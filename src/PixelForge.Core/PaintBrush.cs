namespace PixelForge.Core;

public enum PaintTool { Brush, Pencil, Eraser, Airbrush, Smudge }

/// <summary>Brush settings. Size in pixels; the other values 0–1. Pressure can drive size and opacity.</summary>
public sealed record BrushSettings(
    PaintTool Tool = PaintTool.Brush, double Size = 12, double Hardness = 0.7, double Spacing = 0.15,
    double Opacity = 1, double Flow = 1, double Grain = 0, bool PressureSize = true, bool PressureOpacity = false,
    int Smoothing = 2, bool Square = false, bool Wrap = false);

/// <summary>
/// One stroke on one layer (or its mask). Dabs are placed along the path at the brush spacing. A coverage buffer per
/// stroke keeps overlapping dabs from building up past the opacity (except airbrush and smudge, which accumulate),
/// and the tiles' state before the stroke is kept for undo.
/// </summary>
public sealed class PaintStroke
{
    readonly PaintDoc _doc;
    readonly PaintLayer _layer;
    readonly BrushSettings _b;
    readonly uint _color;
    readonly bool _mask;
    readonly Dictionary<long, uint[]?> _before = [];
    readonly Dictionary<long, byte[]?> _maskBefore = [];
    readonly Dictionary<long, float[]> _coverage = [];
    readonly Queue<(double X, double Y, double P)> _smooth = new();
    (double X, double Y, double P)? _last;
    double _carry;   // distance walked since the last dab
    uint _smudge;    // colour carried by the smudge tool
    readonly Rng _grain = new(7);

    /// <summary>Canvas area changed since the last call to TakeDirty (for redrawing only that part).</summary>
    (int X0, int Y0, int X1, int Y1)? _dirty;

    public PaintStroke(PaintDoc doc, PaintLayer layer, BrushSettings b, uint color, bool mask = false)
    {
        (_doc, _layer, _b, _color, _mask) = (doc, layer, b, color, mask);
        if (mask) _layer.Mask ??= [];
    }

    public (int X, int Y, int W, int H)? TakeDirty()
    {
        if (_dirty is not var (x0, y0, x1, y1)) return null;
        _dirty = null;
        x0 = Math.Max(0, x0); y0 = Math.Max(0, y0); x1 = Math.Min(_doc.Width, x1); y1 = Math.Min(_doc.Height, y1);
        return x1 > x0 && y1 > y0 ? (x0, y0, x1 - x0, y1 - y0) : null;
    }

    /// <summary>Next input point (pressure 0–1; mouse = 1). Smoothing averages the last few points.</summary>
    public void To(double x, double y, double pressure = 1)
    {
        _smooth.Enqueue((x, y, pressure));
        while (_smooth.Count > Math.Max(1, _b.Smoothing + 1)) _smooth.Dequeue();
        var p = (_smooth.Average(q => q.X), _smooth.Average(q => q.Y), _smooth.Average(q => q.P));
        if (_last is not var (lx, ly, lp)) { Dab(p.Item1, p.Item2, p.Item3); _last = p; return; }
        double dx = p.Item1 - lx, dy = p.Item2 - ly, dist = Math.Sqrt(dx * dx + dy * dy);
        double step = Math.Max(0.5, SizeAt(p.Item3) * _b.Spacing);
        double t = step - _carry;
        for (; t <= dist; t += step)
        {
            double f = t / dist;
            Dab(lx + dx * f, ly + dy * f, lp + (p.Item3 - lp) * f);
        }
        _carry = dist - (t - step);
        _last = p;
    }

    /// <summary>Airbrush keeps spraying while the pen rests: one more dab at the current point.</summary>
    public void Hold()
    {
        if (_last is var (x, y, pr) && _b.Tool == PaintTool.Airbrush) Dab(x, y, pr);
    }

    double SizeAt(double pressure) => Math.Max(1, _b.PressureSize ? _b.Size * (0.15 + 0.85 * pressure) : _b.Size);

