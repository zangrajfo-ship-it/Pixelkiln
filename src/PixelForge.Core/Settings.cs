using System.Text.Json;

namespace PixelForge.Core;

/// <summary>A workspace: which tab and mode to show, whether the asset browser is open, and the tool to start with.</summary>
public sealed record WorkspaceProfile(string Name, string Tab, string? Mode = null, bool AssetBrowser = true, string? Tool = null);

/// <summary>User preferences, stored as JSON in %AppData%\PixelForge\settings.json.</summary>
public sealed class Settings
{
    public const int MaxRecent = 12;

    /// <summary>Modern, Dark, Light or System.</summary>
    public string Theme { get; set; } = "Modern";
    /// <summary>1 = 100 %.</summary>
    public double UiScale { get; set; } = 1;
    /// <summary>ARGB of the pixel grid lines.</summary>
    public uint GridColor { get; set; } = 0x99000000;
    public uint CheckerLight { get; set; } = 0xFFCCCCCC;
    public uint CheckerDark { get; set; } = 0xFF999999;
    /// <summary>Checkerboard square size in screen pixels.</summary>
    public int CheckerSize { get; set; } = 8;
    /// <summary>Second grid on sprites (e.g. 16×16 tiles), independent of the pixel grid.</summary>
    public int TileGridWidth { get; set; } = 16;
    public int TileGridHeight { get; set; } = 16;
    public int TileGridOffsetX { get; set; }
    public int TileGridOffsetY { get; set; }
    public uint TileGridColor { get; set; } = 0xCC00B4FF;
    public int DefaultSpriteWidth { get; set; } = 32;
    public int DefaultSpriteHeight { get; set; } = 32;
    public int UndoLimit { get; set; } = 100;
    /// <summary>0 = off.</summary>
    public int AutosaveMinutes { get; set; } = 2;
    public List<string> RecentFiles { get; set; } = [];
    /// <summary>Command palette titles, most recent first (shown on top when the palette opens).</summary>
    public List<string> RecentCommands { get; set; } = [];
    /// <summary>The user's own workspace profiles (the built-in ones are not stored).</summary>
    public List<WorkspaceProfile> Workspaces { get; set; } = [];
    /// <summary>Project reopened at start-up (null = none).</summary>
    public string? LastProject { get; set; }

    /// <summary>%AppData%\PixelForge, or PIXELFORGE_HOME when set (tests and portable use keep real settings untouched).</summary>
    public static string Folder => Environment.GetEnvironmentVariable("PIXELFORGE_HOME") is { Length: > 0 } home
        ? home
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PixelForge");
    public static string DefaultPath => Path.Combine(Folder, "settings.json");

    public void AddRecentCommand(string title)
    {
        RecentCommands.Remove(title);
        RecentCommands.Insert(0, title);
        if (RecentCommands.Count > MaxRecent) RecentCommands.RemoveRange(MaxRecent, RecentCommands.Count - MaxRecent);
    }

    /// <summary>Most recent first, no duplicates (case-insensitive), capped.</summary>
    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecent) RecentFiles.RemoveRange(MaxRecent, RecentFiles.Count - MaxRecent);
    }

    /// <summary>Clamps every value into a usable range (hand-edited or old files).</summary>
    public Settings Sanitized() => new()
    {
        Theme = Theme is "Modern" or "Dark" or "Light" or "System" ? Theme : "Modern",
        UiScale = Math.Clamp(UiScale, 0.75, 2),
        GridColor = GridColor,
        CheckerLight = CheckerLight | 0xFF000000,
        CheckerDark = CheckerDark | 0xFF000000,
        CheckerSize = Math.Clamp(CheckerSize, 2, 64),
        TileGridWidth = Math.Clamp(TileGridWidth, 1, 1024),
        TileGridHeight = Math.Clamp(TileGridHeight, 1, 1024),
        TileGridOffsetX = Math.Clamp(TileGridOffsetX, 0, 1023),
        TileGridOffsetY = Math.Clamp(TileGridOffsetY, 0, 1023),
        TileGridColor = TileGridColor,
        DefaultSpriteWidth = Math.Clamp(DefaultSpriteWidth, 1, 4096),
        DefaultSpriteHeight = Math.Clamp(DefaultSpriteHeight, 1, 4096),
        UndoLimit = Math.Clamp(UndoLimit, 10, 1000),
        AutosaveMinutes = Math.Clamp(AutosaveMinutes, 0, 60),
        LastProject = LastProject,
        RecentFiles = [.. (RecentFiles ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxRecent)],
        RecentCommands = [.. (RecentCommands ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().Take(MaxRecent)],
        Workspaces = [.. (Workspaces ?? []).Where(w => !string.IsNullOrWhiteSpace(w.Name) && !string.IsNullOrWhiteSpace(w.Tab)).GroupBy(w => w.Name).Select(g => g.Last()).Take(20)],
    };

    static readonly JsonSerializerOptions Json = DocumentFile.Indented;

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static Settings Deserialize(string json) =>
        (JsonSerializer.Deserialize<Settings>(json) ?? new Settings()).Sanitized();

    /// <summary>Missing or broken file = defaults (never blocks start-up).</summary>
    public static Settings Load(string? path = null)
    {
        try { return Deserialize(File.ReadAllText(path ?? DefaultPath)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new Settings(); }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        DocumentFile.WriteAtomic(path, Serialize());
    }
}
