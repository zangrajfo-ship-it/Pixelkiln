using PixelForge.Core;

namespace PixelForge.Core.Tests;

public class AdvancedToolsTests
{
    const uint R = 0xFFFF0000, B = 0xFF0000FF;

    // 4×3:  R R B R
    //       B R B R
    //       B B B R
    static readonly uint[] Img = [R, R, B, R, B, R, B, R, B, B, B, R];

    [Fact]
    public void Magic_wand_contiguous_and_global()
    {
        var m = Selection.MagicWand(Img, 4, 3, 0, 0, contiguous: true);
        Assert.Equal(3, m.Count(b => b));                       // the R group top-left
        Assert.Equal((0, 0, 2, 2), Selection.Bounds(m, 4, 3));
        var all = Selection.MagicWand(Img, 4, 3, 0, 0, contiguous: false);
        Assert.Equal(6, all.Count(b => b));                     // every R
        Assert.Null(Selection.Bounds(new bool[12], 4, 3));
    }

    [Fact]
    public void Masked_crop_clear_and_outline()
    {
        var m = Selection.MagicWand(Img, 4, 3, 0, 0, contiguous: true);
        Assert.Equal([R, R, 0u, R], Selection.CropMasked(Img, m, 4, 0, 0, 2, 2));
        var c = (uint[])Img.Clone();
        Selection.ClearMasked(c, m);
        Assert.Equal(3, c.Count(p => p == 0));
        Assert.Equal(4, Selection.Outline([true], 1, 1).Count());   // one pixel = 4 edges
        Assert.Equal(8, Selection.Outline(m, 4, 3).Count());        // L of 3 pixels
    }

    [Fact]
    public void L_corner_detection()
    {
        Assert.True(Tools.IsLCorner((0, 0), (1, 0), (1, 1)));
        Assert.True(Tools.IsLCorner((0, 0), (0, 1), (1, 1)));
        Assert.False(Tools.IsLCorner((0, 0), (1, 0), (2, 0)));      // straight
        Assert.False(Tools.IsLCorner((0, 0), (1, 1), (2, 2)));      // diagonal already clean
    }

    [Fact]
    public void Replace_color_and_layer_opacity()
    {
        var c = (uint[])Img.Clone();
        Assert.Equal(6, PixelOps.ReplaceColor(c, R, B));
        Assert.All(c, p => Assert.Equal(B, p));

        var s = new Sprite(1, 1);
        s.Cel(0, 0)[0] = R;
        s.Layers[0].Opacity = 0.5;
        Assert.InRange(Compositor.Flatten(s, 0)[0] >> 24, 0x7Fu, 0x80u);
        Assert.Equal(0.5, ProjectFile.Deserialize(ProjectFile.Serialize(s)).Layers[0].Opacity);
        Assert.Equal(0.5, s.Clone().Layers[0].Opacity);
    }

    [Fact]
    public void Scale_nearest_neighbour()
    {
        var s = new Sprite(2, 1);
        (s.Cel(0, 0)[0], s.Cel(0, 0)[1]) = (R, B);
        var big = PixelOps.Scaled(s, 4, 2);
        Assert.Equal([R, R, B, B, R, R, B, B], big.Cel(0, 0));
        var back = PixelOps.Scaled(big, 2, 1);
        Assert.Equal([R, B], back.Cel(0, 0));
    }

    [Fact]
    public void Import_sheet_into_frames()
    {
        // 6×2 sheet, 2×2 frames: red, blue, empty  ->  2 frames (empty tail dropped)
        uint[] px = [R, R, B, B, 0, 0,
                     R, R, B, B, 0, 0];
        var s = SpriteSheet.Import(px, 6, 2, 2, 2);
        Assert.Equal((2, 2, 2), (s.Width, s.Height, s.Frames.Count));
        Assert.All(s.Cel(0, 0), p => Assert.Equal(R, p));
        Assert.All(s.Cel(0, 1), p => Assert.Equal(B, p));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpriteSheet.Import(px, 6, 2, 7, 2));
    }
}
