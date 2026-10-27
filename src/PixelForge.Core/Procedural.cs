namespace PixelForge.Core;

/// <summary>SplitMix64: same seed, same numbers on every machine and .NET version (System.Random makes no such promise).</summary>
public sealed class Rng(ulong seed)
{
    ulong _s = seed;

    const ulong Gamma = 0x9E3779B97F4A7C15;

    public ulong NextULong()
    {
        ulong r = Hash(_s);
        _s += Gamma;
        return r;
    }

    /// <summary>One SplitMix64 step on its own: a well-mixed number for x (particles hash their index with it).</summary>
    public static ulong Hash(ulong x)
    {
        ulong z = x + Gamma;
        z = (z ^ z >> 30) * 0xBF58476D1CE4E5B9;
        z = (z ^ z >> 27) * 0x94D049BB133111EB;
        return z ^ z >> 31;
    }

    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));
    public int Next(int n) => (int)(NextDouble() * n);
    public bool Chance(double p) => NextDouble() < p;

    /// <summary>Seed from text: numbers are used as they are, anything else is hashed (FNV-1a), so "forest" is a valid seed.</summary>
    public static ulong Seed(string text)
    {
        if (ulong.TryParse(text.Trim(), out ulong n)) return n;
        ulong h = 14695981039346656037;
        foreach (char c in text) h = (h ^ c) * 1099511628211;
        return h;
    }
}

public enum TilePattern { Noise, Bricks, Grass, Stones }

