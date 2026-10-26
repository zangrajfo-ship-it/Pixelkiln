using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace PixelForge.Core;

public enum BlendMode { Normal, Multiply, Screen, Overlay, Add, Darken, Lighten }

/// <summary>
/// Raster layer for painting, stored as 256×256 tiles that exist only where something was painted, so a 8192×8192
/// canvas with a few strokes stays small in memory and on disk.
/// </summary>
public sealed class PaintLayer
{
    public const int Tile = 256;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Layer";
    public bool Visible { get; set; } = true;
    public double Opacity { get; set; } = 1;
    public BlendMode Blend { get; set; }
    /// <summary>Painting changes colour only where the layer already has paint.</summary>
    public bool AlphaLocked { get; set; }
    /// <summary>ARGB tiles keyed by Key(tx, ty).</summary>
    public Dictionary<long, uint[]> Tiles { get; } = [];
    /// <summary>Layer mask (0 hides, 255 shows) as tiles; null = no mask. Missing mask tiles show fully.</summary>
    public Dictionary<long, byte[]>? Mask { get; set; }

    public static long Key(int tx, int ty) => (long)ty << 32 | (uint)tx;
    public static (int X, int Y) Unkey(long k) => ((int)(uint)k, (int)(k >> 32));

    public uint[] TileFor(int tx, int ty) => Tiles.TryGetValue(Key(tx, ty), out var t) ? t : Tiles[Key(tx, ty)] = new uint[Tile * Tile];

    public uint Get(int x, int y) => Tiles.TryGetValue(Key(x / Tile, y / Tile), out var t) ? t[y % Tile * Tile + x % Tile] : 0;

    public byte MaskAt(int x, int y) => Mask is null ? (byte)255 : Mask.TryGetValue(Key(x / Tile, y / Tile), out var m) ? m[y % Tile * Tile + x % Tile] : (byte)255;

    /// <summary>Settings only (tiles are shared): for undo of layer changes.</summary>
    public PaintLayer Shallow() => (PaintLayer)MemberwiseClone();
}

/// <summary>A painting (.pxpaint): any size up to 8192×8192, layers with blend modes, masks and alpha lock.</summary>
public sealed class PaintDoc
{
    public const int Version = 1, MaxSize = 8192;

    public int Width { get; }
    public int Height { get; }
    public List<PaintLayer> Layers { get; set; } = [];

    public PaintDoc(int width, int height, bool withLayer = true)
    {
        if (width is < 1 or > MaxSize || height is < 1 or > MaxSize) throw new ArgumentOutOfRangeException(nameof(width), $"Paintings are 1–{MaxSize} px on each side.");
        (Width, Height) = (width, height);
        if (withLayer) Layers.Add(new PaintLayer { Name = "Layer 1" });
    }

    public int TilesX => (Width + PaintLayer.Tile - 1) / PaintLayer.Tile;
    public int TilesY => (Height + PaintLayer.Tile - 1) / PaintLayer.Tile;

    /// <summary>Memory held by tiles (for the undo budget and the status bar).</summary>
    public long Bytes => Layers.Sum(l => (long)l.Tiles.Count * PaintLayer.Tile * PaintLayer.Tile * 4 + (l.Mask?.Count ?? 0) * (long)PaintLayer.Tile * PaintLayer.Tile);

    // ---------- compositing ----------

    /// <summary>All visible layers over transparency for one rectangle (clipped to the canvas).</summary>
    public uint[] Composite(int x, int y, int w, int h)
    {
        var o = new uint[w * h];
        foreach (var l in Layers.Where(l => l.Visible && l.Opacity > 0))
            for (int ty = Math.Max(0, y / PaintLayer.Tile); ty <= Math.Min(TilesY - 1, (y + h - 1) / PaintLayer.Tile); ty++)
                for (int tx = Math.Max(0, x / PaintLayer.Tile); tx <= Math.Min(TilesX - 1, (x + w - 1) / PaintLayer.Tile); tx++)
                {
                    if (!l.Tiles.TryGetValue(PaintLayer.Key(tx, ty), out var tile)) continue;
                    byte[]? mask = l.Mask is null ? null : l.Mask.GetValueOrDefault(PaintLayer.Key(tx, ty));
                    int x0 = Math.Max(x, tx * PaintLayer.Tile), x1 = Math.Min(Math.Min(x + w, Width), (tx + 1) * PaintLayer.Tile);
                    int y0 = Math.Max(y, ty * PaintLayer.Tile), y1 = Math.Min(Math.Min(y + h, Height), (ty + 1) * PaintLayer.Tile);
                    for (int py = y0; py < y1; py++)
                    {
                        int row = (py - ty * PaintLayer.Tile) * PaintLayer.Tile - tx * PaintLayer.Tile, orow = (py - y) * w - x;
                        for (int px = x0; px < x1; px++)
                        {
                            uint s = tile[row + px];
                            if (s >> 24 == 0) continue;
                            double a = (s >> 24) / 255.0 * l.Opacity * (mask is null ? 1 : mask[row + px] / 255.0);
                            o[orow + px] = Blending.Over(o[orow + px], s, a, l.Blend);
                        }
                    }
                }
        return o;
    }

