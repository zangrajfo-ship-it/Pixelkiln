using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelForge.Core;

/// <summary>PNG via WPF imaging. ARGB uint in little-endian memory = Bgra32 bytes.</summary>
public static class ImageIO
{
    public static BitmapSource ToBitmap(uint[] px, int w, int h) =>
        BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);

    public static void SavePng(string path, uint[] px, int w, int h) => File.WriteAllBytes(path, EncodePng(px, w, h));

    public static byte[] EncodePng(uint[] px, int w, int h)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(ToBitmap(px, w, h)));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    public static (uint[] Pixels, int Width, int Height) LoadImage(string path) =>
        FromBitmap(BitmapFrame.Create(new Uri(Path.GetFullPath(path)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad));

    /// <summary>
    /// RenderTargetBitmap gives premultiplied pixels; PixelForge pictures are straight alpha. Crisp: every pixel ends
    /// fully on or off (RenderTargetBitmap ignores EdgeMode), so the result is clean pixel art.
    /// </summary>
    public static void Unpremultiply(uint[] px, bool crisp)
    {
        for (int i = 0; i < px.Length; i++)
        {
            uint p = px[i], a = p >> 24;
            if (a is 0 or 255) continue;
            if (crisp && a < 128) { px[i] = 0; continue; }
            uint U(int s) => Math.Min(255u, ((p >> s & 0xFF) * 255 + a / 2) / a) << s;
            px[i] = (crisp ? 255u : a) << 24 | U(16) | U(8) | U(0);
        }
    }

    public static (uint[] Pixels, int Width, int Height) FromBitmap(BitmapSource src)
    {
        var bmp = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        var px = new uint[bmp.PixelWidth * bmp.PixelHeight];
        bmp.CopyPixels(px, bmp.PixelWidth * 4, 0);
        return (px, bmp.PixelWidth, bmp.PixelHeight);
    }
}
