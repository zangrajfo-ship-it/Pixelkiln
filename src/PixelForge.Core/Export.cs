using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PixelForge.Core;

public enum ExportTarget { Generic, Godot4 }

/// <summary>Saved export settings (in the project): where to, for which engine, which assets.</summary>
public sealed class ExportProfile
{
    public string Name { get; set; } = "Godot 4";
    public ExportTarget Target { get; set; } = ExportTarget.Godot4;
    /// <summary>Relative to the project folder; the whole folder is replaced on export.</summary>
    public string OutputFolder { get; set; } = "export/godot";
    /// <summary>Godot: folder inside the Godot project the files are copied to (res://ResFolder/…).</summary>
    public string ResFolder { get; set; } = "assets";
    /// <summary>Also write OutputFolder.zip.</summary>
    public bool Zip { get; set; }
    /// <summary>Assets to export (plus everything they use); null = all.</summary>
    public List<Guid>? Assets { get; set; }

    public ExportProfile Clone() => new() { Name = Name, Target = Target, OutputFolder = OutputFolder, ResFolder = ResFolder, Zip = Zip, Assets = Assets?.ToList() };
}

public sealed class ExportResult
{
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
    /// <summary>Relative path (forward slashes) → content, sorted so output and report are deterministic.</summary>
    public SortedDictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
    public string Report { get; set; } = "";
    public bool Ok => Errors.Count == 0;
}

