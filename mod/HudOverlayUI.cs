using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace IGTAPMod
{
    /// <summary>
    /// uGUI-based HUD overlay that displays registered DebugMenuAPI.HudItems
    /// in the top-left corner with proper text styling and shadows.
    /// Replaces the old IMGUI DrawHUD() in DebugUI.
    /// </summary>
    public class HudOverlayUI : MonoBehaviour
    {
        private Canvas _canvas;
        private RectTransform _panel;
        private readonly List<TextMeshProUGUI> _textItems = new List<TextMeshProUGUI>();
        private int _lastItemCount;
        private bool _built;

        private void LateUpdate()
        {
            if (!_built) Build();
            UpdateItems();
        }

        private void Build()
        {
            _canvas = GameUI.CreateScreenCanvas("HudOverlayCanvas", 90);

            // Panel with auto-size
            _panel = GameUI.CreatePanel(_canvas.transform, "HudPanel", UIStyle.HudBackground);
            GameUI.SetAnchor(_panel, UIAnchor.TopLeft);
            _panel.pivot = new Vector2(0, 1);
            _panel.anchoredPosition = new Vector2(10, -10);

            var layout = GameUI.AddHorizontalLayout(_panel, spacing: 12, padding: 8);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            var fitter = _panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _built = true;
        }

        private void UpdateItems()
        {
            var items = DebugMenuAPI.HudItems;

            // Rebuild text elements if HUD item count changed
            if (items.Count != _lastItemCount)
            {
                RebuildTextElements(items.Count);
                _lastItemCount = items.Count;
            }

            // Update text content
            bool anyVisible = false;
            for (int i = 0; i < items.Count && i < _textItems.Count; i++)
            {
                string text = items[i].GetText?.Invoke();
                var tmp = _textItems[i];

                if (string.IsNullOrEmpty(text))
                {
                    tmp.gameObject.SetActive(false);
                }
                else
                {
                    tmp.gameObject.SetActive(true);
                    tmp.text = text;
                    anyVisible = true;

                    // Color special status items
                    if (text.Contains("[GOD]") || text.Contains("[NOCLIP]"))
                        tmp.color = UIStyle.Warning;
                    else
                        tmp.color = UIStyle.TextPrimary;
                }
            }

            // Show/hide the panel background based on whether anything is visible
            if (_panel != null)
                _panel.gameObject.SetActive(anyVisible);
        }

        private void RebuildTextElements(int count)
        {
            // Remove excess
            while (_textItems.Count > count)
            {
                var last = _textItems[_textItems.Count - 1];
                Destroy(last.gameObject);
                _textItems.RemoveAt(_textItems.Count - 1);
            }

            // Add new
            while (_textItems.Count < count)
            {
                var tmp = GameUI.CreateText(_panel.transform, "HudItem" + _textItems.Count,
                    "", UIStyle.FontSizeBody, UIStyle.TextPrimary,
                    TextAlignmentOptions.MidlineLeft);
                tmp.fontStyle = FontStyles.Bold;
                // Enable TMP shadow/underlay for readability
                tmp.fontMaterial.EnableKeyword("UNDERLAY_ON");
                tmp.fontMaterial.SetColor("_UnderlayColor", new Color(0, 0, 0, 0.7f));
                tmp.fontMaterial.SetFloat("_UnderlayOffsetX", 0.5f);
                tmp.fontMaterial.SetFloat("_UnderlayOffsetY", -0.5f);
                tmp.fontMaterial.SetFloat("_UnderlaySoftness", 0.2f);
                _textItems.Add(tmp);
            }
        }
    }
}
