using Frosty.Core;
using Frosty.Core.Viewport;
using FrostySdk;
using FrostySdk.IO;
using FrostySdk.Managers;
using LevelEditorPlugin.Options;
using MeshSetPlugin.Render;
using MeshSetPlugin.Resources;
using SharpDX;
using SharpDX.Direct3D;
using System.Collections.Generic;
using System.IO;
using D3D11 = SharpDX.Direct3D11;
using MeshSet = LevelEditorPlugin.Resources.MeshSet;
using MeshSetLod = LevelEditorPlugin.Resources.MeshSetLod;
using MeshSetSection = LevelEditorPlugin.Resources.MeshSetSection;

namespace LevelEditorPlugin.Render;

public struct MeshVertex
{
    public Vector3 Position;
    public Vector3 Normal;
}

public class MeshSectionRenderable
{
    public int StartIndex { get; private set; }
    public int VertexOffset { get; private set; }
    public int PrimitiveCount { get; private set; }
    public PrimitiveTopology PrimitiveType { get; private set; }
    public int VertexStride { get; private set; }

    public D3D11.Buffer VertexBuffer { get; private set; }
    public D3D11.VertexBufferBinding VertexBufferBinding { get; private set; }

    public List<Vector3> HitProxyVertices { get; private set; } = [];

