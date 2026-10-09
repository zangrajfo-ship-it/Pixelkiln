using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.App;

public partial class TilemapEditor : UserControl
{
    static readonly int[] ZoomLevels = [1, 2, 3, 4, 6, 8];

    Tilemap _map = new(20, 15, 16);
    readonly History<Tilemap> _history = new(m => m.Clone());
    int _layer, _tile, _zoom = 2;
    string? _path;
    bool _dirty;
    WriteableBitmap? _bmp;

    bool _stroking;
    int _lastX, _lastY, _strokeTile;
    Point? _panFrom;

    /// <summary>Set by MainWindow: the Sprite tab's current image.</summary>
    public Func<(uint[] Pixels, int Width, int Height)>? SpriteImage { get; set; }

    public TilemapEditor()
    {
        InitializeComponent();
        Loaded += Init;
        IsVisibleChanged += (_, _) => UpdateTitle();

        void Init(object s, RoutedEventArgs e) { Loaded -= Init; LoadMap(_map, null); }
    }

    // ---------- document ----------

    void LoadMap(Tilemap m, string? path)
    {
        _map = m;
        _path = path;
        _dirty = false;
        _history.Clear();
        RefreshAll();
    }

    void Snapshot()
    {
        _history.Push(_map);
        _dirty = true;
        UpdateTitle();
    }

    void RefreshAll()
    {
        _layer = Math.Clamp(_layer, 0, _map.Layers.Count - 1);
        ApplyZoom();
        RefreshTileset();
        RefreshLayers();
        Render();
        UpdateTitle();
    }

    // ponytail: full redraw per edit; redraw only changed cells if big maps lag
    void Render()
    {
        int w = _map.Width * _map.TileSize, h = _map.Height * _map.TileSize;
        if (_bmp is null || _bmp.PixelWidth != w || _bmp.PixelHeight != h)
            MapImage.Source = _bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        _bmp.WritePixels(new Int32Rect(0, 0, w, h), _map.Render(), w * 4, 0);
    }

    void UpdateTitle()
    {
        if (IsVisible && Window.GetWindow(this) is { } win)
            win.Title = $"PixelForge — {(_path is null ? "untitled map" : System.IO.Path.GetFileName(_path))}{(_dirty ? " *" : "")}";
    }

