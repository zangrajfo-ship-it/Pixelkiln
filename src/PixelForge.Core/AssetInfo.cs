namespace PixelForge.Core;

/// <summary>Human-readable facts about an asset file for the asset browser (dimensions, frames, duration …).</summary>
public static class AssetInfo
{
    public static string Describe(AssetType type, string path)
    {
        if (!File.Exists(path)) return "File is missing.";
        var file = new FileInfo(path);
        string size = file.Length < 1024 ? $"{file.Length} B" : file.Length < 1 << 20 ? $"{file.Length / 1024.0:0.#} KB" : $"{file.Length / 1048576.0:0.#} MB";
        string facts;
        try
        {
            facts = AssetKinds.Of(type).Describe(path);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException or FormatException)
        {
            facts = "Cannot be read: " + ex.Message;
        }
        return $"{facts}\n{size}, changed {file.LastWriteTime:d. M. yyyy HH:mm}";
    }

    /// <summary>Nearest-neighbour downscale so the longer side is at most <paramref name="max"/> (never upscales).</summary>
    public static (uint[] Pixels, int Width, int Height) Thumbnail(uint[] px, int w, int h, int max)
    {
        if (w <= max && h <= max) return (px, w, h);
        double f = (double)max / Math.Max(w, h);
        int tw = Math.Max(1, (int)(w * f)), th = Math.Max(1, (int)(h * f));
        return (PixelOps.Resize(px, w, h, tw, th), tw, th);
    }

    /// <summary>Picture of a palette: 8 swatches per row, 4×4 px each.</summary>
    public static (uint[] Pixels, int Width, int Height) PaletteImage(IReadOnlyList<uint> colors)
    {
        int cols = 8, rows = Math.Max(1, (colors.Count + cols - 1) / cols), w = cols * 4, h = rows * 4;
        var px = new uint[w * h];
        for (int i = 0; i < colors.Count; i++)
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++) px[(i / cols * 4 + y) * w + i % cols * 4 + x] = colors[i];
        return (px, w, h);
    }
}
