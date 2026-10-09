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

    RadioButton CurrentTool => new[] { PaintBtn, EraseBtn, FillBtn, PickBtn, AutoBtn, ObjectBtn }.First(r => r.IsChecked == true);

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
            if (CurrentTerrain is not int terrain || _map.Tileset is null)
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
        else if (_strokeTool == AutoBtn && CurrentTerrain is int terrain)
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
        if (_map.Tileset is not { } ts) { StampPreview.Source = null; return; }
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
        StampPreview.Source = ImageIO.ToBitmap(px, pw, h * t);
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

    int TilesetScale => _map.Tileset is { } ts ? Math.Clamp(240 / ts.Width, 1, 8) : 1;

    void RefreshTileset()
    {
        if (_map.Tileset is not { } ts)
        {
            TilesetImage.Source = null;
            TilesetHost.Width = TilesetHost.Height = 0;
            SelectionRect.Visibility = Visibility.Collapsed;
            StampPreview.Source = null;
            return;
        }
        int s = TilesetScale;
        TilesetImage.Source = ImageIO.ToBitmap(ts.Pixels, ts.Width, ts.Height);
        TilesetHost.Width = ts.Width * s;
        TilesetHost.Height = ts.Height * s;
        TilesetGrid.Fill = ts.TileSize * s >= 4 ? Ui.GridBrush(ts.TileSize * s) : null;
        if (_tsSel.X + _tsSel.Width > ts.Columns || _tsSel.Y + _tsSel.Height > ts.Rows) _tsSel = new Int32Rect(0, 0, 1, 1);
        SetTilesetSelection(_tsSel);
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
        System.Windows.Automation.AutomationProperties.SetItemStatus(TilesetHost,
            r.Width * r.Height == 1 ? $"Tile {stamp[0] + 1} of {ts.Count}" : $"{r.Width} by {r.Height} tiles from tile {stamp[0] + 1}");
    }

    // ---------- terrains ----------

    int? CurrentTerrain => TerrainBox.SelectedIndex >= 0 && TerrainBox.SelectedIndex < _map.Terrains.Count ? _map.Terrains[TerrainBox.SelectedIndex] : null;

    void RefreshTerrains(int? select = null)
    {
        int keep = select ?? TerrainBox.SelectedIndex;
        TerrainBox.Items.Clear();
        for (int i = 0; i < _map.Terrains.Count; i++) TerrainBox.Items.Add($"{i + 1}: from tile #{_map.Terrains[i]}");
        TerrainBox.SelectedIndex = _map.Terrains.Count == 0 ? -1 : Math.Clamp(keep < 0 ? 0 : keep, 0, _map.Terrains.Count - 1);
    }

    void OnAddTerrain(object sender, RoutedEventArgs e)
    {
        if (_map.Tileset is not { } ts) { Ui.Error(new InvalidOperationException("Load a tileset first.")); return; }
        int terrainBase = _tsSel.Y * ts.Columns + _tsSel.X;
        try { Autotile.Validate(ts, terrainBase); }
        catch (InvalidOperationException ex) { Ui.Error(ex); return; }
        if (!_map.Terrains.Contains(terrainBase))
        {
            Snapshot();
            _map.Terrains.Add(terrainBase);
        }
        RefreshTerrains(_map.Terrains.IndexOf(terrainBase));
        SetTool(AutoBtn);
    }

    void OnRemoveTerrain(object sender, RoutedEventArgs e)
    {
        if (TerrainBox.SelectedIndex < 0) return;
        Snapshot();
        _map.Terrains.RemoveAt(TerrainBox.SelectedIndex);
        RefreshTerrains();
    }

    void OnTerrainTemplate(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = $"terrain-template-{_map.TileSize}px.png" };
        if (dlg.ShowDialog() != true) return;
        try { ImageIO.SavePng(dlg.FileName, Autotile.Template(_map.TileSize), _map.TileSize * 4, _map.TileSize * 4); }
        catch (Exception ex) { Ui.Error(ex); }
    }
}
