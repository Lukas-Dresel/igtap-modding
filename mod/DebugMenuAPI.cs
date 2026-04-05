using System;
using System.Collections.Generic;

namespace IGTAPMod
{
    /// <summary>
    /// Public API for mods to register sections in the debug menu (F8) and HUD items.
    ///
    /// Usage from another BepInEx plugin:
    ///   DebugMenuAPI.RegisterSection("My Section", 10, () => {
    ///       GUILayout.Toggle(ref myBool, "My Toggle");
    ///   });
    ///   DebugMenuAPI.RegisterHudItem("MyMod", 10, () => myActive ? "[ACTIVE]" : null);
    /// </summary>
    public static class DebugMenuAPI
    {
        internal static readonly List<MenuSection> Sections = new List<MenuSection>();
        internal static readonly List<HudItem> HudItems = new List<HudItem>();

        /// <summary>
        /// Register a section in the debug menu window.
        /// </summary>
        /// <param name="title">Section header text</param>
        /// <param name="order">Sort order (lower = higher in menu)</param>
        /// <param name="drawCallback">Called inside the menu's scroll view to draw your controls</param>
        public static void RegisterSection(string title, int order, Action drawCallback)
        {
            Sections.RemoveAll(s => s.Title == title);
            Sections.Add(new MenuSection { Title = title, Order = order, Draw = drawCallback });
            Sections.Sort((a, b) => a.Order.CompareTo(b.Order));
        }

        /// <summary>
        /// Remove a previously registered section.
        /// </summary>
        public static void UnregisterSection(string title)
        {
            Sections.RemoveAll(s => s.Title == title);
        }

        /// <summary>
        /// Register a HUD item shown in the top-left overlay.
        /// </summary>
        /// <param name="id">Unique identifier</param>
        /// <param name="order">Sort order (lower = further left)</param>
        /// <param name="getText">Returns text to display, or null to hide</param>
        public static void RegisterHudItem(string id, int order, Func<string> getText)
        {
            HudItems.RemoveAll(h => h.Id == id);
            HudItems.Add(new HudItem { Id = id, Order = order, GetText = getText });
            HudItems.Sort((a, b) => a.Order.CompareTo(b.Order));
        }

        /// <summary>
        /// Remove a previously registered HUD item.
        /// </summary>
        public static void UnregisterHudItem(string id)
        {
            HudItems.RemoveAll(h => h.Id == id);
        }

        internal class MenuSection
        {
            public string Title;
            public int Order;
            public Action Draw;
        }

        internal class HudItem
        {
            public string Id;
            public int Order;
            public Func<string> GetText;
        }
    }
}
