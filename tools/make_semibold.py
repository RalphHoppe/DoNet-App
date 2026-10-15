#!/usr/bin/env python3
"""
Regenerates DoNet/Assets/Fonts/Baloo2-SemiBold.ttf from Google's variable Baloo 2.

Baloo 2 ships as a wght 400-800 variable font, and XAML's font loader only picks a
variable font's default named instance - so the weights the app uses have to be pinned
into separate static files.

The instancer pins the outlines but leaves the metadata claiming Regular 400. Left like
that the result collides with Baloo2-Regular.ttf: two files declaring the same family
and weight, which the loader cannot tell apart. So this also rewrites the name table to
give the instance its own family name and sets usWeightClass.

    pip install fonttools
    python3 tools/make_semibold.py path/to/Baloo2[wght].ttf
"""
import os
import sys

from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

WEIGHT = 600
FAMILY = "Baloo 2 SemiBold"
POSTSCRIPT = "Baloo2-SemiBold"

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                   "DoNet", "Assets", "Fonts", "Baloo2-SemiBold.ttf")


def main(source):
    font = instancer.instantiateVariableFont(TTFont(source), {"wght": WEIGHT}, inplace=False)

    names = {1: FAMILY, 2: "Regular", 3: FAMILY, 4: FAMILY, 6: POSTSCRIPT}
    table = font["name"]
    for name_id, value in names.items():
        table.setName(value, name_id, 3, 1, 0x409)   # Windows / Unicode BMP / en-US
        table.setName(value, name_id, 1, 0, 0)       # Mac / Roman / English
    for name_id in (16, 17):                         # typographic family/subfamily
        table.removeNames(nameID=name_id)

    font["OS/2"].usWeightClass = WEIGHT
    font.save(OUT)
    print(f"wrote {OUT}")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1])
