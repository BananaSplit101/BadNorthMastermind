using System.Collections.Generic;
using System.Reflection;
using BadNorthMP.Net;
using HarmonyLib;
using I2.Loc;
using UnityEngine;
using UnityEngine.UI;
using Voxels.TowerDefense;
using Voxels.TowerDefense.UI;

namespace BadNorthMP.UI
{
    // Hidden Lineup and Unit Integrity Display, ported from GMW's mods of the same names (used with permission).

    /// <summary>In multiplayer, defenders don't see which Vikings are coming; the mastermind (who sends them) does.</summary>
    internal static class HiddenLineup
    {
        public static bool Active => ModConfig.HideEnemyLineup != null && ModConfig.HideEnemyLineup.Value &&
            SteamSession.Instance && SteamSession.Instance.InLobby && !Roles.IsMastermind;
    }

    /// <summary>The island intro's row of Viking faces.</summary>
    [HarmonyPatch]
    internal static class HiddenLineupIntroPatch
    {
        private static MethodBase TargetMethod()
        {
            foreach (MethodInfo method in typeof(EnemyLineupIntro).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (method.Name.EndsWith("OnSetup"))
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(Island)) return method;
                }
            return AccessTools.Method(typeof(EnemyLineupIntro), "Voxels.TowerDefense.IslandGameplayManager.ISetupIsland.OnSetup");
        }

        private static void Postfix(EnemyLineupIntro __instance)
        {
            if (!HiddenLineup.Active || __instance.vikingInfoList == null) return;
            foreach (EnemyLineupIntro.VikingInfo info in __instance.vikingInfoList)
                if (info != null && info.intro) info.intro.gameObject.SetActive(false);
        }
    }

    /// <summary>The results screen's lineup of Vikings faced.</summary>
    [HarmonyPatch(typeof(EnemyLineup), nameof(EnemyLineup.Prepare))]
    internal static class HiddenLineupResultsPatch
    {
        private static void Postfix(EnemyLineup __instance)
        {
            if (!HiddenLineup.Active) return;
            List<AgentInfo> infos = Traverse.Create(__instance).Field<List<AgentInfo>>("vikingInfoList").Value;
            if (infos == null) return;
            foreach (AgentInfo info in infos)
                if (info) info.gameObject.SetActive(false);
        }
    }

    /// <summary>The enemy icons on the campaign map's islands.</summary>
    [HarmonyPatch(typeof(LevelNodeEnemy), "Setup")]
    internal static class HiddenLineupMapPatch
    {
        private static void Postfix(LevelNodeEnemy __instance)
        {
            if (HiddenLineup.Active) __instance.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// The mastermind knows every Viking type: the map's island icons show the real faces, not the "?" the game keeps
    /// until the defenders' campaign has met that type. Not counted as a discovery (no flourish).
    /// </summary>
    [HarmonyPatch(typeof(LevelNodeEnemy), nameof(LevelNodeEnemy.UpdateVisual))]
    internal static class MastermindKnowsEveryVikingPatch
    {
        private static bool Prefix(LevelNodeEnemy __instance, ref bool __result, VikingReference ___vikingReference, ref bool ___seen)
        {
            if (!Roles.IsMastermind || ___seen || ___vikingReference == null || ___vikingReference.seen) return true;
            var headshot = __instance.GetComponent<HeadshotImage>();
            if (!headshot) return true;
            headshot.sprite = UnitIcons.Of(___vikingReference);
            headshot.rectTransform.sizeDelta = headshot.rectTransform.sizeDelta * __instance.scale;
            ___seen = true;
            __result = false;
            return false;
        }
    }

    /// <summary>A selected squad's banner shows how many soldiers are left: "Name (5/9 left)".</summary>
    [HarmonyPatch(typeof(SquadSelectedBanner), nameof(SquadSelectedBanner.Setup))]
    internal static class UnitIntegrityPatch
    {
        private static void Postfix(SquadSelectedBanner __instance, EnglishSquad squad)
        {
            if (!squad || squad.hero == null || ModConfig.ShowUnitIntegrity == null || !ModConfig.ShowUnitIntegrity.Value) return;
            Localize name = Traverse.Create(__instance).Field<Localize>("nameText").Value;
            Text text = name ? name.GetComponent<Text>() : null;
            if (!text) return;
            // The banner is pooled and Setup sets the term again each time; keep it from re-translating over our text.
            name.enabled = false;
            var updater = __instance.GetComponent<UnitIntegrity>();
            if (!updater) updater = __instance.gameObject.AddComponent<UnitIntegrity>();
            updater.Initialize(squad, text, ScriptLocalization.Get(squad.hero.nameTerm));
        }
    }

    internal sealed class UnitIntegrity : MonoBehaviour
    {
        private EnglishSquad squad;
        private Text text;
        private string baseName;
        private int peak;

        public void Initialize(EnglishSquad newSquad, Text label, string name)
        {
            squad = newSquad;
            text = label;
            baseName = string.IsNullOrEmpty(name) ? squad.hero.dbgName : name;
            peak = 0;
            Refresh();
        }

        private void Update() => Refresh();

        private void Refresh()
        {
            if (!squad || !text) return;
            int living = 0;
            foreach (Agent agent in squad.agents)
                if (agent && (agent.deadState == null || !agent.deadState.active)) living++;
            peak = Mathf.Max(peak, living);
            // A mirrored squad may not know its full size; the most it has had is the next best thing.
            int full = squad.maxCount > 0 ? squad.maxCount : peak;
            string wanted = $"{baseName}\n({living}/{full} left)";
            if (text.text != wanted) text.text = wanted;
        }
    }
}
