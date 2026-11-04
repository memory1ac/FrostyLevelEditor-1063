using Frosty.Core;
using Frosty.Core.Windows;
using LevelEditorPlugin.Managers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LevelEditorPlugin.Editors;

/// <summary>
/// 素材库里的目录节点（按 EBX 路径分层）。
/// </summary>
public class ObjectPaletteFolder : INotifyPropertyChanged
{
    public string Name { get; set; }
    public string FullPath { get; set; }
    public List<object> Children { get; } = [];

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (isExpanded != value)
            {
                isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }
    }

    private bool isExpanded;

    public event PropertyChangedEventHandler PropertyChanged;
}

/// <summary>
/// 拖拽到视口时携带的数据。
/// </summary>
public class ObjectPaletteDropData
{
    public ObjectPaletteEntry Entry;
}

/// <summary>
/// 「目标 SubWorld」下拉项：Node 为 null 时表示 Auto（按名字规则自动解析落点）。
/// </summary>
public class SubWorldTargetOption
{
    public SubWorldIndexNode Node { get; set; }
    public string Label { get; set; }
    public string Detail { get; set; }

    public bool IsAuto => Node == null;

    public override string ToString() => Label ?? "";
}

/// <summary>
/// Level Editor 的 Object Blueprint 素材库面板。
/// 收集当前关卡所有可用的 ObjectBlueprint（bundle 直采 / StaticModelGroup 解包 / SpatialPrefab 拍平），
/// 支持拖拽到视口或点击进入放置模式。
/// </summary>
public class ObjectPaletteViewModel : Controls.IDockableItem, INotifyPropertyChanged, IDragDropSourceProvider
{
    public string Header => "Object Palette";

    // 复用原来的 dock id，这样用户已有的布局配置不用改
    public string UniqueId => "UID_LevelEditor_Toolbox";

    public string Icon => "Images/Toolbox.png";

    public IEnumerable<ObjectPaletteFolder> Groups
    {
        get => m_groups;
        private set
        {
            if (!ReferenceEquals(m_groups, value))
            {
                m_groups = value;
                NotifyPropertyChanged();
            }
        }
    }

    public string FilterText
    {
        get => m_filterText;
        set
        {
            if (m_filterText != value)
            {
                m_filterText = value;
                NotifyPropertyChanged();
                RebuildTree();
            }
        }
    }

    public string StatusText
    {
        get => m_statusText;
        private set
        {
            if (m_statusText != value)
            {
                m_statusText = value;
                NotifyPropertyChanged();
            }
        }
    }

    public ObjectPaletteEntry SelectedEntry
    {
        get => m_selectedEntry;
        private set
        {
            if (!ReferenceEquals(m_selectedEntry, value))
            {
                m_selectedEntry = value;
                NotifyPropertyChanged();
            }
        }
    }

    /// <summary>「目标 SubWorld」下拉框的选项：第一项是 Auto（自动解析）。</summary>
    public IReadOnlyList<SubWorldTargetOption> TargetSubWorlds
    {
        get => m_targetSubWorlds;
        private set
        {
            if (!ReferenceEquals(m_targetSubWorlds, value))
            {
                m_targetSubWorlds = value;
                NotifyPropertyChanged();
            }
        }
    }

    /// <summary>当前选中的目标 SubWorld；选 Auto 时自动解析。</summary>
    public SubWorldTargetOption SelectedTargetSubWorld
    {
        get => m_selectedTargetSubWorld;
        set
        {
            if (!ReferenceEquals(m_selectedTargetSubWorld, value))
            {
                m_selectedTargetSubWorld = value;
                NotifyPropertyChanged();
                ApplyTargetSubWorld();
            }
        }
    }

    public ICommand SelectedItemChangedCommand { get; }
    public ICommand PlaceCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand LoadedCommand { get; }

    /// <summary>放置按钮文字：正在放置时变成「取消放置」</summary>
    public string PlaceButtonText => m_owner?.PendingPlacement != null ? "取消放置" : "放置";

