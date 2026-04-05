#!/usr/bin/env python3.11
"""Build a tilemap-to-sprite-path index for the editor.

Maps each tilemap (by path_id) to the tile sprite PNG paths that
extract_tile_sprites.py produced. This lets the editor look up which
sprite image to draw for each tile without scanning the filesystem.

Reads from raw_dump/ for tilemap and GameObject data, and checks
the tile_sprites/ directory for actually extracted PNGs.

Produces:
  output/tilemap_sprite_index.json
"""
import json
import os

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT = os.path.join(SCRIPT_DIR, "output")
RAW = os.path.join(OUTPUT, "raw_dump")


def load_raw(source, obj_type):
    path = os.path.join(RAW, f"{source}__{obj_type}.json")
    if not os.path.exists(path):
        return []
    with open(path) as f:
        return json.load(f)


def main():
    index = {}

    for source, level_idx in [("level0", 0), ("level1", 1)]:
        tilemaps = load_raw(source, "Tilemap")
        game_objects = load_raw(source, "GameObject")

        # GO name lookup
        go_names = {}
        for go in game_objects:
            tt = go.get("typetree", {})
            go_names[go["path_id"]] = tt.get("m_Name", go.get("m_Name", "?"))

        for tm in tilemaps:
            tt = tm.get("typetree", {})
            if not tt:
                continue

            pid = tm["path_id"]
            go_pid = (tt.get("m_GameObject") or {}).get("m_PathID", 0)
            go_name = go_names.get(go_pid, "unknown")
            safe_name = go_name.replace(" ", "_").replace("(", "").replace(")", "").replace("/", "_")

            # Find which sprite indices are used by tiles
            used_indices = set()
            for pos, data in tt.get("m_Tiles", []):
                used_indices.add(data["m_TileSpriteIndex"])

            # Map used indices to actual tile_sprites/ paths
            sprites = {}
            for idx in sorted(used_indices):
                rel_path = f"tile_sprites/level{level_idx}/{safe_name}/{idx}.png"
                abs_path = os.path.join(OUTPUT, rel_path)
                if os.path.exists(abs_path):
                    sprites[str(idx)] = rel_path

            if sprites:
                index[str(pid)] = {
                    "name": go_name,
                    "level": level_idx,
                    "path_id": pid,
                    "sprites": sprites,
                }
                print(f"  level{level_idx}/{go_name}: {len(sprites)} sprite paths")

    out_path = os.path.join(OUTPUT, "tilemap_sprite_index.json")
    with open(out_path, "w") as f:
        json.dump(index, f, indent=2)
    print(f"\nWrote {out_path} ({len(index)} tilemaps)")


if __name__ == "__main__":
    main()
