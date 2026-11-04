using Frosty.Core;
using LevelEditorPlugin.Havok;
using LevelEditorPlugin.Render.Proxies;
using SharpDX;
using SharpDX.D3DCompiler;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using D3D11 = SharpDX.Direct3D11;

namespace LevelEditorPlugin.Render.Havok;

/// <summary>
/// Level Editor 的 Havok 碰撞体叠加层。
///
/// 数据来源：每个可见渲染 Proxy 的实体 → ObjectBlueprint 里的 HavokAsset → HavokPhysicsData res
/// （见 <see cref="HavokSourceResolver"/>），解析用插件内置的 Havok 解析器，得到和 Havok 编辑器里一样的
/// 三角形 + 线框，再按实体世界变换画到关卡视口上。
///
/// 性能上的两个考虑：
/// 1. 同一个 res 在一个关卡里会被成百上千个实例引用，所以几何按 ResRid 只解析/上传一次，绘制时只换世界矩阵；
/// 2. 解析是分帧「泵」进去的（<see cref="Pump"/>，每帧只处理几个 res），避免打开开关时卡住整个编辑器。
/// </summary>
public sealed class HavokOverlay : IDisposable
{
    /// <summary>一帧最多画多少个实例（按距离取最近的），防止整关卡几万个实例把帧率拖死。</summary>
    private const int MaxDrawInstances = 800;

    /// <summary>每帧最多解析几个新的碰撞资源。</summary>
    private const int DefaultParseBudget = 8;

    /// <summary>每帧解析时间上限（毫秒），超了就留到下一帧。</summary>
    private const double ParseTimeBudgetMs = 6.0;

    /// <summary>
    /// 关卡里模型渲染用的 X 镜像（见 LevelEditorScreen.RenderMeshes：WorldMatrix = Scaling(-1,1,1) * transpose(world)，
    /// 经 HLSL cbuffer 的 column_major 解释后等价于 p * world * Mirror）。
    /// 所以叠加层必须把镜像乘在 <b>世界变换之后</b>，乘在左边会让位移/旋转对不上。
    /// </summary>
    private static readonly Matrix Mirror = Matrix.Scaling(-1, 1, 1);

    /// <summary>实心面在关卡里要透一点，否则一堆碰撞体叠在一起会把场景糊死。</summary>
    private const float SolidAlpha = 0.55f;

    [StructLayout(LayoutKind.Sequential)]
    private struct Constants
    {
        public Matrix ViewProj;
        public Vector4 Params;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OverlayVertex
    {
        public Vector3 Position;
        public Vector4 Color;
    }

    private const string ShaderSource = @"
cbuffer Constants : register(b0)
{
    float4x4 ViewProj;
    float4 Params;
};

struct VSIn { float3 Pos : POSITION; float4 Color : COLOR; };
struct PSIn { float4 Pos : SV_POSITION; float4 Color : COLOR; };

PSIn VSMain(VSIn i)
{
    PSIn o;
    o.Pos = mul(float4(i.Pos, 1), ViewProj);
    o.Color = i.Color;
    return o;
}

float4 PSMain(PSIn i) : SV_TARGET
{
    return i.Color;
}";

    private static readonly int VertexStride = Utilities.SizeOf<OverlayVertex>();

    private sealed class MeshData
    {
        public D3D11.Buffer Triangles;
        public int TriangleVertices;
        public D3D11.Buffer Lines;
        public int LineVertices;
        public BoundingBox LocalBounds;

        /// <summary>诊断用：res 名与形状摘要。</summary>
        public string ResName;
        public string Summary;
    }

    private struct Instance
    {
        public ulong ResRid;

        /// <summary>普通实例：世界变换每帧从 Proxy 取，实体被移动后叠加层立刻跟上。</summary>
        public RenderProxy Proxy;

        /// <summary>烘焙进 res 的实例（StaticModelGroup）：用父级实体的世界变换，整份 res 只画一次。</summary>
        public LevelEditorPlugin.Entities.Entity WorldEntity;
    }

    private readonly D3D11.Device device;

    private D3D11.VertexShader vertexShader;
    private D3D11.PixelShader pixelShader;
    private D3D11.InputLayout inputLayout;
    private D3D11.Buffer constantBuffer;
    private D3D11.BlendState alphaBlend;
    private D3D11.DepthStencilState depthReadOnly;
    private D3D11.RasterizerState rasterizer;

