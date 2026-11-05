using Frosty.Core.Screens;
using Frosty.Core.Viewport;
using Frosty.Core.Viewport.DXUT;
using Frosty.Core.Windows;
using FrostySdk;
using LevelEditorPlugin;
using LevelEditorPlugin.BF1Core.Utils;
using LevelEditorPlugin.BF1Core;
using LevelEditorPlugin.Editors;
using LevelEditorPlugin.Entities;
using LevelEditorPlugin.Options;
using LevelEditorPlugin.Library.Image;
using LevelEditorPlugin.Managers;
using LevelEditorPlugin.Render;
using LevelEditorPlugin.Render.Havok;
using LevelEditorPlugin.Render.Proxies;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using D3D11 = SharpDX.Direct3D11;

namespace LevelEditorPlugin.Screens;

public delegate void RenderAction(RenderCreateState state);

public class LevelEditorCamera : FirstPersonCamera
{
    public float FOV => fov;
    public float AspectRatio => aspectRatio;

    public Matrix WorldMatrix
    {
        get
        {
            return Matrix.RotationYawPitchRoll(-cameraYawAngle, cameraPitchAngle, 0) * Matrix.Translation(cameraWorldMatrix.TranslationVector * new Vector3(-1, 1, 1));
        }
    }

    public LevelEditorCamera()
    {
        rotationScaler = 0.015f;
        moveScaler = 10.0f;
    }
}

public class GizmoRenderProxy : MeshRenderBase
{
    public Matrix Transform { get; set; }
    public Quaternion Orientation { get; set; } = Quaternion.Identity;
    public float Scale { get; protected set; }
    public int HighlightAxis { get; set; } = -1;

    private ObjRenderable renderData;
    private RotationRingMesh rotationRings;

    private ShaderPermutation permutation;
    private D3D11.Buffer pixelParameters;
    private List<D3D11.ShaderResourceView> pixelTextures = [];
    private Matrix originalTransform;

    private List<ShaderParameter> materialParameters = [];
    private List<ShaderParameter> materialTextures = [];

    private static GeometryDeclarationDesc GeometryDecl = GeometryDeclarationDesc.Create(new GeometryDeclarationDesc.Element[]
    {
        new GeometryDeclarationDesc.Element
        {
            Usage = VertexElementUsage.Pos,
            Format = VertexElementFormat.Float3
        },
        new GeometryDeclarationDesc.Element
        {
            Usage = VertexElementUsage.Normal,
            Format = VertexElementFormat.Float3
        },
        new GeometryDeclarationDesc.Element
        {
            Usage = VertexElementUsage.TexCoord0,
            Format = VertexElementFormat.Float2
        }
    });

    public GizmoRenderProxy(RenderCreateState state, ObjRenderable meshData)
    {
        renderData = meshData;
        Transform = Matrix.Identity;
        originalTransform = Transform;

        CreateMaterial(state);
        rotationRings = new RotationRingMesh(state.Device);
    }

    public void RebuildScaledTransform()
    {
        Vector3 position = Transform.TranslationVector;
        float distance = 1f;
        if (Screens.LevelEditorScreen.EditorCamera != null)
            distance = (position - Screens.LevelEditorScreen.EditorCamera.WorldMatrix.TranslationVector).Length();

        Scale = Math.Max(distance / 5.0f, 0.125f);
        if (Orientation.LengthSquared() < 1e-8f)
            Orientation = Quaternion.Identity;

        Transform = Matrix.Scaling(Scale) * Matrix.RotationQuaternion(Orientation) * Matrix.Translation(position);
    }

    public Ray ToLocalRay(Ray worldRay)
    {
        Quaternion inverse = Quaternion.Invert(Orientation);
        Vector3 center = Transform.TranslationVector;
        Vector3 origin = Vector3.Transform(worldRay.Position - center, inverse);
        Vector3 direction = Vector3.Transform(worldRay.Direction, inverse);
        if (direction.LengthSquared() > 1e-12f)
            direction.Normalize();
        return new Ray(origin, direction);
    }

    public override void Render(D3D11.DeviceContext context, MeshRenderPath renderPath)
    {
        Matrix cameraTransform = Screens.LevelEditorScreen.EditorCamera.WorldMatrix;
        RebuildScaledTransform();

        if (renderPath == MeshRenderPath.Forward)
        {
            permutation.SetState(context, renderPath);
            context.PixelShader.SetShaderResources(1, pixelTextures.ToArray());

            if (!TerrainEditState.IsTerrainEditModeEnabled && rotationRings != null)
            {
                Vector3 worldOffset = cameraTransform.TranslationVector - Transform.TranslationVector;
                Vector3 localCamera = Vector3.Transform(worldOffset, Quaternion.Invert(Orientation));
                rotationRings.Draw(context, HighlightAxis, localCamera);
            }

            context.VertexShader.SetConstantBuffer(2, pixelParameters);
            context.PixelShader.SetConstantBuffer(2, pixelParameters);
            renderData.Render(context, renderPath);
        }
    }

    public bool RaycastRings(Ray ray, out int localAxis, out float distance)
    {
        localAxis = -1;
        distance = float.MaxValue;
        if (rotationRings == null || Scale < 1e-4f || Screens.LevelEditorScreen.EditorCamera == null)
            return false;

        return RotationRingMesh.Raycast(ToLocalRay(ray), Vector3.Zero, Scale, out localAxis, out distance);
    }

    public virtual MeshRenderInstance GetInstance(float distToCamera)
    {
        return new MeshRenderInstance() { RenderMesh = this, Transform = Transform };
    }

    public void UpdateSelectIndex(RenderCreateState state, int newSelectIndex)
    {
        materialParameters.Clear();
        materialParameters.Add(new ShaderParameter("SelectIndex", ShaderParameterType.Float4, new[] { (float)newSelectIndex, 0, 0, 0 }));
        permutation.AssignParameters(state, materialParameters, materialTextures, ref pixelParameters, ref pixelTextures);
    }

    private void CreateMaterial(RenderCreateState state)
    {
        materialTextures.Add(new ShaderParameter("Texture", ShaderParameterType.Tex2d, ContentPaths.Texture("TranslateGizmo.dds")));
        materialTextures.Add(new ShaderParameter("SelectMask", ShaderParameterType.Tex2d, ContentPaths.Texture("TranslateGizmoSelected.dds")));

        permutation = state.ShaderLibrary.GetUserShader("GizmoShader", GeometryDecl);
        permutation.IsTwoSided = true;
        permutation.LoadShaders(state.Device);
        permutation.AssignParameters(state, materialParameters, materialTextures, ref pixelParameters, ref pixelTextures);
    }

    public void Dispose()
    {
        rotationRings?.Dispose();
        rotationRings = null;
        renderData.Dispose();
        permutation.Dispose();
        pixelParameters?.Dispose();
        pixelTextures.Clear();
    }
}

public class Gizmo
{
    public bool IsVisible { get; set; }
    public Matrix Transform
    {
        get => renderProxy.Transform;
        set
        {
            if (renderProxy.Transform != value)
            {
                renderProxy.Transform = value;
            }
        }
    }
    public BoundingBox BoundingBox { get; protected set; }

    protected ObjRenderable meshData;
    protected string meshFilename;

    protected GizmoRenderProxy renderProxy;

    public Gizmo()
    {
        IsVisible = false;
    }

    public void CreateRenderProxy(RenderCreateState state)
    {
        meshData = LoadedMeshManager.Instance.LoadMesh(state, "TranslateGizmo");
        renderProxy = new GizmoRenderProxy(state, meshData);
    }

    public MeshRenderInstance GetInstance()
    {
        renderProxy.RebuildScaledTransform();
        return renderProxy.GetInstance(0.0f);
    }

    public virtual bool HitTest(Ray hitTestRay)
    {
        return IsVisible && hitTestRay.Intersects(BoundingBox);
    }

    public virtual void Update(RenderCreateState state)
    {
    }

    public virtual bool MouseDown(int x, int y, Ray rayCast, MouseButton button)
    {
        return false;
    }

    public virtual bool MouseMove(int x, int y, Ray rayCast)
    {
        return true;
    }

    public virtual bool MouseUp(int x, int y, Ray rayCast, MouseButton button)
    {
        return false;
    }

    public void Dispose()
    {
        renderProxy.Dispose();
        LoadedMeshManager.Instance.UnloadMesh(meshData);
    }
}

public class TranslateGizmo : Gizmo
{
    public bool IsMoving => moved;
    public bool IsDragging => leftMouseDown;
    public bool IsRotating => leftMouseDown && selectedAxis >= FirstRotateAxis;
    public Quaternion Orientation => renderProxy.Orientation;

    private const int FirstRotateAxis = 3;

