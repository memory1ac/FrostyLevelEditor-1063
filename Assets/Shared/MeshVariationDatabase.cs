using Frosty.Core;
using FrostySdk.Ebx;
using FrostySdk.IO;
using FrostySdk.Managers;
using System;
using System.Collections.Generic;

namespace LevelEditorPlugin.Assets;

[AssetBinding(DataType = typeof(FrostySdk.Ebx.MeshVariationDatabase))]
public class MeshVariationDatabase : Asset, IAssetData<FrostySdk.Ebx.MeshVariationDatabase>
{
    public FrostySdk.Ebx.MeshVariationDatabase Data => data as FrostySdk.Ebx.MeshVariationDatabase;

    private static Dictionary<uint, FrostySdk.IO.EbxImportReference> variationNameCache = [];

    public MeshVariationDatabase(Guid fileGuid, FrostySdk.Ebx.MeshVariationDatabase inData)
        : base(fileGuid, inData)
    {
    }

    public PointerRef GetVariation(uint variationHash)
    {
        if (variationHash == 0)
            return new PointerRef();

        if (variationNameCache.Count == 0)
        {
            foreach (EbxAssetEntry ebxEntry in App.AssetManager.EnumerateEbx("ObjectVariation"))
            {
                EbxAsset ebxAsset = App.AssetManager.GetEbx(ebxEntry);
                ObjectVariation variation = ebxAsset.RootObject as ObjectVariation;

                variationNameCache.Add(variation.NameHash, new FrostySdk.IO.EbxImportReference() { FileGuid = ebxEntry.Guid, ClassGuid = ebxAsset.RootInstanceGuid });
            }
        }

        if (!variationNameCache.ContainsKey(variationHash))
        {
            // @todo: what to do when variation cannot be found
            return new PointerRef();
        }
        return new PointerRef(variationNameCache[variationHash]);
    }
}
