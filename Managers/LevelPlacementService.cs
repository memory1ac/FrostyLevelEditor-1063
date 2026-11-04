using Frosty.Core;
using FrostySdk;
using FrostySdk.Ebx;
using FrostySdk.IO;
using FrostySdk.Managers;
using LevelEditorPlugin.BF1Core.Utils;
using LevelEditorPlugin.Editors;
using LevelEditorPlugin.Entities;
using LevelEditorPlugin.Layers;
using SharpDX;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Entity = LevelEditorPlugin.Entities.Entity;

namespace LevelEditorPlugin.Managers;

public class PlacementResult
{
    public bool Success;
    public string Message;
    public SubWorldIndexNode SubWorld;
    public WorldPartIndexNode WorldPart;
}

/// <summary>
/// 负责把素材库里的 ObjectBlueprint 实例化到正确的 content SubWorld 的 WorldPartData 里。
///
/// 目标解析规则（用户核心诉求）：
///  1. 合法落点是 content 层：contentsublevel01 / content_zone_01 / content_backdrop / art_content 之类；
///     allcontentsublevels、breakthrough0_sector2、gameplay、conquest0 之类一律不写。
///     如果整个关卡一个 content 命名的层都没有，才退回「非玩法/非逻辑」的普通 SubWorld（兜底）。
///  2. 蓝图所属 bundle 就是某个 content 层 → 必须落在那一个。
///  3. 蓝图属于母/共享 bundle（例如 allcontentsublevels，本身没有 WorldPartData）→ 任意 content 层都可以放，
///     优先「当前选中 layer」→「离落点最近的 content 层」→「已经用过该蓝图的 content 层」→ 名字打分。
///  4. WorldPartData：选中的 layer &gt; 离落点最近 &gt; 已经含有该蓝图 &gt; 名字打分（props/backdrop 加分，collision 减分）。
///
/// 只写 EBX：不注册 NetworkRegistry，也不改 bundle 成员（蓝图本来就在关卡 bundle 里）。
/// </summary>
public static class LevelPlacementService
{
    public static PlacementResult Place(LevelEditor owner, LevelBlueprintIndex index, ObjectPaletteEntry paletteEntry, Vector3 worldPoint, SubWorldIndexNode forcedSubWorld = null)
    {
        PlacementResult result = new PlacementResult();

        if (owner == null || paletteEntry?.Entry == null)
        {
            result.Message = "无效的放置请求";
            return result;
        }

        bool hasForcedTarget = forcedSubWorld != null && forcedSubWorld.HasWorldParts;

        // 索引有可能是在关卡还没加载完（WorldPart 还没建出来）时建立的，这里兜底重建一次
        if (index == null || (!HasPlaceableTargets(index) && !hasForcedTarget))
        {
            try
            {
                owner.RebuildBlueprintIndex(null);
                index = owner.BlueprintIndex;
            }
            catch (Exception ex)
            {
                App.Logger?.LogWarning($"[ObjectPalette] 重建索引失败：{ex.Message}");
            }
        }

        hasForcedTarget = forcedSubWorld != null && forcedSubWorld.HasWorldParts;

        if (index == null || (!HasPlaceableTargets(index) && !hasForcedTarget))
        {
            result.Message = "关卡里没有可写入的 content SubWorld（WorldPartData）";
            App.Logger?.LogWarning($"[ObjectPalette] 放置失败：没有可写的 SubWorld（sublevel bundle {index?.SubLevelBundles.Count ?? 0} 个，蓝图 {paletteEntry.FilePath}）");
            return result;
        }

        WorldPartIndexNode selectedWorldPart = ResolveSelectedWorldPart(owner, index);

        SubWorldIndexNode targetSubWorld = hasForcedTarget
            ? forcedSubWorld
            : ResolveTargetSubWorld(index, paletteEntry, selectedWorldPart, worldPoint);
        if (targetSubWorld == null)
        {
            result.Message = "找不到可用的 content SubWorld";
            return result;
        }

        WorldPartIndexNode targetWorldPart = ResolveTargetWorldPart(owner, targetSubWorld, selectedWorldPart, paletteEntry, worldPoint);
        if (targetWorldPart == null)
        {
            result.Message = $"{targetSubWorld.DisplayName} 里没有可写入的 WorldPartData";
            return result;
        }

        // 最后一道保险：vfx / sound / lighting / collision 这类系统层绝不能写道具（写进去游戏会崩）
        if (ScoreWorldPart(targetWorldPart) <= MinPlaceableWorldPartScore)
        {
            result.Message = $"{targetWorldPart.Entry.Filename} 是特效/声音/灯光之类系统层，不能放静态道具，请手动切换 Target SubWorld";
            App.Logger?.LogWarning($"[ObjectPalette] 拒绝写入系统层：{targetWorldPart.Entry.Name}");
            return result;
        }

        result.SubWorld = targetSubWorld;
        result.WorldPart = targetWorldPart;

        bool written = false;
        string errorText = null;
        Guid instanceGuid = Guid.Empty;

        PlacementTransaction txn = new PlacementTransaction()
        {
            Owner = owner,
            Entry = paletteEntry,
            WorldPart = targetWorldPart,
        };

        Frosty.Core.Managers.UndoManager.Instance.CommitUndo(
            new Frosty.Core.Managers.GenericUndoUnit($"Place {paletteEntry.DisplayName}",
                o =>
                {
                    written = WriteObjectReference(txn, worldPoint, out instanceGuid, out errorText);
                },
                o =>
                {
                    RemoveObjectReference(txn);
                }));

        if (!written)
        {
            result.Message = errorText ?? "写入失败";
            return result;
        }

        result.Success = true;
        result.Message = $"已放置到 {targetSubWorld.DisplayName} / {targetWorldPart.Entry.Filename}";

        App.Logger?.Log($"[ObjectPalette] {paletteEntry.FilePath} → {targetWorldPart.Entry.Name} (guid {instanceGuid})");
        return result;
    }

