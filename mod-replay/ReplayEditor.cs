using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IGTAPReplay
{
    /// <summary>
    /// Handles pause/frame-step during recording, timeline bar during replay,
    /// and ghost markers during both recording and replay.
    /// </summary>
    public class ReplayEditor : MonoBehaviour
    {
        private bool paused;
        private float savedTimeScale = 1f;
        private Movement player;

        // Mode
        private enum EditorMode { None, Recording, Replay }
        private EditorMode editorMode = EditorMode.None;

        // Replay data (for timeline + precomputed ghosts)
        private ReplayFile replayFile;

        /// <summary>Path the replay was originally loaded from. Set by Plugin.PlayFile
        /// before BeginReplay. Needed by edit-session Save-As.</summary>
        public string OriginalLoadPath;

        // --- TAS edit mode ---
        private ReplayEditSession editSession;
        private bool editMode;
        private EditScope editScope = EditScope.FromHere;
        private bool pickerOpen;
        private string pickerFilter = "";
        private Vector2 pickerScroll;
        private bool captureBoxFocused;
        private string captureBoxFeedback; // shown briefly after a capture
        private float captureBoxFeedbackEndTime;
        private const int CaptureBoxControlId = 0x7A5E; // arbitrary stable ID
        private const float EditPanelHeight = 170f;

        // Ghost markers (live, for recording)
        private readonly List<GhostMarker> liveMarkers = new List<GhostMarker>();
        private readonly List<MarkerLabel> liveLabels = new List<MarkerLabel>();
        private const float LiveMarkerLifetime = 5f;
        private const float MarkerAlpha = 0.35f;
        private HashSet<string> prevMarkerKeys = new HashSet<string>();

        // Precomputed ghost data (for replay)
        private List<ReplayGhost> replayGhosts = new List<ReplayGhost>();

        // Active replay ghost GameObjects
        private readonly List<GhostMarker> activeReplayMarkers = new List<GhostMarker>();

        private struct GhostMarker
        {
            public GameObject Go;
            public SpriteRenderer Sr;
            public float SpawnTime; // used only for live markers
            public int GhostIndex;  // index into replayGhosts, -1 for live
        }

        private struct MarkerLabel
        {
            public Vector3 WorldPos;
            public string Text;
            public float SpawnTime;
            public bool IsPress; // true = blue (pressed), false = orange (released)
        }

        /// <summary>Precomputed ghost from replay file spans.</summary>
        private struct ReplayGhost
        {
            public int Frame;
            public int SpanIndex;
            public string PressLabel;   // "▲Space ▲D" or null
            public string ReleaseLabel; // "▼LMB" or null
            public bool IsPress;        // had keys added
        }

        // Timeline
        private const float TimelineHeight = 48f;
        private const float TransportHeight = 28f;
        private const float TimelinePadding = 10f;
        private const float TimelineWindowSecondsHalf = 5f;
        private static readonly float[] SpeedOptions = { 0.1f, 0.25f, 0.5f, 1f, 2f, 4f };
        private Texture2D timelineBgTex;
        private Texture2D timelineWhiteTex;
        private GUIStyle timelineLabelStyle;
        private bool timelineStylesInit;
        private readonly Dictionary<string, Color> comboColorCache = new Dictionary<string, Color>();

        // Stepping
        private int pendingStepTarget;

        // Scrubbing
        private bool isScrubbing;

        // Tooltip
        private string tooltipText;
        private Vector2 tooltipPos;

        public bool IsPaused => paused;

        /// <summary>Pause from external call (e.g. playback finished).</summary>
        public void PausePlayback()
        {
            Plugin.DbgLog($"PausePlayback called, paused={paused}");
            if (!paused) Pause();
        }

        // --- Begin/End for recording ---

        public void Begin(Movement targetPlayer)
        {
            player = targetPlayer;
            editorMode = EditorMode.Recording;
            paused = false;
            prevMarkerKeys.Clear();
            ClearLiveMarkers();
        }

        // --- Begin/End for replay ---

        public void BeginReplay(Movement targetPlayer, ReplayFile file)
        {
            player = targetPlayer;
            replayFile = file;
            editorMode = EditorMode.Replay;
            paused = false;
            prevMarkerKeys.Clear();
            ClearLiveMarkers();
            ClearReplayMarkers();
            PrecomputeReplayGhosts();
        }

        public void End()
        {
            if (paused)
                Resume();
            ClearLiveMarkers();
            ClearReplayMarkers();
            editMode = false;
            editSession = null;
            pickerOpen = false;
            player = null;
            replayFile = null;
            OriginalLoadPath = null;
            editorMode = EditorMode.None;
        }

        // ===========================
        // TAS EDIT MODE LIFECYCLE
        // ===========================

        public bool IsEditMode => editMode;

        private void BeginEditMode()
        {
            if (editorMode != EditorMode.Replay || player == null || replayFile == null)
                return;
            if (string.IsNullOrEmpty(OriginalLoadPath))
            {
                Plugin.Instance?.ShowToast("Edit mode needs a loaded replay path");
                return;
            }

            // Ensure we're paused so the user can see what they're editing.
            if (!paused) Pause();

            editSession = new ReplayEditSession(replayFile, OriginalLoadPath, player);
            editMode = true;
            pickerOpen = false;
            captureBoxFeedback = null;
            Plugin.Instance?.ShowToast("Edit mode ON");
            Plugin.DbgLog($"BeginEditMode path={OriginalLoadPath}");
        }

        private void EndEditMode()
        {
            if (!editMode) return;
            editMode = false;
            editSession = null;
            pickerOpen = false;
            captureBoxFocused = false;
            Plugin.Instance?.ShowToast("Edit mode OFF");
            Plugin.DbgLog("EndEditMode");
        }

        /// <summary>
        /// Called from ReplayPlayback when a reverify pass finishes. Clears the
        /// dirty flag on the edit session and allows the editor to relayout.
        /// </summary>
        public void OnReverifyComplete()
        {
            editSession?.MarkCleanAfterReverify();
            PrecomputeReplayGhosts();
            Plugin.Instance?.ShowToast("Re-verify complete");
            Plugin.DbgLog("Editor.OnReverifyComplete");
        }

        // --- Update ---

        private void Update()
        {
            if (player == null || editorMode == EditorMode.None) return;

            var playback = FindAnyObjectByType<ReplayPlayback>();

            // While reverifying, lock out ALL user controls (hotkeys + step).
            // The reverify pass is uninterruptible by design.
            if (playback != null && playback.IsReverifying)
            {
                // Ensure our internal paused state matches playback's live state:
                // during reverify, playback is running, so we're not paused.
                if (paused) { paused = false; }
                return;
            }

            if (editorMode == EditorMode.Replay)
            {
                if (playback != null)
                {
                    if (pendingStepTarget > 0)
                    {
                        playback.RequestUnpause(pendingStepTarget);
                        paused = false;
                        Plugin.DbgLog($"Editor.Update step REQUESTED target={pendingStepTarget}");
                        pendingStepTarget = 0;
                    }
                    else if (playback.PauseOnFrame > 0)
                    {
                    }
                    else if (playback.IsPaused && !playback.IsSeeking && !paused)
                    {
                        paused = true;
                        Time.timeScale = 0f;
                        Plugin.DbgLog($"Editor.Update synced pause from playback fc={playback.FrameCount}");
                    }
                }
            }

            // Editor hotkeys (Backspace=pause, arrows=step).
            // Uses old Input API because during replay, real InputSystem devices are
            // disabled. Wrapped in try-catch because the game may have old Input disabled
            // entirely (Input System package only in Player Settings).
            try
            {
                if (Input.GetKeyDown(KeyCode.Backspace))
                {
                    Plugin.DbgLog($"Backspace pressed, paused={paused}");
                    if (paused) Resume();
                    else Pause();
                }

                if (paused)
                    HandleStepping(playback);
            }
            catch (System.InvalidOperationException)
            {
                // Old Input Manager is disabled — hotkeys won't work, but don't block cleanup
            }

            // Live marker cleanup (recording)
            if (editorMode == EditorMode.Recording)
                CleanupLiveMarkers();

            // Replay ghost management
            if (editorMode == EditorMode.Replay && Plugin.ReplayGhostsEnabled.Value)
                UpdateReplayGhosts();
        }

        // --- Pause/Step ---

        private void Pause()
        {
            if (paused) return;

            var playback = FindAnyObjectByType<ReplayPlayback>();
            if (playback != null)
            {
                // Don't freeze anything now — just tell playback to stop after next frame.
                // PostMovementUpdate will set paused=true and timeScale=0 cleanly.
                playback.PauseOnFrame = playback.FrameCount + 1;
                Plugin.DbgLog($"Editor.Pause: PauseOnFrame={playback.PauseOnFrame}");
            }

            // Mark editor as paused immediately so buttons show correct state
            paused = true;
            savedTimeScale = Time.timeScale;
            Plugin.Instance.ShowToast("Paused");
        }

        private void Resume()
        {
            if (!paused) return;
            paused = false;

            var playback = FindAnyObjectByType<ReplayPlayback>();
            if (playback != null)
            {
                // Request unpause at the Movement prefix lifecycle point.
                // PauseOnFrame=0 means don't auto-pause (just resume normal play).
                playback.RequestUnpause(0);
            }

            Plugin.DbgLog("Editor.Resume requested");
            Plugin.Instance.ShowToast("Resumed");
        }

        private void HandleStepping(ReplayPlayback playback)
        {
            bool stepForward = Input.GetKeyDown(KeyCode.RightArrow);
            bool stepBackward = Input.GetKeyDown(KeyCode.LeftArrow);
            bool stepSecFwd = false;
            bool stepSecBack = false;

            float scroll = Input.mouseScrollDelta.y;
            if (scroll > 0.5f) stepForward = true;
            else if (scroll < -0.5f) stepBackward = true;

            // Shift+arrow for 1 second steps
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                if (stepForward) { stepForward = false; stepSecFwd = true; }
                if (stepBackward) { stepBackward = false; stepSecBack = true; }
            }

            int timestep = replayFile?.Timestep ?? 50;
            int fc = playback != null ? playback.FrameCount : -1;

            if (stepForward) { Plugin.DbgLog($"KEY: step fwd +1 frame (fc={fc})"); StepFrames(1); }
            else if (stepSecFwd) { Plugin.DbgLog($"KEY: step fwd +1s/{timestep} frames (fc={fc})"); StepFrames(timestep); }
            else if (stepBackward) { Plugin.DbgLog($"KEY: step back -1 frame (fc={fc})"); StepFrames(-1); }
            else if (stepSecBack) { Plugin.DbgLog($"KEY: step back -1s/{timestep} frames (fc={fc})"); StepFrames(-timestep); }
        }

        // ===========================
        // LIVE GHOSTS (recording)
        // ===========================

        /// <summary>Called by the recorder when input changes during recording.</summary>
        public void OnInputChanged(HashSet<string> currentKeys, Movement p)
        {
            if (p == null) return;

            var added = new HashSet<string>(currentKeys);
            added.ExceptWith(prevMarkerKeys);
            var removed = new HashSet<string>(prevMarkerKeys);
            removed.ExceptWith(currentKeys);

            if (added.Count == 0 && removed.Count == 0)
            {
                prevMarkerKeys = new HashSet<string>(currentKeys);
                return;
            }

            // Spawn ghost at player position
            SpawnLiveGhost(p, added, removed);
            prevMarkerKeys = new HashSet<string>(currentKeys);
        }

        private void SpawnLiveGhost(Movement p, HashSet<string> added, HashSet<string> removed)
        {
            bool hasAdded = added.Count > 0;
            var go = CreateGhostObject(p, hasAdded);
            if (go == null) return;

            liveMarkers.Add(new GhostMarker
            {
                Go = go,
                Sr = go.GetComponent<SpriteRenderer>(),
                SpawnTime = Time.unscaledTime,
                GhostIndex = -1,
            });

            // Pressed keys: head level (above), blue, ▲ prefix
            if (added.Count > 0)
            {
                string pressText = string.Join(" ", added.Select(k => "\u25B2" + KeyNames.ToShortName(k)));
                liveLabels.Add(new MarkerLabel
                {
                    WorldPos = p.transform.position + Vector3.up * 45f,
                    Text = pressText,
                    SpawnTime = Time.unscaledTime,
                    IsPress = true,
                });
            }

            // Released keys: foot level (below), orange, ▼ prefix
            if (removed.Count > 0)
            {
                string releaseText = string.Join(" ", removed.Select(k => "\u25BC" + KeyNames.ToShortName(k)));
                liveLabels.Add(new MarkerLabel
                {
                    WorldPos = p.transform.position + Vector3.down * 35f,
                    Text = releaseText,
                    SpawnTime = Time.unscaledTime,
                    IsPress = false,
                });
            }
        }

        private void CleanupLiveMarkers()
        {
            float now = Time.unscaledTime;
            for (int i = liveMarkers.Count - 1; i >= 0; i--)
            {
                float age = now - liveMarkers[i].SpawnTime;
                if (age > LiveMarkerLifetime)
                {
                    Destroy(liveMarkers[i].Go);
                    liveMarkers.RemoveAt(i);
                }
                else
                {
                    float alpha = age > LiveMarkerLifetime - 1f
                        ? MarkerAlpha * (LiveMarkerLifetime - age) : MarkerAlpha;
                    var sr = liveMarkers[i].Sr;
                    sr.color = new Color(sr.color.r, sr.color.g, sr.color.b, alpha);
                }
            }
            liveLabels.RemoveAll(l => now - l.SpawnTime > LiveMarkerLifetime);
        }

        private void ClearLiveMarkers()
        {
            foreach (var m in liveMarkers)
                if (m.Go != null) Destroy(m.Go);
            liveMarkers.Clear();
            liveLabels.Clear();
        }

        // ===========================
        // REPLAY GHOSTS (precomputed)
        // ===========================

        private void PrecomputeReplayGhosts()
        {
            replayGhosts.Clear();
            if (replayFile == null) return;

            var prev = new HashSet<string>();
            for (int i = 0; i < replayFile.Spans.Count; i++)
            {
                var span = replayFile.Spans[i];
                var cur = span.Keys ?? new HashSet<string>();

                var added = new HashSet<string>(cur);
                added.ExceptWith(prev);
                var removed = new HashSet<string>(prev);
                removed.ExceptWith(cur);

                if (added.Count > 0 || removed.Count > 0)
                {
                    string pressLabel = added.Count > 0
                        ? string.Join(" ", added.Select(k => "\u25B2" + KeyNames.ToShortName(k)))
                        : null;
                    string releaseLabel = removed.Count > 0
                        ? string.Join(" ", removed.Select(k => "\u25BC" + KeyNames.ToShortName(k)))
                        : null;

                    replayGhosts.Add(new ReplayGhost
                    {
                        Frame = span.Frame,
                        SpanIndex = i,
                        PressLabel = pressLabel,
                        ReleaseLabel = releaseLabel,
                        IsPress = added.Count > 0,
                    });
                }

                prev = new HashSet<string>(cur);
            }
        }

        private void UpdateReplayGhosts()
        {
            if (replayFile == null || player == null) return;

            // Can't place ghosts at exact world positions without the recorded path,
            // but we DO have the player's current position from the actual replay.
            // We'll show ghosts from the replay file based on proximity to current frame.
            // Ghost positions aren't known in advance (no path data), so we show/hide
            // them as the playback passes each span change point — using the player's
            // live position at that moment.

            // This is handled by detecting span transitions in playback.
            // The playback calls us via NotifySpanChange when spanIndex advances.
        }

        /// <summary>
        /// Called by ReplayPlayback each frame with the current frame number and span index.
        /// We use this to spawn/despawn ghosts based on the window around the current frame.
        /// </summary>
        public void OnPlaybackFrame(int currentFrame, Movement p)
        {
            if (!Plugin.ReplayGhostsEnabled.Value || p == null) return;

            bool showAll = Plugin.ReplayGhostsAll.Value;
            int prevCount = Plugin.ReplayGhostsPrev.Value;
            int nextCount = Plugin.ReplayGhostsNext.Value;

            // Find the ghost index closest to (but not past) the current frame
            int currentGhostIdx = -1;
            for (int i = 0; i < replayGhosts.Count; i++)
            {
                if (replayGhosts[i].Frame <= currentFrame)
                    currentGhostIdx = i;
                else
                    break;
            }

            // Determine visible range
            int visStart, visEnd;
            if (showAll)
            {
                visStart = 0;
                visEnd = replayGhosts.Count - 1;
            }
            else
            {
                visStart = Mathf.Max(0, currentGhostIdx - prevCount + 1);
                visEnd = Mathf.Min(replayGhosts.Count - 1, currentGhostIdx + nextCount);
            }

            // For ghosts at or before the current frame, we can use their recorded position
            // (spawned when the player was at that point). For future ghosts, we can't know
            // the position yet — so we only spawn those that the player has already passed.

            // Clean up out-of-range markers
            for (int i = activeReplayMarkers.Count - 1; i >= 0; i--)
            {
                int gi = activeReplayMarkers[i].GhostIndex;
                if (gi < visStart || gi > visEnd || replayGhosts[gi].Frame > currentFrame)
                {
                    Destroy(activeReplayMarkers[i].Go);
                    activeReplayMarkers.RemoveAt(i);
                }
            }

            // Check if we need to spawn new ghosts that we've passed
            var activeSet = new HashSet<int>();
            foreach (var m in activeReplayMarkers) activeSet.Add(m.GhostIndex);

            for (int gi = Mathf.Max(visStart, 0); gi <= visEnd && gi < replayGhosts.Count; gi++)
            {
                if (replayGhosts[gi].Frame > currentFrame) continue; // not reached yet
                if (activeSet.Contains(gi)) continue; // already spawned

                var ghost = replayGhosts[gi];
                var go = CreateGhostObject(p, ghost.IsPress);
                if (go == null) continue;

                // Position: we place it at the player's current position only on the exact
                // frame it was reached. For past ghosts, we'd need recorded positions.
                // Store position when first spawned.
                go.transform.position = p.transform.position;

                // Dim older ghosts
                float ageFrac = (currentGhostIdx >= 0 && !showAll)
                    ? 1f - Mathf.Abs(gi - currentGhostIdx) / (float)Mathf.Max(prevCount + nextCount, 1)
                    : 1f;
                var sr = go.GetComponent<SpriteRenderer>();
                sr.color = new Color(sr.color.r, sr.color.g, sr.color.b, MarkerAlpha * Mathf.Clamp01(ageFrac));

                activeReplayMarkers.Add(new GhostMarker
                {
                    Go = go,
                    Sr = sr,
                    SpawnTime = Time.unscaledTime,
                    GhostIndex = gi,
                });
            }
        }

        /// <summary>
        /// Called once per playback frame when the span just changed.
        /// Records the player's world position for the ghost at this frame.
        /// </summary>
        private readonly Dictionary<int, Vector3> ghostPositions = new Dictionary<int, Vector3>();

        public void RecordGhostPosition(int ghostIndex, Vector3 pos)
        {
            ghostPositions[ghostIndex] = pos;
        }

        private void ClearReplayMarkers()
        {
            foreach (var m in activeReplayMarkers)
                if (m.Go != null) Destroy(m.Go);
            activeReplayMarkers.Clear();
            ghostPositions.Clear();
        }

        // ===========================
        // SHARED GHOST HELPERS
        // ===========================

        private GameObject CreateGhostObject(Movement p, bool isPress)
        {
            var playerSprite = p.transform.Find("Sprite");
            if (playerSprite == null) return null;
            var playerSr = playerSprite.GetComponent<SpriteRenderer>();
            if (playerSr == null) return null;

            var go = new GameObject("ReplayMarker");
            go.transform.position = p.transform.position;
            go.transform.localScale = p.transform.localScale;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = playerSr.sprite;
            sr.sortingLayerName = playerSr.sortingLayerName;
            sr.sortingOrder = playerSr.sortingOrder - 1;

            Color c = isPress
                ? new Color(PressColor.r, PressColor.g, PressColor.b, MarkerAlpha)
                : new Color(ReleaseColor.r, ReleaseColor.g, ReleaseColor.b, MarkerAlpha);
            sr.color = c;
            return go;
        }

        // ===========================
        // OnGUI
        // ===========================

        private void OnGUI()
        {
            // Timeline during replay
            if (editorMode == EditorMode.Replay)
                DrawTimeline();

            // TAS edit panel (only while paused and editing)
            if (editorMode == EditorMode.Replay && editMode && paused)
                DrawEditPanel();

            // Reverify overlay — replaces transport + edit panel while the pass runs.
            var pb = FindAnyObjectByType<ReplayPlayback>();
            if (pb != null && pb.IsReverifying)
                DrawReverifyOverlay(pb);

            // Ghost labels (live, during recording)
            if (liveLabels.Count > 0)
                DrawLiveLabels();

            // Replay ghost labels
            if (editorMode == EditorMode.Replay && Plugin.ReplayGhostsEnabled.Value)
                DrawReplayGhostLabels();

            // Paused banner (suppress during edit mode — the edit panel has its own header)
            if (paused && !editMode)
                DrawPausedBanner();
        }

        // Colorblind-safe: blue = press, orange = release
        private static readonly Color PressColor = new Color(0.2f, 0.5f, 1f);
        private static readonly Color ReleaseColor = new Color(1f, 0.6f, 0.15f);

        private void DrawLiveLabels()
        {
            var cam = Camera.main;
            if (cam == null) return;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };

            float now = Time.unscaledTime;
            foreach (var label in liveLabels)
            {
                float age = now - label.SpawnTime;
                if (age > LiveMarkerLifetime) continue;
                float alpha = age > LiveMarkerLifetime - 1f ? (LiveMarkerLifetime - age) : 1f;

                Vector3 screenPos = cam.WorldToScreenPoint(label.WorldPos);
                if (screenPos.z < 0) continue;
                float guiY = Screen.height - screenPos.y;

                Color fg = label.IsPress ? PressColor : ReleaseColor;

                style.normal.textColor = new Color(0f, 0f, 0f, alpha);
                GUI.Label(new Rect(screenPos.x - 49, guiY - 9, 100, 20), label.Text, style);
                style.normal.textColor = new Color(fg.r, fg.g, fg.b, alpha);
                GUI.Label(new Rect(screenPos.x - 50, guiY - 10, 100, 20), label.Text, style);
            }
        }

        private void DrawReplayGhostLabels()
        {
            var cam = Camera.main;
            if (cam == null) return;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };

            foreach (var m in activeReplayMarkers)
            {
                if (m.Go == null || m.GhostIndex < 0 || m.GhostIndex >= replayGhosts.Count) continue;
                var ghost = replayGhosts[m.GhostIndex];
                float alpha = m.Sr.color.a / MarkerAlpha;

                // Press label at head level (blue)
                if (ghost.PressLabel != null)
                {
                    Vector3 headPos = m.Go.transform.position + Vector3.up * 45f;
                    Vector3 sp = cam.WorldToScreenPoint(headPos);
                    if (sp.z >= 0)
                    {
                        float gy = Screen.height - sp.y;
                        style.normal.textColor = new Color(0f, 0f, 0f, alpha);
                        GUI.Label(new Rect(sp.x - 59, gy - 9, 120, 20), ghost.PressLabel, style);
                        style.normal.textColor = new Color(PressColor.r, PressColor.g, PressColor.b, alpha);
                        GUI.Label(new Rect(sp.x - 60, gy - 10, 120, 20), ghost.PressLabel, style);
                    }
                }

                // Release label at foot level (orange)
                if (ghost.ReleaseLabel != null)
                {
                    Vector3 footPos = m.Go.transform.position + Vector3.down * 35f;
                    Vector3 sp = cam.WorldToScreenPoint(footPos);
                    if (sp.z >= 0)
                    {
                        float gy = Screen.height - sp.y;
                        style.normal.textColor = new Color(0f, 0f, 0f, alpha);
                        GUI.Label(new Rect(sp.x - 59, gy - 9, 120, 20), ghost.ReleaseLabel, style);
                        style.normal.textColor = new Color(ReleaseColor.r, ReleaseColor.g, ReleaseColor.b, alpha);
                        GUI.Label(new Rect(sp.x - 60, gy - 10, 120, 20), ghost.ReleaseLabel, style);
                    }
                }
            }
        }

        private void DrawPausedBanner()
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            float w = 300f, h = 30f;
            float x = (Screen.width - w) / 2f, y = 40f;
            style.normal.textColor = Color.black;
            GUI.Label(new Rect(x + 1, y + 1, w, h), "PAUSED  (scroll/arrows to step)", style);
            style.normal.textColor = Color.yellow;
            GUI.Label(new Rect(x, y, w, h), "PAUSED  (scroll/arrows to step)", style);
        }

        // ===========================
        // TIMELINE
        // ===========================

        private void EnsureTimelineStyles()
        {
            if (timelineStylesInit) return;
            timelineStylesInit = true;

            timelineBgTex = MakeTex(1, 1, new Color(0f, 0f, 0f, 0.7f));
            timelineWhiteTex = MakeTex(1, 1, Color.white);

            timelineLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                wordWrap = false,
            };
            timelineLabelStyle.normal.textColor = Color.white;
            timelineLabelStyle.padding = new RectOffset(1, 1, 0, 0);
        }

        private void DrawTimeline()
        {
            EnsureTimelineStyles();

            // Get replay data — either from playback or from loaded file
            var playback = FindAnyObjectByType<ReplayPlayback>();
            ReplayFile file = replayFile ?? playback?.File;
            if (file == null || file.Spans.Count == 0) return;

            int currentFrame = playback != null ? playback.FrameCount : 0;
            int timestep = file.Timestep > 0 ? file.Timestep : 50;

            // Draw transport controls above the timeline
            DrawTransportControls(timestep);

            // Determine the total frame range of the replay
            int lastSpanFrame = file.Spans[file.Spans.Count - 1].Frame;
            int totalReplayFrames = lastSpanFrame + timestep; // a bit of buffer after last span

            // Determine visible window
            int frameStart, frameEnd;
            bool fullTimeline = Plugin.TimelineFullReplay.Value;

            if (fullTimeline)
            {
                frameStart = 1;
                frameEnd = totalReplayFrames;
            }
            else
            {
                int windowFrames = (int)(TimelineWindowSecondsHalf * timestep);
                frameStart = Mathf.Max(1, currentFrame - windowFrames);
                frameEnd = currentFrame + windowFrames;
            }

            float totalFrames = frameEnd - frameStart;
            if (totalFrames <= 0) return;

            // Timeline rect
            float barX = TimelinePadding;
            float barW = Screen.width - TimelinePadding * 2;
            float barY = Screen.height - TimelineHeight - TimelinePadding;
            float barH = TimelineHeight;

            GUI.DrawTexture(new Rect(barX, barY, barW, barH), timelineBgTex);

            float rowH = barH - 14f;
            float rowY = barY + 2f;

            // Draw spans + hover detection
            var spans = file.Spans;
            Vector2 mouse = Event.current.mousePosition;
            tooltipText = null;

            for (int i = 0; i < spans.Count; i++)
            {
                int spanStart = spans[i].Frame;
                int spanEnd = (i + 1 < spans.Count) ? spans[i + 1].Frame : totalReplayFrames;

                if (spanEnd <= frameStart || spanStart >= frameEnd) continue;

                int drawStart = Mathf.Max(spanStart, frameStart);
                int drawEnd = Mathf.Min(spanEnd, frameEnd);

                float x0 = barX + (drawStart - frameStart) / totalFrames * barW;
                float x1 = barX + (drawEnd - frameStart) / totalFrames * barW;
                float segW = Mathf.Max(x1 - x0, 1f);

                var keys = spans[i].Keys;
                if (keys == null || keys.Count == 0) continue;

                string comboKey = string.Join("+", keys.OrderBy(k => k));
                Color segColor = GetComboColor(comboKey);

                Rect segRect = new Rect(x0, rowY, segW, rowH);

                GUI.color = segColor;
                GUI.DrawTexture(segRect, timelineWhiteTex);
                GUI.color = Color.white;

                if (segW > 20f)
                {
                    string label = AbbreviateKeys(keys);
                    timelineLabelStyle.normal.textColor = GetLabelColor(segColor);
                    GUI.Label(segRect, label, timelineLabelStyle);
                }

                // Hover tooltip
                if (segRect.Contains(mouse))
                {
                    float startSec = (float)spanStart / timestep;
                    float endSec = (float)spanEnd / timestep;
                    string keyList = string.Join(", ", keys.Select(k => KeyNames.ToShortName(k)).OrderBy(k => k));
                    tooltipText = $"Keys: {keyList}\nFrames {spanStart}-{spanEnd}  ({startSec:F2}s - {endSec:F2}s)";
                    tooltipPos = mouse;
                }
            }

            // Playhead
            float playheadX = barX + (currentFrame - frameStart) / totalFrames * barW;
            playheadX = Mathf.Clamp(playheadX, barX, barX + barW);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(playheadX - 1, barY, 2f, barH), timelineWhiteTex);

            // Second tick marks
            var tickStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 8,
                alignment = TextAnchor.UpperCenter,
            };
            tickStyle.normal.textColor = new Color(1f, 1f, 1f, 0.5f);

            float tickInterval = fullTimeline
                ? Mathf.Max(1f, Mathf.Floor(totalFrames / timestep / 10f)) // ~10 ticks max
                : 1f;

            for (float sec = 0; sec * timestep <= totalFrames; sec += tickInterval)
            {
                int tickFrame = frameStart + (int)(sec * timestep);
                if (tickFrame > frameEnd) break;
                float tickX = barX + (tickFrame - frameStart) / totalFrames * barW;
                GUI.DrawTexture(new Rect(tickX, barY + rowH, 1f, 4f), timelineWhiteTex);

                float secFromStart = (float)tickFrame / timestep;
                string tickLabel = fullTimeline
                    ? $"{secFromStart:F0}s"
                    : $"{(tickFrame - currentFrame) / (float)timestep:+0.#;-0.#;0}s";
                GUI.Label(new Rect(tickX - 15, barY + rowH + 2, 30, 12), tickLabel, tickStyle);
            }

            // Frame counter
            var frameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
            };
            frameStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(barX + barW - 80, barY - 14, 80, 14), $"F{currentFrame}", frameStyle);

            // Hover tooltip
            if (!string.IsNullOrEmpty(tooltipText))
                DrawTooltip(tooltipPos, tooltipText);

            // Scrubbing — click and drag on the timeline bar to seek
            HandleScrub(barX, barY, barW, barH, frameStart, totalFrames, playback);

            GUI.color = Color.white;
        }

        private void HandleScrub(float barX, float barY, float barW, float barH,
                                  int frameStart, float totalFrames, ReplayPlayback playback)
        {
            if (playback == null || editorMode != EditorMode.Replay) return;
            if (playback.IsReverifying) return;

            Event e = Event.current;
            Vector2 mousePos = e.mousePosition;
            Rect barRect = new Rect(barX, barY, barW, barH);

            if (e.type == EventType.MouseDown && e.button == 0 && barRect.Contains(mousePos))
            {
                isScrubbing = true;
                if (!paused) Pause();
                Plugin.DbgLog($"SCRUB: started at mouse x={mousePos.x:F0}");
                e.Use();
            }

            if (isScrubbing && (e.type == EventType.MouseDrag || e.type == EventType.MouseDown))
            {
                float frac = Mathf.Clamp01((mousePos.x - barX) / barW);
                int targetFrame = frameStart + Mathf.RoundToInt(frac * totalFrames);
                targetFrame = Mathf.Max(1, targetFrame);

                Plugin.DbgLog($"SCRUB: seek to frame {targetFrame}");
                paused = false;
                Time.timeScale = savedTimeScale;
                playback.SeekToFrame(targetFrame);
                e.Use();
            }

            if (isScrubbing && e.type == EventType.MouseUp && e.button == 0)
            {
                Plugin.DbgLog($"SCRUB: ended at fc={playback.FrameCount}");
                isScrubbing = false;
                e.Use();
            }
        }

        private void DrawTransportControls(int timestep)
        {
            float barX = TimelinePadding;
            float barW = Screen.width - TimelinePadding * 2;
            float barY = Screen.height - TimelineHeight - TimelinePadding - TransportHeight - 2f;
            float btnW = 32f;
            float btnH = TransportHeight;
            float gap = 4f;
            var speedStyle = new GUIStyle(GUI.skin.button) { fontSize = 11 };
            float speedBtnW = 40f;

            float exitBtnW = 44f;
            float editBtnW = 50f;

            // Calculate total width of controls to center them
            float controlsWidth = btnW * 5 + gap * 4       // 5 buttons + gaps
                + gap * 3                                     // spacer before speed
                + 44f                                         // "Speed:" label
                + SpeedOptions.Length * (speedBtnW + 2f)      // speed buttons
                + gap * 3                                     // spacer before toggle
                + 100f                                        // toggle
                + gap * 3                                     // spacer before edit
                + editBtnW                                    // edit button
                + gap * 3                                     // spacer before exit
                + exitBtnW;                                   // exit button

            // Background
            GUI.DrawTexture(new Rect(barX, barY, barW, btnH), timelineBgTex);

            float x = barX + (barW - controlsWidth) / 2f;

            // |<< back 1 second
            if (GUI.Button(new Rect(x, barY, btnW, btnH), "|<<"))
            { Plugin.DbgLog("BTN: |<< (back 1s)"); StepFrames(-timestep); }
            x += btnW + gap;

            // |< back 1 frame
            if (GUI.Button(new Rect(x, barY, btnW, btnH), "|<"))
            { Plugin.DbgLog("BTN: |< (back 1 frame)"); StepFrames(-1); }
            x += btnW + gap;

            // Play / Pause
            string playLabel = paused ? "\u25B6" : "\u2016"; // ▶ or ‖
            if (GUI.Button(new Rect(x, barY, btnW, btnH), playLabel))
            {
                Plugin.DbgLog($"BTN: play/pause (paused={paused})");
                if (paused) Resume();
                else Pause();
            }
            x += btnW + gap;

            // >| forward 1 frame
            if (GUI.Button(new Rect(x, barY, btnW, btnH), ">|"))
            { Plugin.DbgLog("BTN: >| (fwd 1 frame)"); StepFrames(1); }
            x += btnW + gap;

            // >>| forward 1 second
            if (GUI.Button(new Rect(x, barY, btnW, btnH), ">>|"))
            { Plugin.DbgLog("BTN: >>| (fwd 1s)"); StepFrames(timestep); }
            x += btnW + gap * 3;

            // Speed selector
            GUI.Label(new Rect(x, barY, 40, btnH), "Speed:");
            x += 44f;

            for (int i = 0; i < SpeedOptions.Length; i++)
            {
                float spd = SpeedOptions[i];
                bool isActive = Mathf.Approximately(savedTimeScale, spd) ||
                    (!paused && Mathf.Approximately(Time.timeScale, spd));

                var prevBg = GUI.backgroundColor;
                if (isActive) GUI.backgroundColor = Color.yellow;

                string label = spd >= 1f ? $"{spd:0}x" : $"{spd:0.##}x";
                if (GUI.Button(new Rect(x, barY, speedBtnW, btnH), label, speedStyle))
                {
                    Plugin.DbgLog($"BTN: speed {spd}x");
                    savedTimeScale = spd;
                    if (!paused) Time.timeScale = spd;
                }

                GUI.backgroundColor = prevBg;
                x += speedBtnW + 2f;
            }

            x += gap * 3;

            // Full timeline toggle
            float toggleW = 100f;
            bool fullTl = Plugin.TimelineFullReplay.Value;
            bool newFull = GUI.Toggle(
                new Rect(x, barY + 4, toggleW, btnH - 4),
                fullTl, "Full timeline");
            if (newFull != fullTl)
            {
                Plugin.DbgLog($"BTN: Full timeline toggled to {newFull}");
                Plugin.TimelineFullReplay.Value = newFull;
            }
            x += toggleW + gap * 3;

            // Edit mode toggle
            var prevEditBg = GUI.backgroundColor;
            if (editMode) GUI.backgroundColor = new Color(1f, 0.8f, 0.2f);
            if (GUI.Button(new Rect(x, barY, editBtnW, btnH), editMode ? "Edit*" : "Edit"))
            {
                Plugin.DbgLog($"BTN: Edit (was={editMode})");
                if (editMode) EndEditMode();
                else BeginEditMode();
            }
            GUI.backgroundColor = prevEditBg;
            x += editBtnW + gap * 3;

            // Exit replay
            var prevExitBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.8f, 0.2f, 0.2f);
            if (GUI.Button(new Rect(x, barY, exitBtnW, btnH), "Exit"))
            { Plugin.DbgLog("BTN: Exit"); Plugin.Instance.RequestStop(); }
            GUI.backgroundColor = prevExitBg;
        }

        private void StepFrames(int frames)
        {
            if (frames == 0) return;

            if (editorMode == EditorMode.Replay)
            {
                var playback = FindAnyObjectByType<ReplayPlayback>();
                if (playback == null) return;

                int target = Mathf.Max(1, playback.FrameCount + frames);
                Plugin.DbgLog($"StepFrames({frames}) current={playback.FrameCount} target={target}");

                if (frames < 0)
                {
                    playback.SeekToFrame(target);
                    return;
                }

                // Set pending — editor's Update on the next frame will execute the step
                pendingStepTarget = target;
                // playback.Update() will run each frame and set paused=true when done.
                // We sync our paused state from playback in Update().
            }
            else
            {
                // Recording: step forward by briefly unpausing
                if (frames > 0)
                {
                    if (!paused) Pause();
                    StartCoroutine(RunRecordingFrames(frames));
                }
            }
        }

        private System.Collections.IEnumerator RunRecordingFrames(int frames)
        {
            Time.timeScale = savedTimeScale;
            for (int i = 0; i < frames; i++)
                yield return null;
            Time.timeScale = 0f;
        }

        private void DrawTooltip(Vector2 pos, string text)
        {
            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
            };
            style.normal.textColor = Color.white;
            style.padding = new RectOffset(6, 6, 4, 4);

            var content = new GUIContent(text);
            Vector2 size = style.CalcSize(content);
            // Handle multiline
            float lineCount = text.Split('\n').Length;
            size.y = Mathf.Max(size.y, lineCount * 16f + 8f);

            // Position above cursor, clamped to screen
            float x = Mathf.Clamp(pos.x - size.x / 2f, 4f, Screen.width - size.x - 4f);
            float y = pos.y - size.y - 8f;
            if (y < 4f) y = pos.y + 20f;

            GUI.Box(new Rect(x, y, size.x, size.y), text, style);
        }

        // ===========================
        // HELPERS
        // ===========================

        private Color GetComboColor(string comboKey)
        {
            if (comboColorCache.TryGetValue(comboKey, out Color cached))
                return cached;

            int hash = comboKey.GetHashCode();
            float hue = ((hash & 0x7FFFFFFF) % 360) / 360f;
            float sat = 0.5f + ((hash >> 8) & 0xFF) / 512f;
            float val = 0.6f + ((hash >> 16) & 0xFF) / 640f;
            Color c = Color.HSVToRGB(hue, sat, val);
            c.a = 0.85f;
            comboColorCache[comboKey] = c;
            return c;
        }

        private static Color GetLabelColor(Color bg)
        {
            float lum = bg.r * 0.299f + bg.g * 0.587f + bg.b * 0.114f;
            return lum > 0.5f ? Color.black : Color.white;
        }

        private static string AbbreviateKeys(HashSet<string> keys)
        {
            var parts = new List<string>();
            foreach (var k in keys.OrderBy(x => x))
                parts.Add(KeyNames.ToTimelineLabel(k));
            return string.Join(" ", parts);
        }

        private static Texture2D MakeTex(int w, int h, Color col)
        {
            var pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++) pix[i] = col;
            var tex = new Texture2D(w, h);
            tex.SetPixels(pix);
            tex.Apply();
            return tex;
        }

        // ===========================
        // TAS EDIT PANEL
        // ===========================

        private void DrawEditPanel()
        {
            if (editSession == null) return;

            var playback = FindAnyObjectByType<ReplayPlayback>();
            if (playback == null) return;

            int currentFrame = playback.FrameCount;
            int timestep = replayFile?.Timestep ?? 50;

            // Panel layout: above the transport controls
            float panelW = Mathf.Min(Screen.width - TimelinePadding * 2, 760f);
            float panelX = (Screen.width - panelW) / 2f;
            float panelY = Screen.height - TimelineHeight - TimelinePadding - TransportHeight - 2f - EditPanelHeight - 6f;
            float panelH = EditPanelHeight;

            EnsureTimelineStyles();
            GUI.DrawTexture(new Rect(panelX, panelY, panelW, panelH), timelineBgTex);

            // Border to distinguish from timeline
            DrawBorder(new Rect(panelX, panelY, panelW, panelH), new Color(1f, 0.8f, 0.2f, 0.9f));

            // Header
            var headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
            };
            headerStyle.normal.textColor = Color.white;
            string header = $"EDIT  •  Frame {currentFrame}  •  Spans {replayFile.Spans.Count}";
            if (editSession.IsDirty) header += $"  •  \u25CF DIRTY (from {editSession.DirtyFromFrame})";
            GUI.Label(new Rect(panelX + 10, panelY + 6, panelW - 20, 18), header, headerStyle);

            // Held keys list (top-left area)
            var heldKeys = editSession.GetKeysAtFrame(currentFrame);
            DrawHeldKeysList(panelX + 10, panelY + 28, 260, panelH - 38, heldKeys, currentFrame);

            // Capture box + add-key picker (middle area)
            DrawCaptureAndPicker(panelX + 280, panelY + 28, 220, panelH - 38, heldKeys, currentFrame);

            // Right-side controls (scope + undo/redo/reverify/save/discard)
            DrawEditControls(panelX + 510, panelY + 28, panelW - 520, panelH - 38, playback, currentFrame);

            // Transient capture feedback toast (centered under the panel)
            if (!string.IsNullOrEmpty(captureBoxFeedback) && Time.unscaledTime < captureBoxFeedbackEndTime)
            {
                var toastStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11,
                    alignment = TextAnchor.MiddleCenter,
                };
                toastStyle.normal.textColor = new Color(0.6f, 1f, 0.6f);
                GUI.Label(new Rect(panelX, panelY + panelH - 16, panelW, 14), captureBoxFeedback, toastStyle);
            }
        }

        private void DrawHeldKeysList(float x, float y, float w, float h,
                                       HashSet<string> heldKeys, int currentFrame)
        {
            var labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            labelStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            GUI.Label(new Rect(x, y, w, 14), "Held keys at playhead:", labelStyle);

            float rowY = y + 18;
            float rowH = 20;
            if (heldKeys.Count == 0)
            {
                var emptyStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Italic };
                emptyStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
                GUI.Label(new Rect(x, rowY, w, 18), "(none — span is empty)", emptyStyle);
                return;
            }

            var sorted = heldKeys.OrderBy(k => KeyNames.ToShortName(k)).ToList();
            foreach (var keyPath in sorted)
            {
                if (rowY + rowH > y + h) break;
                string name = KeyNames.ToShortName(keyPath);
                GUI.Label(new Rect(x, rowY, w - 28, rowH), "  " + name, labelStyle);
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.7f, 0.3f, 0.3f);
                if (GUI.Button(new Rect(x + w - 24, rowY + 1, 22, rowH - 2), "x"))
                {
                    Plugin.DbgLog($"BTN: Remove key '{KeyNames.ToShortName(keyPath)}' at frame {currentFrame} scope={editScope}");
                    var newSet = new HashSet<string>(heldKeys);
                    newSet.Remove(keyPath);
                    editSession.SetHeldKeysAt(currentFrame, newSet, editScope);
                    PrecomputeReplayGhosts();
                }
                GUI.backgroundColor = prevBg;
                rowY += rowH;
            }
        }

        private void DrawCaptureAndPicker(float x, float y, float w, float h,
                                           HashSet<string> heldKeys, int currentFrame)
        {
            var labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            labelStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            GUI.Label(new Rect(x, y, w, 14), "Add key:", labelStyle);

            float rowY = y + 18;

            // Press-to-capture box (focusable). Click to focus, then press any key.
            float boxH = 28f;
            var boxRect = new Rect(x, rowY, w, boxH);

            // Draw background
            GUI.DrawTexture(boxRect, timelineBgTex);
            Color borderCol = captureBoxFocused ? new Color(0.3f, 0.9f, 1f) : new Color(0.4f, 0.4f, 0.4f);
            DrawBorder(boxRect, borderCol);

            // Focus detection via mouse click
            if (Event.current.type == EventType.MouseDown &&
                Event.current.button == 0 &&
                boxRect.Contains(Event.current.mousePosition))
            {
                captureBoxFocused = true;
                GUI.FocusControl(null); // drop any text field focus
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseDown && !boxRect.Contains(Event.current.mousePosition))
            {
                captureBoxFocused = false;
            }

            var boxLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = captureBoxFocused ? FontStyle.Bold : FontStyle.Normal,
            };
            boxLabel.normal.textColor = captureBoxFocused
                ? new Color(0.3f, 0.9f, 1f)
                : new Color(0.7f, 0.7f, 0.7f);
            GUI.Label(boxRect, captureBoxFocused
                ? "Press any key to add…"
                : "Click, then press a key", boxLabel);

            // Handle KeyDown / MouseDown events while focused (swallowed so they
            // don't leak to the game). Mouse-button capture only fires on OUR box,
            // not on a general mouse-down — to keep click-outside-to-unfocus working.
            if (captureBoxFocused && Event.current.type == EventType.KeyDown &&
                Event.current.keyCode != KeyCode.None)
            {
                string path = KeyCodeToInputPath(Event.current.keyCode);
                if (path != null)
                {
                    Plugin.DbgLog($"CAPTURE: key press '{KeyNames.ToShortName(path)}' ({Event.current.keyCode}) at frame {currentFrame}");
                    AddKeyToSpan(path, heldKeys, currentFrame);
                    Event.current.Use();
                }
            }

            rowY += boxH + 6;

            // Add key picker button
            float pickerBtnH = 22;
            if (GUI.Button(new Rect(x, rowY, w, pickerBtnH),
                pickerOpen ? "\u25BC Add key from list (open)" : "\u25B6 Add key from list\u2026"))
            {
                pickerOpen = !pickerOpen;
                Plugin.DbgLog($"BTN: Picker toggled to {(pickerOpen ? "open" : "closed")}");
                pickerFilter = "";
                pickerScroll = Vector2.zero;
            }
            rowY += pickerBtnH + 4;

            // Scope radio
            DrawScopeRadio(x, rowY, w);
        }

        private void DrawScopeRadio(float x, float y, float w)
        {
            var lblStyle = new GUIStyle(GUI.skin.label) { fontSize = 10 };
            lblStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
            GUI.Label(new Rect(x, y, w, 14), "Scope:", lblStyle);

            float ry = y + 14;
            bool fromHere = editScope == EditScope.FromHere;
            bool newFromHere = GUI.Toggle(new Rect(x, ry, w, 16), fromHere, " From this frame onward");
            if (newFromHere && !fromHere) { editScope = EditScope.FromHere; Plugin.DbgLog("BTN: Scope changed to FromHere"); }

            bool thisOnly = editScope == EditScope.ThisFrameOnly;
            bool newThisOnly = GUI.Toggle(new Rect(x, ry + 16, w, 16), thisOnly, " This frame only");
            if (newThisOnly && !thisOnly) { editScope = EditScope.ThisFrameOnly; Plugin.DbgLog("BTN: Scope changed to ThisFrameOnly"); }
        }

        private void DrawEditControls(float x, float y, float w, float h,
                                       ReplayPlayback playback, int currentFrame)
        {
            var labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            labelStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            GUI.Label(new Rect(x, y, w, 14), "Actions:", labelStyle);

            float btnH = 22;
            float gap = 4;
            float colY = y + 18;
            float half = (w - gap) / 2f;

            bool canUndo = editSession.CanUndo;
            bool canRedo = editSession.CanRedo;

            GUI.enabled = canUndo;
            if (GUI.Button(new Rect(x, colY, half, btnH), "Undo"))
            {
                Plugin.DbgLog($"BTN: Undo (stack depth={editSession.UndoDepth})");
                editSession.Undo();
                PrecomputeReplayGhosts();
            }
            GUI.enabled = canRedo;
            if (GUI.Button(new Rect(x + half + gap, colY, half, btnH), "Redo"))
            {
                Plugin.DbgLog($"BTN: Redo (stack depth={editSession.RedoDepth})");
                editSession.Redo();
                PrecomputeReplayGhosts();
            }
            GUI.enabled = true;
            colY += btnH + gap;

            // Re-verify (only enabled when dirty)
            GUI.enabled = editSession.IsDirty;
            var prevBg = GUI.backgroundColor;
            if (editSession.IsDirty) GUI.backgroundColor = new Color(0.9f, 0.7f, 0.2f);
            if (GUI.Button(new Rect(x, colY, w, btnH), "Re-verify from dirty"))
            {
                int from = editSession.DirtyFromFrame;
                Plugin.DbgLog($"BTN: Re-verify from frame {from}");
                playback.BeginReverifyFromFrame(from);
            }
            GUI.backgroundColor = prevBg;
            GUI.enabled = true;
            colY += btnH + gap;

            // Save As (blocked while dirty)
            GUI.enabled = !editSession.IsDirty;
            if (GUI.Button(new Rect(x, colY, w, btnH), "Save As new file"))
            {
                try
                {
                    string saved = editSession.SaveAsNext();
                    Plugin.Instance?.ShowToast($"Saved: {Path.GetFileName(saved)}");
                    Plugin.DbgLog($"BTN: Save As -> {saved}");
                }
                catch (System.Exception ex)
                {
                    Plugin.Instance?.ShowToast("Save failed: " + ex.Message);
                    Plugin.DbgLog($"Save As failed: {ex}");
                }
            }
            GUI.enabled = true;
            colY += btnH + gap;

            // Discard edits — reloads from disk
            prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.7f, 0.3f, 0.3f);
            if (GUI.Button(new Rect(x, colY, w, btnH), "Discard edits"))
            {
                Plugin.DbgLog("BTN: Discard edits");
                DiscardEditsAndReload(playback);
            }
            GUI.backgroundColor = prevBg;

            // Draw the picker popup LAST so it overlays everything else in the panel.
            if (pickerOpen)
                DrawAddKeyPicker(currentFrame);
        }

        private void DrawAddKeyPicker(int currentFrame)
        {
            // Popup: draw on top, filling a big chunk of the screen bottom so it
            // doesn't collide with the transport bar. Keys already held are
            // greyed out.
            float popupW = Mathf.Min(520f, Screen.width - 40f);
            float popupH = 260f;
            float popupX = (Screen.width - popupW) / 2f;
            float popupY = Screen.height - TimelineHeight - TimelinePadding - TransportHeight - 2f - EditPanelHeight - popupH - 14f;

            GUI.DrawTexture(new Rect(popupX, popupY, popupW, popupH), timelineBgTex);
            DrawBorder(new Rect(popupX, popupY, popupW, popupH), new Color(0.3f, 0.9f, 1f));

            var titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(popupX + 10, popupY + 6, popupW - 60, 18), "Add key (bound keys first)", titleStyle);

            if (GUI.Button(new Rect(popupX + popupW - 46, popupY + 4, 40, 20), "Close"))
            {
                Plugin.DbgLog("BTN: Picker closed");
                pickerOpen = false;
            }

            // Filter box
            GUI.SetNextControlName("PickerFilter");
            pickerFilter = GUI.TextField(new Rect(popupX + 10, popupY + 28, popupW - 20, 22), pickerFilter ?? "");

            // Build ordered list: game-bound keys first, then everything else.
            var heldKeys = editSession.GetKeysAtFrame(currentFrame);
            var ordered = BuildPickerOrder(currentFrame);

            string filter = (pickerFilter ?? "").Trim().ToLowerInvariant();

            // Scrollable body
            var scrollRect = new Rect(popupX + 10, popupY + 56, popupW - 20, popupH - 66);
            int visibleCount = 0;
            foreach (var entry in ordered)
            {
                if (!string.IsNullOrEmpty(filter) &&
                    !KeyNames.ToShortName(entry.Path).ToLowerInvariant().Contains(filter) &&
                    !entry.Path.ToLowerInvariant().Contains(filter))
                    continue;
                visibleCount++;
            }

            float rowH = 22;
            var viewRect = new Rect(0, 0, scrollRect.width - 18, visibleCount * rowH + 2);
            pickerScroll = GUI.BeginScrollView(scrollRect, pickerScroll, viewRect);

            float rowY = 0;
            foreach (var entry in ordered)
            {
                string shortName = KeyNames.ToShortName(entry.Path);
                if (!string.IsNullOrEmpty(filter) &&
                    !shortName.ToLowerInvariant().Contains(filter) &&
                    !entry.Path.ToLowerInvariant().Contains(filter))
                    continue;

                bool already = heldKeys.Contains(entry.Path);
                GUI.enabled = !already;

                string label = shortName;
                if (!string.IsNullOrEmpty(entry.Action)) label += $"   ({entry.Action})";
                if (already) label += "   [held]";

                if (GUI.Button(new Rect(2, rowY, viewRect.width - 4, rowH - 2), label))
                {
                    Plugin.DbgLog($"PICKER: selected '{KeyNames.ToShortName(entry.Path)}' at frame {currentFrame}");
                    AddKeyToSpan(entry.Path, heldKeys, currentFrame);
                    pickerOpen = false;
                }
                GUI.enabled = true;
                rowY += rowH;
            }
            GUI.EndScrollView();
        }

        private struct PickerEntry
        {
            public string Path;
            public string Action; // "" if not a bound action
        }

        private List<PickerEntry> BuildPickerOrder(int currentFrame)
        {
            var result = new List<PickerEntry>();
            var seen = new HashSet<string>();

            // 1. Bound keys first, grouped by action in declaration order.
            var bindings = editSession.GetBindingsAtFrame(currentFrame);
            if (bindings.ActionKeys != null)
            {
                // Move/Jump/Dash first, then any extras.
                var actionOrder = new List<string>();
                foreach (var preferred in new[] { "Move", "Jump", "Dash" })
                    if (bindings.ActionKeys.ContainsKey(preferred)) actionOrder.Add(preferred);
                foreach (var kv in bindings.ActionKeys)
                    if (!actionOrder.Contains(kv.Key)) actionOrder.Add(kv.Key);

                foreach (var action in actionOrder)
                {
                    foreach (var path in bindings.ActionKeys[action])
                    {
                        if (string.IsNullOrEmpty(path)) continue;
                        if (seen.Add(path))
                            result.Add(new PickerEntry { Path = path, Action = action });
                    }
                }
            }

            // 2. Everything else from the static registry.
            foreach (var path in KeyNames.AllPaths())
            {
                if (seen.Add(path))
                    result.Add(new PickerEntry { Path = path, Action = "" });
            }
            return result;
        }

        private void AddKeyToSpan(string keyPath, HashSet<string> heldKeys, int currentFrame)
        {
            if (heldKeys.Contains(keyPath))
            {
                Plugin.DbgLog($"AddKeyToSpan: '{KeyNames.ToShortName(keyPath)}' already held at frame {currentFrame}, no-op");
                captureBoxFeedback = $"{KeyNames.ToShortName(keyPath)} already held";
                captureBoxFeedbackEndTime = Time.unscaledTime + 1.5f;
                return;
            }
            Plugin.DbgLog($"AddKeyToSpan: adding '{KeyNames.ToShortName(keyPath)}' at frame {currentFrame} scope={editScope}");
            var newSet = new HashSet<string>(heldKeys) { keyPath };
            editSession.SetHeldKeysAt(currentFrame, newSet, editScope);
            PrecomputeReplayGhosts();
            captureBoxFeedback = $"+ {KeyNames.ToShortName(keyPath)}";
            captureBoxFeedbackEndTime = Time.unscaledTime + 1.5f;
        }

        private void DiscardEditsAndReload(ReplayPlayback playback)
        {
            if (string.IsNullOrEmpty(OriginalLoadPath)) return;
            try
            {
                var reloaded = ReplayFormat.Read(OriginalLoadPath);
                // Swap the in-memory ReplayFile everywhere it matters.
                replayFile = reloaded;
                editSession = new ReplayEditSession(reloaded, OriginalLoadPath, player);
                PrecomputeReplayGhosts();

                // Restart playback bound to the reloaded file from frame 0.
                playback.StopPlayback();
                playback.StartPlayback(player, reloaded);
                Plugin.Instance?.ShowToast("Edits discarded — reloaded from disk");
            }
            catch (System.Exception ex)
            {
                Plugin.Instance?.ShowToast("Discard failed: " + ex.Message);
                Plugin.DbgLog($"DiscardEditsAndReload failed: {ex}");
            }
        }

        private void DrawReverifyOverlay(ReplayPlayback playback)
        {
            float w = Mathf.Min(Screen.width - 40f, 560f);
            float h = 70f;
            float panelX = (Screen.width - w) / 2f;
            float panelY = Screen.height - TimelineHeight - TimelinePadding - TransportHeight - 2f - h - 6f;

            EnsureTimelineStyles();
            GUI.DrawTexture(new Rect(panelX, panelY, w, h), timelineBgTex);
            DrawBorder(new Rect(panelX, panelY, w, h), new Color(0.9f, 0.7f, 0.2f));

            int lastFrame = replayFile.Spans.Count > 0
                ? replayFile.Spans[replayFile.Spans.Count - 1].Frame
                : 1;
            int cur = playback.FrameCount;
            float frac = lastFrame > 0 ? Mathf.Clamp01((float)cur / lastFrame) : 0f;

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            titleStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);
            GUI.Label(new Rect(panelX, panelY + 6, w, 18),
                "RE-VERIFYING — controls locked", titleStyle);

            var subStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
            };
            subStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(panelX, panelY + 24, w, 14),
                $"frame {cur} / {lastFrame}", subStyle);

            // Progress bar
            float barPad = 20f;
            var barBgRect = new Rect(panelX + barPad, panelY + 44, w - barPad * 2, 14);
            GUI.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            GUI.DrawTexture(barBgRect, timelineWhiteTex);
            GUI.color = new Color(0.9f, 0.7f, 0.2f);
            GUI.DrawTexture(new Rect(barBgRect.x, barBgRect.y, barBgRect.width * frac, barBgRect.height),
                timelineWhiteTex);
            GUI.color = Color.white;
        }

        private static void DrawBorder(Rect rect, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            // Use the white timeline texture if available, otherwise GUI.skin.box
            var tex = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1), tex);
            GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - 1, rect.width, 1), tex);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 1, rect.height), tex);
            GUI.DrawTexture(new Rect(rect.x + rect.width - 1, rect.y, 1, rect.height), tex);
            GUI.color = prev;
        }

        /// <summary>
        /// Map a Unity KeyCode (from IMGUI Event.current.keyCode) to the canonical
        /// InputSystem path format used by replay files (e.g. "&lt;Keyboard&gt;/a").
        /// Returns null for keycodes we don't have a stable path for.
        /// </summary>
        private static string KeyCodeToInputPath(KeyCode kc)
        {
            if (kc >= KeyCode.A && kc <= KeyCode.Z)
                return "<Keyboard>/" + char.ToLower((char)('A' + (kc - KeyCode.A)));
            if (kc >= KeyCode.Alpha0 && kc <= KeyCode.Alpha9)
                return "<Keyboard>/" + (kc - KeyCode.Alpha0).ToString();
            if (kc >= KeyCode.F1 && kc <= KeyCode.F12)
                return "<Keyboard>/f" + (1 + (kc - KeyCode.F1));
            switch (kc)
            {
                case KeyCode.Space: return "<Keyboard>/space";
                case KeyCode.Return: return "<Keyboard>/enter";
                case KeyCode.KeypadEnter: return "<Keyboard>/enter";
                case KeyCode.Escape: return "<Keyboard>/escape";
                case KeyCode.Tab: return "<Keyboard>/tab";
                case KeyCode.Backspace: return "<Keyboard>/backspace";
                case KeyCode.LeftShift: return "<Keyboard>/leftShift";
                case KeyCode.RightShift: return "<Keyboard>/rightShift";
                case KeyCode.LeftControl: return "<Keyboard>/leftCtrl";
                case KeyCode.RightControl: return "<Keyboard>/rightCtrl";
                case KeyCode.LeftAlt: return "<Keyboard>/leftAlt";
                case KeyCode.RightAlt: return "<Keyboard>/rightAlt";
                case KeyCode.LeftArrow: return "<Keyboard>/leftArrow";
                case KeyCode.RightArrow: return "<Keyboard>/rightArrow";
                case KeyCode.UpArrow: return "<Keyboard>/upArrow";
                case KeyCode.DownArrow: return "<Keyboard>/downArrow";
                case KeyCode.Delete: return "<Keyboard>/delete";
                case KeyCode.Insert: return "<Keyboard>/insert";
                case KeyCode.Home: return "<Keyboard>/home";
                case KeyCode.End: return "<Keyboard>/end";
                case KeyCode.PageUp: return "<Keyboard>/pageUp";
                case KeyCode.PageDown: return "<Keyboard>/pageDown";
                case KeyCode.Comma: return "<Keyboard>/comma";
                case KeyCode.Period: return "<Keyboard>/period";
                case KeyCode.Slash: return "<Keyboard>/slash";
                case KeyCode.Backslash: return "<Keyboard>/backslash";
                case KeyCode.Semicolon: return "<Keyboard>/semicolon";
                case KeyCode.Quote: return "<Keyboard>/quote";
                case KeyCode.LeftBracket: return "<Keyboard>/leftBracket";
                case KeyCode.RightBracket: return "<Keyboard>/rightBracket";
                case KeyCode.Minus: return "<Keyboard>/minus";
                case KeyCode.Equals: return "<Keyboard>/equals";
                case KeyCode.BackQuote: return "<Keyboard>/backquote";
                case KeyCode.CapsLock: return "<Keyboard>/capsLock";
            }
            return null;
        }

        private void OnDestroy()
        {
            ClearLiveMarkers();
            ClearReplayMarkers();
            if (paused)
            {
                Time.timeScale = savedTimeScale;
                paused = false;
            }
        }
    }
}
