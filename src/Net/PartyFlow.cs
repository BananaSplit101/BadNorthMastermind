using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BadNorthMP.Game;
using BadNorthMP.Spectator;
using BadNorthMP.UI;
using HarmonyLib;
using Steamworks;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.CampaignGeneration;
using Voxels.TowerDefense.UI;

namespace BadNorthMP.Net
{
    internal static class PartyFlow
    {
        private const string IdleStatus = "The mastermind starts the campaign, then defenders choose their squads.";
        public static PartyRoster State = new PartyRoster();
        public static string Status = IdleStatus;
        public static int Difficulty = 2, SaveChoice = -1;
        public static readonly bool[] Saved = new bool[5];
        public static readonly bool[] Occupied = new bool[5];
        public static readonly string[] SaveDetails = new string[5];
        // Each multiplayer save's difficulty (0 unknown) and "Allow level restarts", from the leader's catalogue.
        public static readonly int[] SaveDifficulty = new int[5];
        public static readonly bool[] SaveAllowReplays = new bool[5];
        public static bool CatalogReady, AllowReplays = true;
        public static int NewSaveSlot = -1;
        public static bool Active => SteamSession.Instance && SteamSession.Instance.InLobby && State.phase != 0;
        public static bool Deployed => Active && State.phase == 3;
        public static bool Loading => Active && State.phase == 2;
        public static ulong AuthorityId => Active ? State.authority : 0;
        public static bool ExecutingOrder;
        public static bool NewMapPending, BeginFailed;
        private static CSteamID lobby = CSteamID.Nil;
        private static float nextSync, nextBegin, nextCatalog;
        // Guests: when the host's party state last arrived, and when we last tried to recover from silence.
        private static float lastStateAt, nextRecover;
        /// <summary>When this player saw the party start (phase 2 or later); 0 before that.</summary>
        public static float StartedAt { get; private set; }
        private static int beganToken, pendingToken;
        private static DefenderSeat pendingPick;
        public static bool PendingPick => pendingPick != null && pendingToken == State.token && State.phase == 1;
        private static void PreservePendingPick(PartyRoster incoming)
        {
            if (!PendingPick || incoming.phase != 1 || incoming.token != pendingToken) { pendingPick = null; return; }
            var mine = incoming.Find(Session.Me.m_SteamID);
            if (mine == null) { pendingPick = null; return; }
            bool matched = mine.ready == pendingPick.ready && mine.choices.Length == pendingPick.choices.Length;
            for (int i = 0; matched && i < mine.choices.Length; i++)
                matched = mine.choices[i] == pendingPick.choices[i] && mine.traits[i] == pendingPick.traits[i] && mine.items[i] == pendingPick.items[i];
            if (matched) pendingPick = null;
            else { mine.choices = (int[])pendingPick.choices.Clone(); mine.traits = (string[])pendingPick.traits.Clone();
                mine.items = (string[])pendingPick.items.Clone(); mine.ready = pendingPick.ready; }
        }
        private static bool headersLoading, catalogSent;
        private static SteamSession Session => SteamSession.Instance;
        public static string ChoiceName(int choice) => "Commander " + (choice + 1);
        public static DefenderSeat Mine => State.Find(Session.Me.m_SteamID);
        public static bool Owns(CSteamID player, HeroDefinition hero) => hero != null && Profile.campaign != null &&
            State.Owns(player.m_SteamID, Profile.campaign.heroes.IndexOf(hero));
        public static bool CanSelect(EnglishSquad squad) => !Active || (!Roles.IsMastermind && Owns(Session.Me, squad.hero));
        public static bool CanOrder(EnglishSquad squad, NavSpot target)
        {
            if (!Active) return true;
            if (!squad || (!ExecutingOrder && !CanSelect(squad))) return false;
            // Native MoveToDirect swaps an occupied destination. Never move a teammate's squad this way.
            var occupant = target ? target.occupant : null;
            return !occupant || occupant == squad.navSpotOccupant ||
                Owner(occupant.enSquad) == Owner(squad);
        }
        private static ulong Owner(EnglishSquad squad)
        {
            if (!squad || Profile.campaign == null) return 0;
            var seat = State.OwnerOf(Profile.campaign.heroes.IndexOf(squad.hero));
            return seat != null ? seat.player : 0;
        }

