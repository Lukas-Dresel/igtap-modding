# IGTAP Replay Mod

Record player inputs to a human-editable text file and replay them deterministically.

## Dependencies

- **IGTAP Core** (`mod/`) — DebugMenuAPI, GameState
- **IGTAP Fixed Timestep** (`mod-fixedtimestep/`) — locks `Time.captureFramerate = 50` for deterministic physics

## Controls

- **F5** — Start recording (press again while recording to restart)
- **F6** — Start playback from replay file
- **F7** — Stop recording or exit replay mode
- **Exit button** — on the transport bar during replay

### During Replay

- **Play/Pause button** — on the transport bar
- **|< >|** — Step backward/forward one frame
- **|<< >>|** — Step backward/forward one second
- **Speed buttons** — 0.1x, 0.25x, 0.5x, 1x, 2x, 4x
- **Full timeline toggle** — switch between +/-5s window and full recording view
- **Timeline scrubbing** — click/drag on the timeline bar to seek
- **Hover** — hover over a timeline segment to see keys and frame range

### Ghost Markers

Ghost sprites appear at input change points showing what changed:
- **Blue tint + `▲` prefix** at head level — keys pressed
- **Orange tint + `▼` prefix** at foot level — keys released
- Colors are colorblind-safe (blue/orange pair)
- Visible during both recording and replay (including single-step mode)

## Replay File Format

Text file at `BepInEx/config/replay.txt`. Human-editable.

```
# IGTAP Replay v1
# Recorded: 2026-04-05 14:30:22
# Timestep: 50
# Bindings: Move=<Keyboard>/w,<Keyboard>/a,<Keyboard>/s,<Keyboard>/d Jump=<Keyboard>/space Dash=<Mouse>/leftButton

# !CP 0 0 {"Position":{"x":100,"y":200},...}

41       D LMB x=1
42       D LMB Space x=1
74       D x=1
86       . x=0

# @50 pos=4380.2,1886.0 vel=12000.0,0.0
# !CP 50 2 {"Position":...}
```

### Key Names

Keys are displayed and serialized using short human-readable names. Both short names and full InputSystem paths are accepted when editing:

| Short Name | InputSystem Path | | Short Name | InputSystem Path |
|------------|------------------|-|------------|------------------|
| A-Z | `<Keyboard>/a`-`z` | | LMB | `<Mouse>/leftButton` |
| 0-9 | `<Keyboard>/0`-`9` | | RMB | `<Mouse>/rightButton` |
| Space | `<Keyboard>/space` | | MMB | `<Mouse>/middleButton` |
| LShift | `<Keyboard>/leftShift` | | Mouse4 | `<Mouse>/forwardButton` |
| RShift | `<Keyboard>/rightShift` | | Mouse5 | `<Mouse>/backButton` |
| LCtrl | `<Keyboard>/leftCtrl` | | GP-A | `<Gamepad>/buttonSouth` |
| Esc | `<Keyboard>/escape` | | GP-B | `<Gamepad>/buttonEast` |
| Tab | `<Keyboard>/tab` | | GP-X | `<Gamepad>/buttonWest` |
| Enter | `<Keyboard>/enter` | | GP-Y | `<Gamepad>/buttonNorth` |
| Left/Right/Up/Down | `<Keyboard>/*Arrow` | | GP-LB/RB | `<Gamepad>/*Shoulder` |
| F1-F12 | `<Keyboard>/f1`-`f12` | | GP-Start | `<Gamepad>/start` |

### Line Format

- `frame  Key1 Key2 ... x=N` — input span (keys held from this frame until the next span)
- `x=N` — resolved horizontal move axis (-1, 0, or 1), handles opposing-key ambiguity
- `.` — no keys held
- `# !CP frame spanIndex {json}` — checkpoint (full state snapshot for seeking/validation)
- `# @frame pos=x,y vel=x,y` — verification point
- `# Bindings:` — recorded keybindings with full InputSystem paths

## Config Options

In `BepInEx/config/com.igtapmod.replay.cfg`:

| Section | Key | Default | Description |
|---------|-----|---------|-------------|
| General | ReplayFile | replay.txt | Path to replay file (relative to BepInEx/config/) |
| General | RecordMousePosition | false | Record mouse screen position (bloats file) |
| General | RestartOnRespawn | true | Auto-restart recording on death |
| Debug | PerFrameCheckpoints | false | Checkpoint every frame (large files, for debugging) |
| Debug | LifecycleLogging | false | Log all lifecycle events |
| Timeline | ShowFullTimeline | false | Show entire recording instead of +/-5s window |
| Ghosts | EnableDuringReplay | true | Show ghost markers at input changes |
| Ghosts | ShowAll | false | Show all ghosts at once |
| Ghosts | PreviousCount | 5 | Past ghosts to show |
| Ghosts | NextCount | 3 | Upcoming ghosts to show |

## Known Limitations

- **Do not tab out during replay.** Losing window focus causes `InputSystem.Update()` to miss or misprocess events, leading to desync. Keep the game focused throughout playback.
- **Real input devices are removed during replay.** Keyboard, mouse, and gamepad are removed from InputSystem while replaying to prevent interference. They are restored on exit. BepInEx hotkeys (F7) still work via the legacy Input API. On-screen buttons are available for all replay controls.
- **Opposing keys (e.g. A+D simultaneously) may resolve differently.** The replay records the resolved `xMoveAxis` value per span to handle this. The `x=N` field in the span overrides the composite result.
- **Springs may double-trigger on long replays.** Spring collision detection depends on exact sub-pixel positioning. Over very long replays (1000+ frames), tiny floating-point differences can cause a spring to fire an extra time.
- **Backward seeking replays from a checkpoint.** Seeking backward restores the nearest checkpoint before the target frame, then plays forward. Seek time is proportional to the distance from the nearest checkpoint.
- **Checkpoints are recorded at every second and every input change.** This balances file size and seek responsiveness. Enable `PerFrameCheckpoints` for maximum precision at the cost of much larger files.
