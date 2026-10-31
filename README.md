# ROVERNAUTS

**CISC 486 · Group 7 · Assignment 2 alpha prototype**

Rovernauts is a co-op, wave-based survival game. Two to four players share one space rover on a hostile moon: one player drives and the others crew the turrets. Together they hold out against waves of alien enemies, bosses and moon events for as long as they can.

**Gameplay video (30-60 s):** [Rovernauts Alpha Demo](https://www.youtube.com/watch?v=GKxgjDqZYaE)

**AI Usage Disclaimer:** 
This project was worked on in accordance with the CISC 486 course academic integrity guidelines which stated level 4 AI usage permitted. Generative AI tools (Anthropic Claude - approved by Queen's University for ideation, brainstorming, and first drafts) were utilized for the following developmental tasks:
- **Debugging:** Identifying and resolving syntax and runtime errors. Helping to fix issues we ran into.
- **Logical Arrangement:** Reorganizing existing code structure for better readability and optimization.
- **Ideation:** Placeholder models and brainstorming of potential pathways.

---

## Running the alpha

1. Install **Unity 6000.6.7f1** (Unity 6.6) through Unity Hub. Windows build support is included with the editor.
2. In Unity Hub, choose **Add → Add project from disk** and select this folder.
3. Open `Assets/Scenes/Main.unity` and press **Play**.
   If the scene is missing, use the menu **Rovernauts → Rebuild Main Scene**. The game also bootstraps itself from an empty scene.
4. To make a standalone build, use **Rovernauts → Build Windows Player**. The output goes to `Builds/Windows/Rovernauts.exe`.

The entire game is built at runtime from code and Unity primitives. There are no imported assets to go missing.

## Controls (local co-op on one rover)

| Crew | Role | Controls |
| --- | --- | --- |
| P1 | Driver | `WASD` / arrow keys to drive (turns in place, like a tank). `Space` / `Shift` to boost. |
| P2 | Main gunner | Mouse to aim. Hold left click to fire. |
| P3-P4 | Side gunners | Gamepad: press **A / Start** to join. Right stick aims, **RT** fires. |
| Anyone | | `Enter` launch · `Esc` pause · `1/2/3` pick an upgrade (gamepad X/Y/B) · `R` restart |

**Dev / demo keys:** `F1` AI debug view (FSM state labels, A* paths, blocked grid) · `F2` autopilot demo (drives and shoots by itself, handy for recording) · `F3` skip to the next boss wave · `F4` god mode.

## What's in the alpha

### Core loop
- A shared rover with arcade driving, a boost on cooldown, and independent turret aiming for every gunner.
- Survival loop: waves → clear → hull patch → next wave. When the rover is destroyed, the game-over screen shows waves reached, time, kills and score.
- Title screen with a tutorial, HUD (wave, aliens left, time, kills, hull, boost, crew list, boss bar), pause, and game-over/restart.

### NPC AI
Every enemy uses three AI layers:

- A **finite state machine** (`AI/StateMachine.cs`).
- **Steering behaviours** (`AI/SteeringAgent.cs`): seek, flee, arrive, pursuit/prediction, orbit, wander, separation, whisker obstacle avoidance and hard collision sliding.
- **A\* pathfinding** (`AI/GridPathfinder.cs`): an 8-connected grid baked from the rocks and ridges, with an octile heuristic, no corner cutting, and line-of-sight string pulling. Enemies steer straight at the rover when the way is clear and switch to A* when terrain blocks it.

| Enemy | FSM states | Behaviour |
| --- | --- | --- |
| **Crawler** | Spawn → Chase ⇄ Attack | Melee swarmer. Paths around rocks and spreads out to surround the rover. |
| **Spitter** | Spawn → Approach → Strafe ⇄ Charge, Retreat | Ranged. Orbits at a preferred range, telegraphs, then fires a leading shot (3-shot spread from wave 6). Backs off when the rover gets close. |
| **Dasher** | Spawn → Stalk → WindUp → Dash → Recover | Fast flanker. Predicts the rover's position, shows a red charge lane, then dashes. |
| **Crater Brute** (boss, every 5th wave) | Intro → Pursue → Barrage / Summon / ChargeWindUp → Charge → Recover; Enrage at 50% HP | Spiral bullet rings, summons minions, gap-closing charge. A rule-based choice in Pursue (distance, cooldowns, minion count) picks the next attack. Enrage makes every attack faster and denser. |
| **Void Titan** (boss, every 10th wave) | same as above | Bigger and tougher. Adds aimed volleys to every barrage. |

All idle enemies fall back to **Wander** if the rover is destroyed.

### Scripted events
- **Progression triggers:** a boss on every 5th wave and a harder boss on every 10th. Beating a boss or surviving a moon event offers **3 random upgrades**: damage, fire rate, hull, speed, multishot, pierce or repair.
- **Time trigger:** alien HP, damage and speed keep scaling with total run time, on top of per-wave scaling.
- **Game-mode events:**
  - *Asteroid Storm* (waves 3, 8, 13…): telegraphed meteors rain around the rover and damage everyone, aliens included.
  - *Blood Moon* (every 7th wave): red sky and a blood moon, with aliens +20% speed and +25% damage.
- **Environment:** a floating moon plateau with craters, boulders and ridges over a starfield with distant planets. The sky, sun and ground shift from calm blue to danger red as difficulty rises.

### Audio
Every sound effect is synthesized at runtime as a placeholder. To replace one with the group's DIY recording, drop the clip into `Assets/Resources/Audio/` using the same name: `shoot`, `enemy_shot`, `hit`, `hurt`, `explode`, `boss`, `dash`, `upgrade`, `alarm`, `meteor`, `spawn` or `bite`. Background music has not been added yet.

## Not in the alpha yet (planned for the final version)
- **Online multiplayer.** The alpha is local co-op on one machine (keyboard + mouse + gamepads). The crew/turret split is built so each seat can later be driven by a networked player, for example with Netcode for GameObjects.
- Final art and models, the group's own sound effects, and background music.
- Balancing past wave ~10, and more enemy and event types.

## Project layout
```
Assets/Scripts/
  AI/      StateMachine, SteeringAgent, GridPathfinder, EnemyBase + Crawler/Spitter/Dasher/Boss
  Combat/  Health, Projectile
  Core/    Visuals (primitive art), Fx, AudioManager (synth SFX), CameraRig, Obstacle
  Game/    GameManager (bootstrap + game states), WaveDirector, EventDirector (+ Meteor), UpgradeSystem, WorldBuilder, AutoTest
  Player/  Rover (driving + stats), Turret (gunner seats)
  UI/      HUD (menu, HUD, upgrade cards, AI debug overlay)
Assets/Editor/RovernautsSetup.cs   scene rebuild + Windows build menu items
```

## Automated smoke test
A built player can play itself and log AI state transitions:
```
Rovernauts.exe -autoplay -godmode -startWave 4 -aidebug -shots shots -quitAfter 90 -logFile run.log
```
`run.log` gets a status line every 5 s (wave, alive enemies by FSM state, hull, FPS) and a final count of every FSM transition seen.
