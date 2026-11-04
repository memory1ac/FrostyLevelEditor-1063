using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace LevelEditorPlugin.Havok
{
    /// <summary>
    /// Frostbite HavokPhysicsData 资源（res）：
    /// 文件头数组（part 平移 / 局部 AABB / 材质索引 / 材质标志 / detail 索引）+ 两个 Havok packfile（32/64 位）+ 末尾 fixup 表。
    /// 解析以"扫描 magic + 文件头指针"为主，不依赖具体 Profile 的字段顺序。
    /// </summary>
    public sealed class HavokRes
    {
        private const uint Magic1 = 0x57E0E057;
        private const uint Magic2 = 0x10C0C010;

        private sealed class ArrayDesc
        {
            public int Count;
            public int FileOffset;
            public int ElementSize;
            public string Name;
        }

        private readonly List<ArrayDesc> arrays = new List<ArrayDesc>();

        public byte[] Bytes { get; private set; }
        public string Name { get; private set; } = "";
        public HavokLayout Layout { get; private set; }

        public int PartCount { get; private set; }
        public List<Vector4> PartTranslations { get; } = new List<Vector4>();
        public List<Vector4> LocalAabbMin { get; } = new List<Vector4>();
        public List<Vector4> LocalAabbMax { get; } = new List<Vector4>();
        public List<byte> MaterialIndices { get; } = new List<byte>();
        public List<uint> MaterialFlagsAndIndices { get; } = new List<uint>();
        public List<ushort> DetailResourceIndices { get; } = new List<ushort>();
        public int MaterialCountUsed { get; private set; }
        public int HighestMaterialIndex { get; private set; }

        public HkxPackFile PackFile32 { get; private set; }
        public HkxPackFile PackFile64 { get; private set; }
        public HkxPackFile Primary => PackFile64 ?? PackFile32;

        public int HeaderArraysEnd { get; private set; }
        public int HeaderStart { get; private set; }
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Issues { get; } = new List<string>();

        private HavokRes()
        {
        }

        public static HavokRes Parse(byte[] bytes, string name = "", HavokLayout layout = null)
        {
            HavokRes res = new HavokRes
            {
                Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes)),
                Name = name ?? "",
                Layout = layout ?? HavokLayout.BuiltIn,
            };
            res.ParseInternal();
            return res;
        }

        private void ParseInternal()
        {
            if (Bytes.Length < 0x50)
                throw new HkxParseException($"res 太小（{Bytes.Length} 字节），不像是 HavokPhysicsData");

            // 从资源管理器拿到的 res 直接以 partCount 开头；
            // 有些工具导出的 .res 会在前面多出 16 字节 resMeta，所以两个起点都试一遍。
            // 判定标准很硬：文件头数组区结束的位置必须紧跟 packfile magic。
            HkxParseException firstError = null;
            foreach (int headerStart in new[] { 0, 0x10 })
            {
                if (headerStart + 0x50 > Bytes.Length)
                    continue;

                ResetState();
                try
                {
                    ParseAt(headerStart);
                    HeaderStart = headerStart;
                    if (headerStart != 0)
                        Warnings.Add($"数据前面有 0x{headerStart:X} 字节的 resMeta 前缀，已跳过（导出的 .res 有时会带）。");
                    return;
                }
                catch (HkxParseException e)
                {
                    if (firstError == null)
                        firstError = e;
                }
            }

            throw firstError ?? new HkxParseException("无法定位 HavokPhysicsData 的文件头");
        }

        private void ResetState()
        {
            arrays.Clear();
            PartTranslations.Clear();
            LocalAabbMin.Clear();
            LocalAabbMax.Clear();
            MaterialIndices.Clear();
            MaterialFlagsAndIndices.Clear();
            DetailResourceIndices.Clear();
            PackFile32 = null;
            PackFile64 = null;
            Warnings.Clear();
            Issues.Clear();
            HeaderArraysEnd = 0;
            PartCount = 0;
            MaterialCountUsed = 0;
            HighestMaterialIndex = 0;
        }

        private void ParseAt(int headerStart)
        {
            HkxBuffer buf = new HkxBuffer(Bytes, headerStart);
            PartCount = buf.I32("partCount");
            if (PartCount < 0 || PartCount > 100000)
                throw new HkxParseException($"partCount 异常：{PartCount}（起点 0x{headerStart:X}）");

            // 文件头：若干 (count, offset) 数组描述符。BF1 是 5 个；用"偏移是否合理"判断结束，避免写死 Profile。
            // 偏移相对文件头起点（也就是 res 数据本身的开头）。
            string[] names = { "partTranslations", "localAabbs", "materialIndices", "materialFlagsAndIndices", "detailResourceIndices", "unknown6" };
            int[] elementSizes = { 16, 32, 1, 4, 2, 4 };

            for (int i = 0; i < names.Length; i++)
            {
                int headerPos = buf.Position;
                if (headerPos + 12 > Bytes.Length)
                    break;

                int count = buf.I32($"array[{i}] count");
                uint offLow = buf.U32($"array[{i}] offset");
                uint offHigh = buf.U32($"array[{i}] offsetHigh");

                bool plausible = count >= 0 && count <= 1_000_000
                    && offHigh == 0
                    && offLow >= 0x20 && headerStart + (long)offLow < Bytes.Length
                    && (long)count * elementSizes[i] <= Bytes.Length;

                if (!plausible)
                {
                    buf.Position = headerPos;
                    break;
                }

                arrays.Add(new ArrayDesc
                {
                    Count = count,
                    FileOffset = headerStart + (int)offLow,
                    ElementSize = elementSizes[i],
                    Name = names[i],
                });
            }

            if (arrays.Count < 4)
                throw new HkxParseException($"文件头只解析出 {arrays.Count} 个数组（至少需要 4 个），可能不是 HavokPhysicsData");

            int pos = buf.Position;
            if (pos + 2 <= Bytes.Length)
            {
                MaterialCountUsed = Bytes[pos];
                HighestMaterialIndex = Bytes[pos + 1];
            }

            HeaderArraysEnd = 0;
            foreach (ArrayDesc a in arrays)
                HeaderArraysEnd = Math.Max(HeaderArraysEnd, a.FileOffset + a.Count * a.ElementSize);

            // 数组区结束处就是 packfile 起点（cratewood / fencemetal / 带 resMeta 前缀的导出文件都是这样）。
            // 这里卡死这个位置，才能真正区分"从 0x00 开始"和"跳过 16 字节前缀"两种起点。
            int pf1Offset = HkxBuffer.FindMagicPair(Bytes, HeaderArraysEnd,
                Math.Min(Bytes.Length - 8, HeaderArraysEnd + 0x20), Magic1, Magic2);
            if (pf1Offset < 0)
                throw new HkxParseException(
                    $"文件头数组区结束于 0x{HeaderArraysEnd:X}，但那里没有 Havok packfile 的 magic（0x{headerStart:X} 起点可能不对）");

            // 读取数组内容
            HkxBuffer reader = new HkxBuffer(Bytes);
            for (int i = 0; i < arrays.Count; i++)
            {
                ArrayDesc a = arrays[i];
                reader.Position = a.FileOffset;
                for (int e = 0; e < a.Count; e++)
                {
                    switch (i)
                    {
                        case 0:
                            PartTranslations.Add(new Vector4(reader.F32(), reader.F32(), reader.F32(), reader.F32()));
                            break;
                        case 1:
                            LocalAabbMin.Add(new Vector4(reader.F32(), reader.F32(), reader.F32(), reader.F32()));
                            LocalAabbMax.Add(new Vector4(reader.F32(), reader.F32(), reader.F32(), reader.F32()));
                            break;
                        case 2:
                            MaterialIndices.Add(reader.U8());
                            break;
                        case 3:
                            MaterialFlagsAndIndices.Add(reader.U32());
                            break;
                        case 4:
                            DetailResourceIndices.Add(reader.U16());
                            break;
                        default:
                            reader.Skip(a.ElementSize);
                            break;
                    }
                }
            }

            ParsePackFiles(pf1Offset);
        }

        private void ParsePackFiles(int pf1Offset)
        {
            HkxPackFile pf1 = HkxPackFile.Parse(Bytes, pf1Offset, out int pf1End);

            HkxPackFile pf2 = null;
            int pf2Offset = pf1End >= 0 && pf1End + 8 <= Bytes.Length
                ? HkxBuffer.FindMagicPair(Bytes, pf1End, Math.Min(Bytes.Length - 8, pf1End + 0x20), Magic1, Magic2)
                : -1;
            if (pf2Offset >= 0)
                pf2 = HkxPackFile.Parse(Bytes, pf2Offset, out _);

            // 末尾 fixup 表：两个 u32 大小，然后按 packfile 顺序各一段
            int tableOffset = pf2 != null ? pf2.DataEndFile : pf1.DataEndFile;
            if (tableOffset + 8 > Bytes.Length)
                throw new HkxParseException($"fixup 表位置 0x{tableOffset:X} 超出文件长度 0x{Bytes.Length:X}");

            uint size1 = ReadU32(Bytes, tableOffset);
            uint size2 = ReadU32(Bytes, tableOffset + 4);
            int block1 = tableOffset + 8;
            int block2 = block1 + (int)size1;

            // 两块外部 fixup 表按 packfile 顺序排列，但两个 packfile 的块大小经常相同，
            // 所以要按"fixup 是否真的落在对象/数据上"来打分，选分高的那组顺序。
            int directScore = ReadFixups(pf1, block1, pf2, block2);
            int swappedScore = int.MinValue;
            if (pf2 != null && block2 + 8 <= Bytes.Length)
                swappedScore = ReadFixups(pf1, block2, pf2, block1);

            if (swappedScore > directScore)
                ReadFixups(pf1, block2, pf2, block1);
            else
            {
                if (directScore == int.MinValue)
                    throw new HkxParseException($"packfile 的 fixup 表解析失败（块 0x{block1:X} / 0x{block2:X}，大小 0x{size1:X} / 0x{size2:X}）");
                ReadFixups(pf1, block1, pf2, block2);
            }

            if (pf1.PointerSize == 8)
            {
                PackFile64 = pf1;
                PackFile32 = pf2;
            }
            else
            {
                PackFile32 = pf1;
                PackFile64 = pf2;
            }

            if (PackFile64 == null && PackFile32 != null)
                Warnings.Add("这个 res 只有 32 位 packfile，编辑器按 32 位结构解析。");

            if (PackFile32 != null && PackFile64 != null)
            {
                HkxObject c32 = ShapeLookup.FindFirst(PackFile32, "hknpStaticCompoundShape");
                HkxObject c64 = ShapeLookup.FindFirst(PackFile64, "hknpStaticCompoundShape");
                if (c32 != null && c64 != null)
                {
                    int n32 = ShapeLookup.CountInstances(PackFile32, Bytes, c32, Layout);
                    int n64 = ShapeLookup.CountInstances(PackFile64, Bytes, c64, Layout);
                    if (n32 != n64)
                    {
                        Warnings.Add($"32 位与 64 位 packfile 的实例数不一致（{n32} vs {n64}），写回时只会同步 64 位副本。");
                    }
                }
            }

            ValidateStructure();
        }

        private int ReadFixups(HkxPackFile pf1, int block1, HkxPackFile pf2, int block2)
        {
            int score = 0;
            try
            {
                pf1.ReadFixupTables(Bytes, block1);
                score += pf1.ScoreFixups();
            }
            catch (HkxParseException)
            {
                return int.MinValue;
            }

            if (pf2 != null)
            {
                try
                {
                    pf2.ReadFixupTables(Bytes, block2);
                    score += pf2.ScoreFixups();
                }
                catch (HkxParseException)
                {
                    return int.MinValue;
                }
            }

            return score;
        }

        private void ValidateStructure()
        {
            foreach (HkxPackFile pf in new[] { PackFile32, PackFile64 })
            {
                if (pf == null)
                    continue;
                foreach (string issue in pf.Validate(Layout))
                    Issues.Add($"{pf.PointerSize} 位 packfile：{issue}");
            }

            if (Primary == null)
                Issues.Add("没有任何可用的 packfile");
        }

        #region -- 写回 --

        /// <summary>
        /// 把一个实例的 transform / scale / 材质等改动应用到字节副本。
        /// 只做等长原地写入，不改变文件大小与布局。
        /// </summary>
        public byte[] BuildPatchedBytes(IEnumerable<HavokInstanceEdit> edits)
        {
            byte[] output = (byte[])Bytes.Clone();
            foreach (HavokInstanceEdit edit in edits)
            {
                if (edit.Transform.HasValue)
                    WriteTransform(output, edit.FileOffset, edit.Transform.Value);

                if (edit.Scale.HasValue)
                    WriteVector4(output, edit.FileOffset + 0x40, edit.Scale.Value);
            }
            return output;
        }

        /// <summary>
        /// 等长原地写入一个实例的 transform / scale（供编辑器每次拖动无延迟回填）。
        /// 每行第 4 个 float 与 padding 保持原样。
        /// </summary>
        public static void PatchInstance(byte[] buffer, int fileOffset, Matrix4x4? transform, Vector4? scale, int scaleOffset = 0x40)
        {
            if (transform.HasValue)
                WriteTransform(buffer, fileOffset, transform.Value);
            if (scale.HasValue)
                WriteVector4(buffer, fileOffset + scaleOffset, scale.Value);
        }

        private static void WriteTransform(byte[] buffer, int fileOffset, Matrix4x4 m)
        {
            WriteFloat(buffer, fileOffset + 0, m.M11);
            WriteFloat(buffer, fileOffset + 4, m.M12);
            WriteFloat(buffer, fileOffset + 8, m.M13);
            WriteFloat(buffer, fileOffset + 16, m.M21);
            WriteFloat(buffer, fileOffset + 20, m.M22);
            WriteFloat(buffer, fileOffset + 24, m.M23);
            WriteFloat(buffer, fileOffset + 32, m.M31);
            WriteFloat(buffer, fileOffset + 36, m.M32);
            WriteFloat(buffer, fileOffset + 40, m.M33);
            WriteFloat(buffer, fileOffset + 48, m.M41);
            WriteFloat(buffer, fileOffset + 52, m.M42);
            WriteFloat(buffer, fileOffset + 56, m.M43);
            // 每行第 4 个 float（M14/M24/M34/M44）保持原值，游戏里当 padding 用
        }

        private static void WriteVector4(byte[] buffer, int fileOffset, Vector4 v)
        {
            WriteFloat(buffer, fileOffset + 0, v.X);
            WriteFloat(buffer, fileOffset + 4, v.Y);
            WriteFloat(buffer, fileOffset + 8, v.Z);
        }

        private static void WriteFloat(byte[] buffer, int offset, float value)
        {
            if (offset < 0 || offset + 4 > buffer.Length)
                throw new HkxParseException($"写回越界：偏移 0x{offset:X}，文件长度 0x{buffer.Length:X}");
            byte[] tmp = BitConverter.GetBytes(value);
            Buffer.BlockCopy(tmp, 0, buffer, offset, 4);
        }

        #endregion

        internal static uint ReadU32(byte[] data, int offset)
            => (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

        internal static int ReadU16(byte[] data, int offset)
            => data[offset] | (data[offset + 1] << 8);

        public string DescribeArrays()
        {
            StringWriter sw = new StringWriter();
            sw.WriteLine($"partCount = {PartCount}，材质：used={MaterialCountUsed} highest={HighestMaterialIndex}");
            foreach (ArrayDesc a in arrays)
                sw.WriteLine($"  数组 {a.Name,-24} count={a.Count,-4} offset=0x{a.FileOffset:X} 元素 {a.ElementSize} 字节");
            sw.WriteLine($"  数组区结束 0x{HeaderArraysEnd:X}");
            foreach (Vector4 t in PartTranslations)
                sw.WriteLine($"  part 平移 {t.X:F4}, {t.Y:F4}, {t.Z:F4}");
            for (int i = 0; i < LocalAabbMin.Count; i++)
                sw.WriteLine($"  局部 AABB[{i}] min=({LocalAabbMin[i].X:F4},{LocalAabbMin[i].Y:F4},{LocalAabbMin[i].Z:F4}) max=({LocalAabbMax[i].X:F4},{LocalAabbMax[i].Y:F4},{LocalAabbMax[i].Z:F4})");
            return sw.ToString();
        }
    }

    /// <summary>一次实例写回请求。</summary>
    public sealed class HavokInstanceEdit
    {
        public int FileOffset;
        public Matrix4x4? Transform;
        public Vector4? Scale;
    }

    internal static class ShapeLookup
    {
        public static HkxObject FindFirst(HkxPackFile pf, string className)
        {
            foreach (HkxObject obj in pf.Objects)
            {
                if (obj.ClassName == className)
                    return obj;
            }
            return null;
        }

        public static int CountInstances(HkxPackFile pf, byte[] data, HkxObject compound, HavokLayout layout)
        {
            if (!layout.TryGetClass(compound.ClassName, out HavokLayoutClass cls) || !cls.TryMember("instances", out HavokLayoutMember member))
                return -1;

            int field = compound.FileOffset + member.Offset;
            int arrayFile = pf.ResolveArray(field);
            if (arrayFile < 0)
                return -1;

            return (int)HavokRes.ReadU32(data, field + pf.PointerSize);
        }
    }
}
