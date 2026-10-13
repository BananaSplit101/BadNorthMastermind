using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthMP.Game
{
    // Viking commander traits and their flag, ported from GMW's "Viking Commander" 1.1.0 (used with permission).
    // Here only the mastermind's commanders carry them: one Viking holds the trait's flag, and the whole squad has
    // the trait. Effects run on the campaign leader; everyone sees the flag (VikingFlag) and the size change.

    internal enum VikingTrait : byte { None, Giant, Heavy, Iron, Medic, Sharp, Speed, Sturdy, Thorns, Short, Sick, Slash }

    internal static class VikingTraits
    {
        public static VikingTrait Random() => (VikingTrait)UnityEngine.Random.Range(1, EconomyConfig.VikingTraitCount + 1);

        public static string Name(int trait) => trait > 0 && trait <= EconomyConfig.VikingTraitCount ? ((VikingTrait)trait).ToString() : "";

        /// <summary>What a trait does, at normal strength, in a few words.</summary>
        public static string Describe(int trait)
        {
            switch ((VikingTrait)trait)
            {
                case VikingTrait.Giant: return "bigger and tougher, hits harder, a little slower";
                case VikingTrait.Heavy: return "blows knock back and stun more";
                case VikingTrait.Iron: return "takes a third less damage";
                case VikingTrait.Medic: return "heals over time";
                case VikingTrait.Sharp: return "hits harder, knocks back less";
                case VikingTrait.Speed: return "runs much faster";
                case VikingTrait.Sturdy: return "hard to knock back or stun";
                case VikingTrait.Thorns: return "hurts whoever hits it";
                case VikingTrait.Short: return "small and very quick";
                case VikingTrait.Sick: return "weaker and slower";
                case VikingTrait.Slash: return "blows cleave into nearby enemies";
                default: return "";
            }
        }

        /// <summary>Scale a multiplier's distance from 1 by the configured strength (never below 0.1).</summary>
        public static float Multiplier(float baseMultiplier) => Mathf.Max(0.1f, 1f + (baseMultiplier - 1f) * ModConfig.TraitFactor());

        public static float BaseValue(float value) => value * ModConfig.TraitFactor();

        /// <summary>Leader: the squad gets the trait, one random Viking carries the flag.</summary>
        public static void Apply(Squad squad, VikingTrait trait)
        {
            if (!squad || trait == VikingTrait.None || squad.agents.Count == 0) return;
            Agent bearer = squad.agents[UnityEngine.Random.Range(0, squad.agents.Count)];
            foreach (Agent agent in squad.agents)
            {
                if (!agent || agent.GetComponent<VikingTraitController>()) continue;
                agent.gameObject.AddComponent<VikingTraitController>().Initialize(trait, agent == bearer);
            }
            if (bearer) VikingFlag.Attach(bearer, trait);
        }

        public static Agent FlagBearer(Squad squad)
        {
            if (!squad) return null;
            foreach (Agent agent in squad.agents)
            {
                var controller = agent ? agent.GetComponent<VikingTraitController>() : null;
                if (controller && controller.isCommander) return agent;
            }
            return null;
        }
    }

    /// <summary>Leader side: a trait's stat changes on one Viking, applied once it has spawned (spawning sets health).</summary>
    internal sealed class VikingTraitController : MonoBehaviour
    {
        public VikingTrait trait;
        public bool isCommander;
        private Agent agent;
        private bool applied;

        public void Initialize(VikingTrait newTrait, bool commander)
        {
            trait = newTrait;
            isCommander = commander;
            agent = GetComponent<Agent>();
        }

        private void Update()
        {
            if (!agent) return;
            if (!applied)
            {
                if (agent.spawned == null || !agent.spawned.active) return;
                applied = true;
                ApplyBaseStats();
            }
            if (trait == VikingTrait.Medic) HealOverTime(VikingTraits.BaseValue(0.175f));
        }

        private void ApplyBaseStats()
        {
            switch (trait)
            {
                case VikingTrait.Giant:
                    agent.transform.localScale *= VikingTraits.Multiplier(1.2f);
                    ScaleHealth(VikingTraits.Multiplier(1.25f));
                    MultiplyWalkSpeed(VikingTraits.Multiplier(0.95f));
                    break;
                case VikingTrait.Speed:
                    MultiplyWalkSpeed(VikingTraits.Multiplier(1.3f));
                    break;
                case VikingTrait.Medic:
                case VikingTrait.Thorns:
                    ScaleHealth(VikingTraits.Multiplier(1.1f));
                    break;
                case VikingTrait.Short:
                    // The original shrinks the whole ship; here the Vikings themselves are small.
                    agent.transform.localScale *= VikingTraits.Multiplier(0.8f);
                    MultiplyWalkSpeed(VikingTraits.Multiplier(1.4f));
                    break;
                case VikingTrait.Sick:
                    MultiplyWalkSpeed(VikingTraits.Multiplier(0.9f));
                    break;
            }
        }

        private void ScaleHealth(float multiplier)
        {
            agent.maxHealth *= multiplier;
            agent.health *= multiplier;
        }

        private void MultiplyWalkSpeed(float multiplier)
        {
            foreach (Component component in agent.GetComponents<Component>())
            {
                if (!component) continue;
                FieldInfo field = component.GetType().GetField("walkSpeed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(float))
                    field.SetValue(component, (float)field.GetValue(component) * multiplier);
            }
        }

        private void HealOverTime(float fractionPerSecond)
        {
            if (agent.health <= 0f || agent.health >= agent.maxHealth) return;
            agent.health = Mathf.Min(agent.maxHealth, agent.health + agent.maxHealth * fractionPerSecond * Time.deltaTime);
        }
    }

    /// <summary>Trait effects on blows given and taken (only the leader simulates fights).</summary>
    [HarmonyPatch(typeof(Agent), nameof(Agent.DealDamage))]
    internal static class VikingTraitDamagePatch
    {
        private static bool slashing;

        private static void Prefix(Agent __instance, ref Attack attack)
        {
            var defender = __instance.GetComponent<VikingTraitController>();
            Agent attackerAgent = attack.monoAttacker as Agent;
            var attacker = attackerAgent ? attackerAgent.GetComponent<VikingTraitController>() : null;
            if (defender)
            {
                if (defender.trait == VikingTrait.Iron) attack.damage *= VikingTraits.Multiplier(0.65f);
                if (defender.trait == VikingTrait.Giant || defender.trait == VikingTrait.Sturdy)
                {
                    attack.knockback *= VikingTraits.Multiplier(0.7f);
                    attack.stun *= VikingTraits.Multiplier(0.7f);
                }
            }
            if (attacker)
            {
                switch (attacker.trait)
                {
                    case VikingTrait.Giant:
                        attack.damage *= VikingTraits.Multiplier(1.15f);
                        attack.knockback *= VikingTraits.Multiplier(1.2f);
                        attack.stun *= VikingTraits.Multiplier(1.2f);
                        break;
                    case VikingTrait.Sharp:
                        attack.damage *= VikingTraits.Multiplier(1.15f);
                        attack.knockback *= VikingTraits.Multiplier(0.85f);
                        attack.stun *= VikingTraits.Multiplier(0.85f);
                        break;
                    case VikingTrait.Sick:
                        attack.damage *= VikingTraits.Multiplier(0.85f);
                        break;
                    case VikingTrait.Heavy:
                        attack.knockback *= VikingTraits.Multiplier(1.3f);
                        attack.stun *= VikingTraits.Multiplier(1.3f);
                        break;
                    case VikingTrait.Slash:
                        if (!slashing) Cleave(__instance, attackerAgent, attack);
                        break;
                }
            }
            if (defender && defender.trait == VikingTrait.Thorns && attackerAgent)
            {
                var thorns = new Attack(attack.damage * VikingTraits.BaseValue(0.07f), 0f, 0f, -attack.direction,
                    attackerAgent.transform.position, __instance, __instance.squad, attack.soundPrefix);
                attackerAgent.DealDamage(thorns);
            }
        }

        /// <summary>Slash: part of the blow lands on up to two more of the victim's squad nearby.</summary>
        private static void Cleave(Agent victim, Agent attacker, Attack attack)
        {
            if (!victim.squad || victim.squad.agents == null) return;
            slashing = true;
            try
            {
                float damage = attack.damage * VikingTraits.BaseValue(0.4f);
                int hit = 0;
                foreach (Agent other in victim.squad.agents.ToArray())
                {
                    if (!other || other == victim || other.health <= 0f ||
                        Vector3.Distance(victim.transform.position, other.transform.position) > 1.5f) continue;
                    other.DealDamage(new Attack(damage, attack.knockback * 0.5f, attack.launchImpulse, attack.direction,
                        other.transform.position, attacker, attack.killerSquad, attack.soundPrefix, attack.effect));
                    if (++hit >= 2) break;
                }
            }
            finally { slashing = false; }
        }
    }

    /// <summary>
    /// The commander's flag: a pole and a waving cloth showing the trait's icon. It follows its bearer from outside
    /// the Viking's own hierarchy (which is mirrored to other players piece by piece), and goes when the bearer falls.
    /// </summary>
    internal sealed class VikingFlag : MonoBehaviour
    {
        private static readonly Vector3 Offset = new Vector3(0.05f, 0.15f, -0.03f);
        private Agent bearer;
        private GameObject visual;

        public static void Attach(Agent agent, VikingTrait trait)
        {
            if (!agent || trait == VikingTrait.None) return;
            var root = new GameObject("BNMP Viking flag");
            var flag = root.AddComponent<VikingFlag>();
            flag.bearer = agent;
            flag.Follow();
            // Shown only while the bearer is (a mirrored Viking stays hidden until its first position arrives).
            flag.visual = new GameObject("Visual");
            flag.visual.transform.SetParent(root.transform, false);

            var pole = new GameObject("Pole");
            pole.transform.SetParent(flag.visual.transform, false);
            pole.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            pole.AddComponent<PoleBillboard>();
            var poleSprite = new GameObject("Pole sprite");
            poleSprite.transform.SetParent(pole.transform, false);
            poleSprite.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            poleSprite.transform.localScale = new Vector3(1.25f, 1f, 1f);
            var poleRenderer = poleSprite.AddComponent<SpriteRenderer>();
            Texture2D poleTexture = EmbeddedAssets.Texture("Flagpole_0.png", FilterMode.Point);
            Shader sprites = Shader.Find("Sprites/Default");
            if (poleTexture)
            {
                poleRenderer.sprite = Sprite.Create(poleTexture, new Rect(0f, 0f, poleTexture.width, poleTexture.height), new Vector2(0f, 0.5f), 250f);
                if (sprites) poleRenderer.sharedMaterial = new Material(sprites) { mainTexture = poleTexture };
            }
            var cloth = flag.visual.AddComponent<VikingProceduralFlag>();
            cloth.poleRenderer = poleRenderer;
            cloth.flagTexture = FlagTexture(trait);
        }

        private void Follow()
        {
            Transform target = bearer.transform;
            transform.position = target.TransformPoint(Offset);
            transform.rotation = target.rotation;
            transform.localScale = target.lossyScale;
        }

        private void LateUpdate()
        {
            if (!bearer || (bearer.deadState != null && bearer.deadState.active))
            {
                Destroy(gameObject);
                return;
            }
            bool shown = bearer.gameObject.activeInHierarchy;
            if (visual.activeSelf != shown) visual.SetActive(shown);
            if (shown) Follow();
        }

        private static readonly System.Collections.Generic.Dictionary<VikingTrait, Texture2D> flags =
            new System.Collections.Generic.Dictionary<VikingTrait, Texture2D>();

        /// <summary>A black-to-red banner with the trait's icon in the middle (cached per trait).</summary>
        private static Texture2D FlagTexture(VikingTrait trait)
        {
            if (flags.TryGetValue(trait, out Texture2D cached) && cached) return cached;
            Texture2D icon = EmbeddedAssets.Texture("vktrait_" + trait.ToString().ToLower() + ".png");
            const int width = 256, height = 128;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var red = new Color(0.75f, 0.15f, 0.15f, 1f);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    texture.SetPixel(x, y, Color.Lerp(Color.black, red, Mathf.Clamp01(x / (width * 0.75f))));
            if (icon)
            {
                int iconHeight = 150;
                int iconWidth = icon.width * iconHeight / icon.height;
                int left = (width - iconWidth) / 2, bottom = (height - iconHeight) / 2;
                for (int y = 0; y < iconHeight; y++)
                {
                    int py = bottom + y;
                    if (py < 0 || py >= height) continue;
                    for (int x = 0; x < iconWidth; x++)
                    {
                        int px = left + x;
                        if (px < 0 || px >= width) continue;
                        Color pixel = icon.GetPixelBilinear((float)x / iconWidth, (float)y / iconHeight);
                        texture.SetPixel(px, py, Color.Lerp(texture.GetPixel(px, py), pixel, pixel.a));
                    }
                }
            }
            texture.Apply();
            flags[trait] = texture;
            return texture;
        }
    }

    /// <summary>Keeps the pole sprite facing the camera around the pole's own axis.</summary>
    internal sealed class PoleBillboard : MonoBehaviour
    {
        private void LateUpdate()
        {
            Camera camera = Camera.main;
            if (!camera || !transform.parent) return;
            Vector3 up = transform.parent.up;
            Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, up).normalized;
            if (forward != Vector3.zero) transform.rotation = Quaternion.LookRotation(forward, up);
        }
    }

    /// <summary>A small cloth mesh that ripples in the wind (12 segments).</summary>
    internal sealed class VikingProceduralFlag : MonoBehaviour
    {
        public Texture2D flagTexture;
        public SpriteRenderer poleRenderer;
        private MeshRenderer meshRenderer;
        private Mesh cloth;
        private readonly Vector3[] baseVerts = new Vector3[26];
        private readonly Vector3[] verts = new Vector3[26];
        private readonly Vector2[] uvs = new Vector2[26];
        private readonly int[] tris = new int[144];

        private void Start()
        {
            var go = new GameObject("Cloth");
            go.transform.SetParent(transform, false);
            cloth = new Mesh();
            cloth.MarkDynamic();
            const float length = 0.6f, depth = 0.3f, top = 0.6f;
            for (int i = 0; i < 13; i++)
            {
                float z = -(i / 12f) * length;
                baseVerts[i * 2] = new Vector3(0f, top, z);
                baseVerts[i * 2 + 1] = new Vector3(0f, top - depth, z);
                uvs[i * 2] = new Vector2(i / 12f, 1f);
                uvs[i * 2 + 1] = new Vector2(i / 12f, 0f);
            }
            for (int j = 0; j < 12; j++)
            {
                int t = j * 12, v = j * 2;
                // Both faces, so the cloth shows from either side.
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                tris[t + 3] = v + 2; tris[t + 4] = v + 1; tris[t + 5] = v + 3;
                tris[t + 6] = v; tris[t + 7] = v + 2; tris[t + 8] = v + 1;
                tris[t + 9] = v + 2; tris[t + 10] = v + 3; tris[t + 11] = v + 1;
            }
            cloth.vertices = baseVerts;
            cloth.triangles = tris;
            cloth.uv = uvs;
            cloth.RecalculateNormals();
            cloth.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = cloth;
            meshRenderer = go.AddComponent<MeshRenderer>();
            Shader sprites = Shader.Find("Sprites/Default");
            if (sprites)
            {
                var material = new Material(sprites);
                if (flagTexture) material.mainTexture = flagTexture;
                meshRenderer.sharedMaterial = material;
            }
        }

        private void Update()
        {
            if (!cloth) return;
            for (int i = 0; i < 13; i++)
            {
                float wave = Mathf.Sin(Time.time * 8f - i * 0.5f) * 0.08f * (i / 12f);
                verts[i * 2] = baseVerts[i * 2] + new Vector3(wave, 0f, 0f);
                verts[i * 2 + 1] = baseVerts[i * 2 + 1] + new Vector3(wave, 0f, 0f);
            }
            cloth.vertices = verts;
            cloth.RecalculateNormals();
            cloth.RecalculateBounds();
            Camera camera = Camera.main;
            if (camera && poleRenderer && meshRenderer)
            {
                // Draw the cloth in front of the pole or behind it, depending on which side the camera is.
                float facing = Vector3.Dot((transform.position - camera.transform.position).normalized, transform.forward);
                if (facing > 0.05f) { meshRenderer.sortingOrder = 1; poleRenderer.sortingOrder = 0; }
                else if (facing < -0.05f) { meshRenderer.sortingOrder = 0; poleRenderer.sortingOrder = 1; }
            }
        }

        private void OnDestroy()
        {
            if (cloth) Destroy(cloth);
        }
    }
}
