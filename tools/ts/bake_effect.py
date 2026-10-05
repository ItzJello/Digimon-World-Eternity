#!/usr/bin/env python3
"""Bake a Time Stranger battle effect (ef_b_gfi_s02) into DMM parts.

Pepper Breath (battle_skill 20501) and Blue Blaster (21511) both reference
effect id 30011, which is this mesh. Effect anims use the same 80AE clips as
characters, except some chunk headers have a non-zero flag the chr parser
rejects. Each card is rigid on one bone, so the clip stores that bone's world
matrix and the vertices stay in bone-local space.

Parts stay inside the DMM caps (48 bones, 8 textures, 16 batches).
"""
from __future__ import annotations

import struct
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
UNPACKED = ROOT / "private/reference/time-stranger/unpacked"
TSDAT = ROOT / "assets/extracted/us/TSDAT"
VENV = ROOT / "tools/ts/.venv/lib/python3.12/site-packages"
sys.path.insert(0, str(VENV))

from dsts_extractor import geom  # noqa: E402
from dsts_extractor.anim import (  # noqa: E402
    _IDX_LIST_SECTION_START,
    _parse_header,
    _read_chunk,
    _read_idx_list,
    _read_rel_ptr,
)
from dsts_extractor.eyes import _decode_dds_dx10  # noqa: E402
from dsts_extractor.native_export import mat4_from_trs  # noqa: E402

STEM = "ef_b_gfi_s02"
GAMEDATA = Path(
    "/mnt/c/Program Files (x86)/Steam/steamapps/common/"
    "Digimon Story Time Stranger/gamedata/app_0.dx11.mvgl"
)
MAX_BONES = 48
MAX_TEX = 8
SAMPLE_FRAMES = 24
# Debris cards. The flame / impact / glow meshes are the skill.
SKIP_PREFIX = ("sp_frg", "sm_frg")
PREFER_TEX = (
    "ef_fir_1441a",
    "ef_fir_1473c",
    "ef_lin_0201c",
    "ef_bul_0141c",
    "ef_wtr_1731a",
    "ef_smk_0800c",
    "ef_bur_0200",
)


def load_anim(path: Path):
    """parse_anim, minus the chunk flag it rejects on effect clips."""
    blob = path.read_bytes()
    header = _parse_header(blob)
    cursor = _IDX_LIST_SECTION_START
    sentinel = header.num_bones
    su_sentinel = header.shader_uniform_channel_count
    _static_rot, cursor = _read_idx_list(blob, cursor, header.static_rot_count, 16, sentinel)
    _static_loc, cursor = _read_idx_list(blob, cursor, header.static_loc_count, 8, sentinel)
    _static_scl, cursor = _read_idx_list(blob, cursor, header.static_scl_count, 8, sentinel)
    _static_su, cursor = _read_idx_list(blob, cursor, header.static_su_count, 8, su_sentinel)
    anim_rot, cursor = _read_idx_list(blob, cursor, header.anim_rot_count, 8, sentinel)
    anim_loc, cursor = _read_idx_list(blob, cursor, header.anim_loc_count, 8, sentinel)
    anim_scl, cursor = _read_idx_list(blob, cursor, header.anim_scl_count, 8, sentinel)

    ptrs = _read_rel_ptr(blob, 0x38)
    counts = _read_rel_ptr(blob, 0x3C)
    chunks = []
    for i in range(header.num_keyframe_chunks):
        _length, _flag, abs_ptr = struct.unpack_from("<HHI", blob, ptrs + i * 8)
        cumulative, frames_m1 = struct.unpack_from("<HH", blob, counts + i * 4)
        chunks.append(
            _read_chunk(
                blob,
                abs_ptr,
                header.anim_rot_count,
                header.anim_loc_count,
                header.anim_scl_count,
                header.anim_su_count,
                cumulative,
                frames_m1 + 1,
            )
        )

    def tracks(kind: str, index_list):
        out: dict[int, list] = {}
        for chunk in chunks:
            series = getattr(chunk, kind)
            for slot, track in enumerate(series):
                if slot >= len(index_list):
                    break
                bidx = index_list[slot]
                if bidx < 0 or bidx >= header.num_bones:
                    continue
                for frame, value in track:
                    out.setdefault(bidx, []).append((chunk.cumulative_frames + frame, value))
        for samples in out.values():
            samples.sort(key=lambda fv: fv[0])
        return out

    return header, tracks("rotations", anim_rot), tracks("locations", anim_loc), tracks("scales", anim_scl)


