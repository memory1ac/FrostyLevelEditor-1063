using Frosty.Core;
using FrostySdk.Ebx;
using FrostySdk.IO;
using FrostySdk.Managers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LevelEditorPlugin.Assets;

[AssetBinding(DataType = typeof(FrostySdk.Ebx.MeshVariationDatabase))]
public class MeshVariationDatabase : Asset, IAssetData<FrostySdk.Ebx.MeshVariationDatabase>
{
    public FrostySdk.Ebx.MeshVariationDatabase Data => data as FrostySdk.Ebx.MeshVariationDatabase;

    private static readonly Dictionary<uint, (EbxImportReference Reference, string Name)> variationNameCache = [];

    public MeshVariationDatabase(Guid fileGuid, FrostySdk.Ebx.MeshVariationDatabase inData)
        : base(fileGuid, inData)
    {
    }

    private static void EnsureVariationCache()
    {
        if (variationNameCache.Count != 0)
            return;

        foreach (EbxAssetEntry ebxEntry in App.AssetManager.EnumerateEbx("ObjectVariation"))
        {
            EbxAsset ebxAsset = App.AssetManager.GetEbx(ebxEntry);
            if (ebxAsset?.RootObject is not ObjectVariation variation)
                continue;

            variationNameCache[variation.NameHash] = (
                new EbxImportReference() { FileGuid = ebxEntry.Guid, ClassGuid = ebxAsset.RootInstanceGuid },
                variation.Name);
        }
    }

    public PointerRef GetVariation(uint variationHash)
    {
        if (variationHash == 0)
            return new PointerRef();

        EnsureVariationCache();
        if (!variationNameCache.TryGetValue(variationHash, out (EbxImportReference Reference, string Name) entry))
        {
            // @todo: what to do when variation cannot be found
            return new PointerRef();
        }
        return new PointerRef(entry.Reference);
    }

    public string GetVariationName(uint variationHash)
    {
        if (variationHash == 0)
            return null;

        EnsureVariationCache();
        return variationNameCache.TryGetValue(variationHash, out (EbxImportReference Reference, string Name) entry) ? entry.Name : null;
    }

    public Guid GetVariationFileGuid(uint variationHash)
    {
        if (variationHash == 0)
            return Guid.Empty;

        EnsureVariationCache();
        return variationNameCache.TryGetValue(variationHash, out (EbxImportReference Reference, string Name) entry) ? entry.Reference.FileGuid : Guid.Empty;
    }

    /// <summary>
    /// 返回本 MeshVariationDb 中该网格拥有的所有 VariationAssetNameHash（包含 0 = 默认变体）。
    /// </summary>
    public IEnumerable<uint> GetVariationHashes(Guid meshGuid)
    {
        List<uint> hashes = [];
        dynamic database = Data;
        if (database == null)
            return hashes;

        try
        {
            foreach (dynamic entry in database.Entries)
            {
                PointerRef meshRef = entry.Mesh;
                if (meshRef.Type != PointerRefType.External || meshRef.External.FileGuid != meshGuid)
                    continue;

                try
                {
                    uint hash = entry.VariationAssetNameHash;
                    if (!hashes.Contains(hash))
                        hashes.Add(hash);
                }
                catch (Exception)
                {
                    // 老版本 Profile 没有 VariationAssetNameHash 字段，当作默认变体 0
                    if (!hashes.Contains(0))
                        hashes.Add(0);
                }
            }
        }
        catch (Exception ex)
        {
            App.Logger?.LogWarning($"[MeshVariationDb] 读取 {Name} 的 Entries 失败：{ex.Message}");
        }

        return hashes;
    }

    public bool HasVariation(Guid meshGuid, uint variationHash)
    {
        return GetVariationHashes(meshGuid).Contains(variationHash);
    }
}
