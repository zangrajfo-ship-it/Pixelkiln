using PixelForge.Core;

namespace PixelForge.Core.Tests;

/// <summary>
/// Golden files in tests/PixelForge.Core.Tests/Fixtures: every file version PixelForge ever wrote must
/// keep loading with the same content. v1 files are hand-written in the old shape; v2 files were
/// written by the code at commit 9144477 (regenerate only on purpose, see <see cref="WriteV2Fixtures"/>).
/// </summary>
public class FormatRegressionTests
{
    const uint R = 0xFFFF0000, B = 0xFF0000FF;

    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void Sprite_v1()
    {
        var s = ProjectFile.Load(Fixture("sprite.v1.pxf"));
        Assert.Equal((2, 2, 2, 1), (s.Width, s.Height, s.Frames.Count, s.Layers.Count));
        Assert.Equal([100, 250], s.Frames.Select(f => f.DurationMs));
        Assert.Equal([R, 0u, 0u, B], s.Cel(0, 0));
        Assert.Equal(1.0, s.Layers[0].Opacity);   // added later, defaults to opaque
        Assert.Empty(s.Tags);                     // added later
    }

    [Fact]
    public void Sprite_v2()
    {
        var s = ProjectFile.Load(Fixture("sprite.v2.pxf"));
        Assert.Equal((3, 2, 3, 2), (s.Width, s.Height, s.Frames.Count, s.Layers.Count));
        Assert.Equal("walk", s.Tags.Single().Name);
        Assert.Equal((1, 2), (s.Tags[0].From, s.Tags[0].To));
        Assert.Equal(0.5, s.Layers[1].Opacity);
        Assert.False(s.Layers[1].Visible);
        Assert.Equal(B, s.Cel(1, 2)[5]);
    }

    [Fact]
    public void Map_v1()
    {
        var m = Tilemap.Load(Fixture("map.v1.pxm"));
        Assert.Equal((3, 2, 2), (m.Width, m.Height, m.TileSize));
        Assert.Equal(2, m.Tileset!.Count);
        Assert.Equal([0, 1, -1, -1, 1, 0], m.Layers[0].Tiles);
        Assert.Empty(m.Objects);
        Assert.Empty(m.Terrains);
    }

    [Fact]
    public void Map_v2()
    {
        var m = Tilemap.Load(Fixture("map.v2.pxm"));
        Assert.Equal(2, m.Layers.Count);
        Assert.Equal(["Player", "Wall"], m.Objects.Select(o => o.Name));
        Assert.True(m.Objects[0].IsPoint);
        Assert.Equal([0], m.Terrains.Select(t => t.Base));
        Assert.Equal(TerrainMode.Blob16, m.Terrains[0].Mode);
        Assert.Equal(1, m.Layers[1].Tiles[0]);
    }

    [Fact]
    public void Song_v1()
    {
        var s = Song.Load(Fixture("song.v1.pxs"));
        Assert.Equal((140, 16), (s.Bpm, s.Steps));
        Assert.Equal(60, s.Patterns[0].Notes[0][0]);
        Assert.Equal(69, s.Patterns[0].Notes[2][4]);
        Assert.Equal(Pattern.DefaultVolume, s.Patterns[0].Volumes[0][0]);
        Assert.Equal(0.25, s.Instruments[0].Duty);   // channel default
    }

    [Fact]
    public void Song_v2()
    {
        var s = Song.Load(Fixture("song.v2.pxs"));
        Assert.Equal([0, 1, 0], s.Order);
        Assert.Equal(Pattern.Hold, s.Patterns[1].Notes[1][3]);
        Assert.Equal(5, s.Patterns[1].Volumes[1][2]);
        Assert.Equal(0.125, s.Instruments[0].Duty);
        Assert.Equal(300, s.Instruments[3].DecayMs);
    }

    [Fact]
    public void Sound_v1_without_version_field()
    {
        var s = Sfx.Load(Fixture("sound.v1.sfx"));
        Assert.Equal(SfxWave.Saw, s.Wave);
        Assert.Equal(523, s.Freq);
        Assert.Equal(-2, s.Slide);
        Assert.Equal((int)Math.Round(0.25 * Synth.SampleRate), s.Render().Length);
    }