    /// <summary>一次放置的上下文，撤销时需要它把东西清回去</summary>
    private sealed class PlacementTransaction
    {
        public LevelEditor Owner;
        public ObjectPaletteEntry Entry;
        public WorldPartIndexNode WorldPart;

        public EbxAsset WorldPartAsset;
        public object NewObject;
        public Entity LiveEntity;
    }

    /// <summary>撤销：把刚写进去的 EBX 对象和视口实体清掉</summary>
    private static void RemoveObjectReference(PlacementTransaction txn)
    {
        try
        {
            if (txn.WorldPartAsset != null && txn.NewObject != null)
            {
                dynamic root = txn.WorldPartAsset.RootObject;
                if (root.Objects is IList objects)
                {
                    for (int i = objects.Count - 1; i >= 0; i--)
                    {
                        if (objects[i] is PointerRef pr && pr.Type == PointerRefType.Internal && ReferenceEquals(pr.Internal, txn.NewObject))
                            objects.RemoveAt(i);
                    }
                }

                txn.WorldPartAsset.RemoveObject(txn.NewObject);
                txn.WorldPartAsset.Update();
                App.AssetManager.ModifyEbx(txn.WorldPart.Entry.Name, txn.WorldPartAsset);
                txn.WorldPart.BlueprintGuids.Remove(txn.Entry.FileGuid);
            }

            if (txn.LiveEntity != null && txn.Owner?.Screen != null)
            {
                txn.Owner.Screen.DeleteEntityFromRenderProxy(txn.LiveEntity);
                if (txn.WorldPart != null)
                {
                    List<SceneLayer> layers = [];
                    txn.Owner.RootLayer?.CollectLayers(layers);
                    foreach (SceneLayer layer in layers)
                    {
                        if (layer.Entity is WorldPartReferenceObject wp && wp.Blueprint?.FileGuid == txn.WorldPart.FileGuid)
                        {
                            wp.DeleteEntity(txn.LiveEntity);
                            break;
                        }
                    }
                }
                MeshUtil.NotifyLayerReloaded();
            }
        }
        catch (Exception ex)
        {
            App.Logger?.LogError($"[ObjectPalette] 撤销放置失败：{ex}");
        }
    }

    #region -- 目标解析 --

    private static IEnumerable<SubWorldIndexNode> AllNodes(LevelBlueprintIndex index)
        => index.RootNodes.SelectMany(n => n.SelfAndDescendants());

