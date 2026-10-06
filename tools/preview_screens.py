#!/usr/bin/env python3
"""
Renders a pixel reconstruction of the two DoNet screens using the real fonts, so the
layout numbers can be checked against the Figma export before they are written into XAML.

This is a design aid only - it is not part of the app build.
"""
import math
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

_FD = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                   "DoNet", "Assets", "Fonts")
PAC = os.path.join(_FD, "Pacifico-Regular.ttf")
BAL = os.path.join(_FD, "Baloo2-Regular.ttf")
BALSB = os.path.join(_FD, "Baloo2-SemiBold.ttf")
JER = os.path.join(_FD, "Jersey25-Regular.ttf")
BAU = os.path.join(_FD, "Baumans-Regular.ttf")

W, H = 1546, 980
SS = 2  # supersample for clean edges

# ---- palette ---------------------------------------------------------------
TEAL_DARK = (73, 164, 140)
TEAL_LITE = (131, 205, 180)
BTN = (75, 170, 152)
TXT_SUB = (92, 96, 102)
ERR_FG = (192, 57, 43)
PLACEHOLDER = (140, 145, 150)
ICON = (160, 165, 171)
FIELD_BORDER = (228, 230, 233)
WARN_BG = (253, 244, 231)
WARN_BORDER = (246, 222, 180)
WARN_FG = (196, 113, 11)
OK_BG = (234, 246, 241)
OK_BORDER = (191, 224, 210)
OK_FG = (46, 125, 91)
DANGER = (224, 27, 36)
DANGER_HEAD = (164, 22, 26)
DANGER_BODY = (192, 57, 43)

# ---- metrics ---------------------------------------------------------------
COL = 352
FIELD_H, WARN_H, BTN_H, RADIUS = 38, 32, 40, 8
PAC_SIZE = 26
RING_RATIO, GAP_RATIO, HOLE_RATIO = 0.365, 0.095, 0.47


def font(p, s):
    return ImageFont.truetype(p, int(s * SS))


def ink(dr, f, t):
    b = dr.textbbox((0, 0), t, font=f)
    return b


def hgrad(dr, box, c0, c1, radius=None):
    """Horizontal linear gradient, optionally clipped to a rounded rect."""
    x0, y0, x1, y1 = [int(v) for v in box]
    strip = Image.new("RGB", (x1 - x0, y1 - y0))
    sd = ImageDraw.Draw(strip)
    for i in range(x1 - x0):
        t = i / max(1, (x1 - x0 - 1))
        sd.line([(i, 0), (i, y1 - y0)],
                fill=tuple(int(c0[k] + (c1[k] - c0[k]) * t) for k in range(3)))
    return strip, (x0, y0)


def draw_logo(img, dr, cx, cy, pac_size):
    """Ring + Pacifico wordmark, sharing one left-to-right teal gradient."""
    f = font(PAC, pac_size)
    b = ink(dr, f, "DoNet")
    tw, th = b[2] - b[0], b[3] - b[1]
    ring = tw * RING_RATIO
    gap = tw * GAP_RATIO
    hole = ring * HOLE_RATIO
    total = ring + gap + tw
    left = cx * SS - total / 2
    top = cy * SS

    # paint the whole lockup into a mask, then composite the gradient through it
    mask = Image.new("L", img.size, 0)
    md = ImageDraw.Draw(mask)
    rcx, rcy = left + ring / 2, top
    md.ellipse([rcx - ring / 2, rcy - ring / 2, rcx + ring / 2, rcy + ring / 2], fill=255)
    md.ellipse([rcx - hole / 2, rcy - hole / 2, rcx + hole / 2, rcy + hole / 2], fill=0)
    md.text((left + ring + gap - b[0], top - th / 2 - b[1]), "DoNet", font=f, fill=255)

    grad = Image.new("RGB", img.size)
    gd = ImageDraw.Draw(grad)
    for i in range(int(left), int(left + total) + 1):
        t = (i - left) / max(1.0, total)
        gd.line([(i, 0), (i, img.size[1])],
                fill=tuple(int(TEAL_DARK[k] + (TEAL_LITE[k] - TEAL_DARK[k]) * t) for k in range(3)))
    img.paste(grad, (0, 0), mask)
    return total / SS, ring / SS


