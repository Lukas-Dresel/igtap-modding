using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace IGTAPMod
{
    /// <summary>
    /// Cached accessors for Movement private fields and player lookup.
    /// Use from any mod: GameState.Player, GameState.IsGrounded, etc.
    /// </summary>
    public static class GameState
    {
        // Cached field accessors for Movement private fields
        public static readonly FieldInfo F_onGround = AccessTools.Field(typeof(Movement), "onGround");
        public static readonly FieldInfo F_OnWall = AccessTools.Field(typeof(Movement), "OnWall");
        public static readonly FieldInfo F_airDashesLeft = AccessTools.Field(typeof(Movement), "airDashesLeft");
        public static readonly FieldInfo F_airJumpsLeft = AccessTools.Field(typeof(Movement), "airJumpsLeft");
        public static readonly FieldInfo F_wallJumpsLeft = AccessTools.Field(typeof(Movement), "wallJumpsLeft");
        public static readonly FieldInfo F_dashSpeed = AccessTools.Field(typeof(Movement), "dashSpeed");
        public static readonly FieldInfo F_dashBuffer = AccessTools.Field(typeof(Movement), "dashBuffer");

        private static Movement cachedPlayer;

        /// <summary>
        /// The current Movement (player) instance. Cached, auto-refreshes if destroyed.
        /// </summary>
        public static Movement Player
        {
            get
            {
                if (cachedPlayer == null)
                    cachedPlayer = Object.FindAnyObjectByType<Movement>();
                return cachedPlayer;
            }
        }

        // Convenience read-only accessors (null-safe)
        public static bool IsGrounded => Player != null && (bool)F_onGround.GetValue(Player);
        public static bool IsOnWall => Player != null && (bool)F_OnWall.GetValue(Player);
        public static int AirDashesLeft => Player != null ? (int)F_airDashesLeft.GetValue(Player) : 0;
        public static int AirJumpsLeft => Player != null ? (int)F_airJumpsLeft.GetValue(Player) : 0;
        public static int WallJumpsLeft => Player != null ? (int)F_wallJumpsLeft.GetValue(Player) : 0;
        public static float DashSpeed => Player != null ? (float)F_dashSpeed.GetValue(Player) : 0f;
        public static bool DashBuffered => Player != null && (bool)F_dashBuffer.GetValue(Player);
    }
}
