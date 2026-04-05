#!/usr/bin/env python3.11
"""Dump a summary of all Unity assets in the game data directory."""
import json
import sys
import os

import UnityPy

GAME_DATA = "/home/honululu/.local/share/Steam/steamapps/common/IGTAP an Incremental Game That's Also a Platformer Demo/IGTAP_Data"
OUTPUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "output")
os.makedirs(OUTPUT_DIR, exist_ok=True)

def main():
    print(f"Loading assets from {GAME_DATA} ...")
    env = UnityPy.load(GAME_DATA)

    result = {}

    # Object type counts
    type_counts = {}
    for obj in env.objects:
        t = obj.type.name
        type_counts[t] = type_counts.get(t, 0) + 1
    result["object_type_counts"] = dict(sorted(type_counts.items(), key=lambda x: -x[1]))
    result["total_objects"] = sum(type_counts.values())

    # Container paths
    result["container_paths"] = {
        path: obj.type.name for path, obj in sorted(env.container.items())
    }

    # MonoScript names (tells us what scripts exist)
    scripts = []
    for obj in env.objects:
        if obj.type.name == "MonoScript":
            try:
                data = obj.read()
                scripts.append({
                    "name": data.m_Name,
                    "namespace": getattr(data, "m_Namespace", ""),
                    "class_name": getattr(data, "m_ClassName", data.m_Name),
                    "path_id": obj.path_id,
                })
            except Exception as e:
                scripts.append({"path_id": obj.path_id, "error": str(e)})
    result["mono_scripts"] = scripts

    # Texture2D names
    textures = []
    for obj in env.objects:
        if obj.type.name == "Texture2D":
            try:
                data = obj.read()
                textures.append({
                    "name": data.m_Name,
                    "width": data.m_Width,
                    "height": data.m_Height,
                    "format": str(data.m_TextureFormat),
                    "path_id": obj.path_id,
                })
            except Exception as e:
                textures.append({"path_id": obj.path_id, "error": str(e)})
    result["textures"] = textures

    # Sprite names
    sprites = []
    for obj in env.objects:
        if obj.type.name == "Sprite":
            try:
                data = obj.read()
                sprites.append({
                    "name": data.m_Name,
                    "path_id": obj.path_id,
                })
            except Exception as e:
                sprites.append({"path_id": obj.path_id, "error": str(e)})
    result["sprites"] = [s["name"] for s in sprites if "name" in s]
    result["sprite_count"] = len(sprites)

    # AudioClip names
    audio = []
    for obj in env.objects:
        if obj.type.name == "AudioClip":
            try:
                data = obj.read()
                audio.append({"name": data.m_Name, "path_id": obj.path_id})
            except Exception as e:
                audio.append({"path_id": obj.path_id, "error": str(e)})
    result["audio_clips"] = audio

    # AnimationClip names
    anims = []
    for obj in env.objects:
        if obj.type.name == "AnimationClip":
            try:
                data = obj.read()
                anims.append({"name": data.m_Name, "path_id": obj.path_id})
            except Exception as e:
                anims.append({"path_id": obj.path_id, "error": str(e)})
    result["animation_clips"] = anims

    out_path = os.path.join(OUTPUT_DIR, "assets_summary.json")
    with open(out_path, "w") as f:
        json.dump(result, f, indent=2, default=str)
    print(f"Wrote {out_path}")

    # Also write a human-readable summary
    txt_path = os.path.join(OUTPUT_DIR, "assets_summary.txt")
    with open(txt_path, "w") as f:
        f.write("=== IGTAP Asset Summary ===\n\n")
        f.write("Object type counts:\n")
        for t, c in result["object_type_counts"].items():
            f.write(f"  {t}: {c}\n")
        f.write(f"\nTotal objects: {result['total_objects']}\n")
        f.write(f"\n--- Container paths ({len(result['container_paths'])}) ---\n")
        for p, t in result["container_paths"].items():
            f.write(f"  {p} -> {t}\n")
        f.write(f"\n--- MonoScripts ({len(scripts)}) ---\n")
        for s in scripts:
            if "name" in s:
                ns = s.get("namespace", "")
                f.write(f"  {ns + '.' if ns else ''}{s['name']}\n")
        f.write(f"\n--- Textures ({len(textures)}) ---\n")
        for t in textures:
            if "name" in t:
                f.write(f"  {t['name']} ({t['width']}x{t['height']}, {t['format']})\n")
        f.write(f"\n--- Sprites ({result['sprite_count']}) ---\n")
        for name in result["sprites"][:50]:
            f.write(f"  {name}\n")
        if result["sprite_count"] > 50:
            f.write(f"  ... ({result['sprite_count'] - 50} more)\n")
        f.write(f"\n--- Audio ({len(audio)}) ---\n")
        for a in audio:
            if "name" in a:
                f.write(f"  {a['name']}\n")
        f.write(f"\n--- Animations ({len(anims)}) ---\n")
        for a in anims:
            if "name" in a:
                f.write(f"  {a['name']}\n")
    print(f"Wrote {txt_path}")


if __name__ == "__main__":
    main()
