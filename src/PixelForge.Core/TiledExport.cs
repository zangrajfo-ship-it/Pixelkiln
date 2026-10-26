using System.Text.Json;

namespace PixelForge.Core;

/// <summary>Tiled map editor JSON (.tmj). gid = tile index + 1, 0 = empty.</summary>
public static class TiledExport
{
    public static string Json(Tilemap m, string tilesetImage)
    {
        var ts = m.RequireTileset();
        return JsonSerializer.Serialize(new
        {
            type = "map", version = "1.10", tiledversion = "1.10.2",
            orientation = "orthogonal", renderorder = "right-down", infinite = false,
            width = m.Width, height = m.Height, tilewidth = m.TileSize, tileheight = m.TileSize,
            nextlayerid = m.Layers.Count + 2, nextobjectid = m.NextObjectId,
            layers = m.Layers.Select((l, i) => (object)new
            {
                id = i + 1, name = l.Name, type = "tilelayer", visible = l.Visible, opacity = 1,
                x = 0, y = 0, width = m.Width, height = m.Height,
                data = l.Tiles.Select(t => t < 0 ? 0 : t + 1),
            }).Append(new
            {
                id = m.Layers.Count + 1, name = "Objects", type = "objectgroup", visible = true, opacity = 1,
                x = 0, y = 0, draworder = "topdown",
                objects = m.Objects.Select(o => new
                {
                    id = o.Id, name = o.Name, type = o.Type, x = o.X, y = o.Y, width = o.W, height = o.H,
                    rotation = 0, visible = true, point = o.IsPoint,
                }),
            }),
            tilesets = new[]
            {
                new
                {
                    firstgid = 1, name = Path.GetFileNameWithoutExtension(tilesetImage), image = tilesetImage,
                    imagewidth = ts.Width, imageheight = ts.Height, tilewidth = m.TileSize, tileheight = m.TileSize,
                    columns = ts.Columns, tilecount = ts.Count, margin = 0, spacing = 0,
                    // per-tile collision shapes, the way Tiled's collision editor stores them
                    tiles = m.TileCollisions.OrderBy(kv => kv.Key).Select(kv => new
                    {
                        id = kv.Key,
                        objectgroup = new
                        {
                            draworder = "index", name = "", opacity = 1, type = "objectgroup", visible = true, x = 0, y = 0,
                            objects = new[]
                            {
                                new
                                {
                                    id = 1, name = "", type = kv.Value.OneWay ? "oneway" : "solid", rotation = 0, visible = true,
                                    x = kv.Value.X, y = kv.Value.Y, width = kv.Value.W, height = kv.Value.H,
                                    properties = new[] { new { name = "category", type = "int", value = kv.Value.Category } },
                                },
                            },
                        },
                    }),
                },
            },
        }, DocumentFile.Indented);
    }
}
