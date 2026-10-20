using Frosty.Core.Screens;
using Frosty.Core.Viewport;
using FrostySdk;
using LevelEditorPlugin.Entities;
using SharpDX;
using System.Collections.Generic;
using D3D11 = SharpDX.Direct3D11;

namespace LevelEditorPlugin.Render.Proxies;

/// <summary>
/// 包装通过 SetMeshResPath 渲染的 mesh：非纯色模式下用 LevelShader 避免显示异常；
/// 纯色模式下与正常模型一致，由管线用共享 LevelShader 绘制裸 mesh，从而随 view mode 切换渲染管线。
/// </summary>
public class SolidColorMeshWrapper : MeshRenderBase
{
    private readonly MeshRenderBase _inner;
    private readonly ShaderPermutation _permutation;
    private D3D11.Buffer _pixelParameters;
    private List<D3D11.ShaderResourceView> _pixelTextures = [];

    private static GeometryDeclarationDesc GeometryDecl => GeometryDeclarationDesc.Create(new GeometryDeclarationDesc.Element[]
    {
        new GeometryDeclarationDesc.Element { Usage = VertexElementUsage.Pos, Format = VertexElementFormat.Float3 },
        new GeometryDeclarationDesc.Element { Usage = VertexElementUsage.Normal, Format = VertexElementFormat.Float3 }
    });

    /// <summary>纯色模式下绘制裸 MeshLodRenderable 时使用的共享 LevelShader 状态</summary>
    private static ShaderPermutation s_sharedPermutation;
    private static D3D11.Buffer s_sharedPixelParameters;
    private static List<D3D11.ShaderResourceView> s_sharedPixelTextures = [];

    /// <summary>在关闭 level 时释放静态共享状态，避免第二次打开时 DeviceRemoved</summary>
    public static void DisposeSharedState()
    {
        s_sharedPixelParameters?.Dispose();
        s_sharedPixelParameters = null;
        s_sharedPixelTextures?.Clear();
        s_sharedPermutation?.Dispose();
        s_sharedPermutation = null;
    }

    public override string DebugName => "SolidColorMesh";

    public SolidColorMeshWrapper(RenderCreateState state, Entity owner, MeshRenderBase innerMesh)
    {
        _inner = innerMesh;

        var color = owner?.Owner?.Layer?.LayerColor;
        var r = color?.Red ?? 1.0f;
        var g = color?.Green ?? 1.0f;
        var b = color?.Blue ?? 1.0f;

        var material = new MeshMaterial();
        material.VectorParameters.Add(new FrostySdk.Ebx.VectorShaderParameter
        {
            ParameterName = "Color",
            ParameterType = FrostySdk.Ebx.ShaderParameterType.ShaderParameterType_Vec4,
            Value = new FrostySdk.Ebx.Vec4 { x = r, y = g, z = b, w = 1.0f }
        });

        _permutation = state.ShaderLibrary.GetUserShader("LevelShader", GeometryDecl);
        _permutation.IsTwoSided = true;
        _permutation.LoadShaders(state.Device);
        _permutation.AssignParameters(state, material, ref _pixelParameters, ref _pixelTextures);

        EnsureSharedState(state);
    }

    /// <summary>确保共享 LevelShader 状态已创建（纯色模式下绘制裸 mesh 时使用）</summary>
    public static void EnsureSharedState(RenderCreateState state)
    {
        if (s_sharedPermutation != null)
            return;

        var material = new MeshMaterial();
        material.VectorParameters.Add(new FrostySdk.Ebx.VectorShaderParameter
        {
            ParameterName = "Color",
            ParameterType = FrostySdk.Ebx.ShaderParameterType.ShaderParameterType_Vec4,
            Value = new FrostySdk.Ebx.Vec4 { x = 1.0f, y = 1.0f, z = 1.0f, w = 1.0f }
        });

        s_sharedPermutation = state.ShaderLibrary.GetUserShader("LevelShader", GeometryDecl);
        s_sharedPermutation.IsTwoSided = true;
        s_sharedPermutation.LoadShaders(state.Device);
        s_sharedPermutation.AssignParameters(state, material, ref s_sharedPixelParameters, ref s_sharedPixelTextures);
    }

    /// <summary>设置共享 LevelShader 状态（纯色模式下在绘制裸 MeshLodRenderable 前调用）</summary>
    public static void SetSharedShaderState(D3D11.DeviceContext context, MeshRenderPath renderPath)
    {
        if (renderPath != MeshRenderPath.Deferred || s_sharedPermutation == null)
            return;
        s_sharedPermutation.SetState(context, renderPath);
        context.PixelShader.SetShaderResources(1, s_sharedPixelTextures.ToArray());
        context.PixelShader.SetConstantBuffer(2, s_sharedPixelParameters);
    }

    public override void Render(D3D11.DeviceContext context, MeshRenderPath renderPath)
    {
        if (renderPath != MeshRenderPath.Deferred)
            return;

        _permutation.SetState(context, renderPath);
        context.PixelShader.SetShaderResources(1, _pixelTextures.ToArray());
        context.PixelShader.SetConstantBuffer(2, _pixelParameters);

        _inner.Render(context, renderPath);
    }

    public void Dispose()
    {
        _pixelParameters?.Dispose();
        _pixelParameters = null;
        _pixelTextures.Clear();
        _permutation?.Dispose();
    }
}
