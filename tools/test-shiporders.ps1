$ErrorActionPreference = 'Stop'
$source = (Get-Content "$PSScriptRoot/../src/Game/CustomShipOrders.cs" -Raw) + (Get-Content "$PSScriptRoot/test-shiporders-stubs.cs" -Raw)
Add-Type -TypeDefinition $source
[BadNorthMP.Game.ShipOrdersTest]::Run()
Write-Output 'Actual ship-order helper passed registration, stale-order replacement, duplicate prevention, and post-disembark combat handoff checks (game types mocked).'
