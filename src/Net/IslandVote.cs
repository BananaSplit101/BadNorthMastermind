using System;
using System.Collections.Generic;
using BadNorthMP.Spectator;
using BadNorthMP.UI;
using HarmonyLib;
using Steamworks;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.CampaignGeneration;
using Voxels.TowerDefense.UI;
using Stack = Voxels.TowerDefense.Stack;

namespace BadNorthMP.Net
{
    /// <summary>
    /// The defenders vote on an island or Next Turn, and the leader counts. Once everyone has voted, or Countdown
    /// seconds after the first vote, a LaunchCountdown runs and then the most votes win (ties at random). Taking a
    /// vote back stops the launch and gives a fresh Countdown. No voting while someone is still loading the map (LoadGate).
    /// </summary>
    internal static class IslandVote
    {
        public const int NextTurn = -2, None = -1;
        public const float Countdown = 20f; // seconds
        public const float LaunchCountdown = 5f; // seconds

        // The latest vote state (the leader's own, or as received from the leader).
        public static readonly Dictionary<ulong, int> Votes = new Dictionary<ulong, int>();
        public static bool NextTurnAllowed;
        private static float deadline = -1f;
        // When the result is carried out (the "Launching game" countdown), or -1.
        private static float launchAt = -1f;

        // Leader only.
        private static bool changed;
        private static float nextBroadcast, nextValidation;
        private static readonly System.Random random = new System.Random();

        private static SteamSession Session => SteamSession.Instance;

        public static void Init()
        {
            Router.On(MsgType.VoteCast, (sender, r) =>
            {
                int token = r.ReadInt32(), seed = r.ReadInt32(), option = r.ReadInt32();
                if (!Roles.IsDefender || !PartyFlow.Deployed || Profile.campaign == null || token != PartyFlow.State.token ||
                    seed != Profile.campaign.seed || sender == Roles.MastermindId || PartyFlow.State.Find(sender.m_SteamID) == null ||
                    !Session.Members().Contains(sender)) return;
                Record(sender.m_SteamID, option);
            });
            Router.On(MsgType.VoteState, (sender, r) =>
            {
                if (sender != Roles.DefenderId || Roles.IsDefender) return;
                int token = r.ReadInt32(), seed = r.ReadInt32();
                float secondsLeft = r.ReadSingle(), launchLeft = r.ReadSingle();
                bool nextTurn = r.ReadBoolean();
                int count = r.ReadByte();
                var votes = new Dictionary<ulong, int>();
                for (int i = 0; i < count; i++) votes[r.ReadUInt64()] = r.ReadInt32();
                int decided = r.ReadInt32();
                if (!PartyFlow.Deployed || token != PartyFlow.State.token || Profile.campaign == null || seed != Profile.campaign.seed) return;
                Votes.Clear();
                foreach (var vote in votes) Votes[vote.Key] = vote.Value;
                NextTurnAllowed = nextTurn;
                deadline = secondsLeft >= 0f ? Time.unscaledTime + secondsLeft : -1f;
                launchAt = launchLeft >= 0f ? Time.unscaledTime + launchLeft : -1f;
                if (decided != None) ModLog.Info("Party vote decided: " + Describe(decided));
            });
        }

        /// <summary>This player is a seated defender in a deployed party.</summary>
        public static bool CanVote => PartyFlow.Deployed && !Roles.IsMastermind && PartyFlow.Mine != null;

        /// <summary>Seconds until the vote is decided, or -1 when no countdown is running.</summary>
        public static float SecondsLeft => deadline < 0f ? -1f : Mathf.Max(0f, deadline - Time.unscaledTime);

        /// <summary>Seconds until the chosen option launches, or -1 when the launch countdown isn't running.</summary>
        public static float LaunchSecondsLeft => launchAt < 0f ? -1f : Mathf.Max(0f, launchAt - Time.unscaledTime);

        public static bool HasVoted => Session && Votes.ContainsKey(Session.Me.m_SteamID);

        /// <summary>The connected defenders, in seat order (the leader first).</summary>
        public static List<ulong> Voters()
        {
            var result = new List<ulong>();
            if (!PartyFlow.Deployed) return result;
            foreach (var seat in PartyFlow.State.seats)
                if (PartyFlow.IsMember(seat.player)) result.Add(seat.player);
            return result;
        }

