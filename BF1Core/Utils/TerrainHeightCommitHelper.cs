using Frosty.Core;
using FrostySdk.Managers;
using LevelEditorPlugin.Entities;
using LevelEditorPlugin.Resources;
using System;
using System.Collections.Generic;

namespace LevelEditorPlugin.BF1Core.Utils;

/// <summary>
/// 把当前会话里改过的高度样本写回 Heightfield，并更新包围盒 Y 与 min/max 栈。
/// </summary>
public static class TerrainHeightCommitHelper
{
    public static void CommitPendingEdits(TerrainEntity terrainEntity)
    {
        var edits = TerrainEditState.PendingEdits;
        if (terrainEntity == null || edits.Count == 0)
        {
            App.Logger.Log("当前没有待写回的地形高度修改");
            return;
        }

        var data = terrainEntity.Terrain.Data;
        ResAssetEntry resEntry = App.AssetManager.GetResEntry(data.TerrainStreamingTreeResource);
        if (resEntry == null)
        {
            App.Logger.LogError("TerrainStreamingTree Res 未找到，无法写回地形高度");
            return;
        }

        TerrainStreamingTree tree = App.AssetManager.GetResAs<TerrainStreamingTree>(resEntry);
        HeightfieldTree htree = tree?.GetRasterTree(RasterTreeTypes.HeightfieldTreeType) as HeightfieldTree;
        if (htree == null)
        {
            App.Logger.LogError("无法加载 HeightfieldTree，无法写回地形高度");
            return;
        }

        float worldSizeY = htree.worldSizeY;
        if (worldSizeY <= 0.0f)
        {
            App.Logger.LogError("HeightfieldTree.worldSizeY 无效，无法换算高度");
            return;
        }

        int dim = (int)htree.nodeSamplesPerSide;
        var nodeToChunkMap = TerrainHeightStorage.BuildNodeToChunkOffsetMap(tree, htree);
        byte[] resBytes = TerrainHeightStorage.GetResBytes(resEntry);
        if (resBytes == null || resBytes.Length == 0)
        {
            App.Logger.LogError("无法读取 TerrainStreamingTree Res 数据");
            return;
        }

        var resInfos = TerrainHeightStorage.CollectResNodeInfos(resBytes, htree);
        var chunkBuffers = new Dictionary<Guid, byte[]>();
        var touchedNodes = new HashSet<(ushort X, ushort Y, byte Level)>();
        int resWrites = 0;

        foreach (var e in edits)
        {
            if (e.SampleIndex < 0 || e.SampleIndex >= dim * dim)
                continue;

            var key = (e.NodeIndexX, e.NodeIndexY, e.NodeLevel);
            ushort sample = TerrainHeightCodec.EncodeLocalY(e.TargetLocalY, worldSizeY);

            if (nodeToChunkMap.TryGetValue(key, out var chunkInfo))
            {
                if (!chunkBuffers.TryGetValue(chunkInfo.ChunkId, out byte[] chunkBytes))
                {
                    ChunkAssetEntry chunkEntry = App.AssetManager.GetChunkEntry(chunkInfo.ChunkId);
                    if (chunkEntry == null)
                        continue;
                    chunkBytes = TerrainHeightStorage.GetChunkBytes(chunkEntry);
                    if (chunkBytes == null)
                        continue;
                    chunkBuffers[chunkInfo.ChunkId] = chunkBytes;
                }

                TerrainHeightCodec.WriteU16(chunkBytes, chunkInfo.BaseOffset + e.SampleIndex * 2, sample);
                touchedNodes.Add(key);
                continue;
            }

            if (resInfos.TryGetValue(key, out HeightNodeResInfo info) && info.HeightOffset >= 0)
            {
                TerrainHeightCodec.WriteU16(resBytes, info.HeightOffset + e.SampleIndex * 2, sample);
                touchedNodes.Add(key);
                resWrites++;
            }
        }

        foreach (var key in touchedNodes)
        {
            HeightfieldTreeNode node = FindNode(htree, key);
            if (node == null)
                continue;

            byte[] buffer = resBytes;
            int heightOffset = -1;
            if (nodeToChunkMap.TryGetValue(key, out var chunkInfo) && chunkBuffers.TryGetValue(chunkInfo.ChunkId, out byte[] chunkBytes))
            {
                buffer = chunkBytes;
                heightOffset = chunkInfo.BaseOffset;
            }
            else if (resInfos.TryGetValue(key, out HeightNodeResInfo info) && info.HeightOffset >= 0)
            {
                heightOffset = info.HeightOffset;
            }

            if (heightOffset < 0)
                continue;

            TerrainMinMaxStack.Rebuild(buffer, heightOffset, dim, htree.minMaxStackDepth);
            TerrainMinMaxStack.UpdateBBoxFromSamples(node, buffer, heightOffset, dim, worldSizeY);
            TerrainMinMaxStack.WriteNodeBBoxY(resBytes, node);
            UpdateAncestors(node, resBytes, resInfos, worldSizeY, htree);
        }

        App.AssetManager.ModifyRes(resEntry.ResRid, resBytes, resEntry.ResMeta);

        int modifiedChunks = 0;
        foreach (var kv in chunkBuffers)
        {
            if (App.AssetManager.ModifyChunk(kv.Key, kv.Value))
                modifiedChunks++;
        }

        App.Logger.Log($"地形高度已写入 Res 中 {resWrites} 处样本，并更新 {modifiedChunks} 个 chunk。");
        TerrainEditState.ClearEdits();
    }

    private static HeightfieldTreeNode FindNode(HeightfieldTree htree, (ushort X, ushort Y, byte Level) key)
    {
        foreach (RasterTreeNode rnode in htree.nodes)
        {
            if (rnode is HeightfieldTreeNode node &&
                node.id.indexX == key.X && node.id.indexY == key.Y && node.id.level == key.Level)
                return node;
        }
        return null;
    }

    private static void UpdateAncestors(HeightfieldTreeNode node, byte[] resBytes, Dictionary<(ushort, ushort, byte), HeightNodeResInfo> resInfos, float worldSizeY, HeightfieldTree htree)
    {
        HeightfieldTreeNode current = node.parent as HeightfieldTreeNode;
        while (current != null)
        {
            TerrainMinMaxStack.UnionChildrenBBoxY(current);
            TerrainMinMaxStack.WriteNodeBBoxY(resBytes, current);

            var key = (current.id.indexX, current.id.indexY, current.id.level);
            if (resInfos.TryGetValue(key, out HeightNodeResInfo info) && info.Pruned && info.MinMaxOffset >= 0 && info.HeightOffset < 0)
            {
                ushort min = TerrainHeightCodec.EncodeLocalY(current.boundingBox.min.y, worldSizeY);
                ushort max = TerrainHeightCodec.EncodeLocalY(current.boundingBox.max.y, worldSizeY);
                TerrainMinMaxStack.FillConstant(resBytes, info.MinMaxOffset, htree.minMaxStackSize, min, max);
            }

            current = current.parent as HeightfieldTreeNode;
        }
    }
}