    [Fact]
    public void All_fixtures_still_render_or_export()
    {
        var sprite = ProjectFile.Load(Fixture("sprite.v2.pxf"));
        Assert.Contains("frameTags", SpriteSheet.Json(sprite, SpriteSheet.Build(sprite), "a.png", "a"));
        Assert.Contains("\"objectgroup\"", TiledExport.Json(Tilemap.Load(Fixture("map.v2.pxm")), "t.png"));
        Assert.NotEmpty(Synth.Render(Song.Load(Fixture("song.v2.pxs"))));
    }

    // ---------- formats added in phases 3–5 (Fixtures/current, written at commit efccf07) ----------

    static readonly Guid SpriteId = Guid.Parse("11111111-1111-1111-1111-111111111111"), MapId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        CharId = Guid.Parse("33333333-3333-3333-3333-333333333333"), WavId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void Map_v5_with_tileset_asset_rules_and_collisions()
    {
        var m = Tilemap.Load(Fixture("current/map.v5.pxm"));
        Assert.Equal(MapId, m.TilesetAssetId);
        Assert.Equal(TerrainMode.Blob47, m.Terrains[0].Mode);
        Assert.Equal([3], m.Terrains[0].Variants);
        Assert.Equal(new TileCollision(0, 0, 2, 1, true, 2), m.TileCollisions[1]);
    }

    [Fact]
    public void Character_v1()
    {
        var c = Character.Load(Fixture("current/hero.v1.pxchar"));
        Assert.Equal((CharacterKind.Player, SpriteId, 4, 8), (c.Kind, c.SpriteAssetId!.Value, c.OriginX, c.OriginY));
        Assert.Equal("walk", c.Animations["run"]);
        Assert.Equal((BoxKind.Hit, "run", 1), (c.Boxes[1].Kind, c.Boxes[1].State, c.Boxes[1].Frame!.Value));
        Assert.Equal("120", c.Properties["speed"]);
    }

    [Fact]
    public void Scene_v1()
    {
        var s = Scene.Load(Fixture("current/level.v1.pxscene"));
        Assert.Equal(("World 1", 2, 3), (s.Level, s.Order, s.Layers.Count));
        Assert.Equal((0.5, true), (s.Layers[0].ParallaxX, s.Layers[0].RepeatX));
        Assert.Equal(CharId, s.Instances[0].AssetId);
        Assert.Equal(Easing.EaseOut, s.Instances[0].Track!.Keys[1].Easing);
        Assert.Equal(AreaKind.Checkpoint, s.Areas[0].Kind);
    }

    [Fact]
    public void Mix_v1()
    {
        var m = Mix.Load(Fixture("current/music.v1.pxmix"));
        Assert.Equal((500, 2500), (m.LoopStartMs!.Value, m.LoopEndMs!.Value));
        var c = m.Tracks[0].Clips[0];
        Assert.Equal((WavId, 100, 900, 3, 50), (c.AssetId, c.OffsetMs, c.LengthMs!.Value, c.Repeat, c.FadeOutMs));
        Assert.True(m.Tracks[1].Mute);
    }

    static readonly Guid UiId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public void Scene_v2_with_ui_overlay_and_v1_still_loads()
    {
        var v2 = Scene.Load(Fixture("current/level.v2.pxscene"));
        Assert.Equal((UiId, "HUD"), (v2.UiAssetId!.Value, v2.UiScreen));
        var v1 = Scene.Load(Fixture("current/level.v1.pxscene"));
        Assert.Null(v1.UiAssetId);                                    // v1 had no overlay
        Assert.Equal(v1.Instances[0].Track!.Keys.Count, v2.Instances[0].Track!.Keys.Count);
    }

    [Fact]
    public void Painting_v1()
    {
        var d = PaintDoc.Load(Fixture("current/painting.v1.pxpaint"));
        Assert.Equal((300, 40, 2), (d.Width, d.Height, d.Layers.Count));
        Assert.Equal(R, d.Layers[0].Get(256 + 3, 0));
        Assert.Equal((BlendMode.Screen, 0.5), (d.Layers[1].Blend, d.Layers[1].Opacity));
        Assert.Equal(0, d.Layers[1].MaskAt(5, 5));
    }

