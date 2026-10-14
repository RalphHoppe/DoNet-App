#!/usr/bin/env python3
"""Renders the world map the VPS map dialog draws its dots on.

The map is the equirectangular plate: longitude runs left to right over the
full width, latitude top to bottom over the full height, exactly 2:1. The
dialog positions every dot with that one rule - x from longitude, y from
latitude - so the asset and the placement math can never disagree about
where a country is.

The shapes are Natural Earth-derived country polygons, public domain, taken
from the world.geo.json project (UNLICENSE):

    https://github.com/johan/world.geo.json

Downloaded once with:

    curl -L -o world.tar.gz \
        https://codeload.github.com/johan/world.geo.json/tar.gz/refs/heads/master

and unpacked next to this script's data path. The script is kept in the
repository so the asset can be regenerated or restyled; the geo data itself
is not kept here.

Two things come out of the one pass, on purpose:

* the map image - Assets/Maps/world-map.png, drawn at twice the target
  resolution and downscaled, then quantised, so the coastlines are smooth
  and the file stays small;

* the anchor table - Models/CountryAnchors.cs, one point per country,
  computed from the same polygons the image draws. An anchor is the
  centroid of the country's largest polygon, shifted a third of the way
  toward that polygon's northern edge - the dot sits at the top of its
  country, as asked, without sliding off small ones.
"""

from __future__ import annotations

import json
import math
from pathlib import Path

from PIL import Image, ImageDraw

REPO = Path(__file__).resolve().parent.parent
GEO = Path("/tmp/geo/world.geo.json-master/countries.geo.json")
OUT_PNG = REPO / "DoNet" / "Assets" / "Maps" / "world-map.png"
OUT_CS = REPO / "DoNet" / "Models" / "CountryAnchors.cs"

# Final image. 2:1, and wide enough that the dialog never needs more than
# it holds, even on a large monitor at high scaling.
W, H = 4096, 2048
SS = 2  # supersample factor for smooth coastlines

# The palette, from the app's own colours: the sea is the map's tint, the
# land is the card's white, the borders and graticule are the app's slate.
SEA = (233, 239, 246)
LAND = (255, 255, 255)
BORDER = (201, 213, 227)
GRATICULE = (222, 230, 238)

# FlagData's 196 country names -> the geo data's spellings, where they
# differ. Everything not here matches by name, and the countries the 110m
# polygons do not carry at all are in MANUAL_ANCHORS below.
ALIASES = {
    "United States": "United States of America",
    "North Macedonia": "Macedonia",
    "Serbia": "Republic of Serbia",
    "Eswatini": "Swaziland",
    "Timor-Leste": "East Timor",
    "Bahamas": "The Bahamas",
    "Guinea-Bissau": "Guinea Bissau",
    "Tanzania": "United Republic of Tanzania",
    "Czechia": "Czech Republic",
    "Palestine": "West Bank",
    "Congo": "Republic of the Congo",
}

# Countries in the flag set that the 110m polygons do not draw: islands and
# microstates. Their anchors are their own coordinates, so a VPS there still
# lands on the map - on the sea, in the right place, with its flag on the dot.
MANUAL_ANCHORS = {
    "Andorra": (42.55, 1.58),
    "Antigua and Barbuda": (17.06, -61.80),
    "Bahrain": (26.03, 50.55),
    "Barbados": (13.18, -59.55),
    "Cape Verde": (15.10, -23.60),
    "Comoros": (-11.70, 43.30),
    "Dominica": (15.40, -61.35),
    "Grenada": (12.10, -61.70),
    "Kiribati": (1.40, 173.00),
    "Liechtenstein": (47.15, 9.55),
    "Maldives": (3.20, 73.20),
    "Marshall Islands": (7.10, 171.20),
    "Mauritius": (-20.35, 57.55),
    "Micronesia": (6.90, 158.20),
    "Monaco": (43.73, 7.42),
    "Nauru": (-0.52, 166.93),
    "Palau": (7.50, 134.60),
    "Saint Kitts and Nevis": (17.33, -62.75),
    "Saint Lucia": (13.90, -61.00),
    "Saint Vincent and the Grenadines": (13.25, -61.20),
    "Samoa": (-13.75, -172.10),
    "San Marino": (43.94, 12.45),
    "Sao Tome and Principe": (0.25, 6.60),
    "Seychelles": (-4.60, 55.45),
    "Singapore": (1.35, 103.82),
    "Tonga": (-21.20, -175.20),
    "Tuvalu": (-7.50, 178.00),
    "Vatican City": (41.90, 12.45),
}


