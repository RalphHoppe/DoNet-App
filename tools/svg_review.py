#!/usr/bin/env python3
"""
svg_review.py - complete, deterministic reading of an SVG.

An SVG is code, so unlike a screenshot it can be read exactly. This tool
walks the whole element tree with the full transform chain, resolves the
paint cascade (inline style, <style> element rules, presentation
attributes, inheritance, CSS custom properties), resolves <use>
references and gradients, and prints an inventory of every drawable:
what it is, where it sits (absolute user units and percent of the view
box), how big it is, and what it is painted with. It also rasterises the
file with resvg and runs the eyes.py block map and census over the
render, so geometry and pixels can be cross-checked.

Usage:
  python3 tools/svg_review.py FILE.svg [--width 1200] [--no-render]
"""

import argparse
import io
import os
import re
import sys

import numpy as np
from svgpathtools.parser import parse_path, parse_transform
from svgpathtools.path import transform as apply_transform
import resvg_py
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from eyes import block_map, census  # noqa: E402

SVG_NS = "{http://www.w3.org/2000/svg}"
XLINK_NS = "{http://www.w3.org/1999/xlink}"
SHAPES = {"path", "rect", "circle", "ellipse", "line", "polyline", "polygon"}
PAINT_KEYS = ("fill", "stroke", "stroke-width", "fill-opacity",
              "stroke-opacity", "opacity", "font-family", "font-size",
              "font-weight", "stop-color")


def local(tag):
    return tag[len(SVG_NS):] if tag.startswith(SVG_NS) else tag


# --------------------------------------------------------------------------
# CSS support: <style> blocks, inline style, custom properties
# --------------------------------------------------------------------------

def parse_rules(css):
    """Tiny CSS parser: selector { prop: value; ... } comma lists only."""
    css = re.sub(r"/\*.*?\*/", "", css or "", flags=re.S)
    rules = []
    for m in re.finditer(r"([^{}]+)\{([^}]*)\}", css):
        body = {}
        for decl in m.group(2).split(";"):
            if ":" in decl:
                k, v = decl.split(":", 1)
                body[k.strip()] = v.strip()
        for sel in m.group(1).split(","):
            sel = sel.strip()
            if sel and body:
                ids = sel.count("#")
                classes = sel.count(".")
                rules.append(((ids, classes), sel, body))
    rules.sort(key=lambda r: r[0])
    return rules


def selector_matches(sel, el, root):
    sel = re.sub(r"::?[\w-]+(\([^)]*\))?", "", sel).strip()  # drop pseudos
    if not sel or sel == "*":
        return True
    if sel == ":root" or sel.strip() == "root":
        return el is root
    parts = re.findall(r"(?:^|(?![\w-]))([#.]?[\w-]+)", sel)
    tag = ident = None
    classes = []
    for p in parts:
        if p.startswith("#"):
            ident = p[1:]
        elif p.startswith("."):
            classes.append(p[1:])
        else:
            tag = p
    if tag and tag != "svg" and local(el.tag) != tag:
        return False
    if tag == "svg" and local(el.tag) != "svg":
        return False
    if ident is not None and el.get("id") != ident:
        return False
    have = (el.get("class") or "").split()
    return all(c in have for c in classes)


def resolve_var(value, variables):
    """Substitute var(--name, fallback) references."""
    for _ in range(8):
        m = re.search(r"var\(\s*(--[\w-]+)\s*(?:,\s*([^()]*))?\)", value or "")
        if not m:
            return value
        name, fallback = m.group(1), (m.group(2) or "").strip()
        sub = variables.get(name, fallback or "none")
        value = value[:m.start()] + sub + value[m.end():]
    return value


def style_dict(el):
    out = {}
    for decl in (el.get("style") or "").split(";"):
        if ":" in decl:
            k, v = decl.split(":", 1)
            out[k.strip()] = v.strip()
    return out


def paint_of(el, key, state, css_rules, root):
    """The full cascade for one presentation property."""
    inline = style_dict(el)
    if key in inline:
        return resolve_var(inline[key], state["--vars"])
    for _, sel, body in reversed(css_rules):
        if key in body and selector_matches(sel, el, root):
            return resolve_var(body[key], state["--vars"])
    if el.get(key) is not None:
        return resolve_var(el.get(key), state["--vars"])
    return state.get(key)


# --------------------------------------------------------------------------
# geometry
# --------------------------------------------------------------------------

