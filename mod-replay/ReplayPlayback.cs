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
        private Gamepad virtualGamepad;
        private Mouse savedMouse;
        private Keyboard realKeyboard;
        private Mouse realMouse;
        private Gamepad realGamepad;
        private readonly List<InputDevice> disabledDevices = new List<InputDevice>();

        // Names of our virtual devices so we can skip them when disabling
        private static readonly HashSet<string> VirtualDeviceNames = new HashSet<string>
            { "ReplayKeyboard", "ReplayMouse", "ReplayGamepad" };
        private Movement player;
        private ReplayFile replayFile;
        private int frameCounter;
        private int spanIndex;
        private int verifyIndex;
        private bool playing;
        private bool finished;
        private bool paused;
        private Vector2 lastMousePos;
        private HashSet<string> prevInjectedKeys = new HashSet<string>();

        // Seeking
        private bool seeking;
        private int seekTarget;

        /// <summary>If > 0, auto-pause when frameCounter reaches this value.</summary>
        public int PauseOnFrame;

        // Checkpoint validation index
        private int checkpointIndex;

        // --- Reverify state (see BeginReverifyFromFrame) ---
        private bool reverifying;
        private int reverifyDirtyFrame; // first frame where edits diverge
        private int reverifyCaptureEveryN = 50; // default: every second at 50fps
        private int reverifyLastCapturedFrame = -1;
        public bool IsReverifying => reverifying;

        // (post-restore contact cache correction is handled at the physics level
        // in ReplayState.Restore via double Physics2D.Simulate(0) calls)

        // Saved binding overrides for ALL actions, to restore on stop
        private readonly Dictionary<System.Guid, List<string>> savedOverrides = new Dictionary<System.Guid, List<string>>();

        // Pending restore: set by external callers (StartPlayback, SeekToFrame,
        // BeginReverifyFromFrame). Consumed by PostMovementUpdate at the next
        // postfix tick. We restore in postfix (not prefix) so that the next
        // FixedUpdate naturally integrates from the restored body state — same
        // lifecycle as during recording (POST fc=N → FixedUpdate → PRE fc=N+1).
        // -1 = no pending restore. >=0 = restore from checkpoint at/before this frame.
        private int pendingRestoreFrame = -1;
        // Whether the pending restore is from a seek (auto-pause when target reached)
        private bool pendingRestoreSeek;
        private int pendingRestoreSeekTarget;

        // Warmup state: while > 0, Movement.Update + FixedUpdate run normally
        // (inputs are injected, gravity applies, contacts form). PostMovementUpdate
        // force-snaps body.position/linearVelocity back to the live trail each
        // tick so drift from cold-cache physics doesn't accumulate. Validation,
        // ghost updates, and seek/end-of-replay detection are suppressed until
        // warmup completes.
        private int warmupFramesRemaining;
        private const int WarmupFrames = 3;
        private bool warmupUsingTrail; // true if liveTrail has entries for the warmup range
        private int warmupLastSeenFrame = -1; // detect postfix calls without a frame advance

        // Live trail: runtime-only cache of body.position / body.linearVelocity
        // captured at every postfix tick during normal playback. Used by the
        // kinematic warmup path: to seek to frame T, we restore to a full
        // checkpoint C, then for frames [C+1, T] drive the body from liveTrail[i]
        // so Box2D builds contact manifolds against the recording's trajectory.
        //
        // Invalidated (truncated) by ReplayPlayback.InvalidateTrailFrom when an
        // edit happens. Not persisted to disk — rebuilt as playback progresses.
        private struct BodyTrailEntry { public Vector2 Pos; public Vector2 Vel; }
        private readonly List<BodyTrailEntry> liveTrail = new List<BodyTrailEntry>();
        private int liveTrailValidThrough = -1;

        private void WriteTrail(int frame, Vector2 pos, Vector2 vel)
        {
            while (liveTrail.Count <= frame) liveTrail.Add(default);
            liveTrail[frame] = new BodyTrailEntry { Pos = pos, Vel = vel };
            if (frame > liveTrailValidThrough) liveTrailValidThrough = frame;
        }

        /// <summary>
        /// Invalidate trail entries at or after `frame`. Called when an edit
        /// modifies inputs: anything from the edit onward can no longer be
        /// trusted to match the recorded physics trajectory.
        /// </summary>
        public void InvalidateTrailFrom(int frame)
        {
            if (frame <= liveTrailValidThrough)
            {
                liveTrailValidThrough = frame - 1;
                Plugin.DbgLog($"InvalidateTrailFrom({frame}): validThrough={liveTrailValidThrough}");
            }
        }

        // Pending unpause: set by editor.Update, executed by Movement prefix
        private bool pendingUnpause;
        private int pendingPauseOnFrame;

        /// <summary>
        /// Called from Harmony prefix on Movement.Update. Returns false to skip the update.
        /// </summary>
        /// <summary>
        /// Called by the editor to request an unpause. The actual unpause happens
        /// inside the Movement.Update prefix — the correct lifecycle point.
        /// </summary>
        public void RequestUnpause(int pauseOnFrame)
        {
            pendingUnpause = true;
            pendingPauseOnFrame = pauseOnFrame;
            Plugin.DbgLog($"RequestUnpause: pauseOnFrame={pauseOnFrame}");
        }

        public bool ShouldMovementUpdate()
        {
            // Restore is now handled in PostMovementUpdate (postfix), not here.
            // Movement.Update always runs normally — this matches recording's
            // lifecycle exactly and avoids skipping any internal game state.

            // Execute pending unpause HERE, at the exact right lifecycle point.
            // Only execute when dt > 0, otherwise Movement would be a no-op.
            // Setting timeScale=1 here ensures the NEXT frame has dt > 0.
            if (pendingUnpause)
            {
                if (Time.deltaTime == 0f)
                {
                    // Not ready yet — ensure timeScale is set for next frame
                    Time.timeScale = 1f;
                    Plugin.DbgLog("ShouldMovementUpdate: pending unpause waiting (dt=0)");
                    return false;
                }
                pendingUnpause = false;
                PauseOnFrame = pendingPauseOnFrame;
                paused = false;
                Plugin.DbgLog($"ShouldMovementUpdate: executed pending unpause, PauseOnFrame={PauseOnFrame}");
                return true;
            }

            // When paused (and not seeking), skip Movement.Update entirely
            if (paused && !seeking)
            {
                Plugin.DbgLog($"ShouldMovementUpdate: SKIPPED (paused)");
                return false;
            }
            // Skip dt=0 frames (timeScale just switched)
            if (Time.deltaTime == 0f)
            {
                Plugin.DbgLog("ShouldMovementUpdate: SKIPPED (dt=0)");
                return false;
            }
            return true;
        }

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
            seeking = false;
            checkpointIndex = 0;

            // Defer restore to the next Movement.Update postfix. This way the
            // following FixedUpdate naturally integrates from the restored body
            // state — same lifecycle as during recording.
            pendingRestoreFrame = 0;
            pendingRestoreSeek = false;

            // Reset live trail — it will be populated as playback progresses.
            liveTrail.Clear();
            liveTrailValidThrough = -1;
            warmupFramesRemaining = 0;
            warmupUsingTrail = false;

            lastMousePos = Vector2.zero;
            prevInjectedKeys.Clear();

            // Remove real devices so they can't interfere during playback.
            // UnityEngine.Input (old API) still works for F7 since it reads from OS.
            realKeyboard = Keyboard.current;
            realMouse = Mouse.current;
            realGamepad = Gamepad.current;
            savedMouse = realMouse;
            if (realKeyboard != null) InputSystem.RemoveDevice(realKeyboard);
            if (realMouse != null) InputSystem.RemoveDevice(realMouse);
            if (realGamepad != null) InputSystem.RemoveDevice(realGamepad);

            // Create virtual devices
            virtualKeyboard = InputSystem.AddDevice<Keyboard>("ReplayKeyboard");
            virtualMouse = InputSystem.AddDevice<Mouse>("ReplayMouse");
            virtualGamepad = InputSystem.AddDevice<Gamepad>("ReplayGamepad");
            Log.LogInfo($"Virtual devices created: keyboard={virtualKeyboard.deviceId} mouse={virtualMouse.deviceId} gamepad={virtualGamepad.deviceId}");

            // Rebind game actions to virtual devices
            RebindActions();

            playing = true;

            var dbgBody = (Rigidbody2D)ReplayState.F_body.GetValue(player);
            Plugin.DbgLog($"StartPlayback: playing=true pendingRestore=0 fc={frameCounter} dt={Time.deltaTime:F4} vel=({dbgBody.linearVelocityX:F1},{dbgBody.linearVelocityY:F1}) pos=({dbgBody.position.x:F1},{dbgBody.position.y:F1})");

            Log.LogInfo($"Playback started. {replayFile.Spans.Count} spans to play.");
        }

        public void StopPlayback()
        {
            if (!playing) return;
            playing = false;
            reverifying = false;

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
            if (virtualGamepad != null)
            {
                InputSystem.RemoveDevice(virtualGamepad);
                virtualGamepad = null;
            }

            // Re-add real devices
            if (realKeyboard != null) { try { InputSystem.AddDevice(realKeyboard); } catch {} }
            if (realMouse != null) { try { InputSystem.AddDevice(realMouse); } catch {} }
            if (realGamepad != null) { try { InputSystem.AddDevice(realGamepad); } catch {} }
            realKeyboard = null;
            realMouse = null;
            realGamepad = null;
            savedMouse = null;

            // Restore original bindings
            ResetBindings();

            player = null;
            replayFile = null;

            Log.LogInfo("Playback stopped.");
        }

        /// <summary>
        /// Seek to a target frame. Runs the actual game to get there.
        /// Forward: sets a target and lets Update() advance naturally.
        /// Backward: restores the nearest checkpoint before target, then forward-seeks.
        /// The game stays unpaused while seeking; auto-pauses when target is reached.
        /// </summary>
        public void SeekToFrame(int targetFrame)
        {
            if (!playing || player == null || replayFile == null) return;
            targetFrame = Mathf.Max(1, targetFrame);
            finished = false;

            Plugin.DbgLog($"SeekToFrame target={targetFrame} current={frameCounter}");

            if (targetFrame <= frameCounter)
            {
                // Defer restore to the next postfix, where the FixedUpdate→
                // Movement.Update lifecycle naturally integrates from restored state.
                pendingRestoreFrame = targetFrame;
                pendingRestoreSeek = true;
                pendingRestoreSeekTarget = targetFrame;
                Plugin.DbgLog($"SeekToFrame: pendingRestore={targetFrame}");
            }
            else
            {
                seeking = true;
                seekTarget = targetFrame;
            }
            paused = false;
            // Previous auto-pause left timeScale=0. Without this, dt stays 0,
            // Movement.Update skips, frameCounter never advances, and the warmup
            // countdown drains on postfix-only ticks without actually warming.
            Time.timeScale = 1f;
            Plugin.DbgLog($"SeekToFrame set seeking={seeking} target={seekTarget} paused=false timeScale={Time.timeScale}");
        }

        /// <summary>
        /// Warmup-aware restore: restores from a checkpoint at/before
        /// (target - WarmupFrames), then returns how many frames of silent
        /// replay are needed to reach `target`. Caller sets
        /// warmupFramesRemaining to the returned value.
        /// </summary>
        private int RestoreWithWarmup(int target)
        {
            int preWarmupFrame = Mathf.Max(0, target - WarmupFrames);
            var cps = replayFile.Checkpoints;

            // Find the latest checkpoint at/before preWarmupFrame
            int idx = -1;
            for (int i = cps.Count - 1; i >= 0; i--)
            {
                if (cps[i].Frame <= preWarmupFrame) { idx = i; break; }
            }
            // Fallback: if no checkpoint ≤ preWarmupFrame but we do have checkpoints,
            // use the earliest. This happens when target is very small (e.g. 0 or 1).
            if (idx < 0 && cps.Count > 0) idx = 0;

            int restoredFrame = (idx >= 0 && idx < cps.Count) ? cps[idx].Frame : 0;
            // If the best checkpoint is already > target, don't overshoot — clamp.
            if (restoredFrame > target) restoredFrame = target;

            RestoreCheckpointBefore(restoredFrame);
            int warmupNeeded = Mathf.Max(0, target - restoredFrame);
            return warmupNeeded;
        }

        private void RestoreCheckpointBefore(int targetFrame)
        {
            var cps = replayFile.Checkpoints;

            // Find the latest checkpoint at or before the target
            int bestIdx = -1;
            for (int i = cps.Count - 1; i >= 0; i--)
            {
                if (cps[i].Frame <= targetFrame)
                {
                    bestIdx = i;
                    break;
                }
            }

            if (bestIdx >= 0)
            {
                var cp = cps[bestIdx];
                ReplayState.Restore(player, cp.State);
                player.CancelInvoke();
                frameCounter = cp.Frame;
                spanIndex = cp.SpanIndex;
                // Recalculate verify index
                verifyIndex = 0;
                for (int i = 0; i < replayFile.VerifyPoints.Count; i++)
                {
                    if (replayFile.VerifyPoints[i].Frame <= cp.Frame)
                        verifyIndex = i + 1;
                    else break;
                }
                lastMousePos = Vector2.zero;
                // Reset checkpoint validation index
                checkpointIndex = 0;
                for (int i = 0; i < replayFile.Checkpoints.Count; i++)
                {
                    if (replayFile.Checkpoints[i].Frame <= cp.Frame)
                        checkpointIndex = i + 1;
                    else break;
                }
                Log.LogInfo($"Restored checkpoint at frame {cp.Frame}");
            }
            else
            {
                // No checkpoint found, restart from the very beginning
                ReplayState.Restore(player, replayFile.InitialState);
                player.CancelInvoke();
                frameCounter = 0;
                spanIndex = 0;
                verifyIndex = 0;
                checkpointIndex = 0;
                lastMousePos = Vector2.zero;
                Log.LogInfo("Restored to initial state (no earlier checkpoint)");
            }
        }

        /// <summary>
        /// Called from Harmony prefix on Movement.Update. Advances frame, injects input,
        /// forces InputSystem to process it immediately.
        /// </summary>
        public void InjectCurrentFrame()
        {
            if (!playing || player == null || virtualKeyboard == null) return;
            if (!seeking && (finished || paused))
            {
                Plugin.DbgLog($"InjectCurrentFrame SKIPPED fc={frameCounter} seeking={seeking} finished={finished} paused={paused} timeScale={Time.timeScale}");
                return;
            }
            // Skip frames with deltaTime=0 (happens when timeScale switches mid-visual-frame).
            if (Time.deltaTime == 0f)
            {
                Plugin.DbgLog($"InjectCurrentFrame SKIPPED fc={frameCounter} dt=0");
                return;
            }

            // FULL state dump at PREFIX ENTRY — what FixedUpdate left us with
            Plugin.DbgLog($"PLAY-PRE-ENTRY fc={frameCounter + 1} dt={Time.deltaTime:F4} :: {ReplayState.DumpAllFields(player)}");

            frameCounter++;
            AdvanceSpanIndex();
            InjectForFrame(frameCounter);
            InputSystem.Update();

            // Override xMoveAxis from the recorded span value to handle opposing-key
            // composite resolution differences (e.g. a+d simultaneously)
            if (spanIndex < replayFile.Spans.Count && replayFile.Spans[spanIndex].Frame <= frameCounter)
                ReplayState.F_xMoveAxis.SetValue(player, replayFile.Spans[spanIndex].XMoveAxis);

            var moveAct = (UnityEngine.InputSystem.InputAction)ReplayState.F_moveAction.GetValue(player);
            var moveVal = moveAct.ReadValue<Vector2>();
            Plugin.DbgLog($"InjectCurrentFrame fc={frameCounter} seeking={seeking} seekTarget={seekTarget} paused={paused} timeScale={Time.timeScale} dt={Time.deltaTime:F4} moveVal=({moveVal.x:F1},{moveVal.y:F1}) xma={replayFile.Spans[spanIndex].XMoveAxis}");

            // FULL state dump AFTER input injection — what Movement.Update will actually see
            Plugin.DbgLog($"PLAY-PRE fc={frameCounter} :: {ReplayState.DumpAllFields(player)}");
        }

        /// <summary>
        /// Begin an uninterruptible re-verify pass: seek to the nearest surviving
        /// checkpoint at or before <paramref name="fromFrame"/>, then run normal
        /// playback to the end of the replay while appending fresh checkpoints
        /// and verify points. All user controls must be locked by the editor
        /// for the duration.
        /// </summary>
        public void BeginReverifyFromFrame(int fromFrame)
        {
            if (!playing || player == null || replayFile == null) return;

            // Derive capture cadence from the spacing of surviving checkpoints.
            // If we have at least two, use their average gap. Otherwise default
            // to one per second. Clamp to [1, 600].
            int cadence = 50;
            var cps = replayFile.Checkpoints;
            if (cps.Count >= 2)
            {
                int sum = 0;
                int n = 0;
                for (int i = 1; i < cps.Count; i++)
                {
                    int d = cps[i].Frame - cps[i - 1].Frame;
                    if (d > 0) { sum += d; n++; }
                }
                if (n > 0) cadence = Mathf.Clamp(sum / n, 1, 600);
            }
            reverifyCaptureEveryN = cadence;
            reverifyLastCapturedFrame = -1;
            reverifyDirtyFrame = fromFrame;

            // Defer restore to the next postfix.
            finished = false;
            pendingRestoreFrame = 0;
            pendingRestoreSeek = false;

            reverifying = true;
            seeking = false;
            seekTarget = 0;
            paused = false;
            PauseOnFrame = 0;
            Time.timeScale = 1f;
            Plugin.DbgLog($"BeginReverifyFromFrame fromFrame={fromFrame} cadence={cadence}, pendingRestore=0");
        }

        /// <summary>
        /// Called from InjectCurrentFrame while reverifying. Appends a checkpoint
        /// and verify point at the configured cadence.
        /// </summary>
        private void CaptureReverifyPoints()
        {
            if (reverifyLastCapturedFrame < 0 ||
                frameCounter - reverifyLastCapturedFrame >= reverifyCaptureEveryN)
            {
                reverifyLastCapturedFrame = frameCounter;

                var cp = new ReplayCheckpoint
                {
                    Frame = frameCounter,
                    SpanIndex = spanIndex,
                    State = ReplayState.Capture(player),
                };
                replayFile.Checkpoints.Add(cp);

                var body = (Rigidbody2D)ReplayState.F_body.GetValue(player);
                replayFile.VerifyPoints.Add(new VerifyPoint
                {
                    Frame = frameCounter,
                    Position = player.transform.position,
                    Velocity = body.linearVelocity,
                });
            }
        }

        /// <summary>
        /// Called from PostMovementUpdate when reverify reaches end-of-replay.
        /// </summary>
        private void EndReverify()
        {
            reverifying = false;
            paused = true;
            Time.timeScale = 0f;
            Plugin.DbgLog($"EndReverify fc={frameCounter} cp.Count={replayFile.Checkpoints.Count} vp.Count={replayFile.VerifyPoints.Count}");

            var editor = FindAnyObjectByType<ReplayEditor>();
            if (editor != null)
                editor.OnReverifyComplete();
        }

        /// <summary>
        /// Called from Harmony postfix on Movement.Update, after Movement processed input.
        /// Handles bookkeeping: ghosts, seeking, end detection.
        /// </summary>
        public void PostMovementUpdate()
        {
            if (!playing || player == null) return;

            // Pending restore: Movement.Update already ran (with whatever state
            // was there — we don't care, we're about to overwrite it). Restore
            // here in postfix so the next FixedUpdate naturally integrates from
            // restored body state — same lifecycle as during recording.
            //
            // We restore to a checkpoint WarmupFrames before the requested target,
            // then silently replay those frames so Box2D builds natural contact
            // history (warm-start impulses) that matches the recording.
            if (pendingRestoreFrame >= 0)
            {
                int target = pendingRestoreFrame;
                bool wasSeek = pendingRestoreSeek;
                int seekTgt = pendingRestoreSeekTarget;
                pendingRestoreFrame = -1;
                pendingRestoreSeek = false;

                int warmupNeeded = RestoreWithWarmup(target);
                warmupFramesRemaining = warmupNeeded;
                warmupLastSeenFrame = frameCounter;
                // Trail is usable if it covers [restoredFrame+1, target].
                int firstWarmup = frameCounter + 1;
                int lastWarmup = target;
                warmupUsingTrail = (warmupNeeded > 0 &&
                                    firstWarmup <= liveTrailValidThrough &&
                                    lastWarmup <= liveTrailValidThrough);
                Plugin.DbgLog($"PostMovementUpdate: restored at frame {frameCounter}, target={target}, warmup={warmupNeeded}, usingTrail={warmupUsingTrail} (valid through {liveTrailValidThrough})");
                if (wasSeek)
                {
                    seeking = true;
                    seekTarget = seekTgt;
                }
                // Skip validation this frame — we just restored, the state IS the checkpoint.
                return;
            }

            // Warmup: silently replay frames to let Box2D build contact history.
            // Movement.Update + FixedUpdate run normally (recorded inputs are
            // injected via InjectCurrentFrame in the prefix). If the live trail
            // covers this frame, force-snap body pos/vel back onto the recorded
            // trajectory so drift from cold-cache physics doesn't compound.
            if (warmupFramesRemaining > 0)
            {
                // Guard: only tick warmup when frameCounter actually advanced
                // (i.e. InjectCurrentFrame ran). Otherwise postfix-only ticks
                // (timeScale=0, paused, etc.) would drain the countdown without
                // any physics actually running.
                if (frameCounter == warmupLastSeenFrame)
                {
                    Plugin.DbgLog($"PostMovementUpdate: warmup postfix at fc={frameCounter} but frame didn't advance — waiting");
                    return;
                }
                warmupLastSeenFrame = frameCounter;
                if (warmupUsingTrail && frameCounter >= 0 && frameCounter < liveTrail.Count)
                {
                    var warmupBody = (Rigidbody2D)ReplayState.F_body.GetValue(player);
                    var t = liveTrail[frameCounter];
                    var wasInterp = warmupBody.interpolation;
                    warmupBody.interpolation = RigidbodyInterpolation2D.None;
                    warmupBody.position = t.Pos;
                    warmupBody.linearVelocity = t.Vel;
                    warmupBody.interpolation = wasInterp;
                    Plugin.DbgLog($"PostMovementUpdate: warmup fc={frameCounter}, snapped to trail pos={t.Pos} vel={t.Vel}, {warmupFramesRemaining - 1} remaining");
                }
                else
                {
                    Plugin.DbgLog($"PostMovementUpdate: warmup fc={frameCounter}, {warmupFramesRemaining - 1} remaining (no trail)");
                }
                warmupFramesRemaining--;

                // If this was the final warmup tick AND we've reached the seek
                // target, pause in place. Otherwise returning here lets the
                // next postfix advance past target before the seek check fires,
                // overshooting by one frame.
                if (warmupFramesRemaining == 0 && seeking && frameCounter >= seekTarget)
                {
                    seeking = false;
                    paused = true;
                    Time.timeScale = 0f;
                    Plugin.DbgLog($"PostMovementUpdate SEEK DONE post-warmup fc={frameCounter} target={seekTarget}");
                }
                return;
            }

            if (!seeking && (finished || paused))
            {
                Plugin.DbgLog($"PostMovementUpdate SKIPPED fc={frameCounter} seeking={seeking} finished={finished} paused={paused}");
                return;
            }

            // FULL state dump at POSTFIX ENTRY — what Movement.Update produced
            Plugin.DbgLog($"PLAY-POST fc={frameCounter} :: {ReplayState.DumpAllFields(player)}");

            // Record body state into the live trail. This is what warmup-driven
            // seeks read from to scripted-move the body during kinematic warmup.
            {
                var trailBody = (Rigidbody2D)ReplayState.F_body.GetValue(player);
                WriteTrail(frameCounter, trailBody.position, trailBody.linearVelocity);
            }

            // Validate checkpoints and capture reverify points AFTER Movement.Update
            // ran — this is the deterministic point, matching where recording captures them.
            if (frameCounter > 1)
            {
                if (reverifying && frameCounter < reverifyDirtyFrame)
                {
                    CheckVerifyPoints();
                    ValidateCheckpointsStrict();
                }
                else if (!reverifying)
                {
                    CheckVerifyPoints();
                    ValidateCheckpoints();
                }
            }

            if (reverifying && frameCounter >= reverifyDirtyFrame)
                CaptureReverifyPoints();

            // Reached seek target — pause immediately (including timeScale)
            if (seeking && frameCounter >= seekTarget)
            {
                seeking = false;
                paused = true;
                Time.timeScale = 0f;
                Plugin.DbgLog($"PostMovementUpdate SEEK DONE fc={frameCounter} target={seekTarget}");
                return;
            }

            // Step target reached — pause and sync visual position
            if (PauseOnFrame > 0 && frameCounter >= PauseOnFrame)
            {
                PauseOnFrame = 0;
                paused = true;
                Time.timeScale = 0f;
                var body = (Rigidbody2D)ReplayState.F_body.GetValue(player);
                player.transform.position = new Vector3(body.position.x, body.position.y, player.transform.position.z);

                // Update ghosts even when stepping
                var stepEditor = FindAnyObjectByType<ReplayEditor>();
                if (stepEditor != null)
                    stepEditor.OnPlaybackFrame(frameCounter, player);

                Plugin.DbgLog($"PostMovementUpdate STEP DONE fc={frameCounter} pos={body.position}");
                return;
            }

            // Notify editor for replay ghosts (skip during fast-seeking to avoid spam)
            if (!seeking)
            {
                var editor = FindAnyObjectByType<ReplayEditor>();
                if (editor != null)
                    editor.OnPlaybackFrame(frameCounter, player);
            }

            // At end of replay, pause in place (don't tear down)
            bool pastAllSpans = replayFile.Spans.Count == 0 ||
                (spanIndex >= replayFile.Spans.Count - 1 &&
                 frameCounter > replayFile.Spans[replayFile.Spans.Count - 1].Frame + 50);

            if (pastAllSpans && !finished)
            {
                seeking = false;
                finished = true;
                paused = true;
                Plugin.DbgLog($"PostMovementUpdate END OF REPLAY fc={frameCounter} reverifying={reverifying}");
                Log.LogInfo($"Playback reached end at frame {frameCounter}.");

                if (reverifying)
                {
                    // End-of-replay during reverify: finalize the pass instead of
                    // tearing down playback. The editor will unlock controls.
                    EndReverify();
                }
                else
                {
                    Plugin.Instance.OnPlaybackFinished();
                }
            }
        }


        private void InjectForFrame(int frame)
        {
            // Find the span that covers the target frame
            int si = spanIndex;
            while (si + 1 < replayFile.Spans.Count &&
                   replayFile.Spans[si + 1].Frame <= frame)
            {
                si++;
            }

            var keyboardState = new KeyboardState();
            var mouseState = new MouseState();
            var gamepadState = new GamepadState();

            if (si < replayFile.Spans.Count && replayFile.Spans[si].Frame <= frame)
            {
                var span = replayFile.Spans[si];

                foreach (var path in span.Keys)
                {
                    // All recorded inputs are now full InputSystem paths: <Device>/control
                    int close = path.IndexOf('>');
                    if (close < 0) continue;
                    string device = path.Substring(1, close - 1);
                    string control = path.Substring(close + 2);

                    switch (device)
                    {
                        case "Keyboard":
                            // Find the Key enum for this control name
                            var keyCtrl = virtualKeyboard.TryGetChildControl(control);
                            if (keyCtrl is UnityEngine.InputSystem.Controls.KeyControl kc)
                                keyboardState.Set(kc.keyCode, true);
                            break;

                        case "Mouse":

                            switch (control)
                            {
                                case "leftButton": mouseState = mouseState.WithButton(MouseButton.Left); break;
                                case "rightButton": mouseState = mouseState.WithButton(MouseButton.Right); break;
                                case "middleButton": mouseState = mouseState.WithButton(MouseButton.Middle); break;
                                case "forwardButton": mouseState = mouseState.WithButton(MouseButton.Forward); break;
                                case "backButton": mouseState = mouseState.WithButton(MouseButton.Back); break;
                            }
                            break;

                        case "Gamepad":

                            gamepadState = SetGamepadButton(gamepadState, control);
                            break;
                    }
                }

                if (span.MousePos.HasValue)
                    lastMousePos = span.MousePos.Value;
            }

            // Queue state for ALL virtual devices every frame so transitions are detected.
            mouseState.position = lastMousePos;
            InputSystem.QueueStateEvent(virtualKeyboard, keyboardState, InputState.currentTime);
            InputSystem.QueueStateEvent(virtualMouse, mouseState, InputState.currentTime);
            InputSystem.QueueStateEvent(virtualGamepad, gamepadState, InputState.currentTime);
        }

        private void ValidateCheckpoints()
        {
            var cps = replayFile.Checkpoints;
            while (checkpointIndex < cps.Count && cps[checkpointIndex].Frame <= frameCounter)
            {
                var cp = cps[checkpointIndex];
                if (cp.Frame == frameCounter)
                {
                    var actual = ReplayState.Capture(player);
                    var expected = cp.State;

                    float posDelta = Vector2.Distance(actual.Position, expected.Position);
                    float bodyPosDelta = Vector2.Distance(actual.BodyPosition, expected.BodyPosition);
                    float velDelta = Vector2.Distance(actual.Velocity, expected.Velocity);
                    float momDelta = Vector2.Distance(actual.Momentum, expected.Momentum);

                    if (posDelta > 0.5f)
                        Log.LogWarning($"CHECKPOINT MISMATCH frame {cp.Frame}: transform.pos delta={posDelta:F2} (expected {expected.Position}, got {actual.Position})");
                    if (bodyPosDelta > 0.5f)
                        Log.LogWarning($"CHECKPOINT MISMATCH frame {cp.Frame}: body.pos delta={bodyPosDelta:F2} (expected {expected.BodyPosition}, got {actual.BodyPosition})");
                    if (velDelta > 5f)
                        Log.LogWarning($"CHECKPOINT MISMATCH frame {cp.Frame}: vel delta={velDelta:F2} (expected {expected.Velocity}, got {actual.Velocity})");
                    if (momDelta > 1f)
                        Log.LogWarning($"CHECKPOINT MISMATCH frame {cp.Frame}: momentum delta={momDelta:F2} (expected {expected.Momentum}, got {actual.Momentum})");
                    if (actual.OnGround != expected.OnGround)
                        Log.LogWarning($"CHECKPOINT MISMATCH frame {cp.Frame}: onGround expected={expected.OnGround} got={actual.OnGround}");
                    if (actual.OnWall != expected.OnWall)
                        Log.LogWarning($"CHECKPOINT MISMATCH frame {cp.Frame}: onWall expected={expected.OnWall} got={actual.OnWall}");
                    if (actual.AirDashesLeft != expected.AirDashesLeft)
                        Log.LogWarning($"CHECKPOINT MISMATCH frame {cp.Frame}: airDashesLeft expected={expected.AirDashesLeft} got={actual.AirDashesLeft}");
                    if (actual.DashActive != expected.DashActive)
                        Log.LogWarning($"CHECKPOINT MISMATCH frame {cp.Frame}: dashActive expected={expected.DashActive} got={actual.DashActive}");
                    if (actual.CutsceneMode != expected.CutsceneMode)
                        Log.LogWarning($"CHECKPOINT MISMATCH frame {cp.Frame}: cutsceneMode expected={expected.CutsceneMode} got={actual.CutsceneMode}");
                }
                checkpointIndex++;
            }
        }

        /// <summary>
        /// Strict checkpoint validation used during reverify for frames BEFORE the
        /// dirty frame. These checkpoints survived InvalidateDownstream and must
        /// match exactly — a mismatch means non-deterministic replay, which is fatal.
        /// </summary>
        private void ValidateCheckpointsStrict()
        {
            var cps = replayFile.Checkpoints;
            while (checkpointIndex < cps.Count && cps[checkpointIndex].Frame <= frameCounter)
            {
                var cp = cps[checkpointIndex];
                if (cp.Frame == frameCounter)
                {
                    var actual = ReplayState.Capture(player);
                    var expected = cp.State;

                    float posDelta = Vector2.Distance(actual.Position, expected.Position);
                    float bodyPosDelta = Vector2.Distance(actual.BodyPosition, expected.BodyPosition);
                    float velDelta = Vector2.Distance(actual.Velocity, expected.Velocity);
                    float momDelta = Vector2.Distance(actual.Momentum, expected.Momentum);

                    var errors = new System.Text.StringBuilder();
                    if (posDelta > 0.01f)
                        errors.AppendLine($"  transform.pos delta={posDelta:F4} (expected {expected.Position}, got {actual.Position})");
                    if (bodyPosDelta > 0.01f)
                        errors.AppendLine($"  body.pos delta={bodyPosDelta:F4} (expected {expected.BodyPosition}, got {actual.BodyPosition})");
                    if (velDelta > 0.01f)
                        errors.AppendLine($"  vel delta={velDelta:F4} (expected {expected.Velocity}, got {actual.Velocity})");
                    if (momDelta > 0.01f)
                        errors.AppendLine($"  momentum delta={momDelta:F4} (expected {expected.Momentum}, got {actual.Momentum})");
                    if (actual.OnGround != expected.OnGround)
                        errors.AppendLine($"  onGround expected={expected.OnGround} got={actual.OnGround}");
                    if (actual.OnWall != expected.OnWall)
                        errors.AppendLine($"  onWall expected={expected.OnWall} got={actual.OnWall}");
                    if (actual.AirDashesLeft != expected.AirDashesLeft)
                        errors.AppendLine($"  airDashesLeft expected={expected.AirDashesLeft} got={actual.AirDashesLeft}");
                    if (actual.DashActive != expected.DashActive)
                        errors.AppendLine($"  dashActive expected={expected.DashActive} got={actual.DashActive}");
                    if (actual.CutsceneMode != expected.CutsceneMode)
                        errors.AppendLine($"  cutsceneMode expected={expected.CutsceneMode} got={actual.CutsceneMode}");

                    if (errors.Length > 0)
                    {
                        string msg = $"FATAL: Pre-dirty checkpoint diverged at frame {cp.Frame} " +
                            $"(dirty starts at {reverifyDirtyFrame}). Replay is non-deterministic!\n{errors}";
                        Log.LogError(msg);
                        reverifying = false;
                        paused = true;
                        Time.timeScale = 0f;
                        throw new System.Exception(msg);
                    }
                }
                checkpointIndex++;
            }
        }

        private void DisableRealDevices()
        {
            disabledDevices.Clear();
            foreach (var device in InputSystem.devices)
            {
                if (VirtualDeviceNames.Contains(device.name)) continue;
                if (device.enabled)
                {
                    InputSystem.DisableDevice(device);
                    disabledDevices.Add(device);
                }
            }
            Log.LogInfo($"Disabled {disabledDevices.Count} real input devices during playback.");
        }

        private void EnableRealDevices()
        {
            foreach (var device in disabledDevices)
            {
                try { InputSystem.EnableDevice(device); }
                catch { /* device may have been removed */ }
            }
            Log.LogInfo($"Re-enabled {disabledDevices.Count} real input devices.");
            disabledDevices.Clear();
        }

        private void AdvanceSpanIndex()
        {
            while (spanIndex + 1 < replayFile.Spans.Count &&
                   replayFile.Spans[spanIndex + 1].Frame <= frameCounter)
            {
                spanIndex++;
            }
        }

        private static GamepadState SetGamepadButton(GamepadState state, string control)
        {
            switch (control)
            {
                case "buttonSouth": state = state.WithButton(GamepadButton.South); break;
                case "buttonNorth": state = state.WithButton(GamepadButton.North); break;
                case "buttonEast": state = state.WithButton(GamepadButton.East); break;
                case "buttonWest": state = state.WithButton(GamepadButton.West); break;
                case "leftShoulder": state = state.WithButton(GamepadButton.LeftShoulder); break;
                case "rightShoulder": state = state.WithButton(GamepadButton.RightShoulder); break;
                case "leftTrigger": state = state.WithButton(GamepadButton.LeftTrigger); break;
                case "rightTrigger": state = state.WithButton(GamepadButton.RightTrigger); break;
                case "start": state = state.WithButton(GamepadButton.Start); break;
                case "select": state = state.WithButton(GamepadButton.Select); break;
                case "dpad/up": state = state.WithButton(GamepadButton.DpadUp); break;
                case "dpad/down": state = state.WithButton(GamepadButton.DpadDown); break;
                case "dpad/left": state = state.WithButton(GamepadButton.DpadLeft); break;
                case "dpad/right": state = state.WithButton(GamepadButton.DpadRight); break;
                case "leftStickButton": state = state.WithButton(GamepadButton.LeftStick); break;
                case "rightStickButton": state = state.WithButton(GamepadButton.RightStick); break;
            }
            return state;
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
            string vkName = virtualKeyboard.name;
            string vmName = virtualMouse.name;
            string vgName = virtualGamepad.name;

            // Save and remap ALL registered actions, not just the 3 we know about
            foreach (var action in InputSystem.actions)
            {
                savedOverrides[action.id] = SaveActionOverrides(action);
                RemapAllBindings(action, vkName, vmName, vgName);
            }
        }

        /// <summary>
        /// Remap every binding on an action to its virtual device equivalent.
        /// </summary>
        private static void RemapAllBindings(InputAction action, string vkName, string vmName, string vgName)
        {
            if (action == null) return;
            bool wasEnabled = action.enabled;
            action.Disable();

            for (int i = 0; i < action.bindings.Count; i++)
            {
                var binding = action.bindings[i];
                if (binding.isComposite) continue;

                // Use effectivePath (includes user overrides) so rebound keys are captured
                string origPath = binding.effectivePath;
                if (string.IsNullOrEmpty(origPath)) continue;

                string virtualPath = RemapToVirtualDevice(origPath, vkName, vmName, vgName);
                if (virtualPath != null)
                    action.ApplyBindingOverride(i, new InputBinding { overridePath = virtualPath });
            }

            if (wasEnabled) action.Enable();
        }

        /// <summary>
        /// Takes a full InputSystem path like "&lt;Keyboard&gt;/space" or "&lt;Mouse&gt;/leftButton"
        /// and swaps the device to our virtual device. Returns null for unmappable devices (gamepad).
        /// </summary>
        private static string RemapToVirtualDevice(string fullPath, string vkName, string vmName, string vgName)
        {
            if (string.IsNullOrEmpty(fullPath)) return null;

            // Parse "<DeviceType>/controlPath"
            int close = fullPath.IndexOf('>');
            if (close < 0) return null;
            string device = fullPath.Substring(1, close - 1); // e.g. "Keyboard", "Mouse", "Gamepad"
            string control = fullPath.Substring(close + 2);    // e.g. "space", "leftButton", "buttonSouth"

            switch (device)
            {
                case "Keyboard": return $"/{vkName}/{control}";
                case "Mouse": return $"/{vmName}/{control}";
                case "Gamepad": return $"/{vgName}/{control}";
                default: return null;
            }
        }


        private void ResetBindings()
        {
            foreach (var action in InputSystem.actions)
            {
                if (savedOverrides.TryGetValue(action.id, out var overrides))
                    RestoreActionOverrides(action, overrides);
            }
            savedOverrides.Clear();
        }

        private static List<string> SaveActionOverrides(InputAction action)
        {
            if (action == null) return null;
            var overrides = new List<string>();
            for (int i = 0; i < action.bindings.Count; i++)
                overrides.Add(action.bindings[i].overridePath); // null if no override
            return overrides;
        }

        private static void RestoreActionOverrides(InputAction action, List<string> overrides)
        {
            if (action == null) return;
            bool wasEnabled = action.enabled;
            action.Disable();

            if (overrides != null && overrides.Count == action.bindings.Count)
            {
                for (int i = 0; i < action.bindings.Count; i++)
                    action.ApplyBindingOverride(i, new InputBinding { overridePath = overrides[i] });
            }
            else
            {
                action.RemoveAllBindingOverrides();
            }

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



        private void OnDestroy()
        {
            if (playing)
                StopPlayback();
        }
    }
}
