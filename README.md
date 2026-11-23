# Rovernauts

Rovernauts is a 3D lunar survival game. Drive a rover, aim its turret independently, and fend off alien waves. Play solo or share the rover in two-player keyboard co-op.

**Gameplay video (30-60 s):** [Rovernauts Alpha Demo](https://www.youtube.com/watch?v=GKxgjDqZYaE)

**AI Usage Disclaimer:**
This project was worked on in accordance with the CISC 486 course academic integrity guidelines which stated level 4 AI usage permitted. Generative AI tools (Anthropic Claude - approved by Queen's University for ideation, brainstorming, and first drafts) were utilized for the following developmental tasks:
- **Debugging:** Identifying and resolving syntax and runtime errors. Helping to fix issues we ran into.
- **Logical Arrangement:** Reorganizing existing code structure for better readability and optimization.
- **Ideation:** Placeholder models and brainstorming of potential pathways.

## Launch

- **Windows build:** Extract the entire archive, then open `Rovernauts.exe`. Keep the executable with its data folder and companion files.
- **Unity project:** Add this repository in Unity Hub with Unity **6000.6.7f1**. Open `Assets/Scenes/Main.unity` and press Play. To make a fresh Windows build, select **Rovernauts → Build Windows Player**. The build goes to `Builds/Windows/Rovernauts.exe`.

## Missions

- **Crater Expedition** takes you through three encounters. Survive the asteroid storm on wave 3, choose a rover upgrade, clear the mixed swarm, and defeat the Crater Brute and its remaining minions on wave 5.
- **Endless Survival** starts at wave 1. Clear each wave for a partial hull repair. Moon events and bosses earn a choice of three upgrades. The Crater Brute appears every fifth wave, with a Void Titan every tenth. The Blood Moon speeds up aliens and increases their damage, and longer runs increase enemy strength. The run lasts until the shared hull is destroyed.

The radar shows the rover in cyan and hostiles in red. Crawlers swarm the hull, Spitters circle and shoot from range, and Dashers mark their charge lanes. Stay outside the red asteroid markers before impact. Bosses alternate between barrages, charges, and reinforcements, and enrage at half hull.

## Controls

These are the defaults. Press **K** on the title or pause screen to rebind them (see below).

| Action | Input |
| --- | --- |
| Steer, accelerate, reverse | WASD |
| Boost | Left Shift or Right Shift. In local co-op, only Left Shift. Boost goes backwards while you hold reverse. |
| Aim the turret | Mouse. The arrow keys also aim (8 directions) and override the mouse while held. |
| Fire the turret | Hold Left Click. Space also fires in solo and LAN, and Right Ctrl in local co-op. |
| Rebind controls | K on the title or pause screen |
| Toggle local co-op | C on the title screen |
| Open the LAN menu | L on the title screen |
| Start Endless Survival | Enter |
| Start Crater Expedition | E |
| Watch the expedition demo | F2 on the title screen (solo or local co-op) |
| Take control from autopilot | F2 during play (solo or local co-op) |
| Pause or resume | Esc, also on the upgrade screen |
| Install an upgrade | 1 / 2 / 3 |
| Return to title | R when paused, or R / Enter after a run |
| Toggle music | M on the title or pause screen |
| Quit | Q on the title screen |

### Rebinding

The Controls screen has two sets of bindings: **Solo / LAN** and **Local co-op**. Press **Tab** to switch between them. Every action has a primary and an alternate input, and either one can be a key or a mouse button.

- **Up / Down** picks an action and **Left / Right** picks the primary or alternate slot. **Enter** waits for the new key or mouse button.
- **Delete** clears a slot and **Backspace** restores the defaults for the set you are viewing. **Esc** saves and closes.
- **Aim with** switches the gunner between the mouse and the aim keys only.
- **Steering in reverse** chooses **Tank** (the rover turns toward the key you press) or **Car** (while reversing, the back of the rover swings toward the key).
- A key can only do one thing per set. If you bind a key that another action already uses, that action loses it, and the screen tells you which one.
- Esc and F1 to F12 are reserved and can't be bound. The menu keys always work, so a bad binding can't lock you out.

Bindings are saved between sessions. The `-check...` test commands always use the defaults and never change your saved bindings.

## The endless moon

The map has no edge. The surface repeats seamlessly in every direction, and the rover wraps around to the other side without a visible jump. Aliens, shots, and meteors always stay on whichever repeat of the map is closest to the rover, so the swarm is never more than half a map away. If you drive off in a straight line, the aliens behind you come around in front of you, so running away doesn't work. Waves spawn on a ring around the rover.

## Damage feedback

Taking a hit shakes the camera and flashes the screen edges red, more strongly for bigger hits. Below 30% hull, the edges pulse red until you repair or die.

The title and pause screens include a music toggle. The score adds a combat layer during encounters. The moon, enemies, rover meshes, and sound are generated by the project.

## Two-player co-op

- **One keyboard:** Press **C** at the title screen and launch either mission. Player 1 drives with **WASD** and boosts with **Left Shift**. Player 2 aims with the **mouse** and fires with **Left Click**, or uses the **arrow keys** and **Right Ctrl**. Sharing the mouse also means fewer keys held at once, which helps keyboards that can't register many keys together. Both players can be rebound in the **Local co-op** set.
- **Two PCs on the same LAN:** Run the same build on both PCs. The driver presses **L**, then **H**, and shares one of the local IPv4 addresses displayed on the host screen. The gunner presses **L**, then **J**, types that address, and presses **Enter**. Once the gunner connects, the driver starts **Endless Survival** with **Enter** or **Crater Expedition** with **E**. The gunner aims with the **mouse** and fires with **Left Click**, or uses the **arrow keys** and **Space**. Each player uses their own PC's **Solo / LAN** bindings. Both PCs need this version, because the gunner now also sends a mouse-aim angle.
- The driver controls pause and upgrades in LAN games. The gunner can leave with **F8**. Losing the connection during a run returns the remaining player to the title.
- LAN uses TCP port **48600** and accepts a peer on the host's private IPv4 subnet. If Windows Firewall blocks the host, allow Rovernauts on **Private** networks with **Local Subnet** scope. Internet co-op is outside this build.

## Demonstration and verification

- **F1** toggles the AI overlay, including FSM states and A* routes.
- **F2** starts the expedition demo from the title screen and toggles autopilot during a run, in solo or local co-op. The title-screen demo drives, shoots, and installs the first upgrade automatically.
- **F3** jumps to the next boss in Endless Survival. **F4** toggles invulnerability for debugging.

From the built player's directory, these commands run the control smoke test, the full expedition, and an Endless Survival check that begins at the Blood Moon.

```powershell
.\Rovernauts.exe -checkControls -logFile controls.log
.\Rovernauts.exe -autoplay -expedition -quitAfter 120 -logFile expedition.log
.\Rovernauts.exe -autoplay -startWave 7 -godmode -quitAfter 180 -logFile endless.log
```

Add `-shots screenshots` to capture frames from a visible test window. The control test uses virtual devices to exercise the same input path as the game. Run `-checkCoopLocal` for shared-keyboard input and `-checkLanSnapshot` to verify LAN state replication. Use `-checkLanHost` and `-checkLanGuest <host IPv4>` in separate processes to verify a LAN connection.
