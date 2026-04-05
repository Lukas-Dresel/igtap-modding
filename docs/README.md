# IGTAP Analysis & Level Editor - Documentation

Reverse engineering analysis and tooling for **IGTAP: An Incremental Game That's
Also a Platformer** (Demo, Unity 6).

## Documentation Index

| Document | Description |
|----------|-------------|
| [overview.md](overview.md) | Game overview, tech stack, key stats |
| [file-formats.md](file-formats.md) | Unity binary formats, save data JSON schema, extracted data formats |
| [code-structure.md](code-structure.md) | All 50 C# classes: fields, methods, interactions |
| [game-mechanics.md](game-mechanics.md) | Movement, clones, upgrades, currencies, progression, springs, breakers |
| [tilemap-layers.md](tilemap-layers.md) | All 38 tilemap layers: purpose, tile counts, transforms, coordinates |
| [map-layout.md](map-layout.md) | Scene hierarchy, zone layout, course structure, world coordinates |
| [coordinate-system.md](coordinate-system.md) | World coordinates, grid mapping, runtime repositioning, sorting, PPU, parallax |
| [rendering.md](rendering.md) | Sprite system, tile transforms, camera, lighting, particles |
| [save-system.md](save-system.md) | SaveableObject pattern, per-file schemas, prestige flow |
| [editor.md](editor.md) | Level editor usage, controls, data pipeline, export format |
| [demo-restrictions.md](demo-restrictions.md) | Demo vs full game: the 2 gates, isDemoBuild flag, what's blocked |
| [raw-dump.md](raw-dump.md) | Complete raw data dump: every object, every field, every file (456 MB) |
| [extraction-scripts.md](extraction-scripts.md) | Setup, running scripts, output structure, troubleshooting |

## Quick Reference

### Key Numbers
- 2 scenes (level0, level1)
- 5 courses across 2 zones
- 38 tilemap layers (~160K tiles per level)
- 376 scene objects (upgrade boxes, checkpoints, springs, gates, decorations)
- 8,128 tile sprites + 32 object sprites
- 50 C# classes (8,563 lines)
- 3 currency types + 13 global upgrades + 9 local upgrades per course
- 2 demo-gated upgrade boxes (patchable via `patch_demo.py`)

### Important Paths
- Game: `~/.local/share/Steam/steamapps/common/IGTAP an Incremental Game That's Also a Platformer Demo/`
- Project: `~/lukas/games/igtap/`
- Output: `~/lukas/games/igtap/output/`
- Docs: `~/lukas/games/igtap/docs/`

### Coordinate System
- Tilemap grid: integer (x, y) positions, 1 unit = 1 tile
- World: floating point, 1 tile = 32 world units
- Grid transform offset: (0, -23)
- Conversion: `grid = world / 32`, `grid_y += 23/32`
