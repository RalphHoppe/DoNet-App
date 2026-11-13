#!/usr/bin/env python3
"""
The checks a build would catch, for machines without a Windows build to hand.

  1. every XAML file is well-formed XML
  2. every x:Class has a matching partial class in its code-behind
  3. every event handler named in XAML exists in the code-behind
  4. every x:Bind ViewModel.<member> resolves against the real view model
  5. every C# file's braces balance, and generated files say so
  6. every framework type a file mentions has its namespace imported
  7. no XAML that WinUI's compiler would refuse: string figures, non-rect clips
  8. no positional cancellation token in the serialiser's async calls

Run from the repository root:   python3 tools/validate.py
"""
import glob
import os
import re
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
APP = os.path.join(ROOT, "EasyNet")

problems = []

XAML = sorted(glob.glob(os.path.join(APP, "**", "*.xaml"), recursive=True))
CS = sorted(glob.glob(os.path.join(APP, "**", "*.cs"), recursive=True))
cs_text = {p: open(p, encoding="utf-8-sig").read() for p in CS}

# ---- 1. well-formed XML -----------------------------------------------------
for path in XAML:
    try:
        ET.parse(path)
    except ET.ParseError as error:
        problems.append(f"{os.path.relpath(path, ROOT)}: not well-formed XML ({error})")

# ---- 2. x:Class has a code-behind ------------------------------------------
for path in XAML:
    src = open(path, encoding="utf-8-sig").read()
    match = re.search(r'x:Class="([\w.]+)"', src)
    if not match:
        continue
    namespace, _, cls = match.group(1).rpartition(".")
    behind = path + ".cs"
    if not os.path.exists(behind):
        problems.append(f"{os.path.relpath(path, ROOT)}: no code-behind at {os.path.relpath(behind, ROOT)}")
        continue
    body = cs_text.get(behind, "")
    if not re.search(rf"\bpartial class {re.escape(cls)}\b", body):
        problems.append(f"{os.path.relpath(behind, ROOT)}: no 'partial class {cls}' for the XAML's x:Class")
    if f"namespace {namespace}" not in body:
        problems.append(f"{os.path.relpath(behind, ROOT)}: no 'namespace {namespace}' for the XAML's x:Class")

# ---- 3. event handlers exist ------------------------------------------------
EVENT_ATTRS = {"Click", "Loaded", "Unloaded", "PasswordChanged", "GotFocus", "LostFocus",
               "KeyDown", "KeyUp", "SelectionChanged", "TextChanged", "Tapped",
               "PointerEntered", "PointerExited", "SizeChanged", "Checked", "Unchecked"}
for path in XAML:
    src = open(path, encoding="utf-8-sig").read()
    behind = cs_text.get(path + ".cs", "")
    for attr, handler in re.findall(r'\b(\w+)="([A-Za-z_]\w*)"', src):
        if attr not in EVENT_ATTRS:
            continue
        if not re.search(rf"\b(?:void|Task) {re.escape(handler)}\s*\(", behind):
            problems.append(
                f"{os.path.relpath(path, ROOT)}: handler '{handler}' for {attr} "
                f"not found in {os.path.relpath(path + '.cs', ROOT)}")

# ---- 4. x:Bind ViewModel.<member> resolves -----------------------------------
def members_of(vm_type):
    for body in cs_text.values():
        if re.search(rf"\bclass {re.escape(vm_type)}\b", body):
            members = set(re.findall(r"\b(?:internal|public)\s+(?:static\s+)?(?:readonly\s+)?[\w<>,\[\]?. ]+?\s(\w+)\s*(?:\{|=>|;)", body))
            for m in re.finditer(r"\[ObservableProperty\][\s\S]{0,200}?private\s+[\w<>,\[\]?]+\s+_(\w+)\s*[;=]", body):
                name = m.group(1)
                members.add(name[0].upper() + name[1:])
            for m in re.finditer(r"\[RelayCommand\][\s\S]{0,300}?\b(?:private\s+)?(?:async\s+)?(?:Task|void)\s+(\w+)\s*\(", body):
                name = m.group(1)
                if name.endswith("Async"):
                    name = name[:-5]
                members.add(name + "Command")
            return members
    return None

for path in XAML:
    src = open(path, encoding="utf-8-sig").read()
    behind = cs_text.get(path + ".cs", "")
    decl = re.search(r"public\s+(\w+ViewModel)\s+ViewModel\b", behind)
    if not decl:
        continue
    members = members_of(decl.group(1))
    if not members:
        problems.append(f"{os.path.relpath(path, ROOT)}: view model {decl.group(1)} not found for bind checks")
        continue
    for m in re.finditer(r"x:Bind\s+ViewModel\.(\w+)", src):
        if m.group(1) not in members:
            problems.append(
                f"{os.path.relpath(path, ROOT)}: x:Bind ViewModel.{m.group(1)} - "
                f"{decl.group(1)} has no such member")

# ---- 5. braces balance, and generated files say so ---------------------------
for path, body in cs_text.items():
    if body.count("{") != body.count("}"):
        problems.append(f"{os.path.relpath(path, ROOT)}: braces do not balance "
                        f"({body.count('{')} open, {body.count('}')} close)")
    if os.path.basename(path).endswith(".geometry.cs") and "<auto-generated>" not in body:
        problems.append(f"{os.path.relpath(path, ROOT)}: a generated file that does not say so")