        public static void Init()
        {
            Router.On(MsgType.PartyState, (sender, r) =>
            {
                RefreshContext();
                if (sender != Session.Owner || Session.IsHost) return;
                var state = PartyRoster.Read(r);
                if (state.token < State.token || (state.token == State.token && state.phase < State.phase)) return;
                if (state.token == State.token && state.phase == 3 &&
                    (Roles.IsDefender && Deployed || state.round < State.round)) return;
                bool transition = state.token != State.token || state.phase != State.phase;
                PreservePendingPick(state);
                var before = State;
                State = state; Status = r.ReadString();
                if (!transition && state.phase == 3) AnnounceNewSeats(before, state);
                lastStateAt = Time.unscaledTime;
                if (transition)
                {
                    ModLog.Info($"Party state from the host: phase {state.phase}, draft {state.token}");
                    if (state.phase >= 2 && StartedAt <= 0f) StartedAt = Time.unscaledTime;
                }
                if (state.phase == 1) MultiplayerScreen.RequestOpen();
                else if (transition)
                {
                    MultiplayerScreen.CloseForBattle();
                    if (state.phase == 2) GameNotify.Post("All defenders ready. " + SteamSession.NameOf(new CSteamID(state.authority)) + " is loading the campaign...");
                }
            });
            Router.On(MsgType.PartyStateRequest, (sender, r) =>
            {
                RefreshContext();
                if (!Session.IsHost || !Session.Members().Contains(sender) || !Active) return;
                ModLog.Info(SteamSession.NameOf(sender) + " reconnected and asked for the party state");
                Broadcast();
            });
            Session.ConnectionReset += member =>
            {
                // Whatever the session dropped gets superseded right away.
                if (Session.IsHost && Active) Broadcast();
            };
            Router.On(MsgType.PartyPick, (sender, r) =>
            {
                RefreshContext();
                if (!Session.IsHost || !Session.Members().Contains(sender)) return;
                int token = r.ReadInt32(), count = r.ReadByte();
                if (count < 1 || count > 2) return;
                int[] types = new int[count];
                for (int i = 0; i < count; i++) types[i] = r.ReadByte();
                bool ready = r.ReadBoolean();
                string[] traits = null, items = null;
                if (r.BaseStream.Position < r.BaseStream.Length) { traits = PartyRoster.ReadNames(r); items = PartyRoster.ReadNames(r); }
                if (State.Pick(sender.m_SteamID, token, types, ready, traits, items))
                { ModLog.Info("Party Ready: " + SteamSession.NameOf(sender) + " = " + ready + ", draft " + token); Broadcast(); }
            });
            Router.On(MsgType.PartyBegin, (sender, r) =>
            {
                RefreshContext();
                if (sender != Session.Owner) return;
                var state = PartyRoster.Read(r);
                int slot = r.ReadInt32();
                if (r.BaseStream.Length - r.BaseStream.Position >= 5)
                { NewSaveSlot = r.ReadInt32(); AllowReplays = r.ReadBoolean(); }
                else { NewSaveSlot = -1; AllowReplays = true; }
                if (state.phase != 2 || !state.AllReady || state.authority != Session.Me.m_SteamID ||
                    state.token != State.token || state.token == beganToken || !MembersMatch(state)) return;
                State = state; beganToken = state.token;
                ModLog.Info("Party Begin accepted: draft " + state.token + ", leader " + state.authority);
                MultiplayerGame.Instance.StartCoroutine(Begin(slot));
            });
            Router.On(MsgType.PartyOwnership, (sender, r) =>
            {
                RefreshContext();
                if (sender.m_SteamID != State.authority || !Active) return;
                var state = PartyRoster.Read(r);
                if (state.token != State.token || state.phase != 3 || state.round < State.round || !KeepsPlayers(state)) return;
                bool transition = State.phase != 3;
                var before = State;
                State = state; Status = "Choose your squad below. Only its owner can issue orders.";
                if (transition) MultiplayerScreen.CloseForBattle();
                else AnnounceNewSeats(before, state);
                if (Session.IsHost) Broadcast();
            });
            Router.On(MsgType.PartyFailure, (sender, r) =>
            {
                RefreshContext();
                if (!Session.IsHost || sender.m_SteamID != State.authority || r.ReadInt32() != State.token) return;
                Status = r.ReadString();
                State.phase = 1; ++State.token;
                foreach (var seat in State.seats) seat.ready = false;
                Broadcast(); MultiplayerScreen.Open();
            });
            Router.On(MsgType.PartyCatalog, (sender, r) =>
            {
                RefreshContext();
                if (!Session.IsHost || sender != Roles.DefenderId) return;
                for (int i = 0; i < Saved.Length; i++) Saved[i] = r.ReadBoolean();
                for (int i = 0; i < Saved.Length; i++)
                {
                    Occupied[i] = Saved[i]; SaveDetails[i] = "";
                    if (r.BaseStream.Position < r.BaseStream.Length)
                    { Occupied[i] = r.ReadBoolean(); SaveDetails[i] = r.ReadString(); }
                }
                if (r.BaseStream.Position < r.BaseStream.Length)
                {
                    State.allowedTraits = PartyRoster.ReadNames(r); State.allowedItems = PartyRoster.ReadNames(r);
                    foreach (var seat in State.seats)
                        for (int i = 0; i < seat.choices.Length; i++)
                        {
                            if (!PartyRoster.Allowed(State.allowedTraits, seat.traits[i])) { seat.traits[i] = ""; seat.ready = false; }
                            if (!PartyRoster.Allowed(State.allowedItems, seat.items[i])) { seat.items[i] = ""; seat.ready = false; }
                        }
                }
                for (int i = 0; i < Saved.Length; i++)
                {
                    SaveDifficulty[i] = 0; SaveAllowReplays[i] = true;
                    if (r.BaseStream.Position < r.BaseStream.Length) { SaveDifficulty[i] = r.ReadByte(); SaveAllowReplays[i] = r.ReadBoolean(); }
                }
                CatalogReady = true;
            });
            Router.On(MsgType.PartyDeleteSave, (sender, r) =>
            {
                RefreshContext();
                int slot = r.ReadInt32();
                if (!Roles.IsDefender || sender != Session.Owner || Active || slot < 0 || slot >= MultiplayerCampaigns.SlotCount) return;
                var meta = MultiplayerCampaigns.Saves[slot];
                var manager = Singleton<CampaignManager>.instance;
                if (meta == null || (manager && manager.campaign && Profile.activeCampaignMeta != null && Profile.activeCampaignMeta.saveSlot == meta.saveSlot))
                { ModLog.Warn("Not deleting multiplayer campaign " + (slot + 1) + ": it's empty or in use"); return; }
                ModLog.Info($"Deleting multiplayer campaign {slot + 1} at the host's request");
                MultiplayerCampaigns.Delete(meta, () =>
                {
                    // A fresh list goes to the host.
                    catalogSent = false;
                    nextCatalog = 0f;
                });
            });
            Router.On(MsgType.PartyClosed, (sender, r) =>
            {
                RefreshContext();
                if (!Active || (sender.m_SteamID != State.authority && !(sender == Session.Owner && State.phase == 1)) ||
                    r.ReadInt32() != State.token) return;
                Finish();
                MultiplayerScreen.RequestOpen();
            });
        }

