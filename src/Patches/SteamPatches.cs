using CS.Platform.Steam.Client.Part;
using HarmonyLib;

namespace BadNorthMP.Patches
{
    // Bad North ships a shared platform library (from another game) whose SteamJoining component reacts to
    // Steam invites on its own: it joins the lobby a second time, and its rich-presence handler expects
    // "ip:port" and throws on anything else. SteamSession handles both, so switch these off.

    [HarmonyPatch(typeof(SteamJoining), "OnGameLobbyJoinRequested")]
    internal static class SteamJoiningLobbyRequestPatch
    {
        private static bool Prefix() => false;
    }

    [HarmonyPatch(typeof(SteamJoining), "OnJoinUser")]
    internal static class SteamJoiningRichPresencePatch
    {
        private static bool Prefix() => false;
    }

    // The stock platform lobby also subscribes to every Steam lobby-enter callback, including ours.
    // It must not adopt mod lobbies and run a second lobby lifecycle.
    [HarmonyPatch(typeof(SteamLobby), "OnLobbyEntered")]
    internal static class StockLobbyEnterPatch
    {
        private static bool Prefix(Steamworks.LobbyEnter_t message) =>
            string.IsNullOrEmpty(Steamworks.SteamMatchmaking.GetLobbyData(
                new Steamworks.CSteamID(message.m_ulSteamIDLobby), "bnmp_version"));
    }
}
