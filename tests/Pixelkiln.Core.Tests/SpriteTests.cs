using Pixelkiln.Core;

namespace Pixelkiln.Core.Tests;

public class SpriteTests() : TempDirTest("sprite")
{
    const uint Red = 0xFFFF0000, Blue = 0xFF0000FF;

    [Fact]
    public void Fill_stays_inside_border()
    {
        var g = new uint[5 * 5];
        Tools.Line(g, 5, 5, 2, 0, 2, 4, Red);      // vertical wall at x=2
        Tools.Fill(g, 5, 5, 0, 0, Blue);
        Assert.Equal(Blue, g[0 * 5 + 1]);
        Assert.Equal(Red, g[2 * 5 + 2]);
        Assert.Equal(0u, g[0 * 5 + 3]);
        Assert.Equal(10, g.Count(p => p == Blue));
    }

    [Fact]
    public void Fill_same_color_and_out_of_bounds_are_noops()
    {
        var g = new int[4];
        Tools.Fill(g, 2, 2, 0, 0, 0);
        Tools.Fill(g, 2, 2, 5, 5, 7);
        Assert.All(g, v => Assert.Equal(0, v));
    }

    [Fact]
    public void Line_has_no_gaps()
    {
        var g = new uint[10 * 10];
        Tools.Line(g, 10, 10, 0, 0, 9, 3, Red);
        for (int x = 0; x < 10; x++)
            Assert.Contains(Enumerable.Range(0, 10), y => g[y * 10 + x] == Red);
    }

    [Fact]
    public void Flatten_skips_hidden_and_blends_alpha()
    {
        var s = new Sprite(1, 1);
        s.Cel(0, 0)[0] = Red;
        s.AddLayer(1, "top").Cels[0][0] = 0x800000FF;   // 50% blue
        Assert.Equal(0xFF7F0080u, Compositor.Flatten(s, 0)[0] & 0xFFFFFFFF, new ToleranceComparer(1));
        s.Layers[1].Visible = false;
        Assert.Equal(Red, Compositor.Flatten(s, 0)[0]);
    }

    [Fact]
    public void Undo_redo_round_trip()
    {
        var h = new History<Sprite>(x => x.Clone());
        var s = new Sprite(2, 2);
        h.Push(s);
        s.Cel(0, 0)[0] = Red;
        var undone = h.Undo(s)!;
        Assert.Equal(0u, undone.Cel(0, 0)[0]);
        var redone = h.Redo(undone)!;
        Assert.Equal(Red, redone.Cel(0, 0)[0]);
        Assert.Null(h.Redo(redone));
    }

    [Fact]
    public void Project_round_trip()
    {
        var s = new Sprite(3, 2);
        s.AddLayer(1, "fg").Visible = false;
        s.AddFrame(1, copyFrom: 0);
        s.Frames[1].DurationMs = 250;
        s.Cel(1, 1)[5] = Blue;
        s.Palette.Add(0xFF123456);

        var t = ProjectFile.Deserialize(ProjectFile.Serialize(s));

        Assert.Equal((3, 2), (t.Width, t.Height));
        Assert.Equal(["Layer 1", "fg"], t.Layers.Select(l => l.Name));
        Assert.False(t.Layers[1].Visible);
        Assert.Equal(250, t.Frames[1].DurationMs);
        Assert.Equal(Blue, t.Cel(1, 1)[5]);
        Assert.Equal(s.Palette, t.Palette);
    }

    [Fact]
    public void Png_round_trip()
    {
        var path = P("a.png");
        uint[] px = [Red, 0, 0x80112233, Blue];
        ImageIO.SavePng(path, px, 2, 2);
        var (back, w, h) = ImageIO.LoadImage(path);
        Assert.Equal((2, 2), (w, h));
        Assert.Equal(px, back);
    }

    [Fact]
    public void Frames_add_copy_move_remove()
    {
        var s = new Sprite(1, 1);
        s.Cel(0, 0)[0] = Red;
        s.AddFrame(1, copyFrom: 0);
        s.AddFrame(2);
        Assert.Equal([Red, Red, 0u], s.Layers[0].Cels.Select(c => c[0]));
        s.MoveFrame(2, 0);
        Assert.Equal([0u, Red, Red], s.Layers[0].Cels.Select(c => c[0]));
        s.RemoveFrame(0);
        Assert.Equal(2, s.Frames.Count);
        Assert.Equal(2, s.Layers[0].Cels.Count);
    }

    sealed class ToleranceComparer(int tol) : IEqualityComparer<uint>
    {
        public bool Equals(uint a, uint b) =>
            Enumerable.Range(0, 4).All(i => Math.Abs((int)(a >> (i * 8) & 0xFF) - (int)(b >> (i * 8) & 0xFF)) <= tol);
        public int GetHashCode(uint v) => 0;
    }
}