    [Fact]
    public void Drawing_v1()
    {
        var v = VectorDoc.Load(Fixture("current/drawing.v1.pxvec"));
        Assert.Equal((32.0, 16.0, 2), (v.Width, v.Height, v.Shapes.Count));
        Assert.Equal((VShapeKind.Rect, 2.0), (v.Shapes[0].Kind, v.Shapes[0].Radius));
        Assert.Equal(VPaintKind.Linear, v.Shapes[1].Stroke!.Kind);
        Assert.Equal(5.0, v.Shapes[1].Contours[0].Nodes[0].OutX);
    }

    [Fact]
    public void Texture_v1_and_kit_v1()
    {
        var t = TextureDoc.Load(Fixture("current/texture.v1.pxtex"));
        Assert.Equal((Material.Wood, "oak", 64, true), (t.Material, t.Seed, t.Size, t.FlipGreen));
        var k = UiKit.Load(Fixture("current/kit.v1.pxui"));
        Assert.Equal(["Main menu", "HUD"], k.Screens.Select(s => s.Name));
        Assert.Equal(SpriteId, k.Elements[0].Art[UiState.Hover]);
        Assert.Equal(UiAnchor.TopLeft, k.Screens[1].Items[0].Anchor);
    }

    static readonly Guid AnimId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [Fact]
    public void Anim_v1_and_rig_and_clip_fields()
    {
        var l = AnimLibrary.Load(Fixture("current/anim.v1.pxanim"));
        Assert.Equal(["wave", "shake"], l.Clips.Select(c => c.Name));
        Assert.Equal(new AnimKey(0, 0, AnimEase.Bezier, 0.42, 0, 0.58, 1), l.Clips[0].Tracks[0].Keys[0]);
        Assert.Equal((AnimPlay.Once, true, "arm"), (l.Clips[1].Play, l.Clips[1].Reverse, l.Clips[0].Tracks[0].Target));

        var c = Character.Load(Fixture("current/hero.v2.pxchar"));
        Assert.Equal((2, "body", -4.0), (c.Parts.Count, c.Parts[1].Parent, c.Parts[1].OffsetY));
        Assert.Equal((AnimId, "wave"), (c.RigAnimAssetId!.Value, c.RigClips["idle"]));
        Assert.Empty(Character.Load(Fixture("current/hero.v1.pxchar")).Parts);       // v1: no rig

        var s = Scene.Load(Fixture("current/level.v3.pxscene"));
        Assert.Equal((AnimId, "wave", "shake"), (s.Instances[0].ClipAssetId!.Value, s.Instances[0].Clip, s.CameraClip));
        Assert.Null(Scene.Load(Fixture("current/level.v2.pxscene")).Instances[0].ClipAssetId);

        var k = UiKit.Load(Fixture("current/kit.v2.pxui"));
        Assert.Equal("shake", k.Screens[0].Items[0].Clip);
        Assert.All(UiKit.Load(Fixture("current/kit.v1.pxui")).Screens.SelectMany(x => x.Items), i => Assert.Null(i.ClipAssetId));
    }

    [Fact]
    public void Scene_v4_with_lights_and_particles_and_v3_stays_unlit()
    {
        var s = Scene.Load(Fixture("current/level.v4.pxscene"));
        Assert.Equal((0xFF203040u, false), (s.Ambient, s.TileShadows));
        Assert.Equal((LightKind.Spot, 70, 45.0, 0.5), (s.Lights[0].Kind, s.Lights[0].Radius, s.Lights[0].Direction, s.Lights[0].Flicker));
        Assert.Equal([0, 0, 10, 0, 5, 8], s.Occluders[0].Points);
        Assert.Equal((EmitterPreset.Smoke, 7, true, -4.0), (s.Emitters[0].Preset, s.Emitters[0].Seed, s.Emitters[0].Additive, s.Emitters[0].Gravity));
        Assert.Equal(2, s.Instances[0].Track!.Keys.Count);                          // older fields kept
        var v3 = Scene.Load(Fixture("current/level.v3.pxscene"));
        Assert.False(Lighting.IsOn(v3));
        Assert.True(v3.TileShadows);
    }

