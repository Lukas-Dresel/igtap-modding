# Extraction Scripts

Scripts for extracting and processing IGTAP game data. All scripts are in
`~/lukas/games/igtap/` and output to `~/lukas/games/igtap/output/`.

## Quick Start

```bash
cd ~/lukas/games/igtap

# 1. Install dependencies
bash setup.sh

# 2. Run the full extraction pipeline
bash extract_all.sh

# 3. Extract tile sprites (optional, slow)
python3.11 extract_tile_sprites.py

# 4. Extract all sprite images (optional, very slow)
python3.11 extract_sprites.py

# 5. Launch the level editor
bash serve_editor.sh
```

## Dependencies

Installed by `setup.sh`:

| Dependency | Version | Purpose |
|------------|---------|---------|
| python3.11 | 3.11+ | Script runtime |
| UnityPy | 1.25.0 | Parse Unity asset files |
| Pillow | latest | Image processing for sprite extraction |
| brotli | latest | Compression support for UnityPy |
| lz4 | latest | Compression support for UnityPy |
| dotnet SDK | 8.0 | Required for ILSpy decompiler |
| ilspycmd | 9.1.0 | .NET assembly decompiler |

## Script Reference

### setup.sh
Installs all system and Python dependencies.
- Checks for python3.11, pip, dotnet SDK
- Installs missing system packages via apt
- Installs Python packages via pip
- Installs ilspycmd via dotnet tool

### config.sh
Shared configuration sourced by other shell scripts.
- `GAME_DIR`: path to Steam game directory
- `DATA_DIR`: path to IGTAP_Data
- `OUTPUT_DIR`: path to output directory

### extract_all.sh
Runs the full extraction pipeline in 5 steps:
1. `decompile.sh` - Decompile C# assemblies
2. `dump_assets.py` - Asset summary
3. `extract_tilemaps.py` - Tilemap data
4. `extract_gameobjects.py` - Scene hierarchy
5. `parse_course_data.py` - Course save data

### decompile.sh
Decompiles .NET assemblies using ILSpy.

**Input:**
- `IGTAP_Data/Managed/Assembly-CSharp.dll`
- `IGTAP_Data/Managed/Assembly-CSharp-firstpass.dll`

**Output:**
- `output/decompiled/Assembly-CSharp/Assembly-CSharp.decompiled.cs` (8,563 lines)
- `output/decompiled/Assembly-CSharp-firstpass/Assembly-CSharp-firstpass.decompiled.cs`

### dump_assets.py
Loads all Unity assets and produces a summary.

**Output:**
- `output/assets_summary.json` - Structured data (type counts, scripts, textures, sprites, audio)
- `output/assets_summary.txt` - Human-readable summary

### extract_tilemaps.py
Extracts all tilemap data from level0 and level1 scene files.

**Output:**
- `output/layer_index.json` - Metadata index for all 38 tilemap layers
- `output/layers/level{N}_{name}.json` - Per-layer tile data with matrices
- `output/tilemaps_all.json` - Combined output (67 MB)

**Layer file format:**
```json
{
  "name": "ground",
  "path_id": 2926,
  "matrices": [[1,0,0,1], [0,1,-1,0], ...],
  "tiles": [{"x":0, "y":0, "z":0, "ti":0, "si":0, "mi":0, "ci":0, "fl":1}, ...]
}
```

### extract_gameobjects.py
Extracts the full GameObject hierarchy from both scenes.

**Output:**
- `output/gameobjects.json` - Flat list of 1,671 GameObjects with components and transforms
- `output/gameobject_hierarchy.json` - Nested tree with 52 root objects

### parse_course_data.py
Pretty-prints the MenuCourseData.txt save file.

**Output:**
- `output/MenuCourseData_pretty.json` - Formatted JSON
- `output/MenuCourseData_analysis.txt` - Human-readable field analysis

### extract_tile_sprites.py
Extracts the actual sprite images for each tilemap tile.

**Process:**
1. Iterates over all Tilemap objects in both levels
2. For each tilemap, reads the `m_TileSpriteArray` PPtr references
3. Resolves references through external file table (FileID -> externals[FileID-1])
4. Extracts sprite images from `sharedassets0.assets`
5. Saves as PNG indexed by sprite_index

