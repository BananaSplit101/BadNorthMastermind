$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$tally = Get-Content "$root/src/Net/VoteTally.cs" -Raw
$test = @'
namespace BadNorthMP.Net {
 public static class VoteTests {
  static int checks;
  static void Check(bool ok, string why) { checks++; if (!ok) throw new System.Exception(why); }
  static System.Collections.Generic.Dictionary<ulong, int> Votes(params int[] pairs) {
   var votes = new System.Collections.Generic.Dictionary<ulong, int>();
   for (int i = 0; i < pairs.Length; i += 2) votes[(ulong)pairs[i]] = pairs[i + 1];
   return votes;
  }
  public static string Run() {
   var voters = new System.Collections.Generic.List<ulong> { 2, 3, 4 };
   // Nothing happens without votes; one vote starts waiting for the others.
   Check(!VoteTally.Ready(Votes(), voters, 100f, 10f), "empty vote resolved");
   Check(!VoteTally.Ready(Votes(2, 5), voters, 5f, 20f), "resolved before everyone voted or the countdown ended");
   // Everyone voted: decide at once. Countdown over: decide with whoever voted.
   Check(VoteTally.Ready(Votes(2, 5, 3, 5, 4, 7), voters, 5f, 20f), "all voted but still waiting");
   Check(VoteTally.Ready(Votes(2, 5), voters, 20f, 20f), "countdown ended but still waiting");
   // Majority wins; Next Turn (-2) is an option like any island.
   Check(VoteTally.Winner(Votes(2, 5, 3, 5, 4, 7), new System.Random(1)) == 5, "majority lost");
   Check(VoteTally.Winner(Votes(2, -2, 3, -2, 4, 7), new System.Random(1)) == -2, "Next Turn majority lost");
   // Ties: only the tied options can win, and both can.
   var tied = Votes(2, 5, 3, 7);
   bool five = false, seven = false;
   for (int seed = 0; seed < 64; seed++) { int w = VoteTally.Winner(tied, new System.Random(seed)); Check(w == 5 || w == 7, "tie picked an option nobody tied on"); five |= w == 5; seven |= w == 7; }
   Check(five && seven, "tie break is not random");
   Check(VoteTally.Leaders(Votes(2, 9, 3, 5, 4, 9)).Count == 1, "clear winner reported as tie");
   // Changing a vote replaces it (one vote per player).
   var change = Votes(2, 5); change[2] = 7; Check(change.Count == 1 && VoteTally.Count(change, 7) == 1 && VoteTally.Count(change, 5) == 0, "vote change counted twice");
   // A player who leaves loses their vote; votes for options that became invalid are dropped.
   var prune = Votes(2, 5, 3, 7, 9, 5);
   Check(VoteTally.Prune(prune, voters, null) && !prune.ContainsKey(9) && prune.Count == 2, "departed player's vote kept");
   Check(VoteTally.Prune(prune, voters, option => option != 7) && !prune.ContainsKey(3) && prune.Count == 1, "invalid island vote kept");
   Check(!VoteTally.Prune(prune, voters, option => true), "valid votes pruned");
   // After a departure, the remaining voters can still finish the vote.
   Check(VoteTally.Ready(Votes(2, 5, 3, 5), new System.Collections.Generic.List<ulong> { 2, 3 }, 1f, 20f), "vote stuck after a player left");
   // Solo party: a single click decides.
   Check(VoteTally.Ready(Votes(2, 4), new System.Collections.Generic.List<ulong> { 2 }, 0f, 20f), "solo vote waited");
   return checks + " island vote checks passed against the actual VoteTally source.";
  }
 }
}
'@
Add-Type -TypeDefinition ($tally + "`n" + $test)
[BadNorthMP.Net.VoteTests]::Run()
