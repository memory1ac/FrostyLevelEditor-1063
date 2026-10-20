namespace LevelEditorPlugin.Entities;

#if !GW2
[EntityBinding(DataType = typeof(FrostySdk.Ebx.BlueprintSpawnReferenceObjectData))]
public class BlueprintSpawnReferenceObject : ReferenceObject, IEntityData<FrostySdk.Ebx.BlueprintSpawnReferenceObjectData>
{
    public new FrostySdk.Ebx.BlueprintSpawnReferenceObjectData Data => data as FrostySdk.Ebx.BlueprintSpawnReferenceObjectData;

    public BlueprintSpawnReferenceObject(FrostySdk.Ebx.BlueprintSpawnReferenceObjectData inData, Entity inParent)
        : base(inData, inParent)
    {
    }
}
#endif
