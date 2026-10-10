using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.Upgrades;

namespace BadNorthMP.Net
{
    internal static class NativeDefenderControls
    {
        private static readonly HashSet<EnglishSquad> initialized = new HashSet<EnglishSquad>();
        // Clicking a squad's flag selects it; teammates' flags are made unclickable.
        private static readonly Dictionary<EnglishSquad, Collider[]> flagColliders = new Dictionary<EnglishSquad, Collider[]>();
        // The leader's tile for each squad (hero index, nav spot index), as last received.
        private static readonly List<KeyValuePair<int, int>> leaderTiles = new List<KeyValuePair<int, int>>();
        // Commanders (hero index) whose squad can flee right now, according to the leader.
        private static readonly HashSet<int> canFlee = new HashSet<int>();
        private static bool tilesReceived;
        private static float nextTiles, nextReport;
        private static string lastReport;

        public static void Reset()
        {
            initialized.Clear(); flagColliders.Clear(); leaderTiles.Clear(); canFlee.Clear();
            tilesReceived = false; nextTiles = nextReport = 0; lastReport = null;
        }

        /// <summary>Until the leader's first tile update arrives, estimate each squad's tile from where its hero stands.</summary>
        public static void UpdateTiles()
        {
            if (tilesReceived || Time.unscaledTime < nextTiles) return;
            nextTiles = Time.unscaledTime + .1f;
            foreach (var candidate in GameAccess.PlayerSquads)
                if (candidate is EnglishSquad squad && squad.heroAgent && squad.heroAgent.gameObject.activeInHierarchy)
                {
                    var spot = squad.GetHeroNavSpot();
                    if (spot && squad.navSpotOccupant.navSpot != spot) squad.navSpotOccupant.SetNavSpot(spot, false);
                }
        }

        /// <summary>
        /// Watchers: the leader's tile for every squad. Native ordering rules (no moving onto a teammate's tile,
        /// swapping your own squads) read tile occupancy, so it must match the leader's, not where heroes stand.
        /// </summary>
        public static void ApplyTiles(BinaryReader r)
        {
            int count = r.ReadByte();
            leaderTiles.Clear();
            for (int i = 0; i < count; i++)
                leaderTiles.Add(new KeyValuePair<int, int>(r.ReadInt32(), r.ReadInt16()));
            tilesReceived = true;
            RestoreTiles();
        }

        /// <summary>Watchers: which squads have a beached, empty boat they fit on (the copy has no boats of its own).</summary>
        public static void ApplyEvac(BinaryReader r)
        {
            int count = r.ReadByte();
            canFlee.Clear();
            for (int i = 0; i < count; i++)
            {
                int hero = r.ReadInt32();
                if (r.ReadBoolean()) canFlee.Add(hero);
            }
        }

        public static bool CanFlee(EnglishSquad squad) =>
            squad && squad.hero != null && Profile.campaign != null && canFlee.Contains(Profile.campaign.heroes.IndexOf(squad.hero));

        /// <summary>Put every squad back on the leader's tile, e.g. after the leader refused an order shown locally.</summary>
        public static void RestoreTiles()
        {
            Island island = GameAccess.CurrentIsland;
            if (!tilesReceived || !island || Profile.campaign == null) return;
            List<NavSpot> spots = island.navSpotter.navSpots;
            foreach (KeyValuePair<int, int> tile in leaderTiles)
            {
                if (tile.Value < 0 || tile.Value >= spots.Count) continue;
                foreach (var candidate in GameAccess.PlayerSquads)
                    if (candidate is EnglishSquad squad && squad.navSpotOccupant && squad.hero != null &&
                        Profile.campaign.heroes.IndexOf(squad.hero) == tile.Key && squad.navSpotOccupant.navSpot != spots[tile.Value])
                        squad.navSpotOccupant.SetNavSpot(spots[tile.Value], false);
            }
        }

        /// <summary>
        /// Remote defender: what the game does locally for a move (SquadMover.MoveTo), while the leader carries
        /// it out. The squad is shown on its new tile at once; a refusal puts it back.
        /// </summary>
        public static void MoveSent(EnglishSquad squad, NavSpot target, bool deselect)
        {
            if (squad.squadTemplate) FabricWrapper.PostEvent(squad.squadTemplate.moveSound);
            FabricWrapper.PostEvent("UI/InGame/UnitMove");
            if (squad.navSpotOccupant && !target.occupant)
                squad.navSpotOccupant.SetNavSpot(target, false);
            if (deselect) Singleton<SquadSelector>.instance.SelectSquad(null);
        }

