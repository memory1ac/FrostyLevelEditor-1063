using LevelEditorPlugin.Render;
using LevelEditorPlugin.Render.Proxies;
using LevelEditorPlugin.Resources;
using SharpDX;
using System.Collections.Generic;

namespace LevelEditorPlugin.BF1Core.Utils;

public enum TerrainBrushMode
{
    Raise,
    Lower
}

/// <summary>
/// 地形编辑模式的全局状态（笔刷参数、待写回的高度修改）。
/// </summary>
public static class TerrainEditState
{
    public static bool IsTerrainEditModeEnabled { get; set; }

    public static TerrainBrushMode BrushMode { get; set; } = TerrainBrushMode.Raise;
    public static float BrushRadius { get; set; } = 16.0f;
    public static float BrushStrength { get; set; } = 8.0f;
    public static float BrushFeather { get; set; } = 0.5f;

    public static TerrainVertexSelection? CurrentVertex { get; set; }

    public static List<TerrainVertexEdit> PendingEdits { get; } = new List<TerrainVertexEdit>();

    public static void ClearSelection()
    {
        CurrentVertex = null;
    }

    public static void ClearEdits()
    {
        PendingEdits.Clear();
    }

    public static void AddOrUpdateEdit(QuadTreeNodeId nodeId, int sampleIndex, float targetLocalY)
    {
        for (int i = 0; i < PendingEdits.Count; i++)
        {
            var e = PendingEdits[i];
            if (e.NodeIndexX == nodeId.indexX && e.NodeIndexY == nodeId.indexY && e.NodeLevel == nodeId.level && e.SampleIndex == sampleIndex)
            {
                e.TargetLocalY = targetLocalY;
                PendingEdits[i] = e;
                return;
            }
        }

        PendingEdits.Add(new TerrainVertexEdit
        {
            NodeIndexX = nodeId.indexX,
            NodeIndexY = nodeId.indexY,
            NodeLevel = nodeId.level,
            SampleIndex = sampleIndex,
            TargetLocalY = targetLocalY
        });
    }
}

public struct TerrainVertexSelection
{
    public TerrainRenderProxy Proxy;
    public TerrainChunkRenderable Chunk;
    public int VertexIndex;
    public Vector3 WorldPosition;
}

public struct TerrainVertexEdit
{
    public ushort NodeIndexX;
    public ushort NodeIndexY;
    public byte NodeLevel;
    public int SampleIndex;

    /// <summary>
    /// 目标局部高度 Y，写回时按 worldSizeY/65535 编码。
    /// </summary>
    public float TargetLocalY;
}
