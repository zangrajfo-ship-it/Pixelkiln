using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelForge.Core;

/// <summary>PNG via WPF imaging. ARGB uint in little-endian memory = Bgra32 bytes.</summary>
public static class ImageIO
{
    public static BitmapSource ToBitmap(uint[] px, int w, int h) =>
        BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);

    public static void SavePng(string path, uint[] px, int w, int h)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(ToBitmap(px, w, h)));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    public static (uint[] Pixels, int Width, int Height) LoadImage(string path)
    {
        var src = BitmapFrame.Create(new Uri(Path.GetFullPath(path)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var bmp = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        var px = new uint[bmp.PixelWidth * bmp.PixelHeight];
        bmp.CopyPixels(px, bmp.PixelWidth * 4, 0);
        return (px, bmp.PixelWidth, bmp.PixelHeight);
    }
}
