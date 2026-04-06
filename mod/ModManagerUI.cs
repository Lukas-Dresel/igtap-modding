using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace IGTAPMod
{
    public class ModManagerUI : MonoBehaviour
    {
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;

        private bool isOpen;
        private Canvas canvas;
        private GameObject root;
        private RectTransform contentArea;
        private readonly Dictionary<string, Toggle> toggles = new Dictionary<string, Toggle>();

        private void Update()
        {
            if (ToggleKey.Value.IsDown())
            {
                isOpen = !isOpen;
                if (isOpen) Show();
                else Hide();
            }
        }

        private void Show()
        {
            if (canvas == null)
                BuildUI();
            else
                RefreshModList();

            root.SetActive(true);
        }

        private void Hide()
        {
            if (root != null)
                root.SetActive(false);
        }

        private void BuildUI()
        {
            canvas = GameUI.CreateScreenCanvas("ModManagerCanvas", 200);
            root = canvas.gameObject;

            // Main panel — centered, draggable
            var panel = GameUI.CreatePanel(canvas.transform, "Panel");
            GameUI.SetAnchor(panel, UIAnchor.Center);
            panel.sizeDelta = new Vector2(480, 420);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            GameUI.MakeDraggable(panel);
            GameUI.AddVerticalLayout(panel, spacing: 6, padding: 12);

            // ---- Title bar ----
            var titleRow = GameUI.CreatePanel(panel.transform, "TitleRow", UIStyle.PanelBackgroundLight);
            GameUI.AddHorizontalLayout(titleRow, spacing: 0, padding: 8);
            GameUI.SetSize(titleRow, height: 42);

            var title = GameUI.CreateText(titleRow.transform, "Title", "Mod Manager",
                UIStyle.FontSizeHeader, alignment: TextAlignmentOptions.MidlineLeft);
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            var closeBtn = GameUI.CreateButton(titleRow.transform, "CloseBtn", "X",
                () => { isOpen = false; Hide(); }, fontSize: 18f);
            GameUI.SetSize(closeBtn.GetComponent<RectTransform>(), width: 34, height: 34);

            // ---- Column headers ----
            var headerRow = GameUI.CreatePanel(panel.transform, "HeaderRow", Color.clear);
            GameUI.AddHorizontalLayout(headerRow, spacing: 8, padding: 6);
            GameUI.SetSize(headerRow, height: 24);

            var onLabel = GameUI.CreateText(headerRow.transform, "OnHeader", "On",
                UIStyle.FontSizeSmall, UIStyle.TextSecondary, TextAlignmentOptions.Center);
            onLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 40;

            var nameLabel = GameUI.CreateText(headerRow.transform, "NameHeader", "Mod",
                UIStyle.FontSizeSmall, UIStyle.TextSecondary);
            nameLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            // ---- Scrollable mod list ----
            contentArea = GameUI.CreateScrollView(panel.transform, "ModList");
            // The scroll view root (grandparent of content) must fill remaining space
            var scrollViewGo = contentArea.parent.parent.gameObject;
            var scrollLayout = scrollViewGo.AddComponent<LayoutElement>();
            scrollLayout.flexibleHeight = 1;
            scrollLayout.minHeight = 100;
            GameUI.AddVerticalLayout(contentArea, spacing: 3, padding: 2);

            // Debug: log hierarchy
            Plugin.Log.LogInfo($"[ModManagerUI] contentArea={contentArea.name}, parent={contentArea.parent?.name}, grandparent={contentArea.parent?.parent?.name}");
            Plugin.Log.LogInfo($"[ModManagerUI] scrollViewGo={scrollViewGo.name}, components: {string.Join(", ", scrollViewGo.GetComponents<Component>().Select(c => c.GetType().Name))}");
            Plugin.Log.LogInfo($"[ModManagerUI] panel children: {panel.childCount}");
            for (int i = 0; i < panel.childCount; i++)
            {
                var child = panel.GetChild(i);
                var le = child.GetComponent<LayoutElement>();
                Plugin.Log.LogInfo($"[ModManagerUI]   [{i}] {child.name} le={le != null} rt.sizeDelta={child.GetComponent<RectTransform>()?.sizeDelta}");
            }

            RefreshModList();
        }

        private void RefreshModList()
        {
            // Clear old entries
            for (int i = contentArea.childCount - 1; i >= 0; i--)
                Destroy(contentArea.GetChild(i).gameObject);
            toggles.Clear();

            var plugins = Chainloader.PluginInfos
                .OrderBy(p => p.Value.Metadata.Name)
                .ToList();

            Plugin.Log.LogInfo($"[ModManagerUI] RefreshModList: {plugins.Count} plugins found");

            foreach (var kvp in plugins)
            {
                Plugin.Log.LogInfo($"[ModManagerUI]   creating row for {kvp.Value.Metadata.Name}");
                CreateModRow(kvp.Value);
            }

            Plugin.Log.LogInfo($"[ModManagerUI] contentArea now has {contentArea.childCount} children, sizeDelta={contentArea.sizeDelta}");
            var scrollRect = contentArea.parent?.parent?.GetComponent<ScrollRect>();
            if (scrollRect != null)
                Plugin.Log.LogInfo($"[ModManagerUI] scrollRect.viewport={scrollRect.viewport?.name} content={scrollRect.content?.name}");
        }

        private void CreateModRow(PluginInfo info)
        {
            bool isSelf = info.Metadata.GUID == Plugin.PluginGUID;
            bool isEnabled = info.Instance != null && info.Instance.enabled;

            var row = GameUI.CreatePanel(contentArea.transform,
                "Mod_" + info.Metadata.GUID, UIStyle.PanelBackgroundLight);
            GameUI.AddHorizontalLayout(row, spacing: 8, padding: 6);
            GameUI.SetSize(row, height: 36);

            // Enable / disable toggle (no label — constrain to checkbox size)
            var toggle = GameUI.CreateToggle(row.transform, "Toggle", "",
                isSelf || isEnabled,
                v => OnToggleMod(info, v));
            var toggleLayout = toggle.gameObject.AddComponent<LayoutElement>();
            toggleLayout.preferredWidth = 24;
            toggleLayout.preferredHeight = 24;
            // Hide the empty label child so it doesn't take space
            var toggleLabel = toggle.transform.Find("Label");
            if (toggleLabel != null) toggleLabel.gameObject.SetActive(false);

            if (isSelf)
                toggle.interactable = false;

            toggles[info.Metadata.GUID] = toggle;

            // Name + version
            string color = isSelf ? "#6af" : "#fff";
            string versionColor = "#888";
            var label = GameUI.CreateText(row.transform, "Label",
                $"<color={color}>{info.Metadata.Name}</color>  " +
                $"<color={versionColor}>v{info.Metadata.Version}</color>",
                UIStyle.FontSizeBody);
            label.richText = true;
            label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        }

        private void OnToggleMod(PluginInfo info, bool enabled)
        {
            if (info.Instance == null) return;
            if (info.Metadata.GUID == Plugin.PluginGUID) return;

            info.Instance.enabled = enabled;

            // Also toggle any sibling MonoBehaviours the plugin likely added
            foreach (var mb in info.Instance.gameObject.GetComponents<MonoBehaviour>())
            {
                // Skip other BepInEx plugins on the same object
                if (mb is BaseUnityPlugin) continue;
                mb.enabled = enabled;
            }

            Plugin.Log.LogInfo($"{(enabled ? "Enabled" : "Disabled")} {info.Metadata.Name}");
        }
    }
}
