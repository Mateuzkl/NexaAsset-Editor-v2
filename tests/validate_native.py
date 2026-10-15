"""Read-only native-format check, independent of the C#/AIR reader and writer.

Uses Python's liblzma-backed decoder and Pillow BMP decoding. It validates the
wire-level appearance graph and an optional ARGB oracle from the UI test.
This is a file-format validator, not a running Tibia-client certification.
"""
import argparse
import bisect
import hashlib
import io
import json
import lzma
import struct
from pathlib import Path
from PIL import Image


def varint(data, offset):
    value = 0
    for shift in range(0, 70, 7):
        if offset >= len(data):
            raise ValueError("Truncated varint")
        byte = data[offset]
        offset += 1
        if shift == 63 and byte > 1:
            raise ValueError("Varint overflow")
        value |= (byte & 127) << shift
        if not byte & 128:
            return value, offset
    raise ValueError("Invalid varint")


def fields(data):
    offset = 0
    while offset < len(data):
        key, offset = varint(data, offset)
        number, wire = key >> 3, key & 7
        if not number:
            raise ValueError("Zero protobuf field")
        if wire == 0:
            value, offset = varint(data, offset)
        else:
            if wire == 2:
                size, offset = varint(data, offset)
            elif wire in (1, 5):
                size = 8 if wire == 1 else 4
            else:
                raise ValueError("Unsupported wire type")
            if size > len(data) - offset:
                raise ValueError("Truncated field")
            value = data[offset:offset + size]
            offset += size
        yield number, wire, value


def values(data, number):
    return [value for field, wire, value in fields(data) if field == number]


def payload(root, name):
    path = (root / name).resolve()
    if not path.is_relative_to(root) or not path.is_file():
        raise ValueError("Missing or external catalog resource: " + name)
    return path


def decode_sheet(path):
    data = path.read_bytes()
    if len(data) > 4 * 1024 * 1024:
        raise ValueError("Oversized compressed sheet")
    offset = 0
    while offset < 32 and data[offset] == 0:
        offset += 1
    if data[offset:offset + 5] != bytes.fromhex("700afa8024"):
        raise ValueError("Invalid CIP magic")
    size, offset = varint(data, offset + 5)
    if offset != 32 or size != len(data) - 32:
        raise ValueError("Invalid CIP size")
    properties = data[32]
    dictionary = struct.unpack_from("<I", data, 33)[0]
    if properties >= 225 or not 0 < dictionary <= 64 * 1024 * 1024:
        raise ValueError("Invalid LZMA properties")
    remainder = properties // 9
    decoder = lzma.LZMADecompressor(format=lzma.FORMAT_RAW, filters=[{
        "id": lzma.FILTER_LZMA1, "dict_size": min(dictionary, 1024 * 1024),
        "lc": properties % 9, "lp": remainder % 5, "pb": remainder // 5,
    }])
    bmp = decoder.decompress(data[45:], max_length=1024 * 1024 + 1)
    if len(bmp) > 1024 * 1024 or not decoder.eof:
        raise ValueError("Invalid/oversized LZMA output")
    with Image.open(io.BytesIO(bmp)) as image:
        if image.size != (384, 384):
            raise ValueError("Invalid BMP sheet dimensions")
        return image.convert("RGBA")