    /// <summary>One dab; with Wrap it is repeated across the opposite edges, so painted textures tile.</summary>
    void Dab(double cx, double cy, double pressure)
    {
        if (!_b.Wrap) { DabAt(cx, cy, pressure); return; }
        double r = SizeAt(pressure) / 2 + 1;
        foreach (int ox in new[] { 0, -_doc.Width, _doc.Width })
            foreach (int oy in new[] { 0, -_doc.Height, _doc.Height })
            {
                double x = cx + ox, y = cy + oy;
                if (x + r < 0 || y + r < 0 || x - r > _doc.Width || y - r > _doc.Height) continue;
                DabAt(x, y, pressure);
            }
    }

    void DabAt(double cx, double cy, double pressure)
    {
        double size = SizeAt(pressure), r = size / 2;
        double alpha = _b.Opacity * (_b.PressureOpacity ? pressure : 1);
        bool pencil = _b.Tool == PaintTool.Pencil;
        double hard = pencil ? 1 : Math.Clamp(_b.Hardness, 0, 1);
        int x0 = (int)Math.Floor(cx - r - 1), x1 = (int)Math.Ceiling(cx + r + 1), y0 = (int)Math.Floor(cy - r - 1), y1 = (int)Math.Ceiling(cy + r + 1);
        x0 = Math.Max(0, x0); y0 = Math.Max(0, y0); x1 = Math.Min(_doc.Width, x1); y1 = Math.Min(_doc.Height, y1);
        if (x1 <= x0 || y1 <= y0) return;
        _dirty = _dirty is var (a0, b0, a1, b1) ? (Math.Min(a0, x0), Math.Min(b0, y0), Math.Max(a1, x1), Math.Max(b1, y1)) : (x0, y0, x1, y1);
        if (_b.Tool == PaintTool.Smudge && _smudge == 0 && !_mask) _smudge = _layer.Get((int)Math.Clamp(cx, 0, _doc.Width - 1), (int)Math.Clamp(cy, 0, _doc.Height - 1));
        double feather = Math.Max(0.5, r * (1 - hard));
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                double dx = x + 0.5 - cx, dy = y + 0.5 - cy;
                double d = _b.Square ? Math.Max(Math.Abs(dx), Math.Abs(dy)) : Math.Sqrt(dx * dx + dy * dy);
                // pencil: hard pixel edge; brush: anti-aliased edge that softens towards the rim with lower hardness
                double m = pencil ? (d <= r ? 1 : 0) : Math.Clamp((r - d) / feather + (hard >= 1 ? 0.5 : 0), 0, 1);
                if (m <= 0) continue;
                if (_b.Grain > 0) m *= 1 - _b.Grain * _grain.NextDouble();
                Apply(x, y, m, alpha);
            }
        if (_b.Tool == PaintTool.Smudge && !_mask)   // pick up a little of the colour under the dab for the next one
        {
            uint under = _layer.Get((int)Math.Clamp(cx, 0, _doc.Width - 1), (int)Math.Clamp(cy, 0, _doc.Height - 1));
            _smudge = ColorUtil.Lerp(_smudge, under, 0.25);
        }
    }

    void Apply(int x, int y, double m, double alpha)
    {
        int tx = x / PaintLayer.Tile, ty = y / PaintLayer.Tile, i = y % PaintLayer.Tile * PaintLayer.Tile + x % PaintLayer.Tile;
        long k = PaintLayer.Key(tx, ty);
        if (!_coverage.TryGetValue(k, out var cov)) _coverage[k] = cov = new float[PaintLayer.Tile * PaintLayer.Tile];
        // coverage of this stroke so far: max for brush/pencil/eraser, accumulating for airbrush and smudge
        double before = cov[i];
        double now = _b.Tool is PaintTool.Airbrush or PaintTool.Smudge ? before + m * _b.Flow * (1 - before) : Math.Max(before, m * _b.Flow);
        if (now <= before) return;
        cov[i] = (float)now;

        if (_mask)
        {
            Remember(k, mask: true);
            var mt = _layer.Mask!.TryGetValue(k, out var t) ? t : _layer.Mask[k] = Filled255();
            byte orig = _maskBefore[k]?[i] ?? 255;
            double target = _b.Tool == PaintTool.Eraser ? 255 : 0;   // painting hides, erasing reveals
            mt[i] = (byte)Math.Round(orig + (target - orig) * now * alpha);
            return;
        }
        Remember(k, mask: false);
        var tile = _layer.TileFor(tx, ty);
        uint o = _before[k]?[i] ?? 0;   // the pixel as it was before this stroke
        uint result;
        if (_b.Tool == PaintTool.Eraser)
            result = (uint)Math.Round((o >> 24) * (1 - now * alpha)) << 24 | o & 0x00FFFFFF;
        else if (_b.Tool == PaintTool.Smudge)
            result = ColorUtil.Lerp(tile[i], _smudge, m * _b.Flow * alpha);
        else
        {
            result = Blending.Over(o, _color | 0xFF000000, now * alpha * (_color >> 24) / 255.0);
        }
        if (_layer.AlphaLocked) result = o >> 24 == 0 ? o : o & 0xFF000000 | result & 0x00FFFFFF;   // locked: only where paint already is
        tile[i] = result;
    }

    static byte[] Filled255() { var b = new byte[PaintLayer.Tile * PaintLayer.Tile]; Array.Fill(b, (byte)255); return b; }

    void Remember(long k, bool mask)
    {
        if (mask) { if (!_maskBefore.ContainsKey(k)) _maskBefore[k] = _layer.Mask!.TryGetValue(k, out var t) ? (byte[])t.Clone() : null; }
        else if (!_before.ContainsKey(k)) _before[k] = _layer.Tiles.TryGetValue(k, out var t) ? (uint[])t.Clone() : null;
    }

    /// <summary>Undo record for this stroke (tiles as they were before it).</summary>
    public PaintUndo.Step Finish() => new PaintUndo.Tiles(_layer, _before, _maskBefore);
}

