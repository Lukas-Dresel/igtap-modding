# Map Layout & Scene Hierarchy

## Scene Structure

The game has 2 Unity scenes (`level0` and `level1`) representing the same world in
different states (likely before/after certain progression milestones). Level 1 has
additional layers (moss, trees) and slight tile count differences.

### Root GameObject Hierarchy

```
Scene Root
  Atom                        # Atom crafting collectible (pos: 63, 0)
  DebugUI*                    # Debug overlay (multiple prefabs)
  HUD                         # Heads-up display
    MiniDisplay               # Compact currency readout
    FullCurrencyPopout        # Expanded currency panel
  Zone 1                      # Primary playable zone container
    Controls (x2)             # Input prompt displays
    zone 1 background         # Visual backdrop
    course 1 to 2             # Transition area between courses
    course 2 to 3             # Transition area
    course 3 to 4 to sky      # Transition with vertical element
    Course 3 to 4             # Transition area
    Long Fall                 # Special long fall section
    Breaker layer             # Dark area with light puzzle
    DecorBeam                 # Decorative beam element
    CoolStatue                # Decorative statue
  zone 2                      # Secondary zone (unlockable)
    Area1                     # First section
    AtomColliderArea          # Atom crafting zone
    AtomColliderSystem        # Crafting logic
  Zone 1-2 transition         # Bridge between zones
    LongFallCollider (x2)     # Fall zone triggers
    zone 2 activator          # Unlock trigger
  Courses                     # Container for all 5 courses
    course 1 through course 5 # See "Course Structure" below
  Grid                        # Primary tilemap parent
    [17-21 tilemap layers]    # See tilemap-layers.md
  biggerGrid                  # Separate grid for large tiles
    moss                      # Moss overlay (Level 1)
  PlayerObject                # Player entity
    Player                    # Character (RigidBody, Colliders, Audio)
      Sprite                  # Animated player sprite
      Light                   # Player light (dark areas)
      *Particles (x5)         # Sweat, landing, wall particles
    Afterimages               # Pool of 21 afterimage sprites for dash
  Clone                       # Clone prefab template
  pauseMenu                   # Pause/settings overlay
  MusicPlayer                 # Background music (2 audio sources)
  EndOfDemoCanvas             # End-of-demo screen
  SceneTransition             # Scene change animation
  AudioPlayer                 # SFX manager
  Utils                       # Utility scripts
  Main Camera                 # Camera with CameraMovement script
  GlobalLight                 # Scene-wide 2D light
  Global Volume               # URP post-processing
  EventSystem                 # Unity UI input
```

## Course Structure

Each course follows an identical hierarchy pattern:

```
course N (courseScript, + serialization scripts)
  DisableBits/                # Stuff that enables/disables based on course state
    Start                     # Start gate trigger (startGate script)
    RealEnd                   # True course end (endGate script)
    End (1), End (2) [, End (3)]  # Additional end triggers
    EndSpriteDisabler         # Hides end sprite after completion
    Canvas                    # Per-course UI overlay
    BallKillers               # Kill zones (3-4 colliders)
    Checkpoint (1-6)          # Respawn checkpoints (checkpointScript)
    staticCameraZone (1-N)    # Fixed camera regions
    camSizeTrigger (1-N)      # Camera zoom change triggers
    background geometry/      # Course-specific background sprites
    area 1, area 2, ...       # Sub-sections (courses 3-5)
    Decorations               # Per-course decorative sprites
    Spring (N)                # Spring objects (course 5)
    brokenSpring (N)          # Visual broken springs (course 5)
  localUpgrades/              # Upgrade box container
    upgradeBox (N)            # Each box: SpriteRenderer + 2x BoxCollider2D
      Cost display            # TextMeshPro cost label
      Name display            # Upgrade name label
      Particle effects        # Purchase VFX
      Description             # Tooltip text
    DashUnlock                # Special movement unlock box (course 2)
    Double jump               # Movement unlock (course 3)
    SwapBlocksOnce/endDemo    # Block swap + demo end trigger (course 4)
    enablePrestige            # Prestige unlock (course 3)
    CashPerLoop, CloneCount, CloneMult, FastCloneChance, GLOBALcash, ...
  Clones/                     # Clone replay container
    activeCloneArea           # Trigger zone for clone visibility
    endParticles              # Clone death particle system
```

