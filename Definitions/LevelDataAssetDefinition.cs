using Frosty.Controls;
using Frosty.Core.Controls;
using System.Windows.Media;

namespace LevelEditorPlugin.Definitions;

public class LevelDataAssetDefinition : BaseAssetDefinition
{
    protected override ImageSource ImageIcon => Icons.LevelDataImageSource;
    protected override SvgImageData SvgIcon => Icons.LevelDataIcon;
}
