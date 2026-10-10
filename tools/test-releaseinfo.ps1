$ErrorActionPreference = 'Stop'
$source = (Get-Content -LiteralPath "$PSScriptRoot/../src/Net/ReleaseInfo.cs" -Raw) + @'
namespace BadNorthMP.Net
{
    public static class ReleaseInfoTest
    {
        static int checks;
        static void Check(bool ok, string why) { checks++; if (!ok) throw new System.Exception(why); }
        public static string Run(string[] published)
        {
            string hash = new string('a', 60) + "B0c9";
            var info = ReleaseInfo.Parse("version 1.2.3\r\nsize 654336\r\nsha256 " + hash + "\r\n");
            Check(info != null && info.version == "1.2.3" && info.size == 654336 && info.sha256 == hash.ToLowerInvariant(), "a published release parses (CRLF, any case)");
            Check(ReleaseInfo.Parse("size 654336\nsha256 " + hash) == null, "no version: refused");
            Check(ReleaseInfo.Parse("version 1.2.3\nsize 654336\nsha256 abc") == null, "short hash: refused");
            Check(ReleaseInfo.Parse("version 1.2.3\nsize 654336\nsha256 " + new string('g', 64)) == null, "non-hex hash: refused");
            Check(ReleaseInfo.Parse("version 1.2.3\nsize 12\nsha256 " + hash) == null, "too small to be the DLL: refused");
            Check(ReleaseInfo.Parse("version 1.2.3\nsize 99999999\nsha256 " + hash) == null, "too large: refused");
            Check(ReleaseInfo.Parse("version banana\nsize 654336\nsha256 " + hash) == null, "not a version: refused");
            Check(ReleaseInfo.Parse("") == null && ReleaseInfo.Parse(null) == null, "empty: refused");
            Check(ReleaseInfo.Newer("1.0.1", "1.0.0") && ReleaseInfo.Newer("1.10.0", "1.9.9") && ReleaseInfo.Newer("2.0", "1.99.99"), "later versions are newer");
            Check(!ReleaseInfo.Newer("1.0.0", "1.0.0") && !ReleaseInfo.Newer("0.19.2", "1.0.0") && !ReleaseInfo.Newer("", "1.0.0") && !ReleaseInfo.Newer(null, "1.0.0"), "same, older or unknown versions aren't");
            // The files tools/package.ps1 builds (and the 1.0.0 bridge in release/) must be ones the game accepts.
            foreach (string text in published) Check(ReleaseInfo.Parse(text) != null, "a latest.txt as published does not parse");
            return checks + " release checks passed";
        }
    }
}
'@
Add-Type -TypeDefinition $source
$published = [string[]]@(foreach ($path in "$PSScriptRoot/../dist/release/latest.txt", "$PSScriptRoot/../release/latest.txt") {
    if (Test-Path $path) { Get-Content -LiteralPath $path -Raw }
})
Write-Output ([BadNorthMP.Net.ReleaseInfoTest]::Run($published))
