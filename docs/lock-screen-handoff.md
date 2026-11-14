# Lock Screen — Designer's Handoff (canonical)

Source: the designer's Figma agent, inspecting the live `lock_screen` node.
This document supersedes `docs/lock-screen-spec.md` (which measured the
earlier fixed 700 × 400 export). Every value below is **the design's own**;
the app reproduces them with nested responsive layout, never absolute
positioning or proportional scaling.

> **Corrections from the user, 2026-10-05** (after reviewing the build):
> the pieces are **52 tall, not 48** — and the rounds 52 × 52 — so the
> corner radius is **26** (the inspector's 26 was the truth all along;
> the SVG exports' 48/24 was the export's own rounding). The submit
> button sits **left**, not centred. The window opens at **800 × 450**.
> The derived numbers move with the heights: form floor **304**
> (52+10+180+10+52), wrap threshold **596** (26+212+28+304+26), screen
> floor **356**, window minimum **372 × 400** with the resize borders.
> Sections 1–5 below keep the handoff's own numbers as it was given.

> **Second round, same day** (after reviewing the 52s build): the fields'
> pill rounds a little under its full capsule — radius **22** on the 52
> tall pill — and the submit buttons widen to a **168 floor** at the
> fields' own 52 height, rounded **22** (wider, not taller — the taller
> 56 was a misread, taken back). The window becomes a **fixed 800 × 450
> card**: borderless (no frame, no system shadow), not resizable, its
> corners square — a round-corner request was tried and withdrawn.
> The X appears only once the splash has handed over to a real screen. The
> create screen's second field carries no label — the first field's
> "Password" speaks for both, the mismatch hint is its only word. The
> create button's padlock-and-clock is now the designer's own vector,
> verbatim (mask-resolved, render-verified IoU 1.000).

---

## 1. Overall layout

All measurements in pixels. Fill = take available space, Hug = size to
content, Fixed = retain the dimension. Text wraps naturally; font size
never shrinks.

| Element | Auto layout | Width | Height | Spacing / alignment |
|---|---|---|---|---|
| Screen | Vertical | current 800; min 348 | current 420; min 400 | Padding **26** all sides; content centred H+V |
| Main content | Horizontal, **wrap** | Fill; min **296**, max **767** | Hug | Column gap **28**, wrapped-row gap **28**; children **left/top** |
| Logo | Image container | **212** | **212** | Original 1254 × 1254 raster, Fit/Contain, transparent margins preserved, **non-shrinking** |
| Form column | Vertical | Fill; min **296**, no max | Hug | Gap **20** introduction ↔ password section |
| Introduction | Vertical | Fill | Hug | Gap **6** heading ↔ description |
| Password section | Vertical | Fill | Hug | Gap **16** control row ↔ submit |
| Control row | Horizontal, **no wrap** | Fill | Hug (76) | Gap **10**; children align **bottom** |
| Leading lock | Horizontal | 48 | 48 | 20 × 20 icon, centred |
| Labeled input | Vertical | Fill; min **180** | Hug (76) | Label ↔ field gap **8** |
| Visibility | Vertical | 48 | 48 | 20 × 20 icon, centred |
| Submit | Horizontal | Hug; min **148** | 48 | Padding **16**, icon↔text gap **8**, group **centred** |

Inner containers: zero padding, no background, no border, no shadow.

### Input internals

Width Fill, height **48**, horizontal padding **18**, vertical centred,
one line with end ellipsis. The two circles align with the 48 px input
surface, not the label top — hence the row's bottom alignment.

## 2. Typography (Nunito, exactly)

| Text | Content | Weight | Size/line | Colour |
|---|---|---|---|---|
| Heading | Unlock to access your data. | ExtraBold 800 | 25 / 32 | #303138 |
| Description | Enter your password to unlock the app and access your locked data. | Regular 400 | 14 / 20 | #74757A |
| Input label | Password | Bold 700 | 14 / 20 | #424349 |
| Placeholder | Enter your password | Regular 400 | 15 / 22 | #74757A |
| Button label | Unlock | Bold 700 | 15 / 20 | #424349 |

Letter spacing 0, paragraph spacing 0. Heading/description/label left
aligned, Fill width, Hug height, may wrap. Button label centred, Hug.

## 3. Colours, borders, effects

- Screen: **#FFFFFF**, corner radius 20 (the outer surface), no border/shadow.
- Shared piece appearance (input, circles, submit):
  - Fill white **80%**;
  - corner radius **24** as exported (the handoff's inspector reads 26 —
    Figma clamps to half the 48 px height; both SVG exports render rx 24);
  - border **5.5 px, inside**, **#EAE9E9 at 20%** — an inset ring that does
    not consume layout space;
  - background blur 24 (a no-op over the solid white page);
  - shadow: offset **0 / +10**, blur **24**, spread **−8** (= the shape
    eroded 8 px), black at **7.84%**.
- Icons: containers 20 × 20; leading lock stroke ~1 px #424349 @ 90%;
  eye 1.7 px #74757A; submit unlock 1.6 px #424349; no solid fills;
  original artwork, not substitutes.

## 4. Reference positions at 800 × 420

Main content (26, 101) 748 × 218 — vertically centred: (420 − 218) / 2 = 101.
Logo (26, 101) 212 × 212. Form (266, 101) 508 × 218. Heading (266, 101)
508 × 32; description (266, 139) 508 × 20. Label (324, 179) 392 × 20;
input (324, 207) 392 × 48; lock (266, 207) 48 × 48; eye (726, 207) 48 × 48.
Submit (266, 271) 148 × 48.

## 5. Responsive rules

1. Available content width = screen − 52 (the 26 padding twice).
2. Content width stops growing at **767**.
3. The form takes the remaining width beside the logo.
4. The input takes the remaining width between the two fixed circles.
5. The control row never wraps.

Minimums: form **296** (48+10+180+10+48), screen **348** (26+296+26).
**Wrap threshold 588** (26+212+28+296+26): side by side at ≥ 588, stacked
below — logo above form, still left-aligned (centring the logo when
stacked is a separate, unmade choice). Short viewports **scroll**; the
400 px minimum height does not guarantee the stacked layout fits.

## 6. Implementation notes (this app)

- `Controls/Screen` owns the scaffold: white page, scroll-when-short,
  centring that yields to scrolling so nothing clips, padding 26, content
  max 767 (+52). The centre-or-scroll behaviour is structural: the scroll
  content is a fitter that is never shorter than the viewport, and the
  host's width is the viewport's own until the 819 cap, then centred —
  fill up to the cap, centre beyond it.
- Wrap is a switch in each page's code-behind at width 596 (the 52s moved
  it from the handoff's 588), measured on
  the page itself. It was an `AdaptiveTrigger` visual state first and
  never fired on a window 800 wide — the stacked base state shipped as-is
  and the form squeezed into the logo's 212 px column. The stacked
  posture is the form spanning all three columns, not sitting in the
  logo's; measured evidence in the build history.
- The window is a fixed 800 × 450 card: `SetBorderAndTitleBar(false,
  false)` takes the frame and the DWM shadow with it, `IsResizable` is
  false, and the corners are square. The X stays hidden until the splash
  hands the frame to a real screen (`RootFrame.Navigated`).
- The shadow recipe lives in `Controls/PieceShadow`; masks in
  `Assets/Shadows` are the eroded shapes.
- The logo raster is `Assets/Brand/logo.png` (the original 1254 × 1254),
  shown 212 × 212, Stretch Uniform.
- Type: Nunito Regular / Bold / ExtraBold only.
