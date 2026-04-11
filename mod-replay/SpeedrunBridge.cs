using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx;
using IGTAPMod;
using UnityEngine;

namespace IGTAPReplay
{
    /// <summary>
    /// Optional integration with the speedrun timer mod. When both mods are installed,
    /// automatically records a replay during speedruns with game event markers.
    /// All methods that reference IGTAPSpeedrun types use [MethodImpl(NoInlining)]
    /// so the CLR never JIT-compiles them when the speedrun mod is absent.
    /// </summary>
    public static class SpeedrunBridge
    {
        private static bool initialized;
        private static bool active;          // true while a speedrun recording is in progress
        private static int pinFrame;
        private static readonly Stopwatch wallclock = new Stopwatch();
        private static readonly List<EventMarker> markers = new List<EventMarker>();
        private static string profileName;
        private static string splitIds;

        public static bool IsAvailable
        {
            get
            {
                try { return CheckSpeedrunLoaded(); }
                catch { return false; }
            }
        }

        public static bool IsActive => active;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool CheckSpeedrunLoaded()
        {
            return IGTAPSpeedrun.Plugin.Instance != null;
        }

        /// <summary>
        /// Call from replay Plugin.Awake(). If the speedrun mod is present,
        /// subscribes to timer lifecycle events and game events for marker logging.
        /// </summary>
        public static void Init()
        {
            if (!IsAvailable) return;
            AttachToSpeedrun();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AttachToSpeedrun()
        {
            var timer = IGTAPSpeedrun.Plugin.Instance.Timer;
            timer.OnTimerStarted += OnTimerStarted;
            timer.OnTimerStopped += OnTimerStopped;
            timer.OnTimerReset += OnTimerReset;
            timer.OnSplitRecorded += OnSplitRecorded;

            // Subscribe to all game events for marker logging
            GameEvents.GameStarted += OnGameStarted;
            GameEvents.CourseStarted += OnCourseStarted;
            GameEvents.CourseStopped += OnCourseStopped;
            GameEvents.UpgradeBought += OnUpgradeBought;
            GameEvents.CheckpointHit += OnCheckpointHit;
            GameEvents.PlayerDied += OnPlayerDied;
            GameEvents.PlayerRespawned += OnPlayerRespawned;

            initialized = true;
            Plugin.Log.LogInfo("SpeedrunBridge: attached to speedrun timer");
        }

        /// <summary>
        /// Call from replay Plugin.OnDestroy(). Unsubscribes from all events.
        /// </summary>
        public static void Destroy()
        {
            if (!initialized) return;
            DetachFromSpeedrun();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DetachFromSpeedrun()
        {
            if (IGTAPSpeedrun.Plugin.Instance != null)
            {
                var timer = IGTAPSpeedrun.Plugin.Instance.Timer;
                timer.OnTimerStarted -= OnTimerStarted;
                timer.OnTimerStopped -= OnTimerStopped;
                timer.OnTimerReset -= OnTimerReset;
                timer.OnSplitRecorded -= OnSplitRecorded;
            }

            GameEvents.GameStarted -= OnGameStarted;
            GameEvents.CourseStarted -= OnCourseStarted;
            GameEvents.CourseStopped -= OnCourseStopped;
            GameEvents.UpgradeBought -= OnUpgradeBought;
            GameEvents.CheckpointHit -= OnCheckpointHit;
            GameEvents.PlayerDied -= OnPlayerDied;
            GameEvents.PlayerRespawned -= OnPlayerRespawned;

            if (active)
                CleanupPin();

            initialized = false;
        }

        // --- Timer lifecycle handlers ---

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void OnTimerStarted(IGTAPSpeedrun.TimerStartedInfo info)
        {
            var ringBuffer = Plugin.Instance?.ringBuffer;
            if (ringBuffer == null || !ringBuffer.IsRunning)
            {
                Plugin.Log.LogWarning("SpeedrunBridge: ring buffer not running, skipping recording");
                return;
            }

            ringBuffer.CaptureSnapshot(ringBuffer.TrackedPlayer);
            pinFrame = ringBuffer.HeadFrame;
            ringBuffer.Pin(pinFrame);
            wallclock.Restart();
            markers.Clear();

            profileName = info.ProfileName ?? "auto";
            splitIds = "";
            if (info.Profile != null)
                splitIds = string.Join(",", info.Profile.splits.Select(s => s.id));

            AddMarker(new EventMarker
            {
                Type = "speedrun_start",
                Profile = profileName,
            });

            active = true;
            Plugin.Log.LogInfo($"SpeedrunBridge: recording started at frame {pinFrame}, profile={profileName}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void OnTimerStopped(IGTAPSpeedrun.TimerStoppedInfo info)
        {
            if (!active) return;

            var ringBuffer = Plugin.Instance?.ringBuffer;
            if (ringBuffer == null || !ringBuffer.IsRunning)
            {
                Plugin.Log.LogWarning("SpeedrunBridge: ring buffer not running on timer stop, discarding");
                CleanupPin();
                return;
            }

            ringBuffer.CaptureSnapshot(ringBuffer.TrackedPlayer);

            AddMarker(new EventMarker { Type = "speedrun_finish" });

            int endFrame = ringBuffer.HeadFrame;
            var replayFile = ringBuffer.Extract(pinFrame, endFrame);
            ringBuffer.Unpin(pinFrame);
            active = false;

            if (replayFile == null)
            {
                Plugin.Log.LogWarning("SpeedrunBridge: extract produced empty replay");
                wallclock.Stop();
                markers.Clear();
                return;
            }

            // Inject event markers
            replayFile.Events.AddRange(markers);
            replayFile.SpeedrunProfile = profileName;
            replayFile.SpeedrunSplits = splitIds;

            // Save
            string path = GetSpeedrunSavePath();
            ReplayFormat.Write(replayFile, path);

            float durationSecs = (float)wallclock.Elapsed.TotalSeconds;
            wallclock.Stop();
            markers.Clear();

            Plugin.Log.LogInfo($"SpeedrunBridge: speedrun replay saved to {path} ({durationSecs:F1}s, {markers.Count} events)");
            Plugin.Instance?.ShowToast($"Speedrun replay saved ({durationSecs:F1}s)");
        }

        private static void OnTimerReset()
        {
            if (!active) return;
            CleanupPin();
            Plugin.Log.LogInfo("SpeedrunBridge: recording cancelled (timer reset)");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void OnSplitRecorded(IGTAPSpeedrun.SplitRecordedInfo info)
        {
            if (!active) return;

            var ringBuffer = Plugin.Instance?.ringBuffer;
            ringBuffer?.CaptureSnapshot(ringBuffer.TrackedPlayer);

            AddMarker(new EventMarker
            {
                Type = "split",
                UpgradeId = info.Split.Id,
                Label = info.Split.Label,
                SplitTime = info.Split.Time,
                SegmentTime = info.Split.SegmentTime,
            });
        }

        // --- Game event handlers (log markers for ALL events, not just tracked splits) ---

        private static void OnGameStarted(GameStartInfo info)
        {
            if (!active) return;
            AddMarker(new EventMarker
            {
                Type = "game_start",
                X = info.SpawnPosition.x,
                Y = info.SpawnPosition.y,
                IsCleanStart = info.IsCleanStart,
            });
        }

        private static void OnCourseStarted(int courseNumber, courseScript course)
        {
            if (!active) return;
            AddMarker(new EventMarker
            {
                Type = "course_start",
                CourseNumber = courseNumber,
            });
        }

        private static void OnCourseStopped(int courseNumber, bool completed, float courseTime)
        {
            if (!active) return;

            var ringBuffer = Plugin.Instance?.ringBuffer;
            if (completed)
                ringBuffer?.CaptureSnapshot(ringBuffer.TrackedPlayer);

            AddMarker(new EventMarker
            {
                Type = "course_end",
                CourseNumber = courseNumber,
                Completed = completed,
                CourseTime = courseTime,
            });
        }

        private static void OnUpgradeBought(UpgradeInfo info)
        {
            if (!active) return;

            var ringBuffer = Plugin.Instance?.ringBuffer;
            ringBuffer?.CaptureSnapshot(ringBuffer.TrackedPlayer);

            AddMarker(new EventMarker
            {
                Type = "upgrade",
                UpgradeId = info.Id,
                Label = info.Label,
                Category = info.Category,
            });
        }

        private static void OnCheckpointHit(Vector2 position)
        {
            if (!active) return;
            AddMarker(new EventMarker
            {
                Type = "checkpoint",
                X = position.x,
                Y = position.y,
            });
        }

        private static void OnPlayerDied(Vector2 position)
        {
            if (!active) return;
            AddMarker(new EventMarker
            {
                Type = "death",
                X = position.x,
                Y = position.y,
            });
        }

        private static void OnPlayerRespawned(Vector2 position)
        {
            if (!active) return;
            AddMarker(new EventMarker
            {
                Type = "respawn",
                X = position.x,
                Y = position.y,
            });
        }

        // --- Helpers ---

        private static void AddMarker(EventMarker marker)
        {
            var ringBuffer = Plugin.Instance?.ringBuffer;
            marker.Frame = ringBuffer != null ? ringBuffer.HeadFrame - pinFrame : 0;
            marker.WallclockTime = (float)wallclock.Elapsed.TotalSeconds;
            markers.Add(marker);
        }

        private static void CleanupPin()
        {
            var ringBuffer = Plugin.Instance?.ringBuffer;
            if (ringBuffer != null && ringBuffer.IsRunning)
                ringBuffer.Unpin(pinFrame);
            active = false;
            wallclock.Stop();
            markers.Clear();
        }

        private static string GetSpeedrunSavePath()
        {
            string baseDir = ClipManager.GetReplayDirectory();
            string dir = Path.Combine(baseDir, "speedrun");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string safeName = profileName ?? "auto";
            // Sanitize profile name for filename
            foreach (char c in Path.GetInvalidFileNameChars())
                safeName = safeName.Replace(c, '_');

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            return Path.Combine(dir, $"speedrun_{safeName}_{timestamp}.txt");
        }
    }
}
