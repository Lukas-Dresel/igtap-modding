using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IGTAPDashPlus
{
    [HarmonyPatch(typeof(Movement))]
    public static class DashPatches
    {
        // Cached field accessors for private Movement fields
        private static readonly FieldInfo F_onGround = AccessTools.Field(typeof(Movement), "onGround");
        private static readonly FieldInfo F_momentum = AccessTools.Field(typeof(Movement), "momentum");
        private static readonly FieldInfo F_dashJumpMagnitude = AccessTools.Field(typeof(Movement), "dashJumpMagnitude");

        // Directional dash state (set by GetInput_Prefix, used by Update_Postfix)
        private static float dashDirY;
        private static float dashDirX;
        private static bool wasDashing;

        // Hyper-dash detection: were we in a downward-diagonal dash on the ground at frame start?
        private static bool wasDownDashOnGround;

        // Ultra-dash detection: were we in a downward-diagonal dash in the air at frame start?
        private static bool wasDownDashInAir;
        private static float preLandingVelocityY;

        // Tuning constants
        const float HyperJumpHeightMul = 0.65f;
        const float HyperMomentumMul = 1.8f;
        const float UltraSpeedConversion = 3.0f;

        // Track consecutive airborne frames to filter ground-detection flicker
        private static int airborneFrames;

        // Capture directional input right before the game processes the dash
        [HarmonyPatch("getPlayerControlledInput")]
        [HarmonyPrefix]
        static void GetInput_Prefix(Movement __instance)
        {
            if (!Plugin.DiagonalDash.Value && !Plugin.VerticalDash.Value)
                return;

            bool dashBuffer = IGTAPMod.GameState.DashBuffered;
            if (dashBuffer && __instance.dashUnlocked)
            {
                var moveAction = InputSystem.actions.FindAction("Move");
                Vector2 input = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;

                Plugin.Log.LogInfo($"Dash input: raw=({input.x:F2},{input.y:F2})");

                dashDirY = Mathf.Abs(input.y) > 0.3f ? Mathf.Sign(input.y) : 0f;
                dashDirX = Mathf.Abs(input.x) > 0.3f ? Mathf.Sign(input.x) : 0f;

                Plugin.Log.LogInfo($"Dash dir: ({dashDirX},{dashDirY})");

                // If vertical dash is disabled, only allow Y when there's also X (diagonal)
                if (!Plugin.VerticalDash.Value && dashDirX == 0f)
                    dashDirY = 0f;

                // If diagonal dash is disabled, only allow pure vertical
                if (!Plugin.DiagonalDash.Value && dashDirX != 0f)
                    dashDirY = 0f;
            }
        }

        // Capture pre-frame state for hyper/ultra detection
        [HarmonyPatch("Update")]
        [HarmonyPrefix]
        static void Update_Prefix(Movement __instance)
        {
            bool isDashing = __instance.cutsceneMode == Movement.cutsceneModes.dash;
            bool isDownDiagonal = isDashing && dashDirY < 0f && dashDirX != 0f;

            if (!isDownDiagonal)
            {
                wasDownDashOnGround = false;
                wasDownDashInAir = false;
                return;
            }

            bool onGround = (bool)F_onGround.GetValue(__instance);

            wasDownDashOnGround = onGround && Plugin.HyperDash.Value;

            if (!onGround)
                airborneFrames++;
            else
                airborneFrames = 0;

            // Require at least 2 airborne frames to filter ground-detection flicker
            wasDownDashInAir = !onGround && airborneFrames >= 2 && Plugin.UltraDash.Value;
            if (wasDownDashInAir)
                preLandingVelocityY = __instance.GetComponent<Rigidbody2D>().linearVelocityY;
        }

        // Apply directional velocity during dash + hyper/ultra logic
        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        static void Update_Postfix(Movement __instance)
        {
            bool isDashing = __instance.cutsceneMode == Movement.cutsceneModes.dash;

            // Save direction before potential reset (needed by hyper/ultra after dash ends)
            float dirX = dashDirX;
            float dirY = dashDirY;

            // Reset dash direction when dash ends
            if (!isDashing && wasDashing)
            {
                dashDirY = 0f;
                dashDirX = 0f;
            }
            wasDashing = isDashing;

            var body = __instance.GetComponent<Rigidbody2D>();
            bool onGroundNow = (bool)F_onGround.GetValue(__instance);

            // --- Diagonal/vertical velocity override during dash ---
            if (isDashing && dirY != 0f)
            {
                float dashSpeed = IGTAPMod.GameState.DashSpeed;
                float fullSpeed = dashSpeed * 10f;
                const float inv_sqrt2 = 0.7071f;

                if (dirX == 0f)
                {
                    // Pure vertical
                    body.linearVelocityX = 0f;
                    body.linearVelocityY = dirY * fullSpeed * inv_sqrt2;
                }
                else
                {
                    // Diagonal
                    body.linearVelocityX = dirX * fullSpeed * inv_sqrt2;
                    // Don't push player into the ground during a downward dash
                    if (dirY < 0f && onGroundNow)
                        body.linearVelocityY = 0f;
                    else
                        body.linearVelocityY = dirY * fullSpeed * inv_sqrt2;
                }
            }

            // --- Hyper-dash: dash-jump fired during a downward-diagonal ground dash ---
            if (wasDownDashOnGround
                && __instance.cutsceneMode == Movement.cutsceneModes.none
                && body.linearVelocityY > 0f)
            {
                Plugin.Log.LogInfo($"HYPER-DASH! dirX={dirX} velY={body.linearVelocityY:F0}");

                // Reduce jump height
                body.linearVelocityY = __instance.jumpForce * HyperJumpHeightMul;

                // Replace the normal dash-jump momentum with a bigger horizontal boost
                float dashJumpMag = (float)F_dashJumpMagnitude.GetValue(__instance);
                Vector2 momentum = (Vector2)F_momentum.GetValue(__instance);
                // Remove the original dash-jump contribution, add the hyper-scaled version
                momentum.x += dirX * 10f * dashJumpMag * (HyperMomentumMul - 1f);
                F_momentum.SetValue(__instance, momentum);

                Plugin.Log.LogInfo($"  -> velY={body.linearVelocityY:F0} momentum.x={momentum.x:F0}");
                wasDownDashOnGround = false;
            }

            // --- Ultra-dash: landed during a downward-diagonal air dash ---
            if (wasDownDashInAir && onGroundNow)
            {
                float downwardSpeed = Mathf.Abs(preLandingVelocityY);
                float ultraBoost = downwardSpeed * UltraSpeedConversion;

                Plugin.Log.LogInfo($"ULTRA-DASH! dirX={dirX} downSpeed={downwardSpeed:F0} boost={ultraBoost:F0}");

                Vector2 momentum = (Vector2)F_momentum.GetValue(__instance);
                momentum.x += dirX * ultraBoost;
                F_momentum.SetValue(__instance, momentum);

                Plugin.Log.LogInfo($"  -> momentum.x={momentum.x:F0}");
                wasDownDashInAir = false;
            }
        }
    }
}
