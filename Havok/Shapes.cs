using System;
using System.Collections.Generic;
using System.Numerics;

namespace LevelEditorPlugin.Havok
{
    public struct HavokFace
    {
        public byte MinHalfAngle;
        public byte NumIndices;
        public ushort FirstIndex;
    }

    /// <summary>一个 hknp*Shape 对象解码后的内容（凸包 / 球 / 胶囊 / 复合形状）。</summary>
    public sealed class HavokShape
    {
        public HkxObject Object;
        public string ClassName = "";
        public int Flags;
        public float ConvexRadius;

        public List<Vector4> Vertices = new List<Vector4>();
        public List<Vector4> Planes = new List<Vector4>();
        public List<HavokFace> Faces = new List<HavokFace>();
        public List<byte> Indices = new List<byte>();

        public bool IsPolytope;
        public bool IsSphere;
        public bool IsCapsule;

        /// <summary>hknpCompressedMeshShape：三角/四边形网格（索引在 Polygons，位置在 Vertices）。</summary>
        public bool IsCompressedMesh;
        public List<int[]> Polygons = new List<int[]>();

        /// <summary>Polygons 里每个面所属的段下标（分块着色用；不是所有资源都有）。</summary>
        public List<int> PolygonSections;
        public int MeshPrimitiveCount;
        public int MeshSectionCount;
        public int MeshSharedVertexCount;

        public Vector3 SphereCenter;
        public Vector3 CapsuleA;
        public Vector3 CapsuleB;
        public float Radius;

        public HavokCompound Compound;
        public string Unsupported;

        /// <summary>包围盒来自 res 头部局部 AABB（形状本身没有几何）。</summary>
        public bool FromResHeaderBounds;

        public bool HasLocalBounds { get; private set; }
        public Vector3 LocalBoundsMin { get; private set; }
        public Vector3 LocalBoundsMax { get; private set; }

        public bool Mirrored => (Flags & 0x1000) != 0;

        public string Summary {
            get {
                if (IsCompressedMesh)
                {
                    int triangles = 0;
                    foreach (int[] polygon in Polygons)
                        triangles += Math.Max(0, polygon.Length - 2);
                    return $"压缩网格 {Vertices.Count} 顶点 / {Polygons.Count} 面（{triangles} 三角）"
                        + (MeshSectionCount > 1 ? $" / {MeshSectionCount} 段" : "");
                }
                if (Unsupported != null)
                    return HasLocalBounds ? $"{Unsupported}，只显示 part AABB" : Unsupported;
                if (Compound != null)
                    return $"复合形状（{Compound.Instances.Count} 个实例）";
                if (IsPolytope)
                    return $"凸包 {Vertices.Count} 顶点 / {Faces.Count} 面 / {Indices.Count} 索引";
                if (IsSphere)
                    return $"球 r={Radius:F3}";
                if (IsCapsule)
                    return $"胶囊 r={Radius:F3}";
                return ClassName;
            }
        }

        public void SetLocalBounds(Vector3 min, Vector3 max)
        {
            LocalBoundsMin = min;
            LocalBoundsMax = max;
            HasLocalBounds = true;
        }

        public void GrowLocalBounds(Vector3 p)
        {
            if (!HasLocalBounds)
            {
                SetLocalBounds(p, p);
                return;
            }
            LocalBoundsMin = Vector3.Min(LocalBoundsMin, p);
            LocalBoundsMax = Vector3.Max(LocalBoundsMax, p);
        }

        public void ExpandLocalBounds(float amount)
        {
            if (!HasLocalBounds)
                return;
            Vector3 v = new Vector3(amount);
            LocalBoundsMin -= v;
            LocalBoundsMax += v;
        }
    }

    /// <summary>复合形状的一个实例（= hknpShapeInstance）。</summary>
    public sealed class HavokShapeInstance
    {
        public int Index;
        public HavokCompound Owner;
        public HkxPackFile PackFile;

        /// <summary>transform 字段在资源文件里的绝对偏移（写回用）。</summary>
        public int FileOffset;
        public int DataOffset;

        public Matrix4x4 Transform = Matrix4x4.Identity;
        public Vector4 Scale = Vector4.One;
        public ushort ShapeTag;
        public ushort DestructionTag;

        public HavokShape Shape;
        public string Error;