    public MeshSectionRenderable(RenderCreateState state, MeshSetSection section, byte[] chunkData, bool isHitProxy)
    {
        StartIndex = (int)section.StartIndex;
        VertexOffset = (int)section.VertexOffset;
        PrimitiveCount = (int)section.PrimitiveCount;
        VertexStride = (int)Utilities.SizeOf<MeshVertex>();

        switch (section.PrimitiveType)
        {
            case Resources.PrimitiveType.PrimitiveType_LineList: PrimitiveType = PrimitiveTopology.LineList; break;
            case Resources.PrimitiveType.PrimitiveType_LineStrip: PrimitiveType = PrimitiveTopology.LineStrip; break;
            case Resources.PrimitiveType.PrimitiveType_PointList: PrimitiveType = PrimitiveTopology.PointList; break;
            case Resources.PrimitiveType.PrimitiveType_TriangleList: PrimitiveType = PrimitiveTopology.TriangleList; break;
            case Resources.PrimitiveType.PrimitiveType_TriangleStrip: PrimitiveType = PrimitiveTopology.TriangleStrip; break;
        }

        MeshVertex[] vertices = new MeshVertex[section.VertexCount];
        using (NativeReader reader = new NativeReader(new MemoryStream(chunkData)))
        {
            reader.Position = section.VertexOffset;
            int totalStride = 0;

            foreach (GeometryDeclarationDesc.Stream stream in section.GeometryDeclDesc[0].Streams)
            {
                if (stream.VertexStride == 0)
                    continue;

                for (int v = 0; v < section.VertexCount; v++)
                {
                    MeshVertex vertex = vertices[v];
                    int currentStride = 0;

                    foreach (GeometryDeclarationDesc.Element element in section.GeometryDeclDesc[0].Elements)
                    {
                        if (element.Usage == VertexElementUsage.Unknown)
                            continue;

                        if (currentStride >= totalStride && currentStride < (totalStride + stream.VertexStride))
                        {
                            if (element.Usage == VertexElementUsage.Pos)
                            {
                                switch (element.Format)
                                {
                                    case VertexElementFormat.Float3: vertex.Position = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()); break;
                                    case VertexElementFormat.Float4: vertex.Position = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()); reader.ReadFloat(); break;
                                    case VertexElementFormat.Half3: vertex.Position = new Vector3(HalfUtils.Unpack(reader.ReadUShort()), HalfUtils.Unpack(reader.ReadUShort()), HalfUtils.Unpack(reader.ReadUShort())); break;
                                    case VertexElementFormat.Half4: vertex.Position = new Vector3(HalfUtils.Unpack(reader.ReadUShort()), HalfUtils.Unpack(reader.ReadUShort()), HalfUtils.Unpack(reader.ReadUShort())); reader.ReadUShort(); break;
                                }
                            }
                            else if (element.Usage == VertexElementUsage.Normal)
                            {
                                switch (element.Format)
                                {
                                    case VertexElementFormat.Float3: vertex.Normal = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()); break;
                                    case VertexElementFormat.Float4: vertex.Normal = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()); reader.ReadFloat(); break;
                                    case VertexElementFormat.Half3: vertex.Normal = new Vector3(HalfUtils.Unpack(reader.ReadUShort()), HalfUtils.Unpack(reader.ReadUShort()), HalfUtils.Unpack(reader.ReadUShort())); break;
                                    case VertexElementFormat.Half4: vertex.Normal = new Vector3(HalfUtils.Unpack(reader.ReadUShort()), HalfUtils.Unpack(reader.ReadUShort()), HalfUtils.Unpack(reader.ReadUShort())); reader.ReadUShort(); break;
                                }
                            }
                            //else if (element.Usage == VertexElementUsage.Tangent)
                            //{
                            //    switch (element.Format)
                            //    {
                            //        case VertexElementFormat.Float3: vertex.Tangent = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()); break;
                            //        case VertexElementFormat.Float4: vertex.Tangent = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()); reader.ReadFloat(); break;
                            //        case VertexElementFormat.Half3: vertex.Tangent = new Vector3(HalfUtils.Unpack(reader.ReadUShort()), HalfUtils.Unpack(reader.ReadUShort()), HalfUtils.Unpack(reader.ReadUShort())); break;
                            //        case VertexElementFormat.Half4: vertex.Tangent = new Vector3(HalfUtils.Unpack(reader.ReadUShort()), HalfUtils.Unpack(reader.ReadUShort()), HalfUtils.Unpack(reader.ReadUShort())); reader.ReadUShort(); break;
                            //    }
                            //}
                            //else if (element.Usage == VertexElementUsage.Binormal)
                            //{
                            //    switch (element.Format)
                            //    {
                            //        case VertexElementFormat.Float3: vertex.Binormal = new Vector4(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(), 1.0f); break;
                            //        case VertexElementFormat.Float4: vertex.Binormal = new Vector4(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()); break;
                            //        case VertexElementFormat.Half3: vertex.Binormal = new Vector4(HalfUtils.Unpack(reader.ReadUShort()), HalfUtils.Unpack(reader.ReadUShort()), HalfUtils.Unpack(reader.ReadUShort()), 1.0f); break;
                            //        case VertexElementFormat.Half4: vertex.Binormal = new Vector4(HalfUtils.Unpack(reader.ReadUShort()), HalfUtils.Unpack(reader.ReadUShort()), HalfUtils.Unpack(reader.ReadUShort()), HalfUtils.Unpack(reader.ReadUShort())); break;
                            //    }
                            //}
                            //else if (element.Usage == VertexElementUsage.BinormalSign)
                            //{
                            //    switch (element.Format)
                            //    {
                            //        case VertexElementFormat.Half: vertex.Binormal = new Vector4(vertex.Binormal.X, vertex.Binormal.Y, vertex.Binormal.Z, HalfUtils.Unpack(reader.ReadUShort())); break;
                            //        case VertexElementFormat.Float: vertex.Binormal = new Vector4(vertex.Binormal.X, vertex.Binormal.Y, vertex.Binormal.Z, reader.ReadFloat()); break;
                            //    }
                            //}
                            else
                            {
                                reader.Position += element.Size;
                            }
                        }

                        currentStride += element.Size;
                    }

                    if (isHitProxy)
                    {
                        HitProxyVertices.Add(vertex.Position);
                    }

                    vertices[v] = vertex;

