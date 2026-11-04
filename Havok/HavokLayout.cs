using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace LevelEditorPlugin.Havok
{
    // DataContractJsonSerializer 直接填这些字段，编译器看不到赋值
#pragma warning disable 0649

    [DataContract]
    internal sealed class LayoutFileDto
    {
        [DataMember(Name = "version")] public int Version;
        [DataMember(Name = "source")] public string Source;
        [DataMember(Name = "classes")] public LayoutClassDto[] Classes;
    }

    [DataContract]
    internal sealed class LayoutClassDto
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "parent")] public string Parent;
        [DataMember(Name = "size")] public int Size;
        [DataMember(Name = "members")] public LayoutMemberDto[] Members;
    }

    [DataContract]
    internal sealed class LayoutMemberDto
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "offset")] public int Offset;
        [DataMember(Name = "type")] public string Type;
        [DataMember(Name = "size")] public int Size;
        [DataMember(Name = "enum")] public string EnumName;
        [DataMember(Name = "isPointer")] public bool IsPointer;
        [DataMember(Name = "isArray")] public bool IsArray;
        [DataMember(Name = "isRelArray")] public bool IsRelArray;
        [DataMember(Name = "elementClass")] public string ElementClass;
        [DataMember(Name = "arraySize")] public int ArraySize;
    }

#pragma warning restore 0649

    public sealed class HavokLayoutMember
    {
        public string Name;
        public int Offset;
        public string TypeName;
        public int FixedSize;
        public string EnumName;
        public bool IsPointer;
        public bool IsArray;
        public bool IsRelArray;
        public string ElementClass;
        public int ArraySize;

        /// <summary>成员在 64 位 packfile 里占用的字节数（指针一律按 8 字节槽算，和实测一致）。</summary>
        public int SlotSize => IsArray ? 16 : 8;

        public override string ToString() => $"{Name}@{Offset:X} : {TypeName}";
    }

    public sealed class HavokLayoutClass
    {
        private readonly Dictionary<string, HavokLayoutMember> allMembers =
            new Dictionary<string, HavokLayoutMember>(StringComparer.Ordinal);

        public string Name;
        public string ParentName;
        public int Size;
        public HavokLayoutClass Parent;
        public List<HavokLayoutMember> OwnMembers = new List<HavokLayoutMember>();
        public List<HavokLayoutMember> AllMembers = new List<HavokLayoutMember>();

        public HavokLayoutMember Member(string name)
        {
            if (allMembers.TryGetValue(name, out HavokLayoutMember m))
                return m;
            throw new HkxParseException($"类 {Name} 没有成员 \"{name}\"（布局表 HavokLayout.bf1.json 可能不完整）");
        }

        public bool TryMember(string name, out HavokLayoutMember member)
            => allMembers.TryGetValue(name, out member);

        /// <summary>字段能延伸到的最远字节，用来做对象跨度断言。</summary>
        public int MinFieldExtent()
        {
            int extent = 0;
            foreach (HavokLayoutMember m in AllMembers)
                extent = Math.Max(extent, m.Offset + m.SlotSize);
            return extent;
        }

        /// <summary>按 packfile 的指针宽度算字段最远字节：32 位副本里数组/指针槽是 4 字节。</summary>
        public int MinFieldExtent(int pointerSize)
        {
            int slot = pointerSize == 8 ? 8 : 4;
            int extent = 0;
            foreach (HavokLayoutMember m in AllMembers)
                extent = Math.Max(extent, m.Offset + (m.IsArray ? 8 : m.IsPointer ? slot : m.FixedSize > 0 ? m.FixedSize : 8));
            return extent;
        }

        internal void Flatten(HavokLayout layout)
        {
            allMembers.Clear();
            AllMembers.Clear();

            if (!string.IsNullOrEmpty(ParentName) && layout.TryGetClass(ParentName, out HavokLayoutClass parent))
                Parent = parent;

            if (Parent != null)
            {
                foreach (HavokLayoutMember m in Parent.AllMembers)
                {
                    allMembers[m.Name] = m;
                    AllMembers.Add(m);
                }
            }

            foreach (HavokLayoutMember m in OwnMembers)
            {
                allMembers[m.Name] = m;
                if (!AllMembers.Contains(m))
                    AllMembers.Add(m);
            }
        }
    }

    /// <summary>
    /// BF1 的 hkClass 布局表（由 IDA 读取注册 stub 导出）。
    /// 用于对象树显示真实成员名与偏移、以及形状解码（按名字查偏移而不是散落的魔数）。
    /// </summary>
    public sealed class HavokLayout
    {
        private readonly Dictionary<string, HavokLayoutClass> classes =
            new Dictionary<string, HavokLayoutClass>(StringComparer.Ordinal);

        public string Source { get; private set; } = "";
        public IEnumerable<HavokLayoutClass> Classes => classes.Values;

        public static HavokLayout BuiltIn { get; } = LoadEmbedded();

        public bool TryGetClass(string name, out HavokLayoutClass layoutClass)
            => classes.TryGetValue(name, out layoutClass);

        public HavokLayoutClass GetClass(string name)
        {
            if (classes.TryGetValue(name, out HavokLayoutClass c))
                return c;
            throw new HkxParseException($"布局表里没有类 \"{name}\"");
        }

        private static HavokLayout LoadEmbedded()
        {
            Assembly asm = typeof(HavokLayout).Assembly;
            string name = null;
            foreach (string res in asm.GetManifestResourceNames())
            {
                if (res.EndsWith("HavokLayout.bf1.json", StringComparison.OrdinalIgnoreCase))
                {
                    name = res;
                    break;
                }
            }

            if (name == null)
                throw new HkxParseException("程序集里找不到内嵌资源 HavokLayout.bf1.json");

            using (Stream stream = asm.GetManifestResourceStream(name))
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                return FromJson(reader.ReadToEnd());
            }
        }

        public static HavokLayout FromJson(string json)
        {
            LayoutFileDto dto;
            try
            {
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(LayoutFileDto));
                using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    dto = (LayoutFileDto)ser.ReadObject(ms);
            }
            catch (Exception e)
            {
                throw new HkxParseException("HavokLayout.bf1.json 解析失败：" + e.Message);
            }

            HavokLayout layout = new HavokLayout { Source = dto.Source ?? "" };
            if (dto.Classes == null)
                throw new HkxParseException("HavokLayout.bf1.json 里没有 classes 数组");

            foreach (LayoutClassDto cd in dto.Classes)
            {
                HavokLayoutClass cls = new HavokLayoutClass
                {
                    Name = cd.Name,
                    ParentName = string.IsNullOrEmpty(cd.Parent) ? null : cd.Parent,
                    Size = cd.Size,
                };

                if (cd.Members != null)
                {
                    foreach (LayoutMemberDto md in cd.Members)
                    {
                        cls.OwnMembers.Add(new HavokLayoutMember
                        {
                            Name = md.Name,
                            Offset = md.Offset,
                            TypeName = md.Type ?? "",
                            FixedSize = md.Size,
                            EnumName = md.EnumName,
                            IsPointer = md.IsPointer,
                            IsArray = md.IsArray,
                            IsRelArray = md.IsRelArray,
                            ElementClass = md.ElementClass,
                            ArraySize = md.ArraySize,
                        });
                    }
                }

                layout.classes[cls.Name] = cls;
            }

            foreach (HavokLayoutClass cls in layout.classes.Values)
                cls.Flatten(layout);

            return layout;
        }
    }
}
