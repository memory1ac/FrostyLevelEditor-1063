using Frosty.Core;
using Frosty.Core.Screens;
using Frosty.Core.Viewport;
using Frosty.Hash;
using FrostySdk;
using FrostySdk.IO;
using FrostySdk.Managers;
using MeshSetPlugin.Render;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D11;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace LevelEditorPlugin.Render;

/// <summary>
/// Paints terrain layers into one virtual texture, then ObjectPreset_VirtualTextureOnly
/// sections sample it with WorldPosToVirtualTextureUVs. The fallback shader is not modified.
/// </summary>
public static class VirtualTexturePage
{
    private const float TileMeters = 4.0f;

    private static TerrainRenderable source;
    private static string[] layerColors = [];
    private static Matrix terrainWorld = Matrix.Identity;
    private static bool baked;
    private static ShaderResourceView atlas;
    private static Texture2D atlasTexture;
    private static Vector2 atlasMin;
    private static Vector2 atlasSize = Vector2.One;
    private static ShaderPermutation samplePermutation;
    private static readonly HashSet<MeshRenderSection> VirtualSections = [];
    private static readonly HashSet<MeshRenderSection> BoundSections = [];
    private static readonly HashSet<int> DiscoveredMeshes = [];

    // 整张地形一次画进 4096 贴图，再叠上第一次的驱动着色器编译，会超过 TDR，设备被摘掉。
    // 每帧只提交几块，驱动缓存暖了之后第二次打开才不会崩。
    private const int ChunksPerFrame = 2;
    private static int chunksThisFrame;
    private static bool bakeFailed;
    private static BakeSession session;
    private static byte[] bakeVsBytecode;
    private static byte[] bakePsBytecode;

    public static bool IsReady => atlas != null;

    /// <summary>每帧开始时清零本帧烘焙配额。必须在本帧任何 Apply 之前调用。</summary>
    public static void BeginFrame()
    {
        chunksThisFrame = 0;
    }

    /// <summary>
    /// 在延迟通道开始前推进烘焙。不要在网格 Render/Apply 里调用，
    /// 否则 Flush 会和 G-Buffer 画在同一个 GPU 包里，容易 TDR。
    /// </summary>
    public static void PumpBake(DeviceContext context, RenderCreateState state)
    {
        if (context == null || state == null || bakeFailed || source == null)
            return;
        if (baked)
        {
            GetSamplePermutation(state);
            return;
        }

        try
        {
            if (session == null)
            {
                context.Flush();
                if (!TryStartSession(context, state))
                {
                    bakeFailed = true;
                    return;
                }
                return;
            }

            StepSession(context);
            if (baked)
                GetSamplePermutation(state);
        }
        catch (Exception ex)
        {
            App.Logger.LogError("虚拟贴图烘焙失败: {0}", ex.Message);
            CancelSession();
            bakeFailed = true;
        }
    }

    public static void Bind(TerrainRenderable terrain, string[] colors, Matrix world)
    {
        if (terrain == null)
            return;
        if (source != terrain)
        {
            CancelSession();
            DisposeAtlas();
            samplePermutation = null;
            source = terrain;
            baked = false;
            bakeFailed = false;
            BoundSections.Clear();
            VirtualSections.Clear();
            DiscoveredMeshes.Clear();
        }
        layerColors = colors ?? [];
        terrainWorld = world;
    }

    public static void Collect(MeshRenderMesh mesh, MeshMaterialCollection materials)
    {
        if (mesh == null || materials == null)
            return;

        foreach (MeshRenderLod lod in mesh.LODs)
        {
            foreach (MeshRenderSection section in lod.Sections)
            {
                int id = section.MeshSection.MaterialId;
                if ((uint)id >= (uint)materials.Count)
                    continue;
                if (IsVirtualTextureOnly(materials[id]))
                    VirtualSections.Add(section);
            }
        }
    }

