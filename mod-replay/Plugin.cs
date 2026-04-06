using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using IGTAPMod;
using UnityEngine;

namespace IGTAPReplay
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.igtapmod.plugin")]
    [BepInDependency("com.igtapmod.fixedtimestep")]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.igtapmod.replay";
        public const string PluginName = "IGTAP Replay";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;
        internal static Harmony HarmonyInstance;

        private enum Mode { Idle, Recording, Playing }
        private Mode mode = Mode.Idle;

        internal static ConfigEntry<KeyboardShortcut> RecordKey;
        internal static ConfigEntry<KeyboardShortcut> PlayKey;
        internal static ConfigEntry<KeyboardShortcut> StopKey;
        internal static ConfigEntry<string> ReplayFilePath;
        internal static ConfigEntry<bool> RecordMousePosition;
        internal static ConfigEntry<bool> RestartOnRespawn;

        // Timeline
        internal static ConfigEntry<bool> TimelineFullReplay;

        // Ghosts during replay
        internal static ConfigEntry<bool> ReplayGhostsEnabled;
        internal static ConfigEntry<int> ReplayGhostsPrev;
        internal static ConfigEntry<int> ReplayGhostsNext;
        internal static ConfigEntry<bool> ReplayGhostsAll;

        private ReplayPlayback playback;
        private ReplayEditor editor;

        // Toast
        private string toastMessage;
        private float toastEndTime;
        private const float ToastDuration = 2f;

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            RecordKey = Config.Bind("Keybinds", "Record",
                new KeyboardShortcut(KeyCode.F5),
                "Start recording input");
            PlayKey = Config.Bind("Keybinds", "Play",
                new KeyboardShortcut(KeyCode.F6),
                "Start playback from replay file");
            StopKey = Config.Bind("Keybinds", "Stop",
                new KeyboardShortcut(KeyCode.F7),
                "Stop recording or playback");
            ReplayFilePath = Config.Bind("General", "ReplayFile", "replay.txt",
                "Path to the replay file (relative to BepInEx/config/)");
            RecordMousePosition = Config.Bind("General", "RecordMousePosition", false,
                "Record mouse screen position each frame (bloats the file, usually not needed)");
            RestartOnRespawn = Config.Bind("General", "RestartOnRespawn", false,
                "Automatically restart recording when the player respawns (discards previous recording)");

            TimelineFullReplay = Config.Bind("Timeline", "ShowFullTimeline", false,
                "Show the full replay timeline instead of a +/-5s window around the playhead");

            ReplayGhostsEnabled = Config.Bind("Ghosts", "EnableDuringReplay", true,
                "Show ghost markers at input change points during replay");
            ReplayGhostsAll = Config.Bind("Ghosts", "ShowAll", false,
                "Show all ghosts at once (overrides Prev/Next counts)");
            ReplayGhostsPrev = Config.Bind("Ghosts", "PreviousCount", 5,
                "Number of past input-change ghosts to show during replay");
            ReplayGhostsNext = Config.Bind("Ghosts", "NextCount", 3,
                "Number of upcoming input-change ghosts to show during replay");

            // Set up ignored keys for recorder (includes Backspace for pause/step)
            ReplayRecorder.SetIgnoredKeys(new[]
            {
                RecordKey.Value.MainKey,
                PlayKey.Value.MainKey,
                StopKey.Value.MainKey,
                KeyCode.Backspace,
            });

            HarmonyInstance = new Harmony(PluginGUID);
            HarmonyInstance.PatchAll();

            // Register HUD item
            DebugMenuAPI.RegisterHudItem("Replay", 20, GetHudText);

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded!");
        }

        private void Update()
        {
            if (RecordKey.Value.IsDown() && mode == Mode.Idle)
            {
                StartRecording();
            }
            else if (PlayKey.Value.IsDown() && mode == Mode.Idle)
            {
                StartPlaying();
            }
            else if (StopKey.Value.IsDown() && mode != Mode.Idle)
            {
                Stop();
            }
        }

        private void StartRecording()
        {
            var player = GameState.Player;
            if (player == null)
            {
                Log.LogError("No player found. Enter a level first.");
                return;
            }

            ReplayRecorder.StartRecording(player);
            mode = Mode.Recording;

            if (editor == null)
                editor = gameObject.AddComponent<ReplayEditor>();
            editor.Begin(player);
            ReplayRecorder.Editor = editor;

            ShowToast("Recording started");
            Log.LogInfo("Recording...");
        }

        private void StartPlaying()
        {
            var player = GameState.Player;
            if (player == null)
            {
                Log.LogError("No player found. Enter a level first.");
                return;
            }

            string path = GetReplayPath();
            if (!File.Exists(path))
            {
                Log.LogError($"Replay file not found: {path}");
                return;
            }

            var replayFile = ReplayFormat.Read(path);
            Log.LogInfo($"Loaded replay: {replayFile.Spans.Count} spans, {replayFile.VerifyPoints.Count} verify points.");

            if (playback == null)
                playback = gameObject.AddComponent<ReplayPlayback>();

            if (editor == null)
                editor = gameObject.AddComponent<ReplayEditor>();

            playback.StartPlayback(player, replayFile);
            editor.BeginReplay(player, replayFile);
            mode = Mode.Playing;
            ShowToast("Playback started");
            Log.LogInfo("Playing...");
        }

        private void Stop()
        {
            if (mode == Mode.Recording)
            {
                ReplayRecorder.Editor = null;
                editor?.End();
                var replayFile = ReplayRecorder.StopRecording();
                if (replayFile != null)
                {
                    string path = GetReplayPath();
                    ReplayFormat.Write(replayFile, path);
                    ShowToast($"Recording saved ({ReplayRecorder.FrameCount} frames)");
                    Log.LogInfo($"Replay saved to: {path}");
                }
            }
            else if (mode == Mode.Playing)
            {
                editor?.End();
                playback?.StopPlayback();
                ShowToast("Playback stopped");
            }

            mode = Mode.Idle;
        }

        /// <summary>Called by ReplayPlayback when it reaches the end of the replay.</summary>
        internal void OnPlaybackFinished()
        {
            // Don't tear down — just pause at the end so the user can scrub/review
            editor?.PausePlayback();
            ShowToast("Playback finished");
        }

        /// <summary>Called by the respawn Harmony patch when the player respawns.</summary>
        internal void OnPlayerRespawned(Movement player)
        {
            if (mode != Mode.Recording) return;
            if (!RestartOnRespawn.Value) return;

            Log.LogInfo("Player respawned — restarting recording.");
            editor?.End();
            ReplayRecorder.StopRecording();
            ReplayRecorder.StartRecording(player);
            editor?.Begin(player);
            ShowToast("Recording restarted (respawn)");
        }

        private string GetHudText()
        {
            switch (mode)
            {
                case Mode.Recording:
                    return $"[REC {ReplayRecorder.FrameCount}]";
                case Mode.Playing:
                    return playback != null ? $"[PLAY {playback.FrameCount}]" : "[PLAY]";
                default:
                    return null;
            }
        }

        internal void ShowToast(string message)
        {
            toastMessage = message;
            toastEndTime = Time.unscaledTime + ToastDuration;
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(toastMessage)) return;
            if (Time.unscaledTime > toastEndTime)
            {
                toastMessage = null;
                return;
            }

            float alpha = Mathf.Clamp01((toastEndTime - Time.unscaledTime) / 0.5f);
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };

            float w = 400f;
            float h = 30f;
            float x = (Screen.width - w) / 2f;
            float y = Screen.height - 60f;

            // Shadow
            style.normal.textColor = new Color(0f, 0f, 0f, alpha);
            GUI.Label(new Rect(x + 1, y + 1, w, h), toastMessage, style);
            // Text
            style.normal.textColor = new Color(1f, 1f, 1f, alpha);
            GUI.Label(new Rect(x, y, w, h), toastMessage, style);
        }

        private static string GetReplayPath()
        {
            var filePath = ReplayFilePath.Value;
            if (Path.IsPathRooted(filePath))
                return filePath;
            return Path.Combine(Paths.ConfigPath, filePath);
        }

        private void OnDestroy()
        {
            if (mode != Mode.Idle)
                Stop();
            HarmonyInstance?.UnpatchSelf();
            DebugMenuAPI.UnregisterHudItem("Replay");
        }
    }

    /// <summary>
    /// Harmony patch: prefix on Movement.Update to capture input for recording.
    /// </summary>
    [HarmonyPatch(typeof(Movement), "Update")]
    public static class MovementUpdatePatch
    {
        static void Prefix(Movement __instance)
        {
            if (ReplayRecorder.IsRecording)
                ReplayRecorder.OnFrame(__instance);
        }
    }

    /// <summary>
    /// Harmony patch: postfix on Movement.respawn to restart recording on death.
    /// </summary>
    [HarmonyPatch(typeof(Movement), "respawn")]
    public static class MovementRespawnPatch
    {
        static void Postfix(Movement __instance)
        {
            Plugin.Instance?.OnPlayerRespawned(__instance);
        }
    }
}