    public uint[] Flatten() => Composite(0, 0, Width, Height);

    /// <summary>Small picture (longest side at most max) built tile by tile, so a huge painting never needs one huge buffer.</summary>
    public (uint[] Pixels, int Width, int Height) Preview(int max)
    {
        double scale = Math.Min(1, (double)max / Math.Max(Width, Height));
        int w = Math.Max(1, (int)(Width * scale)), h = Math.Max(1, (int)(Height * scale));
        var o = new uint[w * h];
        for (int ty = 0; ty < TilesY; ty++)
            for (int tx = 0; tx < TilesX; tx++)
            {
                int x0 = tx * PaintLayer.Tile, y0 = ty * PaintLayer.Tile, tw = Math.Min(PaintLayer.Tile, Width - x0), th = Math.Min(PaintLayer.Tile, Height - y0);
                if (!Layers.Any(l => l.Visible && l.Tiles.ContainsKey(PaintLayer.Key(tx, ty)))) continue;
                var part = Composite(x0, y0, tw, th);
                for (int y = (int)Math.Ceiling(y0 * scale); y < Math.Min(h, (y0 + th) * scale); y++)
                    for (int x = (int)Math.Ceiling(x0 * scale); x < Math.Min(w, (x0 + tw) * scale); x++)
                    {
                        int sx = Math.Min(tw - 1, (int)(x / scale) - x0), sy = Math.Min(th - 1, (int)(y / scale) - y0);
                        o[y * w + x] = part[sy * tw + sx];
                    }
            }
        return (o, w, h);
    }

    // ---------- file: zip with manifest.json and PNG tiles ----------

    sealed record LayerDto(Guid Id, string Name, bool Visible, double Opacity, BlendMode Blend, bool AlphaLocked, bool HasMask);
    sealed record Manifest(int Version, int Width, int Height, List<LayerDto> Layers);
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static void Save(PaintDoc d, string path) => DocumentFile.WriteViaTemp(path, fs =>
    {
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var m = new Manifest(Version, d.Width, d.Height, [.. d.Layers.Select(l => new LayerDto(l.Id, l.Name, l.Visible, l.Opacity, l.Blend, l.AlphaLocked, l.Mask is not null))]);
        using (var w = new StreamWriter(zip.CreateEntry("manifest.json").Open())) w.Write(JsonSerializer.Serialize(m, Json));
        for (int i = 0; i < d.Layers.Count; i++)
        {
            var l = d.Layers[i];
            foreach (var (k, t) in l.Tiles.OrderBy(kv => kv.Key))
            {
                if (t.All(p => p >> 24 == 0)) continue;   // empty tiles are not stored
                var (tx, ty) = PaintLayer.Unkey(k);
                using var s = zip.CreateEntry($"layers/{i}/{tx}_{ty}.png", CompressionLevel.NoCompression).Open();
                s.Write(ImageIO.EncodePng(t, PaintLayer.Tile, PaintLayer.Tile));
            }
            foreach (var (k, mt) in (l.Mask ?? []).OrderBy(kv => kv.Key))
            {
                var (tx, ty) = PaintLayer.Unkey(k);
                using var s = zip.CreateEntry($"layers/{i}/mask_{tx}_{ty}.png", CompressionLevel.NoCompression).Open();
                s.Write(ImageIO.EncodePng([.. mt.Select(v => (uint)v << 24)], PaintLayer.Tile, PaintLayer.Tile));   // mask value in the alpha channel
            }
        }
    });

