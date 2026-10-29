using System.Security.Cryptography;
using System.Text;

namespace Pixelkiln.Core;

public sealed record AssetSize(AssetEntry Asset, long FileBytes, long MemoryBytes, int Width, int Height);

/// <summary>What the project costs and where it can shrink.</summary>
public sealed record OptimizeReport(
    List<AssetSize> Sizes, List<AssetEntry> Unused, List<List<AssetEntry>> Duplicates, List<(AssetEntry Asset, string Problem)> SizeProblems,
    int AtlasPages, long AtlasBytes, long LooseBytes, int DuplicateFrames, int EmptyFrames)
{
    static string Kb(long b) => b >= 1 << 20 ? $"{b / 1048576.0:0.0} MB" : $"{b / 1024.0:0.#} KB";

    public string Text()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Files: {Kb(Sizes.Sum(s => s.FileBytes))} on disk; pictures need {Kb(Sizes.Sum(s => s.MemoryBytes))} in memory (4 bytes a pixel).");
        sb.AppendLine($"Atlas: {AtlasPages} page(s), {Kb(AtlasBytes)} instead of {Kb(LooseBytes)} for separate pictures" +
            (LooseBytes > 0 ? $" ({100 - AtlasBytes * 100 / LooseBytes}% less)" : "") + $"; {DuplicateFrames} repeated and {EmptyFrames} empty frame(s) stored once or not at all.");
        sb.AppendLine();
        void Section<T>(string title, List<T> items, Func<T, string> line)
        {
            sb.AppendLine($"{title}: {(items.Count == 0 ? "none" : items.Count.ToString())}");
            foreach (var i in items) sb.AppendLine("  • " + line(i));
            sb.AppendLine();
        }
        Section("Largest assets", [.. Sizes.OrderByDescending(s => Math.Max(s.FileBytes, s.MemoryBytes)).Take(10)],
            s => $"{s.Asset.Path}: {Kb(s.FileBytes)} file" + (s.MemoryBytes > 0 ? $", {Kb(s.MemoryBytes)} in memory ({s.Width}×{s.Height})" : ""));
        Section("Not used by any scene, character, kit or playlist", Unused, a => $"{a.Path} ({Kb(Sizes.FirstOrDefault(s => s.Asset == a)?.FileBytes ?? 0)})");
        Section("Identical files", Duplicates, g => string.Join(" = ", g.Select(a => a.Path)));
        Section("Size problems", SizeProblems, x => $"{x.Asset.Path}: {x.Problem}");
        return sb.ToString();
    }
}

/// <summary>Project analysis for the Optimize window and the export atlas.</summary>
public static class Optimizer
{
    /// <summary>Every picture of the given assets for an atlas: sprite frames ("name#frame") and whole pictures.</summary>
    public static IEnumerable<Atlas.Item> Pictures(Project p, IEnumerable<AssetEntry> assets, Func<AssetEntry, string> key)
    {
        foreach (var a in assets.Where(a => AssetLoader.IsPicture(a.Type)).OrderBy(a => a.Path, StringComparer.Ordinal))
        {
            string path = p.FullPath(a);
            if (!File.Exists(path)) continue;
            if (a.Type == AssetType.Sprite)
            {
                var s = ProjectFile.Load(path);
                for (int f = 0; f < s.Frames.Count; f++) yield return new Atlas.Item($"{key(a)}#{f}", Compositor.Flatten(s, f), s.Width, s.Height);
            }
            else
            {
                var (px, w, h) = AssetLoader.LoadImage(path);
                yield return new Atlas.Item(key(a), px, w, h);
            }
        }
    }

    public static OptimizeReport Run(Project p, int maxTexture = 2048, CancellationToken ct = default)
    {
        var sizes = new List<AssetSize>();
        var problems = new List<(AssetEntry, string)>();
        var unreadable = new HashSet<AssetEntry>();
        var present = p.Assets.Where(a => File.Exists(p.FullPath(a))).ToList();
        foreach (var a in present)
        {
            ct.ThrowIfCancellationRequested();
            string path = p.FullPath(a);
            long bytes = new FileInfo(path).Length, memory = 0;
            int w = 0, h = 0;
            try
            {
                if (a.Type == AssetType.Sprite) { var s = ProjectFile.Load(path); (w, h) = (s.Width, s.Height); memory = 4L * w * h * s.Frames.Count; }
                else if (AssetLoader.IsPicture(a.Type)) { (_, w, h) = AssetLoader.LoadImage(path); memory = 4L * w * h; }
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
            {
                problems.Add((a, "cannot be read: " + ex.Message));
                unreadable.Add(a);
            }
            if (Math.Max(w, h) > maxTexture) problems.Add((a, $"{w}×{h} is larger than {maxTexture} px; many devices cannot load such a texture."));
            if (a.Type == AssetType.Texture && w > 0 && (!System.Numerics.BitOperations.IsPow2(w) || !System.Numerics.BitOperations.IsPow2(h)))
                problems.Add((a, $"{w}×{h}: repeating textures should be a power of two."));
            sizes.Add(new AssetSize(a, bytes, memory, w, h));
        }

        // used = referenced by another asset or a playlist; scenes are where the game starts, palettes are tools
        var used = present.SelectMany(p.ReferencesOf).Concat(p.Settings.Playlists.SelectMany(pl => pl.Items)).ToHashSet();
        var unused = present.Where(a => a.Type is not (AssetType.Scene or AssetType.Palette) && !used.Contains(a.Id)).OrderBy(a => a.Path).ToList();

        var duplicates = present.GroupBy(a => (a.Type, Hash: Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p.FullPath(a))))))
            .Where(g => g.Count() > 1).Select(g => g.OrderBy(a => a.Path).ToList()).ToList();

        ct.ThrowIfCancellationRequested();
        var items = Pictures(p, present.Where(a => !unreadable.Contains(a)), a => a.Path).ToList();
        int pages = 0, dupFrames = 0, empty = 0;
        long atlasBytes = 0, loose = items.Sum(i => 4L * i.W * i.H);
        var fitting = items.Where(i => Math.Max(i.W, i.H) + 2 <= maxTexture).ToList();   // with the atlas padding
        try
        {
            var atlas = Atlas.Pack(fitting, maxTexture);
            (pages, atlasBytes) = (atlas.Pages.Count, atlas.Pages.Sum(pg => 4L * pg.W * pg.H));
            (dupFrames, empty) = (atlas.Frames.Count(f => f.SameAs is not null), atlas.Frames.Count(f => f.Page < 0));
            loose = fitting.Sum(i => 4L * i.W * i.H);
        }
        catch (InvalidDataException) { }   // reported above as a size problem
        return new OptimizeReport(sizes, unused, duplicates, problems, pages, atlasBytes, loose, dupFrames, empty);
    }
}
