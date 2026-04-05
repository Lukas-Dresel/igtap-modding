#!/usr/bin/env python3.11
"""Parse and pretty-print MenuCourseData.txt from IGTAP."""
import json
import os

GAME_DATA = "/home/honululu/.local/share/Steam/steamapps/common/IGTAP an Incremental Game That's Also a Platformer Demo/IGTAP_Data"
OUTPUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "output")
os.makedirs(OUTPUT_DIR, exist_ok=True)

COURSE_DATA_PATH = os.path.join(GAME_DATA, "StreamingAssets", "MenuCourseData.txt")


def main():
    print(f"Reading {COURSE_DATA_PATH} ...")
    with open(COURSE_DATA_PATH) as f:
        data = json.load(f)

    # Write pretty JSON
    out_path = os.path.join(OUTPUT_DIR, "MenuCourseData_pretty.json")
    with open(out_path, "w") as f:
        json.dump(data, f, indent=2)
    print(f"Wrote {out_path}")

    # Write a human-readable analysis
    txt_path = os.path.join(OUTPUT_DIR, "MenuCourseData_analysis.txt")
    with open(txt_path, "w") as f:
        f.write("=== MenuCourseData.txt Analysis ===\n\n")

        f.write("--- Top-level keys ---\n")
        for k, v in data.items():
            if isinstance(v, list):
                f.write(f"  {k}: list[{len(v)}]\n")
            elif isinstance(v, dict):
                f.write(f"  {k}: dict with keys {list(v.keys())[:10]}\n")
            else:
                f.write(f"  {k}: {v}\n")

        # Player path analysis
        if "_bestPlayerPath" in data:
            path = data["_bestPlayerPath"]
            f.write(f"\n--- Best Player Path ---\n")
            f.write(f"  Length: {len(path)} coordinate values ({len(path)//2} points)\n")
            xs = path[0::2]
            ys = path[1::2]
            if xs and ys:
                f.write(f"  X range: [{min(xs):.1f}, {max(xs):.1f}]\n")
                f.write(f"  Y range: [{min(ys):.1f}, {max(ys):.1f}]\n")
                f.write(f"  First 5 points: {list(zip(xs[:5], ys[:5]))}\n")
                f.write(f"  Last 5 points: {list(zip(xs[-5:], ys[-5:]))}\n")

        # Box positions
        if "_boxPositions" in data:
            f.write(f"\n--- Box Positions ---\n")
            for i, pos in enumerate(data["_boxPositions"]):
                f.write(f"  Box {i}: ({pos['x']}, {pos['y']})\n")

        # Box costs
        if "_boxCosts" in data:
            f.write(f"\n--- Box Costs ---\n")
            for i, cost in enumerate(data["_boxCosts"]):
                f.write(f"  Box {i}: {cost}\n")

        # Upgrades
        if "_localUpgradeDict" in data:
            f.write(f"\n--- Local Upgrades ---\n")
            for entry in data["_localUpgradeDict"]:
                if isinstance(entry, dict):
                    f.write(f"  {json.dumps(entry)}\n")
                else:
                    f.write(f"  {entry}\n")

        f.write(f"\n--- Misc ---\n")
        for k in ["_bestPathLength", "_bestPathTime", "_cloneCount", "_reward",
                   "_rewardTier", "_costTier", "_trippedBreaker"]:
            if k in data:
                f.write(f"  {k}: {data[k]}\n")

    print(f"Wrote {txt_path}")


if __name__ == "__main__":
    main()
