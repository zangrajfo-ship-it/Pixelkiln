using PixelForge.Core;

namespace PixelForge.Core.Tests;

public sealed class SceneTests() : TempDirTest("scene")
{
    [Fact]
    public void Round_trip_keeps_layers_instances_areas_and_tracks()
    {
        var s = new Scene { Width = 800, Height = 240, Level = "World 1", Order = 2, CameraX = 10, MusicAssetId = Guid.NewGuid() };
        var bg = new SceneLayer { Name = "Sky", Kind = SceneLayerKind.Image, AssetId = Guid.NewGuid(), ParallaxX = 0.25, RepeatX = true };
        s.Layers.Insert(0, bg);
        var inst = new SceneInstance { Name = "Platform", AssetId = Guid.NewGuid(), LayerId = s.Layers[1].Id, X = 50, Y = 100, FlipX = true, Z = 3 };
        inst.Track = new KeyframeTrack { Keys = [new Keyframe(0), new Keyframe(2000, X: 64, Easing: Easing.EaseInOut)] };
        s.Instances.Add(inst);
        s.Areas.Add(new SceneArea { Kind = AreaKind.Checkpoint, Name = "cp1", X = 300, Y = 100, W = 8, H = 32, Properties = { ["id"] = "1" } });

        var t = Scene.Deserialize(Scene.Serialize(s));
        Assert.Equal((800, 240, "World 1", 2, 10), (t.Width, t.Height, t.Level, t.Order, t.CameraX));
        Assert.Equal(0.25, t.Layers[0].ParallaxX);
        Assert.True(t.Layers[0].RepeatX);
        var ti = t.Instances.Single();
        Assert.Equal((50, 100, true, 3), (ti.X, ti.Y, ti.FlipX, ti.Z));
        Assert.Equal(64, ti.Track!.Keys[1].X);
        Assert.Equal(Easing.EaseInOut, ti.Track.Keys[1].Easing);
        Assert.Equal("1", t.Areas[0].Properties["id"]);
        Assert.Contains("\"Checkpoint\"", Scene.Serialize(s));
        Assert.Equal(3, s.ReferencedAssets().Count());   // image, instance asset, music
    }

    [Fact]
    public void Resize_keeps_objects_and_bad_files_are_rejected()
    {
        var s = new Scene();
        s.Instances.Add(new SceneInstance { X = 600, Y = 300, LayerId = s.Layers[0].Id });
        s.Resize(320, 180);
        Assert.Equal(600, s.Instances[0].X);                     // not moved or deleted; validation reports it
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Resize(4, 4));

