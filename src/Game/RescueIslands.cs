using System.Collections.Generic;

namespace BadNorthMP.Game
{
    /// <summary>
    /// Bad North numbers the commanders to rescue from 2 on, because a solo campaign starts with two. A party starts
    /// with up to four, so the first rescue islands would point at commanders already in the party: nothing to rescue
    /// there. Repair points every unplayed rescue island at a commander who really can be rescued. Game-independent.
    /// </summary>
    internal static class RescueIslands
    {
        /// <summary>
        /// heroIds, meta and played are per island (its commander to rescue or -1, whether it's a meta reward, whether
        /// it's been played); rescuable says which commanders can still be rescued. A broken island (unplayed, its
        /// commander not rescuable) gets one no island has yet, meta-reward islands first; with none left, a meta-reward
        /// island takes one from an ordinary unplayed rescue island, and the rest become ordinary islands. Returns how
        /// many islands changed.
        /// </summary>
        public static int Repair(int[] heroIds, bool[] meta, bool[] played, bool[] rescuable)
        {
            var used = new HashSet<int>();
            foreach (int id in heroIds) if (id >= 0) used.Add(id);
            var spare = new Queue<int>();
            for (int hero = 0; hero < rescuable.Length; hero++)
                if (rescuable[hero] && !used.Contains(hero)) spare.Enqueue(hero);
            var broken = new List<int>();
            for (int i = 0; i < heroIds.Length; i++)
            {
                int id = heroIds[i];
                if (id >= 0 && !played[i] && (id >= rescuable.Length || !rescuable[id])) broken.Add(i);
            }
            // Meta-reward islands (their commander's trait unlocks for good) are served first.
            broken.Sort((a, b) => meta[a] == meta[b] ? a.CompareTo(b) : meta[a] ? -1 : 1);
            foreach (int i in broken)
            {
                if (spare.Count > 0) { heroIds[i] = spare.Dequeue(); continue; }
                int donor = -1;
                if (meta[i])
                    for (int j = heroIds.Length - 1; j >= 0; j--)
                        if (j != i && !meta[j] && !played[j] && heroIds[j] >= 0 && heroIds[j] < rescuable.Length && rescuable[heroIds[j]])
                        { donor = j; break; }
                if (donor >= 0) { heroIds[i] = heroIds[donor]; heroIds[donor] = -1; }
                else { heroIds[i] = -1; meta[i] = false; }
            }
            return broken.Count;
        }
    }
}
