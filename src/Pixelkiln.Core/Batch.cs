namespace Pixelkiln.Core;

/// <summary>
/// Changes to many assets at once (asset browser multi-selection). Each method checks everything first and only then
/// changes files, so a bad pattern or size never leaves half the selection done.
/// </summary>
public static class Batch
{
    /// <summary>
    /// Renames with a pattern: {name} = current name, {n} = position in the selection (zero-padded to the count's width).
    /// Returns (old full path, new full path) per renamed file.
    /// </summary>
    public static List<(string From, string To)> Rename(Project p, IReadOnlyList<Guid> ids, string pattern)
    {
        if (!pattern.Contains("{n}") && !pattern.Contains("{name}") && ids.Count > 1)
            throw new ArgumentException("The pattern needs {n} or {name}, otherwise every file would get the same name.", nameof(pattern));
        int width = ids.Count.ToString().Length;
        var plan = ids.Select(id => p.Find(id) ?? throw new KeyNotFoundException("Unknown asset."))
            .Select((e, i) => (Entry: e, Name: pattern.Replace("{name}", e.Name).Replace("{n}", (i + 1).ToString().PadLeft(width, '0')).Trim())).ToList();
        var targets = plan.Select(x => System.IO.Path.Combine(x.Entry.Folder, x.Name + System.IO.Path.GetExtension(x.Entry.Path))).ToList();
        if (plan.Any(x => x.Name.Length == 0 || x.Name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0))
            throw new ArgumentException("A resulting name is empty or contains \\ / : * ? \" < > |.", nameof(pattern));
        if (targets.Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Count)
            throw new ArgumentException("Two files would get the same name.", nameof(pattern));
        var moving = plan.Select(x => x.Entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var clash = targets.FirstOrDefault(t => !moving.Contains(t) && File.Exists(System.IO.Path.Combine(p.Root, t)));
        if (clash is not null) throw new IOException($"“{clash}” already exists.");

        // via temporary names, so a → b, b → a style swaps work
        var result = new List<(string, string)>();
        var temp = plan.Select(x => (x.Entry, Old: p.FullPath(x.Entry))).ToList();
        foreach (var (e, _) in temp) p.Rename(e.Id, $"~pf{Guid.NewGuid():N}");
        for (int i = 0; i < plan.Count; i++)
        {
            p.Rename(plan[i].Entry.Id, plan[i].Name);
            result.Add((temp[i].Old, p.FullPath(plan[i].Entry)));
        }
        return result;
    }

    public static void Tag(Project p, IEnumerable<Guid> ids, IEnumerable<string> add, IEnumerable<string> remove)
    {
        var plus = add.Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        var minus = remove.Select(t => t.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var a in ids.Select(p.Find).OfType<AssetEntry>())
            a.Tags = [.. a.Tags.Where(t => !minus.Contains(t)).Concat(plus).Distinct(StringComparer.OrdinalIgnoreCase)];
        p.Save();
    }

    /// <summary>Scales sprites (all frames and layers, nearest neighbour) by a percentage. Returns the files changed.</summary>
    public static List<string> ScaleSprites(Project p, IEnumerable<Guid> ids, int percent)
    {
        if (percent is < 1 or > 1600) throw new ArgumentOutOfRangeException(nameof(percent), "Use 1–1600 %.");
        var sprites = ids.Select(p.Find).OfType<AssetEntry>().Where(a => a.Type == AssetType.Sprite).Select(a => (Path: p.FullPath(a), Sprite: ProjectFile.Load(p.FullPath(a)))).ToList();
        var sized = sprites.Select(x => (x.Path, W: Math.Max(1, x.Sprite.Width * percent / 100), H: Math.Max(1, x.Sprite.Height * percent / 100), x.Sprite)).ToList();
        if (sized.Any(x => x.W > 4096 || x.H > 4096)) throw new ArgumentOutOfRangeException(nameof(percent), "A sprite would be larger than 4096 px.");
        foreach (var x in sized) ProjectFile.Save(PixelOps.Scaled(x.Sprite, x.W, x.H), x.Path);
        return [.. sized.Select(x => x.Path)];
    }

    /// <summary>
    /// Images become editable sprites and sprites become PNG pictures (first frame), as new files next to the
    /// originals. Returns the new assets; existing files are never overwritten.
    /// </summary>
    public static List<AssetEntry> Convert(Project p, IEnumerable<Guid> ids)
    {
        var made = new List<AssetEntry>();
        foreach (var a in ids.Select(p.Find).OfType<AssetEntry>().Where(a => a.Type is AssetType.Image or AssetType.Sprite).ToList())
        {
            string src = p.FullPath(a), ext = a.Type == AssetType.Image ? ".pxf" : ".png";
            string dest = System.IO.Path.ChangeExtension(src, ext);
            for (int n = 2; File.Exists(dest); n++) dest = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(src)!, $"{a.Name} {n}{ext}");
            var (px, w, h) = AssetLoader.LoadImage(src);
            if (a.Type == AssetType.Image)
            {
                var s = new Sprite(w, h);
                Array.Copy(px, s.Cel(0, 0), px.Length);
                ProjectFile.Save(s, dest);
            }
            else ImageIO.SavePng(dest, px, w, h);
            made.Add(p.Add(dest));
        }
        return made;
    }
}
