#!/usr/bin/env python3
"""Export Time Stranger walk maps and the collision config that matches them.

Godot drops empty instance points, and the texture pass hides shells that have
no color yet. The collision file names the solid meshes and the border props
so those two passes cannot open a hole the original map blocks.

The lobby map is left alone. Shinjuku Park already has its own barricades.
"""
from __future__ import annotations

import json
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/ts"))
sys.path.insert(0, "/home/jelly/Digimon-World-Eternity/tools/ts/.venv/lib/python3.12/site-packages")

from extract_map import DEFAULT_GAMEDATA, extract_stems  # noqa: E402

UNPACKED = Path("/home/jelly/Digimon-World-Eternity/private/reference/time-stranger/unpacked")
MAPS = ROOT / "src/assets/maps"
PROPS = MAPS / "Props"
DATA = ROOT / "src/data/maps"
LOBBY = "shinjukupark_waterfall"

# Visual cards. They are not the floor or a wall in Time Stranger.
SKIP = ("sky", "shadow", "outline", "fog", "cloud", "particle", "effect", "expression")
# Instance props that close a street. Trees, bikes, and lamps are not borders.
BORDER = (
    "barricade",
    "fence",
    "hedge",
    "railing",
    "gate",
    "curb",
    "concreteparts",
    "construction",
    "wall",
)

# First walk map of each region, plus the hub that was never exported.
# Battle bowls stay in stages/. The lobby is not in this list.
ZONE_MAPS = [
    "h0201f",
    "d0101f",
    "d0201f",
    "d0301f",
    "d0401f",
    "d0501f",
    "d0601f",
    "d0701f",
    "d0801f",
    "d0901f",
    "d1001f",
    "d1101f",
    "d1201f",
    "d1301f",
    "d1401f",
]


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


def _template(node_name: str) -> str:
    body = node_name[len("instance_") :]
    mesh, _, index = body.rpartition("_")
    if mesh and index.isdigit():
        return mesh
    return body


def _is_border(mesh: str) -> bool:
    low = mesh.lower()
    if "light" in low or "lamp" in low:
        return False
    return any(token in low for token in BORDER)


def _trs(node: dict) -> tuple[list[float], list[float], list[float]] | None:
    if "matrix" in node:
        m = node["matrix"]
        if len(m) != 16:
            return None
        # glTF matrices are column-major.
        translation = [float(m[12]), float(m[13]), float(m[14])]
        scale = [
            float((m[0] ** 2 + m[1] ** 2 + m[2] ** 2) ** 0.5) or 1.0,
            float((m[4] ** 2 + m[5] ** 2 + m[6] ** 2) ** 0.5) or 1.0,
            float((m[8] ** 2 + m[9] ** 2 + m[10] ** 2) ** 0.5) or 1.0,
        ]
        # Rotation is left as identity when a map bakes the turn into the matrix.
        # Those nodes are rare on these fields; the translation still blocks the gap.
        return translation, [0.0, 0.0, 0.0, 1.0], scale
    translation = [float(v) for v in node.get("translation", [0.0, 0.0, 0.0])]
    rotation = [float(v) for v in node.get("rotation", [0.0, 0.0, 0.0, 1.0])]
    scale = [float(v) for v in node.get("scale", [1.0, 1.0, 1.0])]
    return translation, rotation, scale


def write_dressing(glb: Path) -> set[str]:
    """Props Godot drops: trees, bikes, lamps, repeated buildings. Not the border set."""
    if glb.stem.lower() == LOBBY:
        return set()
    doc = _gltf(glb)
    spots: list[dict] = []
    props: set[str] = set()
    for node in doc.get("nodes", []):
        name = str(node.get("name") or "")
        if not name.startswith("instance_"):
            continue
        mesh = _template(name)
        if _is_border(mesh):
            continue
        placed = _trs(node)
        if placed is None:
            continue
        translation, rotation, scale = placed
        spots.append(
            {
                "mesh": mesh,
                "translation": translation,
                "rotation": rotation,
                "scale": scale,
            }
        )
        props.add(mesh)
    if not spots:
        return set()
    dest = DATA / f"{glb.stem}_dressing.json"
    dest.write_text(json.dumps({"spots": spots}), encoding="utf-8")
    print(f"{glb.stem}: {len(spots)} placed props, {len(props)} kinds")
    return props


