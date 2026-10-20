using Frosty.Core;
using Frosty.Core.Viewport;
using System;

namespace LevelEditorPlugin.Assets;

[AssetBinding(DataType = typeof(FrostySdk.Ebx.TerrainData))]
public class Terrain : Asset, IAssetData<FrostySdk.Ebx.TerrainData>
{
    public FrostySdk.Ebx.TerrainData Data => data as FrostySdk.Ebx.TerrainData;
    public Render.TerrainRenderable TerrainData { get; private set; }
    public string[] LayerColorPaths { get; private set; } = [];
    public int MaxLayerCount { get; private set; }

    private Resources.TerrainStreamingTree terrainStreamingTree;
    private Resources.TerrainLayerCombinations terrainLayerCombinations;
    //private Resources.TerrainDecals terrainDecals;
    private Resources.VisualTerrain visualTerrain;

    public Terrain(Guid fileGuid, FrostySdk.Ebx.TerrainData inData)
        : base(fileGuid, inData)
    {
        terrainStreamingTree = App.AssetManager.GetResAs<Resources.TerrainStreamingTree>(App.AssetManager.GetResEntry(Data.TerrainStreamingTreeResource));
        try
        {
            FrostySdk.Managers.ResAssetEntry combinationEntry = App.AssetManager.GetResEntry(Data.TerrainLayerCombinationsResource);
            if (combinationEntry != null)
                terrainLayerCombinations = App.AssetManager.GetResAs<Resources.TerrainLayerCombinations>(combinationEntry);
        }
        catch (Exception ex)
        {
            App.Logger.LogError("TerrainLayerCombinations 读取失败: {0}", ex.Message);
        }
        visualTerrain = App.AssetManager.GetResAs<Resources.VisualTerrain>(App.AssetManager.GetResEntry(Data.VisualResource));

        MaxLayerCount = (int)(terrainStreamingTree.GetRasterTree(Resources.RasterTreeTypes.TerrainMaskTreeType) as Resources.TerrainMaskTree).maxLayerCount;
    }

    public void LoadResource(RenderCreateState state)
    {
        if (TerrainData == null)
        {
            LayerColorPaths = Render.TerrainAlbedoResolver.Resolve(FileGuid, terrainLayerCombinations);
            TerrainData = new Render.TerrainRenderable(state, terrainStreamingTree);
        }
    }

    public override void Dispose()
    {
        TerrainData.Dispose();
    }
}
