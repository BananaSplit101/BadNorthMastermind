$ErrorActionPreference = 'Stop'
$sources = @(
    "$PSScriptRoot/../src/Net/CampaignSync.cs",
    "$PSScriptRoot/../src/Spectator/SpectatorSession.cs",
    "$PSScriptRoot/test-campaignsync-stubs.cs"
)
Add-Type -Path $sources -IgnoreWarnings -WarningAction SilentlyContinue
[BadNorthMP.Net.CampaignSyncTest]::Run()
Write-Output 'Actual CampaignSync/SpectatorSession source passed bounded chunks, revisions, late join, campaign close, native teardown ordering, map/battle transitions, buffered history, failure cleanup, waiting for the profile, clearing a half-loaded campaign, giving up after repeated failures, and save restoration checks (Steam/Unity mocked).'
