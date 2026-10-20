using LevelEditorPlugin.Entities;
using LevelEditorPlugin.Render;
using LevelEditorPlugin.Resources;
using LevelEditorPlugin.Screens;
using SharpDX;
using System;
using System.Collections.Generic;

namespace LevelEditorPlugin.BF1Core.Utils;

public static class TerrainBrush
{
    private const float HeightEps = 1e-4f;
    private const float UpFaceMinY = 0.25f;

    public static bool Apply(LevelEditorScreen screen, Ray worldRay, float deltaTime)
    {
        TerrainEntity terrain = FindTerrain(screen);
        if (terrain?.Terrain?.TerrainData == null || deltaTime <= 0.0f)
            return false;

        if (!TryHit(terrain, worldRay, out Vector3 localHit, out _))
            return false;

        float radius = TerrainEditState.BrushRadius;
        if (radius <= 0.0f)
            return false;

        float sign = TerrainEditState.BrushMode == TerrainBrushMode.Lower ? -1.0f : 1.0f;
        float delta = sign * TerrainEditState.BrushStrength * deltaTime;
        float radiusSq = radius * radius;
        float feather = TerrainEditState.BrushFeather;

        List<TerrainChunkRenderable> chunks = [.. terrain.Terrain.TerrainData.TerrainChunks];
        var grids = new Dictionary<TerrainChunkRenderable, float[]>();
        var dirty = new HashSet<TerrainChunkRenderable>();

        foreach (TerrainChunkRenderable chunk in chunks)
        {
            if (chunk.TerrainVertices == null || chunk.TerrainVertices.Count == 0)
                continue;
            if (!OverlapsBrush(chunk, localHit.X, localHit.Z, radius))
                continue;

            float[] heights = ReadSampleHeights(chunk);
            if (heights == null)
                continue;

            bool changed = ApplyToSamples(chunk, heights, localHit.X, localHit.Z, radiusSq, delta, feather);
            grids[chunk] = heights;
            if (changed)
                dirty.Add(chunk);
        }

        if (dirty.Count == 0)
            return false;

        foreach (TerrainChunkRenderable chunk in chunks)
        {
            if (grids.ContainsKey(chunk) || chunk.TerrainVertices == null || chunk.TerrainVertices.Count == 0)
                continue;
            if (!IsNeighborOfAny(chunk, dirty))
                continue;
            float[] heights = ReadSampleHeights(chunk);
            if (heights != null)
                grids[chunk] = heights;
        }

        StitchGrids(grids);

        bool any = false;
        foreach (var kv in grids)
        {
            TerrainChunkRenderable chunk = kv.Key;
            float[] heights = kv.Value;
            if (!WriteSampleHeights(chunk, heights))
                continue;

            chunk.MarkVerticesDirty();
            any = true;
            dirty.Add(chunk);
        }

        return any;
    }

    public static bool TryHit(TerrainEntity terrain, Ray worldRay, out Vector3 localHit, out Vector3 worldHit)
    {
        localHit = default;
        worldHit = default;
        Matrix world = terrain.GetTransform();
        Matrix inv = Matrix.Invert(world);
        Ray localRay = new Ray(
            Vector3.TransformCoordinate(worldRay.Position, inv),
            Vector3.Normalize(Vector3.TransformNormal(worldRay.Direction, inv)));

        float bestT = float.MaxValue;
        bool hit = false;
        foreach (TerrainChunkRenderable chunk in terrain.Terrain.TerrainData.TerrainChunks)
        {
            if (TryHitChunk(chunk, localRay, out float t, out Vector3 p) && t < bestT)
            {
                bestT = t;
                localHit = p;
                hit = true;
            }
        }

        if (hit)
            worldHit = Vector3.TransformCoordinate(localHit, world);
        return hit;
    }

