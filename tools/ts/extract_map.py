#!/usr/bin/env python3
"""Pull a Time Stranger battle field (and its textures) out of the Steam MVGL.

Default stage is d0172b — the d01-area battle map listed in battle_field.mbe.
Writes into the existing unpacked tree so `dsts export` can see the files.
Steam is read-only.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UNPACKED = ROOT / "private/reference/time-stranger/unpacked"
DEFAULT_GAMEDATA = Path(
    "/mnt/c/Program Files (x86)/Steam/steamapps/common/"
    "Digimon Story Time Stranger/gamedata"
)


def _mvgl_path(gamedata: Path) -> Path:
    p = gamedata / "app_0.dx11.mvgl"
    if not p.is_file():
        sys.exit(f"gamedata not found: {p}")
    return p


def extract_stems(stems: list[str], gamedata: Path, dest: Path) -> list[str]:
    from dsts_extractor.mvgl import MvglArchive

    archive = _mvgl_path(gamedata)
    written: list[str] = []
    wanted: set[str] = set()
    extras = (".geom", ".nlst", ".anim", ".note")
    for stem in stems:
        for ext in extras:
            wanted.add(f"{stem}{ext}")

    print(f"opening {archive} …")
    with MvglArchive(archive) as arc:
        names = set(arc.files)
        for name in sorted(wanted):
            if name not in names:
                continue
            out = dest / name
            if out.is_file() and out.stat().st_size > 0:
                print(f"  keep {name}")
            else:
                print(f"  extract {name} ({arc.files[name].full_size} B)")
                arc.extract_file(name, out)
            written.append(name)

        tex: set[str] = set()
        # Field geoms do not always parse with the chr IBM layout. Pull
        # sibling/parent albedos by prefix (d0172b → d0172* + d0101*).
        for stem in stems:
            for img in names:
                if not img.startswith("images/"):
                    continue
                base = img.rsplit("/", 1)[-1].rsplit(".", 1)[0]
                if base.startswith(stem) or base.startswith(stem[:5]):
                    tex.add(base)
            if len(stem) >= 5 and stem[0] in "dthe" and stem[1:5].isdigit():
                parent = stem[:5]
                for img in names:
                    if not img.startswith("images/"):
                        continue
                    base = img.rsplit("/", 1)[-1].rsplit(".", 1)[0]
                    if base.startswith(parent) or base.startswith(parent[:3]):
                        tex.add(base)

        print(f"textures referenced/sibling: {len(tex)}")
        (dest / "images").mkdir(parents=True, exist_ok=True)
        n = 0
        for stem in sorted(tex):
            name = f"images/{stem}.img"
            if name not in names:
                continue
            out = dest / name
            if not out.is_file():
                arc.extract_file(name, out)
                n += 1
            written.append(name)
        print(f"extracted {n} new images")
    return written


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument(
        "stems",
        nargs="*",
        default=["d0172b", "d0172l", "d0172sun"],
        help="geom stems (default: d0172b battle field + lights + sun)",
    )
    ap.add_argument("--gamedata", type=Path, default=DEFAULT_GAMEDATA)
    ap.add_argument("--dest", type=Path, default=UNPACKED)
    args = ap.parse_args()
    extract_stems(args.stems, args.gamedata, args.dest)


if __name__ == "__main__":
    main()
