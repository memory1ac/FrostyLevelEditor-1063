using Frosty.Core;
using Frosty.Core.Viewport;
using FrostySdk.Ebx;
using LevelEditorPlugin.Editors;
using SharpDX;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace LevelEditorPlugin.Entities;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class TypeProviderAttribute : Attribute
{
    public string TypeName { get; set; }
    public Type Type { get; set; }

    public TypeProviderAttribute(string typeName, Type type)
    {
        TypeName = typeName;
        Type = type;
    }
}
public interface IObjectStringConverter
{
    string ConvertToString(object value, bool includeType);
    object ConvertToObject(string value);
}

[TypeProvider("CString", typeof(FrostySdk.Ebx.CString))] public class CStringValueConverter : IObjectStringConverter { public object ConvertToObject(string value) { return (FrostySdk.Ebx.CString)value.Trim('"'); } public string ConvertToString(object value, bool includeType) { return $"{((includeType) ? "CString " : "")}\"{value}\""; } }
[TypeProvider("Float32", typeof(float))] public class FloatValueConverter : IObjectStringConverter { public object ConvertToObject(string value) { return float.Parse(value); } public string ConvertToString(object value, bool includeType) { return $"{((includeType) ? "Float32 " : "")}{value}"; } }
[TypeProvider("Int32", typeof(int))] public class IntValueConverter : IObjectStringConverter { public object ConvertToObject(string value) { return int.Parse(value); } public string ConvertToString(object value, bool includeType) { return $"{((includeType) ? "Int32 " : "")}{value}"; } }
[TypeProvider("Boolean", typeof(bool))] public class BooleanValueConverter : IObjectStringConverter { public object ConvertToObject(string value) { return (value.Equals("True", StringComparison.OrdinalIgnoreCase) ? true : false); } public string ConvertToString(object value, bool includeType) { return $"{((includeType) ? "Boolean " : "")}{((bool)value ? "True" : "False")}"; } }
[TypeProvider("Vec2", typeof(FrostySdk.Ebx.Vec2))]
public class Vec2ValueConverter : IObjectStringConverter
{
    public object ConvertToObject(string value)
    {
        string[] values = value.Split(new[] { '(', ')', ',' }, StringSplitOptions.RemoveEmptyEntries);
        return new FrostySdk.Ebx.Vec2()
        {
            x = float.Parse(values[0]),
            y = float.Parse(values[1])
        };
    }
    public string ConvertToString(object value, bool includeType)
    {
        Vec2 vec = value as FrostySdk.Ebx.Vec2;
        return $"{((includeType) ? "Vec2 " : "")}({vec.x},{vec.y})";
    }
}
[TypeProvider("Vec3", typeof(FrostySdk.Ebx.Vec3))]
public class Vec3ValueConverter : IObjectStringConverter
{
    public object ConvertToObject(string value)
    {
        string[] values = value.Split(new[] { '(', ')', ',' }, StringSplitOptions.RemoveEmptyEntries);
        return new FrostySdk.Ebx.Vec3()
        {
            x = float.Parse(values[0]),
            y = float.Parse(values[1]),
            z = float.Parse(values[2])
        };
    }
    public string ConvertToString(object value, bool includeType)
    {
        Vec3 vec = value as FrostySdk.Ebx.Vec3;
        return $"{((includeType) ? "Vec3 " : "")}({vec.x},{vec.y},{vec.z})";
    }
}
[TypeProvider("LinearTransform", typeof(FrostySdk.Ebx.LinearTransform))]
public class LinearTransformValueConverter : IObjectStringConverter
{
    private Vec3ValueConverter vec3ValueConverter = new Vec3ValueConverter();

