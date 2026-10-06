# Digimon World Eternity

A **standalone Digimon game** inspired by **Digimon World**. The product is **arena combat and PvP**: pick a command, the Digimon fights, and the bout is the point of the game. The city is the walkable lobby you pass through on the way into a match and back out again.

Fan project for **private testing only**. Digimon is © Bandai. Local dumps and extracted assets may live in this tree for development.

The game is a **Godot 4.7 .NET** app in [`src/`](src/). Combat follows the Digimon World arena: commands, spacing, and techniques. Current visuals are Digimon Story Time Stranger models. Shaders and art get their own pass later.

---

## Product loop

Arena matches are the loop. The lobby is how you reach one.

```text
        CITY LOBBY (walk, partner, queue)
                    │
                    ▼
              ARENA GATE
                    │
                    ▼
        ARENA BOUT (commands + AI, then PvP)
                    │
                    ▼
                 RESULT
                    │
                    ▼
              CITY LOBBY
```

The bout is its **own system**. The lobby camera and the battle camera do not share a control model. A future server syncs the **simulation**, not the camera.

## Goals

1. **Standalone game** — Run the Godot project. No `.cue` / `.bin`, no disc picker, no retail-disc boot.
2. **Arena combat and PvP** — This is the game. Command bouts first, then player versus player on that same sim.
3. **Windows 11 first** — Godot .NET editor and export. Linux is for builds and preview.
4. **City as the lobby** — Walk in, bring a partner, enter the Arena. A raising sim or island MMO is not the goal.

---

## How Arena works

This is the current bout, and it is what PvP will run. Combat is **not turn-based** and **not a fighting-game stick**. The player issues a **command**. The Digimon AI handles approach, retreat, technique choice, distance, and timing.

| Player does | Digimon AI does |
| --- | --- |
| Pick `ATTACK` / `MODERATE` / `DISTANCE` / `DEFEND` | Move on X/Z, keep range, use techniques, recover, reposition |

**WASD does not drive Digimon position in battle.** Walking is for the city lobby only.

**Distance is a combat variable.** Technique range and the current command decide when a swing can connect. The skill effect is a client visual. Damage is applied by the bout when the swing connects, not when the effect model arrives.

**Battle camera** follows the two combatants. It stays client-side.

### Digimon stats (DW1 six — keep the names)

| Stat | Arena role |
| --- | --- |
| **HP** | Life |
| **MP** | Technique resource |
| **Offense** | Damage dealt, with technique power |
| **Defense** | Damage taken |
| **Speed** | Movement / responsiveness |
| **Brains** | Decision quality |

How those six numbers change a bout is the next combat design. What they do **today** is below. Practice kits in `src/gameplay/digimon/DigimonKit.cs` are placeholders until that plan and real partner data exist.

Raising-only stats (weight, discipline, happiness, fatigue, care mistakes) stay **out of Arena** for now.

Techniques carry power, MP, range, element, and a Time Stranger effect clip. Element is stored and used to pick the effect. It does not change damage yet.

---

## Combat today

This is the sim in `src/gameplay/combat/` (`BattleSim`, `DamageRules`, `TechniquePlanner`, `BattleSteering`), so a stat plan can replace these numbers on purpose. `gameplay/arena/ArenaBout.cs` only binds it to bodies.

### Loop

Each fighter, every frame:

1. If HP is 0, play knockdown and stop. A swing that has not connected yet still gets to land.
2. Tick cooldowns and MP regen.
3. If they are charging, striking, recovering, or in hitstun, they do not walk.
4. Otherwise they keep their chosen technique until it connects (up to 3.2s), or pick a new one when the think timer expires, then walk toward that technique's range.

The opponent CPU picks a new command every 7–10s. Under 30% HP it often switches to Distance. It sometimes Defends if the player is charging.

### Choosing a technique

The basic is the 0 MP move. Anything with MP is a special.

- Within 1.25 m, and the command is not Distance: use the basic if it is ready. This stops them walking away to line up a shot.
- Else roll a special they can afford. Highest power wins. Chance: Attack 85%, Moderate 50%, Distance 30%, Defend never.
- Distance will not pick a melee special. It uses the basic only if the opponent is already inside 1.075 m.
- Defend does not pick a technique. It holds at 4.6 m and guards.

### Range

| | Meters |
| --- | --- |
| Melee connect | 1.075 |
| Melee preferred stand | 0.8 |
| Ranged connect | 6.0 |
| Ranged preferred stand | 4.7 |
| Closest two fighters may stand | 0.68 |
| Arena radius | 6.4 |

Attack likes to stand just outside melee (1.2 m) when it has no technique lined up, and at least 3.4 m for a ranged technique. Distance holds a ranged shot farther out (preferred + 1.15, capped under the 6 m reach) and rests at 5.2 m. Moderate rests at 3.4 m.

A ranged shot will not fire if they are more than 1.4 m inside their spacing. That is so Distance can open the gap first.

Walk speed is `2.7 + Speed × 0.008` m/s. Attack, while still far, adds 1.3. Defend moves at 40% speed. They stop inside a 0.14 m band around the desired range so a melee basic can actually connect.

### One technique

