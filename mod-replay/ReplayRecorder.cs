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

            replayFile = new ReplayFile
            {
                RecordedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Timestep = Time.captureFramerate > 0 ? Time.captureFramerate : 50,
                InitialState = ReplayState.Capture(player),
                Bindings = new List<BindingSnapshot> { CaptureBindings(player, 0) },
            };

            recording = true;
            Log.LogInfo("Recording started.");
        }

        public static ReplayFile StopRecording()
        {
            if (!recording) return null;
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
                            currentKeys.Add(KeyName(key));
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
                if (mouse.leftButton.isPressed) currentKeys.Add("mouse0");
                if (mouse.rightButton.isPressed) currentKeys.Add("mouse1");
                if (mouse.middleButton.isPressed) currentKeys.Add("mouse2");
                if (mouse.forwardButton.isPressed) currentKeys.Add("mouse3");
                if (mouse.backButton.isPressed) currentKeys.Add("mouse4");
            }

            // Mouse position (screen coords) — only if enabled in config
            bool trackMousePos = Plugin.RecordMousePosition.Value;
            Vector2 currentMousePos = (trackMousePos && mouse != null) ? mouse.position.ReadValue() : Vector2.zero;
            bool mouseMoved = trackMousePos && Vector2.Distance(currentMousePos, prevMousePos) > 0.5f;

            // Emit a span if keys or mouse position changed
            bool keysChanged = !currentKeys.SetEquals(prevKeys);
            if (keysChanged || mouseMoved)
            {
                var span = new InputSpan
                {
                    Frame = frameCounter,
                    Keys = new HashSet<string>(currentKeys),
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

                // Extract the key name from the path, e.g. "<Keyboard>/space" -> "space"
                var path = binding.effectivePath;
                if (string.IsNullOrEmpty(path)) continue;
                var slash = path.LastIndexOf('/');
                if (slash >= 0)
                    keys.Add(path.Substring(slash + 1).ToLower());
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
