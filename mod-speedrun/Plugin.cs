using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using IGTAPMod;
using UnityEngine;

namespace IGTAPSpeedrun
{
    public enum ScreenCorner
    {
        TopLeft, TopRight, BottomLeft, BottomRight
    }

    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.igtapmod.plugin")]
    [BepInDependency("com.igtapmod.fixedtimestep", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.igtapmod.speedrun";
        public const string PluginName = "IGTAP Speedrun Timer";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        public static Plugin Instance;

        public SpeedrunTimer Timer;

        // Keybinds
        internal static ConfigEntry<KeyboardShortcut> StartKey;
        internal static ConfigEntry<KeyboardShortcut> StopKey;
        internal static ConfigEntry<KeyboardShortcut> ResetKey;

        // General
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> ShowHUD;
        internal static ConfigEntry<ScreenCorner> HUDPosition;
        internal static ConfigEntry<bool> ShowDeathCount;
        internal static ConfigEntry<string> LastProfileName;

        // Split toggles (auto-detect mode)
        internal static ConfigEntry<bool> TrackCourse1;
        internal static ConfigEntry<bool> TrackCourse2;
        internal static ConfigEntry<bool> TrackCourse3;
        internal static ConfigEntry<bool> TrackCourse4;
        internal static ConfigEntry<bool> TrackCourse5;
        internal static ConfigEntry<bool> TrackDash;
        internal static ConfigEntry<bool> TrackWallJump;
        internal static ConfigEntry<bool> TrackDoubleJump;
        internal static ConfigEntry<bool> TrackBlockSwap;
        internal static ConfigEntry<bool> TrackSwapBlocksOnce;
        internal static ConfigEntry<bool> TrackOpenGate;
        internal static ConfigEntry<bool> TrackUnlockPrestige;
        internal static ConfigEntry<string> AutoDetectStartSplits;
        internal static ConfigEntry<string> AutoDetectEndSplit;

        private ProfileSelectorUI activeSelectorUI;

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            // Keybinds
            StartKey = Config.Bind("Keybinds", "StartKey",
                new KeyboardShortcut(KeyCode.Backspace),
                "Open profile selector and start the speedrun timer");
            StopKey = Config.Bind("Keybinds", "StopKey",
                new KeyboardShortcut(KeyCode.Backspace, KeyCode.LeftControl),
                "Stop the speedrun timer (Ctrl+Backspace)");
            ResetKey = Config.Bind("Keybinds", "ResetKey",
                new KeyboardShortcut(KeyCode.Backspace, KeyCode.LeftShift),
                "Reset the speedrun timer (Shift+Backspace)");

            // General
            Enabled = Config.Bind("General", "Enabled", true,
                "Enable the speedrun timer");
            ShowHUD = Config.Bind("General", "ShowHUD", true,
                "Show the splits HUD overlay");
            HUDPosition = Config.Bind("General", "HUDPosition", ScreenCorner.BottomLeft,
                "Which corner of the screen to place the timer");
            ShowDeathCount = Config.Bind("General", "ShowDeathCount", true,
                "Show death counter in the HUD");
            LastProfileName = Config.Bind("General", "LastProfileName", "",
                "Last used profile name (empty = auto-detect)");

            // Split toggles (auto-detect mode only)
            TrackCourse1 = Config.Bind("Splits", "TrackCourse1", true, "Track Course 1 completion");
            TrackCourse2 = Config.Bind("Splits", "TrackCourse2", true, "Track Course 2 completion");
            TrackCourse3 = Config.Bind("Splits", "TrackCourse3", true, "Track Course 3 completion");
            TrackCourse4 = Config.Bind("Splits", "TrackCourse4", true, "Track Course 4 completion");
            TrackCourse5 = Config.Bind("Splits", "TrackCourse5", true, "Track Course 5 completion");
            TrackDash = Config.Bind("Splits", "TrackDash", true, "Track Dash upgrade purchase");
            TrackWallJump = Config.Bind("Splits", "TrackWallJump", true, "Track Wall Jump upgrade purchase");
            TrackDoubleJump = Config.Bind("Splits", "TrackDoubleJump", true, "Track Double Jump upgrade purchase");
            TrackBlockSwap = Config.Bind("Splits", "TrackBlockSwap", true, "Track Block Swap upgrade purchase");
            TrackSwapBlocksOnce = Config.Bind("Splits", "TrackSwapBlocksOnce", true, "Track Swap Blocks Once purchase");
            TrackOpenGate = Config.Bind("Splits", "TrackOpenGate", true, "Track Open Gate purchase");
            TrackUnlockPrestige = Config.Bind("Splits", "TrackUnlockPrestige", true, "Track Prestige unlock");
            AutoDetectStartSplits = Config.Bind("Splits", "AutoDetectStartSplits", "",
                "Comma-separated event IDs that auto-start the timer (empty = manual only). Options: game_start, game_start_clean, course_start1..5, course1..5, dash, wallJump, doubleJump, blockSwap, openGate, checkpoint, etc.");
            AutoDetectEndSplit = Config.Bind("Splits", "AutoDetectEndSplits", "swapBlocksOnce,openGate,unlockPrestige",
                "Comma-separated split IDs that end the run in auto-detect mode");

            Timer = new SpeedrunTimer();

            // Subscribe to centralized game events from core mod
            GameEvents.GameStarted += OnGameStarted;
            GameEvents.CourseStarted += OnCourseStarted;
            GameEvents.CourseStopped += OnCourseStopped;
            GameEvents.UpgradeBought += OnUpgradeBought;
            GameEvents.CheckpointHit += OnCheckpointHit;
            GameEvents.PlayerDied += OnPlayerDied;
            GameEvents.PlayerRespawned += OnPlayerRespawned;

            DebugMenuAPI.RegisterHudItem("speedrun", 15, GetHudText);
            DebugMenuAPI.RegisterSection("Speedrun", 45, BuildMainSection);
            DebugMenuAPI.RegisterSection("SR Splits", 46, BuildSplitsSection);

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded!");
        }

        private void Update()
        {
            if (Timer.State == SpeedrunTimer.TimerState.Running)
                Timer.FrameCount++;

            if (!Enabled.Value) return;

            if (StartKey.Value.IsDown() && Timer.State == SpeedrunTimer.TimerState.Idle
                && activeSelectorUI == null)
            {
                string lastProfile = string.IsNullOrEmpty(LastProfileName.Value) ? null : LastProfileName.Value;
                activeSelectorUI = ProfileSelectorUI.Show(lastProfile, OnProfileSelected);
            }

            if (StopKey.Value.IsDown() && Timer.State == SpeedrunTimer.TimerState.Running)
            {
                Timer.StopTimer();
                Log.LogInfo("Speedrun timer stopped manually");
            }

            if (ResetKey.Value.IsDown())
            {
                Timer.ResetTimer();
                Log.LogInfo("Speedrun timer reset");
            }
        }

        private void OnProfileSelected(SpeedrunProfile profile)
        {
            activeSelectorUI = null;
            LastProfileName.Value = profile?.name ?? "";
            Timer.StartTimer(profile);
        }

        private void OnGUI()
        {
            if (!Enabled.Value || !ShowHUD.Value) return;
            if (Timer.State == SpeedrunTimer.TimerState.Idle) return;

            SpeedrunHUD.Draw(Timer);
        }

        private void OnDestroy()
        {
            GameEvents.GameStarted -= OnGameStarted;
            GameEvents.CourseStarted -= OnCourseStarted;
            GameEvents.CourseStopped -= OnCourseStopped;
            GameEvents.UpgradeBought -= OnUpgradeBought;
            GameEvents.CheckpointHit -= OnCheckpointHit;
            GameEvents.PlayerDied -= OnPlayerDied;
            GameEvents.PlayerRespawned -= OnPlayerRespawned;

            DebugMenuAPI.UnregisterHudItem("speedrun");
            DebugMenuAPI.UnregisterSection("Speedrun");
            DebugMenuAPI.UnregisterSection("SR Splits");
        }

        // --- GameEvents handlers ---

        /// <summary>
        /// Resolve the profile to use for auto-start. Uses the last-used profile name if set,
        /// or null for auto-detect mode.
        /// </summary>
        private SpeedrunProfile GetAutoStartProfile()
        {
            if (string.IsNullOrEmpty(LastProfileName.Value))
                return null; // auto-detect
            return ProfileManager.Load(LastProfileName.Value);
        }

        /// <summary>
        /// Try to auto-start the timer for a given event ID. Returns true if it started.
        /// </summary>
        private bool TryAutoStart(string eventId)
        {
            if (!Enabled.Value) return false;
            return Timer.TryAutoStart(eventId, GetAutoStartProfile());
        }

        private void OnGameStarted(GameStartInfo info)
        {
            // Try both generic and clean-only IDs
            if (TryAutoStart("game_start")) return;
            if (info.IsCleanStart) TryAutoStart("game_start_clean");
        }

        private void OnCourseStarted(int courseNumber, courseScript course)
        {
            TryAutoStart($"course_start{courseNumber}");
        }

        private void OnCourseStopped(int courseNumber, bool completed, float courseTime)
        {
            if (!completed) return;
            // Auto-start on course completion if configured
            if (Timer.State == SpeedrunTimer.TimerState.Idle)
            {
                TryAutoStart($"course{courseNumber}");
                return;
            }
            Timer.OnCourseComplete(courseNumber);
        }

        private void OnUpgradeBought(UpgradeInfo info)
        {
            // Auto-start on upgrade if configured
            if (Timer.State == SpeedrunTimer.TimerState.Idle)
            {
                TryAutoStart(info.Id);
                return;
            }
            Timer.OnUpgrade(info.Id, info.Label);
        }

        private void OnCheckpointHit(UnityEngine.Vector2 position)
        {
            TryAutoStart("checkpoint");
        }

        private void OnPlayerDied(UnityEngine.Vector2 position)
        {
            if (Timer.State == SpeedrunTimer.TimerState.Idle)
            {
                TryAutoStart("death");
                return;
            }
            Timer.OnDeath();
        }

        private void OnPlayerRespawned(UnityEngine.Vector2 position)
        {
            TryAutoStart("respawn");
        }

        private string GetHudText()
        {
            if (!Enabled.Value) return null;
            if (Timer.State == SpeedrunTimer.TimerState.Idle) return null;

            string time = SpeedrunHUD.FormatTime(Timer.CurrentTime);
            if (Timer.State == SpeedrunTimer.TimerState.Finished)
                return $"[SPD {time} DONE]";
            return $"[SPD {time}]";
        }

        internal static bool IsSplitEnabled(string splitId)
        {
            switch (splitId)
            {
                case "course1": return TrackCourse1.Value;
                case "course2": return TrackCourse2.Value;
                case "course3": return TrackCourse3.Value;
                case "course4": return TrackCourse4.Value;
                case "course5": return TrackCourse5.Value;
                case "dash": return TrackDash.Value;
                case "wallJump": return TrackWallJump.Value;
                case "doubleJump": return TrackDoubleJump.Value;
                case "blockSwap": return TrackBlockSwap.Value;
                case "swapBlocksOnce": return TrackSwapBlocksOnce.Value;
                case "openGate": return TrackOpenGate.Value;
                case "unlockPrestige": return TrackUnlockPrestige.Value;
                default: return false;
            }
        }

        private void BuildMainSection(WidgetPanel panel)
        {
            panel.AddLabel(() =>
            {
                string status = Timer.State == SpeedrunTimer.TimerState.Running ? "RUNNING"
                    : Timer.State == SpeedrunTimer.TimerState.Finished ? "FINISHED"
                    : "IDLE";
                return $"Status: {status}";
            });

            panel.AddLabel(() =>
            {
                if (Timer.State == SpeedrunTimer.TimerState.Idle) return "";
                return $"Time: {SpeedrunHUD.FormatTime(Timer.CurrentTime)}   Deaths: {Timer.DeathCount}   Splits: {Timer.Splits.Count}";
            });

            panel.AddLabel(() =>
            {
                if (Timer.State == SpeedrunTimer.TimerState.Idle) return "";
                return Timer.IsProfileMode ? $"Profile: {Timer.ActiveProfile.name}" : "Mode: Auto-detect";
            });

            panel.AddLabel(() =>
            {
                var pb = Timer.CurrentPB ?? PBData.Load();
                if (pb != null && pb.totalTime < float.MaxValue)
                    return $"PB: {SpeedrunHUD.FormatTime(pb.totalTime)}  Deaths: {pb.deaths}";
                return "PB: No record";
            });

            panel.AddButtonRow(
                ("Reset Timer", () => { Timer.ResetTimer(); }),
                ("Reset PB", () => { PBData.Delete(); Timer.ReloadPB(); })
            );

            panel.AddButton("Edit Profiles & Triggers", () => { ProfileEditorUI.Show(); });

            panel.AddToggle("Show HUD Overlay", () => ShowHUD.Value, v => ShowHUD.Value = v);
            panel.AddDropdown("HUD Position", () => HUDPosition.Value, v => HUDPosition.Value = v);
            panel.AddToggle("Show Death Count", () => ShowDeathCount.Value, v => ShowDeathCount.Value = v);
        }

        private void BuildSplitsSection(WidgetPanel panel)
        {
            panel.AddToggle("Course 1", () => TrackCourse1.Value, v => TrackCourse1.Value = v);
            panel.AddToggle("Course 2", () => TrackCourse2.Value, v => TrackCourse2.Value = v);
            panel.AddToggle("Course 3", () => TrackCourse3.Value, v => TrackCourse3.Value = v);
            panel.AddToggle("Course 4", () => TrackCourse4.Value, v => TrackCourse4.Value = v);
            panel.AddToggle("Course 5", () => TrackCourse5.Value, v => TrackCourse5.Value = v);
            panel.AddToggle("Dash", () => TrackDash.Value, v => TrackDash.Value = v);
            panel.AddToggle("Wall Jump", () => TrackWallJump.Value, v => TrackWallJump.Value = v);
            panel.AddToggle("Double Jump", () => TrackDoubleJump.Value, v => TrackDoubleJump.Value = v);
            panel.AddToggle("Block Swap", () => TrackBlockSwap.Value, v => TrackBlockSwap.Value = v);
            panel.AddToggle("Swap Blocks Once", () => TrackSwapBlocksOnce.Value, v => TrackSwapBlocksOnce.Value = v);
            panel.AddToggle("Open Gate", () => TrackOpenGate.Value, v => TrackOpenGate.Value = v);
            panel.AddToggle("Prestige", () => TrackUnlockPrestige.Value, v => TrackUnlockPrestige.Value = v);
        }

    }
}
