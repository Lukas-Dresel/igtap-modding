using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
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

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("General", "Enabled", true,
                "Lock deltaTime to a fixed value (1/TargetFramerate)");
            TargetFramerate = Config.Bind("General", "TargetFramerate", 50,
                "The fixed framerate to lock to. deltaTime will always be 1/this value.");

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded!");
        }

        private void Update()
        {
            int target = Enabled.Value ? TargetFramerate.Value : 0;
            if (Time.captureFramerate != target)
            {
                Time.captureFramerate = target;
                Log.LogInfo($"captureFramerate set to {target}");
            }
        }

        private void OnDestroy()
        {
            Time.captureFramerate = 0;
        }
    }
}
