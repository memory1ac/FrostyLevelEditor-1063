using Frosty.Controls;
using Frosty.Core.Controls;
using System.Windows.Media;

namespace LevelEditorPlugin.Definitions;

public class DetachedSubWorldDataAssetDefinition : BaseAssetDefinition
{
    protected override ImageSource ImageIcon => Icons.DetachedSubWorldImageSource;
    protected override SvgImageData SvgIcon => Icons.DetachedSubWorldIcon;
}
