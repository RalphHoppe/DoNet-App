#!/usr/bin/env python3
"""
Rasterises the inline icon geometries so they can be eyeballed without a Windows build.

The icons are hand-written path mini-language inlined into the XAML. This parses the
same strings - including elliptical arcs, which the fonts never produce and the other
preview scripts therefore do not implement - and strokes them on an 18x18 grid.

    python3 tools/render_icons.py
"""
import math
import os

from PIL import Image, ImageDraw

SS = 16                       # supersample factor
GRID = 18.0
OUT = "/tmp/preview/icons.png"

ICONS = {
    "Services (briefcase)":
        "M4,5.9 H14 A2,2 0 0 1 16,7.9 V13.1 A2,2 0 0 1 14,15.1 H4 A2,2 0 0 1 2,13.1 "
        "V7.9 A2,2 0 0 1 4,5.9 Z "
        "M6.9,5.9 V4.5 A1.3,1.3 0 0 1 8.2,3.2 H9.8 A1.3,1.3 0 0 1 11.1,4.5 V5.9 "
        "M7.8,10.5 H10.2",
    "Persons":
        "M9.3,6.2 A2.5,2.5 0 1 1 4.3,6.2 A2.5,2.5 0 1 1 9.3,6.2 Z "
        "M2.7,15.1 V14 C2.7,12.1 4.2,10.6 6.1,10.6 H7.5 C9.4,10.6 10.9,12.1 10.9,14 V15.1 "
        "M11.8,4.1 A2.5,2.5 0 0 1 11.8,8.3 "
        "M12.9,10.6 H13.3 C15.2,10.6 16.7,12.1 16.7,14 V15.1",
    "Sites (pin)":
        "M9,15.6 C9,15.6 13.6,11.1 13.6,6.9 A4.6,4.6 0 1 0 4.4,6.9 C4.4,11.1 9,15.6 9,15.6 Z "
        "M10.9,6.8 A1.9,1.9 0 1 1 7.1,6.8 A1.9,1.9 0 1 1 10.9,6.8 Z",
    "Lock":
        "M4.6,7.7 H13.4 A1.9,1.9 0 0 1 15.3,9.6 V13.5 A1.9,1.9 0 0 1 13.4,15.4 H4.6 "
        "A1.9,1.9 0 0 1 2.7,13.5 V9.6 A1.9,1.9 0 0 1 4.6,7.7 Z "
        "M5.7,7.7 V5.5 A3.3,3.3 0 0 1 12.3,5.5 V7.7 "
        "M9,10.6 V12.5",
}


def _bezier(p0, ctrl, steps=24):
    out = []
    for i in range(1, steps + 1):
        t = i / steps
        q = [p0] + ctrl
        while len(q) > 1:
            q = [((1 - t) * q[j][0] + t * q[j + 1][0],
                  (1 - t) * q[j][1] + t * q[j + 1][1]) for j in range(len(q) - 1)]
        out.append(q[0])
    return out


def _arc(p0, rx, ry, rot, large, sweep, p1, steps=36):
    """SVG endpoint-parameterised arc -> polyline (endpoint to centre conversion)."""
    if p0 == p1:
        return []
    phi = math.radians(rot)
    dx2, dy2 = (p0[0] - p1[0]) / 2.0, (p0[1] - p1[1]) / 2.0
    x1 = math.cos(phi) * dx2 + math.sin(phi) * dy2
    y1 = -math.sin(phi) * dx2 + math.cos(phi) * dy2

    # scale the radii up if they are too small to span the chord
    lam = (x1 * x1) / (rx * rx) + (y1 * y1) / (ry * ry)
    if lam > 1:
        rx, ry = rx * math.sqrt(lam), ry * math.sqrt(lam)

    num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1
    den = rx * rx * y1 * y1 + ry * ry * x1 * x1
    co = math.sqrt(max(0.0, num / den))
    if large == sweep:
        co = -co
    cx1, cy1 = co * rx * y1 / ry, -co * ry * x1 / rx

    cx = math.cos(phi) * cx1 - math.sin(phi) * cy1 + (p0[0] + p1[0]) / 2.0
    cy = math.sin(phi) * cx1 + math.cos(phi) * cy1 + (p0[1] + p1[1]) / 2.0

    def angle(ux, uy, vx, vy):
        dot = ux * vx + uy * vy
        n = math.hypot(ux, uy) * math.hypot(vx, vy)
        a = math.acos(max(-1.0, min(1.0, dot / n)))
        return -a if ux * vy - uy * vx < 0 else a

    th0 = angle(1, 0, (x1 - cx1) / rx, (y1 - cy1) / ry)
    dth = angle((x1 - cx1) / rx, (y1 - cy1) / ry, (-x1 - cx1) / rx, (-y1 - cy1) / ry)
    if not sweep and dth > 0:
        dth -= 2 * math.pi
    elif sweep and dth < 0:
        dth += 2 * math.pi

    pts = []
    for i in range(1, steps + 1):
        th = th0 + dth * i / steps
        px = math.cos(phi) * rx * math.cos(th) - math.sin(phi) * ry * math.sin(th) + cx
        py = math.sin(phi) * rx * math.cos(th) + math.cos(phi) * ry * math.sin(th) + cy
        pts.append((px, py))
    return pts


