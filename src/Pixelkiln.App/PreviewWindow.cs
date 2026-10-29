using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pixelkiln.Core;

namespace Pixelkiln.App;

/// <summary>Small floating preview at 1×/2×/4×: follows the current frame, or plays the animation on its own.</summary>
sealed class PreviewWindow : Window
{
    readonly Func<Sprite> _sprite;
    readonly Func<(int From, int To)> _range;
    readonly Image _image = new() { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly ToggleButton _play = new() { Content = "▶ Play", Margin = new Thickness(6, 0, 0, 0), ToolTip = "Play the animation here while you keep drawing" };
    readonly DispatcherTimer _timer = new();
    WriteableBitmap? _bmp;
    int _frame;

    public PreviewWindow(Func<Sprite> sprite, Func<(int From, int To)> range)
    {
        (_sprite, _range) = (sprite, range);
        Title = "Preview";
        WindowStyle = WindowStyle.ToolWindow;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 220;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);

        var scale = new ComboBox { ItemsSource = new[] { "1×", "2×", "4×" }, SelectedIndex = 1, Width = 72 };
        AutomationProperties.SetName(scale, "Preview size");
        scale.SelectionChanged += (_, _) =>
        {
            double f = scale.SelectedIndex switch { 0 => 1, 1 => 2, _ => 4 };
            _image.LayoutTransform = new ScaleTransform(f, f);
        };
        _image.LayoutTransform = new ScaleTransform(2, 2);

        _timer.Tick += (_, _) => Step();
        _play.Click += (_, _) =>
        {
            if (_play.IsChecked == true) { _frame = _range().From; Step(); _timer.Start(); _play.Content = "■ Stop"; }
            else { _timer.Stop(); _play.Content = "▶ Play"; }
        };
        Closed += (_, _) => _timer.Stop();

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8), Children = { scale, _play } };
        DockPanel.SetDock(top, Dock.Top);
        Content = new DockPanel
        {
            Children =
            {
                top,
                new Border { Background = Ui.CheckerBrush(), Margin = new Thickness(8, 0, 8, 8), Padding = new Thickness(4), Child = _image },
            },
        };
    }

    /// <summary>Shows the editor's current frame (ignored while the preview plays on its own).</summary>
    public void ShowFrame(uint[] px, int w, int h)
    {
        if (_play.IsChecked != true) Draw(px, w, h);
    }

    void Step()
    {
        var s = _sprite();
        var (from, to) = _range();
        _frame = _frame < from || _frame >= to ? from : _frame + 1;
        _frame = Math.Min(_frame, s.Frames.Count - 1);
        Draw(Compositor.Flatten(s, _frame), s.Width, s.Height);
        _timer.Interval = TimeSpan.FromMilliseconds(s.Frames[_frame].DurationMs);
    }

    void Draw(uint[] px, int w, int h)
    {
        if (_bmp is null || _bmp.PixelWidth != w || _bmp.PixelHeight != h)
            _image.Source = _bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        _bmp.WritePixels(new Int32Rect(0, 0, w, h), px, w * 4, 0);
    }
}
