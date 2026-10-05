#!/usr/bin/env python3
"""Bake Time Stranger Digimon + a battle field into DWM/DWS for the C arena view.

Reads the unpacked dsts tree (and all .anim clips). Writes:
  private/reference/time-stranger/baked/{agumon,gabumon}.dwm
  private/reference/time-stranger/baked/d0172b.dws
  assets/extracted/us/TSDAT/  (so dwe_fs_read can see them)

DWM1 = every clip, 8 yaw banks, atlas per clip.
DWS1 = clipped/scaled battle-field mesh in DW1 arena units.
"""
from __future__ import annotations

import argparse
import math
import shutil
import struct
import sys
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
UNPACKED = ROOT / "private/reference/time-stranger/unpacked"
BAKED = ROOT / "private/reference/time-stranger/baked"
TSDAT = ROOT / "assets/extracted/us/TSDAT"

TILE = 96
YAW_BANKS = 8
FRAMES = 3
PLAY_XZ = 110.0  # TS metres kept around the fight ring
ARENA_RADIUS = 2600.0
MAX_MAP_TRIS = 18000
MAX_FIGHTER_TRIS = 700
MAX_TEX = 256


def _read_geom(path: Path, field: bool = False):
    import dsts_extractor.geom as geom_mod

    old = geom_mod._ENGINE_REMAP_TABLE_SIZE
    if field:
        geom_mod._ENGINE_REMAP_TABLE_SIZE = 0
    try:
        return geom_mod.read_geom(path)
    finally:
        geom_mod._ENGINE_REMAP_TABLE_SIZE = old


