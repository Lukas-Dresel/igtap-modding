using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace IGTAPReplay
{
    /// <summary>
    /// Manages replay clips: course auto-recordings (with delayed saves),
    /// manual start/stop recordings, and instant highlights.
    /// All clips are frame ranges extracted from the InputRingBuffer.
    /// </summary>
    public class ClipManager
    {
        private static ManualLogSource Log => Plugin.Log;

        private readonly InputRingBuffer buffer;
        private readonly List<PendingCourseClip> pendingCourseClips = new List<PendingCourseClip>();
        private ManualClip activeManualClip;

        public bool IsManualRecording => activeManualClip != null;
        public bool HasActiveRecording => activeManualClip != null || pendingCourseClips.Count > 0;
        public int ManualRecordingFrames => activeManualClip != null ? buffer.HeadFrame - activeManualClip.StartFrame : 0;

        public ClipManager(InputRingBuffer buffer)
        {
            this.buffer = buffer;
        }

        // --- Course clips ---

        public void OnCourseStart(courseScript course)
        {
            if (!buffer.IsRunning) return;
            if (!Plugin.AutoRecordCourses.Value) return;

            // Deduplicate: if there's already any clip for this course (active or pending save), ignore
            for (int i = 0; i < pendingCourseClips.Count; i++)
            {
                if (pendingCourseClips[i].CourseNumber == course.courseNumber)
                {
                    Plugin.DbgLog($"Course {course.courseNumber} startTracking ignored — clip already exists");
                    return;
                }
            }

            // Force a snapshot at the course start boundary
            buffer.CaptureSnapshot(buffer.TrackedPlayer);

            // Extend the recording start back to the nearest checkpoint before
            // the pre-padding point, so the clip always begins at an exact snapshot.
            int prePaddingFrames = Plugin.CoursePrePaddingSeconds.Value * buffer.Timestep;
            int desiredStart = Mathf.Max(buffer.HeadFrame - prePaddingFrames, buffer.TailFrame);
            int startFrame = buffer.FindCheckpointAtOrBefore(desiredStart);
            buffer.Pin(startFrame);

            var clip = new PendingCourseClip
            {
                CourseNumber = course.courseNumber,
                StartFrame = startFrame,
                EndFrame = -1,
                SaveAtFrame = -1,
                Completed = false,
            };
            pendingCourseClips.Add(clip);

            Log.LogInfo($"Course {course.courseNumber} started — recording from frame {startFrame}");
            Plugin.Instance?.ShowToast($"Course {course.courseNumber} recording started");
        }

        public void OnCourseEnd(courseScript course, bool completed)
        {
            if (!buffer.IsRunning) return;

            // Find the matching pending clip
            for (int i = pendingCourseClips.Count - 1; i >= 0; i--)
            {
                var clip = pendingCourseClips[i];
                if (clip.CourseNumber == course.courseNumber && clip.EndFrame < 0)
                {
                    if (!completed)
                    {
                        // Player left the course through a boundary gate — game resets its timer.
                        // Discard the clip to match.
                        buffer.Unpin(clip.StartFrame);
                        pendingCourseClips.RemoveAt(i);
                        Plugin.DbgLog($"Course {course.courseNumber} tracking stopped (not completed) — discarding clip");
                        return;
                    }

                    // Real completion — save the clip
                    buffer.CaptureSnapshot(buffer.TrackedPlayer);

                    int postPaddingFrames = Plugin.CoursePostPaddingSeconds.Value * buffer.Timestep;
                    clip.EndFrame = buffer.HeadFrame;
                    clip.SaveAtFrame = buffer.HeadFrame + postPaddingFrames;
                    clip.Completed = true;
                    pendingCourseClips[i] = clip;

                    Log.LogInfo($"Course {course.courseNumber} completed — will save after {Plugin.CoursePostPaddingSeconds.Value}s post-padding");
                    return;
                }
            }

            // No clip found — likely a duplicate event from a second courseScript instance
            Plugin.DbgLog($"Course {course.courseNumber} stopTracking but no pending clip (duplicate event)");
        }

        // --- Manual recording ---

        public void StartManualRecording()
        {
            if (!buffer.IsRunning) return;
            if (activeManualClip != null) return; // already recording

            int startFrame = buffer.HeadFrame;
            buffer.CaptureSnapshot(buffer.TrackedPlayer);
            buffer.Pin(startFrame);

            activeManualClip = new ManualClip { StartFrame = startFrame };

            Log.LogInfo($"Manual recording started at frame {startFrame}");
            Plugin.Instance?.ShowToast("Recording started");
        }

        public string StopManualRecording()
        {
            if (activeManualClip == null) return null;

            buffer.CaptureSnapshot(buffer.TrackedPlayer);

            int startFrame = activeManualClip.StartFrame;
            int endFrame = buffer.HeadFrame;

            var replayFile = buffer.Extract(startFrame, endFrame);
            buffer.Unpin(startFrame);
            activeManualClip = null;

            if (replayFile == null)
            {
                Log.LogWarning("Manual recording produced empty replay");
                return null;
            }

            string path = GetSavePath("recording");
            ReplayFormat.Write(replayFile, path);

            int durationFrames = endFrame - startFrame;
            float durationSecs = (float)durationFrames / (buffer.Timestep > 0 ? buffer.Timestep : 50);
            Log.LogInfo($"Manual recording saved: {path} ({durationFrames} frames, {durationSecs:F1}s)");
            Plugin.Instance?.ShowToast($"Recording saved ({durationSecs:F1}s)");

            return path;
        }

        // --- Instant highlight ---

        public string SaveHighlight()
        {
            if (!buffer.IsRunning) return null;

            int highlightFrames = Plugin.HighlightSeconds.Value * buffer.Timestep;
            int startFrame = Mathf.Max(buffer.HeadFrame - highlightFrames, buffer.TailFrame);
            int endFrame = buffer.HeadFrame;

            var replayFile = buffer.Extract(startFrame, endFrame);
            if (replayFile == null)
            {
                Log.LogWarning("Highlight save produced empty replay");
                return null;
            }

            string path = GetSavePath("highlight");
            ReplayFormat.Write(replayFile, path);

            float durationSecs = (float)(endFrame - startFrame) / (buffer.Timestep > 0 ? buffer.Timestep : 50);
            Log.LogInfo($"Highlight saved: {path} ({durationSecs:F1}s)");
            Plugin.Instance?.ShowToast($"Highlight saved ({durationSecs:F1}s)");

            return path;
        }

        // --- Update (called every frame from Plugin.Update) ---

        public void Update()
        {
            if (!buffer.IsRunning) return;

            // Check pending course clips for delayed saves
            for (int i = pendingCourseClips.Count - 1; i >= 0; i--)
            {
                var clip = pendingCourseClips[i];

                // Skip clips that haven't ended yet
                if (clip.EndFrame < 0) continue;

                // Check if post-padding has elapsed
                if (buffer.HeadFrame >= clip.SaveAtFrame)
                {
                    SaveCourseClip(clip);
                    pendingCourseClips.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Flush all pending clips immediately (e.g., player destroyed, scene change).
        /// </summary>
        public void FlushPending()
        {
            // Save any course clips that have ended (even without full post-padding)
            for (int i = pendingCourseClips.Count - 1; i >= 0; i--)
            {
                var clip = pendingCourseClips[i];
                if (clip.EndFrame >= 0)
                {
                    SaveCourseClip(clip);
                }
                else
                {
                    // Course never ended — just unpin and discard
                    buffer.Unpin(clip.StartFrame);
                    Log.LogInfo($"Discarded unfinished course {clip.CourseNumber} clip");
                }
                pendingCourseClips.RemoveAt(i);
            }

            // Stop manual recording if active
            if (activeManualClip != null)
                StopManualRecording();
        }

        // --- Private helpers ---

        private void SaveCourseClip(PendingCourseClip clip)
        {
            int endFrame = Mathf.Min(clip.SaveAtFrame, buffer.HeadFrame);
            var replayFile = buffer.Extract(clip.StartFrame, endFrame);
            buffer.Unpin(clip.StartFrame);

            if (replayFile == null)
            {
                Log.LogWarning($"Course {clip.CourseNumber} clip produced empty replay");
                return;
            }

            string path = GetCourseSavePath(clip.CourseNumber, "complete");
            ReplayFormat.Write(replayFile, path);

            float durationSecs = (float)(endFrame - clip.StartFrame) / (buffer.Timestep > 0 ? buffer.Timestep : 50);
            Log.LogInfo($"Course {clip.CourseNumber} saved: {path} ({durationSecs:F1}s)");
            Plugin.Instance?.ShowToast($"Course {clip.CourseNumber} replay saved ({durationSecs:F1}s)");
        }

        private static string GetSavePath(string prefix)
        {
            string dir = GetReplayDirectory();
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            return Path.Combine(dir, $"{prefix}_{timestamp}.txt");
        }

        private static string GetCourseSavePath(int courseNumber, string prefix)
        {
            string dir = Path.Combine(GetReplayDirectory(), $"course{courseNumber}");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            return Path.Combine(dir, $"{prefix}_{timestamp}.txt");
        }

        internal static string GetReplayDirectory()
        {
            var dirPath = Plugin.ReplayDirectory.Value;
            if (Path.IsPathRooted(dirPath))
                return dirPath;
            return Path.Combine(Paths.ConfigPath, dirPath);
        }

        // --- Inner types ---

        private struct PendingCourseClip
        {
            public int CourseNumber;
            public int StartFrame;
            public int EndFrame;      // -1 while course is still active
            public int SaveAtFrame;   // endFrame + postPadding (delayed save trigger)
            public bool Completed;    // true if player reached the actual end gate
        }

        private class ManualClip
        {
            public int StartFrame;
        }
    }
}
