using Frosty.Core;
using Frosty.Hash;
using FrostySdk;
using FrostySdk.Managers;
using LevelEditorPlugin.Resources;
using System;
using System.Collections.Generic;
using System.IO;

namespace LevelEditorPlugin.Render;

/// <summary>
/// Maps a terrain's 2D layer-combination shaders onto tiling _CS textures.
/// Used only to paint the virtual texture atlas. The terrain preview shader is unchanged.
/// </summary>
public static class TerrainAlbedoResolver
{
    private const int MaxLayers = 24;

    private static readonly Dictionary<string, Dictionary<uint, string[]>> ColorTexturesByShaderDb = new Dictionary<string, Dictionary<uint, string[]>>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<uint, string[]> MeshShaderTextures = new Dictionary<uint, string[]>();
    private static readonly HashSet<uint> VirtualTextureShaders = new HashSet<uint>();
    private static readonly HashSet<uint> SeenShaders = new HashSet<uint>();
    private static readonly HashSet<uint> UnresolvedShaders = new HashSet<uint>();
    private static readonly HashSet<string> IndexedDatabases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> RejectedDatabases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static string activeLevel;

    private static bool IsBattlefield1 => ProfilesLibrary.DataVersion == (int)ProfileVersion.Battlefield1;

    public static bool HasMeshShaderIndex => MeshShaderTextures.Count > 0 || VirtualTextureShaders.Count > 0;

    public static void SetActiveLevel(string levelName)
    {
        activeLevel = levelName;
    }

    public static bool IsVirtualTextureShader(string shaderName)
    {
        return !string.IsNullOrEmpty(shaderName) && VirtualTextureShaders.Contains(HashName(shaderName));
    }

    /// <summary>
    /// Reads the level shaderdb, or a shaderdb from the shader's own bundles, until this shader is known.
    /// </summary>
    public static void ResolveShader(string shaderName)
    {
        if (!IsBattlefield1 || string.IsNullOrEmpty(shaderName))
            return;
        uint hash = HashName(shaderName);
        if (MeshShaderTextures.ContainsKey(hash) || VirtualTextureShaders.Contains(hash) || UnresolvedShaders.Contains(hash))
            return;

        if (!string.IsNullOrEmpty(activeLevel))
        {
            List<ResAssetEntry> levelDatabases = FindShaderDatabases(activeLevel);
            if (levelDatabases.Count > 0)
            {
                foreach (ResAssetEntry shaderDb in levelDatabases)
                {
                    if (IndexDatabase(shaderDb, hash))
                    {
                        FinishResolve(hash);
                        return;
                    }
                }
                UnresolvedShaders.Add(hash);
                return;
            }
        }

        EbxAssetEntry shader = App.AssetManager.GetEbxEntry(shaderName);
        if (shader != null)
        {
            foreach (int bundleId in shader.EnumerateBundles())
            {
                BundleEntry bundle = App.AssetManager.GetBundleEntry(bundleId);
                if (bundle == null || string.IsNullOrEmpty(bundle.Name))
                    continue;
                string name = bundle.Name;
                if (name.StartsWith("win32/", StringComparison.OrdinalIgnoreCase))
                    name = name.Substring("win32/".Length);
                ResAssetEntry shaderDb = App.AssetManager.GetResEntry(name + "/shaderdb");
                if (shaderDb == null || shaderDb.ResType != (uint)ResourceType.IShaderDatabase)
                    continue;
                if (IndexDatabase(shaderDb, hash))
                {
                    FinishResolve(hash);
                    return;
                }
            }
        }

        UnresolvedShaders.Add(hash);
    }

