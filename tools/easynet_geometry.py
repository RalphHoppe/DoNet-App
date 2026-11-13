#!/usr/bin/env python3
"""
Builds the EasyNet vector controls from the design's own SVGs.

The four SVGs in new_design/easynet_animated_svgs share one geometry: the
mark's slabs, bars, ring segments and nodes, and the wordmark's seven
letterforms. This script reads that geometry straight out of the SVG text
and writes the XAML the app draws it with, plus a partial class carrying
the numbers the hand-written animation code needs (phases, delays, dash
offsets). Nothing is retyped by hand, so the SVGs stay the single source
of truth: change the design, run the script, the app follows.

Writes:
  EasyNet/Controls/EasyNetMark.xaml          the mark's static geometry
  EasyNet/Controls/EasyNetMark.geometry.cs   the numbers its animations use
  EasyNet/Controls/EasyNetWordmark.xaml      the wordmark's letterforms
  EasyNet/Controls/EasyNetWordmark.geometry.cs

Run from the repository root:   python3 tools/easynet_geometry.py
"""
import math
import os
import re

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
SVG_DIR = os.path.join(ROOT, "new_design", "easynet_animated_svgs")
OUT_DIR = os.path.join(ROOT, "EasyNet", "Controls")

# The design's tokens, read from the SVGs' own stylesheet.
INK = "#1D212C"
GRADIENT_TOP = "#FCBD2E"
GRADIENT_BOTTOM = "#F26A17"
HALO_COLOUR = "#F7961E"
TICK_A_COLOUR = "#FFD37A"
TICK_A_OPACITY = 0.55
TICK_B_COLOUR = "#FFF6D8"


def read(name):
    with open(os.path.join(SVG_DIR, name), encoding="utf-8") as handle:
        return handle.read()


def path_coords(data):
    """Every (x, y) pair in an all-absolute M/L path, for bounds measuring."""
    nums = [float(n) for n in re.findall(r"-?\d+\.?\d*", data)]
    return list(zip(nums[0::2], nums[1::2]))


src = read("easynet-logo.svg")

# ---- the mark's parts, by the classes the SVG stylesheet drives -------------

slabs = re.findall(r'<path class="en-dk (en-d[12])"[^>]*? d="([^"]+)"', src)
bars = re.findall(r'<path class="en-bar (en-b[12])"[^>]*? d="([^"]+)"', src)

ring_group = re.search(r'<g id="en-rp">(.*?)</g>', src, re.S).group(1)
ring_segments = re.findall(r'<path d="([^"]+)"', ring_group)

nodes = re.findall(
    r'<g class="en-n" style="--f:([\d.]+);--d:([\d.]+)s">'
    r'<circle cx="([\d.]+)" cy="([\d.]+)" r="([\d.]+)"/></g>', src)

# The orbit every moving part shares: one rotated ellipse.
ell = re.search(
    r'<ellipse class="en-rvs" cx="([\d.]+)" cy="([\d.]+)" '
    r'rx="([\d.]+)" ry="([\d.]+)" transform="rotate\(([\d.-]+)', src)
ell_cx, ell_cy, ell_rx, ell_ry, ell_rot = (float(g) for g in ell.groups())


def orbit_angle(data):
    """A segment's place around the orbit, as its parametric angle.

    The shine wave visits the segments in the order the SVG's own travelling
    light moves: along the ellipse's own direction. The segment's centroid,
    un-rotated into the ellipse's frame, gives that as one atan2.
    """
    pts = path_coords(data)
    px = sum(point[0] for point in pts) / len(pts)
    py = sum(point[1] for point in pts) / len(pts)
    angle = math.radians(-ell_rot)
    dx = px - ell_cx
    dy = py - ell_cy
    return math.atan2(
        ((dx * math.sin(angle)) + (dy * math.cos(angle))) / ell_ry,
        ((dx * math.cos(angle)) - (dy * math.sin(angle))) / ell_rx) % (2 * math.pi)


# The segments, walked in the orbit's own order - which is the order the
# generated RingSeg names and the shine wave both follow.
ring_segments.sort(key=orbit_angle)
segment_phases = [orbit_angle(d) / (2 * math.pi) for d in ring_segments]

