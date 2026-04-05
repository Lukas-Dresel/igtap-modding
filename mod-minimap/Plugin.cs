using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using IGTAPMod;
using UnityEngine;

namespace IGTAPMinimap
{
    public enum ViewMode
    {
        FullWorld,
        FollowPlayer,
        CurrentCourse
    }

    public enum ScreenCorner
    {
        BottomRight,
        BottomLeft,
        TopRight,
        TopLeft
    }

    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.igtapmod.plugin")]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.igtapmod.minimap";
        public const string PluginName = "IGTAP Minimap";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<KeyboardShortcut> CycleViewModeKey;
        internal static ConfigEntry<ViewMode> CurrentViewMode;
        internal static ConfigEntry<int> MinimapSize;
        internal static ConfigEntry<float> MinimapOpacity;
        internal static ConfigEntry<ScreenCorner> Position;
        internal static ConfigEntry<float> FollowZoom;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("General", "Enabled", true,
                "Show the minimap overlay");
            ToggleKey = Config.Bind("General", "ToggleKey",
                new KeyboardShortcut(KeyCode.F6),
                "Press to toggle minimap on/off");
            CycleViewModeKey = Config.Bind("General", "CycleViewModeKey",
                new KeyboardShortcut(KeyCode.F5),
                "Press to cycle between view modes");
            CurrentViewMode = Config.Bind("General", "ViewMode", ViewMode.FullWorld,
                "Current view mode: FullWorld, FollowPlayer, or CurrentCourse");

            MinimapSize = Config.Bind("Appearance", "Size", 200,
                "Minimap size in pixels (width and height)");
            MinimapOpacity = Config.Bind("Appearance", "Opacity", 0.8f,
                "Minimap background opacity (0-1)");
            Position = Config.Bind("Appearance", "Position", ScreenCorner.BottomRight,
                "Which corner of the screen to place the minimap");
            FollowZoom = Config.Bind("Appearance", "FollowZoom", 30.0f,
                "World units per pixel in Follow Player mode (higher = more zoomed out)");

            gameObject.AddComponent<MinimapOverlay>();

            DebugMenuAPI.RegisterHudItem("minimap.mode", 30, () =>
                Enabled.Value ? $"[Map: {CurrentViewMode.Value}]" : null);

            DebugMenuAPI.RegisterSection("Minimap", 40, DrawMenuSection);

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded!");
        }

        private void OnDestroy()
        {
            DebugMenuAPI.UnregisterHudItem("minimap.mode");
            DebugMenuAPI.UnregisterSection("Minimap");
        }

        private static void DrawMenuSection()
        {
            Enabled.Value = GUILayout.Toggle(Enabled.Value, "Enabled");

            GUILayout.BeginHorizontal();
            GUILayout.Label("View Mode", GUILayout.Width(85));
            if (GUILayout.Button(CurrentViewMode.Value.ToString()))
                CycleViewMode();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Position", GUILayout.Width(85));
            if (GUILayout.Button(Position.Value.ToString()))
            {
                var vals = (ScreenCorner[])System.Enum.GetValues(typeof(ScreenCorner));
                int idx = System.Array.IndexOf(vals, Position.Value);
                Position.Value = vals[(idx + 1) % vals.Length];
            }
            GUILayout.EndHorizontal();

            MinimapSize.Value = MenuWidgets.IntField("Size", MinimapSize.Value, 50);

            float newOpacity = MenuWidgets.FloatField("Opacity", MinimapOpacity.Value);
            MinimapOpacity.Value = Mathf.Clamp01(newOpacity);

            float newZoom = MenuWidgets.FloatField("Follow Zoom", FollowZoom.Value);
            FollowZoom.Value = Mathf.Max(0.5f, newZoom);

            if (GUILayout.Button("Rescan Tilemaps"))
                MinimapData.MarkDirty();
        }

        internal static void CycleViewMode()
        {
            var vals = (ViewMode[])System.Enum.GetValues(typeof(ViewMode));
            int idx = System.Array.IndexOf(vals, CurrentViewMode.Value);
            CurrentViewMode.Value = vals[(idx + 1) % vals.Length];
        }
    }
}