        public bool Mirrored => Shape != null && Shape.Mirrored;

        /// <summary>镜像修正 * 缩放 * transform（与 LevelEditor 的 GetTransform 顺序一致）。</summary>
        public Matrix4x4 LocalMatrix {
            get {
                Matrix4x4 m = Matrix4x4.CreateScale(Scale.X, Scale.Y, Scale.Z);
                m = Matrix4x4.Multiply(m, Transform);
                if (Mirrored)
                    m = Matrix4x4.Multiply(Matrix4x4.CreateScale(-1, 1, 1), m);
                return m;
            }
        }

        public Matrix4x4 WorldMatrix = Matrix4x4.Identity;

        public string Label => $"实例 {Index} · {Shape?.ClassName ?? "?"} (tag {ShapeTag})";
    }

    /// <summary>hknpCompoundShape / hknpStaticCompoundShape 解码结果。</summary>
    public sealed class HavokCompound
    {
        public HkxObject Object;
        public HavokShape Shape;
        public HkxPackFile PackFile;
        public List<HavokShapeInstance> Instances = new List<HavokShapeInstance>();
        public HavokShapeData ShapeData;
        public string Error;
        public bool HasAabb;
        public Vector3 AabbMin;
        public Vector3 AabbMax;
    }

    /// <summary>hknpStaticCompoundShapeData 里的 hkcdStaticTree::Tree（BVH）。</summary>
    public sealed class HavokShapeData
    {
        public HkxObject Object;
        public Vector3 DomainMin;
        public Vector3 DomainMax;
        public int NodeCount;
        public int NodesFileOffset;
        public bool NodesDecoded;
        public string NodeStatus = "未解析";
        public List<Vector3[]> NodeBoxes = new List<Vector3[]>();
    }

    /// <summary>
    /// 把一个 HavokRes 里的形状树解码成可渲染的场景。
    /// 所有偏移都来自 HavokLayout（IDA 导出的 hkClass 布局），只有在布局缺失时才回退到已验证常量。
    /// </summary>
    public static class HavokSceneBuilder
    {
        private const int MaxDepth = 4;

        public static HavokScene Build(HavokRes res)
        {
            HavokScene scene = new HavokScene { Res = res };
            HkxPackFile pf = res.Primary;
            if (pf == null)
            {
                scene.Warnings.Add("没有可用的 packfile");
                return scene;
            }

            HavokLayout layout = res.Layout;
            List<HkxObject> roots = FindRootShapes(res, pf, layout);

            foreach (HkxObject root in roots)
            {
                HavokShape shape = DecodeShape(pf, res.Bytes, layout, root, 0, scene.Warnings);
                if (shape == null)
                    continue;

                if (shape.Compound != null)
                    scene.RootCompounds.Add(shape.Compound);
                else
                    scene.RootShapes.Add(shape);
            }

            if (scene.RootCompounds.Count == 0 && scene.RootShapes.Count == 0)
            {
                // 退路：把 packfile 里所有复合形状都当作根
                foreach (HkxObject obj in pf.Objects)
                {
                    if (!layout.TryGetClass(obj.ClassName, out HavokLayoutClass cls) || !cls.TryMember("instances", out _))
                        continue;
                    HavokShape shape = DecodeShape(pf, res.Bytes, layout, obj, 0, scene.Warnings);
                    if (shape?.Compound != null)
                        scene.RootCompounds.Add(shape.Compound);
                }
            }

            foreach (HavokCompound c in scene.RootCompounds)
                ApplyWorld(c, Matrix4x4.Identity, 0);

            ApplyResHeaderBounds(res, scene);

            foreach (HavokShape s in scene.RootShapes)
                scene.SingleShapes++;

            scene.CountStats();
            return scene;
        }

