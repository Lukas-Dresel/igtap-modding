using System;
using System.Collections.Generic;
using IGTAPMod;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IGTAPSpeedrun
{
    public class ProfileSelectorUI : MonoBehaviour
    {
        private Canvas canvas;
        private string selectedProfileName; // null = auto-detect
        private Action<SpeedrunProfile> onSelect;
        private List<SpeedrunProfile> profiles;
        private readonly List<Button> profileButtons = new List<Button>();
        private readonly List<string> profileLabels = new List<string>();

        public static ProfileSelectorUI Show(string lastProfileName, Action<SpeedrunProfile> onSelect)
        {
            var go = new GameObject("SpeedrunProfileSelector");
            var ui = go.AddComponent<ProfileSelectorUI>();
            ui.selectedProfileName = lastProfileName;
            ui.onSelect = onSelect;
            ui.Build();
            return ui;
        }

        private void Build()
        {
            canvas = GameUI.CreateScreenCanvas("SpeedrunSelectorCanvas", 200);

            profiles = ProfileManager.LoadAll();

            // Dimmed background overlay (raycast disabled so clicks reach the center panel)
            var overlay = GameUI.CreatePanel(canvas.transform, "Overlay", new Color(0, 0, 0, 0.6f));
            GameUI.SetAnchor(overlay, UIAnchor.StretchAll);
            overlay.offsetMin = Vector2.zero;
            overlay.offsetMax = Vector2.zero;
            overlay.GetComponent<Image>().raycastTarget = false;

            // Center panel
            var panel = GameUI.CreatePanel(canvas.transform, "Panel", UIStyle.PanelBackground);
            GameUI.SetAnchor(panel, UIAnchor.Center);
            panel.sizeDelta = new Vector2(340, 400);
            var vLayout = GameUI.AddVerticalLayout(panel, 6, 16);
            vLayout.childAlignment = TextAnchor.UpperCenter;

            // Title
            var title = GameUI.CreateText(panel.transform, "Title", "Select Profile",
                UIStyle.FontSizeHeader, UIStyle.TextPrimary, TextAlignmentOptions.Center);
            GameUI.SetSize(title.rectTransform, height: 36);

            // Scroll area for profile list
            var scrollContent = GameUI.CreateScrollView(panel.transform, "ProfileList",
                bgColor: UIStyle.PanelBackgroundLight);
            GameUI.SetSize(scrollContent.parent.parent.GetComponent<RectTransform>(), height: 260);
            var scrollLayout = GameUI.AddVerticalLayout(scrollContent, 4, 6);

            // Auto-detect button
            AddProfileButton(scrollContent.transform, "Auto-detect", null);

            // Profile buttons
            foreach (var profile in profiles)
            {
                var p = profile; // capture
                AddProfileButton(scrollContent.transform, p.name, p.name);
            }

            // Highlight the last-used or auto-detect
            UpdateSelection();

            // Bottom buttons row
            var bottomRow = GameUI.CreatePanel(panel.transform, "BottomRow", Color.clear);
            GameUI.SetSize(bottomRow, height: 40);
            var hLayout = GameUI.AddHorizontalLayout(bottomRow, 10, 0);
            hLayout.childAlignment = TextAnchor.MiddleCenter;

            var okBtn = GameUI.CreateButton(bottomRow.transform, "BtnOK", "Start",
                OnOK, 18f, UIStyle.Success);
            GameUI.SetSize(okBtn.GetComponent<RectTransform>(), width: 120, height: 36);

            var cancelBtn = GameUI.CreateButton(bottomRow.transform, "BtnCancel", "Cancel",
                OnCancel, 18f);
            GameUI.SetSize(cancelBtn.GetComponent<RectTransform>(), width: 120, height: 36);
        }

        private void AddProfileButton(Transform parent, string label, string profileName)
        {
            var btn = GameUI.CreateButton(parent, $"Btn_{label}", label,
                () => SelectProfile(profileName), 16f);
            GameUI.SetSize(btn.GetComponent<RectTransform>(), height: 34);
            // Left-align text so selection indicator doesn't shift layout
            var tmp = btn.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (tmp != null)
                tmp.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
            profileButtons.Add(btn);
            profileLabels.Add(label);
        }

        private void SelectProfile(string name)
        {
            Plugin.Log.LogInfo($"[ProfileSelector] SelectProfile clicked: '{name ?? "auto-detect"}'");
            selectedProfileName = name;
            UpdateSelection();
        }

        private void UpdateSelection()
        {
            for (int i = 0; i < profileButtons.Count; i++)
            {
                bool isSelected;
                if (i == 0)
                    isSelected = selectedProfileName == null;
                else
                    isSelected = profiles[i - 1].name == selectedProfileName;

                // Update the button label text to show selection
                var label = profileLabels[i];
                var tmp = profileButtons[i].GetComponentInChildren<TMPro.TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.color = isSelected ? Color.white : UIStyle.TextMuted;
                }
            }
        }

        private void OnOK()
        {
            SpeedrunProfile profile = null;
            if (selectedProfileName != null)
            {
                foreach (var p in profiles)
                {
                    if (p.name == selectedProfileName)
                    {
                        profile = p;
                        break;
                    }
                }
            }

            Plugin.Log.LogInfo($"[ProfileSelector] OnOK: selected='{selectedProfileName ?? "auto-detect"}', profile={profile?.name ?? "null"}");
            onSelect?.Invoke(profile);
            Close();
        }

        private void OnCancel()
        {
            Close();
        }

        private void Close()
        {
            if (canvas != null)
                Destroy(canvas.gameObject);
            Destroy(gameObject);
        }

        private void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame)
                OnCancel();
            if (kb.enterKey.wasPressedThisFrame)
                OnOK();
        }
    }
}
