using Frosty.Core;
using Frosty.Core.Controls;
using LevelEditorPlugin.Converters;
using LevelEditorPlugin.Options;

namespace LevelEditorPlugin.Editors;

public static class ViewModeToolbarItem
{
    public static ToolbarItem Create()
    {
        var names = LevelEditorRenderingOptions.ViewModeNames;
        int index = LevelEditorRenderingOptions.ViewModeIndex;
        if (index < 0 || index >= names.Count)
            index = 1;

        return new ComboBoxToolbarItem(
            "View Mode",
            "切换关卡编辑器视口渲染模式",
            names,
            names[index],
            new RelayCommand(o =>
            {
                if (o is string name)
                {
                    int i = IndexOf(names, name);
                    if (i >= 0)
                        LevelEditorRenderingOptions.ViewModeIndex = i;
                }
            }),
            out _,
            130.0,
            new DisplayStringConverter());
    }

    private static int IndexOf(System.Collections.Generic.IList<string> names, string name)
    {
        for (int i = 0; i < names.Count; i++)
        {
            if (names[i] == name)
                return i;
        }
        return -1;
    }
}