halo = re.search(r'<ellipse class="en-halo" cx="([\d.]+)" cy="([\d.]+)" '
                 r'rx="([\d.]+)" ry="([\d.]+)"', read("easynet-splash.svg"))
halo_cx, halo_cy, halo_rx, halo_ry = (float(g) for g in halo.groups())

letters = re.findall(r'<path class="en-ch (en-ink|en-or)" style="--i:(\d+)" d="([^"]+)"', src)

# ---- the mark canvas -------------------------------------------------------
# The loader-icon variant's viewBox frames the mark alone; the wordmark gets
# a canvas measured from its own letterforms.

mark_left, mark_top, mark_w, mark_h = 180.0, 120.0, 910.0, 790.0

letter_points = [pt for _, _, d in letters for pt in path_coords(d)]
word_left = min(p[0] for p in letter_points)
word_top = min(p[1] for p in letter_points)
word_right = max(p[0] for p in letter_points)
word_bottom = max(p[1] for p in letter_points)
word_w = round(word_right - word_left, 1)
word_h = round(word_bottom - word_top, 1)


def esc(data):
    return data


# --------------------------------------------------------------------------- #
# EasyNetMark.xaml
# --------------------------------------------------------------------------- #
mark_xaml = f'''<UserControl
    x:Class="EasyNet.Controls.EasyNetMark"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    mc:Ignorable="d">

    <!--
        Generated from new_design/easynet_animated_svgs by tools/easynet_geometry.py.
        The geometry is the SVGs' own, verbatim; the named parts and transforms
        exist for the animation code in EasyNetMark.xaml.cs, which mirrors the
        SVG stylesheet's en-play entrance and en-loop cadence. Default states
        are the settled logo: the halo and the ring's shine are hidden, as the
        static easynet-logo.svg shows them. The RingSeg and Shine names run in
        the orbit's own order, which is the order the light walks them in.
    -->
    <Viewbox Stretch="Uniform">
        <Canvas x:Name="Root"
                Width="{mark_w}"
                Height="{mark_h}">
            <Canvas.Resources>
                <SolidColorBrush x:Key="MarkInkBrush" Color="{INK}" />
                <LinearGradientBrush x:Key="MarkBrandBrush"
                                     StartPoint="1,0" EndPoint="0,1">
                    <GradientStop Color="{GRADIENT_TOP}" Offset="0" />
                    <GradientStop Color="{GRADIENT_BOTTOM}" Offset="1" />
                </LinearGradientBrush>
                <!--
                    The ring's gradient is painted in the art's own coordinates
                    (the SVG's userSpaceOnUse brush), so every segment shades
                    as part of one continuous sweep.
                -->
                <LinearGradientBrush x:Key="MarkRingBrush"
                                     MappingMode="Absolute"
                                     StartPoint="1030,200" EndPoint="240,800">
                    <GradientStop Color="{GRADIENT_TOP}" Offset="0" />
                    <GradientStop Color="{GRADIENT_BOTTOM}" Offset="1" />
                </LinearGradientBrush>
            </Canvas.Resources>

            <Canvas x:Name="Art"
                    Canvas.Left="{-mark_left}"
                    Canvas.Top="{-mark_top}">

                <Ellipse x:Name="Halo"
                         Canvas.Left="{round(halo_cx - halo_rx, 1)}"
                         Canvas.Top="{round(halo_cy - halo_ry, 1)}"
                         Width="{round(halo_rx * 2, 1)}"
                         Height="{round(halo_ry * 2, 1)}"
                         Opacity="0">
                    <Ellipse.Fill>
                        <RadialGradientBrush>
                            <!-- The SVG's stop-opacity .32 to 0, as colours:
                                 XAML gradient stops carry no opacity of their
                                 own. -->
                            <GradientStop Color="#52F7961E" Offset="0" />
                            <GradientStop Color="#00F7961E" Offset="1" />
                        </RadialGradientBrush>
                    </Ellipse.Fill>
                </Ellipse>

{chr(10).join(f'                <Path x:Name="Slab{index}" Data="{esc(d)}" Fill="{{StaticResource MarkInkBrush}}">' + chr(10) + '                    <Path.RenderTransform>' + chr(10) + f'                        <TranslateTransform x:Name="Slab{index}Shift" />' + chr(10) + '                    </Path.RenderTransform>' + chr(10) + '                </Path>' for index, (_, d) in enumerate(slabs, 1))}

{chr(10).join(f'                <Path x:Name="Bar{index}" Data="{esc(d)}" Fill="{{StaticResource MarkBrandBrush}}">' + chr(10) + '                    <Path.RenderTransform>' + chr(10) + f'                        <TranslateTransform x:Name="Bar{index}Drift" />' + chr(10) + '                    </Path.RenderTransform>' + chr(10) + '                </Path>' for index, (_, d) in enumerate(bars, 1))}

                <Canvas x:Name="Ring">
                    <Canvas.RenderTransform>
                        <ScaleTransform x:Name="RingGrow"
                                        CenterX="{ell_cx}" CenterY="{ell_cy}" />
                    </Canvas.RenderTransform>
{chr(10).join(f'                    <Path x:Name="RingSeg{index}" Data="{esc(d)}" Fill="{{StaticResource MarkRingBrush}}" />' for index, d in enumerate(ring_segments, 1))}
                </Canvas>

{chr(10).join(f'                <Path x:Name="ShineA{index}"' + chr(10) + f'                      Data="{esc(d)}"' + chr(10) + '                      Fill="#FFD37A"' + chr(10) + '                      Opacity="0" />' for index, d in enumerate(ring_segments, 1))}

{chr(10).join(f'                <Path x:Name="ShineB{index}"' + chr(10) + f'                      Data="{esc(d)}"' + chr(10) + '                      Fill="#FFF6D8"' + chr(10) + '                      Opacity="0" />' for index, d in enumerate(ring_segments, 1))}

{chr(10).join(f'                <Ellipse x:Name="Node{index}"' + chr(10) + f'                         Canvas.Left="{round(float(cx) - float(r), 1)}"' + chr(10) + f'                         Canvas.Top="{round(float(cy) - float(r), 1)}"' + chr(10) + f'                         Width="{round(float(r) * 2, 1)}"' + chr(10) + f'                         Height="{round(float(r) * 2, 1)}"' + chr(10) + '                         Fill="{StaticResource MarkBrandBrush}">' + chr(10) + '                    <Ellipse.RenderTransform>' + chr(10) + f'                        <ScaleTransform x:Name="Node{index}Grow" CenterX="{float(r)}" CenterY="{float(r)}" />' + chr(10) + '                    </Ellipse.RenderTransform>' + chr(10) + '                </Ellipse>' for index, (_, _, cx, cy, r) in enumerate(nodes, 1))}
            </Canvas>
        </Canvas>
    </Viewbox>
</UserControl>
'''

