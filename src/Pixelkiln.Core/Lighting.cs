namespace Pixelkiln.Core;

public enum LightKind { Point, Spot }

/// <summary>A 2D light: colour and strength falling off to zero at Radius; a spot only lights its cone.</summary>
public sealed class SceneLight
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "light";
    public LightKind Kind { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Radius { get; set; } = 96;
    public uint Color { get; set; } = 0xFFFFE0B0;
    public double Intensity { get; set; } = 1;
    /// <summary>Spot direction in degrees (0 = right, 90 = down) and full cone angle.</summary>
    public double Direction { get; set; } = 90;
    public double Cone { get; set; } = 60;
    /// <summary>Solid tiles and occluder polygons cast shadows from this light.</summary>
    public bool Shadows { get; set; } = true;
    /// <summary>0–1: a halo added on top (torches, lamps), also over dark areas.</summary>
    public double Glow { get; set; }
    /// <summary>0–1: deterministic flicker (fire).</summary>
    public double Flicker { get; set; }
    public SceneLight Clone() => (SceneLight)MemberwiseClone();

    /// <summary>Ready-made lights offered when adding one.</summary>
    public static readonly IReadOnlyDictionary<string, Func<SceneLight>> Presets = new Dictionary<string, Func<SceneLight>>
    {
        ["Torch"] = () => new() { Name = "torch", Radius = 80, Color = 0xFFFFA040, Intensity = 1.2, Glow = 0.35, Flicker = 0.6 },
        ["Lamp"] = () => new() { Name = "lamp", Radius = 120, Color = 0xFFFFF0C8, Intensity = 1 },
        ["Spotlight"] = () => new() { Name = "spot", Kind = LightKind.Spot, Radius = 160, Color = 0xFFFFFFFF, Intensity = 1.3, Direction = 90, Cone = 45 },
        ["Moonlight"] = () => new() { Name = "moon", Radius = 320, Color = 0xFF8090FF, Intensity = 0.6, Shadows = false },
        ["Magic"] = () => new() { Name = "magic", Radius = 64, Color = 0xFFB060FF, Intensity = 1.4, Glow = 0.5, Flicker = 0.3 },
    };
}

/// <summary>A polygon that blocks light (Points = x0, y0, x1, y1 … in scene pixels).</summary>
public sealed class SceneOccluder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "occluder";
    public List<int> Points { get; set; } = [];
    public SceneOccluder Clone() => new() { Id = Id, Name = Name, Points = [.. Points] };

    public static SceneOccluder Rect(int x, int y, int w, int h) => new() { Points = [x, y, x + w, y, x + w, y + h, x, y + h] };
}

/// <summary>
/// Software 2D lighting for the view: an ambient colour, lights with falloff and cones, shadows by ray casting
/// against solid tiles and occluder polygons, and additive glow. Multiplies the picture in place.
/// </summary>
public static class Lighting
{
    public static bool IsOn(Scene s) => s.Lights.Count > 0 || s.Ambient != 0xFFFFFFFF;

