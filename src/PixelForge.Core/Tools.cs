namespace PixelForge.Core;

/// <summary>Pixel operations on a w×h grid. Works for any T (pixels, tile indices).</summary>
public static class Tools
{
    public static bool InBounds(int w, int h, int x, int y) => x >= 0 && y >= 0 && x < w && y < h;

    /// <summary>Wraps a coordinate into 0..n-1 (tiled mode: drawing past an edge continues on the other side).</summary>
    public static int Wrap(int v, int n) => (v % n + n) % n;

    public static void Set<T>(T[] grid, int w, int h, int x, int y, T value)
    {
        if (InBounds(w, h, x, y)) grid[y * w + x] = value;
    }

    /// <summary>Bresenham line points, so fast mouse strokes have no gaps.</summary>
    public static IEnumerable<(int X, int Y)> LinePoints(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            yield return (x0, y0);
            if (x0 == x1 && y0 == y1) yield break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    public static void Line<T>(T[] grid, int w, int h, int x0, int y0, int x1, int y1, T value)
    {
        foreach (var (x, y) in LinePoints(x0, y0, x1, y1)) Set(grid, w, h, x, y, value);
    }

    /// <summary>Square brush footprint of the given size around (x, y); odd sizes are centered, even ones lean up-left.</summary>
    public static IEnumerable<(int X, int Y)> BrushPoints(int x, int y, int size)
    {
        int o = (size - 1) / 2;
        for (int dy = 0; dy < size; dy++)
            for (int dx = 0; dx < size; dx++)
                yield return (x - o + dx, y - o + dy);
    }

    public static void Rect<T>(T[] grid, int w, int h, int x0, int y0, int x1, int y1, T value, bool filled)
    {
        (x0, x1) = (Math.Min(x0, x1), Math.Max(x0, x1));
        (y0, y1) = (Math.Min(y0, y1), Math.Max(y0, y1));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (filled || x == x0 || x == x1 || y == y0 || y == y1) Set(grid, w, h, x, y, value);
    }

    /// <summary>Ellipse inscribed in the box; outline = filled pixels touching the outside (gap-free, symmetric).</summary>
    public static void Ellipse<T>(T[] grid, int w, int h, int x0, int y0, int x1, int y1, T value, bool filled)
    {
        (x0, x1) = (Math.Min(x0, x1), Math.Max(x0, x1));
        (y0, y1) = (Math.Min(y0, y1), Math.Max(y0, y1));
        int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
        double rx = bw / 2.0, ry = bh / 2.0, cx = x0 + rx, cy = y0 + ry;
        var inside = new bool[bw, bh];
        for (int y = y0; y <= y1; y++)
        {
            double dy = (y + 0.5 - cy) / ry;
            double half = rx * Math.Sqrt(Math.Max(0, 1 - dy * dy));
            int xl = (int)Math.Ceiling(cx - half - 0.5), xr = (int)Math.Floor(cx + half - 0.5);
            if (xl > xr) (xl, xr) = ((int)Math.Floor(cx - 0.5), (int)Math.Ceiling(cx - 0.5));   // keep the tips
            for (int x = Math.Max(xl, x0); x <= Math.Min(xr, x1); x++) inside[x - x0, y - y0] = true;
        }
        for (int j = 0; j < bh; j++)
            for (int i = 0; i < bw; i++)
                if (inside[i, j] && (filled || Edge(i, j))) Set(grid, w, h, x0 + i, y0 + j, value);

        bool Edge(int i, int j) => !In(i - 1, j) || !In(i + 1, j) || !In(i, j - 1) || !In(i, j + 1);
        bool In(int i, int j) => i >= 0 && j >= 0 && i < bw && j < bh && inside[i, j];
    }

    /// <summary>4-way flood fill of the contiguous region matching the start cell.</summary>
    public static void Fill<T>(T[] grid, int w, int h, int x, int y, T value) =>
        FillPattern(grid, w, h, x, y, (_, _) => value);

    /// <summary>Flood fill where each cell's new value depends on its position (e.g. a repeating stamp).</summary>
    public static void FillPattern<T>(T[] grid, int w, int h, int x, int y, Func<int, int, T> valueAt)
    {
        if (!InBounds(w, h, x, y)) return;
        var cmp = EqualityComparer<T>.Default;
        T target = grid[y * w + x];
        var seen = new bool[w * h];
        var q = new Queue<int>();
        q.Enqueue(y * w + x);
        seen[y * w + x] = true;
        while (q.Count > 0)
        {
            int i = q.Dequeue(), cx = i % w, cy = i / w;
            grid[i] = valueAt(cx, cy);
            Try(cx - 1, cy); Try(cx + 1, cy); Try(cx, cy - 1); Try(cx, cy + 1);
        }

        void Try(int nx, int ny)
        {
            if (!InBounds(w, h, nx, ny)) return;
            int n = ny * w + nx;
            if (seen[n] || !cmp.Equals(grid[n], target)) return;
            seen[n] = true;
            q.Enqueue(n);
        }
    }
}
