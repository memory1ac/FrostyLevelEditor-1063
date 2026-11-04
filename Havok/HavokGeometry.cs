using System;
using System.Collections.Generic;
using System.Numerics;

namespace LevelEditorPlugin.Havok
{
    /// <summary>渲染用的顶点：位置 + 颜色（32 字节，便于和 D3D11 输入布局对齐）。</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct HavokVertex
    {
        public Vector3 Position;
        public Vector4 Color;
        public float Padding;
    }

    public sealed class HavokGeometryOptions
    {
        public bool DrawSolid = true;
        public bool DrawWire = true;
        public bool DrawCompoundAabb = true;
        public bool DrawInstanceAabb = false;
        public bool DrawBvh = false;
        public bool DrawDomain = true;
        public bool DrawGrid = true;
        public bool DrawResParts = false;

        /// <summary>按实例/段着色，方便看清"分块"到底是怎么切的。</summary>
        public bool DrawPieceColors = false;

        /// <summary>只画选中的实例，用来逐个核对分块。</summary>
        public bool OnlySelected = false;
        public HavokShapeInstance Selected;
    }

    public sealed class HavokGeometryResult
    {
        public List<HavokVertex> Triangles = new List<HavokVertex>();
        public List<HavokVertex> Lines = new List<HavokVertex>();
        public int TriangleCount;
        public Vector3 Min;
        public Vector3 Max;

        public bool HasGeometry => Triangles.Count > 0 || Lines.Count > 0;
    }

    /// <summary>把 HavokScene 变成可渲染的三角形/线段顶点流（含镜像与缩放修正）。</summary>
    public static class HavokGeometry
    {
        public static readonly Vector4 ColorSolid = new Vector4(0.55f, 0.72f, 0.95f, 0.80f);
        public static readonly Vector4 ColorWire = new Vector4(0.80f, 0.90f, 1.00f, 0.90f);
        public static readonly Vector4 ColorSelectedSolid = new Vector4(1.00f, 0.80f, 0.25f, 0.85f);
        public static readonly Vector4 ColorSelectedWire = new Vector4(1.00f, 0.95f, 0.55f, 1.00f);
        public static readonly Vector4 ColorCompoundAabb = new Vector4(0.20f, 1.00f, 0.45f, 1.00f);
        public static readonly Vector4 ColorInstanceAabb = new Vector4(1.00f, 0.55f, 0.10f, 1.00f);
        public static readonly Vector4 ColorBvh = new Vector4(1.00f, 0.20f, 0.85f, 1.00f);
        public static readonly Vector4 ColorDomain = new Vector4(0.90f, 0.90f, 0.90f, 0.55f);
        public static readonly Vector4 ColorResAabb = new Vector4(0.30f, 0.85f, 1.00f, 0.85f);
        public static readonly Vector4 ColorGrid = new Vector4(0.32f, 0.34f, 0.38f, 0.85f);
        public static readonly Vector4 ColorAxisX = new Vector4(0.95f, 0.30f, 0.30f, 1f);
        public static readonly Vector4 ColorAxisY = new Vector4(0.35f, 0.95f, 0.35f, 1f);
        public static readonly Vector4 ColorAxisZ = new Vector4(0.35f, 0.55f, 1.00f, 1f);
        public static readonly Vector4 ColorPart = new Vector4(1.00f, 1.00f, 0.35f, 1.00f);

        public static HavokGeometryResult Build(HavokScene scene, HavokGeometryOptions options)
        {
            HavokGeometryResult result = new HavokGeometryResult();
            if (scene == null)
            {
                result.Min = new Vector3(-1);
                result.Max = new Vector3(1);
                return result;
            }

            if (options.DrawGrid)
                AddGrid(result);

            if (options.DrawResParts)
                AddResHeaderArrays(scene, result);

            foreach (HavokCompound compound in scene.RootCompounds)
                AddCompound(compound, Matrix4x4.Identity, 0, result, options);

            foreach (HavokShape shape in scene.RootShapes)
                AddShape(shape, Matrix4x4.Identity, null, result, options);

            foreach (HavokCompound compound in scene.RootCompounds)
                AddCompoundExtras(compound, result, options);

            if (!scene.TryGetBounds(out Vector3 min, out Vector3 max))
            {
                min = new Vector3(-1);
                max = new Vector3(1);
            }
            result.Min = min;
            result.Max = max;
            return result;
        }