def lock_icon(dr, x, y, s, colour):
    """Outline padlock, drawn on a 16x16 grid scaled by s/16."""
    u = s / 16.0
    t = max(1, int(1.25 * u))
    bx0, by0, bx1, by1 = x + 2.6 * u, y + 7.0 * u, x + 13.4 * u, y + 14.2 * u
    dr.rounded_rectangle([bx0, by0, bx1, by1], radius=1.6 * u, outline=colour, width=t)
    dr.arc([x + 5.0 * u, y + 2.2 * u, x + 11.0 * u, y + 9.4 * u], 180, 360, fill=colour, width=t)
    dr.line([x + 5.0 * u, y + 5.8 * u, x + 5.0 * u, y + 7.2 * u], fill=colour, width=t)
    dr.line([x + 11.0 * u, y + 5.8 * u, x + 11.0 * u, y + 7.2 * u], fill=colour, width=t)


def warn_icon(dr, x, y, s, colour):
    u = s / 16.0
    t = max(1, int(1.3 * u))
    dr.polygon([(x + 8 * u, y + 2.2 * u), (x + 15 * u, y + 13.8 * u), (x + 1 * u, y + 13.8 * u)],
               outline=colour, width=t)
    dr.line([x + 8 * u, y + 6.6 * u, x + 8 * u, y + 9.8 * u], fill=colour, width=t)
    dr.ellipse([x + 7.2 * u, y + 11.0 * u, x + 8.8 * u, y + 12.6 * u], fill=colour)


def eye_icon(dr, x, y, s, colour):
    """Outline eye on a 16x16 grid: two circular arcs forming the almond, plus a pupil.

    The XAML draws the almond with cubic beziers, which PIL has no primitive for; two
    arcs through the same three points (0.9,8) (8,3.4) (15.1,8) are visually identical
    at this size and keep the preview honest about the icon's footprint.
    """
    u = s / 16.0
    t = max(1, int(1.3 * u))
    r = 7.78 * u
    for cy_u, a0, a1 in ((11.18, 204.1, 335.9), (4.82, 24.1, 155.9)):
        c = (x + 8 * u, y + cy_u * u)
        dr.arc([c[0] - r, c[1] - r, c[0] + r, c[1] + r], a0, a1, fill=colour, width=t)
    pr = 2.05 * u
    dr.ellipse([x + 8 * u - pr, y + 8 * u - pr, x + 8 * u + pr, y + 8 * u + pr],
               outline=colour, width=t)


def lock_screen(error=False):
    """Screen 3. Set error=True to check that the message and the link share the row."""
    img = Image.new("RGB", (W * SS, H * SS), "white")
    dr = ImageDraw.Draw(img)

    col_x = (W - COL) / 2
    block_h = 29 + 26 + 20 + 20 + FIELD_H + 10 + 18 + 22 + BTN_H
    y = (H - block_h) / 2 - 24

    draw_logo(img, dr, W / 2, y + 14.5, PAC_SIZE)
    y += 29 + 26

    f_sub = font(BAL, 16)
    sub = "Welcome! Please enter your password to continue."
    b = ink(dr, f_sub, sub)
    dr.text(((W * SS - (b[2] - b[0])) / 2 - b[0], y * SS - b[1]), sub, font=f_sub, fill=TXT_SUB)
    y += 20 + 20

    # field, with the reveal toggle at the right
    dr.rounded_rectangle([col_x * SS, y * SS, (col_x + COL) * SS, (y + FIELD_H) * SS],
                         radius=RADIUS * SS, fill="white", outline=FIELD_BORDER, width=int(1.2 * SS))
    lock_icon(dr, (col_x + 15) * SS, (y + FIELD_H / 2 - 8) * SS, 16 * SS, ICON)
    f_ph = font(BAL, 14)
    ph = "Enter your password"
    pb = ink(dr, f_ph, ph)
    dr.text(((col_x + 42) * SS - pb[0], (y + FIELD_H / 2) * SS - (pb[3] - pb[1]) / 2 - pb[1]),
            ph, font=f_ph, fill=PLACEHOLDER)
    eye_icon(dr, (col_x + COL - 31) * SS, (y + FIELD_H / 2 - 8) * SS, 16 * SS, ICON)
    y += FIELD_H + 10

    # link row: error on the left (when shown), "Forgot password?" pinned right
    f_lk = font(BAL, 14)
    if error:
        et = "Incorrect password."
        eb = ink(dr, f_lk, et)
        dr.text((col_x * SS - eb[0], (y + 9) * SS - (eb[3] - eb[1]) / 2 - eb[1]),
                et, font=f_lk, fill=ERR_FG)
    lt = "Forgot password?"
    lb = ink(dr, f_lk, lt)
    dr.text(((col_x + COL) * SS - (lb[2] - lb[0]) - lb[0], (y + 9) * SS - (lb[3] - lb[1]) / 2 - lb[1]),
            lt, font=f_lk, fill=TXT_SUB)
    y += 18 + 22

    dr.rounded_rectangle([col_x * SS, y * SS, (col_x + COL) * SS, (y + BTN_H) * SS],
                         radius=RADIUS * SS, fill=BTN)
    f_b = font(JER, 17)
    bt = "CONTINUE"
    bb = ink(dr, f_b, bt)
    dr.text(((W * SS - (bb[2] - bb[0])) / 2 - bb[0], (y + BTN_H / 2) * SS - (bb[3] - bb[1]) / 2 - bb[1]),
            bt, font=f_b, fill="white")

    return img.resize((W, H), Image.LANCZOS)