                    if (currentStride < stream.VertexStride)
                        reader.Position += (stream.VertexStride - currentStride);
                }

                totalStride += stream.VertexStride;
            }
        }

        int size = (int)(Utilities.SizeOf<MeshVertex>() * section.VertexCount);
        using (DataStream stream = new DataStream(size, false, true))
        {
            stream.WriteRange<MeshVertex>(vertices);
            stream.Position = 0;

            VertexBuffer = new D3D11.Buffer(state.Device, stream, size, D3D11.ResourceUsage.Default, D3D11.BindFlags.VertexBuffer, D3D11.CpuAccessFlags.None, D3D11.ResourceOptionFlags.None, 0);
            VertexBufferBinding = new D3D11.VertexBufferBinding(VertexBuffer, VertexStride, 0);
        }
    }

    public void Dispose()
    {
        VertexBuffer.Dispose();
    }

    public void Render(D3D11.DeviceContext context, MeshRenderPath renderPath)
    {

        context.InputAssembler.PrimitiveTopology = PrimitiveType;
        context.InputAssembler.SetVertexBuffers(0, VertexBufferBinding);

        context.DrawIndexed(PrimitiveCount * 3, StartIndex, 0);
    }
}

public class MeshLodRenderable : BaseRenderable
{
    public List<MeshSectionRenderable> Sections { get; private set; } = [];

    private D3D11.Buffer indexBuffer;
    private SharpDX.DXGI.Format indexBufferFormat;

    private List<int> hitProxyIndices = [];

    public MeshLodRenderable(RenderCreateState state, MeshSetLod lod, bool isHitProxy)
    {
        byte[] chunkData = GetChunkData(lod);
        using (DataStream chunkStream = new DataStream((int)lod.IndexBufferSize, false, true))
        {
            chunkStream.Write(chunkData, (int)lod.VertexBufferSize, (int)lod.IndexBufferSize);
            chunkStream.Position = 0;

            indexBuffer = new D3D11.Buffer(state.Device, chunkStream, (int)lod.IndexBufferSize, D3D11.ResourceUsage.Default, D3D11.BindFlags.IndexBuffer, D3D11.CpuAccessFlags.None, D3D11.ResourceOptionFlags.None, lod.IndexUnitSize / 8);
            if (isHitProxy)
            {
                using (NativeReader reader = new NativeReader(new MemoryStream(chunkData)))
                {
                    reader.Position = lod.VertexBufferSize;
                    while (reader.Position < reader.Length)
                    {
                        int index = (lod.IndexUnitSize == 16)
                            ? reader.ReadUShort()
                            : reader.ReadInt();

                        hitProxyIndices.Add(index);
                    }
                }
            }
        }

        indexBufferFormat = (lod.IndexUnitSize == 16)
            ? SharpDX.DXGI.Format.R16_UInt
            : SharpDX.DXGI.Format.R32_UInt;

        foreach (MeshSetSection section in lod.Sections)
        {
            if (!lod.IsSectionRenderable(section))
                continue;

            if (section.VertexCount == 0)
                continue;

            MeshSectionRenderable renderSection = new MeshSectionRenderable(state, section, chunkData, isHitProxy);
            Sections.Add(renderSection);
        }
    }

    private byte[] GetChunkData(MeshSetLod lod)
    {
        ChunkAssetEntry entry = App.AssetManager.GetChunkEntry(lod.ChunkId);
        byte[] chunkData = lod.InlineData;
        if (entry != null)
            chunkData = new NativeReader(App.AssetManager.GetChunk(entry)).ReadToEnd();
        return chunkData;
    }

    public bool HitTest(Matrix transform, Ray hitTestRay, out Vector3 hitLocation)
    {
        foreach (MeshSectionRenderable section in Sections)
        {
            for (int i = 0; i < section.PrimitiveCount; i++)
            {
                Vector3 vertex1 = section.HitProxyVertices[hitProxyIndices[section.StartIndex + ((i * 3) + 0)]];
                Vector3 vertex2 = section.HitProxyVertices[hitProxyIndices[section.StartIndex + ((i * 3) + 1)]];
                Vector3 vertex3 = section.HitProxyVertices[hitProxyIndices[section.StartIndex + ((i * 3) + 2)]];
                vertex1 = Vector3.TransformCoordinate(vertex1, transform);
                vertex2 = Vector3.TransformCoordinate(vertex2, transform);
                vertex3 = Vector3.TransformCoordinate(vertex3, transform);
                bool intersects = hitTestRay.Intersects(ref vertex1, ref vertex2, ref vertex3, out hitLocation);
                if (intersects)
                {
                    return true;
                }
            }
        }

        hitLocation = Vector3.Zero;
        return false;
    }

