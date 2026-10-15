# Juego de Peleas BH6: 2.5D fighting game combat core

A Street Fighter / Tekken style 2.5D fighter made in Unity 6. It is a solo gameplay-programming project:
the art is placeholder on purpose, and the work is in the combat underneath. That means a state machine,
frame data, an input buffer with motion inputs, combos and juggles, hit resolution, corner pushback
and hitstop.

![Training room](Media/training-room.png)

<!-- TODO: replace with a 20–30 s GIF of a combo (launcher → jump cancel → air chain) with the current HUD. -->

| | |
|---|---|
| **Role** | Solo project: gameplay programming |
| **Engine** | Unity 6 (6000.3.16f1), URP, New Input System, PhysX `CharacterController` |
| **Language** | C# |
| **Status** | Playable prototype: local 1v1 in a training room, two robot fighters |

<!-- TODO: add credits for the robot models and animation clips. -->

## Controls

| Action | Player 1 (keyboard) | Player 2 (keyboard) | Gamepad |
|---|---|---|---|
| Move / jump / crouch | WASD | Arrow keys | Left stick / D-pad |
| Left punch (LP) | T | Numpad 7 | West (X / □) |
| Right punch (RP) | Y | Numpad 8 | North (Y / △) |
| Left kick (LK) | G | Numpad 4 | South (A / ✕) |
| Right kick (RK) | H | Numpad 5 | East (B / ○) |
| Super Art (full meter) | U | Numpad 9 | Right trigger |

Motion inputs are written for a fighter facing right (numpad notation):

| Move | Input |
|---|---|
| Hadouken | `236` + LP (quarter circle forward) |
| Shoryuken | `623` + RP (dragon punch) |
| Overhead | `6` + RP |
| Launcher | `6` + RK |
| Anti-air | `3` + RK |
| Dash | LK + RK together |
| Super Art | `236236` + LP, or the Super Art button, with a full meter |

Pause with **Esc** or **Start**. In the training room, **M** (or R3) fills the meter and **Backspace** (or
Select) restores health. DirectInput arcade sticks are supported as well.

## What to look at in the code

All gameplay code lives in [`Assets/_Project/Scripts`](Assets/_Project/Scripts).

- **Combat state machine.** Each fighter has 17 states (`Idle`, `Walk`, `Jump`, `Crouch`, `Dash`,
  `AirDash`, `Attack`, `CrouchAttack`, `AirAttack`, `SpecialAttack`, `Shoryuken`, `SuperArt`, `Hitstun`,
  `Blockstun`, `AirborneHurt`, `Knockdown`, `Death`). They are created once in `RobotController` and
  re-entered through typed entry points (`EnterAttack`, `EnterHitstun`…), so a chain cancel is just
  `Attack → Attack` and nothing is allocated on transitions.
  [`FSM/`](Assets/_Project/Scripts/FSM) ·
  [`RobotController.cs`](Assets/_Project/Scripts/Controllers/RobotController.cs)
- **Data-driven attacks and rules.** Every move is an `AttackData` asset (startup / active / recovery,
  hitbox, cancel windows, hit reaction). A single `CombatRules` asset holds the global tuning: damage
  proration, hitstun decay, juggle gravity and relaunch decay, bounce limits and hitstop tiers.
  [`Combat/Data/`](Assets/_Project/Scripts/Combat/Data) · [`Attacks/`](Assets/_Project/Attacks)
- **Combos without infinites.** Normals chain upwards on a gatling ladder (light → medium → heavy →
  launcher) plus some authored target combos. Juggles get heavier and relaunch lower with every hit, each
  combo gets one wall bounce and one ground bounce, and every juggle ends in a hard knockdown with
  wake-up invulnerability.
- **Input buffer.** Motion inputs (`236`, `623`, `236236`) with lenient shortcuts. A button pressed
  during startup, recovery or hitstun is held and comes out on the first frame the fighter can act, and
  a two-deep queue keeps fast strings in order.
  [`InputBuffer.cs`](Assets/_Project/Scripts/Controllers/InputBuffer.cs)
- **Hit resolution.** Hitboxes are kinematic triggers reshaped from each attack's data. Guard is decided
  by the defender's current state, not by the stick: a fighter in hitstun cannot block the next hit of a
  combo. Overheads beat a crouching guard, lows beat a standing guard, and counter hits get stronger
  reactions.
  [`Hurtbox.cs`](Assets/_Project/Scripts/Combat/Hurtbox.cs) ·
  [`Hitbox.cs`](Assets/_Project/Scripts/Combat/Hitbox.cs)
- **Corner pushback.** `PushbackSolver` (a struct) moves the defender and checks how far it really
  travelled. If the wall stopped it, the defender stays pinned and the rest of the push goes to the
  attacker.
  [`PushbackSolver.cs`](Assets/_Project/Scripts/Combat/PushbackSolver.cs)
- **Hitstop per fighter.** Each fighter freezes on its own unscaled timer (the defender longer than the
  attacker), while the camera, effects, HUD and projectiles keep running. `Time.timeScale` is only used
  for the knockout slow-motion and pause. Big hits also add camera punch and shake and duck the music
  through mixer snapshots.
- **Pooling.** Projectiles and hitsparks come from pools created at startup, and animator parameter hashes
  are cached.

## Running it

1. Open the project with **Unity 6000.3.16f1**.
2. Open `Assets/_Project/Scenes/TrainingRoom.unity` and press Play.

## Known limitations and next steps

- Several attacks still reuse placeholder animation clips, and there is only one stage.
- The fighter's hurt volume is the `CharacterController` capsule. Next: separate limb hurtboxes.
- The state machine ticks in `Update`. Moving it to the fixed step would be the first step towards
  rollback netcode.

---

**Víctor Cárdenas** · Gameplay Programmer · [GitHub](https://github.com/Vixtorbir) ·
[LinkedIn](https://www.linkedin.com/in/victor-cardenas-gamedev/)
