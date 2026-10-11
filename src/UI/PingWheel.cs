using System.Collections.Generic;
using System.Reflection;
using BadNorthMP.Net;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Voxels.TowerDefense;

namespace BadNorthMP.UI
{
    /// <summary>
    /// The ping wheel: hold the middle mouse button on the island, point at what you mean (Attack, Move here, Fall back,
    /// Hold) and let go; a quick click is a plain "Look here", and letting go in the middle sends nothing. Pings
    /// (Net.Pings) stand on the island where they were made: a short column of light in that player's main commander's
    /// colour, with who made it and what it means above. The island hides them like anything else on it (label too);
    /// one off screen waits at the screen's edge. While pings are on, the middle button no longer turns the camera
    /// (MiddleMouseForPingsPatch).
    /// </summary>
    internal class PingWheel : MonoBehaviour
    {
        // Clockwise from the top.
        private static readonly Pings.Kind[] Choices = { Pings.Kind.Attack, Pings.Kind.Move, Pings.Kind.Retreat, Pings.Kind.Hold };
        private const float Radius = 100f, DeadZone = 34f, OpenDelay = 0.2f, EdgeMargin = 70f;
        private static readonly Color Chip = new Color(0.94f, 0.91f, 0.85f, 1f);

        private sealed class Choice
        {
            public RectTransform rect;
            public Image back;
            public Text label;
        }

        private sealed class Marker
        {
            public Pings.Ping ping;
            public GameObject world;
            public Transform column, ring;
            public Material material;
            public RectTransform chip;
            public Image chipBack, dot;
            public Text label;
            public CanvasGroup group;
        }

        private GameObject canvasObject;
        private Canvas canvas;
        private RectTransform rootRect, wheel;
        private readonly List<Choice> choices = new List<Choice>();
        private readonly List<Marker> markers = new List<Marker>();

        private bool holding;
        private float downAt;
        private Vector2 anchor;
        private Vector3 target;
        private int hover = -1;

        private void Update()
        {
            Pings.Update();
            if (!Pings.CanPing) holding = false;
            else if (!holding && Input.GetMouseButtonDown(2) && Aim(Input.mousePosition, out target))
            {
                holding = true;
                downAt = Time.unscaledTime;
                anchor = Input.mousePosition;
                hover = -1;
            }
            if (holding) Hold();
            if (holding || Pings.Active.Count > 0 || (canvasObject && canvasObject.activeSelf)) Draw();
        }

        private void Hold()
        {
            Vector2 offset = (Vector2)Input.mousePosition - anchor;
            int now = offset.magnitude < DeadZone * Screen.height / 1080f ? -1 : Sector(offset);
            if (now != hover && now >= 0) FabricWrapper.PostEvent(FabricID.uiHover);
            hover = now;
            if (Input.GetMouseButton(2)) return;
            holding = false;
            if (hover >= 0) Pings.Send(Choices[hover], target);
            else if (Time.unscaledTime - downAt < OpenDelay) Pings.Send(Pings.Kind.Look, target);
            else FabricWrapper.PostEvent(FabricID.uiBack);
            hover = -1;
        }

        /// <summary>Which choice a direction points at: one slice each, the first centred straight up.</summary>
        private static int Sector(Vector2 offset)
        {
            float angle = Mathf.Atan2(offset.x, offset.y) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;
            return Mathf.RoundToInt(angle / (360f / Choices.Length)) % Choices.Length;
        }

        private static int groundMask;

        /// <summary>The island's ground and buildings: the layers the game picks tiles on.</summary>
        private static int GroundMask => groundMask != 0 ? groundMask : (groundMask = LayerMask.GetMask("Voxels", "Modules"));

