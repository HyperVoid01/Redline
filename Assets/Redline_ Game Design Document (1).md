# REDLINE: Game Design Document

**Working title:** Redline | **Author:** Sooriyan | **Engine:** Unity 6 (6000.0.69f1), Universal 3D (URP), C# | **Platform:** PC | **Timeline:** 2 weeks

*Updated 17 October 2026 to match the game as built. Section 15 lists what changed from the original plan and why.*

---

## 1. Overview

**One-line pitch:** Drive a procedurally generated mountain road at midnight in first person while an armed enemy car chases you. Survive the gunfire, score points on the way, and reach the finish line.

**Genre:** Arcade driving / chase survival
**Camera:** Driver's seat (first person) or chase camera, switchable in play
**Session length:** About 2 to 4 minutes per run
**Target audience:** Arcade driving fans, itch.io players, course assessors

**Why it exists:** This is a scoped-down remake of the earlier getaway driver game. The city and the road-following police AI were too big for the time frame, so Redline keeps the car handling and the enemy cars and replaces the city with a generated road through a dark forest.

## 2. Design Pillars

1. **Speed and style.** Fast driving and drifting are what earn points.
2. **Readable pressure.** The player can always tell where the threat is: engine sound, headlights and bullet tracers behind them.
3. **Atmosphere.** A near-black forest lit only by headlights and occasional street lights, with a driving soundtrack.
4. **Small and finishable.** One core loop, one enemy type, and as few assets as possible.

## 3. Core Loop

Start at the beginning of a freshly generated road, drive forward while the enemy chases and shoots, drift and drive fast to build score, and cross the finish line to win. If health reaches zero the run ends. Then retry on a new road.

## 4. Core Mechanics

### 4.1 Driving

- The car is an AE86 driven with four WheelColliders. The front two steer, and drive torque goes mostly to the rear.
- Handling is arcade-like: strong acceleration, steering lock that reduces at high speed, and automatic counter-steer in slides.
- **Handbrake** locks the rear wheels and drops rear grip so the tail swings out. Grip returns gradually after release.
- **Traction control** cuts drive torque while the tyres slip. It can be switched off in Settings.
- Top speed is 150 km/h (set in `CarData`).
- Tyre smoke and skid marks appear when the tyres slip.

### 4.2 Staying on the road

- The player is not allowed to leave the road. Steel barriers run along both sides of the whole track.
- The land and vegetation beside the road are scenery only, so vegetation has no colliders.
- The road is 24 m wide.

### 4.3 Health and damage

- The player has 100 health.
- Enemy bullets deal 3 damage per hit.
- Parts of the car (bumpers, doors, bonnet and so on) fall off as health drops.
- Health reaching zero ends the run with an explosion, then the game over screen.
- Hitting a barrier scrubs speed, plays a scrape sound and rumbles the controller.

### 4.4 Win and lose

- **Win:** cross the finish line. The car slows itself down, the game dips into slow motion, and the win screen appears after a short delay.
- **Lose:** health reaches zero.

### 4.5 Scoring

| Source | Points |
| --- | --- |
| Distance driven forward | 1 per metre (reversing scores nothing) |
| Drifting | 50 per second while the car is sliding past the drift angle |
| Finish bonus | 1,000 |

The best score is saved with PlayerPrefs and shown in the main menu, on the game over screen and on the win screen. There are no checkpoints.

## 5. Enemy

There is one enemy type, spawned by `CarSpawner` behind the player at the start of the run.

- **Movement:** the enemy follows the track spline by distance, so it needs no NavMesh and no physics. It weaves gently side to side inside its lane.
- **Rubber-banding:** it matches the player's speed and speeds up or slows down to hold a target gap. The target gap starts at 30 m and shrinks slowly to a minimum of 12 m.
- **Fair play:** it never gets closer than 7 m, so it cannot overtake or ram the player. Its threat is its gun.
- **Gun:** after a 3 second delay it fires bursts of 10 bullets, 0.1 s apart, then rests for 2.5 s. Bullets travel at 110 m/s, lead the player's movement, and have a small random spread. Range is 70 m.
- **Bullets:** pooled, with trails, a hit particle on impact and a whiz sound on a near miss.
- The player cannot damage or attack the enemy. It chases for the whole run.

