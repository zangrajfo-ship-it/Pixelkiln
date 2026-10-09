using System.Runtime.InteropServices;
using System.Text.Json;

namespace PixelForge.Core;

/// <summary>Image sliced into square tiles, left-to-right, top-to-bottom.</summary>
public sealed record Tileset(uint[] Pixels, int Width, int Height, int TileSize)
{
    public int Columns => Width / TileSize;
    public int Rows => Height / TileSize;
    public int Count => Columns * Rows;

    public uint[] Tile(int index)
    {
        var t = new uint[TileSize * TileSize];
        int ox = index % Columns * TileSize, oy = index / Columns * TileSize;
        for (int y = 0; y < TileSize; y++)
            Array.Copy(Pixels, (oy + y) * Width + ox, t, y * TileSize, TileSize);
        return t;
    }
}

public sealed class MapLayer : ILayer
{
    public string Name { get; set; } = "Layer";
    public bool Visible { get; set; } = true;
    /// <summary>Tile index per cell, row-major; -1 = empty.</summary>
    public int[] Tiles { get; init; } = [];
}

/// <summary>Layers[0] is the bottom layer.</summary>
public sealed class Tilemap
{
    public const int Empty = -1;

    public int Width { get; }
    public int Height { get; }
    public int TileSize { get; }
    public Tileset? Tileset { get; set; }
    /// <summary>Where the tileset was loaded from (for "reload"); pixels are stored in the map anyway.</summary>
    public string? TilesetSource { get; set; }
    public List<MapLayer> Layers { get; } = [];

    public Tilemap(int width, int height, int tileSize, bool empty = false)
    {
        if (width < 1 || height < 1 || width > 1024 || height > 1024) throw new ArgumentOutOfRangeException(nameof(width), "Map size must be 1..1024 tiles.");
        if (tileSize < 1 || tileSize > 256) throw new ArgumentOutOfRangeException(nameof(tileSize), "Tile size must be 1..256.");
        (Width, Height, TileSize) = (width, height, tileSize);
        if (!empty) AddLayer(0, "Layer 1");
    }

    public MapLayer AddLayer(int index, string name)
    {
        var l = new MapLayer { Name = name, Tiles = Enumerable.Repeat(Empty, Width * Height).ToArray() };
        Layers.Insert(index, l);
        return l;
    }

    public void RemoveLayer(int index)
    {
        if (Layers.Count > 1) Layers.RemoveAt(index);
    }

    public void MoveLayer(int from, int to)
    {
        if (to < 0 || to >= Layers.Count) return;
        var l = Layers[from];
        Layers.RemoveAt(from);
        Layers.Insert(to, l);
    }

    /// <summary>Topmost visible tile at a cell, or Empty.</summary>
    public int Pick(int x, int y)
    {
        for (int i = Layers.Count - 1; i >= 0; i--)
            if (Layers[i].Visible && Layers[i].Tiles[y * Width + x] != Empty) return Layers[i].Tiles[y * Width + x];
        return Empty;
    }

    /// <summary>Renders visible layers to pixels (Width*TileSize × Height*TileSize).</summary>
    public uint[] Render()
    {
        int pw = Width * TileSize, ph = Height * TileSize;
        var dst = new uint[pw * ph];
        if (Tileset is not { } ts || ts.TileSize != TileSize) return dst;
        var tiles = new Dictionary<int, uint[]>();
        var layerPx = new uint[pw * ph];
        foreach (var l in Layers)
        {
            if (!l.Visible) continue;
            Array.Clear(layerPx);
            for (int i = 0; i < l.Tiles.Length; i++)
            {
                int t = l.Tiles[i];
                if (t < 0 || t >= ts.Count) continue;
                if (!tiles.TryGetValue(t, out var tp)) tiles[t] = tp = ts.Tile(t);
                int ox = i % Width * TileSize, oy = i / Width * TileSize;
                for (int y = 0; y < TileSize; y++)
                    Array.Copy(tp, y * TileSize, layerPx, (oy + y) * pw + ox, TileSize);
            }
            Compositor.Blend(dst, layerPx);
        }
        return dst;
    }

    public Tilemap Clone()
    {
        var m = new Tilemap(Width, Height, TileSize, empty: true) { Tileset = Tileset, TilesetSource = TilesetSource };
        foreach (var l in Layers) m.Layers.Add(new MapLayer { Name = l.Name, Visible = l.Visible, Tiles = (int[])l.Tiles.Clone() });
        return m;
    }

    // ---------- .pxm file (JSON) ----------

    record LayerDto(string Name, bool Visible, int[] Tiles);
    record MapDto(int Version, int Width, int Height, int TileSize, string? TilesetSource, int TilesetWidth, int TilesetHeight, string? TilesetPixels, LayerDto[] Layers);

    public static string Serialize(Tilemap m) => JsonSerializer.Serialize(new MapDto(
        1, m.Width, m.Height, m.TileSize, m.TilesetSource, m.Tileset?.Width ?? 0, m.Tileset?.Height ?? 0,
        m.Tileset is { } ts ? Convert.ToBase64String(MemoryMarshal.AsBytes(ts.Pixels.AsSpan())) : null,
        [.. m.Layers.Select(l => new LayerDto(l.Name, l.Visible, l.Tiles))]));

    public static Tilemap Deserialize(string json)
    {
        var d = JsonSerializer.Deserialize<MapDto>(json) ?? throw new InvalidDataException("Empty map file.");
        var m = new Tilemap(d.Width, d.Height, d.TileSize, empty: true) { TilesetSource = d.TilesetSource };
        if (d.TilesetPixels is not null)
        {
            var px = MemoryMarshal.Cast<byte, uint>(Convert.FromBase64String(d.TilesetPixels)).ToArray();
            if (px.Length != d.TilesetWidth * d.TilesetHeight) throw new InvalidDataException("Tileset size does not match its pixels.");
            m.Tileset = new Tileset(px, d.TilesetWidth, d.TilesetHeight, d.TileSize);
        }
        foreach (var l in d.Layers)
        {
            if (l.Tiles.Length != d.Width * d.Height) throw new InvalidDataException($"Layer '{l.Name}' has wrong size.");
            m.Layers.Add(new MapLayer { Name = l.Name, Visible = l.Visible, Tiles = l.Tiles });
        }
        if (m.Layers.Count == 0) throw new InvalidDataException("Map has no layers.");
        return m;
    }

    public static void Save(Tilemap m, string path) => File.WriteAllText(path, Serialize(m));
    public static Tilemap Load(string path) => Deserialize(File.ReadAllText(path));
}