        /// <summary>
        /// 未支持的形状没有可用几何；
        /// 如果这类形状数正好等于 res 头部局部 AABB 的数量（BF1 里是每个 part 一个），
        /// 就把头部 AABB 当作这些形状的包围盒来显示，至少不丢空间信息。
        /// （压缩网格形状现在由 HkxCompressedMesh 解出真实网格，不会再走到这里。）
        /// </summary>
        private static void ApplyResHeaderBounds(HavokRes res, HavokScene scene)
        {
            int count = Math.Min(res.LocalAabbMin.Count, res.LocalAabbMax.Count);
            if (count == 0)
                return;

            // 没有任何几何、也没有自身包围盒的形状（典型是暂不支持的形状类）。
            // BF1 里每个 part 在头部有一个局部 AABB，所以"无几何形状数 == part 数"时
            // 就按顺序把头部 AABB 当成这些形状的包围盒 —— 只显示 it，不假装解出了网格。
            List<HavokShape> unbounded = new List<HavokShape>();
            CollectUnbounded(scene, unbounded);

            if (unbounded.Count == 0 || unbounded.Count != count)
                return;

            for (int i = 0; i < count; i++)
            {
                Vector4 min = res.LocalAabbMin[i];
                Vector4 max = res.LocalAabbMax[i];
                unbounded[i].SetLocalBounds(
                    new Vector3(min.X, min.Y, min.Z),
                    new Vector3(max.X, max.Y, max.Z));
                unbounded[i].FromResHeaderBounds = true;
            }

            scene.Warnings.Add(
                $"{count} 个形状没有可用几何（暂不支持的形状类），已按 res 头部的 part 局部 AABB 显示包围盒。");
        }

        private static void CollectUnbounded(HavokScene scene, List<HavokShape> result)
        {
            foreach (HavokShape shape in scene.RootShapes)
                CollectUnbounded(shape, result, 0);

            foreach (HavokCompound compound in scene.RootCompounds)
                CollectUnbounded(compound, result, 0);
        }

        private static void CollectUnbounded(HavokCompound compound, List<HavokShape> result, int depth)
        {
            if (compound == null || depth > MaxDepth)
                return;
            foreach (HavokShapeInstance inst in compound.Instances)
                CollectUnbounded(inst.Shape, result, depth);
        }

        private static void CollectUnbounded(HavokShape shape, List<HavokShape> result, int depth)
        {
            if (shape == null || depth > MaxDepth)
                return;

            if (shape.Compound != null)
            {
                CollectUnbounded(shape.Compound, result, depth + 1);
                return;
            }

            if (shape.HasLocalBounds || shape.IsPolytope || shape.IsSphere || shape.IsCapsule || shape.IsCompressedMesh)
                return;

            if (shape.Unsupported == null)
                return;

            result.Add(shape);
        }

        private static List<HkxObject> FindRootShapes(HavokRes res, HkxPackFile pf, HavokLayout layout)
        {
            List<HkxObject> result = new List<HkxObject>();
            HkxObject container = ShapeLookup.FindFirst(pf, "HavokPhysicsContainer");
            if (container != null && layout.TryGetClass("HavokPhysicsContainer", out HavokLayoutClass cls)
                && cls.TryMember("shapes", out HavokLayoutMember member))
            {
                int field = container.FileOffset + member.Offset;
                int arrayFile = pf.ResolveArray(field);
                int count = arrayFile >= 0 ? (int)HavokRes.ReadU32(res.Bytes, field + pf.PointerSize) : 0;
                int stride = pf.PointerSize;

                if (arrayFile >= 0 && count > 0 && count < 4096 && arrayFile + count * stride <= res.Bytes.Length)
                {
                    for (int i = 0; i < count; i++)
                    {
                        HkxObject obj = pf.ResolveObject(arrayFile + i * stride);
                        if (obj != null)
                            result.Add(obj);
                    }
                }
            }

            if (result.Count == 0)
            {
                foreach (HkxObject obj in pf.Objects)
                {
                    if (obj.ClassName.EndsWith("CompoundShape", StringComparison.Ordinal))
                        result.Add(obj);
                }
            }

            return result;
        }

        /// <summary>重新计算所有实例的世界矩阵（编辑 transform 后调用）。</summary>
        public static void UpdateWorldMatrices(HavokScene scene)
        {
            if (scene == null)
                return;
            foreach (HavokCompound c in scene.RootCompounds)
                ApplyWorld(c, Matrix4x4.Identity, 0);
        }

        private static void ApplyWorld(HavokCompound compound, Matrix4x4 parentWorld, int depth)
        {
            if (depth > MaxDepth)
                return;

            foreach (HavokShapeInstance inst in compound.Instances)
            {
                inst.WorldMatrix = Matrix4x4.Multiply(inst.LocalMatrix, parentWorld);
                if (inst.Shape?.Compound != null)
                    ApplyWorld(inst.Shape.Compound, inst.WorldMatrix, depth + 1);
            }
        }

