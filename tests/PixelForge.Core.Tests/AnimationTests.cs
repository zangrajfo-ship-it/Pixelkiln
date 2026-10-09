using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelForge.Core;

namespace PixelForge.Core.Tests;

public class AnimationTests
{
    [Theory]
    [InlineData(40, 30)]     // codes grow past 9 bits
    [InlineData(200, 200)]   // dictionary fills (4096) and resets
    public void Gif_decodes_back_to_same_pixels(int w, int h)
    {
        var rnd = new Random(1);
        var colors = Enumerable.Range(0, 200).Select(i => 0xFF000000 | (uint)rnd.Next(0xFFFFFF)).ToArray();
        var frames = Enumerable.Range(0, 3).Select(f =>
            Enumerable.Range(0, w * h).Select(i => i % 7 == f ? 0u : colors[rnd.Next(colors.Length)]).ToArray()).ToList();

        var ms = new MemoryStream();
        GifWriter.Write(ms, frames, [100, 200, 50], w, h);
        ms.Position = 0;

        var dec = new GifBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Assert.Equal(3, dec.Frames.Count);
        for (int f = 0; f < 3; f++)
        {
            var bmp = new FormatConvertedBitmap(dec.Frames[f], PixelFormats.Bgra32, null, 0);
            var px = new uint[w * h];
            bmp.CopyPixels(px, w * 4, 0);
            Assert.Equal(frames[f], px);
        }
        Assert.Equal((ushort)20, (ushort)((BitmapMetadata)dec.Frames[1].Metadata).GetQuery("/grctlext/Delay"));
    }

    [Fact]
    public void Gif_rejects_more_than_255_colors()
    {
        var px = Enumerable.Range(0, 256).Select(i => 0xFF000000 | (uint)i).ToArray();
        Assert.Throws<InvalidOperationException>(() => GifWriter.Write(new MemoryStream(), [px], [100], 16, 16));
    }

    [Fact]
    public void Sheet_grid_layout_and_json()
    {
        var s = new Sprite(2, 3);
        for (int i = 1; i < 5; i++) s.AddFrame(i);
        s.Cel(0, 4)[0] = 0xFFFF0000;

        var sheet = SpriteSheet.Build(s, columns: 2);   // 5 frames -> 2 cols × 3 rows
        Assert.Equal((4, 9), (sheet.Width, sheet.Height));
        Assert.Equal(0xFFFF0000, sheet.Pixels[6 * sheet.Width + 0]);   // frame 4 at (0,6)

        var json = JsonDocument.Parse(SpriteSheet.Json(s, sheet, "a.png", "a"));
        var f4 = json.RootElement.GetProperty("frames")[4].GetProperty("frame");
        Assert.Equal(0, f4.GetProperty("x").GetInt32());
        Assert.Equal(6, f4.GetProperty("y").GetInt32());

        Assert.Equal((10, 3), (SpriteSheet.Build(s).Width, SpriteSheet.Build(s).Height));
    }
}
