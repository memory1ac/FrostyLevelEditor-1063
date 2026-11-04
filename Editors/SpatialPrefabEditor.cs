using Frosty.Core;
using Frosty.Core.Controls;
using Frosty.Core.Windows;
using FrostySdk.Interfaces;
using LevelEditorPlugin.Entities;
using LevelEditorPlugin.Screens;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace LevelEditorPlugin.Editors;

[TemplatePart(Name = PART_Renderer, Type = typeof(FrostyViewport))]
public class SpatialPrefabEditor : SpatialEditor
{
    private const string PART_Renderer = "PART_Renderer";

    private FrostyViewport viewport;

    public SpatialPrefabEditor(ILogger inLogger)
        : base(inLogger)
    {
        screen = new LevelEditorScreen(true);
        dockManager.LoadFromConfig("SpatialPrefabEditor", new Controls.DockManager.DockManagerConfigData()
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
                    Location = Controls.DockLocation.Bottom
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
        List<ToolbarItem> items =
        [
            new DockingToolbarItem("", "Show/Hide layers tab", "Images/Layers.png", new RelayCommand((o) => DockManager.AddItem(((DockingToolbarItem)o).Location, new LayersViewModel(this))), DockManager, "UID_LevelEditor_Layers"),
            new DockingToolbarItem("", "Show/Hide instances tab", "Images/Instances.png", new RelayCommand((o) => DockManager.AddItem(((DockingToolbarItem)o).Location, new InstancesViewModel(this, selectedEntity))), DockManager, "UID_LevelEditor_Instances"),
            new DockingToolbarItem("", "Show/Hide properties tab", "Images/Properties.png", new RelayCommand((o) => DockManager.AddItem(((DockingToolbarItem)o).Location, new PropertiesViewModel(this, selectedEntity))), DockManager, "UID_LevelEditor_Properties"),
            new DockingToolbarItem("", "Show/Hide timeline editor", "Images/Timeline.png", new RelayCommand((o) => DockManager.AddItem(((DockingToolbarItem)o).Location, new TimelineViewModel(this))), DockManager, "UID_LevelEditor_Timeline"),
            new FloatingOnlyDockingToolbarItem("", "Show/Hide schematics editor", "Images/Schematics.png", new RelayCommand((o) => DockManager.AddItem(((DockingToolbarItem)o).Location, new SchematicsViewModel(this, rootLayer))), DockManager, "UID_LevelEditor_Schematics"),
            new DividerToolbarItem(),
            ViewModeToolbarItem.Create(),
            new DividerToolbarItem(),
            //new RegularToolbarItem("", "Capture thumbnail preview", "LevelEditorPlugin/Images/CaptureThumbnail.png", new RelayCommand((o) => { CaptureThumbnail(viewport); })),
            //new ToggleToolbarItem("", "Show/Hide thumbnail safezone", "LevelEditorPlugin/Images/ThumbnailSafezone.png", false, new RelayCommand((o) => { ShowThumbnailSafeZone(thumbnailBorder.Visibility == Visibility.Collapsed, viewport); }))
        ];

        // SubWorld（SpatialPrefabBlueprint）走的就是这个编辑器，Havok 叠加开关也要有
        HavokOverlayToolbar.Add(items, this, logger);

        return items;
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
        DockManager.SaveToConfig("SpatialPrefabEditor");
        viewport.Shutdown();

        base.Closed();
    }

    protected override void Initialize()
    {
        Stopwatch timer = new Stopwatch();
        timer.Start();

        // AssetEntry 是 WPF 依赖属性，必须在 UI 线程读取后再进 FrostyTaskWindow 后台任务。
        string assetName = AssetEntry.Name;
        string assetType = AssetEntry.Type;
        Guid fileGuid = Asset.FileGuid;
        Guid rootGuid = Asset.RootInstanceGuid;
        bool isSubWorld = Asset.RootObject is FrostySdk.Ebx.SubWorldData
            || string.Equals(assetType, "SubWorldData", StringComparison.OrdinalIgnoreCase);

        FrostyTaskWindow.Show($"Loading {Path.GetFileName(assetName)}", "", (task) =>
        {
            currentLoadingState = new LoadingStateInfo()
            {
                Task = task,
                Logger = logger
            };

            FrostySdk.Ebx.PointerRef blueprintRef = new FrostySdk.Ebx.PointerRef(new FrostySdk.IO.EbxImportReference()
            {
                FileGuid = fileGuid,
                ClassGuid = rootGuid
            });

            FrostySdk.Ebx.GameObjectData objectData;
            if (isSubWorld)
            {
                objectData = new FrostySdk.Ebx.SubWorldReferenceObjectData()
                {
                    Blueprint = blueprintRef,
                    BundleName = assetName,
                    AutoLoad = true
                };
            }
            else
            {
                objectData = new FrostySdk.Ebx.SpatialPrefabReferenceObjectData()
                {
                    Blueprint = blueprintRef
                };
            }

            world = new EntityWorld();
            Entity created = Entities.Entity.CreateEntity(objectData, fileGuid, world);
            if (created is not ReferenceObject refObj)
                throw new InvalidOperationException("无法为 " + (assetType ?? "unknown") + " 创建地编实体");

            rootLayer = (created as ILayerEntity)?.GetLayer();

            viewport?.SetPaused(true);
            screen?.AddEntity(refObj);
            if (screen != null)
                screen.ShowTaskWindow = true;
            viewport?.SetPaused(false);

            editingWorld = refObj;
            currentLoadingState = null;

            world.Initialize();
        });

        timer.Stop();
        logger.Log($"Loaded spatial prefab in {timer.Elapsed.ToString()}");

        // 让Layers在Instance后加载
        DockManager.AddItemOnLoad(new InstancesViewModel(this, null));
        DockManager.AddItemOnLoad(new LayersViewModel(this));
        DockManager.AddItemOnLoad(new PropertiesViewModel(this, editingWorld));
        DockManager.AddItemOnLoad(new TimelineViewModel(this));
        DockManager.AddItemOnLoad(new SchematicsViewModel(this, rootLayer));
    }
}