        public static HavokShape DecodeShape(HkxPackFile pf, byte[] data, HavokLayout layout, HkxObject obj, int depth, List<string> warnings)
        {
            HavokShape shape = new HavokShape { Object = obj, ClassName = obj.ClassName };

            // 压缩网格形状：几何在 hknpCompressedMeshShapeData.meshTree 里，
            // 走专门的解码器（布局常量见 HkxCompressedMesh，全部来自 IDA 的 hkClass 注册表）。
            if (obj.ClassName == "hknpCompressedMeshShape")
            {
                shape.Flags = ReadU16(data, obj.FileOffset + 0x10);        // hknpShape.flags
                shape.ConvexRadius = ReadF32(data, obj.FileOffset + 0x14); // hknpShape.convexRadius
                if (HkxCompressedMesh.TryDecode(pf, data, obj, shape, warnings))
                    return shape;

                shape.Unsupported = "hknpCompressedMeshShape（压缩网格解码失败）";
                return shape;
            }

            if (!layout.TryGetClass(obj.ClassName, out HavokLayoutClass cls))
            {
                shape.Unsupported = obj.ClassName.IndexOf("CompressedMesh", StringComparison.Ordinal) >= 0
                    ? $"{obj.ClassName}（压缩网格暂不支持）"
                    : $"布局表里没有类 {obj.ClassName}（不解析成员）";
                return shape;
            }

            shape.Flags = ReadU16(data, obj.FileOffset + OffsetOf(cls, "flags", 0x10));
            shape.ConvexRadius = ReadF32(data, obj.FileOffset + OffsetOf(cls, "convexRadius", 0x14));

            if (cls.TryMember("instances", out _))
            {
                shape.Compound = DecodeCompound(pf, data, layout, obj, cls, depth, warnings);
                shape.Compound.Shape = shape;
                return shape;
            }

            bool hasVertices = cls.TryMember("vertices", out HavokLayoutMember verticesMember);
            if (hasVertices)
            {
                int field = obj.FileOffset + verticesMember.Offset;
                ReadRelArrayVector4(data, field, shape.Vertices, 0, out _);
            }

            bool isPolytope = cls.TryMember("faces", out HavokLayoutMember facesMember);
            if (isPolytope && depth < MaxDepth)
            {
                shape.IsPolytope = true;
                ReadRelArrayVector4(data, obj.FileOffset + OffsetOf(cls, "planes", 0x40), shape.Planes, 0, out _);

                int facesField = obj.FileOffset + facesMember.Offset;
                ReadRelArrayRaw(data, facesField, 4, out int faceCount, out int faceData, out _);
                for (int i = 0; i < faceCount; i++)
                {
                    int p = faceData + i * 4;
                    if (p + 4 > data.Length)
                        break;
                    shape.Faces.Add(new HavokFace
                    {
                        MinHalfAngle = data[p],
                        NumIndices = data[p + 1],
                        FirstIndex = (ushort)(data[p + 2] | (data[p + 3] << 8)),
                    });
                }

                int indicesField = obj.FileOffset + OffsetOf(cls, "indices", 0x48);
                ReadRelArrayRaw(data, indicesField, 1, out int indexCount, out int indexData, out _);
                for (int i = 0; i < indexCount && indexData + i < data.Length; i++)
                    shape.Indices.Add(data[indexData + i]);

                ValidatePolytope(shape, warnings);
                foreach (Vector4 v in shape.Vertices)
                    shape.GrowLocalBounds(new Vector3(v.X, v.Y, v.Z));
                shape.ExpandLocalBounds(Math.Max(0f, shape.ConvexRadius));
                return shape;
            }

            if (obj.ClassName == "hknpSphereShape")
            {
                shape.IsSphere = true;
                shape.Radius = shape.ConvexRadius;
                shape.SphereCenter = shape.Vertices.Count > 0
                    ? new Vector3(shape.Vertices[0].X, shape.Vertices[0].Y, shape.Vertices[0].Z)
                    : Vector3.Zero;
                shape.SetLocalBounds(shape.SphereCenter - new Vector3(shape.Radius), shape.SphereCenter + new Vector3(shape.Radius));
                return shape;
            }

            if (obj.ClassName == "hknpCapsuleShape")
            {
                shape.IsCapsule = true;
                shape.Radius = shape.ConvexRadius;
                shape.CapsuleA = ReadVec3(data, obj.FileOffset + OffsetOf(cls, "a", 0x50));
                shape.CapsuleB = ReadVec3(data, obj.FileOffset + OffsetOf(cls, "b", 0x60));
                Vector3 min = Vector3.Min(shape.CapsuleA, shape.CapsuleB) - new Vector3(shape.Radius);
                Vector3 max = Vector3.Max(shape.CapsuleA, shape.CapsuleB) + new Vector3(shape.Radius);
                shape.SetLocalBounds(min, max);
                return shape;
            }

            shape.Unsupported = $"暂不支持 {obj.ClassName}（只显示局部 AABB）";
            return shape;        }

