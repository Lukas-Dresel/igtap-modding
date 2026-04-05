# Code Structure

Decompiled from `Assembly-CSharp.dll` (8,563 lines, 50 classes).
`Assembly-CSharp-firstpass.dll` contains only auto-generated metadata.

## Class Reference

### Core Game Loop

#### Movement (lines ~4724-6271)
The main player controller. Singleton accessed via static fields.

**Key fields (122 total):**
- `runSpeed = 2000f`, `jumpForce = 6000f`, `gravity = 9.8f`, `maxDownSpeed = 2000f`
- `dashSpeed = 8000f`, `dashTime = 0.1f`, `dashCooldown = 0.1f`, `numDashes = 1`
- `wallJumpForce = 300f`, `wallJumpMoveLock = 0.1f`
- `maxAirJumps = 0` (unlockable)
- `respawnPoint`, `courseResetPoint`, `overallResetPoint`: Vector2 checkpoints
- `Cash`, `GreenPower`, `AtomicPower`: double currencies
- All `has*` booleans: `hasDash`, `hasWallJump`, `hasDoubleJump`, `hasBlockSwap`

**Key methods:**
- `Update()`: Main physics loop - gravity, movement, dash, wall jump, input
- `Kill(Quaternion)`: Death handler - spawns particles, resets to checkpoint
- `Respawn()`: Teleport to respawn point
- `SaveState()` / `LoadState()`: Serializes 122 fields to JSON file
- `ChangeGlobalState()`: Called by upgrade boxes to modify persistent state

**Enums:**
- `cutsceneMode`: none, dash, deathFreeze, longfall, spring
- `movementUpgrades`: dash, wallJump, doubleJump, swapBlocksOnce, unlockBlockSwap

**SaveObject struct:** Nested struct with every field for JSON serialization.

#### courseScript (lines ~3422-3896)
Per-course state manager. Each of the 5 courses has one instance.

**Key fields (50 total):**
- `courseName`: string identifier
- `bestPlayerPath`, `bestPlayerSprites`, `bestFacingRights`: arrays for clone replay
- `bestPathLength`, `bestPathTime`: timing data
- `cloneCount`: number of clones in this course
- `reward`, `rewardTier`, `costTier`: economy values
- `boxCosts[3]`, `boxPositions[3]`, `boxActive[3]`: upgrade box state
- `localUpgradeDict`: Dictionary<localUpgradeSet, double>
- `trippedBreaker`: bool for light puzzle

**Key methods:**
- `StartTracking()`: Begin recording player path
- `StopTracking()`: End recording, save if best time
- `RecordFrame()`: Called each frame during tracking to store position/sprite
- `Save()` / `Load()`: Persist to `Savedata/course{N}data.txt`
- `Prestige()`: Reset course for multiplier bonus

#### clonesScript (lines ~3069-3381)
Clone spawning and replay system.

**Key fields:**
- `clonePath`: Vector2[] positions per frame
- `cloneSprites`: Sprite[] visual per frame  
- `cloneScales`: Vector2[] scale per frame
- `pathLength`: float duration in seconds
- `cloneEndVelocity`: Vector2 final momentum
- `cloneFastness`, `cloneBigness`: float modifiers

**Key methods:**
- `SpawnClones()`: Instantiate clone objects from recorded data
- `UpdateClone()`: Advance clone position along recorded path
- `AwardCash()`: Give currency for off-screen clones

#### globalStats (lines ~4136-4327)
Singleton tracking global game state.

**Currencies (enum):**
- `Cash`: primary currency
- `GreenPower`: secondary
- `AtomicPower`: tertiary (from atom crafting)
- `regularNumber`: internal

**Global upgrades (enum globalUpgradeSet):**
- `cashPerLoop`, `fastCloneChance`, `maxCloneFastness`
- `bigCloneChance`, `maxCloneBigness`, `cloneMult`
- `spawnNewAtom`, `atomLevelChance`, `greenCloneChance`
- `TreeGrowth`, `unlockPrestige`, `openGate`, `increasedWatts`

