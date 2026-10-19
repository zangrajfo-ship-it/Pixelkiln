using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PixelForge.Core;

public sealed record VersionEntry(string Hash, DateTime Utc, string Note, long Size);

/// <summary>
/// Saved versions of project assets in &lt;project&gt;/.history (ADR-0003): gzip objects named by SHA-256 (identical
/// content is stored once), an index per asset, and limits on versions per asset and total size.
/// </summary>
public sealed class VersionStore(Project project)
{
    public const int DefaultMaxVersions = 50;
    public const int DefaultMaxMegabytes = 500;

    readonly string _dir = Path.Combine(project.Root, ".history");
    Dictionary<Guid, List<VersionEntry>>? _index;

    public int MaxVersions => Math.Max(1, project.Settings.HistoryMaxVersions);
    public long MaxBytes => Math.Max(1, project.Settings.HistoryMaxMegabytes) * 1024L * 1024;

    string IndexPath => Path.Combine(_dir, "index.json");
    string ObjectPath(string hash) => Path.Combine(_dir, "objects", hash);

    Dictionary<Guid, List<VersionEntry>> Index
    {
        get
        {
            if (_index is not null) return _index;
            try
            {
                _index = File.Exists(IndexPath) ? JsonSerializer.Deserialize<Dictionary<Guid, List<VersionEntry>>>(File.ReadAllText(IndexPath)) ?? [] : [];
            }
            catch (JsonException) { _index = []; }   // a damaged index only loses history, never the project
            return _index;
        }
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<VersionEntry> Versions(Guid assetId) =>
        Index.TryGetValue(assetId, out var l) ? [.. Enumerable.Reverse(l)] : [];

    /// <summary>Stores the asset's current file as a version unless it equals the newest one. Returns the entry or null.</summary>
    public VersionEntry? Record(Guid assetId, string note = "")
    {
        if (project.Find(assetId) is not { } a) return null;
        string path = project.FullPath(a);
        if (!File.Exists(path)) return null;
        byte[] data = File.ReadAllBytes(path);
        string hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var list = Index.TryGetValue(assetId, out var l) ? l : Index[assetId] = [];
        if (list.Count > 0 && list[^1].Hash == hash) return null;
        Directory.CreateDirectory(Path.Combine(_dir, "objects"));
        if (!File.Exists(ObjectPath(hash)))
        {
            string tmp = ObjectPath(hash) + ".tmp";
            using (var f = File.Create(tmp))
            using (var gz = new GZipStream(f, CompressionLevel.Optimal))
                gz.Write(data);
            File.Move(tmp, ObjectPath(hash), overwrite: true);
        }
        var e = new VersionEntry(hash, DateTime.UtcNow, note, data.Length);
        list.Add(e);
        Prune();
        Save();
        return e;
    }

    /// <summary>Content of a version (checked against its hash).</summary>
    public byte[] Read(VersionEntry e)
    {
        if (!File.Exists(ObjectPath(e.Hash))) throw new InvalidDataException("This version's data is missing from .history.");
        using var gz = new GZipStream(File.OpenRead(ObjectPath(e.Hash)), CompressionMode.Decompress);
        using var ms = new MemoryStream();
        gz.CopyTo(ms);
        var data = ms.ToArray();
        if (Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant() != e.Hash) throw new InvalidDataException("This version's data is damaged.");
        return data;
    }

    /// <summary>Puts a version back: the current file is recorded first, so the restore itself can be undone.</summary>
    public void Restore(Guid assetId, VersionEntry e)
    {
        var a = project.Find(assetId) ?? throw new InvalidOperationException("The asset is not in the project.");
        var data = Read(e);
        Record(assetId, "before restore");
        string path = project.FullPath(a), tmp = path + ".tmp";
        File.WriteAllBytes(tmp, data);
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
        Record(assetId, $"restored version from {e.Utc:yyyy-MM-dd HH:mm:ss} UTC");
    }

    public long TotalBytes => Directory.Exists(Path.Combine(_dir, "objects"))
        ? new DirectoryInfo(Path.Combine(_dir, "objects")).EnumerateFiles().Sum(f => f.Length) : 0;

    /// <summary>Oldest versions go first: per asset over MaxVersions, then globally until the objects fit MaxBytes.</summary>
    void Prune()
    {
        foreach (var list in Index.Values)
            if (list.Count > MaxVersions) list.RemoveRange(0, list.Count - MaxVersions);
        DeleteUnreferenced();
        while (TotalBytes > MaxBytes)
        {
            // the oldest entry of any asset that still has more than its newest version
            var victim = Index.Values.Where(l => l.Count > 1).Select(l => (l, e: l[0])).OrderBy(x => x.e.Utc).FirstOrDefault();
            if (victim.l is null) break;
            victim.l.RemoveAt(0);
            DeleteUnreferenced();
        }
    }

    void DeleteUnreferenced()
    {
        string objects = Path.Combine(_dir, "objects");
        if (!Directory.Exists(objects)) return;
        var used = Index.Values.SelectMany(l => l).Select(e => e.Hash).ToHashSet();
        foreach (var f in Directory.EnumerateFiles(objects).Where(f => !f.EndsWith(".tmp") && !used.Contains(Path.GetFileName(f))).ToList()) File.Delete(f);
    }

    void Save()
    {
        Directory.CreateDirectory(_dir);
        DocumentFile.WriteAtomic(IndexPath, JsonSerializer.Serialize(Index, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>Differences between two versions of an asset: pixels for pictures and maps, changed fields for everything else.</summary>
public static class VersionDiff
{
    public sealed record Picture(uint[] Pixels, int Width, int Height);

    /// <summary>Changed pixels shown in magenta over a dimmed copy of b; also the count. Sizes may differ.</summary>
    public static (Picture Image, int Changed) Pixels(Picture a, Picture b)
    {
        int w = Math.Max(a.Width, b.Width), h = Math.Max(a.Height, b.Height), changed = 0;
        var o = new uint[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                uint pa = x < a.Width && y < a.Height ? a.Pixels[y * a.Width + x] : 0, pb = x < b.Width && y < b.Height ? b.Pixels[y * b.Width + x] : 0;
                if (pa != pb) { changed++; o[y * w + x] = 0xFFFF00FF; }
                else o[y * w + x] = pb >> 24 == 0 ? 0 : 0x60000000 | pb & 0x00FFFFFF;
            }
        return (new Picture(o, w, h), changed);
    }

    /// <summary>Picture of a version for visual comparison (sprite: first frame; map: rendered; image as is); null for non-visual assets.</summary>
    public static Picture? Render(AssetType type, byte[] data, Project? p = null)
    {
        string Text() => Encoding.UTF8.GetString(data).TrimStart('﻿');
        switch (type)
        {
            case AssetType.Sprite:
            {
                var s = ProjectFile.Deserialize(Text());
                return new Picture(Compositor.Flatten(s, 0), s.Width, s.Height);
            }
            case AssetType.Tilemap:
            {
                var m = Tilemap.Deserialize(Text());
                if (p is not null) AssetLoader.RefreshTileset(m, p);
                return new Picture(m.Render(), m.Width * m.TileSize, m.Height * m.TileSize);
            }
            case AssetType.Image:
            {
                var (px, w, h) = ImageIO.FromBitmap(System.Windows.Media.Imaging.BitmapFrame.Create(new MemoryStream(data),
                    System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad));
                return new Picture(px, w, h);
            }
            default: return null;
        }
    }

    /// <summary>Readable list of what changed between two JSON documents (paths like Layers[1].Name), at most max lines.</summary>
    public static List<string> Fields(byte[] a, byte[] b, int max = 200)
    {
        var o = new List<string>();
        JsonNode? na, nb;
        try { (na, nb) = (JsonNode.Parse(a), JsonNode.Parse(b)); }
        catch (JsonException) { return [a.AsSpan().SequenceEqual(b) ? "identical" : $"binary content differs ({a.Length} → {b.Length} bytes)"]; }
        Walk("", na, nb, o, max);
        return o.Count == 0 ? ["identical"] : o;
    }

    static string Short(JsonNode? n)
    {
        string s = n?.ToJsonString() ?? "null";
        return s.Length > 60 ? s[..57] + "…" : s;
    }

    static void Walk(string path, JsonNode? a, JsonNode? b, List<string> o, int max)
    {
        if (o.Count >= max) return;
        switch (a, b)
        {
            case (JsonObject oa, JsonObject ob):
                foreach (var key in oa.Select(kv => kv.Key).Union(ob.Select(kv => kv.Key)))
                {
                    string p = path.Length == 0 ? key : $"{path}.{key}";
                    bool inA = oa.ContainsKey(key), inB = ob.ContainsKey(key);
                    if (!inB) o.Add($"− {p} (removed)");
                    else if (!inA) o.Add($"+ {p} = {Short(ob[key])}");
                    else Walk(p, oa[key], ob[key], o, max);
                    if (o.Count >= max) { o.Add("…"); return; }
                }
                break;
            case (JsonArray aa, JsonArray ab):
                for (int i = 0; i < Math.Max(aa.Count, ab.Count); i++)
                {
                    if (i >= ab.Count) o.Add($"− {path}[{i}] (removed)");
                    else if (i >= aa.Count) o.Add($"+ {path}[{i}] = {Short(ab[i])}");
                    else Walk($"{path}[{i}]", aa[i], ab[i], o, max);
                    if (o.Count >= max) { o.Add("…"); return; }
                }
                break;
            default:
                if (!JsonNode.DeepEquals(a, b)) o.Add(a is JsonValue va && va.TryGetValue<string>(out var sa) && sa.Length > 60
                    ? $"~ {path} (data changed)" : $"~ {path}: {Short(a)} → {Short(b)}");
                break;
        }
    }
}