        private static HavokCompound DecodeCompound(HkxPackFile pf, byte[] data, HavokLayout layout, HkxObject obj, HavokLayoutClass cls, int depth, List<string> warnings)
        {
            HavokCompound compound = new HavokCompound
            {
                Object = obj,
                PackFile = pf,
            };

            HavokLayoutMember instancesMember = cls.Member("instances");
            int field = obj.FileOffset + instancesMember.Offset;
            int arrayFile = pf.ResolveArray(field);
            int count = arrayFile >= 0 ? (int)HavokRes.ReadU32(data, field + pf.PointerSize) : 0;

            if (arrayFile < 0)
            {
                compound.Error = "instances 数组没有 fixup（数组指针为 0）";
                return compound;
            }

            if (count < 0 || count > 100000)
            {
                compound.Error = $"instances 数量异常：{count}";
                return compound;
            }

            if (!layout.TryGetClass("hknpShapeInstance", out HavokLayoutClass instanceCls))
            {
                compound.Error = "布局表缺少 hknpShapeInstance";
                return compound;
            }

            int stride = instanceCls.Size;
            if (stride <= 0)
                stride = 128;

            int shapePtrOffset = OffsetOf(instanceCls, "shape", 0x50);
            int scaleOffset = OffsetOf(instanceCls, "scale", 0x40);
            int shapeTagOffset = OffsetOf(instanceCls, "shapeTag", 0x58);
            int destructionTagOffset = OffsetOf(instanceCls, "destructionTag", 0x5A);

            for (int i = 0; i < count; i++)
            {
                int off = arrayFile + i * stride;
                if (off + stride > data.Length)
                    break;

                HavokShapeInstance inst = new HavokShapeInstance
                {
                    Index = i,
                    Owner = compound,
                    PackFile = pf,
                    FileOffset = off,
                    DataOffset = off - pf.DataStartFile,
                    Transform = ReadMatrix4x4(data, off),
                    Scale = ReadVector4(data, off + scaleOffset),
                    ShapeTag = ReadU16(data, off + shapeTagOffset),
                    DestructionTag = ReadU16(data, off + destructionTagOffset),
                };

                HkxObject child = pf.ResolveObject(off + shapePtrOffset);
                if (child == null)
                {
                    inst.Error = "shape 指针没有 fixup（可能指向外部或未导出）";
                }
                else
                {
                    inst.Shape = DecodeShape(pf, data, layout, child, depth + 1, warnings);
                }

                compound.Instances.Add(inst);
            }

            // 复合形状自身的局部 AABB
            if (cls.TryMember("aabb", out HavokLayoutMember aabbMember))
            {
                int p = obj.FileOffset + aabbMember.Offset;
                Vector4 min = ReadVector4(data, p);
                Vector4 max = ReadVector4(data, p + 16);
                compound.HasAabb = true;
                compound.AabbMin = new Vector3(min.X, min.Y, min.Z);
                compound.AabbMax = new Vector3(max.X, max.Y, max.Z);
            }

            // boundingVolumeData → hknpStaticCompoundShapeData → aabbTree
            if (cls.TryMember("boundingVolumeData", out HavokLayoutMember bvMember))
            {
                HkxObject shapeData = pf.ResolveObject(obj.FileOffset + bvMember.Offset);
                if (shapeData != null)
                    compound.ShapeData = DecodeShapeData(pf, data, layout, shapeData, warnings);
            }

            return compound;
        }

