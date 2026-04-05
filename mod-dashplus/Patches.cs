using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IGTAPDashPlus
{
    [HarmonyPatch(typeof(Movement))]
    public static class DashPatches
    {
        private static float dashDirY;
        private static float dashDirX;
        private static bool wasDashing;

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

                dashDirY = Mathf.Abs(input.y) > 0.3f ? Mathf.Sign(input.y) : 0f;
                dashDirX = Mathf.Abs(input.x) > 0.3f ? Mathf.Sign(input.x) : 0f;

                // If vertical dash is disabled, only allow Y when there's also X (diagonal)
                if (!Plugin.VerticalDash.Value && dashDirX == 0f)
                    dashDirY = 0f;

                // If diagonal dash is disabled, only allow pure vertical
                if (!Plugin.DiagonalDash.Value && dashDirX != 0f)
                    dashDirY = 0f;
            }
        }

        // Apply the directional velocity each frame while dashing
        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        static void Update_Postfix(Movement __instance)
        {
            bool isDashing = __instance.cutsceneMode == Movement.cutsceneModes.dash;
            wasDashing = isDashing;

            if (!isDashing || dashDirY == 0f)
                return;

            var body = __instance.GetComponent<Rigidbody2D>();
            float dashSpeed = IGTAPMod.GameState.DashSpeed;
            float fullSpeed = dashSpeed * 10f;
            const float inv_sqrt2 = 0.7071f;

            if (dashDirX == 0f)
            {
                // Pure vertical
                body.linearVelocityX = 0f;
                body.linearVelocityY = dashDirY * fullSpeed * inv_sqrt2;
            }
            else
            {
                // Diagonal — normalize so total speed matches a normal dash
                body.linearVelocityX = dashDirX * fullSpeed * inv_sqrt2;
                body.linearVelocityY = dashDirY * fullSpeed * inv_sqrt2;
            }
        }
    }
}