def shape_to_d(el):
    tag = local(el.tag)
    if tag == "path":
        return el.get("d", "")
    if tag == "rect":
        x, y = float(el.get("x", 0)), float(el.get("y", 0))
        w, h = float(el.get("width", 0)), float(el.get("height", 0))
        return f"M {x},{y} h {w} v {h} h {-w} Z"
    if tag == "circle":
        cx, cy, r = (float(el.get(k, 0)) for k in ("cx", "cy", "r"))
        return (f"M {cx - r},{cy} a {r},{r} 0 1 0 {2*r},0 "
                f"a {r},{r} 0 1 0 {-2*r},0 Z")
    if tag == "ellipse":
        cx, cy = float(el.get("cx", 0)), float(el.get("cy", 0))
        rx, ry = float(el.get("rx", 0)), float(el.get("ry", 0))
        return (f"M {cx - rx},{cy} a {rx},{ry} 0 1 0 {2*rx},0 "
                f"a {rx},{ry} 0 1 0 {-2*rx},0 Z")
    if tag == "line":
        x1, y1 = float(el.get("x1", 0)), float(el.get("y1", 0))
        x2, y2 = float(el.get("x2", 0)), float(el.get("y2", 0))
        return f"M {x1},{y1} L {x2},{y2}"
    pts = nums_of(el.get("points", ""))
    if not pts:
        return ""
    d = f"M {pts[0]},{pts[1]}"
    for i in range(2, len(pts) - 1, 2):
        d += f" L {pts[i]},{pts[i+1]}"
    return d + (" Z" if tag == "polygon" else "")


def nums_of(s):
    return [float(v) for v in re.split(r"[\s,]+", s.strip()) if v]


def parse_color(v, gradients):
    if v is None or v == "none" or v == "":
        return None
    v = v.strip()
    if v == "currentColor":
        return "currentColor"
    if v.startswith("url("):
        gid = v[v.find("#") + 1:v.rfind(")")] if "#" in v else v[4:v.rfind(")")]
        g = gradients.get(gid)
        if g:
            stops = " ".join(f"{c}@{o:.0%}" for o, c in g[1])
            return f"gradient '{gid}'({g[0]}) [{stops}]"
        return f"gradient '{gid}' (unresolved)"
    return v


# --------------------------------------------------------------------------
# tree walk
# --------------------------------------------------------------------------

def walk(el, ctm, state, rows, gradients, css_rules, root, by_id):
    tag = local(el.tag)
    if tag in ("defs", "linearGradient", "radialGradient", "clipPath",
               "mask", "style", "title", "desc", "metadata", "script"):
        return

    m = ctm
    if el.get("transform"):
        m = ctm @ parse_transform(el.get("transform"))

    child_state = dict(state)
    # custom properties defined by any matching rule cascade downwards
    for _, sel, body in css_rules:
        if selector_matches(sel, el, root):
            for k, v in body.items():
                if k.startswith("--"):
                    child_state["--vars"][k] = v

    if tag == "use":
        href = el.get(XLINK_NS + "href") or el.get("href") or ""
        target = by_id.get(href.lstrip("#"))
        if target is not None:
            x, y = float(el.get("x", 0) or 0), float(el.get("y", 0) or 0)
            inner = m @ np.array([[1, 0, x], [0, 1, y], [0, 0, 1]], dtype=float)
            rows.append((0, 0, el.get("id") or "", "use", None,
                         f"inlines {href}", ""))
            walk(target, inner, child_state, rows, gradients, css_rules,
                 root, by_id)
        else:
            rows.append((0, 0, el.get("id") or "", "use", None,
                         f"UNRESOLVED {href}", ""))
        return

    ident = el.get("id") or el.get("class") or ""
    if tag in SHAPES:
        d = shape_to_d(el)
        try:
            p = apply_transform(parse_path(d), m)
            xmin, xmax, ymin, ymax = p.bbox()
            bbox = (xmin, xmax, ymin, ymax)
        except Exception:
            rows.append((0, 0, ident, tag, None, "unparseable path data", ""))
            return
        fill = parse_color(paint_of(el, "fill", child_state, css_rules, root),
                           gradients)
        stroke = parse_color(paint_of(el, "stroke", child_state, css_rules,
                                      root), gradients)
        sw = paint_of(el, "stroke-width", child_state, css_rules, root)
        fo = alpha_mult(1.0, paint_of(el, "fill-opacity", child_state,
                                      css_rules, root),
                        paint_of(el, "opacity", child_state, css_rules, root))
        so = alpha_mult(1.0, paint_of(el, "stroke-opacity", child_state,
                                      css_rules, root),
                        paint_of(el, "opacity", child_state, css_rules, root))
        paint = []
        if fill:
            paint.append(f"fill {fill}" + (f" @{fo:.0%}" if fo < 0.999 else ""))
        if stroke:
            paint.append(f"stroke {stroke} w={sw}"
                         + (f" @{so:.0%}" if so < 0.999 else ""))
        rows.append((ymin, xmin, ident, tag, bbox,
                     " ".join(paint) or "no paint", ""))
        return

    if tag == "text":
        content = "".join(el.itertext()).strip()
        rows.append((float(el.get("y", 0)), float(el.get("x", 0)), ident,
                     "text", None,
                     f"'{content}' font {paint_of(el, 'font-family', child_state, css_rules, root)} "
                     f"{paint_of(el, 'font-size', child_state, css_rules, root)} "
                     f"{paint_of(el, 'font-weight', child_state, css_rules, root)} "
                     f"fill {paint_of(el, 'fill', child_state, css_rules, root)}",
                     ""))
        return

    # container (svg, g, a, switch...): inherit resolved paints downwards
    for key in PAINT_KEYS:
        v = paint_of(el, key, child_state, css_rules, root)
        if v is not None:
            child_state[key] = v

    for child in el:
        walk(child, m, child_state, rows, gradients, css_rules, root, by_id)


