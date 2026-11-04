using Frosty.Core;
using Frosty.Core.Controls;
using Frosty.Core.Windows;
using FrostySdk;
using FrostySdk.Attributes;
using FrostySdk.Ebx;
using FrostySdk.Interfaces;
using FrostySdk.IO;
using FrostySdk.Managers;
using LevelEditorPlugin.Entities;
using LevelEditorPlugin.Layers;
using LevelEditorPlugin.Managers;
using LevelEditorPlugin.Render;
using LevelEditorPlugin.Screens;
using MeshSetPlugin;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Xml;
using MeshSet = MeshSetPlugin.Resources.MeshSet;
using RelayCommand = Frosty.Core.RelayCommand;

namespace LevelEditorPlugin.Editors;

public static class Utils
{
    private static bool IsTypeValid(Type a, Type b)
    {
        return a == b || a.IsSubclassOf(b);
    }

    // This should only be used to obtain internal references or external IF the asset has already been loaded
    // through the LoadedAssetManager

    public static T GetObjectAs<T>(this PointerRef pr)
    {
        if (pr.Type == PointerRefType.External)
        {
            EbxAsset asset = LoadedAssetManager.Instance.GetEbxAsset(pr);
            Debug.Assert(asset != null, "Asset was not previously loaded via the LoadedAssetManager");

            dynamic obj = asset.GetObject(pr.External.ClassGuid);
            Debug.Assert(!(obj is Assets.Asset), "GetObjectAs should not be used on Assets");

            if (IsTypeValid(obj.GetType(), typeof(T)))
                return (T)obj;
        }
        else if (pr.Type == PointerRefType.Internal)
        {
            if (IsTypeValid(pr.Internal.GetType(), typeof(T)))
                return (T)pr.Internal;
        }

        return default(T);
    }

    /// <summary>
    /// Gets the value of a <see cref="PointerRef"/>.
    /// </summary>
    /// <param name="pointerRef">The <see cref="PointerRef"/> to be used.</param>
    /// <returns>The resolved value.</returns>
    public static object Resolve(this PointerRef pointerRef)
    {
        // Create a variable for storing the returned pointerref value
        object pointerRefValue = null;

        // Check if it's external
        if (pointerRef.Type == PointerRefType.External)
        {
            // Get the pointerref's external ebximportreference
            EbxImportReference importReference = pointerRef.External;

            // Get the associated asset and asset entry
            EbxAssetEntry importEntry = App.AssetManager.GetEbxEntry(importReference.FileGuid);
            EbxAsset importAsset = App.AssetManager.GetEbx(importEntry);

            // Set the pointerref's value to its import reference's referenced object
            pointerRefValue = importAsset.GetObject(importReference.ClassGuid);
        }
        else if (pointerRef.Type == PointerRefType.Internal)
        {
            // Set the pointerref's value to its internal value
            pointerRefValue = pointerRef.Internal;
        }

        // If it isn't either of these, it is a null pointerref, so nothing has to be set since the pointerref's value is defaulted to null
        // Return the value
        return pointerRefValue;
    }

    public static XmlWriter EbxObjectToXml(this XmlWriter xmlWriter, object Obj, Type ObjType)
    {
        StringBuilder SB = new StringBuilder();
        int TotalCount = ObjType.GetProperties().Length;

        PropertyInfo[] Properties = ObjType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Array.Sort(Properties, new PropertyComparer());

        string StrGuid = "";
        FieldInfo FI = ObjType.GetField("__Guid", BindingFlags.NonPublic | BindingFlags.Instance);

        if (FI != null)
        {
            AssetClassGuid Guid = (AssetClassGuid)FI.GetValue(Obj);
            StrGuid = Guid.ToString();
        }

        if (TotalCount != 0 && (Properties.Length > 0 || (ObjType.BaseType != typeof(object) && ObjType.BaseType != typeof(ValueType))))
        {
            xmlWriter.WriteStartElement(ObjType.Name);
            xmlWriter.WriteElementString("Guid", StrGuid);

            foreach (PropertyInfo PI in Properties)
            {
                if (PI.GetCustomAttribute<IsTransientAttribute>() != null)
                    continue;

                object Value = PI.GetValue(Obj);
                string Tmp = "";

                xmlWriter.EbxFieldToXml(Value, ref Tmp, PI.Name);
            }

            xmlWriter.WriteEndElement();
        }
        else
        {
            xmlWriter.WriteStartElement(ObjType.Name + StrGuid);
            xmlWriter.WriteEndElement();
        }
        return xmlWriter;
    }

