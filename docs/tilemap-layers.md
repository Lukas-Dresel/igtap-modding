# Tilemap Layers

The game uses Unity's Tilemap system with multiple layers for different purposes.
Each tilemap is a child of a `Grid` GameObject. Tiles are 1x1 unit in world space,
rendered as 32x32 pixel sprites.

## Layer Categories

### Collision Layers

| Layer | Level 0 | Level 1 | Description |
|-------|---------|---------|-------------|
| **ground** | 26,651 | 26,648 | Main solid terrain. Metal/stone platforms. 298 unique sprites. |
| **blueBlocks** | 107 | 110 | Color-swappable blocks (solid when blue active). |
| **orangeBlocks** | 62 | 62 | Color-swappable blocks (solid when orange active). |
| **Ilusorywalls** | 89 | 9 | Appear solid but player passes through. Secret passages. |
| **InvisibleWall** | 6 | 6 | Invisible collision boundaries at world edges. |

### Hazard Layers

| Layer | Level 0 | Level 1 | Description |
|-------|---------|---------|-------------|
| **Spikes** | 4,336 | 4,331 | Main spike hazards. Rotated via matrix transforms. 30 sprites. |
| **hiddenSpikes** | 638 | 641 | Concealed spikes (initially invisible to player). |
| **blueSpikes** | 81 | 78 | Active only when blue blocks are active. |
| **orangeSpikes** | 39 | 39 | Active only when orange blocks are active. |
| **backgroundSpikes1** | 16 | 16 | Decorative background spikes (no collision). |
| **backgroundSpikes2** | 16 | 16 | Decorative background spikes (no collision). |

### Decoration Layers

| Layer | Level 0 | Level 1 | Description |
|-------|---------|---------|-------------|
| **Environmental objects** | 21,420 | 21,487 | Main decoration layer. Plants, structures. 914 sprites. |
| **Environmental objects_back** | 6,720 | 6,722 | Background decorations behind ground. |
| **environmentalObjects_front** | 147 | 169 | Foreground decorations in front of player. |
| **Background** | 2,711 | 2,727 | Distant background elements. |
| **OOB areas** | 96,099 | 99,277 | Out-of-bounds fill / kill zone background. 2,416 sprites. |

### Lighting Layers

| Layer | Level 0 | Level 1 | Description |
|-------|---------|---------|-------------|
| **lightBlocker** | 1,574 | 1,584 | Shadow-casting tiles for the dark area. |

### Special Layers (Level 1 only)

| Layer | Tiles | Description |
|-------|-------|-------------|
| **moss** | 2,407 | Moss/vegetation overlay layer. |
| **Tree1Ground** | 13 | Progressive tree stage 1 (grows with TreeGrowth upgrade). |
| **Tree2Ground** | 13 | Progressive tree stage 2. |
| **Tree3Ground** | 20 | Progressive tree stage 3. |

## Tile Data Structure

Each tile stores:

| Field | Key | Description |
|-------|-----|-------------|
| Position | `x`, `y`, `z` | Grid coordinates (integers) |
| Tile Index | `ti` | Index into the tilemap's `m_TileAssetArray` (the TileBase ScriptableObject) |
| Sprite Index | `si` | Index into the tilemap's `m_TileSpriteArray` (the visual sprite) |
| Matrix Index | `mi` | Index into the tilemap's `matrices` array (rotation/flip transform) |
| Color Index | `ci` | Index into the tilemap's `m_TileColorArray` (color tint) |
| Flags | `fl` | Unity's AllTileFlags bitmask |

## Tile Transforms (Matrix Array)

Each tilemap has a `matrices` array containing 2D affine transform matrices stored
as `[e00, e01, e10, e11]`. The Unity 4x4 matrix maps as:

```
| e00  e01 |    x' = e00*x + e01*y
| e10  e11 |    y' = e10*x + e11*y
```

### Common Transforms

| Matrix | Name | Description |
|--------|------|-------------|
| `[1, 0, 0, 1]` | Identity | No transform |
| `[-1, 0, 0, -1]` | Rotate 180 | Upside down |
| `[0, 1, -1, 0]` | Rotate 90 CW | Quarter turn clockwise |
| `[0, -1, 1, 0]` | Rotate 90 CCW | Quarter turn counter-clockwise |
| `[-1, 0, 0, 1]` | Flip Horizontal | Mirror left-right |
| `[1, 0, 0, -1]` | Flip Vertical | Mirror top-bottom |
| `[0, 1, 1, 0]` | Flip H + Rot 90 CW | Combined |
| `[0, -1, -1, 0]` | Flip H + Rot 90 CCW | Combined |

### Transform Usage Example (Spikes layer, Level 0)

The Spikes layer has 8 matrices, heavily used:
- Matrix 0 (identity): 2,727 tiles (spikes pointing up)
- Matrix 3 (90 CCW): 864 tiles (spikes pointing right)
- Matrix 2 (90 CW): 482 tiles (spikes pointing left)
- Matrix 1 (180): 249 tiles (spikes pointing down)
- Matrices 4-7: 14 tiles total (rare flip combinations)

## World Coordinate Ranges

### Level 0

| Layer | X range | Y range |
|-------|---------|---------|
| ground | -178 to 815 | -51 to 571 |
| Spikes | -489 to 791 | -99 to 571 |
| OOB areas | -115 to 928 | -52 to 475 |
| Environmental objects | -172 to 776 | -53 to 464 |

Overall playable area: roughly **X: -200 to 800, Y: -100 to 600** (1000x700 tiles).

### Level 1

Similar ranges, with additions:
- `moss`: X 451-618, Y 41-132 (small region)
- `Tree*Ground`: X -6 to 4, Y -6 to -3 (tiny, near origin)

## Sprite Statistics

| Layer | Unique Sprites (L0) | Unique Sprites (L1) |
|-------|--------------------|--------------------|
| ground | 298 | 297 |
| Environmental objects | 914 | 916 |
| OOB areas | 2,416 | 2,416 |
| Environmental objects_back | 162 | 164 |
| Background | 57 | 68 |
| Spikes | 30 | 30 |
| blueBlocks | 12 | 12 |
| hiddenSpikes | 8 | 7 |

Most tile sprites are 32x32 pixels. Environmental objects include larger sprites
(plants, structures) that span multiple visual tiles.
