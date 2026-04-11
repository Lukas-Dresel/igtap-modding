using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Tilemaps;

namespace IGTAPRandomizer
{
    /// <summary>
    /// Cached references to the prototype course's root GameObject and the Grid its tiles live on.
    /// Resolved lazily and re-resolved if the scene swaps them out.
    /// </summary>
    public static class CourseRefs
    {
        public static courseScript Course { get; private set; }
        public static Grid Grid { get; private set; }

        public static bool TryResolve(int courseNumber)
        {
            if (Course != null && Grid != null) return true;

            var scripts = Object.FindObjectsByType<courseScript>(FindObjectsSortMode.None);
            if (scripts == null) return false;

            foreach (var cs in scripts)
            {
                if (cs == null) continue;
                if (cs.courseNumber != courseNumber) continue;

                // Find any child Tilemap and steal its layoutGrid (same pattern as MinimapData).
                var tm = cs.GetComponentInChildren<Tilemap>(true);
                if (tm == null || tm.layoutGrid == null) continue;

                Course = cs;
                Grid = tm.layoutGrid;
                return true;
            }
            return false;
        }

        public static void Invalidate()
        {
            Course = null;
            Grid = null;
        }
    }

    /// <summary>
    /// Room authoring state and input handling. Driven from Plugin.Update / Plugin.OnGUI.
    /// </summary>
    public static class RoomAuthoring
    {
        public enum Mode
        {
            None,
            PlaceFirstCorner,
            PlaceSecondCorner,
            PlaceEntryIn,
            PlaceEntryOut,
        }

        public static Mode CurrentMode;
        public static string SelectedRoomId;

        // Tile coordinate of the first corner while waiting for the second click.
        private static Vector2Int _firstCorner;

        // 1x1 white texture for drawing rect edges in OnGUI.
        private static Texture2D _whiteTex;

        // --- Public menu actions -------------------------------------------------

        public static void BeginNewRoom()
        {
            CurrentMode = Mode.PlaceFirstCorner;
            Plugin.Log.LogInfo("Authoring: click tile to set first corner.");
        }

        public static void BeginAddEntry(EntryKind kind)
        {
            if (string.IsNullOrEmpty(SelectedRoomId))
            {
                Plugin.Log.LogWarning("Authoring: select a room before adding entries.");
                return;
            }
            CurrentMode = kind == EntryKind.In ? Mode.PlaceEntryIn : Mode.PlaceEntryOut;
            Plugin.Log.LogInfo($"Authoring: click tile inside '{SelectedRoomId}' to place {kind} entry.");
        }

        public static void CancelPending()
        {
            CurrentMode = Mode.None;
        }

        public static void DeleteSelected()
        {
            if (string.IsNullOrEmpty(SelectedRoomId)) return;
            var layout = Plugin.Course4Layout;
            var room = layout.FindRoom(SelectedRoomId);
            if (room == null) return;
            layout.rooms.Remove(room);
            if (layout.startRoomId == SelectedRoomId) layout.startRoomId = null;
            if (layout.endRoomId == SelectedRoomId) layout.endRoomId = null;
            SelectedRoomId = null;
            Plugin.Log.LogInfo($"Authoring: deleted room '{room.id}'.");
        }

        public static void SetSelectedAsStart()
        {
            if (string.IsNullOrEmpty(SelectedRoomId)) return;
            Plugin.Course4Layout.startRoomId = SelectedRoomId;
        }

        public static void SetSelectedAsEnd()
        {
            if (string.IsNullOrEmpty(SelectedRoomId)) return;
            Plugin.Course4Layout.endRoomId = SelectedRoomId;
        }

        public static void ClearSelectedEntries()
        {
            var room = Plugin.Course4Layout.FindRoom(SelectedRoomId);
            if (room == null) return;
            room.entries.Clear();
        }

        // --- Per-frame tick ------------------------------------------------------

        public static void Tick()
        {
            if (!Plugin.AnnotateMode.Value)
            {
                if (CurrentMode != Mode.None) CurrentMode = Mode.None;
                return;
            }

            CourseRefs.TryResolve(Plugin.PrototypeCourse);
            if (CourseRefs.Grid == null) return;

            if (CurrentMode == Mode.None) return;

            // Only commit a click when we're actively placing something.
            if (Input.GetMouseButtonDown(0))
            {
                if (IsPointerOverUI()) return;
                HandleClick(MouseToTile());
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelPending();
            }
        }

