#!/usr/bin/env python3
"""Export a Time Stranger battle field as a GLB for the Godot arena.

d0172b is the first stage in battle_field.mbe. The fight bowl is recentered
on the origin and scaled so it wraps the 11 m combat ring.
"""

from __future__ import annotations

import io
import json
import struct
import sys
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/ts"))
sys.path.insert(0, str(ROOT / "tools/ts/.venv/lib/python3.12/site-packages"))

from bake_arena import _read_geom, _trs  # noqa: E402
from dsts_extractor import geom as geom_mod  # noqa: E402
from dsts_extractor.eyes import _decode_dds_dx10  # noqa: E402

UNPACKED = ROOT / "private/reference/time-stranger/unpacked"
OUT = ROOT / "src/assets/stages"
PLATFORM_RADIUS = 13.5
BOWL_RADIUS = 36.0
MAX_EXTENT = 42.0
MAX_TRIS = 80000
MAX_TEX = 512
SKIP = ("ani_add", "ani_alp", "fireline", "fire", "sky", "_far", "effect")

# One floor picture and one sky per field. The gear disc and the d0101
# night panorama belong to the rock bowl only.
THEMES = {
    "d0172b": {"floor": "d0101_gear02_c", "sky": "d0101_sky01_c", "kind": "equirect"},
    "d0178b": {"floor": "d0101_floor01_c", "sky": "sky_cloud_01", "kind": "clouds", "ground": (0.55, 0.68, 0.74)},
    "d0273b": {"floor": "d02floor03_c", "sky": "sky_cloud_d0201", "kind": "clouds", "ground": (0.62, 0.78, 0.76)},
    "d0374b": {"floor": "d03floor100_c", "sky": "sky_cloud_01night", "kind": "clouds", "ground": (0.08, 0.10, 0.16), "crop": 0.66},
    "d0572b": {"floor": "d0501floor02_c", "sky": "d0501_sky01_c", "kind": "equirect"},
}


class Blob:
    def __init__(self) -> None:
        self.data = bytearray()
        self.views: list[dict] = []
        self.accessors: list[dict] = []

    def pad(self, n: int = 4) -> None:
        while len(self.data) % n:
            self.data.append(0)

    def view(self, raw: bytes, target: int | None = None) -> int:
        self.pad()
        offset = len(self.data)
        self.data += raw
        entry: dict = {"buffer": 0, "byteOffset": offset, "byteLength": len(raw)}
        if target is not None:
            entry["target"] = target
        self.views.append(entry)
        return len(self.views) - 1

    def accessor(self, view: int, count: int, ctype: int, atype: str, mins=None, maxs=None) -> int:
        entry: dict = {
            "bufferView": view,
            "componentType": ctype,
            "count": count,
            "type": atype,
        }
        if mins is not None:
            entry["min"] = mins
            entry["max"] = maxs
        self.accessors.append(entry)
        return len(self.accessors) - 1


def albedo_name(mat) -> str | None:
    """Color map from the material, or the sibling `_c` / `_ca` of a normal map.

    Most field rocks bind only a normal, a roughness map, and a cubemap. The
    color file sits next to them (`d0101_rock02_n` → `d0101_rock02_c`).
    """
    if mat is None:
        return None
    prefer: list[str] = []
    siblings: list[str] = []
    for binding in mat.bindings:
        name = binding.texture_name or ""
        low = name.lower()
        if not name or "cube" in low:
            continue
        if low.endswith(("_c", "_ca")):
            prefer.append(low)
            continue
        stem = low
        for suffix in ("_n", "_na", "_rm", "_em", "_e"):
            if stem.endswith(suffix):
                stem = stem[: -len(suffix)]
                break
        else:
            continue
        for suffix in ("_c", "_ca"):
            candidate = stem + suffix
            if (UNPACKED / "images" / f"{candidate}.img").is_file():
                siblings.append(candidate)
                break
    return (prefer or siblings or [None])[0]


