"""Read-only structural probe for the supplied custom DAT; never executes the client.
Unknown payload widths are hypotheses, not attribute semantics. A successful
candidate must parse every record, bounded sprite reference and exact EOF.
"""
import collections
import pathlib
import struct
import sys

source = pathlib.Path(sys.argv[1])
data = (source / "Tibia.dat").read_bytes()
with (source / "Tibia.spr").open("rb") as sprites:
    signature, sprite_count = struct.unpack("<II", sprites.read(8))
counts = struct.unpack_from("<4H", data, 4)

def probe(width):
    at = 12
    unknown = []
    group_types = collections.Counter()
    shapes = collections.Counter()
    for category, count in zip(("item", "outfit", "effect", "missile"), counts):
        for item in range(100 if category == "item" else 1, count + 1):
            for _ in range(256):
                flag_at = at
                flag = data[at]
                at += 1
                if flag == 255:
                    break
                if flag in (0, 8, 9, 26, 29, 30, 33, 35):
                    at += 2
                elif flag in (22, 25):
                    at += 4
                elif flag == 34:
                    length = struct.unpack_from("<H", data, at + 6)[0]
                    at += 12 + length
                elif flag == 39:
                    at += 16
                elif flag == 40:
                    unknown.append((category, item, flag_at, data[at:at+32].hex()))
                    at += width
                elif flag not in set(range(40)) | {254}:
                    raise ValueError(f"unknown flag {flag:#x} in {category}/{item} at {flag_at}")
            else:
                raise ValueError("too many flags")
            groups = data[at] if category == "outfit" else 1
            if category == "outfit":
                at += 1
                if not 1 <= groups <= 2:
                    raise ValueError(f"invalid group count {groups}: {category}/{item} byte {at}")
            for _ in range(groups):
                if category == "outfit":
                    group_types[data[at]] += 1
                    at += 1
                w, h = data[at:at+2]
                at += 2
                if w > 1 or h > 1:
                    at += 1
                layers, x, y, z, frames = data[at:at+5]
                at += 5
                refs = w * h * layers * x * y * z * frames
                if not 0 < refs <= 65536:
                    raise ValueError(f"invalid refs {refs} ({w,h,layers,x,y,z,frames}): {category}/{item} byte {at}")
                shapes[(w, h)] += 1
                if frames > 1:
                    at += 6 + frames * 8
                indices = struct.unpack_from(f"<{refs}I", data, at)
                if max(indices) > sprite_count:
                    raise ValueError(f"sprite ref {max(indices)} > {sprite_count}: {category}/{item} byte {at}")
                at += refs * 4
    if at != len(data):
        raise ValueError(f"trailing {len(data)-at} bytes")
    return unknown, group_types, shapes

print(f"bytes={len(data)} signature={data[:4].hex()} counts={counts} sprites={sprite_count}")
for width in (0, 1, 2, 4, 8, 16):
    try:
        unknown, groups, shapes = probe(width)
        print(f"STRUCTURAL PASS width={width} groups={dict(groups)} shapes={dict(shapes)} flag40={unknown}")
    except (ValueError, IndexError, struct.error) as error:
        print(f"FAIL width={width}: {error}")