        /// <summary>Vote for an island (its level index) or NextTurn; voting again changes the vote; None takes it back.</summary>
        public static void Cast(int option)
        {
            if (!CanVote || Profile.campaign == null) return;
            if (option != None && LoadGate.MapWaiting)
            {
                FabricWrapper.PostEvent("UI/InGame/Error");
                GameNotify.Post("Wait until everyone has loaded the map", 2.5f);
                return;
            }
            ModLog.Info(option == None ? "Party vote withdrawn" : "Party vote: " + Describe(option));
            FabricWrapper.PostEvent(option == None ? "UI/InGame/UnitDeselect" : "UI/InGame/UnitSelect");
            if (Roles.IsDefender) { Record(Session.Me.m_SteamID, option); return; }
            Session.Send(Roles.DefenderId, Packet.Build(MsgType.VoteCast, w =>
            {
                w.Write(PartyFlow.State.token); w.Write(Profile.campaign.seed); w.Write(option);
            }));
            // Show it at once; the leader's next state confirms or corrects it.
            if (option == None) Votes.Remove(Session.Me.m_SteamID);
            else Votes[Session.Me.m_SteamID] = option;
        }

        public static string Describe(int option)
        {
            if (option == NextTurn) return "Next Turn";
            LevelNode node = Level(option);
            return node ? node.levelState.dbgName : "island " + option;
        }

        public static LevelNode Level(int index)
        {
            var manager = Singleton<CampaignManager>.instance;
            Campaign campaign = manager ? manager.campaign : null;
            return campaign && index >= 0 && index < campaign.levels.Count ? campaign.levels[index] : null;
        }

        /// <summary>The native click rule (LevelNodeUIProxy.HandleClick), without the checkpoint reload.</summary>
        public static bool IslandVotable(LevelNode node)
        {
            var manager = Singleton<CampaignManager>.instance;
            Campaign campaign = manager ? manager.campaign : null;
            return node && campaign && node.IsAvailable() && node.levelState.checkpointState != LevelState.CheckpointState.Current &&
                !campaign.trialOver && !campaign.campaignSave.gameOver;
        }

        // ---- Leader ---------------------------------------------------------------------------------

        private static CampaignMapUI mapUI;
        private static float nextMapSearch;
        private static CampaignMapUI MapUI()
        {
            if (!mapUI && Time.unscaledTime >= nextMapSearch)
            {
                nextMapSearch = Time.unscaledTime + 1f;
                mapUI = UnityEngine.Object.FindObjectOfType<CampaignMapUI>();
            }
            return mapUI;
        }

        private static bool LeaderNextTurnAllowed()
        {
            CampaignMapUI map = MapUI();
            return map && map.isActiveAndEnabled && Traverse.Create(map).Method("ShouldEnableNextTurnButton").GetValue<bool>();
        }

        /// <summary>The leader is on the native campaign map (not in a battle or loading).</summary>
        internal static bool LeaderOnMap()
        {
            var manager = Singleton<CampaignManager>.instance;
            var stack = Singleton<Stack>.instance;
            return manager && manager.campaign && Profile.campaign != null && !SpectatorSession.Active &&
                stack && stack.stateCampaign.gameObject.activeInHierarchy && !stack.stateLoading.gameObject.activeInHierarchy &&
                !GameAccess.CurrentIsland;
        }

        /// <summary>On the map with nothing animating: the result can be carried out now.</summary>
        private static bool LeaderIdle()
        {
            CampaignMapUI map = MapUI();
            return LeaderOnMap() && map && map.isActiveAndEnabled && !map.blockingInput;
        }

        private static bool Valid(int option) => option == NextTurn ? LeaderNextTurnAllowed() : IslandVotable(Level(option));

        private static void Record(ulong player, int option)
        {
            if (option == None)
            {
                if (!Votes.Remove(player)) return;
                // A change of mind: whoever is left gets a fresh countdown (and any launch countdown stops).
                deadline = Votes.Count > 0 ? Mathf.Max(deadline, Time.unscaledTime + Countdown) : -1f;
                changed = true;
                ModLog.Info("Party vote withdrawn by " + SteamSession.NameOf(new CSteamID(player)));
                return;
            }
            if (!LeaderOnMap() || !Valid(option) || LoadGate.MapHolding)
            {
                ModLog.Info("Party vote refused from " + SteamSession.NameOf(new CSteamID(player)) + ": " + Describe(option));
                changed = true; // resend the real state so the voter's own display is corrected
                return;
            }
            if (Votes.TryGetValue(player, out int previous) && previous == option) return;
            Votes[player] = option;
            if (deadline < 0f) deadline = Time.unscaledTime + Countdown;
            changed = true;
            ModLog.Info("Party vote from " + SteamSession.NameOf(new CSteamID(player)) + ": " + Describe(option));
        }

