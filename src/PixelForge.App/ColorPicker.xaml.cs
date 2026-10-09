using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PixelForge.Core;

namespace PixelForge.App;

/// <summary>HSV + alpha color picker. Raises <see cref="ColorChanged"/> only for user edits.</summary>
public partial class ColorPicker : UserControl
{
    double _h, _s, _v = 0;
    byte _a = 255;
    bool _updating;

    public event Action<uint>? ColorChanged;

    public ColorPicker()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateVisuals();
    }

    /// <summary>ARGB. Setting it updates the picker without raising <see cref="ColorChanged"/>.</summary>
    public uint Color
    {
        get => ColorUtil.FromHsv(_h, _s, _v, _a);
        set
        {
            if (value == Color) return;
            var (h, s, v) = ColorUtil.ToHsv(value);
            if (s > 0 && v > 0) _h = h;   // keep the hue for greys and black
            if (v > 0) _s = s;
            (_v, _a) = (v, (byte)(value >> 24));
            UpdateVisuals();
        }
    }

    void Edit()
    {
        UpdateVisuals();
        ColorChanged?.Invoke(Color);
    }

    void UpdateVisuals()
    {
        _updating = true;
        uint c = Color;
        HueFill.Fill = Ui.BrushOf(ColorUtil.FromHsv(_h, 1, 1));
        double w = SvArea.ActualWidth, h = SvArea.ActualHeight;
        Canvas.SetLeft(SvMarker, _s * w - 5.5);
        Canvas.SetTop(SvMarker, (1 - _v) * h - 5.5);
        Canvas.SetLeft(SvMarkerIn, _s * w - 4.5);
        Canvas.SetTop(SvMarkerIn, (1 - _v) * h - 4.5);
        Canvas.SetLeft(HueMarker, 0);
        Canvas.SetTop(HueMarker, _h / 360 * HueArea.ActualHeight - 2);
        AlphaSlider.Value = _a;
        AlphaValue.Text = _a.ToString();
        if (!HexBox.IsKeyboardFocused) HexBox.Text = ColorUtil.ToHex(c);
        Preview.Background = Ui.BrushOf(c);
        AutomationProperties.SetItemStatus(this, $"{ColorUtil.ToHex(c)}, hue {_h:0}, saturation {_s * 100:0} %, brightness {_v * 100:0} %, alpha {_a}");
        _updating = false;
    }

    // ---------- mouse ----------

    void OnSvDown(object sender, MouseButtonEventArgs e)
    {
        SvArea.Focus();
        SvArea.CaptureMouse();
        SetSv(e.GetPosition(SvArea));
    }

    void OnSvMove(object sender, MouseEventArgs e)
    {
        if (SvArea.IsMouseCaptured) SetSv(e.GetPosition(SvArea));
    }

    void SetSv(Point p)
    {
        _s = Math.Clamp(p.X / SvArea.ActualWidth, 0, 1);
        _v = Math.Clamp(1 - p.Y / SvArea.ActualHeight, 0, 1);
        Edit();
    }

    void OnHueDown(object sender, MouseButtonEventArgs e)
    {
        HueArea.Focus();
        HueArea.CaptureMouse();
        SetHue(e.GetPosition(HueArea));
    }

    void OnHueMove(object sender, MouseEventArgs e)
    {
        if (HueArea.IsMouseCaptured) SetHue(e.GetPosition(HueArea));
    }

    void SetHue(Point p)
    {
        _h = Math.Clamp(p.Y / HueArea.ActualHeight, 0, 1) * 359.9;
        Edit();
    }

    void OnAreaUp(object sender, MouseButtonEventArgs e) => ((UIElement)sender).ReleaseMouseCapture();

    // ---------- keyboard ----------

    void OnSvKey(object sender, KeyEventArgs e)
    {
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1 : 0.01;
        switch (e.Key)
        {
            case Key.Left: _s = Math.Max(0, _s - step); break;
            case Key.Right: _s = Math.Min(1, _s + step); break;
            case Key.Up: _v = Math.Min(1, _v + step); break;
            case Key.Down: _v = Math.Max(0, _v - step); break;
            default: return;
        }
        e.Handled = true;
        Edit();
    }

    void OnHueKey(object sender, KeyEventArgs e)
    {
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        if (e.Key == Key.Up) _h = (_h - step + 360) % 360;
        else if (e.Key == Key.Down) _h = (_h + step) % 360;
        else return;
        e.Handled = true;
        Edit();
    }

    void OnAlpha(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating) return;
        _a = (byte)AlphaSlider.Value;
        Edit();
    }

    void OnHexKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnHexCommit(sender, e);
    }

    void OnHexCommit(object sender, RoutedEventArgs e)
    {
        if (!ColorUtil.TryParseHex(HexBox.Text, out uint c))
        {
            Ui.MarkInvalid(HexBox, "Use 6 hex digits (RRGGBB) or 8 (AARRGGBB), e.g. #FF8000. Not applied.");
            return;
        }
        Ui.MarkInvalid(HexBox, null);
        if (c == Color) return;
        Color = c;
        ColorChanged?.Invoke(c);
    }
}