def rings_of(geometry: dict) -> list[list[list[float]]]:
    """The polygon rings of a GeoJSON geometry, outer rings only.

    Holes are skipped: at this scale they are invisible, and skipping them
    keeps both the drawing and the centroid math simple.
    """
    kind = geometry["type"]
    if kind == "Polygon":
        return [geometry["coordinates"][0]]
    if kind == "MultiPolygon":
        return [poly[0] for poly in geometry["coordinates"]]
    return []


def unwrap(ring: list[list[float]]) -> list[list[float]]:
    """Makes a ring continuous across the antimeridian.

    Russia and Fiji cross the line where longitude jumps from 180 to -180.
    Walking the ring and undoing any jump bigger than 180 degrees turns them
    into ordinary shapes, which both the shoelace formulas and the drawing
    (with the ring repeated at both offsets) then handle correctly.
    """
    out = [list(ring[0])]
    for lon, lat in ring[1:]:
        prev = out[-1][0]
        while lon - prev > 180:
            lon -= 360
        while prev - lon > 180:
            lon += 360
        out.append([lon, lat])
    return out


def centroid_and_top(ring: list[list[float]]) -> tuple[float, float, float]:
    """A ring's centroid and northern edge, by the shoelace formulas."""
    area = 0.0
    cx = 0.0
    cy = 0.0
    top = ring[0][1]
    for (x1, y1), (x2, y2) in zip(ring, ring[1:] + ring[:1]):
        cross = x1 * y2 - x2 * y1
        area += cross
        cx += (x1 + x2) * cross
        cy += (y1 + y2) * cross
        top = max(top, y1, y2)
    area /= 2.0
    if abs(area) < 1e-9:
        return ring[0][0], ring[0][1], top
    return cx / (6 * area), cy / (6 * area), top


def bbox_area(ring: list[list[float]]) -> float:
    xs = [p[0] for p in ring]
    ys = [p[1] for p in ring]
    return (max(xs) - min(xs)) * (max(ys) - min(ys))


def anchor_of(rings: list[list[list[float]]]) -> tuple[float, float]:
    """The dot anchor: top-of-country, from the country's largest ring.

    The centroid of the largest ring, shifted a third of the way toward that
    ring's northern edge. Small countries shift a hair; wide ones read as
    pinned at the top of their shape rather than floating mid-country.
    """
    ring = max(rings, key=bbox_area)
    ring = unwrap(ring)
    cx, cy, top = centroid_and_top(ring)
    lat = cy + (top - cy) * 0.34
    lon = ((cx + 180) % 360) - 180  # back onto the map
    return round(lat, 2), round(lon, 2)