    private static bool TryHitChunk(TerrainChunkRenderable chunk, Ray localRay, out float t, out Vector3 point)
    {
        t = 0;
        point = default;
        BoundingBox bb = chunk.Bounds.GetBoundingBox();
        ExpandCoverage(chunk, ref bb);
        if (!localRay.Intersects(ref bb, out float _))
            return false;

        var verts = chunk.TerrainVertices;
        var indices = chunk.TerrainIndices;
        if (verts == null || indices == null || indices.Count < 3)
            return false;

        float best = float.MaxValue;
        bool hit = false;
        for (int i = 0; i + 2 < indices.Count; i += 3)
        {
            Vector3 a = verts[(int)indices[i]].Position;
            Vector3 b = verts[(int)indices[i + 1]].Position;
            Vector3 c = verts[(int)indices[i + 2]].Position;
            Vector3 n = Vector3.Cross(b - a, c - a);
            float nLen = n.Length();
            if (nLen < 1e-8f || n.Y < UpFaceMinY * nLen)
                continue;

            if (localRay.Intersects(ref a, ref b, ref c, out float dist) && dist >= 0.0f && dist < best)
            {
                best = dist;
                hit = true;
            }
        }

        if (!hit)
            return false;

        t = best;
        point = localRay.Position + localRay.Direction * best;
        return true;
    }

    public static TerrainEntity FindTerrain(LevelEditorScreen screen)
    {
        if (screen?.EditorProvider?.RootLayer == null)
            return null;

        List<Entity> entities = [];
        screen.EditorProvider.RootLayer.CollectEntities(entities);
        return entities.Find(e => e is TerrainEntity) as TerrainEntity;
    }

    private static bool ApplyToSamples(TerrainChunkRenderable chunk, float[] heights, float hitX, float hitZ, float radiusSq, float delta, float feather)
    {
        int dim = chunk.Dim;
        float radius = (float)Math.Sqrt(radiusSq);
        bool changed = false;
        for (int sy = 0; sy < dim; sy++)
        {
            for (int sx = 0; sx < dim; sx++)
            {
                SampleWorldXZ(chunk, sx, sy, out float wx, out float wz);
                float dx = wx - hitX;
                float dz = wz - hitZ;
                float distSq = dx * dx + dz * dz;
                if (distSq > radiusSq)
                    continue;

                float weight = TerrainHeightCodec.BrushWeight((float)Math.Sqrt(distSq), radius, feather);
                if (weight <= 0.0f)
                    continue;

                int index = sy * dim + sx;
                float newY = heights[index] + delta * weight;
                if (newY < 0.0f)
                    newY = 0.0f;
                else if (newY > chunk.WorldSizeY)
                    newY = chunk.WorldSizeY;

                if (Math.Abs(newY - heights[index]) <= HeightEps)
                    continue;

                heights[index] = newY;
                changed = true;
            }
        }

        return changed;
    }

    private static void StitchGrids(Dictionary<TerrainChunkRenderable, float[]> grids)
    {
        var list = new List<TerrainChunkRenderable>(grids.Keys);
        for (int i = 0; i < list.Count; i++)
        {
            for (int j = i + 1; j < list.Count; j++)
            {
                TerrainChunkRenderable a = list[i];
                TerrainChunkRenderable b = list[j];
                if (!grids.TryGetValue(a, out float[] ha) || !grids.TryGetValue(b, out float[] hb))
                    continue;
                StitchPair(a, ha, b, hb);
            }
        }
    }

    private static void StitchPair(TerrainChunkRenderable a, float[] ha, TerrainChunkRenderable b, float[] hb)
    {
        if (a.Dim != b.Dim || a.Dim <= 1)
            return;

        if (a.NodeId.level == b.NodeId.level)
        {
            if (IsEastOf(a.NodeId, b.NodeId))
                StitchEastWest(a, ha, b, hb);
            else if (IsEastOf(b.NodeId, a.NodeId))
                StitchEastWest(b, hb, a, ha);
            else if (IsNorthOf(a.NodeId, b.NodeId))
                StitchSouthNorth(a, ha, b, hb);
            else if (IsNorthOf(b.NodeId, a.NodeId))
                StitchSouthNorth(b, hb, a, ha);
            return;
        }

        if (a.NodeId.level > b.NodeId.level)
            StitchTJunction(a, ha, b, hb);
        else
            StitchTJunction(b, hb, a, ha);
    }