def validate(root, oracle=None, all_sheets=False):
    root = root.resolve()
    catalog = json.loads(payload(root, "catalog-content.json").read_text(encoding="utf-8"))
    for entry in catalog:
        if "file" in entry:
            payload(root, entry["file"])
    appearance_entries = [e for e in catalog if e["type"] == "appearances"]
    if len(appearance_entries) != 1:
        raise ValueError("Expected one appearances entry")
    sheets = sorted([e for e in catalog if e["type"] == "sprite"], key=lambda e: e["firstspriteid"])
    shapes = [(32, 32), (32, 64), (64, 32), (64, 64)]
    last = -1
    for sheet in sheets:
        first, end = sheet["firstspriteid"], sheet["lastspriteid"]
        width, height = shapes[sheet["spritetype"]]
        if first <= last or end < first or end - first + 1 > (384 // width) * (384 // height):
            raise ValueError("Overlapping/over-capacity sprite range")
        last = end
    starts = [e["firstspriteid"] for e in sheets]

    def sheet_for(sprite):
        index = bisect.bisect_right(starts, sprite) - 1
        if index < 0 or sprite > sheets[index]["lastspriteid"]:
            raise ValueError("Unresolved sprite reference: " + str(sprite))
        return sheets[index]

    names = ("item", "outfit", "effect", "missile")
    records, references = {}, 0
    source = payload(root, appearance_entries[0]["file"]).read_bytes()
    for category, wire, body in fields(source):
        if category not in range(1, 5):
            continue
        if wire != 2:
            raise ValueError("Invalid appearance field")
        ids = values(body, 1)
        if len(ids) != 1 or (category, ids[0]) in records:
            raise ValueError("Missing/duplicate appearance ID")
        groups = []
        for frame in values(body, 2):
            infos = values(frame, 3)
            if len(infos) != 1:
                raise ValueError("Invalid sprite info")
            info = infos[0]
            dimensions = [(values(info, n) or [1])[-1] for n in range(1, 5)]
            animation = values(info, 6)
            phases = len(values(animation[-1], 6)) if animation else 0
            expected = max(1, phases)
            for dimension in dimensions:
                if not 0 < dimension <= 256:
                    raise ValueError("Invalid patterns/layers")
                expected *= dimension
            sprites = []
            for number, sprite_wire, value in fields(info):
                if number != 5:
                    continue
                if sprite_wire == 0:
                    sprites.append(value)
                elif sprite_wire == 2:
                    at = 0
                    while at < len(value):
                        sprite, at = varint(value, at)
                        sprites.append(sprite)
                else:
                    raise ValueError("Invalid sprite ID wire type")
            if len(sprites) != expected:
                raise ValueError("Frame/reference count mismatch")
            for sprite in sprites:
                sheet_for(sprite)
            references += len(sprites)
            groups.append(sprites)
        if not groups:
            raise ValueError("Appearance has no groups")
        records[(category, ids[0])] = groups
    if all_sheets:
        for index, sheet in enumerate(sheets):
            decode_sheet(payload(root, sheet["file"])).close()
            if index % 512 == 0:
                print(f"Independent LZMA/BMP validation: {index + 1}/{len(sheets)}", flush=True)
    if oracle:
        expected = json.loads(oracle.read_text(encoding="utf-8"))
        for index, category in enumerate(names, 1):
            if sum(1 for key in records if key[0] == index) != expected["counts"][category]["count"]:
                raise ValueError("Category count differs from pre-save oracle")
        if sum(e["lastspriteid"] - e["firstspriteid"] + 1 for e in sheets) != expected["spriteCount"]:
            raise ValueError("Sprite IDs/count changed")
        row = expected["sprite"]
        sheet = sheet_for(row["id"])
        width, height = shapes[sheet["spritetype"]]
        if (width, height) != (row["width"], row["height"]):
            raise ValueError("Edited sprite dimensions changed")
        offset = row["id"] - sheet["firstspriteid"]
        x, y = offset % (384 // width) * width, offset // (384 // width) * height
        with decode_sheet(payload(root, sheet["file"])) as image:
            with image.crop((x, y, x + width, y + height)) as tile:
                rgba = tile.tobytes()
        argb = bytearray(len(rgba))
        for offset in range(0, len(rgba), 4):
            red, green, blue, alpha = rgba[offset:offset + 4]
            if (red, green, blue) == (255, 0, 255):
                red = green = blue = alpha = 0
            argb[offset:offset + 4] = bytes((alpha, red, green, blue))
        if hashlib.md5(argb).hexdigest() != row["argbMd5"]:
            raise ValueError("Independent edited ARGB hash differs")
        if records[(1, expected["item"])][0][0] != expected["reference"]:
            raise ValueError("Edited item reference did not persist")
    print(f"PASS independent native files: {len(records)} objects, {len(sheets)} sheets, {references} valid references; liblzma/Pillow and edited ARGB oracle", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("root", type=Path)
    parser.add_argument("--oracle", type=Path)
    parser.add_argument("--all-sheets", action="store_true")
    args = parser.parse_args()
    validate(args.root, args.oracle, args.all_sheets)
