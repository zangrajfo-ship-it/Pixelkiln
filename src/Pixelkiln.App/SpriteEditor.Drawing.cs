using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pixelkiln.Core;

namespace Pixelkiln.App;

/// <summary>Mouse drawing: tools, shapes, mirror, selection and clipboard.</summary>
public partial class SpriteEditor
{
    enum Tool { Pencil, Eraser, Fill, Picker, Line, Rect, Ellipse, Select, Wand }

    Tool CurrentTool => Enum.Parse<Tool>((string)ToolsPanel.Children.OfType<RadioButton>().First(r => r.IsChecked == true).Tag);

    void SetTool(Tool t)
    {
        if (t != Tool.Select) CommitFloat();
        ToolsPanel.Children.OfType<RadioButton>().First(r => (string)r.Tag == t.ToString()).IsChecked = true;
        UpdateToolOptions();
    }

    void OnToolClick(object sender, RoutedEventArgs e) => SetTool(CurrentTool);

    int BrushSize
    {
        get => (int)SizeSlider.Value;
        set => SizeSlider.Value = Math.Clamp(value, 1, 8);
    }

    // ---------- stroke state ----------
    bool _stroking;
    Tool _strokeTool;
    int _lastX, _lastY;
    (int X, int Y) _start;
    uint _strokeColor;
    uint[]? _shapeBase;          // cel before the shape preview / before a pixel-perfect stroke
    readonly List<(int X, int Y)> _strokePts = [];
    Point? _panFrom;

    // ---------- selection state ----------
    Int32Rect? _sel;
    bool[]? _selMask;            // free-form selection (magic wand); _sel is then its bounding box
    uint[]? _float;              // lifted pixels, drawn above the cel until committed
    int _fw, _fh, _fx, _fy, _floatLayer, _floatFrame;
    bool _movingFloat, _marquee;
    (int X, int Y) _grab;

    /// <summary>Magic wand on the active layer; Shift adds to the current selection.</summary>
    void WandSelect(int x, int y, bool add)
    {
        CommitFloat();
        int w = _sprite.Width, h = _sprite.Height;
        var mask = Selection.MagicWand(_sprite.Cel(_layer, _frame), w, h, x, y, ContiguousToggle.IsChecked == true);
        if (add && _sel is { } old)
        {
            var prev = _selMask ?? RectMask(old);
            for (int i = 0; i < mask.Length; i++) mask[i] |= prev[i];
        }
        if (Selection.Bounds(mask, w, h) is not var (bx, by, bw, bh)) return;
        (_sel, _selMask) = (new Int32Rect(bx, by, bw, bh), mask);
        UpdateOverlay();
    }

    bool[] RectMask(Int32Rect r)
    {
        var m = new bool[_sprite.Width * _sprite.Height];
        for (int y = r.Y; y < r.Y + r.Height; y++)
            for (int x = r.X; x < r.X + r.Width; x++) m[y * _sprite.Width + x] = true;
        return m;
    }

    // ---------- clipboard ----------
    uint[]? _clip;
    int _clipW, _clipH;
    DataObject? _clipData;

    (int X, int Y) PixelAt(MouseEventArgs e)
    {
        var p = e.GetPosition(CanvasCore);   // the real canvas (in tiled mode it sits inside the 3×3 repeat)
        return ((int)Math.Floor(p.X / _zoom), (int)Math.Floor(p.Y / _zoom));
    }

