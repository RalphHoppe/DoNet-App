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

The tokens are the SVGs' own: ink `#1D212C`, line `#E2E5EB`, the brand
gradient `#FCBD2E` to `#F26A17` top-right to bottom-left, the paper `#FFFFFF`.
They live in `App.xaml`, named for what they are. The type is Nunito - the
design's own choice - instanced statically from Google's variable font (OFL
1.1, licence bundled) and referenced per weight.

The window is a startup screen's register - small and centred, like the
Visual Studio splash - with one centred column per screen. Everything
interactive wears the glass language: a transparent glassy frame, the white
button inside it, the icon inside that - the window's X, each field's three
separate pieces (padlock, word, eye), and every action. The icons are the
design's own vectors, redrawn as strokes on their own canvases.

Motion follows the SVGs' stylesheet, translated property by property: the
splash's entrance (slabs slide, ring cascades, letters rise) then the living
logo (two waves of light walk the ring's segments, nodes pulse in phase, bars
drift, halo breathes). All of it runs on transforms, opacity and dash offsets - nothing
animates layout - and none of it runs when the system says animations are off.

## The layout of the code

```
EasyNet/
  App.xaml(.cs)        tokens and the composition root
  MainWindow.xaml(.cs) the chrome-free shell: a drag strip, one close button
  Controls/            the mark, the wordmark, the password field
  Views/               splash, create password, unlock, the vault placeholder
  ViewModels/          one mind per screen
  Services/            the vault (PBKDF2 + AES-GCM verifier), paths, navigation
  Converters/          the one true/false-to-visibility bridge
tools/
  easynet_geometry.py  design SVGs -> the vector controls
  validate.py          the checks a build would catch, for machines without one
```

## Where it stands

The three startup screens are real: the splash plays and routes, the password
creation makes an actual vault (`%LOCALAPPDATA%\EasyNet\vault.json` - a
PBKDF2-derived key sealing a verifier with AES-GCM; no data in it yet), and
unlock really unlocks. The vault's own screens - where the records will live -
are the next design's to give.
