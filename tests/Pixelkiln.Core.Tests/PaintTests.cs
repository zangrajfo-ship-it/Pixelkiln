using System.Diagnostics;
using Pixelkiln.Core;

namespace Pixelkiln.Core.Tests;

public sealed class PaintTests() : TempDirTest("paint")
{
    const uint Red = 0xFFFF0000, White = 0xFFFFFFFF, Blue = 0xFF0000FF;

    static uint A(uint p) => p >> 24;

    [Fact]
    public void Blend_modes_follow_the_usual_formulas()
    {
        Assert.Equal(Red, Blending.Over(White, Red, 1));
        Assert.Equal(Red, Blending.Over(White, Red, 1, BlendMode.Multiply));          // white × red = red
        Assert.Equal(White, Blending.Over(Red, White, 1, BlendMode.Screen));
        Assert.Equal(0xFF000000u, Blending.Over(Red, Blue, 1, BlendMode.Multiply));    // no shared channel
        Assert.Equal(0xFFFF00FFu, Blending.Over(Red, Blue, 1, BlendMode.Add));
        Assert.Equal(0xFF000000u, Blending.Over(Red, Blue, 1, BlendMode.Darken));
        Assert.Equal(Red, Blending.Over(0, Red, 1, BlendMode.Multiply));               // over nothing a mode is plain colour
        var half = Blending.Over(White, 0xFF000000, 0.5);
        Assert.InRange(half & 0xFF, 127u, 128u);
        Assert.Equal(128u, A(Blending.Over(0, Red, 0.5)));
    }

    [Fact]
    public void Stroke_opacity_does_not_build_up_but_airbrush_does()
    {
        var d = new PaintDoc(64, 64);
        var s = new PaintStroke(d, d.Layers[0], new BrushSettings(Size: 10, Hardness: 1, Opacity: 0.5, Smoothing: 0, PressureSize: false), Red);
        for (int i = 0; i < 5; i++) s.To(20, 20);           // the same spot five times
        s.To(30, 20);
        Assert.InRange(A(d.Layers[0].Get(20, 20)), 127u, 128u);
        Assert.InRange(A(d.Layers[0].Get(25, 20)), 127u, 128u);  // between dabs, overlapping: still half

        var air = new PaintStroke(d, d.Layers[0], new BrushSettings(PaintTool.Airbrush, Size: 10, Hardness: 1, Flow: 0.2, Smoothing: 0, PressureSize: false), Blue);
        air.To(50, 50);
        uint once = A(d.Layers[0].Get(50, 50));
        for (int i = 0; i < 10; i++) air.Hold();
        Assert.True(A(d.Layers[0].Get(50, 50)) > once);
    }

    [Fact]
    public void Eraser_alpha_lock_mask_and_undo()
    {
        var d = new PaintDoc(300, 300);   // two tiles wide
        var l = d.Layers[0];
        var paint = new PaintStroke(d, l, new BrushSettings(PaintTool.Pencil, Size: 20, Smoothing: 0, PressureSize: false), Red);
        paint.To(250, 100);
        paint.To(270, 100);                                  // crosses into the second tile
        var undo = new PaintUndo();
        undo.Push(paint.Finish());
        Assert.Equal(2, l.Tiles.Count);
        Assert.Equal(Red, l.Get(260, 100));

        l.AlphaLocked = true;
        var recolor = new PaintStroke(d, l, new BrushSettings(Size: 400, Hardness: 1, Smoothing: 0, PressureSize: false), Blue);
        recolor.To(150, 150);
        undo.Push(recolor.Finish());
        Assert.Equal(Blue, l.Get(260, 100));
        Assert.Equal(0u, l.Get(10, 10));                     // nothing appears where there was no paint
        l.AlphaLocked = false;

        var erase = new PaintStroke(d, l, new BrushSettings(PaintTool.Eraser, Size: 6, Hardness: 1, Smoothing: 0, PressureSize: false), 0);
        erase.To(260, 100);
        undo.Push(erase.Finish());
        Assert.Equal(0u, A(l.Get(260, 100)));

        var hide = new PaintStroke(d, l, new BrushSettings(PaintTool.Pencil, Size: 50, Smoothing: 0, PressureSize: false), 0xFF000000, mask: true);
        hide.To(250, 100);
        undo.Push(hide.Finish());
        Assert.Equal(0u, A(d.Composite(255, 105, 1, 1)[0]));  // painted but masked
        Assert.Equal(Blue, l.Get(255, 105));                  // the paint itself is still there

        Assert.True(undo.Undo(d));                            // mask
        Assert.True(undo.Undo(d));                            // eraser
        Assert.Equal(Blue, l.Get(260, 100));
        Assert.True(undo.Undo(d));                            // recolor
        Assert.Equal(Red, l.Get(260, 100));
        Assert.True(undo.Undo(d));                            // first stroke: tiles that did not exist are gone again
        Assert.Empty(l.Tiles);
        Assert.True(undo.Redo(d));
        Assert.Equal(Red, l.Get(260, 100));

        var layers = new PaintUndo.Layers(d);
        d.Layers.Add(new PaintLayer { Name = "top", Blend = BlendMode.Multiply });
        l.Opacity = 0.3;
        undo.Push(layers);
        Assert.True(undo.Undo(d));
        Assert.Single(d.Layers);
        Assert.Equal(1, l.Opacity);
        Assert.True(undo.Redo(d));
        Assert.Equal((2, 0.3), (d.Layers.Count, l.Opacity));
    }

