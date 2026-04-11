using System.Collections.Generic;
using UnityEngine;

namespace IGTAPMod
{
    /// <summary>
    /// Styled IMGUI drawing toolkit for overlays (InputViz, Minimap, Replay).
    /// Provides procedural textures, cached GUIStyles, and drawing helpers
    /// that match the game's dark blue-purple theme.
    /// </summary>
    public static class IMGUIStyle
    {
        // =====================================================================
        //  Texture cache
        // =====================================================================

        private static readonly Dictionary<long, Texture2D> _texCache = new Dictionary<long, Texture2D>();
        private static Texture2D _white;

        /// <summary>
        /// 1x1 white pixel texture for tinting.
        /// </summary>
        public static Texture2D White
        {
            get
            {
                if (_white == null)
                {
                    _white = new Texture2D(1, 1);
                    _white.SetPixel(0, 0, Color.white);
                    _white.Apply();
                }
                return _white;
            }
        }

        /// <summary>
        /// Get a cached solid-color 1x1 texture.
        /// </summary>
        public static Texture2D SolidTex(Color color)
        {
            long key = ((long)(color.r * 255) << 24) | ((long)(color.g * 255) << 16) |
                       ((long)(color.b * 255) << 8) | (long)(color.a * 255);
            if (_texCache.TryGetValue(key, out var tex) && tex != null)
                return tex;

            tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            _texCache[key] = tex;
            return tex;
        }