def trash_icon(dr, x, y, s, colour):
    u = s / 16.0
    t = max(1, int(1.3 * u))
    dr.line([x + 2.9 * u, y + 4.5 * u, x + 13.1 * u, y + 4.5 * u], fill=colour, width=t)
    dr.line([x + 6.4 * u, y + 4.5 * u, x + 6.4 * u, y + 3.0 * u], fill=colour, width=t)
    dr.line([x + 6.4 * u, y + 3.0 * u, x + 9.6 * u, y + 3.0 * u], fill=colour, width=t)
    dr.line([x + 9.6 * u, y + 3.0 * u, x + 9.6 * u, y + 4.5 * u], fill=colour, width=t)
    dr.line([x + 4.3 * u, y + 4.5 * u, x + 4.3 * u, y + 13.6 * u], fill=colour, width=t)
    dr.line([x + 11.7 * u, y + 4.5 * u, x + 11.7 * u, y + 13.6 * u], fill=colour, width=t)
    dr.line([x + 4.3 * u, y + 13.6 * u, x + 11.7 * u, y + 13.6 * u], fill=colour, width=t)


def forgot_password():
    img = Image.new("RGB", (W * SS, H * SS), "white")
    dr = ImageDraw.Draw(img)
    col_x = (W - COL) / 2

    block_h = 29 + 26 + 26 + 28 + 20 + 10 + 88 + 32 + BTN_H + 12 + BTN_H
    y = (H - block_h) / 2 - 24

    draw_logo(img, dr, W / 2, y + 14.5, PAC_SIZE)
    y += 29 + 26

    f_t = font(BAL, 20)
    t = "Forgot your password?"
    b = ink(dr, f_t, t)
    dr.text(((W * SS - (b[2] - b[0])) / 2 - b[0], (y + 13) * SS - (b[3] - b[1]) / 2 - b[1]),
            t, font=f_t, fill=TXT_SUB)
    y += 26 + 28

    f_h = font(BALSB, 16)
    hb = ink(dr, f_h, "Reset all data")
    dr.text((col_x * SS - hb[0], (y + 10) * SS - (hb[3] - hb[1]) / 2 - hb[1]),
            "Reset all data", font=f_h, fill=DANGER_HEAD)
    y += 20 + 10

    f_b = font(BAL, 16)
    for i, line in enumerate([
            "Use this only if you truly forgot your password, and",
            "you want to erase all data.",
            "Resetting deletes all your saved data and cannot be",
            "undone."]):
        lb = ink(dr, f_b, line)
        dr.text((col_x * SS - lb[0], (y + 11 + i * 22) * SS - (lb[3] - lb[1]) / 2 - lb[1]),
                line, font=f_b, fill=DANGER_BODY)
    y += 88 + 32

    f_j = font(JER, 17)
    for label, fill, icon in (("RESET", DANGER, True), ("BACK TO LOCK SCREEN", BTN, False)):
        dr.rounded_rectangle([col_x * SS, y * SS, (col_x + COL) * SS, (y + BTN_H) * SS],
                             radius=RADIUS * SS, fill=fill)
        jb = ink(dr, f_j, label)
        tw = jb[2] - jb[0]
        group = tw + (16 + 9) * SS if icon else tw
        gx = (W * SS - group) / 2
        if icon:
            trash_icon(dr, gx, (y + BTN_H / 2 - 8) * SS, 16 * SS, (255, 255, 255))
            gx += (16 + 9) * SS
        dr.text((gx - jb[0], (y + BTN_H / 2) * SS - (jb[3] - jb[1]) / 2 - jb[1]),
                label, font=f_j, fill="white")
        y += BTN_H + 12

    return img.resize((W, H), Image.LANCZOS)


