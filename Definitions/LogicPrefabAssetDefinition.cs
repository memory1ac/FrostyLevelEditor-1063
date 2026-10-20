using Frosty.Controls;
using Frosty.Core.Controls;
using System.Windows.Media;

namespace LevelEditorPlugin.Definitions;

public class LogicPrefabAssetDefinition : BaseAssetDefinition
{
    protected override ImageSource ImageIcon => Icons.LogicBlueprintImageSource;
    protected override SvgImageData SvgIcon => Icons.LogicBlueprintIcon;
}
