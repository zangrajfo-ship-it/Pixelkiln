using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PixelForge.Core;

public enum TemplateKind { Character, Level, Scene, Asset, Music, Pack }
public enum TemplateSource { BuiltIn, Project, User }

/// <summary>A value the user picks before creating (Options = choice list; Source = choose a project asset of that type).</summary>
public sealed record TemplateParam(string Key, string Label, string Default, List<string>? Options = null, AssetType? Source = null);

public sealed record TemplateState(string Name, int Frames = 1);

/// <summary>Character box: Rect is a keyword (body, feet, front, head, full) or four fractions "x y w h" of the sprite size.</summary>
public sealed record TemplateBox(BoxKind Kind, string Rect, string? State = null, int Category = 0);

/// <summary>
/// One asset the recipe makes. Type decides which Settings keys are allowed (see TemplateLoader.StepKeys).
/// Values may contain {param} placeholders; Ref-like settings name earlier steps by Id.
/// </summary>
public sealed record TemplateStep(
    string Id, string Type, string? Name = null, bool Core = false,
    Dictionary<string, string>? Settings = null, List<TemplateState>? States = null, List<TemplateBox>? Boxes = null,
    Dictionary<string, string>? Properties = null, string? File = null, Guid? OriginalId = null);

/// <summary>template.json. Data only, like plugins (ADR-0002): steps choose from built-in generators and copies.</summary>
public sealed record TemplateManifest(
    int ApiVersion, string Id, string Name, string Version, TemplateKind Kind, string Primary, List<TemplateStep> Steps,
    List<string>? Tags = null, string? Description = null, List<TemplateParam>? Parameters = null);

public sealed record TemplateInfo(TemplateManifest Manifest, TemplateSource Source, string? Folder)
{
    public string Key => $"{Manifest.Id}@{Manifest.Version}";
}

public static class TemplateLoader
{
    public const int ApiVersion = 1;
    public const long MaxManifestBytes = 1024 * 1024;
    static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9._-]{1,63}$");
    static readonly Regex VersionPattern = new(@"^\d+\.\d+(\.\d+)?$");
    static readonly Regex Placeholder = new(@"\{([A-Za-z0-9_]+)\}");

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Allowed Settings keys per step type; keys ending in "Step" name an earlier step.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> StepKeys = new Dictionary<string, string[]>
    {
        ["sprite"] = ["width", "height", "seed", "density", "palette"],
        ["items"] = ["names", "size", "seed", "palette"],
        ["character"] = ["spriteStep", "kind", "facing", "origin"],
        ["tileset"] = ["tileSize", "pattern", "baseColor", "variants", "seed", "asset"],
        ["tilemap"] = ["width", "height", "tilesetStep", "layout", "fill", "seed", "difficulty"],
        ["level"] = ["mapStep", "playerStep", "musicStep", "collectibleStep", "checkpoints", "hazards", "collectibles", "seed", "background", "genre"],
        ["song"] = ["seed", "bpm", "scale", "patterns"],
        ["texture"] = ["material", "seed", "size", "dark", "light", "scale", "detail", "normalStrength"],
        ["file"] = [],
    };

    public static TemplateManifest Parse(string json)
    {
        try { return JsonSerializer.Deserialize<TemplateManifest>(json, Json) ?? throw new InvalidDataException("template.json is empty."); }
        catch (JsonException ex) { throw new InvalidDataException($"template.json is not valid: {ex.Message}"); }
    }

    public static string Serialize(TemplateManifest m) => JsonSerializer.Serialize(m, Json);

    public static TemplateInfo Load(string dir, TemplateSource source)
    {
        string file = Path.Combine(dir, "template.json");
        if (!File.Exists(file)) throw new InvalidDataException("No template.json in the folder.");
        if (new FileInfo(file).Length > MaxManifestBytes) throw new InvalidDataException("template.json is larger than 1 MB.");
        var m = Parse(File.ReadAllText(file));
        Validate(m, dir);
        return new TemplateInfo(m, source, dir);
    }

    public static void Validate(TemplateManifest m, string? folder)
    {
        if (m.ApiVersion != ApiVersion) throw new InvalidDataException($"Template API {m.ApiVersion} is not supported; this PixelForge supports API {ApiVersion}.");
        if (m.Id is null || !IdPattern.IsMatch(m.Id)) throw new InvalidDataException("id must be 2–64 lowercase letters, digits, dots, dashes or underscores.");
        if (string.IsNullOrWhiteSpace(m.Name) || m.Name.Length > 80) throw new InvalidDataException("name is required (at most 80 characters).");
        if (m.Version is null || !VersionPattern.IsMatch(m.Version)) throw new InvalidDataException("version must look like 1.0 or 1.2.3.");
        if (m.Steps is null || m.Steps.Count is < 1 or > 200) throw new InvalidDataException("A template needs 1–200 steps.");
        var keys = (m.Parameters ?? []).Select(p => p.Key).ToList();
        if (keys.Count != keys.Distinct().Count()) throw new InvalidDataException("Two parameters have the same key.");
        foreach (var p in m.Parameters ?? [])
            if (p.Options is { Count: 0 }) throw new InvalidDataException($"Parameter “{p.Key}” has an empty option list.");

        var seen = new HashSet<string>();
        foreach (var s in m.Steps)
        {
            if (string.IsNullOrWhiteSpace(s.Id) || !seen.Add(s.Id)) throw new InvalidDataException($"Step id “{s.Id}” is empty or used twice.");
            if (!StepKeys.TryGetValue(s.Type ?? "", out var allowed)) throw new InvalidDataException($"Step “{s.Id}”: unknown type “{s.Type}” (allowed: {string.Join(", ", StepKeys.Keys)}).");
            foreach (var (k, v) in s.Settings ?? [])
            {
                if (!allowed.Contains(k)) throw new InvalidDataException($"Step “{s.Id}”: unknown setting “{k}” (allowed: {string.Join(", ", allowed)}).");
                if (k.EndsWith("Step") && !seen.Contains(v)) throw new InvalidDataException($"Step “{s.Id}”: {k} “{v}” must name an earlier step.");
            }
            foreach (Match ph in (s.Settings ?? []).Values.Concat((s.Properties ?? []).Values).Append(s.Name ?? "").SelectMany(v => Placeholder.Matches(v)))
                if (!keys.Contains(ph.Groups[1].Value)) throw new InvalidDataException($"Step “{s.Id}” uses {{{ph.Groups[1].Value}}}, which is not a parameter.");
            if (s.Type == "file")
            {
                if (folder is null) throw new InvalidDataException("Built-in templates cannot copy files.");
                if (s.File is null || Path.IsPathRooted(s.File) || s.File.Replace('\\', '/').Split('/').Contains("..")) throw new InvalidDataException($"Step “{s.Id}”: file must be a relative path inside the template.");
                if (!File.Exists(Path.Combine(folder, s.File))) throw new InvalidDataException($"Step “{s.Id}”: “{s.File}” is missing from the template.");
            }
        }
        if (!seen.Contains(m.Primary)) throw new InvalidDataException($"primary “{m.Primary}” is not a step.");
    }

    /// <summary>The value with every {param} replaced.</summary>
    public static string Fill(string text, IReadOnlyDictionary<string, string> values) =>
        Placeholder.Replace(text, x => values.TryGetValue(x.Groups[1].Value, out var v) ? v : x.Value);
}

