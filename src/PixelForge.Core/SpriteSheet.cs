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
            PixelOps.Paste(px, w, Compositor.Flatten(s, f), s.Width, s.Height, f % cols * s.Width, f / cols * s.Height);
        return new Sheet(px, w, h, cols);
    }

    /// <summary>
    /// Cuts a sprite sheet image into frames of fw x fh, left to right, top to bottom.
    /// Empty (fully transparent) cells at the end are dropped.
    /// </summary>
    public static Sprite Import(uint[] px, int w, int h, int fw, int fh)
    {
        if (fw < 1 || fh < 1 || fw > w || fh > h) throw new ArgumentOutOfRangeException(nameof(fw), $"Frame size must fit inside the {w}x{h} image.");
        int cols = w / fw, rows = h / fh;
        var cells = new List<uint[]>();
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                cells.Add(PixelOps.Crop(px, w, c * fw, r * fh, fw, fh));
        while (cells.Count > 1 && cells[^1].All(p => p >> 24 == 0)) cells.RemoveAt(cells.Count - 1);

        var s = new Sprite(fw, fh);
        for (int i = 1; i < cells.Count; i++) s.AddFrame(i);
        for (int i = 0; i < cells.Count; i++) cells[i].CopyTo(s.Cel(0, i), 0);
        return s;
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
    }, DocumentFile.Indented);
}
