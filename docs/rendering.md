# Rendering & Sprites

## Rendering Pipeline

The game uses Unity's **Universal Render Pipeline (URP)** in 2D mode.

### Render Order (back to front)

1. **Background** tilemap layer
2. **Environmental objects_back** (behind ground)
3. **OOB areas** (out-of-bounds background fill)
4. **lightBlocker** (shadow casters for dark area)
5. **ground** (main terrain)
6. **Colored blocks** (blueBlocks, orangeBlocks - toggled by gameplay)
7. **Ilusorywalls** (fake walls, rendered same as ground)
8. **Spikes**, hiddenSpikes, blueSpikes, orangeSpikes (hazards)
9. **Environmental objects** (main decoration)
10. **Player sprite** (animated character)
11. **Clone sprites** (replayed paths)
12. **environmentalObjects_front** (foreground decoration)
13. **Particle systems** (death, landing, dash effects)
14. **UI Canvas** (HUD, upgrade boxes, menus)

### Tilemap Rendering

Each tilemap layer has a `TilemapRenderer` component that determines sort order.
Tiles are rendered in a single draw call per visible chunk using Unity's built-in
tilemap batching.

## Sprite Assets

### Tile Sprites

- **Total:** 7,875 sprite assets in the game
- **Tile sprites extracted:** 8,128 (for the editor, including both levels)
- **Typical size:** 32x32 pixels, RGBA
- **Format:** Various compressed formats (ASTC, ETC2, DXT) decoded to RGBA on extraction
- **Source textures:** Sprite atlas sheets in `sharedassets0.assets`

### Sprite Naming Convention

Most tile sprites follow the pattern `{SheetName}_{Index}`:
- `Asset_Sheet_227`: Metal platform tile from the main asset sheet
- `Spikes Tileset_7`: Spike variant from the spikes spritesheet
- `Plant5_00003_0`: Plant decoration from vegetation atlas
- `Mossy - Hanging Plants_1`: Moss/plant decoration
- `gate 1_0000_0`: Gate animation frame (1920x1080)

### Sprite-to-Tile Mapping

Each tilemap layer has a `m_TileSpriteArray` that maps sprite indices to actual
Sprite objects via PPtr references:

```
Tilemap (e.g., ground, path_id=2926)
  m_TileSpriteArray[300 entries]:
    [0]: null (unused)
    [1]: null (unused)  
    [2]: -> sharedassets0.assets, path_id=806  -> "Asset_Sheet_239" (32x32)
    [3]: -> sharedassets0.assets, path_id=2591 -> "Asset_Sheet_226" (32x32)
    [4]: -> sharedassets0.assets, path_id=881  -> "Asset_Sheet_227" (32x32)
    ...
```

Each tile references a `sprite_index` (field `si`) into this array.

## Tile Transforms

Tiles can be rotated and flipped using a 2x2 affine matrix stored per-tilemap
in the `matrices` array. Each tile's `matrix_index` (field `mi`) selects which
matrix to apply.

### Matrix Format

Stored as `[e00, e01, e10, e11]` representing the 2D portion of Unity's 4x4 matrix:

```
| e00  e01 |     Applied as:
| e10  e11 |     x' = e00 * x + e01 * y
                 y' = e10 * x + e11 * y
```

### Standard Transforms

| e00 | e01 | e10 | e11 | Name | Angle |
|-----|-----|-----|-----|------|-------|
| 1 | 0 | 0 | 1 | Identity | 0 |
| 0 | 1 | -1 | 0 | Rotate 90 CW | 90 |
| -1 | 0 | 0 | -1 | Rotate 180 | 180 |
| 0 | -1 | 1 | 0 | Rotate 90 CCW | 270 |
| -1 | 0 | 0 | 1 | Flip Horizontal | - |
| 1 | 0 | 0 | -1 | Flip Vertical | - |
| 0 | 1 | 1 | 0 | Flip H + 90 CW | - |
| 0 | -1 | -1 | 0 | Flip H + 90 CCW | - |

### Canvas Rendering of Transforms

When rendering in a web canvas (Y-axis flipped vs Unity), the matrix must be
converted. Given Unity matrix `[e00, e01, e10, e11]`, the canvas 2D transform is:

```javascript
// Canvas transform(a, b, c, d, e, f):
//   x_screen = a*x + c*y + e
//   y_screen = b*x + d*y + f
//
// Converting from Unity (Y-up) to Canvas (Y-down):
ctx.transform(e00, -e10, -e01, e11, 0, 0);
```