        private static bool IsPointerOverUI()
        {
            // Avoid consuming clicks that are hitting the debug menu itself.
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private static void HandleClick(Vector2Int tile)
        {
            switch (CurrentMode)
            {
                case Mode.PlaceFirstCorner:
                    _firstCorner = tile;
                    CurrentMode = Mode.PlaceSecondCorner;
                    Plugin.Log.LogInfo($"Authoring: first corner at ({tile.x},{tile.y}). Click again for second.");
                    break;

                case Mode.PlaceSecondCorner:
                    CreateRoomFromCorners(_firstCorner, tile);
                    CurrentMode = Mode.None;
                    break;

                case Mode.PlaceEntryIn:
                case Mode.PlaceEntryOut:
                {
                    var room = Plugin.Course4Layout.FindRoom(SelectedRoomId);
                    if (room != null)
                    {
                        var kind = CurrentMode == Mode.PlaceEntryIn ? EntryKind.In : EntryKind.Out;
                        var local = new Vector2Int(tile.x - room.tileXMin, tile.y - room.tileYMin);
                        var entry = new EntryPoint
                        {
                            id = $"{kind.ToString().ToLowerInvariant()}_{room.entries.Count + 1}",
                            tileLocal = local,
                            kind = kind,
                            required = AbilityRequirement.None,
                        };
                        room.entries.Add(entry);
                        Plugin.Log.LogInfo($"Authoring: placed {kind} at room-local ({local.x},{local.y}) in '{room.id}'.");
                    }
                    CurrentMode = Mode.None;
                    break;
                }
            }
        }

        private static void CreateRoomFromCorners(Vector2Int a, Vector2Int b)
        {
            int minX = Mathf.Min(a.x, b.x);
            int minY = Mathf.Min(a.y, b.y);
            int maxX = Mathf.Max(a.x, b.x) + 1; // clicks are inclusive; store exclusive max
            int maxY = Mathf.Max(a.y, b.y) + 1;

            var layout = Plugin.Course4Layout;
            var room = new RoomDef
            {
                id = GenerateUniqueId(layout, "room"),
                tileXMin = minX,
                tileYMin = minY,
                tileXMax = maxX,
                tileYMax = maxY,
            };
            layout.rooms.Add(room);
            SelectedRoomId = room.id;
            Plugin.Log.LogInfo($"Authoring: created '{room.id}' tiles ({minX},{minY})-({maxX},{maxY}).");
        }

        private static string GenerateUniqueId(CourseLayout layout, string prefix)
        {
            int n = layout.rooms.Count + 1;
            while (true)
            {
                string candidate = $"{prefix}_{n}";
                if (layout.FindRoom(candidate) == null) return candidate;
                n++;
            }
        }

        // --- Mouse / world helpers ----------------------------------------------

        public static Vector2Int MouseToTile()
        {
            var cam = Camera.main;
            var grid = CourseRefs.Grid;
            if (cam == null || grid == null) return Vector2Int.zero;

            // For an orthographic 2D camera, Z distance equals -camera.position.z; any positive value
            // beyond the near plane works for a pure 2D scene, so use the absolute camera Z.
            float z = Mathf.Abs(cam.transform.position.z);
            var mouseScreen = new Vector3(Input.mousePosition.x, Input.mousePosition.y, z);
            var world = cam.ScreenToWorldPoint(mouseScreen);
            var cell = grid.WorldToCell(world);
            return new Vector2Int(cell.x, cell.y);
        }

        // --- OnGUI overlay -------------------------------------------------------

        public static void DrawOverlay()
        {
            if (!Plugin.AnnotateMode.Value) return;
            if (Event.current.type != EventType.Repaint) return;

            if (_whiteTex == null)
            {
                _whiteTex = new Texture2D(1, 1);
                _whiteTex.SetPixel(0, 0, Color.white);
                _whiteTex.Apply();
            }

            var cam = Camera.main;
            var grid = CourseRefs.Grid;
            if (cam == null || grid == null)
            {
                DrawStatusBar("Randomizer: waiting for course 4 grid…");
                return;
            }

            var layout = Plugin.Course4Layout;
            foreach (var room in layout.rooms)
            {
                bool selected = room.id == SelectedRoomId;
                bool isStart = room.id == layout.startRoomId;
                bool isEnd = room.id == layout.endRoomId;

                Color col;
                if (isStart) col = new Color(0.3f, 1f, 0.3f, 0.9f);
                else if (isEnd) col = new Color(0.3f, 0.6f, 1f, 0.9f);
                else if (selected) col = new Color(1f, 0.9f, 0.2f, 0.9f);
                else col = new Color(0.8f, 0.8f, 0.9f, 0.7f);

                DrawRoomRect(cam, grid, room.tileXMin, room.tileYMin, room.tileXMax, room.tileYMax, col,
                    selected ? 3f : 2f);
                DrawRoomLabel(cam, grid, room, isStart, isEnd, selected);
                DrawRoomEntries(cam, grid, room);
            }

            // Preview rect while placing the second corner.
            if (CurrentMode == Mode.PlaceSecondCorner)
            {
                var cur = MouseToTile();
                int minX = Mathf.Min(_firstCorner.x, cur.x);
                int minY = Mathf.Min(_firstCorner.y, cur.y);
                int maxX = Mathf.Max(_firstCorner.x, cur.x) + 1;
                int maxY = Mathf.Max(_firstCorner.y, cur.y) + 1;
                DrawRoomRect(cam, grid, minX, minY, maxX, maxY,
                    new Color(0.4f, 1f, 0.4f, 0.9f), 2f);
            }

            DrawStatusBar(BuildStatusText());
        }

        private static string BuildStatusText()
        {
            var layout = Plugin.Course4Layout;
            var cur = MouseToTile();
            string mode = CurrentMode.ToString();
            string selected = string.IsNullOrEmpty(SelectedRoomId) ? "(none)" : SelectedRoomId;
            return $"Randomizer annotate — tile ({cur.x},{cur.y}) | mode {mode} | selected {selected} | rooms {layout.rooms.Count}";
        }

        private static void DrawStatusBar(string text)
        {
            var rect = new Rect(8, 8, 720, 22);
            var bg = new Color(0f, 0f, 0f, 0.6f);
            GUI.color = bg;
            GUI.DrawTexture(rect, _whiteTex);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 6, rect.y + 2, rect.width - 12, rect.height), text);
            GUI.color = Color.white;
        }

