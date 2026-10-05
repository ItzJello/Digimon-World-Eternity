#!/usr/bin/env python3
"""Pull a few solid props out of Time Stranger and stand them on the floor.

Each file is one object: centered, sitting on y = 0, scaled to a fight-sized
height. Leaves keep their alpha so the cards cut out instead of showing the
green backing.
"""

from __future__ import annotations

import json
import struct
import sys
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/ts"))
sys.path.insert(0, str(ROOT / "tools/ts/.venv/lib/python3.12/site-packages"))

from dsts_extractor.eyes import _decode_dds_dx10  # noqa: E402
from export_field import UNPACKED, Blob, _piece, bone_worlds  # noqa: E402
from bake_arena import _read_geom  # noqa: E402

OUT = ROOT / "src/assets/stages/props"

# (file, source geom, predicate on (tris, albedo, label), target height)
PICKS = [
    ("rock_a", "d0172b", lambda n, a, label: n == 2548 and a == "d0101_rock02_c", 3.1),
    ("rock_b", "d0172b", lambda n, a, label: n == 362 and a == "d0101_rock02_c", 2.4),
    ("rock_c", "d0172b", lambda n, a, label: n == 1804 and "cliff" in label, 2.8),
    ("crystal", "d0374b", lambda n, a, label: n == 1074 and a == "d03cliff10_c", 3.0),
    ("pipe", "d0572b", lambda n, a, label: n == 1200 and "pipe" in (a or ""), 3.2),
]


def _png(stem: str, keep_alpha: bool) -> bytes | None:
    path = UNPACKED / "images" / f"{stem.lower()}.img"
    if not path.is_file():
        return None
    image = _decode_dds_dx10(path)
    if image is None:
        try:
            image = Image.open(path)
        except OSError:
            return None
    image = image.convert("RGBA")
    if not keep_alpha:
        image.putalpha(Image.new("L", image.size, 255))
    image.thumbnail((512, 512))
    buf = __import__("io").BytesIO()
    image.save(buf, format="PNG")
    return buf.getvalue()


def _normals(pos: np.ndarray, tris: np.ndarray) -> np.ndarray:
    normal = np.zeros_like(pos, dtype=np.float64)
    a = pos[tris[:, 0]]
    b = pos[tris[:, 1]]
    c = pos[tris[:, 2]]
    face = np.cross(b - a, c - a)
    for corner in range(3):
        np.add.at(normal, tris[:, corner], face)
    length = np.linalg.norm(normal, axis=1, keepdims=True)
    length[length < 1e-8] = 1.0
    return (normal / length).astype(np.float32)


def _stand(parts: list[tuple], height: float) -> list[tuple]:
    """Stand the long axis up, then sit the object in the floor."""
    stacked = np.concatenate([pos for pos, *_ in parts], axis=0)
    centre = stacked.mean(0)
    local = stacked - centre
    _, axes = np.linalg.eigh(np.cov(local.T))
    # Smallest spread becomes depth, largest becomes up.
    basis = np.stack([axes[:, 1], axes[:, 2], axes[:, 0]], axis=1)
    if np.linalg.det(basis) < 0:
        basis[:, 0] *= -1
    turned = [(pos - centre) @ basis for pos, *_ in parts]
    stacked = np.concatenate(turned, axis=0)
    low = stacked.min(0)
    high = stacked.max(0)
    span = float(high[1] - low[1]) or 1.0
    scale = height / span
    wide = float(max(high[0] - low[0], high[2] - low[2])) * scale
    if wide > 3.2:
        scale *= 3.2 / wide
    centre_x = float((low[0] + high[0]) * 0.5)
    centre_z = float((low[2] + high[2]) * 0.5)
    stood = []
    for (_pos, uv, tris, albedo, label), moved in zip(parts, turned):
        placed = moved.copy()
        placed[:, 0] -= centre_x
        placed[:, 2] -= centre_z
        placed[:, 1] -= float(low[1])
        placed *= scale
        placed[:, 1] -= height * 0.12
        stood.append((placed.astype(np.float32), uv, tris, albedo, label))
    return stood


