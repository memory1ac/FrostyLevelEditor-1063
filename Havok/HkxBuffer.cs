using System;
using System.Text;

namespace LevelEditorPlugin.Havok
{
    /// <summary>
    /// 带边界检查的小端读取器。所有越界都会抛出带上下文的 <see cref="HkxParseException"/>，
    /// 绝不静默返回 0 —— 这样坏文件会给出可定位的报错。
    /// </summary>
    internal sealed class HkxBuffer
    {
        private readonly byte[] data;
        private int pos;

        public HkxBuffer(byte[] buffer, int position = 0)
        {
            data = buffer ?? throw new ArgumentNullException(nameof(buffer));
            pos = position;
        }

        public byte[] Data => data;
        public int Length => data.Length;
        public int Position {
            get => pos;
            set {
                if (value < 0 || value > data.Length)
                    throw new HkxParseException($"定位越界：0x{value:X}（文件长度 0x{data.Length:X}）");
                pos = value;
            }
        }

        public int Remaining => data.Length - pos;

        private void Need(int count, string what)
        {
            if (count < 0 || pos < 0 || pos + count > data.Length)
                throw new HkxParseException($"{what} 越界：偏移 0x{pos:X} 需要 {count} 字节，文件长度 0x{data.Length:X}");
        }

        public byte U8(string what = "u8")
        {
            Need(1, what);
            return data[pos++];
        }

        public ushort U16(string what = "u16")
        {
            Need(2, what);
            ushort v = (ushort)(data[pos] | (data[pos + 1] << 8));
            pos += 2;
            return v;
        }

        public uint U32(string what = "u32")
        {
            Need(4, what);
            uint v = (uint)(data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24));
            pos += 4;
            return v;
        }

        public int I32(string what = "i32") => unchecked((int)U32(what));

        public ulong U64(string what = "u64")
        {
            Need(8, what);
            ulong lo = U32(what);
            ulong hi = U32(what);
            return lo | (hi << 32);
        }

        public float F32(string what = "f32")
        {
            Need(4, what);
            byte[] tmp = new byte[4];
            Buffer.BlockCopy(data, pos, tmp, 0, 4);
            pos += 4;
            return BitConverter.ToSingle(tmp, 0);
        }

        public byte[] Bytes(int count, string what = "bytes")
        {
            Need(count, what);
            byte[] r = new byte[count];
            Buffer.BlockCopy(data, pos, r, 0, count);
            pos += count;
            return r;
        }

        public void Skip(int count, string what = "skip")
        {
            Need(count, what);
            pos += count;
        }

        public void Align(int alignment)
        {
            int aligned = (pos + alignment - 1) / alignment * alignment;
            if (aligned > data.Length)
                throw new HkxParseException($"对齐越界：0x{pos:X} → 0x{aligned:X}，文件长度 0x{data.Length:X}");
            pos = aligned;
        }

        /// <summary>
        /// 读到终止字节（不含），并吃掉终止字节。
        /// </summary>
        public string Terminated(byte terminator, int maxLength, string what)
        {
            int start = pos;
            int end = start;
            while (end < data.Length && data[end] != terminator)
            {
                if (end - start >= maxLength)
                    throw new HkxParseException($"{what} 字符串过长（超过 {maxLength} 字节），偏移 0x{start:X} 未找到终止符 0x{terminator:X2}");
                end++;
            }

            if (end >= data.Length)
                throw new HkxParseException($"{what} 字符串未终止：偏移 0x{start:X} 直到文件末尾都没有 0x{terminator:X2}");

            string s = Encoding.ASCII.GetString(data, start, end - start);
            pos = end + 1;
            return s;
        }

        /// <summary>
        /// 从当前偏移开始找 8 字节小端 magic（含第二个 magic 校验），返回其位置，找不到返回 -1。
        /// </summary>
        public static int FindMagicPair(byte[] buffer, int from, int to, uint magic1, uint magic2)
        {
            from = Math.Max(0, from);
            to = Math.Min(buffer.Length - 8, to);
            for (int i = from; i <= to; i++)
            {
                if (buffer[i] != (byte)(magic1 & 0xFF) || buffer[i + 1] != (byte)((magic1 >> 8) & 0xFF)
                    || buffer[i + 2] != (byte)((magic1 >> 16) & 0xFF) || buffer[i + 3] != (byte)((magic1 >> 24) & 0xFF))
                    continue;

                uint m2 = (uint)(buffer[i + 4] | (buffer[i + 5] << 8) | (buffer[i + 6] << 16) | (buffer[i + 7] << 24));
                if (m2 == magic2)
                    return i;
            }
            return -1;
        }
    }
}
