using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BadNorthMP.Telemetry
{
    /// <summary>
    /// A small JSON writer for what we send PostHog (the game's JsonUtility can't write maps or lists of mixed values):
    /// strings, numbers, booleans, null, dictionaries (string keys) and lists, nested as deep as needed.
    /// </summary>
    internal static class Json
    {
        public static string Write(object value)
        {
            var sb = new StringBuilder(256);
            Write(sb, value);
            return sb.ToString();
        }

        public static void Write(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null: sb.Append("null"); return;
                case string s: String(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case float f: Number(sb, f); return;
                case double d: Number(sb, d); return;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); return;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); return;
                case uint u: sb.Append(u.ToString(CultureInfo.InvariantCulture)); return;
                case ulong ul: sb.Append(ul.ToString(CultureInfo.InvariantCulture)); return;
                case short sh: sb.Append(sh.ToString(CultureInfo.InvariantCulture)); return;
                case byte by: sb.Append(by.ToString(CultureInfo.InvariantCulture)); return;
                case Raw raw: sb.Append(raw.json); return;
                case IDictionary<string, object> map:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (KeyValuePair<string, object> pair in map)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        String(sb, pair.Key);
                        sb.Append(':');
                        Write(sb, pair.Value);
                    }
                    sb.Append('}');
                    return;
                }
                case IEnumerable list:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (object item in list)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Write(sb, item);
                    }
                    sb.Append(']');
                    return;
                }
                default: String(sb, value.ToString()); return;
            }
        }

        private static void Number(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append("null"); // JSON has neither
            else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        public static void String(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        /// <summary>Already-written JSON, put in as it is.</summary>
        public sealed class Raw
        {
            public readonly string json;
            public Raw(string json) { this.json = json; }
        }

        /// <summary>A map in the order things were added (handy for readable payloads).</summary>
        public static Dictionary<string, object> Map() => new Dictionary<string, object>();

        /// <summary>Unix time in nanoseconds as a string (OpenTelemetry's timeUnixNano).</summary>
        public static string Nanos(DateTime utc) =>
            ((utc.Ticks - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks) * 100L).ToString(CultureInfo.InvariantCulture);

        /// <summary>ISO 8601, UTC, to the millisecond (PostHog's timestamp).</summary>
        public static string Iso(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        // ---- Reading -------------------------------------------------------------------------------------------------

        /// <summary>
        /// JSON back into objects: maps as Dictionary&lt;string, object&gt;, arrays as List&lt;object&gt;, numbers as double,
        /// strings, booleans and null. Null if the text isn't JSON.
        /// </summary>
        public static object Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try
            {
                int at = 0;
                object value = ParseValue(text, ref at);
                SkipSpace(text, ref at);
                return at == text.Length ? value : null;
            }
            catch (FormatException) { return null; }
            catch (IndexOutOfRangeException) { return null; }
            catch (ArgumentOutOfRangeException) { return null; }
        }

        /// <summary>A value inside parsed JSON by keys ("flags", "telemetry-off", "enabled"), or null.</summary>
        public static object Get(object json, params string[] path)
        {
            object at = json;
            foreach (string key in path)
            {
                if (!(at is Dictionary<string, object> map) || !map.TryGetValue(key, out at)) return null;
            }
            return at;
        }

        private static void SkipSpace(string s, ref int at)
        {
            while (at < s.Length && char.IsWhiteSpace(s[at])) at++;
        }

        private static object ParseValue(string s, ref int at)
        {
            SkipSpace(s, ref at);
            char c = s[at];
            if (c == '{')
            {
                at++;
                var map = new Dictionary<string, object>();
                SkipSpace(s, ref at);
                if (s[at] == '}') { at++; return map; }
                while (true)
                {
                    SkipSpace(s, ref at);
                    string key = ParseString(s, ref at);
                    SkipSpace(s, ref at);
                    if (s[at++] != ':') throw new FormatException();
                    map[key] = ParseValue(s, ref at);
                    SkipSpace(s, ref at);
                    char next = s[at++];
                    if (next == '}') return map;
                    if (next != ',') throw new FormatException();
                }
            }
            if (c == '[')
            {
                at++;
                var list = new List<object>();
                SkipSpace(s, ref at);
                if (s[at] == ']') { at++; return list; }
                while (true)
                {
                    list.Add(ParseValue(s, ref at));
                    SkipSpace(s, ref at);
                    char next = s[at++];
                    if (next == ']') return list;
                    if (next != ',') throw new FormatException();
                }
            }
            if (c == '"') return ParseString(s, ref at);
            if (string.CompareOrdinal(s, at, "true", 0, 4) == 0) { at += 4; return true; }
            if (string.CompareOrdinal(s, at, "false", 0, 5) == 0) { at += 5; return false; }
            if (string.CompareOrdinal(s, at, "null", 0, 4) == 0) { at += 4; return null; }
            int start = at;
            while (at < s.Length && "+-0123456789.eE".IndexOf(s[at]) >= 0) at++;
            if (at == start) throw new FormatException();
            return double.Parse(s.Substring(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static string ParseString(string s, ref int at)
        {
            if (s[at] != '"') throw new FormatException();
            at++;
            var sb = new StringBuilder();
            while (true)
            {
                char c = s[at++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[at++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        sb.Append((char)int.Parse(s.Substring(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        at += 4;
                        break;
                    default: sb.Append(e); break; // \" \\ \/
                }
            }
        }
    }
}
