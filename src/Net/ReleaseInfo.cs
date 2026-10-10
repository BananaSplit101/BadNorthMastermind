using System;

namespace BadNorthMP.Net
{
    /// <summary>
    /// The newest release as published in the repo's release/latest.txt, one "key value" per line:
    /// version 1.0.1 / size 654336 / sha256 (64 hex digits). tools/package.ps1 writes it next to release/BadNorthMP.dll.
    /// </summary>
    internal sealed class ReleaseInfo
    {
        public const int MaxSize = 8 * 1024 * 1024;

        public string version;
        public string sha256;
        public int size;

        /// <summary>The release described by <paramref name="text"/>, or null if anything is missing or malformed.</summary>
        public static ReleaseInfo Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var info = new ReleaseInfo();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                int space = line.IndexOf(' ');
                if (space <= 0) continue;
                string key = line.Substring(0, space), value = line.Substring(space + 1).Trim();
                if (key == "version") info.version = value;
                else if (key == "sha256") info.sha256 = value.ToLowerInvariant();
                else if (key == "size") int.TryParse(value, out info.size);
            }
            bool valid = ParseVersion(info.version) != null && info.sha256 != null && info.sha256.Length == 64 &&
                IsHex(info.sha256) && info.size > 1024 && info.size <= MaxSize;
            return valid ? info : null;
        }

        /// <summary>Is <paramref name="candidate"/> a later version than <paramref name="current"/> (both like 1.2.3)?</summary>
        public static bool Newer(string candidate, string current)
        {
            Version a = ParseVersion(candidate), b = ParseVersion(current);
            return a != null && b != null && a.CompareTo(b) > 0;
        }

        private static Version ParseVersion(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            try { return new Version(value); }
            catch (Exception) { return null; }
        }

        private static bool IsHex(string value)
        {
            foreach (char c in value)
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
            return true;
        }
    }
}
