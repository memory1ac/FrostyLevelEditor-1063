using Frosty.Core.Managers;
using Frosty.Core.Viewport;
using FrostySdk.Ebx;
using FrostySdk.IO;
using LevelEditorPlugin.Editors;
using LevelEditorPlugin.Managers;
using LevelEditorPlugin.Render.Proxies;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Asset = LevelEditorPlugin.Assets.Asset;
using DataField = FrostySdk.Ebx.DataField;
using ObjectBlueprint = LevelEditorPlugin.Assets.ObjectBlueprint;
using PrefabBlueprint = LevelEditorPlugin.Assets.PrefabBlueprint;

namespace LevelEditorPlugin.Entities;

[EntityBinding(DataType = typeof(FrostySdk.Ebx.ReferenceObjectData))]
public class ReferenceObject : Entity, ISpatialReferenceEntity, ILogicEntity
{
    public FrostySdk.Ebx.ReferenceObjectData Data => data as FrostySdk.Ebx.ReferenceObjectData;
    public InterfaceDescriptor InterfaceDescriptor => interfaceDescriptor;
    public override string DisplayName => (blueprint != null) ? Path.GetFileName(blueprint.Name) : base.DisplayName;
    public long UniqueId => uniqueId;
    // 使用自己的变量requiresTransformUpdate，而不是base.RequiresTransformUpdate
    // 主要是为了解决3D窗口拖动光标时，载具图标不随之移动的问题
    public override bool RequiresTransformUpdate
    {
        get => requiresTransformUpdate;
        set
        {
            requiresTransformUpdate = value;

            foreach (Entity entity in entities)
            {
                entity.RequiresTransformUpdate = value;
            }
        }
    }
    public Assets.Blueprint Blueprint => blueprint;
    public virtual string Icon => "";
    public virtual IEnumerable<ConnectionDesc> Links
    {
        get
        {
            List<ConnectionDesc> outLinks = [];
            if (Blueprint != null)
            {
                InterfaceDescriptorData interfaceDesc = Blueprint.Data.Interface.GetObjectAs<FrostySdk.Ebx.InterfaceDescriptorData>();
                if (interfaceDesc != null)
                {
                    foreach (DynamicLink linkDesc in interfaceDesc.InputLinks)
                    {
                        outLinks.Add(new ConnectionDesc() { Name = linkDesc.Name, Direction = Direction.Out });
                    }
                    foreach (DynamicLink linkDesc in interfaceDesc.OutputLinks)
                    {
                        outLinks.Add(new ConnectionDesc() { Name = linkDesc.Name, Direction = Direction.In });
                    }
                }

                if (blueprint is Assets.ObjectBlueprint)
                {
                    if (entities.Count > 0)
                    {
                        Entity rootEntity = entities[0];
                        outLinks.AddRange((rootEntity as ILogicEntity).Links);
                    }
                }
            }
            return outLinks;
        }
    }
    public virtual IEnumerable<ConnectionDesc> Events
    {
        get
        {
            List<ConnectionDesc> outEvents = [];
            if (Blueprint != null)
            {
                InterfaceDescriptorData interfaceDesc = Blueprint.Data.Interface.GetObjectAs<FrostySdk.Ebx.InterfaceDescriptorData>();
                if (interfaceDesc != null)
                {
                    foreach (DynamicEvent eventDesc in interfaceDesc.InputEvents)
                    {
                        outEvents.Add(new ConnectionDesc() { Name = eventDesc.Name, Direction = Direction.In });
                    }
                    foreach (DynamicEvent eventDesc in interfaceDesc.OutputEvents)
                    {
                        outEvents.Add(new ConnectionDesc() { Name = eventDesc.Name, Direction = Direction.Out });
                    }
                }

                if (blueprint is Assets.ObjectBlueprint)
                {
                    if (entities.Count > 0)
                    {
                        Entity rootEntity = entities[0];
                        outEvents.AddRange((rootEntity as ILogicEntity).Events);
                    }
                }
            }
            return outEvents;
        }
    }
    public virtual IEnumerable<ConnectionDesc> Properties
    {
        get
        {
            List<ConnectionDesc> outProperties = [];
            if (Blueprint != null)
            {
                InterfaceDescriptorData interfaceDesc = Blueprint.Data.Interface.GetObjectAs<FrostySdk.Ebx.InterfaceDescriptorData>();
                if (interfaceDesc != null)
                {
                    foreach (DataField fieldDesc in interfaceDesc.Fields)
                    {
                        string fieldName = fieldDesc.Name;
                        if (Utils.IsFieldProperty(fieldName))
                            continue;

                        if (fieldDesc.AccessType == FrostySdk.Ebx.FieldAccessType.FieldAccessType_Source)
                        {
                            outProperties.Add(new ConnectionDesc() { Name = fieldDesc.Name, Direction = Direction.Out });
                        }
                        else if (fieldDesc.AccessType == FrostySdk.Ebx.FieldAccessType.FieldAccessType_Target)
                        {
                            outProperties.Add(new ConnectionDesc() { Name = fieldDesc.Name, Direction = Direction.In });
                        }
                        else
                        {
                            outProperties.Add(new ConnectionDesc() { Name = fieldDesc.Name, Direction = Direction.In });
                            outProperties.Add(new ConnectionDesc() { Name = fieldDesc.Name, Direction = Direction.Out });
                        }
                    }
                }

                if (blueprint is Assets.ObjectBlueprint)
                {
                    if (entities.Count > 0)
                    {
                        Entity rootEntity = entities[0];
                        outProperties.AddRange((rootEntity as ILogicEntity).Properties);
                    }
                }
            }
            outProperties.Add(new ConnectionDesc("BlueprintTransform", Direction.In));
            return outProperties;
        }
    }
    public virtual IEnumerable<string> HeaderRows { get { return new List<string>(); } }
    public virtual IEnumerable<string> DebugRows { get { return new List<string>(); } }
    public byte FlagsPropertyRealm
    {
        get => flagsPropertyRealm;
        set
        {
            if (flagsPropertyRealm >= value)
                return;

            flagsPropertyRealm = value;
            Data.Flags |= (uint)value << 27;
        }
    }
    public byte FlagsEventRealm
    {
        get => flagsEventRealm;
        set
        {
            if (flagsEventRealm >= value)
                return;

            flagsEventRealm = value;
            Data.Flags |= (uint)value << 25;
        }
    }
    public byte FlagsLinkRealm
    {
        get => flagsLinkRealm;
        set
        {
            if (flagsLinkRealm >= value)
                return;

            flagsLinkRealm = value;
            Data.Flags |= (uint)value << 25;
        }
    }