        /// <summary>Remote defender: keep flag clicks in step with ownership, and log what can be controlled.</summary>
        public static void Report()
        {
            if (Time.unscaledTime < nextReport) return;
            nextReport = Time.unscaledTime + 2f;
            var report = new System.Text.StringBuilder();
            foreach (var candidate in GameAccess.PlayerSquads)
            {
                var squad = candidate as EnglishSquad;
                if (!squad) continue;
                bool mine = PartyFlow.CanSelect(squad);
                bool flagClickable = false;
                if (flagColliders.TryGetValue(squad, out Collider[] colliders))
                    foreach (Collider collider in colliders)
                        if (collider)
                        {
                            if (collider.enabled != mine) collider.enabled = mine;
                            flagClickable |= collider.enabled;
                        }
                if (!mine) continue;
                report.Append(squad.hero != null ? squad.hero.dbgName : squad.name)
                    .Append(": selectable=").Append(squad.selectable)
                    .Append(" banner=").Append(initialized.Contains(squad))
                    .Append(" flag=").Append(flagClickable).Append("; ");
            }
            string text = report.Length > 0 ? report.ToString() : "none of your squads are on this island";
            if (text == lastReport) return;
            lastReport = text;
            ModLog.Info("Remote controls: " + text);
        }

        public static void InitializeSquad(EnglishSquad squad)
        {
            if (!Roles.IsRemoteDefender || !squad.heroAgent) return;
            var colliders = new List<Collider>();
            foreach (var flag in squad.heroAgent.GetComponentsInChildren<Voxels.TowerDefense.Flag.FlagMesh>(true))
                foreach (var collider in flag.GetComponentsInChildren<Collider>(true))
                {
                    collider.enabled = PartyFlow.CanSelect(squad);
                    colliders.Add(collider);
                }
            flagColliders[squad] = colliders.ToArray();
            if (initialized.Contains(squad)) return;
            Traverse.Create(squad).Field("heroStun").SetValue(squad.heroAgent.stun);
            var prefabs = ScriptableObjectSingleton<PrefabManager>.instance;
            foreach (var upgrade in prefabs.universalUpgrades) squad.upgradeManager.AddUpgrade(upgrade, 0);
            foreach (var upgrade in squad.hero.upgrades) upgrade.definition.OnAppliedToSquad(squad, upgrade.level);
            RegisterPikes(squad);
            squad.OnSquadSpawnComplete();
            // The game's banner manager and ability buttons subscribe to this native event.
            var spawner = Singleton<IslandGameplayManager>.instance.squadSpawner;
            var callback = Traverse.Create(spawner).Field<Action<EnglishSquad>>("onSquadSpawned").Value;
            callback?.Invoke(squad);
            initialized.Add(squad);
            RestoreTiles();
        }

        public static bool Send(ActiveAbility ability, byte kind, int target)
        {
            var squad = ability.squad;
            if (!Roles.IsRemoteDefender || !PartyFlow.Deployed || !PartyFlow.CanSelect(squad) ||
                !Spectator.SpectatorSession.IslandReady())
            {
                ModLog.Info("Ability order not sent: " + ability.GetType().Name + " isn't usable on this squad now");
                return false;
            }
            var island = GameAccess.CurrentIsland;
            SteamSession.Instance.Send(Roles.DefenderId, Packet.Build(MsgType.DefenderAction, w =>
            {
                w.Write(PartyFlow.State.token); w.Write(PartyFlow.State.round); w.Write(Profile.campaign.seed);
                w.Write((int)island.levelNode.index); w.Write(Profile.campaign.heroes.IndexOf(squad.hero)); w.Write(2);
                w.Write(ability.GetType().FullName); w.Write(kind); w.Write(target);
            }));
            RemoteDefender.OrderSent(ability.GetType().Name + " " + squad.hero.dbgName + " target " + target);
            return true;
        }

        /// <summary>
        /// Pike Charge registers each soldier as it is created, so natively its upgrade is applied before the squad
        /// fills. A mirrored squad's soldiers already exist when its upgrades go on; register them now (the hero, created
        /// first natively, stays out), or the ability counts no pikes ("All pikes are dead").
        /// </summary>
        private static void RegisterPikes(EnglishSquad squad)
        {
            foreach (var upgrade in squad.upgradeManager)
            {
                if (!(upgrade is PikeChargeAbility pike) || pike.components == null) continue;
                foreach (Agent agent in squad.agents)
                {
                    if (!agent || agent == squad.heroAgent || pike.components.Exists(c => c && c.agent == agent)) continue;
                    pike.components.Add(agent.GetOrAddComponent<PikeChargeComponent>().SetupPikeChargeComponent(pike));
                }
            }
        }

