using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BadNorthMP.Telemetry
{
    /// <summary>One log line or one event, as kept in a session's spool file.</summary>
    internal sealed class SpoolRecord
    {
        public bool isEvent;
        public long seq, unixMs;
        public int severity = 9;               // OpenTelemetry severity number (9 info, 13 warning, 17 error, 21 fatal)
        public string source = "", context = "", incidents = "", text = "";
        public string json;                    // an event: one line of JSON, sent as it is
    }

    /// <summary>
    /// A game session's spool (BepInEx/BadNorthMP-logs/session-*.log): every log line and event of the session, written
    /// as it happens, and the queue of what's still to be sent. A "*.sent" file beside it says how far it has been
    /// sent (the last sequence number and the byte it ends at); "#END" at its end means the session closed properly.
    /// So nothing is lost to a crash or to playing offline: the next launch sends what's left.
    ///
    /// Format, one record a line (tabs and line breaks inside fields escaped):
    ///   #SESSION {meta json}
    ///   L  seq  unixMs  severity  source  context  incidents  text
    ///   E  seq  unixMs  {event json}
    ///   #END
    /// </summary>
    internal sealed class SpoolFile : IDisposable
    {
        public const long MaxBytes = 20L * 1024 * 1024; // a runaway session stops spooling here
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public readonly string path;
        private readonly object gate = new object();
        private StreamWriter writer;
        private long seq, bytes;
        private bool capped, ended;

        private SpoolFile(string path) { this.path = path; }

        public static SpoolFile Create(string directory, string name, string metaJson)
        {
            Directory.CreateDirectory(directory);
            var file = new SpoolFile(Path.Combine(directory, name));
            var stream = new FileStream(file.path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            file.writer = new StreamWriter(stream, Utf8);
            file.WriteLine("#SESSION " + metaJson);
            file.writer.Flush();
            return file;
        }

        public long LastSeq { get { lock (gate) return seq; } }

        /// <summary>Adds a record (any thread); its sequence number, or 0 if the spool is full or closed.</summary>
        public long Append(SpoolRecord record, bool flush)
        {
            lock (gate)
            {
                if (writer == null || ended || capped) return 0;
                record.seq = ++seq;
                string line = Format(record);
                if (bytes + line.Length > MaxBytes)
                {
                    capped = true;
                    WriteLine("L\t" + record.seq + "\t" + record.unixMs + "\t13\tTelemetry\t\t\tThe session's log is full: nothing more is kept");
                    writer.Flush();
                    return 0;
                }
                WriteLine(line);
                if (flush) writer.Flush();
                return record.seq;
            }
        }

        public void Flush()
        {
            lock (gate)
            {
                try { writer?.Flush(); } catch (IOException) { }
            }
        }

        /// <summary>The session closed properly.</summary>
        public void End()
        {
            lock (gate)
            {
                if (writer == null || ended) return;
                ended = true;
                try
                {
                    WriteLine("#END");
                    writer.Flush();
                    writer.Close();
                }
                catch (IOException) { }
                writer = null;
            }
        }

        public void Dispose() => End();

        private void WriteLine(string line)
        {
            writer.Write(line);
            writer.Write('\n');
            bytes += Utf8.GetByteCount(line) + 1;
        }

        // ---- The record format -----------------------------------------------------------------------------------

        public static string Format(SpoolRecord r)
        {
            if (r.isEvent) return "E\t" + r.seq.ToString(CultureInfo.InvariantCulture) + "\t" + r.unixMs.ToString(CultureInfo.InvariantCulture) + "\t" + Escape(r.json);
            return "L\t" + r.seq.ToString(CultureInfo.InvariantCulture) + "\t" + r.unixMs.ToString(CultureInfo.InvariantCulture) + "\t" +
                r.severity.ToString(CultureInfo.InvariantCulture) + "\t" + Escape(r.source) + "\t" + Escape(r.context) + "\t" +
                Escape(r.incidents) + "\t" + Escape(r.text);
        }

        /// <summary>A record line back (null if it isn't one).</summary>
        public static SpoolRecord Parse(string line)
        {
            if (string.IsNullOrEmpty(line) || line[0] == '#') return null;
            string[] f = line.Split('\t');
            try
            {
                if (f[0] == "E" && f.Length >= 4)
                    return new SpoolRecord { isEvent = true, seq = long.Parse(f[1], CultureInfo.InvariantCulture), unixMs = long.Parse(f[2], CultureInfo.InvariantCulture), json = Unescape(f[3]) };
                if (f[0] == "L" && f.Length >= 8)
                    return new SpoolRecord
                    {
                        seq = long.Parse(f[1], CultureInfo.InvariantCulture), unixMs = long.Parse(f[2], CultureInfo.InvariantCulture),
                        severity = int.Parse(f[3], CultureInfo.InvariantCulture), source = Unescape(f[4]), context = Unescape(f[5]),
                        incidents = Unescape(f[6]), text = Unescape(f[7]),
                    };
            }
            catch (FormatException) { }
            catch (OverflowException) { }
            return null;
        }

        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOfAny(new[] { '\\', '\t', '\n', '\r' }) < 0) return s;
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        public static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0) return s ?? "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char n = s[++i];
                sb.Append(n == 't' ? '\t' : n == 'n' ? '\n' : n == 'r' ? '\r' : n);
            }
            return sb.ToString();
        }

        // ---- Reading what's still to send --------------------------------------------------------------------------

        /// <summary>A stretch of a spool not sent yet.</summary>
        internal sealed class Unsent
        {
            public string path, meta = "{}";
            public readonly List<SpoolRecord> records = new List<SpoolRecord>();
            public long lastSeq, endOffset;
            public bool ended;      // its "#END" was reached: the session closed properly
            public bool reachedEnd; // nothing after this stretch (yet)
        }

        /// <summary>
        /// Up to about <paramref name="maxBytes"/> of the spool after what has been sent: whole lines only (the one being
        /// written may be cut off), with the session's meta.
        /// </summary>
        public static Unsent ReadUnsent(string path, int maxBytes)
        {
            var result = new Unsent { path = path };
            ReadSent(path, out long sentSeq, out long offset);
            result.lastSeq = sentSeq;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long headerEnd = ReadHeader(stream, out string meta);
                result.meta = meta ?? "{}";
                if (offset < headerEnd) offset = headerEnd;
                result.endOffset = offset;
                if (offset >= stream.Length) { result.reachedEnd = true; return result; }
                stream.Position = offset;
                var buffer = new byte[Math.Max(1024, maxBytes)];
                int read = 0, got;
                while (read < buffer.Length && (got = stream.Read(buffer, read, buffer.Length - read)) > 0) read += got;
                int start = 0;
                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] != (byte)'\n') continue;
                    string line = Utf8.GetString(buffer, start, i - start);
                    start = i + 1;
                    if (line == "#END") { result.ended = true; continue; }
                    SpoolRecord record = Parse(line);
                    if (record == null) continue;
                    result.records.Add(record);
                    result.lastSeq = Math.Max(result.lastSeq, record.seq);
                }
                result.endOffset = offset + start;
                if (start == 0 && read == buffer.Length)
                {
                    // One line longer than the whole stretch: it's left out, or nothing after it would ever be sent.
                    stream.Position = offset + read;
                    int b;
                    while ((b = stream.ReadByte()) >= 0 && b != '\n') { }
                    if (b == '\n') result.endOffset = stream.Position;
                }
                result.reachedEnd = result.endOffset >= stream.Length;
            }
            return result;
        }

        /// <summary>The "#SESSION" line's meta; where the records start.</summary>
        private static long ReadHeader(FileStream stream, out string meta)
        {
            meta = null;
            stream.Position = 0;
            var line = new List<byte>(512);
            int b;
            while ((b = stream.ReadByte()) >= 0 && b != '\n') line.Add((byte)b);
            string header = Utf8.GetString(line.ToArray());
            if (header.StartsWith("#SESSION ")) meta = header.Substring(9);
            return stream.Position;
        }

        public static void ReadSent(string path, out long seq, out long offset)
        {
            seq = 0;
            offset = 0;
            try
            {
                string sent = path + ".sent";
                if (!File.Exists(sent)) return;
                string[] parts = File.ReadAllText(sent).Trim().Split(' ');
                if (parts.Length >= 2)
                {
                    long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out seq);
                    long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out offset);
                }
            }
            catch (IOException) { }
        }

        /// <summary>Everything up to <paramref name="upTo"/> has been sent.</summary>
        public static void MarkSent(Unsent upTo) => WriteSent(upTo.path, upTo.lastSeq, upTo.endOffset);

        public static void WriteSent(string path, long seq, long offset)
        {
            try { File.WriteAllText(path + ".sent", seq.ToString(CultureInfo.InvariantCulture) + " " + offset.ToString(CultureInfo.InvariantCulture)); }
            catch (IOException) { }
        }

        /// <summary>Marks everything in a spool so far as sent without sending it (reports turned off).</summary>
        public static void Discard(string path)
        {
            try
            {
                long length = new FileInfo(path).Length;
                ReadSent(path, out long seq, out _);
                WriteSent(path, seq, length);
            }
            catch (IOException) { }
        }

        /// <summary>Whether a spool's session closed properly ("#END" at its end).</summary>
        public static bool Ended(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long from = Math.Max(0, stream.Length - 16);
                    stream.Position = from;
                    var tail = new byte[stream.Length - from];
                    stream.Read(tail, 0, tail.Length);
                    return Utf8.GetString(tail).TrimEnd('\n', '\r').EndsWith("#END");
                }
            }
            catch (IOException) { return false; }
        }

        /// <summary>The meta of a spool's session.</summary>
        public static string Meta(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    ReadHeader(stream, out string meta);
                    return meta ?? "{}";
                }
            }
            catch (IOException) { return "{}"; }
        }

        /// <summary>The log lines in the last <paramref name="maxBytes"/> of a spool, readable (for a crash report).</summary>
        public static List<string> Tail(string path, int maxBytes)
        {
            var lines = new List<string>();
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long from = Math.Max(0, stream.Length - maxBytes);
                    stream.Position = from;
                    var data = new byte[stream.Length - from];
                    int read = 0, got;
                    while (read < data.Length && (got = stream.Read(data, read, data.Length - read)) > 0) read += got;
                    string[] all = Utf8.GetString(data, 0, read).Split('\n');
                    for (int i = from > 0 ? 1 : 0; i < all.Length; i++) // the first may be cut off
                    {
                        SpoolRecord record = Parse(all[i]);
                        if (record != null && !record.isEvent) lines.Add(Readable(record));
                    }
                }
            }
            catch (IOException) { }
            return lines;
        }

        /// <summary>A log record as a log line reads: "12:00:01.250 [Warning: Source] text".</summary>
        public static string Readable(SpoolRecord r)
        {
            string level = r.severity >= 21 ? "Fatal" : r.severity >= 17 ? "Error" : r.severity >= 13 ? "Warning" : r.severity <= 5 ? "Debug" : "Info";
            DateTime at = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(r.unixMs);
            return at.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " [" + level + ": " + r.source + "] " + r.text;
        }

        /// <summary>Old sessions beyond the newest <paramref name="keep"/> go (with their markers).</summary>
        public static void Prune(string directory, int keep, string current)
        {
            try
            {
                if (!Directory.Exists(directory)) return;
                var files = new List<string>(Directory.GetFiles(directory, "session-*.log"));
                files.Sort(StringComparer.Ordinal);
                files.Reverse(); // newest first: names start with the time
                for (int i = keep; i < files.Count; i++)
                {
                    if (files[i] == current) continue;
                    foreach (string extra in new[] { "", ".sent", ".done" })
                        if (File.Exists(files[i] + extra)) File.Delete(files[i] + extra);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
