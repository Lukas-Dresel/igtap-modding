# Assembly-CSharp Source Map

Decompiled source reference for IGTAP modding. Each entry describes what a file
covers and which lines to look at. All paths are relative to
`output/decompiled/Assembly-CSharp/`.

---

## Player Movement & Physics

### Movement.cs
Core player controller — the largest file in the game.

| What you'll find | Lines |
|---|---|
| Starting-position debug enum (`placesToStart`) | 11 |
| Cutscene mode enum (`cutsceneModes`) | 21 |
| Ground-type enum (`groundTypes`) | 30 |
| Run speed, jump force, gravity tuning | 102–108 |
| Ability unlock flags (wall-jump, double-jump, dash, block-swap) | 185–191 |
| Facing direction & animator refs | 251–255 |
| Cutscene mode field | 215 |
| Respawn point | 312 |
| Light radius / active state | 336–339 |
| Main Update loop | 467 |
| Death handler (`onDeath`) | search `onDeath` |
| Spring collision (`hitSpring`) | search `hitSpring` |
| Save / load | search `save()` / `load()` |

Questions this answers: How does the player move? What physics
values control speed/jump/gravity? How are abilities gated? What
triggers cutscenes? How does respawning work?

### SaveableObject.cs
Abstract base class for anything that persists across sessions.

| What you'll find | Lines |
|---|---|
| Abstract `save()` / `load()` contract | 5–8 |

Questions this answers: What interface must a saveable object implement?

### PlayerAnimationEventProxy.cs
Relays animation events (e.g. footsteps) back to the Movement component.

| What you'll find | Lines |
|---|---|
| `RelayFootstep()` | 8 |

### playerSpriteLookup.cs
Indexes all player animation sprites into a dictionary.

| What you'll find | Lines |
|---|---|
| Sprite array & lookup dict | 6–8 |

### PlayerinputBugWorkaround.cs
Singleton that refreshes PlayerInput to work around a Unity bug.

| What you'll find | Lines |
|---|---|
| `refreshPlayerInput()` | 9 |

---

## Course / Level System

### courseScript.cs
Per-course logic — rewards, tracking, clone paths, prestige.

| What you'll find | Lines |
|---|---|
| Base reward field | 98 |
| Course number | 110 |
| Cost/reward tier fields | 128–131 |
| On-screen & init flags | 160–162 |
| `startTracking()` — begin recording player path | 217 |
| `stopTracking()` — end recording | 234 |
| `UpdateReward()` | 307 |
| Clone multiplier bonus | 318 |
| Save / load serialization | 329 / 393 |
| `onPrestige()` | 464 |
| `HasBeenCleared()` | 470 |
| `fullUpdate()` | 479 |

Questions this answers: How are course rewards calculated? How does
player-path recording work for clones? What gets saved per course?
What resets on prestige?

### startGate.cs
Trigger collider that marks the start of a course.

| What you'll find | Lines |
|---|---|
| Reset point reference | 6 |

### endGate.cs
Trigger collider that marks the end of a course.

| What you'll find | Lines |
|---|---|
| `isEndOfCourse` flag | 6 |

### checkpointScript.cs
Respawn-point trigger — minimal script, behaviour is in Movement.

### spikeScript.cs
Spike hazard — kills the player on contact.

### SpringScript.cs
Bouncy platform mechanic.

| What you'll find | Lines |
|---|---|
| Strength, upward force, movement lock duration | 6–12 |
| Animator reference | 15 |

---

## Currency & Economy

### globalStats.cs
Central game-state singleton — currencies, global upgrades.

| What you'll find | Lines |
|---|---|
| Global upgrade enum (`globalUpgradeSet`, 13 types) | 5 |
| Currency enum (`Currencies`) | 22 |
| `currencyLookup` — live currency values | 34 |
| `globalCurrencyValues` — persistent list | 41 |
| `globalUpgradeDict` / `globalUpgradeValues` | 43–45 |
| Atom count fields | 30–32 |

Questions this answers: What currencies exist? How are global
upgrades tracked? Where is the authoritative game-economy state?

### CashDisplay.cs
Formats and displays currency values with color and lerp.

| What you'll find | Lines |
|---|---|
| Currency color array | 17 |
| `updateCashDisplay()` — formatting logic | 36 |
| Lerp rate | 14 |

### fullCurrencyReadout.cs
Full currency panel — wires up per-currency displays.

| What you'll find | Lines |
|---|---|
| Watt / GreenPower / AtomicPower display refs | 9–19 |

### CurrencyDisplayController.cs
Tab-switching controller for currency HUD.

| What you'll find | Lines |
|---|---|
| Tab enum (none / mini / full) | inner enum |
| Active tab field | 21 |
| `swapToMiniDisplay()` / `swapToFullDisplay()` / `swapToNoDisplay()` | 86 / 93 / 100 |

---

## Upgrades

### upgradeBox.cs
Purchasable upgrade box — UI, cost scaling, buy logic.

