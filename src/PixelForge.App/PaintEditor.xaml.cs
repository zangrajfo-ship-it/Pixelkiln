using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.App;

/// <summary>
/// Paint mode (Sprite tab): smooth brushes on large canvases with layers, blend modes, masks, alpha lock and pen
/// pressure. Only the changed part of the picture is recomposited and redrawn while painting.
/// </summary>
public partial class PaintEditor : UserControl, IEditor, IStyleSource, IToolSelectable
{
    static readonly double[] Zooms = [1 / 16.0, 1 / 8.0, 0.25, 1 / 3.0, 0.5, 0.75, 1, 1.5, 2, 3, 4, 6, 8, 12, 16, 24, 32];

    PaintDoc _doc = new(1920, 1080);
    readonly PaintUndo _undo = new(App.Settings.UndoLimit);
    readonly ViewAids _aids;
    string? _path;
    bool _dirty, _refreshing, _ready, _needsFit;
    int _layer;
    double _zoom = 0.5;
    WriteableBitmap? _bmp;
    uint _primary = 0xFF202020, _secondary = 0xFFFFFFFF;

    PaintStroke? _stroke;
    bool _stylusActive;
    Point? _panFrom, _gradFrom;
    Point _panScroll;
    PaintUndo.Step? _layerOpacityBefore;
    readonly DispatcherTimer _airbrush = new() { Interval = TimeSpan.FromMilliseconds(30) };

    public PaintEditor()
    {
        InitializeComponent();
        _ready = true;
        _aids = new ViewAids(this, CanvasHost, Scroller, CanvasImage, () => _zoom);
        BlendBox.ItemsSource = Enum.GetNames<BlendMode>();
        Picker.Color = _primary;
        Picker.ColorChanged += c => { _primary = c; RefreshSwatches(); };
        Checker.Fill = Ui.CheckerBrush();
        LayerOpacitySlider.ValueChanged += OnLayerOpacity;
        _airbrush.Tick += (_, _) => { _stroke?.Hold(); Redraw(); };
        IsVisibleChanged += (_, _) =>
        {
            UpdateTitle();
            if (IsVisible && _needsFit) Dispatcher.BeginInvoke(ZoomFit, DispatcherPriority.Loaded);   // fit once the viewport has a size
        };
        IsKeyboardFocusedChanged += (_, _) => Scroller.BorderBrush = IsKeyboardFocused ? (Brush)FindResource("FocusRing") : Brushes.Transparent;
        Loaded += Init;
        void Init(object s, RoutedEventArgs e) { Loaded -= Init; LoadDoc(_doc, null); RefreshSwatches(); OnBrushChanged(null, null); }
    }

    PaintLayer Layer => _doc.Layers[Math.Clamp(_layer, 0, _doc.Layers.Count - 1)];

    // ---------- document ----------

    void LoadDoc(PaintDoc d, string? path)
    {
        EndStroke();
        (_doc, _path, _dirty) = (d, path, false);
        _undo.Clear();
        _aids.Bind(path);
        _layer = d.Layers.Count - 1;
        _bmp = new WriteableBitmap(d.Width, d.Height, 96, 96, PixelFormats.Bgra32, null);
        CanvasImage.Source = _bmp;
        CanvasImage.Width = Checker.Width = d.Width;
        CanvasImage.Height = Checker.Height = d.Height;
        RedrawAll();
        RefreshLayers();
        _needsFit = true;
        if (IsVisible) Dispatcher.BeginInvoke(ZoomFit, DispatcherPriority.Loaded);
        else ApplyZoom();
        UpdateTitle();
    }

    void Changed()
    {
        _dirty = true;
        UpdateTitle();
    }

