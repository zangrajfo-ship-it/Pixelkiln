using System.Text.Json;

namespace PixelForge.Core;

/// <summary>
/// Character as engine-neutral JSON: states with their sprite frames, durations and per-frame boxes
/// (box coordinates relative to the origin, so a game can place them directly).
/// </summary>
public static class CharacterExport
{
    public static string Json(Character c, Sprite sprite, string name, string sheetImage)
    {
        var states = c.Animations.Keys.Select(state => (state, range: c.FramesOf(state, sprite))).Where(s => s.range is not null).ToList();
        return JsonSerializer.Serialize(new
        {
            format = "pixelforge-character", version = 1, name, kind = c.Kind.ToString(), facing = c.Facing.ToString(),
            frameWidth = sprite.Width, frameHeight = sprite.Height, sheet = sheetImage,
            origin = new { x = c.OriginX, y = c.OriginY },
            properties = c.Properties,
            states = states.ToDictionary(s => s.state, s => new
            {
                frames = Enumerable.Range(s.range!.Value.From, s.range.Value.To - s.range.Value.From + 1).Select((f, i) => new
                {
                    frame = f,
                    duration = sprite.Frames[f].DurationMs,
                    boxes = c.BoxesAt(s.state, i).Select(b => new
                    {
                        kind = b.Kind.ToString(), x = b.X - c.OriginX, y = b.Y - c.OriginY, w = b.W, h = b.H, category = b.Category,
                    }),
                }),
            }),
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
