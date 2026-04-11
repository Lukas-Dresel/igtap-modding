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

        // --- Buttons (from game: orange normal, green hover, red press) ---
        public static readonly Color ButtonNormal = new Color(0.996f, 0.416f, 0f, 1f);
        public static readonly Color ButtonHighlight = new Color(0f, 1f, 0.008f, 1f);
        public static readonly Color ButtonPressed = new Color(1f, 0f, 0f, 1f);
        public static readonly Color ButtonDisabled = new Color(0.784f, 0.784f, 0.784f, 0.5f);

        // --- Controls: toggle/dropdown (from game: white normal, green hover) ---
        public static readonly Color ControlNormal = Color.white;
        public static readonly Color ControlHighlight = new Color(0f, 1f, 0.008f, 1f);
        public static readonly Color ControlPressed = new Color(0.784f, 0.784f, 0.784f, 1f);

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

        // --- Tabs ---
        public static readonly Color TabActive = Accent;
        public static readonly Color TabInactive = new Color(0.14f, 0.14f, 0.20f, 1f);
        public static readonly Color TabHover = new Color(0.22f, 0.22f, 0.32f, 1f);

        // --- Separator ---
        public static readonly Color Separator = new Color(0.25f, 0.25f, 0.35f, 0.6f);

        // --- Input field ---
        public static readonly Color InputFieldBg = new Color(0.06f, 0.06f, 0.10f, 1f);
        public static readonly Color InputFieldBorder = new Color(0.2f, 0.2f, 0.3f, 0.8f);

        // --- HUD ---
        public static readonly Color HudBackground = new Color(0.05f, 0.05f, 0.08f, 0.75f);

        // --- Row hover ---
        public static readonly Color RowHover = new Color(0.16f, 0.16f, 0.24f, 0.5f);

        // --- Font sizes ---
        public const float FontSizeTitle = 32f;
        public const float FontSizeHeader = 24f;
        public const float FontSizeBody = 18f;
        public const float FontSizeSmall = 14f;
        public const float FontSizeCaption = 11f;
    }
}
