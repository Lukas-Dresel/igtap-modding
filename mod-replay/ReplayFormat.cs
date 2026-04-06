using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IGTAPReplay
{
    /// <summary>
    /// One span of constant input state, starting at Frame and lasting until the next span.
    /// </summary>
    public struct InputSpan
    {
        /// <summary>Frame number where this input state begins (1-based).</summary>
        public int Frame;

        /// <summary>Set of key/button names held during this span. Empty set = no keys.
        /// Mouse buttons appear as "mouse0", "mouse1", "mouse2", etc.</summary>
        public HashSet<string> Keys;

        /// <summary>Mouse screen position, or null if unchanged from previous span.</summary>
        public Vector2? MousePos;

        /// <summary>The resolved xMoveAxis value at recording time.</summary>
        public float XMoveAxis;
    }

    /// <summary>
    /// A verification point recorded during capture for desync detection on playback.
    /// </summary>
    public struct VerifyPoint
    {
        public int Frame;
        public Vector2 Position;
        public Vector2 Velocity;
    }

    /// <summary>
    /// Keybinding snapshot mapping action names to their bound keys.
    /// </summary>
    public struct BindingSnapshot
    {
        /// <summary>Frame at which these bindings take effect (0 = from start).</summary>
        public int Frame;
        /// <summary>Action name -> list of bound key names.</summary>
        public Dictionary<string, List<string>> ActionKeys;
    }

    /// <summary>
    /// Full state checkpoint for seeking during playback.
    /// </summary>
    public struct ReplayCheckpoint
    {
        public int Frame;
        public int SpanIndex;
        public ReplayState.Snapshot State;
    }

    /// <summary>
    /// Complete replay file data model.
    /// </summary>
    public class ReplayFile
    {
        public int Version = 1;
        public string RecordedAt;
        public int Timestep = 50;
        public ReplayState.Snapshot InitialState;
        public List<BindingSnapshot> Bindings = new List<BindingSnapshot>();
        public List<InputSpan> Spans = new List<InputSpan>();
        public List<VerifyPoint> VerifyPoints = new List<VerifyPoint>();
        public List<ReplayCheckpoint> Checkpoints = new List<ReplayCheckpoint>();
    }

    /// <summary>
    /// Parser and serializer for the human-editable replay text format.
    /// </summary>
    public static class ReplayFormat
    {
        public static void Write(ReplayFile file, string path)
        {
            var sb = new StringBuilder();
            var snap = file.InitialState;

            sb.AppendLine("# IGTAP Replay v1");
            sb.AppendLine($"# Recorded: {file.RecordedAt}");
            sb.AppendLine($"# Timestep: {file.Timestep}");

            foreach (var binding in file.Bindings)
            {
                var parts = new List<string>();
                foreach (var kv in binding.ActionKeys)
                    parts.Add($"{kv.Key}={string.Join(",", kv.Value)}");
                if (binding.Frame == 0)
                    sb.AppendLine($"# Bindings: {string.Join(" ", parts)}");
                else
                    sb.AppendLine($"# Bindings@{binding.Frame}: {string.Join(" ", parts)}");
            }

            sb.AppendLine();

            // Interleave spans, verify points, and checkpoints in frame order
            int vi = 0;
            int ci = 0;
            foreach (var span in file.Spans)
            {
                // Write any verify points before this span
                while (vi < file.VerifyPoints.Count && file.VerifyPoints[vi].Frame <= span.Frame)
                {
                    var vp = file.VerifyPoints[vi];
                    sb.AppendLine($"# @{vp.Frame} pos={F(vp.Position.x)},{F(vp.Position.y)} vel={F(vp.Velocity.x)},{F(vp.Velocity.y)}");
                    vi++;
                }

                // Write any checkpoints before this span
                while (ci < file.Checkpoints.Count && file.Checkpoints[ci].Frame <= span.Frame)
                {
                    var cp = file.Checkpoints[ci];
                    string json = JsonUtility.ToJson(cp.State);
                    sb.AppendLine($"# !CP {cp.Frame} {cp.SpanIndex} {json}");
                    ci++;
                }

                string keys = span.Keys.Count > 0
                    ? string.Join(" ", span.Keys.Select(k => KeyNames.ToShortName(k)).OrderBy(k => k))
                    : ".";
                string mousePart = span.MousePos.HasValue
                    ? $" @{F(span.MousePos.Value.x)},{F(span.MousePos.Value.y)}"
                    : "";
                string axisPart = $" x={span.XMoveAxis:F0}";
                sb.AppendLine($"{span.Frame,-8} {keys}{mousePart}{axisPart}");
            }

            // Write remaining verify points and checkpoints
            while (vi < file.VerifyPoints.Count)
            {
                var vp = file.VerifyPoints[vi];
                sb.AppendLine($"# @{vp.Frame} pos={F(vp.Position.x)},{F(vp.Position.y)} vel={F(vp.Velocity.x)},{F(vp.Velocity.y)}");
                vi++;
            }
            while (ci < file.Checkpoints.Count)
            {
                var cp = file.Checkpoints[ci];
                string json = JsonUtility.ToJson(cp.State);
                sb.AppendLine($"# !CP {cp.Frame} {cp.SpanIndex} {json}");
                ci++;
            }

            File.WriteAllText(path, sb.ToString());
        }

        public static ReplayFile Read(string path)
        {
            var file = new ReplayFile();
            var lines = File.ReadAllLines(path);

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                if (line.StartsWith("#"))
                {
                    ParseComment(file, line);
                    continue;
                }

                // Data line: "frame  key1 key2 ..."
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;

                if (!int.TryParse(parts[0], out int frame))
                    continue;

                var keys = new HashSet<string>();
                Vector2? mousePos = null;
                float xMoveAxis = 0f;
                for (int i = 1; i < parts.Length; i++)
                {
                    if (parts[i] == ".")
                        continue;
                    if (parts[i].StartsWith("@"))
                    {
                        // Mouse position: @x,y
                        var coords = parts[i].Substring(1).Split(',');
                        if (coords.Length == 2 &&
                            float.TryParse(coords[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float mx) &&
                            float.TryParse(coords[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float my))
                        {
                            mousePos = new Vector2(mx, my);
                        }
                    }
                    else if (parts[i].StartsWith("x="))
                    {
                        float.TryParse(parts[i].Substring(2), NumberStyles.Float, CultureInfo.InvariantCulture, out xMoveAxis);
                    }
                    else
                    {
                        keys.Add(KeyNames.ToPath(parts[i]));
                    }
                }

                file.Spans.Add(new InputSpan { Frame = frame, Keys = keys, MousePos = mousePos, XMoveAxis = xMoveAxis });
            }

            // Set InitialState from frame-0 checkpoint if available
            if (file.Checkpoints.Count > 0 && file.Checkpoints[0].Frame == 0)
                file.InitialState = file.Checkpoints[0].State;

            return file;
        }

        private static void ParseComment(ReplayFile file, string line)
        {
            // Strip "# " prefix
            var content = line.StartsWith("# ") ? line.Substring(2) : line.Substring(1);
            content = content.Trim();

            if (content.StartsWith("IGTAP Replay v"))
            {
                if (int.TryParse(content.Substring("IGTAP Replay v".Length), out int v))
                    file.Version = v;
            }
            else if (content.StartsWith("Recorded: "))
            {
                file.RecordedAt = content.Substring("Recorded: ".Length);
            }
            else if (content.StartsWith("Timestep: "))
            {
                if (int.TryParse(content.Substring("Timestep: ".Length), out int ts))
                    file.Timestep = ts;
            }
            else if (content.StartsWith("Bindings"))
            {
                ParseBindings(file, content);
            }
            else if (content.StartsWith("!CP "))
            {
                // "!CP <frame> <spanIndex> <json>"
                var rest = content.Substring(4);
                var spaceIdx1 = rest.IndexOf(' ');
                if (spaceIdx1 < 0) return;
                var spaceIdx2 = rest.IndexOf(' ', spaceIdx1 + 1);
                if (spaceIdx2 < 0) return;

                if (!int.TryParse(rest.Substring(0, spaceIdx1), out int cpFrame)) return;
                if (!int.TryParse(rest.Substring(spaceIdx1 + 1, spaceIdx2 - spaceIdx1 - 1), out int cpSpan)) return;
                string json = rest.Substring(spaceIdx2 + 1);

                var state = JsonUtility.FromJson<ReplayState.Snapshot>(json);
                file.Checkpoints.Add(new ReplayCheckpoint
                {
                    Frame = cpFrame,
                    SpanIndex = cpSpan,
                    State = state,
                });
            }
            else if (content.StartsWith("@"))
            {
                ParseVerifyPoint(file, content);
            }
        }

        private static void ParseBindings(ReplayFile file, string content)
        {
            // "Bindings: Move=w,a,s,d Jump=space Dash=lshift"
            // "Bindings@120: Move=w,a,s,d Jump=space Dash=lshift"
            int frame = 0;
            string data;

            if (content.StartsWith("Bindings@"))
            {
                var colonIdx = content.IndexOf(':');
                var frameStr = content.Substring("Bindings@".Length, colonIdx - "Bindings@".Length);
                int.TryParse(frameStr, out frame);
                data = content.Substring(colonIdx + 1).Trim();
            }
            else
            {
                data = content.Substring("Bindings:".Length).Trim();
            }

            var actionKeys = new Dictionary<string, List<string>>();
            foreach (var part in data.Split(' '))
            {
                var eq = part.IndexOf('=');
                if (eq < 0) continue;
                var action = part.Substring(0, eq);
                var keys = part.Substring(eq + 1).Split(',').ToList();
                actionKeys[action] = keys;
            }

            file.Bindings.Add(new BindingSnapshot { Frame = frame, ActionKeys = actionKeys });
        }

        private static void ParseVerifyPoint(ReplayFile file, string content)
        {
            // "@50 pos=4380.2,1886.0 vel=12000.0,0.0"
            var parts = content.Split(' ');
            if (parts.Length < 3) return;

            if (!int.TryParse(parts[0].Substring(1), out int frame)) return;

            Vector2 pos = Vector2.zero, vel = Vector2.zero;
            for (int i = 1; i < parts.Length; i++)
            {
                var eq = parts[i].IndexOf('=');
                if (eq < 0) continue;
                var key = parts[i].Substring(0, eq);
                var val = parts[i].Substring(eq + 1);
                var comps = val.Split(',');
                if (comps.Length != 2) continue;
                if (!float.TryParse(comps[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) continue;
                if (!float.TryParse(comps[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) continue;

                if (key == "pos") pos = new Vector2(x, y);
                else if (key == "vel") vel = new Vector2(x, y);
            }

            file.VerifyPoints.Add(new VerifyPoint { Frame = frame, Position = pos, Velocity = vel });
        }

        private static Vector2 ParseVec2(string s)
        {
            var parts = s.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return Vector2.zero;
            float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x);
            float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y);
            return new Vector2(x, y);
        }

        private static string F(float v) => v.ToString("F1", CultureInfo.InvariantCulture);
        private static string B(bool v) => v ? "true" : "false";
    }
}
