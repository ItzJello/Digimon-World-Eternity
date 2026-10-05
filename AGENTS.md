# Digimon World : Eternity

## Purpose

**Digimon World Eternity** is a **DW1-lite** fan client. The product loop is:

```text
City lobby → Arena match → battle → city lobby
```

Arena is the heart of the product. The city is the walkable lobby around it, not a full raising sim or island MMO.

The client is the **Godot 4.7 .NET** project in [`src/`](src/). [README.md](README.md) is the scope charter. Visuals are Time Stranger GLBs under `src/assets/` (gitignored; see `src/assets/README.md`). Combat rules follow DW1 Arena: commands, distance, and the six stats.

## Product constraints

1. **All-in-one client** — Run the Godot project. Do not design around pointing at a `.cue` every launch.
2. **Windows 11 first** — Godot .NET on Windows. Linux is for builds and preview.
3. **Arena is its own system** — Do not reuse the lobby camera or walk code for battles.
4. **Commands, not a stick** — In Arena the player picks `ATTACK` / `MODERATE` / `DISTANCE` / `DEFEND`. The Digimon AI owns approach, retreat, technique choice, and timing. **Do not drive battle position with WASD.**
5. **Simulation vs presentation** — The sim owns position, hits, damage, and results (`src/gameplay/combat/BattleSim.cs`, no scene nodes). `src/gameplay/arena/ArenaBout.cs` binds it to bodies. Camera, VFX, and animation stay in `src/presentation/`. A future server takes the sim. Sync the simulation, not the camera.
6. **Language** — Gameplay code is **C#** under `src/core/`, `src/gameplay/`, and `src/presentation/`. The folder names the subsystem (see `docs/LAYOUT.md`). Do not start a second client.
7. **Private testing only** — Disc images, extracts, and GLBs may live under `private/`. Do not push them publicly unless the owner asks.
8. **No injection into original `.bin` files** — `vendor/dw_decomp/` is read-only reference for DW1 formulas. Do not patch a retail disc.
9. **Time Stranger is the art source** — Export with `tools/ts/`. Do not download new game archives or tools without asking first and getting an explicit yes twice.

## Arena design

### Mental model

```text
PLAYER INPUT → battle command → Digimon AI → move / tech / defend → result
```

### Simulation plane

- Gameplay motion is **X/Z**. `Y` is the floor.
- **Distance** decides whether a technique can connect. Melee reach is shorter than a ranged shot.
- The arena has a hard boundary. Fighters clamp inside it.
- A hit applies when the swing connects and the distance check passes. The effect GLB is cosmetic and can still be in flight.
- A same-frame trade lets both swings land, then both flinch for the same length.
- A knockout plays **knockdown** (`bd02`) and holds the last pose. A non-lethal hit plays the light flinch (`bd01`). Misses float the same way damage does.

### Commands

| Command | Intent |
| --- | --- |
| `ATTACK` | Close and use techniques |
| `MODERATE` | Balance attack, space, and defense |
| `DISTANCE` | Keep range; melee only if they are already on top of each other |
| `DEFEND` | Guard. Hits deal less and the flinch is shorter |

### Stats (DW1 six — keep the names)

```text
HP, MP, Offense, Defense, Speed, Brains
```

- **HP / MP** — life and technique resource
- **Offense / Defense** — damage dealt and taken
- **Speed** — movement and responsiveness
- **Brains** — decision quality

The owner is writing how these numbers should change a bout. Until that plan is in, keep the practice kits in `DigimonKit.cs` and do not invent a second stat system. Technique power, MP, range, and element live on the technique.

**Out of Arena for now:** weight, discipline, happiness, fatigue, care mistakes, the raising schedule.

### Camera

Combatant-aware: midpoint, battle axis, separation, soft follow. Client-side only.

### Clip names

Shared motion codes live in `src/presentation/animation/ClipLegend.cs`. The model viewer shows the code and that name. Current corrections from playback:

| Code | Name |
| --- | --- |
| `bd01` | Light hit |
| `bd02` | Knockdown |
| `bd03` | Get up |
| `bf01` | Attack 3 |
| `bv01` | Victory |

Do not rename a code the owner has already confirmed.

## City lobby

- Walkable Time Stranger maps (park and plaza), partner follow, and an entry into the Arena.
- NPC bodies that have a field walk or run are candidates for character creation later. Do not wire them into the creator until asked.
- Camera-relative walk is correct **here only**.
- Presence sync is later, and it is a different session from the Arena bout.

## Agent rules

### Downloads and untrusted binaries

- Never download installers, zips, discs, toolchains, or third-party packages without asking first and getting an explicit yes twice.
- If approval was conditional and you cannot meet it, stop and ask again.
- Do not run untrusted game executables unless the owner clearly asks after that confirmation.

### Git and changes

- Only commit when asked. Do not push unless asked.
- No drive-by refactors, extra markdown, or unrelated files, except when the owner asks to update these charter docs.
- Commit subjects use the tags below. One tag. The text after the colon is why the commit exists, not a file list.

### Architecture

- Keep the Arena bout and the city walk in their own scripts.
- Prefer a small change in the existing C# bout over a new framework.
- Ask before large new directories or a new language.
- Asset export writes finished GLBs into `src/assets/`. Digimon and bosses go in `models/digimon/`. Human NPCs go in `models/characters/`. Unpacked archives stay in `private/reference/time-stranger/`.

### Communication

- Be direct and concise.
- Private testing data is allowed locally. Say so only if a public push or an untrusted download is about to happen.

## Commit messages

The tag says what kind of change this is. The rest of the line says why.

```text
tag: why this commit exists
```

| Tag | Use it when |
| --- | --- |
| `feat` | The player can do something they could not do before. |
| `fix` | Something broken is fixed. |
| `polish` | Something that already works gets better: feel, stats, AI, UI. |
| `refactor` | Code or files move, and behavior stays the same. |
| `docs` | Docs, notes, or a legend. No behavior change. |
| `asset` | Art, audio, UI chrome, or data the game loads. |
| `chore` | Tooling, the build, ignore rules, or project wiring. |

One tag per commit. Do not stack them, and do not invent a new tag.

Add a body only when the subject cannot carry the why. Keep it to a few sentences about the result.

```text
feat: the Godot client is what gets pushed

polish: melee spacing matches the reach the sim already uses

fix: both catchlights stay hidden so each iris shows

refactor: the bout sim no longer lives inside the arena scene
```

## Near-term focus

1. Arena feel: command spacing, flinch, knockdown, combat text, skill effects. Rules are in `src/gameplay/combat/`; the body binding is `src/gameplay/arena/ArenaBout.cs`.
2. **Stats plan** — the owner is defining how HP, MP, Offense, Defense, Speed, and Brains change combat. Wait for that before replacing the placeholder formulas.
3. Lobby → gate → battle → result → lobby, with the partner that was created on the title screen.
4. Finish exporting remaining Time Stranger Digimon, NPCs, and bosses into `src/assets/`.
5. Arena net only after the bout and the stat rules feel right.
6. No full raising sim before Arena feels right.
