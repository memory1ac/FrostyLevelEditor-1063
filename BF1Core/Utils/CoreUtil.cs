using Frosty.Core;
using FrostySdk;
using FrostySdk.Ebx;
using FrostySdk.IO;
using LevelEditorPlugin.BF1Core.Enums;
using LevelEditorPlugin.Entities;
using System;
using System.Reflection;

namespace LevelEditorPlugin.BF1Core.Utils;

public static class CoreUtil
{
    /// <summary>
    /// 播放按钮点击音效
    /// </summary>
    public static void PlayAudio()
    {
        Console.Beep(500, 75);
    }

    /// <summary>
    /// 定位编辑器路径
    /// </summary>
    public static void OpenEditorPath(string nameUrl)
    {
        var assetEntry = App.AssetManager.GetEbxEntry(nameUrl);
        if (assetEntry == null)
            return;
        App.EditorWindow.DataExplorer.SelectAsset(assetEntry);
    }

    /// <summary>
    /// 通过字符串设置枚举类型
    /// </summary>
    public static void SetFrostyEnum(dynamic obj, string propName, string enumName)
    {
        if (string.IsNullOrWhiteSpace(propName))
            return;
        if (string.IsNullOrWhiteSpace(enumName))
            return;

        Type objType = obj.GetType();
        var property = objType.GetProperty(propName);

        dynamic value = Enum.Parse(property.PropertyType, enumName);
        property.SetValue(obj, value);
    }