        /// <summary>
        /// Pike Charge only works out whether its squad stands in formation while the squad is selected
        /// (PikeChargeAbility.UpdateSquadSelected, which also redraws target tiles). The leader never selects a remote
        /// player's squad, so the charge always read as "not in formation". The same check, without the drawing.
        /// </summary>
        private static void RefreshPikeFormation(PikeChargeAbility pike)
        {
            var fields = Traverse.Create(pike);
            List<PikeChargeComponent> inFormation = fields.Field<List<PikeChargeComponent>>("compoonentsInFormation").Value;
            if (inFormation == null || pike.components == null) return;
            inFormation.Clear();
            EnglishSquad squad = pike.squad;
            Agent hero = squad ? squad.heroAgent : null;
            if (hero && hero.aliveAndGrounded.active)
            {
                NavSpot spot = squad.GetHeroNavSpot();
                Vector3 position = hero.transform.position;
                if (spot && (spot.bounds.ClosestPoint(position) - position).sqrMagnitude < 0.04f)
                    foreach (PikeChargeComponent component in pike.components)
                        if (component && (!component.spear || (component.agent.transform.position - position).sqrMagnitude < 1f))
                            inFormation.Add(component);
            }
            fields.Field("inFormation").SetValue(inFormation.Count > 1 + pike.components.Count / 3);
        }

        public static bool Execute(EnglishSquad squad, BinaryReader reader, out string result)
        {
            string type = reader.ReadString(); byte kind = reader.ReadByte(); int target = reader.ReadInt32();
            result = "Invalid ability order.";
            if (type.Length > 200 || reader.BaseStream.Position != reader.BaseStream.Length) return false;
            ActiveAbility ability = null;
            foreach (var upgrade in squad.upgradeManager)
                if (upgrade is ActiveAbility active && active.GetType().FullName == type) { ability = active; break; }
            if (ability is PikeChargeAbility pikes) RefreshPikeFormation(pikes);
            if (!ability || ability.IsBlocked())
            {
                result = ability is PikeChargeAbility charge && charge.components != null && charge.components.Count <= 1
                    ? "No pikemen are left to charge." : ability is PikeChargeAbility
                    ? "Pike Charge needs the squad standing in formation on its tile." : "That ability is unavailable.";
                return false;
            }
            if (kind == 0 && ability is NavSpotTargetableAbility)
            {
                var spots = GameAccess.CurrentIsland.navSpotter.navSpots;
                var origin = squad.GetHeroNavSpot();
                var cache = Traverse.Create(ability).Field<NavSpotTargetCache>("targetCache").Value;
                if (!origin || target < 0 || target >= spots.Count || cache == null ||
                    origin.index >= cache.entries.Count || !cache.entries[origin.index].targetMask.Get(target))
                { result = "That tile is outside the ability's targets."; return false; }
                Traverse.Create(ability).Method("DoTargetedAction", origin, spots[target]).GetValue();
                Traverse.Create(ability).Method("OnActivated").GetValue();
            }
            else if (kind == 1 && ability is HouseTargetableAbility house)
            {
                if (target < 0 || target >= house.locations.Count || !house.locations[target].available || !house.IsAvailable())
                { result = "That house is unavailable."; return false; }
                house.SelectLocation(house.locations[target]);
            }
            else if (kind == 2 && ability is EvacuateAbility)
            {
                // Boat indices from the remote copy don't match; the leader picks the nearest suitable boat.
                return Game.Evacuation.Order(squad, null, out result);
            }
            else if (kind == 3 && ability is InvulnerableAbility && ability.IsAvailable()) ability.OnConfirmed();
            else return false;
            result = "Ability order accepted.";
            return true;
        }
    }

