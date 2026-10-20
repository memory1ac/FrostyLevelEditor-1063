using LevelEditorPlugin.Editors;
using LevelEditorPlugin.Managers;
using System.Collections.Generic;
using System.IO;

namespace LevelEditorPlugin.Entities;

[EntityBinding(DataType = typeof(FrostySdk.Ebx.SubWorldReferenceObjectData))]
public class SubWorldReferenceObject : ReferenceObject, IEntityData<FrostySdk.Ebx.SubWorldReferenceObjectData>, ILayerEntity
{
    public new FrostySdk.Ebx.SubWorldReferenceObjectData Data => data as FrostySdk.Ebx.SubWorldReferenceObjectData;
    public new Assets.SubWorld Blueprint => blueprint as Assets.SubWorld;
    public override string DisplayName => Path.GetFileName(blueprint.Name);
    public override IEnumerable<ConnectionDesc> Events
    {
        get
        {
            List<ConnectionDesc> outEvents =
            [
                .. base.Events,
                new ConnectionDesc("Preload", Direction.In),
                new ConnectionDesc("StreamIn", Direction.In),
                new ConnectionDesc("StreamOut", Direction.In),
                new ConnectionDesc("OnStreamedIn", Direction.Out),
                new ConnectionDesc("OnStreamedOut", Direction.Out),
            ];

            return outEvents;
        }
    }
    public override IEnumerable<ConnectionDesc> Properties
    {
        get
        {
            List<ConnectionDesc> outProperties = [.. base.Properties, new ConnectionDesc("IsLoaded", Direction.Out)];

            return outProperties;
        }
    }
    public Assets.NetworkRegistryAsset NetworkRegistry { get; protected set; }
    public Assets.MeshVariationDatabase MeshVariationDatabase { get; protected set; }

    public SubWorldReferenceObject(FrostySdk.Ebx.SubWorldReferenceObjectData inData, Entity inParent, EntityWorld inWorld)
        : base(inData, inParent, inWorld)
    {
    }

    public SubWorldReferenceObject(FrostySdk.Ebx.SubWorldReferenceObjectData inData, Entity inParent)
        : this(inData, inParent, null)
    {
    }

    protected override void Initialize()
    {
        blueprint = LoadedAssetManager.Instance.LoadAsset<Assets.SubWorld>(Data.BundleName);

        string networkRegistryName = $"{Data.BundleName}_networkregistry_win32";
        string meshVariationName = $"{Data.BundleName}/MeshVariationDb_Win32";

        NetworkRegistry = LoadedAssetManager.Instance.LoadAsset<Assets.NetworkRegistryAsset>(networkRegistryName);
        MeshVariationDatabase = LoadedAssetManager.Instance.LoadAsset<Assets.MeshVariationDatabase>(meshVariationName);
    }

    public Layers.SceneLayer GetLayer()
    {
        if (blueprint == null)
            return null;

        string layerName = Path.GetFileName(blueprint.Name);
        Layers.SceneLayer layer = new Layers.SceneLayer(this, layerName, new SharpDX.Color4(0.0f, 0.5f, 0.0f, 1.0f));

        foreach (Entity entity in entities)
        {
            if (entity is ILayerEntity)
            {
                ILayerEntity entityLayer = entity as ILayerEntity;
                Layers.SceneLayer childLayer = entityLayer.GetLayer();
                if (childLayer != null)
                    layer.ChildLayers.Add(childLayer);
            }
            else
            {
                layer.AddEntity(entity);
                entity.SetOwner(entity);
            }
        }
        layer.IsVisible = Data.AutoLoad;
        return layer;
    }

    public override void Destroy()
    {
        LoadedAssetManager.Instance.UnloadAsset(NetworkRegistry);
        LoadedAssetManager.Instance.UnloadAsset(MeshVariationDatabase);

        base.Destroy();
    }
}