    /// <summary>
    /// 判断是否为指定类型
    /// </summary>
    public static bool IsType(dynamic obj, string typeName)
    {
        return obj.GetType().Name.Equals(typeName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断PointerRef是内部还是外部
    /// </summary>
    public static bool IsExportedPointerRef(dynamic pointerRef)
    {
        AssetClassGuid instGuid;

        // FrostySdk.Ebx.PointerRef
        if (IsType(pointerRef, "PointerRef"))
            instGuid = pointerRef.Internal.GetInstanceGuid();
        else
            instGuid = pointerRef.GetInstanceGuid();

        return instGuid.IsExported;
    }

    /// <summary>
    /// 获取 PointerRef 的 ClassGuid（仅 Internal）
    /// </summary>
    public static Guid GetInternalClassGuid(dynamic objInternal)
    {
        AssetClassGuid instGuid;

        // FrostySdk.Ebx.PointerRef
        if (IsType(objInternal, "PointerRef"))
            instGuid = objInternal.Internal.GetInstanceGuid();
        else
            instGuid = objInternal.GetInstanceGuid();

        if (instGuid.IsExported == true)
            return instGuid.ExportedGuid;

        // 数字转Guid，前面自动补0
        // 如果Guid是类似这种的 00000000-0000-0000-0000-0000000000dd
        // 他会变成一个数字221，使用下面方法转换为正确的Guid
        return Guid.Parse(instGuid.InternalId.ToString("X").PadLeft(32, '0'));
    }

    /// <summary>
    /// 创建蓝图组件
    /// </summary>
    public static PointerRef CreateClassObjectRef(EbxAsset ebxAsset, string typeName)
    {
        object newObject = TypeLibrary.CreateObject(typeName);

        // 这里有两种创建方式
        // 一种是直接修改原有的组件类型，这样Guid不是不会变化的
        // 一种是添加一个新的空组件槽，然后创建一个新的组件

        // 我们这里直接是创建一个新的组件，不修改原有组件类型
        // 将内部id设置为-1，以便在添加到资产时设置
        var classGuid = new AssetClassGuid(
            FrostySdk.Utils.GenerateDeterministicGuid(ebxAsset.Objects, typeName, ebxAsset.FileGuid), -1);

        var newPointerRef = new PointerRef(newObject);
        ((dynamic)newPointerRef.Internal).SetInstanceGuid(classGuid);

        if (TypeLibrary.IsSubClassOf(newPointerRef.Internal, "DataBusPeer"))
        {
            var bytes = classGuid.ExportedGuid.ToByteArray();
            var flagValue = (uint)((bytes[2] << 16) | (bytes[1] << 8) | bytes[0]);
            newPointerRef.Internal.GetType().GetProperty("Flags",
                BindingFlags.Public | BindingFlags.Instance).SetValue(newPointerRef.Internal, flagValue);
        }

        // 将其添加到对象列表中，以便为其分配位置
        // 这个必须要添加，否则下一个guid不会变化
        ebxAsset.AddObject(newPointerRef.Internal);

        return newPointerRef;
    }

    /// <summary>
    /// 判断对象缩放有某个属性
    /// </summary>
    public static bool IsObjectHasProperty(dynamic objectReference, string propName)
    {
        PropertyInfo propertyInfo = objectReference.GetType().GetProperty(propName);
        if (propertyInfo == null)
            return false;

        return true;
    }
    /// <summary>
    /// 判断是否为蓝色的模型（StaticModelGroup 子元素，现已允许修改 transform）
    /// 保留此方法供其他逻辑使用，但不再用于阻止 transform 修改
    /// </summary>
    public static bool IsBlueMesh(Entities.Entity entity)
    {
        return entity is StaticModelGroupElementEntity;
    }

    /// <summary>
    /// 复制一份全选的LinearTransform
    /// </summary>
    public static LinearTransform CopyNewLinearTransform(LinearTransform dataLinearTrans)
    {
        var transform = new LinearTransform();
        {
            transform.right.x = dataLinearTrans.right.x;
            transform.right.y = dataLinearTrans.right.y;
            transform.right.z = dataLinearTrans.right.z;

            transform.up.x = dataLinearTrans.up.x;
            transform.up.y = dataLinearTrans.up.y;
            transform.up.z = dataLinearTrans.up.z;

            transform.forward.x = dataLinearTrans.forward.x;
            transform.forward.y = dataLinearTrans.forward.y;
            transform.forward.z = dataLinearTrans.forward.z;

            transform.trans.x = dataLinearTrans.trans.x;
            transform.trans.y = dataLinearTrans.trans.y;
            transform.trans.z = dataLinearTrans.trans.z;

            transform.Translate.x = dataLinearTrans.Translate.x;
            transform.Translate.y = dataLinearTrans.Translate.y;
            transform.Translate.z = dataLinearTrans.Translate.z;

            transform.Rotation.x = dataLinearTrans.Rotation.x;
            transform.Rotation.y = dataLinearTrans.Rotation.y;
            transform.Rotation.z = dataLinearTrans.Rotation.z;

            transform.Scale.x = dataLinearTrans.Scale.x;
            transform.Scale.y = dataLinearTrans.Scale.y;
            transform.Scale.z = dataLinearTrans.Scale.z;
        }
        return transform;
    }

    /// <summary>
    /// 应用Transform复制方向
    /// </summary>
    public static void ApplyTransformCopyDirection(CopyDirection copyDirection, LinearTransform newTransform, int index, float step)
    {
        switch (copyDirection)
        {
            case CopyDirection.XAxisAdd:
                newTransform.trans.x += (index + 1) * step;
                break;
            case CopyDirection.YAxisAdd:
                newTransform.trans.y += (index + 1) * step;
                break;
            case CopyDirection.ZAxisAdd:
                newTransform.trans.z += (index + 1) * step;
                break;
            case CopyDirection.XAxisSub:
                newTransform.trans.x -= (index + 1) * step;
                break;
            case CopyDirection.YAxisSub:
                newTransform.trans.y -= (index + 1) * step;
                break;
            case CopyDirection.ZAxisSub:
                newTransform.trans.z -= (index + 1) * step;
                break;
        }
    }

    /// <summary>
    /// 将单个角度归一化到[-180, 180]范围
    /// </summary>
    public static float NormalizeAngle(float angle)
    {
        // 使用模运算将角度限制在[0, 360]范围
        angle %= 360f;

        // 如果角度大于180度，转换为负角度
        if (angle > 180f)
            angle -= 360f;
        // 如果角度小于-180度，转换为正角度
        else if (angle < -180f)
            angle += 360f;

        return angle;
    }
}
