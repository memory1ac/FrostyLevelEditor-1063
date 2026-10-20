using Frosty.Core;
using Frosty.Core.Controls;
using FrostySdk.Ebx;
using FrostySdk.IO;
using FrostySdk.Managers;
using LevelEditorPlugin.BF1Core.Enums;
using LevelEditorPlugin.Editors;
using LevelEditorPlugin.Entities;
using LevelEditorPlugin.Managers;
using LevelEditorPlugin.Screens;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LevelEditorPlugin.BF1Core.Utils;

public static class MeshUtil
{
    /// <summary>
    /// Layer重新加载事件
    /// </summary>
    public static event Action LayerReloaded;

    /// <summary>
    /// 复制选中的网格组件到当前蓝图中 
    /// </summary>
    public static async Task CopySelectedMesh(CopyMeshOption option)
    {
        if (option.Entity == null)
        {
            App.Logger.LogError("当前Entity为Null");
            return;
        }
        if (option.LevelEditorScreen == null)
        {
            App.Logger.LogError("当前LevelEditorScreen为Null");
            return;
        }
        var camera = LevelEditorScreen.EditorCamera;
        if (option.IsCopyToCamera)
        {
            if (camera == null)
            {
                App.Logger.LogError("未找到当前地图编辑器相机");
                return;
            }
            option.Count = 1;
            option.Step = 0.0f;
        }

        var ebxAssetEnity = App.AssetManager.GetEbxEntry(option.Entity.FileGuid);
        App.Logger.Log($"需要添加模型的Ebx文件为：{ebxAssetEnity.Name}");

        // 用于保存查询到的需要网络注册的组件Ebx资源实体
        var ebxAssetEnityList_NetReg = new List<EbxAssetEntry>();
        // 网络注册
        foreach (EbxAssetEntry subEntry in App.AssetManager.EnumerateEbx())
        {
            if (subEntry.ContainsDependency(ebxAssetEnity.Guid))
            {
                if (subEntry.Type == "NetworkRegistryAsset")
                {
                    ebxAssetEnityList_NetReg.Add(subEntry);
                }
            }
        }

        ///////////////////////////////////

        // 2026/01/22
        // 核心步骤（数据不一致的关键原因）
        // 复制模型这里使用统一的缓存数据，而不是自己直接Get读取，这样保证数据统一性
        EbxAsset ebxAsset = LoadedAssetManager.Instance.GetEbxAsset(option.Entity.FileGuid);
        dynamic rootObject = ebxAsset.RootObject;

        // 保存复制模型组件引用集合
        var pointerRefList_CopyMesh = new List<PointerRef>();
        // 遍历要复制模型的数量
        for (int index = 0; index < option.Count; index++)
        {
            // 如果复制到相机，则只循环一次
            if (option.IsCopyToCamera)
            {
                if (index >= 1)
                    break;
            }

            PointerRef copyMeshPointerRef;
            if (option.Entity is StaticModelGroupElementEntity staticModelGroupElementEntity)
            {
                // 添加蓝色模型

                // 创建新的 Entity.ObjectReferenceObjectData 组件
                copyMeshPointerRef = CoreUtil.CreateClassObjectRef(ebxAsset, "ObjectReferenceObjectData");
                dynamic objectReference = copyMeshPointerRef.Internal;

                // 获取当前组件的Guid
                Guid guid = CoreUtil.GetInternalClassGuid(copyMeshPointerRef);
                App.Logger.Log($"已创建组件 ObjectReferenceObjectData {guid}");

                ///////////////////////////////////

                // FrostySdk.Ebx.LinearTransform
                LinearTransform dataLinearTrans = staticModelGroupElementEntity.Data.Transform;

                // 这里不能直接修改trans里的值，需要拷贝一份，否则会修改到原始entity里的值
                LinearTransform newTransform = CoreUtil.CopyNewLinearTransform(dataLinearTrans);
                if (option.IsCopyToCamera)
                {
                    // 注意：这里使用的是trans而不是Translate
                    newTransform.trans.x = camera.WorldMatrix.TranslationVector.X;
                    newTransform.trans.y = camera.WorldMatrix.TranslationVector.Y;
                    newTransform.trans.z = camera.WorldMatrix.TranslationVector.Z;
                }
                else
                {
                    // 应用Transform复制方向
                    CoreUtil.ApplyTransformCopyDirection(option.CopyDirection, newTransform, index, option.Step);
                }

                {
                    // 这个BlueprintTransform不能直接赋值4x4矩阵
                    objectReference.BlueprintTransform = newTransform;
                    objectReference.Blueprint = staticModelGroupElementEntity.Data.Blueprint;
                    objectReference.ObjectVariation = staticModelGroupElementEntity.Data.ObjectVariation;

                    CoreUtil.SetFrostyEnum(objectReference, "StreamRealm", "StreamRealm_None");
                    CoreUtil.SetFrostyEnum(objectReference, "RadiosityTypeOverride", "RadiosityTypeOverride_None");
                    objectReference.LightmapResolutionScale = 1u;
                    objectReference.ForceSeparateEnlightenSystem = false;
                    objectReference.CastSunShadowEnable = true;
                    objectReference.CastReflectionEnable = true;
                    objectReference.CastEnvmapEnable = true;
                    objectReference.Excluded = false;
                    CoreUtil.SetFrostyEnum(objectReference, "LocalPlayerId", "LocalPlayerId_Invalid");
                }
            }
            else
            {
                // 添加红色模型

                // 这个其实一个PointerRef
                // FrostySdk.Ebx.PointerRef
                dynamic targetItemRef = null;
                // 这个Item也是一个PointerRef
                foreach (dynamic itemRef in rootObject.Objects)
                {
                    Guid guid = CoreUtil.GetInternalClassGuid(itemRef);
                    if (guid == option.Entity.InstanceGuid)
                    {
                        // 找到第一个就跳出循环
                        targetItemRef = itemRef;
                        break;
                    }
                }
                if (targetItemRef == null)
                    throw new Exception("没有在蓝图中找到指定组件");

                //////////////////////////////////////////////////////////

                // 目标组件实例
                dynamic targetComp = targetItemRef.Internal;
                // Entity.SpatialPrefabReferenceObjectData
                string typeName = targetComp.GetType().Name;

                // 这里复制的是一个PointerRef
                FrostyClipboard.Current.SetData(targetItemRef);
                copyMeshPointerRef = (PointerRef)FrostyClipboard.Current.GetData(ebxAsset, ebxAssetEnity);
                if (copyMeshPointerRef == null)
                    throw new Exception($"目标组件 {typeName} 复制失败");

                // 复制组件实例
                dynamic objectReference = copyMeshPointerRef.Internal;
                {
                    if (CoreUtil.IsObjectHasProperty(objectReference, "BlueprintTransform"))
                    {
                        if (option.IsCopyToCamera)
                        {
                            // 注意：这里使用的是trans而不是Translate
                            objectReference.BlueprintTransform.trans.x = camera.WorldMatrix.TranslationVector.X;
                            objectReference.BlueprintTransform.trans.y = camera.WorldMatrix.TranslationVector.Y;
                            objectReference.BlueprintTransform.trans.z = camera.WorldMatrix.TranslationVector.Z;
                        }
                        else
                        {
                            // 应用Transform复制方向
                            CoreUtil.ApplyTransformCopyDirection(
                                option.CopyDirection, objectReference.BlueprintTransform, index, option.Step);
                        }
                    }
                    else
                    {
                        if (option.IsCopyToCamera)
                        {
                            // 注意：这里使用的是trans而不是Translate
                            objectReference.Transform.trans.x = camera.WorldMatrix.TranslationVector.X;
                            objectReference.Transform.trans.y = camera.WorldMatrix.TranslationVector.Y;
                            objectReference.Transform.trans.z = camera.WorldMatrix.TranslationVector.Z;
                        }
                        else
                        {
                            // 应用Transform复制方向
                            CoreUtil.ApplyTransformCopyDirection(
                                option.CopyDirection, objectReference.Transform, index, option.Step);
                        }
                    }
                }
            }

            ///////////////////////////////////

            rootObject.Objects.Add(copyMeshPointerRef);
            App.Logger.Log($"已添加组件，当前Objects数量：{rootObject.Objects.Count}");

            // 记录这个新的组件
            pointerRefList_CopyMesh.Add(copyMeshPointerRef);

            await Task.Delay(1);
        }

        // 更新并保存修改
        ebxAsset.Update();
        App.AssetManager.ModifyEbx(ebxAssetEnity.Name, ebxAsset);

        ///////////////////////////////////

        // 网络注册
        foreach (EbxAssetEntry netRegEntry in ebxAssetEnityList_NetReg)
        {
            var ebxAsset_NetReg = App.AssetManager.GetEbx(netRegEntry);
            dynamic rootObj_NetReg = ebxAsset_NetReg.RootObject;

            // 遍历我们创建的全部组件引用
            foreach (PointerRef itemRef in pointerRefList_CopyMesh)
            {
                // 如果Guid是00000这种，则不注册网络
                if (!CoreUtil.IsExportedPointerRef(itemRef))
                    continue;

                rootObj_NetReg.Objects.Add(new PointerRef(new EbxImportReference()
                {
                    FileGuid = option.Entity.FileGuid,
                    ClassGuid = CoreUtil.GetInternalClassGuid(itemRef)
                }));
            }

            ebxAsset_NetReg.Update();
            App.AssetManager.ModifyEbx(netRegEntry.Name, ebxAsset_NetReg);
        }

        App.Logger.Log("----------");

        // 添加渲染
        {
            var layerRoot = option.Entity.FindAncestor<ReferenceObject>();
            var index = 0;
            // 遍历我们创建的全部组件引用
            foreach (PointerRef itemRef in pointerRefList_CopyMesh)
            {
                App.Logger.Log($"正在添加第 {++index}/{option.Count} 个模型");

                // 为我们创建的新组件创建对应的Entity
                Entities.Entity copyEntity = layerRoot.MyCreateEntity(itemRef.GetObjectAs<GameObjectData>());
                copyEntity.Layer = option.Entity.Layer;

                await Task.Delay(1);
                option.LevelEditorScreen.AddEntity(copyEntity);
                await Task.Delay(1);
                layerRoot.AddNewEntity(copyEntity);
                await Task.Delay(1);
            }

            App.Logger.Log("==========");

            // 通知更新UI
            LayerReloaded?.Invoke();
        }
    }