    public bool ConfirmDiscard()
    {
        if (!_dirty) return true;
        var r = MessageBox.Show("Save changes to the current map?", "PixelForge", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return r == MessageBoxResult.No || (r == MessageBoxResult.Yes && Save(_path));
    }

    // ---------- zoom / grid ----------

    int CellPx => _map.TileSize * _zoom;

    void ApplyZoom()
    {
        CanvasHost.Width = _map.Width * CellPx;
        CanvasHost.Height = _map.Height * CellPx;
        GridOverlay.Fill = GridToggle.IsChecked == true && CellPx >= 4 ? Ui.GridBrush(CellPx) : null;
        Status.Text = $"{_map.Width}×{_map.Height} tiles of {_map.TileSize}px   {_zoom}×";
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
        Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset + p.X / old * (_zoom - old));
        Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + p.Y / old * (_zoom - old));
    }

    void OnGridToggle(object sender, RoutedEventArgs e) => ApplyZoom();

    // ---------- painting ----------

    (int X, int Y) CellAt(MouseEventArgs e)
    {
        var p = e.GetPosition(CanvasHost);
        return ((int)Math.Floor(p.X / CellPx), (int)Math.Floor(p.Y / CellPx));
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
        var (x, y) = CellAt(e);
        if (!Tools.InBounds(_map.Width, _map.Height, x, y)) return;
        var tiles = _map.Layers[_layer].Tiles;

        if (PickBtn.IsChecked == true)
        {
            int t = _map.Pick(x, y);
            if (t != Tilemap.Empty) { SelectTile(t); PaintBtn.IsChecked = true; }
            return;
        }

        Snapshot();
        _strokeTile = EraseBtn.IsChecked == true || e.ChangedButton == MouseButton.Right ? Tilemap.Empty : _tile;
        if (FillBtn.IsChecked == true)
        {
            Tools.Fill(tiles, _map.Width, _map.Height, x, y, _strokeTile);
        }
        else
        {
            Tools.Set(tiles, _map.Width, _map.Height, x, y, _strokeTile);
            (_lastX, _lastY, _stroking) = (x, y, true);
            CanvasHost.CaptureMouse();
        }
        Render();
    }

    void OnCanvasMove(object sender, MouseEventArgs e)
    {
        var (x, y) = CellAt(e);
        Status.Text = $"{_map.Width}×{_map.Height} tiles of {_map.TileSize}px   {_zoom}×   {x}, {y}";
        if (_panFrom is Point from)
        {
            var now = e.GetPosition(Scroller);
            Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset - (now.X - from.X));
            Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset - (now.Y - from.Y));
            _panFrom = now;
            return;
        }
        if (!_stroking || (x, y) == (_lastX, _lastY)) return;
        Tools.Line(_map.Layers[_layer].Tiles, _map.Width, _map.Height, _lastX, _lastY, x, y, _strokeTile);
        (_lastX, _lastY) = (x, y);
        Render();
    }

    void OnCanvasUp(object sender, MouseButtonEventArgs e)
    {
        _stroking = false;
        _panFrom = null;
        CanvasHost.ReleaseMouseCapture();
    }

    // ---------- tileset ----------

    int TilesetScale => _map.Tileset is { } ts ? Math.Clamp(240 / ts.Width, 1, 8) : 1;

    void RefreshTileset()
    {
        if (_map.Tileset is not { } ts)
        {
            TilesetImage.Source = null;
            TilesetHost.Width = TilesetHost.Height = 0;
            SelectionRect.Visibility = Visibility.Collapsed;
            return;
        }
        int s = TilesetScale;
        TilesetImage.Source = ImageIO.ToBitmap(ts.Pixels, ts.Width, ts.Height);
        TilesetHost.Width = ts.Width * s;
        TilesetHost.Height = ts.Height * s;
        TilesetGrid.Fill = ts.TileSize * s >= 4 ? Ui.GridBrush(ts.TileSize * s) : null;
        SelectTile(Math.Clamp(_tile, 0, Math.Max(0, ts.Count - 1)));
    }

    void SelectTile(int t)
    {
        _tile = t;
        if (_map.Tileset is not { } ts) return;
        int cell = ts.TileSize * TilesetScale;
        SelectionRect.Visibility = Visibility.Visible;
        SelectionRect.Width = SelectionRect.Height = cell;
        Canvas.SetLeft(SelectionRect, t % ts.Columns * cell);
        Canvas.SetTop(SelectionRect, t / ts.Columns * cell);
    }

    void OnTilesetClick(object sender, MouseButtonEventArgs e)
    {
        if (_map.Tileset is not { } ts) return;
        var p = e.GetPosition(TilesetHost);
        int cell = ts.TileSize * TilesetScale;
        int cx = (int)(p.X / cell), cy = (int)(p.Y / cell);
        if (cx < ts.Columns && cy < ts.Rows) SelectTile(cy * ts.Columns + cx);
        if (EraseBtn.IsChecked == true) PaintBtn.IsChecked = true;
    }

    void SetTileset(uint[] px, int w, int h, string? source)
    {
        if (w < _map.TileSize || h < _map.TileSize)
            throw new InvalidOperationException($"Tileset image ({w}×{h}) is smaller than one {_map.TileSize}px tile.");
        Snapshot();
        _map.Tileset = new Tileset(px, w, h, _map.TileSize);
        _map.TilesetSource = source;
        RefreshTileset();
        Render();
    }

    static (uint[] Pixels, int Width, int Height) ReadImage(string path)
    {
        if (!path.EndsWith(".pxf", StringComparison.OrdinalIgnoreCase)) return ImageIO.LoadImage(path);
        var s = ProjectFile.Load(path);
        return (Compositor.Flatten(s, 0), s.Width, s.Height);
    }

    void OnLoadTileset(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Tileset (*.png;*.pxf;*.gif;*.bmp)|*.png;*.pxf;*.gif;*.bmp|All files|*.*" };
        if (dlg.ShowDialog() != true) return;
        try { var (px, w, h) = ReadImage(dlg.FileName); SetTileset(px, w, h, dlg.FileName); }
        catch (Exception ex) { Ui.Error(ex); }
    }

    void OnTilesetFromSprite(object sender, RoutedEventArgs e)
    {
        if (SpriteImage is null) return;
        try { var (px, w, h) = SpriteImage(); SetTileset(px, w, h, null); }
        catch (Exception ex) { Ui.Error(ex); }
    }

    void OnReloadTileset(object sender, RoutedEventArgs e)
    {
        if (_map.TilesetSource is not string src) { Ui.Error(new InvalidOperationException("This tileset was not loaded from a file.")); return; }
        try { var (px, w, h) = ReadImage(src); SetTileset(px, w, h, src); }
        catch (Exception ex) { Ui.Error(ex); }
    }

    // ---------- layers ----------

    bool _refreshingLayers;

    void RefreshLayers()
    {
        _refreshingLayers = true;
        Ui.FillLayers(LayersList, _map.Layers, _layer,
            visibilityChanged: () => { _dirty = true; UpdateTitle(); Render(); },
            rename: (layer, name) => { Snapshot(); layer.Name = name; });
        _refreshingLayers = false;
    }

    void OnLayerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshingLayers && LayersList.SelectedIndex >= 0)
            _layer = _map.Layers.Count - 1 - LayersList.SelectedIndex;
    }

    void OnAddLayer(object sender, RoutedEventArgs e)
    {
        Snapshot();
        _map.AddLayer(++_layer, $"Layer {_map.Layers.Count + 1}");
        RefreshLayers();
    }

    void OnDeleteLayer(object sender, RoutedEventArgs e)
    {
        if (_map.Layers.Count == 1) return;
        Snapshot();
        _map.RemoveLayer(_layer);
        _layer = Math.Max(0, _layer - 1);
        RefreshLayers();
        Render();
    }

    void OnLayerUp(object sender, RoutedEventArgs e) => MoveLayer(+1);
    void OnLayerDown(object sender, RoutedEventArgs e) => MoveLayer(-1);

    void MoveLayer(int delta)
    {
        int to = _layer + delta;
        if (to < 0 || to >= _map.Layers.Count) return;
        Snapshot();
        _map.MoveLayer(_layer, to);
        _layer = to;
        RefreshLayers();
        Render();
    }

    // ---------- undo ----------

    void OnUndo(object? sender, RoutedEventArgs? e) => Restore(_history.Undo(_map));
    void OnRedo(object? sender, RoutedEventArgs? e) => Restore(_history.Redo(_map));

    void Restore(Tilemap? m)
    {
        if (m is null || _stroking) return;
        _map = m;
        _dirty = true;
        RefreshAll();
    }

    // ---------- files ----------

    const string MapFilter = "PixelForge map (*.pxm)|*.pxm";
    string BaseName => System.IO.Path.GetFileNameWithoutExtension(_path) ?? "map";

    void OnNew(object? sender, RoutedEventArgs? e)
    {
        if (!ConfirmDiscard()) return;
        if (Ui.Ask(this, "New map", "Size in tiles and tile size (W x H, tile px):", $"{_map.Width}x{_map.Height}, {_map.TileSize}") is not string text) return;
        try
        {
            var n = text.Split(['x', 'X', '×', ',', ' '], StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
            if (n.Length != 3) throw new FormatException("Enter e.g. 20x15, 16");
            var m = new Tilemap(n[0], n[1], n[2]);
            if (_map.Tileset is { } ts && n[2] <= Math.Min(ts.Width, ts.Height))   // keep the tileset, re-sliced
                (m.Tileset, m.TilesetSource) = (ts with { TileSize = n[2] }, _map.TilesetSource);
            LoadMap(m, null);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentOutOfRangeException) { Ui.Error(ex); }
    }

    void OnOpen(object? sender, RoutedEventArgs? e)
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog { Filter = MapFilter };
        if (dlg.ShowDialog() != true) return;
        try { LoadMap(Tilemap.Load(dlg.FileName), dlg.FileName); }
        catch (Exception ex) { Ui.Error(ex); }
    }

    void OnSave(object? sender, RoutedEventArgs? e) => Save(_path);
    void OnSaveAs(object sender, RoutedEventArgs e) => Save(null);

    bool Save(string? path)
    {
        if (path is null)
        {
            var dlg = new SaveFileDialog { Filter = MapFilter, FileName = "map.pxm" };
            if (dlg.ShowDialog() != true) return false;
            path = dlg.FileName;
        }
        try
        {
            Tilemap.Save(_map, path);
            (_path, _dirty) = (path, false);
            UpdateTitle();
            return true;
        }
        catch (Exception ex) { Ui.Error(ex); return false; }
    }

    void OnExportPng(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = BaseName + ".png" };
        if (dlg.ShowDialog() != true) return;
        try { ImageIO.SavePng(dlg.FileName, _map.Render(), _map.Width * _map.TileSize, _map.Height * _map.TileSize); }
        catch (Exception ex) { Ui.Error(ex); }
    }

    void OnExportTiled(object sender, RoutedEventArgs e)
    {
        if (_map.Tileset is not { } ts) { Ui.Error(new InvalidOperationException("Load a tileset first.")); return; }
        var dlg = new SaveFileDialog { Filter = "Tiled map (*.tmj)|*.tmj", FileName = BaseName + ".tmj" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            string tsName = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName) + "-tileset.png";
            ImageIO.SavePng(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dlg.FileName)!, tsName), ts.Pixels, ts.Width, ts.Height);
            File.WriteAllText(dlg.FileName, TiledExport.Json(_map, tsName));
        }
        catch (Exception ex) { Ui.Error(ex); }
    }

    // ---------- keyboard ----------

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
            case Key.B when !ctrl: PaintBtn.IsChecked = true; break;
            case Key.E when !ctrl: EraseBtn.IsChecked = true; break;
            case Key.G when !ctrl: FillBtn.IsChecked = true; break;
            case Key.I when !ctrl: PickBtn.IsChecked = true; break;
            default: e.Handled = false; break;
        }
    }
}