def alpha_mult(base, *factors):
    a = base
    for f in factors:
        if f is None:
            continue
        try:
            a *= float(f)
        except (ValueError, TypeError):
            pass
    return a


def collect_gradients(root):
    gradients = {}
    for el in root.iter():
        tag = local(el.tag)
        if tag not in ("linearGradient", "radialGradient"):
            continue
        gid = el.get("id", "?")
        stops = []
        for s in el:
            if local(s.tag) == "stop":
                off = s.get("offset", "0")
                o = float(off.rstrip("%")) / 100 if off.endswith("%") else float(off)
                stops.append((o, s.get("stop-color", "?")))
        gtype = "linear" if tag == "linearGradient" else "radial"
        coords = " ".join(f"{k}={el.get(k)}" for k in
                          ("x1", "y1", "x2", "y2", "cx", "cy", "r", "fx", "fy")
                          if el.get(k))
        gradients[gid] = (f"{gtype} {coords}", stops)
    return gradients


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("svg")
    ap.add_argument("--width", type=int, default=1200)
    ap.add_argument("--no-render", action="store_true")
    args = ap.parse_args()

    import xml.etree.ElementTree as ET
    tree = ET.parse(args.svg)
    root = tree.getroot()

    vb = root.get("viewBox")
    if vb:
        vbx, vby, vbw, vbh = nums_of(vb)
    else:
        vbx = vby = 0
        vbw = float(root.get("width", "1200").rstrip("px"))
        vbh = float(root.get("height", "800").rstrip("px"))

    print(f"==== {args.svg} ====")
    print(f"viewBox: {vbx:g} {vby:g} {vbw:g} {vbh:g}  (aspect {vbw/vbh:.3f})")

    gradients = collect_gradients(root)
    print(f"\n== GRADIENTS  ({len(gradients)}) ==")
    for gid, (desc, stops) in gradients.items():
        print(f"  '{gid}' {desc}: " + " ".join(f"{c}@{o:.0%}" for o, c in stops))

    css_rules = []
    for el in root.iter():
        if local(el.tag) == "style" and el.text:
            css_rules += parse_rules(el.text)
    print(f"\n== CSS  {len(css_rules)} rules ==")
    for _, sel, body in css_rules:
        print(f"  {sel} {{ {', '.join(f'{k}: {v}' for k, v in body.items())} }}")

    by_id = {el.get("id"): el for el in root.iter() if el.get("id")}
    rows = []
    walk(root, np.eye(3), {"--vars": {}}, rows, gradients, css_rules, root,
         by_id)

    print(f"\n== INVENTORY  {len(rows)} drawables, top-to-bottom, left-to-right ==")
    for ymin, xmin, ident, tag, bbox, desc, _ in sorted(
            rows, key=lambda r: (r[0], r[1])):
        where = ""
        if bbox:
            x1, x2, y1, y2 = bbox
            where = (f"({x1:7.1f},{y1:7.1f})-({x2:7.1f},{y2:7.1f})  "
                     f"{x2-x1:6.1f}x{y2-y1:<6.1f}  "
                     f"[{100*(x2-x1)/vbw:4.1f}%w {100*(y2-y1)/vbh:4.1f}%h]")
        label = tag + (f"#{ident}" if ident else "")
        print(f"  {label:24s} {where:66s} {desc}")

    sized = [r for r in rows if r[4]]
    print("\n== BY AREA ==")
    for ymin, xmin, ident, tag, bbox, desc, _ in sorted(
            sized, key=lambda r: -(r[4][1]-r[4][0]) * (r[4][3]-r[4][2]))[:12]:
        x1, x2, y1, y2 = bbox
        print(f"  {tag:8s} {(x2-x1)*(y2-y1):10.0f}u^2  {x2-x1:6.1f}x{y2-y1:<6.1f}"
              f"  {desc[:80]}")

    if args.no_render:
        return
    png = resvg_py.svg_to_bytes(svg_path=args.svg, width=args.width,
                                background="#FFFFFF")
    im = Image.open(io.BytesIO(png)).convert("RGB")
    out = os.path.splitext(args.svg)[0] + ".review.png"
    im.save(out)
    print(f"\n== RENDER  resvg {im.size[0]}px wide -> {out} ==")
    block_map(im, min(96, im.size[0]))
    census(im)


if __name__ == "__main__":
    sys.exit(main())
