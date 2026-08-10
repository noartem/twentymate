"""
app.ico generator: a rounded square with a blue gradient and a white eye.

TrayIconFactory draws the same silhouette for the tray icon, so the geometry
matches between the two. The palette is tuned for WCAG: the white eye's
contrast against the tile never drops below 4.5:1, and the tile's contrast
against a dark taskbar never drops below 3:1.

    python Assets/generate-icon.py Assets/app.ico
"""

import math
import struct
import sys
import zlib

SIZES = [256, 128, 64, 48, 32, 24, 16]

TOP = (0x17, 0x74, 0xC9)
BOTTOM = (0x1A, 0x6F, 0xC0)
EYE = (255, 255, 255)

# Fractions of the tile's side — single source of truth for the .ico and the tray icon.
CORNER = 0.22
LENS_RADIUS = 0.50
LENS_OFFSET = 0.335
STROKE = 0.075
PUPIL_RADIUS = 0.105


def clamp(v, lo=0.0, hi=1.0):
    return max(lo, min(hi, v))


def rounded_square_sd(x, y, half, radius):
    """Signed distance to a rounded square centered at the origin."""
    qx = abs(x) - (half - radius)
    qy = abs(y) - (half - radius)
    return math.hypot(max(qx, 0.0), max(qy, 0.0)) + min(max(qx, qy), 0.0) - radius


def lens_sd(x, y, radius, offset):
    """Signed distance to the "lens" — the intersection of two circles."""
    return max(math.hypot(x, y - offset) - radius, math.hypot(x, y + offset) - radius)


def render(size):
    half = size / 2.0
    aa = 1.0  # anti-aliasing width in pixels

    corner = size * CORNER
    stroke = max(size * STROKE, 1.0)
    lens_r = size * LENS_RADIUS
    offset = size * LENS_OFFSET
    pupil_r = size * PUPIL_RADIUS

    rows = []
    for py in range(size):
        row = bytearray()
        for px in range(size):
            x = px + 0.5 - half
            y = py + 0.5 - half

            tile = clamp(0.5 - rounded_square_sd(x, y, half, corner) / aa)
            if tile <= 0.0:
                row += bytes((0, 0, 0, 0))
                continue

            t = (py + 0.5) / size
            r, g, b = (int(TOP[i] + (BOTTOM[i] - TOP[i]) * t) for i in range(3))

            # Eye outline — a band around the lens boundary, plus the pupil.
            outline = clamp(0.5 - (abs(lens_sd(x, y, lens_r, offset)) - stroke / 2) / aa)
            pupil = clamp(0.5 - (math.hypot(x, y) - pupil_r) / aa)
            eye = max(outline, pupil)

            r = int(r + (EYE[0] - r) * eye)
            g = int(g + (EYE[1] - g) * eye)
            b = int(b + (EYE[2] - b) * eye)

            row += bytes((r, g, b, int(round(tile * 255))))
        rows.append(bytes(row))

    return rows


def to_png(size, rows):
    raw = b"".join(b"\x00" + row for row in rows)

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body))

    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(raw, 9))
        + chunk(b"IEND", b"")
    )


def main(out_path):
    images = [(s, to_png(s, render(s))) for s in SIZES]

    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)

    entries = b""
    payload = b""
    for size, data in images:
        # In the ICO directory, size 256 is recorded as zero.
        dim = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        payload += data
        offset += len(data)

    with open(out_path, "wb") as f:
        f.write(header + entries + payload)

    print(f"wrote {out_path}: {len(header + entries + payload)} bytes")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "Assets/app.ico")
