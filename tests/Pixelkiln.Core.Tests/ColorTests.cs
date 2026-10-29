using Pixelkiln.Core;

namespace Pixelkiln.Core.Tests;

public class ColorTests
{
    [Theory]
    [InlineData(0xFFFF0000u, 0, 1, 1)]
    [InlineData(0xFF00FF00u, 120, 1, 1)]
    [InlineData(0xFF0000FFu, 240, 1, 1)]
    [InlineData(0xFF808080u, 0, 0, 0.502)]
    public void Hsv_known_values(uint argb, double h, double s, double v)
    {
        var (hh, ss, vv) = ColorUtil.ToHsv(argb);
        Assert.Equal(h, hh, 1);
        Assert.Equal(s, ss, 2);
        Assert.Equal(v, vv, 2);
        Assert.Equal(argb, ColorUtil.FromHsv(h, s, v));
    }

    [Fact]
    public void Hsv_round_trips_whole_palette_and_keeps_alpha()
    {
        foreach (var c in Sprite.DefaultPalette)
        {
            var (h, s, v) = ColorUtil.ToHsv(c);
            Assert.Equal(c, ColorUtil.FromHsv(h, s, v));
        }
        Assert.Equal(0x80FF0000u, ColorUtil.FromHsv(360, 1, 1, 0x80));
    }

    [Fact]
    public void Hex_parse_and_format()
    {
        Assert.True(ColorUtil.TryParseHex("#ff8000", out var a));
        Assert.Equal(0xFFFF8000u, a);
        Assert.True(ColorUtil.TryParseHex("80112233", out var b));
        Assert.Equal(0x80112233u, b);
        Assert.False(ColorUtil.TryParseHex("#12345", out _));
        Assert.False(ColorUtil.TryParseHex("zzzzzz", out _));
        Assert.Equal("#FF8000", ColorUtil.ToHex(a));
        Assert.Equal("#80112233", ColorUtil.ToHex(b));
    }
}
