using System;
using System.Collections.Generic;
using System.Reflection;
using BadNorthMP.Net;
using BadNorthMP.Spectator;
using HarmonyLib;
using I2.Loc;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.UI;

namespace BadNorthMP.UI
{
    /// <summary>
    /// The party's choice of commanders (DeployFlow) in the game's own loadout screen. Each defender's banners show only
    /// their own commanders, as many as their share of squads (with copies of the banner past the game's four, the way
    /// GMW's Extended Deployment adds them), plus the island's rescued commander for whoever commands it. Deploy marks
    /// the player ready instead of landing anyone; the campaign leader lands everyone once all are ready. If the screen
    /// can't be set up, DeployPanel's own cards stand in.
    /// </summary>
    internal class NativeLoadout : MonoBehaviour
    {
        /// <summary>The loadout screen is open for the party's choice (watchers' islands mustn't force "running" then).</summary>
        public static bool Showing { get; private set; }
        /// <summary>The game's screen couldn't be set up: DeployPanel shows its cards instead.</summary>
        public static bool Unavailable { get; private set; }

        private static int configuredToken = -1;
        private static bool warned;

        private void Update()
        {
            var manager = Singleton<IslandGameplayManager>.instance;
            DeploySeat mine = DeployFlow.Mine;
            bool want = PartyFlow.Deployed && DeployFlow.Choosing && mine != null && mine.Choosing && !mine.ready &&
                Profile.campaign != null && manager && manager.island && !Unavailable;
            if (!want)
            {
                if (Showing && manager && manager.states.loadout.active) Close(manager);
                Showing = false;
                Restore();
                if (!DeployFlow.Choosing) { configuredToken = -1; Unavailable = false; }
                return;
            }
            LoadoutUI loadout = FindLoadout();
            if (!loadout) { Fail("the game's loadout screen isn't there"); return; }
            if (configuredToken != DeployFlow.Current.token || !Showing)
            {
                try { Configure(loadout, DeployFlow.Current, mine); }
                catch (Exception e) { Fail(e.Message); return; }
                configuredToken = DeployFlow.Current.token;
            }
            Showing = true;
            if (!manager.states.loadout.active) manager.states.loadout.SetActive(true);
        }

        private static void Fail(string why)
        {
            Unavailable = true;
            Showing = false;
            if (!warned) { warned = true; ModLog.Warn("Choosing commanders with the mod's own cards: " + why); }
        }

        private static LoadoutUI FindLoadout()
        {
            foreach (LoadoutUI candidate in Resources.FindObjectsOfTypeAll<LoadoutUI>())
                if (candidate && candidate.gameObject.scene.IsValid()) return candidate;
            return null;
        }

        /// <summary>Close the screen without landing anyone (the leader does that when everyone is ready).</summary>
        public static void Close(IslandGameplayManager manager)
        {
            LoadoutUI loadout = FindLoadout();
            if (loadout)
            {
                // The screen framed the island for itself; give the camera back, as its own Deploy button does.
                var ui = Traverse.Create(loadout);
                var target = ui.Field<RectTransform>("cameraTarget").Value;
                var islandUi = ui.Field<IslandUIManager>("manager").Value;
                if (islandUi && islandUi.islandViewfinder && target) islandUi.islandViewfinder.Remove(target);
            }
            if (manager && manager.states.loadout.active) manager.states.loadout.SetActive(false);
            Showing = false;
            Restore();
        }

        // ---- The game's own setup, kept to put back --------------------------------------------------------------
        // The game sets each island's loadout up from its banners and slot count; a rescue island puts its commander
        // in the last slot and fills the slots before it. Left at one player's share (one slot, say), the next rescue
        // island had no slot before the last and ran off the end of the banners, which stopped the island loading.

        private static LoadoutUI nativeFor;
        private static LoadoutUIListBanner[] nativeBanners;
        private static int nativeSlots;
        private static Vector3 nativeScale;
        private static RectTransform nativeList;
        private static readonly List<Transform> nativeOrder = new List<Transform>();
        private static readonly List<bool> nativeActive = new List<bool>();
        private static bool changed;

