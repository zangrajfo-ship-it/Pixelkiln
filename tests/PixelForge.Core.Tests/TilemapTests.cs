using System.Text.Json;
using PixelForge.Core;

namespace PixelForge.Core.Tests;

public class TilemapTests
{
    // 4×2 px tileset, tile size 2 -> 2 tiles: red, blue (blue's top-left pixel transparent)
    const uint R = 0xFFFF0000, B = 0xFF0000FF;
    static Tileset Set() => new([R, R, 0, B, R, R, B, B], 4, 2, 2);

    [Fact]
    public void Tileset_slices_tiles()
    {
        var ts = Set();
        Assert.Equal(2, ts.Count);
        Assert.Equal([0u, B, B, B], ts.Tile(1));
    }

    [Fact]
    public void Render_stacks_layers_with_transparency()
    {
        var m = new Tilemap(2, 1, 2) { Tileset = Set() };
        Tools.Fill(m.Layers[0].Tiles, 2, 1, 0, 0, 0);       // red everywhere
        m.AddLayer(1, "top").Tiles[0] = 1;                   // blue on cell 0
        var px = m.Render();                                 // 4×2 px
        Assert.Equal(R, px[0]);                              // blue tile's transparent pixel shows red
        Assert.Equal(B, px[1]);
        Assert.Equal(R, px[2]);
        Assert.Equal(1, m.Pick(0, 0));
        m.Layers[1].Visible = false;
        Assert.Equal(0, m.Pick(0, 0));
    }

    [Fact]
    public void Map_file_round_trip()
    {
        var m = new Tilemap(3, 2, 2) { Tileset = Set(), TilesetSource = "tiles.png" };
        m.Layers[0].Tiles[4] = 1;
        m.AddLayer(1, "deco").Visible = false;
        var t = Tilemap.Deserialize(Tilemap.Serialize(m));
        Assert.Equal((3, 2, 2), (t.Width, t.Height, t.TileSize));
        Assert.Equal(1, t.Layers[0].Tiles[4]);
        Assert.False(t.Layers[1].Visible);
        Assert.Equal(m.Tileset!.Pixels, t.Tileset!.Pixels);
        Assert.Equal("tiles.png", t.TilesetSource);
    }

    [Fact]
    public void Tiled_json_uses_gids()
    {
        var m = new Tilemap(3, 2, 2) { Tileset = Set() };
        m.Layers[0].Tiles[0] = 0;
        m.Layers[0].Tiles[5] = 1;
        var root = JsonDocument.Parse(TiledExport.Json(m, "tiles.png")).RootElement;
        Assert.Equal(3, root.GetProperty("width").GetInt32());
        Assert.Equal(2, root.GetProperty("height").GetInt32());
        Assert.Equal([1, 0, 0, 0, 0, 2], root.GetProperty("layers")[0].GetProperty("data").EnumerateArray().Select(e => e.GetInt32()));
        var ts = root.GetProperty("tilesets")[0];
        Assert.Equal(1, ts.GetProperty("firstgid").GetInt32());
        Assert.Equal(2, ts.GetProperty("tilecount").GetInt32());
        Assert.Equal("tiles.png", ts.GetProperty("image").GetString());
    }
}

public class TilemapUpgradeTests
{
    // 8×4 tiles tileset of 1px tiles; terrain block at index 2 (cols 2..5, rows 0..3)
    static Tilemap Map(int w = 5, int h = 5) => new(w, h, 1) { Tileset = new Tileset(new uint[8 * 4], 8, 4, 1) };
    const int Base = 2;
    static int Tile(Tilemap m, int mask) => Autotile.TileFor(m.Tileset!, Base, mask);

    [Fact]
    public void Autotile_island_row_and_cross()
    {
        var m = Map();
        var l = m.Layers[0];
        Autotile.Apply(m, l, Base, 2, 2, on: true);
        Assert.Equal(Tile(m, 0), l.Tiles[2 * 5 + 2]);                        // lone island

        Autotile.Apply(m, l, Base, 3, 2, on: true);                           // row of two
        Assert.Equal(Tile(m, Autotile.E), l.Tiles[2 * 5 + 2]);
        Assert.Equal(Tile(m, Autotile.W), l.Tiles[2 * 5 + 3]);

        Autotile.Apply(m, l, Base, 1, 2, on: true);
        Autotile.Apply(m, l, Base, 2, 1, on: true);
        Autotile.Apply(m, l, Base, 2, 3, on: true);                           // plus sign
        Assert.Equal(Tile(m, 15), l.Tiles[2 * 5 + 2]);
        Assert.Equal(Tile(m, Autotile.S), l.Tiles[1 * 5 + 2]);

        Autotile.Apply(m, l, Base, 2, 2, on: false);                          // remove center: arms become islands
        Assert.Equal(Tilemap.Empty, l.Tiles[2 * 5 + 2]);
        Assert.Equal(Tile(m, 0), l.Tiles[1 * 5 + 2]);
        Assert.Equal(Tile(m, 0), l.Tiles[2 * 5 + 3]);
    }

