#!/usr/bin/env python3
"""
Catches the XAML mistakes a compiler would, for when a Windows build is not to hand.

Checks:
  1. every x:Class has a matching partial class in a .cs file
  2. every {StaticResource}/{ThemeResource} key is defined somewhere in the project
  3. every event-handler attribute names a method that exists in the code-behind
  4. every x:Name referenced from code-behind is actually declared in the markup
  5. every Grid.Row / Grid.Column sits inside the rows and columns its Grid defines
  6. no element's content children are split in two by a property element

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

    # IsEnabled is a Control member. Panels and decorators are FrameworkElements
    # and do not have it - WMC0011 Unknown member, one build cycle per mistake.
    # Gate a layer with Visibility (fully inert) or IsHitTestVisible (pointer only).
    **{
        tag: {
            "IsEnabled":
                f"{tag} is not a Control and has no IsEnabled. "
                "Gate a layer with Visibility, or IsHitTestVisible for the pointer alone."
        }
        for tag in (
            "Grid", "StackPanel", "Border", "Canvas", "ItemsRepeater",
            "ContentPresenter", "Viewbox", "RelativePanel", "WrapPanel",
        )
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


# Properties a CommunityToolkit.Mvvm view model exposes, including the ones its source
# generator produces. Written because rewriting a view model and leaving a binding
# pointing at a property that no longer exists costs a full build cycle, and x:Bind
# failures are compile-time so they are worth catching here rather than in Visual
# Studio one file at a time.
def viewmodel_members(src):
    import re
    members = set()

    # ordinary properties: public T Name { ... } or public T Name =>
    for m in re.finditer(r"public\s+(?:static\s+)?[\w<>,\[\]\?\s]+?\s+(\w+)\s*(?:\{|=>)", src):
        members.add(m.group(1))

    # [ObservableProperty] private T _name;  ->  Name
    for m in re.finditer(r"\[ObservableProperty\][\s\S]{0,400}?private\s+[\w<>,\[\]\?]+\s+_(\w+)\s*[;=]", src):
        name = m.group(1)
        members.add(name[0].upper() + name[1:])

    # [RelayCommand] on Foo() / FooAsync()  ->  FooCommand
    for m in re.finditer(r"\[RelayCommand[^\]]*\][\s\S]{0,300}?\b(\w+)\s*\([^)]*\)\s*(?:\{|=>)", src):
        name = m.group(1)
        if name.endswith("Async"):
            name = name[: -len("Async")]
        members.add(name + "Command")

    return members


def check_viewmodel_bindings(xaml_rel, xaml_src, cs_src, all_cs, problems):
    """Resolve every x:Bind ViewModel.<member> path against the real view model."""
    import re

    decl = re.search(r"public\s+(\w*ViewModel)\s+ViewModel\b", cs_src)
    if not decl:
        return
    vm_type = decl.group(1)

    vm_src = None
    for path, text in all_cs.items():
        if re.search(rf"class\s+{vm_type}\b", text):
            vm_src = text
            break
    if vm_src is None:
        return

    members = viewmodel_members(vm_src)
    if not members:
        return

    for m in re.finditer(r"x:Bind\s+ViewModel\.(\w+)", xaml_src):
        member = m.group(1)
        if member in members:
            continue
        line = xaml_src[: m.start()].count("\n") + 1
        problems.append(
            f"{xaml_rel}:{line}: x:Bind ViewModel.{member} - "
            f"{vm_type} has no such member")


# ---- 2b. members that do not exist on that element ------------------------
for path, text in xaml_text.items():
    check_forbidden_members(os.path.relpath(path, ROOT), text, problems)

# ---- 2c. x:Bind ViewModel paths resolve -----------------------------------
for path, text in xaml_text.items():
    base = path[: -len(".xaml")] + ".xaml.cs"
    if os.path.exists(base):
        check_viewmodel_bindings(
            os.path.relpath(path, ROOT), text, cs_text.get(base, ""), cs_text, problems)

def check_datatemplate_binds(path, xaml_src, all_cs, problems):
    """x:Bind paths inside a typed DataTemplate resolve against the x:DataType.

    {x:Bind ViewModel.X} inside a DataTemplate asks the template's own data type
    for a ViewModel property it does not have (XLS0432 at design time, an error
    in the generated code at build time), because the path's root is the
    x:DataType, not the page. Every first segment is checked against the members
    of that type; the whole-object bind ({x:Bind}), function bindings and casts
    are left alone.
    """
    rel = os.path.relpath(path, ROOT)
    for m in re.finditer(r"<DataTemplate\b[^>]*x:DataType=\"([\w:.]+)\"", xaml_src):
        type_name = m.group(1).rsplit(":", 1)[-1]
        end = xaml_src.find("</DataTemplate>", m.end())
        if end == -1:
            continue
        body = xaml_src[m.end():end]

        members = None
        for src in all_cs.values():
            if re.search(rf"\b(?:class|record)\s+{type_name}\b", src):
                members = viewmodel_members(src)
                break
        if not members:
            continue

        for b in re.finditer(r"\{x:Bind\s+([^{}]+)", body):
            raw = b.group(1)
            depth = 0
            stop = len(raw)
            for i, ch in enumerate(raw):
                if ch == "(":
                    depth += 1
                elif ch == ")":
                    depth -= 1
                elif ch == "," and depth == 0:
                    stop = i
                    break
            bind_path = raw[:stop].strip()
            if bind_path.lower().startswith("path="):
                bind_path = bind_path[5:].strip()

            if not bind_path or "(" in bind_path:
                # The whole-object bind, a function binding or a cast: not a
                # property path the compiler resolves against the type.
                continue

            first = bind_path.split(".")[0]
            if first in members or not first[0].isupper():
                # A lowercase first segment is an attached-property-style path,
                # not a member lookup; leave it to the compiler.
                continue

            line = xaml_src[: m.end() + b.start()].count("\n") + 1
            problems.append(
                f"{rel}:{line}: x:Bind path '{bind_path}' inside the {type_name} "
                f"template - {type_name} has no member '{first}'. A path in a "
                f"DataTemplate starts from the x:DataType, not from the page.")


# ---- 2d. x:Bind paths inside typed templates resolve against the DataType --
for path, text in xaml_text.items():
    check_datatemplate_binds(path, text, cs_text, problems)

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

# ---- 5. Grid.Row / Grid.Column inside what the Grid defines ----------------
# A child placed in a row or column its Grid never declared is not an error the
# parser reports - the grid simply drops it into the implicit single row (or the
# last defined one), where it draws on top of whatever is already there. That is
# how a NOTE field once landed on top of a NAME field, and why this exists.
import xml.etree.ElementTree as ET


def local(tag):
    return tag.rsplit("}", 1)[-1] if isinstance(tag, str) else ""


def describe(element):
    name = element.get("x:Name")
    return f"<{local(element.tag)}>" + (f" {name}" if name else "")


def check_grid_indices(path, problems):
    rel = os.path.relpath(path, ROOT)
    try:
        tree = ET.parse(path)
    except ET.ParseError as error:
        problems.append(f"{rel}: not well-formed XML ({error})")
        return

    for grid in tree.iter():
        if local(grid.tag) != "Grid":
            continue

        rows = cols = 0
        for child in grid:
            if local(child.tag) == "Grid.RowDefinitions":
                rows = sum(1 for row in child if local(row.tag) == "RowDefinition")
            elif local(child.tag) == "Grid.ColumnDefinitions":
                cols = sum(1 for col in child if local(col.tag) == "ColumnDefinition")

        for child in grid:
            tag = local(child.tag)
            if tag in ("Grid.RowDefinitions", "Grid.ColumnDefinitions"):
                continue

            row = child.get("Grid.Row")
            if row is not None and row.isdigit() and int(row) >= max(rows, 1):
                problems.append(
                    f"{rel}: {describe(child)} Grid.Row={row} but its Grid defines "
                    + (f"{rows} row(s)" if rows else "no RowDefinitions"))

            column = child.get("Grid.Column")
            if column is not None and column.isdigit() and int(column) >= max(cols, 1):
                problems.append(
                    f"{rel}: {describe(child)} Grid.Column={column} but its Grid defines "
                    + (f"{cols} column(s)" if cols else "no ColumnDefinitions"))

        # A child with no placement at all lands in cell (0,0) - invisible
        # stacking when its siblings are placed. Exists because the VPS form
        # shipped six rebuilt cells without their Grid.Row/Grid.Column: they
        # all piled into the top-left cell, five of them hidden under the
        # sixth, and the form read as missing fields in the wrong order.
        #
        # The (0,0)-omission idiom - the first cell leaves both attributes off -
        # is legitimate, so the rule fires only on real collisions: an unplaced
        # child whose grid has a sibling explicitly claiming (0,0), or unplaced
        # children plural, which stack on each other. Property elements
        # (Grid.RowDefinitions, VisualStateManager.*, Grid.RenderTransform)
        # carry a dot in their tag and are not children at all.
        PLACEMENT = ("Grid.Row", "Grid.Column", "Grid.RowSpan", "Grid.ColumnSpan")
        kids = [
            c for c in grid
            if local(c.tag) not in ("Grid.RowDefinitions", "Grid.ColumnDefinitions")
            and "." not in local(c.tag)
        ]
        unplaced = [c for c in kids if not any(key in c.attrib for key in PLACEMENT)]
        claims00 = any(
            any(key in c.attrib for key in PLACEMENT)
            and (c.get("Grid.Row") or "0") == "0"
            and (c.get("Grid.Column") or "0") == "0"
            for c in kids)

        # Only grids that actually divide space - more than one row or column.
        # A 0x0 or 1x1 grid is the overlay idiom, where stacking is the point.
        if (rows > 1 or cols > 1) and unplaced and (len(unplaced) > 1 or claims00):
            for child in unplaced:
                problems.append(
                    f"{rel}: {describe(child)} has no Grid.Row/Grid.Column in a "
                    f"{rows}x{cols} Grid - it lands in cell (0,0) on top of whatever "
                    "belongs there")


def check_content_splits(path, problems):
    """Content children split in two by a property element.

    The WinRT XAML parser ends an element's implicit content assignment at the
    first property element that arrives after content has started, so a second
    run of content reads as a duplicate assignment - "Duplication assignment to
    the 'Children' property of the 'Grid' object" - and everything after it is
    reported against a parser state that no longer matches the file. Property
    elements before the content, or after all of it, are fine; only a run of
    content with a property element in the middle breaks. ControlTemplates are
    exempt: the template loader places VisualStateManager groups freely, and
    every template in this app relies on that.
    """
    rel = os.path.relpath(path, ROOT)
    try:
        tree = ET.parse(path)
    except ET.ParseError:
        return  # already reported by check_grid_indices

    def walk(element, in_template):
        template = in_template or local(element.tag) == "ControlTemplate"
        children = list(element)
        tags = [local(child.tag) for child in children]
        seen_content = False
        for i, child in enumerate(children):
            tag = tags[i]
            if "." in tag:
                # A property element after content only breaks the parse when
                # more content follows it - a trailing one is fine.
                more_content = any(not later.__contains__(".") for later in tags[i + 1:])
                if seen_content and more_content and not template:
                    problems.append(
                        f"{rel}: {describe(element)} has content children on both "
                        f"sides of <{tag}> - the parser reads the second run as a "
                        f"duplicate assignment to the content property")
                continue
            seen_content = True
            walk(child, template)

    walk(tree.getroot(), False)


for path in XAML:
    check_grid_indices(path, problems)

def check_static_bind_paths(path, problems):
    """x:Bind paths must start from the page; class-qualified statics are not paths.

    {x:Bind vm:ServiceDesignerViewModel.KindNames} looks like it should reach the
    static, but the generated code ends up naming it bare and the build fails
    with CS0103 in the .g.cs. Function bindings are the exception - the first
    token of a call may be a class-qualified method - so paths with a top-level
    '(' are left alone, as are casts.
    """
    rel = os.path.relpath(path, ROOT)
    src = read(path)
    for m in re.finditer(r"\{x:Bind\s+([^{}]+)", src):
        raw = m.group(1)
        # The path ends at the first comma outside any parens of the whole
        # markup extension (Mode=, Converter=, FallbackValue= follow it).
        depth = 0
        end = len(raw)
        for i, ch in enumerate(raw):
            if ch == "(":
                depth += 1
            elif ch == ")":
                depth -= 1
            elif ch == "," and depth == 0:
                end = i
                break
        bind_path = raw[:end].strip()
        if bind_path.lower().startswith("path="):
            bind_path = bind_path[5:].strip()

        if "(" not in bind_path and ":" in bind_path:
            line = src[: m.start()].count("\n") + 1
            problems.append(
                f"{rel}:{line}: x:Bind path '{bind_path}' starts from the page or "
                f"view model it is written on; it cannot reach a class-qualified "
                f"static. Bind through a property on the view, or use a function binding")


for path in XAML:
    check_content_splits(path, problems)

for path in XAML:
    check_static_bind_paths(path, problems)

print("\n".join(notes))
print()
if problems:
    print(f"{len(problems)} PROBLEM(S):")
    for p in problems:
        print("  -", p)
    sys.exit(1)

print(f"OK - {len(XAML)} XAML files, {len(defined)} resource keys, no problems found")
