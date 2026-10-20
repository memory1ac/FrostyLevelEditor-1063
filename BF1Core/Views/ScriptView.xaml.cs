using Frosty.Core;
using LevelEditorPlugin.BF1Core.Scripts;
using LevelEditorPlugin.BF1Core.Utils;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace LevelEditorPlugin.BF1Core.Views;

/// <summary>
/// ScriptView.xaml 的交互逻辑
/// </summary>
public partial class ScriptView : UserControl
{
    public ScriptView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 修改要塞征服复活点
    /// </summary>
    private void Button_ChangeMpUnderworldConquestAlternateSpawn_Click(object sender, RoutedEventArgs e)
    {
        MapScript.ChangeMpUnderworldConquestAlternateSpawn();

        CoreUtil.PlayAudio();
        App.Logger.Log("修改要塞征服复活点，操作成功");
    }

    /// <summary>
    /// 修改要塞死斗复活点
    /// </summary>
    private void Button_ChangeMpUnderworldTdmAlternateSpawn_Click(object sender, RoutedEventArgs e)
    {
        MapScript.ChangeMpUnderworldTdmAlternateSpawn();

        CoreUtil.PlayAudio();
        App.Logger.Log("修改要塞死斗复活点，操作成功");
    }

    /// <summary>
    /// 修复全部复活点组件
    /// </summary>
    private async void Button_FixAlternateSpawnEntityData_Click(object sender, RoutedEventArgs e)
    {
        Button_FixAlternateSpawnEntityData.IsEnabled = false;
        await Task.Run(() =>
        {
            var result = App.AssetManager.EnumerateEbx()
                .Where(ebx => ebx.DisplayName.StartsWith("layer", StringComparison.OrdinalIgnoreCase) &&
                (ebx.DisplayName.EndsWith("layer", StringComparison.OrdinalIgnoreCase) ||
                ebx.DisplayName.IndexOf("team", StringComparison.OrdinalIgnoreCase) >= 0 ||
                ebx.DisplayName.EndsWith("attackerhq", StringComparison.OrdinalIgnoreCase) ||
                ebx.DisplayName.EndsWith("defenderhq", StringComparison.OrdinalIgnoreCase) ||
                ebx.DisplayName.IndexOf("sector", StringComparison.OrdinalIgnoreCase) >= 0)
                );
            App.Logger.Log($"搜索到的数量为：{result.Count()}");

            foreach (var ebxAssetEntry in result)
            {
                var ebxAsset = App.AssetManager.GetEbx(ebxAssetEntry);
                dynamic rootObject = ebxAsset.RootObject;

                var objsCount = rootObject.Objects.Count;
                foreach (dynamic item in ebxAsset.RootObjects)
                {
                    // GameShared.AlternateSpawnEntityData
                    if (!CoreUtil.IsType(item, "AlternateSpawnEntityData"))
                        continue;

                    rootObject.Objects.Add(new FrostySdk.Ebx.PointerRef(item));
                }

                if (objsCount != rootObject.Objects.Count)
                {
                    ebxAsset.Update();
                    App.AssetManager.ModifyEbx(ebxAssetEntry.Name, ebxAsset);
                }
            }
        });
        Button_FixAlternateSpawnEntityData.IsEnabled = true;

        App.Logger.Log("修复全部复活点组件，操作成功");

        CoreUtil.PlayAudio();
    }
}