| What you'll find | Lines |
|---|---|
| Movement-unlock enum (`movementUpgrades`) | 11 |
| Upgrade type fields (local + global) | 20–22 |
| Active / visible / buyMax flags | 24–29 |
| Current cost | 39 |
| Purchase cap | 87 |
| Times used counter | 109 |
| `customBoxUpdate()` — per-frame update | 202 |
| `BoostOldCourses()` | 236 |
| `setBoxText()` — cost/label display | 284 |
| `buyUpgrade()` — collision-triggered purchase | 376 |
| `DoBuyUpgrade()` — actual purchase execution | 404 |

Questions this answers: How is upgrade cost calculated? What
happens when the player buys an upgrade? How do caps work? How do
upgrades interact with courses?

### localUpgrades.cs
Per-course upgrade state manager.

| What you'll find | Lines |
|---|---|
| Local upgrade enum (9 types) | 7 |
| Upgrade dict & persistent values | 24–37 |
| Localisation dictionaries | 39 |
| `onPrestige()` | 124 |
| `updateAllBoxes()` | 139 |

### prestigeEnabler.cs
Trigger that enables a prestige upgrade box.

| What you'll find | Lines |
|---|---|
| Box reference | 6 |

---

## Clone System

### clonesScript.cs
Manages clone spawning, paths, and rendering.

| What you'll find | Lines |
|---|---|
| On-screen flag | 8 |
| Clone count | 30 |
| Path data (positions, sprites, scales, length) | 50–58 |
| `updateClonePath()` | 227 |
| `spawnNewClone()` | 262 |
| `updateCloneInterval()` | 282 |
| `isOnScreen()` | 288 |
| `onPrestige()` | 302 |

Questions this answers: How are clones spawned? How do they follow
recorded player paths? What controls clone frequency?

### clonesLoD.cs
Level-of-detail trigger for clones + course music track assignment.

| What you'll find | Lines |
|---|---|
| LOD override flag | 6 |
| Course music track | 9 |

### cloneShaderScript.cs
Updates clone shader material parameters each frame.

| What you'll find | Lines |
|---|---|
| Material array | 6 |

---

## Atom / Power System

### AtomColliderSystem.cs
Manages atomic-power atoms — spawning, tiering, collisions.

| What you'll find | Lines |
|---|---|
| Waiting atoms counter | 21 |
| Atom contents list (by tier) | 23 |
| Sprite prefab | 30 |
| Base spawn interval | 86 |
| `BuyNewAtom()` | 106 |
| `AddNewAtom(tier)` | 111 |
| Save / load | 256 / 270 |

Questions this answers: How does the atom system work? How are atoms
tiered? What controls spawn rate?

---

## Camera

### cameraMover.cs
Main camera follow logic.

| What you'll find | Lines |
|---|---|
| Auto-mode flag | 5 |
| Follow target | 7 |
| Default & current offset | 10–12 |
| Orthographic size | 14 |
| `newTarget()` | 34 |
| `newCamSize()` | 41 |

### CamShake.cs
Screen-shake effect system (singleton).

| What you'll find | Lines |
|---|---|
| Shake preset enum | inner enum |
| `DoShake()` — returns current offset | 62 |
| `AddShake()` — preset overload | 74 |
| `AddShake()` — manual params overload | 80 |

### camSizeTrigger.cs
Trigger zone that changes camera zoom.

| What you'll find | Lines |
|---|---|
| Target size | 6 |

### camZoneScript.cs
Trigger zone that retargets camera.

| What you'll find | Lines |
|---|---|
| Target-player flag | 6 |
| Camera offset | 9 |

---

## Audio

### Audio.cs
SFX playback singleton.

| What you'll find | Lines |
|---|---|
| Audio ID enum (MetalLand, MetalFootstep, BoxHit, Spring) | inner enum |
| Audio library array | 27 |
| `PlayAudio()` | 35 |
| `PlayAudioSetPitch()` | 66 |
| `PlayAudioAtPoint()` | 97 |

### MusicPlayer.cs
Music track manager singleton.

| What you'll find | Lines |
|---|---|
| Track ID enum (none, Area1Track1, Area2Track2) | inner enum |
| Current track field | 34 |
| `quickSwapMusic()` | 41 |
| `playNewMusic()` | 84 |

---

## Persistence

### Saveloader.cs
Top-level save manager — triggers save on all SaveableObjects.

| What you'll find | Lines |
|---|---|
| `delayedManualSave()` | 54 |
| `manualSave()` | 59 |

### timer.cs
Tracks total playtime.

| What you'll find | Lines |
|---|---|
| Elapsed time field | 10 |
| `load()` / `save()` | 12 / 25 |

---

## Progression & World

### tripBreakerScript.cs
Circuit-breaker mechanic gating area progression.

| What you'll find | Lines |
|---|---|
| Fixed-areas state array | 40 |
| Breaker active flag | 42 |
| `TripBreaker()` | 46 |
| `fixedArea(courseNumber)` | 80 |
| Save / load | 106 / 114 |

