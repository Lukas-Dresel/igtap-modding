using IGTAPMod;
using UnityEngine;

namespace IGTAPFreeplay
{
    public static class FreeplayMenu
    {
        public static void Register()
        {
            // HUD items
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

            // Menu sections
            DebugMenuAPI.RegisterSection("Unlocks", 10, DrawUnlocks);
            DebugMenuAPI.RegisterSection("Max Counts", 20, DrawCounts);
            DebugMenuAPI.RegisterSection("Speed", 30, DrawSpeed);
            DebugMenuAPI.RegisterSection("Currency", 40, DrawCurrency);
        }

        private static void DrawUnlocks()
        {
            var p = GameState.Player;
            if (p == null) { GUILayout.Label("(no player found)"); return; }

            p.dashUnlocked = GUILayout.Toggle(p.dashUnlocked, "Dash");
            p.wallJumpUnlocked = GUILayout.Toggle(p.wallJumpUnlocked, "Wall Jump");
            p.doubleJumpUnlocked = GUILayout.Toggle(p.doubleJumpUnlocked, "Double Jump");
            p.blockSwapUnlocked = GUILayout.Toggle(p.blockSwapUnlocked, "Block Swap");
            Plugin.GodMode.Value = GUILayout.Toggle(Plugin.GodMode.Value, "God Mode");
        }

        private static void DrawCounts()
        {
            var p = GameState.Player;
            if (p == null) return;

            p.maxAirDashes = MenuWidgets.IntFieldInf("Air Dashes", p.maxAirDashes, Plugin.InfiniteDashes);
            p.maxAirJumps = MenuWidgets.IntFieldInf("Air Jumps", p.maxAirJumps, Plugin.InfiniteJumps);
            p.maxWallJumps = MenuWidgets.IntFieldInf("Wall Jumps", p.maxWallJumps, Plugin.InfiniteWallJumps);
        }

        private static void DrawSpeed()
        {
            var p = GameState.Player;
            if (p == null) return;

            p.runSpeed = MenuWidgets.FloatField("Run Speed", p.runSpeed);
            p.jumpForce = MenuWidgets.FloatField("Jump Force", p.jumpForce);
            float ds = GameState.DashSpeed;
            float newDs = MenuWidgets.FloatField("Dash Speed", ds);
            if (newDs != ds) GameState.F_dashSpeed.SetValue(p, newDs);
            p.gravity = MenuWidgets.FloatField("Gravity", p.gravity);
        }

        private static void DrawCurrency()
        {
            double cash = globalStats.currencyLookup[globalStats.Currencies.Cash];
            GUILayout.Label($"Cash: {cash:N0}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+1K")) globalStats.currencyLookup[globalStats.Currencies.Cash] += 1000;
            if (GUILayout.Button("+1M")) globalStats.currencyLookup[globalStats.Currencies.Cash] += 1000000;
            if (GUILayout.Button("+1B")) globalStats.currencyLookup[globalStats.Currencies.Cash] += 1000000000;
            GUILayout.EndHorizontal();
        }
    }
}