def splash():
    img = Image.new("RGB", (W * SS, H * SS), "white")
    dr = ImageDraw.Draw(img)
    draw_logo(img, dr, W / 2, H / 2, 90)
    return img.resize((W, H), Image.LANCZOS)


def create_password(success=False):
    img = Image.new("RGB", (W * SS, H * SS), "white")
    dr = ImageDraw.Draw(img)

    col_x = (W - COL) / 2
    block_h = 29 + 26 + 20 + 20 + FIELD_H + 12 + FIELD_H + 18 + WARN_H + 18 + BTN_H
    y = (H - block_h) / 2 - 24

    _, ring = draw_logo(img, dr, W / 2, y + 14.5, PAC_SIZE)
    y += 29 + 26

    f_sub = font(BAL, 16)
    sub = "Create a password for your DoNet account."
    b = ink(dr, f_sub, sub)
    dr.text(((W * SS - (b[2] - b[0])) / 2 - b[0], y * SS - b[1]), sub, font=f_sub, fill=TXT_SUB)
    y += 20 + 20

    f_ph = font(BAL, 14)
    for ph in ("Create password", "Retype password"):
        dr.rounded_rectangle([col_x * SS, y * SS, (col_x + COL) * SS, (y + FIELD_H) * SS],
                             radius=RADIUS * SS, fill="white", outline=FIELD_BORDER, width=int(1.2 * SS))
        lock_icon(dr, (col_x + 15) * SS, (y + FIELD_H / 2 - 8) * SS, 16 * SS, ICON)
        pb = ink(dr, f_ph, ph)
        dr.text(((col_x + 42) * SS - pb[0], (y + FIELD_H / 2) * SS - (pb[3] - pb[1]) / 2 - pb[1]),
                ph, font=f_ph, fill=PLACEHOLDER)
        y += FIELD_H + 12
    y += 18 - 12

    bg, bd, fg = (OK_BG, OK_BORDER, OK_FG) if success else (WARN_BG, WARN_BORDER, WARN_FG)
    dr.rounded_rectangle([col_x * SS, y * SS, (col_x + COL) * SS, (y + WARN_H) * SS],
                         radius=RADIUS * SS, fill=bg, outline=bd, width=int(1.2 * SS))
    if success:
        u = 15 / 16.0
        ix, iy = (col_x + 13) * SS, (y + WARN_H / 2 - 7.5) * SS
        dr.line([ix + 3.1 * u * SS, iy + 8.5 * u * SS, ix + 6.5 * u * SS, iy + 11.9 * u * SS,
                 ix + 12.9 * u * SS, iy + 4.4 * u * SS], fill=fg, width=int(1.6 * SS), joint="curve")
    else:
        warn_icon(dr, (col_x + 13) * SS, (y + WARN_H / 2 - 7.5) * SS, 15 * SS, WARN_FG)
    f_w = font(BAL, 12.5)
    wt = "Password created successfully." if success else "Do not forget your password, you will lose all your data."
    wb = ink(dr, f_w, wt)
    dr.text(((col_x + 35) * SS - wb[0], (y + WARN_H / 2) * SS - (wb[3] - wb[1]) / 2 - wb[1]),
            wt, font=f_w, fill=fg)
    y += WARN_H + 18

    dr.rounded_rectangle([col_x * SS, y * SS, (col_x + COL) * SS, (y + BTN_H) * SS],
                         radius=RADIUS * SS, fill=BTN)
    f_b = font(JER, 17)
    bt = "CREATE AND CONTINUE"
    bb = ink(dr, f_b, bt)
    dr.text(((W * SS - (bb[2] - bb[0])) / 2 - bb[0], (y + BTN_H / 2) * SS - (bb[3] - bb[1]) / 2 - bb[1]),
            bt, font=f_b, fill="white")

    return img.resize((W, H), Image.LANCZOS)



# --- Persons directory ------------------------------------------------------
#
# Mirrors Views/PersonsView.xaml. Every number here is the same number that is in
# the XAML; when one changes the other must, or this render starts certifying a
# layout the app does not actually have.

