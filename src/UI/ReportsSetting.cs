using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense.UI;

namespace BadNorthMP.UI
{
    /// <summary>"Error reports" in the game's Settings, after Blood: on or off at any time.</summary>
    [HarmonyPatch(typeof(UserSettingsMenu), "Initialize")]
    internal static class ReportsSetting
    {
        private static void Postfix(UserSettingsMenu __instance)
        {
            try
            {
                var category = Traverse.Create(__instance).Field<Transform>("categoryGraphics").Value;
                BoolWidget widget = __instance.AddBoolWidget("SETTINGS/BLOOD", () => ModConfig.SendReports.Value, on =>
                {
                    ReportsUi.Choose(on);
                    return true;
                }, category);
                GameUi.SetPlainLabel(widget, "Error reports");
                widget.SetSuccessAudio(FabricID.settingChange);
            }
            catch (System.Exception e) { ModLog.Warn("Couldn't add Error reports to the settings: " + e.Message); }
        }
    }
}
