using System.IO.Compression;
using System.Text.Json;
using Pixelkiln.Core;

namespace Pixelkiln.Core.Tests;

public sealed class ExportTests() : TempDirTest("export")
{
    /// <summary>Small game: sprite with tags, map with collisions, player character, parallax sky, music mix with a loop, one level.</summary>
    Project Game()
    {
        var p = Project.Create(_dir, "Demo");
        Directory.CreateDirectory(P("chars"));
        var s = new Sprite(8, 8);
        s.AddFrame(1);
        s.AddFrame(2);
        (s.Frames[0].DurationMs, s.Frames[1].DurationMs, s.Frames[2].DurationMs) = (100, 200, 100);
        s.Tags.Add(new FrameTag { Name = "idle", From = 0, To = 0 });
        s.Tags.Add(new FrameTag { Name = "run", From = 1, To = 2 });
        ProjectFile.Save(s, P("chars/hero.pxf"));
        var sprite = p.Add(P("chars/hero.pxf"));

        var c = new Character { Kind = CharacterKind.Player, SpriteAssetId = sprite.Id, OriginX = 4, OriginY = 8, Properties = { ["speed"] = "120" } };
        c.Animations["idle"] = "idle";
        c.Animations["run"] = "run";
        c.Boxes.Add(new CharacterBox { Kind = BoxKind.Solid, X = 2, Y = 0, W = 4, H = 8, Category = 1 });
        c.Boxes.Add(new CharacterBox { Kind = BoxKind.Hurt, X = 1, Y = 1, W = 6, H = 7 });
        Character.Save(c, P("chars/hero.pxchar"));
        var hero = p.Add(P("chars/hero.pxchar"));

        var map = new Tilemap(4, 2, 8) { Tileset = new Tileset(Enumerable.Repeat(0xFF336699u, 16 * 8).ToArray(), 16, 8, 8) };
        Array.Fill(map.Layers[0].Tiles, -1);
        map.Layers[0].Tiles[4] = 1;                                   // cell (0,1) uses atlas tile (1,0)
        map.TileCollisions[1] = new TileCollision(0, 0, 8, 8);
        map.TileCollisions[0] = new TileCollision(0, 0, 8, 2, OneWay: true, Category: 2);
        map.Objects.Add(new MapObject { Id = 1, Name = "door", Type = "exit", X = 8, Y = 0, W = 8, H = 8 });
        Tilemap.Save(map, P("ground.pxm"));
        var ground = p.Add(P("ground.pxm"));

        ImageIO.SavePng(P("sky.png"), Enumerable.Repeat(0xFF8899FFu, 32 * 4).ToArray(), 32, 4);
        var sky = p.Add(P("sky.png"));
        WavWriter.Save(P("tune.wav"), Enumerable.Repeat(0.25f, 4410).ToArray());
        var tune = p.Add(P("tune.wav"));
        var mix = new Mix { LoopStartMs = 0, LoopEndMs = 100 };
        mix.Tracks[0].Clips.Add(new MixClip { AssetId = tune.Id });
        Mix.Save(mix, P("music.pxmix"));
        var music = p.Add(P("music.pxmix"));
        p.Settings.Playlists.Add(new Playlist { Name = "World", Items = [music.Id] });
        p.Settings.CollisionMasks[1] = 0b101;

        var sc = new Scene { Width = 64, Height = 32, Level = "World 1", Order = 1, MusicAssetId = music.Id };
        sc.Layers.Insert(0, new SceneLayer { Name = "Sky", Kind = SceneLayerKind.Image, AssetId = sky.Id, ParallaxX = 0.5, RepeatX = true });
        sc.Layers.Insert(1, new SceneLayer { Name = "Ground", Kind = SceneLayerKind.Tilemap, AssetId = ground.Id });
        sc.Instances.Add(new SceneInstance { Name = "Hero", AssetId = hero.Id, LayerId = sc.Layers[2].Id, X = 12, Y = 8, FlipX = true });
        sc.Instances.Add(new SceneInstance
        {
            Name = "Bird", AssetId = sprite.Id, LayerId = sc.Layers[2].Id, X = 40, Y = 4, Animation = "run",
            Track = new KeyframeTrack { Keys = [new Keyframe(0), new Keyframe(1000, X: 16, Opacity: 0.5, Easing: Easing.EaseInOut)] },
        });
        sc.Areas.Add(new SceneArea { Kind = AreaKind.Spawn, Name = "start", X = 8, Y = 0, W = 8, H = 8 });
        Scene.Save(sc, P("level1.pxscene"));
        p.Add(P("level1.pxscene"));
        p.Save();
        return p;
    }