    public object ConvertToObject(string value)
    {
        string[] values = value.Split(new[] { '(', ')', ',' }, StringSplitOptions.RemoveEmptyEntries);
        return new FrostySdk.Ebx.LinearTransform()
        {
            right = new FrostySdk.Ebx.Vec3() { x = float.Parse(values[0]), y = float.Parse(values[1]), z = float.Parse(values[2]) },
            up = new FrostySdk.Ebx.Vec3() { x = float.Parse(values[3]), y = float.Parse(values[4]), z = float.Parse(values[5]) },
            forward = new FrostySdk.Ebx.Vec3() { x = float.Parse(values[6]), y = float.Parse(values[7]), z = float.Parse(values[8]) },
            trans = new FrostySdk.Ebx.Vec3() { x = float.Parse(values[9]), y = float.Parse(values[10]), z = float.Parse(values[11]) }
        };
    }
    public string ConvertToString(object value, bool includeType)
    {
        LinearTransform lt = value as FrostySdk.Ebx.LinearTransform;
        return $"{((includeType) ? "LinearTransform " : "")}({vec3ValueConverter.ConvertToString(lt.right, false)},{vec3ValueConverter.ConvertToString(lt.up, false)},{vec3ValueConverter.ConvertToString(lt.forward, false)},{vec3ValueConverter.ConvertToString(lt.trans, false)})";
    }
}

public class PropertyValue
{
    public FrostySdk.Ebx.CString Name { get; set; } = "";
    public object Value { get; set; } = null;

    private IObjectStringConverter valueConverter;

    private FrostySdk.Ebx.DataField dataField;
    private FrostySdk.Ebx.PropertyConnection propConnection;
    private Assets.Blueprint blueprint;
    private bool usesOriginalData;

    public PropertyValue(FrostySdk.Ebx.CString name, FrostySdk.Ebx.DataField inDataField, FrostySdk.Ebx.PropertyConnection inPropConnection, Assets.Blueprint inBlueprint)
    {
        dataField = inDataField;
        propConnection = inPropConnection;
        blueprint = inBlueprint;
        usesOriginalData = !((string)inDataField.Name).StartsWith("PropertyValue_");

        string strValue = dataField.Value;
        string[] arr = strValue.Split(new[] { ' ' }, 2);

        foreach (Type type in Assembly.GetExecutingAssembly().GetTypes().Where(t => t.GetInterface("IObjectStringConverter") != null))
        {
            TypeProviderAttribute typeProvider = type.GetCustomAttribute<TypeProviderAttribute>();
            if (typeProvider == null)
                continue;

            if (typeProvider.TypeName.Equals(arr[0], StringComparison.OrdinalIgnoreCase))
            {
                valueConverter = (IObjectStringConverter)Activator.CreateInstance(type);
                break;
            }
        }

        Name = name;
        if (valueConverter != null) Value = valueConverter.ConvertToObject(arr[1]);
        else Value = arr[1];
    }

    public void SetValue(object value)
    {
        Value = value;
        if (valueConverter != null)
        {
            if (usesOriginalData)
            {
                // Need to create a new data field as other objects may be sharing this particular
                // data field value.

                string newName = blueprint.GetNextAvailablePropertyValue();
                dataField = new FrostySdk.Ebx.DataField()
                {
                    AccessType = dataField.AccessType,
                    Name = newName,
                    Value = "",
                    ValueRef = dataField.ValueRef
                };

                InterfaceDescriptorData interfaceDesc = blueprint.Data.Interface.GetObjectAs<FrostySdk.Ebx.InterfaceDescriptorData>();
                interfaceDesc.Fields.Add(dataField);

                // Replace the source field with our new source field
                propConnection.SourceField = newName;
                usesOriginalData = false;
            }

            dataField.Value = valueConverter.ConvertToString(value, true);
        }
    }
}

public interface IEbxType
{
    Guid FileGuid { get; }
    Guid InstanceGuid { get; }
}

public class EntityModifiedEventArgs : EventArgs
{
    /// <summary>
    /// Tells the property grid which value is being updated. If this value is null then the
    /// entire object will be re-evaluated by the property grid
    /// </summary>
    public string OptionalParameterNameChanged { get; private set; }

    public EntityModifiedEventArgs(string inOptParamNameChanged)
    {
        OptionalParameterNameChanged = inOptParamNameChanged;
    }
}

[Flags]
public enum EntityFlags
{
    None = 0,
    Initialized = 1,
    RenderProxyGenerated = 2,
    Destroyed = 4,

    HasLogic = 0x1000
}

