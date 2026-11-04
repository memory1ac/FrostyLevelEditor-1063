using System;
using System.Collections.Generic;
using System.Text;

namespace LevelEditorPlugin.Havok
{
    public sealed class HkxSectionInfo
    {
        public string Name;
        public int AbsoluteDataStart;
        public int LocalFixupsOffset;
        public int GlobalFixupsOffset;
        public int VirtualFixupsOffset;
        public int ExportsOffset;
        public int ImportsOffset;
        public int EndOffset;

        public override string ToString() => Name;
    }

    public sealed class HkxClassInfo
    {
        public uint Signature;
        public byte Version;
        public string Name;
        /// <summary>名字字符串在 __classnames__ 段内的偏移（virtual fixup 的 classOffset 就是这个值）。</summary>
        public int NameOffset;
    }

    /// <summary>
    /// packfile 里的一个对象。DataOffset 相对 __data__ 段起点，FileOffset 是资源文件里的绝对偏移。
    /// </summary>
    public sealed class HkxObject
    {
        public HkxPackFile PackFile;
        public int DataOffset;
        public int FileOffset;
        public HkxClassInfo Class;
        /// <summary>到下一个对象（或数据段末尾）的字节数，是对象实际占用的上界。</summary>
        public int Span;

        public string ClassName => Class?.Name ?? "<unknown>";

        public override string ToString() => $"{ClassName} @+0x{DataOffset:X}";
    }

    /// <summary>
    /// 单个 Havok packfile（32 位或 64 位）的解析结果：
    /// 头部、三个段落（__classnames__ / __types__ / __data__）、类名表、对象表、数组/对象 fixup。
    /// </summary>
    public sealed class HkxPackFile
    {
        private const uint Magic1 = 0x57E0E057;
        private const uint Magic2 = 0x10C0C010;
        private const int SectionEntrySize = 0x40;

        private readonly Dictionary<int, int> arrayFixups = new Dictionary<int, int>();
        private readonly Dictionary<int, int> objectFixups = new Dictionary<int, int>();
        private readonly Dictionary<int, HkxObject> objectsByDataOffset = new Dictionary<int, HkxObject>();
        private readonly Dictionary<int, string> classNames = new Dictionary<int, string>();

        public int PointerSize { get; private set; }
        public int StartFileOffset { get; private set; }
        public string Version { get; private set; } = "";
        public int DataStartFile { get; private set; }
        public int DataEndFile { get; private set; }
        public int ClassNamesStartFile { get; private set; }
        public int FixupBlockFile { get; private set; }
        public int ArrayFixupBytes { get; private set; }
        public int ObjectFixupBytes { get; private set; }

        public Dictionary<string, HkxSectionInfo> Sections { get; } = new Dictionary<string, HkxSectionInfo>();
        public List<HkxClassInfo> Classes { get; } = new List<HkxClassInfo>();
        public List<HkxObject> Objects { get; } = new List<HkxObject>();

        public int DataSize => DataEndFile - DataStartFile;

        /// <summary>
        /// 解析一个 packfile。返回时 <paramref name="endFileOffset"/> 指向该 packfile 的末尾（下一个 packfile 或 fixup 表）。
        /// </summary>
        public static HkxPackFile Parse(byte[] data, int startOffset, out int endFileOffset)
        {
            HkxPackFile pf = new HkxPackFile();
            pf.ParseInternal(data, startOffset);
            endFileOffset = pf.DataEndFile;
            return pf;
        }