        /// <summary>
        /// The spot under the mouse: the island itself (its ground and buildings, the layers the game picks tiles on;
        /// other colliders are invisible and left pings floating), or else the sea.
        /// </summary>
        private static bool Aim(Vector2 screen, out Vector3 world)
        {
            world = Vector3.zero;
            Camera camera = IslandCamera();
            if (!camera) return false;
            Ray ray = camera.ScreenPointToRay(screen);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, GroundMask, QueryTriggerInteraction.Ignore))
            {
                world = hit.point;
                return true;
            }
            if (ray.direction.y > -0.01f) return false;
            world = ray.origin + ray.direction * ((Voxels.Constants.waterLevel - ray.origin.y) / ray.direction.y);
            return true;
        }

        private static Camera IslandCamera()
        {
            var level = Singleton<LevelCamera>.instance;
            return level ? level.cameraRef : null;
        }

        // ---- Drawing ----------------------------------------------------------------------------------------------

        private void Draw()
        {
            if (!canvasObject) Build();
            bool any = holding || Pings.Active.Count > 0;
            if (canvasObject.activeSelf != any) canvasObject.SetActive(any);
            if (!any) return;

            bool open = holding && (Time.unscaledTime - downAt >= OpenDelay || hover >= 0);
            if (wheel.gameObject.activeSelf != open) wheel.gameObject.SetActive(open);
            if (open)
            {
                wheel.anchoredPosition = Local(anchor);
                Color mine = Pings.Colour(Net.SteamSession.Instance ? Net.SteamSession.Instance.Me : Steamworks.CSteamID.Nil);
                for (int i = 0; i < choices.Count; i++)
                {
                    bool lit = i == hover;
                    choices[i].back.color = lit ? Color.Lerp(Chip, mine, 0.45f) : Chip;
                    choices[i].rect.localScale = Vector3.one * (lit ? 1.12f : 1f);
                }
            }

            // One marker per ping: new ones get theirs, faded ones go.
            for (int i = markers.Count - 1; i >= 0; i--)
                if (!Pings.Active.Contains(markers[i].ping)) { Remove(markers[i]); markers.RemoveAt(i); }
            foreach (Pings.Ping ping in Pings.Active)
                if (!markers.Exists(m => m.ping == ping)) markers.Add(NewMarker(ping));
            Camera camera = IslandCamera();
            foreach (Marker marker in markers)
            {
                if (marker.world && marker.world.activeSelf != (bool)camera) marker.world.SetActive(camera);
                if (marker.chip.gameObject.activeSelf != (bool)camera) marker.chip.gameObject.SetActive(camera);
                if (camera) Place(marker, camera);
            }
        }

        private void Place(Marker marker, Camera camera)
        {
            Pings.Ping ping = marker.ping;
            float age = Time.unscaledTime - ping.at;
            float fade = Mathf.Clamp01((Pings.Duration - age) / 1f) * (ping.name == "You" ? 0.8f : 1f);
            marker.label.text = ping.name + ": " + Pings.Label(ping.kind);
            marker.dot.color = ping.colour;
            marker.chipBack.color = Color.Lerp(Chip, ping.colour, 0.3f);

            // The column rises out of the ground, and its base ring breathes.
            float tile = Tile();
            float rise = 1f - Mathf.Pow(1f - Mathf.Clamp01(age / 0.3f), 3f);
            float height = tile * 1.5f * Mathf.Max(0.02f, rise), radius = tile * 0.2f;
            if (marker.world)
            {
                marker.world.transform.position = ping.position;
                marker.column.localScale = new Vector3(radius, height, radius);
                float pulse = radius * (1.7f + 0.15f * Mathf.Sin(age * 5f) * rise);
                marker.ring.localPosition = new Vector3(0f, tile * 0.02f, 0f);
                marker.ring.localScale = new Vector3(pulse, 1f, pulse);
                Color colour = ping.colour;
                colour.a = fade;
                marker.material.color = colour;
            }

            // The label sits above the column; it hides where the island hides the column, and one off screen waits at the edge.
            Vector3 top = ping.position + Vector3.up * height;
            Vector3 screen = camera.WorldToScreenPoint(top);
            bool behind = screen.z < 0f;
            if (behind) screen = new Vector3(Screen.width - screen.x, Screen.height - screen.y, 0f); // mirrored, so it points the right way
            Vector2 local = Local(screen);
            Vector2 half = rootRect.rect.size * 0.5f - new Vector2(EdgeMargin, EdgeMargin);
            bool onScreen = !behind && Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y;
            if (!onScreen)
            {
                float fit = Mathf.Min(half.x / Mathf.Max(1f, Mathf.Abs(local.x)), half.y / Mathf.Max(1f, Mathf.Abs(local.y)));
                local *= Mathf.Min(1f, fit);
            }
            bool hidden = onScreen && Hidden(camera, top);
            marker.chip.anchoredPosition = onScreen ? local + new Vector2(0f, 10f) : local;
            marker.group.alpha = hidden ? 0f : fade;
        }

        /// <summary>The island stands between the camera and this point.</summary>
        private static bool Hidden(Camera camera, Vector3 point)
        {
            Ray ray = camera.ScreenPointToRay(camera.WorldToScreenPoint(point));
            float distance = Vector3.Dot(point - ray.origin, ray.direction);
            return distance > 0.05f && Physics.Raycast(ray, distance - 0.03f, GroundMask, QueryTriggerInteraction.Ignore);
        }

        private static Island sizedFor;
        private static float tile = 0.25f;

        /// <summary>The island's tile width (between neighbouring tiles), which the pings are sized from.</summary>
        private static float Tile()
        {
            Island island = GameAccess.CurrentIsland;
            if (island == sizedFor) return tile;
            sizedFor = island;
            tile = 0.25f;
            List<NavSpot> spots = island && island.navSpotter ? island.navSpotter.navSpots : null;
            if (spots == null || spots.Count < 2 || !spots[0]) return tile;
            Vector3 first = spots[0].navPos.pos;
            float nearest = float.MaxValue;
            for (int i = 1; i < spots.Count; i++)
            {
                if (!spots[i]) continue;
                Vector3 other = spots[i].navPos.pos;
                float distance = new Vector2(other.x - first.x, other.z - first.z).magnitude;
                if (distance > 0.01f && distance < nearest) nearest = distance;
            }
            if (nearest < 5f) tile = nearest;
            return tile;
        }

        /// <summary>A screen point in the canvas's own units, from its centre.</summary>
        private Vector2 Local(Vector2 screen)
        {
            float scale = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            return (screen - new Vector2(Screen.width, Screen.height) * 0.5f) / scale;
        }

        private void Build()
        {
            canvasObject = GameStyle.Canvas("BNMP Pings", 540);
            canvas = canvasObject.GetComponent<Canvas>();
            canvasObject.GetComponent<GraphicRaycaster>().enabled = false; // never takes a click
            rootRect = (RectTransform)canvasObject.transform;

            wheel = Node("Wheel", rootRect, 2f * (Radius + 70f));
            Image shade = wheel.gameObject.AddComponent<Image>();
            shade.sprite = Shapes.Disc;
            shade.color = new Color(0f, 0f, 0f, 0.32f);
            Image hub = Node("Hub", wheel, 26f).gameObject.AddComponent<Image>();
            hub.sprite = Shapes.Ring;
            hub.color = Chip;
            for (int i = 0; i < Choices.Length; i++)
            {
                float angle = i * 2f * Mathf.PI / Choices.Length;
                RectTransform chip = ChipNode(wheel, Pings.Label(Choices[i]), Color.white, 20, out Image back, out Text label, out Image dot);
                dot.gameObject.SetActive(false); // the words say it; colour is for who
                chip.anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * Radius;
                choices.Add(new Choice { rect = chip, back = back, label = label });
            }
            wheel.SetAsLastSibling();
            foreach (Graphic graphic in canvasObject.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        }

        private Marker NewMarker(Pings.Ping ping)
        {
            var marker = new Marker { ping = ping };
            marker.chip = ChipNode(rootRect, "", Color.white, 18, out marker.chipBack, out marker.label, out marker.dot);
            marker.chip.pivot = new Vector2(0.5f, 0f);
            marker.group = marker.chip.gameObject.AddComponent<CanvasGroup>();
            marker.group.blocksRaycasts = marker.group.interactable = false;
            foreach (Graphic graphic in marker.chip.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            wheel.SetAsLastSibling();

            // In the world, so the island hides it: drawn see-through, without writing depth or casting shadows.
            Shader shader = Shader.Find("Sprites/Default");
            if (shader)
            {
                marker.material = new Material(shader) { color = ping.colour };
                marker.world = new GameObject("BNMP Ping");
                marker.column = WorldPart(marker.world.transform, "Column", PingMeshes.Column, marker.material);
                marker.ring = WorldPart(marker.world.transform, "Base", PingMeshes.BaseRing, marker.material);
            }
            return marker;
        }

        private static Transform WorldPart(Transform parent, string name, Mesh mesh, Material material)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return part.transform;
        }

        private static void Remove(Marker marker)
        {
            if (marker.world) Destroy(marker.world);
            if (marker.material) Destroy(marker.material);
            if (marker.chip) Destroy(marker.chip.gameObject);
        }

        private static RectTransform Node(string name, Transform parent, float size)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            return rect;
        }

        /// <summary>A parchment label with a coloured dot, sized to its text, centred on its position.</summary>
        private static RectTransform ChipNode(Transform parent, string text, Color colour, int size, out Image back, out Text label, out Image dot)
        {
            RectTransform chip = GameStyle.Panel(parent, new RectOffset(10, 12, 4, 5), 0f);
            chip.anchorMin = chip.anchorMax = chip.pivot = new Vector2(0.5f, 0.5f);
            back = chip.GetComponent<Image>();
            back.color = Chip;
            Transform row = GameStyle.Row(chip, size + 6f, 7f);
            dot = new GameObject("Dot", typeof(RectTransform)).AddComponent<Image>();
            dot.transform.SetParent(row, false);
            dot.sprite = Shapes.Disc;
            dot.color = colour;
            var element = dot.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = element.preferredHeight = size * 0.6f;
            label = GameStyle.Label(row, text, size);
            return chip;
        }
    }

    /// <summary>
    /// A ping's shapes in the world, made once: an open column (radius and height 1) brightest at its foot and fading to
    /// nothing at the top, and a flat ring for its base. White; the ping's material tints them.
    /// </summary>
    internal static class PingMeshes
    {
        private const int Sides = 28;
        private static Mesh column, baseRing;

        public static Mesh Column => column ? column : (column = Tube(0f, 1f, 0.75f, 0f, false));
        public static Mesh BaseRing => baseRing ? baseRing : (baseRing = Tube(0.7f, 1f, 0.95f, 0.95f, true));

        /// <summary>
        /// A band of quads between two circles: upright (radius 1, from height a to b) or flat (from radius a to b at
        /// height 0). Alpha alphaA at the first circle, alphaB at the second.
        /// </summary>
        private static Mesh Tube(float a, float b, float alphaA, float alphaB, bool flat)
        {
            var vertices = new Vector3[(Sides + 1) * 2];
            var colours = new Color[vertices.Length];
            var triangles = new int[Sides * 6];
            for (int i = 0; i <= Sides; i++)
            {
                float angle = i * 2f * Mathf.PI / Sides;
                var around = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = flat ? around * a : around + Vector3.up * a;
                vertices[i * 2 + 1] = flat ? around * b : around + Vector3.up * b;
                colours[i * 2] = new Color(1f, 1f, 1f, alphaA);
                colours[i * 2 + 1] = new Color(1f, 1f, 1f, alphaB);
            }
            for (int i = 0; i < Sides; i++)
            {
                int v = i * 2;
                triangles[i * 6] = v; triangles[i * 6 + 1] = v + 1; triangles[i * 6 + 2] = v + 2;
                triangles[i * 6 + 3] = v + 1; triangles[i * 6 + 4] = v + 3; triangles[i * 6 + 5] = v + 2;
            }
            var mesh = new Mesh { name = flat ? "BNMP Ping Ring" : "BNMP Ping Column", hideFlags = HideFlags.DontUnloadUnusedAsset };
            mesh.vertices = vertices;
            mesh.colors = colours;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>Round shapes drawn once in code: a ring and a filled disc (white, tinted where used).</summary>
    internal static class Shapes
    {
        private static Sprite ring, disc;

        public static Sprite Ring => ring ? ring : (ring = Make(0.78f));
        public static Sprite Disc => disc ? disc : (disc = Make(0f));

        private static Sprite Make(float hole)
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };
            float centre = (size - 1) * 0.5f, outer = size * 0.5f - 1f, inner = outer * hole;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));
                    float alpha = Mathf.Clamp01(outer - d + 0.5f) * (hole > 0f ? Mathf.Clamp01(d - inner + 0.5f) : 1f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }
    }

    /// <summary>
    /// While pings are on, the middle mouse button is the ping wheel's: the game would otherwise turn the camera with a
    /// middle drag (any button drags it) and pass the click on. A press it doesn't take keeps its whole drag and click.
    /// </summary>
    [HarmonyPatch]
    internal static class MiddleMouseForPingsPatch
    {
        private static bool pressed, dragging;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "OnButtonDown", "OnButtonUp", "OnDragStart", "OnDrag", "OnDragEnd" })
                yield return AccessTools.Method(typeof(CursorManager), name);
        }

        private static bool Prefix(MethodBase __originalMethod, PointerEventData.InputButton button)
        {
            if (button != PointerEventData.InputButton.Middle) return true;
            switch (__originalMethod.Name)
            {
                case "OnButtonDown":
                    pressed = Pings.CanPing;
                    return !pressed;
                case "OnButtonUp":
                    if (!pressed) return true;
                    pressed = false;
                    return false;
                case "OnDragStart":
                    dragging = pressed || Pings.CanPing;
                    return !dragging;
                case "OnDrag":
                    return !dragging;
                default: // OnDragEnd
                    if (!dragging) return true;
                    dragging = false;
                    return false;
            }
        }
    }
}
