using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PixelForge.Core;

public sealed record PluginPalette(string Name, List<string> Colors)
{
    public List<uint> Argb() => [.. Colors.Select(c => ColorUtil.TryParseHex(c.TrimStart('#'), out uint v) ? v | (c.TrimStart('#').Length == 6 ? 0xFF000000 : 0) : 0)];
}

/// <summary>Saved settings for one of the built-in generators (values as the generator dialogs take them).</summary>
public sealed record PluginGeneratorPreset(string Name, string Generator, Dictionary<string, string> Settings);

public sealed record PluginExportProfile(string Name, ExportTarget Target, string OutputFolder, string ResFolder = "assets", bool Zip = false);

/// <summary>A sequence of existing commands, named by their command palette titles.</summary>
public sealed record PluginMacro(string Name, List<string> Commands);

public sealed record PluginContributions(
    List<PluginPalette>? Palettes = null, List<PluginGeneratorPreset>? GeneratorPresets = null,
    List<PluginExportProfile>? ExportProfiles = null, List<PluginMacro>? Macros = null);

/// <summary>plugin.json. Data only (ADR-0002): PixelForge never loads or runs code from a plugin.</summary>
public sealed record PluginManifest(int ApiVersion, string Id, string Name, string Version, string? Description, string? Author, PluginContributions Contributions);

public sealed record Plugin(PluginManifest Manifest, string Folder);

public sealed record RejectedPlugin(string Folder, string Reason);

public static class PluginLoader
{
    /// <summary>The plugin API this PixelForge understands; manifests asking for another one are refused.</summary>
    public const int ApiVersion = 1;
    public const long MaxManifestBytes = 1024 * 1024;
    public static readonly string[] Generators = ["sprite", "tiles", "layout"];