        private static void AddCompound(HavokCompound compound, Matrix4x4 parent, int depth, HavokGeometryResult result, HavokGeometryOptions options)
        {
            if (depth > 4)
                return;

            foreach (HavokShapeInstance inst in compound.Instances)
            {
                Matrix4x4 world = Matrix4x4.Multiply(inst.LocalMatrix, parent);
                AddShape(inst.Shape, world, inst, result, options);

                if (options.DrawInstanceAabb && inst.Shape != null && inst.Shape.HasLocalBounds
                    && (!options.OnlySelected || ReferenceEquals(inst, options.Selected)))
                {
                    Vector4 color = ReferenceEquals(inst, options.Selected) ? ColorSelectedWire : ColorInstanceAabb;
                    AddBox(result.Lines, inst.Shape.LocalBoundsMin, inst.Shape.LocalBoundsMax, color, world);
                }

                if (inst.Shape?.Compound != null)
                    AddCompound(inst.Shape.Compound, world, depth + 1, result, options);
            }
        }

        private static void AddShape(HavokShape shape, Matrix4x4 world, HavokShapeInstance inst, HavokGeometryResult result, HavokGeometryOptions options)
        {
            if (shape == null)
                return;

            bool selected = inst != null && ReferenceEquals(inst, options.Selected);

            // 只看选中：200 个碰撞体同时画会糊成一片，逐个核对才看得清分块
            if (options.OnlySelected && inst != null && !selected)
                return;

            Vector4 solid = selected ? ColorSelectedSolid : ColorSolid;
            Vector4 wire = selected ? ColorSelectedWire : ColorWire;

            if (options.DrawPieceColors && !selected)
            {
                solid = PieceColor(inst != null ? inst.Index : 0, -1, 0.85f);
                wire = new Vector4(solid.X * 1.15f, solid.Y * 1.15f, solid.Z * 1.15f, 0.95f);
            }

            if (shape.IsPolytope && shape.Vertices.Count > 0)
            {
                if (options.DrawSolid)
                {
                    foreach (HavokFace face in shape.Faces)
                    {
                        if (face.NumIndices < 3)
                            continue;

                        int i0 = shape.Indices[face.FirstIndex];
                        for (int k = 1; k + 1 < face.NumIndices; k++)
                        {
                            int i1 = shape.Indices[face.FirstIndex + k];
                            int i2 = shape.Indices[face.FirstIndex + k + 1];
                            AddTriangle(result, shape.Vertices[i0], shape.Vertices[i1], shape.Vertices[i2], world, solid);
                        }
                    }
                }

                if (options.DrawWire)
                {
                    foreach (HavokFace face in shape.Faces)
                    {
                        if (face.NumIndices < 2)
                            continue;

                        for (int k = 0; k < face.NumIndices; k++)
                        {
                            int a = shape.Indices[face.FirstIndex + k];
                            int b = shape.Indices[face.FirstIndex + (k + 1) % face.NumIndices];
                            AddLine(result.Lines, shape.Vertices[a], shape.Vertices[b], world, wire);
                        }
                    }
                }
            }
            else if (shape.IsCompressedMesh && shape.Vertices.Count > 0)
            {
                bool sectioned = options.DrawPieceColors && !selected && shape.PolygonSections != null
                    && shape.PolygonSections.Count == shape.Polygons.Count;

                if (options.DrawSolid)
                {
                    for (int pi = 0; pi < shape.Polygons.Count; pi++)
                    {
                        int[] polygon = shape.Polygons[pi];
                        if (polygon.Length < 3)
                            continue;

                        Vector4 color = sectioned
                            ? PieceColor(inst != null ? inst.Index : 0, shape.PolygonSections[pi], 0.85f)
                            : solid;

                        int i0 = polygon[0];
                        for (int k = 1; k + 1 < polygon.Length; k++)
                            AddTriangle(result, shape.Vertices[i0], shape.Vertices[polygon[k]], shape.Vertices[polygon[k + 1]], world, color);
                    }
                }

                if (options.DrawWire)
                {
                    for (int pi = 0; pi < shape.Polygons.Count; pi++)
                    {
                        int[] polygon = shape.Polygons[pi];
                        Vector4 color = sectioned
                            ? PieceColor(inst != null ? inst.Index : 0, shape.PolygonSections[pi], 0.95f)
                            : wire;

                        for (int k = 0; k < polygon.Length; k++)
                        {
                            int a = polygon[k];
                            int b = polygon[(k + 1) % polygon.Length];
                            AddLine(result.Lines, shape.Vertices[a], shape.Vertices[b], world, color);
                        }
                    }
                }
            }
            else if (shape.IsSphere)
            {
                AddSphere(result, shape.SphereCenter, Math.Max(shape.Radius, 0.001f), world, wire, options.DrawSolid ? solid : wire);
            }
            else if (shape.IsCapsule)
            {
                AddCapsule(result, shape.CapsuleA, shape.CapsuleB, Math.Max(shape.Radius, 0.001f), world, wire);
            }
            else if (shape.HasLocalBounds)
            {
                // 来自 res 头部 part AABB 的包围盒用另一种颜色，提示它不是形状自身的几何
                AddBox(result.Lines, shape.LocalBoundsMin, shape.LocalBoundsMax,
                    shape.FromResHeaderBounds ? ColorResAabb : ColorInstanceAabb, world);
            }

            if (shape.Compound != null && options.DrawCompoundAabb && !options.OnlySelected)
            {
                Vector3 min = shape.Compound.HasAabb
                    ? shape.Compound.AabbMin
                    : shape.HasLocalBounds ? shape.LocalBoundsMin : Vector3.Zero;
                Vector3 max = shape.Compound.HasAabb
                    ? shape.Compound.AabbMax
                    : shape.HasLocalBounds ? shape.LocalBoundsMax : Vector3.Zero;
                AddBox(result.Lines, min, max, ColorCompoundAabb, world);
            }
        }

