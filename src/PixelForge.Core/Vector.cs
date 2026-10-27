using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelForge.Core;

public enum VShapeKind { Path, Rect, Ellipse, Group }
public enum VPaintKind { Solid, Linear, Radial }

/// <summary>Colour stop of a gradient (offset 0–1).</summary>
public sealed record VStop(double Offset, uint Color);

/// <summary>Fill or stroke paint. Gradient points are in document coordinates (Linear: X1,Y1→X2,Y2; Radial: centre X1,Y1, radius R).</summary>
public sealed record VPaint(VPaintKind Kind, uint Color, List<VStop>? Stops = null, double X1 = 0, double Y1 = 0, double X2 = 0, double Y2 = 0, double R = 0)
{
    public static VPaint Solid(uint c) => new(VPaintKind.Solid, c);
}

/// <summary>A point of a path with its Bezier handles (absolute coordinates; a handle equal to the point = no curve on that side).</summary>
public sealed record VNode(double X, double Y, double InX, double InY, double OutX, double OutY)
{
    public static VNode Corner(double x, double y) => new(x, y, x, y, x, y);
    public VNode Moved(double dx, double dy) => new(X + dx, Y + dy, InX + dx, InY + dy, OutX + dx, OutY + dy);
    public bool Smooth => (InX, InY) != (X, Y) || (OutX, OutY) != (X, Y);
}

public sealed record VContour(List<VNode> Nodes, bool Closed);

/// <summary>
/// One vector shape. Rect and Ellipse keep their parameters (editable as such); Path holds contours of Bezier nodes;
/// Group holds children. Moving or scaling changes the geometry directly (no separate transform to keep in sync).
/// </summary>
public sealed class VShape
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public VShapeKind Kind { get; set; }
    public List<VContour> Contours { get; set; } = [];
    public bool EvenOdd { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    /// <summary>Rounded corner radius for Rect.</summary>
    public double Radius { get; set; }
    public VPaint? Fill { get; set; }
    public VPaint? Stroke { get; set; }
    public double StrokeWidth { get; set; } = 1;
    public double Opacity { get; set; } = 1;
    public List<VShape> Children { get; set; } = [];

    public VShape Clone()
    {
        var c = (VShape)MemberwiseClone();
        c.Contours = [.. Contours.Select(k => new VContour([.. k.Nodes], k.Closed))];
        c.Children = [.. Children.Select(x => x.Clone())];
        return c;
    }

    /// <summary>Copy under new ids (duplicate, paste, template copies).</summary>
    public VShape CloneNew()
    {
        var c = Clone();
        c.Id = Guid.NewGuid();
        c.Children = [.. Children.Select(x => x.CloneNew())];
        return c;
    }
}

/// <summary>Vector drawing (.pxvec): an art board of Width×Height units; shapes are drawn first to last.</summary>
public sealed class VectorDoc
{
    public const int Version = 1;
    public double Width { get; set; } = 64;
    public double Height { get; set; } = 64;
    public List<VShape> Shapes { get; set; } = [];

    public VectorDoc Clone() => new() { Width = Width, Height = Height, Shapes = [.. Shapes.Select(s => s.Clone())] };

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() }, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    record Dto(int Version, double Width, double Height, List<VShape> Shapes);

    public static string Serialize(VectorDoc d) => JsonSerializer.Serialize(new Dto(Version, d.Width, d.Height, d.Shapes), Json);

    public static VectorDoc Deserialize(string json)
    {
        var dto = JsonSerializer.Deserialize<Dto>(json, Json) ?? throw new InvalidDataException("Empty vector file.");
        if (dto.Width is <= 0 or > 100000 || dto.Height is <= 0 or > 100000) throw new InvalidDataException("The art board size must be 1–100000.");
        var d = new VectorDoc { Width = dto.Width, Height = dto.Height, Shapes = dto.Shapes ?? [] };
        var ids = new HashSet<Guid>();
        void Check(VShape s)
        {
            if (!ids.Add(s.Id)) throw new InvalidDataException("The drawing contains duplicate shape ids.");
            s.Contours ??= [];
            s.Children ??= [];
            if (s.StrokeWidth < 0 || s.Opacity is < 0 or > 1) throw new InvalidDataException($"Shape “{s.Name}” has a negative stroke or an opacity outside 0–1.");
            foreach (var c in s.Children) Check(c);
        }
        foreach (var s in d.Shapes) Check(s);
        return d;
    }

    public static void Save(VectorDoc d, string path) => DocumentFile.Save(path, Serialize(d), Version);
    public static VectorDoc Load(string path) => DocumentFile.Load(path, "vector drawing", Version, Deserialize);
}