    protected long uniqueId;

    protected byte flagsPropertyRealm;
    protected byte flagsEventRealm;
    protected byte flagsLinkRealm;

    protected Assets.Blueprint blueprint;
    protected List<Entity> entities = [];
    protected List<Assets.Asset> additionalAssets = [];
    protected InterfaceDescriptor interfaceDescriptor;

    protected List<IProperty> properties = [];
    protected List<IEvent> events = [];
    protected List<ILink> links = [];

    protected bool requiresTransformUpdate;

    public ReferenceObject(FrostySdk.Ebx.ReferenceObjectData inData, Entity inParent, EntityWorld inWorld)
        : base(inData, inParent)
    {
        if (inWorld != null)
        {
            world = inWorld;
            world.AddEntity(this);

            uniqueId = world.GenerateDeterministicId(this);
        }

        Initialize();
        if (blueprint != null)
        {
            if (blueprint.Data.Interface.Type != FrostySdk.IO.PointerRefType.Null)
            {
                interfaceDescriptor = new InterfaceDescriptor(blueprint.Data.Interface.GetObjectAs<FrostySdk.Ebx.InterfaceDescriptorData>(), this);
            }
        }
        SpawnEntities();
        CollectPropertyValues();
        //InitializeSchematics();
    }

    public ReferenceObject(FrostySdk.Ebx.ReferenceObjectData inData, Entity inParent)
        : this(inData, inParent, null)
    {
    }

    /// <summary>
    /// 添加复制选中模型后新创建的Entity
    /// </summary>
    public void AddNewEntity(Entity entity)
    {
        entities.Add(entity);
    }

    public void DeleteEntity(Entity entity)
    {
        entities.Remove(entity);
    }

    public List<Entity> GetAllEntity()
    {
        return entities;
    }

    public Entity MyCreateEntity(GameObjectData objectData)
    {
        return CreateEntity(objectData);
    }

    public override void CreateRenderProxy(List<RenderProxy> proxies, RenderCreateState state)
    {
        foreach (Entity entity in entities)
            entity.CreateRenderProxy(proxies, state);
    }

    public override void SetOwner(Entity newOwner)
    {
        base.SetOwner(newOwner);
        foreach (Entity entity in entities)
            entity.SetOwner(newOwner);
    }

