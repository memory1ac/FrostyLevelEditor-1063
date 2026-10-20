using Frosty.Core.Viewport;
using LevelEditorPlugin.Managers;
using LevelEditorPlugin.Render;
using LevelEditorPlugin.Render.Proxies;
using SharpDX;
using System.Collections.Generic;

namespace LevelEditorPlugin.Entities;

[EntityBinding(DataType = typeof(FrostySdk.Ebx.PbrSphereLightEntityData))]
public class PbrSphereLightEntity : SpatialEntity, IEntityData<FrostySdk.Ebx.PbrSphereLightEntityData>
{
    public new FrostySdk.Ebx.PbrSphereLightEntityData Data => data as FrostySdk.Ebx.PbrSphereLightEntityData;

    public ObjRenderable SpriteData { get; private set; }

    public PbrSphereLightEntity(FrostySdk.Ebx.PbrSphereLightEntityData inData, Entity inParent)
        : base(inData, inParent)
    {
    }

    public override void CreateRenderProxy(List<RenderProxy> proxies, RenderCreateState state)
    {
        SpriteData = LoadedMeshManager.Instance.LoadMesh(state, "Sprite");
        proxies.Add(new SpriteRenderProxy(state, this, SpriteData, "PointLight", new Color4(Data.Color.x, Data.Color.y, Data.Color.z, 1.0f)));

        SetFlags(EntityFlags.RenderProxyGenerated);
    }

    public override void Destroy()
    {
        LoadedMeshManager.Instance.UnloadMesh(SpriteData);
    }
}