# ---- 6. framework types have their namespaces imported ------------------------
# Written after a round where three pages said ': Page' with no
# Microsoft.UI.Xaml.Controls among their usings - one build cycle each. The
# map is hand-kept, listing the types this codebase actually reaches for.
NAMESPACES = {
    "Microsoft.UI.Xaml": [
        "Application", "DependencyObject", "DependencyProperty", "Duration",
        "EasingMode", "FocusState", "FrameworkElement", "PropertyChangedCallback",
        "PropertyMetadata", "RoutedEventArgs", "UIElement", "Visibility",
        "VisualStateManager", "XamlRoot",
    ],
    "Microsoft.UI.Xaml.Controls": [
        "Button", "Canvas", "ContentPresenter", "ControlTemplate", "Frame",
        "Page", "PasswordBox", "PasswordRevealMode", "Primitives", "StackPanel",
        "UserControl", "VisualState", "VisualStateGroup",
    ],
    "Microsoft.UI.Xaml.Controls.Primitives": [],
    "Microsoft.UI.Xaml.Input": [
        "FocusState", "KeyRoutedEventArgs", "VirtualKey",
    ],
    "Microsoft.UI.Xaml.Media": [
        "Brush", "LinearGradientBrush", "RadialGradientBrush", "ScaleTransform",
        "SolidColorBrush", "TranslateTransform",
    ],
    "Microsoft.UI.Xaml.Media.Animation": [
        "BackEase", "CubicEase", "DoubleAnimation", "DoubleAnimationUsingKeyFrames",
        "EasingFunctionBase", "KeySpline", "KeyTime", "QuinticEase",
        "RepeatBehavior", "SplineDoubleKeyFrame", "Storyboard",
        "Timeline",
    ],
    "Microsoft.UI.Xaml.Navigation": [
        "NavigationEventArgs",
    ],
    "Windows.Foundation": [
        "Point", "Rect",
    ],
    "Windows.Graphics": [
        "RectInt32",
    ],
    "Windows.UI.ViewManagement": [
        "UISettings",
    ],
}
TYPE_TO_NS = {t: ns for ns, types in NAMESPACES.items() for t in types}

for path, body in cs_text.items():
    if "<auto-generated>" in body:
        continue
    usings = set(re.findall(r'^using ([\w.]+);', body, re.M))
    # 'FocusState' lives in both Xaml and Input; either import satisfies it.
    for match in re.finditer(r'(?<![\w.])([A-Z]\w*)\b', body):
        token = match.group(1)
        ns = TYPE_TO_NS.get(token)
        if ns is None:
            continue
        needed = NAMESPACES[ns]
        if not any(candidate in usings for candidate in ([ns] + [
                other for other, types in NAMESPACES.items() if token in types])):
            problems.append(
                f"{os.path.relpath(path, ROOT)}: mentions '{token}' "
                f"but imports none of the namespaces that carry it")

# ---- 7. XAML the compiler would refuse ----------------------------------------
# Written the day a clipped canvas shipped: UIElement.Clip is RectangleGeometry
# only, and PathGeometry.Figures takes no path-string - both WMC errors that
# only a Windows build reports. Check 1 proves the files parse; this proves
# they would compile.
for path in XAML:
    src = open(path, encoding="utf-8-sig").read()
    rel = os.path.relpath(path, ROOT)
    for m in re.finditer(r'Figures="([^"]*)"', src):
        if m.group(1).lstrip().startswith(("M", "m", "F", "f")):
            problems.append(f"{rel}: PathGeometry.Figures cannot take a path string "
                            f"(WMC0055) - use Path.Data, or element-syntax figures")
    for m in re.finditer(r'\.Clip>\s*<(\w+)', src):
        if m.group(1) != "RectangleGeometry":
            problems.append(f"{rel}: Clip must be a RectangleGeometry, not {m.group(1)} "
                            f"(WMC0015) - UIElement.Clip accepts nothing else")

    # Properties that exist in SVG or WPF but not on WinUI shapes. Each of
    # these has cost a build cycle; the compiler reports them one at a time.
    for m in re.finditer(r'\s(StrokeLineCap|StrokeLinecap|StrokeOpacity|FillRule)=', src):
        problems.append(f"{rel}:{src[: m.start()].count(chr(10)) + 1}: '{m.group(1)}' is not a "
                        f"WinUI shape property - SVG/WPF naming that the "
                        f"compiler refuses (use Start/End caps, colour alpha, "
                        f"or the path's own F0/F1 rule)")

# ---- 8. the serialiser's token is named, not positional ----------------------
# SerializeAsync(stream, value, cancellationToken) does not bind the token to
# the token parameter - it binds it to options, and the compiler answers
# CS1503. The token must be named, or sit behind an explicit options value.
# Written the day exactly that shipped. The comma count is honest for this
# codebase: these calls pass plain identifiers, nothing nested.
for path, body in cs_text.items():
    for m in re.finditer(r'\b((?:Serialize|Deserialize)Async)(?:<[^>()]*>)?\(([^()]*)\)', body):
        args = [a.strip() for a in m.group(2).split(',')]
        if 'cancellationToken' not in args:
            continue
        index = args.index('cancellationToken')
        needed = 3 if m.group(1) == 'SerializeAsync' else 2
        if index < needed:
            line = body[: m.start()].count('\n') + 1
            problems.append(
                f"{os.path.relpath(path, ROOT)}:{line}: {m.group(1)} - the cancellation "
                f"token is positional at argument {index + 1}, where it would bind to "
                f"options/typeInfo. Name it: cancellationToken: cancellationToken")

print()
if problems:
    print(f"{len(problems)} PROBLEM(S):")
    for p in problems:
        print("  -", p)
    sys.exit(1)

print(f"OK - {len(XAML)} XAML files, {len(CS)} C# files, no problems found")