    [Fact]
    public void Autotile_ignores_other_tiles_and_validates_block()
    {
        var m = Map();
        m.Layers[0].Tiles[2 * 5 + 3] = 0;                                     // a non-terrain tile to the east
        Autotile.Apply(m, m.Layers[0], Base, 2, 2, on: true);
        Assert.Equal(Tile(m, 0), m.Layers[0].Tiles[2 * 5 + 2]);
        Assert.Equal(0, m.Layers[0].Tiles[2 * 5 + 3]);
        Assert.Throws<InvalidOperationException>(() => Autotile.Apply(m, m.Layers[0], 5, 0, 0, on: true));   // block overflows columns
        Assert.Equal(16 * 16 * 4, Autotile.Template(8).Length);
    }

    [Fact]
    public void Resized_moves_tiles_and_objects()
    {
        var m = new Tilemap(3, 2, 16);
        m.Layers[0].Tiles[1 * 3 + 2] = 7;
        m.Objects.Add(new MapObject { Id = 1, Name = "Player", X = 20, Y = 5 });
        var r = m.Resized(5, 4, 1, 2);
        Assert.Equal((5, 4), (r.Width, r.Height));
        Assert.Equal(7, r.Layers[0].Tiles[3 * 5 + 3]);
        Assert.Equal(-1, r.Layers[0].Tiles[0]);
        Assert.Equal((36, 37), (r.Objects[0].X, r.Objects[0].Y));
        Assert.Equal(20, m.Objects[0].X);                                     // original untouched
    }

    [Fact]
    public void Objects_and_terrains_round_trip_and_v1_still_loads()
    {
        var m = new Tilemap(2, 2, 8);
        m.Objects.Add(new MapObject { Id = 3, Name = "Coin", Type = "pickup", X = 4, Y = 4 });
        m.Objects.Add(new MapObject { Id = 4, Name = "Wall", X = 0, Y = 0, W = 16, H = 8 });
        m.Terrains.Add(new TerrainRule { Base = 5, Mode = TerrainMode.Blob47, Variants = [1, 2] });
        var t = Tilemap.Deserialize(Tilemap.Serialize(m));
        Assert.Equal(["Coin", "Wall"], t.Objects.Select(o => o.Name));
        Assert.True(t.Objects[0].IsPoint);
        Assert.Equal((16, 8), (t.Objects[1].W, t.Objects[1].H));
        Assert.Equal(5, t.Terrains.Single().Base);
        Assert.Equal(TerrainMode.Blob47, t.Terrains[0].Mode);
        Assert.Equal([1, 2], t.Terrains[0].Variants);
        Assert.Equal(5, t.NextObjectId);

        var v1 = """{"Version":1,"Width":1,"Height":1,"TileSize":8,"TilesetSource":null,"TilesetWidth":0,"TilesetHeight":0,"TilesetPixels":null,"Layers":[{"Name":"L","Visible":true,"Tiles":[-1]}]}""";
        var old = Tilemap.Deserialize(v1);
        Assert.Empty(old.Objects);
        Assert.Empty(old.Terrains);
    }

    [Fact]
    public void Tiled_json_has_object_layer()
    {
        var m = new Tilemap(2, 2, 8) { Tileset = new Tileset(new uint[64], 8, 8, 8) };
        m.Objects.Add(new MapObject { Id = 1, Name = "Player", Type = "spawn", X = 3, Y = 4 });
        var root = JsonDocument.Parse(TiledExport.Json(m, "t.png")).RootElement;
        var og = root.GetProperty("layers")[1];
        Assert.Equal("objectgroup", og.GetProperty("type").GetString());
        var o = og.GetProperty("objects")[0];
        Assert.Equal("Player", o.GetProperty("name").GetString());
        Assert.Equal("spawn", o.GetProperty("type").GetString());
        Assert.True(o.GetProperty("point").GetBoolean());
        Assert.Equal(2, root.GetProperty("nextobjectid").GetInt32());
    }
}
