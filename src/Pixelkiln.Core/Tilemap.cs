using System.Runtime.InteropServices;
using System.Text.Json;

namespace Pixelkiln.Core;

/// <summary>Image sliced into square tiles, left-to-right, top-to-bottom.</summary>
public sealed record Tileset(uint[] Pixels, int Width, int Height, int TileSize)
{
    public int Columns => Width / TileSize;
    public int Rows => Height / TileSize;
    public int Count => Columns * Rows;

    public uint[] Tile(int index) => PixelOps.Crop(Pixels, Width, index % Columns * TileSize, index / Columns * TileSize, TileSize, TileSize);
}

/// <summary>
/// How a tile is turned in its cell: three flags as in Tiled, applied in this order: <see cref="D"/> (mirror along
/// the top-left to bottom-right diagonal), then <see cref="H"/> (mirror left/right), then <see cref="V"/> (up/down).
/// Every rotation by 90° and every mirror is one of the 8 combinations, e.g. a quarter turn clockwise is D + H.
/// </summary>
public static class TileFlip
{
    public const int H = 1, V = 2, D = 4;
    public const int RotateCw = D | H;

    /// <summary>The pixels of an n×n tile turned by the flags.</summary>
    public static uint[] Orient(uint[] tile, int n, int flip)
    {
        if (flip == 0) return tile;
        var o = new uint[tile.Length];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // walk the steps backwards (V, H, D) from the turned pixel to the pixel it comes from
                int sx = (flip & H) != 0 ? n - 1 - x : x, sy = (flip & V) != 0 ? n - 1 - y : y;
                if ((flip & D) != 0) (sx, sy) = (sy, sx);
                o[y * n + x] = tile[sy * n + sx];
            }
        return o;
    }

    // Combined[flip * 8 + op] = the flags that look like "turn by flip, then by op"; found by trying all 8 on a 2×2 tile
    static readonly int[] Combined = BuildCombined();

    static int[] BuildCombined()
    {
        uint[] probe = [0, 1, 2, 3];
        var looks = Enumerable.Range(0, 8).Select(f => Orient(probe, 2, f)).ToList();
        var table = new int[64];
        for (int f = 0; f < 8; f++)
            for (int op = 0; op < 8; op++)
                table[f * 8 + op] = looks.FindIndex(l => l.SequenceEqual(Orient(looks[f], 2, op)));
        return table;
    }

    /// <summary>The flags of a tile that is already turned by <paramref name="flip"/> and is now turned again by <paramref name="op"/>.</summary>
    public static int Then(int flip, int op) => Combined[(flip & 7) * 8 + (op & 7)];

    /// <summary>A tile's collision box after the tile is turned (one-way boxes stay one-way).</summary>
    public static TileCollision Turn(TileCollision c, int n, int flip)
    {
        var (x, y, w, h) = (c.X, c.Y, c.W, c.H);
        if ((flip & D) != 0) (x, y, w, h) = (y, x, h, w);
        if ((flip & H) != 0) x = n - x - w;
        if ((flip & V) != 0) y = n - y - h;
        return c with { X = x, Y = y, W = w, H = h };
    }
}

public sealed class MapLayer : ILayer
{
    public string Name { get; set; } = "Layer";
    public bool Visible { get; set; } = true;
    /// <summary>Tile index per cell, row-major; -1 = empty.</summary>
    public int[] Tiles { get; init; } = [];
    /// <summary><see cref="TileFlip"/> flags per cell; empty while no tile of the layer is turned.</summary>
    public byte[] Flips { get; set; } = [];

    public int FlipAt(int cell) => cell < Flips.Length ? Flips[cell] : 0;

    public void SetFlip(int cell, int flip)
    {
        if (Flips.Length == 0)
        {
            if (flip == 0) return;
            Flips = new byte[Tiles.Length];
        }
        Flips[cell] = (byte)(flip & 7);
    }
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

/// <summary>Collision rectangle inside one tile (tile pixels). OneWay = only solid from above (platforms).</summary>
public sealed record TileCollision(int X, int Y, int W, int H, bool OneWay = false, int Category = 0);

/// <summary>Layers[0] is the bottom layer.</summary>
public sealed class Tilemap
{
    public const int Empty = -1;
    /// <summary>Largest map picture in pixels, e.g. 500×500 tiles of 16 px.</summary>
    public const long MaxPixels = 64_000_000;

