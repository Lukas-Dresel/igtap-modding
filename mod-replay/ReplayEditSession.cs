using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IGTAPReplay
{
    /// <summary>
    /// Semantic scope for a held-keys edit. Applied by ReplayEditSession.SetHeldKeysAt.
    /// </summary>
    public enum EditScope
    {
        /// <summary>The new key set replaces the keys of the span covering the frame,
        /// extending through whatever span boundary comes next. Default.</summary>
        FromHere,
        /// <summary>The new key set is applied only at the single specified frame;
        /// the following frame reverts to the original pre-edit keys.</summary>
        ThisFrameOnly,
    }

    /// <summary>
    /// Owns all editing state for a loaded replay: the working <see cref="ReplayFile"/>,
    /// undo/redo stacks, dirty tracking, and the mutation primitive used by the editor UI.
    ///
    /// The session mutates the ReplayFile *in place*, so other systems (playback, editor)
    /// that hold a reference see changes immediately. The on-disk file is never touched
    /// until an explicit Save-As.
    /// </summary>
    public class ReplayEditSession
    {
        public readonly ReplayFile File;
        public readonly string OriginalLoadPath;

        private readonly Stack<ReplayFile> undoStack = new Stack<ReplayFile>();
        private readonly Stack<ReplayFile> redoStack = new Stack<ReplayFile>();
        private const int MaxUndoDepth = 50;

        // Lowest frame touched since the last successful reverify. -1 = clean.
        public int DirtyFromFrame { get; private set; } = -1;
        public bool IsDirty => DirtyFromFrame >= 0;

        // Composite Move action parts — collected once per session so SetHeldKeysAt can
        // compute xMoveAxis from the held keys without querying a live InputSystem.
        private readonly HashSet<string> leftMoveKeys = new HashSet<string>();
        private readonly HashSet<string> rightMoveKeys = new HashSet<string>();

        public ReplayEditSession(ReplayFile file, string originalLoadPath, Movement player)
        {
            File = file;
            OriginalLoadPath = originalLoadPath;
            CaptureMoveCompositeParts(player);
        }

        // ============================================================
        // Move composite detection (for xMoveAxis on edited spans)
        // ============================================================

        private void CaptureMoveCompositeParts(Movement player)
        {
            if (player == null) return;
            var moveAction = (InputAction)ReplayState.F_moveAction.GetValue(player);
            if (moveAction == null) return;

            for (int i = 0; i < moveAction.bindings.Count; i++)
            {
                var b = moveAction.bindings[i];
                if (!b.isPartOfComposite) continue;

                string partName = (b.name ?? "").ToLowerInvariant();
                if (partName != "left" && partName != "right" &&
                    partName != "negative" && partName != "positive")
                    continue;

                // During playback, bindings are overridden to virtual device paths
                // like "/ReplayKeyboard/a". Canonicalize back to "<Keyboard>/a" so
                // comparisons against the Keys HashSet (which stores canonical paths) work.
                string canonical = CanonicalizePath(b.effectivePath);
                if (string.IsNullOrEmpty(canonical)) continue;

                if (partName == "left" || partName == "negative")
                    leftMoveKeys.Add(canonical);
                else
                    rightMoveKeys.Add(canonical);
            }

            Plugin.DbgLog($"EditSession: Move composite — left={string.Join(",", leftMoveKeys)} right={string.Join(",", rightMoveKeys)}");
        }

        private static string CanonicalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (path.StartsWith("<")) return path;
            // Virtual device paths: "/ReplayKeyboard/a" -> "<Keyboard>/a"
            if (path.StartsWith("/ReplayKeyboard/"))
                return "<Keyboard>/" + path.Substring("/ReplayKeyboard/".Length);
            if (path.StartsWith("/ReplayMouse/"))
                return "<Mouse>/" + path.Substring("/ReplayMouse/".Length);
            if (path.StartsWith("/ReplayGamepad/"))
                return "<Gamepad>/" + path.Substring("/ReplayGamepad/".Length);
            return path;
        }

        private float ComputeXMoveAxis(HashSet<string> keys)
        {
            bool leftHeld = keys.Any(k => leftMoveKeys.Contains(k));
            bool rightHeld = keys.Any(k => rightMoveKeys.Contains(k));
            if (leftHeld == rightHeld) return 0f;
            return rightHeld ? 1f : -1f;
        }

        // ============================================================
        // Queries
        // ============================================================

        /// <summary>
        /// Return the keys of the span covering the given frame. Returns an empty
        /// set if there are no spans at all (should not happen for a valid replay).
        /// </summary>
        public HashSet<string> GetKeysAtFrame(int frame)
        {
            int idx = FindSpanIndexFor(frame);
            if (idx < 0) return new HashSet<string>();
            return new HashSet<string>(File.Spans[idx].Keys);
        }

        /// <summary>Binding snapshot active at the given frame.</summary>
        public BindingSnapshot GetBindingsAtFrame(int frame)
        {
            BindingSnapshot best = default;
            bool found = false;
            foreach (var b in File.Bindings)
            {
                if (b.Frame <= frame) { best = b; found = true; }
                else break;
            }
            return found ? best : (File.Bindings.Count > 0 ? File.Bindings[0] : default);
        }

        private int FindSpanIndexFor(int frame)
        {
            var spans = File.Spans;
            if (spans.Count == 0) return -1;
            // Binary search would be nicer but spans are small and this is called
            // only in response to user interactions. Linear is fine.
            for (int i = spans.Count - 1; i >= 0; i--)
            {
                if (spans[i].Frame <= frame) return i;
            }
            return 0;
        }

        // ============================================================
        // Mutation
        // ============================================================

        /// <summary>
        /// Set the held-keys state at the given frame, splitting/merging spans as
        /// needed. Pushes an undo snapshot, drops downstream checkpoints/verify
        /// points, and marks the session dirty.
        /// </summary>
        public void SetHeldKeysAt(int frame, HashSet<string> newKeys, EditScope scope)
        {
            if (File.Spans.Count == 0) return;

            int idx = FindSpanIndexFor(frame);
            if (idx < 0) return;

            var current = File.Spans[idx];
            if (current.Keys.SetEquals(newKeys)) return; // no-op

            PushUndoSnapshot();
            redoStack.Clear();

            float xma = ComputeXMoveAxis(newKeys);

            if (scope == EditScope.FromHere)
            {
                ApplyFromHere(idx, frame, newKeys, xma);
            }
            else
            {
                ApplyThisFrameOnly(idx, frame, newKeys, xma);
            }

            CanonicalizeSpans();
            InvalidateDownstream(frame);
            DirtyFromFrame = (DirtyFromFrame < 0) ? frame : Mathf.Min(DirtyFromFrame, frame);
            Plugin.DbgLog($"EditSession.SetHeldKeysAt frame={frame} scope={scope} spans={File.Spans.Count} dirty={DirtyFromFrame}");
        }

        private void ApplyFromHere(int idx, int frame, HashSet<string> newKeys, float xma)
        {
            var current = File.Spans[idx];

            if (current.Frame == frame)
            {
                // Replace keys in the existing span — no split needed.
                current.Keys = new HashSet<string>(newKeys);
                current.XMoveAxis = xma;
                File.Spans[idx] = current;
                return;
            }

            // Split: leave the left half unchanged (up to frame-1) and insert a
            // new span starting at `frame` with the new keys. Preserve MousePos:
            // the new span inherits no mouse position change (null).
            var newSpan = new InputSpan
            {
                Frame = frame,
                Keys = new HashSet<string>(newKeys),
                MousePos = null,
                XMoveAxis = xma,
            };
            File.Spans.Insert(idx + 1, newSpan);
        }

        private void ApplyThisFrameOnly(int idx, int frame, HashSet<string> newKeys, float xma)
        {
            var current = File.Spans[idx];
            var originalKeys = new HashSet<string>(current.Keys);
            float originalXma = current.XMoveAxis;

            // Insert a 1-frame span at `frame`, then re-insert a span at frame+1
            // that restores the original keys. The revert span inherits no mouse
            // position (null).
            if (current.Frame == frame)
            {
                // The existing span already starts here — we rewrite it in place
                // and insert a revert span at frame+1.
                current.Keys = new HashSet<string>(newKeys);
                current.XMoveAxis = xma;
                File.Spans[idx] = current;
                File.Spans.Insert(idx + 1, new InputSpan
                {
                    Frame = frame + 1,
                    Keys = originalKeys,
                    MousePos = null,
                    XMoveAxis = originalXma,
                });
            }
            else
            {
                // Insert the edit span after the current span, then a revert span
                // after that. The current span naturally covers frames up to frame-1.
                File.Spans.Insert(idx + 1, new InputSpan
                {
                    Frame = frame,
                    Keys = new HashSet<string>(newKeys),
                    MousePos = null,
                    XMoveAxis = xma,
                });
                File.Spans.Insert(idx + 2, new InputSpan
                {
                    Frame = frame + 1,
                    Keys = originalKeys,
                    MousePos = null,
                    XMoveAxis = originalXma,
                });
            }
        }

        /// <summary>
        /// Walk the span list and merge adjacent spans with identical Keys sets
        /// and xMoveAxis. Preserves MousePos by never merging a span that has
        /// a recorded mouse position change with its predecessor.
        /// </summary>
        private void CanonicalizeSpans()
        {
            var spans = File.Spans;
            for (int i = spans.Count - 1; i > 0; i--)
            {
                var prev = spans[i - 1];
                var cur = spans[i];

                // Never collapse a span that records a mouse movement.
                if (cur.MousePos.HasValue) continue;

                if (cur.Keys.SetEquals(prev.Keys) && Mathf.Approximately(cur.XMoveAxis, prev.XMoveAxis))
                    spans.RemoveAt(i);
            }
        }

        // ============================================================
        // Downstream invalidation
        // ============================================================

        private void InvalidateDownstream(int frame)
        {
            // The edit takes effect at `frame`; anything the player's physics
            // did from `frame` onward in the live trail is now stale.
            var playback = UnityEngine.Object.FindAnyObjectByType<ReplayPlayback>();
            if (playback != null) playback.InvalidateTrailFrom(frame);

            // Drop every checkpoint with Frame > frame. The nearest checkpoint at
            // or before `frame` remains valid because the edit happens after it.
            for (int i = File.Checkpoints.Count - 1; i >= 0; i--)
            {
                if (File.Checkpoints[i].Frame > frame)
                    File.Checkpoints.RemoveAt(i);
            }
            // Same for verify points.
            for (int i = File.VerifyPoints.Count - 1; i >= 0; i--)
            {
                if (File.VerifyPoints[i].Frame > frame)
                    File.VerifyPoints.RemoveAt(i);
            }
            // Re-fix the SpanIndex on surviving checkpoints, since span insertions
            // above `frame` may have shifted indices.
            for (int i = 0; i < File.Checkpoints.Count; i++)
            {
                var cp = File.Checkpoints[i];
                int newSi = FindSpanIndexFor(cp.Frame);
                if (newSi >= 0 && cp.SpanIndex != newSi)
                {
                    cp.SpanIndex = newSi;
                    File.Checkpoints[i] = cp;
                }
            }
        }

        public void MarkCleanAfterReverify()
        {
            DirtyFromFrame = -1;
        }

        // ============================================================
        // Undo / Redo
        // ============================================================

        private void PushUndoSnapshot()
        {
            undoStack.Push(SnapshotFile());
            while (undoStack.Count > MaxUndoDepth)
            {
                // Drop oldest by rebuilding — Stack doesn't support removal from bottom.
                var keep = undoStack.ToArray();
                undoStack.Clear();
                for (int i = keep.Length - 2; i >= 0; i--) undoStack.Push(keep[i]);
                break;
            }
        }

        private ReplayFile SnapshotFile() => File.DeepCloneForEdit();

        public bool CanUndo => undoStack.Count > 0;
        public bool CanRedo => redoStack.Count > 0;
        public int UndoDepth => undoStack.Count;
        public int RedoDepth => redoStack.Count;

        public void Undo()
        {
            if (undoStack.Count == 0) return;
            Plugin.DbgLog($"EditSession.Undo: undoStack={undoStack.Count} redoStack={redoStack.Count} dirty={DirtyFromFrame}");
            redoStack.Push(SnapshotFile());
            var prev = undoStack.Pop();
            RestoreFrom(prev);
        }

        public void Redo()
        {
            if (redoStack.Count == 0) return;
            Plugin.DbgLog($"EditSession.Redo: undoStack={undoStack.Count} redoStack={redoStack.Count} dirty={DirtyFromFrame}");
            undoStack.Push(SnapshotFile());
            var next = redoStack.Pop();
            RestoreFrom(next);
        }

        private void RestoreFrom(ReplayFile snap)
        {
            File.Spans.Clear();
            File.Spans.AddRange(snap.Spans);
            File.Checkpoints.Clear();
            File.Checkpoints.AddRange(snap.Checkpoints);
            File.VerifyPoints.Clear();
            File.VerifyPoints.AddRange(snap.VerifyPoints);
            // After restoring, the whole tail is "unknown again" conservatively —
            // but practically, if we undo the edit that dirtied us, the stacked
            // post-reverify state brings back clean checkpoints. We conservatively
            // leave the dirty flag as-is: reverify covers any ambiguity.
        }

        // ============================================================
        // Save
        // ============================================================

        /// <summary>
        /// Write the current edited file to a new "_editN" path branching off the
        /// original load path. Returns the path written.
        /// </summary>
        public string SaveAsNext()
        {
            string target = ReplayFormat.NextEditPath(OriginalLoadPath);
            ReplayFormat.Write(File, target);
            return target;
        }
    }
}
