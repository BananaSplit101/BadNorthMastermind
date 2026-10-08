using BepInEx;
using HarmonyLib;
using BadNorthMP.DevTools;
using BadNorthMP.Net;
using BadNorthMP.UI;

namespace BadNorthMP
{
    [BepInPlugin(Id, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Id = "com.bananasplit.badnorthmp";
        public const string Name = "Bad North Multiplayer";
        public const string Version = "0.2.0";

        private void Awake()
        {
            ModLog.Init(Logger);

            var harmony = new Harmony(Id);
            harmony.PatchAll(typeof(Plugin).Assembly);
            foreach (var method in harmony.GetPatchedMethods())
                ModLog.Info($"Patched {method.DeclaringType?.Name}.{method.Name}");

            gameObject.AddComponent<SteamSession>();
            gameObject.AddComponent<MultiplayerGame>();
            gameObject.AddComponent<MultiplayerMenu>();
            gameObject.AddComponent<DebugKeys>();
            gameObject.AddComponent<DebugOverlay>();

            ModLog.Info($"{Name} {Version} loaded. F7 overlay, F8 move squad, F9 next wave, F10 dump island.");
        }
    }
}
