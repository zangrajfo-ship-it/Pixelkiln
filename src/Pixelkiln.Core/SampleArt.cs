namespace Pixelkiln.Core;

/// <summary>
/// Hand-made example pixel art for learning: a hero with idle, run and jump, a slime, a spinning coin, a chest,
/// items, a tree, a cloud and a 16 px tileset. Small pieces are drawn as character grids, round shapes with code.
/// </summary>
public static class SampleArt
{
    public static readonly IReadOnlyList<string> Names = ["hero", "slime", "coin", "chest", "items", "tree", "cloud", "tiles"];

    // one palette for everything, so the samples look like one game
    static readonly Dictionary<char, uint> Ink = new()
    {
        ['.'] = 0x00000000, ['k'] = 0xFF1A1C2C, ['w'] = 0xFFF4F4F4, ['s'] = 0xFFF4C79A, ['h'] = 0xFF6B3E26,
        ['r'] = 0xFFD04648, ['b'] = 0xFF3B5DC9, ['y'] = 0xFFFFCD4A, ['Y'] = 0xFFC9852B, ['g'] = 0xFF38B764,
        ['G'] = 0xFF257179, ['l'] = 0xFFA7F070, ['d'] = 0xFF8A5A32, ['D'] = 0xFF5D3A1A, ['e'] = 0xFF94B0C2,
        ['E'] = 0xFF566C86, ['c'] = 0xFF73EFF7, ['n'] = 0xFF29366F, ['B'] = 0xFF41A6F6,
    };

    public static IReadOnlyList<uint> Palette => [.. Ink.Values.Where(c => c >> 24 != 0)];

    static uint C(char c) => Ink[c];

    static uint[] Grid(params string[] rows)
    {
        int w = rows[0].Length;
        if (rows.Any(r => r.Length != w)) throw new InvalidOperationException("Sample art rows differ in width.");
        return [.. rows.SelectMany(r => r.Select(C))];
    }

    static Sprite Build(int w, int h, IEnumerable<(uint[] Px, int Ms)> frames, params (string Name, int From, int To)[] tags)
    {
        var s = new Sprite(w, h);
        s.Palette.Clear();
        s.Palette.AddRange(Palette);
        int i = 0;
        foreach (var (px, ms) in frames)
        {
            if (i > 0) s.AddFrame(i);
            if (px.Length != w * h) throw new InvalidOperationException("Sample art frame has the wrong size.");
            px.CopyTo(s.Cel(0, i), 0);
            s.Frames[i].DurationMs = ms;
            i++;
        }
        foreach (var (name, from, to) in tags) s.Tags.Add(new FrameTag { Name = name, From = from, To = to });
        return s;
    }

    /// <summary>A sample sprite by name (see <see cref="Names"/>).</summary>
    public static Sprite Make(string name) => name switch
    {
        "hero" => Hero(),
        "slime" => Slime(),
        "coin" => Coin(),
        "chest" => Chest(),
        "items" => Items(),
        "tree" => Tree(),
        "cloud" => Cloud(),
        "tiles" => Tiles(),
        _ => throw new ArgumentException($"There is no sample art “{name}”.", nameof(name)),
    };

    // ---------- hero: 16×16, faces right ----------

    static readonly string[] HeroTop =
    [
        "................",
        ".....kkkkk......",
        "....khhhhhk.....",
        "....khhhhhhk....",
        "....khsssssk....",
        "....kssskskk....",
        "....ksssssk.....",
        ".....kkkkk......",
        "....krrrrrk.....",
        "...ksrrrrrsk....",
        "...kkrrrrrkk....",
    ];

    static string[] Blink(string[] top) { var t = (string[])top.Clone(); t[5] = "....ksssssskk..."; return t; }

    static readonly string[] LegsStand = ["....kbbbbbk.....", "....kbbkbbk.....", "....kbk.kbk.....", "....kkk.kkk.....", "................"];
    static readonly string[] LegsRun1 = ["....kbbbbbk.....", "...kbbk.kbbk....", "..kbk.....kbk...", "..kk.......kk...", "................"];
    static readonly string[] LegsRun2 = ["....kbbbbbk.....", "....kbbkbbk.....", ".....kbkbk......", ".....kkkkk......", "................"];
    static readonly string[] LegsRun3 = ["....kbbbbbk.....", "...kbbk.kbk.....", "...kbk...kbk....", "...kk.....kk....", "................"];
    static readonly string[] LegsJump = ["...kbbbbbbbk....", "..kbbk...kbbk...", "..kk.......kk...", "................", "................"];