    private BoundingBox[] axisBoundingBoxes = new BoundingBox[3]
    {
        new BoundingBox(Vector3.Zero, new Vector3(0.25f, 0.25f, 1.0f)),
        new BoundingBox(Vector3.Zero, new Vector3(0.25f, 1.0f, 0.25f)),
        new BoundingBox(Vector3.Zero, new Vector3(1.0f, 0.25f, 0.25f)),
    };
    private int selectedAxis = -1;
    private bool leftMouseDown;
    private bool moved;

    private Quaternion dragStartOrientation = Quaternion.Identity;
    private Vector3 dragAxis;
    private Vector3 dragLocalAxis;
    private Vector3 dragEye;
    private Vector3 dragPointStart;
    private Vector3 dragPlaneNormal;
    private Vector3 dragOrigin;
    private float dragGrab;
    private float dragRadius;
    private bool dragOrbit;
    private bool dragPointReady;

    public TranslateGizmo()
    {
        meshFilename = "TranslateGizmo";
    }

    public void SetPose(Vector3 position, Quaternion orientation)
    {
        renderProxy.Orientation = orientation.LengthSquared() > 1e-8f ? Quaternion.Normalize(orientation) : Quaternion.Identity;
        Matrix transform = Transform;
        transform.TranslationVector = position;
        Transform = transform;
        renderProxy.RebuildScaledTransform();
    }

    public void SetPose(Matrix world)
    {
        if (!world.Decompose(out _, out Quaternion rotation, out Vector3 translation))
        {
            translation = world.TranslationVector;
            rotation = Quaternion.Identity;
        }

        SetPose(translation, rotation);
    }

    public override void Update(RenderCreateState state)
    {
        if (!IsVisible)
            return;

        renderProxy.RebuildScaledTransform();

        float reach = Math.Max(0.5f, RotationRingMesh.AxisRadius + RotationRingMesh.PickRadius);
        BoundingBox = BoundingBox.FromSphere(new BoundingSphere(Transform.TranslationVector, renderProxy.Scale * reach));
        renderProxy.HighlightAxis = selectedAxis;

        float smallAxisAmount = 0.0625f * renderProxy.Scale;
        float largeAxisAmount = 0.5f * renderProxy.Scale;

        axisBoundingBoxes[0] = new BoundingBox(new Vector3(-smallAxisAmount, -smallAxisAmount, 0f), new Vector3(smallAxisAmount, smallAxisAmount, largeAxisAmount));
        axisBoundingBoxes[1] = new BoundingBox(new Vector3(-smallAxisAmount, 0f, -smallAxisAmount), new Vector3(smallAxisAmount, largeAxisAmount, smallAxisAmount));
        axisBoundingBoxes[2] = new BoundingBox(new Vector3(-largeAxisAmount, -smallAxisAmount, -smallAxisAmount), new Vector3(0f, smallAxisAmount, smallAxisAmount));

        renderProxy.UpdateSelectIndex(state, selectedAxis);
    }

    public override bool HitTest(Ray hitTestRay)
    {
        if (leftMouseDown)
            return true;

        if (!base.HitTest(hitTestRay))
        {
            ClearHover();
            return false;
        }

        Ray localRay = renderProxy.ToLocalRay(hitTestRay);
        int arrowAxis = -1;
        float arrowDist = float.MaxValue;
        Vector3 arrowPoint = Vector3.Zero;
        for (int i = 0; i < 3; i++)
        {
            if (localRay.Intersects(ref axisBoundingBoxes[i], out Vector3 hit))
            {
                float dist = (hit - localRay.Position).Length();
                if (dist < arrowDist)
                {
                    arrowDist = dist;
                    arrowAxis = i;
                    arrowPoint = hit;
                }
            }
        }

        int ringAxis = -1;
        float ringDist = float.MaxValue;
        if (!TerrainEditState.IsTerrainEditModeEnabled &&
            renderProxy != null &&
            renderProxy.RaycastRings(hitTestRay, out int localRing, out ringDist))
        {
            ringAxis = localRing + FirstRotateAxis;
        }

        bool preferArrow = false;
        if (arrowAxis >= 0 && ringAxis >= 0)
        {
            Vector3 radial = arrowPoint;
            Vector3 axis = TranslateLocalAxis(arrowAxis);
            radial -= axis * Vector3.Dot(radial, axis);
            float shaft = 0.07f * renderProxy.Scale;
            if (radial.LengthSquared() <= shaft * shaft && arrowDist <= ringDist + shaft)
                preferArrow = true;
        }

        if (ringAxis >= 0 && !preferArrow && (arrowAxis < 0 || ringDist < arrowDist))
            selectedAxis = ringAxis;
        else
            selectedAxis = arrowAxis;

        if (renderProxy != null)
            renderProxy.HighlightAxis = selectedAxis;

        if (selectedAxis >= FirstRotateAxis)
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Hand;
        else if (selectedAxis >= 0)
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.SizeAll;
        else
            System.Windows.Input.Mouse.OverrideCursor = null;

        return selectedAxis != -1;
    }

    private void ClearHover()
    {
        if (selectedAxis == -1)
            return;

        selectedAxis = -1;
        if (renderProxy != null)
            renderProxy.HighlightAxis = -1;
        System.Windows.Input.Mouse.OverrideCursor = null;
    }

    public override bool MouseDown(int x, int y, Ray rayCast, MouseButton button)
    {
        if (button == MouseButton.Left && selectedAxis != -1)
        {
            leftMouseDown = true;
            dragStartOrientation = renderProxy.Orientation;

            if (selectedAxis >= FirstRotateAxis)
            {
                BeginRotationDrag(rayCast);
                return true;
            }

            dragAxis = WorldDirection(TranslateLocalAxis(selectedAxis));
            dragOrigin = Transform.TranslationVector;
            dragPlaneNormal = TranslationPlaneNormal(dragAxis, dragOrigin);
            Plane axisPlane = new Plane(dragOrigin, dragPlaneNormal);
            if (!rayCast.Intersects(ref axisPlane, out Vector3 hit))
                hit = dragOrigin;
            dragGrab = Vector3.Dot(hit - dragOrigin, dragAxis);

            return true;
        }

        return false;
    }

    public override bool MouseMove(int x, int y, Ray rayCast)
    {
        if (leftMouseDown)
        {
            if (selectedAxis >= FirstRotateAxis)
            {
                UpdateRotationDrag(rayCast);
                return true;
            }

            moved = true;
            Plane axisPlane = new Plane(dragOrigin, dragPlaneNormal);
            if (rayCast.Intersects(ref axisPlane, out Vector3 hit))
            {
                float distance = Vector3.Dot(hit - dragOrigin, dragAxis);
                Matrix m = Transform;
                m.TranslationVector = dragOrigin + dragAxis * (distance - dragGrab);
                Transform = m;
            }

            return true;
        }

        return false;
    }

    public override bool MouseUp(int x, int y, Ray rayCast, MouseButton button)
    {
        if (leftMouseDown)
        {
            bool gizmoMoved = moved;

            moved = false;
            leftMouseDown = false;
            selectedAxis = -1;
            if (renderProxy != null)
                renderProxy.HighlightAxis = -1;
            System.Windows.Input.Mouse.OverrideCursor = null;

            return gizmoMoved;
        }

        return false;
    }

    private static Vector3 TranslateLocalAxis(int axis)
    {
        if (axis == 0) return Vector3.UnitZ;
        if (axis == 1) return Vector3.UnitY;
        return -Vector3.UnitX;
    }

    private Vector3 WorldDirection(Vector3 localDirection)
    {
        Vector3 world = Vector3.Transform(localDirection, renderProxy.Orientation);
        return world.LengthSquared() > 1e-8f ? Vector3.Normalize(world) : localDirection;
    }

    private void BeginRotationDrag(Ray ray)
    {
        Vector3 center = Transform.TranslationVector;
        Vector3 toCamera = Screens.LevelEditorScreen.EditorCamera.WorldMatrix.TranslationVector - center;
        dragEye = toCamera.LengthSquared() > 1e-8f ? Vector3.Normalize(toCamera) : Vector3.UnitZ;
        dragPointReady = false;

        dragLocalAxis = selectedAxis == 4 ? Vector3.UnitY : selectedAxis == 5 ? Vector3.UnitZ : Vector3.UnitX;
        dragAxis = WorldDirection(dragLocalAxis);
        // 圆环正对摄像机时用圆周角，转过之后改用屏幕切线，避免斜着的圆环一拖就跳。
        dragOrbit = Math.Abs(Vector3.Dot(dragAxis, dragEye)) > 0.92f;
        dragRadius = RotationRingMesh.AxisRadius * renderProxy.Scale;

        if (TryCameraPlanePoint(ray, center, dragEye, out Vector3 hit))
        {
            dragPointStart = hit - center;
            dragPointReady = dragPointStart.LengthSquared() > 1e-8f;
        }
    }

