using Frosty.Core.Viewport;
using LevelEditorPlugin.Managers;
using LevelEditorPlugin.Render.Proxies;
using System.Collections.Generic;

namespace LevelEditorPlugin.Entities;

[EntityBinding(DataType = typeof(FrostySdk.Ebx.StaticModelEntityData))]
public class StaticModelEntity : GamePhysicsEntity, IEntityData<FrostySdk.Ebx.StaticModelEntityData>
{
    public new FrostySdk.Ebx.StaticModelEntityData Data => data as FrostySdk.Ebx.StaticModelEntityData;
    public Assets.MeshAsset Mesh { get; private set; }
    public override IEnumerable<ConnectionDesc> Links
    {
        get
        {
            List<ConnectionDesc> outLinks = [.. base.Links, new ConnectionDesc("ShaderParameters", Direction.In)];
            return outLinks;
        }
    }
    public override IEnumerable<ConnectionDesc> Properties
    {
        get
        {
            List<ConnectionDesc> outProperties =
            [
                .. base.Properties,
                new ConnectionDesc("Visible", Direction.In),
                new ConnectionDesc("AnimatePhysics", Direction.In),
                new ConnectionDesc("Space", Direction.In),
            ];
            return outProperties;
        }
    }

    public StaticModelEntity(FrostySdk.Ebx.StaticModelEntityData inData, Entity inParent)
        : base(inData, inParent)
    {
        Mesh = LoadedAssetManager.Instance.LoadAsset<Assets.MeshAsset>(this, Data.Mesh);
    }

    public override void CreateRenderProxy(List<RenderProxy> proxies, RenderCreateState state)
    {
        Mesh.LoadResource(state);
        proxies.Add(new ModelRenderProxy(state, this));

        SetFlags(EntityFlags.RenderProxyGenerated);
    }

    public override void Destroy()
    {
        LoadedAssetManager.Instance.UnloadAsset(Mesh);
    }
}