        private static void AddCompoundExtras(HavokCompound compound, HavokGeometryResult result, HavokGeometryOptions options)
        {
            HavokShapeData sd = compound.ShapeData;
            if (sd == null || !options.DrawBvh)
                return;

            if (options.DrawDomain)
                AddBox(result.Lines, sd.DomainMin, sd.DomainMax, ColorDomain, Matrix4x4.Identity);

            if (sd.NodesDecoded)
            {
                foreach (Vector3[] box in sd.NodeBoxes)
                    AddBox(result.Lines, box[0], box[1], ColorBvh, Matrix4x4.Identity);
            }
        }

        private static void AddResHeaderArrays(HavokScene scene, HavokGeometryResult result)
        {
            HavokRes res = scene.Res;
            if (res == null)
                return;

            for (int i = 0; i < res.PartTranslations.Count; i++)
            {
                Vector4 t = res.PartTranslations[i];
                Vector3 p = new Vector3(t.X, t.Y, t.Z);
                const float s = 0.05f;
                AddLine(result.Lines, p - new Vector3(s, 0, 0), p + new Vector3(s, 0, 0), ColorPart);
                AddLine(result.Lines, p - new Vector3(0, s, 0), p + new Vector3(0, s, 0), ColorPart);
                AddLine(result.Lines, p - new Vector3(0, 0, s), p + new Vector3(0, 0, s), ColorPart);
            }

            for (int i = 0; i < res.LocalAabbMin.Count && i < res.LocalAabbMax.Count; i++)
            {
                Vector4 a = res.LocalAabbMin[i];
                Vector4 b = res.LocalAabbMax[i];
                AddBox(result.Lines,
                    new Vector3(a.X, a.Y, a.Z),
                    new Vector3(b.X, b.Y, b.Z),
                    ColorResAabb,
                    Matrix4x4.Identity);
            }
        }

        private static void AddGrid(HavokGeometryResult result)
        {
            const int half = 10;
            const float step = 0.5f;
            for (int i = -half; i <= half; i++)
            {
                float v = i * step;
                Vector4 color = i == 0 ? new Vector4(0.55f, 0.55f, 0.6f, 1f) : ColorGrid;
                AddLine(result.Lines, new Vector3(-half * step, 0, v), new Vector3(half * step, 0, v), color);
                AddLine(result.Lines, new Vector3(v, 0, -half * step), new Vector3(v, 0, half * step), color);
            }

            AddLine(result.Lines, Vector3.Zero, new Vector3(1, 0, 0), ColorAxisX);
            AddLine(result.Lines, Vector3.Zero, new Vector3(0, 1, 0), ColorAxisY);
            AddLine(result.Lines, Vector3.Zero, new Vector3(0, 0, 1), ColorAxisZ);
        }

        #region -- 基本体 --

