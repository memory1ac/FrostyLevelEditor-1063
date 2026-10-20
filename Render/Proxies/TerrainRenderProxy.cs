using Frosty.Core;
using Frosty.Core.Viewport;
using FrostySdk;
using LevelEditorPlugin.Entities;
using SharpDX;
using System.Collections.Generic;
using D3D11 = SharpDX.Direct3D11;

namespace LevelEditorPlugin.Render.Proxies;

public class TerrainRenderProxy : RenderProxy
{
    protected TerrainChunkRenderable renderData;
    protected int previewLayer = -1;
    protected int previewHoleLayer = -1;

    private ShaderPermutation permutation;
    private D3D11.Buffer pixelParameters;
    private List<D3D11.ShaderResourceView> pixelTextures = [];

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
        },
        new GeometryDeclarationDesc.Element
        {
            Usage = VertexElementUsage.Color0,
            Format = VertexElementFormat.Float3
        }
    });

    public TerrainRenderProxy(RenderCreateState state, TerrainEntity owner, TerrainChunkRenderable terrainChunk)
        : base(owner)
    {
        renderData = terrainChunk;

        BoundingBox = terrainChunk.Bounds.GetBoundingBox();

        //materialParameters.Add(new ShaderParameter("Color", ShaderParameterType.Float4, new[] { 0.0f, 1.0f, 0.0f, 1.0f }));
        switch (renderData.Level)
        {
            case 1: materialParameters.Add(new ShaderParameter("Color", ShaderParameterType.Float4, new[] { 1.0f, 0.0f, 0.0f, 1.0f })); break;
            case 2: materialParameters.Add(new ShaderParameter("Color", ShaderParameterType.Float4, new[] { 0.0f, 1.0f, 0.0f, 1.0f })); break;
            case 3: materialParameters.Add(new ShaderParameter("Color", ShaderParameterType.Float4, new[] { 0.0f, 0.0f, 1.0f, 1.0f })); break;
            case 4: materialParameters.Add(new ShaderParameter("Color", ShaderParameterType.Float4, new[] { 1.0f, 1.0f, 0.0f, 1.0f })); break;
            case 5: materialParameters.Add(new ShaderParameter("Color", ShaderParameterType.Float4, new[] { 0.0f, 1.0f, 1.0f, 1.0f })); break;
            case 6: materialParameters.Add(new ShaderParameter("Color", ShaderParameterType.Float4, new[] { 1.0f, 0.0f, 1.0f, 1.0f })); break;
            case 7: materialParameters.Add(new ShaderParameter("Color", ShaderParameterType.Float4, new[] { 1.0f, 1.0f, 1.0f, 1.0f })); break;
            case 8: materialParameters.Add(new ShaderParameter("Color", ShaderParameterType.Float4, new[] { 1.0f, 1.0f, 1.0f, 1.0f })); break;
        }
        materialParameters.Add(new ShaderParameter("HoleMaskIndex", ShaderParameterType.Float4, new[] { (float)previewLayer, (float)previewHoleLayer, 0, 0 }));

        for (int i = 0; i < terrainChunk.TexOffsets.Count; i++)
        {
            materialParameters.Add(new ShaderParameter($"LayerOffsets{i + 1}", ShaderParameterType.Float4, terrainChunk.TexOffsets[i].ToArray()));
        }

        AddLayerColors(owner);

        permutation = state.ShaderLibrary.GetUserShader("TerrainShader", GeometryDecl);
        permutation.IsTwoSided = true;
        permutation.LoadShaders(state.Device);
        permutation.AssignParameters(state, materialParameters, materialTextures, ref pixelParameters, ref pixelTextures);
    }

    /// <summary>
    /// 暴露内部的地形 chunk 渲染数据，供地形编辑工具使用。
    /// </summary>
    public TerrainChunkRenderable RenderData => renderData;

    private void AddLayerColors(TerrainEntity terrain)
    {
        string[] colors = terrain.Terrain.LayerColorPaths ?? [];
        float[] has = new float[24];
        for (int i = 0; i < 24; i++)
        {
            string path = i < colors.Length ? colors[i] : null;
            string bound = "Resources/Textures/DefaultLayer.dds";
            if (!string.IsNullOrEmpty(path) && App.AssetManager.GetEbxEntry(path) != null)
            {
                bound = path;
                has[i] = 1;
            }
            materialTextures.Add(new ShaderParameter($"LayerColor{i}", ShaderParameterType.Tex2d, bound));
        }

        for (int group = 0; group < 6; group++)
        {
            int offset = group * 4;
            materialParameters.Add(new ShaderParameter($"LayerHasColor{group + 1}", ShaderParameterType.Float4, has[offset], has[offset + 1], has[offset + 2], has[offset + 3]));
        }
        materialParameters.Add(new ShaderParameter("TextureTileSize", ShaderParameterType.Float4, 4.0f, 4.0f, 0, 0));
    }

    public void SetPreviewLayers(RenderCreateState state, int layerIndex, int holeIndex)
    {
        if (layerIndex != previewLayer || holeIndex != previewHoleLayer)
        {
            previewLayer = layerIndex;
            previewHoleLayer = holeIndex;

            int index = materialParameters.FindIndex(sp => sp.Name == "HoleMaskIndex");
            materialParameters.RemoveAt(index);

            materialParameters.Add(new ShaderParameter("HoleMaskIndex", ShaderParameterType.Float4, new[] { (float)previewLayer, (float)previewHoleLayer, 0, 0 }));
            permutation.AssignParameters(state, materialParameters, materialTextures, ref pixelParameters, ref pixelTextures);
        }
    }

    public override void Update(RenderCreateState state)
    {
        TerrainEntity terrainEntity = OwnerEntity as TerrainEntity;
        SetPreviewLayers(state, terrainEntity.PreviewLayerIndex, terrainEntity.PreviewHoleIndex);
    }

    public override void Render(D3D11.DeviceContext context, MeshRenderPath renderPath)
    {
        if (renderPath == MeshRenderPath.Deferred)
        {
            permutation.SetState(context, renderPath);
            context.PixelShader.SetShaderResources(1, pixelTextures.ToArray());
            context.PixelShader.SetConstantBuffer(2, pixelParameters);

            renderData.Render(context, renderPath);
        }
    }

    public override bool ShouldRender(float distToCamera, float screenSize)
    {
        return OwnerEntity.IsVisible && screenSize > 0.02f;
    }

    public override bool HitTest(Ray hitTestRay, out Vector3 hitLocation)
    {
        // 非地形编辑模式下，射线不命中地形，从而无法选中
        if (!LevelEditorPlugin.BF1Core.Utils.TerrainEditState.IsTerrainEditModeEnabled)
        {
            hitLocation = Vector3.Zero;
            return false;
        }

        // 地形编辑模式下，使用当前代理的世界包围盒做拾取测试
        BoundingBox bbox = BoundingBox;
        if (hitTestRay.Intersects(ref bbox, out hitLocation))
        {
            return true;
        }

        hitLocation = Vector3.Zero;
        return false;
    }

    public override void Dispose()
    {
        renderData.Dispose();
        permutation.Dispose();
        pixelParameters.Dispose();
        pixelTextures.Clear();
    }
}
