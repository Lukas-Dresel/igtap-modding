#!/usr/bin/env python3.11
"""Extract all sprite/texture images from IGTAP assets."""
import os

import UnityPy

GAME_DATA = "/home/honululu/.local/share/Steam/steamapps/common/IGTAP an Incremental Game That's Also a Platformer Demo/IGTAP_Data"
OUTPUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "output")
SPRITES_DIR = os.path.join(OUTPUT_DIR, "sprites")
TEXTURES_DIR = os.path.join(OUTPUT_DIR, "textures")


def main():
    os.makedirs(SPRITES_DIR, exist_ok=True)
    os.makedirs(TEXTURES_DIR, exist_ok=True)

    print(f"Loading assets from {GAME_DATA} ...")
    env = UnityPy.load(GAME_DATA)

    # Extract Texture2D images
    tex_count = 0
    for obj in env.objects:
        if obj.type.name == "Texture2D":
            try:
                data = obj.read()
                if data.m_Width > 0 and data.m_Height > 0:
                    img = data.image
                    name = data.m_Name or f"texture_{obj.path_id}"
                    # Sanitize filename
                    name = name.replace("/", "_").replace("\\", "_")
                    out_path = os.path.join(TEXTURES_DIR, f"{name}.png")
                    img.save(out_path)
                    tex_count += 1
                    print(f"  Texture: {name} ({data.m_Width}x{data.m_Height})")
            except Exception as e:
                print(f"  ERROR texture path_id={obj.path_id}: {e}")

    # Extract Sprite images
    sprite_count = 0
    for obj in env.objects:
        if obj.type.name == "Sprite":
            try:
                data = obj.read()
                img = data.image
                name = data.m_Name or f"sprite_{obj.path_id}"
                name = name.replace("/", "_").replace("\\", "_")
                out_path = os.path.join(SPRITES_DIR, f"{name}.png")
                img.save(out_path)
                sprite_count += 1
                if sprite_count % 100 == 0:
                    print(f"  ... extracted {sprite_count} sprites")
            except Exception as e:
                # Many sprites share texture data, some may fail
                pass

    print(f"\nDone: {tex_count} textures -> {TEXTURES_DIR}")
    print(f"Done: {sprite_count} sprites -> {SPRITES_DIR}")


if __name__ == "__main__":
    main()
