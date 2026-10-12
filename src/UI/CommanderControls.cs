using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.SpriteMagic;

namespace BadNorthMP.UI
{
    // The mastermind commands Vikings with the game's own commanding look, the way GMW's Viking Invasion does for its
    // Viking ships: the tile grid an English squad gets, the game's path line to where a squad was sent, and the glow
    // and tint a squad gets under the mouse and while picked. Only the mastermind's screen turns any of it on.

    /// <summary>
    /// The game's own tile grid (NavigatorNavSpotPool: the tile outlines that show while an English squad is selected,
    /// with the tile under the mouse lit), lent to the mastermind: every tile while commanders take orders, only the
    /// shore tiles ships can land on while a landing is planned.
    /// </summary>
    internal static class IslandGrid
    {
        public enum Mode { Off, All, Shore }

        private static readonly AccessTools.FieldRef<NavigatorNavSpotPool, Dictionary<NavSpot, TargetNavSpot>> Tiles =
            AccessTools.FieldRefAccess<NavigatorNavSpotPool, Dictionary<NavSpot, TargetNavSpot>>("dictionary");
        private static readonly AccessTools.FieldRef<NavigatorNavSpotPool, bool> Shown =
            AccessTools.FieldRefAccess<NavigatorNavSpotPool, bool>("visible");

        private static Mode mode;
        private static readonly HashSet<NavSpot> shore = new HashSet<NavSpot>();
        private static NavigatorNavSpotPool pool;
        private static float nextSearch;

        /// <summary>The grid is the mastermind's (IslandGridPatch, IslandGridHoverPatch).</summary>
        public static bool Active => mode != Mode.Off;

        /// <summary>The tile lit under the mouse while the grid is the mastermind's.</summary>
        public static NavSpot Hover { get; private set; }

        /// <summary>Each frame: which tiles show (<paramref name="landable"/> ones for Shore), and the one to light.</summary>
        public static void Show(Mode wanted, ICollection<NavSpot> landable, NavSpot hover)
        {
            NavigatorNavSpotPool grid = Pool();
            if (!grid) return;
            bool changed = wanted != mode || (wanted == Mode.Shore && !SameShore(landable));
            if (changed)
            {
                mode = wanted;
                shore.Clear();
                if (wanted == Mode.Shore && landable != null) shore.UnionWith(landable);
                grid.SetDirty(); // its LateUpdate applies it (or, Off, hides the grid again)
            }
            NavSpot next = wanted == Mode.Off ? null : hover;
            if (next != Hover || changed)
            {
                Hover = next;
                grid.SetHover(null); // IslandGridHoverPatch puts the mastermind's tile in (or, Off, just clears it)
            }
        }

        private static bool SameShore(ICollection<NavSpot> landable)
        {
            if (landable == null) return shore.Count == 0;
            if (landable.Count != shore.Count) return false;
            foreach (NavSpot spot in landable)
                if (!shore.Contains(spot)) return false;
            return true;
        }

        private static NavigatorNavSpotPool Pool()
        {
            if (pool) return pool;
            if (Time.unscaledTime < nextSearch) return null;
            nextSearch = Time.unscaledTime + 1f;
            return pool = Object.FindObjectOfType<NavigatorNavSpotPool>();
        }

        /// <summary>The grid's update while it's the mastermind's: the wanted tiles in (each flashing as it appears), the rest out.</summary>
        internal static void Apply(NavigatorNavSpotPool grid)
        {
            Dictionary<NavSpot, TargetNavSpot> tiles = Tiles(grid);
            if (tiles != null)
                foreach (KeyValuePair<NavSpot, TargetNavSpot> tile in tiles)
                {
                    if (!tile.Value) continue;
                    bool want = mode == Mode.All || shore.Contains(tile.Key);
                    bool showing = tile.Value.gameObject.activeSelf && tile.Value.visibilityFraction > 0f;
                    tile.Value.SetVisible(want);
                    if (want && !showing) tile.Value.DoFlash();
                }
            // So the game's own update hides them all again once the mastermind lets go of the grid.
            Shown(grid) = true;
        }
    }

    // While the grid is the mastermind's, its own update (which follows the selected English squad) stands aside...
    [HarmonyPatch(typeof(NavigatorNavSpotPool), nameof(NavigatorNavSpotPool.LateUpdate))]
    internal static class IslandGridPatch
    {
        private static bool Prefix(NavigatorNavSpotPool __instance)
        {
            if (!IslandGrid.Active) return true;
            IslandGrid.Apply(__instance);
            __instance.enabled = false;
            return false;
        }
    }

    // ...and the tile it lights is the mastermind's, whoever asks.
    [HarmonyPatch(typeof(NavigatorNavSpotPool), nameof(NavigatorNavSpotPool.SetHover), typeof(NavSpot))]
    internal static class IslandGridHoverPatch
    {
        private static void Prefix(ref NavSpot navSpot)
        {
            if (IslandGrid.Active) navSpot = IslandGrid.Hover;
        }
    }

