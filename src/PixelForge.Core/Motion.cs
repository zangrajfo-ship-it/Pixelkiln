using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelForge.Core;

public enum AnimProperty { X, Y, Rotation, ScaleX, ScaleY, Opacity }
public enum AnimEase { Linear, EaseIn, EaseOut, EaseInOut, Step, Bezier }
public enum AnimPlay { Loop, Once, PingPong }

/// <summary>A key: value at a time, and the curve towards the next key (Bezier = CSS-style cubic-bezier(X1, Y1, X2, Y2)).</summary>
public sealed record AnimKey(int TimeMs, double Value, AnimEase Ease = AnimEase.Linear, double X1 = 0.25, double Y1 = 0.1, double X2 = 0.25, double Y2 = 1);

/// <summary>Keys of one property; Target names a rig part (empty = the whole object, camera or UI element).</summary>
public sealed class AnimTrack
{
    public string Target { get; set; } = "";
    public AnimProperty Property { get; set; }
    public List<AnimKey> Keys { get; set; } = [];
    public AnimTrack Clone() => new() { Target = Target, Property = Property, Keys = [.. Keys] };
}

public sealed class AnimClip
{
    public string Name { get; set; } = "clip";
    public AnimPlay Play { get; set; } = AnimPlay.Loop;
    public bool Reverse { get; set; }
    public List<AnimTrack> Tracks { get; set; } = [];

    public int DurationMs => Tracks.SelectMany(t => t.Keys).Select(k => k.TimeMs).DefaultIfEmpty(0).Max();
    public AnimClip Clone() => new() { Name = Name, Play = Play, Reverse = Reverse, Tracks = [.. Tracks.Select(t => t.Clone())] };

    /// <summary>Default value of a property when a clip has no track for it.</summary>
    public static double Neutral(AnimProperty p) => p is AnimProperty.ScaleX or AnimProperty.ScaleY or AnimProperty.Opacity ? 1 : 0;

    /// <summary>Clip time for a moment of playback: looping, ping-pong and reverse applied.</summary>
    public double LocalTime(double t)
    {
        double d = DurationMs;
        if (d <= 0) return 0;
        double local = Play switch
        {
            AnimPlay.Once => Math.Clamp(t, 0, d),
            AnimPlay.PingPong => (t % (2 * d) + 2 * d) % (2 * d) is var x && x > d ? 2 * d - x : (t % (2 * d) + 2 * d) % (2 * d),
            _ => (t % d + d) % d,
        };
        return Reverse ? d - local : local;
    }

    public double Value(string target, AnimProperty p, double t) =>
        Tracks.FirstOrDefault(x => x.Target == target && x.Property == p) is { Keys.Count: > 0 } tr ? Sample(tr, LocalTime(t)) : Neutral(p);

    /// <summary>The pose of one target at a moment (same shape as the scene's older movement tracks).</summary>
    public Keyframe Pose(string target, double t) => new(0,
        Value(target, AnimProperty.X, t), Value(target, AnimProperty.Y, t), Value(target, AnimProperty.ScaleX, t), Value(target, AnimProperty.ScaleY, t),
        Value(target, AnimProperty.Rotation, t), Value(target, AnimProperty.Opacity, t));

    public static double Sample(AnimTrack tr, double time)
    {
        var keys = tr.Keys;
        if (time <= keys[0].TimeMs) return keys[0].Value;
        for (int i = 0; i + 1 < keys.Count; i++)
        {
            var a = keys[i];
            var b = keys[i + 1];
            if (time > b.TimeMs) continue;
            double u = b.TimeMs == a.TimeMs ? 1 : (time - a.TimeMs) / (b.TimeMs - a.TimeMs);
            return a.Value + (b.Value - a.Value) * Ease(a, u);
        }
        return keys[^1].Value;
    }

    /// <summary>Progress 0–1 along a segment shaped by the key's curve.</summary>
    public static double Ease(AnimKey k, double u) => k.Ease switch
    {
        AnimEase.EaseIn => u * u,
        AnimEase.EaseOut => 1 - (1 - u) * (1 - u),
        AnimEase.EaseInOut => u < 0.5 ? 2 * u * u : 1 - 2 * (1 - u) * (1 - u),
        AnimEase.Step => u < 1 ? 0 : 1,
        AnimEase.Bezier => CubicBezier(k.X1, k.Y1, k.X2, k.Y2, u),
        _ => u,
    };