    /// <summary>
    /// 删除选中模型
    /// </summary>
    public static async Task DeleteSelectedMesh(DeleteMeshOption option)
    {
        if (option.Entity == null)
        {
            App.Logger.LogError("当前Entity为Null");
            return;
        }
        if (option.LevelEditorScreen == null)
        {
            App.Logger.LogError("当前LevelEditorScreen为Null");
            return;
        }

        if (CoreUtil.IsBlueMesh(option.Entity))
        {
            App.Logger.LogWarning("检测到当前为蓝色模型，操作取消");
            return;
        }

        // 删除渲染
        {
            var layerRoot = option.Entity.FindAncestor<ReferenceObject>();

            await Task.Delay(1);
            option.LevelEditorScreen.DeleteEntityFromRenderProxy(option.Entity);
            await Task.Delay(1);
            layerRoot.DeleteEntity(option.Entity);
            await Task.Delay(1);

            // 通知更新UI
            LayerReloaded?.Invoke();
        }

        ///////////////////////////////////

        App.Logger.Log("==========");

        var ebxAssetEnity = App.AssetManager.GetEbxEntry(option.Entity.FileGuid);
        App.Logger.Log($"需要删除模型的Ebx文件为：{ebxAssetEnity.Name}");

        // 用于保存查询到的需要移除网络注册的组件Ebx资源实体
        var ebxAssetEnityList_NetReg = new List<EbxAssetEntry>();
        // 网络注册
        foreach (EbxAssetEntry subEntry in App.AssetManager.EnumerateEbx())
        {
            if (subEntry.ContainsDependency(ebxAssetEnity.Guid))
            {
                if (subEntry.Type == "NetworkRegistryAsset")
                {
                    ebxAssetEnityList_NetReg.Add(subEntry);
                }
            }
        }

        ///////////////////////////////////

        // 2026/01/22
        // 核心步骤（数据不一致的关键原因）
        // 复制模型这里使用统一的缓存数据，而不是自己直接Get读取，这样保证数据统一性
        EbxAsset ebxAsset = LoadedAssetManager.Instance.GetEbxAsset(option.Entity.FileGuid);
        dynamic rootObject = ebxAsset.RootObject;

        // 这个其实一个PointerRef
        // FrostySdk.Ebx.PointerRef
        dynamic targetItemRef = null;
        // 这个Item也是一个PointerRef
        foreach (dynamic itemRef in rootObject.Objects)
        {
            Guid guid = CoreUtil.GetInternalClassGuid(itemRef);
            if (guid == option.Entity.InstanceGuid)
            {
                // 找到第一个就跳出循环
                targetItemRef = itemRef;
                break;
            }
        }
        if (targetItemRef == null)
            throw new Exception("没有在蓝图中找到指定组件");

        // 待删除组件的ClassGuid
        Guid targetItemClassGuid = CoreUtil.GetInternalClassGuid(targetItemRef);

        // 移除这个组件
        rootObject.Objects.Remove(targetItemRef);
        App.Logger.Log($"已删除组件 {targetItemClassGuid}，当前Objects数量：{rootObject.Objects.Count}");

        // 更新并保存修改
        ebxAsset.Update();
        App.AssetManager.ModifyEbx(ebxAssetEnity.Name, ebxAsset);

        ///////////////////////////////////

        // 移除网络注册
        foreach (EbxAssetEntry netRegEntry in ebxAssetEnityList_NetReg)
        {
            var ebxAsset_NetReg = App.AssetManager.GetEbx(netRegEntry);
            dynamic rootObj_NetReg = ebxAsset_NetReg.RootObject;

            // 采用倒序移除
            for (int i = rootObj_NetReg.Objects.Count - 1; i >= 0; i--)
            {
                // FrostySdk.Ebx.PointerRef
                PointerRef itemRef = rootObj_NetReg.Objects[i];
                if (itemRef.External.FileGuid == option.Entity.FileGuid &&
                    itemRef.External.ClassGuid == targetItemClassGuid)
                {
                    rootObj_NetReg.Objects.RemoveAt(i);
                }
            }

            ebxAsset_NetReg.Update();
            App.AssetManager.ModifyEbx(netRegEntry.Name, ebxAsset_NetReg);
        }
    }
}

public class CopyMeshOption
{
    public Entities.Entity Entity { get; set; }
    public LevelEditorScreen LevelEditorScreen { get; set; }

    public bool IsCopyToCamera { get; set; }

    public int Count { get; set; }
    public float Step { get; set; }
    public CopyDirection CopyDirection { get; set; }
}

public class DeleteMeshOption
{
    public Entities.Entity Entity { get; set; }
    public LevelEditorScreen LevelEditorScreen { get; set; }
}
