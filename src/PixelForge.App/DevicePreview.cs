using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PixelForge.Core;

namespace PixelForge.App;

/// <summary>
/// Device preview (View, Ctrl+Shift+D): the current picture inside the game's view as a phone, tablet or monitor would
/// show it (whole-number scaling, like a pixel-perfect game), with the real on-screen size in pixels and centimetres.
/// </summary>
static class DevicePreview
{
    sealed record Device(string Name, int W, int H, double Dpi);

    static readonly Device[] Devices =
    [
        new("Monitor 1920 × 1080 (24\")", 1920, 1080, 92), new("Laptop 1366 × 768 (14\")", 1366, 768, 112), new("Monitor 4K 3840 × 2160 (27\")", 3840, 2160, 163),
        new("Phone 2400 × 1080 landscape", 2400, 1080, 400), new("Phone 1080 × 2400 portrait", 1080, 2400, 400), new("Tablet 2048 × 1536", 2048, 1536, 264),
        new("Handheld 1280 × 800 (7\")", 1280, 800, 215),
    ];

    public static void Show(Window owner, (uint[] Pixels, int Width, int Height) picture, string what)
    {
        int gw = App.Project?.Settings.TargetWidth ?? 320, gh = App.Project?.Settings.TargetHeight ?? 180;
        var deviceBox = new ComboBox { ItemsSource = Devices.Select(d => d.Name).ToList(), SelectedIndex = 0, Width = 260 };
        AutomationProperties.SetName(deviceBox, "Device");
        var integer = new CheckBox { Content = "Whole-number scaling (pixel perfect)", IsChecked = true, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
        AutomationProperties.SetLiveSetting(info, AutomationLiveSetting.Polite);
        var stage = new Canvas { Background = Brushes.Black, ClipToBounds = true };
        var frame = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(2), Child = stage, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Children = { deviceBox, integer } };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(info, Dock.Top);
        var win = new Window
        {
            Title = $"Device preview — {what}", Width = 960, Height = 720, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel { Margin = new Thickness(12), Children = { top, info, frame } },
        };
        var bmp = ImageIO.ToBitmap(picture.Pixels, picture.Width, picture.Height);
        bmp.Freeze();

        void Draw()
        {
            var d = Devices[deviceBox.SelectedIndex];
            double fit = Math.Min((double)d.W / gw, (double)d.H / gh);
            double scale = integer.IsChecked == true ? Math.Max(1, Math.Floor(fit)) : fit;   // device pixels per game pixel
            double viewW = gw * scale, viewH = gh * scale;
            // the device screen, shrunk to fit the window
            double shrink = Math.Min((win.ActualWidth - 60) / d.W, (win.ActualHeight - 160) / d.H);
            if (shrink <= 0) return;
            stage.Width = d.W * shrink;
            stage.Height = d.H * shrink;
            stage.Children.Clear();
            var view = new Rectangle { Width = viewW * shrink, Height = viewH * shrink, Fill = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x30)) };
            Canvas.SetLeft(view, (d.W - viewW) / 2 * shrink);
            Canvas.SetTop(view, (d.H - viewH) / 2 * shrink);
            stage.Children.Add(view);
            // the picture in the middle of the game view at its game size
            double pw = Math.Min(picture.Width, gw) * scale * shrink, ph = Math.Min(picture.Height, gh) * scale * shrink;
            var img = new Image { Source = bmp, Width = pw, Height = ph, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(img, (d.W - pw / shrink) / 2 * shrink);
            Canvas.SetTop(img, (d.H - ph / shrink) / 2 * shrink);
            stage.Children.Add(img);
            double cm = picture.Width * scale / d.Dpi * 2.54;
            info.Text = $"Game view {gw}×{gh} shown at {scale:0.##}× = {viewW:0}×{viewH:0} device pixels" + (viewW < d.W || viewH < d.H ? $" (black bars {d.W - viewW:0}×{d.H - viewH:0})" : "") +
                        $". The {picture.Width}×{picture.Height} picture is {picture.Width * scale:0}×{picture.Height * scale:0} device pixels, about {cm:0.#} cm wide on this screen" +
                        (integer.IsChecked != true && scale % 1 != 0 ? ". Fractional scaling makes pixel art uneven." : ".");
        }

        deviceBox.SelectionChanged += (_, _) => Draw();
        integer.Click += (_, _) => Draw();
        win.SizeChanged += (_, _) => Draw();
        win.Show();
    }
}
