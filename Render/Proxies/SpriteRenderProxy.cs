using Frosty.Core;
using Frosty.Core.Screens;
using Frosty.Core.Viewport;
using FrostySdk;
using LevelEditorPlugin;
using FrostySdk.Managers;
using LevelEditorPlugin.Entities;
using LevelEditorPlugin.Options;
using SharpDX;
using System;
using System.Collections.Generic;
using D3D11 = SharpDX.Direct3D11;

namespace LevelEditorPlugin.Render.Proxies;

public class SpriteRenderProxy : RenderProxy
{
    private ObjRenderable renderData;

    private ShaderPermutation permutation;
    private D3D11.Buffer pixelParameters;
    private List<D3D11.ShaderResourceView> pixelTextures = [];
    private Matrix originalTransform;

    private List<ShaderParameter> materialParameters = [];
    private List<ShaderParameter> materialTextures = [];
    private Color4 tintColor;

    /////////////////////////

    /// <summary>
    /// 模型资源路径
    /// </summary>
    public string MeshResPath { get; private set; }
    // 万一模型设置错误，则显示这个默认模型
    private MeshRenderShape defaultMeshRender;

    private ResAssetEntry meshResAssetEntry;
    private Resources.MeshSet meshSet;
    private MeshLodRenderable renderMeshLod;
    /// <summary>非纯色模式下用 LevelShader 渲染 mesh，避免显示异常</summary>
    private SolidColorMeshWrapper solidColorMeshWrapper;

    private Vector3 obbHalfExtents;

    private enum RenderType
    {
        None,
        ResMesh,
        CubeMesh
    }
    private RenderType renderType;

    /////////////////////////

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

    public SpriteRenderProxy(RenderCreateState state, Entity owner, ObjRenderable meshData, string spriteName, Color4? inTintColor = null)
        : base(owner as ISpatialEntity)
    {
        BoundingBox = BoundingBox.FromSphere(new BoundingSphere(Transform.TranslationVector, 0.5f));
        renderData = meshData;
        originalTransform = Transform;
        tintColor = inTintColor.HasValue ? inTintColor.Value : Color4.White;

        CreateMaterial(state, spriteName, tintColor);
    }

    public override void Update(RenderCreateState state)
    {
        if (OwnerEntity.RequiresTransformUpdate)
        {
            originalTransform = (OwnerEntity as ISpatialEntity).GetTransform();
            OwnerEntity.RequiresTransformUpdate = false;
        }
    }

    /// <summary>
    /// 设置模型资源路径，同时初始化
    /// </summary>
    public void SetMeshResPath(RenderCreateState state, string meshResPath)
    {
        renderType = RenderType.ResMesh;

        // 必须检查MeshResPath是否为空
        if (string.IsNullOrWhiteSpace(meshResPath))
            return;

        MeshResPath = meshResPath;
        defaultMeshRender = MeshRenderShape.CreateCube(state, "GroundBox", "CustomCubeShader", 1, 10, 1);

        meshResAssetEntry = App.AssetManager.GetResEntry(meshResPath);

        if (meshResAssetEntry != null)
            meshSet = App.AssetManager.GetResAs<Resources.MeshSet>(meshResAssetEntry);
        if (meshSet != null)
        {
            renderMeshLod = new MeshLodRenderable(state, meshSet.Lods[0], false);
            solidColorMeshWrapper = new SolidColorMeshWrapper(state, OwnerEntity, renderMeshLod);
        }
    }

    /// <summary>
    /// 设置立方体模型
    /// </summary>
    public void SetMeshCubeData(RenderCreateState state, Vector3 halfExtents)
    {
        renderType = RenderType.CubeMesh;

        MeshResPath = "Create Custom Cube";
        defaultMeshRender = MeshRenderShape.CreateCube(state, "GroundBox", "CustomCubeShader", 1, 1, 1);

        obbHalfExtents = halfExtents;
    }

