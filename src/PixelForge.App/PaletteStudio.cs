using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using PixelForge.Core;

namespace PixelForge.App;

/// <summary>
/// Project → Palette studio: checks sprites against a palette asset, snaps or recolors several sprites at once
/// with a before/after preview, and can undo the last apply (the old file contents are kept in memory).
/// </summary>
static class PaletteStudio
{
    /// <param name="canTouch">False for files that may not be changed now (open with unsaved changes elsewhere).</param>
    /// <param name="changed">Called for every file that was rewritten.</param>
    public static void Show(Window owner, Project p, Func<string, bool> canTouch, Action<string> changed)
    {
        var palettes = p.Assets.Where(a => a.Type == AssetType.Palette).OrderBy(a => a.Path).ToList();
        var sprites = p.Assets.Where(a => a.Type == AssetType.Sprite && File.Exists(p.FullPath(a))).OrderBy(a => a.Path).ToList();
        if (palettes.Count == 0) { Ui.Error(new InvalidOperationException("Add a palette (.hex or .gpl) to the project first, e.g. from the Sprite tab: Edit → Palette → Save.")); return; }

        var paletteBox = new ComboBox { ItemsSource = palettes.Select(a => a.Path).ToList(), SelectedIndex = 0 };
        AutomationProperties.SetName(paletteBox, "Palette");
        var list = new ListBox { MinHeight = 200 };
        AutomationProperties.SetName(list, "Sprites; checked ones are changed by Apply");
        var snap = new RadioButton { Content = "Snap colors outside the palette to the nearest palette color", IsChecked = true, GroupName = "op" };
        var replace = new RadioButton { Content = "Replace one color:", GroupName = "op" };
        var fromBox = new TextBox { Width = 90, Text = "#000000" };
        var toBox = new TextBox { Width = 90, Text = "#000000" };
        AutomationProperties.SetName(fromBox, "Color to replace (hex)");
        AutomationProperties.SetName(toBox, "New color (hex)");
        var before = new Image { Height = 128, Stretch = Stretch.Uniform };
        var after = new Image { Height = 128, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(before, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(after, BitmapScalingMode.NearestNeighbor);
        AutomationProperties.SetName(before, "Before");
        AutomationProperties.SetName(after, "After");
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
        var apply = new Button { Content = "Apply to checked sprites", Margin = new Thickness(0, 0, 8, 0) };
        var undo = new Button { Content = "Undo last apply", IsEnabled = false, Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 80 };

        var checks = new Dictionary<Guid, CheckBox>();
        foreach (var s in sprites)
        {
            var cb = new CheckBox { Content = s.Path, Tag = s };
            checks[s.Id] = cb;
            list.Items.Add(cb);
        }

        IReadOnlyList<uint> Palette() => PaletteIO.Load(p.FullPath(palettes[paletteBox.SelectedIndex]));

        /// <summary>The chosen operation on a copy; null when the hex fields are invalid.</summary>
        int? Run(Sprite s)
        {
            if (snap.IsChecked == true) return PaletteTools.SnapToPalette(s, Palette());
            bool ok1 = ColorUtil.TryParseHex(fromBox.Text, out uint a), ok2 = ColorUtil.TryParseHex(toBox.Text, out uint b);
            Ui.MarkInvalid(fromBox, ok1 ? null : "RRGGBB or AARRGGBB");
            Ui.MarkInvalid(toBox, ok2 ? null : "RRGGBB or AARRGGBB");
            return ok1 && ok2 ? PaletteTools.Replace(s, a, b) : null;
        }

        void Refresh()
        {
            try
            {
                var pal = Palette();
                foreach (var cb in checks.Values)
                {
                    var a = (AssetEntry)cb.Tag;
                    int off = PaletteTools.OutOfPalette(ProjectFile.Load(p.FullPath(a)), pal).Values.Sum();
                    cb.Content = off == 0 ? $"{a.Path}  ✓" : $"{a.Path}  ({off} px outside)";
                    if (off > 0 && snap.IsChecked == true) cb.IsChecked ??= true;
                }
                if (list.SelectedItem is CheckBox { Tag: AssetEntry sel })
                {
                    var s = ProjectFile.Load(p.FullPath(sel));
                    before.Source = ImageIO.ToBitmap(Compositor.Flatten(s, 0), s.Width, s.Height);
                    int? n = Run(s);
                    after.Source = ImageIO.ToBitmap(Compositor.Flatten(s, 0), s.Width, s.Height);
                    info.Text = n is int changedPx ? $"{changedPx} pixel(s) would change in “{sel.Name}” (all frames and layers)." : "Fix the colors above.";
                }
                else info.Text = "Select a sprite to preview the change.";
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException) { info.Text = ex.Message; }
        }

        var backups = new List<(string Path, string Text)>();
        apply.Click += (_, _) =>
        {
            var targets = checks.Values.Where(c => c.IsChecked == true).Select(c => (AssetEntry)c.Tag).ToList();
            if (targets.Count == 0) { info.Text = "Check the sprites to change."; return; }
            backups.Clear();
            var skipped = new List<string>();
            int total = 0;
            foreach (var a in targets)
            {
                string path = p.FullPath(a);
                if (!canTouch(path)) { skipped.Add($"{a.Name} (unsaved in the Sprite tab)"); continue; }
                try
                {
                    string old = File.ReadAllText(path);
                    var s = ProjectFile.Deserialize(old);
                    if (Run(s) is not int n) return;
                    if (n == 0) continue;
                    backups.Add((path, old));
                    ProjectFile.Save(s, path);
                    total += n;
                    changed(path);
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { skipped.Add($"{a.Name} ({ex.Message})"); }
            }
            undo.IsEnabled = backups.Count > 0;
            App.NotifyProjectChanged();
            Refresh();
            info.Text = $"Changed {total} pixel(s) in {backups.Count} sprite(s)." + (skipped.Count > 0 ? " Skipped: " + string.Join("; ", skipped) : "");
        };
        undo.Click += (_, _) =>
        {
            foreach (var (path, text) in backups)
            {
                if (!canTouch(path)) continue;
                DocumentFile.WriteAtomic(path, text);
                changed(path);
            }
            info.Text = $"Restored {backups.Count} sprite(s).";
            backups.Clear();
            undo.IsEnabled = false;
            App.NotifyProjectChanged();
            Refresh();
        };
        paletteBox.SelectionChanged += (_, _) => Refresh();
        list.SelectionChanged += (_, _) => Refresh();
        snap.Click += (_, _) => Refresh();
        replace.Click += (_, _) => Refresh();
        fromBox.LostKeyboardFocus += (_, _) => Refresh();
        toBox.LostKeyboardFocus += (_, _) => Refresh();

        var left = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
        var top = new StackPanel { Children = { new Label { Content = "_Palette", Target = paletteBox, Padding = new Thickness(0) }, paletteBox, new TextBlock { Text = "Sprites", Margin = new Thickness(0, 8, 0, 2) } } };
        DockPanel.SetDock(top, Dock.Top);
        left.Children.Add(top);
        left.Children.Add(list);
        var right = new StackPanel
        {
            Width = 320,
            Children =
            {
                snap,
                new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Children = { replace, fromBox, new TextBlock { Text = " → ", VerticalAlignment = VerticalAlignment.Center }, toBox } },
                new TextBlock { Text = "Before / after (frame 1)", Margin = new Thickness(0, 12, 0, 4) },
                new StackPanel { Orientation = Orientation.Horizontal, Children = { new Border { Background = Ui.CheckerBrush(), Margin = new Thickness(0, 0, 8, 0), Child = before }, new Border { Background = Ui.CheckerBrush(), Child = after } } },
                info,
            },
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), Children = { apply, undo, close } };
        var grid = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(right, Dock.Right);
        grid.Children.Add(buttons);
        grid.Children.Add(right);
        grid.Children.Add(left);
        var win = new Window { Title = "Palette studio", Width = 820, Height = 560, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = grid };
        win.Loaded += (_, _) => Refresh();
        win.ShowDialog();
    }
}
