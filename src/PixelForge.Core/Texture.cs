using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelForge.Core;

public enum Material { Ground, Stone, Grass, Wood, Water, Sand, Brick, Metal }
public enum TextureSource { Procedural, Painting, Picture }

/// <summary>
/// A seamless texture (.pxtex): where the colours come from (a seeded material recipe, a painting or any picture of
/// the project) and how the extra maps (height, normal, ambient occlusion) are made from it.
/// </summary>
public sealed class TextureDoc
{
    public const int Version = 1;
    public TextureSource Source { get; set; } = TextureSource.Procedural;
    public Material Material { get; set; } = Material.Stone;
    public string Seed { get; set; } = "stone";
    public int Size { get; set; } = 256;
    public uint Dark { get; set; } = 0xFF3A3A44;
    public uint Light { get; set; } = 0xFFA8A8B4;
    /// <summary>Features per tile (cells, bricks, rings …).</summary>
    public double Scale { get; set; } = 6;
    /// <summary>Noise octaves 1–6.</summary>
    public int Detail { get; set; } = 4;
    /// <summary>Painting or picture asset for those sources.</summary>
    public Guid? AssetId { get; set; }
    /// <summary>Blend the edges of a painting or picture so it repeats (pixels; 0 = use as is).</summary>
    public int SeamBlend { get; set; }
    public double NormalStrength { get; set; } = 2;
    /// <summary>DirectX-style normal maps have green pointing down (Godot and Unity default: up).</summary>
    public bool FlipGreen { get; set; }
    public double OcclusionStrength { get; set; } = 1;

    public TextureDoc Clone() => (TextureDoc)MemberwiseClone();

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    sealed record Dto(int Version, TextureDoc Texture);

    public static string Serialize(TextureDoc t) => JsonSerializer.Serialize(new Dto(Version, t), Json);

    public static TextureDoc Deserialize(string json)
    {
        var t = (JsonSerializer.Deserialize<Dto>(json, Json) ?? throw new InvalidDataException("Empty texture file.")).Texture ?? throw new InvalidDataException("Empty texture file.");
        if (t.Size is < 4 or > 2048) throw new InvalidDataException("Texture size must be 4–2048 px.");
        if (t.Source != TextureSource.Procedural && t.AssetId is null) throw new InvalidDataException("A painted or picture texture needs its asset.");
        (t.Detail, t.Scale, t.SeamBlend) = (Math.Clamp(t.Detail, 1, 6), Math.Clamp(t.Scale, 1, 64), Math.Clamp(t.SeamBlend, 0, 512));
        t.Seed ??= "";
        return t;
    }

    public static void Save(TextureDoc t, string path) => DocumentFile.Save(path, Serialize(t), Version);
    public static TextureDoc Load(string path) => DocumentFile.Load(path, "texture", Version, Deserialize);
}

/// <summary>Colour, height, normal and occlusion maps of one texture, all the same size and all tiling.</summary>
public sealed record TextureMaps(int Width, int Height, uint[] Albedo, float[] HeightMap, uint[] Normal, uint[] Occlusion);

public static class Textures
{
    /// <summary>Builds every map. Painting and picture sources need the project to find their asset.</summary>
    public static TextureMaps Build(TextureDoc t, Project? p = null)
    {
        uint[] albedo;
        float[] height;
        int w, h;
        if (t.Source == TextureSource.Procedural)
        {
            (w, h) = (t.Size, t.Size);
            (albedo, height) = Generate(t);
        }
        else
        {
            if (p is null || t.AssetId is not Guid id || p.Resolve(id) is not string path) throw new InvalidDataException("The texture's painting or picture is missing from the project.");
            (albedo, w, h) = AssetLoader.LoadImage(path);
            if (t.SeamBlend > 0) albedo = MakeSeamless(albedo, w, h, Math.Min(t.SeamBlend, Math.Min(w, h) / 2));
            height = [.. albedo.Select(c => (float)(Luma(c) * ((c >> 24) / 255.0)))];
        }
        return new TextureMaps(w, h, albedo, height, NormalMap(height, w, h, t.NormalStrength, t.FlipGreen), OcclusionMap(height, w, h, t.OcclusionStrength));
    }

    static double Luma(uint c) => (0.299 * (c >> 16 & 0xFF) + 0.587 * (c >> 8 & 0xFF) + 0.114 * (c & 0xFF)) / 255;

    // ---------- seamless noise ----------

    /// <summary>Value noise on a lattice that wraps every px cells across and py cells down, so the result tiles without seams.</summary>
    sealed class Noise(ulong seed, int px, int py)
    {
        readonly int _px = Math.Max(1, px), _py = Math.Max(1, py);
        readonly float[] _v = Enumerable.Range(0, Math.Max(1, px) * Math.Max(1, py)).Select(i => (float)new Rng(seed + (ulong)i * 0x9E3779B97F4A7C15).NextDouble()).ToArray();

