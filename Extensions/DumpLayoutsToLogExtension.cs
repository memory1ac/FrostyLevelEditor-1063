using Frosty.Core;
using LevelEditorPlugin.Managers;

namespace LevelEditorPlugin.Extensions;

public class DumpLayoutsToLogExtension : MenuExtension
{
    public override string TopLevelMenuName => "Developer";
    public override string MenuItemName => "Dump Layouts to Log";
    public override RelayCommand MenuItemClicked => new RelayCommand((o) =>
    {
        SchematicsLayoutManager.Instance.PrintLayouts(App.Logger);
    });
}