        private static void Remember(LoadoutUI loadout, Traverse ui, Transform list, RectTransform listRect)
        {
            if (nativeFor == loadout) return;
            nativeFor = loadout;
            nativeBanners = ui.Field<LoadoutUIListBanner[]>("heroBanners").Value;
            nativeSlots = ui.Field<int>("maxSlots").Value;
            nativeList = listRect;
            nativeScale = listRect ? listRect.localScale : Vector3.one;
            nativeOrder.Clear();
            nativeActive.Clear();
            foreach (Transform child in list)
            {
                nativeOrder.Add(child);
                nativeActive.Add(child.gameObject.activeSelf);
            }
        }

        /// <summary>The game's banners, slot count, order and size again (the party's copies of the banner hidden).</summary>
        internal static void Restore()
        {
            if (!changed) return;
            changed = false;
            if (!nativeFor || nativeBanners == null || nativeBanners.Length == 0 || !nativeBanners[0]) return;
            var ui = Traverse.Create(nativeFor);
            ui.Field("heroBanners").SetValue(nativeBanners);
            ui.Field("maxSlots").SetValue(nativeSlots);
            Transform list = nativeBanners[0].transform.parent;
            var copies = new List<Transform>();
            foreach (Transform child in list) if (!nativeOrder.Contains(child)) copies.Add(child);
            for (int i = 0; i < nativeOrder.Count; i++)
            {
                if (!nativeOrder[i]) continue;
                nativeOrder[i].SetAsLastSibling();
                nativeOrder[i].gameObject.SetActive(nativeActive[i]);
            }
            foreach (Transform copy in copies)
            {
                copy.SetAsLastSibling();
                copy.gameObject.SetActive(false);
            }
            if (nativeList) nativeList.localScale = nativeScale;
        }

        // ---- Setting the screen up for this player ----------------------------------------------------------------

        private static void Configure(LoadoutUI loadout, PartyDeployment plan, DeploySeat mine)
        {
            List<HeroDefinition> heroes = Profile.campaign.heroes;
            var ui = Traverse.Create(loadout);
            bool rescue = plan.rescueHero >= 0 && plan.rescueOwner == mine.player && plan.rescueHero < heroes.Count;
            int slots = mine.quota + (rescue ? 1 : 0);

            // Enough banners: the game's own, then copies of the last.
            LoadoutUIListBanner[] current = ui.Field<LoadoutUIListBanner[]>("heroBanners").Value;
            if (current == null || current.Length == 0) throw new InvalidOperationException("no commander banners");
            Transform list = current[0].transform.parent;
            var listRect = ui.Field<RectTransform>("listTransform").Value;
            if (!listRect) listRect = list as RectTransform;
            Remember(loadout, ui, list, listRect);
            changed = true;
            var all = new List<LoadoutUIListBanner>();
            foreach (Transform child in list)
            {
                var banner = child.GetComponent<LoadoutUIListBanner>();
                if (banner) all.Add(banner);
            }
            MethodInfo onHeroChanged = AccessTools.Method(typeof(LoadoutUI), "OnHeroChanged");
            while (all.Count < slots)
            {
                var copy = Instantiate(all[all.Count - 1].gameObject, list).GetComponent<LoadoutUIListBanner>();
                copy.name = "Banner_Slot_" + all.Count;
                if (onHeroChanged != null) copy.onHeroChanged += (Action)Delegate.CreateDelegate(typeof(Action), loadout, onHeroChanged);
                all.Add(copy);
            }
            var used = new LoadoutUIListBanner[slots];
            for (int i = 0; i < all.Count; i++)
            {
                all[i].gameObject.SetActive(i < slots);
                if (i < slots) used[i] = all[i];
            }
            ui.Field("heroBanners").SetValue(used);
            ui.Field("maxSlots").SetValue(slots);

            // Only this player's commanders can be picked, swapped in or cycled to.
            var ordered = ui.Field<List<HeroDefinition>>("orderedHeroes").Value;
            ordered.Clear();
            foreach (int id in mine.candidates) if (id < heroes.Count) ordered.Add(heroes[id]);
            var clicked = ui.Field<Action<LoadoutUIListBanner>>("handleListHeroClicked").Value;
            var next = ui.Field<Func<HeroDefinition, int, HeroDefinition>>("getNextDeployableHero").Value;
            var close = ui.Field<Action<LoadoutUIListBanner>>("handleListHeroClose").Value;
            for (int i = 0; i < mine.quota; i++)
            {
                HeroDefinition hero = i < mine.picks.Count && mine.picks[i] < heroes.Count ? heroes[mine.picks[i]] : null;
                used[i].Setup(hero, clicked, next, close);
            }

            // The rescued commander, locked, in the last banner with the game's own heading above it.
            var rescueTitle = ui.Field<Localize>("localCommanderTitle").Value;
            if (rescue) used[slots - 1].Setup(heroes[plan.rescueHero], null);
            if (rescueTitle) rescueTitle.gameObject.SetActive(rescue);
            foreach (LoadoutUIListBanner banner in used) banner.transform.SetAsLastSibling();
            if (rescue && rescueTitle && rescueTitle.transform.parent == list)
            {
                rescueTitle.transform.SetAsLastSibling();
                used[slots - 1].transform.SetAsLastSibling();
            }
            for (int i = slots; i < all.Count; i++) all[i].transform.SetAsLastSibling();

            // More than six banners: a little smaller, so they still fit.
            if (listRect)
            {
                float scale = slots > 6 ? Mathf.Clamp(1.3f - slots * 0.06f, 0.2f, 1f) : 1f;
                listRect.localScale = new Vector3(scale * nativeScale.x, scale * nativeScale.y, nativeScale.z);
            }
        }

