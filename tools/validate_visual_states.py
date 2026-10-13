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

# A named Grid and everything inside it, so a cell can be matched to the grid
# that places it.
GRID_OPEN = re.compile(r'<Grid x:Name="(\w+)"[^>]*>')
CELL_NAME = re.compile(r'x:Name="(\w+)"')


def grid_row_targets(text: str):
    """
    Yields (grid, cell, target row) for every Setter that moves a cell into a
    Grid row, and (grid, rows available) for every named Grid.
    """
    # element spans: a Grid's body runs to the next </Grid> at its own depth -
    # approximation by first close is safe here because the named grids in this
    # app do not nest another named Grid inside.
    spans = []
    for m in GRID_OPEN.finditer(text):
        close = text.find("</Grid>", m.end())
        body = text[m.end():close]
        rows = body.count("<RowDefinition ")
        names = set(CELL_NAME.findall(body)) - {m.group(1)}
        spans.append((m.group(1), rows, names))

    for element, value in re.findall(
            r'Target="(\w+)\.\(Grid\.Row\)" Value="(\d+)"', text):
        for grid, rows, names in spans:
            if element in names:
                yield grid, element, int(value), rows
                break


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

        # A cell moved into a row its grid does not have is the shape of a bug
        # that waited for a narrow window to appear: exists because the VPS
        # preview shipped with its grids sized for the wide layout only, and
        # every field from Memory down vanished the first time the window went
        # under the adaptive trigger. A Grid renders a child in a nonexistent
        # row as nothing at all - no error, no log, just gone.
        for grid, cell, target, rows in grid_row_targets(text):
            checked += 1
            if target >= rows:
                problems.append(
                    f"{path.relative_to(root.parent)}: "
                    f'Setter moves {cell} to row {target}, but {grid} has '
                    f"{rows} row(s)"
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
