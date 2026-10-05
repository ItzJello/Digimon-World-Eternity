#!/usr/bin/env python3
"""Bake Time Stranger battle effects into GLBs the Godot client can play.

Each card is rigid on one bone. The GLB animates those bones and leaves the
vertices in bone-local space. Frame 0 is recentered on the origin so the
effect starts on the caster and travels in its local +Z.
"""

from __future__ import annotations

import io
import math
import struct
import sys
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
UNPACKED = ROOT / "private/reference/time-stranger/unpacked"
OUT = ROOT / "src/assets/effects"
sys.path.insert(0, str(ROOT / "tools/ts"))
sys.path.insert(0, str(ROOT / "tools/ts/.venv/lib/python3.12/site-packages"))

from bake_effect import bone_worlds, ensure_textures, load_anim, mesh_texture, mesh_tint  # noqa: E402
from dsts_extractor import geom  # noqa: E402
from dsts_extractor.eyes import _decode_dds_dx10  # noqa: E402

SKIP_PREFIX = ("sp_frg", "sm_frg")
MAX_SAMPLES = 36
MAX_TEX = 256


def quat_from_mat(rot: np.ndarray) -> tuple[float, float, float, float]:
    m = rot
    trace = float(m[0, 0] + m[1, 1] + m[2, 2])
    if trace > 0.0:
        s = math.sqrt(trace + 1.0) * 2.0
        w = 0.25 * s
        x = (m[2, 1] - m[1, 2]) / s
        y = (m[0, 2] - m[2, 0]) / s
        z = (m[1, 0] - m[0, 1]) / s
    elif m[0, 0] > m[1, 1] and m[0, 0] > m[2, 2]:
        s = math.sqrt(1.0 + m[0, 0] - m[1, 1] - m[2, 2]) * 2.0
        w = (m[2, 1] - m[1, 2]) / s
        x = 0.25 * s
        y = (m[0, 1] + m[1, 0]) / s
        z = (m[0, 2] + m[2, 0]) / s
    elif m[1, 1] > m[2, 2]:
        s = math.sqrt(1.0 + m[1, 1] - m[0, 0] - m[2, 2]) * 2.0
        w = (m[0, 2] - m[2, 0]) / s
        x = (m[0, 1] + m[1, 0]) / s
        y = 0.25 * s
        z = (m[1, 2] + m[2, 1]) / s
    else:
        s = math.sqrt(1.0 + m[2, 2] - m[0, 0] - m[1, 1]) * 2.0
        w = (m[1, 0] - m[0, 1]) / s
        x = (m[0, 2] + m[2, 0]) / s
        y = (m[1, 2] + m[2, 1]) / s
        z = 0.25 * s
    length = math.sqrt(x * x + y * y + z * z + w * w)
    if length < 1e-8:
        return (0.0, 0.0, 0.0, 1.0)
    return (x / length, y / length, z / length, w / length)


def decompose(world: np.ndarray) -> tuple[np.ndarray, tuple[float, float, float, float], np.ndarray]:
    translation = world[:3, 3].astype(np.float64).copy()
    scale = np.ones(3, np.float64)
    rot = np.eye(3, dtype=np.float64)
    for axis in range(3):
        column = world[:3, axis].astype(np.float64)
        length = float(np.linalg.norm(column))
        scale[axis] = length
        if length > 1e-6:
            rot[:, axis] = column / length
    return translation, quat_from_mat(rot), scale


def load_texture(stem: str) -> bytes | None:
    path = UNPACKED / "images" / f"{stem}.img"
    if not path.is_file():
        return None
    image = _decode_dds_dx10(path)
    if image is None:
        return None
    image = image.convert("RGBA")
    image.thumbnail((MAX_TEX, MAX_TEX))
    buf = io.BytesIO()
    image.save(buf, format="PNG")
    return buf.getvalue()


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