# --------------------------------------------------------------------------- #
# EasyNetMark.geometry.cs
# --------------------------------------------------------------------------- #
node_phases = ", ".join(f"{n[0]}" for n in nodes)
node_delays = ", ".join(f"{n[1]}" for n in nodes)
segment_list = ", ".join(f"{p:.4f}" for p in segment_phases)

mark_geometry = f'''// <auto-generated>
// Generated from new_design/easynet_animated_svgs by tools/easynet_geometry.py.
// The SVG stylesheet's own numbers: loop period (--T), the splash's loop
// delay (--ld), and each node's pulse phase (--f) and pop delay (--d). The
// ring segments' own places around the orbit are here too: the shine wave
// walks them in that order.
// </autogenerated>
namespace EasyNet.Controls;

public sealed partial class EasyNetMark
{{
    /// <summary>The loop's period, in seconds -- the SVG's --T.</summary>
    internal const double LoopPeriodSeconds = 2.8;

    /// <summary>The entrance's full length before the loop takes over, in seconds.</summary>
    internal const double EntranceSeconds = 2.3;

    /// <summary>Pulse phases for the nodes, in loop periods -- the SVG's --f.</summary>
    internal static readonly double[] NodePhases = {{ {node_phases} }};

    /// <summary>Pop delays for the nodes' entrance, in seconds -- the SVG's --d.</summary>
    internal static readonly double[] NodePopDelays = {{ {node_delays} }};

    /// <summary>
    /// Each ring segment's place around the orbit, as a fraction of one loop
    /// - the shine wave visits them in this order.
    /// </summary>
    internal static readonly double[] SegmentPhases = {{ {segment_list} }};
}}
'''

