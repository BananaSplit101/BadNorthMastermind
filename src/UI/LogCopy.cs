using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace BadNorthMP.UI
{
    /// <summary>The pause menu's "Copy log": the whole BepInEx log on the clipboard, to paste into a bug report.</summary>
    internal static class LogCopy
    {
        // Far more than any session writes; a runaway log keeps its start (versions, setup) and its latest part.
        private const int MaxChars = 3000000, HeadChars = 20000;

        public static bool Copy()
        {
            try
            {
                // Lines still waiting in the writer go to the file first.
                foreach (ILogListener listener in BepInEx.Logging.Logger.Listeners)
                    if (listener is DiskLogListener disk) disk.LogWriter?.Flush();
                string path = Path.Combine(Paths.BepInExRootPath, "LogOutput.log");
                string text;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                    text = reader.ReadToEnd();
                if (text.Length > MaxChars)
                    text = text.Substring(0, HeadChars) + "\n...\n" + text.Substring(text.Length - (MaxChars - HeadChars));
                GUIUtility.systemCopyBuffer = text;
                ModLog.Info($"Copied the log to the clipboard ({text.Length / 1024} KB)");
                return true;
            }
            catch (Exception e)
            {
                ModLog.Warn("Couldn't copy the log: " + e.Message);
                return false;
            }
        }
    }
}