        private void ParseInternal(byte[] data, int startOffset)
        {
            StartFileOffset = startOffset;
            HkxBuffer buf = new HkxBuffer(data, startOffset);

            uint magic1 = buf.U32("packfile magic1");
            if (magic1 != Magic1)
                throw new HkxParseException($"packfile magic 不匹配：偏移 0x{startOffset:X} 读到 0x{magic1:X8}，期望 0x{Magic1:X8}");

            uint magic2 = buf.U32("packfile magic2");
            if (magic2 != Magic2)
                throw new HkxParseException($"packfile 第二 magic 不匹配：偏移 0x{startOffset + 4:X} 读到 0x{magic2:X8}，期望 0x{Magic2:X8}");

            buf.Skip(8, "userTag/fileVersion");
            // 0x10 处是布局规则，低字节 = 指针宽度（4 或 8）
            byte layoutPtr = data[startOffset + 0x10];
            PointerSize = layoutPtr == 8 ? 8 : layoutPtr == 4 ? 4 : 0;
            if (PointerSize == 0)
            {
                int guess = data[startOffset + 0x14] == 3 && data[startOffset + 0x20] == 0 ? 8 : 4;
                PointerSize = guess;
            }

            buf.Position = startOffset + 0x28;
            Version = buf.Terminated(0xFF, 64, "contentsVersion");

            int sectionTable = FindSectionTable(data, startOffset);
            if (sectionTable < 0)
                throw new HkxParseException($"偏移 0x{startOffset:X} 的 packfile 里找不到 __classnames__ 段落表");

            for (int i = 0; ; i++)
            {
                int entry = sectionTable + i * SectionEntrySize;
                if (entry + 20 + 28 > data.Length)
                    break;

                HkxBuffer nameBuf = new HkxBuffer(data, entry);
                string name;
                try
                {
                    // 段落名是 20 字节，用 0xFF 终止（中间用 0 填充），所以要去掉尾部 NUL
                    name = nameBuf.Terminated(0xFF, 20, $"section[{i}] 名称").TrimEnd('\0');
                }
                catch (HkxParseException)
                {
                    break;
                }

                if (!name.StartsWith("__", StringComparison.Ordinal))
                    break;

                HkxBuffer f = new HkxBuffer(data, entry + 20);
                HkxSectionInfo info = new HkxSectionInfo
                {
                    Name = name,
                    AbsoluteDataStart = f.I32("section absoluteDataStart"),
                    LocalFixupsOffset = f.I32("section localFixupsOffset"),
                    GlobalFixupsOffset = f.I32("section globalFixupsOffset"),
                    VirtualFixupsOffset = f.I32("section virtualFixupsOffset"),
                    ExportsOffset = f.I32("section exportsOffset"),
                    ImportsOffset = f.I32("section importsOffset"),
                    EndOffset = f.I32("section endOffset"),
                };
                Sections[name.Trim('_')] = info;
            }

            if (!Sections.TryGetValue("classnames", out HkxSectionInfo classnames))
                throw new HkxParseException($"packfile @0x{startOffset:X} 缺 __classnames__ 段");
            if (!Sections.TryGetValue("data", out HkxSectionInfo dataSection))
                throw new HkxParseException($"packfile @0x{startOffset:X} 缺 __data__ 段");

            ClassNamesStartFile = startOffset + classnames.AbsoluteDataStart;
            DataStartFile = startOffset + dataSection.AbsoluteDataStart;
            DataEndFile = DataStartFile + dataSection.EndOffset;
            ArrayFixupBytes = dataSection.GlobalFixupsOffset;
            ObjectFixupBytes = dataSection.LocalFixupsOffset - dataSection.GlobalFixupsOffset;

            ParseClassNames(data);
            ParseObjects(data, dataSection);
        }

        private void ParseClassNames(byte[] data)
        {
            int boundary = DataStartFile > ClassNamesStartFile ? DataStartFile : data.Length;
            int p = ClassNamesStartFile;
            while (p + 5 < boundary && data[p] != 0xFF)
            {
                HkxBuffer buf = new HkxBuffer(data, p);
                HkxClassInfo info = new HkxClassInfo
                {
                    Signature = buf.U32("class signature"),
                    Version = buf.U8("class version"),
                };
                info.NameOffset = buf.Position - ClassNamesStartFile;
                int nameStart = buf.Position;
                int q = nameStart;
                while (q < boundary && data[q] != 0)
                    q++;
                if (q >= boundary)
                    break;
                info.Name = Encoding.ASCII.GetString(data, nameStart, q - nameStart);
                p = q + 1;

                Classes.Add(info);
                if (!classNames.ContainsKey(info.NameOffset))
                    classNames.Add(info.NameOffset, info.Name);
            }
        }