/// <summary>Seeded generators for sprites, tiles and level layouts. Same seed and settings → same result.</summary>
public static class Procedural
{
    /// <summary>
    /// Mirrored creature/item sprite: random half mask, smoothed, outlined; colours from the palette (darkest = outline,
    /// others shade top to bottom). Fully transparent border of one pixel so the outline fits.
    /// </summary>
    public static uint[] Sprite(ulong seed, int w, int h, IReadOnlyList<uint> palette, double density = 0.45)
    {
        if (w < 4 || h < 4) throw new ArgumentOutOfRangeException(nameof(w), "Sprites need at least 4×4 pixels.");
        var colors = Ramp(palette);
        var r = new Rng(seed);
        int half = (w + 1) / 2;
        var on = new bool[w * h];
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < half; x++)
            {
                // denser towards the centre line and the middle rows, so the shape holds together
                double centre = 1 - Math.Abs(y - h / 2.0) / (h / 2.0) * 0.5 - (half - 1 - x) / (double)half * 0.4;
                bool v = r.Chance(density * (0.6 + centre * 0.8));
                on[y * w + x] = v;
                on[y * w + (w - 1 - x)] = v;
            }
        // one smoothing pass: cells follow the majority of their neighbours
        var smooth = (bool[])on.Clone();
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                int n = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if ((dx, dy) != (0, 0) && on[(y + dy) * w + x + dx]) n++;
                smooth[y * w + x] = n > 4 || (on[y * w + x] && n >= 2);
            }
        var px = new uint[w * h];
        var fills = colors.Skip(1).ToList();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (smooth[y * w + x])
                {
                    int band = Math.Clamp((int)((1 - y / (double)h) * fills.Count + (r.NextDouble() - 0.5) * 0.8), 0, fills.Count - 1);
                    px[y * w + x] = fills[band];
                }
                else if (Neighbour(smooth, w, h, x, y)) px[y * w + x] = colors[0];
            }
        // mirror the colours too, so both halves match exactly
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w / 2; x++) px[y * w + (w - 1 - x)] = px[y * w + x];
        return px;
    }

    static bool Neighbour(bool[] m, int w, int h, int x, int y) =>
        (x > 0 && m[y * w + x - 1]) || (x < w - 1 && m[y * w + x + 1]) || (y > 0 && m[(y - 1) * w + x]) || (y < h - 1 && m[(y + 1) * w + x]);

    static double Luma(uint c) => 0.299 * (c >> 16 & 0xFF) + 0.587 * (c >> 8 & 0xFF) + 0.114 * (c & 0xFF);

    /// <summary>The palette's visible colours, darkest first.</summary>
    static List<uint> Ramp(IReadOnlyList<uint> palette)
    {
        var ramp = palette.Where(c => c >> 24 != 0).Distinct().OrderBy(Luma).ToList();
        if (ramp.Count < 2) throw new ArgumentException("The palette needs at least two visible colours.", nameof(palette));
        return ramp;
    }

    /// <summary>Marks the outer ring of cells solid.</summary>
    static void Border(bool[] m, int w, int h)
    {
        for (int x = 0; x < w; x++) m[x] = m[(h - 1) * w + x] = true;
        for (int y = 0; y < h; y++) m[y * w] = m[y * w + w - 1] = true;
    }

    /// <summary>A row of seamless tiles (variants of one pattern), ready to load as a tileset.</summary>
    public static (uint[] Pixels, int Width, int Height) Tiles(ulong seed, int size, IReadOnlyList<uint> palette, TilePattern pattern, int variants = 4)
    {
        if (size < 4 || size > 128) throw new ArgumentOutOfRangeException(nameof(size), "Tile size must be 4–128.");
        var ramp = Ramp(palette);
        variants = Math.Clamp(variants, 1, 16);
        int w = size * variants;
        var px = new uint[w * size];
        for (int v = 0; v < variants; v++)
            PixelOps.Paste(px, w, Tile(new Rng(seed + (ulong)v * 7919), size, ramp, pattern), size, size, v * size, 0);
        return (px, w, size);
    }

    static uint[] Tile(Rng r, int n, List<uint> ramp, TilePattern pattern)
    {
        var o = new uint[n * n];
        // wrap-around value noise on a 4×4 lattice: the tile repeats without seams
        int g = 4;
        var lattice = new double[g * g];
        for (int i = 0; i < lattice.Length; i++) lattice[i] = r.NextDouble();
        double Noise(int x, int y)
        {
            double fx = x * g / (double)n, fy = y * g / (double)n;
            int x0 = (int)fx, y0 = (int)fy;
            double tx = fx - x0, ty = fy - y0;
            tx = tx * tx * (3 - 2 * tx);
            ty = ty * ty * (3 - 2 * ty);
            double L(int a, int b) => lattice[(b % g) * g + a % g];
            return (L(x0, y0) * (1 - tx) + L(x0 + 1, y0) * tx) * (1 - ty) + (L(x0, y0 + 1) * (1 - tx) + L(x0 + 1, y0 + 1) * tx) * ty;
        }
        uint Shade(double t) => ramp[Math.Clamp((int)(t * ramp.Count), 0, ramp.Count - 1)];

        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                double noise = Noise(x, y) * 0.75 + r.NextDouble() * 0.25;
                o[y * n + x] = pattern switch
                {
                    TilePattern.Bricks => Brick(x, y, n, noise, r, ramp),
                    TilePattern.Grass => y < n / 4 + (int)(Noise(x, 0) * n / 6) ? (noise > 0.6 ? ramp[^2] : ramp[^1]) : Shade(noise * 0.6),
                    TilePattern.Stones => Noise(x * 2 % n, y * 2 % n) > 0.55 ? Shade(0.5 + noise * 0.5) : Shade(noise * 0.35),
                    _ => Shade(noise),
                };
            }
        return o;
    }

    static uint Brick(int x, int y, int n, double noise, Rng r, List<uint> ramp)
    {
        int rowH = Math.Max(2, n / 4), brickW = Math.Max(4, n / 2);
        int row = y / rowH, offset = row % 2 == 0 ? 0 : brickW / 2;
        bool mortar = y % rowH == rowH - 1 || (x + offset) % brickW == 0;
        return mortar ? ramp[0] : ramp[Math.Clamp(1 + (int)(noise * (ramp.Count - 1)), 1, ramp.Count - 1)];
    }

    /// <summary>Cave layout (true = solid) by cellular automaton; the border is always solid.</summary>
    public static bool[] Cave(ulong seed, int w, int h, double fill = 0.45, int steps = 4)
    {
        var r = new Rng(seed);
        var m = new bool[w * h];
        for (int i = 0; i < m.Length; i++) m[i] = r.Chance(fill);
        for (int s = 0; s < steps; s++)
        {
            var next = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if ((dx, dy) != (0, 0) && (!Tools.InBounds(w, h, nx, ny) || m[ny * w + nx])) n++;
                        }
                    next[y * w + x] = n > 4 || (m[y * w + x] && n == 4);
                }
            m = next;
        }
        Border(m, w, h);
        return m;
    }

    /// <summary>Side-view platformer layout (true = solid): rolling ground with gaps a player can jump, plus floating platforms.</summary>
    public static bool[] Platforms(ulong seed, int w, int h, int maxGap = 3)
    {
        var r = new Rng(seed);
        var m = new bool[w * h];
        int ground = h - Math.Max(2, h / 4);
        for (int x = 0; x < w;)
        {
            int run = 3 + r.Next(6);
            for (int i = 0; i < run && x < w; i++, x++)
                for (int y = ground; y < h; y++) m[y * w + x] = true;
            if (maxGap > 0 && x > 4 && x < w - 4 && r.Chance(0.35)) x += 1 + r.Next(maxGap);   // a gap
            ground = Math.Clamp(ground + r.Next(3) - 1, h / 2, h - 2);
        }
        for (int k = 0; k < w / 8; k++)
        {
            int len = 2 + r.Next(4), px = r.Next(Math.Max(1, w - len)), py = 2 + r.Next(Math.Max(1, h / 2 - 2));
            for (int i = 0; i < len; i++) m[py * w + px + i] = true;
        }
        return m;
    }

    /// <summary>Top-down rooms (true = solid): an outer wall and inner walls on a grid, each with a doorway, plus a few pillars.</summary>
    public static bool[] Rooms(ulong seed, int w, int h, int roomSize = 8)
    {
        var r = new Rng(seed);
        var m = new bool[w * h];
        Border(m, w, h);
        for (int wx = roomSize; wx < w - 2; wx += roomSize)
        {
            int door = 1 + r.Next(Math.Max(1, h - 3));
            for (int y = 1; y < h - 1; y++) if (Math.Abs(y - door) > 1) m[y * w + wx] = true;
        }
        for (int wy = roomSize; wy < h - 2; wy += roomSize)
            for (int x = 1; x < w - 1; x++)
                if (x % roomSize != roomSize / 2 && x % roomSize != roomSize / 2 + 1) m[wy * w + x] = true;   // doorways in the middle of each room
        for (int k = 0; k < w * h / 60; k++)
        {
            int x = 2 + r.Next(Math.Max(1, w - 4)), y = 2 + r.Next(Math.Max(1, h - 4));
            if (x % roomSize > 1 && y % roomSize > 1) m[y * w + x] = true;
        }
        return m;
    }

    static readonly int[] Major = [0, 2, 4, 5, 7, 9, 11], Minor = [0, 2, 3, 5, 7, 8, 10], Pentatonic = [0, 2, 4, 7, 9];
    static readonly int[][] Progressions = [[0, 4, 5, 3], [0, 5, 3, 4], [0, 3, 4, 4], [5, 3, 0, 4]];   // scale degrees per bar

    /// <summary>
    /// A short chiptune loop: chord roots on the triangle, an arpeggio on pulse 2, a melody that walks the scale on
    /// pulse 1, and a drum kit beat on noise (a fill in the last bar). One pattern per bar; same seed, same song.
    /// </summary>
    public static Song Song(ulong seed, int bpm, string scale, int patterns)
    {
        var r = new Rng(seed);
        int[] sc = scale switch { "minor" => Minor, "pentatonic" => Pentatonic, _ => Major };
        int root = 57 + r.Next(5);   // A3..C#4
        var prog = Progressions[r.Next(Progressions.Length)];
        var s = new Song(16, empty: true) { Bpm = Math.Clamp(bpm, 40, 300) };
        int Note(int degree, int octave) => root + octave * 12 + sc[Tools.Wrap(degree, sc.Length)] + 12 * (int)Math.Floor(degree / (double)sc.Length);
        int melody = 2;
        int bars = Math.Clamp(patterns, 1, 16);
        for (int bar = 0; bar < bars; bar++)
        {
            var pat = Pattern.Empty(16);
            int chord = prog[bar % prog.Length] % sc.Length;
            pat.SetNote((int)Channel.Triangle, 0, Note(chord, -1), 8);
            pat.SetNote((int)Channel.Triangle, 8, Note(chord, -1), 8);
            for (int st = 0; st < 16; st += 2) pat.SetNote((int)Channel.Pulse2, st, Note(chord + st / 2 % 3 * 2, 0), 1);
            for (int st = 0; st < 16;)
            {
                int len = r.Chance(0.3) ? 4 : 2;
                if (r.Chance(0.85)) pat.SetNote((int)Channel.Pulse1, st, Math.Clamp(Note(melody, 1), Core.Song.MinNote, Core.Song.MaxNote), Math.Min(len, 16 - st));
                melody = Math.Clamp(melody + r.Next(5) - 2, -2, 9);
                st += len;
            }
            DrumBeats.Apply(pat, bar == bars - 1 && bar > 0 ? "Tom fill" : bpm >= 130 ? "Four on the floor" : "Rock");
            s.Patterns.Add(pat);
            s.Order.Add(bar);
        }
        return s;
    }

    /// <summary>Writes a layout into a map layer: with a terrain rule the edges are auto-tiled, otherwise one tile is used.</summary>
    public static void Fill(Tilemap map, MapLayer layer, bool[] solid, TerrainRule? terrain, int tile)
    {
        if (solid.Length != map.Width * map.Height) throw new ArgumentException("The layout does not match the map size.", nameof(solid));
        Array.Fill(layer.Tiles, Tilemap.Empty);
        for (int i = 0; i < solid.Length; i++)
        {
            if (!solid[i]) continue;
            if (terrain is not null) Autotile.Apply(map, layer, terrain, i % map.Width, i / map.Width, true);
            else layer.Tiles[i] = tile;
        }
    }
}
