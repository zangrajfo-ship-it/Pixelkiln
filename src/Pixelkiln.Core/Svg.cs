using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace Pixelkiln.Core;

/// <summary>
/// SVG to and from vector drawings. Import reads a common subset (path, rect, circle, ellipse, line, polyline,
/// polygon, g with transforms, fill, stroke, opacity, linear and radial gradients); everything else is listed in the
/// report instead of being dropped silently.
/// </summary>
public static class Svg
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly XNamespace Ns = "http://www.w3.org/2000/svg";
    static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";

    public sealed record ImportResult(VectorDoc Doc, List<string> Skipped);

    public static ImportResult Import(string text)
    {
        XDocument x;
        try
        {
            // no DTD processing: an SVG can declare external entities
            using var reader = System.Xml.XmlReader.Create(new StringReader(text), new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
            x = XDocument.Load(reader);
        }
        catch (System.Xml.XmlException ex) { throw new InvalidDataException($"Not a valid SVG: {ex.Message}"); }
        var root = x.Root ?? throw new InvalidDataException("Empty SVG.");
        if (root.Name.LocalName != "svg") throw new InvalidDataException("The file does not start with <svg>.");
        var skipped = new List<string>();
        var gradients = root.Descendants().Where(e => e.Name.LocalName is "linearGradient" or "radialGradient" && e.Attribute("id") is not null)
            .ToDictionary(e => (string)e.Attribute("id")!, e => e);

        double width = Len(root, "width", 0), height = Len(root, "height", 0);
        var view = ((string?)root.Attribute("viewBox"))?.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Select(v => double.Parse(v, Inv)).ToArray();
        var m = Matrix.Identity;
        if (view is [var vx, var vy, var vw, var vh] && vw > 0 && vh > 0)
        {
            if (width <= 0) width = vw;
            if (height <= 0) height = vh;
            m.Translate(-vx, -vy);
            m.Scale(width / vw, height / vh);
        }
        if (width <= 0 || height <= 0) (width, height) = (100, 100);
        var doc = new VectorDoc { Width = width, Height = height };
        foreach (var e in root.Elements()) Read(e, m, Style.Default, doc.Shapes, gradients, skipped);
        return new ImportResult(doc, skipped.Distinct().ToList());
    }

    sealed record Style(VPaintSpec? Fill, VPaintSpec? Stroke, double StrokeWidth, double Opacity, double FillOpacity, double StrokeOpacity, bool EvenOdd)
    {
        public static readonly Style Default = new(new VPaintSpec(0xFF000000, null), null, 1, 1, 1, 1, false);
    }

    /// <summary>A colour or a reference to a gradient (resolved against the shape's bounds when needed).</summary>
    sealed record VPaintSpec(uint Color, string? GradientId);

    static void Read(XElement e, Matrix parent, Style inherited, List<VShape> into, Dictionary<string, XElement> gradients, List<string> skipped)
    {
        string name = e.Name.LocalName;
        if (name is "defs" or "title" or "desc" or "metadata" or "linearGradient" or "radialGradient") return;
        if (name == "style") { skipped.Add("<style> sheets (use presentation attributes)"); return; }
        var m = parent;
        if ((string?)e.Attribute("transform") is string t) m = Transform(t) * parent;
        var st = StyleOf(e, inherited);
        if ((string?)e.Attribute("display") == "none" || (string?)e.Attribute("visibility") == "hidden") return;

        Geometry? g = null;
        switch (name)
        {
            case "g":
            case "a":
            {
                var group = new VShape { Kind = VShapeKind.Group, Name = (string?)e.Attribute("id") ?? "Group" };
                foreach (var c in e.Elements()) Read(c, m, st, group.Children, gradients, skipped);
                if (group.Children.Count > 0) into.Add(group.Children.Count == 1 ? group.Children[0] : group);
                return;
            }
            case "path":
                try { g = Geometry.Parse((string?)e.Attribute("d") ?? ""); }
                catch (FormatException) { skipped.Add("a path with unreadable data"); return; }
                break;
            case "rect":
                double rx = Len(e, "rx", Len(e, "ry", 0));
                g = new RectangleGeometry(new Rect(Len(e, "x", 0), Len(e, "y", 0), Len(e, "width", 0), Len(e, "height", 0)), rx, Len(e, "ry", rx));
                break;
            case "circle":
                double r = Len(e, "r", 0);
                g = new EllipseGeometry(new Point(Len(e, "cx", 0), Len(e, "cy", 0)), r, r);
                break;
            case "ellipse":
                g = new EllipseGeometry(new Point(Len(e, "cx", 0), Len(e, "cy", 0)), Len(e, "rx", 0), Len(e, "ry", 0));
                break;
            case "line":
                g = new LineGeometry(new Point(Len(e, "x1", 0), Len(e, "y1", 0)), new Point(Len(e, "x2", 0), Len(e, "y2", 0)));
                st = st with { Fill = null };
                break;
            case "polyline":
            case "polygon":
                var pts = Numbers((string?)e.Attribute("points") ?? "");
                if (pts.Length < 4) return;
                var fig = new PathFigure { StartPoint = new Point(pts[0], pts[1]), IsClosed = name == "polygon" };
                for (int i = 2; i + 1 < pts.Length; i += 2) fig.Segments.Add(new LineSegment(new Point(pts[i], pts[i + 1]), true));
                g = new PathGeometry([fig]);
                break;
            default:
                skipped.Add($"<{name}>");
                return;
        }
        // the transform goes into the points (an affine map of Bezier control points is exact)
        Point T(double x, double y) => m.Transform(new Point(x, y));
        var contours = VectorGeometry.Contours(g).Select(c => new VContour([.. c.Nodes.Select(n =>
        {
            Point p = T(n.X, n.Y), i = T(n.InX, n.InY), o = T(n.OutX, n.OutY);
            return new VNode(p.X, p.Y, i.X, i.Y, o.X, o.Y);
        })], c.Closed)).ToList();
        var shape = new VShape
        {
            Kind = VShapeKind.Path, Name = (string?)e.Attribute("id") ?? name, Contours = contours, EvenOdd = st.EvenOdd,
            StrokeWidth = st.StrokeWidth * Math.Sqrt(Math.Abs(m.Determinant)), Opacity = st.Opacity,
        };
        var bounds = VectorGeometry.Bounds(shape);
        shape.Fill = Paint(st.Fill, st.FillOpacity, bounds, m, gradients, skipped);
        shape.Stroke = Paint(st.Stroke, st.StrokeOpacity, bounds, m, gradients, skipped);
        into.Add(shape);
    }

    static Style StyleOf(XElement e, Style s)
    {
        var props = new Dictionary<string, string>();
        foreach (var a in e.Attributes()) props[a.Name.LocalName] = a.Value;
        if ((string?)e.Attribute("style") is string css)
            foreach (var decl in css.Split(';', StringSplitOptions.RemoveEmptyEntries))
                if (decl.Split(':', 2) is [var k, var v]) props[k.Trim()] = v.Trim();
        VPaintSpec? P(string key, VPaintSpec? current) => props.TryGetValue(key, out var v) ? v.Trim() switch
        {
            "none" or "transparent" => null,
            var u when u.StartsWith("url(") => new VPaintSpec(0, u[4..].TrimEnd(')').Trim('\'', '"').TrimStart('#')),
            var c => ParseColor(c) is uint col ? new VPaintSpec(col, null) : current,
        } : current;
        double D(string key, double current) => props.TryGetValue(key, out var v) && double.TryParse(v.Replace("px", ""), NumberStyles.Float, Inv, out double d) ? d : current;
        return new Style(P("fill", s.Fill), P("stroke", s.Stroke), D("stroke-width", s.StrokeWidth), s.Opacity * D("opacity", 1),
            D("fill-opacity", s.FillOpacity), D("stroke-opacity", s.StrokeOpacity), props.TryGetValue("fill-rule", out var fr) ? fr == "evenodd" : s.EvenOdd);
    }

    static VPaint? Paint(VPaintSpec? spec, double opacity, Rect bounds, Matrix m, Dictionary<string, XElement> gradients, List<string> skipped)
    {
        if (spec is null) return null;
        uint Fade(uint c, double o) => (uint)Math.Round((c >> 24) * Math.Clamp(o, 0, 1)) << 24 | c & 0xFFFFFF;
        if (spec.GradientId is null) return VPaint.Solid(Fade(spec.Color, opacity));
        if (!gradients.TryGetValue(spec.GradientId, out var gr)) { skipped.Add($"paint url(#{spec.GradientId}) that is not a gradient"); return VPaint.Solid(Fade(0xFF000000, opacity)); }
        // stops may come from a referenced gradient (xlink:href)
        var stopsFrom = gr;
        if (!gr.Elements().Any(x => x.Name.LocalName == "stop") && ((string?)gr.Attribute(XLink + "href") ?? (string?)gr.Attribute("href")) is string href && gradients.TryGetValue(href.TrimStart('#'), out var linked)) stopsFrom = linked;
        var stops = stopsFrom.Elements().Where(x => x.Name.LocalName == "stop").Select(x =>
        {
            double off = Fraction((string?)x.Attribute("offset") ?? "0");
            var props = x.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value);
            if ((string?)x.Attribute("style") is string css)
                foreach (var decl in css.Split(';', StringSplitOptions.RemoveEmptyEntries)) if (decl.Split(':', 2) is [var k, var v]) props[k.Trim()] = v.Trim();
            uint col = props.TryGetValue("stop-color", out var sc) && ParseColor(sc) is uint c ? c : 0xFF000000;
            double so = props.TryGetValue("stop-opacity", out var sop) && double.TryParse(sop, NumberStyles.Float, Inv, out var d) ? d : 1;
            return new VStop(Math.Clamp(off, 0, 1), Fade(col, so * opacity));
        }).ToList();
        if (stops.Count == 0) return null;
        bool user = (string?)gr.Attribute("gradientUnits") == "userSpaceOnUse";
        double A(string key, double fallback) => (string?)gr.Attribute(key) is string v ? Fraction(v) : fallback;
        Point Map(double x, double y) => user ? m.Transform(new Point(x, y)) : new Point(bounds.X + x * bounds.Width, bounds.Y + y * bounds.Height);
        if (gr.Name.LocalName == "linearGradient")
        {
            var p1 = Map(A("x1", 0), A("y1", 0));
            var p2 = Map(A("x2", user ? 0 : 1), A("y2", 0));
            return new VPaint(VPaintKind.Linear, stops[0].Color, stops, p1.X, p1.Y, p2.X, p2.Y);
        }
        var c0 = Map(A("cx", 0.5), A("cy", 0.5));
        double rr = A("r", 0.5) * (user ? Math.Sqrt(Math.Abs(m.Determinant)) : Math.Max(bounds.Width, bounds.Height));
        return new VPaint(VPaintKind.Radial, stops[0].Color, stops, c0.X, c0.Y, 0, 0, rr);
    }

    /// <summary>"50%" → 0.5, otherwise the number as written.</summary>
    static double Fraction(string v) => v.EndsWith('%') ? double.Parse(v[..^1], Inv) / 100 : double.Parse(v, Inv);

    static double Len(XElement e, string attr, double fallback)
    {
        if ((string?)e.Attribute(attr) is not string v) return fallback;
        v = v.Trim().Replace("px", "");
        return double.TryParse(v, NumberStyles.Float, Inv, out double d) ? d : fallback;
    }

    static double[] Numbers(string s) =>
        [.. Regex.Matches(s, @"[-+]?(\d+\.?\d*|\.\d+)([eE][-+]?\d+)?").Select(x => double.Parse(x.Value, Inv))];

    static Matrix Transform(string t)
    {
        var m = Matrix.Identity;
        foreach (Match f in Regex.Matches(t, @"(\w+)\s*\(([^)]*)\)"))
        {
            var a = Numbers(f.Groups[2].Value);
            var step = Matrix.Identity;
            switch (f.Groups[1].Value)
            {
                case "translate": step.Translate(a.ElementAtOrDefault(0), a.ElementAtOrDefault(1)); break;
                case "scale": step.Scale(a[0], a.Length > 1 ? a[1] : a[0]); break;
                case "rotate":
                    if (a.Length >= 3) step.RotateAt(a[0], a[1], a[2]); else step.Rotate(a[0]);
                    break;
                case "matrix" when a.Length == 6: step = new Matrix(a[0], a[1], a[2], a[3], a[4], a[5]); break;
                case "skewX": step.Skew(a[0], 0); break;
                case "skewY": step.Skew(0, a[0]); break;
            }
            m = step * m;   // the rightmost transform applies first
        }
        return m;
    }

    static readonly Dictionary<string, uint> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = 0xFF000000, ["white"] = 0xFFFFFFFF, ["red"] = 0xFFFF0000, ["green"] = 0xFF008000, ["blue"] = 0xFF0000FF, ["yellow"] = 0xFFFFFF00,
        ["orange"] = 0xFFFFA500, ["purple"] = 0xFF800080, ["gray"] = 0xFF808080, ["grey"] = 0xFF808080, ["silver"] = 0xFFC0C0C0, ["maroon"] = 0xFF800000,
        ["navy"] = 0xFF000080, ["teal"] = 0xFF008080, ["lime"] = 0xFF00FF00, ["aqua"] = 0xFF00FFFF, ["cyan"] = 0xFF00FFFF, ["fuchsia"] = 0xFFFF00FF,
        ["magenta"] = 0xFFFF00FF, ["olive"] = 0xFF808000, ["brown"] = 0xFFA52A2A, ["pink"] = 0xFFFFC0CB, ["gold"] = 0xFFFFD700,
    };

    static uint? ParseColor(string s)
    {
        s = s.Trim();
        if (Named.TryGetValue(s, out var n)) return n;
        if (s.StartsWith('#'))
        {
            string h = s[1..];
            if (h.Length == 3) h = string.Concat(h.Select(c => $"{c}{c}"));
            return h.Length == 6 && uint.TryParse(h, NumberStyles.HexNumber, Inv, out uint v) ? 0xFF000000 | v : null;
        }
        if (s.StartsWith("rgb"))
        {
            var a = Numbers(s);
            if (a.Length < 3) return null;
            bool pct = s.Contains('%');
            uint C(double v) => (uint)Math.Clamp(Math.Round(pct ? v * 2.55 : v), 0, 255);
            uint alpha = a.Length >= 4 ? (uint)Math.Clamp(Math.Round(a[3] * 255), 0, 255) : 255;
            return alpha << 24 | C(a[0]) << 16 | C(a[1]) << 8 | C(a[2]);
        }
        return null;
    }

    // ---------- export ----------

    public static string Export(VectorDoc d)
    {
        var defs = new StringBuilder();
        var body = new StringBuilder();
        int gid = 0;
        string F(double v) => Math.Round(v, 3).ToString(Inv);
        string Hex(uint c) => $"#{c & 0xFFFFFF:x6}";
        string Paint(VPaint? p, string attr)
        {
            if (p is null) return $" {attr}=\"none\"";
            if (p.Kind == VPaintKind.Solid) return $" {attr}=\"{Hex(p.Color)}\"" + (p.Color >> 24 < 255 ? $" {attr}-opacity=\"{F((p.Color >> 24) / 255.0)}\"" : "");
            string id = $"g{++gid}";
            string stops = string.Concat((p.Stops ?? []).Select(s => $"<stop offset=\"{F(s.Offset)}\" stop-color=\"{Hex(s.Color)}\"{(s.Color >> 24 < 255 ? $" stop-opacity=\"{F((s.Color >> 24) / 255.0)}\"" : "")}/>"));
            defs.Append(p.Kind == VPaintKind.Linear
                ? $"<linearGradient id=\"{id}\" gradientUnits=\"userSpaceOnUse\" x1=\"{F(p.X1)}\" y1=\"{F(p.Y1)}\" x2=\"{F(p.X2)}\" y2=\"{F(p.Y2)}\">{stops}</linearGradient>\n"
                : $"<radialGradient id=\"{id}\" gradientUnits=\"userSpaceOnUse\" cx=\"{F(p.X1)}\" cy=\"{F(p.Y1)}\" r=\"{F(p.R)}\">{stops}</radialGradient>\n");
            return $" {attr}=\"url(#{id})\"";
        }
        void Write(VShape s, string indent)
        {
            string common = Paint(s.Fill, "fill") + Paint(s.Stroke, "stroke") + (s.Stroke is not null ? $" stroke-width=\"{F(s.StrokeWidth)}\" stroke-linejoin=\"round\"" : "")
                            + (s.Opacity < 1 ? $" opacity=\"{F(s.Opacity)}\"" : "");
            switch (s.Kind)
            {
                case VShapeKind.Group:
                    body.Append($"{indent}<g{(s.Opacity < 1 ? $" opacity=\"{F(s.Opacity)}\"" : "")}>\n");
                    foreach (var c in s.Children) Write(c, indent + "  ");
                    body.Append($"{indent}</g>\n");
                    break;
                case VShapeKind.Rect:
                    body.Append($"{indent}<rect x=\"{F(s.X)}\" y=\"{F(s.Y)}\" width=\"{F(s.W)}\" height=\"{F(s.H)}\"{(s.Radius > 0 ? $" rx=\"{F(s.Radius)}\"" : "")}{common}/>\n");
                    break;
                case VShapeKind.Ellipse:
                    body.Append($"{indent}<ellipse cx=\"{F(s.X + s.W / 2)}\" cy=\"{F(s.Y + s.H / 2)}\" rx=\"{F(s.W / 2)}\" ry=\"{F(s.H / 2)}\"{common}/>\n");
                    break;
                default:
                    var dpath = new StringBuilder();
                    foreach (var c in s.Contours.Where(c => c.Nodes.Count > 0))
                    {
                        dpath.Append($"M{F(c.Nodes[0].X)} {F(c.Nodes[0].Y)}");
                        int n = c.Nodes.Count;
                        for (int i = 0; i < (c.Closed ? n : n - 1); i++)
                        {
                            var a = c.Nodes[i];
                            var b = c.Nodes[(i + 1) % n];
                            bool straight = (a.OutX, a.OutY) == (a.X, a.Y) && (b.InX, b.InY) == (b.X, b.Y);
                            dpath.Append(straight ? $"L{F(b.X)} {F(b.Y)}" : $"C{F(a.OutX)} {F(a.OutY)} {F(b.InX)} {F(b.InY)} {F(b.X)} {F(b.Y)}");
                        }
                        if (c.Closed) dpath.Append('Z');
                    }
                    body.Append($"{indent}<path d=\"{dpath}\"{(s.EvenOdd ? " fill-rule=\"evenodd\"" : "")}{common}/>\n");
                    break;
            }
        }
        foreach (var s in d.Shapes) Write(s, "  ");
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(d.Width)}\" height=\"{F(d.Height)}\" viewBox=\"0 0 {F(d.Width)} {F(d.Height)}\">\n"
               + (defs.Length > 0 ? $"<defs>\n{defs}</defs>\n" : "") + body + "</svg>\n";
    }
}
