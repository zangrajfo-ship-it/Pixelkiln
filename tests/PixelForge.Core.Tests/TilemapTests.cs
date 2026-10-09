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
