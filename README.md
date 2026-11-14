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

`new_design/screens_svg/` holds the screens as Figma SVG exports, and
`docs/lock-screen-handoff.md` is the canonical spec: the designer's own
auto-layout rules, followed exactly. The page is plain white; the ink scale
is `#303138` / `#424349` / `#494242` / `#74757A`; pieces are white at 80%
wearing an inset 5.5 px ring of `#EAE9E9` at 20%; and one shadow recipe -
the piece's own shape eroded 8 px (spread -8), offset 10 px down, blur 24,
black at 7.84% - carried by `PieceShadow` over mask PNGs in
`Assets/Shadows/`. The type is Nunito, per weight (Regular, Bold,
ExtraBold): headings 25/32, supporting lines 14/20, labels 14/20, the word
and buttons 15.

The layout is nested and responsive, never absolute: the screen pads 26
all sides and caps content at 767; the logo (the design's original
1254 px raster, contained in a non-shrinking 212 px box) sits beside the
form while the window is at least 596 wide and stacks above it below that;
the form fills the remaining width with a floor of 304; the input fills
between the two fixed 52 px circles with a floor of 180; the control row
never wraps. The window is a fixed 800 x 450 card - borderless,
shadowless, not resizable, rounded by the system where the platform
offers it (Windows 11) - opened centred and clamped to the work area;
the screens still lay out responsively inside it should that ever
change, and short viewports scroll instead of clipping. The X stays
hidden until the splash has handed over to a real screen. The icons are
the design's own vectors, verbatim, verified pixel for pixel against
their renders - including the create button's padlock-and-clock.

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
  Controls/            the mark, the wordmark, the field, the shadow, the screen
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

While the create-password screen is still being reviewed against its design,
delete that `vault.json` to see it again: with the file gone the splash routes
back to creation, exactly as on a first run.
