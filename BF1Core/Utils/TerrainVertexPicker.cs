using LevelEditorPlugin.Render;
using LevelEditorPlugin.Render.Proxies;
using SharpDX;

namespace LevelEditorPlugin.BF1Core.Utils;

/// <summary>
/// 提供从鼠标射线中拾取地形顶点的帮助方法。
/// </summary>
public static class TerrainVertexPicker
{
    /// <summary>
    /// 尝试在给定 TerrainRenderProxy 上拾取离射线最近的顶点。
    /// 仅在距离小于一定阈值时认为命中。
    /// </summary>
    public static bool TryPickVertex(Ray worldRay, TerrainRenderProxy proxy, out TerrainVertexSelection selection)
    {
        selection = default;

        TerrainChunkRenderable chunk = proxy.RenderData;
        if (chunk == null || chunk.TerrainVertices == null || chunk.TerrainVertices.Count == 0)
            return false;

        // 将射线变换到 chunk 的局部空间
        Matrix invWorld = Matrix.Invert(proxy.Transform);
        Ray localRay = new Ray(
            Vector3.TransformCoordinate(worldRay.Position, invWorld),
            Vector3.Normalize(Vector3.TransformNormal(worldRay.Direction, invWorld))
        );

        float bestDistSq = float.MaxValue;
        int bestIndex = -1;
        Vector3 bestLocalPos = default;

        for (int i = 0; i < chunk.TerrainVertices.Count; i++)
        {
            Vector3 p = chunk.TerrainVertices[i].Position; // 局部空间

            Vector3 v = p - localRay.Position;
            float t = Vector3.Dot(v, localRay.Direction);
            if (t < 0.0f)
                continue; // 在射线背后

            Vector3 closest = localRay.Position + t * localRay.Direction;
            float distSq = (closest - p).LengthSquared();

            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestIndex = i;
                bestLocalPos = p;
            }
        }

        // 阈值可根据地形分辨率调整，这里先给个相对保守的值
        const float MaxPickDistanceSq = 4.0f; // 约 2 个单位的距离

        if (bestIndex >= 0 && bestDistSq <= MaxPickDistanceSq)
        {
            Vector3 worldPos = Vector3.TransformCoordinate(bestLocalPos, proxy.Transform);
            selection = new TerrainVertexSelection
            {
                Proxy = proxy,
                Chunk = chunk,
                VertexIndex = bestIndex,
                WorldPosition = worldPos
            };
            return true;
        }

        return false;
    }

    /// <summary>
    /// 根据 TerrainChunkRenderable 的顶点索引和 dim，推导对应 Heightfield 样本索引。
    /// 逻辑与构造顶点时的循环（ny, nx, y, x）保持一致。
    /// </summary>
    public static int GetSampleIndexForVertex(int vertexIndex, int dim)
    {
        if (dim <= 0)
            return -1;

        int tileDimVert = (dim / 4) + 1;           // 每个 tile 顶点数 = (dim/4+1)^2
        int vertsPerTile = tileDimVert * tileDimVert;

        if (vertsPerTile <= 0)
            return -1;

        int tileIndex = vertexIndex / vertsPerTile;      // 0..15
        int vertexInTile = vertexIndex % vertsPerTile;

        int ny = tileIndex / 4;
        int nx = tileIndex % 4;

        int y = vertexInTile / tileDimVert;
        int x = vertexInTile % tileDimVert;

        int dim4 = dim / 4;

        int sampleY = ny * dim4 + y;
        int sampleX = nx * dim4 + x;

        if (sampleY < 0 || sampleX < 0 || sampleY >= dim || sampleX >= dim)
            return -1;

        return sampleY * dim + sampleX;
    }
}