    void UpdateTitle()
    {
        if (IsVisible && Window.GetWindow(this) is { } win)
            win.Title = $"PixelForge — {(_path is null ? "untitled painting" : System.IO.Path.GetFileName(_path))}{(_dirty ? " *" : "")}";
        Status.Text = $"{_doc.Width}×{_doc.Height} px · {_doc.Layers.Count} layer{(_doc.Layers.Count == 1 ? "" : "s")} · {_doc.Bytes / 1048576.0:0.#} MB in tiles";
    }

    // ---------- drawing ----------

    /// <summary>Recomposites and shows one area of the canvas.</summary>
    void Show(int x, int y, int w, int h)
    {
        if (_bmp is null || w <= 0 || h <= 0) return;
        var px = _doc.Composite(x, y, w, h);
        _bmp.WritePixels(new Int32Rect(x, y, w, h), px, w * 4, 0);
    }

    void RedrawAll()
    {
        for (int ty = 0; ty < _doc.TilesY; ty++)
            for (int tx = 0; tx < _doc.TilesX; tx++)
            {
                int x = tx * PaintLayer.Tile, y = ty * PaintLayer.Tile;
                Show(x, y, Math.Min(PaintLayer.Tile, _doc.Width - x), Math.Min(PaintLayer.Tile, _doc.Height - y));
            }
    }

    void Redraw()
    {
        if (_stroke?.TakeDirty() is var (x, y, w, h)) Show(x, y, w, h);
    }

    // ---------- zoom and view ----------

