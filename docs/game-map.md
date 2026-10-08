# Bad North game map

Notes on where things live in `Assembly-CSharp.dll` (Bad North: Jotunn Edition, Steam build, Unity 2018.4.0).
To regenerate the decompiled source, see the README. All paths below are relative to `decompiled/`.

## Runtime
- Uses the **legacy .NET 3.5 Mono runtime** (`mscorlib 2.0`, `System.Core 3.5`, CLR 2.0.50727) and is 32-bit. The plugin targets `net35`.
- `Managed\UnityEngine.dll` is a type-forwarding facade that references mscorlib 4.0. It confuses MSBuild's dependency walk, which is why `_FindDependencies=false` is set in the csproj.

## Getting at the current game
- `Singleton<IslandGameplayManager>.instance.island` gives the current `Island`, or null on menus and the map. (`Singleton<T>` is in the global namespace. `instance` falls back to `FindObjectOfType`, which is slow, so don't call it every frame.)
- `Island` fields: `english` / `vikings` (`Faction`), `raid` (`Raid`), `navSpotter` (`NavSpotter`), `state` (`Created/Processing/Idle/Playing`), and `levelNode`.
- `Faction`: `livingSquads`, `allSquads`, `agents`, `enemy`, `side`.

## Squads and orders (defender side)
- `EnglishSquad : Squad` is a player squad. It has `hero` (`HeroDefinition`, name via `dbgName`), `heroAgent`, `navSpotOccupant` (`NavSpotController`), `selectable`, and `upgradeManager`.
- `Agent` has `health`, `maxHealth` and `wPos`.
- Movement is **discrete**: squads stand on `NavSpot`s (`island.navSpotter.navSpots`; each has a stable `index`, a `navPos`, and an `occupant`).
- **Every order goes through the static `Voxels.TowerDefense.SquadMover`:**
  - `MoveTo(EnglishSquad, NavSpot)`: a player move. It checks `navSpotOccupant.canMove`, plays sounds, then deselects. It is called from:
    - `Navigator.cs` (mouse)
    - `UI/TouchMoveConfirmer.cs` (touch)
    - `Upgrades/JoystickMoveAbility.cs` (gamepad)
  - `MoveToDirect(EnglishSquad, NavSpot, bool showPath)` does the actual move and bumps any occupant to a nearby spot. Abilities call it directly: `PikeChargeAbility` and `PlungeAttackAbility`.
  - `SwapWith(EnglishSquad, NavSpotController)` swaps two squads.
  - **Network order format idea:** `(squadIndex in english.livingSquads, navSpot.index)`. Squad index may not be stable when squads die, so prefer a squad id assigned at island start.
- Selection uses `SquadSelector` (a singleton): `SelectSquad(EnglishSquad, bool showGodray)`, `selectedSquad`, `squads`.
  - **Selecting a squad slows time to 0.1×** (`OnSelectStateChange` → `TimeManager.RequestTimeScale(this, 0.1f)`). In multiplayer, decide whether a remote player's selection slows the shared game. It probably shouldn't, so we'd patch it out.
- Abilities live in `Voxels.TowerDefense.Upgrades/*Ability.cs` and are managed by `SquadUpgradeManager`. We still need to map these out for networking.

## Raid and waves (mastermind side)
- `Raid` (an `IslandComponent`) owns `List<Wave> waves`, a private `waveIndex`, and a private `timer`. Waves are generated in `IIslandFirstEnter.OnIslandFirstEnter` using `UnityEngine.Random`. That generation is not network-safe, so the host should generate waves and send the result.
- The launch path is `RaidDriver.Update` → `Raid.MaybeLaunchWaves()` (timer) → `Raid.BeginNextWave()` → `StartCoroutine(Wave.BeginWave())` and `OnBeginWave(idx)`.
  - **Mastermind hook:** block `MaybeLaunchWaves` with a prefix that returns false and sets `__result = false`, then call `BeginNextWave()` on the mastermind's command. `MaybeLaunchWaves` also advances `waveIndex` once `haveAllLaunched` is true, so that bookkeeping has to be kept.
- `Wave` (`RaidGeneration/Wave.cs`): `shipGroups` → `ShipGroup.landings` → `Landing.shipLoads` (`ShipLoad.vikingRef`, `count`). It also has `waveStartTime`, `triggered`, `haveAllLaunched`, `haveAllSpawned` and `bounty`.
- `Landing.Launch()` spawns a `Longship`. Landings are pre-placed on beaches during generation (`Landing.TryPlace`).
- Viking unit types are `VikingReference` (`island.levelNode.enemies`).
- To let a mastermind compose waves freely, build new `Wave` / `ShipGroup` / `Landing` objects the way `Raid.OnIslandFirstEnter` does, then call `RefreshLandings()` and `Landing.Spawn()`. This still needs investigating.

## Island lifecycle
- `IslandGameplayManager` interfaces: `IAwake`, `ISetupIsland`, `IWipeIsland`. Island component interfaces: `IIslandFirstEnter`, `IIslandEnter`, `IIslandPlay`, `IIslandWipe`, `IIslandLeave`.
- `Island` coroutines: `SetupRoutine`, `EnterIsland`, `PlayIsland`, `WipeIsland`, `ResetIsland`, `LeaveIsland`.

## Platform and networking already in the game
- `CS.Platform.Steam.Client.Part.*`:
  - `SteamLobby` (create, join, invite, lobby data and chat)
  - `SteamPostBox` (P2P `SendNewMessage` / `GetMessage`)
  - `SteamJoining` (invites and join requests)
  - `SteamManager`
- The Steamworks.NET bindings (`Steamworks.*`) are compiled into `Assembly-CSharp`, so `SteamNetworking.SendP2PPacket` is usable directly.
- That platform layer is live. `CorePlatformSetup` adds `SteamManager`, and `BasePlatformManager.Update` → `SteamAPI.RunCallbacks()` runs on the **main thread**. Steamworks calls are only valid once `CS.Platform.BasePlatformManager.Initialized` is true.
  - `SteamPostBox` accepts every `P2PSessionRequest` and reads channel 1 (`SteamElements.ClientChannel`; 2 is the server channel and 3 is voice). The mod uses channel 7.
  - `SteamLobby` listens to every lobby callback and mirrors whichever lobby we're in. No Bad North code subscribes to the resulting `PlatformEvents`, so this is harmless.
  - `SteamJoining.OnGameLobbyJoinRequested` auto-joins invited lobbies, and `OnJoinUser` parses rich presence `connect` as `ip:port` (it throws on anything else). The mod disables both (`Patches/SteamPatches.cs`).
- There's a dev console from ReflexCLI (`[ConsoleCommand]` attributes on things like `SquadSelector.SelectNext` and `Raid.bountyMultiplier`). How to open it is still unknown.