    public static void Apply(DeviceContext context, RenderCreateState state, MeshRenderable mesh)
    {
        if (context == null || state == null || mesh?.TexturedMesh == null)
            return;
        Discover(mesh);
        if (VirtualSections.Count == 0)
            return;

        bool needed = false;
        foreach (MeshRenderLod lod in mesh.TexturedMesh.LODs)
        {
            foreach (MeshRenderSection section in lod.Sections)
            {
                if (VirtualSections.Contains(section) && !BoundSections.Contains(section))
                {
                    needed = true;
                    break;
                }
            }
            if (needed)
                break;
        }
        if (!needed)
            return;

        if (atlas == null)
            return;

        ShaderPermutation permutation = GetSamplePermutation(state);
        if (permutation == null)
            return;

        float sx = atlasSize.X > 0.001f ? 1.0f / atlasSize.X : 0;
        float sz = atlasSize.Y > 0.001f ? 1.0f / atlasSize.Y : 0;
        List<ShaderParameter> parameters =
        [
            new ShaderParameter("WorldPosToVirtualTextureUVs0", ShaderParameterType.Float4, sx, 0, 0, -atlasMin.X * sx),
            new ShaderParameter("WorldPosToVirtualTextureUVs1", ShaderParameterType.Float4, 0, 0, sz, -atlasMin.Y * sz),
            new ShaderParameter("WorldPosToVirtualTextureUVs2", ShaderParameterType.Float4, 0, 0, 0, 0)
        ];
        List<ShaderParameter> textures =
        [
            new ShaderParameter("VirtualTextureAtlas", ShaderParameterType.Tex2d, "Resources/Textures/DefaultLayer.dds")
        ];

        foreach (MeshRenderLod lod in mesh.TexturedMesh.LODs)
        {
            foreach (MeshRenderSection section in lod.Sections)
            {
                if (!VirtualSections.Contains(section) || BoundSections.Contains(section))
                    continue;

                try
                {
                    section.Permutation = permutation;
                    section.IsFallback = false;
                    permutation.AssignParameters(state, parameters, textures, ref section.PixelParameters, ref section.PixelTextures);
                    if (section.PixelTextures.Count > 0)
                    {
                        state.TextureLibrary.UnloadTexture(section.PixelTextures[0]);
                        section.PixelTextures[0] = atlas;
                    }
                    BoundSections.Add(section);
                }
                catch (SharpDXException ex)
                {
                    App.Logger.LogError("虚拟贴图参数上传失败: {0}", ex.Message);
                    bakeFailed = true;
                    return;
                }
            }
        }
    }

