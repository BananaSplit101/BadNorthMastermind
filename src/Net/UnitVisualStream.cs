using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.SpriteMagic;

namespace BadNorthMP.Net
{
    // Separate from the legacy transform stream, so old recordings remain readable.
    // 25: node definition (id,index,parentIndex,name,pooledPrefab)
    // 26: animator records; 27: node records; 28: agent visibility/health; 29: node removed.
    // Frame datagrams carry every unit captured in one snapshot: float time, then until the end of the
    // datagram: int agentId, ushort count, (ushort length, byte[] record)*.
    internal static class UnitVisualStream
    {
        public const MsgType NodeDefinition = (MsgType)25, AnimatorFrame = (MsgType)26,
            NodeFrame = (MsgType)27, AgentFrame = (MsgType)28, NodeGone = (MsgType)29;
        internal const int PacketLimit = 1100;

        // Node record flags. Each record stands alone (frames are unreliable); default values are left out.
        internal const byte NodeActive = 1, NodeWorld = 2, NodeMoved = 4, NodeRotated = 8, NodeScaled = 16,
            NodeSprite = 32, NodeSpriteRenderer = 64;
        // Sprite block flags.
        internal const byte SpriteEnabled = 1, SpriteFloatColor = 2;

        // Snapshots record hundreds of small records; reuse one buffer instead of allocating a stream per record.
        private static readonly MemoryStream scratch = new MemoryStream(256);
        private static readonly BinaryWriter scratchWriter = new BinaryWriter(scratch);
        private static bool recording;

        internal static byte[] Record(Action<BinaryWriter> write)
        {
            if (recording)
                using (var ms = new MemoryStream())
                using (var w = new BinaryWriter(ms)) { write(w); w.Flush(); return ms.ToArray(); }
            recording = true;
            try
            {
                scratch.SetLength(0);
                write(scratchWriter);
                scratchWriter.Flush();
                return scratch.ToArray();
            }
            finally { recording = false; }
        }

        /// <summary>Packs the records of every unit captured in one snapshot into as few datagrams as possible.</summary>
        private sealed class FrameBatch
        {
            private readonly MsgType type;
            private readonly MemoryStream stream = new MemoryStream(PacketLimit);
            private readonly BinaryWriter writer;
            private float time;
            private int sectionId;
            private long countAt = -1;
            private ushort count;

            public FrameBatch(MsgType type) { this.type = type; writer = new BinaryWriter(stream); }

            public void Add(int id, float time, byte[] record)
            {
                const int header = 1 + 4, sectionHeader = 4 + 2;
                if (header + sectionHeader + 2 + record.Length > PacketLimit) throw new IOException("Unit visual record exceeds packet budget");
                if (stream.Length > 0 && time != this.time) Flush();
                bool newSection = countAt < 0 || id != sectionId;
                if (stream.Length > 0 && stream.Length + (newSection ? sectionHeader : 0) + 2 + record.Length > PacketLimit)
                {
                    Flush();
                    newSection = true;
                }
                if (stream.Length == 0) { writer.Write((byte)type); writer.Write(time); this.time = time; }
                if (newSection) { writer.Write(id); countAt = stream.Position; writer.Write((ushort)0); count = 0; sectionId = id; }
                writer.Write((ushort)record.Length); writer.Write(record);
                long end = stream.Position;
                stream.Position = countAt; writer.Write(++count);
                stream.Position = end;
            }

            public void Flush()
            {
                if (stream.Length > 0)
                {
                    writer.Flush();
                    BattleStream.Send(stream.ToArray(), false);
                }
                Discard();
            }

            public void Discard() { stream.SetLength(0); countAt = -1; }
        }

        private static readonly FrameBatch nodeBatch = new FrameBatch(NodeFrame);
        private static readonly FrameBatch animatorBatch = new FrameBatch(AnimatorFrame);
        private static readonly FrameBatch agentBatch = new FrameBatch(AgentFrame);
        // Unchanged child poses are resent this often (plus a stagger) to recover lost datagrams.
        private const float NodeRefresh = 1.5f;

        /// <summary>Send everything captured since the last flush; called once per snapshot.</summary>
        public static void FlushFrames() { nodeBatch.Flush(); animatorBatch.Flush(); agentBatch.Flush(); }

        /// <summary>A new battle: drop frames and sprite ids from the previous one.</summary>
        public static void Reset() { nodeBatch.Discard(); animatorBatch.Discard(); agentBatch.Discard(); spriteKeys.Clear(); }

