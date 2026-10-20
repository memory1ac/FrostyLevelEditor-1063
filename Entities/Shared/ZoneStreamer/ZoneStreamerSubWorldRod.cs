namespace LevelEditorPlugin.Entities;

#if SWBF2
[EntityBinding(DataType = typeof(FrostySdk.Ebx.ZoneStreamerSubWorldRod))]
public class ZoneStreamerSubWorldRod : SubWorldReferenceObject, IEntityData<FrostySdk.Ebx.ZoneStreamerSubWorldRod>
{
    public new FrostySdk.Ebx.ZoneStreamerSubWorldRod Data => data as FrostySdk.Ebx.ZoneStreamerSubWorldRod;

    public ZoneStreamerSubWorldRod(FrostySdk.Ebx.ZoneStreamerSubWorldRod inData, Entity inParent)
        : base(inData, inParent)
    {
    }
}
#endif
