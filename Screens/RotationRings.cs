using SharpDX;
using SharpDX.Direct3D;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using D3D11 = SharpDX.Direct3D11;

namespace LevelEditorPlugin.Screens;

/// <summary>
/// 平移 Gizmo 上的旋转环。轴环比例和拾取半径参考 three.js TransformControls：
/// 可视圆环较细，拾取体积更宽；背向摄像机的半圈变暗，悬停时整圈高亮。
/// </summary>
internal sealed class RotationRingMesh : IDisposable
{
    public const float AxisRadius = 0.42f;
    public const float TubeRadius = 0.016f;
    public const float PickRadius = 0.055f;
    public const int AxisCount = 3;

    public static readonly Vector3[] Axes =
    {
        Vector3.UnitX,
        Vector3.UnitY,
        Vector3.UnitZ
    };

    public static readonly Vector4[] AxisColors =
    {
        new Vector4(1.0f, 0.16f, 0.12f, 1f),
        new Vector4(0.18f, 0.92f, 0.22f, 1f),
        new Vector4(0.25f, 0.48f, 1.0f, 1f)
    };

    public static readonly Vector4 HighlightColor = new Vector4(1f, 1f, 0f, 1f);

    const int MajorSegments = 64;
    const int MinorSegments = 8;
    const int PickSamples = 64;

    [StructLayout(LayoutKind.Sequential)]
    struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoord;