        private static readonly Dictionary<Sprite, int> spriteKeys = new Dictionary<Sprite, int>();

        /// <summary>
        /// A stable 32-bit id for a sprite (texture name + sprite name), identical on every machine, so records
        /// carry 4 bytes instead of both names. 0 means no sprite.
        /// </summary>
        internal static int SpriteKey(Sprite sprite)
        {
            if (!sprite) return 0;
            if (spriteKeys.TryGetValue(sprite, out int key)) return key;
            if (spriteKeys.Count > 8192) spriteKeys.Clear(); // runtime-created sprites; never grow without bound
            uint hash = 2166136261;
            string texture = sprite.texture ? sprite.texture.name : "", name = sprite.name;
            for (int i = 0; i < texture.Length; i++) hash = (hash ^ texture[i]) * 16777619;
            hash = (hash ^ '\n') * 16777619;
            for (int i = 0; i < name.Length; i++) hash = (hash ^ name[i]) * 16777619;
            key = hash == 0 ? 1 : unchecked((int)hash);
            spriteKeys[sprite] = key;
            return key;
        }

        internal static void WriteColor(BinaryWriter w, Color color, bool asFloats)
        {
            if (asFloats) { w.Write(color.r); w.Write(color.g); w.Write(color.b); w.Write(color.a); return; }
            w.Write(ColorByte(color.r)); w.Write(ColorByte(color.g)); w.Write(ColorByte(color.b)); w.Write(ColorByte(color.a));
        }

        internal static Color ReadColor(BinaryReader r, bool asFloats) => asFloats
            ? new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle())
            : new Color(r.ReadByte() / 255f, r.ReadByte() / 255f, r.ReadByte() / 255f, r.ReadByte() / 255f);

        /// <summary>Tints outside 0..1 (HDR) keep full precision; everything else fits in a byte per channel.</summary>
        internal static bool NeedsFloats(Color c) =>
            c.r < 0 || c.r > 1 || c.g < 0 || c.g > 1 || c.b < 0 || c.b > 1 || c.a < 0 || c.a > 1;

        private static byte ColorByte(float v) => (byte)(v <= 0 ? 0 : v >= 1 ? 255 : (int)(v * 255f + 0.5f));

        internal sealed class Sender
        {
            private sealed class Node
            {
                public ushort index, parent;
                public Transform transform;
                public float refresh;
                public bool gone;
                public bool pooled;
                public Animator[] animators;
                public Renderer[] renderers;
                public SpriteAnimator sprite;
                public SpriteRenderer spriteRenderer;
                public bool captured, active, detached, spriteEnabled;
                public ushort capturedParent;
                public Vector3 position, scale;
                public Quaternion rotation;
                public Sprite firstSprite, secondSprite, rendererSprite;
                public Color spriteColor, rendererColor;
                public bool[] rendererStates;
            }
            private readonly List<Node> nodes = new List<Node>();
            private readonly List<Node> activeNodes = new List<Node>();
            private readonly Dictionary<Transform, Node> lookup = new Dictionary<Transform, Node>();
            private struct ParameterValue { public AnimatorControllerParameterType type; public float value; public int integer; }
            private struct LayerValue { public bool enabled, transition; public int hash, nextHash; public float weight, phase; }
            private readonly Dictionary<ulong, ParameterValue> parameters = new Dictionary<ulong, ParameterValue>();
            private readonly Dictionary<ulong, LayerValue> layers = new Dictionary<ulong, LayerValue>();
            private float nextParameterRefresh;
            private float nextAnimationFrame;
            private float nextDiscovery;
            private byte[] lastAgent;
            private float nextAgentRefresh;
            private readonly List<byte[]> nodeFrames = new List<byte[]>();
            private readonly List<byte[]> animatorFrames = new List<byte[]>();
            private readonly Dictionary<Animator, AnimatorControllerParameter[]> animatorParameters = new Dictionary<Animator, AnimatorControllerParameter[]>();
            private readonly List<ulong> staleKeys = new List<ulong>();

