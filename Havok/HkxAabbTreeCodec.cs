using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace LevelEditorPlugin.Havok
{
    /// <summary>
    /// hkcdStaticTree 的节点解码（hkpStaticCompoundShape 用的是 codec3Axis6 量化）。
    ///
    /// 每个节点 8 字节，低 36 位是 6 个 6 位的量化坐标（x0,y0,z0,x1,y1,z1），
    /// 高 4 位（bit 36..39）是子节点索引 / 叶标志，剩余 24 位保留。
    ///
    /// 注意：BF1 的 cratewood_02 上按这个编码解出来的盒子明显不合理（根节点只覆盖极小一块），
    /// 说明 BF1 用的是另一套节点编码。为了不误导用户，这里做"合理性校验"：
    /// 解出来的根节点必须至少覆盖 domain 的一半，否则判为"未知编码"，只显示 domain。
    /// </summary>
    public static class HkxAabbTreeCodec
    {
        public const int NodeSize = 8;
        private const int QuantBits = 6;
        private const int QuantMax = (1 << QuantBits) - 1;

        public static bool TryDecode(byte[] data, int arrayFile, int count, Vector3 domainMin, Vector3 domainMax,
            List<Vector3[]> boxes, out string status)
        {
            boxes.Clear();

            if (count <= 0)
            {
                status = "没有节点";
                return false;
            }

            if (arrayFile < 0 || arrayFile + (long)count * NodeSize > data.Length)
            {
                status = $"节点数组越界（0x{arrayFile:X} + {count}*{NodeSize}）";
                return false;
            }

            List<Vector3[]> decoded = new List<Vector3[]>(count);
            int degenerate = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3[] box = DecodeNode(data, arrayFile + i * NodeSize, domainMin, domainMax);
                if (box == null)
                {
                    status = $"节点 {i} 的量化坐标自相矛盾（min > max）";
                    return false;
                }

                Vector3 size = box[1] - box[0];
                if (size.X <= 1e-6f && size.Y <= 1e-6f && size.Z <= 1e-6f)
                    degenerate++;
                decoded.Add(box);
            }

            // 合理性校验：根节点（index 0）应当覆盖 domain 的相当部分
            Vector3 rootSize = decoded[0][1] - decoded[0][0];
            Vector3 domainSize = Vector3.Max(domainMax - domainMin, new Vector3(1e-6f));
            float coverage = Math.Min(rootSize.X / domainSize.X,
                Math.Min(rootSize.Y / domainSize.Y, rootSize.Z / domainSize.Z));

            if (coverage < 0.5f || decoded.Count - degenerate < decoded.Count / 2)
            {
                status = $"节点编码未知（根节点仅覆盖 domain 的 {coverage * 100f:F0}%，退化节点 {degenerate}/{count}），只显示 domain";
                return false;
            }

            boxes.AddRange(decoded);
            status = $"{count} 个节点（codec3Axis6）";
            return true;
        }

        private static Vector3[] DecodeNode(byte[] data, int offset, Vector3 domainMin, Vector3 domainMax)
        {
            ulong lo = BitConverter.ToUInt32(data, offset);
            ulong hi = BitConverter.ToUInt32(data, offset + 4);
            ulong value = lo | (hi << 32);

            int[] q = new int[6];
            for (int i = 0; i < 6; i++)
                q[i] = (int)((value >> (i * QuantBits)) & QuantMax);

            if (q[0] > q[3] || q[1] > q[4] || q[2] > q[5])
                return null;

            Vector3 min = Unpack(domainMin, domainMax, q[0], q[1], q[2]);
            Vector3 max = Unpack(domainMin, domainMax, q[3], q[4], q[5]);
            return new[] { min, max };
        }

        public static Vector3 Unpack(Vector3 domainMin, Vector3 domainMax, int x, int y, int z)
        {
            Vector3 size = domainMax - domainMin;
            return new Vector3(
                domainMin.X + size.X * x / QuantMax,
                domainMin.Y + size.Y * y / QuantMax,
                domainMin.Z + size.Z * z / QuantMax);
        }

        /// <summary>诊断用：把前 N 个节点按原始 6 位字段打印出来。</summary>
        public static string DescribeRaw(byte[] data, int arrayFile, int count, int maxNodes = 16)
        {
            StringBuilder sb = new StringBuilder();
            if (arrayFile < 0 || count <= 0)
                return "（无节点）";

            for (int i = 0; i < Math.Min(count, maxNodes); i++)
            {
                int p = arrayFile + i * NodeSize;
                if (p + NodeSize > data.Length)
                    break;

                ulong lo = BitConverter.ToUInt32(data, p);
                ulong hi = BitConverter.ToUInt32(data, p + 4);
                ulong value = lo | (hi << 32);

                sb.Append($"node[{i,2}] raw=0x{hi:X8}{lo:X8} q=[");
                for (int k = 0; k < 6; k++)
                {
                    sb.Append(((value >> (k * QuantBits)) & QuantMax).ToString());
                    if (k != 5)
                        sb.Append(',');
                }
                sb.Append(']');
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
