using System;
using System.Collections.Generic;

namespace BadNorthMP.Net
{
    /// <summary>The party vote's counting rules, kept free of game types so they can be tested on their own.</summary>
    internal static class VoteTally
    {
        /// <summary>Drop votes from players who are no longer voters, or for options that are no longer allowed.</summary>
        public static bool Prune(IDictionary<ulong, int> votes, ICollection<ulong> voters, Func<int, bool> valid)
        {
            var stale = new List<ulong>();
            foreach (var vote in votes)
                if (!voters.Contains(vote.Key) || (valid != null && !valid(vote.Value))) stale.Add(vote.Key);
            foreach (ulong player in stale) votes.Remove(player);
            return stale.Count > 0;
        }

        public static bool AllVoted(IDictionary<ulong, int> votes, ICollection<ulong> voters)
        {
            if (voters.Count == 0) return false;
            foreach (ulong voter in voters) if (!votes.ContainsKey(voter)) return false;
            return true;
        }

        /// <summary>Decide once everyone has voted, or when the countdown that started with the first vote runs out.</summary>
        public static bool Ready(IDictionary<ulong, int> votes, ICollection<ulong> voters, float now, float deadline) =>
            votes.Count > 0 && (AllVoted(votes, voters) || (deadline >= 0f && now >= deadline));

        /// <summary>The options with the most votes, in ascending order.</summary>
        public static List<int> Leaders(IDictionary<ulong, int> votes)
        {
            var tally = new Dictionary<int, int>();
            foreach (int option in votes.Values) tally[option] = tally.TryGetValue(option, out int n) ? n + 1 : 1;
            int best = 0;
            foreach (int n in tally.Values) best = Math.Max(best, n);
            var leaders = new List<int>();
            foreach (var entry in tally) if (entry.Value == best) leaders.Add(entry.Key);
            leaders.Sort();
            return leaders;
        }

        public static int Count(IDictionary<ulong, int> votes, int option)
        {
            int n = 0;
            foreach (int vote in votes.Values) if (vote == option) n++;
            return n;
        }

        /// <summary>The most-voted option; a tie is broken at random between the tied options only.</summary>
        public static int Winner(IDictionary<ulong, int> votes, Random random)
        {
            List<int> leaders = Leaders(votes);
            return leaders.Count == 0 ? int.MinValue : leaders[random.Next(leaders.Count)];
        }
    }
}
