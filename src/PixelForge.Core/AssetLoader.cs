namespace PixelForge.Core;

/// <summary>Reads any asset file by type: health checks, thumbnails and tilesets share this.</summary>
public static class AssetLoader
{
    /// <summary>Loads the file to prove it is readable; throws a readable exception if not.</summary>
    public static void Probe(AssetType type, string path)
    {
        switch (type)
        {
            case AssetType.Sprite: ProjectFile.Load(path); break;
            case AssetType.Image: ImageIO.LoadImage(path); break;
            case AssetType.Tilemap: Tilemap.Load(path); break;
            case AssetType.Song: Song.Load(path); break;
            case AssetType.Sound: Sfx.Load(path); break;
            case AssetType.Palette: PaletteIO.Load(path); break;
        }
    }

    /// <summary>A picture of an image or sprite file (sprite: first frame, all visible layers).</summary>
    public static (uint[] Pixels, int Width, int Height) LoadImage(string path)
    {
        if (!path.EndsWith(".pxf", StringComparison.OrdinalIgnoreCase)) return ImageIO.LoadImage(path);
        var s = ProjectFile.Load(path);
        return (Compositor.Flatten(s, 0), s.Width, s.Height);
    }

    /// <summary>
    /// Refreshes a map's tileset from its project asset (ADR-0001). Returns false and keeps the embedded copy
    /// when the map has no asset id or the asset is missing, so maps still open on their own.
    /// </summary>
    public static bool RefreshTileset(Tilemap map, Project project)
    {
        if (map.TilesetAssetId is not Guid id || project.Resolve(id) is not string path) return false;
        var (px, w, h) = LoadImage(path);
        if (w < map.TileSize || h < map.TileSize) return false;
        map.Tileset = new Tileset(px, w, h, map.TileSize);
        return true;
    }
}
