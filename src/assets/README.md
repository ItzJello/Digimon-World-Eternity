# Runtime art

The C# project compiles without anything in this folder except `boot/`, `ui/`, and `shaders/`, which stay in git. Godot loads the rest by absolute path at runtime. They are not `res://` resources.

Those packs are hosted outside the repo. Drop them here before launching. A missing file fails the scene that asks for it. The title screen still opens.

Godot must not import them. Each pack folder has a `.gdignore` so the editor skips it. Leave those files in place.

## Layout

```text
src/assets/
├── audio/                  title, lobby, and Arena tracks
├── effects/                skill and hit GLBs (ef_b_*.glb)
├── maps/                   walkable city and park GLBs
│   ├── Props/              barricades and park dressing
│   └── Textures/           color and emissive PNGs for the maps
├── models/
│   ├── characters/         tamers (pc001*, pc002*) and NPC GLBs
│   │   └── preview/        short idle copies used by character creation
│   ├── digimon/            one <slug>.glb per Digimon or boss
│   └── eyes/               optional <slug>.png iris sheets for the practice roster
├── stages/                 Arena fields
│   ├── <stem>_sky.png      one sky per field (d0172b, d0178b, d0273b, d0374b, d0572b)
│   ├── kit/                floor and ring textures (floor_dark.png is required)
│   └── props/              rocks and dressing placed on the ring
├── boot/                   splash image (in git)
├── shaders/                (in git)
└── ui/                     menus, plates, skill icons (in git)
```

## Tracks

| File | Scene |
| --- | --- |
| `Digimon_World_Next_Order_OST_21_-_Digital_Grit_ne0_Version.wav` | Title |
| `Digimon World OST - File City (Day).wav` | Lobby |
| `Major_Battle.wav` | Arena match |

## What each scene opens

| Scene | Needs |
| --- | --- |
| Title, create character | `models/characters/` (`pc001a_city.glb`, `pc002*.glb`, and `preview/` when present) |
| Lobby | `maps/ShinjukuPark_Waterfall.glb`, `maps/Props/`, `maps/Textures/`, the tamer GLB, the partner GLB |
| City plaza | `maps/HigashiShinjuku_VisionPlaza.glb`, `maps/Textures/`, `pc001a_city.glb`, `kuga.glb`, the partner GLB |
| Arena match | `stages/` (kit, sky, props), `models/digimon/<slug>.glb` for both fighters, `effects/` for the techniques they use |
| Model viewer | `models/digimon/` and `models/characters/` |

Partner slugs the practice roster asks for: `agumon`, `gabumon`, `patamon`, `biyomon`, `tentomon`, `palmon`, `gomamon`, `elecmon`, `betamon`, `gazimon`. Each is `models/digimon/<slug>.glb`. A matching `models/eyes/<slug>.png` replaces the iris packed in the GLB when the file is there.

Unpacked Time Stranger archives stay under `private/reference/time-stranger/`. The export scripts write finished fields, props, and effects straight into this folder.