def _quat_mul_xyzw(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (
        aw * bx + ax * bw + ay * bz - az * by,
        aw * by - ax * bz + ay * bw + az * bx,
        aw * bz + ax * by - ay * bx + az * bw,
        aw * bw - ax * bx - ay * by - az * bz,
    )


def _quat_to_mat3(q):
    x, y, z, w = q
    n = math.sqrt(x * x + y * y + z * z + w * w) or 1.0
    x, y, z, w = x / n, y / n, z / n, w / n
    xx, yy, zz = x * x, y * y, z * z
    xy, xz, yz = x * y, x * z, y * z
    wx, wy, wz = w * x, w * y, w * z
    return np.array(
        [
            [1 - 2 * (yy + zz), 2 * (xy - wz), 2 * (xz + wy)],
            [2 * (xy + wz), 1 - 2 * (xx + zz), 2 * (yz - wx)],
            [2 * (xz - wy), 2 * (yz + wx), 1 - 2 * (xx + yy)],
        ],
        dtype=np.float64,
    )


def _trs(t, q, s):
    m = np.identity(4, dtype=np.float64)
    m[:3, :3] = _quat_to_mat3(q) * np.asarray(s, dtype=np.float64)
    m[:3, 3] = t
    return m


def _ibm_mat(ibm12):
    m = np.identity(4, dtype=np.float64)
    rows = np.asarray(ibm12, dtype=np.float64).reshape(3, 4)
    m[:3, :] = rows
    return m


def _slerp(a, b, t):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    dot = ax * bx + ay * by + az * bz + aw * bw
    if dot < 0:
        bx, by, bz, bw, dot = -bx, -by, -bz, -bw, -dot
    if dot > 0.9995:
        q = np.array([ax + t * (bx - ax), ay + t * (by - ay), az + t * (bz - az), aw + t * (bw - aw)])
        q /= np.linalg.norm(q) or 1.0
        return tuple(q)
    theta = math.acos(min(1.0, max(-1.0, dot)))
    s = math.sin(theta)
    w0 = math.sin((1 - t) * theta) / s
    w1 = math.sin(t * theta) / s
    return (ax * w0 + bx * w1, ay * w0 + by * w1, az * w0 + bz * w1, aw * w0 + bw * w1)


def _lerp3(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def _sample_track(samples, frame, is_quat):
    if not samples:
        return None
    if frame <= samples[0][0]:
        return samples[0][1]
    if frame >= samples[-1][0]:
        return samples[-1][1]
    for i in range(1, len(samples)):
        f0, v0 = samples[i - 1]
        f1, v1 = samples[i]
        if frame <= f1:
            t = 0.0 if f1 == f0 else (frame - f0) / (f1 - f0)
            return _slerp(v0, v1, t) if is_quat else _lerp3(v0, v1, t)
    return samples[-1][1]


def _bind_locals(bones):
    locals_ = []
    for b in bones:
        q = b.bind_pose.rotation  # xyzw
        locals_.append(_trs(b.bind_pose.position, q, b.bind_pose.scale))
    return locals_


def _worlds(bones, locals_):
    worlds = [np.identity(4) for _ in bones]
    for b in bones:
        i = b.index
        p = b.parent_index
        worlds[i] = locals_[i] if p is None else worlds[p] @ locals_[i]
    return worlds


def _skin_matrices(bones, worlds):
    out = []
    for b, w in zip(bones, worlds):
        out.append(w @ _ibm_mat(b.inverse_bind_matrix))
    return out


def _open_tex(images: Path, stem: str) -> Image.Image | None:
    if not stem:
        return None
    for name in (stem, stem.lower(), stem.upper()):
        for ext in (".img", ".dds", ".png"):
            p = images / f"{name}{ext}"
            if p.is_file():
                try:
                    return Image.open(p).convert("RGBA")
                except Exception:
                    return None
    return None


def _albedo_stem(mat) -> str | None:
    prefer, fallback = [], []
    for b in mat.bindings:
        n = (b.texture_name or "").lower()
        if not n:
            continue
        if n.endswith(("_n", "_na", "_rm", "_em", "_e")):
            continue
        if n.endswith(("_c", "_ca")) or n.endswith("_c.img"):
            prefer.append(b.texture_name)
        else:
            fallback.append(b.texture_name)
    return (prefer or fallback or [None])[0]


def _mesh_influences(mesh, bones):
    from dsts_extractor.native_export import _skin_influences

    return _skin_influences(mesh, bones)


def _skin_mesh(mesh, bones, skins, geom_mod):
    pos = np.array([v[:3] for v in mesh.streams["Position"]], dtype=np.float64)
    joints, weights = _mesh_influences(mesh, bones)
    out = np.zeros_like(pos)
    for i in range(len(pos)):
        acc = np.zeros(4)
        p = np.array([pos[i, 0], pos[i, 1], pos[i, 2], 1.0])
        for k in range(4):
            w = weights[i][k]
            if w <= 0:
                continue
            acc += w * (skins[int(joints[i][k])] @ p)
        out[i] = acc[:3]
    nrm = None
    if "Normal" in mesh.streams:
        nrm = np.array([v[:3] for v in mesh.streams["Normal"]], dtype=np.float64)
    uv = None
    if "UV" in mesh.streams:
        uv = np.array([v[:2] for v in mesh.streams["UV"]], dtype=np.float64)
    tris = np.array(geom_mod.triangles(mesh), dtype=np.int32)
    return out, nrm, uv, tris


def _raster(verts, nrm, uv, tris, albedo: Image.Image | None, yaw, w, h):
    """Orthographic-ish perspective raster. yaw rotates the posed mesh around Y."""
    c, s = math.cos(yaw), math.sin(yaw)
    rot = np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]], dtype=np.float64)
    v = verts @ rot.T
    n = None if nrm is None else nrm @ rot.T
    # Fit in frame: look from +Z at the model, Y up.
    mins, maxs = v.min(0), v.max(0)
    span = max(float(maxs[1] - mins[1]), 0.35)
    eye = np.array([0.0, (mins[1] + maxs[1]) * 0.5, maxs[2] + span * 1.8])
    light = np.array([0.35, 0.85, 0.4])
    light /= np.linalg.norm(light)
    img = np.zeros((h, w, 4), dtype=np.uint8)
    zbuf = np.full((h, w), 1.0e9, dtype=np.float32)
    tex = np.asarray(albedo) if albedo is not None else None
    tw, th = (tex.shape[1], tex.shape[0]) if tex is not None else (1, 1)
    proj = (h * 0.42) * span / max(span, 1e-3) * (1.15 / span)
    # scale so model height fills ~78% of the tile
    proj = (h * 0.78) / span

    for t in tris:
        p = v[t]
        cam = p - eye
        if np.any(cam[:, 2] >= -0.05):
            # camera looks -Z toward origin; our eye is at +Z looking at model
            pass
        # Simple camera: x right, y up, z toward camera from model...
        # Project as if camera at +Z looking to -Z: sx = x/z_cam
        zc = eye[2] - p[:, 2]
        if np.any(zc <= 0.05):
            continue
        xs = w * 0.5 + p[:, 0] * proj
        ys = h * 0.82 - (p[:, 1] - mins[1]) * proj  # feet near bottom
        zs = zc
        minx = max(0, int(np.min(xs)) - 1)
        maxx = min(w - 1, int(np.max(xs)) + 1)
        miny = max(0, int(np.min(ys)) - 1)
        maxy = min(h - 1, int(np.max(ys)) + 1)
        if minx >= maxx or miny >= maxy:
            continue
        x0, x1, x2 = xs
        y0, y1, y2 = ys
        denom = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
        if abs(denom) < 1e-8:
            continue
        shade = 0.7
        if n is not None:
            nn = n[t].mean(0)
            ln = np.linalg.norm(nn)
            if ln > 1e-6:
                shade = 0.35 + 0.65 * max(0.0, float(nn.dot(light) / ln))
        uvs = uv[t] if uv is not None else None
        for y in range(miny, maxy + 1):
            for x in range(minx, maxx + 1):
                wa = ((y1 - y2) * (x - x2) + (x2 - x1) * (y - y2)) / denom
                wb = ((y2 - y0) * (x - x2) + (x0 - x2) * (y - y2)) / denom
                wc = 1.0 - wa - wb
                if wa < 0 or wb < 0 or wc < 0:
                    continue
                z = wa * zs[0] + wb * zs[1] + wc * zs[2]
                if z >= zbuf[y, x]:
                    continue
                if tex is not None and uvs is not None:
                    uu = wa * uvs[0, 0] + wb * uvs[1, 0] + wc * uvs[2, 0]
                    vv = wa * uvs[0, 1] + wb * uvs[1, 1] + wc * uvs[2, 1]
                    uu = uu - math.floor(uu)
                    vv = vv - math.floor(vv)
                    tx = min(tw - 1, max(0, int(uu * tw)))
                    ty = min(th - 1, max(0, int((1.0 - vv) * th)))
                    col = tex[ty, tx]
                    if col[3] < 16:
                        continue
                    rgb = (col[:3].astype(np.float32) * shade).clip(0, 255).astype(np.uint8)
                    img[y, x, 0:3] = rgb
                    img[y, x, 3] = 255
                else:
                    g = int(200 * shade)
                    img[y, x] = (g, int(g * 0.85), int(g * 0.55), 255)
                zbuf[y, x] = z
    return img