| Step | Melee | Ranged |
| --- | --- | --- |
| Charge (windup, rooted) | 0.35 s | 0.55 s |
| Strike (hit check on the first frame) | 0.18 s | 0.18 s |
| Recover, then 0.45 s before the next decision | 0.45 s | 0.70 s |
| Cooldown | 2.4 s | — |

A special's own cooldown is 8 s if melee and 11 s if ranged. Other specials are pushed to at least 6.5 s. MP is spent when the charge ends and the strike starts.

### Hit, miss, flinch

Accuracy is `78 + Brains / 12`, capped at 100, plus 8 for a ranged technique. Brains 100 is about 86%. A failed roll, or being outside 110% of range, is a miss. **Miss** floats over the target.

```text
damage = Power + (Offense − Defense) × Power / 500
```

Offense − Defense is clamped to ±500. Guard halves the result. Then it rolls 90–110% and the minimum is 1. A floating number shows the final amount. Guard is pale blue, 80+ is orange, anything else is gold.

On a practice kit this means Offense barely moves the number. Power 42 with Offense 130 vs Defense 85 is about 45 before the 90–110% roll.

A hit that does not KO plays the light-hit flinch and roots them: 0.28 s under 45 damage, 0.42 s from 45, 0.55 s from 75. Guard is 0.16 s and a shorter knockback (0.35 m vs 0.8 m). After hitstun they wait 0.4 s before choosing again.

If both are already in their strike, both hits land, neither is shoved until the swings finish, and both flinch for the longer of the two stun times.

HP at 0 plays knockdown (`bd02`) and locks the last pose. The winner loops victory (`bv01`). Both at 0 is a draw and both go down.

### MP

One point returns every 0.33 s, or every 0.16 s on Distance.

### What the stats do not do yet

Element, technique timing, and crits are not in the damage roll. Speed does not change windup, cooldown, or flinch. Brains does not change which command is legal. It only shortens the think pause (0.7 s down toward 0.4 s) and adds a little accuracy.

---

## Stack

| Layer | Choice |
| --- | --- |
| Game | Godot 4.7, Forward+, C# (`DigimonWorldEternity`) |
| Code | `src/core/`, `src/gameplay/`, `src/presentation/`; scenes in `src/scenes/` |
| Models, maps, effects | Time Stranger GLBs under `src/assets/` for now (gitignored; see `src/assets/README.md`) |
| PvP | Later, on the arena sim. Commands in, sim state out. Camera and VFX stay on the client. |

---

## Where the work is

| Area | Now |
| --- | --- |
| Arena | The focus. Command bout, flinch, knockdown, combat text, skill effects. Stat rules, then PvP on this sim. |
| Title | Create / load a tamer and enter a match |
| Lobby | The walkable way into the Arena. Partner follows. |
| Art | Time Stranger meshes for now. Shader and asset pass later. |
| PvP | Not started. The bout has to feel right first. |

---

## In scope / out of scope

**In**

- A standalone Godot game. No runtime disc load.
- Arena combat: commands, distance, techniques, flinch, and knockdown.
- PvP on that same bout, once the fight feels right.
- A lobby that exists to reach the Arena and return from it.
- The six Digimon World stat names, then a plan for how they change a bout.
- Current Time Stranger meshes until the art pass.

**Out**

- A raising sim, care schedule, or island MMO.
- Direct stick control of the Digimon in the Arena.
- Booting a retail disc, or injecting into an original `.bin`.
- Public redistribution of the project or of game data.

---

## Repository

| Path | Role |
| --- | --- |
| `src/` | The game. Main scene `scenes/menus/start.tscn`. Layout in [docs/LAYOUT.md](docs/LAYOUT.md). |
| `src/core/` | Session autoload, repo paths, loaders |
| `src/gameplay/combat/` | Battle sim: commands, steering, damage, AI. No scene nodes. |
| `src/gameplay/arena/` | Arena mode: gate, card, bracket, fields, bout binding |
| `src/gameplay/overworld/` | Walkable lobby and city, player, partner follow |
| `src/gameplay/digimon/`, `src/gameplay/character/` | Practice kits, rosters, tamer bodies |
| `src/presentation/` | Cameras, clips, eyes, VFX, HUD and menus |
| `src/assets/` | Runtime art. `boot/`, `ui/`, and `shaders/` are in git. Models, maps, stages, effects, and audio are not. |
| `tools/ts/` | Time Stranger unpack and GLB export |
| `docs/TIME_STRANGER_ASSETS.md` | Asset pipeline |
| `vendor/dw_decomp/` | DW1 decomp. Read for formulas. Not linked into the client. |

Agent guidance: [AGENTS.md](AGENTS.md).

## Run

Open `src/project.godot` in **Godot 4.7 with .NET**. Viewport is 1600×900. The title scene is `res://scenes/menus/start.tscn`.

On Linux, the C# build needs the editor's GodotSharp packages. Point a temporary `src/NuGet.Config` at `GodotSharp/Tools/nupkgs` inside the Godot 4.7 .NET install, build `src/DigimonWorldEternity.csproj`, then remove that file. Quit Godot fully before launching so it loads the new assembly.

Game models are read from `src/assets/`. That pack is not in git; [src/assets/README.md](src/assets/README.md) lists what a checkout needs. Export helpers live in `tools/ts/`. See [docs/TIME_STRANGER_ASSETS.md](docs/TIME_STRANGER_ASSETS.md).
