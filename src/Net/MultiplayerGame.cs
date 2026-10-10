using System.Collections.Generic;
using Steamworks;
using UnityEngine;
using BadNorthMP.UI;
using Voxels.TowerDefense;

namespace BadNorthMP.Net
{
    /// <summary>
    /// Lobby-level flow: wires up the message handlers, pings everyone, keeps a short event log (joins, leaves,
    /// roles) and runs the per-frame updates of the multiplayer systems.
    /// </summary>
    internal class MultiplayerGame : MonoBehaviour
    {
        private const float PingInterval = 2f;
        private const int MaxEvents = 8;

        public static MultiplayerGame Instance { get; private set; }

        /// <summary>The campaign leader plays against a mastermind: the game's own wave timer never sends a wave.</summary>
        public static bool MastermindControlsWaves => Roles.IsDefender && Roles.HasMastermind;

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

        public static bool HostLoading { get; private set; }
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
            Router.On(MsgType.HostLoading, (sender, reader) =>
            {
                if (sender != Roles.MastermindId) return;
                HostLoading = reader.ReadBoolean();
                bool ready = reader.ReadBoolean();
                Announce(HostLoading ? "The host is loading the live battle..." : ready
                    ? "The host is ready and watching the battle." : "The host could not finish loading the battle.");
            });
            RemoteDefender.Init();
            PartyUpgrades.Init();
            IslandVote.Init();
            PartyFlow.Init();
            LoadGate.Init();
            TurnSync.Init();
            BattleSync.Init();
            MastermindNet.Init();
            DeployFlow.Init();
        }

        private void Update()
        {
            GameNotify.Update();
            BattleStream.Update();
            DefenderTracker.Update();
            PartyFlow.Update();
            IslandVote.Update();
            DeployFlow.Update();
            if (!Session.InLobby)
                return;
            MastermindNet.Update();
            TurnSync.Update();
            FriendLobbies.Publish();

            float now = Time.realtimeSinceStartup;
            if (now >= nextPing)
            {
                nextPing = now + PingInterval;
                Session.Broadcast(Packet.Build(MsgType.Ping, w => w.Write(now)), reliable: false);
            }
        }

        public string PingText(CSteamID user) =>
            pingMs.TryGetValue(user.m_SteamID, out float ms) ? $"{ms:0} ms" : "...";

        // ---- Lobby events --------------------------------------------------------------------

        private void OnLobbyChanged()
        {
            if (!Session.InLobby)
            {
                HostLoading = false;
                knownOwner = CSteamID.Nil;
                events.Clear();
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
