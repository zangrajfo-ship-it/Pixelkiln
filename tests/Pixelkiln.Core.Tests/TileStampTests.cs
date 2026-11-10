namespace Pixelkiln.Core.Tests;

/// <summary>The tilemap brush: blocks from the tileset or the map, turning, painting and pattern fill.</summary>
public class TileStampTests
{
    // 4×4 tileset of 1 px tiles: tile numbers 0..15, row by row
    static Tilemap Map(int w = 6, int h = 4) => new(w, h, 1) { Tileset = new Tileset(new uint[16], 4, 4, 1) };

    [Fact]
    public void A_block_of_the_tileset_numbers_its_tiles_row_by_row()
    {
        var s = TileStamp.FromTileset(1, 2, 2, 2, columns: 4);
        Assert.Equal([9, 10, 13, 14], s.Tiles);
        Assert.Equal((2, 2), (s.Width, s.Height));
        Assert.All(s.Flips, f => Assert.Equal(0, f));
        Assert.Equal([5], TileStamp.Single(5).Tiles);
    }

    [Fact]
    public void Turning_a_block_rearranges_it_and_turns_every_tile()
    {
        // 3×2 block:  0 1 2
        //             4 5 6
        var s = TileStamp.FromTileset(0, 0, 3, 2, columns: 4);

        var h = s.Turned(TileFlip.H);
        Assert.Equal([2, 1, 0, 6, 5, 4], h.Tiles);
        Assert.All(h.Flips, f => Assert.Equal(TileFlip.H, f));

        var v = s.Turned(TileFlip.V);
        Assert.Equal([4, 5, 6, 0, 1, 2], v.Tiles);

        // a quarter turn clockwise: 2 wide, 3 high, the left column becomes the top row read from the bottom
        var r = s.Turned(TileFlip.RotateCw);
        Assert.Equal((2, 3), (r.Width, r.Height));
        Assert.Equal([4, 0, 5, 1, 6, 2], r.Tiles);
        Assert.All(r.Flips, f => Assert.Equal(TileFlip.RotateCw, f));

        // four quarter turns, or the same mirror twice, give the block back
        var back = s.Turned(TileFlip.RotateCw).Turned(TileFlip.RotateCw).Turned(TileFlip.RotateCw).Turned(TileFlip.RotateCw);
        Assert.Equal(s.Tiles, back.Tiles);
        Assert.Equal(s.Flips, back.Flips);
        Assert.Equal(s.Tiles, h.Turned(TileFlip.H).Tiles);
        Assert.Equal(s.Flips, h.Turned(TileFlip.H).Flips);
    }

    [Fact]
    public void A_turned_block_draws_like_the_turned_picture_of_the_block()
    {
        // tiles of 2×2 px with different pixels, so both the arrangement and each tile's turn show in the picture
        var pixels = Enumerable.Range(0, 8 * 4).Select(i => 0xFF000000u | (uint)(i + 1)).ToArray();
        var m = new Tilemap(2, 2, 2) { Tileset = new Tileset(pixels, 8, 4, 2) };
        var block = TileStamp.FromTileset(0, 0, 2, 2, columns: 4);
        block.PaintAt(m.Layers[0], 2, 2, 0, 0);
        var picture = m.Render();   // 4×4 px

        foreach (int op in new[] { TileFlip.H, TileFlip.V, TileFlip.RotateCw })
        {
            var turned = new Tilemap(2, 2, 2) { Tileset = m.Tileset };
            block.Turned(op).PaintAt(turned.Layers[0], 2, 2, 0, 0);
            Assert.Equal(TileFlip.Orient(picture, 4, op), turned.Render());
        }
    }

    [Fact]
    public void Painting_skips_holes_and_cells_outside_the_map()
    {
        var m = Map();
        var layer = m.Layers[0];
        var s = new TileStamp([1, Tilemap.Empty, 3, 4], [0, 0, TileFlip.H, 0], 2, 2);
        layer.Tiles[1] = 9;                               // under the hole: stays
        s.PaintAt(layer, m.Width, m.Height, 0, 0);
        Assert.Equal([1, 9], layer.Tiles[..2]);
        Assert.Equal([3, 4], layer.Tiles[6..8]);
        Assert.Equal(TileFlip.H, layer.FlipAt(6));

        s.PaintAt(layer, m.Width, m.Height, 5, 3);        // only its top-left cell is inside the map
        Assert.Equal(1, layer.Tiles[3 * 6 + 5]);
        s.PaintAt(layer, m.Width, m.Height, -1, -1);      // only its bottom-right cell is inside
        Assert.Equal(4, layer.Tiles[0]);
        s.PaintAt(layer, m.Width, m.Height, 50, 50);      // nothing inside: no error
    }

