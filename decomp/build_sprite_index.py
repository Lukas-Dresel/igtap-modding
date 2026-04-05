#!/usr/bin/env python3.11
"""Build a compact sprite metadata index from raw dump data.

Reads all *__Sprite.json files from raw_dump/ and produces a single
sprite_index.json keyed by path_id with compact metadata fields.

Produces:
  output/raw_dump/sprite_index.json
"""
import json
import os

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(SCRIPT_DIR, "output", "raw_dump")


def main():
    index = {}

    for fname in sorted(os.listdir(RAW)):
        if not fname.endswith("__Sprite.json"):
            continue
        if "Renderer" in fname or "Atlas" in fname:
            continue

        path = os.path.join(RAW, fname)
        with open(path) as f:
            sprites = json.load(f)

        added = 0
        for s in sprites:
            pid = s["path_id"]
            tt = s.get("typetree", {})
            if not tt:
                continue

            rect = tt.get("m_Rect", {})
            pivot = tt.get("m_Pivot", {})

            index[str(pid)] = {
                "n": tt.get("m_Name", s.get("m_Name", "")),
                "w": rect.get("width", 0),
                "h": rect.get("height", 0),
                "p": tt.get("m_PixelsToUnits", 1.0),
                "px": pivot.get("x", 0.5),
                "py": pivot.get("y", 0.5),
            }
            added += 1

        print(f"  {fname}: {added} sprites")

    out_path = os.path.join(RAW, "sprite_index.json")
    with open(out_path, "w") as f:
        json.dump(index, f, separators=(",", ":"))
    print(f"\nWrote {out_path} ({len(index)} sprites)")


if __name__ == "__main__":
    main()