def _foot_row(img: np.ndarray) -> int:
    rows = np.where(img[:, :, 3] > 0)[0]
    return int(rows.max()) if len(rows) else img.shape[0] // 2


def _clip_name(path: Path, stem: str) -> str:
    s = path.stem
    if s == stem:
        return "rest"
    prefix = stem + "_"
    return s[len(prefix) :] if s.startswith(prefix) else s


def _anim_paths(data: Path, stem: str) -> list[Path]:
    out = []
    for p in sorted(data.glob(f"{stem}*.anim")):
        if p.stem.startswith(f"{stem}loc") or p.stem.startswith(f"{stem}_lod_"):
            continue
        out.append(p)
    return out


def _pose_locals(bones, bind_locals, rot, loc, scl, frame):
    locals_ = [m.copy() for m in bind_locals]
    for i, b in enumerate(bones):
        q = b.bind_pose.rotation
        t = b.bind_pose.position
        s = b.bind_pose.scale
        rd = _sample_track(rot.get(i, []), frame, True)
        ld = _sample_track(loc.get(i, []), frame, False)
        sd = _sample_track(scl.get(i, []), frame, False)
        if rd is not None:
            # file quats are WXYZ → xyzw
            w, x, y, z = rd
            q = _quat_mul_xyzw(b.bind_pose.rotation, (x, y, z, w))
        if ld is not None:
            t = tuple(b.bind_pose.position[k] + ld[k] for k in range(3))
        if sd is not None:
            s = tuple(b.bind_pose.scale[k] * sd[k] for k in range(3))
        locals_[i] = _trs(t, q, s)
    return locals_


