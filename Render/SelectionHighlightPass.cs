using Frosty.Core;
using Frosty.Core.Screens;
using Frosty.Core.Viewport;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.DXGI;
using System;
using System.IO;
using System.Runtime.InteropServices;
using D3D11 = SharpDX.Direct3D11;

namespace LevelEditorPlugin.Render;

/// <summary>
/// 关卡编辑器里被选中物体的屏幕空间描边高亮。
///
/// Frosty 自带的 SelectionOutline 着色器拿“两侧世界坐标深度差 / 10000”判断轮廓，
/// 而关卡编辑器的相机远裁剪面只有 1000，这个阈值永远不会触发，所以自带的描边在
/// Default Lit 下实际上是不显示的（见 Content/Shaders/SelectionHighlight.hlsl 的说明）。
///
/// 这里用一个运行时编译的全屏后处理代替它：读取选中遮罩 + 场景深度，在物体轮廓上
/// 画一圈实线、外面压一圈暗边、再补一点柔光和内部淡染色，和相机距离、关卡坐标都无关。
/// </summary>
public sealed class SelectionHighlightPass : IDisposable
{
    private const string ShaderFileName = "SelectionHighlight.hlsl";

    /// <summary>描边颜色（琥珀色，和自带的黄色选中色同一色系，但在沙地/雪地上更明显）。</summary>
    public Vector4 OutlineColor = new(1.0f, 0.62f, 0.05f, 1.0f);

    /// <summary>内部淡染色，a 为强度；整块区域轻微变色，快速移动视角时也能看清选中了什么。</summary>
    public Vector4 FillColor = new(1.0f, 0.75f, 0.25f, 0.10f);

    /// <summary>描边采样半径（像素），1 表示 1 像素采样、实线约 2 像素宽。</summary>
    public float RadiusPixels = 1.0f;

    /// <summary>
    /// 遮挡判断的深度容差。选中遮罩和场景深度是同一份几何，正常情况下深度完全相等；
    /// 这个容差只用来吸收 TXAA 抖动 / 浮点误差。
    /// </summary>
    public float DepthTolerance = 0.0005f;

    /// <summary>轮廓外侧柔光强度。</summary>
    public float GlowStrength = 0.35f;

    [StructLayout(LayoutKind.Sequential)]
    private struct Constants
    {
        public Vector4 OutlineColor;
        public Vector4 FillColor;
        public Vector4 Params;
        public Vector4 ScreenParams;
    }

    private readonly D3D11.Device device;
    private D3D11.PixelShader pixelShader;
    private ConstantBuffer<Constants> constantBuffer;
    private D3D11.BlendState blendState;
    private D3D11.RasterizerState rasterizerState;
    private D3D11.DepthStencilState depthState;
    private D3D11.SamplerState samplerState;

    // 选择遮罩的 SRV 自己建：基类给它的 SRV 声明成了 Texture2DMultisampled，
    // 而这张纹理其实是单采样的，按 Texture2D 取样才可靠。视口尺寸变化时纹理会重建，
    // 所以这里跟着纹理引用走。
    private D3D11.Texture2D selectionDepthSource;
    private D3D11.ShaderResourceView selectionDepthView;

    /// <summary>着色器编译成功、可以出图。</summary>
    public bool IsReady { get; private set; }

    public bool IsDisposed { get; private set; }

    public SelectionHighlightPass(D3D11.Device inDevice)
    {
        device = inDevice;

        if (!TryLoadShaderSource(out string source))
        {
            App.Logger?.LogError($"选中高亮：找不到着色器 {ShaderFileName}，将退回自带的 SelectionOutline。");
            return;
        }

        try
        {
            using (CompilationResult result = ShaderBytecode.Compile(source, "PS_Main", "ps_5_0"))
            {
                if (result.HasErrors || result.Bytecode == null)
                {
                    App.Logger?.LogError($"选中高亮：着色器编译失败：{result.Message}");
                    return;
                }

                pixelShader = new D3D11.PixelShader(device, result.Bytecode);
            }
        }
        catch (Exception e)
        {
            App.Logger?.LogError($"选中高亮：着色器创建失败：{e.Message}");
            return;
        }

        constantBuffer = new ConstantBuffer<Constants>(device, new Constants());
        // 这些状态由 FrostyDeviceManager 缓存（GetOrCreateXxxState），不要释放。
        blendState = D3DUtils.CreateBlendState(D3DUtils.CreateBlendStateRenderTarget());
        rasterizerState = D3DUtils.CreateRasterizerState(D3D11.CullMode.None);
        depthState = D3DUtils.CreateDepthStencilState(false);
        samplerState = D3DUtils.CreateSamplerState(address: D3D11.TextureAddressMode.Clamp, filter: D3D11.Filter.MinMagMipPoint);

        IsReady = true;
    }

