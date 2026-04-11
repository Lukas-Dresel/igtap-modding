using IGTAPMod;
using UnityEngine;

namespace IGTAPFreeplay
{
    public static class FreeplayMenu
    {
        public static void Register()
        {
            // HUD items (unchanged -- these are already data-only)
            DebugMenuAPI.RegisterHudItem("freeplay.dash", 10, () =>
            {
                var p = GameState.Player;
                if (p == null || !p.dashUnlocked) return null;
                return $"Dash: {GameState.AirDashesLeft}/{p.maxAirDashes}";
            });
            DebugMenuAPI.RegisterHudItem("freeplay.jump", 11, () =>
            {
                var p = GameState.Player;
                if (p == null || !p.doubleJumpUnlocked) return null;
                return $"Jump: {GameState.AirJumpsLeft}/{p.maxAirJumps}";
            });
            DebugMenuAPI.RegisterHudItem("freeplay.wall", 12, () =>
            {
                var p = GameState.Player;
                if (p == null || !p.wallJumpUnlocked) return null;
                return $"Wall: {GameState.WallJumpsLeft}/{p.maxWallJumps}";
            });
            DebugMenuAPI.RegisterHudItem("freeplay.state", 13, () =>
            {
                if (GameState.Player == null) return null;
                return $"[{(GameState.IsGrounded ? "Ground" : GameState.IsOnWall ? "Wall" : "Air")}]";
            });
            DebugMenuAPI.RegisterHudItem("freeplay.god", 20, () =>
                Plugin.GodMode.Value ? "[GOD]" : null);
            DebugMenuAPI.RegisterHudItem("freeplay.noclip", 21, () =>
                Plugin.NoclipActive ? "[NOCLIP]" : null);

            // Menu sections -- now using widget API
            DebugMenuAPI.RegisterSection("Unlocks", 10, BuildUnlocks);
            DebugMenuAPI.RegisterSection("Max Counts", 20, BuildCounts);
            DebugMenuAPI.RegisterSection("Speed", 30, BuildSpeed);
            DebugMenuAPI.RegisterSection("Currency", 40, BuildCurrency);
        }

        private static void BuildUnlocks(WidgetPanel panel)
        {
            panel.AddLabel(() => GameState.Player == null ? "(no player found)" : null,
                UIStyle.FontSizeSmall, UIStyle.TextMuted);

            panel.AddToggle("Dash",
                () => GameState.Player?.dashUnlocked ?? false,
                v => { if (GameState.Player != null) GameState.Player.dashUnlocked = v; });

            panel.AddToggle("Wall Jump",
                () => GameState.Player?.wallJumpUnlocked ?? false,
                v => { if (GameState.Player != null) GameState.Player.wallJumpUnlocked = v; });

            panel.AddToggle("Double Jump",
                () => GameState.Player?.doubleJumpUnlocked ?? false,
                v => { if (GameState.Player != null) GameState.Player.doubleJumpUnlocked = v; });

            panel.AddToggle("Block Swap",
                () => GameState.Player?.blockSwapUnlocked ?? false,
                v => { if (GameState.Player != null) GameState.Player.blockSwapUnlocked = v; });

            panel.AddToggle("God Mode",
                () => Plugin.GodMode.Value,
                v => Plugin.GodMode.Value = v);
        }

        private static void BuildCounts(WidgetPanel panel)
        {
            panel.AddIntField("Air Dashes",
                () => GameState.Player?.maxAirDashes ?? 0,
                v => { if (GameState.Player != null) GameState.Player.maxAirDashes = v; },
                infToggle: Plugin.InfiniteDashes);

            panel.AddIntField("Air Jumps",
                () => GameState.Player?.maxAirJumps ?? 0,
                v => { if (GameState.Player != null) GameState.Player.maxAirJumps = v; },
                infToggle: Plugin.InfiniteJumps);

            panel.AddIntField("Wall Jumps",
                () => GameState.Player?.maxWallJumps ?? 0,
                v => { if (GameState.Player != null) GameState.Player.maxWallJumps = v; },
                infToggle: Plugin.InfiniteWallJumps);
        }

        private static void BuildSpeed(WidgetPanel panel)
        {
            panel.AddFloatField("Run Speed",
                () => GameState.Player?.runSpeed ?? 0f,
                v => { if (GameState.Player != null) GameState.Player.runSpeed = v; });

            panel.AddFloatField("Jump Force",
                () => GameState.Player?.jumpForce ?? 0f,
                v => { if (GameState.Player != null) GameState.Player.jumpForce = v; });

            panel.AddFloatField("Dash Speed",
                () => GameState.DashSpeed,
                v => { if (GameState.Player != null) GameState.F_dashSpeed.SetValue(GameState.Player, v); });

            panel.AddFloatField("Gravity",
                () => GameState.Player?.gravity ?? 0f,
                v => { if (GameState.Player != null) GameState.Player.gravity = v; });
        }

        private static void BuildCurrency(WidgetPanel panel)
        {
            panel.AddLabel(() =>
            {
                double cash = globalStats.currencyLookup[globalStats.Currencies.Cash];
                return $"Cash: {cash:N0}";
            }, UIStyle.FontSizeBody, UIStyle.CurrencyCash);

            panel.AddButtonRow(
                ("+1K", () => globalStats.currencyLookup[globalStats.Currencies.Cash] += 1000),
                ("+1M", () => globalStats.currencyLookup[globalStats.Currencies.Cash] += 1000000),
                ("+1B", () => globalStats.currencyLookup[globalStats.Currencies.Cash] += 1000000000)
            );
        }
    }
}
