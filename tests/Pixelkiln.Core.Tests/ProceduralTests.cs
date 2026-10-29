using Pixelkiln.Core;

namespace Pixelkiln.Core.Tests;

public class ProceduralTests
{
    static readonly uint[] Pal = [0xFF101010, 0xFF406020, 0xFF60A030, 0xFFA0E060];

    [Fact]
    public void Same_seed_same_result_different_seed_different_result()
    {
        Assert.Equal(Procedural.Sprite(7, 16, 16, Pal), Procedural.Sprite(7, 16, 16, Pal));
        Assert.NotEqual(Procedural.Sprite(7, 16, 16, Pal), Procedural.Sprite(8, 16, 16, Pal));
        Assert.Equal(Procedural.Tiles(1, 8, Pal, TilePattern.Bricks).Pixels, Procedural.Tiles(1, 8, Pal, TilePattern.Bricks).Pixels);
        Assert.Equal(Procedural.Cave(3, 20, 10), Procedural.Cave(3, 20, 10));
        Assert.Equal(Procedural.Platforms(3, 30, 10), Procedural.Platforms(3, 30, 10));
        Assert.Equal(Rng.Seed("forest"), Rng.Seed("forest"));
        Assert.Equal(42UL, Rng.Seed("42"));
        Assert.Equal(new Rng(1).NextULong(), new Rng(1).NextULong());
    }

    [Fact]
    public void Generated_content_has_the_promised_shape()
    {
        var sp = Procedural.Sprite(11, 12, 12, Pal);
        for (int y = 0; y < 12; y++)
            for (int x = 0; x < 6; x++) Assert.Equal(sp[y * 12 + x], sp[y * 12 + 11 - x]);   // mirrored
        Assert.All(sp, c => Assert.True(c == 0 || Pal.Contains(c)));
        Assert.Contains(sp, c => c != 0);

        var (tiles, w, h) = Procedural.Tiles(5, 8, Pal, TilePattern.Noise, 3);
        Assert.Equal((24, 8), (w, h));
        Assert.All(tiles, c => Assert.Contains(c, Pal));

        var cave = Procedural.Cave(9, 16, 12);
        Assert.True(cave[0] && cave[15] && cave[16 * 11] && cave[^1]);                  // solid border
        Assert.Contains(false, cave);

        var map = new Tilemap(16, 12, 8) { Tileset = new Tileset(new uint[8 * 8 * 16], 32, 32, 8) };
        Procedural.Fill(map, map.Layers[0], cave, null, 5);
        Assert.Equal(cave.Count(c => c), map.Layers[0].Tiles.Count(t => t == 5));
        Assert.Throws<ArgumentException>(() => Procedural.Sprite(1, 8, 8, [0xFF000000]));
    }
}
