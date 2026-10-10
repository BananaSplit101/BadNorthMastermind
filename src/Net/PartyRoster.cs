using System;
using System.Collections.Generic;
using System.IO;

namespace BadNorthMP.Net
{
    internal sealed class DefenderSeat
    {
        public ulong player;
        // heroes, choices, traits and items are the drafted starting slots, one entry each (none for a player who
        // joined mid-campaign). A starting commander handed to someone else leaves its slot at -1.
        public int[] choices, heroes;
        public string[] traits, items;
        // Commanders rescued during the campaign, beyond the starting slots.
        public List<int> recruits = new List<int>();
        public bool ready;
    }

    // Game-independent draft and ownership rules, shared by the lobby and battle controls.
    internal sealed class PartyRoster
    {
        public const int ChoiceCount = 8;
        public string[] allowedTraits = new string[0], allowedItems = new string[0];
        public readonly List<DefenderSeat> seats = new List<DefenderSeat>();
        public int token, phase, seed, difficulty = 2, round;
        public ulong authority;
        // The seat that receives the next rescued commander: the leader first, then the others in seat order.
        public int nextRecruit;
        /// <summary>The multiplayer save being continued (picker slot 0..4), or -1 for a new campaign. Continuing, the
        /// commanders come from the save, so the draft only asks for Ready.</summary>
        public int continuing = -1;
        public bool Continuing => continuing >= 0;
        public const int MaxHeroes = 10;
        public bool AllReady
        {
            get
            {
                if (seats.Count == 0) return false;
                foreach (var seat in seats) if (!seat.ready) return false;
                return true;
            }
        }
        public DefenderSeat Find(ulong player) => seats.Find(seat => seat.player == player);
        public bool Owns(ulong player, int hero)
        {
            var seat = Find(player);
            if (seat == null || hero < 0) return false;
            foreach (int id in seat.heroes) if (id == hero) return true;
            return seat.recruits.Contains(hero);
        }
        /// <summary>Every commander a seat owns: starting slots first, then rescued commanders.</summary>
        public static List<int> Owned(DefenderSeat seat)
        {
            var result = new List<int>();
            foreach (int id in seat.heroes) if (id >= 0) result.Add(id);
            result.AddRange(seat.recruits);
            return result;
        }
        public DefenderSeat OwnerOf(int hero)
        {
            if (hero < 0) return null;
            foreach (var seat in seats)
                if (Array.IndexOf(seat.heroes, hero) >= 0 || seat.recruits.Contains(hero)) return seat;
            return null;
        }
        /// <summary>
        /// Give a rescued commander to the next connected player in turn: the leader, then the other defenders in
        /// seat (lobby join) order. A commander already owned by a connected player keeps its owner.
        /// </summary>
        public DefenderSeat AssignRecruit(int hero, Func<ulong, bool> connected)
        {
            if (hero < 0 || hero >= MaxHeroes || seats.Count == 0) return null;
            var owner = OwnerOf(hero);
            if (owner != null && connected(owner.player)) return owner;
            // Starting slots stay with their player; only a rescued commander moves on.
            if (owner != null && Array.IndexOf(owner.heroes, hero) >= 0) return owner;
            if (owner != null) owner.recruits.Remove(hero);
            // A player without a single commander (who joined mid-campaign) gets the next one before the round goes on.
            foreach (var seat in seats)
                if (connected(seat.player) && Owned(seat).Count == 0) { seat.recruits.Add(hero); return seat; }
            if (nextRecruit < 0 || nextRecruit >= seats.Count) nextRecruit = 0;
            for (int step = 0; step < seats.Count; step++)
            {
                int index = (nextRecruit + step) % seats.Count;
                if (!connected(seats[index].player)) continue;
                seats[index].recruits.Add(hero);
                nextRecruit = (index + 1) % seats.Count;
                return seats[index];
            }
            if (owner != null) owner.recruits.Add(hero); // nobody connected: leave it where it was
            return owner;
        }
        /// <summary>
        /// A player who joined after the draft takes a seat in the running campaign (at most 4 seats). Whoever commands
        /// the most commanders, if that's two or more, hands one over (on a tie, the later seat, so the leader keeps
        /// theirs longest): one that can fight next if they have one, a rescued commander before a starting one, the
        /// most recently gained first. Returns the new seat (null: no room, or already seated), with the commander
        /// handed over and who had it (-1 and null when nobody had one to spare).
        /// </summary>
        public DefenderSeat SeatLateJoiner(ulong player, Func<int, bool> available, out int hero, out DefenderSeat donor)
        {
            hero = -1; donor = null;
            if (player == 0 || phase != 3 || seats.Count >= 4 || Find(player) != null) return null;
            foreach (var seat in seats)
            {
                int owned = Owned(seat).Count;
                if (owned >= 2 && (donor == null || owned >= Owned(donor).Count)) donor = seat;
            }
            var joined = new DefenderSeat { player = player, choices = new int[0], heroes = new int[0],
                traits = new string[0], items = new string[0], ready = true };
            seats.Add(joined);
            if (donor == null) return joined;
            List<int> spare = Owned(donor);
            spare.Reverse(); // rescued commanders, latest first, then the starting slots
            int pick = spare.FindIndex(id => available(id));
            hero = spare[pick >= 0 ? pick : 0];
            if (!donor.recruits.Remove(hero)) donor.heroes[Array.IndexOf(donor.heroes, hero)] = -1;
            joined.recruits.Add(hero);
            return joined;
        }

