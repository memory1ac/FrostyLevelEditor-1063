namespace LevelEditorPlugin.Assets;

#if SWBF2
[AssetBinding(DataType = typeof(FrostySdk.Ebx.DetachedSubWorldData))]
public class DetachedSubWorld : SubWorld, IAssetData<FrostySdk.Ebx.DetachedSubWorldData>
{
    public new FrostySdk.Ebx.DetachedSubWorldData Data => data as FrostySdk.Ebx.DetachedSubWorldData;

    public DetachedSubWorld(Guid fileGuid, FrostySdk.Ebx.DetachedSubWorldData inData)
        : base(fileGuid, inData)
    {
    }
}
#endif
