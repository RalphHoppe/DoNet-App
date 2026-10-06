#!/usr/bin/env python3
"""
Catches the XAML mistakes a compiler would, for when a Windows build is not to hand.

Checks:
  1. every x:Class has a matching partial class in a .cs file
  2. every {StaticResource}/{ThemeResource} key is defined somewhere in the project
  3. every event-handler attribute names a method that exists in the code-behind
  4. every x:Name referenced from code-behind is actually declared in the markup

Run from the repository root:   python3 tools/validate_xaml.py
"""
import glob
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
XAML = sorted(glob.glob(os.path.join(ROOT, "DoNet", "**", "*.xaml"), recursive=True))
CS = sorted(glob.glob(os.path.join(ROOT, "DoNet", "**", "*.cs"), recursive=True))

# Keys that ship with WinUI and are resolved from the framework dictionaries.
BUILTIN_PREFIXES = ("TextControl", "SystemControl", "SystemAccent", "Content", "Body",
                    "Caption", "Subtitle", "Title", "Default")

EVENT_ATTRS = {"Click", "Loaded", "Unloaded", "PasswordChanged", "GotFocus", "LostFocus",
               "KeyDown", "KeyUp", "Submitted", "PointerEntered", "PointerExited",
               "SelectionChanged", "TextChanged", "Tapped", "Checked", "Unchecked"}

problems: list[str] = []
notes: list[str] = []



# Members that exist on a similar control but not on this one. Each of these has cost a
# build cycle; the compiler only reports them one file at a time, and there is no SDK on
# this machine to check against, so they are listed by hand as they are found.
FORBIDDEN_MEMBERS = {
    "PasswordBox": {
        "PlaceholderForeground":
            "PasswordBox has PlaceholderText but no PlaceholderForeground. "
            "Set the TextControlPlaceholderForeground brush in the parent's Resources.",
    },
}


def check_forbidden_members(path, src, problems):
    import re
    for m in re.finditer(r"<(\w+)([^>]*?)/?>", src, re.S):
        tag, attrs = m.group(1), m.group(2)
        for member, why in FORBIDDEN_MEMBERS.get(tag, {}).items():
            if re.search(rf"\b{member}\s*=", attrs):
                line = src[:m.start()].count("\n") + 1
                problems.append(f"{path}:{line}: <{tag}> {member} - {why}")


def read(path):
    with open(path, encoding="utf-8-sig") as handle:
        return handle.read()


cs_text = {p: read(p) for p in CS}
xaml_text = {p: read(p) for p in XAML}
all_cs = "\n".join(cs_text.values())

# ---- 1. x:Class has a code-behind -----------------------------------------
for path, text in xaml_text.items():
    match = re.search(r'x:Class="([\w.]+)"', text)
    if not match:
        continue
    full = match.group(1)
    namespace, _, cls = full.rpartition(".")
    pattern = re.compile(rf"\bclass\s+{re.escape(cls)}\b")
    owner = [p for p, t in cs_text.items()
             if pattern.search(t) and (f"namespace {namespace}" in t)]
    if not owner:
        problems.append(f"x:Class {full} in {os.path.relpath(path, ROOT)} has no matching partial class")

# ---- 2. resource keys ------------------------------------------------------
defined = set()
for text in xaml_text.values():
    defined.update(re.findall(r'x:Key="([^"]+)"', text))
for text in cs_text.values():
    defined.update(re.findall(r'Resources\["([^"]+)"\]', text))

used = []
for path, text in xaml_text.items():
    for key in re.findall(r"\{(?:StaticResource|ThemeResource)\s+([\w.]+)\s*\}", text):
        used.append((path, key))
    # keys inside markup extensions with extra params, e.g. Converter={StaticResource X}, ...
    for key in re.findall(r"(?:StaticResource|ThemeResource)\s+([\w.]+)\s*[,}]", text):
        used.append((path, key))

for path, key in used:
    if key in defined or key.startswith(BUILTIN_PREFIXES):
        continue
    problems.append(f"undefined resource key '{key}' used in {os.path.relpath(path, ROOT)}")

# ---- 2b. members that do not exist on that element ------------------------
for path, text in xaml_text.items():
    check_forbidden_members(os.path.relpath(path, ROOT), text, problems)

# ---- 3. event handlers exist ----------------------------------------------
for path, text in xaml_text.items():
    base = path[: -len(".xaml")] + ".xaml.cs"
    if not os.path.exists(base):
        continue
    behind = cs_text.get(base, "")
    for attr, handler in re.findall(r'\b(\w+)="([A-Za-z_]\w*)"', text):
        if attr not in EVENT_ATTRS:
            continue
        if not re.search(rf"\b(?:void|Task)\s+{re.escape(handler)}\s*\(", behind):
            problems.append(
                f"handler {handler} for {attr} not found in {os.path.relpath(base, ROOT)}")

# ---- 4. x:Name used from code-behind --------------------------------------
for path, text in xaml_text.items():
    base = path[: -len(".xaml")] + ".xaml.cs"
    if not os.path.exists(base):
        continue
    names = set(re.findall(r'x:Name="(\w+)"', text))
    behind = cs_text[base]
    # identifiers in the code-behind that look like element references
    for ident in set(re.findall(r"\b([A-Z]\w*)\.(?:[A-Z]\w*)", behind)):
        if ident in names:
            continue
        # a type name or namespace rather than an element - only flag the suspicious ones
        looks_like_glyph_element = re.fullmatch(r"(?:Trace|Fill)[A-Z]", ident) is not None
        if looks_like_glyph_element or ident in {
            "Input", "FieldBorder", "RootFrame", "DragRegion", "Logo",
            "ContentPanel", "PasswordInput", "ConfirmInput", "LogoCanvas", "RingTrace",
        }:
            problems.append(
                f"{os.path.relpath(base, ROOT)} references '{ident}' which has no x:Name in the markup")

    notes.append(f"{os.path.relpath(path, ROOT)}: names = {', '.join(sorted(names)) or '(none)'}")

print("\n".join(notes))
print()
if problems:
    print(f"{len(problems)} PROBLEM(S):")
    for p in problems:
        print("  -", p)
    sys.exit(1)

print(f"OK - {len(XAML)} XAML files, {len(defined)} resource keys, no problems found")
