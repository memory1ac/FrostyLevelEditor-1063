using Frosty.Core;
using Frosty.Core.Managers;
using Frosty.Core.Viewport;
using FrostySdk.IO;
using LevelEditorPlugin.BF1Core.Enums;
using LevelEditorPlugin.BF1Core.Native;
using LevelEditorPlugin.BF1Core.Utils;
using LevelEditorPlugin.Entities;
using LevelEditorPlugin.Managers;
using LevelEditorPlugin.Options;
using LevelEditorPlugin.Resources;
using LevelEditorPlugin.Screens;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LevelEditorPlugin.BF1Core.Views;

/// <summary>
/// MeshView.xaml 的交互逻辑
/// </summary>
public partial class MeshView : UserControl
{
    /// <summary>
    /// 3D窗口中选中的实体
    /// </summary>
    public Entity Entity { get; private set; }
    /// <summary>
    /// 地图编辑器屏幕
    /// </summary>
    public LevelEditorScreen LevelEditorScreen { get; private set; }

    /// <summary>
    /// 复制模型方向
    /// </summary>
    private CopyDirection copyDirection = CopyDirection.ZAxisAdd;

    private static readonly string[] ViewModeItems =
    [
        "纯色",
        "Default Lit",
        "Wireframe",
        "BaseColor",
        "SpecularColor",
        "Normals",
        "MaterialAO",
        "Smoothness",
        "Metallic",
        "Reflectance"
    ];

    public MeshView()
    {
        InitializeComponent();

        HotKey.KeyDownEvent += HotKey_KeyDownEvent;
        HavokPhysicsData.PendingCommitChanged += OnPendingCommitChanged;

        ComboBox_ViewMode.ItemsSource = ViewModeItems;
        ComboBox_ViewMode.SelectedIndex = Math.Min(LevelEditorRenderingOptions.ViewModeIndex, ViewModeItems.Length - 1);

        // 初始化地形笔刷 UI
        if (CheckBox_TerrainEditMode != null)
            CheckBox_TerrainEditMode.IsChecked = TerrainEditState.IsTerrainEditModeEnabled;
        SyncBrushControlsFromState();
    }

    private void OnPendingCommitChanged()
    {
        Dispatcher.InvokeAsync(UpdateWriteHavokButtonState);
    }