def parse(d):
    """Path mini-language -> list of polylines. Absolute commands only, as emitted."""
    import re
    tokens = re.findall(r"[A-Za-z]|-?\d*\.?\d+", d)
    subpaths, cur, pt, start, cmd = [], [], (0.0, 0.0), (0.0, 0.0), None
    i = 0

    def num():
        nonlocal i
        v = float(tokens[i])
        i += 1
        return v

    while i < len(tokens):
        if tokens[i].isalpha():
            cmd = tokens[i]
            i += 1
            if cmd in "Zz":
                if cur:
                    cur.append(start)
                    subpaths.append(cur)
                    cur = []
                pt = start
                continue
        if cmd in "Mm":
            if cur:
                subpaths.append(cur)
            pt = start = (num(), num())
            cur = [pt]
        elif cmd in "Ll":
            pt = (num(), num())
            cur.append(pt)
        elif cmd in "Hh":
            pt = (num(), pt[1])
            cur.append(pt)
        elif cmd in "Vv":
            pt = (pt[0], num())
            cur.append(pt)
        elif cmd in "Cc":
            c = [(num(), num()), (num(), num()), (num(), num())]
            cur += _bezier(pt, c)
            pt = c[2]
        elif cmd in "Qq":
            c = [(num(), num()), (num(), num())]
            cur += _bezier(pt, c)
            pt = c[1]
        elif cmd in "Aa":
            rx, ry, rot = num(), num(), num()
            large, sweep = int(num()), int(num())
            end = (num(), num())
            cur += _arc(pt, rx, ry, rot, large, sweep, end)
            pt = end
        else:
            i += 1
    if cur:
        subpaths.append(cur)
    return subpaths


def main():
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    cell = int(GRID * SS)
    pad = 10 * SS
    img = Image.new("RGB", ((cell + pad) * len(ICONS) + pad, cell + 2 * pad), "white")
    dr = ImageDraw.Draw(img)

    for idx, (name, data) in enumerate(ICONS.items()):
        ox = pad + idx * (cell + pad)
        oy = pad
        dr.rectangle([ox, oy, ox + cell, oy + cell], outline=(230, 232, 235))
        for sp in parse(data):
            dr.line([(ox + x * SS, oy + y * SS) for x, y in sp],
                    fill=(0x2F, 0x88, 0x72), width=int(1.6 * SS), joint="curve")
        xs = [x for sp in parse(data) for x, _ in sp]
        ys = [y for sp in parse(data) for _, y in sp]
        print(f"  {name:22} bbox {min(xs):5.1f}..{max(xs):5.1f} x {min(ys):5.1f}..{max(ys):5.1f}"
              f"   {'OVERFLOWS 18x18' if min(xs) < 0.8 or min(ys) < 0.8 or max(xs) > 17.2 or max(ys) > 17.2 else 'fits'}")

    scale = 4
    img.resize((img.width // SS * scale, img.height // SS * scale), Image.LANCZOS).save(OUT)
    print(f"wrote {OUT}")


if __name__ == "__main__":
    main()