    public int Width { get; }
    public int Height { get; }
    public int TileSize { get; }
    public Tileset? Tileset { get; set; }
    /// <summary>Project asset the tileset comes from (ADR-0001); the pixels above stay as a copy for files used without a project.</summary>
    public Guid? TilesetAssetId { get; set; }
    /// <summary>Where the tileset was loaded from (for "reload"); pixels are stored in the map anyway.</summary>
    public string? TilesetSource { get; set; }
    public List<MapLayer> Layers { get; } = [];
    public List<MapObject> Objects { get; } = [];
    /// <summary>Auto-tile terrain rules: block start, 16- or 47-tile layout, variants (see <see cref="Autotile"/>).</summary>
    public List<TerrainRule> Terrains { get; } = [];
    /// <summary>Collision per tile index (tiles without an entry are not solid).</summary>
    public Dictionary<int, TileCollision> TileCollisions { get; } = [];

    public int NextObjectId => Objects.Count == 0 ? 1 : Objects.Max(o => o.Id) + 1;

    /// <summary>
    /// The topmost object (the last one added) at a map pixel, or null. A point object is hit within
    /// <paramref name="pointRadius"/> pixels of it, so it stays easy to click at any zoom.
    /// </summary>
    public MapObject? ObjectAt(int x, int y, double pointRadius)
    {
        for (int i = Objects.Count - 1; i >= 0; i--)
        {
            var o = Objects[i];
            bool hit = o.IsPoint
                ? Math.Abs(x - o.X) <= pointRadius && Math.Abs(y - o.Y) <= pointRadius
                : x >= o.X && y >= o.Y && x < o.X + Math.Max(1, o.W) && y < o.Y + Math.Max(1, o.H);
            if (hit) return o;
        }
        return null;
    }

    /// <summary>The tileset, for operations that cannot work without one.</summary>
    public Tileset RequireTileset() => Tileset ?? throw new InvalidOperationException("Load a tileset first.");

