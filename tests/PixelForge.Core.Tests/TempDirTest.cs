namespace PixelForge.Core.Tests;

/// <summary>
/// Base for tests that write files: each test gets its own empty folder "pf-{name}-…" in the temp directory,
/// deleted afterwards. <see cref="P"/> builds paths inside it.
/// </summary>
public abstract class TempDirTest : IDisposable
{
    protected readonly string _dir;

    protected TempDirTest(string name)
    {
        _dir = Path.Combine(Path.GetTempPath(), $"pf-{name}-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    protected string P(params string[] parts) => Path.Combine([_dir, .. parts]);
}