        public static void Update()
        {
            if (!Session.InLobby)
            {
                if (lobby != CSteamID.Nil) Reset();
                return;
            }
            RefreshContext();
            if (!Updater.Compatible) return;
            if (Roles.IsDefender && !catalogSent && !headersLoading && Time.unscaledTime >= nextCatalog)
                MultiplayerGame.Instance.StartCoroutine(Catalog());
            if (Active && State.phase > 1 && !Session.Members().Contains(new CSteamID(State.authority)))
            {
                Session.Leave("The campaign leader left. Start a new lobby to continue safely.");
                return;
            }
            if (Session.IsHost && State.phase == 1)
            {
                if (!MembersMatch(State))
                {
                    var defenders = Defenders();
                    if (defenders.Count == 0) { Reset(); lobby = Session.Lobby; return; }
                    State.Draft(defenders);
                    Status = "The player count changed. Choose and confirm your squads again.";
                    Broadcast();
                }
                if (State.AllReady) Launch();
            }
            if (Session.IsHost && Loading && Time.unscaledTime >= nextBegin)
            { SendBegin(); nextBegin = Time.unscaledTime + 2f; }
            var manager = Singleton<CampaignManager>.instance;
            var stack = Singleton<Voxels.TowerDefense.Stack>.instance;
            if (Roles.IsDefender && Deployed && manager && !manager.campaign && stack && !stack.stateLoading.gameObject.activeInHierarchy)
            {
                Session.Broadcast(Packet.Build(MsgType.PartyClosed, w => w.Write(State.token)));
                Finish();
                return;
            }
            if (Active && Time.unscaledTime >= nextSync)
            {
                nextSync = Time.unscaledTime + 2f;
                if (Session.IsHost) Broadcast();
                if (Roles.IsDefender && Deployed) PublishOwnership();
            }
            if (Session.IsHost && State.phase >= 2 && StartedAt <= 0f) StartedAt = Time.unscaledTime;
            RecoverFromSilence();
        }

        // Guests: the host repeats the party state every 2 s while choosing and loading, so 8 s of silence means our
        // session with the host broke (it happens after a restart to update). Dropping it makes Steam negotiate a new one.
        private static void RecoverFromSilence()
        {
            if (Session.IsHost || !Active || (State.phase != 1 && State.phase != 2) || lastStateAt <= 0f) return;
            float silent = Time.unscaledTime - lastStateAt;
            if (silent < 8f || Time.unscaledTime < nextRecover) return;
            nextRecover = Time.unscaledTime + 8f;
            ModLog.Problem("party_silent", $"No party updates from the host for {silent:0} s; reconnecting");
            Session.ResetConnection(Session.Owner);
            Session.Send(Session.Owner, Packet.Build(MsgType.PartyStateRequest));
        }
        private static void Reset()
        {
            State = new PartyRoster(); pendingPick = null; lobby = CSteamID.Nil; beganToken = 0; nextSync = nextCatalog = 0;
            lastStateAt = nextRecover = StartedAt = 0f;
            catalogSent = false; SaveChoice = -1;
            NewMapPending = BeginFailed = false;
            Array.Clear(Saved, 0, Saved.Length);
            Array.Clear(Occupied, 0, Occupied.Length); Array.Clear(SaveDetails, 0, SaveDetails.Length); Array.Clear(SaveDifficulty, 0, SaveDifficulty.Length);
            CatalogReady = false; NewSaveSlot = -1;
            Status = IdleStatus;
        }
        private static void RefreshContext()
        {
            if (lobby == Session.Lobby) return;
            Reset(); lobby = Session.Lobby;
        }
        private static void Finish()
        {
            State = new PartyRoster { token = State.token + 1 }; pendingPick = null;
            catalogSent = false;
            Status = "The mastermind can start or continue a multiplayer campaign.";
        }
        public static void CancelDraft()
        {
            if (!Session.IsHost || State.phase != 1) return;
            Session.Broadcast(Packet.Build(MsgType.PartyClosed, w => w.Write(State.token)));
            Finish();
        }
        public static void Failed(Exception error)
        {
            BeginFailed = true;
            ModLog.Warn("Party campaign start failed: " + error);
            Session.Send(Session.Owner, Packet.Build(MsgType.PartyFailure, w => { w.Write(State.token); w.Write(error.Message); }));
        }
        /// <summary>Host: ask the campaign leader to delete one of its multiplayer campaigns (picker slot 0..4).</summary>
        public static void RequestDelete(int slot)
        {
            if (!Session.IsHost || Active || slot < 0 || slot >= MultiplayerCampaigns.SlotCount) return;
            Session.Send(Roles.DefenderId, Packet.Build(MsgType.PartyDeleteSave, w => w.Write(slot)));
            Saved[slot] = Occupied[slot] = false;
            SaveDetails[slot] = "";
            ModLog.Info("Asked the campaign leader to delete multiplayer campaign " + (slot + 1));
        }

