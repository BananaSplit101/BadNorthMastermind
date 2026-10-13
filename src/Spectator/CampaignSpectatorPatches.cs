using System;
using System.Collections.Generic;
using System.Reflection;
using BadNorthMP.Net;
using HarmonyLib;
using Voxels.TowerDefense;
using Voxels.TowerDefense.UI;
using Voxels.TowerDefense.UI.UpgradeScreen;

namespace BadNorthMP.Spectator
{
    [HarmonyPatch(typeof(SuperUpgradesCampaignProxy), nameof(SuperUpgradesCampaignProxy.OpenMenu))]
    internal static class RemoteCampaignUpgradesMenuPatch
    {
        private static bool Prefix()
        {
            if (!SpectatorSession.Active || PartyUpgrades.RemoteMenu) return true;
            if (Roles.IsMastermind && SpectatorSession.ViewingCampaign)
                BadNorthMP.UI.MastermindPanel.OpenMapUpgrades();
            return false;
        }
    }

    [HarmonyPatch(typeof(Profile), nameof(Profile.SaveUserSave))]
    internal static class RemoteCampaignUserSavePatch
    {
        private static bool Prefix() => !SpectatorSession.Active;
    }

    // In a party, upgrade and buy buttons only show for the player's own commanders. Remote defenders get them
    // too; their purchases are forwarded to the leader (PartyUpgrades).
    [HarmonyPatch(typeof(SuperUpgradeMenu), nameof(SuperUpgradeMenu.IsUpgradingAllowed))]
    internal static class RemoteCampaignHeroUpgradeAllowedPatch
    {
        private static void Postfix(SuperUpgradeMenu __instance, ref bool __result)
        {
            // While the menu is open or shows a commander: the one it opens on is set up before the menu counts as
            // open. Its closing animation asks too (no commander shown) whether the coin counter goes back onto the
            // map; a "no" there loses the counter.
            HeroDefinition shown = PartyUpgrades.Focused(__instance);
            if (!__result || (!__instance.isOpen && shown == null)) return;
            if (SpectatorSession.Active && !PartyUpgrades.RemoteMenu) __result = false;
            else if (PartyFlow.Deployed) __result = PartyUpgrades.MayUpgrade(shown);
        }
    }

