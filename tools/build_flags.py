#!/usr/bin/env python3
"""Generate flag artwork for the country picker.

There is no flag font on Windows and no way to fetch official SVGs from this
workspace, so the flags are drawn. Each one is a short list of primitives in a
3x2 box, which the app turns into Rectangles, Ellipses and Paths.

The primitives are deliberately only three:

    R x y w h RRGGBB            rectangle
    E cx cy rx ry RRGGBB        ellipse
    P <path data> RRGGBB        path

Everything expressive - bands, cantons, Nordic crosses, saltires, stars,
crescents - is a helper here that expands into those three. The generator
carries the complexity so the control does not: FlagIcon only has to parse
three shapes.

These are rendered at 24x16 in a dropdown row. At that size the field colours
carry almost all of the recognition and a coat of arms is four pixels, so
emblems are simplified to the shape that reads: a disc, a ring, a star. The
field, the proportions and the colours are the parts worth getting right.

Run it to regenerate DoNet/Controls/FlagData.cs and tools/flag-proof.png.
"""

from __future__ import annotations

import math
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
W, H = 3.0, 2.0


# ---------------------------------------------------------------- primitives

def rect(x, y, w, h, c):
    return [f"R {x:.4g} {y:.4g} {w:.4g} {h:.4g} {c}"]


def solid(c):
    return rect(0, 0, W, H, c)


def ell(cx, cy, rx, ry, c):
    return [f"E {cx:.4g} {cy:.4g} {rx:.4g} {ry:.4g} {c}"]


def disc(cx, cy, r, c):
    return ell(cx, cy, r, r, c)


def path(d, c):
    return [f"P {d} {c}"]


def poly(points, c):
    d = "M" + " L".join(f"{x:.4g},{y:.4g}" for x, y in points) + " Z"
    return path(d, c)


# ------------------------------------------------------------------ builders

def h(*colors):
    """Horizontal bands of equal height."""
    band = H / len(colors)
    out = []
    for i, c in enumerate(colors):
        out += rect(0, i * band, W, band, c)
    return out


def v(*colors):
    """Vertical bands of equal width."""
    band = W / len(colors)
    out = []
    for i, c in enumerate(colors):
        out += rect(i * band, 0, band, H, c)
    return out


def hw(*pairs):
    """Horizontal bands with explicit weights: hw((2, 'FF0000'), (1, '000000'))."""
    total = sum(p[0] for p in pairs)
    out, y = [], 0.0
    for weight, c in pairs:
        band = H * weight / total
        out += rect(0, y, W, band, c)
        y += band
    return out


def vw(*pairs):
    """Vertical bands with explicit weights."""
    total = sum(p[0] for p in pairs)
    out, x = [], 0.0
    for weight, c in pairs:
        band = W * weight / total
        out += rect(x, 0, band, H, c)
        x += band
    return out


def canton(c, w=1.2, ht=1.0):
    return rect(0, 0, w, ht, c)


def nordic(field, cross, inner=None):
    """A Nordic cross: offset towards the hoist, as every one of them is."""
    out = solid(field)
    t = 0.42
    x = 0.95
    out += rect(0, (H - t) / 2, W, t, cross)
    out += rect(x - t / 2, 0, t, H, cross)
    if inner:
        ti = t * 0.42
        out += rect(0, (H - ti) / 2, W, ti, inner)
        out += rect(x - ti / 2, 0, ti, H, inner)
    return out


def star(cx, cy, r, c, points=5, rot=-90.0):
    pts = []
    for i in range(points * 2):
        ang = math.radians(rot + i * 180.0 / points)
        rr = r if i % 2 == 0 else r * 0.382
        pts.append((cx + rr * math.cos(ang), cy + rr * math.sin(ang)))
    return poly(pts, c)


def crescent(cx, cy, r, c, field, shift=0.26, tilt=0.0):
    """A disc with a second disc of the field colour bitten out of it."""
    return disc(cx, cy, r, c) + disc(cx + r * shift, cy + tilt, r * 0.82, field)


def tri(c, w=1.1):
    """A triangle on the hoist."""
    return poly([(0, 0), (w, H / 2), (0, H)], c)


def saltire(c, t=0.3):
    """A diagonal cross, corner to corner."""
    k = t / 2 * math.sqrt(1 + (W / H) ** 2) / (W / H)
    return (poly([(0, 0), (k * W / H, 0), (W, H - k), (W, H), (W - k * W / H, H), (0, k)], c)
            + poly([(W, 0), (W - k * W / H, 0), (0, H - k), (0, H), (k * W / H, H), (W, k)], c))


def diag(c, t=0.34, up=True):
    """A single diagonal band."""
    if up:
        return poly([(0, H), (0, H - t * H), (W, 0), (W, t * H)], c)
    return poly([(0, 0), (t * W, 0), (W, H), (W - t * W, H)], c)


def ring(cx, cy, r, c, thickness=0.09, field="FFFFFF"):
    return disc(cx, cy, r, c) + disc(cx, cy, r - thickness, field)


def emblem(c, cx=1.5, cy=1.0, r=0.3):
    """Stand-in for a coat of arms, which is unreadable at this size anyway."""
    return disc(cx, cy, r, c)


def place(layers, ox, oy, fx, fy):
    """Rewrite a finished layer list into a sub-rectangle.

    Used for the Union Jack, which five flags carry as a canton and which is
    worth having in exactly one place.
    """
    out = []
    for part in layers:
        kind, rest = part.split(" ", 1)
        body, color = rest.rsplit(" ", 1)
        n = [float(t) for t in body.split()] if kind in ("R", "E") else None
        if kind == "R":
            out += rect(ox + n[0] * fx, oy + n[1] * fy, n[2] * fx, n[3] * fy, color)
        elif kind == "E":
            out += ell(ox + n[0] * fx, oy + n[1] * fy, n[2] * fx, n[3] * fy, color)
        else:
            moved = []
            for token in body.replace("M", " M ").replace("L", " L ").replace("Z", " Z ").split():
                if "," in token:
                    px, py = token.split(",")
                    moved.append(f"{ox + float(px) * fx:.4g},{oy + float(py) * fy:.4g}")
                else:
                    moved.append(token)
            d = ""
            for token in moved:
                d += token if token in ("M", "L", "Z") else (" " + token)
            out += path(d.strip().replace("M ", "M").replace("L ", "L"), color)
    return out


def halfdisc(cx, cy, r, c, upper=True, steps=18):
    """Half a circle, as a polygon. Keeps the primitive set free of arcs."""
    a0, a1 = (180, 360) if upper else (0, 180)
    pts = [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / steps)),
            cy + r * math.sin(math.radians(a0 + (a1 - a0) * i / steps)))
           for i in range(steps + 1)]
    return poly(pts, c)


# ------------------------------------------------------------------ the table

UNION_JACK = (solid("012169") + saltire("FFFFFF", 0.34) + saltire("C8102E", 0.17)
              + rect(0, 0.7, W, 0.6, "FFFFFF") + rect(1.2, 0, 0.6, H, "FFFFFF")
              + rect(0, 0.8, W, 0.4, "C8102E") + rect(1.3, 0, 0.4, H, "C8102E"))


def jack():
    """The Union Jack in the canton - half width, half height."""
    return place(UNION_JACK, 0, 0, 0.5, 0.5)


F = {}

# --- Europe ---------------------------------------------------------------
F["Albania"] = solid("E41E20") + ell(1.5, 1.05, 0.56, 0.3, "000000") + \
    disc(1.24, 0.72, 0.19, "000000") + disc(1.76, 0.72, 0.19, "000000") + \
    poly([(1.5, 0.95), (1.66, 1.5), (1.34, 1.5)], "000000")
