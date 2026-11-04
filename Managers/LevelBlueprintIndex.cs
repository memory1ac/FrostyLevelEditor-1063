using Frosty.Core;
using Frosty.Core.Windows;
using FrostySdk;
using FrostySdk.Ebx;
using FrostySdk.IO;
using FrostySdk.Managers;
using LevelEditorPlugin.Editors;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using BundleType = FrostySdk.Managers.BundleType;

namespace LevelEditorPlugin.Managers;

/// <summary>
/// 蓝图在关卡里的来源标记（可组合）。
/// </summary>
[Flags]
public enum PaletteSourceFlags
{
    None = 0,
    /// <summary>直接位于关卡某个 bundle 内</summary>
    Bundle = 1,
    /// <summary>由 StaticModelGroupEntityData.MemberDatas.MemberType 解出来的</summary>
    StaticModelGroup = 2,
    /// <summary>由 SpatialPrefabBlueprint 的引用层级拍平得到的</summary>
    SpatialPrefab = 4,
}

/// <summary>
/// 某个 WorldPartData 在关卡树里的节点。
/// </summary>
public class WorldPartIndexNode
{
    public EbxAssetEntry Entry;
    public SubWorldIndexNode SubWorld;

    public string Name => Entry?.Filename ?? "(null)";
    public string FullName => Entry?.Name ?? "(null)";
    public Guid FileGuid => Entry?.Guid ?? Guid.Empty;

    /// <summary>该 WorldPart 里直接出现的 ObjectBlueprint 的 FileGuid</summary>
    public HashSet<Guid> BlueprintGuids { get; } = [];

    /// <summary>WorldPart 内对象位置的采样（用于「拖到哪儿就落到最近的 layer」）</summary>
    public List<SharpDX.Vector3> SamplePositions { get; } = [];

    /// <summary>该 WorldPart 里 ObjectReferenceObjectData（真正能放静态模型的层）的数量</summary>
    public int ObjectInstanceCount;

    /// <summary>该 WorldPart 里 EffectReferenceObjectData（vfx / sound 这类系统层）的数量</summary>
    public int EffectInstanceCount;

    /// <summary>该 WorldPart 的 Objects 总数</summary>
    public int TotalObjectCount;

    /// <summary>到某个世界坐标的最近采样距离（没有采样时返回 float.MaxValue）</summary>
    public float GetDistanceTo(SharpDX.Vector3 point)
    {
        float best = float.MaxValue;
        foreach (SharpDX.Vector3 sample in SamplePositions)
        {
            float distance = (sample - point).Length();
            if (distance < best)
                best = distance;
        }

        return best;
    }
}

/// <summary>
/// SubWorld 作为放置目标的归类。
/// </summary>
public enum SubWorldTargetKind
{
    /// <summary>明显是玩法/逻辑层，不能写</summary>
    NotPlaceable = 0,
    /// <summary>名字看不出是内容层，但没有玩法/逻辑特征：只有在关卡没有任何 content 层时才兜底使用</summary>
    Fallback = 1,
    /// <summary>content* 命名层（contentsublevel / content_zone / content_backdrop …），首选落点</summary>
    Content = 2,
    /// <summary>
    /// 美术层（LEVELART）：MP_Underworld / MP_Trench 这类关卡的 content 层只装 ui/sound/lighting/collision，
    /// 真正的 obj 全在 LEVELART 里，因此它的优先级高于 Content。
    /// </summary>
    Art = 3,
}

/// <summary>
/// 某个 SubWorldData 在关卡树里的节点（SubWorld 是嵌套的，例如 AllContentSubLevels 里再套 ContentSubLevel03…）。
/// </summary>
public class SubWorldIndexNode
{
    /// <summary>SubWorldData 的 EBX 名（例如 Levels/MP/MP_Amiens/ContentSubLevel01）</summary>
    public string SubWorldName;
    public Guid FileGuid;
    public SubWorldIndexNode Parent;
    public List<SubWorldIndexNode> Children { get; } = [];
    public List<WorldPartIndexNode> WorldParts { get; } = [];

    /// <summary>该 SubWorld EBX 实际所属的 bundle（通常只有一个，例如 win32/levels/mp/mp_amiens/contentsublevel01）</summary>
    public List<string> BundleNames { get; } = [];

    /// <summary>承载这个 SubWorld 的 sublevel bundle 名（例如 win32/levels/mp/mp_amiens/contentsublevel01）</summary>
    public string BundleName;

    public bool HasWorldParts => WorldParts.Count > 0;

