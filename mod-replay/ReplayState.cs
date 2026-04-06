using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace IGTAPReplay
{
    /// <summary>
    /// Reflection accessors for Movement private fields needed by the replay system.
    /// Extends the core mod's GameState with additional fields for snapshot/restore.
    /// </summary>
    public static class ReplayState
    {
        // Physics
        public static readonly FieldInfo F_body = AccessTools.Field(typeof(Movement), "body");
        public static readonly FieldInfo F_momentum = AccessTools.Field(typeof(Movement), "momentum");
        public static readonly FieldInfo F_lastAppliedMomentum = AccessTools.Field(typeof(Movement), "lastAppliedMomentum");

        // Ground/wall state
        public static readonly FieldInfo F_onGround = AccessTools.Field(typeof(Movement), "onGround");
        public static readonly FieldInfo F_OnWall = AccessTools.Field(typeof(Movement), "OnWall");
        public static readonly FieldInfo F_coyoteFrames = AccessTools.Field(typeof(Movement), "coyoteFrames");
        public static readonly FieldInfo F_wallCoyoteFrames = AccessTools.Field(typeof(Movement), "wallCoyoteFrames");
        public static readonly FieldInfo F_wallCoyoteDir = AccessTools.Field(typeof(Movement), "wallCoyoteDir");

        // Ability resources
        public static readonly FieldInfo F_airDashesLeft = AccessTools.Field(typeof(Movement), "airDashesLeft");
        public static readonly FieldInfo F_airJumpsLeft = AccessTools.Field(typeof(Movement), "airJumpsLeft");
        public static readonly FieldInfo F_wallJumpsLeft = AccessTools.Field(typeof(Movement), "wallJumpsLeft");

        // Dash state
        public static readonly FieldInfo F_dashCooldown = AccessTools.Field(typeof(Movement), "dashCooldown");
        public static readonly FieldInfo F_dashActive = AccessTools.Field(typeof(Movement), "dashActive");
        public static readonly FieldInfo F_isDashingRight = AccessTools.Field(typeof(Movement), "isDashingRight");
        public static readonly FieldInfo F_dashBuffer = AccessTools.Field(typeof(Movement), "dashBuffer");
        public static readonly FieldInfo F_timeSinceLastDash = AccessTools.Field(typeof(Movement), "timeSinceLastDash");
        public static readonly FieldInfo F_dashSpeed = AccessTools.Field(typeof(Movement), "dashSpeed");

        // Jump state
        public static readonly FieldInfo F_jumpBuffer = AccessTools.Field(typeof(Movement), "jumpBuffer");
        public static readonly FieldInfo F_MostRecentUpwardsMoveWasJump = AccessTools.Field(typeof(Movement), "MostRecentUpwardsMoveWasJump");

        // Wall jump state
        public static readonly FieldInfo F_isFinalWallClingActive = AccessTools.Field(typeof(Movement), "isFinalWallClingActive");
        public static readonly FieldInfo F_finalWallClingTimeLeft = AccessTools.Field(typeof(Movement), "finalWallClingTimeLeft");

        // Air tracking
        public static readonly FieldInfo F_timeInAir = AccessTools.Field(typeof(Movement), "timeInAir");
        public static readonly FieldInfo F_heightOfLastJumpStart = AccessTools.Field(typeof(Movement), "heightOfLastJumpStart");

        // Input
        public static readonly FieldInfo F_xMoveAxis = AccessTools.Field(typeof(Movement), "xMoveAxis");
        public static readonly FieldInfo F_moveAction = AccessTools.Field(typeof(Movement), "moveAction");
        public static readonly FieldInfo F_jumpAction = AccessTools.Field(typeof(Movement), "jumpAction");
        public static readonly FieldInfo F_dashAction = AccessTools.Field(typeof(Movement), "dashAction");

        // Death
        public static readonly FieldInfo F_isDead = AccessTools.Field(typeof(Movement), "isDead");

        // Pause
        public static readonly FieldInfo F_pauseMenu = AccessTools.Field(typeof(Movement), "pauseMenu");

        /// <summary>
        /// A snapshot of all gameplay-relevant state needed to restore the player
        /// to an exact position for deterministic replay.
        /// </summary>
        public struct Snapshot
        {
            // Transform
            public Vector2 Position;
            public Vector2 Velocity;
            public Vector2 Momentum;
            public float LastAppliedMomentum;

            // Ground/wall
            public bool OnGround;
            public bool OnWall;
            public float CoyoteFrames;
            public float WallCoyoteFrames;
            public float WallCoyoteDir;

            // Ability resources
            public int AirDashesLeft;
            public int AirJumpsLeft;
            public int WallJumpsLeft;

            // Dash
            public float DashCooldown;
            public bool DashActive;
            public bool IsDashingRight;
            public bool DashBuffer;
            public float TimeSinceLastDash;

            // Jump
            public bool JumpBuffer;
            public bool MostRecentUpwardsMoveWasJump;

            // Wall
            public float WallJumpDirection;
            public float WallJumpMovementLockRemaining;
            public bool IsFinalWallClingActive;
            public float FinalWallClingTimeLeft;

            // Air
            public float TimeInAir;
            public float HeightOfLastJumpStart;

            // Input
            public float XMoveAxis;

            // Death
            public bool IsDead;

            // Facing
            public bool FacingRight;

            // Cutscene
            public Movement.cutsceneModes CutsceneMode;

            // Unlocks & limits (needed to verify replay is compatible)
            public bool DashUnlocked;
            public bool WallJumpUnlocked;
            public bool DoubleJumpUnlocked;
            public bool BlockSwapUnlocked;
            public int MaxAirDashes;
            public int MaxAirJumps;
            public int MaxWallJumps;

            // Respawn
            public Vector2 RespawnPoint;
        }

        public static Snapshot Capture(Movement player)
        {
            var body = (Rigidbody2D)F_body.GetValue(player);
            return new Snapshot
            {
                Position = (Vector2)player.transform.position,
                Velocity = body.linearVelocity,
                Momentum = (Vector2)F_momentum.GetValue(player),
                LastAppliedMomentum = (float)F_lastAppliedMomentum.GetValue(player),

                OnGround = (bool)F_onGround.GetValue(player),
                OnWall = (bool)F_OnWall.GetValue(player),
                CoyoteFrames = (float)F_coyoteFrames.GetValue(player),
                WallCoyoteFrames = (float)F_wallCoyoteFrames.GetValue(player),
                WallCoyoteDir = (float)F_wallCoyoteDir.GetValue(player),

                AirDashesLeft = (int)F_airDashesLeft.GetValue(player),
                AirJumpsLeft = (int)F_airJumpsLeft.GetValue(player),
                WallJumpsLeft = (int)F_wallJumpsLeft.GetValue(player),

                DashCooldown = (float)F_dashCooldown.GetValue(player),
                DashActive = (bool)F_dashActive.GetValue(player),
                IsDashingRight = (bool)F_isDashingRight.GetValue(player),
                DashBuffer = (bool)F_dashBuffer.GetValue(player),
                TimeSinceLastDash = (float)F_timeSinceLastDash.GetValue(player),

                JumpBuffer = (bool)F_jumpBuffer.GetValue(player),
                MostRecentUpwardsMoveWasJump = (bool)F_MostRecentUpwardsMoveWasJump.GetValue(player),

                WallJumpDirection = player.wallJumpDirection,
                WallJumpMovementLockRemaining = player.wallJumpMovementLockRemaining,
                IsFinalWallClingActive = (bool)F_isFinalWallClingActive.GetValue(player),
                FinalWallClingTimeLeft = (float)F_finalWallClingTimeLeft.GetValue(player),

                TimeInAir = (float)F_timeInAir.GetValue(player),
                HeightOfLastJumpStart = (float)F_heightOfLastJumpStart.GetValue(player),

                XMoveAxis = (float)F_xMoveAxis.GetValue(player),
                IsDead = (bool)F_isDead.GetValue(player),

                FacingRight = player.facingRight,
                CutsceneMode = player.cutsceneMode,

                DashUnlocked = player.dashUnlocked,
                WallJumpUnlocked = player.wallJumpUnlocked,
                DoubleJumpUnlocked = player.doubleJumpUnlocked,
                BlockSwapUnlocked = player.blockSwapUnlocked,
                MaxAirDashes = player.maxAirDashes,
                MaxAirJumps = player.maxAirJumps,
                MaxWallJumps = player.maxWallJumps,

                RespawnPoint = player.respawnPoint,
            };
        }

        public static void Restore(Movement player, Snapshot snap)
        {
            var body = (Rigidbody2D)F_body.GetValue(player);

            // Set position on both transform and rigidbody, then force physics sync
            var pos = new Vector3(snap.Position.x, snap.Position.y, player.transform.position.z);
            player.transform.position = pos;
            body.position = snap.Position;
            Physics2D.SyncTransforms();
            body.linearVelocity = snap.Velocity;
            F_momentum.SetValue(player, snap.Momentum);
            F_lastAppliedMomentum.SetValue(player, snap.LastAppliedMomentum);

            F_onGround.SetValue(player, snap.OnGround);
            F_OnWall.SetValue(player, snap.OnWall);
            F_coyoteFrames.SetValue(player, snap.CoyoteFrames);
            F_wallCoyoteFrames.SetValue(player, snap.WallCoyoteFrames);
            F_wallCoyoteDir.SetValue(player, snap.WallCoyoteDir);

            F_airDashesLeft.SetValue(player, snap.AirDashesLeft);
            F_airJumpsLeft.SetValue(player, snap.AirJumpsLeft);
            F_wallJumpsLeft.SetValue(player, snap.WallJumpsLeft);

            F_dashCooldown.SetValue(player, snap.DashCooldown);
            F_dashActive.SetValue(player, snap.DashActive);
            F_isDashingRight.SetValue(player, snap.IsDashingRight);
            F_dashBuffer.SetValue(player, snap.DashBuffer);
            F_timeSinceLastDash.SetValue(player, snap.TimeSinceLastDash);

            F_jumpBuffer.SetValue(player, snap.JumpBuffer);
            F_MostRecentUpwardsMoveWasJump.SetValue(player, snap.MostRecentUpwardsMoveWasJump);

            player.wallJumpDirection = snap.WallJumpDirection;
            player.wallJumpMovementLockRemaining = snap.WallJumpMovementLockRemaining;
            F_isFinalWallClingActive.SetValue(player, snap.IsFinalWallClingActive);
            F_finalWallClingTimeLeft.SetValue(player, snap.FinalWallClingTimeLeft);

            F_timeInAir.SetValue(player, snap.TimeInAir);
            F_heightOfLastJumpStart.SetValue(player, snap.HeightOfLastJumpStart);

            F_xMoveAxis.SetValue(player, snap.XMoveAxis);
            F_isDead.SetValue(player, snap.IsDead);

            player.facingRight = snap.FacingRight;
            player.cutsceneMode = snap.CutsceneMode;

            player.dashUnlocked = snap.DashUnlocked;
            player.wallJumpUnlocked = snap.WallJumpUnlocked;
            player.doubleJumpUnlocked = snap.DoubleJumpUnlocked;
            player.blockSwapUnlocked = snap.BlockSwapUnlocked;
            player.maxAirDashes = snap.MaxAirDashes;
            player.maxAirJumps = snap.MaxAirJumps;
            player.maxWallJumps = snap.MaxWallJumps;

            player.respawnPoint = snap.RespawnPoint;

            // Set facing scale to match
            player.transform.localScale = new Vector3(snap.FacingRight ? 1f : -1f, 1f, 1f);
        }
    }
}
