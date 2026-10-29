namespace Pixelkiln.Core;

/// <summary>Style checks and palette operations across sprites.</summary>
public static class PaletteTools
{
    /// <summary>Opaque-ish colors (alpha &gt; 0) used in the sprite that are not in the palette, with pixel counts.</summary>
    public static Dictionary<uint, int> OutOfPalette(Sprite s, IEnumerable<uint> palette)
    {
        var allowed = palette.Select(c => c | 0xFF000000).ToHashSet();
        var result = new Dictionary<uint, int>();
        foreach (var cel in s.Layers.SelectMany(l => l.Cels))
            foreach (uint p in cel)
                if (p >> 24 != 0 && !allowed.Contains(p | 0xFF000000))
                    result[p] = result.GetValueOrDefault(p) + 1;
        return result;
    }

    /// <summary>Closest palette color (RGB, weighted for perceived brightness); alpha is kept.</summary>
    public static uint Nearest(uint color, IReadOnlyList<uint> palette)
    {
        uint best = palette[0];
        double bestD = double.MaxValue;
        foreach (uint p in palette)
        {
            double dr = (int)(color >> 16 & 0xFF) - (int)(p >> 16 & 0xFF), dg = (int)(color >> 8 & 0xFF) - (int)(p >> 8 & 0xFF), db = (int)(color & 0xFF) - (int)(p & 0xFF);
            double d = 0.3 * dr * dr + 0.59 * dg * dg + 0.11 * db * db;
            if (d < bestD) (bestD, best) = (d, p);
        }
        return color & 0xFF000000 | best & 0xFFFFFF;
    }

    /// <summary>Moves every off-palette pixel to its nearest palette color; returns how many changed.</summary>
    public static int SnapToPalette(Sprite s, IReadOnlyList<uint> palette)
    {
        if (palette.Count == 0) return 0;
        var cache = new Dictionary<uint, uint>();
        var allowed = palette.Select(c => c & 0xFFFFFF).ToHashSet();
        int n = 0;
        foreach (var cel in s.Layers.SelectMany(l => l.Cels))
            for (int i = 0; i < cel.Length; i++)
            {
                uint p = cel[i];
                if (p >> 24 == 0 || allowed.Contains(p & 0xFFFFFF)) continue;
                if (!cache.TryGetValue(p, out uint q)) cache[p] = q = Nearest(p, palette);
                cel[i] = q;
                n++;
            }
        return n;
    }

    /// <summary>Replaces one color (exact, any alpha kept) in every cel; returns pixels changed.</summary>
    public static int Replace(Sprite s, uint from, uint to) =>
        s.Layers.SelectMany(l => l.Cels).Sum(cel => PixelOps.ReplaceColor(cel, from, to));

    /// <summary>
    /// Light-to-shadow ramp around a base color, <paramref name="steps"/> colors with the base in the middle.
    /// Each step away from the base shifts the hue by <paramref name="hueShift"/> degrees, the same way for darker
    /// and lighter steps: down for greens, cyans and blues (60° to 240°), up for the other hues.
    /// </summary>
    public static List<uint> Ramp(uint baseColor, int steps = 5, double hueShift = 12)
    {
        var (h, s, v) = ColorUtil.ToHsv(baseColor);
        var ramp = new List<uint>();
        int mid = steps / 2;
        for (int i = 0; i < steps; i++)
        {
            int d = i - mid;   // negative = darker
            double nh = h - Math.Abs(d) * hueShift * (h > 60 && h < 240 ? 1 : -1);
            double nv = Math.Clamp(v + d * 0.15, 0.05, 1);
            double ns = Math.Clamp(s + (d < 0 ? 0.08 : -0.1) * Math.Abs(d), 0, 1);
            ramp.Add(ColorUtil.FromHsv(nh, ns, nv));
        }
        return ramp;
    }
}