    public Tilemap(int width, int height, int tileSize, bool empty = false)
    {
        if (width < 1 || height < 1 || width > 1024 || height > 1024) throw new ArgumentOutOfRangeException(nameof(width), "Map size must be 1..1024 tiles.");
        if (tileSize < 1 || tileSize > 256) throw new ArgumentOutOfRangeException(nameof(tileSize), "Tile size must be 1..256.");
        // the map is drawn as one picture; beyond this it would not fit in memory (and the pixel count overflows an int)
        if ((long)width * tileSize * height * tileSize > MaxPixels)
            throw new ArgumentOutOfRangeException(nameof(width), $"A {width}×{height} map of {tileSize} px tiles is too big to draw (over {MaxPixels / 1_000_000} million pixels). Use fewer or smaller tiles.");
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
    public int Pick(int x, int y) => PickCell(x, y).Tile;

    /// <summary>Topmost visible tile at a cell with its <see cref="TileFlip"/> flags, or (Empty, 0).</summary>
    public (int Tile, int Flip) PickCell(int x, int y)
    {
        for (int i = Layers.Count - 1; i >= 0; i--)
            if (Layers[i].Visible && Layers[i].Tiles[y * Width + x] != Empty) return (Layers[i].Tiles[y * Width + x], Layers[i].FlipAt(y * Width + x));
        return (Empty, 0);
    }

    /// <summary>Renders visible layers to pixels (Width*TileSize × Height*TileSize).</summary>
    public uint[] Render()
    {
        var dst = new uint[Width * TileSize * Height * TileSize];
        RenderCells(dst, 0, 0, Width, Height);
        return dst;
    }

    /// <summary>
    /// Renders only a rectangle of cells into a picture of the whole map (as made by <see cref="Render"/>), so an
    /// editor can redraw just what a stroke changed. Cells do not affect each other, so the result is exact.
    /// </summary>
    public void RenderCells(uint[] into, int x, int y, int w, int h)
    {
        int size = TileSize, pw = Width * size;
        int x1 = Math.Min(Width, x + w), y1 = Math.Min(Height, y + h);
        (x, y) = (Math.Max(0, x), Math.Max(0, y));
        var ts = Tileset is { } set && set.TileSize == size ? set : null;
        var tiles = new Dictionary<int, uint[]>();   // key: tile * 8 + flip
        for (int cy = y; cy < y1; cy++)
            for (int cx = x; cx < x1; cx++)
            {
                int cell = cy * Width + cx, at = cy * size * pw + cx * size;
                for (int py = 0; py < size; py++) Array.Clear(into, at + py * pw, size);
                if (ts is null) continue;
                foreach (var l in Layers)   // bottom to top
                {
                    int t = l.Tiles[cell], flip = l.FlipAt(cell);
                    if (!l.Visible || t < 0 || t >= ts.Count) continue;
                    if (!tiles.TryGetValue(t * 8 + flip, out var tp)) tiles[t * 8 + flip] = tp = TileFlip.Orient(ts.Tile(t), size, flip);
                    for (int py = 0; py < size; py++)
                        for (int px = 0; px < size; px++)
                            into[at + py * pw + px] = Compositor.Over(into[at + py * pw + px], tp[py * size + px]);
                }
            }
    }

    public Tilemap Clone()
    {
        var m = new Tilemap(Width, Height, TileSize, empty: true) { Tileset = Tileset, TilesetSource = TilesetSource, TilesetAssetId = TilesetAssetId };
        foreach (var l in Layers) m.Layers.Add(new MapLayer { Name = l.Name, Visible = l.Visible, Tiles = (int[])l.Tiles.Clone(), Flips = (byte[])l.Flips.Clone() });
        m.Objects.AddRange(Objects.Select(o => o.Clone()));
        m.Terrains.AddRange(Terrains.Select(t => t.Clone()));
        foreach (var kv in TileCollisions) m.TileCollisions[kv.Key] = kv.Value;
        return m;
    }

    /// <summary>New map size in tiles; content moves by (ox, oy) tiles and is clipped. Objects move along.</summary>
    public Tilemap Resized(int w, int h, int ox, int oy)
    {
        var m = new Tilemap(w, h, TileSize, empty: true) { Tileset = Tileset, TilesetSource = TilesetSource, TilesetAssetId = TilesetAssetId };
        foreach (var l in Layers)
        {
            var nl = m.AddLayer(m.Layers.Count, l.Name);
            nl.Visible = l.Visible;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    if (!Tools.InBounds(w, h, x + ox, y + oy)) continue;
                    nl.Tiles[(y + oy) * w + x + ox] = l.Tiles[y * Width + x];
                    nl.SetFlip((y + oy) * w + x + ox, l.FlipAt(y * Width + x));
                }
        }
        foreach (var o in Objects)
        {
            var c = o.Clone();
            c.X += ox * TileSize;
            c.Y += oy * TileSize;
            m.Objects.Add(c);
        }
        m.Terrains.AddRange(Terrains.Select(t => t.Clone()));
        foreach (var kv in TileCollisions) m.TileCollisions[kv.Key] = kv.Value;
        return m;
    }

    // ---------- .pxm file (JSON) ----------

    record LayerDto(string Name, bool Visible, int[] Tiles, byte[]? Flips = null);
    record ObjectDto(int Id, string Name, string Type, int X, int Y, int W, int H);
    record RuleDto(int Base, TerrainMode Mode, int[]? Variants);
    record CollisionDto(int Tile, int X, int Y, int W, int H, bool OneWay, int Category);
    // v2 added Objects and Terrains (block bases), v3 TilesetAssetId, v4 TerrainRules (mode + variants)
    // replacing Terrains; all optional so older files still load
    record MapDto(int Version, int Width, int Height, int TileSize, string? TilesetSource, int TilesetWidth, int TilesetHeight, string? TilesetPixels,
        LayerDto[] Layers, ObjectDto[]? Objects = null, int[]? Terrains = null, Guid? TilesetAssetId = null, RuleDto[]? TerrainRules = null,
        CollisionDto[]? TileCollisions = null);

    // v5: TileCollisions; v6: Flips per layer (left out while no tile is turned)
    public const int Version = 6;