        public void Draft(IList<ulong> defenders)
        {
            if (defenders.Count < 1 || defenders.Count > 4) throw new IOException("Need 1-4 defenders");
            var old = new List<DefenderSeat>(seats);
            seats.Clear();
            int slots = defenders.Count == 1 ? 2 : 1;
            foreach (ulong player in defenders)
            {
                if (Find(player) != null || player == 0) throw new IOException("Invalid defender roster");
                var previous = old.Find(seat => seat.player == player);
                var seat = new DefenderSeat { player = player, choices = new int[slots], heroes = new int[slots], traits = new string[slots], items = new string[slots] };
                for (int i = 0; i < slots; i++)
                {
                    seat.choices[i] = previous != null && i < previous.choices.Length ? previous.choices[i] : 0;
                    seat.heroes[i] = -1;
                    seat.traits[i] = previous != null && previous.traits != null && i < previous.traits.Length ? previous.traits[i] : "";
                    seat.items[i] = previous != null && previous.items != null && i < previous.items.Length ? previous.items[i] : "";
                }
                seats.Add(seat);
            }
            authority = defenders[0];
            phase = 1;
            round = 0;
            nextRecruit = 0;
            ++token;
        }
        public bool Pick(ulong player, int revision, int[] choices, bool ready, string[] traits = null, string[] items = null)
        {
            var seat = Find(player);
            if (phase != 1 || revision != token || seat == null || choices.Length != seat.choices.Length) return false;
            foreach (int type in choices) if (type < 0 || type >= ChoiceCount) return false;
            if (traits != null || items != null)
            {
                if (traits == null || items == null || traits.Length != choices.Length || items.Length != choices.Length) return false;
                for (int i = 0; i < choices.Length; i++)
                    if (!Allowed(allowedTraits, traits[i]) || !Allowed(allowedItems, items[i])) return false;
                seat.traits = (string[])traits.Clone(); seat.items = (string[])items.Clone();
            }
            seat.choices = (int[])choices.Clone();
            seat.ready = ready;
            return true;
        }
        public static bool Allowed(string[] catalog, string value) => value == "" || (value != null && Array.IndexOf(catalog, value) >= 0);
        public static void WriteNames(BinaryWriter w, string[] names)
        { w.Write((byte)names.Length); foreach (string name in names) w.Write(name ?? ""); }
        public static string[] ReadNames(BinaryReader r)
        {
            int count = r.ReadByte(); if (count > 128) throw new IOException("Invalid loadout catalog");
            var names = new string[count];
            for (int i = 0; i < count; i++) { names[i] = r.ReadString(); if (names[i].Length > 200) throw new IOException("Invalid upgrade name"); }
            return names;
        }
        public void AssignStartingHeroes()
        {
            int index = 0;
            foreach (var seat in seats)
                for (int i = 0; i < seat.heroes.Length; i++) seat.heroes[i] = index++;
        }
        public void Write(BinaryWriter w)
        {
            w.Write(token); w.Write(phase); w.Write(seed); w.Write(difficulty); w.Write(round); w.Write(authority);
            w.Write((byte)seats.Count);
            foreach (var seat in seats)
            {
                w.Write(seat.player); w.Write(seat.ready); w.Write((byte)seat.choices.Length);
                for (int i = 0; i < seat.choices.Length; i++) { w.Write(seat.choices[i]); w.Write(seat.heroes[i]); }
            }
            WriteNames(w, allowedTraits); WriteNames(w, allowedItems);
            foreach (var seat in seats) { WriteNames(w, seat.traits); WriteNames(w, seat.items); }
            w.Write((byte)nextRecruit);
            foreach (var seat in seats)
            {
                w.Write((byte)seat.recruits.Count);
                foreach (int hero in seat.recruits) w.Write(hero);
            }
            w.Write((sbyte)continuing);
        }
        public static PartyRoster Read(BinaryReader r)
        {
            var result = new PartyRoster { token = r.ReadInt32(), phase = r.ReadInt32(), seed = r.ReadInt32(),
                difficulty = r.ReadInt32(), round = r.ReadInt32(), authority = r.ReadUInt64() };
            int count = r.ReadByte();
            if (count < 1 || count > 4 || result.phase < 1 || result.phase > 3 || result.token <= 0 ||
                result.difficulty < 1 || result.difficulty > 4 || result.round < 0) throw new IOException("Invalid party state");
            var heroes = new HashSet<int>();
            for (int n = 0; n < count; n++)
            {
                var seat = new DefenderSeat { player = r.ReadUInt64(), ready = r.ReadBoolean() };
                int slots = r.ReadByte();
                // Drafted seats have one starting slot (two for a lone defender); in a running campaign a player who
                // joined late has none.
                bool slotsValid = result.phase == 3 ? slots <= 2 : slots == (count == 1 ? 2 : 1);
                if (!slotsValid || seat.player == 0 || result.Find(seat.player) != null)
                    throw new IOException("Invalid defender seats");
                seat.choices = new int[slots]; seat.heroes = new int[slots];
                for (int i = 0; i < slots; i++)
                {
                    seat.choices[i] = r.ReadInt32(); seat.heroes[i] = r.ReadInt32();
                    if (seat.choices[i] < 0 || seat.choices[i] >= ChoiceCount || seat.heroes[i] < -1 || seat.heroes[i] > 9 ||
                        (seat.heroes[i] >= 0 && !heroes.Add(seat.heroes[i]))) throw new IOException("Invalid squad assignment");
                }
                result.seats.Add(seat);
            }
            result.allowedTraits = ReadNames(r); result.allowedItems = ReadNames(r);
            foreach (var seat in result.seats)
            {
                seat.traits = ReadNames(r); seat.items = ReadNames(r);
                if (seat.traits.Length != seat.choices.Length || seat.items.Length != seat.choices.Length) throw new IOException("Invalid loadout slots");
                for (int i = 0; i < seat.choices.Length; i++)
                    if (!Allowed(result.allowedTraits, seat.traits[i]) || !Allowed(result.allowedItems, seat.items[i])) throw new IOException("Unavailable starting upgrade");
            }
            result.nextRecruit = r.ReadByte();
            if (result.nextRecruit >= count) throw new IOException("Invalid recruit order");
            foreach (var seat in result.seats)
            {
                int recruits = r.ReadByte();
                if (recruits > MaxHeroes) throw new IOException("Invalid recruits");
                for (int i = 0; i < recruits; i++)
                {
                    int hero = r.ReadInt32();
                    if (hero < 0 || hero >= MaxHeroes || !heroes.Add(hero)) throw new IOException("Invalid recruit assignment");
                    seat.recruits.Add(hero);
                }
            }
            result.continuing = r.ReadSByte();
            if (result.continuing < -1 || result.continuing > 4) throw new IOException("Invalid continued save");
            if (result.Find(result.authority) == null) throw new IOException("Missing campaign authority");
            return result;
        }
    }
}