    private void UpdateRotationDrag(Ray ray)
    {
        Vector3 center = Transform.TranslationVector;
        if (!TryCameraPlanePoint(ray, center, dragEye, out Vector3 hit))
            return;

        Vector3 pointEnd = hit - center;
        if (!dragPointReady)
        {
            dragPointStart = pointEnd;
            dragPointReady = pointEnd.LengthSquared() > 1e-8f;
            return;
        }

        float angle;
        if (dragOrbit)
        {
            if (pointEnd.LengthSquared() < 1e-8f)
                return;
            angle = SignedAngle(Vector3.Normalize(dragPointStart), Vector3.Normalize(pointEnd), dragAxis);
        }
        else
        {
            Vector3 tangent = Vector3.Cross(dragAxis, dragEye);
            if (tangent.LengthSquared() < 1e-8f)
                return;
            tangent.Normalize();
            angle = Vector3.Dot(pointEnd - dragPointStart, tangent) / Math.Max(dragRadius, 1e-4f);
        }

        Quaternion delta = Quaternion.RotationAxis(dragLocalAxis, angle);
        // SharpDX 的四元数乘法与矩阵相反：绕局部轴是 start * local。
        renderProxy.Orientation = Quaternion.Normalize(Quaternion.Multiply(dragStartOrientation, delta));
        renderProxy.RebuildScaledTransform();
        moved = true;
    }

    private static bool TryCameraPlanePoint(Ray ray, Vector3 center, Vector3 eye, out Vector3 point)
    {
        point = center;
        float denom = Vector3.Dot(ray.Direction, eye);
        if (Math.Abs(denom) < 1e-6f)
            return false;

        float t = Vector3.Dot(center - ray.Position, eye) / denom;
        if (t < 0f)
            return false;

        point = ray.Position + ray.Direction * t;
        return true;
    }

    private Vector3 TranslationPlaneNormal(Vector3 axis, Vector3 origin)
    {
        Vector3 toCamera = Screens.LevelEditorScreen.EditorCamera.WorldMatrix.TranslationVector - origin;
        Vector3 normal = Vector3.Cross(axis, Vector3.Cross(axis, toCamera));
        if (normal.LengthSquared() < 1e-8f)
        {
            Vector3 helper = Math.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
            normal = Vector3.Cross(axis, helper);
        }

        return Vector3.Normalize(normal);
    }

    private static float SignedAngle(Vector3 from, Vector3 to, Vector3 axis)
    {
        return (float)Math.Atan2(Vector3.Dot(Vector3.Cross(from, to), axis), Vector3.Dot(from, to));
    }
}

public class SelectedEntityChangedEventArgs : EventArgs
{
    public Entity Entity { get; private set; }

    public SelectedEntityChangedEventArgs(Entity entity)
    {
        Entity = entity;
    }
}

public class LevelEditorScreen : DeferredRenderScreen2
{

    // 对极小屏幕占比的实例进行统一裁剪，避免 CPU/GPU 在海量小物体上浪费开销
    // 经验值：0.002 ~= 占屏幕宽高 0.2%
    private const float MinScreenSizeCull = 0.002f;

    public bool ShowTaskWindow { get; set; }

    /// <summary>
    /// 由 LevelEditor 在 Initialize 时设置，供 MeshView 等获取当前关卡 RootLayer（如地形实体）。
    /// </summary>
    public IEditorProvider EditorProvider { get; set; }

    /// <summary>
    /// 当前正在渲染的关卡视口。MeshView 写回地形时用它，不依赖有没有选中实体。
    /// </summary>
    public static LevelEditorScreen Current { get; private set; }

    // @hack: so that entities have access to the camera. Primarily for sprites
    public static LevelEditorCamera EditorCamera { get; private set; }

    private List<MeshRenderInstance> sprites = [];

    private Queue<RenderAction> renderTasks = new Queue<RenderAction>();
    private List<RenderProxy> proxies = [];

    /// <summary>放置取点的射线检测上限（世界单位），超出这个距离的模型不参与命中</summary>
    private const float PlacementPickMaxDistance = 250.0f;
    /// <summary>放置取点窄相位（逐三角形）的时间预算，超时退回包围盒结果，避免拖拽时掉帧</summary>
    private const double PlacementPickTriangleBudgetMs = 4.0;

    private struct PlacementPickCandidate
    {
        public ModelRenderProxy Proxy;
        public float NearDistance;
        public Vector3 BoxPoint;
    }

    private readonly List<PlacementPickCandidate> placementPickCandidates = [];

    // Havok 碰撞体叠加层（工具栏开关控制，见 LevelEditor.RegisterToolbarItems）
    private HavokOverlay havokOverlay;
    private bool havokOverlayDirty;
    private int lastHavokProxyCount = -1;

    private TranslateGizmo translateGizmo;
    private BindableDepthTexture gizmoDepthTexture;
    private SelectionHighlightPass selectionHighlight;
    private bool selectionHighlightUnavailable;
    private bool gizmoDragActive;
    private bool terrainPainting;
    private readonly Stopwatch terrainPaintClock = new Stopwatch();

    // 地形笔刷光标：鼠标在地形上时的投影圈（见 Render/TerrainBrushCursor.cs）
    private TerrainBrushCursor terrainBrushCursor;
    private bool terrainBrushCursorUnavailable;
    private TerrainEntity terrainBrushCursorTerrain;
    private bool terrainCursorValid;
    private int terrainCursorX;
    private int terrainCursorY;
    private bool terrainCursorHitCached;
    private Vector3 terrainCursorHitWorld;
    private Matrix terrainCursorHitViewProj;
    private Vector3 gizmoDragScale;
    private Quaternion gizmoDragRotation;
    private Vector3 gizmoDragTranslation;

    public event EventHandler<SelectedEntityChangedEventArgs> SelectedEntityChanged;

    public LevelEditorScreen(bool groundVisibleByDefault = false)
    {
        GroundVisible = groundVisibleByDefault;
        translateGizmo = new TranslateGizmo();

        renderTasks.Enqueue(state =>
        {
            translateGizmo.CreateRenderProxy(state);

        });
    }

    public void AddEntity(Entity entity)
    {
        renderTasks.Enqueue((RenderCreateState state) =>
        {
            List<RenderProxy> newProxies = [];
            entity.CreateRenderProxy(newProxies, state);
            proxies.AddRange(newProxies);
        });

        havokOverlayDirty = true;
    }

    /// <summary>
    /// 是否在关卡视口里叠加显示 Havok 物理碰撞体（几何来自 ObjectBlueprint 引用的 HavokPhysicsData res）。
    /// </summary>
    public bool ShowHavokCollision
    {
        get => LevelEditorRenderingOptions.ShowHavokCollision;
        set
        {
            if (LevelEditorRenderingOptions.ShowHavokCollision == value)
                return;

            LevelEditorRenderingOptions.ShowHavokCollision = value;
            havokOverlayDirty = true;
        }
    }

    /// <summary>关卡内容变化（实体增删/重新载入）后调用，让叠加层重新收集实例。</summary>
    public void RefreshHavokOverlay() => havokOverlayDirty = true;

    /// <summary>
    /// 叠加层的生命周期：开关打开时懒创建，分帧解析碰撞资源，关掉时只停止绘制（缓冲保留，避免反复重解析）。
    /// </summary>
    private void UpdateHavokOverlay()
    {
        if (!LevelEditorRenderingOptions.ShowHavokCollision || Viewport?.Device == null || Viewport.Context == null)
            return;

        havokOverlay ??= new HavokOverlay(Viewport.Device);

        // Proxy 是在 Render 阶段（renderTasks 里）建出来的，所以除了 dirty 标记还要看数量变化，
        // 否则刚 AddEntity 完的那一批会漏掉。
        if (havokOverlayDirty || proxies.Count != lastHavokProxyCount)
        {
            lastHavokProxyCount = proxies.Count;
            havokOverlay.Rebuild(proxies);
            havokOverlayDirty = false;
        }

        havokOverlay.Pump();
    }

    public void DeleteEntityFromRenderProxy(Entity entity)
    {
        renderTasks.Enqueue(state =>
        {
            var foundProxies = proxies.Where(rp => rp.OwnerEntity.Owner == entity).ToList();
            if (foundProxies == null)
                return;

            foreach (RenderProxy renderProxy in foundProxies)
            {
                proxies.Remove(renderProxy);
            }
        });

        havokOverlayDirty = true;
    }

    public override void Update(double timestep)
    {
        Current = this;
        RenderMode = LevelEditorRenderingOptions.GetDebugRenderMode();

        base.Update(timestep);

        UpdateHavokOverlay();

        if (TerrainEditState.IsTerrainEditModeEnabled)
            translateGizmo.IsVisible = false;
        else if (!translateGizmo.IsDragging)
        {
            if (selectedProxies.Count > 0 && selectedProxies[0].OwnerEntity.RequiresTransformUpdate)
            {
                if (selectedProxies[0].OwnerEntity.Owner is ISpatialEntity spatialEntity)
                    translateGizmo.SetPose(spatialEntity.GetTransform());
            }
        }

        translateGizmo.Update(RenderCreateState);

        foreach (RenderProxy proxy in proxies)
            proxy.Update(RenderCreateState);
    }