    private readonly List<Instance> instances = [];
    private readonly Dictionary<ulong, MeshData> meshes = [];
    private readonly HashSet<ulong> failed = [];
    private readonly Queue<ulong> pendingRids = new();
    private readonly HashSet<ulong> queuedRids = [];
    private readonly Queue<RenderProxy> rebuildQueue = new();
    private bool collecting;
    private bool diagnosticsDumped;
    private readonly HashSet<(Guid, ulong)> instanceKeys = [];
    private readonly List<(float Distance, Instance Instance)> drawList = [];

    private readonly Stopwatch parseClock = new();

    public bool IsDisposed { get; private set; }

    /// <summary>已解析好的碰撞资源数量。</summary>
    public int AssetCount => meshes.Count;

    /// <summary>当前关卡里引用到碰撞体的实例数量。</summary>
    public int InstanceCount => instances.Count;

    /// <summary>还在排队等待解析的资源数量。</summary>
    public int PendingCount => pendingRids.Count;

    public int FailedCount => failed.Count;

    public HavokOverlay(D3D11.Device inDevice)
    {
        device = inDevice;

        using (CompilationResult vs = ShaderBytecode.Compile(ShaderSource, "VSMain", "vs_5_0"))
        using (CompilationResult ps = ShaderBytecode.Compile(ShaderSource, "PSMain", "ps_5_0"))
        {
            vertexShader = new D3D11.VertexShader(device, vs.Bytecode);
            pixelShader = new D3D11.PixelShader(device, ps.Bytecode);
            inputLayout = new D3D11.InputLayout(device, ShaderSignature.GetInputSignature(vs.Bytecode), new[]
            {
                new D3D11.InputElement("POSITION", 0, SharpDX.DXGI.Format.R32G32B32_Float, 0, 0),
                new D3D11.InputElement("COLOR", 0, SharpDX.DXGI.Format.R32G32B32A32_Float, 12, 0),
            });
        }

        constantBuffer = new D3D11.Buffer(device, Utilities.SizeOf<Constants>(), D3D11.ResourceUsage.Default,
            D3D11.BindFlags.ConstantBuffer, D3D11.CpuAccessFlags.None, D3D11.ResourceOptionFlags.None, 0);

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
        alphaBlend = new D3D11.BlendState(device, blendDesc);

        // 只读深度：叠加层永远画在最上面（不会被模型自己挡住），但自身不做深度写入
        depthReadOnly = new D3D11.DepthStencilState(device, new D3D11.DepthStencilStateDescription
        {
            IsDepthEnabled = true,
            DepthWriteMask = D3D11.DepthWriteMask.Zero,
            DepthComparison = D3D11.Comparison.LessEqual
        });

        rasterizer = new D3D11.RasterizerState(device, new D3D11.RasterizerStateDescription
        {
            CullMode = D3D11.CullMode.None,
            FillMode = D3D11.FillMode.Solid,
            IsDepthClipEnabled = true,
            IsScissorEnabled = false
        });
    }

    #region -- 收集实例 --

    /// <summary>每帧最多处理多少个 Proxy（会触发 HavokAsset 的 EBX 载入，所以也要限量）。</summary>
    private const int ProxyBudget = 64;

    /// <summary>按当前关卡里的渲染 Proxy 重建实例列表（几何缓存保留，不清）。</summary>
    public void Rebuild(IEnumerable<RenderProxy> proxies)
    {
        instances.Clear();
        instanceKeys.Clear();
        pendingRids.Clear();
        queuedRids.Clear();
        rebuildQueue.Clear();

        HavokSourceResolver.ClearCache();
        diagnosticsDumped = false;

        foreach (RenderProxy proxy in proxies)
        {
            if (proxy != null)
                rebuildQueue.Enqueue(proxy);
        }

        collecting = true;
    }