        float V(int x, int y) => _v[Tools.Wrap(y, _py) * _px + Tools.Wrap(x, _px)];

        public double At(double x, double y)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            double fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            return (V(x0, y0) * (1 - fx) + V(x0 + 1, y0) * fx) * (1 - fy) + (V(x0, y0 + 1) * (1 - fx) + V(x0 + 1, y0 + 1) * fx) * fy;
        }
    }

    /// <summary>
    /// Fractal noise 0–1 at texture coordinates u, v in 0–1. Whole numbers of cells per axis (more across than down
    /// gives streaks), and every octave doubles them, so every octave tiles.
    /// </summary>
    sealed class Fbm(ulong seed, int cellsX, int cellsY, int octaves)
    {
        readonly Noise[] _n = [.. Enumerable.Range(0, octaves).Select(o => new Noise(seed + (ulong)o * 7919, cellsX << o, cellsY << o))];

        public Fbm(ulong seed, int cells, int octaves) : this(seed, cells, cells, octaves) { }

        public double At(double u, double v)
        {
            double sum = 0, amp = 1, norm = 0;
            for (int o = 0; o < _n.Length; o++)
            {
                sum += _n[o].At(u * (cellsX << o), v * (cellsY << o)) * amp;
                norm += amp;
                amp *= 0.5;
            }
            return sum / norm;
        }
    }

    /// <summary>Distance to the nearest and second-nearest feature point (tiling cellular noise), in cell units.</summary>
    static (double F1, double F2) Cells(double u, double v, int cells, ulong seed)
    {
        double x = u * cells, y = v * cells, f1 = 9, f2 = 9;
        int cx = (int)Math.Floor(x), cy = (int)Math.Floor(y);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int gx = cx + dx, gy = cy + dy, wx = Tools.Wrap(gx, cells), wy = Tools.Wrap(gy, cells);
                var r = new Rng(seed ^ (ulong)(wy * 73856093 ^ wx * 19349663));
                double px = gx + 0.15 + 0.7 * r.NextDouble(), py = gy + 0.15 + 0.7 * r.NextDouble();
                double d = Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                if (d < f1) (f1, f2) = (d, f1); else if (d < f2) f2 = d;
            }
        return (f1, f2);
    }

    /// <summary>Colour and height of a material. Same document, same pixels.</summary>
    public static (uint[] Albedo, float[] Height) Generate(TextureDoc t)
    {
        int n = t.Size, cells = Math.Max(1, (int)Math.Round(t.Scale));
        ulong seed = Rng.Seed(t.Seed);
        var fbm = new Fbm(seed, Math.Max(2, cells), t.Detail);
        var fine = new Fbm(seed ^ 0xF1E, Math.Max(4, cells * 4), Math.Min(3, t.Detail));
        var albedo = new uint[n * n];
        var height = new float[n * n];
        var speck = new Rng(seed ^ 0x5EC);
        // streaky noise for grain, blades and brushing (different cell counts across and down, all whole numbers)
        var woodWarp = new Fbm(seed ^ 0x77, 2, 4, 2);
        var woodGrain = new Fbm(seed ^ 0x78, Math.Max(8, cells * 8), 2, 2);
        var blades = new Fbm(seed ^ 0x79, Math.Max(12, cells * 12), Math.Max(2, cells * 2), 2);
        var brush = new Fbm(seed ^ 0x7A, 2, Math.Max(16, cells * 16), 2);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                double u = (x + 0.5) / n, v = (y + 0.5) / n, f = fbm.At(u, v), shade, h;
                switch (t.Material)
                {
                    case Material.Stone:
                    {
                        var (f1, f2) = Cells(u, v, cells, seed);
                        double edge = Math.Clamp((f2 - f1) * 4, 0, 1);       // dark mortar between stones
                        shade = (0.35 + 0.65 * f) * (0.25 + 0.75 * edge);
                        h = edge * (0.6 + 0.4 * f);
                        break;
                    }
                    case Material.Brick:
                    {
                        int perRow = Math.Max(1, cells / 2);   // whole bricks per row, so rows tile across
                        // an even number of rows, so the half-brick offset repeats from top to bottom
                        double rows = cells + cells % 2, by = v * rows, row = Math.Floor(by), bx = u * perRow + (row % 2) * 0.5;
                        double mx = Math.Min(bx - Math.Floor(bx), 1 - (bx - Math.Floor(bx))) / perRow * n, my = Math.Min(by - row, 1 - (by - row)) / rows * n;
                        double mortar = Math.Clamp(Math.Min(mx, my) / 2, 0, 1);
                        var r = new Rng(seed ^ (ulong)(((long)row * 31 + ((long)Math.Floor(bx) % perRow + perRow) % perRow) & 0xFFFF));
                        shade = mortar < 1 ? 0.25 + 0.2 * f : 0.5 + 0.3 * r.NextDouble() + 0.2 * f;
                        h = mortar * (0.8 + 0.2 * f);
                        break;
                    }
                    case Material.Wood:
                    {
                        double warp = woodWarp.At(u, v) * 3;
                        double rings = Math.Sin((u * cells + warp) * Math.PI * 2) * 0.5 + 0.5;
                        double grain = woodGrain.At(u, v);
                        shade = 0.35 + 0.45 * rings + 0.2 * grain;
                        h = 0.5 + 0.3 * rings + 0.2 * grain;
                        break;
                    }
                    case Material.Water:
                    {
                        double waves = Math.Sin((v * cells + fbm.At(u, v) * 2) * Math.PI * 2) * 0.5 + 0.5;
                        shade = 0.4 + 0.35 * waves + 0.25 * fine.At(u, v);
                        h = waves * 0.5 + 0.5 * f;
                        break;
                    }
                    case Material.Grass:
                    {
                        double blade = blades.At(u, v);                            // tall thin strokes
                        shade = 0.3 + 0.5 * blade + 0.2 * f;
                        h = blade;
                        break;
                    }
                    case Material.Sand:
                    {
                        double ripples = Math.Sin((v * cells + f * 1.5) * Math.PI * 2) * 0.5 + 0.5;
                        double grains = speck.NextDouble();
                        shade = 0.55 + 0.25 * ripples + 0.12 * (grains - 0.5) + 0.08 * f;
                        h = 0.6 * ripples + 0.4 * f;
                        break;
                    }
                    case Material.Metal:
                    {
                        double brushed = brush.At(u, v);                           // long horizontal streaks
                        shade = 0.55 + 0.3 * brushed + 0.15 * f;
                        h = 0.5 + 0.1 * brushed;
                        break;
                    }
                    default:   // ground
                    {
                        double clumps = fine.At(u, v);
                        shade = 0.3 + 0.5 * f + 0.2 * clumps + (speck.NextDouble() < 0.02 ? 0.2 : 0);
                        h = 0.7 * f + 0.3 * clumps;
                        break;
                    }
                }
                albedo[y * n + x] = Lerp(t.Dark, t.Light, Math.Clamp(shade, 0, 1));
                height[y * n + x] = (float)Math.Clamp(h, 0, 1);
            }
        return (albedo, height);
    }

    static uint Lerp(uint a, uint b, double t) => 0xFF000000 | ColorUtil.Lerp(a, b, t);   // textures are always opaque

    // ---------- derived maps ----------

    /// <summary>Tangent-space normal map from a height map (Sobel, wrapping at the edges so it tiles).</summary>
    public static uint[] NormalMap(float[] h, int w, int hgt, double strength, bool flipGreen = false)
    {
        var o = new uint[w * hgt];
        float H(int x, int y) => h[Tools.Wrap(y, hgt) * w + Tools.Wrap(x, w)];
        for (int y = 0; y < hgt; y++)
            for (int x = 0; x < w; x++)
            {
                double dx = (H(x + 1, y - 1) + 2 * H(x + 1, y) + H(x + 1, y + 1)) - (H(x - 1, y - 1) + 2 * H(x - 1, y) + H(x - 1, y + 1));
                double dy = (H(x - 1, y + 1) + 2 * H(x, y + 1) + H(x + 1, y + 1)) - (H(x - 1, y - 1) + 2 * H(x, y - 1) + H(x + 1, y - 1));
                double nx = -dx * strength, ny = dy * strength, nz = 1;   // y up in the map (OpenGL style)
                if (flipGreen) ny = -ny;
                double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                uint E(double v) => (uint)Math.Round((v / len * 0.5 + 0.5) * 255);
                o[y * w + x] = 0xFF000000 | E(nx) << 16 | E(ny) << 8 | E(nz);
            }
        return o;
    }

    /// <summary>Ambient occlusion guess: points lower than their surroundings get darker (wrapping box blur of the height).</summary>
    public static uint[] OcclusionMap(float[] h, int w, int hgt, double strength)
    {
        int r = Math.Max(1, Math.Min(w, hgt) / 32);
        var blur = BoxBlur(h, w, hgt, r);
        var o = new uint[w * hgt];
        for (int i = 0; i < o.Length; i++)
        {
            double ao = Math.Clamp(1 - Math.Max(0, blur[i] - h[i]) * 4 * strength, 0, 1);
            uint g = (uint)Math.Round(ao * 255);
            o[i] = 0xFF000000 | g << 16 | g << 8 | g;
        }
        return o;
    }

    static float[] BoxBlur(float[] src, int w, int h, int r)
    {
        var tmp = new float[src.Length];
        var dst = new float[src.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double s = 0;
                for (int k = -r; k <= r; k++) s += src[y * w + Tools.Wrap(x + k, w)];
                tmp[y * w + x] = (float)(s / (2 * r + 1));
            }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double s = 0;
                for (int k = -r; k <= r; k++) s += tmp[Tools.Wrap(y + k, h) * w + x];
                dst[y * w + x] = (float)(s / (2 * r + 1));
            }
        return dst;
    }

    /// <summary>
    /// Makes a picture tile: it is blended with a copy shifted by half its size, weighted so the copy only shows near
    /// the original's edges (where the seams would be).
    /// </summary>
    public static uint[] MakeSeamless(uint[] px, int w, int h, int blend)
    {
        if (blend <= 0) return px;
        var o = new uint[px.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double ex = Math.Min(x + 0.5, w - x - 0.5) / blend, ey = Math.Min(y + 0.5, h - y - 0.5) / blend;
                double keep = Math.Clamp(Math.Min(ex, ey), 0, 1);   // 1 in the middle, 0 at the border
                uint a = px[y * w + x], b = px[(y + h / 2) % h * w + (x + w / 2) % w];
                uint C(int s) => (uint)Math.Round((a >> s & 0xFF) * keep + (b >> s & 0xFF) * (1 - keep)) << s;
                o[y * w + x] = C(24) | C(16) | C(8) | C(0);
            }
        return o;
    }

    /// <summary>Largest colour difference across the wrap-around edges (0 = seamless); for tests and the status bar.</summary>
    public static int SeamError(uint[] px, int w, int h)
    {
        int D(uint a, uint b) => Math.Max(Math.Max(Math.Abs((int)(a >> 16 & 0xFF) - (int)(b >> 16 & 0xFF)), Math.Abs((int)(a >> 8 & 0xFF) - (int)(b >> 8 & 0xFF))), Math.Abs((int)(a & 0xFF) - (int)(b & 0xFF)));
        int worst = 0;
        for (int y = 0; y < h; y++) worst = Math.Max(worst, D(px[y * w], px[y * w + w - 1]) - Math.Max(D(px[y * w], px[y * w + 1]), D(px[y * w + w - 1], px[y * w + w - 2])));
        for (int x = 0; x < w; x++) worst = Math.Max(worst, D(px[x], px[(h - 1) * w + x]) - Math.Max(D(px[x], px[w + x]), D(px[(h - 1) * w + x], px[(h - 2) * w + x])));
        return Math.Max(0, worst);
    }

    /// <summary>Simple lighting preview: albedo × (ambient + diffuse) with the normal map and a light from the given angle.</summary>
    public static uint[] Lit(TextureMaps m, double lightAngleDegrees, double elevation = 0.6)
    {
        double a = lightAngleDegrees * Math.PI / 180, lx = Math.Cos(a) * Math.Sqrt(1 - elevation * elevation), ly = Math.Sin(a) * Math.Sqrt(1 - elevation * elevation), lz = elevation;
        var o = new uint[m.Albedo.Length];
        for (int i = 0; i < o.Length; i++)
        {
            uint n = m.Normal[i], c = m.Albedo[i];
            double nx = (n >> 16 & 0xFF) / 127.5 - 1, ny = (n >> 8 & 0xFF) / 127.5 - 1, nz = (n & 0xFF) / 127.5 - 1;
            double ao = (m.Occlusion[i] & 0xFF) / 255.0;
            double k = Math.Clamp((0.35 + 0.9 * Math.Max(0, nx * lx - ny * ly + nz * lz)) * ao, 0, 1.4);   // map y is up, screen y is down
            uint C(int s) => (uint)Math.Clamp(Math.Round((c >> s & 0xFF) * k), 0, 255) << s;
            o[i] = c & 0xFF000000 | C(16) | C(8) | C(0);
        }
        return o;
    }

    public static uint[] Gray(float[] h) => [.. h.Select(v => { uint g = (uint)Math.Round(Math.Clamp(v, 0, 1) * 255); return 0xFF000000 | g << 16 | g << 8 | g; })];

    /// <summary>Godot CanvasTexture (diffuse + normal) for 2D lights.</summary>
    public static string GodotCanvasTexture(string diffusePath, string normalPath) =>
        "[gd_resource type=\"CanvasTexture\" load_steps=3 format=3]\n\n" +
        $"[ext_resource type=\"Texture2D\" path=\"{diffusePath}\" id=\"1\"]\n[ext_resource type=\"Texture2D\" path=\"{normalPath}\" id=\"2\"]\n\n" +
        "[resource]\ndiffuse_texture = ExtResource(\"1\")\nnormal_texture = ExtResource(\"2\")\ntexture_repeat = 2\n";
}
