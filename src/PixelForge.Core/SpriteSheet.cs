using System.Text.Json;

namespace PixelForge.Core;

public static class SpriteSheet
{
    public record Sheet(uint[] Pixels, int Width, int Height, int Columns);

    /// <summary>Lays frames out left-to-right, top-to-bottom. columns ≤ 0 = one row.</summary>
    public static Sheet Build(Sprite s, int columns = 0)
    {
        int n = s.Frames.Count;
        int cols = columns <= 0 ? n : Math.Min(columns, n);
        int rows = (n + cols - 1) / cols;
        int w = cols * s.Width, h = rows * s.Height;
        var px = new uint[w * h];
        for (int f = 0; f < n; f++)
        {
            var frame = Compositor.Flatten(s, f);
            int ox = f % cols * s.Width, oy = f / cols * s.Height;
            for (int y = 0; y < s.Height; y++)
                Array.Copy(frame, y * s.Width, px, (oy + y) * w + ox, s.Width);
        }
        return new Sheet(px, w, h, cols);
    }

    /// <summary>Aseprite "array" style JSON (read by Phaser, Godot importers, etc.).</summary>
    public static string Json(Sprite s, Sheet sheet, string imageName, string name) => JsonSerializer.Serialize(new
    {
        frames = s.Frames.Select((f, i) => new
        {
            filename = $"{name} {i}",
            frame = new { x = i % sheet.Columns * s.Width, y = i / sheet.Columns * s.Height, w = s.Width, h = s.Height },
            rotated = false,
            trimmed = false,
            spriteSourceSize = new { x = 0, y = 0, w = s.Width, h = s.Height },
            sourceSize = new { w = s.Width, h = s.Height },
            duration = f.DurationMs,
        }),
        meta = new
        {
            app = "PixelForge", image = imageName, format = "RGBA8888", size = new { w = sheet.Width, h = sheet.Height }, scale = "1",
            frameTags = s.Tags.Select(t => new { name = t.Name, from = t.From, to = t.To, direction = "forward" }),
        },
    }, new JsonSerializerOptions { WriteIndented = true });
}
