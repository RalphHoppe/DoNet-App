#!/usr/bin/env python3
"""
Draws the app's package icons as the title bar's own mark.

The app's identity is the segmented ring in the DoNet lockup - the circle at
the left of the title bar, drawn by DoNetLogo.xaml with a mid-stroke radius
of 40.79, a 29.41 stroke, and an 8.71-on / 8.71-off dash pattern, filled with
the lockup gradient #49A48C to #83CDB4. This renders that same ring, from the
same numbers, at every manifest size - so the Start menu, the taskbar and the
installer all show the mark the window already carries.

The splash is the ring on transparency: the manifest paints white behind it,
which is the surface the app's own splash page continues.

Run from the repository root:   python3 tools/make_app_icons.py
"""
import math
import pathlib

from PIL import Image, ImageDraw

ROOT = pathlib.Path(__file__).resolve().parent.parent
ASSETS = ROOT / "DoNet" / "Assets"

# The lockup's own geometry, from DoNetLogo.xaml - canvas units.
RING_RADIUS = 40.79        # mid-stroke radius
RING_STROKE = 29.41        # stroke thickness
RING_DASH = 8.71           # dash length (gap is equal)
RING_OUTER = 2 * (RING_RADIUS + RING_STROKE / 2)   # 111.0 outer diameter

# The lockup's gradient, from DoNetLogo.xaml - absolute mapping, shallow
# diagonal across the wordmark. Across the ring alone it reads as a horizontal
# ramp between these two stops.
GRADIENT_START = (0x49, 0xA4, 0x8C)
GRADIENT_END = (0x83, 0xCD, 0xB4)

SUPERSAMPLE = 4

# (file, size, ring diameter as a share of the shorter side)
ASSETS_TO_DRAW = [
    ("Square150x150Logo.scale-200.png", (300, 300), 0.74),
    ("Square44x44Logo.scale-200.png", (88, 88), 0.74),
    ("Square44x44Logo.targetsize-24_altform-unplated.png", (24, 24), 0.78),
    ("StoreLogo.png", (50, 50), 0.74),
    ("Wide310x150Logo.scale-200.png", (620, 300), 0.74),
    ("LockScreenLogo.scale-200.png", (48, 48), 0.74),
    ("SplashScreen.scale-200.png", (1240, 600), 0.5),
]


def ring(size, share):
    """The lockup's ring, centred on a transparent tile."""
    width, height = size
    w, h = width * SUPERSAMPLE, height * SUPERSAMPLE

    # The dash pattern is defined in canvas units, so it scales with the ring:
    # the same number of segments at every size. Below ~44px the segments fall
    # under two pixels each and read as noise, so the smallest tile draws the
    # ring solid - still the mark, still legible.
    dashed = min(width, height) >= 44

    unit = min(width, height) * share / RING_OUTER * SUPERSAMPLE
    r_mid = RING_RADIUS * unit
    stroke = max(1, round(RING_STROKE * unit))
    cx, cy = w / 2, h / 2

    # Pillow's arc with a width thickens INWARD from the path (measured: ink
    # spans [R-W, R]), so the path is the ring's outer edge and the ink lands
    # on [r_mid - stroke/2, r_mid + stroke/2] - the band the XAML ring draws,
    # where the path is the mid-stroke circle thickening both ways.
    r_path = r_mid + stroke / 2
    bbox = [cx - r_path, cy - r_path, cx + r_path, cy + r_path]

    # The ring as a mask: arcs in solid white on transparency.
    mask = Image.new("L", (w, h), 0)
    draw = ImageDraw.Draw(mask)

    if dashed:
        # The dash length is defined along the lockup's own path radius, so the
        # angular pattern is the same at every size this draws.
        dash_deg = math.degrees(RING_DASH / RING_RADIUS)
        # The lockup's path starts at twelve o'clock; so does the pattern.
        angle = 0.0
        while angle < 360.0:
            end = min(angle + dash_deg, 360.0)
            draw.arc(bbox, angle - 90, end - 90, fill=255, width=stroke)
            angle = end + dash_deg
    else:
        draw.arc(bbox, 0, 360, fill=255, width=stroke)

    # The gradient, as a horizontal ramp across the ring's own extent - what
    # the lockup's absolute mapping samples over these same pixels.
    row = Image.new("RGB", (w, 1))
    left, right = cx - r_mid, cx + r_mid
    row.putdata([
        tuple(round(a + (b - a) * min(max((x - left) / (right - left), 0.0), 1.0))
              for a, b in zip(GRADIENT_START, GRADIENT_END))
        for x in range(w)
    ])
    gradient = row.resize((w, h))

    image = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    image.paste(gradient, (0, 0), mask)

    return image.resize((width, height), Image.LANCZOS)


def main():
    for name, size, share in ASSETS_TO_DRAW:
        path = ASSETS / name
        ring(size, share).save(path)
        print(f"{path.relative_to(ROOT)}: {size[0]}x{size[1]} ring drawn")

    print("Done. The manifest needs no new names - every file kept its own.")


if __name__ == "__main__":
    main()
