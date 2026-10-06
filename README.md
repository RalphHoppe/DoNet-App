# DoNet

A WinUI 3 / Windows App SDK desktop app.

## What is built

| Screen | File | Notes |
| --- | --- | --- |
| Splash | `DoNet/Views/SplashPage.xaml` | The DoNet lockup draws itself on, holds, un-draws, then navigates |
| Create password | `DoNet/Views/CreatePasswordPage.xaml` | First run only, with validation |
| Lock | `DoNet/Views/LockPage.xaml` | Every launch after that |
| Forgot password | `DoNet/Views/ForgotPasswordPage.xaml` | From the lock screen's link |
| Welcome | `DoNet/Views/WelcomePage.xaml` | "Welcome to DoNet" writes itself on, then hands over to home |
| Home | `DoNet/Views/HomePage.xaml` | Navigation rail and the title-bar logo; the content surface is still empty |

### Where each screen leads

```
splash ──► create password ──┐                     ┌──► lock ──► forgot password
            (first run)      ├──► welcome ──► home ─┤              │        │
       └──► lock ────────────┘                      └── lock ◄─────┘        │
                                                         button             ▼
                                                                    create password
                                                                      (after reset)
```

Both ways in go through the welcome screen rather than landing on home directly. It is
the same draw-on trick as the splash, minus the ring.

The splash decides between the last two by asking `IVaultService.IsInitialized`, which
is true once a vault exists on disk.

![splash animation](docs/splash-animation.gif)

## Running it

This is a Windows-only project. Open `DoNet.sln` in Visual Studio 2022 (workload:
*.NET desktop development* + *Windows App SDK*), let NuGet restore, then F5. The
`DoNet (Package)` launch profile is the one to use.

Two packages are added on top of the template:

- `CommunityToolkit.Mvvm` - source-generated `ObservableProperty` / `RelayCommand`
- `Microsoft.Extensions.DependencyInjection` - composition root in `App.xaml.cs`

## The draw-on animation

The wordmark is not text at runtime. `tools/gen_logo_control.py` reads
`Assets/Fonts/Pacifico-Regular.ttf`, pulls the outline of each letter of "DoNet", and
writes `Controls/DoNetLogo.xaml` with every glyph emitted twice:

- a hairline **trace** `Path`, drawn on by animating `StrokeDashOffset` from one full
  path length down to zero - a single dash as long as the whole outline sliding across it
- a solid **fill** `Path` that cross-fades in once its trace completes

Letters are staggered 140 ms apart, so the word appears to be written rather than
revealed. The outro runs the same thing backwards, last letter first. The ring is a
single stroked circle whose dash offset sweeps it into existence.

Three things worth knowing before editing it:

- **The path data is generated.** Re-run the script rather than hand-editing it; the
  `StrokeDashArray` values are arc-length measurements that will not survive a manual tweak.
- **`EnableDependentAnimation="True"` is required.** `StrokeDashOffset` cannot be handed
  to the compositor, so WinUI silently discards the animation without it.
- **A completed `Storyboard` only *holds* its values.** `Stop()` snaps everything back,
  so `DoNetLogo` writes the end state back as local values after each phase.

Timings live as constants at the top of `Controls/DoNetLogo.xaml.cs`. After changing
them, mirror them into `tools/preview_animation.py` to regenerate the GIF above.

`SplashPage` has a 6-second watchdog: whatever happens to the animation, the user
reaches onboarding.

## Fonts

| Family | Used for | File |
| --- | --- | --- |
| Pacifico | Logo wordmark | `Assets/Fonts/Pacifico-Regular.ttf` |
| Baloo 2 | Body copy, fields | `Assets/Fonts/Baloo2-Regular.ttf` |
| Baloo 2 SemiBold | Section headings | `Assets/Fonts/Baloo2-SemiBold.ttf` |
| Jersey 25 | Button captions | `Assets/Fonts/Jersey25-Regular.ttf` |

All three are SIL Open Font License 1.1; the `OFL.txt` files ship alongside them to
satisfy the attribution requirement. Baloo 2 is distributed by Google as a `wght`
variable font - the file here is a static 400 instance, because XAML's font loader only
picks a variable font's default named instance.