    public void Dispose()
    {
        foreach (MeshSectionRenderable section in Sections)
            section.Dispose();

        indexBuffer.Dispose();
    }

    public override void Render(D3D11.DeviceContext context, MeshRenderPath renderPath)
    {
        context.InputAssembler.SetIndexBuffer(indexBuffer, indexBufferFormat, 0);
        foreach (MeshSectionRenderable section in Sections)
            section.Render(context, renderPath);
    }
}

public class MeshRenderable : BaseRenderable
{
    public BoundingBox Bounds { get; private set; }
    public List<MeshLodRenderable> Lods { get; private set; } = [];
    public float[] LodDistances { get; private set; } = new float[6];
    public float CullScreenArea { get; private set; } = 0.0f;

    /// <summary>
    /// 当材质加载成功时，使用 MeshSetPlugin 的带贴图渲染
    /// </summary>
    public MeshRenderMesh TexturedMesh { get; private set; }
    public MeshMaterialCollection Materials { get; private set; }

    public MeshRenderable(RenderCreateState state, MeshSet meshSet, FrostySdk.Ebx.MeshLodGroup lodGroup,
        MeshMaterialCollection materials = null, ResAssetEntry meshResEntry = null)
    {
        LodDistances[0] = lodGroup.Lod1Distance;
        LodDistances[1] = lodGroup.Lod2Distance;
        LodDistances[2] = lodGroup.Lod3Distance;
        LodDistances[3] = lodGroup.Lod4Distance;
        LodDistances[4] = lodGroup.Lod5Distance;
        LodDistances[5] = lodGroup.Lod6Distance;
        CullScreenArea = lodGroup.CullScreenArea;

        bool isHitProxyLod = true;
        foreach (MeshSetLod lod in meshSet.Lods)
        {
            MeshLodRenderable renderLod = new MeshLodRenderable(state, lod, isHitProxyLod);
            Lods.Add(renderLod);

            isHitProxyLod = false;
        }

        Bounds = new BoundingBox(
            new Vector3(meshSet.BoundingBox.min.x, meshSet.BoundingBox.min.y, meshSet.BoundingBox.min.z),
            new Vector3(meshSet.BoundingBox.max.x, meshSet.BoundingBox.max.y, meshSet.BoundingBox.max.z)
            );

        if (materials != null && materials.Count > 0 && meshResEntry != null)
        {
            try
            {
                MeshSetPlugin.Resources.MeshSet texturedMeshSet = App.AssetManager.GetResAs<MeshSetPlugin.Resources.MeshSet>(meshResEntry);
                if (texturedMeshSet != null)
                {
                    Materials = materials;
                    MeshRenderSkeleton skeleton = new MeshRenderSkeleton();
                    TexturedMesh = new MeshRenderMesh(state, texturedMeshSet, materials, skeleton);
                    VirtualTexturePage.Collect(TexturedMesh, materials);
                }
            }
            catch
            {
                TexturedMesh = null;
            }
        }
    }

    public bool UseTexturedRendering => TexturedMesh != null;

    /// <summary>
    /// 是否实际使用带贴图渲染（需同时满足：有 TexturedMesh 且用户选项开启）
    /// </summary>
    public bool ShouldUseTexturedRendering =>
        UseTexturedRendering && LevelEditorRenderingOptions.UseTexturedModelRendering;

    public object GetLod(int index)
    {
        if (ShouldUseTexturedRendering)
        {
            return TexturedMesh.GetLod(index);
        }
        if (index >= Lods.Count)
            return Lods[Lods.Count - 1];
        return Lods[index];
    }

    public void Dispose()
    {
        TexturedMesh?.Dispose();
        TexturedMesh = null;
        foreach (MeshLodRenderable lod in Lods)
            lod.Dispose();
    }
}