        private void ParseObjects(byte[] data, HkxSectionInfo dataSection)
        {
            int vfix = DataStartFile + dataSection.VirtualFixupsOffset;
            int end = DataEndFile;

            List<KeyValuePair<int, string>> found = new List<KeyValuePair<int, string>>();
            int p = vfix;
            while (p + 12 <= end)
            {
                uint src = ReadU32(data, p);
                uint section = ReadU32(data, p + 4);
                uint classOffset = ReadU32(data, p + 8);
                if (classOffset == 0xFFFFFFFF || src == 0xFFFFFFFF)
                    break;

                if (!classNames.TryGetValue((int)classOffset, out string className))
                    throw new HkxParseException(
                        $"packfile @0x{StartFileOffset:X} 的对象表在 +0x{p - DataStartFile:X} 引用了未知类偏移 0x{classOffset:X}（对象 src=+0x{src:X}，section={section}）");

                found.Add(new KeyValuePair<int, string>((int)src, className));
                p += 12;
            }

            found.Sort((a, b) => a.Key.CompareTo(b.Key));

            HashSet<int> seen = new HashSet<int>();
            for (int i = 0; i < found.Count; i++)
            {
                int off = found[i].Key;
                if (!seen.Add(off))
                    throw new HkxParseException($"packfile @0x{StartFileOffset:X} 对象表里有重复偏移 +0x{off:X}");

                HkxObject obj = new HkxObject
                {
                    PackFile = this,
                    DataOffset = off,
                    FileOffset = DataStartFile + off,
                    Class = Classes.Find(c => c.Name == found[i].Value),
                };

                // 最后一个对象一直延伸到虚拟 fixup 表（对象表）之前
                int next = i + 1 < found.Count ? found[i + 1].Key : dataSection.VirtualFixupsOffset;
                obj.Span = next - off;
                if (obj.Span <= 0)
                    throw new HkxParseException(
                        $"packfile @0x{StartFileOffset:X} 对象 {found[i].Value} 偏移 +0x{off:X} 之后的对象偏移 +0x{next:X} 不递增");

                Objects.Add(obj);
                objectsByDataOffset[off] = obj;
            }
        }

        /// <summary>
        /// 读取 packfile 末尾之后的两张 fixup 表。
        /// 表 1（数组）：8 字节 (srcOffset, dstOffset)，都相对 __data__ 起点；
        /// 表 2（对象）：12 字节 (srcOffset, sectionIndex, dstOffset)。
        /// </summary>
        public void ReadFixupTables(byte[] data, int blockFileOffset)
        {
            FixupBlockFile = blockFileOffset;
            arrayFixups.Clear();
            objectFixups.Clear();

            if (ArrayFixupBytes < 0 || ObjectFixupBytes < 0 || ArrayFixupBytes + ObjectFixupBytes <= 0)
                throw new HkxParseException($"packfile @0x{StartFileOffset:X} 的 fixup 段大小异常（数组 0x{ArrayFixupBytes:X}，对象 0x{ObjectFixupBytes:X}）");

            // 两张表都以 0xFFFFFFFF 哨兵结束，表尾可能还有对齐填充（FenceMetal 的 res 就多出 8 字节 0xFF），
            // 所以不能要求段大小正好是条目大小的整数倍，也不能把填充当条目读。
            for (int i = 0; i + 8 <= ArrayFixupBytes; i += 8)
            {
                int pos = blockFileOffset + i;
                if (pos + 8 > data.Length)
                    throw new HkxParseException($"数组 fixup 表越界：偏移 0x{pos:X}");

                uint src = ReadU32(data, pos);
                uint dst = ReadU32(data, pos + 4);
                if (src == 0xFFFFFFFF)
                    break;

                arrayFixups[DataStartFile + (int)src] = DataStartFile + (int)dst;
            }

            int objBase = blockFileOffset + ArrayFixupBytes;
            for (int i = 0; i + 12 <= ObjectFixupBytes; i += 12)
            {
                int pos = objBase + i;
                if (pos + 12 > data.Length)
                    throw new HkxParseException($"对象 fixup 表越界：偏移 0x{pos:X}");

                uint src = ReadU32(data, pos);
                uint dst = ReadU32(data, pos + 8);
                if (src == 0xFFFFFFFF)
                    break;

                objectFixups[DataStartFile + (int)src] = (int)dst;
            }
        }

        /// <summary>
        /// fixup 表可信度：对象 fixup 是否都落在已知对象上、数组 fixup 是否都落在数据段内。
        /// 用来判断两块外部 fixup 表到底属于哪个 packfile（res 里两块大小可能相同）。
        /// </summary>
        public int ScoreFixups()
        {
            int score = 0;
            foreach (KeyValuePair<int, int> kv in arrayFixups)
            {
                if (kv.Value >= DataStartFile && kv.Value < DataEndFile)
                    score++;
            }

            foreach (KeyValuePair<int, int> kv in objectFixups)
            {
                if (objectsByDataOffset.ContainsKey(kv.Value))
                    score += 3;
            }

            return score;
        }