    public static XmlWriter EbxFieldToXml(this XmlWriter xmlWriter, object Value, ref string AdditionalInfo, string propertyName)
    {
        Type FieldType = Value.GetType();

        if (FieldType.Name == "List`1")
        {
            xmlWriter.WriteStartElement(propertyName);
            int Count = (int)FieldType.GetMethod("get_Count").Invoke(Value, null);
            //AdditionalInfo = " Count=\"" + Count + "\"";
            xmlWriter.WriteAttributeString("Count", Count.ToString());
            if (Count > 0)
            {
                for (int i = 0; i < Count; i++)
                {
                    //SB.Append("".PadLeft(TabCount) + "<member Index=\"" + i.ToString() + "\">");
                    xmlWriter.WriteStartElement("Member");
                    xmlWriter.WriteAttributeString("Index", i.ToString());

                    object SubValue = FieldType.GetMethod("get_Item").Invoke(Value, new object[] { i });
                    string Tmp = "";

                    //xmlWriter.EbxFieldToXml(SubValue, ref Tmp);

                    xmlWriter.WriteEndElement();
                }
            }
            xmlWriter.WriteEndElement();
        }
        else
        {
            if (FieldType.Namespace == "FrostySdk.Ebx" && FieldType.BaseType != typeof(Enum))
            {
                if (FieldType == typeof(CString)) xmlWriter.WriteElementString(FieldType.Name, Value.ToString());
                else if (FieldType == typeof(ResourceRef)) xmlWriter.WriteElementString(FieldType.Name, Value.ToString());
                else if (FieldType == typeof(FileRef)) xmlWriter.WriteElementString(FieldType.Name, Value.ToString());
                else if (FieldType == typeof(TypeRef)) xmlWriter.WriteElementString(FieldType.Name, Value.ToString());
                else if (FieldType == typeof(BoxedValueRef)) xmlWriter.WriteElementString(FieldType.Name, Value.ToString());
                else if (FieldType == typeof(PointerRef))
                {
                    PointerRef Reference = (PointerRef)Value;
                    if (Reference.Type == PointerRefType.Internal)
                    {
                        Type SubObjType = Reference.Internal.GetType();
                        AssetClassGuid guid = (AssetClassGuid)SubObjType.GetField("__Guid", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Reference.Internal);
                        xmlWriter.WriteValue("[" + SubObjType.Name + "]" + guid.ToString());
                    }
                    else if (Reference.Type == PointerRefType.External)
                    {
                        EbxAssetEntry entry = App.AssetManager.GetEbxEntry(Reference.External.FileGuid);
                        if (entry != null)
                        {
                            xmlWriter.WriteValue("[Ebx] " + entry.Name + " [" + Reference.External.ClassGuid + "]");
                        }
                        else
                        {
                            xmlWriter.WriteValue("[Ebx] BadRef " + Reference.External.FileGuid + "/" + Reference.External.ClassGuid);
                        }
                    }
                    else
                        xmlWriter.WriteValue("nullptr");
                }
                else
                {
                    xmlWriter.EbxObjectToXml(Value, Value.GetType());
                }
            }
            else
            {
                if (FieldType == typeof(byte)) xmlWriter.WriteElementString(FieldType.Name, ((byte)Value).ToString("X2"));
                else if (FieldType == typeof(ushort)) xmlWriter.WriteElementString(FieldType.Name, ((ushort)Value).ToString("X4"));
                else if (FieldType == typeof(uint))
                {
                    uint value = (uint)Value;
                    string val = FrostySdk.Utils.GetString((int)value);

                    if (!val.StartsWith("0x"))
                    {
                        xmlWriter.WriteElementString(FieldType.Name, val + " [" + value.ToString("X8") + "]");
                    }
                    else
                    {
                        xmlWriter.WriteElementString(FieldType.Name, val);
                    }
                }
                else if (FieldType == typeof(int))
                {
                    int value = (int)Value;
                    string val = FrostySdk.Utils.GetString(value);

                    if (!val.StartsWith("0x"))
                    {
                        xmlWriter.WriteElementString(FieldType.Name, val + " [" + value.ToString("X8") + "]");
                    }
                    else
                    {
                        xmlWriter.WriteElementString(FieldType.Name, val);
                    }
                }
                else if (FieldType == typeof(ulong)) xmlWriter.WriteElementString(FieldType.Name, ((ulong)Value).ToString("X16"));
                else if (FieldType == typeof(float)) xmlWriter.WriteElementString(FieldType.Name, ((float)Value).ToString());
                else if (FieldType == typeof(double)) xmlWriter.WriteElementString(FieldType.Name, ((double)Value).ToString());
                else xmlWriter.WriteElementString(FieldType.Name, Value.ToString());
            }
        }
        return xmlWriter;
    }

    public static Guid GetInstanceGuid(this PointerRef pr)
    {
        if (pr.Type == PointerRefType.Null)
            return Guid.Empty;

        else if (pr.Type == PointerRefType.Internal)
        {
            DataContainer container = pr.Internal as FrostySdk.Ebx.DataContainer;
            if (container.__InstanceGuid.IsExported)
                return container.__InstanceGuid.ExportedGuid;

            return Guid.Parse(container.__InstanceGuid.ToString());
        }

        return pr.External.ClassGuid;
    }

    public static bool IsFieldProperty(string fieldName)
    {
        if (ProfilesLibrary.DataVersion == (int)ProfileVersion.MassEffectAndromeda)
        {
            Type extension = Type.GetType("LevelEditorPlugin.UtilsExtension");
            if (extension != null)
            {
                return (bool)extension.GetMethod("IsFieldProperty", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { fieldName });
            }
        }
        return false;
    }
}

public class SelectedEntityChangedEventArgs : EventArgs
{
    public Entities.Entity NewSelection { get; private set; }
    public Entities.Entity OldSelection { get; private set; }

