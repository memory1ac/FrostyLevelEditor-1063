using LevelEditorPlugin.Resources;

namespace LevelEditorPlugin.BF1Core.Utils;

/// <summary>
/// 按游戏运行时的层级（1x1 最粗，然后 2x2、4x4…）重建高度 min/max 栈。
/// 每个格子两个 ushort，紧挨在高度样本后面。
/// </summary>
public static class TerrainMinMaxStack
{
    public static int HeightBytes(int dim) => dim * dim * 2;

    public static void Rebuild(byte[] buffer, int heightOffset, int dim, uint minMaxStackDepth)
    {
        if (buffer == null || dim <= 0 || minMaxStackDepth == 0)
            return;

        int dest = heightOffset + HeightBytes(dim);
        for (int level = 0; level < minMaxStackDepth; level++)
        {
            int grid = 1 << level;
            for (int cy = 0; cy < grid; cy++)
            {
                for (int cx = 0; cx < grid; cx++)
                {
                    int x0 = cx * dim / grid;
                    int x1 = (cx + 1) * dim / grid;
                    int y0 = cy * dim / grid;
                    int y1 = (cy + 1) * dim / grid;
                    if (x1 <= x0)
                        x1 = x0 + 1;
                    if (y1 <= y0)
                        y1 = y0 + 1;
                    if (x1 > dim)
                        x1 = dim;
                    if (y1 > dim)
                        y1 = dim;

                    ushort min = ushort.MaxValue;
                    ushort max = 0;
                    for (int y = y0; y < y1; y++)
                    {
                        int row = heightOffset + y * dim * 2;
                        for (int x = x0; x < x1; x++)
                        {
                            ushort sample = TerrainHeightCodec.ReadU16(buffer, row + x * 2);
                            if (sample < min)
                                min = sample;
                            if (sample > max)
                                max = sample;
                        }
                    }

                    TerrainHeightCodec.WriteU16(buffer, dest, min);
                    dest += 2;
                    TerrainHeightCodec.WriteU16(buffer, dest, max);
                    dest += 2;
                }
            }
        }
    }

    public static void FillConstant(byte[] buffer, int minMaxOffset, uint minMaxStackSize, ushort min, ushort max)
    {
        int bytes = (int)(minMaxStackSize * 2);
        for (int i = 0; i + 3 < bytes; i += 4)
        {
            TerrainHeightCodec.WriteU16(buffer, minMaxOffset + i, min);
            TerrainHeightCodec.WriteU16(buffer, minMaxOffset + i + 2, max);
        }
    }

    public static void WriteNodeBBoxY(byte[] resBytes, HeightfieldTreeNode node)
    {
        if (resBytes == null || node == null || node.offset < 0)
            return;
        int offset = (int)node.offset;
        TerrainHeightCodec.WriteFloat(resBytes, offset + 4, node.boundingBox.min.y);
        TerrainHeightCodec.WriteFloat(resBytes, offset + 16, node.boundingBox.max.y);
    }

    public static void UpdateBBoxFromSamples(HeightfieldTreeNode node, byte[] buffer, int heightOffset, int dim, float worldSizeY)
    {
        if (node == null || buffer == null || dim <= 0)
            return;

        ushort min = ushort.MaxValue;
        ushort max = 0;
        int count = dim * dim;
        for (int i = 0; i < count; i++)
        {
            ushort sample = TerrainHeightCodec.ReadU16(buffer, heightOffset + i * 2);
            if (sample < min)
                min = sample;
            if (sample > max)
                max = sample;
        }

        node.boundingBox.min.y = TerrainHeightCodec.DecodeLocalY(min, worldSizeY);
        node.boundingBox.max.y = TerrainHeightCodec.DecodeLocalY(max, worldSizeY);
    }

    public static void UnionChildrenBBoxY(HeightfieldTreeNode parent)
    {
        if (parent == null || parent.children.Count == 0)
            return;

        float minY = float.MaxValue;
        float maxY = float.MinValue;
        foreach (RasterTreeNode child in parent.children)
        {
            if (child is not HeightfieldTreeNode hchild)
                continue;
            if (hchild.boundingBox.min.y < minY)
                minY = hchild.boundingBox.min.y;
            if (hchild.boundingBox.max.y > maxY)
                maxY = hchild.boundingBox.max.y;
        }

        if (minY <= maxY)
        {
            parent.boundingBox.min.y = minY;
            parent.boundingBox.max.y = maxY;
        }
    }
}
