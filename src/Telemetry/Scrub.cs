using System;
using System.Collections.Generic;
using System.Text;

namespace BadNorthMP.Telemetry
{
    /// <summary>
    /// What never leaves the player's computer: Steam names and IDs, and the Windows user name (in paths). Each lobby
    /// member becomes a stable label, "player-3f9a2c", worked out from their Steam ID and the lobby, so every member's
    /// report calls the same person the same thing (and a label can be matched across one lobby's reports, not traced
    /// back to an account). Steam IDs we don't know are masked; "C:\Users\name\" becomes "C:\Users\&lt;user&gt;\".
    /// Written without regular expressions (the game ships a trimmed System.dll).
    /// </summary>
    internal static class Scrub
    {
        private static readonly object gate = new object();
        // Longest names first, so "Jay" never eats the start of "Jayden".
        private static readonly List<KeyValuePair<string, string>> names = new List<KeyValuePair<string, string>>();
        private static readonly Dictionary<ulong, string> labels = new Dictionary<ulong, string>();
        private static ulong salt;
        private static string userName;

        /// <summary>
        /// The salt for labels: the lobby (0 outside one). A new lobby gives everyone new labels as they're learned
        /// again; names already known stay hidden (lines from before still mention them).
        /// </summary>
        public static void Lobby(ulong lobby)
        {
            lock (gate)
            {
                if (lobby == salt) return;
                salt = lobby;
                labels.Clear();
            }
        }

        /// <summary>A lobby member (or ourselves): their name and Steam ID become their label wherever they appear.</summary>
        public static string Learn(ulong steamId, string name)
        {
            lock (gate)
            {
                if (!labels.TryGetValue(steamId, out string label))
                {
                    label = LabelFor(steamId, salt);
                    labels[steamId] = label;
                }
                if (!string.IsNullOrEmpty(name) && name.Trim().Length >= 2)
                {
                    int known = names.FindIndex(p => p.Key == name);
                    if (known >= 0) names[known] = new KeyValuePair<string, string>(name, label);
                    else
                    {
                        names.Add(new KeyValuePair<string, string>(name, label));
                        names.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
                    }
                }
                return label;
            }
        }

        /// <summary>The label for a Steam ID (learned or not).</summary>
        public static string Label(ulong steamId)
        {
            lock (gate)
                return labels.TryGetValue(steamId, out string label) ? label : LabelFor(steamId, salt);
        }

        /// <summary>"player-" and six hex digits of FNV-1a over the Steam ID and the lobby: stable within a lobby.</summary>
        public static string LabelFor(ulong steamId, ulong lobby) => "player-" + (Hash(steamId, lobby) & 0xFFFFFF).ToString("x6");

        /// <summary>A lobby, as everyone in it names it (never its Steam ID).</summary>
        public static string LobbyLabel(ulong lobby) => lobby == 0 ? "" : "lobby-" + Hash(lobby, 0x5A17B2C3D4E5F607UL).ToString("x8");

        private static uint Hash(ulong a, ulong b)
        {
            uint hash = 2166136261;
            for (int i = 0; i < 8; i++) { hash ^= (byte)(a >> (8 * i)); hash *= 16777619; }
            for (int i = 0; i < 8; i++) { hash ^= (byte)(b >> (8 * i)); hash *= 16777619; }
            return hash;
        }

        /// <summary>The text, with names, Steam IDs and the Windows user name taken out.</summary>
        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (userName == null)
            {
                try { userName = Environment.UserName ?? ""; } catch (Exception) { userName = ""; }
            }
            string result = text;
            lock (gate)
            {
                foreach (KeyValuePair<string, string> pair in names)
                    if (result.IndexOf(pair.Key, StringComparison.Ordinal) >= 0) result = result.Replace(pair.Key, pair.Value);
            }
            result = SteamIds(result);
            result = UserPaths(result);
            if (userName.Length >= 3 && result.IndexOf(userName, StringComparison.OrdinalIgnoreCase) >= 0)
                result = ReplaceIgnoringCase(result, userName, "<user>");
            return result;
        }

