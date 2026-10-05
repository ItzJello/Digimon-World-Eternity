# Repository layout

```text
Digimon World Eternity/
├── src/            # The client (Godot 4.7 .NET, C#)
│   ├── core/         #   Session autoload, repo paths, loaders
│   ├── gameplay/     #   Combat sim, Digimon data, Arena mode, overworld, character
│   ├── presentation/ #   Cameras, animation, model dressing, VFX, UI
│   ├── scenes/       #   boot, menus, overworld, arena, debug
│   ├── data/         #   Map paint and prop placement JSON
│   └── assets/       #   Boot, UI, shaders (in git). Models, maps, audio (gitignored)
├── private/          # Local test data (not for a public remote)
│   ├── reference/    #   Time Stranger unpack and intermediate GLBs
│   └── disc/         #   optional USA dump; the client does not read it
├── tools/ts/         # Time Stranger unpack and GLB export
├── vendor/dw_decomp/ # DW1 decomp, read-only formula reference
├── docs/             # Asset pipeline and this layout
├── README.md
└── AGENTS.md
```

## Client

Open `src/project.godot`. The title scene is `scenes/menus/start.tscn`.

The folder names the subsystem. Three kinds of code:

| Kind | Where | What lives there |
| --- | --- | --- |
| Systems | `src/gameplay/` | Rules and flow. The combat sim has no scene nodes. |
| Presentation | `src/presentation/` | Cameras, clips, eyes, effects, HUD. Reads the sim; never decides a hit. |
| Scenes | `src/scenes/` | Thin `.tscn` roots that point at a gameplay or UI script. |

| Path | Role |
| --- | --- |
| `src/core/` | `GameSession` autoload, `RepoPaths`, GLB and WAV loaders |
| `src/gameplay/combat/` | `BattleSim`, `Combatant`, `BattleCommand`, rules. Subfolders: `movement/`, `attacks/`, `ai/` |
| `src/gameplay/digimon/` | Practice kits, partner roster, partner spawn |
| `src/gameplay/arena/` | Arena mode: gate, versus card, bracket, fields, `ArenaBout` (sim → bodies), `ArenaMatch` (scene root) |
| `src/gameplay/overworld/` | `lobby/`, `city/`, `player/` — walkable lobby, partner follow |
| `src/gameplay/character/` | Tamer bodies and saved characters |
| `src/presentation/camera/` | Battle, orbit, and top-down cameras |
| `src/presentation/animation/` | `ActorVisual` clip player, `ClipLegend` names |
| `src/presentation/models/` | Eyes, lighting shaders on loaded GLBs |
| `src/presentation/vfx/` | Skill effects, damage numbers, skill callouts |
| `src/presentation/ui/` | Chrome, menus, battle HUD, dialogue, chat, model viewer |

The Arena bout and the city walk stay separate. Battle position is a command, not WASD.

### Combat sim

`gameplay/combat/` runs without Godot nodes so a server or a story battle can tick it:

```text
BattleCommand → BattleSim.Tick → TechniquePlanner / BattleSteering / DamageRules → Combatant state
```

`ArenaBout` binds two `Combatant`s to `ActorVisual` bodies and turns state changes into clips and effects. `CommandAi` is the computer trainer; a human or a remote player calls `SetCommand` instead.

## Original disc

| Question | Answer |
| --- | --- |
| Keep a retail `.bin` / `.cue` locally? | Optional, under `private/disc/`. |
| Does the client load it? | No. |
| Patch or inject into that `.bin`? | No. |
| Public redistribution of game data? | No. |
