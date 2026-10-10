using System.Collections.Generic;
using BadNorthMP.Net;
using HarmonyLib;
using Voxels.TowerDefense;

namespace BadNorthMP.Patches
{
    // In a party, the squad hotkeys count only the player's own squads: "1" is your first squad even when a
    // teammate's squad comes first in the island's list, and next/previous skip teammates' squads.

    [HarmonyPatch(typeof(SquadSelector), nameof(SquadSelector.SelectSquad), typeof(int))]
    internal static class OwnedSquadHotkeyPatch
    {
        private static bool Prefix(SquadSelector __instance, int idx, ref bool __result)
        {
            if (!PartyFlow.Active || Roles.IsMastermind) return true;
            List<EnglishSquad> owned = RemoteDefender.OwnedSquads(SteamSession.Instance.Me);
            if (idx < 0 || idx >= owned.Count)
            {
                FabricWrapper.PostEvent(FabricID.uiError);
                __result = false;
                return false;
            }
            __instance.SelectSquad(owned[idx], showGodray: true);
            __result = __instance.selectedSquad == owned[idx];
            return false;
        }
    }

    // GameController deselects when the pressed number matches this index, so it must count the same way.
    [HarmonyPatch(typeof(SquadSelector), "get_selectedSquadIdx")]
    internal static class OwnedSquadIndexPatch
    {
        private static void Postfix(SquadSelector __instance, ref int __result)
        {
            EnglishSquad selected = __instance.selectedSquad;
            if (PartyFlow.Active && !Roles.IsMastermind && selected)
                __result = RemoteDefender.OwnedSquads(SteamSession.Instance.Me).IndexOf(selected);
        }
    }

    // The native version also throws when nothing is selected and the first squad isn't selectable by this player.
    [HarmonyPatch(typeof(SquadSelector), "CycleSelection")]
    internal static class OwnedSquadCyclePatch
    {
        private static bool Prefix(SquadSelector __instance, int change)
        {
            if (!PartyFlow.Active || Roles.IsMastermind) return true;
            List<EnglishSquad> owned = RemoteDefender.OwnedSquads(SteamSession.Instance.Me);
            int count = owned.Count;
            int current = owned.IndexOf(__instance.selectedSquad);
            int start = current >= 0 ? current : change > 0 ? -1 : count;
            for (int step = 1; step <= count; step++)
            {
                EnglishSquad next = owned[((start + change * step) % count + count) % count];
                if (next != __instance.selectedSquad && next.selectable)
                {
                    __instance.SelectSquad(next, showGodray: true);
                    return false;
                }
            }
            FabricWrapper.PostEvent(FabricID.uiError);
            return false;
        }
    }
}