    /// <summary>把一个 Proxy 引用的碰撞 res 记进实例表。</summary>
    private void CollectProxy(RenderProxy proxy)
    {
        LevelEditorPlugin.Entities.Entity entity = proxy.OwnerEntity;
        if (entity == null || !entity.IsVisible)
            return;

        // StaticModelGroup 里烘焙进 res 的成员（StaticModelGroupElementEntityData.IsHavok）：
        // 成员的变换本身就是 res 里的那份实例变换，碰撞引用在 **父级 group** 的 components 上，
        // 所以对这类实体按 group + res 只记一次，用 group 的变换画整份 res。
        LevelEditorPlugin.Entities.Entity source = entity;
        bool baked = IsBakedHavokTransform(entity.Data);
        if (baked)
        {
            if (entity.Parent is LevelEditorPlugin.Entities.Entity parentEntity)
                source = parentEntity;
            else
                return;
        }

        List<ulong> rids;
        try
        {
            rids = HavokSourceResolver.Resolve(source);
        }
        catch (Exception e)
        {
            App.Logger?.LogWarning($"Havok 叠加：解析实体碰撞引用失败（{entity.DisplayName}）：{e.Message}");
            return;
        }

        if (rids.Count == 0)
            return;

        foreach (ulong rid in rids)
        {
            Instance instance;
            if (baked)
            {
                // 整份 res 已经包含所有成员的变换，只画一次
                instance = new Instance { ResRid = rid, WorldEntity = source };
                if (!instanceKeys.Add((source.InstanceGuid, rid)))
                    continue;
            }
            else
            {
                instance = new Instance { ResRid = rid, Proxy = proxy };
                if (!instanceKeys.Add((entity.InstanceGuid, rid)))
                    continue;
            }

            instances.Add(instance);

            if (!meshes.ContainsKey(rid) && !failed.Contains(rid) && queuedRids.Add(rid))
                pendingRids.Enqueue(rid);
        }
    }

    /// <summary>诊断：打开开关后打一次日志，把碰撞体包围盒和模型包围盒放在一起对比，便于定位坐标偏差。</summary>
    private void DumpDiagnostics()
    {
        if (diagnosticsDumped || meshes.Count == 0)
            return;

        diagnosticsDumped = true;

        int logged = 0;
        int mismatched = 0;
        foreach (Instance instance in instances)
        {
            if (!meshes.TryGetValue(instance.ResRid, out MeshData mesh))
                continue;

            BoundingBox havokBounds = TransformBounds(mesh.LocalBounds, GetInstanceWorld(instance) * Mirror);

            LevelEditorPlugin.Entities.Entity entity = instance.Proxy != null
                ? instance.Proxy.OwnerEntity
                : instance.WorldEntity;

            if (instance.Proxy == null)
            {
                if (logged < 8)
                {
                    logged++;
                    App.Logger?.Log($"Havok 叠加诊断：res={mesh.ResName}（{mesh.Summary}）局部包围盒 {Format(mesh.LocalBounds)}");
                }
                continue;
            }

            BoundingBox modelBounds = instance.Proxy.BoundingBox;
            bool modelValid = IsValidBounds(modelBounds);
            Vector3 havokCenter = (havokBounds.Minimum + havokBounds.Maximum) * 0.5f;

            if (logged < 8)
            {
                logged++;
                App.Logger?.Log($"Havok 叠加诊断：实体={entity?.DisplayName} res={mesh.ResName}（{mesh.Summary}）");
                App.Logger?.Log($"    res 局部包围盒 {Format(mesh.LocalBounds)}");
                App.Logger?.Log($"    世界变换 T={Format((GetInstanceWorld(instance) * Mirror).TranslationVector)}");
                App.Logger?.Log($"    碰撞体世界包围盒 {Format(havokBounds)}");
                App.Logger?.Log(modelValid
                    ? $"    模型世界包围盒 {Format(modelBounds)}  中心差 {Format(havokCenter - (modelBounds.Minimum + modelBounds.Maximum) * 0.5f)}"
                    : "    模型包围盒不可用");
            }

            if (modelValid && !havokBounds.Intersects(modelBounds))
            {
                mismatched++;
                if (mismatched <= 5)
                {
                    App.Logger?.LogWarning($"Havok 叠加：{entity?.DisplayName} 的碰撞体与模型包围盒不重叠（碰撞中心 {Format(havokCenter)}，模型中心 {Format((modelBounds.Minimum + modelBounds.Maximum) * 0.5f)}，res={mesh.ResName}）");
                }
            }
        }

        if (mismatched > 0)
            App.Logger?.LogWarning($"Havok 叠加：{mismatched} 个实例的碰撞体与模型包围盒不重叠，可能是 res 空间与实体空间不一致。");
    }

    private static bool IsValidBounds(BoundingBox b)
    {
        return b.Minimum.X <= b.Maximum.X && b.Minimum.Y <= b.Maximum.Y && b.Minimum.Z <= b.Maximum.Z
            && !float.IsNaN(b.Minimum.X) && !float.IsNaN(b.Maximum.X);
    }

