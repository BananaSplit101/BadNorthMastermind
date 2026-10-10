using BepInEx;
using HarmonyLib;
using BadNorthMP.Net;
using BadNorthMP.Spectator;
using BadNorthMP.UI;

namespace BadNorthMP
{
    [BepInPlugin(Id, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Id = "com.bananasplit.badnorthmp";
        public const string Name = "Bad North Multiplayer";
        public const string Version = "1.0.2";

        private void Awake()
        {
            ModLog.Init(Logger);
            ModConfig.Bind(Config);

            new Harmony(Id).PatchAll(typeof(Plugin).Assembly);

            gameObject.AddComponent<SteamSession>();
            gameObject.AddComponent<Updater>();
            gameObject.AddComponent<MultiplayerGame>();
            gameObject.AddComponent<MultiplayerScreen>();
            gameObject.AddComponent<SpectatorSession>();
            gameObject.AddComponent<PuppetWorld>();
            gameObject.AddComponent<LoadGate>();
            gameObject.AddComponent<CoinVisuals>();
            gameObject.AddComponent<MastermindPanel>();
            gameObject.AddComponent<VotePanel>();
            gameObject.AddComponent<CampaignScreen>();
            gameObject.AddComponent<RemoteDefender>();
            gameObject.AddComponent<CommanderDraftScreen>();

            ModLog.Info($"{Name} {Version} loaded");
        }
    }
}
