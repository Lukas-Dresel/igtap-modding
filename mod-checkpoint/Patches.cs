using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace IGTAPCheckpoint
{
    [HarmonyPatch(typeof(Movement))]
    public static class RespawnPatches
    {
        private static readonly FieldInfo F_isDead = AccessTools.Field(typeof(Movement), "isDead");
        private static readonly FieldInfo F_normalCollider = AccessTools.Field(typeof(Movement), "normalCollider");
        private static readonly FieldInfo F_defaultColliderSize = AccessTools.Field(typeof(Movement), "defaultColliderSize");
        private static readonly FieldInfo F_defaultColliderOffset = AccessTools.Field(typeof(Movement), "defaultColliderOffset");
        private static readonly FieldInfo F_blockSwapper = AccessTools.Field(typeof(Movement), "blockSwapper");

        [HarmonyPatch("respawn")]
        [HarmonyPrefix]
        static bool Respawn_Prefix(Movement __instance)
        {
            if (!Plugin.OverrideRespawn.Value)
                return true;

            var slot = Plugin.Data.ActiveSlot;
            if (slot == null || !slot.HasPosition)
                return true;

            // Block swap reset (same as original)
            if (__instance.blockSwapUnlocked)
            {
                var blockSwapper = (colouredBlockSwapper)F_blockSwapper.GetValue(__instance);
                blockSwapper.swapBlocks(blockSwapper.isBlueActive);
            }

            // Teleport to custom checkpoint
            __instance.transform.position = new Vector3(slot.X, slot.Y, 0f);

            // Reset death state (same as original)
            F_isDead.SetValue(__instance, false);
            __instance.cutsceneMode = Movement.cutsceneModes.none;

            // Reset collider (same as original)
            var normalCollider = (BoxCollider2D)F_normalCollider.GetValue(__instance);
            var defaultSize = (Vector2)F_defaultColliderSize.GetValue(__instance);
            var defaultOffset = (Vector2)F_defaultColliderOffset.GetValue(__instance);
            normalCollider.size = defaultSize;
            normalCollider.offset = defaultOffset;

            __instance.transform.eulerAngles = Vector3.zero;

            return false; // skip original respawn
        }
    }
}
