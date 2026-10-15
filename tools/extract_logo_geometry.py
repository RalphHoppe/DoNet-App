#!/usr/bin/env python3
"""
Generates the XAML path geometry for the DoNet lockup from Pacifico-Regular.ttf.

The splash screen traces each letter with a StrokeDashOffset animation, which needs
(a) every glyph outline in one shared coordinate space and (b) the arc length of each
outline, because XAML expresses StrokeDashArray/StrokeDashOffset in multiples of
StrokeThickness rather than in absolute units.

Output is pasted into DoNet/Controls/DoNetLogo.xaml. Re-run only if the wordmark,
the font, or the ring proportions change.

    python3 tools/extract_logo_geometry.py
"""
import math
import os

from fontTools.pens.basePen import BasePen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

FONT_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                         "..", "DoNet", "Assets", "Fonts", "Pacifico-Regular.ttf")
WORD = "DoNet"
EM = 100.0                 # design-space em; the control is Viewbox-scaled to fit

# Proportions measured against the Figma export and confirmed with tools/preview_screens.py.
RING_D_OVER_INK_W = 0.365  # ring outer diameter / wordmark ink width
GAP_OVER_INK_W = 0.095     # ring-to-wordmark gap  / wordmark ink width
HOLE_OVER_RING_D = 0.470   # hole diameter / ring outer diameter

STROKE_W = 2.2             # trace thickness in design units
PAD = 2.0                  # keeps the round stroke caps off the canvas edge


class PathPen(BasePen):
    """Emits XAML path mini-language and accumulates a flattened arc length."""

    def __init__(self, glyph_set, scale, baseline_y):
        super().__init__(glyph_set)
        self._scale = scale
        self._baseline = baseline_y
        self.commands: list[str] = []
        self.length = 0.0
        self.minx = self.miny = math.inf
        self.maxx = self.maxy = -math.inf
        self._pt = (0.0, 0.0)
        self._start = (0.0, 0.0)

    # font units are Y-up; XAML is Y-down
    def _t(self, pt):
        p = (pt[0] * self._scale, self._baseline - pt[1] * self._scale)
        self.minx, self.maxx = min(self.minx, p[0]), max(self.maxx, p[0])
        self.miny, self.maxy = min(self.miny, p[1]), max(self.maxy, p[1])
        return p

    @staticmethod
    def _n(v):
        return f"{v:.2f}".rstrip("0").rstrip(".")

    def _emit(self, letter, *pts):
        self.commands.append(letter + " ".join(f"{self._n(x)},{self._n(y)}" for x, y in pts))

    def _moveTo(self, pt):
        p = self._t(pt)
        self._emit("M", p)
        self._pt = self._start = p

    def _lineTo(self, pt):
        p = self._t(pt)
        self._emit("L", p)
        self.length += math.dist(self._pt, p)
        self._pt = p

    def _curveToOne(self, p1, p2, p3):
        a, b, c = self._t(p1), self._t(p2), self._t(p3)
        self._emit("C", a, b, c)
        self.length += _flatten(self._pt, a, b, c)
        self._pt = c

    def _qCurveToOne(self, p1, p2):
        a, b = self._t(p1), self._t(p2)
        self._emit("Q", a, b)
        self.length += _flatten(self._pt, a, b)
        self._pt = b

    def _closePath(self):
        self.commands.append("Z")
        self.length += math.dist(self._pt, self._start)
        self._pt = self._start


def _flatten(*ctrl, steps=32):
    """Arc length of a quadratic or cubic Bezier by polyline approximation."""
    n = len(ctrl) - 1
    total, prev = 0.0, ctrl[0]
    for i in range(1, steps + 1):
        t = i / steps
        pt = _de_casteljau(ctrl, t, n)
        total += math.dist(prev, pt)
        prev = pt
    return total


def _de_casteljau(ctrl, t, n):
    pts = list(ctrl)
    for r in range(n):
        pts = [((1 - t) * pts[i][0] + t * pts[i + 1][0],
                (1 - t) * pts[i][1] + t * pts[i + 1][1]) for i in range(len(pts) - 1)]
    return pts[0]


def build():
    font = TTFont(FONT_PATH)
    scale = EM / font["head"].unitsPerEm
    cmap, glyph_set, hmtx = font.getBestCmap(), font.getGlyphSet(), font["hmtx"]

    def run(origin_x, baseline_y):
        """Draw the word once; returns per-glyph pens laid out from origin_x."""
        pens, pen_x = [], origin_x
        for ch in WORD:
            name = cmap[ord(ch)]
            pen = PathPen(glyph_set, scale, baseline_y)
            glyph_set[name].draw(TransformPen(pen, (1, 0, 0, 1, pen_x / scale, 0)))
            pens.append((ch, pen))
            pen_x += hmtx[name][0] * scale
        return pens

    # pass 1: measure the wordmark's true ink box with the baseline at y = 0
    probe = run(0.0, 0.0)
    ink_w = max(p.maxx for _, p in probe) - min(p.minx for _, p in probe)
    ink_top = min(p.miny for _, p in probe)
    ink_bot = max(p.maxy for _, p in probe)

    ring_d = ink_w * RING_D_OVER_INK_W
    gap = ink_w * GAP_OVER_INK_W
    hole_d = ring_d * HOLE_OVER_RING_D
    ring_t = (ring_d - hole_d) / 2            # annulus thickness
    ring_r = (ring_d - ring_t) / 2            # radius of the stroked centre line
    circumference = 2 * math.pi * ring_r

    # pass 2: place the word to the right of the ring, with everything padded into view
    text_x = PAD + ring_d + gap - min(p.minx for _, p in probe)
    baseline = PAD - ink_top
    glyphs = run(text_x, baseline)

    ring_cx = PAD + ring_d / 2
    ring_cy = PAD + (ink_bot - ink_top) / 2   # ring centred on the wordmark's optical middle

    canvas_w = max(p.maxx for _, p in glyphs) + PAD
    canvas_h = max(PAD + (ink_bot - ink_top), ring_cy + ring_d / 2) + PAD

    n = lambda v: f"{v:.2f}".rstrip("0").rstrip(".")

    print("=" * 78)
    print(f"canvas            {n(canvas_w)} x {n(canvas_h)}")
    print(f"wordmark ink      {n(ink_w)} wide, baseline y={n(baseline)}")
    print(f"ring              centre {n(ring_cx)},{n(ring_cy)}  outer d={n(ring_d)} "
          f"hole d={n(hole_d)} thickness={n(ring_t)}")
    print(f"ring centre-line  r={n(ring_r)}  circumference={n(circumference)}")
    print(f"stroke width      {STROKE_W}   (dash values below are length / strokeWidth)")
    print("=" * 78)
    print()
    print(f'<!-- ring: stroke this with StrokeThickness="{n(ring_t)}" to render the annulus -->')
    print(f'RING  dash="{n(circumference / ring_t)}"')
    print(f'      M{n(ring_cx)},{n(ring_cy - ring_r)}'
          f' A{n(ring_r)},{n(ring_r)} 0 1 1 {n(ring_cx)},{n(ring_cy + ring_r)}'
          f' A{n(ring_r)},{n(ring_r)} 0 1 1 {n(ring_cx)},{n(ring_cy - ring_r)} Z')
    print()
    for ch, pen in glyphs:
        print(f'<!-- {ch} : outline length {n(pen.length)} -->')
        print(f'{ch}  dash="{n(pen.length / STROKE_W)}"')
        print(f'   {" ".join(pen.commands)}')
        print()


if __name__ == "__main__":
    build()
