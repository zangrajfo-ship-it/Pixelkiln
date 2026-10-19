using System.Text.Json;

namespace PixelForge.Core;

/// <summary>
/// Auto-tile rules as JSON, so a game can repeat PixelForge's choices at runtime:
/// for each terrain the mask → tile table (mask bits N1 E2 S4 W8 NE16 SE32 SW64 NW128).
/// </summary>
public static class TerrainExport
{
    public static string Json(Tilemap map)
    {
        var ts = map.Tileset ?? throw new InvalidOperationException("Load a tileset first.");
        return JsonSerializer.Serialize(new
        {
            format = "pixelforge-terrains", version = 1, tileSize = map.TileSize, columns = ts.Columns,
            bits = new { N = Autotile.N, E = Autotile.E, S = Autotile.S, W = Autotile.W, NE = Autotile.NE, SE = Autotile.SE, SW = Autotile.SW, NW = Autotile.NW },
            terrains = map.Terrains.Select((t, i) => new
            {
                index = i,
                mode = t.Mode.ToString(),
                @base = t.Base,
                variants = t.Variants,
                // masks are reduced the same way as in the editor (corners only count with both edges)
                tiles = (t.Mode == TerrainMode.Blob47 ? Autotile.Masks47 : Enumerable.Range(0, 16))
                    .ToDictionary(m => m.ToString(), m => Autotile.TileFor(ts, t, m)),
            }),
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
