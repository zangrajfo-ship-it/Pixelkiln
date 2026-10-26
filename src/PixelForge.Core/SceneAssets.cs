namespace PixelForge.Core;

/// <summary>What to draw for a scene instance: its sprite, the pivot inside it, and the frames of its animation.</summary>
public sealed record InstanceLook(Sprite Sprite, int OriginX, int OriginY, int From, int To, Character? Character);

/// <summary>
/// Loads (and caches) the assets a scene uses: tilemap pictures, images, sprites and characters.
/// The cache key includes the file's write time, so edits made in other tabs show up.
/// </summary>
public sealed class SceneAssets(Project project)
{
    readonly Dictionary<string, object?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public Project Project { get; } = project;

    T? Get<T>(Guid id, Func<string, T> load) where T : class
    {
        if (Project.Resolve(id) is not string path) return null;
        string key = $"{typeof(T).Name}|{path}|{File.GetLastWriteTimeUtc(path).Ticks}";
        if (_cache.TryGetValue(key, out var hit)) return hit as T;
        T? value;
        try { value = load(path); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException) { value = null; }
        _cache[key] = value;
        return value;
    }

    /// <summary>Rendered tilemap (its tileset taken from the project when linked).</summary>
    public (uint[] Pixels, int Width, int Height)? TilemapImage(Guid id) => Get(id, path =>
    {
        var m = Core.Tilemap.Load(path);
        AssetLoader.RefreshTileset(m, Project);
        return new Box<(uint[], int, int)>((m.Render(), m.Width * m.TileSize, m.Height * m.TileSize));
    })?.Value;

    public (uint[] Pixels, int Width, int Height)? Image(Guid id) => Get(id, path => new Box<(uint[], int, int)>(
        path.EndsWith(".pxtex", StringComparison.OrdinalIgnoreCase) && Textures.Build(TextureDoc.Load(path), Project) is var m ? (m.Albedo, m.Width, m.Height) : AssetLoader.LoadImage(path)))?.Value;

    public Sprite? Sprite(Guid id) => Get(id, ProjectFile.Load);

    public Character? Character(Guid id) => Get(id, Core.Character.Load);

    public AnimLibrary? Anim(Guid id) => Get(id, AnimLibrary.Load);

    public LogicGraph? Logic(Guid id) => Get(id, LogicGraph.Load);

    /// <summary>Collision rectangles of a tilemap's visible tiles, in map pixels (cached with the file).</summary>
    List<Collider>? TileSolids(Guid id) => Get(id, path =>
    {
        var m = Core.Tilemap.Load(path);
        var o = new List<Collider>();
        foreach (var layer in m.Layers.Where(x => x.Visible))
            for (int i = 0; i < layer.Tiles.Length; i++)
                if (layer.Tiles[i] >= 0 && m.TileCollisions.TryGetValue(layer.Tiles[i], out var c))
                    o.Add(new(i % m.Width * m.TileSize + c.X, i / m.Width * m.TileSize + c.Y, c.W, c.H, c.OneWay));
        return o;
    });

    /// <summary>Solid tiles of the scene's world-speed tilemap layers, in scene pixels (collisions and shadows).</summary>
    public IEnumerable<Collider> Solids(Scene s) =>
        s.Layers.Where(l => l.Kind == SceneLayerKind.Tilemap && l.ParallaxX == 1 && l.ParallaxY == 1 && l.AssetId is not null)
            .SelectMany(l => (TileSolids(l.AssetId!.Value) ?? []).Select(c => c with { X = c.X + l.OffsetX, Y = c.Y + l.OffsetY }));

    /// <summary>The clip a scene object plays (its library clip, or null when it uses its own track or none).</summary>
    public AnimClip? ClipOf(SceneInstance i) => i.ClipAssetId is Guid id ? Anim(id)?.Clip(i.Clip) : null;

    /// <summary>The body-part clip of a rigged character in a state (null = parts at rest).</summary>
    public AnimClip? RigClip(Character c, string? state) =>
        c.RigAnimAssetId is Guid id && Anim(id) is { } lib ? lib.Clip(state is not null && c.RigClips.TryGetValue(state, out var n) ? n : c.RigClips.Values.FirstOrDefault()) : null;

    /// <summary>Sprite, pivot and frame range for an instance; null when its asset is missing or unreadable.</summary>
    public InstanceLook? Look(SceneInstance i)
    {
        var entry = Project.Find(i.AssetId);
        if (entry?.Type == AssetType.Character)
        {
            if (Character(i.AssetId) is not { } c || c.SpriteAssetId is not Guid sid || Sprite(sid) is not { } cs) return null;
            string? state = i.Animation ?? c.Animations.Keys.FirstOrDefault();
            var range = state is not null ? c.FramesOf(state, cs) : null;
            var (f, t) = range ?? (0, cs.Frames.Count - 1);
            return new InstanceLook(cs, c.OriginX, c.OriginY, f, t, c);
        }
        if (Sprite(i.AssetId) is not { } s) return null;
        var tag = i.Animation is null ? null : s.Tags.FirstOrDefault(x => x.Name == i.Animation);
        return new InstanceLook(s, 0, 0, tag?.From ?? 0, tag?.To ?? s.Frames.Count - 1, null);
    }

    /// <summary>Sprite frame shown at a time (ms) inside a looping frame range, using the frames' own durations.</summary>
    public static int FrameAt(Sprite s, int from, int to, double timeMs)
    {
        int total = 0;
        for (int f = from; f <= to; f++) total += s.Frames[f].DurationMs;
        if (total <= 0) return from;
        double t = (timeMs % total + total) % total;
        for (int f = from; f <= to; f++)
        {
            t -= s.Frames[f].DurationMs;
            if (t < 0) return f;
        }
        return to;
    }

    sealed class Box<T>(T value) { public T Value { get; } = value; }
}