Questions this answers: How does area-unlock progression work? What
triggers a breaker trip?

### TreeController.cs
Tree growth progression visualization.

| What you'll find | Lines |
|---|---|
| Growing-in-progress flag | 8 |
| Segments left / grown | 10–12 |
| `onNewGrowCommandRecieved()` | 30 |
| `GrowNewSegment()` | 41 |
| `OnNewSegmentGrown()` | 47 |

### TreeSegmentActivationComponent.cs
Individual tree segment growth animation.

| What you'll find | Lines |
|---|---|
| Trunk, land, box, lights refs | 9–18 |
| `growTreePart()` | 20 |

### colouredBlockSwapper.cs
Orange/blue tilemap toggle mechanic.

| What you'll find | Lines |
|---|---|
| `isBlueActive` state | 12 |
| `swapBlocks()` | 22 |

### zoneChanger.cs
Area/zone transition trigger.

| What you'll find | Lines |
|---|---|
| Area references | 6–9 |
| Dark flags per area | 12–15 |

### ZoneDisableLoadList.cs
List of objects to disable/load per zone.

| What you'll find | Lines |
|---|---|
| Object list | 5 |

---

## UI & Menus

### pauseMenuScript.cs
Pause menu controller.

| What you'll find | Lines |
|---|---|
| Menu-open / settings-open flags | 42–44 |
| `changeScene()` | 122 |
| `updateControlMode()` | 132 |
| `settingsButtonPressed()` / `CloseSettingsButtonPressed()` | 157 / 168 |
| `menuButtonPressed()` | 185 |
| `DeleteSavePressed()` | 239 |
| `QuitGame()` | 283 |
| `resetButtonPressed()` | 288 |

### SettingsScript.cs
Settings menu bindings.

| What you'll find | Lines |
|---|---|
| `onFullscreenModeChanged()` | 80 |
| `onVsyncChanged()` | 96 |
| `onFPSChanged()` | 110 |
| `onLanguageChanged()` | 116 |
| `updateLevels()` | 131 |

### SceneTransitionObject.cs
Animated scene loader.

| What you'll find | Lines |
|---|---|
| Scene name + animator | 7–10 |
| `loadScene()` | 17 |

---

## Input

### KeybindSetterItemScript.cs
Single keybind row in the rebinding UI.

| What you'll find | Lines |
|---|---|
| Action + composite index | 9–12 |
| Display refs (keyboard, controller) | 15–18 |
| `setNewKeyboardBind()` / `setNewControllerBind()` | 35 / 58 |

### KeypromptSetter.cs
Shows the correct input glyph for the current device.

| What you'll find | Lines |
|---|---|
| Action reference | 11 |
| Font options | 16 |

### ButtonToGlyphDict.cs
Maps button/key names to unicode glyph characters.

| What you'll find | Lines |
|---|---|
| Button-to-glyph dict | 5 |
| Key-to-glyph dict | 14 |

### PromptFont.cs
Static ASCII character constants used by the glyph system. ~250 entries — no
game logic, pure data.

---

## Visual Effects

### LightFlicker.cs
Ambient light flicker (intensity, speed, min bounds).

| What you'll find | Lines |
|---|---|
| Flicker params | 12–21 |

### playerLightFlickerVariant.cs
Player-attached light flicker variant.

| What you'll find | Lines |
|---|---|
| Flicker params | 14–17 |

### LightLerper.cs
Smooth light radius transitions.

| What you'll find | Lines |
|---|---|
| `activateLight()` / `deactivateLight()` | 16 / 25 |

### backgroundScroller.cs
Parallax background scrolling.

| What you'll find | Lines |
|---|---|
| Camera ref + parallax factors | 19–23 |

### GenericSpriteDisabler.cs
Hides sprites when the player is nearby.

| What you'll find | Lines |
|---|---|
| Sprite list | 7 |

### ObjectBobber.cs
Vertical bobbing animation for collectibles/decorations.

| What you'll find | Lines |
|---|---|
| Bob target, multiplier, rate | 6–12 |

---

## Infrastructure

### Singleton.cs
Generic singleton base — used by Audio, globalStats, CamShake, etc.

| What you'll find | Lines |
|---|---|
| `Instance` property | 8 |

### Utilities.cs
General-purpose helpers.

| What you'll find | Lines |
|---|---|
| `ObjectLerp()` | 6 |

### LinkOpener.cs
Opens a URL in the system browser.

| What you'll find | Lines |
|---|---|
| Link field + `openLink()` | 6–8 |

---

## Misc / Minimal

| File | One-liner |
|---|---|
| disableOnImpulse.cs | Disables its GameObject on event call |
| DisableOnStartComponent.cs | Disables its GameObject on Start |
| endOfDemoScript.cs | Empty demo-end placeholder |
| longFallColliderController.cs | Triggers a cutscene mode + fall speed multiplier |
| TutorialTextTrigger.cs | Fades in tutorial text on player collision |
