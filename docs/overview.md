# IGTAP - Game Overview

**Full title:** IGTAP: An Incremental Game That's Also a Platformer (Demo)

**Engine:** Unity 6 (version 6000.3.9f1 LTS)  
**Platform:** Linux x86_64 (also available on other platforms via Steam)  
**Developer:** Pepper Tango Games  
**Runtime:** Mono/.NET (MonoBleedingEdge)

## What is IGTAP?

IGTAP is a 2D platformer with idle/incremental game mechanics. The player runs through
courses (levels), and their runs are recorded. On subsequent loops, "clones" replay
previous runs, earning currency even when the player isn't actively controlling them.
Currency is spent on upgrades that improve movement abilities (dash, wall jump, double
jump) and incremental mechanics (clone count, clone speed, cash multipliers).

## Core Gameplay Loop

1. **Run a course** from start gate to end gate
2. **Path is recorded** (position, sprite, scale every frame)
3. **Clones replay** your best run each loop, earning cash
4. **Spend cash** on upgrade boxes placed throughout the course
5. **Unlock abilities** that let you reach new areas and faster times
6. **Prestige** to reset progress for permanent multipliers

## Key Stats

- **2 Unity scenes** (level0 and level1 - same world, different states)
- **5 courses** across 2 zones
- **38 tilemap layers** per scene
- **~160,000 tiles** per level
- **7,875 sprites** in the asset bundle
- **8,128 tile sprites** extracted for the editor
- **50 C# classes** in the game code (8,563 lines decompiled)
- **6 languages** supported (EN, ES, PT, ZH, ZH-Hant, Pirate)

## Technology Stack

| Component | Technology |
|-----------|------------|
| Engine | Unity 6 (URP - Universal Render Pipeline) |
| Scripting | C# / Mono |
| Physics | Unity Physics2D |
| Tilemaps | Unity Tilemap system |
| Animation | DOTween + Unity Animator |
| Audio | Unity AudioMixer |
| Localization | Unity Localization package |
| Input | Unity InputSystem |
| Asset Management | Unity Addressables |
| Rendering | Universal Render Pipeline (2D) |
