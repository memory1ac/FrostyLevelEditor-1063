using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace LevelEditorPlugin.Havok
{
    /// <summary>
    /// hknpCompressedMeshShape（压缩网格形状）解码器。
    ///
    /// 结构（BF1 = Havok 2013.3，全部由 IDA 读 bf1_dump_SCY.exe 里的 hkClass 静态注册表确认）：
    ///   hknpCompressedMeshShape (0xA0)
    ///     data           @0x60 hkRefPtr&lt;hknpCompressedMeshShapeData&gt;
    ///     quadIsFlat     @0x68（hkBitField，未使用）
    ///     triangleIsInternal @0x80（hkBitField，未使用）
    ///   hknpCompressedMeshShapeData (0xF0) : hkReferencedObject
    ///     meshTree       @0x10 hkRefPtr&lt;hknpCompressedMeshShapeTree&gt;
    ///     simdTree       @0xB0（hkcdSimdTree，宽阶段 BVH，渲染用不到）
    ///   hknpCompressedMeshShapeTree (0xA0) : hkcdStaticMeshTree&lt;CommonConfig&lt;uint,ull,1,1,21&gt;, DataRun&gt;
    ///     hkcdStaticMeshTree 部分：packedVertices @0x70（hkArray&lt;u32&gt;）
    ///                             sharedVertices @0x80（hkArray&lt;u64&gt;）、primitiveDataRuns @0x90
    ///     hkcdStaticMeshTreeBase 部分（0x70）：
    ///       numPrimitiveKeys @0x30  bitsPerKey @0x34  maxKeyValue @0x38
    ///       sections @0x40（hkArray&lt;Section&gt;，元素 0x60）
    ///       primitives @0x50（hkArray&lt;Primitive&gt;，元素 4 = 4 个 u8）
    ///       sharedVerticesIndex @0x60（hkArray&lt;u16&gt;）
    ///     hkcdStaticTree::Tree 部分：nodes @0x00、domain @0x10
    ///   hkcdStaticMeshTreeBase::Section (0x60) : hkcdStaticTree::Tree (0x30)
    ///     codecParms @0x30（REAL CARRAY）：min xyz（0x30..0x3C）+ step xyz（0x3C..0x48）
    ///     firstPackedVertex @0x48 (u32)
    ///     sharedVertices @0x4C (u32)：低 8 位 = 本段打包顶点数，高 24 位 = 本段共享顶点在
    ///                  sharedVerticesIndex 里的起始下标（与各段 numSharedIndices 累加一致，已用数据核对）
    ///     primitives @0x50 (u32)：低 8 位 = 面数，第 8..23 位 = 该段第一个面
    ///     dataRuns   @0x54 (u32)：同上打包
    ///     numPackedVertices @0x58 (u8)   numSharedIndices @0x59 (u8)   leafIndex/layerData/flags @0x5A..0x5F
    ///
    /// 顶点量化：
    ///   打包顶点（packedVertices @0x70，每个 4 字节）：pos = section.codecParms.min + q * step，
    ///     位段依次 x → y → z（低位在前），BF1 实测 11/11/10。
    ///   共享顶点（sharedVertices @0x80，每个 8 字节）：pos = tree.domain.min + q * (domainSpan / (2^21-1))，
    ///     位段 21/21/21（CommonConfig 里的 21 就是单轴上限），用整棵树的 domain 当量化网格。
    ///
    /// 面索引的映射（HouseFarm_01 这类有共享顶点的资源才用得到），两层：
    ///   local &lt; numPackedVertices  → 打包顶点 firstPackedVertex + local（按本段 codecParms 解）
    ///   local &gt;= numPackedVertices → sharedVertices[ sharedVerticesIndex[firstShared + local - numPackedVertices] ]
    /// 也就是 sharedVerticesIndex 存的是 sharedVertices 的下标，不是打包顶点下标
    /// （HouseFarm 实测：sharedVerticesIndex 取值 0..102，而 sharedVertices 正好 103 个元素）。
    ///
    /// 位宽：Section 的成员表里没有任何位宽字段（IDA 全量核对过），所以是编进 Havok 的固定常量。
    /// 但不能用「domain 跨度 / step + 1 == 2^bits」去反推——step 是按填充过的盒子选的，
    /// 实际用到的 q 范围可以明显小于 2^bits−1（HouseFarm_01 的 x 轴比值在 2047..7703 之间乱跳）。
    /// 所以改为用数据自证：拿候选拆分解码，检查顶点是否都落在该段 domain 内，
    /// 首选 BF1 实测的 11/11/10，不合格才枚举（每棵树独立判定）。
    /// </summary>
    public static class HkxCompressedMesh
    {
        // hknpCompressedMeshShape
        private const int ShapeDataPointer = 0x60;

        // hknpCompressedMeshShapeData
        private const int DataMeshTree = 0x10;

        // hkcdStaticMeshTreeBase
        private const int TreeSections = 0x40;
        private const int TreePrimitives = 0x50;
        private const int TreeSharedVerticesIndex = 0x60;
        private const int TreePackedVertices = 0x70;
        private const int TreeSharedVertices = 0x80;
        private const int TreeDataRuns = 0x90;

        // hkcdStaticTree::Tree
        private const int TreeDomain = 0x10;

        // hkcdStaticMeshTreeBase::Section
        private const int SectionSize = 0x60;
        private const int SectionDomain = 0x10;
        private const int SectionCodecParms = 0x30;
        private const int SectionFirstPackedVertex = 0x48;
        private const int SectionPrimitives = 0x50;
        private const int SectionDataRunsOffset = 0x54;
        private const int SectionNumPackedVertices = 0x58;
        private const int SectionNumSharedIndices = 0x59;

        /// <summary>CommonConfig&lt;uint,ull,1,1,21&gt;：单轴最多 21 位。</summary>
        private const int MaxBitsPerAxis = 21;

        /// <summary>共享顶点的位宽：x 21 / y 21 / z 22（IDA 反编译取掩码 0x1FFFFF,0x1FFFFF,0x3FFFFF 确认）。</summary>
        private static readonly int[] SharedBits = { 21, 21, 22 };

        /// <summary>
        /// BF1 的 Havok 编译进去的固定拆分（FT17 / Shield_Deploy / cratewood / FenceMetal / HouseFarm
        /// 共 5 个资源、数百段实测都是 11/11/10）。先按它解，解完用 domain 校验。
        /// </summary>
        private static readonly int[] DefaultBits = { 11, 11, 10 };

        /// <summary>顶点允许超出 domain 的步数（浮点量化误差，不是编码错误）。</summary>
        private const double AllowedExcessSteps = 8.0;

        /// <summary>校验时每个候选拆分最多检查的顶点数（大资源防止界面卡住）。</summary>
        private const int MaxProbeVertices = 8192;

        /// <summary>一个 section 参与解码所需的全部字段。</summary>
        private sealed class SectionInfo
        {
            public int Offset;

            public int FirstPrimitive;
            public int NumPrimitives;
            public int FirstSharedVertex;
            public int NumSharedIndices;

            public float[] Parms = new float[6];
            public Vector3 DomainMin;
            public Vector3 DomainMax;
            public int NumPacked;
            public int FirstPacked;
            public bool Usable;
        }

        /// <summary>一个 meshTree 里解码需要的东西。</summary>
        private sealed class MeshLayout
        {
            public int SectionsFile;
            public int SectionCount;
            public int PrimitivesFile = -1;
            public int PrimitiveCount;
            public int PackedFile = -1;
            public int PackedCount;
            public int SharedIndexFile = -1;
            public int SharedIndexCount;
            public int SharedFile = -1;
            public int SharedCount;
            public Vector3 TreeDomainMin;
            public Vector3 TreeDomainMax;
            public List<SectionInfo> Sections = new List<SectionInfo>();
        }

        public static bool TryDecode(HkxPackFile pf, byte[] data, HkxObject obj, HavokShape shape, List<string> warnings)
        {
            MeshLayout layout = Collect(pf, data, obj, warnings);
            if (layout == null)
                return false;

            int[] bits = ResolveBits(data, layout, warnings, obj.ClassName);

            int packedCount = layout.PackedCount;
            int sharedCount = layout.SharedCount;
            int total = packedCount + sharedCount;

            // 顶点表：先打包顶点（用所属 section 的 codecParms），再共享顶点（用 tree domain）
            Vector3[] positions = new Vector3[total];
            bool[] decoded = new bool[total];

            foreach (SectionInfo section in layout.Sections)
            {
                if (!section.Usable)
                    continue;

                for (int i = 0; i < section.NumPacked; i++)
                {
                    int global = section.FirstPacked + i;
                    if (global < 0 || global >= packedCount)
                        break;

                    positions[global] = DecodePoint(data, layout, section, bits, global);
                    decoded[global] = true;
                }
            }

            if (sharedCount > 0)
            {
                for (int i = 0; i < sharedCount; i++)
                {
                    int at = layout.SharedFile + i * 8;
                    if (at < 0 || at + 8 > data.Length)
                        break;

                    ulong packed = HavokRes.ReadU32(data, at) | ((ulong)HavokRes.ReadU32(data, at + 4) << 32);
                    positions[packedCount + i] = DecodeShared(layout, packed);
                    decoded[packedCount + i] = true;
                }
            }

            int baseIndex = shape.Vertices.Count;
            for (int i = 0; i < total; i++)
            {
                // 段没有覆盖到的位置：填 0 占位，面引用到它会被跳过
                Vector3 p = decoded[i] ? positions[i] : Vector3.Zero;
                shape.Vertices.Add(new Vector4(p.X, p.Y, p.Z, 1f));
            }

            int skippedFaces = 0;
            int firstBadIndex = -1;

            for (int sectionIndex = 0; sectionIndex < layout.Sections.Count; sectionIndex++)
            {
                SectionInfo section = layout.Sections[sectionIndex];
                for (int p = 0; p < section.NumPrimitives; p++)
                {
                    int face = section.FirstPrimitive + p;
                    int off = layout.PrimitivesFile + face * 4;
                    if (off < 0 || off + 4 > data.Length)
                        break;

                    int[] corners = new int[4];
                    int count = 0;
                    bool ok = true;

                    for (int k = 0; k < 4 && ok; k++)
                    {
                        int global = MapVertexIndex(data, layout, section, data[off + k]);
                        if (global < 0 || global >= total || !decoded[global])
                        {
                            ok = false;
                            skippedFaces++;
                            if (firstBadIndex < 0)
                                firstBadIndex = data[off + k];
                            break;
                        }

                        int index = baseIndex + global;
                        if (count > 0 && corners[count - 1] == index)
                            continue;

                        corners[count++] = index;
                    }

                    if (!ok || count < 3)
                        continue;

                    int[] polygon = new int[count];
                    Array.Copy(corners, polygon, count);
                    shape.Polygons.Add(polygon);
                    if (shape.PolygonSections == null)
                        shape.PolygonSections = new List<int>();
                    shape.PolygonSections.Add(sectionIndex);
                }
            }

            if (skippedFaces > 0)
            {
                warnings.Add($"{obj.ClassName}: {skippedFaces} 个面引用了不存在的顶点"
                    + $"（首个越界索引 {firstBadIndex}，共 {packedCount} 个打包顶点 + {sharedCount} 个共享顶点），已跳过");
            }

            if (shape.Polygons.Count == 0)
            {
                warnings.Add($"{obj.ClassName}: 解出 {total} 个顶点但没有可用面");
                return false;
            }

            shape.IsCompressedMesh = true;
            shape.MeshPrimitiveCount = layout.PrimitiveCount;
            shape.MeshSectionCount = layout.Sections.Count;
            shape.MeshSharedVertexCount = sharedCount;

            foreach (Vector4 v in shape.Vertices)
                shape.GrowLocalBounds(new Vector3(v.X, v.Y, v.Z));

            // 量化常量本身就有误差，稍微外扩一点，避免包围盒比几何还紧
            shape.ExpandLocalBounds(0.002f);

            return true;
        }

        private static MeshLayout Collect(HkxPackFile pf, byte[] data, HkxObject obj, List<string> warnings)
        {
            if (pf == null || data == null || obj == null)
                return null;

            HkxObject dataObj = pf.ResolveObject(obj.FileOffset + ShapeDataPointer);
            if (dataObj == null)
            {
                warnings.Add($"{obj.ClassName} 的 data 指针没有 fixup，压缩网格无法解码");
                return null;
            }

            int tree = dataObj.FileOffset + DataMeshTree;

            if (!TryArray(pf, data, tree + TreeSections, out int sectionsFile, out int sectionCount) || sectionCount <= 0)
            {
                warnings.Add($"{obj.ClassName}: meshTree.sections 为空（data @0x{dataObj.FileOffset:X}）");
                return null;
            }

            MeshLayout layout = new MeshLayout
            {
                SectionsFile = sectionsFile,
                SectionCount = sectionCount,
            };

            TryArray(pf, data, tree + TreePrimitives, out layout.PrimitivesFile, out layout.PrimitiveCount);
            TryArray(pf, data, tree + TreePackedVertices, out layout.PackedFile, out layout.PackedCount);
            TryArray(pf, data, tree + TreeSharedVerticesIndex, out layout.SharedIndexFile, out layout.SharedIndexCount);
            TryArray(pf, data, tree + TreeSharedVertices, out layout.SharedFile, out layout.SharedCount);
            layout.TreeDomainMin = ReadVec3(data, tree + TreeDomain);
            layout.TreeDomainMax = ReadVec3(data, tree + TreeDomain + 16);

            if (layout.PrimitivesFile < 0 || layout.PrimitiveCount <= 0
                || layout.PackedFile < 0 || layout.PackedCount <= 0)
            {
                warnings.Add($"{obj.ClassName}: primitives/packedVertices 为空"
                    + $"（{layout.PrimitiveCount} 面 / {layout.PackedCount} 顶点）");
                return null;
            }

            if (layout.SharedIndexCount > 0 && (layout.SharedFile < 0 || layout.SharedCount <= 0))
            {
                warnings.Add($"{obj.ClassName}: 有 {layout.SharedIndexCount} 个共享索引但 sharedVertices 为空");
                layout.SharedIndexCount = 0;
            }

            for (int i = 0; i < sectionCount; i++)
            {
                int s = sectionsFile + i * SectionSize;
                if (s + SectionSize > data.Length)
                    break;

                layout.Sections.Add(new SectionInfo { Offset = s });
            }

            int sharedCursor = 0;
            foreach (SectionInfo section in layout.Sections)
            {
                ReadSection(data, layout, section);

                // firstShared 用累加值最稳（与 0x4C 字段高 24 位一致，已核对）
                section.FirstSharedVertex = sharedCursor;
                sharedCursor += section.NumSharedIndices;
            }

            ResolvePrimitiveRanges(data, layout);

            return layout;
        }

        /// <summary>把 section 的 codecParms / domain / 顶点与共享索引区间读出来。</summary>
        private static void ReadSection(byte[] data, MeshLayout layout, SectionInfo section)
        {
            int s = section.Offset;

            for (int i = 0; i < 6; i++)
                section.Parms[i] = ReadF32(data, s + SectionCodecParms + i * 4);

            section.DomainMin = ReadVec3(data, s + SectionDomain);
            section.DomainMax = ReadVec3(data, s + SectionDomain + 16);

            section.NumPacked = Math.Max(0, (int)data[s + SectionNumPackedVertices]);
            section.NumSharedIndices = Math.Max(0, (int)data[s + SectionNumSharedIndices]);
            section.FirstPacked = (int)Math.Min(HavokRes.ReadU32(data, s + SectionFirstPackedVertex), (uint)layout.PackedCount);

            if (section.FirstPacked + section.NumPacked > layout.PackedCount)
                section.NumPacked = Math.Max(0, layout.PackedCount - section.FirstPacked);

            section.Usable = section.NumPacked > 0;
        }

        /// <summary>
        /// primitives / dataRuns 是打包的 (count, first)。BF1 里是「低 8 位 count，接着 16 位 first」
        /// （HouseFarm 实测：110/92/105/53/74 段累加正好等于总数）；小的资源用 u16/u16 也能对上，
        /// 所以两种都试，取「各段 count 之和 == 全局数组长度」的那种。
        /// </summary>
        private static void ResolvePrimitiveRanges(byte[] data, MeshLayout layout)
        {
            bool useNarrow = FitsSum(data, layout, SectionPrimitives, layout.PrimitiveCount, true);

            foreach (SectionInfo section in layout.Sections)
            {
                uint prim = HavokRes.ReadU32(data, section.Offset + SectionPrimitives);
                uint runs = HavokRes.ReadU32(data, section.Offset + SectionDataRunsOffset);

                section.NumPrimitives = useNarrow ? (int)(prim & 0xFF) : (int)(prim & 0xFFFF);
                section.FirstPrimitive = useNarrow ? (int)((prim >> 8) & 0xFFFF) : (int)(prim >> 16);
            }
        }

        /// <summary>按给定打包方式算各段 count 之和，看是否等于全局数组长度。</summary>
        private static bool FitsSum(byte[] data, MeshLayout layout, int field, int total, bool narrow)
        {
            int sum = 0;
            foreach (SectionInfo section in layout.Sections)
            {
                uint value = HavokRes.ReadU32(data, section.Offset + field);
                sum += narrow ? (int)(value & 0xFF) : (int)(value & 0xFFFF);
            }

            return sum == total;
        }

        /// <summary>把 primitive 里的局部索引换成整棵树的顶点下标（打包在前，共享在后）。</summary>
        private static int MapVertexIndex(byte[] data, MeshLayout layout, SectionInfo section, int local)
        {
            if (local < section.NumPacked)
                return section.FirstPacked + local;

            int at = section.FirstSharedVertex + (local - section.NumPacked);
            if (layout.SharedIndexFile < 0 || at < 0 || at >= layout.SharedIndexCount)
                return -1;

            int sharedIndex = HavokRes.ReadU16(data, layout.SharedIndexFile + at * 2);
            if (sharedIndex < 0 || sharedIndex >= layout.SharedCount)
                return -1;

            return layout.PackedCount + sharedIndex;
        }

        #region -- 量化位宽 --

        /// <summary>
        /// 决定这棵树的位宽拆分：先用 BF1 实测常量，越界就枚举所有和为 32 的拆分，
        /// 取「顶点落在 domain 内」的那种（只依赖数据，不用 span/step 反推）。
        /// </summary>
        private static int[] ResolveBits(byte[] data, MeshLayout layout, List<string> warnings, string className)
        {
            double excess = MeasureExcess(data, layout, DefaultBits);
            if (excess <= AllowedExcessSteps)
                return DefaultBits;

            int[] best = DefaultBits;
            double bestExcess = excess;

            for (int bx = 1; bx <= MaxBitsPerAxis; bx++)
            {
                for (int by = 1; by <= MaxBitsPerAxis; by++)
                {
                    int bz = 32 - bx - by;
                    if (bz < 1 || bz > MaxBitsPerAxis)
                        continue;

                    int[] candidate = { bx, by, bz };
                    double value = MeasureExcess(data, layout, candidate);
                    if (value < bestExcess)
                    {
                        bestExcess = value;
                        best = candidate;
                    }
                }
            }

            if (bestExcess > AllowedExcessSteps)
            {
                warnings.Add($"{className}: 量化位宽对不上任何拆分，按 {best[0]}/{best[1]}/{best[2]} 处理"
                    + $"（仍有 {bestExcess:0.#} 步越界）");
            }
            else
            {
                warnings.Add($"{className}: 本树的量化拆分不是 11/11/10，按数据判为 {best[0]}/{best[1]}/{best[2]}");
            }

            return best;
        }

        /// <summary>用给定位宽解一遍，统计顶点超出各段 domain 的步数（截断避免单点爆炸）。</summary>
        private static double MeasureExcess(byte[] data, MeshLayout layout, int[] bits)
        {
            int budget = MaxProbeVertices;
            int totalPacked = Math.Max(1, layout.PackedCount);
            int stride = Math.Max(1, totalPacked / budget);

            double excess = 0;
            foreach (SectionInfo section in layout.Sections)
            {
                if (!section.Usable)
                    continue;

                for (int i = 0; i < section.NumPacked; i += stride)
                {
                    int global = section.FirstPacked + i;
                    if (global < 0 || global >= layout.PackedCount)
                        break;

                    Vector3 p = DecodePoint(data, layout, section, bits, global);
                    excess += Overshoot(p.X, section.DomainMin.X, section.DomainMax.X, section.Parms[3]);
                    excess += Overshoot(p.Y, section.DomainMin.Y, section.DomainMax.Y, section.Parms[4]);
                    excess += Overshoot(p.Z, section.DomainMin.Z, section.DomainMax.Z, section.Parms[5]);
                }
            }

            return excess;
        }

        private static double Overshoot(float value, float min, float max, float step)
        {
            double unit = step > 0f ? step : 1e-5;
            double over = 0;

            if (value < min) over += (min - value) / unit;
            if (value > max) over += (value - max) / unit;

            return Math.Min(over, 64.0);
        }

        /// <summary>解一个打包顶点：min + q * step（各轴按 bits 拆分）。</summary>
        private static Vector3 DecodePoint(byte[] data, MeshLayout layout, SectionInfo section, int[] bits, int global)
        {
            uint packed = HavokRes.ReadU32(data, layout.PackedFile + global * 4);
            int qx = (int)(packed & Mask(bits[0]));
            int qy = (int)((packed >> bits[0]) & Mask(bits[1]));
            int qz = (int)((packed >> (bits[0] + bits[1])) & Mask(bits[2]));

            return new Vector3(
                section.Parms[0] + qx * section.Parms[3],
                section.Parms[1] + qy * section.Parms[4],
                section.Parms[2] + qz * section.Parms[5]);
        }

        /// <summary>
        /// 解一个共享顶点：tree.domain.min + q * (domainSpan / (2^bits-1))，bits = 21/21/22。
        /// 与游戏里 getVertex 的常量 xmmword 0x1434D0180 = (1/(2^21-1), 1/(2^21-1), 1/(2^22-1)) 对齐。
        /// </summary>
        private static Vector3 DecodeShared(MeshLayout layout, ulong packed)
        {
            int qx = (int)(packed & Mask(SharedBits[0]));
            int qy = (int)((packed >> SharedBits[0]) & Mask(SharedBits[1]));
            int qz = (int)((packed >> (SharedBits[0] + SharedBits[1])) & Mask(SharedBits[2]));

            double sx = SharedStep(layout.TreeDomainMin.X, layout.TreeDomainMax.X, SharedBits[0]);
            double sy = SharedStep(layout.TreeDomainMin.Y, layout.TreeDomainMax.Y, SharedBits[1]);
            double sz = SharedStep(layout.TreeDomainMin.Z, layout.TreeDomainMax.Z, SharedBits[2]);

            return new Vector3(
                (float)(layout.TreeDomainMin.X + qx * sx),
                (float)(layout.TreeDomainMin.Y + qy * sy),
                (float)(layout.TreeDomainMin.Z + qz * sz));
        }

        private static double SharedStep(float min, float max, int bits)
        {
            double span = max - min;
            return span > 0 ? span / Mask(bits) : 0.0;
        }

        private static uint Mask(int bits) => bits >= 32 ? 0xFFFFFFFFu : ((1u << bits) - 1u);

        #endregion

        #region -- 诊断 --

        /// <summary>把每个 section 的 domain / codecParms / 位宽写出来，排查量化问题时用。</summary>
        internal static string DescribeSections(HkxPackFile pf, byte[] data, HkxObject obj)
        {
            StringBuilder sb = new StringBuilder();
            List<string> warnings = new List<string>();
            MeshLayout layout = Collect(pf, data, obj, warnings);

            if (layout == null)
                return string.Join(Environment.NewLine, warnings.ToArray());

            int[] bits = ResolveBits(data, layout, warnings, obj.ClassName);

            sb.AppendLine($"{obj.ClassName} @0x{obj.FileOffset:X}: {layout.SectionCount} 段，"
                + $"{layout.PackedCount} 个打包顶点 + {layout.SharedCount} 个共享顶点"
                + $"（{layout.SharedIndexCount} 个共享索引），"
                + $"{layout.PrimitiveCount} 个面，位宽 {bits[0]}/{bits[1]}/{bits[2]}"
                + $"（domain 校验越界 {MeasureExcess(data, layout, bits):0.#} 步）");

            foreach (SectionInfo section in layout.Sections)
            {
                sb.AppendLine($"  section @0x{section.Offset:X}: {section.NumPrimitives} 面 / {section.NumPacked} 顶点"
                    + $"（firstPacked {section.FirstPacked}，共享 {section.NumSharedIndices} 个"
                    + $" @{section.FirstSharedVertex}）");
                sb.AppendLine($"    domain ({section.DomainMin.X:F4},{section.DomainMin.Y:F4},{section.DomainMin.Z:F4})"
                    + $"-({section.DomainMax.X:F4},{section.DomainMax.Y:F4},{section.DomainMax.Z:F4})");
                sb.AppendLine($"    parms  min({section.Parms[0]:F6},{section.Parms[1]:F6},{section.Parms[2]:F6})"
                    + $" step({section.Parms[3]:0.#######},{section.Parms[4]:0.#######},{section.Parms[5]:0.#######})");
            }

            foreach (string w in warnings)
                sb.AppendLine("    " + w);

            return sb.ToString();
        }

        #endregion

        private static bool TryArray(HkxPackFile pf, byte[] data, int field, out int fileOffset, out int count)
        {
            fileOffset = -1;
            count = 0;

            int resolved = pf.ResolveArray(field);
            if (resolved < 0 || resolved >= data.Length)
                return false;

            count = (int)HavokRes.ReadU32(data, field + pf.PointerSize);
            if (count < 0 || count > 1 << 20)
                return false;

            fileOffset = resolved;
            return true;
        }

        private static float ReadF32(byte[] data, int offset)
            => offset >= 0 && offset + 4 <= data.Length ? BitConverter.ToSingle(data, offset) : 0f;

        private static Vector3 ReadVec3(byte[] data, int offset)
            => new Vector3(ReadF32(data, offset), ReadF32(data, offset + 4), ReadF32(data, offset + 8));
    }
}
