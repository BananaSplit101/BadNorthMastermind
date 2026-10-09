using System.Collections.Generic;
using System.IO;
using BadNorthMP.Game;
using BadNorthMP.UI;
using Steamworks;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthMP.Net
{
    /// <summary>The mastermind's view of their economy, sent by the defender.</summary>
    internal struct MasterState
    {
        public bool inBattle;
        public float gold;
        public float incomePerSecond;
        public float incomeLeft;
        public float cap;
        public int wavesSent;
        public int vikingsAlive;
        public bool done;

        public void Write(BinaryWriter w)
        {
            w.Write(inBattle);
            w.Write(gold);
            w.Write(incomePerSecond);
            w.Write(incomeLeft);
            w.Write(cap);
            w.Write(wavesSent);
            w.Write(vikingsAlive);
            w.Write(done);
        }

        public static MasterState Read(BinaryReader r) => new MasterState
        {
            inBattle = r.ReadBoolean(),
            gold = r.ReadSingle(),
            incomePerSecond = r.ReadSingle(),
            incomeLeft = r.ReadSingle(),
            cap = r.ReadSingle(),
            wavesSent = r.ReadInt32(),
            vikingsAlive = r.ReadInt32(),
            done = r.ReadBoolean(),
        };
    }

    /// <summary>
    /// Requests from the mastermind (build a wave, give up) handled by the authoritative defender, and the
    /// state/results the defender sends back.
    /// </summary>
    internal static class MastermindNet
    {
        private const float StateInterval = 0.25f;
        private static float nextState;

        // Mastermind side
        public static MasterState State { get; private set; }
        public static bool HasState { get; private set; }
        public static string LastResult { get; private set; } = "";
        public static float LastResultTime { get; private set; } = -100f;

        private static SteamSession Session => SteamSession.Instance;

        public static void Init()
        {
            Router.On(MsgType.BuildWave, (sender, r) =>
            {
                if (Roles.IsDefender && sender == Roles.MastermindId)
                    HandleBuildWave(sender, r);
            });
            Router.On(MsgType.GiveUp, (sender, r) =>
            {
                if (!Roles.IsDefender || sender != Roles.MastermindId || !Economy.Active)
                    return;
                Economy.GiveUp();
                MultiplayerGame.Instance.Announce("The mastermind gave up! Defeat the remaining Vikings.");
                SendResult(true, "You gave up this island.");
            });
            Router.On(MsgType.MasterState, (sender, r) =>
            {
                if (sender != Roles.DefenderId)
                    return;
                State = MasterState.Read(r);
                HasState = true;
            });
            Router.On(MsgType.Result, (sender, r) =>
            {
                if (sender != Roles.DefenderId)
                    return;
                bool ok = r.ReadBoolean();
                SetResult((ok ? "" : "Can't: ") + r.ReadString());
            });
        }

        /// <summary>Defender, every frame: income, and the state the mastermind's panel shows.</summary>
        public static void Update()
        {
            if (!Roles.IsDefender)
                return;
            Economy.Tick(Time.deltaTime);
            if (!Roles.HasMastermind || Time.unscaledTime < nextState)
                return;
            nextState = Time.unscaledTime + StateInterval;
            Island island = GameAccess.CurrentIsland;
            var state = new MasterState
            {
                inBattle = Economy.Active,
                gold = Economy.Gold,
                incomePerSecond = Economy.IncomeDone ? 0f : Economy.IncomePerSecond,
                incomeLeft = Economy.IncomeLeft,
                cap = Economy.Cap,
                wavesSent = CustomWaves.WavesSent,
                vikingsAlive = island && island.vikings ? island.vikings.agents.Count : 0,
                done = Economy.Active && Economy.Done,
            };
            Session.Send(Roles.MastermindId, Packet.Build(MsgType.MasterState, state.Write), reliable: false);
        }

        public static void SendResult(bool ok, string message)
        {
            if (Roles.IsDefender && Roles.HasMastermind)
                Session.Send(Roles.MastermindId, Packet.Build(MsgType.Result, w =>
                {
                    w.Write(ok);
                    w.Write(message);
                }));
        }

        // ---- Mastermind requests ---------------------------------------------------------------

        public static void RequestWave(int beachIndex, List<KeyValuePair<VikingAgent.Type, int>> units)
        {
            if (!Roles.IsMastermind)
                return;
            Session.Send(Roles.DefenderId, Packet.Build(MsgType.BuildWave, w =>
            {
                w.Write(beachIndex);
                w.Write((byte)units.Count);
                foreach (var unit in units)
                {
                    w.Write((int)unit.Key);
                    w.Write(unit.Value);
                }
            }));
            SetResult("Sending wave...");
        }

        public static void RequestGiveUp()
        {
            if (Roles.IsMastermind)
                Session.Send(Roles.DefenderId, Packet.Build(MsgType.GiveUp));
        }

        private static void SetResult(string text)
        {
            LastResult = text;
            LastResultTime = Time.unscaledTime;
        }

        // ---- Defender handling -----------------------------------------------------------------

        private static void HandleBuildWave(CSteamID sender, BinaryReader r)
        {
            int beach = r.ReadInt32();
            int count = r.ReadByte();
            var requested = new List<KeyValuePair<int, int>>();
            for (int i = 0; i < count; i++)
                requested.Add(new KeyValuePair<int, int>(r.ReadInt32(), r.ReadInt32()));

            Island island = GameAccess.CurrentIsland;
            if (!Economy.Active || !island || island.state != Island.State.Playing)
            {
                SendResult(false, "the battle hasn't started");
                return;
            }

            List<VikingReference> available = Economy.AvailableTypes(island);
            var units = new List<KeyValuePair<VikingReference, int>>();
            float cost = 0f;
            foreach (var req in requested)
            {
                VikingReference type = available.Find(v => (int)v.type == req.Key);
                if (!type || req.Value <= 0 || req.Value > 99)
                {
                    SendResult(false, "that unit isn't available here");
                    return;
                }
                units.Add(new KeyValuePair<VikingReference, int>(type, req.Value));
                cost += Economy.UnitCost(type) * req.Value;
            }

            if (cost > Economy.Gold + 0.001f)
            {
                SendResult(false, $"needs {cost:0} gold, you have {Economy.Gold:0}");
                return;
            }
            if (!CustomWaves.TryLaunch(island, beach, units, out string error, out int unitCount))
            {
                SendResult(false, error);
                return;
            }
            Economy.TrySpend(cost);
            string beachName = BeachChoices.Get(island)[beach].label;
            MultiplayerGame.Instance.Announce($"The mastermind sends {unitCount} Vikings at the {beachName} beach!");
            SendResult(true, $"Wave sent: {unitCount} Vikings, {cost:0} gold");
        }
    }
}