    [Fact]
    public void File_round_trip_keeps_layers_masks_and_skips_empty_tiles()
    {
        var d = new PaintDoc(600, 300);
        d.Layers[0].TileFor(2, 1)[5] = Red;
        d.Layers[0].TileFor(0, 0);                            // empty tile: not written
        d.Layers.Add(new PaintLayer { Name = "glow", Blend = BlendMode.Screen, Opacity = 0.5, AlphaLocked = true, Mask = new() { [PaintLayer.Key(1, 0)] = new byte[256 * 256] } });
        string f = P("a.pxpaint");
        PaintDoc.Save(d, f);
        var e = PaintDoc.Load(f);
        Assert.Equal((600, 300, 2), (e.Width, e.Height, e.Layers.Count));
        Assert.Single(e.Layers[0].Tiles);
        Assert.Equal(Red, e.Layers[0].Get(2 * 256 + 5, 256));
        Assert.Equal((BlendMode.Screen, 0.5, true), (e.Layers[1].Blend, e.Layers[1].Opacity, e.Layers[1].AlphaLocked));
        Assert.Equal(0, e.Layers[1].MaskAt(300, 10));
        Assert.Equal(255, e.Layers[1].MaskAt(10, 10));
        Assert.Throws<InvalidDataException>(() => PaintDoc.Load(P("missing.pxpaint")));
        File.WriteAllText(P("bad.pxpaint"), "not a zip");
        Assert.Throws<InvalidDataException>(() => PaintDoc.Load(P("bad.pxpaint")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PaintDoc(9000, 10));
    }

    [Fact]
    public void Pressure_smoothing_gradient_and_downscale()
    {
        var d = new PaintDoc(200, 100);
        var s = new PaintStroke(d, d.Layers[0], new BrushSettings(Size: 40, Hardness: 1, Smoothing: 0, PressureSize: true), Red);
        s.To(50, 50, pressure: 0.1);
        Assert.Equal(0u, A(d.Layers[0].Get(50 + 8, 50)));   // light pressure: small dab
        s = new PaintStroke(d, d.Layers[0], new BrushSettings(Size: 40, Hardness: 1, Smoothing: 0, PressureSize: true), Red);
        s.To(150, 50, pressure: 1);
        Assert.Equal(255u, A(d.Layers[0].Get(150 + 15, 50)));

        // a zigzag smoothed over five points stays near the middle line instead of reaching the corners
        var jitter = new PaintStroke(d, d.Layers[0], new BrushSettings(Size: 2, Hardness: 1, Smoothing: 4, PressureSize: false), Blue);
        foreach (var y in new[] { 20.0, 30, 20, 30, 20, 30, 20, 30 }) jitter.To(100, y);
        Assert.Equal(0u, A(d.Layers[0].Get(100, 30)));
        Assert.NotNull(jitter.TakeDirty());

        var l = new PaintLayer();
        d.Layers.Add(l);
        PaintOps.Gradient(d, l, 0, 0, 200, 0, 0xFF000000, White);
        Assert.InRange(l.Get(100, 10) & 0xFF, 120u, 135u);
        var small = PaintOps.Downscale(d, 20, 10);
        Assert.Equal(200, small.Length);
        Assert.Equal(0xFF000000u, small[19] & 0xFF000000);
    }
}

[Collection("Load")]
[Trait("Category", "Load")]
public sealed class PaintLoadTests
{
    [Fact]
    public void A_64px_stroke_across_a_4096_canvas_is_quick()
    {
        var d = new PaintDoc(4096, 4096);
        var s = new PaintStroke(d, d.Layers[0], new BrushSettings(Size: 64, Hardness: 0.5, Smoothing: 2), 0xFF336699);
        s.To(0, 2048);   // warm-up
        var sw = Stopwatch.StartNew();
        for (int x = 0; x <= 4096; x += 8) s.To(x, 2048 + Math.Sin(x / 200.0) * 300);
        long strokeMs = sw.ElapsedMilliseconds;
        var r = s.TakeDirty()!.Value;
        sw.Restart();
        d.Composite(r.X, r.Y, Math.Min(r.W, 1024), Math.Min(r.H, 1024));
        long compositeMs = sw.ElapsedMilliseconds;
        Assert.InRange(strokeMs, 0, 1500);                     // ~500 input events across the canvas
        Assert.InRange(compositeMs, 0, 500);
    }
}