    private static bool TryStartSession(DeviceContext context, RenderCreateState state)
    {
        if (!EnsureBakeBytecode())
            return false;

        Device device = context.Device;
        Matrix uploaded = UploadMatrix(terrainWorld);
        if (!TryAtlasBounds(uploaded, out Vector2 min, out Vector2 size))
            return false;

        int resolution = 1024;
        float extent = Math.Max(size.X, size.Y);
        while (resolution < extent * 2.0f && resolution < 4096)
            resolution *= 2;

        BakeSession job = new BakeSession
        {
            Chunks = [.. source.TerrainChunks],
            Uploaded = uploaded,
            Min = min,
            Size = size,
            Resolution = resolution,
            Has = new float[24],
            ColorSrvs = new ShaderResourceView[24],
            State = state,
            Device = device
        };
        session = job;

        job.Vs = new VertexShader(device, bakeVsBytecode);
        job.Ps = new PixelShader(device, bakePsBytecode);
        job.Layout = new InputLayout(device, bakeVsBytecode, new[]
        {
            new InputElement("POSITION", 0, SharpDX.DXGI.Format.R32G32B32_Float, Marshal.OffsetOf<TerrainVertex>(nameof(TerrainVertex.Position)).ToInt32(), 0),
            new InputElement("NORMAL", 0, SharpDX.DXGI.Format.R32G32B32_Float, Marshal.OffsetOf<TerrainVertex>(nameof(TerrainVertex.Normal)).ToInt32(), 0),
            new InputElement("TEXCOORD", 0, SharpDX.DXGI.Format.R32G32_Float, Marshal.OffsetOf<TerrainVertex>(nameof(TerrainVertex.TexCoords)).ToInt32(), 0),
            new InputElement("COLOR", 0, SharpDX.DXGI.Format.R32G32B32_Float, Marshal.OffsetOf<TerrainVertex>(nameof(TerrainVertex.Color)).ToInt32(), 0)
        });
        // 不用 D3DUtils 的采样器缓存。那个缓存被释放后，其他网格的平铺 UV 会变成钳制。
        job.Clamp = new SamplerState(device, new SamplerStateDescription
        {
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            Filter = Filter.MinMagMipLinear,
            ComparisonFunction = Comparison.Always,
            MaximumAnisotropy = 16,
            MinimumLod = 0,
            MaximumLod = 20
        });
        job.Wrap = new SamplerState(device, new SamplerStateDescription
        {
            AddressU = TextureAddressMode.Wrap,
            AddressV = TextureAddressMode.Wrap,
            AddressW = TextureAddressMode.Wrap,
            Filter = Filter.MinMagMipLinear,
            ComparisonFunction = Comparison.Always,
            MaximumAnisotropy = 16,
            MinimumLod = 0,
            MaximumLod = 20
        });
        job.Raster = new RasterizerState(device, new RasterizerStateDescription
        {
            CullMode = CullMode.None,
            FillMode = FillMode.Solid,
            IsDepthClipEnabled = false
        });
        job.Depth = new DepthStencilState(device, new DepthStencilStateDescription
        {
            IsDepthEnabled = false,
            IsStencilEnabled = false
        });
        BlendStateDescription blendDesc = BlendStateDescription.Default();
        blendDesc.RenderTarget[0].IsBlendEnabled = false;
        blendDesc.RenderTarget[0].RenderTargetWriteMask = ColorWriteMaskFlags.All;
        job.Blend = new BlendState(device, blendDesc);

        for (int i = 0; i < layerColors.Length && i < 24; i++)
        {
            if (string.IsNullOrEmpty(layerColors[i]))
                continue;
            EbxAssetEntry entry = App.AssetManager.GetEbxEntry(layerColors[i]);
            if (entry == null)
                continue;
            job.ColorSrvs[i] = state.TextureLibrary.LoadTextureAsset(entry.Guid);
            if (job.ColorSrvs[i] != null)
                job.Has[i] = 1;
        }

        byte[] black = { 0, 0, 0, 255 };
        GCHandle blackPin = GCHandle.Alloc(black, GCHandleType.Pinned);
        try
        {
            job.BlackTex = new Texture2D(device, new Texture2DDescription
            {
                Width = 1,
                Height = 1,
                MipLevels = 1,
                ArraySize = 1,
                Format = SharpDX.DXGI.Format.R8G8B8A8_UNorm,
                SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                Usage = ResourceUsage.Immutable,
                BindFlags = BindFlags.ShaderResource,
                OptionFlags = ResourceOptionFlags.None
            }, new[] { new DataBox(blackPin.AddrOfPinnedObject(), 4, 0) });
        }
        finally
        {
            blackPin.Free();
        }
        job.BlackSrv = new ShaderResourceView(device, job.BlackTex);

        job.Target = new Texture2D(device, new Texture2DDescription
        {
            Width = resolution,
            Height = resolution,
            MipLevels = 1,
            ArraySize = 1,
            Format = SharpDX.DXGI.Format.R8G8B8A8_UNorm,
            SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None
        });
        job.Rtv = new RenderTargetView(device, job.Target);
        job.Srv = new ShaderResourceView(device, job.Target);

        // 着色器创建单独成一个包。第一次驱动编译不会和后面的地形绘制算在同一次超时里。
        context.Flush();
        return true;
    }

    private static bool EnsureBakeBytecode()
    {
        if (bakeVsBytecode != null && bakePsBytecode != null)
            return true;
        if (!File.Exists("Shaders/VirtualTextureBake.hlsl"))
        {
            App.Logger.LogWarning("找不到 Shaders/VirtualTextureBake.hlsl");
            return false;
        }

        string sourceCode = File.ReadAllText("Shaders/VirtualTextureBake.hlsl");
        using CompilationResult vsResult = ShaderBytecode.Compile(sourceCode, "VSMain", "vs_5_0");
        using CompilationResult psResult = ShaderBytecode.Compile(sourceCode, "PSMain", "ps_5_0");
        if (vsResult.Bytecode == null || psResult.Bytecode == null)
        {
            App.Logger.LogError("虚拟贴图烘焙着色器编译失败: {0}", vsResult.Message ?? psResult.Message);
            return false;
        }

        bakeVsBytecode = vsResult.Bytecode;
        bakePsBytecode = psResult.Bytecode;
        return true;
    }

