namespace PixelForge.Core;

/// <summary>Scene validation: broken references, things outside the scene, spawn points inside walls, overlapping solids.</summary>
public static class SceneCheck
{
    public sealed record Issue(string Severity, string Message);

    public static List<Issue> Run(Scene s, Project p)
    {
        var issues = new List<Issue>();
        void Error(string m) => issues.Add(new("error", m));
        void Warn(string m) => issues.Add(new("warning", m));

        var layerIds = s.Layers.Select(l => l.Id).ToHashSet();
        foreach (var l in s.Layers)
        {
            if (l.Kind != SceneLayerKind.Instances && (l.AssetId is not Guid id || p.Resolve(id) is null))
                Error($"Layer “{l.Name}” has no {l.Kind.ToString().ToLowerInvariant()} asset or it is missing.");
            else if (l.AssetId is Guid a && p.Find(a) is { } e && (l.Kind == SceneLayerKind.Tilemap) != (e.Type == AssetType.Tilemap))
                Error($"Layer “{l.Name}” refers to a {e.Type} asset.");
        }
        foreach (var i in s.Instances)
        {
            string name = i.Name.Length > 0 ? i.Name : i.Id.ToString()[..8];
            if (p.Find(i.AssetId) is not { } e || p.Resolve(i.AssetId) is null) Error($"Instance “{name}” refers to a missing asset.");
            else if (e.Type is not (AssetType.Sprite or AssetType.Character)) Error($"Instance “{name}” refers to a {e.Type}, not a sprite or character.");
            if (!layerIds.Contains(i.LayerId)) Error($"Instance “{name}” is on a layer that no longer exists.");
            if (i.X < 0 || i.Y < 0 || i.X >= s.Width || i.Y >= s.Height) Warn($"Instance “{name}” is outside the scene ({i.X}, {i.Y}).");
        }
        foreach (var a in s.Areas)
            if (a.X + a.W <= 0 || a.Y + a.H <= 0 || a.X >= s.Width || a.Y >= s.Height) Warn($"{a.Kind} area “{a.Name}” is outside the scene.");

        if (!s.Areas.Any(a => a.Kind == AreaKind.Spawn) && !s.Instances.Any(i => p.Find(i.AssetId)?.Type == AssetType.Character && Load<Character>(p, i.AssetId)?.Kind == CharacterKind.Player))
            Warn("No spawn area and no player character: where does the player start?");

        var solid = SolidCells(s, p);
        foreach (var a in s.Areas.Where(a => a.Kind is AreaKind.Spawn or AreaKind.Checkpoint))
            if (solid.Any(r => Overlaps(r, (a.X, a.Y, a.W, a.H)))) Warn($"{a.Kind} “{a.Name}” overlaps a solid tile: the player would start inside a wall.");

        // solid boxes of characters placed on top of each other or inside walls
        var placed = s.Instances.Select(i => (i, box: SolidBox(i, p))).Where(t => t.box is not null).ToList();
        for (int x = 0; x < placed.Count; x++)
        {
            var bx = placed[x].box!.Value;
            if (solid.Any(r => Overlaps(r, bx))) Warn($"“{Label(placed[x].i)}” starts inside a solid tile.");
            for (int y = x + 1; y < placed.Count; y++)
                if (Overlaps(bx, placed[y].box!.Value)) Warn($"“{Label(placed[x].i)}” and “{Label(placed[y].i)}” overlap.");
        }
        return issues;
    }

    static string Label(SceneInstance i) => i.Name.Length > 0 ? i.Name : i.Id.ToString()[..8];

    static bool Overlaps((int X, int Y, int W, int H) a, (int X, int Y, int W, int H) b) =>
        a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;

    static T? Load<T>(Project p, Guid id) where T : class
    {
        if (p.Resolve(id) is not string path) return null;
        try
        {
            return typeof(T) == typeof(Character) ? Character.Load(path) as T
                : typeof(T) == typeof(Tilemap) ? Tilemap.Load(path) as T : null;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException) { return null; }
    }

    /// <summary>Scene-space rectangles of solid tiles on tilemap layers that move with the world (parallax 1).</summary>
    public static List<(int X, int Y, int W, int H)> SolidCells(Scene s, Project p)
    {
        var cells = new List<(int, int, int, int)>();
        foreach (var l in s.Layers.Where(l => l.Kind == SceneLayerKind.Tilemap && l.ParallaxX == 1 && l.ParallaxY == 1 && l.AssetId is not null))
        {
            if (Load<Tilemap>(p, l.AssetId!.Value) is not { } m) continue;
            foreach (var layer in m.Layers.Where(x => x.Visible))
                for (int i = 0; i < layer.Tiles.Length; i++)
                    if (layer.Tiles[i] >= 0 && m.TileCollisions.TryGetValue(layer.Tiles[i], out var c) && !c.OneWay)
                        cells.Add((l.OffsetX + i % m.Width * m.TileSize + c.X, l.OffsetY + i / m.Width * m.TileSize + c.Y, c.W, c.H));
        }
        return cells;
    }

    /// <summary>A character's first solid box in scene space (origin at the instance position), or null.</summary>
    static (int X, int Y, int W, int H)? SolidBox(SceneInstance i, Project p)
    {
        if (Load<Character>(p, i.AssetId) is not { } c) return null;
        var b = c.BoxesAt(i.Animation ?? c.Animations.Keys.FirstOrDefault(), 0).FirstOrDefault(x => x.Kind == BoxKind.Solid);
        if (b is null) return null;
        int x = i.FlipX ? -(b.X + b.W - c.OriginX) : b.X - c.OriginX;
        return (i.X + x, i.Y + b.Y - c.OriginY, b.W, b.H);
    }
}