    /// <summary>
    /// The game's own path line and target marker (copies of an EnglishPatherVisualizer's), for a commander sent to a
    /// tile: laid along the tile's own paths from where its (mirrored) Vikings were, shortening as they close in; bright
    /// while the commander is picked, an outline while another one is, gone once they're there and nobody is picked.
    /// The tile itself tints in the commander's colour, like a defender squad's target.
    /// </summary>
    internal sealed class CommanderPath : NavSpot.IHighlight
    {
        private const int Nodes = 256;
        private const float Step = 0.2f;
        private static readonly Vector3 Lift = new Vector3(0f, 0.05f, 0f);
        private static readonly int ColorId = Shader.PropertyToID("_Color"), FillId = Shader.PropertyToID("_Fill"),
            OutlineId = Shader.PropertyToID("_Outline"), ShadeId = Shader.PropertyToID("_Shade"),
            NavDistId = Shader.PropertyToID("_NavDist"), RadiusId = Shader.PropertyToID("_Radius");
        private static readonly AccessTools.FieldRef<EnglishPatherVisualizer, GameObject> PathField =
            AccessTools.FieldRefAccess<EnglishPatherVisualizer, GameObject>("path");
        private static readonly AccessTools.FieldRef<EnglishPatherVisualizer, GameObject> EndField =
            AccessTools.FieldRefAccess<EnglishPatherVisualizer, GameObject>("pathEnd");
        private static GameObject pathTemplate, endTemplate;
        private static float nextTemplateSearch;
        private static bool warned;
        private static int made;

        private readonly GameObject pathObject, endObject;
        private readonly MeshFilter pathFilter, endFilter;
        private readonly MeshRenderer pathRenderer, endRenderer;
        private readonly MaterialPropertyBlock pathBlock = new MaterialPropertyBlock(), endBlock = new MaterialPropertyBlock();
        private NavSpot target;
        private bool laid;
        private float navDistance, radius;
        private Vector2 alpha;
        private Color tint;

        Color NavSpot.IHighlight.highlightColor => tint;

        /// <summary>A path in <paramref name="color"/>, or null if the game's path line can't be found.</summary>
        public static CommanderPath Create(Color color)
        {
            if (!Templates()) return null;
            var path = new CommanderPath(Object.Instantiate(pathTemplate), Object.Instantiate(endTemplate), color);
            if (path.pathFilter && path.pathRenderer) return path;
            path.Destroy();
            Warn("Commander paths: the game's path line has no mesh to draw with");
            return null;
        }

        private CommanderPath(GameObject path, GameObject end, Color color)
        {
            pathObject = path;
            endObject = end;
            made++;
            pathObject.name = "BNMP Commander Path " + made;
            endObject.name = "BNMP Commander Path End " + made;
            pathObject.transform.SetParent(null, false);
            endObject.transform.SetParent(null, false);
            pathObject.transform.position = Vector3.zero;
            pathObject.transform.rotation = Quaternion.identity;
            pathObject.transform.localScale = Vector3.one;
            pathFilter = pathObject.GetComponent<MeshFilter>();
            pathRenderer = pathObject.GetComponent<MeshRenderer>();
            endFilter = endObject.GetComponentInChildren<MeshFilter>();
            endRenderer = endObject.GetComponentInChildren<MeshRenderer>();
            if (pathFilter) pathFilter.sharedMesh = NewMesh();
            Color shown = color * 1.2f;
            shown.a = 1f;
            pathBlock.SetColor(ColorId, shown);
            endBlock.SetColor(ColorId, shown);
            tint = new Color(shown.r, shown.g, shown.b, 0f);
            pathObject.SetActive(false);
            endObject.SetActive(false);
        }

        private static bool Templates()
        {
            if (pathTemplate && endTemplate) return true;
            if (Time.unscaledTime < nextTemplateSearch) return false;
            nextTemplateSearch = Time.unscaledTime + 3f;
            // One from a squad in play is set up as the game draws it; otherwise a prefab's will do.
            foreach (EnglishPatherVisualizer visualizer in Resources.FindObjectsOfTypeAll<EnglishPatherVisualizer>())
            {
                GameObject path = visualizer ? PathField(visualizer) : null, end = visualizer ? EndField(visualizer) : null;
                if (!path || !end) continue;
                pathTemplate = path;
                endTemplate = end;
                if (visualizer.gameObject.scene.IsValid()) break;
            }
            if (pathTemplate && endTemplate) return true;
            Warn("Commander paths: the game's path line wasn't found");
            return false;
        }

