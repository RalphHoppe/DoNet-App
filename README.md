# EasyNet

A private vault for the things one person keeps: passwords first, records as
they arrive. Windows desktop, C#, WinUI 3 on .NET 8, unpackaged.

The first DoNet lives on in this repository's history - `main`'s earliest
commits and the `donet-v1` tag hold all of it. This tree is the second app,
built from scratch from a new design.

## Building

Visual Studio 2022 with the .NET 8 SDK and the Windows App SDK workload, or
plain `dotnet build`. The app is unpackaged: F5 runs the exe directly, no
certificate, no developer mode. Configuration `Debug|x64` is the inner loop;
`Release|x64` builds self-contained plain files that run on any Windows 10
1809+ machine.

## The design

`new_design/` is the source of truth - the animated SVGs and mockups the app
is drawn from. `tools/easynet_geometry.py` reads the SVGs and writes the
vector controls (`EasyNet/Controls/EasyNetMark.*`, `EasyNetWordmark.*`), so
the geometry on screen is the design's own, extracted rather than retyped.
Change the SVGs, run the script, commit the result.

`new_design/screens_svg/` holds the screens themselves as Figma SVG exports,
and every value in the app is measured from them (see
`docs/lock-screen-spec.md` for the full spec): the page is plain white, the
ink scale `#303138` / `#424349` / `#494242` / `#74757A`, pieces of white at
80% edged in `#EAE9E9` at 20%, and one shadow recipe - the piece's own shape
eroded 8 px, offset 10 px down, blurred, black at 7.84% - carried by
`PieceShadow` over small mask PNGs in `Assets/Shadows/`. The type is Nunito,
the design's own choice, instanced statically from Google's variable font
(OFL 1.1, licence bundled) and referenced per weight: Light, Regular, Bold
and ExtraBold. The measured type scale: headings ExtraBold 25, supporting
lines Light 14, labels Bold 14, the word and buttons 15.

The lock screen is a fixed 700 x 400 composition, centred in whatever room
the window has: the brand standing at the left in its 112 px box, the form
at the right - three field pieces that never touch (padlock round, word
pill, eye round, 48 px tall, 10 px of air between), the label above the
pill, the action pill with its icon left-anchored 20 px in. The window
opens at 800 x 540 (a comfortable fraction of any desktop, DPI-aware,
clamped to the work area) and can shrink to 740 x 480 - the composition
plus its margins - and never below it, because there is nothing left to
show. The icons are the design's own vectors, verbatim, verified pixel for
pixel against its render.

Motion lives on the splash alone, per the design's word: the entrance (slabs
slide, ring cascades, letters rise) then the living logo (two waves of light
walk the ring's segments, nodes pulse in phase, bars drift, halo breathes).
The lock and password-generation screens hold the brand still. All motion
runs on transforms, opacity and dash offsets - nothing animates layout - and
none of it runs when the system says animations are off.

## The layout of the code

```
EasyNet/
  App.xaml(.cs)        tokens and the composition root
  MainWindow.xaml(.cs) the chrome-free shell: a drag strip, one close button
  Controls/            the mark, the wordmark, the password field, the shadow
  Views/               splash, create password, unlock, the vault placeholder
  ViewModels/          one mind per screen
  Services/            the vault (PBKDF2 + AES-GCM verifier), paths, navigation
  Converters/          the one true/false-to-visibility bridge
tools/
  easynet_geometry.py  design SVGs -> the vector controls
  eyes.py              sight for rasters: block maps, colour census, structure, OCR
  probe.py             scanline colour runs - edges, radii and tints to the pixel
  svg_review.py        full SVG inventory: geometry, paint cascade, render check
  validate.py          the checks a build would catch, for machines without one
```

## Where it stands

The three startup screens are real: the splash plays and routes, the password
creation makes an actual vault (`%LOCALAPPDATA%\EasyNet\vault.json` - a
PBKDF2-derived key sealing a verifier with AES-GCM; no data in it yet), and
unlock really unlocks. The vault's own screens - where the records will live -
are the next design's to give.
