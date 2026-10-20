using Frosty.Controls;
using Frosty.Core.Controls;
using System.Windows.Media;

namespace LevelEditorPlugin.Definitions;

public class ObjectBlueprintAssetDefinition : BaseAssetDefinition
{
    protected override ImageSource ImageIcon => Icons.ObjectBlueprintImageSource;
    protected override SvgImageData SvgIcon => Icons.ObjectBlueprintIcon;
}
