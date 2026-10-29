using System.Text.Json;
using Pixelkiln.Core;

namespace Pixelkiln.Core.Tests;

public class CollisionTests
{
    [Fact]
    public void Tile_collisions_round_trip_validate_and_export_to_tiled()
    {
        var m = new Tilemap(2, 1, 8) { Tileset = new Tileset(new uint[16 * 8], 16, 8, 8) };
        m.TileCollisions[1] = new TileCollision(0, 0, 8, 4, OneWay: true, Category: 2);
        var t = Tilemap.Deserialize(Tilemap.Serialize(m));
        Assert.Equal(new TileCollision(0, 0, 8, 4, true, 2), t.TileCollisions[1]);
        Assert.Throws<InvalidDataException>(() => Tilemap.Deserialize(Tilemap.Serialize(m).Replace("\"W\":8", "\"W\":9")));

        var tile = JsonDocument.Parse(TiledExport.Json(m, "t.png")).RootElement.GetProperty("tilesets")[0].GetProperty("tiles")[0];
        Assert.Equal(1, tile.GetProperty("id").GetInt32());
        var obj = tile.GetProperty("objectgroup").GetProperty("objects")[0];
        Assert.Equal(("oneway", 8, 4), (obj.GetProperty("type").GetString(), obj.GetProperty("width").GetInt32(), obj.GetProperty("height").GetInt32()));
    }

    [Fact]
    public void Duplicate_removal_keeps_collisions_and_masks_are_symmetric()
    {
        uint R = 0xFFFF0000;
        var m = new Tilemap(1, 1, 1) { Tileset = new Tileset([R, 0, R], 3, 1, 1) };
        m.TileCollisions[1] = new TileCollision(0, 0, 1, 1);
        m.TileCollisions[2] = new TileCollision(0, 0, 1, 1, OneWay: true);   // 2 is a copy of 0 and goes away
        TilesetTools.RemoveDuplicates(m);
        Assert.Equal([1], m.TileCollisions.Keys);

        var s = new ProjectSettings();
        Assert.True(s.Collides(1, 2));
        s.CollisionMasks[1] = 1u << 0;            // player only hits the world
        Assert.False(s.Collides(1, 2));
        Assert.False(s.Collides(2, 1));
        Assert.True(s.Collides(1, 0));
    }
}
