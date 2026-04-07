using System.Collections.Generic;

namespace IGTAPReplay
{
    /// <summary>
    /// Bidirectional lookup between internal InputSystem paths (e.g. "&lt;Keyboard&gt;/space")
    /// and short display names (e.g. "Space") for human-readable serialization.
    /// Uses the same display names as the game's keybinding UI.
    /// </summary>
    public static class KeyNames
    {
        // Short name -> InputSystem path
        private static readonly Dictionary<string, string> toPath = new Dictionary<string, string>(
            System.StringComparer.OrdinalIgnoreCase);

        // InputSystem path -> short name
        private static readonly Dictionary<string, string> toName = new Dictionary<string, string>();

        static KeyNames()
        {
            // Keyboard - letters
            for (char c = 'a'; c <= 'z'; c++)
                Add($"{char.ToUpper(c)}", $"<Keyboard>/{c}");

            // Keyboard - digits
            for (int i = 0; i <= 9; i++)
                Add($"{i}", $"<Keyboard>/{i}");

            // Keyboard - special
            Add("Space",      "<Keyboard>/space");
            Add("LShift",     "<Keyboard>/leftShift");
            Add("RShift",     "<Keyboard>/rightShift");
            Add("LCtrl",      "<Keyboard>/leftCtrl");
            Add("RCtrl",      "<Keyboard>/rightCtrl");
            Add("LAlt",       "<Keyboard>/leftAlt");
            Add("RAlt",       "<Keyboard>/rightAlt");
            Add("Enter",      "<Keyboard>/enter");
            Add("Esc",        "<Keyboard>/escape");
            Add("Tab",        "<Keyboard>/tab");
            Add("Backspace",  "<Keyboard>/backspace");
            Add("Left",       "<Keyboard>/leftArrow");
            Add("Right",      "<Keyboard>/rightArrow");
            Add("Up",         "<Keyboard>/upArrow");
            Add("Down",       "<Keyboard>/downArrow");
            Add("Comma",      "<Keyboard>/comma");
            Add("Period",     "<Keyboard>/period");
            Add("Slash",      "<Keyboard>/slash");
            Add("Backslash",  "<Keyboard>/backslash");
            Add("Semicolon",  "<Keyboard>/semicolon");
            Add("Quote",      "<Keyboard>/quote");
            Add("LBracket",   "<Keyboard>/leftBracket");
            Add("RBracket",   "<Keyboard>/rightBracket");
            Add("Minus",      "<Keyboard>/minus");
            Add("Equals",     "<Keyboard>/equals");
            Add("Backquote",  "<Keyboard>/backquote");
            Add("Delete",     "<Keyboard>/delete");
            Add("Insert",     "<Keyboard>/insert");
            Add("Home",       "<Keyboard>/home");
            Add("End",        "<Keyboard>/end");
            Add("PageUp",     "<Keyboard>/pageUp");
            Add("PageDown",   "<Keyboard>/pageDown");
            Add("CapsLock",   "<Keyboard>/capsLock");

            // F-keys
            for (int i = 1; i <= 12; i++)
                Add($"F{i}", $"<Keyboard>/f{i}");

            // Mouse
            Add("LMB",   "<Mouse>/leftButton");
            Add("RMB",   "<Mouse>/rightButton");
            Add("MMB",   "<Mouse>/middleButton");
            Add("Mouse4", "<Mouse>/forwardButton");
            Add("Mouse5", "<Mouse>/backButton");

            // Gamepad
            Add("GP-A",      "<Gamepad>/buttonSouth");
            Add("GP-B",      "<Gamepad>/buttonEast");
            Add("GP-X",      "<Gamepad>/buttonWest");
            Add("GP-Y",      "<Gamepad>/buttonNorth");
            Add("GP-LB",     "<Gamepad>/leftShoulder");
            Add("GP-RB",     "<Gamepad>/rightShoulder");
            Add("GP-LT",     "<Gamepad>/leftTrigger");
            Add("GP-RT",     "<Gamepad>/rightTrigger");
            Add("GP-Start",  "<Gamepad>/start");
            Add("GP-Select", "<Gamepad>/select");
            Add("GP-DUp",    "<Gamepad>/dpad/up");
            Add("GP-DDown",  "<Gamepad>/dpad/down");
            Add("GP-DLeft",  "<Gamepad>/dpad/left");
            Add("GP-DRight", "<Gamepad>/dpad/right");
            Add("GP-LS",     "<Gamepad>/leftStickButton");
            Add("GP-RS",     "<Gamepad>/rightStickButton");
        }

        private static void Add(string shortName, string path)
        {
            toPath[shortName] = path;
            toName[path] = shortName;
        }

        /// <summary>
        /// Convert an InputSystem path to a short display name.
        /// Returns the path unchanged if no mapping exists.
        /// </summary>
        public static string ToShortName(string path)
        {
            return toName.TryGetValue(path, out string name) ? name : path;
        }

        /// <summary>
        /// Convert a short display name (or raw InputSystem path) to an InputSystem path.
        /// Accepts both "Space" and "&lt;Keyboard&gt;/space".
        /// </summary>
        public static string ToPath(string nameOrPath)
        {
            // Already a path?
            if (nameOrPath.StartsWith("<"))
                return nameOrPath;
            return toPath.TryGetValue(nameOrPath, out string path) ? path : nameOrPath;
        }

        /// <summary>
        /// Get an abbreviated display name for the timeline UI.
        /// </summary>
        public static string ToTimelineLabel(string path)
        {
            return ToShortName(path);
        }
    }
}