    private static void StepSession(DeviceContext context)
    {
        BakeSession job = session;
        DepthStencilView oldDepth;
        RenderTargetView[] oldTargets = context.OutputMerger.GetRenderTargets(6, out oldDepth);
        SharpDX.Mathematics.Interop.RawViewportF[] oldViewports = context.Rasterizer.GetViewports<SharpDX.Mathematics.Interop.RawViewportF>();
        BlendState oldBlend = context.OutputMerger.GetBlendState(out SharpDX.Mathematics.Interop.RawColor4 blendFactor, out int sampleMask);
        DepthStencilState oldDepthState = context.OutputMerger.GetDepthStencilState(out int stencilRef);
        RasterizerState oldRaster = context.Rasterizer.State;
        VertexShader oldVs = context.VertexShader.Get();
        PixelShader oldPs = context.PixelShader.Get();
        InputLayout oldLayout = context.InputAssembler.InputLayout;
        SharpDX.Direct3D11.Buffer[] oldVsCb = context.VertexShader.GetConstantBuffers(0, 1);
        SharpDX.Direct3D11.Buffer[] oldPsCb = context.PixelShader.GetConstantBuffers(0, 1);
        ShaderResourceView[] oldSrv = context.PixelShader.GetShaderResources(0, 30);
        SamplerState[] oldSamp = context.PixelShader.GetSamplers(0, 2);

        try
        {
            context.OutputMerger.SetRenderTargets(null, job.Rtv);
            context.Rasterizer.SetViewport(new Viewport(0, 0, job.Resolution, job.Resolution));
            if (job.Index == 0)
                context.ClearRenderTargetView(job.Rtv, new Color4(0.45f, 0.45f, 0.45f, 1));
            context.OutputMerger.SetBlendState(job.Blend, default(SharpDX.Mathematics.Interop.RawColor4), -1);
            context.OutputMerger.SetDepthStencilState(job.Depth, 0);
            context.Rasterizer.State = job.Raster;
            context.VertexShader.Set(job.Vs);
            context.PixelShader.Set(job.Ps);
            context.InputAssembler.InputLayout = job.Layout;
            context.PixelShader.SetSampler(0, job.Clamp);
            context.PixelShader.SetSampler(1, job.Wrap);

            var pending = new List<IDisposable>();
            try
            {
                while (chunksThisFrame < ChunksPerFrame && job.Index < job.Chunks.Count)
                {
                    TerrainChunkRenderable chunk = job.Chunks[job.Index++];
                    if (!chunk.TryCreateLayerGroups(job.Device, out ShaderResourceView[] groups, out Texture2D[] owned, out Vector4[] offsets))
                        continue;

                    SharpDX.Direct3D11.Buffer constants = CreateBakeConstants(job.Device, job.Uploaded, job.Min, job.Size, job.Has, offsets);
                    pending.Add(constants);
                    if (groups != null)
                    {
                        foreach (ShaderResourceView srv in groups)
                        {
                            if (srv != null)
                                pending.Add(srv);
                        }
                    }
                    if (owned != null)
                    {
                        foreach (Texture2D tex in owned)
                        {
                            if (tex != null)
                                pending.Add(tex);
                        }
                    }

                    context.VertexShader.SetConstantBuffer(0, constants);
                    context.PixelShader.SetConstantBuffer(0, constants);
                    for (int i = 0; i < 6; i++)
                        context.PixelShader.SetShaderResource(i, i < groups.Length && groups[i] != null ? groups[i] : job.BlackSrv);
                    for (int i = 0; i < 24; i++)
                        context.PixelShader.SetShaderResource(6 + i, job.ColorSrvs[i] ?? job.BlackSrv);
                    chunk.DrawGeometry(context);
                    job.Drawn++;
                    chunksThisFrame++;
                }
            }
            finally
            {
                ShaderResourceView[] unbound = new ShaderResourceView[30];
                context.PixelShader.SetShaderResources(0, unbound);
                context.VertexShader.SetConstantBuffer(0, null);
                context.PixelShader.SetConstantBuffer(0, null);
                if (pending.Count > 0)
                {
                    context.Flush();
                    foreach (IDisposable item in pending)
                        item.Dispose();
                }
            }

            if (job.Index >= job.Chunks.Count)
            {
                context.VertexShader.Set(null);
                context.PixelShader.Set(null);
                context.InputAssembler.InputLayout = null;
                FinishSession();
            }
        }
        finally
        {
            context.OutputMerger.SetRenderTargets(oldDepth, oldTargets);
            if (oldViewports != null && oldViewports.Length > 0)
                context.Rasterizer.SetViewports(oldViewports);
            context.OutputMerger.SetBlendState(oldBlend, blendFactor, sampleMask);
            context.OutputMerger.SetDepthStencilState(oldDepthState, stencilRef);
            context.Rasterizer.State = oldRaster;
            context.VertexShader.Set(oldVs);
            context.PixelShader.Set(oldPs);
            context.InputAssembler.InputLayout = oldLayout;
            if (oldVsCb != null)
                context.VertexShader.SetConstantBuffers(0, oldVsCb);
            if (oldPsCb != null)
                context.PixelShader.SetConstantBuffers(0, oldPsCb);
            if (oldSrv != null)
                context.PixelShader.SetShaderResources(0, oldSrv);
            if (oldSamp != null)
                context.PixelShader.SetSamplers(0, oldSamp);

            oldBlend?.Dispose();
            oldDepthState?.Dispose();
            oldRaster?.Dispose();
            oldVs?.Dispose();
            oldPs?.Dispose();
            oldLayout?.Dispose();
            oldDepth?.Dispose();
            ReleaseAll(oldVsCb);
            ReleaseAll(oldPsCb);
            ReleaseAll(oldSrv);
            ReleaseAll(oldSamp);
            if (oldTargets != null)
            {
                foreach (RenderTargetView target in oldTargets)
                    target?.Dispose();
            }
        }
    }