    private readonly LevelEditor m_owner;
    private IEnumerable<ObjectPaletteFolder> m_groups = [];
    private string m_filterText = "";
    private string m_statusText = "打开面板后自动扫描关卡素材";
    private ObjectPaletteEntry m_selectedEntry;
    private IReadOnlyList<SubWorldTargetOption> m_targetSubWorlds = [];
    private SubWorldTargetOption m_selectedTargetSubWorld;
    private bool m_indexReady;

    public ObjectPaletteViewModel(LevelEditor inOwner)
    {
        m_owner = inOwner;

        SelectedItemChangedCommand = new RelayCommand(SelectedItemChanged);
        PlaceCommand = new RelayCommand(o => BeginPlacement(),
            o => m_owner != null && (m_selectedEntry != null || m_owner.PendingPlacement != null));
        RefreshCommand = new RelayCommand(o => Refresh(), o => m_owner != null);
        LoadedCommand = new RelayCommand(o => EnsureIndex());

        if (m_owner != null)
        {
            m_owner.PlacementStateChanged += OnPlacementStateChanged;
            if (m_owner.BlueprintIndex != null)
                EnsureIndex();
        }
    }

    private void OnPlacementStateChanged(object sender, EventArgs e)
    {
        NotifyPropertyChanged(nameof(PlaceButtonText));

        ObjectPaletteEntry pending = m_owner?.PendingPlacement;
        if (pending != null)
        {
            StatusText = $"移动鼠标预览，左键点击放置 {pending.DisplayName}（Esc 取消）";
            return;
        }

        string message = m_owner?.LastPlacementStatus;
        if (!string.IsNullOrEmpty(message))
        {
            StatusText = message;
            return;
        }

        if (m_indexReady)
            StatusText = $"共 {m_allEntries?.Count ?? 0} 个可用 ObjectBlueprint";
    }

    #region -- 索引 --

    private LevelBlueprintIndex m_index;
    private List<ObjectPaletteEntry> m_allEntries;

    private void EnsureIndex()
    {
        if (m_indexReady || m_owner == null)
            return;

        try
        {
            m_index = m_owner.GetOrBuildBlueprintIndex();
        }
        catch (Exception ex)
        {
            StatusText = $"扫描失败：{ex.Message}";
            App.Logger?.LogError($"[ObjectPalette] 扫描失败：{ex}");
            m_indexReady = true;
            return;
        }

        m_allEntries = m_index?.Entries ?? [];
        m_indexReady = true;
        m_owner.BlueprintIndexChanged += OnBlueprintIndexChanged;

        if (!string.IsNullOrEmpty(m_index?.LastWarning))
            StatusText = m_index.LastWarning;

        RebuildTree();
        RefreshTargetSubWorlds();
        StatusText = m_index == null ? "没有扫描到任何 ObjectBlueprint" : BuildStatusText();
    }

    private string BuildStatusText()
    {
        int entries = m_allEntries?.Count ?? 0;
        int subLevels = m_index?.PlaceableSubWorldsWithWorldParts.Count() ?? 0;
        int worldParts = m_index?.PlaceableSubWorldsWithWorldParts.Sum(n => n.WorldParts.Count) ?? 0;
        int artLevels = m_index?.PlaceableSubWorldsWithWorldParts
            .Count(n => LevelBlueprintIndex.ClassifySubWorldNode(n) == SubWorldTargetKind.Art) ?? 0;

        if (worldParts == 0)
            return $"共 {entries} 个可用 ObjectBlueprint · 没有扫描到可写入的内容 SubWorld WorldPartData";

        string artText = artLevels > 0 ? $"（其中 {artLevels} 个 LEVELART 美术层）" : "";
        return $"共 {entries} 个可用 ObjectBlueprint · 可写入 {subLevels} 个内容 SubWorld{artText} / {worldParts} 个 WorldPart";
    }

    private void OnBlueprintIndexChanged(object sender, EventArgs e)
    {
        m_index = m_owner.BlueprintIndex;
        m_allEntries = m_index?.Entries ?? [];
        RebuildTree();
        RefreshTargetSubWorlds();
        StatusText = BuildStatusText();
    }

