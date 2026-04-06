using System.Collections.Generic;
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
        }

        /// <summary>Precomputed ghost from replay file spans.</summary>
        private struct ReplayGhost
        {
            public int Frame;
            public int SpanIndex;
            public string Label;     // "+space -d" etc
            public bool IsPress;     // had keys added
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
            player = null;
            replayFile = null;
            editorMode = EditorMode.None;
        }

        // --- Update ---

        private void Update()
        {
            if (player == null || editorMode == EditorMode.None) return;

            if (editorMode == EditorMode.Replay)
            {
                var playback = FindAnyObjectByType<ReplayPlayback>();
                if (playback != null)
                {
                    if (pendingStepTarget > 0)
                    {
                        Time.timeScale = savedTimeScale > 0 ? savedTimeScale : 1f;
                        playback.PauseOnFrame = pendingStepTarget;
                        playback.IsPaused = false;
                        paused = false;
                        Plugin.DbgLog($"Editor.Update step GO target={pendingStepTarget}");
                        pendingStepTarget = 0;
                    }
                    // Don't sync pause while a step is in flight
                    else if (playback.PauseOnFrame > 0)
                    {
                    }
                    // Sync pause state from playback (seeking/step finished)
                    else if (playback.IsPaused && !playback.IsSeeking && !paused)
                    {
                        paused = true;
                        Time.timeScale = 0f;
                        Plugin.DbgLog("Editor.Update synced pause from playback");
                    }
                }
            }

            // Use old Input system for editor hotkeys (real InputSystem devices are
            // disabled during playback, but UnityEngine.Input still works)
            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                if (paused) Resume();
                else Pause();
            }

            if (paused)
                HandleStepping();

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
            Time.timeScale = savedTimeScale > 0 ? savedTimeScale : 1f;

            var playback = FindAnyObjectByType<ReplayPlayback>();
            if (playback != null)
            {
                playback.IsPaused = false;
                playback.PauseOnFrame = 0; // clear any pending pause
            }

            Plugin.DbgLog($"Editor.Resume timeScale={Time.timeScale}");
            Plugin.Instance.ShowToast("Resumed");
        }

        private void HandleStepping()
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

            var playback = FindAnyObjectByType<ReplayPlayback>();
            int timestep = replayFile?.Timestep ?? 50;

            if (stepForward) StepFrames(1);
            else if (stepSecFwd) StepFrames(timestep);
            else if (stepBackward) StepFrames(-1);
            else if (stepSecBack) StepFrames(-timestep);
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

            var parts = new List<string>();
            foreach (var k in added) parts.Add("+" + k);
            foreach (var k in removed) parts.Add("-" + k);
            string labelText = string.Join(" ", parts);

            SpawnLiveGhost(p, labelText, added.Count > 0);
            prevMarkerKeys = new HashSet<string>(currentKeys);
        }

        private void SpawnLiveGhost(Movement p, string label, bool isPress)
        {
            var go = CreateGhostObject(p, isPress);
            if (go == null) return;

            liveMarkers.Add(new GhostMarker
            {
                Go = go,
                Sr = go.GetComponent<SpriteRenderer>(),
                SpawnTime = Time.unscaledTime,
                GhostIndex = -1,
            });

            liveLabels.Add(new MarkerLabel
            {
                WorldPos = p.transform.position + Vector3.up * 40f,
                Text = label,
                SpawnTime = Time.unscaledTime,
            });
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
                    var parts = new List<string>();
                    foreach (var k in added) parts.Add("+" + k);
                    foreach (var k in removed) parts.Add("-" + k);

                    replayGhosts.Add(new ReplayGhost
                    {
                        Frame = span.Frame,
                        SpanIndex = i,
                        Label = string.Join(" ", parts),
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
                ? new Color(0.3f, 1f, 0.3f, MarkerAlpha)
                : new Color(1f, 0.3f, 0.3f, MarkerAlpha);
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

            // Ghost labels (live, during recording)
            if (liveLabels.Count > 0)
                DrawLiveLabels();

            // Replay ghost labels
            if (editorMode == EditorMode.Replay && Plugin.ReplayGhostsEnabled.Value)
                DrawReplayGhostLabels();

            // Paused banner
            if (paused)
                DrawPausedBanner();
        }

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

                style.normal.textColor = new Color(0f, 0f, 0f, alpha);
                GUI.Label(new Rect(screenPos.x - 49, guiY - 9, 100, 20), label.Text, style);
                style.normal.textColor = new Color(1f, 1f, 1f, alpha);
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

                Vector3 worldPos = m.Go.transform.position + Vector3.up * 40f;
                Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
                if (screenPos.z < 0) continue;
                float guiY = Screen.height - screenPos.y;

                float alpha = m.Sr.color.a / MarkerAlpha; // match ghost opacity
                style.normal.textColor = new Color(0f, 0f, 0f, alpha);
                GUI.Label(new Rect(screenPos.x - 59, guiY - 9, 120, 20), ghost.Label, style);
                style.normal.textColor = new Color(1f, 1f, 1f, alpha);
                GUI.Label(new Rect(screenPos.x - 60, guiY - 10, 120, 20), ghost.Label, style);
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
                    string keyList = string.Join(", ", keys.OrderBy(k => k));
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

            Event e = Event.current;
            Vector2 mousePos = e.mousePosition;
            Rect barRect = new Rect(barX, barY, barW, barH);

            if (e.type == EventType.MouseDown && e.button == 0 && barRect.Contains(mousePos))
            {
                isScrubbing = true;
                if (!paused) Pause();
                e.Use();
            }

            if (isScrubbing && (e.type == EventType.MouseDrag || e.type == EventType.MouseDown))
            {
                float frac = Mathf.Clamp01((mousePos.x - barX) / barW);
                int targetFrame = frameStart + Mathf.RoundToInt(frac * totalFrames);
                targetFrame = Mathf.Max(1, targetFrame);

                paused = false;
                Time.timeScale = savedTimeScale;
                playback.SeekToFrame(targetFrame);
                e.Use();
            }

            if (isScrubbing && e.type == EventType.MouseUp && e.button == 0)
            {
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

            // Calculate total width of controls to center them
            float controlsWidth = btnW * 5 + gap * 4       // 5 buttons + gaps
                + gap * 3                                     // spacer before speed
                + 44f                                         // "Speed:" label
                + SpeedOptions.Length * (speedBtnW + 2f)      // speed buttons
                + gap * 3                                     // spacer before toggle
                + 100f                                        // toggle
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
                Plugin.TimelineFullReplay.Value = newFull;
            x += toggleW + gap * 3;

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
            {
                switch (k)
                {
                    case "space": parts.Add("SPC"); break;
                    case "lshift": parts.Add("DASH"); break;
                    case "a": parts.Add("L"); break;
                    case "d": parts.Add("R"); break;
                    case "w": parts.Add("U"); break;
                    case "s": parts.Add("D"); break;
                    case "mouse0": parts.Add("M1"); break;
                    case "mouse1": parts.Add("M2"); break;
                    case "mouse2": parts.Add("M3"); break;
                    default: parts.Add(k.ToUpper()); break;
                }
            }
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
