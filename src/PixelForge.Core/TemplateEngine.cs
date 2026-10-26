using System.Globalization;

namespace PixelForge.Core;

public sealed record TemplateResult(List<AssetEntry> Created, AssetEntry Primary);

/// <summary>
/// Makes project assets from a template: fills the parameters, runs each step with the built-in generators (same seed,
/// same result), copies the files of saved templates and re-points their references to the new assets.
/// </summary>
public static class TemplateEngine
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Default values of the parameters, overridden by the given ones.</summary>
    public static Dictionary<string, string> Values(TemplateManifest m, IReadOnlyDictionary<string, string>? given = null)
    {
        var v = (m.Parameters ?? []).ToDictionary(p => p.Key, p => p.Default);
        foreach (var (k, val) in given ?? new Dictionary<string, string>()) if (v.ContainsKey(k)) v[k] = val;
        return v;
    }

    /// <summary>References a step uses only when that step was made (a level without player, pickups or music is still a level).</summary>
    static readonly HashSet<string> OptionalRefs = ["playerStep", "collectibleStep", "musicStep"];

    /// <summary>Steps to run: everything for a full kit; otherwise the core steps and the steps they need.</summary>
    public static List<TemplateStep> Plan(TemplateManifest m, bool fullKit)
    {
        if (fullKit) return m.Steps;
        var need = new HashSet<string>(m.Steps.Where(s => s.Core || s.Id == m.Primary).Select(s => s.Id));
        foreach (var s in Enumerable.Reverse(m.Steps))   // dependencies are always earlier steps
            if (need.Contains(s.Id))
                foreach (var (k, v) in s.Settings ?? []) if (k.EndsWith("Step") && !OptionalRefs.Contains(k)) need.Add(v);
        return [.. m.Steps.Where(s => need.Contains(s.Id))];
    }

    public static TemplateResult Create(TemplateInfo t, IReadOnlyDictionary<string, string>? given, Project p, string folder, bool fullKit)
    {
        var m = t.Manifest;
        var values = Values(m, given);
        string dir = Path.GetFullPath(Path.Combine(p.Root, folder));
        if (!dir.StartsWith(p.Root, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The folder must be inside the project.", nameof(folder));
        Directory.CreateDirectory(dir);
        var made = new Dictionary<string, AssetEntry>();
        var idMap = new Dictionary<Guid, Guid>();
        var ctx = new Ctx(p, dir, values, made);
        foreach (var s in Plan(m, fullKit))
        {
            var entry = ctx.Run(s, t.Folder);
            made[s.Id] = entry;
            if (s.OriginalId is Guid old) idMap[old] = entry.Id;
        }
        // copied files still point at the ids of the project they were saved from
        if (idMap.Count > 0)
            foreach (var a in made.Values)
                AssetKinds.Of(a.Type).Remap?.Invoke(p.FullPath(a), g => idMap.GetValueOrDefault(g, g));
        var primary = made.GetValueOrDefault(m.Primary) ?? made.Values.Last();
        return new TemplateResult([.. made.Values], primary);
    }

    sealed class Ctx(Project p, string dir, IReadOnlyDictionary<string, string> values, Dictionary<string, AssetEntry> made)
    {
        TemplateStep _s = null!;

        string S(string key, string fallback) => _s.Settings?.TryGetValue(key, out var v) == true ? TemplateLoader.Fill(v, values) : fallback;
        int I(string key, int fallback, int min, int max) =>
            int.TryParse(S(key, fallback.ToString(Inv)), NumberStyles.Integer, Inv, out int v) ? Math.Clamp(v, min, max)
                : throw new InvalidDataException($"Step “{_s.Id}”: {key} must be a whole number.");
        double D(string key, double fallback) =>
            double.TryParse(S(key, fallback.ToString(Inv)), NumberStyles.Float, Inv, out double v) ? v : throw new InvalidDataException($"Step “{_s.Id}”: {key} must be a number.");
        ulong Seed() => Rng.Seed(S("seed", _s.Id));
        AssetEntry Ref(string key) => made.TryGetValue(S(key, ""), out var a) ? a : throw new InvalidDataException($"Step “{_s.Id}”: {key} was not created (is it in the full kit only?).");
        /// <summary>Like <see cref="Ref"/>, but optional: null when the setting is absent or its step was not created.</summary>
        AssetEntry? Made(string key) => _s.Settings?.ContainsKey(key) == true && made.TryGetValue(S(key, ""), out var a) ? a : null;
        string Name(string fallback) => Safe(TemplateLoader.Fill(_s.Name ?? fallback, values));

        static string Safe(string name)
        {
            var bad = Path.GetInvalidFileNameChars();
            string n = new([.. name.Trim().Select(c => bad.Contains(c) ? '_' : c)]);
            return n.Length == 0 ? "asset" : n;
        }

        /// <summary>A free file name in the target folder (never overwrites).</summary>
        string FileFor(string name, string ext)
        {
            string f = Path.Combine(dir, name + ext);
            for (int n = 2; File.Exists(f); n++) f = Path.Combine(dir, $"{name} {n}{ext}");
            return f;
        }

        List<uint> Palette()
        {
            string choice = S("palette", "(default)");
            if (choice is "" or "(default)") return [.. Sprite.DefaultPalette];
            var a = p.Assets.FirstOrDefault(x => x.Type == AssetType.Palette && (x.Path == choice || x.Name == choice))
                    ?? throw new InvalidDataException($"Palette “{choice}” is not in the project.");
            return PaletteIO.Load(p.FullPath(a));
        }

        public AssetEntry Run(TemplateStep s, string? templateFolder)
        {
            _s = s;
            switch (s.Type)
            {
                case "sprite": return MakeSprite();
                case "art":
                {
                    string art = S("art", "");
                    if (!SampleArt.Names.Contains(art)) throw new InvalidDataException($"Step “{_s.Id}”: unknown art “{art}” (use {string.Join(", ", SampleArt.Names)}).");
                    string f = FileFor(Name(art), ".pxf");
                    ProjectFile.Save(SampleArt.Make(art), f);
                    return p.Add(f);
                }
                case "items": return MakeItems();
                case "character": return MakeCharacter();
                case "tileset": return MakeTileset();
                case "tilemap": return MakeTilemap();
                case "level": return MakeLevel();
                case "song":
                {
                    var song = Procedural.Song(Seed(), I("bpm", 120, 40, 300), S("scale", "major"), I("patterns", 4, 1, 16));
                    if (S("beat", "") is { Length: > 0 } beat)
                    {
                        if (!DrumBeats.Names.Contains(beat)) throw new InvalidDataException($"Step “{_s.Id}”: unknown beat “{beat}” (use {string.Join(", ", DrumBeats.Names)}).");
                        foreach (var pat in song.Patterns) DrumBeats.Apply(pat, beat);
                    }
                    if (S("drumsOnly", "no") == "yes")   // a beat to learn from or to build a song on
                        foreach (var pat in song.Patterns)
                            for (int ch = 0; ch < (int)Channel.Noise; ch++) Array.Fill(pat.Notes[ch], Pattern.Off);
                    string f = FileFor(Name("music"), ".pxs");
                    Core.Song.Save(song, f);
                    return p.Add(f);
                }
                case "ui":
                {
                    string styleName = S("style", "Fantasy");
                    if (!UiKit.Styles.TryGetValue(styleName, out var style)) throw new InvalidDataException($"Step “{_s.Id}”: unknown UI style “{styleName}” (use {string.Join(", ", UiKit.Styles.Keys)}).");
                    style = style with { Pixel = S("pixel", style.Pixel ? "true" : "false") == "true" };
                    var names = S("screens", string.Join(",", UiKit.ScreenNames)).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    var unknown = names.Where(n => !UiKit.ScreenNames.Contains(n)).ToList();
                    if (unknown.Count > 0) throw new InvalidDataException($"Step “{_s.Id}”: unknown screen(s) {string.Join(", ", unknown)}.");
                    var kit = UiKit.Starter(style, names, I("width", p.Settings.TargetWidth, 64, 4096), I("height", p.Settings.TargetHeight, 64, 4096));
                    string f = FileFor(Name("ui"), ".pxui");
                    UiKit.Save(kit, f);
                    return p.Add(f);
                }
                case "texture":
                {
                    if (!Enum.TryParse<Material>(S("material", "Stone"), true, out var mat)) throw new InvalidDataException($"Step “{_s.Id}”: unknown material.");
                    if (!ColorUtil.TryParseHex(S("dark", "3A3A44").TrimStart('#'), out uint dark) || !ColorUtil.TryParseHex(S("light", "A8A8B4").TrimStart('#'), out uint light))
                        throw new InvalidDataException($"Step “{_s.Id}”: dark and light must be RRGGBB.");
                    var tex = new TextureDoc
                    {
                        Material = mat, Seed = S("seed", "1"), Size = I("size", 256, 4, 2048), Dark = dark | 0xFF000000, Light = light | 0xFF000000,
                        Scale = D("scale", 6), Detail = I("detail", 4, 1, 6), NormalStrength = D("normalStrength", 2),
                    };
                    string f = FileFor(Name("texture"), ".pxtex");
                    TextureDoc.Save(tex, f);
                    return p.Add(f);
                }
                case "file":
                {
                    string src = Path.Combine(templateFolder!, s.File!);
                    string f = FileFor(Name(Path.GetFileNameWithoutExtension(s.File!)), Path.GetExtension(s.File!));
                    File.Copy(src, f);
                    return p.Add(f);
                }
                default: throw new InvalidDataException($"Unknown step type “{s.Type}”.");
            }
        }

        /// <summary>A generated base look; every state gets its own tag with copies of it to draw over.</summary>
        AssetEntry MakeSprite()
        {
            int w = I("width", 32, 4, 512), h = I("height", w, 4, 512);
            var sprite = new Sprite(w, h);
            sprite.Palette.Clear();
            sprite.Palette.AddRange(Palette());
            var look = Procedural.Sprite(Seed(), w, h, sprite.Palette, D("density", 0.45));
            Array.Copy(look, sprite.Cel(0, 0), look.Length);
            var states = _s.States is { Count: > 0 } list ? list : [new TemplateState("idle")];
            int frame = 0;
            foreach (var st in states)
            {
                for (int k = 0; k < Math.Max(1, st.Frames); k++, frame++)
                    if (frame > 0) sprite.AddFrame(frame, copyFrom: 0);
                sprite.Tags.Add(new FrameTag { Name = st.Name, From = frame - Math.Max(1, st.Frames), To = frame - 1 });
            }
            string f = FileFor(Name("sprite"), ".pxf");
            ProjectFile.Save(sprite, f);
            return p.Add(f);
        }

        /// <summary>Several small generated sprites (coins, gems, icons …); returns the first, all are added to the project.</summary>
        AssetEntry MakeItems()
        {
            int size = I("size", 16, 4, 256);
            var names = S("names", "item").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var pal = Palette();
            AssetEntry? first = null;
            ulong seed = Seed();
            foreach (var (n, i) in names.Select((n, i) => (n, i)))
            {
                var sprite = new Sprite(size, size);
                sprite.Palette.Clear();
                sprite.Palette.AddRange(pal);
                var px = Procedural.Sprite(seed + (ulong)i * 104729, size, size, pal, 0.55);
                Array.Copy(px, sprite.Cel(0, 0), px.Length);
                string f = FileFor(Safe(n), ".pxf");
                ProjectFile.Save(sprite, f);
                var a = p.Add(f);
                first ??= a;
                if (i > 0) made[$"{_s.Id}#{i}"] = a;   // the rest are reported as created too
            }
            return first!;
        }

        AssetEntry MakeCharacter()
        {
            var spriteAsset = Ref("spriteStep");
            var sprite = ProjectFile.Load(p.FullPath(spriteAsset));
            int w = sprite.Width, h = sprite.Height;
            var c = new Character
            {
                Kind = Enum.Parse<CharacterKind>(S("kind", "Player"), true), Facing = Enum.Parse<Facing>(S("facing", "Right"), true),
                SpriteAssetId = spriteAsset.Id,
            };
            (c.OriginX, c.OriginY) = S("origin", "feet") == "center" ? (w / 2, h / 2) : (w / 2, h);
            foreach (var tag in sprite.Tags) c.Animations[tag.Name] = tag.Name;
            foreach (var b in _s.Boxes ?? [])
            {
                var (x, y, bw, bh) = Rect(b.Rect, w, h);
                c.Boxes.Add(new CharacterBox { Kind = b.Kind, State = b.State, X = x, Y = y, W = bw, H = bh, Category = b.Category });
            }
            foreach (var (k, v) in _s.Properties ?? []) c.Properties[k] = TemplateLoader.Fill(v, values);
            string f = FileFor(Name("character"), ".pxchar");
            Core.Character.Save(c, f);
            return p.Add(f);
        }

        (int, int, int, int) Rect(string rect, int w, int h)
        {
            (double x, double y, double rw, double rh) = rect switch
            {
                "full" => (0, 0, 1, 1),
                "body" => (0.2, 0.15, 0.6, 0.85),
                "feet" => (0.3, 0.1, 0.4, 0.9),
                "head" => (0.25, 0, 0.5, 0.35),
                "front" => (0.6, 0.3, 0.4, 0.4),
                _ => rect.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => double.Parse(v, Inv)).ToArray() is [var a, var b, var c, var d]
                    ? (a, b, c, d) : throw new InvalidDataException($"Box “{rect}”: use full, body, feet, head, front or four fractions."),
            };
            int bx = (int)(x * w), by = (int)(y * h);
            return (bx, by, Math.Max(1, Math.Min(w - bx, (int)Math.Round(rw * w))), Math.Max(1, Math.Min(h - by, (int)Math.Round(rh * h))));
        }

        /// <summary>A generated seamless tileset, or an existing project picture when the "asset" setting names one.</summary>
        AssetEntry MakeTileset()
        {
            string asset = S("asset", "(generate)");
            if (asset is not ("" or "(generate)"))
                return p.Assets.FirstOrDefault(x => AssetLoader.IsPicture(x.Type) && (x.Path == asset || x.Name == asset))
                       ?? throw new InvalidDataException($"Tileset “{asset}” is not in the project.");
            if (!ColorUtil.TryParseHex(S("baseColor", "4A7A3A").TrimStart('#'), out uint baseColor)) throw new InvalidDataException($"Step “{_s.Id}”: baseColor must be RRGGBB.");
            var (px, w, h) = Procedural.Tiles(Seed(), I("tileSize", 16, 4, 128), PaletteTools.Ramp(baseColor | 0xFF000000),
                Enum.Parse<TilePattern>(S("pattern", "Noise"), true), I("variants", 4, 1, 16));
            string f = FileFor(Name("tiles"), ".png");
            ImageIO.SavePng(f, px, w, h);
            return p.Add(f);
        }

        AssetEntry MakeTilemap()
        {
            var tsAsset = Ref("tilesetStep");
            var (tpx, tw, th) = AssetLoader.LoadImage(p.FullPath(tsAsset));
            // a generated tileset is one row of square tiles; another picture needs an explicit tileSize (16 if not given)
            int tile = _s.Settings?.ContainsKey("tileSize") == true ? I("tileSize", 16, 4, 128) : th <= 128 && tw % th == 0 ? th : 16;
            int w = I("width", 40, 8, 1024), h = I("height", 15, 6, 1024);
            var m = new Tilemap(w, h, tile) { Tileset = new Tileset(tpx, tw, th, tile), TilesetAssetId = tsAsset.Id };
            ulong seed = Seed();
            int difficulty = I("difficulty", 2, 1, 5);
            var solid = S("layout", "Platforms") switch
            {
                "Cave" => Procedural.Cave(seed, w, h, D("fill", 0.45)),
                "Rooms" => Procedural.Rooms(seed, w, h),
                "Flat" => Procedural.Platforms(seed, w, h, maxGap: 0),
                _ => Procedural.Platforms(seed, w, h, maxGap: 1 + difficulty / 2),
            };
            var rng = new Rng(seed ^ 0xA5A5);
            int count = m.Tileset.Count;
            for (int i = 0; i < solid.Length; i++) m.Layers[0].Tiles[i] = solid[i] ? rng.Next(count) : -1;   // variants for variety
            for (int t = 0; t < count; t++) m.TileCollisions[t] = new TileCollision(0, 0, tile, tile);
            string f = FileFor(Name("map"), ".pxm");
            Core.Tilemap.Save(m, f);
            return p.Add(f);
        }

        AssetEntry MakeLevel()
        {
            var mapAsset = Ref("mapStep");
            var map = Core.Tilemap.Load(p.FullPath(mapAsset));
            var solid = map.Layers[0].Tiles.Select(t => t >= 0).ToArray();
            var plan = LevelBuilder.Place(solid, map.Width, map.Height, Seed(), I("checkpoints", 2, 0, 20), I("hazards", 3, 0, 50), I("collectibles", 8, 0, 200),
                S("genre", "platformer") == "platformer");
            int ts = map.TileSize;
            var scene = new Scene { Width = map.Width * ts, Height = map.Height * ts, Level = Name("level") };
            if (ColorUtil.TryParseHex(S("background", "1E2A3A").TrimStart('#'), out uint bg)) scene.Background = bg | 0xFF000000;
            scene.Layers.Insert(0, new SceneLayer { Name = "Map", Kind = SceneLayerKind.Tilemap, AssetId = mapAsset.Id });
            var objects = scene.Layers[^1];
            var (sx, sy) = plan.Spawn;
            scene.Areas.Add(new SceneArea { Kind = AreaKind.Spawn, Name = "start", X = sx * ts, Y = sy * ts, W = ts, H = ts });
            foreach (var (cx, cy, n) in plan.Checkpoints.Select((c, n) => (c.X, c.Y, n + 1)))
                scene.Areas.Add(new SceneArea { Kind = AreaKind.Checkpoint, Name = $"checkpoint {n}", X = cx * ts, Y = (cy - 1) * ts, W = ts, H = ts * 2 });
            foreach (var (hx, hy, n) in plan.Hazards.Select((c, n) => (c.X, c.Y, n + 1)))
                scene.Areas.Add(new SceneArea { Kind = AreaKind.Kill, Name = $"hazard {n}", X = hx * ts, Y = hy * ts, W = ts, H = ts });
            if (Made("playerStep") is { } player)
                scene.Instances.Add(new SceneInstance { Name = "Player", AssetId = player.Id, LayerId = objects.Id, X = sx * ts + ts / 2, Y = (sy + 1) * ts });
            if (Made("collectibleStep") is { } item)
                foreach (var (ix, iy, n) in plan.Collectibles.Select((c, n) => (c.X, c.Y, n + 1)))
                    scene.Instances.Add(new SceneInstance { Name = $"item {n}", AssetId = item.Id, LayerId = objects.Id, X = ix * ts, Y = iy * ts, Properties = { ["collect"] = "1" } });
            if (Made("musicStep") is { } music) scene.MusicAssetId = music.Id;
            // lighting and weather: a dim ambient, torches at the start and the checkpoints, rain or snow over the level
            if (_s.Settings?.ContainsKey("ambient") == true && ColorUtil.TryParseHex(S("ambient", "FFFFFF").TrimStart('#'), out uint ambient)) scene.Ambient = ambient | 0xFF000000;
            if (S("torches", "none") == "checkpoints")
                foreach (var (tx, ty) in plan.Checkpoints.Prepend(plan.Spawn))
                {
                    var torch = SceneLight.Presets["Torch"]();
                    (torch.X, torch.Y) = (tx * ts + ts / 2, (ty - 1) * ts + ts / 2);
                    scene.Lights.Add(torch);
                }
            if (Enum.TryParse<EmitterPreset>(S("weather", ""), out var weather) && weather is EmitterPreset.Rain or EmitterPreset.Snow)
            {
                var sky = SceneEmitter.FromPreset(weather);
                (sky.X, sky.Y, sky.W, sky.Rate) = (0, -8, scene.Width, sky.Rate * Math.Max(1, scene.Width / 320.0));
                sky.Rate = Math.Min(2000, sky.Rate);
                scene.Emitters.Add(sky);
            }
            scene.CameraX = Math.Clamp(sx * ts - p.Settings.TargetWidth / 2, 0, Math.Max(0, scene.Width - p.Settings.TargetWidth));
            scene.CameraY = Math.Clamp(sy * ts - p.Settings.TargetHeight / 2, 0, Math.Max(0, scene.Height - p.Settings.TargetHeight));
            string f = FileFor(Name("level"), ".pxscene");
            Scene.Save(scene, f);
            return p.Add(f);
        }
    }
}

