using System.Collections.Generic;
using System.Reflection;
using BadNorthMP.Game;
using HarmonyLib;
using Voxels;
using Voxels.TowerDefense;

namespace BadNorthMP.Patches
{
    [HarmonyPatch(typeof(LevelLeaver), nameof(LevelLeaver.ReplayLevel))]
    internal static class MultiplayerRestartPatch
    {
        private static bool Prefix()
        {
            if (Spectator.SpectatorSession.Active) return true;
            if (RestartRules.Refuse("restart") || RetryLimit.Refuse("retry")) return false;
            Net.BattleStream.End();
            Economy.Restart();
            CustomWaves.Reset();
            return true;
        }
    }

    // Leaving for the map mid-fight without a penalty is a restart too (the island stays open); the game only offers
    // it before the fight or when restarts are on. Abandoning (commanders left behind) stays available.
    [HarmonyPatch(typeof(LevelLeaver), nameof(LevelLeaver.ExitLevel))]
    internal static class NoRestartExitPatch
    {
        private static bool Prefix()
        {
            if (Spectator.SpectatorSession.Active) return true;
            var manager = Singleton<IslandGameplayManager>.instance;
            if (manager && manager.states.loadout.active) return true;
            // Back to the map and in again is a fresh go at the island too: it counts as a retry.
            return !RestartRules.Refuse("leave mid-fight") && !RetryLimit.Refuse("leave mid-fight");
        }
    }

    /// <summary>
    /// A party gets three retries on an island: the fourth is refused and the fight has to be won or lost. Leaving for the
    /// map mid-fight (which keeps the island open to come back to) counts as one. The count starts again on the next
    /// island. Single player keeps the game's own rule.
    /// </summary>
    internal static class RetryLimit
    {
        public const int MaxRetries = 3;
        private static string island = "";
        private static int used;

        public static bool Refuse(string what)
        {
            Island current = GameAccess.CurrentIsland;
            if (!Net.PartyFlow.Deployed || !current || !current.levelNode || Profile.campaign == null) return false;
            string key = Profile.campaign.seed + ":" + current.levelNode.index;
            if (key != island) { island = key; used = 0; }
            if (used >= MaxRetries)
            {
                ModLog.Info($"Refused to {what}: all retries on island {current.levelNode.index} are used");
                FabricWrapper.PostEvent(FabricID.uiError);
                UI.GameNotify.Post("No retries left on this island: fight it out", 3f);
                return true;
            }
            used++;
            ModLog.Info($"Island {current.levelNode.index}: retry {used} of {MaxRetries} ({what})");
            Net.MultiplayerGame.Instance.Announce(used < MaxRetries
                ? $"Retry {used} of {MaxRetries} on this island"
                : "Last retry on this island: next time it has to be fought out");
            return false;
        }
    }

    /// <summary>
    /// "Allow level restarts" off really means off: the game's buttons already hide, and this refuses the restart itself
    /// however it's reached (a debug command, another mod, a menu the game forgot to hide).
    /// </summary>
    internal static class RestartRules
    {
        public static bool Allowed
        {
            get
            {
                Island island = GameAccess.CurrentIsland;
                var save = island && island.levelNode && island.levelNode.campaign ? island.levelNode.campaign.campaignSave : Profile.campaign;
                return save == null || save.prefs.allowReplays;
            }
        }

        public static bool Refuse(string what)
        {
            if (Allowed) return false;
            ModLog.Warn($"Refused to {what}: this campaign doesn't allow level restarts");
            FabricWrapper.PostEvent(FabricID.uiError);
            UI.GameNotify.Post("This campaign doesn't allow level restarts", 3f);
            return true;
        }
    }

    // Raid implements IIslandPlay explicitly. This runs before it spawns the (hidden, offshore) longships:
    // with a mastermind, the generated raid is swapped for a placeholder and the economy starts.
    [HarmonyPatch]
    internal static class MastermindRaidPatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Raid), "Voxels.TowerDefense.IIslandPlay.OnIslandPlay");

        private static void Prefix(Raid __instance, Island island)
        {
            if (!CustomWaves.MastermindMode || Economy.Active)
                return;
            CustomWaves.ReplaceRaid(__instance);
            Economy.Begin(island);
        }
    }

    [HarmonyPatch(typeof(IslandWinConditions), nameof(IslandWinConditions.AllEnemiesDefeated))]
    internal static class MastermindWinPatch
    {
        // Normally: the last wave has spawned and no Vikings remain. With a mastermind the placeholder wave
        // never spawns, so decide here: every wave has sailed (or the mastermind gave up), every ship has
        // unloaded, and no Vikings remain.
        private static void Postfix(ref bool __result)
        {
            if (!Economy.Active)
                return;
            Island island = GameAccess.CurrentIsland;
            __result = Economy.Done && CustomWaves.AllLanded && island && island.vikings.agents.Count == 0;
        }
    }

    // How the island ended decides the mastermind's war-chest payout (Economy.End).
    [HarmonyPatch(typeof(EndOfLevel), "ProcessEOL")]
    internal static class MastermindIslandResultPatch
    {
        private static void Prefix(EndOfLevel.Reason reason)
        {
            if (Economy.Active) Economy.Result = reason;
        }
    }

    [HarmonyPatch(typeof(Island), nameof(Island.LeaveIsland))]
    internal static class MastermindLeaveIslandPatch
    {
        private static void Prefix()
        {
            if (Spectator.SpectatorSession.Active)
                return;
            Economy.End();
            CustomWaves.Reset();
        }
    }
}