            public void Capture(int id, Agent agent, float time)
            {
                // Walk every hierarchy, including inactive equipment and held pooled arrows/torches.
                // Equipment can attach during an attack; discover it at a bounded rate rather
                // than walking every unit's complete hierarchy on every snapshot.
                if (nodes.Count == 0 || time >= nextDiscovery)
                { Discover(id, agent.transform, ushort.MaxValue); nextDiscovery = time + 0.1f; }
                var frames = nodeFrames; frames.Clear();
                var animations = animatorFrames; animations.Clear();
                bool refreshParameters = time >= nextParameterRefresh;
                if (refreshParameters) nextParameterRefresh = time + 0.5f;
                // Positions/child poses still use the full snapshot rate. Layer phases interpolate
                // between 10 Hz samples; actual state/transition/weight changes are sent immediately.
                bool refreshAnimation = time + 0.0001f >= nextAnimationFrame;
                if (refreshAnimation) nextAnimationFrame = time + 0.1f;
                for (int nodeIndex = activeNodes.Count - 1; nodeIndex >= 0; nodeIndex--)
                {
                    Node node = activeNodes[nodeIndex];
                    Transform tr = node.transform;
                    if (node.gone) continue;
                    bool detached = tr && tr != agent.transform && !tr.IsChildOf(agent.transform);
                    if (!tr || (detached && node.pooled))
                    {
                        if (!node.gone)
                        {
                            node.gone = true;
                            if (!ReferenceEquals(tr, null)) lookup.Remove(tr);
                            BattleStream.Send(Packet.Build(NodeGone, w => { w.Write(id); w.Write(node.index); }), true);
                            activeNodes.RemoveAt(nodeIndex);
                            nodes[node.index] = null;
                            foreach (Animator animator in node.animators) animatorParameters.Remove(animator);
                            staleKeys.Clear();
                            foreach (ulong key in parameters.Keys) if (key >> 40 == node.index) staleKeys.Add(key);
                            foreach (ulong key in staleKeys) parameters.Remove(key);
                            staleKeys.Clear();
                            foreach (ulong key in layers.Keys) if (key >> 40 == node.index) staleKeys.Add(key);
                            foreach (ulong key in staleKeys) layers.Remove(key);
                        }
                        continue;
                    }
                    ushort currentParent = tr.parent && lookup.TryGetValue(tr.parent, out Node parentNode) ? parentNode.index : ushort.MaxValue;
                    bool changed = Changed(node, tr, tr == agent.transform, detached, currentParent);
                    if (time >= node.refresh || changed)
                    {
                        frames.Add(Record(w => WriteNode(w, node, tr, tr == agent.transform, detached, currentParent)));
                        // Unchanged poses are only resent to recover lost datagrams; spread those refreshes out.
                        node.refresh = time + NodeRefresh + (node.index % 8) * (NodeRefresh / 8f);
                    }
                    Animator[] animators = node.animators;
                    for (int i = 0; i < animators.Length; i++)
                        WriteAnimator(animations, node.index, (byte)i, animators[i], refreshParameters, refreshAnimation);
                }
                foreach (byte[] record in frames) nodeBatch.Add(id, time, record);
                foreach (byte[] record in animations) animatorBatch.Add(id, time, record);
                byte[] agentState = Record(w =>
                {
                    w.Write(agent.gameObject.activeSelf && agent.gameObject.activeInHierarchy);
                    w.Write(agent.health); w.Write(agent.maxHealth);
                    var replenishing = agent.GetComponent<ReplenishComponent>();
                    w.Write(replenishing && replenishing.indoors != null && replenishing.indoors.active);
                });
                if (time >= nextAgentRefresh || !Equal(agentState, lastAgent))
                {
                    lastAgent = agentState; nextAgentRefresh = time + 0.5f;
                    agentBatch.Add(id, time, agentState);
                }
            }

            private void Discover(int id, Transform tr, ushort parent)
            {
                if (!lookup.TryGetValue(tr, out Node node))
                {
                    if (nodes.Count >= ushort.MaxValue) throw new IOException("Too many unit visual nodes");
                    node = new Node { index = (ushort)nodes.Count, parent = parent, transform = tr,
                        animators = tr.GetComponents<Animator>(), renderers = tr.GetComponents<Renderer>(),
                        sprite = tr.GetComponent<SpriteAnimator>(), spriteRenderer = tr.GetComponent<SpriteRenderer>() };
                    nodes.Add(node); activeNodes.Add(node); lookup.Add(tr, node);
                    var pooled = tr.GetComponent<SelfPoolingPrefab>();
                    node.pooled = pooled || (parent != ushort.MaxValue && nodes[parent].pooled);
                    string prefab = pooled ? tr.name.Replace("(Clone)", "").Trim() : "";
                    BattleStream.Send(Packet.Build(NodeDefinition, w =>
                    {
                        w.Write(id); w.Write(node.index); w.Write(parent); w.Write(tr.name); w.Write(prefab);
                    }), true);
                }
                for (int i = 0; i < tr.childCount; i++) Discover(id, tr.GetChild(i), node.index);
            }

