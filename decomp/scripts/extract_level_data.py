#!/usr/bin/env python3.11
"""Extract per-course upgrade data from IGTAP Unity scene file.

Reads level1 (the only Unity scene), finds all upgradeBox MonoBehaviours,
groups them by their parent courseScript GameObject, and writes one JSON
file per course to simulator/data/level{N}_data.json.
"""
import os
import sys
import json
import struct
from collections import defaultdict
from pathlib import Path

import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
from UnityPy.helpers.TypeTreeNode import TypeTreeNode

GAME_DIR = "/home/honululu/.local/share/Steam/steamapps/common/IGTAP an Incremental Game That's Also a Platformer Demo"
DATA_DIR = os.path.join(GAME_DIR, "IGTAP_Data")
MANAGED = os.path.join(DATA_DIR, "Managed")
OUT_DIR = Path(__file__).resolve().parents[2] / "simulator" / "data"

# Enums from decompiled Assembly-CSharp
# localUpgrades.localUpgradeSet (localUpgrades.cs:7)
# Values 0 and 1 are sentinels meaning "look at globalUpgrade/movementUpgrade field"
LOCAL_UPGRADE_NAMES = {
    0: "<GLOBAL_SENTINEL>", 1: "<MOVEMENT_SENTINEL>",
    2: "cloneCount", 3: "cashPerLoop",
    4: "fastCloneChance", 5: "bigCloneChance", 6: "prestige",
    7: "cloneMult", 8: "DUMMY_cloneCountPlural",
}
# globalStats.globalUpgradeSet (globalStats.cs:5)
GLOBAL_UPGRADE_NAMES = {
    0: "global_cashPerLoop", 1: "global_fastCloneChance",
    2: "maxCloneFastness", 3: "global_bigCloneChance",
    4: "maxCloneBigness", 5: "global_cloneMult",
    6: "spawnNewAtom", 7: "atomLevelChance",
    8: "greenCloneChance", 9: "TreeGrowth",
    10: "unlockPrestige", 11: "openGate",
    12: "increasedWatts",
}
# upgradeBox.movementUpgrades (upgradeBox.cs:11)
MOVEMENT_NAMES = {
    0: "dash", 1: "wallJump", 2: "doubleJump",
    3: "swapBlocksOnce", 4: "unlockBlockSwap",
}


def upgrade_name(box):
    """Resolve a box's upgrade name using proper dispatch.

    The localUpgrade enum values 0 and 1 are SENTINELS that mean
    "look at the globalUpgrade or movementUpgrade field instead".
    """
    upg = box["upgrade"]
    if upg == 0:  # GLOBAL sentinel
        return GLOBAL_UPGRADE_NAMES.get(box["globalUpgrade"], f"global_{box['globalUpgrade']}")
    if upg == 1:  # Movement sentinel
        return MOVEMENT_NAMES.get(box["movementUpgrade"], f"movement_{box['movementUpgrade']}")
    return LOCAL_UPGRADE_NAMES.get(upg, f"local_{upg}")


def upgrade_type(box):
    upg = box["upgrade"]
    if upg == 0:
        return "global"
    if upg == 1:
        return "movement"
    return "local"


def is_terminal(name):
    """Movement upgrades that gate level progression."""
    return name in ("wallJump", "doubleJump", "dash", "swapBlocksOnce", "unlockBlockSwap")


def _course1_transitions(box_names: list[str]) -> dict:
    """Course 1's hand-measured travel times: 2.0s exit->box, 0.75s box-pair, 2.5s box->entrance."""
    t = {}
    for b in box_names:
        t[f"exit -> {b}"] = {"travel_time": 2.0}
    t["exit -> entrance"] = {"travel_time": 0.75}
    for a in box_names:
        for b in box_names:
            t[f"{a} -> {b}"] = {"travel_time": 0.75}
        t[f"{a} -> entrance"] = {"travel_time": 2.5}
    return t