### Course Upgrade Box Counts

| Course | Upgrade Boxes | Notable Unlocks |
|--------|--------------|-----------------|
| Course 1 | 3 | Basic upgrades (clone count, cash) |
| Course 2 | 6 | **Dash** unlock |
| Course 3 | 11 | **Double Jump** unlock, **Prestige** unlock |
| Course 4 | 12 | **Block Swap** unlock, prestige box |
| Course 5 | 9 | Wall jump, advanced upgrades |

## Zone Layout (World Coordinates)

The world is laid out horizontally with courses progressing left-to-right
and upward.

```
Y
^
|  600 ┌─────────────────────────────────────┐
|      │          Course 5 / Zone 2          │
|  500 │                                     │
|      │    Course 4 (block swap area)       │
|  400 │         ┌──────────────────┐        │
|      │         │  Breaker (dark)  │        │
|  300 │         │  blueBlocks      │        │
|      │         │  orangeBlocks    │        │
|  200 │         └──────────────────┘        │
|      │                                     │
|  100 │         Course 3 area               │
|      │                                     │
|    0 ├─────────────────────────────────────┤
|      │  Course 1    Course 2               │
| -100 │  (start)     (dash unlock)          │
|      └─────────────────────────────────────┘
└──────────────────────────────────────────────> X
      -200     0      200     400     600     800
```

### Key Locations

| Feature | Approximate Position |
|---------|---------------------|
| Player start | (448, -144) |
| Course 1 upgrade boxes | (1357, -49) to (1744, -49) |
| Blue/Orange block area | X: 289-739, Y: 247-401 |
| Breaker/dark area | X: 397-673, Y: -19 to 271 |
| Tree growth area | X: -6 to 4, Y: -6 to -3 |
| Moss area | X: 451-618, Y: 41-132 |
| InvisibleWall (boundary) | X: 1, Y: -2 to 3 |

## Coordinate Systems

The game uses two coordinate systems that must be mapped between:

### World Coordinates
Used by GameObjects (upgrade boxes, checkpoints, springs, player, etc.).
Position values are in Unity world units (pixels at 1:1).

### Tilemap Grid Coordinates
Used by tilemap tile positions. Integer (x, y) values representing grid cells.

### Conversion

The primary Grid object has:
- **cellSize:** (32, 32) — each grid cell is 32x32 world units
- **transform position:** (0, -23) — Grid is offset 23 units down

To convert between them:
```
grid_x = world_x / 32
grid_y = (world_y + 23) / 32

world_x = grid_x * 32
world_y = grid_y * 32 - 23
```

The level editor displays everything in grid coordinates.

## Tilemap Grid Organization

Tilemaps are children of three Grid objects:

### Grid (primary, cellSize=32x32)
Contains all gameplay-critical tilemap layers: ground, spikes, blocks,
hazards, decorations, lighting. Transform at (0, -23).

### biggerGrid (cellSize=64x64)
Contains the moss layer (Level 1 only), with double-size cells for
larger background tiles.

### The Tree (cellSize=64x64)
Contains the Tree1Ground, Tree2Ground, Tree3Ground layers (Level 1 only).

## Scene Objects (Non-Tilemap)

376 GameObjects with SpriteRenderers exist outside the tilemap system.
These are extracted to `scene_objects_level{N}.json` with grid coordinates.

### Object Types

| Category | Examples | Count (L1) |
|----------|----------|-----------|
| Upgrade boxes | upgradeBox, DashUnlock, enablePrestige | ~41 |
| Checkpoints | Checkpoint (1-6) per course | ~25 |
| Course gates | Start, End (1-3), RealEnd | ~15 |
| Springs | Spring (1), Spring (3), brokenSpring | ~6 |
| Decorations | DecorBeam, Decorations, CoolStatue | ~50+ |
| Background | Background sprites per course | ~30+ |
| UI/Other | Canvas elements, kill zones, camera triggers | remainder |

## Zone Transitions

- **Zone 1 to Zone 2**: Via `Zone 1-2 transition` object containing LongFallColliders
  and a zone 2 activator trigger
- **Long Fall**: Special gravity section between zones (increased fall speed)
- **Course transitions**: Areas named `course N to N+1` contain visual connectors
  and triggers between adjacent courses
