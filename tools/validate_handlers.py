#!/usr/bin/env python3
"""Check that every handler named by name actually exists.

Three places in this app refer to a method purely by its name, with nothing
nearby to prove the name is real:

  1. DependencyProperty.Register(..., new PropertyMetadata(x, OnSomethingChanged))
  2. XAML event attributes - Click="OnSave", PointerExited="OnPointerExited"
  3. event subscriptions - thing.Changed += OnChanged;

A typo in any of them is a plain compile error, but only once somebody compiles,
and there is no .NET SDK in this workspace. This script is the stand-in.

Exists because FieldCell registered its Options property against OnAnyChanged
when the method is called OnAnyPropertyChanged, and nothing noticed until the
app was built on a real machine.
"""

from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
APP = ROOT / "DoNet"

IDENT = r"[A-Za-z_][A-Za-z0-9_]*"

# A method declaration, with or without modifiers, generic or not.
METHOD = re.compile(
    r"^\s*(?:(?:public|private|protected|internal|static|async|partial|override|"
    r"virtual|sealed|new|extern|unsafe)\s+)*"
    r"(?:[\w<>,\.\[\]\?]+)\s+"
    rf"({IDENT})\s*(?:<[^>()]*>)?\s*\(",
    re.MULTILINE,
)

# The second argument of PropertyMetadata, when it is a bare name rather than a
# lambda or null.
METADATA = re.compile(rf"PropertyMetadata\s*\([^()]*?,\s*({IDENT})\s*\)")

# thing.Event += Handler;  /  -= Handler;
SUBSCRIPTION = re.compile(rf"(?:\+=|-=)\s*({IDENT})\s*;")

# Attribute names that carry a handler rather than a value. WinUI has no way to
# tell them apart from markup alone, so this is a list rather than a rule.
EVENT_SUFFIXES = (
    "Changed",
    "Click",
    "Tapped",
    "Pressed",
    "Released",
    "Entered",
    "Exited",
    "Submitted",
    "Chosen",
    "Opened",
    "Closed",
    "Closing",
    "Completed",
    "Invoked",
    "Prepared",
    "Clearing",
    "Toggled",
    "Activated",
    "Deactivated",
)
EVENT_NAMES = {
    "Loaded",
    "Unloaded",
    "Loading",
    "KeyDown",
    "KeyUp",
    "PreviewKeyDown",
    "PreviewKeyUp",
    "GotFocus",
    "LostFocus",
    "Holding",
    "PointerMoved",
    "PointerCanceled",
    "PointerCaptureLost",
    "PointerWheelChanged",
    "DragOver",
    "DragEnter",
    "DragLeave",
    "Drop",
    "ItemClick",
    "ContextRequested",
    "LayoutUpdated",
    "ProcessKeyboardAccelerators",
}
# Attributes that end in a handler suffix but are ordinary properties.
NOT_EVENTS = {
    "IsTapEnabled",
    "IsDoubleTapEnabled",
    "IsRightTapEnabled",
    "IsHoldingEnabled",
    "AllowDrop",
}

ATTRIBUTE = re.compile(rf'\s({IDENT})\s*=\s*"([^"]*)"')

# Handlers a code-behind inherits rather than declares.
INHERITED: dict[str, set[str]] = {}


def is_event_attribute(name: str) -> bool:
    if name in NOT_EVENTS:
        return False
    return name in EVENT_NAMES or name.endswith(EVENT_SUFFIXES)


def methods_in(text: str) -> set[str]:
    """Every method name declared in a file, plus the names of its lambdas' owners."""
    found = {m.group(1) for m in METHOD.finditer(text)}
    # Expression-bodied and block-bodied both match above; drop keywords that
    # the loose pattern can pick up at a statement start.
    return found - {"if", "while", "for", "foreach", "switch", "catch", "lock", "using", "return", "fixed"}


def base_types(text: str, class_name: str) -> list[str]:
    decl = re.search(
        rf"\bclass\s+{re.escape(class_name)}\b\s*(?::\s*([^{{]+))?",
        text,
    )
    if not decl or not decl.group(1):
        return []
    return [p.strip().split("<")[0] for p in decl.group(1).split(",")]


def collect_inherited(cs_files: list[pathlib.Path]) -> None:
    """Map each type to the methods it can see through its base types."""
    declared: dict[str, set[str]] = {}
    bases: dict[str, list[str]] = {}
    for path in cs_files:
        text = path.read_text(encoding="utf-8")
        for match in re.finditer(rf"\bclass\s+({IDENT})", text):
            name = match.group(1)
            declared.setdefault(name, set()).update(methods_in(text))
            bases.setdefault(name, []).extend(base_types(text, name))

    def walk(name: str, seen: set[str]) -> set[str]:
        if name in seen:
            return set()
        seen.add(name)
        out = set(declared.get(name, set()))
        for parent in bases.get(name, []):
            out |= walk(parent, seen)
        return out

    for name in declared:
        INHERITED[name] = walk(name, set())


def main() -> int:
    cs_files = sorted(APP.rglob("*.cs"))
    cs_files = [p for p in cs_files if "obj" not in p.parts and "bin" not in p.parts]
    collect_inherited(cs_files)

    problems: list[str] = []
    checked = 0

    for path in cs_files:
        text = path.read_text(encoding="utf-8")
        available = methods_in(text)
        for match in re.finditer(rf"\bclass\s+({IDENT})", text):
            available |= INHERITED.get(match.group(1), set())

        for pattern, what in ((METADATA, "PropertyMetadata callback"), (SUBSCRIPTION, "event subscription")):
            for match in pattern.finditer(text):
                name = match.group(1)
                if name in {"null", "value", "true", "false"}:
                    continue
                checked += 1
                if name not in available:
                    line = text.count("\n", 0, match.start()) + 1
                    problems.append(
                        f"{path.relative_to(ROOT)}:{line}: {what} '{name}' is not a method in this file"
                    )

    for path in sorted(APP.rglob("*.xaml")):
        if "obj" in path.parts or "bin" in path.parts:
            continue
        code_behind = path.with_suffix(".xaml.cs")
        if not code_behind.exists():
            continue
        text = path.read_text(encoding="utf-8")
        behind = code_behind.read_text(encoding="utf-8")
        available = methods_in(behind)
        for match in re.finditer(rf"\bclass\s+({IDENT})", behind):
            available |= INHERITED.get(match.group(1), set())

        for match in ATTRIBUTE.finditer(text):
            name, value = match.group(1), match.group(2)
            if not is_event_attribute(name):
                continue
            if not re.fullmatch(IDENT, value):
                continue  # {x:Bind ...}, a number, an enum member
            checked += 1
            if value not in available:
                line = text.count("\n", 0, match.start()) + 1
                problems.append(
                    f"{path.relative_to(ROOT)}:{line}: {name} handler '{value}' "
                    f"is not a method in {code_behind.name}"
                )

    if problems:
        print(f"{len(problems)} unresolved handler(s):\n")
        for problem in problems:
            print(f"  {problem}")
        return 1

    print(f"All {checked} handler references resolve.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
