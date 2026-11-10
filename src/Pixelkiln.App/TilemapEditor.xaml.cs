using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Pixelkiln.Core;

namespace Pixelkiln.App;

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
    /// <summary>Set by MainWindow: the Sprite tab's file when it has one (saved first if it has changes), else null.</summary>
    public Func<string?>? SpriteFile { get; set; }
    /// <summary>Set by MainWindow: shows a sprite file in the Sprite tab with this pixel rectangle selected.</summary>
    public Action<string, Int32Rect>? EditTileInSprite { get; set; }

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
        RefreshLayers();   // ApplyZoom already placed the objects
        Render();
        UpdateTitle();
    }

    uint[] _picture = [];        // the map as drawn, kept so a stroke can redraw only its cells
    Int32Rect? _touched;         // cells changed since the last draw (see Touch)

    /// <summary>Draws the whole map (after loading, undo, layer or tileset changes).</summary>
    void Render()
    {
        int w = _map.Width * _map.TileSize, h = _map.Height * _map.TileSize;
        if (_bmp is null || _bmp.PixelWidth != w || _bmp.PixelHeight != h)
            MapImage.Source = _bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        if (_picture.Length != w * h) _picture = new uint[w * h];
        _touched = null;
        _map.RenderCells(_picture, 0, 0, _map.Width, _map.Height);
        _bmp.WritePixels(new Int32Rect(0, 0, w, h), _picture, w * 4, 0);
        RenderCollision();
    }

    /// <summary>Notes that a tool changed these cells; <see cref="RenderTouched"/> then redraws only them.</summary>
    void Touch(int x, int y, int w = 1, int h = 1)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y), x1 = Math.Min(_map.Width, x + w), y1 = Math.Min(_map.Height, y + h);
        if (x1 <= x0 || y1 <= y0) return;
        if (_touched is { } t)
            (x0, y0, x1, y1) = (Math.Min(x0, t.X), Math.Min(y0, t.Y), Math.Max(x1, t.X + t.Width), Math.Max(y1, t.Y + t.Height));
        _touched = new Int32Rect(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>Redraws the cells a stroke touched instead of the whole map (big maps stay quick to paint).</summary>
    void RenderTouched()
    {
        if (_touched is not { } t || _bmp is null) return;
        _touched = null;
        int size = _map.TileSize;
        _map.RenderCells(_picture, t.X, t.Y, t.Width, t.Height);
        var area = new Int32Rect(t.X * size, t.Y * size, t.Width * size, t.Height * size);
        _bmp.WritePixels(area, _picture, _bmp.PixelWidth * 4, area.X, area.Y);   // that part of the picture, to the same place
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

    void UpdateTitle() => Ui.ShowTitle(this, _path, _dirty, "untitled map");

    public bool ConfirmDiscard() => Ui.ConfirmDiscard(_dirty, "map", () => Save(_path));

    // ---------- zoom / grid ----------

    int CellPx => _map.TileSize * _zoom;

    void ApplyZoom()
    {
        CanvasHost.Width = _map.Width * CellPx;
        CanvasHost.Height = _map.Height * CellPx;
        // grid in the theme's muted text colour: light lines on a dark map background, dark ones on a light one
        var line = ((SolidColorBrush)FindResource("Text3Brush")).Color;
        GridOverlay.Fill = GridToggle.IsChecked == true && CellPx >= 4 ? Ui.GridBrush(CellPx, Color.FromArgb(0x60, line.R, line.G, line.B)) : null;
        Status.Text = $"{_map.Width}×{_map.Height} tiles of {_map.TileSize} px";
        MapInfo.Text = $"Tile {_map.TileSize} px · {_map.Width} × {_map.Height}";
        ZoomText.Text = $"{_zoom * 100} %";
        RefreshObjects();
    }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        ZoomStep(Math.Sign(e.Delta), e.GetPosition(CanvasHost));
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
        // a saved sprite is linked by its file, so later changes to it reach the map; an unsaved one is copied in
        if (SpriteFile?.Invoke() is string file) { UseTileset(file); return; }
        if (SpriteImage is null) return;
        try { var (px, w, h) = SpriteImage(); SetTileset(px, w, h, null); }
        catch (Exception ex) { Ui.Error(ex); }
    }

    /// <summary>Uses a picture or sprite file as the tileset and remembers where it came from (the Sprite tab's "Use as tileset").</summary>
    public void UseTileset(string file)
    {
        try { var (px, w, h) = AssetLoader.LoadImage(file); SetTileset(px, w, h, file); }
        catch (Exception ex) { Ui.Error(ex); }
    }

    /// <summary>The file the tileset comes from: its project asset, else the file it was loaded from; null when it only lives in the map.</summary>
    string? TilesetFile => (_map.TilesetAssetId is Guid id ? App.Project?.Resolve(id) : null) ?? _map.TilesetSource;

    /// <summary>A file was saved in another tab: if it is this map's tileset, show the new tiles.</summary>
    public void OnAssetSaved(string path)
    {
        if (!string.Equals(TilesetFile, path, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var (px, w, h) = AssetLoader.LoadImage(path);
            if (w < _map.TileSize || h < _map.TileSize) return;   // keep the old tiles rather than break the map
            _map.Tileset = new Tileset(px, w, h, _map.TileSize);
            // a project map reads its tileset again when it opens; a loose map only has its own copy, so it needs saving
            if (_map.TilesetAssetId is null) { _dirty = true; UpdateTitle(); }
            RefreshTileset();
            Render();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException) { Status.Text = $"The tileset could not be read again: {ex.Message}"; }
    }

    /// <summary>
    /// Opens the tileset's sprite in the Sprite tab with the selected tile marked. A tileset that only lives in the
    /// map (or is a plain picture) is saved as a sprite first, so the map can follow the edits.
    /// </summary>
    void OnEditTile(object sender, RoutedEventArgs e)
    {
        if (_map.Tileset is not { } ts) { Ui.Error(new InvalidOperationException("Load a tileset first.")); return; }
        string? file = TilesetFile;
        if (file is null || !file.EndsWith(".pxf", StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
        {
            if (MessageBox.Show(Window.GetWindow(this), "To edit its tiles, the tileset is saved as a sprite file that this map then uses. Save it now?",
                    "Edit tile", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            var dlg = new SaveFileDialog
            {
                Filter = "Pixelkiln sprite (*.pxf)|*.pxf", FileName = (file is null ? BaseName + "-tiles" : System.IO.Path.GetFileNameWithoutExtension(file)) + ".pxf",
                InitialDirectory = App.Project?.Root ?? System.IO.Path.GetDirectoryName(_path) ?? "",
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var sprite = new Sprite(ts.Width, ts.Height);
                ts.Pixels.CopyTo(sprite.Cel(0, 0), 0);
                ProjectFile.Save(sprite, dlg.FileName);
                ProjectCommands.OfferProjectFor(Window.GetWindow(this), dlg.FileName);
                App.AddRecent(dlg.FileName);
                SetTileset(ts.Pixels, ts.Width, ts.Height, dlg.FileName);
                file = dlg.FileName;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { Ui.Error(ex); return; }
        }
        int size = ts.TileSize;
        EditTileInSprite?.Invoke(file, new Int32Rect(_tsSel.X * size, _tsSel.Y * size, _tsSel.Width * size, _tsSel.Height * size));
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

    const string MapFilter = "Pixelkiln map (*.pxm)|*.pxm";
    string BaseName => System.IO.Path.GetFileNameWithoutExtension(_path) ?? "map";

    void OnNew(object? sender, RoutedEventArgs? e) => NewMap();

    /// <summary>Asks for the size and makes an empty map (File › New map and the start screen); the tileset is kept.</summary>
    public void NewMap()
    {
        if (!ConfirmDiscard()) return;
        if (Ui.AskFields(this, "New map",
                ("_Width in tiles", _map.Width.ToString(), null),
                ("_Height in tiles", _map.Height.ToString(), null),
                ("_Tile size in pixels", _map.TileSize.ToString(), null)) is not { } f) return;
        try
        {
            int[] n = [.. f.Select(int.Parse)];
            var m = new Tilemap(n[0], n[1], n[2]);
            if (_map.Tileset is { } ts && n[2] <= Math.Min(ts.Width, ts.Height))   // keep the tileset, re-sliced
                (m.Tileset, m.TilesetSource, m.TilesetAssetId) = (ts with { TileSize = n[2] }, _map.TilesetSource, _map.TilesetAssetId);
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
            // first the project (it may be made now), so a tileset file inside it is saved as a link, not only a copy
            ProjectCommands.OfferProjectFor(Window.GetWindow(this), path);
            if (_map.TilesetAssetId is null && _map.TilesetSource is string source && App.Project?.FindByPath(source) is { } asset)
                _map.TilesetAssetId = asset.Id;
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
            case Key.X when !ctrl: TurnStamp(TileFlip.H); break;
            case Key.Y when !ctrl: TurnStamp(TileFlip.V); break;
            case Key.R when !ctrl: TurnStamp(TileFlip.RotateCw); break;
            case Key.K when !ctrl: CollisionToggle.IsChecked = CollisionToggle.IsChecked != true; RenderCollision(); break;
            case Key.Delete when _selObj is not null: OnDeleteObject(null, null); break;
            default: e.Handled = false; break;
        }
    }
}
