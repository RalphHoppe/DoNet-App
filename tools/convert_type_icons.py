#!/usr/bin/env python3
"""
Converts the supplied v2 type icons into the app's geometry source.

Source: screenshots/v2_screenshots_and_stuff/icons_svgs.txt - five SVGs from the
design: the four primary service marks (VPS, Proxy, Domain, Wallets) and the
thin left chevron the rail's back button shows. Output: DoNet/Controls/
TypeMarkData.cs, consumed by TypeMark and the rail, in the same shape as the
line-icon sheet's generated class.

Two conversions matter:

* Number normalisation. The SVGs use scientific notation (1.57503e-07), which
  the XAML path mini-language does not accept; values that small are rounding
  noise and become 0. Everything else is re-emitted plain, commands made
  explicit (no implicit repeats), rounded to 4 decimals.

* Fill rules. SVG defaults to nonzero winding and marks its exceptions with
  fill-rule="evenodd"; XAML's mini-language defaults to even-odd. Every
  path is therefore emitted with an explicit F0/F1 prefix so both engines
  agree, per path - which is why the output is a list of strings per icon
  rather than one: a single Data carries one fill rule for all its subpaths,
  and the wallet mixes rules between its paths.

The chevron is re-centred onto the rail's 28x28 icon grid (NavRailButton draws
its glyphs there), translated from its native 12x23 box.

Run from the repository root:   python3 tools/convert_type_icons.py
"""
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent.parent
SOURCE = ROOT / "screenshots" / "v2_screenshots_and_stuff" / "icons_svgs.txt"
OUTPUT = ROOT / "DoNet" / "Controls" / "TypeMarkData.cs"

NUMBER = r"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?"

# (command, numbers per coordinate group, numbers are x,y pairs / x / y)
ARITY = {"M": 2, "L": 2, "H": 1, "V": 1, "C": 6, "S": 4, "Q": 4, "T": 2, "A": 7, "Z": 0}


def number(value: str) -> float:
    out = float(value)
    return 0.0 if abs(out) < 5e-5 else out


def fmt(value: float) -> str:
    out = round(value, 4)
    if out == int(out):
        return str(int(out))
    return str(out)


def parse(d: str):
    """The d attribute as (command, [floats]) steps, implicit repeats expanded."""
    tokens = re.findall(rf"([MmLlHhVvCcSsQqTtAaZz])|({NUMBER})", d)
    steps, i = [], 0
    while i < len(tokens):
        letter, value = tokens[i]
        if letter:
            command, i = letter, i + 1
        else:
            # A number where a command is expected: an implicit repeat.
            command = steps[-1][0] if steps else "L"
        nums = []
        while i < len(tokens) and not tokens[i][0]:
            nums.append(number(tokens[i][1]))
            i += 1
        arity = ARITY[command.upper()]
        if arity == 0:
            steps.append((command, []))
            continue
        for j in range(0, len(nums), arity):
            chunk = nums[j:j + arity]
            if len(chunk) < arity:
                break
            # Implicit M repeats are L in SVG, in either case.
            step = command if j == 0 else ("L" if command == "M" else "l" if command == "m" else command)
            steps.append((step, chunk))
    return steps


def bbox(steps):
    """The path's extent, for verification against the SVG's viewBox."""
    x = y = 0.0
    xs, ys = [0.0], [0.0]
    for command, nums in steps:
        up = command.upper()
        rel = command.islower()
        if up == "M" or up == "L":
            for j in range(0, len(nums), 2):
                x = nums[j] + (x if rel else 0)
                y = nums[j + 1] + (y if rel else 0)
                xs.append(x); ys.append(y)
        elif up == "H":
            for v in nums:
                x = v + (x if rel else 0); xs.append(x)
        elif up == "V":
            for v in nums:
                y = v + (y if rel else 0); ys.append(y)
        elif up in ("C", "S", "Q", "T"):
            for j in range(0, len(nums), 2):
                xs.append(nums[j] + (x if rel else 0))
                ys.append(nums[j + 1] + (y if rel else 0))
            x = nums[-2] + (x if rel else 0); y = nums[-1] + (y if rel else 0)
        elif up == "A":
            x = nums[-2] + (x if rel else 0); y = nums[-1] + (y if rel else 0)
            xs.append(x); ys.append(y)
    return min(xs), min(ys), max(xs), max(ys)


