namespace PixelForge.Core;

/// <summary>Tileset generator helpers: duplicate tiles, compaction with map remapping, repeat preview.</summary>
public static class TilesetTools
{
    /// <summary>Groups of tile indices whose pixels are identical (each group has 2+ tiles, first = kept).</summary>
    public static List<List<int>> Duplicates(Tileset ts)
    {
        var byHash = new Dictionary<string, List<int>>();
        for (int i = 0; i < ts.Count; i++)
        {
            string key = Convert.ToBase64String(System.Runtime.InteropServices.MemoryMarshal.AsBytes(ts.Tile(i).AsSpan()));
            if (!byHash.TryGetValue(key, out var list)) byHash[key] = list = [];
            list.Add(i);
        }
        return [.. byHash.Values.Where(l => l.Count > 1)];
    }

    /// <summary>
    /// Builds a tileset without duplicate tiles (kept in the old order, same column count) and rewrites the
    /// map's tile indices and terrain rules to match. Returns how many tiles were removed.
    /// </summary>
    public static int RemoveDuplicates(Tilemap map)
    {
        var ts = map.Tileset ?? throw new InvalidOperationException("Load a tileset first.");
        var remap = Enumerable.Range(0, ts.Count).ToArray();
        foreach (var group in Duplicates(ts))
            foreach (int dup in group.Skip(1)) remap[dup] = group[0];
        var keep = Enumerable.Range(0, ts.Count).Where(i => remap[i] == i).ToList();
        if (keep.Count == ts.Count) return 0;
        var newIndex = new int[ts.Count];
        for (int k = 0; k < keep.Count; k++) newIndex[keep[k]] = k;

        int cols = Math.Min(ts.Columns, keep.Count), rows = (keep.Count + cols - 1) / cols, w = cols * ts.TileSize, h = rows * ts.TileSize;
        var px = new uint[w * h];
        for (int k = 0; k < keep.Count; k++)
        {
            var tile = ts.Tile(keep[k]);
            int ox = k % cols * ts.TileSize, oy = k / cols * ts.TileSize;
            for (int y = 0; y < ts.TileSize; y++) Array.Copy(tile, y * ts.TileSize, px, (oy + y) * w + ox, ts.TileSize);
        }
        foreach (var l in map.Layers)
            for (int i = 0; i < l.Tiles.Length; i++)
                if (l.Tiles[i] >= 0 && l.Tiles[i] < ts.Count) l.Tiles[i] = newIndex[remap[l.Tiles[i]]];
        var collisions = map.TileCollisions.Where(kv => kv.Key < ts.Count && remap[kv.Key] == kv.Key).ToList();
        map.TileCollisions.Clear();
        foreach (var (tile, c) in collisions) map.TileCollisions[newIndex[tile]] = c;
        map.Tileset = new Tileset(px, w, h, ts.TileSize);
        map.TilesetAssetId = null;   // the map now carries its own, compacted tileset
        // terrain blocks rely on fixed positions; a compacted tileset breaks them, so they are dropped
        map.Terrains.Clear();
        return ts.Count - keep.Count;
    }

    /// <summary>A block of tiles repeated n×n times, to judge seams of repeating patterns.</summary>
    public static (uint[] Pixels, int Width, int Height) Repeat(Tileset ts, int x, int y, int w, int h, int times)
    {
        int bw = w * ts.TileSize, bh = h * ts.TileSize;
        var block = PixelOps.Crop(ts.Pixels, ts.Width, x * ts.TileSize, y * ts.TileSize, bw, bh);
        int ow = bw * times, oh = bh * times;
        var px = new uint[ow * oh];
        for (int ty = 0; ty < times; ty++)
            for (int tx = 0; tx < times; tx++)
                for (int row = 0; row < bh; row++) Array.Copy(block, row * bw, px, (ty * bh + row) * ow + tx * bw, bw);
        return (px, ow, oh);
    }
}