CARD_BORDER = (0xCB, 0xD5, 0xE1)
CARD_AVATAR = (0xF8, 0xFA, 0xFC)
LABEL_GREY = (0x7A, 0x87, 0x98)
META_GREY = (0x6B, 0x72, 0x80)
SEARCH_BORDER = (0xE6, 0xEB, 0xF0)
SEARCH_PH = (0x7B, 0x84, 0x91)
ADD_BORDER = (0xBB, 0xDF, 0xD8)
TEXT_PRIMARY = (0x2B, 0x2F, 0x33)


def tracked(dr, xy, text, f, fill, em_per_1000):
    """Draw text with XAML CharacterSpacing, which PIL has no concept of."""
    x, y = xy
    extra = (em_per_1000 / 1000.0) * f.size
    for ch in text:
        dr.text((x, y), ch, font=f, fill=fill)
        x += dr.textlength(ch, font=f) + extra
    return x - xy[0]


def tracked_w(dr, text, f, em_per_1000):
    extra = (em_per_1000 / 1000.0) * f.size
    return sum(dr.textlength(c, font=f) for c in text) + extra * max(0, len(text) - 1)


def persons_content(img, dr, box, state="loaded", narrow=False):
    """The Persons screen inside the content plate. box is the plate in CSS px."""
    x0, y0, x1, y1 = [v * SS for v in box]

    def rr(b, radius, fill, outline=None, w=0):
        dr.rounded_rectangle(b, radius=radius * SS, fill=fill, outline=outline,
                             width=int(w * SS))

    pad = (12 if narrow else 32) * SS
    cx0, cy0, cx1 = x0 + pad, y0 + pad, x1 - pad
    cy1 = y1 - pad

    # Header -----------------------------------------------------------------
    f_title = font(BAU, 34)
    f_sub = font(BAL, 14)
    tracked(dr, (cx0, cy0 + (40 - 34) / 2 * SS - 6 * SS), "PERSONS", f_title, BTN, 20)
    dr.text((cx0, cy0 + 40 * SS - 2 * SS), "Preview directory \u00b7 Static placeholder values only",
            font=f_sub, fill=META_GREY)

    # search + add, right aligned on wide, full width underneath on narrow
    f_ph = font(BAL, 14)
    f_btn = font(JER, 17)
    add_label_w = tracked_w(dr, "ADD PERSON", f_btn, 40)
    add_w = (14 + 18 + 10) * SS + add_label_w + 18 * SS
    H = 42 * SS

    if narrow:
        ay = cy0 + 60 * SS + 14 * SS
        search_x0, search_x1 = cx0, cx1 - add_w - 12 * SS
        header_h = 60 * SS + 14 * SS + H + 16 * SS
    else:
        ay = cy0 + (60 * SS - H) / 2
        search_w = 304 * SS
        search_x1 = cx1 - add_w - 12 * SS
        search_x0 = search_x1 - search_w
        header_h = 60 * SS + 24 * SS

    rr([search_x0, ay, search_x1, ay + H], 21, (255, 255, 255), SEARCH_BORDER, 1)
    # magnifier: circle r5.5 at (8,8) in an 18 box, handle to (16,16)
    mx, my = search_x0 + 16 * SS, ay + H / 2
    r = 5.5 * SS
    dr.ellipse([mx - r, my - r, mx + r, my + r], outline=SEARCH_PH,
               width=max(1, round(1.8 * SS)))
    dr.line([mx + 4.2 * SS, my + 4.2 * SS, mx + 8 * SS, my + 8 * SS],
            fill=SEARCH_PH, width=max(1, round(1.8 * SS)))
    dr.text((search_x0 + 16 * SS + 18 * SS + 12 * SS, my - 10 * SS),
            "SEARCH DIRECTORY", font=f_ph, fill=SEARCH_PH)

    bx0 = cx1 - add_w
    rr([bx0, ay, cx1, ay + H], 10, (255, 255, 255), ADD_BORDER, 1)
    # the supplied 18x18 icon: teal rounded square, white round-capped cross
    ix, iy = bx0 + 14 * SS, ay + (H - 18 * SS) / 2
    rr([ix, iy, ix + 18 * SS, iy + 18 * SS], 6, BTN)
    w2 = max(1, round(2 * SS))
    dr.line([ix + 3.75 * SS, iy + 9 * SS, ix + 14.25 * SS, iy + 9 * SS],
            fill=(255, 255, 255), width=w2)
    dr.line([ix + 9 * SS, iy + 3.75 * SS, ix + 9 * SS, iy + 14.25 * SS],
            fill=(255, 255, 255), width=w2)
    tracked(dr, (ix + 18 * SS + 10 * SS, ay + H / 2 - 12 * SS), "ADD PERSON", f_btn, BTN, 40)

    # Card surface ------------------------------------------------------------
    sy0 = cy0 + header_h
    rr([cx0, sy0, cx1, cy1], 18 if narrow else 24, (255, 255, 255))

    spad = (12 if narrow else 24) * SS
    ix0, iy0, ix1 = cx0 + spad, sy0 + spad, cx1 - spad

    if state != "loaded":
        _state_block(dr, state, (ix0 + ix1) / 2, (sy0 + cy1) / 2)
        return

    # UniformGridLayout: cols = floor((avail + gap) / (min + gap)), at least 1
    gap = 20 * SS
    avail = ix1 - ix0
    cols = max(1, int((avail + gap) // (430 * SS + gap)))
    cw = (avail - gap * (cols - 1)) / cols
    ch = 268 * SS

    for i in range(4):
        r_, c_ = divmod(i, cols)
        _card(dr, ix0 + c_ * (cw + gap), iy0 + r_ * (ch + gap), cw, ch)


def _card(dr, x, y, w, h):
    def rr(b, radius, fill, outline=None, wd=0):
        dr.rounded_rectangle(b, radius=radius * SS, fill=fill, outline=outline,
                             width=int(wd * SS))

    rr([x, y, x + w, y + h], 16, (255, 255, 255), CARD_BORDER, 1)
    p = 20 * SS
    ax, ay = x + p, y + p

    # identity row: 56 avatar, 16 gap, name block, "Preview" hard right
    dr.ellipse([ax, ay, ax + 56 * SS, ay + 56 * SS], fill=CARD_AVATAR,
               outline=SEARCH_BORDER, width=max(1, round(1 * SS)))
    f_dash = font(BAL, 15)
    dw = dr.textlength("\u2014", font=f_dash)
    dr.text((ax + 28 * SS - dw / 2, ay + 28 * SS - 13 * SS), "\u2014",
            font=f_dash, fill=LABEL_GREY)

    tx = ax + 56 * SS + 16 * SS
    f_name, f_cap = font(BALSB, 16), font(BAL, 14)
    dr.text((tx, ay + 28 * SS - 23 * SS), "Person record", font=f_name, fill=TEXT_PRIMARY)
    dr.text((tx, ay + 28 * SS + 1 * SS), "Record preview", font=f_cap, fill=META_GREY)

    f_prev = font(BAL, 13)
    pw = dr.textlength("Preview", font=f_prev)
    dr.text((x + w - p - pw, ay + 28 * SS - 11 * SS), "Preview", font=f_prev, fill=META_GREY)

    # 3x3 field grid
    gy = ay + 56 * SS + 22 * SS
    gw = w - p * 2
    colgap = 12 * SS
    colw = (gw - colgap * 2) / 3
    f_lab, f_val = font(BAL, 11), font(BAL, 14)
    labels = [("#", "FIRST NAME", "LAST NAME"),
              ("GENDER", "DATE OF BIRTH", "COUNTRY"),
              ("EMAIL", "CREATED AT", "NOTE")]
    for r_, row in enumerate(labels):
        ry = gy + r_ * (38 + 18) * SS
        for c_, lab in enumerate(row):
            lx = ax + c_ * (colw + colgap)
            tracked(dr, (lx, ry - 2 * SS), lab, f_lab, LABEL_GREY, 60)
            dr.text((lx, ry + 14 * SS + 4 * SS - 4 * SS), "\u2014", font=f_val, fill=TEXT_PRIMARY)


def _state_block(dr, state, cx, cy):
    """Loading / empty / no-match / error, centred on the card surface."""
    f_t, f_b = font(BALSB, 16), font(BAL, 14)
    copy = {
        "loading": ("Opening the directory", "Just a moment."),
        "empty": ("No persons yet", "Records you add will appear here."),
        "nomatch": ("Nothing matches that search",
                    "Try a shorter term, or clear the search to see every record."),
        "error": ("The directory could not be opened",
                  "Nothing was changed. You can try again."),
    }[state]
    icon_c = ERR_FG if state == "error" else LABEL_GREY

    top = cy - 52 * SS
    if state == "loading":
        r = 18 * SS
        dr.arc([cx - r, top - r + 18 * SS, cx + r, top + r + 18 * SS],
               -60, 190, fill=BTN, width=max(1, round(3 * SS)))
    elif state == "error":
        s_ = 22 * SS
        dr.line([cx, top, cx + s_, top + 38 * SS], fill=icon_c, width=max(1, round(1.6 * SS)))
        dr.line([cx + s_, top + 38 * SS, cx - s_, top + 38 * SS], fill=icon_c,
                width=max(1, round(1.6 * SS)))
        dr.line([cx - s_, top + 38 * SS, cx, top], fill=icon_c, width=max(1, round(1.6 * SS)))
        dr.line([cx, top + 14 * SS, cx, top + 25 * SS], fill=icon_c, width=max(1, round(1.6 * SS)))
        dr.ellipse([cx - 1 * SS, top + 30 * SS, cx + 1 * SS, top + 32 * SS], fill=icon_c)
    else:
        r = 17 * SS
        dr.ellipse([cx - r, top + 2 * SS, cx + r, top + 2 * SS + 2 * r], outline=icon_c,
                   width=max(1, round(1.6 * SS)))

    ty = top + 56 * SS
    for txt, f, col in ((copy[0], f_t, TEXT_PRIMARY), (copy[1], f_b, META_GREY)):
        tw = dr.textlength(txt, font=f)
        dr.text((cx - tw / 2, ty), txt, font=f, fill=col)
        ty += 26 * SS

    if state == "error":
        f_btn = font(JER, 17)
        lw = tracked_w(dr, "TRY AGAIN", f_btn, 40)
        bw, bh = lw + 44 * SS, 40 * SS
        by = ty + 14 * SS
        dr.rounded_rectangle([cx - bw / 2, by, cx + bw / 2, by + bh],
                             radius=10 * SS, fill=BTN)
        tracked(dr, (cx - lw / 2, by + bh / 2 - 12 * SS), "TRY AGAIN", f_btn,
                (255, 255, 255), 40)


def home(LW=1440, LH=900, content="persons", state="loaded"):
    """The home shell. Icons come from gen_nav_icons, which holds the design's SVG."""
    import sys
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from gen_nav_icons import ICONS, RING, flatten

    TITLEBAR, LOGO_W = 48, 80
    PAD, RAIL_W, GAP = 12, 64, 20
    RAIL_R, CARD_R = RAIL_W / 2, 34
    DISC, SPACING, RING_D = 48, 16, 28
    TOP_PAD, RING_GAP, BOTTOM_PAD = 16, 32, 16
    TOP = TITLEBAR + 6          # the shell clears the title bar, with 6 beneath it

    SURFACE = (245, 245, 245)
    TEAL = (75, 170, 152)
    TEAL_LIGHT = (140, 215, 197)
    AMBER = (245, 158, 11)

    img = Image.new("RGB", (LW * SS, LH * SS), (255, 255, 255))
    dr = ImageDraw.Draw(img)

    def rr(box, radius, fill, outline=None, w=0):
        dr.rounded_rectangle([v * SS for v in box], radius=radius * SS, fill=fill,
                             outline=outline, width=int(w * SS))

    rr((PAD, TOP, PAD + RAIL_W, LH - PAD), RAIL_R, SURFACE)
    rr((PAD + RAIL_W + GAP, TOP, LW - PAD, LH - PAD), CARD_R, SURFACE)

    # Title bar. The lockup is left-aligned at the same 14px inset the caption dots
    # use on the right; draw_logo centres, so its own width measurement is mirrored
    # here to turn a left edge into a centre.
    pac = LOGO_W * 0.6788 / 3.083
    _b = ink(dr, font(PAC, pac), "DoNet")
    _tw = _b[2] - _b[0]
    _total = (_tw + _tw * RING_RATIO + _tw * GAP_RATIO) / SS
    draw_logo(img, dr, 14 + _total / 2, TITLEBAR / 2, pac)

    # 12px dots on a 20px pitch, 14px from the right edge: red outermost.
    for i, col in enumerate(((0xFF, 0x5F, 0x57), (0xFE, 0xBC, 0x2E), (0x28, 0xC8, 0x40))):
        dcx, dcy = LW - 20 - i * 20, TITLEBAR / 2
        dr.ellipse([(dcx - 6) * SS, (dcy - 6) * SS, (dcx + 6) * SS, (dcy + 6) * SS], fill=col)

    cx = PAD + RAIL_W / 2

    # logo ring, gradient left to right across its box
    rx, ry = cx - RING_D / 2, TOP + TOP_PAD
    for i in range(RING_D * SS):
        t = i / (RING_D * SS - 1)
        col = tuple(round(a + (b - a) * t) for a, b in zip(TEAL, TEAL_LIGHT))
        dr.line([((rx * SS) + i, ry * SS), ((rx * SS) + i, (ry + RING_D) * SS - 1)], fill=col)
    mask = Image.new("L", (RING_D * SS, RING_D * SS), 0)
    md = ImageDraw.Draw(mask)
    md.ellipse([0, 0, RING_D * SS - 1, RING_D * SS - 1], fill=255)
    md.ellipse([RING_D / 4 * SS, RING_D / 4 * SS,
                RING_D * 3 / 4 * SS - 1, RING_D * 3 / 4 * SS - 1], fill=0)
    ring = img.crop((int(rx * SS), int(ry * SS),
                     int(rx * SS) + RING_D * SS, int(ry * SS) + RING_D * SS))
    base = Image.new("RGB", ring.size, SURFACE)
    base.paste(ring, (0, 0), mask)
    img.paste(base, (int(rx * SS), int(ry * SS)))

    def disc(cy, key, selected, accent):
        r = DISC / 2
        dr.ellipse([(cx - r) * SS, (cy - r) * SS, (cx + r) * SS, (cy + r) * SS],
                   fill=accent if selected else (255, 255, 255))
        colour = (255, 255, 255) if selected else TEAL
        spec = ICONS[key]
        k = 24 / 28.0          # the Viewbox in NavRailButton, strokes included
        for sub in spec["paths"]:
            for pts, closed in flatten(sub):
                p = [((cx + (px - 14) * k) * SS, (cy + (py - 14) * k) * SS) for px, py in pts]
                if closed:
                    p.append(p[0])
                dr.line(p, fill=colour, width=max(1, round(spec["w"] * k * SS)), joint="curve")

    top = TOP + TOP_PAD + RING_D + RING_GAP
    discs = [(top + DISC / 2 + i * (DISC + SPACING), key, i == 1, TEAL)
             for i, key in enumerate(("Services", "Persons", "Sites"))]
    discs.append((LH - PAD - BOTTOM_PAD - DISC / 2, "Lock", True, AMBER))

    # The disc shadows, as a true Gaussian: every disc offset down 6, blurred at
    # sigma 7 (Figma blur 14), then composited at 7.06%. This render previously drew
    # no disc shadow at all, which is exactly why a grossly over-strong one in the
    # XAML survived review here and only showed up in a real build.
    sh = Image.new("L", img.size, 0)
    sd = ImageDraw.Draw(sh)
    for cy, *_rest in discs:
        r = DISC / 2
        sd.ellipse([(cx - r) * SS, (cy + 6 - r) * SS,
                    (cx + r) * SS, (cy + 6 + r) * SS], fill=255)
    sh = sh.filter(ImageFilter.GaussianBlur(7 * SS)).point(lambda v: round(v * 0.0706))
    img.paste(Image.new("RGB", img.size, (0x04, 0x15, 0x16)), (0, 0), sh)

    for _args in discs:
        disc(*_args)

    if content == "persons":
        persons_content(img, dr, (PAD + RAIL_W + GAP, TOP, LW - PAD, LH - PAD),
                        state=state, narrow=LW < 820)

    return img.resize((LW, LH), Image.LANCZOS)


if __name__ == "__main__":
    os.makedirs("/tmp/preview", exist_ok=True)
    splash().save("/tmp/preview/screen1_splash.png")
    create_password().save("/tmp/preview/screen2_create.png")
    create_password(success=True).save("/tmp/preview/screen2_success.png")
    lock_screen().save("/tmp/preview/screen3_lock.png")
    lock_screen(error=True).save("/tmp/preview/screen3_lock_error.png")
    forgot_password().save("/tmp/preview/screen4_forgot.png")
    home().save("/tmp/preview/screen5_home.png")
    print("written")