    static Sprite Hero()
    {
        uint[] F(string[] top, string[] legs) => Grid([.. top, .. legs]);
        return Build(16, 16,
        [
            (F(HeroTop, LegsStand), 600), (F(Blink(HeroTop), LegsStand), 150),
            (F(HeroTop, LegsRun1), 100), (F(HeroTop, LegsRun2), 100), (F(HeroTop, LegsRun3), 100), (F(HeroTop, LegsRun2), 100),
            (F(HeroTop, LegsJump), 100),
        ], ("idle", 0, 1), ("run", 2, 5), ("jump", 6, 6));
    }

    // ---------- round things drawn with code ----------

    /// <summary>A filled ellipse with an outline; fill(x, y, u, v) colours inside pixels (u, v = −1..1 across the shape).</summary>
    static uint[] Blob(int w, int h, double cx, double cy, double rx, double ry, Func<int, int, double, double, uint> fill, uint[]? onto = null)
    {
        var o = onto ?? new uint[w * h];
        bool In(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && Math.Pow((x + 0.5 - cx) / rx, 2) + Math.Pow((y + 0.5 - cy) / ry, 2) <= 1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (In(x, y))
                    o[y * w + x] = !In(x - 1, y) || !In(x + 1, y) || !In(x, y - 1) || !In(x, y + 1)
                        ? C('k') : fill(x, y, (x + 0.5 - cx) / rx, (y + 0.5 - cy) / ry);
        return o;
    }

    static Sprite Slime()
    {
        // squash and stretch: (half width, half height, lift off the ground)
        (double Rx, double Ry, double Lift)[] poses = [(6.5, 4.5, 0), (7, 4, 0), (7.5, 3.5, 0), (5, 5.5, 3), (5.5, 5, 1)];
        var frames = poses.Select(p =>
        {
            double cy = 15 - p.Ry - p.Lift;
            var px = Blob(16, 16, 8, cy, p.Rx, p.Ry, (x, y, u, v) => u < -0.3 && v < -0.3 ? C('l') : v > 0.45 ? C('G') : C('g'));
            int ey = (int)Math.Round(cy - 1);
            foreach (int ex in new[] { 6, 10 }) { px[ey * 16 + ex] = C('k'); px[(ey - 1) * 16 + ex] = C('k'); px[(ey - 1) * 16 + ex - 1] = C('w'); }
            return (px, 0);
        }).ToList();
        return Build(16, 16, [(frames[0].px, 400), (frames[1].px, 400), (frames[2].px, 120), (frames[3].px, 120), (frames[4].px, 120)],
            ("idle", 0, 1), ("hop", 2, 4));
    }

    static Sprite Coin()
    {
        // a spinning coin is an ellipse that gets thinner and wider again
        double[] widths = [5, 3.5, 1.2, 3.5];
        var frames = widths.Select(rx => (Blob(16, 16, 8, 8, rx, 5.5, (x, y, u, v) => rx < 2 ? C('Y') : u < -0.35 ? C('w') : u > 0.5 ? C('Y') : C('y')), 120));
        return Build(16, 16, frames, ("spin", 0, 3));
    }

    static Sprite Tree()
    {
        var px = new uint[32 * 32];
        for (int y = 19; y < 32; y++)
            for (int x = 13; x < 19; x++)
                px[y * 32 + x] = x == 13 || x == 18 ? C('k') : x == 14 ? C('d') : C('D');
        foreach (var (cx, cy, r) in new[] { (10.0, 15.0, 7.0), (22.0, 15.0, 7.0), (16.0, 9.0, 8.5) })
            Blob(32, 32, cx, cy, r, r * 0.9, (x, y, u, v) => u < -0.2 && v < -0.2 ? C('l') : v > 0.5 ? C('G') : C('g'), px);
        return Build(32, 32, [(px, 100)]);
    }

    static Sprite Cloud()
    {
        var px = new uint[32 * 16];
        foreach (var (cx, cy, rx, ry) in new[] { (9.0, 10.0, 7.0, 5.0), (22.0, 10.0, 8.0, 5.0), (15.0, 7.0, 7.0, 6.0) })
            Blob(32, 16, cx, cy, rx, ry, (x, y, u, v) => v > 0.4 ? C('e') : C('w'), px);
        return Build(32, 16, [(px, 100)]);
    }

    // ---------- small pieces as grids ----------

    static readonly string[] ChestBody =
    [
        "..kdddddyddddDk.",
        "..kddddyYydddDk.",
        "..kdddddyddddDk.",
        "..kddddddddddDk.",
        "..kDDDDDDDDDDDk.",
        "..kkkkkkkkkkkkk.",
    ];

    static Sprite Chest()
    {
        string blank = "................";
        uint[] closed = Grid([blank, blank, blank, "...kkkkkkkkkk...", "..kdddddddddddk.", "..kdDDDDDDDDDdk.", "..kkkkkkkkkkkkk.", .. ChestBody, blank, blank, blank]);
        uint[] open = Grid([blank, "...kkkkkkkkkk...", "..kDDDDDDDDDDk..", "..kdddddddddddk.", "..kyyYyyyYyyyyk.", "..kkkkkkkkkkkkk.", .. ChestBody, blank, blank, blank, blank]);
        return Build(16, 16, [(closed, 100), (open, 100)], ("closed", 0, 0), ("open", 1, 1));
    }

    static Sprite Items()
    {
        string b = "................";
        uint[] heart = Grid(b, b, "...kkk...kkk....", "..krrrk.krrrk...", ".krwwrrkrrrrrk..", ".krwrrrrrrrrrk..", ".krrrrrrrrrrrk..", "..krrrrrrrrrk...",
            "...krrrrrrrk....", "....krrrrrk.....", ".....krrrk......", "......krk.......", ".......k........", b, b, b);
        uint[] key = Grid(b, b, "...kkkk.........", "..kyyyyk........", "..kykkyk........", "..kyyyykkkkkkk..", "...kkkkyyyyyyyk.", "......kkkkykkyk.",
            "..........k..k..", b, b, b, b, b, b, b);
        uint[] gem = Grid(b, b, "....kkkkkkk.....", "...kcwcccnck....", "..kcwcccccnnk...", "..kkkkkkkkkkk...", "...kcccccnnk....", "....kcccnnk.....",
            ".....kccnk......", "......kck.......", ".......k........", b, b, b, b, b);
        return Build(16, 16, [(heart, 100), (key, 100), (gem, 100)], ("heart", 0, 0), ("key", 1, 1), ("gem", 2, 2));
    }

    // ---------- tileset: eight 16 px tiles in a row ----------

    /// <summary>Tile names in order (index = tile number in the tileset).</summary>
    static readonly string[] TileNames = ["grass", "dirt", "stone", "brick", "ladder", "water", "platform", "spikes"];

    static Sprite Tiles()
    {
        const int T = 16;
        var px = new uint[T * TileNames.Length * T];
        static bool Speck(int x, int y, int salt) => ((x * 7 + y * 13 + salt * 31) * 2654435761u >> 28) == 0;
        void Put(int tile, int x, int y, char c) { if (c != '.') px[y * T * TileNames.Length + tile * T + x] = C(c); }
        for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                char dirt = Speck(x, y, 1) ? 'D' : Speck(x, y, 2) ? 'Y' : 'd';
                // 0 grass: a green top with a ragged edge over dirt
                int edge = 4 + (x * 5 % 3 == 0 ? 1 : 0);
                Put(0, x, y, y == 0 ? 'l' : y < edge ? 'g' : y == edge ? 'G' : dirt);
                Put(1, x, y, dirt);
                // 2 stone: blocks with dark seams and a light top-left edge
                bool seam = y % 8 == 7 || (x + (y / 8 % 2) * 6) % 12 == 11;
                Put(2, x, y, seam ? 'E' : y % 8 == 0 || (x + (y / 8 % 2) * 6) % 12 == 0 ? 'w' : Speck(x, y, 3) ? 'E' : 'e');
                // 3 brick: red bricks in a running bond
                bool mortar = y % 4 == 3 || (x + (y / 4 % 2) * 4) % 8 == 7;
                Put(3, x, y, mortar ? 'e' : y % 4 == 0 ? 'r' : 'h');
                // 4 ladder: two rails and rungs, see-through between
                Put(4, x, y, x is 2 or 13 ? 'D' : x is 3 or 12 ? 'd' : (y % 5 == 2 && x is > 3 and < 12) ? 'd' : (y % 5 == 3 && x is > 3 and < 12) ? 'D' : '.');
                // 5 water: blue with light wave lines
                Put(5, x, y, (y + (x / 4 % 2)) % 6 == 1 && x % 4 != 3 ? 'c' : y > 11 ? 'n' : 'B');
                // 6 platform: a plank on top, empty below (one-way in the sample map)
                Put(6, x, y, y == 0 || y == 5 ? 'k' : y < 5 ? (x % 8 == 7 ? 'D' : y == 1 ? 'Y' : 'd') : '.');
                // 7 spikes: four grey triangles standing on the floor
                double half = (y - 3) / 3.0, mid = x / 8 * 8 + 4;   // two spikes, each widens from a tip at y 3 to 8 px at the floor
                Put(7, x, y, y >= T - 2 ? 'E' : y >= 3 && Math.Abs(x + 0.5 - mid) <= half ? (x + 0.5 < mid ? 'w' : 'e') : '.');
            }
        return Build(T * TileNames.Length, T, [(px, 100)]);
    }
}
