# Builds a Release DLL and the files of a GitHub release in dist/release/:
#   BadNorthMP.zip  first-time install: the mod plus BepInEx (taken from your game install)
#   BadNorthMP.dll  what the in-game updater downloads
#   latest.txt      version, size and SHA-256 of the DLL; the updater reads it from the newest release
# With -Publish it also creates the GitHub release v<version> with those files (needs the GitHub CLI, signed in).
# Usage: pwsh tools/package.ps1 [-Publish] [-Notes "What changed"] [-GameDir "D:\...\BadNorth"]
param(
    [switch]$Publish,
    [string]$Notes = "",
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\BadNorth"
)
$ErrorActionPreference = "Stop"

$repo = "BananaSplit101/BadNorthMastermind"
$root = Split-Path $PSScriptRoot -Parent
$version = [regex]::Match((Get-Content "$root\src\Plugin.cs" -Raw), 'Version = "([^"]+)"').Groups[1].Value
$dist = Join-Path $root "dist"
$stage = Join-Path $dist "stage"
$release = Join-Path $dist "release"

if ($Publish) {
    # A release must be built from exactly what's on GitHub, so its tag points at the code it was built from.
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw "Publishing needs the GitHub CLI (winget install GitHub.cli), then: gh auth login" }
    if (git -C $root status --porcelain) { throw "Commit your changes first" }
    git -C $root fetch -q origin main
    if ((git -C $root rev-parse HEAD) -ne (git -C $root rev-parse origin/main)) { throw "Push main first (git push)" }
    gh release view "v$version" --repo $repo *> $null
    if ($LASTEXITCODE -eq 0) { throw "Release v$version already exists: bump Version in src/Plugin.cs" }
}

dotnet build "$root\src\BadNorthMP.csproj" -c Release -nologo -v q -p:GameDir="$GameDir"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
$dll = "$root\src\bin\Release\BadNorthMP.dll"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $release) { Remove-Item $release -Recurse -Force }
New-Item -ItemType Directory -Force "$stage\BepInEx\core", "$stage\BepInEx\plugins\BadNorthMP", $release | Out-Null

# BepInEx loader files only (no config, logs or other plugins from this machine).
foreach ($file in "winhttp.dll", "doorstop_config.ini", ".doorstop_version") {
    Copy-Item (Join-Path $GameDir $file) $stage
}
Copy-Item "$GameDir\BepInEx\core\*" "$stage\BepInEx\core"
Copy-Item $dll "$stage\BepInEx\plugins\BadNorthMP"

# Compress-Archive skips dot-files, so use .NET directly to keep .doorstop_version.
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, "$release\BadNorthMP.zip")
Remove-Item $stage -Recurse -Force
Copy-Item "$release\BadNorthMP.zip" "$dist\BadNorthMP-$version.zip" -Force

Copy-Item $dll "$release\BadNorthMP.dll"
$size = (Get-Item $dll).Length
$hash = (Get-FileHash $dll -Algorithm SHA256).Hash.ToLowerInvariant()
# ASCII with LF line endings: the updater parses it line by line.
[IO.File]::WriteAllText("$release\latest.txt", "version $version`nsize $size`nsha256 $hash`n", [Text.Encoding]::ASCII)
Write-Host "Release files for $version are in $release"

if ($Publish) {
    $body = if ($Notes) { $Notes } else { "Bad North Multiplayer $version" }
    $body += "`n`nNew install: extract BadNorthMP.zip into the Bad North folder (next to BadNorth.exe). Existing installs update from the Multiplayer screen."
    gh release create "v$version" "$release\BadNorthMP.zip" "$release\BadNorthMP.dll" "$release\latest.txt" `
        --repo $repo --target main --title "Bad North Multiplayer $version" --notes $body
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
    Write-Host "Published https://github.com/$repo/releases/tag/v$version"
}
