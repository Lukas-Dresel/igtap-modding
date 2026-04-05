#!/usr/bin/env python3.11
"""Build all editor data files from the raw dump.

Reads ONLY from output/raw_dump/. No UnityPy calls.
Produces:
  output/layer_index.json
  output/layers/*.json
  output/scene_objects_level{0,1}.json
"""
import json
import math
import os

RAW = os.path.join(os.path.dirname(os.path.abspath(__file__)), "output", "raw_dump")
OUTPUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "output")
SAVE_DIR = "/home/honululu/.config/unity3d/Pepper tango games/IGTAP/Savedata"


def load_raw(source, obj_type):
    path = os.path.join(RAW, f"{source}__{obj_type}.json")
    if not os.path.exists(path):
        return []
    with open(path) as f:
        return json.load(f)


def quat_to_z_angle(q):
    qx = q.get("x", 0); qy = q.get("y", 0)
    qz = q.get("z", 0); qw = q.get("w", 1)
    return math.degrees(math.atan2(2*(qw*qz + qx*qy), 1 - 2*(qy*qy + qz*qz)))


def build_layers(source, level_idx):
    """Build tilemap layer files from raw dump."""
    tilemaps = load_raw(source, "Tilemap")
    grids = load_raw(source, "Grid")
    game_objects = load_raw(source, "GameObject")
    xforms = load_raw(source, "Transform") + load_raw(source, "RectTransform")

    # GO name lookup
    go_names = {}
    for go in game_objects:
        tt = go.get("typetree", {})
        go_names[go["path_id"]] = tt.get("m_Name", go.get("m_Name", "?"))

    # Transform lookup: path_id -> typetree
    t_lookup = {}
    go_to_t = {}
    for t in xforms:
        tt = t.get("typetree", {})
        t_lookup[t["path_id"]] = tt
        go_pid = (tt.get("m_GameObject") or {}).get("m_PathID", 0)
        if go_pid:
            go_to_t[go_pid] = t["path_id"]

    # Grid lookup: go_path_id -> cellSize
    grid_cells = {}
    for g in grids:
        tt = g.get("typetree", {})
        go_pid = (tt.get("m_GameObject") or {}).get("m_PathID", 0)
        cs = tt.get("m_CellSize", {})
        grid_cells[go_pid] = cs.get("x", 32)

    def find_grid_cellsize(go_pid):
        tid = go_to_t.get(go_pid)
        visited = set()
        while tid and tid not in visited:
            visited.add(tid)
            tt = t_lookup.get(tid, {})
            parent_go = (tt.get("m_GameObject") or {}).get("m_PathID", 0)
            if parent_go in grid_cells:
                return grid_cells[parent_go]
            father = tt.get("m_Father") or {}
            tid = father.get("m_PathID", 0)
        return 32

    layers = []
    os.makedirs(os.path.join(OUTPUT, "layers"), exist_ok=True)

    for tm in tilemaps:
        tt = tm.get("typetree", {})
        if not tt:
            continue

        go_pid = (tt.get("m_GameObject") or {}).get("m_PathID", 0)
        go_name = go_names.get(go_pid, "unknown")

        cell_size = find_grid_cellsize(go_pid)
        tile_size = cell_size / 32.0

        raw_mats = tt.get("m_TileMatrixArray", [])
        matrices = []
        for m in raw_mats:
            d = m.get("m_Data", m)
            matrices.append([d.get("e00", 1), d.get("e01", 0), d.get("e10", 0), d.get("e11", 1)])

        raw_tiles = tt.get("m_Tiles", [])
        tiles = []
        for pos, data in raw_tiles:
            tiles.append({
                "x": pos["x"], "y": pos["y"], "z": pos["z"],
                "ti": data["m_TileIndex"], "si": data["m_TileSpriteIndex"],
                "mi": data["m_TileMatrixIndex"], "ci": data["m_TileColorIndex"],
                "fl": data.get("m_AllTileFlags", 0),
            })

        safe = go_name.replace(" ", "_").replace("(", "").replace(")", "").replace("/", "_")
        layer_file = f"layers/level{level_idx}_{safe}.json"

        with open(os.path.join(OUTPUT, layer_file), "w") as f:
            json.dump({
                "name": go_name, "path_id": tm["path_id"],
                "tile_count": len(tiles), "tile_size": tile_size,
                "origin": tt.get("m_Origin"), "size": tt.get("m_Size"),
                "matrices": matrices, "tiles": tiles,
            }, f, separators=(",", ":"))

        xs = [t["x"] for t in tiles] if tiles else [0]
        ys = [t["y"] for t in tiles] if tiles else [0]
        layers.append({
            "level": level_idx, "name": go_name, "path_id": tm["path_id"],
            "file": layer_file, "tile_count": len(tiles), "tile_size": tile_size,
            "origin": tt.get("m_Origin"), "size": tt.get("m_Size"),
            "bbox": {"min_x": min(xs), "max_x": max(xs), "min_y": min(ys), "max_y": max(ys)},
        })
        if tile_size != 1:
            print(f"  {go_name}: tile_size={tile_size}")

    return layers


