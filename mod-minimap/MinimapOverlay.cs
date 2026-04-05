using UnityEngine;
using IGTAPMod;

namespace IGTAPMinimap
{
    public class MinimapOverlay : MonoBehaviour
    {
        private Texture2D texWhite;
        private float logTimer;

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

            if (Plugin.CycleViewModeKey.Value.IsDown())
                Plugin.CycleViewMode();

            logTimer -= Time.deltaTime;
        }

        private void OnGUI()
        {
            if (!Plugin.Enabled.Value) return;
            if (!MinimapData.EnsureReady()) return;

            if (texWhite == null) texWhite = MakeTex(Color.white);

            int size = Plugin.MinimapSize.Value;
            int margin = 10;
            float opacity = Plugin.MinimapOpacity.Value;

            Rect mapRect = GetMinimapRect(size, margin);

            // Draw background
            Color prevColor = GUI.color;
            GUI.color = new Color(0.05f, 0.05f, 0.1f, opacity);
            GUI.DrawTexture(mapRect, texWhite);
            GUI.color = prevColor;

            // View bounds (may extend beyond world)
            Vector2 viewMin, viewMax;
            GetViewBounds(out viewMin, out viewMax);

            // Intersection of view and world — the region where we actually have texture data
            Vector2 visMin = Vector2.Max(viewMin, MinimapData.WorldMin);
            Vector2 visMax = Vector2.Min(viewMax, MinimapData.WorldMax);

            if (visMax.x <= visMin.x || visMax.y <= visMin.y)
                goto DrawUI;

            // Where does the visible region sit within the mapRect?
            // Map using the unclamped view bounds so the player stays correctly positioned
            float sLeft = Mathf.InverseLerp(viewMin.x, viewMax.x, visMin.x);
            float sRight = Mathf.InverseLerp(viewMin.x, viewMax.x, visMax.x);
            float sBottom = Mathf.InverseLerp(viewMin.y, viewMax.y, visMin.y);
            float sTop = Mathf.InverseLerp(viewMin.y, viewMax.y, visMax.y);

            // Screen sub-rect where the texture is drawn (Y flipped for GUI)
            Rect texScreenRect = new Rect(
                mapRect.x + sLeft * mapRect.width,
                mapRect.y + (1f - sTop) * mapRect.height,
                (sRight - sLeft) * mapRect.width,
                (sTop - sBottom) * mapRect.height);

            // UV coords for the visible region within the texture
            float uMin = (visMin.x - MinimapData.WorldMin.x) / (MinimapData.WorldMax.x - MinimapData.WorldMin.x);
            float uMax = (visMax.x - MinimapData.WorldMin.x) / (MinimapData.WorldMax.x - MinimapData.WorldMin.x);
            float vMin = (visMin.y - MinimapData.WorldMin.y) / (MinimapData.WorldMax.y - MinimapData.WorldMin.y);
            float vMax = (visMax.y - MinimapData.WorldMin.y) / (MinimapData.WorldMax.y - MinimapData.WorldMin.y);

            Rect texCoords = new Rect(uMin, vMin, uMax - uMin, vMax - vMin);

            GUI.color = Color.white;
            GUI.DrawTextureWithTexCoords(texScreenRect, MinimapData.Texture, texCoords);

            // Draw player dot (positioned relative to unclamped view bounds)
            var player = GameState.Player;
            if (player != null)
            {
                Vector2 playerWorld = player.transform.position;
                if (logTimer <= 0f)
                {
                    Plugin.Log.LogInfo($"[{Plugin.CurrentViewMode.Value}] player=({playerWorld.x:F0},{playerWorld.y:F0}) view=({viewMin.x:F0},{viewMin.y:F0})->({viewMax.x:F0},{viewMax.y:F0}) vis=({visMin.x:F0},{visMin.y:F0})->({visMax.x:F0},{visMax.y:F0}) uv=({uMin:F3},{vMin:F3},{uMax:F3},{vMax:F3}) texScreen=({texScreenRect.x:F0},{texScreenRect.y:F0},{texScreenRect.width:F0},{texScreenRect.height:F0})");
                    logTimer = 3f;
                }
                float px = Mathf.InverseLerp(viewMin.x, viewMax.x, playerWorld.x);
                float py = Mathf.InverseLerp(viewMin.y, viewMax.y, playerWorld.y);

                if (px >= 0f && px <= 1f && py >= 0f && py <= 1f)
                {
                    float dotSize = 6f;
                    float screenX = mapRect.x + px * mapRect.width - dotSize / 2f;
                    float screenY = mapRect.y + (1f - py) * mapRect.height - dotSize / 2f;

                    GUI.color = new Color(0f, 1f, 0.8f, 1f);
                    GUI.DrawTexture(new Rect(screenX, screenY, dotSize, dotSize), texWhite);
                    GUI.color = prevColor;
                }
            }

            DrawUI:
            // Draw border
            DrawBorder(mapRect, new Color(0.5f, 0.5f, 0.6f, opacity * 0.8f));

            // Draw view mode label
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 10;
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
            labelStyle.alignment = TextAnchor.LowerRight;

            string modeLabel;
            switch (Plugin.CurrentViewMode.Value)
            {
                case ViewMode.FollowPlayer: modeLabel = "Follow"; break;
                case ViewMode.CurrentCourse: modeLabel = "Course"; break;
                default: modeLabel = "World"; break;
            }
            GUI.Label(new Rect(mapRect.x, mapRect.y + mapRect.height - 16, mapRect.width - 4, 16),
                modeLabel, labelStyle);

            GUI.color = prevColor;
        }

