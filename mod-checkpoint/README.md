# IGTAP Checkpoints Mod

Custom checkpoint slots: save position (F4), teleport back (F3), cycle slots (F2), override death respawn, JSON persistence. Manageable from the F8 debug menu.

## Modding Notes for IGTAP

Guidelines learned while building this mod. Useful for anyone writing new mods for this game.

### Debugging Invisible Effects

When something doesn't render, don't guess at the cause. Add debug logging and check the BepInEx log (`BepInEx/LogOutput.log`). Log in layers:

1. **Object state**: Is the GameObject active? Position correct? Component enabled?
2. **Material/shader**: Log `renderer.material.name` and `renderer.material.shader.name`. A null or wrong shader is the most common cause.
3. **Compare to a working reference**: Find something in the game that does work (e.g. one of Movement's particle systems) and log its renderer setup. Diff against yours.
4. **Scale**: Log `sprite.bounds.size` on the player to get the actual world-space scale. Compare your effect sizes against it.

Each round: add logging, rebuild, check log, fix the actual problem. Don't change things without knowing what's wrong.

### World Scale

This game uses very large world coordinates. Player positions are in the thousands and the player sprite is ~272 world units wide. Any visual effect (particles, UI elements, offsets) needs to be sized relative to this. A "normal" Unity particle size of 0.1-1.0 is invisible here.

Always check `sprite.bounds.size` on the player's SpriteRenderer to calibrate.

### Shaders and URP 2D

The game runs Unity 6 with URP 2D. Most shaders you'd expect to find don't exist at runtime:

- All `Universal Render Pipeline/Particles/*` shaders: **not included**
- `Particles/Standard Unlit`: **not included**
- `Sprites/Default`: exists but doesn't render correctly in the 2D lit pipeline

**What works**: Copy the material from an existing game object's renderer. The game uses `Sprite-Lit-Default` for both sprites and particles. Grab it from e.g. `Movement.metalLandParticles`'s `ParticleSystemRenderer` rather than trying to construct materials from shader names.

### Private Field Access

Many useful fields on `Movement` are private. Use Harmony's `AccessTools.Field()` and cache the `FieldInfo` as a static readonly, same pattern as `GameState.cs` in the core mod. Key private fields: `sr` (SpriteRenderer), `isDead`, `normalCollider`, `defaultColliderSize`, `defaultColliderOffset`, `blockSwapper`, `metalLandParticles`.

### Patching Respawn

To override where the player respawns: patch `Movement.respawn()`, not `onDeath()`. The death method triggers the animation and a 0.6s delay before calling `respawn()` — you want that to still play. Your prefix must replicate all the state cleanup the original `respawn()` does (isDead, cutsceneMode, collider reset, rotation reset) or the player gets stuck.

### Persistence

Use `BepInEx.Paths.ConfigPath` (`BepInEx/config/`) for mod data files, not `Application.persistentDataPath` (that's the game's save location). `JsonUtility` works for serialization but requires `[Serializable]` on all classes and can't serialize a bare `List<T>` as root — wrap it in a class.