    [Fact]
    public void Godot_export_writes_sprite_frames_tileset_character_and_level()
    {
        var p = Game();
        var r = Exporter.Plan(p, new ExportProfile());
        Assert.True(r.Ok, string.Join("\n", r.Errors));

        var frames = r.Text("chars/hero.tres");
        Assert.StartsWith("[gd_resource type=\"SpriteFrames\" load_steps=5 format=3]", frames);
        Assert.Contains("[ext_resource type=\"Texture2D\" path=\"res://assets/chars/hero.png\" id=\"1\"]", frames);
        Assert.Contains("\"name\": &\"run\",\n\"speed\": 10.0", frames);         // shortest frame 100 ms → 10 fps
        Assert.Contains("\"duration\": 2.0", frames);                           // the 200 ms frame

        var tiles = r.Text("ground_tileset.tres");
        Assert.Contains("1:0/0/physics_layer_0/polygon_0/points = PackedVector2Array(-4.0, -4.0, 4.0, -4.0, 4.0, 4.0, -4.0, 4.0)", tiles);
        Assert.Contains("0:0/0/physics_layer_1/polygon_0/one_way = true", tiles);
        Assert.Contains("physics_layer_0/collision_layer = 1", tiles);          // category 0
        Assert.Contains("physics_layer_1/collision_layer = 4", tiles);          // category 2
        // cell (x 0, y 1) with atlas tile 3 of a 2-column atlas = atlas (1, 1)
        Assert.Equal([1 << 16, 1 << 16, 1], GodotExport.CellData(new MapLayer { Tiles = [-1, -1, -1, -1, 3, -1, -1, -1] }, 4, 2));
        Assert.Contains("[node name=\"door\" type=\"Area2D\" parent=\".\"]", r.Text("ground.tscn"));

        var ch = r.Text("chars/hero_character.tscn");
        Assert.Contains("type=\"CharacterBody2D\"", ch);
        Assert.Contains("collision_layer = 2\ncollision_mask = 5", ch);
        Assert.Contains("offset = Vector2(-4.0, -8.0)", ch);
        Assert.Contains("metadata/speed = \"120\"", ch);
        Assert.Contains("\"kind\": \"Hurt\"", ch);

        var level = r.Text("level1.tscn");
        Assert.Contains("type=\"Parallax2D\"", level);
        Assert.Contains("scroll_scale = Vector2(0.5, 1.0)", level);
        Assert.Contains("repeat_size = Vector2(32.0, 0.0)", level);
        Assert.Contains("instance=ExtResource(", level);
        Assert.Contains("scale = Vector2(-1.0, 1.0)", level);                   // flipped character
        Assert.Contains("groups=[\"spawn\"]", level);
        Assert.Contains("type=\"AnimationPlayer\" parent=\"Objects/Bird\"", level);
        Assert.Contains("PackedFloat32Array(1.0, -2.0)", level);                 // linear then ease-in-out
        Assert.Contains("Color(1, 1, 1, 0.5)", level);
        Assert.Contains("limit_right = 64", level);
        Assert.Contains("path=\"res://assets/music.wav\"", level);
        Assert.Contains(r.Warnings, w => w.Contains("music.wav") && w.Contains("Loop Mode"));

        var proj = JsonDocument.Parse(r.Files["project.json"]).RootElement;
        Assert.Equal("music.wav", proj.GetProperty("playlists")[0].GetProperty("items")[0].GetString());
        Assert.Contains("export-report.txt", r.Files.Keys);
    }

