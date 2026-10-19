using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelForge.Core;

public enum AssetType { Sprite, Image, Tilemap, Song, Sound, Scene, Character, Palette, Audio, Mix }

/// <summary>One file of a project. <see cref="Id"/> never changes; <see cref="Path"/> is relative to the project folder.</summary>
public sealed class AssetEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AssetType Type { get; set; }
    public string Path { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public bool Favorite { get; set; }

    [JsonIgnore] public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
    [JsonIgnore] public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
}

public sealed class ProjectSettings
{
    /// <summary>Game resolution used by scene preview and camera.</summary>
    public int TargetWidth { get; set; } = 320;
    public int TargetHeight { get; set; } = 180;
    public int TileSize { get; set; } = 16;
    /// <summary>Collision categories: bit index (0..31) → name.</summary>
    public Dictionary<int, string> CollisionCategories { get; set; } = new() { [0] = "world", [1] = "player", [2] = "enemy", [3] = "pickup" };
    /// <summary>Category bit → bit mask of categories it collides with (missing = collides with everything).</summary>
    public Dictionary<int, uint> CollisionMasks { get; set; } = [];
    /// <summary>Music playlists for the game (songs, WAVs or mixes in order).</summary>
    public List<Playlist> Playlists { get; set; } = [];

    public bool Collides(int a, int b) =>
        (CollisionMasks.GetValueOrDefault(a, uint.MaxValue) & 1u << b) != 0 && (CollisionMasks.GetValueOrDefault(b, uint.MaxValue) & 1u << a) != 0;
}

/// <summary>
/// A project = a folder with a .pfproj manifest listing its assets by stable id (ADR-0001).
/// Asset files keep their own formats; references between assets use ids, never paths.
/// </summary>
public sealed class Project
{
    public const int SchemaVersion = 1;
    public const string Extension = ".pfproj";
    /// <summary>Folders inside the project that are never assets.</summary>
    public static readonly string[] HiddenFolders = [".history", ".trash", "export"];

    public static readonly IReadOnlyDictionary<string, AssetType> TypesByExtension = new Dictionary<string, AssetType>(StringComparer.OrdinalIgnoreCase)
    {
        [".pxf"] = AssetType.Sprite, [".png"] = AssetType.Image, [".gif"] = AssetType.Image, [".bmp"] = AssetType.Image,
        [".pxm"] = AssetType.Tilemap, [".pxs"] = AssetType.Song, [".sfx"] = AssetType.Sound,
        [".pxscene"] = AssetType.Scene, [".pxchar"] = AssetType.Character, [".hex"] = AssetType.Palette, [".gpl"] = AssetType.Palette,
        [".wav"] = AssetType.Audio, [".pxmix"] = AssetType.Mix,
    };

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Game";
    public ProjectSettings Settings { get; set; } = new();
    public List<AssetEntry> Assets { get; set; } = [];

    /// <summary>Full path of the .pfproj file (not saved).</summary>
    [JsonIgnore] public string FilePath { get; private set; } = "";
    [JsonIgnore] public string Root => System.IO.Path.GetDirectoryName(FilePath)!;

    // ---------- create / load / save ----------

