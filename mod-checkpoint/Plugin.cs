using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using IGTAPMod;
using UnityEngine;

namespace IGTAPCheckpoint
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.igtapmod.plugin")]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.igtapmod.checkpoint";
        public const string PluginName = "IGTAP Checkpoints";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static Harmony HarmonyInstance;
        internal static CheckpointData Data;

        internal static ConfigEntry<KeyboardShortcut> SaveKey;
        internal static ConfigEntry<KeyboardShortcut> LoadKey;
        internal static ConfigEntry<KeyboardShortcut> CycleKey;
        internal static ConfigEntry<bool> OverrideRespawn;

        private void Awake()
        {
            Log = Logger;

            SaveKey = Config.Bind("Keybinds", "SaveKey",
                new KeyboardShortcut(KeyCode.F4),
                "Save current position to the active checkpoint slot");
            LoadKey = Config.Bind("Keybinds", "LoadKey",
                new KeyboardShortcut(KeyCode.F3),
                "Teleport to the active checkpoint slot's saved position");
            CycleKey = Config.Bind("Keybinds", "CycleKey",
                new KeyboardShortcut(KeyCode.F2),
                "Cycle to the next checkpoint slot");
            OverrideRespawn = Config.Bind("Respawn", "OverrideRespawn", false,
                "When enabled, death respawns at the active custom checkpoint instead of the game's checkpoint");

            Data = CheckpointData.Load(Paths.ConfigPath);

            HarmonyInstance = new Harmony(PluginGUID);
            HarmonyInstance.PatchAll();

            CheckpointMenu.Register();

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded!");
        }

        private void Update()
        {
            var player = GameState.Player;
            if (player == null) return;

            if (SaveKey.Value.IsDown())
            {
                Vector3 pos = player.transform.position;
                Data.SaveToSlot(pos.x, pos.y);
                Data.WriteToDisk();
                Log.LogInfo($"Saved checkpoint '{Data.ActiveSlotName}' at ({pos.x:F1}, {pos.y:F1})");
            }

            if (LoadKey.Value.IsDown())
            {
                var slot = Data.ActiveSlot;
                if (slot != null && slot.HasPosition)
                {
                    player.transform.position = new Vector3(slot.X, slot.Y, 0f);
                    player.respawnPoint = new Vector2(slot.X, slot.Y);
                    Log.LogInfo($"Loaded checkpoint '{slot.Name}' at ({slot.X:F1}, {slot.Y:F1})");
                }
                else
                {
                    Log.LogWarning($"No position saved in slot '{Data.ActiveSlotName}'");
                }
            }

            if (CycleKey.Value.IsDown())
            {
                Data.CycleSlot();
                Log.LogInfo($"Active slot: {Data.ActiveSlotName} ({Data.ActiveSlotIndex + 1}/{Data.Slots.Count})");
            }

            CheckpointClones.UpdateClones();
        }

        private void OnDestroy()
        {
            CheckpointClones.DestroyAll();
            HarmonyInstance?.UnpatchSelf();
        }
    }
}
