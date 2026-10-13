using System;
using System.Collections;
using BadNorthMP.Telemetry;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Voxels.TowerDefense.UI;

namespace BadNorthMP.UI
{
    /// <summary>Error reports in the game's own popups: the one-time notice (on the title screen's Start) and Report bug.</summary>
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

        // ---- Report bug ----------------------------------------------------------------------------------------------

        /// <summary>What happened, in the player's words, sent with everyone in the lobby's logs.</summary>
        public static bool OpenBugReport()
        {
            if (Ask(() => OpenBugReport())) return true;
            ModalOverlay popup = ModalOverlay.GetInstance();
            // A bug report always takes the player's log, error reports on or off: sending it is their choice.
            Text message = GameUi.InitPopup(popup, "Report bug", "What happened? Write a short description of the bug. Your log goes with it" +
                (Reports.Collecting ? ", and everyone in your lobby sends theirs too." : " (error reports are off, so only this report is sent)."));
            InputField field = Traverse.Create(popup).Field<InputField>("inputField").Value;
            Text placeholder = field ? field.placeholder as Text : null;
            if (field)
            {
                field.gameObject.SetActive(true);
                field.text = "";
                field.characterLimit = 500;
                if (placeholder) placeholder.text = "Describe what happened";
                field.Select();
                field.ActivateInputField();
            }
            GameUi.AddPopupButton(popup, "Send", () =>
            {
                string text = field ? field.text.Trim() : "";
                // Nothing to go on without a description: ask again (unless the game's text box is missing).
                if (field && text.Length < MinDescription)
                {
                    FabricWrapper.PostEvent(FabricID.uiError);
                    if (placeholder) placeholder.text = "Write a short description first";
                    if (message) message.text = "Please write a few words about what happened, then press Send.";
                    field.Select();
                    field.ActivateInputField();
                    return;
                }
                popup.Dismiss();
                Later(() => Sent(Incidents.ReportBug(text)));
            });
            popup.AddCancelButton();
            return true;
        }

        private const int MinDescription = 5;

        // The code is for the developer (it's in the report): the player only needs to know it went.
        private static void Sent(string code)
        {
            ModalOverlay popup = ModalOverlay.GetInstance();
            if (code == null) GameUi.InitPopup(popup, "Report bug", "You've just sent one: wait a few seconds and try again.");
            else GameUi.InitPopup(popup, "Bug report sent", "Thanks! Your bug report was sent.");
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