/// <summary>
/// Undo for paintings: strokes keep only the tiles they touched, layer changes keep the layer list and settings.
/// Every step can undo itself and returns the step that redoes it. Steps share a memory budget like History&lt;T&gt;.
/// </summary>
public sealed class PaintUndo(int maxSteps = 100, long maxBytes = History.DefaultBudget)
{
    public abstract class Step
    {
        public abstract long Bytes { get; }
        /// <summary>Restores the saved state and returns the step that would restore the current one.</summary>
        public abstract Step Apply(PaintDoc doc);
    }

    public sealed class Tiles(PaintLayer layer, Dictionary<long, uint[]?> tiles, Dictionary<long, byte[]?> masks) : Step
    {
        public override long Bytes => tiles.Values.Sum(t => (long)(t?.Length ?? 0) * 4) + masks.Values.Sum(m => (long)(m?.Length ?? 0));

        public override Step Apply(PaintDoc doc)
        {
            var nowTiles = tiles.Keys.ToDictionary(k => k, k => layer.Tiles.TryGetValue(k, out var t) ? t : null);
            foreach (var (k, t) in tiles)
                if (t is null) layer.Tiles.Remove(k);
                else layer.Tiles[k] = t;
            var nowMasks = masks.Keys.ToDictionary(k => k, k => layer.Mask?.TryGetValue(k, out var m) == true ? m : null);
            foreach (var (k, m) in masks)
            {
                if (m is null) layer.Mask?.Remove(k);
                else (layer.Mask ??= [])[k] = m;
            }
            return new Tiles(layer, nowTiles, nowMasks);
        }
    }

    /// <summary>Layer list and every layer's settings (tiles are shared, so this step is small).</summary>
    public sealed class Layers(PaintDoc doc) : Step
    {
        readonly List<(PaintLayer Layer, PaintLayer Settings)> _state = [.. doc.Layers.Select(l => (l, l.Shallow()))];
        public override long Bytes => _state.Count * 64;

        public override Step Apply(PaintDoc d)
        {
            var redo = new Layers(d);
            d.Layers = [.. _state.Select(s => s.Layer)];
            foreach (var (l, st) in _state)
                (l.Name, l.Visible, l.Opacity, l.Blend, l.AlphaLocked, l.Mask) = (st.Name, st.Visible, st.Opacity, st.Blend, st.AlphaLocked, st.Mask);
            return redo;
        }
    }

    readonly List<Step> _undo = [], _redo = [];
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public long Bytes => _undo.Concat(_redo).Sum(s => s.Bytes);

    public void Push(Step s)
    {
        _undo.Add(s);
        _redo.Clear();
        long total = _undo.Sum(x => x.Bytes);   // kept up to date while trimming, not summed again per step
        while (_undo.Count > 1 && (_undo.Count > maxSteps || total > maxBytes))
        {
            total -= _undo[0].Bytes;
            _undo.RemoveAt(0);
        }
    }