**Output:**
- `output/tile_sprites/level{N}/{layer_name}/{sprite_index}.png`
- `output/tilemap_sprite_index.json` - Mapping from path_id -> sprite files

**Stats:** 8,128 sprites extracted across 38 tilemaps.

### extract_scene_objects.py
Extracts all non-tilemap GameObjects with SpriteRenderers (upgrade boxes,
checkpoints, springs, gates, decorations, statues, etc.) with world positions
converted to tilemap grid coordinates.

**Process:**
1. Iterates over all SpriteRenderer components in both level files
2. For each, resolves the parent GameObject name
3. Walks the Transform hierarchy to compute world position
4. Converts world coordinates to tilemap grid coordinates using the
   Grid cellSize (32x32) and Grid transform offset (0, -23):
   `grid_x = world_x / 32`, `grid_y = (world_y + 23) / 32`
5. Resolves and extracts the sprite image from sharedassets0.assets

**Output:**
- `output/scene_objects_level0.json` - 123 objects with positions and sprite refs
- `output/scene_objects_level1.json` - 376 objects with positions and sprite refs
- `output/object_sprites/{name}.png` - 32 unique sprite images

**Object entry format:**
```json
{
  "name": "upgradeBox (3)",
  "go_path_id": 2750,
  "x": 193.16,
  "y": 214.31,
  "world_x": 6181.0,
  "world_y": 6835.0,
  "enabled": 1,
  "sprite": "object_sprites/Dot_Ceasar_0.png",
  "sprite_name": "Dot Ceasar_0"
}
```

### patch_demo.py
Patches the demo's `isDemoBuild` flags to unlock gated content.
See [demo-restrictions.md](../docs/demo-restrictions.md) for full details.

```bash
python3.11 patch_demo.py            # patch (backs up level1)
python3.11 patch_demo.py --verify   # check state
python3.11 patch_demo.py --revert   # restore backup
```

### extract_sprites.py
Extracts ALL sprite and texture images from the game (not just tiles).

**Output:**
- `output/sprites/{name}.png` - All 7,875 sprite images
- `output/textures/{name}.png` - All 184 texture atlases

**Note:** This is slow and produces large output. Most sprites are already
covered by `extract_tile_sprites.py` for editor use.

### serve_editor.sh
Starts a local HTTP server for the level editor.

```bash
bash serve_editor.sh [port]  # default: 8080
```

## Output Directory Structure

```
output/
  assets_summary.json          # Asset type inventory
  assets_summary.txt           # Human-readable summary
  layer_index.json             # Tilemap layer metadata (38 entries)
  tilemap_sprite_index.json    # Sprite path mapping per tilemap
  tilemaps_all.json            # All tilemaps combined (67 MB)
  scene_objects_level0.json    # Scene objects for level0 (123 objects)
  scene_objects_level1.json    # Scene objects for level1 (376 objects)
  MenuCourseData_pretty.json   # Course save data
  MenuCourseData_analysis.txt  # Save data analysis
  gameobjects.json             # Flat GO list (1,671 objects)
  gameobject_hierarchy.json    # Nested GO tree (52 roots)
  decompiled/
    Assembly-CSharp/           # Main game code
    Assembly-CSharp-firstpass/ # Generated metadata
  layers/
    level0_ground.json         # Per-layer tile data
    level0_Spikes.json
    level1_ground.json
    ...                        # 38 files total
  tile_sprites/
    level0/
      ground/                  # 298 sprites
        2.png, 3.png, ...
      Spikes/                  # 30 sprites
      Environmental_objects/   # 914 sprites
      ...
    level1/
      ...
  object_sprites/              # Scene object sprites (32 files)
    Spring_head_0.png
    Dot_Ceasar_0.png
    Background.png
    ...
```

## Troubleshooting

### UnityPy import errors
If `import UnityPy` fails with module errors:
```bash
pip3.11 install --force-reinstall UnityPy Pillow brotli lz4
```

### ilspycmd not found
```bash
dotnet tool install -g ilspycmd
export PATH="$PATH:$HOME/.dotnet/tools"
```

### Port already in use
```bash
bash serve_editor.sh 8099  # use a different port
```

### Large file performance
The `tilemaps_all.json` (67 MB) and OOB layers (96K+ tiles) are large.
The editor loads layers lazily starting with smallest first. Consider hiding
the `OOB areas` layer for better performance.
