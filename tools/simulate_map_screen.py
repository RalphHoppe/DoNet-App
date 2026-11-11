#!/usr/bin/env python3
"""Simulates the VPS map screen's math before it ships.

The map screen's whole stability rests on a handful of formulas: where a
marker sits on the plate, how the view's zoom and pan map world points to
screen points, how a network frames itself, and where the chooser opens.
Each of those is mirrored here, and each mirror asserts its invariants - so
a change that would put a tooltip in the wrong place or let a marker drift
off its country fails this script before it ever reaches a build.

Run with:  python3 tools/simulate_map_screen.py
"""

from __future__ import annotations

# The world canvas: fixed, in world pixels. Everything on the map is placed
# here exactly once; zoom and pan move the view over it, never the markers.
WORLD_W = 2560.0
WORLD_H = 1024.0

# The latitude band the plate covers - the same two numbers the renderer
# crops to and the view model places by.
LAT_TOP = 84.0
LAT_BOTTOM = -60.0

# The zoom range, relative to the fit: never wider than the whole earth,
# never deeper than five times closer.
ZOOM_HEADROOM = 5.0

# The marker's on-screen footprint, for the fit's padding.
MARKER_W = 38.0
MARKER_H = 32.0


def place(lat: float, lon: float) -> tuple[float, float]:
    """The marker's world position: the country's anchor on the plate."""
    nx = (lon + 180.0) / 360.0
    ny = (LAT_TOP - lat) / (LAT_TOP - LAT_BOTTOM)
    return nx * WORLD_W, ny * WORLD_H


def base_zoom(viewport_w: float, viewport_h: float) -> float:
    """The zoom at which the whole plate fits the viewport exactly."""
    return min(viewport_w / WORLD_W, viewport_h / WORLD_H)


def letterbox(zoom: float, viewport_w: float, viewport_h: float) -> tuple[float, float]:
    """The dead space between the viewport's edge and the plate's, when the
    plate is smaller than the viewport in a dimension: the content is
    centred, so the gap is half on each side, and the scroll offset is zero
    for that dimension."""
    return (
        max(0.0, (viewport_w - WORLD_W * zoom) / 2),
        max(0.0, (viewport_h - WORLD_H * zoom) / 2),
    )


def scroll_range(zoom: float, viewport_w: float, viewport_h: float) -> tuple[float, float]:
    """How far the view may scroll at this zoom: the scaled plate beyond the
    viewport. Zero in a dimension means the plate fits it, letterboxed."""
    return (
        max(0.0, WORLD_W * zoom - viewport_w),
        max(0.0, WORLD_H * zoom - viewport_h),
    )


def screen_of_world(
    wx: float, wy: float,
    offset_x: float, offset_y: float, zoom: float,
    viewport_w: float, viewport_h: float,
) -> tuple[float, float]:
    """Where a world point sits on screen: scaled, shifted past the scroll,
    and inset by the letterbox when the plate fits."""
    lx, ly = letterbox(zoom, viewport_w, viewport_h)
    return wx * zoom + lx - offset_x, wy * zoom + ly - offset_y


def zoom_about_pointer(
    offset_x: float, offset_y: float, zoom: float,
    pointer_x: float, pointer_y: float, factor: float,
    viewport_w: float, viewport_h: float,
) -> tuple[float, float, float]:
    """Zooms by a factor about the pointer, keeping the world point under it.

    The world point under the pointer is recovered through the letterbox,
    then re-anchored under the pointer at the new zoom. The resulting offset
    is clamped to the scroll range - when the pointer sits over the dead band
    beside the plate, the zoom simply grows toward the plate, and the clamp
    at the plate's true edges is what holds it there.
    """
    base = base_zoom(viewport_w, viewport_h)
    next_zoom = min(max(zoom * factor, base), base * ZOOM_HEADROOM)

    lx, ly = letterbox(zoom, viewport_w, viewport_h)
    world_x = (pointer_x + offset_x - lx) / zoom
    world_y = (pointer_y + offset_y - ly) / zoom

    nlx, nly = letterbox(next_zoom, viewport_w, viewport_h)
    max_x, max_y = scroll_range(next_zoom, viewport_w, viewport_h)
    new_x = min(max(world_x * next_zoom + nlx - pointer_x, 0.0), max_x)
    new_y = min(max(world_y * next_zoom + nly - pointer_y, 0.0), max_y)
    return new_x, new_y, next_zoom