        private static IEnumerator Catalog()
        {
            headersLoading = true;
            nextCatalog = Time.unscaledTime + 3f;
            try
            {
                var load = MultiplayerCampaigns.LoadHeaders();
                while (true)
                {
                    bool more;
                    try { more = load.MoveNext(); }
                    catch (Exception e) { ModLog.Warn("Party save catalogue failed: " + e); yield break; }
                    if (!more) break;
                    yield return load.Current;
                }
                if (!Roles.IsDefender || Profile.userSave == null) yield break;
                try
                {
                    Session.Send(Session.Owner, Packet.Build(MsgType.PartyCatalog, w =>
                    {
                        foreach (var meta in MultiplayerCampaigns.Saves) w.Write(meta != null && !meta.gameOver);
                        foreach (var meta in MultiplayerCampaigns.Saves)
                        {
                            w.Write(meta != null);
                            w.Write(meta == null ? "" : $"{meta.prefs.difficulty}{(meta.prefs.allowReplays ? "" : ", no restarts")}    {Mathf.RoundToInt(meta.campaignFraction * 100f)}% complete\nPlaytime {meta.playTime / 3600}:{meta.playTime / 60 % 60:00}    Saved {meta.savedTime:d MMM HH:mm}" +
                                (meta.gameOver ? "    Campaign ended" : ""));
                        }
                        PartyRoster.WriteNames(w, PartyLoadout.Catalog(true)); PartyRoster.WriteNames(w, PartyLoadout.Catalog(false));
                        foreach (var meta in MultiplayerCampaigns.Saves)
                        {
                            w.Write((byte)(meta == null ? 0 : (int)meta.prefs.difficulty));
                            w.Write(meta == null || meta.prefs.allowReplays);
                        }
                    }));
                    catalogSent = true;
                }
                catch (Exception e) { ModLog.Warn("Party save catalogue failed: " + e); }
            }
            finally { headersLoading = false; }
        }
        private static List<ulong> Defenders()
        {
            var players = new List<ulong>();
            foreach (var member in Session.Members()) if (member != Session.Owner) players.Add(member.m_SteamID);
            return players;
        }
        private static bool MembersMatch(PartyRoster roster)
        {
            var players = Defenders();
            if (players.Count != roster.seats.Count) return false;
            foreach (var seat in roster.seats) if (!players.Contains(seat.player)) return false;
            return true;
        }
        /// <summary>The leader's roster keeps every seat we know (players who joined mid-campaign get added).</summary>
        private static bool KeepsPlayers(PartyRoster roster)
        {
            if (roster.seats.Count < State.seats.Count || roster.authority != State.authority) return false;
            foreach (var seat in State.seats) if (roster.Find(seat.player) == null) return false;
            return true;
        }

        private static float nextSeatCheck;

        /// <summary>Leader, on the campaign map: seat lobby members who joined after the draft, to fight from the next island.</summary>
        public static void SeatLateJoiners()
        {
            if (!Roles.IsDefender || !Deployed || Profile.campaign == null || Time.unscaledTime < nextSeatCheck) return;
            nextSeatCheck = Time.unscaledTime + 1f;
            var heroes = Profile.campaign.heroes;
            bool seated = false;
            foreach (ulong player in Defenders())
            {
                if (State.Find(player) != null || !Updater.IsCompatible(new CSteamID(player))) continue;
                var seat = State.SeatLateJoiner(player, id => id >= 0 && id < heroes.Count && heroes[id].availableThisTurn,
                    out int hero, out DefenderSeat donor);
                if (seat == null) break; // all four seats are taken: they watch
                AnnounceSeat(player, hero, donor != null ? donor.player : 0);
                seated = true;
            }
            if (!seated) return;
            StoreOwnership();
            PublishOwnership();
        }

        /// <summary>Everyone: announce seats the leader added mid-campaign, and whose commander they got.</summary>
        private static void AnnounceNewSeats(PartyRoster before, PartyRoster after)
        {
            foreach (var seat in after.seats)
            {
                if (before.Find(seat.player) != null) continue;
                var owned = PartyRoster.Owned(seat);
                int hero = owned.Count > 0 ? owned[0] : -1;
                var donor = hero >= 0 ? before.OwnerOf(hero) : null;
                AnnounceSeat(seat.player, hero, donor != null ? donor.player : 0);
            }
        }