        private static void Warn(string text)
        {
            if (warned) return;
            warned = true;
            ModLog.Warn(text);
        }

        /// <summary>Sent to <paramref name="spot"/> (null: no longer sent anywhere).</summary>
        public void Aim(NavSpot spot)
        {
            if (spot == target) return;
            if (target) target.RemoveHighlight(this);
            target = spot;
            laid = false;
            navDistance = 0f;
            if (!target)
            {
                Hide();
                return;
            }
            target.AddHighlight(this);
            IPathTarget goal = target;
            if (endFilter) endFilter.sharedMesh = goal.endPointMesh;
            endObject.transform.position = goal.endPointPosition + Lift;
        }

        /// <summary>
        /// Each frame: <paramref name="from"/> is where its Vikings are (null if none are left), <paramref name="picked"/>
        /// whether it's taking orders, <paramref name="anyPicked"/> whether any commander is.
        /// </summary>
        public void Update(Island island, Vector3? from, bool picked, bool anyPicked)
        {
            if (!target || !from.HasValue || !island)
            {
                Hide();
                return;
            }
            float dt = Time.unscaledDeltaTime;
            IPathTarget goal = target;
            var navPos = new NavPos(island.navManager.navigationMesh, from.Value);
            if (navPos.valid)
            {
                // Laid once they're on the island (an order given at sea waits for the landing).
                if (!laid) { Lay(goal, navPos); laid = true; }
                float distance = goal.GetDistanceFrom(navPos);
                if (!float.IsNaN(distance) && distance < navDistance) navDistance = Mathf.Lerp(navDistance, distance, dt * 10f);
            }
            // EnglishPatherVisualizer's own fades.
            Vector2 state = picked ? new Vector2(1f, 0.8f) : anyPicked ? new Vector2(1f, 0f) : Vector2.zero;
            Vector2 far = new Vector2(1f, 0.2f) * ExtraMath.RemapValue(navDistance, 0.5f, 1f);
            alpha = Vector2.Lerp(alpha, ExtraMath.Screen(state, far), dt * 10f);
            radius = Mathf.Lerp(radius, picked ? 1f : 0f, dt * 10f);
            tint.a = alpha.y;
            bool showEnd = alpha.sqrMagnitude > 0.00001f, showPath = laid && navDistance > 0.01f;
            if (endObject.activeSelf != showEnd) endObject.SetActive(showEnd);
            if (pathObject.activeSelf != showPath) pathObject.SetActive(showPath);
            if (showEnd && endRenderer)
            {
                endBlock.SetFloat(OutlineId, alpha.x);
                endBlock.SetFloat(FillId, alpha.y);
                endBlock.SetFloat(ShadeId, alpha.y * 0.3f);
                endRenderer.SetPropertyBlock(endBlock);
            }
            if (showPath)
            {
                pathBlock.SetFloat(NavDistId, navDistance);
                pathBlock.SetFloat(FillId, alpha.x);
                pathBlock.SetFloat(ShadeId, alpha.x * 0.3f);
                pathBlock.SetFloat(RadiusId, radius);
                pathRenderer.SetPropertyBlock(pathBlock);
            }
        }

        /// <summary>
        /// The line's dots, every 0.2 along the tile's own path from <paramref name="from"/> (EnglishPatherVisualizer's
        /// OnNewTarget): each a quad, its second UV how far it still is from the tile (the shader hides those past
        /// _NavDist, so the line shortens as the Vikings close in).
        /// </summary>
        private void Lay(IPathTarget goal, NavPos from)
        {
            var vertices = new List<Vector3>(Nodes * 4);
            var corners = new List<Vector2>(Nodes * 4);
            var distances = new List<Vector2>(Nodes * 4);
            navDistance = goal.GetDistanceFrom(from);
            if (float.IsNaN(navDistance)) navDistance = 0f;
            NavPos navPos = from;
            float carried = 0f;
            int count = 0;
            for (int i = 0; i < Nodes * 4 && count < Nodes; i++)
            {
                Vector3 last = navPos.pos;
                Vector3 dir = Vector3.zero;
                float dist = 0f;
                goal.SampleDistanceDir(navPos, ref dir, ref dist);
                navPos.pos += dir.normalized * Step;
                if (goal.endPointBounds.Contains(navPos.pos) || dist < Step) break;
                carried += Vector3.Distance(last, navPos.pos);
                while (carried > Step && count < Nodes)
                {
                    count++;
                    carried -= Step;
                    Vector3 dot = Vector3.MoveTowards(navPos.pos, last, carried) + Lift;
                    for (int k = 0; k < 4; k++)
                    {
                        vertices.Add(dot);
                        distances.Add(new Vector2(dist - carried, 0f));
                    }
                    corners.Add(new Vector2(-1f, 0f));
                    corners.Add(new Vector2(0f, -1f));
                    corners.Add(new Vector2(1f, 0f));
                    corners.Add(new Vector2(0f, 1f));
                }
            }
            for (int i = count; i < Nodes; i++)
                for (int k = 0; k < 4; k++)
                {
                    vertices.Add(Vector3.zero);
                    corners.Add(Vector2.zero);
                    distances.Add(Vector2.zero);
                }
            Mesh mesh = pathFilter.sharedMesh;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, corners);
            mesh.SetUVs(1, distances);
        }