def main() -> None:
    features = json.loads(GEO.read_text(encoding="utf-8"))["features"]
    by_geo_name: dict[str, dict] = {
        f["properties"]["name"]: f for f in features
    }

    ws, hs = W * SS, H * SS

    def px(lon: float, lat: float) -> tuple[float, float]:
        return (lon + 180) / 360 * ws, (90 - lat) / 180 * hs

    image = Image.new("RGB", (ws, hs), SEA)
    draw = ImageDraw.Draw(image)

    # The graticule sits under the land: it is the sea's own marking, and
    # the countries interrupt it the way the reference maps do.
    for lon in range(-150, 181, 30):
        x = px(lon, 0)[0]
        draw.line([(x, 0), (x, hs)], fill=GRATICULE, width=SS)
    for lat in range(-60, 91, 30):
        y = px(0, lat)[1]
        draw.line([(0, y), (ws, y)], fill=GRATICULE, width=SS)
    # The equator and the prime meridian carry a touch more weight.
    draw.line([(px(0, 0)[0], 0), (px(0, 0)[0], hs)], fill=BORDER, width=SS)
    draw.line([(0, px(0, 0)[1]), (ws, px(0, 0)[1])], fill=BORDER, width=SS)

    # Every ring is drawn at three horizontal offsets. Rings that do not
    # cross the antimeridian fall outside the canvas at the other two and
    # cost nothing; rings that do appear correctly on both edges.
    for feature in features:
        for ring in rings_of(feature["geometry"]):
            ring = unwrap(ring)
            for offset in (-ws, 0, ws):
                points = [(px(lon, lat)[0] + offset, px(lon, lat)[1])
                          for lon, lat in ring]
                if points:
                    draw.polygon(
                        points, fill=LAND, outline=BORDER, width=2 * SS)

    # Antarctica closes at the latitude the dataset clips it to (about
    # -85.6), which would leave the pole itself painted as sea. The ice
    # sheet is completed to the bottom edge: same fill, same border, the
    # seam landing on the closure line the polygon already draws.
    clip_y = px(0, -85.55)[1]
    draw.rectangle(
        [(-40, clip_y), (ws + 40, hs + 40)], fill=LAND)
    draw.line([(-40, clip_y), (ws + 40, clip_y)], fill=BORDER, width=2 * SS)

    image = image.resize((W, H), Image.LANCZOS)

    OUT_PNG.parent.mkdir(parents=True, exist_ok=True)
    quantised = image.convert("P", palette=Image.ADAPTIVE, colors=64)
    quantised.save(OUT_PNG, optimize=True)
    print(f"map: {OUT_PNG} ({OUT_PNG.stat().st_size / 1024:.0f} KiB)")

    # The anchor table, from the same shapes the image just drew.
    import re
    flags = (REPO / "DoNet" / "Controls" / "FlagData.cs").read_text(
        encoding="utf-8")
    countries = re.findall(r'"([^"]+)" => "', flags)

    anchors: dict[str, tuple[float, float]] = {}
    unmatched: list[str] = []
    for country in countries:
        if country in MANUAL_ANCHORS:
            anchors[country] = MANUAL_ANCHORS[country]
            continue
        geo_name = ALIASES.get(country, country)
        feature = by_geo_name.get(geo_name)
        if feature is None:
            unmatched.append(country)
            continue
        anchors[country] = anchor_of(rings_of(feature["geometry"]))

    if unmatched:
        raise SystemExit(f"no anchor for: {unmatched}")

    lines = [
        "using System.Collections.Generic;",
        "",
        "namespace DoNet.Models;",
        "",
        "/// <summary>",
        "/// <para>",
        "/// One point per country, at the top of the country's shape, for",
        "/// placing things on the world map. Latitude then longitude, both",
        "/// degrees; the map's own projection is the plain equirectangular",
        "/// one - x from longitude, y from latitude - so these two numbers",
        "/// are all a placement needs.",
        "/// </para>",
        "/// <para>",
        "/// Generated with <c>tools/render_world_map.py</c> from the same",
        "/// Natural Earth polygons the map image is drawn from (public",
        "/// domain, via the world.geo.json project), so the table and the",
        "/// picture underneath it cannot disagree about where a country is.",
        "/// The countries the 110m polygons do not carry - islands and",
        "/// microstates - are anchored by hand at their own coordinates.",
        "/// </para>",
        "/// </summary>",
        "public static class CountryAnchors",
        "{",
        "    /// <summary>",
        "    /// The anchor point for a country's name, or null when the name",
        "    /// is not one the table knows.",
        "    /// </summary>",
        "    public static (double Latitude, double Longitude)? For(string? country)",
        "    {",
        "        return country is not null && Anchors.TryGetValue(country, out var anchor)",
        "            ? anchor",
        "            : null;",
        "    }",
        "",
        "    /// <summary>The table, alphabetical, in degrees.</summary>",
        "    private static readonly Dictionary<string, (double Latitude, double Longitude)> Anchors = new()",
        "    {",
    ]
    for country in sorted(anchors):
        lat, lon = anchors[country]

        def num(value: float) -> str:
            text = f"{value:.2f}".rstrip("0").rstrip(".")
            return text if text not in ("", "-0") else "0"

        lines.append(
            f'        ["{country}"] = ({num(lat)}, {num(lon)}),')
    lines += ["    };", "}", ""]
    OUT_CS.write_text("\n".join(lines), encoding="utf-8")
    print(f"anchors: {OUT_CS} ({len(anchors)} countries)")


if __name__ == "__main__":
    main()
