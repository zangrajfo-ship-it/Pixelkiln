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
    readonly History<Tilemap> _history = new(m => m.Clone(), App.Settings.UndoLimit, m => 4L * m.Width * m.Height * m.Layers.Count);
    int _layer, _zoom = 2;
    string? _path;
    bool _dirty;
    WriteableBitmap? _bmp;

    /// <summary>Set by MainWindow: the Sprite tab's current image.</summary>
    public Func<(uint[] Pixels, int Width, int Height)>? SpriteImage { get; set; }

    public TilemapEditor()
    {
        InitializeComponent();
        Loaded += Init;
        IsVisibleChanged += (_, _) => UpdateTitle();

        void Init(object s, RoutedEventArgs e) { Loaded -= Init; LoadMap(_map, _path); UpdateToolHint(); }   // keeps a file opened before the first layout
        App.SettingsChanged += () => { ApplyZoom(); RefreshTileset(); };
        IsKeyboardFocusedChanged += (_, _) => Scroller.BorderBrush = IsKeyboardFocused ? (Brush)FindResource("FocusRing") : Brushes.Transparent;
    }

    // ---------- document ----------

    void LoadMap(Tilemap m, string? path)
    {
        _map = m;
        _path = path;
        _dirty = false;
        _history.Clear();
        _selObj = null;
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
        RefreshTerrains();
        RefreshLayers();
        RefreshObjects();
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
        RenderCollision();
    }

    /// <summary>Collision view: one pixel per cell, red where the top tile is solid, blue where it is a one-way platform.</summary>
    void RenderCollision()
    {
        CollisionImage.Visibility = CollisionToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (CollisionToggle.IsChecked != true) return;
        var px = new uint[_map.Width * _map.Height];
        for (int y = 0; y < _map.Height; y++)
            for (int x = 0; x < _map.Width; x++)
                if (_map.TileCollisions.GetValueOrDefault(_map.Pick(x, y)) is { } c)
                    px[y * _map.Width + x] = c.OneWay ? 0x7300B4FFu : 0x73E8505Bu;
        CollisionImage.Source = ImageIO.ToBitmap(px, _map.Width, _map.Height);
    }

    void OnCollisionToggle(object sender, RoutedEventArgs e) => RenderCollision();

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
        GridOverlay.Fill = GridToggle.IsChecked == true && CellPx >= 4 ? Ui.GridBrush(CellPx, Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)) : null;   // light lines on the dark map
        Status.Text = $"{_map.Width}×{_map.Height} tiles of {_map.TileSize} px";
        MapInfo.Text = $"Tile {_map.TileSize} px · {_map.Width} × {_map.Height}";
        ZoomText.Text = $"{_zoom * 100} %";
        RefreshObjects();
    }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        int i = Array.IndexOf(ZoomLevels, _zoom) + Math.Sign(e.Delta);
        if (i >= 0 && i < ZoomLevels.Length) ZoomTo(ZoomLevels[i], e.GetPosition(CanvasHost));
    }

    /// <summary>Changes zoom and keeps the canvas point <paramref name="anchor"/> in place on screen.</summary>
    void ZoomTo(int zoom, Point anchor)
    {
        int old = _zoom;
        _zoom = zoom;
        ApplyZoom();
        Scroller.UpdateLayout();
        Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset + anchor.X / old * (_zoom - old));
        Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + anchor.Y / old * (_zoom - old));
    }

    void OnGridToggle(object sender, RoutedEventArgs e) => ApplyZoom();

    void SetTileset(uint[] px, int w, int h, string? source)
    {
        if (w < _map.TileSize || h < _map.TileSize)
            throw new InvalidOperationException($"Tileset image ({w}×{h}) is smaller than one {_map.TileSize}px tile.");
        Snapshot();
        _map.Tileset = new Tileset(px, w, h, _map.TileSize);
        _map.TilesetSource = source;
        // remember the tileset by asset id when it lives in the open project (ADR-0001)
        _map.TilesetAssetId = source is not null && App.Project?.FindByPath(source) is { } asset ? asset.Id : null;
        RefreshTileset();
        Render();
    }

    void OnLoadTileset(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Tileset (*.png;*.pxf;*.gif;*.bmp)|*.png;*.pxf;*.gif;*.bmp|All files|*.*" };
        if (dlg.ShowDialog() != true) return;
        try { var (px, w, h) = AssetLoader.LoadImage(dlg.FileName); SetTileset(px, w, h, dlg.FileName); }
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
        try { var (px, w, h) = AssetLoader.LoadImage(src); SetTileset(px, w, h, src); }
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
        int? selId = _selObj?.Id;
        _map = m;
        _selObj = _map.Objects.FirstOrDefault(o => o.Id == selId);
        _dirty = true;
        RefreshAll();
    }

    void OnMapSize(object sender, RoutedEventArgs e)
    {
        if (Ui.AskSize(this, "Map size (tiles)", _map.Width, _map.Height, anchor: true) is not (int w, int h, int ax, int ay)) return;
        try
        {
            var resized = _map.Resized(w, h, PixelOps.AnchorOffset(_map.Width, w, ax), PixelOps.AnchorOffset(_map.Height, h, ay));
            Snapshot();
            _map = resized;
            _selObj = null;
            RefreshAll();
        }
        catch (ArgumentOutOfRangeException ex) { Ui.Error(ex); }
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
        if (dlg.ShowDialog() == true) OpenPath(dlg.FileName, confirmed: true);
    }

    public string[] FileExtensions => [".pxm"];
    public bool IsDirty => _dirty;
    public string? CurrentPath => _path;

    /// <summary>The asset browser renamed or moved the open file: keep saving to the new place.</summary>
    public void OnFileMoved(string oldPath, string newPath)
    {
        if (!string.Equals(_path, oldPath, StringComparison.OrdinalIgnoreCase)) return;
        _path = newPath;
        UpdateTitle();
    }

    public void OpenPath(string path, bool confirmed = false)
    {
        if (!confirmed && !ConfirmDiscard()) return;
        try
        {
            var m = Tilemap.Load(path);
            if (App.Project is { } p) AssetLoader.RefreshTileset(m, p);   // the tileset asset may have changed since
            LoadMap(m, path);
            App.AddRecent(path);
        }
        catch (Exception ex) { Ui.Error(ex); }
    }

    public void WriteAutosave(string file) => Tilemap.Save(_map, file);

    public void RestoreAutosave(string file, string? originalPath)
    {
        LoadMap(Tilemap.Load(file), originalPath);
        _dirty = true;
        UpdateTitle();
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
            App.AddRecent(path);
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
            case Key.S when ctrl && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift): Save(null); break;
            case Key.S when ctrl: OnSave(null, null); break;
            case Key.OemPlus or Key.Add when ctrl: ZoomStep(+1); break;
            case Key.OemMinus or Key.Subtract when ctrl: ZoomStep(-1); break;
            case Key.D0 or Key.NumPad0 when ctrl: ZoomFit(); break;
            case Key.N when ctrl: OnNew(null, null); break;
            case Key.O when ctrl: OnOpen(null, null); break;
            case Key.G when ctrl: GridToggle.IsChecked = GridToggle.IsChecked != true; ApplyZoom(); break;
            case Key.B when !ctrl: SetTool(PaintBtn); break;
            case Key.E when !ctrl: SetTool(EraseBtn); break;
            case Key.G when !ctrl: SetTool(FillBtn); break;
            case Key.I when !ctrl: SetTool(PickBtn); break;
            case Key.A when !ctrl: SetTool(AutoBtn); break;
            case Key.O when !ctrl: SetTool(ObjectBtn); break;
            case Key.K when !ctrl: CollisionToggle.IsChecked = CollisionToggle.IsChecked != true; RenderCollision(); break;
            case Key.Delete when _selObj is not null: OnDeleteObject(null, null); break;
            default: e.Handled = false; break;
        }
    }
}
