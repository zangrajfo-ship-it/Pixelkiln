using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.App;

/// <summary>Map tools: stamp painting, erase, pattern fill, pick, auto-tile; tileset selection and terrains.</summary>
public partial class TilemapEditor
{
    bool _stroking;
    RadioButton _strokeTool = null!;
    int _lastX, _lastY;
    bool _strokeOn;                  // auto-tile: paint (true) or erase
    (int X, int Y) _origin;          // stamp alignment / pick start
    (int X, int Y) _lastGrid;
    Point? _panFrom;

    // stamp = block of tile indices (-1 = hole), from the tileset or picked from the map
    int[] _stamp = [0];
    int _sw = 1, _sh = 1;
    Int32Rect _tsSel = new(0, 0, 1, 1);   // tileset selection, in tiles
    (int X, int Y)? _tsDragFrom;

    RadioButton[]? _tools;   // filled on first use: the named buttons exist only after InitializeComponent
    RadioButton CurrentTool => (_tools ??= [PaintBtn, EraseBtn, FillBtn, PickBtn, AutoBtn, ObjectBtn]).First(r => r.IsChecked == true);

    void SetTool(RadioButton tool)
    {
        tool.IsChecked = true;
        OnToolClick(tool, null);
    }

    void OnToolClick(object sender, RoutedEventArgs? e)
    {
        StampPreview.Visibility = Visibility.Collapsed;
        if (CurrentTool != ObjectBtn && _selObj is not null) SelectObject(null);
        UpdateToolHint();
    }