## 6. Procedural Generation

**Method: spline road with a `TrackData` preset.**

- `TrackMaker` walks forward building a list of straights, bends and sharp turns, weighted by the preset, then builds a spline, a road mesh and the barriers from it.
- The default preset makes a 4,000 m road with a flat start and finish straight, hills up to 15 m high with a maximum grade of 12%, bends of 20 to 60 degrees and sharp turns of 70 to 120 degrees.
- Heading drift is limited so the road does not turn back on itself too far.
- Steel barriers are placed in 4 m pieces on both sides of the road.
- `TrackData` is a ScriptableObject, so different presets can be swapped in.
- Each run uses a random seed. A fixed seed can be set for reproducing bugs.
- `TerrainStripGenerator` builds hilly land down both sides of the road. The land is flattened near any road section, so it never rises over the road. Its collider is a coarser mesh than the visible land, to keep loading fast.
- `VegetationScatterer` scatters trees, bushes, ferns and grass along the road in 40 m chunks. Chunks are switched off beyond a set distance.
- `TrackSampler` samples the spline every metre so that both cars know how far along the track they are.

## 7. Camera and Cockpit

- **Driver's seat camera:** mouse or right stick to look around, with a limited head turn. The view shifts with acceleration and shakes slightly at high speed. A recentre button returns the view to the front.
- **Chase camera:** follows behind the car, with a subtle shake.
- **V** (or Y/Triangle) cycles cameras, and the number keys jump straight to one. The mouse cursor is locked during play.
- The cockpit has a working steering wheel, speedometer and RPM gauge.
- A rear-view mirror renders from a second camera to a RenderTexture.

## 8. Audio

| Sound | Purpose |
| --- | --- |
| Engine | Sampled engine loops that crossfade with RPM, with gear shift sounds |
| Tyres | Skid loop and barrier scrape loop |
| Wind | Builds with speed |
| Gunfire | Burst sound for the enemy gun, bullet whiz on near misses, impact sounds on hits |
| Impact and explosion | Collision and death feedback |
| Music | Several driving tracks that play in random order and continue while paused, and lower on game over |
| UI | Hover sounds on menu buttons |

Volume for main, music and sound effects is adjustable in Settings. Each sound and track also has its own volume in the sound library.

## 9. Visual Style

- **Forest at midnight:** a very dark scene with exponential fog, steel barriers, trees and vegetation, and almost no light.
- Light comes from the player's headlights, the enemy's headlights behind, and the occasional street light.
- Post-processing through a URP Volume.
- Tyre smoke, skid marks, muzzle flashes, bullet trails and hit particles add motion and feedback.
- All assets are URP-compatible 3D models and textures, chosen to keep the number of environment assets low.

## 10. UI and Flow

- **Main menu:** title, Play, Settings, and the saved high score.
- **HUD:** speed (km/h or mph), live score, health bar. The HUD hides when the camera view asks for it.
- **Pause menu:** resume, settings, main menu. Escape or Start pauses and resumes.
- **Game over screen:** score, best score, a "new high score" label, retry and main menu.
- **Win screen:** score, best score, a "new high score" label, retry and main menu.
- **Settings:** audio volumes; mouse and stick sensitivity, invert look, steering response, steering assist, traction control and rumble strength; speed unit; quality, fullscreen, resolution, VSync and frame rate cap.
- All menus work with mouse, keyboard and controller.

## 11. Controls

| Action | Keyboard and mouse | Controller |
| --- | --- | --- |
| Accelerate | W / Up | Right trigger |
| Brake and reverse | S / Down | Left trigger |
| Steer | A, D / Left, Right | Left stick |
| Handbrake | Space | A / Cross |
| Change camera | V (1 to 9 for a specific camera) | Y / Triangle |
| Look | Mouse | Right stick |
| Recentre view | C | Right stick click |
| Pause | Esc | Start |
| Back | Esc | B / Circle |