    /// <summary>当前选中 layer 的 WorldPart（如果是的话）</summary>
    private static WorldPartIndexNode ResolveSelectedWorldPart(LevelEditor owner, LevelBlueprintIndex index)
    {
        if (owner.SelectedLayer?.Entity is not WorldPartReferenceObject worldPartRef)
            return null;

        Guid fileGuid = worldPartRef.Blueprint?.FileGuid ?? Guid.Empty;
        if (fileGuid == Guid.Empty)
            return null;

        foreach (SubWorldIndexNode node in AllNodes(index))
        {
            WorldPartIndexNode wp = node.WorldParts.FirstOrDefault(w => w.FileGuid == fileGuid);
            if (wp != null)
                return wp;
        }

        return null;
    }

    private static SubWorldIndexNode ResolveTargetSubWorld(
        LevelBlueprintIndex index,
        ObjectPaletteEntry paletteEntry,
        WorldPartIndexNode selectedWorldPart,
        Vector3 worldPoint)
    {
        HashSet<string> entryBundles = GetBundleNames(paletteEntry.Entry);

        // 只有 content* 层（contentsublevel01 / content_zone_01 / content_backdrop …）才是首选落点；
        // allcontentsublevels / breakthrough0_sector2 / gameplay 之类不写。
        // 关卡完全没有 content 层命名时，PlaceableSubWorldsWithWorldParts 自己会退回普通 SubWorld。
        List<SubWorldIndexNode> contentSubLevels = index.PlaceableSubWorldsWithWorldParts.ToList();

        if (contentSubLevels.Count == 0)
            return null;

        // 1) 蓝图自己就挂在某个 contentsublevel 上（例如 contentsublevel01）→ 只能落在那个 sublevel
        List<SubWorldIndexNode> exact = contentSubLevels
            .Where(n => n.BundleNames.Any(b => entryBundles.Contains(b)))
            .ToList();

        // 2) 蓝图属于母/共享 bundle（例如 allcontentsublevels，本身没有 WorldPartData）
        //    → 任何 contentsublevel 都可以放。
        //    注意：不能只用 exact 当候选池，因为 WF_TreeAsh 这类蓝图同时挂在 mp_ridge/content（系统层）
        //    和 allcontentsublevels 上，只用 exact 会把落点锁死在 content 这种系统层上。

        // 0) 只保留「至少有一个能写道具的 WorldPart」的 SubWorld。
        //    这一步专门挡住 MP_Ridge 的 content 层：它底下全是 vfx / sound / lighting / schematic /
        //    levolution，没有任何美术层，道具写进去游戏会崩。
        List<SubWorldIndexNode> placeable = contentSubLevels.Where(HasPlaceableWorldPart).ToList();
        if (placeable.Count == 0)
            return null;

        // 0b) 关卡按区域/子关卡切分时（content_zone_01 / contentsublevel01 / contentsl_1 / content_02 /
        //     LEVELART），优先在这一档里挑；泛用的 content / content_backdrop 只当兜底。
        List<SubWorldIndexNode> zoneTier = placeable.Where(IsZonePartitionedSubWorld).ToList();
        if (zoneTier.Count > 0)
            placeable = zoneTier;

        // 0c) 蓝图自己挂的 sublevel 只有「可写」时才算硬约束。
        //     例如 WF_TreeAsh... 同时挂在 mp_ridge/content（系统层）和 allcontentsublevels 上，
        //     不能因为它出现在 content bundle 里就把落点锁死到 content。
        if (exact.Count > 0)
        {
            List<SubWorldIndexNode> exactPlaceable = exact.Intersect(placeable).ToList();
            if (exactPlaceable.Count > 0)
                placeable = exactPlaceable;
        }

        // 2a) 当前选中 layer 所在的 sublevel 优先（但选中的层本身不能是系统层）
        if (selectedWorldPart != null
            && placeable.Contains(selectedWorldPart.SubWorld)
            && ScoreWorldPart(selectedWorldPart) > MinPlaceableWorldPartScore)
            return selectedWorldPart.SubWorld;

        // 2b) 离落点最近的 sublevel（拖到哪儿就落在那个 sublevel，只统计可写的 WorldPart）
        SubWorldIndexNode nearest = placeable
            .Where(n => GetNearestPlaceableDistance(n, worldPoint) < float.MaxValue)
            .OrderBy(n => GetNearestPlaceableDistance(n, worldPoint))
            .FirstOrDefault();
        if (nearest != null && GetNearestPlaceableDistance(nearest, worldPoint) <= 250.0f)
            return nearest;

        // 2c) 已经用过这个蓝图的 sublevel 优先（同素材尽量集中）
        SubWorldIndexNode used = placeable
            .Where(n => n.WorldParts.Any(w => w.BlueprintGuids.Contains(paletteEntry.FileGuid)
                                              && ScoreWorldPart(w) > MinPlaceableWorldPartScore))
            .OrderByDescending(n => n.WorldParts.Max(w => ScoreWorldPart(w)))
            .FirstOrDefault();
        if (used != null)
            return used;

        // 2d) 命中了具体 bundle → 取最具体（bundle 名最长）的那个
        if (exact.Count > 0)
        {
            SubWorldIndexNode exactPick = exact.Intersect(placeable)
                .OrderByDescending(n => n.BundleNames.Max(b => b.Length))
                .ThenByDescending(n => n.WorldParts.Count)
                .FirstOrDefault();
            if (exactPick != null)
                return exactPick;
        }

        // 2e) 共享 bundle 素材：按名字打分挑最像场景内容的 sublevel
        return placeable
            .OrderByDescending(n => ScoreSubWorld(n))
            .ThenByDescending(n => n.WorldParts.Max(w => ScoreWorldPart(w)))
            .First();
    }