# --------------------------------------------------------------------------- #
# EasyNetWordmark.xaml
# --------------------------------------------------------------------------- #
letter_xaml = []
for index, (cls, _, data) in enumerate(letters, 1):
    fill = ("{StaticResource MarkInkBrush}" if cls == "en-ink"
            else "{StaticResource MarkBrandBrush}")
    letter_xaml.append(
        f'                <Path x:Name="Letter{index}"\n'
        f'                      Data="{esc(data)}"\n'
        f'                      Fill="{fill}">\n'
        f'                    <Path.RenderTransform>\n'
        f'                        <TranslateTransform x:Name="Letter{index}Rise" />\n'
        f'                    </Path.RenderTransform>\n'
        f'                </Path>')

word_xaml = f'''<UserControl
    x:Class="EasyNet.Controls.EasyNetWordmark"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    mc:Ignorable="d">

    <!--
        Generated from new_design/easynet_animated_svgs by tools/easynet_geometry.py.
        Seven letterforms: Easy in ink, Net in the brand gradient - the wordmark
        is drawn, not typeset, so it is the same at every size and never depends
        on a font being present. The canvas is measured to the letters' own
        bounds, so a Width on this control scales the word exactly.
    -->
    <Viewbox Stretch="Uniform">
        <Canvas x:Name="Root"
                Width="{word_w}"
                Height="{word_h}">
            <Canvas.Resources>
                <SolidColorBrush x:Key="MarkInkBrush" Color="{INK}" />
                <LinearGradientBrush x:Key="MarkBrandBrush"
                                     StartPoint="1,0" EndPoint="0,1">
                    <GradientStop Color="{GRADIENT_TOP}" Offset="0" />
                    <GradientStop Color="{GRADIENT_BOTTOM}" Offset="1" />
                </LinearGradientBrush>
            </Canvas.Resources>

            <Canvas x:Name="Art"
                    Canvas.Left="{round(-word_left, 1)}"
                    Canvas.Top="{round(-word_top, 1)}">

{chr(10).join(letter_xaml)}
            </Canvas>
        </Canvas>
    </Viewbox>
</UserControl>
'''

word_geometry = f'''// <auto-generated>
// Generated from new_design/easynet_animated_svgs by tools/easynet_geometry.py.
// The SVG stylesheet's letter-rise numbers: the first letter's delay after the
// mark's entrance begins, the stagger between letters, and the rise distance.
// </auto-generated>
namespace EasyNet.Controls;

public sealed partial class EasyNetWordmark
{{
    /// <summary>How many letters the wordmark draws.</summary>
    internal const int LetterCount = {len(letters)};

    /// <summary>The first letter's rise delay, in seconds.</summary>
    internal const double FirstLetterDelaySeconds = 1.15;

    /// <summary>The stagger between one letter and the next, in seconds.</summary>
    internal const double LetterStaggerSeconds = 0.06;

    /// <summary>How far up each letter rises, in art units.</summary>
    internal const double LetterRiseUnits = 32;
}}
'''

for path, text in [
    (os.path.join(OUT_DIR, "EasyNetMark.xaml"), mark_xaml),
    (os.path.join(OUT_DIR, "EasyNetMark.geometry.cs"), mark_geometry),
    (os.path.join(OUT_DIR, "EasyNetWordmark.xaml"), word_xaml),
    (os.path.join(OUT_DIR, "EasyNetWordmark.geometry.cs"), word_geometry),
]:
    with open(path, "w", encoding="utf-8") as handle:
        handle.write(text)
    print(f"wrote {os.path.relpath(path, ROOT)}")

print(f"ring segments in orbit order at phases {['%.2f' % p for p in segment_phases]}")