Controller rumble fires on barrier hits and gives a slight shake at high speed. Its strength is set in Settings.

## 12. Technical Notes

- Unity 6 (6000.0.69f1) with URP 17. Packages used include the Input System, Splines and TextMeshPro.
- Enemy movement follows the spline directly: a kinematic Rigidbody moved by distance along the track. There is no NavMesh and no physics on the enemy.
- Systems: car controller, wheel and tyre effects, part detacher, enemy car and gun, car spawner, track maker, terrain and vegetation generators, track sampler and progress, camera controllers and switcher, game manager, score manager, UI manager, settings manager, audio (sound player, music player, sound library, car audio, engine audio), input manager, rumble.
- **Performance:** about 80 FPS on the development machine after optimisation. Changes made: a coarser terrain collider, vegetation placed on the true surface height, rear tyre friction only updated when it changes, one shared wheel ground-hit read per frame, and UI updates that skip unchanged values. Bullets, hit effects and sounds are pooled, and distant vegetation is switched off in chunks.
- Version control on GitHub with a Unity `.gitignore` and Git LFS for large assets.

## 13. Risks and Mitigations

| Risk | Mitigation |
| --- | --- |
| Generated corners feel unfair | Limits on bend angle and radius in `TrackData`; heading drift cap; barriers keep the player on the road |
| Enemy feels unfair or too easy | Rubber-banded gap, minimum gap, gun delay, burst and cooldown timing, spread and damage are all tunable |
| First person is disorienting | Moderate head movement, a recentre button, and a chase camera as an alternative |
| Large forest slows the game | Chunked vegetation with distance switching, coarse terrain collider, no shadows on headlights |
| Asset licences | Credits listed in the README; licences to be checked before redistribution |
| Scope creep | Features outside the core loop were cut (see section 15) |

## 14. Stretch Goals

- More enemy types (a blocker ahead of the player, or a car that pulls alongside)
- Several tracks with rising difficulty
- Daily seed and a local best-time leaderboard
- Difficulty that rises along the track (faster enemy, faster fire rate)
- Instant retry key

## 15. Changes From the Original Plan

| Original plan | What was built | Why |
| --- | --- | --- |
| Neon void with a grid floor | Forest at midnight with fog, trees and steel barriers | A dark forest reads well with headlights and needs few assets |
| Hand-built segments stitched together | One spline road built from weighted straights, bends and sharp turns | Gives smooth roads, height variation, and a path for the enemy to follow |
| Redline speed, chime and stall meter | Removed | The countdown timer was removed, so the mechanic no longer fits |
| Gates that add time, countdown timer | Removed | Replaced by a score from distance, drifting and a finish bonus |
| Three tracks with upgrades between them | One track per run | Cut to keep the scope finishable |
| Three enemy types (chaser, blocker, side-rammer) | One chaser with a gun | The chaser plus gunfire gives enough pressure |
| Enemy contact damage | Enemy bullets only | The enemy keeps a minimum gap, so it cannot ram the player |
| Waypoint paths per segment | The enemy follows the track spline directly | Simpler, and works on any generated track |
| Rear-view mirror, FOV effects | Mirror camera, steering wheel, speedometer and RPM gauge in the cockpit | Kept the cockpit and mirror; dropped speed-based FOV |
| Health, stall meter, timer on the HUD | Health, speed and score on the HUD | Matches the mechanics that remain |

## 16. Success Criteria

- A complete run, from the start line to the finish line, is playable.
- Generated roads never produce an unwinnable corner or a self-intersection.
- The player can survive the enemy by driving well, and can lose by driving badly.
- The game holds a smooth frame rate on a standard PC.
- The game builds and runs, with controls and settings that work on keyboard and controller.