    /// <summary>SubWorld 里是否至少有一个「能写道具的美术层」</summary>
    private static bool HasPlaceableWorldPart(SubWorldIndexNode node)
        => node != null && node.WorldParts.Any(w => ScoreWorldPart(w) > MinPlaceableWorldPartScore);

    /// <summary>
    /// SubWorld 是否按区域 / 子关卡切分：
    /// content_zone_01、contentsublevel01、contentsl_3、content_02、LEVELART、contentzone3 …
    /// （content_backdrop 是远景层，不算道具落点）
    /// </summary>
    private static bool IsZonePartitionedSubWorld(SubWorldIndexNode node)
    {
        string name = (node?.BundleName ?? node?.SubWorldName ?? "").ToLowerInvariant();
        if (name.Length == 0)
            return false;

        if (name.Contains("backdrop"))
            return false;

        if (name.Contains("zone") || name.Contains("contentsublevel") || name.Contains("contentsl")
            || name.Contains("levelart"))
            return true;

        // content_01 / content_2 / contentzone3 这类编号子关卡
        return System.Text.RegularExpressions.Regex.IsMatch(name, @"content_?\d+");
    }

    private static bool HasPlaceableTargets(LevelBlueprintIndex index)
        => index != null && index.PlaceableSubWorldsWithWorldParts.Any();

    private static float GetNearestDistance(SubWorldIndexNode node, Vector3 worldPoint)
    {
        float best = float.MaxValue;
        foreach (WorldPartIndexNode worldPart in node.WorldParts)
        {
            if (worldPart.SamplePositions.Count == 0)
                continue;
            best = Math.Min(best, worldPart.GetDistanceTo(worldPoint));
        }

        return best;
    }

    /// <summary>只统计「能写道具」的 WorldPart 的最近距离（vfx / sound / lighting 层不参与）</summary>
    private static float GetNearestPlaceableDistance(SubWorldIndexNode node, Vector3 worldPoint)
    {
        float best = float.MaxValue;
        foreach (WorldPartIndexNode worldPart in node.WorldParts)
        {
            if (worldPart.SamplePositions.Count == 0)
                continue;
            if (ScoreWorldPart(worldPart) <= MinPlaceableWorldPartScore)
                continue;
            best = Math.Min(best, worldPart.GetDistanceTo(worldPoint));
        }

        return best;
    }

