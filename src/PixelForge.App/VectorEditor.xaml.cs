using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using PixelForge.Core;
using Path = System.Windows.Shapes.Path;

namespace PixelForge.App;

/// <summary>
/// Vector mode (Sprite tab): shapes and Bezier paths with fills, gradients and strokes, node editing, boolean
/// operations, alignment, SVG import and export, and rasterising to PNG or pixel art at any size.
/// </summary>
public partial class VectorEditor : UserControl, IEditor, IStyleSource, IToolSelectable, IPictureSource
{
    static readonly double[] Zooms = [0.25, 0.5, 1, 2, 3, 4, 6, 8, 12, 16, 24, 32];
    enum DragKind { None, Move, Scale, Marquee, Create, Node, HandleIn, HandleOut, PenHandle }

    VectorDoc _doc = new();
    readonly History<VectorDoc> _history = new(d => d.Clone(), App.Settings.UndoLimit);
    readonly ViewAids _aids;
    string? _path;
    bool _dirty, _refreshing, _ready, _needsFit = true, _editSnapshot;
    double _zoom = 8;
    readonly List<Guid> _sel = [];
    (int Contour, int Node)? _node;

    // look for new shapes and the selection
    uint _fill1 = 0xFF3C8DDE, _fill2 = 0xFF1B3A66, _strokeColor = 0xFF101010;
    string _fillKind = "Solid";

    // dragging
    DragKind _drag;
    Point _from;
    bool _dragSnapshot;
    List<VShape> _originals = [];
    Rect _origBounds;
    int _handle;            // 0..7 clockwise from top-left for scaling
    VShape? _pen;           // path being drawn with the pen
    VShape? _preview;       // rectangle/ellipse/polygon/line while dragging

    public VectorEditor()
    {
        InitializeComponent();
        _ready = true;
        _aids = new ViewAids(this, CanvasHost, Scroller, Board, () => _zoom);
        FillKindBox.ItemsSource = new[] { "None", "Solid", "Linear", "Radial" };
        FillKindBox.SelectedItem = _fillKind;
        StrokeWidthBox.Text = "1";
        Picker.Color = _fill1;
        Picker.ColorChanged += OnPickerColor;
        OpacitySlider.ValueChanged += OnOpacity;
        Board.Fill = Ui.CheckerBrush();
        IsVisibleChanged += (_, _) =>
        {
            UpdateTitle();
            if (IsVisible && _needsFit) Dispatcher.BeginInvoke(ZoomFit, System.Windows.Threading.DispatcherPriority.Loaded);
        };
        IsKeyboardFocusedChanged += (_, _) => Scroller.BorderBrush = IsKeyboardFocused ? (Brush)FindResource("FocusRing") : Brushes.Transparent;
        Loaded += Init;
        void Init(object s, RoutedEventArgs e) { Loaded -= Init; LoadDoc(_doc, null); }
    }

    IEnumerable<VShape> Selected => _sel.Select(id => _doc.Shapes.FirstOrDefault(s => s.Id == id)).OfType<VShape>();
    VShape? Single => _sel.Count == 1 ? Selected.FirstOrDefault() : null;
    string ToolName => (string)ToolsPanel.Children.OfType<RadioButton>().First(r => r.IsChecked == true).Tag;

    // ---------- document ----------

    void LoadDoc(VectorDoc d, string? path)
    {
        (_doc, _path, _dirty, _pen, _node) = (d, path, false, null, null);
        _history.Clear();
        _sel.Clear();
        _aids.Bind(path);
        _needsFit = true;
        if (IsVisible) Dispatcher.BeginInvoke(ZoomFit, System.Windows.Threading.DispatcherPriority.Loaded);
        Rebuild();
        RefreshProps();
        UpdateTitle();
    }

    void Snapshot()
    {
        _history.Push(_doc);
        _dirty = true;
        _editSnapshot = false;
        UpdateTitle();
    }

    void UpdateTitle() => Ui.ShowTitle(this, _path, _dirty, "untitled drawing");

    // ---------- drawing ----------

    void Rebuild()
    {
        Board.Width = _doc.Width;
        Board.Height = _doc.Height;
        ShapesLayer.Children.Clear();
        foreach (var s in _doc.Shapes) AddVisual(s, s.Id);
        if (_preview is not null) AddVisual(_preview, Guid.Empty);
        DrawOverlay();
        Status.Text = $"{_doc.Width:0.##}×{_doc.Height:0.##} · {_doc.Shapes.Count} shape{(_doc.Shapes.Count == 1 ? "" : "s")}" + (_sel.Count > 0 ? $" · {_sel.Count} selected" : "");
    }

    void AddVisual(VShape s, Guid top, double parentOpacity = 1)
    {
        if (s.Kind == VShapeKind.Group) { foreach (var c in s.Children) AddVisual(c, top, parentOpacity * s.Opacity); return; }
        ShapesLayer.Children.Add(new Path
        {
            Data = VectorGeometry.Of(s), Fill = VectorGeometry.BrushOf(s.Fill), Stroke = s.Stroke is null ? null : VectorGeometry.BrushOf(s.Stroke),
            StrokeThickness = s.StrokeWidth, StrokeLineJoin = PenLineJoin.Round, Opacity = s.Opacity * parentOpacity, Tag = top, IsHitTestVisible = false,
        });
    }

    double Px(double screenPixels) => screenPixels / _zoom;

    Point[] HandlePoints(Rect b) =>
    [
        b.TopLeft, new(b.Left + b.Width / 2, b.Top), b.TopRight, new(b.Right, b.Top + b.Height / 2),
        b.BottomRight, new(b.Left + b.Width / 2, b.Bottom), b.BottomLeft, new(b.Left, b.Top + b.Height / 2),
    ];

