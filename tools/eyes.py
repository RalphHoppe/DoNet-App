#!/usr/bin/env python3
"""
eyes.py - deterministic sight for a no-vision agent.

Turns a raster into text an agent can actually read instead of guess from:

  * a glyph block-map of the layout - the image resampled to a grid where
    every cell is one character naming its color family and brightness, so
    the layout can be read like a map legend
  * a color census - the palette that actually occupies the canvas, with
    how much of it and where it lives
  * luminance profiles - row and column brightness bands that make card
    edges and shadow gradients visible as numbers
  * structure - the dominant connected regions against the page colour,
    their bounds, fills, and estimated corner radii, plus circle detection
  * OCR with boxes and pixel heights - what text exists, where, and how
    big it is relative to the canvas (which is a font-size measurement)

Usage:
  python3 tools/eyes.py IMAGE [--blocks N] [--crop X,Y,W,H] [--no-ocr]

Everything is measured, nothing is guessed. The block map describes layout;
the census and profiles describe light; OCR describes words. Used together
they are the closest thing to looking at the image that can be printed.
"""

import argparse
import colorsys
import math
import sys

import numpy as np
from PIL import Image

try:
    import cv2
except ImportError:
    cv2 = None

try:
    from rapidocr_onnxruntime import RapidOCR
except ImportError:
    RapidOCR = None


# --------------------------------------------------------------------------
# colour naming
# --------------------------------------------------------------------------

NEUTRAL_BANDS = [
    (0.97, "W"),   # pure white
    (0.90, "w"),   # near white
    (0.75, "."),   # light grey
    (0.55, ":"),   # grey
    (0.35, "n"),   # dark grey
    (0.00, "K"),   # near black
]

HUE_FAMILIES = [
    (0, 15, "R"), (15, 45, "O"), (45, 70, "Y"), (70, 160, "G"),
    (160, 200, "C"), (200, 255, "B"), (255, 290, "P"), (290, 360, "M"),
]


def family(rgb):
    """One character naming a colour's family and brightness."""
    r, g, b = (v / 255.0 for v in rgb)
    mx, mn = max(r, g, b), min(r, g, b)
    lum = (mx + mn) / 2.0
    if mx == mn:
        sat = 0.0
    else:
        sat = (mx - mn) / (1.0 - abs(2 * lum - 1.0))
    if sat < 0.14 or mx < 0.06:
        for edge, glyph in NEUTRAL_BANDS:
            if lum >= edge:
                return glyph
        return "K"
    hue = colorsys.rgb_to_hsv(r, g, b)[0] * 360.0
    for lo, hi, glyph in HUE_FAMILIES:
        if lo <= hue < hi:
            # light tints keep the lowercase letter, saturated/dark go upper
            return glyph if lum < 0.72 else glyph.lower()
    return "?"


def hexc(rgb):
    return "#{:02X}{:02X}{:02X}".format(*(int(round(v)) for v in rgb))


# --------------------------------------------------------------------------
# loading
# --------------------------------------------------------------------------

def load(path, crop=None):
    im = Image.open(path)
    if im.mode != "RGBA":
        im = im.convert("RGBA")
    if crop:
        x, y, w, h = crop
        im = im.crop((x, y, x + w, y + h))
    # composite over white - screenshots are opaque, tints stay honest
    base = Image.new("RGBA", im.size, (255, 255, 255, 255))
    base.alpha_composite(im)
    return base.convert("RGB")


# --------------------------------------------------------------------------
# block map
# --------------------------------------------------------------------------

def block_map(im, blocks):
    w, h = im.size
    gw = min(blocks, w)
    gh = max(1, round(gw * h / w))
    small = im.resize((gw, gh), Image.BOX)
    px = np.asarray(small)
    print(f"== BLOCK MAP  {gw}x{gh} cells, each cell {w/gw:.1f}x{h/gh:.1f}px " 
          f"(character = colour family/brightness) ==")
    print("legend: W white  w near-white  . light-grey  : grey  n dark-grey  K black")
    print("        oO orange/amber  yY cream/yellow  gG green  cC cyan  bB blue  "
          "pP purple  mM magenta (lowercase = light tint)")
    for row in range(gh):
        print("".join(family(px[row, col]) for col in range(gw)))
    print()


# --------------------------------------------------------------------------
# census
# --------------------------------------------------------------------------

