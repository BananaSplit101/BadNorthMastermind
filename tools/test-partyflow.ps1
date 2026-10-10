$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path @(
    "$root/src/Net/PartyRoster.cs", "$root/src/Net/PartyDeployment.cs", "$root/src/Net/DeployFlow.cs", "$root/src/Game/RescueIslands.cs", "$root/src/Net/PartyLoadout.cs", "$root/src/Net/PartyFlow.cs",
    "$root/src/Net/RemoteDefender.cs", "$root/src/Net/Roles.cs",
    "$root/src/Net/Protocol.cs", "$root/src/Patches/OrderPatches.cs", "$PSScriptRoot/test-partyflow-stubs.cs"
)
[BadNorthMP.Net.PartyTests]::Run()
