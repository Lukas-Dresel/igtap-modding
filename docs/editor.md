# Level Editor

A web-based visual level editor for viewing and editing IGTAP tilemap data.

## Running the Editor

```bash
cd ~/lukas/games/igtap
bash serve_editor.sh [port]    # default port 8080
# Open http://localhost:8080/editor.html
```

Or manually:
```bash
cd ~/lukas/games/igtap
python3 -m http.server 8080 --bind 127.0.0.1
```

## Controls

### Navigation
| Input | Action |
|-------|--------|
| WASD / Arrow keys | Pan camera |
| Scroll wheel | Zoom in/out |
| Middle-click drag | Pan camera |
| Right-click drag | Pan camera |
| + / - | Zoom in/out |
| 0 | Reset zoom to 4x |

### Tools
| Key | Tool | Description |
|-----|------|-------------|
| V | Select | Click tiles to inspect properties |
| H | Pan | Click-drag to pan |
| B | Paint | Click/drag to place tiles on active layer |
| E | Erase | Click/drag to remove tiles from active layer |

### Other
| Input | Action |
|-------|--------|
| Ctrl+S | Export current level as JSON |

## Interface

### Sidebar (Left)
- **Level picker:** Switch between Level 0 (17 layers) and Level 1 (21 layers)
- **Tool buttons:** Select, Pan, Paint, Erase
- **Layer list:** All tilemap layers with:
  - Eye icon: toggle visibility
  - Color swatch: fallback color for that layer
  - Layer name: click to select as active layer
  - Tile count: number of tiles in the layer

### Canvas (Center)
- Renders all visible tilemap layers with actual tile sprites
- Renders scene objects (upgrade boxes, checkpoints, springs, gates, decorations)
  on top of tilemap layers at their correct in-game size and position
- Negative scale values flip sprites horizontally (mirrored decorations)
- Grid overlay at high zoom (toggleable)
- Origin axes shown as red (X) and green (Y) lines
- Hover highlight shows current tile position
- Selected tile highlighted with red border
- Selected object highlighted with magenta border

### Top Bar
- **Zoom:** current zoom percentage
- **Grid:** toggle grid overlay
- **Objects:** toggle scene object visibility (upgrade boxes, checkpoints, etc.)
- **Labels:** toggle object name labels
- **Tile:** shows tile/sprite/matrix index of hovered tile
- **Coordinates:** current mouse position in world coordinates
- **Sprites:** loading progress counter

### Properties Panel (Right)
- **Tile properties:** position, indices, transform name, matrix values
- **Object properties:** name, grid position, world position, sprite preview
- **Sprite preview:** enlarged view of selected tile or object's sprite
- **Layer properties:** tile count, origin, size, bounding box, sprite count

## Rendering Details

### Sprite Loading
- Sprites load lazily as tiles come into view
- Up to 50 sprites load concurrently
- Progress shown in top bar ("Sprites: loaded/total")
- Fallback: colored rectangles when sprites haven't loaded yet

### Zoom Levels
- **< 1.5 px/tile:** Single-pixel dots with layer color (fast)
- **1.5-3 px/tile:** Colored rectangles only
- **3+ px/tile:** Actual sprite images rendered
- **8+ px/tile:** Grid lines become visible
- **16+ px/tile:** Pixel-perfect rendering (imageSmoothingEnabled = false)

### Tile Transforms
Sprites are rotated/flipped according to their matrix index. The Unity 2D
matrix `[e00, e01, e10, e11]` is converted to canvas coordinates:
```
canvas.transform(e00, -e10, -e01, e11, 0, 0)
```
Applied around the tile center for correct rotation.

### Scene Object Sizing

Scene objects render at their correct in-game size:
```
screen_size = (sprite_pixels * world_scale) / 32 * zoom
```
- `sprite_pixels`: image dimensions stored in the JSON (e.g., 96x36 for spring)
- `world_scale`: cumulative transform scale from the hierarchy (e.g., 1.5x for upgrade boxes)
- `/32`: converts world units to grid cells (Grid cellSize=32)
- Negative `scale_x` flips the sprite horizontally

### Layer Opacity
- **Active layer:** 100% opacity
- **Inactive layers:** 60% opacity
- **Hidden layers:** not rendered

## Data Flow

```
Unity Game Files
  |
  v
extract_tile_sprites.py   -->  output/tile_sprites/{level}/{layer}/{idx}.png
extract_tilemaps.py        -->  output/layers/level{N}_{name}.json
                           -->  output/layer_index.json
                           -->  output/tilemap_sprite_index.json
extract_scene_objects.py   -->  output/scene_objects_level{N}.json
                           -->  output/object_sprites/{name}.png
  |
  v
editor.html (served via HTTP)
  |
  +-- Loads layer_index.json (tilemap layer metadata)
  +-- Loads tilemap_sprite_index.json (tile sprite path mapping)
  +-- Loads scene_objects_level0.json + scene_objects_level1.json (376 objects)
  +-- Lazily loads layers/level{N}_{name}.json (tile data per layer)
  +-- Lazily loads tile_sprites/.../{idx}.png (tile sprite images)
  +-- Preloads object_sprites/{name}.png (scene object sprite images)
```

## Export Format

Ctrl+S exports the current level as `igtap_level{N}_export.json`:

```json
[
  {
    "name": "ground",
    "path_id": 2926,
    "tiles": [
      {"x": -178, "y": -51, "z": 0, "ti": 3, "si": 4, "mi": 0, "ci": 0, "fl": 1},
      ...
    ]
  },
  ...
]
```

## Level Selection

The editor defaults to **Level 1**, which has the most complete data:
- 21 tilemap layers (vs 17 in level0) including moss and tree layers
- 376 scene objects with correct positions

Level 0 has broken scene object positions because its course GameObjects are
root transforms missing the `Courses` parent offset (e.g., upgrade boxes render
~37 tiles underground). The editor uses Level 1's scene objects for both levels
since they share the same world layout.

## Limitations

- **No undo/redo** for paint/erase operations
- **Paint tool** places tiles with default indices (ti=0, si=0, mi=0)
- No sprite picker / palette for selecting which sprite to paint
- No import back to Unity format (export is JSON only)
- Large layers (OOB areas: 96K+ tiles) may cause performance issues at high zoom
- Some scene objects have runtime-repositioned transforms (e.g., upgrade box Y
  positions may differ slightly from actual gameplay due to save data overrides)
