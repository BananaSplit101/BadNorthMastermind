using System.Text;
using BadNorthMP.Game;
using UnityEngine;
using UnityEngine.UI;
using Voxels.TowerDefense.ProfileInternals;
using Voxels.TowerDefense.UI;

namespace BadNorthMP.UI
{
    /// <summary>
    /// Multiplayer > Play: the defender's multiplayer campaigns (separate save slots). Continue one, delete
    /// one, or start a new one through the game's own new-campaign popup.
    /// </summary>
    internal class CampaignScreen : MonoBehaviour
    {
        private static CampaignScreen instance;

        private ModalOverlay overlay;
        private CampaignSaveMeta confirmDelete;
        private float confirmUntil;
        private bool loading;

        private void Awake() => instance = this;

        public static void Open()
        {
            if (!instance)
                return;
            if (!MultiplayerCampaigns.Loaded)
                instance.StartCoroutine(instance.LoadThenShow());
            else
                instance.Show();
        }

        private System.Collections.IEnumerator LoadThenShow()
        {
            if (loading)
                yield break;
            loading = true;
            yield return StartCoroutine(MultiplayerCampaigns.LoadHeaders());
            loading = false;
            Show();
        }

        private void Rebuild()
        {
            if (overlay)
            {
                ModalOverlay old = overlay;
                overlay = null;
                old.Dismiss();
            }
            Show();
        }

        private void Show()
        {
            ModalOverlay popup = ModalOverlay.GetInstance();
            overlay = popup;
            GameUi.InitPopup(popup, "Multiplayer campaign", BuildBody());

            foreach (CampaignSaveMeta save in MultiplayerCampaigns.Saves)
            {
                if (save == null)
                    continue;
                CampaignSaveMeta target = save;
                string name = DisplayName(save);
                GameUi.AddPopupButton(popup, $"Continue {name}", () => MultiplayerCampaigns.Continue(target), closes: true);
            }
            if (MultiplayerCampaigns.FreeSlot >= 0)
                GameUi.AddPopupButton(popup, "New campaign", MultiplayerCampaigns.StartNew, closes: true);
            foreach (CampaignSaveMeta save in MultiplayerCampaigns.Saves)
            {
                if (save == null)
                    continue;
                CampaignSaveMeta target = save;
                bool confirming = confirmDelete == save && Time.unscaledTime < confirmUntil;
                GameUi.AddPopupButton(popup, confirming ? $"Really delete {DisplayName(save)}?" : $"Delete {DisplayName(save)}",
                    () => Delete(target));
            }

            popup.AddCancelButton();
            popup.SetCloseAction(() =>
            {
                if (overlay == popup)
                    overlay = null;
            });
        }

        private void Delete(CampaignSaveMeta save)
        {
            if (confirmDelete == save && Time.unscaledTime < confirmUntil)
            {
                confirmDelete = null;
                MultiplayerCampaigns.Delete(save);
                MultiplayerCampaigns.Saves[save.saveSlot - MultiplayerCampaigns.FirstSlot] = null;
            }
            else
            {
                confirmDelete = save;
                confirmUntil = Time.unscaledTime + 4f;
            }
            Rebuild();
        }

        private static string DisplayName(CampaignSaveMeta save) => $"Campaign {save.saveSlot - MultiplayerCampaigns.FirstSlot + 1}";

        private static string BuildBody()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Multiplayer campaigns are saved separately from your single-player campaigns. You play the islands; your mastermind sends the Vikings.");
            sb.AppendLine();
            bool any = false;
            foreach (CampaignSaveMeta save in MultiplayerCampaigns.Saves)
            {
                if (save == null)
                    continue;
                any = true;
                string state = save.gameOver ? "lost" : $"{Mathf.RoundToInt(save.campaignFraction * 100f)}% complete";
                sb.AppendLine($"{DisplayName(save)}: {save.prefs.difficulty}, {state}, saved {save.savedTime:d MMM HH:mm}");
            }
            if (!any)
                sb.AppendLine("No multiplayer campaigns yet.");
            if (MultiplayerCampaigns.FreeSlot < 0)
                sb.AppendLine().Append("All 5 multiplayer save slots are used: delete one to start a new campaign.");
            return sb.ToString();
        }
    }
}
