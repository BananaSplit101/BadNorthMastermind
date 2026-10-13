using System;
using System.Collections;
using BadNorthMP.Telemetry;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Voxels.TowerDefense.UI;

namespace BadNorthMP.UI
{
    /// <summary>Error reports in the game's own popups: the one-time notice, the on/off switch, and Report bug.</summary>
    internal static class ReportsUi
    {
        private static bool asking;

        public static bool NoticePending => ModConfig.ReportsNoticeShown != null && !ModConfig.ReportsNoticeShown.Value;

        /// <summary>
        /// The first time: shows the notice instead (true), and runs <paramref name="then"/> once the player has
        /// answered. Otherwise false: carry on. If the popup can't open, false too, so it's never in the way of the
        /// button pressed (the next place that asks shows it).
        /// </summary>
        public static bool Ask(Action then)
        {
            if (!NoticePending) return false;
            if (asking) return true;
            try
            {
                ModalOverlay popup = ModalOverlay.GetInstance();
                GameUi.InitPopup(popup, "Hey!", "By the way we collect error logs automatically. You can turn this off here or leave it on. Up to you!");
                bool answered = false;
                GameUi.AddPopupButton(popup, "Leave it on", () => { answered = true; Choose(true); }, closes: true);
                GameUi.AddPopupButton(popup, "Turn off", () => { answered = true; Choose(false); }, closes: true);
                popup.SetCloseAction(() =>
                {
                    asking = false;
                    if (answered && then != null) Later(then);
                });
                asking = true;
                return true;
            }
            catch (Exception e)
            {
                ModLog.Warn("Couldn't show the error reports notice here: " + e.Message);
                return false;
            }
        }

        public static void Choose(bool send)
        {
            ModConfig.SendReports.Value = send;
            ModConfig.ReportsNoticeShown.Value = true;
            if (!send) Reports.DiscardUnsent();
            ModLog.Info(send ? "Error reports: on" : "Error reports: off (nothing is sent)");
        }

        public static string SwitchLabel => "Error reports: " + (ModConfig.SendReports.Value ? "On" : "Off");

        public static void Switch() => Choose(!ModConfig.SendReports.Value);

        // ---- Report bug ----------------------------------------------------------------------------------------------

        /// <summary>What happened, in the player's words, sent with everyone in the lobby's logs.</summary>
        public static bool OpenBugReport()
        {
            if (Ask(() => OpenBugReport())) return true;
            ModalOverlay popup = ModalOverlay.GetInstance();
            if (!Reports.Collecting)
            {
                GameUi.InitPopup(popup, "Report bug", "Error reports are turned off, so a bug report can't be sent. " +
                    "Turn them on in the Multiplayer screen (Error reports: Off), or use Copy log in the pause menu and send the log yourself.");
                popup.AddCancelButton();
                return true;
            }
            GameUi.InitPopup(popup, "Report bug", "What happened? Your log goes with it, and everyone in your lobby sends theirs too.");
            InputField field = Traverse.Create(popup).Field<InputField>("inputField").Value;
            if (field)
            {
                field.gameObject.SetActive(true);
                field.text = "";
                field.characterLimit = 500;
                if (field.placeholder is Text placeholder) placeholder.text = "Type here (optional)";
                field.Select();
                field.ActivateInputField();
            }
            GameUi.AddPopupButton(popup, "Send", () =>
            {
                string text = field ? field.text : "";
                Later(() => Sent(Incidents.ReportBug(text)));
            }, closes: true);
            popup.AddCancelButton();
            return true;
        }

        private static void Sent(string code)
        {
            ModalOverlay popup = ModalOverlay.GetInstance();
            if (code == null)
            {
                GameUi.InitPopup(popup, "Report bug", "You've just sent one: wait a few seconds and try again.");
            }
            else
            {
                try { GUIUtility.systemCopyBuffer = code; } catch (Exception) { }
                GameUi.InitPopup(popup, "Bug report sent", $"Your code is {code} (copied to your clipboard). Tell the developer this code.");
            }
            GameUi.AddPopupButton(popup, "OK", () => { }, closes: true);
        }

        /// <summary>A moment later, once the closing popup has gone (opening another inside its close confuses the menus).</summary>
        private static void Later(Action action)
        {
            if (Reports.Instance) Reports.Instance.StartCoroutine(AfterFrames(action));
            else action();
        }

        private static IEnumerator AfterFrames(Action action)
        {
            yield return null;
            yield return null;
            action();
        }
    }
}
