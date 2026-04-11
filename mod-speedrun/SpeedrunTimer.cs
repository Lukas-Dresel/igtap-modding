using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace IGTAPSpeedrun
{
    public struct Split
    {
        public string Id;
        public string Label;
        public float Time;        // absolute time since timer start
        public float SegmentTime; // time since previous split (0 for first)
    }

    public struct TimerStartedInfo
    {
        public SpeedrunProfile Profile; // null for auto-detect
        public string ProfileName;      // null for auto-detect
    }

    public struct TimerStoppedInfo
    {
        public float WallclockTime;
        public int FramesSinceStart;
        public int DeathCount;
        public List<Split> Splits;
        public SpeedrunProfile Profile;
    }

    public struct SplitRecordedInfo
    {
        public Split Split;
        public float WallclockTime;
        public int FramesSinceStart;
        public bool EndsRun;
    }

    public class SpeedrunTimer
    {
        public enum TimerState { Idle, Running, Finished }

        public TimerState State { get; private set; } = TimerState.Idle;
        public int DeathCount { get; private set; }
        public int FrameCount { get; internal set; }
        public List<Split> Splits { get; } = new List<Split>();
        public PBRecord CurrentPB { get; private set; }
        public SpeedrunProfile ActiveProfile { get; private set; }

        public bool IsProfileMode => ActiveProfile != null;

        // --- Lifecycle events ---
        public event Action<TimerStartedInfo> OnTimerStarted;
        public event Action<TimerStoppedInfo> OnTimerStopped;
        public event Action OnTimerReset;
        public event Action<SplitRecordedInfo> OnSplitRecorded;

        private readonly Stopwatch wallClock = new Stopwatch();
        private readonly HashSet<string> completedIds = new HashSet<string>();
        private float finalTime;
        private string profileName; // null for auto-detect

        public float CurrentTime =>
            State == TimerState.Finished ? finalTime : (float)wallClock.Elapsed.TotalSeconds;

        public void StartTimer(SpeedrunProfile profile = null)
        {
            ActiveProfile = profile;
            profileName = profile?.name;
            State = TimerState.Running;
            wallClock.Restart();
            Splits.Clear();
            completedIds.Clear();
            DeathCount = 0;
            FrameCount = 0;
            CurrentPB = PBData.Load(profileName);
            Plugin.Log.LogInfo(profile != null
                ? $"Speedrun timer started (profile: {profile.name})"
                : "Speedrun timer started (auto-detect)");

            OnTimerStarted?.Invoke(new TimerStartedInfo
            {
                Profile = profile,
                ProfileName = profileName,
            });
        }

        public void StopTimer()
        {
            if (State != TimerState.Running) return;

            finalTime = (float)wallClock.Elapsed.TotalSeconds;
            wallClock.Stop();
            State = TimerState.Finished;

            SaveBests(finalTime);

            OnTimerStopped?.Invoke(new TimerStoppedInfo
            {
                WallclockTime = finalTime,
                FramesSinceStart = FrameCount,
                DeathCount = DeathCount,
                Splits = new List<Split>(Splits),
                Profile = ActiveProfile,
            });
        }

        public void ResetTimer()
        {
            wallClock.Reset();
            Splits.Clear();
            completedIds.Clear();
            DeathCount = 0;
            FrameCount = 0;
            finalTime = 0f;
            State = TimerState.Idle;
            ActiveProfile = null;
            profileName = null;

            OnTimerReset?.Invoke();
        }

        public void ReloadPB()
        {
            CurrentPB = PBData.Load(profileName);
        }

        /// <summary>
        /// Try to auto-start the timer in response to a game event. Returns true if the timer started.
        /// Only fires if the timer is Idle and the event ID is configured as a start trigger
        /// (via profile.startsRun in profile mode, or AutoDetectStartSplits config in auto-detect).
        /// </summary>
        public bool TryAutoStart(string eventId, SpeedrunProfile profile)
        {
            if (State != TimerState.Idle) return false;
            if (!DoesStartRunFor(eventId, profile)) return false;
            StartTimer(profile);
            return true;
        }

        private static bool DoesStartRunFor(string eventId, SpeedrunProfile profile)
        {
            if (profile != null)
            {
                foreach (var def in profile.splits)
                    if (def.id == eventId) return def.startsRun;
                return false;
            }
            // Auto-detect mode: check comma-separated set
            var startSplits = Plugin.AutoDetectStartSplits.Value.Split(',');
            foreach (var s in startSplits)
                if (s.Trim() == eventId) return true;
            return false;
        }

        public void OnCourseComplete(int courseNumber)
        {
            if (!Plugin.Enabled.Value) return;

            string id = $"course{courseNumber}";
            string label = $"Course {courseNumber}";

            if (!IsSplitTracked(id)) return;

            bool endsRun = DoesEndRun(id);
            RecordSplit(id, label, endsRun);
            if (endsRun) StopTimer();
        }

        public void OnUpgrade(string id, string label)
        {
            if (!Plugin.Enabled.Value) return;
            if (!IsSplitTracked(id)) return;

            bool endsRun = DoesEndRun(id);
            RecordSplit(id, label, endsRun);
            if (endsRun) StopTimer();
        }

        public void OnDeath()
        {
            if (State == TimerState.Running)
                DeathCount++;
        }

        public float? GetDelta(Split current)
        {
            if (CurrentPB == null) return null;

            if (IsProfileMode)
                return GetSegmentDelta(current);

            foreach (var pbSplit in CurrentPB.splits)
            {
                if (pbSplit.id == current.Id)
                    return current.Time - pbSplit.time;
            }
            return null;
        }

        public float? GetSegmentDelta(Split current)
        {
            if (CurrentPB == null) return null;

            string fromId = "";
            int idx = Splits.IndexOf(current);
            if (idx > 0) fromId = Splits[idx - 1].Id;

            foreach (var seg in CurrentPB.segments)
            {
                if (seg.fromId == fromId && seg.toId == current.Id)
                    return current.SegmentTime - seg.bestTime;
            }
            return null;
        }

        public float? GetTotalDelta()
        {
            if (CurrentPB == null) return null;
            return CurrentTime - EstimatePBTime();
        }

        /// <summary>
        /// Get the display time for a split row. In profile mode: segment time. In auto-detect: absolute time.
        /// </summary>
        public float GetDisplayTime(Split split)
        {
            return IsProfileMode ? split.SegmentTime : split.Time;
        }

        private float EstimatePBTime()
        {
            if (CurrentPB == null || CurrentPB.splits.Count == 0)
                return CurrentPB?.totalTime ?? 0f;

            if (Splits.Count == 0)
                return 0f;

            var lastSplit = Splits[Splits.Count - 1];
            foreach (var pbSplit in CurrentPB.splits)
            {
                if (pbSplit.id == lastSplit.Id)
                    return pbSplit.time;
            }
            return 0f;
        }

        private bool IsSplitTracked(string id)
        {
            if (IsProfileMode)
            {
                foreach (var def in ActiveProfile.splits)
                {
                    if (def.id == id) return true;
                }
                return false;
            }
            return Plugin.IsSplitEnabled(id);
        }

        private bool DoesEndRun(string id)
        {
            if (IsProfileMode)
            {
                foreach (var def in ActiveProfile.splits)
                {
                    if (def.id == id) return def.endsRun;
                }
                return false;
            }
            // Auto-detect mode: check comma-separated set
            var endSplits = Plugin.AutoDetectEndSplit.Value.Split(',');
            foreach (var s in endSplits)
            {
                if (s.Trim() == id) return true;
            }
            return false;
        }

        private void RecordSplit(string id, string label, bool endsRun)
        {
            if (completedIds.Contains(id)) return;
            if (State != TimerState.Running) return;

            float time = (float)wallClock.Elapsed.TotalSeconds;
            float prevTime = Splits.Count > 0 ? Splits[Splits.Count - 1].Time : 0f;
            float segmentTime = time - prevTime;

            completedIds.Add(id);
            var split = new Split
            {
                Id = id,
                Label = label,
                Time = time,
                SegmentTime = segmentTime
            };
            Splits.Add(split);

            Plugin.Log.LogInfo($"Split: {label} at {time:F2}s (segment: {segmentTime:F2}s)");

            SaveBests(null);

            OnSplitRecorded?.Invoke(new SplitRecordedInfo
            {
                Split = split,
                WallclockTime = time,
                FramesSinceStart = FrameCount,
                EndsRun = endsRun,
            });
        }

        private void SaveBests(float? finishedTotalTime)
        {
            var pb = CurrentPB;
            bool changed = false;

            if (pb == null)
            {
                pb = new PBRecord
                {
                    totalTime = float.MaxValue,
                    deaths = DeathCount,
                    splits = new List<PBSplit>(),
                    segments = new List<PBSegment>(),
                    timestamp = System.DateTime.Now.ToString("o")
                };
                changed = true;
            }

            if (pb.segments == null)
                pb.segments = new List<PBSegment>();

            // Update best total time
            if (finishedTotalTime.HasValue && finishedTotalTime.Value < pb.totalTime)
            {
                pb.totalTime = finishedTotalTime.Value;
                pb.deaths = DeathCount;
                pb.timestamp = System.DateTime.Now.ToString("o");
                changed = true;
                Plugin.Log.LogInfo($"New PB total! {finishedTotalTime.Value:F2}s");
            }

            // Update each split individually if it improved
            foreach (var split in Splits)
            {
                bool found = false;
                for (int i = 0; i < pb.splits.Count; i++)
                {
                    if (pb.splits[i].id == split.Id)
                    {
                        found = true;
                        if (split.Time < pb.splits[i].time)
                        {
                            pb.splits[i] = new PBSplit { id = split.Id, label = split.Label, time = split.Time };
                            changed = true;
                            Plugin.Log.LogInfo($"New best split: {split.Label} {split.Time:F2}s");
                        }
                        break;
                    }
                }
                if (!found)
                {
                    pb.splits.Add(new PBSplit { id = split.Id, label = split.Label, time = split.Time });
                    changed = true;
                }
            }

            // Update segment bests
            for (int si = 0; si < Splits.Count; si++)
            {
                var split = Splits[si];
                string fromId = si > 0 ? Splits[si - 1].Id : "";

                bool segFound = false;
                for (int j = 0; j < pb.segments.Count; j++)
                {
                    if (pb.segments[j].fromId == fromId && pb.segments[j].toId == split.Id)
                    {
                        segFound = true;
                        if (split.SegmentTime < pb.segments[j].bestTime)
                        {
                            pb.segments[j] = new PBSegment { fromId = fromId, toId = split.Id, bestTime = split.SegmentTime };
                            changed = true;
                            Plugin.Log.LogInfo($"New best segment: {fromId}->{split.Id} {split.SegmentTime:F2}s");
                        }
                        break;
                    }
                }
                if (!segFound)
                {
                    pb.segments.Add(new PBSegment { fromId = fromId, toId = split.Id, bestTime = split.SegmentTime });
                    changed = true;
                }
            }

            if (changed)
            {
                PBData.Save(pb, profileName);
                CurrentPB = pb;
            }
        }
    }
}