#### upgradeBox (lines ~7461-8072)
Interactive upgrade purchase points placed in courses.

**Key fields:**
- `upgradeCost`: double, scales with `upgradeScaleFactor`
- `upgrade`: localUpgradeSet enum
- `globalUpgrade`: globalUpgradeSet enum
- `movementUpgrade`: Movement.movementUpgrades enum
- `isActive`, `visible`, `buyMax`, `Cap`, `TimesUsed`

**Key methods:**
- `Buy()`: Purchase upgrade, deduct currency, apply effect
- `UpdateCost()`: Recalculate cost based on times used and tier

### Per-Course Upgrade System

#### localUpgrades (lines ~4565-4670)
Manages the 9 per-course upgrade types.

**Enum localUpgradeSet:**
- `GLOBAL`, `Movement`, `cloneCount`, `DUMMY_cloneCountPlural`
- `cashPerLoop`, `fastCloneChance`, `bigCloneChance`, `cloneMult`, `prestige`

### Environment / Hazards

#### spikeScript (lines ~7027-7037)
Kills player on trigger contact, preserving spike rotation for death particles.

#### SpringScript (lines ~7038-7081)
Bouncy platforms. Fields: `upForce`, `strength`, `movementLockTime = 0.3f`, cooldown `0.3s`.
Applies directional force and locks player movement briefly.

#### checkpointScript (lines ~3011-3020)
Sets `Movement.respawnPoint` on trigger enter.

#### startGate (lines ~7082-7095)
Course start trigger. Begins path tracking via `courseScript.StartTracking()`.

#### endGate (lines ~4014-4024)
Course end trigger. Calls `courseScript.StopTracking()` if `isEndOfCourse` flag set.

#### tripBreakerScript (lines ~7275-7428)
Light puzzle mechanic in the dark area.
- Manages `fixedAreas[]` boolean array
- When tripped: resets cash to 0, activates player light
- Area lights can be permanently fixed by passing through with best time
- Saves to `Savedata/breakerdata.txt`

### Incremental Systems

#### AtomColliderSystem (lines ~2222-2471)
Three-tier atom crafting system.
- Atoms bob in rings, craft when 3+ collected
- Tier progression: N+2 atoms = 1 upgraded atom
- Contributes to AtomicPower currency (~1 per 0.4s)
- Each tier has color coding and crafting progress

#### Tree growth system
Progressive trees (`Tree1Ground`, `Tree2Ground`, `Tree3Ground` tilemap layers) 
that grow as the `TreeGrowth` global upgrade increases.

### Camera System

#### CameraMovement (lines ~2481-2780)
Follows player with smoothing and zone overrides.
- `staticCameraZones`: areas where camera locks to fixed position
- `camSizeTriggers`: areas that change camera zoom
- `cameraShake`: screen shake on events
- Smooth follow with configurable speed

### Visual Effects

#### afterimage (lines ~2176-2221)
Dash afterimage effect. Pool of 21 sprite renderers that fade out.

#### clonesLoD (lines ~3035-3068)
Clone level-of-detail system. Enables/disables clone rendering based on trigger zones.

#### colouredBlockSwapper (lines ~3382-3421)
Manages the blue/orange block swap mechanic. When player triggers swap,
all blue blocks become passable and orange blocks become solid (or vice versa).

### UI Classes

- `CashDisplay`: Formats and displays currency with suffix notation
- `pauseMenuSettings`: Pause menu with volume sliders, fullscreen, keybinds
- `keybindSettingItem`: Individual key rebinding UI element
- `timerDisplay`: Speedrun timer display

### Audio

- `MusicPlayer`: Background music management
- `SFXManager`: Sound effect playback

### Scene Management

- `SceneTransition`: Animated scene change (gate animation)
- `parallaxEffect`: Background parallax scrolling
- `objectBobbing`: Gentle up/down bobbing animation for collectibles

### Utility

- `SaveableObject<T>`: Generic base class for JSON save/load pattern
- `InputWorkaround`: Fixes for Unity InputSystem edge cases
