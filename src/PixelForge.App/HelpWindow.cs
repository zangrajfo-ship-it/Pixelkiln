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
            ("Alt", "Menu"), ("Tab / Shift+Tab", "Move between controls"),
        ]),
        ("Sprite", [
            ("B E G I", "Pencil, eraser, fill, color picker"), ("L U O", "Line, rectangle, ellipse (Shift: 45° / square / circle)"),
            ("M", "Select"), ("W", "Magic wand"), ("[ ]", "Smaller / bigger brush"),
            ("Ctrl+A / C / X / V", "Select all, copy, cut, paste (also from other apps)"),
            ("Del", "Delete selection"), ("H / V / R", "Flip selection horizontally / vertically, rotate 90°"),
            ("Enter / Esc", "Apply / cancel moved pixels"),
            ("Ctrl+= / Ctrl+- / Ctrl+0", "Zoom in / out / fit to window"), ("Mouse wheel, middle drag", "Zoom, pan"),
            ("Ctrl+G", "Pixel grid"), ("Space", "Play animation"), ("← →", "Previous / next frame"),
            ("Palette: Enter / Shift+Enter / Del", "Use as left / right color, remove"),
        ]),
        ("Tilemap", [
            ("B E G I", "Paint, erase, fill, pick"), ("A", "Auto-tile terrain"), ("O", "Objects"),
            ("Shift (objects)", "Snap to tiles"), ("Del", "Delete selected object"),
            ("Tileset: arrows / Shift+arrows", "Select tile / several tiles"),
            ("Ctrl+= / Ctrl+- / Ctrl+0", "Zoom in / out / fit"), ("Ctrl+G", "Grid"),
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
