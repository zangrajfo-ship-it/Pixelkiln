using System.ComponentModel;
using System.Windows.Media;
using PixelForge.Core;

namespace PixelForge.App;

/// <summary>
/// One row of the asset browser. The thumbnail is made on a background thread the first time
/// the row is shown (the list is virtualized, so 5000 assets do not mean 5000 thumbnails).
/// </summary>
public sealed class AssetItem(AssetEntry entry, string fullPath) : INotifyPropertyChanged
{
    // key: path + write time, so an edited file gets a new thumbnail
    static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public AssetEntry Entry { get; } = entry;
    public string FullPath { get; } = fullPath;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title => (Entry.Favorite ? "★ " : "") + Entry.Name;

    public string Info => string.Join(" · ", new[]
    {
        Entry.Type.ToString(),
        Entry.Folder,
        string.Join(", ", Entry.Tags),
        File.Exists(FullPath) ? "" : "missing",
    }.Where(s => s.Length > 0));

    /// <summary>Icon shown when there is no picture (music, sounds, missing files).</summary>
    public string Glyph => _thumb is not null ? "" : Entry.Type switch
    {
        AssetType.Song => "",
        AssetType.Audio => "",
        AssetType.Mix => "",
        AssetType.Sound => "",
        AssetType.Scene => "",
        AssetType.Character => "",
        AssetType.Palette => "",
        AssetType.Tilemap => "",
        _ => "",
    };

    ImageSource? _thumb;
    bool _requested;

    public ImageSource? Thumb
    {
        get
        {
            if (!_requested) { _requested = true; Request(); }
            return _thumb;
        }
    }

    void Request()
    {
        string key = FullPath + "|" + (File.Exists(FullPath) ? File.GetLastWriteTimeUtc(FullPath).Ticks : 0);
        if (Cache.TryGetValue(key, out var cached)) { _thumb = cached; return; }
        var type = Entry.Type;
        var project = App.Project;
        Task.Run(() => Make(type, FullPath, project)).ContinueWith(t =>
        {
            _thumb = t.Result;
            Cache[key] = _thumb;
            PropertyChanged?.Invoke(this, new(nameof(Thumb)));
            PropertyChanged?.Invoke(this, new(nameof(Glyph)));
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Thumbnail picture, or null for types without one (or files that cannot be read).</summary>
    public static ImageSource? Make(AssetType type, string path, Project? project, int max = 64)
    {
        try
        {
            (uint[] px, int w, int h)? img = type switch
            {
                AssetType.Sprite or AssetType.Image => AssetLoader.LoadImage(path),
                AssetType.Palette => AssetInfo.PaletteImage(PaletteIO.Load(path)),
                AssetType.Tilemap => MapImage(path, project),
                _ => null,
            };
            if (img is not var (p, iw, ih)) return null;
            var (tp, tw, th) = AssetInfo.Thumbnail(p, iw, ih, max);
            var bmp = ImageIO.ToBitmap(tp, tw, th);
            bmp.Freeze();   // made on a worker thread, shown on the UI thread
            return bmp;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException or FormatException or ArgumentException)
        {
            return null;   // the browser shows the type icon; Check project reports the problem
        }
    }

    static (uint[], int, int)? MapImage(string path, Project? project)
    {
        var m = Tilemap.Load(path);
        if (project is not null) AssetLoader.RefreshTileset(m, project);
        int w = m.Width * m.TileSize, h = m.Height * m.TileSize;
        if (m.Tileset is null || (long)w * h > 4_000_000) return null;   // ponytail: no thumbnail for huge maps; render a crop if needed
        return (m.Render(), w, h);
    }
}
