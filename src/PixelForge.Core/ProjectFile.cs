using System.Runtime.InteropServices;
using System.Text.Json;

namespace PixelForge.Core;

/// <summary>.pxf sprite project = JSON, cels as base64 of raw ARGB bytes.</summary>
public static class ProjectFile
{
    record LayerDto(string Name, bool Visible, string[] Cels);
    record TagDto(string Name, int From, int To);
    // v2 added Tags (optional, so v1 files still load)
    record SpriteDto(int Version, int Width, int Height, uint[] Palette, int[] FrameDurations, LayerDto[] Layers, TagDto[]? Tags = null);

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static void Save(Sprite s, string path) => File.WriteAllText(path, Serialize(s));

    public static Sprite Load(string path) => Deserialize(File.ReadAllText(path));

    public static string Serialize(Sprite s) => JsonSerializer.Serialize(new SpriteDto(
        2, s.Width, s.Height, [.. s.Palette], [.. s.Frames.Select(f => f.DurationMs)],
        [.. s.Layers.Select(l => new LayerDto(l.Name, l.Visible,
            [.. l.Cels.Select(c => Convert.ToBase64String(MemoryMarshal.AsBytes(c.AsSpan())))]))],
        [.. s.Tags.Select(t => new TagDto(t.Name, t.From, t.To))]), Json);

    public static Sprite Deserialize(string json)
    {
        var d = JsonSerializer.Deserialize<SpriteDto>(json) ?? throw new InvalidDataException("Empty project file.");
        var s = new Sprite(d.Width, d.Height, empty: true);
        s.Palette.Clear();
        s.Palette.AddRange(d.Palette);
        foreach (var ms in d.FrameDurations) s.Frames.Add(new Frame { DurationMs = ms });
        foreach (var ld in d.Layers)
        {
            if (ld.Cels.Length != s.Frames.Count) throw new InvalidDataException($"Layer '{ld.Name}' has wrong frame count.");
            var l = new Layer { Name = ld.Name, Visible = ld.Visible };
            foreach (var b64 in ld.Cels)
            {
                var px = MemoryMarshal.Cast<byte, uint>(Convert.FromBase64String(b64)).ToArray();
                if (px.Length != d.Width * d.Height) throw new InvalidDataException($"Layer '{ld.Name}' has wrong cel size.");
                l.Cels.Add(px);
            }
            s.Layers.Add(l);
        }
        if (s.Layers.Count == 0 || s.Frames.Count == 0) throw new InvalidDataException("Project has no layers or frames.");
        foreach (var t in d.Tags ?? [])
            if (t.From >= 0 && t.From <= t.To && t.To < s.Frames.Count)   // skip broken ranges instead of failing
                s.Tags.Add(new FrameTag { Name = t.Name ?? "tag", From = t.From, To = t.To });
        return s;
    }
}