/// <summary>Built-in templates (embedded in Core), the project's (&lt;project&gt;/templates) and the user's (&lt;home&gt;/templates).</summary>
public static class TemplateLibrary
{
    public static string UserFolder => Path.Combine(Settings.Folder, "templates");
    public static string ProjectFolder(Project p) => Path.Combine(p.Root, "templates");

    public static IEnumerable<TemplateInfo> BuiltIn()
    {
        var asm = typeof(TemplateLibrary).Assembly;
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".template.json")).Order())
        {
            using var s = asm.GetManifestResourceStream(name)!;
            var m = TemplateLoader.Parse(new StreamReader(s).ReadToEnd());
            TemplateLoader.Validate(m, null);
            yield return new TemplateInfo(m, TemplateSource.BuiltIn, null);
        }
    }

    /// <summary>Every template that loads, plus the folders that did not and why.</summary>
    public static (List<TemplateInfo> Loaded, List<(string Folder, string Reason)> Rejected) All(Project? p)
    {
        var ok = BuiltIn().ToList();
        var bad = new List<(string, string)>();
        foreach (var (dir, src) in new[] { (p is null ? null : ProjectFolder(p), TemplateSource.Project), (UserFolder, TemplateSource.User) })
        {
            if (dir is null || !Directory.Exists(dir)) continue;
            foreach (var d in Directory.GetDirectories(dir).Order(StringComparer.OrdinalIgnoreCase))
            {
                try { ok.Add(TemplateLoader.Load(d, src)); }
                catch (InvalidDataException ex) { bad.Add((d, ex.Message)); }
            }
        }
        return (ok, bad);
    }

    /// <summary>Folder for a new template version: &lt;scope&gt;/&lt;id&gt;@&lt;version&gt;.</summary>
    public static string FolderFor(string scopeFolder, TemplateManifest m) => Path.Combine(scopeFolder, $"{m.Id}@{m.Version}");

    /// <summary>
    /// Saves assets (and everything they use) as a template that copies them: the files go into the template folder,
    /// and references between them are re-pointed when the template is used. Returns the new template.
    /// </summary>
    public static TemplateInfo SaveFromAssets(Project p, IReadOnlyList<Guid> ids, string scopeFolder, TemplateSource source,
        string id, string name, string version, TemplateKind kind, string? description, List<string> tags)
    {
        if (ids.Count == 0) throw new ArgumentException("Select at least one asset.", nameof(ids));
        // the selection plus what it uses, dependencies first so files are copied before their users
        var order = new List<AssetEntry>();
        void Visit(Guid g)
        {
            if (order.Any(a => a.Id == g) || p.Find(g) is not { } a) return;
            foreach (var dep in p.ReferencesOf(a)) Visit(dep);
            order.Add(a);
        }
        foreach (var g in ids) Visit(g);
        var steps = new List<TemplateStep>();
        var files = new List<(string From, string Name)>();
        foreach (var (a, i) in order.Select((a, i) => (a, i)))
        {
            if (!File.Exists(p.FullPath(a))) throw new InvalidDataException($"“{a.Path}” is missing.");
            string file = $"{i:000}_{System.IO.Path.GetFileName(a.Path)}";
            files.Add((p.FullPath(a), file));
            // every copy is core: the selection needs what it uses even when only a starting point is made
            steps.Add(new TemplateStep($"s{i}", "file", a.Name, Core: true, File: file, OriginalId: a.Id));
        }
        var m = new TemplateManifest(TemplateLoader.ApiVersion, id, name, version, kind, steps[order.FindIndex(a => a.Id == ids[0])].Id, steps,
            tags, description, null);
        string folder = FolderFor(scopeFolder, m);
        if (Directory.Exists(folder)) throw new IOException($"Version {version} of “{id}” already exists; use a higher version.");
        string staging = folder + ".saving";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        foreach (var (from, file) in files) File.Copy(from, System.IO.Path.Combine(staging, file));
        File.WriteAllText(System.IO.Path.Combine(staging, "template.json"), TemplateLoader.Serialize(m));
        TemplateLoader.Validate(m, staging);
        Directory.Move(staging, folder);
        return TemplateLoader.Load(folder, source);
    }

    // ---------- packages (.pxtplpack) ----------

    public const long MaxPackBytes = 64L * 1024 * 1024;
    public const int MaxPackEntries = 2000;

    /// <summary>Zip of template folders (built-in ones as their JSON); entries sorted, fixed time, so packs are reproducible.</summary>
    public static void ExportPack(string path, IEnumerable<TemplateInfo> templates)
    {
        string tmp = path + ".tmp";
        try
        {
            using (var fs = File.Create(tmp))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var entries = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
                foreach (var t in templates)
                {
                    string root = t.Key + "/";
                    if (t.Folder is null) entries[root + "template.json"] = System.Text.Encoding.UTF8.GetBytes(TemplateLoader.Serialize(t.Manifest));
                    else
                        foreach (var f in Directory.EnumerateFiles(t.Folder, "*", SearchOption.AllDirectories))
                            entries[root + Path.GetRelativePath(t.Folder, f).Replace('\\', '/')] = File.ReadAllBytes(f);
                }
                foreach (var (name, data) in entries)
                {
                    var e = zip.CreateEntry(name, CompressionLevel.Optimal);
                    e.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    using var s = e.Open();
                    s.Write(data);
                }
            }
            File.Move(tmp, path, overwrite: true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    /// <summary>
    /// Checks a pack (size, entry count, paths, every template) in a temporary folder, then copies the templates into
    /// the target scope. Nothing is installed when any template is invalid. Returns the installed templates.
    /// </summary>
    public static List<TemplateInfo> ImportPack(string path, string scopeFolder, TemplateSource source)
    {
        if (new FileInfo(path).Length > MaxPackBytes) throw new InvalidDataException("The pack is larger than 64 MB.");
        string staging = Path.Combine(Path.GetTempPath(), "pf-pack-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var zip = ZipFile.OpenRead(path))
            {
                if (zip.Entries.Count > MaxPackEntries) throw new InvalidDataException($"The pack has more than {MaxPackEntries} files.");
                long total = 0;
                foreach (var e in zip.Entries)
                {
                    string name = e.FullName.Replace('\\', '/');
                    if (name.EndsWith('/')) continue;
                    if (name.StartsWith('/') || name.Split('/').Contains("..") || name.Contains(':')) throw new InvalidDataException($"“{e.FullName}” points outside the pack.");
                    total += e.Length;
                    if (total > MaxPackBytes * 4) throw new InvalidDataException("The pack unpacks to more than 256 MB.");
                    string dest = Path.GetFullPath(Path.Combine(staging, name));
                    if (!dest.StartsWith(staging + Path.DirectorySeparatorChar)) throw new InvalidDataException($"“{e.FullName}” points outside the pack.");
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    e.ExtractToFile(dest);
                }
            }
            if (!Directory.Exists(staging)) throw new InvalidDataException("The pack is empty.");
            var loaded = Directory.GetDirectories(staging).Select(d => TemplateLoader.Load(d, source)).ToList();   // throws on the first invalid one
            if (loaded.Count == 0) throw new InvalidDataException("The pack contains no templates.");
            var installed = new List<TemplateInfo>();
            foreach (var t in loaded)
            {
                string dest = FolderFor(scopeFolder, t.Manifest);
                if (Directory.Exists(dest)) Directory.Delete(dest, true);   // same id and version: the pack's copy wins
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                CopyFolder(t.Folder!, dest);
                installed.Add(TemplateLoader.Load(dest, source));
            }
            return installed;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    static void CopyFolder(string from, string to)
    {
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string dest = Path.Combine(to, Path.GetRelativePath(from, f));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(f, dest);
        }
    }
}
