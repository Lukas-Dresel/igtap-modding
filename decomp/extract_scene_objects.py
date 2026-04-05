#!/usr/bin/env python3.11
"""Extract ALL non-tilemap GameObjects with SpriteRenderers.

Dumps every available field: full transform chain data (position, rotation,
scale for every ancestor), sprite info (size, PPU, name), SpriteRenderer
fields (drawMode, m_Size, color, flip, sortingOrder), collider sizes, etc.

The editor decides what to use. No data is discarded or pre-computed here.
"""
import json
import math
import os

import UnityPy

GAME_DATA = "/home/honululu/.local/share/Steam/steamapps/common/IGTAP an Incremental Game That's Also a Platformer Demo/IGTAP_Data"
SAVE_DIR = "/home/honululu/.config/unity3d/Pepper tango games/IGTAP/Savedata"
OUTPUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "output")
SPRITES_DIR = os.path.join(OUTPUT_DIR, "object_sprites")
os.makedirs(SPRITES_DIR, exist_ok=True)


def resolve_sprite(env, assets_file, fid, pid):
    if pid == 0:
        return None, None, None, None
    if fid > 0 and fid <= len(assets_file.externals):
        ext_name = assets_file.externals[fid - 1].name
    else:
        ext_name = assets_file.name
    for fname, f in env.files.items():
        if hasattr(f, "name") and f.name == ext_name:
            target = f
            break
        if ext_name in fname:
            target = f
            break
    else:
        return None, None, None, None
    for o in target.objects.values():
        if o.path_id == pid:
            try:
                d = o.read()
                ppu = 1.0
                pivot = {"x": 0.5, "y": 0.5}
                try:
                    tree = o.read_typetree()
                    ppu = tree.get("m_PixelsToUnits", 1.0)
                    pivot = tree.get("m_Pivot", pivot)
                except:
                    pass
                return d.m_Name, d.image, ppu, pivot
            except:
                return None, None, None, None
    return None, None, None, None


def quat_to_z_angle(q):
    """Quaternion (x,y,z,w) -> Z-axis rotation in degrees."""
    qx = q.get("x", 0)
    qy = q.get("y", 0)
    qz = q.get("z", 0)
    qw = q.get("w", 1)
    return math.degrees(math.atan2(2 * (qw * qz + qx * qy), 1 - 2 * (qy * qy + qz * qz)))