    void DrawOverlay()
    {
        Overlay.Children.Clear();
        double t = Px(1), hs = Px(8);
        void Box(Point p, Brush fill, bool round = false)
        {
            Shape h = round ? new Ellipse() : new Rectangle();
            (h.Width, h.Height, h.Fill, h.Stroke, h.StrokeThickness) = (hs, hs, fill, Brushes.Black, t);
            Canvas.SetLeft(h, p.X - hs / 2);
            Canvas.SetTop(h, p.Y - hs / 2);
            Overlay.Children.Add(h);
        }
        if (_sel.Count > 0 && ToolName != "Nodes")
        {
            var b = VectorGeometry.Bounds(Selected);
            var r = new Rectangle { Width = Math.Max(0, b.Width), Height = Math.Max(0, b.Height), Stroke = Brushes.DeepSkyBlue, StrokeThickness = t, StrokeDashArray = [4, 3] };
            Canvas.SetLeft(r, b.X);
            Canvas.SetTop(r, b.Y);
            Overlay.Children.Add(r);
            if (ToolName == "Select") foreach (var p in HandlePoints(b)) Box(p, Brushes.White);
        }
        var edit = ToolName == "Nodes" ? Single : _pen;
        if (edit is { Kind: VShapeKind.Path })
            for (int ci = 0; ci < edit.Contours.Count; ci++)
                for (int ni = 0; ni < edit.Contours[ci].Nodes.Count; ni++)
                {
                    var n = edit.Contours[ci].Nodes[ni];
                    bool chosen = _node == (ci, ni);
                    if (chosen || ToolName == "Pen")
                        foreach (var (hx, hy) in new[] { (n.InX, n.InY), (n.OutX, n.OutY) })
                        {
                            if ((hx, hy) == (n.X, n.Y)) continue;
                            Overlay.Children.Add(new Line { X1 = n.X, Y1 = n.Y, X2 = hx, Y2 = hy, Stroke = Brushes.Orange, StrokeThickness = t });
                            Box(new Point(hx, hy), Brushes.Orange, round: true);
                        }
                    Box(new Point(n.X, n.Y), chosen ? Brushes.DeepSkyBlue : n.Smooth ? Brushes.White : Brushes.LightGray, round: n.Smooth);
                }
    }

    // ---------- zoom ----------

    void ApplyZoom()
    {
        CanvasHost.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        ZoomText.Text = $"{_zoom * 100:0} %";
        Board.Fill = Ui.CheckerBrush(_zoom);
        _aids.Layout(_doc.Width, _doc.Height, 1, Px(1));
        DrawOverlay();
    }

    void ZoomStep(int dir)
    {
        int i = Array.FindIndex(Zooms, z => z >= _zoom - 1e-9) + dir;
        if (i >= 0 && i < Zooms.Length) { _zoom = Zooms[i]; ApplyZoom(); }
    }

    void ZoomFit()
    {
        if (Scroller.ViewportWidth < 100 || Scroller.ViewportHeight < 100) return;
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
        ZoomStep(Math.Sign(e.Delta));
    }

    // ---------- hit testing ----------

    VShape? ShapeAt(Point p)
    {
        foreach (var s in Enumerable.Reverse(_doc.Shapes))
        {
            var g = VectorGeometry.Of(s);
            if (s.Fill is not null && g.FillContains(p, Px(1), ToleranceType.Absolute)) return s;
            if (g.StrokeContains(new Pen(null, Math.Max(s.StrokeWidth, Px(6))), p, Px(1), ToleranceType.Absolute)) return s;
        }
        return null;
    }

    int HandleAt(Point p)
    {
        if (_sel.Count == 0) return -1;
        var pts = HandlePoints(VectorGeometry.Bounds(Selected));
        for (int i = 0; i < pts.Length; i++) if ((pts[i] - p).Length <= Px(7)) return i;
        return -1;
    }

    (int C, int N, DragKind Kind)? NodeAt(VShape s, Point p)
    {
        double r = Px(7);
        if (_node is var (sc, sn) && sc < s.Contours.Count && sn < s.Contours[sc].Nodes.Count)
        {
            var n = s.Contours[sc].Nodes[sn];   // handles of the chosen point first
            if ((new Point(n.InX, n.InY) - p).Length <= r && (n.InX, n.InY) != (n.X, n.Y)) return (sc, sn, DragKind.HandleIn);
            if ((new Point(n.OutX, n.OutY) - p).Length <= r && (n.OutX, n.OutY) != (n.X, n.Y)) return (sc, sn, DragKind.HandleOut);
        }
        for (int c = 0; c < s.Contours.Count; c++)
            for (int i = 0; i < s.Contours[c].Nodes.Count; i++)
                if ((new Point(s.Contours[c].Nodes[i].X, s.Contours[c].Nodes[i].Y) - p).Length <= r) return (c, i, DragKind.Node);
        return null;
    }

    // ---------- mouse ----------