        private static void AnnounceSeat(ulong player, int hero, ulong donor)
        {
            ulong me = Session.Me.m_SteamID;
            string who = player == me ? "You join" : SteamSession.NameOf(new CSteamID(player)) + " joins";
            string name = Profile.campaign != null && hero >= 0 && hero < Profile.campaign.heroes.Count
                ? Profile.campaign.heroes[hero].dbgName : "a commander";
            string text = hero < 0 ? who + " the defenders and gets the next rescued commander."
                : donor == 0 ? $"{who} the defenders with {name}."
                : $"{who} the defenders, taking over {name} from {(donor == me ? "you" : SteamSession.NameOf(new CSteamID(donor)))}.";
            ModLog.Info(text);
            GameNotify.Post(text);
        }
        public static void StartDraft()
        {
            RefreshContext();
            if (!Session.IsHost || !Roles.HasMastermind || Active) return;
            State.seed = new System.Random().Next(1, int.MaxValue);
            // A continued save keeps its own difficulty and restarts: the draft shows those.
            bool saved = SaveChoice >= 0 && SaveChoice < SaveDifficulty.Length && SaveDifficulty[SaveChoice] >= 1 && SaveDifficulty[SaveChoice] <= 4;
            State.difficulty = saved ? SaveDifficulty[SaveChoice] : Difficulty;
            State.allowReplays = saved ? SaveAllowReplays[SaveChoice] : AllowReplays;
            State.Draft(Defenders());
            State.continuing = SaveChoice;
            Status = SaveChoice < 0 ? "Choose your commanders, then Ready. All new squads start as militia."
                : "Continuing a saved campaign: the commanders come from the save. Press Ready.";
            Broadcast();
        }
        public static void ChooseCommander(int slot, int choice)
        {
            var seat = Mine;
            if (State.phase != 1 || State.Continuing || seat == null || slot < 0 || slot >= seat.choices.Length || choice < 0 || choice >= PartyRoster.ChoiceCount) return;
            int[] choices = (int[])seat.choices.Clone(); choices[slot] = choice;
            Submit(choices, false);
        }
        public static HeroDefinition CreateStartingHero(int id, int choice, string trait = "", string item = "")
        {
            if (choice < 0 || choice >= PartyRoster.ChoiceCount || !HeroGeneratorUI.instance) throw new IOException("Commander preview is unavailable");
            var hero = new HeroDefinition(id);
            // Starting traits/items remain available; every squad starts without a class.
            PartyLoadout.PreparePreview(hero, trait, item);
            hero.monoHero = HeroGeneratorUI.instance.GetStartingMonoHero();
            try
            {
                PartyLoadout.IsolatePortraitAtlas(hero.monoHero);
                int seed = unchecked(State.seed + id * 43271 + choice * 7919);
                HeroGeneratorUI.instance.RandomizeStartingHero(hero, hero.upgrades, false, ref seed, 1,
                    HeroGeneratorUI.ChangeFlags.All, new List<HeroDefinition>());
                int total = State.seats.Count == 1 ? 2 : State.seats.Count;
                hero.hue = ((State.seed % 1000) / 1000f + id / (float)total) % 1f;
                hero.color = HeroGeneratorUI.instance.gradient.Evaluate(hero.hue);
                // Seeded too, so every player's game draws the same face for a pick whatever random numbers it drew before.
                UnityEngine.Random.State before = UnityEngine.Random.state;
                UnityEngine.Random.InitState(unchecked(id + State.seed));
                try { hero.monoHero.Setup(hero, hero.upgrades, HeroGeneratorUI.instance.swapSource, unchecked(id + State.seed), crop: false); }
                finally { UnityEngine.Random.state = before; }
                return hero;
            }
            catch
            {
                ReleaseStartingHero(hero);
                throw;
            }
        }
        private static void ReleaseStartingHero(HeroDefinition hero)
        {
            var mono = hero.monoHero;
            if (!mono) return;
            // Setup can fail before the native render texture exists.
            try { mono.Destroy(); }
            catch (Exception e) { ModLog.Warn("Commander preview texture cleanup: " + e.Message); }
            finally { UnityEngine.Object.Destroy(mono.gameObject); hero.monoHero = null; }
        }
        public static int StartingHeroId(ulong player, int slot)
        {
            int id = 0;
            foreach (var seat in State.seats)
            {
                if (seat.player == player) return id + slot;
                id += seat.heroes.Length;
            }
            return -1;
        }
        public static void CycleUpgrade(int slot, bool trait, int step)
        {
            var seat = Mine; if (State.phase != 1 || State.Continuing || seat == null || slot < 0 || slot >= seat.choices.Length) return;
            string[] catalog = trait ? State.allowedTraits : State.allowedItems;
            string[] selected = trait ? seat.traits : seat.items;
            int index = Array.IndexOf(catalog, selected[slot]) + 1;
            index = (index + step + catalog.Length + 1) % (catalog.Length + 1);
            selected[slot] = index == 0 ? "" : catalog[index - 1];
            Submit(seat.choices, false);
        }
        public static void Ready()
        {
            if (PendingPick) return;
            var seat = Mine;
            if (seat != null) Submit(seat.choices, !seat.ready);
        }
        private static void Submit(int[] choices, bool ready)
        {
            var seat = Mine;
            pendingToken = State.token;
            pendingPick = new DefenderSeat { player = seat.player, choices = (int[])choices.Clone(),
                traits = (string[])seat.traits.Clone(), items = (string[])seat.items.Clone(), ready = ready };
            seat.choices = (int[])choices.Clone(); seat.ready = ready;
            Session.Send(Session.Owner, Packet.Build(MsgType.PartyPick, w =>
            {
                w.Write(State.token); w.Write((byte)choices.Length);
                foreach (int type in choices) w.Write((byte)type);
                w.Write(ready);
                PartyRoster.WriteNames(w, seat.traits); PartyRoster.WriteNames(w, seat.items);
            }));
        }
        private static void Broadcast() => Session.Broadcast(Packet.Build(MsgType.PartyState, w =>
        {
            State.Write(w); w.Write(Status);
        }));
        private static void Launch()
        {
            State.AssignStartingHeroes(); State.phase = 2;
            Status = "All defenders are ready. Loading the multiplayer campaign...";
            Broadcast(); MultiplayerScreen.CloseForBattle();
            ModLog.Info("Party starting draft " + State.token + " with " + State.seats.Count + " defenders; leader " + State.authority);
            SendBegin(); nextBegin = Time.unscaledTime + 2f;
        }
        private static void SendBegin()
        {
            Session.Send(new CSteamID(State.authority), Packet.Build(MsgType.PartyBegin, w =>
            {
                State.Write(w); w.Write(SaveChoice < 0 ? -1 : MultiplayerCampaigns.FirstSlot + SaveChoice);
                w.Write(NewSaveSlot); w.Write(AllowReplays);
            }));
        }
        private static IEnumerator Begin(int slot)
        {
            BeginFailed = false;
            CSteamID expectedLobby = Session.Lobby;
            int expectedToken = State.token;
            MultiplayerScreen.CloseForBattle();
            var load = MultiplayerCampaigns.LoadHeaders();
            while (true)
            {
                bool more;
                try { more = load.MoveNext(); }
                catch (Exception e) { Failed(e); yield break; }
                if (!more) break;
                yield return load.Current;
            }
            if (Session.Lobby != expectedLobby || State.token != expectedToken) yield break;
            try
            {
                if (!Roles.IsDefender || State.phase != 2 || !MembersMatch(State)) throw new IOException("The lobby changed during loading");
                var campaigns = Singleton<CampaignManager>.instance;
                if (campaigns && campaigns.campaign) throw new IOException("The campaign leader must return to the main menu before starting another campaign.");
                if (slot >= MultiplayerCampaigns.FirstSlot && slot < MultiplayerCampaigns.FirstSlot + MultiplayerCampaigns.SlotCount)
                {
                    var meta = MultiplayerCampaigns.Saves[slot - MultiplayerCampaigns.FirstSlot];
                    if (meta == null || meta.gameOver) throw new IOException("That multiplayer save is unavailable");
                    MultiplayerCampaigns.Continue(meta);
                }
                else
                {
                    if (slot != -1) throw new IOException("Invalid save slot");
                    int free = NewSaveSlot >= 0 ? NewSaveSlot : MultiplayerCampaigns.FreeSlot;
                    if (free < 0) throw new IOException("All multiplayer save slots are full. Delete a multiplayer save first.");
                    if (free < MultiplayerCampaigns.FirstSlot || free >= MultiplayerCampaigns.FirstSlot + MultiplayerCampaigns.SlotCount ||
                        MultiplayerCampaigns.Saves[free - MultiplayerCampaigns.FirstSlot] != null)
                        throw new IOException("That multiplayer slot is occupied. Choose an empty slot.");
                    if (!HeroGeneratorUI.instance) throw new IOException("Hero generation is not ready");
                    // Set multiplayer slot before generation can write anything; never fall back to a solo slot.
                    MultiplayerCampaigns.PendingNew = false;
                    var save = Profile.CreateNewCampaign(State.seed);
                    // Unlike the normal New Game popup, CreateNewCampaign leaves this null.
                    save.heroes = new List<HeroDefinition>();
                    Profile.activeCampaignMeta.SetSaveSlot(free);
                    save.prefs = new CampaignPrefs((Difficulty)State.difficulty, true, AllowReplays);
                    Profile.activeCampaignMeta.prefs = save.prefs;
                    foreach (var seat in State.seats)
                        for (int i = 0; i < seat.heroes.Length; i++)
                        {
                            var hero = CreateStartingHero(seat.heroes[i], seat.choices[i], seat.traits[i], seat.items[i]);
                            try
                            {
                                PartyLoadout.Commit(hero);
                                save.heroes.Add(hero);
                            }
                            finally
                            {
                                // Generation creates the durable MonoHero; release this temporary one even on failure.
                                ReleaseStartingHero(hero);
                            }
                        }
                    MultiplayerCampaigns.Remember(Profile.activeCampaignMeta);
                    StoreOwnership();
                    NewMapPending = true;
                    MetaMenuHelpers.NewGame(save);
                }
            }
            catch (Exception e)
            {
                Failed(e);
            }
        }
        public static void CampaignReady()
        {
            if (!Loading || !Roles.IsDefender || SpectatorSession.Active || Profile.campaign == null) return;
            var prefs = Profile.campaign.prefs;
            prefs.skipTutorial = true;
            Profile.campaign.prefs = prefs;
            Profile.activeCampaignMeta.prefs = prefs;
            RestoreOwnership();
            State.phase = 3;
            PublishOwnership(); StoreOwnership();
            Profile.SaveCampaign();
            CampaignSync.MarkDirty();
            ModLog.Info("Party ready: " + State.seats.Count + " defenders, ownership assigned before island selection");
        }
        public static void PublishOwnership() => Session.Broadcast(Packet.Build(MsgType.PartyOwnership, State.Write));
        private static string OwnershipPath()
        {
            var meta = Profile.activeCampaignMeta;
            return Path.Combine(BepInEx.Paths.ConfigPath, "BadNorthMP-party-" + meta.saveSlot + "-" + Profile.campaign.seed + "-" + meta.campaignNumber + ".txt");
        }
        private static void StoreOwnership()
        {
            try
            {
                Directory.CreateDirectory(BepInEx.Paths.ConfigPath);
                // Starting slots first, then rescued commanders: restore fills them in that order.
                var lines = new List<string>();
                foreach (var seat in State.seats)
                    for (int i = 0; i < seat.heroes.Length; i++) lines.Add(seat.player + ":" + seat.heroes[i]);
                foreach (var seat in State.seats)
                    foreach (int hero in seat.recruits) lines.Add(seat.player + ":" + hero);
                lines.Add("next:" + State.nextRecruit);
                File.WriteAllLines(OwnershipPath(), lines.ToArray());
            }
            catch (Exception e) { ModLog.Warn("Unable to save squad ownership: " + e.Message); }
        }
        private static void RestoreOwnership()
        {
            // Keep returning players' commanders. Fill empty seats from existing recruited commanders.
            var assigned = new HashSet<int>();
            foreach (var seat in State.seats)
            {
                for (int i = 0; i < seat.heroes.Length; i++) seat.heroes[i] = -1;
                seat.recruits.Clear();
            }
            State.nextRecruit = 0;
            try
            {
                if (File.Exists(OwnershipPath()))
                    foreach (string line in File.ReadAllLines(OwnershipPath()))
                    {
                        string[] parts = line.Split(':'); ulong player; int hero;
                        if (parts.Length == 2 && parts[0] == "next" && int.TryParse(parts[1], out int next))
                        { State.nextRecruit = next >= 0 && next < State.seats.Count ? next : 0; continue; }
                        if (parts.Length != 2 || !ulong.TryParse(parts[0], out player) || !int.TryParse(parts[1], out hero) ||
                            hero < 0 || hero >= Profile.campaign.heroes.Count || assigned.Contains(hero)) continue;
                        var seat = State.Find(player);
                        if (seat == null) continue;
                        var restored = Profile.campaign.heroes[hero];
                        int slot = Array.IndexOf(seat.heroes, -1);
                        if (slot >= 0 && restored.recruited) { seat.heroes[slot] = hero; assigned.Add(hero); }
                        // A rescue that was lost or abandoned keeps its owner for the next attempt.
                        else if (slot < 0 && (restored.recruited || restored.recruitable) && seat.recruits.Count < PartyRoster.MaxHeroes)
                        { seat.recruits.Add(hero); assigned.Add(hero); }
                    }
            }
            catch (Exception e) { ModLog.Warn("Unable to restore squad ownership: " + e.Message); }
            foreach (var seat in State.seats)
                for (int i = 0; i < seat.heroes.Length; i++)
                    if (seat.heroes[i] == -1)
                    {
                        for (int n = 0; n < Profile.campaign.heroes.Count; n++)
                        {
                            var hero = Profile.campaign.heroes[n];
                            if (!assigned.Contains(n) && hero.available)
                            { seat.heroes[i] = n; assigned.Add(n); break; }
                        }
                        // A returning campaign can have fewer recruited commanders than the new party.
                        if (seat.heroes[i] == -1)
                            for (int n = 0; n < Profile.campaign.heroes.Count; n++)
                            {
                                var hero = Profile.campaign.heroes[n];
                                if (!assigned.Contains(n) && hero.recruitable)
                                {
                                    var trait = hero.traitUpgrade != null ? hero.traitUpgrade.definition : null;
                                    var item = hero.itemUpgrade != null ? hero.itemUpgrade.definition : null;
                                    hero.SetupHeroState(true, trait, (HeroUpgradeDefinition)null, item);
                                    seat.heroes[i] = n; assigned.Add(n); break;
                                }
                            }
                    }
            // Commanders recruited without an owner (older saves) join the party in turn.
            for (int n = 0; n < Profile.campaign.heroes.Count; n++)
                if (!assigned.Contains(n) && Profile.campaign.heroes[n].available && State.AssignRecruit(n, IsMember) != null)
                    assigned.Add(n);
        }