    public bool Undo(PaintDoc d) => Swap(_undo, _redo, d);
    public bool Redo(PaintDoc d) => Swap(_redo, _undo, d);
    public void Clear() { _undo.Clear(); _redo.Clear(); }

    static bool Swap(List<Step> from, List<Step> to, PaintDoc d)
    {
        if (from.Count == 0) return false;
        var s = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(s.Apply(d));
        return true;
    }
}

public static class PaintOps
{
    /// <summary>Linear gradient from a to b along the drag (x0,y0)→(x1,y1) over the whole layer (undoable via the returned step).</summary>
    public static PaintUndo.Step Gradient(PaintDoc d, PaintLayer l, double x0, double y0, double x1, double y1, uint a, uint b)
    {
        var before = new Dictionary<long, uint[]?>();
        double dx = x1 - x0, dy = y1 - y0, len2 = Math.Max(1e-9, dx * dx + dy * dy);
        for (int ty = 0; ty < d.TilesY; ty++)
            for (int tx = 0; tx < d.TilesX; tx++)
            {
                long k = PaintLayer.Key(tx, ty);
                before[k] = l.Tiles.TryGetValue(k, out var t) ? (uint[])t.Clone() : null;
                var tile = l.TileFor(tx, ty);
                for (int y = 0; y < PaintLayer.Tile; y++)
                    for (int x = 0; x < PaintLayer.Tile; x++)
                    {
                        int gx = tx * PaintLayer.Tile + x, gy = ty * PaintLayer.Tile + y;
                        if (gx >= d.Width || gy >= d.Height) continue;
                        double f = Math.Clamp(((gx + 0.5 - x0) * dx + (gy + 0.5 - y0) * dy) / len2, 0, 1);
                        uint c = ColorUtil.Lerp(a, b, f);
                        int i = y * PaintLayer.Tile + x;
                        tile[i] = l.AlphaLocked ? tile[i] & 0xFF000000 | c & 0x00FFFFFF : Blending.Over(tile[i], c | 0xFF000000, (c >> 24) / 255.0);
                    }
            }
        return new PaintUndo.Tiles(l, before, []);
    }

    /// <summary>Area-average downscale of the whole painting (for sending it to the pixel editor).</summary>
    public static uint[] Downscale(PaintDoc d, int w, int h)
    {
        var src = d.Flatten();
        var o = new uint[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sx0 = x * d.Width / w, sx1 = Math.Max(sx0 + 1, (x + 1) * d.Width / w), sy0 = y * d.Height / h, sy1 = Math.Max(sy0 + 1, (y + 1) * d.Height / h);
                double a = 0, r = 0, g = 0, b = 0;
                int n = 0;
                for (int sy = sy0; sy < sy1; sy++)
                    for (int sx = sx0; sx < sx1; sx++, n++)
                    {
                        uint p = src[sy * d.Width + sx];
                        double pa = (p >> 24) / 255.0;
                        a += pa; r += (p >> 16 & 0xFF) * pa; g += (p >> 8 & 0xFF) * pa; b += (p & 0xFF) * pa;   // weight colour by alpha
                    }
                o[y * w + x] = a <= 0 ? 0 : (uint)Math.Round(a / n * 255) << 24 | (uint)Math.Round(r / a) << 16 | (uint)Math.Round(g / a) << 8 | (uint)Math.Round(b / a);
            }
        return o;
    }

    /// <summary>A picture as a new layer, placed at the top-left (cropped to the canvas).</summary>
    public static PaintLayer LayerFrom(PaintDoc d, uint[] px, int w, int h, string name)
    {
        var l = new PaintLayer { Name = name };
        for (int y = 0; y < Math.Min(h, d.Height); y++)
            for (int x = 0; x < Math.Min(w, d.Width); x++)
            {
                uint p = px[y * w + x];
                if (p >> 24 == 0) continue;
                l.TileFor(x / PaintLayer.Tile, y / PaintLayer.Tile)[y % PaintLayer.Tile * PaintLayer.Tile + x % PaintLayer.Tile] = p;
            }
        return l;
    }
}