Both Baloo 2 (1.60 em) and Pacifico (1.76 em) declare unusually tall line boxes, so
text slots in the layout pin `LineHeight` explicitly. Remove those and the vertical
rhythm drifts.

## Window chrome

There is no system title bar. `OverlappedPresenter.SetBorderAndTitleBar(true, false)`
removes it outright, leaving a 48px strip in `MainWindow.xaml` as the only chrome: the
three coloured dots at the right — green minimise, amber maximise, red close, left to
right — and the DoNet lockup at the left, inset 14px to match them.

`Width="80"` there is measured, not chosen by eye. The lockup's tallest element is its
ring, which is 111 units against the control's declared canvas width of 447.99, so the
rendered height is `Width × 0.2478` — 19.82px at 80, leaving 14.09px clear above and
below and matching the 14px inset on the left. The logo carries the same margin on
every side it touches.

That coefficient is worth remembering: it is **not** the `103.4 / 447.99 = 0.2308` the
canvas implies. The ring overflows the declared canvas by 7.3% and `Viewbox` does not
clip, so `DoNetLogo` always draws about 7% taller than the box it reserves.

That lockup is shown on the home screen and nowhere else, toggled from the navigation
service's `Navigated` event. Splash, create-password, lock and forgot-password each
present a large centred logo already, and two at once reads as a duplication rather
than as branding. It is also `IsHitTestVisible="False"`, or the window would stop
being draggable wherever the logo happens to sit.

The strip *overlays* the frame instead of taking a grid row. Giving `MainWindow` a
second row would shorten every page by 48px and shift the centred screens, whose
vertical rhythm was measured against the full window. Home clears the strip with its
own top padding instead.

Extending content into the title bar is *not* enough on its own: that keeps the system
caption buttons, which cannot be hidden and would sit exactly where the dots go.
Dropping the title bar is the only way to be rid of them. The border is kept, so the
window still has its resize edges and drop shadow.

Two consequences, both handled in `MainWindow.xaml.cs`:

- **Dragging** is handed to the window manager with `WM_NCLBUTTONDOWN`/`HTCAPTION`
  rather than emulated by moving the window on pointer events. That keeps snapping,
  multi-monitor handoff and restore-on-drag behaving like a real title bar.
- **Double-click to maximise** is detected in code. The window manager only maximises on
  a genuine `WM_NCLBUTTONDBLCLK`, and synthesising a button-down per press means it
  never sees one — it sees two unrelated clicks.

Snap Layouts (hovering the maximise button on Windows 11) is the one thing lost; it is
tied to the system maximise button, which no longer exists.

### Minimum size

The window stops shrinking at **480 x 540**, which is derived rather than picked:

| | |
| --- | --- |
| Width | the 352px content column plus a 64px margin either side |
| Height | the tallest screen's block is 351px and sits 24px above centre, so clearing the 40px caption strip needs `(H - 351) / 2 - 24 >= 64` |

Below that the content starts colliding with the caption buttons. At the floor the
forgot-password screen — the tallest — still clears them by 30px.