public abstract class Entity : INotifyPropertyChanged, IEbxType
{
    public Entity Parent => parent;
    public Entity Owner => owner;
    public EntityWorld World => world;
    public object UserData
    {
        get => userData;
        set
        {
            if (userData != value)
            {
                userData = value;
                NotifyPropertyChanged();
            }
        }
    }
    public bool IsVisible
    {
        get => isVisible;
        set
        {
            if (isVisible != value)
            {
                SetVisibility(value);
                NotifyPropertyChanged();
            }
        }
    }
    public virtual string DisplayName
    {
        get
        {
            return data.GetType().Name;
        }
    }
    public FrostySdk.Ebx.GameObjectData Data
    {
        get
        {
            return data;
        }
    }
    public virtual FrostySdk.Ebx.Realm Realm => FrostySdk.Ebx.Realm.Realm_ClientAndServer;
    public Layers.SceneLayer Layer { get; set; }
    public virtual bool RequiresTransformUpdate
    {
        get => false;
        set
        {
        }
    }
    public Guid FileGuid => fileGuid;
    public Guid InstanceGuid => instanceGuid;

    protected EntityWorld world;
    protected Entity parent;
    protected Entity owner;

    protected FrostySdk.Ebx.GameObjectData data;
    protected bool isVisible;

    protected List<PropertyValue> propertyValues = [];
    protected object mockDataObject;

    protected Guid fileGuid;
    protected Guid instanceGuid;

    protected Link<Entity> selfLink;
    protected object userData;

    protected EntityFlags flags;

    public event EventHandler<EntityModifiedEventArgs> EntityModified;

    public Entity(FrostySdk.Ebx.GameObjectData inData, Entity inParent)
    {
        data = inData;
        parent = inParent;
        owner = this;
        isVisible = true;

        if (inParent != null)
        {
            if (inParent is ReferenceObject)
                fileGuid = (inParent as ReferenceObject).Blueprint.FileGuid;
            else if (inParent is LogicPrefabReferenceObject)
                fileGuid = (inParent as LogicPrefabReferenceObject).Blueprint.FileGuid;
            else
                fileGuid = inParent.FileGuid;

            world = inParent.world;
            world?.AddEntity(this);
        }
        instanceGuid = Guid.Parse(data.__InstanceGuid.ToString());
    }

    protected void BindSelfLink()
    {
        if (this is not ISchematicsType schematics)
            return;

        selfLink ??= new Link<Entity>(schematics, 0, this);
        selfLink.Value = this;
    }

    public bool HasFlags(EntityFlags inFlags)
    {
        return (flags & inFlags) != 0;
    }

    public virtual void CreateRenderProxy(List<Render.Proxies.RenderProxy> proxies, RenderCreateState state)
    {
    }

    public virtual void SetParent(Entity newParent)
    {
        if (parent != newParent)
        {
            parent = newParent;
            fileGuid = parent.FileGuid;
        }
    }

    public virtual void SetOwner(Entity newOwner)
    {
        owner = newOwner;
    }

    public virtual void SetVisibility(bool newVisibility)
    {
        if (newVisibility != isVisible)
        {
            isVisible = newVisibility;
        }
    }

    public virtual void Destroy()
    {
        if (world != null)
        {
            world.RemoveEntity(this);
            world = null;
        }
        else
        {
            App.Logger.LogWarning($"{DisplayName} failed to destroy! World was null");
        }
    }

    // This function will return the appropriate data object to be displayed in the property grid
    // this might be different to the Data object of an entity.
    public virtual object GetPropertyGridData()
    {
        return data;
    }

    public void AddPropertyValue(string name, FrostySdk.Ebx.DataField dataField, FrostySdk.Ebx.PropertyConnection propConnection, Assets.Blueprint blueprint)
    {
        propertyValues.Add(new PropertyValue(name, dataField, propConnection, blueprint));
    }

    public List<PropertyValue> GetPropertyValues()
    {
        return propertyValues;
    }

    public object GetMockDataObject()
    {
        return mockDataObject;
    }

    public T FindAncestor<T>() where T : Entity
    {
        Entity p = parent;
        while (!(p is T))
        {
            p = p.parent;
            if (p == null)
                return default(T);
        }
        return p as T;
    }