def build_scene_objects(source, level_idx, all_layers_index):
    """Build scene objects from raw dump."""
    sprite_renderers = load_raw(source, "SpriteRenderer")
    game_objects = load_raw(source, "GameObject")
    xforms = load_raw(source, "Transform") + load_raw(source, "RectTransform")
    sprites_raw = load_raw("sharedassets0_assets", "Sprite")

    # Lookups
    go_names = {}
    for go in game_objects:
        tt = go.get("typetree", {})
        go_names[go["path_id"]] = tt.get("m_Name", go.get("m_Name", "?"))

    t_lookup = {}
    go_to_t = {}
    for t in xforms:
        tt = t.get("typetree", {})
        t_lookup[t["path_id"]] = tt
        go_pid = (tt.get("m_GameObject") or {}).get("m_PathID", 0)
        if go_pid:
            go_to_t[go_pid] = t["path_id"]

    # Sprite info lookup by path_id
    sprite_info = {}
    for s in sprites_raw:
        tt = s.get("typetree", {})
        if tt:
            rect = tt.get("m_Rect", {})
            sprite_info[s["path_id"]] = {
                "name": tt.get("m_Name", s.get("m_Name", "")),
                "w": rect.get("width"), "h": rect.get("height"),
                "ppu": tt.get("m_PixelsToUnits", 1.0),
                "pivot": tt.get("m_Pivot", {"x": 0.5, "y": 0.5}),
            }

    # Course hierarchy detection
    courses_tid = None
    course_tids = {}
    for go_pid, name in go_names.items():
        tid = go_to_t.get(go_pid)
        if not tid: continue
        if name == "Courses":
            courses_tid = tid
        elif name.startswith("course ") and len(name) <= 10:
            course_tids[tid] = name

    # Save data for box corrections
    save_data = {}
    for i in range(1, 6):
        fpath = os.path.join(SAVE_DIR, f"course{i}data.txt")
        if os.path.exists(fpath):
            with open(fpath) as f:
                save_data[f"course {i}"] = json.load(f)

    skip_tids = set()
    if courses_tid: skip_tids.add(courses_tid)
    skip_tids.update(course_tids.keys())

    # Compute course corrections from save data
    course_serialized_origins = {}
    for ctid, cname in course_tids.items():
        x, y = 0, 0
        cur = ctid
        visited = set()
        while cur and cur not in visited:
            visited.add(cur)
            tt = t_lookup.get(cur, {})
            lp = tt.get("m_LocalPosition", {})
            x += lp.get("x", 0); y += lp.get("y", 0)
            cur = (tt.get("m_Father") or {}).get("m_PathID", 0)
        course_serialized_origins[cname] = (x, y)

    course_corrections = {}
    for go_pid, name in go_names.items():
        if "upgradeBox" not in name and name != "DashUnlock": continue
        tid = go_to_t.get(go_pid)
        if not tid: continue
        lx, ly, course = 0, 0, None
        cur = tid
        visited = set()
        while cur and cur not in visited:
            visited.add(cur)
            tt = t_lookup.get(cur, {})
            if cur in course_tids: course = course_tids[cur]
            if cur not in skip_tids:
                lp = tt.get("m_LocalPosition", {})
                lx += lp.get("x", 0); ly += lp.get("y", 0)
            cur = (tt.get("m_Father") or {}).get("m_PathID", 0)
        if course and course in save_data and course not in course_corrections:
            boxes = save_data[course]["_boxPositions"]
            ox, oy = course_serialized_origins.get(course, (0, 0))
            swx, swy = lx + ox, ly + oy
            best = min(boxes, key=lambda b: abs(b["x"]-swx) + abs(b["y"]-swy))
            course_corrections[course] = (best["x"]-swx, best["y"]-swy)

    def walk(go_pid):
        tid = go_to_t.get(go_pid)
        if not tid: return [], 0, 0, 1, 1, 0, None
        chain, wx, wy, sx, sy, rz, course = [], 0, 0, 1.0, 1.0, 0, None
        cur = tid
        visited = set()
        while cur and cur not in visited:
            visited.add(cur)
            tt = t_lookup.get(cur, {})
            if cur in course_tids: course = course_tids[cur]
            lp = tt.get("m_LocalPosition", {})
            ls = tt.get("m_LocalScale", {"x": 1, "y": 1})
            lr = tt.get("m_LocalRotation", {"x": 0, "y": 0, "z": 0, "w": 1})
            wx += lp.get("x", 0); wy += lp.get("y", 0)
            sx *= ls.get("x", 1); sy *= ls.get("y", 1)
            rz += quat_to_z_angle(lr)
            go_p = (tt.get("m_GameObject") or {}).get("m_PathID", 0)
            chain.append({"name": go_names.get(go_p, "?"),
                          "pos": [round(lp.get("x",0),2), round(lp.get("y",0),2)],
                          "scale": [round(ls.get("x",1),3), round(ls.get("y",1),3)],
                          "rot_z": round(quat_to_z_angle(lr),2)})
            cur = (tt.get("m_Father") or {}).get("m_PathID", 0)
        return chain, wx, wy, sx, sy, rz, course

    objects = []
    for sr in sprite_renderers:
        tt = sr.get("typetree", {})
        if not tt: continue

        go_pid = (tt.get("m_GameObject") or {}).get("m_PathID", 0)
        go_name = go_names.get(go_pid, "?")

        chain, wx, wy, sx, sy, rz, course = walk(go_pid)

        # Box-only correction
        is_box = "upgradeBox" in go_name or go_name == "DashUnlock" or "Prestige" in go_name.lower() or go_name == "MovementBox (1)"
        if is_box and course and course in course_corrections:
            dx, dy = course_corrections[course]
            wx += dx; wy += dy

        gx = wx / 32.0
        gy = (wy + 23.0) / 32.0

        sprite_ref = tt.get("m_Sprite") or {}
        sp_pid = sprite_ref.get("m_PathID", 0)
        si = sprite_info.get(sp_pid, {})
        sname = si.get("name")
        sprite_file = None
        if sname:
            safe = sname.replace("/", "_").replace(" ", "_")
            candidate = f"object_sprites/{safe}.png"
            if os.path.exists(os.path.join(OUTPUT, candidate)):
                sprite_file = candidate

        entry = {
            "name": go_name, "go_path_id": go_pid,
            "x": round(gx, 2), "y": round(gy, 2),
            "world_x": round(wx, 2), "world_y": round(wy, 2),
            "scale_x": round(sx, 4), "scale_y": round(sy, 4),
            "rotation_z": round(rz, 2), "course": course,
            "sr_enabled": tt.get("m_Enabled", 1),
            "sr_color": tt.get("m_Color"),
            "sr_flip_x": tt.get("m_FlipX", False),
            "sr_flip_y": tt.get("m_FlipY", False),
            "sr_draw_mode": tt.get("m_DrawMode", 0),
            "sr_size": tt.get("m_Size"),
            "sr_sorting_order": tt.get("m_SortingOrder", 0),
            "sprite": sprite_file, "sprite_name": sname,
            "sprite_w": si.get("w"), "sprite_h": si.get("h"),
            "sprite_ppu": si.get("ppu"), "sprite_pivot": si.get("pivot"),
            "transform_chain": chain,
        }
        objects.append(entry)

    return objects


def main():
    all_layers = []
    for source, level_idx in [("level0", 0), ("level1", 1)]:
        print(f"=== {source} ===")
        layers = build_layers(source, level_idx)
        all_layers.extend(layers)
        print(f"  {len(layers)} tilemap layers")

        objs = build_scene_objects(source, level_idx, all_layers)
        out = os.path.join(OUTPUT, f"scene_objects_{source}.json")
        with open(out, "w") as f:
            json.dump(objs, f, indent=2)
        print(f"  {len(objs)} scene objects")

    with open(os.path.join(OUTPUT, "layer_index.json"), "w") as f:
        json.dump(all_layers, f, indent=2)
    print(f"\nTotal: {len(all_layers)} layers")


if __name__ == "__main__":
    main()
