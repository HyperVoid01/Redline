# REDLINE: Game Design Document

**Working title:** Redline | **Author:** Sooriyan | **Engine:** Unity (Universal 3D / URP), C# | **Platform:** PC (WebGL optional) | **Timeline:** 2 weeks

*Draft. Values marked (tunable) are starting points to adjust in playtesting. Mechanics marked (proposed) are my design suggestions to confirm.*

---

## 1. Overview

**One-line pitch:** Drive a procedurally generated neon circuit in first person while enemy cars chase you down the track. Stay above the redline speed to stay alive, and reach the end.

**Genre:** Arcade driving / chase survival **Camera:** First person, driver seat **Session length:** 5 to 10 minutes per run (3 tracks) **Target audience:** Arcade driving fans, itch.io players, and course assessors

**Why it exists:** This is a scoped-down remake of the getaway driver game. The city and road-following AI were too big for the time frame, so the new design keeps the car movement and enemy cars, and replaces the city with a generated circuit in an empty neon void.

## 2. Design Pillars

1. **Speed is safety.** Going fast keeps you alive, and slowing down is the danger.
2. **Sound tells the story.** The redline chime is the main feedback. Chime on means safe, silence means trouble.
3. **Readable pressure.** The player should always be able to tell where threats are (mirror, audio) and why they lost.
4. **Small and finishable.** One core loop, a minimal environment, and three enemy variants built from one AI.

## 3. Core Loop

Drive the track, hold speed above the redline, pass gates for bonus time, dodge or outrun enemies, reach the finish, pick an upgrade, and start the next, harder track. Finish three tracks to win. Dying ends the run.

## 4. Core Mechanics

### 4.1 Driving

Reuse the existing car movement system. Handling should feel responsive and arcade-like, with strong acceleration and enough grip to hold speed through generated corners.

### 4.2 The Redline (signature mechanic)

- A **redline speed** is the threshold the player must stay above. (tunable: about 60% of top speed)
- **Above redline:** the chime plays, gate bonuses are active, and the stall meter drains.
- **Below redline:** the chime cuts out and the **stall meter** fills. (proposed)
- **Stall meter full:** the car is destroyed and the run ends. (proposed)
- The chime's pitch or tempo rises as speed approaches the redline, so the player can hear when they're close to the limit.
- The chime is an original sound (not sampled from any anime, game, or car).

### 4.3 Gates and Time

- Gates are glowing rings placed along the track.
- Passing a gate **above redline** adds time to the track timer. (tunable: +3 s)
- Passing below redline gives no bonus.
- The timer running out destroys the car.

### 4.4 Health and Damage

- The player has a health bar. (tunable: 100)
- Enemy contact deals damage, scaled by relative impact speed.
- Health at zero ends the run.
- Hitting track walls or obstacles scrubs speed and deals minor damage.

### 4.5 Win and Lose

- **Win:** reach the finish line of track 3.
- **Lose:** health reaches zero, the stall meter fills, or the timer expires.

## 5. Enemies

All enemies share one base AI that follows the track's **centerline waypoints** (no NavMesh). Each steers toward a point a set distance ahead on the path, plus an offset.

| Type | Behavior | Offset / Role |
| --- | --- | --- |
| **Chaser** | Follows behind and closes in | Centered, behind player |
| **Blocker** | Spawns ahead and holds a lane | Ahead of player, slower |
| **Side-rammer** | Pulls alongside and swerves in | Lateral offset toward player |

**Rubber-banding:** enemies speed up when far behind and slow down when close, so pressure stays fair. (tunable)

**Base speed:** chasers start slightly slower than the player's top speed, so the player can escape by driving well.

**Threat awareness:** an engine-sound proximity cue and the rear-view mirror show chasers behind the player.

## 6. Procedural Generation

**Method: segment stitching.**

- A library of 8 to 10 hand-built segments made from cubes: straight, gentle curve, chicane, ramp, gap, gate cluster, and enemy pocket.
- Each segment has an **entry and exit point** and its own centerline waypoints.
- A spawner chains random segments ahead of the player and removes those far behind.
- Each segment is tagged **easy, medium, or hard**. Later tracks pull from harder pools.
- A **seeded random generator** makes bugs reproducible.

**Fairness rules:**

- Limit the turn angle per segment and bias heading forward to prevent self-intersection.
- Cap curvature by difficulty tier so no corner is unwinnable at redline speed.
- Place gates only on drivable lines.

**Stretch:** a spline-generated track using the existing road generator.

