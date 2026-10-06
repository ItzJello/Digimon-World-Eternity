#!/usr/bin/env python3
"""Export the Time Stranger wiring that sits next to each walk map.

Every field has sibling geoms: <code>c is the walk and camera collision,
<code>link holds the start points, map exits, and invisible borders, and
<code>sun carries the sun. Visual meshes stay in <code>f. This writes
src/assets/maps/Collision/<code>c.glb and src/data/maps/<stem>_wiring.json.

The lobby map is left alone.
"""
from __future__ import annotations

import json
import math
import re
import struct
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/ts"))
sys.path.insert(0, "/home/jelly/Digimon-World-Eternity/tools/ts/.venv/lib/python3.12/site-packages")

from extract_map import DEFAULT_GAMEDATA  # noqa: E402
from export_overworld import _enable_field_layout as _field_layout  # noqa: E402

UNPACKED = Path("/home/jelly/Digimon-World-Eternity/private/reference/time-stranger/unpacked")
MAPS = ROOT / "src/assets/maps"
COLLISION = MAPS / "Collision"
DATA = ROOT / "src/data/maps"
LOBBY = "shinjukupark_waterfall"
# The patch archive carries newer copies of some fields.
ARCHIVES = [DEFAULT_GAMEDATA / "patch.dx11.mvgl", DEFAULT_GAMEDATA / "app_0.dx11.mvgl"]

# Walk maps that were exported under a readable name.
NAMED = {
    "higashishinjuku_visionplaza": "t0101",
    "kabukicho_theatersquare": "t0103",
    "akihabara_electrictown": "t0201",
    "tokyo_metropolitangovernment": "t0301",
    "shinjukupark_waterfall": "t0302",
}

EXIT = re.compile(r"^col_([tdh]\d{3,4})_(start_\d+)_\d+$", re.IGNORECASE)

_archives: list | None = None
_by_lower: list[dict[str, str]] | None = None


def _open_archives() -> None:
    global _archives, _by_lower
    if _archives is not None:
        return
    from dsts_extractor.mvgl import MvglArchive

    _archives = []
    _by_lower = []
    for path in ARCHIVES:
        if not path.is_file():
            continue
        arc = MvglArchive(path)
        arc.__enter__()
        _archives.append(arc)
        _by_lower.append({name.lower(): name for name in arc.files})


def _close_archives() -> None:
    global _archives, _by_lower
    for arc in _archives or []:
        arc.__exit__(None, None, None)
    _archives = None
    _by_lower = None


def fetch(stem: str) -> bool:
    """Pull <stem>.geom/.nlst/.anim into the unpacked folder. Patch wins."""
    _open_archives()
    assert _archives is not None and _by_lower is not None
    found = False
    for ext in (".geom", ".nlst", ".anim"):
        out = UNPACKED / f"{stem}{ext}"
        for arc, lower in zip(_archives, _by_lower):
            real = lower.get(f"{stem}{ext}".lower())
            if real is None:
                continue
            if out.is_file() and out.stat().st_size == arc.files[real].full_size:
                found = found or ext == ".geom"
                break
            arc.extract_file(real, out)
            found = found or ext == ".geom"
            break
    return found


def code_for(stem: str) -> str | None:
    low = stem.lower()
    if low in NAMED:
        return NAMED[low]
    if re.match(r"^[a-z]\d{4}f$", low):
        return low[:5]
    return None


def _gltf(path: Path) -> dict:
    data = path.read_bytes()
    off = 12
    while off + 8 <= len(data):
        length, ctype = struct.unpack_from("<II", data, off)
        chunk = data[off + 8 : off + 8 + length]
        off += 8 + length
        if ctype == 0x4E4F534A:
            return json.loads(chunk)
    raise SystemExit(f"{path.name} has no JSON chunk")