    public SelectedEntityChangedEventArgs(Entities.Entity inNew, Entities.Entity inOld)
    {
        NewSelection = inNew;
        OldSelection = inOld;
    }
}

public class SelectedLayerChangedEventArgs : EventArgs
{
    public Layers.SceneLayer NewSelection { get; private set; }
    public Layers.SceneLayer OldSelection { get; private set; }

    public SelectedLayerChangedEventArgs(Layers.SceneLayer inNew, Layers.SceneLayer inOld)
    {
        NewSelection = inNew;
        OldSelection = inOld;
    }
}

public class SelectedObjectChangedEventArgs : EventArgs
{
    public object NewSelection { get; private set; }
    public object OldSelection { get; private set; }

    public SelectedObjectChangedEventArgs(object inNew, object inOld)
    {
        NewSelection = inNew;
        OldSelection = inOld;
    }
}

[TemplatePart(Name = PART_Renderer, Type = typeof(FrostyViewport))]
public class LevelEditor : SpatialEditor, IDragDropTargetProvider
{
    private const string PART_Renderer = "PART_Renderer";

    private FrostyViewport viewport;

    //static LevelEditor()
    //{
    //    DefaultStyleKeyProperty.OverrideMetadata(typeof(LevelEditor), new FrameworkPropertyMetadata(typeof(LevelEditor)));
    //}

    public LevelEditor(ILogger inLogger)
        : base(inLogger)
    {
        screen = new LevelEditorScreen(false);
        dockManager.LoadFromConfig("LevelEditor", new Controls.DockManager.DockManagerConfigData()
        {
            Layouts =
            [
                new Controls.DockManager.DockLayoutData()
                {
                    UniqueId = "UID_LevelEditor_Layers",
                    IsVisible = true,
                    IsSelected = true,
                    Location = Controls.DockLocation.TopLeft
                },
                new Controls.DockManager.DockLayoutData()
                {
                    UniqueId = "UID_LevelEditor_Instances",
                    IsVisible = true,
                    IsSelected = true,
                    Location = Controls.DockLocation.BottomLeft
                },
                new Controls.DockManager.DockLayoutData()
                {
                    UniqueId = "UID_LevelEditor_Toolbox",
                    IsVisible = true,
                    IsSelected = false,
                    Location = Controls.DockLocation.BottomLeft
                },
                new Controls.DockManager.DockLayoutData()
                {
                    UniqueId = "UID_LevelEditor_Properties",
                    IsVisible = true,
                    IsSelected = true,
                    Location = Controls.DockLocation.TopRight
                },
                new Controls.DockManager.DockLayoutData()
                {
                    UniqueId = "UID_LevelEditor_Timeline",
                    IsVisible = false,
                    IsSelected = false,
                    Location = Controls.DockLocation.Bottom,
                },
                new Controls.DockManager.DockLayoutData()
                {
                    UniqueId = "UID_LevelEditor_TerrainLayers",
                    IsVisible = true,
                    IsSelected = false,
                    Location = Controls.DockLocation.TopLeft
                },
                new Controls.DockManager.DockLayoutData()
                {
                    UniqueId = "UID_LevelEditor_Schematics",
                    IsVisible = false,
                    IsSelected = false,
                    Location = Controls.DockLocation.Floating,
                    FloatingData = new Controls.DockManager.DockLayoutFloatingData()
                    {
                         Width = 800,
                         Height = 400
                    }
                }
            ]
        });
    }

    public override List<ToolbarItem> RegisterToolbarItems()
    {
        List<ToolbarItem> toolbarItems = base.RegisterToolbarItems();
        toolbarItems.Add(new DockingToolbarItem("", "Show/Hide layers tab", "Images/Layers.png", new RelayCommand((o) => DockManager.AddItem(((DockingToolbarItem)o).Location, new LayersViewModel(this))), DockManager, "UID_LevelEditor_Layers"));
        toolbarItems.Add(new DockingToolbarItem("", "Show/Hide instances tab", "Images/Instances.png", new RelayCommand((o) => DockManager.AddItem(((DockingToolbarItem)o).Location, new InstancesViewModel(this, selectedEntity))), DockManager, "UID_LevelEditor_Instances"));
        toolbarItems.Add(new DockingToolbarItem("", "Show/Hide object palette", "Images/Toolbox.png", new RelayCommand((o) => DockManager.AddItem(((DockingToolbarItem)o).Location, new ObjectPaletteViewModel(this))), DockManager, "UID_LevelEditor_Toolbox"));
        toolbarItems.Add(new DockingToolbarItem("", "Show/Hide properties tab", "Images/Properties.png", new RelayCommand((o) => DockManager.AddItem(((DockingToolbarItem)o).Location, new PropertiesViewModel(this, selectedEntity))), DockManager, "UID_LevelEditor_Properties"));
        toolbarItems.Add(new DockingToolbarItem("", "Show/Hide terrain layers tab", "Images/Terrain.png", new RelayCommand((o) => DockManager.AddItem(((DockingToolbarItem)o).Location, new TerrainLayersViewModel(this))), DockManager, "UID_LevelEditor_TerrainLayers"));
        toolbarItems.Add(new DockingToolbarItem("", "Show/Hide timeline editor", "Images/Timeline.png", new RelayCommand((o) => DockManager.AddItem(((DockingToolbarItem)o).Location, new TimelineViewModel(this))), DockManager, "UID_LevelEditor_Timeline"));
        //toolbarItems.Add(new FloatingOnlyDockingToolbarItem("", "Show/Hide schematics editor", "Images/Schematics.png", new RelayCommand((o) => DockManager.AddItem(((DockingToolbarItem)o).Location, new SchematicsViewModel(this, rootLayer))), DockManager, "UID_LevelEditor_Schematics"));
        //toolbarItems.Add(new RegularToolbarItem("", "Export all visible instances to XML", "LevelEditorPlugin/Images/XMLFile.png", new RelayCommand((o) => { ExportLevelAsXML(); })));
        toolbarItems.Add(new DividerToolbarItem());
        toolbarItems.Add(ViewModeToolbarItem.Create());

        // Havok 碰撞体叠加：按下后把每个模型在 ObjectBlueprint 里引用的 Havok 物理 res 画到关卡上。
        // 名字里带 cluster 的破坏碎块（碎片）会跳过，只显示实体碰撞用的那份。
        HavokOverlayToolbar.Add(toolbarItems, this, logger);

        return toolbarItems;
    }

