using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IGTAPMod
{
    /// <summary>
    /// Anchor presets for positioning UI elements within their parent.
    /// </summary>
    public enum UIAnchor
    {
        TopLeft, TopCenter, TopRight,
        MiddleLeft, Center, MiddleRight,
        BottomLeft, BottomCenter, BottomRight,
        StretchAll
    }

    /// <summary>
    /// Public API for creating and querying in-game UI (uGUI / Canvas-based).
    /// Creates UIs using the same system as the game itself (Canvas + TextMeshPro).
    ///
    /// Usage from another BepInEx plugin:
    ///   var canvas = GameUI.CreateScreenCanvas("MyUI");
    ///   var panel = GameUI.CreatePanel(canvas.transform, "Panel");
    ///   GameUI.AddVerticalLayout(panel);
    ///   GameUI.CreateText(panel.transform, "Title", "Hello!", UIStyle.FontSizeHeader);
    ///   GameUI.CreateButton(panel.transform, "Btn", "Click Me", () => Debug.Log("Clicked!"));
    /// </summary>
    public static class GameUI
    {
        private static TMP_FontAsset _cachedFont;

        // =====================================================================
        //  Font
        // =====================================================================

        /// <summary>
        /// Get the game's primary TMP font asset (cached).
        /// Falls back to the first TMP_FontAsset found in loaded resources.
        /// Returns null if no font is available yet.
        /// </summary>
        public static TMP_FontAsset GetGameFont()
        {
            if (_cachedFont != null) return _cachedFont;

            var existing = UnityEngine.Object.FindAnyObjectByType<TMP_Text>();
            if (existing != null)
            {
                _cachedFont = existing.font;
                return _cachedFont;
            }

            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            if (fonts.Length > 0)
                _cachedFont = fonts[0];

            return _cachedFont;
        }

        // =====================================================================
        //  Canvas Creation
        // =====================================================================

        /// <summary>
        /// Create a screen-space overlay canvas suitable for HUD/menu UI.
        /// Includes CanvasScaler (1920x1080 reference) and GraphicRaycaster.
        /// </summary>
        public static Canvas CreateScreenCanvas(string name, int sortOrder = 100)
        {
            var go = new GameObject(name);
            UnityEngine.Object.DontDestroyOnLoad(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();

            return canvas;
        }

        /// <summary>
        /// Create a world-space canvas for in-world UI (labels, health bars, etc.).
        /// </summary>
        public static Canvas CreateWorldCanvas(string name, Vector3 position,
            float scale = 0.01f, Vector2? size = null)
        {
            var go = new GameObject(name);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rt = go.GetComponent<RectTransform>();
            rt.position = position;
            rt.localScale = Vector3.one * scale;
            rt.sizeDelta = size ?? new Vector2(400, 200);

            go.AddComponent<GraphicRaycaster>();

            return canvas;
        }

        // =====================================================================
        //  Element Creation
        // =====================================================================

        /// <summary>
        /// Create a panel with a background color. Returns its RectTransform.
        /// </summary>
        public static RectTransform CreatePanel(Transform parent, string name,
            Color? color = null, bool applyTheme = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();

            if (applyTheme)
            {
                var theme = GameUITheme.Instance;
                if (theme != null && theme.IsReady)
                    theme.ApplyPanelStyle(image);
                else
                    image.color = color ?? UIStyle.PanelBackground;
            }
            else
            {
                image.color = color ?? UIStyle.PanelBackground;
            }

            return go.GetComponent<RectTransform>();
        }

        /// <summary>
        /// Create a TextMeshPro text element.
        /// </summary>
        public static TextMeshProUGUI CreateText(Transform parent, string name, string text,
            float fontSize = 18f, Color? color = null,
            TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color ?? UIStyle.TextPrimary;
            tmp.alignment = alignment;

            var font = GetGameFont();
            if (font != null) tmp.font = font;

            return tmp;
        }

        /// <summary>
        /// Create a clickable button with a text label.
        /// Uses the game's actual button sprite and color block when available.
        /// </summary>
        public static Button CreateButton(Transform parent, string name, string label,
            Action onClick, float fontSize = 20f, Color? bgColor = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.color = Color.white;

            // Use game's sprite for rounded shape, our own color block for contrast
            var theme = GameUITheme.Instance;
            if (theme != null && theme.IsReady && theme.ButtonSprite != null)
            {
                image.sprite = theme.ButtonSprite;
                image.type = Image.Type.Sliced;
            }

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = bgColor ?? UIStyle.ButtonNormal;
            colors.highlightedColor = UIStyle.ButtonHighlight;
            colors.pressedColor = UIStyle.ButtonPressed;
            colors.selectedColor = UIStyle.ButtonHighlight;
            colors.disabledColor = UIStyle.ButtonDisabled;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.1f;
            button.colors = colors;

            if (onClick != null)
                button.onClick.AddListener(() => onClick());

            // Label text fills the button
            var labelTmp = CreateText(go.transform, "Label", label, fontSize,
                alignment: TextAlignmentOptions.Center);
            var labelRt = labelTmp.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(4, 2);
            labelRt.offsetMax = new Vector2(-4, -2);

            return button;
        }

        /// <summary>
        /// Create a toggle (checkbox) with a label.
        /// </summary>
        public static Toggle CreateToggle(Transform parent, string name, string label,
            bool value, Action<bool> onChanged = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            // Checkbox background
            var bgObj = new GameObject("Background", typeof(RectTransform));
            bgObj.transform.SetParent(go.transform, false);
            var bgImage = bgObj.AddComponent<Image>();
            bgImage.color = UIStyle.ButtonNormal;
            var bgLayout = bgObj.AddComponent<LayoutElement>();
            bgLayout.preferredWidth = 28;
            bgLayout.preferredHeight = 28;

            // Use game's toggle sprite if available
            var theme = GameUITheme.Instance;
            if (theme != null && theme.IsReady && theme.ToggleBgSprite != null)
            {
                bgImage.sprite = theme.ToggleBgSprite;
                bgImage.type = Image.Type.Sliced;
            }

            // Checkmark
            var checkObj = new GameObject("Checkmark", typeof(RectTransform));
            checkObj.transform.SetParent(bgObj.transform, false);
            var checkRt = checkObj.GetComponent<RectTransform>();
            checkRt.anchorMin = new Vector2(0.1f, 0.1f);
            checkRt.anchorMax = new Vector2(0.9f, 0.9f);
            checkRt.offsetMin = Vector2.zero;
            checkRt.offsetMax = Vector2.zero;
            var checkImage = checkObj.AddComponent<Image>();
            checkImage.color = UIStyle.Accent;
            if (theme != null && theme.IsReady && theme.ToggleCheckSprite != null)
            {
                checkImage.sprite = theme.ToggleCheckSprite;
                checkImage.type = Image.Type.Simple;
            }

            // Label text
            var labelTmp = CreateText(go.transform, "Label", label, UIStyle.FontSizeBody);
            var labelLayout = labelTmp.gameObject.AddComponent<LayoutElement>();
            labelLayout.flexibleWidth = 1;

            // Toggle component
            var toggle = go.AddComponent<Toggle>();
            toggle.isOn = value;
            toggle.graphic = checkImage;
            toggle.targetGraphic = bgImage;

            // Use game's sprite for shape but keep our own colors
            var toggleTheme = GameUITheme.Instance;
            if (toggleTheme != null && toggleTheme.IsReady)
            {
                if (toggleTheme.ToggleBgSprite != null)
                {
                    bgImage.sprite = toggleTheme.ToggleBgSprite;
                    bgImage.type = Image.Type.Sliced;
                }
                if (toggleTheme.ToggleCheckSprite != null)
                {
                    checkImage.sprite = toggleTheme.ToggleCheckSprite;
                    checkImage.type = Image.Type.Simple;
                }
            }
            // Game-matching colors: white bg, green hover, blue checkmark
            bgImage.color = Color.white;
            checkImage.color = UIStyle.Accent;
            var toggleColors = toggle.colors;
            toggleColors.normalColor = UIStyle.ControlNormal;
            toggleColors.highlightedColor = UIStyle.ControlHighlight;
            toggleColors.pressedColor = UIStyle.ControlPressed;
            toggleColors.colorMultiplier = 1f;
            toggleColors.fadeDuration = 0.1f;
            toggle.colors = toggleColors;

            if (onChanged != null)
                toggle.onValueChanged.AddListener((v) => onChanged(v));

            return toggle;
        }

        /// <summary>
        /// Create a slider with an optional label.
        /// </summary>
        public static Slider CreateSlider(Transform parent, string name, string label,
            float min, float max, float value, Action<float> onChanged = null,
            bool wholeNumbers = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            // If label provided, wrap in vertical layout
            if (!string.IsNullOrEmpty(label))
            {
                var vlayout = go.AddComponent<VerticalLayoutGroup>();
                vlayout.spacing = 4;
                vlayout.childForceExpandWidth = true;
                vlayout.childForceExpandHeight = false;
                vlayout.childControlWidth = true;
                vlayout.childControlHeight = true;

                CreateText(go.transform, "Label", label, UIStyle.FontSizeBody);
            }

            // Slider object
            var sliderObj = label != null
                ? new GameObject("SliderControl", typeof(RectTransform))
                : go;
            if (label != null)
            {
                sliderObj.transform.SetParent(go.transform, false);
                var sliderLayout = sliderObj.AddComponent<LayoutElement>();
                sliderLayout.preferredHeight = 20;
            }

            // Background
            var bgObj = CreateChild(sliderObj.transform, "Background");
            StretchFill(bgObj);
            var bgImage = bgObj.AddComponent<Image>();
            bgImage.color = UIStyle.SliderBackground;

            // Fill Area + Fill
            var fillArea = CreateChild(sliderObj.transform, "FillArea");
            StretchFill(fillArea);

            var fill = CreateChild(fillArea.transform, "Fill");
            var fillRt = fill.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = new Vector2(0, 1);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            var fillImage = fill.AddComponent<Image>();
            fillImage.color = UIStyle.SliderFill;

            // Handle Slide Area + Handle
            var handleArea = CreateChild(sliderObj.transform, "HandleSlideArea");
            StretchFill(handleArea);

            var handle = CreateChild(handleArea.transform, "Handle");
            var handleRt = handle.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(16, 0);
            var handleImage = handle.AddComponent<Image>();
            handleImage.color = UIStyle.SliderHandle;

            // Slider component
            var slider = sliderObj.AddComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImage;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            slider.wholeNumbers = wholeNumbers;

            // Apply game theme if available
            var theme = GameUITheme.Instance;
            if (theme != null && theme.IsReady)
                theme.ApplySliderStyle(slider, bgImage, fillImage, handleImage);

            if (onChanged != null)
                slider.onValueChanged.AddListener((v) => onChanged(v));

            return slider;
        }

        /// <summary>
        /// Create a scroll view with a content area. Returns the content RectTransform
        /// where you should parent your child elements.
        /// </summary>
        public static RectTransform CreateScrollView(Transform parent, string name,
            bool horizontal = false, bool vertical = true, Color? bgColor = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var scrollImage = go.AddComponent<Image>();
            scrollImage.color = bgColor ?? Color.clear;

            var scrollRect = go.AddComponent<ScrollRect>();
            scrollRect.horizontal = horizontal;
            scrollRect.vertical = vertical;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            // Viewport (masks content)
            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(go.transform, false);
            StretchFill(viewport);
            viewport.AddComponent<Image>().color = Color.clear;
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            // Content (parent your elements here)
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = Vector2.one;
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.offsetMin = Vector2.zero;
            contentRt.offsetMax = Vector2.zero;

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewport.GetComponent<RectTransform>();
            scrollRect.content = contentRt;

            return contentRt;
        }

        // =====================================================================
        //  Layout Helpers
        // =====================================================================

        /// <summary>
        /// Add a VerticalLayoutGroup to a RectTransform.
        /// </summary>
        public static VerticalLayoutGroup AddVerticalLayout(RectTransform panel,
            float spacing = 5f, int padding = 10)
        {
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            return layout;
        }

        /// <summary>
        /// Add a HorizontalLayoutGroup to a RectTransform.
        /// </summary>
        public static HorizontalLayoutGroup AddHorizontalLayout(RectTransform panel,
            float spacing = 5f, int padding = 10)
        {
            var layout = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            return layout;
        }

        /// <summary>
        /// Add a ContentSizeFitter to auto-size a panel to its content.
        /// </summary>
        public static ContentSizeFitter AddContentFitter(RectTransform panel,
            ContentSizeFitter.FitMode horizontal = ContentSizeFitter.FitMode.Unconstrained,
            ContentSizeFitter.FitMode vertical = ContentSizeFitter.FitMode.PreferredSize)
        {
            var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = horizontal;
            fitter.verticalFit = vertical;
            return fitter;
        }

        /// <summary>
        /// Set preferred size via LayoutElement (works inside layout groups).
        /// Pass -1 to leave a dimension unconstrained.
        /// </summary>
        public static LayoutElement SetSize(RectTransform rt,
            float width = -1, float height = -1)
        {
            var le = rt.gameObject.GetComponent<LayoutElement>();
            if (le == null) le = rt.gameObject.AddComponent<LayoutElement>();
            if (width >= 0) le.preferredWidth = width;
            if (height >= 0) le.preferredHeight = height;
            return le;
        }

        /// <summary>
        /// Set anchor preset on a RectTransform.
        /// </summary>
        public static void SetAnchor(RectTransform rt, UIAnchor anchor)
        {
            Vector2 min, max;
            switch (anchor)
            {
                case UIAnchor.TopLeft:      min = new Vector2(0, 1); max = new Vector2(0, 1); break;
                case UIAnchor.TopCenter:    min = new Vector2(0.5f, 1); max = new Vector2(0.5f, 1); break;
                case UIAnchor.TopRight:     min = new Vector2(1, 1); max = new Vector2(1, 1); break;
                case UIAnchor.MiddleLeft:   min = new Vector2(0, 0.5f); max = new Vector2(0, 0.5f); break;
                case UIAnchor.Center:       min = new Vector2(0.5f, 0.5f); max = new Vector2(0.5f, 0.5f); break;
                case UIAnchor.MiddleRight:  min = new Vector2(1, 0.5f); max = new Vector2(1, 0.5f); break;
                case UIAnchor.BottomLeft:   min = Vector2.zero; max = Vector2.zero; break;
                case UIAnchor.BottomCenter: min = new Vector2(0.5f, 0); max = new Vector2(0.5f, 0); break;
                case UIAnchor.BottomRight:  min = new Vector2(1, 0); max = new Vector2(1, 0); break;
                case UIAnchor.StretchAll:   min = Vector2.zero; max = Vector2.one; break;
                default:                    min = Vector2.zero; max = Vector2.one; break;
            }
            rt.anchorMin = min;
            rt.anchorMax = max;
        }

        /// <summary>
        /// Make a UI element draggable by the user.
        /// </summary>
        public static DraggableUI MakeDraggable(RectTransform rt)
        {
            return rt.gameObject.AddComponent<DraggableUI>();
        }

        // =====================================================================
        //  Additional Element Creation
        // =====================================================================

        /// <summary>
        /// Create a TMP_InputField for text entry.
        /// </summary>
        public static TMP_InputField CreateInputField(Transform parent, string name,
            string placeholder = "", Action<string> onSubmit = null, float fontSize = 16f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var bgImage = go.AddComponent<Image>();
            bgImage.color = UIStyle.InputFieldBg;
            var theme2 = GameUITheme.Instance;
            if (theme2 != null && theme2.IsReady && theme2.ButtonSprite != null)
            {
                bgImage.sprite = theme2.ButtonSprite;
                bgImage.type = Image.Type.Sliced;
            }

            // Text area viewport
            var textArea = new GameObject("TextArea", typeof(RectTransform));
            textArea.transform.SetParent(go.transform, false);
            var textAreaRt = textArea.GetComponent<RectTransform>();
            textAreaRt.anchorMin = Vector2.zero;
            textAreaRt.anchorMax = Vector2.one;
            textAreaRt.offsetMin = new Vector2(6, 2);
            textAreaRt.offsetMax = new Vector2(-6, -2);
            textArea.AddComponent<RectMask2D>();

            // Placeholder
            var phGo = new GameObject("Placeholder", typeof(RectTransform));
            phGo.transform.SetParent(textArea.transform, false);
            var phRt = phGo.GetComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = Vector2.zero;
            phRt.offsetMax = Vector2.zero;
            var phTmp = phGo.AddComponent<TextMeshProUGUI>();
            phTmp.text = placeholder;
            phTmp.fontSize = fontSize;
            phTmp.color = UIStyle.TextMuted;
            phTmp.alignment = TextAlignmentOptions.MidlineLeft;
            var font = GetGameFont();
            if (font != null) phTmp.font = font;

            // Text display
            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(textArea.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            var textTmp = textGo.AddComponent<TextMeshProUGUI>();
            textTmp.fontSize = fontSize;
            textTmp.color = UIStyle.TextPrimary;
            textTmp.alignment = TextAlignmentOptions.MidlineLeft;
            if (font != null) textTmp.font = font;

            // InputField component
            var inputField = go.AddComponent<TMP_InputField>();
            inputField.textViewport = textAreaRt;
            inputField.textComponent = textTmp;
            inputField.placeholder = phTmp;
            inputField.fontAsset = font;
            inputField.pointSize = fontSize;

            // Caret color
            inputField.caretColor = UIStyle.Accent;
            inputField.selectionColor = new Color(UIStyle.Accent.r, UIStyle.Accent.g, UIStyle.Accent.b, 0.3f);

            if (onSubmit != null)
                inputField.onSubmit.AddListener(s => onSubmit(s));

            return inputField;
        }

        /// <summary>
        /// Create a tab button. Active tab appears raised with accent bar and
        /// connects visually to the content below. Inactive tabs are recessed.
        /// </summary>
        public static Button CreateTabButton(Transform parent, string name, string label,
            Action onClick, bool isActive)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            // Tab background - active is lighter (matches content), inactive is dark
            var image = go.AddComponent<Image>();
            Color activeBg = new Color(0.16f, 0.16f, 0.22f, 1f);
            Color inactiveBg = new Color(0.08f, 0.08f, 0.11f, 1f);
            image.color = isActive ? activeBg : inactiveBg;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = isActive ? activeBg : inactiveBg;
            colors.highlightedColor = isActive ? activeBg : UIStyle.TabHover;
            colors.pressedColor = UIStyle.ButtonPressed;
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = UIStyle.ButtonDisabled;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.1f;
            button.colors = colors;

            if (onClick != null)
                button.onClick.AddListener(() => onClick());

            // Active tab: accent bar on top + raised effect (no bottom border)
            if (isActive)
            {
                // Top accent bar
                var accent = new GameObject("Accent", typeof(RectTransform));
                accent.transform.SetParent(go.transform, false);
                var accentRt = accent.GetComponent<RectTransform>();
                accentRt.anchorMin = new Vector2(0, 1);
                accentRt.anchorMax = new Vector2(1, 1);
                accentRt.pivot = new Vector2(0.5f, 1);
                accentRt.sizeDelta = new Vector2(0, 3);
                accent.AddComponent<Image>().color = UIStyle.Accent;

                // Side highlights (subtle light edges for raised look)
                var leftEdge = new GameObject("LeftEdge", typeof(RectTransform));
                leftEdge.transform.SetParent(go.transform, false);
                var leRt = leftEdge.GetComponent<RectTransform>();
                leRt.anchorMin = new Vector2(0, 0);
                leRt.anchorMax = new Vector2(0, 1);
                leRt.pivot = new Vector2(0, 0.5f);
                leRt.sizeDelta = new Vector2(1, 0);
                leftEdge.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);

                var rightEdge = new GameObject("RightEdge", typeof(RectTransform));
                rightEdge.transform.SetParent(go.transform, false);
                var reRt = rightEdge.GetComponent<RectTransform>();
                reRt.anchorMin = new Vector2(1, 0);
                reRt.anchorMax = new Vector2(1, 1);
                reRt.pivot = new Vector2(1, 0.5f);
                reRt.sizeDelta = new Vector2(1, 0);
                rightEdge.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.15f);
            }
            else
            {
                // Inactive: bottom border to separate from content
                var botBorder = new GameObject("BottomBorder", typeof(RectTransform));
                botBorder.transform.SetParent(go.transform, false);
                var bbRt = botBorder.GetComponent<RectTransform>();
                bbRt.anchorMin = new Vector2(0, 0);
                bbRt.anchorMax = new Vector2(1, 0);
                bbRt.pivot = new Vector2(0.5f, 0);
                bbRt.sizeDelta = new Vector2(0, 1);
                botBorder.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.4f);
            }

            // Label
            var labelTmp = CreateText(go.transform, "Label", label,
                UIStyle.FontSizeSmall,
                color: isActive ? UIStyle.TextPrimary : UIStyle.TextMuted,
                alignment: TextAlignmentOptions.Center);
            if (isActive) labelTmp.fontStyle = TMPro.FontStyles.Bold;
            labelTmp.overflowMode = TextOverflowModes.Masking;
            var labelRt = labelTmp.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(10, 0);
            labelRt.offsetMax = new Vector2(-10, -3);

            return button;
        }

        // =====================================================================
        //  Scene Query Helpers
        // =====================================================================

        /// <summary>
        /// Find a component on a GameObject by name in the scene.
        /// </summary>
        public static T FindByName<T>(string gameObjectName) where T : Component
        {
            var go = GameObject.Find(gameObjectName);
            return go != null ? go.GetComponent<T>() : null;
        }

        /// <summary>
        /// Find all components of a type in the scene (including inactive objects).
        /// </summary>
        public static T[] FindAll<T>() where T : Component
        {
            return Resources.FindObjectsOfTypeAll<T>();
        }

        /// <summary>
        /// Check whether the scene has an EventSystem (required for uGUI interaction).
        /// The game normally provides one, but this can be used to verify.
        /// </summary>
        public static bool HasEventSystem()
        {
            return UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null;
        }

        // =====================================================================
        //  Internal helpers
        // =====================================================================

        private static GameObject CreateChild(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void StretchFill(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }

    /// <summary>
    /// Attach to any UI element to make it draggable by the user.
    /// Typically used via GameUI.MakeDraggable(rt).
    /// </summary>
    public class DraggableUI : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        private Vector2 dragOffset;
        private RectTransform rt;

        private void Awake()
        {
            rt = GetComponent<RectTransform>();
            // Ensure this object can receive pointer events
            if (GetComponent<Graphic>() == null)
            {
                var img = gameObject.AddComponent<Image>();
                img.color = Color.clear;
                img.raycastTarget = true;
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rt.parent as RectTransform, eventData.position,
                eventData.pressEventCamera, out var localPoint);
            dragOffset = (Vector2)rt.localPosition - localPoint;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rt.parent as RectTransform, eventData.position,
                    eventData.pressEventCamera, out var localPoint))
            {
                rt.localPosition = localPoint + dragOffset;
            }
        }
    }
}
