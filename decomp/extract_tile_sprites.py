#!/usr/bin/env python3.11
"""Extract all tile sprites referenced by tilemaps, organized for the level editor.

Creates:
  output/tile_sprites/<level>/<layer_name>/<sprite_index>.png
  output/tile_sprite_map.json  (mapping for the editor to load)
"""
import json
import os
import sys

import UnityPy

GAME_DATA = "/home/honululu/.local/share/Steam/steamapps/common/IGTAP an Incremental Game That's Also a Platformer Demo/IGTAP_Data"
OUTPUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "output")
SPRITES_DIR = os.path.join(OUTPUT_DIR, "tile_sprites")


def resolve_sprite(env, assets_file, pptr_data):
    """Resolve a PPtr<Sprite> from a tilemap's m_TileSpriteArray entry."""
    data = pptr_data.get("m_Data", pptr_data)
    fid = data.get("m_FileID", 0)
    pid = data.get("m_PathID", 0)
    if pid == 0:
        return None, None, None

    # Resolve external file: fid=0 means same file, fid>0 means externals[fid-1]
    if fid > 0 and fid <= len(assets_file.externals):
        ext_name = assets_file.externals[fid - 1].name
    else:
        ext_name = assets_file.name

    # Find the target file in the environment
    target_file = None
    for fname, f in env.files.items():
        if hasattr(f, "name") and f.name == ext_name:
            target_file = f
            break
        if ext_name in fname:
            target_file = f
            break

    if not target_file:
        return None, None, None

    for obj in target_file.objects.values():
        if obj.path_id == pid:
            try:
                sprite = obj.read()
                return sprite, getattr(sprite, "m_Name", f"sprite_{pid}"), sprite.image
            except Exception:
                return None, None, None

    return None, None, None


def main():
    print(f"Loading assets from {GAME_DATA} ...")
    env = UnityPy.load(GAME_DATA)

    sprite_map = {}  # {level_idx: {layer_name: {sprite_idx: "relative/path.png"}}}
    total_sprites = 0
    cached_sprites = {}  # (file_name, path_id) -> saved path, to avoid re-extracting dupes

    for obj in env.objects:
        if obj.type.name != "Tilemap":
            continue

        sf = obj.assets_file
        level_name = sf.name  # "level0" or "level1"
        level_idx = 0 if "0" in level_name else 1

        tree = obj.read_typetree()

        # Get parent GameObject name
        go_name = "unknown"
        go_ref = tree.get("m_GameObject", {})
        go_pid = go_ref.get("m_PathID", 0)
        if go_pid:
            for o2 in sf.objects.values():
                if o2.path_id == go_pid and o2.type.name == "GameObject":
                    try:
                        go_name = o2.read().m_Name
                    except Exception:
                        pass
                    break

        safe_name = go_name.replace(" ", "_").replace("(", "").replace(")", "").replace("/", "_")
        sprite_arr = tree.get("m_TileSpriteArray", [])

        # Find which sprite indices are actually used by tiles
        tiles = tree.get("m_Tiles", [])
        used_indices = set()
        for pos, tdata in tiles:
            used_indices.add(tdata["m_TileSpriteIndex"])

        layer_dir = os.path.join(SPRITES_DIR, f"level{level_idx}", safe_name)
        os.makedirs(layer_dir, exist_ok=True)

        layer_map = {}
        extracted = 0

        for idx in sorted(used_indices):
            if idx >= len(sprite_arr):
                continue

            entry = sprite_arr[idx]
            data = entry.get("m_Data", entry)
            fid = data.get("m_FileID", 0)
            pid = data.get("m_PathID", 0)

            if pid == 0:
                continue

            # Check cache
            cache_key = (fid, pid, level_name)
            if cache_key in cached_sprites:
                # Copy/link the cached path
                src_path = cached_sprites[cache_key]
                out_rel = f"tile_sprites/level{level_idx}/{safe_name}/{idx}.png"
                out_path = os.path.join(OUTPUT_DIR, out_rel)
                if not os.path.exists(out_path):
                    # Just copy the cached image
                    import shutil
                    shutil.copy2(os.path.join(OUTPUT_DIR, src_path), out_path)
                layer_map[str(idx)] = out_rel
                extracted += 1
                continue

            sprite, name, img = resolve_sprite(env, sf, entry)
            if img is None:
                continue

            out_rel = f"tile_sprites/level{level_idx}/{safe_name}/{idx}.png"
            out_path = os.path.join(OUTPUT_DIR, out_rel)
            img.save(out_path)
            layer_map[str(idx)] = out_rel
            cached_sprites[cache_key] = out_rel
            extracted += 1

        if extracted > 0:
            sprite_map.setdefault(str(level_idx), {})[go_name] = layer_map
            total_sprites += extracted
            print(f"  level{level_idx}/{safe_name}: {extracted} sprites ({len(used_indices)} used of {len(sprite_arr)})")

    # Write the sprite map
    map_path = os.path.join(OUTPUT_DIR, "tile_sprite_map.json")
    with open(map_path, "w") as f:
        json.dump(sprite_map, f, indent=2)
    print(f"\nWrote {map_path}")
    print(f"Total: {total_sprites} tile sprites extracted")


if __name__ == "__main__":
    main()