def write_collision(glb: Path) -> set[str]:
    """Write <stem>_collision.json. Returns the border prop names it references."""
    if glb.stem.lower() == LOBBY:
        print(f"skip lobby {glb.name}")
        return set()
    doc = _gltf(glb)
    solid: list[str] = []
    borders: list[dict] = []
    seen: set[str] = set()
    props: set[str] = set()
    for node in doc.get("nodes", []):
        name = str(node.get("name") or "")
        if not name or name.startswith("instance_"):
            if name.startswith("instance_"):
                mesh = _template(name)
                if not _is_border(mesh):
                    continue
                placed = _trs(node)
                if placed is None:
                    continue
                translation, rotation, scale = placed
                borders.append(
                    {
                        "mesh": mesh,
                        "translation": translation,
                        "rotation": rotation,
                        "scale": scale,
                    }
                )
                props.add(mesh)
            continue
        if "mesh" not in node:
            continue
        low = name.lower()
        if any(token in low for token in SKIP):
            continue
        if name in seen:
            continue
        seen.add(name)
        solid.append(name)
    dest = DATA / f"{glb.stem}_collision.json"
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(
        json.dumps({"solid": solid, "borders": borders}, indent=2),
        encoding="utf-8",
    )
    print(f"{glb.stem}: {len(solid)} solid, {len(borders)} borders -> {dest.name}")
    return props


_FIELD_LAYOUT_DONE = False


def _enable_field_layout() -> None:
    """Make the character geom reader accept field files. Runtime only; the
    package on disk is not touched."""
    global _FIELD_LAYOUT_DONE
    import dsts_extractor.geom as geom_mod

    if not _FIELD_LAYOUT_DONE:
        _FIELD_LAYOUT_DONE = True
        import inspect

        # A few fields and props have one more material in the header than
        # the materials region. Pad with an empty material instead of failing.
        source = inspect.getsource(geom_mod)
        needle = (
            "    if decoded_clusters and len(decoded_clusters) != num_materials:\n"
            "        raise ValueError("
        )
        if needle in source:
            patched = source.replace(
                needle,
                "    if decoded_clusters and len(decoded_clusters) < num_materials:\n"
                "        decoded_clusters = tuple(decoded_clusters) + tuple(\n"
                "            ((), ()) for _ in range(num_materials - len(decoded_clusters)))\n"
                "    if decoded_clusters and len(decoded_clusters) != num_materials:\n"
                "        raise ValueError(",
            )
            exec(compile(patched, geom_mod.__file__, "exec"), geom_mod.__dict__)
            import dsts_extractor.export as export_mod

            if hasattr(export_mod, "read_geom"):
                export_mod.read_geom = geom_mod.read_geom
            import dsts_extractor.native_export as native_mod

            if hasattr(native_mod, "read_geom"):
                native_mod.read_geom = geom_mod.read_geom

        # Some fields carry a zero-scale node. Its matrix has no inverse, so
        # fall back to the pseudo-inverse instead of dropping the whole map.
        import numpy as np

        strict_inv = np.linalg.inv

        def tolerant_inv(m):
            try:
                return strict_inv(m)
            except np.linalg.LinAlgError:
                return np.linalg.pinv(m)

        np.linalg.inv = tolerant_inv

    # Field files omit the character engine-remap table. Without this, read_geom
    # rejects every walk map.
    geom_mod._ENGINE_REMAP_TABLE_SIZE = 0


def export_map(stem: str) -> None:
    if stem.lower() == LOBBY or stem.lower().startswith("shinjukupark"):
        print(f"skip lobby {stem}")
        return
    print(f"extract {stem}")
    extract_stems([stem], DEFAULT_GAMEDATA, UNPACKED)
    _enable_field_layout()
    from dsts_extractor.export import export_chr

    print(f"export {stem}")
    try:
        export_chr(stem, UNPACKED, name=stem, export_dir=MAPS, engine="python")
    except Exception as exc:
        print(f"  failed {stem}: {exc}")
        return
    write_collision(MAPS / f"{stem}.glb")


