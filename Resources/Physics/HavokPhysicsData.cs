using Frosty.Core;
using Frosty.Core.IO;
using Frosty.Core.Mod;
using FrostySdk;
using FrostySdk.IO;
using FrostySdk.Managers;
using FrostySdk.Resources;
using LevelEditorPlugin.Resources.Hkx;
using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LevelEditorPlugin.Resources
{
    namespace Hkx
    {
        public class hkBaseClass
        {
            public long Offset;

            public virtual void Serialize(NativeReader reader, HkxHeader header)
            {
            }
        }

        public class hkpStaticCompoundShape : hkBaseClass
        {
            public class hkpInstance
            {
                public Vector3 Translation { get; set; }
                public Vector4 Rotation { get; set; }
                public Vector3 Scale { get; set; }
                public uint FilterInfo { get; set; }
                public uint ChildFilterInfoMask { get; set; }
                public uint UserData { get; set; }

                public long DataOffset { get; set; }

                public void Serialize(NativeReader reader)
                {
                    DataOffset = reader.Position;
                    Translation = new Vector3(
                        reader.ReadFloat(),
                        reader.ReadFloat(),
                        reader.ReadFloat()
                    );
                    reader.ReadFloat();

                    Rotation = new Vector4(
                        reader.ReadFloat(),
                        reader.ReadFloat(),
                        reader.ReadFloat(),
                        reader.ReadFloat()
                    );

                    Scale = new Vector3(
                        reader.ReadFloat(),
                        reader.ReadFloat(),
                        reader.ReadFloat()
                    );
                    reader.ReadFloat();

                    reader.ReadUInt();
                    FilterInfo = reader.ReadUInt();
                    ChildFilterInfoMask = reader.ReadUInt();
                    UserData = reader.ReadUInt();
                }
            }

            public class hkpTreeNode
            {
                public List<int> XYZ { get; set; }
                public int HiData { get; set; }
                public int LoData { get; set; }
            }

            public class hkpDomain
            {
                public Vector4 Min { get; set; }
                public Vector4 Max { get; set; }

                public void Serialize(NativeReader reader)
                {
                    Min = new Vector4(
                        reader.ReadFloat(),
                        reader.ReadFloat(),
                        reader.ReadFloat(),
                        reader.ReadFloat()
                        );
                    Max = new Vector4(
                        reader.ReadFloat(),
                        reader.ReadFloat(),
                        reader.ReadFloat(),
                        reader.ReadFloat()
                        );
                }
            }

            public class hkpTree
            {
                public List<hkpTreeNode> Nodes { get; set; }
                public hkpDomain Domain { get; set; }

                public void Serialize(NativeReader reader)
                {
                    Nodes = [];
                    Domain = new hkpDomain();

                    int NumTreeNodes = reader.ReadInt();
                    reader.BaseStream.Seek(8, SeekOrigin.Current);

                    Domain.Serialize(reader);
                }
            }

            public byte DispatchType { get; set; }
            public byte BitsPerKey { get; set; }
            public byte ShapeInfoCodecType { get; set; }
            public uint UserData { get; set; }
            public byte BvTreeType { get; set; }
            public byte NumBitsForChildShapeKey { get; set; }
            public List<hkpInstance> Instances { get; set; }
            public hkpTree Tree { get; set; }

            public hkpStaticCompoundShape()
            {
            }

            public override void Serialize(NativeReader reader, HkxHeader header)
            {
                reader.BaseStream.Seek(9, SeekOrigin.Current);
                DispatchType = reader.ReadByte();
                BitsPerKey = reader.ReadByte();
                ShapeInfoCodecType = reader.ReadByte();
                UserData = reader.ReadUInt();
                BvTreeType = reader.ReadByte();
                reader.BaseStream.Seek(7, SeekOrigin.Current);
                NumBitsForChildShapeKey = reader.ReadByte();
                reader.BaseStream.Seek(11, SeekOrigin.Current);
                int NumInstances = reader.ReadInt();
                reader.BaseStream.Seek(28, SeekOrigin.Current);

                Tree = new hkpTree();
                Tree.Serialize(reader);

                reader.BaseStream.Seek(16, SeekOrigin.Current);
                Instances = [];

                for (int i = 0; i < NumInstances; i++)
                {
                    hkpInstance CurInstance = new hkpInstance();
                    CurInstance.Serialize(reader);

                    Instances.Add(CurInstance);
                }
            }
        }

        public class hknpShape : hkBaseClass
        {
            public int unknown;
            public hkRefCountedProperties Properties;

            public override void Serialize(NativeReader reader, HkxHeader header)
            {
                reader.Position += 0x11;
                unknown = reader.ReadByte();
                reader.Position += 0x0E;
                Properties = header.ReadObject(reader) as hkRefCountedProperties;
            }
        }

        public class hknpCapsuleShape : hknpShape
        {
        }
        public class hknpConvexPolytopeShape : hknpShape
        {
            public List<Vector4> Vertices = [];
            public List<Vector4> Planes = [];
            public List<byte[]> Faces = [];
            public List<byte> Indices = [];

            public override void Serialize(NativeReader reader, HkxHeader header)
            {
                base.Serialize(reader, header);
                reader.Position += 0x08;

                int numVertices = header.BeginRelArrayRead(reader);
                for (int i = 0; i < numVertices; i++)
                {
                    Vertices.Add(new Vector4(
                        reader.ReadFloat(),
                        reader.ReadFloat(),
                        reader.ReadFloat(),
                        reader.ReadFloat()
                        ));
                }
                header.EndRelArrayRead(reader);

                reader.Position += 0x0c;

                int numPlanes = header.BeginRelArrayRead(reader);
                for (int i = 0; i < numPlanes; i++)
                {
                    Planes.Add(new Vector4(
                        reader.ReadFloat(),
                        reader.ReadFloat(),
                        reader.ReadFloat(),
                        reader.ReadFloat()
                        ));
                }
                header.EndRelArrayRead(reader);

                int numFaces = header.BeginRelArrayRead(reader);
                for (int i = 0; i < numFaces; i++)
                {
                    Faces.Add(new byte[]
                    {
                        reader.ReadByte(),
                        reader.ReadByte(),
                        reader.ReadByte(),
                        reader.ReadByte()
                    });
                }
                header.EndRelArrayRead(reader);

                int numIndices = header.BeginRelArrayRead(reader);
                for (int i = 0; i < numIndices; i++)
                {
                    Indices.Add(reader.ReadByte());
                }
                header.EndRelArrayRead(reader);
            }
        }
        public class hknpSphereShape : hknpShape
        {
        }
        public class hknpCompressedMeshShapeData : hkBaseClass
        {
        }
        public class hknpCompressedMeshShape : hknpShape
        {
        }

        public class hknpStaticCompoundShape : hknpShape
        {
            public class hknpInstance
            {
                public Matrix Transform;
                public Matrix Rotation;
                public Vector3 Translation;
                public Vector3 Scale;
                public float Test;
                public hkBaseClass shape;
                //public ushort shapeTag;
                //public ushort destructionTag;
                public uint[] unknowns = new uint[10];
                public long DataOffset { get; set; }

                public uint tmp1;
                public uint tmp2;
                public uint tmp3;
                public uint tmp4;
                public uint tmp5;

                public void Serialize(NativeReader reader, HkxHeader header)
                {
                    DataOffset = reader.Position;

                    Matrix m = new Matrix { M11 = reader.ReadFloat(), M12 = reader.ReadFloat(), M13 = reader.ReadFloat() };
                    tmp1 = reader.ReadUInt();
                    m.M21 = reader.ReadFloat(); m.M22 = reader.ReadFloat(); m.M23 = reader.ReadFloat(); tmp2 = reader.ReadUInt();
                    m.M31 = reader.ReadFloat(); m.M32 = reader.ReadFloat(); m.M33 = reader.ReadFloat(); tmp3 = reader.ReadUInt();
                    m.M41 = reader.ReadFloat(); m.M42 = reader.ReadFloat(); m.M43 = reader.ReadFloat(); tmp4 = reader.ReadUInt();
                    Transform = m;
                    Rotation = m;//Quaternion.RotationMatrix(m);

                    //Translation = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
                    //tmp4 = reader.ReadFloat();

                    Scale = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
                    tmp5 = reader.ReadUInt();

                    shape = header.ReadObject(reader);
                    //shapeTag = reader.ReadUShort();
                    //destructionTag = reader.ReadUShort();

                    for (int i = 0; i < 10; i++)
                        unknowns[i] = reader.ReadUInt();
                }
            }

            public List<hknpInstance> Instances = [];
            public Vector4 Unknown1;
            public Vector4 Unknown2;
            public hknpStaticCompoundShapeData ShapeData;

            public hknpStaticCompoundShape()
            {
            }

            public override void Serialize(NativeReader reader, HkxHeader header)
            {
                base.Serialize(reader, header);

                reader.Position += 0x38;
                long arrayOffset = header.ReadArray(reader);
                int numInstances = reader.ReadInt();

                reader.Position += 0x14;
                Unknown1 = new Vector4(
                    reader.ReadFloat(),
                    reader.ReadFloat(),
                    reader.ReadFloat(),
                    reader.ReadFloat()
                    );
                Unknown2 = new Vector4(
                    reader.ReadFloat(),
                    reader.ReadFloat(),
                    reader.ReadFloat(),
                    reader.ReadFloat()
                    );

                reader.Position += 0x20;
                ShapeData = header.ReadObject(reader) as hknpStaticCompoundShapeData;

                reader.Position = arrayOffset;
                for (int i = 0; i < numInstances; i++)
                {
                    hknpInstance inst = new hknpInstance();
                    inst.Serialize(reader, header);

                    Instances.Add(inst);
                }
            }
        }

        public class hknpStaticCompoundShapeData : hkBaseClass
        {
            public class TreeNode
            {
                public ushort Index;
                public byte X;
                public byte Y;
                public byte Z;

                public bool HasChildren;
                public List<TreeNode> Children = [];

                public TreeNode(byte[] data)
                {
                    X = data[0];
                    Y = data[1];
                    Z = data[2];
                    HasChildren = data[3] != 0;
                    Index = BitConverter.ToUInt16(data, 4);
                }
            }

            public Vector4 Unknown1;
            public Vector4 Unknown2;
            public List<byte[]> Unknowns = [];
            public long ArrayOffset;
            public TreeNode RootNode;

            public override void Serialize(NativeReader reader, HkxHeader header)
            {
                base.Serialize(reader, header);
                reader.Position += 0x10;

                ArrayOffset = header.ReadArray(reader);
                int numItems = reader.ReadInt();
                reader.Position += 4;

                Unknown1 = new Vector4(
                    reader.ReadFloat(),
                    reader.ReadFloat(),
                    reader.ReadFloat(),
                    reader.ReadFloat()
                    );
                Unknown2 = new Vector4(
                    reader.ReadFloat(),
                    reader.ReadFloat(),
                    reader.ReadFloat(),
                    reader.ReadFloat()
                    );

                reader.Position = ArrayOffset;
                for (int i = 0; i < numItems; i++)
                {
                    Unknowns.Add(reader.ReadBytes(6));
                }
            }
        }

        public class hkRefCountedProperties : hkBaseClass
        {
            public List<hkBaseClass> Properties = [];

            public override void Serialize(NativeReader reader, HkxHeader header)
            {
                base.Serialize(reader, header);

                long arrayOffset = header.ReadArray(reader);
                int numItems = reader.ReadInt();

                reader.Position = arrayOffset;
                for (int i = 0; i < numItems; i++)
                {
                    Properties.Add(header.ReadObject(reader));
                }
            }
        }

        public class hknpShapeMassProperties : hkBaseClass
        {
            public Vector4 Unknown1;
            public Vector4 Unknown2;

            public override void Serialize(NativeReader reader, HkxHeader header)
            {
                base.Serialize(reader, header);
                reader.Position += 0x10;

                Unknown1 = new Vector4(
                    reader.ReadFloat(),
                    reader.ReadFloat(),
                    reader.ReadFloat(),
                    reader.ReadFloat()
                    );
                Unknown2 = new Vector4(
                    reader.ReadFloat(),
                    reader.ReadFloat(),
                    reader.ReadFloat(),
                    reader.ReadFloat()
                    );
            }
        }

        public class HavokPhysicsContainer : hkBaseClass
        {
            public List<hkBaseClass> Objects = [];

            public override void Serialize(NativeReader reader, HkxHeader header)
            {
                base.Serialize(reader, header);
                reader.Position += 0x10;

                long arrayOffset = header.ReadArray(reader);
                int numItems = reader.ReadInt();

                reader.Position = arrayOffset;
                for (int i = 0; i < numItems; i++)
                {
                    Objects.Add(header.ReadObject(reader));
                }
            }
        }

        //public class hkRootLevelContainer : hkBaseClass
        //{
        //}
    }

    public static partial class ReaderUtils
    {

    }

    public class HkxHeader
    {
        public uint StartOffset;
        public uint Magic1;
        public uint Magic2;
        public uint UserTag;
        public uint FileVersion;
        public uint LayoutRules;
        public uint NumSections;
        public uint ContentsSectionIndex;
        public uint ContentsSectionOffset;
        public uint ContentsClassNameSectionIndex;
        public uint ContentsClassNameSectionOffset;
        public String ContentsVersion;
        public uint Flags;
        public uint Padding;
        public int Bits;

        public Dictionary<long, long> ArrayOffsets = [];
        public Dictionary<long, Hkx.hkBaseClass> ObjectOffsets = [];

        private List<long> stackOffsets = [];

        public bool IsValid() { return (Magic1 == 0x57e0e057 && Magic2 == 0x10c0c010); }

        public void Serialize(NativeReader reader, int bits)
        {
            Bits = bits;
            Magic1 = reader.ReadUInt();
            if (Magic1 != 0x57e0e057)
            {
                do
                {
                    reader.BaseStream.Seek(-3, SeekOrigin.Current);
                    Magic1 = reader.ReadUInt();
                }
                while (Magic1 != 0x57e0e057);
            }

            StartOffset = (uint)reader.BaseStream.Position - 4;
            Magic2 = reader.ReadUInt();
            if (Magic2 == 0x10c0c010)
            {
                UserTag = reader.ReadUInt();
                FileVersion = reader.ReadUInt();
                LayoutRules = reader.ReadUInt();
                NumSections = reader.ReadUInt();
                ContentsSectionIndex = reader.ReadUInt();
                ContentsSectionOffset = reader.ReadUInt();
                ContentsClassNameSectionIndex = reader.ReadUInt();
                ContentsClassNameSectionOffset = reader.ReadUInt();
                ContentsVersion = reader.ReadTerminatedString(0xff);
                Flags = reader.ReadUInt();
                Padding = reader.ReadUInt();
            }
        }

        public Hkx.hkBaseClass ReadObject(NativeReader reader)
        {
            reader.ReadInt();
            if (Bits == 64)
                reader.ReadInt();

            if (!ObjectOffsets.ContainsKey(reader.Position - (Bits == 64 ? 8 : 4)))
                return null;
            return ObjectOffsets[reader.Position - (Bits == 64 ? 8 : 4)];
        }

        public long ReadArray(NativeReader reader)
        {
            long arrayOffset = ArrayOffsets[reader.Position];
            reader.ReadInt();
            if (Bits == 64)
                reader.ReadInt();

            return arrayOffset;
        }

        public int BeginRelArrayRead(NativeReader reader)
        {
            ushort numEntries = reader.ReadUShort();
            ushort offset = reader.ReadUShort();

            stackOffsets.Add(reader.Position);
            reader.Position += (offset - 4);

            return numEntries;
        }

        public void EndRelArrayRead(NativeReader reader)
        {
            reader.Position = stackOffsets[stackOffsets.Count - 1];
            stackOffsets.RemoveAt(stackOffsets.Count - 1);
        }
    }

    class HkxInstance
    {
        public class HkxClassDescriptor
        {
            public uint Signature;
            public byte Version;
            public String Name;

            public HkxClassDescriptor()
            {
            }

            public void Serialize(NativeReader reader)
            {
                Signature = reader.ReadUInt();
                Version = reader.ReadByte();
                Name = reader.ReadNullTerminatedString();
            }
        }

        public HkxHeader Header { get; set; }
        public List<Hkx.hkBaseClass> Objects => objList.Values.ToList();

        public int LocalFixupSize;
        public int GlobalFixupSize;
        public int AbsoluteDataStart;

        int virtualFixupsOffset;
        public int endOffset;

        Dictionary<ulong, HkxClassDescriptor> Classes = [];
        Dictionary<long, Hkx.hkBaseClass> objList = [];

        public void Serialize(NativeReader reader, int bits)
        {
            Header = new HkxHeader();
            Header.Serialize(reader, bits);

            int ClassSectionOffset = 0;
            int ClassSectionSize = 0;
            int TypesSectionOffset = 0;
            int exportsOffset = 0;
            int importsOffset = 0;
            int CurrentSection = 0;

            while (CurrentSection < Header.NumSections)
            {
                String BlockType = reader.ReadTerminatedString(0xff);
                if (BlockType.Contains("__classnames__"))
                {
                    ClassSectionOffset = reader.ReadInt();
                    reader.ReadLong();
                    ClassSectionSize = reader.ReadInt();
                    reader.BaseStream.Seek(12, SeekOrigin.Current);
                    CurrentSection++;
                }
                else if (BlockType.Contains("__types__"))
                {
                    TypesSectionOffset = reader.ReadInt();
                    reader.BaseStream.Seek(24, SeekOrigin.Current);
                    CurrentSection++;
                }
                else if (BlockType.Contains("__data__"))
                {
                    AbsoluteDataStart = reader.ReadInt();
                    LocalFixupSize = reader.ReadInt();
                    GlobalFixupSize = reader.ReadInt();
                    virtualFixupsOffset = reader.ReadInt();
                    exportsOffset = reader.ReadInt();
                    importsOffset = reader.ReadInt();
                    endOffset = reader.ReadInt();
                    CurrentSection++;
                }
            }

            reader.Position = Header.StartOffset + ClassSectionOffset;
            while (reader.BaseStream.Position < (Header.StartOffset + ClassSectionOffset + ClassSectionSize))
            {
                ulong ClassOffset = (ulong)(reader.BaseStream.Position - (Header.StartOffset + ClassSectionOffset));
                HkxClassDescriptor Class = new HkxClassDescriptor();
                Class.Serialize(reader);

                Classes.Add(ClassOffset + 5, Class);
            }

            reader.Position = Header.StartOffset + AbsoluteDataStart + virtualFixupsOffset;

            int TotalSize = (int)Header.StartOffset + AbsoluteDataStart + endOffset;

            while (reader.BaseStream.Position < TotalSize)
            {
                long DataOffset = reader.ReadLong();
                int ClassType = reader.ReadInt();

                if (ClassType != -1 && reader.BaseStream.Position < TotalSize)
                {
                    HkxClassDescriptor CurrentClassDescriptor = Classes[(ulong)ClassType];
                    Type type = Type.GetType("LevelEditorPlugin.Resources.Hkx." + CurrentClassDescriptor.Name);

                    if (type != null)
                    {
                        Hkx.hkBaseClass BaseClass = (Hkx.hkBaseClass)Activator.CreateInstance(type);
                        BaseClass.Offset = Header.StartOffset + AbsoluteDataStart + DataOffset;
                        objList.Add(BaseClass.Offset, BaseClass);
                    }
                }
            }

            reader.Position = Header.StartOffset + AbsoluteDataStart + endOffset;
        }

        public void ReadObjects(NativeReader reader)
        {
            foreach (long offset in objList.Keys)
            {
                Hkx.hkBaseClass value = objList[offset];
                if (value != null)
                {
                    reader.Position = offset;
                    value.Serialize(reader, Header);
                }
            }
        }

        public Hkx.hkBaseClass GetObject(long offset)
        {
            if (!objList.ContainsKey(offset))
                return null;
            return objList[offset];
        }
    }

    public class HavokPhysicsData : Resource
    {
        /// <summary>
        /// 待写回 ModifyRes 的 HavokPhysicsData 集合（修改后不立即写回，由用户点击按钮触发）
        /// </summary>
        private static readonly HashSet<HavokPhysicsData> s_pendingCommitSet = [];

        /// <summary>当有待写回修改加入时触发，用于刷新 MeshView 的写回按钮状态。
        /// </summary>
        public static event Action PendingCommitChanged;

        private static void RegisterPendingCommit(HavokPhysicsData data)
        {
            lock (s_pendingCommitSet)
            {
                s_pendingCommitSet.Add(data);
            }
            PendingCommitChanged?.Invoke();
        }

        /// <summary>
        /// 将所有待写回的 Havok transform 修改通过 ModifyRes 写回。
        /// </summary>
        public static int CommitAllPendingToRes()
        {
            HavokPhysicsData[] toCommit;
            lock (s_pendingCommitSet)
            {
                toCommit = [.. s_pendingCommitSet];
                s_pendingCommitSet.Clear();
            }
            PendingCommitChanged?.Invoke();
            int count = 0;
            foreach (var data in toCommit)
            {
                if (data.HasModifications)
                {
                    data.CommitToRes();
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 是否存在待写回的 Havok 修改。
        /// </summary>
        public static bool HasAnyPendingCommit
        {
            get
            {
                lock (s_pendingCommitSet)
                {
                    return s_pendingCommitSet.Count > 0;
                }
            }
        }

        public class HavokPhysicsDataModifiedResource : BaseModifiedResource
        {
            private Dictionary<int, Matrix> modifiedTransforms = [];

            /// <summary>
            /// 无参构造函数，供 ModifiedResource.Read 从 Mod 反序列化时使用。
            /// </summary>
            public HavokPhysicsDataModifiedResource()
                : base()
            {
            }

            public HavokPhysicsDataModifiedResource(ResAssetEntry inEntry)
                : base(inEntry)
            {
            }

            public bool GetTransform(int index, out Matrix outTransform)
            {
                outTransform = Matrix.Identity;
                if (!modifiedTransforms.ContainsKey(index))
                    return false;

                outTransform = modifiedTransforms[index];
                return true;
            }

            public void SetTransform(int index, Matrix transform)
            {
                if (!modifiedTransforms.ContainsKey(index))
                    modifiedTransforms.Add(index, Matrix.Identity);
                modifiedTransforms[index] = transform;
            }

            public void ClearTransform(int index)
            {
                if (!modifiedTransforms.ContainsKey(index))
                    return;
                modifiedTransforms.Remove(index);
            }

            public bool HasModifications => modifiedTransforms.Count > 0;

            public override void SaveInternal(NativeWriter writer)
            {
                writer.Write(modifiedTransforms.Count);
                foreach (KeyValuePair<int, Matrix> kvp in modifiedTransforms)
                {
                    writer.Write(kvp.Key);

                    writer.Write(kvp.Value.M11);
                    writer.Write(kvp.Value.M12);
                    writer.Write(kvp.Value.M13);
                    writer.Write(kvp.Value.M14);

                    writer.Write(kvp.Value.M21);
                    writer.Write(kvp.Value.M22);
                    writer.Write(kvp.Value.M23);
                    writer.Write(kvp.Value.M24);

                    writer.Write(kvp.Value.M31);
                    writer.Write(kvp.Value.M32);
                    writer.Write(kvp.Value.M33);
                    writer.Write(kvp.Value.M34);

                    writer.Write(kvp.Value.M41);
                    writer.Write(kvp.Value.M42);
                    writer.Write(kvp.Value.M43);
                    writer.Write(kvp.Value.M44);
                }
            }

            public override void ReadInternal(NativeReader reader)
            {
                int count = reader.ReadInt();
                for (int i = 0; i < count; i++)
                {
                    int key = reader.ReadInt();
                    Matrix value = new Matrix(
                        reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(),
                        reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(),
                        reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(),
                        reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()
                        );

                    modifiedTransforms.Add(key, value);
                }
            }
        }

        Hkx.hknpStaticCompoundShape RootShape => (inst64.Objects[0] as Hkx.HavokPhysicsContainer).Objects[0] as Hkx.hknpStaticCompoundShape;

        private int partCount;
        private List<Vec3> partTranslations = [];
        private List<AxisAlignedBox> localAabbs = [];
        private List<byte> materialIndices = [];
        private List<uint> materialFlagsAndIndices = [];
        private List<ushort> unknownIndices = [];
        private byte materialCountUsed;
        private byte highestMaterialIndex;

        private HkxInstance inst32;
        private HkxInstance inst64;

        private byte[] resData;
        private ResAssetEntry resEntry;

        private HavokPhysicsDataModifiedResource modifiedData;

        /// <summary>
        /// 是否有未写回的 transform 修改。
        /// </summary>
        public bool HasModifications => modifiedData?.HasModifications ?? false;

        /// <summary>
        /// 将内存中的修改通过 ModifyRes 写回资源。
        /// 调用 SaveBytes() 生成包含修改后 transform 的完整二进制数据，并写入 entry.ModifiedEntry.Data，
        /// 这样导出的 mod 会包含实际修改后的 Havok 物理数据。
        /// </summary>
        public void CommitToRes()
        {
            if (modifiedData == null || !modifiedData.HasModifications || resEntry == null)
                return;

            byte[] modifiedBytes = SaveBytes();
            if (modifiedBytes != null && modifiedBytes.Length > 0)
            {
                App.AssetManager.ModifyRes(resEntry.ResRid, modifiedBytes, ResourceMeta);
            }
        }

        public override void Read(NativeReader reader, AssetManager am, ResAssetEntry entry, ModifiedResource inModifiedData)
        {
            base.Read(reader, am, entry, inModifiedData);

            modifiedData = (inModifiedData != null)
                ? inModifiedData as HavokPhysicsDataModifiedResource
                : new HavokPhysicsDataModifiedResource(entry);

            resData = reader.ReadToEnd();
            resEntry = entry;

            reader.Position = 0;

            {
                partCount = reader.ReadInt();

                int partTranslationsCount = reader.ReadInt();
                reader.ReadLong();
                int localAabbsCount = reader.ReadInt();
                reader.ReadLong();
                int materialIndicesCount = reader.ReadInt();
                reader.ReadLong();
                int materialFlagsAndIndicesCount = reader.ReadInt();
                reader.ReadLong();

                int unknownCount = 0;
                if (ProfilesLibrary.DataVersion != 20141118 && ProfilesLibrary.DataVersion != 20141117)
                {
                    unknownCount = reader.ReadInt();
                    reader.ReadLong();
                }

                materialCountUsed = reader.ReadByte();
                highestMaterialIndex = reader.ReadByte();
                reader.ReadBytes(2);
                reader.Pad(16);

                for (int i = 0; i < partTranslationsCount; i++)
                    partTranslations.Add(reader.ReadVec3());
                reader.Pad(16);
                for (int i = 0; i < localAabbsCount; i++)
                    localAabbs.Add(reader.ReadAxisAlignedBox());
                reader.Pad(16);
                for (int i = 0; i < materialIndicesCount; i++)
                    materialIndices.Add(reader.ReadByte());
                reader.Pad(16);
                for (int i = 0; i < materialFlagsAndIndicesCount; i++)
                    materialFlagsAndIndices.Add(reader.ReadUInt());
                reader.Pad(16);

                if (ProfilesLibrary.DataVersion != 20141118 && ProfilesLibrary.DataVersion != 20141117)
                {
                    for (int i = 0; i < unknownCount; i++)
                        unknownIndices.Add(reader.ReadUShort());
                    reader.Pad(16);
                }
            }

            inst32 = new HkxInstance();
            inst32.Serialize(reader, 32);

            if (ProfilesLibrary.DataVersion != 20140225)
            {
                inst64 = new HkxInstance();
                inst64.Serialize(reader, 64);
            }

            int fixupSize32 = reader.ReadInt();
            int fixupSize64 = reader.ReadInt();

            long pos = reader.Position;
            long fileOffset = inst32.Header.StartOffset + inst32.AbsoluteDataStart;

            while (reader.Position < (pos + inst32.GlobalFixupSize))
            {
                int offset = reader.ReadInt();
                int objOffset = reader.ReadInt();

                if (offset != -1)
                    inst32.Header.ArrayOffsets.Add(fileOffset + offset, fileOffset + objOffset);
            }
            while (reader.Position < (pos + inst32.LocalFixupSize - inst32.GlobalFixupSize))
            {
                int offset = reader.ReadInt();
                int unknown = reader.ReadInt();
                int objOffset = reader.ReadInt();

                inst32.Header.ObjectOffsets.Add(fileOffset + offset, inst32.GetObject(fileOffset + objOffset));
            }

            reader.Position = (pos + fixupSize32);
            pos = reader.Position;

            fileOffset = inst64.Header.StartOffset + inst64.AbsoluteDataStart;

            while (reader.Position < (pos + inst64.GlobalFixupSize))
            {
                int offset = reader.ReadInt();
                int objOffset = reader.ReadInt();

                if (offset != -1)
                    inst64.Header.ArrayOffsets.Add(fileOffset + offset, fileOffset + objOffset);
            }
            pos = reader.Position;
            while (reader.Position < (pos + (inst64.LocalFixupSize - inst64.GlobalFixupSize)))
            {
                int offset = reader.ReadInt();
                int unknown = reader.ReadInt();
                int objOffset = reader.ReadInt();

                inst64.Header.ObjectOffsets.Add(fileOffset + offset, inst64.GetObject(fileOffset + objOffset));
            }

            //inst32.ReadObjects(reader);
            inst64.ReadObjects(reader);
        }

        /// <summary>
        /// 仅更新内存中的 transform，不立即写入 ModifyRes。需调用 CommitToRes 后才会写回。
        /// </summary>
        public void SetTransformInMemory(int index, Matrix newTransform)
        {
            modifiedData.SetTransform(index, newTransform);
            RegisterPendingCommit(this);
        }

        /// <summary>
        /// 更新 transform 并立即写入 ModifyRes（原有逻辑，保留兼容）
        /// </summary>
        public void UpdateData(int index, Matrix newTransform)
        {
            modifiedData.SetTransform(index, newTransform);
            modifiedData.UpdateData(this);
        }

        /// <summary>
        /// 仅恢复内存中的 transform（用于 Undo），不操作 ModifyRes。
        /// </summary>
        public void SetTransformInMemoryForUndo(int index, Matrix originalTransform)
        {
            modifiedData.SetTransform(index, originalTransform);
        }

        public void UndoData(int index, Matrix prevTransform)
        {
            // @todo: Figure out when to clear unmodified transforms, as this will continue to store transforms of meshes that
            //        were moved and then reverted

            modifiedData.SetTransform(index, prevTransform);
            modifiedData.UndoData(this);
        }

        public override byte[] SaveBytes()
        {
            MemoryStream ms = new MemoryStream();
            using (NativeWriter writer = new NativeWriter(ms))
            {
                writer.Write(resData);

                // write 32bit
                //{
                //    int index = 0;
                //    foreach (var instance in (inst32.Objects[0] as Hkx.hknpStaticCompoundShape).Instances)
                //    {
                //        Matrix transform;
                //        if (modifiedData.GetTransform(index, out transform))
                //        {
                //            var shape = instance.shape as Hkx.hknpShape;
                //            if (shape != null && (shape.unknown & 0x10) != 0)
                //            {
                //                transform = Matrix.Scaling(-1, 1, 1) * transform;
                //            }

                //            Vector3 scale;
                //            Vector3 translation;
                //            Quaternion rotation;
                //            Matrix rotationMatrix;

                //            transform.Decompose(out scale, out rotation, out translation);
                //            rotationMatrix = Matrix.RotationQuaternion(rotation);

                //            writer.Position = instance.DataOffset;

                //            writer.BaseStream.Position += 4; //writer.Write(rotationMatrix.M11);
                //            writer.BaseStream.Position += 4; //writer.Write(rotationMatrix.M12);
                //            writer.BaseStream.Position += 4; //writer.Write(rotationMatrix.M13);
                //            writer.BaseStream.Position += 4; //writer.Write(instance.tmp1);

                //            writer.BaseStream.Position += 4; //writer.Write(rotationMatrix.M21);
                //            writer.BaseStream.Position += 4; //writer.Write(rotationMatrix.M22);
                //            writer.BaseStream.Position += 4; //writer.Write(rotationMatrix.M23);
                //            writer.BaseStream.Position += 4; //writer.Write(instance.tmp2);

                //            writer.BaseStream.Position += 4; //writer.Write(rotationMatrix.M31);
                //            writer.BaseStream.Position += 4; //writer.Write(rotationMatrix.M32);
                //            writer.BaseStream.Position += 4; //writer.Write(rotationMatrix.M33);
                //            writer.BaseStream.Position += 4; //writer.Write(instance.tmp3);

                //            writer.Write(translation.X);
                //            writer.Write(translation.Y);
                //            writer.Write(translation.Z);
                //            writer.Write(instance.tmp4);

                //            writer.BaseStream.Position += 4; //writer.Write(scale.X);
                //            writer.BaseStream.Position += 4; //writer.Write(scale.Y);
                //            writer.BaseStream.Position += 4; //writer.Write(scale.Z);
                //        }
                //        index++;
                //    }
                //}

                // write 64bit
                {
                    foreach (hkBaseClass obj in (inst64.Objects[0] as Hkx.HavokPhysicsContainer).Objects)
                    {
                        // reset the static compound shape data tree, this is a hack and is sure to come with some
                        // performance implications, but it works for now.

                        hknpStaticCompoundShape staticCompoundShape = obj as Hkx.hknpStaticCompoundShape;
                        if (staticCompoundShape.ShapeData != null)
                        {
                            writer.Position = staticCompoundShape.ShapeData.ArrayOffset;
                            for (int i = 0; i < staticCompoundShape.ShapeData.Unknowns.Count; i++)
                            {
                                writer.Write(new byte[] { 0x00, 0x00, 0x00 });
                                writer.BaseStream.Position += 3;
                            }
                        }

                        int index = 0;
                        foreach (hknpStaticCompoundShape.hknpInstance instance in (obj as Hkx.hknpStaticCompoundShape).Instances)
                        {
                            Matrix transform;
                            if (modifiedData.GetTransform(index, out transform))
                            {
                                hknpShape shape = instance.shape as Hkx.hknpShape;
                                if (shape != null && (shape.unknown & 0x10) != 0)
                                {
                                    transform = Matrix.Scaling(-1, 1, 1) * transform;
                                }

                                Vector3 scale;
                                Vector3 translation;
                                Quaternion rotation;
                                Matrix rotationMatrix;

                                transform.Decompose(out scale, out rotation, out translation);
                                rotationMatrix = Matrix.RotationQuaternion(rotation);

                                writer.Position = instance.DataOffset;

                                writer.Write(rotationMatrix.M11);
                                writer.Write(rotationMatrix.M12);
                                writer.Write(rotationMatrix.M13);
                                writer.BaseStream.Position += 4;

                                writer.Write(rotationMatrix.M21);
                                writer.Write(rotationMatrix.M22);
                                writer.Write(rotationMatrix.M23);
                                writer.BaseStream.Position += 4;

                                writer.Write(rotationMatrix.M31);
                                writer.Write(rotationMatrix.M32);
                                writer.Write(rotationMatrix.M33);
                                writer.BaseStream.Position += 4;

                                writer.Write(translation.X);
                                writer.Write(translation.Y);
                                writer.Write(translation.Z);
                                writer.BaseStream.Position += 4;

                                writer.Write(scale.X);
                                writer.Write(scale.Y);
                                writer.Write(scale.Z);
                            }
                            index++;
                        }
                    }
                }
            }

            byte[] outBuffer = ms.ToArray();
            ms.Dispose();

            return outBuffer;
        }

        public Matrix GetTransform(int index)
        {
            Matrix outTransform;
            if (modifiedData.GetTransform(index, out outTransform))
                return outTransform;

            Matrix preScale = Matrix.Identity;
            hknpStaticCompoundShape.hknpInstance instance = RootShape.Instances[(int)index];
            hknpShape shape = instance.shape as Hkx.hknpShape;

            if (shape != null && (shape.unknown & 0x10) != 0)
            {
                preScale = Matrix.Scaling(-1, 1, 1);
            }

            return
                preScale *
                Matrix.Scaling(instance.Scale) *
                instance.Transform;
            //instance.Rotation * 
            //Matrix.Translation(instance.Translation);
        }

        public string GetPhysicsShapeType(int index)
        {
            hknpStaticCompoundShape.hknpInstance instance = RootShape.Instances[index];

            // 2026/01/18
            // 改动标记
            if (instance.shape == null)
                return "Shape Is Null";

            return instance.shape.GetType().Name;
        }

        public override ModifiedResource SaveModifiedResource()
        {
            return modifiedData;
        }
    }

    public class HavokPhysicsDataActionHandler : ICustomActionHandler
    {
        public HandlerUsage Usage => HandlerUsage.Modify;

        private class HavokPhysicsDataResource : EditorModResource
        {
            public static uint Hash => (uint)Frosty.Hash.Fnv1.HashString(typeof(HavokPhysicsDataActionHandler).FullName);
            public override ModResourceType Type => ModResourceType.Res;

            private uint resType;
            private ulong resRid;
            private byte[] resMeta;

            public HavokPhysicsDataResource(ResAssetEntry entry, FrostyModWriter.Manifest manifest)
                : base(entry)
            {
                byte[] data = null;

                // 优先使用 Data：完整的修改后二进制（CommitToRes 通过 ModifyRes(bytes) 写入）
                if (entry.ModifiedEntry?.Data != null && entry.ModifiedEntry.Data.Length > 0)
                {
                    data = entry.ModifiedEntry.Data;
                }
                // 兼容 DataObject：ModifiedResource 序列化（旧逻辑）
                else
                {
                    ModifiedResource md = entry.ModifiedEntry?.DataObject as ModifiedResource;
                    data = md?.Save();
                }

                if (data == null)
                {
                    throw new InvalidOperationException($"HavokPhysicsData 资源 {entry.Name} 的修改数据为空，请确保已通过「写回 Havok Transform 到 Res」按钮保存修改");
                }

                name = entry.Name.ToLower();
                sha1 = Utils.GenerateSha1(data);
                resourceIndex = manifest.Add(sha1, data);
                size = entry.ModifiedEntry?.OriginalSize ?? data.Length;
                handlerHash = (int)Hash;

                resType = entry.ResType;
                resRid = entry.ResRid;
                resMeta = (entry.ModifiedEntry.ResMeta != null) ? entry.ModifiedEntry.ResMeta : entry.ResMeta;
            }

            public override void Write(NativeWriter writer)
            {
                base.Write(writer);

                writer.Write(resType);
                writer.Write(resRid);
                writer.Write(resMeta.Length);
                writer.Write(resMeta);
            }
        }

        public void SaveToMod(FrostyModWriter writer, AssetEntry entry)
        {
            writer.AddResource(new HavokPhysicsDataResource(entry as ResAssetEntry, writer.ResourceManifest));
        }

        public IEnumerable<string> GetResourceActions(string name, byte[] data)
        {
            return new List<string>();
        }

        public object Load(object existing, byte[] newData)
        {
            HavokPhysicsData.HavokPhysicsDataModifiedResource newPhysicsData = ModifiedResource.Read(newData) as HavokPhysicsData.HavokPhysicsDataModifiedResource;
            return newPhysicsData;
        }

        public void Modify(AssetEntry origEntry, AssetManager am, RuntimeResources runtimeResources, object data, out byte[] outData)
        {
            HavokPhysicsData.HavokPhysicsDataModifiedResource physicsData = data as HavokPhysicsData.HavokPhysicsDataModifiedResource;
            HavokPhysicsData havokData = am.GetResAs<HavokPhysicsData>(am.GetResEntry((origEntry as ResAssetEntry).ResRid), physicsData);

            byte[] uncompressedData = havokData.SaveBytes();

            origEntry.OriginalSize = uncompressedData.Length;
            outData = Utils.CompressFile(uncompressedData);

            origEntry.Size = outData.Length;
            origEntry.Sha1 = Utils.GenerateSha1(outData);
        }
    }
}
