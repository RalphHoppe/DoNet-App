"""Pixel forensics for services_bar.png / services.png (no vision needed).

Classifies pixels, prints ASCII maps, and measures each colored region:
bbox, fill solidity, enclosed-white interior (the icon inside the button),
and corner-radius estimate for the button shape.
"""

from collections import deque

from PIL import Image

REF = "screenshots/v2_screenshots_and_stuff/services_bar.png"
FULL = "screenshots/v2_screenshots_and_stuff/services.png"


def classify(p):
    r, g, b = p[:3]
    mx, mn = max(r, g, b), min(r, g, b)
    if r > 235 and g > 235 and b > 235:
        return "W"  # white
    if mx - mn < 14 and r > 205:
        return "g"  # light grey
    if mx - mn < 30 and r < 120:
        return "K"  # dark (text)
    # hue-ish classes with generous ranges to catch gradient interiors
    if r > 150 and g < 160 and b < 160 and r - g > 45 and r - b > 45:
        return "R"
    if g > 130 and g - r > 40 and g - b > 30:
        return "G"
    if b > 130 and b - r > 35 and b - g > 25:
        return "B"
    if r > 150 and g > 110 and b < 130 and r - b > 55 and g - b > 25:
        return "Y"
    if g > 120 and r > 90 and b > 100 and abs(r - g) < 45 and g - b > 15:
        return "T"  # teal
    return "?"


def ascii_map(img, x0, y0, x1, y1, step):
    print(f"    map region x[{x0}:{x1}] y[{y0}:{y1}] step={step}")
    hdr = "     " + "".join(
        str((x // 100) % 10) if x % 100 == 0 else " " for x in range(x0, x1, step)
    )
    print(hdr)
    for y in range(y0, y1, step):
        row = "".join(classify(img.getpixel((x, y))) for x in range(x0, x1, step))
        print(f"{y:4d} {row}")


def regions(img):
    """Bounding boxes per class via simple label of classified pixels."""
    w, h = img.size
    px = img.load()
    boxes = {}
    for y in range(0, h, 2):
        for x in range(0, w, 2):
            c = classify(px[x, y])
            if c in "RGBYK":
                bb = boxes.setdefault(c, [x, y, x, y, 0])
                bb[0], bb[1] = min(bb[0], x), min(bb[1], y)
                bb[2], bb[3] = max(bb[2], x), max(bb[3], y)
                bb[4] += 1
    return boxes


def enclosed_white(img, x0, y0, x1, y1, pad=2):
    """White pixels inside the bbox that cannot reach outside it (the icon)."""
    px = img.load()
    x0, y0 = max(0, x0 - pad), max(0, y0 - pad)
    x1, y1 = min(img.size[0] - 1, x1 + pad), min(img.size[1] - 1, y1 + pad)
    seen = set()
    q = deque()
    # seed the ring just outside the bbox
    for x in range(x0, x1 + 1):
        for y in (y0, y1):
            if classify(px[x, y]) == "W":
                q.append((x, y))
    for y in range(y0, y1 + 1):
        for x in (x0, x1):
            if classify(px[x, y]) == "W":
                q.append((x, y))
    seen.update(q)
    while q:
        x, y = q.popleft()
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if x0 <= nx <= x1 and y0 <= ny <= y1 and (nx, ny) not in seen:
                if classify(px[nx, ny]) == "W":
                    seen.add((nx, ny))
                    q.append((nx, ny))
    inner = []
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            if classify(px[x, y]) == "W" and (x, y) not in seen:
                inner.append((x, y))
    return inner


def corner_radius(img, cls, x0, y0, x1, y1):
    """How far the first colored pixel of the top rows sits from the left."""
    px = img.load()
    rows = {}
    for y in range(y0, min(y0 + 40, y1)):
        first = next((x for x in range(x0, x1 + 1) if classify(px[x, y]) == cls), None)
        if first is not None:
            rows[y - y0] = first - x0
    return rows


def report(path):
    img = Image.open(path).convert("RGB")
    print(f"\n===== {path}  size={img.size} =====")
    boxes = regions(img)
    for c, (x0, y0, x1, y1, n) in sorted(boxes.items()):
        w, h = x1 - x0, y1 - y0
        area = (w + 1) * (h + 1)
        print(
            f"\n {c}: bbox x[{x0}..{x1}] y[{y0}..{y1}]  {w+1}x{h+1}px"
            f"  fill={n * 4 / area:.2f}"
        )
        inner = enclosed_white(img, x0, y0, x1, y1)
        if inner:
            ix0 = min(p[0] for p in inner)
            ix1 = max(p[0] for p in inner)
            iy0 = min(p[1] for p in inner)
            iy1 = max(p[1] for p in inner)
            print(
                f"    enclosed white (icon): {len(inner)}px  bbox x[{ix0}..{ix1}]"
                f" y[{iy0}..{iy1}]  {ix1-ix0+1}x{iy1-iy0+1}px"
                f"  margins L{ix0-x0} R{x1-ix1} T{iy0-y0} B{y1-iy1}"
            )
        cr = corner_radius(img, c, x0, y0, x1, y1)
        if cr:
            print(f"    top-left row indents (first 20): {[cr[k] for k in sorted(cr)[:20]]}")
    return img


bar = report(REF)
full = report(FULL)

print("\n===== services_bar.png full ASCII (step 6) =====")
w, h = bar.size
ascii_map(bar, 0, 0, w, h, 6)
