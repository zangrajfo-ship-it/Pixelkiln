using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PixelForge.Core;
using WinForms = System.Windows.Forms;

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

    /// <summary>Windows color picker. Returns opaque ARGB or null on cancel.</summary>
    public static uint? PickColor(uint initial)
    {
        using var dlg = new WinForms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb((int)(initial | 0xFF000000)) };
        return dlg.ShowDialog() == WinForms.DialogResult.OK ? (uint)dlg.Color.ToArgb() | 0xFF000000 : null;
    }

    public static void Error(Exception ex) =>
        MessageBox.Show(ex.Message, "PixelForge", MessageBoxButton.OK, MessageBoxImage.Error);

    public static SolidColorBrush BrushOf(uint c) => new(Color.FromArgb((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c));

    /// <summary>Tiled grid lines every <paramref name="cell"/> screen pixels.</summary>
    public static Brush GridBrush(double cell)
    {
        // lines on the 0.5 px row/column so the tile does not clip half of them away
        var lines = new GeometryGroup { Children = { new LineGeometry(new(0, 0.5), new(cell, 0.5)), new LineGeometry(new(0.5, 0), new(0.5, cell)) } };
        return new DrawingBrush(new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), 1), lines))
        {
            TileMode = TileMode.Tile, Stretch = Stretch.None,
            Viewport = new Rect(0, 0, cell, cell), ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, cell, cell), ViewboxUnits = BrushMappingMode.Absolute,
        };
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
            vis.Click += (_, _) => { layer.Visible = vis.IsChecked == true; visibilityChanged(); };
            var name = new TextBox { Text = layer.Name, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(4, 0, 0, 0) };
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
}