/// <summary>Where things go in a generated layout (cells): a start, checkpoints along the way, hazards and pickups.</summary>
public static class LevelBuilder
{
    public sealed record Placement((int X, int Y) Spawn, List<(int X, int Y)> Checkpoints, List<(int X, int Y)> Hazards, List<(int X, int Y)> Collectibles);

    /// <summary>
    /// Side view: spawn and checkpoints stand on ground, hazards sit at the bottom of gaps, pickups float above ground.
    /// Top view: everything goes on free cells. Deterministic for a seed.
    /// </summary>
    public static Placement Place(bool[] solid, int w, int h, ulong seed, int checkpoints, int hazards, int collectibles, bool sideView)
    {
        bool S(int x, int y) => !Tools.InBounds(w, h, x, y) || solid[y * w + x];
        bool Stand(int x, int y) => !S(x, y) && !S(x, y - 1) && S(x, y + 1);   // two free cells over ground
        var free = new List<(int X, int Y)>();
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
                if (sideView ? Stand(x, y) : !S(x, y)) free.Add((x, y));
        if (free.Count == 0) throw new InvalidDataException("The layout has no free place to start; try another seed.");
        var rng = new Rng(seed ^ 0x5EED);
        var spawn = free.OrderBy(c => c.X).ThenBy(c => c.Y).First();

        // checkpoints: evenly spread from left to right
        var cps = new List<(int, int)>();
        for (int k = 1; k <= checkpoints; k++)
        {
            int target = w * k / (checkpoints + 1);
            if (free.Where(c => c.X > spawn.X + 2).OrderBy(c => Math.Abs(c.X - target)).ThenBy(c => c.Y).FirstOrDefault() is { X: > 0 } c && !cps.Contains(c)) cps.Add(c);
        }

        var used = new HashSet<(int, int)>(cps) { spawn };
        var hz = new List<(int, int)>();
        if (sideView)
        {
            // gaps: columns with no ground in the bottom row get a hazard there
            for (int x = 2; x < w - 2 && hz.Count < hazards; x++)
                if (!S(x, h - 1) && S(x - 1, h - 1)) hz.Add((x, h - 1));
        }
        var others = free.Where(c => !used.Contains(c) && Math.Abs(c.X - spawn.X) > 3).OrderBy(_ => rng.NextULong()).ToList();
        foreach (var c in others.Where(c => !sideView || S(c.X, c.Y + 1)))
        {
            if (hz.Count >= hazards) break;
            hz.Add(c);
            used.Add(c);
        }
        var items = new List<(int, int)>();
        foreach (var c in others.Where(c => !used.Contains(c)))
        {
            if (items.Count >= collectibles) break;
            items.Add(sideView ? (c.X, c.Y - 1) : c);
            used.Add(c);
        }
        return new Placement(spawn, cps, hz, items);
    }
}