    public override void SetVisibility(bool newVisibility)
    {
        if (newVisibility != isVisible)
        {
            isVisible = newVisibility;
            foreach (Entity entity in entities)
                entity.SetVisibility(newVisibility);
        }
    }

    public override void Destroy()
    {
        interfaceDescriptor?.Destroy();

        LoadedAssetManager.Instance.UnloadAsset(blueprint);
        foreach (Entity entity in entities)
            entity.Destroy();

        foreach (Asset asset in additionalAssets)
            LoadedAssetManager.Instance.UnloadAsset(asset);

        base.Destroy();
    }

    public virtual Matrix GetTransform()
    {
        Matrix m = Matrix.Identity;
        if (parent != null && parent is ISpatialEntity)
            m = (parent as ISpatialEntity).GetTransform();
        return SharpDXUtils.FromLinearTransform(Data.BlueprintTransform) * m;
    }

    public virtual Matrix GetLocalTransform()
    {
        return SharpDXUtils.FromLinearTransform(Data.BlueprintTransform);
    }

    public virtual void SetTransform(Matrix m, bool suppressUpdate)
    {
        if (suppressUpdate)
        {
            if (UndoManager.Instance != null &&
                !UndoManager.Instance.IsUndoing &&
                UndoManager.Instance.PendingUndoUnit == null)
            {
                UndoManager.Instance.PendingUndoUnit = new MoveEntityUndoUnit(this, GetLocalTransform());
            }
        }
        else
        {
            UndoManager.Instance?.CommitUndo(UndoManager.Instance.PendingUndoUnit);
        }

        Data.BlueprintTransform = MakeLinearTransform(m);
        NotifyEntityModified("BlueprintTransform");
        requiresTransformUpdate = true;
    }

    public Entity FindEntity(Guid instanceGuid)
    {
        foreach (Entity entity in entities)
        {
            Guid entityInstanceGuid = entity.InstanceGuid;
            if (entityInstanceGuid == instanceGuid)
                return entity;

            if (entity is ILayerEntity)
            {
                if (entity is ReferenceObject)
                {
                    Entity foundEntity = (entity as ReferenceObject).FindEntity(instanceGuid);
                    if (foundEntity != null)
                        return foundEntity;
                }
            }
            else if (entity is IContainerOfEntities)
            {
                Entity foundEntity = (entity as IContainerOfEntities).FindEntity(instanceGuid);
                if (foundEntity != null)
                    return foundEntity;
            }
        }

        return null;
    }

    public virtual void BeginSimulation()
    {
        interfaceDescriptor?.BeginSimulation();
        BindSelfLink();

        foreach (Entity entity in entities)
        {
            if (entity is ISchematicsType)
            {
                ISchematicsType schematicsEntity = entity as ISchematicsType;
                schematicsEntity.BeginSimulation();
            }
        }
    }

    public virtual void EndSimulation()
    {
        interfaceDescriptor?.EndSimulation();
        foreach (Entity entity in entities)
        {
            if (entity is ISchematicsType)
            {
                ISchematicsType schematicsEntity = entity as ISchematicsType;
                schematicsEntity.EndSimulation();
            }
        }
    }

    public virtual void Update_PreFrame()
    {
        if (interfaceDescriptor != null)
        {
            interfaceDescriptor.Update_PreFrame();
        }

        // update property values in preframe
        //foreach (var property in properties)
        //{
        //    if (property.HasValueChanged)
        //    {
        //        OnPropertyChanged(property.NameHash);
        //    }
        //}

        // update link values in preframe
        foreach (ILink link in links)
        {
            if (link.HasValueChanged)
            {
                OnLinkChanged(link.NameHash);
            }
        }

        foreach (Entity entity in entities)
        {
            if (entity is ISchematicsType)
            {
                ISchematicsType schematicsEntity = entity as ISchematicsType;
                schematicsEntity.Update_PreFrame();
            }
        }
    }

    public virtual void Update_PostFrame()
    {
        if (interfaceDescriptor != null)
        {
            interfaceDescriptor.Update_PostFrame();
        }

        foreach (Entity entity in entities)
        {
            if (entity is ISchematicsType)
            {
                ISchematicsType schematicsEntity = entity as ISchematicsType;
                schematicsEntity.Update_PostFrame();
            }
        }
    }

    public virtual void AddPropertyConnection(int srcPort, ISchematicsType dstObject, int dstPort)
    {
        IProperty property = properties.Find(p => p.NameHash == srcPort);
        if (property != null)
        {
            property.AddConnection(dstObject, dstPort);
            return;
        }

        interfaceDescriptor?.AddPropertyConnection(srcPort, dstObject, dstPort);
    }

