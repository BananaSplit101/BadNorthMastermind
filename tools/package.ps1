# Builds a Release DLL, zips it together with BepInEx (taken from your game install) for first-time installs,
# and refreshes release/ (the files the in-game updater reads from GitHub). Commit and push release/ to publish.
# Usage: pwsh tools/package.ps1 [-GameDir "D:\...\BadNorth"]
param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\BadNorth"
)
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$version = [regex]::Match((Get-Content "$root\src\Plugin.cs" -Raw), 'Version = "([^"]+)"').Groups[1].Value
$dist = Join-Path $root "dist"
$stage = Join-Path $dist "stage"
$zip = Join-Path $dist "BadNorthMP-$version.zip"

dotnet build "$root\src\BadNorthMP.csproj" -c Release -nologo -v q -p:GameDir="$GameDir"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force "$stage\BepInEx\core", "$stage\BepInEx\plugins\BadNorthMP" | Out-Null

# BepInEx loader files only (no config, logs or other plugins from this machine).
foreach ($file in "winhttp.dll", "doorstop_config.ini", ".doorstop_version") {
    Copy-Item (Join-Path $GameDir $file) $stage
}
Copy-Item "$GameDir\BepInEx\core\*" "$stage\BepInEx\core"
$dll = "$root\src\bin\Release\BadNorthMP.dll"
Copy-Item $dll "$stage\BepInEx\plugins\BadNorthMP"

if (Test-Path $zip) { Remove-Item $zip }
# Compress-Archive skips dot-files, so use .NET directly to keep .doorstop_version.
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)
Remove-Item $stage -Recurse -Force

# release/: what the in-game updater downloads (latest.txt, then the DLL it describes), plus the install zip.
$release = Join-Path $root "release"
New-Item -ItemType Directory -Force $release | Out-Null
Copy-Item $dll "$release\BadNorthMP.dll" -Force
Copy-Item $zip "$release\BadNorthMP.zip" -Force
$size = (Get-Item $dll).Length
$hash = (Get-FileHash $dll -Algorithm SHA256).Hash.ToLowerInvariant()
# ASCII with LF line endings: the updater parses it line by line.
[IO.File]::WriteAllText("$release\latest.txt", "version $version`nsize $size`nsha256 $hash`n", [Text.Encoding]::ASCII)

Write-Host "Packaged $zip"
Write-Host "Release files updated in $release (version $version). Commit and push them to publish the update."
