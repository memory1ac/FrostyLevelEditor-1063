using System;
using System.Collections.Generic;
using System.Numerics;

namespace LevelEditorPlugin.Havok
{
    /// <summary>
    /// 一次编辑会话：原始字节 + 解析结果 + 几何 + 撤销栈 + 写回。
    /// 所有改动都是等长原地写入，WorkingBytes 始终可以直接作为 res 的新内容。
    /// </summary>
    public sealed class HavokSession
    {
        public const int MaxUndoDepth = 32;
        public const int ScaleOffset = 0x40;

        private readonly Stack<byte[]> undo = new Stack<byte[]>();
        private readonly Stack<byte[]> redo = new Stack<byte[]>();

        public string Name { get; private set; } = "";
        public byte[] OriginalBytes { get; private set; }
        public byte[] WorkingBytes { get; private set; }
        public HavokRes Res { get; private set; }
        public HavokScene Scene { get; private set; }
        public HavokGeometryResult Geometry { get; private set; }
        public HavokGeometryOptions Options { get; } = new HavokGeometryOptions();

        /// <summary>写回目标（EBX 引用到的 res rid / meta）；为 null 时只能导出不能写回。</summary>
        public ulong? ResRid { get; set; }
        public byte[] ResMeta { get; set; }
        public string SourceLabel { get; set; } = "";

        /// <summary>写回时顺便把 BVH 节点清零（LevelEditor 验证过的做法，游戏会在运行时重建）。</summary>
        public bool ResetAabbTreeOnCommit { get; set; } = true;

        public bool IsDirty { get; private set; }
        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;
        public HavokShapeInstance Selected => Options.Selected;

        /// <summary>解析或写回过程中产生的所有警告/错误。</summary>
        public List<string> Messages { get; } = new List<string>();

        /// <summary>场景结构变化（重新解析）后触发，UI 需要重建树。</summary>
        public event Action Reloaded;

        /// <summary>几何或统计变化后触发，UI 只需刷新视口与状态栏。</summary>
        public event Action GeometryChanged;

        #region -- 载入 --

        public static HavokSession Load(byte[] bytes, string name, ulong? resRid = null, byte[] resMeta = null, string sourceLabel = "")
        {
            if (bytes == null || bytes.Length == 0)
                throw new HkxParseException("res 数据为空");

            HavokSession session = new HavokSession
            {
                Name = name ?? "",
                OriginalBytes = bytes,
                WorkingBytes = (byte[])bytes.Clone(),
                ResRid = resRid,
                ResMeta = resMeta,
                SourceLabel = sourceLabel ?? "",
            };
            session.Rebuild(true);
            return session;
        }

        /// <summary>用当前 WorkingBytes 重新解析（撤销/重做/写回校验都走这里）。</summary>
        public void Rebuild(bool resetHistory = false)
        {
            Res = HavokRes.Parse(WorkingBytes, Name, HavokLayout.BuiltIn);
            Scene = HavokSceneBuilder.Build(Res);

            Messages.Clear();
            Messages.AddRange(Res.Warnings);
            Messages.AddRange(Res.Issues);
            Messages.AddRange(Scene.Warnings);

            if (resetHistory)
            {
                undo.Clear();
                redo.Clear();
                Options.Selected = null;
                IsDirty = false;
            }
            else
            {
                // 重新解析后按偏移找回选中实例
                int offset = selectedFileOffset;
                selectedFileOffset = -1;
                if (offset >= 0)
                {
                    foreach (HavokShapeInstance inst in Scene.FlatInstances)
                    {
                        if (inst.FileOffset == offset)
                        {
                            Options.Selected = inst;
                            break;
                        }
                    }
                }
            }

            RefreshGeometry();
            Reloaded?.Invoke();
        }

        #endregion

        #region -- 几何与选中 --

        private int selectedFileOffset = -1;

        public void Select(HavokShapeInstance instance)
        {
            Options.Selected = instance;
            selectedFileOffset = instance?.FileOffset ?? -1;
            RefreshGeometry();
        }