        /// <summary>
        /// 分块着色：按 (实例, 段) 算一个稳定的色相。用黄金角散开，相邻分块颜色差得开，
        /// 又不至于像随机色那样花。
        /// </summary>
        public static Vector4 PieceColor(int instanceIndex, int sectionIndex, float alpha)
        {
            float hue = (instanceIndex * 0.618034f + (sectionIndex + 1) * 0.1375f) % 1f;
            float h = hue * 6f;
            float s = 0.55f;
            float v = 0.95f;

            int i = (int)h % 6;
            float f = h - (float)Math.Floor(h);
            float p = v * (1f - s);
            float q = v * (1f - s * f);
            float t = v * (1f - s * (1f - f));

            float r, g, b;
            switch (i)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }

            return new Vector4(r, g, b, alpha);
        }

        public static void AddLine(List<HavokVertex> lines, Vector3 a, Vector3 b, Vector4 color)
        {
            lines.Add(new HavokVertex { Position = a, Color = color });
            lines.Add(new HavokVertex { Position = b, Color = color });
        }

        public static void AddLine(List<HavokVertex> lines, Vector4 a, Vector4 b, Matrix4x4 world, Vector4 color)
            => AddLine(lines, Vector3.Transform(new Vector3(a.X, a.Y, a.Z), world), Vector3.Transform(new Vector3(b.X, b.Y, b.Z), world), color);

        public static void AddLine(List<HavokVertex> lines, Vector3 a, Vector3 b, Matrix4x4 world, Vector4 color)
            => AddLine(lines, Vector3.Transform(a, world), Vector3.Transform(b, world), color);

        public static void AddTriangle(HavokGeometryResult result, Vector4 a, Vector4 b, Vector4 c, Matrix4x4 world, Vector4 color)
        {
            Vector3 pa = Vector3.Transform(new Vector3(a.X, a.Y, a.Z), world);
            Vector3 pb = Vector3.Transform(new Vector3(b.X, b.Y, b.Z), world);
            Vector3 pc = Vector3.Transform(new Vector3(c.X, c.Y, c.Z), world);

            Vector3 n = Vector3.Cross(pb - pa, pc - pa);
            if (n.LengthSquared() < 1e-12f)
                return;

            n = Vector3.Normalize(n);

            // 双面光照：不管绕序，两个方向都亮
            float light = Math.Abs(Vector3.Dot(n, Vector3.Normalize(new Vector3(0.45f, 0.85f, 0.3f)))) * 0.55f + 0.45f;
            Vector4 shaded = new Vector4(color.X * light, color.Y * light, color.Z * light, color.W);

            result.Triangles.Add(new HavokVertex { Position = pa, Color = shaded });
            result.Triangles.Add(new HavokVertex { Position = pb, Color = shaded });
            result.Triangles.Add(new HavokVertex { Position = pc, Color = shaded });
            result.TriangleCount++;
        }

        public static void AddBox(List<HavokVertex> lines, Vector3 min, Vector3 max, Vector4 color, Matrix4x4 world)
        {
            Vector3[] c = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                c[i] = new Vector3(
                    (i & 1) == 0 ? min.X : max.X,
                    (i & 2) == 0 ? min.Y : max.Y,
                    (i & 4) == 0 ? min.Z : max.Z);
                c[i] = Vector3.Transform(c[i], world);
            }

            int[,] edges =
            {
                { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 },
                { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 },
                { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 },
            };

