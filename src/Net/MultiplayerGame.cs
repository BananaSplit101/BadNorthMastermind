using System.Collections.Generic;
using System.IO;
using Steamworks;
using UnityEngine;
using BadNorthMP.UI;
using Voxels.TowerDefense;

namespace BadNorthMP.Net
{
    /// <summary>
    /// Lobby-level game flow: pings, the defender's battle status, wave requests and the event log.
    /// The defender's game runs the battle; the mastermind decides when (and later what) Vikings attack.
    /// </summary>
    internal class MultiplayerGame : MonoBehaviour
    {
        private const float PingInterval = 2f;
        private const float StatusInterval = 0.5f;
        private const int MaxEvents = 8;

        public static MultiplayerGame Instance { get; private set; }

        /// <summary>Defender option: while a mastermind is connected, waves only launch when they say so.</summary>
        public bool mastermindControlEnabled = true;

        /// <summary>Read by the Raid.MaybeLaunchWaves patch.</summary>
        public static bool MastermindControlsWaves =>
            Instance && Instance.mastermindControlEnabled && Roles.IsDefender && Roles.HasMastermind;

        /// <summary>Latest battle snapshot from the defender (mastermind only).</summary>
        public IslandStatus DefenderStatus { get; private set; }
        public bool HasDefenderStatus { get; private set; }

        public IEnumerable<string> RecentEvents(int count)
        {
            var all = new List<string>(events);
            return all.GetRange(Mathf.Max(0, all.Count - count), Mathf.Min(count, all.Count));
        }

        private static SteamSession Session => SteamSession.Instance;

        private readonly Dictionary<ulong, float> pingMs = new Dictionary<ulong, float>();
        private readonly List<CSteamID> knownMembers = new List<CSteamID>();
        private readonly Queue<string> events = new Queue<string>();
        private CSteamID knownOwner = CSteamID.Nil;
        private CSteamID lastMastermind = CSteamID.Nil;
        private float nextPing;
        private float nextStatus;

        private void Awake() => Instance = this;

        private void Start()
        {
            Session.MessageReceived += Router.Dispatch;
            Session.LobbyChanged += OnLobbyChanged;
            Session.LobbyDataChanged += OnRolesMaybeChanged;

            Router.On(MsgType.Ping, (sender, r) =>
            {
                float sentAt = r.ReadSingle();
                Session.Send(sender, Packet.Build(MsgType.Pong, w => w.Write(sentAt)), reliable: false);
            });
            Router.On(MsgType.Pong, (sender, r) =>
                pingMs[sender.m_SteamID] = (Time.realtimeSinceStartup - r.ReadSingle()) * 1000f);
            Router.On(MsgType.Chat, (sender, r) => AddEvent($"{SteamSession.NameOf(sender)}: {r.ReadString()}"));
            Router.On(MsgType.Status, (sender, r) =>
            {
                if (sender != Roles.DefenderId)
                    return;
                DefenderStatus = IslandStatus.Read(r);
                HasDefenderStatus = true;
            });
            Router.On(MsgType.WaveRequest, (sender, r) =>
            {
                if (Roles.IsDefender && sender == Roles.MastermindId)
                    HandleWaveRequest(sender);
            });
            BattleSync.Init();
            MastermindNet.Init();
            Router.On(MsgType.Notice, (sender, r) =>
            {
                if (sender == Roles.DefenderId)
                    AddEvent(r.ReadString());
            });
        }

        private void Update()
        {
            // Recording runs even without a lobby, so the last battle can be replayed solo (F11).
            BattleStream.Update();
            DefenderTracker.Update();
            if (!Session.InLobby)
                return;
            MastermindNet.Update();

            float now = Time.realtimeSinceStartup;
            if (now >= nextPing)
            {
                nextPing = now + PingInterval;
                Session.Broadcast(Packet.Build(MsgType.Ping, w => w.Write(now)), reliable: false);
            }
            if (Roles.IsDefender && Roles.HasMastermind && now >= nextStatus)
            {
                nextStatus = now + StatusInterval;
                IslandStatus status = BuildStatus();
                Session.Send(Roles.MastermindId, Packet.Build(MsgType.Status, status.Write), reliable: false);
            }
        }