F["Andorra"] = v("10069F", "FEDF00", "D0103A") + emblem("B8860B", r=0.26)
F["Austria"] = h("ED2939", "FFFFFF", "ED2939")
F["Belarus"] = hw((2, "D22730"), (1, "4AA657")) + rect(0, 0, 0.34, H, "FFFFFF")
F["Belgium"] = v("000000", "FDDA24", "EF3340")
F["Bosnia and Herzegovina"] = solid("002F6C") + poly([(1.05, 0), (2.6, 0), (1.05, 2.0)], "FECB00")
F["Bulgaria"] = h("FFFFFF", "00966E", "D62612")
F["Croatia"] = h("FF0000", "FFFFFF", "171796") + emblem("FF0000", r=0.24)
F["Cyprus"] = solid("FFFFFF") + poly([(1.2, 0.62), (1.78, 0.72), (1.9, 1.0), (1.6, 1.2),
                                      (1.24, 1.08)], "D47600")
F["Czechia"] = h("FFFFFF", "D7141A") + poly([(0, 0), (1.2, 1.0), (0, 2.0)], "11457E")
F["Denmark"] = nordic("C8102E", "FFFFFF")
F["Estonia"] = h("0072CE", "000000", "FFFFFF")
F["Finland"] = nordic("FFFFFF", "003580")
F["France"] = v("002395", "FFFFFF", "ED2939")
F["Georgia"] = solid("FFFFFF") + rect(0, 0.78, W, 0.44, "FF0000") + rect(1.28, 0, 0.44, H, "FF0000")
F["Germany"] = h("000000", "DD0000", "FFCE00")
F["Greece"] = hw(*[(1, "0D5EAF" if i % 2 == 0 else "FFFFFF") for i in range(9)]) + \
    rect(0, 0, 1.0, 1.0, "0D5EAF") + rect(0, 0.4, 1.0, 0.2, "FFFFFF") + rect(0.4, 0, 0.2, 1.0, "FFFFFF")
F["Hungary"] = h("CE2939", "FFFFFF", "477050")
F["Iceland"] = nordic("02529C", "FFFFFF", "DC1E35")
F["Ireland"] = v("169B62", "FFFFFF", "FF883E")
F["Italy"] = v("008C45", "F4F5F0", "CD212A")
F["Kosovo"] = solid("244AA5") + poly([(1.05, 0.72), (2.0, 0.78), (1.95, 1.3), (1.2, 1.3)], "D0A650") + \
    [f"R 1.1 0.42 {0.1:.4g} 0.1 FFFFFF"]
F["Latvia"] = hw((2, "9E3039"), (1, "FFFFFF"), (2, "9E3039"))
F["Liechtenstein"] = h("002B7F", "CE1126") + disc(0.75, 0.42, 0.26, "FFD83D")
F["Lithuania"] = h("FDB913", "006A44", "C1272D")
F["Luxembourg"] = h("ED2939", "FFFFFF", "00A1DE")
F["Malta"] = vw((1, "FFFFFF"), (1, "CF142B")) + rect(0.12, 0.14, 0.34, 0.34, "C0C0C0")
F["Moldova"] = v("0046AE", "FFD200", "CC092F") + emblem("A77B06", r=0.26)
F["Monaco"] = h("CE1126", "FFFFFF")
F["Montenegro"] = solid("C40308") + rect(0.1, 0.07, W - 0.2, H - 0.14, "C40308") + emblem("D4AF37", r=0.34)
F["Netherlands"] = h("AE1C28", "FFFFFF", "21468B")
F["North Macedonia"] = solid("D20000") + disc(1.5, 1.0, 0.42, "FFE600") + \
    sum([poly([(1.5, 1.0),
               (1.5 + 2.2 * math.cos(math.radians(a - 7)), 1.0 + 2.2 * math.sin(math.radians(a - 7))),
               (1.5 + 2.2 * math.cos(math.radians(a + 7)), 1.0 + 2.2 * math.sin(math.radians(a + 7)))],
              "FFE600") for a in range(0, 360, 45)], [])
F["Norway"] = nordic("BA0C2F", "FFFFFF", "00205B")
F["Poland"] = h("FFFFFF", "DC143C")
F["Portugal"] = vw((2, "006600"), (3, "FF0000")) + ring(1.2, 1.0, 0.38, "FFD700", 0.08, "FF0000") + \
    disc(1.2, 1.0, 0.2, "FFFFFF")
F["Romania"] = v("002B7F", "FCD116", "CE1126")
F["Russia"] = h("FFFFFF", "0039A6", "D52B1E")
F["San Marino"] = h("FFFFFF", "5EB6E4") + emblem("C8A95B", r=0.26)
F["Serbia"] = h("C6363C", "0C4076", "FFFFFF") + emblem("C6363C", cx=1.1, r=0.26)
F["Slovakia"] = h("FFFFFF", "0B4EA2", "EE1C25") + emblem("EE1C25", cx=1.1, r=0.26)
F["Slovenia"] = h("FFFFFF", "005DA4", "ED1C24") + emblem("005DA4", cx=1.0, cy=0.7, r=0.24)
F["Spain"] = hw((1, "AA151B"), (2, "F1BF00"), (1, "AA151B")) + rect(0.82, 0.76, 0.3, 0.5, "AD1519")
F["Sweden"] = nordic("006AA7", "FECC00")
F["Switzerland"] = solid("FF0000") + rect(1.26, 0.52, 0.48, 0.96, "FFFFFF") + \
    rect(1.02, 0.76, 0.96, 0.48, "FFFFFF")
F["Ukraine"] = h("0057B7", "FFDD00")
F["United Kingdom"] = UNION_JACK
F["Vatican City"] = v("FFE000", "FFE000", "FFFFFF", "FFFFFF") + emblem("D4AF37", cx=2.25, r=0.3)

# --- Americas -------------------------------------------------------------
F["Antigua and Barbuda"] = solid("CE1126") + poly([(0, 0), (W, 0), (2.2, 1.1), (0.8, 1.1)], "000000") + \
    poly([(0.95, 0.72), (2.05, 0.72), (1.95, 1.1), (1.05, 1.1)], "0072C6") + \
    poly([(1.05, 0.95), (1.95, 0.95), (1.9, 1.1), (1.1, 1.1)], "FFFFFF") + \
    disc(1.5, 0.62, 0.22, "FCD116")
F["Argentina"] = h("74ACDF", "FFFFFF", "74ACDF") + disc(1.5, 1.0, 0.22, "F6B40E")
F["Bahamas"] = h("00778B", "FFC72C", "00778B") + poly([(0, 0), (0.95, 1.0), (0, 2.0)], "000000")
F["Barbados"] = v("00267F", "FFC726", "00267F") + poly([(1.42, 0.5), (1.58, 0.5), (1.56, 1.5),
                                                        (1.44, 1.5)], "000000")
F["Belize"] = hw((1, "CE1126"), (6, "003F87"), (1, "CE1126")) + disc(1.5, 1.0, 0.44, "FFFFFF")
F["Bolivia"] = h("D52B1E", "F9E300", "007A33") + emblem("B08D2A", r=0.26)
F["Brazil"] = solid("009739") + poly([(0.22, 1.0), (1.5, 0.22), (2.78, 1.0), (1.5, 1.78)], "FEDD00") + \
    disc(1.5, 1.0, 0.44, "012169") + rect(1.06, 0.92, 0.88, 0.1, "FFFFFF")
