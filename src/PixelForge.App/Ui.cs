using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PixelForge.Core;

namespace PixelForge.App;

static class Ui
{
    /// <summary>One-line text prompt. Returns null on cancel.</summary>
    public static string? Ask(DependencyObject owner, string title, string label, string value)
    {
        var box = new TextBox { Text = value, Margin = new Thickness(0, 6, 0, 10) };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        var win = new Window
        {
            Title = title, Width = 320, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(owner),
            Content = new StackPanel
            {
                Margin = new Thickness(12),
                Children =
                {
                    new TextBlock { Text = label }, box,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
                },
            },
        };
        ok.Click += (_, _) => win.DialogResult = true;
        win.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return win.ShowDialog() == true ? box.Text : null;
    }

    /// <summary>
    /// Runs slow work on a worker thread with a small progress window (Cancel button) while the owner stays
    /// responsive but disabled. Returns the result, or default when cancelled; errors from the work are rethrown.
    /// </summary>
    public static async Task<T?> RunLong<T>(Window owner, string title, Func<IProgress<string>, CancellationToken, T> work)
    {
        using var cts = new CancellationTokenSource();
        var text = new TextBlock { Text = "Starting…", TextTrimming = TextTrimming.CharacterEllipsis, Width = 360, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        var bar = new ProgressBar { IsIndeterminate = true, Height = 6, Margin = new Thickness(0, 0, 0, 10) };
        var cancel = new Button { Content = "Cancel", Width = 80, HorizontalAlignment = HorizontalAlignment.Right };
        var win = new Window
        {
            Title = title, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Content = new StackPanel { Margin = new Thickness(14), Children = { text, bar, cancel } },
        };
        cancel.Click += (_, _) => { cts.Cancel(); text.Text = "Cancelling…"; cancel.IsEnabled = false; };
        bool done = false;
        win.Closing += (_, e) => { if (!done) { cts.Cancel(); e.Cancel = true; } };   // closing the window cancels; it closes when the work stops
        var progress = new Progress<string>(m => text.Text = m);
        owner.IsEnabled = false;
        win.Show();
        try { return await Task.Run(() => work(progress, cts.Token)); }
        catch (OperationCanceledException) { return default; }
        finally
        {
            done = true;
            win.Close();
            owner.IsEnabled = true;
            owner.Activate();
        }
    }

    /// <summary>Several labelled fields (a combo box when options are given). Returns the values, or null on cancel.</summary>
    public static string[]? AskFields(DependencyObject owner, string title, params (string Label, string Value, string[]? Options)[] fields)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        var inputs = new List<Control>();
        for (int i = 0; i < fields.Length; i++)
        {
            var (label, value, options) = fields[i];
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Control input = options is null ? new TextBox { Text = value } : new ComboBox { ItemsSource = options, SelectedItem = value };
            input.Margin = new Thickness(0, 3, 0, 3);
            var l = new Label { Content = label, Target = input, Padding = new Thickness(0, 4, 10, 4) };
            Grid.SetRow(l, i);
            Grid.SetRow(input, i);
            Grid.SetColumn(input, 1);
            grid.Children.Add(l);
            grid.Children.Add(input);
            inputs.Add(input);
        }
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        var win = new Window
        {
            Title = title, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(owner),
            Content = new StackPanel
            {
                Margin = new Thickness(12),
                Children = { grid, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } } },
            },
        };
        ok.Click += (_, _) => win.DialogResult = true;
        win.Loaded += (_, _) => inputs.FirstOrDefault()?.Focus();
        return win.ShowDialog() == true ? [.. inputs.Select(c => c is TextBox t ? t.Text : (string?)((ComboBox)c).SelectedItem ?? "")] : null;
    }

    /// <summary>Width/height dialog, optionally with a 3×3 anchor grid. Anchor 0/1/2 = left·top / center / right·bottom.</summary>
    public static (int W, int H, int AnchorX, int AnchorY)? AskSize(DependencyObject owner, string title, int w, int h, bool anchor)
    {
        var wBox = new TextBox { Text = w.ToString(), Width = 60 };
        var hBox = new TextBox { Text = h.ToString(), Width = 60 };
        var anchors = new RadioButton[9];
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 3, Columns = 3, Width = 72, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        for (int i = 0; i < 9; i++) grid.Children.Add(anchors[i] = new RadioButton { GroupName = "anchor", IsChecked = i == 4, Margin = new Thickness(2) });
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        var panel = new StackPanel
        {
            Margin = new Thickness(12),
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = "Width ", VerticalAlignment = VerticalAlignment.Center }, wBox,
                    new TextBlock { Text = "   Height ", VerticalAlignment = VerticalAlignment.Center }, hBox } },
            },
        };
        if (anchor) { panel.Children.Add(new TextBlock { Text = "Keep content at:", Margin = new Thickness(0, 10, 0, 0) }); panel.Children.Add(grid); }
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), Children = { ok, cancel } });
        var win = new Window
        {
            Title = title, Width = 300, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(owner), Content = panel,
        };
        ok.Click += (_, _) =>
        {
            if (int.TryParse(wBox.Text, out int nw) && int.TryParse(hBox.Text, out int nh) && nw is >= 1 and <= 4096 && nh is >= 1 and <= 4096)
                win.DialogResult = true;
            else
                Error(new FormatException("Width and height must be whole numbers from 1 to 4096."));
        };
        win.Loaded += (_, _) => { wBox.Focus(); wBox.SelectAll(); };
        if (win.ShowDialog() != true) return null;
        int a = Array.FindIndex(anchors, r => r.IsChecked == true);
        return (int.Parse(wBox.Text), int.Parse(hBox.Text), a % 3, a / 3);
    }

    public static void Error(Exception ex) =>
        MessageBox.Show(ex.Message, "PixelForge", MessageBoxButton.OK, MessageBoxImage.Error);

    public static SolidColorBrush BrushOf(uint c) => new(Color.FromArgb((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c));

    /// <summary>Tiled grid lines every <paramref name="cell"/> screen pixels.</summary>
    /// <param name="color">Line color incl. alpha; default = black 60 %, at least 3:1 on the light checkerboard (WCAG 1.4.11).</param>
    public static Brush GridBrush(double cell, Color? color = null) =>
        GridBrush(cell, cell, color ?? ColorOf(App.Settings.GridColor));

    public static Color ColorOf(uint c) => Color.FromArgb((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c);

    /// <summary>Grid of w×h cells (screen pixels), shifted by (ox, oy).</summary>
    public static Brush GridBrush(double w, double h, Color color, double ox = 0, double oy = 0)
    {
        // lines on the 0.5 px row/column so the tile does not clip half of them away
        var lines = new GeometryGroup { Children = { new LineGeometry(new(0, 0.5), new(w, 0.5)), new LineGeometry(new(0.5, 0), new(0.5, h)) } };
        return new DrawingBrush(new GeometryDrawing(null, new Pen(new SolidColorBrush(color), 1), lines))
        {
            TileMode = TileMode.Tile, Stretch = Stretch.None,
            Viewport = new Rect(ox, oy, w, h), ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, w, h), ViewboxUnits = BrushMappingMode.Absolute,
        };
    }

    /// <summary>Transparency checkerboard from the preferences.</summary>
    /// <summary>Transparency checkerboard; scale = the canvas zoom when the brush sits inside a zoomed canvas, so squares keep their screen size.</summary>
    public static Brush CheckerBrush(double scale = 1)
    {
        var s = App.Settings;
        double n = s.CheckerSize / scale, d = n * 2;
        var b = new DrawingBrush
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, d, d), ViewportUnits = BrushMappingMode.Absolute,
            Drawing = new DrawingGroup
            {
                Children =
                {
                    new GeometryDrawing(BrushOf(s.CheckerLight), null, new RectangleGeometry(new Rect(0, 0, d, d))),
                    new GeometryDrawing(BrushOf(s.CheckerDark), null, new GeometryGroup { Children = { new RectangleGeometry(new Rect(0, 0, n, n)), new RectangleGeometry(new Rect(n, n, n, n)) } }),
                },
            },
        };
        b.Freeze();
        return b;
    }

    /// <summary>
    /// Fills a layers ListBox, top layer first. <paramref name="rename"/> is called before a name changes
    /// (so the editor can snapshot for undo); <paramref name="visibilityChanged"/> after a toggle.
    /// </summary>
    public static void FillLayers(ListBox list, IReadOnlyList<ILayer> layers, int active, Action visibilityChanged, Action<ILayer, string> rename)
    {
        list.Items.Clear();
        for (int i = layers.Count - 1; i >= 0; i--)
        {
            var layer = layers[i];
            int row = layers.Count - 1 - i;
            var vis = new CheckBox { IsChecked = layer.Visible, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Visible" };
            AutomationProperties.SetName(vis, $"Visible: {layer.Name}");
            vis.Click += (_, _) => { layer.Visible = vis.IsChecked == true; visibilityChanged(); };
            var name = new TextBox { Text = layer.Name, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(4, 0, 0, 0) };
            AutomationProperties.SetName(name, "Layer name");
            name.GotKeyboardFocus += (_, _) => list.SelectedIndex = row;
            name.LostKeyboardFocus += (_, _) =>
            {
                if (name.Text == layer.Name || string.IsNullOrWhiteSpace(name.Text)) { name.Text = layer.Name; return; }
                rename(layer, name.Text);
            };
            DockPanel.SetDock(vis, Dock.Left);
            list.Items.Add(new DockPanel { Children = { vis, name } });
        }
        list.SelectedIndex = layers.Count - 1 - active;
    }

    /// <summary>
    /// Gives symbol-only buttons (+, −, ▲, ◀ …) their tooltip as accessible name, so screen
    /// readers say "Add layer" instead of "plus" (WCAG 4.1.2).
    /// </summary>
    public static void NameSymbolButtons(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            // symbol text (+, ▲ …) or an icon glyph: the tooltip (without the shortcut part) is the name
            if (child is ButtonBase { ToolTip: string tip } b
                && string.IsNullOrEmpty(AutomationProperties.GetName(b))
                && !(b.Content is string text && text.Any(char.IsLetter)))
                AutomationProperties.SetName(b, tip.Split(" (")[0]);
            NameSymbolButtons(child);
        }
    }

    /// <summary>Menu command; <paramref name="gesture"/> is only shown (the editor handles the key).</summary>
    public static MenuItem Item(string header, string? gesture, Action action, bool enabled = true)
    {
        var m = new MenuItem { Header = header, InputGestureText = gesture ?? "", IsEnabled = enabled };
        m.Click += (_, _) => action();
        return m;
    }

    /// <summary>Checkable menu item that stays in sync with a toolbar CheckBox and runs its Click handler.</summary>
    public static MenuItem Toggle(string header, string? gesture, ToggleButton box)
    {
        var m = new MenuItem { Header = header, InputGestureText = gesture ?? "", IsCheckable = true };
        m.SetBinding(MenuItem.IsCheckedProperty, new System.Windows.Data.Binding(nameof(CheckBox.IsChecked)) { Source = box, Mode = System.Windows.Data.BindingMode.TwoWay });
        m.Click += (_, _) => box.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        return m;
    }

    /// <summary>File → Open recent: refilled from settings each time it opens; only files this tab can open.</summary>
    public static MenuItem RecentMenu(IEditor editor)
    {
        var m = new MenuItem { Header = "Open _recent" };
        m.Items.Add(new MenuItem { Header = "(none)", IsEnabled = false });   // so the submenu arrow shows
        m.SubmenuOpened += (_, _) =>
        {
            m.Items.Clear();
            var files = App.Settings.RecentFiles
                .Where(p => editor.FileExtensions.Contains(Path.GetExtension(p).ToLowerInvariant()) && File.Exists(p))
                .ToList();
            for (int i = 0; i < files.Count; i++)
            {
                string path = files[i];
                // number = access key; underscores in names are doubled so they stay visible
                m.Items.Add(Item($"_{(i + 1) % 10} {Path.GetFileName(path).Replace("_", "__")}", null, () => editor.OpenPath(path)));
                ((MenuItem)m.Items[^1]).ToolTip = path;
            }
            if (files.Count == 0) m.Items.Add(new MenuItem { Header = "(none)", IsEnabled = false });
        };
        return m;
    }

    public static MenuItem Menu(string header, params object[] items)
    {
        var m = new MenuItem { Header = header };
        foreach (var i in items) m.Items.Add(i);
        return m;
    }

    /// <summary>Marks a field as invalid (red border + message), or clears the mark when message is null (WCAG 3.3.1).</summary>
    public static void MarkInvalid(TextBox box, string? message)
    {
        if (message is null)
        {
            box.ClearValue(Control.BorderBrushProperty);
            box.ClearValue(Control.BorderThicknessProperty);
            if (box.Tag is string original) box.ToolTip = original;
            AutomationProperties.SetHelpText(box, "");
            return;
        }
        if (box.Tag is null && box.ToolTip is string tip) box.Tag = tip;
        box.BorderBrush = Brushes.Red;
        box.BorderThickness = new Thickness(2);
        box.ToolTip = message;
        AutomationProperties.SetHelpText(box, message);
    }

    /// <summary>A color swatch that is a real button: focusable, Enter/Space/click = action (WCAG 2.1.1).</summary>
    public static Button Swatch(uint color, double size, string name)
    {
        var b = new Button
        {
            Width = size, Height = size, Margin = new Thickness(1), Background = BrushOf(color),
            BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), ToolTip = name,
            Template = (ControlTemplate)Application.Current.FindResource("SwatchTemplate"),
        };
        AutomationProperties.SetName(b, name);
        return b;
    }

    public static string ColorName(uint c) => (c >> 24) == 0xFF ? $"#{c & 0xFFFFFF:X6}" : (c >> 24) == 0 ? "transparent" : $"#{c & 0xFFFFFF:X6}, {(c >> 24) * 100 / 255}% opaque";
}
