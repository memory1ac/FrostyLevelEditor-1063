using Frosty.Core;
using FrostySdk;
using FrostySdk.Ebx;
using FrostySdk.IO;
using FrostySdk.Managers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace LevelEditorPlugin.Render.Havok;

/// <summary>
/// 从一个关卡实体反查它对应的 Havok 碰撞 res。
///
/// 引用链（ObjectBlueprint 里就能找到，无需加载整个蓝图依赖树）：
///   EntityData.Components[*].PhysicsBodies[*].Asset
///   GamePhysicsEntityData.PhysicsData.Asset
///   → Asset 是外部 EBX（HavokAsset）
///   → HavokAsset.Resource（ResourceRef）= HavokPhysicsData res 的 ResRid
///
/// 这里只在「目标 EBX 的类型确实是 HavokAsset」时才真正去加载它，
/// 其余外部引用（MeshAsset / Shader / Texture ...）一律不看，避免为了找物理把整个依赖树读一遍。
/// 名字里带 cluster 的是破坏碎块（碎片）的碰撞，按需求跳过。
/// </summary>
internal static class HavokSourceResolver
{
    private const int MaxDepth = 4;
    private const int MaxAssetsPerEntity = 4;

    /// <summary>实体数据 → 碰撞 res 列表。同一个蓝图的所有实例共享同一份 EntityData，所以按对象引用缓存。</summary>
    private static readonly Dictionary<object, List<ulong>> dataCache = new(ReferenceComparer.Instance);
    private static readonly Dictionary<(Guid, Guid), ulong> externalCache = [];

    /// <summary>属性表缓存，避免每帧对每个实体做一次 GetProperties。</summary>
    private static readonly Dictionary<Type, PropertyInfo[]> propertyCache = [];

    public static void ClearCache()
    {
        dataCache.Clear();
        externalCache.Clear();
    }

    /// <summary>取该实体引用到的 Havok 碰撞 res（通常 1 个，少数复合物体会引用多个）。</summary>
    public static List<ulong> Resolve(LevelEditorPlugin.Entities.Entity entity)
    {
        GameObjectData data = entity?.Data;
        if (data == null)
            return [];

        if (dataCache.TryGetValue(data, out List<ulong> cached))
            return cached;

        List<ulong> rids = [];
        Walk(data, rids, 0, new HashSet<object>(ReferenceComparer.Instance));
        dataCache[data] = rids;
        return rids;
    }

    private static void Walk(GameObjectData data, List<ulong> rids, int depth, HashSet<object> visited)
    {
        if (data == null || depth > MaxDepth || rids.Count >= MaxAssetsPerEntity)
            return;

        if (!visited.Add(data))
            return;

        foreach (PropertyInfo pi in GetPointerProperties(data.GetType()))
        {
            object value;
            try
            {
                value = pi.GetValue(data);
            }
            catch
            {
                continue;
            }

            if (value is PointerRef pointer)
            {
                VisitPointer(pointer, rids, depth, visited);
            }
            else if (value is IList list)
            {
                int count = Math.Min(list.Count, 512);
                for (int i = 0; i < count; i++)
                {
                    if (list[i] is PointerRef element)
                        VisitPointer(element, rids, depth, visited);
                }
            }
        }
    }

    private static void VisitPointer(PointerRef pointer, List<ulong> rids, int depth, HashSet<object> visited)
    {
        if (pointer.Type == PointerRefType.Null)
            return;

        if (pointer.Type == PointerRefType.Internal)
        {
            if (TryGetHavokResource(pointer.Internal, out ulong internalRid))
            {
                AddRid(rids, internalRid);
                return;
            }

            if (pointer.Internal is GameObjectData child)
                Walk(child, rids, depth + 1, visited);

            return;
        }

        // External：目标可能只是网格/贴图，所以先看资产类型再决定要不要读
        AddRid(rids, ResolveExternalAsset(pointer.External));
    }

    private static ulong ResolveExternalAsset(EbxImportReference import)
    {
        (Guid, Guid) key = (import.FileGuid, import.ClassGuid);
        if (externalCache.TryGetValue(key, out ulong cached))
            return cached;

        ulong rid = 0;
        try
        {
            EbxAssetEntry entry = App.AssetManager.GetEbxEntry(import.FileGuid);
            if (entry != null && IsHavokAssetType(entry.Type))
            {
                EbxAsset asset = App.AssetManager.GetEbx(entry);
                if (asset != null && asset.IsValid)
                {
                    // HavokAsset 一般只有一个根对象，先看根对象，再退化成全表扫描
                    rid = FindHavokResource(asset.RootObject);
                    if (rid == 0)
                    {
                        foreach (object obj in asset.Objects)
                        {
                            rid = FindHavokResource(obj);
                            if (rid != 0)
                                break;
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            App.Logger?.LogWarning($"Havok 叠加：读取外部 HavokAsset 失败 {import.FileGuid}：{e.Message}");
        }

        externalCache[key] = rid;
        return rid;
    }

    private static ulong FindHavokResource(object obj)
    {
        TryGetHavokResource(obj, out ulong rid);
        return rid;
    }

    private static bool IsHavokAssetType(string type)
    {
        if (string.IsNullOrEmpty(type))
            return false;

        // HavokAsset / GroupHavokAsset（碎块 / 复合）在类型库里不一定是同一个继承链，名字匹配更稳
        return type.IndexOf("HavokAsset", StringComparison.OrdinalIgnoreCase) >= 0
            || TypeLibrary.IsSubClassOf(type, "HavokAsset");
    }

    /// <summary>HavokAsset / GroupHavokAsset 都有 Resource（ResourceRef）；用反射取，避免依赖具体子类。</summary>
    private static bool TryGetHavokResource(object obj, out ulong rid)
    {
        rid = 0;
        if (obj == null)
            return false;

        Type type = obj.GetType();
        if (type.Name.IndexOf("HavokAsset", StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        PropertyInfo pi = type.GetProperty("Resource", BindingFlags.Public | BindingFlags.Instance);
        if (pi == null)
            return false;

        try
        {
            object value = pi.GetValue(obj);
            if (value is ResourceRef resourceRef)
            {
                rid = resourceRef;
                return rid != 0;
            }
        }
        catch
        {
        }

        return false;
    }

    private static void AddRid(List<ulong> rids, ulong rid)
    {
        if (rid == 0 || rids.Count >= MaxAssetsPerEntity)
            return;

        if (!rids.Contains(rid))
            rids.Add(rid);
    }

    private static PropertyInfo[] GetPointerProperties(Type type)
    {
        if (propertyCache.TryGetValue(type, out PropertyInfo[] cached))
            return cached;

        List<PropertyInfo> result = [];
        foreach (PropertyInfo pi in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (pi.GetIndexParameters().Length != 0)
                continue;

            Type pt = pi.PropertyType;
            if (pt == typeof(PointerRef) || typeof(IList).IsAssignableFrom(pt))
                result.Add(pi);
        }

        PropertyInfo[] array = [.. result];
        propertyCache[type] = array;
        return array;
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new();

        public new bool Equals(object x, object y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
