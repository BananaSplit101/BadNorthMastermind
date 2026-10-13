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
        public const string Version = "1.0.21";

        private void Awake()
        {
            ModLog.Init(Logger);
            GameErrors.Capture();
            ModConfig.Bind(Config);
            Telemetry.Reports.Begin();
            gameObject.AddComponent<Telemetry.Reports>();

            // GMW's traits first: saves and shared campaigns name their traits, so the game must know them before loading any.
            try
            {
                Traits.VanillaTraitsOverhaul.Register();
                Traits.TraitsGalore.Register();
                PartyLoadout.ExtraTraits = () => Traits.CustomTraits.All;
            }
            catch (System.Exception e) { ModLog.Error("Commander traits failed to load: " + e); }
            new Harmony(Id).PatchAll(typeof(Plugin).Assembly);
            PartyFlow.MaxDeployed = () => ModConfig.MaxDeployed.Value;
            Game.Economy.PartyScale = () => Game.EconomyConfig.PartyScale(PartyFlow.ConnectedDefenders, ModConfig.PerExtraDefender.Value);

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
            gameObject.AddComponent<InvitePopup>();
            gameObject.AddComponent<DeployPanel>();
            gameObject.AddComponent<NativeLoadout>();
            DeployFlow.Landing = NativeLoadout.Close;
            gameObject.AddComponent<RelicTooltip>();
            gameObject.AddComponent<PingWheel>();
            gameObject.AddComponent<HouseLabels>();

            ModLog.Info($"{Name} {Version} loaded");
        }
    }
}
