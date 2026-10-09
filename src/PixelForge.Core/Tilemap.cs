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

/// <summary>Point (W = H = 0) or rectangle in map pixels, e.g. spawn points, triggers, collision boxes.</summary>
public sealed class MapObject
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public bool IsPoint => W == 0 && H == 0;
    public MapObject Clone() => (MapObject)MemberwiseClone();
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
    public List<MapObject> Objects { get; } = [];
    /// <summary>Auto-tile terrains: top-left tile index of each 4×4 block (see <see cref="Autotile"/>).</summary>
    public List<int> Terrains { get; } = [];

    public int NextObjectId => Objects.Count == 0 ? 1 : Objects.Max(o => o.Id) + 1;

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
        m.Objects.AddRange(Objects.Select(o => o.Clone()));
        m.Terrains.AddRange(Terrains);
        return m;
    }

    /// <summary>New map size in tiles; content moves by (ox, oy) tiles and is clipped. Objects move along.</summary>
    public Tilemap Resized(int w, int h, int ox, int oy)
    {
        var m = new Tilemap(w, h, TileSize, empty: true) { Tileset = Tileset, TilesetSource = TilesetSource };
        foreach (var l in Layers)
        {
            var nl = m.AddLayer(m.Layers.Count, l.Name);
            nl.Visible = l.Visible;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    Tools.Set(nl.Tiles, w, h, x + ox, y + oy, l.Tiles[y * Width + x]);
        }
        foreach (var o in Objects)
        {
            var c = o.Clone();
            c.X += ox * TileSize;
            c.Y += oy * TileSize;
            m.Objects.Add(c);
        }
        m.Terrains.AddRange(Terrains);
        return m;
    }

    // ---------- .pxm file (JSON) ----------

    record LayerDto(string Name, bool Visible, int[] Tiles);
    record ObjectDto(int Id, string Name, string Type, int X, int Y, int W, int H);
    // v2 added Objects and Terrains; both are optional so v1 files still load
    record MapDto(int Version, int Width, int Height, int TileSize, string? TilesetSource, int TilesetWidth, int TilesetHeight, string? TilesetPixels,
        LayerDto[] Layers, ObjectDto[]? Objects = null, int[]? Terrains = null);

    public static string Serialize(Tilemap m) => JsonSerializer.Serialize(new MapDto(
        2, m.Width, m.Height, m.TileSize, m.TilesetSource, m.Tileset?.Width ?? 0, m.Tileset?.Height ?? 0,
        m.Tileset is { } ts ? Convert.ToBase64String(MemoryMarshal.AsBytes(ts.Pixels.AsSpan())) : null,
        [.. m.Layers.Select(l => new LayerDto(l.Name, l.Visible, l.Tiles))],
        [.. m.Objects.Select(o => new ObjectDto(o.Id, o.Name, o.Type, o.X, o.Y, o.W, o.H))],
        [.. m.Terrains]));

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
        foreach (var o in d.Objects ?? [])
            m.Objects.Add(new MapObject { Id = o.Id, Name = o.Name ?? "", Type = o.Type ?? "", X = o.X, Y = o.Y, W = Math.Max(0, o.W), H = Math.Max(0, o.H) });
        m.Terrains.AddRange(d.Terrains ?? []);
        return m;
    }

    public static void Save(Tilemap m, string path) => File.WriteAllText(path, Serialize(m));
    public static Tilemap Load(string path) => Deserialize(File.ReadAllText(path));
}
