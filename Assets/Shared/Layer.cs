using System;

namespace LevelEditorPlugin.Assets;

[AssetBinding(DataType = typeof(FrostySdk.Ebx.LayerData))]
public class Layer : PrefabBlueprint, IAssetData<FrostySdk.Ebx.LayerData>
{
    public new FrostySdk.Ebx.LayerData Data => data as FrostySdk.Ebx.LayerData;

    public Layer(Guid fileGuid, FrostySdk.Ebx.LayerData inData)
        : base(fileGuid, inData)
    {
    }
}
