namespace PixelForge.Core;

/// <summary>
/// The project's visual rules (stored in the .pfproj settings): allowed palettes, sprite size grid, colour count,
/// outline colour, vector stroke width and largest size. Zero or empty = not checked.
/// </summary>
public sealed class StyleGuide
{
    /// <summary>Palette assets whose colours sprites and images may use.</summary>
    public List<Guid> Palettes { get; set; } = [];
    /// <summary>Sprite width and height are multiples of this (8, 16 …).</summary>
    public int Grid { get; set; }
    public int MaxColors { get; set; }
    /// <summary>Colour of every opaque pixel at the edge of a shape (RGB; null = outlines not checked).</summary>
    public uint? Outline { get; set; }
    public double VectorStroke { get; set; }
    /// <summary>Largest width or height of sprites and pictures.</summary>
    public int MaxSize { get; set; }

    public bool IsEmpty => Palettes.Count == 0 && Grid == 0 && MaxColors == 0 && Outline is null && VectorStroke == 0 && MaxSize == 0;
    public StyleGuide Clone() => new() { Palettes = [.. Palettes], Grid = Grid, MaxColors = MaxColors, Outline = Outline, VectorStroke = VectorStroke, MaxSize = MaxSize };
}

public sealed record StyleIssue(AssetEntry Asset, string Message);

public static class StyleCheck
{
    /// <summary>The allowed colours (RGB) of the guide's palettes; empty when none are set or readable.</summary>
    public static HashSet<uint> Colors(Project p, StyleGuide g)
    {
        var o = new HashSet<uint>();
        foreach (var id in g.Palettes)
            if (p.Resolve(id) is string path)
                try { foreach (var c in PaletteIO.Load(path)) o.Add(c & 0xFFFFFF); }
                catch (Exception ex) when (ex is InvalidDataException or IOException) { }
        return o;
    }

    public static List<StyleIssue> Run(Project p, StyleGuide g, CancellationToken ct = default)
    {
        var o = new List<StyleIssue>();
        if (g.IsEmpty) return o;
        var allowed = Colors(p, g);
        foreach (var a in p.Assets.OrderBy(a => a.Path, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            string path = p.FullPath(a);
            if (!File.Exists(path)) continue;
            void Add(string m) => o.Add(new StyleIssue(a, m));
            try
            {
                if (a.Type == AssetType.Vector && g.VectorStroke > 0)
                {
                    int strokes = VectorDoc.Load(path).Shapes.Count(s => s.Stroke is not null && Math.Abs(s.StrokeWidth - g.VectorStroke) > 0.01);
                    if (strokes > 0) Add($"{strokes} shape(s) have a stroke other than {g.VectorStroke:0.##} px.");
                }
                if (a.Type is not (AssetType.Sprite or AssetType.Image)) continue;
                List<(uint[] Px, int W, int H)> frames;
                if (a.Type == AssetType.Sprite)
                {
                    var s = ProjectFile.Load(path);
                    frames = [.. Enumerable.Range(0, s.Frames.Count).Select(f => (Compositor.Flatten(s, f), s.Width, s.Height))];
                    if (g.Grid > 0 && (s.Width % g.Grid != 0 || s.Height % g.Grid != 0)) Add($"{s.Width}×{s.Height} is not a multiple of the {g.Grid} px grid.");
                }
                else frames = [AssetLoader.LoadImage(path)];
                var (w, h) = (frames[0].W, frames[0].H);
                if (g.MaxSize > 0 && Math.Max(w, h) > g.MaxSize) Add($"{w}×{h} is larger than {g.MaxSize} px.");
                var used = frames.SelectMany(f => f.Px).Where(c => c >> 24 != 0).Select(c => c & 0xFFFFFF).ToHashSet();
                if (g.MaxColors > 0 && used.Count > g.MaxColors) Add($"uses {used.Count} colours (at most {g.MaxColors}).");
                if (allowed.Count > 0 && used.Where(c => !allowed.Contains(c)).ToList() is { Count: > 0 } off)
                    Add($"{off.Count} colour(s) are not in the style palette, e.g. {string.Join(", ", off.Take(3).Select(c => $"#{c:X6}"))}.");
                if (g.Outline is uint line)
                {
                    int bad = frames.Sum(f => OffOutline(f.Px, f.W, f.H, line & 0xFFFFFF));
                    if (bad > 0) Add($"{bad} edge pixel(s) are not the outline colour #{line & 0xFFFFFF:X6}.");
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException) { Add("cannot be read: " + ex.Message); }
        }
        return o;
    }

    /// <summary>Opaque pixels next to transparency (or the border) whose colour is not the outline colour.</summary>
    public static int OffOutline(uint[] px, int w, int h, uint line)
    {
        int bad = 0;
        bool Clear(int x, int y) => x < 0 || y < 0 || x >= w || y >= h || px[y * w + x] >> 24 == 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * w + x] >> 24 != 0 && (Clear(x - 1, y) || Clear(x + 1, y) || Clear(x, y - 1) || Clear(x, y + 1)) && (px[y * w + x] & 0xFFFFFF) != line) bad++;
        return bad;
    }
}
