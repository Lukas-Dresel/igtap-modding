using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace IGTAPReplay
{
    /// <summary>
    /// Plays back a replay file by injecting keyboard input via a virtual keyboard device.
    /// Runs with DefaultExecutionOrder(-100) to ensure it fires before Movement.Update.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class ReplayPlayback : MonoBehaviour
    {
        private static ManualLogSource Log => Plugin.Log;

        private Keyboard virtualKeyboard;
        private Mouse virtualMouse;
        private Movement player;
        private ReplayFile replayFile;
        private int frameCounter;
        private int spanIndex;
        private int verifyIndex;
        private bool playing;
        private bool finished;
        private bool paused;
        private bool seeking;
        private int seekTarget;
        private Vector2 lastMousePos;

        public bool IsPlaying => playing;
        public bool IsFinished => finished;
        public bool IsSeeking => seeking;
        public bool IsPaused { get => paused; set => paused = value; }
        public int FrameCount => frameCounter;
        public ReplayFile File => replayFile;
        public int SpanIndex => spanIndex;

        public void StartPlayback(Movement targetPlayer, ReplayFile file)
        {
            if (playing) return;
            if (targetPlayer == null || file == null)
            {
                Log.LogError("Cannot play: missing player or replay file.");
                return;
            }

            if (Time.captureFramerate <= 0)
                Log.LogWarning("Fixed timestep mod not active! Replay may not be deterministic.");

            player = targetPlayer;
            replayFile = file;
            frameCounter = 0;
            spanIndex = 0;
            verifyIndex = 0;
            finished = false;

            // Restore initial state
            ReplayState.Restore(player, replayFile.InitialState);

            // Cancel any pending invokes from previous gameplay
            player.CancelInvoke();

            lastMousePos = Vector2.zero;

            // Create virtual devices
            virtualKeyboard = InputSystem.AddDevice<Keyboard>("ReplayKeyboard");
            virtualMouse = InputSystem.AddDevice<Mouse>("ReplayMouse");
            Log.LogInfo($"Virtual keyboard created: {virtualKeyboard.name} (id={virtualKeyboard.deviceId})");
            Log.LogInfo($"Virtual mouse created: {virtualMouse.name} (id={virtualMouse.deviceId})");

            // Rebind game actions to virtual keyboard
            RebindActions();

            playing = true;
            Log.LogInfo($"Playback started. {replayFile.Spans.Count} spans to play.");
        }

        public void StopPlayback()
        {
            if (!playing) return;
            playing = false;

            // Clear device state and remove
            if (virtualKeyboard != null)
            {
                InputSystem.QueueStateEvent(virtualKeyboard, new KeyboardState());
                InputSystem.RemoveDevice(virtualKeyboard);
                virtualKeyboard = null;
            }
            if (virtualMouse != null)
            {
                InputSystem.RemoveDevice(virtualMouse);
                virtualMouse = null;
            }

            // Restore original bindings
            ResetBindings();

            player = null;
            replayFile = null;

            Log.LogInfo("Playback stopped.");
        }

        /// <summary>
        /// Play until a target frame, then pause. Restarts from the beginning
        /// if the target is behind the current position. Plays at normal speed
        /// so physics simulates correctly.
        /// </summary>
        public void PlayUntilFrame(int targetFrame)
        {
            if (!playing || player == null || replayFile == null) return;
            targetFrame = Mathf.Max(1, targetFrame);
            finished = false;

            if (targetFrame <= frameCounter)
            {
                // Must restart from the beginning
                ReplayState.Restore(player, replayFile.InitialState);
                player.CancelInvoke();
                frameCounter = 0;
                spanIndex = 0;
                verifyIndex = 0;
                lastMousePos = Vector2.zero;
            }

            // Set target and unpause — Update will advance normally and pause when reached
            seeking = true;
            seekTarget = targetFrame;
            paused = false;
        }

        private void Update()
        {
            if (!playing || player == null || virtualKeyboard == null) return;

            // Don't advance while paused or finished (but seeking overrides pause)
            if (!seeking && (finished || paused)) return;

            frameCounter++;
            AdvanceSpanIndex();
            InjectCurrentFrame();

            // Reached seek target — pause
            if (seeking && frameCounter >= seekTarget)
            {
                seeking = false;
                paused = true;
                Time.timeScale = 0f;
                return;
            }

            // Notify editor for replay ghosts
            var editor = FindAnyObjectByType<ReplayEditor>();
            if (editor != null)
                editor.OnPlaybackFrame(frameCounter, player);

            // Desync detection at verification points
            CheckVerifyPoints();

            // At end of replay, pause in place (don't tear down)
            bool pastAllSpans = replayFile.Spans.Count == 0 ||
                (spanIndex >= replayFile.Spans.Count - 1 &&
                 frameCounter > replayFile.Spans[replayFile.Spans.Count - 1].Frame + 50);

            if (pastAllSpans && !finished)
            {
                seeking = false;
                finished = true;
                Log.LogInfo($"Playback reached end at frame {frameCounter}.");
                Plugin.Instance.OnPlaybackFinished();
            }
        }

        private void AdvanceSpanIndex()
        {
            while (spanIndex + 1 < replayFile.Spans.Count &&
                   replayFile.Spans[spanIndex + 1].Frame <= frameCounter)
            {
                spanIndex++;
            }
        }

        private void InjectCurrentFrame()
        {
            if (virtualKeyboard == null) return;

            var keyboardState = new KeyboardState();
            var mouseState = new MouseState();
            bool hasMouseButtons = false;

            if (spanIndex < replayFile.Spans.Count &&
                replayFile.Spans[spanIndex].Frame <= frameCounter)
            {
                var span = replayFile.Spans[spanIndex];

                foreach (var keyName in span.Keys)
                {
                    switch (keyName)
                    {
                        case "mouse0":
                            mouseState = mouseState.WithButton(MouseButton.Left);
                            hasMouseButtons = true;
                            continue;
                        case "mouse1":
                            mouseState = mouseState.WithButton(MouseButton.Right);
                            hasMouseButtons = true;
                            continue;
                        case "mouse2":
                            mouseState = mouseState.WithButton(MouseButton.Middle);
                            hasMouseButtons = true;
                            continue;
                        case "mouse3":
                            mouseState = mouseState.WithButton(MouseButton.Forward);
                            hasMouseButtons = true;
                            continue;
                        case "mouse4":
                            mouseState = mouseState.WithButton(MouseButton.Back);
                            hasMouseButtons = true;
                            continue;
                    }

                    var key = ReplayRecorder.ParseKeyName(keyName);
                    if (key != Key.None)
                        keyboardState.Set(key, true);
                }

                if (span.MousePos.HasValue)
                    lastMousePos = span.MousePos.Value;
            }

            InputSystem.QueueStateEvent(virtualKeyboard, keyboardState, InputState.currentTime);

            if (hasMouseButtons || lastMousePos != Vector2.zero)
            {
                mouseState.position = lastMousePos;
                InputSystem.QueueStateEvent(virtualMouse, mouseState, InputState.currentTime);
            }
        }

        private void CheckVerifyPoints()
        {
            while (verifyIndex < replayFile.VerifyPoints.Count &&
                   replayFile.VerifyPoints[verifyIndex].Frame <= frameCounter)
            {
                var vp = replayFile.VerifyPoints[verifyIndex];
                if (vp.Frame == frameCounter)
                {
                    var body = (Rigidbody2D)ReplayState.F_body.GetValue(player);
                    Vector2 actualPos = player.transform.position;
                    Vector2 actualVel = body.linearVelocity;

                    float posDelta = Vector2.Distance(actualPos, vp.Position);
                    float velDelta = Vector2.Distance(actualVel, vp.Velocity);

                    if (posDelta > 1.0f)
                    {
                        Log.LogWarning($"DESYNC at frame {vp.Frame}: " +
                            $"pos expected ({vp.Position.x:F1},{vp.Position.y:F1}) " +
                            $"got ({actualPos.x:F1},{actualPos.y:F1}) delta={posDelta:F1}");
                    }
                    if (velDelta > 10f)
                    {
                        Log.LogWarning($"DESYNC at frame {vp.Frame}: " +
                            $"vel expected ({vp.Velocity.x:F1},{vp.Velocity.y:F1}) " +
                            $"got ({actualVel.x:F1},{actualVel.y:F1}) delta={velDelta:F1}");
                    }
                }
                verifyIndex++;
            }
        }

        private void RebindActions()
        {
            string vkPath = "/" + virtualKeyboard.name;

            // Use the bindings from the replay file to determine what keys map to what actions
            BindingSnapshot bindings = replayFile.Bindings.Count > 0
                ? replayFile.Bindings[0]
                : DefaultBindings();

            var moveAction = (InputAction)ReplayState.F_moveAction.GetValue(player);
            var jumpAction = (InputAction)ReplayState.F_jumpAction.GetValue(player);
            var dashAction = (InputAction)ReplayState.F_dashAction.GetValue(player);

            // Rebind Move composite (WASD)
            if (bindings.ActionKeys.TryGetValue("Move", out var moveKeys))
            {
                var partPaths = new Dictionary<string, string>();
                foreach (var k in moveKeys)
                {
                    // Map key names to composite part names
                    switch (k)
                    {
                        case "w": partPaths["up"] = $"{vkPath}/w"; break;
                        case "s": partPaths["down"] = $"{vkPath}/s"; break;
                        case "a": partPaths["left"] = $"{vkPath}/a"; break;
                        case "d": partPaths["right"] = $"{vkPath}/d"; break;
                        // Arrow keys
                        case "uparrow": partPaths["up"] = $"{vkPath}/upArrow"; break;
                        case "downarrow": partPaths["down"] = $"{vkPath}/downArrow"; break;
                        case "leftarrow": partPaths["left"] = $"{vkPath}/leftArrow"; break;
                        case "rightarrow": partPaths["right"] = $"{vkPath}/rightArrow"; break;
                    }
                }
                RebindComposite(moveAction, partPaths);
            }

            // Rebind Jump
            if (bindings.ActionKeys.TryGetValue("Jump", out var jumpKeys) && jumpKeys.Count > 0)
                RebindSimple(jumpAction, $"{vkPath}/{jumpKeys[0]}");

            // Rebind Dash
            if (bindings.ActionKeys.TryGetValue("Dash", out var dashKeys) && dashKeys.Count > 0)
                RebindSimple(dashAction, $"{vkPath}/{dashKeys[0]}");
        }

        private static BindingSnapshot DefaultBindings()
        {
            return new BindingSnapshot
            {
                Frame = 0,
                ActionKeys = new Dictionary<string, List<string>>
                {
                    { "Move", new List<string> { "w", "a", "s", "d" } },
                    { "Jump", new List<string> { "space" } },
                    { "Dash", new List<string> { "leftShift" } },
                }
            };
        }

        private void ResetBindings()
        {
            if (player == null) return;

            ResetAction(ReplayState.F_moveAction);
            ResetAction(ReplayState.F_jumpAction);
            ResetAction(ReplayState.F_dashAction);
        }

        private void ResetAction(FieldInfo field)
        {
            var action = field.GetValue(player) as InputAction;
            if (action == null) return;
            bool wasEnabled = action.enabled;
            action.Disable();
            action.RemoveAllBindingOverrides();
            if (wasEnabled) action.Enable();
        }

        private static void RebindComposite(InputAction action, Dictionary<string, string> partPaths)
        {
            if (action == null) return;
            bool wasEnabled = action.enabled;
            action.Disable();

            for (int i = 0; i < action.bindings.Count; i++)
            {
                var binding = action.bindings[i];
                if (!binding.isPartOfComposite) continue;
                string partName = binding.name.ToLower();
                if (partPaths.TryGetValue(partName, out string newPath))
                    action.ApplyBindingOverride(i, new InputBinding { overridePath = newPath });
            }

            if (wasEnabled) action.Enable();
        }

        private static void RebindSimple(InputAction action, string newPath)
        {
            if (action == null) return;
            bool wasEnabled = action.enabled;
            action.Disable();

            for (int i = 0; i < action.bindings.Count; i++)
            {
                if (action.bindings[i].isComposite) continue;
                action.ApplyBindingOverride(i, new InputBinding { overridePath = newPath });
            }

            if (wasEnabled) action.Enable();
        }

        private void OnDestroy()
        {
            if (playing)
                StopPlayback();
        }
    }
}