    private static void StitchEastWest(TerrainChunkRenderable west, float[] westH, TerrainChunkRenderable east, float[] eastH)
    {
        int dim = west.Dim;
        int shift = OverlapShift(west);
        for (int y = 0; y < dim; y++)
        {
            for (int x = 0; x < dim; x++)
            {
                int ex = x - shift;
                if (ex < 0 || ex >= dim)
                    continue;
                Average(westH, y * dim + x, eastH, y * dim + ex);
            }
        }
    }

    private static void StitchSouthNorth(TerrainChunkRenderable south, float[] southH, TerrainChunkRenderable north, float[] northH)
    {
        int dim = south.Dim;
        int shift = OverlapShift(south);
        for (int y = 0; y < dim; y++)
        {
            int ny = y - shift;
            if (ny < 0 || ny >= dim)
                continue;
            for (int x = 0; x < dim; x++)
                Average(southH, y * dim + x, northH, ny * dim + x);
        }
    }

    private static void StitchTJunction(TerrainChunkRenderable fine, float[] fineH, TerrainChunkRenderable coarse, float[] coarseH)
    {
        int d = fine.NodeId.level - coarse.NodeId.level;
        if (d <= 0)
            return;

        int ax = fine.NodeId.indexX >> d;
        int ay = fine.NodeId.indexY >> d;
        bool east = ax + 1 == coarse.NodeId.indexX && ay == coarse.NodeId.indexY;
        bool west = ax - 1 == coarse.NodeId.indexX && ay == coarse.NodeId.indexY;
        bool north = ay + 1 == coarse.NodeId.indexY && ax == coarse.NodeId.indexX;
        bool south = ay - 1 == coarse.NodeId.indexY && ax == coarse.NodeId.indexX;
        if (!east && !west && !north && !south)
            return;

        int dim = fine.Dim;
        int border = EffectiveBorder(fine);
        int coarseDim = coarse.Dim;

        for (int y = 0; y < coarseDim; y++)
        {
            for (int x = 0; x < coarseDim; x++)
            {
                SampleWorldXZ(coarse, x, y, out float wx, out float wz);
                if (!WorldToSample(fine, wx, wz, out float fsx, out float fsy))
                    continue;
                if (!IsOnSharedSide(fsx, fsy, dim, border, east, west, north, south))
                    continue;

                int ix = Clampi((int)Math.Round(fsx), 0, dim - 1);
                int iy = Clampi((int)Math.Round(fsy), 0, dim - 1);
                Average(coarseH, y * coarseDim + x, fineH, iy * dim + ix);
            }
        }

        for (int y = 0; y < dim; y++)
        {
            for (int x = 0; x < dim; x++)
            {
                if (!IsOnSharedSide(x, y, dim, border, east, west, north, south))
                    continue;
                SampleWorldXZ(fine, x, y, out float wx, out float wz);
                if (!WorldToSample(coarse, wx, wz, out float csx, out float csy))
                    continue;
                fineH[y * dim + x] = SampleBilinear(coarseH, coarseDim, csx, csy);
            }
        }
    }

    private static bool IsOnSharedSide(float sx, float sy, int dim, int border, bool east, bool west, bool north, bool south)
    {
        int min = Math.Max(0, dim - 1 - 2 * border);
        int max = 2 * border;
        if (east)
            return sx >= min - 0.51f;
        if (west)
            return sx <= max + 0.51f;
        if (north)
            return sy >= min - 0.51f;
        if (south)
            return sy <= max + 0.51f;
        return false;
    }

    private static bool IsOnSharedSide(int sx, int sy, int dim, int border, bool east, bool west, bool north, bool south)
    {
        return IsOnSharedSide((float)sx, (float)sy, dim, border, east, west, north, south);
    }