def export_props(names: set[str]) -> None:
    missing = sorted(name for name in names if not (PROPS / f"{name}.glb").is_file())
    if not missing:
        print("border props already exported")
        return
    from dsts_extractor.mvgl import MvglArchive

    archive = DEFAULT_GAMEDATA / "app_0.dx11.mvgl"
    wanted = {f"{name}.geom": name for name in missing}
    found: list[str] = []
    print(f"looking up {len(missing)} border props")
    with MvglArchive(archive) as arc:
        by_lower = {name.lower(): name for name in arc.files}
        for filename, stem in wanted.items():
            real = by_lower.get(filename.lower())
            if real is None:
                print(f"  no geom {stem}")
                continue
            dest = UNPACKED / f"{stem}.geom"
            nlst_name = by_lower.get(f"{stem}.nlst".lower())
            nlst = UNPACKED / f"{stem}.nlst"
            if not dest.is_file():
                arc.extract_file(real, dest)
            if nlst_name is not None and not nlst.is_file():
                arc.extract_file(nlst_name, nlst)
            found.append(stem)
    if found:
        extract_stems(found, DEFAULT_GAMEDATA, UNPACKED)
    _enable_field_layout()
    from dsts_extractor.export import export_chr

    PROPS.mkdir(parents=True, exist_ok=True)
    for stem in found:
        print(f"prop {stem}")
        try:
            export_chr(stem, UNPACKED, name=stem, export_dir=PROPS, engine="python")
        except Exception as exc:  # a missing texture should not drop the mesh
            print(f"  failed {stem}: {exc}")
            continue
        try:
            write_albedo(PROPS / f"{stem}.glb", PROP_ALBEDO / f"{stem}_albedo.json")
        except Exception as exc:
            print(f"  no color {stem}: {exc}")


TEXTURES = MAPS / "Textures"
PROP_ALBEDO = DATA / "props"
_IMAGE_INDEX: dict[str, Path] | None = None
_ARCHIVE = None
_ARCHIVE_IMAGES: dict[str, str] | None = None


def _image_index() -> dict[str, Path]:
    global _IMAGE_INDEX
    if _IMAGE_INDEX is not None:
        return _IMAGE_INDEX
    found: dict[str, Path] = {}
    folder = UNPACKED / "images"
    if folder.is_dir():
        for path in folder.glob("*.img"):
            found.setdefault(path.stem.lower(), path)
    _IMAGE_INDEX = found
    return found


def _fetch_image(stem: str) -> Path | None:
    """Color file from the unpacked tree, or the Steam archive if it was never pulled."""
    global _ARCHIVE, _ARCHIVE_IMAGES
    key = stem.lower()
    index = _image_index()
    hit = index.get(key)
    if hit is not None and hit.is_file():
        return hit
    if _ARCHIVE is False:
        return None
    from dsts_extractor.mvgl import MvglArchive

    if _ARCHIVE is None:
        print("opening the game archive for color maps that were not unpacked")
        _ARCHIVE = MvglArchive(DEFAULT_GAMEDATA / "app_0.dx11.mvgl")
        _ARCHIVE.__enter__()
        _ARCHIVE_IMAGES = {}
        for candidate in _ARCHIVE.files:
            if candidate.lower().startswith("images/") and candidate.lower().endswith(".img"):
                _ARCHIVE_IMAGES[Path(candidate).stem.lower()] = candidate
    arc = _ARCHIVE
    real = None if _ARCHIVE_IMAGES is None else _ARCHIVE_IMAGES.get(key)
    if real is None:
        return None
    dest = UNPACKED / "images" / f"{key}.img"
    dest.parent.mkdir(parents=True, exist_ok=True)
    if not dest.is_file():
        arc.extract_file(real, dest)
    index[key] = dest
    return dest


def _color_stem(slots: dict) -> str | None:
    """The color picture for a material. Normals and cubes are not it."""
    # Tiers: a color bound on the main slot, the sibling of the main normal
    # map, then overlay colors, then siblings of any other map. Overlays are
    # a detail layer, so they must not win over the base picture.
    direct: list[str] = []
    main: list[str] = []
    plain: list[str] = []
    emissive: list[str] = []
    overlay: list[str] = []
    siblings: list[str] = []
    for key, value in slots.items():
        low = str(value).lower()
        slot = str(key).lower()
        if not low or "cube" in low or "clut" in low or low == "missing":
            continue
        is_overlay = "overlay" in slot
        if low.endswith(("_c", "_ca")):
            (overlay if is_overlay else direct).append(low)
            continue
        # Screens, signs, and skies bind the picture under its own name.
        color_slot = "color" in slot or slot in ("lightpixelproj", "diffuse", "albedo")
        if color_slot or low.startswith("sky_") or "cloud" in low:
            if not low.endswith(("_n", "_na", "_rm", "_em")):
                plain.append(low)
        stem = low
        for suffix in ("_n", "_na", "_rm", "_em", "_e"):
            if stem.endswith(suffix):
                stem = stem[: -len(suffix)]
                break
        else:
            continue
        # An emissive map is the picture on a sign when no color sibling exists.
        if low.endswith("_em"):
            emissive.append(low)
        bucket = main if slot == "bumpiness" else (overlay if is_overlay else siblings)
        for suffix in ("_c", "_ca"):
            bucket.append(stem + suffix)
    seen: set[str] = set()
    for candidate in direct + main + plain + siblings + overlay + emissive:
        if candidate in seen:
            continue
        seen.add(candidate)
        if (TEXTURES / f"{candidate}.png").is_file() or _fetch_image(candidate) is not None:
            return candidate
    return None