F["Canada"] = vw((1, "FF0000"), (2, "FFFFFF"), (1, "FF0000")) + \
    poly([(1.5, 0.42), (1.62, 0.78), (1.84, 0.68), (1.76, 0.98), (1.96, 1.04), (1.74, 1.2),
          (1.82, 1.36), (1.58, 1.32), (1.54, 1.62), (1.46, 1.62), (1.42, 1.32), (1.18, 1.36),
          (1.26, 1.2), (1.04, 1.04), (1.24, 0.98), (1.16, 0.68), (1.38, 0.78)], "FF0000")
F["Chile"] = solid("FFFFFF") + rect(0, 1.0, W, 1.0, "D52B1E") + rect(0, 0, 1.0, 1.0, "0039A6") + \
    star(0.5, 0.5, 0.3, "FFFFFF")
F["Colombia"] = hw((2, "FCD116"), (1, "003893"), (1, "CE1126"))
F["Costa Rica"] = hw((1, "002B7F"), (1, "FFFFFF"), (2, "CE1126"), (1, "FFFFFF"), (1, "002B7F"))
F["Cuba"] = hw(*[(1, "002A8F" if i % 2 == 0 else "FFFFFF") for i in range(5)]) + \
    poly([(0, 0), (1.15, 1.0), (0, 2.0)], "CF142B") + star(0.42, 1.0, 0.3, "FFFFFF")
F["Dominica"] = solid("006B3F") + rect(0, 0.78, W, 0.44, "FCD116") + rect(1.28, 0, 0.44, H, "FCD116") + \
    rect(0, 0.93, W, 0.14, "000000") + rect(1.43, 0, 0.14, H, "000000") + disc(1.5, 1.0, 0.36, "D41C30")
F["Dominican Republic"] = solid("002D62") + rect(1.5, 0, 1.5, 1.0, "CE1126") + \
    rect(0, 1.0, 1.5, 1.0, "CE1126") + rect(0, 0.84, W, 0.32, "FFFFFF") + \
    rect(1.34, 0, 0.32, H, "FFFFFF")
F["Ecuador"] = hw((2, "FFDD00"), (1, "034EA2"), (1, "ED1C24")) + emblem("8B6914", r=0.3)
F["El Salvador"] = h("0F47AF", "FFFFFF", "0F47AF") + emblem("0F47AF", r=0.24)
F["Grenada"] = solid("CE1126") + poly([(0.3, 0.3), (2.7, 0.3), (1.5, 1.0)], "FCD116") + \
    poly([(0.3, 1.7), (2.7, 1.7), (1.5, 1.0)], "FCD116") + \
    poly([(0.3, 0.3), (0.3, 1.7), (1.5, 1.0)], "007A5E") + \
    poly([(2.7, 0.3), (2.7, 1.7), (1.5, 1.0)], "007A5E") + disc(1.5, 1.0, 0.24, "CE1126")
F["Guatemala"] = v("4997D0", "FFFFFF", "4997D0") + emblem("4E7A27", r=0.24)
F["Guyana"] = solid("009E49") + poly([(0, 0), (2.1, 1.0), (0, 2.0)], "FFFFFF") + \
    poly([(0, 0.12), (1.86, 1.0), (0, 1.88)], "FCD116") + poly([(0, 0), (1.1, 1.0), (0, 2.0)], "000000") + \
    poly([(0, 0.18), (0.86, 1.0), (0, 1.82)], "CE1126")
F["Haiti"] = h("00209F", "D21034") + rect(1.1, 0.72, 0.8, 0.56, "FFFFFF")
F["Honduras"] = h("0073CF", "FFFFFF", "0073CF") + star(1.5, 1.0, 0.17, "0073CF") + \
    star(1.0, 0.86, 0.13, "0073CF") + star(2.0, 0.86, 0.13, "0073CF") + \
    star(1.1, 1.18, 0.13, "0073CF") + star(1.9, 1.18, 0.13, "0073CF")
F["Jamaica"] = solid("009B3A") + poly([(0, 0), (W, H), (W, 1.7), (0.42, 0)], "FED100") + \
    poly([(W, 0), (0, H), (0.42, H), (W, 0.3)], "FED100") + \
    poly([(0, 0.28), (1.1, 1.0), (0, 1.72)], "000000") + \
    poly([(W, 0.28), (1.9, 1.0), (W, 1.72)], "000000")
F["Mexico"] = v("006847", "FFFFFF", "CE1126") + emblem("6B4A1B", r=0.26)
F["Nicaragua"] = h("0067C6", "FFFFFF", "0067C6") + emblem("5BA4DC", r=0.24)
F["Panama"] = solid("FFFFFF") + rect(1.5, 0, 1.5, 1.0, "D21034") + rect(0, 1.0, 1.5, 1.0, "005293") + \
    star(0.75, 0.5, 0.26, "005293") + star(2.25, 1.5, 0.26, "D21034")
F["Paraguay"] = h("D52B1E", "FFFFFF", "0038A8") + emblem("0F7C3F", r=0.22)
F["Peru"] = v("D91023", "FFFFFF", "D91023")
F["Saint Kitts and Nevis"] = solid("009E49") + poly([(0, 2.0), (0, 1.4), (2.3, 0), (W, 0)], "CE1126") + \
    poly([(0, 1.35), (0, 0.62), (2.3, 0) if False else (1.6, 0), (W, 0), (W, 0.1)], "000000") + \
    diag("000000", 0.3) + diag("FFC72C", 0.05)
F["Saint Lucia"] = solid("66CCFF") + poly([(1.5, 0.3), (2.2, 1.7), (0.8, 1.7)], "FFFFFF") + \
    poly([(1.5, 0.52), (2.02, 1.7), (0.98, 1.7)], "000000") + \
    poly([(1.5, 1.0), (2.1, 1.82), (0.9, 1.82)], "FCD116")
F["Saint Vincent and the Grenadines"] = vw((1, "0072C6"), (1, "FCD116"), (1, "009E49")) + \
    poly([(1.3, 0.7), (1.45, 1.0), (1.3, 1.3), (1.15, 1.0)], "009E49") + \
    poly([(1.7, 0.7), (1.85, 1.0), (1.7, 1.3), (1.55, 1.0)], "009E49") + \
    poly([(1.5, 1.1), (1.65, 1.4), (1.5, 1.7), (1.35, 1.4)], "009E49")
F["Suriname"] = hw((2, "377E3F"), (1, "FFFFFF"), (4, "B40A2D"), (1, "FFFFFF"), (2, "377E3F")) + \
    star(1.5, 1.0, 0.34, "ECC81D")
F["Trinidad and Tobago"] = solid("DA1A35") + diag("FFFFFF", 0.4, up=False) + diag("000000", 0.26, up=False)
F["United States"] = (hw(*[(1, "B31942" if i % 2 == 0 else "FFFFFF") for i in range(13)])
                      + rect(0, 0, 1.2, 1.0769, "0A3161")
                      + sum([star(0.1 + c * 0.1, 0.09 + r * 0.1077, 0.042, "FFFFFF")
                             for r in range(9) for c in range(6 if r % 2 == 0 else 5)
                             if (c * 0.1 + (0.05 if r % 2 else 0)) < 1.1], []))
F["Uruguay"] = (solid("FFFFFF")
                + sum([rect(0, i * 0.2222, W, 0.2222, "0038A8") for i in (1, 3, 5, 7)], [])
                + rect(0, 0, 1.3334, 1.1111, "FFFFFF") + star(0.667, 0.555, 0.34, "FCD116"))
F["Venezuela"] = hw((1, "FFCC00"), (1, "00247D"), (1, "CF142B")) + \
    sum([star(1.5 + 0.62 * math.cos(math.radians(a)), 1.08 + 0.62 * math.sin(math.radians(a)),
              0.1, "FFFFFF") for a in range(200, 345, 20)], [])

