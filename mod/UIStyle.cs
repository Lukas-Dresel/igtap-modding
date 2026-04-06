using UnityEngine;

namespace IGTAPMod
{
    /// <summary>
    /// Color palette and style constants matching the game's visual style.
    /// Use with GameUI methods to create UIs that feel native.
    /// </summary>
    public static class UIStyle
    {
        // --- Panel / Background ---
        public static readonly Color PanelBackground = new Color(0.08f, 0.08f, 0.12f, 0.92f);
        public static readonly Color PanelBackgroundLight = new Color(0.14f, 0.14f, 0.20f, 0.90f);
        public static readonly Color PanelBorder = new Color(0.3f, 0.3f, 0.4f, 0.8f);

        // --- Buttons ---
        public static readonly Color ButtonNormal = new Color(0.18f, 0.18f, 0.26f, 1f);
        public static readonly Color ButtonHighlight = new Color(0.28f, 0.28f, 0.40f, 1f);
        public static readonly Color ButtonPressed = new Color(0.12f, 0.12f, 0.18f, 1f);
        public static readonly Color ButtonDisabled = new Color(0.15f, 0.15f, 0.15f, 0.6f);

        // --- Text ---
        public static readonly Color TextPrimary = Color.white;
        public static readonly Color TextSecondary = new Color(0.75f, 0.75f, 0.80f, 1f);
        public static readonly Color TextMuted = new Color(0.5f, 0.5f, 0.5f, 1f);

        // --- Game currency colors (from CashDisplay) ---
        public static readonly Color CurrencyCash = new Color(1f, 0.416f, 0f);
        public static readonly Color CurrencyGreenPower = new Color(0f, 0.868f, 0.366f);
        public static readonly Color CurrencyAtomicPower = new Color(0.59f, 0.33f, 0.94f);

        // --- Accent / Highlight ---
        public static readonly Color Accent = new Color(0.3f, 0.6f, 1f, 1f);
        public static readonly Color AccentWarm = new Color(1f, 0.5f, 0.2f, 1f);
        public static readonly Color Success = new Color(0.2f, 0.85f, 0.4f, 1f);
        public static readonly Color Warning = new Color(1f, 0.75f, 0.2f, 1f);
        public static readonly Color Error = Color.red;

        // --- Slider ---
        public static readonly Color SliderBackground = new Color(0.12f, 0.12f, 0.18f, 1f);
        public static readonly Color SliderFill = new Color(0.3f, 0.6f, 1f, 0.8f);
        public static readonly Color SliderHandle = Color.white;

        // --- Font sizes ---
        public const float FontSizeTitle = 32f;
        public const float FontSizeHeader = 24f;
        public const float FontSizeBody = 18f;
        public const float FontSizeSmall = 14f;
    }
}
