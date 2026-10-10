using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthMP.Patches
{
    /// <summary>
    /// An island reaches its mesh and texture pools through its campaign (levelNode.campaign.meshPool), and a
    /// campaign's teardown nulls them. Islands and boats that outlive their campaign then crash: a spectator's campaign
    /// reload aborted in island teardown (MeshMerger2), and boat wakes threw every frame (Wake.Start/LateUpdate). Such
    /// islands get stand-in pools instead, which are emptied now and then.
    /// </summary>
    internal static class OrphanIslandPools
    {
        private static MeshPool meshes;
        private static TexturePool textures;
        private static int handedOut;
        private static bool reported;

        private static GameObject Host()
        {
            var host = new GameObject("BNMP orphan island pools");
            Object.DontDestroyOnLoad(host);
            return host;
        }

        private static void Used()
        {
            if (!reported)
            {
                reported = true;
                ModLog.Info("An island outlived its campaign; using stand-in mesh and texture pools");
            }
            // Returned meshes and textures pile up here; nothing in use is ever in the pool, so clearing is safe.
            if (++handedOut % 200 != 0) return;
            if (meshes) meshes.ClearPool();
            if (textures) textures.ClearPool();
        }

        public static MeshPool Meshes()
        {
            if (!meshes) meshes = (textures ? textures.gameObject : Host()).AddComponent<MeshPool>();
            Used();
            return meshes;
        }

        public static TexturePool Textures()
        {
            if (!textures) textures = (meshes ? meshes.gameObject : Host()).AddComponent<TexturePool>();
            Used();
            return textures;
        }
    }

    [HarmonyPatch(typeof(Island), nameof(Island.meshPool), MethodType.Getter)]
    internal static class OrphanIslandMeshPoolPatch
    {
        private static bool Prefix(Island __instance, ref MeshPool __result)
        {
            LevelNode node = __instance.levelNode;
            var campaign = node ? node.campaign : null;
            __result = campaign && campaign.meshPool ? campaign.meshPool : OrphanIslandPools.Meshes();
            return false;
        }
    }

    [HarmonyPatch(typeof(Island), nameof(Island.texturePool), MethodType.Getter)]
    internal static class OrphanIslandTexturePoolPatch
    {
        private static bool Prefix(Island __instance, ref TexturePool __result)
        {
            LevelNode node = __instance.levelNode;
            var campaign = node ? node.campaign : null;
            __result = campaign && campaign.texturePool ? campaign.texturePool : OrphanIslandPools.Textures();
            return false;
        }
    }

    /// <summary>A squad with no soldiers left has nothing to update (the game's Update reads its first soldier).</summary>
    [HarmonyPatch(typeof(Squad), nameof(Squad.Update))]
    internal static class EmptySquadUpdatePatch
    {
        private static bool Prefix(Squad __instance) => __instance.agents != null && __instance.agents.Count > 0;
    }

    /// <summary>
    /// A squad whose last soldier goes while assigned to a house (a mirrored squad at island teardown, or one whose
    /// pather is already gone) made SquadDead throw. Do the same bookkeeping without the null pather.
    /// </summary>
    [HarmonyPatch(typeof(SquadReplenishLocation), nameof(SquadReplenishLocation.SquadDead))]
    internal static class ReplenishSquadDeadPatch
    {
        private static bool Prefix(SquadReplenishLocation __instance)
        {
            EnglishSquad squad = __instance.currentSquad;
            if (squad && squad.pather) return true;
            __instance.StopAllCoroutines();
            Traverse fields = Traverse.Create(__instance);
            fields.Property("squadExiting").SetValue(false);
            fields.Property("squadHealing").SetValue(false);
            fields.Property("currentSquad").SetValue(null);
            return false;
        }
    }
}