        public void RefreshGeometry()
        {
            Geometry = HavokGeometry.Build(Scene, Options);
            GeometryChanged?.Invoke();
        }

        #endregion

        #region -- 编辑 --

        /// <summary>
        /// 改一个实例的 transform / scale（等长回填 + 撤销快照）。
        /// 只写 3x3 旋转 + 平移，每行第 4 个 float 与 padding 原样保留。
        /// </summary>
        public void ApplyInstance(HavokShapeInstance instance, Matrix4x4 transform, Vector4 scale)
        {
            if (instance == null)
                return;

            PushUndo();
            instance.Transform = transform;
            instance.Scale = scale;
            HavokRes.PatchInstance(WorkingBytes, instance.FileOffset, transform, scale, ScaleOffset);
            HavokSceneBuilder.UpdateWorldMatrices(Scene);
            IsDirty = true;
            selectedFileOffset = instance.FileOffset;
            RefreshGeometry();
        }

        private void PushUndo()
        {
            undo.Push((byte[])WorkingBytes.Clone());
            while (undo.Count > MaxUndoDepth)
            {
                // Stack 只能从顶部移除，超过深度时整体重建
                byte[][] items = undo.ToArray();
                undo.Clear();
                for (int i = Math.Min(items.Length, MaxUndoDepth) - 1; i >= 0; i--)
                    undo.Push(items[i]);
                break;
            }
            redo.Clear();
        }

        public bool Undo()
        {
            if (undo.Count == 0)
                return false;
            redo.Push((byte[])WorkingBytes.Clone());
            WorkingBytes = undo.Pop();
            IsDirty = !BytesEqual(WorkingBytes, OriginalBytes);
            Rebuild(false);
            return true;
        }

        public bool Redo()
        {
            if (redo.Count == 0)
                return false;
            undo.Push((byte[])WorkingBytes.Clone());
            WorkingBytes = redo.Pop();
            IsDirty = !BytesEqual(WorkingBytes, OriginalBytes);
            Rebuild(false);
            return true;
        }

        public void RevertAll()
        {
            if (!IsDirty)
                return;
            PushUndo();
            WorkingBytes = (byte[])OriginalBytes.Clone();
            IsDirty = false;
            Rebuild(false);
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null || a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }

        #endregion

        #region -- 写回 --

        /// <summary>
        /// 写回 res：等长替换内容，不重排任何偏移。
        /// writer 由调用方提供（编辑器里就是 App.AssetManager.ModifyRes），
        /// 这样解码/写回这一层不依赖 Frosty 的全局状态，便于离线验证。
        /// </summary>
        public string Commit(Action<ulong, byte[], byte[]> writer)
        {
            if (!IsDirty)
                return "没有改动，无需写回。";

            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            if (!ResRid.HasValue || ResRid.Value == 0)
                throw new HkxParseException("这个数据没有关联 res（只能导出，不能写回）");

            byte[] bytes = (byte[])WorkingBytes.Clone();
            int cleared = ResetAabbTreeOnCommit ? ClearAabbTrees(bytes) : 0;

            writer(ResRid.Value, bytes, ResMeta);

            WorkingBytes = bytes;
            IsDirty = false;
            Rebuild(false);

            string verify = VerifyAgainst(bytes);
            string note = Res?.PackFile32 != null
                ? "；注意：32 位 packfile 副本没有同步（运行期读取 64 位副本）"
                : "";
            return $"已写回 {bytes.Length} 字节" + (cleared > 0 ? $"，清零 {cleared} 个 BVH 节点" : "") + verify + note;
        }

        /// <summary>把 BVH 节点盒清零：每个 8 字节节点写 3 个 0（与 LevelEditor 的做法一致）。</summary>
        private int ClearAabbTrees(byte[] bytes)
        {
            int count = 0;
            foreach (HavokShapeData sd in EnumerateShapeData(Scene))
            {
                if (sd.NodesFileOffset < 0 || sd.NodeCount <= 0)
                    continue;
                int stride = HkxAabbTreeCodec.NodeSize;
                for (int i = 0; i < sd.NodeCount; i++)
                {
                    int p = sd.NodesFileOffset + i * stride;
                    if (p + 3 > bytes.Length)
                        break;
                    bytes[p] = 0;
                    bytes[p + 1] = 0;
                    bytes[p + 2] = 0;
                    count++;
                }
            }
            return count;
        }

