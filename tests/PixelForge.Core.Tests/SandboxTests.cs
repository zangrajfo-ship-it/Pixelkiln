using PixelForge.Core;

namespace PixelForge.Core.Tests;

public sealed class SandboxTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "pf-sandbox-" + Guid.NewGuid().ToString("N"));

    public SandboxTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    string P(string name) => Path.Combine(_dir, name);

    /// <summary>64×64 scene: solid floor row at y 56–63, a hero (8×8 sprite, feet origin) at (16, 20).</summary>
    (Scene Scene, SceneAssets Assets) World()
    {
        var p = Project.Create(_dir, "G");
        var map = new Tilemap(8, 8, 8) { Tileset = new Tileset(Enumerable.Repeat(0xFF00FF00u, 16 * 8).ToArray(), 16, 8, 8) };
        Array.Fill(map.Layers[0].Tiles, -1);
        for (int x = 0; x < 8; x++) map.Layers[0].Tiles[7 * 8 + x] = 1;
        map.TileCollisions[1] = new TileCollision(0, 0, 8, 8);
        Tilemap.Save(map, P("ground.pxm"));
        var ground = p.Add(P("ground.pxm"));

        var sprite = new Sprite(8, 8);
        sprite.Tags.Add(new FrameTag { Name = "idle", From = 0, To = 0 });
        ProjectFile.Save(sprite, P("hero.pxf"));
        var hero = new Character { Kind = CharacterKind.Player, SpriteAssetId = p.Add(P("hero.pxf")).Id, OriginX = 4, OriginY = 8 };
        hero.Animations["idle"] = "idle";
        hero.Boxes.Add(new CharacterBox { Kind = BoxKind.Solid, X = 2, Y = 0, W = 4, H = 8 });
        Character.Save(hero, P("hero.pxchar"));
        var heroAsset = p.Add(P("hero.pxchar"));

        var s = new Scene { Width = 64, Height = 64 };
        s.Layers.Insert(0, new SceneLayer { Name = "Ground", Kind = SceneLayerKind.Tilemap, AssetId = ground.Id });
        s.Instances.Add(new SceneInstance { Name = "Hero", AssetId = heroAsset.Id, LayerId = s.Layers[1].Id, X = 16, Y = 20 });
        return (s, new SceneAssets(p));
    }

    static void Run(Sandbox sb, int frames, SandboxInput input)
    {
        for (int i = 0; i < frames; i++) sb.Step(16, input);
    }

    [Fact]
    public void Player_falls_lands_on_solid_tiles_walks_and_jumps()
    {
        var (s, a) = World();
        var sb = new Sandbox(s, a, 32, 32);
        Assert.Equal(64, sb.Colliders.Count(c => !c.OneWay) * 8);
        Run(sb, 60, new(false, false, false));
        Assert.True(sb.OnGround);
        Assert.Equal(56, sb.Y);                     // feet on top of the floor row
        Run(sb, 20, new(false, true, false));
        Assert.True(sb.X > 16);
        Assert.False(sb.Player!.FlipX);
        sb.Step(16, new(false, false, true));
        Assert.True(sb.VY < 0);
        Assert.InRange(sb.CamX, 0, 64 - 32);        // the camera stays inside the scene
    }

    [Fact]
    public void Kill_area_respawns_at_the_last_checkpoint_and_reset_restores_the_start()
    {
        var (s, a) = World();
        s.Areas.Add(new SceneArea { Kind = AreaKind.Checkpoint, Name = "cp", X = 30, Y = 40, W = 8, H = 16 });
        s.Areas.Add(new SceneArea { Kind = AreaKind.Kill, Name = "spikes", X = 50, Y = 48, W = 8, H = 8 });
        s.Areas.Add(new SceneArea { Kind = AreaKind.Trigger, Name = "door", X = 40, Y = 40, W = 4, H = 16 });
        string before = Scene.Serialize(s);
        var sb = new Sandbox(s, a, 32, 32);
        var entered = new List<string>();
        for (int i = 0; i < 200 && sb.Deaths == 0; i++)
        {
            sb.Step(16, new(false, true, false));
            entered.AddRange(sb.Entered.Select(e => e.Name));
        }
        Assert.Equal(1, sb.Deaths);
        Assert.Contains("door", entered);
        Assert.Equal((34.0, 56.0), sb.Respawn);
        Assert.Equal(sb.Respawn, (sb.X, sb.Y));

        sb.Reset();
        Assert.Equal((16.0, 20.0, 0, 0.0), (sb.X, sb.Y, sb.Deaths, sb.TimeMs));
        Assert.Equal(before, Scene.Serialize(s));   // the source scene was never touched
    }

    [Fact]
    public void Spawn_area_and_one_way_platforms()
    {
        var (s, a) = World();
        s.Areas.Add(new SceneArea { Kind = AreaKind.Spawn, X = 8, Y = 8, W = 8, H = 8 });
        var sb = new Sandbox(s, a, 32, 32);
        Assert.Equal((12.0, 16.0), (sb.X, sb.Y));
        sb.Colliders.Add(new Collider(0, 30, 64, 2, OneWay: true));
        Run(sb, 60, new(false, false, false));
        Assert.Equal(30, sb.Y);                     // lands on the platform from above
        sb.Colliders.Clear();
        sb.Colliders.Add(new Collider(0, 20, 64, 2, OneWay: true));
        sb.Y = 30; sb.VY = -200;
        sb.Step(16, new(false, false, false));
        Assert.True(sb.Y < 30);                     // and passes through it from below
    }

    [Fact]
    public void Renderer_applies_parallax_flip_and_blends()
    {
        var p = Project.Create(_dir, "G");
        ImageIO.SavePng(P("dot.png"), [0xFFFF0000, 0xFF0000FF], 2, 1);
        var dot = p.Add(P("dot.png"));
        var s = new Scene { Width = 100, Height = 10, Background = 0xFF000000 };
        s.Layers.Insert(0, new SceneLayer { Kind = SceneLayerKind.Image, AssetId = dot.Id, ParallaxX = 0.5, OffsetX = 20 });
        var o = SceneRenderer.Render(s, new SceneAssets(p), 10, 0, 40, 4, 0);
        Assert.Equal(0xFFFF0000, o[15]);            // 20 - 10 * 0.5
        Assert.Equal(0xFF0000FF, o[16]);

        var dst = new uint[2];
        SceneRenderer.Blit(dst, 2, 1, [0xFFFF0000, 0x80FFFFFF], 2, 1, 0, 0, flipX: true, 1);
        Assert.Equal(0xFFFF0000, dst[1]);
        Assert.InRange(dst[0] & 0xFFu, 0x7Fu, 0x81u);
    }

    [Fact]
    public void Transformed_blit_scales_rotates_and_mirrors_around_the_origin()
    {
        const uint R = 0xFFFF0000, B = 0xFF0000FF;
        uint[] At(double sx, double sy, double rot)
        {
            var dst = new uint[10 * 10];
            SceneRenderer.BlitTransformed(dst, 10, 10, [R, B], 2, 1, 0, 0, 5, 5, sx, sy, rot, 1);
            return dst;
        }
        var scaled = At(2, 2, 0);                                   // 2×1 → 4×2 from the pivot
        Assert.Equal([R, R, B, B], scaled[(5 * 10 + 5)..(5 * 10 + 9)]);
        Assert.Equal(R, scaled[6 * 10 + 5]);
        Assert.Equal(8, scaled.Count(p => p != 0));

        var turned = At(1, 1, 90);                                  // clockwise on screen: the row points down
        Assert.Equal(R, turned[5 * 10 + 4]);
        Assert.Equal(B, turned[6 * 10 + 4]);

        var mirrored = At(-1, 1, 0);                                // negative scale mirrors around the origin
        Assert.Equal(R, mirrored[5 * 10 + 4]);
        Assert.Equal(B, mirrored[5 * 10 + 3]);
    }
}
