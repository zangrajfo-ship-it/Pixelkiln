using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelForge.Core;

public enum SceneLayerKind { Tilemap, Image, Instances }
public enum AreaKind { Trigger, Checkpoint, Spawn, Camera, Kill }

/// <summary>One drawing layer of a scene; index 0 in <see cref="Scene.Layers"/> is drawn first (farthest back).</summary>
public sealed class SceneLayer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Layer";
    public SceneLayerKind Kind { get; set; } = SceneLayerKind.Instances;
    /// <summary>Tilemap or image asset (null for instance layers).</summary>
    public Guid? AssetId { get; set; }
    /// <summary>1 = moves with the world; 0.5 = half as fast (far background); 0 = fixed to the screen.</summary>
    public double ParallaxX { get; set; } = 1;
    public double ParallaxY { get; set; } = 1;
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    /// <summary>Image layers: repeat horizontally (endless backgrounds).</summary>
    public bool RepeatX { get; set; }
    public bool Visible { get; set; } = true;
    public SceneLayer Clone() => (SceneLayer)MemberwiseClone();
}

/// <summary>A sprite or character placed in the scene.</summary>
public sealed class SceneInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    /// <summary>Sprite or character asset.</summary>
    public Guid AssetId { get; set; }
    public Guid LayerId { get; set; }
    /// <summary>Position of the asset's origin (characters) or top-left corner (sprites), in scene pixels.</summary>
    public int X { get; set; }
    public int Y { get; set; }
    /// <summary>State (characters) or tag (sprites) to play; null = first frame / all frames.</summary>
    public string? Animation { get; set; }
    public bool FlipX { get; set; }
    /// <summary>Draw order inside its layer (higher = in front).</summary>
    public int Z { get; set; }
    /// <summary>Optional movement (moving platforms, patrols), relative to X/Y.</summary>
    public KeyframeTrack? Track { get; set; }
    /// <summary>A clip from an animation library, used instead of Track when set (reusable across scenes).</summary>
    public Guid? ClipAssetId { get; set; }
    public string? Clip { get; set; }
    public Dictionary<string, string> Properties { get; set; } = [];

    public SceneInstance Clone()
    {
        var c = (SceneInstance)MemberwiseClone();
        c.Track = Track?.Clone();
        c.Properties = new(Properties);
        return c;
    }
}

/// <summary>Rectangle with a game meaning: trigger, checkpoint, spawn point, camera zone, kill zone.</summary>
public sealed class SceneArea
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AreaKind Kind { get; set; } = AreaKind.Trigger;
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; } = 16;
    public int H { get; set; } = 16;
    public Dictionary<string, string> Properties { get; set; } = [];

    public SceneArea Clone()
    {
        var c = (SceneArea)MemberwiseClone();
        c.Properties = new(Properties);
        return c;
    }
}

/// <summary>A level: layers (tilemaps, images, instances), areas and a camera (.pxscene).</summary>
public sealed class Scene
{
    // v2: UiAssetId + UiScreen (a UI kit screen drawn over the game view)
    // v3: SceneInstance.ClipAssetId/Clip and CameraClipAssetId/CameraClip (clips from an animation library)
    // v4: Ambient, Lights, Occluders, TileShadows, Emitters (2D lighting and particles)
    // v5: LogicAssetId (visual logic graph)
    public const int Version = 5;

