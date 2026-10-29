namespace Pixelkiln.Core;

public static class Compositor
{
    /// <summary>Flattens visible layers of a frame, bottom to top.</summary>
    public static uint[] Flatten(Sprite s, int frame)
    {
        var dst = new uint[s.Width * s.Height];
        foreach (var l in s.Layers)
            if (l.Visible && l.Opacity > 0) Blend(dst, l.Cels[frame], l.Opacity);
        return dst;
    }

    /// <summary>Straight-alpha "source over" of src onto dst, with extra opacity 0..1.</summary>
    public static void Blend(uint[] dst, uint[] src, double opacity = 1)
    {
        for (int i = 0; i < dst.Length; i++)
        {
            uint s = src[i];
            double sa = (s >> 24) / 255.0 * opacity;
            if (sa <= 0) continue;
            if (sa >= 1) { dst[i] = s; continue; }
            uint d = dst[i];
            double da = (d >> 24) / 255.0;
            double oa = sa + da * (1 - sa);
            dst[i] = (uint)Math.Round(oa * 255) << 24
                | Mix(s >> 16, d >> 16) << 16
                | Mix(s >> 8, d >> 8) << 8
                | Mix(s, d);

            uint Mix(uint sc, uint dc) =>
                (uint)Math.Round(((sc & 0xFF) * sa + (dc & 0xFF) * da * (1 - sa)) / oa);
        }
    }
}
