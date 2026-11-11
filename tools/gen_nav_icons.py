#!/usr/bin/env python3
"""
Turns the design's exported SVG icons into XAML Path data, and rasterises them so the
result can be checked without a Windows build.

The SVGs arrive as several <path> elements per icon. Where they share a stroke colour
and width their `d` strings are concatenated into one path - XAML strokes each `M`
subpath separately, so the result is identical and the control only has to carry a
single Geometry per icon.

The lock is the one icon that mixes a filled dot with stroked lines. A round-capped
stroke already draws a disc of the stroke's own width at the end of a segment, so the
dot and the line below it collapse into one segment and the fill disappears.

    python3 tools/gen_nav_icons.py
"""
import math
import os
import re

OUT = "/tmp/preview/nav_icons.png"
SS = 12  # supersample

# --- the design's exported paths, verbatim -----------------------------------------

RING = ("M32 16C32 24.8366 24.8366 32 16 32C7.16344 32 0 24.8366 0 16C0 7.16344 "
        "7.16344 0 16 0C24.8366 0 32 7.16344 32 16ZM8 16C8 20.4183 11.5817 24 16 "
        "24C20.4183 24 24 20.4183 24 16C24 11.5817 20.4183 8 16 8C11.5817 8 8 "
        "11.5817 8 16Z")

ICONS = {
    "Services": dict(w=2.0, paths=[
        "M21 8.5H7C5.067 8.5 3.5 10.067 3.5 12V21C3.5 22.933 5.067 24.5 7 24.5H21C22.933 24.5 24.5 22.933 24.5 21V12C24.5 10.067 22.933 8.5 21 8.5Z",
        "M9 8.5V6C9 5.33696 9.26339 4.70107 9.73223 4.23223C10.2011 3.76339 10.837 3.5 11.5 3.5H16.5C17.163 3.5 17.7989 3.76339 18.2678 4.23223C18.7366 4.70107 19 5.33696 19 6V8.5M3.5 14.5C9.9 18.1 18.1 18.1 24.5 14.5M14 14.5V18.5",
    ]),
    "Persons": dict(w=2.0, paths=[
        "M11 12.5C13.2091 12.5 15 10.7091 15 8.5C15 6.29086 13.2091 4.5 11 4.5C8.79086 4.5 7 6.29086 7 8.5C7 10.7091 8.79086 12.5 11 12.5Z",
        "M3.5 23V21C3.5 19.0109 4.29018 17.1032 5.6967 15.6967C7.10322 14.2902 9.01088 13.5 11 13.5C12.9891 13.5 14.8968 14.2902 16.3033 15.6967C17.7098 17.1032 18.5 19.0109 18.5 21V23M20 5C21.0609 5 22.0783 5.42143 22.8284 6.17157C23.5786 6.92172 24 7.93913 24 9C24 10.0609 23.5786 11.0783 22.8284 11.8284C22.0783 12.5786 21.0609 13 20 13M21 16C22.0514 16.4819 22.9412 17.2574 23.5621 18.2332C24.1831 19.209 24.5088 20.3434 24.5 21.5V23",
    ]),
    "Sites": dict(w=2.0, paths=[
        "M23 11.5C23 18 14 25 14 25C14 25 5 18 5 11.5C5 10.3181 5.23279 9.14778 5.68508 8.05585C6.13738 6.96392 6.80031 5.97177 7.63604 5.13604C8.47177 4.30031 9.46392 3.63738 10.5558 3.18508C11.6478 2.73279 12.8181 2.5 14 2.5C15.1819 2.5 16.3522 2.73279 17.4442 3.18508C18.5361 3.63738 19.5282 4.30031 20.364 5.13604C21.1997 5.97177 21.8626 6.96392 22.3149 8.05585C22.7672 9.14778 23 10.3181 23 11.5Z",
        "M14 14.75C15.7949 14.75 17.25 13.2949 17.25 11.5C17.25 9.70507 15.7949 8.25 14 8.25C12.2051 8.25 10.75 9.70507 10.75 11.5C10.75 13.2949 12.2051 14.75 14 14.75Z",
    ]),
    # the filled keyhole dot at (14,17.5) r1 plus "M14 18.5V21" become one capped segment
    "Lock": dict(w=2.2, paths=[
        "M19.5 12H8.5C6.567 12 5 13.567 5 15.5V21.5C5 23.433 6.567 25 8.5 25H19.5C21.433 25 23 23.433 23 21.5V15.5C23 13.567 21.433 12 19.5 12Z",
        "M9 12V8.5C9 7.04131 9.57946 5.64236 10.6109 4.61091C11.6424 3.57946 13.0413 3 14.5 3C15.9587 3 17.3576 3.57946 18.3891 4.61091C19.4205 5.64236 20 7.04131 20 8.5V12",
        "M14 17.5V21",
    ]),
}