    /// <summary>CSS cubic-bezier: find the curve parameter whose x is u (Newton, then bisection), return its y.</summary>
    public static double CubicBezier(double x1, double y1, double x2, double y2, double u)
    {
        x1 = Math.Clamp(x1, 0, 1);
        x2 = Math.Clamp(x2, 0, 1);
        static double B(double a, double b, double s) => 3 * a * (1 - s) * (1 - s) * s + 3 * b * (1 - s) * s * s + s * s * s;
        static double D(double a, double b, double s) => 3 * a * (1 - s) * (1 - s) + 6 * (b - a) * (1 - s) * s + 3 * (1 - b) * s * s;
        double s = u;
        for (int i = 0; i < 8; i++)
        {
            double dx = B(x1, x2, s) - u, slope = D(x1, x2, s);
            if (Math.Abs(dx) < 1e-7) return B(y1, y2, s);
            if (Math.Abs(slope) < 1e-6) break;
            s = Math.Clamp(s - dx / slope, 0, 1);
        }
        double lo = 0, hi = 1;
        s = u;
        for (int i = 0; i < 40; i++)
        {
            if (B(x1, x2, s) < u) lo = s; else hi = s;
            s = (lo + hi) / 2;
        }
        return B(y1, y2, s);
    }

    /// <summary>A clip from a scene object's older movement track (same keys and easings).</summary>
    public static AnimClip FromTrack(KeyframeTrack t, string name)
    {
        AnimEase E(Easing e) => e switch { Easing.EaseIn => AnimEase.EaseIn, Easing.EaseOut => AnimEase.EaseOut, Easing.EaseInOut => AnimEase.EaseInOut, Easing.Step => AnimEase.Step, _ => AnimEase.Linear };
        var keys = t.Keys.OrderBy(k => k.TimeMs).ToList();
        AnimTrack Tr(AnimProperty p, Func<Keyframe, double> v) => new() { Property = p, Keys = [.. keys.Select(k => new AnimKey(k.TimeMs, v(k), E(k.Easing)))] };
        var clip = new AnimClip { Name = name, Play = t.Loop ? AnimPlay.Loop : AnimPlay.Once, Tracks = [Tr(AnimProperty.X, k => k.X), Tr(AnimProperty.Y, k => k.Y)] };
        if (keys.Any(k => k.ScaleX != 1)) clip.Tracks.Add(Tr(AnimProperty.ScaleX, k => k.ScaleX));
        if (keys.Any(k => k.ScaleY != 1)) clip.Tracks.Add(Tr(AnimProperty.ScaleY, k => k.ScaleY));
        if (keys.Any(k => k.Rotation != 0)) clip.Tracks.Add(Tr(AnimProperty.Rotation, k => k.Rotation));
        if (keys.Any(k => k.Opacity != 1)) clip.Tracks.Add(Tr(AnimProperty.Opacity, k => k.Opacity));
        return clip;
    }
}

/// <summary>Animation library (.pxanim): named clips used by scene objects, the camera, UI elements and character rigs.</summary>
public sealed class AnimLibrary
{
    public const int Version = 1;
    public List<AnimClip> Clips { get; set; } = [];

    public AnimClip? Clip(string? name) => name is null ? Clips.FirstOrDefault() : Clips.FirstOrDefault(c => c.Name == name);
    public AnimLibrary Clone() => new() { Clips = [.. Clips.Select(c => c.Clone())] };

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    sealed record Dto(int Version, List<AnimClip> Clips);

    public static string Serialize(AnimLibrary l) => JsonSerializer.Serialize(new Dto(Version, l.Clips), Json);

    public static AnimLibrary Deserialize(string json)
    {
        var d = JsonSerializer.Deserialize<Dto>(json, Json) ?? throw new InvalidDataException("Empty animation file.");
        var l = new AnimLibrary { Clips = d.Clips ?? [] };
        if (l.Clips.Select(c => c.Name).Distinct().Count() != l.Clips.Count) throw new InvalidDataException("Two clips have the same name.");
        foreach (var c in l.Clips)
        {
            c.Tracks ??= [];
            if (c.Tracks.GroupBy(t => (t.Target ?? "", t.Property)).Any(g => g.Count() > 1)) throw new InvalidDataException($"Clip “{c.Name}” animates one property twice.");
            foreach (var t in c.Tracks)
            {
                t.Target ??= "";
                t.Keys ??= [];
                if (t.Keys.Any(k => k.TimeMs < 0) || t.Keys.Zip(t.Keys.Skip(1)).Any(p => p.Second.TimeMs < p.First.TimeMs))
                    throw new InvalidDataException($"Clip “{c.Name}”: key times must be ≥ 0 and in order.");
            }
        }
        return l;
    }

    public static void Save(AnimLibrary l, string path) => DocumentFile.Save(path, Serialize(l), Version);
    public static AnimLibrary Load(string path) => DocumentFile.Load(path, "animation", Version, Deserialize);
}