    private static BoundingBox TransformBounds(BoundingBox local, Matrix world)
    {
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new(
                (i & 1) == 0 ? local.Minimum.X : local.Maximum.X,
                (i & 2) == 0 ? local.Minimum.Y : local.Maximum.Y,
                (i & 4) == 0 ? local.Minimum.Z : local.Maximum.Z);
            Vector3 p = Vector3.TransformCoordinate(corner, world);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return new BoundingBox(min, max);
    }

    private static string Format(Vector3 v) => $"({v.X:F3}, {v.Y:F3}, {v.Z:F3})";

    private static string Format(BoundingBox b) => $"min {Format(b.Minimum)} max {Format(b.Maximum)}";

    /// <summary>实例的世界变换：普通实体看 Proxy（跟着拖拽实时更新），烘焙实例看父级实体。</summary>
    private static Matrix GetInstanceWorld(Instance instance)
    {
        if (instance.Proxy != null)
            return instance.Proxy.Transform;

        if (instance.WorldEntity is LevelEditorPlugin.Entities.ISpatialEntity spatial)
            return spatial.GetTransform();

        return Matrix.Identity;
    }

    private static bool IsHidden(Instance instance)
    {
        LevelEditorPlugin.Entities.Entity entity = instance.Proxy != null ? instance.Proxy.OwnerEntity : instance.WorldEntity;
        return entity != null && !entity.IsVisible;
    }

    private static readonly Dictionary<Type, PropertyInfo> isHavokProperties = [];