def _is_palette(image: Image.Image) -> bool:
    """Color-id ramps and palette charts are not surface pictures."""
    sample = np.asarray(image.convert("RGB").resize((32, 32)), dtype=np.float32)
    peak = sample.max(axis=2)
    dull = sample.min(axis=2)
    sat = (peak - dull) / np.maximum(peak, 1.0)
    vivid = sat > 0.4
    if float(vivid.mean()) < 0.2:
        return False
    red, green, blue = sample[:, :, 0], sample[:, :, 1], sample[:, :, 2]
    hue = (np.arctan2(green - blue, red - blue) + np.pi) / (2.0 * np.pi)
    counts, _edges = np.histogram(hue[vivid], bins=8, range=(0.0, 1.0))
    kinds = int((counts > vivid.sum() * 0.08).sum())
    return kinds >= 4


def texture_png(stem: str, cache: dict[str, bytes | None]) -> bytes | None:
    key = stem.lower()
    if key in cache:
        return cache[key]
    # Chart and id-ramp images, not pictures of a surface.
    if "color0" in key or key.startswith("d02line"):
        cache[key] = None
        return None
    path = UNPACKED / "images" / f"{key}.img"
    raw = None
    if path.is_file():
        image = _decode_dds_dx10(path)
        if image is None:
            try:
                image = Image.open(path)
            except OSError:
                image = None
        if image is not None and _is_palette(image):
            image = None
        if image is not None:
            image = image.convert("RGBA")
            # A mask in the alpha channel would punch the rocks out.
            opaque = image.getchannel("A").point(lambda _v: 255)
            image.putalpha(opaque)
            image.thumbnail((MAX_TEX, MAX_TEX))
            buf = io.BytesIO()
            image.save(buf, format="PNG")
            raw = buf.getvalue()
    cache[key] = raw
    return raw


def bone_worlds(bones) -> list[np.ndarray]:
    worlds: list[np.ndarray | None] = [None] * len(bones)

    def world(index: int) -> np.ndarray:
        cached = worlds[index]
        if cached is not None:
            return cached
        bone = bones[index]
        pose = bone.bind_pose
        local = _trs(np.array(pose.position, dtype=np.float64), pose.rotation, pose.scale)
        if bone.parent_index is None:
            worlds[index] = local
        else:
            worlds[index] = world(bone.parent_index) @ local
        return worlds[index]

    for index in range(len(bones)):
        world(index)
    return [m if m is not None else np.identity(4) for m in worlds]


def _piece(mesh, worlds, names, index, materials):
    if "Position" not in mesh.streams or mesh.num_vertices < 3:
        return None
    name = (names[index] if index < len(names) else "") or (mesh.material_name or "")
    low = name.lower()
    mat = materials[mesh.data_index] if mesh.data_index < len(materials) else None
    label = f"{low} {(mat.name if mat is not None else '').lower()}"
    if any(token in label for token in SKIP):
        return None
    bone = geom_mod.rigid_attach_bone(mesh)
    if bone is None:
        return None
    pos = np.array([v[:3] for v in mesh.streams["Position"]], dtype=np.float64)
    placed = np.concatenate([pos, np.ones((len(pos), 1))], axis=1)
    pos = (worlds[bone] @ placed.T).T[:, :3]
    extent = pos.max(0) - pos.min(0)
    # A parent bone with a huge scale turns a small card into a kilometer-long
    # shard. The authored fight props stay under a few dozen meters.
    if float(extent.max()) > MAX_EXTENT or float(extent.max()) < 0.15:
        return None
    centre = (pos.min(0) + pos.max(0)) * 0.5
    if abs(float(centre[1])) > 80.0:
        return None
    if "UV" in mesh.streams:
        uv = np.array([v[:2] for v in mesh.streams["UV"]], dtype=np.float32)
        uv[:, 1] = 1.0 - uv[:, 1]
    else:
        uv = np.zeros((len(pos), 2), np.float32)
    try:
        tris = np.array(geom_mod.triangles(mesh), dtype=np.int32)
    except Exception:
        return None
    if len(tris) == 0:
        return None
    return pos, uv, tris, albedo_name(mat), label, centre, float(extent[1])


def _turn(pos: np.ndarray, delta: float) -> np.ndarray:
    cosine, sine = float(np.cos(delta)), float(np.sin(delta))
    turned = pos.copy()
    x = turned[:, 0].copy()
    z = turned[:, 2].copy()
    turned[:, 0] = cosine * x - sine * z
    turned[:, 2] = sine * x + cosine * z
    return turned


