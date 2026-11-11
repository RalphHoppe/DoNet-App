#!/usr/bin/env python3
"""
Checks that every XML documentation comment in the project is well formed.

The failure this exists to catch: a '///' prefix dropped from the middle of a
<remarks> block. The compiler then reads English prose as C#, and one missing
prefix produces dozens of unrelated-looking syntax errors in a file that looks
fine at a glance.

A contiguous run of '///' lines must parse as XML on its own. If <remarks>
opens in one run and </remarks> closes in another, something non-doc got
between them - which is precisely the bug.
"""
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

DOC = re.compile(r'^\s*///(.*)$')


def runs(lines):
    """Yield (start_line_number, [content...]) for each contiguous /// block."""
    current, start = [], 0
    for number, line in enumerate(lines, 1):
        match = DOC.match(line)
        if match:
            if not current:
                start = number
            current.append(match.group(1))
        elif current:
            yield start, current
            current = []
    if current:
        yield start, current


def main():
    root = pathlib.Path(__file__).resolve().parent.parent / "DoNet"
    problems = []
    checked = 0

    for path in sorted(root.rglob("*.cs")):
        if "obj" in path.parts or "bin" in path.parts:
            continue
        lines = path.read_text(encoding="utf-8").splitlines()
        for start, content in runs(lines):
            checked += 1
            body = "\n".join(content)
            try:
                # &, < and > appear in prose and in cref text; the wrapper only
                # needs the tags to balance, so escape bare ampersands.
                ET.fromstring("<doc>" + re.sub(r'&(?![a-zA-Z#]+;)', '&amp;', body) + "</doc>")
            except ET.ParseError as error:
                rel = path.relative_to(root.parent)
                problems.append(f"  {rel}:{start}  malformed doc comment - {error}")

    if problems:
        print("Malformed XML documentation comments:\n")
        print("\n".join(problems))
        print(f"\n{len(problems)} problem(s) in {checked} doc comment blocks")
        return 1

    print(f"OK - {checked} documentation comment blocks, all well formed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
