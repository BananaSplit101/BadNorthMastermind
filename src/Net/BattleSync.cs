using System;
using System.Collections.Generic;
using System.IO;
using BadNorthMP.Spectator;
using HarmonyLib;
using Steamworks;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthMP.Net
{
    /// <summary>
    /// Defender side of a battle: a stream of messages that is recorded to disk (for solo replays with F11)
    /// and sent to the mastermind. A mastermind who arrives mid-battle gets the battle info plus every
    /// reliable message so far, so their puppet world catches up.
    /// </summary>
    internal static class BattleStream
    {
        public const int FileMagic = 0x504D4E42; // "BNMP"
        public const int FileVersion = 5;

        public static bool Active { get; private set; }

        /// <summary>Seconds of real time since the battle started; snapshots are stamped with it.</summary>
        public static float StreamTime => Time.unscaledTime - startTime;

        private static byte[] battleInfo;
        private static readonly List<byte[]> reliableHistory = new List<byte[]>();
        private static CSteamID sentTo = CSteamID.Nil;
        private static BinaryWriter recording;
        private static float startTime;

        public static void Begin(BattleInfo info)
        {
            End();
            battleInfo = info.ToBytes();
            reliableHistory.Clear();
            sentTo = CSteamID.Nil;
            startTime = Time.unscaledTime;
            Active = true;
            try
            {
                recording = new BinaryWriter(File.Create(SpectatorSession.LastBattlePath));
                recording.Write(FileMagic);
                recording.Write(FileVersion);
                recording.Write(battleInfo.Length);
                recording.Write(battleInfo);
            }
            catch (Exception e)
            {
                ModLog.Warn($"Not recording this battle: {e.Message}");
                recording = null;
            }
            DefenderTracker.Reset();
            ModLog.Info($"Battle stream started ({battleInfo.Length / 1024} KB of battle info)");
        }

        public static void Send(byte[] packet, bool reliable)
        {
            if (!Active)
                return;
            if (recording != null)
            {
                recording.Write(Time.unscaledTime - startTime);
                recording.Write(packet.Length);
                recording.Write(packet);
            }
            if (reliable)
                reliableHistory.Add(packet);
            if (sentTo != CSteamID.Nil)
                SteamSession.Instance.Send(sentTo, packet, reliable);
        }

        /// <summary>Every frame: catch up a newly arrived (or newly chosen) mastermind.</summary>
        public static void Update()
        {
            if (!Active)
                return;
            CSteamID mastermind = Roles.IsDefender && Roles.HasMastermind ? Roles.MastermindId : CSteamID.Nil;
            if (mastermind == sentTo)
                return;
            sentTo = mastermind;
            if (mastermind == CSteamID.Nil)
                return;
            Chunker.Send(mastermind, battleInfo);
            foreach (byte[] packet in reliableHistory)
                SteamSession.Instance.Send(mastermind, packet, reliable: true);
            ModLog.Info($"Sent battle to {SteamSession.NameOf(mastermind)} ({battleInfo.Length / 1024} KB + {reliableHistory.Count} events)");
        }

        public static void End()
        {
            if (!Active)
                return;
            if (sentTo != CSteamID.Nil)
                SteamSession.Instance.Send(sentTo, Packet.Build(MsgType.BattleEnd));
            Active = false;
            sentTo = CSteamID.Nil;
            reliableHistory.Clear();
            if (recording != null)
            {
                recording.Close();
                recording = null;
            }
            ModLog.Info("Battle stream ended");
        }
    }

    /// <summary>Mastermind side: receive the battle info and hand battle messages to the puppet world.</summary>
    internal static class BattleSync
    {
        public static void Init()
        {
            Router.On(MsgType.BattleChunk, (sender, r) =>
            {
                byte[] payload = Chunker.Receive(r);
                if (payload == null || !Roles.IsMastermind || sender != Roles.DefenderId)
                    return;
                ModLog.Info($"Received battle from {SteamSession.NameOf(sender)} ({payload.Length / 1024} KB)");
                SpectatorSession.Instance.Watch(BattleInfo.FromBytes(payload), null);
            });
            Router.On(MsgType.BattleEnd, (sender, r) =>
            {
                if (sender == Roles.DefenderId)
                    SpectatorSession.Instance.Stop();
            });
            Router.BattleMessage = (sender, packet) =>
            {
                if (Roles.IsMastermind && sender == Roles.DefenderId)
                    PuppetWorld.Receive(packet);
            };
        }
    }

    [HarmonyPatch(typeof(IslandGameplayManager), nameof(IslandGameplayManager.EnterIsland))]
    internal static class DefenderEnterIslandPatch
    {
        private static void Postfix(Island island)
        {
            if (SpectatorSession.Active)
                return;
            try
            {
                BattleStream.Begin(BattleInfo.Capture(island.levelNode.index));
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
