using System;
using System.Collections.Generic;
using UnityEngine;

namespace IGTAPRandomizer
{
    public enum EntryKind
    {
        In,
        Out,
        Bidirectional,
    }

    [Flags]
    public enum AbilityRequirement
    {
        None = 0,
        Dash = 1 << 0,
        WallJump = 1 << 1,
        DoubleJump = 1 << 2,
        BlockSwap = 1 << 3,
    }

    [Serializable]
    public class EntryPoint
    {
        public string id;
        public Vector2Int tileLocal;
        public EntryKind kind;
        public AbilityRequirement required;
    }

    [Serializable]
    public class RoomDef
    {
        public string id;
        // Tile-space bounds on the course's shared Grid. xMin/yMin inclusive, xMax/yMax exclusive.
        public int tileXMin;
        public int tileYMin;
        public int tileXMax;
        public int tileYMax;

        public List<EntryPoint> entries = new List<EntryPoint>();
        // Tilemap GameObject names under the course this room's tiles live in.
        public List<string> containedTilemapNames = new List<string>();
        // Transform paths (relative to the course root) for non-tilemap GOs that move with the room.
        public List<string> containedChildPaths = new List<string>();

        public int TileWidth => Mathf.Max(0, tileXMax - tileXMin);
        public int TileHeight => Mathf.Max(0, tileYMax - tileYMin);

        public bool ContainsTile(int x, int y)
        {
            return x >= tileXMin && x < tileXMax && y >= tileYMin && y < tileYMax;
        }
    }

    [Serializable]
    public class CourseLayout
    {
        public int courseNumber;
        public List<RoomDef> rooms = new List<RoomDef>();
        public string startRoomId;
        public string endRoomId;

        public RoomDef FindRoom(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i].id == id) return rooms[i];
            return null;
        }
    }
}
