namespace PixelForge.Core;

/// <summary>
/// 16-tile auto-tiling. A terrain is a 4×4 block in the tileset whose top-left tile index is <c>terrainBase</c>.
/// Each cell's tile is chosen from which of its 4 neighbors are the same terrain:
/// mask = N·1 + E·2 + S·4 + W·8, tile = block column (mask % 4), block row (mask / 4).
/// </summary>
public static class Autotile
{
    public const int N = 1, E = 2, S = 4, W = 8;

    public static void Validate(Tileset ts, int terrainBase)
    {
        if (terrainBase < 0 || terrainBase % ts.Columns + 4 > ts.Columns || terrainBase / ts.Columns + 4 > ts.Rows)
            throw new InvalidOperationException("A terrain needs a full 4×4 block of tiles inside the tileset.");
    }

    public static int TileFor(Tileset ts, int terrainBase, int mask) => terrainBase + mask / 4 * ts.Columns + mask % 4;

    public static bool IsTerrain(Tileset ts, int terrainBase, int tile)
    {
        if (tile < 0) return false;
        int dc = tile % ts.Columns - terrainBase % ts.Columns, dr = tile / ts.Columns - terrainBase / ts.Columns;
        return dc is >= 0 and < 4 && dr is >= 0 and < 4;
    }

    /// <summary>Paints (on) or clears (off) a terrain cell and re-picks it and its 4 neighbors.</summary>
    public static void Apply(Tilemap map, MapLayer layer, int terrainBase, int x, int y, bool on)
    {
        var ts = map.Tileset ?? throw new InvalidOperationException("Load a tileset first.");
        Validate(ts, terrainBase);
        if (!Tools.InBounds(map.Width, map.Height, x, y)) return;
        layer.Tiles[y * map.Width + x] = on ? terrainBase : Tilemap.Empty;
        Update(x, y);
        Update(x, y - 1); Update(x + 1, y); Update(x, y + 1); Update(x - 1, y);

        void Update(int cx, int cy)
        {
            if (!Is(cx, cy)) return;
            int mask = (Is(cx, cy - 1) ? N : 0) | (Is(cx + 1, cy) ? E : 0) | (Is(cx, cy + 1) ? S : 0) | (Is(cx - 1, cy) ? W : 0);
            layer.Tiles[cy * map.Width + cx] = TileFor(ts, terrainBase, mask);
        }

        bool Is(int cx, int cy) =>
            Tools.InBounds(map.Width, map.Height, cx, cy) && IsTerrain(ts, terrainBase, layer.Tiles[cy * map.Width + cx]);
    }

    /// <summary>A 4×4 guide image: each tile shows which sides connect, so the user knows what to draw where.</summary>
    public static uint[] Template(int tileSize)
    {
        const uint Bg = 0xFF3A3A3A, Fill = 0xFF6ABE30, Line = 0xFF222034;
        int w = tileSize * 4;
        var px = new uint[w * w];
        int a = tileSize / 4, b = tileSize - a;   // inner square [a, b)
        for (int mask = 0; mask < 16; mask++)
        {
            int ox = mask % 4 * tileSize, oy = mask / 4 * tileSize;
            for (int y = 0; y < tileSize; y++)
                for (int x = 0; x < tileSize; x++)
                {
                    bool inner = x >= a && x < b && y >= a && y < b;
                    bool arm = (mask & N) != 0 && y < a && x >= a && x < b
                            || (mask & S) != 0 && y >= b && x >= a && x < b
                            || (mask & W) != 0 && x < a && y >= a && y < b
                            || (mask & E) != 0 && x >= b && y >= a && y < b;
                    bool border = x == 0 || y == 0 || x == tileSize - 1 || y == tileSize - 1;
                    px[(oy + y) * w + ox + x] = border ? Line : inner || arm ? Fill : Bg;
                }
        }
        return px;
    }
}
