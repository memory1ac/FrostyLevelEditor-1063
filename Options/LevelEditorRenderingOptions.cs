using Frosty.Core;
using Frosty.Core.Screens;
using Frosty.Core.Viewport;
using FrostySdk;
using System.Collections.Generic;

namespace LevelEditorPlugin.Options;

/// <summary>
/// Level Editor 渲染相关选项，对应 MeshSetPlugin 的 View Mode
/// </summary>
public static class LevelEditorRenderingOptions
{
    private const string ConfigKeyViewMode = "LevelEditorViewMode";

    public static readonly IList<string> ViewModeNames =
    [
        "纯色",
        "Default Lit",
        "Wireframe",
        "BaseColor",
        "SpecularColor",
        "Normals",
        "MaterialAO",
        "Smoothness",
        "Metallic",
        "Reflectance"
    ];

    /// <summary>
    /// View Mode 选项索引：
    /// 0=纯色, 1=Default Lit, 2=Wireframe, 3=BaseColor, 4=SpecularColor, 5=Normals,
    /// 6=MaterialAO, 7=Smoothness, 8=Metallic, 9=Reflectance
    /// </summary>
    public static int ViewModeIndex
    {
        get => Config.Get<int>(ConfigKeyViewMode, 1, ConfigScope.Game);
        set
        {
            Config.Add(ConfigKeyViewMode, value, ConfigScope.Game);
            Config.Save();
        }
    }

    /// <summary>
    /// 是否使用带贴图的模型渲染。索引 0（纯色）为 false，其余为 true。
    /// </summary>
    public static bool UseTexturedModelRendering => ViewModeIndex != 0;

    /// <summary>
    /// 获取对应的 DebugRenderMode，用于 DeferredRenderScreen2
    /// </summary>
    public static DebugRenderMode GetDebugRenderMode()
    {
        return ViewModeIndex == 0
            ? DebugRenderMode.Default
            : (DebugRenderMode)(ViewModeIndex - 1);
    }
}
