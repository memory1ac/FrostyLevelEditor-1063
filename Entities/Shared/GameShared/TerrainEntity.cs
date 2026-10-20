using Frosty.Core.Viewport;
using LevelEditorPlugin.Managers;
using LevelEditorPlugin.Render;
using LevelEditorPlugin.Render.Proxies;
using System.Collections.Generic;

namespace LevelEditorPlugin.Entities;

[EntityBinding(DataType = typeof(FrostySdk.Ebx.TerrainEntityData))]
public class TerrainEntity : SpatialEntity, IEntityData<FrostySdk.Ebx.TerrainEntityData>
{
    public new FrostySdk.Ebx.TerrainEntityData Data => data as FrostySdk.Ebx.TerrainEntityData;
    public Assets.Terrain Terrain { get; private set; }
    public int PreviewLayerIndex { get; set; }
    public int PreviewHoleIndex { get; set; } = -1;

    public TerrainEntity(FrostySdk.Ebx.TerrainEntityData inData, Entity inParent)
        : base(inData, inParent)
    {
        Terrain = LoadedAssetManager.Instance.LoadAsset<Assets.Terrain>(this, Data.TerrainAsset);
    }

    public override void CreateRenderProxy(List<RenderProxy> proxies, RenderCreateState state)
    {
        Terrain.LoadResource(state);
        VirtualTexturePage.Bind(Terrain.TerrainData, Terrain.LayerColorPaths, GetTransform());
        foreach (TerrainChunkRenderable chunk in Terrain.TerrainData.TerrainChunks)
            proxies.Add(new TerrainRenderProxy(state, this, chunk));

        SetFlags(EntityFlags.RenderProxyGenerated);
    }

    public override void Destroy()
    {
        LoadedAssetManager.Instance.UnloadAsset(Terrain);
    }
}