        /// <summary>
        /// An event's JSON, every string in it cleaned (not the JSON text itself: a name can sit inside an escape, and
        /// replacing it there would break the JSON). JSON that can't be read comes back as "{}".
        /// </summary>
        public static string CleanJson(string json)
        {
            object value = Json.Parse(json);
            if (value == null) return "{}";
            return Json.Write(CleanValue(value));
        }

        private static object CleanValue(object value)
        {
            switch (value)
            {
                case string s: return Clean(s);
                case Dictionary<string, object> map:
                {
                    var cleaned = new Dictionary<string, object>(map.Count);
                    foreach (KeyValuePair<string, object> pair in map) cleaned[pair.Key] = CleanValue(pair.Value);
                    return cleaned;
                }
                case List<object> list:
                {
                    var cleaned = new List<object>(list.Count);
                    foreach (object item in list) cleaned.Add(CleanValue(item));
                    return cleaned;
                }
                default: return value;
            }
        }

        /// <summary>Every 17-digit Steam ID (7656119...): a known one becomes its label, any other "steam-id".</summary>
        private static string SteamIds(string text)
        {
            if (text.IndexOf("7656119", StringComparison.Ordinal) < 0) return text;
            var sb = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                if (char.IsDigit(text[i]) && (i == 0 || !char.IsDigit(text[i - 1])))
                {
                    int end = i;
                    while (end < text.Length && char.IsDigit(text[end])) end++;
                    string run = text.Substring(i, end - i);
                    if (run.Length == 17 && run.StartsWith("7656119") && ulong.TryParse(run, out ulong id))
                    {
                        string label;
                        lock (gate) label = labels.TryGetValue(id, out string known) ? known : "steam-id";
                        sb.Append(label);
                    }
                    else sb.Append(run);
                    i = end;
                }
                else sb.Append(text[i++]);
            }
            return sb.ToString();
        }

        /// <summary>"\Users\&lt;anything&gt;\" (either slash) becomes "\Users\&lt;user&gt;\".</summary>
        private static string UserPaths(string text)
        {
            int at = IndexOfUsers(text, 0);
            if (at < 0) return text;
            var sb = new StringBuilder(text.Length);
            int copied = 0;
            while (at >= 0)
            {
                int nameStart = at + 7; // past "\Users\"
                // An escaped path ("C:\\Users\\name"): past the second slash too.
                while (nameStart < text.Length && (text[nameStart] == '\\' || text[nameStart] == '/')) nameStart++;
                int nameEnd = nameStart;
                while (nameEnd < text.Length && text[nameEnd] != '\\' && text[nameEnd] != '/' && text[nameEnd] != '\n' && text[nameEnd] != '"') nameEnd++;
                sb.Append(text, copied, nameStart - copied).Append("<user>");
                copied = nameEnd;
                at = IndexOfUsers(text, nameEnd);
            }
            sb.Append(text, copied, text.Length - copied);
            return sb.ToString();
        }

        private static int IndexOfUsers(string text, int from)
        {
            for (int i = from; i + 7 <= text.Length; i++)
            {
                char a = text[i], b = text[i + 6];
                if ((a == '\\' || a == '/') && (b == '\\' || b == '/') &&
                    string.Compare(text, i + 1, "users", 0, 5, StringComparison.OrdinalIgnoreCase) == 0)
                    return i;
            }
            return -1;
        }

        private static string ReplaceIgnoringCase(string text, string find, string with)
        {
            var sb = new StringBuilder(text.Length);
            int copied = 0, at;
            while ((at = text.IndexOf(find, copied, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                sb.Append(text, copied, at - copied).Append(with);
                copied = at + find.Length;
            }
            sb.Append(text, copied, text.Length - copied);
            return sb.ToString();
        }
    }
}