    public void CaptureThumbnail(string path)
    {
        Render();

        {
            D3D11.Texture2D texture = Viewport.ColorBuffer;
            D3D11.Texture2DDescription description = texture.Description;

            D3D11.Texture2D textureCopy = new D3D11.Texture2D(Viewport.Device, new D3D11.Texture2DDescription
            {
                Width = (int)description.Width,
                Height = (int)description.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = description.Format,
                Usage = D3D11.ResourceUsage.Staging,
                SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                BindFlags = D3D11.BindFlags.None,
                CpuAccessFlags = D3D11.CpuAccessFlags.Read,
                OptionFlags = D3D11.ResourceOptionFlags.None
            });
            Viewport.Context.CopyResource(texture, textureCopy);

            DataStream dataStream;
            DataBox dataBox = Viewport.Context.MapSubresource(
                textureCopy,
                0,
                0,
                D3D11.MapMode.Read,
                D3D11.MapFlags.None,
                out dataStream);

            byte[] buffer = new byte[description.Width * description.Height * 4];
            for (int i = 0; i < description.Height; i++)
            {
                dataStream.Read(buffer, i * description.Width * 4, description.Width * 4);
                dataStream.Position += dataBox.RowPitch - (description.Width * 4);
            }
            for (int i = 3; i < buffer.Length; i += 4)
            {
                buffer[i] = 0xFF;
            }

            ImageUtils.SaveToPNG(description.Width, description.Height, buffer, new FileStream(path, FileMode.Create));

            Viewport.Context.UnmapSubresource(textureCopy, 0);
            textureCopy.Dispose();
        }
    }

    public override void Render()
    {
        if (renderTasks.Count > 0)
        {
            if (ShowTaskWindow)
            {
                // 每帧只处理一个任务，避免长时间阻塞 UI，加快首帧显示
                RenderAction action = renderTasks.Dequeue();
                action.Invoke(RenderCreateState);
                if (renderTasks.Count == 0)
                    ShowTaskWindow = false;
            }
            else
            {
                ProcessRenderTasks();
            }
        }

        VirtualTexturePage.BeginFrame();
        VirtualTexturePage.PumpBake(Viewport.Context, RenderCreateState);
        gizmoDepthTexture.Clear(Viewport.Context, true, true, 1.0f, 0);
        base.Render();
    }

    public override void CreateSizeDependentBuffers()
    {
        base.CreateSizeDependentBuffers();

        D3D11.Texture2DDescription description = new D3D11.Texture2DDescription
        {
            ArraySize = 1,
            Format = SharpDX.DXGI.Format.R24G8_Typeless,
            Width = Viewport.ViewportWidth,
            Height = Viewport.ViewportHeight,
            MipLevels = 1,
            SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
            Usage = D3D11.ResourceUsage.Default
        };
        D3D11.DepthStencilViewDescription dsViewDesc = new D3D11.DepthStencilViewDescription
        {
            Dimension = D3D11.DepthStencilViewDimension.Texture2D,
            Format = SharpDX.DXGI.Format.D24_UNorm_S8_UInt,
            Texture2D = default(D3D11.DepthStencilViewDescription.Texture2DResource)
        };
        gizmoDepthTexture = new BindableDepthTexture(Viewport.Device, description, false, dsViewDesc);
    }

    public override void DisposeSizeDependentBuffers()
    {
        base.DisposeSizeDependentBuffers();
        gizmoDepthTexture.Dispose();
    }

    public override void DisposeBuffers()
    {
        base.DisposeBuffers();

        translateGizmo.Dispose();

        selectionHighlight?.Dispose();
        selectionHighlight = null;
        selectionHighlightUnavailable = false;

        havokOverlay?.Dispose();
        havokOverlay = null;
        havokOverlayDirty = true;
        lastHavokProxyCount = -1;

        terrainBrushCursor?.Dispose();
        terrainBrushCursor = null;
        terrainBrushCursorUnavailable = false;
        terrainBrushCursorTerrain = null;
        terrainCursorHitCached = false;

        foreach (RenderProxy proxy in proxies)
            proxy.Dispose();

        proxies.Clear();
        selectedProxies.Clear();

        SolidColorMeshWrapper.DisposeSharedState();
        if (Current == this)
            Current = null;
    }

    public override void CreateBuffers()
    {
        if (camera == null)
        {
            camera = new LevelEditorCamera();
            camera.SetViewParams(new Vector3(0, 0, 0), new Vector3(0, 0, 0));

            SetCamera();
        }

        base.CreateBuffers();

        ShadowsEnabled = false;
        HBAOEnabled = false;
    }

    // @hack: so that entities have access to the camera. Primarily for sprites
    public void SetCamera()
    {
        EditorCamera = camera as LevelEditorCamera;
    }

    List<RenderProxy> selectedProxies = [];

    public void ClearSelection()
    {
        translateGizmo.IsVisible = false;
        renderTasks.Enqueue((state) =>
        {
            foreach (RenderProxy proxy in selectedProxies)
                proxy.SetSelected(state, false);
            selectedProxies.Clear();
        });

        MainWindow.UpdateSelectedEntity(null, this);
    }

    public void SelectEntity(Entity entity)
    {
        ClearSelection();

        // proxies 由渲染线程增删，这里在 UI 线程做快照再筛选，避免「集合已修改」
        RenderProxy[] snapshot;
        try
        {
            snapshot = proxies.ToArray();
        }
        catch (Exception)
        {
            return;
        }

        List<RenderProxy> foundProxies = snapshot.Where(rp => rp.OwnerEntity?.Owner == entity).ToList();
        if (foundProxies.Count == 0)
            return;

        // 显示位移控制器（包括 StaticModelGroup 子元素，现已支持修改）
        if (entity is ISpatialEntity spatialEntity)
        {
            translateGizmo.SetPose(spatialEntity.GetTransform());
            translateGizmo.IsVisible = true;
        }

        renderTasks.Enqueue((state) =>
        {
            selectedProxies.AddRange(foundProxies);
            foreach (RenderProxy proxy in selectedProxies)
                proxy.SetSelected(state, true);
        });

        // 2026/01/18
        // 改动标记
        MainWindow.UpdateSelectedEntity(entity, this);
    }

    public void CenterOnSelection()
    {
        if (selectedProxies.Count == 0)
            return;

        BoundingBox totalBbox = new BoundingBox();
        int index = 0;

        foreach (RenderProxy proxy in selectedProxies)
        {
            if (index == 0) totalBbox = proxy.BoundingBox;
            else totalBbox = BoundingBox.Merge(totalBbox, proxy.BoundingBox);
            index++;
        }

        Vector3 center = totalBbox.Minimum + (totalBbox.Maximum - totalBbox.Minimum) * 0.5f;
        Vector3 offset = center + new Vector3(0, Math.Abs(totalBbox.Maximum.Y - totalBbox.Minimum.Y) / 1.25f, (totalBbox.Maximum - totalBbox.Minimum).Length() * 0.9f);

        (camera as FirstPersonCamera).SetViewParams(offset * new Vector3(-1, 1, 1), center * new Vector3(-1, 1, 1));
    }

    /// <summary>
    /// 每帧调用
    /// </summary>
    public override List<MeshRenderInstance> CollectMeshInstances()
    {

        D3DUtils.BeginPerfEvent(Viewport.Context, "CollectMeshInstances_LevelEditor");

        LevelEditorCamera currentCamera = (LevelEditorCamera)camera;
        Matrix worldMatrix = currentCamera.WorldMatrix;

        BoundingFrustum cameraFrustum = BoundingFrustum.FromCamera(worldMatrix.TranslationVector, worldMatrix.Backward, worldMatrix.Down, currentCamera.FOV, currentCamera.GetNearClip(), currentCamera.GetFarClip(), currentCamera.AspectRatio);
        List<MeshRenderInstance> instancesInView = [];

        sprites.Clear();
        foreach (RenderProxy proxy in proxies)
        {
            float dist = (proxy.Transform.TranslationVector - worldMatrix.TranslationVector).Length();
            float width = cameraFrustum.GetWidthAtDepth(dist);
            float height = cameraFrustum.GetHeightAtDepth(dist);

            // 使用缓存的包围球，避免每帧为每个 Proxy 重新 FromBox
            BoundingSphere bSphere = proxy.BoundingSphere;
            float wratio = (bSphere.Radius * 2) / width;
            float hratio = (bSphere.Radius * 2) / height;
            float screenSize = (wratio > hratio) ? wratio : hratio;

            // 全局极小物体裁剪：不依赖具体 Proxy，只看屏幕占比
            if (screenSize < MinScreenSizeCull)
                continue;

            if (proxy.ShouldRender(dist, screenSize))
            {
                ContainmentType containmentType = cameraFrustum.Contains(proxy.BoundingBox);
                if (containmentType == ContainmentType.Contains || containmentType == ContainmentType.Intersects)
                {
                    proxy.CurrentDistanceToCamera = dist;

                    if (proxy is SpriteRenderProxy spriteRenderProxy)
                    {
                        sprites.Add(proxy.GetInstance(dist));

                        // 尝试获取模型渲染实例
                        if (spriteRenderProxy.GetMeshRenderInstance(out MeshRenderInstance instance))
                        {
                            instancesInView.Add(instance);
                        }
                    }
                    else
                    {
                        instancesInView.Add(proxy.GetInstance(dist));
                    }
                }
            }
        }

        D3DUtils.EndPerfEvent(Viewport.Context);

        return instancesInView;
    }