    private void Refresh()
    {
        if (m_owner == null)
            return;

        try
        {
            FrostyTaskWindow.Show("Scanning level assets", "", task =>
            {
                m_owner.RebuildBlueprintIndex(task);
            });
        }
        catch (Exception ex)
        {
            StatusText = $"扫描失败：{ex.Message}";
            return;
        }

        m_index = m_owner.BlueprintIndex;
        m_allEntries = m_index?.Entries ?? [];
        m_indexReady = true;
        RebuildTree();
        RefreshTargetSubWorlds();
        StatusText = BuildStatusText();
    }

    /// <summary>
    /// 重建「目标 SubWorld」下拉项：Auto + 当前关卡所有可写 SubWorld（LEVELART 美术层排在最前）。
    /// 重建索引后按名字恢复之前的选择，避免选择被重置。
    /// </summary>
    private void RefreshTargetSubWorlds()
    {
        string previousKey = GetTargetKey(m_selectedTargetSubWorld?.Node);

        List<SubWorldTargetOption> options =
        [
            new SubWorldTargetOption()
            {
                Label = "Auto（自动解析落点）",
                Detail = "按 levelart / content 命名规则自动选目标 SubWorld",
            },
        ];

        if (m_index != null)
        {
            foreach (SubWorldIndexNode node in m_index.WritableSubWorldsWithWorldParts
                         .OrderBy(n => GetTargetSortRank(n))
                         .ThenByDescending(n => n.WorldParts.Count))
            {
                options.Add(new SubWorldTargetOption()
                {
                    Node = node,
                    Label = node.DisplayName,
                    Detail = node.BundleName,
                });
            }
        }

        SubWorldTargetOption restored = options[0];
        if (!string.IsNullOrEmpty(previousKey))
        {
            restored = options.FirstOrDefault(o => o.Node != null && GetTargetKey(o.Node) == previousKey) ?? options[0];
        }

        TargetSubWorlds = options;
        m_selectedTargetSubWorld = restored;
        NotifyPropertyChanged(nameof(SelectedTargetSubWorld));
        ApplyTargetSubWorld();
    }

    private static string GetTargetKey(SubWorldIndexNode node)
    {
        if (node == null)
            return null;

        return !string.IsNullOrEmpty(node.SubWorldName) ? node.SubWorldName : node.BundleName;
    }

    /// <summary>下拉框排序：LEVELART 美术层 → content 层 → 兜底层。</summary>
    private static int GetTargetSortRank(SubWorldIndexNode node)
    {
        switch (LevelBlueprintIndex.ClassifySubWorldNode(node))
        {
            case SubWorldTargetKind.Art:
                return 0;
            case SubWorldTargetKind.Content:
                return 1;
            default:
                return 2;
        }
    }

    private void ApplyTargetSubWorld()
    {
        if (m_owner == null)
            return;

        m_owner.PlacementTargetSubWorld = m_selectedTargetSubWorld?.Node;
    }

