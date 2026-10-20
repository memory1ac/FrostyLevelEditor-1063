using Frosty.Core;
using FrostySdk.Managers;
using LevelEditorPlugin.Resources;
using SharpDX;
using System;
using System.Collections.Generic;

namespace LevelEditorPlugin.BF1Core.Utils;

/// <summary>
/// 一键把地形写成指定世界高度：按 worldSizeY/65535 编码，并更新包围盒 Y 与 min/max 栈。
/// </summary>
public static class TerrainFlattenHelper
{
    public static bool FlattenTerrain(Entities.TerrainEntity terrainEntity, float worldHeightY = 0.0f)
    {
        if (terrainEntity?.Terrain?.Data == null)
            return false;

        var data = terrainEntity.Terrain.Data;
        ResAssetEntry resEntry = App.AssetManager.GetResEntry(data.TerrainStreamingTreeResource);
        if (resEntry == null)
        {
            App.Logger.LogError("TerrainStreamingTree Res 未找到");
            return false;
        }

        TerrainStreamingTree tree = App.AssetManager.GetResAs<TerrainStreamingTree>(resEntry);
        HeightfieldTree htree = tree?.GetRasterTree(RasterTreeTypes.HeightfieldTreeType) as HeightfieldTree;
        if (htree == null)
        {
            App.Logger.LogError("无法加载 HeightfieldTree");
            return false;
        }

        float worldSizeY = htree.worldSizeY;
        if (worldSizeY <= 0.0f)
        {
            App.Logger.LogError("HeightfieldTree.worldSizeY 无效，无法换算高度");
            return false;
        }

        int dim = (int)htree.nodeSamplesPerSide;
        Matrix world = terrainEntity.GetTransform();
        var nodeToChunkMap = TerrainHeightStorage.BuildNodeToChunkOffsetMap(tree, htree);
        byte[] resBytes = TerrainHeightStorage.GetResBytes(resEntry);
        if (resBytes == null || resBytes.Length == 0)
        {
            App.Logger.LogError("无法读取 TerrainStreamingTree Res 数据");
            return false;
        }

        var resInfos = TerrainHeightStorage.CollectResNodeInfos(resBytes, htree);
        var chunkBuffers = new Dictionary<Guid, byte[]>();

        foreach (RasterTreeNode rnode in htree.nodes)
        {
            if (rnode is not HeightfieldTreeNode node || !node.hasData)
                continue;

            var key = (node.id.indexX, node.id.indexY, node.id.level);
            byte[] buffer = null;
            int heightOffset = -1;

            if (nodeToChunkMap.TryGetValue(key, out var chunkInfo))
            {
                if (!chunkBuffers.TryGetValue(chunkInfo.ChunkId, out buffer))
                {
                    ChunkAssetEntry chunkEntry = App.AssetManager.GetChunkEntry(chunkInfo.ChunkId);
                    if (chunkEntry == null)
                        continue;
                    buffer = TerrainHeightStorage.GetChunkBytes(chunkEntry);
                    if (buffer == null)
                        continue;
                    chunkBuffers[chunkInfo.ChunkId] = buffer;
                }
                heightOffset = chunkInfo.BaseOffset;
            }
            else if (resInfos.TryGetValue(key, out HeightNodeResInfo info) && info.HeightOffset >= 0)
            {
                buffer = resBytes;
                heightOffset = info.HeightOffset;
            }

            if (buffer == null || heightOffset < 0)
                continue;

            FlattenNode(buffer, heightOffset, dim, node, world, worldHeightY, worldSizeY);
            TerrainMinMaxStack.Rebuild(buffer, heightOffset, dim, htree.minMaxStackDepth);
            TerrainMinMaxStack.UpdateBBoxFromSamples(node, buffer, heightOffset, dim, worldSizeY);
            TerrainMinMaxStack.WriteNodeBBoxY(resBytes, node);
        }

        List<HeightfieldTreeNode> byLevel = [];
        foreach (RasterTreeNode rnode in htree.nodes)
        {
            if (rnode is HeightfieldTreeNode node)
                byLevel.Add(node);
        }
        byLevel.Sort((a, b) => b.id.level.CompareTo(a.id.level));
        foreach (HeightfieldTreeNode node in byLevel)
        {
            if (node.children.Count > 0)
                TerrainMinMaxStack.UnionChildrenBBoxY(node);
            TerrainMinMaxStack.WriteNodeBBoxY(resBytes, node);

            var key = (node.id.indexX, node.id.indexY, node.id.level);
            if (resInfos.TryGetValue(key, out HeightNodeResInfo info) && info.Pruned && info.MinMaxOffset >= 0 && info.HeightOffset < 0)
            {
                ushort min = TerrainHeightCodec.EncodeLocalY(node.boundingBox.min.y, worldSizeY);
                ushort max = TerrainHeightCodec.EncodeLocalY(node.boundingBox.max.y, worldSizeY);
                TerrainMinMaxStack.FillConstant(resBytes, info.MinMaxOffset, htree.minMaxStackSize, min, max);
            }
        }

        App.AssetManager.ModifyRes(resEntry.ResRid, resBytes, resEntry.ResMeta);

        int chunkCount = 0;
        foreach (var kv in chunkBuffers)
        {
            if (App.AssetManager.ModifyChunk(kv.Key, kv.Value))
                chunkCount++;
        }

        App.Logger.Log(
            $"已写回超平坦地形：{chunkCount} 个 chunk，worldHeightY={worldHeightY:0.#####}，worldSizeY={worldSizeY:0.#####}");
        return true;
    }

    private static void FlattenNode(byte[] buffer, int heightOffset, int dim, HeightfieldTreeNode node, Matrix world, float worldHeightY, float worldSizeY)
    {
        float sizeX = node.boundingBox.max.x - node.boundingBox.min.x;
        float sizeZ = node.boundingBox.max.z - node.boundingBox.min.z;
        float stepX = dim > 1 ? sizeX / (dim - 1) : 0.0f;
        float stepZ = dim > 1 ? sizeZ / (dim - 1) : 0.0f;

        for (int y = 0; y < dim; y++)
        {
            float localZ = node.boundingBox.min.z + y * stepZ;
            int row = heightOffset + y * dim * 2;
            for (int x = 0; x < dim; x++)
            {
                float localX = node.boundingBox.min.x + x * stepX;
                float localY = TerrainHeightCodec.LocalYForWorldHeight(worldHeightY, localX, localZ, world);
                TerrainHeightCodec.WriteU16(buffer, row + x * 2, TerrainHeightCodec.EncodeLocalY(localY, worldSizeY));
            }
        }
    }
}
