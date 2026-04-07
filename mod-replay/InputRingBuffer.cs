using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IGTAPReplay
{
    /// <summary>
    /// Always-on circular buffer that continuously records player input.
    /// Replaces ReplayRecorder with a ring buffer that can be trimmed, pinned,
    /// and have arbitrary frame ranges extracted as ReplayFile objects.
    /// </summary>
    public class InputRingBuffer
    {
        private static ManualLogSource Log => Plugin.Log;

        // --- Configuration ---
        private int capacity; // max frames to retain (soft limit, pins override)
        private int timestep;

        // --- Frame tracking ---
        private int headFrame; // monotonically increasing, never resets
        private int tailFrame; // oldest frame still retained
        private bool running;
        private Movement trackedPlayer;

        // --- Data (append-only, trimmed from front) ---
        private readonly List<InputSpan> spans = new List<InputSpan>();
        private readonly List<ReplayCheckpoint> checkpoints = new List<ReplayCheckpoint>();
        private readonly List<VerifyPoint> verifyPoints = new List<VerifyPoint>();
        private BindingSnapshot currentBindings;

        // --- Input tracking (for change detection) ---
        private HashSet<string> prevKeys = new HashSet<string>();
        private Vector2 prevMousePos;
        private bool keysChanged;

        // --- Pinning ---
        private readonly Dictionary<int, int> pinnedFrames = new Dictionary<int, int>(); // frame -> refcount
        private readonly Dictionary<int, float> pinTimestamps = new Dictionary<int, float>(); // frame -> Time.unscaledTime when first pinned
        private const float MaxPinDurationSeconds = 600f; // 10 minutes

        // --- Keys to never record (mod hotkeys) ---
        private readonly HashSet<Key> ignoredKeys = new HashSet<Key>();

        // --- Public accessors ---
        public bool IsRunning => running;
        public int HeadFrame => headFrame;
        public int TailFrame => tailFrame;
        public int FrameCount => headFrame; // total frames since buffer started
        public int Timestep => timestep;
        public Movement TrackedPlayer => trackedPlayer;

        /// <summary>Optional editor to notify on input changes for ghost markers.</summary>
        public ReplayEditor Editor;

        public InputRingBuffer(int capacityFrames, int timestep)
        {
            this.capacity = capacityFrames;
            this.timestep = timestep;
        }

        public void SetIgnoredKeys(IEnumerable<KeyCode> keyCodes)
        {
            ignoredKeys.Clear();
            foreach (var kc in keyCodes)
            {
                var key = KeyCodeToKey(kc);
                if (key != Key.None)
                    ignoredKeys.Add(key);
            }
        }

        public void Start(Movement player)
        {
            if (running) return;
            if (player == null)
            {
                Log.LogError("Cannot start ring buffer: no player found.");
                return;
            }

            if (Time.captureFramerate <= 0)
                Log.LogWarning("Fixed timestep mod not active! Replay may not be deterministic.");

            trackedPlayer = player;
            headFrame = 0;
            tailFrame = 0;
            prevKeys.Clear();
            prevMousePos = Vector2.zero;

            spans.Clear();
            checkpoints.Clear();
            verifyPoints.Clear();

            timestep = Time.captureFramerate > 0 ? Time.captureFramerate : 50;

            var initialState = ReplayState.Capture(player);
            currentBindings = CaptureBindings(player, 0);

            checkpoints.Add(new ReplayCheckpoint
            {
                Frame = 0,
                SpanIndex = 0,
                State = initialState,
            });

            running = true;
            Log.LogInfo("Ring buffer started.");
        }

        public void Stop()
        {
            if (!running) return;

            // Final checkpoint
            if (trackedPlayer != null &&
                (checkpoints.Count == 0 || checkpoints[checkpoints.Count - 1].Frame != headFrame))
                CaptureCheckpoint(trackedPlayer);

            running = false;
            trackedPlayer = null;
            Log.LogInfo($"Ring buffer stopped. {spans.Count} spans, {headFrame} frames.");
        }

        /// <summary>
        /// Called from Harmony prefix on Movement.Update, before the game reads input.
        /// </summary>
        public void OnFrame(Movement player)
        {
            if (!running || player != trackedPlayer) return;

            headFrame++;
            if (headFrame <= 3)
            {
                var body = (Rigidbody2D)ReplayState.F_body.GetValue(player);
                Plugin.DbgLog($"BUF OnFrame fc={headFrame} dt={Time.deltaTime:F4} vel=({body.linearVelocityX:F1},{body.linearVelocityY:F1}) pos=({body.position.x:F1},{body.position.y:F1})");
            }

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;

            // Collect all currently pressed keys
            var currentKeys = new HashSet<string>();

            if (keyboard != null)
            {
                foreach (Key key in Enum.GetValues(typeof(Key)))
                {
                    if (key == Key.None) continue;
#pragma warning disable CS0618
                    if (key == Key.IMESelected) continue;
#pragma warning restore CS0618
                    if (ignoredKeys.Contains(key)) continue;

                    try
                    {
                        var ctrl = keyboard[key];
                        if (ctrl != null && ctrl.isPressed)
                            currentKeys.Add("<Keyboard>/" + ctrl.name);
                    }
                    catch
                    {
                        // Some Key enum values may not map to a valid control
                    }
                }
            }

            // Mouse buttons
            if (mouse != null)
            {
                if (mouse.leftButton.isPressed) currentKeys.Add("<Mouse>/leftButton");
                if (mouse.rightButton.isPressed) currentKeys.Add("<Mouse>/rightButton");
                if (mouse.middleButton.isPressed) currentKeys.Add("<Mouse>/middleButton");
                if (mouse.forwardButton.isPressed) currentKeys.Add("<Mouse>/forwardButton");
                if (mouse.backButton.isPressed) currentKeys.Add("<Mouse>/backButton");
            }

            // Gamepad buttons
            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.buttonSouth.isPressed) currentKeys.Add("<Gamepad>/buttonSouth");
                if (gamepad.buttonNorth.isPressed) currentKeys.Add("<Gamepad>/buttonNorth");
                if (gamepad.buttonEast.isPressed) currentKeys.Add("<Gamepad>/buttonEast");
                if (gamepad.buttonWest.isPressed) currentKeys.Add("<Gamepad>/buttonWest");
                if (gamepad.leftShoulder.isPressed) currentKeys.Add("<Gamepad>/leftShoulder");
                if (gamepad.rightShoulder.isPressed) currentKeys.Add("<Gamepad>/rightShoulder");
                if (gamepad.leftTrigger.isPressed) currentKeys.Add("<Gamepad>/leftTrigger");
                if (gamepad.rightTrigger.isPressed) currentKeys.Add("<Gamepad>/rightTrigger");
                if (gamepad.startButton.isPressed) currentKeys.Add("<Gamepad>/start");
                if (gamepad.selectButton.isPressed) currentKeys.Add("<Gamepad>/select");
                if (gamepad.dpad.up.isPressed) currentKeys.Add("<Gamepad>/dpad/up");
                if (gamepad.dpad.down.isPressed) currentKeys.Add("<Gamepad>/dpad/down");
                if (gamepad.dpad.left.isPressed) currentKeys.Add("<Gamepad>/dpad/left");
                if (gamepad.dpad.right.isPressed) currentKeys.Add("<Gamepad>/dpad/right");
                if (gamepad.leftStickButton.isPressed) currentKeys.Add("<Gamepad>/leftStickButton");
                if (gamepad.rightStickButton.isPressed) currentKeys.Add("<Gamepad>/rightStickButton");
            }

            // Mouse position (screen coords) — only if enabled in config
            bool trackMousePos = Plugin.RecordMousePosition.Value;
            Vector2 currentMousePos = (trackMousePos && mouse != null) ? mouse.position.ReadValue() : Vector2.zero;
            bool mouseMoved = trackMousePos && Vector2.Distance(currentMousePos, prevMousePos) > 0.5f;

            // Emit a span if keys or mouse position changed
            keysChanged = !currentKeys.SetEquals(prevKeys);
            if (keysChanged || mouseMoved)
            {
                // Compute xMoveAxis the same way Movement does from the input
                float xma = 0f;
                if (keyboard != null)
                {
                    var moveAction = (InputAction)ReplayState.F_moveAction.GetValue(player);
                    var moveVal = moveAction.ReadValue<Vector2>();
                    xma = moveVal.x;
                    if ((double)xma > 0.85) xma = 1f;
                    else if ((double)xma < -0.85) xma = -1f;
                    else if ((double)xma > 0.4 && (double)Mathf.Abs(moveVal.y) > 0.4) xma = 1f;
                    else if ((double)xma < -0.4 && (double)Mathf.Abs(moveVal.y) > 0.4) xma = -1f;
                    else if ((double)Mathf.Abs(xma) < 0.2) xma = 0f;
                }

                var span = new InputSpan
                {
                    Frame = headFrame,
                    Keys = new HashSet<string>(currentKeys),
                    XMoveAxis = xma,
                };

                if (mouseMoved)
                {
                    span.MousePos = currentMousePos;
                    prevMousePos = currentMousePos;
                }

                spans.Add(span);

                // Notify editor to spawn ghost marker (only during active recordings, not background buffer)
                if (keysChanged && Editor != null && Plugin.Instance?.ClipMgr?.HasActiveRecording == true)
                    Editor.OnInputChanged(currentKeys, player);

                prevKeys = currentKeys;
            }

            // Verification point every 50 frames
            if (headFrame % 50 == 0)
            {
                var body = (Rigidbody2D)ReplayState.F_body.GetValue(player);
                verifyPoints.Add(new VerifyPoint
                {
                    Frame = headFrame,
                    Position = player.transform.position,
                    Velocity = body.linearVelocity,
                });
            }

            // Checkpoint: every second + every input change, or every frame in debug mode
            bool isSecondBoundary = headFrame % (timestep > 0 ? timestep : 50) == 0;
            if (Plugin.PerFrameCheckpoints.Value || isSecondBoundary || keysChanged)
            {
                CaptureCheckpoint(player);
            }

            // Trim old data
            Trim();
        }

        /// <summary>
        /// Force a full state checkpoint at the current frame.
        /// Called on course start/stop events for exact state at boundaries.
        /// </summary>
        public void CaptureSnapshot(Movement player)
        {
            if (!running || player != trackedPlayer) return;
            CaptureCheckpoint(player);
        }

        // --- Pinning ---

        public void Pin(int frame)
        {
            if (pinnedFrames.ContainsKey(frame))
                pinnedFrames[frame]++;
            else
            {
                pinnedFrames[frame] = 1;
                pinTimestamps[frame] = Time.unscaledTime;
            }
        }

        public void Unpin(int frame)
        {
            if (!pinnedFrames.ContainsKey(frame)) return;
            pinnedFrames[frame]--;
            if (pinnedFrames[frame] <= 0)
            {
                pinnedFrames.Remove(frame);
                pinTimestamps.Remove(frame);
            }
        }

        // --- Extraction ---

        /// <summary>
        /// Extract a frame range as a self-contained ReplayFile for playback/saving.
        /// Frames are renumbered to 1-based in the output.
        /// </summary>
        public ReplayFile Extract(int startFrame, int endFrame)
        {
            // Clamp to available range
            startFrame = Mathf.Max(startFrame, tailFrame);
            endFrame = Mathf.Min(endFrame, headFrame);

            if (endFrame <= startFrame)
            {
                Log.LogWarning($"Extract: empty range [{startFrame}, {endFrame}]");
                return null;
            }

            var file = new ReplayFile
            {
                RecordedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Timestep = timestep,
                Bindings = new List<BindingSnapshot> { new BindingSnapshot
                {
                    Frame = 0,
                    ActionKeys = new Dictionary<string, List<string>>(currentBindings.ActionKeys),
                }},
            };

            // Find the best checkpoint at or before startFrame for InitialState
            ReplayCheckpoint? bestCp = null;
            for (int i = checkpoints.Count - 1; i >= 0; i--)
            {
                if (checkpoints[i].Frame <= startFrame)
                {
                    bestCp = checkpoints[i];
                    // If the best checkpoint is before startFrame, adjust startFrame back
                    // so the replay state is exact
                    if (checkpoints[i].Frame < startFrame)
                        startFrame = checkpoints[i].Frame;
                    break;
                }
            }

            if (bestCp.HasValue)
                file.InitialState = bestCp.Value.State;
            else if (checkpoints.Count > 0)
            {
                // Fallback: use earliest checkpoint
                file.InitialState = checkpoints[0].State;
                startFrame = checkpoints[0].Frame;
            }

            int frameOffset = startFrame; // subtract this to renumber to 1-based

            // Copy spans in range. Include the last span at/before startFrame for initial input state.
            int firstSpanIdx = -1;
            for (int i = spans.Count - 1; i >= 0; i--)
            {
                if (spans[i].Frame <= startFrame)
                {
                    firstSpanIdx = i;
                    break;
                }
            }

            var newSpans = new List<InputSpan>();
            int startIdx = firstSpanIdx >= 0 ? firstSpanIdx : 0;
            for (int i = startIdx; i < spans.Count; i++)
            {
                if (spans[i].Frame > endFrame) break;

                var s = spans[i];
                int newFrame = s.Frame - frameOffset;
                if (newFrame < 1) newFrame = 1;
                newSpans.Add(new InputSpan
                {
                    Frame = newFrame,
                    Keys = new HashSet<string>(s.Keys),
                    MousePos = s.MousePos,
                    XMoveAxis = s.XMoveAxis,
                });
            }
            file.Spans = newSpans;

            // Copy verify points in range, renumbered
            foreach (var vp in verifyPoints)
            {
                if (vp.Frame < startFrame) continue;
                if (vp.Frame > endFrame) break;
                file.VerifyPoints.Add(new VerifyPoint
                {
                    Frame = vp.Frame - frameOffset,
                    Position = vp.Position,
                    Velocity = vp.Velocity,
                });
            }

            // Copy checkpoints in range, renumbered with recalculated SpanIndex
            foreach (var cp in checkpoints)
            {
                if (cp.Frame < startFrame) continue;
                if (cp.Frame > endFrame) break;

                int newFrame = cp.Frame - frameOffset;
                if (newFrame < 0) newFrame = 0;

                // Recalculate SpanIndex: find the last span in newSpans at/before this new frame
                int si = 0;
                for (int j = newSpans.Count - 1; j >= 0; j--)
                {
                    if (newSpans[j].Frame <= newFrame) { si = j; break; }
                }

                file.Checkpoints.Add(new ReplayCheckpoint
                {
                    Frame = newFrame,
                    SpanIndex = si,
                    State = cp.State,
                });
            }

            // Ensure frame-0 checkpoint exists with InitialState
            if (file.Checkpoints.Count == 0 || file.Checkpoints[0].Frame != 0)
            {
                file.Checkpoints.Insert(0, new ReplayCheckpoint
                {
                    Frame = 0,
                    SpanIndex = 0,
                    State = file.InitialState,
                });
            }

            Log.LogInfo($"Extracted replay: frames [{startFrame}..{endFrame}] -> {newSpans.Count} spans, {file.Checkpoints.Count} checkpoints.");
            return file;
        }

        // --- Private helpers ---

        private void CaptureCheckpoint(Movement player)
        {
            int si = 0;
            for (int i = spans.Count - 1; i >= 0; i--)
            {
                if (spans[i].Frame <= headFrame) { si = i; break; }
            }
            checkpoints.Add(new ReplayCheckpoint
            {
                Frame = headFrame,
                SpanIndex = si,
                State = ReplayState.Capture(player),
            });
        }

        private void Trim()
        {
            int nominalTail = headFrame - capacity;

            // Respect pins: don't evict past the earliest pinned frame
            int effectiveTail = nominalTail;
            if (pinnedFrames.Count > 0)
            {
                int earliestPin = int.MaxValue;
                var expiredPins = new List<int>();

                foreach (var kvp in pinnedFrames)
                {
                    // Check for stale pins (> MaxPinDurationSeconds)
                    if (pinTimestamps.TryGetValue(kvp.Key, out float pinTime) &&
                        Time.unscaledTime - pinTime > MaxPinDurationSeconds)
                    {
                        expiredPins.Add(kvp.Key);
                        Log.LogWarning($"Auto-unpinning stale pin at frame {kvp.Key} (pinned for >{MaxPinDurationSeconds}s)");
                        Plugin.Instance?.ShowToast($"Warning: auto-released stale recording pin");
                        continue;
                    }
                    if (kvp.Key < earliestPin)
                        earliestPin = kvp.Key;
                }

                foreach (var expired in expiredPins)
                {
                    pinnedFrames.Remove(expired);
                    pinTimestamps.Remove(expired);
                }

                if (pinnedFrames.Count > 0 && earliestPin < effectiveTail)
                    effectiveTail = earliestPin;
            }

            if (effectiveTail <= tailFrame) return; // nothing to trim
            tailFrame = effectiveTail;

            // Trim spans — keep the last span before tailFrame (for input continuity)
            int keepFrom = 0;
            for (int i = 0; i < spans.Count; i++)
            {
                if (spans[i].Frame >= tailFrame) break;
                keepFrom = i;
            }
            if (keepFrom > 0)
                spans.RemoveRange(0, keepFrom);

            // Trim checkpoints
            int cpKeep = 0;
            for (int i = 0; i < checkpoints.Count; i++)
            {
                if (checkpoints[i].Frame >= tailFrame) break;
                cpKeep = i;
            }
            if (cpKeep > 0)
                checkpoints.RemoveRange(0, cpKeep);

            // Trim verify points
            int vpKeep = 0;
            for (int i = 0; i < verifyPoints.Count; i++)
            {
                if (verifyPoints[i].Frame >= tailFrame) break;
                vpKeep = i;
            }
            if (vpKeep > 0)
                verifyPoints.RemoveRange(0, vpKeep);
        }

        private BindingSnapshot CaptureBindings(Movement player, int frame)
        {
            var actionKeys = new Dictionary<string, List<string>>();

            var moveAction = (InputAction)ReplayState.F_moveAction.GetValue(player);
            var jumpAction = (InputAction)ReplayState.F_jumpAction.GetValue(player);
            var dashAction = (InputAction)ReplayState.F_dashAction.GetValue(player);

            actionKeys["Move"] = GetBoundKeys(moveAction);
            actionKeys["Jump"] = GetBoundKeys(jumpAction);
            actionKeys["Dash"] = GetBoundKeys(dashAction);

            return new BindingSnapshot { Frame = frame, ActionKeys = actionKeys };
        }

        private static List<string> GetBoundKeys(InputAction action)
        {
            var keys = new List<string>();
            if (action == null) return keys;

            for (int i = 0; i < action.bindings.Count; i++)
            {
                var binding = action.bindings[i];
                if (binding.isComposite) continue;

                var path = binding.effectivePath;
                if (!string.IsNullOrEmpty(path))
                    keys.Add(path);
            }

            return keys;
        }

        private static Key KeyCodeToKey(KeyCode kc)
        {
            if (kc >= KeyCode.F1 && kc <= KeyCode.F15)
                return Key.F1 + (kc - KeyCode.F1);

            switch (kc)
            {
                case KeyCode.Space: return Key.Space;
                case KeyCode.LeftShift: return Key.LeftShift;
                case KeyCode.RightShift: return Key.RightShift;
                case KeyCode.LeftControl: return Key.LeftCtrl;
                case KeyCode.RightControl: return Key.RightCtrl;
                case KeyCode.LeftAlt: return Key.LeftAlt;
                case KeyCode.RightAlt: return Key.RightAlt;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.Escape: return Key.Escape;
                case KeyCode.Tab: return Key.Tab;
                case KeyCode.Backspace: return Key.Backspace;
                case KeyCode.LeftArrow: return Key.LeftArrow;
                case KeyCode.RightArrow: return Key.RightArrow;
                case KeyCode.UpArrow: return Key.UpArrow;
                case KeyCode.DownArrow: return Key.DownArrow;
                default:
                    if (kc >= KeyCode.A && kc <= KeyCode.Z)
                        return Key.A + (kc - KeyCode.A);
                    return Key.None;
            }
        }
    }
}
