using System;

namespace LevelEditorPlugin.Assets;

[AssetBinding(DataType = typeof(FrostySdk.Ebx.NetworkRegistryAsset))]
public class NetworkRegistryAsset : Asset, IAssetData<FrostySdk.Ebx.NetworkRegistryAsset>
{
    public FrostySdk.Ebx.NetworkRegistryAsset Data => data as FrostySdk.Ebx.NetworkRegistryAsset;

    public NetworkRegistryAsset(Guid fileGuid, FrostySdk.Ebx.NetworkRegistryAsset inData)
        : base(fileGuid, inData)
    {
    }
}
