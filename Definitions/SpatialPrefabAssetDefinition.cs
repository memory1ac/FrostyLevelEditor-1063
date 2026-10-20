using Frosty.Controls;
using Frosty.Core.Controls;
using System.Windows.Media;

namespace LevelEditorPlugin.Definitions;

public class SpatialPrefabAssetDefinition : BaseAssetDefinition
{
    protected override ImageSource ImageIcon => Icons.SpatialBlueprintImageSource;
    protected override SvgImageData SvgIcon => Icons.SpatialBlueprintIcon;
}