        /// <summary>Every frame (MultiplayerGame.Update).</summary>
        public static void Update()
        {
            if (!Roles.IsDefender || !PartyFlow.Deployed || Profile.campaign == null)
            {
                if (Roles.IsDefender && (Votes.Count > 0 || deadline >= 0f)) Clear();
                return;
            }
            if (LeaderOnMap()) PartyFlow.SeatLateJoiners();
            // Drop votes from players who left, and (while the map is up) votes the map no longer allows.
            List<ulong> voters = Voters();
            bool validate = LeaderIdle() && Time.unscaledTime >= nextValidation;
            if (validate) nextValidation = Time.unscaledTime + 0.5f;
            if (VoteTally.Prune(Votes, voters, validate ? (Func<int, bool>)Valid : null)) changed = true;
            if (Votes.Count == 0 && deadline >= 0f) { deadline = -1f; changed = true; }
            bool nextTurn = validate ? LeaderNextTurnAllowed() : NextTurnAllowed;
            if (nextTurn != NextTurnAllowed) { NextTurnAllowed = nextTurn; changed = true; }

            // Someone is still loading the map: no countdown runs; it starts afresh once everyone is there.
            if (LoadGate.MapHolding)
            {
                if (deadline >= 0f || launchAt >= 0f)
                {
                    deadline = launchAt = -1f;
                    changed = true;
                    ModLog.Info("Party vote: waiting for everyone to load the map");
                }
                if (changed || Time.unscaledTime >= nextBroadcast) Broadcast(None);
                return;
            }
            if (Votes.Count > 0 && deadline < 0f) { deadline = Time.unscaledTime + Countdown; changed = true; }

            // Everyone voted (or time ran out): "Launching game" for a few seconds, then carry it out.
            bool ready = VoteTally.Ready(Votes, voters, Time.unscaledTime, deadline);
            if (ready && launchAt < 0f)
            {
                launchAt = Time.unscaledTime + LaunchCountdown;
                changed = true;
                ModLog.Info("Party vote: launching " + Describe(VoteTally.Leaders(Votes)[0]) + " in " + LaunchCountdown + " s");
            }
            else if (!ready && launchAt >= 0f)
            {
                launchAt = -1f;
                changed = true;
                ModLog.Info("Party vote: launch stopped");
            }
            if (launchAt >= 0f && Time.unscaledTime >= launchAt && LeaderIdle())
            {
                Resolve();
                return;
            }
            if (changed || Time.unscaledTime >= nextBroadcast) Broadcast(None);
        }

        private static void Resolve()
        {
            List<int> tied = VoteTally.Leaders(Votes);
            int winner = VoteTally.Winner(Votes, random);
            ModLog.Info("Party vote result: " + Describe(winner) + " with " + VoteTally.Count(Votes, winner) + " of " + Votes.Count + " votes" +
                (tied.Count > 1 ? " (tie broken at random)" : ""));
            Votes.Clear();
            deadline = launchAt = -1f;
            Broadcast(winner);
            if (!Valid(winner)) { GameNotify.Post(Describe(winner) + " can't be chosen any more. Vote again."); return; }
            GameNotify.Post("The party chose " + Describe(winner) + ".", 3f);
            if (winner == NextTurn) TurnSync.Play(MapUI()); // everyone plays it on their own map: no reload
            else Play(Level(winner));
        }

        /// <summary>LevelNodeUIProxy.PlayLevel, which a click on the island runs natively.</summary>
        private static void Play(LevelNode node)
        {
            IslandProxy islandProxy = node.islandProxy;
            islandProxy.preparingToPlay = true;
            islandProxy.UpdateBuildPriority();
            if (islandProxy.state == IslandProxy.State.None)
                islandProxy.GenerateIsland();
            node.Play();
            FabricWrapper.PostEvent("UI/InGame/Resume");
        }

        private static void Broadcast(int decided)
        {
            changed = false;
            nextBroadcast = Time.unscaledTime + (deadline >= 0f || launchAt >= 0f ? 1f : 2f);
            float secondsLeft = SecondsLeft, launchLeft = LaunchSecondsLeft;
            var votes = new List<KeyValuePair<ulong, int>>(Votes);
            bool nextTurn = NextTurnAllowed;
            Session.Broadcast(Packet.Build(MsgType.VoteState, w =>
            {
                w.Write(PartyFlow.State.token); w.Write(Profile.campaign.seed); w.Write(secondsLeft); w.Write(launchLeft); w.Write(nextTurn);
                w.Write((byte)votes.Count);
                foreach (var vote in votes) { w.Write(vote.Key); w.Write(vote.Value); }
                w.Write(decided);
            }));
        }

        private static void Clear()
        {
            Votes.Clear();
            deadline = launchAt = -1f;
            if (PartyFlow.Active && Profile.campaign != null) Broadcast(None);
        }
    }
}
