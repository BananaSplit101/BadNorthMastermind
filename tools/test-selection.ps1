$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path @("$root/src/Net/SelectionLifecycle.cs", "$root/src/Spectator/NativeLifecyclePatches.cs", "$PSScriptRoot/test-selection-stubs.cs")
[BadNorthMP.Tests.SelectionTests]::Run()
