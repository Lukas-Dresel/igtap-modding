using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace IGTAPMod
{
    public class DebugMenuUI : MonoBehaviour
    {
        private bool _isOpen;
        private Canvas _canvas;
        private GameObject _root;
        private RectTransform _mainPanel;
        private RectTransform _tabBar;
        private RectTransform _contentContainer;

        private int _activeTabIndex = -1;
        private int _lastSectionCount;
        private WidgetPanel _activePanel;
        private readonly List<Button> _tabButtons = new List<Button>();

        private CanvasGroup _canvasGroup;
        private float _targetAlpha;
        private bool _themeSizeApplied;
        private bool _sizeNeedsCorrection;
        private ScrollRect _tabScroll;
        private GameObject _fadeLeft;
        private GameObject _fadeRight;

        private void Update()
        {
            if (Plugin.UIToggleKey.Value.IsDown())
            {
                _isOpen = !_isOpen;
                if (_isOpen) Show();
                else Hide();
            }

            if (_canvasGroup != null)
            {
                float prevAlpha = _canvasGroup.alpha;
                _canvasGroup.alpha = Mathf.MoveTowards(_canvasGroup.alpha, _targetAlpha,
                    Time.unscaledDeltaTime * 8f);
                if (_isOpen && Mathf.Abs(prevAlpha - _canvasGroup.alpha) > 0.01f)
                    Plugin.Log.LogInfo($"[DebugMenuUI] alpha {prevAlpha:F2} -> {_canvasGroup.alpha:F2} (target {_targetAlpha:F2})");
                if (_canvasGroup.alpha <= 0f && !_isOpen && _root != null)
                    _root.SetActive(false);
            }

            if (_isOpen && DebugMenuAPI.Sections.Count != _lastSectionCount)
            {
                RebuildTabs();
                _lastSectionCount = DebugMenuAPI.Sections.Count;
            }

            // Apply theme size once ready
            if (!_themeSizeApplied && _mainPanel != null)
                ApplyThemeSize();

            // Wait for CanvasScaler to actually update (lossyScale != 1.0),
            // then correct sizeDelta based on actual lossyScale comparison
            if (_sizeNeedsCorrection && _mainPanel != null && _mainPanel.lossyScale.x > 1.01f)
            {
                _sizeNeedsCorrection = false;
                CorrectSizeFromScale();
            }

            // Update tab fade indicators
            if (_isOpen && _tabScroll != null && _fadeLeft != null && _fadeRight != null)
            {
                float contentW = _tabScroll.content != null ? _tabScroll.content.rect.width : 0;
                float viewportW = _tabScroll.viewport != null ? _tabScroll.viewport.rect.width : 0;
                bool canScroll = contentW > viewportW + 1f;
                float pos = _tabScroll.horizontalNormalizedPosition;
                _fadeLeft.SetActive(canScroll && pos > 0.01f);
                _fadeRight.SetActive(canScroll && pos < 0.99f);
            }

            if (_isOpen && _activePanel != null)
                _activePanel.UpdateAll();
        }

        private void Show()
        {
            if (_canvas == null)
                BuildUI();

            _root.SetActive(true);
            _targetAlpha = 1f;
            // Force alpha to 1 immediately so widgets are visible during construction.
            // The animation was causing widgets created during Show() to be dumped at
            // alpha=0, and there was a strong suspicion the animation never reached 1.
            if (_canvasGroup != null) _canvasGroup.alpha = 1f;
            _lastSectionCount = DebugMenuAPI.Sections.Count;
            RebuildTabs();
            if (_activeTabIndex < 0 && DebugMenuAPI.Sections.Count > 0)
                SwitchTab(0);
        }

        private void Hide()
        {
            _targetAlpha = 0f;
        }

        private void BuildUI()
        {
            _canvas = GameUI.CreateScreenCanvas("DebugMenuCanvas", 150);
            _root = _canvas.gameObject;
            _canvasGroup = _root.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;

            // Main panel -- centered
            _mainPanel = GameUI.CreatePanel(_canvas.transform, "MainPanel", applyTheme: true);
            // Temporary fallback size - will be overwritten by ApplyThemeSize()
            _mainPanel.anchorMin = new Vector2(0.5f, 0.5f);
            _mainPanel.anchorMax = new Vector2(0.5f, 0.5f);
            _mainPanel.pivot = new Vector2(0.5f, 0.5f);
            _mainPanel.offsetMin = new Vector2(-350, -250);
            _mainPanel.offsetMax = new Vector2(350, 250);
            ApplyThemeSize();
            var vlg = GameUI.AddVerticalLayout(_mainPanel, spacing: 4, padding: 0);
            vlg.padding = new RectOffset(16, 16, 16, 10);
            vlg.childForceExpandHeight = false;
            GameUI.MakeDraggable(_mainPanel);

            // ---- Title row: just text + close button, no separate background ----
            var titleRow = new GameObject("TitleRow", typeof(RectTransform));
            titleRow.transform.SetParent(_mainPanel.transform, false);
            var titleHlg = titleRow.AddComponent<HorizontalLayoutGroup>();
            titleHlg.spacing = 0;
            titleHlg.padding = new RectOffset(4, 4, 0, 0);
            titleHlg.childAlignment = TextAnchor.MiddleLeft;
            titleHlg.childForceExpandWidth = false;
            titleHlg.childForceExpandHeight = false;
            titleHlg.childControlWidth = true;
            titleHlg.childControlHeight = true;
            var titleLe = titleRow.AddComponent<LayoutElement>();
            titleLe.preferredHeight = 28;

            var title = GameUI.CreateText(titleRow.transform, "Title", "IGTAP Mod",
                UIStyle.FontSizeBody, UIStyle.TextSecondary,
                TextAlignmentOptions.MidlineLeft);
            title.fontStyle = FontStyles.Bold;
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            var closeBtn = GameUI.CreateButton(titleRow.transform, "Close", "X",
                () => { _isOpen = false; Hide(); }, fontSize: 14f);
            GameUI.SetSize(closeBtn.GetComponent<RectTransform>(), width: 28, height: 28);
            // Center the X text properly
            var closeLabel = closeBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (closeLabel != null)
            {
                var clRt = closeLabel.GetComponent<RectTransform>();
                clRt.offsetMin = Vector2.zero;
                clRt.offsetMax = Vector2.zero;
            }

            // ---- Tab bar ----
            var tabBarBg = new GameObject("TabBarBg", typeof(RectTransform));
            tabBarBg.transform.SetParent(_mainPanel.transform, false);
            tabBarBg.AddComponent<Image>().color = new Color(0.04f, 0.04f, 0.06f, 1f);
            var tabBarLe = tabBarBg.AddComponent<LayoutElement>();
            tabBarLe.preferredHeight = 32;

            _tabScroll = tabBarBg.AddComponent<ScrollRect>();
            var tabScroll = _tabScroll;
            tabScroll.horizontal = true;
            tabScroll.vertical = false;
            tabScroll.movementType = ScrollRect.MovementType.Clamped;
            tabScroll.scrollSensitivity = 20f;

            var tabViewport = new GameObject("TabViewport", typeof(RectTransform));
            tabViewport.transform.SetParent(tabBarBg.transform, false);
            var tvpRt = tabViewport.GetComponent<RectTransform>();
            tvpRt.anchorMin = Vector2.zero;
            tvpRt.anchorMax = Vector2.one;
            tvpRt.offsetMin = new Vector2(16, 0);
            tvpRt.offsetMax = new Vector2(-16, 0);
            tabViewport.AddComponent<Image>().color = Color.clear;
            tabViewport.AddComponent<RectMask2D>();

            _tabBar = new GameObject("TabContent", typeof(RectTransform)).GetComponent<RectTransform>();
            _tabBar.SetParent(tabViewport.transform, false);
            _tabBar.anchorMin = new Vector2(0, 0);
            _tabBar.anchorMax = new Vector2(0, 1);
            _tabBar.pivot = new Vector2(0, 0.5f);

            var tabHlg = _tabBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            tabHlg.spacing = 4;
            tabHlg.padding = new RectOffset(0, 0, 2, 0);
            tabHlg.childForceExpandWidth = false;
            tabHlg.childForceExpandHeight = true;
            tabHlg.childControlWidth = true;
            tabHlg.childControlHeight = true;

            _tabBar.gameObject.AddComponent<ContentSizeFitter>().horizontalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            tabScroll.viewport = tvpRt;
            tabScroll.content = _tabBar;

            // Fade overlays on left/right edges to hint at scrolling
            var fadeTex = CreateHorizontalGradient(32, 1, UIStyle.PanelBackground, true);
            var fadeSprite = Sprite.Create(fadeTex, new Rect(0, 0, 32, 1), new Vector2(0.5f, 0.5f));

            _fadeLeft = new GameObject("FadeLeft", typeof(RectTransform));
            var fadeLeft = _fadeLeft;
            fadeLeft.transform.SetParent(tabBarBg.transform, false);
            var flRt = fadeLeft.GetComponent<RectTransform>();
            flRt.anchorMin = new Vector2(0, 0);
            flRt.anchorMax = new Vector2(0, 1);
            flRt.pivot = new Vector2(0, 0.5f);
            flRt.offsetMin = new Vector2(0, 0);
            flRt.offsetMax = new Vector2(24, 0);
            var flImg = fadeLeft.AddComponent<Image>();
            flImg.sprite = fadeSprite;
            flImg.type = Image.Type.Simple;
            flImg.raycastTarget = false;

            _fadeRight = new GameObject("FadeRight", typeof(RectTransform));
            var fadeRight = _fadeRight;
            fadeRight.transform.SetParent(tabBarBg.transform, false);
            var frRt = fadeRight.GetComponent<RectTransform>();
            frRt.anchorMin = new Vector2(1, 0);
            frRt.anchorMax = new Vector2(1, 1);
            frRt.pivot = new Vector2(1, 0.5f);
            frRt.offsetMin = new Vector2(-24, 0);
            frRt.offsetMax = new Vector2(0, 0);
            var frImg = fadeRight.AddComponent<Image>();
            frImg.sprite = fadeSprite;
            frImg.type = Image.Type.Simple;
            frImg.raycastTarget = false;
            // Flip horizontally
            frRt.localScale = new Vector3(-1, 1, 1);

            // Tab bar bottom border (active tab visually breaks through this
            // because its bg matches the content area)
            var tabBorder = new GameObject("TabBorder", typeof(RectTransform));
            tabBorder.transform.SetParent(_mainPanel.transform, false);
            tabBorder.AddComponent<LayoutElement>().preferredHeight = 1;
            tabBorder.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);

            // ---- Content scroll view ----
            var scrollGo = new GameObject("ContentScroll", typeof(RectTransform));
            scrollGo.transform.SetParent(_mainPanel.transform, false);
            scrollGo.AddComponent<LayoutElement>().flexibleHeight = 1;
            scrollGo.AddComponent<Image>().color = Color.clear;

            var scrollRect = scrollGo.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30f;

            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(scrollGo.transform, false);
            var vpRt = viewport.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(16, 0);
            vpRt.offsetMax = new Vector2(-16, 0);
            // Use RectMask2D instead of Mask: RectMask2D clips by rect bounds without needing
            // a graphic. The previous Mask + Color.clear Image setup was suspect (Unity's Mask
            // uses stencil buffer based on the mask graphic's alpha — alpha=0 may cull everything
            // inside the mask depending on Unity version).
            viewport.AddComponent<RectMask2D>();

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            _contentContainer = content.GetComponent<RectTransform>();
            _contentContainer.anchorMin = new Vector2(0, 1);
            _contentContainer.anchorMax = new Vector2(1, 1);
            _contentContainer.pivot = new Vector2(0.5f, 1);
            _contentContainer.offsetMin = Vector2.zero;
            _contentContainer.offsetMax = Vector2.zero;

            var contentVlg = content.AddComponent<VerticalLayoutGroup>();
            contentVlg.spacing = 8;
            contentVlg.padding = new RectOffset(12, 12, 10, 10);
            contentVlg.childForceExpandWidth = true;
            contentVlg.childForceExpandHeight = false;
            contentVlg.childControlWidth = true;
            contentVlg.childControlHeight = true;

            content.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = vpRt;
            scrollRect.content = _contentContainer;
        }

        private void ApplyThemeSize()
        {
            var theme = GameUITheme.Instance;
            if (theme == null || !theme.HasSettingsPanelSize) return;

            // Update our canvas scaler to match the game's
            if (theme.GameCanvasReferenceResolution != Vector2.zero)
            {
                var scaler = _canvas.GetComponent<CanvasScaler>();
                if (scaler != null)
                {
                    scaler.referenceResolution = theme.GameCanvasReferenceResolution;
                    scaler.matchWidthOrHeight = theme.GameCanvasMatchWidthOrHeight;
                }
            }

            _mainPanel.anchorMin = new Vector2(0.5f, 0.5f);
            _mainPanel.anchorMax = new Vector2(0.5f, 0.5f);
            _mainPanel.pivot = new Vector2(0.5f, 0.5f);
            _mainPanel.sizeDelta = theme.SettingsPanelSizeDelta;
            _mainPanel.anchoredPosition = Vector2.zero;


            _themeSizeApplied = true;
            _sizeNeedsCorrection = true; // correct on next frame after scaler updates

            var img = _mainPanel.GetComponent<Image>();
            if (img != null) theme.ApplyPanelStyle(img);

            Plugin.Log.LogInfo($"[DebugMenuUI] Theme applied, panel size={theme.SettingsPanelSizeDelta}");
        }

        private void CorrectSizeFromScale()
        {
            var theme = GameUITheme.Instance;
            if (theme == null || !theme.HasSettingsPanelSize) return;

            // Get the game's settingsBit lossyScale
            var settingsBitField = HarmonyLib.AccessTools.Field(typeof(pauseMenuScript), "settingsBit");
            var pauseMenus = Resources.FindObjectsOfTypeAll<pauseMenuScript>();
            if (pauseMenus.Length == 0) return;

            var sb = settingsBitField.GetValue(pauseMenus[0]) as GameObject;
            if (sb == null) return;

            float gameScale = sb.GetComponent<RectTransform>().lossyScale.x;
            float myScale = _mainPanel.lossyScale.x;

            if (myScale < 0.001f || gameScale < 0.001f) return;

            float correction = gameScale / myScale;
            _mainPanel.sizeDelta = theme.SettingsPanelSizeDelta * correction;

            Plugin.Log.LogInfo($"[DebugMenuUI] Size corrected: {_mainPanel.sizeDelta} (scale factor={correction:F3})");
        }

        private void RebuildTabs()
        {
            for (int i = _tabBar.childCount - 1; i >= 0; i--)
                DestroyImmediate(_tabBar.GetChild(i).gameObject);
            _tabButtons.Clear();

            var sections = DebugMenuAPI.Sections;
            for (int i = 0; i < sections.Count; i++)
            {
                int idx = i;
                bool active = i == _activeTabIndex;
                var btn = GameUI.CreateTabButton(_tabBar.transform, "Tab_" + sections[i].Title,
                    sections[i].Title, () => SwitchTab(idx), active);

                var label = btn.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                    label.textWrappingMode = TextWrappingModes.NoWrap;

                var le = btn.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 30;
                le.preferredWidth = Mathf.Max(70, sections[i].Title.Length * 10 + 24);
                _tabButtons.Add(btn);
            }

            if (_activeTabIndex >= sections.Count)
                _activeTabIndex = sections.Count > 0 ? 0 : -1;
        }

        private void SwitchTab(int index)
        {
            var sections = DebugMenuAPI.Sections;
            if (index < 0 || index >= sections.Count) return;
            if (index == _activeTabIndex) return;

            _activeTabIndex = index;
            RebuildTabs();

            // Clear content - DestroyImmediate to avoid layout conflicts
            _activePanel?.Clear();
            _activePanel = null;
            for (int i = _contentContainer.childCount - 1; i >= 0; i--)
                DestroyImmediate(_contentContainer.GetChild(i).gameObject);

            // Build widgets directly into content container
            Plugin.Log.LogInfo($"[DebugMenuUI] SwitchTab({index}) '{sections[index].Title}', contentContainer={_contentContainer != null}, children before={_contentContainer?.childCount}");
            _activePanel = new WidgetPanel(_contentContainer);
            try
            {
                sections[index].Build?.Invoke(_activePanel);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"[DebugMenuUI] Tab '{sections[index].Title}': {e}");
            }
            Plugin.Log.LogInfo($"[DebugMenuUI] Tab built, children after={_contentContainer?.childCount}");
        }

        private static Texture2D CreateHorizontalGradient(int w, int h, Color solid, bool solidOnLeft)
        {
            var tex = new Texture2D(w, h);
            for (int x = 0; x < w; x++)
            {
                float t = (float)x / (w - 1);
                float alpha = solidOnLeft ? (1f - t) : t;
                Color c = new Color(solid.r, solid.g, solid.b, solid.a * alpha);
                for (int y = 0; y < h; y++)
                    tex.SetPixel(x, y, c);
            }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        public void RefreshActiveTab()
        {
            if (_activeTabIndex >= 0)
            {
                int idx = _activeTabIndex;
                _activeTabIndex = -1;
                SwitchTab(idx);
            }
        }
    }
}
