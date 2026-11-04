using Frosty.Core;
using Frosty.Core.Controls;
using FrostySdk.Interfaces;
using LevelEditorPlugin.Options;
using System.Collections.Generic;
using RelayCommand = Frosty.Core.RelayCommand;

namespace LevelEditorPlugin.Editors;

/// <summary>
/// 工具栏上的「显示/隐藏 Havok 物理碰撞体」开关。
///
/// 关卡（LevelData）和 SubWorld（SpatialPrefabBlueprint）走的是两个不同的编辑器，
/// 所以开关必须同时注册到 <see cref="LevelEditor"/> 和 <see cref="SpatialPrefabEditor"/>，
/// 否则打开 subworld 时工具栏里就没有这个按钮。
/// 两个编辑器的视口都是 <see cref="Screens.LevelEditorScreen"/>，叠加层逻辑共用一份。
/// </summary>
internal static class HavokOverlayToolbar
{
    public const string Tooltip = "显示/隐藏 Havok 物理碰撞体";

    public static void Add(List<ToolbarItem> items, SpatialEditor editor, ILogger logger)
    {
        items.Add(new ToggleToolbarItem("", Tooltip, "LevelEditorPlugin/Images/Components.png",
            LevelEditorRenderingOptions.ShowHavokCollision, new RelayCommand((o) =>
            {
                bool show = !LevelEditorRenderingOptions.ShowHavokCollision;
                LevelEditorRenderingOptions.ShowHavokCollision = show;

                if (o is ToggleToolbarItem toggle)
                    toggle.IsToggled = show;

                editor.Screen?.RefreshHavokOverlay();
                logger?.Log(show
                    ? "Havok 碰撞体叠加：开（分帧解析碰撞资源，稍等片刻才全部显示）"
                    : "Havok 碰撞体叠加：关");
            })));
    }
}