        // A seated player who's away, or back on another version, counts as absent: their commanders rest.
        internal static bool IsMember(ulong player) =>
            Session.Members().Contains(new CSteamID(player)) && Updater.IsCompatible(new CSteamID(player));

        /// <summary>The commander waiting to be rescued on this island, or -1.</summary>
        public static int RescueHero(Island island)
        {
            var node = island ? island.levelNode : null;
            var hero = node != null ? node.heroDefinition : null;
            return hero != null && hero.recruitable && Profile.campaign != null ? Profile.campaign.heroes.IndexOf(hero) : -1;
        }

        /// <summary>Leader: the island's rescued commander needs an owner before it spawns (the next player in turn, kept on retries).</summary>
        public static void AssignRescue(Island island)
        {
            int hero = RescueHero(island);
            if (hero < 0) return;
            var before = State.OwnerOf(hero);
            var seat = State.AssignRecruit(hero, IsMember);
            if (seat == null || seat == before) return;
            StoreOwnership();
            string text = SteamSession.NameOf(new CSteamID(seat.player)) + " commands the rescued " + Profile.campaign.heroes[hero].dbgName + ".";
            ModLog.Info("Party rescue: " + text + " Next rescue goes to seat " + State.nextRecruit + ".");
            GameNotify.Post(text);
        }
        /// <summary>
        /// Each connected seat's commanders able to fight this turn, longest-waiting first, the rescue apart; rescueSeat
        /// is -1 with no rescue or its owner away.
        /// </summary>
        private static List<int>[] Candidates(Island island, out int rescue, out int rescueSeat)
        {
            var heroes = Profile.campaign.heroes;
            var candidates = new List<int>[State.seats.Count];
            rescue = RescueHero(island);
            rescueSeat = -1;
            for (int s = 0; s < State.seats.Count; s++)
            {
                var seat = State.seats[s];
                candidates[s] = new List<int>();
                if (!IsMember(seat.player)) continue;
                if (rescue >= 0 && State.Owns(seat.player, rescue)) rescueSeat = s;
                foreach (int id in PartyRoster.Owned(seat))
                    if (id != rescue && id >= 0 && id < heroes.Count && heroes[id].availableThisTurn) candidates[s].Add(id);
                candidates[s].Sort((a, b) => { int c = WaitRank(b).CompareTo(WaitRank(a)); return c != 0 ? c : a.CompareTo(b); });
            }
            return candidates;
        }

