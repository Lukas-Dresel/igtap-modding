#!/usr/bin/env python3.11
"""
Patch IGTAP demo to disable the isDemoBuild flag on the two gated upgrade boxes.

This flips isDemoBuild from true (0x01) to false (0x00) on:
  - "SwapBlocksOnce/endDemo" (Course 4 block swap unlock)
  - "prestige upgrade box" (Course 5 prestige unlock)

Both are MonoBehaviour instances of the upgradeBox class in level1.
The isDemoBuild field is at byte offset 316 within each MonoBehaviour's raw data.

The patch locates each MonoBehaviour in the raw file by matching its serialized
header bytes, then flips the single byte in-place. No re-serialization needed.

Usage:
    python3.11 patch_demo.py          # patches the game files (backs up originals)
    python3.11 patch_demo.py --verify # check current state without modifying
    python3.11 patch_demo.py --revert # restores from backup
"""
import struct
import sys
import os
import shutil

import UnityPy

GAME_DATA = "/home/honululu/.local/share/Steam/steamapps/common/IGTAP an Incremental Game That's Also a Platformer Demo/IGTAP_Data"
LEVEL1_PATH = os.path.join(GAME_DATA, "level1")
BACKUP_PATH = LEVEL1_PATH + ".backup"

# upgradeBox MonoScript PPtr: fileID=1 (globalgamemanagers.assets), pathID=748
UPGRADEBOX_SCRIPT_PPTR = struct.pack('<iq', 1, 748)
ISDEMO_OFFSET = 316  # byte offset of isDemoBuild within MonoBehaviour raw data

# Target MonoBehaviour path_ids in level1
TARGETS = {
    7526: "prestige upgrade box",
    7535: "SwapBlocksOnce/endDemo",
}


def find_patch_offsets():
    """Use UnityPy to locate the exact file offsets, then verify against raw file."""
    print(f"Loading {GAME_DATA} ...")
    env = UnityPy.load(GAME_DATA)

    with open(LEVEL1_PATH, 'rb') as f:
        level1_raw = f.read()

    results = []  # list of (file_offset, name, current_value)

    for obj in env.objects:
        if obj.type.name != "MonoBehaviour" or obj.assets_file.name != "level1":
            continue
        if obj.path_id not in TARGETS:
            continue

        mb_raw = obj.get_raw_data()
        if len(mb_raw) <= ISDEMO_OFFSET:
            print(f"  ERROR: {TARGETS[obj.path_id]} raw data too short ({len(mb_raw)} bytes)")
            continue
        if mb_raw[16:28] != UPGRADEBOX_SCRIPT_PPTR:
            print(f"  ERROR: {TARGETS[obj.path_id]} script PPtr mismatch")
            continue

        # Find this MB's data in the raw file by matching its unique header
        needle = mb_raw[:64]
        file_offset = level1_raw.find(needle)
        if file_offset < 0:
            print(f"  ERROR: {TARGETS[obj.path_id]} data not found in level1 file")
            continue

        abs_offset = file_offset + ISDEMO_OFFSET
        current_val = level1_raw[abs_offset]

        # Sanity check: verify the EndOfDemoScreen PPtr follows
        fid = struct.unpack_from('<i', level1_raw, abs_offset + 4)[0]
        pid = struct.unpack_from('<q', level1_raw, abs_offset + 8)[0]
        if pid != 1856:
            print(f"  WARNING: {TARGETS[obj.path_id]} EndOfDemoScreen PPtr unexpected (pathID={pid})")

        results.append((abs_offset, TARGETS[obj.path_id], current_val))

    return results


def patch():
    if os.path.exists(BACKUP_PATH):
        print(f"Backup already exists: {BACKUP_PATH}")
        print("Run with --revert first if you want to re-patch.")
        return False

    offsets = find_patch_offsets()
    if not offsets:
        print("No patch targets found!")
        return False

    to_patch = [(off, name) for off, name, val in offsets if val == 1]
    if not to_patch:
        print("All targets already patched (isDemoBuild=0).")
        return True

    print(f"\nBacking up {LEVEL1_PATH}")
    shutil.copy2(LEVEL1_PATH, BACKUP_PATH)

    with open(LEVEL1_PATH, 'r+b') as f:
        for abs_offset, name in to_patch:
            f.seek(abs_offset)
            f.write(b'\x00')
            print(f"  Patched {name}: offset 0x{abs_offset:x} -> 0x00")

    print(f"\nDone! Patched {len(to_patch)} bytes in level1.")
    print(f"Backup: {BACKUP_PATH}")
    return True


def verify():
    offsets = find_patch_offsets()
    print()
    for abs_offset, name, val in offsets:
        status = "DEMO (blocked)" if val == 1 else "UNLOCKED (patched)" if val == 0 else f"UNKNOWN (0x{val:02x})"
        print(f"  {name:>30s}: isDemoBuild={val} at 0x{abs_offset:x} -> {status}")


def revert():
    if not os.path.exists(BACKUP_PATH):
        print(f"No backup found at {BACKUP_PATH}")
        return False

    print(f"Restoring {BACKUP_PATH} -> {LEVEL1_PATH}")
    shutil.copy2(BACKUP_PATH, LEVEL1_PATH)
    os.remove(BACKUP_PATH)
    print("Reverted to original.")
    return True


if __name__ == "__main__":
    if "--revert" in sys.argv:
        revert()
    elif "--verify" in sys.argv:
        verify()
    else:
        patch()
