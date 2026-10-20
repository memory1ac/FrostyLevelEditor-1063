using Frosty.Core.Viewport;
using FrostySdk.Ebx;
using LevelEditorPlugin.Editors;
using LevelEditorPlugin.Managers;
using LevelEditorPlugin.Render.Proxies;
using SharpDX;
using System.Collections.Generic;
using System.Reflection;

namespace LevelEditorPlugin.Entities;

[EntityBinding("BreakableModelEntityData", BaseTypeName = "GamePhysicsEntityData")]
public class BreakableModelEntity : GamePhysicsEntity
{
    public Assets.MeshAsset Mesh { get; private set; }

    public BreakableModelEntity(GameObjectData inData, Entity inParent)
        : base((GamePhysicsEntityData)inData, inParent)
    {
        Mesh = LoadMesh(this, inData);
    }

    public override void CreateRenderProxy(List<RenderProxy> proxies, RenderCreateState state)
    {
        if (AddMeshProxy(this, Mesh, proxies, state))
            SetFlags(EntityFlags.RenderProxyGenerated);
    }

    public override void Destroy()
    {
        if (Mesh != null)
            LoadedAssetManager.Instance.UnloadAsset(Mesh);
    }

    internal static bool AddMeshProxy(Entity owner, Assets.MeshAsset mesh, List<RenderProxy> proxies, RenderCreateState state)
    {
        if (mesh == null)
            return false;

        mesh.LoadResource(state);
        if (mesh.MeshData == null)
            return false;

        proxies.Add(new ModelRenderProxy(state, (ISpatialEntity)owner, mesh.MeshData));
        return true;
    }

    internal static Assets.MeshAsset LoadMesh(Entity owner, object data)
    {
        PropertyInfo meshProperty = data.GetType().GetProperty("Mesh");
        if (meshProperty?.GetValue(data) is not PointerRef pointer)
            return null;

        return LoadedAssetManager.Instance.LoadAsset<Assets.MeshAsset>(owner, pointer);
    }
}

[EntityBinding("DynamicModelEntityData", BaseTypeName = "GamePhysicsEntityData")]
public class DynamicModelEntity : GamePhysicsEntity
{
    public Assets.MeshAsset Mesh { get; private set; }

    public DynamicModelEntity(GameObjectData inData, Entity inParent)
        : base((GamePhysicsEntityData)inData, inParent)
    {
        Mesh = BreakableModelEntity.LoadMesh(this, inData);
    }

    public override void CreateRenderProxy(List<RenderProxy> proxies, RenderCreateState state)
    {
        if (BreakableModelEntity.AddMeshProxy(this, Mesh, proxies, state))
            SetFlags(EntityFlags.RenderProxyGenerated);
    }

    public override void Destroy()
    {
        if (Mesh != null)
            LoadedAssetManager.Instance.UnloadAsset(Mesh);
    }
}

[EntityBinding("BangerEntityData", BaseTypeName = "GamePhysicsEntityData")]
public class BangerEntity : GamePhysicsEntity
{
    public Assets.MeshAsset Mesh { get; private set; }

    public BangerEntity(GameObjectData inData, Entity inParent)
        : base((GamePhysicsEntityData)inData, inParent)
    {
        Mesh = BreakableModelEntity.LoadMesh(this, inData);
    }

    public override void CreateRenderProxy(List<RenderProxy> proxies, RenderCreateState state)
    {
        if (BreakableModelEntity.AddMeshProxy(this, Mesh, proxies, state))
            SetFlags(EntityFlags.RenderProxyGenerated);
    }

    public override void Destroy()
    {
        if (Mesh != null)
            LoadedAssetManager.Instance.UnloadAsset(Mesh);
    }
}

[EntityBinding("DebrisClusterData", BaseTypeName = "GameComponentEntityData")]
public class DebrisCluster : GameComponentEntity
{
    public Assets.MeshAsset Mesh { get; private set; }