def export_glb(stem: str, out_dir: Path) -> Path | None:
    _field_layout()
    from dsts_extractor.export import export_chr

    out_dir.mkdir(parents=True, exist_ok=True)
    try:
        export_chr(stem, UNPACKED, name=stem, export_dir=out_dir, engine="python")
    except Exception as exc:
        print(f"  failed {stem}: {exc}")
        return None
    glb = out_dir / f"{stem}.glb"
    return glb if glb.is_file() else None


def nlst_rows(stem: str, kind: str) -> list[str]:
    path = UNPACKED / f"{stem}.nlst"
    if not path.is_file():
        return []
    names: list[str] = []
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        parts = [p.strip() for p in line.split(",")]
        if len(parts) >= 4 and parts[3] == kind:
            names.append(parts[0])
    return names


# --- small quaternion helpers (x, y, z, w) -------------------------------

def _qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (
        aw * bx + ax * bw + ay * bz - az * by,
        aw * by - ax * bz + ay * bw + az * bx,
        aw * bz + ax * by - ay * bx + az * bw,
        aw * bw - ax * bx - ay * by - az * bz,
    )


def _qrot(q, v):
    x, y, z, w = q
    vx, vy, vz = v
    tx = 2 * (y * vz - z * vy)
    ty = 2 * (z * vx - x * vz)
    tz = 2 * (x * vy - y * vx)
    return (
        vx + w * tx + (y * tz - z * ty),
        vy + w * ty + (z * tx - x * tz),
        vz + w * tz + (x * ty - y * tx),
    )


def _world_transforms(doc: dict) -> dict[int, tuple[tuple, tuple, tuple]]:
    nodes = doc.get("nodes", [])
    parent: dict[int, int] = {}
    for i, node in enumerate(nodes):
        for child in node.get("children", []) or []:
            parent.setdefault(child, i)
    cache: dict[int, tuple[tuple, tuple, tuple]] = {}

    def local(i: int):
        node = nodes[i]
        t = tuple(float(v) for v in node.get("translation", (0.0, 0.0, 0.0)))
        r = tuple(float(v) for v in node.get("rotation", (0.0, 0.0, 0.0, 1.0)))
        s = tuple(float(v) for v in node.get("scale", (1.0, 1.0, 1.0)))
        return t, r, s

    def world(i: int):
        if i in cache:
            return cache[i]
        t, r, s = local(i)
        if i in parent:
            pt, pr, ps = world(parent[i])
            scaled = (t[0] * ps[0], t[1] * ps[1], t[2] * ps[2])
            rotated = _qrot(pr, scaled)
            t = (pt[0] + rotated[0], pt[1] + rotated[1], pt[2] + rotated[2])
            r = _qmul(pr, r)
            s = (ps[0] * s[0], ps[1] * s[1], ps[2] * s[2])
        cache[i] = (t, r, s)
        return cache[i]

    return {i: world(i) for i in range(len(nodes))}


def _yaw(q) -> float:
    """Heading in degrees for a turn about Y. Identity faces +Z like the actors."""
    x, y, z, w = q
    return math.degrees(2.0 * math.atan2(y, w))


def _normalize_code(code: str) -> str:
    # One exit in the data is written t108 for t0108.
    if len(code) == 4:
        return code[0] + "0" + code[1:]
    return code.lower()


