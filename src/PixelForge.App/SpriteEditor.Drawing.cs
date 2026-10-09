using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PixelForge.Core;

namespace PixelForge.App;

/// <summary>Mouse drawing: tools, shapes, mirror, selection and clipboard.</summary>
public partial class SpriteEditor
{
    enum Tool { Pencil, Eraser, Fill, Picker, Line, Rect, Ellipse, Select }

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
        get => SizeBox.SelectedIndex + 1;
        set => SizeBox.SelectedIndex = Math.Clamp(value, 1, 8) - 1;
    }

    // ---------- stroke state ----------
    bool _stroking;
    Tool _strokeTool;
    int _lastX, _lastY;
    (int X, int Y) _start;
    uint _strokeColor;
    uint[]? _shapeBase;          // cel before the shape preview
    Point? _panFrom;

    // ---------- selection state ----------
    Int32Rect? _sel;
    uint[]? _float;              // lifted pixels, drawn above the cel until committed
    int _fw, _fh, _fx, _fy, _floatLayer, _floatFrame;
    bool _movingFloat, _marquee;
    (int X, int Y) _grab;

    // ---------- clipboard ----------
    uint[]? _clip;
    int _clipW, _clipH;
    DataObject? _clipData;

    (int X, int Y) PixelAt(MouseEventArgs e)
    {
        var p = e.GetPosition(CanvasHost);
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
        if (!Tools.InBounds(_sprite.Width, _sprite.Height, x, y)) return;

        if (tool == Tool.Picker)
        {
            uint c = Compositor.Flatten(_sprite, _frame)[y * _sprite.Width + x];
            if (left) (_primary, _editSecondary) = (c, false); else (_secondary, _editSecondary) = (c, true);
            RefreshSwatches();
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
                foreach (var (lx, ly) in Tools.LinePoints(px, py, x, y)) Plot(lx, ly);
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
        Tools.Set(cel, w, h, x, y, _strokeColor);
        if (MirrorX) Tools.Set(cel, w, h, w - 1 - x, y, _strokeColor);
        if (MirrorY) Tools.Set(cel, w, h, x, h - 1 - y, _strokeColor);
        if (MirrorX && MirrorY) Tools.Set(cel, w, h, w - 1 - x, h - 1 - y, _strokeColor);
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

        // draw into a mask first, then paint every mask pixel mirrored
        var mask = new bool[w * h];
        bool filled = FilledToggle.IsChecked == true;
        switch (tool)
        {
            case Tool.Line:
                foreach (var (lx, ly) in Tools.LinePoints(x0, y0, x1, y1))
                    foreach (var (bx, by) in Tools.BrushPoints(lx, ly, BrushSize)) Tools.Set(mask, w, h, bx, by, true);
                break;
            case Tool.Rect: Tools.Rect(mask, w, h, x0, y0, x1, y1, true, filled); break;
            case Tool.Ellipse: Tools.Ellipse(mask, w, h, x0, y0, x1, y1, true, filled); break;
        }
        for (int i = 0; i < mask.Length; i++)
            if (mask[i]) SetMirrored(i % w, i / w);
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
        if (_sel is { } s && Contains(s, x, y))
        {
            if (_float is null) Lift();
            _movingFloat = true;
            _grab = (x - _fx, y - _fy);
        }
        else
        {
            CommitFloat();
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
        _float = PixelOps.Crop(cel, _sprite.Width, s.X, s.Y, s.Width, s.Height);
        (_fw, _fh, _fx, _fy, _floatLayer, _floatFrame) = (s.Width, s.Height, s.X, s.Y, _layer, _frame);
        PixelOps.Clear(cel, _sprite.Width, s.X, s.Y, s.Width, s.Height);
    }

    /// <summary>Stamps the floating pixels into the cel they came from. Called before any other edit.</summary>
    void CommitFloat()
    {
        if (_float is null) return;
        if (_floatLayer < _sprite.Layers.Count && _floatFrame < _sprite.Frames.Count)
            PixelOps.Stamp(_sprite.Cel(_floatLayer, _floatFrame), _sprite.Width, _sprite.Height, _float, _fw, _fh, _fx, _fy);
        _float = null;
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
        PixelOps.Clear(_sprite.Cel(_layer, _frame), _sprite.Width, s.X, s.Y, s.Width, s.Height);
        Render();
    }

    void CancelFloat()
    {
        if (_float is null) { _sel = null; UpdateOverlay(); return; }
        Restore(_history.Undo(_sprite));   // back to before the lift / paste
    }

    void SelectAll()
    {
        CommitFloat();
        _sel = new Int32Rect(0, 0, _sprite.Width, _sprite.Height);
        SetTool(Tool.Select);
        UpdateOverlay();
    }

    // ---------- clipboard ----------

    void Copy()
    {
        uint[] px;
        int w, h;
        if (_float is not null) (px, w, h) = (_float, _fw, _fh);
        else if (_sel is { } s) (px, w, h) = (PixelOps.Crop(_sprite.Cel(_layer, _frame), _sprite.Width, s.X, s.Y, s.Width, s.Height), s.Width, s.Height);
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
        if (_sel is { } s)
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
