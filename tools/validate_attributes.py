#!/usr/bin/env python3
"""
Checks that every attribute sits on a declaration kind it is valid on.

The failure this catches: [NotMapped] stamped on a helper method by reflex
when marking a model's computed members - NotMappedAttribute is valid on
classes, properties, indexers and fields, and a method is none of those, so
it is a compile error (CS0592), not a hint. The compiler reports it one file
at a time and there is no SDK here, so the valid targets are listed by hand
for the attributes this codebase actually uses, in the same spirit as the
framework-type map in validate_usings.py.

Attribute-to-declaration pairs that exist elsewhere in the framework are not
this tool's business; only the attributes below are checked, and an unknown
attribute is left alone.

Run from the repository root:   python3 tools/validate_attributes.py
"""
import glob
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
CS = sorted(glob.glob(os.path.join(ROOT, "DoNet", "**", "*.cs"), recursive=True))

# The attributes this app uses, by the declaration kinds each is valid on.
# "field" stands for plain fields; ObservableProperty and its Notify* companions
# are field-only, RelayCommand is method-only, NotMapped is everything but
# methods (EF never maps a method in the first place).
ATTRIBUTE_TARGETS = {
    "NotMapped": {"class", "property", "field"},
    "ObservableProperty": {"field"},
    "NotifyPropertyChangedFor": {"field"},
    "NotifyCanExecuteChangedFor": {"field"},
    "RelayCommand": {"method"},
}

TYPE_DECLARATION = re.compile(r"\b(?:class|struct|interface|enum|record)\s+[A-Z]")


def bracket_end(stripped: str) -> int:
    """Where the leading bracket group ends, at paren depth zero, or -1."""
    depth = 0
    for i, ch in enumerate(stripped):
        if ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
        elif ch == "]" and depth == 0:
            return i
    return -1


def attribute_names(line: str) -> list[str]:
    """The attributes a bracket line carries, or empty for anything else.

    Handles stacked single attributes ([Name]) and comma lists ([A, B(...)]),
    strips the arguments ([NotifyPropertyChangedFor(nameof(X))] yields
    NotifyPropertyChangedFor), and leaves attribute-and-declaration-on-one-line
    forms to the caller by returning only what the first bracket group holds.
    """
    stripped = line.strip()
    if not stripped.startswith("["):
        return []

    end = bracket_end(stripped)
    if end == -1:
        return []

    inner = stripped[1:end]
    parts = []
    part = ""
    depth = 0
    for ch in inner:
        if ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
        if ch == "," and depth == 0:
            parts.append(part)
            part = ""
        else:
            part += ch
    parts.append(part)

    found = []
    for name in parts:
        match = re.match(r"\s*(?:return\s*:)?\s*([A-Za-z_]\w*)", name)
        if match and match.group(1) in ATTRIBUTE_TARGETS:
            found.append(match.group(1))
    return found


def classify(declaration: str) -> str | None:
    """Which declaration kind a header line opens, or None when uninteresting."""
    if TYPE_DECLARATION.search(declaration):
        return "class"
    if declaration.strip().startswith("using ") or declaration.strip().startswith("["):
        return None

    # A method's parameter list opens before any assignment or body; a field
    # with a constructor in its initializer opens the paren after the '='.
    equal = re.search(r"=>|[＝=]", declaration)
    body = declaration.find("{")
    paren = declaration.find("(")
    limits = [p for p in (equal.start() if equal else -1, body) if p != -1]
    first_stop = min(limits) if limits else len(declaration)

    if paren != -1 and paren < first_stop:
        return "method"
    if body != -1 or "=>" in declaration:
        return "property"
    if declaration.rstrip().endswith(";"):
        return "field"
    return None


def check(path, pending, kind, problems):
    """Reports every held attribute that is not valid on this declaration kind."""
    for line_number, name in pending:
        if kind not in ATTRIBUTE_TARGETS[name]:
            allowed = ", ".join(sorted(ATTRIBUTE_TARGETS[name]))
            rel = os.path.relpath(path, ROOT)
            problems.append(
                f"{rel}:{line_number}: [{name}] is not valid on a {kind}; "
                f"it is valid on {allowed} declarations only")


def main() -> int:
    problems: list[str] = []
    checked = 0

    for path in CS:
        with open(path, encoding="utf-8-sig") as handle:
            lines = handle.readlines()

        pending: list[tuple[int, str]] = []  # (line number, attribute name)
        for number, line in enumerate(lines, start=1):
            stripped = line.strip()
            if not stripped or stripped.startswith("//") or stripped.startswith("*"):
                # A blank or comment does not end an attribute's run; the
                # declaration is the next line of code.
                continue

            # Braces and parens on their own are structure, not declarations;
            # they end nothing but must not be classified as one.
            if re.fullmatch(r"[{}()\[\];,]*", stripped):
                pending.clear()
                continue

            if stripped.startswith("["):
                end = bracket_end(stripped)
                if end != -1:
                    # This codebase mixes two styles: the attribute on its own
                    # line above the declaration, and attribute and declaration
                    # on one line. Both have to work, so the names are held and
                    # whatever follows the bracket group - on this line or the
                    # next code line - is the declaration they belong to.
                    pending.extend(
                        (number, name)
                        for name in attribute_names(stripped))
                    rest = stripped[end + 1:].strip()
                    if rest:
                        kind = classify(rest)
                        if kind is not None:
                            check(path, pending, kind, problems)
                            checked += len(pending)
                            pending.clear()
                    continue

            kind = classify(stripped)
            if kind is None:
                # using directives and stray brackets clear the run; anything
                # else unclassifiable is not something to hold attributes over.
                pending.clear()
                continue

            check(path, pending, kind, problems)
            checked += len(pending)
            pending.clear()

    if problems:
        print("Attributes on declaration kinds they are not valid on:\n")
        print("\n".join(f"  - {p}" for p in problems))
        return 1

    print(f"OK - {checked} attribute placement(s) checked, all valid")
    return 0


if __name__ == "__main__":
    sys.exit(main())
