namespace Pixelkiln.Core;

/// <summary>Free-form selections as a canvas-sized mask (true = selected).</summary>
public static class Selection
{
    /// <summary>
    /// Magic wand: pixels with exactly the color at (x, y); <paramref name="contiguous"/> = only the
    /// touching area (4-way), otherwise every pixel of that color.
    /// </summary>
    public static bool[] MagicWand(uint[] px, int w, int h, int x, int y, bool contiguous)
    {
        if (contiguous || !Tools.InBounds(w, h, x, y)) return Tools.Region(px, w, h, x, y);
        uint target = px[y * w + x];
        return [.. px.Select(p => p == target)];
    }

    /// <summary>Smallest rectangle around the selected pixels, or null if nothing is selected.</summary>
    public static (int X, int Y, int W, int H)? Bounds(bool[] mask, int w, int h)
    {
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int i = 0; i < mask.Length; i++)
        {
            if (!mask[i]) continue;
            int x = i % w, y = i / w;
            (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
        }
        return x1 < 0 ? null : (x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    /// <summary>Copies the selected pixels inside the rectangle; unselected ones become transparent.</summary>
    public static uint[] CropMasked(uint[] px, bool[] mask, int w, int x, int y, int cw, int ch)
    {
        var d = new uint[cw * ch];
        for (int j = 0; j < ch; j++)
            for (int i = 0; i < cw; i++)
            {
                int s = (y + j) * w + x + i;
                if (mask[s]) d[j * cw + i] = px[s];
            }
        return d;
    }

    public static void ClearMasked(uint[] px, bool[] mask)
    {
        for (int i = 0; i < px.Length; i++) if (mask[i]) px[i] = 0;
    }

    /// <summary>Outline edges of the mask as (x1, y1, x2, y2) segments in pixel units (for drawing marching ants).</summary>
    public static IEnumerable<(int X1, int Y1, int X2, int Y2)> Outline(bool[] mask, int w, int h)
    {
        bool At(int x, int y) => Tools.InBounds(w, h, x, y) && mask[y * w + x];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!mask[y * w + x]) continue;
                if (!At(x, y - 1)) yield return (x, y, x + 1, y);
                if (!At(x, y + 1)) yield return (x, y + 1, x + 1, y + 1);
                if (!At(x - 1, y)) yield return (x, y, x, y + 1);
                if (!At(x + 1, y)) yield return (x + 1, y, x + 1, y + 1);
            }
    }
}
