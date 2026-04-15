using System;
using System.Collections.Generic;
using System.Linq;
using IGTAPMod;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace IGTAPSpeedrun
{
    public class ProfileEditorUI : MonoBehaviour
    {
        // Special profile name for the auto-detect pseudo-profile. The auto-detect "profile"
        // isn't stored on disk — it lives in the AutoDetectStartSplits/AutoDetectEndSplit configs.
        // The editor treats it like any other profile but routes save/load through the configs.
        public const string AutoDetectProfileName = "<Auto-detect>";

        private Canvas canvas;
        private SpeedrunProfile editingProfile;
        private List<SpeedrunProfile> allProfiles;

        // Available (left) and selected (right) lists
        private List<SplitDef> available = new List<SplitDef>();
        private List<SplitDef> selected = new List<SplitDef>();

        // Selection state
        private int availableSelection = -1;
        private int selectedSelection = -1;

        // UI elements that need rebuilding
        private RectTransform availableContent;
        private RectTransform selectedContent;
        private TMP_InputField nameInput;

        private Color normalBtnColor = UIStyle.ButtonNormal;
        private Color selectedBtnColor = UIStyle.Accent;

        public static ProfileEditorUI Show(bool startWithAutoDetect = false)
        {
            var go = new GameObject("SpeedrunProfileEditor");
            var ui = go.AddComponent<ProfileEditorUI>();
            ui._startWithAutoDetect = startWithAutoDetect;
            ui.Build();
            return ui;
        }

        private bool _startWithAutoDetect;

        private TMP_Dropdown profileDropdown;

        private void Build()
        {
            allProfiles = LoadAllWithAutoDetect();
            if (_startWithAutoDetect)
            {
                LoadProfile(BuildAutoDetectProfile());
            }
            else
            {
                // Default to the last-used profile (per Plugin.LastProfileName) if it still exists.
                // Empty LastProfileName means auto-detect mode, which is allProfiles[0].
                SpeedrunProfile initial = null;
                string lastName = Plugin.LastProfileName.Value;
                if (!string.IsNullOrEmpty(lastName))
                {
                    foreach (var p in allProfiles)
                        if (p.name == lastName) { initial = p; break; }
                }
                if (initial == null && allProfiles.Count > 0)
                    initial = allProfiles[0];
                if (initial != null)
                    LoadProfile(initial);
                else
                    LoadProfile(ProfileManager.GetDefault());
            }

            canvas = GameUI.CreateScreenCanvas("SpeedrunEditorCanvas", 200);

            // Dimmed background (full-screen click blocker)
            var overlay = GameUI.CreatePanel(canvas.transform, "Overlay", new Color(0, 0, 0, 0.7f));
            GameUI.SetAnchor(overlay, UIAnchor.StretchAll);
            overlay.offsetMin = Vector2.zero;
            overlay.offsetMax = Vector2.zero;

            // Main panel — bigger and themed
            var panel = GameUI.CreatePanel(canvas.transform, "EditorPanel", applyTheme: true);
            GameUI.SetAnchor(panel, UIAnchor.Center);
            panel.sizeDelta = new Vector2(1100, 720);
            var mainLayout = GameUI.AddVerticalLayout(panel, 10, 20);
            mainLayout.padding = new RectOffset(20, 20, 16, 16);
            mainLayout.childForceExpandHeight = false;

            var theme = GameUITheme.Instance;

            // ---- Title ----
            var title = GameUI.CreateText(panel.transform, "Title", "Profile Editor",
                UIStyle.FontSizeHeader, UIStyle.TextPrimary, TextAlignmentOptions.Center);
            GameUI.SetSize(title.rectTransform, height: 36);

            // ---- Profile selector row: dropdown + name input + new + delete ----
            var selectorRow = GameUI.CreatePanel(panel.transform, "SelectorRow", Color.clear);
            var selectorRowLe = selectorRow.gameObject.AddComponent<LayoutElement>();
            selectorRowLe.preferredHeight = 44;
            selectorRowLe.flexibleHeight = 0;
            selectorRowLe.layoutPriority = 2;
            var selectorLayout = GameUI.AddHorizontalLayout(selectorRow, 8, 0);
            selectorLayout.childAlignment = TextAnchor.MiddleLeft;
            selectorLayout.childForceExpandWidth = false;

            var profLabel = GameUI.CreateText(selectorRow.transform, "ProfLabel", "Profile:",
                UIStyle.FontSizeBody, UIStyle.TextSecondary, TextAlignmentOptions.MidlineLeft);
            var profLabelLe = profLabel.gameObject.AddComponent<LayoutElement>();
            profLabelLe.preferredWidth = 70;
            profLabelLe.preferredHeight = 36;

            // Profile dropdown — clones the game's TMP_Dropdown so it matches the rest of the mod
            profileDropdown = theme.CloneDropdown(selectorRow.transform, "ProfileSelector");
            if (profileDropdown != null)
            {
                var ddLe = profileDropdown.gameObject.AddComponent<LayoutElement>();
                ddLe.preferredWidth = 280;
                ddLe.preferredHeight = 36;
                RefreshProfileDropdown();
                profileDropdown.onValueChanged.AddListener(idx =>
                {
                    if (idx >= 0 && idx < allProfiles.Count)
                    {
                        // For auto-detect, rebuild fresh from current configs each load
                        if (allProfiles[idx].name == AutoDetectProfileName)
                            LoadProfile(BuildAutoDetectProfile());
                        else
                            LoadProfile(allProfiles[idx]);
                        RebuildLists();
                    }
                });
            }

            var nameLabel = GameUI.CreateText(selectorRow.transform, "NameLabel", "Name:",
                UIStyle.FontSizeBody, UIStyle.TextSecondary, TextAlignmentOptions.MidlineLeft);
            var nameLabelLe = nameLabel.gameObject.AddComponent<LayoutElement>();
            nameLabelLe.preferredWidth = 60;
            nameLabelLe.preferredHeight = 36;

            nameInput = GameUI.CreateInputField(selectorRow.transform, "NameInput", "Profile name...");
            nameInput.text = editingProfile.name;
            var nameInputLe = nameInput.gameObject.AddComponent<LayoutElement>();
            nameInputLe.preferredWidth = 280;
            nameInputLe.preferredHeight = 36;
            nameInputLe.flexibleWidth = 1;

            var newBtn = theme.CloneButton(selectorRow.transform, "New", OnNew, textOnly: true);
            var newBtnLe = newBtn.gameObject.AddComponent<LayoutElement>();
            newBtnLe.preferredWidth = 90;
            newBtnLe.preferredHeight = 36;

            var delBtn = theme.CloneButton(selectorRow.transform, "Delete", OnDelete, textOnly: true);
            var delBtnLe = delBtn.gameObject.AddComponent<LayoutElement>();
            delBtnLe.preferredWidth = 90;
            delBtnLe.preferredHeight = 36;

            // ---- Shuttle picker ----
            var shuttleRow = GameUI.CreatePanel(panel.transform, "ShuttleRow", Color.clear);
            var shuttleRowLe = shuttleRow.gameObject.AddComponent<LayoutElement>();
            shuttleRowLe.flexibleHeight = 1;
            shuttleRowLe.preferredHeight = 480;
            var shuttleLayout = GameUI.AddHorizontalLayout(shuttleRow, 12, 0);
            shuttleLayout.childForceExpandWidth = false;
            shuttleLayout.childControlWidth = true;
            shuttleLayout.childControlHeight = true;

            // Left: Available
            var leftPanel = GameUI.CreatePanel(shuttleRow.transform, "AvailablePanel", UIStyle.PanelBackgroundLight);
            var leftLe = leftPanel.gameObject.AddComponent<LayoutElement>();
            leftLe.preferredWidth = 320;
            leftLe.flexibleHeight = 1;
            var leftLayout = GameUI.AddVerticalLayout(leftPanel, 6, 10);

            var leftLabel = GameUI.CreateText(leftPanel.transform, "AvailLabel", "Available",
                UIStyle.FontSizeBody, UIStyle.TextSecondary, TextAlignmentOptions.Center);
            GameUI.SetSize(leftLabel.rectTransform, height: 26);

            availableContent = GameUI.CreateScrollView(leftPanel.transform, "AvailScroll",
                bgColor: Color.clear);
            var availScrollGo = availableContent.parent.parent.gameObject;
            var availScrollLe = availScrollGo.AddComponent<LayoutElement>();
            availScrollLe.flexibleHeight = 1;
            availScrollLe.minHeight = 100;
            GameUI.AddVerticalLayout(availableContent, 4, 6);

            // Center: arrow buttons
            var arrowPanel = GameUI.CreatePanel(shuttleRow.transform, "ArrowPanel", Color.clear);
            var arrowLe = arrowPanel.gameObject.AddComponent<LayoutElement>();
            arrowLe.preferredWidth = 80;
            var arrowLayout = GameUI.AddVerticalLayout(arrowPanel, 8, 4);
            arrowLayout.childAlignment = TextAnchor.MiddleCenter;
            arrowLayout.childForceExpandWidth = false;
            arrowLayout.childForceExpandHeight = false;

            var spacerTop = GameUI.CreatePanel(arrowPanel.transform, "SpacerTop", Color.clear);
            GameUI.SetSize(spacerTop, height: 60);

            var addBtn = theme.CloneButton(arrowPanel.transform, "→", OnAdd, textOnly: true, fontSize: 26f);
            var addBtnLe = addBtn.gameObject.AddComponent<LayoutElement>();
            addBtnLe.preferredWidth = 60;
            addBtnLe.preferredHeight = 40;

            var removeBtn = theme.CloneButton(arrowPanel.transform, "←", OnRemove, textOnly: true, fontSize: 26f);
            var removeBtnLe = removeBtn.gameObject.AddComponent<LayoutElement>();
            removeBtnLe.preferredWidth = 60;
            removeBtnLe.preferredHeight = 40;

            var spacerMid = GameUI.CreatePanel(arrowPanel.transform, "SpacerMid", Color.clear);
            GameUI.SetSize(spacerMid, height: 12);

            var upBtn = theme.CloneButton(arrowPanel.transform, "↑", OnMoveUp, textOnly: true, fontSize: 26f);
            var upBtnLe = upBtn.gameObject.AddComponent<LayoutElement>();
            upBtnLe.preferredWidth = 60;
            upBtnLe.preferredHeight = 40;

            var downBtn = theme.CloneButton(arrowPanel.transform, "↓", OnMoveDown, textOnly: true, fontSize: 26f);
            var downBtnLe = downBtn.gameObject.AddComponent<LayoutElement>();
            downBtnLe.preferredWidth = 60;
            downBtnLe.preferredHeight = 40;

            // Right: Selected (in order)
            var rightPanel = GameUI.CreatePanel(shuttleRow.transform, "SelectedPanel", UIStyle.PanelBackgroundLight);
            var rightLe = rightPanel.gameObject.AddComponent<LayoutElement>();
            rightLe.flexibleWidth = 1;
            rightLe.minWidth = 500;
            rightLe.flexibleHeight = 1;
            var rightLayout = GameUI.AddVerticalLayout(rightPanel, 6, 10);

            // Combined title + column header row: "Selected (in order)" on the left,
            // "Starts" and "Ends" column labels aligned above the toggle columns.
            // NOTE: we set LE.layoutPriority=2 to beat the HorizontalLayoutGroup (default prio=1).
            // The HLG on this row implements ILayoutElement and reports its own preferredHeight
            // based on the TMP children. With narrow initial width, TMP wraps and returns a huge
            // preferredHeight (~196), and the parent VLG picks the max of (our LE, HLG), so the
            // LE gets ignored unless its priority is higher.
            var headerRow = GameUI.CreatePanel(rightPanel.transform, "HeaderRow", Color.clear);
            var headerRowLe = headerRow.gameObject.AddComponent<LayoutElement>();
            headerRowLe.preferredHeight = 22;
            headerRowLe.flexibleHeight = 0;
            headerRowLe.layoutPriority = 2;
            var headerLayout = GameUI.AddHorizontalLayout(headerRow, 4, 0);
            headerLayout.childAlignment = TextAnchor.MiddleLeft;
            headerLayout.childForceExpandWidth = false;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;

            var hdrName = GameUI.CreateText(headerRow.transform, "HdrName", "Selected (in order)",
                UIStyle.FontSizeSmall, UIStyle.TextSecondary, TextAlignmentOptions.MidlineLeft);
            var hdrNameLe = hdrName.gameObject.AddComponent<LayoutElement>();
            hdrNameLe.flexibleWidth = 1;

            var hdrStart = GameUI.CreateText(headerRow.transform, "HdrStart", "Starts",
                UIStyle.FontSizeSmall, UIStyle.TextMuted, TextAlignmentOptions.Center);
            var hdrStartLe = hdrStart.gameObject.AddComponent<LayoutElement>();
            hdrStartLe.preferredWidth = 70;

            var hdrEnd = GameUI.CreateText(headerRow.transform, "HdrEnd", "Ends",
                UIStyle.FontSizeSmall, UIStyle.TextMuted, TextAlignmentOptions.Center);
            var hdrEndLe = hdrEnd.gameObject.AddComponent<LayoutElement>();
            hdrEndLe.preferredWidth = 70;

            selectedContent = GameUI.CreateScrollView(rightPanel.transform, "SelectedScroll",
                bgColor: Color.clear);
            var selScrollGo = selectedContent.parent.parent.gameObject;
            var selScrollLe = selScrollGo.AddComponent<LayoutElement>();
            selScrollLe.flexibleHeight = 1;
            selScrollLe.minHeight = 100;
            GameUI.AddVerticalLayout(selectedContent, 4, 6);

            // ---- Bottom button row ----
            var bottomRow = GameUI.CreatePanel(panel.transform, "BottomRow", Color.clear);
            var bottomRowLe = bottomRow.gameObject.AddComponent<LayoutElement>();
            bottomRowLe.preferredHeight = 48;
            bottomRowLe.flexibleHeight = 0;
            bottomRowLe.layoutPriority = 2;
            var bottomLayout = GameUI.AddHorizontalLayout(bottomRow, 16, 0);
            bottomLayout.childAlignment = TextAnchor.MiddleCenter;
            bottomLayout.childForceExpandWidth = false;

            var saveBtn = theme.CloneButton(bottomRow.transform, "Save", OnSave, textOnly: true);
            var saveBtnLe = saveBtn.gameObject.AddComponent<LayoutElement>();
            saveBtnLe.preferredWidth = 180;
            saveBtnLe.preferredHeight = 44;

            var cancelBtn = theme.CloneButton(bottomRow.transform, "Close", OnClose, textOnly: true);
            var cancelBtnLe = cancelBtn.gameObject.AddComponent<LayoutElement>();
            cancelBtnLe.preferredWidth = 180;
            cancelBtnLe.preferredHeight = 44;

            GameUI.MakeDraggable(panel);
            RebuildLists();

            // DIAGNOSTIC: dump all major layout containers one frame later so we can see what
            // the actual rendered sizes are and find the "too big" header.
            StartCoroutine(DiagDumpLayoutNextFrame());
        }

        private System.Collections.IEnumerator DiagDumpLayoutNextFrame()
        {
            yield return null;
            if (canvas == null) yield break;
            Plugin.Log.LogInfo("[ProfileEditor] === Layout dump ===");
            DumpRect("EditorPanel", canvas.transform.Find("EditorPanel") as RectTransform);
            DumpRect("SelectorRow", canvas.transform.Find("EditorPanel/SelectorRow") as RectTransform);
            DumpRect("ShuttleRow", canvas.transform.Find("EditorPanel/ShuttleRow") as RectTransform);
            DumpRect("AvailablePanel", canvas.transform.Find("EditorPanel/ShuttleRow/AvailablePanel") as RectTransform);
            DumpRect("ArrowPanel", canvas.transform.Find("EditorPanel/ShuttleRow/ArrowPanel") as RectTransform);
            DumpRect("SelectedPanel", canvas.transform.Find("EditorPanel/ShuttleRow/SelectedPanel") as RectTransform);
            DumpRect("SelectedPanel/HeaderRow", canvas.transform.Find("EditorPanel/ShuttleRow/SelectedPanel/HeaderRow") as RectTransform);
            DumpRect("SelectedPanel/SelectedScroll", canvas.transform.Find("EditorPanel/ShuttleRow/SelectedPanel/SelectedScroll") as RectTransform);
            DumpRect("BottomRow", canvas.transform.Find("EditorPanel/BottomRow") as RectTransform);
        }

        private static void DumpRect(string label, RectTransform rt)
        {
            if (rt == null)
            {
                Plugin.Log.LogInfo($"  [{label}] null");
                return;
            }
            var le = rt.GetComponent<LayoutElement>();
            string leInfo = le != null ? $" LE(prefH={le.preferredHeight}, minH={le.minHeight}, flexH={le.flexibleHeight})" : " (no LE)";
            Plugin.Log.LogInfo($"  [{label}] rect={rt.rect} sizeDelta={rt.sizeDelta}{leInfo}");
        }

        private void RefreshProfileDropdown()
        {
            if (profileDropdown == null) return;
            profileDropdown.ClearOptions();
            var opts = new List<TMP_Dropdown.OptionData>();
            foreach (var p in allProfiles)
                opts.Add(new TMP_Dropdown.OptionData(p.name));
            profileDropdown.AddOptions(opts);
            int idx = -1;
            for (int i = 0; i < allProfiles.Count; i++)
                if (allProfiles[i].name == editingProfile.name) { idx = i; break; }
            int finalIdx = idx >= 0 ? idx : 0;
            profileDropdown.SetValueWithoutNotify(finalIdx);
            profileDropdown.RefreshShownValue();
            // Force the caption text directly in case Unity's internal refresh fails on a cloned dropdown
            if (profileDropdown.captionText != null && finalIdx < allProfiles.Count)
                profileDropdown.captionText.text = allProfiles[finalIdx].name;

            Plugin.Log.LogInfo($"[ProfileEditor] RefreshProfileDropdown: {opts.Count} options, idx={finalIdx}, caption='{profileDropdown.captionText?.text}', dd.options.Count={profileDropdown.options.Count}");
        }

        private void LoadProfile(SpeedrunProfile profile)
        {
            editingProfile = profile;
            selected = new List<SplitDef>();
            foreach (var s in profile.splits)
                selected.Add(new SplitDef(s.id, s.label, s.startsRun, s.endsRun));

            RefreshAvailable();
            availableSelection = -1;
            selectedSelection = -1;

            if (nameInput != null)
                nameInput.text = profile.name;
            // Sync dropdown selection to current profile
            RefreshProfileDropdown();
        }

        private void RefreshAvailable()
        {
            var selectedIds = new HashSet<string>();
            foreach (var s in selected)
                selectedIds.Add(s.id);

            available = new List<SplitDef>();
            foreach (var cat in ProfileManager.Catalog)
            {
                if (!selectedIds.Contains(cat.id))
                    available.Add(new SplitDef(cat.id, cat.label, cat.startsRun, cat.endsRun));
            }
        }

        private void RebuildLists()
        {
            RefreshAvailable();
            RebuildListContent(availableContent, available, true);
            RebuildListContent(selectedContent, selected, false);
        }

        private void RebuildListContent(RectTransform content, List<SplitDef> items, bool isAvailable)
        {
            // Clear existing children
            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);

            var theme = GameUITheme.Instance;

            for (int idx = 0; idx < items.Count; idx++)
            {
                int capturedIdx = idx;
                var item = items[idx];

                if (isAvailable)
                {
                    // Available list: simple click-to-select button using game theme (text-only).
                    var btn = theme.CloneButton(content.transform, item.label,
                        () =>
                        {
                            availableSelection = capturedIdx;
                            selectedSelection = -1;
                            UpdateListHighlights();
                        }, textOnly: true);
                    var btnLe = btn.gameObject.AddComponent<LayoutElement>();
                    btnLe.preferredHeight = 32;
                    btnLe.flexibleWidth = 1;

                    if (capturedIdx == availableSelection)
                    {
                        var colors = btn.colors;
                        colors.normalColor = selectedBtnColor;
                        btn.colors = colors;
                    }
                }
                else
                {
                    // Selected list: row with [select button] [Starts toggle] [Ends toggle]
                    var row = GameUI.CreatePanel(content.transform, $"Row_{item.id}", Color.clear);
                    var rowLayout = GameUI.AddHorizontalLayout(row, spacing: 4, padding: 0);
                    rowLayout.childAlignment = TextAnchor.MiddleLeft;
                    rowLayout.childForceExpandWidth = false;
                    rowLayout.childControlWidth = true;
                    rowLayout.childControlHeight = true;
                    var rowLe = row.gameObject.AddComponent<LayoutElement>();
                    rowLe.preferredHeight = 36;
                    rowLe.flexibleWidth = 1;

                    var btn = theme.CloneButton(row.transform, item.label,
                        () =>
                        {
                            selectedSelection = capturedIdx;
                            availableSelection = -1;
                            UpdateListHighlights();
                        }, textOnly: true);
                    var btnLe = btn.gameObject.AddComponent<LayoutElement>();
                    btnLe.flexibleWidth = 1;
                    btnLe.minWidth = 60;
                    btnLe.preferredHeight = 32;

                    if (capturedIdx == selectedSelection)
                    {
                        var colors = btn.colors;
                        colors.normalColor = selectedBtnColor;
                        btn.colors = colors;
                    }

                    var startsToggle = theme.CloneToggle(row.transform, "",
                        item.startsRun, v =>
                        {
                            if (capturedIdx >= 0 && capturedIdx < selected.Count)
                                selected[capturedIdx].startsRun = v;
                        });
                    var startsLe = startsToggle.gameObject.AddComponent<LayoutElement>();
                    startsLe.preferredWidth = 70;
                    startsLe.preferredHeight = 32;

                    var endsToggle = theme.CloneToggle(row.transform, "",
                        item.endsRun, v =>
                        {
                            if (capturedIdx >= 0 && capturedIdx < selected.Count)
                                selected[capturedIdx].endsRun = v;
                        });
                    var endsLe = endsToggle.gameObject.AddComponent<LayoutElement>();
                    endsLe.preferredWidth = 70;
                    endsLe.preferredHeight = 32;
                }
            }
        }

        private void UpdateListHighlights()
        {
            HighlightList(availableContent, availableSelection);
            HighlightList(selectedContent, selectedSelection);
        }

        private void HighlightList(RectTransform content, int selection)
        {
            for (int i = 0; i < content.childCount; i++)
            {
                // The available list has direct Buttons; the selected list wraps each Button in a row Panel.
                var child = content.GetChild(i);
                var btn = child.GetComponent<Button>() ?? child.GetComponentInChildren<Button>();
                if (btn != null)
                {
                    bool isSel = (i == selection);
                    var colors = btn.colors;
                    colors.normalColor = isSel ? selectedBtnColor : normalBtnColor;
                    colors.highlightedColor = isSel ? selectedBtnColor : UIStyle.ButtonHighlight;
                    colors.selectedColor = isSel ? selectedBtnColor : normalBtnColor;
                    btn.colors = colors;

                    var img = btn.GetComponent<Image>();
                    if (img != null)
                        img.color = isSel ? selectedBtnColor : normalBtnColor;
                }
            }
        }

        private void OnAdd()
        {
            if (availableSelection < 0 || availableSelection >= available.Count) return;
            var item = available[availableSelection];
            selected.Add(item);
            availableSelection = -1;
            RebuildLists();
        }

        private void OnRemove()
        {
            if (selectedSelection < 0 || selectedSelection >= selected.Count) return;
            selected.RemoveAt(selectedSelection);
            selectedSelection = -1;
            RebuildLists();
        }

        private void OnMoveUp()
        {
            if (selectedSelection <= 0 || selectedSelection >= selected.Count) return;
            var item = selected[selectedSelection];
            selected.RemoveAt(selectedSelection);
            selected.Insert(selectedSelection - 1, item);
            selectedSelection--;
            RebuildLists();
        }

        private void OnMoveDown()
        {
            if (selectedSelection < 0 || selectedSelection >= selected.Count - 1) return;
            var item = selected[selectedSelection];
            selected.RemoveAt(selectedSelection);
            selected.Insert(selectedSelection + 1, item);
            selectedSelection++;
            RebuildLists();
        }


        private void OnNew()
        {
            Plugin.Log.LogInfo("[ProfileEditor] OnNew clicked");

            // Generate a unique name so clicking New repeatedly doesn't collide with
            // existing profiles in the in-memory list or the dropdown.
            string baseName = "New Profile";
            string name = baseName;
            int n = 2;
            while (allProfiles.Any(p => p.name == name))
                name = $"{baseName} {n++}";

            var profile = new SpeedrunProfile
            {
                name = name,
                splits = new List<SplitDef>()
            };

            // Add it to the in-memory list so RefreshProfileDropdown finds the new entry
            // and the dropdown shows it selected. The profile isn't persisted to disk
            // until the user clicks Save.
            allProfiles.Add(profile);

            LoadProfile(profile);
            RebuildLists();
        }

        private void OnDelete()
        {
            if (string.IsNullOrEmpty(editingProfile.name)) return;
            if (IsEditingAutoDetect)
            {
                Plugin.Log.LogWarning("Cannot delete the auto-detect pseudo-profile");
                return;
            }
            ProfileManager.Delete(editingProfile.name);
            Plugin.Log.LogInfo($"Deleted profile: {editingProfile.name}");

            allProfiles = LoadAllWithAutoDetect();
            if (allProfiles.Count > 0)
                LoadProfile(allProfiles[0]);
            else
                OnNew();
            RebuildLists();
            RefreshProfileDropdown();
        }

        private bool IsEditingAutoDetect => editingProfile != null && editingProfile.name == AutoDetectProfileName;

        private void OnSave()
        {
            if (IsEditingAutoDetect)
            {
                SaveAutoDetect();
                return;
            }

            string name = nameInput.text.Trim();
            if (string.IsNullOrEmpty(name) || name == AutoDetectProfileName)
            {
                Plugin.Log.LogWarning("Profile name cannot be empty or reserved");
                return;
            }

            // If name changed, delete old file
            if (editingProfile.name != name && !string.IsNullOrEmpty(editingProfile.name))
                ProfileManager.Delete(editingProfile.name);

            editingProfile.name = name;
            editingProfile.splits = new List<SplitDef>(selected);
            ProfileManager.Save(editingProfile);
            Plugin.Log.LogInfo($"Saved profile: {name} ({selected.Count} splits)");
        }

        /// <summary>
        /// Saves the current selected list back to the AutoDetect comma-separated configs.
        /// Only IDs that have starts/endsRun checked are written; tracking is unaffected.
        /// </summary>
        private void SaveAutoDetect()
        {
            var startIds = new List<string>();
            var endIds = new List<string>();
            foreach (var s in selected)
            {
                if (s.startsRun) startIds.Add(s.id);
                if (s.endsRun) endIds.Add(s.id);
            }
            Plugin.AutoDetectStartSplits.Value = string.Join(",", startIds);
            Plugin.AutoDetectEndSplit.Value = string.Join(",", endIds);
            Plugin.Log.LogInfo($"Saved auto-detect triggers: starts=[{Plugin.AutoDetectStartSplits.Value}] ends=[{Plugin.AutoDetectEndSplit.Value}]");
        }

        /// <summary>
        /// Builds a virtual SpeedrunProfile representing the current AutoDetect config.
        /// All catalog entries are included; startsRun/endsRun are populated from the configs.
        /// </summary>
        private static SpeedrunProfile BuildAutoDetectProfile()
        {
            var startSet = new HashSet<string>(Plugin.AutoDetectStartSplits.Value.Split(',').Select(s => s.Trim()));
            var endSet = new HashSet<string>(Plugin.AutoDetectEndSplit.Value.Split(',').Select(s => s.Trim()));

            var splits = new List<SplitDef>();
            foreach (var cat in ProfileManager.Catalog)
            {
                splits.Add(new SplitDef(
                    cat.id,
                    cat.label,
                    startsRun: startSet.Contains(cat.id),
                    endsRun: endSet.Contains(cat.id)));
            }
            return new SpeedrunProfile { name = AutoDetectProfileName, splits = splits };
        }

        /// <summary>
        /// Loads disk profiles AND prepends the auto-detect virtual profile.
        /// </summary>
        private static List<SpeedrunProfile> LoadAllWithAutoDetect()
        {
            var list = new List<SpeedrunProfile>();
            list.Add(BuildAutoDetectProfile());
            list.AddRange(ProfileManager.LoadAll());
            return list;
        }

        private void OnClose()
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
                OnClose();
        }
    }
}