    /// <summary>
    /// 纯色模式下，SetMeshResPath 的 mesh 以裸 MeshLodRenderable 加入列表，此处为其设置共享 LevelShader 后再绘制，从而随 view mode 切换管线。
    /// 对延迟渲染路径进行按距离排序渲染，减小过度绘制；其它路径沿用基础实现。
    /// </summary>
    protected override void RenderMeshes(MeshRenderPath renderPath, List<MeshRenderInstance> meshList)
    {
        // 仅在延迟渲染路径上做类型 + 距离排序以提升 Early-Z 与状态切换效率
        if (renderPath == MeshRenderPath.Deferred)
        {
            D3DUtils.BeginPerfEvent(Viewport.Context, "RenderMeshes_Sorted");
            {
                // 先按 RenderMesh 类型分组（Terrain -> Model -> 其它），再按距离从近到远排序
                int GetRenderCategory(MeshRenderInstance inst)
                {
                    if (inst.RenderMesh is TerrainRenderProxy) return 0;
                    if (inst.RenderMesh is ModelRenderProxy) return 1;
                    return 2;
                };

                meshList.Sort((a, b) =>
                {
                    int ca = GetRenderCategory(a);
                    int cb = GetRenderCategory(b);
                    if (ca != cb)
                        return ca.CompareTo(cb);

                    float da = (a.RenderMesh as RenderProxy)?.CurrentDistanceToCamera ?? 0.0f;
                    float db = (b.RenderMesh as RenderProxy)?.CurrentDistanceToCamera ?? 0.0f;
                    return da.CompareTo(db);
                });

                foreach (MeshRenderInstance mesh in meshList)
                {
                    D3DUtils.BeginPerfEvent(Viewport.Context, mesh.RenderMesh.DebugName);
                    {
                        Matrix transform = mesh.Transform;
                        transform.Transpose();

                        functionConstants.UpdateData(Viewport.Context, new FunctionConstants()
                        {
                            WorldMatrix = Matrix.Scaling(-1, 1, 1) * transform,
                            LightProbe1 = SHLightProbe[0],
                            LightProbe2 = SHLightProbe[1],
                            LightProbe3 = SHLightProbe[2],
                            LightProbe4 = SHLightProbe[3],
                            LightProbe5 = SHLightProbe[4],
                            LightProbe6 = SHLightProbe[5],
                            LightProbe7 = SHLightProbe[6],
                            LightProbe8 = SHLightProbe[7],
                            LightProbe9 = SHLightProbe[8],
                        });

                        Viewport.Context.VertexShader.SetConstantBuffer(1, functionConstants.Buffer);
                        Viewport.Context.PixelShader.SetConstantBuffer(1, functionConstants.Buffer);

                        if (mesh.RenderMesh is MeshLodRenderable)
                            SolidColorMeshWrapper.SetSharedShaderState(Viewport.Context, renderPath);

                        mesh.RenderMesh.Render(Viewport.Context, renderPath);
                    }
                    D3DUtils.EndPerfEvent(Viewport.Context);
                }
            }
            D3DUtils.EndPerfEvent(Viewport.Context);
        }
        else
        {
            // Forward / Shadows / Selection 路径保留基础行为（用于 Sprite、Gizmo 等）
            base.RenderMeshes(renderPath, meshList);

        }
    }

    protected List<MeshRenderInstance> GetSortedVisibleList()
    {
        LevelEditorCamera currentCamera = (LevelEditorCamera)camera;
        Matrix worldMatrix = currentCamera.WorldMatrix;

        BoundingFrustum cameraFrustum = BoundingFrustum.FromCamera(worldMatrix.TranslationVector, worldMatrix.Backward, worldMatrix.Down, currentCamera.FOV, currentCamera.GetNearClip(), currentCamera.GetFarClip(), currentCamera.AspectRatio);
        List<MeshRenderInstance> instancesInView = [];

        // 点击选择也跑在 UI 线程，而 proxies 由渲染线程增删，同样先取快照
        RenderProxy[] snapshot;
        try
        {
            snapshot = proxies.ToArray();
        }
        catch (Exception)
        {
            return instancesInView;
        }

        foreach (RenderProxy proxy in snapshot)
        {
            float dist = (proxy.Transform.TranslationVector - worldMatrix.TranslationVector).Length();
            float width = cameraFrustum.GetWidthAtDepth(dist);
            float height = cameraFrustum.GetHeightAtDepth(dist);

            BoundingSphere bSphere = BoundingSphere.FromBox(proxy.BoundingBox);
            float wratio = (bSphere.Radius * 2) / width;
            float hratio = (bSphere.Radius * 2) / height;
            float screenSize = (wratio > hratio) ? wratio : hratio;

            if (proxy.ShouldRender(dist, screenSize))
            {
                ContainmentType containmentType = cameraFrustum.Contains(proxy.BoundingBox);
                if (containmentType == ContainmentType.Contains || containmentType == ContainmentType.Intersects)
                {
                    proxy.CurrentDistanceToCamera = dist;
                    instancesInView.Add(proxy.GetInstance(dist));
                }
            }
        }

        instancesInView.Sort((a, b) =>
        {
            return (a.RenderMesh as RenderProxy).CurrentDistanceToCamera.CompareTo((b.RenderMesh as RenderProxy).CurrentDistanceToCamera);
        });

        return instancesInView;
    }

    private struct MouseButtonData
    {
        public int X;
        public int Y;
        public MouseButton Button;
        public bool MouseDown;
    }
    private MouseButtonData mouseButtonData;

    public override void MouseDown(int x, int y, MouseButton button)
    {
        // 素材库的点击放置模式：先吞掉这次左键点击，避免触发选择/gizmo
        if (button == MouseButton.Left &&
            EditorProvider is LevelEditor levelEditor &&
            levelEditor.PendingPlacement != null)
        {
            levelEditor.PlaceAt(x, y);
            return;
        }

        if (TerrainEditState.IsTerrainEditModeEnabled)
        {
            UpdateTerrainCursor(x, y);

            if (button == MouseButton.Left)
            {
                terrainPainting = true;
                terrainPaintClock.Restart();
                TerrainBrush.Apply(this, GetHitTestRay(x, y), 0.05f);
                return;
            }

            base.MouseDown(x, y, button);
            return;
        }

        if (translateGizmo.MouseDown(x, y, GetHitTestRay(x, y), button))
        {
            CaptureGizmoDrag();
            return;
        }

        if (button == MouseButton.Left)
        {
            if (!mouseButtonData.MouseDown)
            {
                mouseButtonData = new MouseButtonData() { X = x, Y = y, Button = button, MouseDown = true };
            }
        }

        base.MouseDown(x, y, button);
    }

    public override void MouseUp(int x, int y, MouseButton button)
    {
        if (TerrainEditState.IsTerrainEditModeEnabled)
        {
            if (button == MouseButton.Left)
            {
                terrainPainting = false;
                terrainPaintClock.Reset();
                mouseButtonData.MouseDown = false;
                return;
            }

            base.MouseUp(x, y, button);
            return;
        }

        bool rotating = translateGizmo.IsRotating;
        if (translateGizmo.MouseUp(x, y, GetHitTestRay(x, y), button))
        {
            ApplyGizmoToSelection(commit: true, rotating);
            return;
        }

        base.MouseUp(x, y, button);
        if (mouseButtonData.MouseDown)
        {
            if (x == mouseButtonData.X && y == mouseButtonData.Y && button == mouseButtonData.Button)
            {
                Ray ray = GetHitTestRay(x, y);

                List<MeshRenderInstance> visibleInstances = GetSortedVisibleList();
                List<Tuple<MeshRenderInstance, float>> hits = [];

                foreach (MeshRenderInstance instance in visibleInstances)
                {
                    RenderProxy instProxy = instance.RenderMesh as RenderProxy;
                    BoundingBox bbox = instProxy.BoundingBox;

                    Vector3 outPosition;
                    if (ray.Intersects(ref bbox, out outPosition))
                    {
                        if (instProxy.HitTest(ray, out outPosition))
                        {
                            hits.Add(new Tuple<MeshRenderInstance, float>(instance, (outPosition - (camera as LevelEditorCamera).WorldMatrix.TranslationVector).Length()));
                        }
                    }
                }

                RenderProxy proxy = null;
                RenderProxy prevSelection = selectedProxies.FirstOrDefault();

                if (hits.Count > 0)
                {
                    hits.Sort((a, b) => { return a.Item2.CompareTo(b.Item2); });
                    proxy = hits[0].Item1.RenderMesh as RenderProxy;

                }

                Entity entityToSelect = (proxy != null)
                        ? proxy.OwnerEntity.Owner
                        : null;

                SelectedEntityChanged?.Invoke(this, new SelectedEntityChangedEventArgs(entityToSelect));
            }
        }

        mouseButtonData.MouseDown = false;
    }