/// <summary>Shapes as WPF geometry: drawing, hit testing, bounds, boolean operations and rasterising.</summary>
public static class VectorGeometry
{
    public static Geometry Of(VShape s)
    {
        switch (s.Kind)
        {
            case VShapeKind.Rect: return new RectangleGeometry(new Rect(s.X, s.Y, Math.Max(0, s.W), Math.Max(0, s.H)), s.Radius, s.Radius);
            case VShapeKind.Ellipse: return new EllipseGeometry(new Rect(s.X, s.Y, Math.Max(0, s.W), Math.Max(0, s.H)));
            case VShapeKind.Group:
                var g = new GeometryGroup();
                foreach (var c in s.Children) g.Children.Add(Of(c));
                return g;
            default:
                var pg = new PathGeometry { FillRule = s.EvenOdd ? FillRule.EvenOdd : FillRule.Nonzero };
                foreach (var c in s.Contours.Where(c => c.Nodes.Count > 0))
                {
                    var f = new PathFigure { StartPoint = new Point(c.Nodes[0].X, c.Nodes[0].Y), IsClosed = c.Closed, IsFilled = true };
                    int n = c.Nodes.Count, segs = c.Closed ? n : n - 1;
                    for (int i = 0; i < segs; i++)
                    {
                        var a = c.Nodes[i];
                        var b = c.Nodes[(i + 1) % n];
                        f.Segments.Add(new BezierSegment(new Point(a.OutX, a.OutY), new Point(b.InX, b.InY), new Point(b.X, b.Y), true));
                    }
                    pg.Figures.Add(f);
                }
                return pg;
        }
    }

    public static Rect Bounds(VShape s)
    {
        var b = Of(s).Bounds;
        return b.IsEmpty ? new Rect(s.X, s.Y, 0, 0) : b;
    }

    public static Rect Bounds(IEnumerable<VShape> shapes) =>
        shapes.Select(Bounds).Aggregate(Rect.Empty, (a, b) => { a.Union(b); return a; });

    /// <summary>Converts any geometry (also the result of Geometry.Combine) into Bezier contours. Arcs and quadratics become cubics.</summary>
    public static List<VContour> Contours(Geometry g)
    {
        var path = PathGeometry.CreateFromGeometry(g);
        var list = new List<VContour>();
        foreach (var f in path.Figures)
        {
            var nodes = new List<VNode> { VNode.Corner(f.StartPoint.X, f.StartPoint.Y) };
            var cur = f.StartPoint;
            void Line(Point p) { nodes.Add(VNode.Corner(p.X, p.Y)); cur = p; }
            void Cubic(Point c1, Point c2, Point p)
            {
                var last = nodes[^1];
                nodes[^1] = last with { OutX = c1.X, OutY = c1.Y };
                nodes.Add(new VNode(p.X, p.Y, c2.X, c2.Y, p.X, p.Y));
                cur = p;
            }
            foreach (var seg in f.Segments)
                switch (seg)
                {
                    case LineSegment l: Line(l.Point); break;
                    case PolyLineSegment pl: foreach (var p in pl.Points) Line(p); break;
                    case BezierSegment b: Cubic(b.Point1, b.Point2, b.Point3); break;
                    case PolyBezierSegment pb: for (int i = 0; i + 2 < pb.Points.Count; i += 3) Cubic(pb.Points[i], pb.Points[i + 1], pb.Points[i + 2]); break;
                    case QuadraticBezierSegment q: Cubic(cur + (q.Point1 - cur) * 2 / 3, q.Point2 + (q.Point1 - q.Point2) * 2 / 3, q.Point2); break;
                    case PolyQuadraticBezierSegment pq:
                        for (int i = 0; i + 1 < pq.Points.Count; i += 2) Cubic(cur + (pq.Points[i] - cur) * 2 / 3, pq.Points[i + 1] + (pq.Points[i] - pq.Points[i + 1]) * 2 / 3, pq.Points[i + 1]);
                        break;
                    case ArcSegment:
                        // ponytail: arcs (only from SVG import) become short lines (0.25 px tolerance); exact arc-to-Bezier if curves look faceted
                        var flat = new PathGeometry([new PathFigure(cur, [seg.CloneCurrentValue()], false)]).GetFlattenedPathGeometry(0.25, ToleranceType.Absolute);
                        foreach (var ff in flat.Figures) foreach (var s2 in ff.Segments)
                            if (s2 is PolyLineSegment fl) foreach (var p in fl.Points) Line(p);
                            else if (s2 is LineSegment ln) Line(ln.Point);
                        break;
                }
            // a closed figure whose last point repeats the start: merge them so the start keeps its incoming handle
            if (f.IsClosed && nodes.Count > 1 && nodes[^1].X == nodes[0].X && nodes[^1].Y == nodes[0].Y)
            {
                var last = nodes[^1];
                nodes.RemoveAt(nodes.Count - 1);
                nodes[0] = nodes[0] with { InX = last.InX, InY = last.InY };
            }
            list.Add(new VContour(nodes, f.IsClosed));
        }
        return list;
    }

