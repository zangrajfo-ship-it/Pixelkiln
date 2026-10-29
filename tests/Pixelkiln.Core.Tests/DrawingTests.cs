using Pixelkiln.Core;

namespace Pixelkiln.Core.Tests;

public class DrawingTests
{
    static bool[] Mask(int w, int h, Action<int[]> draw)
    {
        var g = new int[w * h];
        draw(g);
        return [.. g.Select(v => v == 1)];
    }

    static void AssertSymmetric(bool[] m, int w, int h)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Assert.Equal(m[y * w + x], m[y * w + w - 1 - x]);
                Assert.Equal(m[y * w + x], m[(h - 1 - y) * w + x]);
            }
    }

    [Theory]
    [InlineData(9, 9)]
    [InlineData(10, 6)]
    [InlineData(3, 12)]
    [InlineData(1, 1)]
    public void Ellipse_is_symmetric_and_outline_is_inside_filled(int w, int h)
    {
        var filled = Mask(w, h, g => Tools.Ellipse(g, w, h, 0, 0, w - 1, h - 1, 1, filled: true));
        var outline = Mask(w, h, g => Tools.Ellipse(g, w, h, w - 1, h - 1, 0, 0, 1, filled: false));   // reversed corners
        AssertSymmetric(filled, w, h);
        AssertSymmetric(outline, w, h);
        for (int i = 0; i < filled.Length; i++) Assert.True(!outline[i] || filled[i]);
        // every row and column of the box is touched
        for (int y = 0; y < h; y++) Assert.Contains(Enumerable.Range(0, w), x => outline[y * w + x]);
        for (int x = 0; x < w; x++) Assert.Contains(Enumerable.Range(0, h), y => outline[y * w + x]);
    }

    [Fact]
    public void Wrap_for_tiled_mode()
    {
        Assert.Equal([31, 0, 1, 0, 31], new[] { -1, 0, 1, 32, 63 }.Select(v => Tools.Wrap(v, 32)));
        Assert.Equal(16, Tools.Wrap(-48, 32));
    }

    [Fact]
    public void Rect_outline_and_filled()
    {
        var o = Mask(5, 4, g => Tools.Rect(g, 5, 4, 4, 3, 0, 0, 1, filled: false));
        Assert.Equal(14, o.Count(b => b));
        Assert.False(o[1 * 5 + 2]);
        Assert.Equal(20, Mask(5, 4, g => Tools.Rect(g, 5, 4, 0, 0, 4, 3, 1, filled: true)).Count(b => b));
    }

    [Fact]
    public void Brush_points_are_centered_for_odd_sizes()
    {
        Assert.Equal([(5, 5)], Tools.BrushPoints(5, 5, 1));
        var p3 = Tools.BrushPoints(5, 5, 3).ToList();
        Assert.Equal(9, p3.Count);
        Assert.Equal((4, 4), (p3.Min(p => p.X), p3.Min(p => p.Y)));
        Assert.Equal((6, 6), (p3.Max(p => p.X), p3.Max(p => p.Y)));
    }

    [Fact]
    public void Fill_pattern_repeats_stamp()
    {
        var g = new int[4 * 2];
        Tools.FillPattern(g, 4, 2, 1, 1, (x, y) => x % 2 + 10 * (y % 2));
        Assert.Equal([0, 1, 0, 1, 10, 11, 10, 11], g);
    }

    // 3×2 image: a b c / d e f
    static readonly uint[] Img = [1, 2, 3, 4, 5, 6];

    [Fact]
    public void Flip_and_rotate()
    {
        Assert.Equal([3u, 2, 1, 6, 5, 4], PixelOps.FlipH(Img, 3, 2));
        Assert.Equal([4u, 5, 6, 1, 2, 3], PixelOps.FlipV(Img, 3, 2));
        Assert.Equal([4u, 1, 5, 2, 6, 3], PixelOps.Rotate90(Img, 3, 2));   // 2 wide, 3 tall
        var r = Img;
        int w = 3, h = 2;
        for (int i = 0; i < 4; i++) { r = PixelOps.Rotate90(r, w, h); (w, h) = (h, w); }
        Assert.Equal(Img, r);
    }

    [Fact]
    public void Crop_stamp_clear()
    {
        Assert.Equal([2u, 3, 5, 6], PixelOps.Crop(Img, 3, 1, 0, 2, 2));
        var dst = new uint[9];
        PixelOps.Stamp(dst, 3, 3, [0xFF000001, 0, 0xFF000003, 0xFF000004], 2, 2, 2, 2);   // mostly off-canvas
        Assert.Equal(0xFF000001, dst[8]);
        Assert.Equal(1, dst.Count(p => p != 0));
        var c = (uint[])Img.Clone();
        PixelOps.Clear(c, 3, 1, 0, 2, 1);
        Assert.Equal([1u, 0, 0, 4, 5, 6], c);
    }

    [Fact]
    public void Resized_keeps_everything_and_anchors()
    {
        var s = new Sprite(2, 2);
        s.AddFrame(1);
        s.AddLayer(1, "top").Visible = false;
        s.Cel(0, 1)[3] = 0xFFFF0000;                                    // bottom-right of frame 2
        int ox = PixelOps.AnchorOffset(2, 4, 1), oy = PixelOps.AnchorOffset(2, 4, 2);
        Assert.Equal((1, 2), (ox, oy));
        var r = PixelOps.Resized(s, 4, 4, ox, oy);
        Assert.Equal((4, 4, 2, 2), (r.Width, r.Height, r.Frames.Count, r.Layers.Count));
        Assert.False(r.Layers[1].Visible);
        Assert.Equal(0xFFFF0000, r.Cel(0, 1)[3 * 4 + 2]);
        var cropped = PixelOps.Resized(r, 1, 1, -2, -3);
        Assert.Equal(0xFFFF0000, cropped.Cel(0, 1)[0]);
    }

    [Fact]
    public void Palette_hex_and_gpl_round_trip()
    {
        uint[] colors = [0xFF000000, 0xFFFF8000, 0xFF12ABEF];
        Assert.Equal(colors, PaletteIO.Parse(PaletteIO.ToHex(colors), gpl: false));
        Assert.Equal(colors, PaletteIO.Parse(PaletteIO.ToGpl(colors, "x"), gpl: true));
        Assert.Equal([0xFFFF0000u], PaletteIO.Parse("#ff0000\r\n\r\n", gpl: false));
        Assert.Throws<InvalidDataException>(() => PaletteIO.Parse("zzz", gpl: false));
    }
}
