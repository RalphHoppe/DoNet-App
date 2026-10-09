#!/usr/bin/env python3
"""Check every VisualState Setter Target against the names declared in the same file.

A Setter whose Target names an element that does not exist is invisible until the
state actually activates - which for an AdaptiveTrigger means it waits for somebody
to resize the window, and then throws. That is the worst possible shape for a bug,
so it is worth a check that costs nothing.

Exists because WebsiteDialog.xaml was cloned from PersonDialog.xaml and kept five
targets that only ever existed in the original.
"""

import pathlib
import re
import sys

NAME = re.compile(r'x:Name="([^"]+)"')
TARGET = re.compile(r'Target="([^".]+)\.([^"]+)"')
STORYBOARD_TARGET = re.compile(r'Storyboard\.TargetName="([^"]+)"')


def main() -> int:
    root = pathlib.Path(__file__).resolve().parent.parent / "DoNet"
    problems = []
    checked = 0

    for path in sorted(root.rglob("*.xaml")):
        text = path.read_text(encoding="utf-8")
        declared = set(NAME.findall(text))

        for element, prop in TARGET.findall(text):
            checked += 1
            if element not in declared:
                problems.append(
                    f"{path.relative_to(root.parent)}: "
                    f'Setter Target="{element}.{prop}" - no element named {element}'
                )

        for element in STORYBOARD_TARGET.findall(text):
            checked += 1
            if element not in declared:
                problems.append(
                    f"{path.relative_to(root.parent)}: "
                    f'Storyboard.TargetName="{element}" - no element of that name'
                )

    if problems:
        print(f"FAIL - {len(problems)} unresolvable target(s):")
        for problem in problems:
            print(f"  {problem}")
        return 1

    print(f"OK - {checked} visual-state and storyboard targets, all resolve")
    return 0


if __name__ == "__main__":
    sys.exit(main())
