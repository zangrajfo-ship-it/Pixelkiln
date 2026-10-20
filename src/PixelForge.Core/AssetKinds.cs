namespace PixelForge.Core;

/// <summary>
/// Everything PixelForge knows about one kind of asset file: extensions, how to check, describe and picture it,
/// which assets it refers to and how to point those references elsewhere. New formats register here instead of
/// adding a case to every switch.
/// </summary>
public sealed record AssetKind(
    AssetType Type,
    string[] Extensions,
    Action<string> Probe,
    Func<string, string> Describe,
    Func<string, Project?, (uint[] Pixels, int Width, int Height)?>? Picture = null,
    Func<string, IEnumerable<Guid>>? References = null,
    /// <summary>Loads, replaces referenced ids through the map, saves; returns whether anything changed.</summary>
    Func<string, Func<Guid, Guid>, bool>? Remap = null);

public static class AssetKinds
{
    static readonly List<AssetKind> _all =
    [
        new(AssetType.Sprite, [".pxf"], p => ProjectFile.Load(p), p => DescribeSprite(ProjectFile.Load(p)), (p, _) => AssetLoader.LoadImage(p)),
        new(AssetType.Image, [".png", ".gif", ".bmp"], p => ImageIO.LoadImage(p), p => ImageIO.LoadImage(p) is var (_, w, h) ? $"{w}×{h} image" : "",
            (p, _) => ImageIO.LoadImage(p)),
        new(AssetType.Tilemap, [".pxm"], p => Tilemap.Load(p), p => DescribeMap(Tilemap.Load(p)), MapPicture,
            p => Tilemap.Load(p).TilesetAssetId is Guid g ? [g] : [],
            (p, map) =>
            {
                var m = Tilemap.Load(p);
                if (m.TilesetAssetId is not Guid g || map(g) == g) return false;
                m.TilesetAssetId = map(g);
                Tilemap.Save(m, p);
                return true;
            }),
        new(AssetType.Song, [".pxs"], p => Song.Load(p), p => DescribeSong(Song.Load(p))),
        new(AssetType.Sound, [".sfx"], p => Sfx.Load(p), p => $"Sound effect, {Sfx.Load(p).Duration:0.00} s"),
        new(AssetType.Scene, [".pxscene"], p => Scene.Load(p), p => DescribeScene(Scene.Load(p)), ScenePicture,
            p => Scene.Load(p).ReferencedAssets(),
            (p, map) =>
            {
                var s = Scene.Load(p);
                bool changed = false;
                Guid M(Guid g) { var n = map(g); changed |= n != g; return n; }
                foreach (var l in s.Layers) if (l.AssetId is Guid g) l.AssetId = M(g);
                foreach (var i in s.Instances)
                {
                    i.AssetId = M(i.AssetId);
                    if (i.ClipAssetId is Guid clip) i.ClipAssetId = M(clip);
                }
                if (s.CameraClipAssetId is Guid cam) s.CameraClipAssetId = M(cam);
                if (s.MusicAssetId is Guid music) s.MusicAssetId = M(music);
                if (s.UiAssetId is Guid ui) s.UiAssetId = M(ui);
                if (changed) Scene.Save(s, p);
                return changed;
            }),
        new(AssetType.Character, [".pxchar"], p => Character.Load(p), p => DescribeCharacter(Character.Load(p)),
            (p, project) => project is not null && Character.Load(p).SpriteAssetId is Guid sid && project.Resolve(sid) is string sp ? AssetLoader.LoadImage(sp) : null,
            p => Character.Load(p) is var ch ? new[] { ch.SpriteAssetId, ch.RigAnimAssetId }.OfType<Guid>() : [],
            (p, map) =>
            {
                var c = Character.Load(p);
                Guid? M(Guid? g) => g is Guid x ? map(x) : null;
                if ((M(c.SpriteAssetId), M(c.RigAnimAssetId)) == (c.SpriteAssetId, c.RigAnimAssetId)) return false;
                (c.SpriteAssetId, c.RigAnimAssetId) = (M(c.SpriteAssetId), M(c.RigAnimAssetId));
                Character.Save(c, p);
                return true;
            }),
        new(AssetType.Palette, [".hex", ".gpl"], p => PaletteIO.Load(p), p => $"{PaletteIO.Load(p).Count} colors", (p, _) => AssetInfo.PaletteImage(PaletteIO.Load(p))),
        new(AssetType.Audio, [".wav"], p => WavReader.Load(p), p => $"WAV, {WavReader.Load(p).Length / (double)Synth.SampleRate:0.00} s"),
        new(AssetType.Paint, [".pxpaint"], p => PaintDoc.Load(p), p => PaintDoc.Load(p) is var d ? $"Painting {d.Width}×{d.Height} px, {Plural(d.Layers.Count, "layer")}" : "",
            (p, _) => PaintDoc.Load(p).Preview(512)),
        new(AssetType.Vector, [".pxvec"], p => VectorDoc.Load(p), p => VectorDoc.Load(p) is var d ? $"Vector {d.Width:0.##}×{d.Height:0.##}, {Plural(d.Shapes.Count, "shape")}" : "",
            (p, _) => VectorDoc.Load(p) is var d && Math.Max(d.Width, d.Height) is var longest && longest > 0
                ? ((int)Math.Max(1, d.Width * 256 / longest), (int)Math.Max(1, d.Height * 256 / longest)) is var (w, h) ? (VectorGeometry.Rasterize(d, w, h), w, h) : null
                : null),
        new(AssetType.Texture, [".pxtex"], p => TextureDoc.Load(p), p => TextureDoc.Load(p) is var t
                ? $"Texture {(t.Source == TextureSource.Procedural ? $"{t.Material} {t.Size}×{t.Size}" : $"from a {t.Source.ToString().ToLowerInvariant()}")}" : "",
            (p, project) => TextureDoc.Load(p) is var t && (t.Source == TextureSource.Procedural || project is not null)
                ? Textures.Build(t, project) is var m ? (m.Albedo, m.Width, m.Height) : null : null,
            p => TextureDoc.Load(p).AssetId is Guid a ? [a] : [],
            (p, map) =>
            {
                var t = TextureDoc.Load(p);
                if (t.AssetId is not Guid g || map(g) == g) return false;
                t.AssetId = map(g);
                TextureDoc.Save(t, p);
                return true;
            }),
        new(AssetType.Ui, [".pxui"], p => UiKit.Load(p), p => UiKit.Load(p) is var k ? $"UI kit, {Plural(k.Elements.Count, "element")}, {Plural(k.Screens.Count, "screen")}" : "",
            (p, project) => UiKit.Load(p) is var k && k.Screens.FirstOrDefault() is { } s ? (UiRender.Screen(k, s, s.Width, s.Height, project), s.Width, s.Height) : null,
            p => UiKit.Load(p).ReferencedAssets(),
            (p, map) =>
            {
                var k = UiKit.Load(p);
                bool changed = false;
                foreach (var e in k.Elements)
                {
                    foreach (var st in e.Art.Keys.ToList()) { var n = map(e.Art[st]); changed |= n != e.Art[st]; e.Art[st] = n; }
                    if (e.IconAssetId is Guid ic && map(ic) != ic) { e.IconAssetId = map(ic); changed = true; }
                }
                foreach (var i in k.Screens.SelectMany(s => s.Items))
                    if (i.ClipAssetId is Guid c && map(c) != c) { i.ClipAssetId = map(c); changed = true; }
                if (changed) UiKit.Save(k, p);
                return changed;
            }),
        new(AssetType.Anim, [".pxanim"], p => AnimLibrary.Load(p), p => AnimLibrary.Load(p) is var l
                ? $"Animations: {string.Join(", ", l.Clips.Take(6).Select(c => $"{c.Name} ({c.DurationMs / 1000.0:0.##} s)"))}{(l.Clips.Count > 6 ? " …" : "")}" : ""),
        new(AssetType.Mix, [".pxmix"], p => Mix.Load(p), p => DescribeMix(Mix.Load(p)), null,
            p => Mix.Load(p).ReferencedAssets(),
            (p, map) =>
            {
                var m = Mix.Load(p);
                bool changed = false;
                foreach (var c in m.Tracks.SelectMany(t => t.Clips))
                {
                    var n = map(c.AssetId);
                    changed |= n != c.AssetId;
                    c.AssetId = n;
                }
                if (changed) Mix.Save(m, p);
                return changed;
            }),
    ];

    public static IReadOnlyList<AssetKind> All => _all;

    /// <summary>Extension → type, rebuilt when a kind registers (looked up once per file when scanning folders).</summary>
    public static IReadOnlyDictionary<string, AssetType> ByExtension { get; private set; } = Index();

    static Dictionary<string, AssetType> Index() =>
        _all.SelectMany(k => k.Extensions.Select(e => (e, k.Type))).ToDictionary(x => x.e, x => x.Type, StringComparer.OrdinalIgnoreCase);

    public static AssetKind Of(AssetType type) => _all.First(k => k.Type == type);

    public static AssetKind? ForExtension(string ext) => _all.FirstOrDefault(k => k.Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase));

    /// <summary>Adds a new kind (each new format registers itself once, from its own file).</summary>
    public static void Register(AssetKind kind)
    {
        if (_all.Any(k => k.Type == kind.Type)) throw new InvalidOperationException($"{kind.Type} is registered twice.");
        _all.Add(kind);
        ByExtension = Index();
    }

    // ---------- descriptions ----------

    static string Plural(int n, string word, string plural = "s") => $"{n} {word}{(n == 1 ? "" : plural)}";

    static string DescribeSprite(Sprite s) =>
        $"{s.Width}×{s.Height} px, {Plural(s.Frames.Count, "frame")}, {Plural(s.Layers.Count, "layer")}, " +
        $"{s.Frames.Sum(f => f.DurationMs) / 1000.0:0.00} s" + (s.Tags.Count > 0 ? $", tags: {string.Join(", ", s.Tags.Select(t => t.Name))}" : "");

    static string DescribeMap(Tilemap m) => $"{m.Width}×{m.Height} tiles of {m.TileSize} px, {Plural(m.Layers.Count, "layer")}, {Plural(m.Objects.Count, "object")}";

    static string DescribeSong(Song s)
    {
        double seconds = s.Order.Count * s.Steps * s.StepSeconds;
        return $"{s.Bpm} BPM, {Plural(s.Patterns.Count, "pattern")}, {(int)seconds / 60}:{(int)seconds % 60:00}";
    }

    static string DescribeMix(Mix m) =>
        $"Mix, {m.Tracks.Count} tracks, {m.Tracks.Sum(t => t.Clips.Count)} clips{(m.LoopStartMs is int a ? $", loop {a / 1000.0:0.##}–{m.LoopEndMs / 1000.0:0.##} s" : "")}";

    static string DescribeScene(Scene s) =>
        $"Scene {s.Width}×{s.Height} px{(s.Level.Length > 0 ? $", level {s.Level} #{s.Order}" : "")}, {s.Layers.Count} layers, {s.Instances.Count} instances, {s.Areas.Count} areas";

    static string DescribeCharacter(Character c) => $"{c.Kind}, {Plural(c.Animations.Count, "state")}, {Plural(c.Boxes.Count, "box", "es")}";

    /// <summary>The whole scene at its camera-free size (needs the project for its assets); huge scenes get no picture.</summary>
    static (uint[], int, int)? ScenePicture(string path, Project? project)
    {
        if (project is null) return null;
        var s = Scene.Load(path);
        if ((long)s.Width * s.Height > 4_000_000) return null;
        return (SceneRenderer.Render(s, new SceneAssets(project), 0, 0, s.Width, s.Height, 0), s.Width, s.Height);
    }

    static (uint[], int, int)? MapPicture(string path, Project? project)
    {
        var m = Tilemap.Load(path);
        if (project is not null) AssetLoader.RefreshTileset(m, project);
        int w = m.Width * m.TileSize, h = m.Height * m.TileSize;
        if (m.Tileset is null || (long)w * h > 4_000_000) return null;   // ponytail: no picture for huge maps; render a crop if needed
        return (m.Render(), w, h);
    }
}