    public override void MouseMove(int x, int y)
    {
        // 素材库的点击放置模式：预览模型跟随光标，不再处理 gizmo 拖动
        if (EditorProvider is LevelEditor levelEditor && levelEditor.PendingPlacement != null)
        {
            levelEditor.UpdatePlacementPreview(x, y);
            base.MouseMove(x, y);
            return;
        }

        if (TerrainEditState.IsTerrainEditModeEnabled)
        {
            UpdateTerrainCursor(x, y);

            if (terrainPainting)
            {
                float dt = (float)terrainPaintClock.Elapsed.TotalSeconds;
                terrainPaintClock.Restart();
                if (dt > 0.05f)
                    dt = 0.05f;
                if (dt > 0.0f)
                    TerrainBrush.Apply(this, GetHitTestRay(x, y), dt);
                return;
            }

            base.MouseMove(x, y);
            return;
        }

        base.MouseMove(x, y);

        // 普通模式下保持原有 Gizmo 行为：修改实体 Transform
        if (!translateGizmo.MouseMove(x, y, GetHitTestRay(x, y)))
        {
            Ray ray = GetHitTestRay(x, y);
            if (translateGizmo.HitTest(ray))
            {
                // do something
            }
        }
        else if (gizmoDragActive && translateGizmo.IsMoving)
        {
            ApplyGizmoToSelection(commit: false, rotating: translateGizmo.IsRotating);
        }
    }

    public override void MouseLeave()
    {
        base.MouseLeave();
        terrainCursorValid = false;
        terrainCursorHitCached = false;
        terrainBrushCursor?.Clear();
    }

    public override void MouseScroll(int delta)
    {
        base.MouseScroll(delta);
        // 滚轮会前后移动摄像机，射线落点必然变化
        terrainCursorHitCached = false;
    }

    /// <summary>记录光标位置，标记下一帧需要重新求交（圈的实际几何在渲染线程按地形高度重建）。</summary>
    private void UpdateTerrainCursor(int x, int y)
    {
        if (!terrainCursorValid || terrainCursorX != x || terrainCursorY != y)
        {
            terrainCursorValid = true;
            terrainCursorX = x;
            terrainCursorY = y;
            terrainCursorHitCached = false;
        }
    }

    /// <summary>
    /// 在地形上画笔刷作用范围圈。位置取自鼠标射线与地形的交点，
    /// 每段顶点的高度都从地形高度场采样，所以圈会贴合高低起伏。
    /// 画在场景深度之后：会被山体正确遮挡，又不会盖住 gizmo。
    /// </summary>
    private void DrawTerrainBrushCursor()
    {
        if (!TerrainEditState.IsTerrainEditModeEnabled || !terrainCursorValid)
        {
            terrainBrushCursor?.Clear();
            return;
        }

        try
        {
            if (!EnsureTerrainBrushCursor())
                return;

            TerrainEntity terrain = GetTerrainForCursor();
            if (terrain?.Terrain?.TerrainData == null)
            {
                terrainBrushCursor.Clear();
                return;
            }

            if (!TryGetTerrainCursorHit(terrain, out Vector3 worldHit))
            {
                terrainBrushCursor.Clear();
                return;
            }

            terrainBrushCursor.Update(terrain, worldHit, camera.GetEyePt(), TerrainEditState.BrushRadius, TerrainEditState.BrushFeather);
            terrainBrushCursor.Draw(Viewport.Context, camera.GetViewProjMatrix(), Viewport.DepthBufferDSV);
        }
        catch (Exception e)
        {
            // chunk 由流式加载在别的线程增删，这里兜底一次，不能因为光标把编辑器崩掉
            terrainBrushCursor?.Clear();
            LogTerrainCursorFailure(e);
        }
    }

    private bool TryGetTerrainCursorHit(TerrainEntity terrain, out Vector3 worldHit)
    {
        worldHit = default;

        // 拖动笔刷时地形高度一直在变，必须每帧重新求交；其余情况按「鼠标/摄像机是否变了」缓存
        if (!terrainPainting && terrainCursorHitCached)
        {
            Matrix currentViewProj = camera.GetViewProjMatrix();
            if (IsSameMatrix(currentViewProj, terrainCursorHitViewProj))
            {
                worldHit = terrainCursorHitWorld;
                return true;
            }
        }

        if (!TerrainBrush.TryHit(terrain, GetHitTestRay(terrainCursorX, terrainCursorY), out Vector3 _, out worldHit))
        {
            terrainCursorHitCached = false;
            return false;
        }

        terrainCursorHitWorld = worldHit;
        terrainCursorHitViewProj = camera.GetViewProjMatrix();
        terrainCursorHitCached = true;
        return true;
    }

    private static bool IsSameMatrix(Matrix a, Matrix b)
    {
        return a.M11 == b.M11 && a.M12 == b.M12 && a.M13 == b.M13 && a.M14 == b.M14
            && a.M21 == b.M21 && a.M22 == b.M22 && a.M23 == b.M23 && a.M24 == b.M24
            && a.M31 == b.M31 && a.M32 == b.M32 && a.M33 == b.M33 && a.M34 == b.M34
            && a.M41 == b.M41 && a.M42 == b.M42 && a.M43 == b.M43 && a.M44 == b.M44;
    }

    private TerrainEntity GetTerrainForCursor()
    {
        if (terrainBrushCursorTerrain == null || terrainBrushCursorTerrain.Terrain?.TerrainData == null)
            terrainBrushCursorTerrain = TerrainBrush.FindTerrain(this);

        return terrainBrushCursorTerrain;
    }

    private bool EnsureTerrainBrushCursor()
    {
        if (terrainBrushCursor != null && !terrainBrushCursor.IsDisposed)
            return true;

        if (terrainBrushCursorUnavailable)
            return false;

        try
        {
            terrainBrushCursor = new TerrainBrushCursor(Viewport.Device);
            return true;
        }
        catch (Exception e)
        {
            Frosty.Core.App.Logger?.LogError($"[地形笔刷] 创建光标渲染器失败，将不显示笔刷圈：{e.Message}");
            terrainBrushCursorUnavailable = true;
            return false;
        }
    }

    private bool terrainCursorFailureLogged;

    private void LogTerrainCursorFailure(Exception e)
    {
        if (terrainCursorFailureLogged)
            return;

        terrainCursorFailureLogged = true;
        Frosty.Core.App.Logger?.LogWarning($"[地形笔刷] 笔刷圈绘制失败（已隐藏光标）：{e.Message}");
    }

    private void CaptureGizmoDrag()
    {
        gizmoDragActive = false;
        if (selectedProxies.Count == 0)
            return;
        if (selectedProxies[0].OwnerEntity.Owner is not ISpatialEntity spatialEntity)
            return;

        spatialEntity.GetLocalTransform().Decompose(out gizmoDragScale, out gizmoDragRotation, out gizmoDragTranslation);
        gizmoDragActive = true;
    }

    private void ApplyGizmoToSelection(bool commit, bool rotating)
    {
        if (!gizmoDragActive || selectedProxies.Count == 0)
            return;
        if (selectedProxies[0].OwnerEntity.Owner is not ISpatialEntity spatialEntity)
            return;

        // 拖动时 gizmo 朝向已经是物体的世界旋转，写回时换算成局部旋转。
        Quaternion rotation = rotating
            ? WorldToLocalRotation(spatialEntity, translateGizmo.Orientation)
            : gizmoDragRotation;
        Vector3 translation = rotating
            ? gizmoDragTranslation
            : translateGizmo.Transform.TranslationVector;

        Matrix m = Matrix.Scaling(gizmoDragScale) * Matrix.RotationQuaternion(rotation) * Matrix.Translation(translation);
        spatialEntity.SetTransform(m, suppressUpdate: !commit);

        foreach (RenderProxy proxy in selectedProxies)
            proxy.OwnerEntity.RequiresTransformUpdate = true;

        MainWindow.UpdateEntityTransform();
    }

