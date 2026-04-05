# Save System

## Architecture

IGTAP uses a `SaveableObject<T>` pattern where each saveable class contains a
nested `SaveObject` struct. Serialization uses Unity's `JsonUtility` for JSON
read/write to files in `{Application.persistentDataPath}/Savedata/`.

### SaveableObject Base Class

```csharp
public class SaveableObject<T> : MonoBehaviour
{
    protected string fileName;          // save file name
    protected T saveObject;             // serialized data struct

    public void Save() {
        string json = JsonUtility.ToJson(saveObject);
        File.WriteAllText(path + fileName, json);
    }

    public void Load() {
        string json = File.ReadAllText(path + fileName);
        saveObject = JsonUtility.FromJson<T>(json);
    }
}
```

## Save Files

### Movement State
**File:** `Savedata/movementdata.txt` (or similar)

Saves all 122 fields from the `Movement` class:

| Category | Fields |
|----------|--------|
| Position | playerPosition (Vector2), respawnPoint, courseResetPoint, overallResetPoint |
| Currencies | Cash, GreenPower, AtomicPower (all double) |
| Abilities | hasDash, hasWallJump, hasDoubleJump, hasBlockSwap (all bool) |
| Upgrades | numDashes, maxAirJumps, dashSpeed, wallJumpForce, etc. |
| Camera | camPosition, camSize, lockedCam state |
| Misc | firstTime flag, checkpoint state |

### Per-Course State
**Files:** `Savedata/course{N}data.txt` (one per course)

Each file contains a `courseScript.SaveObject`:

```
_bestPlayerPath: int[]       # 1700 frames * 2 = 3400 values (x,y pairs)
_bestPlayerSprites: int[]    # 1700 sprite indices
_bestFacingRights: float[]   # 1700 frames * 2 = 3400 (scaleX, scaleY)
_bestPathLength: float       # Run duration (seconds)
_bestPathTime: float         # Best time (seconds)
_cloneCount: int             # Active clones
_cloneEndVelocity: Vector2   # Final frame velocity
_reward: double              # Cash per completion
_rewardTier: double          # 10^tier multiplier for rewards
_costTier: double            # 10^tier multiplier for costs
_boxCosts: double[3]         # Current upgrade box costs
_boxBaseCosts: double[3]     # Base costs
_boxTimesUsed: int[3]        # Purchase counts
_boxActive: bool[3]          # Box unlocked states
_boxVisible: bool[3]         # Box visibility states
_boxPositions: Vector2[3]    # World positions
_boxCaps: int[3]             # Max purchase limits
_boxBaseCaps: int[3]         # Base caps
_boxBuyMaxes: bool[3]        # Auto-buy flags
_localUpgradeDict: double[8] # Per-course upgrade values
_trippedBreaker: bool        # Breaker puzzle state
```

### Atom Crafting State
**File:** `Savedata/atomdata.txt` (or similar)

`AtomColliderSystem.SaveObject` with 24 fields:
- Per-tier atom counts (gross and effective)
- Crafting progress per tier
- AtomicPower production rate

### Timer State
**File:** `Savedata/Timer.txt`

`timer.SaveObject` with 2 fields:
- Elapsed time (float)
- Display state

### Breaker State
**File:** `Savedata/breakerdata.txt`

`tripBreakerScript` saves:
- `fixedAreas[]`: boolean array of which dark sub-areas are permanently lit

### Menu Course Data
**File:** `StreamingAssets/MenuCourseData.txt`

This is the default/initial course state loaded from StreamingAssets.
See `file-formats.md` for the full JSON schema.

## Save/Load Flow

### Saving
1. Game triggers save event (checkpoint, course completion, upgrade purchase, etc.)
2. Each `SaveableObject` copies its runtime fields into its `SaveObject` struct
3. `JsonUtility.ToJson()` serializes the struct to a JSON string
4. JSON written to the appropriate file in `Savedata/`

### Loading
1. On game start, each `SaveableObject` checks for its save file
2. If file exists: `JsonUtility.FromJson<T>()` deserializes JSON into `SaveObject`
3. Fields from `SaveObject` are applied to the runtime object
4. If file doesn't exist: default values are used

### Prestige Reset
When a course is prestiged:
1. Course-local upgrades reset to 0
2. Clone count resets
3. Box purchase counts reset
4. Cost/reward tiers increment (10^tier multiplier)
5. New save file written with reset values
6. Global state is NOT affected

## MenuCourseData.txt Analysis

The included save data shows a course in early state:

- **1,700 recorded frames** (34-second run)
- **4 clones** spawning
- **Player path:** X range 183-1459, Y range -254 to 646
- **3 upgrade boxes** at Y=-49 (horizontal line near course start)
- **Box costs:** 1.0, 8.0, 700.0 (exponential progression)
- **Movement upgrade level:** 4.0
- **Breaker not tripped**
- **No prestige** (tier 0)
