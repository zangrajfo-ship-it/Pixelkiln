using System.Globalization;

namespace PixelForge.Core;

/// <summary>ARGB uint helpers: HSV conversion and hex text.</summary>
public static class ColorUtil
{
    /// <summary>h 0..360, s and v 0..1.</summary>
    public static uint FromHsv(double h, double s, double v, byte alpha = 255)
    {
        h = (h % 360 + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return (uint)alpha << 24 | Byte(r + m) << 16 | Byte(g + m) << 8 | Byte(b + m);

        static uint Byte(double d) => (uint)Math.Round(Math.Clamp(d, 0, 1) * 255);
    }

    public static (double H, double S, double V) ToHsv(uint c)
    {
        double r = (c >> 16 & 0xFF) / 255.0, g = (c >> 8 & 0xFF) / 255.0, b = (c & 0xFF) / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0
            : max == r ? 60 * ((g - b) / d % 6)
            : max == g ? 60 * ((b - r) / d + 2)
            : 60 * ((r - g) / d + 4);
        return ((h + 360) % 360, max == 0 ? 0 : d / max, max);
    }

    /// <summary>#RRGGBB for opaque colors, #AARRGGBB otherwise.</summary>
    public static string ToHex(uint c) => c >> 24 == 0xFF ? $"#{c & 0xFFFFFF:X6}" : $"#{c:X8}";

    /// <summary>Accepts RRGGBB or AARRGGBB, with or without '#'.</summary>
    public static bool TryParseHex(string text, out uint color)
    {
        color = 0;
        var hex = text.Trim().TrimStart('#');
        if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, null, out uint v)) return false;
        color = hex.Length == 6 ? 0xFF000000 | v : v;
        return true;
    }
}