    public static void Apply(uint[] o, int w, int h, int camX, int camY, Scene s, IEnumerable<Collider> solids, double timeMs)
    {
        if (!IsOn(s)) return;
        int n = w * h;
        var lr = new float[n]; var lg = new float[n]; var lb = new float[n];
        float ar = (s.Ambient >> 16 & 0xFF) / 255f, ag = (s.Ambient >> 8 & 0xFF) / 255f, ab = (s.Ambient & 0xFF) / 255f;
        Array.Fill(lr, ar); Array.Fill(lg, ag); Array.Fill(lb, ab);
        float[]? gr = null, gg = null, gb = null;

        var lights = s.Lights.Where(l => l.Radius > 0 && l.Intensity > 0 && l.X + l.Radius > camX && l.X - l.Radius < camX + w && l.Y + l.Radius > camY && l.Y - l.Radius < camY + h).ToList();
        Grid? grid = lights.Any(l => l.Shadows) ? new Grid(camX, camY, w, h, Math.Min(512, lights.Max(l => l.Radius)), s.TileShadows ? solids : [], s.Occluders) : null;
        foreach (var l in lights)
        {
            double f = l.Intensity * (1 - l.Flicker * 0.35 * (0.5 + 0.5 * Math.Sin(timeMs * 0.017 + l.X) * Math.Sin(timeMs * 0.0071 + l.Y)));
            float cr = (float)((l.Color >> 16 & 0xFF) / 255.0 * f), cg = (float)((l.Color >> 8 & 0xFF) / 255.0 * f), cb = (float)((l.Color & 0xFF) / 255.0 * f);
            var reach = l.Shadows && grid is not null ? grid.Rays(l.X, l.Y, l.Radius, out int rays) : null;
            int rayCount = reach?.Length ?? 0;
            double half = l.Cone / 2, dir = l.Direction;
            int x0 = Math.Max(0, l.X - l.Radius - camX), x1 = Math.Min(w, l.X + l.Radius - camX + 1);
            int y0 = Math.Max(0, l.Y - l.Radius - camY), y1 = Math.Min(h, l.Y + l.Radius - camY + 1);
            if (l.Glow > 0 && gr is null) (gr, gg, gb) = (new float[n], new float[n], new float[n]);
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    double dx = x + camX + 0.5 - l.X, dy = y + camY + 0.5 - l.Y, d = Math.Sqrt(dx * dx + dy * dy);
                    if (d >= l.Radius) continue;
                    double a = Math.Atan2(dy, dx);
                    if (reach is not null && d > reach[(int)((a + Math.PI) / (2 * Math.PI) * rayCount) % rayCount] + 1) continue;
                    double k = 1 - d / l.Radius;
                    k *= k;
                    if (l.Kind == LightKind.Spot)
                    {
                        double diff = Math.Abs(((a * 180 / Math.PI - dir) % 360 + 540) % 360 - 180);
                        k *= Math.Clamp((half - diff) / Math.Max(1, half * 0.25), 0, 1);   // soft cone edge
                        if (k <= 0) continue;
                    }
                    int i = y * w + x;
                    lr[i] += (float)(cr * k); lg[i] += (float)(cg * k); lb[i] += (float)(cb * k);
                    if (l.Glow > 0) { float gk = (float)(l.Glow * k * k); gr![i] += cr * gk; gg![i] += cg * gk; gb![i] += cb * gk; }
                }
        }
        for (int i = 0; i < n; i++)
        {
            uint p = o[i];
            static uint C(double v) => (uint)Math.Clamp((int)(v + 0.5), 0, 255);
            double r = (p >> 16 & 0xFF) * Math.Min(lr[i], 2f), g = (p >> 8 & 0xFF) * Math.Min(lg[i], 2f), b = (p & 0xFF) * Math.Min(lb[i], 2f);
            if (gr is not null) { r += gr[i] * 255; g += gg![i] * 255; b += gb![i] * 255; }
            o[i] = (p & 0xFF000000) | C(r) << 16 | C(g) << 8 | C(b);
        }
    }

    /// <summary>Blocked cells around the view (one per pixel), with a margin so lights just off screen still cast shadows in.</summary>
    sealed class Grid
    {
        readonly int _ox, _oy, _w, _h;
        readonly bool[] _solid;

        public Grid(int camX, int camY, int w, int h, int margin, IEnumerable<Collider> solids, IEnumerable<SceneOccluder> polys)
        {
            (_ox, _oy, _w, _h) = (camX - margin, camY - margin, w + 2 * margin, h + 2 * margin);
            _solid = new bool[_w * _h];
            foreach (var c in solids.Where(c => !c.OneWay)) Fill(c.X, c.Y, c.W, c.H, null);
            foreach (var p in polys.Where(p => p.Points.Count >= 6))
            {
                var xs = p.Points.Where((_, i) => i % 2 == 0).ToArray();
                var ys = p.Points.Where((_, i) => i % 2 == 1).ToArray();
                Fill(xs.Min(), ys.Min(), xs.Max() - xs.Min(), ys.Max() - ys.Min(), (x, y) => Inside(xs, ys, x, y));
            }
        }

        void Fill(int x, int y, int w, int h, Func<double, double, bool>? test)
        {
            int ax = Math.Max(0, x - _ox), ay = Math.Max(0, y - _oy), bx = Math.Min(_w, x + w - _ox), by = Math.Min(_h, y + h - _oy);
            for (int gy = ay; gy < by; gy++)
                for (int gx = ax; gx < bx; gx++)
                    if (test is null || test(gx + _ox + 0.5, gy + _oy + 0.5)) _solid[gy * _w + gx] = true;
        }

        static bool Inside(int[] xs, int[] ys, double x, double y)
        {
            bool inside = false;
            for (int i = 0, j = xs.Length - 1; i < xs.Length; j = i++)
                if ((ys[i] > y) != (ys[j] > y) && x < (xs[j] - xs[i]) * (y - ys[i]) / (double)(ys[j] - ys[i]) + xs[i]) inside = !inside;
            return inside;
        }

        bool Blocked(double x, double y)
        {
            int gx = (int)Math.Floor(x) - _ox, gy = (int)Math.Floor(y) - _oy;
            return gx >= 0 && gy >= 0 && gx < _w && gy < _h && _solid[gy * _w + gx];
        }

        /// <summary>How far light travels along each of N rays (N grows with the radius). A light inside a wall first leaves it.</summary>
        public double[] Rays(int lx, int ly, int radius, out int count)
        {
            count = Math.Clamp((int)Math.Ceiling(2 * Math.PI * radius), 64, 2048);
            var reach = new double[count];
            for (int r = 0; r < count; r++)
            {
                double a = (r + 0.5) / count * 2 * Math.PI - Math.PI, dx = Math.Cos(a), dy = Math.Sin(a);
                double d = 0;
                while (d < radius && Blocked(lx + 0.5 + dx * d, ly + 0.5 + dy * d)) d += 1;   // start inside a wall: skip it
                while (d < radius && !Blocked(lx + 0.5 + dx * d, ly + 0.5 + dy * d)) d += 1;
                reach[r] = d;
            }
            return reach;
        }
    }
}
