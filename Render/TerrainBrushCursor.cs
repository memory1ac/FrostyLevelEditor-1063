using Frosty.Core;
using LevelEditorPlugin.BF1Core.Utils;
using LevelEditorPlugin.Entities;
using LevelEditorPlugin.Render.Proxies;
using LevelEditorPlugin.Resources;
using SharpDX;
using SharpDX.D3DCompiler;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using D3D11 = SharpDX.Direct3D11;

namespace LevelEditorPlugin.Render;

/// <summary>
/// 地形笔刷光标：把笔刷的作用范围“投影”到地形表面上的一个圈。
///
/// 笔刷本身是在地形局部空间里按半径/衰减改高度的（见 <see cref="TerrainBrush"/>），
/// 所以这里也在地形局部空间里采样高度场，把圈的每一段都贴到真实地形高度上，
/// 再按 <see cref="TerrainBrushMode"/> 上色（抬升＝琥珀色，压低＝青色）。
/// 圈的粗细按摄像机距离补偿，看起来始终是差不多 2 像素的一条线。
///
/// 着色器直接编译在 DLL 里（见 <see cref="ShaderSource"/>），不依赖 Content 目录，
/// 因为插件发布只有单个 DLL。
/// </summary>
public sealed class TerrainBrushCursor : IDisposable
{
    /// <summary>一圈的分段数。128 段在放大到很近时也不会看出棱角。</summary>
    private const int Segments = 128;

    /// <summary>最多两个圈（外圈＝笔刷半径，内圈＝衰减起点），每段两个三角形共 6 个顶点。</summary>
    private const int MaxVertices = Segments * 6 * 2;

    /// <summary>
    /// 关卡里的模型渲染会额外叠一个 X 镜像（见 LevelEditorScreen.RenderMeshes），
    /// 地形顶点也得走同一条链才不会左右反掉，所以这里照 HavokOverlay 的写法把镜像乘在世界变换之后。
    /// </summary>
    private static readonly Matrix Mirror = Matrix.Scaling(-1, 1, 1);

    /// <summary>圈的半宽（相对笔刷半径）。0.015 → 半径 2m 时线宽约 6cm，10m 时约 30cm。</summary>
    private const float RingWidthRatio = 0.015f;

    /// <summary>线宽的硬上限（相对半径），防止参数异常时又变成实心色块。</summary>
    private const float MaxWidthRatio = 0.05f;