    void ApplyZoom()
    {
        CanvasHost.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        RenderOptions.SetBitmapScalingMode(CanvasImage, _zoom >= 2 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        ZoomText.Text = $"{_zoom * 100:0} %";
        Checker.Fill = Ui.CheckerBrush(_zoom);
        BrushRing.StrokeThickness = BrushRingDark.StrokeThickness = GradientLine.StrokeThickness = 1 / _zoom;
        _aids.Layout(_doc.Width, _doc.Height, 1, 1 / _zoom);
    }

    void ZoomStep(int dir, Point? anchor = null)
    {
        int i = Array.FindIndex(Zooms, z => z >= _zoom - 1e-9) + dir;
        if (i < 0 || i >= Zooms.Length) return;
        double old = _zoom;
        _zoom = Zooms[i];
        ApplyZoom();
        if (anchor is Point a)
        {
            Scroller.UpdateLayout();
            Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset + a.X * (_zoom - old));
            Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + a.Y * (_zoom - old));
        }
    }

    void ZoomFit()
    {
        if (Scroller.ViewportWidth < 100 || Scroller.ViewportHeight < 100) return;   // not laid out yet
        _needsFit = false;
        double fit = Math.Min((Scroller.ViewportWidth - 90) / _doc.Width, (Scroller.ViewportHeight - 90) / _doc.Height);
        _zoom = Zooms.LastOrDefault(z => z <= fit, Zooms[0]);
        ApplyZoom();
    }

    void OnZoomIn(object sender, RoutedEventArgs e) => ZoomStep(+1);
    void OnZoomOut(object sender, RoutedEventArgs e) => ZoomStep(-1);
    void OnZoomFit(object sender, RoutedEventArgs e) => ZoomFit();

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = true;
        ZoomStep(Math.Sign(e.Delta), e.GetPosition(CanvasImage));
    }

    // ---------- brush ----------

    PaintTool? Tool => (string)ToolsPanel.Children.OfType<RadioButton>().First(r => r.IsChecked == true).Tag is var t && Enum.TryParse<PaintTool>(t, out var tool) ? tool : null;
    string ToolName => (string)ToolsPanel.Children.OfType<RadioButton>().First(r => r.IsChecked == true).Tag;

    BrushSettings Brush => new(Tool ?? PaintTool.Brush, SizeSlider.Value, HardSlider.Value, SpacingSlider.Value, OpacitySlider.Value, FlowSlider.Value,
        GrainSlider.Value, PressureSizeBox.IsChecked == true, PressureOpacityBox.IsChecked == true, (int)SmoothSlider.Value, SquareBox.IsChecked == true,
        WrapBox.IsChecked == true);

    /// <summary>Turns wrap-around painting on (Texture mode opens a texture's painting this way).</summary>
    public void SetWrap(bool on) => WrapBox.IsChecked = on;

    void OnBrushChanged(object? sender, RoutedEventArgs? e)
    {
        if (!_ready) return;   // sliders report their first value while the XAML is still loading
        SizeLabel.Text = $"Size {SizeSlider.Value:0} px";
        HardLabel.Text = $"Hardness {HardSlider.Value * 100:0} %";
        OpacityLabel.Text = $"Opacity {OpacitySlider.Value * 100:0} %";
        FlowLabel.Text = $"Flow {FlowSlider.Value * 100:0} %";
        SpacingLabel.Text = $"Spacing {SpacingSlider.Value * 100:0} %";
        GrainLabel.Text = $"Grain {GrainSlider.Value * 100:0} %";
        SmoothLabel.Text = $"Smoothing {SmoothSlider.Value:0}";
    }

    void OnToolClick(object sender, RoutedEventArgs e) => GradientLine.Visibility = Visibility.Collapsed;

    public void SelectTool(string name)
    {
        if (ToolsPanel.Children.OfType<RadioButton>().FirstOrDefault(r => string.Equals((string)r.Tag, name, StringComparison.OrdinalIgnoreCase)) is { } r) r.IsChecked = true;
    }

    public string CurrentToolName => ToolName;

    void RefreshSwatches()
    {
        PrimarySwatch.Background = Ui.BrushOf(_primary);
        SecondarySwatch.Background = Ui.BrushOf(_secondary);
    }

    void SwapColors()
    {
        (_primary, _secondary) = (_secondary, _primary);
        Picker.Color = _primary;
        RefreshSwatches();
    }

    // ---------- input: pen and mouse ----------

    void Begin(Point p, double pressure)
    {
        Keyboard.Focus(this);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || ToolName == "Picker") { Pick(p); return; }
        if (ToolName == "Gradient")
        {
            _gradFrom = p;
            (GradientLine.X1, GradientLine.Y1, GradientLine.X2, GradientLine.Y2) = (p.X, p.Y, p.X, p.Y);
            GradientLine.Visibility = Visibility.Visible;
            return;
        }
        if (!Layer.Visible) { Status.Text = "The layer is hidden: show it to paint on it."; return; }
        bool mask = EditMaskToggle.IsChecked == true && Layer.Mask is not null;
        _stroke = new PaintStroke(_doc, Layer, Brush, _primary, mask);
        _stroke.To(p.X, p.Y, pressure);
        Redraw();
        if (Tool == PaintTool.Airbrush) _airbrush.Start();
    }

    void Continue(Point p, double pressure)
    {
        MoveRing(p, pressure);
        if (_gradFrom is not null) { (GradientLine.X2, GradientLine.Y2) = (p.X, p.Y); return; }
        if (_stroke is null) return;
        _stroke.To(p.X, p.Y, pressure);
        Redraw();
    }

    void End(Point p)
    {
        if (_gradFrom is Point from)
        {
            _gradFrom = null;
            GradientLine.Visibility = Visibility.Collapsed;
            if ((p - from).Length < 2) return;
            _undo.Push(PaintOps.Gradient(_doc, Layer, from.X, from.Y, p.X, p.Y, _primary, _secondary));
            RedrawAll();
            Changed();
            return;
        }
        EndStroke();
    }

    void EndStroke()
    {
        _airbrush.Stop();
        if (_stroke is null) return;
        Redraw();
        _undo.Push(_stroke.Finish());
        _stroke = null;
        Changed();
    }

    void Pick(Point p)
    {
        int x = (int)p.X, y = (int)p.Y;
        if (x < 0 || y < 0 || x >= _doc.Width || y >= _doc.Height) return;
        uint c = _doc.Composite(x, y, 1, 1)[0];
        if (c >> 24 == 0) return;
        _primary = c | 0xFF000000;
        Picker.Color = _primary;
        RefreshSwatches();
    }

    void MoveRing(Point p, double pressure)
    {
        double size = Brush is var b && b.PressureSize ? b.Size * (0.15 + 0.85 * pressure) : SizeSlider.Value;
        foreach (var ring in new[] { BrushRing, BrushRingDark })
        {
            ring.Width = ring.Height = size;
            Canvas.SetLeft(ring, p.X - size / 2);
            Canvas.SetTop(ring, p.Y - size / 2);
            ring.Visibility = ToolName is "Picker" or "Gradient" ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    // pen: pressure (and tilt where the driver reports it) comes with the stylus points; the mouse events WPF
    // promotes from the pen are ignored, so a stroke is never drawn twice
    void OnStylusDown(object sender, StylusDownEventArgs e)
    {
        if (e.StylusDevice.Inverted) SelectTool("Eraser");   // the eraser end of the pen
        _stylusActive = true;
        var pts = e.GetStylusPoints(CanvasImage);
        Begin(pts[0].ToPoint(), pts[0].PressureFactor);
        CanvasHost.CaptureStylus();
        e.Handled = true;
    }

    void OnStylusMove(object sender, StylusEventArgs e)
    {
        if (!_stylusActive) return;
        foreach (var sp in e.GetStylusPoints(CanvasImage)) Continue(sp.ToPoint(), sp.PressureFactor);
        e.Handled = true;
    }

    void OnStylusUp(object sender, StylusEventArgs e)
    {
        if (!_stylusActive) return;
        _stylusActive = false;
        End(e.GetPosition(CanvasImage));
        CanvasHost.ReleaseStylusCapture();
        e.Handled = true;
    }

    void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice is not null) return;
        if (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Left && Keyboard.IsKeyDown(Key.Space))
        {
            _panFrom = e.GetPosition(Scroller);
            _panScroll = new Point(Scroller.HorizontalOffset, Scroller.VerticalOffset);
            CanvasHost.CaptureMouse();
            return;
        }
        if (e.ChangedButton != MouseButton.Left) return;
        Begin(e.GetPosition(CanvasImage), 1);
        CanvasHost.CaptureMouse();
    }

    void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.StylusDevice is not null) return;
        if (_panFrom is Point from)
        {
            var now = e.GetPosition(Scroller);
            Scroller.ScrollToHorizontalOffset(_panScroll.X - (now.X - from.X));
            Scroller.ScrollToVerticalOffset(_panScroll.Y - (now.Y - from.Y));
            return;
        }
        var p = e.GetPosition(CanvasImage);
        Continue(p, 1);
        if (p.X >= 0 && p.Y >= 0 && p.X < _doc.Width && p.Y < _doc.Height) Status.Text = $"{(int)p.X}, {(int)p.Y}   {_doc.Width}×{_doc.Height} px";
    }

    void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice is not null) return;
        CanvasHost.ReleaseMouseCapture();
        if (_panFrom is not null) { _panFrom = null; return; }
        if (e.ChangedButton == MouseButton.Left) End(e.GetPosition(CanvasImage));
    }

    void OnMouseLeave(object sender, MouseEventArgs e) => BrushRing.Visibility = BrushRingDark.Visibility = Visibility.Collapsed;

    // ---------- layers (list shows the top layer first) ----------

    void RefreshLayers()
    {
        _refreshing = true;
        _layer = Math.Clamp(_layer, 0, _doc.Layers.Count - 1);
        LayersList.Items.Clear();
        for (int i = _doc.Layers.Count - 1; i >= 0; i--)
        {
            var l = _doc.Layers[i];
            string extra = (l.Blend != BlendMode.Normal ? $" · {l.Blend}" : "") + (l.Opacity < 1 ? $" · {l.Opacity * 100:0} %" : "") + (l.Mask is not null ? " · mask" : "") + (l.AlphaLocked ? " · locked" : "");
            LayersList.Items.Add(new ListBoxItem { Content = $"{(l.Visible ? "" : "(hidden) ")}{l.Name}{extra}", Tag = i });
        }
        LayersList.SelectedIndex = _doc.Layers.Count - 1 - _layer;
        BlendBox.SelectedItem = Layer.Blend.ToString();
        LayerOpacitySlider.Value = Layer.Opacity;
        VisibleBox.IsChecked = Layer.Visible;
        AlphaLockBox.IsChecked = Layer.AlphaLocked;
        MaskButton.Content = Layer.Mask is null ? "Add mask" : "Remove mask";
        EditMaskToggle.IsEnabled = Layer.Mask is not null;
        if (Layer.Mask is null) EditMaskToggle.IsChecked = false;
        _refreshing = false;
        UpdateTitle();
    }

    void OnLayerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || LayersList.SelectedItem is not ListBoxItem { Tag: int i }) return;
        _layer = i;
        RefreshLayers();
    }

    /// <summary>Layer changes: one undo step that restores the list and every layer's settings.</summary>
    void LayerChange(Action change)
    {
        EndStroke();
        _undo.Push(new PaintUndo.Layers(_doc));
        change();
        RefreshLayers();
        RedrawAll();
        Changed();
    }

    void OnAddLayer(object sender, RoutedEventArgs e) => LayerChange(() =>
    {
        _doc.Layers.Insert(_layer + 1, new PaintLayer { Name = $"Layer {_doc.Layers.Count + 1}" });
        _layer++;
    });

    void OnDeleteLayer(object sender, RoutedEventArgs e)
    {
        if (_doc.Layers.Count < 2) return;
        LayerChange(() => { _doc.Layers.RemoveAt(_layer); _layer = Math.Max(0, _layer - 1); });
    }

    void MoveLayer(int dir)
    {
        int to = _layer + dir;
        if (to < 0 || to >= _doc.Layers.Count) return;
        LayerChange(() =>
        {
            (_doc.Layers[_layer], _doc.Layers[to]) = (_doc.Layers[to], _doc.Layers[_layer]);
            _layer = to;
        });
    }

    void OnLayerUp(object sender, RoutedEventArgs e) => MoveLayer(+1);
    void OnLayerDown(object sender, RoutedEventArgs e) => MoveLayer(-1);

    void OnRenameLayer(object sender, RoutedEventArgs e)
    {
        if (Ui.Ask(this, "Rename layer", "Name:", Layer.Name) is { Length: > 0 } name) LayerChange(() => Layer.Name = name.Trim());
    }

    void OnLayerFieldChanged(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        var blend = Enum.Parse<BlendMode>((string)BlendBox.SelectedItem);
        if ((blend, VisibleBox.IsChecked == true, AlphaLockBox.IsChecked == true) == (Layer.Blend, Layer.Visible, Layer.AlphaLocked)) return;
        LayerChange(() => (Layer.Blend, Layer.Visible, Layer.AlphaLocked) = (blend, VisibleBox.IsChecked == true, AlphaLockBox.IsChecked == true));
    }

    // opacity: live while dragging, one undo step for the whole drag
    void OnLayerOpacity(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_refreshing) return;
        _layerOpacityBefore ??= new PaintUndo.Layers(_doc);
        Layer.Opacity = Math.Round(e.NewValue, 2);
        RedrawAll();
    }

    void CommitOpacity()
    {
        if (_layerOpacityBefore is null) return;
        _undo.Push(_layerOpacityBefore);
        _layerOpacityBefore = null;
        RefreshLayers();
        Changed();
    }

    void OnLayerOpacityDone(object sender, DragCompletedEventArgs e) => CommitOpacity();
    void OnLayerOpacityKey(object sender, KeyEventArgs e) => CommitOpacity();

    void OnMask(object sender, RoutedEventArgs e) => LayerChange(() =>
    {
        Layer.Mask = Layer.Mask is null ? [] : null;
        if (Layer.Mask is not null) EditMaskToggle.IsChecked = true;
    });

    void OnEditMask(object sender, RoutedEventArgs e) =>
        Status.Text = EditMaskToggle.IsChecked == true ? "Painting on the mask: the brush hides, the eraser reveals." : "Painting on the layer.";

    // ---------- undo ----------

    void OnUndo(object? sender, RoutedEventArgs? e) { EndStroke(); if (_undo.Undo(_doc)) AfterUndo(); }
    void OnRedo(object? sender, RoutedEventArgs? e) { EndStroke(); if (_undo.Redo(_doc)) AfterUndo(); }

    void AfterUndo()
    {
        RefreshLayers();
        RedrawAll();
        Changed();
    }

    // ---------- files ----------

    const string Filter = "PixelForge painting (*.pxpaint)|*.pxpaint";

    void OnNew(object? sender, RoutedEventArgs? e)
    {
        if (!ConfirmDiscard()) return;
        if (Ui.AskFields(this, "New painting", ("_Size", "1920 × 1080", ["1280 × 720", "1920 × 1080", "2048 × 2048", "2560 × 1440", "4096 × 4096", "1024 × 1024"])) is not { } f) return;
        var parts = f[0].Split('×', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out int w) || !int.TryParse(parts[1], out int h) || w is < 1 or > PaintDoc.MaxSize || h is < 1 or > PaintDoc.MaxSize)
        {
            Ui.Error(new FormatException($"Write the size as width × height, each 1–{PaintDoc.MaxSize}."));
            return;
        }
        var d = new PaintDoc(w, h);
        var paper = new PaintLayer { Name = "Background" };
        d.Layers.Insert(0, paper);
        PaintOps.Gradient(d, paper, 0, 0, 1, 0, 0xFFFFFFFF, 0xFFFFFFFF);   // white paper under an empty layer
        LoadDoc(d, null);
    }

    void OnOpen(object? sender, RoutedEventArgs? e)
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog { Filter = Filter };
        if (dlg.ShowDialog() == true) OpenPath(dlg.FileName, confirmed: true);
    }

    void OnSave(object? sender, RoutedEventArgs? e) => Save(_path);

    bool Save(string? path)
    {
        EndStroke();
        if (path is null)
        {
            var dlg = new SaveFileDialog { Filter = Filter, FileName = "painting.pxpaint", InitialDirectory = App.Project?.Root ?? "" };
            if (dlg.ShowDialog() != true) return false;
            path = dlg.FileName;
        }
        try
        {
            PaintDoc.Save(_doc, path);
            (_path, _dirty) = (path, false);
            App.AddRecent(path);
            _aids.Rebind(path);
            UpdateTitle();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); return false; }
    }

    void OnImportImage(object? sender, RoutedEventArgs? e)
    {
        var dlg = new OpenFileDialog { Filter = "Pictures (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.pxf)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.pxf" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var (px, w, h) = AssetLoader.LoadImage(dlg.FileName);
            LayerChange(() =>
            {
                _doc.Layers.Insert(_layer + 1, PaintOps.LayerFrom(_doc, px, w, h, System.IO.Path.GetFileNameWithoutExtension(dlg.FileName)));
                _layer++;
            });
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidDataException or FileFormatException) { Ui.Error(ex); }
    }

    void OnExportPng(object? sender, RoutedEventArgs? e)
    {
        var dlg = new SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = System.IO.Path.ChangeExtension(System.IO.Path.GetFileName(_path) ?? "painting", ".png") };
        if (dlg.ShowDialog() != true) return;
        try
        {
            ImageIO.SavePng(dlg.FileName, _doc.Flatten(), _doc.Width, _doc.Height);
            App.AddRecent(dlg.FileName);
            Status.Text = $"Exported {System.IO.Path.GetFileName(dlg.FileName)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); }
    }

    /// <summary>Set by MainWindow: opens pixels in Pixel mode.</summary>
    public Action<uint[], int, int>? SendToPixel { get; set; }

    void OnToPixel(object? sender, RoutedEventArgs? e)
    {
        int longest = Math.Max(_doc.Width, _doc.Height);
        if (Ui.AskFields(this, "To pixel art", ("_Longest side (px)", "64", ["16", "32", "48", "64", "96", "128", "256"])) is not { } f) return;
        if (!int.TryParse(f[0], out int n) || n is < 1 or > 4096) { Ui.Error(new FormatException("A whole number of pixels, 1–4096.")); return; }
        int w = Math.Max(1, _doc.Width * n / longest), h = Math.Max(1, _doc.Height * n / longest);
        SendToPixel?.Invoke(PaintOps.Downscale(_doc, w, h), w, h);
    }

    // ---------- style ----------

    sealed record PaintStyle(BrushSettings Brush, uint Primary, uint Secondary);

    public object? CopyStyle() => new PaintStyle(Brush, _primary, _secondary);

    public string? PasteStyle(object style)
    {
        if (style is not PaintStyle s) return null;
        var b = s.Brush;
        (SizeSlider.Value, HardSlider.Value, SpacingSlider.Value, OpacitySlider.Value, FlowSlider.Value, GrainSlider.Value, SmoothSlider.Value) =
            (b.Size, b.Hardness, b.Spacing, b.Opacity, b.Flow, b.Grain, b.Smoothing);
        (PressureSizeBox.IsChecked, PressureOpacityBox.IsChecked, SquareBox.IsChecked) = (b.PressureSize, b.PressureOpacity, b.Square);
        SelectTool(b.Tool.ToString());
        (_primary, _secondary) = (s.Primary, s.Secondary);
        Picker.Color = _primary;
        RefreshSwatches();
        OnBrushChanged(null, null);
        return "Brush and colors pasted.";
    }

    // ---------- IEditor ----------

    public string[] FileExtensions => [".pxpaint"];
    public bool IsDirty => _dirty;
    public string? CurrentPath => _path;

    public bool ConfirmDiscard()
    {
        if (!_dirty) return true;
        var r = MessageBox.Show("Save changes to the current painting?", "PixelForge", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return r == MessageBoxResult.No || (r == MessageBoxResult.Yes && Save(_path));
    }

    public void OpenPath(string path, bool confirmed = false)
    {
        if (!confirmed && !ConfirmDiscard()) return;
        try { LoadDoc(PaintDoc.Load(path), path); App.AddRecent(path); }
        catch (Exception ex) when (ex is InvalidDataException or IOException) { Ui.Error(ex); }
    }

    public void OnFileMoved(string oldPath, string newPath)
    {
        if (string.Equals(_path, oldPath, StringComparison.OrdinalIgnoreCase)) { _path = newPath; _aids.Rebind(newPath); UpdateTitle(); }
    }

    public void WriteAutosave(string file) => PaintDoc.Save(_doc, file);

    public void RestoreAutosave(string file, string? originalPath)
    {
        LoadDoc(PaintDoc.Load(file), originalPath);
        _dirty = true;
        UpdateTitle();
    }

    IReadOnlyList<MenuItem>? _menus;
    Action Do(RoutedEventHandler h) => () => h(this, new RoutedEventArgs());

    public IReadOnlyList<MenuItem> Menus => _menus ??=
    [
        Ui.Menu("_File",
            Ui.Item("_New painting…", "Ctrl+N", Do(OnNew)),
            Ui.Item("_Open…", "Ctrl+O", Do(OnOpen)),
            Ui.RecentMenu(this),
            Ui.Item("_Save", "Ctrl+S", Do(OnSave)),
            Ui.Item("Save _as…", "Ctrl+Shift+S", () => Save(null)),
            new Separator(),
            Ui.Item("_Import image as layer…", null, Do(OnImportImage)),
            Ui.Item("_Export PNG…", "Ctrl+E", Do(OnExportPng)),
            Ui.Item("To _pixel art…", null, Do(OnToPixel)),
            new Separator(),
            Ui.Item("E_xit", "Alt+F4", () => Window.GetWindow(this)?.Close())),
        Ui.Menu("_Edit",
            Ui.Item("_Undo", "Ctrl+Z", Do(OnUndo)),
            Ui.Item("_Redo", "Ctrl+Y", Do(OnRedo)),
            new Separator(),
            Ui.Item("S_wap colors", "X", SwapColors)),
        Ui.Menu("_Layer",
            Ui.Item("_New layer", "Ctrl+Shift+N", Do(OnAddLayer)),
            Ui.Item("_Delete layer", null, Do(OnDeleteLayer)),
            Ui.Item("Move _up", null, Do(OnLayerUp)),
            Ui.Item("Move d_own", null, Do(OnLayerDown)),
            Ui.Item("_Rename…", null, Do(OnRenameLayer)),
            Ui.Item("Add or remove _mask", null, Do(OnMask))),
        Ui.Menu("_View",
            _aids.Menu(),
            new Separator(),
            Ui.Item("Zoom _in", "Ctrl+=", () => ZoomStep(+1)),
            Ui.Item("Zoom _out", "Ctrl+-", () => ZoomStep(-1)),
            Ui.Item("_Fit to window", "Ctrl+0", ZoomFit),
            Ui.Item("_Actual pixels", null, () => { _zoom = 1; ApplyZoom(); })),
    ];

    public void HandleKey(KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Z when ctrl: OnUndo(null, null); break;
            case Key.Y when ctrl: OnRedo(null, null); break;
            case Key.S when ctrl && shift: Save(null); break;
            case Key.S when ctrl: OnSave(null, null); break;
            case Key.N when ctrl && shift: OnAddLayer(this, new RoutedEventArgs()); break;
            case Key.N when ctrl: OnNew(null, null); break;
            case Key.O when ctrl: OnOpen(null, null); break;
            case Key.E when ctrl: OnExportPng(null, null); break;
            case Key.OemPlus or Key.Add when ctrl: ZoomStep(+1); break;
            case Key.OemMinus or Key.Subtract when ctrl: ZoomStep(-1); break;
            case Key.D0 or Key.NumPad0 when ctrl: ZoomFit(); break;
            case Key.B when !ctrl: SelectTool("Brush"); break;
            case Key.N when !ctrl: SelectTool("Pencil"); break;
            case Key.E when !ctrl: SelectTool("Eraser"); break;
            case Key.A when !ctrl: SelectTool("Airbrush"); break;
            case Key.S when !ctrl: SelectTool("Smudge"); break;
            case Key.I when !ctrl: SelectTool("Picker"); break;
            case Key.G when !ctrl: SelectTool("Gradient"); break;
            case Key.X when !ctrl: SwapColors(); break;
            case Key.W when !ctrl: WrapBox.IsChecked = WrapBox.IsChecked != true; break;
            case Key.M when !ctrl && Layer.Mask is not null: EditMaskToggle.IsChecked = EditMaskToggle.IsChecked != true; OnEditMask(this, new RoutedEventArgs()); break;
            case Key.OemOpenBrackets: SizeSlider.Value = Math.Max(1, SizeSlider.Value * 0.8); break;
            case Key.OemCloseBrackets: SizeSlider.Value = Math.Min(500, SizeSlider.Value * 1.25 + 1); break;
            case Key.Space: break;   // held for panning (mouse drag)
            default: e.Handled = false; break;
        }
    }
}
