namespace PixelForge.Core;

/// <summary>Human-readable facts about an asset file for the asset browser (dimensions, frames, duration …).</summary>
public static class AssetInfo
{
    public static string Describe(AssetType type, string path)
    {
        if (!File.Exists(path)) return "File is missing.";
        var file = new FileInfo(path);
        string size = file.Length < 1024 ? $"{file.Length} B" : file.Length < 1 << 20 ? $"{file.Length / 1024.0:0.#} KB" : $"{file.Length / 1048576.0:0.#} MB";
        string facts;
        try
        {
            facts = type switch
            {
                AssetType.Sprite => Sprite(ProjectFile.Load(path)),
                AssetType.Image => ImageIO.LoadImage(path) is var (_, w, h) ? $"{w}×{h} image" : "",
                AssetType.Tilemap => Map(Tilemap.Load(path)),
                AssetType.Song => Song(Core.Song.Load(path)),
                AssetType.Sound => $"Sound effect, {Sfx.Load(path).Duration:0.00} s",
                AssetType.Palette => $"{PaletteIO.Load(path).Count} colors",
                AssetType.Character => Character(Core.Character.Load(path)),
                AssetType.Scene => Scene(Core.Scene.Load(path)),
                AssetType.Audio => $"WAV, {WavReader.Load(path).Length / (double)Synth.SampleRate:0.00} s",
                AssetType.Mix => Mix(Core.Mix.Load(path)),
                _ => type.ToString(),
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException or FormatException)
        {
            facts = "Cannot be read: " + ex.Message;
        }
        return $"{facts}\n{size}, changed {file.LastWriteTime:d. M. yyyy HH:mm}";
    }

    static string Sprite(Sprite s) =>
        $"{s.Width}×{s.Height} px, {s.Frames.Count} frame{(s.Frames.Count == 1 ? "" : "s")}, {s.Layers.Count} layer{(s.Layers.Count == 1 ? "" : "s")}, " +
        $"{s.Frames.Sum(f => f.DurationMs) / 1000.0:0.00} s" + (s.Tags.Count > 0 ? $", tags: {string.Join(", ", s.Tags.Select(t => t.Name))}" : "");

    static string Map(Tilemap m) =>
        $"{m.Width}×{m.Height} tiles of {m.TileSize} px, {m.Layers.Count} layer{(m.Layers.Count == 1 ? "" : "s")}, {m.Objects.Count} object{(m.Objects.Count == 1 ? "" : "s")}";

    static string Song(Song s)
    {
        double seconds = s.Order.Count * s.Steps * s.StepSeconds;
        return $"{s.Bpm} BPM, {s.Patterns.Count} pattern{(s.Patterns.Count == 1 ? "" : "s")}, {(int)seconds / 60}:{(int)seconds % 60:00}";
    }

    static string Mix(Mix m) =>
        $"Mix, {m.Tracks.Count} tracks, {m.Tracks.Sum(t => t.Clips.Count)} clips{(m.LoopStartMs is int a ? $", loop {a / 1000.0:0.##}–{m.LoopEndMs / 1000.0:0.##} s" : "")}";

    static string Scene(Scene s) =>
        $"Scene {s.Width}×{s.Height} px{(s.Level.Length > 0 ? $", level {s.Level} #{s.Order}" : "")}, {s.Layers.Count} layers, {s.Instances.Count} instances, {s.Areas.Count} areas";

    static string Character(Character c) =>
        $"{c.Kind}, {c.Animations.Count} state{(c.Animations.Count == 1 ? "" : "s")}, {c.Boxes.Count} box{(c.Boxes.Count == 1 ? "" : "es")}";

    /// <summary>Nearest-neighbour downscale so the longer side is at most <paramref name="max"/> (never upscales).</summary>
    public static (uint[] Pixels, int Width, int Height) Thumbnail(uint[] px, int w, int h, int max)
    {
        if (w <= max && h <= max) return (px, w, h);
        double f = (double)max / Math.Max(w, h);
        int tw = Math.Max(1, (int)(w * f)), th = Math.Max(1, (int)(h * f));
        var t = new uint[tw * th];
        for (int y = 0; y < th; y++)
            for (int x = 0; x < tw; x++) t[y * tw + x] = px[y * h / th * w + x * w / tw];
        return (t, tw, th);
    }

    /// <summary>Picture of a palette: 8 swatches per row, 4×4 px each.</summary>
    public static (uint[] Pixels, int Width, int Height) PaletteImage(IReadOnlyList<uint> colors)
    {
        int cols = 8, rows = Math.Max(1, (colors.Count + cols - 1) / cols), w = cols * 4, h = rows * 4;
        var px = new uint[w * h];
        for (int i = 0; i < colors.Count; i++)
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++) px[(i / cols * 4 + y) * w + i % cols * 4 + x] = colors[i];
        return (px, w, h);
    }
}