    private static Quaternion WorldToLocalRotation(ISpatialEntity spatialEntity, Quaternion worldRotation)
    {
        if (spatialEntity is Entity entity && entity.Parent is ISpatialEntity parent
            && parent.GetTransform().Decompose(out _, out Quaternion parentRotation, out _))
        {
            return Quaternion.Normalize(Quaternion.Multiply(worldRotation, Quaternion.Invert(parentRotation)));
        }

        return worldRotation;
    }

    protected override void PostProcessEditorComposite()
    {
        base.PostProcessEditorComposite();

        sprites.Sort((a, b) =>
        {
            SpriteRenderProxy spriteA = a.RenderMesh as SpriteRenderProxy;
            SpriteRenderProxy spriteB = b.RenderMesh as SpriteRenderProxy;

            return spriteB.CurrentDistanceToCamera.CompareTo(spriteA.CurrentDistanceToCamera);
        });

        D3D11.DepthStencilView oldDsv;
        D3D11.RenderTargetView[] rts = Viewport.Context.OutputMerger.GetRenderTargets(6, out oldDsv);

        // Regular elements
        Viewport.Context.OutputMerger.SetRenderTargets(Viewport.DepthBufferDSV, rts);
        Viewport.Context.OutputMerger.DepthStencilState = D3DUtils.CreateDepthStencilState(true, D3D11.DepthWriteMask.Zero, depthComparison: D3D11.Comparison.LessEqual);
        Viewport.Context.OutputMerger.BlendState = D3DUtils.CreateBlendState(D3DUtils.CreateBlendStateRenderTarget(alphaBlend: true));

        Viewport.Context.VertexShader.SetConstantBuffer(0, viewConstants.Buffer);

        Viewport.Context.PixelShader.SetConstantBuffer(0, viewConstants.Buffer);
        Viewport.Context.PixelShader.SetShaderResource(0, normalBasisCubemapTexture.SRV);
        Viewport.Context.PixelShader.SetSampler(0, D3DUtils.CreateSamplerState(D3D11.TextureAddressMode.Clamp, filter: D3D11.Filter.MinMagMipPoint));

        RenderMeshes(MeshRenderPath.Forward, sprites);

        // 地形笔刷圈：同样画在场景深度上（只读深度 + alpha 混合），所以会被山体正确遮挡
        DrawTerrainBrushCursor();

        // Foreground elements
        Viewport.Context.OutputMerger.SetRenderTargets(gizmoDepthTexture.DSV, rts);
        Viewport.Context.OutputMerger.DepthStencilState = D3DUtils.CreateDepthStencilState(true, depthComparison: D3D11.Comparison.LessEqual);
        Viewport.Context.OutputMerger.BlendState = D3DUtils.CreateBlendState(D3DUtils.CreateBlendStateRenderTarget());

        // Havok 碰撞体叠加：用这张只含前景的深度缓冲，所以永远画在模型上面；
        // 放在 gizmo 之前，保证位移/旋转手柄始终在最上层。
        if (havokOverlay != null && LevelEditorRenderingOptions.ShowHavokCollision)
        {
            havokOverlay.Draw(Viewport.Context, camera.GetViewProjMatrix(), gizmoDepthTexture.DSV, camera.GetEyePt());
        }

        List<MeshRenderInstance> instances = [];
        if (translateGizmo.IsVisible)
            instances.Add(translateGizmo.GetInstance());

        RenderMeshes(MeshRenderPath.Forward, instances);

        Viewport.Context.OutputMerger.SetRenderTargets(oldDsv, rts);
    }

    protected override void PostProcessSelectionOutline()
    {
        // 自带的 SelectionOutline 用“世界深度差 / 10000”判断边缘，而关卡相机的远裁剪面是 1000，
        // 这个阈值永远触发不了，所以换成插件里的屏幕空间描边（见 Render/SelectionHighlightPass.cs）。
        RenderSelectionDepthForSelection();

        if (!EnsureSelectionHighlight())
        {
            base.PostProcessSelectionOutline();
            return;
        }

        selectionHighlight.Render(
            Viewport.Context,
            vsFullscreenQuad,
            commonConstants.Buffer,
            selectionOutlineTexture,
            finalColorTexture.SRV,
            selectionDepthTexture,
            Viewport.DepthBufferSRV,
            Viewport.ViewportWidth,
            Viewport.ViewportHeight);
    }

    /// <summary>
    /// 兼容版 Frosty 1.0.6.3 的基类只会把 meshes[0] 写进选择遮罩（Frosty2000 核心新增的
    /// CollectSelectionMeshInstances 虚方法在这里并不存在）。关卡编辑器有上千个实例，
    /// meshes[0] 通常不是选中的物体，所以这里用当前选中的 RenderProxy 重新画一遍选择遮罩。
    /// </summary>
    private void RenderSelectionDepthForSelection()
    {
        if (selectedProxies.Count == 0)
            return;

        D3DUtils.BeginPerfEvent(Viewport.Context, "CollectSelections");
        try
        {
            // 和基类的 PostProcessCollectSelections 保持一致
            UpdateViewConstants(false);

            selectionDepthTexture.Clear(Viewport.Context, true, true, 1.0f, 0);
            Viewport.Context.OutputMerger.SetRenderTargets(selectionDepthTexture.DSV, null, null);
            Viewport.Context.OutputMerger.DepthStencilState = D3DUtils.CreateDepthStencilState(depthComparison: D3D11.Comparison.LessEqual);
            Viewport.Context.OutputMerger.BlendState = D3DUtils.CreateBlendState(D3DUtils.CreateBlendStateRenderTarget());
            Viewport.Context.Rasterizer.State = D3DUtils.CreateRasterizerState(D3D11.CullMode.Front, depthClip: true);

            Viewport.Context.VertexShader.SetConstantBuffer(0, viewConstants.Buffer);

            Viewport.Context.PixelShader.SetConstantBuffer(0, viewConstants.Buffer);
            Viewport.Context.PixelShader.SetShaderResource(0, normalBasisCubemapTexture.SRV);
            Viewport.Context.PixelShader.SetSampler(0, D3DUtils.CreateSamplerState(D3D11.TextureAddressMode.Clamp, filter: D3D11.Filter.MinMagMipPoint));

            List<MeshRenderInstance> selectionInstances = [];
            foreach (RenderProxy proxy in selectedProxies)
            {
                // 只有模型代理支持 Selection 通道；Sprite/Gizmo 不参与描边
                if (proxy is ModelRenderProxy)
                    selectionInstances.Add(proxy.GetInstance(proxy.CurrentDistanceToCamera));
            }

            if (selectionInstances.Count > 0)
                RenderMeshes(MeshRenderPath.Selection, selectionInstances);
        }
        finally
        {
            D3DUtils.EndPerfEvent(Viewport.Context);
        }
    }

    private bool EnsureSelectionHighlight()
    {
        if (selectionHighlight != null && !selectionHighlight.IsDisposed)
            return selectionHighlight.IsReady;

        // 编译/加载失败只试一次，之后静默退回自带实现，避免每帧刷日志
        if (selectionHighlightUnavailable)
            return false;

        selectionHighlight = new SelectionHighlightPass(Viewport.Device);
        if (!selectionHighlight.IsReady)
        {
            selectionHighlight.Dispose();
            selectionHighlight = null;
            selectionHighlightUnavailable = true;
            return false;
        }

        return true;
    }

    /// <summary>
    /// 把视口坐标转成世界落点，供素材库放置使用。
    /// 依次考虑：地形网格 → 视口里已有的模型（物件/建筑表面）→ Y=0 平面 → 相机前方 20m。
    /// </summary>
    public bool TryGetPlacementPoint(int x, int y, out Vector3 worldPoint, out Vector3 worldNormal)
    {
        worldPoint = Vector3.Zero;
        worldNormal = Vector3.Up;

        if (camera == null || Viewport == null)
            return false;

        Ray ray = GetHitTestRay(x, y);
        if (ray.Direction.LengthSquared() < 1e-8f || float.IsNaN(ray.Direction.X))
            return false;

        float bestDistance = float.MaxValue;
        bool hasHit = false;

        // 1) 地形网格（地形 chunk 由流式加载在别的线程增删，同样兜底）
        try
        {
            TerrainEntity terrain = TerrainBrush.FindTerrain(this);
            if (terrain?.Terrain?.TerrainData != null &&
                TerrainBrush.TryHit(terrain, ray, out Vector3 _, out Vector3 terrainHit))
            {
                worldPoint = terrainHit;
                bestDistance = GetRayDistance(ray, terrainHit);
                hasHit = true;
            }
        }
        catch (Exception ex)
        {
            LogPlacementPickFailure(ex);
        }

        // 2) 视口里已有的模型（只测视口内 + 距离有限的代理），比地形近就用模型的表面
        try
        {
            if (TryPickPlacementMesh(ray, bestDistance, out Vector3 meshPoint, out float meshDistance))
            {
                worldPoint = meshPoint;
                bestDistance = meshDistance;
                hasHit = true;
            }
        }
        catch (Exception ex)
        {
            // 拾取跑在 UI 线程、代理列表在渲染线程增删；这里兜底一次，最坏退回地形/地面落点，不能把编辑器崩掉
            LogPlacementPickFailure(ex);
        }

        if (hasHit)
        {
            worldNormal = Vector3.Up;
            return true;
        }

        // 3) 与 Y = 0 平面求交
        if (Math.Abs(ray.Direction.Y) > 1e-5f)
        {
            float t = -ray.Position.Y / ray.Direction.Y;
            if (t > 0.0f)
            {
                worldPoint = ray.Position + (ray.Direction * t);
                worldNormal = Vector3.Up;
                return true;
            }
        }

        // 4) 最后退化：相机前方固定距离，至少保证用户能看到对象出现在视野里
        worldPoint = camera.GetEyePt() + (ray.Direction * 20.0f);
        worldNormal = Vector3.Up;
        return true;
    }