# --- path parsing -------------------------------------------------------------------

TOKEN = re.compile(r"[MmLlHhVvCcSsQqTtAaZz]|-?\d*\.?\d+(?:[eE][-+]?\d+)?")


def tokenize(d):
    return TOKEN.findall(d)


def arc_points(x0, y0, rx, ry, rot, large, sweep, x, y, steps=24):
    """Endpoint-parameterised elliptical arc -> polyline (SVG spec F.6.5)."""
    if rx == 0 or ry == 0 or (x0 == x and y0 == y):
        return [(x, y)]
    rad = math.radians(rot)
    cosr, sinr = math.cos(rad), math.sin(rad)
    dx2, dy2 = (x0 - x) / 2.0, (y0 - y) / 2.0
    x1 = cosr * dx2 + sinr * dy2
    y1 = -sinr * dx2 + cosr * dy2
    rx, ry = abs(rx), abs(ry)
    lam = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry)
    if lam > 1:
        s = math.sqrt(lam)
        rx, ry = rx * s, ry * s
    num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1
    den = rx * rx * y1 * y1 + ry * ry * x1 * x1
    co = math.sqrt(max(0.0, num / den)) if den else 0.0
    if large == sweep:
        co = -co
    cx1 = co * rx * y1 / ry
    cy1 = -co * ry * x1 / rx
    cx = cosr * cx1 - sinr * cy1 + (x0 + x) / 2.0
    cy = sinr * cx1 + cosr * cy1 + (y0 + y) / 2.0

    def ang(ux, uy, vx, vy):
        d = (math.hypot(ux, uy) * math.hypot(vx, vy))
        if d == 0:
            return 0.0
        c = max(-1.0, min(1.0, (ux * vx + uy * vy) / d))
        a = math.acos(c)
        return -a if ux * vy - uy * vx < 0 else a

    t1 = ang(1, 0, (x1 - cx1) / rx, (y1 - cy1) / ry)
    dt = ang((x1 - cx1) / rx, (y1 - cy1) / ry, (-x1 - cx1) / rx, (-y1 - cy1) / ry)
    if not sweep and dt > 0:
        dt -= 2 * math.pi
    elif sweep and dt < 0:
        dt += 2 * math.pi
    pts = []
    for i in range(1, steps + 1):
        t = t1 + dt * i / steps
        px = cosr * rx * math.cos(t) - sinr * ry * math.sin(t) + cx
        py = sinr * rx * math.cos(t) + cosr * ry * math.sin(t) + cy
        pts.append((px, py))
    return pts


def cubic(p0, p1, p2, p3, steps=20):
    out = []
    for i in range(1, steps + 1):
        t = i / steps
        u = 1 - t
        out.append((u**3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t**3 * p3[0],
                    u**3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t**3 * p3[1]))
    return out


def flatten(d):
    """Path mini-language -> list of polylines, each (points, closed)."""
    t = tokenize(d)
    i = 0
    subs, cur = [], []
    x = y = sx = sy = 0.0
    cmd = None
    prev_c2 = None

    def num():
        nonlocal i
        v = float(t[i]); i += 1
        return v

    while i < len(t):
        if re.match(r"[A-Za-z]", t[i]):
            cmd = t[i]; i += 1
        rel = cmd.islower()
        c = cmd.upper()

        if c == "M":
            if cur:
                subs.append((cur, False))
            nx, ny = num(), num()
            x, y = (x + nx, y + ny) if rel else (nx, ny)
            sx, sy = x, y
            cur = [(x, y)]
            cmd = "l" if rel else "L"
            prev_c2 = None
        elif c == "L":
            nx, ny = num(), num()
            x, y = (x + nx, y + ny) if rel else (nx, ny)
            cur.append((x, y)); prev_c2 = None
        elif c == "H":
            nx = num(); x = x + nx if rel else nx
            cur.append((x, y)); prev_c2 = None
        elif c == "V":
            ny = num(); y = y + ny if rel else ny
            cur.append((x, y)); prev_c2 = None
        elif c in ("C", "S"):
            if c == "C":
                a1, b1, a2, b2, nx, ny = (num() for _ in range(6))
                p1 = (x + a1, y + b1) if rel else (a1, b1)
                p2 = (x + a2, y + b2) if rel else (a2, b2)
            else:
                a2, b2, nx, ny = (num() for _ in range(4))
                p2 = (x + a2, y + b2) if rel else (a2, b2)
                p1 = (2 * x - prev_c2[0], 2 * y - prev_c2[1]) if prev_c2 else (x, y)
            p3 = (x + nx, y + ny) if rel else (nx, ny)
            cur += cubic((x, y), p1, p2, p3)
            prev_c2 = p2
            x, y = p3
        elif c == "A":
            rx, ry, rot, la, sw, nx, ny = (num() for _ in range(7))
            ex, ey = (x + nx, y + ny) if rel else (nx, ny)
            cur += arc_points(x, y, rx, ry, rot, int(la), int(sw), ex, ey)
            x, y = ex; prev_c2 = None
        elif c == "Z":
            if cur:
                subs.append((cur, True))
            cur = [(sx, sy)]
            x, y = sx, sy
            prev_c2 = None
        else:
            raise SystemExit(f"unhandled command {cmd}")
    if cur and len(cur) > 1:
        subs.append((cur, False))
    return subs