    public bool BelongsToBundle(string bundleName)
    {
        if (string.IsNullOrEmpty(bundleName))
            return false;

        foreach (string name in BundleNames)
        {
            if (name.Equals(bundleName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return !string.IsNullOrEmpty(BundleName) && BundleName.Equals(bundleName, StringComparison.OrdinalIgnoreCase);
    }

    public string DisplayName
    {
        get
        {
            string source = !string.IsNullOrEmpty(BundleName) ? BundleName : SubWorldName;
            if (string.IsNullOrEmpty(source))
                return "(unknown)";
            int idx = source.LastIndexOf('/');
            return idx == -1 ? source : source.Substring(idx + 1);
        }
    }

    public int Depth
    {
        get
        {
            int d = 0;
            for (SubWorldIndexNode n = Parent; n != null; n = n.Parent)
                d++;
            return d;
        }
    }

    public IEnumerable<SubWorldIndexNode> SelfAndDescendants()
    {
        yield return this;
        foreach (SubWorldIndexNode child in Children)
        {
            foreach (SubWorldIndexNode node in child.SelfAndDescendants())
                yield return node;
        }
    }
}

/// <summary>
/// 素材库里的一条 ObjectBlueprint。
/// </summary>
public class ObjectPaletteEntry
{
    public EbxAssetEntry Entry;
    public PaletteSourceFlags Sources;

    /// <summary>陈列归类用的子关卡名（例如 ContentSubLevel01）</summary>
    public readonly List<string> SubWorlds = [];

    /// <summary>该蓝图在关卡里被引用的次数（静态模型组实例数 + 对象引用数）</summary>
    public int UsageCount;

    public string DisplayName => Entry?.Filename ?? "(null)";
    public string FilePath => Entry?.Name ?? "(null)";
    public Guid FileGuid => Entry?.Guid ?? Guid.Empty;

    public ImageSource Icon => Definitions.Icons.ObjectBlueprintImageSource;

    public string SourceText
    {
        get
        {
            List<string> parts = [];
            if ((Sources & PaletteSourceFlags.StaticModelGroup) != 0) parts.Add("StaticModelGroup");
            if ((Sources & PaletteSourceFlags.SpatialPrefab) != 0) parts.Add("Prefab");
            if ((Sources & PaletteSourceFlags.Bundle) != 0) parts.Add("Bundle");
            return string.Join(" / ", parts);
        }
    }

    public string UsageText => UsageCount > 1 ? $"x{UsageCount}" : "";
}

/// <summary>
/// 关卡素材索引：把 LevelData 展开成「SubWorld 树 + WorldPart 列表 + 可用 ObjectBlueprint 列表」。
/// 只读取 EBX，不修改任何资源。
/// </summary>
public sealed class LevelBlueprintIndex
{
    private const int MaxPrefabDepth = 4;
    private const int MaxPrefabCount = 20000;
    private const int MaxSamplePositions = 64;

    public string LevelName { get; private set; } = "";
    public Guid LevelFileGuid { get; private set; }
    public EbxAssetEntry LevelEntry { get; private set; }
    public EbxAsset LevelAsset { get; private set; }

    public List<SubWorldIndexNode> RootNodes { get; } = [];
    public List<ObjectPaletteEntry> Entries { get; } = [];
    public HashSet<string> LevelBundleNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>关卡 super bundle 下的所有 SubLevel bundle（放置目标就在这些里面）</summary>
    public List<BundleEntry> SubLevelBundles { get; } = [];

    private readonly Dictionary<Guid, ObjectPaletteEntry> byGuid = [];
    private readonly Dictionary<string, SubWorldIndexNode> nodeBySubWorldName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SubWorldIndexNode> nodeByBundleName = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Guid> scannedWorldParts = [];
    private readonly HashSet<Guid> scannedSubWorldAssets = [];
    private readonly List<KeyValuePair<SubWorldIndexNode, EbxAssetEntry>> pendingSubWorldScans = [];
    private readonly HashSet<string> visitedSubWorlds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, HashSet<Guid>> prefabCache = [];
    private readonly HashSet<Guid> prefabInProgress = [];
    private int prefabOpened;

    public string LastWarning { get; private set; }

    /// <summary>所有含 WorldPart 的 SubWorld 节点（放置目标候选）</summary>
    public IEnumerable<SubWorldIndexNode> SubWorldsWithWorldParts
        => RootNodes.SelectMany(n => n.SelfAndDescendants()).Where(n => n.HasWorldParts);

    /// <summary>
    /// 可以写入的 SubWorld，按优先级从高到低：
    ///  1. 美术层 LEVELART（MP_Underworld / MP_Trench：content 层只有 ui/sound/lighting/collision，obj 真正在 LEVELART）；
    ///  2. content* 命名层（contentsublevel / contentsl_* / content_zone_* / content_backdrop / content_pois …）；
    ///  3. 整个关卡没有任何 content/art 层时才退回「非玩法/非逻辑」的普通 SubWorld（兜底）。
    ///
    /// 之前只认 contentsublevel*，导致 Xpack 关卡（Xpack3/Levels/MP/MP_Ridge/CONTENT_ZONE_01 这类命名）
    /// 找不到任何落点；Underworld/Trench 则会误落到 CONTENT 系统层。
    /// </summary>
    public IEnumerable<SubWorldIndexNode> PlaceableSubWorldsWithWorldParts
    {
        get
        {
            List<SubWorldIndexNode> all = SubWorldsWithWorldParts.ToList();
            if (all.Count == 0)
                return all;

            // 1) 美术层最优先（Underworld / Trench 只有这种层装着道具）
            List<SubWorldIndexNode> art = all
                .Where(n => ClassifySubWorldNode(n) == SubWorldTargetKind.Art)
                .ToList();
            if (art.Count > 0)
                return art;

            // 2) content* 层
            List<SubWorldIndexNode> content = all
                .Where(n => ClassifySubWorldNode(n) == SubWorldTargetKind.Content)
                .ToList();
            if (content.Count > 0)
                return content;

            // 3) 兜底：这个关卡的 content 层命名完全不是 content*，只要不是明显的玩法/逻辑层就允许写
            return all
                .Where(n => ClassifySubWorldNode(n) == SubWorldTargetKind.Fallback)
                .ToList();
        }
    }

    /// <summary>content* 层（不含 LEVELART 美术层），用于统计/显示。</summary>
    public IEnumerable<SubWorldIndexNode> ContentSubWorldsWithWorldParts
        => SubWorldsWithWorldParts.Where(n => ClassifySubWorldNode(n) == SubWorldTargetKind.Content);

    /// <summary>
    /// 所有「可以写」的 SubWorld（美术层 / content 层 / 兜底层，排除玩法与逻辑层），
    /// 供面板的「目标 SubWorld」下拉框手动指定用（比自动解析的候选范围宽）。
    /// </summary>
    public IEnumerable<SubWorldIndexNode> WritableSubWorldsWithWorldParts
        => SubWorldsWithWorldParts.Where(n => ClassifySubWorldNode(n) != SubWorldTargetKind.NotPlaceable);

    public static bool IsContentSubLevelNode(SubWorldIndexNode node)
    {
        SubWorldTargetKind kind = ClassifySubWorldNode(node);
        return kind == SubWorldTargetKind.Content || kind == SubWorldTargetKind.Art;
    }

    public static bool IsContentSubLevelBundleName(string bundleName)
    {
        SubWorldTargetKind kind = ClassifyBundleOrAssetName(bundleName);
        return kind == SubWorldTargetKind.Content || kind == SubWorldTargetKind.Art;
    }

    /// <summary>某个节点作为放置目标的归类：优先 bundle 名，其次 SubWorld 资源名。</summary>
    public static SubWorldTargetKind ClassifySubWorldNode(SubWorldIndexNode node)
    {
        if (node == null)
            return SubWorldTargetKind.NotPlaceable;

        SubWorldTargetKind kind = ClassifyBundleOrAssetName(node.BundleName);
        foreach (string name in node.BundleNames)
        {
            SubWorldTargetKind other = ClassifyBundleOrAssetName(name);
            if (other > kind)
                kind = other;
        }

        SubWorldTargetKind byAsset = ClassifyBundleOrAssetName(node.SubWorldName);
        if (byAsset > kind)
            kind = byAsset;

        return kind;
    }

    /// <summary>
    /// 按 bundle / asset 路径判断归属：
    ///  - 任一路径段是 levelart*（MP_Underworld / MP_Trench 的美术层）→ Art（优先级最高）
    ///  - 任一路径段是 content*（但排除 allcontentsublevels 这类聚合层）→ Content
    ///  - 任一路径段是明显的玩法/逻辑层（breakthrough0、gameplay、schematic …）→ NotPlaceable
    ///  - 其余 → Fallback（仅在没有任何 Art/Content 层时才用来兜底）
    /// </summary>
    public static SubWorldTargetKind ClassifyBundleOrAssetName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return SubWorldTargetKind.Fallback;

        SubWorldTargetKind kind = SubWorldTargetKind.Fallback;

        foreach (string rawSegment in name.Split('/'))
        {
            string segment = rawSegment.ToLowerInvariant();
            if (segment.Length == 0)
                continue;

            if (IsArtSegment(segment))
                return SubWorldTargetKind.Art;

            if (IsContentSegment(segment))
                return SubWorldTargetKind.Content;

            if (IsNonContentSegment(segment))
                kind = SubWorldTargetKind.NotPlaceable;
        }

        return kind;
    }

    /// <summary>
    /// 美术层命名段：MP_Underworld 的 levelart / levelart2、MP_Trench 的 levelart。
    /// 这类关卡的 content 层只装 ui/sound/lighting/collision，道具全部在 levelart 里。
    /// </summary>
    private static bool IsArtSegment(string segment)
        => segment.StartsWith("levelart");

    /// <summary>
    /// content 命名段：contentsublevel01 / content_zone_01 / content_backdrop / contentzone3 …
    /// 单人关卡的 art_content / art_content_zs_1 也算内容层。
    /// </summary>
    private static bool IsContentSegment(string segment)
    {
        // 聚合母层（本身没有 WorldPartData），不能当成落点
        if (segment.StartsWith("allcontent"))
            return false;

        return segment.Contains("content");
    }

    /// <summary>明显的玩法层 / 逻辑层：Breakthrough0、Breakthrough0_Sector2、Gameplay、Schematic …</summary>
    private static bool IsNonContentSegment(string segment)
    {
        if (segment == "gameplay" || segment == "logic" || segment == "schematic" || segment == "schematicdata")
            return true;

        if (segment.StartsWith("breakthrough") || segment.StartsWith("sector") || segment.StartsWith("spawn"))
            return true;

        // globe / ui / marketing / loading / autotest 这类关卡附属层，不是美术内容层
        if (segment.StartsWith("globe") || segment.StartsWith("ui_") || segment.StartsWith("marketing")
            || segment.StartsWith("loading") || segment.StartsWith("autotest") || segment.StartsWith("debug"))
            return true;

        foreach (string mode in NonContentModeSegments)
        {
            if (segment.StartsWith(mode))
                return true;
        }

        return false;
    }

    private static readonly string[] NonContentModeSegments =
    [
        "conquest", "rush", "operations", "teamdeathmatch", "domination", "dominiation", "frontline",
        "warpidgeons", "supplydrop", "airassault", "elimination", "shockoperations", "possession",
        "tugofwar",
    ];

    private LevelBlueprintIndex()
    {
    }

    #region -- Build --

    public static LevelBlueprintIndex Build(LevelEditor owner, FrostyTaskWindow task = null)
    {
        LevelBlueprintIndex index = new LevelBlueprintIndex();
        index.BuildFromEditor(owner, task);
        return index;
    }

    private void BuildFromEditor(LevelEditor owner, FrostyTaskWindow task)
    {
        Entities.WorldReferenceObject worldRef = owner?.RootLayer?.Entity as Entities.WorldReferenceObject;

        if (worldRef?.Data?.Blueprint.Type == PointerRefType.External)
        {
            LevelFileGuid = worldRef.Data.Blueprint.External.FileGuid;
            LevelEntry = App.AssetManager.GetEbxEntry(LevelFileGuid);
            LevelAsset = worldRef.Data.EbxAsset ?? (LevelEntry != null ? App.AssetManager.GetEbx(LevelEntry) : null);
        }

        // 编辑器还没建好 world 时，退回直接读关卡 EBX
        if (LevelAsset == null)
        {
            EbxAssetEntry fallbackEntry = owner?.LevelAssetEntry;
            if (fallbackEntry != null)
            {
                LevelEntry = fallbackEntry;
                LevelFileGuid = fallbackEntry.Guid;
                try
                {
                    LevelAsset = App.AssetManager.GetEbx(fallbackEntry);
                }
                catch (Exception ex)
                {
                    App.Logger?.LogWarning($"[ObjectPalette] 读取关卡 EBX 失败：{ex.Message}");
                }
            }
        }

        if (LevelAsset == null)
        {
            LastWarning = "无法读取关卡 EBX，素材索引未建立";
            App.Logger?.LogWarning("[ObjectPalette] 无法读取关卡 EBX");
            return;
        }

        try
        {
            LevelName = LevelEntry?.Name ?? Path2.GetName(LevelAsset);

            task?.Update("Collecting level bundles...");
            CollectLevelBundles();

            // 先按 bundle 把 WorldPartData 全部捞出来：这是最可靠的一手数据，
            // 不依赖 SubWorld 引用链是否完整（母 bundle 例如 allcontentsublevels 就没有 WorldPart）。
            task?.Update("Collecting WorldParts from bundles...");
            CollectWorldPartNodes(task);

            task?.Update("Scanning SubWorlds...");
            ScanSubWorldContents(task);

            task?.Update("Walking SubWorlds...");
            SubWorldIndexNode rootNode = new SubWorldIndexNode
            {
                SubWorldName = LevelName,
                FileGuid = LevelFileGuid,
            };
            RootNodes.Insert(0, rootNode);
            nodeBySubWorldName[LevelName] = rootNode;
            if (LevelEntry != null)
            {
                rootNode.BundleNames.AddRange(LevelEntry.EnumerateBundles()
                    .Select(App.AssetManager.GetBundleEntry)
                    .Where(b => b != null)
                    .Select(b => b.Name));
            }

            // 关卡自身的 Objects 里放的是 SubWorldReferenceObjectData / LayerReferenceObjectData 等
            VisitAsset(LevelAsset, LevelEntry, rootNode, 0, task);

            // 只保留有内容的子树
            PruneEmptyNodes();

            task?.Update("Collecting ObjectBlueprints from bundles...");
            CollectBundleBlueprints(task);

            Entries.Sort((a, b) =>
            {
                int c = string.Compare(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase);
                return c;
            });

            App.Logger?.Log($"[ObjectPalette] 索引完成：{Entries.Count} 个 ObjectBlueprint，{PlaceableSubWorldsWithWorldParts.Count()} 个可写 SubWorld（content 层 {ContentSubWorldsWithWorldParts.Count()}，共 {PlaceableSubWorldsWithWorldParts.Sum(n => n.WorldParts.Count)} 个 WorldPartData，扫描 {SubWorldsWithWorldParts.Count()} 个含 WorldPart 的 SubWorld / {SubLevelBundles.Count} 个 sublevel bundle），扫描 prefab {prefabOpened} 个");
        }
        catch (Exception ex)
        {
            LastWarning = ex.Message;
            App.Logger?.LogError($"[ObjectPalette] 建立索引失败：{ex}");
        }
    }

    private static bool PruneEmpty(SubWorldIndexNode node)
    {
        for (int i = node.Children.Count - 1; i >= 0; i--)
        {
            SubWorldIndexNode child = node.Children[i];
            bool keep = PruneEmpty(child);
            if (!keep)
                node.Children.RemoveAt(i);
        }

        return node.HasWorldParts || node.Children.Count > 0;
    }

    /// <summary>把「既没有 WorldPart 也没有子节点」的节点从根列表里清掉。</summary>
    private void PruneEmptyNodes()
    {
        for (int i = RootNodes.Count - 1; i >= 0; i--)
        {
            SubWorldIndexNode node = RootNodes[i];
            PruneEmpty(node);
            if (!node.HasWorldParts && node.Children.Count == 0)
                RootNodes.RemoveAt(i);
        }
    }

    private void CollectLevelBundles()
    {
        List<BundleEntry> levelBundles = [];
        if (LevelEntry != null)
        {
            foreach (int bid in LevelEntry.EnumerateBundles())
            {
                BundleEntry be = App.AssetManager.GetBundleEntry(bid);
                if (be != null)
                    levelBundles.Add(be);
            }
        }

        if (levelBundles.Count > 0)
        {
            int sbId = levelBundles[0].SuperBundleId;
            foreach (BundleEntry be in App.AssetManager.EnumerateBundles(BundleType.SubLevel))
            {
                if (be.SuperBundleId == sbId && !levelBundles.Contains(be))
                    levelBundles.Add(be);
            }
        }

        foreach (BundleEntry be in levelBundles)
        {
            LevelBundleNames.Add(be.Name);
            if (be.Type == BundleType.SubLevel && !SubLevelBundles.Contains(be))
                SubLevelBundles.Add(be);
        }
    }

    /// <summary>
    /// 遍历关卡 super bundle 下所有 SubLevel bundle，收集其中的 WorldPartData 与 SubWorldData。
    /// 这一步决定了「哪些 bundle 可以被写入」，因此不再依赖 SubWorld 引用链。
    /// </summary>
    private void CollectWorldPartNodes(FrostyTaskWindow task)
    {
        int total = Math.Max(1, SubLevelBundles.Count);
        int index = 0;

        foreach (BundleEntry bundle in SubLevelBundles)
        {
            task?.Update($"Scanning {bundle.DisplayName}", (index++ / (double)total) * 100.0);

            SubWorldIndexNode node = GetOrCreateBundleNode(bundle);
            if (node == null)
                continue;

            try
            {
                foreach (EbxAssetEntry ebxEntry in App.AssetManager.EnumerateEbx(bundle))
                {
                    if (ebxEntry == null)
                        continue;

                    if (TypeLibrary.IsSubClassOf(ebxEntry.Type, "WorldPartData"))
                    {
                        AddWorldPart(node, ebxEntry);
                    }
                    else if (node.FileGuid == Guid.Empty && TypeLibrary.IsSubClassOf(ebxEntry.Type, "SubWorldData"))
                    {
                        // 这个 bundle 对应的 SubWorldData（同一 bundle 里通常只有一个）
                        node.SubWorldName = ebxEntry.Name;
                        node.FileGuid = ebxEntry.Guid;
                        nodeBySubWorldName[ebxEntry.Name] = node;
                        pendingSubWorldScans.Add(new KeyValuePair<SubWorldIndexNode, EbxAssetEntry>(node, ebxEntry));
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.LogWarning($"[ObjectPalette] 扫描 bundle {bundle.Name} 失败：{ex.Message}");
            }
        }
    }

    private void AddWorldPart(SubWorldIndexNode node, EbxAssetEntry worldPartEntry)
    {
        if (node == null || worldPartEntry == null)
            return;

        if (node.WorldParts.Any(w => w.FileGuid == worldPartEntry.Guid))
            return;

        WorldPartIndexNode worldPart = new WorldPartIndexNode() { Entry = worldPartEntry, SubWorld = node };
        node.WorldParts.Add(worldPart);

        if (!scannedWorldParts.Add(worldPartEntry.Guid))
            return;

        EbxAsset worldPartAsset = SafeGetEbx(worldPartEntry);
        if (worldPartAsset != null)
            ScanContainerForBlueprints(worldPartAsset, worldPart);
    }

    /// <summary>
    /// 逐个扫描每个 sublevel 的 SubWorldData：
    /// StaticModelGroupEntityData 解包、SpatialPrefab 拍平、嵌套 SubWorld 递归都在这里完成，
    /// 这样即使关卡引用链有断点也不影响素材收集。
    /// </summary>
    private void ScanSubWorldContents(FrostyTaskWindow task)
    {
        int total = Math.Max(1, pendingSubWorldScans.Count);
        int index = 0;

        foreach (KeyValuePair<SubWorldIndexNode, EbxAssetEntry> pair in pendingSubWorldScans)
        {
            SubWorldIndexNode node = pair.Key;
            EbxAssetEntry subEntry = pair.Value;

            task?.Update($"Scanning {node.DisplayName}", (index++ / (double)total) * 100.0);

            if (!scannedSubWorldAssets.Add(subEntry.Guid))
                continue;

            // 标记为已访问，避免后面从关卡根节点递归时重复扫描
            visitedSubWorlds.Add(subEntry.Name);

            nodeBySubWorldName[subEntry.Name] = node;
            VisitAsset(SafeGetEbx(subEntry), subEntry, node, 0, task);
        }
    }

    /// <summary>拿到（或新建）某个 sublevel bundle 对应的节点。</summary>
    private SubWorldIndexNode GetOrCreateBundleNode(BundleEntry bundle)
    {
        if (bundle == null)
            return null;

        if (nodeByBundleName.TryGetValue(bundle.Name, out SubWorldIndexNode existing))
            return existing;

        SubWorldIndexNode node = new SubWorldIndexNode()
        {
            BundleName = bundle.Name,
            SubWorldName = TryDeriveSubWorldName(bundle.Name),
        };
        node.BundleNames.Add(bundle.Name);

        nodeByBundleName[bundle.Name] = node;
        RootNodes.Add(node);
        return node;
    }

    /// <summary>win32/levels/mp/mp_amiens/contentsublevel01 → levels/mp/mp_amiens/contentsublevel01</summary>
    private static string TryDeriveSubWorldName(string bundleName)
    {
        if (string.IsNullOrEmpty(bundleName))
            return bundleName;

        int idx = bundleName.IndexOf("/levels/", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            return bundleName.Substring(idx + 1);

        idx = bundleName.IndexOf("levels/", StringComparison.OrdinalIgnoreCase);
        if (idx > 0)
            return bundleName.Substring(idx);

        return bundleName;
    }

    /// <summary>
    /// 把 SubWorldReferenceObjectData.BundleName 解析成节点。
    /// BundleName 一般是 SubWorldData 的 EBX 名，但也可能是 bundle 名，两种都要支持。
    /// </summary>
    private SubWorldIndexNode ResolveSubWorldNode(string name, out EbxAssetEntry subEntry)
    {
        subEntry = null;
        if (string.IsNullOrEmpty(name))
            return null;

        EbxAssetEntry entry = App.AssetManager.GetEbxEntry(name);
        if (entry != null && TypeLibrary.IsSubClassOf(entry.Type, "SubWorldData"))
        {
            subEntry = entry;
            return GetOrCreateSubWorldNode(entry);
        }

        // 有可能直接写的是 bundle 名
        int bundleId = App.AssetManager.GetBundleId(name);
        if (bundleId == -1)
            return null;

        BundleEntry bundle = App.AssetManager.GetBundleEntry(bundleId);
        SubWorldIndexNode bundleNode = GetOrCreateBundleNode(bundle);
        if (bundleNode == null)
            return null;

        if (bundleNode.FileGuid == Guid.Empty)
        {
            try
            {
                foreach (EbxAssetEntry candidate in App.AssetManager.EnumerateEbx(bundle))
                {
                    if (candidate == null || !TypeLibrary.IsSubClassOf(candidate.Type, "SubWorldData"))
                        continue;

                    bundleNode.SubWorldName = candidate.Name;
                    bundleNode.FileGuid = candidate.Guid;
                    nodeBySubWorldName[candidate.Name] = bundleNode;
                    subEntry = candidate;
                    break;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.LogWarning($"[ObjectPalette] 扫描 bundle {bundle?.Name} 失败：{ex.Message}");
            }
        }
        else
        {
            subEntry = App.AssetManager.GetEbxEntry(bundleNode.SubWorldName);
        }

        return bundleNode;
    }

    /// <summary>按 SubWorldData EBX 找到（或新建）它所属 bundle 的节点。</summary>
    private SubWorldIndexNode GetOrCreateSubWorldNode(EbxAssetEntry subEntry)
    {
        if (nodeBySubWorldName.TryGetValue(subEntry.Name, out SubWorldIndexNode known))
            return known;

        SubWorldIndexNode node = null;
        foreach (int bundleId in subEntry.EnumerateBundles())
        {
            BundleEntry bundle = App.AssetManager.GetBundleEntry(bundleId);
            if (bundle == null)
                continue;

            LevelBundleNames.Add(bundle.Name);

            SubWorldIndexNode bundleNode = GetOrCreateBundleNode(bundle);
            if (bundleNode == null)
                continue;

            if (bundleNode.FileGuid == Guid.Empty || bundleNode.FileGuid == subEntry.Guid)
            {
                bundleNode.SubWorldName = subEntry.Name;
                bundleNode.FileGuid = subEntry.Guid;
                node ??= bundleNode;
            }
        }

        if (node == null)
        {
            node = new SubWorldIndexNode() { SubWorldName = subEntry.Name, FileGuid = subEntry.Guid };
            node.BundleNames.AddRange(subEntry.EnumerateBundles()
                .Select(App.AssetManager.GetBundleEntry)
                .Where(b => b != null)
                .Select(b => b.Name));
            RootNodes.Add(node);
        }

        nodeBySubWorldName[subEntry.Name] = node;
        return node;
    }

    /// <summary>
    /// 遍历一个 SubWorld/关卡 EBX：处理 SubWorld 引用、WorldPart 引用、StaticModelGroup、直接的对象引用。
    /// </summary>
    private void VisitAsset(EbxAsset asset, EbxAssetEntry entry, SubWorldIndexNode node, int depth, FrostyTaskWindow task)
    {
        if (asset == null || depth > 8)
            return;

        IList objects = GetProp(asset.RootObject, "Objects") as IList;
        if (objects == null)
            return;

        foreach (object raw in objects)
        {
            if (raw is not PointerRef pr || pr.Type != PointerRefType.Internal)
                continue;

            object obj = pr.Internal;
            if (obj == null)
                continue;

            if (TypeLibrary.IsSubClassOf(obj, "SubWorldReferenceObjectData"))
            {
                string bundleName = GetProp(obj, "BundleName") as string;
                if (string.IsNullOrEmpty(bundleName))
                    continue;

                if (!visitedSubWorlds.Add(bundleName))
                    continue;

                SubWorldIndexNode child = ResolveSubWorldNode(bundleName, out EbxAssetEntry subEntry);
                if (child == null)
                {
                    App.Logger?.LogWarning($"[ObjectPalette] 无法解析 SubWorld 引用：{bundleName}");
                    continue;
                }

                if (child != node && child.Parent == null)
                    child.Parent = node;

                task?.Update($"Scanning {child.DisplayName}");
                if (subEntry != null)
                    VisitAsset(SafeGetEbx(subEntry), subEntry, child, depth + 1, task);
            }
            else if (TypeLibrary.IsSubClassOf(obj, "WorldPartReferenceObjectData"))
            {
                PointerRef bpRef = GetProp(obj, "Blueprint") as PointerRef? ?? default;
                if (bpRef.Type != PointerRefType.External)
                    continue;

                EbxAssetEntry wpEntry = App.AssetManager.GetEbxEntry(bpRef.External.FileGuid);
                if (wpEntry == null || !TypeLibrary.IsSubClassOf(wpEntry.Type, "WorldPartData"))
                    continue;

                AddWorldPart(node, wpEntry);
            }
            else if (TypeLibrary.IsSubClassOf(obj, "StaticModelGroupEntityData"))
            {
                CollectStaticModelGroupBlueprints(obj, node);
            }
            else if (TypeLibrary.IsSubClassOf(obj, "SpatialPrefabReferenceObjectData") ||
                     TypeLibrary.IsSubClassOf(obj, "ObjectReferenceObjectData"))
            {
                PointerRef bpRef = GetProp(obj, "Blueprint") as PointerRef? ?? default;
                CollectBlueprintReference(bpRef, node, 0);
            }
        }
    }

    /// <summary>
    /// 扫描 WorldPart（或任意容器 EBX）里直接引用的 ObjectBlueprint / SpatialPrefab。
    /// </summary>
    private void ScanContainerForBlueprints(EbxAsset asset, WorldPartIndexNode wpNode)
    {
        IList objects = GetProp(asset.RootObject, "Objects") as IList;
        if (objects == null)
            return;

        int stride = Math.Max(1, objects.Count / MaxSamplePositions);
        int cursor = 0;

        if (wpNode != null)
            wpNode.TotalObjectCount = Math.Max(wpNode.TotalObjectCount, objects.Count);

        foreach (object raw in objects)
        {
            if (raw is not PointerRef pr || pr.Type != PointerRefType.Internal)
                continue;

            object obj = pr.Internal;
            if (obj == null)
                continue;

            // 统计层的「内容类型」：只有 ObjectReferenceObjectData 才是能放静态模型的美术层，
            // EffectReferenceObjectData 是 vfx/sound 之类系统层，绝不能往上写道具。
            if (wpNode != null)
            {
                if (TypeLibrary.IsSubClassOf(obj, "ObjectReferenceObjectData"))
                    wpNode.ObjectInstanceCount++;
                else if (TypeLibrary.IsSubClassOf(obj, "EffectReferenceObjectData"))
                    wpNode.EffectInstanceCount++;
            }

            // 采样一部分对象的世界坐标，用来判断「拖到哪儿 → 落到哪个 layer」
            if (cursor++ % stride == 0)
                AddSamplePosition(wpNode, obj);

            PointerRef bpRef = GetProp(obj, "Blueprint") as PointerRef? ?? default;
            if (bpRef.Type != PointerRefType.External)
                continue;

            EbxAssetEntry bpEntry = App.AssetManager.GetEbxEntry(bpRef.External.FileGuid);
            if (bpEntry == null)
                continue;

            if (TypeLibrary.IsSubClassOf(bpEntry.Type, "SpatialPrefabBlueprint"))
            {
                HashSet<Guid> flat = GetPrefabBlueprints(bpEntry, 0);
                foreach (Guid guid in flat)
                {
                    ObjectPaletteEntry paletteEntry = Register(guid, PaletteSourceFlags.SpatialPrefab);
                    if (paletteEntry == null)
                        continue;
                    paletteEntry.UsageCount++;
                    wpNode?.BlueprintGuids.Add(guid);
                }
            }
            else if (TypeLibrary.IsSubClassOf(bpEntry.Type, "ObjectBlueprint"))
            {
                ObjectPaletteEntry paletteEntry = Register(bpEntry, PaletteSourceFlags.SpatialPrefab);
                if (paletteEntry != null)
                {
                    paletteEntry.UsageCount++;
                    wpNode?.BlueprintGuids.Add(bpEntry.Guid);
                }
            }
        }
    }

    /// <summary>记录一个对象的世界坐标（用于「拖到哪儿就落到最近的 layer」）。</summary>
    private static void AddSamplePosition(WorldPartIndexNode wpNode, object obj)
    {
        if (wpNode == null || wpNode.SamplePositions.Count >= MaxSamplePositions)
            return;

        object transform = GetProp(obj, "BlueprintTransform");
        if (transform == null)
            return;

        object trans = GetProp(transform, "trans");
        if (trans == null)
            return;

        try
        {
            float x = Convert.ToSingle(GetProp(trans, "x") ?? 0.0f);
            float y = Convert.ToSingle(GetProp(trans, "y") ?? 0.0f);
            float z = Convert.ToSingle(GetProp(trans, "z") ?? 0.0f);
            wpNode.SamplePositions.Add(new SharpDX.Vector3(x, y, z));
        }
        catch (Exception)
        {
        }
    }

    private void CollectStaticModelGroupBlueprints(object group, SubWorldIndexNode node)
    {
        IList memberDatas = GetProp(group, "MemberDatas") as IList;
        if (memberDatas == null)
            return;

        foreach (object member in memberDatas)
        {
            if (member == null)
                continue;

            PointerRef memberType = GetProp(member, "MemberType") as PointerRef? ?? default;
            int instanceCount = Convert.ToInt32(GetProp(member, "InstanceCount") ?? 0);

            if (memberType.Type == PointerRefType.External)
            {
                EbxAssetEntry bpEntry = App.AssetManager.GetEbxEntry(memberType.External.FileGuid);
                if (bpEntry == null)
                    continue;

                if (TypeLibrary.IsSubClassOf(bpEntry.Type, "ObjectBlueprint"))
                {
                    ObjectPaletteEntry paletteEntry = Register(bpEntry, PaletteSourceFlags.StaticModelGroup);
                    if (paletteEntry != null)
                    {
                        paletteEntry.UsageCount += Math.Max(1, instanceCount);
                        AddSubWorldTag(paletteEntry, node);
                    }
                }
                else if (TypeLibrary.IsSubClassOf(bpEntry.Type, "SpatialPrefabBlueprint"))
                {
                    foreach (Guid guid in GetPrefabBlueprints(bpEntry, 0))
                    {
                        ObjectPaletteEntry paletteEntry = Register(guid, PaletteSourceFlags.StaticModelGroup);
                        if (paletteEntry == null)
                            continue;
                        paletteEntry.UsageCount += Math.Max(1, instanceCount);
                        AddSubWorldTag(paletteEntry, node);
                    }
                }
            }
            else if (memberType.Type == PointerRefType.Internal && memberType.Internal != null &&
                     TypeLibrary.IsSubClassOf(memberType.Internal, "Blueprint"))
            {
                // 内部蓝图：直接取其 Objects 里的 ObjectReferenceObjectData
                if (GetProp(memberType.Internal, "Objects") is IList innerObjects)
                {
                    foreach (object rawObj in innerObjects)
                    {
                        if (rawObj is PointerRef inner && inner.Type == PointerRefType.Internal &&
                            TypeLibrary.IsSubClassOf(inner.Internal, "ObjectReferenceObjectData"))
                        {
                            PointerRef innerBp = GetProp(inner.Internal, "Blueprint") as PointerRef? ?? default;
                            CollectBlueprintReference(innerBp, node, 0);
                        }
                    }
                }
            }
        }
    }

    private void CollectBlueprintReference(PointerRef bpRef, SubWorldIndexNode node, int depth)
    {
        if (bpRef.Type != PointerRefType.External)
            return;

        EbxAssetEntry bpEntry = App.AssetManager.GetEbxEntry(bpRef.External.FileGuid);
        if (bpEntry == null)
            return;

        if (TypeLibrary.IsSubClassOf(bpEntry.Type, "SpatialPrefabBlueprint"))
        {
            foreach (Guid guid in GetPrefabBlueprints(bpEntry, depth))
            {
                ObjectPaletteEntry paletteEntry = Register(guid, PaletteSourceFlags.SpatialPrefab);
                if (paletteEntry != null)
                {
                    paletteEntry.UsageCount++;
                    AddSubWorldTag(paletteEntry, node);
                }
            }
        }
        else if (TypeLibrary.IsSubClassOf(bpEntry.Type, "ObjectBlueprint"))
        {
            ObjectPaletteEntry paletteEntry = Register(bpEntry, PaletteSourceFlags.SpatialPrefab);
            if (paletteEntry != null)
            {
                paletteEntry.UsageCount++;
                AddSubWorldTag(paletteEntry, node);
            }
        }
    }

    /// <summary>
    /// 把一个 SpatialPrefabBlueprint 的引用层级拍平成它最终引用的 ObjectBlueprint 集合（带 memo，防止环）。
    /// </summary>
    private HashSet<Guid> GetPrefabBlueprints(EbxAssetEntry prefabEntry, int depth)
    {
        HashSet<Guid> result = [];
        if (prefabEntry == null || depth > MaxPrefabDepth)
            return result;

        if (prefabCache.TryGetValue(prefabEntry.Guid, out HashSet<Guid> cached))
            return cached;

        if (!prefabInProgress.Add(prefabEntry.Guid))
            return result;

        try
        {
            if (++prefabOpened > MaxPrefabCount)
                return result;

            EbxAsset asset = SafeGetEbx(prefabEntry);
            if (asset == null)
                return result;

            IList objects = GetProp(asset.RootObject, "Objects") as IList;
            if (objects == null)
                return result;

            foreach (object raw in objects)
            {
                if (raw is not PointerRef pr || pr.Type != PointerRefType.Internal)
                    continue;

                object obj = pr.Internal;
                if (obj == null)
                    continue;

                PointerRef bpRef = GetProp(obj, "Blueprint") as PointerRef? ?? default;
                if (bpRef.Type != PointerRefType.External)
                    continue;

                EbxAssetEntry bpEntry = App.AssetManager.GetEbxEntry(bpRef.External.FileGuid);
                if (bpEntry == null)
                    continue;

                if (TypeLibrary.IsSubClassOf(bpEntry.Type, "ObjectBlueprint"))
                {
                    result.Add(bpEntry.Guid);
                }
                else if (TypeLibrary.IsSubClassOf(bpEntry.Type, "SpatialPrefabBlueprint"))
                {
                    result.UnionWith(GetPrefabBlueprints(bpEntry, depth + 1));
                }
            }

            prefabCache[prefabEntry.Guid] = result;
            return result;
        }
        finally
        {
            prefabInProgress.Remove(prefabEntry.Guid);
        }
    }

    private void CollectBundleBlueprints(FrostyTaskWindow task)
    {
        // 直接用 SubLevelBundles（CollectLevelBundles 已经算好），避免依赖名字反查
        int index = 0;
        foreach (BundleEntry be in SubLevelBundles)
        {
            task?.Update($"Scanning bundle {be.DisplayName}", (index++ / (double)Math.Max(1, SubLevelBundles.Count)) * 100.0);

            // 该 bundle 对应的 SubWorld 节点（用于归类）
            nodeByBundleName.TryGetValue(be.Name, out SubWorldIndexNode node);

            try
            {
                foreach (EbxAssetEntry ebxEntry in App.AssetManager.EnumerateEbx(be))
                {
                    if (ebxEntry == null || !TypeLibrary.IsSubClassOf(ebxEntry.Type, "ObjectBlueprint"))
                        continue;

                    ObjectPaletteEntry entry = Register(ebxEntry, PaletteSourceFlags.Bundle);
                    if (entry != null)
                        AddSubWorldTag(entry, node);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.LogWarning($"[ObjectPalette] 扫描 bundle {be.Name} 失败：{ex.Message}");
            }
        }
    }

    #endregion

    #region -- Helpers --

    private void AddSubWorldTag(ObjectPaletteEntry entry, SubWorldIndexNode node)
    {
        if (entry == null || node == null)
            return;

        string name = node.DisplayName;
        if (string.IsNullOrEmpty(name))
            return;
        if (!entry.SubWorlds.Contains(name))
            entry.SubWorlds.Add(name);
    }

    private ObjectPaletteEntry Register(EbxAssetEntry bpEntry, PaletteSourceFlags source)
    {
        if (bpEntry == null)
            return null;

        // 组件蓝图（例如 ArtAssets/Vegetation/.../Components/WF_TreeAshMedium_Clustered_01_Skinned）
        // 是别的蓝图拆出来的子部件，单独实例化进 WorldPartData 会让游戏崩溃，不放进素材库。
        if (IsComponentBlueprint(bpEntry))
        {
            if (skippedComponents.Add(bpEntry.Guid))
                App.Logger?.LogWarning($"[ObjectPalette] 跳过组件蓝图（不能单独放置）：{bpEntry.Name}");
            return null;
        }

        if (byGuid.TryGetValue(bpEntry.Guid, out ObjectPaletteEntry existing))
        {
            existing.Sources |= source;
            return existing;
        }

        ObjectPaletteEntry entry = new ObjectPaletteEntry() { Entry = bpEntry, Sources = source };
        byGuid[bpEntry.Guid] = entry;
        Entries.Add(entry);
        return entry;
    }

    private readonly HashSet<Guid> skippedComponents = [];

    /// <summary>判断是不是「被别的蓝图引用的组件」——这类蓝图不能当独立道具放置。</summary>
    private static bool IsComponentBlueprint(EbxAssetEntry entry)
    {
        string path = (entry.Path ?? "").Replace('\\', '/').ToLowerInvariant();
        string file = (entry.Filename ?? "").ToLowerInvariant();

        if (file.EndsWith("_skinned") || file.EndsWith("_skinnedmesh"))
            return true;

        return path.Contains("/components/") && path.Contains("artassets");
    }

    private ObjectPaletteEntry Register(Guid guid, PaletteSourceFlags source)
    {
        EbxAssetEntry bpEntry = App.AssetManager.GetEbxEntry(guid);
        if (bpEntry == null || !TypeLibrary.IsSubClassOf(bpEntry.Type, "ObjectBlueprint"))
            return null;
        return Register(bpEntry, source);
    }

    private static EbxAsset SafeGetEbx(EbxAssetEntry entry)
    {
        try
        {
            return App.AssetManager.GetEbx(entry);
        }
        catch (Exception ex)
        {
            App.Logger?.LogWarning($"[ObjectPalette] 读取 {entry?.Name} 失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 把一个 SpatialPrefabBlueprint 拍平成它最终引用的 ObjectBlueprint 列表（供数据浏览器拖拽使用）。
    /// </summary>
    public List<EbxAssetEntry> FlattenPrefabBlueprints(EbxAssetEntry prefabEntry)
    {
        List<EbxAssetEntry> result = [];
        if (prefabEntry == null)
            return result;

        foreach (Guid guid in GetPrefabBlueprints(prefabEntry, 0))
        {
            EbxAssetEntry entry = App.AssetManager.GetEbxEntry(guid);
            if (entry != null)
                result.Add(entry);
        }

        return result;
    }

    private static object GetProp(object obj, string name)
    {
        if (obj == null)
            return null;
        PropertyInfo pi = obj.GetType().GetProperty(name);
        return pi == null ? null : pi.GetValue(obj);
    }

    private static class Path2
    {
        public static string GetName(EbxAsset asset)
        {
            return GetProp(asset?.RootObject, "Name") as string ?? "";
        }
    }

    #endregion
}
