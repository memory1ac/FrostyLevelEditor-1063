using FrostySdk.Attributes;
using FrostySdk.IO;

namespace LevelEditorPlugin.Data;

public interface IShortcutNodeData
{
    string DisplayName { get; set; }
}

[DisplayName("ShortcutData")]
public class ShortcutParentNodeData : IShortcutNodeData
{
    [Category("Shortcut")]
    [EbxFieldMeta(EbxFieldType.String)]
    public string DisplayName { get; set; }
}

[DisplayName("ShortcutData")]
public class ShortcutChildNodeData : IShortcutNodeData
{
    [IsReadOnly]
    [Category("Shortcut")]
    [EbxFieldMeta(EbxFieldType.String)]
    public string DisplayName { get; set; }
}