def _collect_body_meshes(g, geom_mod, images: Path):
    meshes = []
    for i, m in enumerate(g.meshes):
        name = (g.mesh_names[i] if i < len(g.mesh_names) else m.material_name or "").lower()
        mat_name = (m.material_name or "").lower()
        if "outline" in name or "outline" in mat_name or "line" == mat_name:
            continue
        if m.num_vertices < 3 or "Position" not in m.streams:
            continue
        mat = g.materials[m.data_index] if m.data_index < len(g.materials) else None
        stem = _albedo_stem(mat) if mat else None
        albedo = _open_tex(images, stem) if stem else None
        if albedo is not None and max(albedo.size) > 512:
            albedo = albedo.resize((min(512, albedo.size[0]), min(512, albedo.size[1])), Image.BILINEAR)
        try:
            ntri = len(geom_mod.triangles(m))
        except Exception:
            continue
        # Keep every mesh. Only stride shells past 8k tris (these two are under that).
        step = 1 if ntri <= 8000 else max(1, math.ceil(ntri / 8000))
        meshes.append((m, albedo, step, ntri))
    print(
        "  meshes",
        ", ".join(f"{m.material_name or '?'} tris~{n}//{step}" for m, _a, step, n in meshes),
    )
    return [(m, albedo, step) for m, albedo, step, _n in meshes]


