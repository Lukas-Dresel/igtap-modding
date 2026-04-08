using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace IGTAPDashPlus
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.igtapmod.dashplus";
        public const string PluginName = "IGTAP Dash+";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static Harmony HarmonyInstance;

        internal static ConfigEntry<bool> DiagonalDash;
        internal static ConfigEntry<bool> VerticalDash;
        internal static ConfigEntry<bool> HyperDash;
        internal static ConfigEntry<bool> UltraDash;

        private void Awake()
        {
            Log = Logger;

            DiagonalDash = Config.Bind("General", "DiagonalDash", true,
                "Allow dashing diagonally by holding up/down + left/right when dashing");
            VerticalDash = Config.Bind("General", "VerticalDash", true,
                "Allow dashing straight up/down by holding up/down with no horizontal input");
            HyperDash = Config.Bind("General", "HyperDash", true,
                "Jumping during a diagonal-downward ground dash gives a massive horizontal speed boost with a small hop (Celeste-style hyper-dash)");
            UltraDash = Config.Bind("General", "UltraDash", true,
                "Landing during a diagonal-downward air dash converts vertical momentum into horizontal speed (Celeste-style ultra-dash)");

            HarmonyInstance = new Harmony(PluginGUID);
            HarmonyInstance.PatchAll();

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded!");
        }

        private void OnDestroy()
        {
            HarmonyInstance?.UnpatchSelf();
        }
    }
}
