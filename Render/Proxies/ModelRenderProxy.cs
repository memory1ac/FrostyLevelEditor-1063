using Frosty.Core.Screens;
using Frosty.Core.Viewport;
using FrostySdk;
using LevelEditorPlugin.Entities;
using LevelEditorPlugin.Render;
using SharpDX;
using System.Collections.Generic;
using D3D11 = SharpDX.Direct3D11;

namespace LevelEditorPlugin.Render.Proxies;

public class ModelRenderProxy : RenderProxy
{
    protected MeshRenderable renderData;
    protected int lodIndex;

    private ShaderPermutation permutation;
    private RenderCreateState renderState;
    private D3D11.Buffer pixelParameters;
    private List<D3D11.ShaderResourceView> pixelTextures = [];
    private MeshMaterial material;

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
        }
    });

    public ModelRenderProxy(RenderCreateState state, ISpatialEntity owner, MeshRenderable meshData)
        : base(owner)
    {
        renderData = meshData;
        renderState = state;

        RecalculateBoundingBox();

        material = new MeshMaterial();
        material.VectorParameters.Add(
            new FrostySdk.Ebx.VectorShaderParameter()
            {
                ParameterName = "Color",
                ParameterType = FrostySdk.Ebx.ShaderParameterType.ShaderParameterType_Vec4,
                Value = new FrostySdk.Ebx.Vec4()
                {
                    // 2026/01/19
                    // 改动标记
                    x = OwnerEntity.Owner?.Layer?.LayerColor.Red ?? 1.0f,
                    y = OwnerEntity.Owner?.Layer?.LayerColor.Green ?? 1.0f,
                    z = OwnerEntity.Owner?.Layer?.LayerColor.Blue ?? 1.0f,
                    w = 1.0f
                }
            });

        permutation = state.ShaderLibrary.GetUserShader("LevelShader", GeometryDecl);
        permutation.IsTwoSided = true;
        permutation.LoadShaders(state.Device);
        permutation.AssignParameters(state, material, ref pixelParameters, ref pixelTextures);
    }

    public ModelRenderProxy(RenderCreateState state, MeshProxyEntity owner)
        : this(state, owner, owner.Mesh.MeshData)
    {
    }

    public ModelRenderProxy(RenderCreateState state, StaticModelEntity owner)
        : this(state, owner, owner.Mesh.MeshData)
    {
    }

#if BF1
    public ModelRenderProxy(RenderCreateState state, BreakableModelEntity owner)
      : this(state, owner, owner.Mesh.MeshData)
    {
    }

    public ModelRenderProxy(RenderCreateState state, DebrisCluster owner)
       : this(state, owner, owner.Mesh.MeshData)
    {
    }

    public ModelRenderProxy(RenderCreateState state, DynamicModelEntity owner)
       : this(state, owner, owner.Mesh.MeshData)
    {
    }

    public ModelRenderProxy(RenderCreateState state, BangerEntity owner)
       : this(state, owner, owner.Mesh.MeshData)
    {
    }
#endif

    public ModelRenderProxy(RenderCreateState state, ClothEntity owner)
        : this(state, owner, owner.Mesh.MeshData)
    {
    }

    public ModelRenderProxy(RenderCreateState state, VegetationTreeEntity owner)
        : this(state, owner, owner.Mesh.MeshData)
    {
    }

    public override void Render(D3D11.DeviceContext context, MeshRenderPath renderPath)
    {
        // 在 Selection pass 中，仅对被选中的实例输出几何，用于生成选中轮廓
        if (renderPath == MeshRenderPath.Selection && !isSelected)
            return;

        if (renderPath == MeshRenderPath.Deferred || renderPath == MeshRenderPath.Selection)
        {
            if (renderData.ShouldUseTexturedRendering)
            {
                ShaderDbMeshTextures.Apply(renderState, renderData);
                VirtualTexturePage.Apply(context, renderState, renderData);
                // 带贴图渲染：每个 section 使用自己的材质和贴图（MeshSetPlugin 管线）
                ((MeshRenderBase)renderData.GetLod(lodIndex)).Render(context, renderPath);
            }
            else
            {
                // 无贴图渲染：使用 LevelShader 纯色
                permutation.SetState(context, renderPath);
                context.PixelShader.SetShaderResources(1, pixelTextures.ToArray());
                context.PixelShader.SetConstantBuffer(2, pixelParameters);

                ((MeshRenderBase)renderData.GetLod(lodIndex)).Render(context, renderPath);
            }
        }
    }

    public override bool ShouldRender(float distToCamera, float screenSize)
    {
        return OwnerEntity.IsVisible && screenSize > renderData.CullScreenArea;
    }

    public override MeshRenderInstance GetInstance(float distToCamera)
    {
        lodIndex = 0;
        for (lodIndex = 0; lodIndex < 5; lodIndex++)
        {
            if (distToCamera < renderData.LodDistances[lodIndex])
            {
                break;
            }
        }

        return new MeshRenderInstance() { RenderMesh = this, Transform = Transform };
    }

    public override void SetSelected(RenderCreateState state, bool newSelected)
    {
        base.SetSelected(state, newSelected);

        material.SetVectorParameter("Color", newSelected
            ? new FrostySdk.Ebx.Vec4() { x = 1.0f, y = 1.0f, z = 0.0f, w = 1.0f }
            : new FrostySdk.Ebx.Vec4()
            {
                // 2026/01/19
                // 改动标记
                x = OwnerEntity.Owner?.Layer?.LayerColor.Red ?? 1.0f,
                y = OwnerEntity.Owner?.Layer?.LayerColor.Green ?? 1.0f,
                z = OwnerEntity.Owner?.Layer?.LayerColor.Blue ?? 1.0f,
                w = 1.0f
            });
        permutation.AssignParameters(state, material, ref pixelParameters, ref pixelTextures);

        // 当使用 MeshSetPlugin 的带贴图渲染时，将所有 LOD 的 Section 标记为选中/未选中，
        // 这样 MeshRenderLod 在 Selection 通道中只会渲染被选中的 Section，用于描边高亮。
        if (renderData.ShouldUseTexturedRendering && renderData.TexturedMesh != null)
        {
            foreach (var lod in renderData.TexturedMesh.LODs)
            {
                foreach (var section in lod.Sections)
                {
                    section.IsSelected = newSelected;
                }
            }
        }
    }

    /// <summary>是否具备可射线求交的网格（没有 LOD 的模型不参与点击拾取）</summary>
    public bool CanHitTest => renderData?.Lods is { Count: > 0 };

    public override bool HitTest(Ray hitTestRay, out Vector3 hitLocation)
    {
        return renderData.Lods[0].HitTest(Transform, hitTestRay, out hitLocation);
    }

    public override void RecalculateBoundingBox()
    {
        OrientedBoundingBox meshBbox = new OrientedBoundingBox(renderData.Bounds);
        meshBbox.Transform(Transform);

        BoundingBox = meshBbox.GetBoundingBox();
    }

    public override void Dispose()
    {
        renderData.Dispose();
        permutation.Dispose();
        pixelParameters.Dispose();
        pixelTextures.Clear();
    }
}