`PreferredMinimumWidth`/`Height` take **physical pixels and are not DPI-scaled by the
presenter** ([#10475](https://github.com/microsoft/microsoft-ui-xaml/issues/10475)), so
`ApplyMinimumSize` multiplies by the monitor's DPI. Unscaled, 480 would really mean 320
on a 150% display. The presenter also keeps its pixel values across a DPI change
([#10452](https://github.com/microsoft/microsoft-ui-xaml/issues/10452)), which silently
loosens the limit when the window moves to a monitor with a different scale, so
`XamlRoot.Changed` recalculates it.

There is no maximum: maximising is left alone, only the floor is fixed.

## Layout

The design is a fixed 352 px column, centred, sitting 24 px above the optical centre of
the window. It does not stretch with the window; that is deliberate and matches the
Figma frame. Palette and type ramp are in `Styles/Colors.xaml` and `Styles/Fonts.xaml`;
`Styles/Theme.xaml` is the only dictionary `App.xaml` merges, and it pulls in the other
two itself so that `StaticResource` lookups always resolve.

## The vault

The master password is never stored, in any form, not even hashed. Instead:

1. A random 16-byte salt plus the password go through **Argon2id** (64 MiB, t=3, p=4) to
   derive a key-encryption key.
2. A random 32-byte **data key** is generated and wrapped with **AES-256-GCM** under that
   key-encryption key.
3. Only the salt, nonce, wrapped key, GCM tag and KDF parameters are written to
   `vault.json`.

Unlocking re-derives the key-encryption key and tries to unwrap. A wrong password
produces a wrong key, the GCM authentication tag fails, and the attempt is rejected —
so **the tag is the password check**. There is nothing on disk to compare a guess
against, and every guess costs a full Argon2id derivation.

The indirection through a data key is not decoration: it means the database can be
encrypted under a key that never changes, so changing the master password later only has
to re-wrap 32 bytes instead of re-encrypting everything.

KDF parameters are read back *from the file* rather than from the constants in
`VaultService`, so raising the cost later will not lock anyone out of an existing vault.
`vault.json` is written to a temporary file and renamed over the original, so a crash
cannot leave behind a half-written vault that nothing can open.

### Starting over

The forgot-password screen does this from inside the app: `IVaultService.ResetAsync`
deletes the vault and routes back to onboarding as a first run.

There is deliberately **no recovery path**. The master password is the only way to
unwrap the data key and it is never stored, so a forgotten password means the data is
already unreadable — by us as much as by anyone else. The screen says so plainly rather
than implying a reset is a way back in. There is no confirmation dialog either: reaching
that screen takes a deliberate click, and it states the consequence twice before
offering the button.

To do the same by hand, delete the vault:

```
%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState\vault.json
```

Unpackaged debug runs fall back to `%LOCALAPPDATA%\DoNet\vault.json`.

### Note on trimming

`VaultDescriptor` is serialised through a source-generated `JsonSerializerContext`, not
by reflection. Release builds set `PublishTrimmed`, which would strip the property
metadata reflection-based `System.Text.Json` depends on — the failure would appear only
in Release, not in Debug.

## The navigation rail

Every size and path on this screen comes from the design's own export, not from
measuring a picture of it. Three earlier attempts were measured off a 1546×967 PNG and
each one was wrong by a different factor — the artboard is 1440×900, so that export was
a 1.074× render, and eyeballed measurements carried another ±10% on top. The icons in
particular are the design's SVG `d` strings pasted in unchanged.

Each icon's SVG arrives as several `<path>` elements. Where they share a stroke colour
and width their `d` strings are simply concatenated: XAML strokes each `M` subpath
separately, so the result is identical and the control carries one `Geometry` per icon.
The lock is the only one that mixed a filled dot with stroked lines, and a round-capped
stroke already draws a disc of its own width at the end of a segment — so the dot and
the line below it collapse into `M14 17.5V21` and the fill disappears.

`tools/gen_nav_icons.py` holds those paths, flattens them (including the elliptical
arcs and cubics the other preview scripts never needed), checks each one fits its 28px
box with its stroke, and rasterises a sheet to compare against the design.

### Sizing

The first cut was built at disc 56 / rail 76 and read as too heavy next to the other
screens, so the shell is scaled by **6/7**. The factor was chosen to land on whole
numbers that mean something rather than on a round percentage:

| | first cut | now |
|---|---|---|
| disc | 56 | **48** — the same height as the title bar |
| icon | 28 | **24** — half the disc, exactly as before, and a standard size |
| rail | 76 | **64**, corner radius 32, still a true pill |
| ring | 32 | 28 |
| disc spacing | 19 | 16 |
| outer padding | 14 | 12, and 60 at the top to clear the title bar |
| rail→content gap | 24 | 20 |
| content radius | 40 | 34 |

The icon paths were *not* re-authored. They stay on their native 28 grid and the
`Viewbox` in `NavRailButton` takes them to 24, which scales the stroke widths with
them — 2.0 becomes 1.71 and 2.2 becomes 1.89 without a number being retyped.

The drop shadow is a design token, not a dimension, so `0 / 6 / 14 at 7.06%` is
unchanged. Its gradient stops are *not* unchanged: that fit is a function of the disc's
radius, so shrinking 28 to 24 required re-solving it.

### Shadows

A circle's drop shadow can be reproduced with a `RadialGradientBrush`, because the disc
hides the middle and only the Gaussian tail past the rim is ever on screen. That is what
the rail buttons do — no composition code, nothing to fail.

The trap is assuming the tail *starts* at full strength. It does not. A blurred edge
sits at roughly half the source's opacity, so this design's 7.06% shadow is already down
to **3.18% exactly where the disc ends**, and halves again every ~2.5px. The first
attempt ramped evenly from 7.06% at the rim and read as a hard grey collar rather than a
shadow — 2.2× too dark against the rail. The stops are a numeric fit to the disc
convolved with σ 7 (a Figma/CSS blur radius is two σ), accurate to 0.4 of one alpha
level across the only band that is visible. At the current 48px disc that is 3.12% at
r=24, fitted over r=24–40.

Two smaller traps live in the same element. A top margin does not offset a
centre-aligned child — it shrinks the layout slot and the centring then halves the
result, so `Margin="0,6,0,0"` dropped the shadow 3px instead of 6; use a
`TranslateTransform`, which is outside layout. And `RadialGradientBrush` is a
`XamlCompositionBrushBase`: where composition is unavailable it paints a flat
`FallbackColor` across the whole ellipse, so that is pinned to `Transparent` here.
Losing the shadow is invisible; gaining a solid grey disc behind every button is not.

`tools/preview_screens.py` draws these shadows as a true Gaussian. It previously drew
none at all, which is precisely why an over-strong one passed review in `docs/` and
only surfaced in a real build.

The two rounded panels cannot use that trick, so they take a real composition
`DropShadow` via `Controls/Elevation.cs`. Two things about it are easy to get wrong:
the shadow is shaped from `Shape.GetAlphaMask()`, which is why the panels are
`Rectangle`s rather than `Border`s; and the sprite is parented to an *empty* element
behind the panel, because a child visual draws above its host's content and would
otherwise lay the shadow over the panel's own fill.

`ThemeShadow` was tried first and rendered nothing at all.

The panels are flat — one fill, no stroke. With nothing but a 24px gap between
them, the shadow is the only thing giving the rail an edge, which is why it is a
real one rather than an approximation.

### States

The rail buttons drive their states with named `Storyboard`s rather than a
`VisualStateManager`. `GoToState` looks for its state groups on a control's *template*
root and a `UserControl` has no template, which is a well-known way for states to go
missing with no error at all. Pointer and selection are kept on disjoint properties —
scale and wash for one, accent opacity and icon colour for the other — so they cannot
overwrite each other.

## Not done yet

The home screen's content surface is deliberately empty — the three sections have no
content yet, so it is the bare surface from the design. It becomes their host when
those screens arrive.

`VaultService.ResetAsync` deletes `vault.json`. It gets one more line to delete the
encrypted database once that exists.

## Tools

Design-time scripts, not part of the app build. They need `fonttools` and `pillow`.

| Script | Purpose |
| --- | --- |
| `tools/gen_logo_control.py` | Regenerates `Controls/DoNetLogo.xaml` from the Pacifico outlines |
| `tools/gen_wordmark_control.py` | Regenerates `Controls/WelcomeWordmark.xaml` — same trick, no ring |
| `tools/extract_logo_geometry.py` | Prints the raw geometry and arc lengths; holds the ring proportions |
| `tools/preview_animation.py` | Renders `docs/splash-animation.gif` |
| `tools/preview_screens.py` | Renders a pixel reconstruction of the four screens for comparing against the design |
| `tools/gen_nav_icons.py` | Parses the design's icon SVG, checks it fits, emits the XAML path data |
| `tools/validate_xaml.py` | Checks resource keys, `x:Name`s and event handlers without a Windows build |
| `tools/make_semibold.py` | Regenerates the Baloo 2 SemiBold instance from the variable font |
