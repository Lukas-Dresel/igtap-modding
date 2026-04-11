using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
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
        internal static Harmony HarmonyInstance;

        internal static ConfigEntry<KeyboardShortcut> UIToggleKey;

        private void Awake()
        {
            Log = Logger;

            UIToggleKey = Config.Bind("UI", "ToggleKey",
                new KeyboardShortcut(KeyCode.F8),
                "Press to open/close the mod menu (HUD is always visible)");

            ModManagerUI.ToggleKey = Config.Bind("UI", "ModManagerKey",
                new KeyboardShortcut(KeyCode.F9),
                "Press to open/close the mod manager");

            HarmonyInstance = new Harmony(PluginGUID);
            HarmonyInstance.PatchAll();

            gameObject.AddComponent<GameUITheme>();
            gameObject.AddComponent<DebugMenuUI>();
            gameObject.AddComponent<HudOverlayUI>();
            gameObject.AddComponent<ModManagerUI>();
            gameObject.AddComponent<ModKeybindInjector>();

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded!");
        }

        private void OnDestroy()
        {
            HarmonyInstance?.UnpatchSelf();
        }
    }
}
