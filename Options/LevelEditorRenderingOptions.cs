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
    private const string ConfigKeyShowHavok = "LevelEditorShowHavokCollision";
    private const string ConfigKeyHavokPieceColors = "LevelEditorHavokPieceColors";

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
    /// 是否在关卡视口里叠加显示 Havok 物理碰撞体（和 Havok 编辑器里画的多边形一致）。
    /// 数据取自 ObjectBlueprint 引用的 HavokPhysicsData res，名字里带 cluster 的破坏碎块会被跳过。
    /// </summary>
    public static bool ShowHavokCollision
    {
        get => Config.Get<bool>(ConfigKeyShowHavok, false, ConfigScope.Game);
        set
        {
            Config.Add(ConfigKeyShowHavok, value, ConfigScope.Game);
            Config.Save();
        }
    }

    /// <summary>
    /// Havok 叠加层是否按分块着色（默认开）：同一个碰撞 res 里的每个分块各给一种稳定颜色，
    /// 便于看清复合形状的分块结构。关掉则统一成浅蓝色，和 Havok 编辑器默认外观一致。
    /// </summary>
    public static bool HavokPieceColors
    {
        get => Config.Get<bool>(ConfigKeyHavokPieceColors, true, ConfigScope.Game);
        set
        {
            Config.Add(ConfigKeyHavokPieceColors, value, ConfigScope.Game);
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