# --- Africa ---------------------------------------------------------------
F["Algeria"] = v("006233", "FFFFFF", "FFFFFF") + rect(0, 0, 1.5, H, "006233") + \
    crescent(1.42, 1.0, 0.42, "D21034", "FFFFFF", 0.3) + star(1.78, 1.0, 0.2, "D21034")
F["Angola"] = h("CE1126", "000000") + disc(1.5, 1.0, 0.3, "FFCB00")
F["Benin"] = vw((2, "008751"), (3, "FCD116")) + rect(1.2, 1.0, 1.8, 1.0, "E8112D")
F["Botswana"] = hw((9, "75AADB"), (1, "FFFFFF"), (4, "000000"), (1, "FFFFFF"), (9, "75AADB"))
F["Burkina Faso"] = h("EF2B2D", "009E49") + star(1.5, 1.0, 0.34, "FCD116")
F["Burundi"] = solid("CE1126") + poly([(0, 0), (1.5, 1.0), (0, 2.0)], "1EB53A") + \
    poly([(W, 0), (1.5, 1.0), (W, 2.0)], "1EB53A") + saltire("FFFFFF", 0.26) + disc(1.5, 1.0, 0.44, "FFFFFF")
F["Cameroon"] = v("007A5E", "CE1126", "FCD116") + star(1.5, 1.0, 0.26, "FCD116")
F["Cape Verde"] = hw((6, "003893"), (1, "FFFFFF"), (1, "CF2027"), (1, "FFFFFF"), (3, "003893")) + \
    sum([star(1.0 + 0.58 * math.cos(math.radians(a)), 1.22 + 0.58 * math.sin(math.radians(a)),
              0.09, "F7D116") for a in range(0, 360, 36)], [])
F["Central African Republic"] = hw((1, "003082"), (1, "FFFFFF"), (1, "289728"), (1, "FFCE00")) + \
    rect(1.3, 0, 0.4, H, "D21034") + star(0.4, 0.3, 0.2, "FFCE00")
F["Chad"] = v("002664", "FECB00", "C60C30")
F["Comoros"] = h("FFF100", "FFFFFF", "CE1126", "0072C6") + poly([(0, 0), (1.3, 1.0), (0, 2.0)], "009A44") + \
    crescent(0.5, 1.0, 0.3, "FFFFFF", "009A44", 0.34)
F["Congo"] = solid("009543") + poly([(W, 0), (W, H), (0.6, H)], "DC241F") + \
    poly([(0, H), (1.5, H), (W, 0), (1.5, 0)], "FBDE4A")
F["Djibouti"] = h("6AB2E7", "12AD2B") + poly([(0, 0), (1.2, 1.0), (0, 2.0)], "FFFFFF") + \
    star(0.38, 1.0, 0.26, "D7141A")
F["Egypt"] = h("CE1126", "FFFFFF", "000000") + emblem("C09300", r=0.24)
F["Equatorial Guinea"] = h("3E9A00", "FFFFFF", "E32118") + poly([(0, 0), (0.9, 1.0), (0, 2.0)], "0073CE")
F["Eritrea"] = solid("4189DD") + poly([(0, 0), (W, 0), (0, 1.0)], "EA0437") + \
    poly([(0, 2.0), (W, 2.0), (0, 1.0)], "12AD2B") + \
    poly([(0, 0), (1.5, 1.0), (0, 2.0)], "EA0437") + disc(0.5, 1.0, 0.3, "FFC726")
F["Eswatini"] = hw((3, "3E5EB9"), (1, "FFD900"), (8, "B10C0C"), (1, "FFD900"), (3, "3E5EB9")) + \
    rect(0.6, 0.92, 1.8, 0.16, "FFFFFF")
F["Ethiopia"] = h("078930", "FCDD09", "DA121A") + disc(1.5, 1.0, 0.42, "0F47AF") + \
    star(1.5, 1.0, 0.3, "FCDD09")
F["Gabon"] = h("009E60", "FCD116", "3A75C4")
F["Gambia"] = hw((6, "CE1126"), (1, "FFFFFF"), (6, "0C1C8C"), (1, "FFFFFF"), (6, "3A7728"))
F["Ghana"] = h("CE1126", "FCD116", "006B3F") + star(1.5, 1.0, 0.26, "000000")
F["Guinea"] = v("CE1126", "FCD116", "009460")
F["Guinea-Bissau"] = solid("CE1126") + rect(1.0, 0, 2.0, 1.0, "FCD116") + rect(1.0, 1.0, 2.0, 1.0, "009E49") + \
    poly([(0.5, 0.64), (0.62, 0.95), (0.86, 0.95), (0.66, 1.14), (0.74, 1.44), (0.5, 1.26),
          (0.26, 1.44), (0.34, 1.14), (0.14, 0.95), (0.38, 0.95)], "000000")
F["Ivory Coast"] = v("F77F00", "FFFFFF", "009E60")
F["Kenya"] = hw((2, "000000"), (1, "FFFFFF"), (4, "BB0000"), (1, "FFFFFF"), (2, "006600")) + \
    poly([(1.5, 0.3), (1.78, 1.0), (1.5, 1.7), (1.22, 1.0)], "FFFFFF") + \
    poly([(1.5, 0.46), (1.7, 1.0), (1.5, 1.54), (1.3, 1.0)], "BB0000")
F["Lesotho"] = hw((3, "00209F"), (4, "FFFFFF"), (3, "009543")) + \
    poly([(1.5, 0.74), (1.66, 1.1), (1.34, 1.1)], "000000")
F["Liberia"] = (hw(*[(1, "BF0A30" if i % 2 == 0 else "FFFFFF") for i in range(11)])
                + rect(0, 0, 1.0909, 0.909, "002868") + star(0.545, 0.455, 0.3, "FFFFFF"))
F["Libya"] = hw((1, "239E46"), (2, "000000"), (1, "E70013")) + \
    crescent(1.42, 1.0, 0.26, "FFFFFF", "000000", 0.32) + star(1.72, 1.0, 0.14, "FFFFFF")
F["Madagascar"] = solid("FFFFFF") + rect(1.0, 0, 2.0, 1.0, "FC3D32") + rect(1.0, 1.0, 2.0, 1.0, "007E3A")
F["Malawi"] = h("000000", "CE1126", "339E35") + \
    sum([poly([(1.5, 0.4), (1.5 + 0.44 * math.cos(math.radians(a)), 0.4 + 0.44 * math.sin(math.radians(a))),
               (1.5 + 0.44 * math.cos(math.radians(a + 18)), 0.4 + 0.44 * math.sin(math.radians(a + 18)))],
              "CE1126") for a in range(180, 360, 36)], []) + disc(1.5, 0.4, 0.2, "CE1126")
F["Mali"] = v("14B53A", "FCD116", "CE1126")
F["Mauritania"] = solid("006233") + rect(0, 0, W, 0.26, "CD2A3E") + rect(0, 1.74, W, 0.26, "CD2A3E") + \
    crescent(1.5, 1.16, 0.44, "FFD700", "006233", 0.0, tilt=0.26) + star(1.5, 0.72, 0.17, "FFD700")
F["Mauritius"] = h("EA2839", "1A206D", "FFD500", "00A551")
F["Morocco"] = solid("C1272D") + poly([(1.5, 0.52), (1.78, 1.38), (1.05, 0.85), (1.95, 0.85),
                                       (1.22, 1.38)], "006233")
F["Mozambique"] = h("009A44", "FFFFFF", "000000") + rect(0, 0.62, W, 0.1, "FFFFFF") + \
    rect(0, 1.28, W, 0.1, "FFFFFF") + rect(0, 0.72, W, 0.56, "000000") + \
    poly([(0, 0), (1.1, 1.0), (0, 2.0)], "D21034") + star(0.4, 1.0, 0.26, "FFD100")