    private static void FinishSession()
    {
        BakeSession job = session;
        atlasTexture = job.Target;
        atlas = job.Srv;
        atlasMin = job.Min;
        atlasSize = job.Size;
        job.Target = null;
        job.Srv = null;
        int resolution = job.Resolution;
        int drawn = job.Drawn;
        CancelSession();
        baked = true;
        App.Logger.Log("虚拟贴图: 已烘焙 {0}x{0}，绘制 {1} 块地形", resolution, drawn);
    }

    private static void CancelSession()
    {
        if (session == null)
            return;

        BakeSession job = session;
        session = null;
        job.Rtv?.Dispose();
        job.Srv?.Dispose();
        job.Target?.Dispose();
        job.Vs?.Dispose();
        job.Ps?.Dispose();
        job.Layout?.Dispose();
        job.Clamp?.Dispose();
        job.Wrap?.Dispose();
        job.Raster?.Dispose();
        job.Depth?.Dispose();
        job.Blend?.Dispose();
        job.BlackSrv?.Dispose();
        job.BlackTex?.Dispose();
        if (job.ColorSrvs != null && job.State != null)
        {
            for (int i = 0; i < job.ColorSrvs.Length; i++)
            {
                if (job.ColorSrvs[i] != null)
                    job.State.TextureLibrary.UnloadTexture(job.ColorSrvs[i]);
            }
        }
    }

    private sealed class BakeSession
    {
        public List<TerrainChunkRenderable> Chunks;
        public int Index;
        public int Drawn;
        public Device Device;
        public RenderCreateState State;
        public Matrix Uploaded;
        public Vector2 Min;
        public Vector2 Size;
        public int Resolution;
        public float[] Has;
        public ShaderResourceView[] ColorSrvs;
        public Texture2D Target;
        public RenderTargetView Rtv;
        public ShaderResourceView Srv;
        public VertexShader Vs;
        public PixelShader Ps;
        public InputLayout Layout;
        public SamplerState Clamp;
        public SamplerState Wrap;
        public RasterizerState Raster;
        public DepthStencilState Depth;
        public BlendState Blend;
        public Texture2D BlackTex;
        public ShaderResourceView BlackSrv;
    }

    private static void ReleaseAll<T>(T[] items) where T : IDisposable
    {
        if (items == null)
            return;
        foreach (T item in items)
            item?.Dispose();
    }

    private static SharpDX.Direct3D11.Buffer CreateBakeConstants(Device device, Matrix uploaded, Vector2 min, Vector2 size, float[] has, Vector4[] offsets)
    {
        int bytes = 64 + (2 + 6 + 24) * 16;
        using DataStream stream = new DataStream(bytes, true, true);
        stream.Write(uploaded);
        stream.Write(new Vector4(min.X, min.Y, size.X, size.Y));
        stream.Write(new Vector4(TileMeters, TileMeters, 0, 0));
        for (int i = 0; i < 6; i++)
            stream.Write(new Vector4(has[i * 4], has[i * 4 + 1], has[i * 4 + 2], has[i * 4 + 3]));
        for (int i = 0; i < 24; i++)
            stream.Write(i < offsets.Length ? offsets[i] : new Vector4(0, 0, 1, 1));
        stream.Position = 0;
        return new SharpDX.Direct3D11.Buffer(device, stream, new BufferDescription
        {
            SizeInBytes = bytes,
            BindFlags = BindFlags.ConstantBuffer,
            Usage = ResourceUsage.Immutable,
            CpuAccessFlags = CpuAccessFlags.None
        });
    }

