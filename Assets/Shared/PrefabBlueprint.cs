using System;
using System.Collections.Generic;

namespace LevelEditorPlugin.Assets;

public class PrefabBlueprint : Blueprint, IAssetData<FrostySdk.Ebx.PrefabBlueprint>
{
    public new FrostySdk.Ebx.PrefabBlueprint Data => data as FrostySdk.Ebx.PrefabBlueprint;
    public List<FrostySdk.Ebx.PropertyConnection> PropertyConnections { get; protected set; } = [];
    public List<FrostySdk.Ebx.EventConnection> EventConnections { get; protected set; } = [];
    public List<FrostySdk.Ebx.LinkConnection> LinkConnections { get; protected set; } = [];

    public PrefabBlueprint(Guid fileGuid, FrostySdk.Ebx.PrefabBlueprint inData)
        : base(fileGuid, inData)
    {
    }
}