def main():
    print(f"Loading {GAME_DATA} ...")
    env = UnityPy.load(GAME_DATA)

    # Load save data for all courses
    save_data = {}
    for i in range(1, 6):
        fpath = os.path.join(SAVE_DIR, f"course{i}data.txt")
        if os.path.exists(fpath):
            with open(fpath) as f:
                save_data[f"course {i}"] = json.load(f)

    for level_name in ("level0", "level1"):
        print(f"\n=== Processing {level_name} ===")
        level_idx = 0 if "0" in level_name else 1

        sf = None
        for fname, f in env.files.items():
            if hasattr(f, "name") and f.name == level_name:
                sf = f
                break
        if not sf:
            continue

        # ---- Build full transform lookup ----
        transforms = {}
        for obj in sf.objects.values():
            if obj.type.name in ("Transform", "RectTransform"):
                try:
                    tree = obj.read_typetree()
                    transforms[obj.path_id] = {
                        "lp": tree.get("m_LocalPosition", {"x": 0, "y": 0, "z": 0}),
                        "ls": tree.get("m_LocalScale", {"x": 1, "y": 1, "z": 1}),
                        "lr": tree.get("m_LocalRotation", {"x": 0, "y": 0, "z": 0, "w": 1}),
                        "parent": (tree.get("m_Father") or {}).get("m_PathID", 0),
                        "go": (tree.get("m_GameObject") or {}).get("m_PathID", 0),
                        "children": [
                            c.get("m_PathID", 0) if isinstance(c, dict) else 0
                            for c in tree.get("m_Children", [])
                        ],
                        "type": obj.type.name,
                    }
                except:
                    pass

        go_names = {}
        go_to_t = {}
        for obj in sf.objects.values():
            if obj.type.name == "GameObject":
                try:
                    go_names[obj.path_id] = obj.read().m_Name
                except:
                    pass
        for tid, t in transforms.items():
            if t["go"]:
                go_to_t[t["go"]] = tid

        # ---- Identify course hierarchy for position correction ----
        courses_tid = None
        course_tids = {}
        for gpid, name in go_names.items():
            tid = go_to_t.get(gpid)
            if not tid:
                continue
            if name == "Courses":
                courses_tid = tid
            elif name.startswith("course ") and len(name) <= 10:
                course_tids[tid] = name

        skip_tids = set()
        if courses_tid:
            skip_tids.add(courses_tid)
        skip_tids.update(course_tids.keys())

        # Compute serialized course origins
        course_serialized_origins = {}
        for ctid, cname in course_tids.items():
            x, y = 0, 0
            cur = ctid
            visited = set()
            while cur and cur not in visited:
                visited.add(cur)
                t = transforms.get(cur)
                if not t:
                    break
                x += t["lp"].get("x", 0)
                y += t["lp"].get("y", 0)
                cur = t["parent"]
            course_serialized_origins[cname] = (x, y)

        # Derive corrections from save data
        course_corrections = {}
        for gpid, name in go_names.items():
            if "upgradeBox" not in name and name != "DashUnlock":
                continue
            tid = go_to_t.get(gpid)
            if not tid:
                continue
            lx, ly = 0, 0
            course = None
            cur = tid
            visited = set()
            while cur and cur not in visited:
                visited.add(cur)
                t = transforms.get(cur)
                if not t:
                    break
                if cur in course_tids:
                    course = course_tids[cur]
                if cur not in skip_tids:
                    lx += t["lp"].get("x", 0)
                    ly += t["lp"].get("y", 0)
                cur = t["parent"]
            if course and course in save_data and course not in course_corrections:
                boxes = save_data[course]["_boxPositions"]
                ser_wx = lx + course_serialized_origins.get(course, (0, 0))[0]
                ser_wy = ly + course_serialized_origins.get(course, (0, 0))[1]
                best = min(boxes, key=lambda bp: abs(bp["x"] - ser_wx) + abs(bp["y"] - ser_wy))
                course_corrections[course] = (best["x"] - ser_wx, best["y"] - ser_wy)
                print(f"  {course}: correction=({course_corrections[course][0]:.1f}, {course_corrections[course][1]:.1f})")

        # ---- Helper: walk hierarchy ----
        def walk_hierarchy(gpid):
            """Return full transform chain, world pos, world scale, world Z rotation, course name."""
            tid = go_to_t.get(gpid)
            if not tid:
                return [], 0, 0, 1, 1, 0, None
            chain = []
            wx, wy = 0, 0
            sx, sy = 1.0, 1.0
            total_z_deg = 0
            course = None
            cur = tid
            visited = set()
            while cur and cur not in visited:
                visited.add(cur)
                t = transforms.get(cur)
                if not t:
                    break
                if cur in course_tids:
                    course = course_tids[cur]
                lp = t["lp"]
                ls = t["ls"]
                lr = t["lr"]
                wx += lp.get("x", 0)
                wy += lp.get("y", 0)
                sx *= ls.get("x", 1)
                sy *= ls.get("y", 1)
                total_z_deg += quat_to_z_angle(lr)
                chain.append({
                    "name": go_names.get(t["go"], "?"),
                    "pos": [round(lp.get("x", 0), 2), round(lp.get("y", 0), 2)],
                    "scale": [round(ls.get("x", 1), 3), round(ls.get("y", 1), 3)],
                    "rot_z": round(quat_to_z_angle(lr), 2),
                })
                cur = t["parent"]
            # NOTE: do NOT apply course corrections here.
            # Only upgrade boxes get repositioned at runtime (line 3849 in game code).
            # Everything else stays at its serialized hierarchy position.
            return chain, wx, wy, sx, sy, total_z_deg, course

        # ---- Extract SpriteRenderers ----
        objects = []
        sprite_cache = {}

        for obj in sf.objects.values():
            if obj.type.name != "SpriteRenderer":
                continue
            try:
                sr_tree = obj.read_typetree()
            except:
                continue

            go_pid = (sr_tree.get("m_GameObject") or {}).get("m_PathID", 0)
            go_name = go_names.get(go_pid, "?")

            # SpriteRenderer fields
            sr_enabled = sr_tree.get("m_Enabled", 1)
            sr_color = sr_tree.get("m_Color", {})
            sr_flip_x = sr_tree.get("m_FlipX", False)
            sr_flip_y = sr_tree.get("m_FlipY", False)
            sr_draw_mode = sr_tree.get("m_DrawMode", 0)
            sr_size = sr_tree.get("m_Size", {})
            sr_sorting_layer = sr_tree.get("m_SortingLayerID", 0)
            sr_sorting_order = sr_tree.get("m_SortingOrder", 0)
            sr_mask = sr_tree.get("m_MaskInteraction", 0)

            sprite_ref = sr_tree.get("m_Sprite") or {}
            sp_fid = sprite_ref.get("m_FileID", 0)
            sp_pid = sprite_ref.get("m_PathID", 0)

            # Transform hierarchy
            chain, wx, wy, sx, sy, rot_z, course = walk_hierarchy(go_pid)

            # Only upgrade boxes get repositioned at runtime from save data (line 3849).
            # Apply course correction ONLY to upgrade box objects.
            is_upgrade_box = "upgradeBox" in go_name or go_name == "DashUnlock" or "Prestige" in go_name or "prestige" in go_name or go_name == "MovementBox (1)"
            if is_upgrade_box and course and course in course_corrections:
                dx, dy = course_corrections[course]
                wx += dx
                wy += dy

            # Grid coords
            gx = wx / 32.0
            gy = (wy + 23.0) / 32.0

            # Sprite extraction
            sprite_file = None
            sprite_name = None
            simg_size = None
            sprite_ppu = 1.0
            sprite_pivot = {"x": 0.5, "y": 0.5}
            cache_key = (sp_fid, sp_pid)
            if cache_key in sprite_cache:
                sprite_file, simg_size, sprite_ppu, sprite_pivot = sprite_cache[cache_key]
            elif sp_pid != 0:
                sname, simg, sppu, spivot = resolve_sprite(env, sf, sp_fid, sp_pid)
                if simg:
                    sprite_name = sname or f"sprite_{sp_pid}"
                    simg_size = simg.size
                    sprite_ppu = sppu or 1.0
                    sprite_pivot = spivot or {"x": 0.5, "y": 0.5}
                    safe = sprite_name.replace("/", "_").replace(" ", "_")
                    sprite_file = f"object_sprites/{safe}.png"
                    out_path = os.path.join(OUTPUT_DIR, sprite_file)
                    if not os.path.exists(out_path):
                        simg.save(out_path)
                sprite_cache[cache_key] = (sprite_file, simg_size, sprite_ppu, sprite_pivot)

            entry = {
                "name": go_name,
                "go_path_id": go_pid,
                # Computed world values
                "x": round(gx, 2),
                "y": round(gy, 2),
                "world_x": round(wx, 2),
                "world_y": round(wy, 2),
                "scale_x": round(sx, 4),
                "scale_y": round(sy, 4),
                "rotation_z": round(rot_z, 2),
                "course": course,
                # SpriteRenderer raw fields
                "sr_enabled": sr_enabled,
                "sr_color": {k: round(v, 3) if isinstance(v, float) else v for k, v in sr_color.items()} if sr_color else None,
                "sr_flip_x": sr_flip_x,
                "sr_flip_y": sr_flip_y,
                "sr_draw_mode": sr_draw_mode,
                "sr_size": {k: round(v, 3) if isinstance(v, float) else v for k, v in sr_size.items()} if sr_size else None,
                "sr_sorting_order": sr_sorting_order,
                # Sprite asset info
                "sprite": sprite_file,
                "sprite_name": sprite_name,
                "sprite_w": simg_size[0] if simg_size else None,
                "sprite_h": simg_size[1] if simg_size else None,
                "sprite_ppu": round(sprite_ppu, 4) if sprite_ppu else None,
                "sprite_pivot": sprite_pivot,
                # Full transform chain (for debugging / editor use)
                "transform_chain": chain,
            }
            objects.append(entry)

        out_path = os.path.join(OUTPUT_DIR, f"scene_objects_{level_name}.json")
        with open(out_path, "w") as f:
            json.dump(objects, f, indent=2)
        print(f"  Wrote {out_path} ({len(objects)} objects)")

    # Also dump save data for reference
    save_out = os.path.join(OUTPUT_DIR, "course_save_data.json")
    with open(save_out, "w") as f:
        json.dump(save_data, f, indent=2)
    print(f"\nWrote {save_out}")


if __name__ == "__main__":
    main()
