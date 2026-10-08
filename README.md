# BadNorthMP

A work-in-progress asymmetric multiplayer mod for [Bad North: Jotunn Edition](https://store.steampowered.com/app/688420/).
Defenders each command their own squads, while a "mastermind" player summons the Viking waves.

**Status:** First playable slice. The host creates a Steam lobby and invites a friend. The host defends, and the friend is the mastermind who decides when the host's Viking waves launch (`Multiplayer` in the main menu and pause menu). The island itself isn't synced yet, so the mastermind sees a status readout rather than the battle.

## Making a test build for a friend
```
pwsh tools/package.ps1
```
This produces `dist/BadNorthMP-<version>.zip`, containing the BepInEx loader, the plugin and `INSTALL.txt`. Close the game first, so your own install gets the same DLL (the version check refuses mismatched builds). Bump `Plugin.Version` whenever you send a new build.

## Setup
1. Install **BepInEx 5 x86** (the game is 32-bit): extract `BepInEx_win_x86_5.4.23.x.zip` from
   https://github.com/BepInEx/BepInEx/releases into the game folder
   (`C:\Program Files (x86)\Steam\steamapps\common\BadNorth`). Run the game once.
2. Optional: set `[Logging.Console] Enabled = true` in `BepInEx\config\BepInEx.cfg` for a live log window.
3. Build. The build also copies the DLL to `BepInEx\plugins\BadNorthMP\`:
   ```
   dotnet build src/BadNorthMP.csproj
   ```
   If the game is installed elsewhere, pass `-p:GameDir="D:\path\to\BadNorth"`. Close the game before rebuilding, because the running game locks the DLL.

## Debug hotkeys (on an island)
| Key | Action |
| --- | --- |
| F7  | Toggle the overlay |
| F8  | Move the selected squad (or the first one) to a random free spot |
| F9  | Launch the next Viking wave now |
| F10 | Dump squads, vikings and waves to the log |

The log is at `BepInEx\LogOutput.log`.

## Reading the game code
```
dotnet tool install -g ilspycmd --version 9.1.0.7988
set DOTNET_ROLL_FORWARD=Major
ilspycmd -p -o decompiled "C:\Program Files (x86)\Steam\steamapps\common\BadNorth\BadNorth_Data\Managed\Assembly-CSharp.dll"
```
`decompiled/` is gitignored. Don't commit the game's code. See [docs/game-map.md](docs/game-map.md) for where things are.
