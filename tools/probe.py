#!/usr/bin/env python3
"""
probe.py - scanline colour runs, the tape measure for pixels.

Prints run-length-encoded colour segments along horizontal or vertical
lines through an image: every stretch of similar pixels with its exact
average colour and its pixel range. Edges, radii, gaps and tints stop
being guesses and become numbers.

Usage:
  python3 tools/probe.py IMAGE --hline Y [Y ...] [--vline X [X ...]]
                            [--crop X,Y,W,H] [--tol T]

Segments shorter than 2px are merged into their neighbours unless they
differ strongly (edges/antialiasing bands are kept and marked).
"""

import argparse
import sys

import numpy as np
from PIL import Image


def hexc(px):
    return "#{:02X}{:02X}{:02X}".format(*(int(round(v)) for v in px))


def rle(line, tol, along_offset, origin, axis):
    runs = []
    start = 0
    acc = [line[0]]
    for i in range(1, len(line)):
        if np.abs(line[i].astype(int) - line[start].astype(int)).max() > tol:
            runs.append((start, i, np.mean(acc, axis=0)))
            start = i
            acc = [line[i]]
        else:
            acc.append(line[i])
    runs.append((start, len(line), np.mean(acc, axis=0)))
    print(f"  {axis}={origin}:")
    for a, b, col in runs:
        print(f"    {a + along_offset:5d}..{b + along_offset:5d}  "
              f"({b - a:4d}px)  {hexc(col)}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("image")
    ap.add_argument("--hline", type=int, nargs="*", default=[])
    ap.add_argument("--vline", type=int, nargs="*", default=[])
    ap.add_argument("--crop", default=None)
    ap.add_argument("--tol", type=int, default=8)
    args = ap.parse_args()

    im = Image.open(args.image)
    if im.mode != "RGBA":
        im = im.convert("RGBA")
    base = Image.new("RGBA", im.size, (255, 255, 255, 255))
    base.alpha_composite(im)
    im = base.convert("RGB")
    ox = oy = 0
    if args.crop:
        x, y, w, h = (int(v) for v in args.crop.split(","))
        ox, oy = x, y
        im = im.crop((x, y, x + w, y + h))
    arr = np.asarray(im)
    print(f"==== {args.image} crop={args.crop or 'full'} "
          f"(coordinates below are ORIGINAL image coordinates) ====")
    for y in args.hline:
        rle(arr[y - oy], args.tol, ox, y, "y")
    for x in args.vline:
        rle(arr[:, x - ox], args.tol, oy, x, "x")


if __name__ == "__main__":
    sys.exit(main())
