#!/usr/bin/env python3
"""Checks that every type a C# file names can actually be seen from it.

The app has no compiler in its build environment - the user's machine is the
compiler - so a missing using directive is not caught here until it is a build
error there. This closes that gap for two families of names:

* framework types, against a curated map of the WinUI and core types this app
  uses (UserControl, Storyboard, DispatcherQueue, and so on). A bare use of a
  mapped type without the using that carries it is flagged;

* app types, discovered from the source itself: every class, enum, interface and
  record, the namespace that declares it, and the same visibility rule the C#
  compiler applies - the declaring namespace, a using directive, or an enclosing
  namespace.

Qualified uses (Microsoft.UI.Xaml.Controls.UserControl) satisfy themselves, and
comments, strings and character literals are stripped before any of this, so a
mention in prose is not a use. System.IO.Path and the XAML Path shape share a
name; both spellings are accepted for that one type.

Exit status is non-zero if any use is invisible, which makes this usable as a
pre-commit gate alongside the other validators.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DO_NET = ROOT / "DoNet"

# The framework types this app uses, by the namespace that carries them. Adding
# a type here is only needed the first time a file uses it bare without its
# using - exactly the situation this exists to catch.
FRAMEWORK_TYPES: dict[str, str] = {
    # Microsoft.UI.Xaml.Controls
    "UserControl": "Microsoft.UI.Xaml.Controls",
    "Button": "Microsoft.UI.Xaml.Controls",
    "TextBlock": "Microsoft.UI.Xaml.Controls",
    "TextBox": "Microsoft.UI.Xaml.Controls",
    "PasswordBox": "Microsoft.UI.Xaml.Controls",
    "CalendarDatePicker": "Microsoft.UI.Xaml.Controls",
    "CheckBox": "Microsoft.UI.Xaml.Controls",
    "ListView": "Microsoft.UI.Xaml.Controls",
    "ListViewItem": "Microsoft.UI.Xaml.Controls",
    "ItemsRepeater": "Microsoft.UI.Xaml.Controls",
    "ScrollViewer": "Microsoft.UI.Xaml.Controls",
    "ScrollViewerViewChangedEventArgs": "Microsoft.UI.Xaml.Controls",
    "TextChangedEventArgs": "Microsoft.UI.Xaml.Controls",
    "SelectionChangedEventArgs": "Microsoft.UI.Xaml.Controls",
    "ContentPresenter": "Microsoft.UI.Xaml.Controls",
    "Flyout": "Microsoft.UI.Xaml.Controls",
    "FlyoutBase": "Microsoft.UI.Xaml.Controls",
    # "Page" is deliberately unmapped: the directory contracts name a tuple
    # element "Page", which collides with the type on every read. The files
    # that derive from Page carry the using already.
    "Frame": "Microsoft.UI.Xaml.Controls",
    "Grid": "Microsoft.UI.Xaml.Controls",
    "StackPanel": "Microsoft.UI.Xaml.Controls",
    "Canvas": "Microsoft.UI.Xaml.Controls",
    "Border": "Microsoft.UI.Xaml.Controls",
    "InfoBar": "Microsoft.UI.Xaml.Controls",
    # Microsoft.UI.Xaml
    "Visibility": "Microsoft.UI.Xaml",
    "RoutedEventArgs": "Microsoft.UI.Xaml",
    "DependencyObject": "Microsoft.UI.Xaml",
    "DependencyProperty": "Microsoft.UI.Xaml",
    "DependencyPropertyChangedEventArgs": "Microsoft.UI.Xaml",
    "FrameworkElement": "Microsoft.UI.Xaml",
    "UIElement": "Microsoft.UI.Xaml",
    "Application": "Microsoft.UI.Xaml",
    "FocusState": "Microsoft.UI.Xaml",
    "VisualState": "Microsoft.UI.Xaml",
    "VisualStateManager": "Microsoft.UI.Xaml",
    "CornerRadius": "Microsoft.UI.Xaml",
    "Thickness": "Microsoft.UI.Xaml",
    "ThemeDictionary": "Microsoft.UI.Xaml",
    # Microsoft.UI.Xaml.Media
    "Brush": "Microsoft.UI.Xaml.Media",
    "SolidColorBrush": "Microsoft.UI.Xaml.Media",
    "FontFamily": "Microsoft.UI.Xaml.Media",
    "ImageBrush": "Microsoft.UI.Xaml.Media",
    "Geometry": "Microsoft.UI.Xaml.Media",
    "TranslateTransform": "Microsoft.UI.Xaml.Media",
    "ScaleTransform": "Microsoft.UI.Xaml.Media",
    "RotateTransform": "Microsoft.UI.Xaml.Media",
    "TransformGroup": "Microsoft.UI.Xaml.Media",
    # Microsoft.UI.Xaml.Media.Animation
    "Storyboard": "Microsoft.UI.Xaml.Media.Animation",
    "DoubleAnimation": "Microsoft.UI.Xaml.Media.Animation",
    "CubicEase": "Microsoft.UI.Xaml.Media.Animation",
    "EasingMode": "Microsoft.UI.Xaml.Media.Animation",
    "Duration": "Microsoft.UI.Xaml.Media.Animation",
    # Microsoft.UI.Xaml.Media.Imaging
    "BitmapImage": "Microsoft.UI.Xaml.Media.Imaging",
    # Microsoft.UI.Xaml.Shapes
    "Path": "Microsoft.UI.Xaml.Shapes",
    "Rectangle": "Microsoft.UI.Xaml.Shapes",
    "Ellipse": "Microsoft.UI.Xaml.Shapes",
    # Microsoft.UI.Xaml.Markup
    "XamlBindingHelper": "Microsoft.UI.Xaml.Markup",
    # Microsoft.UI.Xaml.Input
    "TappedRoutedEventArgs": "Microsoft.UI.Xaml.Input",
    "KeyRoutedEventArgs": "Microsoft.UI.Xaml.Input",
    "PointerRoutedEventArgs": "Microsoft.UI.Xaml.Input",
    # Microsoft.UI.Dispatching
    "DispatcherQueue": "Microsoft.UI.Dispatching",
    "DispatcherQueuePriority": "Microsoft.UI.Dispatching",
    # Windows
    "VirtualKey": "Windows.System",
    "Point": "Windows.Foundation",
    "Rect": "Windows.Foundation",
    "Size": "Windows.Foundation",
    "Color": "Windows.UI",
    # Core and collections. List is here because a missing
    # using System.Collections.Generic is exactly the shape of break this
    # exists to catch: invisible until the user's machine compiles it.
    "List": "System.Collections.Generic",
    "Dictionary": "System.Collections.Generic",
    "HashSet": "System.Collections.Generic",
    "KeyValuePair": "System.Collections.Generic",
    "IReadOnlyList": "System.Collections.Generic",
    "IEnumerable": "System.Collections.Generic",
    "ObservableCollection": "System.Collections.ObjectModel",
    "EventArgs": "System",
    "EventHandler": "System",
    "Exception": "System",
    "Action": "System",
    "Func": "System",
    "TimeSpan": "System",
    "DateTime": "System",
    "DateTimeOffset": "System",
    "StringComparer": "System",
    "StringSplitOptions": "System",
    "IFormatProvider": "System",
    "Task": "System.Threading.Tasks",
    "CancellationToken": "System.Threading",
}

# Namespaces that also carry a name, for the types that live in more than one.
ALSO_ACCEPTED: dict[str, list[str]] = {
    # System.IO.Path and the shape share a name; either using satisfies a use.
    "Path": ["System.IO"],
}

# A type name used only to reach an instance member is not a type use:
# "DispatcherQueue.TryEnqueue" in a card is the control's own property, while
# "DispatcherQueue.GetForCurrentThread()" in a view model is the static on the
# type. The two are indistinguishable to a regex, so the instance members this
# app reaches that way are listed and a name whose every bare use is one of
# them is left alone.
QUALIFIER_MEMBERS: dict[str, list[str]] = {
    "DispatcherQueue": ["TryEnqueue"],
}

# Type names in this codebase are PascalCase, and the pattern needs that: a
# local named "record" - "record is not null", "foreach (ServiceRecord record
# in page)" - reads as the contextual keyword followed by a lowercase word, and
# without the case gate that word gets filed as a declared type.
TYPE_DECLARATION = re.compile(r"\b(?:class|enum|interface|record|struct)\s+([A-Z]\w+)")
NAMESPACE = re.compile(r"^namespace\s+([\w.]+);", re.M)
USING = re.compile(r"^using\s+(?:static\s+)?([\w.]+);", re.M)

CHAR_LITERAL = r"'(?:\\.|[^\\'])'"
LINE_COMMENT = r"//[^\n]*"
BLOCK_COMMENT = r"/\*.*?\*/"
STRING_LITERAL = r'"(?:\\.|[^"\\])*"'


def strip_noise(source: str) -> str:
    """Removes character literals, comments and strings, in that order.

    The order is load-bearing: a comment can contain an apostrophe or a quote,
    and stripping in the wrong order leaves a half-eaten literal behind that
    unbalances every count taken afterwards.
    """
    source = re.sub(CHAR_LITERAL, "'c'", source)
    source = re.sub(LINE_COMMENT, "", source)
    source = re.sub(BLOCK_COMMENT, "", source, flags=re.S)
    source = re.sub(STRING_LITERAL, '""', source)
    return source


def enclosing_namespaces(namespace: str) -> set[str]:
    """Every namespace that encloses one, including the global one."""
    parts = namespace.split(".")
    return {".".join(parts[:i]) for i in range(len(parts))}


def main() -> int:
    app_types: dict[str, list[str]] = {}
    sources: dict[Path, tuple[str, str]] = {}

    for path in sorted(DO_NET.rglob("*.cs")):
        raw = path.read_text(encoding="utf-8")
        match = NAMESPACE.search(raw)
        if match is None:
            # Generated or build-time files; nothing to check.
            continue
        namespace = match.group(1)
        code = strip_noise(raw)
        sources[path] = (namespace, code)

        for declared in TYPE_DECLARATION.findall(code):
            app_types.setdefault(declared, []).append(namespace)

    problems: list[str] = []

    for path, (namespace, code) in sources.items():
        usings = set(USING.findall(code)) | enclosing_namespaces(namespace)
        usings.add(namespace)

        declared_here = set(TYPE_DECLARATION.findall(code))

        def uses_bare(name: str) -> bool:
            # Negative lookbehind: a name preceded by a dot is a qualified use
            # and satisfies itself, and one preceded by a word character is a
            # different, longer name.
            return re.search(rf"(?<![\w.]){name}\b", code) is not None

        def has_namespace(ns: str) -> bool:
            return ns in usings

        def only_qualifier_members(name: str) -> bool:
            # Every bare use is "Name.member", where the member is one of the
            # known instance members - the name belongs to a property, not the
            # type, and the file owes no using for it.
            members = QUALIFIER_MEMBERS.get(name)
            if members is None:
                return False

            uses = re.findall(rf"(?<![\w.]){name}\.(\w+)", code)
            return len(uses) > 0 and all(member in members for member in uses)

        def declares_member(name: str) -> bool:
            # A method or property declared in this file with the same name as
            # the type: the bare uses are the member referring to itself, as
            # with ChoicePicker's private static Brush helper.
            return re.search(
                rf"(?m)^\s*(?:\[[^\]]*\]\s*)*"
                rf"(?:(?:public|private|internal|protected|static|sealed|virtual|override|async|readonly|new)\s+)*"
                rf"[\w.]+(?:<[^>(]*>)?(?:\[\])?\??\s+{name}\s*(?:\(|[;{{])",
                code,
            ) is not None

        for name, ns in FRAMEWORK_TYPES.items():
            if name in declared_here or not uses_bare(name):
                continue
            if has_namespace(ns) or any(has_namespace(alt) for alt in ALSO_ACCEPTED.get(name, [])):
                continue
            if only_qualifier_members(name) or declares_member(name):
                continue
            problems.append(f"{path.relative_to(ROOT)}: uses {name} without using {ns}")

        for name, namespaces in app_types.items():
            if name in declared_here or not uses_bare(name):
                continue
            if any(has_namespace(ns) for ns in namespaces):
                continue
            if only_qualifier_members(name) or declares_member(name):
                continue
            problems.append(
                f"{path.relative_to(ROOT)}: uses {name} without a using for "
                f"{' or '.join(sorted(set(namespaces)))}"
            )

    if problems:
        print(f"{len(problems)} invisible type use(s) found:")
        for problem in problems:
            print(f"  {problem}")
        return 1

    print(f"OK - every type use in {len(sources)} files resolves.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