    public DebrisCluster(GameObjectData inData, Entity inParent)
        : base((GameComponentEntityData)inData, inParent)
    {
        Mesh = BreakableModelEntity.LoadMesh(this, inData);
    }

    public override void CreateRenderProxy(List<RenderProxy> proxies, RenderCreateState state)
    {
        if (BreakableModelEntity.AddMeshProxy(this, Mesh, proxies, state))
            SetFlags(EntityFlags.RenderProxyGenerated);
    }

    public override void Destroy()
    {
        if (Mesh != null)
            LoadedAssetManager.Instance.UnloadAsset(Mesh);
    }
}

[EntityBinding("OBBCollisionEntityData", BaseTypeName = "SpatialEntityData")]
public class OBBCollisionEntity : SpatialEntity
{
    public Render.ObjRenderable SpriteData { get; private set; }

    public override string DisplayName => "OBBCollisionEntity";

    public OBBCollisionEntity(GameObjectData inData, Entity inParent)
        : base((SpatialEntityData)inData, inParent)
    {
    }

    public override void CreateRenderProxy(List<RenderProxy> proxies, RenderCreateState state)
    {
        SpriteData = LoadedMeshManager.Instance.LoadMesh(state, "Sprite");
        SpriteRenderProxy spriteRenderProxy = new SpriteRenderProxy(state, this, SpriteData, "Location");
        spriteRenderProxy.SetMeshCubeData(state, ReadHalfExtents(Data));
        proxies.Add(spriteRenderProxy);
        SetFlags(EntityFlags.RenderProxyGenerated);
    }

    public override void Destroy()
    {
        LoadedMeshManager.Instance.UnloadMesh(SpriteData);
    }

    private static Vector3 ReadHalfExtents(object data)
    {
        object extents = data.GetType().GetProperty("HalfExtents")?.GetValue(data);
        if (extents == null)
            return Vector3.One;

        float x = ReadFloat(extents, "x");
        float y = ReadFloat(extents, "y");
        float z = ReadFloat(extents, "z");
        return new Vector3(x, y, z);
    }

    private static float ReadFloat(object value, string name)
    {
        object raw = value.GetType().GetField(name)?.GetValue(value) ?? value.GetType().GetProperty(name)?.GetValue(value);
        return raw == null ? 1f : System.Convert.ToSingle(raw);
    }
}

[EntityBinding("AlternateSpawnEntityData", BaseTypeName = "SpatialEntityData")]
public class AlternateSpawnEntity : SpatialEntity
{
    public Render.ObjRenderable SpriteData { get; private set; }

    public override string DisplayName => "AlternateSpawnEntity";

    public AlternateSpawnEntity(GameObjectData inData, Entity inParent)
        : base((SpatialEntityData)inData, inParent)
    {
    }

    public override void CreateRenderProxy(List<RenderProxy> proxies, RenderCreateState state)
    {
        SpriteData = LoadedMeshManager.Instance.LoadMesh(state, "Sprite");
        string team = Data.GetType().GetProperty("Team")?.GetValue(Data)?.ToString() ?? "";
        bool teamOne = team.Contains("Team1");
        string sprite = teamOne ? "SoldierSpawn1" : "SoldierSpawn2";
        string mesh = teamOne
            ? "gameplay/characters/soldiers/lowreschar_stand_Mesh"
            : "gameplay/characters/soldiers/lowreschar_crouch_Mesh";

        SpriteRenderProxy spriteRenderProxy = new SpriteRenderProxy(state, this, SpriteData, sprite);
        spriteRenderProxy.SetMeshResPath(state, mesh);
        proxies.Add(spriteRenderProxy);
        SetFlags(EntityFlags.RenderProxyGenerated);
    }

    public override void Destroy()
    {
        LoadedMeshManager.Instance.UnloadMesh(SpriteData);
    }
}

[EntityBinding("CameraEntityData", BaseTypeName = "SpatialEntityData")]
public class CameraEntity : SpatialEntity
{
    public Render.ObjRenderable SpriteData { get; private set; }