Applied around the tile center:
```javascript
ctx.save();
ctx.translate(centerX, centerY);
ctx.transform(mat[0], -mat[2], -mat[1], mat[3], 0, 0);
ctx.drawImage(img, -halfSize, -halfSize, size, size);
ctx.restore();
```

## Scene Object Rendering

Non-tilemap GameObjects with SpriteRenderers (501 total across both scenes)
include upgrade boxes, checkpoints, springs, gates, decorations, and statues.

### In the Editor
- Objects render on top of all tilemap layers at their correct in-game size
- Size formula: `grid_size = (sprite_pixels * cumulative_scale) / 32`
- Negative `scale_x` values flip sprites horizontally (mirrored decorations)
- Objects without extracted sprites show as magenta diamond markers
- Labels toggleable via "Labels" checkbox; objects toggleable via "Objects" checkbox
- Click to select — shows properties and sprite preview in the panel

### Coordinate Mapping
Scene objects use world coordinates while tilemaps use grid coordinates.
The Grid's cellSize=(32,32) and position=(0,-23) define the mapping:
- `grid_x = world_x / 32`
- `grid_y = (world_y + 23) / 32`

In this game, 1 pixel = 1 world unit (PPU=1), so a 96x36 sprite occupies
96x36 world units = 3x1.125 grid cells.

### Cumulative Scale
Each scene object stores the product of all ancestor transform scales.
This affects the final rendered size:
- Upgrade boxes: local scale 1.5x (appear 50% larger)
- CoolStatue: local scale 2.0x (518x530 px sprite → ~32x33 grid cells)
- Some decorations use negative X scale (-1.0) for horizontal mirroring

### Sprite Sources
Scene object sprites come from `sharedassets0.assets`, resolved through
the SpriteRenderer's `m_Sprite` PPtr reference. 32 unique sprites are
extracted to `output/object_sprites/`. Sizes vary:
- Tile-sized: 32x32 (checkpoints, gates)
- Medium: 96x36 (springs), 96x96 (decor beams)
- Large: 518x530 (CoolStatue), 434x659 (backgrounds)

### Level 0 vs Level 1 Object Positions
Level 1 has correct object positions (complete `Courses` parent hierarchy).
Level 0 has broken Y coordinates for course-embedded objects because
`course 1` is a root transform missing the parent offset, causing objects
to appear ~37 tiles underground. The editor uses Level 1 objects for both.

## Player Rendering

### Player Sprite
- Animated via Unity Animator with multiple animation clips
- Facing direction controlled by X scale (-1 for left, 1 for right)
- Child of PlayerObject/Player/Sprite

### Afterimages
- Pool of 21 `SpriteRenderer` objects under PlayerObject/Afterimages
- Activated during dash, fade out over time
- Each copies the player's current sprite and position

### Clone Rendering
- Each clone is a `SpriteRenderer` + `ParticleSystem`
- Sprite updated each frame from recorded `cloneSprites` array
- Scale from recorded `cloneScales` (includes facing direction)
- `cloneBigness` modifier scales the clone larger
- `cloneFastness` modifier speeds up path replay
- Death particles play at end of recorded path using `cloneEndVelocity`

## Lighting

### Global Light
- Scene-wide 2D light for base illumination
- Managed by URP 2D lighting system

### Player Light
- Activated when breaker is tripped
- Radius configurable via `increasedWatts` upgrade
- Attached to Player/Light object
- Color and intensity lerp when areas are fixed

### Light Blockers
- `lightBlocker` tilemap layer contains shadow-casting tiles
- Located in the breaker/dark area (X: 397-673, Y: -19 to 271)
- Creates shadows that the player light interacts with

## Particle Systems

| Object | Location | Purpose |
|--------|----------|---------|
| sweatParticles | Player | Exertion effect |
| MetalLandParticles | Player | Landing on metal ground |
| GrassLandParticles | Player | Landing on grass ground |
| MetalWallParticles | Player | Wall sliding on metal |
| GrassWallParticles | Player | Wall sliding on grass |
| endParticles | Per-course Clones | Clone death at path end |
| Upgrade box particles | Per-box | Purchase visual feedback |

## Camera

### CameraMovement Script
- Smooth follow of player position
- **Static camera zones:** Trigger areas that lock camera to fixed position
- **Camera size triggers:** Areas that change orthographic size (zoom level)
- **Camera shake:** Event-driven screen shake
- Respects zone boundaries to prevent seeing out-of-bounds areas
