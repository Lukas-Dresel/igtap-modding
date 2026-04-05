# Coordinate System & Rendering

How the game positions, scales, and renders everything.

## World Coordinates

The game uses direct world units with no global PPU constant in code.
Pixel-perfect rendering is enforced by rounding camera position to integers
every frame (line 2749).

### Grid

- Grid cellSize = (32, 32) — each tilemap cell is 32x32 world units
- Grid transform at (0, -23) — vertical offset
- Tile at grid (x, y) → world (x*32, y*32 - 23)
- There are 3 Grid objects: primary (32x32), biggerGrid (64x64), The Tree (64x64)

### Camera

- Default orthographic size: 500 (visible height = 1000 world units = 31.25 tiles)
- Camera follows player with lerp, position rounded to integers each frame
- Camera Z always -100
- Camera size changes in certain zones (camSizeTrigger objects)

### Coordinate Spaces Used

| Space | Used By | Notes |
|-------|---------|-------|
| World position | Upgrade boxes, checkpoints, gates, decorations | `transform.position` set directly from save data |
| Course-relative | Clone paths | Path recorded as `player.position - course.position`, both rounded to int |
| Local position | Clone rendering | Clones use `transform.localPosition` since they're children of clonesScript |

## Transform Modifications at Runtime

### Objects That Get Repositioned

| Object | How | Line | Space |
|--------|-----|------|-------|
| **Upgrade boxes** | `component.transform.position = saveObject._boxPositions[l]` | 3849 | World |
| **Player** | `base.transform.position = respawnPoint` | 5171 | World |
| **Clones** | `activeClones[k].transform.localPosition = clonePath[num7]` | 3237 | Local (parent=clonesScript) |
| **Parallax backgrounds** | `base.transform.position = startPos + cam.pos * factor` | 2670-2673 | World |
| **Tree growth** | `trunk.transform.position = defaultPos + offset` | 7236 | World |
| **Box spawn animation** | `box.transform.position += (0, 2000, 0)` then lerp back | 7262-7267 | World |

### Objects That Stay at Serialized Position

Springs, checkpoints, start/end gates, decorations, the statue — these do NOT have
runtime position modifications in code. Their serialized transform positions ARE their
runtime positions.

### Player Respawn Points (hardcoded, line 5098-5134)

```
Course 2: (4333, 1885)
Course 3: (9249, 6966)
Course 4: (19863, 10545)
Course 5: (-1053, -180)
Area 2:   (16608, 5066)
```
These are world coordinates.

## Sorting Order

Objects are sorted for rendering via Unity's built-in sorting:
- TilemapRenderer sorting orders set in code for block swap:
  - Active tilemap: sortingOrder = 8 (line 3410)
  - Inactive tilemap: sortingOrder = -4 (line 3418)
- SpriteRenderers use their serialized `m_SortingOrder` field
- Negative sorting orders render behind, positive render in front

## Sprite Sizing

In Unity: `world_size = sprite_pixels / PPU * transform.localScale`

The code uses `SpriteRenderer.bounds.size` (lines 2664-2665, 7222) which returns
the world-space dimensions: `sprite_rect / PPU * lossyScale`.

No PPU constant in game code — PPU is set per-sprite in the asset import settings.
From the raw dump data:
- Tile sprites (Asset_Sheet_*): PPU = 1.0 (32px = 32 world units = 1 tile)
- Most scene object sprites: PPU = 1.0
- Some imported textures (plants): PPU = 100.0

## Parallax

The `backgroundScroller` class (lines 2638-2675):
```
startposX = transform.position.x  // captured at Start()
each frame: transform.position.x = startposX + camera.position.x * paralaxFactorX
same for Y axis independently
```
Each background sprite has its own parallax factor for X and Y.

## Spring Rendering

Springs do NOT modify their transform. `SpringScript` (lines 7038-7081):
- Has an Animator that plays a trigger animation on bounce
- Calls `Movement.hitSpring(upForce, strength, movementLock, transform.up)` on player
- `transform.up` gives the spring's upward direction based on its ROTATION
- The spring's orientation is entirely from its serialized Transform rotation

## Course Path Recording

`courseScript.RecordFrame()` (lines 3619-3625):
```csharp
Vector2 item = new Vector2(
    Mathf.Round(player.transform.position.x),
    Mathf.Round(player.transform.position.y));
item -= new Vector2(
    Mathf.Round(base.transform.position.x),
    Mathf.Round(base.transform.position.y));
currentPlayerPath.Add(item);
```

Paths are **course-relative** (player world pos minus course world pos, both rounded).
Clones replay these as `localPosition` since they're children of the course's Clones object.

## Upgrade Box Save/Load

Saving (line 3760): `list6.Add(component.transform.position)` — saves world position
Loading (line 3849): `component.transform.position = saveObject._boxPositions[l]` — sets world position

Box positions in save data are **world space coordinates**.