        /// <summary>
        /// Generate a rounded rectangle texture with optional border.
        /// </summary>
        public static Texture2D RoundedRect(int w, int h, float radius, Color fill, Color? border = null)
        {
            var tex = new Texture2D(w, h);
            float borderWidth = border.HasValue ? 1f : 0f;
            Color borderColor = border ?? fill;
            Color clear = new Color(0, 0, 0, 0);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dist = DistToRoundedRect(x, y, w, h, radius);
                    if (dist > 0.5f)
                    {
                        tex.SetPixel(x, y, clear);
                    }
                    else if (border.HasValue && dist > -borderWidth)
                    {
                        float alpha = Mathf.Clamp01(0.5f - dist);
                        Color c = borderColor;
                        c.a *= alpha;
                        tex.SetPixel(x, y, c);
                    }
                    else
                    {
                        float alpha = Mathf.Clamp01(0.5f - dist);
                        Color c = fill;
                        c.a *= alpha;
                        tex.SetPixel(x, y, c);
                    }
                }
            }

            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        private static float DistToRoundedRect(float px, float py, int w, int h, float r)
        {
            float cx = Mathf.Clamp(px, r, w - 1 - r);
            float cy = Mathf.Clamp(py, r, h - 1 - r);
            return Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy)) - r;
        }

        // =====================================================================
        //  Cached GUIStyle presets
        // =====================================================================

        private static readonly Dictionary<int, GUIStyle> _labelCache = new Dictionary<int, GUIStyle>();
        private static readonly Dictionary<int, GUIStyle> _buttonCache = new Dictionary<int, GUIStyle>();
        private static GUIStyle _boxStyle;
        private static bool _stylesInit;
        private static Texture2D _panelBg;
        private static Texture2D _panelBgLight;
        private static Texture2D _buttonNormal;
        private static Texture2D _buttonHover;
        private static Texture2D _buttonActive;

        private static void EnsureStyles()
        {
            if (_stylesInit) return;
            _stylesInit = true;

            _panelBg = RoundedRect(32, 32, 6f, UIStyle.PanelBackground, UIStyle.PanelBorder);
            _panelBgLight = RoundedRect(32, 32, 4f, UIStyle.PanelBackgroundLight);
            _buttonNormal = RoundedRect(32, 32, 4f, UIStyle.ButtonNormal);
            _buttonHover = RoundedRect(32, 32, 4f, UIStyle.ButtonHighlight);
            _buttonActive = RoundedRect(32, 32, 4f, UIStyle.ButtonPressed);
        }

        /// <summary>
        /// Get a cached label style with the specified font size.
        /// </summary>
        public static GUIStyle Label(int fontSize = 14, FontStyle fontStyle = FontStyle.Normal,
            TextAnchor alignment = TextAnchor.MiddleLeft, Color? color = null)
        {
            int key = fontSize * 1000 + (int)fontStyle * 10 + (int)alignment;
            if (_labelCache.TryGetValue(key, out var style) && style != null)
            {
                style.normal.textColor = color ?? UIStyle.TextPrimary;
                return style;
            }

            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = fontStyle,
                alignment = alignment,
                wordWrap = false,
                clipping = TextClipping.Clip,
            };
            style.normal.textColor = color ?? UIStyle.TextPrimary;
            _labelCache[key] = style;
            return style;
        }

        /// <summary>
        /// Get a cached button style matching the game theme.
        /// </summary>
        public static GUIStyle Button(int fontSize = 12)
        {
            EnsureStyles();
            if (_buttonCache.TryGetValue(fontSize, out var style) && style != null)
                return style;

            style = new GUIStyle(GUI.skin.button)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(6, 6, 6, 6),
            };
            style.normal.background = _buttonNormal;
            style.normal.textColor = UIStyle.TextPrimary;
            style.hover.background = _buttonHover;
            style.hover.textColor = UIStyle.TextPrimary;
            style.active.background = _buttonActive;
            style.active.textColor = UIStyle.TextPrimary;
            style.focused.background = _buttonHover;
            style.focused.textColor = UIStyle.TextPrimary;

            _buttonCache[fontSize] = style;
            return style;
        }

        /// <summary>
        /// Get a box/panel style with dark rounded background.
        /// </summary>
        public static GUIStyle Box()
        {
            EnsureStyles();
            if (_boxStyle != null) return _boxStyle;

            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                border = new RectOffset(8, 8, 8, 8),
                padding = new RectOffset(8, 8, 6, 6),
            };
            _boxStyle.normal.background = _panelBg;
            _boxStyle.normal.textColor = UIStyle.TextPrimary;

            return _boxStyle;
        }

        // =====================================================================
        //  Drawing helpers
        // =====================================================================

        /// <summary>
        /// Draw a themed panel background with border.
        /// </summary>
        public static void DrawPanel(Rect rect, float opacity = 0.92f)
        {
            EnsureStyles();
            Color prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, opacity);
            GUI.DrawTexture(rect, _panelBg, ScaleMode.StretchToFill);
            GUI.color = prev;
        }

        /// <summary>
        /// Draw a lighter panel background (for rows, sections).
        /// </summary>
        public static void DrawPanelLight(Rect rect, float opacity = 0.9f)
        {
            EnsureStyles();
            Color prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, opacity);
            GUI.DrawTexture(rect, _panelBgLight, ScaleMode.StretchToFill);
            GUI.color = prev;
        }

        /// <summary>
        /// Draw a themed button rectangle.
        /// </summary>
        public static bool DrawButton(Rect rect, string text, bool active = false, int fontSize = 12)
        {
            EnsureStyles();
            var style = Button(fontSize);
            if (active)
            {
                Color prev = GUI.color;
                GUI.color = UIStyle.Accent;
                bool result = GUI.Button(rect, text, style);
                GUI.color = prev;
                return result;
            }
            return GUI.Button(rect, text, style);
        }

        /// <summary>
        /// Draw a border around a rect.
        /// </summary>
        public static void DrawBorder(Rect rect, Color color, float thickness = 1f)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), White);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), White);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), White);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), White);
            GUI.color = prev;
        }

        /// <summary>
        /// Draw a filled circle/dot.
        /// </summary>
        public static void DrawDot(Vector2 center, float radius, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), White);
            GUI.color = prev;
        }

        /// <summary>
        /// Draw a progress/fill bar.
        /// </summary>
        public static void DrawProgressBar(Rect rect, float value, Color fill, Color? bg = null)
        {
            Color prev = GUI.color;
            // Background
            GUI.color = bg ?? UIStyle.SliderBackground;
            GUI.DrawTexture(rect, White);
            // Fill
            if (value > 0f)
            {
                GUI.color = fill;
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(value), rect.height), White);
            }
            GUI.color = prev;
        }

        /// <summary>
        /// Draw text with a shadow/outline for readability on any background.
        /// </summary>
        public static void DrawTextShadow(Rect rect, string text, GUIStyle style,
            Color textColor, Color? shadowColor = null)
        {
            Color shadow = shadowColor ?? new Color(0, 0, 0, textColor.a * 0.8f);
            style.normal.textColor = shadow;
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text, style);
            style.normal.textColor = textColor;
            GUI.Label(rect, text, style);
        }

        /// <summary>
        /// Draw a horizontal line separator.
        /// </summary>
        public static void DrawSeparator(Rect rect)
        {
            Color prev = GUI.color;
            GUI.color = UIStyle.Separator;
            GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height / 2f, rect.width, 1f), White);
            GUI.color = prev;
        }

        /// <summary>
        /// Draw a tooltip panel at a position.
        /// </summary>
        public static void DrawTooltip(Vector2 pos, string text, int fontSize = 12)
        {
            EnsureStyles();
            var style = Label(fontSize);
            var content = new GUIContent(text);
            var size = style.CalcSize(content);
            float pad = 6f;
            Rect bg = new Rect(pos.x + 12, pos.y + 12,
                size.x + pad * 2, size.y + pad * 2);

            // Keep on screen
            if (bg.xMax > Screen.width) bg.x = Screen.width - bg.width;
            if (bg.yMax > Screen.height) bg.y = Screen.height - bg.height;

            DrawPanel(bg, 0.95f);
            style.normal.textColor = UIStyle.TextPrimary;
            GUI.Label(new Rect(bg.x + pad, bg.y + pad, size.x, size.y), text, style);
        }
    }
}
