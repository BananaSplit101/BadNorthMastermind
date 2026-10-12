using HarmonyLib;
using Voxels.TowerDefense;

namespace BadNorthMP.Game
{
    // The game takes it that only the English kill Vikings: a Viking felled by anyone else (the mastermind's own side,
    // say) threw on dying, over and over, and stopped every unit after it from moving, so the waves sat on their boats.
    // Such a kill is credited to no one instead.
    [HarmonyPatch(typeof(VikingAgent), nameof(VikingAgent.ModifyAttack))]
    internal static class VikingKillerPatch
    {
        private static void Postfix(ref Squad ___killerSquad)
        {
            if ((object)___killerSquad != null && !(___killerSquad is EnglishSquad)) ___killerSquad = null;
        }
    }
}
