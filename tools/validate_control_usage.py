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
                if attr in FRAMEWORK or attr.startswith(("x:", "xmlns")) or "." in attr:
                    continue
                if attr not in members:
                    rel = xaml.relative_to(ROOT.parent)
                    problems.append(f"  {rel}: <{control} {attr}=...> - {control} has no '{attr}'")

    if problems:
        print("Properties set on our controls that do not exist:\n")
        print("\n".join(sorted(set(problems))))
        return 1

    print(f"OK - {checked} custom control usages, every property resolves")
    return 0


if __name__ == "__main__":
    sys.exit(main())