        public string PingText(CSteamID user) =>
            pingMs.TryGetValue(user.m_SteamID, out float ms) ? $"{ms:0} ms" : "...";

        // ---- Actions from the UI -------------------------------------------------------------

        public void RequestWave()
        {
            if (Roles.IsMastermind)
                Session.Send(Roles.DefenderId, Packet.Build(MsgType.WaveRequest));
        }

        public void SendChat(string text)
        {
            text = text.Trim();
            if (text.Length == 0)
                return;
            if (text.Length > 200)
                text = text.Substring(0, 200);
            Session.Broadcast(Packet.Build(MsgType.Chat, w => w.Write(text)));
            AddEvent($"{SteamFriends.GetPersonaName()}: {text}");
        }

        // ---- Lobby events --------------------------------------------------------------------

        private void OnLobbyChanged()
        {
            if (!Session.InLobby)
            {
                knownOwner = CSteamID.Nil;
                HasDefenderStatus = false;
                pingMs.Clear();
                knownMembers.Clear();
                lastMastermind = CSteamID.Nil;
                Game.MastermindSession.Reset();
                if (Spectator.SpectatorSession.Active)
                    Spectator.SpectatorSession.Instance.Stop();
                return;
            }

            CSteamID owner = Session.Owner;
            if (knownOwner == CSteamID.Nil)
            {
                knownOwner = owner;
            }
            else if (owner != knownOwner && !Session.IsHost)
            {
                // Host migration isn't supported: lobby settings live with the original owner.
                Session.Leave("The host left the game");
                AddEvent("The host left the game");
                return;
            }

            List<CSteamID> members = Session.Members();
            foreach (CSteamID member in members)
            {
                if (!knownMembers.Contains(member) && member != Session.Me)
                    Announce($"{SteamSession.NameOf(member)} joined");
            }
            foreach (CSteamID member in knownMembers)
            {
                if (!members.Contains(member))
                    Announce($"{SteamSession.NameOf(member)} left");
            }
            knownMembers.Clear();
            knownMembers.AddRange(members);

            Roles.OnMembersChanged();
            OnRolesMaybeChanged();
        }

        private void OnRolesMaybeChanged()
        {
            CSteamID mastermind = Roles.MastermindId;
            if (mastermind == lastMastermind || mastermind == CSteamID.Nil)
                return;
            lastMastermind = mastermind;
            AddEvent(Roles.IsMastermind
                ? "You are the mastermind."
                : $"You defend. {SteamSession.NameOf(Roles.MastermindId)} is the mastermind.");
        }

        // ---- Defender side -------------------------------------------------------------------

        private void HandleWaveRequest(CSteamID requester)
        {
            string name = SteamSession.NameOf(requester);
            bool ok = GameAccess.TryLaunchNextWave(out string result);
            string notice = ok ? $"{name} {result}!" : $"{name}'s wave request refused: {result}";
            ModLog.Info(notice);
            if (ok)
                Announce(notice);
            else
                AddEvent(notice);
            Session.Send(requester, Packet.Build(MsgType.Notice, w => w.Write(notice)));
        }

        private static IslandStatus BuildStatus()
        {
            var status = new IslandStatus { mastermindControlsWaves = MastermindControlsWaves };
            Island island = GameAccess.CurrentIsland;
            if (!island || island.state != Island.State.Playing || !island.raid || island.raid.waves == null)
                return status;

            Raid raid = island.raid;
            int index = GameAccess.RaidWaveIndex(raid);
            status.onIsland = true;
            status.waveCount = raid.waves.Count;
            status.nextWave = index + 1;
            status.waveLaunching = index < raid.waves.Count && raid.waves[index].triggered;
            status.squadsAlive = GameAccess.PlayerSquads.Count;
            status.vikingsAlive = island.vikings ? island.vikings.agents.Count : 0;
            status.timeToNextWave = raid.GetTimeToNextWave();
            return status;
        }

        // ---- Event log -----------------------------------------------------------------------

        /// <summary>Adds to the event list and, during a battle, shows the game's notification banner.</summary>
        public void Announce(string text)
        {
            AddEvent(text);
            GameNotify.Post(text);
        }

        public void AddEvent(string text)
        {
            events.Enqueue(text);
            while (events.Count > MaxEvents)
                events.Dequeue();
        }
    }
}
