using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IGTAPReplay
{
    /// <summary>
    /// Records keyboard input each frame via a Harmony prefix on Movement.Update.
    /// Emits a new span only when the set of held keys changes.
    /// </summary>
    public static class ReplayRecorder
    {
        private static ManualLogSource Log => Plugin.Log;

        private static bool recording;
        private static int frameCounter;
        private static HashSet<string> prevKeys = new HashSet<string>();
        private static Vector2 prevMousePos;
        private static ReplayFile replayFile;
        private static Movement trackedPlayer;

        /// <summary>Optional editor to notify on input changes for ghost markers.</summary>
        public static ReplayEditor Editor;

        // Keys to never record (mod hotkeys)
        private static readonly HashSet<Key> IgnoredKeys = new HashSet<Key>();

        public static bool IsRecording => recording;
        public static ReplayFile CurrentFile => replayFile;
        public static int FrameCount => frameCounter;

        public static void SetIgnoredKeys(IEnumerable<KeyCode> keyCodes)
        {
            IgnoredKeys.Clear();
            foreach (var kc in keyCodes)
            {
                var key = KeyCodeToKey(kc);
                if (key != Key.None)
                    IgnoredKeys.Add(key);
            }
        }

        public static void StartRecording(Movement player)
        {
            if (recording) return;
            if (player == null)
            {
                Log.LogError("Cannot record: no player found.");
                return;
            }

            if (Time.captureFramerate <= 0)
                Log.LogWarning("Fixed timestep mod not active! Replay may not be deterministic.");

            trackedPlayer = player;
            frameCounter = 0;
            prevKeys.Clear();
            prevMousePos = Vector2.zero;

            var initialState = ReplayState.Capture(player);
            replayFile = new ReplayFile
            {
                RecordedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Timestep = Time.captureFramerate > 0 ? Time.captureFramerate : 50,
                InitialState = initialState,
                Bindings = new List<BindingSnapshot> { CaptureBindings(player, 0) },
                Checkpoints = new List<ReplayCheckpoint>
                {
                    new ReplayCheckpoint { Frame = 0, SpanIndex = 0, State = initialState }
                },
            };

            recording = true;
            Log.LogInfo("Recording started.");
        }

        public static ReplayFile StopRecording()
        {
            if (!recording) return null;

            // Final checkpoint at the end of recording (skip if one already exists at this frame)
            if (trackedPlayer != null &&
                (replayFile.Checkpoints.Count == 0 || replayFile.Checkpoints[replayFile.Checkpoints.Count - 1].Frame != frameCounter))
                CaptureCheckpoint(trackedPlayer);

            recording = false;
            trackedPlayer = null;

            Log.LogInfo($"Recording stopped. {replayFile.Spans.Count} spans, {frameCounter} frames.");
            return replayFile;
        }

        /// <summary>
        /// Called from Harmony prefix on Movement.Update, before the game reads input.
        /// </summary>
        public static void OnFrame(Movement player)
        {
            if (!recording || player != trackedPlayer) return;

            frameCounter++;
            if (frameCounter <= 3)
            {
                var body = (Rigidbody2D)ReplayState.F_body.GetValue(player);
                Plugin.DbgLog($"REC OnFrame fc={frameCounter} dt={Time.deltaTime:F4} vel=({body.linearVelocityX:F1},{body.linearVelocityY:F1}) pos=({body.position.x:F1},{body.position.y:F1})");
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
                    if (IgnoredKeys.Contains(key)) continue;

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
            bool keysChanged = !currentKeys.SetEquals(prevKeys);
            if (keysChanged || mouseMoved)
            {
                // Compute xMoveAxis the same way Movement does from the input
                float xma = 0f;
                if (keyboard != null)
                {
                    var moveAction = (UnityEngine.InputSystem.InputAction)ReplayState.F_moveAction.GetValue(player);
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
                    Frame = frameCounter,
                    Keys = new HashSet<string>(currentKeys),
                    XMoveAxis = xma,
                };

                if (mouseMoved)
                {
                    span.MousePos = currentMousePos;
                    prevMousePos = currentMousePos;
                }

                replayFile.Spans.Add(span);

                // Notify editor to spawn ghost marker
                if (keysChanged && Editor != null)
                    Editor.OnInputChanged(currentKeys, player);

                prevKeys = currentKeys;
            }

            // Verification point every 50 frames
            if (frameCounter % 50 == 0)
            {
                var body = (Rigidbody2D)ReplayState.F_body.GetValue(player);
                replayFile.VerifyPoints.Add(new VerifyPoint
                {
                    Frame = frameCounter,
                    Position = player.transform.position,
                    Velocity = body.linearVelocity,
                });
            }

            // Checkpoint: every second + every input change, or every frame in debug mode
            bool isSecondBoundary = frameCounter % (replayFile.Timestep > 0 ? replayFile.Timestep : 50) == 0;
            if (Plugin.PerFrameCheckpoints.Value || isSecondBoundary || keysChanged)
            {
                CaptureCheckpoint(player);
            }
        }

        private static void CaptureCheckpoint(Movement player)
        {
            int si = 0;
            for (int i = replayFile.Spans.Count - 1; i >= 0; i--)
            {
                if (replayFile.Spans[i].Frame <= frameCounter) { si = i; break; }
            }
            replayFile.Checkpoints.Add(new ReplayCheckpoint
            {
                Frame = frameCounter,
                SpanIndex = si,
                State = ReplayState.Capture(player),
            });
        }

        private static BindingSnapshot CaptureBindings(Movement player, int frame)
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

                // Store the full effective path as-is, e.g. "<Keyboard>/space", "<Mouse>/leftButton"
                var path = binding.effectivePath;
                if (!string.IsNullOrEmpty(path))
                    keys.Add(path);
            }

            return keys;
        }

        /// <summary>
        /// Convert a Key enum to a lowercase string name for the replay file.
        /// </summary>
        public static string KeyName(Key key)
        {
            // Map common keys to short readable names
            switch (key)
            {
                case Key.Space: return "space";
                case Key.LeftShift: return "lshift";
                case Key.RightShift: return "rshift";
                case Key.LeftCtrl: return "lctrl";
                case Key.RightCtrl: return "rctrl";
                case Key.LeftAlt: return "lalt";
                case Key.RightAlt: return "ralt";
                case Key.Enter: return "enter";
                case Key.Escape: return "escape";
                case Key.Tab: return "tab";
                case Key.Backspace: return "backspace";
                case Key.LeftArrow: return "left";
                case Key.RightArrow: return "right";
                case Key.UpArrow: return "up";
                case Key.DownArrow: return "down";
                default: return key.ToString().ToLower();
            }
        }

        /// <summary>
        /// Convert a string key name back to a Key enum for playback.
        /// </summary>
        public static Key ParseKeyName(string name)
        {
            switch (name)
            {
                case "space": return Key.Space;
                case "lshift": return Key.LeftShift;
                case "rshift": return Key.RightShift;
                case "lctrl": return Key.LeftCtrl;
                case "rctrl": return Key.RightCtrl;
                case "lalt": return Key.LeftAlt;
                case "ralt": return Key.RightAlt;
                case "enter": return Key.Enter;
                case "escape": return Key.Escape;
                case "tab": return Key.Tab;
                case "backspace": return Key.Backspace;
                case "left": return Key.LeftArrow;
                case "right": return Key.RightArrow;
                case "up": return Key.UpArrow;
                case "down": return Key.DownArrow;
                default:
                    if (Enum.TryParse(name, ignoreCase: true, out Key k))
                        return k;
                    return Key.None;
            }
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
                    // For letter keys, KeyCode.A=97, Key.A is enum-specific
                    if (kc >= KeyCode.A && kc <= KeyCode.Z)
                        return Key.A + (kc - KeyCode.A);
                    return Key.None;
            }
        }
    }
}
