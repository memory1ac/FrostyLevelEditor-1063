using LevelEditorPlugin.Managers;
using System.Collections.Generic;

namespace LevelEditorPlugin.Entities;

[EntityBinding(DataType = typeof(FrostySdk.Ebx.EffectReferenceObjectData))]
public class EffectReferenceObject : SpatialReferenceObject, IEntityData<FrostySdk.Ebx.EffectReferenceObjectData>
{
    public new FrostySdk.Ebx.EffectReferenceObjectData Data => data as FrostySdk.Ebx.EffectReferenceObjectData;
    public new Assets.EffectBlueprint Blueprint => blueprint as Assets.EffectBlueprint;
    public new EffectEntity RootEntity => entities[0] as EffectEntity;
    public override IEnumerable<ConnectionDesc> Events
    {
        get =>
        [
            new ConnectionDesc("Start", Direction.In),
            new ConnectionDesc("Stop", Direction.In)
        ];
    }

    public EffectReferenceObject(FrostySdk.Ebx.EffectReferenceObjectData inData, Entity inParent)
        : base(inData, inParent)
    {
    }

    protected override void Initialize()
    {
        blueprint = LoadedAssetManager.Instance.LoadAsset<Assets.EffectBlueprint>(this, Data.Blueprint);
    }

    public override void Destroy()
    {
        LoadedAssetManager.Instance.UnloadAsset(Blueprint);
    }
}
