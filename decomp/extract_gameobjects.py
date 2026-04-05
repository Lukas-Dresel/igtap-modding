#!/usr/bin/env python3.11
"""Extract the full GameObject hierarchy from IGTAP level files."""
import json
import os

import UnityPy

GAME_DATA = "/home/honululu/.local/share/Steam/steamapps/common/IGTAP an Incremental Game That's Also a Platformer Demo/IGTAP_Data"
OUTPUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "output")
os.makedirs(OUTPUT_DIR, exist_ok=True)


def main():
    print(f"Loading assets from {GAME_DATA} ...")
    env = UnityPy.load(GAME_DATA)

    # Collect all GameObjects
    gameobjects = {}
    for obj in env.objects:
        if obj.type.name == "GameObject":
            try:
                data = obj.read()
                components = []
                for comp_pair in data.m_Components:
                    comp = comp_pair
                    # comp is a PPtr - try to resolve
                    try:
                        comp_obj = comp.read()
                        components.append(comp_obj.type.name if hasattr(comp_obj, "type") else type(comp_obj).__name__)
                    except Exception:
                        components.append("?")

                gameobjects[obj.path_id] = {
                    "path_id": obj.path_id,
                    "name": data.m_Name,
                    "tag": getattr(data, "m_Tag", None),
                    "layer": getattr(data, "m_Layer", None),
                    "is_active": getattr(data, "m_IsActive", True),
                    "components": components,
                }
            except Exception as e:
                gameobjects[obj.path_id] = {"path_id": obj.path_id, "error": str(e)}

    # Collect Transform hierarchy
    transforms = {}
    for obj in env.objects:
        if obj.type.name in ("Transform", "RectTransform"):
            try:
                tree = obj.read_typetree()
                go_ref = tree.get("m_GameObject", {})
                go_path_id = go_ref.get("m_PathID", 0) if isinstance(go_ref, dict) else 0

                father = tree.get("m_Father", {})
                father_path_id = father.get("m_PathID", 0) if isinstance(father, dict) else 0

                children_refs = tree.get("m_Children", [])
                children_ids = []
                for c in children_refs:
                    if isinstance(c, dict):
                        children_ids.append(c.get("m_PathID", 0))

                local_pos = tree.get("m_LocalPosition", {})
                local_rot = tree.get("m_LocalRotation", {})
                local_scale = tree.get("m_LocalScale", {})

                transforms[obj.path_id] = {
                    "path_id": obj.path_id,
                    "type": obj.type.name,
                    "gameobject_path_id": go_path_id,
                    "parent_transform_path_id": father_path_id,
                    "children_transform_path_ids": children_ids,
                    "local_position": local_pos,
                    "local_rotation": local_rot,
                    "local_scale": local_scale,
                }
            except Exception as e:
                transforms[obj.path_id] = {"path_id": obj.path_id, "error": str(e)}

    # Build parent-child relationships via transforms
    # Map: transform_path_id -> gameobject_path_id
    transform_to_go = {}
    for tid, t in transforms.items():
        if "gameobject_path_id" in t:
            transform_to_go[tid] = t["gameobject_path_id"]

    # Attach transform info to gameobjects
    for tid, t in transforms.items():
        go_pid = t.get("gameobject_path_id", 0)
        if go_pid in gameobjects and "error" not in gameobjects[go_pid]:
            go = gameobjects[go_pid]
            go["transform_path_id"] = tid
            go["position"] = t.get("local_position")
            go["rotation"] = t.get("local_rotation")
            go["scale"] = t.get("local_scale")
            # Resolve parent
            parent_tid = t.get("parent_transform_path_id", 0)
            go["parent_gameobject_path_id"] = transform_to_go.get(parent_tid, 0)
            # Resolve children
            child_tids = t.get("children_transform_path_ids", [])
            go["children_gameobject_path_ids"] = [
                transform_to_go.get(ctid, 0) for ctid in child_tids
            ]

    # Write flat list
    go_list = sorted(gameobjects.values(), key=lambda g: g.get("path_id", 0))
    out_path = os.path.join(OUTPUT_DIR, "gameobjects.json")
    with open(out_path, "w") as f:
        json.dump(go_list, f, indent=2, default=str)
    print(f"Wrote {out_path} ({len(go_list)} GameObjects)")

    # Write hierarchy tree (root objects only, with nested children)
    def build_tree(go_pid, depth=0):
        if depth > 20:
            return None  # prevent infinite recursion
        go = gameobjects.get(go_pid, {})
        if "error" in go:
            return None
        node = {
            "name": go.get("name", "?"),
            "path_id": go_pid,
            "components": go.get("components", []),
            "position": go.get("position"),
            "active": go.get("is_active", True),
        }
        child_ids = go.get("children_gameobject_path_ids", [])
        if child_ids:
            node["children"] = [
                c for c in (build_tree(cid, depth + 1) for cid in child_ids if cid) if c
            ]
        return node

    # Find root objects (no parent or parent=0)
    roots = []
    for go in go_list:
        if "error" in go:
            continue
        parent = go.get("parent_gameobject_path_id", 0)
        if parent == 0:
            tree = build_tree(go["path_id"])
            if tree:
                roots.append(tree)

    out_path = os.path.join(OUTPUT_DIR, "gameobject_hierarchy.json")
    with open(out_path, "w") as f:
        json.dump(roots, f, indent=2, default=str)
    print(f"Wrote {out_path} ({len(roots)} root objects)")


if __name__ == "__main__":
    main()
