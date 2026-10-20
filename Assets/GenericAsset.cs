using System;

namespace LevelEditorPlugin.Assets;

public class GenericAsset<T> : Asset, IAssetData<T> where T : FrostySdk.Ebx.Asset
{
    public T Data => data as T;

    public GenericAsset(Guid fileGuid, T inData)
        : base(fileGuid, inData)
    {
    }
}
