using System.Collections.Generic;
using System.IO;

namespace BadNorthMP.Net
{
    internal sealed class DeploySeat
    {
        public ulong player;
        /// <summary>How many squads this player brings (the island's rescued commander not counted).</summary>
        public int quota;
        /// <summary>Their commanders who can fight on this island, most rested first.</summary>
        public List<int> candidates = new List<int>();
        public List<int> picks = new List<int>();
        public bool ready;
        public bool Choosing => quota > 0;
    }

    /// <summary>
    /// Before an island starts: how many squads each player brings and which of their commanders they picked. The
    /// leader shares out the squads, everyone picks their own commanders and presses Ready, then the leader deploys.
    /// Game-independent, like PartyRoster.
    /// </summary>
    internal sealed class PartyDeployment
    {
        public const int MaxSquads = 4, MaxHeroes = 10;
        public int token, level, rescueHero = -1;
        public ulong rescueOwner;
        public float secondsLeft;
        /// <summary>The leader has deployed (everyone was ready, or time ran out).</summary>
        public bool done;
        public readonly List<DeploySeat> seats = new List<DeploySeat>();

        public DeploySeat Find(ulong player) => seats.Find(seat => seat.player == player);

        public bool AllReady
        {
            get
            {
                foreach (var seat in seats) if (seat.Choosing && !seat.ready) return false;
                return true;
            }
        }

        /// <summary>
        /// Shares out the squads: the island's rescued commander always goes and takes one of the MaxSquads; every
        /// player with a commander who can fight gets one squad first, in seat order (the rescue's owner counts it as
        /// theirs); spare squads go round the table, starting with a different player each island. candidates[s] are
        /// seat s's commanders who can fight. With nobody able to fight, nobody goes (the rescue needs company).
        /// </summary>
        public static int[] Shares(IList<List<int>> candidates, int rescueSeat, int round)
        {
            int count = candidates.Count;
            var shares = new int[count];
            bool anyone = false;
            foreach (var list in candidates) anyone |= list.Count > 0;
            if (!anyone) return shares;
            int slots = rescueSeat >= 0 ? MaxSquads - 1 : MaxSquads, used = 0;
            for (int s = 0; s < count && used < slots; s++)
                if (s != rescueSeat && candidates[s].Count > 0) { shares[s]++; used++; }
            int start = count > 0 ? round % count : 0;
            for (bool added = true; added && used < slots;)
            {
                added = false;
                for (int step = 0; step < count && used < slots; step++)
                {
                    int s = (start + step) % count;
                    if (candidates[s].Count <= shares[s]) continue;
                    shares[s]++; used++; added = true;
                }
            }
            return shares;
        }

        /// <summary>Leader: a new deployment for an island, each player starting with their most rested commanders.</summary>
        public static PartyDeployment Plan(int token, int level, IList<ulong> players, IList<List<int>> candidates,
            int rescueSeat, int rescueHero, int round)
        {
            var plan = new PartyDeployment { token = token, level = level };
            if (rescueSeat >= 0 && rescueHero >= 0) { plan.rescueHero = rescueHero; plan.rescueOwner = players[rescueSeat]; }
            int[] shares = Shares(candidates, rescueSeat, round);
            for (int s = 0; s < players.Count; s++)
            {
                var seat = new DeploySeat { player = players[s], quota = shares[s], candidates = new List<int>(candidates[s]) };
                for (int i = 0; i < shares[s]; i++) seat.picks.Add(candidates[s][i]);
                seat.ready = !seat.Choosing;
                plan.seats.Add(seat);
            }
            return plan;
        }

        /// <summary>Every squad going in: each player's picks in seat order, then the rescued commander (where the native loadout puts it).</summary>
        public List<int> Squads()
        {
            var squads = new List<int>();
            foreach (var seat in seats) squads.AddRange(seat.picks);
            if (squads.Count > 0 && rescueHero >= 0) squads.Add(rescueHero);
            return squads;
        }