    public static Project Create(string folder, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Use a project name without \\ / : * ? \" < > |.", nameof(name));
        Directory.CreateDirectory(folder);
        string file = System.IO.Path.Combine(folder, name + Extension);
        if (File.Exists(file)) throw new IOException($"A project “{name}” already exists in that folder.");
        var p = new Project { Name = name, FilePath = System.IO.Path.GetFullPath(file) };
        p.Save();
        return p;
    }

    /// <summary>New project in a folder that already has PixelForge files: they are all registered, none are changed.</summary>
    public static Project CreateFromFolder(string folder, string name)
    {
        var p = Create(folder, name);
        foreach (var f in p.UnregisteredFiles()) p.Add(f);
        p.Save();
        return p;
    }

    record Dto(int Version, Guid Id, string Name, ProjectSettings Settings, List<AssetEntry> Assets);

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static Project Load(string path)
    {
        var p = DocumentFile.Load(path, "project", SchemaVersion, json =>
        {
            var d = JsonSerializer.Deserialize<Dto>(json, Json) ?? throw new InvalidDataException("Empty project file.");
            return new Project { Id = d.Id, Name = d.Name ?? "Game", Settings = d.Settings ?? new(), Assets = d.Assets ?? [] };
        });
        p.FilePath = System.IO.Path.GetFullPath(path);
        p.Validate();
        return p;
    }

    public void Save() => DocumentFile.Save(FilePath, JsonSerializer.Serialize(new Dto(SchemaVersion, Id, Name, Settings, Assets), Json), SchemaVersion);

    /// <summary>Duplicate ids and paths that leave the project folder make a manifest unusable; reject them.</summary>
    void Validate()
    {
        var dup = Assets.GroupBy(a => a.Id).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) throw new InvalidDataException($"The project lists asset id {dup.Key} more than once.");
        foreach (var a in Assets)
            if (!IsInside(a.Path)) throw new InvalidDataException($"Asset path “{a.Path}” points outside the project folder.");
    }

    static bool IsInside(string relative) =>
        !string.IsNullOrWhiteSpace(relative) && !System.IO.Path.IsPathRooted(relative)
        && !relative.Replace('\\', '/').Split('/').Contains("..");

    // ---------- lookup ----------

    public AssetEntry? Find(Guid id) => Assets.FirstOrDefault(a => a.Id == id);

    public string FullPath(AssetEntry a) => System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, a.Path));

    /// <summary>Full path of an asset id, or null if the id is unknown or its file is missing.</summary>
    public string? Resolve(Guid id) => Find(id) is { } a && File.Exists(FullPath(a)) ? FullPath(a) : null;

    public AssetEntry? FindByPath(string fullPath)
    {
        string rel = Relative(fullPath);
        return Assets.FirstOrDefault(a => string.Equals(a.Path, rel, StringComparison.OrdinalIgnoreCase));
    }

    string Relative(string fullPath) => System.IO.Path.GetRelativePath(Root, System.IO.Path.GetFullPath(fullPath));

    // ---------- changes (each one saves the manifest) ----------

    /// <summary>Registers a file inside the project folder (returns the existing entry if already registered).</summary>
    public AssetEntry Add(string fullPath)
    {
        string rel = Relative(fullPath);
        if (!IsInside(rel)) throw new InvalidOperationException("Copy the file into the project folder first; projects only reference their own files.");
        if (!TypesByExtension.TryGetValue(System.IO.Path.GetExtension(rel), out var type))
            throw new InvalidOperationException($"“{System.IO.Path.GetExtension(rel)}” files are not project assets.");
        if (FindByPath(fullPath) is { } existing) return existing;
        var a = new AssetEntry { Type = type, Path = rel };
        Assets.Add(a);
        Save();
        return a;
    }

    /// <summary>Renames the file (same folder, same extension); the id and every reference stay valid.</summary>
    public void Rename(Guid id, string newName)
    {
        var a = Find(id) ?? throw new KeyNotFoundException("Unknown asset.");
        if (string.IsNullOrWhiteSpace(newName) || newName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Use a name without \\ / : * ? \" < > |.", nameof(newName));
        MoveTo(a, System.IO.Path.Combine(a.Folder, newName.Trim() + System.IO.Path.GetExtension(a.Path)));
    }

    /// <summary>Moves the file to another folder of the project (created if needed).</summary>
    public void Move(Guid id, string folder)
    {
        var a = Find(id) ?? throw new KeyNotFoundException("Unknown asset.");
        string rel = System.IO.Path.Combine(folder.Trim(), System.IO.Path.GetFileName(a.Path));
        if (!IsInside(rel)) throw new ArgumentException("The folder must be inside the project.", nameof(folder));
        MoveTo(a, rel);
    }

    void MoveTo(AssetEntry a, string newRel)
    {
        if (string.Equals(newRel, a.Path, StringComparison.OrdinalIgnoreCase)) return;
        string from = FullPath(a), to = System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, newRel));
        if (File.Exists(to)) throw new IOException($"“{newRel}” already exists.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to)!);
        if (File.Exists(from)) File.Move(from, to);   // a missing file can still be renamed in the manifest
        a.Path = newRel;
        Save();
    }

    /// <summary>Copies an asset under a new id ("name copy").</summary>
    public AssetEntry Duplicate(Guid id)
    {
        var a = Find(id) ?? throw new KeyNotFoundException("Unknown asset.");
        string ext = System.IO.Path.GetExtension(a.Path), baseRel = System.IO.Path.Combine(a.Folder, a.Name + " copy");
        string rel = baseRel + ext;
        for (int n = 2; File.Exists(System.IO.Path.Combine(Root, rel)); n++) rel = $"{baseRel} {n}{ext}";
        File.Copy(FullPath(a), System.IO.Path.Combine(Root, rel));
        var copy = new AssetEntry { Type = a.Type, Path = rel, Tags = [.. a.Tags] };
        Assets.Add(copy);
        Save();
        return copy;
    }

    /// <summary>Removes an asset from the project. The file is moved to .trash (never deleted), so it can be recovered.</summary>
    public void Remove(Guid id)
    {
        var a = Find(id) ?? throw new KeyNotFoundException("Unknown asset.");
        string from = FullPath(a);
        if (File.Exists(from))
        {
            string trash = System.IO.Path.Combine(Root, ".trash");
            Directory.CreateDirectory(trash);
            string to = System.IO.Path.Combine(trash, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{System.IO.Path.GetFileName(from)}");
            File.Move(from, to);
        }
        Assets.Remove(a);
        Save();
    }

    // ---------- health ----------

    /// <summary>Entries whose file is gone.</summary>
    public IEnumerable<AssetEntry> MissingAssets() => Assets.Where(a => !File.Exists(FullPath(a)));

    /// <summary>Asset files in the project folder that the manifest does not list.</summary>
    public IEnumerable<string> UnregisteredFiles()
    {
        var known = Assets.Select(a => a.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Where(f => TypesByExtension.ContainsKey(System.IO.Path.GetExtension(f)))
            .Select(f => System.IO.Path.GetRelativePath(Root, f))
            .Where(rel => !HiddenFolders.Any(h => rel.StartsWith(h + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            .Where(rel => !known.Contains(rel))
            .Select(rel => System.IO.Path.Combine(Root, rel))
            .ToList();
    }

    /// <summary>Asset ids another asset refers to (empty for types without references).</summary>
    public IEnumerable<Guid> ReferencesOf(AssetEntry a)
    {
        string path = FullPath(a);
        if (!File.Exists(path)) return [];
        try
        {
            return a.Type switch
            {
                AssetType.Tilemap => Tilemap.Load(path).TilesetAssetId is Guid g ? [g] : [],
                AssetType.Character => Character.Load(path).SpriteAssetId is Guid c ? [c] : [],
                AssetType.Scene => Scene.Load(path).ReferencedAssets(),
                AssetType.Mix => Mix.Load(path).ReferencedAssets(),
                _ => [],
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException) { return []; }   // damaged files are reported by Check
    }

    /// <summary>Which assets use <paramref name="id"/> (back-references for the asset browser).</summary>
    public IEnumerable<AssetEntry> UsersOf(Guid id) => Assets.Where(a => ReferencesOf(a).Contains(id));

    /// <summary>References to ids that are not in the project (or whose file is missing).</summary>
    public IEnumerable<(AssetEntry From, Guid To)> BrokenReferences() =>
        Assets.SelectMany(a => ReferencesOf(a).Where(r => Resolve(r) is null).Select(r => (a, r)));

    /// <summary>Tries to load every asset; returns the ones that fail with the reason.</summary>
    public IEnumerable<(AssetEntry Asset, string Problem)> DamagedAssets()
    {
        foreach (var a in Assets)
        {
            string path = FullPath(a);
            if (!File.Exists(path)) continue;
            string? problem = null;
            try { AssetLoader.Probe(a.Type, path); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException or FormatException) { problem = ex.Message; }
            if (problem is not null) yield return (a, problem);
        }
    }

    // ---------- search ----------

    /// <summary>Case-insensitive match on name, folder and tags; optional type filter; favorites first.</summary>
    public IEnumerable<AssetEntry> Search(string? text, AssetType? type = null, bool favoritesOnly = false)
    {
        var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Assets
            .Where(a => (type is null || a.Type == type) && (!favoritesOnly || a.Favorite))
            .Where(a => words.All(w => a.Path.Contains(w, StringComparison.OrdinalIgnoreCase)
                                    || a.Tags.Any(t => t.Contains(w, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(a => a.Favorite).ThenBy(a => a.Path, StringComparer.OrdinalIgnoreCase);
    }
}
