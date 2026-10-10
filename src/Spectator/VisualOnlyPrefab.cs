using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthMP.Spectator
{
    internal static class VisualOnlyPrefab
    {
        private static readonly FieldInfo Owner = AccessTools.Field(typeof(SelfPoolingPrefab), "owner");
        private static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        private static Transform staging;

        public static void Reset()
        {
            prefabs.Clear();
            if (staging) UnityEngine.Object.Destroy(staging.gameObject);
            staging = null;
        }

        public static GameObject Find(string name)
        {
            if (prefabs.TryGetValue(name, out GameObject found) && found) return found;
            foreach (SelfPoolingPrefab candidate in Resources.FindObjectsOfTypeAll<SelfPoolingPrefab>())
                if (candidate && candidate.name == name && !candidate.GetComponentInParent<Agent>())
                { found = candidate.gameObject; break; }
            if (!found)
                foreach (PoolablePrefab candidate in Resources.FindObjectsOfTypeAll<PoolablePrefab>())
                    if (candidate && candidate.name == name && !candidate.GetComponentInParent<Agent>())
                    { found = candidate.gameObject; break; }
            if (found) prefabs[name] = found;
            return found;
        }

        public static GameObject Clone(GameObject prefab, Transform parent)
        {
            if (!staging)
            {
                var go = new GameObject("BNMP Visual Staging");
                go.SetActive(false); staging = go.transform;
            }
            // Clone inactive: pool OnEnable/OnDisable cannot touch the source's owner/container.
            GameObject clone = UnityEngine.Object.Instantiate(prefab, staging);
            foreach (SelfPoolingPrefab pooled in clone.GetComponentsInChildren<SelfPoolingPrefab>(true))
                Owner.SetValue(pooled, null);
            foreach (MonoBehaviour behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(behaviour is Voxels.TowerDefense.SpriteMagic.BatchedSprite)) behaviour.enabled = false;
            foreach (Collider collider in clone.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (Rigidbody body in clone.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.detectCollisions = false; }
            foreach (Animator animator in clone.GetComponentsInChildren<Animator>(true)) Freeze(animator);
            clone.SetActive(false);
            clone.transform.SetParent(parent, false);
            ExtraRenderers.InitializeAll(clone.transform);
            return clone;
        }

        public static void Freeze(Animator animator)
        {
            if (!animator) return;
            animator.fireEvents = false; animator.applyRootMotion = false;
            animator.speed = 0; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        public static void PrepareAgent(Agent agent)
        {
            foreach (Animator animator in agent.GetComponentsInChildren<Animator>(true)) Freeze(animator);
            // FlagPole.Update and Spear.LateUpdate otherwise overwrite the authoritative child pose.
            foreach (AgentComponent behaviour in agent.GetComponentsInChildren<AgentComponent>(true))
            {
                // These only render and animate cloth; disabling them leaves the flag mesh empty.
                string type = behaviour.GetType().Name;
                behaviour.enabled = type == "FlagMesh" || type == "FlagPhysics";
            }
            foreach (Collider collider in agent.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (Rigidbody body in agent.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.detectCollisions = false; }
        }
    }

    // Unity SendMessage animation events can resolve a private component method named Throw directly.
    // Disabling Agent.Throw alone cannot block Arsonist.Throw/SpawnTorch or ThrowComponent.Throw.
    [HarmonyPatch]
    internal static class SpectatorNativeVisualEventPatch
    {
        internal static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Arsonist), "SpawnTorch");
            yield return AccessTools.Method(typeof(Arsonist), "Throw");
            yield return AccessTools.Method(typeof(ThrowComponent), "SpawnTorch");
            yield return AccessTools.Method(typeof(ThrowComponent), "Throw");
            yield return AccessTools.Method(typeof(AxeThrowing), "ThrowAxe");
        }
        private static bool Prefix() => !SpectatorSession.Active;
    }

    [HarmonyPatch(typeof(Torch), nameof(Torch.Shoot))]
    internal static class SpectatorTorchShootGuard
    {
        private static bool Prefix() => !SpectatorSession.Active;
    }
}
