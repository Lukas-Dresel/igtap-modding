using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine.InputSystem;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using IGTAPMod;
using UnityEngine;

namespace IGTAPReplay
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.igtapmod.plugin")]
    [BepInDependency("com.igtapmod.fixedtimestep", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.igtapmod.speedrun", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.igtapmod.replay";
        public const string PluginName = "IGTAP Replay";
        public const string PluginVersion = "2.0.0";

        internal static ManualLogSource Log;

        internal static void DbgLog(string msg)
        {
            if (DebugLifecycleLogging != null && DebugLifecycleLogging.Value)
                Log.LogInfo($"[DBG] {msg}");
        }
        internal static Plugin Instance;
        internal static Harmony HarmonyInstance;

        private enum Mode { Idle, ManualRecording, Browsing, Playing }
        private Mode mode = Mode.Idle;

        // --- Ring buffer & clip manager ---
        internal InputRingBuffer ringBuffer;
        internal ClipManager ClipMgr;

        // --- Keybinds ---
        internal static ConfigEntry<KeyboardShortcut> RecordKey;
        internal static ConfigEntry<KeyboardShortcut> HighlightKey;
        internal static ConfigEntry<KeyboardShortcut> PlayKey;
        internal static ConfigEntry<KeyboardShortcut> StopKey;

        // --- Ring buffer ---
        internal static ConfigEntry<int> RingBufferSeconds;

        // --- Highlights ---
        internal static ConfigEntry<int> HighlightSeconds;

        // --- Course recording ---
        internal static ConfigEntry<bool> AutoRecordCourses;
        internal static ConfigEntry<int> CoursePrePaddingSeconds;
        internal static ConfigEntry<int> CoursePostPaddingSeconds;

        // --- General ---
        internal static ConfigEntry<string> ReplayDirectory;
        internal static ConfigEntry<bool> RecordMousePosition;

        // --- Debug ---
        internal static ConfigEntry<bool> PerFrameCheckpoints;
        internal static ConfigEntry<bool> DebugLifecycleLogging;

        // --- Timeline ---
        internal static ConfigEntry<bool> TimelineFullReplay;

        // --- Ghosts during replay ---
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

        // File picker
        private struct BrowseCategory
        {
            public string Name;
            public string[] Files; // full paths, sorted newest first
        }
        private List<BrowseCategory> browseCategories;
        private int browseTab;
        private int browseSelection;
        private Vector2 browseScroll;
        private Texture2D browseBgTex;

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            // Keybinds
            RecordKey = Config.Bind("Keybinds", "Record",
                new KeyboardShortcut(KeyCode.F5),
                "Toggle manual recording (start/stop). Pins the ring buffer so it grows past capacity.");
            HighlightKey = Config.Bind("Keybinds", "Highlight",
                new KeyboardShortcut(KeyCode.F5, KeyCode.LeftShift),
                "Save an instant highlight (last N seconds from the ring buffer)");
            PlayKey = Config.Bind("Keybinds", "Play",
                new KeyboardShortcut(KeyCode.F6),
                "Load and start playback of the most recent replay file");
            StopKey = Config.Bind("Keybinds", "Stop",
                new KeyboardShortcut(KeyCode.F7),
                "Stop playback or manual recording");

            // Ring buffer
            RingBufferSeconds = Config.Bind("RingBuffer", "DurationSeconds", 120,
                "How many seconds the background ring buffer retains (soft limit, pins override)");

            // Highlights
            HighlightSeconds = Config.Bind("Highlights", "DurationSeconds", 30,
                "Duration in seconds for instant highlight saves (Shift+F5)");

            // Course recording
            AutoRecordCourses = Config.Bind("CourseRecording", "AutoRecord", true,
                "Automatically save replays for course attempts/completions");
            CoursePrePaddingSeconds = Config.Bind("CourseRecording", "PrePaddingSeconds", 5,
                "Seconds before course start to include in auto-saved replays");
            CoursePostPaddingSeconds = Config.Bind("CourseRecording", "PostPaddingSeconds", 5,
                "Seconds after course end to include in auto-saved replays");

            // General
            ReplayDirectory = Config.Bind("General", "ReplayDirectory", "replays",
                "Directory for saved replays (relative to BepInEx/config/)");
            RecordMousePosition = Config.Bind("General", "RecordMousePosition", false,
                "Record mouse screen position each frame (bloats the file, usually not needed)");

            // Debug
            PerFrameCheckpoints = Config.Bind("Debug", "PerFrameCheckpoints", false,
                "Record a checkpoint every single frame (large files, for debugging desync)");
            DebugLifecycleLogging = Config.Bind("Debug", "LifecycleLogging", false,
                "Log every lifecycle event: inject, skip, seek, pause, resume, step, etc.");

            // Timeline
            TimelineFullReplay = Config.Bind("Timeline", "ShowFullTimeline", false,
                "Show the full replay timeline instead of a +/-5s window around the playhead");

            // Ghosts
            ReplayGhostsEnabled = Config.Bind("Ghosts", "EnableDuringReplay", true,
                "Show ghost markers at input change points during replay");
            ReplayGhostsAll = Config.Bind("Ghosts", "ShowAll", false,
                "Show all ghosts at once (overrides Prev/Next counts)");
            ReplayGhostsPrev = Config.Bind("Ghosts", "PreviousCount", 5,
                "Number of past input-change ghosts to show during replay");
            ReplayGhostsNext = Config.Bind("Ghosts", "NextCount", 3,
                "Number of upcoming input-change ghosts to show during replay");

            // Create ring buffer
            int fps = Time.captureFramerate > 0 ? Time.captureFramerate : 50;
            int capacityFrames = RingBufferSeconds.Value * fps;
            ringBuffer = new InputRingBuffer(capacityFrames, fps);

            // Set up ignored keys for buffer
            ringBuffer.SetIgnoredKeys(new[]
            {
                RecordKey.Value.MainKey,
                HighlightKey.Value.MainKey,
                PlayKey.Value.MainKey,
                StopKey.Value.MainKey,
                KeyCode.Backspace,
            });

            // Create clip manager
            ClipMgr = new ClipManager(ringBuffer);

            HarmonyInstance = new Harmony(PluginGUID);
            HarmonyInstance.PatchAll();

            // Subscribe to centralized game events for course auto-recording
            GameEvents.CourseStarted += OnGameCourseStarted;
            GameEvents.CourseStopped += OnGameCourseStopped;

            // Init speedrun bridge (no-op if speedrun mod is absent)
            SpeedrunBridge.Init();

            // Register HUD item
            DebugMenuAPI.RegisterHudItem("Replay", 20, GetHudText);

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded!");
        }

        private void Update()
        {
            // Auto-start ring buffer when player exists
            var player = GameState.Player;
            if (!ringBuffer.IsRunning && player != null && mode != Mode.Playing)
            {
                ringBuffer.Start(player);
                if (editor == null)
                    editor = gameObject.AddComponent<ReplayEditor>();
                editor.Begin(player);
                ringBuffer.Editor = editor;
            }
            else if (ringBuffer.IsRunning && player == null)
            {
                // Player destroyed — flush pending clips and stop buffer
                ClipMgr.FlushPending();
                ringBuffer.Editor = null;
                editor?.End();
                ringBuffer.Stop();
                if (mode == Mode.ManualRecording)
                    mode = Mode.Idle;
            }

            // Update clip manager (handles delayed course saves)
            ClipMgr.Update();

            // Highlight key (Shift+F5) — check first since it's a modifier combo
            if (HighlightKey.Value.IsDown() && ringBuffer.IsRunning && mode != Mode.Playing)
            {
                ClipMgr.SaveHighlight();
            }
            // Record key (F5) — toggle manual recording
            else if (RecordKey.Value.IsDown() && mode != Mode.Playing)
            {
                if (mode == Mode.ManualRecording)
                {
                    // Stop manual recording
                    ClipMgr.StopManualRecording();
                    mode = Mode.Idle;
                }
                else if (mode == Mode.Idle && ringBuffer.IsRunning)
                {
                    // Start manual recording
                    ClipMgr.StartManualRecording();
                    mode = Mode.ManualRecording;
                }
            }
            // Play key (F6) — open file browser
            else if (PlayKey.Value.IsDown() && mode == Mode.Idle)
            {
                OpenBrowser();
            }
            // Stop key (F7)
            else if (mode != Mode.Idle && mode != Mode.Browsing && StopKey.Value.IsDown())
            {
                StartCoroutine(StopEndOfFrame());
            }

            // File browser input (using new InputSystem — browser is never active during replay)
            if (mode == Mode.Browsing)
            {
                var kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.escapeKey.wasPressedThisFrame || StopKey.Value.IsDown())
                    {
                        mode = Mode.Idle;
                    }
                    else if (kb.leftArrowKey.wasPressedThisFrame)
                    {
                        browseTab = Mathf.Max(0, browseTab - 1);
                        browseSelection = 0;
                        browseScroll = Vector2.zero;
                    }
                    else if (kb.rightArrowKey.wasPressedThisFrame)
                    {
                        browseTab = Mathf.Min(browseCategories.Count - 1, browseTab + 1);
                        browseSelection = 0;
                        browseScroll = Vector2.zero;
                    }
                    else if (kb.upArrowKey.wasPressedThisFrame)
                    {
                        browseSelection = Mathf.Max(0, browseSelection - 1);
                    }
                    else if (kb.downArrowKey.wasPressedThisFrame)
                    {
                        var cat = browseCategories[browseTab];
                        browseSelection = Mathf.Min(cat.Files.Length - 1, browseSelection + 1);
                    }
                    else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                    {
                        var cat = browseCategories[browseTab];
                        if (cat.Files.Length > 0 && browseSelection < cat.Files.Length)
                        {
                            PlayFile(cat.Files[browseSelection]);
                        }
                    }
                }
            }
        }

        private void OpenBrowser()
        {
            string dir = ClipManager.GetReplayDirectory();
            browseCategories = new List<BrowseCategory>();
            var allFiles = new List<string>();

            // Recordings category: files in base directory
            if (Directory.Exists(dir))
            {
                var baseFiles = Directory.GetFiles(dir, "*.txt")
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .ToArray();
                if (baseFiles.Length > 0)
                {
                    browseCategories.Add(new BrowseCategory { Name = "Recordings", Files = baseFiles });
                    allFiles.AddRange(baseFiles);
                }
            }

            // Subdirectory categories: one tab per subdirectory (course1, course2, speedrun, etc.)
            if (Directory.Exists(dir))
            {
                var subDirs = Directory.GetDirectories(dir)
                    .OrderBy(d => {
                        string name = Path.GetFileName(d);
                        // Sort course directories numerically first, others alphabetically after
                        if (name.StartsWith("course") && int.TryParse(name.Substring(6), out int n))
                            return $"0_{n:D4}";
                        return $"1_{name}";
                    })
                    .ToArray();

                foreach (var subDir in subDirs)
                {
                    var subFiles = Directory.GetFiles(subDir, "*.txt")
                        .OrderByDescending(f => File.GetLastWriteTime(f))
                        .ToArray();
                    if (subFiles.Length > 0)
                    {
                        string dirName = Path.GetFileName(subDir);
                        // "course1" -> "Course 1", "speedrun" -> "Speedrun"
                        string label;
                        if (dirName.StartsWith("course") && int.TryParse(dirName.Substring(6), out _))
                            label = "Course " + dirName.Substring(6);
                        else
                            label = char.ToUpper(dirName[0]) + dirName.Substring(1);
                        browseCategories.Add(new BrowseCategory { Name = label, Files = subFiles });
                        allFiles.AddRange(subFiles);
                    }
                }
            }

            // "All" tab at the front
            var allSorted = allFiles.OrderByDescending(f => File.GetLastWriteTime(f)).ToArray();
            browseCategories.Insert(0, new BrowseCategory { Name = "All", Files = allSorted });

            if (allSorted.Length == 0)
            {
                ShowToast("No replay files found");
                return;
            }

            browseTab = 0;
            browseSelection = 0;
            browseScroll = Vector2.zero;
            mode = Mode.Browsing;
        }

        private void PlayFile(string path)
        {
            var player = GameState.Player;
            if (player == null)
            {
                Log.LogError("No player found. Enter a level first.");
                mode = Mode.Idle;
                return;
            }

            var replayFile = ReplayFormat.Read(path);
            Log.LogInfo($"Loaded replay: {Path.GetFileName(path)} — {replayFile.Spans.Count} spans, {replayFile.VerifyPoints.Count} verify points.");

            // Pause ring buffer during playback
            ringBuffer.Editor = null;
            editor?.End();
            ringBuffer.Stop();

            if (playback == null)
                playback = gameObject.AddComponent<ReplayPlayback>();
            if (editor == null)
                editor = gameObject.AddComponent<ReplayEditor>();

            DbgLog("PlayFile called");
            editor.OriginalLoadPath = path;
            playback.StartPlayback(player, replayFile);
            editor.BeginReplay(player, replayFile);
            mode = Mode.Playing;
            ShowToast($"Playing: {Path.GetFileName(path)}");
            Log.LogInfo("Playing...");
        }

        private System.Collections.IEnumerator StopEndOfFrame()
        {
            yield return new WaitForEndOfFrame();
            Stop();
        }

        private void Stop()
        {
            DbgLog($"Stop() mode={mode}");
            if (mode == Mode.ManualRecording)
            {
                ClipMgr.StopManualRecording();
            }
            else if (mode == Mode.Playing)
            {
                editor?.End();
                playback?.StopPlayback();
                ShowToast("Playback stopped");

                // Resume ring buffer after playback
                // (will auto-start on next Update when player is detected)
            }

            mode = Mode.Idle;
        }

        /// <summary>Called by the Exit button on the transport bar.</summary>
        internal void RequestStop() => StartCoroutine(StopEndOfFrame());

        /// <summary>Called by ReplayPlayback when it reaches the end of the replay.</summary>
        internal void OnPlaybackFinished()
        {
            DbgLog("OnPlaybackFinished");
            editor?.PausePlayback();
            ShowToast("Playback finished");
        }

        private string GetHudText()
        {
            switch (mode)
            {
                case Mode.ManualRecording:
                    return $"[REC {ClipMgr.ManualRecordingFrames}]";
                case Mode.Browsing:
                    return "[SELECT REPLAY]";
                case Mode.Playing:
                    return playback != null ? $"[PLAY {playback.FrameCount}]" : "[PLAY]";
                default:
                    return ringBuffer.IsRunning ? $"[BUF {ringBuffer.FrameCount}]" : null;
            }
        }

        internal void ShowToast(string message)
        {
            toastMessage = message;
            toastEndTime = Time.unscaledTime + ToastDuration;
        }

        private void OnGUI()
        {
            // Toast
            if (!string.IsNullOrEmpty(toastMessage))
            {
                if (Time.unscaledTime > toastEndTime)
                    toastMessage = null;
                else
                    DrawToast();
            }

            // File picker
            if (mode == Mode.Browsing && browseCategories != null)
                DrawBrowser();
        }

        private void DrawToast()
        {
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

            style.normal.textColor = new Color(0f, 0f, 0f, alpha);
            GUI.Label(new Rect(x + 1, y + 1, w, h), toastMessage, style);
            style.normal.textColor = new Color(1f, 1f, 1f, alpha);
            GUI.Label(new Rect(x, y, w, h), toastMessage, style);
        }

        private void DrawBrowser()
        {
            // Background texture
            if (browseBgTex == null)
            {
                browseBgTex = new Texture2D(1, 1);
                browseBgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.85f));
                browseBgTex.Apply();
            }

            float panelW = Mathf.Min(600f, Screen.width - 40f);
            float panelH = Mathf.Min(400f, Screen.height - 40f);
            float panelX = (Screen.width - panelW) / 2f;
            float panelY = (Screen.height - panelH) / 2f;
            var panelRect = new Rect(panelX, panelY, panelW, panelH);

            // Draw background
            GUI.DrawTexture(panelRect, browseBgTex);

            GUILayout.BeginArea(panelRect);
            GUILayout.Space(8);

            // Title
            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            titleStyle.normal.textColor = Color.white;
            GUILayout.Label("Select Replay", titleStyle);
            GUILayout.Space(4);

            // Tab bar
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            for (int i = 0; i < browseCategories.Count; i++)
            {
                var cat = browseCategories[i];
                bool isSelected = i == browseTab;
                var tabStyle = new GUIStyle(GUI.skin.button) { fontSize = 13 };
                if (isSelected)
                {
                    tabStyle.normal.textColor = Color.yellow;
                    tabStyle.fontStyle = FontStyle.Bold;
                }
                string label = $"{cat.Name} ({cat.Files.Length})";
                if (GUILayout.Button(label, tabStyle, GUILayout.MinWidth(80)))
                {
                    browseTab = i;
                    browseSelection = 0;
                    browseScroll = Vector2.zero;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // File list
            var currentCat = browseCategories[browseTab];
            if (currentCat.Files.Length == 0)
            {
                var emptyStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 14,
                };
                emptyStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
                GUILayout.FlexibleSpace();
                GUILayout.Label("No replays in this category", emptyStyle);
                GUILayout.FlexibleSpace();
            }
            else
            {
                browseScroll = GUILayout.BeginScrollView(browseScroll);
                for (int i = 0; i < currentCat.Files.Length; i++)
                {
                    string filePath = currentCat.Files[i];
                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    var fileInfo = new FileInfo(filePath);
                    string date = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
                    string size = fileInfo.Length < 1024 ? $"{fileInfo.Length}B" : $"{fileInfo.Length / 1024}KB";

                    // Include parent folder name for "All" tab to distinguish courses
                    string folderHint = "";
                    if (browseTab == 0) // "All" tab
                    {
                        string parentName = Path.GetFileName(Path.GetDirectoryName(filePath));
                        string replayDirName = Path.GetFileName(ClipManager.GetReplayDirectory());
                        if (parentName != replayDirName)
                            folderHint = $"[{parentName}] ";
                    }

                    bool isSelected = i == browseSelection;
                    var itemStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
                    itemStyle.normal.textColor = isSelected ? Color.yellow : Color.white;
                    if (isSelected)
                        itemStyle.fontStyle = FontStyle.Bold;

                    string display = $"{(isSelected ? "> " : "  ")}{folderHint}{fileName}   {date}   {size}";
                    if (GUILayout.Button(display, itemStyle))
                    {
                        browseSelection = i;
                        PlayFile(filePath);
                    }
                }
                GUILayout.EndScrollView();
            }

            GUILayout.FlexibleSpace();

            // Help text
            var helpStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
            };
            helpStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            GUILayout.Label("Left/Right: switch tab   Up/Down: select   Enter: play   Esc: close", helpStyle);
            GUILayout.Space(4);

            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            if (mode != Mode.Idle)
                Stop();
            if (ringBuffer.IsRunning)
            {
                ClipMgr.FlushPending();
                ringBuffer.Stop();
            }

            GameEvents.CourseStarted -= OnGameCourseStarted;
            GameEvents.CourseStopped -= OnGameCourseStopped;
            SpeedrunBridge.Destroy();

            HarmonyInstance?.UnpatchSelf();
            DebugMenuAPI.UnregisterHudItem("Replay");
        }

        // --- GameEvents handlers for course auto-recording ---

        private void OnGameCourseStarted(int courseNumber, courseScript course)
        {
            ClipMgr?.OnCourseStart(course);
        }

        private void OnGameCourseStopped(int courseNumber, bool completed, float courseTime)
        {
            // ClipManager needs the courseScript instance, but GameEvents only passes courseNumber.
            // Find the courseScript by number. Use the same pattern as ClipManager uses internally.
            var courses = Object.FindObjectsByType<courseScript>(FindObjectsSortMode.None);
            foreach (var course in courses)
            {
                if (course.courseNumber == courseNumber)
                {
                    ClipMgr?.OnCourseEnd(course, completed);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Harmony patch: prefix on Movement.Update to record input into ring buffer
    /// and inject input during playback.
    /// </summary>
    [HarmonyPatch(typeof(Movement), "Update")]
    public static class MovementUpdatePatch
    {
        // True while Movement.Update is actively executing — used to gate
        // Physics2D.Raycast logging so we only capture the game's own raycasts.
        public static bool InMovementUpdate;
        public static int RaycastCallIndex; // resets each Movement.Update, ticks per raycast

        static bool Prefix(Movement __instance)
        {
            var ringBuffer = Plugin.Instance?.ringBuffer;
            var playback = Object.FindAnyObjectByType<ReplayPlayback>();
            bool isPlaying = playback != null && playback.IsPlaying;

            Plugin.DbgLog($"Movement.Update PREFIX playing={isPlaying} bufferRunning={ringBuffer?.IsRunning} timeScale={Time.timeScale}");

            // Record into ring buffer (skip during playback)
            if (ringBuffer != null && ringBuffer.IsRunning && !isPlaying)
                ringBuffer.OnFrame(__instance);

            if (isPlaying)
            {
                if (!playback.ShouldMovementUpdate())
                {
                    Plugin.DbgLog("Movement.Update PREFIX -> SKIP (ShouldMovementUpdate=false)");
                    return false;
                }

                playback.InjectCurrentFrame();
            }

            Plugin.DbgLog("Movement.Update PREFIX -> RUN");
            InMovementUpdate = true;
            RaycastCallIndex = 0;
            return true;
        }

        static void Postfix(Movement __instance)
        {
            InMovementUpdate = false;

            // Recording: capture checkpoints/verify points after Movement.Update
            var ringBuffer = Plugin.Instance?.ringBuffer;
            var playback = Object.FindAnyObjectByType<ReplayPlayback>();
            bool isPlaying = playback != null && playback.IsPlaying;

            if (ringBuffer != null && ringBuffer.IsRunning && !isPlaying)
                ringBuffer.OnPostFrame(__instance);

            if (isPlaying)
            {
                Plugin.DbgLog($"Movement.Update POSTFIX fc={playback.FrameCount}");
                playback.PostMovementUpdate();
            }
        }
    }

    /// <summary>
    /// Harmony hook on Physics2D.Raycast(Vector2, Vector2, float, int) — logs every
    /// call made from inside Movement.Update so we can see exactly what ground/wall
    /// detection is returning. Gated by MovementUpdatePatch.InMovementUpdate so we
    /// don't drown in other scripts' raycasts.
    /// </summary>
    [HarmonyPatch(typeof(Physics2D), nameof(Physics2D.Raycast),
        new System.Type[] { typeof(Vector2), typeof(Vector2), typeof(float), typeof(int) })]
    public static class Physics2DRaycastPatch
    {
        static void Postfix(Vector2 origin, Vector2 direction, float distance, int layerMask, RaycastHit2D __result)
        {
            if (!MovementUpdatePatch.InMovementUpdate) return;
            int idx = MovementUpdatePatch.RaycastCallIndex++;
            if (__result.collider != null)
            {
                Plugin.DbgLog($"  RC[{idx}] origin=({origin.x:F3},{origin.y:F3}) dir=({direction.x:F2},{direction.y:F2}) dist={distance:F1} mask=0x{layerMask:X} -> HIT @({__result.point.x:F3},{__result.point.y:F3}) d={__result.distance:F3} n=({__result.normal.x:F2},{__result.normal.y:F2}) tag={__result.collider.tag} col={__result.collider.name}");
            }
            else
            {
                Plugin.DbgLog($"  RC[{idx}] origin=({origin.x:F3},{origin.y:F3}) dir=({direction.x:F2},{direction.y:F2}) dist={distance:F1} mask=0x{layerMask:X} -> MISS");
            }
        }
    }

    /// <summary>
    /// Harmony patch: postfix on Movement.respawn — log the event.
    /// </summary>
    [HarmonyPatch(typeof(Movement), "respawn")]
    public static class MovementRespawnPatch
    {
        static void Postfix(Movement __instance)
        {
            Plugin.DbgLog("Movement.respawn POSTFIX");
        }
    }

    // Course start/stop patches removed — now handled via GameEvents subscriptions in Plugin.cs
}
