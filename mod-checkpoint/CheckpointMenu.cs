using IGTAPMod;
using UnityEngine;

namespace IGTAPCheckpoint
{
    public static class CheckpointMenu
    {
        private static string renameBuffer = "";

        public static void Register()
        {
            DebugMenuAPI.RegisterHudItem("checkpoint.slot", 25, () =>
            {
                var slot = Plugin.Data.ActiveSlot;
                if (slot == null) return null;
                string pos = slot.HasPosition
                    ? $"({slot.X:F0}, {slot.Y:F0})"
                    : "(empty)";
                return $"[CP: {slot.Name} {pos}]";
            });

            DebugMenuAPI.RegisterSection("Checkpoints", 50, DrawCheckpoints);
        }

        private static void DrawCheckpoints()
        {
            var data = Plugin.Data;
            var player = GameState.Player;

            // Active slot header
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(25)))
                data.CycleSlotBack();
            GUILayout.Label($"Slot {data.ActiveSlotIndex + 1}/{data.Slots.Count}: {data.ActiveSlotName}",
                GUILayout.ExpandWidth(true));
            if (GUILayout.Button(">", GUILayout.Width(25)))
                data.CycleSlot();
            GUILayout.EndHorizontal();

            // Active slot position
            var active = data.ActiveSlot;
            if (active != null && active.HasPosition)
                GUILayout.Label($"  Position: ({active.X:F1}, {active.Y:F1})");
            else
                GUILayout.Label("  Position: (empty)");

            // Save / Load buttons
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Here") && player != null)
            {
                Vector3 pos = player.transform.position;
                data.SaveToSlot(pos.x, pos.y);
                data.WriteToDisk();
            }
            if (GUILayout.Button("Teleport") && player != null && active != null && active.HasPosition)
            {
                player.transform.position = new Vector3(active.X, active.Y, 0f);
                player.respawnPoint = new Vector2(active.X, active.Y);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5);

            // Override respawn toggle
            Plugin.OverrideRespawn.Value = GUILayout.Toggle(Plugin.OverrideRespawn.Value,
                "Override death respawn");

            GUILayout.Space(5);

            // Slot list
            GUILayout.Label("All Slots:");
            for (int i = 0; i < data.Slots.Count; i++)
            {
                var slot = data.Slots[i];
                GUILayout.BeginHorizontal();

                string prefix = i == data.ActiveSlotIndex ? "> " : "  ";
                string pos = slot.HasPosition ? $"({slot.X:F0}, {slot.Y:F0})" : "(empty)";
                GUILayout.Label($"{prefix}{slot.Name} {pos}", GUILayout.ExpandWidth(true));

                if (i != data.ActiveSlotIndex && GUILayout.Button("Set", GUILayout.Width(35)))
                    data.ActiveSlotIndex = i;

                if (data.Slots.Count > 1 && GUILayout.Button("X", GUILayout.Width(25)))
                {
                    data.RemoveSlot(i);
                    data.WriteToDisk();
                    break; // list changed, exit loop
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.Space(5);

            // Add / Rename
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ New Slot"))
            {
                data.AddSlot($"Slot {data.Slots.Count + 1}");
                data.WriteToDisk();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            renameBuffer = GUILayout.TextField(renameBuffer, GUILayout.Width(120));
            if (GUILayout.Button("Rename") && !string.IsNullOrEmpty(renameBuffer))
            {
                data.RenameSlot(data.ActiveSlotIndex, renameBuffer);
                renameBuffer = "";
                data.WriteToDisk();
            }
            GUILayout.EndHorizontal();
        }
    }
}
