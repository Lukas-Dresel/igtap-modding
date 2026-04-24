using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using IGTAPMod;
using UnityEngine;

namespace IGTAPFixedTimestep
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.igtapmod.fixedtimestep";
        public const string PluginName = "IGTAP Fixed Timestep";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> TargetFramerate;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("General", "Enabled", true,
                "Lock deltaTime to a fixed value (1/TargetFramerate)");
            TargetFramerate = Config.Bind("General", "TargetFramerate", 50,
                "The fixed framerate to lock to. deltaTime will always be 1/this value.");
            ToggleKey = Config.Bind("Keybinds", "Toggle",
                new KeyboardShortcut(KeyCode.V),
                "Toggle fixed timestep on/off");

            DebugMenuAPI.RegisterHudItem("FixedTimestep", 10, GetHudText);

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded!");
        }

        private void Update()
        {
            if (Input.GetKeyDown(ToggleKey.Value.MainKey))
            {
                Enabled.Value = !Enabled.Value;
                Log.LogInfo($"Fixed timestep {(Enabled.Value ? "enabled" : "disabled")}");
            }

            int target = Enabled.Value ? TargetFramerate.Value : 0;
            if (Time.captureFramerate != target)
            {
                Time.captureFramerate = target;
                Log.LogInfo($"captureFramerate set to {target}");
            }
        }

        private void OnDestroy()
        {
            DebugMenuAPI.UnregisterHudItem("FixedTimestep");
            Time.captureFramerate = 0;
        }

        private string GetHudText()
        {
            if (!Enabled.Value)
                return "[UNLOCKED  V=lock]";
            return null;
        }
    }
}
