using HarmonyLib;
using IGTAPMod;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace IGTAPFreeplay
{
    [HarmonyPatch(typeof(Movement))]
    public static class FreeplayPatches
    {
        [HarmonyPatch("onDeath")]
        [HarmonyPrefix]
        static bool OnDeath_Prefix()
        {
            return !Plugin.GodMode.Value;
        }

        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        static void Start_Postfix(Movement __instance)
        {
            __instance.runSpeed *= Plugin.SpeedMultiplier.Value;
            __instance.jumpForce *= Plugin.JumpMultiplier.Value;
            __instance.maxAirDashes += Plugin.ExtraAirDashes.Value;
            __instance.maxAirJumps += Plugin.ExtraAirJumps.Value;

            if (Plugin.AlwaysGlow.Value)
            {
                Light2D light = __instance.personalLight;
                if (light != null)
                {
                    __instance.lightActive = true;
                    __instance.lightRadius = Plugin.GlowRadius.Value;
                    light.enabled = true;
                    light.gameObject.SetActive(true);
                    light.pointLightOuterRadius = Plugin.GlowRadius.Value;
                    light.pointLightInnerRadius = Plugin.GlowRadius.Value * 0.15f;
                    light.intensity = Plugin.GlowIntensity.Value;
                    light.color = Plugin.GlowColor;
                }
            }
        }

        private static Rigidbody2D cachedBody;
        private static Collider2D[] cachedColliders;
        private static RigidbodyType2D savedBodyType;

        [HarmonyPatch("Update")]
        [HarmonyPrefix]
        static bool Update_Prefix(Movement __instance)
        {
            if (Plugin.AlwaysGlow.Value && __instance.personalLight != null)
            {
                __instance.personalLight.enabled = true;
                __instance.lightActive = true;
            }

            if (Plugin.InfiniteWallJumps.Value && __instance.maxWallJumps > 0)
                GameState.F_wallJumpsLeft.SetValue(__instance, __instance.maxWallJumps);
            if (Plugin.InfiniteDashes.Value)
                GameState.F_airDashesLeft.SetValue(__instance, __instance.maxAirDashes);
            if (Plugin.InfiniteJumps.Value)
                GameState.F_airJumpsLeft.SetValue(__instance, __instance.maxAirJumps);

            if (Plugin.NoclipToggleKey.Value.IsDown())
            {
                Plugin.NoclipActive = !Plugin.NoclipActive;
                Plugin.Log.LogInfo($"Noclip: {(Plugin.NoclipActive ? "ON" : "OFF")}");

                if (cachedBody == null)
                    cachedBody = __instance.GetComponent<Rigidbody2D>();
                if (cachedColliders == null)
                    cachedColliders = __instance.GetComponentsInChildren<Collider2D>();

                if (Plugin.NoclipActive)
                {
                    savedBodyType = cachedBody.bodyType;
                    cachedBody.bodyType = RigidbodyType2D.Kinematic;
                    cachedBody.linearVelocity = Vector2.zero;
                    foreach (var col in cachedColliders)
                        col.enabled = false;
                }
                else
                {
                    cachedBody.bodyType = savedBodyType;
                    foreach (var col in cachedColliders)
                        col.enabled = true;
                }
            }

            if (Plugin.NoclipActive)
            {
                float speed = Plugin.NoclipSpeed.Value;
                if (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed)
                    speed *= Plugin.NoclipFastMultiplier.Value;

                var moveAction = InputSystem.actions.FindAction("Move");
                var jumpAction = InputSystem.actions.FindAction("Jump");
                var dashAction = InputSystem.actions.FindAction("Dash");

                Vector2 stick = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
                float h = stick.x;
                float v = stick.y;

                if (Keyboard.current.wKey.isPressed || (jumpAction != null && jumpAction.IsPressed()))
                    v = 1f;
                if (Keyboard.current.sKey.isPressed || (dashAction != null && dashAction.IsPressed()))
                    v = -1f;

                Vector3 move = new Vector3(h, v, 0f).normalized * speed * Time.deltaTime;
                __instance.transform.position += move;

                if (cachedBody != null)
                    cachedBody.linearVelocity = Vector2.zero;

                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(courseScript))]
    public static class EconomyPatches
    {
        [HarmonyPatch("UpdateReward")]
        [HarmonyPostfix]
        static void UpdateReward_Postfix(courseScript __instance)
        {
            if (Plugin.CurrencyMultiplier.Value != 1.0)
                __instance.reward *= Plugin.CurrencyMultiplier.Value;
        }
    }
}