/// <summary>
/// A cut-out body part: a rectangle of the character's sprite (first frame) with a pivot, hung from a parent part
/// (or the character's origin) at an offset. Animations rotate, move and scale parts around their pivots.
/// </summary>
public sealed class RigPart
{
    public string Name { get; set; } = "part";
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; } = 8;
    public int H { get; set; } = 8;
    /// <summary>Pivot inside the rectangle (joint).</summary>
    public int PivotX { get; set; }
    public int PivotY { get; set; }
    /// <summary>Parent part name; empty = hangs from the character origin.</summary>
    public string Parent { get; set; } = "";
    /// <summary>Where the pivot sits relative to the parent's pivot (or the origin).</summary>
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public int Z { get; set; }
    public RigPart Clone() => (RigPart)MemberwiseClone();
}

public static class Rig
{
    /// <summary>World transform of a part: position of its pivot, rotation (degrees) and scale, relative to the character origin.</summary>
    public sealed record PartPose(RigPart Part, double X, double Y, double Rotation, double ScaleX, double ScaleY, double Opacity);

    /// <summary>
    /// Poses every part for a clip at a time: each part's local move/rotate/scale is applied in its parent's space, so
    /// turning a shoulder carries the arm and hand. Parts are returned in drawing order (Z, then list order).
    /// </summary>
    public static List<PartPose> Pose(IReadOnlyList<RigPart> parts, AnimClip? clip, double timeMs)
    {
        var poses = new Dictionary<string, PartPose>();
        PartPose Of(RigPart p, int depth)
        {
            if (poses.TryGetValue(p.Name, out var done)) return done;
            if (depth > parts.Count) throw new InvalidDataException($"Rig part “{p.Name}” is its own ancestor.");
            var local = clip?.Pose(p.Name, timeMs) ?? new Keyframe(0);
            double lx = p.OffsetX + local.X, ly = p.OffsetY + local.Y;
            PartPose pose;
            if (parts.FirstOrDefault(x => x.Name == p.Parent && x != p) is { } parent)
            {
                var pp = Of(parent, depth + 1);
                double a = pp.Rotation * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
                double wx = pp.X + (lx * pp.ScaleX) * c - (ly * pp.ScaleY) * s, wy = pp.Y + (lx * pp.ScaleX) * s + (ly * pp.ScaleY) * c;
                pose = new PartPose(p, wx, wy, pp.Rotation + local.Rotation, pp.ScaleX * local.ScaleX, pp.ScaleY * local.ScaleY, pp.Opacity * local.Opacity);
            }
            else pose = new PartPose(p, lx, ly, local.Rotation, local.ScaleX, local.ScaleY, local.Opacity);
            return poses[p.Name] = pose;
        }
        foreach (var p in parts) Of(p, 0);
        return [.. parts.Select((p, i) => (poses[p.Name], i)).OrderBy(x => x.Item1.Part.Z).ThenBy(x => x.i).Select(x => x.Item1)];
    }

    /// <summary>Draws the rig at a time into a picture (origin at ox, oy), cutting parts from the sprite's first frame.</summary>
    public static void Draw(uint[] dst, int dw, int dh, uint[] frame, int fw, int fh, IReadOnlyList<RigPart> parts, AnimClip? clip, double timeMs, double ox, double oy, bool flipX = false)
    {
        foreach (var pose in Pose(parts, clip, timeMs))
        {
            var p = pose.Part;
            int w = Math.Clamp(p.W, 1, fw), h = Math.Clamp(p.H, 1, fh);
            var piece = PixelOps.Crop(frame, fw, Math.Clamp(p.X, 0, fw - w), Math.Clamp(p.Y, 0, fh - h), w, h);
            double flip = flipX ? -1 : 1;
            SceneRenderer.BlitTransformed(dst, dw, dh, piece, w, h, p.PivotX, p.PivotY, ox + flip * pose.X, oy + pose.Y,
                flip * pose.ScaleX, pose.ScaleY, flip * pose.Rotation, pose.Opacity);
        }
    }

    /// <summary>Problems a rig can have: unknown parents, loops, duplicate names, parts outside the sprite.</summary>
    public static List<string> Check(IReadOnlyList<RigPart> parts, int fw, int fh)
    {
        var o = new List<string>();
        foreach (var g in parts.GroupBy(p => p.Name).Where(g => g.Count() > 1)) o.Add($"Two parts are named “{g.Key}”.");
        foreach (var p in parts)
        {
            if (p.Parent.Length > 0 && parts.All(x => x.Name != p.Parent)) o.Add($"“{p.Name}” hangs from “{p.Parent}”, which does not exist.");
            if (p.X < 0 || p.Y < 0 || p.X + p.W > fw || p.Y + p.H > fh) o.Add($"“{p.Name}” reaches outside the sprite.");
            var seen = new HashSet<string> { p.Name };
            for (var q = p; q.Parent.Length > 0 && parts.FirstOrDefault(x => x.Name == q.Parent) is { } up; q = up)
                if (!seen.Add(up.Name)) { o.Add($"“{p.Name}” is part of a parent loop."); break; }
        }
        return o;
    }
}
