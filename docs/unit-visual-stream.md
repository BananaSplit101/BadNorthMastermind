# Unit visual replication

The message IDs have descriptive names in Protocol.cs. Router forwards the battle range through 39; no additional router handler is needed.

| ID | Suggested Protocol name | Delivery | Payload after type byte |
| --- | --- | --- | --- |
| 25 | UnitVisualNode | Reliable | int agentId, ushort nodeIndex, ushort initialParentIndex, string name, string pooledPrefabName |
| 26 | UnitAnimatorFrame | Unreliable | frame envelope, animator records |
| 27 | UnitNodeFrame | Unreliable | frame envelope, hierarchy/render records |
| 28 | UnitAgentFrame | Unreliable | frame envelope, visibility/health record |
| 29 | UnitVisualNodeGone | Reliable | int agentId, ushort nodeIndex |

Frame envelope: float streamTime, int agentId, ushort count, then count records each prefixed by ushort byteLength. Packets include the type byte and never exceed 1100 bytes. All integers use BinaryWriter's little endian encoding; strings use its length-prefixed UTF-8 encoding. A parent index of 65535 denotes the unit root / no tracked parent. Indices are per agent and are never reused during its lifetime. Definitions identify individual nodes, including siblings with duplicate names; they are not slash-delimited transform paths.

Animator record: ushort nodeIndex, byte componentIndex, byte kind. Kind 0 is int parameterHash, byte AnimatorControllerParameterType, then bool/int/float value. Trigger parameters are omitted. Kind 1 is ushort layer, bool enabled, float layerWeight, int currentFullPathHash, float normalizedTime, bool loop, bool transitioning. If transitioning, append int nextFullPathHash, float nextNormalizedTime, bool nextLoop, float transitionNormalizedTime. Changed parameters and a 0.5-second refresh recover dropped packets; layer records stream at 20 Hz.

Node record: ushort nodeIndex, bool activeSelf, bool worldSpace, ushort currentParentIndex, Vector3 position, short4 quaternion, Vector3 scale, byte rendererCount, bool enabled for each renderer. Root position/rotation are placeholders: the legacy Snapshot supplies them. Detached non-pooled equipment uses world position/rotation/scale; other nodes use local coordinates. Follow with bool hasSpriteAnimator; if true, bool enabled, two sprite references, Color float4. Follow with bool hasSpriteRenderer; if true, one sprite reference and Color float4. A sprite reference is string spriteName, string textureName; empty names represent null. Records are delta filtered with a 0.5-second refresh.

Agent record: bool activeInHierarchy, float health, float maxHealth, bool indoors. This record continues for hidden, injured, replenishing, dying and dead agents until destruction. Puppet visibility is activeInHierarchy && !indoors; health and maxHealth are authoritative values.

All unit prefabs use the same discovery mechanism, including Archer/TankArcher, Tank/giant, the game's `Beserker` and `Twohanded` variants, sword/shield, spear, and axe throwing. Archery uses layer 1 (`Shoot_Shoot`) and the Aim parameter. Shield/Spear parameters and scripted bow/spear/flag transforms are captured. Giant root scale, SpriteAnimator sprite/sprite2/enabled/color, generated batched mesh scale/color and held pooled equipment are mirrored.

Puppet animators evaluate with fireEvents=false, speed=0 and applyRootMotion=false. AgentComponent Updates (including FlagPole.Update and Spear.LateUpdate) and puppet physics are disabled. Native Arsonist.SpawnTorch/Throw, ThrowComponent.SpawnTorch/Throw, AxeThrowing.ThrowAxe and Torch.Shoot also have spectator guards, verified against the actual installed assembly's metadata. Held items and projectile copies use inactive cloning, never native pool acquisition; copied SelfPoolingPrefab ownership is cleared before deactivation/reparenting. Released pooled items are removed; ordinary planted equipment continues in world space and returns to its source parent on collection. Detached visuals are cleaned up when their agent is removed.

Ship clip events and root motion are disabled without freezing legacy ship playback. CoinVisuals receives 30/31/32 and resets with PuppetWorld; full recurring frames 30/32 are discarded while loading, while 31 remains buffered.

Validation: `pwsh -NoProfile -File tools/test-unitvisuals.ps1` compiles the actual three visual source files against Unity/game stubs and exercises encoding/decoding, packet splitting, all layers, child animators, transitions, typed/changed/refreshed parameters, duplicate sibling bindings, sprite/equipment state, hidden/dying visibility, planted/collected flags, removal, out-of-order packets, event guards and clone pool safety. `python tools/check-game-patch-targets.py` verifies actual game method metadata. Native `dotnet build src/BadNorthMP.csproj --no-restore` succeeds against the stripped game assemblies. No game was launched; runtime visual quality and bandwidth remain to be measured in a multiplayer session. Transition blend reconstruction uses the sampled current/next phases and blend fraction rather than rerunning native controller timing.