        /// <summary>This player's picks: the commanders in their banners (the rescued one apart).</summary>
        public static List<int> Picks(LoadoutUI loadout)
        {
            var picks = new List<int>();
            var heroes = Profile.campaign.heroes;
            int rescue = DeployFlow.Current != null ? DeployFlow.Current.rescueHero : -1;
            foreach (LoadoutUIListBanner banner in Traverse.Create(loadout).Field<LoadoutUIListBanner[]>("heroBanners").Value)
            {
                HeroDefinition hero = banner ? banner.heroDef : null;
                int id = hero ? heroes.IndexOf(hero) : -1;
                if (id >= 0 && id != rescue && !picks.Contains(id)) picks.Add(id);
            }
            return picks;
        }
    }

    /// <summary>Each island's loadout is set up from the game's own banners and slots, whatever the last party choice left.</summary>
    [HarmonyPatch]
    internal static class PartyLoadoutSetupPatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(LoadoutUI), "Voxels.TowerDefense.IslandGameplayManager.ISetupIsland.OnSetup");

        private static void Prefix() => NativeLoadout.Restore();
    }

    /// <summary>In the party's choice, Deploy means "ready": the leader lands everyone once all are.</summary>
    [HarmonyPatch(typeof(LoadoutUI), nameof(LoadoutUI.HandleDeployButton))]
    internal static class PartyLoadoutDeployPatch
    {
        private static bool Prefix(LoadoutUI __instance)
        {
            if (!NativeLoadout.Showing) return true;
            List<int> picks = NativeLoadout.Picks(__instance);
            if (!DeployFlow.Submit(picks))
            {
                FabricWrapper.PostEvent(FabricID.uiError);
                return false;
            }
            FabricWrapper.PostEvent(FabricID.uiButtonClick);
            NativeLoadout.Close(Singleton<IslandGameplayManager>.instance);
            return false;
        }
    }

    /// <summary>The Deploy button follows the party's rule: one to this player's share of their own commanders.</summary>
    [HarmonyPatch]
    internal static class PartyLoadoutCanDeployPatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(LoadoutUI), "CanDeploy", new[] { typeof(List<HeroDefinition>).MakeByRefType() });

        private static void Postfix(LoadoutUI __instance, ref bool __result)
        {
            if (!NativeLoadout.Showing || DeployFlow.Mine == null) return;
            int count = NativeLoadout.Picks(__instance).Count;
            __result = count >= 1 && count <= DeployFlow.Mine.quota;
        }
    }

    /// <summary>No leaving for the map in the middle of the party's choice.</summary>
    [HarmonyPatch(typeof(LoadoutUI), nameof(LoadoutUI.HandleExitToMapButton))]
    internal static class PartyLoadoutExitPatch
    {
        private static bool Prefix()
        {
            if (!NativeLoadout.Showing) return true;
            FabricWrapper.PostEvent(FabricID.uiError);
            return false;
        }
    }
}
