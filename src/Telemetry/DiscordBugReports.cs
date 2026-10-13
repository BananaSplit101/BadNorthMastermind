using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using BadNorthMP.Net;
using UnityEngine;
using UnityEngine.Networking;

namespace BadNorthMP.Telemetry
{
    /// <summary>
    /// A player's bug report, posted to the bug-report Discord channel: what they wrote, where they were, and their
    /// recent log as a file, with names and IDs taken out as for PostHog. The webhook is built in from a file on the
    /// developer's PC (BadNorthMP.csproj); a build without it doesn't post. Never log the webhook.
    /// </summary>
    internal static class DiscordBugReports
    {
        private const string WebhookPrefix = "https://discord.com/api/webhooks/";
        private const int MaxLogBytes = 1024 * 1024, MaxDescription = 1500;
        private static string webhook;

        private static string Webhook
        {
            get
            {
                if (webhook == null)
                {
                    byte[] bytes = EmbeddedAssets.Bytes("bug-webhook.txt");
                    string url = bytes == null ? "" : Encoding.UTF8.GetString(bytes).Trim();
                    webhook = url.StartsWith(WebhookPrefix, StringComparison.Ordinal) ? url : "";
                }
                return webhook;
            }
        }

        public static void Send(string code, string description)
        {
            if (!Reports.Sending || Webhook.Length == 0 || !Reports.Instance) return;
            var post = new Post(code, description ?? "", Reports.Tag, Reports.RecentLines(0, out _));
            // Cleaning the log of names is slow with a long friends list: off the main thread.
            ThreadPool.QueueUserWorkItem(_ => post.Prepare());
            Reports.Instance.StartCoroutine(post.Run());
        }

        private sealed class Post
        {
            private readonly string code, description, tag;
            private readonly List<string> lines;
            private readonly string boundary = "bnmp" + Guid.NewGuid().ToString("N");
            private volatile bool ready;
            private byte[] body;

            public Post(string code, string description, string tag, List<string> lines)
            {
                this.code = code;
                this.description = description;
                this.tag = tag;
                this.lines = lines;
            }

            public void Prepare()
            {
                try
                {
                    string[] where = (tag ?? "").Split('|');
                    string Part(int i) => i < where.Length ? where[i] : "";
                    var content = new StringBuilder();
                    content.Append("**Bug report** `").Append(code).Append("` · ").Append(Part(0)).Append(" · ")
                        .Append(Plugin.Version).Append(' ').Append(ReleaseChannel.Name).Append(" · ").Append(Part(1));
                    if (Part(3).Length > 0) content.Append(" · island ").Append(Part(3));
                    if (Part(2).Length > 0) content.Append(" · ").Append(Part(2));
                    string said = Scrub.Clean(description.Trim());
                    if (said.Length > MaxDescription) said = said.Substring(0, MaxDescription) + "...";
                    content.Append('\n');
                    foreach (string line in (said.Length > 0 ? said : "(no description)").Split('\n'))
                        content.Append("> ").Append(line).Append('\n');
                    content.Append("Everyone in the lobby's logs: `python tools/fetch-incident.py ").Append(code).Append('`');

                    string payload = Json.Write(new Dictionary<string, object>
                    {
                        ["content"] = content.ToString(),
                        ["username"] = "Bad North bug report",
                        // Players' text never pings anyone.
                        ["allowed_mentions"] = new Dictionary<string, object> { ["parse"] = new List<object>() },
                    });

                    // The newest lines that fit.
                    var log = new List<string>();
                    int bytes = 0;
                    for (int i = lines.Count - 1; i >= 0; i--)
                    {
                        string line = Scrub.Clean(lines[i]);
                        bytes += Encoding.UTF8.GetByteCount(line) + 1;
                        if (bytes > MaxLogBytes) break;
                        log.Add(line);
                    }
                    log.Reverse();
                    string file = $"Bad North Multiplayer {Plugin.Version} ({ReleaseChannel.Name}), bug report {code}\n\n" +
                        string.Join("\n", log.ToArray()) + "\n";

                    var multipart = new StringBuilder();
                    multipart.Append("--").Append(boundary).Append("\r\n")
                        .Append("Content-Disposition: form-data; name=\"payload_json\"\r\n")
                        .Append("Content-Type: application/json\r\n\r\n").Append(payload).Append("\r\n")
                        .Append("--").Append(boundary).Append("\r\n")
                        .Append("Content-Disposition: form-data; name=\"files[0]\"; filename=\"").Append(code).Append("-log.txt\"\r\n")
                        .Append("Content-Type: text/plain; charset=utf-8\r\n\r\n").Append(file).Append("\r\n")
                        .Append("--").Append(boundary).Append("--\r\n");
                    body = Encoding.UTF8.GetBytes(multipart.ToString());
                }
                catch (Exception) { body = null; }
                finally { ready = true; }
            }

            public IEnumerator Run()
            {
                while (!ready) yield return null;
                if (body == null) yield break;
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    using (var request = new UnityWebRequest(Webhook, "POST"))
                    {
                        request.uploadHandler = new UploadHandlerRaw(body) { contentType = "multipart/form-data; boundary=" + boundary };
                        request.downloadHandler = new DownloadHandlerBuffer();
                        request.SetRequestHeader("Content-Type", "multipart/form-data; boundary=" + boundary);
                        request.timeout = 30;
                        yield return request.SendWebRequest();
                        long status = request.responseCode;
                        if (!request.isNetworkError && status >= 200 && status < 300)
                        {
                            ModLog.Info($"Bug report {code} posted to Discord");
                            yield break;
                        }
                        // Refused for what it is (not rate limiting): another try won't help.
                        if (!request.isNetworkError && status >= 400 && status < 500 && status != 429)
                        {
                            ModLog.Warn($"Bug report {code}: Discord refused it ({status})");
                            yield break;
                        }
                    }
                    float until = Time.unscaledTime + 5f * attempt;
                    while (Time.unscaledTime < until) yield return null;
                }
                ModLog.Warn($"Bug report {code}: couldn't reach Discord (it's in the error reports all the same)");
            }
        }
    }
}