    private static Matrix UploadMatrix(Matrix entityWorld)
    {
        Matrix transposed = entityWorld;
        transposed.Transpose();
        return Matrix.Scaling(-1, 1, 1) * transposed;
    }

    private static bool TryAtlasBounds(Matrix uploaded, out Vector2 min, out Vector2 size)
    {
        min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        Matrix shaderMatrix = Matrix.Transpose(uploaded);
        bool any = false;
        foreach (TerrainChunkRenderable chunk in source.TerrainChunks)
        {
            BoundingBox box = chunk.Bounds.GetBoundingBox();
            Vector3[] corners =
            [
                new Vector3(box.Minimum.X, box.Minimum.Y, box.Minimum.Z),
                new Vector3(box.Maximum.X, box.Minimum.Y, box.Minimum.Z),
                new Vector3(box.Minimum.X, box.Minimum.Y, box.Maximum.Z),
                new Vector3(box.Maximum.X, box.Minimum.Y, box.Maximum.Z)
            ];
            foreach (Vector3 corner in corners)
            {
                Vector3 world = Vector3.TransformCoordinate(corner, shaderMatrix);
                min.X = Math.Min(min.X, world.X);
                min.Y = Math.Min(min.Y, world.Z);
                max.X = Math.Max(max.X, world.X);
                max.Y = Math.Max(max.Y, world.Z);
                any = true;
            }
        }
        size = new Vector2(Math.Max(0.001f, max.X - min.X), Math.Max(0.001f, max.Y - min.Y));
        return any;
    }

    private static ShaderPermutation GetSamplePermutation(RenderCreateState state)
    {
        if (samplePermutation != null)
            return samplePermutation;

        GeometryDeclarationDesc decl = CreateFallbackLayout();
        ShaderPermutation permutation = state.ShaderLibrary.GetUserShader("VirtualTextureSample", decl);
        if (permutation == null || !LoadSampleShaders(state.Device, permutation))
        {
            App.Logger.LogWarning("VirtualTextureSample 着色器没有对上顶点格式");
            return null;
        }
        samplePermutation = permutation;
        return samplePermutation;
    }

