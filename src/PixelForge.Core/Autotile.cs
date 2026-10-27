namespace PixelForge.Core;

public enum TerrainMode
{
    /// <summary>4×4 block; edges only (N, E, S, W).</summary>
    Blob16,
    /// <summary>8×6 block (47 used); edges and inner corners.</summary>
    Blob47,
}

/// <summary>An auto-tile terrain: a block of tiles in the tileset plus optional random variants of the fully surrounded tile.</summary>
public sealed class TerrainRule
{
    /// <summary>Top-left tile index of the block.</summary>
    public int Base { get; set; }
    public TerrainMode Mode { get; set; } = TerrainMode.Blob16;
    /// <summary>Extra tiles used at random (stable per cell) where the terrain is surrounded on all sides.</summary>
    public List<int> Variants { get; set; } = [];

    public TerrainRule Clone() => new() { Base = Base, Mode = Mode, Variants = [.. Variants] };
}

/// <summary>
/// Auto-tiling. Blob16: tile = block column mask%4, row mask/4 with mask = N·1 + E·2 + S·4 + W·8.
/// Blob47: corners NE·16, SE·32, SW·64, NW·128 count only when both touching edges are set; the 47 resulting
/// masks, sorted ascending, fill the 8×6 block row by row (<see cref="Masks47"/>, see <see cref="Template"/>).
/// </summary>
public static class Autotile
{
    public const int N = 1, E = 2, S = 4, W = 8, NE = 16, SE = 32, SW = 64, NW = 128;

    /// <summary>The 47 distinct masks after removing corners that have a missing edge, ascending.</summary>
    public static readonly int[] Masks47 = [.. Enumerable.Range(0, 256).Select(Reduce).Distinct().Order()];

    static readonly Dictionary<int, int> Index47 = Masks47.Select((m, i) => (m, i)).ToDictionary(p => p.m, p => p.i);

    /// <summary>Drops corner bits whose two neighbouring edges are not both present.</summary>
    public static int Reduce(int m)
    {
        if ((m & (N | E)) != (N | E)) m &= ~NE;
        if ((m & (S | E)) != (S | E)) m &= ~SE;
        if ((m & (S | W)) != (S | W)) m &= ~SW;
        if ((m & (N | W)) != (N | W)) m &= ~NW;
        return m;
    }

    static (int Cols, int Rows) Block(TerrainMode mode) => mode == TerrainMode.Blob47 ? (8, 6) : (4, 4);

    public static void Validate(Tileset ts, TerrainRule rule)
    {
        var (bc, br) = Block(rule.Mode);
        if (rule.Base < 0 || rule.Base % ts.Columns + bc > ts.Columns || rule.Base / ts.Columns + br > ts.Rows)
            throw new InvalidOperationException($"A {(rule.Mode == TerrainMode.Blob47 ? "47-tile" : "16-tile")} terrain needs a full {bc}×{br} block of tiles inside the tileset.");
        if (rule.Variants.Any(v => v < 0 || v >= ts.Count)) throw new InvalidOperationException("A terrain variant points outside the tileset.");
    }

    /// <summary>Blob16 tile for a mask, counted from the block's top-left tile.</summary>
    public static int TileFor(Tileset ts, int terrainBase, int mask) => terrainBase + mask / 4 * ts.Columns + mask % 4;

    public static int TileFor(Tileset ts, TerrainRule rule, int mask)
    {
        if (rule.Mode == TerrainMode.Blob16) return TileFor(ts, rule.Base, mask & 15);
        int i = Index47[Reduce(mask)];
        return rule.Base + i / 8 * ts.Columns + i % 8;
    }

    public static bool IsTerrain(Tileset ts, TerrainRule rule, int tile)
    {
        if (tile < 0) return false;
        if (rule.Variants.Contains(tile)) return true;
        var (bc, br) = Block(rule.Mode);
        int dc = tile % ts.Columns - rule.Base % ts.Columns, dr = tile / ts.Columns - rule.Base / ts.Columns;
        return Tools.InBounds(bc, br, dc, dr);
    }