    private static float GetRayDistance(Ray ray, Vector3 point)
    {
        return Math.Max(0.0f, Vector3.Dot(point - ray.Position, ray.Direction));
    }

    private bool placementPickFailureLogged;

    private void LogPlacementPickFailure(Exception ex)
    {
        // 鼠标每次移动都会走这里，只记一次，避免刷日志
        if (placementPickFailureLogged)
            return;

        placementPickFailureLogged = true;
        Frosty.Core.App.Logger?.LogWarning($"[ObjectPalette] 放置拾取失败（已退回地形/地面落点）：{ex.Message}");
    }

    private BoundingFrustum? GetCameraFrustum()
    {
        if (camera is not LevelEditorCamera levelCamera)
            return null;

        Matrix worldMatrix = levelCamera.WorldMatrix;
        return BoundingFrustum.FromCamera(worldMatrix.TranslationVector, worldMatrix.Backward, worldMatrix.Down,
            levelCamera.FOV, levelCamera.GetNearClip(), levelCamera.GetFarClip(), levelCamera.AspectRatio);
    }

    private static bool IsOwnedBy(Entity entity, Entity root)
    {
        for (Entity cursor = entity; cursor != null; cursor = cursor.Parent)
        {
            if (ReferenceEquals(cursor, root))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 放置取点的「已有物体」命中：让放置能落在物件/建筑表面上，而不是永远落到地形或 Y=0。
    ///
    /// 性能上的取舍：
    ///  - 只考虑 <see cref="ModelRenderProxy"/>（地形走地形通道，Sprite / Gizmo / 放置预览自己都不参与）；
    ///  - 包围球粗筛：射线必须真的从旁边经过，且距离在 <see cref="PlacementPickMaxDistance"/> 以内；
    ///  - 视锥剔除：只有当前视口里看得见的代理才可能被射中（鼠标射线本来就在视锥里）；
    ///  - 包围盒求交拿到粗相位距离并排序，窄相位（逐三角形）只对真正可能更近的代理做，
    ///    并且带时间预算 <see cref="PlacementPickTriangleBudgetMs"/>，超时退回包围盒结果，避免拖拽时掉帧。
    /// </summary>
    private bool TryPickPlacementMesh(Ray ray, float maxDistance, out Vector3 hitPoint, out float hitDistance)
    {
        hitPoint = Vector3.Zero;
        hitDistance = float.MaxValue;

        if (proxies.Count == 0)
            return false;

        float limit = Math.Min(maxDistance, PlacementPickMaxDistance);
        if (limit <= 0.0f)
            return false;

        Entity previewRoot = (EditorProvider as LevelEditor)?.PlacementPreviewEntity;
        BoundingFrustum? frustum = GetCameraFrustum();

        List<PlacementPickCandidate> candidates = placementPickCandidates;
        candidates.Clear();

        // proxies 会在渲染线程的 Render() 里（renderTasks 执行 AddEntity / DeleteEntityFromRenderProxy 时）被增删，
        // 而这里跑在 UI 线程的鼠标消息里。直接 foreach 会撞上「集合已修改」（切换素材时最明显），
        // 所以先拷一份快照再遍历（List.ToArray 底层是数组拷贝，不会抛枚举异常）。
        RenderProxy[] snapshot;
        try
        {
            snapshot = proxies.ToArray();
        }
        catch (Exception)
        {
            return false;
        }

        Vector3 rayOrigin = ray.Position;
        Vector3 rayDirection = ray.Direction;

        foreach (RenderProxy proxy in snapshot)
        {
            if (proxy is not ModelRenderProxy modelProxy || !modelProxy.CanHitTest)
                continue;

            // 隐藏的物件不参与命中
            if (proxy.OwnerEntity != null && !proxy.OwnerEntity.IsVisible)
                continue;

            // 放置预览不能射到自己，否则预览会卡在上一帧的位置
            if (previewRoot != null && IsOwnedBy(proxy.OwnerEntity, previewRoot))
                continue;

            // 包围球粗筛
            BoundingSphere sphere = proxy.BoundingSphere;
            Vector3 toCenter = sphere.Center - rayOrigin;
            float along = Vector3.Dot(toCenter, rayDirection);
            if (along + sphere.Radius < 0.0f)
                continue;
            if (along - sphere.Radius > limit)
                continue;
            if ((toCenter.LengthSquared() - (along * along)) > (sphere.Radius * sphere.Radius))
                continue;

            // 视口剔除
            BoundingBox box = proxy.BoundingBox;
            if (frustum.HasValue && !frustum.Value.Intersects(ref box))
                continue;

            if (!ray.Intersects(ref box, out Vector3 boxHit))
                continue;

            float near = Vector3.Dot(boxHit - rayOrigin, rayDirection);
            if (near <= 0.01f || near > limit)
                continue;   // 相机就在盒子里 / 或者太远

            candidates.Add(new PlacementPickCandidate()
            {
                Proxy = modelProxy,
                NearDistance = near,
                BoxPoint = rayOrigin + (rayDirection * near),
            });
        }

        if (candidates.Count == 0)
            return false;

        candidates.Sort((a, b) => a.NearDistance.CompareTo(b.NearDistance));

        // 窄相位：从最近的包围盒开始做三角形求交。
        // 一旦已知命中比下一个候选的入射距离还近，后面的都不可能更近，可以安全停下。
        float bestDistance = limit;
        Vector3 bestPoint = Vector3.Zero;
        bool hasTriangleHit = false;
        float fallbackDistance = float.MaxValue;
        Vector3 fallbackPoint = Vector3.Zero;

        Stopwatch budget = Stopwatch.StartNew();
        for (int i = 0; i < candidates.Count; i++)
        {
            PlacementPickCandidate candidate = candidates[i];
            if (candidate.NearDistance >= bestDistance)
                break;

            if (candidate.NearDistance < fallbackDistance)
            {
                fallbackDistance = candidate.NearDistance;
                fallbackPoint = candidate.BoxPoint;
            }

            if (budget.Elapsed.TotalMilliseconds > PlacementPickTriangleBudgetMs)
                break;

            bool hit;
            Vector3 point;
            try
            {
                hit = candidate.Proxy.HitTest(ray, out point);
            }
            catch (Exception)
            {
                hit = false;
                point = Vector3.Zero;
            }

            if (!hit)
                continue;

            float distance = GetRayDistance(ray, point);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestPoint = point;
            hasTriangleHit = true;
        }

        if (hasTriangleHit)
        {
            hitPoint = bestPoint;
            hitDistance = bestDistance;
            return true;
        }

        // 三角形阶段没打中/超时：退回最近一个包围盒交点，至少保证预览贴在物体的大致表面上
        if (fallbackDistance < float.MaxValue)
        {
            hitPoint = fallbackPoint;
            hitDistance = fallbackDistance;
            return true;
        }

        return false;
    }

    private Ray GetHitTestRay(int x, int y)
    {
        if (camera == null) return new Ray();
        Matrix viewProj = camera.GetViewProjMatrix();
        Ray ray = Ray.GetPickRay(x, y, new Viewport(0, 0, Viewport.ViewportWidth, Viewport.ViewportHeight, camera.GetNearClip(), camera.GetFarClip()), viewProj);

        ray.Position.X *= -1.0f;

        ray.Direction = (ray.Position - (camera.GetEyePt() * new Vector3(-1, 1, 1)));
        ray.Direction.Normalize();

        return ray;
    }

    private void ProcessRenderTasks()
    {
        while (renderTasks.Count > 0)
        {
            RenderAction action = renderTasks.Dequeue();
            action.Invoke(RenderCreateState);
        }
    }
}
