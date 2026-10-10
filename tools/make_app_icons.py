#!/usr/bin/env python3
"""
Draws the app's package icons from the brand's own pieces.

The template's placeholder tiles shipped with the project and were never
replaced; a build handed to someone shows a coloured square in the Start menu
instead of the app's mark. This draws every manifest asset from two things the
app already owns: the brand teal (#4BAA98) and the Pacifico "D" the wordmark
starts with - deterministic, on-brand, and re-runnable whenever the mark or the
palette moves.

The splash is the one exception: transparent behind a teal D, because the
manifest paints the letterbox around it (white, set in Package.appxmanifest) and
the app's own SplashPage takes over the moment the window exists.

Run from the repository root:   python3 tools/make_app_icons.py
"""
import pathlib

from PIL import Image, ImageDraw, ImageFont

ROOT = pathlib.Path(__file__).resolve().parent.parent
ASSETS = ROOT / "DoNet" / "Assets"
FONT = ASSETS / "Fonts" / "Pacifico-Regular.ttf"

TEAL = (75, 170, 152, 255)      # #4BAA98, BrandPrimaryBrush
WHITE = (255, 255, 255, 255)

# (file, size, glyph height as a share of the tile, glyph colour, background)
TILES = [
    ("Square150x150Logo.scale-200.png", (300, 300), 0.62, WHITE, TEAL),
    ("Square44x44Logo.scale-200.png", (88, 88), 0.62, WHITE, TEAL),
    ("Square44x44Logo.targetsize-24_altform-unplated.png", (24, 24), 0.66, WHITE, TEAL),
    ("StoreLogo.png", (50, 50), 0.62, WHITE, TEAL),
    ("Wide310x150Logo.scale-200.png", (620, 300), 0.62, WHITE, TEAL),
    ("LockScreenLogo.scale-200.png", (48, 48), 0.62, WHITE, TEAL),
]

# The splash: transparent behind a teal D - the white glyph the tiles use would
# vanish on the white letterbox the manifest paints around it.
SPLASH = ("SplashScreen.scale-200.png", (1240, 600), 0.52, TEAL, (0, 0, 0, 0))


def draw_glyph(size, share, colour, background):
    """A tile with the Pacifico D centred on it."""
    image = Image.new("RGBA", size, background)
    draw = ImageDraw.Draw(image)

    # Size the font so the glyph's ink box lands on the share of the tile asked
    # for, then centre the ink box rather than the em box - a script face's
    # flourish makes the two disagree by a lot.
    probe = ImageDraw.Draw(Image.new("RGBA", (1, 1)))
    target = int(size[1] * share)

    font_size = 100
    font = ImageFont.truetype(str(FONT), font_size)
    box = probe.textbbox((0, 0), "D", font=font)
    font_size = max(8, int(font_size * target / (box[3] - box[1])))

    font = ImageFont.truetype(str(FONT), font_size)
    box = probe.textbbox((0, 0), "D", font=font)
    left, top = (size[0] - (box[2] - box[0])) / 2, (size[1] - (box[3] - box[1])) / 2
    draw.text((left - box[0], top - box[1]), "D", font=font, fill=colour)

    return image


def main():
    for name, size, share, colour, background in TILES + [SPLASH]:
        path = ASSETS / name
        image = draw_glyph(size, share, colour, background)
        image.save(path)
        print(f"{path.relative_to(ROOT)}: {size[0]}x{size[1]} drawn")

    print("Done. The manifest needs no new names - every file kept its own.")


if __name__ == "__main__":
    main()
