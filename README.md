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
own top padding instead, leaving 6px beneath it.

### Making the strip behave like a real title bar

Removing the system title bar also removes everything it did. The first attempt put it
back by hand — `ReleaseCapture()` then `SendMessage(WM_NCLBUTTONDOWN, HTCAPTION)` from
`PointerPressed`, with double-clicks timed against `GetDoubleClickTime()`. That cannot
work, for a reason worth recording: **`WM_NCLBUTTONDOWN` runs a modal move loop inside
the message**. The handler does not return until the drag finishes, XAML's input state
is stale afterwards, and the second click of a double-click is consumed by the move
loop rather than arriving as a second press — so maximise fired only when the timing
happened to slip past it.

The supported answer is to *declare* the region rather than emulate the behaviour:

```csharp
var source = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
source.SetRegionRects(NonClientRegionKind.Caption, new[] { captionRect });
source.SetRegionRects(NonClientRegionKind.Passthrough, new[] { captionButtonsRect });
```

`InputNonClientPointerSource` is the documented companion to
`SetBorderAndTitleBar(true, false)`: the presenter hides the system caption, and this
says where the caption actually is. Dragging, double-click maximise, the right-click
system menu, edge snapping and multi-monitor handoff all come back, implemented by the
window manager instead of by us. `Passthrough` wins over `Caption` where they overlap,
which keeps the three dots clickable with their hover states intact.

