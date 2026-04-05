#!/usr/bin/env python3.11
"""Extract all tilemap data from IGTAP level files to JSON."""
import json
import os

import UnityPy

GAME_DATA = "/home/honululu/.local/share/Steam/steamapps/common/IGTAP an Incremental Game That's Also a Platformer Demo/IGTAP_Data"
OUTPUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "output")
os.makedirs(OUTPUT_DIR, exist_ok=True)


def get_gameobject_name(env, tilemap_tree):
    """Resolve the parent GameObject name for a Tilemap."""
    try:
        go_ref = tilemap_tree.get("m_GameObject")
        if go_ref:
            file_id = go_ref.get("m_FileID", 0)
            path_id = go_ref.get("m_PathID", 0)
            if path_id:
                for obj in env.objects:
                    if obj.path_id == path_id and obj.type.name == "GameObject":
                        data = obj.read()
                        return data.m_Name
    except Exception:
        pass
    return None


def extract_tilemap(obj, env):
    """Extract full tilemap data from a Tilemap object."""
    tree = obj.read_typetree()

    go_name = get_gameobject_name(env, tree)

    tilemap = {
        "path_id": obj.path_id,
        "gameobject_name": go_name,
        "enabled": tree.get("m_Enabled", True),
        "origin": tree.get("m_Origin"),
        "size": tree.get("m_Size"),
        "tile_anchor": tree.get("m_TileAnchor"),
        "tile_orientation": tree.get("m_TileOrientation"),
        "animation_frame_rate": tree.get("m_AnimationFrameRate"),
        "color": tree.get("m_Color"),
    }

    # Extract tiles: list of [position, tile_data] pairs
    tiles = tree.get("m_Tiles", [])
    tilemap["tile_count"] = len(tiles)
    tilemap["tiles"] = []
    for tile in tiles:
        pos, data = tile
        tilemap["tiles"].append({
            "x": pos["x"],
            "y": pos["y"],
            "z": pos["z"],
            "tile_index": data["m_TileIndex"],
            "sprite_index": data["m_TileSpriteIndex"],
            "matrix_index": data["m_TileMatrixIndex"],
            "color_index": data["m_TileColorIndex"],
            "flags": data.get("m_AllTileFlags", 0),
        })

    # Sprite array (references to Sprite objects)
    sprite_arr = tree.get("m_TileSpriteArray", [])
    tilemap["sprite_refs"] = []
    for ref in sprite_arr:
        if isinstance(ref, dict):
            tilemap["sprite_refs"].append({
                "file_id": ref.get("m_FileID", 0),
                "path_id": ref.get("m_PathID", 0),
            })
        else:
            tilemap["sprite_refs"].append(ref)

    # Matrix array
    tilemap["matrix_array"] = tree.get("m_TileMatrixArray", [])

    # Color array
    tilemap["color_array"] = tree.get("m_TileColorArray", [])

    # Asset array (tile type references)
    tilemap["tile_asset_refs"] = []
    for ref in tree.get("m_TileAssetArray", []):
        if isinstance(ref, dict):
            tilemap["tile_asset_refs"].append({
                "file_id": ref.get("m_FileID", 0),
                "path_id": ref.get("m_PathID", 0),
            })
        else:
            tilemap["tile_asset_refs"].append(ref)

    return tilemap


def main():
    print(f"Loading assets from {GAME_DATA} ...")
    env = UnityPy.load(GAME_DATA)

    # Group tilemaps by which level file they come from
    tilemaps_by_file = {}
    for obj in env.objects:
        if obj.type.name != "Tilemap":
            continue
        try:
            tm = extract_tilemap(obj, env)
            src = getattr(obj, "assets_file", None)
            src_name = getattr(src, "name", "unknown") if src else "unknown"
            tilemaps_by_file.setdefault(src_name, []).append(tm)
            print(f"  Tilemap path_id={obj.path_id} name={tm['gameobject_name']!r} tiles={tm['tile_count']}")
        except Exception as e:
            print(f"  ERROR reading tilemap path_id={obj.path_id}: {e}")

    # Write per-file outputs
    for src_name, tilemaps in tilemaps_by_file.items():
        safe_name = src_name.replace("/", "_").replace("\\", "_")
        out_path = os.path.join(OUTPUT_DIR, f"tilemaps_{safe_name}.json")
        with open(out_path, "w") as f:
            json.dump(tilemaps, f, indent=2, default=str)
        print(f"Wrote {out_path} ({len(tilemaps)} tilemaps)")

    # Also write a combined output
    all_tilemaps = []
    for tilemaps in tilemaps_by_file.values():
        all_tilemaps.extend(tilemaps)
    out_path = os.path.join(OUTPUT_DIR, "tilemaps_all.json")
    with open(out_path, "w") as f:
        json.dump(all_tilemaps, f, indent=2, default=str)
    print(f"Wrote {out_path} ({len(all_tilemaps)} tilemaps total)")


if __name__ == "__main__":
    main()
