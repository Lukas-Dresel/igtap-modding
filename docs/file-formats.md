# File Formats

## Game Directory Structure

```
IGTAP an Incremental Game That's Also a Platformer Demo/
  IGTAP.x86_64                    # Main executable
  UnityPlayer.so                  # Unity runtime (39 MB)
  libdecor-0.so.0                 # Wayland decoration lib
  libdecor-cairo.so               # Wayland Cairo lib
  IGTAP_BurstDebugInformation_DoNotShip/
    Data/Plugins/lib_burst_generated.txt
  IGTAP_Data/                     # All game data
    app.info                      # "Pepper tango games\nIGTAP"
    boot.config                   # Graphics/runtime config
    RuntimeInitializeOnLoads.json # Init callbacks
    ScriptingAssemblies.json      # Assembly manifest (161 assemblies)
    level0                        # Scene 0 (binary, 6.7 MB)
    level1                        # Scene 1 (binary, 8.0 MB)
    globalgamemanagers            # Global settings (binary)
    globalgamemanagers.assets     # Global assets (binary)
    resources.assets              # Built-in resources
    sharedassets0.assets          # Shared assets (sprites, textures)
    sharedassets0.assets.resS     # Serialized resource data (245 MB)
    sharedassets1.assets          # More shared assets
    Managed/                      # .NET assemblies
      Assembly-CSharp.dll         # Main game code (137 KB)
      Assembly-CSharp-firstpass.dll
      DOTween.dll                 # Tween animation library
      Newtonsoft.Json.dll         # JSON serialization
      [60+ UnityEngine DLLs]
    MonoBleedingEdge/             # Mono runtime
    Plugins/
      lib_burst_generated.so      # Burst-compiled code
    Resources/
      unity, unity_builtin_extra, UnityPlayer.png
    StreamingAssets/
      MenuCourseData.txt          # Course config (JSON, 43 KB)
      UnityServicesProjectConfiguration.json
      aa/                         # Addressable Assets
        catalog.bin, catalog.hash, settings.json
        StandaloneLinux64/        # Localization bundles
```

## Unity Serialized Files (Binary)

Files: `level0`, `level1`, `globalgamemanagers`, `*.assets`

These use Unity's proprietary binary serialization format. Key characteristics:
- Magic bytes at offset 0x30 contain the Unity version string
- Contains serialized GameObjects, Components, and their data
- References between objects use `(m_FileID, m_PathID)` pairs
- `m_FileID=0` means same file; `m_FileID>0` references externals list

### External File References (level0)

| FileID | Target |
|--------|--------|
| 1 | globalgamemanagers.assets |
| 2 | unity default resources |
| 3 | sharedassets0.assets |

### Object Types in the Game (25,681 total)

| Type | Count | Description |
|------|-------|-------------|
| MonoBehaviour | 8,310 | Game script instances |
| Sprite | 7,875 | Sprite assets (32x32 tiles, decorations, UI) |
| MonoScript | 2,572 | Script type definitions |
| GameObject | 2,096 | Scene objects |
| RectTransform | 1,175 | UI transforms |
| Transform | 921 | World transforms |
| CanvasRenderer | 871 | UI renderers |
| SpriteRenderer | 501 | Sprite display components |
| BoxCollider2D | 335 | Collision boxes |
| Texture2D | 184 | Texture atlases |
| ParticleSystem | 118 | Particle effects |
| Animator | 106 | Animation controllers |
| Shader | 103 | Rendering shaders |
| Tilemap | 38 | Tilemap layers (17 in level0, 21 in level1) |
| Rigidbody2D | 35 | Physics bodies |
| CompositeCollider2D | 27 | Merged collision shapes |
| TilemapCollider2D | 26 | Tilemap collision |
| AnimationClip | 18 | Animation data |
| AudioClip | 18 | Sound effects/music |

## Save Data Format (JSON)

Save files are stored in `{Application.persistentDataPath}/Savedata/`.

### MenuCourseData.txt (StreamingAssets)

Single JSON object with course state:

```json
{
  "_bestPlayerPath": [x1,y1,x2,y2,...],  // int[] flattened 2D coords (3400 = 1700 frames)
  "_bestPlayerSprites": [idx,...],         // int[] sprite lookup indices per frame
  "_bestFacingRights": [sx1,sy1,...],      // float[] scale values per frame (flattened)
  "_bestPathLength": 34.0,                 // float: run duration in seconds
  "_bestPathTime": 34.0,                   // float: best completion time
  "_cloneCount": 4,                        // int: active clone count
  "_cloneEndVelocity": {"x": 404.99, "y": -594.99},
  "_reward": 1.0,                          // double: cash per completion
  "_rewardTier": 0.0,                      // double: reward scaling exponent
  "_costTier": 0.0,                        // double: cost scaling exponent
  "_boxCosts": [1.0, 8.0, 700.0],         // double[3]: current upgrade box costs
  "_boxBaseCosts": [1.0, 8.0, 700.0],     // double[3]: base costs before scaling
  "_boxTimesUsed": [0, 0, 0],             // int[3]: purchase counts
  "_boxActive": [true, true, true],        // bool[3]: boxes unlocked
  "_boxVisible": [true, true, true],       // bool[3]: boxes visible
  "_boxPositions": [{"x":1744,"y":-49}, {"x":1547,"y":-49}, {"x":1357,"y":-49}],
  "_boxCaps": [100, 100, 100],             // int[3]: max purchase count
  "_boxBaseCaps": [100, 100, 100],         // int[3]: base caps
  "_boxBuyMaxes": [false, false, false],   // bool[3]: auto-buy-to-cap
  "_localUpgradeDict": [0,4,0,0,0,0,0,0], // double[8]: per-course upgrades
  "_trippedBreaker": false                 // bool: breaker puzzle state
}
```

### Per-Course Save Files

Each course saves to `Savedata/course{N}data.txt` using the same JSON structure
as above (serialized from `courseScript.SaveObject`).

### Global Save Files

- Movement state (position, unlocks, currencies): serialized from `Movement.SaveObject`
- Atom crafting progress: serialized from `AtomColliderSystem.SaveObject`
- Timer: serialized from `timer.SaveObject`
- Breaker state: `Savedata/breakerdata.txt` from `tripBreakerScript`

All use `JsonUtility.ToJson()` / `JsonUtility.FromJson<T>()`.

## Extracted Data Formats

### layer_index.json

Array of layer metadata objects:
```json
{
  "level": 0,
  "name": "ground",
  "path_id": 2926,
  "file": "layers/level0_ground.json",
  "tile_count": 26651,
  "origin": {"x": -178, "y": -57, "z": 0},
  "size": {"x": 994, "y": 629, "z": 1},
  "bbox": {"min_x": -178, "max_x": 815, "min_y": -51, "max_y": 571}
}
```

### Layer Files (layers/level{N}_{name}.json)

```json
{
  "name": "ground",
  "path_id": 2926,
  "tile_count": 26651,
  "origin": {"x": -178, "y": -57, "z": 0},
  "size": {"x": 994, "y": 629, "z": 1},
  "matrices": [
    [1.0, 0.0, 0.0, 1.0],   // [e00, e01, e10, e11] identity
    [0.0, 1.0, -1.0, 0.0],  // 90 CW
    ...
  ],
  "tiles": [
    {"x": -178, "y": -51, "z": 0, "ti": 3, "si": 4, "mi": 0, "ci": 0, "fl": 1},
    ...
  ]
}
```

Tile field abbreviations:
- `ti`: tile_index (index into m_TileAssetArray)
- `si`: sprite_index (index into m_TileSpriteArray)
- `mi`: matrix_index (index into matrices array)
- `ci`: color_index (index into m_TileColorArray)
- `fl`: flags (AllTileFlags bitmask)

### tilemap_sprite_index.json

Maps tilemap path_id to sprite image paths:
```json
{
  "2926": {
    "name": "ground",
    "level": 0,
    "path_id": 2926,
    "sprites": {
      "2": "tile_sprites/level0/ground/2.png",
      "3": "tile_sprites/level0/ground/3.png",
      ...
    }
  }
}
```

### scene_objects_level{N}.json

Array of non-tilemap GameObjects with SpriteRenderers:
```json
{
  "name": "upgradeBox (3)",
  "go_path_id": 2750,
  "x": 598.7,
  "y": 337.9,
  "world_x": 19158.4,
  "world_y": 10790.0,
  "scale_x": 1.5,
  "scale_y": 1.5,
  "enabled": 1,
  "sprite": "object_sprites/UpgradeBox_0.png",
  "sprite_name": "UpgradeBox_0",
  "sprite_w": 64,
  "sprite_h": 64
}
```

Fields:
- `x`, `y`: position in tilemap grid coordinates (matches tile positions)
- `world_x`, `world_y`: raw Unity world coordinates
- `scale_x`, `scale_y`: cumulative world scale (product of all ancestor scales).
  Negative values indicate horizontal/vertical flip.
- `sprite_w`, `sprite_h`: sprite image dimensions in pixels
- `sprite`: relative path to extracted sprite image (if available)
- `sprite_name`: original Unity sprite asset name

Grid size of an object: `grid_w = sprite_w * |scale_x| / 32`

**Note:** Level 1 has correct object positions (376 objects). Level 0 has broken
Y positions for course-embedded objects (123 objects) due to incomplete hierarchy.

### Sprite Images

**Tile sprites:** `output/tile_sprites/level{N}/{layer_name}/{sprite_index}.png`
Most are 32x32 pixels, RGBA format.

**Object sprites:** `output/object_sprites/{name}.png`
32 unique sprites for scene objects. Sizes vary (32x32 to 1920x1080).