    /// <summary>The layer's flags for the file: null when nothing is turned; empty cells never carry flags.</summary>
    static byte[]? FlipsToSave(MapLayer l)
    {
        var flips = l.Tiles.Select((t, i) => t < 0 ? (byte)0 : (byte)l.FlipAt(i)).ToArray();
        return flips.Any(f => f != 0) ? flips : null;
    }

    public static string Serialize(Tilemap m) => JsonSerializer.Serialize(new MapDto(
        Version, m.Width, m.Height, m.TileSize, m.TilesetSource, m.Tileset?.Width ?? 0, m.Tileset?.Height ?? 0,
        m.Tileset is { } ts ? Convert.ToBase64String(MemoryMarshal.AsBytes(ts.Pixels.AsSpan())) : null,
        [.. m.Layers.Select(l => new LayerDto(l.Name, l.Visible, l.Tiles, FlipsToSave(l)))],
        [.. m.Objects.Select(o => new ObjectDto(o.Id, o.Name, o.Type, o.X, o.Y, o.W, o.H))],
        null, m.TilesetAssetId, [.. m.Terrains.Select(t => new RuleDto(t.Base, t.Mode, [.. t.Variants]))],
        [.. m.TileCollisions.OrderBy(kv => kv.Key).Select(kv => new CollisionDto(kv.Key, kv.Value.X, kv.Value.Y, kv.Value.W, kv.Value.H, kv.Value.OneWay, kv.Value.Category))]));

    public static Tilemap Deserialize(string json)
    {
        var d = JsonSerializer.Deserialize<MapDto>(json) ?? throw new InvalidDataException("Empty map file.");
        var m = new Tilemap(d.Width, d.Height, d.TileSize, empty: true) { TilesetSource = d.TilesetSource, TilesetAssetId = d.TilesetAssetId };
        if (d.TilesetPixels is not null)
        {
            var px = MemoryMarshal.Cast<byte, uint>(Convert.FromBase64String(d.TilesetPixels)).ToArray();
            if (px.Length != d.TilesetWidth * d.TilesetHeight) throw new InvalidDataException("Tileset size does not match its pixels.");
            m.Tileset = new Tileset(px, d.TilesetWidth, d.TilesetHeight, d.TileSize);
        }
        foreach (var l in d.Layers)
        {
            if (l.Tiles.Length != d.Width * d.Height) throw new InvalidDataException($"Layer '{l.Name}' has wrong size.");
            if (l.Flips is { } flips && (flips.Length != l.Tiles.Length || flips.Any(f => f > 7)))
                throw new InvalidDataException($"Layer '{l.Name}' has wrong tile flips.");
            m.Layers.Add(new MapLayer { Name = l.Name, Visible = l.Visible, Tiles = l.Tiles, Flips = l.Flips ?? [] });
        }
        if (m.Layers.Count == 0) throw new InvalidDataException("Map has no layers.");
        foreach (var o in d.Objects ?? [])
            m.Objects.Add(new MapObject { Id = o.Id, Name = o.Name ?? "", Type = o.Type ?? "", X = o.X, Y = o.Y, W = Math.Max(0, o.W), H = Math.Max(0, o.H) });
        if (d.TerrainRules is { } rules)
            m.Terrains.AddRange(rules.Select(r => new TerrainRule { Base = r.Base, Mode = Enum.IsDefined(r.Mode) ? r.Mode : TerrainMode.Blob16, Variants = [.. r.Variants ?? []] }));
        else
            m.Terrains.AddRange((d.Terrains ?? []).Select(b => new TerrainRule { Base = b }));   // v2/v3: 16-tile blocks
        foreach (var c in d.TileCollisions ?? [])
        {
            if (c.W < 1 || c.H < 1 || c.X < 0 || c.Y < 0 || c.X + c.W > d.TileSize || c.Y + c.H > d.TileSize || c.Category is < 0 or > 31)
                throw new InvalidDataException($"The collision of tile {c.Tile} does not fit in a {d.TileSize}px tile.");
            m.TileCollisions[c.Tile] = new TileCollision(c.X, c.Y, c.W, c.H, c.OneWay, c.Category);
        }
        return m;
    }

    public static void Save(Tilemap m, string path) => DocumentFile.Save(path, Serialize(m), Version);
    public static Tilemap Load(string path) => DocumentFile.Load(path, "map", Version, Deserialize);
}