        /// <summary>A player's choice: different commanders of their own who can fight, at least one and at most their share.</summary>
        public bool Pick(ulong player, int token, IList<int> heroes, bool ready)
        {
            var seat = Find(player);
            if (done || token != this.token || seat == null || !seat.Choosing || heroes.Count < 1 || heroes.Count > seat.quota) return false;
            var seen = new HashSet<int>();
            foreach (int hero in heroes) if (!seat.candidates.Contains(hero) || !seen.Add(hero)) return false;
            seat.picks = new List<int>(heroes);
            seat.ready = ready;
            return true;
        }

        /// <summary>Time's up: a player who unpicked everything brings their most rested commanders after all.</summary>
        public void FillEmpty()
        {
            foreach (var seat in seats)
                if (seat.Choosing && seat.picks.Count == 0)
                    for (int i = 0; i < seat.quota && i < seat.candidates.Count; i++) seat.picks.Add(seat.candidates[i]);
        }

        /// <summary>A player who left can't command anyone: their squads stay home. True if that changed anything.</summary>
        public bool Drop(ulong player)
        {
            var seat = Find(player);
            if (seat == null || !seat.Choosing) return false;
            seat.quota = 0; seat.picks.Clear(); seat.ready = true;
            return true;
        }

        /// <summary>Clicking a commander: pick or unpick it; with the share already full, it replaces the oldest pick.</summary>
        public static List<int> Toggle(IList<int> picks, int hero, int quota)
        {
            var result = new List<int>(picks);
            if (result.Remove(hero)) return result;
            if (quota <= 0) return result;
            if (result.Count >= quota) result.RemoveAt(0);
            result.Add(hero);
            return result;
        }

        public void Write(BinaryWriter w)
        {
            w.Write(token); w.Write(level); w.Write(rescueHero); w.Write(rescueOwner); w.Write(secondsLeft); w.Write(done);
            w.Write((byte)seats.Count);
            foreach (var seat in seats)
            {
                w.Write(seat.player); w.Write((byte)seat.quota); w.Write(seat.ready);
                WriteIds(w, seat.candidates); WriteIds(w, seat.picks);
            }
        }

        public static PartyDeployment Read(BinaryReader r)
        {
            var result = new PartyDeployment { token = r.ReadInt32(), level = r.ReadInt32(), rescueHero = r.ReadInt32(),
                rescueOwner = r.ReadUInt64(), secondsLeft = r.ReadSingle(), done = r.ReadBoolean() };
            int count = r.ReadByte();
            if (count > 4 || result.rescueHero < -1 || result.rescueHero >= MaxHeroes) throw new IOException("Invalid deployment");
            for (int i = 0; i < count; i++)
            {
                var seat = new DeploySeat { player = r.ReadUInt64(), quota = r.ReadByte(), ready = r.ReadBoolean() };
                seat.candidates = ReadIds(r); seat.picks = ReadIds(r);
                if (seat.player == 0 || result.Find(seat.player) != null || seat.quota > MaxSquads || seat.picks.Count > seat.quota)
                    throw new IOException("Invalid deployment seat");
                foreach (int hero in seat.picks) if (!seat.candidates.Contains(hero)) throw new IOException("Invalid deployment pick");
                result.seats.Add(seat);
            }
            return result;
        }

        private static void WriteIds(BinaryWriter w, List<int> ids)
        {
            w.Write((byte)ids.Count);
            foreach (int id in ids) w.Write((byte)id);
        }

        private static List<int> ReadIds(BinaryReader r)
        {
            int count = r.ReadByte();
            if (count > MaxHeroes) throw new IOException("Invalid commander list");
            var ids = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                int id = r.ReadByte();
                if (id >= MaxHeroes || ids.Contains(id)) throw new IOException("Invalid commander");
                ids.Add(id);
            }
            return ids;
        }
    }
}