        private static HavokShapeData DecodeShapeData(HkxPackFile pf, byte[] data, HavokLayout layout, HkxObject obj, List<string> warnings)
        {
            HavokShapeData sd = new HavokShapeData { Object = obj };

            if (!layout.TryGetClass(obj.ClassName, out HavokLayoutClass cls) || !cls.TryMember("aabbTree", out HavokLayoutMember treeMember))
            {
                sd.NodeStatus = "缺少 aabbTree 布局";
                return sd;
            }

            int tree = obj.FileOffset + treeMember.Offset;
            int nodesField = tree;                     // hkcdStaticTree::Tree.nodes @+0x00
            int count = (int)HavokRes.ReadU32(data, nodesField + 8);
            int arrayFile = pf.ResolveArray(nodesField);
            int domain = tree + 0x10;                  // domain @+0x10（已用 cratewood_02 数据验证）

            Vector4 dmin = ReadVector4(data, domain);
            Vector4 dmax = ReadVector4(data, domain + 16);
            sd.DomainMin = new Vector3(dmin.X, dmin.Y, dmin.Z);
            sd.DomainMax = new Vector3(dmax.X, dmax.Y, dmax.Z);
            sd.NodeCount = Math.Max(0, Math.Min(count, 100000));
            sd.NodesFileOffset = arrayFile;

            if (arrayFile < 0)
            {
                sd.NodeStatus = "节点数组没有 fixup";
                return sd;
            }

            if (HkxAabbTreeCodec.TryDecode(data, arrayFile, sd.NodeCount, sd.DomainMin, sd.DomainMax, sd.NodeBoxes, out string status))
            {
                sd.NodesDecoded = true;
                sd.NodeStatus = status;
            }
            else
            {
                sd.NodeStatus = status;
                sd.NodeBoxes.Clear();
            }

            return sd;
        }

        private static void ValidatePolytope(HavokShape shape, List<string> warnings)
        {
            int vertexCount = shape.Vertices.Count;
            int indexCount = shape.Indices.Count;

            for (int f = 0; f < shape.Faces.Count; f++)
            {
                HavokFace face = shape.Faces[f];
                if (face.FirstIndex + face.NumIndices > indexCount)
                {
                    warnings.Add($"凸包面 {f} 的索引范围 [{face.FirstIndex}, +{face.NumIndices}) 超出索引数 {indexCount}");
                    face.NumIndices = 0;
                    shape.Faces[f] = face;
                    continue;
                }

                for (int i = 0; i < face.NumIndices; i++)
                {
                    int idx = shape.Indices[face.FirstIndex + i];
                    if (idx >= vertexCount)
                    {
                        warnings.Add($"凸包面 {f} 引用了顶点 {idx}，但只有 {vertexCount} 个顶点");
                        face.NumIndices = 0;
                        shape.Faces[f] = face;
                        break;
                    }
                }
            }
        }

        private static int OffsetOf(HavokLayoutClass cls, string member, int fallback)
            => cls.TryMember(member, out HavokLayoutMember m) ? m.Offset : fallback;

        #region -- 二进制小工具 --

        private static void ReadRelArrayRaw(byte[] data, int fieldOffset, int elementSize, out int count, out int dataOffset, out string error)
        {
            count = 0;
            dataOffset = 0;
            error = null;

            if (fieldOffset + 4 > data.Length)
            {
                error = $"rel array 字段 0x{fieldOffset:X} 越界";
                return;
            }

            ushort entries = (ushort)(data[fieldOffset] | (data[fieldOffset + 1] << 8));
            ushort relative = (ushort)(data[fieldOffset + 2] | (data[fieldOffset + 3] << 8));
            int start = fieldOffset + relative;

            if (relative < 4 || start < 0 || start > data.Length)
            {
                error = $"rel array 字段 0x{fieldOffset:X} 的偏移 0x{relative:X} 无效";
                return;
            }

            count = entries;
            dataOffset = start;

            if (start + (long)entries * elementSize > data.Length)
            {
                error = $"rel array 数据 0x{start:X} + {entries}*{elementSize} 越界";
                count = 0;
            }
        }