    /// <summary>
    /// 把“场景颜色 + 选中高亮”写进 <paramref name="target"/>（selectionOutlineTexture），
    /// 参数与自带的 PostProcessSelectionOutline 一致，调用方只需要额外给一个场景深度。
    /// </summary>
    public void Render(
        D3D11.DeviceContext context,
        D3D11.VertexShader fullscreenQuad,
        D3D11.Buffer fullscreenViewConstants,
        BindableTexture target,
        D3D11.ShaderResourceView sceneColor,
        BindableDepthTexture selectionDepth,
        D3D11.ShaderResourceView sceneDepth,
        int width,
        int height)
    {
        if (!IsReady || IsDisposed || target == null || width <= 0 || height <= 0)
            return;

        D3D11.ShaderResourceView selectionDepthSrv = GetSelectionDepthView(selectionDepth);
        if (selectionDepthSrv == null || sceneColor == null || sceneDepth == null)
            return;

        Constants constants = new()
        {
            OutlineColor = OutlineColor,
            FillColor = FillColor,
            Params = new Vector4(RadiusPixels, DepthTolerance, GlowStrength, FillColor.W > 0.0f ? 1.0f : 0.0f),
            ScreenParams = new Vector4(1.0f / width, 1.0f / height, width, height)
        };
        constantBuffer.UpdateData(context, constants);

        SharpDX.Mathematics.Interop.RawViewportF[] savedViewports =
            context.Rasterizer.GetViewports<SharpDX.Mathematics.Interop.RawViewportF>();

        D3DUtils.BeginPerfEvent(context, "SelectionHighlight");
        {
            context.OutputMerger.SetRenderTargets(null, target.RTV);
            context.OutputMerger.SetDepthStencilState(depthState);
            context.OutputMerger.SetBlendState(blendState);
            context.Rasterizer.State = rasterizerState;
            context.Rasterizer.SetViewport(new Viewport(0, 0, width, height));

            context.InputAssembler.SetIndexBuffer(null, Format.Unknown, 0);
            context.InputAssembler.SetVertexBuffers(0, new D3D11.VertexBufferBinding());
            context.InputAssembler.InputLayout = null;

            context.VertexShader.Set(fullscreenQuad);
            context.VertexShader.SetConstantBuffer(0, fullscreenViewConstants);

            context.PixelShader.Set(pixelShader);
            context.PixelShader.SetConstantBuffer(0, constantBuffer.Buffer);
            context.PixelShader.SetShaderResources(0, sceneColor, selectionDepthSrv, sceneDepth);
            context.PixelShader.SetSampler(0, samplerState);

            context.Draw(6, 0);

            context.Rasterizer.SetViewports(savedViewports);
        }
        D3DUtils.EndPerfEvent(context);
    }

    /// <summary>
    /// 为选择遮罩纹理建一个普通的 Texture2D 视口。基类的 SRV 用了
    /// Texture2DMultisampled 维度，而纹理是单采样的，着色器里按 Texture2D 取样会和
    /// 资源类型对不上，所以这里自己建一份（纹理重建时自动跟随）。
    /// </summary>
    private D3D11.ShaderResourceView GetSelectionDepthView(BindableDepthTexture texture)
    {
        D3D11.Texture2D source = texture?.Texture;
        if (source == null || source.IsDisposed)
            return null;

        if (ReferenceEquals(source, selectionDepthSource) && selectionDepthView != null && !selectionDepthView.IsDisposed)
            return selectionDepthView;

        selectionDepthView?.Dispose();
        selectionDepthView = null;
        selectionDepthSource = source;

        try
        {
            selectionDepthView = new D3D11.ShaderResourceView(device, source, new D3D11.ShaderResourceViewDescription()
            {
                Dimension = SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D,
                Format = Format.R24_UNorm_X8_Typeless,
                Texture2D = new D3D11.ShaderResourceViewDescription.Texture2DResource()
                {
                    MipLevels = 1,
                    MostDetailedMip = 0
                }
            });
        }
        catch (Exception e)
        {
            App.Logger?.LogError($"选中高亮：创建选择遮罩 SRV 失败：{e.Message}");
            selectionDepthSource = null;
        }

        return selectionDepthView;
    }

    private static bool TryLoadShaderSource(out string source)
    {
        string path = Path.Combine(ContentPaths.Root ?? string.Empty, "Shaders", ShaderFileName);
        try
        {
            if (File.Exists(path))
            {
                source = File.ReadAllText(path);
                return true;
            }

            App.Logger?.LogError($"选中高亮：着色器文件不存在：{path}");
        }
        catch (Exception e)
        {
            App.Logger?.LogError($"选中高亮：读取着色器失败：{e.Message}");
        }

        source = null;
        return false;
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        IsReady = false;

        pixelShader?.Dispose();
        pixelShader = null;
        constantBuffer?.Dispose();
        constantBuffer = null;

        selectionDepthView?.Dispose();
        selectionDepthView = null;
        selectionDepthSource = null;
    }
}