    void OnCanvasDown(object sender, MouseButtonEventArgs e)
    {
        Keyboard.Focus(this);
        StopPlaying();
        if (e.ChangedButton == MouseButton.Middle)
        {
            _panFrom = e.GetPosition(Scroller);
            CanvasHost.CaptureMouse();
            return;
        }
        if (_stroking || e.ChangedButton is not (MouseButton.Left or MouseButton.Right)) return;

        bool left = e.ChangedButton == MouseButton.Left;
        var (x, y) = PixelAt(e);
        var tool = CurrentTool;
        if (tool == Tool.Select)
        {
            SelectDown(x, y);
            _stroking = true;
            _strokeTool = tool;
            CanvasHost.CaptureMouse();
            return;
        }
        if (Tiled)
        {
            if (!Tools.InBounds(_sprite.Width * 3, _sprite.Height * 3, x + _sprite.Width, y + _sprite.Height)) return;
            if (tool is Tool.Fill or Tool.Picker) (x, y) = (Tools.Wrap(x, _sprite.Width), Tools.Wrap(y, _sprite.Height));
        }
        else if (!Tools.InBounds(_sprite.Width, _sprite.Height, x, y)) return;

        if (tool == Tool.Wand)
        {
            WandSelect(x, y, add: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            return;
        }

        if (tool == Tool.Picker)
        {
            uint c = Compositor.Flatten(_sprite, _frame)[y * _sprite.Width + x];
            UseColor(c, secondary: !left);
            return;
        }

        Snapshot();
        var cel = _sprite.Cel(_layer, _frame);
        _strokeColor = tool == Tool.Eraser ? 0 : left ? _primary : _secondary;
        switch (tool)
        {
            case Tool.Fill:
                Tools.Fill(cel, _sprite.Width, _sprite.Height, x, y, _strokeColor);
                Render();
                return;
            case Tool.Pencil or Tool.Eraser:
                _shapeBase = (uint[])cel.Clone();   // pixel-perfect restores corners from here
                _strokePts.Clear();
                _strokePts.Add((x, y));
                Plot(x, y);
                break;
            default:   // shapes
                _shapeBase = (uint[])cel.Clone();
                _start = (x, y);
                DrawShape(tool, x, y);
                break;
        }
        (_lastX, _lastY, _stroking, _strokeTool) = (x, y, true, tool);
        CanvasHost.CaptureMouse();
        Render();
    }

    void OnCanvasMove(object sender, MouseEventArgs e)
    {
        var (x, y) = PixelAt(e);
        string under = Tools.InBounds(_sprite.Width, _sprite.Height, x, y)
            ? $"   x {x}, y {y}   {Ui.ColorName(Compositor.Flatten(_sprite, _frame)[y * _sprite.Width + x])}" : "";
        Status.Text = $"{_sprite.Width}×{_sprite.Height} px{under}" + (_sel is { } s ? $"   selection {s.Width}×{s.Height}" : "");

        if (_panFrom is Point from)
        {
            var now = e.GetPosition(Scroller);
            Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset - (now.X - from.X));
            Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset - (now.Y - from.Y));
            _panFrom = now;
            return;
        }
        if (!_stroking || (x, y) == (_lastX, _lastY)) return;
        (int px, int py) = (_lastX, _lastY);
        (_lastX, _lastY) = (x, y);

