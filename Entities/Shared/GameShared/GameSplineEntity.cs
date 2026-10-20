namespace LevelEditorPlugin.Entities;

[EntityBinding(DataType = typeof(FrostySdk.Ebx.GameSplineEntityData))]
public class GameSplineEntity : SpatialEntity, IEntityData<FrostySdk.Ebx.GameSplineEntityData>
{
    public new FrostySdk.Ebx.GameSplineEntityData Data => data as FrostySdk.Ebx.GameSplineEntityData;

    public GameSplineEntity(FrostySdk.Ebx.GameSplineEntityData inData, Entity inParent)
        : base(inData, inParent)
    {
    }
}
