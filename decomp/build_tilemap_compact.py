#!/usr/bin/env python3.11
"""Build compact tilemap data from raw dump for the editor.

Reads level1__Tilemap.json from raw_dump/ and produces a compact
representation with only the fields the raw editor needs.

Produces:
  output/raw_dump/tilemap_compact.json
"""
import json
import os

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(SCRIPT_DIR, "output", "raw_dump")


def compact_tilemap(tm):
    tt = tm.get("typetree", {})
    if not tt:
        return None

    go_ref = tt.get("m_GameObject", {})

    # Compact tiles: [x, y, spriteIdx, matIdx, colorIdx, allFlags]
    tiles = []
    for pos, data in tt.get("m_Tiles", []):
        tiles.append([
            pos["x"], pos["y"],
            data["m_TileSpriteIndex"],
            data["m_TileMatrixIndex"],
            data["m_TileColorIndex"],
            data.get("m_AllTileFlags", 0),
        ])

    # Compact matrices: [e00, e01, e10, e11] (2D-relevant components only)
    mats = []
    for m in tt.get("m_TileMatrixArray", []):
        d = m.get("m_Data", m)
        mats.append([d.get("e00", 1), d.get("e01", 0), d.get("e10", 0), d.get("e11", 1)])

    # Sprite refs: {fid, pid}
    spr = []
    for ref in tt.get("m_TileSpriteArray", []):
        d = ref.get("m_Data", ref)
        spr.append({"fid": d.get("m_FileID", 0), "pid": d.get("m_PathID", 0)})

    # Color array
    colors = tt.get("m_TileColorArray", [])

    return {
        "pid": tm["path_id"],
        "go": go_ref.get("m_PathID", 0),
        "enabled": tt.get("m_Enabled", 1),
        "color": tt.get("m_Color", {"r": 1, "g": 1, "b": 1, "a": 1}),
        "tiles": tiles,
        "mats": mats,
        "spr": spr,
        "colors": colors,
    }


def main():
    src = os.path.join(RAW, "level1__Tilemap.json")
    if not os.path.exists(src):
        print(f"ERROR: {src} not found. Run extract_all_objects.py first.")
        return

    with open(src) as f:
        tilemaps = json.load(f)

    result = []
    for tm in tilemaps:
        entry = compact_tilemap(tm)
        if entry:
            result.append(entry)
            print(f"  tilemap pid={entry['pid']} go={entry['go']}: {len(entry['tiles'])} tiles")

    out_path = os.path.join(RAW, "tilemap_compact.json")
    with open(out_path, "w") as f:
        json.dump(result, f, separators=(",", ":"))
    print(f"\nWrote {out_path} ({len(result)} tilemaps)")


if __name__ == "__main__":
    main()