    static readonly Guid LogicId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    [Fact]
    public void Logic_v1_and_scene_v5()
    {
        var g = LogicGraph.Load(Fixture("current/logic.v1.pxlogic"));
        Assert.Equal(["door", "rich", "pause", "go"], g.Nodes.Select(n => n.Id));
        Assert.Equal((LogicKind.Compare, ">=", 200.0), (g.Nodes[1].Kind, g.Nodes[1].P("op"), g.Nodes[1].X));
        Assert.Equal(new LogicLink("rich", "true", "pause"), g.Links[1]);
        Assert.DoesNotContain(g.Check(), m => !m.StartsWith("Warning:"));
        Assert.Equal(LogicId, Scene.Load(Fixture("current/level.v5.pxscene")).LogicAssetId);
        Assert.Null(Scene.Load(Fixture("current/level.v4.pxscene")).LogicAssetId);
    }

    /// <summary>Writes Fixtures/current with the current code (PF_WRITE_FIXTURES=1, PF_FIXTURES_DIR = the Fixtures folder).</summary>
    [Fact]
    public void WriteCurrentFixtures()
    {
        if (Environment.GetEnvironmentVariable("PF_WRITE_FIXTURES") != "1") return;
        string dir = Path.Combine(Environment.GetEnvironmentVariable("PF_FIXTURES_DIR") ?? throw new InvalidOperationException("Set PF_FIXTURES_DIR."), "current");
        Directory.CreateDirectory(dir);
        bool New(string name) => !File.Exists(Path.Combine(dir, name));   // golden files are written once and never replaced

        var m = new Tilemap(2, 2, 2) { Tileset = new Tileset([R, R, B, B, R, R, B, B], 4, 2, 2), TilesetAssetId = MapId };
        m.Layers[0].Tiles[0] = 1;
        m.Terrains.Add(new TerrainRule { Base = 0, Mode = TerrainMode.Blob47, Variants = [3] });
        m.TileCollisions[1] = new TileCollision(0, 0, 2, 1, OneWay: true, Category: 2);
        if (New("map.v5.pxm")) Tilemap.Save(m, Path.Combine(dir, "map.v5.pxm"));

        var c = new Character { Kind = CharacterKind.Player, SpriteAssetId = SpriteId, OriginX = 4, OriginY = 8, Facing = Facing.Left };
        c.Animations["idle"] = "idle";
        c.Animations["run"] = "walk";
        c.Boxes.Add(new CharacterBox { Kind = BoxKind.Solid, X = 2, Y = 0, W = 4, H = 8, Category = 1 });
        c.Boxes.Add(new CharacterBox { Kind = BoxKind.Hit, State = "run", Frame = 1, X = 6, Y = 2, W = 3, H = 3 });
        c.Properties["speed"] = "120";
        if (New("hero.v1.pxchar")) Character.Save(c, Path.Combine(dir, "hero.v1.pxchar"));

        var s = new Scene { Width = 320, Height = 180, Level = "World 1", Order = 2, CameraX = 8, MusicAssetId = WavId };
        s.Layers.Insert(0, new SceneLayer { Name = "Sky", Kind = SceneLayerKind.Image, AssetId = SpriteId, ParallaxX = 0.5, RepeatX = true });
        s.Layers.Insert(1, new SceneLayer { Name = "Ground", Kind = SceneLayerKind.Tilemap, AssetId = MapId });
        s.Instances.Add(new SceneInstance
        {
            Name = "Hero", AssetId = CharId, LayerId = s.Layers[2].Id, X = 16, Y = 64, FlipX = true, Z = 1, Animation = "idle", Properties = { ["hp"] = "3" },
            Track = new KeyframeTrack { Keys = [new Keyframe(0), new Keyframe(800, X: 32, Opacity: 0.5, Easing: Easing.EaseOut)], Loop = false },
        });
        s.Areas.Add(new SceneArea { Kind = AreaKind.Checkpoint, Name = "cp1", X = 100, Y = 40, W = 8, H = 32, Properties = { ["id"] = "1" } });
        if (New("level.v2.pxscene"))
        {
            s.UiAssetId = UiId;
            s.UiScreen = "HUD";
            Scene.Save(s, Path.Combine(dir, "level.v2.pxscene"));
        }

        var mix = new Mix { MasterVolume = 0.8, LoopStartMs = 500, LoopEndMs = 2500 };
        mix.Tracks[0].Clips.Add(new MixClip { AssetId = WavId, StartMs = 250, OffsetMs = 100, LengthMs = 900, Gain = 0.7, FadeInMs = 20, FadeOutMs = 50, Repeat = 3 });
        mix.Tracks[1].Mute = true;
        if (New("music.v1.pxmix")) Mix.Save(mix, Path.Combine(dir, "music.v1.pxmix"));

        // phases 13–16
        if (New("painting.v1.pxpaint"))
        {
            var paint = new PaintDoc(300, 40);
            paint.Layers[0].TileFor(1, 0)[3] = R;
            paint.Layers.Add(new PaintLayer { Name = "glow", Blend = BlendMode.Screen, Opacity = 0.5, Mask = new() { [PaintLayer.Key(0, 0)] = new byte[256 * 256] } });
            PaintDoc.Save(paint, Path.Combine(dir, "painting.v1.pxpaint"));
        }
        if (New("drawing.v1.pxvec"))
        {
            var v = new VectorDoc { Width = 32, Height = 16 };
            v.Shapes.Add(new VShape { Kind = VShapeKind.Rect, Name = "box", X = 1, Y = 2, W = 10, H = 6, Radius = 2, Fill = VPaint.Solid(R) });
            v.Shapes.Add(new VShape
            {
                Kind = VShapeKind.Path, Name = "curve", Contours = [new VContour([new VNode(0, 0, 0, 0, 5, 0), new VNode(10, 10, 5, 10, 10, 10)], false)],
                Stroke = new VPaint(VPaintKind.Linear, B, [new VStop(0, B), new VStop(1, R)], 0, 0, 10, 10), StrokeWidth = 2,
            });
            VectorDoc.Save(v, Path.Combine(dir, "drawing.v1.pxvec"));
        }
        if (New("texture.v1.pxtex")) TextureDoc.Save(new TextureDoc { Material = Material.Wood, Seed = "oak", Size = 64, Scale = 3, FlipGreen = true }, Path.Combine(dir, "texture.v1.pxtex"));
        if (New("kit.v1.pxui"))
        {
            var kit = UiKit.Starter(UiKit.Styles["Sci-fi"], ["Main menu", "HUD"], 320, 180);
            kit.Elements[0].Art[UiState.Hover] = SpriteId;
            UiKit.Save(kit, Path.Combine(dir, "kit.v1.pxui"));
        }

        // phase 17
        if (New("anim.v1.pxanim"))
            AnimLibrary.Save(new AnimLibrary
            {
                Clips =
                [
                    new AnimClip { Name = "wave", Play = AnimPlay.PingPong, Tracks = [new AnimTrack { Target = "arm", Property = AnimProperty.Rotation, Keys = [new AnimKey(0, 0, AnimEase.Bezier, 0.42, 0, 0.58, 1), new AnimKey(400, 45)] }] },
                    new AnimClip { Name = "shake", Play = AnimPlay.Once, Reverse = true, Tracks = [new AnimTrack { Property = AnimProperty.X, Keys = [new AnimKey(0, 0), new AnimKey(100, 3, AnimEase.Step)] }] },
                ],
            }, Path.Combine(dir, "anim.v1.pxanim"));
        if (New("hero.v2.pxchar"))
        {
            c.Parts = [new RigPart { Name = "body", W = 4, H = 6, PivotX = 2, PivotY = 6 }, new RigPart { Name = "arm", X = 4, W = 2, H = 4, Parent = "body", OffsetX = 1, OffsetY = -4, Z = 1 }];
            c.RigAnimAssetId = AnimId;
            c.RigClips["idle"] = "wave";
            Character.Save(c, Path.Combine(dir, "hero.v2.pxchar"));
        }
        if (New("level.v3.pxscene"))
        {
            s.Instances[0].ClipAssetId = AnimId;
            s.Instances[0].Clip = "wave";
            s.CameraClipAssetId = AnimId;
            s.CameraClip = "shake";
            Scene.Save(s, Path.Combine(dir, "level.v3.pxscene"));
        }
        if (New("level.v4.pxscene"))   // phase 18
        {
            s.Ambient = 0xFF203040;
            s.TileShadows = false;
            s.Lights.Add(new SceneLight { Name = "torch", Kind = LightKind.Spot, X = 30, Y = 40, Radius = 70, Color = 0xFFFFA040, Intensity = 1.5, Direction = 45, Cone = 50, Glow = 0.3, Flicker = 0.5 });
            s.Occluders.Add(new SceneOccluder { Name = "rock", Points = [0, 0, 10, 0, 5, 8] });
            s.Emitters.Add(new SceneEmitter { Name = "smoke", Preset = EmitterPreset.Smoke, X = 3, Y = 4, W = 5, Rate = 12, LifeMs = 900, Gravity = -4, Color = 0xB0808080, EndColor = 0x00606060, Additive = true, Seed = 7 });
            Scene.Save(s, Path.Combine(dir, "level.v4.pxscene"));
        }
        if (New("logic.v1.pxlogic"))   // phase 20
        {
            var g = new LogicGraph();
            g.Nodes.Add(LogicGraph.NewNode(LogicKind.EnterArea, "door", 10, 20));
            g.Nodes[0].Params["area"] = "exit";
            g.Nodes.Add(LogicGraph.NewNode(LogicKind.Compare, "rich", 200, 20));
            g.Nodes.Add(LogicGraph.NewNode(LogicKind.Wait, "pause", 400, 20));
            g.Nodes.Add(LogicGraph.NewNode(LogicKind.LoadScene, "go", 600, 20));
            g.Nodes[3].Params["scene"] = SpriteId.ToString();
            g.Links.AddRange([new LogicLink("door", "out", "rich"), new LogicLink("rich", "true", "pause"), new LogicLink("pause", "out", "go")]);
            LogicGraph.Save(g, Path.Combine(dir, "logic.v1.pxlogic"));
        }
        if (New("level.v5.pxscene"))
        {
            s.LogicAssetId = LogicId;
            Scene.Save(s, Path.Combine(dir, "level.v5.pxscene"));
        }
        if (New("kit.v2.pxui"))
        {
            var kit = UiKit.Starter(UiKit.Styles["Sci-fi"], ["HUD"], 320, 180);
            kit.Screens[0].Items[0].ClipAssetId = AnimId;
            kit.Screens[0].Items[0].Clip = "shake";
            UiKit.Save(kit, Path.Combine(dir, "kit.v2.pxui"));
        }
    }