def sample_track(track, frame, default):
    if not track:
        return default
    if frame <= track[0][0]:
        return track[0][1]
    if frame >= track[-1][0]:
        return track[-1][1]
    for i in range(len(track) - 1):
        f0, v0 = track[i]
        f1, v1 = track[i + 1]
        if f0 <= frame <= f1:
            span = f1 - f0
            t = 0.0 if span == 0 else (frame - f0) / span
            return tuple((1.0 - t) * a + t * b for a, b in zip(v0, v1))
    return track[-1][1]


def quat_nlerp(track, frame, default):
    if not track:
        return default
    if frame <= track[0][0]:
        return track[0][1]
    if frame >= track[-1][0]:
        return track[-1][1]
    for i in range(len(track) - 1):
        f0, v0 = track[i]
        f1, v1 = track[i + 1]
        if f0 <= frame <= f1:
            span = f1 - f0
            t = 0.0 if span == 0 else (frame - f0) / span
            # Anim quats are WXYZ. Flip so the blend takes the short way.
            if sum(a * b for a, b in zip(v0, v1)) < 0.0:
                v1 = tuple(-x for x in v1)
            return tuple((1.0 - t) * a + t * b for a, b in zip(v0, v1))
    return track[-1][1]


def wxyz_to_xyzw(q):
    w, x, y, z = q
    return (x, y, z, w)


def retarget_meteor(bones, loc):
    """eff_sp falls from y=30 onto the field. In this arena that reads as a
    fireball dropping out of the ceiling. Keep the timing and send it forward
    from mouth height along +Z, which the client aims at the opponent."""
    idx = next((i for i, b in enumerate(bones) if b.name == "eff_sp"), None)
    track = loc.get(idx) if idx is not None else None
    if not track:
        return
    y0 = track[0][1][1]
    y1 = track[-1][1][1]
    span = y0 - y1
    if span < 1.0:
        return
    travel = 9.0
    mouth = 1.15
    loc[idx] = [
        (frame, (0.0, mouth, travel * max(0.0, min(1.0, (y0 - v[1]) / span))))
        for frame, v in track
    ]


def bone_worlds(bones, rot, loc, scl, frame):
    cache = [None] * len(bones)

    def resolve(idx):
        if cache[idx] is not None:
            return cache[idx]
        b = bones[idx]
        br, bp, bs = b.bind_pose.rotation, b.bind_pose.position, b.bind_pose.scale
        q = wxyz_to_xyzw(quat_nlerp(rot.get(idx), frame, (br[3], br[0], br[1], br[2])))
        # bind rotation is already xyzw in geom (x,y,z,w). Anim override is WXYZ.
        if idx not in rot:
            q = br
        p = sample_track(loc.get(idx), frame, bp)
        s = sample_track(scl.get(idx), frame, bs)
        local = mat4_from_trs(p, q, s)
        if b.parent_index is None:
            world = local
        else:
            world = resolve(b.parent_index) @ local
        cache[idx] = world
        return world

    return [resolve(i) for i in range(len(bones))]


def mesh_tint(g, mesh):
    """Diffuse times emissive. Effect cards with no albedo get their color here.
    Alpha 0 or a black diffuse is a card the shader hides."""
    mat = g.materials[mesh.data_index]
    diffuse = (1.0, 1.0, 1.0, 1.0)
    emissive = (1.0, 1.0, 1.0)
    for u in mat.uniforms:
        if u.uniform_name == "DiffuseAlpha" and len(u.values) >= 4:
            diffuse = tuple(float(x) for x in u.values[:4])
        elif u.uniform_name == "EmissiveIntensity" and len(u.values) >= 3:
            emissive = tuple(float(x) for x in u.values[:3])
    rgb = tuple(max(0.0, diffuse[i] * emissive[i]) for i in range(3))
    if diffuse[3] < 0.05 or max(rgb) < 0.02:
        return None
    return rgb


def _color_map(stem: str) -> bool:
    if stem.startswith("ef_nml"):
        return False
    # Rock slots here are normals and roughness, not the skill color.
    if stem.startswith("ef_roc") and stem[-1:] in ("n", "r"):
        return False
    return True


