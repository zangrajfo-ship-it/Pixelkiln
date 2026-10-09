namespace PixelForge.Core;

/// <summary>Pixel operations on a w×h grid. Works for any T (pixels, tile indices).</summary>
public static class Tools
{
    public static bool InBounds(int w, int h, int x, int y) => x >= 0 && y >= 0 && x < w && y < h;

    public static void Set<T>(T[] grid, int w, int h, int x, int y, T value)
    {
        if (InBounds(w, h, x, y)) grid[y * w + x] = value;
    }

    /// <summary>Bresenham line, so fast mouse strokes have no gaps.</summary>
    public static void Line<T>(T[] grid, int w, int h, int x0, int y0, int x1, int y1, T value)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            Set(grid, w, h, x0, y0, value);
            if (x0 == x1 && y0 == y1) return;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>4-way flood fill of the contiguous region matching the start cell.</summary>
    public static void Fill<T>(T[] grid, int w, int h, int x, int y, T value)
    {
        if (!InBounds(w, h, x, y)) return;
        var cmp = EqualityComparer<T>.Default;
        T target = grid[y * w + x];
        if (cmp.Equals(target, value)) return;
        var q = new Queue<int>();
        q.Enqueue(y * w + x);
        grid[y * w + x] = value;
        while (q.Count > 0)
        {
            int i = q.Dequeue(), cx = i % w, cy = i / w;
            Try(cx - 1, cy); Try(cx + 1, cy); Try(cx, cy - 1); Try(cx, cy + 1);
        }

        void Try(int nx, int ny)
        {
            if (!InBounds(w, h, nx, ny)) return;
            int n = ny * w + nx;
            if (!cmp.Equals(grid[n], target)) return;
            grid[n] = value;
            q.Enqueue(n);
        }
    }
}
