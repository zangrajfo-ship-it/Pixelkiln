using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.App;

public partial class SpriteEditor : UserControl
{
    static readonly int[] ZoomLevels = [1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64];

    Sprite _sprite = new(32, 32);
    readonly History<Sprite> _history = new(s => s.Clone());
    int _layer, _frame, _zoom = 16;
    uint _primary = 0xFF000000, _secondary = 0;
    string? _path;
    bool _dirty;
    WriteableBitmap? _bmp;

    // active stroke
    bool _stroking;
    int _lastX, _lastY;
    uint _strokeColor;
    Point? _panFrom;

    public SpriteEditor()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadSprite(_sprite, null);
    }

    // ---------- document ----------

    void LoadSprite(Sprite s, string? path)
    {
        _sprite = s;
        _path = path;
        _dirty = false;
        _history.Clear();
        _layer = Math.Min(_layer, s.Layers.Count - 1);
        _frame = 0;
        RefreshAll();
    }

    /// <summary>Call before every edit.</summary>
    void Snapshot()
    {
        _history.Push(_sprite);
        _dirty = true;
        UpdateTitle();
    }

    void RefreshAll()
    {
        _layer = Math.Clamp(_layer, 0, _sprite.Layers.Count - 1);
        _frame = Math.Clamp(_frame, 0, _sprite.Frames.Count - 1);
        ApplyZoom();
        RefreshPalette();
        RefreshLayers();
        RefreshSwatches();
        Render();
        UpdateTitle();
    }

    void Render()
    {
        int w = _sprite.Width, h = _sprite.Height;
        if (_bmp is null || _bmp.PixelWidth != w || _bmp.PixelHeight != h)
            CanvasImage.Source = _bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        _bmp.WritePixels(new Int32Rect(0, 0, w, h), Compositor.Flatten(_sprite, _frame), w * 4, 0);
    }

    void UpdateTitle()
    {
        if (Window.GetWindow(this) is { } win)
            win.Title = $"PixelForge — {(_path is null ? "untitled" : System.IO.Path.GetFileName(_path))}{(_dirty ? " *" : "")}";
    }

    /// <summary>True if it is OK to drop the current sprite.</summary>
    public bool ConfirmDiscard()
    {
        if (!_dirty) return true;
        var r = MessageBox.Show("Save changes to the current sprite?", "PixelForge", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return r == MessageBoxResult.No || (r == MessageBoxResult.Yes && Save(_path));
    }

    // ---------- zoom / grid ----------

    void ApplyZoom()
    {
        CanvasHost.Width = _sprite.Width * _zoom;
        CanvasHost.Height = _sprite.Height * _zoom;
        GridOverlay.Fill = GridToggle.IsChecked == true && _zoom >= 4 ? GridBrush(_zoom) : null;
        Status.Text = $"{_sprite.Width}×{_sprite.Height}   {_zoom}×";
    }

    static Brush GridBrush(int cell)
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

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        int i = Array.IndexOf(ZoomLevels, _zoom) + Math.Sign(e.Delta);
        if (i < 0 || i >= ZoomLevels.Length) return;
        var p = e.GetPosition(CanvasHost);
        int old = _zoom;
        _zoom = ZoomLevels[i];
        ApplyZoom();
        Scroller.UpdateLayout();
        // keep the pixel under the mouse in place
        Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset + p.X / old * (_zoom - old));
        Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + p.Y / old * (_zoom - old));
    }

    void OnGridToggle(object sender, RoutedEventArgs e) => ApplyZoom();

    // ---------- drawing ----------

    (int X, int Y) PixelAt(MouseEventArgs e)
    {
        var p = e.GetPosition(CanvasHost);
        return ((int)Math.Floor(p.X / _zoom), (int)Math.Floor(p.Y / _zoom));
    }

    void OnCanvasDown(object sender, MouseButtonEventArgs e)
    {
        Keyboard.Focus(this);
        if (e.ChangedButton == MouseButton.Middle)
        {
            _panFrom = e.GetPosition(Scroller);
            CanvasHost.CaptureMouse();
            return;
        }
        if (_stroking || e.ChangedButton is not (MouseButton.Left or MouseButton.Right)) return;

        bool left = e.ChangedButton == MouseButton.Left;
        var (x, y) = PixelAt(e);
        if (!Tools.InBounds(_sprite.Width, _sprite.Height, x, y)) return;
        var cel = _sprite.Cel(_layer, _frame);

        if (PickerBtn.IsChecked == true)
        {
            uint c = Compositor.Flatten(_sprite, _frame)[y * _sprite.Width + x];
            if (left) _primary = c; else _secondary = c;
            RefreshSwatches();
            return;
        }

        Snapshot();
        _strokeColor = EraserBtn.IsChecked == true ? 0 : left ? _primary : _secondary;
        if (FillBtn.IsChecked == true)
        {
            Tools.Fill(cel, _sprite.Width, _sprite.Height, x, y, _strokeColor);
        }
        else
        {
            Tools.Set(cel, _sprite.Width, _sprite.Height, x, y, _strokeColor);
            (_lastX, _lastY, _stroking) = (x, y, true);
            CanvasHost.CaptureMouse();
        }
        Render();
    }

    void OnCanvasMove(object sender, MouseEventArgs e)
    {
        var (x, y) = PixelAt(e);
        Status.Text = $"{_sprite.Width}×{_sprite.Height}   {_zoom}×   {x}, {y}";

        if (_panFrom is Point from)
        {
            var now = e.GetPosition(Scroller);
            Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset - (now.X - from.X));
            Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset - (now.Y - from.Y));
            _panFrom = now;
            return;
        }
        if (!_stroking || (x, y) == (_lastX, _lastY)) return;
        Tools.Line(_sprite.Cel(_layer, _frame), _sprite.Width, _sprite.Height, _lastX, _lastY, x, y, _strokeColor);
        (_lastX, _lastY) = (x, y);
        Render();
    }

    void OnCanvasUp(object sender, MouseButtonEventArgs e)
    {
        _stroking = false;
        _panFrom = null;
        CanvasHost.ReleaseMouseCapture();
    }

    void OnCanvasLeave(object sender, MouseEventArgs e) => ApplyZoom();

    // ---------- palette / colors ----------

    void RefreshPalette()
    {
        PaletteWrap.Children.Clear();
        foreach (var c in _sprite.Palette)
        {
            var sw = new Border { Width = 20, Height = 20, Margin = new Thickness(1), Background = BrushOf(c), ToolTip = $"#{c & 0xFFFFFF:X6}" };
            sw.MouseLeftButtonDown += (_, _) => { _primary = c; RefreshSwatches(); };
            sw.MouseRightButtonDown += (_, _) => { _secondary = c; RefreshSwatches(); };
            PaletteWrap.Children.Add(sw);
        }
        var add = new Button { Content = "+", Width = 20, Height = 20, Margin = new Thickness(1), ToolTip = "Add color to palette" };
        add.Click += (_, _) =>
        {
            if (Dialogs.PickColor(_primary) is not uint c) return;
            Snapshot();
            _sprite.Palette.Add(c);
            _primary = c;
            RefreshPalette();
            RefreshSwatches();
        };
        PaletteWrap.Children.Add(add);
    }

    void RefreshSwatches()
    {
        PrimarySwatch.Background = BrushOf(_primary);
        SecondarySwatch.Background = BrushOf(_secondary);
    }

    static SolidColorBrush BrushOf(uint c) => new(Color.FromArgb((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c));

    void OnPrimarySwatch(object sender, MouseButtonEventArgs e)
    {
        if (Dialogs.PickColor(_primary) is uint c) { _primary = c; RefreshSwatches(); }
    }

    void OnSecondarySwatch(object sender, MouseButtonEventArgs e)
    {
        if (Dialogs.PickColor(_secondary) is uint c) { _secondary = c; RefreshSwatches(); }
    }

    // ---------- layers (list shows top layer first) ----------

    bool _refreshingLayers;

    void RefreshLayers()
    {
        _refreshingLayers = true;
        LayersList.Items.Clear();
        for (int i = _sprite.Layers.Count - 1; i >= 0; i--)
        {
            var layer = _sprite.Layers[i];
            int index = i;
            var vis = new CheckBox { IsChecked = layer.Visible, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Visible" };
            vis.Click += (_, _) => { layer.Visible = vis.IsChecked == true; _dirty = true; UpdateTitle(); Render(); };
            var name = new TextBox { Text = layer.Name, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(4, 0, 0, 0) };
            name.GotKeyboardFocus += (_, _) => LayersList.SelectedIndex = _sprite.Layers.Count - 1 - index;
            name.LostKeyboardFocus += (_, _) =>
            {
                if (name.Text == layer.Name || string.IsNullOrWhiteSpace(name.Text)) { name.Text = layer.Name; return; }
                Snapshot();
                _sprite.Layers[index].Name = name.Text;
            };
            DockPanel.SetDock(vis, Dock.Left);
            LayersList.Items.Add(new DockPanel { Children = { vis, name } });
        }
        LayersList.SelectedIndex = _sprite.Layers.Count - 1 - _layer;
        _refreshingLayers = false;
    }

    void OnLayerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshingLayers && LayersList.SelectedIndex >= 0)
            _layer = _sprite.Layers.Count - 1 - LayersList.SelectedIndex;
    }

    void OnAddLayer(object sender, RoutedEventArgs e)
    {
        Snapshot();
        _sprite.AddLayer(++_layer, $"Layer {_sprite.Layers.Count + 1}");
        RefreshLayers();
    }

    void OnDeleteLayer(object sender, RoutedEventArgs e)
    {
        if (_sprite.Layers.Count == 1) return;
        Snapshot();
        _sprite.RemoveLayer(_layer);
        _layer = Math.Max(0, _layer - 1);
        RefreshLayers();
        Render();
    }

    void OnLayerUp(object sender, RoutedEventArgs e) => MoveLayer(+1);
    void OnLayerDown(object sender, RoutedEventArgs e) => MoveLayer(-1);

    void MoveLayer(int delta)
    {
        int to = _layer + delta;
        if (to < 0 || to >= _sprite.Layers.Count) return;
        Snapshot();
        _sprite.MoveLayer(_layer, to);
        _layer = to;
        RefreshLayers();
        Render();
    }

    // ---------- undo ----------

    void OnUndo(object? sender, RoutedEventArgs? e) => Restore(_history.Undo(_sprite));
    void OnRedo(object? sender, RoutedEventArgs? e) => Restore(_history.Redo(_sprite));

    void Restore(Sprite? s)
    {
        if (s is null || _stroking) return;
        _sprite = s;
        _dirty = true;
        RefreshAll();
    }

    // ---------- files ----------

    const string ProjectFilter = "PixelForge sprite (*.pxf)|*.pxf";

    void OnNew(object? sender, RoutedEventArgs? e)
    {
        if (!ConfirmDiscard()) return;
        if (Dialogs.Ask(this, "New sprite", "Size (width x height):", $"{_sprite.Width}x{_sprite.Height}") is not string text) return;
        var parts = text.ToLowerInvariant().Split('x', '×', ' ', ',');
        try
        {
            var nums = parts.Where(p => p.Length > 0).Select(int.Parse).ToArray();
            if (nums.Length != 2) throw new FormatException("Enter size as WIDTHxHEIGHT, e.g. 32x32.");
            LoadSprite(new Sprite(nums[0], nums[1]), null);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentOutOfRangeException) { Dialogs.Error(ex); }
    }

    void OnOpen(object? sender, RoutedEventArgs? e)
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog { Filter = ProjectFilter + "|Images (*.png;*.gif;*.bmp;*.jpg)|*.png;*.gif;*.bmp;*.jpg|All files|*.*" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            if (dlg.FileName.EndsWith(".pxf", StringComparison.OrdinalIgnoreCase))
            {
                LoadSprite(ProjectFile.Load(dlg.FileName), dlg.FileName);
            }
            else
            {
                var (px, w, h) = ImageIO.LoadImage(dlg.FileName);
                var s = new Sprite(w, h);
                px.CopyTo(s.Cel(0, 0), 0);
                LoadSprite(s, null);   // imported image: Save asks for a .pxf name
            }
        }
        catch (Exception ex) { Dialogs.Error(ex); }
    }

    void OnSave(object? sender, RoutedEventArgs? e) => Save(_path);
    void OnSaveAs(object sender, RoutedEventArgs e) => Save(null);

    bool Save(string? path)
    {
        if (path is null)
        {
            var dlg = new SaveFileDialog { Filter = ProjectFilter, FileName = "sprite.pxf" };
            if (dlg.ShowDialog() != true) return false;
            path = dlg.FileName;
        }
        try
        {
            ProjectFile.Save(_sprite, path);
            _path = path;
            _dirty = false;
            UpdateTitle();
            return true;
        }
        catch (Exception ex) { Dialogs.Error(ex); return false; }
    }

    void OnExportPng(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = System.IO.Path.ChangeExtension(System.IO.Path.GetFileName(_path) ?? "sprite", ".png") };
        if (dlg.ShowDialog() != true) return;
        try { ImageIO.SavePng(dlg.FileName, Compositor.Flatten(_sprite, _frame), _sprite.Width, _sprite.Height); }
        catch (Exception ex) { Dialogs.Error(ex); }
    }

    // ---------- keyboard (routed from MainWindow) ----------

    public void HandleKey(KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Z when ctrl: OnUndo(null, null); break;
            case Key.Y when ctrl: OnRedo(null, null); break;
            case Key.S when ctrl: OnSave(null, null); break;
            case Key.N when ctrl: OnNew(null, null); break;
            case Key.O when ctrl: OnOpen(null, null); break;
            case Key.G when ctrl: GridToggle.IsChecked = GridToggle.IsChecked != true; ApplyZoom(); break;
            case Key.B when !ctrl: PencilBtn.IsChecked = true; break;
            case Key.E when !ctrl: EraserBtn.IsChecked = true; break;
            case Key.G when !ctrl: FillBtn.IsChecked = true; break;
            case Key.I when !ctrl: PickerBtn.IsChecked = true; break;
            default: e.Handled = false; break;
        }
    }
}
