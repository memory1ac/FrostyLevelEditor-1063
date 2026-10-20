using System;

namespace LevelEditorPlugin.Assets;

[AssetBinding(DataType = typeof(FrostySdk.Ebx.LogicPrefabBlueprint))]
public class LogicPrefabBlueprint : PrefabBlueprint, IAssetData<FrostySdk.Ebx.LogicPrefabBlueprint>
{
    public new FrostySdk.Ebx.LogicPrefabBlueprint Data => data as FrostySdk.Ebx.LogicPrefabBlueprint;

    public LogicPrefabBlueprint(Guid fileGuid, FrostySdk.Ebx.LogicPrefabBlueprint inData)
        : base(fileGuid, inData)
    {
    }
}
