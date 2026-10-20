using Frosty.Core;
using FrostySdk;
using FrostySdk.IO;
using FrostySdk.Managers;
using LevelEditorPlugin.Resources;
using System;
using System.Collections.Generic;
using System.IO;

namespace LevelEditorPlugin.BF1Core.Utils;

public struct HeightNodeResInfo
{
    public QuadTreeNodeId Id;
    public int HeightOffset;
    public int MinMaxOffset;
    public bool HasData;
    public bool Persistent;
    public bool Pruned;
}

/// <summary>
/// Heightfield 在 Res / Chunk 里的偏移，以及读写缓冲。
/// </summary>
public static class TerrainHeightStorage
{
    public static Dictionary<(ushort X, ushort Y, byte Level), (Guid ChunkId, int BaseOffset)> BuildNodeToChunkOffsetMap(TerrainStreamingTree tree, HeightfieldTree htree)
    {
        var map = new Dictionary<(ushort, ushort, byte), (Guid, int)>();
        int dataSize = htree.DataSize;

        foreach (RasterTreeNode rnode in htree.nodes)
        {
            if (rnode is not HeightfieldTreeNode node)
                continue;
            if (!node.hasData || node.hasPersistentData)
                continue;

            var id = node.id;
            var key = (id.indexX, id.indexY, id.level);

            TerrainStreamingTreeNode snode = tree.GetNode(id);
            if (snode != null && snode.lod0Id != Guid.Empty)
            {
                map[key] = (snode.lod0Id, 0);
                continue;
            }

            if (node.parent == null)
                continue;

            TerrainStreamingTreeNode parentSnode = tree.GetNode(node.parent.id);
            if (parentSnode == null || parentSnode.lod1Guid == Guid.Empty)
                continue;

            int childIndex = node.parent.children.IndexOf(node);
            if (childIndex < 0 || childIndex > 3)
                continue;

            int blockIndex = 3 - childIndex;
            map[key] = (parentSnode.lod1Guid, blockIndex * dataSize);
        }

        return map;
    }

    public static Dictionary<(ushort X, ushort Y, byte Level), HeightNodeResInfo> CollectResNodeInfos(byte[] resBytes, HeightfieldTree htree)
    {
        var map = new Dictionary<(ushort, ushort, byte), HeightNodeResInfo>();
        if (resBytes == null || resBytes.Length == 0)
            return map;

        try
        {
            using var stream = new MemoryStream(resBytes, false);
            using var reader = new NativeReader(stream);
            reader.ReadUInt();
            reader.ReadBoolean();
            reader.ReadFloat();
            reader.ReadFloat();
            reader.ReadUInt();
            reader.ReadUInt();
            reader.ReadUInt();
            reader.ReadBoolean();

            byte rasterTreeType = reader.ReadByte();
            while (rasterTreeType != 0xFF)
            {
                uint rasterTreeLoadSize = reader.ReadUInt();
                long startPos = reader.Position;
                if (rasterTreeType == (byte)RasterTreeTypes.HeightfieldTreeType)
                {
                    CollectInHeightfieldBlock(reader, htree, map);
                    break;
                }

                reader.Position = startPos + rasterTreeLoadSize;
                rasterTreeType = reader.ReadByte();
            }
        }
        catch (Exception ex)
        {
            App.Logger.LogError($"解析 Heightfield 节点偏移失败: {ex.Message}");
        }

        return map;
    }

    private static void CollectInHeightfieldBlock(NativeReader reader, HeightfieldTree htree, Dictionary<(ushort, ushort, byte), HeightNodeResInfo> map)
    {
        reader.ReadUInt();
        reader.ReadUInt();
        reader.ReadUInt();
        reader.ReadInt();
        reader.ReadFloat();
        reader.ReadFloat();
        reader.ReadInt();
        reader.ReadBoolean();
        reader.ReadFloat();
        uint minMaxStackDepth = reader.ReadUInt();
        uint occluderGridStackDepth = reader.ReadUInt();
        reader.ReadUInt();
        reader.ReadUInt();
        reader.ReadUInt();
        reader.ReadFloat();

        uint minMaxStackSize = ComputeMinMaxStackSize(minMaxStackDepth);
        ComputeOccluderGridStackSize(occluderGridStackDepth);

#if BFN
        uint nodeCount = reader.ReadUInt(Endian.Big);
        reader.ReadUInt(Endian.Big);
#else
        uint nodeCount = reader.ReadUInt();
        reader.ReadUInt();
#endif
        reader.ReadUInt();
        reader.ReadUInt();
        reader.ReadUInt();

        int id = 0;
        int index = 1;
        int heightBytes = TerrainMinMaxStack.HeightBytes((int)htree.nodeSamplesPerSide);
        WalkNodes(reader, id, ref index, new QuadTreeNodeId(), nodeCount, (int)(minMaxStackSize * 2), htree.DataSize, heightBytes, map);
    }