F["Namibia"] = solid("003580") + poly([(0, 2.0), (W, 0), (W, 0.46), (0.7, 2.0)], "FFFFFF") + \
    poly([(0, 2.0), (W, 0), (W, 0.0)], "009543") + solid("003580") + \
    poly([(0, 2.0), (W, 0), (W, 0.52), (0.78, 2.0)], "FFFFFF") + \
    poly([(0.16, 2.0), (W, 0.1), (W, 0.42), (0.62, 2.0)], "D21034") + \
    poly([(2.0, 0), (W, 0), (W, 1.0)], "009543") + \
    poly([(0, 1.0), (0, 2.0), (1.0, 2.0)], "009543") + \
    sum([poly([(0.62, 0.52),
               (0.62 + 0.42 * math.cos(math.radians(a)), 0.52 + 0.42 * math.sin(math.radians(a))),
               (0.62 + 0.42 * math.cos(math.radians(a + 14)), 0.52 + 0.42 * math.sin(math.radians(a + 14)))],
              "FFCE00") for a in range(0, 360, 30)], []) + disc(0.62, 0.52, 0.2, "FFCE00")
F["Niger"] = h("E05206", "FFFFFF", "0DB02B") + disc(1.5, 1.0, 0.26, "E05206")
F["Nigeria"] = v("008751", "FFFFFF", "008751")
F["Rwanda"] = hw((2, "20603D"), (1, "FAD201"), (1, "00A1DE")) + disc(2.4, 0.42, 0.22, "E5BE01")
F["Sao Tome and Principe"] = hw((1, "12AD2B"), (2, "FFCE00"), (1, "12AD2B")) + \
    poly([(0, 0), (0.9, 1.0), (0, 2.0)], "D21034") + star(1.7, 1.0, 0.18, "000000") + \
    star(2.2, 1.0, 0.18, "000000")
F["Senegal"] = v("00853F", "FDEF42", "E31B23") + star(1.5, 1.0, 0.26, "00853F")
F["Seychelles"] = solid("FFFFFF") + poly([(0, 2.0), (0, 0), (1.0, 0)], "003F87") + \
    poly([(0, 2.0), (1.0, 0), (2.0, 0)], "FCD856") + \
    poly([(0, 2.0), (2.0, 0), (W, 0), (W, 0.2)], "D62828") + \
    poly([(0, 2.0), (W, 0.4), (W, 1.2)], "FFFFFF") + poly([(0, 2.0), (W, 1.2), (W, 2.0)], "007A3D")
F["Sierra Leone"] = h("1EB53A", "FFFFFF", "0072C6")
F["Somalia"] = solid("4189DD") + star(1.5, 1.0, 0.46, "FFFFFF")
F["South Africa"] = (solid("002395") + poly([(0, 0), (W, 0), (W, 0.84), (0, 0.84)], "DE3831")
                     + poly([(0, 1.16), (W, 1.16), (W, 2.0), (0, 2.0)], "002395")
                     + poly([(0, 0), (1.4, 1.0), (0, 2.0)], "007A4D")
                     + poly([(0, 0.26), (1.1, 1.0), (0, 1.74)], "FFFFFF")
                     + poly([(0, 0.52), (0.82, 1.0), (0, 1.48)], "000000")
                     + poly([(0, 0), (W, 0), (W, 0.1), (0.1, 0.9)], "FFFFFF")
                     + poly([(0, 2.0), (W, 2.0), (W, 1.9), (0.1, 1.1)], "FFFFFF")
                     + poly([(1.1, 0.86), (W, 0.4), (W, 0.84), (1.4, 0.98)], "FFCB00")
                     + poly([(1.1, 1.14), (W, 1.6), (W, 1.16), (1.4, 1.02)], "FFCB00"))
F["South Sudan"] = hw((5, "000000"), (1, "FFFFFF"), (5, "DA121A"), (1, "FFFFFF"), (5, "0F47AF")) + \
    poly([(0, 0), (1.2, 1.0), (0, 2.0)], "078930") + star(0.42, 1.0, 0.24, "FCDD09")
F["Sudan"] = h("D21034", "FFFFFF", "000000") + poly([(0, 0), (1.1, 1.0), (0, 2.0)], "007229")
F["Tanzania"] = solid("1EB53A") + poly([(W, 0), (W, 2.0), (0, 2.0)], "00A3DD") + \
    poly([(0, 2.0), (W, 0), (W, 0.42), (0.62, 2.0)], "FCD116") + \
    poly([(0, 2.0), (W, 0), (W, 0.3), (0.44, 2.0)], "000000") + \
    poly([(0, 1.72), (2.58, 0), (W, 0), (0.4, 2.0), (0, 2.0)], "000000")
F["Togo"] = hw(*[(1, "006A4E" if i % 2 == 0 else "FFCE00") for i in range(5)]) + \
    rect(0, 0, 1.2, 1.2, "D21034") + star(0.6, 0.6, 0.34, "FFFFFF")
F["Tunisia"] = solid("E70013") + disc(1.5, 1.0, 0.56, "FFFFFF") + \
    crescent(1.46, 1.0, 0.38, "E70013", "FFFFFF", 0.3) + star(1.66, 1.0, 0.18, "E70013")
F["Uganda"] = hw(*[(1, c) for c in ("000000", "FCDC04", "D90000", "000000", "FCDC04", "D90000")]) + \
    disc(1.5, 1.0, 0.42, "FFFFFF") + emblem("4B3621", r=0.2)
F["Zambia"] = solid("198A00") + rect(2.0, 0.8, 0.34, 1.2, "DE2010") + \
    rect(2.34, 0.8, 0.34, 1.2, "000000") + rect(2.68, 0.8, 0.32, 1.2, "EF7D00") + \
    poly([(2.3, 0.2), (2.6, 0.5), (2.3, 0.72), (2.0, 0.5)], "EF7D00")
F["Zimbabwe"] = hw(*[(1, c) for c in ("319208", "FFD200", "D40000", "000000",
                                      "D40000", "FFD200", "319208")]) + \
    poly([(0, 0), (1.1, 1.0), (0, 2.0)], "FFFFFF") + star(0.4, 1.0, 0.34, "D40000")

# --- Middle East & Central Asia ------------------------------------------
F["Afghanistan"] = v("000000", "BE0000", "007A36") + emblem("FFFFFF", r=0.3)
F["Armenia"] = h("D90012", "0033A0", "F2A800")
F["Azerbaijan"] = h("0092BC", "E4002B", "00AF66") + \
    crescent(1.42, 1.0, 0.26, "FFFFFF", "E4002B", 0.3) + star(1.78, 1.0, 0.16, "FFFFFF")
F["Bahrain"] = solid("FFFFFF") + poly([(0.7, 0), (W, 0), (W, 2.0), (0.7, 2.0)] , "CE1126") + \
    poly([(0.7, 0), (1.0, 0.2), (0.7, 0.4), (1.0, 0.6), (0.7, 0.8), (1.0, 1.0), (0.7, 1.2),
          (1.0, 1.4), (0.7, 1.6), (1.0, 1.8), (0.7, 2.0), (0.4, 2.0), (0.4, 0)], "FFFFFF")
F["Bangladesh"] = solid("006A4E") + disc(1.35, 1.0, 0.56, "F42A41")
F["Bhutan"] = solid("FFCD00") + poly([(0, 2.0), (W, 0), (W, 2.0)], "FF4E12") + emblem("FFFFFF", r=0.3)
F["Brunei"] = solid("F7E017") + poly([(0, 0.4), (W, 0.1), (W, 0.75), (0, 1.05)], "FFFFFF") + \
    poly([(0, 1.05), (W, 0.75), (W, 1.1), (0, 1.4)], "000000") + emblem("CF1126", r=0.28)
