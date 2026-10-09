namespace PixelForge.Core;

/// <summary>Block operations on ARGB pixel buffers (selection, clipboard, canvas size).</summary>
public static class PixelOps
{
    public static uint[] Crop(uint[] src, int w, int x, int y, int cw, int ch)
    {
        var dst = new uint[cw * ch];
        for (int j = 0; j < ch; j++)
            Array.Copy(src, (y + j) * w + x, dst, j * cw, cw);
        return dst;
    }

    /// <summary>Copies src onto dst at (x, y); transparent src pixels are skipped, out-of-bounds parts clipped.</summary>
    public static void Stamp(uint[] dst, int w, int h, uint[] src, int sw, int sh, int x, int y)
    {
        for (int j = 0; j < sh; j++)
            for (int i = 0; i < sw; i++)
            {
                uint p = src[j * sw + i];
                if (p >> 24 != 0 && Tools.InBounds(w, h, x + i, y + j)) dst[(y + j) * w + x + i] = p;
            }
    }

    public static void Clear(uint[] px, int w, int x, int y, int cw, int ch)
    {
        for (int j = 0; j < ch; j++) Array.Clear(px, (y + j) * w + x, cw);
    }

    public static uint[] FlipH(uint[] src, int w, int h)
    {
        var d = new uint[src.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) d[y * w + x] = src[y * w + w - 1 - x];
        return d;
    }

    public static uint[] FlipV(uint[] src, int w, int h)
    {
        var d = new uint[src.Length];
        for (int y = 0; y < h; y++) Array.Copy(src, (h - 1 - y) * w, d, y * w, w);
        return d;
    }

    /// <summary>Clockwise; the result is h wide and w tall.</summary>
    public static uint[] Rotate90(uint[] src, int w, int h)
    {
        var d = new uint[src.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) d[x * h + (h - 1 - y)] = src[y * w + x];
        return d;
    }

    /// <summary>New canvas size; old content is placed at (ox, oy) and clipped. Same layers, frames, palette.</summary>
    public static Sprite Resized(Sprite s, int w, int h, int ox, int oy)
    {
        var r = new Sprite(w, h, empty: true);
        r.Palette.Clear();
        r.Palette.AddRange(s.Palette);
        foreach (var f in s.Frames) r.Frames.Add(new Frame { DurationMs = f.DurationMs });
        r.Tags.AddRange(s.Tags.Select(t => t.Clone()));
        foreach (var l in s.Layers)
        {
            var nl = new Layer { Name = l.Name, Visible = l.Visible, Opacity = l.Opacity };
            foreach (var cel in l.Cels)
            {
                var c = new uint[w * h];
                for (int y = 0; y < s.Height; y++)
                    for (int x = 0; x < s.Width; x++)
                        if (Tools.InBounds(w, h, x + ox, y + oy)) c[(y + oy) * w + x + ox] = cel[y * s.Width + x];
                nl.Cels.Add(c);
            }
            r.Layers.Add(nl);
        }
        return r;
    }

    /// <summary>Nearest-neighbour resize of every cel (2x for chunky pixels, 50 % to shrink).</summary>
    public static Sprite Scaled(Sprite s, int w, int h)
    {
        var r = Resized(s, w, h, 0, 0);   // same layers, frames, tags, palette
        for (int li = 0; li < s.Layers.Count; li++)
            for (int f = 0; f < s.Frames.Count; f++)
            {
                uint[] src = s.Cel(li, f), dst = r.Cel(li, f);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        dst[y * w + x] = src[y * s.Height / h * s.Width + x * s.Width / w];
            }
        return r;
    }

    /// <summary>Replaces every pixel of one exact color; returns how many changed.</summary>
    public static int ReplaceColor(uint[] px, uint from, uint to)
    {
        int n = 0;
        for (int i = 0; i < px.Length; i++)
            if (px[i] == from) { px[i] = to; n++; }
        return n;
    }

    /// <summary>Offset that places old content according to an anchor (0 = start, 1 = center, 2 = end) on each axis.</summary>
    public static int AnchorOffset(int oldSize, int newSize, int anchor) => anchor switch
    {
        0 => 0,
        1 => (newSize - oldSize) / 2,
        _ => newSize - oldSize,
    };
}
