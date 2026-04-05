using BepInEx.Configuration;
using UnityEngine;

namespace IGTAPMod
{
    /// <summary>
    /// Reusable IMGUI widgets for debug menu sections.
    /// Use from any mod's DebugMenuAPI.RegisterSection callback.
    /// </summary>
    public static class MenuWidgets
    {
        /// <summary>
        /// Integer field with +/- buttons and an "Inf" toggle.
        /// </summary>
        public static int IntFieldInf(string label, int value, ConfigEntry<bool> infToggle)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(85));
            if (GUILayout.Button("-", GUILayout.Width(22)) && value > 0) value--;
            GUILayout.Label(value.ToString(), GUILayout.Width(25));
            if (GUILayout.Button("+", GUILayout.Width(22))) value++;
            infToggle.Value = GUILayout.Toggle(infToggle.Value, "Inf", GUILayout.Width(40));
            GUILayout.EndHorizontal();
            return value;
        }

        /// <summary>
        /// Integer field with +/- buttons (no infinite toggle).
        /// </summary>
        public static int IntField(string label, int value, int min = 0)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(85));
            if (GUILayout.Button("-", GUILayout.Width(22)) && value > min) value--;
            GUILayout.Label(value.ToString(), GUILayout.Width(25));
            if (GUILayout.Button("+", GUILayout.Width(22))) value++;
            GUILayout.EndHorizontal();
            return value;
        }

        /// <summary>
        /// Float field with a text input.
        /// </summary>
        public static float FloatField(string label, float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(100));
            string text = GUILayout.TextField(value.ToString("F1"), GUILayout.Width(80));
            if (float.TryParse(text, out float parsed) && parsed != value)
                value = parsed;
            GUILayout.EndHorizontal();
            return value;
        }
    }
}
