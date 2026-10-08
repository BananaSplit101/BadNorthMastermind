using System;
using System.IO;
using BadNorthMP.Spectator;
using HarmonyLib;
using Steamworks;
using Voxels.TowerDefense;

namespace BadNorthMP.Net
{
    /// <summary>
    /// Defender side: when a battle starts, send the mastermind what they need to load the same island.
    /// Mastermind side: receive it and hand it to the spectator session.
    /// </summary>
    internal static class BattleSync
    {
        /// <summary>The battle the defender is currently in (resent if a mastermind joins mid-battle).</summary>
        private static byte[] currentBattle;
        private static CSteamID sentTo = CSteamID.Nil;

        public static void Init()
        {
            Router.On(MsgType.BattleChunk, (sender, r) =>
            {
                byte[] payload = Chunker.Receive(r);
                if (payload == null || !Roles.IsMastermind || sender != Roles.DefenderId)
                    return;
                ModLog.Info($"Received battle from {SteamSession.NameOf(sender)} ({payload.Length / 1024} KB)");
                SpectatorSession.Instance.Watch(BattleInfo.FromBytes(payload));
            });
            Router.On(MsgType.BattleEnd, (sender, r) =>
            {
                if (sender == Roles.DefenderId)
                    SpectatorSession.Instance.Stop();
            });
        }

        /// <summary>Called every frame: (re)send the current battle to a newly arrived mastermind.</summary>
        public static void Update()
        {
            if (currentBattle == null || !Roles.IsDefender || !Roles.HasMastermind)
                return;
            CSteamID mastermind = Roles.MastermindId;
            if (mastermind == sentTo)
                return;
            sentTo = mastermind;
            Chunker.Send(mastermind, currentBattle);
            ModLog.Info($"Sent battle to {SteamSession.NameOf(mastermind)} ({currentBattle.Length / 1024} KB)");
        }

        public static void OnDefenderEnteredIsland(Island island)
        {
            try
            {
                byte[] data = BattleInfo.Capture(island.levelNode.index).ToBytes();
                File.WriteAllBytes(SpectatorSession.LastBattlePath, data); // for solo testing with F11
                currentBattle = data;
                sentTo = CSteamID.Nil; // Update() sends it
            }
            catch (Exception e)
            {
                ModLog.Error($"Couldn't capture the battle: {e}");
            }
        }

        public static void OnDefenderLeftIsland()
        {
            if (currentBattle == null)
                return;
            currentBattle = null;
            if (sentTo != CSteamID.Nil && Roles.IsDefender)
                SteamSession.Instance.Send(sentTo, Packet.Build(MsgType.BattleEnd));
            sentTo = CSteamID.Nil;
        }
    }

    [HarmonyPatch(typeof(IslandGameplayManager), nameof(IslandGameplayManager.EnterIsland))]
    internal static class DefenderEnterIslandPatch
    {
        private static void Postfix(Island island)
        {
            if (!SpectatorSession.Active)
                BattleSync.OnDefenderEnteredIsland(island);
        }
    }

    [HarmonyPatch(typeof(Island), nameof(Island.LeaveIsland))]
    internal static class DefenderLeaveIslandPatch
    {
        private static void Prefix()
        {
            if (!SpectatorSession.Active)
                BattleSync.OnDefenderLeftIsland();
        }
    }
}
