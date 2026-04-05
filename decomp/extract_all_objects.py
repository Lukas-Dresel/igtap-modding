#!/usr/bin/env python3.11
"""Extract EVERY object from EVERY Unity type in the game data.

Dumps the full typetree for every single object in every file.
No filtering, no selecting, no deciding what matters. Everything.
"""
import json
import os
import traceback

import UnityPy

GAME_DATA = "/home/honululu/.local/share/Steam/steamapps/common/IGTAP an Incremental Game That's Also a Platformer Demo/IGTAP_Data"
OUTPUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "output", "raw_dump")
os.makedirs(OUTPUT_DIR, exist_ok=True)


def make_serializable(obj):
    """Recursively convert Unity objects to JSON-serializable dicts."""
    if isinstance(obj, dict):
        return {k: make_serializable(v) for k, v in obj.items()}
    elif isinstance(obj, (list, tuple)):
        return [make_serializable(v) for v in obj]
    elif isinstance(obj, bytes):
        return f"<bytes:{len(obj)}>"
    elif isinstance(obj, (int, float, str, bool)):
        return obj
    elif obj is None:
        return None
    else:
        return str(obj)


def main():
    print(f"Loading {GAME_DATA} ...")
    env = UnityPy.load(GAME_DATA)

    # Dump every object from every file
    for fname, sf in env.files.items():
        if not hasattr(sf, 'objects'):
            continue

        safe_fname = sf.name if hasattr(sf, 'name') else fname
        safe_fname = safe_fname.replace("/", "_").replace("\\", "_").replace(".", "_")

        file_objects = {}  # type_name -> list of objects
        obj_count = 0
        err_count = 0

        for obj in sf.objects.values():
            type_name = obj.type.name
            entry = {
                "path_id": obj.path_id,
                "type": type_name,
                "byte_size": obj.byte_size,
            }

            # Try to read via typetree (gives ALL fields)
            try:
                tree = obj.read_typetree()
                entry["typetree"] = make_serializable(tree)
            except Exception:
                pass

            # Try to read via OOP API (gives parsed fields)
            try:
                data = obj.read()
                # Extract common fields
                if hasattr(data, 'm_Name'):
                    entry["m_Name"] = data.m_Name
                if hasattr(data, 'm_GameObject'):
                    go = data.m_GameObject
                    if hasattr(go, 'path_id'):
                        entry["m_GameObject_pathID"] = go.path_id
                # For sprites, get image size
                if type_name == "Sprite":
                    try:
                        img = data.image
                        entry["image_size"] = [img.width, img.height]
                    except:
                        pass
                if type_name == "Texture2D":
                    entry["texture_size"] = [data.m_Width, data.m_Height]
                    entry["texture_format"] = str(data.m_TextureFormat)
            except Exception:
                pass

            # Also dump raw data size
            try:
                raw = obj.get_raw_data()
                entry["raw_size"] = len(raw)
            except:
                pass

            file_objects.setdefault(type_name, []).append(entry)
            obj_count += 1

        # Write per-file, per-type dumps
        for type_name, objects in file_objects.items():
            safe_type = type_name.replace(" ", "_").replace("/", "_")
            out_path = os.path.join(OUTPUT_DIR, f"{safe_fname}__{safe_type}.json")
            try:
                with open(out_path, "w") as f:
                    json.dump(objects, f, indent=2, default=str)
            except Exception as e:
                print(f"  ERROR writing {out_path}: {e}")
                err_count += 1

        print(f"  {safe_fname}: {obj_count} objects, {len(file_objects)} types, {err_count} errors")

    # Summary
    print(f"\nAll raw data dumped to {OUTPUT_DIR}/")
    total_files = len([f for f in os.listdir(OUTPUT_DIR) if f.endswith('.json')])
    total_size = sum(os.path.getsize(os.path.join(OUTPUT_DIR, f)) for f in os.listdir(OUTPUT_DIR) if f.endswith('.json'))
    print(f"  {total_files} files, {total_size / 1024 / 1024:.1f} MB total")


if __name__ == "__main__":
    main()
