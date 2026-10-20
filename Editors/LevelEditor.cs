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
        toolbarItems.Add(new DockingToolbarItem("", "Show/Hide toolbox", "Images/Toolbox.png", new RelayCommand((o) => DockManager.AddItem(((DockingToolbarItem)o).Location, new InstancesViewModel(this, selectedEntity))), DockManager, "UID_LevelEditor_Toolbox"));
        toolbarItems.Add(new DockingToolbarItem("", "Show/Hide properties tab", "Images/Properties.png", new RelayCommand((o) => DockManager.AddItem(((DockingToolbarItem)o).Location, new PropertiesViewModel(this, selectedEntity))), DockManager, "UID_LevelEditor_Properties"));
        toolbarItems.Add(new DockingToolbarItem("", "Show/Hide terrain layers tab", "Images/Terrain.png", new RelayCommand((o) => DockManager.AddItem(((DockingToolbarItem)o).Location, new TerrainLayersViewModel(this))), DockManager, "UID_LevelEditor_TerrainLayers"));
        toolbarItems.Add(new DockingToolbarItem("", "Show/Hide timeline editor", "Images/Timeline.png", new RelayCommand((o) => DockManager.AddItem(((DockingToolbarItem)o).Location, new TimelineViewModel(this))), DockManager, "UID_LevelEditor_Timeline"));
        //toolbarItems.Add(new FloatingOnlyDockingToolbarItem("", "Show/Hide schematics editor", "Images/Schematics.png", new RelayCommand((o) => DockManager.AddItem(((DockingToolbarItem)o).Location, new SchematicsViewModel(this, rootLayer))), DockManager, "UID_LevelEditor_Schematics"));
        //toolbarItems.Add(new RegularToolbarItem("", "Export all visible instances to XML", "LevelEditorPlugin/Images/XMLFile.png", new RelayCommand((o) => { ExportLevelAsXML(); })));
        toolbarItems.Add(new DividerToolbarItem());
        toolbarItems.Add(ViewModeToolbarItem.Create());

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
        DockManager.SaveToConfig("LevelEditor");
        viewport.Shutdown();
        base.Closed();
    }

    public bool IsValidDropSource(IDataObject draggedData)
    {
        if (draggedData.GetDataPresent(typeof(DataExplorerDropData)))
        {
            var dropData = (DataExplorerDropData)draggedData.GetData(typeof(DataExplorerDropData));
            if (dropData.Entry.Type == "ObjectBlueprint")
                return true;
        }

        return false;
    }

    public void ProcessDrop(IDataObject draggedData, System.Windows.Point dropPoint)
    {
        if (draggedData.GetDataPresent(typeof(DataExplorerDropData)))
        {
            var dropData = (DataExplorerDropData)draggedData.GetData(typeof(DataExplorerDropData));
            var asset = App.AssetManager.GetEbx(dropData.Entry);

            var refObjEntity = SelectedLayer.Entity;
            var layerEntity = SelectedLayer;
        }
    }

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
        DockManager.AddItemOnLoad(new LevelEditorToolboxViewModel());
        DockManager.AddItemOnLoad(new PropertiesViewModel(this, editingWorld));
        DockManager.AddItemOnLoad(new TimelineViewModel(this));
        DockManager.AddItemOnLoad(new TerrainLayersViewModel(this));
        //DockManager.AddItemOnLoad(new SchematicsViewModel(this, rootLayer));
    }

    private EbxAssetEntry levelEntry;
    private string levelEntryName;

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