def emit(steps, dx=0.0, dy=0.0):
    """Steps as mini-language text, translated by (dx, dy) for absolute moves."""
    parts = []
    for command, nums in steps:
        up = command.upper()
        rel = command.islower()
        out = []
        if up in ("M", "L", "C", "S", "Q", "T"):
            for j in range(0, len(nums), 2):
                x, y = nums[j], nums[j + 1]
                if not rel:
                    x, y = x + dx, y + dy
                out += [fmt(x), fmt(y)]
        elif up == "H":
            for v in nums:
                out.append(fmt(v + (dx if not rel else 0)))
        elif up == "V":
            for v in nums:
                out.append(fmt(v + (dy if not rel else 0)))
        elif up == "A":
            out += [fmt(v) for v in nums[:-2]] + [fmt(nums[-2]), fmt(nums[-1])]
        parts.append(f"{command} {' '.join(out)}".strip())
    return " ".join(parts)


def main():
    text = SOURCE.read_text(encoding="utf-8")
    svgs = re.findall(r"<svg\b[^>]*>(.*?)</svg>", text, re.S)
    if len(svgs) != 5:
        raise SystemExit(f"expected 5 SVGs, found {len(svgs)}")

    names = ["Vps", "Proxy", "Domain", "Wallets", "Chevron"]
    boxes = [(30, 28), (32, 28), (32, 32), (32, 32), (12, 23)]
    fields, checks = [], []

    for name, svg, (vw, vh) in zip(names, svgs, boxes):
        width, height = svg, svg  # per-path below
        paths = []
        for tag in re.finditer(r"<path\b([^>]*)/?>", svg):
            attrs = tag.group(1)
            d = re.search(r'\bd="([^"]+)"', attrs)
            if not d:
                continue
            steps = parse(d.group(1))
            rule = "F0" if 'fill-rule="evenodd"' in attrs or 'clip-rule="evenodd"' in attrs else "F1"
            if name == "Chevron":
                # Re-centre onto the rail's 28-grid.
                dx, dy = (28 - vw) / 2, (28 - vh) / 2
                paths.append(f"{rule} {emit(steps, dx, dy)}")
            else:
                paths.append(f"{rule} {emit(steps)}")

            x0, y0, x1, y1 = bbox(steps)
            checks.append((name, round(x0, 1), round(y0, 1), round(x1, 1), round(y1, 1), vw, vh))

        if name == "Chevron":
            fields.append(f"    /// <summary>The rail back button's chevron, on the 28 grid.</summary>\n"
                          f"    internal const string Chevron = \"{paths[0]}\";")
        else:
            literal = ",\n        ".join(f"\"{p}\"" for p in paths)
            fields.append(
                f"    /// <summary>The {name} mark, one entry per SVG path.</summary>\n"
                f"    internal static readonly string[] {name} =\n    [\n        {literal},\n    ];")

    for name, x0, y0, x1, y1, vw, vh in checks:
        if name == "Chevron":
            continue
        pad = 1.5
        if x0 < -pad or y0 < -pad or x1 > vw + pad or y1 > vh + pad:
            raise SystemExit(f"{name}: extent ({x0},{y0})..({x1},{y1}) outside viewBox {vw}x{vh}")
        print(f"{name:8s} extent ({x0},{y0})..({x1},{y1}) in {vw}x{vh} - ok")

    body = "\n\n".join(fields)
    OUTPUT.write_text(f'''// <auto-generated>
//     Generated by tools/convert_type_icons.py from
//     screenshots/v2_screenshots_and_stuff/icons_svgs.txt.
//     Re-run the script after changing the source; do not edit this file by hand.
// </auto-generated>

// The auto-generated marker above switches the nullable context off, so it has
// to be switched back on explicitly.
#nullable enable

namespace DoNet.Controls;

/// <summary>
/// Path data for the v2 service type marks - the four gradient icons and the
/// rail back button's chevron.
/// </summary>
/// <remarks>
/// The marks are filled shapes, not stroked ones like the line-icon sheet: each
/// is a list of paths because the source SVGs mix fill rules between the paths
/// of one icon, and a single Data string carries one rule for all of its
/// subpaths. Every entry keeps its native viewBox, which TypeMark scales. The
/// chevron is the exception: it is re-centred onto the rail's 28 grid so
/// NavRailButton can draw it where it draws every glyph.
/// </remarks>
internal static class TypeMarkData
{{
{body}
}}
''', encoding="utf-8")
    print(f"{OUTPUT.relative_to(ROOT)} written, {len(fields)} fields")


if __name__ == "__main__":
    main()
