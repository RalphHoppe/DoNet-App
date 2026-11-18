# Home Screen (Vault) — Measured from the Designer's Render

Source: the user's screenshot of the designer's home screen (`image-18`,
2026-10-05), rendered at 2×. Every number below is **halved to 1×** and
cross-checked against the app's own design system — the icons land on the
20 px containers, the labels on 15 px type, the card on the pieces' own
height, so the halving is confirmed, not assumed.

The screen: a **sidebar panel** floating on the white page, with the main
area beside it still waiting on its design.

## The panel

- 264 × 818, floating 16 from the page's left, top and bottom; the window
  is therefore **800 × 850** on this screen (sidebar 296 + main 504).
- Fill `#F8F8F8`, corner radius 20, no ring, the one shadow.
- Content padding 20 horizontal (content 224 wide), 35 top, 26 bottom.

## The content, top to bottom (y from the panel's top)

| Element | Geometry | Notes |
|---|---|---|
| Mark | 48 × 41.7 at x54 | the brand mark, `EasyNetMark` |
| Wordmark | 66 × 13 at x45 | `EasyNetWordmark` — Easy in ink, Net in brand |
| WORKSPACE | y149, caps 8 | Bold 11, letterspaced ~150, InkSoft, centred on the lockup's axis (x78) |
| Characters card | 224 × 48 at y181.5 | white fill, **hairline amber ring** (`#F8A622` @ 32%, 2.5), radius 24; amber icon 15.5 × 17 (`#B47B27`) + 16 + label Bold 15 InkStrong |
| 4 category rows | 224 × 35, pitch 56, from y245 | fill `#FBFBFB` (≈ PieceFill on the panel), **no ring**, radius 17.5; icon 20 + 16 + Regular 15 InkSoft |
| *(flex)* | y448 → 699 | the list's growing room |
| Compact view | y699 | Regular 13 InkSoft + a 5 × 8 chevron, right-aligned 16 in |
| Lock | 148 × 36 at y756 | PieceFill, radius 18, no ring; centred group: the verbatim closed padlock + 8 + "Lock" Bold 15 Ink |

Row copy: Characters / Websites / Accounts / Wallets / Services.
The category icons and the chevron ship as rasters cropped from the
design's own render (`Assets/Home/`), the same way the logo does — the
artwork is the designer's, unredrawn.

## Implementation notes (this app)

- `Views/VaultPage` draws it; the main area holds one honest sentence
  until its design arrives.
- The window sizes per screen: `IPreferredWindowSize` — the vault says
  800 × 850, everything else keeps the shell's 800 × 450. The window is
  not resizable, so its size is the screen's to declare.
- Open questions, flagged rather than guessed: whether "Compact view"
  is a toggle or a collapsible section (drawn static, as the render
  shows); whether the category rows become the navigation once the main
  area exists; what the main area holds.
