using System.Text.Json;

namespace PixelForge.Core;

/// <summary>
/// Character as engine-neutral JSON: states with their sprite frames, durations and per-frame boxes
/// (box coordinates relative to the origin, so a game can place them directly).
/// </summary>
public static class CharacterExport
{
    public static string Json(Character c, Sprite sprite, string name, string sheetImage, string? rigAnimations = null)
    {
        var states = c.Animations.Keys.Select(state => (state, range: c.FramesOf(state, sprite))).Where(s => s.range is not null).ToList();
        return JsonSerializer.Serialize(new
        {
            format = "pixelforge-character", version = 1, name, kind = c.Kind.ToString(), facing = c.Facing.ToString(),
            frameWidth = sprite.Width, frameHeight = sprite.Height, sheet = sheetImage,
            origin = new { x = c.OriginX, y = c.OriginY },
            properties = c.Properties,
            // cut-out parts are rectangles of the sheet's first frame; clips are in the animation file
            rig = c.Parts.Count == 0 ? null : new
            {
                parts = c.Parts.Select(p => new { name = p.Name, x = p.X, y = p.Y, w = p.W, h = p.H, pivot = new { x = p.PivotX, y = p.PivotY }, parent = p.Parent, offset = new { x = p.OffsetX, y = p.OffsetY }, z = p.Z }),
                animations = rigAnimations, clips = c.RigClips,
            },
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