    /// <summary>Paints (on) or clears (off) a terrain cell and re-picks it and its 8 neighbours.</summary>
    public static void Apply(Tilemap map, MapLayer layer, TerrainRule rule, int x, int y, bool on)
    {
        var ts = map.RequireTileset();
        Validate(ts, rule);
        if (!Tools.InBounds(map.Width, map.Height, x, y)) return;
        layer.Tiles[y * map.Width + x] = on ? rule.Base : Tilemap.Empty;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++) Update(x + dx, y + dy);

        void Update(int cx, int cy)
        {
            if (!Is(cx, cy)) return;
            int mask = (Is(cx, cy - 1) ? N : 0) | (Is(cx + 1, cy) ? E : 0) | (Is(cx, cy + 1) ? S : 0) | (Is(cx - 1, cy) ? W : 0)
                     | (Is(cx + 1, cy - 1) ? NE : 0) | (Is(cx + 1, cy + 1) ? SE : 0) | (Is(cx - 1, cy + 1) ? SW : 0) | (Is(cx - 1, cy - 1) ? NW : 0);
            int tile = TileFor(ts, rule, mask);
            bool full = rule.Mode == TerrainMode.Blob16 ? (mask & 15) == 15 : Reduce(mask) == 255;
            if (full && rule.Variants.Count > 0)
            {
                // stable per cell, so repainting a neighbour does not reshuffle the variants
                int pick = (int)((uint)(cx * 73856093 ^ cy * 19349663) % (uint)(rule.Variants.Count + 1));
                if (pick > 0) tile = rule.Variants[pick - 1];
            }
            layer.Tiles[cy * map.Width + cx] = tile;
        }

        bool Is(int cx, int cy) =>
            Tools.InBounds(map.Width, map.Height, cx, cy) && IsTerrain(ts, rule, layer.Tiles[cy * map.Width + cx]);
    }

    /// <summary>A guide image for the block: each tile shows which sides (and corners, for 47) connect.</summary>
    public static uint[] Template(int tileSize, TerrainMode mode)
    {
        const uint Bg = 0xFF3A3A3A, Fill = 0xFF6ABE30, Corner = 0xFF37946E, Line = 0xFF222034;
        var (bc, br) = Block(mode);
        int w = tileSize * bc, h = tileSize * br;
        var px = new uint[w * h];
        int a = tileSize / 4, b = tileSize - a;   // inner square [a, b)
        int cells = mode == TerrainMode.Blob47 ? Masks47.Length : 16;
        for (int c = 0; c < bc * br; c++)
        {
            int ox = c % bc * tileSize, oy = c / bc * tileSize;
            int mask = c < cells ? (mode == TerrainMode.Blob47 ? Masks47[c] : c) : -1;
            for (int y = 0; y < tileSize; y++)
                for (int x = 0; x < tileSize; x++)
                {
                    uint color = Bg;
                    if (mask >= 0)
                    {
                        bool inner = x >= a && x < b && y >= a && y < b;
                        bool arm = (mask & N) != 0 && y < a && x >= a && x < b
                                || (mask & S) != 0 && y >= b && x >= a && x < b
                                || (mask & W) != 0 && x < a && y >= a && y < b
                                || (mask & E) != 0 && x >= b && y >= a && y < b;
                        bool corner = (mask & NE) != 0 && x >= b && y < a || (mask & SE) != 0 && x >= b && y >= b
                                   || (mask & SW) != 0 && x < a && y >= b || (mask & NW) != 0 && x < a && y < a;
                        color = inner || arm ? Fill : corner ? Corner : Bg;
                    }
                    if (x == 0 || y == 0 || x == tileSize - 1 || y == tileSize - 1) color = Line;
                    px[(oy + y) * w + ox + x] = color;
                }
        }
        return px;
    }

    /// <summary>Pixel size of the template image for a mode.</summary>
    public static (int Width, int Height) TemplateSize(int tileSize, TerrainMode mode)
    {
        var (c, r) = Block(mode);
        return (c * tileSize, r * tileSize);
    }
}