            private static bool Equal(byte[] a, byte[] b)
            {
                if (b == null || a.Length != b.Length) return false;
                for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
                return true;
            }

            private static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
            private static bool Same(Color a, Color b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

            private static bool Changed(Node n, Transform tr, bool root, bool detached, ushort parent)
            {
                Vector3 pos = root ? Vector3.zero : detached ? tr.position : tr.localPosition;
                Quaternion rot = root ? Quaternion.identity : detached ? tr.rotation : tr.localRotation;
                Vector3 scale = detached ? tr.lossyScale : tr.localScale;
                bool changed = !n.captured || n.active != tr.gameObject.activeSelf || n.detached != detached ||
                    n.capturedParent != parent || !Same(n.position, pos) || !Same(n.scale, scale) ||
                    n.rotation.x != rot.x || n.rotation.y != rot.y || n.rotation.z != rot.z || n.rotation.w != rot.w;
                n.captured = true; n.active = tr.gameObject.activeSelf; n.detached = detached;
                n.capturedParent = parent; n.position = pos; n.rotation = rot; n.scale = scale;
                if (n.rendererStates == null) n.rendererStates = new bool[n.renderers.Length];
                for (int i = 0; i < n.renderers.Length; i++)
                {
                    bool enabled = n.renderers[i].enabled;
                    if (n.rendererStates[i] != enabled) changed = true;
                    n.rendererStates[i] = enabled;
                }
                if (n.sprite)
                {
                    if (n.spriteEnabled != n.sprite.enabled || n.firstSprite != n.sprite.sprite ||
                        n.secondSprite != n.sprite.sprite2 || !Same(n.spriteColor, n.sprite.color)) changed = true;
                    n.spriteEnabled = n.sprite.enabled; n.firstSprite = n.sprite.sprite;
                    n.secondSprite = n.sprite.sprite2; n.spriteColor = n.sprite.color;
                }
                if (n.spriteRenderer)
                {
                    if (n.rendererSprite != n.spriteRenderer.sprite || !Same(n.rendererColor, n.spriteRenderer.color)) changed = true;
                    n.rendererSprite = n.spriteRenderer.sprite; n.rendererColor = n.spriteRenderer.color;
                }
                return changed;
            }

            private static void WriteNode(BinaryWriter w, Node node, Transform tr, bool root, bool detached, ushort parent)
            {
                // Root position/rotation are interpolated by the legacy transform stream.
                Vector3 pos = root ? Vector3.zero : detached ? tr.position : tr.localPosition;
                Quaternion rot = root ? Quaternion.identity : detached ? tr.rotation : tr.localRotation;
                Vector3 scale = detached ? tr.lossyScale : tr.localScale;
                SpriteAnimator sprite = node.sprite;
                SpriteRenderer sr = node.spriteRenderer;
                bool moved = pos.x != 0 || pos.y != 0 || pos.z != 0;
                bool rotated = (short)(rot.x * 32767f) != 0 || (short)(rot.y * 32767f) != 0 || (short)(rot.z * 32767f) != 0 ||
                    (short)(rot.w * 32767f) != 32767;
                bool scaled = scale.x != 1 || scale.y != 1 || scale.z != 1;
                byte flags = 0;
                if (tr.gameObject.activeSelf) flags |= NodeActive;
                if (detached) flags |= NodeWorld;
                if (moved) flags |= NodeMoved;
                if (rotated) flags |= NodeRotated;
                if (scaled) flags |= NodeScaled;
                if (sprite) flags |= NodeSprite;
                if (sr) flags |= NodeSpriteRenderer;
                w.Write(node.index); w.Write(flags); w.Write(parent);
                if (moved) w.Write(pos);
                if (rotated) w.WriteRotation(rot);
                if (scaled) w.Write(scale);
                Renderer[] renderers = node.renderers;
                w.Write((byte)renderers.Length);
                for (int start = 0; start < renderers.Length; start += 8)
                {
                    byte bits = 0;
                    for (int i = start; i < renderers.Length && i < start + 8; i++)
                        if (renderers[i].enabled) bits |= (byte)(1 << (i - start));
                    w.Write(bits);
                }
                if (sprite)
                {
                    Color color = sprite.color;
                    bool floats = NeedsFloats(color);
                    w.Write((byte)((sprite.enabled ? SpriteEnabled : 0) | (floats ? SpriteFloatColor : 0)));
                    w.Write(SpriteKey(sprite.sprite)); w.Write(SpriteKey(sprite.sprite2));
                    WriteColor(w, color, floats);
                }
                if (sr)
                {
                    Color color = sr.color;
                    bool floats = NeedsFloats(color);
                    w.Write(floats ? SpriteFloatColor : (byte)0);
                    w.Write(SpriteKey(sr.sprite));
                    WriteColor(w, color, floats);
                }
            }

            private void WriteAnimator(List<byte[]> records, ushort index, byte component, Animator animator, bool refresh, bool refreshAnimation)
            {
                if (!animator.runtimeAnimatorController || !animator.isInitialized) return;
                // Parameters are individual records so even large controllers fit the datagram limit.
                if (refresh || !animatorParameters.TryGetValue(animator, out AnimatorControllerParameter[] cachedParameters))
                    animatorParameters[animator] = cachedParameters = animator.parameters;
                foreach (AnimatorControllerParameter p in cachedParameters)
                {
                    if (p.type == AnimatorControllerParameterType.Trigger) continue; // replay states, never fire actions
                    ulong key = ((ulong)index << 40) | ((ulong)component << 32) | unchecked((uint)p.nameHash);
                    var value = new ParameterValue { type = p.type };
                    if (p.type == AnimatorControllerParameterType.Float) value.value = animator.GetFloat(p.nameHash);
                    else if (p.type == AnimatorControllerParameterType.Int) value.integer = animator.GetInteger(p.nameHash);
                    else if (p.type == AnimatorControllerParameterType.Bool) value.integer = animator.GetBool(p.nameHash) ? 1 : 0;
                    if (!refresh && parameters.TryGetValue(key, out ParameterValue previous) &&
                        previous.type == value.type && previous.value == value.value && previous.integer == value.integer) continue;
                    byte[] record = Record(w =>
                    {
                        w.Write(index); w.Write(component); w.Write((byte)0); w.Write(p.nameHash); w.Write((byte)p.type);
                        switch (p.type)
                        {
                            case AnimatorControllerParameterType.Bool: w.Write(value.integer != 0); break;
                            case AnimatorControllerParameterType.Int: w.Write(value.integer); break;
                            case AnimatorControllerParameterType.Float: w.Write(value.value); break;
                        }
                    });
                    records.Add(record); parameters[key] = value;
                }
                for (int layer = 0; layer < animator.layerCount; layer++)
                {
                    int l = layer;
                    var state = animator.GetCurrentAnimatorStateInfo(l);
                    bool transition = animator.isActiveAndEnabled && animator.IsInTransition(l);
                    var next = transition ? animator.GetNextAnimatorStateInfo(l) : default(AnimatorStateInfo);
                    var value = new LayerValue { enabled = animator.enabled, transition = transition,
                        hash = state.fullPathHash, nextHash = next.fullPathHash,
                        weight = animator.GetLayerWeight(l), phase = state.normalizedTime };
                    ulong key = ((ulong)index << 40) | ((ulong)component << 32) | (uint)l;
                    if (!refreshAnimation && layers.TryGetValue(key, out LayerValue previous) &&
                        previous.enabled == value.enabled && previous.transition == value.transition &&
                        previous.hash == value.hash && previous.nextHash == value.nextHash && previous.weight == value.weight &&
                        value.phase >= previous.phase - 0.0001f) continue;
                    layers[key] = value;
                    records.Add(Record(w =>
                    {
                        w.Write(index); w.Write(component); w.Write((byte)1); w.Write((ushort)l);
                        w.Write(value.enabled); w.Write(value.weight);
                        w.Write(state.fullPathHash); w.Write(state.normalizedTime); w.Write(state.loop);
                        w.Write(transition);
                        if (transition)
                        {
                            w.Write(next.fullPathHash); w.Write(next.normalizedTime); w.Write(next.loop);
                            w.Write(animator.GetAnimatorTransitionInfo(l).normalizedTime);
                        }
                    }));
                }
            }
        }
    }
}
