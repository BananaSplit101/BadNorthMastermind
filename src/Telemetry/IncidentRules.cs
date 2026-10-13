using System;
using System.Text;

namespace BadNorthMP.Telemetry
{
    /// <summary>
    /// When something going wrong becomes an incident (everyone in the lobby sends their logs under one code), and when
    /// it doesn't, so a bad bug can't flood PostHog or the players' connections:
    /// - within <see cref="MergeWindow"/> of another incident (ours or one we heard from the lobby), it joins that one;
    /// - an automatic one needs <see cref="AutoGap"/> since the last incident, and at most <see cref="MaxAuto"/> a session;
    /// - one the player reports themselves needs only <see cref="ManualGap"/> since their last report.
    /// Times are in seconds, from any clock that only goes forward.
    /// </summary>
    internal sealed class IncidentRules
    {
        public float AutoGap = 120f, ManualGap = 30f, MergeWindow = 10f;
        public int MaxAuto = 5;

        private float lastAny = float.NegativeInfinity, lastManual = float.NegativeInfinity;
        private int autoCount;
        private string lastId;

        /// <summary>
        /// The incident this joins: a new code (<paramref name="isNew"/>), the one it merges into, or null if it's
        /// dropped.
        /// </summary>
        public string Decide(bool manual, float now, Random random, out bool isNew)
        {
            isNew = false;
            if (manual)
            {
                if (now - lastManual < ManualGap) return null;
                lastManual = now;
            }
            else
            {
                if (lastId != null && now - lastAny < MergeWindow) return lastId;
                if (autoCount >= MaxAuto || now - lastAny < AutoGap) return null;
                autoCount++;
            }
            isNew = true;
            lastAny = now;
            lastId = NewId(random);
            return lastId;
        }

        /// <summary>Another lobby member's incident: what we notice soon after joins it rather than starting another.</summary>
        public void Heard(string id, float now)
        {
            if (lastId != null && now - lastAny < MergeWindow) return; // keep ours: theirs is tagged alongside it
            lastAny = now;
            lastId = id;
        }

        // No 0/O, 1/I/L: a code is read out over voice chat.
        private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

        /// <summary>"7F3K-Q2MD".</summary>
        public static string NewId(Random random)
        {
            var sb = new StringBuilder(9);
            for (int i = 0; i < 8; i++)
            {
                if (i == 4) sb.Append('-');
                sb.Append(Alphabet[random.Next(Alphabet.Length)]);
            }
            return sb.ToString();
        }

        /// <summary>Whether a code looks like one of ours (what arrives from the lobby is checked).</summary>
        public static bool IsId(string id)
        {
            if (id == null || id.Length != 9 || id[4] != '-') return false;
            for (int i = 0; i < 9; i++)
                if (i != 4 && Alphabet.IndexOf(id[i]) < 0) return false;
            return true;
        }
    }
}
