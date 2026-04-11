using System.Collections.Generic;
using UnityEngine;

namespace IGTAPSpeedrun
{
    public static class SpeedrunHUD
    {
        private static Texture2D bgTexture;
        private static GUIStyle headerStyle;
        private static GUIStyle timeStyle;
        private static GUIStyle deltaAheadStyle;
        private static GUIStyle deltaBehindStyle;
        private static GUIStyle splitStyle;
        private static GUIStyle splitDimStyle;
        private static GUIStyle splitRightStyle;
        private static GUIStyle labelStyle;
        private static GUIStyle newPBStyle;
        private static bool stylesInitialized;

        private const float PanelWidth = 240f;
        private const float PanelPadding = 8f;
        private const float LineHeight = 20f;

        private static void InitStyles()
        {
            if (stylesInitialized) return;

            bgTexture = new Texture2D(1, 1);
            bgTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.75f));
            bgTexture.Apply();

            headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            headerStyle.normal.textColor = Color.white;

            timeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight
            };
            timeStyle.normal.textColor = Color.white;

            deltaAheadStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleRight
            };
            deltaAheadStyle.normal.textColor = new Color(0.3f, 1f, 0.3f); // green

            deltaBehindStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleRight
            };
            deltaBehindStyle.normal.textColor = new Color(1f, 0.3f, 0.3f); // red

            splitStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13
            };
            splitStyle.normal.textColor = Color.white;

            splitRightStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleRight
            };
            splitRightStyle.normal.textColor = Color.white;

            splitDimStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13
            };
            splitDimStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft
            };
            labelStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);

            newPBStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            newPBStyle.normal.textColor = new Color(1f, 0.85f, 0f); // gold

            stylesInitialized = true;
        }

        public static void Draw(SpeedrunTimer timer)
        {
            InitStyles();

            // Calculate panel height
            int splitCount = timer.Splits.Count;
            int upcomingCount = GetUpcomingSplits(timer).Count;
            float height = PanelPadding * 2 // top/bottom padding
                + LineHeight           // header
                + 32f                  // time (larger)
                + LineHeight           // delta or spacer
                + (Plugin.ShowDeathCount.Value ? LineHeight : 0f) // deaths
                + 4f                   // separator
                + splitCount * LineHeight  // completed splits
                + upcomingCount * LineHeight // upcoming splits
                + (timer.State == SpeedrunTimer.TimerState.Finished ? 28f : 0f); // NEW PB

            float x, y;
            switch (Plugin.HUDPosition.Value)
            {
                case ScreenCorner.TopLeft:
                    x = 10f; y = 10f; break;
                case ScreenCorner.TopRight:
                    x = Screen.width - PanelWidth - 10f; y = 10f; break;
                case ScreenCorner.BottomRight:
                    x = Screen.width - PanelWidth - 10f; y = Screen.height - height - 10f; break;
                default: // BottomLeft
                    x = 10f; y = Screen.height - height - 10f; break;
            }
            Rect panelRect = new Rect(x, y, PanelWidth, height);

            // Background
            GUI.DrawTexture(panelRect, bgTexture);

            float cy = y + PanelPadding;

            // Header
            string headerText = timer.IsProfileMode ? timer.ActiveProfile.name : "IGTAP Speedrun";
            GUI.Label(new Rect(x, cy, PanelWidth, LineHeight), headerText, headerStyle);
            cy += LineHeight;

            float padRight = PanelWidth - PanelPadding;

            // Current time
            string timeText = FormatTime(timer.CurrentTime);
            GUI.Label(new Rect(x, cy, padRight, 32f), timeText, timeStyle);
            cy += 32f;

            // Delta vs PB
            float? totalDelta = timer.GetTotalDelta();
            if (totalDelta.HasValue)
            {
                string deltaText = FormatDelta(totalDelta.Value);
                var style = totalDelta.Value <= 0 ? deltaAheadStyle : deltaBehindStyle;
                GUI.Label(new Rect(x, cy, padRight, LineHeight), deltaText, style);
            }
            cy += LineHeight;

            // Death count
            if (Plugin.ShowDeathCount.Value)
            {
                GUI.Label(new Rect(x + PanelPadding, cy, padRight - PanelPadding, LineHeight), "Deaths:", labelStyle);
                GUI.Label(new Rect(x, cy, padRight, LineHeight), timer.DeathCount.ToString(), splitRightStyle);
                cy += LineHeight;
            }

            // Separator
            DrawSeparator(x + PanelPadding, cy, PanelWidth - PanelPadding * 2);
            cy += 4f;

            // Completed splits
            foreach (var split in timer.Splits)
            {
                DrawSplitRow(x, cy, split, timer);
                cy += LineHeight;
            }

            // Upcoming splits (from PB, dimmed)
            var upcoming = GetUpcomingSplits(timer);
            foreach (var upSplit in upcoming)
            {
                GUI.Label(new Rect(x + PanelPadding, cy, PanelWidth - PanelPadding * 2, LineHeight),
                    $"\u00b7 {upSplit.label}", splitDimStyle);
                cy += LineHeight;
            }

            // NEW PB indicator
            if (timer.State == SpeedrunTimer.TimerState.Finished)
            {
                var pb = timer.CurrentPB;
                bool isNewPB = pb != null && timer.CurrentTime <= pb.totalTime;
                if (isNewPB)
                {
                    GUI.Label(new Rect(x, cy, PanelWidth, 28f), "NEW PB!", newPBStyle);
                }
            }
        }

        private static void DrawSplitRow(float x, float y, Split split, SpeedrunTimer timer)
        {
            float innerX = x + PanelPadding;
            float innerW = PanelWidth - PanelPadding * 2;

            // Label (left-aligned)
            GUI.Label(new Rect(innerX, y, innerW * 0.45f, LineHeight), split.Label, splitStyle);

            // Time (right-aligned) — segment time in profile mode, absolute in auto-detect
            string time = FormatTime(timer.GetDisplayTime(split));
            GUI.Label(new Rect(innerX + innerW * 0.45f, y, innerW * 0.3f, LineHeight), time, splitRightStyle);

            // Delta (right-aligned, colored)
            float? delta = timer.GetDelta(split);
            if (delta.HasValue)
            {
                string deltaText = FormatDelta(delta.Value);
                var style = delta.Value <= 0 ? deltaAheadStyle : deltaBehindStyle;
                GUI.Label(new Rect(innerX + innerW * 0.75f, y, innerW * 0.25f, LineHeight), deltaText, style);
            }
        }

        private static void DrawSeparator(float x, float y, float width)
        {
            if (bgTexture == null) return;
            var oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.3f);
            GUI.DrawTexture(new Rect(x, y + 1, width, 1), Texture2D.whiteTexture);
            GUI.color = oldColor;
        }

        private static List<PBSplit> GetUpcomingSplits(SpeedrunTimer timer)
        {
            var result = new List<PBSplit>();

            var completedIds = new HashSet<string>();
            foreach (var s in timer.Splits)
                completedIds.Add(s.Id);

            if (timer.IsProfileMode)
            {
                // Show remaining splits from the profile's ordered list
                foreach (var def in timer.ActiveProfile.splits)
                {
                    if (!completedIds.Contains(def.id))
                        result.Add(new PBSplit { id = def.id, label = def.label });
                }
            }
            else if (timer.CurrentPB != null)
            {
                foreach (var pbSplit in timer.CurrentPB.splits)
                {
                    if (!completedIds.Contains(pbSplit.id) && Plugin.IsSplitEnabled(pbSplit.id))
                        result.Add(pbSplit);
                }
            }
            return result;
        }

        public static string FormatTime(float seconds)
        {
            if (seconds < 0) seconds = 0;
            int minutes = (int)(seconds / 60f);
            float secs = seconds - minutes * 60f;
            if (minutes > 0)
                return $"{minutes}:{secs:00.00}";
            return $"{secs:0.00}";
        }

        public static string FormatDelta(float delta)
        {
            string sign = delta >= 0 ? "+" : "";
            return $"{sign}{delta:0.00}";
        }
    }
}
