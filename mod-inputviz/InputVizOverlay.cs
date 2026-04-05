using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IGTAPInputViz
{
    public class InputVizOverlay : MonoBehaviour
    {
        private Texture2D texWhite;
        private PlayerInput cachedPlayerInput;
        private bool isGamepad;
        private string[] moveBindNames = new string[4]; // up, down, left, right
        private float bindRefreshTimer;
        private bool bindsDumped;

        private Texture2D MakeTex(Color col)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, col);
            t.Apply();
            return t;
        }

        private void Update()
        {
            if (Plugin.ToggleKey.Value.IsDown())
                Plugin.Enabled.Value = !Plugin.Enabled.Value;
        }

        private void OnGUI()
        {
            if (!Plugin.Enabled.Value) return;
            if (texWhite == null) texWhite = MakeTex(Color.white);

            bindRefreshTimer -= Time.deltaTime;
            if (bindRefreshTimer <= 0f)
            {
                RefreshBindNames();
                bindRefreshTimer = 2f;
            }

            DrawInputViz();
        }

        private void RefreshBindNames()
        {
            if (cachedPlayerInput == null)
                cachedPlayerInput = FindAnyObjectByType<PlayerInput>();
            if (cachedPlayerInput == null) return;

            isGamepad = cachedPlayerInput.currentControlScheme == "Gamepad";
            string group = isGamepad ? "Gamepad" : "Keyboard&Mouse";

            // Dump bindings once for debugging
            if (!bindsDumped)
            {
                DumpBindings(Plugin.DirectionalAction.Value);
                foreach (var entry in InputVizAPI.Actions)
                {
                    if (entry.IsInputAction)
                        DumpBindings(entry.InputActionName);
                }
                bindsDumped = true;
            }

            // Directional
            string dirAction = Plugin.DirectionalAction.Value;
            moveBindNames[0] = GetCompositePartDisplay(dirAction, "up", group);
            moveBindNames[1] = GetCompositePartDisplay(dirAction, "down", group);
            moveBindNames[2] = GetCompositePartDisplay(dirAction, "left", group);
            moveBindNames[3] = GetCompositePartDisplay(dirAction, "right", group);

            // Action buttons
            foreach (var entry in InputVizAPI.Actions)
            {
                if (entry.IsInputAction)
                    entry.CachedBindDisplay = GetSimpleBindDisplay(entry.InputActionName, group);
                else
                    entry.CachedBindDisplay = entry.CustomBindDisplay ?? "?";
            }
        }

        private void DumpBindings(string actionName)
        {
            var action = InputSystem.actions.FindAction(actionName);
            if (action == null) return;
            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                Plugin.Log.LogInfo($"  [{actionName}] binding[{i}]: name=\"{b.name}\" path=\"{b.path}\" groups=\"{b.groups}\" isComposite={b.isComposite} isPartOfComposite={b.isPartOfComposite} display=\"{b.ToDisplayString()}\"");
            }
        }

        private string GetCompositePartDisplay(string actionName, string partName, string group)
        {
            var action = InputSystem.actions.FindAction(actionName);
            if (action == null) return "?";
            var bindings = action.bindings;

            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (!b.isPartOfComposite) continue;
                if (!string.Equals(b.name, partName, System.StringComparison.OrdinalIgnoreCase)) continue;
                string groups = b.groups ?? "";
                if (groups.Contains(group) || (isGamepad && b.path.Contains("Gamepad")) || (!isGamepad && (b.path.Contains("Keyboard") || b.path.Contains("key"))))
                    return b.ToDisplayString();
            }

            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].isPartOfComposite && string.Equals(bindings[i].name, partName, System.StringComparison.OrdinalIgnoreCase))
                    return bindings[i].ToDisplayString();
            }
            return "?";
        }

        private string GetSimpleBindDisplay(string actionName, string group)
        {
            var action = InputSystem.actions.FindAction(actionName);
            if (action == null) return "?";
            var bindings = action.bindings;

            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b.isComposite || b.isPartOfComposite) continue;
                string groups = b.groups ?? "";
                if (groups.Contains(group))
                    return b.ToDisplayString();
            }
            return "?";
        }

        private void DrawInputViz()
        {
            List<InputVizAPI.ActionEntry> actions = InputVizAPI.Actions;
            List<InputVizAPI.StatusEntry> statuses = InputVizAPI.Statuses;
            bool showDir = Plugin.ShowDirectional.Value;

            int s = 30;
            int gap = 3;
            int btnW = 80;
            int dirWidth = showDir ? (s + gap) * 3 - gap : 0;
            int actionHeight = actions.Count * (s + gap) - (actions.Count > 0 ? gap : 0);
            int dirHeight = showDir ? (s + gap) * 3 - gap : 0;
            int inputHeight = Mathf.Max(dirHeight, actionHeight);
            int gapBetween = showDir && actions.Count > 0 ? 12 : 0;
            int totalWidth = (showDir ? dirWidth : 0) + gapBetween + (actions.Count > 0 ? btnW : 0);

            // Status column height
            int statusRowH = 16;
            int statusTotalH = statuses.Count > 0 ? statuses.Count * statusRowH : 0;
            int statusGap = statuses.Count > 0 ? 6 : 0;

            float anchorX = Screen.width - totalWidth - 15;
            float anchorY = 10;

            GUIStyle keyLabel = new GUIStyle(GUI.skin.label);
            keyLabel.fontSize = 10;
            keyLabel.fontStyle = FontStyle.Bold;
            keyLabel.alignment = TextAnchor.MiddleCenter;

            // --- Directional pad ---
            if (showDir)
            {
                string dirActionName = Plugin.DirectionalAction.Value;
                var moveAction = InputSystem.actions.FindAction(dirActionName);
                Vector2 stick = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;

                bool left = stick.x < -0.3f;
                bool right = stick.x > 0.3f;
                bool up = stick.y > 0.3f;
                bool down = stick.y < -0.3f;

                DrawKey(anchorX + (s + gap), anchorY, s, s, up);
                DrawKeyLabel(anchorX + (s + gap), anchorY, s, s, moveBindNames[0], up, keyLabel);

                DrawKey(anchorX, anchorY + (s + gap), s, s, left);
                DrawKeyLabel(anchorX, anchorY + (s + gap), s, s, moveBindNames[2], left, keyLabel);

                DrawKey(anchorX + (s + gap) * 2, anchorY + (s + gap), s, s, right);
                DrawKeyLabel(anchorX + (s + gap) * 2, anchorY + (s + gap), s, s, moveBindNames[3], right, keyLabel);

                DrawKey(anchorX + (s + gap), anchorY + (s + gap) * 2, s, s, down);
                DrawKeyLabel(anchorX + (s + gap), anchorY + (s + gap) * 2, s, s, moveBindNames[1], down, keyLabel);

                DrawKey(anchorX + (s + gap), anchorY + (s + gap), s, s, false);
                float cx = anchorX + (s + gap) + s / 2f;
                float cy = anchorY + (s + gap) + s / 2f;
                float dotR = 4;
                float dx = cx + stick.x * (s / 2f - dotR);
                float dy = cy - stick.y * (s / 2f - dotR);
                GUI.DrawTexture(new Rect(dx - dotR, dy - dotR, dotR * 2, dotR * 2), texWhite);
            }

            // --- Action buttons ---
            if (actions.Count > 0)
            {
                float btnX = anchorX + (showDir ? dirWidth + gapBetween : 0);

                for (int i = 0; i < actions.Count; i++)
                {
                    var entry = actions[i];
                    float btnY = anchorY + i * (s + gap);

                    bool pressed;
                    if (entry.IsInputAction)
                    {
                        var action = InputSystem.actions.FindAction(entry.InputActionName);
                        pressed = action != null && action.IsPressed();
                    }
                    else
                    {
                        pressed = entry.CustomIsPressed != null && entry.CustomIsPressed();
                    }

                    DrawActionKey(btnX, btnY, btnW, s, entry.Label, entry.CachedBindDisplay, pressed, keyLabel);
                }
            }

            // --- Status indicators (vertical list) ---
            if (statuses.Count > 0)
            {
                float statusStartY = anchorY + inputHeight + statusGap;

                GUIStyle statusStyle = new GUIStyle(GUI.skin.label);
                statusStyle.fontSize = 11;
                statusStyle.fontStyle = FontStyle.Bold;
                statusStyle.alignment = TextAnchor.MiddleLeft;

                int dotSize = 8;

                float rightEdge = anchorX + totalWidth;

                for (int i = 0; i < statuses.Count; i++)
                {
                    var st = statuses[i];
                    bool active = st.IsActive != null && st.IsActive();
                    string detail = st.Detail != null ? st.Detail() : null;
                    string text = detail != null ? $"{st.Label} {detail}" : st.Label;

                    float rowY = statusStartY + i * statusRowH;

                    // Dot on the right
                    Color dotCol = active ? new Color(0.2f, 1f, 0.3f, 0.9f) : new Color(0.6f, 0.15f, 0.15f, 0.8f);
                    Color prev = GUI.color;
                    GUI.color = dotCol;
                    GUI.DrawTexture(new Rect(rightEdge - dotSize, rowY + (statusRowH - dotSize) / 2f, dotSize, dotSize), texWhite);
                    GUI.color = prev;

                    // Label right-aligned, left of the dot
                    statusStyle.normal.textColor = active ? new Color(0.8f, 1f, 0.8f, 0.9f) : new Color(0.7f, 0.7f, 0.7f, 0.6f);
                    statusStyle.alignment = TextAnchor.MiddleRight;
                    GUI.Label(new Rect(anchorX, rowY, totalWidth - dotSize - 4, statusRowH), text, statusStyle);
                }
            }

            // --- Controller/keyboard indicator ---
            float modeY = anchorY + inputHeight + statusGap + statusTotalH + 2;
            GUIStyle modeStyle = new GUIStyle(GUI.skin.label);
            modeStyle.fontSize = 11;
            modeStyle.normal.textColor = new Color(1f, 1f, 1f, 0.5f);
            modeStyle.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(anchorX, modeY, totalWidth, 16),
                isGamepad ? "Gamepad" : "Keyboard", modeStyle);
        }

        private void DrawKey(float x, float y, float w, float h, bool active)
        {
            Color col = active ? new Color(0.3f, 1f, 0.3f, 0.9f) : new Color(0.3f, 0.3f, 0.3f, 0.5f);
            Color prev = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(new Rect(x, y, w, h), texWhite);
            GUI.color = prev;
        }

        private void DrawActionKey(float x, float y, float w, float h, string label, string bind, bool active, GUIStyle style)
        {
            DrawKey(x, y, w, h, active);
            style.normal.textColor = active ? Color.black : new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(x, y, w, h), $"{label}: {bind}", style);
        }

        private void DrawKeyLabel(float x, float y, float w, float h, string text, bool active, GUIStyle style)
        {
            style.normal.textColor = active ? Color.black : new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(x, y, w, h), text, style);
        }
    }
}