    public int Width { get; set; } = 640;
    public int Height { get; set; } = 360;
    public uint Background { get; set; } = 0xFF202030;
    /// <summary>Level group (e.g. "World 1") and order inside it, for organising scenes.</summary>
    public string Level { get; set; } = "";
    public int Order { get; set; }
    public int CameraX { get; set; }
    public int CameraY { get; set; }
    /// <summary>Music asset that plays in this scene (optional).</summary>
    public Guid? MusicAssetId { get; set; }
    public List<SceneLayer> Layers { get; set; } = [new() { Name = "Objects" }];
    public List<SceneInstance> Instances { get; set; } = [];
    public List<SceneArea> Areas { get; set; } = [];
    /// <summary>UI kit and the name of its screen shown on top of the game (HUD, menus); null = none.</summary>
    public Guid? UiAssetId { get; set; }
    public string? UiScreen { get; set; }
    /// <summary>Camera move from an animation library (X, Y added to the camera; null = none).</summary>
    public Guid? CameraClipAssetId { get; set; }
    public string? CameraClip { get; set; }
    /// <summary>Light where no lamp shines; white with no lights = lighting off.</summary>
    public uint Ambient { get; set; } = 0xFFFFFFFF;
    public List<SceneLight> Lights { get; set; } = [];
    public List<SceneOccluder> Occluders { get; set; } = [];
    /// <summary>Solid tiles of world-speed tilemap layers cast shadows.</summary>
    public bool TileShadows { get; set; } = true;
    public List<SceneEmitter> Emitters { get; set; } = [];
    /// <summary>Visual logic graph (.pxlogic) that runs in this scene; null = none.</summary>
    public Guid? LogicAssetId { get; set; }

    public IEnumerable<Guid> ReferencedAssets() =>
        Layers.Select(l => l.AssetId).OfType<Guid>().Concat(Instances.Select(i => i.AssetId)).Concat(Instances.Select(i => i.ClipAssetId).OfType<Guid>())
            .Concat(MusicAssetId is Guid m ? [m] : []).Concat(UiAssetId is Guid u ? [u] : []).Concat(CameraClipAssetId is Guid c ? [c] : [])
            .Concat(LogicAssetId is Guid g ? [g] : []).Distinct();

    /// <summary>Instances of a layer in drawing order.</summary>
    public IEnumerable<SceneInstance> InstancesOf(Guid layerId) => Instances.Where(i => i.LayerId == layerId).OrderBy(i => i.Z).ThenBy(i => i.Y);

    /// <summary>New size; objects keep their positions (anything now outside is reported by validation, never deleted).</summary>
    public void Resize(int w, int h)
    {
        if (w < 16 || h < 16 || w > 65536 || h > 65536) throw new ArgumentOutOfRangeException(nameof(w), "Scene size must be 16..65536 pixels.");
        (Width, Height) = (w, h);
        CameraX = Math.Clamp(CameraX, 0, w - 1);
        CameraY = Math.Clamp(CameraY, 0, h - 1);
    }

    public Scene Clone() => new()
    {
        Width = Width, Height = Height, Background = Background, Level = Level, Order = Order, CameraX = CameraX, CameraY = CameraY, MusicAssetId = MusicAssetId,
        UiAssetId = UiAssetId, UiScreen = UiScreen, CameraClipAssetId = CameraClipAssetId, CameraClip = CameraClip,
        Ambient = Ambient, TileShadows = TileShadows, LogicAssetId = LogicAssetId,
        Lights = [.. Lights.Select(l => l.Clone())], Occluders = [.. Occluders.Select(o => o.Clone())], Emitters = [.. Emitters.Select(e => e.Clone())],
        Layers = [.. Layers.Select(l => l.Clone())],
        Instances = [.. Instances.Select(i => i.Clone())],
        Areas = [.. Areas.Select(a => a.Clone())],
    };

