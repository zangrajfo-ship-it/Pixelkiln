using System.Security.Cryptography;
using System.Text.Json;

namespace PixelForge.Core;

/// <summary>
/// Texture atlases: trims transparent borders, merges identical pictures and packs the rest with MaxRects
/// (best short side fit) onto as few pages as possible.
/// </summary>
public static class Atlas
{
    public sealed record Item(string Key, uint[] Pixels, int W, int H);

    /// <summary>Where a picture went: its trimmed rectangle on a page, the trim offset inside the original, and the original size.
    /// SameAs names the item whose pixels it shares (stored once).</summary>
    public sealed record Placed(string Key, int Page, int X, int Y, int W, int H, int TrimX, int TrimY, int SourceW, int SourceH, string? SameAs);

    public sealed record Page(uint[] Pixels, int W, int H);
    public sealed record Result(List<Page> Pages, List<Placed> Frames);

    /// <summary>Bounds of the non-transparent pixels (W = 0 when the picture is empty).</summary>
    public static (int X, int Y, int W, int H) TrimBounds(uint[] px, int w, int h)
    {
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * w + x] >> 24 != 0) (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
        return x1 < 0 ? (0, 0, 0, 0) : (x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    /// <summary>
    /// MaxRects, best short side fit: positions for the sizes that fit in one bin (null for those that do not).
    /// Sizes are tried largest first; the result keeps the input order.
    /// </summary>
    public static (int X, int Y)?[] MaxRects(IReadOnlyList<(int W, int H)> sizes, int binW, int binH)
    {
        var free = new List<(int X, int Y, int W, int H)> { (0, 0, binW, binH) };
        var o = new (int X, int Y)?[sizes.Count];
        foreach (int i in Enumerable.Range(0, sizes.Count).OrderByDescending(i => Math.Max(sizes[i].W, sizes[i].H)).ThenByDescending(i => sizes[i].W * sizes[i].H))
        {
            var (w, h) = sizes[i];
            int best = -1, bestShort = int.MaxValue, bestLong = int.MaxValue;
            for (int f = 0; f < free.Count; f++)
            {
                var r = free[f];
                if (w > r.W || h > r.H) continue;
                int s = Math.Min(r.W - w, r.H - h), l = Math.Max(r.W - w, r.H - h);
                if (s < bestShort || s == bestShort && l < bestLong) (best, bestShort, bestLong) = (f, s, l);
            }
            if (best < 0) continue;
            var (px, py) = (free[best].X, free[best].Y);
            o[i] = (px, py);
            // split every free rectangle the new one overlaps into up to four leftovers
            var next = new List<(int X, int Y, int W, int H)>();
            foreach (var r in free)
            {
                if (px >= r.X + r.W || px + w <= r.X || py >= r.Y + r.H || py + h <= r.Y) { next.Add(r); continue; }
                if (px > r.X) next.Add((r.X, r.Y, px - r.X, r.H));
                if (px + w < r.X + r.W) next.Add((px + w, r.Y, r.X + r.W - px - w, r.H));
                if (py > r.Y) next.Add((r.X, r.Y, r.W, py - r.Y));
                if (py + h < r.Y + r.H) next.Add((r.X, py + h, r.W, r.Y + r.H - py - h));
            }
            // drop rectangles contained in another (of two equal ones, keep the first)
            free = [];
            for (int a = 0; a < next.Count; a++)
            {
                bool inside = false;
                for (int b = 0; b < next.Count && !inside; b++)
                    inside = a != b && Contains(next[b], next[a]) && (next[a] != next[b] || b < a);
                if (!inside) free.Add(next[a]);
            }
        }
        return o;
    }

    static bool Contains((int X, int Y, int W, int H) outer, (int X, int Y, int W, int H) r) =>
        r.X >= outer.X && r.Y >= outer.Y && r.X + r.W <= outer.X + outer.W && r.Y + r.H <= outer.Y + outer.H;

    static string Hash(uint[] px, int w, int h)
    {
        var bytes = new byte[px.Length * 4 + 8];
        Buffer.BlockCopy(px, 0, bytes, 8, px.Length * 4);
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), w);
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), h);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public static Result Pack(IEnumerable<Item> items, int maxSize = 2048, int padding = 1, bool powerOfTwo = true, bool trim = true)
    {
        var list = items.ToList();
        if (list.Select(i => i.Key).Distinct().Count() != list.Count) throw new ArgumentException("Atlas keys must be unique.", nameof(items));
        var all = new Dictionary<string, Placed>();
        var unique = new List<(Item Item, uint[] Px, int W, int H, int TX, int TY)>();
        var byHash = new Dictionary<string, string>();
        foreach (var it in list)
        {
            var (tx, ty, tw, th) = trim ? TrimBounds(it.Pixels, it.W, it.H) : (0, 0, it.W, it.H);
            if (tw == 0) { all[it.Key] = new Placed(it.Key, -1, 0, 0, 0, 0, 0, 0, it.W, it.H, null); continue; }   // empty: nothing to store
            if (tw + 2 * padding > maxSize || th + 2 * padding > maxSize) throw new InvalidDataException($"“{it.Key}” ({tw}×{th}) does not fit on a {maxSize}×{maxSize} atlas page.");
            var px = tw == it.W && th == it.H ? it.Pixels : PixelOps.Crop(it.Pixels, it.W, tx, ty, tw, th);
            string hash = Hash(px, tw, th);
            if (byHash.TryGetValue(hash, out var first)) { all[it.Key] = new Placed(it.Key, -2, 0, 0, tw, th, tx, ty, it.W, it.H, first); continue; }
            byHash[hash] = it.Key;
            unique.Add((it, px, tw, th, tx, ty));
        }

        var pages = new List<Page>();
        var left = Enumerable.Range(0, unique.Count).ToList();
        while (left.Count > 0)
        {
            var pos = MaxRects([.. left.Select(i => (unique[i].W + padding * 2, unique[i].H + padding * 2))], maxSize, maxSize);
            var fit = left.Select((i, k) => (i, p: pos[k])).Where(x => x.p is not null).ToList();
            if (fit.Count == 0) throw new InvalidDataException("Atlas packing made no progress.");
            int pw = fit.Max(x => x.p!.Value.X + unique[x.i].W + 2 * padding), ph = fit.Max(x => x.p!.Value.Y + unique[x.i].H + 2 * padding);
            if (powerOfTwo) (pw, ph) = ((int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)pw), (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)ph));
            var page = new uint[pw * ph];
            foreach (var (i, p) in fit)
            {
                var u = unique[i];
                int x = p!.Value.X + padding, y = p.Value.Y + padding;
                for (int row = 0; row < u.H; row++) Array.Copy(u.Px, row * u.W, page, (y + row) * pw + x, u.W);
                all[u.Item.Key] = new Placed(u.Item.Key, pages.Count, x, y, u.W, u.H, u.TX, u.TY, u.Item.W, u.Item.H, null);
            }
            pages.Add(new Page(page, pw, ph));
            left = [.. left.Where(i => !all.ContainsKey(unique[i].Item.Key))];
        }
        // input order; a duplicate shares the stored copy's rectangle
        Placed Final(Placed f) => f.Page == -2 ? all[f.SameAs!] with { Key = f.Key, TrimX = f.TrimX, TrimY = f.TrimY, SourceW = f.SourceW, SourceH = f.SourceH, SameAs = f.SameAs } : f;
        return new Result(pages, [.. list.Select(it => Final(all[it.Key]))]);
    }

    /// <summary>Atlas description: pages and each frame's rectangle, trim offset and original size.</summary>
    public static string Json(Result r, Func<int, string> pageFile) => JsonSerializer.Serialize(new
    {
        format = "pixelforge-atlas", version = 1,
        pages = r.Pages.Select((p, i) => new { file = pageFile(i), width = p.W, height = p.H }),
        frames = r.Frames.Select(f => new
        {
            key = f.Key, page = f.Page, x = f.X, y = f.Y, w = f.W, h = f.H, trimX = f.TrimX, trimY = f.TrimY, sourceW = f.SourceW, sourceH = f.SourceH, sameAs = f.SameAs,
        }),
    }, new JsonSerializerOptions { WriteIndented = true });
}