## 7. Run Structure and Upgrades

| Track | Segments (tunable) | Difficulty | Enemies |
| --- | --- | --- | --- |
| 1 | 10 | Easy to medium | Chasers |
| 2 | 14 | Medium | Chasers + blockers |
| 3 | 18 | Medium to hard | All three types |

After tracks 1 and 2, the player picks **one of three** random upgrades (proposed):

- Armor: more max health
- Lower redline: a wider safe margin
- Gate bonus: more time per gate
- Faster stall drain: recover from slowdowns quicker
- Boost recharge: shorter cooldown on a short speed burst

## 8. Camera and Cockpit

- First person at the driver's eye position, using a 3D interior asset with separate parts (steering wheel, speedometer needle, mirror).
- **Speed feel:** FOV rises with speed (about 80 to 100), plus wind audio and light speed lines. Head bob and shake are subtle to avoid motion sickness.
- **Rear-view mirror:** a second camera renders to a RenderTexture on the mirror quad, at low resolution.
- The player's exterior mesh is hidden or set to shadows-only.
- Near clip plane set small (about 0.05 to 0.1) to prevent dashboard clipping.

## 9. Audio

| Sound | Purpose |
| --- | --- |
| Redline chime | Safe-speed indicator; changes as speed nears the limit |
| Chime cutoff | Sudden silence signals danger |
| Engine | Pitch tracks speed; enemy engines louder when close |
| Wind | Increases with speed |
| Gate pass | Positive hit sound (stronger above redline) |
| Impact and explosion | Collision and death feedback |

## 10. Visual Style

- **Neon void:** dark skybox, grid floor, glowing track edges, emissive gates, Volume fog and Bloom.
- Low-poly or stylized 3D assets for the interior and enemy cars, tinted to match the palette.
- Environment stays minimal: no buildings or props, only track segments and the void.
- All assets must be URP-compatible and licensed for redistribution.

## 11. UI and Flow

- **In-cockpit:** speedometer, with redline marked on the dial.
- **HUD:** timer, health bar, stall meter, and track progress.
- **Screens:** main menu, upgrade pick, game over, win and results (time, tracks cleared).
- **Warnings:** a flashing stall meter and audio when below redline.

## 12. Technical Notes

- Unity with the **Universal 3D (URP)** pipeline, set at project creation.
- Enemy AI uses waypoint following only, with no NavMesh.
- Systems: car controller, redline/stall manager, gate system, segment spawner, waypoint path, enemy AI with 3 behaviors, upgrade manager, HUD, mirror camera, audio manager.
- Version control on GitHub, with a Unity `.gitignore` and Git LFS for large assets if needed.

## 13. Schedule (2 Weeks)

| Day | Goal |
| --- | --- |
| 1 | Port the car controller, build a hand-made test track from cubes |
| 2 | Death conditions, redline and stall meter, gate bonus, timer, minimal HUD |
| 3 | Waypoint-following chaser with rubber-banding |
| 4 | Blocker and side-rammer variants; interior asset, camera, and mirror |
| 5 | Playtest and tune; cut anything not fun |
| 6 to 7 | Segment library and spawner, seeded generation |
| 8 | Generate waypoints per segment so enemies work on any track |
| 9 | Difficulty scaling across 3 tracks, upgrade picks |
| 10 | Menus, game over, win, and results screens |
| 11 | Neon visuals, particles, sound pass |
| 12 | External playtest, fix the top 3 frustrations |
| 13 to 14 | Bug fixes and build only; no new features |

## 14. Risks and Mitigations

| Risk | Mitigation |
| --- | --- |
| Generated corners feel unfair | Curvature caps per tier; test each segment alone |
| Enemy tuning eats time | Three variants from one base AI |
| First person is disorienting | Moderate FOV, subtle head bob, long sightlines, glowing gates |
| Asset incompatibility (pink materials) | Check URP support; use the Render Pipeline Converter |
| Scope creep | Cut anything outside the core loop by day 5 |
| Redline mechanic feels punishing | Tune the threshold, stall rate, and upgrades in playtests |

## 15. Stretch Goals

- Spline-generated track
- Daily seed
- Local best-time leaderboard
- Extra enemy type or additional upgrades

## 16. Success Criteria

- A complete run of 3 tracks is playable start to finish.
- The redline chime clearly communicates safe and unsafe speed without a tutorial.
- Generated tracks never produce an unwinnable corner or self-intersection.
- The game builds and runs on a standard PC.