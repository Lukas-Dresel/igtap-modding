# IGTAP Modding & Analysis Toolkit

Reverse engineering analysis, modding framework, and level editor tooling for
**IGTAP: An Incremental Game That's Also a Platformer** (Demo, Unity 6).

## Repository Structure

```
igtap/
├── mod/                         # Core mod (BepInEx plugin) - debug menu, HUD, shared APIs
├── mod-freeplay/                # Freeplay mod - god mode, noclip, speed tweaks, hidden spike reveal
├── mod-dashplus/                # Dash+ mod - diagonal and vertical dashing
├── mod-inputviz/                # Input Viz mod - on-screen input/status overlay
├── mod-minimap/                 # Minimap mod - in-game map overlay with multiple view modes
├── mod-IMPORTED-HitboxViewer/   # (Imported) Hitbox Viewer - collision volume visualization
├── mod-IMPORTED-IGTAS/          # (Imported) IGTAS - tool-assisted speedrun input recording/playback
├── decomp/                      # Scripts for decompilation
├── Directory.Build.props        # Shared MSBuild config (game paths, BepInEx/Unity refs)
├── _common.sh                   # Shared shell helpers (game detection, BepInEx install)
├── build_all.sh                 # Build all mods
└── install_all.sh               # Install all mods to game directory
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

Sandbox mod with BepInEx config entries for everything:

- Speed/jump multipliers, extra air dashes/jumps, infinite dashes/jumps/wall jumps
- God mode (immune to death triggers)
- Noclip fly mode (F10) with configurable speed
- Hidden spike reveal (renders invisible spike tilemaps as translucent)
- Player light controls (always-on glow, radius, intensity, color)
- Currency multiplier

### Dash+ (`mod-dashplus/`)

Adds diagonal and vertical dashing via Harmony patches on the `Movement` class.

### Input Viz (`mod-inputviz/`)

On-screen input visualization overlay showing:

- Directional input, jump/dash/reset button presses
- Player state indicators (grounded, on wall, dash/jump availability with counts)

### Minimap (`mod-minimap/`)

In-game minimap/map overlay with real-time tilemap rendering. Features:

- Three view modes: full world, follow player (with configurable zoom), current course
- Configurable position (screen corner), size, and opacity
- Toggle with F6, cycle view modes with F5, settings in debug menu (F8)

### Imported Mods

These are third-party mods imported into the repo for convenience. They are standalone BepInEx plugins and do not depend on the core mod.

#### Hitbox Viewer (`mod-IMPORTED-HitboxViewer/`)

Real-time collision volume visualization with color-coded outlines:

- Supports 3D colliders (Box, Sphere, Capsule, CharacterController, Mesh) and 2D colliders (Box, Circle, Polygon)
- NavMeshObstacle visualization, trigger/non-trigger filtering
- UniverseLib-powered menu UI (F4)

#### IGTAS (`mod-IMPORTED-IGTAS/`)

Tool-assisted speedrun plugin for frame-precise input recording and playback:

- Record (F6), stop (F7), playback (F8) with slowdown mode (F5)
- Frame editor with insert/remove/navigate (F9-F11, arrow keys)
- Saves recorded inputs to files; keyboard-only (no controller support)

## Decompilation (`decomp/`)

Scripts for decompiling game assemblies. Paths are configured in `config.sh`; output goes to `decomp/output/`.

| Script | Purpose |
|--------|---------|
| `setup.sh` | Install dependencies: dotnet-sdk-8.0, ilspycmd |
| `config.sh` | Shared config sourced by shell scripts (game dir, data dir, output dir) |
| `decompile.sh` | Decompile `Assembly-CSharp.dll` and `Assembly-CSharp-firstpass.dll` via ilspycmd into `output/decompiled/` |

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