def export_one(stem: str, dest: Path) -> None:
    g = geom.read_geom(UNPACKED / f"{stem}.geom")
    header, rot, loc, scl = load_anim(UNPACKED / f"{stem}.anim")
    total = max(2, int(header.total_frames))
    duration = float(header.duration_seconds) if header.duration_seconds > 0 else total / 30.0
    step = max(1, total // MAX_SAMPLES)
    frames = list(range(0, total, step))
    if frames[-1] != total - 1:
        frames.append(total - 1)
    worlds = [bone_worlds(g.bones, rot, loc, scl, frame) for frame in frames]

    cards = []
    for mesh in g.meshes:
        if not mesh.weighted_bone_indices or "Position" not in mesh.streams:
            continue
        bone = int(mesh.weighted_bone_indices[0])
        if bone < 0 or bone >= len(g.bones):
            continue
        if g.bones[bone].name.startswith(SKIP_PREFIX):
            continue
        tris = geom.triangles(mesh)
        if not tris:
            continue
        cards.append((mesh, bone, mesh_texture(g, mesh), mesh_tint(g, mesh), tris))
    if not cards:
        raise RuntimeError("no cards")

    used = []
    for _mesh, bone, _tex, _tint, _tris in cards:
        if bone not in used:
            used.append(bone)

    origin = np.zeros(3, np.float64)
    for bone in used:
        origin += worlds[0][bone][:3, 3]
    origin /= max(1, len(used))

    extent = 0.0
    for frame_worlds in worlds:
        for bone in used:
            delta = frame_worlds[bone][:3, 3] - origin
            extent = max(extent, float(np.max(np.abs(delta))))
    root_scale = 1.0 if extent < 8.0 or extent < 1e-4 else 6.0 / extent

    wanted: list[str] = []
    for _mesh, _bone, tex, _tint, _tris in cards:
        if tex and tex not in wanted:
            wanted.append(tex)
    ensure_textures(wanted)
    tex_bytes: dict[str, bytes] = {}
    for tex in wanted:
        raw = load_texture(tex)
        if raw is not None:
            tex_bytes[tex] = raw

    blob = Blob()
    images = []
    textures = []
    materials = []
    tex_index: dict[str, int] = {}
    for name, raw in tex_bytes.items():
        view = blob.view(raw)
        images.append({"bufferView": view, "mimeType": "image/png"})
        textures.append({"source": len(images) - 1})
        materials.append(
            {
                "name": name,
                "doubleSided": True,
                "alphaMode": "BLEND",
                "pbrMetallicRoughness": {
                    "baseColorTexture": {"index": len(textures) - 1},
                    "metallicFactor": 0.0,
                    "roughnessFactor": 1.0,
                },
                "emissiveTexture": {"index": len(textures) - 1},
                "emissiveFactor": [1.0, 1.0, 1.0],
            }
        )
        tex_index[name] = len(materials) - 1
    if not materials:
        materials.append(
            {
                "name": "glow",
                "doubleSided": True,
                "alphaMode": "BLEND",
                "pbrMetallicRoughness": {
                    "baseColorFactor": [1.0, 0.7, 0.3, 0.9],
                    "metallicFactor": 0.0,
                    "roughnessFactor": 1.0,
                },
                "emissiveFactor": [1.0, 0.55, 0.15],
            }
        )

    meshes = []
    mesh_of_card = []
    for mesh, _bone, tex, _tint, tris in cards:
        pos = np.array(mesh.streams["Position"], dtype=np.float32)[:, :3]
        if "UV" in mesh.streams:
            uv = np.array(mesh.streams["UV"], dtype=np.float32)[:, :2]
            uv[:, 1] = 1.0 - uv[:, 1]
        else:
            uv = np.zeros((len(pos), 2), np.float32)
        normal = np.zeros_like(pos)
        normal[:, 1] = 1.0
        indices = np.array([i for tri in tris for i in tri], np.uint32)
        pos_view = blob.view(pos.astype(np.float32).tobytes(), 34962)
        nrm_view = blob.view(normal.astype(np.float32).tobytes(), 34962)
        uv_view = blob.view(uv.astype(np.float32).tobytes(), 34962)
        idx_type = 5123 if int(indices.max()) <= 65535 else 5125
        idx_raw = indices.astype(np.uint16 if idx_type == 5123 else np.uint32).tobytes()
        idx_view = blob.view(idx_raw, 34963)
        primitive = {
            "attributes": {
                "POSITION": blob.accessor(
                    pos_view, len(pos), 5126, "VEC3",
                    pos.min(0).tolist(), pos.max(0).tolist(),
                ),
                "NORMAL": blob.accessor(nrm_view, len(normal), 5126, "VEC3"),
                "TEXCOORD_0": blob.accessor(uv_view, len(uv), 5126, "VEC2"),
            },
            "indices": blob.accessor(idx_view, len(indices), idx_type, "SCALAR"),
            "material": tex_index[tex] if tex and tex in tex_index else 0,
        }
        meshes.append({"primitives": [primitive]})
        mesh_of_card.append(len(meshes) - 1)

    # node 0 is the root. Bone nodes follow, in `used` order.
    bone_node = {bone: 1 + i for i, bone in enumerate(used)}
    nodes: list[dict] = [{"name": stem, "children": [bone_node[b] for b in used], "scale": [root_scale, root_scale, root_scale]}]
    bone_children: dict[int, list[int]] = {bone: [] for bone in used}
    for card_i, (_mesh, bone, _tex, _tint, _tris) in enumerate(cards):
        node_index = len(nodes)
        nodes.append({"name": f"card{card_i}", "mesh": mesh_of_card[card_i]})
        bone_children[bone].append(node_index)
    for bone in used:
        nodes.append({})  # placeholder replaced below
    # Rebuild bone nodes in place: they were reserved as indices 1..len(used)
    # but card nodes were appended after a single root, so bone indices are wrong.
    # Rebuild cleanly.
    nodes = [{"name": stem, "scale": [root_scale, root_scale, root_scale]}]
    bone_node = {}
    for bone in used:
        bone_node[bone] = len(nodes)
        nodes.append({"name": g.bones[bone].name or f"bone{bone}"})
    nodes[0]["children"] = [bone_node[b] for b in used]
    for card_i, (_mesh, bone, _tex, _tint, _tris) in enumerate(cards):
        node_index = len(nodes)
        nodes.append({"name": f"card{card_i}", "mesh": mesh_of_card[card_i]})
        nodes[bone_node[bone]].setdefault("children", []).append(node_index)

    times = np.array([duration * frame / max(1, total - 1) for frame in frames], np.float32)
    time_view = blob.view(times.tobytes())
    time_acc = blob.accessor(time_view, len(times), 5126, "SCALAR", [float(times[0])], [float(times[-1])])

    samplers = []
    channels = []
    for bone in used:
        translations = []
        rotations = []
        scales = []
        for frame_worlds in worlds:
            translation, quat, scale = decompose(frame_worlds[bone])
            translations.append((translation - origin).astype(np.float32))
            rotations.append(quat)
            scales.append(scale.astype(np.float32))
        t_arr = np.stack(translations).astype(np.float32)
        r_arr = np.array(rotations, np.float32)
        s_arr = np.stack(scales).astype(np.float32)
        t_acc = blob.accessor(blob.view(t_arr.tobytes()), len(t_arr), 5126, "VEC3")
        r_acc = blob.accessor(blob.view(r_arr.tobytes()), len(r_arr), 5126, "VEC4")
        s_acc = blob.accessor(blob.view(s_arr.tobytes()), len(s_arr), 5126, "VEC3")
        base = len(samplers)
        samplers.append({"input": time_acc, "output": t_acc, "interpolation": "LINEAR"})
        samplers.append({"input": time_acc, "output": r_acc, "interpolation": "LINEAR"})
        samplers.append({"input": time_acc, "output": s_acc, "interpolation": "LINEAR"})
        node = bone_node[bone]
        channels.append({"sampler": base, "target": {"node": node, "path": "translation"}})
        channels.append({"sampler": base + 1, "target": {"node": node, "path": "rotation"}})
        channels.append({"sampler": base + 2, "target": {"node": node, "path": "scale"}})

    gltf = {
        "asset": {"version": "2.0", "generator": "export_effects.py"},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": nodes,
        "meshes": meshes,
        "materials": materials,
        "buffers": [{"byteLength": len(blob.data)}],
        "bufferViews": blob.views,
        "accessors": blob.accessors,
        "animations": [{"name": "play", "samplers": samplers, "channels": channels}],
    }
    if images:
        gltf["images"] = images
        gltf["textures"] = textures

    import json

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
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_bytes(out)


def main() -> None:
    stems = sorted(p.name[:-5] for p in UNPACKED.glob("ef_b_*.geom"))
    if len(sys.argv) > 1:
        stems = [s for s in sys.argv[1:] if (UNPACKED / f"{s}.geom").is_file()]
    print(f"{len(stems)} effects")
    failed = []
    for stem in stems:
        dest = OUT / f"{stem}.glb"
        try:
            export_one(stem, dest)
            print(f"  {stem}  {dest.stat().st_size / 1e6:.2f} MB")
        except Exception as exc:
            failed.append(stem)
            print(f"  FAIL {stem}: {exc}")
    if failed:
        print("failed", ", ".join(failed))


if __name__ == "__main__":
    main()
