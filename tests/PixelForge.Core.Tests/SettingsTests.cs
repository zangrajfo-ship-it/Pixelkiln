using PixelForge.Core;

namespace PixelForge.Core.Tests;

public class SettingsTests
{
    [Fact]
    public void Round_trip_and_sanitize()
    {
        var s = new Settings { Theme = "Light", UiScale = 1.5, GridColor = 0x80FF0000, UndoLimit = 5, AutosaveMinutes = 0, CheckerSize = 999, TileGridWidth = 8, TileGridOffsetY = 3 };
        var t = Settings.Deserialize(s.Serialize());
        Assert.Equal("Light", t.Theme);
        Assert.Equal(1.5, t.UiScale);
        Assert.Equal(0x80FF0000u, t.GridColor);
        Assert.Equal(10, t.UndoLimit);          // clamped up
        Assert.Equal(64, t.CheckerSize);        // clamped down
        Assert.Equal(0, t.AutosaveMinutes);
        Assert.Equal((8, 16, 0, 3), (t.TileGridWidth, t.TileGridHeight, t.TileGridOffsetX, t.TileGridOffsetY));
        Assert.Equal("Dark", Settings.Deserialize("""{"Theme":"Neon"}""").Theme);
    }

    [Fact]
    public void Recent_files_dedupe_and_cap()
    {
        var s = new Settings();
        for (int i = 0; i < 20; i++) s.AddRecent($@"C:\a\{i}.pxf");
        s.AddRecent(@"c:\A\5.PXF");
        Assert.Equal(Settings.MaxRecent, s.RecentFiles.Count);
        Assert.Equal(@"c:\A\5.PXF", s.RecentFiles[0]);
        Assert.Single(s.RecentFiles, p => p.Equals(@"C:\a\5.pxf", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Missing_or_broken_file_gives_defaults()
    {
        Assert.Equal(100, Settings.Load(Path.Combine(Path.GetTempPath(), $"none-{Guid.NewGuid()}.json")).UndoLimit);
        var bad = Path.Combine(Path.GetTempPath(), $"pf-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(bad, "{ not json");
            Assert.Equal("Dark", Settings.Load(bad).Theme);
        }
        finally { File.Delete(bad); }
    }

    [Fact]
    public void Workspaces_and_recent_commands_survive_and_bad_ones_are_dropped()
    {
        var s = new Settings();
        s.Workspaces.Add(new WorkspaceProfile("Mine", "Scene", "Layout", false, null));
        s.Workspaces.Add(new WorkspaceProfile("", "Scene"));
        s.Workspaces.Add(new WorkspaceProfile("Mine", "Sprite", "Pixel", true, "Pencil"));   // same name: the last one wins
        s.AddRecentCommand("A");
        s.AddRecentCommand("B");
        s.AddRecentCommand("A");
        var t = Settings.Deserialize(s.Serialize()).Sanitized();
        Assert.Equal([new WorkspaceProfile("Mine", "Sprite", "Pixel", true, "Pencil")], t.Workspaces);
        Assert.Equal(["A", "B"], t.RecentCommands);
    }
}
