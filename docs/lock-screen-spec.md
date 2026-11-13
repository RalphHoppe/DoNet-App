# Lock Screen — Measured Specification

Source of truth: `new_design/screens_svg/lock_screen.svg` (Figma export, pushed by
the designer). Every value below was measured from that file — geometry read
exactly from the element tree, paint resolved through the full cascade, text
weights identified by stem-width and ink-density comparison against real Nunito
instances, and the whole render cross-checked with resvg + scanline probes.

All coordinates are in the SVG's own design space: **canvas 700 × 400**
(exactly half the 1400 × 800 mockup PNG; ratios identical).

---

## 1. Canvas and window

| Property | Value |
|---|---|
| Canvas | 700 × 400 (aspect 1.75) |
| Background | `#FFFFFF`, corner radius 20 (the window itself is rounded) |
| Close button | circle Ø 32 at (652, 16) — 16 px from top and right |

## 2. The piece anatomy (glass, light)

Every interactive piece is the same construction:

| Layer | Value |
|---|---|
| Fill | white at **80 %** opacity (over the white page it reads pure white) |
| Edge | `#EAE9E9` at **20 %** — a ring **2.75** thick (close button: 1.75) |
| Shadow | the piece's own shape **eroded 8 px**, offset **(0, +10)**, gaussian blur **12**, **black at 7.84 %** |

Pieces (all 48 tall unless noted):

| Piece | Geometry | Position |
|---|---|---|
| Field leading (padlock) | circle Ø 48 | x 176–224 |
| Field pill | 372 × 48, radius 24 (full pill) | x 234–606 |
| Field trailing (eye) | circle Ø 48 | x 616–664 |
| Unlock button | 148 × 48, radius 24 (full pill) | x 176–324, y 261–309 |
| Close button | circle Ø 32 | (652, 16) |

Gaps between the three field pieces: **10** each. Right margin of the form
(664 → 700) = 36 = left margin of the brand (36).

## 3. Icons (paths verbatim, design space)

- **Closed padlock** (field leading), centred in its circle (centre 200, 221),
  15 × 19.3, stroke `#424349` width 2 at 90 %, includes the Ø 3 keyhole dot
  as a stroked subpath.
- **Eye** (field trailing), centred (640, 221), 16.7 × 11.7, stroke `#74757A`
  width 1.7, round caps, iris is a stroked subpath.
- **Open padlock** (button), 12 × 16 combined, stroke `#424349` width 1.6,
  round caps/joins; body is a 12 × 9 rounded rect (r 2) plus shackle arc and
  keyhole tick.
- **X** (close button), centred (668, 32), 10.6 × 10.6, fill `#494242` —
  identical to the canonical X icon.

Exact path data lives in the SVG (single-source; do not fork it).

## 4. Typography — measured, weights proven

Identified by stem-thickness and ink-density comparison against instanced
Nunito statics (heading matched ExtraBold at ratio 1.00, subtext matched Light
at 1.03, label/button matched Bold, placeholder matched Regular):

| Role | Weight | Size | Colour | Notes |
|---|---|---|---|---|
| Heading `Unlock to access your data.` | **ExtraBold 800** | **25** | `#303138` | bbox 320.3 × 22.6 at (177.6, 97.2) |
| Subtext `Enter your password to unlock the app and access your locked data.` | **Light 300** | **14** | `#74757A` | bbox 422.4 × 12.6 at (177.2, 134.0) |
| Label `Password` | **Bold 700** | **14** | `#424349` | bbox 61.6 × 10.1 at (235.1, 174.0) — sits at the pill's left edge |
| Placeholder `Enter your password` | **Regular 400** | **15** | `#74757A` | starts 19.3 inside the pill |
| Button label `Unlock` | **Bold 700** | **15** | `#424349` | icon at pill-left + 20, 19 gap to label |

New font files required (already staged in `Assets/Fonts/`):
`Nunito-Light.ttf` (300) and `Nunito-ExtraBold.ttf` (800), instanced from the
official variable font. Name tables normalise to family `Nunito`.

## 5. Layout — vertical rhythm (design space)

| From → to | Gap |
|---|---|
| Window top → heading top | 97.2 |
| Heading (h 22.6) → subtext top | 14.3 |
| Subtext (h 12.6) → label top | 27.4 |
| Label (h 10.1) → field top | 12.9 |
| Field (h 48) → button top | 16 |
| Button (h 48) → window bottom | 91 |
| Form text left edge | 177.6 (≈ 1.6 right of the pieces' 176, optical) |

The form block (heading → button, y 97.2–309) is centred at y ≈ 203 —
optically centred (window centre 200).

## 6. Brand block (left column)

Rect 112 × 112 at (36, 144) — vertically centred (centre y = 200 exactly).
The SVG embeds it as a 1254 × 1254 transparent PNG whose measurable bands are:

| Band | Extent inside the 112 box | Size |
|---|---|---|
| Mark (E–N glyph) | y 17–75, x 21–92 | ≈ 71 × 58 |
| Wordmark `EasyNet` | y 78–100, x 7.6–105 | ≈ 97 × 22 |

The raster is the same mark + wordmark we already hold as vectors, so the
vector versions compose this block (crisper, animatable). Gap mark → wordmark
≈ 3.

## 7. Colour system (complete)

| Token | Value |
|---|---|
| Page | `#FFFFFF` |
| Piece fill | `#FFFFFF` @ 80 % |
| Piece edge | `#EAE9E9` @ 20 % |
| Shadow | `#000000` @ 7.84 % |
| Ink heading | `#303138` |
| Ink label / button / padlock | `#424349` (padlock @ 90 %) |
| Ink X icon | `#494242` |
| Ink subtext / placeholder / eye | `#74757A` |

## 8. Open questions (pending designer)

1. Behaviour at other window sizes — scale the whole composition, keep it
   design-sized and centre it, or reflow (brand beside/above)?
2. Default and minimum window size (design canvas is 700 × 400).
3. Create-password screen — same language derived locally, or wait for its
   SVG?

Measured with `tools/svg_review.py`, `tools/eyes.py`, `tools/probe.py`.