def main():
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    print(f"Loading level1 scene from {DATA_DIR}")
    env = UnityPy.load(os.path.join(DATA_DIR, "level1"))
    sf = list(env.files.values())[0]

    print(f"Building type trees from {MANAGED}")
    gen = TypeTreeGenerator(unity_version=sf.unity_version)
    gen.load_local_dll_folder(MANAGED)

    # Look up MonoScript path_ids by class name
    print("Loading MonoScript registry from globalgamemanagers.assets")
    gga = UnityPy.load(os.path.join(DATA_DIR, "globalgamemanagers.assets"))
    script_pid = {}
    for obj in gga.objects:
        if obj.type.name == "MonoScript":
            s = obj.read()
            script_pid[s.m_ClassName] = obj.path_id

    def to_root(class_name):
        flat = gen.get_nodes("Assembly-CSharp", class_name)
        dicts = [{"m_Level": n.m_Level, "m_Type": n.m_Type, "m_Name": n.m_Name,
                  "m_MetaFlag": n.m_MetaFlag} for n in flat]
        return TypeTreeNode.from_list(dicts)

    ub_root = to_root("upgradeBox")
    cs_root = to_root("courseScript")

    script_targets = {
        script_pid["upgradeBox"]: ("upgradeBox", ub_root),
        script_pid["courseScript"]: ("courseScript", cs_root),
        script_pid["startGate"]: ("startGate", None),
        script_pid["endGate"]: ("endGate", None),
    }

    upgrade_boxes = {}  # mb_path_id -> tree
    course_scripts = {}
    start_gates = []  # list of mb_path_id
    end_gates = []
    mb_gameobject = {}  # mb_path_id -> (file_id, go_path_id)

    print("Scanning level1 MonoBehaviours")
    for obj in env.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        try:
            data = obj.get_raw_data()
            if len(data) < 28:
                continue
            go_fid, go_pid = struct.unpack("<iq", data[0:12])
            s_fid, s_pid = struct.unpack("<iq", data[16:28])
            if s_fid != 1 or s_pid not in script_targets:
                continue
            cls_name, root = script_targets[s_pid]
            mb_gameobject[obj.path_id] = (go_fid, go_pid)
            if cls_name == "upgradeBox":
                upgrade_boxes[obj.path_id] = obj.read_typetree(nodes=root)
            elif cls_name == "courseScript":
                course_scripts[obj.path_id] = obj.read_typetree(nodes=root)
            elif cls_name == "startGate":
                start_gates.append(obj.path_id)
            elif cls_name == "endGate":
                end_gates.append(obj.path_id)
        except Exception:
            pass

    # Build Transform parent linkage + local positions
    print("Building transform hierarchy")
    transform_parent = {}  # tr_path_id -> (file_id, parent_tr_path_id)
    transform_to_go = {}
    go_to_transform = {}
    transform_local_pos = {}  # tr_path_id -> (x, y, z)
    for obj in env.objects:
        if obj.type.name != "Transform":
            continue
        try:
            tree = obj.read_typetree()
            father = tree.get("m_Father", {})
            transform_parent[obj.path_id] = (
                father.get("m_FileID", 0), father.get("m_PathID", 0))
            go_pid = tree.get("m_GameObject", {}).get("m_PathID", 0)
            transform_to_go[obj.path_id] = go_pid
            if go_pid:
                go_to_transform[go_pid] = obj.path_id
            local_pos = tree.get("m_LocalPosition", {})
            transform_local_pos[obj.path_id] = (
                local_pos.get("x", 0.0),
                local_pos.get("y", 0.0),
                local_pos.get("z", 0.0),
            )
        except Exception:
            pass

    def world_pos(go_pid):
        """Walk parent chain summing local positions to get world position."""
        tr = go_to_transform.get(go_pid)
        if tr is None:
            return None
        x = y = z = 0.0
        seen = set()
        while tr is not None and tr not in seen:
            seen.add(tr)
            lp = transform_local_pos.get(tr, (0, 0, 0))
            x += lp[0]; y += lp[1]; z += lp[2]
            parent_fid, parent_pid = transform_parent.get(tr, (0, 0))
            if parent_fid != 0 or parent_pid == 0:
                break
            tr = parent_pid if parent_pid in transform_local_pos else None
        return (x, y, z)

    # Map course GameObject -> course number
    course_go_to_num = {}
    for cs_pid, cs in course_scripts.items():
        go_pid = mb_gameobject.get(cs_pid, (0, 0))[1]
        if go_pid:
            course_go_to_num[go_pid] = cs.get("courseNumber", -1)

    def find_course(mb_pid):
        go = mb_gameobject.get(mb_pid, (0, 0))[1]
        if not go:
            return None
        seen = set()
        while go and go not in seen:
            seen.add(go)
            if go in course_go_to_num:
                return course_go_to_num[go]
            tr = go_to_transform.get(go)
            if not tr:
                return None
            parent_fid, parent_pid = transform_parent.get(tr, (0, 0))
            if parent_fid != 0 or not parent_pid:
                return None
            go = transform_to_go.get(parent_pid)
        return None

    # Group upgrade boxes by course (track box pid so we can fetch the position)
    boxes_by_course = defaultdict(list)
    for ub_pid, ub in upgrade_boxes.items():
        course = find_course(ub_pid)
        if course is not None and course > 0:
            boxes_by_course[course].append((ub_pid, ub))

    # Group start/end gates by course
    starts_by_course = defaultdict(list)
    ends_by_course = defaultdict(list)
    for gpid in start_gates:
        c = find_course(gpid)
        if c is not None and c > 0:
            starts_by_course[c].append(gpid)
    for gpid in end_gates:
        c = find_course(gpid)
        if c is not None and c > 0:
            ends_by_course[c].append(gpid)

    print(f"\nFound {len(course_scripts)} courses, {len(upgrade_boxes)} upgrade boxes, "
          f"{len(start_gates)} start gates, {len(end_gates)} end gates")

    # Write one JSON per course
    for course_num, boxes in sorted(boxes_by_course.items()):
        cs = next((c for c in course_scripts.values()
                   if c.get("courseNumber") == course_num), None)
        if cs is None:
            continue

        upgrades_dict = {}
        terminal_upgrade = None
        income_upgrade = None
        clone_upgrade = None

        for ub_pid, box in boxes:
            name = upgrade_name(box)
            cost = box["upgradeCost"]
            scale = box["upgradeScaleFactor"]
            power = box["upgradePowerScaleFactor"]
            add = box["upgradeAddFactor"]
            cap = box["Cap"]
            utype = upgrade_type(box)
            visible = bool(box.get("visible"))
            active = bool(box.get("isActive"))

            base_name = name
            i = 2
            while name in upgrades_dict:
                name = f"{base_name}_{i}"
                i += 1

            box_go = mb_gameobject.get(ub_pid, (0, 0))[1]
            wp = world_pos(box_go) if box_go else None

            upgrades_dict[name] = {
                "type": utype,
                "base_cost": cost,
                "scale_factor": scale,
                "power_scale_factor": power,
                "add_factor": add,
                "cap": cap,
                "visible": visible,
                "active": active,
                "position": list(wp) if wp else None,
            }

            # Identify roles
            if utype == "movement" and is_terminal(base_name):
                if terminal_upgrade is None:
                    terminal_upgrade = name
            if base_name == "cashPerLoop" and income_upgrade is None:
                income_upgrade = name
            if base_name == "cloneCount" and clone_upgrade is None:
                clone_upgrade = name

        # Get start/end gate positions for this course
        start_pos = None
        for gpid in starts_by_course.get(course_num, []):
            go = mb_gameobject.get(gpid, (0, 0))[1]
            if go:
                start_pos = world_pos(go)
                break
        end_pos = None
        for gpid in ends_by_course.get(course_num, []):
            go = mb_gameobject.get(gpid, (0, 0))[1]
            if go:
                end_pos = world_pos(go)
                break

        level_data = {
            "terminal_upgrade": terminal_upgrade or "wallJump",
            "income_upgrade": income_upgrade or "cashPerLoop",
            "clone_upgrade": clone_upgrade or "cloneCount",
            "course": {
                "number": course_num,
                "base_reward": int(cs.get("baseReward", 1)),
                "tier": float(cs.get("tier", 0)),
            },
            "clone": {
                "base_multiplier": 0.1,
            },
            "gate_positions": {
                "start": list(start_pos) if start_pos else None,
                "end": list(end_pos) if end_pos else None,
            },
            # Transitions are populated externally (e.g. from replay data) — not
            # extracted from Unity. Course 1 has known values; others start empty
            # and any algorithm that requests an unmodeled pair will crash.
            "transitions": _course1_transitions(list(upgrades_dict.keys())) if course_num == 1 else {},
            "upgrades": upgrades_dict,
        }

        out_path = OUT_DIR / f"course{course_num}_data.json"
        with open(out_path, "w") as f:
            json.dump(level_data, f, indent=2)
        print(f"  Wrote {out_path}")
        print(f"    base_reward={level_data['course']['base_reward']} tier={level_data['course']['tier']}")
        print(f"    upgrades: {list(upgrades_dict.keys())}")
        print(f"    terminal={terminal_upgrade} income={income_upgrade} clone={clone_upgrade}")


if __name__ == "__main__":
    main()