    /// <summary>StaticModelGroupElementEntityData.IsHavok：变换烘焙在 res 里的那种成员。</summary>
    private static bool IsBakedHavokTransform(FrostySdk.Ebx.GameObjectData data)
    {
        if (data == null)
            return false;

        Type type = data.GetType();
        if (!isHavokProperties.TryGetValue(type, out PropertyInfo pi))
        {
            pi = type.GetProperty("IsHavok", BindingFlags.Public | BindingFlags.Instance);
            if (pi != null && pi.PropertyType != typeof(bool))
                pi = null;
            isHavokProperties[type] = pi;
        }

        if (pi == null)
            return false;

        try
        {
            return (bool)pi.GetValue(data);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>分帧收集实例 + 解析碰撞资源，返回本帧新解析成功的数量。</summary>
    public int Pump(int budget = DefaultParseBudget)
    {
        if (!collecting && pendingRids.Count == 0)
            return 0;

        int parsed = 0;
        int processed = 0;
        parseClock.Restart();

        // 第一步：收集实例（第一次碰到某个蓝图时会顺带载入它的 HavokAsset EBX）
        while (rebuildQueue.Count > 0 && parseClock.Elapsed.TotalMilliseconds < ParseTimeBudgetMs && processed < ProxyBudget)
        {
            RenderProxy proxy = rebuildQueue.Dequeue();
            try
            {
                CollectProxy(proxy);
            }
            catch (Exception e)
            {
                App.Logger?.LogWarning($"Havok 叠加：收集实例失败：{e.Message}");
            }
            processed++;
        }

        if (collecting && rebuildQueue.Count == 0)
        {
            collecting = false;
            App.Logger?.Log($"Havok 叠加：{instances.Count} 个实例，{pendingRids.Count} 个待解析资源");
        }

        // 第二步：解析碰撞 res（每个 res 只解析一次，之后所有实例共用同一份顶点缓冲）
        while (pendingRids.Count > 0 && parsed < budget && parseClock.Elapsed.TotalMilliseconds < ParseTimeBudgetMs * 2)
        {
            ulong rid = pendingRids.Dequeue();
            if (meshes.ContainsKey(rid) || failed.Contains(rid))
                continue;

            if (TryParseResource(rid, out MeshData meshData))
            {
                meshes[rid] = meshData;
                parsed++;
            }
        }

        parseClock.Stop();
        return parsed;
    }

    private bool TryParseResource(ulong rid, out MeshData meshData)
    {
        meshData = null;

        try
        {
            FrostySdk.Managers.ResAssetEntry entry = App.AssetManager.GetResEntry(rid);
            if (entry == null)
            {
                failed.Add(rid);
                return false;
            }

            // 只要 Havok 物理 res，别的类型（布料/动画）走不到这里也防一手
            if (entry.ResType != (uint)FrostySdk.Managers.ResourceType.HavokPhysicsData)
            {
                failed.Add(rid);
                return false;
            }

            // 破坏碎块（碎片）的碰撞：整关卡铺满反而看不清，按需求跳过
            if (ContainsCluster(entry.Name))
            {
                failed.Add(rid);
                return false;
            }

            byte[] bytes;
            using (Stream stream = App.AssetManager.GetRes(entry))
            using (FrostySdk.IO.NativeReader reader = new(stream))
            {
                bytes = reader.ReadToEnd();
            }

            if (bytes == null || bytes.Length == 0)
            {
                failed.Add(rid);
                return false;
            }

            // 直接用解析器，不走 HavokSession：叠加层只要几何，不需要撤销/写回那一套，
            // 也免得 Load 里先按默认选项建一次几何再重建。
            HavokRes res = HavokRes.Parse(bytes, entry.Name, HavokLayout.BuiltIn);
            HavokScene scene = HavokSceneBuilder.Build(res);
            HavokGeometryResult geometry = HavokGeometry.Build(scene, CreateOptions());
            if (geometry == null || !geometry.HasGeometry)
            {
                failed.Add(rid);
                return false;
            }

            meshData = new MeshData
            {
                ResName = entry.Name,
                Summary = $"根形状 {scene.RootShapes.Count} 个 / 复合 {scene.RootCompounds.Count} 个 / 诊断 {res.Warnings.Count + res.Issues.Count} 条",
                LocalBounds = new BoundingBox(
                    new Vector3(geometry.Min.X, geometry.Min.Y, geometry.Min.Z),
                    new Vector3(geometry.Max.X, geometry.Max.Y, geometry.Max.Z))
            };

            if (geometry.Triangles.Count > 0)
            {
                OverlayVertex[] verts = Convert(geometry.Triangles, SolidAlpha);
                meshData.Triangles = D3D11.Buffer.Create(device, D3D11.BindFlags.VertexBuffer, verts);
                meshData.TriangleVertices = verts.Length;
            }

            if (geometry.Lines.Count > 0)
            {
                OverlayVertex[] verts = Convert(geometry.Lines, 1.0f);
                meshData.Lines = D3D11.Buffer.Create(device, D3D11.BindFlags.VertexBuffer, verts);
                meshData.LineVertices = verts.Length;
            }

            return meshData.TriangleVertices > 0 || meshData.LineVertices > 0;
        }
        catch (Exception e)
        {
            App.Logger?.LogWarning($"Havok 叠加：解析 res 0x{rid:X16} 失败：{e.Message}");
            failed.Add(rid);
            meshData = null;
            return false;
        }
    }

    private HavokGeometryOptions CreateOptions() => new()
    {
        DrawSolid = true,
        DrawWire = true,
        DrawGrid = false,
        DrawCompoundAabb = false,
        DrawInstanceAabb = false,
        DrawBvh = false,
        DrawDomain = false,
        DrawResParts = false,
        // 分块着色：一个 res 里的每个碰撞分块各给一种颜色（相邻分块色相差得开），
        // 这是叠加层的默认外观。想要统一的浅蓝色可以在选项里关掉。
        DrawPieceColors = Options.LevelEditorRenderingOptions.HavokPieceColors,
        OnlySelected = false
    };

    private static bool ContainsCluster(string name)
        => !string.IsNullOrEmpty(name) && name.IndexOf("cluster", StringComparison.OrdinalIgnoreCase) >= 0;

    private static OverlayVertex[] Convert(List<HavokVertex> source, float alphaScale)
    {
        OverlayVertex[] result = new OverlayVertex[source.Count];
        for (int i = 0; i < source.Count; i++)
        {
            HavokVertex v = source[i];
            result[i] = new OverlayVertex
            {
                Position = new Vector3(v.Position.X, v.Position.Y, v.Position.Z),
                Color = new Vector4(v.Color.X, v.Color.Y, v.Color.Z, v.Color.W * alphaScale)
            };
        }
        return result;
    }

    #endregion

    #region -- 渲染 --

    /// <summary>
    /// 在编辑器合成阶段画叠加层。调用方负责给好渲染目标；这里会完整保存/还原自己改动的管线状态，
    /// 尤其是 PrimitiveTopology（画线要改成 LineList，不还原会让后续 pass 全部错乱）。
    /// </summary>
    public void Draw(D3D11.DeviceContext context, Matrix viewProj, D3D11.DepthStencilView depthTarget, Vector3 eyePosition)
    {
        if (IsDisposed || meshes.Count == 0 || instances.Count == 0)
            return;

        DumpDiagnostics();

        // 按距离取最近的若干个，避免整关卡全画
        drawList.Clear();
        foreach (Instance instance in instances)
        {
            if (!meshes.ContainsKey(instance.ResRid))
                continue;

            if (IsHidden(instance))
                continue;

            float distance = (GetInstanceWorld(instance).TranslationVector - eyePosition).Length();
            drawList.Add((distance, instance));
        }

        if (drawList.Count == 0)
            return;

        if (drawList.Count > MaxDrawInstances)
        {
            drawList.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            drawList.RemoveRange(MaxDrawInstances, drawList.Count - MaxDrawInstances);
        }

        D3D11.RenderTargetView[] previousTargets = context.OutputMerger.GetRenderTargets(6, out D3D11.DepthStencilView previousDepth);
        SaveState save = SaveState.Capture(context);

        try
        {
            context.OutputMerger.SetRenderTargets(depthTarget, previousTargets);

            context.Rasterizer.State = rasterizer;
            context.OutputMerger.SetBlendState(alphaBlend);
            context.OutputMerger.SetDepthStencilState(depthReadOnly);
            context.InputAssembler.InputLayout = inputLayout;

            context.VertexShader.Set(vertexShader);
            context.VertexShader.SetConstantBuffer(0, constantBuffer);
            context.PixelShader.Set(pixelShader);
            context.PixelShader.SetConstantBuffer(0, constantBuffer);

            foreach ((float _, Instance instance) in drawList)
            {
                if (!meshes.TryGetValue(instance.ResRid, out MeshData mesh))
                    continue;

                Matrix world = GetInstanceWorld(instance) * Mirror;
                Constants constants = new()
                {
                    ViewProj = Matrix.Transpose(world * viewProj),
                    Params = Vector4.Zero
                };
                context.UpdateSubresource(ref constants, constantBuffer);

                if (mesh.Triangles != null)
                {
                    context.InputAssembler.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
                    context.InputAssembler.SetVertexBuffers(0, new D3D11.VertexBufferBinding(mesh.Triangles, VertexStride, 0));
                    context.Draw(mesh.TriangleVertices, 0);
                }

                if (mesh.Lines != null)
                {
                    context.InputAssembler.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.LineList;
                    context.InputAssembler.SetVertexBuffers(0, new D3D11.VertexBufferBinding(mesh.Lines, VertexStride, 0));
                    context.Draw(mesh.LineVertices, 0);
                }
            }

            context.OutputMerger.SetRenderTargets(previousDepth, previousTargets);
        }
        finally
        {
            save.Restore(context);
        }
    }

    /// <summary>
    /// 保存/还原会被叠加层改动的管线状态。
    /// 着色器对象没有办法读回来（SharpDX 没暴露 Get），所以还原时把 VS/PS 解绑成 null：
    /// 紧接着画 Gizmo 的代码会自己重新绑定，而 null 恰好是「干净状态」，不会再污染后面的 pass。
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
                vsConstants = context.VertexShader.GetConstantBuffers(0, 2),
                psConstants = context.PixelShader.GetConstantBuffers(0, 2),
                rasterizerState = context.Rasterizer.State,
                blendState = context.OutputMerger.BlendState,
                depthStencilState = context.OutputMerger.DepthStencilState
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

            context.VertexShader.Set(null);
            context.PixelShader.Set(null);

            context.Rasterizer.State = rasterizerState;
            context.OutputMerger.SetBlendState(blendState);
            context.OutputMerger.SetDepthStencilState(depthStencilState);
        }
    }

    #endregion

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;

        foreach (MeshData mesh in meshes.Values)
        {
            mesh.Triangles?.Dispose();
            mesh.Lines?.Dispose();
        }
        meshes.Clear();
        instances.Clear();
        pendingRids.Clear();
        rebuildQueue.Clear();
        failed.Clear();

        constantBuffer?.Dispose();
        alphaBlend?.Dispose();
        depthReadOnly?.Dispose();
        rasterizer?.Dispose();
        inputLayout?.Dispose();
        vertexShader?.Dispose();
        pixelShader?.Dispose();
    }
}