    /// <summary>
    /// 候选 SubWorld 的优先级打分：LEVELART 美术层 &gt; content 层 &gt; 兜底，WorldPart 越多越优先。
    /// 同时兼顾 contentsublevel01、contentsl_*、Xpack 的 content_zone_01 / content_backdrop 命名。
    /// </summary>
    private static int ScoreSubWorld(SubWorldIndexNode node)
    {
        string name = (node.BundleName ?? node.SubWorldName ?? "").ToLowerInvariant();
        int score = 0;

        if (name.Contains("allcontentsublevel"))
            score -= 200;          // 母 bundle 本身没有 WorldPart，正常不会出现在候选里

        switch (LevelBlueprintIndex.ClassifySubWorldNode(node))
        {
            case SubWorldTargetKind.Art:
                score += 200;      // LEVELART 美术层：道具真正的落点
                break;
            case SubWorldTargetKind.Content:
                score += 100;
                break;
            case SubWorldTargetKind.Fallback:
                score += 20;       // 兜底层
                break;
            default:
                score -= 300;      // 玩法/逻辑层
                break;
        }

        if (name.Contains("levelart"))
            score += 30;
        if (name.Contains("zone"))
            score += 20;           // content_zone_01 这类按区域切分的艺术层
        if (name.Contains("backdrop"))
            score += 10;
        if (name.EndsWith("/content", StringComparison.OrdinalIgnoreCase))
            score += 20;

        score += Math.Min(20, node.WorldParts.Count);

        if (node.WorldParts.Count > 0)
            score += node.WorldParts.Max(w => ScoreWorldPart(w)) / 10;

        return score;
    }

    private static WorldPartIndexNode ResolveTargetWorldPart(
        LevelEditor owner,
        SubWorldIndexNode subWorld,
        WorldPartIndexNode selectedWorldPart,
        ObjectPaletteEntry paletteEntry,
        Vector3 worldPoint)
    {
        // 1. 当前选中的 layer 就在这个 SubWorld 下 → 直接用它（系统层除外）
        if (selectedWorldPart != null
            && selectedWorldPart.SubWorld == subWorld
            && ScoreWorldPart(selectedWorldPart) > MinPlaceableWorldPartScore)
            return selectedWorldPart;

        // 2. 离落点最近、且能放装饰物的美术层（vfx/sound/lighting/collision 这类层直接不算候选）
        WorldPartIndexNode nearest = subWorld.WorldParts
            .Where(w => w.SamplePositions.Count > 0 && ScoreWorldPart(w) > MinPlaceableWorldPartScore)
            .OrderBy(w => w.GetDistanceTo(worldPoint) - ScoreWorldPart(w) * 2.0f)
            .FirstOrDefault();
        if (nearest != null && nearest.GetDistanceTo(worldPoint) < 500.0f)
            return nearest;

        // 3. 已经含有该蓝图的 WorldPart（只在可写层里找）
        WorldPartIndexNode existing = subWorld.WorldParts
            .Where(w => w.BlueprintGuids.Contains(paletteEntry.FileGuid)
                        && ScoreWorldPart(w) > MinPlaceableWorldPartScore)
            .OrderByDescending(w => ScoreWorldPart(w))
            .FirstOrDefault();
        if (existing != null)
            return existing;

        // 4. 名字打分（同样只在可写层里挑，绝不能回落到 vfx 之类的系统层）
        return subWorld.WorldParts
            .Where(w => ScoreWorldPart(w) > MinPlaceableWorldPartScore)
            .OrderByDescending(w => ScoreWorldPart(w))
            .FirstOrDefault();
    }

    private static int ScoreWorldPart(WorldPartIndexNode worldPart)
    {
        if (worldPart?.Entry == null)
            return int.MinValue / 2;

        string name = (worldPart.Entry.Filename ?? "").ToLowerInvariant();
        int score = 0;

        // 1) 名字命中系统层（vfx / sound / lighting / ui / collision / schematic …）→ 一票否决
        bool systemLayer = IsSystemWorldPartName(name);
        if (systemLayer)
            score -= 1000;

        if (!systemLayer && IsArtWorldPartName(name))
            score += 120;

        // 2) 内容判据（比名字更可信）：只装 Effect 的层、静态模型占比过低的层都不能写道具
        int staticCount = worldPart.ObjectInstanceCount;
        int effectCount = worldPart.EffectInstanceCount;
        int total = Math.Max(worldPart.TotalObjectCount, staticCount + effectCount);

        if (staticCount == 0 && effectCount > 0)
            score -= 900;                                   // 纯特效层，例如 layer5_vfx_global
        else if (total > 0 && staticCount * 4 < total)
            score -= 300;                                   // 静态模型不足 25%

        if (worldPart.BlueprintGuids.Count > 0)
            score += 20;

        // layer 序号越大越可能是「上层装饰物」，这里封顶，避免序号压过内容判据
        if (name.StartsWith("layer"))
        {
            int end = 5;
            while (end < name.Length && char.IsDigit(name[end]))
                end++;
            if (end > 5 && int.TryParse(name.Substring(5, end - 5), out int layerIndex))
                score += Math.Min(layerIndex, 8);
        }

        return score;
    }

