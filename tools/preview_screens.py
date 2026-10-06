#!/usr/bin/env python3
"""
Renders a pixel reconstruction of the two DoNet screens using the real fonts, so the
layout numbers can be checked against the Figma export before they are written into XAML.

This is a design aid only - it is not part of the app build.
"""
import math
import os
from PIL import Image, ImageDraw, ImageFont

_FD = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                   "DoNet", "Assets", "Fonts")
PAC = os.path.join(_FD, "Pacifico-Regular.ttf")
BAL = os.path.join(_FD, "Baloo2-Regular.ttf")
BALSB = os.path.join(_FD, "Baloo2-SemiBold.ttf")
JER = os.path.join(_FD, "Jersey25-Regular.ttf")

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


if __name__ == "__main__":
    os.makedirs("/tmp/preview", exist_ok=True)
    splash().save("/tmp/preview/screen1_splash.png")
    create_password().save("/tmp/preview/screen2_create.png")
    create_password(success=True).save("/tmp/preview/screen2_success.png")
    lock_screen().save("/tmp/preview/screen3_lock.png")
    lock_screen(error=True).save("/tmp/preview/screen3_lock_error.png")
    forgot_password().save("/tmp/preview/screen4_forgot.png")
    print("written")
