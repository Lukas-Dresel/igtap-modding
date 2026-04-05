# Game Mechanics

## Player Movement

### Base Physics
- **Run speed:** 2000 units/s
- **Jump force:** 6000 units
- **Gravity:** 9.8 (applied per frame as `velocity.y -= gravity`)
- **Max downward speed:** 2000 units/s (clamped)
- **Grounded check:** BoxCollider2D overlap test against ground layers

### Unlockable Abilities

#### Dash
- Unlocked in Course 2 via DashUnlock upgrade box
- **Speed:** 8000 units/s
- **Duration:** 0.1s
- **Cooldown:** 0.1s
- **Air dashes:** configurable via `numDashes` (default 1)
- Creates afterimage trail (pool of 21 sprites that fade)
- Enters `cutsceneMode.dash` during dash (overrides normal movement)

#### Wall Jump
- **Force:** 300 units
- **Movement lock:** 0.1s (prevents immediate re-input)
- Requires contact with wall collider
- Resets air jump count

#### Double/Air Jump
- Unlocked in Course 3
- `maxAirJumps` configurable (default 0, each upgrade adds 1)
- Uses same jump force as ground jump

#### Block Swap
- Unlocked in Course 4 (two stages: `swapBlocksOnce` then `unlockBlockSwap`)
- Toggles blue/orange block layers between solid and passable
- Managed by `colouredBlockSwapper` class

### Death & Respawn
- Triggered by spike collision or OOB areas
- `Kill(Quaternion)`: plays death particles at spike rotation angle
- Respawns at nearest checkpoint (`respawnPoint`)
- Course reset returns to `courseResetPoint` (start of current course)
- Full reset returns to `overallResetPoint`

## Clone System

### Recording
During a course run, `courseScript.RecordFrame()` captures every frame:
- **Position:** player (x, y) as integers (multiplied from world coords)
- **Sprite:** current animation sprite index
- **Scale:** player facing direction as (scaleX, scaleY) * 1000

### Playback
After completing a course, clones replay the recorded path:
- Clone objects use `SpriteRenderer` to display the recorded sprites
- Position interpolated along the recorded path
- `cloneFastness` modifier: speeds up clone replay (from `fastCloneChance` upgrade)
- `cloneBigness` modifier: scales clone size (from `bigCloneChance` upgrade)
- Off-screen clones still generate revenue without rendering

### Clone Economy
- Each clone completion awards `reward * cloneMult * (tier bonuses)`
- More clones = more passive income
- `cloneCount` upgraded per-course via local upgrades
- Global `cloneMult` multiplier affects all courses

### Level of Detail (LoD)
- `clonesLoD` trigger zones control clone rendering
- Clones outside active LoD zone are hidden but still earn currency
- Prevents performance issues from rendering hundreds of clones

## Upgrade System

### Local Upgrades (Per-Course)

Each course has its own upgrade tree with 9 types:

| Upgrade | Effect |
|---------|--------|
| `GLOBAL` | Applies to global upgrade pool |
| `Movement` | Unlocks movement abilities |
| `cloneCount` | Adds clones to this course |
| `cashPerLoop` | Increases cash earned per course completion |
| `fastCloneChance` | Chance for clone to be sped up |
| `bigCloneChance` | Chance for clone to be enlarged |
| `cloneMult` | Multiplier on clone cash earnings |
| `prestige` | Triggers prestige reset for this course |

### Global Upgrades (Cross-Course)

13 types affecting all courses:

| Upgrade | Effect |
|---------|--------|
| `cashPerLoop` | Global cash multiplier |
| `fastCloneChance` | Global fast clone probability |
| `maxCloneFastness` | Maximum speed bonus for fast clones |
| `bigCloneChance` | Global big clone probability |
| `maxCloneBigness` | Maximum size bonus for big clones |
| `cloneMult` | Global clone earnings multiplier |
| `spawnNewAtom` | Spawns atoms in the atom crafting area |
| `atomLevelChance` | Chance for higher-tier atom spawns |
| `greenCloneChance` | Chance for clones to produce GreenPower |
| `TreeGrowth` | Progresses tree growth stages |
| `unlockPrestige` | Makes prestige available |
| `openGate` | Opens the gate to Zone 2 |
| `increasedWatts` | Increases light radius in dark areas |

### Upgrade Box Mechanics
- Cost scales exponentially: `cost = baseCost * scaleFactor^timesUsed`
- Tier system: costs and rewards scale by `10^tier`
- Each box has a purchase cap (`Cap`)
- `buyMax` option: auto-purchase to cap
- 0.2s cooldown between purchases

## Currency System

### Three Currencies

| Currency | Source | Primary Use |
|----------|--------|-------------|
| **Cash** | Course completion, clone earnings | Upgrade boxes |
| **GreenPower** | Green clones (chance-based) | Zone 2 upgrades |
| **AtomicPower** | Atom crafting system (~1 per 0.4s) | Advanced upgrades |

### Prestige
- Unlocked via `unlockPrestige` global upgrade
- Resets course-local progress (clone count, local upgrades, box purchases)
- Provides permanent multiplier bonus
- Triggered by purchasing the prestige upgrade box in a course

## Atom Crafting System

Located in Zone 2's AtomColliderArea:
- **Three tiers** of atoms with color coding
- Atoms bob in circular rings (visual effect)
- **Crafting:** collect 3+ atoms of same tier -> 1 atom of next tier
- **Formula:** `N + 2` atoms required per craft
- Generates AtomicPower passively (~1 per 0.4s when active)
- Tracks gross and effective atom counts per tier
- Progress bar shows crafting progress

## Breaker / Dark Area

The breaker area (X: 397-673, Y: -19 to 271) is a special dark zone:

1. **Initial state:** well-lit
2. **Breaker tripped:** area goes dark, player gets a limited light radius
3. **Tripping penalty:** all Cash currency reset to 0
4. **Fixing areas:** sub-zones that can be permanently lit by passing through
5. **`increasedWatts` upgrade:** increases player light radius
6. **`lightBlocker` tilemap:** casts shadows in the dark area
7. **State saved** to `Savedata/breakerdata.txt`

## Color Block Mechanic

Two sets of swappable blocks in the course 4 area:

- **blueBlocks** (107 tiles): solid when blue is active
- **orangeBlocks** (62 tiles): solid when orange is active
- **blueSpikes** (81 tiles): hazardous when blue is active
- **orangeSpikes** (39 tiles): hazardous when orange is active

Player triggers swap via the block swap ability (unlocked in Course 4).
Only one color is active at a time.

## Springs

Course 5 features spring platforms:
- **upForce:** vertical bounce strength
- **strength:** horizontal push
- **movementLockTime:** 0.3s (player loses control briefly)
- **Cooldown:** 0.3s between bounces
- Animated via Unity Animator
- Some springs are "broken" (decorative only, no function)
