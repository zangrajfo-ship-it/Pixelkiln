namespace Pixelkiln.Core;

public enum Easing { Linear, EaseIn, EaseOut, EaseInOut, Step }

/// <summary>Pose of an animated scene object at one moment.</summary>
public sealed record Keyframe(int TimeMs, double X = 0, double Y = 0, double ScaleX = 1, double ScaleY = 1, double Rotation = 0, double Opacity = 1,
    Easing Easing = Easing.Linear);

/// <summary>
/// Keyframe animation for scene objects (position, scale, rotation, opacity). Pixel art itself is animated
/// frame by frame in the sprite editor; tracks move whole objects (platforms, doors, camera targets).
/// </summary>
public sealed class KeyframeTrack
{
    public List<Keyframe> Keys { get; set; } = [];
    public bool Loop { get; set; } = true;

    public int DurationMs => Keys.Count == 0 ? 0 : Keys.Max(k => k.TimeMs);

    /// <summary>Pose at time t; between keys the easing of the earlier key shapes the blend.</summary>
    public Keyframe Sample(double t)
    {
        if (Keys.Count == 0) return new Keyframe(0);
        var keys = Keys.OrderBy(k => k.TimeMs).ToList();
        int dur = keys[^1].TimeMs;
        if (Loop && dur > 0) t = (t % dur + dur) % dur;
        if (t <= keys[0].TimeMs) return keys[0] with { TimeMs = (int)t };
        if (t >= dur) return keys[^1] with { TimeMs = (int)t };
        int i = keys.FindLastIndex(k => k.TimeMs <= t);
        Keyframe a = keys[i], b = keys[i + 1];
        double u = Ease(a.Easing, (t - a.TimeMs) / Math.Max(1, b.TimeMs - a.TimeMs));
        double L(double x, double y) => x + (y - x) * u;
        return new Keyframe((int)t, L(a.X, b.X), L(a.Y, b.Y), L(a.ScaleX, b.ScaleX), L(a.ScaleY, b.ScaleY), L(a.Rotation, b.Rotation), L(a.Opacity, b.Opacity), a.Easing);
    }

    static double Ease(Easing e, double u) => e switch
    {
        Easing.EaseIn => u * u,
        Easing.EaseOut => 1 - (1 - u) * (1 - u),
        Easing.EaseInOut => u < 0.5 ? 2 * u * u : 1 - Math.Pow(-2 * u + 2, 2) / 2,
        Easing.Step => 0,
        _ => u,
    };

    public KeyframeTrack Clone() => new() { Keys = [.. Keys], Loop = Loop };
}