def mesh_texture(g, mesh) -> str | None:
    mat = g.materials[mesh.data_index]
    stems = []
    for b in mat.bindings:
        if b.texture_name:
            stems.append(b.texture_name)
    if not stems and callable(mat.texture_stems):
        stems = [s for s in mat.texture_stems() if s]
    stems = [stem for stem in stems if _color_map(stem)]
    for prefer in PREFER_TEX:
        if prefer in stems:
            return prefer
    # Fire clips used to be the only ones that kept a texture. Water, thunder,
    # leaf, and dark cards name theirs ef_wtr / ef_ene / ef_lef and were baked
    # as flat boxes.
    color = (
        "ef_fir",
        "ef_wtr",
        "ef_wnd",
        "ef_lef",
        "ef_ene",
        "ef_emi",
        "ef_lig",
        "ef_gra",
        "ef_dis",
        "ef_smk",
        "ef_bur",
        "ef_bul",
        "ef_lin",
    )
    for prefix in color:
        for stem in stems:
            if stem.startswith(prefix):
                return stem
    for stem in stems:
        if stem.startswith("ef_") and not stem.startswith("ef_nml"):
            return stem
    return None


def decode_tex(stem: str) -> np.ndarray | None:
    path = UNPACKED / "images" / f"{stem}.img"
    if not path.is_file():
        return None
    from PIL import Image

    img = _decode_dds_dx10(path)
    if img is None:
        try:
            img = Image.open(path)
        except OSError:
            return None
    arr = np.asarray(img.convert("RGBA"))
    # Cap so the pack stays small.
    h, w = arr.shape[:2]
    step = 1
    while max(h, w) // step > 256:
        step *= 2
    if step > 1:
        arr = arr[::step, ::step]
    return np.ascontiguousarray(arr)


def ensure_textures(stems: list[str]) -> None:
    if not GAMEDATA.is_file():
        return
    from dsts_extractor.mvgl import MvglArchive

    wanted = [f"images/{s}.img" for s in stems]
    with MvglArchive(GAMEDATA) as arc:
        (UNPACKED / "images").mkdir(parents=True, exist_ok=True)
        for name in wanted:
            if name not in arc.files:
                print("  no image", name)
                continue
            out = UNPACKED / name
            if not out.is_file():
                print("  extract", name)
                arc.extract_file(name, out)


def write_dmm(path: Path, pos, nrm, uv, jnt, wgt, idx, batches, images, palettes, duration):
    nframes = len(palettes)
    nbones = palettes[0].shape[0] // 16
    out = bytearray()
    out += b"DMM1"
    out += struct.pack("<IIHHHH", len(pos), len(idx), nbones, len(images), len(batches), 1)
    out += struct.pack("<fff", 0.0, 1.0, 1.0)
    for i in range(len(pos)):
        out += struct.pack(
            "<8f4B4f",
            float(pos[i, 0]), float(pos[i, 1]), float(pos[i, 2]),
            float(nrm[i, 0]), float(nrm[i, 1]), float(nrm[i, 2]),
            float(uv[i, 0]), float(uv[i, 1]),
            int(jnt[i, 0]), int(jnt[i, 1]), int(jnt[i, 2]), int(jnt[i, 3]),
            float(wgt[i, 0]), float(wgt[i, 1]), float(wgt[i, 2]), float(wgt[i, 3]),
        )
    out += idx.astype(np.uint32).tobytes()
    cursor = 0
    for count, slot in batches:
        out += struct.pack("<III", cursor, count, slot)
        cursor += count
    for arr in images:
        h, w = arr.shape[0], arr.shape[1]
        out += struct.pack("<HH", w, h)
        out += arr.tobytes()
    out += b"gfi_s02".ljust(32, b"\0")
    out += struct.pack("<fH", float(duration), int(nframes))
    for pal in palettes:
        out += pal.astype(np.float32).tobytes()
    path.write_bytes(out)