    private void ComboBox_ViewMode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ComboBox_ViewMode.SelectedIndex >= 0)
        {
            LevelEditorRenderingOptions.ViewModeIndex = ComboBox_ViewMode.SelectedIndex;
        }
    }

    /// <summary>
    /// 接收快捷键按下事件
    /// </summary>
    private void HotKey_KeyDownEvent(Keys key)
    {
        if (UndoManager.Instance == null) return;

        this.Dispatcher.Invoke(() =>
        {
            switch (key)
            {
                case Keys.F1:
                    Button_SnapToCam_Click(null, null);
                    break;
                case Keys.F5:
                    Button_CopyMeshToCamera_Click(null, null);
                    break;
            }
        });
    }

    /// <summary>
    /// 更新选中实体信息
    /// </summary>
    public void UpdateSelectedEntity(Entity entity, LevelEditorScreen screen)
    {
        Entity = entity;
        LevelEditorScreen = screen;

        UpdateUI();
    }

    /// <summary>
    /// 更新UI
    /// </summary>
    public void UpdateUI()
    {
        if (Entity == null)
        {
            TextBlock_EntityName.Text = "Null";
            TextBlock_LayerName.Text = "Null";

            TextBox_TransX.Text = "0.00000";
            TextBox_TransY.Text = "0.00000";
            TextBox_TransZ.Text = "0.00000";

            TextBox_RotateX.Text = "0.00000";
            TextBox_RotateY.Text = "0.00000";
            TextBox_RotateZ.Text = "0.00000";

            TextBox_ScaleX.Text = "1.00000";
            TextBox_ScaleY.Text = "1.00000";
            TextBox_ScaleZ.Text = "1.00000";

            UpdateWriteHavokButtonState();
        }
        else
        {
            TextBlock_EntityName.Text = Entity.DisplayName;
            TextBlock_LayerName.Text = Entity.Layer?.LayerName;

            if (Entity is ISpatialEntity spatialEntity)
            {
                var matrix = spatialEntity.GetLocalTransform();
                _ = matrix.Decompose(out Vector3 scale, out _, out Vector3 translation);
                Vector3 euler = SharpDXUtils.ExtractEulerAngles(matrix);

                TextBox_TransX.Text = translation.X.ToString("0.00000");
                TextBox_TransY.Text = translation.Y.ToString("0.00000");
                TextBox_TransZ.Text = translation.Z.ToString("0.00000");

                TextBox_RotateX.Text = euler.X.ToString("0.00000");
                TextBox_RotateY.Text = euler.Y.ToString("0.00000");
                TextBox_RotateZ.Text = euler.Z.ToString("0.00000");

                TextBox_ScaleX.Text = scale.X.ToString("0.00000");
                TextBox_ScaleY.Text = scale.Y.ToString("0.00000");
                TextBox_ScaleZ.Text = scale.Z.ToString("0.00000");
            }

            UpdateWriteHavokButtonState();
        }

        UpdateTerrainVertexUI();
    }

    /// <summary>
    /// 更新「写回 Havok Transform 到 Res」按钮的启用状态。
    /// </summary>
    private void UpdateWriteHavokButtonState()
    {
        if (Button_WriteHavokTransformsToRes != null)
        {
            Button_WriteHavokTransformsToRes.IsEnabled = HavokPhysicsData.HasAnyPendingCommit;
        }
    }

    private void UpdateTerrainVertexUI()
    {
        SyncBrushControlsFromState();
    }

    private void SyncBrushControlsFromState()
    {
        if (Slider_BrushRadius != null)
            Slider_BrushRadius.Value = TerrainEditState.BrushRadius;
        if (Slider_BrushStrength != null)
            Slider_BrushStrength.Value = TerrainEditState.BrushStrength;
        if (Slider_BrushFeather != null)
            Slider_BrushFeather.Value = TerrainEditState.BrushFeather;
        if (Radio_BrushRaise != null)
            Radio_BrushRaise.IsChecked = TerrainEditState.BrushMode == TerrainBrushMode.Raise;
        if (Radio_BrushLower != null)
            Radio_BrushLower.IsChecked = TerrainEditState.BrushMode == TerrainBrushMode.Lower;
        UpdateBrushLabels();
    }

    private void UpdateBrushLabels()
    {
        if (TextBlock_BrushRadius != null)
            TextBlock_BrushRadius.Text = TerrainEditState.BrushRadius.ToString("0");
        if (TextBlock_BrushStrength != null)
            TextBlock_BrushStrength.Text = TerrainEditState.BrushStrength.ToString("0.0");
        if (TextBlock_BrushFeather != null)
            TextBlock_BrushFeather.Text = TerrainEditState.BrushFeather.ToString("0.00");
    }

    /// <summary>
    /// 实时修改模型坐标
    /// </summary>
    private void TextBox_Transform_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (UndoManager.Instance == null) return;

        if (Entity == null)
            return;

        try
        {
            if (Entity is ISpatialEntity spatialEntity)
            {
                var matrix = spatialEntity.GetLocalTransform();
                _ = matrix.Decompose(out Vector3 scale, out _, out Vector3 translation);
                var euler = SharpDXUtils.ExtractEulerAngles(matrix);

                if (float.TryParse(TextBox_TransX.Text, out float value)) translation.X = value;
                if (float.TryParse(TextBox_TransY.Text, out value)) translation.Y = value;
                if (float.TryParse(TextBox_TransZ.Text, out value)) translation.Z = value;

                if (float.TryParse(TextBox_RotateX.Text, out value)) euler.X = value;
                if (float.TryParse(TextBox_RotateY.Text, out value)) euler.Y = value;
                if (float.TryParse(TextBox_RotateZ.Text, out value)) euler.Z = value;

                if (float.TryParse(TextBox_ScaleX.Text, out value)) scale.X = value;
                if (float.TryParse(TextBox_ScaleY.Text, out value)) scale.Y = value;
                if (float.TryParse(TextBox_ScaleZ.Text, out value)) scale.Z = value;

                // 欧拉角归一化
                euler.X = CoreUtil.NormalizeAngle(euler.X);
                euler.Y = CoreUtil.NormalizeAngle(euler.Y);
                euler.Z = CoreUtil.NormalizeAngle(euler.Z);

                TextBox_RotateX.Text = euler.X.ToString("0.00000");
                TextBox_RotateY.Text = euler.Y.ToString("0.00000");
                TextBox_RotateZ.Text = euler.Z.ToString("0.00000");

                var degToRad = (float)(Math.PI / 180.0);
                var quaternion = SharpDXUtils.CreateFromEulerAngles(euler.X * degToRad, euler.Y * degToRad, euler.Z * degToRad);

                var newMatrix = Matrix.Scaling(scale.X, scale.Y, scale.Z) *
                    Matrix.RotationQuaternion(quaternion) *
                    Matrix.Translation(translation);

                spatialEntity.SetTransform(newMatrix, true);
                spatialEntity.RequiresTransformUpdate = true;

                UpdateWriteHavokButtonState();
            }
        }
        catch (Exception ex)
        {
            App.Logger.LogError($"修改选中模型坐标时发生异常: {ex.Message}");
        }
    }

    /// <summary>
    /// TextBox鼠标滚轮事件
    /// </summary>
    private void TextBox_Transform_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (UndoManager.Instance == null) return;

        if (Entity == null)
            return;

        if (sender is TextBox textBox)
        {
            if (float.TryParse(textBox.Text, out float value))
            {
                float step;
                if (textBox.Name.StartsWith("TextBox_Rotate"))
                    step = 1.0f;
                else if (textBox.Name.StartsWith("TextBox_Scale"))
                    step = 0.1f;
                else
                    step = 0.1f; // translation

                if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                    step *= 10.0f;
                if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                    step *= 0.1f;

                value += (e.Delta > 0) ? step : -step;

                textBox.Text = value.ToString("0.00000");

                if (CheckBox_UniformScale.IsChecked == true && textBox.Name.StartsWith("TextBox_Scale"))
                {
                    TextBox_ScaleX.Text = value.ToString("0.00000");
                    TextBox_ScaleY.Text = value.ToString("0.00000");
                    TextBox_ScaleZ.Text = value.ToString("0.00000");
                }

                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// Slider鼠标滚轮事件
    /// </summary>
    private void Slider_Count_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (UndoManager.Instance == null) return;

        if (sender is Slider slider)
        {
            var value = slider.Value;

            if (value < slider.Minimum)
            {
                slider.Value = slider.Minimum;
                return;
            }
            if (value > slider.Maximum)
            {
                slider.Value = slider.Maximum;
                return;
            }

            var step = slider.SmallChange;
            value += (e.Delta > 0) ? step : -step;
            slider.Value = value;

            e.Handled = true;
        }
    }

    /// <summary>
    /// 移动模型到相机
    /// </summary>
    private void Button_SnapToCam_Click(object sender, RoutedEventArgs e)
    {
        if (UndoManager.Instance == null) return;

        if (Entity == null)
        {
            App.Logger.LogWarning("当前Entity为Null");
            return;
        }
        var camera = LevelEditorScreen.EditorCamera;
        if (camera == null)
        {
            App.Logger.LogWarning("未找到当前地图编辑器相机");
            return;
        }

        Button_SnapToCam.IsEnabled = false;

        if (Entity is ISpatialEntity spatialEntity)
        {
            var cameraPos = camera.WorldMatrix.TranslationVector;

            var matrix = spatialEntity.GetLocalTransform();
            _ = matrix.Decompose(out Vector3 scale, out Quaternion quaternion, out _);

            var newMatrix = Matrix.Scaling(scale) *
                Matrix.RotationQuaternion(quaternion) *
                Matrix.Translation(cameraPos);
            spatialEntity.SetTransform(newMatrix, true);
            spatialEntity.RequiresTransformUpdate = true;
        }

        Button_SnapToCam.IsEnabled = true;

        CoreUtil.PlayAudio();
    }

    /// <summary>
    /// 重置旋转
    /// </summary>
    private void Button_ResetRotate_Click(object sender, RoutedEventArgs e)
    {
        TextBox_RotateX.Text = "0.0";
        TextBox_RotateY.Text = "0.0";
        TextBox_RotateZ.Text = "0.0";
    }

    /// <summary>
    /// 重置缩放
    /// </summary>
    private void Button_ResetScale_Click(object sender, RoutedEventArgs e)
    {
        TextBox_ScaleX.Text = "1.0";
        TextBox_ScaleY.Text = "1.0";
        TextBox_ScaleZ.Text = "1.0";
    }

    /// <summary>
    /// 复制模型到相机
    /// </summary>
    private async void Button_CopyMeshToCamera_Click(object sender, RoutedEventArgs e)
    {
        if (UndoManager.Instance == null) return;

        if (Entity == null)
        {
            App.Logger.LogWarning("当前Entity为Null");
            return;
        }
        if (LevelEditorScreen == null)
        {
            App.Logger.LogWarning("当前LevelEditorScreen为Null");
            return;
        }

        Button_CopyMeshToCamera.IsEnabled = false;
        App.Logger.Log("=================================");

        App.Logger.Log("开始复制模型到相机...");

        try
        {
            await MeshUtil.CopySelectedMesh(new CopyMeshOption()
            {
                Entity = Entity,
                LevelEditorScreen = LevelEditorScreen,
                IsCopyToCamera = true
            });
        }
        catch (Exception ex)
        {
            App.Logger.LogError($"复制模型到相机发生异常：{ex.Message}");
        }
        finally
        {
            App.Logger.Log("复制模型到相机操作结束");
        }

        App.Logger.Log("=================================");
        Button_CopyMeshToCamera.IsEnabled = true;

        CoreUtil.PlayAudio();
    }

    /// <summary>
    /// 删除选中实体
    /// </summary>
    private async void Button_DeleteSelectedEntity_Click(object sender, RoutedEventArgs e)
    {
        if (UndoManager.Instance == null) return;

        if (Entity == null)
        {
            App.Logger.LogWarning("当前Entity为Null");
            return;
        }
        if (LevelEditorScreen == null)
        {
            App.Logger.LogWarning("当前LevelEditorScreen为Null");
            return;
        }

        Button_DeleteSelectedEntity.IsEnabled = false;
        App.Logger.Log("=================================");

        App.Logger.Log("开始删除选中实体...");

        try
        {
            await MeshUtil.DeleteSelectedMesh(new DeleteMeshOption()
            {
                Entity = Entity,
                LevelEditorScreen = LevelEditorScreen
            });
        }
        catch (Exception ex)
        {
            App.Logger.LogError($"删除选中实体发生异常：{ex.Message}");
        }
        finally
        {
            App.Logger.Log("删除选中实体操作结束");
        }

        App.Logger.Log("=================================");
        Button_DeleteSelectedEntity.IsEnabled = true;

        CoreUtil.PlayAudio();
    }

    /// <summary>
    /// 一键超平坦地形：修改二进制高度数据并写回 Res 与 Chunk，不更新视口。
    /// </summary>
    private void Button_FlattenTerrain_Click(object sender, RoutedEventArgs e)
    {
        TerrainEntity terrainEntity = FindTerrainEntity();
        if (terrainEntity == null)
            return;

        Button_FlattenTerrain.IsEnabled = false;
        App.Logger.Log("=================================");
        App.Logger.Log("开始一键超平坦地形...");

        try
        {
            float worldHeightY = 0.0f;
            if (TextBox_FlattenTerrainHeight != null &&
                float.TryParse(TextBox_FlattenTerrainHeight.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed))
            {
                worldHeightY = parsed;
            }

            bool ok = TerrainFlattenHelper.FlattenTerrain(terrainEntity, worldHeightY);
            if (ok)
                App.Logger.Log("一键超平坦地形完成，请手动重新打开地图查看效果");
            else
                App.Logger.LogWarning("一键超平坦地形未执行或失败");
        }
        catch (Exception ex)
        {
            App.Logger.LogError($"一键超平坦地形异常: {ex.Message}");
        }
        finally
        {
            App.Logger.Log("=================================");
            Button_FlattenTerrain.IsEnabled = true;
        }

        CoreUtil.PlayAudio();
    }

    private void CheckBox_TerrainEditMode_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = CheckBox_TerrainEditMode.IsChecked == true;
        TerrainEditState.IsTerrainEditModeEnabled = enabled;
        if (!enabled)
            TerrainEditState.ClearSelection();
        App.Logger.Log(enabled ? "已开启地形笔刷，按住左键涂抹" : "已关闭地形笔刷");
    }

    private void Radio_BrushMode_Checked(object sender, RoutedEventArgs e)
    {
        if (Radio_BrushLower?.IsChecked == true)
            TerrainEditState.BrushMode = TerrainBrushMode.Lower;
        else
            TerrainEditState.BrushMode = TerrainBrushMode.Raise;
    }

    private void Slider_BrushRadius_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        TerrainEditState.BrushRadius = (float)e.NewValue;
        UpdateBrushLabels();
    }

    private void Slider_BrushStrength_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        TerrainEditState.BrushStrength = (float)e.NewValue;
        UpdateBrushLabels();
    }

    private void Slider_BrushFeather_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        TerrainEditState.BrushFeather = (float)e.NewValue;
        UpdateBrushLabels();
    }

    private Screens.LevelEditorScreen GetActiveLevelScreen()
    {
        if (LevelEditorScreen?.EditorProvider?.RootLayer != null)
            return LevelEditorScreen;
        return Screens.LevelEditorScreen.Current;
    }

    private TerrainEntity FindTerrainEntity()
    {
        var screen = GetActiveLevelScreen();
        if (screen?.EditorProvider?.RootLayer == null)
        {
            App.Logger.LogWarning("无法获取当前关卡（请确保在关卡编辑器中打开地图）");
            return null;
        }

        List<Entity> entities = [];
        screen.EditorProvider.RootLayer.CollectEntities(entities);
        TerrainEntity terrainEntity = entities.Find(ei => ei is TerrainEntity) as TerrainEntity;
        if (terrainEntity == null)
            App.Logger.LogWarning("当前关卡没有地形实体，无法写入地形高度");
        return terrainEntity;
    }

    private void Button_CommitTerrainHeights_Click(object sender, RoutedEventArgs e)
    {
        TerrainEntity terrainEntity = FindTerrainEntity();
        if (terrainEntity == null)
            return;

        App.Logger.Log("=================================");
        App.Logger.Log("开始将地形笔刷修改写入 Res/Chunk...");

        try
        {
            TerrainHeightCommitHelper.CommitPendingEdits(terrainEntity);
        }
        catch (Exception ex)
        {
            App.Logger.LogError($"写入地形失败: {ex.Message}");
        }
        finally
        {
            App.Logger.Log("写入地形操作结束");
            App.Logger.Log("=================================");
        }

        CoreUtil.PlayAudio();
    }

    /// <summary>
    /// 将 StaticModelGroup 修改过的 Havok transform 通过 ModifyRes 写回资源。
    /// </summary>
    private void Button_WriteHavokTransformsToRes_Click(object sender, RoutedEventArgs e)
    {
        if (!HavokPhysicsData.HasAnyPendingCommit)
        {
            App.Logger.Log("没有待写回的 Havok Transform 修改");
            return;
        }

        Button_WriteHavokTransformsToRes.IsEnabled = false;
        int count = HavokPhysicsData.CommitAllPendingToRes();
        App.Logger.Log($"已将 {count} 个 Havok 物理资源的 Transform 修改写回 Res");
        Button_WriteHavokTransformsToRes.IsEnabled = false; // 写回后无待处理项，保持禁用
        CoreUtil.PlayAudio();
    }

    /// <summary>
    /// 修复OBBCollision缩放
    /// </summary>
    private void Button_FixOBBCollisionScale_Click(object sender, RoutedEventArgs e)
    {
        if (UndoManager.Instance == null) return;

        if (Entity == null)
        {
            App.Logger.LogWarning("当前Entity为Null");
            return;
        }
        if (!(Entity is OBBCollisionEntity))
        {
            App.Logger.LogWarning("当前Entity不是OBBCollisionEntity类型");
            return;
        }

        Button_FixOBBCollisionScale.IsEnabled = false;
        App.Logger.Log("=================================");

        App.Logger.Log("开始修复OBBCollision缩放...");

        try
        {
            var ebxAssetEnity = App.AssetManager.GetEbxEntry(Entity.FileGuid);
            EbxAsset ebxAsset = LoadedAssetManager.Instance.GetEbxAsset(Entity.FileGuid);
            dynamic rootObject = ebxAsset.RootObject;

            foreach (dynamic itemRef in rootObject.Objects)
            {
                Guid guid = CoreUtil.GetInternalClassGuid(itemRef);
                if (guid == Entity.InstanceGuid)
                {
                    var obbCollision = itemRef.Internal;

                    // FrostySdk.Ebx.LinearTransform
                    Matrix matrix = SharpDXUtils.FromLinearTransform(obbCollision.Transform);
                    _ = matrix.Decompose(out Vector3 scale, out Quaternion quaternion, out Vector3 translation);

                    // 找到第一个就跳出循环
                    obbCollision.HalfExtents.x *= scale.X;
                    obbCollision.HalfExtents.y *= scale.Y;
                    obbCollision.HalfExtents.z *= scale.Z;

                    var newMatrix = Matrix.Scaling(1.0f, 1.0f, 1.0f) *
                        Matrix.RotationQuaternion(quaternion) *
                        Matrix.Translation(translation);

                    // FrostySdk.Ebx.LinearTransform
                    obbCollision.Transform = Entity.MakeLinearTransform(newMatrix);
                    break;
                }
            }

            // 更新并保存修改
            ebxAsset.Update();
            App.AssetManager.ModifyEbx(ebxAssetEnity.Name, ebxAsset);
        }
        catch (Exception ex)
        {
            App.Logger.LogError($"修复OBBCollision缩放发生异常：{ex.Message}");
        }
        finally
        {
            UpdateUI();
            TextBox_Transform_TextChanged(null, null);

            App.Logger.Log("修复OBBCollision缩放操作结束");
        }

        App.Logger.Log("=================================");
        Button_FixOBBCollisionScale.IsEnabled = true;

        CoreUtil.PlayAudio();
    }

    /// <summary>
    /// 选择复制模型方向
    /// </summary>
    private void RadioButton_XYZAxis_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radioButton)
        {
            switch (radioButton.Name)
            {
                case "RadioButton_XAxisAdd":
                    copyDirection = CopyDirection.XAxisAdd;
                    break;
                case "RadioButton_YAxisAdd":
                    copyDirection = CopyDirection.YAxisAdd;
                    break;
                case "RadioButton_ZAxisAdd":
                    copyDirection = CopyDirection.ZAxisAdd;
                    break;
                case "RadioButton_XAxisSub":
                    copyDirection = CopyDirection.XAxisSub;
                    break;
                case "RadioButton_YAxisSub":
                    copyDirection = CopyDirection.YAxisSub;
                    break;
                case "RadioButton_ZAxisSub":
                    copyDirection = CopyDirection.ZAxisSub;
                    break;
            }
        }
    }

    /// <summary>
    /// 打印选中信息
    /// </summary>
    private void Button_PrintSelectInfo_Click(object sender, RoutedEventArgs e)
    {
        if (UndoManager.Instance == null) return;

        if (Entity == null)
        {
            App.Logger.LogWarning("当前Entity为Null");
            return;
        }

        Button_PrintSelectInfo.IsEnabled = false;
        App.Logger.Log("=================================");

        App.Logger.Log($"Entity.DisplayName: {Entity.DisplayName}");
        App.Logger.Log($"Entity.Layer.LayerName: {Entity.Layer.LayerName}");
        App.Logger.Log($"Entity.FileGuid: {Entity.FileGuid}");
        App.Logger.Log($"Entity.InstanceGuid: {Entity.InstanceGuid}");

        App.Logger.Log("----------");
        App.Logger.Log($"Entity.Parent.DisplayName: {Entity.Parent?.DisplayName}");
        App.Logger.Log($"Entity.Owner.DisplayName: {Entity.Owner?.DisplayName}");

        App.Logger.Log("----------");
        App.Logger.Log($"Entity.Parent.Layer.LayerName: {Entity.Parent?.Layer?.LayerName}");
        App.Logger.Log($"Entity.Owner.Layer.LayerName: {Entity.Owner?.Layer?.LayerName}");

        App.Logger.Log("----------");
        var ebxAssetEnity = App.AssetManager.GetEbxEntry(Entity.FileGuid);
        App.Logger.Log($"EbxAssetEnity Name: {ebxAssetEnity.Name}");

        if (Entity is ISpatialEntity spatialEntity)
        {
            App.Logger.Log("----------");

            var matrix1 = spatialEntity.GetTransform();
            App.Logger.Log($"Entity GetTransform: \t\t{matrix1}");

            var matrix2 = spatialEntity.GetLocalTransform();
            App.Logger.Log($"Entity GetLocalTransform: \t{matrix2}");
        }

        if (Entity is StaticModelGroupElementEntity staticModelGroupElementEntity)
        {
            App.Logger.Log("----------");

            App.Logger.Log($"当前 Entity 为 StaticModelGroupElementEntity 类型");
            var transform = staticModelGroupElementEntity.Data.Transform;
            App.Logger.Log(transform.ToString());
        }

        App.Logger.Log("=================================");
        Button_PrintSelectInfo.IsEnabled = true;

        CoreUtil.PlayAudio();
    }

    /// <summary>
    /// 复制模型
    /// </summary>
    private async void Button_CopyMesh_Click(object sender, RoutedEventArgs e)
    {
        if (UndoManager.Instance == null) return;

        if (Entity == null)
        {
            App.Logger.LogWarning("当前Entity为Null");
            return;
        }
        if (LevelEditorScreen == null)
        {
            App.Logger.LogWarning("当前LevelEditorScreen为Null");
            return;
        }

        Button_CopyMesh.IsEnabled = false;
        UniformGrid_CopyDirection.IsEnabled = false;
        ProgressBar_TaskState.IsIndeterminate = true;
        App.Logger.Log("=================================");

        var stopwatch = new Stopwatch();
        stopwatch.Start();
        App.Logger.Log("开始复制选择的网格体...");

        try
        {
            // 复制数量
            var count = (int)Slider_CopyCount.Value;
            // 复制间距
            var step = (float)Slider_CopyStep.Value;

            await MeshUtil.CopySelectedMesh(new CopyMeshOption()
            {
                Entity = Entity,
                LevelEditorScreen = LevelEditorScreen,
                IsCopyToCamera = false,
                Count = count,
                Step = step,
                CopyDirection = copyDirection
            });
        }
        catch (Exception ex)
        {
            App.Logger.LogError($"复制选择的网格体发生异常：{ex.Message}");
        }
        finally
        {
            stopwatch.Stop();
            App.Logger.Log($"复制选择的网格体操作结束，耗时：{stopwatch.ElapsedMilliseconds}ms");
        }

        App.Logger.Log("=================================");
        Button_CopyMesh.IsEnabled = true;
        UniformGrid_CopyDirection.IsEnabled = true;
        ProgressBar_TaskState.IsIndeterminate = false;

        CoreUtil.PlayAudio();
    }
}