    /// <summary>
    /// Stock Frosty leaves UInt4 as DXGI Unknown, so LoadShaders throws E_INVALIDARG.
    /// The bytecode is already in Shaders/VirtualTextureSample.bin; only the input layout is created here.
    /// </summary>
    private static bool LoadSampleShaders(Device device, ShaderPermutation permutation)
    {
        if (permutation.vertexShader != null && permutation.inputLayout != null)
            return true;

        permutation.vertexShader?.Dispose();
        permutation.pixelShader?.Dispose();
        permutation.inputLayout?.Dispose();
        permutation.vertexShader = null;
        permutation.pixelShader = null;
        permutation.inputLayout = null;

        string shaderPath = "Shaders/VirtualTextureSample.bin";
        if (!File.Exists(shaderPath))
            return false;

        byte[] vsBytecode = null;
        byte[] psBytecode = null;
        int nameHash = Fnv1.HashString(permutation.Name ?? "");
        using (NativeReader reader = new NativeReader(new FileStream(shaderPath, FileMode.Open, FileAccess.Read)))
        {
            reader.ReadLong();
            int permutationCount = reader.ReadInt();
            for (int i = 0; i < permutationCount; i++)
            {
                int permutationHash = reader.ReadInt();
                long offset = reader.ReadLong();
                if (permutationHash != nameHash)
                    continue;

                reader.Position = offset;
                int vsSize = reader.ReadInt();
                int psSize = reader.ReadInt();
                if (vsSize <= 0 || psSize <= 0)
                    return false;
                vsBytecode = reader.ReadBytes(vsSize);
                psBytecode = reader.ReadBytes(psSize);
                break;
            }
        }
        if (vsBytecode == null || psBytecode == null)
            return false;

        GeometryDeclarationDesc geomDecl = permutation.GeometryDeclaration;
        List<InputElement> elements = new List<InputElement>(geomDecl.ElementCount);
        for (int i = 0; i < geomDecl.ElementCount; i++)
        {
            if (!TryVertexFormat(geomDecl.Elements[i].Format, out SharpDX.DXGI.Format format))
                return false;

            int index = 0;
            string semanticName = geomDecl.Elements[i].Usage.ToString().ToUpper();
            if (geomDecl.Elements[i].Usage >= VertexElementUsage.TexCoord0 && geomDecl.Elements[i].Usage <= VertexElementUsage.TexCoord7)
            {
                index = (int)(geomDecl.Elements[i].Usage - VertexElementUsage.TexCoord0);
                semanticName = semanticName.Remove(semanticName.Length - 1, 1);
            }
            if (geomDecl.Elements[i].Usage >= VertexElementUsage.Color0 && geomDecl.Elements[i].Usage <= VertexElementUsage.Color1)
            {
                index = (int)(geomDecl.Elements[i].Usage - VertexElementUsage.Color0);
                semanticName = semanticName.Remove(semanticName.Length - 1, 1);
            }
            if (geomDecl.Elements[i].Usage == VertexElementUsage.BoneIndices2 || geomDecl.Elements[i].Usage == VertexElementUsage.BoneWeights2)
            {
                index = 2;
                semanticName = semanticName.Remove(semanticName.Length - 1, 1);
            }

            elements.Add(new InputElement(semanticName, index, format, geomDecl.Elements[i].Offset, geomDecl.Elements[i].StreamIndex, InputClassification.PerVertexData, 0));
        }

        VertexShader vertexShader = null;
        PixelShader pixelShader = null;
        InputLayout inputLayout = null;
        try
        {
            vertexShader = new VertexShader(device, vsBytecode);
            inputLayout = new InputLayout(device, vsBytecode, elements.ToArray());
            pixelShader = new PixelShader(device, psBytecode);
        }
        catch (SharpDXException ex)
        {
            inputLayout?.Dispose();
            pixelShader?.Dispose();
            vertexShader?.Dispose();
            App.Logger.LogError("VirtualTextureSample 输入布局失败: {0}", ex.Message);
            return false;
        }

        permutation.vertexShader = vertexShader;
        permutation.pixelShader = pixelShader;
        permutation.inputLayout = inputLayout;
        foreach (SamplerStateDescription desc in permutation.PixelSamplerDescs)
            permutation.pixelSamplers.Add(D3DUtils.CreateSamplerState(desc));
        if (permutation.IsSkinned)
            permutation.boneBuffer = new BoneBuffer(device, 100);
        return true;
    }

    private static bool TryVertexFormat(VertexElementFormat source, out SharpDX.DXGI.Format format)
    {
        switch (source)
        {
            case VertexElementFormat.Float: format = SharpDX.DXGI.Format.R32_Float; return true;
            case VertexElementFormat.Float2: format = SharpDX.DXGI.Format.R32G32_Float; return true;
            case VertexElementFormat.Float3: format = SharpDX.DXGI.Format.R32G32B32_Float; return true;
            case VertexElementFormat.Float4: format = SharpDX.DXGI.Format.R32G32B32A32_Float; return true;
            case VertexElementFormat.Half: format = SharpDX.DXGI.Format.R16_Float; return true;
            case VertexElementFormat.Half2: format = SharpDX.DXGI.Format.R16G16_Float; return true;
            case VertexElementFormat.Half3: format = SharpDX.DXGI.Format.R16G16B16A16_Float; return true;
            case VertexElementFormat.Half4: format = SharpDX.DXGI.Format.R16G16B16A16_Float; return true;
            case VertexElementFormat.UByteN: format = SharpDX.DXGI.Format.R8_UInt; return true;
            case VertexElementFormat.Byte4: format = SharpDX.DXGI.Format.R8G8B8A8_SInt; return true;
            case VertexElementFormat.Byte4N: format = SharpDX.DXGI.Format.R8G8B8A8_SNorm; return true;
            case VertexElementFormat.UByte4N: format = SharpDX.DXGI.Format.R8G8B8A8_UNorm; return true;
            case VertexElementFormat.UByte4: format = SharpDX.DXGI.Format.R8G8B8A8_UInt; return true;
            case VertexElementFormat.Short2N: format = SharpDX.DXGI.Format.R16G16_SNorm; return true;
            case VertexElementFormat.UShort4: format = SharpDX.DXGI.Format.R16G16B16A16_UInt; return true;
            case VertexElementFormat.Short4: format = SharpDX.DXGI.Format.R16G16B16A16_SInt; return true;
            case VertexElementFormat.Short4N: format = SharpDX.DXGI.Format.R16G16B16A16_SNorm; return true;
            case VertexElementFormat.UInt: format = SharpDX.DXGI.Format.R32_UInt; return true;
            case VertexElementFormat.UInt2: format = SharpDX.DXGI.Format.R32G32_UInt; return true;
            case VertexElementFormat.UInt3: format = SharpDX.DXGI.Format.R32G32B32_UInt; return true;
            case VertexElementFormat.UInt4: format = SharpDX.DXGI.Format.R32G32B32A32_UInt; return true;
            default:
                format = SharpDX.DXGI.Format.Unknown;
                return false;
        }
    }

