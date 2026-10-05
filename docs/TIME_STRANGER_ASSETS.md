# Time Stranger asset pipeline

Private-testing visual source for Eternity Digimon. **Gameplay stays DW1 Arena.**
Steam files are never modified. Unpacked archives stay under
`private/reference/` (git-ignored). Finished GLBs the client loads go in
`src/assets/` (also git-ignored; see `src/assets/README.md`).

## Locations

| What | Path |
| --- | --- |
| Unpacked archives | `private/reference/time-stranger/unpacked/` |
| Intermediate GLBs (`export.sh`) | `private/reference/time-stranger/glb/` |
| What the Godot client loads | `src/assets/` (`models/`, `maps/`, `stages/`, `effects/`, `audio/`) |
| Steam install (read-only) | `C:\Program Files (x86)\Steam\steamapps\common\Digimon Story Time Stranger` |
| Tool venv | `tools/ts/.venv` |

Steam appid `1984270`, build `23514637` (as of 2026-10-03). Archives live in
`gamedata/`: `app_0.dx11.cpk` (CRI), `app_0.dx11.mvgl` (`MDB1` / Media.Vision),
plus `patch.*` and `*_text*.mvgl`. Models come from the `.mvgl` files.

## Tooling

- **dsts-extractor 0.8.7** (MIT, PyPI). Python ≥ 3.12. No Blender for GLB.
- Deps (venv only): click, numpy, Pillow, texture2ddecoder, platformdirs, lz4.
- Unpack writes ~14.5 GB (model subset). Full archive is ~41 GB — we do **not**
  use `--full`.

```bash
# first time (or after a game/tool update)
./tools/ts/setup.sh

# export one Digimon (repeatable)
./tools/ts/export.sh agumon
```

Override the Steam gamedata path with `DSTS_GAMEDATA=...` if the install is
not under the default WSL `/mnt/c/Program Files (x86)/Steam/...` location.

List names: `tools/ts/.venv/bin/dsts list agu --filter=digimon`

## First extract (Agumon)

| Field | Value |
| --- | --- |
| Slug / chr | `agumon` / `chr050` |
| GLB | `private/reference/time-stranger/glb/agumon.glb` (~3.0 MB) |
| Bones | 25 (humanoid-ish: spine, arms, legs, tail, jaw) |
| Meshes | body + overlays + eyes + outline (6) |
| Textures | albedo / mask / normal / spec / eye / env cubemap / CLUT (18 PNGs in GLB) |
| Animations | 32 clips (`chr050`, `ba01/02`, `bd01–03`, `bf01`, `bg01/02`, `bn01/02`, `br01`, `bs01`, `bv01`, `e00x`, `fe0x`, `fq01/02`, …) |

Confirmed clip names live in `src/presentation/animation/ClipLegend.cs`. Do not guess from the prefix: `bd02` is knockdown, `bd03` is get up, and `bf01` is an attack, not a faint.

## How the client uses this

```text
Steam .mvgl  --dsts setup-->   time-stranger/unpacked/
             --dsts export-->  time-stranger/glb/     (intermediate)
             copy / export --> src/assets/
                               models/digimon/  models/characters/  maps/  stages/  effects/
```

The Godot client reads `src/assets/` only. Digimon and bosses go in `models/digimon/`. Human NPCs go in `models/characters/`.

```bash
./tools/ts/export.sh agumon gabumon
```

`export.sh` writes the intermediate GLB under `private/reference/time-stranger/glb/`. The bout, lobby, and model viewer load the copies under `src/assets/`.

## Add another Digimon

```bash
./tools/ts/export.sh gabumon   # chr151
./tools/ts/export.sh greymon   # chr326
./tools/ts/export.sh garurumon # chr012
```

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| `gamedata not found` | Set `DSTS_GAMEDATA` to the folder with `app_0.dx11.mvgl` |
| `already has a setup manifest` | Already unpacked. Use `./tools/ts/setup.sh --force` only to redo |
| `export` looks under `~/.local/share/dsts-extractor/game` | Always pass `--data-root` (the scripts do this) |
| Blender warning | Ignore for GLB. FBX is optional and unused |
| Cel shading looks flat | DSTS shaders are **not** in the GLB. Use albedo + a simple toon later |

## Do not commit

`private/reference/**`, `src/assets/{audio,effects,maps,models,stages}/`,
`tools/ts/.venv/**`, Steam files, CPK/MVGL, unpacked geoms, exported GLBs.