    /// <summary>
    /// 远处至少保留约 1 像素宽的近似系数（世界单位/米），只用来兜住"远处细到看不见"。
    /// 远小于之前的 0.0012，不会再把圈撑粗。
    /// </summary>
    private const float PixelWorldAtDistance = 0.0004f;

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorVertex
    {
        public Vector3 Position;
        public Vector4 Color;
        /// <summary>-1 = 圈的内边，+1 = 圈的外边，着色器据此做软边。</summary>
        public float Edge;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Constants
    {
        public Matrix ViewProj;
    }

    /// <summary>
    /// 顶点只带位置/颜色/软边坐标，全部计算在 CPU 侧完成，所以着色器非常短。
    /// 乘法方向和关卡的其它叠加层保持一致：mul(v, ViewProj) 配 Transpose(viewProj)。
    ///
    /// 注意：这段着色器必须保持纯 ASCII。SharpDX 的 ShaderBytecode.Compile(string) 按
    /// ANSI 封送源码，字符串里一旦有中文（哪怕是注释），尾部会被截断，报
    /// "error X3000: syntax error: unexpected end of file"。
    /// </summary>
    private const string ShaderSource = @"
cbuffer Constants : register(b0)
{
    float4x4 ViewProj;
};

struct VSIn
{
    float3 Pos : POSITION;
    float4 Color : COLOR;
    float Edge : TEXCOORD0;
};

struct PSIn
{
    float4 Pos : SV_POSITION;
    float4 Color : COLOR;
    float Edge : TEXCOORD0;
};

PSIn VSMain(VSIn i)
{
    PSIn o;
    o.Pos = mul(float4(i.Pos, 1), ViewProj);
    o.Color = i.Color;
    o.Edge = i.Edge;
    return o;
}

float4 PSMain(PSIn i) : SV_TARGET
{
    // solid in the middle, fading to both sides: an anti-aliased thick line
    float core = saturate(1.0 - abs(i.Edge));
    float alpha = smoothstep(0.0, 0.45, core) * i.Color.a;
    return float4(i.Color.rgb, alpha);
}";

    private static readonly int VertexStride = Utilities.SizeOf<CursorVertex>();

    /// <summary>抬升色（和选中描边的琥珀色同色系）。</summary>
    private static readonly Vector4 RaiseColor = new(1.0f, 0.66f, 0.22f, 1.0f);

    /// <summary>压低色，冷色便于和抬升区分。</summary>
    private static readonly Vector4 LowerColor = new(0.28f, 0.86f, 1.0f, 1.0f);

    private readonly D3D11.VertexShader vertexShader;
    private readonly D3D11.PixelShader pixelShader;
    private readonly D3D11.InputLayout inputLayout;
    private readonly D3D11.Buffer constantBuffer;
    private readonly D3D11.Buffer vertexBuffer;
    private readonly D3D11.BlendState blendState;
    private readonly D3D11.DepthStencilState depthState;
    private readonly D3D11.RasterizerState rasterizerState;

    private readonly CursorVertex[] vertices = new CursorVertex[MaxVertices];
    private readonly Vector3[] innerScratch = new Vector3[Segments + 1];
    private readonly Vector3[] outerScratch = new Vector3[Segments + 1];
    private readonly Dictionary<TerrainChunkRenderable, HeightCache> heightCache = [];
    private readonly List<TerrainChunkRenderable> chunkSnapshot = [];
    private int vertexCount;
    private int frameStamp;

    private bool hasTarget;
    private Vector3 worldCenter;
    private Matrix localToWorld = Matrix.Identity;

    /// <summary>最近一次构建圈时是否成功贴合地形（否则退化成平面圆，仍然给出范围提示）。</summary>
    public bool IsProjected { get; private set; }

    public bool IsDisposed { get; private set; }

    public TerrainBrushCursor(D3D11.Device device)
    {
        EnsureAsciiShader();

        using (CompilationResult vs = ShaderBytecode.Compile(ShaderSource, "VSMain", "vs_5_0"))
        using (CompilationResult ps = ShaderBytecode.Compile(ShaderSource, "PSMain", "ps_5_0"))
        {
            if (vs.HasErrors || vs.Bytecode == null)
                throw new InvalidOperationException($"地形笔刷光标的顶点着色器编译失败：{vs.Message}");
            if (ps.HasErrors || ps.Bytecode == null)
                throw new InvalidOperationException($"地形笔刷光标的像素着色器编译失败：{ps.Message}");

            vertexShader = new D3D11.VertexShader(device, vs.Bytecode);
            pixelShader = new D3D11.PixelShader(device, ps.Bytecode);
            inputLayout = new D3D11.InputLayout(device, ShaderSignature.GetInputSignature(vs.Bytecode), new[]
            {
                new D3D11.InputElement("POSITION", 0, SharpDX.DXGI.Format.R32G32B32_Float, 0, 0),
                new D3D11.InputElement("COLOR", 0, SharpDX.DXGI.Format.R32G32B32A32_Float, 12, 0),
                new D3D11.InputElement("TEXCOORD", 0, SharpDX.DXGI.Format.R32_Float, 28, 0),
            });
        }

        constantBuffer = new D3D11.Buffer(device, Utilities.SizeOf<Constants>(), D3D11.ResourceUsage.Default,
            D3D11.BindFlags.ConstantBuffer, D3D11.CpuAccessFlags.None, D3D11.ResourceOptionFlags.None, 0);

        vertexBuffer = new D3D11.Buffer(device, new D3D11.BufferDescription
        {
            SizeInBytes = VertexStride * MaxVertices,
            Usage = D3D11.ResourceUsage.Dynamic,
            BindFlags = D3D11.BindFlags.VertexBuffer,
            CpuAccessFlags = D3D11.CpuAccessFlags.Write,
            OptionFlags = D3D11.ResourceOptionFlags.None,
            StructureByteStride = 0
        });

        D3D11.BlendStateDescription blendDesc = new();
        blendDesc.RenderTarget[0] = new D3D11.RenderTargetBlendDescription
        {
            IsBlendEnabled = true,
            SourceBlend = D3D11.BlendOption.SourceAlpha,
            DestinationBlend = D3D11.BlendOption.InverseSourceAlpha,
            BlendOperation = D3D11.BlendOperation.Add,
            SourceAlphaBlend = D3D11.BlendOption.One,
            DestinationAlphaBlend = D3D11.BlendOption.InverseSourceAlpha,
            AlphaBlendOperation = D3D11.BlendOperation.Add,
            RenderTargetWriteMask = D3D11.ColorWriteMaskFlags.All
        };
        blendState = new D3D11.BlendState(device, blendDesc);

        // 只读深度 + 负的深度偏移：圈贴在抬升过的地形表面上，既能被前面的山挡住，也不会和地形打架
        depthState = new D3D11.DepthStencilState(device, new D3D11.DepthStencilStateDescription
        {
            IsDepthEnabled = true,
            DepthWriteMask = D3D11.DepthWriteMask.Zero,
            DepthComparison = D3D11.Comparison.LessEqual
        });

        rasterizerState = new D3D11.RasterizerState(device, new D3D11.RasterizerStateDescription
        {
            CullMode = D3D11.CullMode.None,
            FillMode = D3D11.FillMode.Solid,
            IsDepthClipEnabled = true,
            IsScissorEnabled = false,
            DepthBias = -200,
            SlopeScaledDepthBias = -1.0f,
            DepthBiasClamp = 0.0f
        });
    }

    /// <summary>
    /// SharpDX 的 Compile(string) 按 ANSI 封送源码，非 ASCII 字符会让源码在尾部被截断
    /// （表现为 "unexpected end of file"）。这里提前把它变成一条能看懂的报错。
    /// </summary>
    private static void EnsureAsciiShader()
    {
        for (int i = 0; i < ShaderSource.Length; i++)
        {
            if (ShaderSource[i] > 127)
            {
                throw new InvalidOperationException(
                    $"地形笔刷光标的着色器里包含非 ASCII 字符（索引 {i}），SharpDX 会截断源码，请改成英文注释。");
            }
        }
    }

    /// <summary>当前没有可画的圈（鼠标不在视口 / 没打到地形 / 不在编辑模式）。</summary>
    public void Clear()
    {
        hasTarget = false;
        vertexCount = 0;
        worldCenter = Vector3.Zero;
    }

    /// <summary>
    /// 用一次世界空间落点重建圈。每帧调用一次即可（高度场每帧都要重读，地形被笔刷改过要及时反映）。
    /// </summary>
    public void Update(TerrainEntity terrain, Vector3 inWorldCenter, Vector3 eyePosition, float radius, float feather)
    {
        if (IsDisposed || terrain?.Terrain?.TerrainData == null || radius <= 0.0f)
        {
            Clear();
            return;
        }

        worldCenter = inWorldCenter;
        hasTarget = true;
        vertexCount = 0;
        frameStamp++;
        IsProjected = false;

        // 地形流式加载会不断换 chunk 对象，缓存别无限长
        if (heightCache.Count > 512)
            heightCache.Clear();

        // 顶点都在地形局部空间里构建，绘制时再乘 terrainTransform * Mirror * viewProj，
        // 和 HavokOverlay 对待模型的方式一致（圈和地形本身走同一条变换链）。
        localToWorld = terrain.GetTransform();
        Matrix worldToLocal = Matrix.Invert(localToWorld);

        // 半径是地形局部空间的单位（和 TerrainBrush 一致），落点也换算到局部空间
        Vector3 localCenter = Vector3.TransformCoordinate(inWorldCenter, worldToLocal);

        // 线宽按半径比例给：半径 2m~10m 时圈的粗细看起来始终一致，
        // 也不会因为摄像机拉远就膨胀成一片实心色块（之前按距离线性放大就是这个毛病）。
        float distance = (inWorldCenter - eyePosition).Length();
        float localScale = GetLocalScale(localToWorld);
        float halfWidth = Math.Max(radius * RingWidthRatio, distance * PixelWorldAtDistance / localScale);
        halfWidth = Math.Min(halfWidth, radius * MaxWidthRatio);

        // 贴地抬起量只要压住深度抖动即可，跟着线宽走，不要太大
        float localOffset = Math.Min(halfWidth * 0.5f, Math.Max(0.02f / localScale, distance * 0.0002f / localScale));

        chunkSnapshot.Clear();
        chunkSnapshot.AddRange(terrain.Terrain.TerrainData.TerrainChunks);

        Vector4 color = TerrainEditState.BrushMode == TerrainBrushMode.Lower ? LowerColor : RaiseColor;

        // 外圈＝笔刷半径；内圈＝衰减起点（feather 越大内圈越小，直观显示"核心区"和"渐变区"）
        AppendRing(localCenter, radius, halfWidth, localOffset, color);

        float featherRadius = radius * (1.0f - MathUtil.Clamp(feather, 0.0f, 0.95f));
        if (featherRadius > radius * 0.05f)
        {
            Vector4 innerColor = color;
            innerColor.W *= 0.5f;
            AppendRing(localCenter, featherRadius, halfWidth * 0.5f, localOffset * 0.6f, innerColor);
        }

        chunkSnapshot.Clear();
    }

    /// <summary>地形变换的横向缩放（大多数地形是 1，这里只是防止非等比缩放时圈画错大小）。</summary>
    private static float GetLocalScale(Matrix transform)
    {
        float sx = new Vector3(transform.M11, transform.M12, transform.M13).Length();
        float sz = new Vector3(transform.M31, transform.M32, transform.M33).Length();
        float scale = (sx + sz) * 0.5f;
        return scale > 1e-4f ? scale : 1.0f;
    }

    /// <summary>
    /// 画到当前绑定的渲染目标上（调用方已经绑定场景深度 + alpha 混合的 RTV）。
    /// 会完整保存/还原自己改动的管线状态。
    /// </summary>
    public void Draw(D3D11.DeviceContext context, Matrix viewProj, D3D11.DepthStencilView depthTarget)
    {
        if (IsDisposed || !hasTarget || vertexCount < 6)
            return;

        D3D11.RenderTargetView[] previousTargets = context.OutputMerger.GetRenderTargets(6, out D3D11.DepthStencilView previousDepth);
        SaveState save = SaveState.Capture(context);

        try
        {
            context.OutputMerger.SetRenderTargets(depthTarget, previousTargets);
            context.OutputMerger.SetBlendState(blendState);
            context.OutputMerger.SetDepthStencilState(depthState);
            context.Rasterizer.State = rasterizerState;

            context.InputAssembler.InputLayout = inputLayout;
            context.InputAssembler.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;

            // 顶点表每帧重写：Dynamic + WriteDiscard，不需要额外的同步
            DataBox box = context.MapSubresource(vertexBuffer, 0, D3D11.MapMode.WriteDiscard, D3D11.MapFlags.None);
            Utilities.Write(box.DataPointer, vertices, 0, vertexCount);
            context.UnmapSubresource(vertexBuffer, 0);

            context.InputAssembler.SetVertexBuffers(0, new D3D11.VertexBufferBinding(vertexBuffer, VertexStride, 0));

            Constants constants = new()
            {
                ViewProj = Matrix.Transpose(localToWorld * Mirror * viewProj)
            };
            context.UpdateSubresource(ref constants, constantBuffer);

            context.VertexShader.Set(vertexShader);
            context.VertexShader.SetConstantBuffer(0, constantBuffer);
            context.PixelShader.Set(pixelShader);
            context.PixelShader.SetConstantBuffer(0, constantBuffer);

            context.Draw(vertexCount, 0);
        }
        finally
        {
            save.Restore(context);
            context.OutputMerger.SetRenderTargets(previousDepth, previousTargets);
        }
    }

    #region -- 几何构建 --

    private void AppendRing(Vector3 localCenter, float radius, float halfWidth, float localOffset, Vector4 color)
    {
        // 先把整圈上的采样点算出来（相邻段共用），再连成三角带
        const int samples = Segments + 1;
        Vector3[] innerPoints = innerScratch;
        Vector3[] outerPoints = outerScratch;

        for (int i = 0; i < samples; i++)
        {
            float angle = i * MathUtil.TwoPi / Segments;
            float cos = (float)Math.Cos(angle);
            float sin = (float)Math.Sin(angle);

            innerPoints[i] = SampleSurface(localCenter, cos, sin, Math.Max(radius - halfWidth, 0.0f), localOffset);
            outerPoints[i] = SampleSurface(localCenter, cos, sin, radius + halfWidth, localOffset);
        }

        for (int i = 0; i < Segments; i++)
        {
            if (vertexCount + 6 > MaxVertices)
                return;

            Vector3 in0 = innerPoints[i];
            Vector3 out0 = outerPoints[i];
            Vector3 in1 = innerPoints[i + 1];
            Vector3 out1 = outerPoints[i + 1];

            vertices[vertexCount++] = new CursorVertex { Position = in0, Color = color, Edge = -1.0f };
            vertices[vertexCount++] = new CursorVertex { Position = out0, Color = color, Edge = 1.0f };
            vertices[vertexCount++] = new CursorVertex { Position = in1, Color = color, Edge = -1.0f };

            vertices[vertexCount++] = new CursorVertex { Position = in1, Color = color, Edge = -1.0f };
            vertices[vertexCount++] = new CursorVertex { Position = out0, Color = color, Edge = 1.0f };
            vertices[vertexCount++] = new CursorVertex { Position = out1, Color = color, Edge = 1.0f };
        }
    }

    /// <summary>在地形局部空间里取 (x, z) 处的高度，并沿局部 Y 抬起一点避免和地形重叠闪烁。</summary>
    private Vector3 SampleSurface(Vector3 localCenter, float cos, float sin, float ringRadius, float localOffset)
    {
        float x = localCenter.X + cos * ringRadius;
        float z = localCenter.Z + sin * ringRadius;

        float y;
        if (TrySampleLocalHeight(x, z, out float sampled))
        {
            y = sampled;
            IsProjected = true;
        }
        else
        {
            // 圈落到地形边缘外面：退回命中点的高度，至少还是个平面圆
            y = localCenter.Y;
        }

        return new Vector3(x, y + localOffset, z);
    }

    /// <summary>在局部空间 (x, z) 处双线性采样地形高度；跨 chunk 时取最细的那块。</summary>
    private bool TrySampleLocalHeight(float x, float z, out float height)
    {
        height = 0.0f;
        bool found = false;
        int bestLevel = -1;

        foreach (TerrainChunkRenderable chunk in chunkSnapshot)
        {
            if (!TerrainBrush.TryGetSampleCoords(chunk, x, z, out float sx, out float sy))
                continue;

            if (chunk.NodeId.level <= bestLevel)
                continue;

            float[] heights = GetHeights(chunk);
            if (heights == null)
                continue;

            height = TerrainBrush.SampleBilinearHeights(heights, chunk.Dim, sx, sy);
            bestLevel = chunk.NodeId.level;
            found = true;
        }

        return found;
    }

    /// <summary>
    /// 把 chunk 的顶点高度折叠成 dim×dim 的样本高度表（同一处可能有多份顶点）。
    /// 数组按 chunk 缓存并复用，每帧只在第一次访问时重填，避免拖笔刷时产生 GC 压力。
    /// </summary>
    private float[] GetHeights(TerrainChunkRenderable chunk)
    {
        int dim = chunk.Dim;
        var verts = chunk.TerrainVertices;
        if (dim <= 0 || verts == null || verts.Count == 0)
            return null;

        if (!heightCache.TryGetValue(chunk, out HeightCache cache) || cache.Heights.Length != dim * dim)
        {
            cache = new HeightCache(dim);
            heightCache[chunk] = cache;
        }

        if (cache.Frame == frameStamp)
            return cache.Heights;

        cache.Frame = frameStamp;

        float[] heights = cache.Heights;
        int[] counts = cache.Counts;
        Array.Clear(heights, 0, heights.Length);
        Array.Clear(counts, 0, counts.Length);

        for (int i = 0; i < verts.Count; i++)
        {
            int index = TerrainVertexPicker.GetSampleIndexForVertex(i, dim);
            if (index < 0 || index >= heights.Length)
                continue;

            heights[index] += verts[i].Position.Y;
            counts[index]++;
        }

        for (int i = 0; i < heights.Length; i++)
        {
            if (counts[i] > 0)
                heights[i] /= counts[i];
        }

        return heights;
    }

    private sealed class HeightCache
    {
        public readonly float[] Heights;
        public readonly int[] Counts;
        public int Frame = -1;

        public HeightCache(int dim)
        {
            Heights = new float[dim * dim];
            Counts = new int[dim * dim];
        }
    }

    #endregion

    /// <summary>
    /// 保存/还原会被逐帧改动的管线状态。着色器对象读不回来，还原时解绑成 null：
    /// 紧跟在后面的绘制都会自己重新绑定（和 HavokOverlay 的做法一致）。
    /// </summary>
    private struct SaveState
    {
        private D3D11.InputLayout inputLayout;
        private SharpDX.Direct3D.PrimitiveTopology topology;
        private D3D11.Buffer[] vertexBuffers;
        private int[] vertexStrides;
        private int[] vertexOffsets;
        private D3D11.Buffer[] vsConstants;
        private D3D11.Buffer[] psConstants;
        private D3D11.RasterizerState rasterizerState;
        private D3D11.BlendState blendState;
        private D3D11.DepthStencilState depthStencilState;

        public static SaveState Capture(D3D11.DeviceContext context)
        {
            SaveState state = new()
            {
                inputLayout = context.InputAssembler.InputLayout,
                topology = context.InputAssembler.PrimitiveTopology,
                vertexBuffers = new D3D11.Buffer[1],
                vertexStrides = new int[1],
                vertexOffsets = new int[1],
                rasterizerState = context.Rasterizer.State,
                blendState = context.OutputMerger.BlendState,
                depthStencilState = context.OutputMerger.DepthStencilState,
                vsConstants = context.VertexShader.GetConstantBuffers(0, 2),
                psConstants = context.PixelShader.GetConstantBuffers(0, 2),
            };

            context.InputAssembler.GetVertexBuffers(0, 1, state.vertexBuffers, state.vertexStrides, state.vertexOffsets);
            return state;
        }

        public void Restore(D3D11.DeviceContext context)
        {
            context.InputAssembler.InputLayout = inputLayout;
            context.InputAssembler.PrimitiveTopology = topology;
            context.InputAssembler.SetVertexBuffers(0, new D3D11.VertexBufferBinding(vertexBuffers[0], vertexStrides[0], vertexOffsets[0]));

            context.VertexShader.SetConstantBuffers(0, vsConstants);
            context.PixelShader.SetConstantBuffers(0, psConstants);

            // 着色器对象读不回来，解绑成 null；后面的绘制都会自己重新绑定
            context.VertexShader.Set(null);
            context.PixelShader.Set(null);

            context.Rasterizer.State = rasterizerState;
            context.OutputMerger.SetBlendState(blendState);
            context.OutputMerger.SetDepthStencilState(depthStencilState);
        }
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        hasTarget = false;

        vertexShader?.Dispose();
        pixelShader?.Dispose();
        inputLayout?.Dispose();
        constantBuffer?.Dispose();
        vertexBuffer?.Dispose();
        blendState?.Dispose();
        depthState?.Dispose();
        rasterizerState?.Dispose();
        heightCache.Clear();
        chunkSnapshot.Clear();
    }
}
