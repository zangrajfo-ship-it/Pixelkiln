using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace PixelForge.App;

/// <summary>F1: every shortcut in one place.</summary>
static class HelpWindow
{
    static readonly (string Section, (string Keys, string What)[] Rows)[] Shortcuts =
    [
        ("Everywhere", [
            ("F1", "This list"), ("Ctrl+N / Ctrl+O / Ctrl+S", "New / open / save (current tab)"),
            ("Ctrl+Z / Ctrl+Y", "Undo / redo"), ("Ctrl+, ", "Preferences"),
            ("Ctrl+Shift+P", "Command palette: find and run any command or open any asset"),
            ("Ctrl+Shift+B", "Show / hide the asset browser"),
            ("Ctrl+H", "History of the selected asset: compare versions, restore"),
            ("Ctrl+T", "Template Studio: new characters, levels, items and music from templates"),
            ("Ctrl+1 … Ctrl+9", "Editing mode of the current tab (when it has several)"),
            ("Ctrl+Alt+C / Ctrl+Alt+V", "Copy / paste style (palette and colors, object or area settings, character kind and properties)"),
            ("Rulers: click / right-click", "Add a guide / remove the nearest guide"),
            ("Asset browser: Ctrl/Shift+click", "Select several assets; right-click → Selected assets for batch rename, tags, scale, convert"),
            ("Ctrl+Shift+E", "Export (profiles, Godot 4, generic)"),
            ("Asset browser: Enter / F2 / Del / Ctrl+D / Space", "Open, rename, remove, favorite, preview"),
            ("Alt", "Menu"), ("Tab / Shift+Tab", "Move between controls"),
        ]),
        ("Sprite", [
            ("B E G I", "Pencil, eraser, fill, color picker"), ("L U O", "Line, rectangle, ellipse (Shift: 45° / square / circle)"),
            ("M", "Select"), ("W", "Magic wand"), ("[ ]", "Smaller / bigger brush"),
            ("Ctrl+A / C / X / V", "Select all, copy, cut, paste (also from other apps)"),
            ("Del", "Delete selection"), ("H / V / R", "Flip selection horizontally / vertically, rotate 90°"),
            ("Enter / Esc", "Apply / cancel moved pixels"),
            ("Ctrl+= / Ctrl+- / Ctrl+0", "Zoom in / out / fit to window"), ("Mouse wheel, middle drag", "Zoom, pan"),
            ("Ctrl+G / Ctrl+Shift+G", "Pixel grid / tile grid"), ("T", "Tiled mode (seamless tiles)"), ("P", "Preview window"),
            ("Shift+click (wand)", "Add to selection"),
            ("Space", "Play animation (the chosen tag only)"), ("← →", "Previous / next frame"),
            ("Palette: Enter / Shift+Enter / Del", "Use as left / right color, remove"),
        ]),
        ("Sprite › Paint (Ctrl+2)", [
            ("B N E A S I G", "Brush, pencil, eraser, airbrush, smudge, picker, gradient"),
            ("Alt+click", "Pick a colour"), ("X", "Swap primary and secondary colour"),
            ("[ / ]", "Smaller / larger brush"), ("M", "Paint on the layer mask (brush hides, eraser reveals)"),
            ("Space+drag, middle drag", "Pan"), ("Ctrl+wheel, Ctrl+= / Ctrl+-", "Zoom"),
            ("Pen", "Pressure sets size and/or opacity; the pen's eraser end erases"),
            ("Ctrl+Shift+N / Ctrl+E", "New layer / export PNG"),
        ]),
        ("Tilemap", [
            ("B E G I", "Paint, erase, fill, pick"), ("A", "Auto-tile terrain"), ("O", "Objects"),
            ("Shift (objects)", "Snap to tiles"), ("Del", "Delete selected object"),
            ("Tileset: arrows / Shift+arrows", "Select tile / several tiles"),
            ("Ctrl+= / Ctrl+- / Ctrl+0", "Zoom in / out / fit"), ("Ctrl+G", "Grid"),
        ]),
        ("Scene", [
            ("V I A C", "Select, place object, draw area, set camera start"),
            ("Arrows / Shift+arrows", "Move selection by 1 px / one tile"), ("Shift (drag)", "Ignore tile snapping"),
            ("Del", "Delete selected object or area"), ("Space", "Play animations and movement tracks"),
            ("F5", "Game preview: ←→ move, Space jump, R reset, H hitboxes, M mute, P pause"),
            ("Ctrl+= / Ctrl+- / Ctrl+0", "Zoom in / out / fit"),
        ]),
        ("Audio", [
            ("Space / L / Home", "Play or stop / loop region / playhead to start"),
            ("Click on the timeline", "Playhead (or select a clip)"), ("Drag a clip", "Move; snaps to clip edges, Shift = free"),
            ("← → / Shift+← →", "Move clip by 10 / 100 ms"), ("↑ ↓", "Move clip to the track above / below"),
            ("[ ]", "Select previous / next clip"), ("S / Del", "Split at playhead / delete clip"),
            ("Library: Enter", "Add to the selected track at the playhead"), ("Ctrl+E", "Export WAV"),
        ]),
        ("Music", [
            ("Space", "Play / stop song"), ("1 2 3 4", "Pulse 1, pulse 2, triangle, noise"),
            ("Piano roll: arrows", "Move cursor"), ("Enter", "Add / remove note"),
            ("Shift+← →", "Shorter / longer note"), ("+ / −", "Note volume"), ("Del", "Remove note"),
        ]),
        ("SFX", [("Space", "Play sound")]),
    ];

    public static void Show(Window owner)
    {
        var doc = new StackPanel { Margin = new Thickness(16) };
        foreach (var (section, rows) in Shortcuts)
        {
            doc.Children.Add(new TextBlock { Text = section, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
            var grid = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(260) }, new ColumnDefinition() } };
            for (int i = 0; i < rows.Length; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition());
                var keys = new TextBlock { Text = rows[i].Keys, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Margin = new Thickness(0, 2, 12, 2) };
                var what = new TextBlock { Text = rows[i].What, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
                Grid.SetRow(keys, i);
                Grid.SetRow(what, i);
                Grid.SetColumn(what, 1);
                grid.Children.Add(keys);
                grid.Children.Add(what);
            }
            doc.Children.Add(grid);
        }
        new Window
        {
            Title = "Keyboard shortcuts", Width = 680, Height = 720, Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer { Content = doc, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        }.Show();
    }
}