    private static float[] ReadSampleHeights(TerrainChunkRenderable chunk)
    {
        int dim = chunk.Dim;
        if (dim <= 0 || chunk.TerrainVertices == null)
            return null;

        float[] sum = new float[dim * dim];
        int[] counts = new int[dim * dim];
        var verts = chunk.TerrainVertices;
        for (int i = 0; i < verts.Count; i++)
        {
            int index = TerrainVertexPicker.GetSampleIndexForVertex(i, dim);
            if (index < 0 || index >= sum.Length)
                continue;
            sum[index] += verts[i].Position.Y;
            counts[index]++;
        }

        for (int i = 0; i < sum.Length; i++)
        {
            if (counts[i] > 0)
                sum[i] /= counts[i];
        }

        return sum;
    }

    private static bool WriteSampleHeights(TerrainChunkRenderable chunk, float[] heights)
    {
        int dim = chunk.Dim;
        var verts = chunk.TerrainVertices;
        if (verts == null || heights == null || heights.Length != dim * dim)
            return false;

        bool dirty = false;
        for (int i = 0; i < verts.Count; i++)
        {
            int index = TerrainVertexPicker.GetSampleIndexForVertex(i, dim);
            if (index < 0 || index >= heights.Length)
                continue;

            TerrainVertex v = verts[i];
            float newY = heights[index];
            if (newY < 0.0f)
                newY = 0.0f;
            else if (newY > chunk.WorldSizeY)
                newY = chunk.WorldSizeY;

            if (Math.Abs(newY - v.Position.Y) <= HeightEps)
                continue;

            v.Position.Y = newY;
            verts[i] = v;
            dirty = true;
            TerrainEditState.AddOrUpdateEdit(chunk.NodeId, index, newY);
        }

        return dirty;
    }

    private static void SampleWorldXZ(TerrainChunkRenderable chunk, int sx, int sy, out float x, out float z)
    {
        GetSpacing(chunk, out float spacing, out int border);
        x = chunk.CoverageMinX + (sx - border) * spacing;
        z = chunk.CoverageMinZ + (sy - border) * spacing;
    }

    private static bool WorldToSample(TerrainChunkRenderable chunk, float wx, float wz, out float sx, out float sy)
    {
        GetSpacing(chunk, out float spacing, out int border);
        if (spacing <= 1e-8f)
        {
            sx = sy = 0;
            return false;
        }

        sx = (wx - chunk.CoverageMinX) / spacing + border;
        sy = (wz - chunk.CoverageMinZ) / spacing + border;
        int dim = chunk.Dim;
        return sx >= -0.51f && sy >= -0.51f && sx <= dim - 1 + 0.51f && sy <= dim - 1 + 0.51f;
    }

    private static float SampleBilinear(float[] heights, int dim, float sx, float sy)
    {
        if (sx < 0)
            sx = 0;
        else if (sx > dim - 1)
            sx = dim - 1;
        if (sy < 0)
            sy = 0;
        else if (sy > dim - 1)
            sy = dim - 1;

        int x0 = (int)Math.Floor(sx);
        int y0 = (int)Math.Floor(sy);
        int x1 = Math.Min(x0 + 1, dim - 1);
        int y1 = Math.Min(y0 + 1, dim - 1);
        float tx = sx - x0;
        float ty = sy - y0;
        float h00 = heights[y0 * dim + x0];
        float h10 = heights[y0 * dim + x1];
        float h01 = heights[y1 * dim + x0];
        float h11 = heights[y1 * dim + x1];
        float h0 = h00 + (h10 - h00) * tx;
        float h1 = h01 + (h11 - h01) * tx;
        return h0 + (h1 - h0) * ty;
    }