    private static GeometryDeclarationDesc CreateFallbackLayout()
    {
        GeometryDeclarationDesc decl = new GeometryDeclarationDesc
        {
            Elements = new GeometryDeclarationDesc.Element[GeometryDeclarationDesc.MaxElements],
            Streams = new GeometryDeclarationDesc.Stream[GeometryDeclarationDesc.MaxStreams]
        };
        for (int i = 0; i < GeometryDeclarationDesc.MaxElements; i++)
            decl.Elements[i].Offset = 0xFF;

        (VertexElementUsage usage, VertexElementFormat format)[] layout =
        [
            (VertexElementUsage.Pos, VertexElementFormat.Float3),
            (VertexElementUsage.Normal, VertexElementFormat.Float4),
            (VertexElementUsage.Tangent, VertexElementFormat.Float4),
            (VertexElementUsage.Binormal, VertexElementFormat.Float4),
            (VertexElementUsage.TexCoord0, VertexElementFormat.Float2),
            (VertexElementUsage.TexCoord1, VertexElementFormat.Float2),
            (VertexElementUsage.TexCoord2, VertexElementFormat.Float2),
            (VertexElementUsage.Color0, VertexElementFormat.Float4),
            (VertexElementUsage.Color1, VertexElementFormat.Float4),
            (VertexElementUsage.BoneIndices, VertexElementFormat.UInt4),
            (VertexElementUsage.BoneIndices2, VertexElementFormat.UInt4),
            (VertexElementUsage.BoneWeights, VertexElementFormat.Float4),
            (VertexElementUsage.BoneWeights2, VertexElementFormat.Float4),
            (VertexElementUsage.TangentSpace, VertexElementFormat.UInt)
        ];

        int index = 0;
        foreach ((VertexElementUsage usage, VertexElementFormat format) item in layout)
        {
            GeometryDeclarationDesc.Element elem = new GeometryDeclarationDesc.Element
            {
                Usage = item.usage,
                Format = item.format,
                StreamIndex = 0
            };
            elem.Offset = decl.Streams[0].VertexStride;
            decl.Elements[index++] = elem;
            decl.ElementCount++;
            decl.Streams[0].Classification = VertexElementClassification.PerVertex;
            decl.Streams[0].VertexStride += (byte)elem.Size;
            decl.StreamCount = 1;
        }
        return decl;
    }

    private static void Discover(MeshRenderable mesh)
    {
        if (mesh?.Materials == null || mesh.TexturedMesh == null)
            return;
        if (!DiscoveredMeshes.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(mesh)))
            return;

        foreach (MeshMaterial material in mesh.Materials)
        {
            try
            {
                Guid guid = material.Shader.External.FileGuid;
                if (guid == Guid.Empty)
                    continue;
                EbxAssetEntry shader = App.AssetManager.GetEbxEntry(guid);
                if (shader != null)
                    TerrainAlbedoResolver.ResolveShader(shader.Name);
            }
            catch
            {
            }
        }
        Collect(mesh.TexturedMesh, mesh.Materials);
    }

    private static bool IsVirtualTextureOnly(MeshMaterial material)
    {
        try
        {
            if (material == null)
                return false;
            Guid guid = material.Shader.External.FileGuid;
            if (guid == Guid.Empty)
                return false;
            EbxAssetEntry shader = App.AssetManager.GetEbxEntry(guid);
            return shader != null && (shader.Name.IndexOf("VirtualTextureOnly", StringComparison.OrdinalIgnoreCase) >= 0
                || TerrainAlbedoResolver.IsVirtualTextureShader(shader.Name));
        }
        catch
        {
            return false;
        }
    }

    private static void DisposeAtlas()
    {
        atlas?.Dispose();
        atlasTexture?.Dispose();
        atlas = null;
        atlasTexture = null;
    }
}
