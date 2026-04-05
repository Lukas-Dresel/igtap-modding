# IGTAP Modding & Analysis Toolkit

Reverse engineering analysis, modding framework, and level editor tooling for
**IGTAP: An Incremental Game That's Also a Platformer** (Demo, Unity 6).

## Repository Structure

```
igtap/
├── mod/                  # Core mod (BepInEx plugin) - debug menu, HUD, shared APIs
├── mod-freeplay/         # Freeplay mod - god mode, noclip, speed tweaks, hidden spike reveal
├── mod-dashplus/         # Dash+ mod - diagonal and vertical dashing
├── mod-inputviz/         # Input Viz mod - on-screen input/status overlay
├── mod-undemo/           # Undemo mod - bypasses demo restrictions
├── decomp/               # Python scripts for asset extraction and decompilation
├── docs/                 # Detailed reverse engineering documentation
├── editor.html           # Browser-based level editor
├── Directory.Build.props # Shared MSBuild config (game paths, BepInEx/Unity refs)
├── _common.sh            # Shared shell helpers (game detection, BepInEx install)
├── build_all.sh          # Build all mods
└── install_all.sh        # Install all mods to game directory
```

## Mods

All mods are [BepInEx 5](https://github.com/BepInEx/BepInEx) plugins targeting .NET Standard 2.1.
They use [Harmony](https://github.com/pardeike/Harmony) to patch the game at runtime.

### Core (`mod/`)

The foundation plugin that other mods depend on. Provides:

- **DebugMenuAPI** - register menu sections and HUD items from any mod
- **DebugUI** - togglable in-game menu window (F8) with HUD overlay
- **GameState** - cached reflection accessors for `Movement` private fields (grounded, wall state, dash/jump counts)
- **MenuWidgets** - reusable IMGUI controls (int/float fields with +/- buttons)

### Freeplay (`mod-freeplay/`)

Sandbox/cheat mod with BepInEx config entries for everything:

- Speed/jump multipliers, extra air dashes/jumps, infinite dashes/jumps/wall jumps
- God mode (immune to death triggers)
- Noclip fly mode (F10) with configurable speed
- Hidden spike reveal (renders invisible spike tilemaps as translucent)
- Player light controls (always-on glow, radius, intensity, color)
- Currency multiplier and give-cash hotkey (F9)

### Dash+ (`mod-dashplus/`)

Adds diagonal and vertical dashing via Harmony patches on the `Movement` class.

### Input Viz (`mod-inputviz/`)

On-screen input visualization overlay showing:

- Directional input, jump/dash/reset button presses
- Player state indicators (grounded, on wall, dash/jump availability with counts)

### Undemo (`mod-undemo/`)

Patches `isDemoBuild` checks to unlock full-game content in the demo build.

## Decompilation & Asset Extraction (`decomp/`)

Python 3.11 + [UnityPy](https://github.com/K0lb3/UnityPy) scripts for extracting and processing game data.
All paths are configured in `config.sh`; output goes to `decomp/output/`.

### Setup & Pipeline

| Script | Purpose |
|--------|---------|
| `setup.sh` | Install all dependencies: python3.11, UnityPy, Pillow, brotli, lz4, dotnet-sdk-8.0, ilspycmd |
| `config.sh` | Shared config sourced by shell scripts (game dir, data dir, output dir) |
| `extract_all.sh` | Run the 5-step pipeline: decompile -> dump assets -> extract tilemaps -> extract gameobjects -> parse course data |

### Extraction Scripts

| Script | What it reads | What it produces |
|--------|---------------|------------------|
| `decompile.sh` | `Assembly-CSharp.dll` and `Assembly-CSharp-firstpass.dll` via ilspycmd | Decompiled C# source in `output/decompiled/` |
| `dump_assets.py` | All Unity assets via `UnityPy.load(IGTAP_Data)` | `assets_summary.json` + `.txt`: object type counts, container paths, MonoScript/Texture2D/Sprite/AudioClip/AnimationClip inventories |
| `extract_tilemaps.py` | All `Tilemap` objects from level files via typetree | Per-level + combined JSON (`tilemaps_level0.json`, `tilemaps_all.json`) with tile positions, sprite/matrix/color indices, and sprite refs |
| `extract_gameobjects.py` | All `GameObject` and `Transform`/`RectTransform` objects | `gameobjects.json` (flat list with components, transform data, parent/child links) + `gameobject_hierarchy.json` (nested tree from roots) |
| `extract_sprites.py` | All `Texture2D` and `Sprite` objects | PNGs in `output/textures/` and `output/sprites/` (optional, slow) |
| `extract_tile_sprites.py` | `Tilemap` sprite arrays, resolving `PPtr<Sprite>` cross-file refs | PNGs organized as `output/tile_sprites/level{N}/{layer}/` + `tile_sprite_map.json` for the editor |
| `extract_scene_objects.py` | All `SpriteRenderer` objects, walking full transform chains, plus course save data for upgrade box position correction | `scene_objects_level{0,1}.json` with world/grid coords, sprite info, SR fields, transform chains + extracted PNGs in `output/object_sprites/` |
| `extract_all_objects.py` | Every object of every type via both typetree and OOP API | Per-file per-type JSON dumps in `output/raw_dump/` (full typetrees, no filtering) |
| `parse_course_data.py` | `StreamingAssets/MenuCourseData.txt` (game's course config JSON) | `MenuCourseData_pretty.json` + `MenuCourseData_analysis.txt` (player path stats, box positions/costs, upgrades, timing data) |
| `build_editor_data.py` | Reads only from `output/raw_dump/` (no UnityPy) + course save data | `layer_index.json`, per-layer tile data in `output/layers/`, and `scene_objects_level{0,1}.json` — all pre-processed for the browser editor |
| `patch_demo.py` | `level1` binary file, locating `upgradeBox` MonoBehaviours by path_id and script PPtr | Binary patches `isDemoBuild` byte (offset 316) from 0x01 to 0x00 on 2 gated boxes; supports `--verify` and `--revert` with automatic backup |

## Building & Installing

Prerequisites: .NET SDK 8.0+, the IGTAP demo installed via Steam.

```bash
# Build all mods
./build_all.sh

# Interactive installer: prompts for each mod, auto-installs BepInEx if missing
./install_all.sh

# Non-interactive (accept all)
./install_all.sh -y

# Build/install a single mod (auto-detects game dir, installs BepInEx if needed)
cd mod-freeplay
./build.sh
./install.sh
```

The game directory is auto-detected from common Steam install paths (Linux, macOS, Windows).
Override by passing a path: `./install_all.sh /path/to/game`.

`Directory.Build.props` provides shared MSBuild references to BepInEx, Harmony, Unity, and `Assembly-CSharp.dll` so individual `.csproj` files only need to add extra refs.

**Steam launch options** (required for BepInEx):
```
./run_bepinex.sh %command%
```

## Documentation

See [`docs/`](docs/README.md) for detailed reverse engineering notes:

- [Game overview](docs/overview.md) - tech stack, key stats
- [Code structure](docs/code-structure.md) - all 50 decompiled classes
- [Game mechanics](docs/game-mechanics.md) - movement, clones, upgrades, currencies
- [Tilemap layers](docs/tilemap-layers.md) - all 38 layers with tile counts
- [Map layout](docs/map-layout.md) - scene hierarchy, zones, courses
- [Coordinate system](docs/coordinate-system.md) - world/grid mapping
- [Rendering](docs/rendering.md) - sprites, camera, lighting, particles
- [Save system](docs/save-system.md) - SaveableObject pattern, file schemas
- [File formats](docs/file-formats.md) - Unity binary formats, save data JSON
- [Editor](docs/editor.md) - browser-based level editor
- [Demo restrictions](docs/demo-restrictions.md) - what the demo locks down