    /// <summary>WorldPart 分数高于这个值才允许写入（低于它的都是 vfx/sound/lighting/collision 这类系统层）</summary>
    private const int MinPlaceableWorldPartScore = -100;

    /// <summary>按 token 匹配（文件名里用 _ / . 分隔，避免 "particle" 命中 "art" 这种事）</summary>
    private static bool ContainsToken(string name, string token)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(token))
            return false;

        int index = name.IndexOf(token, StringComparison.Ordinal);
        while (index >= 0)
        {
            int end = index + token.Length;
            bool leftOk = index == 0 || !char.IsLetterOrDigit(name[index - 1]);
            bool rightOk = end >= name.Length || !char.IsLetterOrDigit(name[end]);
            if (leftOk && rightOk)
                return true;

            index = name.IndexOf(token, index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>系统层命名：vfx / sound / lighting / ui / collision / schematic …，绝对不能写道具</summary>
    private static bool IsSystemWorldPartName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        foreach (string token in SystemWorldPartTokens)
        {
            if (ContainsToken(name, token))
                return true;
        }

        return false;
    }

    /// <summary>美术层命名：levelart / props / vegetation / zone / art …（优先级高于普通层）</summary>
    private static bool IsArtWorldPartName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        foreach (string token in ArtWorldPartTokens)
        {
            if (ContainsToken(name, token))
                return true;
        }

        return false;
    }

    private static readonly string[] SystemWorldPartTokens =
    [
        "vfx", "fx", "sfx", "effect", "effects", "particle", "particles",
        "sound", "sounds", "audio", "music",
        "lighting", "light", "lights", "lightmap", "lightmaps", "reflection", "reflections", "probe", "probes",
        "ui", "hud", "squadselect",
        "collision", "collisions", "occluder", "occluders", "occlusion",
        "levolution", "levolutionsystems", "schematic", "schematics", "logic", "setup", "gameplay",
        "volume", "volumes", "volumetric", "postprocess", "sky", "weather", "spawn", "spawns",
    ];

    private static readonly string[] ArtWorldPartTokens =
    [
        "levelart", "art", "props", "prop", "vegetation", "veg", "foliage", "zone", "area",
        "backdrop", "decor", "detail", "structure", "building", "terrain",
    ];

    private static HashSet<string> GetBundleNames(EbxAssetEntry entry)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        if (entry == null)
            return names;

        foreach (int bundleId in entry.EnumerateBundles())
        {
            BundleEntry bundleEntry = App.AssetManager.GetBundleEntry(bundleId);
            if (bundleEntry != null)
                names.Add(bundleEntry.Name);
        }

        return names;
    }

    #endregion

    #region -- EBX 写入 --

    private static bool WriteObjectReference(
        PlacementTransaction txn,
        Vector3 worldPoint,
        out Guid instanceGuid,
        out string error)
    {
        instanceGuid = Guid.Empty;
        error = null;

        ObjectPaletteEntry paletteEntry = txn.Entry;
        WorldPartIndexNode worldPart = txn.WorldPart;
        EbxAssetEntry blueprintEntry = paletteEntry.Entry;
        EbxAssetEntry worldPartEntry = worldPart.Entry;

        // 优先使用编辑器正在用的那份 EbxAsset，这样视口能立刻同步
        EbxAsset worldPartAsset = LoadedAssetManager.Instance.GetEbxAsset(worldPartEntry.Guid)
                                  ?? App.AssetManager.GetEbx(worldPartEntry);
        if (worldPartAsset == null)
        {
            error = "无法读取 WorldPartData";
            return false;
        }

        txn.WorldPartAsset = worldPartAsset;

        try
        {
            dynamic root = worldPartAsset.RootObject;
            object newObject = TypeLibrary.CreateObject("ObjectReferenceObjectData");
            if (newObject == null)
            {
                error = "无法创建 ObjectReferenceObjectData";
                return false;
            }

            txn.NewObject = newObject;

            dynamic rod = newObject;
            rod.Blueprint = new PointerRef(new EbxImportReference()
            {
                FileGuid = blueprintEntry.Guid,
                ClassGuid = GetRootInstanceGuid(blueprintEntry),
            });
            rod.CastSunShadowEnable = true;
            rod.CastReflectionEnable = true;
            rod.CastEnvmapEnable = true;
            SetProperty(rod, "BlueprintTransform", Entity.MakeLinearTransform(Matrix.Translation(worldPoint)));

            AssetClassGuid classGuid = new AssetClassGuid(
                FrostySdk.Utils.GenerateDeterministicGuid(worldPartAsset.Objects, (Type)newObject.GetType(), worldPartAsset.FileGuid), -1);
            ((dynamic)newObject).SetInstanceGuid(classGuid);
            instanceGuid = classGuid.ExportedGuid;

            // DataBusPeer 需要把 guid 的低位塞进 Flags，否则运行时识别不到实例
            try
            {
                SetProperty(rod, "Flags", unchecked((uint)instanceGuid.GetHashCode()));
            }
            catch (Exception)
            {
                // 部分类型没有 Flags，忽略
            }

            worldPartAsset.AddObject(newObject);
            if (root.Objects is IList objects)
                objects.Add(new PointerRef(newObject));
            else
                root.Objects.Add(new PointerRef(newObject));

            worldPartAsset.AddDependency(blueprintEntry.Guid);
            worldPartAsset.Update();
            App.AssetManager.ModifyEbx(worldPartEntry.Name, worldPartAsset);

            worldPart.BlueprintGuids.Add(blueprintEntry.Guid);
            paletteEntry.UsageCount++;

            // 视口实体
            txn.LiveEntity = SpawnLiveEntity(txn.Owner, worldPart, newObject);

            return true;
        }
        catch (Exception ex)
        {
            error = $"写入失败：{ex.Message}";
            App.Logger?.LogError($"[ObjectPalette] 写入 {worldPartEntry.Name} 失败：{ex}");
            return false;
        }
    }

    internal static Guid GetRootInstanceGuid(EbxAssetEntry blueprintEntry)
    {
        EbxAsset asset = App.AssetManager.GetEbx(blueprintEntry);
        if (asset?.RootObject == null)
            return Guid.Empty;

        try
        {
            return ((dynamic)asset.RootObject).GetInstanceGuid().ExportedGuid;
        }
        catch (Exception)
        {
            return blueprintEntry.Guid;
        }
    }

    #endregion

    #region -- 视口实体 & Undo --

    private static Entity SpawnLiveEntity(LevelEditor owner, WorldPartIndexNode worldPart, object newObject)
    {
        try
        {
            List<SceneLayer> layers = [];
            owner.RootLayer?.CollectLayers(layers);

            SceneLayer targetLayer = null;
            WorldPartReferenceObject worldPartRef = null;
            foreach (SceneLayer layer in layers)
            {
                if (layer.Entity is WorldPartReferenceObject candidate &&
                    candidate.Blueprint?.FileGuid == worldPart.FileGuid)
                {
                    targetLayer = layer;
                    worldPartRef = candidate;
                    break;
                }
            }

            if (worldPartRef == null || targetLayer == null || owner.Screen == null)
                return null; // 该 SubWorld 没有加载，写入 EBX 即可

            if (newObject is not GameObjectData gameObjectData)
                return null;

            Entity entity = worldPartRef.MyCreateEntity(gameObjectData);
            if (entity == null)
                return null;

            entity.Layer = targetLayer;
            owner.Screen.AddEntity(entity);
            worldPartRef.AddNewEntity(entity);

            MeshUtil.NotifyLayerReloaded();
            return entity;
        }
        catch (Exception ex)
        {
            App.Logger?.LogWarning($"[ObjectPalette] 创建视口实体失败：{ex.Message}");
            return null;
        }
    }

    #endregion

    #region -- 工具 --

    internal static void SetProperty(object target, string name, object value)
    {
        if (target == null || value == null)
            return;

        PropertyInfo prop = target.GetType().GetProperty(name);
        if (prop == null || !prop.CanWrite)
            return;

        prop.SetValue(target, value);
    }

    #endregion
}
