namespace Pixelkiln.Core;

public enum EmitterPreset { Smoke, Fire, Rain, Snow, Sparks, Magic }

/// <summary>
/// A particle emitter: particles are born at Rate per second at a point (or anywhere in a W×H area), fly in a
/// direction with spread, fall with gravity, and change size and colour over their life.
/// </summary>
public sealed class SceneEmitter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "emitter";
    public EmitterPreset Preset { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    /// <summary>Spawn area from X, Y (0 = a point).</summary>
    public int W { get; set; }
    public int H { get; set; }
    public double Rate { get; set; } = 20;
    public int LifeMs { get; set; } = 1500;
    /// <summary>Pixels per second; direction in degrees (0 = right, -90 = up) and full spread angle.</summary>
    public double Speed { get; set; } = 30;
    public double Direction { get; set; } = -90;
    public double Spread { get; set; } = 30;
    /// <summary>Pixels per second² downwards (negative rises).</summary>
    public double Gravity { get; set; }
    public double Size { get; set; } = 2;
    public double EndSize { get; set; } = 1;
    public uint Color { get; set; } = 0xFFFFFFFF;
    /// <summary>Colour at the end of life (its alpha fades the particle out).</summary>
    public uint EndColor { get; set; } = 0x00FFFFFF;
    /// <summary>Light-like particles (fire, sparks, magic) add their colour and stay bright in the dark.</summary>
    public bool Additive { get; set; }
    public int Seed { get; set; } = 1;
    public SceneEmitter Clone() => (SceneEmitter)MemberwiseClone();

    public static SceneEmitter FromPreset(EmitterPreset p) => p switch
    {
        EmitterPreset.Smoke => new() { Preset = p, Name = "smoke", Rate = 12, LifeMs = 2500, Speed = 14, Spread = 40, Gravity = -4, Size = 2, EndSize = 6, Color = 0xB0808080, EndColor = 0x00606060 },
        EmitterPreset.Fire => new() { Preset = p, Name = "fire", Rate = 40, LifeMs = 700, Speed = 28, Spread = 35, Gravity = -20, Size = 3, EndSize = 1, Color = 0xFFFFD040, EndColor = 0x00FF3000, Additive = true },
        EmitterPreset.Rain => new() { Preset = p, Name = "rain", W = 320, Rate = 120, LifeMs = 900, Speed = 220, Direction = 100, Spread = 4, Size = 1, EndSize = 1, Color = 0xC0A0C0FF, EndColor = 0x80A0C0FF },
        EmitterPreset.Snow => new() { Preset = p, Name = "snow", W = 320, Rate = 40, LifeMs = 5000, Speed = 22, Direction = 95, Spread = 30, Size = 2, EndSize = 1, Color = 0xFFFFFFFF, EndColor = 0x80FFFFFF },
        EmitterPreset.Sparks => new() { Preset = p, Name = "sparks", Rate = 30, LifeMs = 600, Speed = 90, Spread = 120, Gravity = 260, Size = 1, EndSize = 1, Color = 0xFFFFF0A0, EndColor = 0x00FF8000, Additive = true },
        _ => new() { Preset = p, Name = "magic", W = 16, H = 16, Rate = 25, LifeMs = 1200, Speed = 10, Spread = 360, Gravity = -12, Size = 2, EndSize = 0, Color = 0xFFC080FF, EndColor = 0x004080FF, Additive = true },
    };
}

public readonly record struct Particle(double X, double Y, double Size, uint Color);

/// <summary>
/// Stateless, seeded particles: each particle's state is a formula of its index and age, so any moment can be
/// drawn directly (scrubbing, the game preview and exports agree) and the result never depends on frame rate.
/// </summary>
public static class Particles
{
    /// <summary>Most particles one emitter shows at a time (the oldest are dropped).</summary>
    public const int MaxAlive = 4000;

    public static List<Particle> At(SceneEmitter e, double timeMs)
    {
        var o = new List<Particle>();
        if (e.Rate <= 0 || e.LifeMs <= 0) return o;
        double t = timeMs + e.LifeMs;   // pre-warmed: rain is already falling when the scene starts
        double interval = 1000 / e.Rate;
        long last = (long)Math.Floor(t / interval), first = Math.Max(Math.Max(0, (long)Math.Floor((t - e.LifeMs) / interval) + 1), last - MaxAlive + 1);
        for (long k = first; k <= last; k++)
        {
            double age = t - k * interval;
            if (age < 0 || age >= e.LifeMs) continue;
            ulong h = Rng.Hash((ulong)e.Seed * 0x9E3779B97F4A7C15UL ^ (ulong)k);
            double R() { h = Rng.Hash(h); return (h >> 11) / (double)(1UL << 53); }
            double px = e.X + R() * e.W, py = e.Y + R() * e.H;
            double a = (e.Direction + (R() - 0.5) * e.Spread) * Math.PI / 180, v = e.Speed * (0.6 + 0.4 * R());
            double s = age / 1000, u = age / e.LifeMs;
            o.Add(new Particle(px + Math.Cos(a) * v * s, py + Math.Sin(a) * v * s + 0.5 * e.Gravity * s * s, e.Size + (e.EndSize - e.Size) * u, Lerp(e.Color, e.EndColor, u)));
        }
        return o;
    }

    static uint Lerp(uint a, uint b, double u)
    {
        uint C(int shift) => (uint)Math.Round((a >> shift & 0xFF) + ((double)(b >> shift & 0xFF) - (a >> shift & 0xFF)) * u) << shift;
        return C(24) | C(16) | C(8) | C(0);
    }

    /// <summary>Draws one emitter's particles as squares into the view (alpha-over, or added for additive emitters).</summary>
    public static void Draw(uint[] o, int w, int h, int camX, int camY, SceneEmitter e, double timeMs)
    {
        foreach (var p in At(e, timeMs))
        {
            int size = Math.Max(0, (int)Math.Round(p.Size));
            uint alpha = p.Color >> 24;
            if (size == 0 || alpha == 0) continue;
            int x0 = (int)Math.Round(p.X - size / 2.0) - camX, y0 = (int)Math.Round(p.Y - size / 2.0) - camY;
            for (int y = Math.Max(0, y0); y < Math.Min(h, y0 + size); y++)
                for (int x = Math.Max(0, x0); x < Math.Min(w, x0 + size); x++)
                {
                    int i = y * w + x;
                    uint d = o[i];
                    uint Ch(int shift) => e.Additive
                        ? Math.Min(255u, (d >> shift & 0xFF) + (p.Color >> shift & 0xFF) * alpha / 255) << shift
                        : ((p.Color >> shift & 0xFF) * alpha + (d >> shift & 0xFF) * (255 - alpha)) / 255 << shift;
                    o[i] = 0xFF000000 | Ch(16) | Ch(8) | Ch(0);
                }
        }
    }
}
