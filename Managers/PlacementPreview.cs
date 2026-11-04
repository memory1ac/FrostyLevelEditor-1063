using Frosty.Core;
using FrostySdk;
using FrostySdk.Ebx;
using FrostySdk.IO;
using LevelEditorPlugin.Editors;
using LevelEditorPlugin.Entities;
using LevelEditorPlugin.Screens;
using SharpDX;
using System;
using Entity = LevelEditorPlugin.Entities.Entity;

namespace LevelEditorPlugin.Managers;

/// <summary>
/// 放置模式下的「幽灵」预览实体：鼠标在视口里移动时，让选中的 ObjectBlueprint 模型跟着光标走。
///
/// 只创建视口实体和渲染代理：
///  - 不写 EBX、不进 Undo、不注册 NetworkRegistry；
///  - 不进 EntityWorld、也不挂到关卡 layer 树里（所以不会出现在 Layers / Instances 面板）；
///  - 取消放置时直接把渲染代理摘掉即可。
/// </summary>
public sealed class PlacementPreview : IDisposable
{
    private readonly LevelEditorScreen screen;
    private ReferenceObject previewObject;

    public bool IsValid => previewObject != null;

    /// <summary>预览的根实体（用于射线拾取时把自己排除掉）</summary>
    public ReferenceObject RootEntity => previewObject;

    private PlacementPreview(LevelEditorScreen inScreen, ReferenceObject inPreviewObject)
    {
        screen = inScreen;
        previewObject = inPreviewObject;
    }

    /// <summary>
    /// 为素材库里的一个 ObjectBlueprint 建预览实体。
    /// 失败（资源读不出来 / 类型不支持）时返回 null，调用方按「没有预览」继续走普通放置流程。
    /// </summary>
    public static PlacementPreview Create(LevelEditor owner, ObjectPaletteEntry paletteEntry)
    {
        if (owner?.Screen == null || paletteEntry?.Entry == null)
            return null;

        ReferenceObject previewObject = null;
        try
        {
            object newObject = TypeLibrary.CreateObject("ObjectReferenceObjectData");
            if (newObject == null)
                return null;

            dynamic rod = newObject;
            rod.Blueprint = new PointerRef(new EbxImportReference()
            {
                FileGuid = paletteEntry.FileGuid,
                ClassGuid = LevelPlacementService.GetRootInstanceGuid(paletteEntry.Entry),
            });
            rod.BlueprintTransform = Entity.MakeLinearTransform(Matrix.Identity);
            rod.SetInstanceGuid(new AssetClassGuid(Guid.NewGuid(), -1));

            // parent / world 都传 null：只建实体，不挂进场景树
            previewObject = new ReferenceObject((ReferenceObjectData)newObject, null, null);
            if (previewObject.Blueprint == null || previewObject.GetAllEntity().Count == 0)
            {
                // 没有可渲染的实体（蓝图读不出来 / 根对象类型不支持），当作没有预览
                LoadedAssetManager.Instance.UnloadAsset(previewObject.Blueprint);
                previewObject = null;
                return null;
            }

            owner.Screen.AddEntity(previewObject);
            return new PlacementPreview(owner.Screen, previewObject);
        }
        catch (Exception ex)
        {
            App.Logger?.LogWarning($"[ObjectPalette] 创建放置预览失败：{ex.Message}");
            if (previewObject != null)
                LoadedAssetManager.Instance.UnloadAsset(previewObject.Blueprint);
            return null;
        }
    }

    /// <summary>把预览挪到世界坐标上（鼠标每次移动调用）。</summary>
    public void MoveTo(Vector3 worldPoint)
    {
        if (previewObject == null)
            return;

        // 直接改 BlueprintTransform 并标记 RequiresTransformUpdate：
        // 走 SetTransform 会写 Undo 并触发 NotifyEntityModified，预览不该产生任何修改记录。
        previewObject.Data.BlueprintTransform = Entity.MakeLinearTransform(Matrix.Translation(worldPoint));
        previewObject.RequiresTransformUpdate = true;
    }

    public void Dispose()
    {
        ReferenceObject preview = previewObject;
        previewObject = null;

        if (preview == null)
            return;

        try
        {
            if (screen != null)
            {
                // Proxy 的 OwnerEntity 是 ReferenceObject 下面的子实体（网格实体），
                // screen.DeleteEntityFromRenderProxy 是按 OwnerEntity.Owner 匹配的，所以按子实体删。
                foreach (Entity child in preview.GetAllEntity())
                    screen.DeleteEntityFromRenderProxy(child);
            }

            LoadedAssetManager.Instance.UnloadAsset(preview.Blueprint);
        }
        catch (Exception ex)
        {
            App.Logger?.LogWarning($"[ObjectPalette] 销毁放置预览失败：{ex.Message}");
        }
    }
}