def bake_fighter(stem: str, slug: str, data: Path, images: Path, dest: Path) -> Path:
    from dsts_extractor import anim as anim_mod
    from dsts_extractor import anim_build, geom as geom_mod

    g = _read_geom(data / f"{stem}.geom", field=False)
    bind = _bind_locals(g.bones)
    body = _collect_body_meshes(g, geom_mod, images)
    print(f"{slug}: {len(g.bones)} bones, {len(body)} meshes, {len(_anim_paths(data, stem))} clips")

    clips = []
    for ap in _anim_paths(data, stem):
        name = _clip_name(ap, stem)
        try:
            af = anim_mod.read_anim(ap)
        except Exception as e:
            print(f"  skip {name}: {e}")
            continue
        rot, loc, scl = anim_build._collect_tracks(af, g.bones, 0.5)
        total = max(1, af.header.total_frames)
        take = min(FRAMES, total)
        frames = [0] if take == 1 else [int(i * (total - 1) / (take - 1)) for i in range(take)]
        atlases = []
        feet = [TILE // 2] * YAW_BANKS
        print(f"  clip {name} frames={frames} ({af.header.duration_seconds:.2f}s)")
        for fi, fr in enumerate(frames):
            locals_ = _pose_locals(g.bones, bind, rot, loc, scl, float(fr))
            worlds = _worlds(g.bones, locals_)
            skins = _skin_matrices(g.bones, worlds)
            posed = []
            for mesh, albedo, step in body:
                try:
                    verts, nrm, uv, tris = _skin_mesh(mesh, g.bones, skins, geom_mod)
                except Exception:
                    continue
                if step > 1:
                    tris = tris[::step]
                posed.append((verts, nrm, uv, tris, albedo))
            if not posed:
                continue
            all_v = np.concatenate([p[0] for p in posed], 0)
            for yaw_i in range(YAW_BANKS):
                yaw = 2 * math.pi * yaw_i / YAW_BANKS
                tile = np.zeros((TILE, TILE, 4), dtype=np.uint8)
                # raster each mesh into the same tile
                # combine by z — call raster per mesh then composite by alpha
                acc = np.zeros((TILE, TILE, 4), dtype=np.uint8)
                zbest = np.full((TILE, TILE), 1.0e9, dtype=np.float32)
                # cheaper: raster concatenated? different albedos — per mesh
                for verts, nrm, uv, tris, albedo in posed:
                    part = _raster(verts, nrm, uv, tris, albedo, yaw, TILE, TILE)
                    mask = part[:, :, 3] > acc[:, :, 3]
                    acc[mask] = part[mask]
                atlases.append((yaw_i, fi, acc))
                fr = _foot_row(acc)
                if fr > feet[yaw_i]:
                    feet[yaw_i] = fr
        if not atlases:
            continue
        # pack atlas: columns = frames, rows = yaw
        nfr = take
        atlas = np.zeros((TILE * YAW_BANKS, TILE * nfr, 4), dtype=np.uint8)
        for yaw_i, fi, tile in atlases:
            atlas[yaw_i * TILE : (yaw_i + 1) * TILE, fi * TILE : (fi + 1) * TILE] = tile
        clips.append((name, nfr, feet, atlas))

    if not clips:
        raise SystemExit(f"no clips baked for {slug}")

    # units_per_px from rest / first clip height
    first = clips[0][3]
    # measure idle-ish
    pick = next((c for c in clips if c[0] in ("bs01", "bg01", "rest")), clips[0])
    tile0 = pick[3][0:TILE, 0:TILE]
    rows = np.where(tile0[:, :, 3] > 0)[0]
    px_h = float(rows.max() - rows.min() + 1) if len(rows) else float(TILE)
    # Agumon ~1.1 m; view scales by loadout height_u / this
    units_per_px = 1.10 / px_h  # metres per sprite px; view remaps via height_u

    dest.parent.mkdir(parents=True, exist_ok=True)
    blob = bytearray()
    blob += b"DWM1"
    blob += struct.pack("<HHHH", TILE, TILE, YAW_BANKS, len(clips))
    blob += struct.pack("<f", units_per_px)
    for name, nfr, feet, atlas in clips:
        raw = atlas.tobytes()
        blob += name.encode("ascii")[:31].ljust(32, b"\0")
        blob += struct.pack("<H", nfr)
        for y in range(YAW_BANKS):
            blob += struct.pack("<H", feet[y])
        blob += struct.pack("<I", len(raw))
        blob += raw
    dest.write_bytes(blob)
    print(f"wrote {dest} ({dest.stat().st_size / 1e6:.1f} MB, {len(clips)} clips)")
    return dest


def bake_map(stem: str, data: Path, images: Path, dest: Path) -> Path:
    import dsts_extractor.geom as geom_mod

    g = _read_geom(data / f"{stem}.geom", field=True)
    print(f"map {stem}: {len(g.meshes)} meshes {len(g.bones)} bones")

    batches = []  # (verts Nx5, idx, tex_rgba, tw, th)
    tex_cache: dict[str, tuple[np.ndarray, int, int]] = {}

    def tex_of(stem_name):
        if stem_name in tex_cache:
            return tex_cache[stem_name]
        im = _open_tex(images, stem_name)
        if im is None:
            arr = np.full((4, 4, 4), 140, dtype=np.uint8)
            arr[:, :, 3] = 255
            tex_cache[stem_name] = (arr, 4, 4)
            return tex_cache[stem_name]
        if max(im.size) > MAX_TEX:
            im = im.resize((min(MAX_TEX, im.size[0]), min(MAX_TEX, im.size[1])), Image.BILINEAR)
        arr = np.asarray(im.convert("RGBA"))
        tex_cache[stem_name] = (arr, arr.shape[1], arr.shape[0])
        return tex_cache[stem_name]

    kept_tris = 0
    for i, m in enumerate(g.meshes):
        if "Position" not in m.streams or m.num_vertices < 3:
            continue
        name = (g.mesh_names[i] if i < len(g.mesh_names) else "") or (m.material_name or "")
        low = name.lower()
        if any(k in low for k in ("ani_add", "ani_alp", "fireline", "sky")):
            continue
        pos = np.array([v[:3] for v in m.streams["Position"]], dtype=np.float32)
        uv = (
            np.array([v[:2] for v in m.streams["UV"]], dtype=np.float32)
            if "UV" in m.streams
            else np.zeros((len(pos), 2), np.float32)
        )
        try:
            tris = np.array(geom_mod.triangles(m), dtype=np.int32)
        except Exception:
            continue
        if len(tris) == 0:
            continue
        def in_bowl(v):
            return (np.hypot(v[:, 0], v[:, 2]) < PLAY_XZ) & (np.abs(v[:, 1]) < 400.0)

        keep = in_bowl(pos[tris[:, 0]]) & in_bowl(pos[tris[:, 1]]) & in_bowl(pos[tris[:, 2]])
        tris = tris[keep]
        if len(tris) == 0:
            continue
        used = np.unique(tris)
        remap = -np.ones(len(pos), dtype=np.int32)
        remap[used] = np.arange(len(used), dtype=np.int32)
        vp = pos[used]
        vu = uv[used]
        idx = remap[tris].reshape(-1)
        mat = g.materials[m.data_index] if m.data_index < len(g.materials) else None
        stem_name = _albedo_stem(mat) if mat else None
        arr, tw, th = tex_of(stem_name or "")
        verts = np.concatenate([vp, vu], axis=1)
        batches.append((verts, idx.astype(np.uint32), stem_name or "", arr, tw, th))
        kept_tris += len(tris)

    if kept_tris > MAX_MAP_TRIS:
        # stride triangles globally
        stride = int(math.ceil(kept_tris / MAX_MAP_TRIS))
        slim = []
        for verts, idx, key, arr, tw, th in batches:
            idx = idx.reshape(-1, 3)[::stride].reshape(-1)
            if len(idx) >= 3:
                slim.append((verts, idx, key, arr, tw, th))
        batches = slim
        kept_tris = sum(len(b[1]) // 3 for b in batches)
    print(f"  kept ~{kept_tris} tris in {len(batches)} batches")

    if not batches:
        raise SystemExit("map bake produced no triangles")

    all_v = np.concatenate([b[0][:, :3] for b in batches], 0)
    # Floor/center from the fight bowl only — the skybox min-Y is thousands
    # of units below the ring and must not drive the transform.
    xz0 = np.hypot(all_v[:, 0], all_v[:, 2])
    near = all_v[xz0 < PLAY_XZ]
    if len(near) < 16:
        near = all_v
    y0 = float(np.percentile(near[:, 1], 6))
    cx = float(np.median(near[:, 0]))
    cz = float(np.median(near[:, 2]))
    rad = float(np.percentile(np.hypot(near[:, 0] - cx, near[:, 2] - cz), 90))
    scale = ARENA_RADIUS / max(rad, 1.0)
    print(f"  center=({cx:.1f},{y0:.1f},{cz:.1f}) rad={rad:.1f} scale={scale:.2f} near={len(near)}")

    # pack unique textures
    tex_keys = []
    tex_map = {}
    for _v, _i, key, arr, tw, th in batches:
        if key not in tex_map:
            tex_map[key] = len(tex_keys)
            tex_keys.append((arr, tw, th))

    dest.parent.mkdir(parents=True, exist_ok=True)
    xyzuv = []
    idx_all = []
    batch_rec = []
    vbase = 0
    ibase = 0
    for verts, idx, key, arr, tw, th in batches:
        v = verts.copy()
        v[:, 0] = (v[:, 0] - cx) * scale
        v[:, 1] = (v[:, 1] - y0) * scale
        v[:, 2] = (v[:, 2] - cz) * scale
        xyzuv.append(v)
        idx_all.append(idx + np.uint32(vbase))
        batch_rec.append((ibase, len(idx), tex_map[key]))
        vbase += len(v)
        ibase += len(idx)
    xyzuv = np.concatenate(xyzuv, 0).astype(np.float32)
    idx_all = np.concatenate(idx_all, 0).astype(np.uint32)

    blob = bytearray()
    blob += b"DWS1"
    blob += struct.pack("<IIII", len(xyzuv), len(idx_all), len(tex_keys), len(batch_rec))
    blob += struct.pack("<I", 0)  # pad / flags
    blob += xyzuv.tobytes()
    blob += idx_all.tobytes()
    for arr, tw, th in tex_keys:
        blob += struct.pack("<HH", tw, th)
        blob += np.ascontiguousarray(arr).tobytes()
    for first, count, ti in batch_rec:
        blob += struct.pack("<III", first, count, ti)
    dest.write_bytes(blob)
    print(f"wrote {dest} ({dest.stat().st_size / 1e6:.1f} MB)")
    return dest


def _install(src: Path):
    TSDAT.mkdir(parents=True, exist_ok=True)
    dst = TSDAT / src.name.upper()
    shutil.copy2(src, dst)
    print(f"installed {dst}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--mons-only", action="store_true")
    ap.add_argument("--map-only", action="store_true")
    ap.add_argument("--data", type=Path, default=UNPACKED)
    args = ap.parse_args()
    images = args.data / "images"
    BAKED.mkdir(parents=True, exist_ok=True)

    if not args.map_only:
        for stem, slug, out in (
            ("chr050", "agumon", BAKED / "agumon.dwm"),
            ("chr151", "gabumon", BAKED / "gabumon.dwm"),
        ):
            bake_fighter(stem, slug, args.data, images, out)
            _install(out)
            # also AGUM.DWM / GABU.DWM names the view looks for
            alias = TSDAT / ("AGUM.DWM" if slug == "agumon" else "GABU.DWM")
            shutil.copy2(out, alias)
            print(f"installed {alias}")

    if not args.mons_only:
        p = bake_map("d0172b", args.data, images, BAKED / "d0172b.dws")
        _install(p)
        shutil.copy2(p, TSDAT / "D0172B.DWS")
        print(f"installed {TSDAT / 'D0172B.DWS'}")


if __name__ == "__main__":
    main()
