using BadNorthMP.Spectator;
using BadNorthMP.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using Voxels.TowerDefense;
namespace BadNorthMP.Net
{
    // Shared squad control for extra defenders. Orders are validated and executed by the campaign leader.
    internal class RemoteDefender : MonoBehaviour
    {
        private EnglishSquad selected;
        public static void Init()
        {
            Router.On(MsgType.DefenderMove, (sender, reader) =>
            {
                int hero = reader.ReadInt32(), spot = reader.ReadInt32();
                var session = SteamSession.Instance;
                if (!Roles.IsDefender || sender == Roles.MastermindId || sender == session.Me ||
                    !session.Members().Contains(sender) || !GameAccess.IsPlaying) return;
                var island = GameAccess.CurrentIsland;
                if (hero < 0 || hero >= Profile.campaign.heroes.Count || spot < 0 || spot >= island.navSpotter.navSpots.Count) return;
                foreach (Squad candidate in island.english.livingSquads)
                {
                    var squad = candidate as EnglishSquad;
                    if (squad && squad.hero == Profile.campaign.heroes[hero] && squad.selectable && squad.navSpotOccupant.canMove)
                    {
                        SquadMover.MoveToDirect(squad, island.navSpotter.navSpots[spot]);
                        break;
                    }
                }
            });
        }
        private void Update()
        {
            if (!Roles.IsRemoteDefender || !SpectatorSession.Active || !SpectatorSession.IslandReady()) { selected = null; return; }
            if (EventSystem.current && EventSystem.current.IsPointerOverGameObject()) return;
            var squads = GameAccess.PlayerSquads;
            for (int i = 0; i < 4 && i < squads.Count; i++)
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i)))
                {
                    if (selected) selected.ShowSelection(false);
                    selected = squads[i] as EnglishSquad;
                    if (selected) { selected.ShowSelection(true); GameNotify.Post("Selected " + selected.hero.dbgName + ": right-click a tile to move"); }
                }
            if (!selected || !Input.GetMouseButtonDown(1)) return;
            var island = GameAccess.CurrentIsland;
            var target = island.navSpotter.NavSpotCast(Input.mousePosition);
            if (!target) return;
            int hero = Profile.campaign.heroes.IndexOf(selected.hero);
            int spot = island.navSpotter.navSpots.IndexOf(target);
            SteamSession.Instance.Send(Roles.DefenderId, Packet.Build(MsgType.DefenderMove, writer => { writer.Write(hero); writer.Write(spot); }));
        }
    }
    // Prevent each participant's normal Play button from opening a separate local campaign.
    [HarmonyPatch(typeof(Voxels.TowerDefense.UI.MainMenu), "Play")]
    internal static class SharedCampaignPlayPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!SteamSession.Instance || !SteamSession.Instance.InLobby) return true;
            MultiplayerScreen.Open();
            __result = true;
            return false;
        }
    }
}