    public virtual void OnDataModified()
    {
    }

    public object GetRawData()
    {
        return data;
    }

    public virtual void SetDefaultValues()
    {
    }

    protected Entity CreateEntity(FrostySdk.Ebx.GameObjectData objectData)
    {
        return CreateEntity(objectData, this);
    }

    private static Dictionary<Type, Type> entityTypes;
    private static bool entityTypesReady;

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null);
        }
    }

    private static void EnsureEntityTypes()
    {
        if (entityTypesReady)
            return;

        entityTypes = [];
        foreach (Type type in SafeGetTypes(Assembly.GetExecutingAssembly()))
        {
            EntityBindingAttribute attr = type.GetCustomAttribute<EntityBindingAttribute>();
            if (attr == null)
                continue;

            Type dataType;
            try
            {
                dataType = attr.ResolveDataType();
            }
            catch (Exception)
            {
                continue;
            }

            if (dataType == null || entityTypes.ContainsKey(dataType))
                continue;

            entityTypes.Add(dataType, type);
        }

        entityTypesReady = true;
    }

    public static Entity CreateEntity(FrostySdk.Ebx.GameObjectData objectData, Entity parent, EntityWorld inWorld = null)
    {
        if (objectData == null)
            return null;

        EnsureEntityTypes();

        Type objectDataType = objectData.GetType();
        Type objectType = null;
        for (Type cursor = objectDataType; cursor != null && cursor != typeof(object); cursor = cursor.BaseType)
        {
            if (entityTypes.TryGetValue(cursor, out objectType))
                break;
        }

        if (objectType == null)
        {
            System.Diagnostics.Debug.WriteLine(objectDataType.ToString());
            return null;
        }

        // StaticModelGroup 在关卡数据里按两个参数构造
        if (objectDataType.Name == "StaticModelGroupEntityData")
            return InvokeEntityConstructor(objectType, objectData, parent, null);

        return InvokeEntityConstructor(objectType, objectData, parent, inWorld);
    }

    private static Entity InvokeEntityConstructor(Type objectType, FrostySdk.Ebx.GameObjectData objectData, Entity parent, EntityWorld inWorld)
    {
        Type objectDataType = objectData.GetType();
        if (inWorld != null)
        {
            ConstructorInfo threeParamConstructor = objectType.GetConstructors().FirstOrDefault(constructor =>
            {
                ParameterInfo[] parameters = constructor.GetParameters();
                return parameters.Length == 3
                    && parameters[0].ParameterType.IsAssignableFrom(objectDataType)
                    && parameters[1].ParameterType == typeof(Entity)
                    && parameters[2].ParameterType == typeof(EntityWorld);
            });
            if (threeParamConstructor != null)
                return (Entity)threeParamConstructor.Invoke([objectData, parent, inWorld]);
        }

        ConstructorInfo twoParamConstructor = objectType.GetConstructors().FirstOrDefault(constructor =>
        {
            ParameterInfo[] parameters = constructor.GetParameters();
            return parameters.Length == 2
                && parameters[0].ParameterType.IsAssignableFrom(objectDataType)
                && parameters[1].ParameterType == typeof(Entity);
        });
        if (twoParamConstructor == null)
            return null;

        return (Entity)twoParamConstructor.Invoke([objectData, parent]);
    }

    public static Entity CreateEntity(FrostySdk.Ebx.GameObjectData objectData, Guid blueprintGuid, EntityWorld inWorld)
    {
        Entity entity = CreateEntity(objectData, null, inWorld);
        entity.fileGuid = blueprintGuid;
        return entity;
    }

    public static FrostySdk.Ebx.LinearTransform MakeLinearTransform(Matrix m)
    {
        FrostySdk.Ebx.LinearTransform lt = new FrostySdk.Ebx.LinearTransform
        {
            right = new FrostySdk.Ebx.Vec3() { x = m.M11, y = m.M12, z = m.M13 },
            up = new FrostySdk.Ebx.Vec3() { x = m.M21, y = m.M22, z = m.M23 },
            forward = new FrostySdk.Ebx.Vec3() { x = m.M31, y = m.M32, z = m.M33 },
            trans = new FrostySdk.Ebx.Vec3() { x = m.M41, y = m.M42, z = m.M43 }
        };
        return lt;
    }

    public static string GenerateUniqueIdentifier(Guid inFileGuid, Guid inInstanceGuid)
    {
        string fileGuid = inFileGuid.ToString().Replace("-", "");
        string instanceGuid = inInstanceGuid.ToString().Replace("-", "");

        // Trim all starting zeroes to decrease the length of the unique identifier
        instanceGuid = instanceGuid.TrimStart('0');

        return string.Format("{0}{1}", fileGuid, instanceGuid);
    }

    public void NotifyEntityModified(string optParamNameChanged = null)
    {
        EntityModified?.Invoke(this, new EntityModifiedEventArgs(optParamNameChanged));
    }

    protected void SetFlags(EntityFlags inFlags)
    {
        flags |= inFlags;
    }

    protected void ClearFlags(EntityFlags inFlags)
    {
        flags ^= inFlags;
    }

    #region -- INotifyPropertyChanged --
    public event PropertyChangedEventHandler PropertyChanged;
    protected void NotifyPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] String propertyName = "")
    {
        if (PropertyChanged != null)
        {
            PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    #endregion
}

public interface IEntityData<T>
{
    T Data { get; }
}

/// <summary>
/// Provides the entity with functionality for setting and getting a transform
/// </summary>
public interface ITransformEntity
{
    Matrix GetTransform();
    Matrix GetLocalTransform();
    void SetTransform(Matrix m, bool suppressUpdate);
    bool RequiresTransformUpdate { get; set; }
}

/// <summary>
/// Tells various systems that this entity has a spatial representation
/// </summary>
public interface ISpatialEntity : ITransformEntity
{
}
public interface ISpatialReferenceEntity : ISpatialEntity
{
}

/// <summary>
/// Use on spatial entities to have them not show in any spatial lists. (eg. LogicPrefabs)
/// </summary>
public interface INotRealSpatialEntity
{
}

/// <summary>
/// Use on entities that can be removed from schematics view if it has zero connections, only use
/// on entities where you will have a secondary list of instances to allow you to drag a new version
/// into the canvas if required.
/// </summary>
public interface IHideInSchematicsView
{
}

/// <summary>
/// Entity is a layer and can hold other entities, and will appear in any layer based lists.
/// </summary>
public interface ILayerEntity
{
    Layers.SceneLayer GetLayer();
}

/// <summary>
/// Entity is a component or a holder of components, and will appear in any component based lists.
/// </summary>
public interface IComponentEntity
{
    IEnumerable<Entity> Components { get; }
    void SpawnComponents();
}

/// <summary>
/// Entity is a timeline track, and will appear in any timeline based lists
/// </summary>
public interface ITimelineTrackEntity
{
    string Icon { get; }
    IEnumerable<TimelineTrack> Tracks { get; }
}

/// <summary>
/// This tells the timeline editor to use this DisplayName instead of the default.
/// </summary>
public interface ITimelineCustomTrackName
{
    string DisplayName { get; }
}

public interface IContainerOfEntities
{
    Entity FindEntity(Guid instanceGuid);
    void AddEntity(Entity inEntity);
    void RemoveEntity(Entity inEntity);
}

public interface IAllowComponentsInLevel
{
}

public class EntityBindingAttribute : Attribute
{
    public Type DataType { get; set; }
    public string DataTypeName { get; set; }
    public string BaseTypeName { get; set; }

    public EntityBindingAttribute()
    {
    }

    public EntityBindingAttribute(string dataTypeName)
    {
        DataTypeName = dataTypeName;
    }

    public Type ResolveDataType()
    {
        Type dataType = DataType;
        if (dataType == null && !string.IsNullOrEmpty(DataTypeName))
            dataType = FrostySdk.TypeLibrary.GetType(DataTypeName);

        if (dataType == null)
            return null;

        if (!string.IsNullOrEmpty(BaseTypeName))
        {
            Type baseType = FrostySdk.TypeLibrary.GetType(BaseTypeName);
            if (baseType == null || !baseType.IsAssignableFrom(dataType))
                return null;
        }

        return dataType;
    }
}