F["Cambodia"] = hw((1, "032EA1"), (2, "E00025"), (1, "032EA1")) + rect(1.25, 0.72, 0.5, 0.56, "FFFFFF")
F["China"] = solid("EE1C25") + star(0.6, 0.55, 0.34, "FFDE00") + star(1.15, 0.26, 0.13, "FFDE00") + \
    star(1.38, 0.52, 0.13, "FFDE00") + star(1.38, 0.86, 0.13, "FFDE00") + star(1.15, 1.1, 0.13, "FFDE00")
F["India"] = h("FF9933", "FFFFFF", "138808") + ring(1.5, 1.0, 0.28, "000080", 0.05, "FFFFFF")
F["Indonesia"] = h("FF0000", "FFFFFF")
F["Iran"] = h("239F40", "FFFFFF", "DA0000") + emblem("DA0000", r=0.22)
F["Iraq"] = h("CE1126", "FFFFFF", "000000") + rect(1.1, 0.88, 0.8, 0.24, "007A3D")
F["Israel"] = solid("FFFFFF") + rect(0, 0.26, W, 0.22, "0038B8") + rect(0, 1.52, W, 0.22, "0038B8") + \
    poly([(1.5, 0.66), (1.79, 1.17), (1.21, 1.17)], "FFFFFF") + \
    poly([(1.5, 1.34), (1.21, 0.83), (1.79, 0.83)], "FFFFFF") + \
    path("M1.5,0.66 L1.79,1.17 L1.21,1.17 Z M1.5,1.34 L1.21,0.83 L1.79,0.83 Z", "0038B8") + \
    path("M1.5,0.78 L1.69,1.11 L1.31,1.11 Z M1.5,1.22 L1.31,0.89 L1.69,0.89 Z", "FFFFFF")
F["Jordan"] = h("000000", "FFFFFF", "007A3D") + poly([(0, 0), (1.2, 1.0), (0, 2.0)], "CE1126") + \
    star(0.44, 1.0, 0.17, "FFFFFF", points=7)
F["Kazakhstan"] = solid("00AFCA") + disc(1.6, 0.88, 0.3, "FEC50C") + \
    sum([poly([(1.6, 0.88),
               (1.6 + 0.52 * math.cos(math.radians(a)), 0.88 + 0.52 * math.sin(math.radians(a))),
               (1.6 + 0.52 * math.cos(math.radians(a + 10)), 0.88 + 0.52 * math.sin(math.radians(a + 10)))],
              "FEC50C") for a in range(0, 360, 24)], []) + rect(0.1, 0, 0.16, H, "FEC50C")
F["Kuwait"] = h("007A3D", "FFFFFF", "CE1126") + poly([(0, 0), (0.9, 0.667), (0.9, 1.333), (0, 2.0)], "000000")
F["Kyrgyzstan"] = solid("E8112D") + disc(1.5, 1.0, 0.34, "FFEF00") + \
    sum([poly([(1.5, 1.0),
               (1.5 + 0.52 * math.cos(math.radians(a)), 1.0 + 0.52 * math.sin(math.radians(a))),
               (1.5 + 0.52 * math.cos(math.radians(a + 11)), 1.0 + 0.52 * math.sin(math.radians(a + 11)))],
              "FFEF00") for a in range(0, 360, 15)], [])
F["Laos"] = hw((1, "CE1126"), (2, "002868"), (1, "CE1126")) + disc(1.5, 1.0, 0.36, "FFFFFF")
F["Lebanon"] = hw((1, "ED1C24"), (2, "FFFFFF"), (1, "ED1C24")) + \
    poly([(1.5, 0.62), (1.84, 1.22), (1.62, 1.22), (1.62, 1.38), (1.38, 1.38), (1.38, 1.22),
          (1.16, 1.22)], "00A651")
F["Maldives"] = solid("D21034") + rect(0.52, 0.34, 1.96, 1.32, "007E3A") + \
    crescent(1.62, 1.0, 0.4, "FFFFFF", "007E3A", 0.34)
F["Mongolia"] = v("C4272F", "015197", "C4272F") + rect(0.22, 0.52, 0.16, 0.96, "F9CF02") + \
    disc(0.3, 0.42, 0.16, "F9CF02") + poly([(0.14, 1.54), (0.46, 1.54), (0.3, 1.78)], "F9CF02")
F["Myanmar"] = h("FECB00", "34B233", "EA2839") + star(1.5, 1.0, 0.52, "FFFFFF")
F["Nepal"] = solid("FFFFFF") + poly([(0.2, 0.1), (2.3, 0.78), (0.9, 0.78), (2.3, 1.5),
                                     (0.2, 1.5)], "DC143C") + \
    disc(0.78, 1.18, 0.14, "FFFFFF") + disc(0.72, 0.5, 0.12, "FFFFFF")
F["North Korea"] = hw((1, "024FA2"), (1, "FFFFFF"), (4, "ED1C27"), (1, "FFFFFF"), (1, "024FA2")) + \
    disc(1.05, 1.0, 0.34, "FFFFFF") + star(1.05, 1.0, 0.26, "ED1C27")
F["Oman"] = solid("FFFFFF") + rect(0.7, 0, 2.3, 0.667, "C8102E") + rect(0.7, 1.333, 2.3, 0.667, "008000") + \
    rect(0, 0, 0.7, H, "C8102E") + emblem("FFFFFF", cx=0.35, cy=0.36, r=0.16)
F["Pakistan"] = solid("01411C") + rect(0, 0, 0.75, H, "FFFFFF") + \
    crescent(1.75, 1.08, 0.42, "FFFFFF", "01411C", 0.3) + star(2.08, 0.74, 0.2, "FFFFFF")
F["Palestine"] = h("000000", "FFFFFF", "007A3D") + poly([(0, 0), (1.2, 1.0), (0, 2.0)], "CE1126")
F["Philippines"] = h("0038A8", "CE1126") + poly([(0, 0), (1.3, 1.0), (0, 2.0)], "FFFFFF") + \
    star(0.44, 1.0, 0.2, "FCD116") + star(0.14, 0.2, 0.11, "FCD116") + star(0.14, 1.8, 0.11, "FCD116") + \
    star(1.0, 1.0, 0.11, "FCD116")
F["Qatar"] = solid("FFFFFF") + poly([(1.0, 0), (W, 0), (W, 2.0), (1.0, 2.0)], "8A1538") + \
    poly([(1.0, 0), (1.3, 0.22), (1.0, 0.44), (1.3, 0.67), (1.0, 0.89), (1.3, 1.11), (1.0, 1.33),
          (1.3, 1.56), (1.0, 1.78), (1.3, 2.0), (0.7, 2.0), (0.7, 0)], "FFFFFF")
F["Saudi Arabia"] = solid("006C35") + rect(0.5, 0.72, 2.0, 0.18, "FFFFFF") + \
    rect(0.6, 1.2, 1.8, 0.12, "FFFFFF") + rect(0.6, 1.32, 0.16, 0.26, "FFFFFF")
F["Singapore"] = h("ED2939", "FFFFFF") + crescent(0.62, 0.5, 0.34, "FFFFFF", "ED2939", 0.34) + \
    sum([star(1.1 + 0.26 * math.cos(math.radians(a)), 0.5 + 0.26 * math.sin(math.radians(a)),
              0.1, "FFFFFF") for a in range(-90, 270, 72)], [])