def bounds(d):
    xs, ys = [], []
    for pts, _ in flatten(d):
        for px, py in pts:
            xs.append(px); ys.append(py)
    return min(xs), min(ys), max(xs), max(ys)


def main():
    from PIL import Image, ImageDraw

    os.makedirs("/tmp/preview", exist_ok=True)
    print(f'{"icon":10}{"stroke":>7}{"ink bounds (x0,y0,x1,y1)":>30}{"fits 28 box":>14}')

    combined = {}
    for name, spec in ICONS.items():
        d = " ".join(spec["paths"])
        combined[name] = d
        x0, y0, x1, y1 = bounds(d)
        h = spec["w"] / 2.0  # stroke straddles the outline
        ok = (x0 - h >= -0.01 and y0 - h >= -0.01 and x1 + h <= 28.01 and y1 + h <= 28.01)
        print(f'{name:10}{spec["w"]:>7}{f"({x0:.1f},{y0:.1f},{x1:.1f},{y1:.1f})":>30}'
              f'{("yes" if ok else "OVERFLOWS"):>14}')

    rx0, ry0, rx1, ry1 = bounds(RING)
    print(f'{"Ring":10}{"fill":>7}{f"({rx0:.1f},{ry0:.1f},{rx1:.1f},{ry1:.1f})":>30}'
          f'{("yes" if rx1 <= 32.01 and ry1 <= 32.01 else "OVERFLOWS"):>14}')

    # --- render a sheet: ring, then each icon on its disc --------------------------
    DISC, PAD = 56, 16
    n = len(ICONS) + 1
    W = n * (DISC + PAD) + PAD
    img = Image.new("RGB", (W * SS, (DISC + 2 * PAD) * SS), (245, 245, 245))
    dr = ImageDraw.Draw(img)

    def place(ox, oy, d, colour, width, scale, dx, dy):
        for pts, closed in flatten(d):
            p = [((ox + dx + px * scale) * SS, (oy + dy + py * scale) * SS) for px, py in pts]
            if closed:
                p.append(p[0])
            dr.line(p, fill=colour, width=max(1, int(width * scale * SS)), joint="curve")

    x = PAD
    # the ring, filled: outer disc in teal then punch the hole back to the rail colour
    cxr, cyr = x + DISC / 2, PAD + DISC / 2
    dr.ellipse([(cxr - 16) * SS, (cyr - 16) * SS, (cxr + 16) * SS, (cyr + 16) * SS],
               fill=(91, 185, 166))
    dr.ellipse([(cxr - 8) * SS, (cyr - 8) * SS, (cxr + 8) * SS, (cyr + 8) * SS],
               fill=(245, 245, 245))
    x += DISC + PAD

    for i, (name, spec) in enumerate(ICONS.items()):
        selected = name in ("Services", "Lock")
        fill = (75, 170, 152) if name != "Lock" else (245, 166, 35)
        cx, cy = x + DISC / 2, PAD + DISC / 2
        dr.ellipse([(cx - 28) * SS, (cy - 28) * SS, (cx + 28) * SS, (cy + 28) * SS],
                   fill=fill if selected else (255, 255, 255))
        colour = (255, 255, 255) if selected else (75, 170, 152)
        place(x, PAD, combined[name], colour, spec["w"], 1.0, (DISC - 28) / 2, (DISC - 28) / 2)
        x += DISC + PAD

    img.resize((W, DISC + 2 * PAD), Image.LANCZOS).save(OUT)
    print(f"\nwrote {OUT}")

    print("\n--- combined XAML path data ---")
    for name, d in combined.items():
        print(f"\n{name} (StrokeThickness {ICONS[name]['w']}):\n{d}")


if __name__ == "__main__":
    main()
