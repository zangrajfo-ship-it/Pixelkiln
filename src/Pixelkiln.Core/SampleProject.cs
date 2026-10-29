namespace Pixelkiln.Core;

/// <summary>
/// A small complete game to learn from: hand-drawn art, a hero and a slime, a tilemap, two levels (a sunny one with
/// coins and a dark cave with torches), logic, music with drums, sound effects, a HUD and a soundtrack mix.
/// </summary>
public static class SampleProject
{
    public const string Name = "Sample game";
    const int T = 16;

    // tile numbers of SampleArt's tileset
    const int Grass = 0, Dirt = 1, Stone = 2, Brick = 3, Ladder = 4, Water = 5, Plank = 6, Spikes = 7;

    /// <summary>Creates the sample in an empty (or new) folder and returns the project.</summary>
    public static Project Create(string folder)
    {
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new IOException($"“{folder}” is not empty; choose an empty folder for the sample project.");
        var p = Project.Create(folder, Name);
        string F(string rel)
        {
            string path = Path.Combine(folder, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return path;
        }
        AssetEntry Add(string rel) => p.Add(F(rel));

        // art
        var art = SampleArt.Names.ToDictionary(n => n, n =>
        {
            ProjectFile.Save(SampleArt.Make(n), F($"art/{n}.pxf"));
            return Add($"art/{n}.pxf");
        });

        // characters: feet origin, a body that hurts and solid feet
        AssetEntry MakeCharacter(string name, CharacterKind kind, string[] states, int category, Dictionary<string, string> props)
        {
            var c = new Character { Kind = kind, SpriteAssetId = art[name].Id, OriginX = 8, OriginY = 16, Properties = props };
            foreach (var s in states) c.Animations[s] = s;
            c.Boxes.Add(new CharacterBox { Kind = BoxKind.Hurt, X = 4, Y = 3, W = 8, H = 12 });
            c.Boxes.Add(new CharacterBox { Kind = BoxKind.Solid, X = 4, Y = 4, W = 8, H = 12, Category = category });
            Character.Save(c, F($"characters/{name}.pxchar"));
            return Add($"characters/{name}.pxchar");
        }
        var hero = MakeCharacter("hero", CharacterKind.Player, ["idle", "run", "jump"], 1, new() { ["speed"] = "90", ["jump"] = "250", ["gravity"] = "640" });
        var slime = MakeCharacter("slime", CharacterKind.Enemy, ["idle", "hop"], 2, new() { ["damage"] = "1" });

        // sound: two songs with drums, three effects and a soundtrack mix
        Song.Save(Procedural.Song(Rng.Seed("sunny"), 124, "major", 4), F("music/sunny.pxs"));
        var sunny = Add("music/sunny.pxs");
        var caveSong = Procedural.Song(Rng.Seed("cave"), 92, "minor", 4);
        foreach (var pat in caveSong.Patterns) DrumBeats.Apply(pat, "Half-time");
        Song.Save(caveSong, F("music/cave.pxs"));
        var cave = Add("music/cave.pxs");
        var sounds = new[] { "Coin", "Jump", "Hit" }.ToDictionary(n => n, n =>
        {
            Sfx.Save(Sfx.Preset(n, new Random(7)), F($"sounds/{n.ToLowerInvariant()}.sfx"));
            return Add($"sounds/{n.ToLowerInvariant()}.sfx");
        });
        var mix = new Mix();
        mix.Tracks[0].Clips.Add(new MixClip { AssetId = sunny.Id, Repeat = 2 });
        mix.Tracks[1].Clips.Add(new MixClip { AssetId = sounds["Coin"].Id, StartMs = 1000, Gain = 0.8 });
        Mix.Save(mix, F("music/soundtrack.pxmix"));
        Add("music/soundtrack.pxmix");

        var hud = UiKit.Starter(UiKit.Styles["Fantasy"], ["HUD"], p.Settings.TargetWidth, p.Settings.TargetHeight);
        UiKit.Save(hud, F("ui/hud.pxui"));
        var hudAsset = Add("ui/hud.pxui");

        var level2 = Cave(F, Add, art, hero, sounds, cave);
        Meadow(F, Add, art, hero, slime, sounds, sunny, hudAsset, level2);

        File.WriteAllText(F("README.txt"), Readme);
        p.Save();
        return p;
    }

    /// <summary>Level 1, “Meadow”: walk right, collect five coins, avoid the water and the spikes, reach the exit.</summary>
    static void Meadow(Func<string, string> F, Func<string, AssetEntry> Add, Dictionary<string, AssetEntry> art, AssetEntry hero, AssetEntry slime,
        Dictionary<string, AssetEntry> sounds, AssetEntry music, AssetEntry hud, AssetEntry next)
    {
        const int W = 40, H = 15, ground = 12;
        var map = Map(W, H, art["tiles"]);
        var tiles = map.Layers[0].Tiles;
        void Set(int x, int y, int tile) => tiles[y * W + x] = tile;
        for (int x = 0; x < W; x++)
        {
            if (x is >= 18 and <= 20) { Set(x, 13, Water); Set(x, 14, Water); continue; }   // a pond to jump over
            Set(x, ground, Grass);
            Set(x, 13, Dirt);
            Set(x, 14, Dirt);
        }
        for (int x = 10; x <= 13; x++) Set(x, 9, Plank);   // one-way platforms
        for (int x = 22; x <= 25; x++) Set(x, 7, Plank);
        Set(28, ground - 1, Spikes);
        foreach (int y in new[] { 10, 11 }) { Set(32, y, Stone); Set(33, y, Stone); }
        for (int y = 9; y < ground; y++) Set(8, y, Ladder);   // decoration: ladders have no collision
        for (int y = 0; y < ground; y++) Set(W - 1, y, Brick);   // the wall at the end
        Tilemap.Save(map, F("levels/meadow.pxm"));
        var mapAsset = Add("levels/meadow.pxm");

        var s = new Scene { Width = W * T, Height = H * T, Level = "World 1", Order = 1, Background = 0xFF8FC7E8, MusicAssetId = music.Id, UiAssetId = hud.Id, UiScreen = "HUD" };
        var sky = new SceneLayer { Name = "Sky (parallax)", Kind = SceneLayerKind.Instances, ParallaxX = 0.3, ParallaxY = 1 };
        s.Layers.Insert(0, sky);
        s.Layers.Insert(1, new SceneLayer { Name = "Ground", Kind = SceneLayerKind.Tilemap, AssetId = mapAsset.Id });
        foreach (var (cx, cy, k) in new[] { (20, 18, 1), (150, 40, 2), (260, 12, 3), (380, 34, 4), (500, 20, 5) })
            s.Instances.Add(new SceneInstance { Name = $"Cloud{k}", AssetId = art["cloud"].Id, LayerId = sky.Id, X = cx, Y = cy });
        var objects = s.Layers[^1];
        objects.Name = "Objects";
        int floor = ground * T;
        SceneInstance Put(string name, AssetEntry a, int x, int y, string? anim = null, int z = 0)
        {
            var i = new SceneInstance { Name = name, AssetId = a.Id, LayerId = objects.Id, X = x, Y = y, Animation = anim, Z = z };
            s.Instances.Add(i);
            return i;
        }
        Put("Tree", art["tree"], 4 * T, floor - 32, z: -1);
        Put("Tree2", art["tree"], 30 * T, floor - 32, z: -1);
        Put("Hero", hero, 2 * T, floor, "idle", 1);
        Put("Slime", slime, 14 * T, floor, "idle").Track = new KeyframeTrack { Keys = [new Keyframe(0), new Keyframe(1500, X: 40, Easing: Easing.EaseInOut), new Keyframe(3000)], Loop = true };
        var coins = new (int X, int Y)[] { (5 * T, floor - T), (7 * T, floor - T), (11 * T + 8, 9 * T - T), (23 * T + 8, 7 * T - T), (35 * T, floor - T) };
        for (int c = 0; c < coins.Length; c++) Put($"Coin{c + 1}", art["coin"], coins[c].X, coins[c].Y, "spin");
        Put("Chest", art["chest"], 37 * T, floor - T, "closed");
        s.Areas.Add(new SceneArea { Kind = AreaKind.Spawn, Name = "start", X = 2 * T, Y = floor - 2 * T, W = T, H = 2 * T });
        s.Areas.Add(new SceneArea { Kind = AreaKind.Checkpoint, Name = "halfway", X = 22 * T, Y = floor - 3 * T, W = T, H = 3 * T });
        s.Areas.Add(new SceneArea { Kind = AreaKind.Kill, Name = "pond", X = 18 * T, Y = 13 * T, W = 3 * T, H = 2 * T });
        s.Areas.Add(new SceneArea { Kind = AreaKind.Kill, Name = "spikes", X = 28 * T + 2, Y = floor - T + 6, W = T - 4, H = T - 6 });
        s.Areas.Add(new SceneArea { Kind = AreaKind.Trigger, Name = "exit", X = 37 * T, Y = floor - 3 * T, W = 2 * T, H = 3 * T });
        s.Emitters.Add(new SceneEmitter { Name = "pollen", Preset = EmitterPreset.Magic, X = 0, Y = 6 * T, W = W * T, H = 5 * T, Rate = 6, LifeMs = 3000, Speed = 6, Spread = 360, Gravity = -2, Size = 1, EndSize = 1, Color = 0xFFFFF4A0, EndColor = 0x00FFF4A0, Seed = 3 });

        // logic: each coin counts and disappears with a sound; the exit opens with all five
        var g = new LogicGraph();
        int n = 0;
        LogicNode Node(LogicKind k, double x, double y, params (string Key, string Value)[] ps)
        {
            var node = LogicGraph.NewNode(k, $"{char.ToLowerInvariant(k.ToString()[0])}{k.ToString()[1..]}{++n}", x, y);
            foreach (var (key, value) in ps) node.Params[key] = value;
            g.Nodes.Add(node);
            return node;
        }
        void Link(LogicNode a, LogicNode b, string port = "out") => g.Links.Add(new LogicLink(a.Id, port, b.Id));
        var start = Node(LogicKind.Start, 20, 20);
        Link(start, Node(LogicKind.SetVariable, 240, 20, ("variable", "coins"), ("op", "="), ("value", "0")));
        for (int c = 1; c <= coins.Length; c++)
        {
            double y = 20 + c * 90;
            var touch = Node(LogicKind.Touch, 20, y, ("object", $"Coin{c}"));
            var count = Node(LogicKind.SetVariable, 240, y, ("variable", "coins"), ("op", "+="), ("value", "1"));
            var hide = Node(LogicKind.Show, 460, y, ("object", $"Coin{c}"), ("visible", "no"));
            var ding = Node(LogicKind.PlaySound, 680, y, ("sound", sounds["Coin"].Id.ToString()));
            Link(touch, count); Link(count, hide); Link(hide, ding);
        }
        var exit = Node(LogicKind.EnterArea, 20, 600, ("area", "exit"));
        var all = Node(LogicKind.Compare, 240, 600, ("variable", "coins"), ("op", ">="), ("value", $"{coins.Length}"));
        var open = Node(LogicKind.SetState, 460, 560, ("object", "Chest"), ("state", "open"));
        var wait = Node(LogicKind.Wait, 680, 560, ("ms", "600"));
        var go = Node(LogicKind.LoadScene, 900, 560, ("scene", next.Id.ToString()));
        var nope = Node(LogicKind.PlaySound, 460, 680, ("sound", sounds["Hit"].Id.ToString()));
        Link(exit, all); Link(all, open, "true"); Link(open, wait); Link(wait, go); Link(all, nope, "false");
        var bump = Node(LogicKind.Touch, 20, 760, ("object", "Slime"));
        Link(bump, Node(LogicKind.PlaySound, 240, 760, ("sound", sounds["Hit"].Id.ToString())));
        LogicGraph.Save(g, F("levels/meadow.pxlogic"));
        s.LogicAssetId = Add("levels/meadow.pxlogic").Id;
        Scene.Save(s, F("levels/meadow.pxscene"));
        Add("levels/meadow.pxscene");
    }

    /// <summary>Level 2, “Cave”: dark, lit by torches, with a chest that opens when touched.</summary>
    static AssetEntry Cave(Func<string, string> F, Func<string, AssetEntry> Add, Dictionary<string, AssetEntry> art, AssetEntry hero,
        Dictionary<string, AssetEntry> sounds, AssetEntry music)
    {
        const int W = 24, H = 12, floor = 10;
        var map = Map(W, H, art["tiles"]);
        var tiles = map.Layers[0].Tiles;
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                if (y == 0 || y >= floor || x == 0 || x == W - 1 || (y == 1 && x % 5 == 2)) tiles[y * W + x] = y == floor ? Stone : Brick;
        for (int x = 9; x <= 12; x++) tiles[7 * W + x] = Plank;
        Tilemap.Save(map, F("levels/cave.pxm"));
        var mapAsset = Add("levels/cave.pxm");

        var s = new Scene { Width = W * T, Height = H * T, Level = "World 1", Order = 2, Background = 0xFF141020, MusicAssetId = music.Id, Ambient = 0xFF4A4060 };
        s.Layers.Insert(0, new SceneLayer { Name = "Rock", Kind = SceneLayerKind.Tilemap, AssetId = mapAsset.Id });
        var objects = s.Layers[^1];
        objects.Name = "Objects";
        int y0 = floor * T;
        s.Instances.Add(new SceneInstance { Name = "Hero", AssetId = hero.Id, LayerId = objects.Id, X = 2 * T, Y = y0, Animation = "idle", Z = 1 });
        s.Instances.Add(new SceneInstance { Name = "Chest", AssetId = art["chest"].Id, LayerId = objects.Id, X = 20 * T, Y = y0 - T, Animation = "closed" });
        s.Areas.Add(new SceneArea { Kind = AreaKind.Spawn, Name = "start", X = 2 * T, Y = y0 - 2 * T, W = T, H = 2 * T });
        foreach (int x in new[] { 4, 11, 19 })
        {
            var torch = SceneLight.Presets["Torch"]();
            (torch.Name, torch.X, torch.Y, torch.Radius) = ($"torch{x}", x * T + 8, (floor - 3) * T, 120);
            s.Lights.Add(torch);
            var fire = SceneEmitter.FromPreset(EmitterPreset.Fire);
            (fire.Name, fire.X, fire.Y, fire.Rate) = ($"flame{x}", x * T + 8, (floor - 3) * T + 4, 18);
            s.Emitters.Add(fire);
        }
        var glow = SceneEmitter.FromPreset(EmitterPreset.Magic);
        (glow.Name, glow.X, glow.Y) = ("treasure", 20 * T, y0 - 2 * T);
        s.Emitters.Add(glow);

        var g = new LogicGraph();
        var touch = LogicGraph.NewNode(LogicKind.Touch, "touchChest", 20, 20);
        touch.Params["object"] = "Chest";
        var open = LogicGraph.NewNode(LogicKind.SetState, "open", 240, 20);
        (open.Params["object"], open.Params["state"]) = ("Chest", "open");
        var ding = LogicGraph.NewNode(LogicKind.PlaySound, "ding", 460, 20);
        ding.Params["sound"] = sounds["Coin"].Id.ToString();
        var found = LogicGraph.NewNode(LogicKind.SetVariable, "found", 680, 20);
        (found.Params["variable"], found.Params["op"], found.Params["value"]) = ("treasure", "=", "1");
        g.Nodes.AddRange([touch, open, ding, found]);
        g.Links.AddRange([new LogicLink("touchChest", "out", "open"), new LogicLink("open", "out", "ding"), new LogicLink("ding", "out", "found")]);
        LogicGraph.Save(g, F("levels/cave.pxlogic"));
        s.LogicAssetId = Add("levels/cave.pxlogic").Id;
        Scene.Save(s, F("levels/cave.pxscene"));
        return Add("levels/cave.pxscene");
    }

    /// <summary>An empty map using the sample tileset: solid ground, stone and brick, one-way planks.</summary>
    static Tilemap Map(int w, int h, AssetEntry tilesAsset)
    {
        var tiles = SampleArt.Make("tiles");
        var map = new Tilemap(w, h, T) { Tileset = new Tileset(Compositor.Flatten(tiles, 0), tiles.Width, tiles.Height, T), TilesetAssetId = tilesAsset.Id };
        foreach (int t in new[] { Grass, Dirt, Stone, Brick }) map.TileCollisions[t] = new TileCollision(0, 0, T, T);
        map.TileCollisions[Plank] = new TileCollision(0, 0, T, 6, OneWay: true);
        return map;
    }

    const string Readme = """
        Pixelkiln sample game
        ======================

        Two small levels made only with Pixelkiln. Open things from the asset browser (double-click) and change them.

        art/           hand-drawn pixel art: hero (idle, run, jump), slime, coin, chest, items, tree, cloud, tiles
        characters/    the hero (Player) and the slime (Enemy) with their states and hitboxes
        levels/        meadow (level 1) and cave (level 2): tilemaps, scenes and logic graphs
        music/         two songs with drums and a soundtrack mix
        sounds/        coin, jump and hit sound effects
        ui/            the HUD shown over the game

        Try this:
        1. Open levels/meadow.pxscene and press F5. Arrows walk, Space jumps. Collect the five coins and walk into the chest.
        2. Open art/hero.pxf: each animation is a tag (idle, run, jump). Turn on the onion skin and step through the frames.
        3. Open music/sunny.pxs, press Space. Press 4 for the drum channel: each row is a drum. Try Pattern > Drum beat.
        4. Open levels/meadow.pxlogic (Scene > Logic) to see how a coin counts and how the exit checks for five coins.
        5. Help > Getting started has lessons for every part.
        """;
}