    [Fact]
    public void Generic_export_keeps_the_public_formats()
    {
        var p = Game();
        var r = Exporter.Plan(p, new ExportProfile { Name = "Generic", Target = ExportTarget.Generic, OutputFolder = "export/generic" });
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal("hero.png", JsonDocument.Parse(r.Files["chars/hero.json"]).RootElement.GetProperty("meta").GetProperty("image").GetString());
        Assert.Equal("ground_tiles.png", JsonDocument.Parse(r.Files["ground.tmj"]).RootElement.GetProperty("tilesets")[0].GetProperty("image").GetString());
        Assert.Equal("hero.png", JsonDocument.Parse(r.Files["chars/hero_character.json"]).RootElement.GetProperty("sheet").GetString());
        var scene = JsonDocument.Parse(r.Files["level1_scene.json"]).RootElement;
        var objects = scene.GetProperty("layers")[2].GetProperty("instances").EnumerateArray().ToDictionary(i => i.GetProperty("name").GetString()!, i => i.GetProperty("file").GetString());
        Assert.Equal("chars/hero_character.json", objects["Hero"]);
        Assert.Equal("chars/hero.json", objects["Bird"]);
        Assert.Equal("music.wav", scene.GetProperty("music").GetString());
    }

    [Fact]
    public void Export_is_deterministic_and_replaces_the_folder_in_one_step()
    {
        var p = Game();
        var prof = new ExportProfile { Zip = true };
        var a = Exporter.Run(p, prof);
        Assert.True(a.Ok, string.Join("\n", a.Errors));
        string outDir = Path.Combine(p.Root, "export", "godot");
        File.WriteAllText(Path.Combine(outDir, "stale.txt"), "old");
        byte[] zip1 = File.ReadAllBytes(outDir + ".zip");

        var b = Exporter.Run(p, prof);
        Assert.Equal(a.Files.Keys, b.Files.Keys);
        foreach (var k in a.Files.Keys) Assert.Equal(a.Files[k], b.Files[k]);
        Assert.Equal(zip1, File.ReadAllBytes(outDir + ".zip"));
        Assert.False(File.Exists(Path.Combine(outDir, "stale.txt")));           // the folder is replaced, not merged
        Assert.False(Directory.Exists(outDir + ".staging"));
        Assert.True(File.Exists(Path.Combine(outDir, "chars", "hero.tres")));
        using var z = ZipFile.OpenRead(outDir + ".zip");
        Assert.Equal(a.Files.Keys.Order(StringComparer.Ordinal), z.Entries.Select(e => e.FullName));
    }

    [Fact]
    public void Errors_stop_the_export_and_keep_the_previous_one()
    {
        var p = Game();
        Assert.True(Exporter.Run(p, new ExportProfile()).Ok);
        var level = p.Assets.First(a => a.Type == AssetType.Scene);
        var sc = Scene.Load(p.FullPath(level));
        sc.Instances.Add(new SceneInstance { Name = "Ghost", AssetId = Guid.NewGuid(), LayerId = sc.Layers[^1].Id });
        Scene.Save(sc, p.FullPath(level));

        var r = Exporter.Run(p, new ExportProfile());
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Contains("Unknown asset"));
        Assert.True(File.Exists(Path.Combine(p.Root, "export", "godot", "level1.tscn")));   // previous export untouched

        Assert.Contains(Exporter.Plan(p, new ExportProfile { OutputFolder = "../outside" }).Errors, e => e.Contains("inside the project"));
        Assert.Contains(Exporter.Plan(p, new ExportProfile { OutputFolder = "chars" }).Errors, e => e.Contains("contains project assets"));
    }

    [Fact]
    public void Cancelled_export_writes_nothing_and_reports_progress()
    {
        var p = Game();
        var seen = new List<string>();
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(m => { seen.Add(m); if (seen.Count == 2) cts.Cancel(); });
        Assert.Throws<OperationCanceledException>(() => Exporter.Run(p, new ExportProfile(), progress, cts.Token));
        Assert.Equal(2, seen.Count);
        Assert.False(Directory.Exists(Path.Combine(p.Root, "export", "godot")));
    }

    /// <summary>Progress&lt;T&gt; posts to a sync context; tests need the call to happen right away.</summary>
    sealed class SyncProgress(Action<string> on) : IProgress<string> { public void Report(string value) => on(value); }

    [Fact]
    public void Selected_assets_bring_their_dependencies()
    {
        var p = Game();
        var hero = p.Assets.First(a => a.Type == AssetType.Character);
        var r = Exporter.Plan(p, new ExportProfile { Assets = [hero.Id] });
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal(["chars/hero.png", "chars/hero.tres", "chars/hero_character.tscn", "export-report.txt", "project.json"], r.Files.Keys);
    }
}