        /// <summary>256 quads to lay dots in (EnglishPatherVisualizer.GetMesh), with bounds big enough never to be culled.</summary>
        private static Mesh NewMesh()
        {
            var mesh = new Mesh { name = "BNMP Commander Path Mesh " + made };
            var vertices = new List<Vector3>(Nodes * 4);
            var corners = new List<Vector2>(Nodes * 4);
            var distances = new List<Vector2>(Nodes * 4);
            var triangles = new List<int>(Nodes * 6);
            for (int i = 0; i < Nodes; i++)
            {
                for (int k = 0; k < 4; k++)
                {
                    vertices.Add(Vector3.zero);
                    distances.Add(Vector2.zero);
                }
                corners.Add(new Vector2(-1f, 0f));
                corners.Add(new Vector2(0f, -1f));
                corners.Add(new Vector2(1f, 0f));
                corners.Add(new Vector2(0f, 1f));
                int first = i * 4;
                triangles.Add(first);
                triangles.Add(first + 1);
                triangles.Add(first + 2);
                triangles.Add(first + 2);
                triangles.Add(first + 3);
                triangles.Add(first);
            }
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, corners);
            mesh.SetUVs(1, distances);
            mesh.bounds = new Bounds(new Vector3(0f, made % 32, 0f), Vector3.one * 1000f);
            return mesh;
        }

        private void Hide()
        {
            tint.a = 0f;
            alpha = Vector2.zero;
            if (pathObject && pathObject.activeSelf) pathObject.SetActive(false);
            if (endObject && endObject.activeSelf) endObject.SetActive(false);
        }

        public void Destroy()
        {
            if (target) target.RemoveHighlight(this);
            target = null;
            if (pathFilter && pathFilter.sharedMesh) Object.Destroy(pathFilter.sharedMesh);
            if (pathObject) Object.Destroy(pathObject);
            if (endObject) Object.Destroy(endObject);
        }
    }

    /// <summary>
    /// The glow the game gives a defender squad under the mouse (_Hover) and its picked tint (_SelectionColor), on a
    /// commander's mirrored Vikings, set the way the game's AgentSelected sets them: through each sprite batch, or the
    /// renderer's own property block.
    /// </summary>
    internal static class CommanderGlow
    {
        private sealed class Look
        {
            public readonly List<BatchedSprite> batches = new List<BatchedSprite>();
            public readonly List<MeshRenderer> renderers = new List<MeshRenderer>();
            public float hover = -1f;
            public Color selection = new Color(-1f, -1f, -1f, -1f);
        }

        private static readonly int HoverId = Shader.PropertyToID("_Hover"), SelectionId = Shader.PropertyToID("_SelectionColor");
        private static readonly Dictionary<Agent, Look> looks = new Dictionary<Agent, Look>();
        private static MaterialPropertyBlock block;

        public static void Set(Agent agent, float hover, Color selection)
        {
            if (!agent) return;
            if (!looks.TryGetValue(agent, out Look look)) looks[agent] = look = Read(agent);
            if (Mathf.Abs(look.hover - hover) < 0.002f && look.selection == selection) return;
            look.hover = hover;
            look.selection = selection;
            if (block == null) block = new MaterialPropertyBlock();
            foreach (MeshRenderer renderer in look.renderers)
            {
                if (!renderer) continue;
                renderer.GetPropertyBlock(block);
                block.SetFloat(HoverId, hover);
                block.SetColor(SelectionId, selection);
                renderer.SetPropertyBlock(block);
            }
            foreach (BatchedSprite batch in look.batches)
            {
                if (!batch) continue;
                batch.block.SetFloat(HoverId, hover);
                batch.block.SetColor(SelectionId, selection);
                batch.ComittBlock();
            }
        }

        private static Look Read(Agent agent)
        {
            var look = new Look();
            foreach (MeshRenderer renderer in agent.GetComponentsInChildren<MeshRenderer>(true))
            {
                Transform parent = renderer.transform.parent;
                BatchedSprite batch = parent ? parent.GetComponent<BatchedSprite>() : null;
                if (!batch) look.renderers.Add(renderer);
                else if (!look.batches.Contains(batch)) look.batches.Add(batch);
            }
            return look;
        }

        /// <summary>Forget every Viking (the island is gone).</summary>
        public static void Clear() => looks.Clear();
    }
}
