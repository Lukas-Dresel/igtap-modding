using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IGTAPMod
{
    public class ModKeybindInjector : MonoBehaviour
    {
        // Cached scene references (reset on scene load)
        private pauseMenuScript pauseMenu;
        private GameObject settingsBit;
        private Transform keybindContainer;
        private GameObject rowTemplate;

        // Reflection accessors for private fields
        private static readonly FieldInfo F_settingsBit =
            AccessTools.Field(typeof(pauseMenuScript), "settingsBit");
        private static readonly FieldInfo F_keyboardDisplay =
            AccessTools.Field(typeof(KeybindSetterItemScript), "keyboardButtonDisplay");
        private static readonly FieldInfo F_controllerDisplay =
            AccessTools.Field(typeof(KeybindSetterItemScript), "controllerButtonDisplay");

        // Injection state
        private bool injected;
        private readonly List<BindingRow> rows = new List<BindingRow>();

        // Rebind state
        private bool waitingForKey;
        private BindingRow rebindingRow;

        private class BindingRow
        {
            public string ModName;
            public string Label;
            public ConfigEntry<KeyboardShortcut> Config;
            public TMP_Text KeyDisplay;
            public GameObject Root;
            public GameObject WaitingPopup;
        }

        private void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            injected = false;
            rows.Clear();
            pauseMenu = null;
            settingsBit = null;
            keybindContainer = null;
            rowTemplate = null;
        }

        private void Update()
        {
            if (pauseMenu == null)
            {
                pauseMenu = FindAnyObjectByType<pauseMenuScript>();
                if (pauseMenu == null) return;
                settingsBit = (GameObject)F_settingsBit.GetValue(pauseMenu);
            }

            if (settingsBit == null) return;

            if (settingsBit.activeInHierarchy && !injected)
            {
                try
                {
                    if (FindKeybindContainer())
                    {
                        InjectModKeybindings();
                        Plugin.Log.LogInfo($"ModKeybindInjector: injected {rows.Count} keybinding row(s)");
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"ModKeybindInjector: injection failed: {e}");
                }
                // Always mark injected so we don't retry every frame on failure
                injected = true;
            }

            if (waitingForKey)
                PollRebindInput();
        }

        private bool FindKeybindContainer()
        {
            var setters = settingsBit.GetComponentsInChildren<KeybindSetterItemScript>(true);
            if (setters.Length == 0)
            {
                Plugin.Log.LogWarning("ModKeybindInjector: no KeybindSetterItemScript found");
                return false;
            }

            // 'Controls' is the TMP header label; keybind items are its children
            var controlsTransform = setters[0].transform.parent;
            rowTemplate = setters[0].gameObject;

            // Log keybind item positions
            for (int i = 0; i < setters.Length; i++)
            {
                var t = setters[i].transform;
                Plugin.Log.LogInfo($"[ModKeybindInjector] setter[{i}] '{t.name}' localPos={t.localPosition}");
            }

            // Controls is both the "Controls" header text AND the parent of keybind rows.
            // We need to:
            // 1. Leave Controls in place as the fixed header
            // 2. Create a scrollable container below it
            // 3. Move the keybind children into the scrollable container
            var controlsRT = controlsTransform.GetComponent<RectTransform>();
            if (controlsRT != null && settingsBit.transform.Find("ModKeybindViewport") == null)
            {
                // Create viewport below the Controls header
                var viewport = new GameObject("ModKeybindViewport", typeof(RectTransform));
                var vpRT = viewport.GetComponent<RectTransform>();
                viewport.transform.SetParent(settingsBit.transform, false);
                vpRT.anchorMin = controlsRT.anchorMin;
                vpRT.anchorMax = controlsRT.anchorMax;
                vpRT.pivot = new Vector2(0.5f, 1f);
                vpRT.anchoredPosition = new Vector2(controlsRT.anchoredPosition.x,
                    controlsRT.anchoredPosition.y - 60);
                vpRT.sizeDelta = new Vector2(350, 370);

                viewport.AddComponent<RectMask2D>();
                var vpImage = viewport.AddComponent<Image>();
                vpImage.color = new Color(0, 0, 0, 0);
                vpImage.raycastTarget = true;

                // Create a content container inside the viewport
                var content = new GameObject("ModKeybindContent", typeof(RectTransform));
                var contentRT = content.GetComponent<RectTransform>();
                content.transform.SetParent(viewport.transform, false);
                contentRT.anchorMin = new Vector2(0.5f, 1f);
                contentRT.anchorMax = new Vector2(0.5f, 1f);
                contentRT.pivot = new Vector2(0.5f, 1f);
                contentRT.anchoredPosition = Vector2.zero;
                contentRT.sizeDelta = new Vector2(350, 400); // will be resized after injection

                var scrollRect = viewport.AddComponent<ScrollRect>();
                scrollRect.horizontal = false;
                scrollRect.vertical = true;
                scrollRect.movementType = ScrollRect.MovementType.Clamped;
                scrollRect.content = contentRT;
                scrollRect.viewport = vpRT;

                // Move only KeybindSetterItem children into the scroll container
                // (leave Directions headers etc. as fixed children of Controls)
                var toMove = new List<Transform>();
                for (int i = 0; i < controlsTransform.childCount; i++)
                {
                    var child = controlsTransform.GetChild(i);
                    if (child.GetComponent<KeybindSetterItemScript>() != null)
                        toMove.Add(child);
                }

                foreach (var child in toMove)
                    child.SetParent(content.transform, true);

                keybindContainer = content.transform;
                Plugin.Log.LogInfo($"[ModKeybindInjector] created scroll container, moved {toMove.Count} keybind rows");
            }
            else
            {
                // Already set up from a previous injection
                var existing = settingsBit.transform.Find("ModKeybindViewport");
                keybindContainer = existing != null
                    ? existing.Find("ModKeybindContent")
                    : controlsTransform;
            }
            return true;
        }

        private void InjectModKeybindings()
        {
            var bindings = DiscoverBindings();
            if (bindings.Count == 0) return;

            // Find the lowest Y position among existing keybind items
            // to place our rows below them
            var setters = keybindContainer.GetComponentsInChildren<KeybindSetterItemScript>(true);
            float lowestY = 0f;
            float rowSpacing = 35f; // default spacing
            if (setters.Length >= 2)
            {
                // Calculate spacing from first two items
                rowSpacing = Mathf.Abs(setters[0].transform.localPosition.y - setters[1].transform.localPosition.y);
            }
            foreach (var s in setters)
            {
                if (s.transform.localPosition.y < lowestY)
                    lowestY = s.transform.localPosition.y;
            }

            Plugin.Log.LogInfo($"[ModKeybindInjector] lowestY={lowestY} rowSpacing={rowSpacing}");

            // Start placing our rows below the existing ones
            float nextY = lowestY - rowSpacing * 1.5f;

            // Section header
            nextY = CreateHeader("— Mod Keybindings —", nextY, 30);
            nextY -= rowSpacing * 0.5f;

            // Group by mod, create rows
            string currentMod = null;
            foreach (var b in bindings)
            {
                if (b.ModName != currentMod)
                {
                    currentMod = b.ModName;
                    nextY -= rowSpacing * 0.5f;
                    nextY = CreateHeader(currentMod, nextY, 25);
                    nextY -= rowSpacing * 0.5f;
                }
                nextY = CreateBindingRow(b, nextY, rowSpacing);
            }

            DetectConflicts();

            // Resize the content (Controls) rect so ScrollRect knows the full scrollable range
            var contentRT = keybindContainer.GetComponent<RectTransform>();
            if (contentRT != null)
            {
                float totalHeight = Mathf.Abs(nextY) + 20;
                contentRT.sizeDelta = new Vector2(contentRT.sizeDelta.x, totalHeight);
                Plugin.Log.LogInfo($"[ModKeybindInjector] content height set to {totalHeight}");
            }
        }

        private List<BindingInfo> DiscoverBindings()
        {
            var results = new List<BindingInfo>();

            foreach (var kvp in Chainloader.PluginInfos.OrderBy(p => p.Value.Metadata.Name))
            {
                var info = kvp.Value;
                if (info.Instance == null) continue;

                foreach (var entry in info.Instance.Config)
                {
                    if (entry.Value.SettingType != typeof(KeyboardShortcut))
                        continue;

                    results.Add(new BindingInfo
                    {
                        ModName = info.Metadata.Name,
                        Section = entry.Key.Section,
                        Key = entry.Key.Key,
                        Config = (ConfigEntry<KeyboardShortcut>)entry.Value
                    });
                }
            }

            return results;
        }

        private struct BindingInfo
        {
            public string ModName;
            public string Section;
            public string Key;
            public ConfigEntry<KeyboardShortcut> Config;
        }

        // -------------------------------------------------------------------
        //  Row creation
        // -------------------------------------------------------------------

        private float CreateHeader(string text, float y, float height)
        {
            var refText = rowTemplate.GetComponentInChildren<TMP_Text>();

            var go = new GameObject("ModHeader_" + text);
            go.transform.SetParent(keybindContainer, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.font = refText != null ? refText.font : null;
            tmp.fontSize = refText != null ? refText.fontSize : 20f;
            tmp.color = UIStyle.TextSecondary;
            tmp.alignment = TextAlignmentOptions.Center;

            // Match anchors to parent's pivot so anchoredPosition == localPosition
            // (same coordinate space as the plain-Transform keybind rows)
            var parentPivot = keybindContainer.GetComponent<RectTransform>().pivot;
            var rt = tmp.rectTransform;
            rt.anchorMin = parentPivot;
            rt.anchorMax = parentPivot;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(350, height);
            rt.anchoredPosition = new Vector2(0, y);

            return y - height;
        }

        private float CreateBindingRow(BindingInfo info, float y, float spacing)
        {
            var clone = Instantiate(rowTemplate, keybindContainer);
            clone.name = $"ModKeybind_{info.ModName}_{info.Key}";
            clone.transform.localPosition = new Vector3(
                rowTemplate.transform.localPosition.x, y, 0);
            clone.SetActive(true);

            var setter = clone.GetComponent<KeybindSetterItemScript>();
            if (setter == null) return y - spacing;

            var kbDisplay = (TMP_Text)F_keyboardDisplay.GetValue(setter);
            var ctrlDisplay = (TMP_Text)F_controllerDisplay.GetValue(setter);
            Destroy(setter);

            if (kbDisplay != null)
                kbDisplay.text = FormatKeyGlyph(info.Config.Value);
            if (ctrlDisplay != null)
                ctrlDisplay.text = "—";

            // Set the label — the child named "Title"
            var titleTransform = clone.transform.Find("Title");
            if (titleTransform != null)
            {
                // Remove LocalizeStringEvent so localization doesn't overwrite our text
                foreach (var comp in titleTransform.GetComponents<MonoBehaviour>())
                {
                    if (comp.GetType().Name == "LocalizeStringEvent")
                    {
                        DestroyImmediate(comp);
                        break;
                    }
                }

                var titleText = titleTransform.GetComponent<TMP_Text>();
                if (titleText != null)
                    titleText.text = info.Key;
            }

            // Find this clone's own WaitingForInput child
            GameObject rowPopup = null;
            var waitingTransform = clone.transform.Find("WaitingForInput");
            if (waitingTransform != null)
                rowPopup = waitingTransform.gameObject;

            // Wire up the keyboard rebind button, disable controller button
            var buttons = clone.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                // Replace entire onClick to clear persistent (serialized) listeners
                buttons[i].onClick = new Button.ButtonClickedEvent();

                if (i == 0)
                {
                    var row = new BindingRow
                    {
                        ModName = info.ModName,
                        Label = info.Key,
                        Config = info.Config,
                        KeyDisplay = kbDisplay,
                        Root = clone,
                        WaitingPopup = rowPopup
                    };
                    rows.Add(row);

                    var capturedRow = row;
                    buttons[i].onClick.AddListener(() => StartRebind(capturedRow));
                }
                else
                {
                    buttons[i].interactable = false;
                }
            }

            return y - 35;
        }

        // -------------------------------------------------------------------
        //  Glyph formatting
        // -------------------------------------------------------------------

        private static string FormatKeyGlyph(KeyboardShortcut ks)
        {
            if (ks.MainKey == KeyCode.None) return "(none)";

            string keyName = KeyCodeToDisplayName(ks.MainKey);

            try
            {
                var dict = Singleton<ButtonToGlyphDict>.Instance.keyToGlyphDict;
                if (dict.ContainsKey(keyName))
                    return dict[keyName];
            }
            catch { }

            return keyName.ToUpper();
        }

        private static string KeyCodeToDisplayName(KeyCode kc)
        {
            string name = kc.ToString();
            if (name.Length == 1) return name;

            switch (kc)
            {
                case KeyCode.LeftShift:
                case KeyCode.RightShift: return "Shift";
                case KeyCode.Space: return "Space";
                case KeyCode.Escape: return "Escape";
                default: return name;
            }
        }

        // -------------------------------------------------------------------
        //  Rebinding
        // -------------------------------------------------------------------

        private void StartRebind(BindingRow row)
        {
            rebindingRow = row;
            waitingForKey = true;

            if (row.WaitingPopup != null)
                row.WaitingPopup.SetActive(true);
        }

        // Map InputSystem Key to BepInEx KeyCode
        private static readonly Dictionary<Key, KeyCode> KeyMap = new Dictionary<Key, KeyCode>();

        static ModKeybindInjector()
        {
            for (Key k = Key.A; k <= Key.Z; k++)
                KeyMap[k] = KeyCode.A + (k - Key.A);
            for (Key k = Key.Digit0; k <= Key.Digit9; k++)
                KeyMap[k] = KeyCode.Alpha0 + (k - Key.Digit0);
            for (Key k = Key.F1; k <= Key.F12; k++)
                KeyMap[k] = KeyCode.F1 + (k - Key.F1);
            KeyMap[Key.Space] = KeyCode.Space;
            KeyMap[Key.Enter] = KeyCode.Return;
            KeyMap[Key.Tab] = KeyCode.Tab;
            KeyMap[Key.Backspace] = KeyCode.Backspace;
            KeyMap[Key.Escape] = KeyCode.Escape;
            KeyMap[Key.LeftShift] = KeyCode.LeftShift;
            KeyMap[Key.RightShift] = KeyCode.RightShift;
            KeyMap[Key.LeftCtrl] = KeyCode.LeftControl;
            KeyMap[Key.RightCtrl] = KeyCode.RightControl;
            KeyMap[Key.LeftAlt] = KeyCode.LeftAlt;
            KeyMap[Key.RightAlt] = KeyCode.RightAlt;
            KeyMap[Key.UpArrow] = KeyCode.UpArrow;
            KeyMap[Key.DownArrow] = KeyCode.DownArrow;
            KeyMap[Key.LeftArrow] = KeyCode.LeftArrow;
            KeyMap[Key.RightArrow] = KeyCode.RightArrow;
            KeyMap[Key.Delete] = KeyCode.Delete;
            KeyMap[Key.Home] = KeyCode.Home;
            KeyMap[Key.End] = KeyCode.End;
            KeyMap[Key.PageUp] = KeyCode.PageUp;
            KeyMap[Key.PageDown] = KeyCode.PageDown;
            KeyMap[Key.Insert] = KeyCode.Insert;
            KeyMap[Key.Comma] = KeyCode.Comma;
            KeyMap[Key.Period] = KeyCode.Period;
            KeyMap[Key.Slash] = KeyCode.Slash;
            KeyMap[Key.Backslash] = KeyCode.Backslash;
            KeyMap[Key.Semicolon] = KeyCode.Semicolon;
            KeyMap[Key.Quote] = KeyCode.Quote;
            KeyMap[Key.LeftBracket] = KeyCode.LeftBracket;
            KeyMap[Key.RightBracket] = KeyCode.RightBracket;
            KeyMap[Key.Minus] = KeyCode.Minus;
            KeyMap[Key.Equals] = KeyCode.Equals;
            KeyMap[Key.Backquote] = KeyCode.BackQuote;
        }

        private void PollRebindInput()
        {
            // Escape cancels (check keyboard)
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                CancelRebind();
                return;
            }

            // Collect modifiers from keyboard
            var modifiers = new List<KeyCode>();
            if (keyboard != null)
            {
                if (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed)
                    modifiers.Add(KeyCode.LeftControl);
                if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
                    modifiers.Add(KeyCode.LeftShift);
                if (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed)
                    modifiers.Add(KeyCode.LeftAlt);
            }

            // Check keyboard
            if (keyboard != null)
            {
                foreach (var key in keyboard.allKeys)
                {
                    if (!key.wasPressedThisFrame) continue;
                    if (key.keyCode == Key.None) continue;
                    if (!KeyMap.TryGetValue(key.keyCode, out var keyCode)) continue;
                    if (keyCode == KeyCode.Escape) continue;

                    // Don't bind a modifier alone if it's already in the modifier list
                    if (modifiers.Contains(keyCode)) continue;

                    ApplyRebind(keyCode, modifiers.ToArray());
                    return;
                }
            }

            // Check mouse buttons
            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame)   { ApplyRebind(KeyCode.Mouse0, modifiers.ToArray()); return; }
                if (mouse.rightButton.wasPressedThisFrame)  { ApplyRebind(KeyCode.Mouse1, modifiers.ToArray()); return; }
                if (mouse.middleButton.wasPressedThisFrame) { ApplyRebind(KeyCode.Mouse2, modifiers.ToArray()); return; }
                if (mouse.forwardButton.wasPressedThisFrame){ ApplyRebind(KeyCode.Mouse3, modifiers.ToArray()); return; }
                if (mouse.backButton.wasPressedThisFrame)   { ApplyRebind(KeyCode.Mouse4, modifiers.ToArray()); return; }
            }

            // Check gamepad buttons
            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                var gpButtons = new (ButtonControl btn, KeyCode kc)[]
                {
                    (gamepad.buttonSouth, KeyCode.JoystickButton0),
                    (gamepad.buttonEast,  KeyCode.JoystickButton1),
                    (gamepad.buttonWest,  KeyCode.JoystickButton2),
                    (gamepad.buttonNorth, KeyCode.JoystickButton3),
                    (gamepad.leftShoulder,  KeyCode.JoystickButton4),
                    (gamepad.rightShoulder, KeyCode.JoystickButton5),
                    (gamepad.selectButton,  KeyCode.JoystickButton6),
                    (gamepad.startButton,   KeyCode.JoystickButton7),
                    (gamepad.leftStickButton,  KeyCode.JoystickButton8),
                    (gamepad.rightStickButton, KeyCode.JoystickButton9),
                    (gamepad.leftTrigger,  KeyCode.JoystickButton10),
                    (gamepad.rightTrigger, KeyCode.JoystickButton11),
                };

                foreach (var (btn, kc) in gpButtons)
                {
                    if (btn.wasPressedThisFrame)
                    {
                        ApplyRebind(kc, System.Array.Empty<KeyCode>());
                        return;
                    }
                }
            }
        }

        private void ApplyRebind(KeyCode key, KeyCode[] modifiers)
        {
            rebindingRow.Config.Value = new KeyboardShortcut(key, modifiers);
            rebindingRow.KeyDisplay.text = FormatKeyGlyph(rebindingRow.Config.Value);

            Plugin.Log.LogInfo($"Rebound {rebindingRow.ModName}/{rebindingRow.Label} -> {key}");

            FinishRebind();
            DetectConflicts();
        }

        private void CancelRebind()
        {
            FinishRebind();
        }

        private void FinishRebind()
        {
            if (rebindingRow?.WaitingPopup != null)
                rebindingRow.WaitingPopup.SetActive(false);

            waitingForKey = false;
            rebindingRow = null;
        }

        // -------------------------------------------------------------------
        //  Conflict detection
        // -------------------------------------------------------------------

        private void DetectConflicts()
        {
            var keyGroups = new Dictionary<string, List<BindingRow>>();
            foreach (var row in rows)
            {
                var ks = row.Config.Value;
                if (ks.MainKey == KeyCode.None) continue;

                string sig = ks.MainKey.ToString();
                if (!keyGroups.ContainsKey(sig))
                    keyGroups[sig] = new List<BindingRow>();
                keyGroups[sig].Add(row);
            }

            foreach (var row in rows)
            {
                if (row.KeyDisplay == null) continue;
                var ks = row.Config.Value;
                if (ks.MainKey == KeyCode.None)
                {
                    row.KeyDisplay.color = UIStyle.TextPrimary;
                    continue;
                }

                string sig = ks.MainKey.ToString();
                bool conflict = keyGroups.TryGetValue(sig, out var group) && group.Count > 1;
                row.KeyDisplay.color = conflict ? UIStyle.Warning : UIStyle.TextPrimary;
            }
        }
    }
}