        /// <summary>把"字段所在文件偏移"解析成"数组数据所在文件偏移"，没有 fixup 返回 -1。</summary>
        public int ResolveArray(int fieldFileOffset)
            => arrayFixups.TryGetValue(fieldFileOffset, out int v) ? v : -1;

        /// <summary>把"指针字段所在文件偏移"解析成对象，没有 fixup 返回 null。</summary>
        public HkxObject ResolveObject(int fieldFileOffset)
        {
            if (!objectFixups.TryGetValue(fieldFileOffset, out int dataOffset))
                return null;
            return objectsByDataOffset.TryGetValue(dataOffset, out HkxObject obj) ? obj : null;
        }

        public HkxObject ObjectAtDataOffset(int dataOffset)
            => objectsByDataOffset.TryGetValue(dataOffset, out HkxObject obj) ? obj : null;

        /// <summary>对象跨度内是否包含给定文件偏移（用于断言 rel array 没有跑到别人身上）。</summary>
        public bool ContainsFileOffset(HkxObject obj, int fileOffset)
            => fileOffset >= obj.FileOffset && fileOffset < obj.FileOffset + obj.Span;

        public string Describe()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"packfile {PointerSize} 位 @0x{StartFileOffset:X} 版本 \"{Version}\"");
            sb.AppendLine($"  数据段 0x{DataStartFile:X}..0x{DataEndFile:X}（{DataSize} 字节），对象 {Objects.Count} 个，类 {Classes.Count} 个");
            foreach (HkxSectionInfo s in Sections.Values)
            {
                sb.AppendLine($"  段 {s.Name,-14} start=0x{s.AbsoluteDataStart:X} localFix=0x{s.LocalFixupsOffset:X} globalFix=0x{s.GlobalFixupsOffset:X} vfix=0x{s.VirtualFixupsOffset:X} end=0x{s.EndOffset:X}");
            }
            sb.AppendLine($"  数组 fixup {ArrayFixupBytes / 8} 条，对象 fixup {ObjectFixupBytes / 12} 条");
            return sb.ToString();
        }

        /// <summary>
        /// 结构自检：对象顺序、跨度、rel array 是否落在对象跨度内、fixup 目标是否有效。
        /// 返回问题列表（空 = 通过）。
        /// </summary>
        public List<string> Validate(HavokLayout layout)
        {
            List<string> issues = new List<string>();
            for (int i = 0; i < Objects.Count; i++)
            {
                HkxObject obj = Objects[i];
                if (obj.FileOffset < DataStartFile || obj.FileOffset + obj.Span > DataEndFile)
                    issues.Add($"对象 {obj.ClassName} @+0x{obj.DataOffset:X} 跨度 0x{obj.Span:X} 超出数据段");

                if (layout != null && layout.TryGetClass(obj.ClassName, out HavokLayoutClass cls))
                {
                    int minSize = cls.MinFieldExtent(PointerSize);
                    if (minSize > obj.Span)
                        issues.Add($"对象 {obj.ClassName} @+0x{obj.DataOffset:X} 的布局需要 {minSize} 字节，但跨度只有 {obj.Span}");
                }
            }

            foreach (KeyValuePair<int, int> kv in arrayFixups)
            {
                if (kv.Value < DataStartFile || kv.Value >= DataEndFile)
                    issues.Add($"数组 fixup 目标 0x{kv.Value:X}（来自字段 0x{kv.Key:X}）落在数据段之外");
            }

            foreach (KeyValuePair<int, int> kv in objectFixups)
            {
                if (!objectsByDataOffset.ContainsKey(kv.Value))
                    issues.Add($"对象 fixup 0x{kv.Key:X} 指向 +0x{kv.Value:X}，那里没有对象");
            }

            return issues;
        }

        private static int FindSectionTable(byte[] data, int startOffset)
        {
            int from = startOffset + 0x30;
            int to = Math.Min(data.Length - 16, startOffset + 0x400);
            for (int p = from; p <= to; p++)
            {
                if (data[p] != (byte)'_' || data[p + 1] != (byte)'_')
                    continue;
                if (MatchesAscii(data, p, "__classnames__"))
                    return p;
            }
            return -1;
        }

        private static bool MatchesAscii(byte[] data, int pos, string text)
        {
            if (pos + text.Length > data.Length)
                return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (data[pos + i] != (byte)text[i])
                    return false;
            }
            return true;
        }

        private static uint ReadU32(byte[] data, int offset)
            => (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
    }
}