    static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);
    static int Mod(int a, int b) => (a % b + b) % b;

    (int X, int Y) CellAt(MouseEventArgs e)
    {
        var p = e.GetPosition(CanvasHost);
        return ((int)Math.Floor(p.X / CellPx), (int)Math.Floor(p.Y / CellPx));
    }

    // ---------- mouse on the map ----------

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
        bool right = e.ChangedButton == MouseButton.Right;
        var tool = CurrentTool;

        if (tool == ObjectBtn)
        {
            ObjectDown(PixelAt(e), right);
            BeginStroke(tool, 0, 0);
            return;
        }

        var (x, y) = CellAt(e);
        if (!Tools.InBounds(_map.Width, _map.Height, x, y)) return;

        if (tool == PickBtn)
        {
            _origin = (x, y);
            ShowPickRect(x, y);
            BeginStroke(tool, x, y);
            return;
        }

        var tiles = _map.Layers[_layer].Tiles;
        if (tool == AutoBtn)
        {
            if (CurrentTerrain is not { } terrain || _map.Tileset is null)
            {
                Ui.Error(new InvalidOperationException("Add a terrain first: select a 4×4 block in the tileset and press + next to Terrain."));
                return;
            }
            Snapshot();
            _strokeOn = !right;
            Autotile.Apply(_map, _map.Layers[_layer], terrain, x, y, _strokeOn);
        }
        else if (tool == FillBtn)
        {
            Snapshot();
            if (right) Tools.Fill(tiles, _map.Width, _map.Height, x, y, Tilemap.Empty);
            else Tools.FillPattern(tiles, _map.Width, _map.Height, x, y, (cx, cy) => _stamp[Mod(cy - y, _sh) * _sw + Mod(cx - x, _sw)]);
            Render();
            return;
        }
        else if (tool == EraseBtn || right)
        {
            Snapshot();
            tool = EraseBtn;
            Tools.Set(tiles, _map.Width, _map.Height, x, y, Tilemap.Empty);
        }
        else   // paint
        {
            Snapshot();
            _origin = (x, y);
            _lastGrid = (0, 0);
            PaintStamp(x, y);
        }
        BeginStroke(tool, x, y);
        Render();
    }

    void BeginStroke(RadioButton tool, int x, int y)
    {
        (_stroking, _strokeTool, _lastX, _lastY) = (true, tool, x, y);
        CanvasHost.CaptureMouse();
    }

    void OnCanvasMove(object sender, MouseEventArgs e)
    {
        var (x, y) = CellAt(e);
        Status.Text = $"{_map.Width}×{_map.Height} tiles of {_map.TileSize} px" + (Tools.InBounds(_map.Width, _map.Height, x, y) ? $"   cell {x}, {y}   tile {_map.Pick(x, y) + 1}" : "");
        if (_panFrom is Point from)
        {
            var now = e.GetPosition(Scroller);
            Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset - (now.X - from.X));
            Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset - (now.Y - from.Y));
            _panFrom = now;
            return;
        }
        UpdateStampPreview(x, y);
        if (!_stroking) return;

        if (_strokeTool == ObjectBtn) { ObjectMove(PixelAt(e)); return; }
        if (_strokeTool == PickBtn) { ShowPickRect(x, y); return; }
        if ((x, y) == (_lastX, _lastY)) return;

        var tiles = _map.Layers[_layer].Tiles;
        if (_strokeTool == PaintBtn)
        {
            var g = (FloorDiv(x - _origin.X, _sw), FloorDiv(y - _origin.Y, _sh));
            foreach (var (gx, gy) in Tools.LinePoints(_lastGrid.X, _lastGrid.Y, g.Item1, g.Item2))
                PaintStamp(_origin.X + gx * _sw, _origin.Y + gy * _sh);
            _lastGrid = g;
        }
        else if (_strokeTool == EraseBtn)
        {
            Tools.Line(tiles, _map.Width, _map.Height, _lastX, _lastY, x, y, Tilemap.Empty);
        }
        else if (_strokeTool == AutoBtn && CurrentTerrain is { } terrain)
        {
            foreach (var (lx, ly) in Tools.LinePoints(_lastX, _lastY, x, y))
                Autotile.Apply(_map, _map.Layers[_layer], terrain, lx, ly, _strokeOn);
        }
        (_lastX, _lastY) = (x, y);
        Render();
    }

    void OnCanvasUp(object sender, MouseButtonEventArgs e)
    {
        if (_stroking && _strokeTool == PickBtn) PickStamp(CellAt(e));
        if (_stroking && _strokeTool == ObjectBtn) ObjectUp();
        _stroking = false;
        _panFrom = null;
        CanvasHost.ReleaseMouseCapture();
    }

    void OnCanvasLeave(object sender, MouseEventArgs e) => StampPreview.Visibility = Visibility.Collapsed;

    void PaintStamp(int cx, int cy)
    {
        var tiles = _map.Layers[_layer].Tiles;
        for (int j = 0; j < _sh; j++)
            for (int i = 0; i < _sw; i++)
                if (_stamp[j * _sw + i] >= 0) Tools.Set(tiles, _map.Width, _map.Height, cx + i, cy + j, _stamp[j * _sw + i]);
    }

    // ---------- pick ----------

    (int X, int Y, int W, int H) PickArea((int X, int Y) end)
    {
        int x0 = Math.Clamp(Math.Min(_origin.X, end.X), 0, _map.Width - 1), y0 = Math.Clamp(Math.Min(_origin.Y, end.Y), 0, _map.Height - 1);
        int x1 = Math.Clamp(Math.Max(_origin.X, end.X), 0, _map.Width - 1), y1 = Math.Clamp(Math.Max(_origin.Y, end.Y), 0, _map.Height - 1);
        return (x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    void ShowPickRect(int x, int y)
    {
        var (ax, ay, w, h) = PickArea((x, y));
        PickRect.Visibility = Visibility.Visible;
        Canvas.SetLeft(PickRect, ax * CellPx);
        Canvas.SetTop(PickRect, ay * CellPx);
        PickRect.Width = w * CellPx;
        PickRect.Height = h * CellPx;
    }

    /// <summary>Copies the visible top tiles of the dragged area into the stamp, then switches to Paint.</summary>
    void PickStamp((int X, int Y) end)
    {
        PickRect.Visibility = Visibility.Collapsed;
        var (x, y, w, h) = PickArea(end);
        var stamp = new int[w * h];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++) stamp[j * w + i] = _map.Pick(x + i, y + j);
        if (stamp.All(t => t == Tilemap.Empty)) return;
        SetStamp(stamp, w, h);
        SelectionRect.Visibility = Visibility.Collapsed;   // the stamp no longer matches a tileset area
        SetTool(PaintBtn);
    }

    // ---------- stamp + preview ----------

    void SetStamp(int[] stamp, int w, int h)
    {
        (_stamp, _sw, _sh) = (stamp, w, h);
        if (_map.Tileset is not { } ts) { BrushPreview.Source = StampPreview.Source = null; return; }
        int t = ts.TileSize, pw = w * t;
        var px = new uint[pw * h * t];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                int idx = stamp[j * w + i];
                if (idx < 0 || idx >= ts.Count) continue;
                var tile = ts.Tile(idx);
                for (int y = 0; y < t; y++) Array.Copy(tile, y * t, px, (j * t + y) * pw + i * t, t);
            }
        BrushPreview.Source = StampPreview.Source = ImageIO.ToBitmap(px, pw, h * t);
    }

    void UpdateStampPreview(int x, int y)
    {
        bool show = CurrentTool == PaintBtn && StampPreview.Source is not null && (!_stroking || _strokeTool == PaintBtn);
        StampPreview.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        if (_stroking)   // snap to the stroke's stamp grid
            (x, y) = (_origin.X + FloorDiv(x - _origin.X, _sw) * _sw, _origin.Y + FloorDiv(y - _origin.Y, _sh) * _sh);
        Canvas.SetLeft(StampPreview, x * CellPx);
        Canvas.SetTop(StampPreview, y * CellPx);
        StampPreview.Width = _sw * CellPx;
        StampPreview.Height = _sh * CellPx;
    }

    // ---------- tileset ----------

    int TilesetScale => _map.Tileset is { } ts ? Math.Clamp(264 / ts.Width, 1, 8) : 1;   // fills the 297 px panel

    void RefreshTileset()
    {
        if (_map.Tileset is not { } ts)
        {
            TilesetImage.Source = null;
            TilesetHost.Width = TilesetHost.Height = 0;
            SelectionRect.Visibility = Visibility.Collapsed;
            BrushPreview.Source = StampPreview.Source = null;
            TilesetName.Text = "No tileset";
            UpdateTileProps();
            return;
        }
        string name = _map.TilesetSource is { } src ? System.IO.Path.GetFileName(src)
            : _map.TilesetAssetId is Guid id && App.Project?.Find(id) is { } asset ? System.IO.Path.GetFileName(asset.Path) : "Embedded";
        TilesetName.Text = $"{name} · {ts.TileSize} px · {ts.Count} tiles";
        int s = TilesetScale;
        TilesetImage.Source = ImageIO.ToBitmap(ts.Pixels, ts.Width, ts.Height);
        TilesetHost.Width = ts.Width * s;
        TilesetHost.Height = ts.Height * s;
        TilesetGrid.Fill = ts.TileSize * s >= 4 ? Ui.GridBrush(ts.TileSize * s) : null;
        if (_tsSel.X + _tsSel.Width > ts.Columns || _tsSel.Y + _tsSel.Height > ts.Rows) _tsSel = new Int32Rect(0, 0, 1, 1);
        SetTilesetSelection(_tsSel);
        DrawCollisionMarks();
    }

    // ---------- tile collisions ----------

    /// <summary>Red boxes (solid) or blue tops (one-way) over the tiles that have collision.</summary>
    void DrawCollisionMarks()
    {
        CollisionMarks.Children.Clear();
        if (_map.Tileset is not { } ts) return;
        int s = TilesetScale;
        foreach (var (tile, c) in _map.TileCollisions.Where(kv => kv.Key < ts.Count))
        {
            var color = c.OneWay ? System.Windows.Media.Colors.DeepSkyBlue : System.Windows.Media.Colors.Red;
            var r = new System.Windows.Shapes.Rectangle
            {
                Width = c.W * s, Height = c.H * s, StrokeThickness = 1,
                Stroke = new System.Windows.Media.SolidColorBrush(color),
                Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x50, color.R, color.G, color.B)),
            };
            Canvas.SetLeft(r, (tile % ts.Columns * ts.TileSize + c.X) * s);
            Canvas.SetTop(r, (tile / ts.Columns * ts.TileSize + c.Y) * s);
            CollisionMarks.Children.Add(r);
        }
        UpdateTileProps();
        if (_bmp is not null) RenderCollision();
    }

    /// <summary>TILE PROPERTIES: the first selected tileset tile, its number and collision.</summary>
    void UpdateTileProps()
    {
        bool has = _map.Tileset is not null;
        SolidToggle.IsEnabled = OneWayToggle.IsEnabled = has;
        if (_map.Tileset is not { } ts)
        {
            TilePreview.Source = null;
            (TileTitle.Text, TileInfo.Text) = ("No tile", "Load a tileset");
            SolidToggle.IsChecked = OneWayToggle.IsChecked = false;
            return;
        }
        var sel = SelectedTiles().ToList();
        int first = sel[0];
        TilePreview.Source = ImageIO.ToBitmap(ts.Tile(first), ts.TileSize, ts.TileSize);
        TileTitle.Text = sel.Count == 1 ? $"Tile #{first + 1}" : $"{sel.Count} tiles";
        TileInfo.Text = $"ID #{first + 1} · {ts.TileSize}×{ts.TileSize}";
        // checked only when every selected tile has it, so a click sets it for all of them
        SolidToggle.IsChecked = sel.All(t => _map.TileCollisions.GetValueOrDefault(t) is { OneWay: false });
        OneWayToggle.IsChecked = sel.All(t => _map.TileCollisions.GetValueOrDefault(t) is { OneWay: true });
    }

    void OnSolidToggle(object sender, RoutedEventArgs e) =>
        SetTileCollision(SolidToggle.IsChecked == true ? SolidBox : null);

    void OnOneWayToggle(object sender, RoutedEventArgs e) =>
        SetTileCollision(OneWayToggle.IsChecked == true ? OneWayBox : null);

    IEnumerable<int> SelectedTiles()
    {
        if (_map.Tileset is not { } ts) yield break;
        for (int j = 0; j < _tsSel.Height; j++)
            for (int i = 0; i < _tsSel.Width; i++) yield return (_tsSel.Y + j) * ts.Columns + _tsSel.X + i;
    }

    // the two usual collision boxes: the whole tile, or a thin top to jump through from below
    TileCollision SolidBox => new(0, 0, _map.TileSize, _map.TileSize);
    TileCollision OneWayBox => new(0, 0, _map.TileSize, Math.Max(1, _map.TileSize / 4), OneWay: true);

    /// <summary>Sets (or with null clears) the collision of every selected tileset tile.</summary>
    void SetTileCollision(TileCollision? c)
    {
        if (_map.Tileset is null) { Ui.Error(new InvalidOperationException("Load a tileset first.")); return; }
        Snapshot();
        foreach (int t in SelectedTiles())
            if (c is null) _map.TileCollisions.Remove(t);
            else _map.TileCollisions[t] = c;
        DrawCollisionMarks();
    }

    void CustomTileCollision()
    {
        if (_map.Tileset is null) { Ui.Error(new InvalidOperationException("Load a tileset first.")); return; }
        int n = _map.TileSize;
        var first = _map.TileCollisions.GetValueOrDefault(SelectedTiles().First()) ?? new TileCollision(0, 0, n, n);
        string? text = Ui.Ask(this, "Tile collision", $"x y width height [oneway] [category 0-31], in pixels inside a {n}px tile",
            $"{first.X} {first.Y} {first.W} {first.H}{(first.OneWay ? " oneway" : "")} {first.Category}");
        if (text is null) return;
        var p = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool oneWay = p.Contains("oneway", StringComparer.OrdinalIgnoreCase);
        var nums = p.Where(x => !x.Equals("oneway", StringComparison.OrdinalIgnoreCase)).Select(x => int.TryParse(x, out int v) ? v : -1).ToArray();
        if (nums.Length is < 4 or > 5 || nums.Any(v => v < 0) || nums[2] < 1 || nums[3] < 1 || nums[0] + nums[2] > n || nums[1] + nums[3] > n || nums.ElementAtOrDefault(4) > 31)
        {
            Ui.Error(new FormatException($"Use four whole numbers that fit in the {n}px tile, then optionally “oneway” and a category from 0 to 31."));
            return;
        }
        SetTileCollision(new TileCollision(nums[0], nums[1], nums[2], nums[3], oneWay, nums.ElementAtOrDefault(4)));
    }

    (int X, int Y) TilesetCell(MouseEventArgs e, Tileset ts)
    {
        var p = e.GetPosition(TilesetHost);
        int cell = ts.TileSize * TilesetScale;
        return (Math.Clamp((int)(p.X / cell), 0, ts.Columns - 1), Math.Clamp((int)(p.Y / cell), 0, ts.Rows - 1));
    }

    void OnTilesetDown(object sender, MouseButtonEventArgs e)
    {
        if (_map.Tileset is not { } ts) return;
        var c = TilesetCell(e, ts);
        TilesetHost.Focus();
        _tsDragFrom = c;
        TilesetHost.CaptureMouse();
        SetTilesetSelection(new Int32Rect(c.X, c.Y, 1, 1));
    }

    void OnTilesetMove(object sender, MouseEventArgs e)
    {
        if (_tsDragFrom is not { } a || _map.Tileset is not { } ts) return;
        var b = TilesetCell(e, ts);
        SetTilesetSelection(new Int32Rect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X) + 1, Math.Abs(a.Y - b.Y) + 1));
    }

    void OnTilesetUp(object sender, MouseButtonEventArgs e)
    {
        _tsDragFrom = null;
        TilesetHost.ReleaseMouseCapture();
        if (CurrentTool != FillBtn && CurrentTool != AutoBtn) SetTool(PaintBtn);
    }

    /// <summary>Arrows move the tileset selection, Shift+arrows grow/shrink it (WCAG 2.1.1).</summary>
    void OnTilesetKey(object sender, KeyEventArgs e)
    {
        if (_map.Tileset is not { } ts) return;
        int dx = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0, dy = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
        if (dx == 0 && dy == 0) return;
        var r = _tsSel;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            r = new Int32Rect(r.X, r.Y, Math.Clamp(r.Width + dx, 1, ts.Columns - r.X), Math.Clamp(r.Height + dy, 1, ts.Rows - r.Y));
        else
            r = new Int32Rect(Math.Clamp(r.X + dx, 0, ts.Columns - 1), Math.Clamp(r.Y + dy, 0, ts.Rows - 1), 1, 1);
        SetTilesetSelection(r);
        TilesetHost.BringIntoView(new Rect(r.X * ts.TileSize * TilesetScale, r.Y * ts.TileSize * TilesetScale, ts.TileSize * TilesetScale, ts.TileSize * TilesetScale));
        e.Handled = true;
    }

    void SetTilesetSelection(Int32Rect r)
    {
        if (_map.Tileset is not { } ts) return;
        _tsSel = r;
        int cell = ts.TileSize * TilesetScale;
        SelectionRect.Visibility = Visibility.Visible;
        SelectionRect.Width = r.Width * cell;
        SelectionRect.Height = r.Height * cell;
        Canvas.SetLeft(SelectionRect, r.X * cell);
        Canvas.SetTop(SelectionRect, r.Y * cell);
        var stamp = new int[r.Width * r.Height];
        for (int j = 0; j < r.Height; j++)
            for (int i = 0; i < r.Width; i++) stamp[j * r.Width + i] = (r.Y + j) * ts.Columns + r.X + i;
        SetStamp(stamp, r.Width, r.Height);
        UpdateTileProps();
        System.Windows.Automation.AutomationProperties.SetItemStatus(TilesetHost,
            r.Width * r.Height == 1 ? $"Tile {stamp[0] + 1} of {ts.Count}" : $"{r.Width} by {r.Height} tiles from tile {stamp[0] + 1}");
    }

    // ---------- terrains ----------

    TerrainRule? CurrentTerrain => TerrainBox.SelectedIndex >= 0 && TerrainBox.SelectedIndex < _map.Terrains.Count ? _map.Terrains[TerrainBox.SelectedIndex] : null;

    void RefreshTerrains(int? select = null)
    {
        int keep = select ?? TerrainBox.SelectedIndex;
        TerrainBox.Items.Clear();
        for (int i = 0; i < _map.Terrains.Count; i++)
        {
            var r = _map.Terrains[i];
            TerrainBox.Items.Add($"{i + 1}: {(r.Mode == TerrainMode.Blob47 ? "47" : "16")} tiles from #{r.Base}" + (r.Variants.Count > 0 ? $", {r.Variants.Count} variants" : ""));
        }
        TerrainBox.SelectedIndex = _map.Terrains.Count == 0 ? -1 : Math.Clamp(keep < 0 ? 0 : keep, 0, _map.Terrains.Count - 1);
    }

    /// <summary>
    /// New terrain from the tileset selection: an 8×6 (or bigger) selection makes a 47-tile terrain with corners,
    /// anything smaller a 16-tile one (4×4). Its top-left tile is the block's start.
    /// </summary>
    void OnAddTerrain(object sender, RoutedEventArgs e)
    {
        if (_map.Tileset is not { } ts) { Ui.Error(new InvalidOperationException("Load a tileset first.")); return; }
        var rule = new TerrainRule
        {
            Base = _tsSel.Y * ts.Columns + _tsSel.X,
            Mode = _tsSel.Width >= 8 && _tsSel.Height >= 6 ? TerrainMode.Blob47 : TerrainMode.Blob16,
        };
        try { Autotile.Validate(ts, rule); }
        catch (InvalidOperationException ex) { Ui.Error(ex); return; }
        int existing = _map.Terrains.FindIndex(t => t.Base == rule.Base && t.Mode == rule.Mode);
        if (existing < 0)
        {
            Snapshot();
            _map.Terrains.Add(rule);
            existing = _map.Terrains.Count - 1;
        }
        RefreshTerrains(existing);
        SetTool(AutoBtn);
    }

    /// <summary>The tiles selected in the tileset become random variants of the chosen terrain's full tile.</summary>
    void SetTerrainVariants()
    {
        if (CurrentTerrain is not { } rule || _map.Tileset is not { } ts) { Ui.Error(new InvalidOperationException("Choose a terrain first.")); return; }
        Snapshot();
        rule.Variants = [.. _stamp.Where(t => t >= 0 && t < ts.Count && !Autotile.IsTerrain(ts, new TerrainRule { Base = rule.Base, Mode = rule.Mode }, t)).Distinct()];
        RefreshTerrains();
    }

    void OnRemoveTerrain(object sender, RoutedEventArgs e)
    {
        if (TerrainBox.SelectedIndex < 0) return;
        Snapshot();
        _map.Terrains.RemoveAt(TerrainBox.SelectedIndex);
        RefreshTerrains();
    }

    void OnTerrainTemplate(object sender, RoutedEventArgs e) => SaveTemplate(CurrentTerrain?.Mode ?? TerrainMode.Blob16);

    void SaveTemplate(TerrainMode mode)
    {
        string kind = mode == TerrainMode.Blob47 ? "47" : "16";
        var dlg = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = $"terrain-{kind}-template-{_map.TileSize}px.png" };
        if (dlg.ShowDialog() != true) return;
        var (w, h) = Autotile.TemplateSize(_map.TileSize, mode);
        try { ImageIO.SavePng(dlg.FileName, Autotile.Template(_map.TileSize, mode), w, h); }
        catch (Exception ex) { Ui.Error(ex); }
    }

    // ---------- tileset generator ----------

    void FindDuplicateTiles()
    {
        if (_map.Tileset is not { } ts) { Ui.Error(new InvalidOperationException("Load a tileset first.")); return; }
        var groups = TilesetTools.Duplicates(ts);
        if (groups.Count == 0) { MessageBox.Show(Window.GetWindow(this), "No duplicate tiles.", "Duplicate tiles"); return; }
        int extra = groups.Sum(g => g.Count - 1);
        string list = string.Join("\n", groups.Take(12).Select(g => "• " + string.Join(" = ", g.Select(i => $"#{i}"))));
        if (MessageBox.Show(Window.GetWindow(this),
                $"{extra} tile(s) are copies of other tiles:\n{list}{(groups.Count > 12 ? "\n…" : "")}\n\nRemove the copies? The map is updated to use the kept tiles; terrains are cleared because tile positions change. Undo with Ctrl+Z.",
                "Duplicate tiles", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Snapshot();
        TilesetTools.RemoveDuplicates(_map);
        RefreshAll();
    }

    // ---------- generators ----------

    /// <summary>Seeded seamless tiles (a row of variants) as the map's tileset.</summary>
    void GenerateTileset()
    {
        if (Ui.AskFields(this, "Generate tileset",
                ("_Seed (number or word)", Random.Shared.Next(100000).ToString(), null),
                ("_Pattern", nameof(TilePattern.Noise), Enum.GetNames<TilePattern>()),
                ("_Base color (hex)", "4A7A3A", null),
                ("_Variants", "4", ["1", "2", "4", "8"])) is not { } f) return;
        GenerateTileset(f[0], Enum.Parse<TilePattern>(f[1]), f[2], int.Parse(f[3]));
    }

    public void GenerateTileset(string seed, TilePattern pattern, string baseColorHex, int variants)
    {
        if (!ColorUtil.TryParseHex(baseColorHex.TrimStart('#'), out uint baseColor)) { Ui.Error(new FormatException("Base color: RRGGBB, e.g. 4A7A3A.")); return; }
        if (_map.Tileset is not null && MessageBox.Show(Window.GetWindow(this), "Replace the current tileset? Ctrl+Z undoes.", "Generate tileset", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        try
        {
            var (px, w, h) = Procedural.Tiles(Rng.Seed(seed), _map.TileSize, PaletteTools.Ramp(baseColor | 0xFF000000), pattern, variants);
            SetTileset(px, w, h, null);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { Ui.Error(ex); }
    }

    /// <summary>Seeded cave or platform layout on the current layer, auto-tiled with the chosen terrain if there is one.</summary>
    void GenerateLayout()
    {
        if (_map.Tileset is null) { Ui.Error(new InvalidOperationException("Load or generate a tileset first.")); return; }
        if (Ui.AskFields(this, "Generate level layout",
                ("_Seed (number or word)", Random.Shared.Next(100000).ToString(), null),
                ("_Layout", "Cave", ["Cave", "Platforms"]),
                ("_Fill (cave)", "0.45", ["0.40", "0.45", "0.50", "0.55"])) is not { } f) return;
        GenerateLayout(f[0], f[1], double.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture));
    }

    public void GenerateLayout(string seedText, string layout, double fill)
    {
        if (_map.Tileset is null) { Ui.Error(new InvalidOperationException("Load or generate a tileset first.")); return; }
        ulong seed = Rng.Seed(seedText);
        var solid = layout == "Cave" ? Procedural.Cave(seed, _map.Width, _map.Height, fill) : Procedural.Platforms(seed, _map.Width, _map.Height);
        Snapshot();
        Procedural.Fill(_map, _map.Layers[_layer], solid, CurrentTerrain, _stamp.FirstOrDefault(t => t >= 0));
        Render();
        Status.Text = $"{layout} from seed “{seedText}” on layer “{_map.Layers[_layer].Name}”{(CurrentTerrain is null ? " with the selected tile" : " with the chosen terrain")}. Ctrl+Z undoes.";
    }

    /// <summary>Shows the tileset selection repeated 4×4, to spot seams in repeating patterns.</summary>
    void PreviewRepeat()
    {
        if (_map.Tileset is not { } ts) return;
        var (px, w, h) = TilesetTools.Repeat(ts, _tsSel.X, _tsSel.Y, _tsSel.Width, _tsSel.Height, 4);
        double scale = Math.Clamp(512.0 / Math.Max(w, h), 1, 8);
        var img = new Image { Source = ImageIO.ToBitmap(px, w, h), Width = w * scale, Height = h * scale, Stretch = System.Windows.Media.Stretch.Fill };
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(img, System.Windows.Media.BitmapScalingMode.NearestNeighbor);
        System.Windows.Automation.AutomationProperties.SetName(img, "Selected tiles repeated four times in each direction");
        new Window
        {
            Title = "Repeat preview", SizeToContent = SizeToContent.WidthAndHeight, Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new Border { Padding = new Thickness(8), Child = img },
        }.Show();
    }

    void ExportTerrainRules()
    {
        if (_map.Terrains.Count == 0) { Ui.Error(new InvalidOperationException("This map has no terrains.")); return; }
        var dlg = new SaveFileDialog { Filter = "Terrain rules (*.json)|*.json", FileName = BaseName + "-terrains.json" };
        if (dlg.ShowDialog() != true) return;
        try { DocumentFile.WriteAtomic(dlg.FileName, TerrainExport.Json(_map)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { Ui.Error(ex); }
    }
}