        switch (_strokeTool)
        {
            case Tool.Select:
                SelectMove(x, y);
                return;
            case Tool.Pencil or Tool.Eraser:
                foreach (var p in Tools.LinePoints(px, py, x, y).Skip(1)) StrokeTo(p.X, p.Y);
                break;
            default:
                Array.Copy(_shapeBase!, _sprite.Cel(_layer, _frame), _shapeBase!.Length);
                DrawShape(_strokeTool, x, y);
                break;
        }
        Render();
    }

    void OnCanvasUp(object sender, MouseButtonEventArgs e)
    {
        if (_stroking && _strokeTool == Tool.Select && _marquee && _sel is { Width: 1, Height: 1 } s && (s.X, s.Y) == _start)
        {
            _sel = null;   // a plain click deselects
            UpdateOverlay();
        }
        _stroking = _movingFloat = _marquee = false;
        _shapeBase = null;
        _panFrom = null;
        CanvasHost.ReleaseMouseCapture();
    }

    void OnCanvasLeave(object sender, MouseEventArgs e) => ApplyZoom();

    // ---------- painting with brush + mirror ----------

    bool MirrorX => MirrorXToggle.IsChecked == true;
    bool MirrorY => MirrorYToggle.IsChecked == true;

    /// <summary>Sets one pixel and its mirror images.</summary>
    void SetMirrored(int x, int y)
    {
        int w = _sprite.Width, h = _sprite.Height;
        var cel = _sprite.Cel(_layer, _frame);
        if (Tiled) (x, y) = (Tools.Wrap(x, w), Tools.Wrap(y, h));   // drawing past an edge continues on the other side
        Tools.Set(cel, w, h, x, y, _strokeColor);
        if (MirrorX) Tools.Set(cel, w, h, w - 1 - x, y, _strokeColor);
        if (MirrorY) Tools.Set(cel, w, h, x, h - 1 - y, _strokeColor);
        if (MirrorX && MirrorY) Tools.Set(cel, w, h, w - 1 - x, h - 1 - y, _strokeColor);
    }

    bool PixelPerfect => PixelPerfectToggle.IsChecked == true && BrushSize == 1 && _strokeTool == Tool.Pencil;

    /// <summary>Adds a stroke point; pixel-perfect mode removes the corner of every "L" so diagonals stay 1 px.</summary>
    void StrokeTo(int x, int y)
    {
        _strokePts.Add((x, y));
        Plot(x, y);
        if (!PixelPerfect || _strokePts.Count < 3 || !Tools.IsLCorner(_strokePts[^3], _strokePts[^2], _strokePts[^1])) return;
        var (cx, cy) = _strokePts[^2];
        RestoreMirrored(cx, cy);
        _strokePts.RemoveAt(_strokePts.Count - 2);
    }

    /// <summary>Puts back the pixel from before the stroke (and its mirror images).</summary>
    void RestoreMirrored(int x, int y)
    {
        int w = _sprite.Width, h = _sprite.Height;
        var cel = _sprite.Cel(_layer, _frame);
        void Put(int px, int py)
        {
            if (Tiled) (px, py) = (Tools.Wrap(px, w), Tools.Wrap(py, h));
            if (Tools.InBounds(w, h, px, py)) cel[py * w + px] = _shapeBase![py * w + px];
        }
        Put(x, y);
        if (MirrorX) Put(w - 1 - x, y);
        if (MirrorY) Put(x, h - 1 - y);
        if (MirrorX && MirrorY) Put(w - 1 - x, h - 1 - y);
    }

    void Plot(int x, int y)
    {
        foreach (var (bx, by) in Tools.BrushPoints(x, y, BrushSize)) SetMirrored(bx, by);
    }

    void DrawShape(Tool tool, int x1, int y1)
    {
        int w = _sprite.Width, h = _sprite.Height;
        var (x0, y0) = _start;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) (x1, y1) = Constrain(tool, x0, y0, x1, y1);

        // draw into a mask first, then paint every mask pixel mirrored;
        // in tiled mode the mask covers the 3×3 area so shapes crossing an edge wrap around
        int o = Tiled ? 1 : 0, mw = w * (1 + 2 * o), mh = h * (1 + 2 * o), ox = w * o, oy = h * o;
        var mask = new bool[mw * mh];
        bool filled = FilledToggle.IsChecked == true;
        (x0, y0, x1, y1) = (x0 + ox, y0 + oy, x1 + ox, y1 + oy);
        switch (tool)
        {
            case Tool.Line:
                foreach (var (lx, ly) in Tools.LinePoints(x0, y0, x1, y1))
                    foreach (var (bx, by) in Tools.BrushPoints(lx, ly, BrushSize)) Tools.Set(mask, mw, mh, bx, by, true);
                break;
            case Tool.Rect: Tools.Rect(mask, mw, mh, x0, y0, x1, y1, true, filled); break;
            case Tool.Ellipse: Tools.Ellipse(mask, mw, mh, x0, y0, x1, y1, true, filled); break;
        }
        for (int i = 0; i < mask.Length; i++)
            if (mask[i]) SetMirrored(i % mw - ox, i / mw - oy);
    }

    /// <summary>Shift: lines snap to 0/45/90°, boxes become squares.</summary>
    static (int X, int Y) Constrain(Tool tool, int x0, int y0, int x1, int y1)
    {
        int dx = x1 - x0, dy = y1 - y0, d = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (tool == Tool.Line)
        {
            if (Math.Abs(dx) > 2 * Math.Abs(dy)) return (x1, y0);
            if (Math.Abs(dy) > 2 * Math.Abs(dx)) return (x0, y1);
        }
        return (x0 + Math.Sign(dx) * d, y0 + Math.Sign(dy) * d);
    }

    void OnMirrorToggle(object sender, RoutedEventArgs e) => UpdateOverlay();

    // ---------- selection ----------

    static bool Contains(Int32Rect r, int x, int y) => x >= r.X && y >= r.Y && x < r.X + r.Width && y < r.Y + r.Height;

    void SelectDown(int x, int y)
    {
        if (_sel is { } s && Contains(s, x, y) && (_selMask is null || _float is not null || _selMask[y * _sprite.Width + x]))
        {
            if (_float is null) Lift();
            _movingFloat = true;
            _grab = (x - _fx, y - _fy);
        }
        else
        {
            CommitFloat();
            _selMask = null;
            _marquee = true;
            _start = (Math.Clamp(x, 0, _sprite.Width - 1), Math.Clamp(y, 0, _sprite.Height - 1));
            _sel = new Int32Rect(_start.X, _start.Y, 1, 1);
        }
        (_lastX, _lastY) = (x, y);
        Render();
    }

    void SelectMove(int x, int y)
    {
        if (_movingFloat)
        {
            (_fx, _fy) = (x - _grab.X, y - _grab.Y);
            _sel = new Int32Rect(_fx, _fy, _fw, _fh);
            Render();
        }
        else if (_marquee)
        {
            x = Math.Clamp(x, 0, _sprite.Width - 1);
            y = Math.Clamp(y, 0, _sprite.Height - 1);
            _sel = new Int32Rect(Math.Min(x, _start.X), Math.Min(y, _start.Y), Math.Abs(x - _start.X) + 1, Math.Abs(y - _start.Y) + 1);
            UpdateOverlay();
        }
    }

    /// <summary>Moves the selected pixels of the active cel into the floating buffer.</summary>
    void Lift()
    {
        if (_sel is not { } s) return;
        Snapshot();
        var cel = _sprite.Cel(_layer, _frame);
        (_fw, _fh, _fx, _fy, _floatLayer, _floatFrame) = (s.Width, s.Height, s.X, s.Y, _layer, _frame);
        if (_selMask is { } m)   // magic wand: only the selected pixels move
        {
            _float = Selection.CropMasked(cel, m, _sprite.Width, s.X, s.Y, s.Width, s.Height);
            Selection.ClearMasked(cel, m);
        }
        else
        {
            _float = PixelOps.Crop(cel, _sprite.Width, s.X, s.Y, s.Width, s.Height);
            PixelOps.Clear(cel, _sprite.Width, s.X, s.Y, s.Width, s.Height);
        }
    }

    /// <summary>Stamps the floating pixels into the cel they came from. Called before any other edit.</summary>
    void CommitFloat()
    {
        if (_float is null) return;
        if (_floatLayer < _sprite.Layers.Count && _floatFrame < _sprite.Frames.Count)
            PixelOps.Stamp(_sprite.Cel(_floatLayer, _floatFrame), _sprite.Width, _sprite.Height, _float, _fw, _fh, _fx, _fy);
        _float = null;
        _selMask = null;   // the pixels moved; keep the box only
        _sel = ClipToCanvas(_sel);
        Render();
    }

    Int32Rect? ClipToCanvas(Int32Rect? r)
    {
        if (r is not { } s) return null;
        int x0 = Math.Max(0, s.X), y0 = Math.Max(0, s.Y);
        int x1 = Math.Min(_sprite.Width, s.X + s.Width), y1 = Math.Min(_sprite.Height, s.Y + s.Height);
        return x1 > x0 && y1 > y0 ? new Int32Rect(x0, y0, x1 - x0, y1 - y0) : null;
    }

    /// <summary>Lifts the selection (or the whole cel) so it can be flipped/rotated.</summary>
    void EnsureFloat()
    {
        if (_float is not null) return;
        _sel ??= new Int32Rect(0, 0, _sprite.Width, _sprite.Height);
        Lift();
    }

    void TransformFloat(Func<uint[], int, int, uint[]> op, bool swapsSize = false)
    {
        EnsureFloat();
        _float = op(_float!, _fw, _fh);
        if (swapsSize) (_fw, _fh) = (_fh, _fw);
        _sel = new Int32Rect(_fx, _fy, _fw, _fh);
        SetTool(Tool.Select);
        Render();
    }

    void DeleteSelection()
    {
        if (_float is not null) { _float = null; Render(); return; }   // lift already took the undo snapshot
        if (_sel is not { } s) return;
        Snapshot();
        if (_selMask is { } m) Selection.ClearMasked(_sprite.Cel(_layer, _frame), m);
        else PixelOps.Clear(_sprite.Cel(_layer, _frame), _sprite.Width, s.X, s.Y, s.Width, s.Height);
        Render();
    }

    void CancelFloat()
    {
        if (_float is null) { (_sel, _selMask) = (null, null); UpdateOverlay(); return; }
        Restore(_history.Undo(_sprite));   // back to before the lift / paste
    }

    void SelectAll()
    {
        CommitFloat();
        (_sel, _selMask) = (new Int32Rect(0, 0, _sprite.Width, _sprite.Height), null);
        SetTool(Tool.Select);
        UpdateOverlay();
    }

    // ---------- clipboard ----------

    void Copy()
    {
        uint[] px;
        int w, h;
        if (_float is not null) (px, w, h) = (_float, _fw, _fh);
        else if (_sel is { } s) (px, w, h) = (_selMask is { } m
                ? Selection.CropMasked(_sprite.Cel(_layer, _frame), m, _sprite.Width, s.X, s.Y, s.Width, s.Height)
                : PixelOps.Crop(_sprite.Cel(_layer, _frame), _sprite.Width, s.X, s.Y, s.Width, s.Height), s.Width, s.Height);
        else (px, w, h) = ((uint[])_sprite.Cel(_layer, _frame).Clone(), _sprite.Width, _sprite.Height);
        (_clip, _clipW, _clipH) = ((uint[])px.Clone(), w, h);

        // system clipboard: bitmap for most apps + PNG stream that keeps transparency
        try
        {
            var bmp = ImageIO.ToBitmap(px, w, h);
            var png = new MemoryStream();
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            enc.Save(png);
            _clipData = new DataObject();
            _clipData.SetImage(bmp);
            _clipData.SetData("PNG", png);
            Clipboard.SetDataObject(_clipData, true);
        }
        catch (System.Runtime.InteropServices.COMException) { _clipData = null; }   // clipboard busy: internal copy still works
    }

    void Cut()
    {
        Copy();
        DeleteSelection();
    }

    void Paste()
    {
        var (px, w, h) = ReadClipboard();
        if (px is null) return;
        CommitFloat();
        Snapshot();
        _float = px;
        _selMask = null;
        (_fw, _fh, _floatLayer, _floatFrame) = (w, h, _layer, _frame);
        (_fx, _fy) = _sel is { } s ? (s.X, s.Y) : (0, 0);
        _sel = new Int32Rect(_fx, _fy, _fw, _fh);
        SetTool(Tool.Select);
        Render();
    }

    (uint[]? Pixels, int W, int H) ReadClipboard()
    {
        try
        {
            if (_clipData is not null && Clipboard.IsCurrent(_clipData)) return ((uint[])_clip!.Clone(), _clipW, _clipH);
            if (Clipboard.GetData("PNG") is MemoryStream png)
                return ImageIO.FromBitmap(new PngBitmapDecoder(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0]);
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } img)
                return ImageIO.FromBitmap(img);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or NotSupportedException) { }
        return _clip is null ? (null, 0, 0) : ((uint[])_clip.Clone(), _clipW, _clipH);
    }

    // ---------- overlay: selection outline + mirror axes ----------

    void UpdateOverlay()
    {
        bool maskOutline = _selMask is not null && _float is null;
        SelPath.Visibility = SelPathBack.Visibility = maskOutline ? Visibility.Visible : Visibility.Collapsed;
        if (maskOutline)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
                foreach (var (x1, y1, x2, y2) in Selection.Outline(_selMask!, _sprite.Width, _sprite.Height))
                {
                    ctx.BeginFigure(new Point(x1 * _zoom, y1 * _zoom), false, false);
                    ctx.LineTo(new Point(x2 * _zoom, y2 * _zoom), true, false);
                }
            g.Freeze();
            SelPath.Data = SelPathBack.Data = g;
        }
        if (_sel is { } s && !maskOutline)
        {
            foreach (var r in new[] { SelRectBack, SelRect })
            {
                r.Visibility = Visibility.Visible;
                Canvas.SetLeft(r, s.X * _zoom);
                Canvas.SetTop(r, s.Y * _zoom);
                r.Width = s.Width * _zoom;
                r.Height = s.Height * _zoom;
            }
        }
        else SelRect.Visibility = SelRectBack.Visibility = Visibility.Collapsed;

        double cw = _sprite.Width * _zoom, ch = _sprite.Height * _zoom;
        MirrorXLine.Visibility = MirrorX ? Visibility.Visible : Visibility.Collapsed;
        (MirrorXLine.X1, MirrorXLine.X2, MirrorXLine.Y1, MirrorXLine.Y2) = (cw / 2, cw / 2, 0, ch);
        MirrorYLine.Visibility = MirrorY ? Visibility.Visible : Visibility.Collapsed;
        (MirrorYLine.X1, MirrorYLine.X2, MirrorYLine.Y1, MirrorYLine.Y2) = (0, cw, ch / 2, ch / 2);
    }
}