    private void RebuildTree()
    {
        if (m_allEntries == null)
        {
            Groups = [];
            return;
        }

        IEnumerable<ObjectPaletteEntry> entries = m_allEntries;
        string filter = (m_filterText ?? "").Trim();
        if (filter.Length > 0)
        {
            entries = entries.Where(e =>
                e.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                e.FilePath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        List<ObjectPaletteFolder> roots = [];
        Dictionary<string, ObjectPaletteFolder> map = new(StringComparer.OrdinalIgnoreCase);

        foreach (ObjectPaletteEntry entry in entries.OrderBy(e => e.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            string folderPath = GetFolderPath(entry.FilePath);
            ObjectPaletteFolder folder = GetOrCreateFolder(roots, map, folderPath);
            folder.Children.Add(entry);
        }

        SortFolder(roots);
        Groups = roots;
    }

    private static string GetFolderPath(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
            return "";

        int idx = fullPath.LastIndexOf('/');
        return idx <= 0 ? "" : fullPath.Substring(0, idx);
    }

    private static ObjectPaletteFolder GetOrCreateFolder(
        List<ObjectPaletteFolder> roots,
        Dictionary<string, ObjectPaletteFolder> map,
        string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath))
        {
            if (!map.TryGetValue("", out ObjectPaletteFolder root))
            {
                root = new ObjectPaletteFolder() { Name = "(root)", FullPath = "", IsExpanded = true };
                roots.Add(root);
                map[""] = root;
            }
            return root;
        }

        if (map.TryGetValue(folderPath, out ObjectPaletteFolder existing))
            return existing;

        int idx = folderPath.LastIndexOf('/');
        ObjectPaletteFolder folder;
        if (idx > 0)
        {
            ObjectPaletteFolder parent = GetOrCreateFolder(roots, map, folderPath.Substring(0, idx));
            folder = new ObjectPaletteFolder() { Name = folderPath.Substring(idx + 1), FullPath = folderPath };
            parent.Children.Add(folder);
        }
        else
        {
            folder = new ObjectPaletteFolder() { Name = folderPath, FullPath = folderPath, IsExpanded = true };
            roots.Add(folder);
        }

        map[folderPath] = folder;
        return folder;
    }

    private static void SortFolder(List<ObjectPaletteFolder> folders)
    {
        folders.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        foreach (ObjectPaletteFolder folder in folders)
        {
            List<object> sorted = [.. folder.Children.OfType<ObjectPaletteFolder>()];
            sorted.Sort((a, b) => string.Compare(((ObjectPaletteFolder)a).Name, ((ObjectPaletteFolder)b).Name, StringComparison.OrdinalIgnoreCase));
            sorted.AddRange(folder.Children.OfType<ObjectPaletteEntry>()
                .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase));

            folder.Children.Clear();
            folder.Children.AddRange(sorted);
            SortFolder(folder.Children.OfType<ObjectPaletteFolder>().ToList());
        }
    }

    #endregion

    #region -- 交互 --

    private void SelectedItemChanged(object item)
    {
        if (item is not ObjectPaletteEntry entry)
            return;

        SelectedEntry = entry;

        // 放置模式下改选别的素材：直接换成新素材的预览（旧预览在 BeginPlacement 里销毁），
        // 不用让用户先取消再重新点放置。
        if (m_owner?.PendingPlacement != null && !ReferenceEquals(m_owner.PendingPlacement, entry))
            m_owner.BeginPlacement(entry);
    }

    public bool InitiateDrag(out object dataToDrag, out FrameworkElement optionalVisual)
    {
        dataToDrag = null;
        optionalVisual = null;

        ObjectPaletteEntry entry = m_selectedEntry;
        if (entry == null)
            return false;

        dataToDrag = new ObjectPaletteDropData() { Entry = entry };

        // DragDropExtension 的反馈窗需要 visual 非空，这里给一个轻量提示块
        Border visual = new Border()
        {
            Width = 240,
            Height = 24,
            Background = new SolidColorBrush(Color.FromArgb(200, 40, 40, 40)),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Child = new TextBlock()
            {
                Text = entry.DisplayName,
                Foreground = Brushes.White,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        optionalVisual = visual;
        return true;
    }

    private void BeginPlacement()
    {
        if (m_owner == null)
            return;

        // 已经在放置模式：再次点击按钮 → 取消，回到正常状态
        if (m_owner.PendingPlacement != null)
        {
            if (m_selectedEntry == null || ReferenceEquals(m_owner.PendingPlacement, m_selectedEntry))
            {
                m_owner.CancelPlacement();
                return;
            }

            // 选了别的素材再点按钮 → 换成新素材的预览
            m_owner.BeginPlacement(m_selectedEntry);
            return;
        }

        if (m_selectedEntry == null)
            return;

        m_owner.BeginPlacement(m_selectedEntry);
    }

    #endregion

    #region -- INotifyPropertyChanged --

    public event PropertyChangedEventHandler PropertyChanged;

    private void NotifyPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}
