using Frosty.Core;
using LevelEditorPlugin.BF1Core.Utils;
using System.Windows;
using System.Windows.Controls;

namespace LevelEditorPlugin.BF1Core.Views;

/// <summary>
/// EasyView.xaml 的交互逻辑
/// </summary>
public partial class EasyView : UserControl
{
    public EasyView()
    {
        InitializeComponent();
    }

    private void Button_OpenPath_CartWoodLong_Click(object sender, RoutedEventArgs e)
    {
        var nameUrl = "Levels/MP/MP_Amiens/Prefabs/PF_CartWoodLong_03";
        CoreUtil.OpenEditorPath(nameUrl);

        CoreUtil.PlayAudio();
    }

    private void Button_OpenPath_MpAmiensContentSubLevel02A_Click(object sender, RoutedEventArgs e)
    {
        var nameUrl = "Levels/MP/MP_Amiens/ContentSubLevel02A";
        CoreUtil.OpenEditorPath(nameUrl);

        CoreUtil.PlayAudio();
    }

    private void Button_OpenPath_MpRidge_Click(object sender, RoutedEventArgs e)
    {
        var nameUrl = "Xpack3/Levels/MP/MP_Ridge/MP_Ridge";
        CoreUtil.OpenEditorPath(nameUrl);

        CoreUtil.PlayAudio();
    }

    private void Button_OpenPath_MpAmiensConquestSmall0_Click(object sender, RoutedEventArgs e)
    {
        var nameUrl = "Levels/MP/MP_Amiens/CONQUESTSMALL0";
        CoreUtil.OpenEditorPath(nameUrl);

        CoreUtil.PlayAudio();
    }

    private void Button_SetPlayerUnlimitedAmmo_Click(object sender, RoutedEventArgs e)
    {
        var nameUrl = "Gameplay/Characters/Soldiers/MpSoldier";
        var ebxAsset = App.AssetManager.GetEbx(nameUrl);

        dynamic rootObject = ebxAsset.RootObject;
        {
            dynamic soldierEntityData = rootObject.Object.Internal;

            dynamic soldierBodyComponentData = soldierEntityData.Components[0].Internal;
            dynamic bfSoldierHealthComponentData = soldierEntityData.Components[4].Internal;

            dynamic soldierWeaponsComponentData = soldierBodyComponentData.Components[14].Internal;

            soldierWeaponsComponentData.UnlimitedAmmo = true;
            soldierWeaponsComponentData.UnlimitedMags = true;

            // 设置最大生命值
            soldierEntityData.MaxHealth = 10000.0f;
            bfSoldierHealthComponentData.MaxHealth = 10000.0f;

            // 玩家生命值模块
            dynamic healthModule = soldierEntityData.HealthModule.Internal;

            // 回血延迟
            healthModule.RegenerationDelay = 2.0f;
            healthModule.RegenerationRate = 500.0f;
        }

        ebxAsset.Update();
        App.AssetManager.ModifyEbx(nameUrl, ebxAsset);

        App.Logger.Log("设置玩家无限弹药，操作成功");

        CoreUtil.PlayAudio();
    }
}