        /// <summary>Seated defenders here and on this version (1 outside a party campaign).</summary>
        public static int ConnectedDefenders
        {
            get
            {
                if (!Deployed) return 1;
                int count = 0;
                foreach (var seat in State.seats) if (IsMember(seat.player)) count++;
                return Math.Max(1, count);
            }
        }

        /// <summary>Squads the party may bring to an island (the leader's config; Plugin wires it up).</summary>
        public static Func<int> MaxDeployed = () => PartyDeployment.DefaultSquads;

        /// <summary>Leader: share out an island's squads on everyone's most rested commanders, for DeployFlow to choose from.</summary>
        public static PartyDeployment PlanDeployment(Island island, int token)
        {
            var candidates = Candidates(island, out int rescue, out int rescueSeat);
            var players = State.seats.ConvertAll(seat => seat.player);
            int level = island && island.levelNode != null ? island.levelNode.index : -1;
            int max = Mathf.Clamp(MaxDeployed(), 1, PartyDeployment.SquadLimit);
            return PartyDeployment.Plan(token, level, players, candidates, rescueSeat, rescue, State.round, max);
        }

        /// <summary>The squads that go with everyone on their most rested commanders (none: nobody can fight).</summary>
        public static List<HeroDefinition> Deployment(Island island)
        {
            var heroes = Profile.campaign.heroes;
            return PlanDeployment(island, 0).Squads().ConvertAll(id => heroes[id]);
        }