    private static bool OverlapsBrush(TerrainChunkRenderable chunk, float hitX, float hitZ, float radius)
    {
        GetSpacing(chunk, out float spacing, out int border);
        float extra = border * spacing + radius;
        float minX = chunk.CoverageMinX - extra;
        float maxX = chunk.CoverageMaxX + extra;
        float minZ = chunk.CoverageMinZ - extra;
        float maxZ = chunk.CoverageMaxZ + extra;
        float cx = hitX < minX ? minX : (hitX > maxX ? maxX : hitX);
        float cz = hitZ < minZ ? minZ : (hitZ > maxZ ? maxZ : hitZ);
        float dx = cx - hitX;
        float dz = cz - hitZ;
        return dx * dx + dz * dz <= radius * radius;
    }

    private static void ExpandCoverage(TerrainChunkRenderable chunk, ref BoundingBox bb)
    {
        GetSpacing(chunk, out float spacing, out int border);
        float extra = border * spacing;
        bb.Minimum.X = chunk.CoverageMinX - extra;
        bb.Maximum.X = chunk.CoverageMaxX + extra;
        bb.Minimum.Z = chunk.CoverageMinZ - extra;
        bb.Maximum.Z = chunk.CoverageMaxZ + extra;
    }

    private static void GetSpacing(TerrainChunkRenderable chunk, out float spacing, out int border)
    {
        border = EffectiveBorder(chunk);
        int innerSteps = chunk.Dim - 1 - 2 * border;
        if (innerSteps < 1)
            innerSteps = Math.Max(chunk.Dim - 1, 1);
        float size = chunk.CoverageMaxX - chunk.CoverageMinX;
        spacing = size / innerSteps;
    }

    private static int EffectiveBorder(TerrainChunkRenderable chunk)
    {
        int border = chunk.SampleBorder;
        if (border < 0)
            border = 0;
        if (2 * border >= chunk.Dim - 1)
            return 0;
        return border;
    }

    private static int OverlapShift(TerrainChunkRenderable chunk)
    {
        int shift = chunk.Dim - 1 - 2 * EffectiveBorder(chunk);
        return shift < 1 ? chunk.Dim - 1 : shift;
    }

    private static bool IsNeighborOfAny(TerrainChunkRenderable chunk, HashSet<TerrainChunkRenderable> dirty)
    {
        foreach (TerrainChunkRenderable other in dirty)
        {
            if (SharesEdge(chunk, other))
                return true;
        }
        return false;
    }

    private static bool SharesEdge(TerrainChunkRenderable a, TerrainChunkRenderable b)
    {
        if (a.NodeId.level == b.NodeId.level)
        {
            return IsEastOf(a.NodeId, b.NodeId) || IsEastOf(b.NodeId, a.NodeId) ||
                   IsNorthOf(a.NodeId, b.NodeId) || IsNorthOf(b.NodeId, a.NodeId);
        }

        TerrainChunkRenderable fine = a.NodeId.level > b.NodeId.level ? a : b;
        TerrainChunkRenderable coarse = fine == a ? b : a;
        int d = fine.NodeId.level - coarse.NodeId.level;
        int ax = fine.NodeId.indexX >> d;
        int ay = fine.NodeId.indexY >> d;
        return (Math.Abs(ax - coarse.NodeId.indexX) == 1 && ay == coarse.NodeId.indexY) ||
               (Math.Abs(ay - coarse.NodeId.indexY) == 1 && ax == coarse.NodeId.indexX);
    }

    private static bool IsEastOf(QuadTreeNodeId west, QuadTreeNodeId east)
    {
        return west.level == east.level && west.indexY == east.indexY && (int)west.indexX + 1 == east.indexX;
    }

    private static bool IsNorthOf(QuadTreeNodeId south, QuadTreeNodeId north)
    {
        return south.level == north.level && south.indexX == north.indexX && (int)south.indexY + 1 == north.indexY;
    }

    private static void Average(float[] a, int ia, float[] b, int ib)
    {
        float avg = (a[ia] + b[ib]) * 0.5f;
        a[ia] = avg;
        b[ib] = avg;
    }

    private static int Clampi(int v, int min, int max)
    {
        if (v < min)
            return min;
        if (v > max)
            return max;
        return v;
    }
}