        public Vertex(Vector3 position, Vector3 normal, Vector2 texCoord)
        {
            Position = position;
            Normal = normal;
            TexCoord = texCoord;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PixelConstants
    {
        public Vector4 SelectIndex;
        public Vector4 AxisColor;
        public Vector4 RingAxis;
        public Vector4 LocalCamera;
    }

    readonly D3D11.Buffer vertexBuffer;
    readonly D3D11.Buffer indexBuffer;
    readonly D3D11.Buffer constantBuffer;
    readonly D3D11.VertexBufferBinding vertexBinding;
    readonly int[] indexStarts = new int[AxisCount];
    readonly int indexCount;
    readonly int vertexStride;

    static readonly Vector3[][] PickPoints = new Vector3[AxisCount][];

    static RotationRingMesh()
    {
        for (int axis = 0; axis < AxisCount; axis++)
            PickPoints[axis] = BuildCircle(Axes[axis], AxisRadius, PickSamples);
    }

    public RotationRingMesh(D3D11.Device device)
    {
        List<Vertex> vertices = new List<Vertex>();
        List<ushort> indices = new List<ushort>();

        for (int axis = 0; axis < AxisCount; axis++)
        {
            indexStarts[axis] = indices.Count;
            AppendTorus(vertices, indices, Axes[axis], AxisRadius, TubeRadius);
        }

        indexCount = indexStarts[1] - indexStarts[0];

        vertexStride = System.Runtime.InteropServices.Marshal.SizeOf<Vertex>();

        using (DataStream stream = new DataStream(indices.Count * sizeof(ushort), false, true))
        {
            stream.WriteRange(indices.ToArray());
            stream.Position = 0;
            indexBuffer = new D3D11.Buffer(device, stream, indices.Count * sizeof(ushort), D3D11.ResourceUsage.Default, D3D11.BindFlags.IndexBuffer, D3D11.CpuAccessFlags.None, D3D11.ResourceOptionFlags.None, sizeof(ushort));
        }

        using (DataStream stream = new DataStream(vertices.Count * vertexStride, false, true))
        {
            stream.WriteRange(vertices.ToArray());
            stream.Position = 0;
            vertexBuffer = new D3D11.Buffer(device, stream, vertices.Count * vertexStride, D3D11.ResourceUsage.Default, D3D11.BindFlags.VertexBuffer, D3D11.CpuAccessFlags.None, D3D11.ResourceOptionFlags.None, vertexStride);
            vertexBinding = new D3D11.VertexBufferBinding(vertexBuffer, vertexStride, 0);
        }

        constantBuffer = new D3D11.Buffer(device, new D3D11.BufferDescription
        {
            SizeInBytes = System.Runtime.InteropServices.Marshal.SizeOf<PixelConstants>(),
            Usage = D3D11.ResourceUsage.Default,
            BindFlags = D3D11.BindFlags.ConstantBuffer,
            CpuAccessFlags = D3D11.CpuAccessFlags.None,
            OptionFlags = D3D11.ResourceOptionFlags.None,
            StructureByteStride = 0
        });
    }

    public void Draw(D3D11.DeviceContext context, int highlightAxis, Vector3 localCamera)
    {
        context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
        context.InputAssembler.SetVertexBuffers(0, vertexBinding);
        context.InputAssembler.SetIndexBuffer(indexBuffer, SharpDX.DXGI.Format.R16_UInt, 0);

        int hotRing = highlightAxis >= 3 && highlightAxis <= 5 ? highlightAxis - 3 : -1;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < AxisCount; i++)
            {
                bool hot = i == hotRing;
                if ((pass == 0 && hot) || (pass == 1 && !hot))
                    continue;

                Vector4 color = hot ? HighlightColor : AxisColors[i];
                color.W = hot ? 2f : 1f;
                DrawRing(context, i, color, new Vector4(Axes[i], 0f), localCamera);
            }
        }
    }

    public static bool Raycast(Ray ray, Vector3 center, float scale, out int localAxis, out float distance)
    {
        localAxis = -1;
        distance = float.MaxValue;
        if (scale < 1e-4f)
            return false;

        float best = float.MaxValue;
        int bestAxis = -1;

        for (int i = 0; i < AxisCount; i++)
        {
            if (TryHitCircle(ray, center, PickPoints[i], scale, out float hitT) && hitT < best)
            {
                best = hitT;
                bestAxis = i;
            }
        }

        if (bestAxis < 0)
            return false;

        localAxis = bestAxis;
        distance = best;
        return true;
    }

    public void Dispose()
    {
        constantBuffer?.Dispose();
        vertexBuffer?.Dispose();
        indexBuffer?.Dispose();
    }

    void DrawRing(D3D11.DeviceContext context, int ring, Vector4 color, Vector4 axis, Vector3 localCamera)
    {
        PixelConstants constants = new PixelConstants
        {
            SelectIndex = Vector4.Zero,
            AxisColor = color,
            RingAxis = axis,
            LocalCamera = new Vector4(localCamera, 0f)
        };
        context.UpdateSubresource(ref constants, constantBuffer, 0);
        context.VertexShader.SetConstantBuffer(2, constantBuffer);
        context.PixelShader.SetConstantBuffer(2, constantBuffer);
        context.DrawIndexed(indexCount, indexStarts[ring], 0);
    }

    static bool TryHitCircle(Ray ray, Vector3 center, Vector3[] localPoints, float scale, out float hitT)
    {
        hitT = float.MaxValue;
        float pick = PickRadius * scale;
        float pickSq = pick * pick;
        bool hit = false;

        for (int i = 0; i < localPoints.Length; i++)
        {
            Vector3 world = center + localPoints[i] * scale;
            float t = Vector3.Dot(world - ray.Position, ray.Direction);
            if (t < 0f || t >= hitT)
                continue;

            Vector3 closest = ray.Position + ray.Direction * t;
            if ((closest - world).LengthSquared() > pickSq)
                continue;

            hitT = t;
            hit = true;
        }

        return hit;
    }

    static Vector3[] BuildCircle(Vector3 axis, float radius, int samples)
    {
        BuildBasis(axis, out Vector3 u, out Vector3 v);
        Vector3[] points = new Vector3[samples];
        for (int i = 0; i < samples; i++)
        {
            float angle = i * MathUtil.TwoPi / samples;
            points[i] = (u * (float)Math.Cos(angle) + v * (float)Math.Sin(angle)) * radius;
        }

        return points;
    }

    static void BuildBasis(Vector3 axis, out Vector3 u, out Vector3 v)
    {
        Vector3 n = Vector3.Normalize(axis);
        Vector3 helper = Math.Abs(n.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        u = Vector3.Normalize(Vector3.Cross(helper, n));
        v = Vector3.Cross(n, u);
    }

    static void AppendTorus(List<Vertex> vertices, List<ushort> indices, Vector3 axis, float majorRadius, float minorRadius)
    {
        BuildBasis(axis, out Vector3 u, out Vector3 v);
        Vector3 n = Vector3.Normalize(axis);
        int vbase = vertices.Count;
        int stride = MinorSegments + 1;

        for (int i = 0; i <= MajorSegments; i++)
        {
            float a = i * MathUtil.TwoPi / MajorSegments;
            float ca = (float)Math.Cos(a);
            float sa = (float)Math.Sin(a);
            Vector3 radial = u * ca + v * sa;
            Vector3 tubeCenter = radial * majorRadius;

            for (int j = 0; j <= MinorSegments; j++)
            {
                float b = j * MathUtil.TwoPi / MinorSegments;
                float cb = (float)Math.Cos(b);
                float sb = (float)Math.Sin(b);
                Vector3 normal = radial * cb + n * sb;
                vertices.Add(new Vertex(tubeCenter + normal * minorRadius, normal, Vector2.Zero));
            }
        }

        for (int i = 0; i < MajorSegments; i++)
        {
            for (int j = 0; j < MinorSegments; j++)
            {
                int i0 = vbase + i * stride + j;
                int i1 = i0 + stride;
                int i2 = i0 + 1;
                int i3 = i1 + 1;
                indices.Add((ushort)i0);
                indices.Add((ushort)i2);
                indices.Add((ushort)i1);
                indices.Add((ushort)i1);
                indices.Add((ushort)i2);
                indices.Add((ushort)i3);
            }
        }
    }
}
