"""Fit the button corner shape + full-layout geometry of services.png."""

from PIL import Image

BAR = Image.open("screenshots/v2_screenshots_and_stuff/services_bar.png").convert("RGB")
FULL = Image.open("screenshots/v2_screenshots_and_stuff/services.png").convert("RGB")


def mask_bbox(img, pred, x0=0, y0=0, x1=None, y1=None):
    w, h = img.size
    x1 = x1 or w
    y1 = y1 or h
    px = img.load()
    xs, ys = [], []
    for y in range(y0, y1):
        for x in range(x0, x1):
            if pred(px[x, y]):
                xs.append(x)
                ys.append(y)
    if not xs:
        return None
    return min(xs), min(ys), max(xs), max(ys)


def is_flat_red(p):
    r, g, b = p[:3]
    return r > 200 and g < 140 and b < 140 and abs(r - g) > 80


# ---- 1. corner-shape fit on the flat-red VPS button in the bar image ----
bx = mask_bbox(BAR, is_flat_red)
print("VPS button bbox (bar):", bx)
x0, y0, x1, y1 = bx
w, h = x1 - x0 + 1, y1 - y0 + 1
print(f"size {w}x{h}")
px = BAR.load()
widths = []
for y in range(y0, y1 + 1):
    row = [x for x in range(x0, x1 + 1) if is_flat_red(px[x, y])]
    widths.append((y - y0, (max(row) - min(row) + 1) if row else 0, min(row) - x0 if row else None))
print("row -> (width, left-indent) every 4 rows:")
for dy, wd, ind in widths[::4]:
    print(f"  y={dy:3d}  width={wd:3d}  indent={ind}")

# fit candidates against full profile
print("\nfit candidates (predicted width at row, observed):")
r = w / 2
for name in ("circle", "rho45", "rho50", "rho56", "sq4", "sq5"):
    preds = []
    for dy, wd, _ in widths[::4]:
        if name == "circle":
            v = 2 * (r * r - (dy - r) ** 2) ** 0.5 if abs(dy - r) < r else 0
        else:
            rho = {"rho45": 45, "rho50": 50, "rho56": 56}.get(name)
            if rho:
                v = w - 2 * (rho - max(0.0, (2 * rho * dy - dy * dy) ** 0.5)) if dy < rho else w
            else:
                n = 4 if name == "sq4" else 5
                t = max(0.0, 1 - (dy / r) ** n) ** (1 / n)
                v = 2 * r * t
        preds.append(f"{v:5.0f}/{wd:<3d}")
    print(f"  {name:6s}: " + " ".join(preds))

# ---- 2. full layout of services.png ----
print("\n===== services.png layout =====")
W, H = FULL.size
print("size", FULL.size)


def is_dark(p):
    r, g, b = p[:3]
    return max(r, g, b) < 130


def is_teal(p):
    r, g, b = p[:3]
    return abs(r - 75) < 60 and abs(g - 170) < 60 and abs(b - 152) < 60 and g > r and g > b


def is_pill(p):
    r, g, b = p[:3]
    return 232 < r < 250 and 232 < g < 250 and 232 < b < 250


# rail: dark/teal region on the far left - find its right edge
rail_bb = mask_bbox(FULL, lambda p: max(p[:3]) < 200, 0, 0, 400, H)
print("dark-rail bbox (left strip):", rail_bb)

# title text 'SERVICES' - dark pixels in content top area, left of x=1500
title_bb = mask_bbox(FULL, is_dark, 400, 150, 1600, 420)
print("title text bbox:", title_bb)

# search/ADD pills - light grey blobs top-right
pill_bb = mask_bbox(FULL, is_pill, 1700, 150, W, 450)
print("grey pill zone bbox (top-right):", pill_bb)


def is_green(p):
    r, g, b = p[:3]
    return g > 120 and g - r > 40 and g - b > 30


def is_blue(p):
    r, g, b = p[:3]
    return b > 120 and b - r > 35 and b - g > 25


def is_yellow_or_orange(p):
    r, g, b = p[:3]
    return r > 150 and g > 100 and b < 140 and r - b > 60 and g - b > 20


vps = mask_bbox(FULL, is_flat_red, 400, 300, 2000, 800)
proxy = mask_bbox(FULL, is_green, 400, 300, 2000, 800)
domain = mask_bbox(FULL, is_blue, 400, 300, 2000, 800)
wallets = mask_bbox(FULL, is_yellow_or_orange, 400, 300, 2000, 800)
print("VPS btn:", vps)
print("Proxy btn:", proxy)
print("Domain btn:", domain)
print("Wallets btn:", wallets)

# labels under buttons (dark text y>540)
labels = mask_bbox(FULL, is_dark, 400, 545, 2000, 700)
print("labels zone bbox:", labels)

# anything below y=700? sample classes
counts = {}
fpx = FULL.load()
for y in range(700, H, 8):
    for x in range(0, W, 8):
        r, g, b = fpx[x, y][:3]
        if max(r, g, b) > 235 and min(r, g, b) > 235:
            counts["white"] = counts.get("white", 0) + 1
        elif max(r, g, b) < 130:
            counts["dark"] = counts.get("dark", 0) + 1
        else:
            counts["other"] = counts.get("other", 0) + 1
            if counts["other"] < 12:
                print("  non-white below y=700 sample:", x, y, (r, g, b))
print("below y=700 class counts (sampled/8):", counts)
