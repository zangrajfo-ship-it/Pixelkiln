using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Pixelkiln.App;

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
        /* simplified app: shortcuts of the switched-off modes
        ("Sprite › Paint (Ctrl+2)", [
            ("B N E A S I G", "Brush, pencil, eraser, airbrush, smudge, picker, gradient"),
            ("Alt+click", "Pick a colour"), ("X", "Swap primary and secondary colour"),
            ("[ / ]", "Smaller / larger brush"), ("M", "Paint on the layer mask (brush hides, eraser reveals)"),
            ("Space+drag, middle drag", "Pan"), ("Ctrl+wheel, Ctrl+= / Ctrl+-", "Zoom"),
            ("Pen", "Pressure sets size and/or opacity; the pen's eraser end erases"),
            ("Ctrl+Shift+N / Ctrl+E", "New layer / export PNG"),
        ]),
        ("Sprite › Vector (Ctrl+3)", [
            ("V A P R E Y L", "Select, nodes, pen, rectangle, ellipse, polygon, line"),
            ("Shift+click / drag on empty space", "Add to selection / select with a box"),
            ("Handles (Shift: keep ratio)", "Scale the selection"), ("Arrows / Shift+arrows", "Move by 1 / 10"),
            ("Pen: click / drag / Enter", "Corner point / curve point / finish (click the first point to close)"),
            ("Nodes: double-click / Alt+drag handle / Del", "Smooth or corner / move one handle / delete point"),
            ("Ctrl+G / Ctrl+Shift+G / Ctrl+D", "Group / ungroup / duplicate"), ("Ctrl+Shift+] / [", "Bring to front / send to back"),
        ]),
        ("Sprite › Texture (Ctrl+4)", [
            ("1 2 3 4 5", "Show colour, height, normal, occlusion, lit"), ("T", "Repeat 4×4 on or off"),
            ("R", "New random seed"), ("Ctrl+E", "Export colour, normal, height and occlusion PNGs"),
            ("Paint it…", "Paint the texture in Paint mode; strokes wrap around the edges (W toggles wrap there)"),
        ]),
        ("Sprite › UI (Ctrl+5)", [
            ("Elements / Screens lists", "Edit one element (all four states and a stretched copy) or lay out a screen"),
            ("P / Del", "Place the selected element on the screen / remove it"), ("Arrows / Shift+arrows", "Move by 1 / 8 px"),
            ("Preview at", "See a screen at another resolution; anchors move elements as in the game"),
            ("Ctrl+Shift+D", "Device preview of what you are editing (any mode)"),
        ]),
        */
        ("Tilemap", [
            ("B E G I", "Paint, erase, fill, pick"), ("A", "Auto-tile terrain"), ("O", "Objects"),
            ("Shift (objects)", "Snap to tiles"), ("Del", "Delete selected object"),
            ("Tileset: arrows / Shift+arrows", "Select tile / several tiles"),
            ("Ctrl+= / Ctrl+- / Ctrl+0", "Zoom in / out / fit"), ("Ctrl+G", "Grid"),
        ]),
        /* simplified app: shortcuts of the switched-off tabs and modes
        ("Scene", [
            ("V I A C", "Select, place object, draw area, set camera start"),
            ("Arrows / Shift+arrows", "Move selection by 1 px / one tile"), ("Shift (drag)", "Ignore tile snapping"),
            ("Del", "Delete selected object or area"), ("Space", "Play animations and movement tracks"),
            ("F5", "Game preview: ←→ move, Space jump, R reset, H hitboxes, M mute, P pause"),
            ("Ctrl+= / Ctrl+- / Ctrl+0", "Zoom in / out / fit"),
            ("Motion clip / Camera motion", "Play a clip from an animation library instead of the object's track, or on the camera"),
            ("Lighting and effects", "Ambient light, lights, particles and occluders; drag their markers, double-click to edit"),
            ("Show lighting and particles", "Draw the scene as the game shows it (Space plays it)"),
        ]),
        ("Scene › Motion (Ctrl+2)", [
            ("Double-click a row / drag a key", "Add a key / move it in time (10 ms steps)"),
            ("Selected key", "Value, curve to the next key, Bezier handles (x1 y1 x2 y2 as in CSS)"),
            ("Add track…", "Target: empty = object, camera or UI element; a body part's name for rigs"),
            ("Space / Home / Del", "Play or stop / rewind / delete the selected key"),
        ]),
        ("Scene › Logic (Ctrl+3)", [
            ("Palette buttons", "Add an event (blue), condition (orange) or action (green) node"),
            ("Drag title / drag from a port dot", "Move a node / link it to another node (green = true, red = false)"),
            ("Click a link, Del", "Delete the link (or the selected node)"),
            ("Layout › Logic, then F5", "Run the graph in the game preview; variables and items show in its status line"),
        ]),
        ("Character › Rig (Ctrl+2)", [
            ("Drag / right-click on the sprite", "Cut a body part / set the selected part's pivot"),
            ("Enter / Del", "Edit the part (parent, offset, Z) / delete it"),
            ("Clip per state", "Which clip of the animation library each state plays; Space plays the preview"),
        ]),
        ("Audio", [
            ("Space / L / Home", "Play or stop / loop region / playhead to start"),
            ("Click on the timeline", "Playhead (or select a clip)"), ("Drag a clip", "Move; snaps to clip edges, Shift = free"),
            ("← → / Shift+← →", "Move clip by 10 / 100 ms"), ("↑ ↓", "Move clip to the track above / below"),
            ("[ ]", "Select previous / next clip"), ("S / Del", "Split at playhead / delete clip"),
            ("Library: Enter", "Add to the selected track at the playhead"), ("Ctrl+E", "Export WAV"),
        ]),
        ("Music", [
            ("Space", "Play / stop song"), ("1 2 3 4", "Pulse 1 (melody), pulse 2 (chords), triangle (bass), drum kit"),
            ("Channel 4", "Each row is a drum: kick, snare, clap, hi-hat, open hat, toms, crash (Drum kit box off = old pitched noise)"),
            ("Pattern › Drum beat", "Write a ready beat (rock, hip-hop, four on the floor …) into the current pattern"),
            ("Pattern › Generate song…", "A whole song from a seed: melody, chords, bass and drums"),
            ("Piano roll: arrows", "Move cursor"), ("Enter", "Add / remove note"),
            ("Shift+← →", "Shorter / longer note"), ("+ / −", "Note volume"), ("Del", "Remove note"),
        ]),
        ("SFX", [("Space", "Play sound"), ("Preset buttons / Mutate", "Coin, jump, hit … / a variation of the current sound")]),
        */
        ("Learning", [("Help › Getting started", "Three short lessons; each opens part of the sample game"), ("Help › Open sample game", "A small finished game to look inside and change")]),
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