    // Natively the menu opens on whoever can buy the priciest upgrade; in a party, on one of the player's own commanders.
    [HarmonyPatch]
    internal static class PartyUpgradeMenuOpensOnOwnPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(SuperUpgradeMenu), nameof(SuperUpgradeMenu.OpenMenu), Type.EmptyTypes);

        private static bool Prefix(SuperUpgradeMenu __instance)
        {
            if (!PartyFlow.Deployed || Roles.IsMastermind || Profile.campaign == null) return true;
            PortraitToken best = null;
            int bestCost = -1;
            foreach (PortraitToken portrait in Traverse.Create(__instance).Field<List<PortraitToken>>("availablePortraits").Value)
            {
                HeroDefinition hero = portrait ? portrait.heroDef : null;
                if (hero == null || !hero.alive || !hero.recruited || !PartyUpgrades.MayUpgrade(hero)) continue;
                int cost = hero.GetMostExpensiveAvailableUpgradeCost(Profile.campaign.coinBank);
                if (cost > bestCost) { bestCost = cost; best = portrait; }
            }
            if (!best) return true;
            __instance.OpenMenu(best);
            return false;
        }
    }

    [HarmonyPatch(typeof(SuperUpgradeMenu), nameof(SuperUpgradeMenu.OnClicked), new[] { typeof(UpgradeToken) })]
    internal static class RemoteCampaignHeroPurchasePatch
    {
        // Includes item equip, which shares the native upgrade-token purchase path.
        private static bool Prefix(SuperUpgradeMenu __instance, UpgradeToken upgradeToken, out PartyUpgrades.Purchase __state)
        {
            __state = null;
            if (SpectatorSession.Active && !PartyUpgrades.RemoteMenu) return false;
            __state = PartyUpgrades.Begin(__instance, upgradeToken, out bool allow);
            return allow;
        }
        private static void Postfix(PartyUpgrades.Purchase __state) => PartyUpgrades.Complete(__state);
    }

    // Purchases are broadcast as they happen, so closing the menu saves without making everyone reload the map.
    [HarmonyPatch(typeof(SuperUpgradesCampaignProxy), "Voxels.TowerDefense.UI.IUpgradesProxy.OnMenuClosed")]
    internal static class PartyUpgradeMenuClosedPatch
    {
        private static void Prefix()
        {
            PartyUpgrades.MenuClosedAt = UnityEngine.Time.unscaledTime;
            if (PartyFlow.Deployed && Roles.IsDefender && !SpectatorSession.Active) CampaignSync.QuietSave = true;
        }
        private static Exception Finalizer(Exception __exception)
        {
            CampaignSync.QuietSave = false;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Profile), nameof(Profile.SaveCampaign))]
    internal static class RemoteCampaignSavePatch
    {
        // A stub disk handler alone is insufficient: SaveCampaign also replaces meta.campaigns
        // and can delete the current save. Skip those in-memory mutations as well.
        private static bool Prefix() => !SpectatorSession.Active;
        private static Exception Finalizer(Exception __exception)
        {
            if (!SpectatorSession.Active && Roles.IsDefender)
            {
                // The party's Next Turn: every player has played it on their own map already (TurnSync).
                bool turn = TurnSync.TakeQuietSave();
                if (CampaignSync.QuietSave || turn) CampaignSync.MarkQuiet();
                else CampaignSync.MarkDirty();
            }
            return __exception;
        }
    }

    // The map builds islands in the background as they come within reach and saves what it worked out (seeds, houses).
    // Every viewer's map works out the same, so those saves only refresh the leader's snapshot for late joiners: sent
    // loudly, they made everyone load the map a second time.
    [HarmonyPatch(typeof(IslandGenerator), "InfiniteEnumerator")]
    internal static class IslandGeneratorQuietSavePatch
    {
        private static void Postfix(ref IEnumerator<Voxels.GenInfo> __result) => __result = Quietly(__result);

        private static IEnumerator<Voxels.GenInfo> Quietly(IEnumerator<Voxels.GenInfo> original)
        {
            try
            {
                while (true)
                {
                    bool before = CampaignSync.QuietSave, more;
                    CampaignSync.QuietSave = true;
                    try { more = original.MoveNext(); }
                    finally { CampaignSync.QuietSave = before; }
                    if (!more) yield break;
                    yield return original.Current;
                }
            }
            finally { original.Dispose(); }
        }
    }

    [HarmonyPatch(typeof(CampaignMapUI), nameof(CampaignMapUI.OpenMenu))]
    internal static class CampaignMapOpenedPatch
    {
        // Includes continue/new campaign, checkpoint reload, and the return from a live battle.
        private static void Postfix()
        {
            if (SpectatorSession.Active || !Roles.IsDefender) return;
            // Closing the upgrade menu returns to the map; its purchases were already broadcast.
            if (PartyFlow.Deployed && UnityEngine.Time.unscaledTime - PartyUpgrades.MenuClosedAt < 1f) CampaignSync.MarkQuiet();
            else CampaignSync.MarkDirty();
        }
    }

    [HarmonyPatch(typeof(LevelNodeUIProxy), nameof(LevelNodeUIProxy.HandleClick))]
    internal static class RemoteCampaignIslandClickPatch
    {
        // Camera dragging, hovering and hero inspection remain native. In a party a click is a vote
        // (IslandVote); the leader launches the island the party chose.
        private static bool Prefix(LevelNodeUIProxy __instance)
        {
            bool viewing = Roles.IsDefender ? !SpectatorSession.Active : SpectatorSession.ViewingCampaign;
            if (!IslandVote.CanVote || !viewing) return !SpectatorSession.Active;
            LevelNode node = __instance.levelNode;
            if (IslandVote.IslandVotable(node)) IslandVote.Cast(node.index);
            // Reloading the current checkpoint stays the leader's native choice.
            else if (Roles.IsDefender && node && node.levelState.checkpointState == LevelState.CheckpointState.Current) return true;
            else FabricWrapper.PostEvent(FabricID.uiError);
            return false;
        }
    }

    [HarmonyPatch(typeof(CampaignMapUI), "HandleNextTurnButton")]
    internal static class PartyNextTurnVotePatch
    {
        // In a party, Next Turn is a vote option like an island.
        private static bool Prefix()
        {
            if (!IslandVote.CanVote || !Roles.IsDefender || SpectatorSession.Active) return true;
            IslandVote.Cast(IslandVote.NextTurn);
            return false;
        }
    }

    [HarmonyPatch]
    internal static class RemoteCampaignTurnPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(CampaignMapUI), "NextTurn");
            yield return AccessTools.Method(typeof(CampaignMapUI), "HandleNextTurnButton");
            yield return AccessTools.Method(typeof(CampaignMapUI), "ReloadCheckpoint");
        }
        // A player's map only plays the leader's Next Turn (TurnSync).
        private static bool Prefix() => !SpectatorSession.Active || TurnSync.Replaying;
    }

    [HarmonyPatch(typeof(MetaMenuHelpers), nameof(MetaMenuHelpers.LoadCheckpoint))]
    internal static class RemoteCampaignCheckpointPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!SpectatorSession.Active) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class RemoteCampaignLeavePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(LevelLeaver), "ExitToMainMenu");
            yield return AccessTools.Method(typeof(LevelLeaver), "ExitLevel");
            yield return AccessTools.Method(typeof(LevelLeaver), "ReplayLevel");
            yield return AccessTools.Method(typeof(LevelLeaver), "CompleteLevel");
            yield return AccessTools.Method(typeof(LevelLeaver), "AbandonLevel");
        }
        private static bool Prefix()
        {
            if (!SpectatorSession.Active) return true;
            SpectatorSession.Instance.Stop();
            return false;
        }
    }

    [HarmonyPatch(typeof(MetaMenuHelpers), nameof(MetaMenuHelpers.ExitToMainMenu))]
    internal static class RemoteCampaignMenuExitPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!SpectatorSession.Active) return true;
            SpectatorSession.Instance.Stop();
            __result = true;
            return false;
        }
    }
}