        private static void DrawRoomRect(Camera cam, Grid grid, int xMin, int yMin, int xMax, int yMax,
            Color color, float thickness)
        {
            // Use tile-space corners; CellToWorld returns the min-corner world position of the cell.
            var worldMin = grid.CellToWorld(new Vector3Int(xMin, yMin, 0));
            var worldMax = grid.CellToWorld(new Vector3Int(xMax, yMax, 0));

            var c00 = cam.WorldToScreenPoint(new Vector3(worldMin.x, worldMin.y, 0));
            var c10 = cam.WorldToScreenPoint(new Vector3(worldMax.x, worldMin.y, 0));
            var c11 = cam.WorldToScreenPoint(new Vector3(worldMax.x, worldMax.y, 0));
            var c01 = cam.WorldToScreenPoint(new Vector3(worldMin.x, worldMax.y, 0));

            if (c00.z < 0 || c10.z < 0 || c11.z < 0 || c01.z < 0) return;

            Vector2 b = ToGui(c00);
            Vector2 r = ToGui(c10);
            Vector2 tr = ToGui(c11);
            Vector2 l = ToGui(c01);

            DrawLine(b, r, color, thickness);
            DrawLine(r, tr, color, thickness);
            DrawLine(tr, l, color, thickness);
            DrawLine(l, b, color, thickness);
        }

        private static void DrawRoomLabel(Camera cam, Grid grid, RoomDef room, bool isStart, bool isEnd, bool selected)
        {
            var worldMin = grid.CellToWorld(new Vector3Int(room.tileXMin, room.tileYMax, 0));
            var screen = cam.WorldToScreenPoint(new Vector3(worldMin.x, worldMin.y, 0));
            if (screen.z < 0) return;
            var gui = ToGui(screen);
            string tag = "";
            if (isStart) tag = " [START]";
            else if (isEnd) tag = " [END]";
            else if (selected) tag = " *";
            string text = $"{room.id}{tag}";
            var rect = new Rect(gui.x + 2, gui.y - 18, 220, 18);
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(rect, _whiteTex);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 4, rect.y, rect.width - 8, rect.height), text);
        }

        private static void DrawRoomEntries(Camera cam, Grid grid, RoomDef room)
        {
            foreach (var entry in room.entries)
            {
                int wx = room.tileXMin + entry.tileLocal.x;
                int wy = room.tileYMin + entry.tileLocal.y;
                var worldCenter = grid.CellToWorld(new Vector3Int(wx, wy, 0));
                var half = grid.cellSize * 0.5f;
                var center = new Vector3(worldCenter.x + half.x, worldCenter.y + half.y, 0);
                var screen = cam.WorldToScreenPoint(center);
                if (screen.z < 0) continue;
                var gui = ToGui(screen);

                Color col = entry.kind == EntryKind.In ? new Color(0.3f, 1f, 0.3f, 0.9f)
                          : entry.kind == EntryKind.Out ? new Color(1f, 0.4f, 0.4f, 0.9f)
                          : new Color(0.9f, 0.9f, 0.3f, 0.9f);

                var dot = new Rect(gui.x - 5, gui.y - 5, 10, 10);
                GUI.color = col;
                GUI.DrawTexture(dot, _whiteTex);
                GUI.color = Color.white;
            }
        }

        private static Vector2 ToGui(Vector3 screenPoint)
        {
            // Unity Screen coords have origin at bottom-left; OnGUI uses top-left.
            return new Vector2(screenPoint.x, Screen.height - screenPoint.y);
        }

        private static void DrawLine(Vector2 a, Vector2 b, Color color, float thickness)
        {
            GUI.color = color;
            float length = Vector2.Distance(a, b);
            if (length < 0.5f) { GUI.color = Color.white; return; }
            float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, a);
            GUI.DrawTexture(new Rect(a.x, a.y - thickness * 0.5f, length, thickness), _whiteTex);
            GUI.matrix = matrix;
            GUI.color = Color.white;
        }
    }
}