F["South Korea"] = solid("FFFFFF") + disc(1.5, 1.0, 0.4, "CD2E3A") + \
    halfdisc(1.5, 1.0, 0.4, "0047A0", upper=False) + \
    disc(1.3, 1.0, 0.2, "CD2E3A") + disc(1.7, 1.0, 0.2, "0047A0") + \
    rect(0.36, 0.3, 0.42, 0.07, "000000") + rect(0.36, 0.42, 0.42, 0.07, "000000") + \
    rect(0.36, 0.54, 0.42, 0.07, "000000") + rect(2.22, 1.39, 0.42, 0.07, "000000") + \
    rect(2.22, 1.51, 0.42, 0.07, "000000") + rect(2.22, 1.63, 0.42, 0.07, "000000")
F["Sri Lanka"] = solid("FFBE29") + rect(0.1, 0.1, 0.72, 1.8, "FFBE29") + \
    rect(0.16, 0.16, 0.3, 1.68, "00534E") + rect(0.5, 0.16, 0.3, 1.68, "EB7400") + \
    rect(0.92, 0.16, 1.92, 1.68, "8D153A") + emblem("FFBE29", cx=1.88, r=0.34)
F["Syria"] = h("CE1126", "FFFFFF", "000000") + star(1.1, 1.0, 0.2, "007A3D") + \
    star(1.9, 1.0, 0.2, "007A3D")
F["Taiwan"] = solid("FE0000") + rect(0, 0, 1.5, 1.0, "000095") + \
    sum([poly([(0.75, 0.5),
               (0.75 + 0.42 * math.cos(math.radians(a)), 0.5 + 0.42 * math.sin(math.radians(a))),
               (0.75 + 0.42 * math.cos(math.radians(a + 15)), 0.5 + 0.42 * math.sin(math.radians(a + 15)))],
              "FFFFFF") for a in range(0, 360, 30)], []) + disc(0.75, 0.5, 0.2, "FFFFFF")
F["Tajikistan"] = hw((2, "CC0000"), (3, "FFFFFF"), (2, "006600")) + \
    emblem("F8C300", r=0.2) + sum([star(1.5 + 0.34 * math.cos(math.radians(a)),
                                        0.66 + 0.34 * math.sin(math.radians(a)), 0.07, "F8C300")
                                   for a in range(-162, 199, 72)], [])
F["Thailand"] = hw((1, "A51931"), (1, "F4F5F8"), (2, "2D2A4A"), (1, "F4F5F8"), (1, "A51931"))
F["Timor-Leste"] = solid("DC241F") + poly([(0, 0), (1.5, 1.0), (0, 2.0)], "FFC726") + \
    poly([(0, 0), (1.0, 1.0), (0, 2.0)], "000000") + star(0.4, 1.0, 0.26, "FFFFFF")
F["Turkey"] = solid("E30A17") + crescent(1.15, 1.0, 0.44, "FFFFFF", "E30A17", 0.28) + \
    star(1.72, 1.0, 0.22, "FFFFFF")
F["Turkmenistan"] = solid("28AE66") + rect(0.42, 0, 0.46, H, "FFFFFF") + \
    rect(0.46, 0.1, 0.38, 1.1, "C4342A") + \
    crescent(1.55, 0.62, 0.26, "FFFFFF", "28AE66", 0.3) + \
    sum([star(1.95 + i * 0.2, 0.62, 0.09, "FFFFFF") for i in range(5)], [])
F["United Arab Emirates"] = h("00732F", "FFFFFF", "000000") + rect(0, 0, 0.75, H, "FF0000")
F["Uzbekistan"] = hw((6, "0099B5"), (1, "CE1126"), (6, "FFFFFF"), (1, "CE1126"), (6, "1EB53A")) + \
    crescent(0.52, 0.34, 0.24, "FFFFFF", "0099B5", 0.34) + \
    sum([star(0.95 + c * 0.24, 0.2 + r * 0.22, 0.07, "FFFFFF")
         for r, n in enumerate((3, 4, 5)) for c in range(n)], [])
F["Vietnam"] = solid("DA251D") + star(1.5, 1.0, 0.56, "FFFF00")
F["Yemen"] = h("CE1126", "FFFFFF", "000000")

# --- Asia-Pacific ---------------------------------------------------------
F["Australia"] = (solid("00008B") + jack()
                  + star(0.75, 1.5, 0.3, "FFFFFF", points=7)
                  + star(2.4, 0.3, 0.14, "FFFFFF", points=7)
                  + star(2.05, 0.82, 0.14, "FFFFFF", points=7)
                  + star(2.4, 1.3, 0.14, "FFFFFF", points=7)
                  + star(2.72, 0.95, 0.14, "FFFFFF", points=7)
                  + star(2.28, 0.58, 0.08, "FFFFFF", points=5))
F["Fiji"] = (solid("68BFE5") + jack()
             + rect(1.95, 0.55, 0.52, 0.86, "FFFFFF")
             + rect(1.95, 0.55, 0.52, 0.2, "CF142B"))
F["Kiribati"] = hw((1, "CE1126"), (1, "0033A0")) + \
    sum([rect(0, 1.0 + i * 0.2, W, 0.1, "FFFFFF") for i in range(5)], []) + \
    disc(1.5, 1.0, 0.3, "FCD116") + \
    sum([poly([(1.5, 1.0),
               (1.5 + 0.5 * math.cos(math.radians(a)), 1.0 + 0.5 * math.sin(math.radians(a))),
               (1.5 + 0.5 * math.cos(math.radians(a + 9)), 1.0 + 0.5 * math.sin(math.radians(a + 9)))],
              "FCD116") for a in range(180, 360, 18)], [])
F["Marshall Islands"] = solid("003893") + poly([(0, 2.0), (W, 0), (W, 0.46), (0.0, 2.0)], "DD7500") + \
    poly([(0, 2.0), (W, 0.0), (W, 0.3)], "FFFFFF") + \
    poly([(0, 1.86), (W, 0.0), (W, 0.26), (0, 2.0)], "DD7500") + \
    poly([(0, 1.7), (2.74, 0.0), (W, 0.0), (0, 1.9)], "FFFFFF") + star(0.6, 0.42, 0.3, "FFFFFF", points=24)
F["Micronesia"] = solid("75B2DD") + star(1.5, 0.62, 0.17, "FFFFFF") + star(1.5, 1.38, 0.17, "FFFFFF") + \
    star(1.12, 1.0, 0.17, "FFFFFF") + star(1.88, 1.0, 0.17, "FFFFFF")
F["Nauru"] = solid("002B7F") + rect(0, 0.92, W, 0.16, "FFC726") + star(0.9, 1.5, 0.26, "FFFFFF", points=12)
F["New Zealand"] = (solid("00247D") + jack()
                    + star(2.4, 0.42, 0.15, "FFFFFF") + star(2.1, 1.0, 0.15, "FFFFFF")
                    + star(2.68, 1.0, 0.15, "FFFFFF") + star(2.4, 1.56, 0.15, "FFFFFF"))
F["Palau"] = solid("4AADD6") + disc(1.3, 1.0, 0.44, "FFDE00")
F["Papua New Guinea"] = solid("CE1126") + poly([(0, 0), (0, 2.0), (W, 2.0)], "000000") + \
    poly([(0.45, 1.05), (0.62, 1.42), (0.45, 1.78), (0.28, 1.42)], "FFFFFF") + \
    star(0.78, 1.72, 0.14, "FFFFFF") + star(0.42, 0.72, 0.0, "FFFFFF") + \
    poly([(2.2, 0.35), (2.45, 0.52), (2.6, 0.42), (2.5, 0.7), (2.3, 0.62)], "FCD116")
