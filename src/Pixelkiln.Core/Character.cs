using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pixelkiln.Core;

public enum CharacterKind { Player, Enemy, Npc, Item }
public enum Facing { Right, Left }
public enum BoxKind { Hurt, Hit, Solid, Trigger }

/// <summary>Rectangle on a character's sprite: where it can be hit, where it hits, what is solid.</summary>
public sealed class CharacterBox
{
    public BoxKind Kind { get; set; } = BoxKind.Hurt;
    /// <summary>Animation state this box belongs to; null = every state.</summary>
    public string? State { get; set; }
    /// <summary>Frame inside the state's tag (0-based); null = every frame of the state.</summary>
    public int? Frame { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; } = 8;
    public int H { get; set; } = 8;
    /// <summary>Collision category bit (see ProjectSettings.CollisionCategories).</summary>
    public int Category { get; set; }
    public CharacterBox Clone() => (CharacterBox)MemberwiseClone();
}

/// <summary>
/// A game character on top of a sprite asset (.pxchar). Animations are the sprite's frame tags,
/// so timing and onion skin stay in the sprite editor; the character adds states, origin and boxes.
/// </summary>
public sealed class Character
{
    // v2: Parts, RigAnimAssetId, RigClips (cut-out body parts)
    public const int Version = 2;
    public static readonly string[] StandardStates = ["idle", "walk", "run", "jump", "attack", "hurt", "death"];

    public CharacterKind Kind { get; set; } = CharacterKind.Player;
    public Guid? SpriteAssetId { get; set; }
    /// <summary>State name → sprite tag name (often the same).</summary>
    public Dictionary<string, string> Animations { get; set; } = [];
    /// <summary>Pivot in sprite pixels (feet for platformers), used for placement in scenes.</summary>
    public int OriginX { get; set; }
    public int OriginY { get; set; }
    /// <summary>Which way the sprite faces as drawn.</summary>
    public Facing Facing { get; set; } = Facing.Right;
    public List<CharacterBox> Boxes { get; set; } = [];
    /// <summary>Free game data (hp, speed …) passed through to exports.</summary>
    public Dictionary<string, string> Properties { get; set; } = [];
    /// <summary>Cut-out body parts (empty = the character is drawn frame by frame from its sprite).</summary>
    public List<RigPart> Parts { get; set; } = [];
    /// <summary>Animation library with the parts' clips, and which clip plays in each state.</summary>
    public Guid? RigAnimAssetId { get; set; }
    public Dictionary<string, string> RigClips { get; set; } = [];

    /// <summary>Boxes active in a state's frame (frame index within the tag).</summary>
    public IEnumerable<CharacterBox> BoxesAt(string? state, int frame) =>
        Boxes.Where(b => (b.State is null || b.State == state) && (b.Frame is null || b.Frame == frame));

    /// <summary>Sprite frame range of a state, or null if the state has no matching tag.</summary>
    public (int From, int To)? FramesOf(string state, Sprite sprite) =>
        Animations.TryGetValue(state, out var tag) && sprite.Tags.FirstOrDefault(t => t.Name == tag) is { } ft ? (ft.From, ft.To) : null;

    /// <summary>
    /// Template: gives the sprite a tag for every standard state it lacks (one new frame each, copied from the
    /// last frame so there is something to edit) and maps each state to its tag. Returns the states added.
    /// </summary>
    public List<string> ApplyTemplate(Sprite sprite, IEnumerable<string>? states = null)
    {
        var added = new List<string>();
        foreach (var state in states ?? StandardStates)
        {
            if (sprite.Tags.All(t => t.Name != state))
            {
                int at = sprite.Frames.Count;
                sprite.AddFrame(at, copyFrom: at - 1);
                sprite.Tags.Add(new FrameTag { Name = state, From = at, To = at });
                added.Add(state);
            }
            Animations.TryAdd(state, state);
        }
        return added;
    }

    public Character Clone()
    {
        var c = (Character)MemberwiseClone();
        c.Animations = new(Animations);
        c.Boxes = [.. Boxes.Select(b => b.Clone())];
        c.Properties = new(Properties);
        c.Parts = [.. Parts.Select(p => p.Clone())];
        c.RigClips = new(RigClips);
        return c;
    }

    // ---------- .pxchar ----------

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    sealed class Dto
    {
        public int Version { get; set; }
        public CharacterKind Kind { get; set; }
        public Guid? SpriteAssetId { get; set; }
        public Dictionary<string, string>? Animations { get; set; }
        public int OriginX { get; set; }
        public int OriginY { get; set; }
        public Facing Facing { get; set; }
        public List<CharacterBox>? Boxes { get; set; }
        public Dictionary<string, string>? Properties { get; set; }
        public List<RigPart>? Parts { get; set; }
        public Guid? RigAnimAssetId { get; set; }
        public Dictionary<string, string>? RigClips { get; set; }
    }

    public static string Serialize(Character c) => JsonSerializer.Serialize(new Dto
    {
        Version = Version, Kind = c.Kind, SpriteAssetId = c.SpriteAssetId, Animations = c.Animations, OriginX = c.OriginX, OriginY = c.OriginY,
        Facing = c.Facing, Boxes = c.Boxes, Properties = c.Properties, Parts = c.Parts, RigAnimAssetId = c.RigAnimAssetId, RigClips = c.RigClips,
    }, Json);

    public static Character Deserialize(string json)
    {
        var d = JsonSerializer.Deserialize<Dto>(json, Json) ?? throw new InvalidDataException("Empty character file.");
        var c = new Character
        {
            Kind = d.Kind, SpriteAssetId = d.SpriteAssetId, Animations = d.Animations ?? [], OriginX = d.OriginX, OriginY = d.OriginY,
            Facing = d.Facing, Boxes = d.Boxes ?? [], Properties = d.Properties ?? [],
            Parts = d.Parts ?? [], RigAnimAssetId = d.RigAnimAssetId, RigClips = d.RigClips ?? [],   // v1: no rig
        };
        if (c.Parts.Select(p => p.Name).Distinct().Count() != c.Parts.Count) throw new InvalidDataException("Two body parts have the same name.");
        if (c.Parts.Any(p => p.W < 1 || p.H < 1)) throw new InvalidDataException("A body part has no size.");
        foreach (var b in c.Boxes)
            if (b.W < 1 || b.H < 1 || b.Category is < 0 or > 31) throw new InvalidDataException("A box has no size or an invalid collision category.");
        return c;
    }

    public static void Save(Character c, string path) => DocumentFile.Save(path, Serialize(c), Version);
    public static Character Load(string path) => DocumentFile.Load(path, "character", Version, Deserialize);
}