    /// <summary>
    /// Color and normal paths compiled into the level shaderdb for a surface shader.
    /// The fallback shader is unchanged; callers bind these through the names it already understands.
    /// </summary>
    public static bool TryGetMeshTextures(string shaderName, out string colorPath, out string normalPath)
    {
        colorPath = null;
        normalPath = null;
        if (string.IsNullOrEmpty(shaderName) || MeshShaderTextures.Count == 0)
            return false;

        uint hash = unchecked((uint)Fnv1.HashString(shaderName.ToLowerInvariant()));
        if (!MeshShaderTextures.TryGetValue(hash, out string[] paths))
            return false;

        foreach (string path in paths)
        {
            if (colorPath == null && path.EndsWith("_CS", StringComparison.OrdinalIgnoreCase))
                colorPath = path;
        }
        foreach (string path in paths)
        {
            if (IsSurfaceNormal(path))
            {
                normalPath = path;
                break;
            }
        }
        return colorPath != null || normalPath != null;
    }

    private static bool IsSurfaceNormal(string path)
    {
        if (path.EndsWith("_NS", StringComparison.OrdinalIgnoreCase))
            return false;
        return path.EndsWith("_N", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("_NM", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("_NA", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("_NRH", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("_NAH", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ShaderBlock
    {
        public List<string> Paths = new List<string>();
        public bool VirtualTexture;
    }

    private static void IndexMeshShaders(byte[] data, List<ShaderBlock> blocks, int lookup, int perm, int count)
    {
        Dictionary<uint, Dictionary<int, int>> votes = new Dictionary<uint, Dictionary<int, int>>();
        for (int i = 0; i < count; i++)
        {
            uint hash = ReadU32(data, lookup + i * 0x20);
            if (hash == 0)
                continue;
            int blockIndex = (int)ReadU32(data, perm + i * 0x88 + 0x40);
            if ((uint)blockIndex >= (uint)blocks.Count)
                continue;
            if (!votes.TryGetValue(hash, out Dictionary<int, int> blockVotes))
            {
                blockVotes = new Dictionary<int, int>();
                votes.Add(hash, blockVotes);
            }
            blockVotes.TryGetValue(blockIndex, out int n);
            blockVotes[blockIndex] = n + 1;
        }

        foreach (KeyValuePair<uint, Dictionary<int, int>> pair in votes)
        {
            SeenShaders.Add(pair.Key);
            if (MeshShaderTextures.ContainsKey(pair.Key) || VirtualTextureShaders.Contains(pair.Key))
                continue;

            // Permutation stubs are often the single highest block and contain no paths.
            // Sum the blocks that actually carry an albedo, then keep that path list.
            bool anyVirtual = false;
            Dictionary<string, int> colorVotes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string[]> colorPaths = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<int, int> vote in pair.Value)
            {
                ShaderBlock block = blocks[vote.Key];
                if (block.VirtualTexture)
                    anyVirtual = true;
                string color = PickColor(block.Paths);
                if (color == null)
                    continue;
                colorVotes.TryGetValue(color, out int n);
                colorVotes[color] = n + vote.Value;
                colorPaths[color] = block.Paths.ToArray();
            }

            string bestColor = null;
            int bestCount = 0;
            foreach (KeyValuePair<string, int> vote in colorVotes)
            {
                if (vote.Value > bestCount)
                {
                    bestCount = vote.Value;
                    bestColor = vote.Key;
                }
            }
            if (bestColor == null)
            {
                if (anyVirtual)
                    VirtualTextureShaders.Add(pair.Key);
                continue;
            }
            MeshShaderTextures[pair.Key] = colorPaths[bestColor];
        }
    }

    private static string PickColor(List<string> paths)
    {
        if (paths == null)
            return null;
        string diffuse = null;
        string loose = null;
        foreach (string path in paths)
        {
            if (string.IsNullOrEmpty(path) || path.IndexOf("Debug", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (path.EndsWith("_CS", StringComparison.OrdinalIgnoreCase))
                return path;
            if (diffuse == null && path.EndsWith("_D", StringComparison.OrdinalIgnoreCase) && !IsAuxiliary(path))
                diffuse = path;
            else if (loose == null && IsLooseColor(path))
                loose = path;
        }
        return diffuse ?? loose;
    }

    private static bool IsAuxiliary(string path)
    {
        int slash = path.LastIndexOf('/');
        string file = slash >= 0 ? path.Substring(slash + 1) : path;
        return file.IndexOf("Mask", StringComparison.OrdinalIgnoreCase) >= 0
            || file.IndexOf("Noise", StringComparison.OrdinalIgnoreCase) >= 0
            || file.IndexOf("Detail", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsLooseColor(string path)
    {
        if (path.IndexOf('/') < 0 || IsAuxiliary(path) || path.StartsWith("Systems/", StringComparison.OrdinalIgnoreCase))
            return false;
        return !path.EndsWith("_N", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith("_NR", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith("_NM", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith("_NA", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith("_NAA", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith("_NS", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith("_NRH", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith("_NAH", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith("_C", StringComparison.OrdinalIgnoreCase);
    }

    private static void FinishResolve(uint hash)
    {
        if (!MeshShaderTextures.ContainsKey(hash) && !VirtualTextureShaders.Contains(hash))
            UnresolvedShaders.Add(hash);
    }

    private static bool IndexDatabase(ResAssetEntry shaderDb, uint probe)
    {
        if (shaderDb == null || RejectedDatabases.Contains(shaderDb.Name))
            return false;
        if (IndexedDatabases.Contains(shaderDb.Name))
            return SeenShaders.Contains(probe);

        byte[] data = ReadResBytes(shaderDb);
        if (data == null || data.Length < 0x40 || IndexOfU32(data, probe, 0) < 0)
            return false;

        List<ShaderBlock> blocks = ParseBlocks(data);
        if (blocks.Count == 0)
        {
            RejectedDatabases.Add(shaderDb.Name);
            return false;
        }
        if (!TryFindLookup(data, blocks.Count, new[] { probe }, out int lookup, out int perm, out int count))
            return false;

        App.Logger.Log("正在读取网格 shaderdb: {0}", shaderDb.Name);
        IndexMeshShaders(data, blocks, lookup, perm, count);
        IndexedDatabases.Add(shaderDb.Name);
        return SeenShaders.Contains(probe);
    }

    private static uint HashName(string name)
    {
        return unchecked((uint)Fnv1.HashString(name.ToLowerInvariant()));
    }
    private static readonly object CacheLock = new object();

    public static string[] Resolve(Guid terrainGuid, TerrainLayerCombinations combinations)
    {
        try
        {
            if (!IsBattlefield1 || combinations == null || combinations.ColorShaders.Count == 0)
                return [];

            EbxAssetEntry terrainEntry = App.AssetManager.GetEbxEntry(terrainGuid);
            if (terrainEntry == null || string.IsNullOrEmpty(terrainEntry.Name))
                return [];

            List<ResAssetEntry> shaderDbs = FindShaderDatabases(terrainEntry.Name);
            if (shaderDbs.Count == 0)
            {
                App.Logger.LogWarning("地形 {0} 没有找到关卡 shaderdb", terrainEntry.Name);
                return [];
            }

            foreach (ResAssetEntry shaderDb in shaderDbs)
            {
                uint[] wanted = new uint[combinations.ColorShaders.Count];
                for (int i = 0; i < wanted.Length; i++)
                    wanted[i] = unchecked((uint)Fnv1.HashString(combinations.ColorShaders[i].Name.ToLowerInvariant()));

                Dictionary<uint, string[]> colors = GetColorTextures(shaderDb, wanted);
                if (colors == null)
                    continue;

                string[] paths = VoteLayerColors(combinations.ColorShaders, colors);
                int mapped = 0;
                foreach (string path in paths)
                {
                    if (!string.IsNullOrEmpty(path))
                        mapped++;
                }
                if (mapped > 0)
                {
                    App.Logger.Log("虚拟贴图图层: {0} 层来自 {1}", mapped, shaderDb.Name);
                    return paths;
                }
            }

            App.Logger.LogWarning("地形 {0} 的组合着色器在 shaderdb 里没有对上颜色贴图", terrainEntry.Name);
            return [];
        }
        catch (Exception ex)
        {
            App.Logger.LogError("虚拟贴图图层解析失败: {0}", ex.Message);
            return [];
        }
    }

    private static List<ResAssetEntry> FindShaderDatabases(string terrainPath)
    {
        List<ResAssetEntry> found = [];
        string[] parts = terrainPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return found;

        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int end = parts.Length; end >= 2; end--)
        {
            string folder = parts[end - 1];
            string root = string.Join("/", parts, 0, end);
            AddShaderDatabase(found, seen, (root + "/" + folder + "/shaderdb").ToLowerInvariant());
        }
        if (found.Count > 0)
            return found;

        int mapIndex = -1;
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].StartsWith("MP_", StringComparison.OrdinalIgnoreCase) ||
                parts[i].StartsWith("SP_", StringComparison.OrdinalIgnoreCase))
            {
                mapIndex = i;
                break;
            }
        }
        if (mapIndex < 0)
            return found;

        string mapRoot = string.Join("/", parts, 0, mapIndex + 1).ToLowerInvariant() + "/";
        foreach (ResAssetEntry entry in App.AssetManager.EnumerateRes((uint)ResourceType.IShaderDatabase))
        {
            if (entry.Name.StartsWith(mapRoot, StringComparison.OrdinalIgnoreCase) &&
                entry.Name.EndsWith("/shaderdb", StringComparison.OrdinalIgnoreCase))
                AddShaderDatabase(found, seen, entry.Name);
        }
        return found;
    }

    private static void AddShaderDatabase(List<ResAssetEntry> found, HashSet<string> seen, string name)
    {
        if (!seen.Add(name))
            return;
        ResAssetEntry entry = App.AssetManager.GetResEntry(name);
        if (entry != null && entry.ResType == (uint)ResourceType.IShaderDatabase)
            found.Add(entry);
    }

    private static Dictionary<uint, string[]> GetColorTextures(ResAssetEntry shaderDb, uint[] wantedHashes)
    {
        lock (CacheLock)
        {
            if (ColorTexturesByShaderDb.TryGetValue(shaderDb.Name, out Dictionary<uint, string[]> cached))
                return cached;
        }

        App.Logger.Log("正在读取地形 shaderdb: {0}", shaderDb.Name);
        byte[] data = ReadResBytes(shaderDb);
        if (data == null || data.Length < 0x40)
            return null;

        List<ShaderBlock> blocks = ParseBlocks(data);
        if (blocks.Count == 0 || !TryFindLookup(data, blocks.Count, wantedHashes, out int lookup, out int perm, out int count))
            return null;

        Dictionary<uint, Dictionary<int, int>> blockVotes = new Dictionary<uint, Dictionary<int, int>>();
        for (int i = 0; i < count; i++)
        {
            uint hash = ReadU32(data, lookup + i * 0x20);
            if (hash == 0)
                continue;
            int blockIndex = (int)ReadU32(data, perm + i * 0x88 + 0x40);
            if ((uint)blockIndex >= (uint)blocks.Count)
                continue;
            if (!blockVotes.TryGetValue(hash, out Dictionary<int, int> votes))
            {
                votes = new Dictionary<int, int>();
                blockVotes.Add(hash, votes);
            }
            votes.TryGetValue(blockIndex, out int n);
            votes[blockIndex] = n + 1;
        }

        IndexMeshShaders(data, blocks, lookup, perm, count);

        Dictionary<uint, string[]> colors = new Dictionary<uint, string[]>();
        foreach (KeyValuePair<uint, Dictionary<int, int>> pair in blockVotes)
        {
            int bestBlock = -1;
            int bestCount = 0;
            foreach (KeyValuePair<int, int> vote in pair.Value)
            {
                if (vote.Value > bestCount)
                {
                    bestCount = vote.Value;
                    bestBlock = vote.Key;
                }
            }
            if (bestBlock < 0)
                continue;

            List<string> cs = [];
            foreach (string path in blocks[bestBlock].Paths)
            {
                if (path.EndsWith("_CS", StringComparison.OrdinalIgnoreCase))
                    cs.Add(path);
            }
            if (cs.Count > 0)
                colors[pair.Key] = cs.ToArray();
        }

        lock (CacheLock)
            ColorTexturesByShaderDb[shaderDb.Name] = colors;
        IndexedDatabases.Add(shaderDb.Name);
        return colors;
    }

    private static string[] VoteLayerColors(List<TerrainColorShader> shaders, Dictionary<uint, string[]> colors)
    {
        Dictionary<int, Dictionary<string, int>> votes = new Dictionary<int, Dictionary<string, int>>();
        int matched = 0;
        foreach (TerrainColorShader shader in shaders)
        {
            if (shader.Layers == null || shader.Layers.Length == 0)
                continue;
            uint hash = unchecked((uint)Fnv1.HashString(shader.Name.ToLowerInvariant()));
            if (!colors.TryGetValue(hash, out string[] cs) || cs.Length != shader.Layers.Length)
                continue;
            HashSet<string> unique = new HashSet<string>(cs, StringComparer.OrdinalIgnoreCase);
            if (unique.Count != cs.Length)
                continue;

            matched++;
            for (int i = 0; i < shader.Layers.Length; i++)
            {
                int layer = shader.Layers[i];
                if (layer < 0 || layer >= MaxLayers)
                    continue;
                if (!votes.TryGetValue(layer, out Dictionary<string, int> layerVotes))
                {
                    layerVotes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    votes.Add(layer, layerVotes);
                }
                layerVotes.TryGetValue(cs[i], out int n);
                layerVotes[cs[i]] = n + 1;
            }
        }
        if (matched == 0 || votes.Count == 0)
            return [];

        int maxLayer = 0;
        foreach (int layer in votes.Keys)
        {
            if (layer > maxLayer)
                maxLayer = layer;
        }

        string[] paths = new string[Math.Min(MaxLayers, maxLayer + 1)];
        foreach (KeyValuePair<int, Dictionary<string, int>> pair in votes)
        {
            string best = null;
            int bestCount = 0;
            foreach (KeyValuePair<string, int> vote in pair.Value)
            {
                if (vote.Value > bestCount)
                {
                    bestCount = vote.Value;
                    best = vote.Key;
                }
            }
            if (string.IsNullOrEmpty(best) || App.AssetManager.GetEbxEntry(best) == null)
                continue;
            paths[pair.Key] = best;
        }
        return paths;
    }

    private static byte[] ReadResBytes(ResAssetEntry entry)
    {
        Stream stream = App.AssetManager.GetRes(entry);
        if (stream == null)
            return null;
        using (stream)
        {
            if (stream is MemoryStream memory && memory.TryGetBuffer(out ArraySegment<byte> buffer) && buffer.Offset == 0 && buffer.Count == memory.Length)
                return buffer.Array;
            using (MemoryStream copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                return copy.ToArray();
            }
        }
    }

    private static List<ShaderBlock> ParseBlocks(byte[] data)
    {
        int declared = (int)ReadU32(data, 0x18);
        if (declared <= 0 || declared > 100000)
            return [];

        byte[] virtualNeedle = System.Text.Encoding.ASCII.GetBytes("virtualTextureAtlas0");
        List<ShaderBlock> blocks = new List<ShaderBlock>(declared);
        int pos = 0x1C;
        for (int n = 0; n < declared; n++)
        {
            if (pos + 8 > data.Length)
                break;
            int size = (int)ReadU32(data, pos);
            if (size < 0x38 || pos + size > data.Length)
                break;

            long off0 = ReadU64(data, pos + 8);
            long off2 = ReadU64(data, pos + 8 + 16);
            ShaderBlock block = new ShaderBlock
            {
                VirtualTexture = ContainsBytes(data, pos, size, virtualNeedle)
            };
            if (off0 >= 0 && off2 >= off0 && off2 <= size)
            {
                long cursor = pos + off0;
                long end = pos + off2;
                while (cursor + 0x98 <= end)
                {
                    string path = ReadCString(data, (int)(cursor + 8), 0x80);
                    if (path.Length > 0)
                        block.Paths.Add(path);
                    cursor += 0x98;
                }
            }
            blocks.Add(block);
            pos += size;
        }
        return blocks;
    }

    private static bool ContainsBytes(byte[] data, int pos, int size, byte[] needle)
    {
        int end = pos + size - needle.Length;
        for (int i = pos; i <= end; i++)
        {
            int j = 0;
            for (; j < needle.Length; j++)
            {
                if (data[i + j] != needle[j])
                    break;
            }
            if (j == needle.Length)
                return true;
        }
        return false;
    }

    private static bool TryFindLookup(byte[] data, int blockCount, uint[] wantedHashes, out int lookup, out int perm, out int count)
    {
        lookup = 0;
        perm = 0;
        count = 0;
        if (wantedHashes == null || wantedHashes.Length == 0)
            return false;

        int probes = Math.Min(wantedHashes.Length, 8);
        for (int probe = 0; probe < probes; probe++)
        {
            int hit = 0;
            int hits = 0;
            while (hits < 8 && (hit = IndexOfU32(data, wantedHashes[probe], hit)) >= 0)
            {
                hits++;
                if (TryAcceptLookup(data, blockCount, hit, out lookup, out perm, out count))
                    return true;
                hit += 4;
            }
        }
        return false;
    }

    private static bool TryAcceptLookup(byte[] data, int blockCount, int hit, out int lookup, out int perm, out int count)
    {
        lookup = 0;
        perm = 0;
        count = 0;
        int cursor = hit;
        int steps = 0;
        while (cursor >= 0x20 && steps < 200000 && ReadU32(data, cursor + 0x1C) == 0 && ReadU32(data, cursor) != 0)
        {
            cursor -= 0x20;
            steps++;
        }

        int records = cursor + 0x20;
        if (records < 4 || hit < records)
            return false;
        int candidate = (int)ReadU32(data, records - 4);
        if (candidate < 1000 || candidate > 200000)
            return false;
        long tableEnd = records + (long)candidate * 0x20;
        if (tableEnd > data.Length || hit >= tableEnd || ReadU32(data, records + 0x1C) != 0)
            return false;

        int permAt = records - 4 - candidate * 0x88;
        if (permAt < 0 || permAt + 0x88 > data.Length)
            return false;

        int ok = 0;
        for (int i = 0; i < Math.Min(8, candidate); i++)
        {
            int rec = permAt + i * 0x88;
            if (rec + 0x44 > data.Length)
                return false;
            if (ReadU32(data, rec + 0x20) == 0xFFFFFFFFu && ReadU32(data, rec + 0x40) < (uint)blockCount)
                ok++;
        }
        if (ok < 4)
            return false;

        lookup = records;
        perm = permAt;
        count = candidate;
        return true;
    }

    private static unsafe int IndexOfU32(byte[] data, uint value, int from)
    {
        int start = from <= 0 ? 0 : from;
        if ((start & 3) != 0)
            start = (start + 3) & ~3;
        if (start > data.Length - 4)
            return -1;
        fixed (byte* ptr = data)
        {
            for (int i = start; i <= data.Length - 4; i += 4)
            {
                if (*(uint*)(ptr + i) == value)
                    return i;
            }
        }
        return -1;
    }

    private static string ReadCString(byte[] data, int offset, int max)
    {
        if (offset < 0 || offset >= data.Length)
            return "";
        int end = Math.Min(data.Length, offset + max);
        int i = offset;
        while (i < end && data[i] != 0)
            i++;
        if (i == offset)
            return "";
        return System.Text.Encoding.ASCII.GetString(data, offset, i - offset);
    }

    private static uint ReadU32(byte[] data, int offset) => BitConverter.ToUInt32(data, offset);
    private static long ReadU64(byte[] data, int offset) => BitConverter.ToInt64(data, offset);
}