    public override string DisplayName => "CameraEntity";

    public CameraEntity(GameObjectData inData, Entity inParent)
        : base((SpatialEntityData)inData, inParent)
    {
    }

    public override void CreateRenderProxy(List<RenderProxy> proxies, RenderCreateState state)
    {
        SpriteData = LoadedMeshManager.Instance.LoadMesh(state, "Sprite");
        SpriteRenderProxy spriteRenderProxy = new SpriteRenderProxy(state, this, SpriteData, "PointLight");
        spriteRenderProxy.SetMeshResPath(state, "xpack1-3/artassets/props/lamptripod_01/lamptripodhead_01_Mesh");
        proxies.Add(spriteRenderProxy);
        SetFlags(EntityFlags.RenderProxyGenerated);
    }

    public override void Destroy()
    {
        LoadedMeshManager.Instance.UnloadMesh(SpriteData);
    }
}

[EntityBinding("VehicleSpawnReferenceObjectData", BaseTypeName = "SpatialReferenceObjectData")]
public class VehicleSpawnReferenceObject : SpatialReferenceObject
{
    public Render.ObjRenderable SpriteData { get; private set; }

    public VehicleSpawnReferenceObject(GameObjectData inData, Entity inParent)
        : base((SpatialReferenceObjectData)inData, inParent)
    {
    }

    public VehicleSpawnReferenceObject(GameObjectData inData, Entity inParent, EntityWorld inWorld)
        : base((SpatialReferenceObjectData)inData, inParent, inWorld)
    {
    }

    public override void CreateRenderProxy(List<RenderProxy> proxies, RenderCreateState state)
    {
        SpriteData = LoadedMeshManager.Instance.LoadMesh(state, "Sprite");
        string ownerDisplayName = owner.DisplayName ?? "";
        string sprite;
        string mesh;
        if (ownerDisplayName.StartsWith("SpawnerPrefab_Tanker"))
        {
            sprite = "VehicleSpawnTanker";
            mesh = "gameplay/vehicles/gbr_markv/gbr_markv_lowpoly_Mesh";
        }
        else if (ownerDisplayName.StartsWith("SpawnerPrefab_Pilot"))
        {
            sprite = "VehicleSpawnPilot";
            mesh = "fx/mesh/specific/vehicles/airplanes/meshp_vehicle_fokkerdr1_Mesh";
        }
        else if (ownerDisplayName.StartsWith("SpawnerPrefab_Cavalryman"))
        {
            sprite = "VehicleSpawnRider";
            mesh = "gameplay/vehicles/usa_harleydavidson_18j/usa_harleydavidson_18j_wreck_Mesh";
        }
        else if (ownerDisplayName.StartsWith("SpawnerPrefab_CoastalAirship"))
        {
            sprite = "VehicleSpawnPilot";
            mesh = "xpack3/gameplay/vehicles/ger_halberstadt_clii_staticwreck/da_ger_halberstadt_clii_staticwreck_Mesh";
        }
        else if (ownerDisplayName.StartsWith("SpawnerPrefab_Destroyer"))
        {
            sprite = "VehicleSpawnTanker";
            mesh = "gameplay/vehicles/ita_mas/ita_mas_wreck_Mesh";
        }
        else
        {
            sprite = "VehicleSpawnTanker";
            mesh = "gameplay/vehicles/ger_fk96/ger_fk_96_wreck_meshproxy_Mesh";
        }

        SpriteRenderProxy spriteRenderProxy = new SpriteRenderProxy(state, this, SpriteData, sprite);
        spriteRenderProxy.SetMeshResPath(state, mesh);
        proxies.Add(spriteRenderProxy);
        SetFlags(EntityFlags.RenderProxyGenerated);
    }

    public override void Destroy()
    {
        LoadedMeshManager.Instance.UnloadMesh(SpriteData);
    }
}
