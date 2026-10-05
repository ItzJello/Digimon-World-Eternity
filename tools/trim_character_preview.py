#!/usr/bin/env python3
"""Write a character-creation preview GLB: mesh, textures, and one idle clip.

The source outfits are ~50MB because they carry 900+ animation clips. The
creation screen only plays the idle, so the preview copy drops the rest.
"""

import json
import os
import struct
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
SRC = os.path.join(ROOT, "src", "assets", "models", "characters")
DST = os.path.join(SRC, "preview")


def trim(src_path, dst_path):
    data = open(src_path, "rb").read()
    if data[:4] != b"glTF":
        raise SystemExit(f"not a glb: {src_path}")
    json_len, json_type = struct.unpack_from("<II", data, 12)
    if json_type != 0x4E4F534A:
        raise SystemExit(f"missing json chunk: {src_path}")
    js = json.loads(data[20 : 20 + json_len])
    bin_off = 20 + json_len
    bin_len, bin_type = struct.unpack_from("<II", data, bin_off)
    if bin_type != 0x004E4942:
        raise SystemExit(f"missing bin chunk: {src_path}")
    blob = data[bin_off + 8 : bin_off + 8 + bin_len]

    keep = None
    for anim in js.get("animations", []):
        if anim.get("name", "").endswith("_fn01_01"):
            keep = anim
            break
    if keep is None:
        for anim in js.get("animations", []):
            if "fn01_01" in anim.get("name", ""):
                keep = anim
                break

    used_acc = set()

    def add_acc(index):
        if index is not None:
            used_acc.add(index)

    for mesh in js.get("meshes", []):
        for prim in mesh.get("primitives", []):
            for index in prim.get("attributes", {}).values():
                add_acc(index)
            add_acc(prim.get("indices"))
            for target in prim.get("targets", []):
                for index in target.values():
                    add_acc(index)
    for skin in js.get("skins", []):
        add_acc(skin.get("inverseBindMatrices"))
    if keep is not None:
        for sampler in keep.get("samplers", []):
            add_acc(sampler.get("input"))
            add_acc(sampler.get("output"))

    views = js.get("bufferViews", [])
    used_views = set()
    for index in used_acc:
        view = js["accessors"][index].get("bufferView")
        if view is not None:
            used_views.add(view)
    for image in js.get("images", []):
        if "bufferView" in image:
            used_views.add(image["bufferView"])

    view_map = {}
    new_views = []
    new_blob = bytearray()
    for old_index, view in enumerate(views):
        if old_index not in used_views:
            continue
        while len(new_blob) % 4:
            new_blob.append(0)
        start = view.get("byteOffset", 0)
        length = view["byteLength"]
        view_map[old_index] = len(new_views)
        copied = dict(view)
        copied["byteOffset"] = len(new_blob)
        copied["buffer"] = 0
        new_views.append(copied)
        new_blob += blob[start : start + length]

    acc_map = {}
    new_accessors = []
    for old_index in sorted(used_acc):
        acc_map[old_index] = len(new_accessors)
        accessor = dict(js["accessors"][old_index])
        if "bufferView" in accessor:
            accessor["bufferView"] = view_map[accessor["bufferView"]]
        new_accessors.append(accessor)

    def remap(index):
        return acc_map[index]

    for mesh in js.get("meshes", []):
        for prim in mesh.get("primitives", []):
            prim["attributes"] = {key: remap(value) for key, value in prim["attributes"].items()}
            if "indices" in prim:
                prim["indices"] = remap(prim["indices"])
            if "targets" in prim:
                prim["targets"] = [
                    {key: remap(value) for key, value in target.items()}
                    for target in prim["targets"]
                ]
    for skin in js.get("skins", []):
        if "inverseBindMatrices" in skin:
            skin["inverseBindMatrices"] = remap(skin["inverseBindMatrices"])
    if keep is not None:
        for sampler in keep["samplers"]:
            sampler["input"] = remap(sampler["input"])
            sampler["output"] = remap(sampler["output"])
        js["animations"] = [keep]
    else:
        js.pop("animations", None)

    for image in js.get("images", []):
        if "bufferView" in image:
            image["bufferView"] = view_map[image["bufferView"]]

    js["bufferViews"] = new_views
    js["accessors"] = new_accessors
    js["buffers"] = [{"byteLength": len(new_blob)}]

    payload = json.dumps(js, separators=(",", ":")).encode("utf-8")
    while len(payload) % 4:
        payload += b" "
    while len(new_blob) % 4:
        new_blob.append(0)

    total = 12 + 8 + len(payload) + 8 + len(new_blob)
    out = bytearray()
    out += struct.pack("<4sII", b"glTF", 2, total)
    out += struct.pack("<II", len(payload), 0x4E4F534A)
    out += payload
    out += struct.pack("<II", len(new_blob), 0x004E4942)
    out += new_blob
    os.makedirs(os.path.dirname(dst_path), exist_ok=True)
    with open(dst_path, "wb") as handle:
        handle.write(out)
    return len(data), len(out)


def main():
    names = []
    for name in sorted(os.listdir(SRC)):
        if not name.endswith(".glb"):
            continue
        stem = name[:-4]
        if name == "pc001a.glb":
            continue
        if stem == "pc001a_city" or (
            len(stem) == 6 and stem[:5] in ("pc001", "pc002")
        ):
            names.append(name)
    if not names:
        raise SystemExit(f"no character glbs in {SRC}")
    for name in names:
        before, after = trim(os.path.join(SRC, name), os.path.join(DST, name))
        print(f"{name:18} {before / 1e6:5.1f}MB -> {after / 1e6:4.1f}MB")


if __name__ == "__main__":
    sys.exit(main())