def _write(name: str, parts: list[tuple]) -> None:
    blob = Blob()
    images: list[dict] = []
    textures: list[dict] = []
    materials: list[dict] = []
    meshes = []
    nodes = []
    tex_index: dict[str, int] = {}
    for pos, uv, tris, albedo, _label in parts:
        indices = tris.reshape(-1).astype(np.uint32)
        normal = _normals(pos.astype(np.float64), tris)
        pos_view = blob.view(np.ascontiguousarray(pos, dtype=np.float32).tobytes(), 34962)
        uv_view = blob.view(np.ascontiguousarray(uv, dtype=np.float32).tobytes(), 34962)
        n_view = blob.view(np.ascontiguousarray(normal).tobytes(), 34962)
        idx_type = 5123 if int(indices.max()) <= 65535 else 5125
        idx_np = indices.astype(np.uint16 if idx_type == 5123 else np.uint32)
        idx_view = blob.view(idx_np.tobytes(), 34963)
        primitive = {
            "attributes": {
                "POSITION": blob.accessor(
                    pos_view, len(pos), 5126, "VEC3",
                    pos.min(0).tolist(), pos.max(0).tolist(),
                ),
                "NORMAL": blob.accessor(n_view, len(normal), 5126, "VEC3"),
                "TEXCOORD_0": blob.accessor(uv_view, len(uv), 5126, "VEC2"),
            },
            "indices": blob.accessor(idx_view, len(indices), idx_type, "SCALAR"),
        }
        keep_alpha = bool(albedo) and "leaf" in albedo
        png = _png(albedo, keep_alpha) if albedo else None
        key = (albedo or "") + ("#a" if keep_alpha else "")
        if png is not None and key not in tex_index:
            view = blob.view(png)
            images.append({"bufferView": view, "mimeType": "image/png"})
            textures.append({"source": len(images) - 1})
            material = {
                "doubleSided": True,
                "pbrMetallicRoughness": {
                    "baseColorTexture": {"index": len(textures) - 1},
                    "metallicFactor": 0.0,
                    "roughnessFactor": 0.86,
                },
            }
            if keep_alpha:
                material["alphaMode"] = "MASK"
                material["alphaCutoff"] = 0.35
            materials.append(material)
            tex_index[key] = len(materials) - 1
        if key in tex_index:
            primitive["material"] = tex_index[key]
        meshes.append({"primitives": [primitive]})
        nodes.append({"mesh": len(meshes) - 1})

    gltf = {
        "asset": {"version": "2.0", "generator": "export_props.py"},
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
    total = 12 + 8 + len(payload) + 8 + len(blob.data)
    out = bytearray()
    out += struct.pack("<4sII", b"glTF", 2, total)
    out += struct.pack("<II", len(payload), 0x4E4F534A)
    out += payload
    out += struct.pack("<II", len(blob.data), 0x004E4942)
    out += blob.data
    dest = OUT / f"{name}.glb"
    dest.write_bytes(out)
    print(f"  {dest.name} {dest.stat().st_size // 1024} KB  parts {len(parts)}")


def _box_uv(pos: np.ndarray, normal: np.ndarray, tile: float = 1.7) -> np.ndarray:
    """Project the mesh onto its faces so a tiling picture sits on the rock."""
    uv = np.zeros((len(pos), 2), np.float32)
    axis = np.argmax(np.abs(normal), axis=1)
    pairs = ((1, 2), (0, 2), (0, 1))
    for index, (u, v) in enumerate(pairs):
        mask = axis == index
        uv[mask, 0] = pos[mask, u] / tile
        uv[mask, 1] = pos[mask, v] / tile
    return uv


def _collect(stem: str, pred) -> list[tuple]:
    geom = _read_geom(UNPACKED / f"{stem}.geom", field=True)
    worlds = bone_worlds(geom.bones)
    best: tuple | None = None
    for index, mesh in enumerate(geom.meshes):
        piece = _piece(mesh, worlds, geom.mesh_names, index, geom.materials)
        if piece is None:
            continue
        pos, uv, tris, albedo, label, _centre, _height = piece
        if not pred(len(tris), albedo or "", label):
            continue
        extent = pos.max(0) - pos.min(0)
        ordered = sorted(float(v) for v in extent)
        ratio = ordered[0] / ordered[2] if ordered[2] else 0.0
        # Flat cards and long splinters read as broken shards.
        if ratio < 0.55:
            continue
        if best is None or ratio > best[0]:
            best = (ratio, [(pos, uv, tris, albedo, label)])
    return [] if best is None else best[1]


def _tree() -> list[tuple]:
    geom = _read_geom(UNPACKED / "treel06.geom", field=True)
    worlds = bone_worlds(geom.bones)
    parts = []
    for index, mesh in enumerate(geom.meshes):
        piece = _piece(mesh, worlds, geom.mesh_names, index, geom.materials)
        if piece is None:
            continue
        pos, uv, tris, albedo, label, _centre, _height = piece
        parts.append((pos, uv, tris, albedo, label))
    return parts


def _stones() -> None:
    """Two cliff chunks, rewrapped so each arena can use its own tiling picture."""
    geom = _read_geom(UNPACKED / "d0172b.geom", field=True)
    worlds = bone_worlds(geom.bones)
    found = []
    for index, mesh in enumerate(geom.meshes):
        piece = _piece(mesh, worlds, geom.mesh_names, index, geom.materials)
        if piece is None:
            continue
        pos, uv, tris, albedo, label, _centre, _height = piece
        if len(tris) != 1804 or "cliff" not in label:
            continue
        extent = pos.max(0) - pos.min(0)
        ordered = sorted(float(v) for v in extent)
        ratio = ordered[0] / ordered[2] if ordered[2] else 0.0
        if ratio < 0.6:
            continue
        found.append((ratio, float(ordered[2]), [(pos, uv, tris, albedo, label)]))
    found.sort(key=lambda row: -row[0])
    picked = []
    for row in found:
        if not picked or abs(row[1] - picked[0][1]) > 1.5:
            picked.append(row)
        if len(picked) == 2:
            break
    for name, (_ratio, _size, parts) in zip(("stone_a", "stone_b"), picked):
        stood = _stand(parts, 2.8)
        boxed = []
        for pos, _uv, tris, albedo, label in stood:
            normal = _normals(pos.astype(np.float64), tris)
            boxed.append((pos, _box_uv(pos, normal), tris, albedo, label))
        _write(name, boxed)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for name, stem, pred, height in PICKS:
        parts = _collect(stem, pred)
        if not parts:
            print(f"  missing {name}")
            continue
        _write(name, _stand(parts, height))
    _stones()
    tree = _tree()
    if tree:
        _write("tree", _stand(tree, 4.6))


if __name__ == "__main__":
    main()