def census(im):
    px = np.asarray(im).reshape(-1, 3)
    # quantise to 5 bits per channel so near-equal colours merge honestly
    q = (px // 8).astype(np.uint16)
    keys = q[:, 0] << 10 | q[:, 1] << 5 | q[:, 2]
    uniq, counts = np.unique(keys, return_counts=True)
    order = np.argsort(-counts)
    total = len(keys)
    print(f"== COLOUR CENSUS  top buckets of {total} pixels ==")
    for idx in order[:14]:
        bucket = ((uniq[idx] >> 10) & 31, (uniq[idx] >> 5) & 31, uniq[idx] & 31)
        mean = np.array(bucket) * 8 + 4
        pct = 100.0 * counts[idx] / total
        first = np.argmax(keys == uniq[idx])
        x, y = int(first % im.size[0]), int(first // im.size[0])
        print(f"  {hexc(mean)}  {pct:5.1f}%   first seen at ({x},{y})")
    print()


# --------------------------------------------------------------------------
# luminance profiles
# --------------------------------------------------------------------------

def profiles(im):
    g = np.asarray(im.convert("L"), dtype=float)
    h, w = g.shape
    rows = g.mean(axis=1)
    cols = g.mean(axis=0)

    def bands(v, n=48):
        step = max(1, len(v) // n)
        out = []
        for i in range(0, len(v), step):
            out.append(int(round(v[i:i + step].mean() / 25.5)))  # 0..10
        return out

    print("== LUMINANCE PROFILES  0=black .. 10=white ==")
    print("  rows (top to bottom):")
    print("   ", " ".join(f"{v:d}" for v in bands(rows)))
    print("  columns (left to right):")
    print("   ", " ".join(f"{v:d}" for v in bands(cols)))
    print()


# --------------------------------------------------------------------------
# structure
# --------------------------------------------------------------------------

def structure(im):
    if cv2 is None:
        print("== STRUCTURE  (skipped - no opencv) ==")
        return
    arr = np.asarray(im)
    h, w = arr.shape[:2]
    # page colour = median of the four 16px corner patches
    k = 16
    corners = np.concatenate([
        arr[:k, :k].reshape(-1, 3), arr[:k, -k:].reshape(-1, 3),
        arr[-k:, :k].reshape(-1, 3), arr[-k:, -k:].reshape(-1, 3)])
    bg = np.median(corners, axis=0)
    print(f"== STRUCTURE  page colour {hexc(bg)} ==")
    diff = np.abs(arr.astype(int) - bg).sum(axis=2)
    mask = (diff > 24).astype(np.uint8)
    n, lab, stats, _ = cv2.connectedComponentsWithStats(mask, 8)
    total = h * w
    order = np.argsort(-stats[1:, cv2.CC_STAT_AREA]) + 1
    shown = 0
    for idx in order:
        area = stats[idx, cv2.CC_STAT_AREA]
        if area < total * 0.004 or shown >= 8:
            break
        x, y, bw, bh = (stats[idx, cv2.CC_STAT_LEFT], stats[idx, cv2.CC_STAT_TOP],
                        stats[idx, cv2.CC_STAT_WIDTH], stats[idx, cv2.CC_STAT_HEIGHT])
        region = arr[lab == idx]
        mean = region.mean(axis=0)
        # corner radius from the contour perimeter:
        # P = 2(bw+bh) - 8r + 2*pi*r  =>  r = (2(bw+bh) - P) / (8 - 2*pi)
        contours, _ = cv2.findContours((lab == idx).astype(np.uint8),
                                       cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
        r_est = None
        if contours:
            perim = cv2.arcLength(max(contours, key=cv2.contourArea), True)
            denom = 8 - 2 * math.pi
            guess = (2 * (bw + bh) - perim) / denom
            if 0 < guess < min(bw, bh) / 2:
                r_est = guess
        rtxt = f" radius~{r_est:.0f}px" if r_est else ""
        print(f"  region {x:4d},{y:4d}  {bw:4d}x{bh:4d}px  "
              f"({100*bw/w:.1f}% x {100*bh/h:.1f}% of canvas)  "
              f"fill {hexc(mean)}  area {100*area/total:.1f}%{rtxt}")
        shown += 1

    # circles - hints for the round pieces of the field anatomy
    gray = cv2.cvtColor(arr, cv2.COLOR_RGB2GRAY)
    circles = cv2.HoughCircles(gray, cv2.HOUGH_GRADIENT, dp=1.3, param1=120,
                               param2=42, minDist=24, minRadius=10, maxRadius=90)
    if circles is not None:
        print("  circles (x, y, r, mean colour inside):")
        for cx, cy, r in circles[0][:10]:
            cx, cy, r = int(cx), int(cy), int(r)
            if r + 2 < min(h, w) and 0 <= cx - r and cx + r < w and 0 <= cy - r and cy + r < h:
                inside = arr[cy - r // 2:cy + r // 2, cx - r // 2:cx + r // 2]
                col = inside.reshape(-1, 3).mean(axis=0) if inside.size else bg
                print(f"    ({cx:4d},{cy:4d}) r={r:3d}  {hexc(col)}")
    print()


# --------------------------------------------------------------------------
# OCR
# --------------------------------------------------------------------------

def ocr(im):
    if RapidOCR is None:
        print("== OCR  (skipped - no rapidocr) ==")
        return
    big = im.resize((im.size[0] * 2, im.size[1] * 2), Image.LANCZOS)
    engine = RapidOCR()
    result, _ = engine(np.asarray(big))
    print(f"== OCR  at 2x  (heights in original pixels) ==")
    if not result:
        print("  (no text found)")
        print()
        return
    W, H = im.size
    items = []
    for box, text, conf in result:
        xs = [p[0] for p in box]
        ys = [p[1] for p in box]
        items.append((sum(ys) / 4 / 2, sum(xs) / 4 / 2,
                      (max(ys) - min(ys)) / 2, text, conf))
    for cy, cx, hpx, text, conf in sorted(items):
        print(f"  y={cy:6.0f} x={cx:6.0f} h={hpx:4.0f}px "
              f"({100*hpx/H:4.1f}% of height)  '{text}'")
    print()


# --------------------------------------------------------------------------

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("image")
    ap.add_argument("--blocks", type=int, default=96)
    ap.add_argument("--crop", default=None,
                    help="X,Y,W,H in original pixels")
    ap.add_argument("--no-ocr", action="store_true")
    args = ap.parse_args()

    crop = None
    if args.crop:
        crop = tuple(int(v) for v in args.crop.split(","))
    im = load(args.image, crop)
    print(f"==== {args.image}  {im.size[0]}x{im.size[1]}px "
          f"(crop={args.crop or 'full'}) ====\n")
    block_map(im, args.blocks)
    census(im)
    profiles(im)
    structure(im)
    if not args.no_ocr:
        ocr(im)


if __name__ == "__main__":
    sys.exit(main())