    /// <summary>Writes the v2 fixtures with the current code. Runs only with PF_WRITE_FIXTURES=1 and the fixtures folder as PF_FIXTURES_DIR.</summary>
    [Fact]
    public void WriteV2Fixtures()
    {
        if (Environment.GetEnvironmentVariable("PF_WRITE_FIXTURES") != "1") return;
        string dir = Environment.GetEnvironmentVariable("PF_FIXTURES_DIR") ?? throw new InvalidOperationException("Set PF_FIXTURES_DIR.");

        var s = new Sprite(3, 2);
        s.AddFrame(1);
        s.AddFrame(2, copyFrom: 1);
        s.AddLayer(1, "fx");
        s.Layers[1].Visible = false;
        s.Layers[1].Opacity = 0.5;
        s.Cel(0, 0)[0] = R;
        s.Cel(1, 2)[5] = B;
        s.Tags.Add(new FrameTag { Name = "walk", From = 1, To = 2 });
        ProjectFile.Save(s, Path.Combine(dir, "sprite.v2.pxf"));

        var m = new Tilemap(3, 2, 2) { Tileset = new Tileset([R, R, B, B, R, R, B, B], 4, 2, 2) };
        m.Layers[0].Tiles[4] = 0;
        m.AddLayer(1, "Deco").Tiles[0] = 1;
        m.Objects.Add(new MapObject { Id = 1, Name = "Player", Type = "spawn", X = 1, Y = 1 });
        m.Objects.Add(new MapObject { Id = 2, Name = "Wall", Type = "solid", X = 0, Y = 0, W = 6, H = 2 });
        m.Terrains.Add(new TerrainRule { Base = 0 });
        Tilemap.Save(m, Path.Combine(dir, "map.v2.pxm"));

        var song = new Song { Bpm = 150 };
        song.Patterns.Add(Pattern.Empty(16));
        song.Patterns[1].SetNote(1, 2, 64, 3);
        song.Patterns[1].Volumes[1][2] = 5;
        song.Instruments[0].Duty = 0.125;
        song.Instruments[3].DecayMs = 300;
        song.Order.AddRange([1, 0]);
        Song.Save(song, Path.Combine(dir, "song.v2.pxs"));
    }
}