    public void ExportLevelAsXML()
    {
        //List<Entities.Entity> entities = new List<Entities.Entity>();
        //RootLayer.CollectEntities(entities);

        List<SceneLayer> layers = [];
        RootLayer.CollectLayers(layers);

        string rootLayerName = RootLayer.LayerName;
        FileInfo fi = new FileInfo(Assembly.GetExecutingAssembly().FullName);
        string basePath = Environment.CurrentDirectory + "\\Levels\\" + rootLayerName + "\\";
        string meshPath = basePath + "Meshes\\";
        string terrainPath = basePath + "TerrainChunks\\";
        Directory.CreateDirectory(basePath);
        Directory.CreateDirectory(meshPath);
        Directory.CreateDirectory(terrainPath);

        Dictionary<string, bool> hasExportedMesh = [];

        FrostyTaskWindow.Show("Exporting " + rootLayerName, "", (task) =>
        {
            uint smiCount = 0; // Total Static Model Instances
            uint objCount = 0; // Total ObjectRefs
            uint spatialCount = 0; // Total SpatialObjRefs

            XmlWriterSettings xmlSettings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true
            };

            task.Update("Writing XML");

            XmlWriter xmlWriter = XmlWriter.Create(basePath + rootLayerName + ".xml", xmlSettings);

            xmlWriter.WriteStartElement("FrostbiteLevel");
            xmlWriter.WriteElementString("Name", rootLayerName);
            xmlWriter.WriteElementString("Game", ProfilesLibrary.ProfileName);

            FBXExporter fbxExporter = new FBXExporter(task);

            foreach (SceneLayer layer in layers)
            {
                if (layer.LayerName == "static_instances") continue;

                xmlWriter.WriteStartElement("WorldLayer");
                xmlWriter.WriteElementString("Name", layer.LayerName);

                List<Entities.Entity> entities = [];
                layer.CollectEntities(entities);

                xmlWriter.WriteStartElement("StaticModelInstances");

                int staticInstanceCount = 0;
                foreach (Entities.Entity ent in entities.Where(e => e is Entities.StaticModelGroupElementEntity))
                {
                    xmlWriter.WriteStartElement("StaticModelGroupElementEntity");
                    StaticModelGroupElementEntity smi = ent as StaticModelGroupElementEntity;

                    //Get required assets (Object blueprint, mesh asset)
                    EbxAssetEntry objBlueprint = App.AssetManager.GetEbxEntry(smi.Data.Blueprint.External.FileGuid);

                    if (objBlueprint == null) continue;

                    EbxAsset objAsset = App.AssetManager.GetEbx(objBlueprint);

                    dynamic objRootAsset = objAsset.RootObject;

                    EbxAssetEntry objMeshAsset = App.AssetManager.GetEbxEntry(((dynamic)objRootAsset.Object.Internal).Mesh.External.FileGuid);

                    if (objMeshAsset == null) continue;

                    task.Update("StaticModel " + objMeshAsset.DisplayName);

                    // Export mesh

                    string meshFilename = meshPath + objBlueprint.DisplayName + "_mesh.fbx";
                    if (!hasExportedMesh.TryGetValue(meshFilename, out bool hasExported))
                    {
                        EbxAsset meshAssetEbx = App.AssetManager.GetEbx(objMeshAsset);
                        dynamic meshAsset = (dynamic)meshAssetEbx.RootObject;

                        ResAssetEntry meshSetEntry = App.AssetManager.GetResEntry(meshAsset.MeshSetResource);
                        fbxExporter.ExportFBX(meshAsset, meshFilename, "2017", "Meters", false, true, string.Empty, "binary", App.AssetManager.GetResAs<MeshSet>(meshSetEntry));
                        hasExportedMesh[meshFilename] = true;
                    }

                    FrostySdk.Ebx.LinearTransform smiTransform = smi.Data.Transform;
                    xmlWriter.WriteElementString("Blueprint", objBlueprint.Name);
                    WriteTransformToXML(xmlWriter, smiTransform);

                    // End of element
                    xmlWriter.WriteEndElement(); // Element entity
                    smiCount++;
                    staticInstanceCount++;
                }
                xmlWriter.WriteEndElement(); // static instances
                xmlWriter.WriteElementString("StaticInstanceCount", staticInstanceCount.ToString());

                xmlWriter.WriteStartElement("Objects");
                int objectCount = 0;
                foreach (Entities.Entity ent in entities.Where(e => e is Entities.ObjectReferenceObject))
                {
                    xmlWriter.WriteStartElement("ObjectInstance");
                    ObjectReferenceObject obj = ent as ObjectReferenceObject;
                    if (!(obj.Data.GetType().Name == "ObjectReferenceObjectData"))
                    {
                        continue;
                    }
                    ObjectReferenceObjectData objRef = obj.Data;

                    EbxAssetEntry objRefAssetEntry = App.AssetManager.GetEbxEntry(objRef.Blueprint.External.FileGuid);

                    if (objRefAssetEntry == null)
                        continue;

                    EbxAsset objRefAsset = App.AssetManager.GetEbx(objRefAssetEntry);

                    dynamic objRefRoot = objRefAsset.RootObject;

                    EbxAssetEntry objMeshAsset = null;
                    try
                    {
                        objMeshAsset = App.AssetManager.GetEbxEntry(((dynamic)objRefRoot.Object.Internal).Mesh.External.FileGuid);
                    }
                    catch
                    {
                        continue;
                    }

                    if (objMeshAsset == null) continue;

                    task.Update("Object " + objMeshAsset.DisplayName);

                    FrostySdk.Ebx.LinearTransform smiTransform = objRef.BlueprintTransform;
                    xmlWriter.WriteElementString("Blueprint", objRefAssetEntry.Name);
                    WriteTransformToXML(xmlWriter, smiTransform);

                    string meshFilename = meshPath + objRefAssetEntry.DisplayName + "_mesh.fbx";
                    if (!hasExportedMesh.TryGetValue(meshFilename, out bool hasExported))
                    {
                        EbxAsset meshAssetEbx = App.AssetManager.GetEbx(objMeshAsset);
                        dynamic meshAsset = (dynamic)meshAssetEbx.RootObject;

                        ResAssetEntry meshSetEntry = App.AssetManager.GetResEntry(meshAsset.MeshSetResource);
                        fbxExporter.ExportFBX(meshAsset, meshFilename, "2017", "Meters", false, true, string.Empty, "binary", App.AssetManager.GetResAs<MeshSet>(meshSetEntry));
                        hasExportedMesh[meshFilename] = true;
                    }

                    xmlWriter.WriteEndElement(); // object instance
                    objCount++;
                    objectCount++;
                }
                foreach (Entities.Entity ent in entities.Where(e => e is Entities.SpatialPrefabReferenceObject))
                {
                    xmlWriter.WriteStartElement("SpatialPrefabInstance");
                    SpatialPrefabReferenceObject obj = ent as SpatialPrefabReferenceObject;
                    SpatialPrefabReferenceObjectData objRef = obj.Data;

                    EbxAssetEntry objRefAssetEntry = App.AssetManager.GetEbxEntry(objRef.Blueprint.External.FileGuid);

                    if (objRefAssetEntry == null)
                        continue;

                    EbxAsset objRefAsset = App.AssetManager.GetEbx(objRefAssetEntry);

                    dynamic objRefRoot = objRefAsset.RootObject;

                    task.Update("SpatialPrefab " + objRefAssetEntry.DisplayName);

                    FrostySdk.Ebx.LinearTransform smiTransform = objRef.BlueprintTransform;
                    xmlWriter.WriteElementString("Blueprint", objRefAssetEntry.Name);
                    WriteTransformToXML(xmlWriter, smiTransform);

                    xmlWriter.WriteEndElement(); // spatialprefab instance
                    objCount++;
                    objectCount++;
                }
                /*foreach (Entities.Entity ent in entities.Where(e => e is Entities.PointLightEntity))
                {
                    xmlWriter.WriteStartElement("PointLightInstance");

                    PointLightEntity obj = ent as PointLightEntity;
                    PointLightEntityData pointLight = obj.Data as PointLightEntityData;

                    xmlWriter.WriteElementString("Color", pointLight.Color.ToString());
                    xmlWriter.WriteElementString("Intensity", pointLight.Intensity.ToString());
                    xmlWriter.WriteElementString("Radius", pointLight.Radius.ToString());
                    WriteTransformToXML(xmlWriter, pointLight.Transform);
                    xmlWriter.WriteEndElement();
                    objCount++;
                    objectCount++;
                }*/
                xmlWriter.WriteEndElement(); // objects
                xmlWriter.WriteElementString("ObjectCount", objectCount.ToString());

                xmlWriter.WriteEndElement(); // World layer
            }
            xmlWriter.WriteEndElement(); // level
            xmlWriter.Dispose();

            List<Entities.Entity> allEntites = [];
            RootLayer.CollectEntities(allEntites);

            foreach (Entities.Entity ent in allEntites)
            {
                if (ent is TerrainEntity)
                {
                    task.Update("Exporting Terrain");
                    TerrainEntity terrain = ent as TerrainEntity;
                    int idx = 0;
                    foreach (TerrainChunkRenderable chunk in terrain.Terrain.TerrainData.TerrainChunks)
                    {
                        chunk.ExportToOBJ(terrainPath + $"chunk_{chunk.Level}_{idx}.obj");
                        idx++;
                    }
                }
            }

            App.Logger.Log("Exported {0} static models, {1} objects", smiCount, objCount);
        });
    }

    private void WriteTransformToXML(XmlWriter xmlWriter, FrostySdk.Ebx.LinearTransform smiTransform)
    {
        // Write transform
        xmlWriter.WriteStartElement("Transform");
        xmlWriter.WriteStartElement("LinearTransform");

        xmlWriter.WriteStartElement("right");
        WriteVec3ToXML(xmlWriter, smiTransform.right.x.ToString(), smiTransform.right.y.ToString(), smiTransform.right.z.ToString());
        xmlWriter.WriteEndElement();

        xmlWriter.WriteStartElement("up");
        WriteVec3ToXML(xmlWriter, smiTransform.up.x.ToString(), smiTransform.up.y.ToString(), smiTransform.up.z.ToString());
        xmlWriter.WriteEndElement();

        xmlWriter.WriteStartElement("forward");
        WriteVec3ToXML(xmlWriter, smiTransform.forward.x.ToString(), smiTransform.forward.y.ToString(), smiTransform.forward.z.ToString());
        xmlWriter.WriteEndElement();

        xmlWriter.WriteStartElement("trans");
        WriteVec3ToXML(xmlWriter, smiTransform.trans.x.ToString(), smiTransform.trans.y.ToString(), smiTransform.trans.z.ToString());
        xmlWriter.WriteEndElement();

        xmlWriter.WriteEndElement();
        xmlWriter.WriteEndElement();
    }

    private void WriteVec3ToXML(XmlWriter xmlWriter, string x, string y, string z)
    {
        xmlWriter.WriteStartElement("Vec3");
        xmlWriter.WriteElementString("x", x);
        xmlWriter.WriteElementString("y", y);
        xmlWriter.WriteElementString("z", z);
        xmlWriter.WriteEndElement();
    }
    public override void OnApplyTemplate()
    {
        viewport = GetTemplateChild(PART_Renderer) as FrostyViewport;
        base.OnApplyTemplate();
        if (viewport != null)
            viewport.Screen = screen;
    }
    public override void Closed()
    {
        // 关闭编辑器时顺手清掉放置预览，避免蓝图引用跟着泄露
        pendingPlacement = null;
        DestroyPlacementPreview();

        DockManager.SaveToConfig("LevelEditor");
        viewport.Shutdown();
        base.Closed();
    }

    public bool IsValidDropSource(IDataObject draggedData)
    {
        if (draggedData == null)
            return false;

        if (draggedData.GetDataPresent(typeof(ObjectPaletteDropData)))
        {
            ObjectPaletteDropData dropData = (ObjectPaletteDropData)draggedData.GetData(typeof(ObjectPaletteDropData));
            if (dropData?.Entry != null)
                return true;
        }

        if (draggedData.GetDataPresent(typeof(DataExplorerDropData)))
        {
            var dropData = (DataExplorerDropData)draggedData.GetData(typeof(DataExplorerDropData));
            if (dropData?.Entry == null)
                return false;
            if (dropData.Entry.Type == "ObjectBlueprint")
                return true;
            if (dropData.Entry.Type == "SpatialPrefabBlueprint")
                return true;
        }

        return false;
    }

    public void ProcessDrop(IDataObject draggedData, System.Windows.Point dropPoint)
    {
        if (draggedData == null)
            return;

        List<ObjectPaletteEntry> entries = [];

        if (draggedData.GetDataPresent(typeof(ObjectPaletteDropData)))
        {
            ObjectPaletteDropData dropData = (ObjectPaletteDropData)draggedData.GetData(typeof(ObjectPaletteDropData));
            if (dropData?.Entry != null)
                entries.Add(dropData.Entry);
        }
        else if (draggedData.GetDataPresent(typeof(DataExplorerDropData)))
        {
            var dropData = (DataExplorerDropData)draggedData.GetData(typeof(DataExplorerDropData));
            entries.AddRange(ResolveDataExplorerEntries(dropData?.Entry));
        }

        if (entries.Count == 0)
            return;

        if (!screen.TryGetPlacementPoint((int)dropPoint.X, (int)dropPoint.Y, out SharpDX.Vector3 worldPoint, out _))
        {
            LastPlacementStatus = "无法在视口里确定落点";
            return;
        }

        foreach (ObjectPaletteEntry entry in entries)
            PlaceEntry(entry, worldPoint);
    }

    /// <summary>
    /// 数据浏览器拖进来的可能是 SpatialPrefabBlueprint，需要拍平到它引用的 ObjectBlueprint 才能放置。
    /// </summary>
    private List<ObjectPaletteEntry> ResolveDataExplorerEntries(FrostySdk.Managers.EbxAssetEntry assetEntry)
    {
        List<ObjectPaletteEntry> result = [];
        if (assetEntry == null)
            return result;

        LevelBlueprintIndex index = GetOrBuildBlueprintIndex();

        // 索引里已有的直接复用
        ObjectPaletteEntry existing = index?.Entries.FirstOrDefault(e => e.FileGuid == assetEntry.Guid);
        if (existing != null)
        {
            result.Add(existing);
            return result;
        }

        if (assetEntry.Type == "ObjectBlueprint")
        {
            result.Add(new ObjectPaletteEntry() { Entry = assetEntry, Sources = PaletteSourceFlags.Bundle });
            return result;
        }

        if (assetEntry.Type == "SpatialPrefabBlueprint" && index != null)
        {
            foreach (FrostySdk.Managers.EbxAssetEntry entry in index.FlattenPrefabBlueprints(assetEntry))
                result.Add(new ObjectPaletteEntry() { Entry = entry, Sources = PaletteSourceFlags.SpatialPrefab });
        }

        return result;
    }

    #region -- Object Palette 放置 --

    private LevelBlueprintIndex blueprintIndex;
    private ObjectPaletteEntry pendingPlacement;
    private SubWorldIndexNode placementTargetSubWorld;
    private PlacementPreview placementPreview;
    private Window placementWindow;
    private string lastPlacementStatus = "";

    /// <summary>预览跟随鼠标的像素阈值：小于这个位移就不重新做射线（性能）</summary>
    private const int PlacementPreviewMoveThreshold = 2;
    private const int PlacementPreviewInvalidPosition = -100000;
    private int lastPlacementPreviewX = PlacementPreviewInvalidPosition;
    private int lastPlacementPreviewY = PlacementPreviewInvalidPosition;

    /// <summary>关卡素材索引（首次访问时构建）</summary>
    public LevelBlueprintIndex BlueprintIndex => blueprintIndex;

    /// <summary>索引重建完成</summary>
    public event EventHandler BlueprintIndexChanged;

    /// <summary>放置状态变化（进入/退出放置模式、放置结果）</summary>
    public event EventHandler PlacementStateChanged;

    /// <summary>等待点击放置的蓝图（null 表示不在放置模式）</summary>
    public ObjectPaletteEntry PendingPlacement => pendingPlacement;

    /// <summary>当前放置预览的根实体（没有预览时为 null），射线拾取时要排除它</summary>
    public LevelEditorPlugin.Entities.Entity PlacementPreviewEntity => placementPreview?.RootEntity;

    /// <summary>
    /// 面板里手动指定（覆盖自动解析）的放置目标 SubWorld；null = Auto，按名字规则自动解析。
    /// 拖拽放置与点击放置都会遵守这个值。
    /// </summary>
    public SubWorldIndexNode PlacementTargetSubWorld
    {
        get => placementTargetSubWorld;
        set => placementTargetSubWorld = value;
    }

    /// <summary>最近一次放置的结果描述，供状态栏显示</summary>
    public string LastPlacementStatus
    {
        get => lastPlacementStatus;
        private set
        {
            lastPlacementStatus = value;
            PlacementStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public LevelBlueprintIndex GetOrBuildBlueprintIndex()
    {
        if (blueprintIndex != null)
            return blueprintIndex;

        FrostyTaskWindow.Show("Scanning level assets", "", task => RebuildBlueprintIndex(task));
        return blueprintIndex;
    }

    public void RebuildBlueprintIndex(FrostyTaskWindow task)
    {
        blueprintIndex = LevelBlueprintIndex.Build(this, task);
        BlueprintIndexChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 进入「点击放置」模式：鼠标在视口里移动时显示跟随光标的模型预览，
    /// 左键点击落点才真正写 EBX；Esc 或再次点击放置按钮取消。
    /// </summary>
    public void BeginPlacement(ObjectPaletteEntry entry)
    {
        if (entry == null)
            return;

        // 换素材时先把上一个预览清掉（预览是懒创建的，等鼠标进视口再建）
        DestroyPlacementPreview();
        lastPlacementPreviewX = PlacementPreviewInvalidPosition;
        lastPlacementPreviewY = PlacementPreviewInvalidPosition;

        pendingPlacement = entry;
        LastPlacementStatus = $"移动鼠标预览，左键点击放置 {entry.DisplayName}（Esc 取消）";

        if (placementWindow == null)
        {
            placementWindow = Window.GetWindow(this);
            if (placementWindow != null)
                placementWindow.PreviewKeyDown += PlacementWindow_PreviewKeyDown;
        }

        PlacementStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CancelPlacement()
    {
        CancelPlacement(destroyPreview: true);
    }

    private void CancelPlacement(bool destroyPreview)
    {
        if (pendingPlacement == null)
            return;

        pendingPlacement = null;
        if (destroyPreview)
            DestroyPlacementPreview();

        LastPlacementStatus = "已取消放置";

        if (placementWindow != null)
        {
            placementWindow.PreviewKeyDown -= PlacementWindow_PreviewKeyDown;
            placementWindow = null;
        }

        PlacementStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 放置模式下鼠标在视口里移动（由 LevelEditorScreen.MouseMove 调用）：
    /// 用射线取地面/地形/已有模型的落点，把预览模型挪过去。
    /// </summary>
    public void UpdatePlacementPreview(int x, int y)
    {
        if (pendingPlacement == null || screen == null)
            return;

        // 鼠标只挪了一两个像素就不用重新做全场景射线，省点开销
        if (placementPreview != null &&
            Math.Abs(x - lastPlacementPreviewX) < PlacementPreviewMoveThreshold &&
            Math.Abs(y - lastPlacementPreviewY) < PlacementPreviewMoveThreshold)
        {
            return;
        }

        if (!screen.TryGetPlacementPoint(x, y, out SharpDX.Vector3 worldPoint, out _))
            return;

        lastPlacementPreviewX = x;
        lastPlacementPreviewY = y;

        if (placementPreview == null || !placementPreview.IsValid)
        {
            DestroyPlacementPreview();
            placementPreview = PlacementPreview.Create(this, pendingPlacement);
        }

        placementPreview?.MoveTo(worldPoint);
    }

    private void DestroyPlacementPreview()
    {
        if (placementPreview == null)
            return;

        placementPreview.Dispose();
        placementPreview = null;
    }

    /// <summary>视口点击放置（由 LevelEditorScreen 在 MouseDown 里调用）</summary>
    public bool PlaceAt(int x, int y)
    {
        if (pendingPlacement == null || screen == null)
            return false;

        ObjectPaletteEntry entry = pendingPlacement;

        // 先退出放置模式，但预览先留着：等真正生成实体后再销毁，
        // 免得同一个蓝图被「卸载 → 重新加载」一次，也能避免闪一下空白。
        CancelPlacement(destroyPreview: false);

        try
        {
            if (!screen.TryGetPlacementPoint(x, y, out SharpDX.Vector3 worldPoint, out _))
            {
                LastPlacementStatus = "无法在视口里确定落点";
                return true;
            }

            PlaceEntry(entry, worldPoint);
        }
        finally
        {
            DestroyPlacementPreview();
        }

        return true;
    }

    private void PlaceEntry(ObjectPaletteEntry entry, SharpDX.Vector3 worldPoint)
    {
        PlacementResult result = LevelPlacementService.Place(this, GetOrBuildBlueprintIndex(), entry, worldPoint, placementTargetSubWorld);
        LastPlacementStatus = result.Message;

        if (!result.Success)
            App.Logger?.LogWarning($"[ObjectPalette] 放置失败：{result.Message}");
    }

    private void PlacementWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape && pendingPlacement != null)
        {
            CancelPlacement();
            e.Handled = true;
        }
    }

    #endregion

    protected override void Initialize()
    {
        Stopwatch timer = new Stopwatch();
        timer.Start();

        EbxAssetEntry ebxEntry = AssetEntry as EbxAssetEntry;
        if (ebxEntry == null && AssetEntry != null)
            ebxEntry = App.AssetManager.GetEbxEntry(AssetEntry.Name);
        levelEntry = ebxEntry;
        levelEntryName = ebxEntry?.Name ?? AssetEntry?.Name;

        FrostyTaskWindow.Show($"Loading {Path.GetFileName(levelEntryName)}", "", LoadLevel);
        timer.Stop();
        logger?.Log($"Level loaded in {timer.Elapsed}");

        // 让Layers在Instance后加载
        DockManager.AddItemOnLoad(new InstancesViewModel(this, null));
        DockManager.AddItemOnLoad(new LayersViewModel(this));
        DockManager.AddItemOnLoad(new ObjectPaletteViewModel(this));
        DockManager.AddItemOnLoad(new PropertiesViewModel(this, editingWorld));
        DockManager.AddItemOnLoad(new TimelineViewModel(this));
        DockManager.AddItemOnLoad(new TerrainLayersViewModel(this));
        //DockManager.AddItemOnLoad(new SchematicsViewModel(this, rootLayer));
    }

    private EbxAssetEntry levelEntry;
    private string levelEntryName;

    /// <summary>关卡 LevelData 的 asset entry（素材索引在 world 还没建好时会用到）</summary>
    public EbxAssetEntry LevelAssetEntry => levelEntry ?? (AssetEntry as EbxAssetEntry);

    private void LoadLevel(FrostyTaskWindow task)
    {
        currentLoadingState = new LoadingStateInfo()
        {
            Task = task,
            Logger = logger
        };

        TerrainAlbedoResolver.SetActiveLevel(levelEntryName);
        if (asset == null && levelEntry != null)
            asset = App.AssetManager.GetEbx(levelEntry);
        if (asset == null)
            throw new InvalidOperationException("无法读取关卡 EBX：" + (levelEntryName ?? "(null)"));

        WorldData worldData = null;
        foreach (object obj in asset.Objects)
        {
            if (obj is WorldData candidate)
            {
                worldData = candidate;
                break;
            }
        }
        if (worldData == null)
            throw new InvalidOperationException("关卡里没有 WorldData：" + levelEntryName);

        world = new EntityWorld();
        WorldReferenceObject refObj = new Entities.WorldReferenceObject(asset.FileGuid, worldData, asset, world);
        rootLayer = refObj.GetLayer();
        if (screen is LevelEditorScreen levelScreen)
            levelScreen.EditorProvider = this;
        viewport?.SetPaused(true);
        screen?.AddEntity(refObj);
        if (screen != null)
            screen.ShowTaskWindow = true;
        viewport?.SetPaused(false);
        editingWorld = refObj;
        currentLoadingState = null;
        world.Initialize();
    }
}
