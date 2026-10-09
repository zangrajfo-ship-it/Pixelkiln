using System.Text.Json;

namespace PixelForge.Core;

/// <summary>Tiled map editor JSON (.tmj). gid = tile index + 1, 0 = empty.</summary>
public static class TiledExport
{
    public static string Json(Tilemap m, string tilesetImage)
    {
        var ts = m.Tileset ?? throw new InvalidOperationException("Load a tileset first.");
        return JsonSerializer.Serialize(new
        {
            type = "map", version = "1.10", tiledversion = "1.10.2",
            orientation = "orthogonal", renderorder = "right-down", infinite = false,
            width = m.Width, height = m.Height, tilewidth = m.TileSize, tileheight = m.TileSize,
            nextlayerid = m.Layers.Count + 1, nextobjectid = 1,
            layers = m.Layers.Select((l, i) => new
            {
                id = i + 1, name = l.Name, type = "tilelayer", visible = l.Visible, opacity = 1,
                x = 0, y = 0, width = m.Width, height = m.Height,
                data = l.Tiles.Select(t => t < 0 ? 0 : t + 1),
            }),
            tilesets = new[]
            {
                new
                {
                    firstgid = 1, name = Path.GetFileNameWithoutExtension(tilesetImage), image = tilesetImage,
                    imagewidth = ts.Width, imageheight = ts.Height, tilewidth = m.TileSize, tileheight = m.TileSize,
                    columns = ts.Columns, tilecount = ts.Count, margin = 0, spacing = 0,
                },
            },
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
