using IGTAPMod;
using UnityEngine;

namespace IGTAPCheckpoint
{
    public static class CheckpointMenu
    {
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

            DebugMenuAPI.RegisterSection("Checkpoints", 50, BuildCheckpoints);
        }

        private static void BuildCheckpoints(WidgetPanel panel)
        {
            var data = Plugin.Data;

            // Active slot navigation
            panel.AddLabel(() =>
            {
                return $"Slot {data.ActiveSlotIndex + 1}/{data.Slots.Count}: {data.ActiveSlotName}";
            }, UIStyle.FontSizeBody);

            panel.AddButtonRow(
                ("< Prev", () => data.CycleSlotBack()),
                ("Next >", () => data.CycleSlot())
            );

            // Position display
            panel.AddLabel(() =>
            {
                var active = data.ActiveSlot;
                if (active != null && active.HasPosition)
                    return $"Position: ({active.X:F1}, {active.Y:F1})";
                return "Position: (empty)";
            }, UIStyle.FontSizeSmall, UIStyle.TextSecondary);

            // Save / Load buttons
            panel.AddButtonRow(
                ("Save Here", () =>
                {
                    var player = GameState.Player;
                    if (player != null)
                    {
                        Vector3 pos = player.transform.position;
                        data.SaveToSlot(pos.x, pos.y);
                        data.WriteToDisk();
                    }
                }),
                ("Teleport", () =>
                {
                    var player = GameState.Player;
                    var active = data.ActiveSlot;
                    if (player != null && active != null && active.HasPosition)
                    {
                        player.transform.position = new Vector3(active.X, active.Y, 0f);
                        player.respawnPoint = new Vector2(active.X, active.Y);
                    }
                })
            );

            panel.AddSpacer();

            // Override respawn toggle
            panel.AddToggle("Override death respawn",
                () => Plugin.OverrideRespawn.Value,
                v => Plugin.OverrideRespawn.Value = v);

            panel.AddSeparator();

            // Dynamic slot list
            panel.AddLabel(() => "All Slots:", UIStyle.FontSizeSmall, UIStyle.TextSecondary);
            panel.AddDynamic("slots", BuildSlotList);

            panel.AddSpacer();

            // New slot button
            panel.AddButton("+ New Slot", () =>
            {
                data.AddSlot($"Slot {data.Slots.Count + 1}");
                data.WriteToDisk();
            });

            // Rename
            string renameBuffer = "";
            panel.AddTextField("Rename", () => renameBuffer, v => renameBuffer = v);
            panel.AddButton("Apply Rename", () =>
            {
                if (!string.IsNullOrEmpty(renameBuffer))
                {
                    data.RenameSlot(data.ActiveSlotIndex, renameBuffer);
                    renameBuffer = "";
                    data.WriteToDisk();
                }
            });
        }

        private static void BuildSlotList(WidgetPanel panel)
        {
            var data = Plugin.Data;
            for (int i = 0; i < data.Slots.Count; i++)
            {
                int slotIndex = i;
                var slot = data.Slots[i];
                string prefix = i == data.ActiveSlotIndex ? "> " : "  ";
                string pos = slot.HasPosition ? $"({slot.X:F0}, {slot.Y:F0})" : "(empty)";

                panel.AddLabel(() => $"{prefix}{slot.Name} {pos}", UIStyle.FontSizeSmall);

                if (i != data.ActiveSlotIndex)
                {
                    panel.AddButtonRow(
                        ("Set Active", () => data.ActiveSlotIndex = slotIndex),
                        ("Delete", () =>
                        {
                            if (data.Slots.Count > 1)
                            {
                                data.RemoveSlot(slotIndex);
                                data.WriteToDisk();
                            }
                        })
                    );
                }
            }
        }
    }
}