    // ---------- .pxscene ----------

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() }, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    sealed class Dto
    {
        public int Version { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public uint Background { get; set; }
        public string? Level { get; set; }
        public int Order { get; set; }
        public int CameraX { get; set; }
        public int CameraY { get; set; }
        public Guid? MusicAssetId { get; set; }
        public Guid? UiAssetId { get; set; }
        public string? UiScreen { get; set; }
        public Guid? CameraClipAssetId { get; set; }
        public string? CameraClip { get; set; }
        public uint? Ambient { get; set; }
        public bool? TileShadows { get; set; }
        public List<SceneLight>? Lights { get; set; }
        public List<SceneOccluder>? Occluders { get; set; }
        public List<SceneEmitter>? Emitters { get; set; }
        public Guid? LogicAssetId { get; set; }
        public List<SceneLayer>? Layers { get; set; }
        public List<SceneInstance>? Instances { get; set; }
        public List<SceneArea>? Areas { get; set; }
    }

    public static string Serialize(Scene s) => JsonSerializer.Serialize(new Dto
    {
        Version = Version, Width = s.Width, Height = s.Height, Background = s.Background, Level = s.Level, Order = s.Order,
        CameraX = s.CameraX, CameraY = s.CameraY, MusicAssetId = s.MusicAssetId, UiAssetId = s.UiAssetId, UiScreen = s.UiScreen,
        CameraClipAssetId = s.CameraClipAssetId, CameraClip = s.CameraClip,
        Ambient = s.Ambient, TileShadows = s.TileShadows, Lights = s.Lights, Occluders = s.Occluders, Emitters = s.Emitters, LogicAssetId = s.LogicAssetId,
        Layers = s.Layers, Instances = s.Instances, Areas = s.Areas,
    }, Json);

    public static Scene Deserialize(string json)
    {
        var d = JsonSerializer.Deserialize<Dto>(json, Json) ?? throw new InvalidDataException("Empty scene file.");
        var s = new Scene
        {
            Level = d.Level ?? "", Order = d.Order, Background = d.Background, MusicAssetId = d.MusicAssetId,
            UiAssetId = d.UiAssetId, UiScreen = d.UiScreen,   // absent in v1 files: no overlay
            CameraClipAssetId = d.CameraClipAssetId, CameraClip = d.CameraClip,   // absent before v3
            Ambient = d.Ambient ?? 0xFFFFFFFF, TileShadows = d.TileShadows ?? true,   // before v4: unlit
            Lights = d.Lights ?? [], Occluders = d.Occluders ?? [], Emitters = d.Emitters ?? [],
            LogicAssetId = d.LogicAssetId,   // absent before v5
            Layers = d.Layers ?? [], Instances = d.Instances ?? [], Areas = d.Areas ?? [],
        };
        s.Resize(d.Width, d.Height);
        (s.CameraX, s.CameraY) = (Math.Clamp(d.CameraX, 0, s.Width - 1), Math.Clamp(d.CameraY, 0, s.Height - 1));
        if (s.Layers.Count == 0) throw new InvalidDataException("A scene needs at least one layer.");
        var ids = s.Layers.Select(l => l.Id).Concat(s.Instances.Select(i => i.Id)).Concat(s.Areas.Select(a => a.Id)).ToList();
        if (ids.Count != ids.Distinct().Count()) throw new InvalidDataException("The scene uses an id twice.");
        if (s.Areas.Any(a => a.W < 1 || a.H < 1)) throw new InvalidDataException("An area has no size.");
        if (s.Lights.Any(l => l.Radius is < 1 or > 4096 || l.Intensity is < 0 or > 16 || l.Cone is < 1 or > 360)) throw new InvalidDataException("A light has a radius outside 1–4096, an intensity outside 0–16 or a cone outside 1–360°.");
        if (s.Occluders.Any(o => o.Points.Count < 6 || o.Points.Count % 2 != 0)) throw new InvalidDataException("An occluder needs at least three points.");
        if (s.Emitters.Any(e => e.Rate is < 0 or > 2000 || e.LifeMs is < 1 or > 60000 || e.W < 0 || e.H < 0))
            throw new InvalidDataException("An emitter has a rate outside 0–2000 per second, a life outside 1–60000 ms or a negative area.");
        var fx = s.Lights.Select(l => l.Id).Concat(s.Occluders.Select(o => o.Id)).Concat(s.Emitters.Select(e => e.Id)).ToList();
        if (fx.Count != fx.Distinct().Count()) throw new InvalidDataException("The scene uses an id twice.");
        return s;
    }

    public static void Save(Scene s, string path) => DocumentFile.Save(path, Serialize(s), Version);
    public static Scene Load(string path) => DocumentFile.Load(path, "scene", Version, Deserialize);
}