        private static void ReadRelArrayVector4(byte[] data, int fieldOffset, List<Vector4> target, int pad, out string error)
        {
            ReadRelArrayRaw(data, fieldOffset, 16, out int count, out int start, out error);
            for (int i = 0; i < count; i++)
            {
                int p = start + i * 16;
                if (p + 16 > data.Length)
                    break;
                target.Add(ReadVector4(data, p));
            }
        }

        private static float ReadF32(byte[] data, int offset)
        {
            if (offset < 0 || offset + 4 > data.Length)
                return 0f;
            return BitConverter.ToSingle(data, offset);
        }

        private static ushort ReadU16(byte[] data, int offset)
        {
            if (offset < 0 || offset + 2 > data.Length)
                return 0;
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        private static Vector3 ReadVec3(byte[] data, int offset)
            => new Vector3(ReadF32(data, offset), ReadF32(data, offset + 4), ReadF32(data, offset + 8));

        private static Vector4 ReadVector4(byte[] data, int offset)
            => new Vector4(ReadF32(data, offset), ReadF32(data, offset + 4), ReadF32(data, offset + 8), ReadF32(data, offset + 12));

        private static Matrix4x4 ReadMatrix4x4(byte[] data, int offset)
        {
            return new Matrix4x4(
                ReadF32(data, offset + 0), ReadF32(data, offset + 4), ReadF32(data, offset + 8), ReadF32(data, offset + 12),
                ReadF32(data, offset + 16), ReadF32(data, offset + 20), ReadF32(data, offset + 24), ReadF32(data, offset + 28),
                ReadF32(data, offset + 32), ReadF32(data, offset + 36), ReadF32(data, offset + 40), ReadF32(data, offset + 44),
                ReadF32(data, offset + 48), ReadF32(data, offset + 52), ReadF32(data, offset + 56), ReadF32(data, offset + 60));
        }

        #endregion
    }

    /// <summary>解码后的整个场景（供树视图与渲染使用）。</summary>
    public sealed class HavokScene
    {
        public HavokRes Res;
        public List<HavokCompound> RootCompounds = new List<HavokCompound>();
        public List<HavokShape> RootShapes = new List<HavokShape>();
        public List<string> Warnings = new List<string>();

        public int InstanceCount { get; private set; }
        public int TriangleCount { get; private set; }
        public int SingleShapes { get; set; }
        public List<HavokShapeInstance> FlatInstances { get; } = new List<HavokShapeInstance>();

        public void CountStats()
        {
            InstanceCount = 0;
            TriangleCount = 0;
            FlatInstances.Clear();

            foreach (HavokCompound c in RootCompounds)
                CountCompound(c, 0);
        }

        private void CountCompound(HavokCompound compound, int depth)
        {
            if (depth > 4)
                return;

            foreach (HavokShapeInstance inst in compound.Instances)
            {
                InstanceCount++;
                FlatInstances.Add(inst);
                TriangleCount += HavokGeometry.CountTriangles(inst.Shape);
                if (inst.Shape?.Compound != null)
                    CountCompound(inst.Shape.Compound, depth + 1);
            }
        }

        public bool TryGetBounds(out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue);
            max = new Vector3(float.MinValue);
            bool any = false;

            foreach (HavokShapeInstance inst in FlatInstances)
            {
                if (inst.Shape == null || !inst.Shape.HasLocalBounds)
                    continue;
                Vector3 a = inst.Shape.LocalBoundsMin;
                Vector3 b = inst.Shape.LocalBoundsMax;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = new Vector3(
                        (i & 1) == 0 ? a.X : b.X,
                        (i & 2) == 0 ? a.Y : b.Y,
                        (i & 4) == 0 ? a.Z : b.Z);
                    Vector3 world = Vector3.Transform(corner, inst.WorldMatrix);
                    min = Vector3.Min(min, world);
                    max = Vector3.Max(max, world);
                    any = true;
                }
            }

            foreach (HavokShape shape in RootShapes)
            {
                if (!shape.HasLocalBounds)
                    continue;
                min = Vector3.Min(min, shape.LocalBoundsMin);
                max = Vector3.Max(max, shape.LocalBoundsMax);
                any = true;
            }

            if (!any)
            {
                min = new Vector3(-1);
                max = new Vector3(1);
            }
            return any;
        }
    }
}
