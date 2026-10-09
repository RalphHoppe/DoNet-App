#!/usr/bin/env python3
"""
Checks that every property set on one of our own controls actually exists.

The failure this catches: renaming a dependency property's type but not the
property itself, so XAML sets `Website="..."` on a control whose property is
still called `Person`. The compiler reports it as CS0119 somewhere else
entirely - "'Website' is a type, which is not valid in the given context" -
which points at the symptom rather than the cause.
"""
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent / "DoNet"

# Properties every FrameworkElement already has; not our business to verify.
FRAMEWORK = {
    "x:Name", "x:Load", "x:Uid", "Width", "Height", "MinWidth", "MinHeight",
    "MaxWidth", "MaxHeight", "Margin", "Padding", "Visibility", "Opacity",
    "HorizontalAlignment", "VerticalAlignment", "Background", "Foreground",
    "BorderBrush", "BorderThickness", "CornerRadius", "FontFamily", "FontSize",
    "FontWeight", "Style", "IsEnabled", "IsTabStop", "TabIndex", "RenderTransform",
    "RenderTransformOrigin", "DataContext", "Tag", "Grid.Row", "Grid.Column",
    "Grid.RowSpan", "Grid.ColumnSpan", "Canvas.Left", "Canvas.Top", "Canvas.ZIndex",
    "ToolTipService.ToolTip", "AutomationProperties.Name", "HorizontalContentAlignment",
    "VerticalContentAlignment", "Content", "ItemsSource", "ItemTemplate",
    "IsHitTestVisible", "Clip", "Transitions", "CacheMode", "Shadow", "Translation",
    "FlowDirection", "Language", "AllowFocusOnInteraction", "UseSystemFocusVisuals",
    "XYFocusKeyboardNavigation", "TabFocusNavigation", "KeyboardAcceleratorPlacementMode",
}

# Inherited UIElement events wired in XAML; declared on FrameworkElement, not on
# the control's own code-behind, so the member scan of the .xaml.cs cannot see
# them and they are none of its business.
INHERITED_EVENTS = {
    "Loaded", "Unloaded", "SizeChanged", "LayoutUpdated",
    "PointerEntered", "PointerExited", "PointerPressed", "PointerReleased",
    "PointerMoved", "PointerCaptureLost", "PointerWheelChanged",
    "Tapped", "DoubleTapped", "RightTapped", "Holding",
    "GotFocus", "LostFocus", "KeyDown", "KeyUp", "CharacterReceived",
    "DragEnter", "DragLeave", "DragOver", "Drop",
    "GettingFocus", "LosingFocus", "ProcessKeyboardAccelerators",
}


def declared_members(name: str) -> set[str] | None:
    """Properties and events on a control, from its code-behind."""
    for folder in ("Controls", "Views"):
        path = ROOT / folder / f"{name}.xaml.cs"
        if path.exists():
            text = path.read_text(encoding="utf-8")
            found = set(re.findall(r"public\s+(?:event\s+)?[\w<>?\[\],\s\.]+?\s+(\w+)\s*(?:\{|;|=>)", text))
            found |= set(re.findall(r"public\s+event\s+[\w<>?\.]+\s+(\w+)\s*;", text))
            return found
    return None


def check_gridlength_assignments(problems):
    """Height and Width on row/column definitions are GridLength, not GridUnitType.

    new RowDefinition { Height = GridUnitType.Auto } reads as if it should work
    and is CS0266 ("cannot implicitly convert GridUnitType to GridLength"). The
    auto case is the one written by reflex; every unit needs the GridLength
    wrapper, even the unit-less Auto.
    """
    for cs in sorted(ROOT.rglob("*.cs")):
        text = cs.read_text(encoding="utf-8-sig")
        for m in re.finditer(r"\b(Height|Width)\s*=\s*(?:\w+\.)*GridUnitType\.\w+", text):
            line = text[: m.start()].count("\n") + 1
            problems.append(
                f"  {cs.relative_to(ROOT.parent)}:{line}: {m.group(0)} - "
                f"these are GridLength properties; use new GridLength(1, GridUnitType.Auto)")


def main() -> int:
    problems = []
    checked = 0

    for xaml in sorted(ROOT.rglob("*.xaml")):
        text = xaml.read_text(encoding="utf-8")
        text = re.sub(r"<!--.*?-->", "", text, flags=re.S)

        for match in re.finditer(r"<(?:controls|views):(\w+)\b((?:[^<>]|\n)*?)/?>", text):
            control, body = match.group(1), match.group(2)
            members = declared_members(control)
            if members is None:
                continue

            checked += 1
            for attr in re.findall(r"(?:^|\s)([\w:\.]+)\s*=\s*\"", body):
                if (attr in FRAMEWORK or attr in INHERITED_EVENTS
                        or attr.startswith(("x:", "xmlns")) or "." in attr):
                    continue
                if attr not in members:
                    rel = xaml.relative_to(ROOT.parent)
                    problems.append(f"  {rel}: <{control} {attr}=...> - {control} has no '{attr}'")

    check_gridlength_assignments(problems)

    if problems:
        print("Properties set on our controls that do not exist:\n")
        print("\n".join(sorted(set(problems))))
        return 1

    print(f"OK - {checked} custom control usages, every property resolves")
    return 0


if __name__ == "__main__":
    sys.exit(main())
