# Bad North Mastermind

An asymmetric co-op multiplayer mod for [Bad North: Jotunn Edition](https://store.steampowered.com/app/688420/) (Steam, Windows).

Up to four defenders play one shared campaign together, each commanding their own squads. A fifth player, the **mastermind**, plays the Vikings:
- they plan every wave: what lands and on which beaches;
- they spend a war chest on Viking upgrades;
- they recruit Viking commanders that they deploy and order around in battle.

Everything runs over Steam lobbies and invites. Each player sees the battle live in their own game. Defenders can ping the island for each other: hold the middle mouse button and pick Attack, Move here, Fall back or Hold.

Found a bug? Pause the game, press **Copy log**, and paste it into your report.

## Install
1. Download `BadNorthMP.zip` from the [latest release](https://github.com/BananaSplit101/BadNorthMastermind/releases/latest).
2. Close Bad North and open its folder: Steam > Bad North > Manage > Browse local files.
3. Extract the zip there, so `winhttp.dll` and `BepInEx/` sit next to `BadNorth.exe`.
4. Start the game through Steam. **Multiplayer** is in the main menu. Enable the Steam overlay for invites.

The zip contains [BepInEx 5](https://github.com/BepInEx/BepInEx) (x86) and the mod.

## Updates
The mod checks this repository's releases for a new version and updates itself:
- **Where it looks:** it reads `latest.txt` from the newest [release](https://github.com/BananaSplit101/BadNorthMastermind/releases). That file lists the version, size and SHA-256 of the release's `BadNorthMP.dll`.
- **When it checks:** at start-up, every 15 minutes, and on joining a lobby.
- **What you see:** a notice appears, and the Multiplayer screen offers *Update to X*.
- **Safety:** the download is verified against the published size and hash before it's kept.
- **Install and restart:** swaps the DLL, keeps the old one as `BadNorthMP.dll.previous`, and restarts the game through Steam back into the lobby.

Everyone in a lobby must run the same version.

## Error reports
To help fix bugs, the mod sends error logs to [PostHog](https://posthog.com) (US cloud). The first time you press Start, a popup tells you, with *Leave it on* and *Turn off*. You can change it later in the game's Settings (*Error reports*) or in `BepInEx/config/com.bananasplit.badnorthmp.cfg` (`[Diagnostics] Send error reports`). Turned off, nothing is sent, including anything kept from before.

**What's sent:**
- the session's log lines (the mod's, other mods', the game's);
- errors, with their stack traces;
- what the game was doing: your role, menu, map or battle, the island, loading and party state, frame rate;
- the mod version, the game and BepInEx versions, the installed mods, your OS, CPU, GPU and memory;
- events like joining a lobby or starting an island.

When something goes wrong, it becomes an *incident*: everyone in the lobby sends their recent log under one code, so every side of a multiplayer bug is there. **Report bug** in the pause menu does the same, with your description, and also posts your description and recent log (names taken out) to the developer's bug-report channel on Discord.

**What's never sent:** Steam names and Steam IDs (each player becomes a label like `player-3f9a2c`), and your Windows user name. Reports are tied to a random install id, not your Steam account. Logs wait in `BepInEx/BadNorthMP-logs/` until they're sent, so a crash's last lines go at the next launch.

For the developer: `python tools/fetch-incident.py <code>` downloads an incident, every member's log included, into `dist/incidents/<code>/`. It needs a PostHog personal API key in `POSTHOG_PERSONAL_KEY`; never commit the key.

## Building
Requirements:
- Bad North installed with BepInEx 5 x86 (install the zip above once);
- the .NET SDK (any recent version builds the `net35` target through reference packages);
- PowerShell 7 (`pwsh`) for the tools;
- the [GitHub CLI](https://cli.github.com/) (`gh`, signed in with `gh auth login`) to publish releases.

```
dotnet build src/BadNorthMP.csproj
```
The build copies the DLL to the game's `BepInEx/plugins/BadNorthMP/`. Close the game first, because it locks the DLL. If the game isn't in the default Steam folder, pass `-p:GameDir="D:\path\to\BadNorth"`.

### Tests and checks
```
pwsh tools/test-mastermindupgrades.ps1   # and every other tools/test-*.ps1 (test-telemetry.ps1: error reports)
python tools/check-game-patch-targets.py # every Harmony/reflection target exists in the installed game
python tools/check-battle-messages.py    # battle-stream message ids stay in their range
python tools/check-spectator-gate.py
```
The tests compile the real sources against small stubs of the game, Unity and Steam, then run them on the .NET SDK.

### Releasing
1. Bump `Version` in [`src/Plugin.cs`](src/Plugin.cs), then commit and push to `main`.
2. Run `pwsh tools/package.ps1 -Publish -Notes "What changed"`. It builds Release and creates the GitHub release `v<version>` with three files:
   - `BadNorthMP.zip`, the full install with BepInEx;
   - `BadNorthMP.dll`;
   - `latest.txt`, the version, size and SHA-256.

Running games pick the update up on their next check. Without `-Publish`, the files are only built into `dist/release/`.

## Layout
| Path | What |
| --- | --- |
| `src/Net/` | Steam lobby and P2P transport, message routing, the party/draft flow, voting, loading gates, campaign and battle streaming, the updater |
| `src/Game/` | The mastermind's economy, wave schedule, custom waves, upgrades and Viking commanders |
| `src/Spectator/` | Watching another player's campaign and battle: the puppet world, the native lifecycle and the spectator guards |
| `src/UI/` | The mastermind HUD and war chest, commander draft, vote panel and Multiplayer screen, built from the game's own widgets |
| `src/Patches/` | Harmony patches on the game |
| `src/Traits/` | Commander traits: GMW's Traits Galore and Vanilla Traits Overhaul |
| `assets/` | Commander flags, sail-in war cries and trait icons (from GMW's mods, with permission) embedded in the DLL |
| `release/` | The update channel of 1.0.0, which read the newest version from here instead of from releases. It points at 1.0.1 and doesn't change again |
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
  - Unit Integrity Display;
  - Traits Galore: six of its new commander traits (all but Mad and Unpopular) and their icons;
  - Vanilla Traits Overhaul: new stats and behaviours for the game's 13 commander traits.
- **GMW's Viking Invasion**, whose Viking controls the mastermind's commanding follows: the game's own tile grid, path line and squad glow, used for Vikings (written for this mod, not ported).
- **ABaLaQiYaShanMaiI's BadNorthNewMode (MIT)**, for the Viking control rules: settling in place, unsticking, torches only at the house, box select, and orders from the ship.
- The install zip bundles [BepInEx](https://github.com/BepInEx/BepInEx) (LGPL-2.1) with HarmonyX, MonoMod and Mono.Cecil (MIT).
- **Bad North** is made by Plausible Concept and published by Raw Fury. This project is not affiliated with them.