def _ensure_png(stem: str) -> str | None:
    dest = TEXTURES / f"{stem}.png"
    if dest.is_file():
        return dest.name
    source = _fetch_image(stem)
    if source is None:
        return None
    from dsts_extractor.eyes import _decode_dds_dx10
    from export_field import _is_palette
    from PIL import Image

    image = _decode_dds_dx10(source)
    if image is None:
        try:
            image = Image.open(source)
        except OSError:
            return None
    if _is_palette(image):
        return None
    image = image.convert("RGBA")
    low = stem.lower()
    keep_alpha = any(token in low for token in ("fence", "leaf", "ivy", "hedge", "cloud", "tree", "shrub"))
    if not keep_alpha:
        image.putalpha(Image.new("L", image.size, 255))
    image.thumbnail((1024, 1024))
    TEXTURES.mkdir(parents=True, exist_ok=True)
    image.save(dest)
    return dest.name


def _albedo_map(glb: Path) -> dict[str, str]:
    doc = _gltf(glb)
    materials = doc.get("materials", [])
    meshes = doc.get("meshes", [])
    painted: dict[str, str] = {}
    cache: dict[int, str | None] = {}
    for node in doc.get("nodes", []):
        name = str(node.get("name") or "")
        if "mesh" not in node or not name or name.startswith("instance_"):
            continue
        mesh = meshes[node["mesh"]]
        for prim in mesh.get("primitives", []):
            index = prim.get("material")
            if not isinstance(index, int) or index >= len(materials):
                continue
            if index not in cache:
                slots = ((materials[index].get("extras") or {}).get("dsts") or {}).get("texture_slots") or {}
                color = _color_stem(slots) if slots else None
                cache[index] = _ensure_png(color) if color else None
            png = cache[index]
            if not png:
                continue
            painted[name] = png
            material_name = str(materials[index].get("name") or "")
            if material_name:
                painted[material_name] = png
    return painted


def write_albedo(glb: Path, dest: Path) -> None:
    if glb.stem.lower() == LOBBY:
        print(f"skip lobby {glb.name}")
        return
    painted = _albedo_map(glb)
    if not painted:
        print(f"{glb.stem}: no color maps")
        return
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(json.dumps(painted, indent=2), encoding="utf-8")
    print(f"{glb.stem}: {len(painted)} textured -> {dest.name}")


def albedo_for_existing() -> None:
    for glb in sorted(MAPS.glob("*.glb")):
        write_albedo(glb, DATA / f"{glb.stem}_albedo.json")
    if PROPS.is_dir():
        for glb in sorted(PROPS.glob("*.glb")):
            write_albedo(glb, PROP_ALBEDO / f"{glb.stem}_albedo.json")
    global _ARCHIVE
    if _ARCHIVE not in (None, False):
        _ARCHIVE.__exit__(None, None, None)
        _ARCHIVE = None


def collision_for_existing() -> set[str]:
    props: set[str] = set()
    for glb in sorted(MAPS.glob("*.glb")):
        props |= write_collision(glb)
    return props


def main() -> None:
    args = sys.argv[1:]
    if args == ["--dressing"]:
        wanted: set[str] = set()
        for glb in sorted(MAPS.glob("*.glb")):
            wanted |= write_dressing(glb)
        export_props(wanted)
        return
    if args == ["--albedo"]:
        albedo_for_existing()
        return
    if not args or args == ["--collision"]:
        props = collision_for_existing()
        export_props(props)
        return
    if args == ["--zones"]:
        args = ZONE_MAPS
    props: set[str] = set()
    for stem in args:
        export_map(stem)
        path = DATA / f"{stem}_collision.json"
        if path.is_file():
            doc = json.loads(path.read_text(encoding="utf-8"))
            props.update(item["mesh"] for item in doc.get("borders", []))
    export_props(props)


if __name__ == "__main__":
    main()
