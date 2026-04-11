using System;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace IGTAPMod
{
    /// <summary>
    /// Extracts visual styles (sprites, color blocks) from the game's own UI at runtime.
    /// This lets mod UIs look identical to the game's settings panel.
    /// Falls back to UIStyle flat colors if extraction hasn't completed yet.
    /// </summary>
    public class GameUITheme : MonoBehaviour
    {
        public static GameUITheme Instance { get; private set; }

        // Extracted sprites
        public Sprite ButtonSprite;
        public Sprite ToggleBgSprite;
        public Sprite ToggleCheckSprite;
        public Sprite SliderBgSprite;
        public Sprite SliderFillSprite;
        public Sprite SliderHandleSprite;
        public Sprite PanelSprite;
        public Sprite InputFieldSprite;
        public Sprite DropdownSprite;

        // Extracted color blocks
        public ColorBlock ButtonColors;
        public ColorBlock ToggleColors;
        public ColorBlock SliderColors;
        public ColorBlock DropdownColors;

        // Extracted panel color
        public Color PanelColor = UIStyle.PanelBackground;

        // Settings panel sizing
        public Vector2 SettingsPanelSizeDelta;
        // Game canvas scaler settings (so we can match the coordinate space)
        public Vector2 GameCanvasReferenceResolution;
        public float GameCanvasMatchWidthOrHeight;
        public bool HasSettingsPanelSize;

        // Hidden template clones of game UI components
        private GameObject _toggleTemplate;
        private GameObject _sliderTemplate;
        private GameObject _buttonTemplate;
        private GameObject _dropdownTemplate;
        public bool HasTemplates { get; private set; }

        public bool IsReady { get; private set; }

        // Reflection accessors for SettingsScript private fields
        private static readonly FieldInfo F_vsyncToggle =
            AccessTools.Field(typeof(SettingsScript), "VsyncMode");
        private static readonly FieldInfo F_effectsSlider =
            AccessTools.Field(typeof(SettingsScript), "effectsSlider");
        private static readonly FieldInfo F_fullscreenDropdown =
            AccessTools.Field(typeof(SettingsScript), "fullscreenDropdown");

        private void Awake()
        {
            Instance = this;
            // Set reasonable default color blocks
            ButtonColors = new ColorBlock
            {
                normalColor = UIStyle.ButtonNormal,
                highlightedColor = UIStyle.ButtonHighlight,
                pressedColor = UIStyle.ButtonPressed,
                selectedColor = UIStyle.ButtonHighlight,
                disabledColor = UIStyle.ButtonDisabled,
                colorMultiplier = 1f,
                fadeDuration = 0.1f,
            };
            ToggleColors = ButtonColors;
            SliderColors = ButtonColors;
            DropdownColors = ButtonColors;
        }

        private void Update()
        {
            if (!IsReady) TryExtract();
        }

        private void TryExtract()
        {
            var settings = Resources.FindObjectsOfTypeAll<SettingsScript>();
            if (settings.Length == 0) return;

            var script = settings[0];
            bool gotAnything = false;

            // Extract Toggle style from VsyncMode
            var toggle = F_vsyncToggle?.GetValue(script) as Toggle;
            if (toggle != null)
            {
                ToggleColors = toggle.colors;
                Plugin.Log.LogInfo($"  Game toggle: normal={toggle.colors.normalColor} highlight={toggle.colors.highlightedColor} pressed={toggle.colors.pressedColor}");

                var bgImage = toggle.targetGraphic as Image;
                if (bgImage != null && bgImage.sprite != null)
                    ToggleBgSprite = bgImage.sprite;

                if (toggle.graphic is Image checkImage && checkImage.sprite != null)
                    ToggleCheckSprite = checkImage.sprite;

                gotAnything = true;
            }

            // Extract Slider style from effectsSlider
            var slider = F_effectsSlider?.GetValue(script) as Slider;
            if (slider != null)
            {
                SliderColors = slider.colors;

                if (slider.fillRect != null)
                {
                    var fillImg = slider.fillRect.GetComponent<Image>();
                    if (fillImg != null && fillImg.sprite != null)
                        SliderFillSprite = fillImg.sprite;
                }
                if (slider.handleRect != null)
                {
                    var handleImg = slider.handleRect.GetComponent<Image>();
                    if (handleImg != null && handleImg.sprite != null)
                        SliderHandleSprite = handleImg.sprite;
                }

                // Background is usually a sibling/parent
                var bgTransform = slider.transform.Find("Background");
                if (bgTransform != null)
                {
                    var bgImg = bgTransform.GetComponent<Image>();
                    if (bgImg != null && bgImg.sprite != null)
                        SliderBgSprite = bgImg.sprite;
                }

                gotAnything = true;
            }

            // Extract Dropdown style
            var dropdown = F_fullscreenDropdown?.GetValue(script) as TMPro.TMP_Dropdown;
            if (dropdown != null)
            {
                var dropImg = dropdown.GetComponent<Image>();
                if (dropImg != null)
                {
                    if (dropImg.sprite != null)
                        DropdownSprite = dropImg.sprite;
                    DropdownColors = dropdown.colors;
                }
                Plugin.Log.LogInfo($"  Game dropdown: imgColor={dropImg?.color} normal={dropdown.colors.normalColor} highlight={dropdown.colors.highlightedColor} pressed={dropdown.colors.pressedColor}");
                gotAnything = true;
            }

            // Extract Button style from the settings panel's parent (pause menu buttons)
            var buttons = script.GetComponentsInChildren<Button>(true);
            if (buttons.Length == 0)
            {
                // Try the parent pause menu
                var pauseMenu = Resources.FindObjectsOfTypeAll<pauseMenuScript>();
                if (pauseMenu.Length > 0)
                    buttons = pauseMenu[0].GetComponentsInChildren<Button>(true);
            }
            if (buttons.Length > 0)
            {
                ButtonColors = buttons[0].colors;
                var btnImg = buttons[0].GetComponent<Image>();
                if (btnImg != null && btnImg.sprite != null)
                    ButtonSprite = btnImg.sprite;
                Plugin.Log.LogInfo($"  Game MAIN MENU button: imgColor={btnImg?.color} normal={ButtonColors.normalColor} highlight={ButtonColors.highlightedColor} pressed={ButtonColors.pressedColor}");
                gotAnything = true;
            }

            // Extract panel background + sizing from settingsBit
            var settingsBitField = AccessTools.Field(typeof(pauseMenuScript), "settingsBit");
            var pauseMenus = Resources.FindObjectsOfTypeAll<pauseMenuScript>();
            if (pauseMenus.Length > 0 && settingsBitField != null)
            {
                var settingsBit = settingsBitField.GetValue(pauseMenus[0]) as GameObject;
                if (settingsBit != null)
                {
                    var panelImg = settingsBit.GetComponent<Image>();
                    if (panelImg != null)
                    {
                        if (panelImg.sprite != null)
                            PanelSprite = panelImg.sprite;
                        PanelColor = panelImg.color;
                    }

                    var rt = settingsBit.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        SettingsPanelSizeDelta = rt.sizeDelta;

                        var gameCanvas = settingsBit.GetComponentInParent<Canvas>();
                        if (gameCanvas != null)
                        {
                            var scaler = gameCanvas.GetComponent<CanvasScaler>();
                            if (scaler != null)
                            {
                                GameCanvasReferenceResolution = scaler.referenceResolution;
                                GameCanvasMatchWidthOrHeight = scaler.matchWidthOrHeight;
                            }
                        }

                        HasSettingsPanelSize = true;
                        Plugin.Log.LogInfo($"  SettingsPanel: size={rt.sizeDelta} canvasRef={GameCanvasReferenceResolution} match={GameCanvasMatchWidthOrHeight}");
                        Plugin.Log.LogInfo($"  PanelColor={PanelColor} PanelSprite={PanelSprite?.name}");

                        // Log the Close button inside settings (this is a settings-style button, not main menu)
                        var closeTransform = settingsBit.transform.Find("Settings")?.Find("Close");
                        if (closeTransform != null)
                        {
                            var closeBtnComp = closeTransform.GetComponent<Button>();
                            var closeBtnImg = closeTransform.GetComponent<Image>();
                            if (closeBtnComp != null)
                                Plugin.Log.LogInfo($"  SETTINGS Close btn: imgColor={closeBtnImg?.color} normal={closeBtnComp.colors.normalColor} highlight={closeBtnComp.colors.highlightedColor} pressed={closeBtnComp.colors.pressedColor} colorMult={closeBtnComp.colors.colorMultiplier}");
                        }

                        // Log ALL buttons found inside settingsBit
                        var settingsButtons = settingsBit.GetComponentsInChildren<Button>(true);
                        for (int bi = 0; bi < settingsButtons.Length; bi++)
                        {
                            var sb = settingsButtons[bi];
                            var sbImg = sb.GetComponent<Image>();
                            Plugin.Log.LogInfo($"  SettingsBtn[{bi}] '{sb.name}': imgColor={sbImg?.color} sprite={sbImg?.sprite?.name} normal={sb.colors.normalColor} highlight={sb.colors.highlightedColor}");
                        }
                    }
                }
            }

            if (gotAnything)
            {
                IsReady = true;

                // Clone game components as hidden templates
                CloneTemplates(toggle, slider, dropdown, script);

                Plugin.Log.LogInfo("[GameUITheme] Extracted game UI theme successfully");
                Plugin.Log.LogInfo($"  Templates: toggle={_toggleTemplate != null} slider={_sliderTemplate != null} button={_buttonTemplate != null} dropdown={_dropdownTemplate != null}");
            }
        }

        private void CloneTemplates(Toggle toggle, Slider slider, TMP_Dropdown dropdown, SettingsScript script)
        {
            // Clone toggle
            if (toggle != null)
            {
                _toggleTemplate = Instantiate(toggle.gameObject, transform);
                _toggleTemplate.name = "ToggleTemplate";
                _toggleTemplate.SetActive(false);
                // Remove any localization event that might overwrite our text
                foreach (var comp in _toggleTemplate.GetComponentsInChildren<MonoBehaviour>(true))
                    if (comp.GetType().Name == "LocalizeStringEvent")
                        DestroyImmediate(comp);
                Plugin.Log.LogInfo($"  Cloned toggle template from '{toggle.name}'");
            }

            // Clone slider
            if (slider != null)
            {
                _sliderTemplate = Instantiate(slider.gameObject, transform);
                _sliderTemplate.name = "SliderTemplate";
                _sliderTemplate.SetActive(false);
                Plugin.Log.LogInfo($"  Cloned slider template from '{slider.name}'");
            }

            // Clone dropdown
            if (dropdown != null)
            {
                _dropdownTemplate = Instantiate(dropdown.gameObject, transform);
                _dropdownTemplate.name = "DropdownTemplate";
                _dropdownTemplate.SetActive(false);
                foreach (var comp in _dropdownTemplate.GetComponentsInChildren<MonoBehaviour>(true))
                    if (comp.GetType().Name == "LocalizeStringEvent")
                        DestroyImmediate(comp);
                Plugin.Log.LogInfo($"  Cloned dropdown template from '{dropdown.name}'");
            }

            // Clone a button from the settings panel (Close button)
            var settingsBitField2 = AccessTools.Field(typeof(pauseMenuScript), "settingsBit");
            var pm = Resources.FindObjectsOfTypeAll<pauseMenuScript>();
            if (pm.Length > 0)
            {
                var sb = settingsBitField2.GetValue(pm[0]) as GameObject;
                if (sb != null)
                {
                    var closeTransform = sb.transform.Find("Settings")?.Find("Close");
                    if (closeTransform != null)
                    {
                        _buttonTemplate = Instantiate(closeTransform.gameObject, transform);
                        _buttonTemplate.name = "ButtonTemplate";
                        _buttonTemplate.SetActive(false);
                        foreach (var comp in _buttonTemplate.GetComponentsInChildren<MonoBehaviour>(true))
                            if (comp.GetType().Name == "LocalizeStringEvent")
                                DestroyImmediate(comp);
                        Plugin.Log.LogInfo($"  Cloned button template from 'Close'");
                    }
                }
            }

            HasTemplates = _toggleTemplate != null || _sliderTemplate != null ||
                           _buttonTemplate != null || _dropdownTemplate != null;
        }

        // =================================================================
        //  Public clone methods - creates game-native UI components
        // =================================================================

        /// <summary>
        /// Clone the game's toggle. Returns null if template not available.
        /// Caller must set parent, label text, isOn, and onValueChanged.
        /// </summary>
        public Toggle CloneToggle(Transform parent, string label, bool value, Action<bool> onChanged = null)
        {
            if (_toggleTemplate == null) return null;

            var go = Instantiate(_toggleTemplate, parent);
            go.name = "Toggle_" + label;
            go.SetActive(true);
            NormalizeForLayout(go);

            var toggle = go.GetComponent<Toggle>();
            if (toggle != null)
            {
                toggle.isOn = value;
                toggle.onValueChanged.RemoveAllListeners();
                if (onChanged != null)
                    toggle.onValueChanged.AddListener(v => onChanged(v));

                Plugin.Log.LogInfo($"[CloneToggle] '{label}' colors: normal={toggle.colors.normalColor} highlight={toggle.colors.highlightedColor} pressed={toggle.colors.pressedColor} disabled={toggle.colors.disabledColor} colorMult={toggle.colors.colorMultiplier} interactable={toggle.interactable}");
                Plugin.Log.LogInfo($"[CloneToggle] '{label}' targetGraphic={(toggle.targetGraphic != null ? toggle.targetGraphic.name : "null")} graphic={(toggle.graphic != null ? toggle.graphic.name : "null")}");
            }

            // Set label text
            var labelText = go.GetComponentInChildren<TMP_Text>();
            if (labelText != null)
                labelText.text = label;

            return toggle;
        }

        /// <summary>
        /// Clone the game's slider. Returns null if template not available.
        /// </summary>
        public Slider CloneSlider(Transform parent, string label, float min, float max,
            float value, Action<float> onChanged = null)
        {
            if (_sliderTemplate == null) return null;

            var go = Instantiate(_sliderTemplate, parent);
            go.name = "Slider_" + label;
            go.SetActive(true);
            NormalizeForLayout(go);

            var slider = go.GetComponent<Slider>();
            if (slider != null)
            {
                slider.minValue = min;
                slider.maxValue = max;
                slider.value = value;
                slider.onValueChanged.RemoveAllListeners();
                if (onChanged != null)
                    slider.onValueChanged.AddListener(v => onChanged(v));
            }

            return slider;
        }

        /// <summary>
        /// Clone the game's button. Returns null if template not available.
        /// </summary>
        /// <summary>
        /// Clone the game's TMP dropdown. Returns null if the template wasn't extracted.
        /// Caller must populate options and set the value.
        /// </summary>
        public TMP_Dropdown CloneDropdown(Transform parent, string name)
        {
            if (_dropdownTemplate == null) return null;

            var go = Instantiate(_dropdownTemplate, parent);
            go.name = "Dropdown_" + name;
            go.SetActive(true);
            NormalizeForLayout(go);

            var dd = go.GetComponent<TMP_Dropdown>();
            if (dd != null)
            {
                dd.onValueChanged.RemoveAllListeners();
                dd.ClearOptions();

                // The cloned game dropdown carries pre-existing static Item children inside
                // Template/Viewport/Content from the original FullscreenMode setup. ClearOptions
                // only clears dd.options at runtime — it doesn't remove these static prototype
                // duplicates. When the popup opens, those leftover Items are visible alongside
                // any items we add. Wipe them all here so only the runtime-built items appear.
                if (dd.template != null)
                {
                    var viewport = dd.template.Find("Viewport");
                    var content = viewport != null ? viewport.Find("Content") : null;
                    if (content != null)
                    {
                        // Keep only the FIRST Item child as the prototype (TMP_Dropdown clones
                        // it for each option at popup time). Destroy any extras.
                        int kept = 0;
                        for (int i = content.childCount - 1; i >= 0; i--)
                        {
                            var c = content.GetChild(i);
                            if (c.name == "Item" && kept == 0)
                            {
                                kept++;
                                continue;
                            }
                            DestroyImmediate(c.gameObject);
                            Plugin.Log.LogInfo($"[CloneDropdown] '{name}' stripped popup Content child: {c.name}");
                        }
                    }
                }

                Plugin.Log.LogInfo($"[CloneDropdown] '{name}' after ClearOptions: dd.options.Count={dd.options.Count} captionText='{dd.captionText?.text}'");
            }

            // Strip stray children. The cloned game dropdown carries an extra "Text (TMP) (1)"
            // child that displayed "Window size" as a header label in the original settings panel.
            // The standard TMP_Dropdown children are Label (captionText), Arrow, and Template.
            // We identify the legitimate ones by checking against the dropdown's known references
            // and destroy anything else (immediate children only).
            if (dd != null)
            {
                var legitChildren = new System.Collections.Generic.HashSet<Transform>();
                if (dd.captionText != null) legitChildren.Add(dd.captionText.transform);
                if (dd.captionImage != null) legitChildren.Add(dd.captionImage.transform);
                if (dd.template != null) legitChildren.Add(dd.template);

                // Walk up to immediate children of the dropdown root
                var toDestroy = new System.Collections.Generic.List<GameObject>();
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    var child = go.transform.GetChild(i);
                    bool isLegit = false;
                    foreach (var legit in legitChildren)
                    {
                        if (legit == child || legit.IsChildOf(child))
                        {
                            isLegit = true;
                            break;
                        }
                    }
                    // The Arrow child is the dropdown's down-arrow image — keep anything literally
                    // named "Arrow" too, since dd's references don't include it directly.
                    if (!isLegit && child.name != "Arrow")
                    {
                        toDestroy.Add(child.gameObject);
                        Plugin.Log.LogInfo($"[CloneDropdown] '{name}' stripping stray child: {child.name}");
                    }
                }
                foreach (var d in toDestroy)
                    DestroyImmediate(d);
            }

            Plugin.Log.LogInfo($"[CloneDropdown] '{name}' templateValid={dd != null}");
            return dd;
        }

        public Button CloneButton(Transform parent, string label, Action onClick = null, bool textOnly = false, float fontSize = 0f)
        {
            if (_buttonTemplate == null) return null;

            var go = Instantiate(_buttonTemplate, parent);
            go.name = "Btn_" + label;
            go.SetActive(true);
            NormalizeForLayout(go);

            var button = go.GetComponent<Button>();
            var rootImg = go.GetComponent<Image>();

            if (textOnly)
            {
                // Subtle text-only style: hide the background, use text tinting for hover feedback.
                // The cloned game Close button already has Image disabled and targetGraphic=Text so
                // the existing colors block tints the text directly. Override the normal color to
                // white so the text reads white in idle, then highlights in the game's accent on hover.
                if (rootImg != null) rootImg.enabled = false;
                if (button != null)
                {
                    var c = button.colors;
                    c.normalColor = Color.white;
                    button.colors = c;
                }
            }
            else
            {
                // Filled-button style: enable the background Image, point targetGraphic at it so the
                // orange tint becomes the button background. The cloned Close button has Image disabled
                // and targetGraphic=Text so the orange tint went to the text — we need to flip both.
                if (rootImg != null)
                {
                    rootImg.enabled = true;
                    rootImg.color = Color.white;
                }
                if (button != null && rootImg != null)
                    button.targetGraphic = rootImg;
            }

            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                if (onClick != null)
                    button.onClick.AddListener(() => onClick());

                Plugin.Log.LogInfo($"[CloneButton] '{label}' textOnly={textOnly} colors: normal={button.colors.normalColor} highlighted={button.colors.highlightedColor} colorMult={button.colors.colorMultiplier} interactable={button.interactable} targetGraphic={(button.targetGraphic != null ? button.targetGraphic.name : "null")}");
            }

            // Reset the text RectTransform to actually fill the button — the cloned Close button's
            // text has sizeDelta=(-70, 0) meaning "70px narrower than parent" which renders fine for the
            // original ~300px-wide button but breaks when the button is shrunk (e.g. 32px arrow buttons).
            // For filled buttons we set white text so it reads on the orange background; for textOnly
            // buttons we leave text.color alone since the Button component tints it via state colors.
            var texts = go.GetComponentsInChildren<TMP_Text>(true);
            foreach (var t in texts)
            {
                t.text = label;
                if (!textOnly) t.color = Color.white;
                t.alignment = TMPro.TextAlignmentOptions.Center;
                if (fontSize > 0f)
                {
                    t.enableAutoSizing = false;
                    t.fontSize = fontSize;
                }
                var trt = t.GetComponent<RectTransform>();
                if (trt != null)
                {
                    trt.anchorMin = Vector2.zero;
                    trt.anchorMax = Vector2.one;
                    trt.offsetMin = new Vector2(4f, 2f);
                    trt.offsetMax = new Vector2(-4f, -2f);
                    trt.pivot = new Vector2(0.5f, 0.5f);
                    var lp = trt.localPosition;
                    trt.localPosition = new Vector3(lp.x, lp.y, 0f);
                }
            }

            return button;
        }

        /// <summary>
        /// Normalize a freshly cloned game UI element so it works inside a VerticalLayoutGroup
        /// or other generic layout container. The game's original components have anchors,
        /// pivots, scales, and LayoutElements designed for the specific settings panel layout
        /// — when stuck into an arbitrary layout group they end up zero-sized, off-screen,
        /// or marked ignoreLayout=true, which is why they appear invisible.
        /// </summary>
        private static void NormalizeForLayout(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                // Top-stretched: VerticalLayoutGroup expects this so it can size width via the parent
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(0f, rt.sizeDelta.y > 0f ? rt.sizeDelta.y : 32f);
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;
                // CRITICAL: reset local Z position. The cloned game widgets carry over a non-zero Z
                // from their original position in the game's settings panel hierarchy. With the canvas
                // scale of 1.85, this becomes world Z = 1.85, which can cause depth-related rendering
                // issues. Set to 0 explicitly.
                var localPos = rt.localPosition;
                rt.localPosition = new Vector3(localPos.x, localPos.y, 0f);
            }

            // Reset ALL LayoutElement components on the root (there can be multiple).
            var allLes = go.GetComponents<LayoutElement>();
            foreach (var le in allLes)
            {
                le.ignoreLayout = false;
                le.minWidth = -1f;
                le.minHeight = -1f;
                le.preferredWidth = -1f;
                le.preferredHeight = -1f;
                le.flexibleWidth = -1f;
                le.flexibleHeight = -1f;
            }
        }

        /// <summary>
        /// Dump a cloned widget's hierarchy and visual state to the log for debugging
        /// invisible widget issues. Call right after Instantiate + NormalizeForLayout.
        /// </summary>
        public static void DumpWidgetTree(GameObject go, string label)
        {
            Plugin.Log.LogInfo($"[GameUITheme] === Dump '{label}' ===");
            DumpRecursive(go.transform, 0);
        }

        /// <summary>
        /// Deep dump that also walks the parent chain (showing alpha/scale/canvas inheritance)
        /// and reports world rect, material, and any nested Canvas components — basically
        /// everything needed to figure out why a widget isn't rendering visibly.
        /// </summary>
        public static void DumpWidgetTreeDeep(GameObject go, string label)
        {
            Plugin.Log.LogInfo($"[GameUITheme] ====== DEEP DUMP '{label}' ======");

            // Walk up the parent chain so we can see CanvasGroup alpha, scale, and Canvas inheritance
            Plugin.Log.LogInfo("  -- parent chain (root -> leaf) --");
            var chain = new System.Collections.Generic.List<Transform>();
            for (var p = go.transform; p != null; p = p.parent)
                chain.Add(p);
            chain.Reverse();
            foreach (var p in chain)
            {
                var prt = p as RectTransform;
                var pcg = p.GetComponent<CanvasGroup>();
                var pc = p.GetComponent<Canvas>();
                var pmask = p.GetComponent<UnityEngine.UI.Mask>();
                var prectMask = p.GetComponent<UnityEngine.UI.RectMask2D>();
                string parts = $"[{p.name}] active={p.gameObject.activeSelf} scale={p.localScale}";
                if (prt != null)
                    parts += $" worldPos={prt.position} sizeDelta={prt.sizeDelta} rect={prt.rect}";
                if (pcg != null) parts += $" CG(alpha={pcg.alpha},blocksRaycasts={pcg.blocksRaycasts},interactable={pcg.interactable})";
                if (pc != null) parts += $" Canvas(sortOrder={pc.sortingOrder},override={pc.overrideSorting},renderMode={pc.renderMode})";
                if (pmask != null) parts += $" Mask(enabled={pmask.enabled})";
                if (prectMask != null) parts += $" RectMask2D(enabled={prectMask.enabled})";
                Plugin.Log.LogInfo($"    {parts}");
            }

            Plugin.Log.LogInfo("  -- subtree --");
            DumpRecursiveDeep(go.transform, 0);
        }

        private static void DumpRecursive(Transform t, int depth)
        {
            string indent = new string(' ', depth * 2);
            var rt = t.GetComponent<RectTransform>();
            var img = t.GetComponent<Image>();
            var tmp = t.GetComponent<TMP_Text>();
            var le = t.GetComponent<LayoutElement>();
            var canvasGroup = t.GetComponent<CanvasGroup>();
            var canvasRenderer = t.GetComponent<CanvasRenderer>();

            string info = $"[{t.name}] active={t.gameObject.activeSelf}";
            if (rt != null)
                info += $" anchors=({rt.anchorMin.x:F2},{rt.anchorMin.y:F2})-({rt.anchorMax.x:F2},{rt.anchorMax.y:F2}) pivot=({rt.pivot.x:F2},{rt.pivot.y:F2}) sizeDelta={rt.sizeDelta} pos={rt.anchoredPosition} scale={rt.localScale} rect={rt.rect}";
            if (img != null)
                info += $" Image(color={img.color}, sprite={img.sprite?.name ?? "null"}, enabled={img.enabled}, raycast={img.raycastTarget})";
            if (tmp != null)
                info += $" TMP(text='{tmp.text}', color={tmp.color}, enabled={tmp.enabled}, fontSize={tmp.fontSize})";
            if (le != null)
                info += $" LE(ignore={le.ignoreLayout}, minH={le.minHeight}, prefH={le.preferredHeight}, flexH={le.flexibleHeight})";
            if (canvasGroup != null)
                info += $" CG(alpha={canvasGroup.alpha}, blocksRaycasts={canvasGroup.blocksRaycasts})";
            if (canvasRenderer != null)
                info += $" CR(cull={canvasRenderer.cull})";

            Plugin.Log.LogInfo($"  {indent}{info}");

            for (int i = 0; i < t.childCount && depth < 4; i++)
                DumpRecursive(t.GetChild(i), depth + 1);
        }

        private static void DumpRecursiveDeep(Transform t, int depth)
        {
            string indent = new string(' ', depth * 2);
            var rt = t.GetComponent<RectTransform>();
            var img = t.GetComponent<Image>();
            var rawImg = t.GetComponent<RawImage>();
            var tmp = t.GetComponent<TMP_Text>();
            var le = t.GetComponent<LayoutElement>();
            var canvasGroup = t.GetComponent<CanvasGroup>();
            var canvasRenderer = t.GetComponent<CanvasRenderer>();
            var canvas = t.GetComponent<Canvas>();
            var mask = t.GetComponent<UnityEngine.UI.Mask>();
            var rectMask = t.GetComponent<UnityEngine.UI.RectMask2D>();

            string info = $"[{t.name}] active={t.gameObject.activeSelf}";
            if (rt != null)
            {
                info += $" anchors=({rt.anchorMin.x:F2},{rt.anchorMin.y:F2})-({rt.anchorMax.x:F2},{rt.anchorMax.y:F2})";
                info += $" pivot=({rt.pivot.x:F2},{rt.pivot.y:F2}) sizeDelta={rt.sizeDelta} pos={rt.anchoredPosition} scale={rt.localScale} rect={rt.rect}";
                info += $" worldPos={rt.position}";
                // Compute world rect corners
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                info += $" worldCorners=[BL{corners[0]} TR{corners[2]}]";
            }
            if (img != null)
            {
                info += $" Image(color={img.color}, sprite={img.sprite?.name ?? "null"}, enabled={img.enabled}, raycast={img.raycastTarget}";
                info += $", type={img.type}, fillCenter={img.fillCenter}";
                if (img.material != null)
                    info += $", mat={img.material.name}/{img.material.shader?.name}";
                info += ")";
            }
            if (rawImg != null)
                info += $" RawImage(color={rawImg.color}, tex={rawImg.texture?.name ?? "null"}, enabled={rawImg.enabled})";
            if (tmp != null)
                info += $" TMP(text='{tmp.text}', color={tmp.color}, enabled={tmp.enabled}, fontSize={tmp.fontSize}, font={tmp.font?.name ?? "null"})";
            if (le != null)
                info += $" LE(ignore={le.ignoreLayout}, minH={le.minHeight}, prefH={le.preferredHeight}, flexH={le.flexibleHeight})";
            if (canvasGroup != null)
                info += $" CG(alpha={canvasGroup.alpha}, blocksRaycasts={canvasGroup.blocksRaycasts})";
            if (canvasRenderer != null)
                info += $" CR(cull={canvasRenderer.cull}, alpha={canvasRenderer.GetAlpha()}, hasPopInstr={canvasRenderer.hasPopInstruction}, materialCount={canvasRenderer.materialCount})";
            if (canvas != null)
                info += $" SubCanvas(sortOrder={canvas.sortingOrder}, override={canvas.overrideSorting}, mode={canvas.renderMode}, enabled={canvas.enabled})";
            if (mask != null)
                info += $" Mask(enabled={mask.enabled}, showGraphic={mask.showMaskGraphic})";
            if (rectMask != null)
                info += $" RectMask2D(enabled={rectMask.enabled})";

            Plugin.Log.LogInfo($"    {indent}{info}");

            for (int i = 0; i < t.childCount; i++)
                DumpRecursiveDeep(t.GetChild(i), depth + 1);
        }

        private void DumpChildren(Transform t, int depth)
        {
            string indent = new string(' ', depth * 2);
            var crt = t.GetComponent<RectTransform>();
            var img = t.GetComponent<Image>();
            string imgInfo = img != null ? $" img=({img.color}, sprite={img.sprite?.name ?? "null"})" : "";
            string rtInfo = crt != null ? $" size={crt.sizeDelta} anchors={crt.anchorMin}->{crt.anchorMax}" : "";
            Plugin.Log.LogInfo($"  {indent}[{t.name}]{rtInfo}{imgInfo}");
            for (int i = 0; i < t.childCount && depth < 2; i++)
                DumpChildren(t.GetChild(i), depth + 1);
        }

        /// <summary>
        /// Apply game button styling to an Image + Button pair.
        /// Uses extracted sprites and color blocks when available.
        /// </summary>
        public void ApplyButtonStyle(Button button, Image image)
        {
            if (ButtonSprite != null)
            {
                image.sprite = ButtonSprite;
                image.type = Image.Type.Sliced;
            }
            button.colors = ButtonColors;
        }

        /// <summary>
        /// Apply game toggle styling to a Toggle + its background Image.
        /// </summary>
        public void ApplyToggleStyle(Toggle toggle, Image background, Image checkmark)
        {
            toggle.colors = ToggleColors;
            if (ToggleBgSprite != null)
            {
                background.sprite = ToggleBgSprite;
                background.type = Image.Type.Sliced;
            }
            if (ToggleCheckSprite != null)
            {
                checkmark.sprite = ToggleCheckSprite;
                checkmark.type = Image.Type.Sliced;
            }
        }

        /// <summary>
        /// Apply game slider styling.
        /// </summary>
        public void ApplySliderStyle(Slider slider, Image background, Image fill, Image handle)
        {
            slider.colors = SliderColors;
            if (SliderBgSprite != null)
            {
                background.sprite = SliderBgSprite;
                background.type = Image.Type.Sliced;
            }
            if (SliderFillSprite != null)
            {
                fill.sprite = SliderFillSprite;
                fill.type = Image.Type.Sliced;
            }
            if (SliderHandleSprite != null)
            {
                handle.sprite = SliderHandleSprite;
                handle.type = Image.Type.Sliced;
            }
        }

        /// <summary>
        /// Apply game panel styling to a background Image.
        /// </summary>
        public void ApplyPanelStyle(Image image)
        {
            if (PanelSprite != null)
            {
                image.sprite = PanelSprite;
                image.type = Image.Type.Sliced;
            }
            image.color = PanelColor;
        }
    }
}
