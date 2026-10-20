using Frosty.Core;
using FrostySdk.Managers;
using FrostySdk.Resources;

namespace LevelEditorPlugin.Resources;

// @todo: Need to clear modification count on saving of project

public class BaseModifiedResource : ModifiedResource
{
    private int modificationCount;
    private ResAssetEntry resEntry;

    /// <summary>
    /// 无参构造函数，供 ModifiedResource.Read 反序列化时使用。
    /// </summary>
    public BaseModifiedResource()
    {
    }

    public BaseModifiedResource(ResAssetEntry inEntry)
    {
        resEntry = inEntry;
        modificationCount = 0;
    }

    public void UpdateData(Resource resource)
    {
        if (resEntry != null)
        {
            App.AssetManager.ModifyRes(resEntry.ResRid, resource);
            modificationCount++;
        }
    }

    public void UndoData(Resource resource)
    {
        if (resEntry == null)
            return;
        modificationCount--;
        if (modificationCount > 0)
        {
            App.AssetManager.ModifyRes(resEntry.ResRid, resource);
        }
        else
        {
            resEntry.ClearModifications();
        }
    }
}