# Keep the combat disc clear. Tall scenery that sits past the floor reads as
# a sliver on the horizon, and a low wedge in that sliver is see-through.
KEEP_RADIUS = 6.4
WALL_RADIUS = 8.6


def _seat_outward(pos: np.ndarray, factor: float) -> np.ndarray:
    """Scale a whole piece around the origin so its shape does not shear."""
    seated = pos.copy()
    centre = seated.mean(0)
    radius = float(np.hypot(centre[0], centre[2]))
    if radius <= KEEP_RADIUS:
        return seated
    landed = KEEP_RADIUS + (radius - KEEP_RADIUS) * factor
    scale = landed / radius
    seated[:, 0] *= scale
    seated[:, 2] *= scale
    return seated


def seat_around_ring(batches):
    """Slide the cliffs in so they stand just outside the combat ring.

    Height stays put. Only the horizontal radius changes, and vertices
    inside the ring are left where they are.
    """
    clouds = [pos for pos, _uv, _tris, _albedo, name in batches if "floor" not in name]
    if not clouds:
        return batches
    all_pos = np.concatenate(clouds, 0)
    radial = np.hypot(all_pos[:, 0], all_pos[:, 2])
    tall = all_pos[:, 1] > 2.0
    if int(tall.sum()) < 200:
        return batches
    inner = float(np.percentile(radial[tall], 12))
    if inner <= WALL_RADIUS + 0.8:
        factor = 1.0
    else:
        factor = (WALL_RADIUS - KEEP_RADIUS) / max(inner - KEEP_RADIUS, 0.5)
        factor = float(np.clip(factor, 0.18, 1.0))
    if factor < 0.999:
        print(f"  seated cliffs inward ({inner:.1f} m -> {WALL_RADIUS:.1f} m)")
    seated = [
        (_seat_outward(pos, factor), uv, tris, albedo, name)
        for pos, uv, tris, albedo, name in batches
    ]
    return seated


def _close_tall_gaps(batches):
    """Copy a tall wall into a wedge that is only a low sliver."""
    bins = 24
    tops = [-1.0] * bins
    donors: list[tuple[int, float, float]] = []
    for index, (pos, _uv, _tris, _albedo, name) in enumerate(batches):
        if "floor" in name:
            continue
        radial = np.hypot(pos[:, 0], pos[:, 2])
        rising = (radial > 7.0) & (radial < 14.5) & (pos[:, 1] > 1.2)
        if int(rising.sum()) < 8:
            continue
        angles = np.arctan2(pos[rising, 2], pos[rising, 0])
        for angle, height in zip(angles, pos[rising, 1], strict=False):
            slot = int((float(angle) + np.pi) / (2.0 * np.pi) * bins) % bins
            tops[slot] = max(tops[slot], float(height))
        high = pos[:, 1] > 3.0
        if int(high.sum()) < 8:
            continue
        radius = float(np.hypot(pos[high, 0].mean(), pos[high, 2].mean()))
        top = float(pos[:, 1].max())
        order = np.sort(np.arctan2(pos[high, 2], pos[high, 0]))
        gaps = np.diff(order, append=order[0] + 2.0 * np.pi)
        span = float(2.0 * np.pi - gaps.max()) if len(order) else 0.0
        if 7.2 <= radius <= 14.0 and top >= 4.0 and len(pos) < 8000 and span < np.deg2rad(70):
            donors.append((index, float(np.arctan2(pos[high, 2].mean(), pos[high, 0].mean())), top))
    if not donors:
        return batches
    donors.sort(key=lambda item: item[2], reverse=True)
    # A bowl that is already walled most of the way around gets every hole
    # filled. An open cliff stays open instead of growing a copied ring.
    closed = sum(1 for top in tops if top >= 3.0) >= bins * 0.35
    extra = []
    for slot, top in enumerate(tops):
        if top >= 2.8:
            continue
        if not closed:
            nearby = [tops[(slot + delta) % bins] for delta in (-2, -1, 1, 2)]
            if max(nearby) < 3.5:
                continue
        target = -np.pi + (slot + 0.5) * (2.0 * np.pi / bins)
        donor = donors[len(extra) % len(donors)]
        pos, uv, tris, albedo, name = batches[donor[0]]
        extra.append((_turn(pos, target - donor[1]), uv, tris, albedo, name + " fill"))
    if extra:
        print(f"  closed {len(extra)} gaps in the outer ring")
    return batches + extra