    void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Keyboard.Focus(this);
        if (e.ChangedButton != MouseButton.Left) return;
        var p = e.GetPosition(Board);
        _from = p;
        _dragSnapshot = false;
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (ToolName)
        {
            case "Select":
                if (HandleAt(p) is int h and >= 0)
                {
                    (_drag, _handle) = (DragKind.Scale, h);
                    _originals = [.. Selected.Select(s => s.Clone())];
                    _origBounds = VectorGeometry.Bounds(_originals);
                    break;
                }
                var hit = ShapeAt(p);
                if (hit is null)
                {
                    if (!shift) _sel.Clear();
                    _drag = DragKind.Marquee;
                }
                else
                {
                    if (shift) { if (!_sel.Remove(hit.Id)) _sel.Add(hit.Id); }
                    else if (!_sel.Contains(hit.Id)) { _sel.Clear(); _sel.Add(hit.Id); }
                    _drag = DragKind.Move;
                    _originals = [.. Selected.Select(s => s.Clone())];
                }
                RefreshProps();
                break;
            case "Nodes":
                if (Single is { Kind: VShapeKind.Path } path && NodeAt(path, p) is var (c, n, kind))
                {
                    _node = (c, n);
                    _drag = kind;
                    if (e.ClickCount == 2 && kind == DragKind.Node) { ToggleSmooth(path, c, n); _drag = DragKind.None; }
                }
                else
                {
                    var s = ShapeAt(p);
                    _sel.Clear();
                    if (s is not null) _sel.Add(s.Id);
                    _node = null;
                    if (s is not null && s.Kind != VShapeKind.Path) Status.Text = "Use “To path” to edit the points of a rectangle, ellipse or group.";
                    RefreshProps();
                }
                break;
            case "Pen":
                PenClick(p);
                break;
            default:
                _drag = DragKind.Create;
                break;
        }
        CanvasHost.CaptureMouse();
        Rebuild();
    }

    void OnMouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(Board);
        if (e.LeftButton != MouseButtonState.Pressed || _drag == DragKind.None)
        {
            if (p.X >= 0 && p.Y >= 0) Status.Text = $"{p.X:0.#}, {p.Y:0.#}";
            return;
        }
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        double dx = _aids.Snap(p.X, true, _zoom) - _from.X, dy = _aids.Snap(p.Y, false, _zoom) - _from.Y;
        // the first real movement (2 screen px) of a drag takes the undo snapshot
        if (_drag is DragKind.Move or DragKind.Scale or DragKind.Node or DragKind.HandleIn or DragKind.HandleOut && !_dragSnapshot)
        {
            if ((p - _from).Length < Px(2)) return;
            Snapshot();
            _dragSnapshot = true;
        }
        switch (_drag)
        {
            case DragKind.Move:
                Replace(_originals.Select(o => { var c = o.Clone(); VectorGeometry.Move(c, dx, dy); return c; }));
                break;
            case DragKind.Scale:
                ScaleFromHandle(p, shift);
                break;
            case DragKind.Marquee:
                var r = new Rect(_from, p);
                _sel.Clear();
                _sel.AddRange(_doc.Shapes.Where(s => r.IntersectsWith(VectorGeometry.Bounds(s))).Select(s => s.Id));
                Rebuild();
                var box = new Rectangle { Width = r.Width, Height = r.Height, Stroke = Brushes.White, StrokeThickness = Px(1), StrokeDashArray = [3, 3] };
                Canvas.SetLeft(box, r.X);
                Canvas.SetTop(box, r.Y);
                Overlay.Children.Add(box);
                return;
            case DragKind.Create:
                _preview = MakeShape(_from, p, shift);
                break;
            case DragKind.Node or DragKind.HandleIn or DragKind.HandleOut when Single is { } s && _node is var (c, n):
                var nodes = s.Contours[c].Nodes;
                var nd = nodes[n];
                nodes[n] = _drag switch
                {
                    DragKind.Node => nd.Moved(p.X - nd.X, p.Y - nd.Y),
                    DragKind.HandleIn => alt || !nd.Smooth ? nd with { InX = p.X, InY = p.Y } : nd with { InX = p.X, InY = p.Y, OutX = 2 * nd.X - p.X, OutY = 2 * nd.Y - p.Y },
                    _ => alt || !nd.Smooth ? nd with { OutX = p.X, OutY = p.Y } : nd with { OutX = p.X, OutY = p.Y, InX = 2 * nd.X - p.X, InY = 2 * nd.Y - p.Y },
                };
                break;
            case DragKind.PenHandle when _pen is not null:
                var last = _pen.Contours[0].Nodes;
                var ln = last[^1];
                last[^1] = ln with { OutX = p.X, OutY = p.Y, InX = 2 * ln.X - p.X, InY = 2 * ln.Y - p.Y };   // drag = a smooth point
                break;
        }
        Rebuild();
    }

    void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        CanvasHost.ReleaseMouseCapture();
        if (_drag == DragKind.Create && _preview is { } s)
        {
            _preview = null;
            var b = VectorGeometry.Bounds(s);
            if (b.Width >= Px(2) || b.Height >= Px(2))
            {
                Snapshot();
                _doc.Shapes.Add(s);
                _sel.Clear();
                _sel.Add(s.Id);
            }
        }
        if (_drag == DragKind.Move && !_dragSnapshot && ShapeAt(_from) is { } clicked && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _sel.Clear();   // a plain click on a shape of a multi-selection selects just that one
            _sel.Add(clicked.Id);
        }
        _drag = DragKind.None;
        RefreshProps();
        Rebuild();
    }

    void Replace(IEnumerable<VShape> changed)
    {
        foreach (var c in changed)
        {
            int i = _doc.Shapes.FindIndex(s => s.Id == c.Id);
            if (i >= 0) _doc.Shapes[i] = c;
        }
    }

    void ScaleFromHandle(Point p, bool keepRatio)
    {
        var b = _origBounds;
        // the opposite handle stays where it is
        var pts = HandlePoints(b);
        var anchor = pts[(_handle + 4) % 8];
        double sx = _handle is 1 or 5 ? 1 : (p.X - anchor.X) / (pts[_handle].X - anchor.X);
        double sy = _handle is 3 or 7 ? 1 : (p.Y - anchor.Y) / (pts[_handle].Y - anchor.Y);
        if (double.IsNaN(sx) || double.IsInfinity(sx)) sx = 1;
        if (double.IsNaN(sy) || double.IsInfinity(sy)) sy = 1;
        if (keepRatio && _handle % 2 == 0) sx = sy = Math.Abs(sx) > Math.Abs(sy) ? sx : sy;
        Replace(_originals.Select(o => { var c = o.Clone(); VectorGeometry.Scale(c, anchor.X, anchor.Y, sx, sy); return c; }));
    }

    VShape MakeShape(Point a, Point b, bool shift)
    {
        double w = b.X - a.X, h = b.Y - a.Y;
        if (shift && ToolName is "Rect" or "Ellipse") { double m = Math.Max(Math.Abs(w), Math.Abs(h)); (w, h) = (Math.Sign(w) * m, Math.Sign(h) * m); }
        var r = new Rect(a, new Point(a.X + w, a.Y + h));
        VShape s = ToolName switch
        {
            "Rect" => new VShape { Kind = VShapeKind.Rect, Name = "Rectangle", X = r.X, Y = r.Y, W = r.Width, H = r.Height },
            "Ellipse" => new VShape { Kind = VShapeKind.Ellipse, Name = "Ellipse", X = r.X, Y = r.Y, W = r.Width, H = r.Height },
            "Polygon" => VectorGeometry.Polygon(a.X, a.Y, (b - a).Length, int.TryParse(SidesBox.Text, out int n) ? n : 6, StarBox.IsChecked == true),
            _ => new VShape { Kind = VShapeKind.Path, Name = "Line", Contours = [new VContour([VNode.Corner(a.X, a.Y), LineEnd(a, b, shift)], false)] },
        };
        ApplyLook(s, ToolName == "Line");
        return s;
    }

    static VNode LineEnd(Point a, Point b, bool snap45)
    {
        if (!snap45) return VNode.Corner(b.X, b.Y);
        double ang = Math.Round(Math.Atan2(b.Y - a.Y, b.X - a.X) / (Math.PI / 4)) * Math.PI / 4, len = (b - a).Length;
        return VNode.Corner(a.X + Math.Cos(ang) * len, a.Y + Math.Sin(ang) * len);
    }

    // ---------- pen ----------

    void PenClick(Point p)
    {
        if (_pen is null || !_doc.Shapes.Contains(_pen))
        {
            Snapshot();
            _pen = new VShape { Kind = VShapeKind.Path, Name = "Path", Contours = [new VContour([VNode.Corner(p.X, p.Y)], false)] };
            ApplyLook(_pen, line: false);
            _doc.Shapes.Add(_pen);
            _sel.Clear();
            _sel.Add(_pen.Id);
            _drag = DragKind.PenHandle;
            return;
        }
        var nodes = _pen.Contours[0].Nodes;
        if (nodes.Count >= 2 && (new Point(nodes[0].X, nodes[0].Y) - p).Length <= Px(8))
        {
            _pen.Contours[0] = new VContour(nodes, true);
            FinishPen();
            return;
        }
        nodes.Add(VNode.Corner(p.X, p.Y));
        _drag = DragKind.PenHandle;
    }

    void FinishPen()
    {
        if (_pen is not null && _pen.Contours[0].Nodes.Count < 2) _doc.Shapes.Remove(_pen);   // a single click is not a path
        _pen = null;
        _drag = DragKind.None;
        RefreshProps();
        Rebuild();
    }

    void ToggleSmooth(VShape s, int c, int n)
    {
        Snapshot();
        var nodes = s.Contours[c].Nodes;
        var nd = nodes[n];
        if (nd.Smooth) nodes[n] = VNode.Corner(nd.X, nd.Y);
        else
        {
            var prev = nodes[(n - 1 + nodes.Count) % nodes.Count];
            var next = nodes[(n + 1) % nodes.Count];
            double dx = (next.X - prev.X) / 4, dy = (next.Y - prev.Y) / 4;
            nodes[n] = nd with { InX = nd.X - dx, InY = nd.Y - dy, OutX = nd.X + dx, OutY = nd.Y + dy };
        }
        Rebuild();
    }

    void DeleteNode()
    {
        if (Single is not { Kind: VShapeKind.Path } s || _node is not var (c, n)) return;
        var nodes = s.Contours[c].Nodes;
        if (nodes.Count <= 2) { Status.Text = "A path needs at least two points; delete the shape instead (select tool)."; return; }
        Snapshot();
        nodes.RemoveAt(n);
        _node = null;
        Rebuild();
    }

    // ---------- look: fill, stroke, opacity ----------

    VPaint? FillFor(Rect b) => _fillKind switch
    {
        "None" => null,
        "Linear" => new VPaint(VPaintKind.Linear, _fill1, [new VStop(0, _fill1), new VStop(1, _fill2)], b.Left, b.Top + b.Height / 2, b.Right, b.Top + b.Height / 2),
        "Radial" => new VPaint(VPaintKind.Radial, _fill1, [new VStop(0, _fill1), new VStop(1, _fill2)], b.Left + b.Width / 2, b.Top + b.Height / 2, 0, 0, Math.Max(b.Width, b.Height) / 2),
        _ => VPaint.Solid(_fill1),
    };

    double StrokeWidth => double.TryParse(StrokeWidthBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double w) ? Math.Clamp(w, 0, 1000) : 1;

    void ApplyLook(VShape s, bool line)
    {
        s.Fill = line ? null : FillFor(VectorGeometry.Bounds(s));
        s.Stroke = StrokeBox.IsChecked == true || line ? VPaint.Solid(_strokeColor) : null;
        s.StrokeWidth = StrokeWidth;
    }

    /// <summary>Changes to the look apply to the selection; one undo step per editing session (until something else changes).</summary>
    void ApplyToSelection()
    {
        if (_sel.Count == 0) return;
        if (!_editSnapshot) { Snapshot(); _editSnapshot = true; }
        foreach (var s in Selected) Recolor(s);
        Rebuild();
    }

    void Recolor(VShape s)
    {
        if (s.Kind == VShapeKind.Group) { foreach (var c in s.Children) Recolor(c); return; }
        ApplyLook(s, line: false);
    }

    void RefreshProps()
    {
        _refreshing = true;
        _editSnapshot = false;
        var s = Single;
        SelectionTitle.Text = _sel.Count switch { 0 => "Nothing selected (settings apply to new shapes)", 1 => $"{s!.Kind}: {s.Name}", var n => $"{n} shapes" };
        NameBox.IsEnabled = s is not null;
        NameBox.Text = s?.Name ?? "";
        if (s is not null && s.Kind != VShapeKind.Group)
        {
            _fillKind = s.Fill is null ? "None" : s.Fill.Kind.ToString();
            if (s.Fill is { } f) { _fill1 = f.Stops?.FirstOrDefault()?.Color ?? f.Color; _fill2 = f.Stops?.LastOrDefault()?.Color ?? _fill2; }
            StrokeBox.IsChecked = s.Stroke is not null;
            if (s.Stroke is { } st) _strokeColor = st.Color;
            StrokeWidthBox.Text = s.StrokeWidth.ToString("0.##", CultureInfo.InvariantCulture);
            OpacitySlider.Value = s.Opacity;
        }
        FillKindBox.SelectedItem = _fillKind;
        Picker.Color = TargetColor;
        _refreshing = false;
    }

    void OnColorTarget(object sender, RoutedEventArgs e)
    {
        _refreshing = true;
        Picker.Color = TargetColor;
        _refreshing = false;
    }

    /// <summary>The color the picker edits: stroke, second gradient color or fill.</summary>
    uint TargetColor => EditStroke.IsChecked == true ? _strokeColor : EditFill2.IsChecked == true ? _fill2 : _fill1;

    void SelectAll()
    {
        _sel.Clear();
        _sel.AddRange(_doc.Shapes.Select(s => s.Id));
        RefreshProps();
        Rebuild();
    }

    void OnPickerColor(uint c)
    {
        if (_refreshing) return;
        if (EditStroke.IsChecked == true) { _strokeColor = c; StrokeBox.IsChecked = true; }
        else if (EditFill2.IsChecked == true) _fill2 = c;
        else { _fill1 = c; if (_fillKind == "None") { _fillKind = "Solid"; _refreshing = true; FillKindBox.SelectedItem = "Solid"; _refreshing = false; } }
        ApplyToSelection();
    }

    void OnPaintChanged(object sender, RoutedEventArgs e)
    {
        if (_refreshing || !_ready) return;
        _fillKind = FillKindBox.SelectedItem as string ?? "Solid";
        ApplyToSelection();
    }

    void OnOpacity(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_refreshing || _sel.Count == 0) return;
        if (!_editSnapshot) { Snapshot(); _editSnapshot = true; }
        foreach (var s in Selected) s.Opacity = Math.Round(e.NewValue, 2);
        Rebuild();
    }

    void OnOpacityDone(object sender, DragCompletedEventArgs e) => _editSnapshot = false;
    void OnOpacityKey(object sender, KeyEventArgs e) => _editSnapshot = false;

    void OnNameCommit(object sender, RoutedEventArgs e)
    {
        if (_refreshing || Single is not { } s || NameBox.Text.Trim() == s.Name) return;
        Snapshot();
        s.Name = NameBox.Text.Trim();
        RefreshProps();
    }

    void OnToolClick(object sender, RoutedEventArgs e)
    {
        if (_pen is not null && ToolName != "Pen") FinishPen();
        _node = null;
        DrawOverlay();
    }

    public string CurrentToolName => ToolName;

    public void SelectTool(string name)
    {
        if (ToolsPanel.Children.OfType<RadioButton>().FirstOrDefault(r => string.Equals((string)r.Tag, name, StringComparison.OrdinalIgnoreCase)) is { } r) r.IsChecked = true;
        OnToolClick(this, new RoutedEventArgs());
    }

    // ---------- arrange, combine, align ----------

    void Arrange(bool front)
    {
        if (_sel.Count == 0) return;
        Snapshot();
        var moving = _doc.Shapes.Where(s => _sel.Contains(s.Id)).ToList();
        _doc.Shapes.RemoveAll(s => _sel.Contains(s.Id));
        if (front) _doc.Shapes.AddRange(moving); else _doc.Shapes.InsertRange(0, moving);
        Rebuild();
    }

    void OnToFront(object? sender, RoutedEventArgs? e) => Arrange(true);
    void OnToBack(object? sender, RoutedEventArgs? e) => Arrange(false);

    void OnGroup(object? sender, RoutedEventArgs? e)
    {
        if (_sel.Count < 2) return;
        Snapshot();
        var members = _doc.Shapes.Where(s => _sel.Contains(s.Id)).ToList();
        int at = _doc.Shapes.IndexOf(members[^1]) - members.Count + 1;
        _doc.Shapes.RemoveAll(s => _sel.Contains(s.Id));
        var g = new VShape { Kind = VShapeKind.Group, Name = "Group", Children = members };
        _doc.Shapes.Insert(Math.Clamp(at, 0, _doc.Shapes.Count), g);
        _sel.Clear();
        _sel.Add(g.Id);
        RefreshProps();
        Rebuild();
    }

    void OnUngroup(object? sender, RoutedEventArgs? e)
    {
        var groups = Selected.Where(s => s.Kind == VShapeKind.Group).ToList();
        if (groups.Count == 0) return;
        Snapshot();
        _sel.Clear();
        foreach (var g in groups)
        {
            int at = _doc.Shapes.IndexOf(g);
            _doc.Shapes.RemoveAt(at);
            foreach (var c in g.Children) c.Opacity *= g.Opacity;
            _doc.Shapes.InsertRange(at, g.Children);
            _sel.AddRange(g.Children.Select(c => c.Id));
        }
        RefreshProps();
        Rebuild();
    }

    void OnToPath(object? sender, RoutedEventArgs? e)
    {
        var shapes = Selected.Where(s => s.Kind != VShapeKind.Path).ToList();
        if (shapes.Count == 0) return;
        Snapshot();
        foreach (var s in shapes)
        {
            var look = s.Kind == VShapeKind.Group ? s.Children.FirstOrDefault() ?? s : s;
            var p = new VShape
            {
                Id = s.Id, Name = s.Name, Kind = VShapeKind.Path, Contours = VectorGeometry.Contours(VectorGeometry.Of(s)), EvenOdd = s.Kind == VShapeKind.Group,
                Fill = look.Fill, Stroke = look.Stroke, StrokeWidth = look.StrokeWidth, Opacity = s.Opacity,
            };
            _doc.Shapes[_doc.Shapes.IndexOf(s)] = p;
        }
        RefreshProps();
        Rebuild();
    }

    void Combine(VectorGeometry.BoolOp op)
    {
        var shapes = _doc.Shapes.Where(s => _sel.Contains(s.Id)).ToList();   // drawing order
        if (shapes.Count < 2) { Status.Text = "Select two or more shapes (Shift+click)."; return; }
        Snapshot();
        var result = VectorGeometry.Combine(shapes, op);
        int at = _doc.Shapes.IndexOf(shapes[0]);
        _doc.Shapes.RemoveAll(s => _sel.Contains(s.Id));
        _doc.Shapes.Insert(Math.Min(at, _doc.Shapes.Count), result);
        _sel.Clear();
        _sel.Add(result.Id);
        RefreshProps();
        Rebuild();
    }

    void OnUnion(object sender, RoutedEventArgs e) => Combine(VectorGeometry.BoolOp.Union);
    void OnIntersect(object sender, RoutedEventArgs e) => Combine(VectorGeometry.BoolOp.Intersect);
    void OnSubtract(object sender, RoutedEventArgs e) => Combine(VectorGeometry.BoolOp.Subtract);
    void OnExclude(object sender, RoutedEventArgs e) => Combine(VectorGeometry.BoolOp.Exclude);

    void OnAlign(object sender, RoutedEventArgs e)
    {
        if (_sel.Count == 0) return;
        Snapshot();
        VectorGeometry.AlignShapes([.. Selected], Enum.Parse<VectorGeometry.Align>((string)((Button)sender).Tag), new Rect(0, 0, _doc.Width, _doc.Height));
        Rebuild();
    }

    void Distribute(bool horizontal)
    {
        if (_sel.Count < 3) { Status.Text = "Select three or more shapes to space them evenly."; return; }
        Snapshot();
        VectorGeometry.Distribute([.. Selected], horizontal);
        Rebuild();
    }

    void OnDistributeH(object sender, RoutedEventArgs e) => Distribute(true);
    void OnDistributeV(object sender, RoutedEventArgs e) => Distribute(false);

    void DeleteSelected()
    {
        if (_sel.Count == 0) return;
        Snapshot();
        _doc.Shapes.RemoveAll(s => _sel.Contains(s.Id));
        _sel.Clear();
        RefreshProps();
        Rebuild();
    }

    void Duplicate()
    {
        if (_sel.Count == 0) return;
        Snapshot();
        var copies = Selected.Select(s => { var c = s.CloneNew(); VectorGeometry.Move(c, Px(10), Px(10)); return c; }).ToList();
        _doc.Shapes.AddRange(copies);
        _sel.Clear();
        _sel.AddRange(copies.Select(c => c.Id));
        Rebuild();
    }

    void Nudge(double dx, double dy)
    {
        if (_sel.Count == 0) return;
        Snapshot();
        foreach (var s in Selected) VectorGeometry.Move(s, dx, dy);
        Rebuild();
    }

    // ---------- undo ----------

    void OnUndo(object? sender, RoutedEventArgs? e) => Restore(_history.Undo(_doc));
    void OnRedo(object? sender, RoutedEventArgs? e) => Restore(_history.Redo(_doc));

    void Restore(VectorDoc? d)
    {
        if (d is null || _drag != DragKind.None) return;
        _doc = d;
        _pen = null;
        _node = null;
        _sel.RemoveAll(id => _doc.Shapes.All(s => s.Id != id));
        _dirty = true;
        RefreshProps();
        Rebuild();
        UpdateTitle();
    }

    // ---------- files ----------

    const string Filter = "PixelForge vector drawing (*.pxvec)|*.pxvec";

    void OnNew(object? sender, RoutedEventArgs? e)
    {
        if (!ConfirmDiscard()) return;
        if (Ui.AskFields(this, "New drawing", ("_Art board size", "64 × 64", ["16 × 16", "32 × 32", "64 × 64", "128 × 128", "256 × 256", "512 × 512", "1920 × 1080"])) is not { } f) return;
        var parts = f[0].Split('×', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double w) || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double h) || w <= 0 || h <= 0)
        {
            Ui.Error(new FormatException("Write the size as width × height."));
            return;
        }
        LoadDoc(new VectorDoc { Width = w, Height = h }, null);
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
        if (_pen is not null) FinishPen();
        if (path is null)
        {
            var dlg = new SaveFileDialog { Filter = Filter, FileName = "drawing.pxvec", InitialDirectory = App.Project?.Root ?? "" };
            if (dlg.ShowDialog() != true) return false;
            path = dlg.FileName;
        }
        try
        {
            VectorDoc.Save(_doc, path);
            (_path, _dirty) = (path, false);
            App.AddRecent(path);
            _aids.Rebind(path);
            UpdateTitle();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); return false; }
    }

    void OnImportSvg(object? sender, RoutedEventArgs? e)
    {
        var dlg = new OpenFileDialog { Filter = "SVG (*.svg)|*.svg" };
        if (dlg.ShowDialog() != true) return;
        Svg.ImportResult r;
        try { r = Svg.Import(File.ReadAllText(dlg.FileName)); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or FormatException) { Ui.Error(ex); return; }
        string note = r.Skipped.Count == 0 ? "" : "\n\nNot imported: " + string.Join(", ", r.Skipped);
        if (_doc.Shapes.Count > 0 && MessageBox.Show(Window.GetWindow(this), $"Add the SVG to the current drawing?\n(No opens it as a new drawing.){note}", "Import SVG",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            Snapshot();
            _doc.Shapes.AddRange(r.Doc.Shapes);
            _sel.Clear();
            _sel.AddRange(r.Doc.Shapes.Select(s => s.Id));
            RefreshProps();
            Rebuild();
            return;
        }
        if (!ConfirmDiscard()) return;
        LoadDoc(r.Doc, null);
        _dirty = true;
        UpdateTitle();
        if (note.Length > 0) MessageBox.Show(Window.GetWindow(this), note.Trim(), "Import SVG", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    void OnExportSvg(object? sender, RoutedEventArgs? e)
    {
        var dlg = new SaveFileDialog { Filter = "SVG (*.svg)|*.svg", FileName = System.IO.Path.ChangeExtension(System.IO.Path.GetFileName(_path) ?? "drawing", ".svg") };
        if (dlg.ShowDialog() != true) return;
        try
        {
            DocumentFile.WriteAtomic(dlg.FileName, Svg.Export(_doc));
            App.AddRecent(dlg.FileName);
            Status.Text = $"Exported {System.IO.Path.GetFileName(dlg.FileName)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); }
    }

    (int W, int H) SizeFor(int longest) =>
        _doc.Width >= _doc.Height ? (longest, Math.Max(1, (int)Math.Round(longest * _doc.Height / _doc.Width))) : (Math.Max(1, (int)Math.Round(longest * _doc.Width / _doc.Height)), longest);

    void OnExportPng(object? sender, RoutedEventArgs? e)
    {
        if (Ui.AskFields(this, "Export PNG",
                ("_Sizes (longest side, commas)", "16, 32, 64", null),
                ("_Output", "Separate files", ["Separate files", "One sprite sheet"]),
                ("_Edges", "Smooth", ["Smooth", "Crisp (pixel art)"])) is not { } f) return;
        var sizes = f[0].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out int n) ? n : -1).ToList();
        if (sizes.Count == 0 || sizes.Any(n => n is < 1 or > 8192)) { Ui.Error(new FormatException("Sizes: whole numbers from 1 to 8192, separated by commas.")); return; }
        var dlg = new SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = System.IO.Path.ChangeExtension(System.IO.Path.GetFileName(_path) ?? "drawing", ".png") };
        if (dlg.ShowDialog() != true) return;
        bool crisp = f[2].StartsWith("Crisp");
        try
        {
            var images = sizes.Select(n => { var (w, h) = SizeFor(n); return (Px: VectorGeometry.Rasterize(_doc, w, h, crisp), W: w, H: h, N: n); }).ToList();
            if (f[1] == "One sprite sheet")
            {
                int sw = images.Sum(i => i.W), sh = images.Max(i => i.H);
                var sheet = new uint[sw * sh];
                int x = 0;
                foreach (var i in images) { PixelOps.Stamp(sheet, sw, sh, i.Px, i.W, i.H, x, 0); x += i.W; }
                ImageIO.SavePng(dlg.FileName, sheet, sw, sh);
                App.AddRecent(dlg.FileName);
            }
            else
                foreach (var i in images)
                {
                    string file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dlg.FileName)!, $"{System.IO.Path.GetFileNameWithoutExtension(dlg.FileName)}_{i.N}.png");
                    ImageIO.SavePng(file, i.Px, i.W, i.H);
                    App.AddRecent(file);
                }
            Status.Text = $"Exported {images.Count} size(s).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); }
    }

    /// <summary>Set by MainWindow: opens pixels in Pixel mode.</summary>
    public Action<uint[], int, int>? SendToPixel { get; set; }

    void OnToPixel(object? sender, RoutedEventArgs? e)
    {
        if (Ui.AskFields(this, "To pixel art", ("_Longest side (px)", "32", ["16", "24", "32", "48", "64", "128"]), ("_Edges", "Crisp (pixel art)", ["Crisp (pixel art)", "Smooth"])) is not { } f) return;
        if (!int.TryParse(f[0], out int n) || n is < 1 or > 4096) { Ui.Error(new FormatException("A whole number of pixels, 1–4096.")); return; }
        var (w, h) = SizeFor(n);
        SendToPixel?.Invoke(VectorGeometry.Rasterize(_doc, w, h, f[1].StartsWith("Crisp")), w, h);
    }

    public (uint[] Pixels, int Width, int Height)? CurrentPicture =>
        SizeFor((int)Math.Ceiling(Math.Max(_doc.Width, _doc.Height))) is var (w, h) ? (VectorGeometry.Rasterize(_doc, w, h), w, h) : null;

    // ---------- style ----------

    sealed record VectorStyle(VPaint? Fill, VPaint? Stroke, double StrokeWidth, double Opacity);

    public object? CopyStyle() => Single is { } s && s.Kind != VShapeKind.Group ? new VectorStyle(s.Fill, s.Stroke, s.StrokeWidth, s.Opacity) : null;

    public string? PasteStyle(object style)
    {
        if (style is not VectorStyle st || _sel.Count == 0) return null;
        Snapshot();
        foreach (var s in Selected)
        {
            // gradients are placed over the target's own bounds
            var b = VectorGeometry.Bounds(s);
            VPaint? Fit(VPaint? p) => p is null || p.Kind == VPaintKind.Solid ? p : p.Kind == VPaintKind.Linear
                ? p with { X1 = b.Left, Y1 = b.Top + b.Height / 2, X2 = b.Right, Y2 = b.Top + b.Height / 2 }
                : p with { X1 = b.Left + b.Width / 2, Y1 = b.Top + b.Height / 2, R = Math.Max(b.Width, b.Height) / 2 };
            (s.Fill, s.Stroke, s.StrokeWidth, s.Opacity) = (Fit(st.Fill), st.Stroke, st.StrokeWidth, st.Opacity);
        }
        RefreshProps();
        Rebuild();
        return "Fill, stroke and opacity pasted.";
    }

    // ---------- IEditor ----------

    public string[] FileExtensions => [".pxvec", ".svg"];
    public bool IsDirty => _dirty;
    public string? CurrentPath => _path;

    public bool ConfirmDiscard() => Ui.ConfirmDiscard(_dirty, "drawing", () => Save(_path));

    public void OpenPath(string path, bool confirmed = false)
    {
        if (!confirmed && !ConfirmDiscard()) return;
        try
        {
            if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                var r = Svg.Import(File.ReadAllText(path));
                LoadDoc(r.Doc, null);   // saved as .pxvec; the SVG stays untouched
                _dirty = true;
                UpdateTitle();
                if (r.Skipped.Count > 0) Status.Text = "Not imported: " + string.Join(", ", r.Skipped);
                return;
            }
            LoadDoc(VectorDoc.Load(path), path);
            App.AddRecent(path);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or FormatException) { Ui.Error(ex); }
    }

    public void OnFileMoved(string oldPath, string newPath)
    {
        if (string.Equals(_path, oldPath, StringComparison.OrdinalIgnoreCase)) { _path = newPath; _aids.Rebind(newPath); UpdateTitle(); }
    }

    public void WriteAutosave(string file) => VectorDoc.Save(_doc, file);

    public void RestoreAutosave(string file, string? originalPath)
    {
        LoadDoc(VectorDoc.Load(file), originalPath);
        _dirty = true;
        UpdateTitle();
    }

    IReadOnlyList<MenuItem>? _menus;
    Action Do(RoutedEventHandler h) => () => h(this, new RoutedEventArgs());

    public IReadOnlyList<MenuItem> Menus => _menus ??=
    [
        Ui.Menu("_File",
            Ui.Item("_New drawing…", "Ctrl+N", Do(OnNew)),
            Ui.Item("_Open…", "Ctrl+O", Do(OnOpen)),
            Ui.RecentMenu(this),
            Ui.Item("_Save", "Ctrl+S", Do(OnSave)),
            Ui.Item("Save _as…", "Ctrl+Shift+S", () => Save(null)),
            new Separator(),
            Ui.Item("_Import SVG…", null, Do(OnImportSvg)),
            Ui.Item("Export S_VG…", null, Do(OnExportSvg)),
            Ui.Item("_Export PNG…", "Ctrl+E", Do(OnExportPng)),
            Ui.Item("To _pixel art…", null, Do(OnToPixel)),
            new Separator(),
            Ui.Item("E_xit", "Alt+F4", () => Window.GetWindow(this)?.Close())),
        Ui.Menu("_Edit",
            Ui.Item("_Undo", "Ctrl+Z", Do(OnUndo)),
            Ui.Item("_Redo", "Ctrl+Y", Do(OnRedo)),
            new Separator(),
            Ui.Item("Select _all", "Ctrl+A", SelectAll),
            Ui.Item("_Duplicate", "Ctrl+D", Duplicate),
            Ui.Item("De_lete", "Del", DeleteSelected)),
        Ui.Menu("_Object",
            Ui.Item("Bring to _front", "Ctrl+Shift+]", () => Arrange(true)),
            Ui.Item("Send to _back", "Ctrl+Shift+[", () => Arrange(false)),
            Ui.Item("_Group", "Ctrl+G", () => OnGroup(null, null)),
            Ui.Item("_Ungroup", "Ctrl+Shift+G", () => OnUngroup(null, null)),
            Ui.Item("To _path", null, () => OnToPath(null, null)),
            new Separator(),
            Ui.Item("U_nion", null, () => Combine(VectorGeometry.BoolOp.Union)),
            Ui.Item("_Intersect", null, () => Combine(VectorGeometry.BoolOp.Intersect)),
            Ui.Item("_Subtract", null, () => Combine(VectorGeometry.BoolOp.Subtract)),
            Ui.Item("E_xclude", null, () => Combine(VectorGeometry.BoolOp.Exclude))),
        Ui.Menu("_View",
            _aids.Menu(),
            new Separator(),
            Ui.Item("Zoom _in", "Ctrl+=", () => ZoomStep(+1)),
            Ui.Item("Zoom _out", "Ctrl+-", () => ZoomStep(-1)),
            Ui.Item("_Fit to window", "Ctrl+0", ZoomFit)),
    ];

    public void HandleKey(KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        double step = shift ? 10 : 1;
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Z when ctrl: OnUndo(null, null); break;
            case Key.Y when ctrl: OnRedo(null, null); break;
            case Key.S when ctrl && shift: Save(null); break;
            case Key.S when ctrl: OnSave(null, null); break;
            case Key.N when ctrl: OnNew(null, null); break;
            case Key.O when ctrl: OnOpen(null, null); break;
            case Key.E when ctrl: OnExportPng(null, null); break;
            case Key.A when ctrl: SelectAll(); break;
            case Key.D when ctrl: Duplicate(); break;
            case Key.G when ctrl && shift: OnUngroup(null, null); break;
            case Key.G when ctrl: OnGroup(null, null); break;
            case Key.OemCloseBrackets when ctrl: Arrange(true); break;
            case Key.OemOpenBrackets when ctrl: Arrange(false); break;
            case Key.OemPlus or Key.Add when ctrl: ZoomStep(+1); break;
            case Key.OemMinus or Key.Subtract when ctrl: ZoomStep(-1); break;
            case Key.D0 or Key.NumPad0 when ctrl: ZoomFit(); break;
            case Key.Enter or Key.Escape when _pen is not null: FinishPen(); break;
            case Key.Back when _pen is not null && _pen.Contours[0].Nodes.Count > 1: _pen.Contours[0].Nodes.RemoveAt(_pen.Contours[0].Nodes.Count - 1); Rebuild(); break;
            case Key.Delete when ToolName == "Nodes" && _node is not null: DeleteNode(); break;
            case Key.Delete: DeleteSelected(); break;
            case Key.Escape: _sel.Clear(); _node = null; RefreshProps(); Rebuild(); break;
            case Key.Left: Nudge(-step, 0); break;
            case Key.Right: Nudge(step, 0); break;
            case Key.Up: Nudge(0, -step); break;
            case Key.Down: Nudge(0, step); break;
            case Key.V when !ctrl: SelectTool("Select"); break;
            case Key.A when !ctrl: SelectTool("Nodes"); break;
            case Key.P when !ctrl: SelectTool("Pen"); break;
            case Key.R when !ctrl: SelectTool("Rect"); break;
            case Key.E when !ctrl: SelectTool("Ellipse"); break;
            case Key.Y when !ctrl: SelectTool("Polygon"); break;
            case Key.L when !ctrl: SelectTool("Line"); break;
            default: e.Handled = false; break;
        }
    }
}