F["Samoa"] = solid("CE1126") + rect(0, 0, 1.5, 1.0, "002B7F") + star(0.9, 0.42, 0.18, "FFFFFF") + \
    star(0.62, 0.68, 0.14, "FFFFFF") + star(0.95, 0.76, 0.1, "FFFFFF") + star(0.78, 0.26, 0.1, "FFFFFF") + \
    star(0.5, 0.5, 0.1, "FFFFFF")
F["Solomon Islands"] = solid("0051BA") + poly([(0, 2.0), (W, 0), (W, 2.0)], "215B33") + \
    poly([(0, 2.0), (W, 0), (W, 0.28), (0.42, 2.0)], "FCD116") + \
    star(0.3, 0.26, 0.13, "FFFFFF") + star(0.78, 0.26, 0.13, "FFFFFF") + \
    star(0.3, 0.72, 0.13, "FFFFFF") + star(0.78, 0.72, 0.13, "FFFFFF") + star(0.54, 0.49, 0.13, "FFFFFF")
F["Tonga"] = solid("C10000") + rect(0, 0, 1.2, 1.0, "FFFFFF") + rect(0.5, 0.18, 0.2, 0.64, "C10000") + \
    rect(0.3, 0.4, 0.6, 0.2, "C10000")
F["Tuvalu"] = (solid("5B97B1") + jack()
               + sum([star(1.7 + (i % 3) * 0.46, 0.5 + (i // 3) * 0.5, 0.11, "FFCE00")
                      for i in range(9)], []))
F["Vanuatu"] = solid("009543") + rect(0, 0, W, 1.0, "D21034") + \
    poly([(0, 0.84), (W, 0.84), (W, 1.16), (0, 1.16)], "FDCE12") + \
    poly([(0, 0), (1.25, 1.0), (0, 2.0)], "000000") + \
    poly([(0, 0.2), (0.95, 1.0), (0, 1.8)], "FDCE12") + \
    poly([(0, 0.34), (0.8, 1.0), (0, 1.66)], "000000") + disc(0.38, 1.0, 0.2, "FDCE12")
F["Japan"] = solid("FFFFFF") + disc(1.5, 1.0, 0.6, "BC002D")
F["Malaysia"] = (hw(*[(1, "CC0001" if i % 2 == 0 else "FFFFFF") for i in range(14)])
                 + rect(0, 0, 1.5, 1.0, "010066")
                 + crescent(0.62, 0.52, 0.3, "FFCC00", "010066", 0.3)
                 + star(1.08, 0.52, 0.22, "FFCC00", points=14))

# -------------------------------------------------------------- the renderer

CS_HEADER = '''// <auto-generated>
//     Generated by tools/build_flags.py. Edit the table there, not this file.
// </auto-generated>
#nullable enable

namespace DoNet.Controls;

/// <summary>
/// Flag artwork for the country picker, as a list of primitives in a 3x2 box.
/// </summary>
/// <remarks>
/// <para>
/// Windows has no flag font - emoji flags render as two boxed letters - and there is
/// no licensed flag asset pack in this repository, so the flags are drawn from this
/// table by <see cref="FlagIcon"/>.
/// </para>
/// <para>
/// Three primitives only, space separated, semicolon between layers: <c>R x y w h
/// RRGGBB</c>, <c>E cx cy rx ry RRGGBB</c> and <c>P pathData RRGGBB</c>. Layers paint
/// in order. Everything structural - bands, cantons, Nordic crosses, saltires, stars,
/// crescents - was expanded into those three by the generator, so nothing here needs
/// interpreting beyond a split.
/// </para>
/// <para>
/// Drawn for a 24x16 dropdown row. At that size the field carries the recognition and
/// a coat of arms is four pixels across, so emblems are simplified to the shape that
/// reads at a glance.
/// </para>
/// </remarks>
internal static class FlagData
{
'''


def main() -> int:
    table = {k: v for k, v in F.items() if v}
    arms = []
    for name in sorted(table):
        spec = ";".join(table[name])
        arms.append(f'        "{name}" => "{spec}",')

    body = (CS_HEADER
            + "    /// <summary>\n"
            + "    /// The layers for <paramref name=\"country\"/>, or null when there is no\n"
            + "    /// drawing for it.\n"
            + "    /// </summary>\n"
            + "    internal static string? For(string? country) => country switch\n    {\n"
            + "\n".join(arms)
            + "\n        _ => null,\n    };\n}\n")

    target = ROOT / "DoNet" / "Controls" / "FlagData.cs"
    target.write_text(body, encoding="utf-8")
    print(f"{len(table)} flags -> {target.relative_to(ROOT)} ({len(body)} bytes)")

    missing = [c for c in countries() if c not in table]
    if missing:
        print(f"\n{len(missing)} countries in the catalog with no flag:")
        for name in missing:
            print(f"  {name}")

    proof(table)
    return 0


def countries():
    import re
    src = (ROOT / "DoNet" / "Models" / "Catalogs.cs").read_text()
    block = re.search(r"Countries \{ get; \} = new\[\]\s*\{(.*?)\};", src, re.S)
    return re.findall(r'"([^"]+)"', block.group(1)) if block else []


def proof(table):
    try:
        from PIL import Image, ImageDraw, ImageFont
    except ImportError:
        print("Pillow not installed - skipping the proof sheet")
        return

    names = sorted(table)
    cols, cw, ch, ss = 10, 108, 86, 3
    rows = (len(names) + cols - 1) // cols
    img = Image.new("RGB", (cols * cw * ss, rows * ch * ss), "#FFFFFF")
    dr = ImageDraw.Draw(img)
    try:
        font = ImageFont.truetype(str(ROOT / "DoNet/Assets/Fonts/Baloo2-Regular.ttf"), 10 * ss)
    except OSError:
        font = ImageFont.load_default()

    fw, fh = 72, 48
    for i, name in enumerate(names):
        ox = (i % cols) * cw * ss + (cw - fw) // 2 * ss
        oy = (i // cols) * ch * ss + 10 * ss
        sx = fw * ss / W
        sy = fh * ss / H
        layer = Image.new("RGB", (fw * ss, fh * ss), "#FFFFFF")
        ld = ImageDraw.Draw(layer)
        for part in table[name]:
            kind, rest = part.split(" ", 1)
            *args, color = rest.rsplit(" ", 1)
            color = "#" + color
            if kind == "R":
                x, y, w, hh = (float(n) for n in args[0].split())
                if w <= 0 or hh <= 0:
                    continue
                ld.rectangle([x * sx, y * sy, (x + w) * sx, (y + hh) * sy], fill=color)
            elif kind == "E":
                cx, cy, rx, ry = (float(n) for n in args[0].split())
                if rx <= 0 or ry <= 0:
                    continue
                ld.ellipse([(cx - rx) * sx, (cy - ry) * sy, (cx + rx) * sx, (cy + ry) * sy], fill=color)
            elif kind == "P":
                # One P can carry several subpaths; each M starts a new one.
                for sub in args[0].split("M"):
                    pts = []
                    for token in sub.replace("L", " ").replace("Z", " ").split():
                        if "," in token:
                            px, py = token.split(",")
                            pts.append((float(px) * sx, float(py) * sy))
                    if len(pts) > 2:
                        ld.polygon(pts, fill=color)
        img.paste(layer, (ox, oy))
        dr.rectangle([ox, oy, ox + fw * ss, oy + fh * ss], outline="#C8CDD4", width=ss)
        dr.text(((i % cols) * cw * ss + 4 * ss, oy + fh * ss + 4 * ss),
                name[:17], font=font, fill="#2B2F33")

    img = img.resize((cols * cw, rows * ch), Image.LANCZOS)
    out = ROOT / "tools" / "flag-proof.png"
    img.save(out)
    print(f"proof sheet -> {out.relative_to(ROOT)}")


if __name__ == "__main__":
    sys.exit(main())
