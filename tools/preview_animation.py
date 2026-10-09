#!/usr/bin/env python3
"""
Renders the splash animation to a GIF so the timing can be reviewed without a Windows build.

This mirrors what DoNetLogo.xaml.cs does at runtime rather than approximating it: the same
timings, the same cubic easing, and the same per-contour dash behaviour that XAML applies
(each closed contour restarts the dash pattern, so every contour reveals min(its own
length, totalLength * progress)).

    python3 tools/preview_animation.py
"""
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from fontTools.pens.basePen import BasePen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

from extract_logo_geometry import (
    EM, FONT_PATH, GAP_OVER_INK_W, HOLE_OVER_RING_D, PAD,
    RING_D_OVER_INK_W, STROKE_W, WORD,
)

OUT = "/tmp/preview/splash_animation.gif"
WIDTH = 520           # rendered logo width in px
FPS = 25
SS = 2                # supersample factor

TEAL_DEEP = (0x49, 0xA4, 0x8C)
TEAL_SOFT = (0x83, 0xCD, 0xB4)

# --- timings, kept in step with DoNetLogo.xaml.cs ---------------------------
RING_DRAW_MS = 620
LETTER_START_MS = 250
LETTER_STAGGER_MS = 140
LETTER_DRAW_MS = 600
FILL_DELAY_MS = 430
FILL_FADE_MS = 260
HOLD_MS = 420
OUTRO_STAGGER_MS = 80
OUTRO_FILL_FADE_MS = 200
OUTRO_TRACE_DELAY_MS = 110
OUTRO_TRACE_MS = 380
OUTRO_RING_DELAY_MS = 240
OUTRO_RING_MS = 440


def ease_in_out(t):
    """CubicEase, EasingMode=EaseInOut."""
    return 4 * t * t * t if t < 0.5 else 1 - ((-2 * t + 2) ** 3) / 2


def ease_in(t):
    """CubicEase, EasingMode=EaseIn."""
    return t * t * t


def clamp01(v):
    return max(0.0, min(1.0, v))


class FlatPen(BasePen):
    """Flattens a glyph into polylines, one per contour."""

    def __init__(self, glyph_set, scale, baseline):
        super().__init__(glyph_set)
        self.scale, self.baseline = scale, baseline
        self.contours, self._cur, self._pt, self._start = [], [], (0, 0), (0, 0)

    def _t(self, pt):
        return (pt[0] * self.scale, self.baseline - pt[1] * self.scale)

    def _moveTo(self, pt):
        self._flush()
        self._pt = self._start = self._t(pt)
        self._cur = [self._pt]

    def _lineTo(self, pt):
        self._pt = self._t(pt)
        self._cur.append(self._pt)

    def _curveToOne(self, p1, p2, p3):
        self._bez(self._pt, self._t(p1), self._t(p2), self._t(p3))

    def _qCurveToOne(self, p1, p2):
        self._bez(self._pt, self._t(p1), self._t(p2))

    def _bez(self, *ctrl, steps=14):
        n = len(ctrl) - 1
        for i in range(1, steps + 1):
            t = i / steps
            pts = list(ctrl)
            for _ in range(n):
                pts = [((1 - t) * a[0] + t * b[0], (1 - t) * a[1] + t * b[1])
                       for a, b in zip(pts, pts[1:])]
            self._cur.append(pts[0])
        self._pt = ctrl[-1]

    def _closePath(self):
        if self._cur:
            self._cur.append(self._start)
        self._flush()

    def _flush(self):
        if len(self._cur) > 1:
            self.contours.append(self._cur)
        self._cur = []

    def done(self):
        self._flush()
        return self.contours


def contour_length(points):
    return sum(math.dist(a, b) for a, b in zip(points, points[1:]))


def partial(points, want):
    """The first `want` units of a polyline."""
    if want <= 0:
        return []
    out, used = [points[0]], 0.0
    for a, b in zip(points, points[1:]):
        seg = math.dist(a, b)
        if used + seg >= want:
            k = (want - used) / seg if seg else 0
            out.append((a[0] + (b[0] - a[0]) * k, a[1] + (b[1] - a[1]) * k))
            return out
        out.append(b)
        used += seg
    return out