        private Rect GetMinimapRect(int size, int margin)
        {
            float x, y;
            switch (Plugin.Position.Value)
            {
                case ScreenCorner.TopLeft:
                    x = margin;
                    y = margin;
                    break;
                case ScreenCorner.TopRight:
                    x = Screen.width - size - margin;
                    y = margin;
                    break;
                case ScreenCorner.BottomLeft:
                    x = margin;
                    y = Screen.height - size - margin;
                    break;
                default: // BottomRight
                    x = Screen.width - size - margin;
                    y = Screen.height - size - margin;
                    break;
            }
            return new Rect(x, y, size, size);
        }

        private void GetViewBounds(out Vector2 viewMin, out Vector2 viewMax)
        {
            switch (Plugin.CurrentViewMode.Value)
            {
                case ViewMode.FollowPlayer:
                {
                    var player = GameState.Player;
                    Vector2 center = player != null
                        ? (Vector2)player.transform.position
                        : (MinimapData.WorldMin + MinimapData.WorldMax) / 2f;

                    float halfExtent = Plugin.FollowZoom.Value * Plugin.MinimapSize.Value / 2f;
                    viewMin = center - new Vector2(halfExtent, halfExtent);
                    viewMax = center + new Vector2(halfExtent, halfExtent);
                    break;
                }
                case ViewMode.CurrentCourse:
                {
                    var player = GameState.Player;
                    if (player != null && MinimapData.Courses.Count > 0)
                    {
                        Vector2 playerPos = player.transform.position;
                        float bestDist = float.MaxValue;
                        int bestIdx = 0;
                        for (int i = 0; i < MinimapData.Courses.Count; i++)
                        {
                            var cb = MinimapData.Courses[i];
                            Vector2 clamped = new Vector2(
                                Mathf.Clamp(playerPos.x, cb.Min.x, cb.Max.x),
                                Mathf.Clamp(playerPos.y, cb.Min.y, cb.Max.y));
                            float dist = Vector2.Distance(playerPos, clamped);
                            if (dist < bestDist)
                            {
                                bestDist = dist;
                                bestIdx = i;
                            }
                        }

                        var course = MinimapData.Courses[bestIdx];
                        viewMin = course.Min;
                        viewMax = course.Max;
                    }
                    else
                    {
                        viewMin = MinimapData.WorldMin;
                        viewMax = MinimapData.WorldMax;
                    }
                    break;
                }
                default: // FullWorld
                    viewMin = MinimapData.WorldMin;
                    viewMax = MinimapData.WorldMax;
                    break;
            }
        }

        private void DrawBorder(Rect rect, Color col)
        {
            Color prev = GUI.color;
            GUI.color = col;
            float t = 1f;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, t), texWhite);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - t, rect.width, t), texWhite);
            GUI.DrawTexture(new Rect(rect.x, rect.y, t, rect.height), texWhite);
            GUI.DrawTexture(new Rect(rect.xMax - t, rect.y, t, rect.height), texWhite);
            GUI.color = prev;
        }
    }
}
