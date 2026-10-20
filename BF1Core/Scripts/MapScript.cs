using Frosty.Core;
using LevelEditorPlugin.BF1Core.Utils;
using SharpDX;
using System.Collections.Generic;

namespace LevelEditorPlugin.BF1Core.Scripts;

public static class MapScript
{
    /// <summary>
    /// 修改要塞征服复活点
    /// </summary>
    public static void ChangeMpUnderworldConquestAlternateSpawn()
    {
        // 需要移动的复活点位置
        var team1Trans = new Vector3(0.0f, 10.0f, 0.0f);
        var team2Trans = new Vector3(0.0f, 20.0f, 0.0f);

        var nameUrlList = new List<string>()
        {
            "xpack1/levels/mp_underworld/conquest0/layer2_conquest0_team1",
            "xpack1/levels/mp_underworld/conquest0/layer3_conquest0_team2"
        };
        foreach (var nameUrl in nameUrlList)
        {
            var ebxAsset = App.AssetManager.GetEbx(nameUrl);
            dynamic rootObject = ebxAsset.RootObject;

            // 先移动组件，确保能渲染
            foreach (dynamic item in ebxAsset.RootObjects)
            {
                // GameShared.AlternateSpawnEntityData
                if (!CoreUtil.IsType(item, "AlternateSpawnEntityData"))
                    continue;

                rootObject.Objects.Add(new FrostySdk.Ebx.PointerRef(item));
            }

            // 然后分别传送队伍1和队伍2的坐标
            foreach (dynamic itemRef in rootObject.Objects)
            {
                dynamic item = itemRef.Internal;

                // GameShared.AlternateSpawnEntityData
                if (!CoreUtil.IsType(item, "AlternateSpawnEntityData"))
                    continue;

                string teamStr = item.Team.ToString();
                if (teamStr == "Team1")
                {
                    item.Transform.trans.x = team1Trans.X;
                    item.Transform.trans.y = team1Trans.Y;
                    item.Transform.trans.z = team1Trans.Z;
                }
                else if (teamStr == "Team2")
                {
                    item.Transform.trans.x = team2Trans.X;
                    item.Transform.trans.y = team2Trans.Y;
                    item.Transform.trans.z = team2Trans.Z;
                }
            }

            ///////////////////////////

            ebxAsset.Update();
            App.AssetManager.ModifyEbx(nameUrl, ebxAsset);
        }
    }

    /// <summary>
    /// 修改要塞死斗复活点
    /// </summary>
    public static void ChangeMpUnderworldTdmAlternateSpawn()
    {
        // 需要移动的复活点位置
        var team1Trans = new Vector3(-79.23306f, 160.1353f, -71.73387f);
        var team2Trans = new Vector3(-77.60828f, 160.133f, 16.74566f);

        var nameUrlList = new List<string>()
        {
            "xpack1/levels/mp_underworld/teamdeathmatch0/layer0_teamdeathmatch0_layer"
        };
        foreach (var nameUrl in nameUrlList)
        {
            var ebxAsset = App.AssetManager.GetEbx(nameUrl);
            dynamic rootObject = ebxAsset.RootObject;

            // 先移动组件，确保能渲染
            foreach (dynamic item in ebxAsset.RootObjects)
            {
                // GameShared.AlternateSpawnEntityData
                if (!CoreUtil.IsType(item, "AlternateSpawnEntityData"))
                    continue;

                rootObject.Objects.Add(new FrostySdk.Ebx.PointerRef(item));
            }

            int index = 0;

            // 然后分别传送队伍1和队伍2的坐标
            foreach (dynamic itemRef in rootObject.Objects)
            {
                dynamic item = itemRef.Internal;

                // GameShared.AlternateSpawnEntityData
                if (!CoreUtil.IsType(item, "AlternateSpawnEntityData"))
                    continue;

                // 死斗队伍只能设置为中立，否则队伍2将无法部署
                CoreUtil.SetFrostyEnum(item, "Team", "TeamNeutral");

                item.Priority = 1.0f;
                item.UseAsFallback = true;

                if (index % 2 == 0)
                {
                    item.Transform.trans.x = team1Trans.X;
                    item.Transform.trans.y = team1Trans.Y;
                    item.Transform.trans.z = team1Trans.Z;
                }
                else
                {
                    item.Transform.trans.x = team2Trans.X;
                    item.Transform.trans.y = team2Trans.Y;
                    item.Transform.trans.z = team2Trans.Z;
                }
                index++;
            }

            ///////////////////////////

            ebxAsset.Update();
            App.AssetManager.ModifyEbx(nameUrl, ebxAsset);
        }
    }
}
