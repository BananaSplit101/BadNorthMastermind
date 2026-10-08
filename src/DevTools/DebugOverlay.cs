using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthMP.DevTools
{
    /// <summary>F7 toggles a panel with recent mod log lines and the selected squad.</summary>
    internal class DebugOverlay : MonoBehaviour
    {
        private const float StatusRefreshSeconds = 0.5f;

        private bool visible;
        private string status = "";
        private float nextStatusRefresh;
        private GUIStyle style;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F7))
                visible = !visible;

            // Singleton lookups fall back to FindObjectOfType when nothing is loaded, so don't do this every frame.
            if (visible && Time.unscaledTime >= nextStatusRefresh)
            {
                nextStatusRefresh = Time.unscaledTime + StatusRefreshSeconds;
                status = BuildStatus();
            }
        }

        private static string BuildStatus()
        {
            Island island = GameAccess.CurrentIsland;
            if (!island)
                return "No island";
            return $"Island {island.state} | squads {GameAccess.PlayerSquads.Count} | selected {GameAccess.Describe(EnglishSquad.selected)}";
        }

        private void OnGUI()
        {
            if (!visible)
                return;

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = 12,
                    wordWrap = true,
                };
            }

            string text = $"BadNorthMP {Plugin.Version}  [F7 hide  F8 move  F9 wave  F10 dump]\n{status}\n\n" +
                          string.Join("\n", new System.Collections.Generic.List<string>(ModLog.Recent).ToArray());
            GUI.Box(new Rect(10, 10, 620, 300), text, style);
        }
    }
}
