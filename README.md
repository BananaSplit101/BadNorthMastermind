# Bad North Mastermind

An asymmetric co-op multiplayer mod for [Bad North: Jotunn Edition](https://store.steampowered.com/app/688420/) (Steam, Windows).

Up to four defenders play one shared campaign together, each commanding their own squads. A fifth player, the **mastermind**, plays the Vikings:
- they plan every wave: what lands and on which beaches;
- they spend a war chest on Viking upgrades;
- they recruit Viking commanders that they deploy and order around in battle.

Everything runs over Steam lobbies and invites. Each player sees the battle live in their own game.

## Install
1. Download [`release/BadNorthMP.zip`](https://github.com/BananaSplit101/BadNorthMastermind/raw/main/release/BadNorthMP.zip).
2. Close Bad North and open its folder: Steam > Bad North > Manage > Browse local files.
3. Extract the zip there, so `winhttp.dll` and `BepInEx/` sit next to `BadNorth.exe`.
4. Start the game through Steam. **Multiplayer** is in the main menu. Enable the Steam overlay for invites.

The zip contains [BepInEx 5](https://github.com/BepInEx/BepInEx) (x86) and the mod.

## Updates
The mod checks this repository for a new version and updates itself:
- **Where it looks:** it reads `release/latest.txt` from the `main` branch, which lists the version, size and SHA-256 of `release/BadNorthMP.dll`.
- **When it checks:** at start-up, every 15 minutes, and on joining a lobby.
- **What you see:** a notice appears, and the Multiplayer screen offers *Update to X*.
- **Safety:** the download is verified against the published size and hash before it's kept.
- **Install and restart:** swaps the DLL, keeps the old one as `BadNorthMP.dll.previous`, and restarts the game through Steam back into the lobby.

Everyone in a lobby must run the same version.

## Building
Requirements:
- Bad North installed with BepInEx 5 x86 (install the zip above once);
- the .NET SDK (any recent version builds the `net35` target through reference packages);
- PowerShell 7 (`pwsh`) for the tools.

```
dotnet build src/BadNorthMP.csproj
```
The build copies the DLL to the game's `BepInEx/plugins/BadNorthMP/`. Close the game first, because it locks the DLL. If the game isn't in the default Steam folder, pass `-p:GameDir="D:\path\to\BadNorth"`.

### Tests and checks
```
pwsh tools/test-mastermindupgrades.ps1   # and every other tools/test-*.ps1
python tools/check-game-patch-targets.py # every Harmony/reflection target exists in the installed game
python tools/check-battle-messages.py    # battle-stream message ids stay in their range
python tools/check-spectator-gate.py
```
The tests compile the real sources against small stubs of the game, Unity and Steam, then run them on the .NET SDK.

### Releasing
1. Bump `Version` in [`src/Plugin.cs`](src/Plugin.cs).
2. Run `pwsh tools/package.ps1`. It builds Release and refreshes `release/`:
   - `BadNorthMP.dll`;
   - `BadNorthMP.zip`, the full install with BepInEx;
   - `latest.txt`, the version, size and SHA-256.
3. Commit and push `release/` to `main`. Running games pick the update up on their next check.

## Layout
| Path | What |
| --- | --- |
| `src/Net/` | Steam lobby and P2P transport, message routing, the party/draft flow, voting, loading gates, campaign and battle streaming, the updater |
| `src/Game/` | The mastermind's economy, wave schedule, custom waves, upgrades and Viking commanders |
| `src/Spectator/` | Watching another player's campaign and battle: the puppet world, the native lifecycle and the spectator guards |
| `src/UI/` | The mastermind HUD and war chest, commander draft, vote panel and Multiplayer screen, built from the game's own widgets |
| `src/Patches/` | Harmony patches on the game |
| `assets/` | Commander flags and sail-in war cries (from GMW's mods, with permission) embedded in the DLL |
| `docs/` | Notes on the game's internals and the unit-visual stream format |
| `tools/` | The package script, test suites and checks |

## Reading the game code
```
dotnet tool install -g ilspycmd
ilspycmd -p -o decompiled "C:\Program Files (x86)\Steam\steamapps\common\BadNorth\BadNorth_Data\Managed\Assembly-CSharp.dll"
```
`decompiled/` is gitignored. **Never commit the game's code.** See [docs/game-map.md](docs/game-map.md) for where things are.

## Credits
- **GMW's mods**, ported with permission:
  - Viking commander traits and flags;
  - Commanders Sail In, including its war cries and flag artwork;
  - Hidden Lineup;
  - Unit Integrity Display.
- **ABaLaQiYaShanMaiI's BadNorthNewMode (MIT)**, for the Viking control rules: settling in place, unsticking, torches only at the house, box select, and orders from the ship.
- The install zip bundles [BepInEx](https://github.com/BepInEx/BepInEx) (LGPL-2.1) with HarmonyX, MonoMod and Mono.Cecil (MIT).
- **Bad North** is made by Plausible Concept and published by Raw Fury. This project is not affiliated with them.
