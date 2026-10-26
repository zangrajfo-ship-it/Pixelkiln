using PixelForge.Core;

namespace PixelForge.Core.Tests;

public sealed class SampleArtTests
{
    [Fact]
    public void Every_sample_has_its_tags_and_animated_frames_differ()
    {
        foreach (var name in SampleArt.Names)
        {
            var s = SampleArt.Make(name);
            Assert.True(s.Frames.Count >= 1);
            Assert.All(s.Tags, t => Assert.InRange(t.To, t.From, s.Frames.Count - 1));
            for (int f = 0; f < s.Frames.Count; f++) Assert.Contains(s.Cel(0, f), p => p >> 24 != 0);      // nothing empty
            for (int f = 1; f < s.Frames.Count; f++) Assert.NotEqual(s.Cel(0, f - 1), s.Cel(0, f));
            Assert.Equal(SampleArt.Palette, s.Palette);
        }
        Assert.Equal(["idle", "run", "jump"], SampleArt.Make("hero").Tags.Select(t => t.Name));
        Assert.Equal((128, 16), (SampleArt.Make("tiles").Width, SampleArt.Make("tiles").Height));
        Assert.Throws<ArgumentException>(() => SampleArt.Make("dragon"));
    }

    /// <summary>PF_ART_PREVIEW=folder writes every sample frame side by side, enlarged 4×, for a look.</summary>
    [Trait("Category", "Tool")]   // a helper for people, not a check: without its variable it passes doing nothing
    [Fact]
    public void Write_preview()
    {
        if (Environment.GetEnvironmentVariable("PF_ART_PREVIEW") is not string dir) return;
        const int z = 4;
        var sprites = SampleArt.Names.Select(SampleArt.Make).ToList();
        int w = sprites.Max(s => s.Width * s.Frames.Count + s.Frames.Count) * z, h = sprites.Sum(s => s.Height + 2) * z;
        var o = Enumerable.Repeat(0xFF8FC7E8u, w * h).ToArray();
        int oy = 0;
        foreach (var s in sprites)
        {
            for (int f = 0; f < s.Frames.Count; f++)
            {
                var px = Compositor.Flatten(s, f);
                int ox = f * (s.Width + 1);
                for (int y = 0; y < s.Height * z; y++)
                    for (int x = 0; x < s.Width * z; x++)
                        if (px[y / z * s.Width + x / z] is var c && c >> 24 != 0) o[(oy * z + y) * w + ox * z + x] = c;
            }
            oy += s.Height + 2;
        }
        ImageIO.SavePng(Path.Combine(dir, "sample-art.png"), o, w, h);
    }
}
