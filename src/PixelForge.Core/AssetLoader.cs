namespace PixelForge.Core;

/// <summary>Reads any asset file by type: health checks, thumbnails and tilesets share this.</summary>
public static class AssetLoader
{
    /// <summary>Loads the file to prove it is readable; throws a readable exception if not.</summary>
    public static void Probe(AssetType type, string path) => AssetKinds.Of(type).Probe(path);

    /// <summary>Asset types that can stand in for a picture (tilesets, scene image layers, exports).</summary>
    public static bool IsPicture(AssetType t) => t is AssetType.Image or AssetType.Sprite or AssetType.Paint or AssetType.Vector or AssetType.Texture;

    /// <summary>
    /// A picture of an image, sprite (first frame, all visible layers), painting (all visible layers), vector drawing
    /// (art board at 1 pixel per unit) or procedural texture (its colour map). Textures made from another asset need
    /// their project and throw here (use Textures.Build).
    /// </summary>
    public static (uint[] Pixels, int Width, int Height) LoadImage(string path)
    {
        switch (System.IO.Path.GetExtension(path).ToLowerInvariant())
        {
            case ".pxf":
                var s = ProjectFile.Load(path);
                return (Compositor.Flatten(s, 0), s.Width, s.Height);
            case ".pxpaint":
                var d = PaintDoc.Load(path);
                return (d.Flatten(), d.Width, d.Height);
            case ".pxvec":
                var v = VectorDoc.Load(path);
                int w = Math.Max(1, (int)Math.Ceiling(v.Width)), h = Math.Max(1, (int)Math.Ceiling(v.Height));
                if ((long)w * h > 64L * 1024 * 1024) throw new InvalidDataException("The drawing is too large to use as a picture.");
                return (VectorGeometry.Rasterize(v, w, h), w, h);
            case ".pxtex":
                var t = TextureDoc.Load(path);
                if (t.Source != TextureSource.Procedural) throw new InvalidDataException("A painted texture is read through its project (Textures.Build).");
                var (albedo, _) = Textures.Generate(t);
                return (albedo, t.Size, t.Size);
            default:
                return ImageIO.LoadImage(path);
        }
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
