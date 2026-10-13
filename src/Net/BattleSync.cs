using System;
using System.Collections.Generic;
using BadNorthMP.Spectator;
using HarmonyLib;
using Steamworks;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthMP.Net
{
    /// <summary>
    /// The campaign leader's side of a battle: a stream of messages sent to everyone watching it. A player who
    /// arrives mid-battle gets the battle info plus every reliable message so far, so their copy catches up.
    /// </summary>
    internal static class BattleStream
    {
        public static bool Active { get; private set; }

        /// <summary>Seconds of game time since the battle started; snapshots are stamped with it.</summary>
        public static float StreamTime => Time.time - startTime;

        private static byte[] battleInfo;
        private static readonly List<byte[]> reliableHistory = new List<byte[]>();
        private static readonly HashSet<CSteamID> sentTo = new HashSet<CSteamID>();
        private static float startTime;

        public static void Begin(BattleInfo info)
        {
            End();
            battleInfo = info.ToBytes();
            reliableHistory.Clear();
            sentTo.Clear();
            startTime = Time.time;
            Active = true;
            DefenderTracker.Reset();
            ModLog.Info($"Battle stream started ({battleInfo.Length / 1024} KB of battle info)");
        }

        public static void Send(byte[] packet, bool reliable)
        {
            if (!Active)
                return;
            if (reliable)
                reliableHistory.Add(packet);
            foreach (CSteamID recipient in sentTo)
                SteamSession.Instance.Send(recipient, packet, reliable);
        }

        /// <summary>Every frame: send the battle so far to each member who doesn't have it yet.</summary>
        public static void Update()
        {
            if (!Active || !Roles.IsDefender || !Roles.HasMastermind)
                return;
            var members = SteamSession.Instance.Members();
            sentTo.RemoveWhere(member => !members.Contains(member));
            foreach (CSteamID recipient in members)
            {
                if (recipient == SteamSession.Instance.Me || !sentTo.Add(recipient)) continue;
                Chunker.Send(recipient, battleInfo);
                foreach (byte[] packet in reliableHistory)
                    SteamSession.Instance.Send(recipient, packet, reliable: true);
                ModLog.Info($"Sent shared battle to {SteamSession.NameOf(recipient)}");
            }
        }

        /// <summary>A watcher's load of this battle got stuck: send it the battle again, with everything so far (Update).</summary>
        public static void Resend(CSteamID member)
        {
            if (Active && sentTo.Remove(member)) ModLog.Info($"{SteamSession.NameOf(member)} asked for the battle again");
        }

        public static void End()
        {
            if (!Active)
                return;
            foreach (CSteamID recipient in sentTo)
                SteamSession.Instance.Send(recipient, Packet.Build(MsgType.BattleEnd));
            Active = false;
            sentTo.Clear();
            reliableHistory.Clear();
            ModLog.Info("Battle stream ended");
        }
    }

    /// <summary>Watcher side: receive the battle info and hand battle messages to the puppet world.</summary>
    internal static class BattleSync
    {
        public static void Init()
        {
            Router.On(MsgType.BattleChunk, (sender, r) =>
            {
                if (!Roles.WatchesBattle || sender != Roles.DefenderId) return;
                byte[] payload = Chunker.Receive(r);
                if (payload == null)
                    return;
                ModLog.Info($"Received battle from {SteamSession.NameOf(sender)} ({payload.Length / 1024} KB)");
                SpectatorSession.Instance.Watch(BattleInfo.FromBytes(payload));
            });
            Router.On(MsgType.BattleRequest, (sender, r) =>
            {
                if (Roles.IsDefender && SteamSession.Instance.Members().Contains(sender)) BattleStream.Resend(sender);
            });
            Router.On(MsgType.BattleEnd, (sender, r) =>
            {
                if (Roles.WatchesBattle && sender == Roles.DefenderId)
                    SpectatorSession.Instance.BattleEnded();
            });
            Router.BattleMessage = (sender, packet) =>
            {
                if (Roles.WatchesBattle && sender == Roles.DefenderId)
                    PuppetWorld.Receive(packet);
            };
        }
    }

    // Both initial entry and ReplayIslandRoutine run this before spawning the new raid.
    [HarmonyPatch(typeof(IslandGameplayManager), "PlayIslandRoutine")]
    internal static class DefenderEnterIslandPatch
    {
        private static void Prefix(IslandGameplayManager __instance)
        {
            // Only the campaign leader of a lobby shares its battles.
            if (SpectatorSession.Active || !Roles.IsDefender)
                return;
            try
            {
                Island island = __instance.island;
                BattleStream.Begin(BattleInfo.Capture(island.levelNode.index));
                // Everyone now loads the island; it starts once they all have (LoadGatePatch).
                LoadGate.Begin(island.levelNode.index);
            }
            catch (Exception e)
            {
                ModLog.Error($"Couldn't capture the battle: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(Island), nameof(Island.LeaveIsland))]
    internal static class DefenderLeaveIslandPatch
    {
        private static void Prefix()
        {
            if (!SpectatorSession.Active)
                BattleStream.End();
        }
    }
}
