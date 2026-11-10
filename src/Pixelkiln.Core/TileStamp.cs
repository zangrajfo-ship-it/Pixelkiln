namespace Pixelkiln.Core;

/// <summary>
/// The brush of the tilemap editor: a block of tile indices (-1 = a hole that paints nothing) with their
/// <see cref="TileFlip"/> flags, taken from the tileset or picked from the map.
/// </summary>
public sealed record TileStamp(int[] Tiles, byte[] Flips, int Width, int Height)
{
    /// <summary>One tile, not turned.</summary>
    public static TileStamp Single(int tile) => new([tile], [0], 1, 1);

    /// <summary>A rectangle of the tileset (in tiles); tiles are numbered row by row.</summary>
    public static TileStamp FromTileset(int x, int y, int w, int h, int columns)
    {
        var tiles = new int[w * h];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++) tiles[j * w + i] = (y + j) * columns + x + i;
        return new(tiles, new byte[w * h], w, h);
    }

    /// <summary>The topmost visible tiles of a rectangle of the map, with the way they are turned.</summary>
    public static TileStamp FromMap(Tilemap map, int x, int y, int w, int h)
    {
        var tiles = new int[w * h];
        var flips = new byte[w * h];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                var (tile, flip) = map.PickCell(x + i, y + j);
                (tiles[j * w + i], flips[j * w + i]) = (tile, (byte)flip);
            }
        return new(tiles, flips, w, h);
    }

    /// <summary>Nothing to paint (only holes).</summary>
    public bool IsEmpty => Tiles.All(t => t < 0);

    /// <summary>
    /// The whole block mirrored or rotated: the cells are rearranged and every tile is turned the same way.
    /// <paramref name="op"/> is TileFlip.H, TileFlip.V or TileFlip.RotateCw (a quarter turn clockwise).
    /// </summary>
    public TileStamp Turned(int op)
    {
        bool rotate = op == TileFlip.RotateCw;
        int w = rotate ? Height : Width, h = rotate ? Width : Height;
        var tiles = new int[w * h];
        var flips = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // the old cell that lands on (x, y)
                int from = rotate ? (Height - 1 - x) * Width + y
                    : op == TileFlip.H ? y * Width + (Width - 1 - x)
                    : (Height - 1 - y) * Width + x;
                tiles[y * w + x] = Tiles[from];
                flips[y * w + x] = (byte)TileFlip.Then(Flips[from], op);
            }
        return new(tiles, flips, w, h);
    }

    /// <summary>
    /// The stamp cell that covers a map cell when the stamp repeats over the map from an origin
    /// (a pattern fill): (dx, dy) is the map cell minus the origin and may be negative.
    /// </summary>
    public int CellAt(int dx, int dy) => Tools.Wrap(dy, Height) * Width + Tools.Wrap(dx, Width);

    /// <summary>Paints the block with its top-left cell at (x, y); holes and cells outside the map are skipped.</summary>
    public void PaintAt(MapLayer layer, int mapWidth, int mapHeight, int x, int y)
    {
        for (int j = 0; j < Height; j++)
            for (int i = 0; i < Width; i++)
            {
                if (Tiles[j * Width + i] < 0 || !Tools.InBounds(mapWidth, mapHeight, x + i, y + j)) continue;
                int cell = (y + j) * mapWidth + x + i;
                layer.Tiles[cell] = Tiles[j * Width + i];
                layer.SetFlip(cell, Flips[j * Width + i]);
            }
    }

    /// <summary>
    /// Flood fill from (x, y) with the repeating block (or, with <paramref name="clear"/>, with empty cells).
    /// Returns the bounds of the changed cells (x, y, width, height), or null when the start is outside the map.
    /// </summary>
    public (int X, int Y, int W, int H)? Fill(MapLayer layer, int mapWidth, int mapHeight, int x, int y, bool clear = false)
    {
        if (!Tools.InBounds(mapWidth, mapHeight, x, y)) return null;
        var area = Tools.Region(layer.Tiles, mapWidth, mapHeight, x, y);   // the touching cells with the same tile
        int x0 = x, y0 = y, x1 = x, y1 = y;
        for (int i = 0; i < area.Length; i++)
        {
            if (!area[i]) continue;
            int cx = i % mapWidth, cy = i / mapWidth, from = CellAt(cx - x, cy - y);
            layer.Tiles[i] = clear ? Tilemap.Empty : Tiles[from];
            layer.SetFlip(i, clear ? 0 : Flips[from]);
            (x0, y0, x1, y1) = (Math.Min(x0, cx), Math.Min(y0, cy), Math.Max(x1, cx), Math.Max(y1, cy));
        }
        return (x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }
}
