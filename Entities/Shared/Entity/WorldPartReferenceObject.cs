using System.IO;

namespace LevelEditorPlugin.Entities;

[EntityBinding(DataType = typeof(FrostySdk.Ebx.WorldPartReferenceObjectData))]
#if BF1
public class WorldPartReferenceObject : ReferenceObject, IEntityData<FrostySdk.Ebx.WorldPartReferenceObjectData>, ILayerEntity
#else
public class WorldPartReferenceObject : LayerReferenceObject, IEntityData<FrostySdk.Ebx.WorldPartReferenceObjectData>
#endif
{
    public new FrostySdk.Ebx.WorldPartReferenceObjectData Data => data as FrostySdk.Ebx.WorldPartReferenceObjectData;
    public new Assets.WorldPart Blueprint => blueprint as Assets.WorldPart;

    public WorldPartReferenceObject(FrostySdk.Ebx.WorldPartReferenceObjectData inData, Entity inParent)
        : base(inData, inParent)
    {
    }

#if BF1
    public override void AddEntity(Entity inEntity)
    {
        inEntity.SetParent(this);
        Blueprint.Data.Objects.Add(new FrostySdk.Ebx.PointerRef(inEntity.GetRawData()));
    }

    public Layers.SceneLayer GetLayer()
    {
        if (blueprint == null)
            return null;

        //if (IsLogicLayer)
        //    return null;

        string layerName = $"{Path.GetFileName(blueprint.Name)}";
        Layers.SceneLayer layer = new Layers.SceneLayer(this, layerName, new SharpDX.Color4(1.0f, 0.0f, 0.0f, 1.0f));

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
        return layer;
    }
#endif
}
