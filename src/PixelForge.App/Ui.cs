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
