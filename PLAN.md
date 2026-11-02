# ANGER FOOT VR: Plan

A full VR mod for **Anger Foot** (Free Lives / Devolver, 2023; Steam 1978590): kick your way through the city in VR
with a **full body** (your legs swing the game's kicks), **motion-aimed guns** in your hands, **button kicks**, its own
**cinematic screen area**, its own **VR launcher** with the game's music, and a short **opening** when the mod starts.

The bar: a clear step up from InkVR, Dark Revival VR and Mirror's Edge VR in every part: launcher, opening, menus,
body, hands, feel and performance. It's built on what those mods proved, and nothing official is shipped: art, music and
sound are read from your own copy of the game.

Status: **phase 0 done: GO** (user's OK 2026-11-02, "go ahead, A/X kicks are good"). Phase 1 under way. Fallen Aces
VR comes after Anger Foot VR is released.

Phase 0 found:
- **VR route 1 works:** Unity's OpenXR provider, pre-1.0 (0.1.2-preview.2), is the one whose start-up runs on Unity
  2019.4 (1.x crashes in UnityPluginLoad). It needs the old entry points, the package's own init order, and a hook on
  xrGetInstanceProcAddr that drops extensions a runtime doesn't offer.
- Both eyes render the title and the first level through the built-in pipeline, with post-processing (PPv2).
- **No stripping:** the game ships full .NET and Unity libraries, so BepInEx 5 works out of the box.
- **Cameras:** Main, Effects and Weapon cameras plus a mono Decal Prepass camera, all under PlayerCamera.
- **VR graphics profile:** first level at 14 ms on the test runtime and 1.9 GB of video memory, down from 4.9 GB with
  the game's High settings. The profile turns ambient occlusion off, textures one step down on GPUs with 6 GB or less,
  and chromatic aberration off. It's applied in memory only, never saved.
- **Saves:** dev runs keep their own saves and settings (BepInEx/AngerFootVR/dev-saves). The player's real save is
  never touched.

## What the game is made of (from the user's 2024 Player.log; to confirm once it's installed)

| | | What it means for VR |
|---|---|---|
| Engine | Unity **2019.4.35f1**, game 1.46 | Older than InkVR's Unity 2022: the VR core's route is chosen in phase 0 (below) |
| Scripting | **Mono** (Managed folder) | The code is readable; BepInEx 5 and Harmony patch it directly |
| Graphics | **Direct3D 11** | As InkVR. Render pipeline expected to be the built-in one (InkVR's proven path); checked in phase 0 |
| XR | Unity's XR plugin system is in the build (it searches UnitySubsystems) | A VR provider can be registered the same way MOUSE VR did, if its version fits 2019.4 |
| Input | **Rewired** | A virtual controller fed from the VR controllers (as InkVR's virtual pad) |
| Saves | `LocalLow\Free Lives\Anger Foot` (Saves, Settings.json) | Backed up before any test; never changed by the mod |

## The VR core: three routes, picked in phase 0

1. **Unity's OpenXR provider** registered through UnitySubsystems (as in MOUSE VR), if it runs on Unity 2019.4. This is
   native OpenXR, so Virtual Desktop (VDXR) works directly.
2. **Valve's OpenVR XR plugin**, which officially supports Unity 2019.4. It goes through SteamVR, which Virtual Desktop
   can run.
3. **Our own OpenXR renderer**: the game's camera renders each eye and the mod hands the images to OpenXR itself. This
   is the most work, but it's native on every headset and gives full control of timing and quality.

Phase 0 tries them in that order, with one short run each, and keeps the first that works cleanly.

## Playing it

- **Guns in your hand.** The gun you carry is held in your hand and aims where your hand points. Trigger shoots. Throw an
  empty gun with a throwing motion; letting go with grip throws it too. Guns are only what the game gives you: you pick
  up what enemies drop, as the game does.
- **Button kicks.** A/X kicks with either hand. Your body's leg swings the kick in time with the game's own kick, so you
  see your foot hit the door. The kick goes where you look, as in the game.
- **Full body.** It uses InkVR's proven body solver, with Anger Foot's shoes on your feet: the pair you picked in the
  shoe menu, so new shoes show up on your own feet. Look down and you see your legs, shoes and body, and the body shows
  in mirrors.
- **Moving:** smooth movement on the stick, jump on a button, snap or smooth turn. **Comfort for a fast game:** an
  optional tunnel vignette at speed, the game's screen shake and FOV punches toned down for the headset (an option), and
  no forced camera movement.
- **Playable for everyone:** every action also works on buttons. Aiming can follow your head or either hand.
- **Feel:** controller rumble on kicks, hits, shots and door breaks, from the game's own events. Hand sounds use the
  game's own effects. The game pauses when you take the headset off.

## The cinematic screen area

A place in Anger Foot's own style, built in code like InkVR's theatre (the quality bar the user liked): a run-down neon
movie house in the game's city, with loud signage, posters made from the game's own art and a shoe display case.
The title menu, pause menu, level select and the game's story screens and level intros play on its big screen. You can
walk around it, and the details stay small and fitting: a lobby mirror for your body, a projection booth and the shoe
wall.

## The opening

- **When the VR mod starts:** a short in-headset opening (about 10 seconds). A dark hallway, a door gets kicked in, and
  the ANGER FOOT VR title slams onto the screen to the game's music, then you're in the movie house.
  - Any button skips it.
  - OPENING: ON / FIRST LAUNCH ONLY / OFF in VR settings and the launcher.
  - Lesson from InkVR (its intro was removed after testing): keep it short, never block the menu, and make it easy to
    switch off.
- **The launcher** has its own opening animation too, with an ON/OFF option.

## The VR launcher

Its own app in Anger Foot's style, never copied from anyone else's (Outlast VR's launcher is inspiration only): bold neon
type, a kicked-door opening and the game's soundtrack playing, read from your copy of the game at first run and never
shipped.

| Page | What it does |
|---|---|
| **INSTALL / UPDATE** | BepInEx and the mod into the game folder, listing every file it adds, so UNINSTALL removes them all |
| **PLAY IN VR** | Also offers a desktop shortcut |
| **VR CHECK** | Headset runtime (Virtual Desktop, Link or SteamVR), what needs fixing, and a free RAM/VRAM check before launch |
| **OPTIONS** | Every VR setting: comfort, opening, spectator view and FOV, music volume |
| **CONTROLS** | Your controllers' layout, with remapping |
| **UNINSTALL** | Leaves the game exactly as it was |

## Polish carried over from InkVR 2.0 (made better here)

- Steady spectator view with FOV.
- Game haptics and hand sounds.
- Pause when away.
- A HOW TO PLAY card and a VR SETTINGS board in the movie house.
- Native layouts for Quest, Index, Vive and WMR controllers.
- 90 fps on the RTX 4050 / Quest 3 before the first test, with a frame-time readout in the dev log.

## Safety (after the MOUSE crash)

- Test runs are light:
  - Check free RAM/VRAM and that no old game instance is running before each launch.
  - Make few launches, each one longer.
  - Release every test texture.
  - At the first warning sign (out of memory, process errors, a GPU stall) stop, close the game and report.
- The user's saves and Settings.json are backed up before the first run and compared after each one. The game's
  settings are never changed.
- The dev loader is only switched on during a dev run, so the user's own launches stay vanilla.

## Phases

| Phase | What | Done when |
|---|---|---|
| **0. Feasibility** | Install the game. File checks only: render pipeline, stripping, audio system, Rewired, the kick and gun code. Then one short run per VR route, with safety checks | VR route chosen; both eyes render; frame time known. Go/no-go report |
| **1. VR core** | Head-tracked camera, both eyes, menus off the face, spectator view | Play a level seated with a gamepad in VR |
| **2. Controls** | Rewired virtual controller, guns in the hand, button kicks, throw, comfort options | Play a level with VR controllers only |
| **3. Full body** | Body and legs, kick leg-swing synced to the game, your shoes on your feet, mirror | Look down and see yourself kick |
| **4. Movie house** | The cinematic screen area, menus and story screens on its screen, VR SETTINGS and HOW TO PLAY | Whole game playable from start to credits |
| **5. Launcher + opening** | The launcher with music, its opening, the in-VR opening, install and uninstall | A fresh install works from the launcher alone |
| **6. Polish** | Haptics, sounds, performance pass, a full playthrough, teasers | Ready for the user's headset test |

Versions:
- **0.1.0:** phases 0 to 2. A first test build, playable with motion guns and kicks.
- **0.2.0:** body, movie house and launcher.
- **1.0:** the polished release.

Following the user's rules, nothing goes public until the user has tested that exact build.

## Risks

- **Unity 2019.4's VR route:** phase 0 has three routes.
- **Speed:** Anger Foot is fast, so the comfort options matter, and so does keeping 90 fps.
- **Kicks:** the game's kick animation and timing must line up with the body's leg.
- **The game's camera effects** (screen shake, FOV punches, slow motion) need VR versions that keep the feel without
  making anyone sick.