    public void AddEventConnection(int srcPort, ISchematicsType dstObject, int dstPort)
    {
        interfaceDescriptor?.AddEventConnection(srcPort, dstObject, dstPort);
    }

    public void AddLinkConnection(int srcPort, ISchematicsType dstObject, int dstPort)
    {
        interfaceDescriptor?.AddLinkConnection(srcPort, dstObject, dstPort);
    }

    public IProperty GetProperty(int nameHash)
    {
        IProperty property = interfaceDescriptor?.GetProperty(nameHash);
        if (property != null)
            return property;

        return properties.Find(p => p.NameHash == nameHash);
    }

    public IEvent GetEvent(int nameHash)
    {
        IEvent evt = interfaceDescriptor?.GetEvent(nameHash);
        if (evt != null)
            return evt;

        return events.Find(e => e.NameHash == nameHash);
    }

    public ILink GetLink(int nameHash)
    {
        ILink link = interfaceDescriptor?.GetLink(nameHash);
        if (link != null)
            return link;

        return links.Find(l => l.NameHash == nameHash);
    }

    public virtual void OnEvent(int eventHash)
    {
    }

    public virtual void OnPropertyChanged(int propertyHash)
    {
    }

    public virtual void OnLinkChanged(int linkHash)
    {
    }

    public void AddProperty(IProperty property)
    {
        properties.Add(property);
    }

    public virtual void AddEntity(Entity inEntity)
    {
        throw new NotImplementedException();
    }

    public virtual void RemoveEntity(Entity inEntity)
    {
        throw new NotImplementedException();
    }

    public override void SetDefaultValues()
    {
        base.SetDefaultValues();
#if SWBF2
        Data.LightmapScaleWithSize = true;
#endif
#if MASS_EFFECT
        Data.OverrideSpawningRadius = -1;
#endif
    }

    protected virtual void Initialize()
    {
        blueprint = LoadedAssetManager.Instance.LoadAsset<Assets.Blueprint>(this, Data.Blueprint);
    }

    protected virtual void SpawnEntities()
    {
        if (blueprint == null /*|| (this is INotRealSpatialEntity && Parent != null)*/)
            return;

        if (blueprint is Assets.ObjectBlueprint)
        {
            ObjectBlueprint objectBlueprint = blueprint as Assets.ObjectBlueprint;
            GameObjectData objectData = objectBlueprint.Data.Object.GetObjectAs<FrostySdk.Ebx.GameObjectData>();
            Entity entity = CreateEntity(objectData);

            if (entity != null)
            {
                entities.Add(entity);
                //App.Logger.Log($"ObjectBlueprint entity name: {entity.DisplayName}");
            }
        }
        else if (blueprint is Assets.PrefabBlueprint)
        {
            PrefabBlueprint prefabBlueprint = blueprint as Assets.PrefabBlueprint;
            int index = 0;

            foreach (PointerRef objPointer in prefabBlueprint.Data.Objects)
            {
                if (objPointer.Type == FrostySdk.IO.PointerRefType.External)
                {
                    // make sure the external asset is already loaded and add a ref to it
                    additionalAssets.Add(LoadedAssetManager.Instance.LoadAsset<Assets.Asset>(objPointer.External.FileGuid));
                }

                GameObjectData objectData = objPointer.GetObjectAs<FrostySdk.Ebx.GameObjectData>();
                Entity entity = CreateEntity(objectData);

                if (entity != null)
                {
                    entities.Add(entity);
                    //App.Logger.Log($"PrefabBlueprint entity name: {entity.DisplayName}");
                }

                if (parent is SubWorldReferenceObject)
                {
                    // only update the task window with layers that are associated with the current level (not layers within spatial prefabs)
                    LevelEditor.UpdateTask($"{Path.GetFileName(Blueprint.Name)}", (index++ / (double)prefabBlueprint.Data.Objects.Count) * 100);
                }
            }

            EbxAsset ebxAsset = LoadedAssetManager.Instance.GetEbxAsset(blueprint.FileGuid);
            foreach (object instance in ebxAsset.Objects)
            {
                if (instance is FrostySdk.Ebx.BaseShapeData)
                {
                    Entity entity = CreateEntity(instance as FrostySdk.Ebx.GameObjectData);
                    if (entity != null)
                    {
                        entities.Add(entity);
                        //App.Logger.Log($"BaseShapeData entity name: {entity.DisplayName}");
                    }
                }
            }
        }
    }

