using System.Collections.Generic;
using System.IO;
using BadNorthMP.Game;
using BadNorthMP.UI;
using Steamworks;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthMP.Net
{
    /// <summary>The mastermind's view of their army, sent by the leader. The war chest is upgrades.gold.</summary>
    internal struct MasterState
    {
        public bool inBattle;
        public int waveIndex, waveCount;     // the wave being planned (0-based); waveIndex == waveCount: all sailed
        public float secondsToDeparture;
        public float waveBudget, planCost;
        public int planTile;
        public int vikingsAlive;
        public bool done;
        public bool landingHold;             // the wave is due but waits for the defenders' boats to land
        public long sequence;
        public MasterUpgradeState upgrades;

        public bool FinalWave => waveIndex == waveCount - 1;

        public void Write(BinaryWriter w)
        {
            w.Write(inBattle);
            w.Write(waveIndex); w.Write(waveCount);
            w.Write(secondsToDeparture);
            w.Write(waveBudget); w.Write(planCost);
            w.Write(planTile);
            w.Write(vikingsAlive);
            w.Write(done);
            w.Write(landingHold);
            w.Write(sequence);
            upgrades.Write(w);
        }

        public static MasterState Read(BinaryReader r)
        {
            var state = new MasterState
            {
                inBattle = r.ReadBoolean(),
                waveIndex = r.ReadInt32(), waveCount = r.ReadInt32(),
                secondsToDeparture = r.ReadSingle(),
                waveBudget = r.ReadSingle(), planCost = r.ReadSingle(),
                planTile = r.ReadInt32(),
                vikingsAlive = r.ReadInt32(),
                done = r.ReadBoolean(),
                landingHold = r.ReadBoolean(),
                sequence = r.ReadInt64(),
                upgrades = MasterUpgradeState.Read(r),
            };
            if (state.waveIndex < 0 || state.waveCount < 0 || state.waveIndex > state.waveCount || state.waveCount > 100 ||
                !Valid(state.secondsToDeparture) || !Valid(state.waveBudget) || !Valid(state.planCost))
                throw new IOException("Invalid wave state");
            return state;
        }

        private static bool Valid(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
    }

    /// <summary>
    /// Requests from the mastermind (build a wave, give up) handled by the authoritative defender, and the
    /// state/results the defender sends back.
    /// </summary>
    internal static class MastermindNet
    {
        private const float StateInterval = 0.25f;
        private static float nextState;
        private static long stateSequence;
        private const MsgType BuyUpgradeMessage = MsgType.BuyUpgrade;
        private static bool upgradePending;
        private static int pendingUpgradeRevision;
        private static float upgradeSentAt;
        public static float LastStateTime { get; private set; } = -100f;
        public static bool HasFreshState => HasState && Time.unscaledTime - LastStateTime < 2f;
        public static bool UpgradePending => upgradePending && Time.unscaledTime - upgradeSentAt < 8f;

        public static void Reset()
        {
            State = default(MasterState);
            HasState = upgradePending = false;
            nextState = 0f;
            LastStateTime = LastResultTime = -100f;
            LastResult = "";
        }

        // Mastermind side
        public static MasterState State { get; private set; }
        public static bool HasState { get; private set; }
        public static string LastResult { get; private set; } = "";
        public static float LastResultTime { get; private set; } = -100f;

        private static SteamSession Session => SteamSession.Instance;

        private static bool FromMastermind(CSteamID sender) => sender == Roles.MastermindId;
        private static bool FromLeader(CSteamID sender) => sender == Roles.DefenderId;
        private static void ToLeader(byte[] packet) => Session.Send(Roles.DefenderId, packet);
        private static void ToMastermind(byte[] packet, bool reliable = true) => Session.Send(Roles.MastermindId, packet, reliable);

        public static void Init()
        {
            Router.On(BuyUpgradeMessage, (sender, r) =>
            {
                if (!(Roles.IsDefender && Roles.HasMastermind) || !FromMastermind(sender)) return;
                try
                {
                    if (r.BaseStream.Length - r.BaseStream.Position != 18)
                    { SendResult(false, "invalid upgrade request length"); return; }
                    long session = r.ReadInt64();
                    int revision = r.ReadInt32();
                    int island = r.ReadInt32();
                    var type = (VikingAgent.Type)r.ReadByte();
                    var kind = (UnitUpgrade)r.ReadByte();
                    bool ok = MastermindUpgrades.TryBuy(session, revision, island, type, kind, out string message);
                    SendState(reliable: true);
                    SendResult(ok, message);
                }
                catch (System.Exception e)
                {
                    ModLog.Error("Mastermind upgrade request failed: " + e);
                    SendResult(false, "upgrade request failed; see the campaign leader's log");
                }
            });
            Router.On(MsgType.WavePlan, (sender, r) =>
            {
                if (Roles.IsDefender && FromMastermind(sender))
                {
                    try { HandleWavePlan(sender, r); }
                    catch (System.Exception e)
                    {
                        ModLog.Error("Custom wave failed: " + e);
                        SendResult(false, "wave planning failed; see the campaign leader's log");
                    }
                }
            });
            Router.On(MsgType.CommanderBuy, (sender, r) => LeaderRequest(sender, r, 13, "recruiting", () =>
            {
                long session = r.ReadInt64(); int nextId = r.ReadInt32(); int type = r.ReadByte();
                bool ok = VikingCommanders.TryBuy(session, nextId, type, out string message);
                return new KeyValuePair<bool, string>(ok, message);
            }));
            Router.On(MsgType.CommanderSell, (sender, r) => LeaderRequest(sender, r, 12, "selling", () =>
            {
                long session = r.ReadInt64(); int id = r.ReadInt32();
                bool ok = VikingCommanders.TrySell(session, id, out string message);
                return new KeyValuePair<bool, string>(ok, message);
            }));
            Router.On(MsgType.CommanderDeploy, (sender, r) => LeaderRequest(sender, r, 8, "deploying", () =>
            {
                int id = r.ReadInt32(), tile = r.ReadInt32();
                bool ok = CommanderBattle.TryDeploy(id, tile, out string message);
                return new KeyValuePair<bool, string>(ok, message);
            }));
            Router.On(MsgType.CommanderOrder, (sender, r) => LeaderRequest(sender, r, 9, "ordering", () =>
            {
                int id = r.ReadInt32(); byte kind = r.ReadByte(); int target = r.ReadInt32();
                bool ok = CommanderBattle.TryOrder(id, kind, target, out string message);
                return new KeyValuePair<bool, string>(ok, message);
            }));
            Router.On(MsgType.GiveUp, (sender, r) =>
            {
                if (!Roles.IsDefender || !FromMastermind(sender) || !Economy.Active)
                    return;
                Economy.GiveUp();
                MultiplayerGame.Instance.Announce("The mastermind gave up! Defeat the remaining Vikings.");
                SendResult(true, "You gave up this island.");
            });
            Router.On(MsgType.MasterState, (sender, r) =>
            {
                if (!Roles.IsMastermind || !FromLeader(sender))
                    return;
                try
                {
                    var received = MasterState.Read(r);
                    if (r.BaseStream.Position != r.BaseStream.Length || received.sequence <= 0)
                        throw new IOException("Invalid mastermind state");
                    if (HasState && received.upgrades.session < State.upgrades.session) return;
                    if (HasState && received.upgrades.session == State.upgrades.session && received.sequence <= State.sequence) return;
                    if (HasState && received.upgrades.session == State.upgrades.session &&
                        received.upgrades.revision < State.upgrades.revision) return;
                    State = received;
                    HasState = true;
                    LastStateTime = Time.unscaledTime;
                    if (upgradePending && received.upgrades.revision > pendingUpgradeRevision) upgradePending = false;
                }
                catch (System.Exception e) { ModLog.Problem("invalid_mastermind_state", "Ignored invalid mastermind state: " + e.Message); }
            });
            Router.On(MsgType.Result, (sender, r) =>
            {
                if (!FromLeader(sender))
                    return;
                bool ok = r.ReadBoolean();
                string message = r.ReadString();
                if (upgradePending && !message.StartsWith("+"))
                    upgradePending = false;
                ModLog.Info("Mastermind: defender replied, success=" + ok + ": " + message);
                SetResult((ok ? "" : "Can't: ") + message);
            });
        }

        /// <summary>Leader: a mastermind request of an exact length, answered with the result and a fresh state.</summary>
        private static void LeaderRequest(CSteamID sender, BinaryReader r, int length, string what, System.Func<KeyValuePair<bool, string>> handle)
        {
            if (!(Roles.IsDefender && Roles.HasMastermind) || !FromMastermind(sender)) return;
            try
            {
                if (r.BaseStream.Length - r.BaseStream.Position != length) { SendResult(false, "invalid " + what + " request"); return; }
                KeyValuePair<bool, string> result = handle();
                SendState(reliable: true);
                SendResult(result.Key, result.Value);
            }
            catch (System.Exception e)
            {
                ModLog.Error("Mastermind " + what + " failed: " + e);
                SendResult(false, what + " failed; see the campaign leader's log");
            }
        }

        /// <summary>Leader, every frame: the wave timer, and the state the mastermind's panel shows.</summary>
        public static void Update()
        {
            if (!Roles.IsDefender)
                return;
            MastermindUpgradeSave.EnsureCampaign();
            Island current = GameAccess.CurrentIsland;
            if (!Economy.Active && !Economy.ReplayPending && CustomWaves.MastermindMode && current && current.state == Island.State.Playing)
            {
                current.raid.StopAllCoroutines();
                CustomWaves.ReplaceRaid(current.raid);
                Economy.Begin(current);
            }
            Economy.Tick(Time.deltaTime);
            if (!Roles.HasMastermind || Time.unscaledTime < nextState)
                return;
            nextState = Time.unscaledTime + StateInterval;
            SendState(reliable: false);
        }

        private static void SendState(bool reliable)
        {
            if (!(Roles.IsDefender && Roles.HasMastermind)) return;
            Island island = GameAccess.CurrentIsland;
            var upgrades = new MasterUpgradeState
            {
                session = MastermindSession.UpgradeSession,
                revision = MastermindSession.UpgradeRevision,
                islandIndex = Economy.Active && island && island.levelNode != null ? island.levelNode.index : -1,
                canBuy = MastermindUpgrades.CanBuy(),
                gold = MastermindSession.WarChest,
                levels = MastermindSession.CopyUpgrades(),
            };
            foreach (VikingReference type in MastermindUpgrades.AvailableTypes())
                upgrades.unitCosts[(int)type.type] = Economy.UnitCost(type);
            upgrades.nextCommanderId = MastermindSession.NextCommanderId;
            foreach (VikingCommander commander in MastermindSession.Commanders)
                upgrades.commanders.Add(new MasterUpgradeState.CommanderInfo
                {
                    id = commander.id, type = commander.type, crew = System.Math.Min(255, VikingCommanders.Crew(commander.type)), trait = commander.trait,
                    sellValue = CommanderRoster.SellValue(commander, upgrades.unitCosts[commander.type]),
                    status = CommanderRoster.Status(commander, Economy.Active ? Economy.DeployedCommanders.Keys : null),
                });
            bool active = Economy.Active;
            var state = new MasterState
            {
                inBattle = CustomWaves.CanLaunch(out string readinessError),
                waveIndex = active ? Economy.WaveIndex : 0,
                waveCount = active ? Economy.WaveCount : 0,
                secondsToDeparture = active ? Economy.SecondsToDeparture : 0f,
                waveBudget = active ? Economy.WaveBudget : 0f,
                planCost = active ? Economy.PlanCost : 0f,
                planTile = active ? Economy.PlanTile : -1,
                vikingsAlive = island && island.vikings ? island.vikings.agents.Count : 0,
                done = Economy.Active && Economy.Done,
                landingHold = Economy.HeldForLanding,
                sequence = ++stateSequence,
                upgrades = upgrades,
            };
            ToMastermind(Packet.Build(MsgType.MasterState, state.Write), reliable);
        }

        public static void SendResult(bool ok, string message)
        {
            ModLog.Info("Mastermind result: " + message);
            if (Roles.IsDefender && Roles.HasMastermind)
                ToMastermind(Packet.Build(MsgType.Result, w =>
                {
                    w.Write(ok);
                    w.Write(message);
                }));
        }

        // ---- Mastermind requests ---------------------------------------------------------------

        /// <summary>
        /// Plan the current wave (it sails when its timer runs out), or send it now. An empty plan clears it:
        /// a random wave sails instead.
        /// </summary>
        /// <summary>The whole plan for the current wave: per beach (index into BeachChoices), the paid Vikings per type.</summary>
        public static void RequestWavePlan(List<KeyValuePair<int, List<KeyValuePair<VikingAgent.Type, int>>>> landings)
        {
            if (!(Roles.IsMastermind && Roles.HasMastermind))
            {
                SetResult("Can't: multiplayer is not ready");
                return;
            }
            var beaches = BeachChoices.Get(GameAccess.CurrentIsland);
            if (!HasFreshState || State.done || landings == null || landings.Count > EconomyConfig.MaxLandingsPerWave)
            { SetResult("Can't: army controls aren't ready"); return; }
            ToLeader(Packet.Build(MsgType.WavePlan, w =>
            {
                var valid = landings.FindAll(l => l.Key >= 0 && l.Key < beaches.Count && l.Value.Count <= MastermindUpgrades.TypeCount);
                w.Write((byte)valid.Count);
                foreach (var landing in valid)
                {
                    w.Write(beaches[landing.Key].tileIndex);
                    w.Write((byte)landing.Value.Count);
                    foreach (var unit in landing.Value)
                    {
                        w.Write((int)unit.Key);
                        w.Write(unit.Value);
                    }
                }
            }));
        }

        public static void RequestCommanderBuy(VikingAgent.Type type)
        {
            if (!Roles.IsMastermind || !HasFreshState || State.upgrades == null || !State.upgrades.canBuy || UpgradePending)
            { SetResult("Can't: recruiting isn't ready"); return; }
            upgradePending = true;
            pendingUpgradeRevision = State.upgrades.revision;
            upgradeSentAt = Time.unscaledTime;
            SetResult("Recruiting...");
            ToLeader(Packet.Build(MsgType.CommanderBuy, w =>
            {
                w.Write(State.upgrades.session); w.Write(State.upgrades.nextCommanderId); w.Write((byte)type);
            }));
        }

        public static void RequestCommanderSell(int id)
        {
            if (!Roles.IsMastermind || !HasFreshState || State.upgrades == null || !State.upgrades.canBuy || UpgradePending)
            { SetResult("Can't: selling isn't ready"); return; }
            upgradePending = true;
            pendingUpgradeRevision = State.upgrades.revision;
            upgradeSentAt = Time.unscaledTime;
            SetResult("Selling...");
            ToLeader(Packet.Build(MsgType.CommanderSell, w => { w.Write(State.upgrades.session); w.Write(id); }));
        }

        public static void RequestCommanderDeploy(int id, int beachIndex)
        {
            var beaches = BeachChoices.Get(GameAccess.CurrentIsland);
            if (!Roles.IsMastermind || !HasFreshState || !State.inBattle || beachIndex < 0 || beachIndex >= beaches.Count)
            { SetResult("Can't: pick a landing tile first"); return; }
            SetResult("Commander setting sail...");
            ToLeader(Packet.Build(MsgType.CommanderDeploy, w => { w.Write(id); w.Write(beaches[beachIndex].tileIndex); }));
        }

        public static void RequestCommanderOrder(int id, byte kind, int target)
        {
            if (!Roles.IsMastermind || !HasFreshState) return;
            ToLeader(Packet.Build(MsgType.CommanderOrder, w => { w.Write(id); w.Write(kind); w.Write(target); }));
        }

        public static void RequestGiveUp()
        {
            if (Roles.IsMastermind)
                ToLeader(Packet.Build(MsgType.GiveUp));
        }

        public static void RequestUpgrade(VikingAgent.Type type, UnitUpgrade kind)
        {
            if (!(Roles.IsMastermind && Roles.HasMastermind) || !HasFreshState || UpgradePending ||
                State.upgrades == null || !State.upgrades.canBuy || !MastermindUpgrades.ValidType(type) || !MastermindUpgrades.ValidKind(kind))
            { SetResult("Can't: upgrade controls aren't ready"); return; }
            int level = State.upgrades.Level(type, kind);
            int cost = MastermindUpgrades.Cost(State.upgrades.unitCosts[(int)type], kind, level);
            if (cost <= 0 || State.upgrades.gold + 0.001f < cost)
            { SetResult("Can't: upgrade is maxed, locked, or unaffordable"); return; }
            upgradePending = true;
            pendingUpgradeRevision = State.upgrades.revision;
            upgradeSentAt = Time.unscaledTime;
            SetResult("Buying upgrade...");
            ToLeader(Packet.Build(BuyUpgradeMessage, w =>
            {
                w.Write(State.upgrades.session);
                w.Write(State.upgrades.revision);
                w.Write(State.upgrades.islandIndex);
                w.Write((byte)type);
                w.Write((byte)kind);
            }));
        }

        private static void SetResult(string text)
        {
            LastResult = text;
            LastResultTime = Time.unscaledTime;
        }

        // ---- Defender handling -----------------------------------------------------------------

        /// <summary>
        /// The mastermind's plan for the current wave: byte landings, then per landing int tile, byte count and
        /// (int vikingType, int paid) pairs. It sails when the wave's timer runs out; there's no sending early.
        /// </summary>
        private static void HandleWavePlan(CSteamID sender, BinaryReader r)
        {
            int landingCount = r.ReadByte();
            if (landingCount > EconomyConfig.MaxLandingsPerWave)
            { SendResult(false, $"a wave can land on at most {EconomyConfig.MaxLandingsPerWave} beaches"); return; }
            var requestedLandings = new List<KeyValuePair<int, List<KeyValuePair<int, int>>>>();
            for (int l = 0; l < landingCount; l++)
            {
                if (r.BaseStream.Length - r.BaseStream.Position < 5) { SendResult(false, "invalid wave plan"); return; }
                int landingTile = r.ReadInt32();
                int count = r.ReadByte();
                if (count > MastermindUpgrades.TypeCount || r.BaseStream.Length - r.BaseStream.Position < count * 8)
                { SendResult(false, "invalid wave plan"); return; }
                var units = new List<KeyValuePair<int, int>>();
                for (int i = 0; i < count; i++)
                    units.Add(new KeyValuePair<int, int>(r.ReadInt32(), r.ReadInt32()));
                requestedLandings.Add(new KeyValuePair<int, List<KeyValuePair<int, int>>>(landingTile, units));
            }
            if (r.BaseStream.Position != r.BaseStream.Length) { SendResult(false, "invalid wave plan"); return; }

            Island island = GameAccess.CurrentIsland;
            if (!Economy.Active || !island || island.state != Island.State.Playing)
            { SendResult(false, "the battle hasn't started"); return; }
            if (Economy.Done)
            { SendResult(false, "no more waves sail on this island"); return; }
            List<VikingReference> available = Economy.AvailableTypes(island);
            List<BeachChoice> beaches = BeachChoices.Get(island);
            var landings = new List<Economy.PlanLanding>();
            var tiles = new HashSet<int>();
            float cost = 0f;
            foreach (var requestedLanding in requestedLandings)
            {
                int tile = requestedLanding.Key;
                if (beaches.FindIndex(c => c.tileIndex == tile) < 0 || !tiles.Add(tile))
                { SendResult(false, "unknown or repeated beach"); return; }
                var landing = new Economy.PlanLanding { tile = tile };
                var chosenTypes = new HashSet<int>();
                foreach (var req in requestedLanding.Value)
                {
                    VikingReference type = available.Find(v => (int)v.type == req.Key);
                    if (!type || req.Value < 0 || req.Value > 99 || !chosenTypes.Add(req.Key))
                    { SendResult(false, "that unit isn't available here"); return; }
                    if (req.Value == 0) continue;
                    landing.units.Add(new KeyValuePair<VikingReference, int>(type, req.Value));
                    cost += Economy.UnitCost(type) * req.Value;
                }
                if (landing.units.Count > 0) landings.Add(landing);
            }
            if (cost > Economy.WaveBudget + 0.001f)
            { SendResult(false, $"that wave costs {cost:0} gold; this wave's budget is {Economy.WaveBudget:0}"); return; }
            Economy.SetPlan(landings);
            SendState(reliable: true);
        }
    }
}
