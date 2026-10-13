using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthMP.Net
{
    /// <summary>
    /// Whose squad is in (or on its way into) each house, for the labels over the houses (UI.HouseLabels) and so a
    /// teammate's healing order only offers houses that are free on the leader's island.
    /// </summary>
    internal static class HouseOccupants
    {
        public struct Entry
        {
            public int hero;
            public bool inside;
        }

        // How long a watcher trusts what the leader last said.
        private const float Stale = 5f;

        /// <summary>House index (in the island's village) -> who's there.</summary>
        public static readonly Dictionary<int, Entry> Current = new Dictionary<int, Entry>();
        private static float heardAt = -100f, nextRead;

        public static void Reset()
        {
            Current.Clear();
            heardAt = -100f;
        }

        /// <summary>The leader's own houses.</summary>
        public static Dictionary<int, Entry> Read(Island island)
        {
            var found = new Dictionary<int, Entry>();
            if (!island || !island.village || island.village.houses == null || Profile.campaign == null) return found;
            House[] houses = island.village.houses;
            for (int i = 0; i < houses.Length && i < 256; i++)
            {
                var location = houses[i] ? houses[i].GetComponent<SquadReplenishLocation>() : null;
                EnglishSquad squad = location ? location.currentSquad : null;
                if (!squad || squad.hero == null) continue;
                found[i] = new Entry
                {
                    hero = Profile.campaign.heroes.IndexOf(squad.hero),
                    inside = location.numContainedAgents > 0 || location.squadHealing || location.squadExiting,
                };
            }
            return found;
        }

        public static string Key(Dictionary<int, Entry> occupants)
        {
            var key = new System.Text.StringBuilder();
            foreach (var pair in occupants) key.Append(pair.Key).Append(':').Append(pair.Value.hero).Append(pair.Value.inside ? '+' : '-').Append(';');
            return key.ToString();
        }

        public static void Write(BinaryWriter w, Dictionary<int, Entry> occupants)
        {
            w.Write((byte)Math.Min(occupants.Count, 255));
            int written = 0;
            foreach (var pair in occupants)
            {
                if (written++ >= 255) break;
                w.Write((byte)pair.Key);
                w.Write(pair.Value.hero);
                w.Write(pair.Value.inside);
            }
        }

        /// <summary>Watchers: the leader's houses.</summary>
        public static void Apply(BinaryReader r)
        {
            Current.Clear();
            for (int count = r.ReadByte(), i = 0; i < count; i++)
                Current[r.ReadByte()] = new Entry { hero = r.ReadInt32(), inside = r.ReadBoolean() };
            heardAt = Time.unscaledTime;
        }

        /// <summary>Every frame: a game that runs its own battle reads its houses; a watcher's list lapses without news.</summary>
        public static void Update()
        {
            if (!Spectator.SpectatorSession.Active)
            {
                if (Time.unscaledTime < nextRead) return;
                nextRead = Time.unscaledTime + 0.25f;
                Dictionary<int, Entry> read = Read(GameAccess.CurrentIsland);
                Current.Clear();
                foreach (var pair in read) Current[pair.Key] = pair.Value;
            }
            else if (Current.Count > 0 && Time.unscaledTime - heardAt > Stale) Current.Clear();
        }

        /// <summary>The index of a house in the island's village, or -1.</summary>
        public static int IndexOf(SquadReplenishLocation location)
        {
            Island island = GameAccess.CurrentIsland;
            if (!location || !island || !island.village || island.village.houses == null) return -1;
            return Array.IndexOf(island.village.houses, location.GetComponent<House>());
        }

        /// <summary>Someone else's squad is in (or heading into) this house on the leader's island.</summary>
        public static bool TakenFrom(SquadReplenishLocation location, EnglishSquad squad, out int hero)
        {
            hero = -1;
            if (Profile.campaign == null || !Current.TryGetValue(IndexOf(location), out Entry entry)) return false;
            int mine = squad && squad.hero != null ? Profile.campaign.heroes.IndexOf(squad.hero) : -1;
            hero = entry.hero;
            return entry.hero != mine;
        }

        /// <summary>Who a commander belongs to, for a label: "You", the player's name, or "" when no one holds it.</summary>
        public static string OwnerName(int hero)
        {
            var seat = PartyFlow.State != null ? PartyFlow.State.OwnerOf(hero) : null;
            if (seat == null) return "";
            var session = SteamSession.Instance;
            return session && seat.player == session.Me.m_SteamID ? "You" : SteamSession.NameOf(new Steamworks.CSteamID(seat.player));
        }
    }
}