    /// <summary>
    /// 获取模型渲染实例
    /// </summary>
    public bool GetMeshRenderInstance(out MeshRenderInstance instance)
    {
        instance = new MeshRenderInstance();

        // 判断模型资源路径是否为空，如果资源路径为空，则不渲染
        if (string.IsNullOrWhiteSpace(MeshResPath))
            return false;

        if (renderType == RenderType.ResMesh)
        {
            instance.Transform = originalTransform;

            if (renderMeshLod == null)
                instance.RenderMesh = defaultMeshRender;
            else if (LevelEditorRenderingOptions.UseTexturedModelRendering)
                // 非纯色模式：用包装器设置 LevelShader，避免显示异常
                instance.RenderMesh = solidColorMeshWrapper != null ? solidColorMeshWrapper : (MeshRenderBase)renderMeshLod;
            else
                // 纯色模式：与正常模型一致，返回裸 mesh，由管线用共享 LevelShader 绘制，从而随 view mode 切换
                instance.RenderMesh = (MeshRenderBase)renderMeshLod;
        }
        else if (renderType == RenderType.CubeMesh)
        {
            _ = originalTransform.Decompose(out Vector3 scale, out Quaternion rotation, out Vector3 translation);
            var newScale = new Vector3
            {
                X = scale.X * obbHalfExtents.X * 2.0f,
                Y = scale.Y * obbHalfExtents.Y * 2.0f,
                Z = scale.Z * obbHalfExtents.Z * 2.0f
            };
            var newMatrix = Matrix.Scaling(newScale) *
                Matrix.RotationQuaternion(rotation) *
                Matrix.Translation(translation);

            instance.Transform = newMatrix;
            instance.RenderMesh = defaultMeshRender;
        }

        return true;
    }

    public override void Render(D3D11.DeviceContext context, MeshRenderPath renderPath)
    {
        Matrix cameraTransform = Screens.LevelEditorScreen.EditorCamera.WorldMatrix;
        float distToCamera = (Transform.TranslationVector - cameraTransform.TranslationVector).Length();

        cameraTransform.M41 = 0;
        cameraTransform.M42 = 0;
        cameraTransform.M43 = 0;

        float scaleAmount = (distToCamera < 10.0f) ? Math.Max(distToCamera / 10.0f, 0.125f) : 1.0f;
        Transform = Matrix.Scaling(scaleAmount) * cameraTransform * Matrix.Translation(originalTransform.TranslationVector);
        BoundingBox = BoundingBox.FromSphere(new BoundingSphere(Transform.TranslationVector, 0.5f * scaleAmount));

        if (renderPath == MeshRenderPath.Forward)
        {
            permutation.SetState(context, renderPath);
            context.PixelShader.SetShaderResources(1, pixelTextures.ToArray());
            context.PixelShader.SetConstantBuffer(2, pixelParameters);

            renderData.Render(context, renderPath);
        }
    }

    private void CreateMaterial(RenderCreateState state, string spriteName, Color4 tintColor)
    {
        materialParameters.Add(new ShaderParameter("Tint", ShaderParameterType.Float4, tintColor.Red, tintColor.Green, tintColor.Blue, 1.0f));
        materialTextures.Add(new ShaderParameter("Sprite", ShaderParameterType.Tex2d, ContentPaths.Texture("Sprites", spriteName + ".dds")));

        permutation = state.ShaderLibrary.GetUserShader("SpriteShader", GeometryDecl);
        permutation.IsTwoSided = true;
        permutation.LoadShaders(state.Device);
        permutation.AssignParameters(state, materialParameters, materialTextures, ref pixelParameters, ref pixelTextures);
    }

    public override bool ShouldRender(float distToCamera, float screenSize)
    {
        return OwnerEntity.IsVisible && screenSize > 0.02f;
    }

    public override void SetSelected(RenderCreateState state, bool newSelected)
    {
        base.SetSelected(state, newSelected);

        float[] colors = tintColor.ToArray();
        if (newSelected)
            colors = new float[] { 1, 1, 0, 1 };

        materialParameters[0] = new ShaderParameter("Tint", ShaderParameterType.Float4, colors);
        permutation.AssignParameters(state, materialParameters, materialTextures, ref pixelParameters, ref pixelTextures);
    }

    public override void Dispose()
    {
        solidColorMeshWrapper?.Dispose();
        solidColorMeshWrapper = null;
        renderData.Dispose();
        permutation.Dispose();
        pixelParameters.Dispose();
        pixelTextures.Clear();
    }
}
