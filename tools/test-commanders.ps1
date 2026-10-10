$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sources = (Get-Content "$root/src/Game/CommanderRoster.cs" -Raw) + "`n" + (Get-Content "$root/src/Game/EconomyConfig.cs" -Raw)
$test = @'
namespace BadNorthMP.Game {
 public static class CommanderTests {
  static int checks;
  static void Check(bool ok, string why) { checks++; if (!ok) throw new System.Exception(why); }
  public static string Run() {
   var roster = new System.Collections.Generic.List<VikingCommander>();
   var none = new System.Collections.Generic.HashSet<int>();
   string why;
   // Price: multiplier x unit cost x squad size; invalid types and locked costs can't be bought.
   Check(CommanderRoster.Price(4, 0) == EconomyConfig.CommanderCostMultiplier * 4 * 9, "swordsman commander price");
   Check(CommanderRoster.Price(9, 3) == EconomyConfig.CommanderCostMultiplier * 9 * 3, "brute commander price");
   // Squad sizes by type: Sword, Shield, Archer, Brute, Berserker, Axe thrower, Brute archer, Champion.
   int[] sizes = { 9, 7, 6, 3, 5, 7, 3, 7 };
   Check(EconomyConfig.CommanderSquadSize.Length == sizes.Length, "a squad size for every Viking type");
   for (int type = 0; type < sizes.Length; type++) Check(CommanderRoster.SquadSize(type) == sizes[type], "commander squad size of type " + type);
   Check(CommanderRoster.Price(0, 0) == 0 && CommanderRoster.Price(4, 99) == 0, "locked or invalid type has no price");
   Check(!CommanderRoster.CanBuy(roster, 0, 108, 107f, out why), "can't buy without enough war chest");
   Check(CommanderRoster.CanBuy(roster, 0, 108, 108f, out why), "buy with exact war chest");
   Check(!CommanderRoster.CanBuy(roster, 99, 72, 500f, out why), "invalid type rejected");
   for (int i = 1; i <= EconomyConfig.MaxCommanders; i++) roster.Add(new VikingCommander { id = i, type = 0 });
   Check(!CommanderRoster.CanBuy(roster, 0, 72, 1000f, out why), "no more than the maximum owned");
   // Deploying: ready ones only, at most two per island, then rest exactly one island.
   var deployed = new System.Collections.Generic.HashSet<int>();
   Check(CommanderRoster.CanDeploy(roster[0], deployed, out why), "ready commander deploys");
   deployed.Add(1); CommanderRoster.MarkDeployed(roster[0]);
   Check(CommanderRoster.Status(roster[0], deployed) == CommanderStatus.Deployed, "status: on this island");
   Check(!CommanderRoster.CanDeploy(roster[0], deployed, out why), "same commander can't land twice");
   deployed.Add(2); CommanderRoster.MarkDeployed(roster[1]);
   Check(!CommanderRoster.CanDeploy(roster[2], deployed, out why), "island cap of two");
   CommanderRoster.IslandBegins(roster);
   Check(CommanderRoster.Status(roster[0], none) == CommanderStatus.Resting && !CommanderRoster.CanDeploy(roster[0], none, out why), "rests on the next island");
   Check(CommanderRoster.Status(roster[2], none) == CommanderStatus.Ready, "unused commanders stay ready");
   CommanderRoster.IslandBegins(roster);
   Check(CommanderRoster.Status(roster[0], none) == CommanderStatus.Ready && CommanderRoster.CanDeploy(roster[0], none, out why), "ready again the island after");
   CommanderRoster.IslandBegins(roster); Check(roster[0].rest == 0, "rest never goes negative");
   // Copies are independent (retry snapshots).
   var copy = CommanderRoster.Copy(roster); copy[0].rest = 2; Check(roster[0].rest == 0, "copy doesn't alias");
   // Save format round trip and validation.
   roster[3].rest = 1;
   using (var stream = new System.IO.MemoryStream()) {
    var w = new System.IO.BinaryWriter(stream); CommanderRoster.Write(w, roster, 5); w.Flush(); stream.Position = 0;
    int next; var read = CommanderRoster.Read(new System.IO.BinaryReader(stream), out next);
    Check(next == 5 && read.Count == 4 && read[3].rest == 1 && read[2].id == 3, "roster round trip");
   }
   // Traits: saved since save version 3; version 2 rosters (no trait byte) still load, with no trait yet.
   roster[0].trait = 11; roster[1].trait = 3;
   using (var stream = new System.IO.MemoryStream()) {
    var w = new System.IO.BinaryWriter(stream); CommanderRoster.Write(w, roster, 5); w.Flush(); stream.Position = 0;
    int next; var read = CommanderRoster.Read(new System.IO.BinaryReader(stream), out next);
    Check(read[0].trait == 11 && read[1].trait == 3 && read[2].trait == 0, "traits round trip");
   }
   using (var stream = new System.IO.MemoryStream()) {
    var w = new System.IO.BinaryWriter(stream); CommanderRoster.Write(w, roster, 5, false, false); w.Flush(); stream.Position = 0;
    int next; var read = CommanderRoster.Read(new System.IO.BinaryReader(stream), out next, false, false);
    Check(read.Count == 4 && read[0].trait == 0 && stream.Position == stream.Length, "version 2 roster loads without traits");
   }
   Check(CommanderRoster.Copy(roster)[0].trait == 11, "copies keep the trait");
   bool badTrait = false;
   roster[2].trait = EconomyConfig.VikingTraitCount + 1;
   using (var stream = new System.IO.MemoryStream()) {
    var w = new System.IO.BinaryWriter(stream); CommanderRoster.Write(w, roster, 5); w.Flush(); stream.Position = 0;
    try { int next; CommanderRoster.Read(new System.IO.BinaryReader(stream), out next); } catch (System.IO.IOException) { badTrait = true; }
   }
   Check(badTrait, "unknown traits are rejected");
   roster[2].trait = 0;
   bool rejected = false;
   using (var stream = new System.IO.MemoryStream()) {
    var w = new System.IO.BinaryWriter(stream); CommanderRoster.Write(w, roster, 3); w.Flush(); stream.Position = 0;
    try { int next; CommanderRoster.Read(new System.IO.BinaryReader(stream), out next); } catch (System.IO.IOException) { rejected = true; }
   }
   Check(rejected, "ids at or past the next id are rejected");
   // Each commander owned makes the next dearer (+50% each); selling gives back a quarter of what it cost.
   int basePrice = CommanderRoster.Price(4, 0);
   Check(CommanderRoster.Price(4, 0, 0) == basePrice && CommanderRoster.Price(4, 0, 1) == (int)System.Math.Round(basePrice * 1.5, System.MidpointRounding.AwayFromZero) &&
    CommanderRoster.Price(4, 0, 2) == basePrice * 2 && CommanderRoster.Price(4, 0, 3) == (int)System.Math.Round(basePrice * 2.5, System.MidpointRounding.AwayFromZero), "commanders get dearer the more you own");
   Check(CommanderRoster.Price(3, 3, 1) == 41, "a +50% price rounds half up (27 x 1.5 = 40.5)");
   var bought = new VikingCommander { id = 9, type = 0, price = 101 };
   Check(CommanderRoster.SellValue(bought, 4) == 25, "sells for a quarter of what it cost, rounded down");
   Check(CommanderRoster.SellValue(new VikingCommander { id = 9, type = 0 }, 4) == basePrice / 4, "an older commander (price unknown) sells for a quarter of its base price");
   var here = new System.Collections.Generic.HashSet<int> { 9 };
   Check(!CommanderRoster.CanSell(bought, here, out why) && CommanderRoster.CanSell(bought, none, out why) && CommanderRoster.CanSell(bought, null, out why), "can't sell one fighting on the island");
   Check(!CommanderRoster.CanSell(null, none, out why), "an unknown commander can't be sold");
   // Prices: saved since save version 4; version 3 rosters (traits, no prices) still load.
   roster[0].price = 123;
   Check(CommanderRoster.Copy(roster)[0].price == 123, "copies keep the price");
   using (var stream = new System.IO.MemoryStream()) {
    var w = new System.IO.BinaryWriter(stream); CommanderRoster.Write(w, roster, 5); w.Flush(); stream.Position = 0;
    int next; var read = CommanderRoster.Read(new System.IO.BinaryReader(stream), out next);
    Check(read[0].price == 123 && read[1].price == 0 && stream.Position == stream.Length, "prices round trip");
   }
   using (var stream = new System.IO.MemoryStream()) {
    var w = new System.IO.BinaryWriter(stream); CommanderRoster.Write(w, roster, 5, true, false); w.Flush(); stream.Position = 0;
    int next; var read = CommanderRoster.Read(new System.IO.BinaryReader(stream), out next, true, false);
    Check(read[0].price == 0 && read[0].trait == 11 && stream.Position == stream.Length, "version 3 roster loads without prices");
   }
   return checks + " commander roster checks passed against the actual CommanderRoster source.";
  }
 }
}
'@
Add-Type -TypeDefinition ($sources + "`n" + $test)
[BadNorthMP.Game.CommanderTests]::Run()