def build():
    font = TTFont(FONT_PATH)
    scale_units = EM / font["head"].unitsPerEm
    cmap, glyph_set, hmtx = font.getBestCmap(), font.getGlyphSet(), font["hmtx"]

    def run(origin_x, baseline):
        pens, pen_x = [], origin_x
        for ch in WORD:
            g = cmap[ord(ch)]
            pen = FlatPen(glyph_set, scale_units, baseline)
            glyph_set[g].draw(TransformPen(pen, (1, 0, 0, 1, pen_x / scale_units, 0)))
            pens.append((ch, pen.done(), pen_x))
            pen_x += hmtx[g][0] * scale_units
        return pens

    probe = run(0.0, 0.0)
    xs = [p[0] for _, cs, _ in probe for c in cs for p in c]
    ys = [p[1] for _, cs, _ in probe for c in cs for p in c]
    ink_w, ink_top, ink_bot = max(xs) - min(xs), min(ys), max(ys)

    ring_d = ink_w * RING_D_OVER_INK_W
    gap = ink_w * GAP_OVER_INK_W
    ring_t = (ring_d - ring_d * HOLE_OVER_RING_D) / 2
    ring_r = (ring_d - ring_t) / 2

    text_x = PAD + ring_d + gap - min(xs)
    baseline = PAD - ink_top
    glyphs = run(text_x, baseline)

    ring_cx, ring_cy = PAD + ring_d / 2, PAD + (ink_bot - ink_top) / 2
    canvas_w = max(p[0] for _, cs, _ in glyphs for c in cs for p in c) + PAD
    canvas_h = max(PAD + (ink_bot - ink_top), ring_cy + ring_d / 2) + PAD

    k = WIDTH / canvas_w * SS
    W, H = int(canvas_w * k) + 40 * SS, int(canvas_h * k) + 40 * SS
    ox = oy = 20 * SS

    pac = ImageFont.truetype(FONT_PATH, int(EM * k))

    gradient = Image.new("RGB", (W, H))
    gd = ImageDraw.Draw(gradient)
    for x in range(W):
        t = clamp01((x - ox) / max(1, W - 2 * ox))
        gd.line([(x, 0), (x, H)],
                fill=tuple(int(TEAL_DEEP[i] + (TEAL_SOFT[i] - TEAL_DEEP[i]) * t) for i in range(3)))

    lengths = [sum(contour_length(c) for c in cs) for _, cs, _ in glyphs]
    ring_circ = 2 * math.pi * ring_r

    intro_end = max(RING_DRAW_MS,
                    LETTER_START_MS + (len(WORD) - 1) * LETTER_STAGGER_MS + FILL_DELAY_MS + FILL_FADE_MS)
    outro_start = intro_end + HOLD_MS
    outro_end = max((len(WORD) - 1) * OUTRO_STAGGER_MS + OUTRO_TRACE_DELAY_MS + OUTRO_TRACE_MS,
                    OUTRO_RING_DELAY_MS + OUTRO_RING_MS)
    total = outro_start + outro_end

    frames, step = [], 1000 // FPS
    for ms in range(0, int(total) + step, step):
        mask = Image.new("L", (W, H), 0)
        md = ImageDraw.Draw(mask)

        # ---- ring ----------------------------------------------------------
        if ms < outro_start:
            ring_p = ease_in_out(clamp01(ms / RING_DRAW_MS))
        else:
            o = ms - outro_start
            ring_p = 1 - ease_in(clamp01((o - OUTRO_RING_DELAY_MS) / OUTRO_RING_MS))
        if ring_p > 0.001:
            # PIL insets an arc's width from the bounding box, whereas XAML centres the
            # stroke on the path - so the box here is the ring's OUTER circle, not the
            # centre line, otherwise the preview would show a much thicker ring.
            outer = ring_d / 2
            box = [ox + (ring_cx - outer) * k, oy + (ring_cy - outer) * k,
                   ox + (ring_cx + outer) * k, oy + (ring_cy + outer) * k]
            md.arc(box, -90, -90 + 360 * ring_p, fill=255, width=max(1, round(ring_t * k)))

        # ---- letters -------------------------------------------------------
        for i, (ch, contours, pen_x) in enumerate(glyphs):
            if ms < outro_start:
                begin = LETTER_START_MS + i * LETTER_STAGGER_MS
                trace_p = ease_in_out(clamp01((ms - begin) / LETTER_DRAW_MS))
                fill_a = clamp01((ms - begin - FILL_DELAY_MS) / FILL_FADE_MS)
                trace_visible = ms < begin + FILL_DELAY_MS + FILL_FADE_MS
            else:
                o = ms - outro_start
                begin = (len(glyphs) - 1 - i) * OUTRO_STAGGER_MS
                trace_p = 1 - ease_in(clamp01((o - begin - OUTRO_TRACE_DELAY_MS) / OUTRO_TRACE_MS))
                fill_a = 1 - clamp01((o - begin) / OUTRO_FILL_FADE_MS)
                trace_visible = True

            if fill_a > 0.004:
                layer = Image.new("L", (W, H), 0)
                ImageDraw.Draw(layer).text(
                    (ox + pen_x * k, oy + baseline * k), ch, font=pac, fill=int(255 * fill_a), anchor="ls")
                mask.paste(ImageChops_lighter(mask, layer), (0, 0))

            if trace_visible and trace_p > 0.001:
                want = lengths[i] * trace_p
                for contour in contours:
                    pts = partial([(ox + x * k, oy + y * k) for x, y in contour],
                                  min(want, contour_length(contour)) * k)
                    if len(pts) > 1:
                        md.line(pts, fill=255, width=max(1, int(STROKE_W * k)), joint="curve")

        frame = Image.new("RGB", (W, H), "white")
        frame.paste(gradient, (0, 0), mask)
        frames.append(frame.resize((W // SS, H // SS), Image.LANCZOS))

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    frames[0].save(OUT, save_all=True, append_images=frames[1:], duration=step, loop=0, optimize=True)
    print(f"wrote {OUT}  ({len(frames)} frames, {total/1000:.2f}s, {os.path.getsize(OUT)//1024} KB)")
    print(f"  intro ends {intro_end}ms, hold {HOLD_MS}ms, outro {outro_end}ms")


def ImageChops_lighter(a, b):
    from PIL import ImageChops
    return ImageChops.lighter(a, b)


if __name__ == "__main__":
    build()
