using IGTAPMod;
using UnityEngine;

namespace IGTAPRandomizer
{
    public static class RandomizerMenu
    {
        public static void Register()
        {
            DebugMenuAPI.RegisterHudItem("randomizer.seed", 40, () =>
            {
                if (!Plugin.RandomizeOnCourseStart.Value && !Plugin.AnnotateMode.Value)
                    return null;
                string tag = Plugin.AnnotateMode.Value ? "ANNOTATE" : "SEED";
                return $"[RAND {tag}: {Plugin.Seed.Value}]";
            });

            DebugMenuAPI.RegisterSection("Randomizer", 60, BuildMenu);
        }

        private static void BuildMenu(WidgetPanel panel)
        {
            panel.AddLabel(() =>
            {
                var layout = Plugin.Course4Layout;
                int rooms = layout?.rooms.Count ?? 0;
                string start = string.IsNullOrEmpty(layout?.startRoomId) ? "(unset)" : layout.startRoomId;
                string end = string.IsNullOrEmpty(layout?.endRoomId) ? "(unset)" : layout.endRoomId;
                return $"Course {Plugin.PrototypeCourse}: {rooms} rooms | start={start} | end={end}";
            }, UIStyle.FontSizeBody);

            panel.AddSpacer();

            panel.AddToggle("Annotate mode (F8 to toggle menu)",
                () => Plugin.AnnotateMode.Value,
                v => Plugin.AnnotateMode.Value = v);

            panel.AddToggle("Randomize on course start (Phase 5)",
                () => Plugin.RandomizeOnCourseStart.Value,
                v => Plugin.RandomizeOnCourseStart.Value = v);

            panel.AddIntField("Seed",
                () => Plugin.Seed.Value,
                v => Plugin.Seed.Value = v,
                min: 0);

            panel.AddButtonRow(
                ("Re-roll seed", () =>
                {
                    Plugin.Seed.Value = Random.Range(1, int.MaxValue);
                    Plugin.Log.LogInfo($"Re-rolled seed: {Plugin.Seed.Value}");
                }),
                ("Reload layout", () =>
                {
                    Plugin.ReloadLayout();
                    PanelRebuild(panel);
                })
            );

            panel.AddSeparator();

            panel.AddLabel(() => "Authoring", UIStyle.FontSizeBody);

            panel.AddButtonRow(
                ("New room", () => RoomAuthoring.BeginNewRoom()),
                ("Cancel pending", () => RoomAuthoring.CancelPending())
            );

            panel.AddLabel(() => $"Current mode: {RoomAuthoring.CurrentMode}",
                UIStyle.FontSizeSmall, UIStyle.TextSecondary);

            panel.AddSeparator();

            panel.AddLabel(() => "Rooms", UIStyle.FontSizeBody);
            panel.AddDynamic("rooms", BuildRoomList);

            panel.AddSpacer();

            panel.AddButton("Save layout", () =>
            {
                Plugin.SaveLayout();
            });
        }

        private static void BuildRoomList(WidgetPanel inner)
        {
            var layout = Plugin.Course4Layout;
            if (layout == null || layout.rooms.Count == 0)
            {
                inner.AddLabel(() => "  (no rooms annotated yet)",
                    UIStyle.FontSizeSmall, UIStyle.TextSecondary);
                return;
            }

            for (int i = 0; i < layout.rooms.Count; i++)
            {
                var room = layout.rooms[i];
                string roomId = room.id; // capture for closures

                bool isStart = layout.startRoomId == roomId;
                bool isEnd = layout.endRoomId == roomId;
                string tag = isStart ? " [START]" : isEnd ? " [END]" : "";

                inner.AddLabel(() =>
                {
                    var r = layout.FindRoom(roomId);
                    if (r == null) return $"  {roomId} (missing)";
                    string sel = RoomAuthoring.SelectedRoomId == roomId ? "> " : "  ";
                    return $"{sel}{roomId}{tag}  tiles ({r.tileXMin},{r.tileYMin})-({r.tileXMax},{r.tileYMax})  entries:{r.entries.Count}";
                }, UIStyle.FontSizeSmall);

                inner.AddButtonRow(
                    ("Select", () => RoomAuthoring.SelectedRoomId = roomId),
                    ("Delete", () =>
                    {
                        RoomAuthoring.SelectedRoomId = roomId;
                        RoomAuthoring.DeleteSelected();
                    })
                );
                inner.AddButtonRow(
                    ("Set Start", () => { RoomAuthoring.SelectedRoomId = roomId; RoomAuthoring.SetSelectedAsStart(); }),
                    ("Set End", () => { RoomAuthoring.SelectedRoomId = roomId; RoomAuthoring.SetSelectedAsEnd(); })
                );
                inner.AddButtonRow(
                    ("Add Entry (In)", () => { RoomAuthoring.SelectedRoomId = roomId; RoomAuthoring.BeginAddEntry(EntryKind.In); }),
                    ("Add Entry (Out)", () => { RoomAuthoring.SelectedRoomId = roomId; RoomAuthoring.BeginAddEntry(EntryKind.Out); })
                );
                inner.AddButton("Clear entries", () =>
                {
                    RoomAuthoring.SelectedRoomId = roomId;
                    RoomAuthoring.ClearSelectedEntries();
                });
                inner.AddSeparator();
            }
        }

        private static void PanelRebuild(WidgetPanel panel)
        {
            panel.RebuildDynamic("rooms");
        }
    }
}
