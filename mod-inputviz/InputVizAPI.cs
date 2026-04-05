using System;
using System.Collections.Generic;

namespace IGTAPInputViz
{
    /// <summary>
    /// Public API for other mods to register actions and status indicators.
    ///
    /// Actions (input buttons):
    ///   InputVizAPI.RegisterInputAction("Jump", "Jump");
    ///   InputVizAPI.RegisterAction("Noclip", "F10", () => MyMod.NoclipActive);
    ///
    /// Status indicators (state readouts):
    ///   InputVizAPI.RegisterStatus("Ground", () => isGrounded);
    ///   InputVizAPI.RegisterStatus("Dash", () => dashesLeft > 0, () => $"{dashesLeft}/{maxDashes}");
    /// </summary>
    public static class InputVizAPI
    {
        internal static readonly List<ActionEntry> Actions = new List<ActionEntry>();
        internal static readonly List<StatusEntry> Statuses = new List<StatusEntry>();

        // --- Actions (input buttons) ---

        public static void RegisterInputAction(string label, string inputActionName)
        {
            if (Actions.Exists(a => a.Label == label)) return;
            Actions.Add(new ActionEntry { Label = label, InputActionName = inputActionName });
        }

        public static void RegisterAction(string label, string bindingDisplay, Func<bool> isPressed)
        {
            if (Actions.Exists(a => a.Label == label)) return;
            Actions.Add(new ActionEntry { Label = label, CustomBindDisplay = bindingDisplay, CustomIsPressed = isPressed });
        }

        public static void UnregisterAction(string label)
        {
            Actions.RemoveAll(a => a.Label == label);
        }

        // --- Status indicators ---

        /// <summary>
        /// Register a status indicator. Shows as a colored dot + label.
        /// </summary>
        /// <param name="label">Display name (e.g. "Ground", "Dash Ready")</param>
        /// <param name="isActive">Returns true when the state is active/available</param>
        /// <param name="detail">Optional: returns detail text (e.g. "2/3"). Null for simple on/off.</param>
        public static void RegisterStatus(string label, Func<bool> isActive, Func<string> detail = null)
        {
            if (Statuses.Exists(s => s.Label == label)) return;
            Statuses.Add(new StatusEntry { Label = label, IsActive = isActive, Detail = detail });
        }

        public static void UnregisterStatus(string label)
        {
            Statuses.RemoveAll(s => s.Label == label);
        }

        internal class ActionEntry
        {
            public string Label;
            public string InputActionName;
            public string CustomBindDisplay;
            public Func<bool> CustomIsPressed;
            public bool IsInputAction => InputActionName != null;
            public string CachedBindDisplay = "?";
        }

        internal class StatusEntry
        {
            public string Label;
            public Func<bool> IsActive;
            public Func<string> Detail;
        }
    }
}
