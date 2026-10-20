using FrostySdk.Ebx;
using LevelEditorPlugin.Editors;
using System;

namespace LevelEditorPlugin.Assets;

public class Blueprint : Asset, IAssetData<FrostySdk.Ebx.Blueprint>
{
    public FrostySdk.Ebx.Blueprint Data => data as FrostySdk.Ebx.Blueprint;

    public Blueprint(Guid fileGuid, FrostySdk.Ebx.Blueprint inData)
        : base(fileGuid, inData)
    {
    }

    public virtual FrostySdk.Ebx.ReferenceObjectData CreateEntityData()
    {
        return null;
    }

    public string GetNextAvailablePropertyValue()
    {
        int index = 0;
        InterfaceDescriptorData interfaceDesc = Data.Interface.GetObjectAs<FrostySdk.Ebx.InterfaceDescriptorData>();

        while (interfaceDesc.Fields.Find(f => ((string)f.Name).Equals($"PropertyValue_{index}")) != null)
            index++;

        return $"PropertyValue_{index}";
    }
}