    [HarmonyPatch]
    internal static class RemoteNativeAbilityPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(NavSpotTargetableAbility), "HandleScreenSpaceClick");
            yield return AccessTools.Method(typeof(NavSpotTargetableAbility), "OnJoystickSelect");
            yield return AccessTools.Method(typeof(ReplenishAbility), "SelectLocation");
            yield return AccessTools.Method(typeof(CallTroopsAbility), "SelectLocation");
            yield return AccessTools.Method(typeof(EvacuateAbility), "SelectLocation");
            yield return AccessTools.Method(typeof(InvulnerableAbility), "OnConfirmed");
        }
        private static bool Prefix(ActiveAbility __instance, MethodBase __originalMethod, object[] __args)
        {
            if (!Roles.IsRemoteDefender || !Spectator.SpectatorSession.Active) return true;
            if (__instance is NavSpotTargetableAbility)
            {
                NavSpot spot = __args[0] is NavSpot ns ? ns : NavSpot.NavSpotCast((Vector2)__args[0]);
                if (spot) NativeDefenderControls.Send(__instance, 0, GameAccess.CurrentIsland.navSpotter.navSpots.IndexOf(spot));
            }
            else if (__instance is HouseTargetableAbility house)
                NativeDefenderControls.Send(__instance, 1, house.locations.IndexOf((SquadReplenishLocation)__args[0]));
            else if (__instance is EvacuateAbility)
            {
                var locations = Traverse.Create(__instance).Field<List<SquadEvacuationLocation>>("locations").Value;
                NativeDefenderControls.Send(__instance, 2, locations.IndexOf((SquadEvacuationLocation)__args[0]));
            }
            else NativeDefenderControls.Send(__instance, 3, -1);
            return false;
        }
    }

    [HarmonyPatch(typeof(NavSpotTargetableAbility), "HandleScreenSpaceClick")]
    internal static class RemoteNativeTargetResultPatch
    {
        private static void Postfix(ref bool __result)
        { if (Roles.IsRemoteDefender && Spectator.SpectatorSession.Active) __result = true; }
    }

    // Mirrored ships have no local sailing/evacuation simulation. Use the real evacuation
    // button, with the authority choosing the nearest available boat, just like healing.
    // Whether a boat is free comes from the leader (SquadEvac).
    [HarmonyPatch(typeof(EvacuateAbility), nameof(EvacuateAbility.IsAvailable))]
    internal static class RemoteNativeEvacAvailablePatch
    {
        private static void Postfix(EvacuateAbility __instance, ref bool __result)
        {
            if (Roles.IsRemoteDefender && Spectator.SpectatorSession.Active)
                __result = !__instance.isBanned && !__instance.IsBlocked() &&
                    PartyFlow.CanSelect(__instance.squad) && NativeDefenderControls.CanFlee(__instance.squad);
        }
    }

    // Puppet boats can't be clicked (no colliders or local boat list), so a click while Evacuate is targeted
    // would deselect the squad. Treat it as "flee to the nearest suitable boat".
    [HarmonyPatch(typeof(EvacuateAbility), "HandleScreenSpaceClick")]
    internal static class RemoteNativeEvacClickPatch
    {
        private static bool Prefix(EvacuateAbility __instance, ref bool __result)
        {
            if (!Roles.IsRemoteDefender || !Spectator.SpectatorSession.Active) return true;
            __result = true;
            if (!NativeDefenderControls.CanFlee(__instance.squad))
            {
                RemoteDefender.Refused("No boat is beached and empty yet.");
                return false;
            }
            RemoteDefender.Select(__instance.squad);
            RemoteDefender.Command(__instance.squad, 1, true);
            return false;
        }
    }

    // The remote copy has no boats of its own, so it would always say there are no ships.
    [HarmonyPatch(typeof(EvacuateAbility), "GetNotificationTerm")]
    internal static class RemoteNativeEvacNoticePatch
    {
        private static void Postfix(EvacuateAbility __instance, ref string __result)
        {
            if (Roles.IsRemoteDefender && Spectator.SpectatorSession.Active && !__instance.isBanned &&
                NativeDefenderControls.CanFlee(__instance.squad))
                __result = string.Empty;
        }
    }

    [HarmonyPatch(typeof(TargetableAbility), nameof(TargetableAbility.WillHandleConfirmation))]
    internal static class RemoteNativeEvacConfirmPatch
    {
        private static void Postfix(TargetableAbility __instance, ref bool __result)
        {
            if (__instance is EvacuateAbility && Roles.IsRemoteDefender && Spectator.SpectatorSession.Active) __result = false;
        }
    }

    [HarmonyPatch(typeof(TargetableAbility), nameof(TargetableAbility.OnConfirmed))]
    internal static class RemoteNativeEvacOrderPatch
    {
        private static bool Prefix(TargetableAbility __instance)
        {
            if (!(__instance is EvacuateAbility) || !Roles.IsRemoteDefender || !Spectator.SpectatorSession.Active) return true;
            RemoteDefender.Select(__instance.squad);
            RemoteDefender.Command(__instance.squad, 1, true);
            return false;
        }
    }
}