    public static PaintDoc Load(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Not a PixelForge painting (no manifest.json).");
            Manifest m;
            using (var r = new StreamReader(entry.Open())) m = JsonSerializer.Deserialize<Manifest>(r.ReadToEnd(), Json) ?? throw new InvalidDataException("Empty painting manifest.");
            if (m.Version > Version) throw new InvalidDataException($"This painting was saved by a newer PixelForge (format {m.Version}); this version reads up to {Version}.");
            var d = new PaintDoc(m.Width, m.Height, withLayer: false);
            for (int i = 0; i < m.Layers.Count; i++)
            {
                var dto = m.Layers[i];
                var l = new PaintLayer { Id = dto.Id, Name = dto.Name, Visible = dto.Visible, Opacity = Math.Clamp(dto.Opacity, 0, 1), Blend = dto.Blend, AlphaLocked = dto.AlphaLocked };
                if (dto.HasMask) l.Mask = [];
                string prefix = $"layers/{i}/";
                foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith(prefix) && e.FullName.EndsWith(".png")))
                {
                    string name = e.Name[..^4];
                    bool isMask = name.StartsWith("mask_");
                    var parts = (isMask ? name[5..] : name).Split('_');
                    if (parts.Length != 2 || !int.TryParse(parts[0], out int tx) || !int.TryParse(parts[1], out int ty) || tx < 0 || ty < 0 || tx >= d.TilesX || ty >= d.TilesY)
                        throw new InvalidDataException($"Unexpected tile “{e.FullName}”.");
                    using var ms = new MemoryStream();
                    using (var s = e.Open()) s.CopyTo(ms);
                    ms.Position = 0;
                    var (px, w, h) = ImageIO.FromBitmap(BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad));
                    if (w != PaintLayer.Tile || h != PaintLayer.Tile) throw new InvalidDataException($"Tile “{e.FullName}” is not {PaintLayer.Tile}×{PaintLayer.Tile}.");
                    if (isMask) l.Mask![PaintLayer.Key(tx, ty)] = [.. px.Select(p => (byte)(p >> 24))];
                    else l.Tiles[PaintLayer.Key(tx, ty)] = px;
                }
                d.Layers.Add(l);
            }
            if (d.Layers.Count == 0) throw new InvalidDataException("The painting has no layers.");
            return d;
        }
        catch (Exception ex) when (ex is IOException or JsonException or NotSupportedException or ArgumentException or FormatException)
        {
            throw new InvalidDataException($"The painting cannot be read: {ex.Message}");
        }
    }
}

/// <summary>Separable blend modes and source-over in straight (non-premultiplied) ARGB.</summary>
public static class Blending
{
    static double Mode(BlendMode m, double b, double s) => m switch
    {
        BlendMode.Multiply => b * s,
        BlendMode.Screen => b + s - b * s,
        BlendMode.Overlay => b <= 0.5 ? 2 * b * s : 1 - 2 * (1 - b) * (1 - s),
        BlendMode.Add => Math.Min(1, b + s),
        BlendMode.Darken => Math.Min(b, s),
        BlendMode.Lighten => Math.Max(b, s),
        _ => s,
    };

    /// <summary>
    /// Puts colour s with coverage a (0–1, already including the layer's alpha and opacity) over backdrop d.
    /// Blend modes act where the backdrop is opaque and fade to plain colour where it is transparent (W3C compositing).
    /// </summary>
    public static uint Over(uint d, uint s, double a, BlendMode mode = BlendMode.Normal)
    {
        if (a <= 0) return d;
        double ab = (d >> 24) / 255.0, ao = a + ab * (1 - a);
        if (ao <= 0) return 0;
        uint C(int shift)
        {
            double cb = (d >> shift & 0xFF) / 255.0, cs = (s >> shift & 0xFF) / 255.0;
            double mixed = mode == BlendMode.Normal ? cs : (1 - ab) * cs + ab * Mode(mode, cb, cs);
            double co = (a * mixed + ab * cb * (1 - a)) / ao;
            return (uint)Math.Clamp(Math.Round(co * 255), 0, 255) << shift;
        }
        return (uint)Math.Round(ao * 255) << 24 | C(16) | C(8) | C(0);
    }
}
