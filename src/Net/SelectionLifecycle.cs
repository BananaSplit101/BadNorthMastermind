using HarmonyLib;
using Voxels.TowerDefense;
using BadNorthMP.Spectator;

namespace BadNorthMP.Net
{
    internal static class SelectionLifecycle
    {
        public static void Clear()
        {
            RemoteDefender.ForgetSelection();
            var selector = Singleton<SquadSelector>.instance;
            if (!selector) return;
            var reference = Traverse.Create(selector).Field<RTM.Utilities.WeakReference<EnglishSquad>>("_selectedSquad").Value;
            // RTM's weak-reference bool tests CLR null, so it considers a destroyed Unity squad alive.
            // Clear the target before changing the state: OnSelectStateChange dereferences it even during teardown.
            if (reference != null) reference.Target = null;
            var changed = Traverse.Create(selector).Field<SquadSelector.OnSquadSelectionChanged>("onSquadSelectionChanged").Value;
            if (changed != null) changed(null);
            var state = Traverse.Create(selector).Field<State>("selectionState").Value;
            if (state != null) state.SetActive(false);
        }
    }

    [HarmonyPatch(typeof(EnglishSquad), nameof(EnglishSquad.ShowSelection))]
    internal static class SpectatorSelectionVisualPatch
    {
        private static bool Prefix(EnglishSquad __instance, bool show, bool showGodray)
        {
            if (!SpectatorSession.Active) return true;
            if (!__instance || __instance.selectedState == null || __instance.agents == null) return false;
            __instance.selectedState.SetActive(show);
            if (CinematicCamera.isActive) showGodray = false;
            foreach (var agent in __instance.agents) if (agent) agent.SetSelection(show, showGodray);
            return false;
        }
    }
}