def _bowl_center(centres: np.ndarray) -> np.ndarray:
    best_count = -1
    best = centres.mean(0)
    xs = np.arange(centres[:, 0].min(), centres[:, 0].max() + 1.0, 16.0)
    zs = np.arange(centres[:, 2].min(), centres[:, 2].max() + 1.0, 16.0)
    for x in xs:
        for z in zs:
            near = (np.abs(centres[:, 0] - x) < 28.0) & (np.abs(centres[:, 2] - z) < 28.0)
            count = int(near.sum())
            if count > best_count and count > 0:
                best_count = count
                picked = centres[near]
                best = np.array([float(picked[:, 0].mean()), 0.0, float(picked[:, 2].mean())])
    return best


def _load_image(stem: str) -> Image.Image | None:
    path = UNPACKED / "images" / f"{stem.lower()}.img"
    if not path.is_file():
        return None
    image = _decode_dds_dx10(path)
    if image is None:
        try:
            image = Image.open(path)
        except OSError:
            return None
    return image.convert("RGB")


def clouds_to_equirect(image: Image.Image, ground: tuple[float, float, float], crop: float = 1.0) -> Image.Image:
    """Lay a square cloud layer across the top of a 2:1 panorama."""
    if crop < 0.99:
        image = image.crop((0, 0, image.width, max(8, int(image.height * crop))))
    src = np.asarray(image.resize((2048, 512), Image.Resampling.LANCZOS), dtype=np.float32)
    canvas = np.zeros((1024, 2048, 3), np.float32)
    canvas[:512] = src
    color = np.array(ground, np.float32) * 255.0
    span = np.linspace(0.0, 1.0, 512, dtype=np.float32)[:, None, None]
    canvas[512:] = color * (1.0 - 0.45 * span)
    fade = np.linspace(0.0, 1.0, 72, dtype=np.float32)[:, None, None]
    edge = src[-1][None, :, :]
    canvas[512:584] = edge * (1.0 - fade) + canvas[512:584] * fade
    return Image.fromarray(np.clip(canvas, 0, 255).astype(np.uint8), "RGB")


def write_sky(stem: str) -> None:
    theme = THEMES.get(stem)
    if theme is None:
        return
    image = _load_image(theme["sky"])
    if image is None:
        print(f"  sky missing {theme['sky']}")
        return
    if theme["kind"] == "clouds":
        image = clouds_to_equirect(image, theme["ground"], float(theme.get("crop", 1.0)))
    else:
        image = image.resize((2048, 1024), Image.Resampling.LANCZOS)
    dest = OUT / f"{stem}_sky.png"
    image.save(dest)
    print(f"  sky {dest.name}")


def append_floor(blob, images, textures, materials, tex_index, tex_cache, meshes, nodes, floor_stem: str) -> None:
    """A solid disc. The authored gear is a ring, so the middle was open sky."""
    radius = 18.0
    segments = 72
    positions = np.zeros((segments + 1, 3), np.float32)
    uvs = np.zeros((segments + 1, 2), np.float32)
    uvs[0] = (0.5, 0.5)
    for i in range(segments):
        angle = i / segments * np.pi * 2.0
        x = np.cos(angle) * radius
        z = np.sin(angle) * radius
        positions[i + 1] = (x, 0.04, z)
        uvs[i + 1] = (0.5 + x / radius, 0.5 + z / radius)
    # The first vertex is the center. This order points the normal upward,
    # so the disc is visible from the battle camera.
    positions[0, 1] = 0.04
    tris = np.array(
        [(0, 1 if i + 1 == segments else i + 2, i + 1) for i in range(segments)],
        dtype=np.uint32,
    )
    indices = tris.reshape(-1)
    pos_view = blob.view(np.ascontiguousarray(positions).tobytes(), 34962)
    uv_view = blob.view(np.ascontiguousarray(uvs).tobytes(), 34962)
    idx_view = blob.view(indices.astype(np.uint16).tobytes(), 34963)
    primitive = {
        "attributes": {
            "POSITION": blob.accessor(
                pos_view, len(positions), 5126, "VEC3",
                positions.min(0).tolist(), positions.max(0).tolist(),
            ),
            "TEXCOORD_0": blob.accessor(uv_view, len(uvs), 5126, "VEC2"),
        },
        "indices": blob.accessor(idx_view, len(indices), 5123, "SCALAR"),
    }
    png = texture_png(floor_stem, tex_cache)
    if png is not None:
        if "floor" not in tex_index:
            view = blob.view(png)
            images.append({"bufferView": view, "mimeType": "image/png"})
            textures.append({"source": len(images) - 1})
            materials.append({
                "name": "floor",
                "doubleSided": True,
                "pbrMetallicRoughness": {
                    "baseColorTexture": {"index": len(textures) - 1},
                    "metallicFactor": 0.0,
                    "roughnessFactor": 0.9,
                },
            })
            tex_index["floor"] = len(materials) - 1
        primitive["material"] = tex_index["floor"]
    meshes.append({"primitives": [primitive]})
    nodes.append({"mesh": len(meshes) - 1})