        var dupe = Scene.Serialize(s).Replace(s.Instances[0].Id.ToString(), s.Layers[0].Id.ToString());
        Assert.Throws<InvalidDataException>(() => Scene.Deserialize(dupe));
        Assert.Throws<InvalidDataException>(() => Scene.Deserialize("""{"Version":1,"Width":100,"Height":100,"Layers":[]}"""));
    }

    [Fact]
    public void Check_reports_missing_assets_outside_objects_and_spawn_in_wall()
    {
        var p = Project.Create(_dir, "G");
        var map = new Tilemap(4, 4, 8) { Tileset = new Tileset(new uint[16 * 8], 16, 8, 8) };
        Tools.Fill(map.Layers[0].Tiles, 4, 4, 0, 0, 1);
        map.TileCollisions[1] = new TileCollision(0, 0, 8, 8);
        Tilemap.Save(map, P("ground.pxm"));
        var ground = p.Add(P("ground.pxm"));

        var hero = new Character { Kind = CharacterKind.Player, OriginX = 4, OriginY = 8 };
        hero.Animations["idle"] = "idle";
        hero.Boxes.Add(new CharacterBox { Kind = BoxKind.Solid, X = 2, Y = 0, W = 4, H = 8 });
        ProjectFile.Save(new Sprite(8, 8), P("hero.pxf"));
        hero.SpriteAssetId = p.Add(P("hero.pxf")).Id;
        Character.Save(hero, P("hero.pxchar"));
        var heroAsset = p.Add(P("hero.pxchar"));

        var s = new Scene { Width = 64, Height = 64 };
        s.Layers.Insert(0, new SceneLayer { Name = "Ground", Kind = SceneLayerKind.Tilemap, AssetId = ground.Id });
        s.Instances.Add(new SceneInstance { Name = "Hero", AssetId = heroAsset.Id, LayerId = s.Layers[1].Id, X = 10, Y = 10 });     // inside solid tiles
        s.Instances.Add(new SceneInstance { Name = "Lost", AssetId = Guid.NewGuid(), LayerId = s.Layers[1].Id, X = 100, Y = 5 });
        s.Areas.Add(new SceneArea { Kind = AreaKind.Spawn, Name = "start", X = 0, Y = 0, W = 4, H = 4 });

        var msgs = SceneCheck.Run(s, p).Select(i => i.Message).ToList();
        Assert.Contains(msgs, m => m.Contains("“Lost” refers to a missing asset"));
        Assert.Contains(msgs, m => m.Contains("“Lost” is outside the scene"));
        Assert.Contains(msgs, m => m.Contains("Spawn “start” overlaps a solid tile"));
        Assert.Contains(msgs, m => m.Contains("“Hero” starts inside a solid tile"));
        Assert.Equal(16, SceneCheck.SolidCells(s, p).Count);

        Scene.Save(s, P("level1.pxscene"));
        var level = p.Add(P("level1.pxscene"));
        Assert.Contains(level.Id, p.UsersOf(heroAsset.Id).Select(a => a.Id));   // back-reference through the scene
    }
}

public sealed class SceneAssetsTests() : TempDirTest("sa")
{
    [Fact]
    public void Frame_at_time_uses_durations_and_loops()
    {
        var s = new Sprite(1, 1);
        s.AddFrame(1);
        s.AddFrame(2);
        (s.Frames[0].DurationMs, s.Frames[1].DurationMs, s.Frames[2].DurationMs) = (100, 300, 100);
        Assert.Equal(0, SceneAssets.FrameAt(s, 0, 2, 50));
        Assert.Equal(1, SceneAssets.FrameAt(s, 0, 2, 150));
        Assert.Equal(2, SceneAssets.FrameAt(s, 0, 2, 450));
        Assert.Equal(1, SceneAssets.FrameAt(s, 0, 2, 650));   // looped
        Assert.Equal(1, SceneAssets.FrameAt(s, 1, 1, 12345));
    }

    [Fact]
    public void Look_resolves_sprites_characters_and_follows_edits()
    {
        var p = Project.Create(_dir, "G");
        var s = new Sprite(8, 8);
        s.AddFrame(1);
        s.AddFrame(2);
        s.Tags.Add(new FrameTag { Name = "run", From = 1, To = 2 });
        string sp = P("hero.pxf");
        ProjectFile.Save(s, sp);
        var spriteAsset = p.Add(sp);
        var c = new Character { SpriteAssetId = spriteAsset.Id, OriginX = 4, OriginY = 8 };
        c.Animations["run"] = "run";
        Character.Save(c, P("hero.pxchar"));
        var charAsset = p.Add(P("hero.pxchar"));

        var assets = new SceneAssets(p);
        var look = assets.Look(new SceneInstance { AssetId = charAsset.Id })!;
        Assert.Equal((4, 8, 1, 2), (look.OriginX, look.OriginY, look.From, look.To));
        var plain = assets.Look(new SceneInstance { AssetId = spriteAsset.Id, Animation = "run" })!;
        Assert.Equal((0, 0, 1, 2), (plain.OriginX, plain.OriginY, plain.From, plain.To));
        Assert.Null(assets.Look(new SceneInstance { AssetId = Guid.NewGuid() }));

        s.Tags[0].To = 1;                                     // edited elsewhere: new write time, new result
        ProjectFile.Save(s, sp);
        File.SetLastWriteTimeUtc(sp, DateTime.UtcNow.AddSeconds(5));
        Assert.Equal(1, assets.Look(new SceneInstance { AssetId = charAsset.Id })!.To);
    }
}