def fit_to_markers(
    markers: list[tuple[float, float]],
    viewport_w: float, viewport_h: float,
    pad: float = 0.92,
) -> tuple[float, float, float]:
    """Frames a set of world points: the zoom that fits them, and the
    centred offsets."""
    base = base_zoom(viewport_w, viewport_h)

    left = min(x for x, _ in markers) - MARKER_W
    right = max(x for x, _ in markers) + MARKER_W
    top = min(y for _, y in markers) - MARKER_H
    bottom = max(y for _, y in markers) + MARKER_H

    zoom = min(
        viewport_w / max(right - left, 1.0),
        viewport_h / max(bottom - top, 1.0),
    ) * pad
    zoom = min(max(zoom, base), base * ZOOM_HEADROOM)

    lx, ly = letterbox(zoom, viewport_w, viewport_h)
    max_x, max_y = scroll_range(zoom, viewport_w, viewport_h)
    centre_x = (left + right) / 2 * zoom + lx
    centre_y = (top + bottom) / 2 * zoom + ly
    offset_x = min(max(centre_x - viewport_w / 2, 0.0), max_x)
    offset_y = min(max(centre_y - viewport_h / 2, 0.0), max_y)
    return offset_x, offset_y, zoom


def chooser_position(
    world_x: float, world_y: float,
    offset_x: float, offset_y: float, zoom: float,
    viewport_w: float, viewport_h: float, panel_w: float, panel_h: float,
) -> tuple[float, float]:
    """The chooser's top-left screen position, beside its marker, flipped to
    the left when the right would run it off the plate."""
    anchor_x, anchor_y = screen_of_world(
        world_x, world_y, offset_x, offset_y, zoom, viewport_w, viewport_h)

    gap = 14.0
    fits_right = anchor_x + gap + panel_w <= viewport_w - 16.0
    left = anchor_x + gap if fits_right else anchor_x - gap - panel_w
    top = anchor_y - 30.0

    left = min(max(left, 12.0), max(12.0, viewport_w - panel_w - 12.0))
    top = min(max(top, 12.0), max(12.0, viewport_h - panel_h - 12.0))
    return left, top


def spread_duplicates(points: list[tuple[float, float]], step: float = 13.0):
    """Markers sharing one anchor step onto a deterministic diagonal, so a
    shared country never stacks its markers into one invisible pile - and
    never moves a solitary marker off its anchor."""
    seen: dict[tuple[float, float], int] = {}
    out = []
    for x, y in points:
        n = seen.get((round(x, 1), round(y, 1)), 0)
        seen[(round(x, 1), round(y, 1))] = n + 1
        out.append((x + n * step, y + n * step))
    return out


def check_placement() -> None:
    # Spot countries, incl. the extremes of the band.
    cases = [
        ("Egypt", 27.0, 30.0),
        ("India", 22.0, 80.0),
        ("Greenland top", 83.0, -40.0),
        ("South America tip", -54.0, -70.0),
        ("Australia", -25.0, 135.0),
        ("USA west", 40.0, -100.0),
    ]
    for name, lat, lon in cases:
        x, y = place(lat, lon)
        assert 0 <= x <= WORLD_W, (name, x)
        assert 0 <= y <= WORLD_H, (name, y)

    # The band maps onto the frame exactly once.
    assert place(LAT_TOP, 0.0)[1] == 0.0
    assert place(LAT_BOTTOM, 0.0)[1] == WORLD_H
    assert place(0.0, -180.0)[0] == 0.0
    assert place(0.0, 180.0)[0] == WORLD_W
    print("placement: anchors land inside the world, band maps exactly")


def check_base_zoom() -> None:
    for vw, vh in [(1420, 832), (1100, 832), (866, 832), (524, 832), (3400, 1800)]:
        z = base_zoom(vw, vh)
        # At the base zoom the plate fits: both dimensions within the viewport.
        assert WORLD_W * z <= vw + 0.01 and WORLD_H * z <= vh + 0.01
        # And it fills one of them exactly.
        assert abs(WORLD_W * z - vw) < 0.01 or abs(WORLD_H * z - vh) < 0.01
    print("base zoom: the plate fits and fills at every window size")