    private static void WalkNodes(NativeReader reader, int id, ref int index, QuadTreeNodeId nodeId, uint nodeCount, int minMaxBytes, int dataSize, int heightBytes, Dictionary<(ushort, ushort, byte), HeightNodeResInfo> map)
    {
        if (id >= nodeCount)
            return;

        reader.ReadFloat();
        reader.ReadFloat();
        reader.ReadFloat();
        reader.ReadFloat();
        reader.ReadFloat();
        reader.ReadFloat();

        if (ProfilesLibrary.DataVersion == 20170321 || ProfilesLibrary.DataVersion == 20171110)
            reader.ReadFloat();

        bool nodeDisabled = reader.ReadBoolean();
        if (nodeDisabled)
            return;

        bool pruned = reader.ReadBoolean();
        bool hasData = reader.ReadBoolean();
        reader.ReadBoolean();
        bool persistent = reader.ReadBoolean();

        var info = new HeightNodeResInfo
        {
            Id = nodeId,
            HeightOffset = -1,
            MinMaxOffset = -1,
            HasData = hasData,
            Persistent = persistent,
            Pruned = pruned
        };

        if (pruned)
        {
            info.MinMaxOffset = (int)reader.Position;
            reader.ReadBytes(minMaxBytes);
        }

        if (hasData && persistent)
        {
            info.HeightOffset = (int)reader.Position;
            info.MinMaxOffset = info.HeightOffset + heightBytes;
            reader.Position = info.HeightOffset + dataSize;
        }

        map[(nodeId.indexX, nodeId.indexY, nodeId.level)] = info;

        bool hasChildren = reader.ReadBoolean();
        if (hasChildren)
        {
            int firstChildIndex = index;
            index += 4;
            QuadTreeNodeId childNodeId = new QuadTreeNodeId { level = (byte)(nodeId.level + 1) };
            for (int i = 0; i < 4; i++)
            {
                childNodeId.indexX = (ushort)(QuadTreeNodeId.quadtreeNodeChildOffsetX[i] + 2 * nodeId.indexX);
                childNodeId.indexY = (ushort)(QuadTreeNodeId.quadtreeNodeChildOffsetY[i] + 2 * nodeId.indexY);
                WalkNodes(reader, firstChildIndex + i, ref index, childNodeId, nodeCount, minMaxBytes, dataSize, heightBytes, map);
            }
        }
    }

    public static byte[] GetChunkBytes(ChunkAssetEntry entry)
    {
        try
        {
            using Stream s = App.AssetManager.GetChunk(entry);
            if (s == null)
                return null;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            App.Logger.LogError($"读取 Chunk 失败: {ex.Message}");
            return null;
        }
    }

    public static byte[] GetResBytes(ResAssetEntry entry)
    {
        try
        {
            using Stream s = App.AssetManager.GetRes(entry);
            if (s == null)
                return null;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            App.Logger.LogError($"读取 Res 失败: {ex.Message}");
            return null;
        }
    }

    public static HashSet<Guid> CollectTerrainChunkIds(TerrainStreamingTree tree)
    {
        var set = new HashSet<Guid>();
        CollectChunkIdsRecursive(tree.GetRootNode(), set);
        return set;
    }

    private static void CollectChunkIdsRecursive(TerrainStreamingTreeNode node, HashSet<Guid> set)
    {
        if (node == null)
            return;
        if (node.lod0Id != Guid.Empty)
            set.Add(node.lod0Id);
        if (node.lod1Guid != Guid.Empty)
            set.Add(node.lod1Guid);
        foreach (var child in node.children)
            CollectChunkIdsRecursive(child, set);
    }

    public static uint ComputeMinMaxStackSize(uint minMaxStackDepth)
    {
        if (minMaxStackDepth == 0)
            return 0;
        uint tmp = (uint)(1 << (int)(minMaxStackDepth - 1));
        int cnt = 0;
        uint minMaxStackSize = 0;
        do
        {
            uint b = (uint)((long)tmp >> cnt++);
            minMaxStackSize += 2 * b * b;
        } while (cnt < minMaxStackDepth);
        return minMaxStackSize;
    }

    public static uint ComputeOccluderGridStackSize(uint occluderGridStackDepth)
    {
        if (occluderGridStackDepth == 0)
            return 0;
        uint tmp = (uint)(1 << (int)(occluderGridStackDepth - 1));
        int cnt = 0;
        uint occluderGridStackSize = 0;
        do
        {
            uint b = (uint)((int)tmp >> cnt++);
            occluderGridStackSize += (b + 1) * (b + 1);
        } while (cnt < occluderGridStackDepth);
        return occluderGridStackSize;
    }
}
