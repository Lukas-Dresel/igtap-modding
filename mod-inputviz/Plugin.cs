using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using IGTAPMod;
using UnityEngine;

namespace IGTAPInputViz
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.igtapmod.plugin")]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.igtapmod.inputviz";
        public const string PluginName = "IGTAP Input Viz";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<bool> ShowDirectional;
        internal static ConfigEntry<string> DirectionalAction;
        internal static ConfigEntry<bool> ShowJump;
        internal static ConfigEntry<bool> ShowDash;
        internal static ConfigEntry<bool> ShowReset;
        internal static ConfigEntry<bool> ShowStatusGrounded;
        internal static ConfigEntry<bool> ShowStatusOnWall;
        internal static ConfigEntry<bool> ShowStatusDashReady;
        internal static ConfigEntry<bool> ShowStatusJumpReady;
        internal static ConfigEntry<bool> ShowStatusWallJumpReady;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("General", "Enabled", true,
                "Show the input visualization overlay");
            ToggleKey = Config.Bind("General", "ToggleKey",
                new KeyboardShortcut(KeyCode.F7),
                "Press to toggle input visualization on/off");

            ShowDirectional = Config.Bind("Actions", "ShowDirectional", true,
                "Show the directional pad (Move action)");
            DirectionalAction = Config.Bind("Actions", "DirectionalAction", "Move",
                "InputSystem action name for the directional pad");
            ShowJump = Config.Bind("Actions", "ShowJump", true,
                "Show Jump in the action buttons");
            ShowDash = Config.Bind("Actions", "ShowDash", true,
                "Show Dash in the action buttons");
            ShowReset = Config.Bind("Actions", "ShowReset", true,
                "Show Reset Course in the action buttons");

            ShowStatusGrounded = Config.Bind("Status", "ShowGrounded", true,
                "Show grounded/airborne indicator");
            ShowStatusOnWall = Config.Bind("Status", "ShowOnWall", true,
                "Show wall-clinging indicator");
            ShowStatusDashReady = Config.Bind("Status", "ShowDashReady", true,
                "Show dash availability with count");
            ShowStatusJumpReady = Config.Bind("Status", "ShowJumpReady", true,
                "Show air jump availability with count");
            ShowStatusWallJumpReady = Config.Bind("Status", "ShowWallJumpReady", true,
                "Show wall jump availability with count");

            // Actions
            if (ShowJump.Value) InputVizAPI.RegisterInputAction("Jump", "Jump");
            if (ShowDash.Value) InputVizAPI.RegisterInputAction("Dash", "Dash");
            if (ShowReset.Value) InputVizAPI.RegisterInputAction("Reset", "ResetCourse");

            ShowJump.SettingChanged += (_, __) => Toggle("Jump", "Jump", ShowJump.Value);
            ShowDash.SettingChanged += (_, __) => Toggle("Dash", "Dash", ShowDash.Value);
            ShowReset.SettingChanged += (_, __) => Toggle("Reset", "ResetCourse", ShowReset.Value);

            // Statuses (using GameState from core)
            RegisterStatuses();

            gameObject.AddComponent<InputVizOverlay>();

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded!");
        }

        private void RegisterStatuses()
        {
            if (ShowStatusGrounded.Value)
                InputVizAPI.RegisterStatus("Ground", () => GameState.IsGrounded);
            if (ShowStatusOnWall.Value)
                InputVizAPI.RegisterStatus("Wall", () => GameState.IsOnWall);
            if (ShowStatusDashReady.Value)
                InputVizAPI.RegisterStatus("Dash Rdy",
                    () => GameState.Player != null && GameState.Player.dashUnlocked && GameState.AirDashesLeft > 0,
                    () => $"{GameState.AirDashesLeft}/{GameState.Player?.maxAirDashes}");
            if (ShowStatusJumpReady.Value)
                InputVizAPI.RegisterStatus("Jump Rdy",
                    () => GameState.Player != null && GameState.Player.doubleJumpUnlocked && GameState.AirJumpsLeft > 0,
                    () => $"{GameState.AirJumpsLeft}/{GameState.Player?.maxAirJumps}");
            if (ShowStatusWallJumpReady.Value)
                InputVizAPI.RegisterStatus("WJump Rdy",
                    () => GameState.Player != null && GameState.Player.wallJumpUnlocked && GameState.WallJumpsLeft > 0,
                    () => $"{GameState.WallJumpsLeft}/{GameState.Player?.maxWallJumps}");

            ShowStatusGrounded.SettingChanged += (_, __) => ToggleSt("Ground", ShowStatusGrounded.Value,
                () => GameState.IsGrounded, null);
            ShowStatusOnWall.SettingChanged += (_, __) => ToggleSt("Wall", ShowStatusOnWall.Value,
                () => GameState.IsOnWall, null);
            ShowStatusDashReady.SettingChanged += (_, __) => ToggleSt("Dash Rdy", ShowStatusDashReady.Value,
                () => GameState.Player != null && GameState.Player.dashUnlocked && GameState.AirDashesLeft > 0,
                () => $"{GameState.AirDashesLeft}/{GameState.Player?.maxAirDashes}");
            ShowStatusJumpReady.SettingChanged += (_, __) => ToggleSt("Jump Rdy", ShowStatusJumpReady.Value,
                () => GameState.Player != null && GameState.Player.doubleJumpUnlocked && GameState.AirJumpsLeft > 0,
                () => $"{GameState.AirJumpsLeft}/{GameState.Player?.maxAirJumps}");
            ShowStatusWallJumpReady.SettingChanged += (_, __) => ToggleSt("WJump Rdy", ShowStatusWallJumpReady.Value,
                () => GameState.Player != null && GameState.Player.wallJumpUnlocked && GameState.WallJumpsLeft > 0,
                () => $"{GameState.WallJumpsLeft}/{GameState.Player?.maxWallJumps}");
        }

        private static void Toggle(string label, string action, bool on)
        {
            if (on) InputVizAPI.RegisterInputAction(label, action);
            else InputVizAPI.UnregisterAction(label);
        }

        private static void ToggleSt(string label, bool on, System.Func<bool> isActive, System.Func<string> detail)
        {
            if (on) InputVizAPI.RegisterStatus(label, isActive, detail);
            else InputVizAPI.UnregisterStatus(label);
        }
    }
}