def check_zoom_anchor() -> None:
    # Whatever the map holds under the pointer stays under it - wherever the
    # scroll range allows; where it does not (the pointer over the dead band
    # beside the plate), the offset clamps to the plate's true edge.
    vw, vh = 1420, 832
    base = base_zoom(vw, vh)
    rng = 0
    for zoom in [base, base * 1.6, base * 2.3, base * 5.0]:
        max_x, max_y = scroll_range(zoom, vw, vh)
        for px, py in [(700, 400), (10, 10), (1400, 800), (5, 700)]:
            for ox, oy in [(0.0, 0.0), (max_x / 2, max_y / 2), (max_x, max_y)]:
                lx, ly = letterbox(zoom, vw, vh)
                wx = (px + ox - lx) / zoom
                wy = (py + oy - ly) / zoom
                nx, ny, nz = zoom_about_pointer(ox, oy, zoom, px, py, 1.15, vw, vh)

                # Recover the world point's new screen position.
                sx, sy = screen_of_world(wx, wy, nx, ny, nz, vw, vh)

                # A pointer over the dead band beside the plate recovers a
                # world point outside it; that is a real case, not an error,
                # and the clamped branch below is what the view must do.

                nlx, nly = letterbox(nz, vw, vh)
                desired_x = wx * nz + nlx - px
                desired_y = wy * nz + nly - py
                nmax_x, nmax_y = scroll_range(nz, vw, vh)

                if 0 <= desired_x <= nmax_x:
                    assert abs(sx - px) < 0.01, (zoom, px, ox, sx, px)
                else:
                    # Clamped: the offset sits at a true edge, so the screen
                    # position is as close as the plate allows.
                    assert nx in (0.0, nmax_x), (nx, desired_x, nmax_x)
                if 0 <= desired_y <= nmax_y:
                    assert abs(sy - py) < 0.01, (zoom, py, oy, sy, py)
                else:
                    assert ny in (0.0, nmax_y), (ny, desired_y, nmax_y)
                rng += 1
    print(f"zoom: {rng} cases - anchored where scrollable, clamped at the edges")


def check_fit() -> None:
    vw, vh = 1420, 832
    # A spread network: Amsterdam, New York, Tokyo, Sydney.
    markers = [place(52.4, 4.9), place(40.7, -74.0), place(35.7, 139.7), place(-33.9, 151.2)]
    ox, oy, z = fit_to_markers(markers, vw, vh)
    for (x, y) in markers:
        sx, sy = screen_of_world(x, y, ox, oy, z, vw, vh)
        assert -MARKER_W <= sx <= vw + MARKER_W, (sx, vw)
        assert -MARKER_H <= sy <= vh + MARKER_H, (sy, vh)
    # A single-marker network frames at the deepest zoom, not beyond.
    ox, oy, z = fit_to_markers([place(52.4, 4.9)], vw, vh)
    base = base_zoom(vw, vh)
    assert z <= base * ZOOM_HEADROOM + 0.01
    # And the single marker is on screen.
    sx, sy = screen_of_world(*place(52.4, 4.9), ox, oy, z, vw, vh)
    assert 0 <= sx <= vw and 0 <= sy <= vh, (sx, sy)
    print("fit: networks frame inside the viewport, zoom within range")


def check_chooser() -> None:
    vw, vh = 1420, 832
    base = base_zoom(vw, vh)
    # A marker near the right edge flips its chooser to the left.
    for (lat, lon), expect_flip in [
        ((52.4, 4.9), False),      # Amsterdam: mid-map, opens right
        ((35.7, 139.7), True),     # Tokyo: far east, flips left
        ((-33.9, 151.2), True),    # Sydney: far east, flips left
        ((40.7, -74.0), False),    # New York: opens right
    ]:
        wx, wy = place(lat, lon)
        z = base * 2.0
        # Centre the view on the marker, within the scroll range.
        max_x, max_y = scroll_range(z, vw, vh)
        lx, ly = letterbox(z, vw, vh)
        ox = min(max(wx * z + lx - vw / 2, 0.0), max_x)
        oy = min(max(wy * z + ly - vh / 2, 0.0), max_y)
        left, top = chooser_position(wx, wy, ox, oy, z, vw, vh, 300.0, 260.0)
        anchor_x, _ = screen_of_world(wx, wy, ox, oy, z, vw, vh)
        flipped = left < anchor_x
        assert flipped == expect_flip, (lat, lon, left, anchor_x)
        assert 12 <= left <= vw - 312 and 12 <= top <= vh - 272
    print("chooser: opens beside its marker, flips at the east edge, stays on screen")


def check_duplicates() -> None:
    a = place(52.4, 4.9)
    spread = spread_duplicates([a, a, a, place(50.1, 8.7)])
    assert spread[0] == a                      # a solitary marker never moves
    assert spread[1][0] > spread[0][0]          # duplicates step apart
    assert spread[3] == place(50.1, 8.7)        # other anchors untouched
    print("duplicates: shared anchors spread; solitary markers never move")


def main() -> None:
    check_placement()
    check_base_zoom()
    check_zoom_anchor()
    check_fit()
    check_chooser()
    check_duplicates()
    print("\nall map screen math holds")


if __name__ == "__main__":
    main()
