using System.Text;

namespace PixelForge.Core.Tests;

/// <summary>Golden fixture files and the text of exported files.</summary>
static class TestFiles
{
    /// <summary>A file from Fixtures/, copied next to the test assembly.</summary>
    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>The exported file at exactly this relative path, as UTF-8 text.</summary>
    public static string Text(this ExportResult r, string path) => Encoding.UTF8.GetString(r.Files[path]);

    /// <summary>The one exported file whose path ends with <paramref name="suffix"/>, as UTF-8 text.</summary>
    public static string TextEnding(this ExportResult r, string suffix) => Encoding.UTF8.GetString(r.Files.Single(f => f.Key.EndsWith(suffix)).Value);
}