Two details that are easy to miss. The rects are **physical pixels** relative to the
client area, so they are scaled by `RasterizationScale` and re-declared on load, on
resize and on DPI change. And the caption is **inset by the resize border**
(`SM_CXSIZEFRAME + SM_CXPADDEDBORDER` at the window's DPI): a caption region claims
every pixel it covers, so running it to the window edge would swallow the top-left
grab handles and the window could no longer be resized from the top.

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

The two panels have none. The rail and the content surface are flat `#F5F5F5` on a white
page and are separated from it by colour alone — no stroke, no shadow. `Elevation.cs`,
which gave them a composition `DropShadow`, is gone with them; it is in the history if
it is ever wanted. Dropping it also takes a per-frame composition resize off the window
during a live drag-resize, and the mask it built was only ever stretched rather than
rebuilt, so its rounded corners were quietly distorting as the window grew.

The rail *buttons* keep their shadow. A circle's drop shadow can be reproduced with a
`RadialGradientBrush`, because the disc hides the middle and only the Gaussian tail past
the rim is ever on screen — no composition code, nothing to fail.

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
them, the disc shadow is the only thing giving a button an edge, which is why it is a
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
| `tools/validate_xaml.py` | Checks resource keys, `x:Name`s, event handlers and Grid row/column indices without a Windows build |
| `tools/validate_handlers.py` | Resolves every XAML event attribute and `+=`/`-=` subscription against the methods that exist |
| `tools/validate_control_usage.py` | Resolves every custom-control property set in XAML against the properties the controls declare |
| `tools/validate_csharp_docs.py` | Checks every documentation comment block is well formed |
| `tools/validate_visual_states.py` | Checks every visual-state `Setter Target` and storyboard target resolves |
| `tools/validate_usings.py` | Checks every type a C# file names can be seen from it — framework types against a curated namespace map, app types against their declaring namespaces |
| `tools/build_icons.py` | Normalises the icon paths and regenerates `Controls/LineIconData.cs` plus the proof sheet |
| `tools/make_semibold.py` | Regenerates the Baloo 2 SemiBold instance from the variable font |

## The Persons directory

The first section with content. It lives inside the home screen's content plate rather
than being its own page: the rail stays put, and `HomePage` shows `PersonsView` when
`IsPersonsSelected`. Two surfaces are in play and they are easy to confuse — the grey
`#F5F5F5` plate belongs to `HomePage` and the header sits directly on it, while the
white rounded panel holding the cards belongs to this screen.

The screen opens on Persons rather than Services, because Services and Sites have no
design yet and opening on a blank surface reads as a failure to load.

### What is deliberately not here

The copy button on every field cell is drawn in the supplied design and is **not**
implemented — it was cut after the design was produced. ADD PERSON and double-clicking
a card are both rendered and wired to commands that do nothing, because the forms they
would open have not been designed. The gesture, the command and its parameter are all
in place, so adding those screens later is a navigation call in the view model and no
change to the view.

A person has eighteen columns. A card shows nine of them; the rest belong to the full
record form. `PersonPreview` models only those nine on purpose — inventing a shape for
a screen that does not exist yet would guess at decisions that are not ours to make.

### States

Four of them, and they are derived from one `DirectoryState` field rather than from a
handful of independent booleans. Independent booleans drift: two end up true and the
screen shows a spinner on top of an error. The view binds to flags computed from the
single field, so only one branch can ever be visible.

| state | when |
|---|---|
| Loading | a fetch is in flight |
| Ready | records exist and at least one survives the search |
| Empty | no records at all |
| Error | the fetch threw |

"No matches for this search" is kept separate from "no records at all". Collapsing them
would have the empty state claim the directory is empty when it is really the filter
that is too narrow.

The error state never shows the exception text. A provider stack trace tells a user
nothing they can act on, so the message is fixed and the detail stays in the log.

### The seam the database will land in

`IPersonDirectory` is the only thing the view model knows about. `PersonDirectoryService`
currently returns the four blank cards the design specifies — the screen is explicitly
labelled "Static placeholder values only", so that is the designed content, not fake
data standing in for something richer. Replacing it with EF Core over SQLCipher is one
registration change in `App.xaml.cs` and no view rework.

That stub carries a `Mode` property, which exists only because the empty and error
states would otherwise be unreachable: a service that always succeeds gives you no way
to look at them. The real implementation reaches them on its own, and the property
disappears with the class.

### On encrypting column names

SQLCipher encrypts the entire database file, schema included. Without the key, table and
column names are indistinguishable from random bytes on disk — so readable names in the
schema are already unreadable at rest, and naming columns `c1`, `c2` would only add
protection against someone who already holds the key. Readable names it is.

### Sizing

`UniformGridLayout` with `MinItemWidth="430"` is what makes the grid responsive: two
columns while both fit, one below that, with no size-watching code. Below a window
width of 820 the search row drops under the title and stretches, and the paddings
tighten from 32/24 to 12/12 — at 480 the three field columns would otherwise have about
81px each, which is narrower than "DATE OF BIRTH" renders. Card padding stays fixed,
because a `VisualState` on the page cannot reach inside a `DataTemplate`.

### Why the store keeps one connection open

SQLCipher derives the file key with 256,000 rounds of PBKDF2-HMAC-SHA512, by design, on
every `PRAGMA key`. That is a deliberate brute-force cost and it is not tunable without
weakening the encryption. The consequence for the app is the thing to remember:

> **Never open a keyed connection per operation, and never open one on the UI thread.**

`PersonDirectoryService` opens exactly one connection per unlocked session, on a thread
pool thread, and builds a short-lived `DoNetDbContext` over it for each piece of work.
Contexts stay per-operation so the change tracker never goes stale; the connection -
and therefore the key derivation - does not. `Lock()` disposes it, so the next unlock
re-keys and a locked app holds no usable handle.

The search box debounces 250ms and cancels the query in flight, so a typed word is one
round trip rather than one per character, and a page plus its total is fetched in a
single trip instead of two.

## The Websites directory

Seven columns - `#`, Name, Domain, Description, Available Payment Methods, Note,
Created At - over the same encrypted store, with the same card, search, paging and
three-mode dialog as Persons. Divergence between the two screens would be a bug, so
the view models are deliberately the same shape.

### Payment methods are two things

Six ship with the app and are always offered, each with its brand mark drawn as
vectors. Anything else the user types is saved to `PaymentMethodOptions` and merged on
top, so a method added once is a tick box from then on. Deriving the list from existing
records instead was the alternative, and it loses a method the moment the last record
using it is edited. Matching is case-insensitive throughout, so a hand-typed "paypal"
does not become a seventh option beside PayPal.

A record's own selection is one delimited column rather than a join table: the
directory reads whole pages and never queries by method, so a relationship would add an
`Include` to every read to serve a query nobody makes. The separator is ASCII Unit
Separator, because methods are free text and "Visa, Mastercard" typed as one entry must
not silently split in two.

### Two controls worth knowing about

`PaymentMethodIcon` holds all six marks plus a neutral card outline for user-added
methods, each `x:Load="False"` and realised by name - these appear in quantity, so
building all seven in every instance would be most of a thousand elements for a handful
of small logos.

`WrapPanel` exists because WinUI ships no wrapping panel and `UniformGridLayout` would
pad "USDT" out to the width of the longest chip. Fifty lines of `MeasureOverride` was
cheaper than the workarounds.

### One confirmation dialog, two directories

`ConfirmDialog` takes every `IDeleteConfirmHost`, shows for whichever is asking and
sends the answer back to that one. The alternative was a second copy with "person"
swapped for "website", which is how two dialogs that are meant to look identical start
to drift.

## The icon set

Every mark in the app comes from one sheet. `tools/icons-source.json` holds each
icon's raw path exactly as it was drawn; `tools/build_icons.py` normalises them and
writes `DoNet/Controls/LineIconData.cs`. Nothing else reads the JSON, and the
generated file is never edited by hand.

Normalising means moving each icon to its own origin and scaling it into a 24 by 24
box, sized to leave the one-unit margin the source sheet's own 12 by 12 clip
rectangles use. Two things fall out of that. The first is that one geometry serves
every size: `LineIcon` puts it in a Viewbox, so a 13 px mark beside a field label and
a 19 px mark inside a title disc are the same path scaled, and a 2 unit stroke lands
on 1.08 px and 1.83 px respectively. The second is that the sheet's separate "large"
drawings turned out to be redundant - once normalised, the large globe, info and
shield were identical to their small versions to within 0.006 of a unit, and the user
and users marks to within 1 unit in 24. They are not carried twice.

A field's icon is chosen in one place. `FieldLabel` pairs the caption with its mark,
and both the record card and the dialog use it, so a field cannot end up with a
calendar in one view and something else in the other.

`LineIcon` parses its path per instance rather than sharing a `Geometry` from a
resource dictionary. A `Geometry` cannot be attached to two `Path` elements at once,
so a shared one works until the second card appears and then throws.

## Payment methods on a card

A record with six payment methods used to make its card taller than its neighbours.
The chip row is now capped at one line: `WrapPanel.MaxLines` lays out what fits and
marks the rest, and the preview dialog still shows all of them.

The count is measured rather than fixed, so a wide window shows more chips and a
narrow one fewer, and the card's height never changes either way. Two details make
that stable. Children that do not fit are arranged at zero size rather than collapsed
- WinUI clips a child arranged smaller than it asked for, and unlike setting
Visibility it changes no property, so it cannot dirty layout and restart the pass.
And the "+N" marker carries a MinWidth, because the panel has to reserve room for it
before it knows the number it will show; without a floor, "+1" and "+12" would
reserve different widths and the second measuring pass could disagree with the first.

## Schema changes on an existing database

`EnsureCreated` builds the whole schema when the database file is new and does
nothing at all when it already exists. It has no opinion about a file that exists but
was written by an older build, which is why the first run after the Websites
directory shipped opened a vault with no `Websites` table, failed every query against
it, and showed the directory's error state. Erasing the database fixed it, which is
not a repair anyone should have to discover.

`SchemaGuard` closes that gap without taking on migrations. It reads `sqlite_master`,
compares it against the model, and returns immediately when nothing is missing - the
normal path costs one cheap query. When something is missing it takes the DDL from
`GenerateCreateScript`, so the model stays the single source of truth, makes each
statement idempotent and replays it. It cannot rename a column or change a type; when
the schema needs that, this is the thing to replace with real migrations.

## Stability and performance

### Nothing fails silently

Three handlers are installed in the `App` constructor, before anything else runs,
because they cover different ground in WinUI 3:

| Handler | Catches | Can it save the app? |
| --- | --- | --- |
| `Application.UnhandledException` | UI thread | Yes |
| `AppDomain.UnhandledException` | background threads | No - already terminating |
| `TaskScheduler.UnobservedTaskException` | faulted tasks nobody awaited | N/A - fires at GC |

Background-thread failures do **not** reach the `Application` event in WinUI 3, which
is why the `AppDomain` one is not redundant. And the exception the `Application` event
hands over usually arrives with its stack trace stripped, so the most recent
first-chance exception is kept and substituted when the stack is missing.

UI-thread failures are marked handled and the app carries on - but only up to five in
ten seconds. Blanket swallowing sounds safer than it is: an exception thrown from
layout repeats every frame, and a handler that always recovers turns that into an
unkillable app burning a core. Past the limit it is allowed to end.

Everything lands in `%LOCALAPPDATA%\...\logs\donet.log`, rolling at 256 KB with one
previous file kept. The logger never throws and switches itself off after a write
failure - a logger that fails while reporting a failure turns a diagnosable fault into
an undiagnosable one. Nothing sensitive is written: exception text and timings only,
never a field value, a password or a key.

### async void

Seven event handlers were `async void`. An exception in one of those has no caller to
catch it and kills the process. All seven are wrapped. Fire-and-forget tasks go through
`.Observe(context)`, which attaches a failure-only continuation so the exception is seen
and logged at the moment it happens rather than whenever the task is collected.

### What was measured, and what is logged

The app logs the one genuinely expensive thing it does - keying the database - on every
open, and any database operation that crosses 150ms. Only the outliers: putting a file
append on the path of every keystroke is the kind of instrumentation that becomes the
performance problem. If something feels slow, the log says whether it was the key
derivation or something else.

### Deliberate decisions

- **One connection per unlocked session.** See above; this is the single biggest win.
- **No index on `Id`.** An `INTEGER PRIMARY KEY` in SQLite *is* the rowid, so ordering
  by it is already free. The index that was there built a second B-tree over the same
  keys and charged every insert to maintain it. The only other access pattern is
  `LIKE '%term%'`, which no index can serve.
- **The native SQLCipher library loads lazily**, on the thread pool, instead of in the
  `App` constructor - it was a native DLL load sitting between process start and the
  first pixel.
- **`x:Bind` bindings are released on navigating away from the home screen.** Compiled
  bindings subscribe straight to `PropertyChanged` and never unsubscribe; with a
  singleton view model that means every lock/unlock cycle stranded a whole page tree in
  memory. Safe here only because the home screen is never navigated back to.
- **Closing waits for in-flight work.** Disposing a `SqliteConnection` mid-statement is
  a native race, and an access violation as the window closes is still a crash. If the
  wait times out the connection is left for the OS and WAL recovers on next open.
- **Workstation concurrent GC, `TieredPGO`, ReadyToRun on Release.**

### Considered and not done

`PersonDialog` is built eagerly with the home screen - sixteen `FieldCell`s and their
inputs - even though it is invisible until used. `x:Load` would defer that, but the
control also owns the subscription that tells it when to open, so deferring it means
moving that trigger out to the host. That is a structural change with real regression
risk, and this round was about stability. It wants a measurement first: the home intro
animation is ~0.54s and may already cover the cost.

### The biggest single cost: 1,700 elements nobody could see

Microsoft's XAML guidance budgets roughly **a millisecond per element created** at
startup, and is explicit that `Visibility="Collapsed"` does not prevent creation - a
collapsed element is built in full and merely skipped when drawing.

The two modals on the home screen were collapsed, not deferred:

| | elements |
| --- | --- |
| `PersonDialog` markup | 136 |
| 18 x `FieldCell` (TextBox, PasswordBox, ComboBox, CalendarDatePicker, 3 buttons each) | 1,566 |
| **built on every home screen load, to show nothing** | **~1,700** |

Both now use `x:Load="False"`. But deferring alone just moves the bill to the first
click, so `HomePage` realises them on a `DispatcherQueuePriority.Low` callback once the
entrance animation has finished - the home screen gets to be interactive first, the
modals are built while nothing else wants the thread, and the first person you open is
already paid for. If something asks to open a modal before that idle pass runs, the
page realises it immediately instead; the warm-up is an optimisation, not a dependency.

Deferred controls need one extra thing: a modal can be created *because* it is already
supposed to be showing, so the property change that opens it fired before the instance
existed. Both now reconcile with the view model on `Loaded`.

### Card actions on hover

Edit and delete used to live in a drawer that grew out of a three-dot button. They are
now two 46px circular buttons in the bottom-right corner of the card, fading in from
the right while the pointer is over the card and fading out when it leaves.

The pair shares the content grid's cell with `Grid.RowSpan` rather than taking a column
of its own. A reserved column would indent every record permanently for the sake of
something on screen a fraction of the time. The corner is the right home for them: it
is the one region of a record card that is predictably quiet, and anchoring there means
the buttons land in the same place on every card regardless of how tall its fields run.

Only opacity and a translate are animated, and both are independent animations, so they
run on the compositor thread and stay smooth while the directory scrolls behind them.
That is the real gain over the drawer, which had to animate `Height` with
`EnableDependentAnimation` and therefore ran on the UI thread. Out is faster than in -
0.14s against 0.18s - because arriving should feel unhurried and leaving should be done
before the pointer reaches the next card.

Two details that are easy to miss. `PointerExited` **bubbles**, so moving off one of the
buttons and back onto the card raises it on the card too; without a bounds check the
actions blink out every time the pointer crosses one of them, which is every time
somebody reaches for them. And a hover-only affordance is a mouse-only affordance, so
the buttons also appear when either takes keyboard focus - with the decision queued on
the dispatcher, because tabbing between them raises `LostFocus` before the other's
`GotFocus` and deciding immediately would hide the buttons out from under the caret.

### Accounts, and the first real relationship

Persons and Websites stand alone. An account cannot: it is a login *on a site*, so
`Account.WebsiteId` is a required foreign key rather than a nullable one, and the form
offers a list of saved websites instead of a text box. A typed name that nearly matches
is not a near miss - it is a record the database will refuse.

Three things fell out of that which the standalone tables never needed.

**The page read uses `Include`.** The card names the site, and without the navigation
loaded every card would go back to the database for one string: the classic page of
twenty-four extra queries.

**The picker works in labels, the store works in ids.** A combo box shows text, so the
dialog holds the label and the id side by side and resolves one to the other on save.
The label is name *and* domain together, because either alone can repeat and a picker
offering the same entry twice is unusable. The card shows that same label, so the value
you chose is the value you see afterwards.

**Deleting a website cascades to its accounts.** The alternative - refusing the delete -
turns a confirmed destructive action into an error the user cannot act on without
hunting down records the Websites screen never mentions.

Two traps worth naming. `AddAsync` nulls the cloned `Website` before inserting, or EF
treats the attached object as a second unsaved site and writes a duplicate alongside the
account. And `FieldCell` only ever assigned a combo box's `ItemsSource` once, which is
right for the static gender and country catalogs and wrong for a list that is data: a
website added after the cell first rendered would never appear. It now reassigns when
the list itself changes.

The password is not previewed on the card. A card sits in a grid that is visible across
a room, and a secret on screen whenever the directory is has stopped being a secret.

### A visual state that pointed at nothing

`WebsiteDialog.xaml` was cloned from `PersonDialog.xaml` and kept five `Setter Target`s
naming elements that only ever existed in the original - `ColMiddle`, `MiddleColumn`,
`RightColumn`, `CreatedCell`, `NoteCell`. Nine setters in total, all unresolvable.

Nothing complained, because a `VisualState` is inert until it activates. This one is
driven by an `AdaptiveTrigger`, so it was waiting for somebody to drag the window under
1000px with the website dialog open, and would then have thrown. The narrow layout now
names the cells it moves and has the seven rows a single column needs.

`tools/validate_visual_states.py` checks every `Setter Target` and
`Storyboard.TargetName` against the names declared in the same file. 156 targets, all
resolving. It would have caught this the day it was written.

### Moving between tabs

Switching rail destinations used to be a cut. The three grids sat in one cell with their
`Visibility` bound to the view model, so a click swapped one for another between frames.
Nothing was wrong with it; it just did not look like the rest of the app, where every
other state change is animated.

The sections now sit in their own layer above the content plate. The plate does not move
or fade - it belongs to the shell, and a surface that blinks on every tab change reads as
a failed redraw rather than as navigation. Only the layer above it animates: 0.12s out,
then the swap, then 0.22s in. Opacity is linear and movement is eased, which is what the
rail and the entrance animation already do.

The motion is directional. Going down the rail sends the old section up and brings the
new one in from below; going back up reverses it. Fourteen pixels, which is enough to be
felt and not enough to be watched.

Three things had to be true for it to be safe:

**The bindings had to go.** A binding flips the instant the rail is clicked, so the old
section would already be gone before there was anything to animate. `HomePage` owns
section visibility now and performs the swap at the midpoint, while the layer is
invisible.

**The incoming grid is built before the animation starts, not at the midpoint.** Each one
is a few hundred elements, and element construction is UI-thread work. Dropped in the
middle of a transition it would be a visible hitch; done up front it happens while
nothing is moving.

**And it is built transparent rather than collapsed.** This is the part that is not
obvious. A collapsed element is never measured, and an `ItemsRepeater` that has never
been measured throws on any change to its collection - the same fault this app has now
hit three times. Leaving the incoming grid collapsed for the 0.12s of the fade-out would
have opened a crash window for anything that finished loading inside it. `Visibility`
`Visible` at `Opacity` 0 is laid out like anything else while showing exactly as much as
collapsed does.

A transition in flight is never interrupted. A second click during one is recorded by the
view model and chased when the current one finishes, which costs at most a third of a
second. The alternative is restarting an animation from a value the UI thread cannot
reliably read mid-flight, because these run on the compositor - and that is how a clean
fade turns into a flash.

### A handler that was never there

The `Options` property added to `FieldCell` for the account dialog's website picker
registered its change callback as `OnAnyChanged`. The method is called
`OnAnyPropertyChanged`, which the other nine properties on that control all name
correctly. One property, one wrong name, and the first real build said:

> The name 'OnAnyChanged' does not exist in the current context

That is the cheapest kind of bug to fix and the most annoying kind to find, because a
method group passed by name has nothing near it to check it against. The same shape
appears in two other places: a XAML event attribute (`Click="OnSave"`) and an event
subscription (`thing.Changed += OnChanged`). All three are a bare identifier that the
compiler resolves and nothing else does.

`tools/validate_handlers.py` resolves all three against the methods the type actually
has, following base classes so an inherited handler still counts. 161 references, all
resolving.

### Arriving with the data already there

The home screen used to appear empty for a beat after every unlock, then fill in. The
cause was two things that each looked right on their own.

`PreloadAsync` was called from the welcome screen and documented as leaving "the first
page ready" - but all it actually did was `_store.WarmUpAsync()`. It opened and keyed
the database and stopped there. Then `PersonsView.OnLoaded` called `LoadAsync()`
**unconditionally**, which drops to the loading state and re-queries. So the warm-up
saved the expensive part and the view threw away the cheap part, visibly.

Both halves are fixed. The preload now fetches the page as well as opening the store,
and the view calls `EnsureLoadedAsync()`, which returns the in-flight task instead of
starting a second load:

```csharp
public Task PreloadAsync()      => _firstLoad ??= PreloadCoreAsync();
public Task EnsureLoadedAsync() => _firstLoad ??= LoadAsync();
public Task LoadAsync()         => _firstLoad  = LoadCoreAsync(showLoading: true);
```

A view that opens mid-preload waits for it rather than racing it; one that opens after
it finishes finds `State == Ready` and the records already in the collection, and
renders them on its first frame. `Reset` clears the latch, so the next unlock fetches
again rather than joining a stale task.

`PreloadCoreAsync` swallows a warm-up failure on purpose. It is an optimisation, and
`LoadCoreAsync` opens the store itself if it has to - letting it escape would leave the
task the view awaits in a faulted state with nothing having moved the directory off
`Loading`, which is the same stuck spinner as before wearing a different hat.

The Websites directory gets the same treatment from the home screen's idle window, so
the rail's first trip to Sites also arrives populated.

### Cancelling without disposing

`LoadCoreAsync` used to do this on every call:

```csharp
_loadCts?.Cancel();
_loadCts?.Dispose();          // <- the superseded query still holds this token
_loadCts = new CancellationTokenSource();
```

Cancelling a superseded load is right. Disposing its source is not, because the load
being cancelled is still inside an `await` that holds its token, and
`CancellationToken.Register` - which the SQLite provider calls to wire up statement
interrupt - throws `ObjectDisposedException` once the source behind the token is gone.
The result is an intermittent failure on exactly the paths that cancel most: typing in
the search box, and scrolling while a page is in flight.

The `Dispose` calls are gone. These sources never touch `WaitHandle` and hold no
unmanaged resource, so letting the collector take them costs nothing, and it removes
the race outright rather than trying to time it.

### Clearing a list that an ItemsRepeater cannot see

Two separate symptoms, one cause. Locking the app threw
`COMException: Unspecified error` out of `People.Clear()`, and the Websites directory
hung on "Opening the directory" forever.

The card host was written the obvious way:

```xml
<ScrollViewer x:Name="CardScroller" Visibility="{x:Bind ViewModel.HasCards}">
    <ItemsRepeater ItemsSource="{x:Bind ViewModel.People}" ... />
```

`HasCards` was `State == Ready && Count > 0`, so any time the list was empty the
repeater sat inside a **collapsed** subtree. A collapsed `ScrollViewer` is never
measured, so the repeater's viewport manager has no viewport to work against, and a
collection change arriving in that state comes back as `E_FAIL`.

What made it hard to see is that the crash happened on an **empty** collection - the
debugger showed `Count = 0` at the throw, which looks impossible for a method whose job
is to remove things. `ObservableCollection<T>.Clear()` raises a Reset unconditionally,
whether or not it removed anything, so clearing an already-empty list still sent the
one notification the repeater could not service.

The hang was the same fault wearing a disguise. `LoadCoreAsync` set `State = Loading`
- collapsing the host - then cleared the collection, which threw. Its handler caught
the exception and, as its first act, **cleared the same collection again**. That threw
a second time and escaped the method, so the state was never moved off `Loading`. An
error had turned into a spinner that ran until the app was killed.

Three changes, each sufficient on its own:

- **The scroller is never collapsed.** With nothing in the collection it draws nothing
  anyway, so there was never anything to gain by hiding it. The four state panels are
  now opaque and stack above it.
- **`ClearSafely()` returns early when the collection is already empty**, so the
  pointless Reset is never raised.
- **A handler never touches the thing that just threw before reaching safety.** The
  terminal state is set first, and the clear comes last.

The fourth change is the one that matters most next time: **all eight `catch (Exception)`
blocks in the two directory view-models were silent**. No log line, no breadcrumb. A
swallowed exception with no record of it does not make the app more robust, it makes
the next bug undiagnosable - and this one cost a full round of guessing that a single
`AppLog.Error` would have answered immediately.

### The same fault twice more: detached views that keep listening

The fix above stopped the crash but not the error state, because the collapsed-repeater
trap had two more instances - and a second, independent bug was feeding it.

**A singleton view model outliving the view bound to it.** `PersonsViewModel` and
`WebsitesViewModel` are singletons so a directory keeps its place across a lock, but
`HomePage` is rebuilt on every unlock. `WebsiteDialog` already guarded against this for
its `PropertyChanged` handlers, with a comment explaining it exactly - and still leaked,
three ways the guard did not cover:

- `ViewModel.Selected.CollectionChanged += (_, _) => RebuildChips();` - subscribed in
  the constructor, never unsubscribed, and an **anonymous lambda cannot be**. It is a
  named method now.
- All three `ItemsRepeater`s. `Bindings.StopTracking()` does not help here:
  an ItemsRepeater subscribes to its source through its own `ItemsSourceView`, which
  x:Bind knows nothing about. The subscription is a strong reference *from* the
  singleton *to* the dead page, so it is never collected and keeps being notified.

Every lock/unlock cycle therefore added another detached directory listening to the
live collection. The next `Clear()` or `Add()` was delivered to all of them, and the
ones with no visual tree threw. Two orphans, two `COMException`s.

**The dialog's payment options were collapsed twice over.** `MethodOptions` is hidden
while the dialog is closed *and* hidden again in preview mode - but `ShowAdd`,
`ShowEdit` and `ShowPreview` all rebuild `Choices` **before** `IsDialogOpen` turns true.
Reordering that does not fix it, because setting `Visibility` does not measure anything
synchronously; the repeater is still viewport-less on the next line. So the source is
attached from code only while the repeater is genuinely on screen, and dropped the rest
of the time - the rebuilds then reach nobody.

The general rule, which is what to remember: **a collection that outlives the view bound
to it must be detached explicitly, and `StopTracking` is not that.** Anything holding an
`ItemsSource` needs it nulled, and any handler meant to be removable must be a named
method.

### A layout clip that ate the old drawer

Worth keeping even though the drawer is gone, because the rule is general.

Pinning the drawer's host to 36x36 with a `Grid` broke it, because **WinUI applies a
layout clip whenever a child is arranged smaller than it asked to be**. A Grid arranges
children inside its own box, so a surface animating to 106px was cut back to 36 and the
buttons below the dots disappeared. What survived was the opacity fade and the dot
rotation, so the menu looked like a small empty outline.

A `Canvas` arranges each child at its own desired size, so no clip is ever applied.
The rule: if an element must overflow its parent, the parent has to be a Canvas. Fixing
a size on any other panel is asking for a clip.

### Tab order, and why TabIndex could not do it

`TabIndex` is only compared **within a container**: `FrameworkElement.TabFocusNavigation`
defaults to `Local`, so indices are ranked against siblings, not globally. The dialog's
three columns are separate `StackPanel`s, which meant the tab indices on the cells only
ever reordered each column internally. Tabbing walked the middle panel top to bottom -
First Name, Gender, Country - and only then moved to the next panel.

The order the form actually needs is the schema order, and it zigzags, because the
design puts Last Name under the record number on the left while First Name heads the
middle column:

| # | field | column |
| --- | --- | --- |
| 1 | First Name | middle |
| 2 | Last Name | left |
| 3 | Gender | middle |
| 4 | Date of Birth | left |
| 5 | Country | middle |
| 6 | State | left |
| 7 | City | middle |
| 8 | Street | left |
| 9 | Postal Code | middle |
| 10 | Phone Number | left |
| 11 | Email | right |
| 12 | Email Password | right |
| 13 | Recovery Email | right |
| 14 | Recovery Password | right |
| 15 | Recovery Words | right |
| 16 | Note | full width |

`#` and `Created At` are not in the list: both are assigned on save and have no input
to focus.

No arrangement of `TabIndex` can interleave two containers, so `PersonDialog` drives
Tab itself from `PreviewKeyDown`, walking an explicit ordered list. It deliberately does
not trap focus: tabbing past the last field, or back past the first, is left unhandled
so focus carries on to the buttons and out of the dialog. A form you cannot tab out of
is worse than one that tabs oddly.

The cells still carry a matching `TabOrder`. It cannot cross columns, but it keeps each
column internally correct, so the built-in behaviour stays sensible if the handler ever
does not run.

### A failed open still holds the file

`SqliteConnection.Open()` leaves a handle on the file when it throws. SQLite opens it,
reads the header, rejects it, and the managed wrapper keeps the handle until something
disposes it.

That is not a tidy-up detail here. The connection that failed was never assigned to the
field `CloseAsync` disposes, so it was invisible - and a locked `donet.db` is a file the
password reset cannot delete. The one action that recovers from an unreadable database
was being blocked by the failed attempt to read it. Opening is now wrapped so a failure
disposes before it rethrows.

Deleting during a reset also retries a few times. The gap between a handle being
released and Windows agreeing it has been is real - a just-disposed connection, an
indexer, a virus scanner - and failing a destructive one-shot operation on a
hundred-millisecond race leaves a half-erased vault.

### Spending the time the user is already spending

Four moments where the app is free and nobody is waiting on it. `Services/IdleWork.cs`
schedules into them - `OnUiIdle` at the lowest dispatcher priority for work that must
touch the visual tree, `InBackground` at below-normal thread priority for work that
must not.

| Window | What happens in it |
| --- | --- |
| Splash animation (~2.7s) | Load the SQLCipher native library; build EF's object model |
| Lock screen (typing) | Load and JIT the Argon2id and AES-GCM paths |
| Welcome animation (~2.6s) | Open and key the database |
| After home settles | Build the deferred modals; prefetch the second page of records |

EF builds its model lazily on first use by reflecting over the entity types - and does
**not** need a database to do it, because the model describes entities, not tables. So
it can be built on the splash screen, before the user has even typed a password, rather
than landing on the first query alongside the key derivation.

The lock screen warm-up runs Argon2id with deliberately tiny parameters. The expense of
Argon2 is its memory-hardness and none of that is wanted here; the point is only to
touch the same methods, so the assembly is loaded and the inner loop compiled before
the real derivation needs them.

Multicore JIT is on (`ProfileOptimization` in the `App` constructor). The runtime
records which methods it compiled during startup and, on every launch after,
recompiles them across spare cores while the main thread gets on with starting.
Desktop apps have to opt in; ASP.NET gets it by default.

Everything here is an optimisation and never a dependency: each caller is correct if
the work never runs. A warm-up that becomes required is just initialisation with extra
steps and a race condition.

### The four directory states

`Controls/StateArt.xaml` draws the loading, empty, no-match and error illustrations.
They share one motif - a 92x60 record card on a 120x92 canvas - so they read as the
same object in four conditions rather than four unrelated icons: contents not yet
arrived, an empty slot, a lens finding nothing, a card that stayed locked.

Two constraints shaped the implementation more than the drawing did.

**Every animation moves opacity or a transform, nothing else.** Those are
*independent* animations, which the compositor runs on its own thread. Width, height
and `StrokeDashOffset` would be *dependent* animations, tied to the UI thread. That
distinction is not academic here: the loading illustration plays at the exact moment
the app is busiest, and an indicator that stutters while you wait for it is worse than
no indicator at all.

**`IsActive` is a separate property from visibility.** A collapsed element still runs
its storyboards, so binding only `Visibility` would leave three unseen animations
looping forever, waking the compositor sixty times a second to draw nothing. The host
binds the same view-model flag to both, and the control starts exactly one storyboard.

Each illustration is `x:Load="False"` and realised through `FindName` when `Kind` is
applied, so an instance builds only the picture it was asked for - four of these sit on
the Persons screen at once, and building all four drawings in all four instances would
be sixteen pictures to show one.

The error state shakes once and stops. A looping error animation keeps shouting at
someone who has already understood.

### The compiler as reviewer

`.editorconfig` at the repo root raises a curated set of analyzer rules to warning.
Deliberately curated rather than `AnalysisMode=All`: a wall of style opinions buries
the findings that matter. Every rule enabled is one where a hit means a defect -
resource lifetime, dropped `CancellationToken`s, culture-sensitive string comparison,
weak crypto, SQL built from strings. Nothing fails the build; a gate that blocks
shipping is a gate that gets switched off.

It immediately paid for itself. `WelcomeWordmark` matched element names with
`StartsWith("Trace")` - no `StringComparison`, so culture-sensitive, and the Turkish
dotless i makes that a real bug rather than a theoretical one. Also a missing
`GC.SuppressFinalize` in `PersonDirectoryService.Dispose`.

Two rules the codebase knowingly violates are left off rather than suppressed, with
reasons recorded in the file: `CA1031` (the crash handlers catch `Exception` on
purpose) and `CA1303` (the app is English-only).

### Checked and already correct

Worth recording so it is not re-investigated:

- **Key material is zeroed.** `VaultService` runs `CryptographicOperations.ZeroMemory`
  over the derived key, the data key and the password bytes in `finally` blocks. The
  remaining exposure is the base64 data key held as a `string` while unlocked, which
  cannot be wiped - a disclosed trade-off, not an oversight.
- **`Catalogs` allocates once.** The gender and country lists are static
  initialisers, not properties rebuilding an array per call.
- **No `JsonSerializerOptions` churn** in the vault's read/write path.

### The picker, and why it is not a ComboBox

Three fields drop a list open: gender, country, and the website an account belongs to.
All three used a `ComboBox`, which is the one control in the app that never looked like
the app. Its popup is grey, square-ish and lands wherever the system decides; the rows
highlight in the system accent, not the teal everything else uses; and the country list
is 196 entries of unbroken text with no way to narrow it.

It is now one control, `ChoicePicker`, that all three fields share.

The closed state is just the value and a chevron, drawn into the field cell it already
lived in, so nothing about the dialogs moved. The chevron turns over in 0.18s when the
drawer opens and back when it closes - the same cubic ease as the rail and the section
transition, because a control that animates on its own curve reads as a control from
somewhere else.

The drawer is a `Flyout` with the app's own geometry: 20px corners, the field's own
white, no padding of its own. Rows are 42px tall with 13px corners and inset 8px from
the edge, so the highlight is a pill inside the surface rather than a bar across it.
The rows carry a template this app owns rather than the stock one: a single rounded
`Border` whose pointer-over, pressed and selected states paint the app's teal directly -
hover `#F0F7F5`, selected `#E4F0EC`, and the pressed variants. No stock state brush
remains to paint the row blue, and no selection indicator bar exists to switch off; the
tick on the right says selected more quietly.

The row corners are why the stock template had to go, and the story is worth writing
down because two attempts failed first. Setting the `ListViewItem`'s `CornerRadius`
property rounded nothing: the stock template draws its states inside a
`ListViewItemPresenter` that reads the `ListViewItemCornerRadius` theme resource and
never the property. Overriding that theme resource in the drawer's scope is the
textbook fix, it is what the reference template's presenter asks for, and on the
machine that matters it rounded nothing either - whichever list-item variant that
Windows App SDK build actually ships was not listening to either lever. So the row
template is owned: one `Border` with `CornerRadius="{TemplateBinding CornerRadius}"`,
five states with the names the `ListViewItem` control itself raises, taken from the
platform's own expanded template. A template the app owns cannot be second-guessed by
whichever variant the platform picked.

**Countries get flags.** Real ones now: the 196 flags in `Assets/Flags` are from
[flag-icons](https://github.com/yammadev/flag-icons) by Yammadev, MIT licensed, with the
licence shipped beside them. (The first version drew all 196 by hand as vector
primitives - the sandbox cannot reach a CDN and Windows has no flag font - and the
first "flag pack" uploaded here turned out to be 191 identical grey placeholder cards
reading NL, US, GB. The real pack came from the project's GitHub tarball.) `FlagData`
maps each catalog name to a two-letter code, which is the file name; each PNG is the
pack's 63x45 render, three times the 21x15 the UI draws it at, so it stays sharp at
200 percent scaling with no per-scale variants to ship. Every flag in the pack shares
one 7:5 frame, which is why `FlagIcon` can be a fixed size with `Stretch=UniformToFill`
and never crop or letterbox.

The flag is a rounded `Rectangle` with an `ImageBrush` fill, not an `Image` inside a
round corner: nothing in this framework clips children to a `CornerRadius`, so a
square-cornered bitmap would poke out behind any stroke drawn on top of it. Filling a
rounded shape *is* the clip - the picture only exists inside the geometry. The brush is
handed a fresh `BitmapImage` per country because bitmaps decode asynchronously and a
recycled row would otherwise show the previous country's flag for a frame. A country
with no file draws nothing rather than a placeholder.

Every other list gets the field's own icon on each row - `Globe` for websites, `Users`
for gender - which keeps the rule the rest of the app follows: one mark per field, the
same mark on the card, in the dialog, and now in the drawer.

**The filter appears at twelve options and not before.** Five genders do not need a
search box; 196 countries are unusable without one. Matches are substring, but ones that
*start* with what was typed sort first, so typing "ni" offers Nicaragua and Nigeria
before Bosnia and Herzegovina. Down moves from the box into the list, Enter takes the
first row, Escape closes without choosing.

Country also still accepts anything typed, because a residence this app has never heard
of is the user's problem to state and not ours to refuse. It arrives as a `Use "..."` row
at the top of the list rather than as a silent commit on focus loss, which is what the
editable `ComboBox` did.

Two details are defensive rather than cosmetic. The drawer's rows are built on `Opening`,
not held live, so nothing is mutating a collection the list has not measured - the fault
this app has hit three times. And neither the presenter nor the row template uses
`ColumnSpacing`: a `Grid` spaces around a collapsed column exactly as it does a visible
one, and the flag, the icon and the tick are each collapsed most of the time.

### Payment methods you can change, not just add

The methods panel on the website form grew two small discs beside its heading: a pencil
and a trash, 32px, matching the card hover actions' colouring - teal edit, red delete.
Each opens a *tap mode*: the form's contents step aside, a banner says what is being
asked - "Tap the payment method you want to edit", "Tap the payment methods you want to
delete" - and every method becomes a pill.

Edit takes one tap. The pill opens a small popup over the dialog - one field, because a
payment method's only attribute is its name - seeded with the current name and selected.
SAVE stays quiet (disabled, on the disabled teal) until what is typed is a real rename:
not blank, not the same name, not a name that already exists. The rename is applied
everywhere in one transaction - the catalog, and the delimited methods column of every
website that lists it - so no record is left stranded on the old name. Naming a method
after a deleted built-in revives that built-in; renaming a built-in off its shipped name
hides the shipped name, exactly as deleting it would.

Delete takes as many taps as you like. Picked pills turn red with a white tick, and the
action button counts them: `DELETE (3)`. Confirming opens the same confirmation dialog
the directories use, and the confirmation carries the warning that makes the whole flow
honest: *"PayPal is used by 4 websites. It will be removed from those records and no
longer offered."* The count is gathered before the dialog opens, not after it closes.
Confirming strips the methods from every record, removes them from the catalog, and
takes them off the open form - saving that form unchanged would otherwise write them
straight back.

The six built-ins are editable and deletable like everything else, which is why there
is now a `HiddenPaymentMethods` table. The built-ins live in code so they are always
offered, which is the right default and the wrong behaviour once the user deletes one:
a delete that quietly undoes itself on the next catalog read is not a delete. The table
is the memory of that decision, and `SchemaGuard` creates it on existing databases, so
no vault has to be rebuilt to get it.

One rule of the form's plumbing got stricter at the same time. The options list is an
`ItemsRepeater`, and a collapsed repeater that has never been measured throws on any
collection change - this app has hit that three times. A tap mode hides that repeater,
and the host's rename and delete rewrite the method list from under it, so
`SyncMethodOptionsSource` now detaches whenever a tap mode is running and reattaches
when it ends. The crash window closed before it ever opened.

### Adding a website without leaving the account form

The website field on the account form is a picker, and a picker can only offer what
exists. An account for a site that is not in the vault yet used to mean cancelling the
form, finding the Websites tab, creating the site, and starting the account over.

The site drawer now ends in an "Add a website" row when the form is the account form -
`ChoicePicker` takes an `AddLabel` and raises `AddRequested`; `FieldCell` passes both
through; `AccountDialog` is the only host that sets one. Picking it opens the add
website dialog *over* the account form - the modals at the HomePage root are stacked
account-then-website for exactly this reason - and when it closes, the account form is
still there with the new site already selected in the picker. The hand-off is a single
`LastAddedWebsite` property on the websites view model, read by the accounts view model
when it sees the website dialog close while its own is still open: the options are
re-fetched from the store rather than patched, the picker's own state is replaced
without touching anything else the user may have typed, and the new label is chosen.
Enter in the filter box takes the add row when nothing matches, so it is not mouse-only.

### The Services tab arrives: the vocabulary, before the records

The last empty stop on the rail. Services was the one section that faded to the bare
plate when clicked, because it was the one with nothing behind it - and it is also
the hardest thing in the app to design, for the reason the whole directory has been
waiting on: a service record can be almost anything, with its own columns and its
own relationships. So the tab starts where the workbook starts: not with records but
with the *vocabulary* they will be filed under. The six types the workbook names -
VPS, Domain, SMS, Proxy, Phone, Cloud - with a description and a note each, as cards
that name the type twice: in the identity row, and again as a NAME field beside the
description, with the note spanning below - the arrangement the website card already
established.

It is the same screen as the other three directories - the header with the disc, the
search, the card surface, the four states - because a fifth thing that behaved like a
fourth thing would not be simpler, it would be broken. The briefcase the rail button
already drew was promoted into the icon set so the section header carries the same
mark as the button that opens it. The dialog is the account dialog's shape: one
column, three fields, `# · RECORD ID` in preview, EDIT RECORD turning the preview
into the form in place. A blank name holds the form open rather than saving a
nameless type, and so does a name another type already has - checked against the
store with a case-insensitive LIKE rather than the loaded page, because the page is
a page.

**The six are seeded once, and only once.** The payment methods keep their built-ins
in code and merge them into every read, which is right for them: they are names with
brand marks, matched by name. A service type carries a description and a note of its
own, so it has to be a row the moment it exists at all - and rows the user can edit
and delete cannot also be immortal code constants. So the table *is* the catalog, and
the seed runs at exactly one moment: when the Services table has just been created.
`SchemaGuard` now returns the tables it created, and "this table did not exist a
moment ago" is the one honest signal for "this database has never had a catalog".
Seeding an empty table instead would resurrect a deleted VPS on every unlock, which
is not a seed, that is a haunting.

`SchemaGuard` creating the table on an existing vault means no vault has to be
rebuilt to get the catalog - it appears, seeded, at the next unlock.
### Still worth doing

A test project. The logic worth covering is pure .NET and does not need a UI:
`PasswordGenerator` (character classes, distribution), `VaultService` (wrong password
throws, parameters round-trip), and the search filter's escaping of `%` and `_`. It is
not here because this environment has no .NET SDK, and shipping a test project that has
never been compiled would be worse than not shipping one.

