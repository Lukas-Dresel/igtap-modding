using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace IGTAPMod
{
    /// <summary>
    /// Upgrade info passed to UpgradeBought subscribers.
    /// </summary>
    public struct UpgradeInfo
    {
        public string Id;          // "dash", "wallJump", "cloneCount", "openGate", etc.
        public string Label;       // human-readable
        public string Category;    // "movement", "global", "local"
        public upgradeBox Box;     // raw reference for advanced consumers
    }

    /// <summary>
    /// Info about a game start (player spawn / level load).
    /// </summary>
    public struct GameStartInfo
    {
        public Vector2 SpawnPosition;
        public bool IsCleanStart;  // true = no save existed (fresh game), false = returning player
    }

    /// <summary>
    /// Centralized game event dispatch. Harmony patches fire Action events that any mod
    /// can subscribe to. These are notification-only — they never alter game behavior.
    /// Mods that need to BLOCK behavior (god mode, custom respawn, etc.) keep their own prefixes.
    /// </summary>
    public static class GameEvents
    {
        // --- Events ---

        /// <summary>Fires when player enters a course start gate.</summary>
        public static event Action<int, courseScript> CourseStarted;

        /// <summary>Fires when player exits a course (end gate or boundary).
        /// Args: courseNumber, completed (true = reached end gate), courseTime (seconds the player was in the course).</summary>
        public static event Action<int, bool, float> CourseStopped;

        /// <summary>Fires when any upgrade is purchased.</summary>
        public static event Action<UpgradeInfo> UpgradeBought;

        /// <summary>Fires when player touches a checkpoint (respawn point update). Arg: checkpoint world position.</summary>
        public static event Action<Vector2> CheckpointHit;

        /// <summary>Fires when player dies. Arg: player world position at death.</summary>
        public static event Action<Vector2> PlayerDied;

        /// <summary>Fires when player respawns. Arg: player world position after respawn.</summary>
        public static event Action<Vector2> PlayerRespawned;

        /// <summary>Fires when the player object is created (game/level loaded). Carries spawn position
        /// and a flag indicating whether this is a clean first start (no save data) vs a returning player.</summary>
        public static event Action<GameStartInfo> GameStarted;

        // --- Harmony patches ---

        // Reflection for private fields
        private static readonly FieldInfo F_tracking = AccessTools.Field(typeof(courseScript), "tracking");
        private static readonly FieldInfo F_currentPathTime = AccessTools.Field(typeof(courseScript), "currentPathTime");

        private static readonly FieldInfo F_upgrade = AccessTools.Field(typeof(upgradeBox), "upgrade");
        private static readonly FieldInfo F_globalUpgrade = AccessTools.Field(typeof(upgradeBox), "globalUpgrade");
        private static readonly FieldInfo F_movementUpgrade = AccessTools.Field(typeof(upgradeBox), "movementUpgrade");

        private static readonly FieldInfo F_movementInit = AccessTools.Field(typeof(Movement), "init");

        [HarmonyPatch(typeof(courseScript), "startTracking")]
        public static class CourseStartPatch
        {
            static void Postfix(courseScript __instance)
            {
                CourseStarted?.Invoke(__instance.courseNumber, __instance);
            }
        }

        [HarmonyPatch(typeof(courseScript), "stopTracking")]
        public static class CourseStopPatch
        {
            // Prefix captures courseTime before the method resets it to 0
            static void Prefix(courseScript __instance, out (bool wasTracking, float time) __state)
            {
                bool tracking = (bool)F_tracking.GetValue(__instance);
                float time = (float)F_currentPathTime.GetValue(__instance);
                __state = (tracking, time);
            }

            static void Postfix(courseScript __instance, bool savePositionData, (bool wasTracking, float time) __state)
            {
                if (!__state.wasTracking) return;
                CourseStopped?.Invoke(__instance.courseNumber, savePositionData, __state.time);
            }
        }

        [HarmonyPatch(typeof(upgradeBox), "DoBuyUpgrade")]
        public static class UpgradePatch
        {
            static void Postfix(upgradeBox __instance)
            {
                var upgradeType = (localUpgrades.localUpgradeSet)F_upgrade.GetValue(__instance);
                int movUpgrade = (int)F_movementUpgrade.GetValue(__instance);
                var globalUpgradeType = (globalStats.globalUpgradeSet)F_globalUpgrade.GetValue(__instance);

                var info = ResolveUpgradeInfo(upgradeType, movUpgrade, globalUpgradeType, __instance);
                if (info.HasValue)
                    UpgradeBought?.Invoke(info.Value);
            }
        }

        [HarmonyPatch(typeof(checkpointScript), "OnTriggerEnter2D")]
        public static class CheckpointPatch
        {
            static void Postfix(checkpointScript __instance)
            {
                CheckpointHit?.Invoke(__instance.transform.position);
            }
        }

        [HarmonyPatch(typeof(Movement), "onDeath")]
        public static class DeathPatch
        {
            static void Postfix(Movement __instance)
            {
                PlayerDied?.Invoke(__instance.transform.position);
            }
        }

        [HarmonyPatch(typeof(Movement), "respawn")]
        public static class RespawnPatch
        {
            static void Postfix(Movement __instance)
            {
                PlayerRespawned?.Invoke(__instance.transform.position);
            }
        }

        [HarmonyPatch(typeof(Movement), "Start")]
        public static class MovementStartPatch
        {
            static void Postfix(Movement __instance)
            {
                bool isClean = (bool)F_movementInit.GetValue(__instance);
                GameStarted?.Invoke(new GameStartInfo
                {
                    SpawnPosition = __instance.transform.position,
                    IsCleanStart = isClean,
                });
            }
        }

        // --- Upgrade resolution ---

        private static UpgradeInfo? ResolveUpgradeInfo(
            localUpgrades.localUpgradeSet upgradeType,
            int movUpgrade,
            globalStats.globalUpgradeSet globalUpgradeType,
            upgradeBox box)
        {
            // Movement upgrades
            if (upgradeType == localUpgrades.localUpgradeSet.Movement)
            {
                switch (movUpgrade)
                {
                    case 0: return MakeInfo("dash", "Dash", "movement", box);
                    case 1: return MakeInfo("wallJump", "Wall Jump", "movement", box);
                    case 2: return MakeInfo("doubleJump", "Double Jump", "movement", box);
                    case 3: return MakeInfo("swapBlocksOnce", "Swap Blocks Once", "movement", box);
                    case 4: return MakeInfo("blockSwap", "Block Swap", "movement", box);
                }
            }

            // Global upgrades
            if (upgradeType == localUpgrades.localUpgradeSet.GLOBAL)
            {
                switch (globalUpgradeType)
                {
                    case globalStats.globalUpgradeSet.openGate:
                        return MakeInfo("openGate", "Open Gate", "global", box);
                    case globalStats.globalUpgradeSet.unlockPrestige:
                        return MakeInfo("unlockPrestige", "Prestige", "global", box);
                    case globalStats.globalUpgradeSet.cashPerLoop:
                        return MakeInfo("cashPerLoop", "Cash Per Loop", "global", box);
                    case globalStats.globalUpgradeSet.cloneMult:
                        return MakeInfo("cloneMult", "Clone Mult", "global", box);
                    case globalStats.globalUpgradeSet.TreeGrowth:
                        return MakeInfo("treeGrowth", "Tree Growth", "global", box);
                    case globalStats.globalUpgradeSet.maxCloneFastness:
                        return MakeInfo("maxCloneFastness", "Max Clone Speed", "global", box);
                    case globalStats.globalUpgradeSet.maxCloneBigness:
                        return MakeInfo("maxCloneBigness", "Max Clone Size", "global", box);
                    case globalStats.globalUpgradeSet.greenCloneClance:
                        return MakeInfo("greenCloneChance", "Green Clone Chance", "global", box);
                    case globalStats.globalUpgradeSet.spawnNewAtom:
                        return MakeInfo("spawnNewAtom", "Spawn Atom", "global", box);
                    default:
                        return MakeInfo(globalUpgradeType.ToString(), globalUpgradeType.ToString(), "global", box);
                }
            }

            // Local upgrades
            switch (upgradeType)
            {
                case localUpgrades.localUpgradeSet.cloneCount:
                    return MakeInfo("cloneCount", "Clone Count", "local", box);
                case localUpgrades.localUpgradeSet.cashPerLoop:
                    return MakeInfo("localCashPerLoop", "Cash Per Loop", "local", box);
                case localUpgrades.localUpgradeSet.fastCloneChance:
                    return MakeInfo("fastCloneChance", "Fast Clone Chance", "local", box);
                case localUpgrades.localUpgradeSet.bigCloneChance:
                    return MakeInfo("bigCloneChance", "Big Clone Chance", "local", box);
                case localUpgrades.localUpgradeSet.prestige:
                    return MakeInfo("prestige", "Prestige", "local", box);
                case localUpgrades.localUpgradeSet.cloneMult:
                    return MakeInfo("localCloneMult", "Clone Mult", "local", box);
            }

            return null;
        }

        private static UpgradeInfo MakeInfo(string id, string label, string category, upgradeBox box)
        {
            return new UpgradeInfo
            {
                Id = id,
                Label = label,
                Category = category,
                Box = box,
            };
        }
    }
}
