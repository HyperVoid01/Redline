# Redline

A first-person arcade driving game. Race a Toyota AE86 down a procedurally generated mountain road at midnight while an enemy car chases you from behind and shoots at you. Reach the finish line alive, and score as high as you can on the way.

**Engine:** Unity 6 (6000.0.69f1), Universal Render Pipeline | **Language:** C# | **Platform:** PC (Windows)
**Author:** Sooriyan | **Play it:** https://hypervoid01.itch.io/

---

## How to play

- Every run generates a new 4 km road with bends, sharp turns and hills, lined with steel barriers.
- An enemy car chases you the whole way and fires bursts of bullets. Your car cannot attack. Outrun it and keep out of its line of fire.
- Each hit damages your car, and parts fall off as your health drops. Health reaching zero ends the run.
- Cross the finish line to win.
- Stay on the road. The barriers keep you in, and the land beside the road is scenery only.

### Scoring

| Source | Points |
| --- | --- |
| Distance driven forward | 1 point per metre (reversing scores nothing) |
| Drifting | 50 points per second while sliding |
| Reaching the finish | 1,000 bonus |

Your best score is saved on your computer and shown in the main menu and on the game over and win screens.

---

## Controls

| Action | Keyboard and mouse | Controller |
| --- | --- | --- |
| Accelerate | W or Up Arrow | Right trigger |
| Brake and reverse | S or Down Arrow | Left trigger |
| Steer | A / D or Left / Right Arrow | Left stick |
| Handbrake (drift) | Space | A / Cross (south button) |
| Change camera | V | Y / Triangle (north button) |
| Jump to a camera | 1 to 9 | |
| Look around (driver's seat view) | Mouse | Right stick |
| Re-centre the view | C | Right stick click |
| Pause and resume | Esc | Start |
| Go back (settings) | Esc | B / Circle |

Tips:
- Tap the handbrake in a corner to swing the tail out and earn drift points. The car counter-steers for you, and you can turn that assist down in Settings.
- Going fast on the straights scores the most, but the road has sharp turns. Brake early.

---

## Settings

Open Settings from the main menu or the pause menu.

- **Audio:** main, music and sound effects volume.
- **Controls:** mouse and stick sensitivity, invert look Y, steering response, steering assist, traction control, controller rumble strength.
- **Interface:** speed in km/h or mph.
- **Graphics:** quality level, fullscreen, resolution, VSync and frame rate limit.

Settings are saved automatically.

---

## Running the project

1. Install **Unity 6000.0.69f1** with the Windows build support module.
2. Clone the repository and open the folder in Unity Hub. The first import takes a few minutes.
3. Open `Assets/Scenes/MainMenu.unity` and press Play. The scenes in the build are `MainMenu` and `Game`.
4. To build: File > Build Profiles > Windows > Build.

Some large assets (models, textures, audio) are stored with Git LFS. Install Git LFS before cloning, or they will arrive as small placeholder files.

---

## How it works

| Area | Scripts | What it does |
| --- | --- | --- |
| Track | `TrackMaker`, `TrackData`, `TrackSampler` | Builds a road along a spline from a `TrackData` preset (length, bends, hills, barriers) and samples it by distance |
| Land and plants | `TerrainStripGenerator`, `VegetationScatterer` | Builds hilly land down both sides of the road with a coarse collider, then scatters trees, bushes and ferns in chunks that switch on and off by distance |
| Player car | `CarController`, `WheelController`, `CarData`, `TireEffects`, `CarPartDetacher` | WheelCollider driving with traction control and drift assist, tyre smoke and skid marks, parts that fall off as health drops |
| Enemy | `EnemyCarController`, `EnemyGun`, `EnemyBullet`, `CarSpawner` | An enemy that follows the track spline with rubber-banded speed, and a gun with pooled bullets, trails and hit effects |
| Cameras | `CameraController`, `InteriorCameraController`, `CameraSwitcher` | Chase camera and a driver's seat camera with mouse look, switchable in play |
| Game flow | `GameManager`, `PlayerHealth`, `FinishTrigger`, `ScoreManager`, `UIManager` | Pause, game over, win, slow motion on finish, score and high score, HUD |
| Audio | `SoundPlayer`, `MusicPlayer`, `SoundLibrary`, `CarAudio`, `EngineAudioDriver` | Pooled sound effects, shuffled music, sampled engine sound that follows RPM |
| Input and settings | `InputManager`, `SettingsManager`, `CarRumble` | Input System bindings for keyboard and controller, saved settings, controller rumble |

The design document is in `Assets/Redline_ Game Design Document.md`.

---

## Credits

- Car model: "Toyota AE86 Sprinter Trueno Zenki" by Martin Trafas ([@TinoD2](https://sketchfab.com/TinoD2)) on [Sketchfab](https://sketchfab.com/3d-models/toyota-ae86-sprinter-trueno-zenki-a5737bf3cc9b4179a6e5ebe173ff70d9), licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).
- Speedometer and gauge set: "Greddy Racing Meter Set" by Dorichan.
- Music: "ハイスピード☆パラダイス (High-Speed☆Paradise)" and other tracks by ZTMusic, plus tracks by kissan4 and yoshiyuki_tatsuya, from [Pixabay](https://pixabay.com/music/dance-ztmusic-%E3%83%8F%E3%82%A4%E3%82%B9%E3%83%94%E3%83%BC%E3%83%89%E3%83%91%E3%83%A9%E3%83%80%E3%82%A4%E3%82%B9-high-speedparadise-eurobeat-373558/) under the Pixabay Content License. "European Canyon" by unlistenable from Freesound.
- Built with Unity, the Universal Render Pipeline, the Input System, Splines and TextMeshPro.

Check the licence of every asset before redistributing a build.