    [Fact]
    public void Picking_from_the_map_keeps_holes_and_turns()
    {
        var m = Map();
        m.Layers[0].Tiles[0] = 2;
        m.Layers[0].SetFlip(0, TileFlip.V);
        var top = m.AddLayer(1, "top");
        top.Tiles[1] = 7;                                 // the top layer wins where it has a tile
        var s = TileStamp.FromMap(m, 0, 0, 3, 1);
        Assert.Equal([2, 7, Tilemap.Empty], s.Tiles);
        Assert.Equal([TileFlip.V, 0, 0], s.Flips.Select(f => (int)f));
        Assert.False(s.IsEmpty);
        Assert.True(TileStamp.FromMap(m, 3, 2, 2, 2).IsEmpty);
    }

    [Fact]
    public void Fill_repeats_the_block_from_the_clicked_cell_and_reports_what_changed()
    {
        var m = Map(6, 4);
        var layer = m.Layers[0];
        for (int y = 0; y < 4; y++) layer.Tiles[y * 6 + 4] = 15;      // a wall in column 4: the fill stays left of it
        var s = new TileStamp([1, 2], [0, TileFlip.H], 2, 1);

        var changed = s.Fill(layer, 6, 4, 1, 1);
        Assert.Equal((0, 0, 4, 4), changed);
        // the block starts at the clicked cell (1, 1) and repeats in both directions
        Assert.Equal([2, 1, 2, 1, 15, Tilemap.Empty], layer.Tiles[6..12]);
        Assert.Equal(0, layer.FlipAt(6 + 1));
        Assert.Equal(TileFlip.H, layer.FlipAt(6 + 0));
        Assert.Equal(TileFlip.H, layer.FlipAt(6 + 2));

        // clearing empties one connected area of equal tiles and drops its flags
        Assert.Equal((0, 0, 1, 4), s.Fill(layer, 6, 4, 0, 0, clear: true));   // column 0 holds tile 2 in every row, column 1 tile 1
        Assert.Equal(Tilemap.Empty, layer.Tiles[0]);
        Assert.Equal(0, layer.FlipAt(0));
        Assert.Equal(1, layer.Tiles[1]);
        Assert.Null(s.Fill(layer, 6, 4, 9, 9));
    }

    [Fact]
    public void The_topmost_object_under_a_map_pixel_is_found()
    {
        var m = Map();
        m.Objects.Add(new MapObject { Id = 1, Name = "area", X = 10, Y = 10, W = 20, H = 10 });
        m.Objects.Add(new MapObject { Id = 2, Name = "spawn", X = 15, Y = 15 });                 // a point, added later: on top
        Assert.Equal(2, m.ObjectAt(15, 15, pointRadius: 2)!.Id);
        Assert.Equal(2, m.ObjectAt(17, 13, pointRadius: 2)!.Id);                                 // within the radius
        Assert.Equal(1, m.ObjectAt(18, 15, pointRadius: 2)!.Id);                                 // past it: the box below
        Assert.Equal(1, m.ObjectAt(29, 19, pointRadius: 2)!.Id);                                 // the last pixel inside the box
        Assert.Null(m.ObjectAt(30, 19, pointRadius: 2));
        Assert.Null(m.ObjectAt(0, 0, pointRadius: 2));
    }
}

public class ZoomTests
{
    static readonly int[] Levels = [1, 2, 3, 4, 6, 8];

    [Fact]
    public void Steps_stop_at_both_ends_and_fit_picks_the_biggest_level_that_shows_everything()
    {
        Assert.Equal(3, Zoom.Step(Levels, 2, +1));
        Assert.Equal(4, Zoom.Step(Levels, 6, -1));
        Assert.Null(Zoom.Step(Levels, 8, +1));
        Assert.Null(Zoom.Step(Levels, 1, -1));

        Assert.Equal(6, Zoom.Fit(Levels, 100, 50, 640, 480));       // 8× would be 800 wide
        Assert.Equal(4, Zoom.Fit(Levels, 100, 100, 640, 480));      // the height decides
        Assert.Equal(8, Zoom.Fit(Levels, 10, 10, 640, 480));
        Assert.Equal(1, Zoom.Fit(Levels, 1000, 1000, 640, 480));    // nothing fits: the smallest
    }
}