    static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9._-]{1,63}$");
    static readonly Regex VersionPattern = new(@"^\d+\.\d+(\.\d+)?([-+][0-9A-Za-z.-]+)?$");

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,   // a typo in a key is an error, not a silently ignored setting
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string DefaultFolder => Path.Combine(Settings.Folder, "plugins");

    /// <summary>Reads every plugins/&lt;id&gt;/plugin.json; anything invalid is rejected with a reason and the rest still load.</summary>
    public static (List<Plugin> Loaded, List<RejectedPlugin> Rejected) LoadAll(string folder)
    {
        var ok = new List<Plugin>();
        var bad = new List<RejectedPlugin>();
        if (!Directory.Exists(folder)) return (ok, bad);
        foreach (var dir in Directory.GetDirectories(folder).Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var p = Load(dir);
                if (ok.Any(x => x.Manifest.Id == p.Manifest.Id)) throw new InvalidDataException($"Another plugin already uses the id “{p.Manifest.Id}”.");
                ok.Add(p);
            }
            catch (InvalidDataException ex) { bad.Add(new RejectedPlugin(dir, ex.Message)); }
        }
        return (ok, bad);
    }

    public static Plugin Load(string dir)
    {
        string file = Path.Combine(dir, "plugin.json");
        if (!File.Exists(file)) throw new InvalidDataException("No plugin.json in the folder.");
        if (new FileInfo(file).Length > MaxManifestBytes) throw new InvalidDataException("plugin.json is larger than 1 MB.");
        PluginManifest? m;
        try { m = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(file), Json); }
        catch (JsonException ex) { throw new InvalidDataException($"plugin.json is not valid: {ex.Message}"); }
        catch (IOException ex) { throw new InvalidDataException($"plugin.json cannot be read: {ex.Message}"); }
        if (m is null) throw new InvalidDataException("plugin.json is empty.");
        Validate(m, Path.GetFileName(dir));
        return new Plugin(m, dir);
    }

    public static void Validate(PluginManifest m, string folderName)
    {
        if (m.ApiVersion != ApiVersion)
            throw new InvalidDataException($"Plugin API {m.ApiVersion} is not supported; this PixelForge supports API {ApiVersion}.");
        if (m.Id is null || !IdPattern.IsMatch(m.Id)) throw new InvalidDataException("id must be 2–64 lowercase letters, digits, dots, dashes or underscores.");
        if (!string.Equals(m.Id, folderName, StringComparison.Ordinal)) throw new InvalidDataException($"The folder must be named after the id “{m.Id}”.");
        if (string.IsNullOrWhiteSpace(m.Name) || m.Name.Length > 80) throw new InvalidDataException("name is required (at most 80 characters).");
        if (m.Version is null || !VersionPattern.IsMatch(m.Version)) throw new InvalidDataException("version must look like 1.0 or 1.2.3.");
        var c = m.Contributions ?? throw new InvalidDataException("contributions is required.");

        foreach (var p in c.Palettes ?? [])
        {
            if (string.IsNullOrWhiteSpace(p.Name)) throw new InvalidDataException("Every palette needs a name.");
            if (p.Colors is null || p.Colors.Count is < 1 or > 256) throw new InvalidDataException($"Palette “{p.Name}” needs 1–256 colours.");
            foreach (var col in p.Colors)
                if (col is null || col.TrimStart('#').Length is not (6 or 8) || !ColorUtil.TryParseHex(col.TrimStart('#'), out _))
                    throw new InvalidDataException($"Palette “{p.Name}”: “{col}” is not a colour (#RRGGBB or #AARRGGBB).");
        }
        foreach (var g in c.GeneratorPresets ?? [])
        {
            if (string.IsNullOrWhiteSpace(g.Name)) throw new InvalidDataException("Every generator preset needs a name.");
            if (!Generators.Contains(g.Generator)) throw new InvalidDataException($"Preset “{g.Name}”: generator must be one of {string.Join(", ", Generators)}.");
            ValidatePreset(g);
        }
        foreach (var e in c.ExportProfiles ?? [])
        {
            if (string.IsNullOrWhiteSpace(e.Name)) throw new InvalidDataException("Every export profile needs a name.");
            if (string.IsNullOrWhiteSpace(e.OutputFolder) || Path.IsPathRooted(e.OutputFolder) || e.OutputFolder.Replace('\\', '/').Split('/').Contains(".."))
                throw new InvalidDataException($"Export profile “{e.Name}”: outputFolder must be a relative folder inside the project.");
        }
        foreach (var macro in c.Macros ?? [])
        {
            if (string.IsNullOrWhiteSpace(macro.Name)) throw new InvalidDataException("Every macro needs a name.");
            if (macro.Commands is null || macro.Commands.Count is < 1 or > 50 || macro.Commands.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException($"Macro “{macro.Name}” needs 1–50 command titles.");
        }
    }

    static readonly Dictionary<string, Dictionary<string, Func<string, bool>>> PresetKeys = new()
    {
        ["sprite"] = new() { ["seed"] = _ => true, ["density"] = v => double.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out var d) && d is > 0 and < 1 },
        ["tiles"] = new()
        {
            ["seed"] = _ => true, ["pattern"] = v => Enum.TryParse<TilePattern>(v, false, out _), ["baseColor"] = v => ColorUtil.TryParseHex(v.TrimStart('#'), out _),
            ["variants"] = v => int.TryParse(v, out int n) && n is >= 1 and <= 16,
        },
        ["layout"] = new()
        {
            ["seed"] = _ => true, ["layout"] = v => v is "Cave" or "Platforms",
            ["fill"] = v => double.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out var d) && d is > 0 and < 1,
        },
    };

    static void ValidatePreset(PluginGeneratorPreset g)
    {
        var keys = PresetKeys[g.Generator];
        foreach (var (k, v) in g.Settings ?? [])
        {
            if (!keys.TryGetValue(k, out var check)) throw new InvalidDataException($"Preset “{g.Name}”: unknown setting “{k}” (allowed: {string.Join(", ", keys.Keys)}).");
            if (v is null || !check(v)) throw new InvalidDataException($"Preset “{g.Name}”: “{v}” is not a valid {k}.");
        }
    }

    /// <summary>Copies a plugin folder into the plugins folder after it validated; an installed plugin with the same id is replaced.</summary>
    public static Plugin Install(string sourceDir, string pluginsFolder)
    {
        var p = Load(sourceDir);
        string dest = Path.Combine(pluginsFolder, p.Manifest.Id);
        if (Path.GetFullPath(dest).TrimEnd('\\') == Path.GetFullPath(sourceDir).TrimEnd('\\')) return p;
        string staging = dest + ".installing";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        // ponytail: only plugin.json is copied; data plugins need nothing else today. Copy more file types when contributions reference files.
        File.Copy(Path.Combine(sourceDir, "plugin.json"), Path.Combine(staging, "plugin.json"));
        if (Directory.Exists(dest)) Directory.Delete(dest, true);
        Directory.Move(staging, dest);
        return Load(dest);
    }
}