def main() -> None:
    g = geom.read_geom(UNPACKED / f"{STEM}.geom")
    header, rot, loc, scl = load_anim(UNPACKED / f"{STEM}.anim")
    retarget_meteor(g.bones, loc)
    print(f"anim {header.duration_seconds:.2f}s  bones {header.num_bones}  frames {header.total_frames}")

    meshes = []
    for m in g.meshes:
        if not m.weighted_bone_indices or "Position" not in m.streams:
            continue
        bone = int(m.weighted_bone_indices[0])
        if g.bones[bone].name.startswith(SKIP_PREFIX):
            continue
        tint = mesh_tint(g, m)
        if tint is None:
            continue
        tris = geom.triangles(m)
        if not tris:
            continue
        meshes.append((m, bone, mesh_texture(g, m), tint, tris))
    print(f"meshes {len(meshes)}")

    tex_names = ["__white__"]
    for _m, _b, tex, _tint, _t in meshes:
        if tex and tex not in tex_names and len(tex_names) < MAX_TEX:
            tex_names.append(tex)
    ensure_textures([n for n in tex_names if n != "__white__"])
    images = [np.full((4, 4, 4), 255, np.uint8)]
    images[0][:, :, 3] = 200
    for name in tex_names[1:]:
        arr = decode_tex(name)
        if arr is None:
            print("  missing tex", name)
            images.append(images[0])
        else:
            print(f"  tex {name} {arr.shape[1]}x{arr.shape[0]}")
            images.append(arr)
    tex_slot = {name: i for i, name in enumerate(tex_names)}

    # Group by mesh bone so each part stays within the bone cap.
    bone_ids = []
    for _m, bone, _tex, _tint, _tris in meshes:
        if bone not in bone_ids:
            bone_ids.append(bone)
    parts = [bone_ids[i : i + MAX_BONES] for i in range(0, len(bone_ids), MAX_BONES)]
    print(f"parts {len(parts)} bones {len(bone_ids)}")

    duration = float(header.duration_seconds)
    frames = [int(round(header.total_frames * i / (SAMPLE_FRAMES - 1))) for i in range(SAMPLE_FRAMES)]
    frames = [min(header.total_frames - 1, f) for f in frames]
    worlds_by_frame = [bone_worlds(g.bones, rot, loc, scl, f) for f in frames]

    # Extent of a mid frame, in effect-local meters, so the client scale is sane.
    mid = worlds_by_frame[len(worlds_by_frame) // 2]
    pts = []
    for m, bone, _tex, _tint, _tris in meshes[:80]:
        pos = np.array(m.streams["Position"], dtype=np.float64)[:, :3]
        pts.append((mid[bone][:3, :3] @ pos.T).T + mid[bone][:3, 3])
    if pts:
        allp = np.concatenate(pts, axis=0)
        print("mid bbox", allp.min(0), allp.max(0))

    TSDAT.mkdir(parents=True, exist_ok=True)
    # Drop older parts so a shorter bake doesn't leave stale files.
    for old in TSDAT.glob("GFI*.DMM"):
        old.unlink()

    for pi, group in enumerate(parts):
        remap = {b: i for i, b in enumerate(group)}
        pos_all = []
        nrm_all = []
        uv_all = []
        jnt_all = []
        wgt_all = []
        idx_all = []
        batch_map: dict[int, list[int]] = {}
        base = 0
        for m, bone, tex, tint, tris in meshes:
            if bone not in remap:
                continue
            pos = np.array(m.streams["Position"], dtype=np.float32)[:, :3]
            if "UV" in m.streams:
                uv = np.array(m.streams["UV"], dtype=np.float32)[:, :2]
            else:
                uv = np.zeros((len(pos), 2), np.float32)
            nrm = np.zeros_like(pos)
            nrm[:, 0] = tint[0]
            nrm[:, 1] = tint[1]
            nrm[:, 2] = tint[2]
            slot = tex_slot.get(tex, 0)
            local_idx = []
            for a, b, c in tris:
                local_idx.extend((a + base, b + base, c + base))
            batch_map.setdefault(slot, []).extend(local_idx)
            j = remap[bone]
            joints = np.zeros((len(pos), 4), np.uint8)
            joints[:, 0] = j
            weights = np.zeros((len(pos), 4), np.float32)
            weights[:, 0] = 1.0
            pos_all.append(pos)
            nrm_all.append(nrm)
            uv_all.append(uv)
            jnt_all.append(joints)
            wgt_all.append(weights)
            base += len(pos)
        if base == 0:
            continue
        pos = np.concatenate(pos_all)
        nrm = np.concatenate(nrm_all)
        uv = np.concatenate(uv_all)
        jnt = np.concatenate(jnt_all)
        wgt = np.concatenate(wgt_all)
        idx = []
        batches = []
        for slot, inds in batch_map.items():
            batches.append((len(inds), slot))
            idx.extend(inds)
        idx = np.array(idx, np.uint32)
        palettes = []
        for worlds in worlds_by_frame:
            mats = []
            for b in group:
                mats.append(worlds[b].flatten(order="F").astype(np.float32))
            palettes.append(np.concatenate(mats))
        dest = TSDAT / f"GFI{pi}.DMM"
        write_dmm(dest, pos, nrm, uv, jnt, wgt, idx, batches, images, palettes, duration)
        print(f"wrote {dest.name} verts {len(pos)} bones {len(group)} batches {len(batches)} {dest.stat().st_size/1e6:.2f} MB")


if __name__ == "__main__":
    main()
