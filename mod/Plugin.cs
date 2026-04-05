using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace IGTAPMod
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.igtapmod.plugin";
        public const string PluginName = "IGTAP Core";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<KeyboardShortcut> UIToggleKey;

        private void Awake()
        {
            Log = Logger;

            UIToggleKey = Config.Bind("UI", "ToggleKey",
                new KeyboardShortcut(KeyCode.F8),
                "Press to open/close the mod menu (HUD is always visible)");

            gameObject.AddComponent<DebugUI>();

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded!");
        }
    }
}