            for (int e = 0; e < edges.GetLength(0); e++)
                AddLine(lines, c[edges[e, 0]], c[edges[e, 1]], color);
        }

        public static void AddSphere(HavokGeometryResult result, Vector3 center, float radius, Matrix4x4 world, Vector4 color, Vector4? solidColor)
        {
            const int segments = 16;
            const int rings = 8;

            if (solidColor.HasValue)
            {
                for (int r = 0; r < rings; r++)
                {
                    float t0 = Pi * r / rings;
                    float t1 = Pi * (r + 1) / rings;
                    for (int s = 0; s < segments; s++)
                    {
                        float p0 = Pi * 2f * s / segments;
                        float p1 = Pi * 2f * (s + 1) / segments;

                        Vector3 a = SpherePoint(center, radius, t0, p0);
                        Vector3 b = SpherePoint(center, radius, t0, p1);
                        Vector3 c = SpherePoint(center, radius, t1, p1);
                        Vector3 d = SpherePoint(center, radius, t1, p0);

                        AddTri(result, a, b, c, world, solidColor.Value);
                        AddTri(result, a, c, d, world, solidColor.Value);
                    }
                }
            }

            // 三个正交大圆
            for (int s = 0; s < segments; s++)
            {
                float a0 = Pi * 2f * s / segments;
                float a1 = Pi * 2f * (s + 1) / segments;

                Vector3 x0 = SpherePoint(center, radius, Pi / 2f, a0);
                Vector3 x1 = SpherePoint(center, radius, Pi / 2f, a1);
                AddLine(result.Lines, x0, x1, world, color);

                Vector3 y0 = center + new Vector3(Cos(a0) * radius, 0, Sin(a0) * radius);
                Vector3 y1 = center + new Vector3(Cos(a1) * radius, 0, Sin(a1) * radius);
                AddLine(result.Lines, y0, y1, world, color);

                Vector3 z0 = center + new Vector3(0, Cos(a0) * radius, Sin(a0) * radius);
                Vector3 z1 = center + new Vector3(0, Cos(a1) * radius, Sin(a1) * radius);
                AddLine(result.Lines, z0, z1, world, color);
            }
        }

        private const float Pi = (float)Math.PI;
        private static float Sin(float v) => (float)Math.Sin(v);
        private static float Cos(float v) => (float)Math.Cos(v);

        private static Vector3 SpherePoint(Vector3 center, float radius, float theta, float phi)
            => center + new Vector3(
                Sin(theta) * Cos(phi) * radius,
                Cos(theta) * radius,
                Sin(theta) * Sin(phi) * radius);

        private static void AddTri(HavokGeometryResult result, Vector3 a, Vector3 b, Vector3 c, Matrix4x4 world, Vector4 color)
        {
            Vector3 pa = Vector3.Transform(a, world);
            Vector3 pb = Vector3.Transform(b, world);
            Vector3 pc = Vector3.Transform(c, world);

            Vector3 n = Vector3.Cross(pb - pa, pc - pa);
            if (n.LengthSquared() < 1e-12f)
                return;

            n = Vector3.Normalize(n);
            float light = Math.Abs(Vector3.Dot(n, Vector3.Normalize(new Vector3(0.45f, 0.85f, 0.3f)))) * 0.55f + 0.45f;
            Vector4 shaded = new Vector4(color.X * light, color.Y * light, color.Z * light, color.W);

            result.Triangles.Add(new HavokVertex { Position = pa, Color = shaded });
            result.Triangles.Add(new HavokVertex { Position = pb, Color = shaded });
            result.Triangles.Add(new HavokVertex { Position = pc, Color = shaded });
            result.TriangleCount++;
        }

        public static void AddCapsule(HavokGeometryResult result, Vector3 a, Vector3 b, float radius, Matrix4x4 world, Vector4 color)
        {
            Vector3 axis = b - a;
            if (axis.LengthSquared() < 1e-10f)
            {
                AddSphere(result, a, radius, world, color, null);
                return;
            }

            axis = Vector3.Normalize(axis);
            Vector3 up = Math.Abs(axis.Y) > 0.99f ? new Vector3(1, 0, 0) : new Vector3(0, 1, 0);
            Vector3 u = Vector3.Normalize(Vector3.Cross(up, axis));
            Vector3 v = Vector3.Cross(axis, u);

            const int segments = 16;
            for (int s = 0; s < segments; s++)
            {
                float t0 = Pi * 2f * s / segments;
                float t1 = Pi * 2f * (s + 1) / segments;

                Vector3 d0 = u * Cos(t0) + v * Sin(t0);
                Vector3 d1 = u * Cos(t1) + v * Sin(t1);

                AddLine(result.Lines, a + d0 * radius, a + d1 * radius, world, color);
                AddLine(result.Lines, b + d0 * radius, b + d1 * radius, world, color);
                AddLine(result.Lines, a + d0 * radius, b + d0 * radius, world, color);

                // 两端的半球用两条弧线示意
                AddLine(result.Lines, a + d0 * radius, a + (d0 + axis) * radius * 0.7071f, world, color);
                AddLine(result.Lines, b + d0 * radius, b + (d0 - axis) * radius * 0.7071f, world, color);
            }
        }

        #endregion

        /// <summary>凸包三角化后的三角形数量（用于状态栏统计）。</summary>
        public static int CountTriangles(HavokShape shape)
        {
            if (shape == null)
                return 0;

            if (shape.IsPolytope)
            {
                int n = 0;
                foreach (HavokFace face in shape.Faces)
                {
                    if (face.NumIndices >= 3)
                        n += face.NumIndices - 2;
                }
                return n;
            }

            if (shape.IsCompressedMesh)
            {
                int n = 0;
                foreach (int[] polygon in shape.Polygons)
                    n += Math.Max(0, polygon.Length - 2);
                return n;
            }

            if (shape.IsSphere)
                return 16 * 8 * 2;

            return 0;
        }
    }
}
