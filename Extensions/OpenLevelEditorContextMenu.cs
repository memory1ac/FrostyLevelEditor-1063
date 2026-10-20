using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Frosty.Core;
using Frosty.Core.Controls;
using FrostySdk;
using FrostySdk.Interfaces;
using FrostySdk.Managers;
using LevelEditorPlugin.Editors;

namespace LevelEditorPlugin.Extensions;

public class OpenLevelEditorContextMenuItem : DataExplorerContextMenuExtension
{
    public override string ContextItemName => "打开关卡编辑器";

    public override RelayCommand ContextItemClicked => new RelayCommand((o) =>
    {
        EbxAssetEntry asset = App.SelectedAsset;
        if (asset == null)
            return;

        string key = asset.Name;
        if (LevelEditorHost.TrySelect(key))
            return;

        FrostyAssetEditor editor = CreateEditor(asset.Type, App.Logger);
        if (editor == null)
        {
            App.Logger.LogError($"这个 EBX 不能用关卡编辑器打开 ({asset.Type})");
            return;
        }

        try
        {
            editor.SetAsset(asset);
        }
        catch (Exception ex)
        {
            App.Logger.LogError($"关卡编辑器打开失败 ({asset.Type}): {ex.Message}");
            return;
        }

        string title = string.IsNullOrEmpty(asset.Filename)
            ? asset.Name
            : asset.Filename;
        var host = new LevelEditorHost(key, editor);
        App.EditorWindow.OpenEditor(title + " (关卡编辑器)", host);
    });

    private static FrostyAssetEditor CreateEditor(string type, ILogger logger)
    {
        if (string.IsNullOrEmpty(type))
            return null;

        // 1.0.6.3 的 SDK 类型经常没有 EBX 继承链，TypeLibrary.IsSubClassOf
        // 对 SubWorldData / SpatialPrefabBlueprint 会返回 false。
        // 地图根是 WorldData/LevelData；分区是 SubWorldData，必须走 SpatialPrefabEditor。
        if (TypeNameIs(type, "LevelData", "WorldData", "DetachedSubWorldData")
            || IsSdkSubClass(type, "WorldData"))
            return new LevelEditor(logger);
        if (TypeNameIs(type, "SubWorldData", "SpatialPrefabBlueprint")
            || IsSdkSubClass(type, "SpatialPrefabBlueprint"))
            return new SpatialPrefabEditor(logger);
        if (TypeNameIs(type, "ObjectBlueprint") || IsSdkSubClass(type, "ObjectBlueprint"))
            return new ObjectBlueprintEditor(logger);
        if (TypeNameIs(type, "LogicPrefabBlueprint") || IsSdkSubClass(type, "LogicPrefabBlueprint"))
            return new LogicPrefabEditor(logger);

        return null;
    }

    private static bool TypeNameIs(string type, params string[] names)
    {
        foreach (string name in names)
        {
            if (string.Equals(type, name, StringComparison.OrdinalIgnoreCase))
                return true;
            if (type.EndsWith("." + name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsSdkSubClass(string type, string name)
    {
        try
        {
            return TypeLibrary.IsSubClassOf(type, name);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// FrostyAssetEditor cannot be passed to OpenEditor. This host is a FrostyBaseEditor,
/// so the existing editor window API can show the level editor in its own tab.
/// </summary>
public class LevelEditorHost : FrostyBaseEditor
{
    private static readonly Dictionary<string, LevelEditorHost> OpenHosts = new Dictionary<string, LevelEditorHost>();

    public static readonly DependencyProperty HostedEditorProperty = DependencyProperty.Register(
        nameof(HostedEditor), typeof(object), typeof(LevelEditorHost));

    public object HostedEditor
    {
        get => GetValue(HostedEditorProperty);
        set => SetValue(HostedEditorProperty, value);
    }

    private readonly string key;
    private readonly FrostyAssetEditor editor;
    private TabItem ownerTab;
    private EventHandler selectedChanged;

    public LevelEditorHost(string key, FrostyAssetEditor editor)
    {
        this.key = key;
        this.editor = editor;
        OpenHosts[key] = this;

        // 模板工厂不能直接塞 Visual。编辑器实例挂在宿主属性上，由 ContentPresenter 绑定显示。
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(HostedEditor))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        Template = new ControlTemplate(typeof(LevelEditorHost))
        {
            VisualTree = presenter
        };
        HostedEditor = editor;

        Loaded += OnLoaded;
        editor.Loaded += OnEditorLoaded;
    }

    public static bool TrySelect(string key)
    {
        if (!OpenHosts.TryGetValue(key, out LevelEditorHost host))
            return false;

        TabItem tab = host.ownerTab ?? FindAncestor<TabItem>(host);
        if (tab == null)
        {
            OpenHosts.Remove(key);
            return false;
        }

        tab.IsSelected = true;
        return true;
    }

    public override void Closed()
    {
        if (ownerTab != null && selectedChanged != null)
        {
            DependencyPropertyDescriptor.FromProperty(TabItem.IsSelectedProperty, typeof(TabItem))
                .RemoveValueChanged(ownerTab, selectedChanged);
        }

        if (OpenHosts.TryGetValue(key, out LevelEditorHost current) && current == this)
            OpenHosts.Remove(key);

        editor.Closed();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        ownerTab = FindAncestor<TabItem>(this);
        if (ownerTab == null)
            return;

        selectedChanged = (s, args) =>
        {
            if (ownerTab.IsSelected)
                ShowToolbar();
        };
        DependencyPropertyDescriptor.FromProperty(TabItem.IsSelectedProperty, typeof(TabItem))
            .AddValueChanged(ownerTab, selectedChanged);

        if (ownerTab.IsSelected)
            ShowToolbar();
    }

    private void OnEditorLoaded(object sender, RoutedEventArgs e)
    {
        editor.Loaded -= OnEditorLoaded;
        ShowToolbar();
    }

    private void ShowToolbar()
    {
        ItemsControl toolbar = Application.Current?.MainWindow?.FindName("editorToolbarItems") as ItemsControl;
        if (toolbar == null)
            return;

        toolbar.ItemsSource = editor.RegisterToolbarItems();
    }

    private static T FindAncestor<T>(DependencyObject child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T match)
                return match;
            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }
}