def read_link(code: str) -> dict | None:
    stem = f"{code}link"
    if not fetch(stem):
        return None
    with tempfile.TemporaryDirectory() as tmp:
        glb = export_glb(stem, Path(tmp))
        if glb is None:
            return None
        doc = _gltf(glb)
    nodes = doc.get("nodes", [])
    worlds = _world_transforms(doc)
    by_name: dict[str, int] = {}
    for i, node in enumerate(nodes):
        name = str(node.get("name") or "")
        if name and name not in by_name and "mesh" not in node:
            by_name[name] = i

    starts: dict[str, dict] = {}
    for name, i in by_name.items():
        if not name.lower().startswith("start_"):
            continue
        t, r, _ = worlds[i]
        starts[name.lower()] = {"at": [round(v, 3) for v in t], "yaw": round(_yaw(r), 2)}

    # Geometry rows and mesh rows share one order in the node list.
    geometry = nlst_rows(stem, "geometry")
    meshes = doc.get("meshes", [])
    accessors = doc.get("accessors", [])
    exits: list[dict] = []
    borders: list[dict] = []
    for index, name in enumerate(geometry):
        if index >= len(meshes) or name not in by_name:
            continue
        prims = meshes[index].get("primitives", [])
        if not prims:
            continue
        acc = accessors[prims[0]["attributes"]["POSITION"]]
        lo = acc.get("min", [0, 0, 0])
        hi = acc.get("max", [0, 0, 0])
        t, r, s = worlds[by_name[name]]
        centre = [(lo[k] + hi[k]) * 0.5 * s[k] for k in range(3)]
        centre = _qrot(r, centre)
        at = [round(t[k] + centre[k], 3) for k in range(3)]
        size = [round(max((hi[k] - lo[k]) * s[k], 0.0), 3) for k in range(3)]
        spot = {"at": at, "rotation": [round(v, 5) for v in r], "size": size}
        match = EXIT.match(name)
        if match:
            spot["to"] = _normalize_code(match.group(1))
            spot["start"] = match.group(2).lower()
            exits.append(spot)
        elif name.lower().startswith("mapborder"):
            borders.append(spot)
    return {"starts": starts, "exits": exits, "borders": borders}


def read_sun(code: str) -> dict | None:
    stem = f"{code}sun"
    if not fetch(stem):
        return None
    _field_layout()
    from dsts_extractor import geom as geom_mod

    try:
        g = geom_mod.read_geom(UNPACKED / f"{stem}.geom")
    except Exception as exc:
        print(f"  sun unreadable {stem}: {exc}")
        return None
    for bone in g.bones:
        if bone.name.lower().endswith("sun_mesh"):
            p = bone.bind_pose.position
            if any(abs(v) > 1.0 for v in p):
                return {"from": [round(float(v), 2) for v in p]}
    for bone in g.bones:
        if "dirlamp" in bone.name.lower():
            q = bone.bind_pose.rotation
            # Lamps shine down their local -Z. The sun sits the other way.
            d = _qrot(tuple(q), (0.0, 0.0, -1.0))
            return {"from": [round(-v * 300.0, 2) for v in d]}
    return None


def export_collision(code: str) -> dict | None:
    stem = f"{code}c"
    if not fetch(stem):
        return None
    glb = COLLISION / f"{stem}.glb"
    if not glb.is_file():
        if export_glb(stem, COLLISION) is None:
            return None
    return {"glb": stem, "meshes": nlst_rows(stem, "mesh")}


def write_wiring(stem: str) -> None:
    if stem.lower() == LOBBY:
        return
    code = code_for(stem)
    if code is None:
        print(f"{stem}: no field code")
        return
    out: dict = {"code": code}
    collision = export_collision(code)
    if collision:
        out["collision"] = collision
    link = read_link(code)
    if link:
        out.update(link)
    sun = read_sun(code)
    if sun:
        out["sun"] = sun
    if len(out) == 1:
        print(f"{stem}: no wiring in the archive")
        return
    dest = DATA / f"{stem}_wiring.json"
    dest.write_text(json.dumps(out, indent=1), encoding="utf-8")
    print(
        f"{stem}: collision {'yes' if collision else 'no'}, "
        f"{len(out.get('starts', {}))} starts, {len(out.get('exits', []))} exits, "
        f"{len(out.get('borders', []))} borders, sun {'yes' if sun else 'no'}"
    )


def main(args: list[str]) -> None:
    stems = args or [glb.stem for glb in sorted(MAPS.glob("*.glb"))]
    try:
        for stem in stems:
            write_wiring(stem)
    finally:
        _close_archives()


if __name__ == "__main__":
    main(sys.argv[1:])