/// <summary>
/// Export pipeline: validate, build every file in memory (same input → same bytes), then replace the output folder
/// in one step (staging folder + rename), so a failed export never leaves half-written files.
/// </summary>
public static class Exporter
{
    public static ExportResult Plan(Project p, ExportProfile prof)
    {
        var r = new ExportResult();
        if (prof.Name.Trim().Length == 0) r.Errors.Add("The profile needs a name.");
        string outFull = Path.GetFullPath(Path.Combine(p.Root, prof.OutputFolder));
        if (prof.OutputFolder.Trim().Length == 0 || !outFull.StartsWith(p.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            r.Errors.Add("The output folder must be inside the project folder.");
        else if (p.Assets.Any(a => p.FullPath(a).StartsWith(outFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            r.Errors.Add("The output folder contains project assets; exporting would replace them. Choose another folder.");

        var set = Closure(p, prof.Assets ?? p.Assets.Select(a => a.Id), r);
        foreach (var a in set.Where(a => !File.Exists(p.FullPath(a)))) r.Errors.Add($"“{a.Path}” is missing.");
        if (!r.Ok) return Finish(r, prof);

        var ctx = new Context(p, prof, r, set);
        foreach (var a in set.OrderBy(a => a.Path, StringComparer.Ordinal))
        {
            try { ctx.Export(a); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException or ArgumentException)
            {
                r.Errors.Add($"“{a.Path}”: {ex.Message}");
            }
        }
        ctx.WriteProjectJson();
        return Finish(r, prof);
    }

    /// <summary>Plans, and when there are no errors writes the files (and the zip).</summary>
    public static ExportResult Run(Project p, ExportProfile prof)
    {
        var r = Plan(p, prof);
        if (!r.Ok) return r;
        string outDir = Path.GetFullPath(Path.Combine(p.Root, prof.OutputFolder));
        string staging = outDir + ".staging", old = outDir + ".old";
        foreach (var d in new[] { staging, old }) if (Directory.Exists(d)) Directory.Delete(d, true);
        try
        {
            foreach (var (rel, data) in r.Files)
            {
                string f = Path.Combine(staging, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(f)!);
                File.WriteAllBytes(f, data);
            }
            Directory.CreateDirectory(staging);
            if (Directory.Exists(outDir)) Directory.Move(outDir, old);
            Directory.Move(staging, outDir);
            if (Directory.Exists(old)) Directory.Delete(old, true);
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            if (!Directory.Exists(outDir) && Directory.Exists(old)) Directory.Move(old, outDir);   // put the previous export back
            throw;
        }
        if (prof.Zip) WriteZip(outDir + ".zip", r.Files);
        return r;
    }

    /// <summary>Zip with sorted entries and a fixed timestamp, so the same export gives the same archive.</summary>
    static void WriteZip(string path, SortedDictionary<string, byte[]> files)
    {
        string tmp = path + ".tmp";
        try
        {
            using (var fs = File.Create(tmp))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                foreach (var (rel, data) in files)
                {
                    var e = zip.CreateEntry(rel, CompressionLevel.Optimal);
                    e.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    using var s = e.Open();
                    s.Write(data);
                }
            File.Move(tmp, path, overwrite: true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    static ExportResult Finish(ExportResult r, ExportProfile prof)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PixelForge export: {prof.Name} ({prof.Target})");
        sb.AppendLine(r.Ok ? $"{r.Files.Count} files" : "Not exported: fix the errors below.");
        foreach (var e in r.Errors) sb.AppendLine("error: " + e);
        foreach (var w in r.Warnings) sb.AppendLine("warning: " + w);
        sb.AppendLine();
        foreach (var (rel, data) in r.Files)
            sb.AppendLine($"{rel}  {data.Length} B  {Convert.ToHexString(SHA256.HashData(data))[..12].ToLowerInvariant()}");
        r.Report = sb.ToString();
        if (r.Ok) r.Files["export-report.txt"] = Encoding.UTF8.GetBytes(r.Report);
        return r;
    }

    /// <summary>The chosen assets plus everything they use, recursively.</summary>
    static List<AssetEntry> Closure(Project p, IEnumerable<Guid> ids, ExportResult r)
    {
        var seen = new HashSet<Guid>();
        var queue = new Queue<Guid>(ids);
        var list = new List<AssetEntry>();
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!seen.Add(id)) continue;
            if (p.Find(id) is not { } a) { r.Errors.Add($"Unknown asset {id} is referenced."); continue; }
            list.Add(a);
            try { foreach (var dep in p.ReferencesOf(a)) queue.Enqueue(dep); }
            catch (Exception ex) when (ex is InvalidDataException or IOException) { r.Errors.Add($"“{a.Path}” cannot be read: {ex.Message}"); }
        }
        return list;
    }

    // ---------- per-asset export ----------

    sealed class Context(Project p, ExportProfile prof, ExportResult r, List<AssetEntry> set)
    {
        readonly SceneAssets _assets = new(p);
        readonly AudioLibrary _audio = new(p);
        readonly Dictionary<Guid, string> _base = BaseNames(set);
        bool Godot => prof.Target == ExportTarget.Godot4;

        /// <summary>Output path without extension: the asset's folder + a safe name, unique across the export.</summary>
        static Dictionary<Guid, string> BaseNames(List<AssetEntry> set)
        {
            var names = new Dictionary<Guid, string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in set.OrderBy(a => a.Path, StringComparer.Ordinal))
            {
                string folder = string.Join("/", a.Folder.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Select(Safe));
                string stem = (folder.Length > 0 ? folder + "/" : "") + Safe(a.Name);
                // assets only clash when their output files can (hero.pxf and hero.pxchar can share "hero")
                string group = a.Type switch
                {
                    AssetType.Sprite or AssetType.Image => "picture", AssetType.Tilemap or AssetType.Scene => "scene", AssetType.Character => "character",
                    AssetType.Palette => "palette", _ => "audio",
                };
                string name = stem;
                for (int k = 2; !used.Add(group + "|" + name); k++) name = $"{stem}_{k}";
                names[a.Id] = name;
            }
            return names;
        }

        static string Safe(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s) sb.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_');
            return sb.Length == 0 ? "_" : sb.ToString();
        }

        string Res(string rel) => $"res://{(prof.ResFolder.Trim('/').Length > 0 ? prof.ResFolder.Trim('/') + "/" : "")}{rel}";

        void Add(string rel, byte[] data) => r.Files[rel] = data;
        void Add(string rel, string text) => r.Files[rel] = Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n"));

        public void Export(AssetEntry a)
        {
            string path = p.FullPath(a), b = _base[a.Id];
            switch (a.Type)
            {
                case AssetType.Sprite:
                {
                    var s = ProjectFile.Load(path);
                    var sheet = SheetOf(s);
                    Add(b + ".png", ImageIO.EncodePng(sheet.Pixels, sheet.Width, sheet.Height));
                    if (Godot) Add(b + ".tres", GodotExport.SpriteFrames(s, sheet.Columns, Res(b + ".png")));
                    else Add(b + ".json", SpriteSheet.Json(s, sheet, FileName(b + ".png"), a.Name));
                    break;
                }
                case AssetType.Image:
                {
                    var (px, w, h) = AssetLoader.LoadImage(path);
                    Add(b + ".png", ImageIO.EncodePng(px, w, h));
                    break;
                }
                case AssetType.Tilemap:
                {
                    var m = Tilemap.Load(path);
                    AssetLoader.RefreshTileset(m, p);
                    if (m.Tileset is not { } ts) throw new InvalidDataException("The map has no tileset.");
                    Add(b + "_tiles.png", ImageIO.EncodePng(ts.Pixels, ts.Width, ts.Height));
                    if (Godot)
                    {
                        Add(b + "_tileset.tres", GodotExport.TileSet(m, Res(b + "_tiles.png"), p.Settings));
                        Add(b + ".tscn", GodotExport.TileMapScene(m, a.Name, Res(b + "_tileset.tres")));
                    }
                    else
                    {
                        Add(b + ".tmj", TiledExport.Json(m, FileName(b + "_tiles.png")));
                        if (m.Terrains.Count > 0) Add(b + "_terrain.json", TerrainExport.Json(m));
                    }
                    break;
                }
                case AssetType.Character:
                {
                    var c = Character.Load(path);
                    if (c.SpriteAssetId is not Guid sid || p.Find(sid) is not { Type: AssetType.Sprite }) throw new InvalidDataException("The character has no sprite.");
                    var s = ProjectFile.Load(p.Resolve(sid)!);
                    if (Godot) Add(b + "_character.tscn", GodotExport.CharacterScene(c, s, a.Name, Res(_base[sid] + ".tres"), p.Settings));
                    else Add(b + "_character.json", CharacterExport.Json(c, s, a.Name, Rel(b, _base[sid] + ".png")));
                    break;
                }
                case AssetType.Song or AssetType.Sound or AssetType.Audio or AssetType.Mix:
                {
                    Add(b + ".wav", WavWriter.ToBytes(_audio.Samples(a.Id)));
                    if (a.Type == AssetType.Mix && Mix.Load(path) is { LoopStartMs: int ls, LoopEndMs: int le })
                        r.Warnings.Add(Godot
                            ? $"{b}.wav: set the loop in Godot's import dock (Loop Mode Forward, begin {AudioEdit.Samples(ls)}, end {AudioEdit.Samples(le)} samples)."
                            : $"{b}.wav loops from {ls} to {le} ms (listed in project.json).");
                    break;
                }
                case AssetType.Scene:
                {
                    var sc = Scene.Load(path);
                    foreach (var issue in SceneCheck.Run(sc, p))
                        (issue.Severity == "error" ? r.Errors : r.Warnings).Add($"{a.Path}: {issue.Message}");
                    if (Godot) Add(b + ".tscn", GodotExport.LevelScene(sc, a.Name, p, _assets, id => Res(_base[id]), r.Warnings));
                    else Add(b + "_scene.json", SceneJson(sc, b));
                    break;
                }
                case AssetType.Palette:
                    Add(b + Path.GetExtension(a.Path).ToLowerInvariant(), File.ReadAllBytes(path));
                    break;
            }
        }

        SpriteSheet.Sheet SheetOf(Sprite s) => SpriteSheet.Build(s, (int)Math.Ceiling(Math.Sqrt(s.Frames.Count)));

        static string FileName(string rel) => rel[(rel.LastIndexOf('/') + 1)..];

        /// <summary>Path of target relative to the folder of from (both export-relative, forward slashes).</summary>
        static string Rel(string from, string target)
        {
            var a = from.Split('/')[..^1];
            var t = target.Split('/');
            int common = 0;
            while (common < a.Length && common < t.Length - 1 && a[common] == t[common]) common++;
            return string.Join("/", Enumerable.Repeat("..", a.Length - common).Concat(t[common..]));
        }

        /// <summary>Generic scene: the .pxscene structure with asset ids replaced by exported file paths.</summary>
        string SceneJson(Scene s, string b)
        {
            string? File(Guid? id) => id is Guid g && p.Find(g) is { } e ? Rel(b, _base[g] + e.Type switch
            {
                AssetType.Sprite => ".json", AssetType.Image => ".png", AssetType.Tilemap => ".tmj", AssetType.Character => "_character.json", _ => ".wav",
            }) : null;
            return JsonSerializer.Serialize(new
            {
                format = "pixelforge-scene", version = 1, width = s.Width, height = s.Height, background = $"#{s.Background:X8}",
                level = s.Level, order = s.Order, camera = new { x = s.CameraX, y = s.CameraY }, music = File(s.MusicAssetId),
                layers = s.Layers.Select(l => new
                {
                    name = l.Name, kind = l.Kind.ToString(), file = File(l.AssetId), parallax = new { x = l.ParallaxX, y = l.ParallaxY },
                    offset = new { x = l.OffsetX, y = l.OffsetY }, repeatX = l.RepeatX, visible = l.Visible,
                    instances = s.InstancesOf(l.Id).Select(i => new
                    {
                        name = i.Name, file = File(i.AssetId), x = i.X, y = i.Y, animation = i.Animation, flipX = i.FlipX, z = i.Z, properties = i.Properties,
                        track = i.Track is null ? null : new { loop = i.Track.Loop, keys = i.Track.Keys.Select(k => new { time = k.TimeMs, x = k.X, y = k.Y, scaleX = k.ScaleX, scaleY = k.ScaleY, rotation = k.Rotation, opacity = k.Opacity, easing = k.Easing.ToString() }) },
                    }),
                }),
                areas = s.Areas.Select(a => new { name = a.Name, kind = a.Kind.ToString(), x = a.X, y = a.Y, w = a.W, h = a.H, properties = a.Properties }),
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>project.json: game settings, collision categories, playlists and music loops, with file paths.</summary>
        public void WriteProjectJson()
        {
            if (!r.Ok) return;
            string? Wav(Guid id) => _base.TryGetValue(id, out var b) ? b + ".wav" : null;
            var settings = p.Settings;
            Add("project.json", JsonSerializer.Serialize(new
            {
                format = "pixelforge-project", version = 1, name = p.Name,
                resolution = new { width = settings.TargetWidth, height = settings.TargetHeight }, tileSize = settings.TileSize,
                collision = Enumerable.Range(0, 32).Where(i => settings.CollisionCategories.ContainsKey(i) || settings.CollisionMasks.ContainsKey(i))
                    .Select(i => new { bit = i, name = settings.CollisionCategories.GetValueOrDefault(i, ""), mask = settings.CollisionMasks.GetValueOrDefault(i, uint.MaxValue) }),
                playlists = settings.Playlists.Select(pl => new { name = pl.Name, loop = pl.Loop, shuffle = pl.Shuffle, items = pl.Items.Select(Wav).Where(x => x is not null) }),
                musicLoops = set.Where(a => a.Type == AssetType.Mix).Select(a => (a, m: Mix.Load(p.FullPath(a)))).Where(x => x.m.LoopStartMs is not null)
                    .Select(x => new { file = Wav(x.a.Id), startMs = x.m.LoopStartMs, endMs = x.m.LoopEndMs }),
                exported = set.OrderBy(a => a.Path, StringComparer.Ordinal).Select(a => new { asset = a.Path, type = a.Type.ToString(), name = _base[a.Id] }),
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