        /// <summary>Leader: the chosen squads land (PartyDeployPatch held the island while everyone picked).</summary>
        public static void DeploySquads(IslandGameplayManager manager, List<int> squads)
        {
            var heroes = Profile.campaign.heroes;
            ++State.round; PublishOwnership();
            manager.states.root.SetActive(true);
            PreparingDeployment = true;
            try { manager.squadSpawner.SpawnEnglishSquads(squads.ConvertAll(id => heroes[id])); }
            finally { PreparingDeployment = false; }
        }

        // Never deployed ranks highest, then the longest ago (CampaignSave.oldDeployOrder lists the most recent first).
        private static int WaitRank(int hero)
        {
            var order = Profile.campaign.oldDeployOrder;
            int index = order != null ? order.IndexOf(hero) : -1;
            return index < 0 ? int.MaxValue : index;
        }
        public static bool PreparingDeployment;
    }

    [HarmonyPatch(typeof(LevelNode), nameof(LevelNode.Play))]
    internal static class PartyIslandAvailablePatch
    {
        private static bool Prefix()
        {
            if (!PartyFlow.Deployed || !Roles.IsDefender || SpectatorSession.Active) return true;
            if (PartyFlow.Deployment(null).Count > 0) return true;
            GameNotify.Post("Your commanders are resting or fallen. Advance the turn before choosing an island.");
            return false;
        }
    }

    [HarmonyPatch(typeof(MetaMenuHelpers), "EnterNewCampaign")]
    internal static class PartyFirstMapPatch
    {
        private static bool Prefix()
        {
            if (!PartyFlow.NewMapPending || !Roles.IsDefender) return true;
            PartyFlow.NewMapPending = false;
            if (PartyFlow.BeginFailed)
            {
                Singleton<Voxels.TowerDefense.Stack>.instance.stateMeta.SetActive(true);
                return false;
            }
            PartyFlow.CampaignReady();
            Singleton<Voxels.TowerDefense.Stack>.instance.stateCampaign.SetActive(true);
            return false;
        }
    }
    [HarmonyPatch(typeof(CampaignManager), "GenerateCampaign", typeof(Voxels.TowerDefense.ProfileInternals.CampaignSave), typeof(bool))]
    internal static class PartyLoadedMapPatch
    {
        private static void Postfix(bool campaignIsNew, ref IEnumerator __result)
        {
            if (PartyFlow.Loading && Roles.IsDefender && !SpectatorSession.Active) __result = Ready(__result, campaignIsNew);
        }
        private static IEnumerator Ready(IEnumerator routine, bool isNew)
        {
            while (true)
            {
                bool moved;
                try { moved = routine.MoveNext(); }
                catch (Exception e) { PartyFlow.Failed(e); yield break; }
                if (!moved) break;
                yield return routine.Current;
            }
            if (!isNew) PartyFlow.CampaignReady();
        }
    }
    [HarmonyPatch(typeof(IslandGameplayManager), "EnterLevel")]
    internal static class PartyDeployPatch
    {
        private static bool Prefix(IslandGameplayManager __instance)
        {
            if (!PartyFlow.Deployed || !Roles.IsDefender || SpectatorSession.Active) return true;
            if (__instance.states.Spawning.active || __instance.states.running.active || DeployFlow.ChoosingFor(__instance)) return false;
            PartyFlow.AssignRescue(__instance.island);
            // Everyone has the island loaded: each player picks which of their commanders go, then they land.
            if (!DeployFlow.Begin(__instance))
            {
                ModLog.Warn("Party deployment refused: no available owned commanders. Advance the campaign turn.");
                __instance.states.loadout.SetActive(true);
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(SquadSpawner), nameof(SquadSpawner.SpawnEnglishSquads))]
    internal static class PartySpawnOwnershipPatch
    {
        private static bool Prefix()
        {
            // The native loadout cannot add or take over another player's commanders.
            if (!PartyFlow.Deployed || !Roles.IsDefender || SpectatorSession.Active) return true;
            if (PartyFlow.PreparingDeployment) return true;
            GameNotify.Post("The party's chosen commanders deploy automatically. Advance the turn if they need rest.");
            return false;
        }
    }
}
