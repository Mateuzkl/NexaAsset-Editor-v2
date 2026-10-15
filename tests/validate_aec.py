"""Read-only AEC wire/embedded-image oracle, independent of the C#/AIR implementation."""
import argparse
import io
from pathlib import Path
from PIL import Image
from validate_native import fields, values, varint


def validate(path, first_pixel=None):
    if path.stat().st_size > 32 * 1024 * 1024:
        raise ValueError("AEC exceeds input budget")
    count = sprites = 0
    for category, wire, record in fields(path.read_bytes()):
        if not 1 <= category <= 4:
            continue
        if wire != 2 or not values(record, 1):
            raise ValueError("Invalid appearance framing")
        if values(record, 6) and values(record, 6)[-1] != category:
            raise ValueError("Appearance category mismatch")
        refs = []
        for group in values(record, 2):
            info, = values(group, 3)
            ids = []
            for number, encoding, value in fields(info):
                if number != 5:
                    continue
                if encoding == 0:
                    ids.append(value)
                elif encoding == 2:
                    offset = 0
                    while offset < len(value):
                        sprite, offset = varint(value, offset)
                        ids.append(sprite)
                else:
                    raise ValueError("Invalid sprite ID encoding")
            dimensions = [values(info, n)[-1] if values(info, n) else 1 for n in range(1, 5)]
            animations = values(info, 6)
            phases = len(values(animations[-1], 6)) if animations else 1
            expected = max(1, phases)
            for dimension in dimensions:
                expected *= dimension
            if expected != len(ids):
                raise ValueError("AEC group reference count mismatch")
            refs.extend(ids)
        images = values(record, 7)
        if len(refs) != len(images) or not refs:
            raise ValueError("Missing embedded sprite dependencies")
        for image in images:
            with Image.open(io.BytesIO(image)) as decoded:
                if decoded.format != "PNG" or decoded.width not in (32, 64) or decoded.height not in (32, 64):
                    raise ValueError("Invalid embedded sprite format/size")
                decoded.load()
                with decoded.convert("RGBA") as rgba:
                    if first_pixel is not None and rgba.tobytes()[:4] != first_pixel:
                        raise ValueError("AEC straight RGBA oracle mismatch")
            sprites += 1
        count += 1
    if not count:
        raise ValueError("Empty AEC container")
    print(f"PASS independent AEC: {count} objects, {sprites} embedded PNG sprites; protobuf graph and Pillow decoding", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("file", type=Path)
    parser.add_argument("--first-rgba")
    args = parser.parse_args()
    validate(args.file, bytes.fromhex(args.first_rgba) if args.first_rgba else None)
