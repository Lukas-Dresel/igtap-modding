using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace IGTAPFreeplay
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.igtapmod.plugin")]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.igtapmod.freeplay";
        public const string PluginName = "IGTAP Freeplay";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static Harmony HarmonyInstance;

        // Movement
        internal static ConfigEntry<float> SpeedMultiplier;
        internal static ConfigEntry<float> JumpMultiplier;
        internal static ConfigEntry<int> ExtraAirDashes;
        internal static ConfigEntry<int> ExtraAirJumps;
        internal static ConfigEntry<bool> InfiniteWallJumps;
        internal static ConfigEntry<bool> InfiniteDashes;
        internal static ConfigEntry<bool> InfiniteJumps;
        internal static ConfigEntry<bool> GodMode;

        // Noclip
        internal static ConfigEntry<KeyboardShortcut> NoclipToggleKey;
        internal static ConfigEntry<float> NoclipSpeed;
        internal static ConfigEntry<float> NoclipFastMultiplier;
        internal static bool NoclipActive;

        // Visuals
        internal static ConfigEntry<bool> RevealHiddenSpikes;
        internal static ConfigEntry<float> HiddenSpikeAlpha;
        internal static ConfigEntry<bool> AlwaysGlow;
        internal static ConfigEntry<float> GlowRadius;
        internal static ConfigEntry<float> GlowIntensity;
        internal static ConfigEntry<string> GlowColorHex;

        internal static Color GlowColor
        {
            get
            {
                if (ColorUtility.TryParseHtmlString(GlowColorHex.Value, out Color c))
                    return c;
                return Color.green;
            }
        }

        // Economy
        internal static ConfigEntry<double> CurrencyMultiplier;
        internal static ConfigEntry<KeyboardShortcut> GiveCashKey;
        internal static ConfigEntry<double> GiveCashAmount;

        private static Movement cachedPlayer;
        internal static Movement Player
        {
            get
            {
                if (cachedPlayer == null)
                    cachedPlayer = Object.FindAnyObjectByType<Movement>();
                return cachedPlayer;
            }
        }

        private void Awake()
        {
            Log = Logger;

            SpeedMultiplier = Config.Bind("Movement", "SpeedMultiplier", 1.0f,
                "Multiplier for run speed");
            JumpMultiplier = Config.Bind("Movement", "JumpMultiplier", 1.0f,
                "Multiplier for jump force");
            ExtraAirDashes = Config.Bind("Movement", "ExtraAirDashes", 0,
                "Extra air dashes to add on top of the game's default");
            ExtraAirJumps = Config.Bind("Movement", "ExtraAirJumps", 0,
                "Extra air jumps to add on top of the game's default");
            InfiniteWallJumps = Config.Bind("Movement", "InfiniteWallJumps", false,
                "Never run out of wall jumps");
            InfiniteDashes = Config.Bind("Movement", "InfiniteDashes", false,
                "Never run out of air dashes");
            InfiniteJumps = Config.Bind("Movement", "InfiniteJumps", false,
                "Never run out of air jumps");
            GodMode = Config.Bind("Movement", "GodMode", false,
                "Immune to spikes and other death triggers");

            NoclipToggleKey = Config.Bind("Noclip", "ToggleKey",
                new KeyboardShortcut(KeyCode.F10),
                "Press to toggle noclip fly mode");
            NoclipSpeed = Config.Bind("Noclip", "FlySpeed", 1500f,
                "Fly speed in noclip mode");
            NoclipFastMultiplier = Config.Bind("Noclip", "FastMultiplier", 3.0f,
                "Speed multiplier when holding Shift in noclip mode");

            RevealHiddenSpikes = Config.Bind("Visuals", "RevealHiddenSpikes", true,
                "Render hidden spikes as translucent so you can see them");
            HiddenSpikeAlpha = Config.Bind("Visuals", "HiddenSpikeAlpha", 0.35f,
                "Alpha (0-1) for revealed hidden spikes");

            AlwaysGlow = Config.Bind("Light", "AlwaysGlow", true,
                "Always enable the player's Light2D, even before Area 2");
            GlowRadius = Config.Bind("Light", "GlowRadius", 800f,
                "Outer radius of the player glow light");
            GlowIntensity = Config.Bind("Light", "GlowIntensity", 1.0f,
                "Intensity of the player glow light");
            GlowColorHex = Config.Bind("Light", "GlowColor", "#FFFFFF",
                "Hex color of the glow (e.g. #00FF66 for green, #FFFFFF for white)");

            CurrencyMultiplier = Config.Bind("Economy", "CurrencyMultiplier", 1.0,
                "Multiplier for all currency gains from course completions");
            GiveCashKey = Config.Bind("Economy", "GiveCashKey",
                new KeyboardShortcut(KeyCode.F9),
                "Press this key to give yourself cash");
            GiveCashAmount = Config.Bind("Economy", "GiveCashAmount", 1000000.0,
                "Amount of cash given when pressing the give-cash key");

            HarmonyInstance = new Harmony(PluginGUID);
            HarmonyInstance.PatchAll();

            SceneManager.sceneLoaded += OnSceneLoaded;

            FreeplayMenu.Register();

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded!");
        }

        private void Update()
        {
            if (GiveCashKey.Value.IsDown())
            {
                globalStats.currencyLookup[globalStats.Currencies.Cash] += GiveCashAmount.Value;
                Log.LogInfo($"Gave {GiveCashAmount.Value} cash. Total: {globalStats.currencyLookup[globalStats.Currencies.Cash]}");
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (RevealHiddenSpikes.Value)
                RevealHiddenSpikeTilemaps();
        }

        private static void RevealHiddenSpikeTilemaps()
        {
            float alpha = HiddenSpikeAlpha.Value;
            int count = 0;

            foreach (var tr in Object.FindObjectsByType<TilemapRenderer>(FindObjectsSortMode.None))
            {
                if (!tr.gameObject.name.ToLower().Contains("hiddenspike"))
                    continue;
                var tilemap = tr.GetComponent<Tilemap>();
                if (tilemap != null)
                {
                    Color c = tilemap.color;
                    tilemap.color = new Color(c.r, c.g, c.b, alpha);
                    count++;
                }
            }

            foreach (var spike in Object.FindObjectsByType<spikeScript>(FindObjectsSortMode.None))
            {
                var sr = spike.GetComponent<SpriteRenderer>();
                if (sr != null && sr.color.a < 0.01f)
                {
                    Color c = sr.color;
                    sr.color = new Color(c.r, c.g, c.b, alpha);
                    count++;
                }
            }

            if (count > 0)
                Log.LogInfo($"Revealed {count} hidden spike layer(s) at alpha={alpha}");
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            HarmonyInstance?.UnpatchSelf();
        }
    }
}
