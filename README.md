# DoNet

A WinUI 3 / Windows App SDK desktop app.

## What is built

| Screen | File | Notes |
| --- | --- | --- |
| Splash | `DoNet/Views/SplashPage.xaml` | The DoNet lockup draws itself on, holds, un-draws, then navigates |
| Create password | `DoNet/Views/CreatePasswordPage.xaml` | First run only, with validation |
| Lock | `DoNet/Views/LockPage.xaml` | Every launch after that |

The splash decides between the last two by asking `IAppState.IsPasswordConfigured`,
which is true once a vault exists on disk.

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
| Jersey 25 | Button captions | `Assets/Fonts/Jersey25-Regular.ttf` |

All three are SIL Open Font License 1.1; the `OFL.txt` files ship alongside them to
satisfy the attribution requirement. Baloo 2 is distributed by Google as a `wght`
variable font - the file here is a static 400 instance, because XAML's font loader only
picks a variable font's default named instance.

Both Baloo 2 (1.60 em) and Pacifico (1.76 em) declare unusually tall line boxes, so
text slots in the layout pin `LineHeight` explicitly. Remove those and the vertical
rhythm drifts.

## Layout

The design is a fixed 352 px column, centred, sitting 24 px above the optical centre of
the window. It does not stretch with the window; that is deliberate and matches the
Figma frame. Palette and type ramp are in `Styles/Colors.xaml` and `Styles/Fonts.xaml`;
`Styles/Theme.xaml` is the only dictionary `App.xaml` merges, and it pulls in the other
two itself so that `StaticResource` lookups always resolve.

## Not done yet

Three events are raised and nothing subscribes to them. They are the seams the encrypted
store plugs into, kept out of the views on purpose:

| Event | Should do |
| --- | --- |
| `CreatePasswordViewModel.Submitted` | Derive a key, create the SQLCipher database, move on |
| `LockViewModel.Unlocking` | Verify the password (the AES-GCM tag decides) and return the result |
| `LockViewModel.ForgotPasswordRequested` | Undecided - see below |

Until `Unlocking` has a subscriber the lock screen's button does nothing, by design: with
no verifier there is nothing to check against, so waving the user through would be worse
than standing still.

`AppState` decides "has a password been set" by looking for `vault.json` in the app's
local folder. That file is never written yet, so a fresh install always routes to
onboarding. It is the one place to revisit when the vault lands.

**"Forgot password?" is a product decision, not a technical one.** The warning on the
create-password screen says losing the password loses the data, and the key hierarchy
makes that literally true - there is no recovery path to build. The realistic options are
to explain that and offer to wipe and start over, or to add a recovery code at setup time.
The link is wired to a command that raises an event; nothing happens until that is settled.

## Tools

Design-time scripts, not part of the app build. They need `fonttools` and `pillow`.

| Script | Purpose |
| --- | --- |
| `tools/gen_logo_control.py` | Regenerates `Controls/DoNetLogo.xaml` from the Pacifico outlines |
| `tools/extract_logo_geometry.py` | Prints the raw geometry and arc lengths; holds the ring proportions |
| `tools/preview_animation.py` | Renders `docs/splash-animation.gif` |
| `tools/preview_screens.py` | Renders a pixel reconstruction of both screens for comparing against Figma |
| `tools/validate_xaml.py` | Checks resource keys, `x:Name`s and event handlers without a Windows build |
