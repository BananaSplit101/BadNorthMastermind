using System.Collections.Generic;
using System.Reflection;
using BadNorthMP.Game;
using BadNorthMP.Net;
using BadNorthMP.Spectator;
using HarmonyLib;
using Voxels.TowerDefense;
using Voxels.TowerDefense.CampaignGeneration;
using Voxels.TowerDefense.ProfileInternals;

namespace BadNorthMP.Patches
{
    // Bad North's campaign generation assumes a campaign starts with two commanders (a party can start with four):
    // rescue islands are numbered from commander 2 and random traits go to commanders 2 and up. These patches keep the
    // party's own commanders out of both, for new party campaigns, and repair the rescue islands of older ones.

    internal static class PartyCampaign
    {
        /// <summary>The party's starting commanders while the leader creates a new party campaign (0: not creating one).</summary>
        public static int Starting;

        public static bool Leader => PartyFlow.Active && Roles.IsDefender && !SpectatorSession.Active;

        /// <summary>Point unplayed rescue islands at commanders who can be rescued (see RescueIslands.Repair).</summary>
        public static void RepairRescues(CampaignSave save, System.Func<int, bool> rescuable, string when)
        {
            List<LevelState> levels = save.levelStates;
            if (levels == null || levels.Count == 0) return;
            var ids = new int[levels.Count];
            var meta = new bool[levels.Count];
            var played = new bool[levels.Count];
            for (int i = 0; i < levels.Count; i++) { ids[i] = levels[i].heroId; meta[i] = levels[i].metaReward; played[i] = levels[i].playCount > 0; }
            var canRescue = new bool[PartyRoster.MaxHeroes];
            for (int hero = 0; hero < canRescue.Length; hero++) canRescue[hero] = rescuable(hero);
            int changed = RescueIslands.Repair(ids, meta, played, canRescue);
            if (changed == 0) return;
            for (int i = 0; i < levels.Count; i++) { levels[i].heroId = ids[i]; levels[i].metaReward = meta[i]; }
            ModLog.Info($"Party campaign ({when}): {changed} rescue islands pointed at commanders who can really be rescued");
        }

        public static MethodBase Creator(System.Type type)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (method.Name.EndsWith("OnCampaigCreation")) return method;
            return null;
        }
    }

    /// <summary>A new campaign: note how many commanders the party starts with (all the save holds before generation).</summary>
    [HarmonyPatch(typeof(Campaign), nameof(Campaign.Create))]
    internal static class PartyCampaignCreatePatch
    {
        private static void Prefix(CampaignSave campaignSave)
        {
            PartyCampaign.Starting = PartyCampaign.Leader && PartyFlow.Loading && campaignSave != null && campaignSave.heroes != null
                ? campaignSave.heroes.Count : 0;
        }
    }

    /// <summary>Right after the map is laid out: rescue islands get commanders from after the party's own.</summary>
    [HarmonyPatch]
    internal static class PartyRescueIslandsPatch
    {
        private static MethodBase TargetMethod() => PartyCampaign.Creator(typeof(GraphGenerator));

        private static void Postfix(Campaign campaign, ref System.Collections.IEnumerator __result)
        {
            if (PartyCampaign.Starting > 2) __result = Then(__result, campaign);
        }

        private static System.Collections.IEnumerator Then(System.Collections.IEnumerator original, Campaign campaign)
        {
            while (original.MoveNext()) yield return original.Current;
            int starting = PartyCampaign.Starting;
            // The commanders to rescue may not be generated yet: everyone after the party's own can be.
            PartyCampaign.RepairRescues(campaign.campaignSave, hero => hero >= starting, "new");
        }
    }

    /// <summary>
    /// Random traits for the commanders to rescue: while they're handed out, stand-ins take the places of the party's
    /// commanders beyond the first two (their drafted traits stay theirs), and a trait a stand-in got goes to a
    /// commander to rescue who has none.
    /// </summary>
    [HarmonyPatch]
    internal static class PartyRescueTraitsPatch
    {
        private static MethodBase TargetMethod() => PartyCampaign.Creator(typeof(HeroTraitAssigner));

        private static void Postfix(Campaign campaign, ref System.Collections.IEnumerator __result)
        {
            if (PartyCampaign.Starting > 2) __result = Around(__result, campaign.campaignSave.heroes, PartyCampaign.Starting);
        }

        private static System.Collections.IEnumerator Around(System.Collections.IEnumerator original, List<HeroDefinition> heroes, int starting)
        {
            var party = new Dictionary<int, HeroDefinition>();
            for (int i = 2; i < starting && i < heroes.Count; i++) { party[i] = heroes[i]; heroes[i] = new HeroDefinition(i); }
            try
            {
                while (original.MoveNext()) yield return original.Current;
            }
            finally
            {
                var given = new List<HeroUpgradeDefinition>();
                foreach (var seat in party)
                {
                    var standIn = heroes[seat.Key];
                    if (standIn.traitUpgrade != null && standIn.traitUpgrade.definition) given.Add(standIn.traitUpgrade.definition);
                    heroes[seat.Key] = seat.Value;
                }
                foreach (HeroUpgradeDefinition trait in given)
                    for (int i = starting; i < heroes.Count; i++)
                        if (heroes[i] != null && heroes[i].traitUpgrade == null) { heroes[i].SetTrait(trait); break; }
            }
        }
    }

    /// <summary>Older party campaigns: their unplayed rescue islands pointing at party commanders are repaired on load.</summary>
    [HarmonyPatch(typeof(Campaign), nameof(Campaign.Setup))]
    internal static class PartyRescueRepairPatch
    {
        private static void Prefix(CampaignSave campaignSave)
        {
            if (!PartyCampaign.Leader || campaignSave == null || campaignSave.heroes == null) return;
            List<HeroDefinition> heroes = campaignSave.heroes;
            PartyCampaign.RepairRescues(campaignSave, hero => hero < heroes.Count && heroes[hero] != null && heroes[hero].recruitable, "loaded");
        }
    }
}