    protected virtual void CollectPropertyValues()
    {
        //if (this is LayerReferenceObject || this is SubWorldReferenceObject)
        if (interfaceDescriptor != null)
        {
            // find all property values
            foreach (PropertyConnection propertyConnection in Blueprint.Data.PropertyConnections.Where(pc => Utils.IsFieldProperty(((string)pc.SourceField))))
            {
                ReferenceObject layerEntity = entities.Where(le =>
                {
                    ReferenceObject refObjEntity = le as ReferenceObject;
                    return refObjEntity?.Blueprint?.FileGuid == propertyConnection.Target.External.FileGuid;

                }).FirstOrDefault() as ReferenceObject;

                if (layerEntity == null)
                {
                    layerEntity = this;
                }

                Entity entity = layerEntity?.FindEntity(propertyConnection.Target.GetInstanceGuid());
                if (entity != null)
                {
                    DataField field = interfaceDescriptor.Data.Fields.Find(f => f.Id == propertyConnection.SourceFieldId);
                    entity.AddPropertyValue(propertyConnection.TargetField, field, propertyConnection, Blueprint);
                }
            }
        }
    }

    public void InitializeSchematics()
    {
        if (Blueprint == null)
            return;

        foreach (PropertyConnection propertyConnection in Blueprint.Data.PropertyConnections)
        {
            ISchematicsType sourceEntity = world.FindEntity(GetPointerRefUniqueIdentifier(propertyConnection.Source)) as ISchematicsType;
            ISchematicsType targetEntity = world.FindEntity(GetPointerRefUniqueIdentifier(propertyConnection.Target)) as ISchematicsType;

            if (sourceEntity == null) sourceEntity = interfaceDescriptor;
            if (targetEntity == null) targetEntity = interfaceDescriptor;

            if (targetEntity == null || sourceEntity == null)
            {
                Debug.WriteLine($"Unable to add connection for {propertyConnection.Source.GetInstanceGuid()} -> {propertyConnection.Target.GetInstanceGuid()} in {Blueprint.Name}");
                continue;
            }

            targetEntity.AddPropertyConnection(propertyConnection.TargetFieldId, sourceEntity, propertyConnection.SourceFieldId);
        }

        foreach (EventConnection eventConnection in Blueprint.Data.EventConnections)
        {
            ISchematicsType sourceEntity = world.FindEntity(GetPointerRefUniqueIdentifier(eventConnection.Source)) as ISchematicsType;
            ISchematicsType targetEntity = world.FindEntity(GetPointerRefUniqueIdentifier(eventConnection.Target)) as ISchematicsType;

            if (sourceEntity == null) sourceEntity = interfaceDescriptor;
            if (targetEntity == null) targetEntity = interfaceDescriptor;

            if (targetEntity == null || sourceEntity == null)
            {
                Debug.WriteLine($"Unable to add connection for {eventConnection.Source.GetInstanceGuid()} -> {eventConnection.Target.GetInstanceGuid()} in {Blueprint.Name}");
                continue;
            }

            sourceEntity.AddEventConnection(eventConnection.SourceEvent.Id, targetEntity, eventConnection.TargetEvent.Id);
        }

        foreach (LinkConnection linkConnection in Blueprint.Data.LinkConnections)
        {
            ISchematicsType sourceEntity = world.FindEntity(GetPointerRefUniqueIdentifier(linkConnection.Source)) as ISchematicsType;
            ISchematicsType targetEntity = world.FindEntity(GetPointerRefUniqueIdentifier(linkConnection.Target)) as ISchematicsType;

            if (sourceEntity == null) sourceEntity = interfaceDescriptor;
            if (targetEntity == null) targetEntity = interfaceDescriptor;

            if (targetEntity == null || sourceEntity == null)
            {
                Debug.WriteLine($"Unable to add connection for {linkConnection.Source.GetInstanceGuid()} -> {linkConnection.Target.GetInstanceGuid()} in {Blueprint.Name}");
                continue;
            }

            sourceEntity.AddLinkConnection(linkConnection.SourceFieldId, targetEntity, linkConnection.TargetFieldId);
        }
    }
    public string GetPointerRefUniqueIdentifier(PointerRef @ref)
    {
        switch (@ref.Type)
        {
            case PointerRefType.External:
                return GenerateUniqueIdentifier(@ref.External.FileGuid, @ref.External.ClassGuid);

            case PointerRefType.Internal:
                return GenerateUniqueIdentifier(FileGuid, @ref.GetInstanceGuid());

            case PointerRefType.Null:
                return null;
        }

        return null;
    }
}