    public enum BoolOp { Union, Intersect, Subtract, Exclude }

    /// <summary>
    /// Boolean operation over the shapes in drawing order (Subtract removes every later shape from the first).
    /// The result is one path with the first shape's fill and stroke.
    /// </summary>
    public static VShape Combine(IReadOnlyList<VShape> shapes, BoolOp op)
    {
        if (shapes.Count < 2) throw new ArgumentException("Select at least two shapes.", nameof(shapes));
        Geometry acc = Of(shapes[0]);
        var mode = op switch { BoolOp.Intersect => GeometryCombineMode.Intersect, BoolOp.Subtract => GeometryCombineMode.Exclude, BoolOp.Exclude => GeometryCombineMode.Xor, _ => GeometryCombineMode.Union };
        foreach (var s in shapes.Skip(1)) acc = Geometry.Combine(acc, Of(s), mode, null, 0.01, ToleranceType.Absolute);
        var first = shapes[0];
        return new VShape
        {
            Kind = VShapeKind.Path, Name = $"{op} of {shapes.Count}", Contours = Contours(acc), EvenOdd = true,
            Fill = first.Fill, Stroke = first.Stroke, StrokeWidth = first.StrokeWidth, Opacity = first.Opacity,
        };
    }

    public static Brush? BrushOf(VPaint? p)
    {
        if (p is null) return null;
        Color C(uint c) => Color.FromArgb((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c);
        var stops = new GradientStopCollection((p.Stops ?? [new VStop(0, p.Color), new VStop(1, p.Color)]).Select(s => new GradientStop(C(s.Color), s.Offset)));
        Brush b = p.Kind switch
        {
            VPaintKind.Linear => new LinearGradientBrush(stops, new Point(p.X1, p.Y1), new Point(p.X2, p.Y2)) { MappingMode = BrushMappingMode.Absolute },
            VPaintKind.Radial => new RadialGradientBrush(stops)
            {
                MappingMode = BrushMappingMode.Absolute, Center = new Point(p.X1, p.Y1), GradientOrigin = new Point(p.X1, p.Y1), RadiusX = p.R, RadiusY = p.R,
            },
            _ => new SolidColorBrush(C(p.Color)),
        };
        b.Freeze();
        return b;
    }

    /// <summary>Draws the shapes (groups recursively) into a drawing context.</summary>
    public static void Draw(DrawingContext dc, IEnumerable<VShape> shapes)
    {
        foreach (var s in shapes)
        {
            dc.PushOpacity(s.Opacity);
            if (s.Kind == VShapeKind.Group) Draw(dc, s.Children);
            else dc.DrawGeometry(BrushOf(s.Fill), s.Stroke is null || s.StrokeWidth <= 0 ? null : new Pen(BrushOf(s.Stroke), s.StrokeWidth) { LineJoin = PenLineJoin.Round }, Of(s));
            dc.Pop();
        }
    }

    /// <summary>The drawing as pixels at a size (art board scaled to fit w×h); aliased = crisp pixel edges for pixel art.</summary>
    public static uint[] Rasterize(VectorDoc d, int w, int h, bool aliased = false) => Sta.Run(() =>
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(w / d.Width, h / d.Height));
            Draw(dc, d.Shapes);
            dc.Pop();
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var px = new uint[w * h];
        rtb.CopyPixels(px, w * 4, 0);
        ImageIO.Unpremultiply(px, aliased);
        return px;
    });

    // ---------- editing helpers ----------

    /// <summary>Moves a shape (and children) by dx, dy, gradients included.</summary>
    public static void Move(VShape s, double dx, double dy)
    {
        s.X += dx; s.Y += dy;
        s.Contours = [.. s.Contours.Select(c => new VContour([.. c.Nodes.Select(n => n.Moved(dx, dy))], c.Closed))];
        s.Fill = MovePaint(s.Fill, dx, dy);
        s.Stroke = MovePaint(s.Stroke, dx, dy);
        foreach (var c in s.Children) Move(c, dx, dy);
    }

    static VPaint? MovePaint(VPaint? p, double dx, double dy) => p is null || p.Kind == VPaintKind.Solid ? p : p with { X1 = p.X1 + dx, Y1 = p.Y1 + dy, X2 = p.X2 + dx, Y2 = p.Y2 + dy };

    /// <summary>Scales a shape around an origin (bounding-box handles); stroke widths stay.</summary>
    public static void Scale(VShape s, double ox, double oy, double sx, double sy)
    {
        double X(double x) => ox + (x - ox) * sx;
        double Y(double y) => oy + (y - oy) * sy;
        (s.X, s.Y, s.W, s.H) = (X(s.X), Y(s.Y), s.W * sx, s.H * sy);
        if (s.W < 0) { s.X += s.W; s.W = -s.W; }
        if (s.H < 0) { s.Y += s.H; s.H = -s.H; }
        s.Contours = [.. s.Contours.Select(c => new VContour([.. c.Nodes.Select(n => new VNode(X(n.X), Y(n.Y), X(n.InX), Y(n.InY), X(n.OutX), Y(n.OutY)))], c.Closed))];
        VPaint? P(VPaint? p) => p is null || p.Kind == VPaintKind.Solid ? p : p with { X1 = X(p.X1), Y1 = Y(p.Y1), X2 = X(p.X2), Y2 = Y(p.Y2), R = p.R * Math.Max(Math.Abs(sx), Math.Abs(sy)) };
        (s.Fill, s.Stroke) = (P(s.Fill), P(s.Stroke));
        foreach (var c in s.Children) Scale(c, ox, oy, sx, sy);
    }

    public enum Align { Left, Center, Right, Top, Middle, Bottom }

    /// <summary>Aligns shapes to the bounds of the whole selection (or the art board when one shape is selected).</summary>
    public static void AlignShapes(IReadOnlyList<VShape> shapes, Align a, Rect? board = null)
    {
        var all = shapes.Count == 1 && board is Rect bd ? bd : Bounds(shapes);
        foreach (var s in shapes)
        {
            var b = Bounds(s);
            double dx = a switch { Align.Left => all.Left - b.Left, Align.Center => all.Left + all.Width / 2 - (b.Left + b.Width / 2), Align.Right => all.Right - b.Right, _ => 0 };
            double dy = a switch { Align.Top => all.Top - b.Top, Align.Middle => all.Top + all.Height / 2 - (b.Top + b.Height / 2), Align.Bottom => all.Bottom - b.Bottom, _ => 0 };
            Move(s, dx, dy);
        }
    }

    /// <summary>Equal gaps between shapes along x (or y), keeping the first and last in place.</summary>
    public static void Distribute(IReadOnlyList<VShape> shapes, bool horizontal)
    {
        if (shapes.Count < 3) return;
        var sorted = shapes.OrderBy(s => horizontal ? Bounds(s).Left : Bounds(s).Top).ToList();
        double total = sorted.Sum(s => horizontal ? Bounds(s).Width : Bounds(s).Height);
        var first = Bounds(sorted[0]);
        var last = Bounds(sorted[^1]);
        double span = horizontal ? last.Right - first.Left : last.Bottom - first.Top;
        double gap = (span - total) / (sorted.Count - 1), at = horizontal ? first.Left : first.Top;
        foreach (var s in sorted)
        {
            var b = Bounds(s);
            if (horizontal) Move(s, at - b.Left, 0); else Move(s, 0, at - b.Top);
            at += (horizontal ? b.Width : b.Height) + gap;
        }
    }

    /// <summary>A regular polygon or star as a closed path.</summary>
    public static VShape Polygon(double cx, double cy, double r, int sides, bool star = false)
    {
        sides = Math.Clamp(sides, 3, 64);
        var nodes = new List<VNode>();
        int n = star ? sides * 2 : sides;
        for (int i = 0; i < n; i++)
        {
            double a = -Math.PI / 2 + i * 2 * Math.PI / n, rr = star && i % 2 == 1 ? r * 0.45 : r;
            nodes.Add(VNode.Corner(cx + Math.Cos(a) * rr, cy + Math.Sin(a) * rr));
        }
        return new VShape { Kind = VShapeKind.Path, Name = star ? "Star" : "Polygon", Contours = [new VContour(nodes, true)] };
    }
}

/// <summary>Runs WPF rendering work on an STA thread (worker threads and test runners are MTA).</summary>
public static class Sta
{
    public static T Run<T>(Func<T> f)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) return f();
        T? result = default;
        Exception? error = null;
        var t = new Thread(() => { try { result = f(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        return result!;
    }
}