def export(stem: str) -> Path:
    g = _read_geom(UNPACKED / f"{stem}.geom", field=True)
    worlds = bone_worlds(g.bones)
    names = list(getattr(g, "mesh_names", []))
    pieces = []
    for i, mesh in enumerate(g.meshes):
        piece = _piece(mesh, worlds, names, i, g.materials)
        if piece is not None:
            pieces.append(piece)
    if not pieces:
        raise SystemExit(f"{stem} produced no stage pieces")

    centres = np.stack([piece[5] for piece in pieces])
    bowl = _bowl_center(centres)
    kept = []
    for piece in pieces:
        centre = piece[5]
        flat = float(np.hypot(centre[0] - bowl[0], centre[2] - bowl[2]))
        if flat <= BOWL_RADIUS:
            kept.append(piece)
    if len(kept) < 4:
        kept = pieces
    batches = [(p, u, t, a, n) for p, u, t, a, n, _c, _ey in kept]

    total = sum(len(b[2]) for b in batches)
    if total > MAX_TRIS:
        stride = int(np.ceil(total / MAX_TRIS))
        batches = [(p, u, t[::stride], a, n) for p, u, t, a, n in batches if len(t[::stride])]
        total = sum(len(b[2]) for b in batches)

    # Center on the biggest flat piece (the gear disc) so the 11 m ring
    # sits on a floor instead of in the gap between rock walls.
    def platform_radius(piece) -> float:
        pos, centre = piece[0], piece[5]
        flat = np.hypot(pos[:, 0] - centre[0], pos[:, 2] - centre[2])
        return float(np.percentile(flat, 95))

    flats = [piece for piece in kept if piece[6] < 1.2]
    platform = max(flats, key=platform_radius) if flats else None
    radius = platform_radius(platform) if platform is not None else 0.0
    # A prop smaller than the combat ring is not the bowl. Scaling the
    # whole map off it throws the walls hundreds of metres out.
    if platform is None or radius < 6.0:
        cx = float(bowl[0])
        cz = float(bowl[2])
        cloud = np.concatenate([piece[0] for piece in kept], 0)
        y0 = float(np.median(cloud[:, 1]))
        # These bowls are authored at roughly the same size as d0172.
        # Dividing by a tiny prop, or by the median of a spread-out prop
        # cloud, either blows the walls out or shrinks the floor away.
        scale = PLATFORM_RADIUS / 16.0
    else:
        cx = float(platform[5][0])
        cz = float(platform[5][2])
        # The gear is a shallow dish. Level the walk surface on the inner
        # floor, where the Digimon stand, rather than on the raised rim.
        local = platform[0]
        inward = np.hypot(local[:, 0] - cx, local[:, 2] - cz)
        inner = local[inward < radius * 0.35]
        if len(inner) < 8:
            inner = local
        y0 = float(np.median(inner[:, 1]))
        scale = PLATFORM_RADIUS / max(radius, 1.0)
    print(
        f"{stem}: {len(kept)} meshes, {total} tris, "
        f"center=({cx:.1f},{y0:.1f},{cz:.1f}) scale={scale:.3f}"
    )

    tex_cache: dict[str, bytes | None] = {}
    images: list[dict] = []
    textures: list[dict] = []
    materials: list[dict] = []
    tex_index: dict[str, int] = {}
    blob = Blob()
    meshes = []
    nodes = []

    placed = []
    for pos, uv, tris, albedo, name in batches:
        # The source gear is a ring with a hole. A solid disc is added below.
        if "gear" in name:
            continue
        pos = pos.copy()
        pos[:, 0] = (pos[:, 0] - cx) * scale
        pos[:, 1] = (pos[:, 1] - y0) * scale
        pos[:, 2] = (pos[:, 2] - cz) * scale
        placed.append((pos, uv, tris, albedo, name))
    placed = seat_around_ring(placed)
    # Only the rock bowl should grow copied wall sections. Other fields
    # were turning one crystal spire into a ring of identical pillars.
    if stem == "d0172b":
        placed = _close_tall_gaps(placed)

    for pos, uv, tris, albedo, _name in placed:
        indices = tris.reshape(-1).astype(np.uint32)
        pos_view = blob.view(np.ascontiguousarray(pos, dtype=np.float32).tobytes(), 34962)
        uv_view = blob.view(np.ascontiguousarray(uv, dtype=np.float32).tobytes(), 34962)
        idx_type = 5123 if int(indices.max()) <= 65535 else 5125
        idx_np = indices.astype(np.uint16 if idx_type == 5123 else np.uint32)
        idx_view = blob.view(idx_np.tobytes(), 34963)
        primitive = {
            "attributes": {
                "POSITION": blob.accessor(
                    pos_view, len(pos), 5126, "VEC3",
                    pos.min(0).tolist(), pos.max(0).tolist(),
                ),
                "TEXCOORD_0": blob.accessor(uv_view, len(uv), 5126, "VEC2"),
            },
            "indices": blob.accessor(idx_view, len(indices), idx_type, "SCALAR"),
        }
        png = texture_png(albedo, tex_cache) if albedo else None
        if png is not None and albedo:
            key = albedo.lower()
            if key not in tex_index:
                view = blob.view(png)
                images.append({"bufferView": view, "mimeType": "image/png"})
                textures.append({"source": len(images) - 1})
                materials.append({
                    "name": key,
                    "doubleSided": True,
                    "pbrMetallicRoughness": {
                        "baseColorTexture": {"index": len(textures) - 1},
                        "metallicFactor": 0.0,
                        "roughnessFactor": 0.85,
                    },
                })
                tex_index[key] = len(materials) - 1
            primitive["material"] = tex_index[key]
        else:
            if "flat" not in tex_index:
                materials.append({
                    "name": "flat",
                    "doubleSided": True,
                    "pbrMetallicRoughness": {
                        "baseColorFactor": [0.34, 0.36, 0.4, 1.0],
                        "metallicFactor": 0.0,
                        "roughnessFactor": 0.9,
                    },
                })
                tex_index["flat"] = len(materials) - 1
            primitive["material"] = tex_index["flat"]
        meshes.append({"primitives": [primitive]})
        nodes.append({"mesh": len(meshes) - 1})

    floor_stem = THEMES.get(stem, {}).get("floor", "d0101_gear02_c")
    append_floor(blob, images, textures, materials, tex_index, tex_cache, meshes, nodes, floor_stem)

    gltf = {
        "asset": {"version": "2.0", "generator": "export_field.py"},
        "scene": 0,
        "scenes": [{"nodes": list(range(len(nodes)))}],
        "nodes": nodes,
        "meshes": meshes,
        "materials": materials,
        "buffers": [{"byteLength": len(blob.data)}],
        "bufferViews": blob.views,
        "accessors": blob.accessors,
    }
    if images:
        gltf["images"] = images
        gltf["textures"] = textures

    payload = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    while len(payload) % 4:
        payload += b" "
    while len(blob.data) % 4:
        blob.data.append(0)
    total_len = 12 + 8 + len(payload) + 8 + len(blob.data)
    out = bytearray()
    out += struct.pack("<4sII", b"glTF", 2, total_len)
    out += struct.pack("<II", len(payload), 0x4E4F534A)
    out += payload
    out += struct.pack("<II", len(blob.data), 0x004E4942)
    out += blob.data
    dest = OUT / f"{stem}.glb"
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_bytes(out)
    print(f"wrote {dest} ({dest.stat().st_size / 1e6:.1f} MB) textures={len(images)}")
    write_sky(stem)
    return dest


if __name__ == "__main__":
    export(sys.argv[1] if len(sys.argv) > 1 else "d0172b")
