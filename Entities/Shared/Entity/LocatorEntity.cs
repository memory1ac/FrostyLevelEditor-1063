using Frosty.Core.Viewport;
using LevelEditorPlugin.Managers;
using LevelEditorPlugin.Render;
using LevelEditorPlugin.Render.Proxies;
using System.Collections.Generic;

namespace LevelEditorPlugin.Entities;

[EntityBinding(DataType = typeof(FrostySdk.Ebx.LocatorEntityData))]
public class LocatorEntity : SpatialEntity, IEntityData<FrostySdk.Ebx.LocatorEntityData>
{
    public new FrostySdk.Ebx.LocatorEntityData Data => data as FrostySdk.Ebx.LocatorEntityData;

    public ObjRenderable MeshData { get; private set; }

    public LocatorEntity(FrostySdk.Ebx.LocatorEntityData inData, Entity inParent)
        : base(inData, inParent)
    {
    }

    public override void CreateRenderProxy(List<RenderProxy> proxies, RenderCreateState state)
    {
        MeshData = LoadedMeshManager.Instance.LoadMesh(state, "Sprite");
        proxies.Add(new SpriteRenderProxy(state, this, MeshData, "Location"));

        SetFlags(EntityFlags.RenderProxyGenerated);
    }

    public override void Destroy()
    {
        LoadedMeshManager.Instance.UnloadMesh(MeshData);
    }
}