        private static IEnumerable<HavokShapeData> EnumerateShapeData(HavokScene scene)
        {
            foreach (HavokCompound c in scene.RootCompounds)
            {
                foreach (HavokShapeData sd in EnumerateShapeData(c, 0))
                    yield return sd;
            }
        }

        private static IEnumerable<HavokShapeData> EnumerateShapeData(HavokCompound compound, int depth)
        {
            if (depth > 4)
                yield break;
            if (compound.ShapeData != null)
                yield return compound.ShapeData;
            foreach (HavokShapeInstance inst in compound.Instances)
            {
                if (inst.Shape?.Compound == null)
                    continue;
                foreach (HavokShapeData sd in EnumerateShapeData(inst.Shape.Compound, depth + 1))
                    yield return sd;
            }
        }

        /// <summary>写回后重解析，逐字段比对实例 transform / scale。</summary>
        private string VerifyAgainst(byte[] bytes)
        {
            try
            {
                HavokRes check = HavokRes.Parse(bytes, Name, HavokLayout.BuiltIn);
                HavokScene checkScene = HavokSceneBuilder.Build(check);

                Dictionary<int, HavokShapeInstance> byOffset = new Dictionary<int, HavokShapeInstance>();
                foreach (HavokShapeInstance inst in checkScene.FlatInstances)
                    byOffset[inst.FileOffset] = inst;

                int compared = 0;
                foreach (HavokShapeInstance inst in Scene.FlatInstances)
                {
                    if (!byOffset.TryGetValue(inst.FileOffset, out HavokShapeInstance other))
                        continue;
                    compared++;
                    if (!MatrixClose(inst.Transform, other.Transform) || !VectorClose(inst.Scale, other.Scale))
                        return $"；校验失败：实例 {inst.Index} 写回后数值不一致（偏移 0x{inst.FileOffset:X}）";
                }

                return $"，重解析校验通过（{compared} 个实例）";
            }
            catch (Exception e)
            {
                return $"；注意：写回后重解析失败：{e.Message}";
            }
        }

        private static bool MatrixClose(Matrix4x4 a, Matrix4x4 b, float eps = 1e-5f)
        {
            return Math.Abs(a.M11 - b.M11) < eps && Math.Abs(a.M12 - b.M12) < eps && Math.Abs(a.M13 - b.M13) < eps
                && Math.Abs(a.M21 - b.M21) < eps && Math.Abs(a.M22 - b.M22) < eps && Math.Abs(a.M23 - b.M23) < eps
                && Math.Abs(a.M31 - b.M31) < eps && Math.Abs(a.M32 - b.M32) < eps && Math.Abs(a.M33 - b.M33) < eps
                && Math.Abs(a.M41 - b.M41) < eps && Math.Abs(a.M42 - b.M42) < eps && Math.Abs(a.M43 - b.M43) < eps;
        }

        private static bool VectorClose(Vector4 a, Vector4 b, float eps = 1e-5f)
            => Math.Abs(a.X - b.X) < eps && Math.Abs(a.Y - b.Y) < eps && Math.Abs(a.Z - b.Z) < eps;

        #endregion

        #region -- 统计 --

        public string Statistics()
        {
            int objects = Res?.Primary?.Objects.Count ?? 0;
            int instances = Scene?.FlatInstances.Count ?? 0;
            int triangles = Geometry?.TriangleCount ?? 0;
            int lines = Geometry?.Lines.Count ?? 0;
            int nodes = 0;
            foreach (HavokShapeData sd in EnumerateShapeData(Scene))
                nodes += sd.NodeCount;
            return $"{instances} 实例 · {objects} 对象 · {triangles} 三角 · {lines / 2} 线段 · {nodes} BVH 节点";
        }

        #endregion
    }
}
