$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path @("$root/src/Net/SquadAbilityMirror.cs", "$PSScriptRoot/test-abilitymirror-stubs.cs")
[BadNorthMP.Tests.AbilityMirrorTests]::Run()
