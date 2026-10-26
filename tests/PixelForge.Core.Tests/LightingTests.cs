using System.Diagnostics;
using PixelForge.Core;

namespace PixelForge.Core.Tests;

public sealed class LightingTests
{
    const uint Gray = 0xFF808080;

    static uint[] Lit(Scene s, IEnumerable<Collider>? solids = null, int w = 100, int h = 100, double t = 0)
    {
        var o = Enumerable.Repeat(Gray, w * h).ToArray();
        Lighting.Apply(o, w, h, 0, 0, s, solids ?? [], t);
        return o;
    }

    static int R(uint p) => (int)(p >> 16 & 0xFF);

    [Fact]
    public void Lights_fall_off_spots_keep_to_their_cone_and_walls_cast_shadows()
    {
        var s = new Scene { Ambient = 0xFF000000 };
        Assert.Equal(Gray, Lit(new Scene())[0]);                                           // white ambient, no lights: off
        s.Lights.Add(new SceneLight { X = 50, Y = 50, Radius = 40, Color = 0xFFFFFFFF, Shadows = false });
        var o = Lit(s);
        Assert.True(R(o[50 * 100 + 50]) > 110);
        Assert.True(R(o[50 * 100 + 50]) > R(o[50 * 100 + 75]));
        Assert.Equal(0, R(o[50 * 100 + 95]));                                              // beyond the radius: ambient black

        s.Lights[0] = new SceneLight { Kind = LightKind.Spot, X = 50, Y = 50, Radius = 40, Direction = 0, Cone = 40, Color = 0xFFFFFFFF, Shadows = false };
        o = Lit(s);
        Assert.True(R(o[50 * 100 + 65]) > 0);                                             // in front
        Assert.Equal(0, R(o[50 * 100 + 35]));                                              // behind

        s.Lights[0] = new SceneLight { X = 50, Y = 50, Radius = 45, Color = 0xFFFFFFFF };
        var wall = new Collider(60, 40, 4, 20, false);
        o = Lit(s, [wall]);
        Assert.Equal(0, R(o[50 * 100 + 75]));                                              // behind the wall
        Assert.True(R(o[50 * 100 + 25]) > 0);                                             // same distance, open side
        Assert.True(R(Lit(s, [wall with { OneWay = true }])[50 * 100 + 75]) > 0);         // one-way platforms let light through
        s.TileShadows = false;
        Assert.True(R(Lit(s, [wall])[50 * 100 + 75]) > 0);
        s.Occluders.Add(SceneOccluder.Rect(60, 40, 4, 20));                                // a polygon does the same
        Assert.Equal(0, R(Lit(s, [wall])[50 * 100 + 75]));
    }

    [Fact]
    public void Glow_brightens_and_flicker_changes_over_time()
    {
        var s = new Scene { Ambient = 0xFF000000 };
        s.Lights.Add(new SceneLight { X = 50, Y = 50, Radius = 30, Color = 0xFFFF8000, Glow = 1, Flicker = 1, Shadows = false });
        var black = new uint[100 * 100];
        Array.Fill(black, 0xFF000000);
        Lighting.Apply(black, 100, 100, 0, 0, s, [], 0);
        Assert.True(R(black[50 * 100 + 50]) > 100);                                        // glow shows on black
        Assert.NotEqual(Lit(s, t: 0)[50 * 100 + 55], Lit(s, t: 400)[50 * 100 + 55]);
        Assert.Equal(Lit(s, t: 400), Lit(s, t: 400));
    }

    [Fact]
    public void Particles_are_seeded_prewarmed_and_capped()
    {
        var rain = SceneEmitter.FromPreset(EmitterPreset.Rain);
        var a = Particles.At(rain, 0);
        Assert.InRange(a.Count, (int)(rain.Rate * rain.LifeMs / 1000) - 2, (int)(rain.Rate * rain.LifeMs / 1000) + 2);   // already falling
        Assert.Equal(a, Particles.At(rain, 0));
        rain.Seed = 2;
        Assert.NotEqual(a, Particles.At(rain, 0));
        var fire = SceneEmitter.FromPreset(EmitterPreset.Fire);
        Assert.All(Particles.At(fire, 500), p => Assert.True(p.Y < fire.Y + 5));        // fire rises
        var heavy = new SceneEmitter { Rate = 2000, LifeMs = 5000 };
        Assert.Equal(Particles.MaxAlive, Particles.At(heavy, 1000).Count);
        Assert.Empty(Particles.At(new SceneEmitter { Rate = 0 }, 1000));
    }

    [Fact]
    public void Scene_v4_round_trips_effects_and_rejects_bad_values()
    {
        var s = new Scene { Ambient = 0xFF202040, TileShadows = false };
        s.Lights.Add(SceneLight.Presets["Torch"]());
        s.Occluders.Add(SceneOccluder.Rect(1, 2, 3, 4));
        s.Emitters.Add(SceneEmitter.FromPreset(EmitterPreset.Snow));
        var back = Scene.Deserialize(Scene.Serialize(s));
        Assert.Equal((0xFF202040u, false, 0.6), (back.Ambient, back.TileShadows, back.Lights[0].Flicker));
        Assert.Equal([1, 2, 4, 2, 4, 6, 1, 6], back.Occluders[0].Points);
        Assert.Equal(EmitterPreset.Snow, back.Emitters[0].Preset);
        Assert.Equal(s.Emitters[0].Id, s.Clone().Emitters[0].Id);

        s.Lights[0].Radius = 0;
        Assert.Throws<InvalidDataException>(() => Scene.Deserialize(Scene.Serialize(s)));
        s.Lights[0].Radius = 10;
        s.Occluders[0].Points.RemoveRange(0, 4);
        Assert.Throws<InvalidDataException>(() => Scene.Deserialize(Scene.Serialize(s)));
        s.Occluders.Clear();
        s.Emitters[0].Rate = 5000;
        Assert.Throws<InvalidDataException>(() => Scene.Deserialize(Scene.Serialize(s)));
    }

    [Fact]
    public void Godot_and_generic_exports_carry_lights_occluders_and_particles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pf-light-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var p = Project.Create(dir, "L");
            var s = new Scene { Ambient = 0xFF101020 };
            s.Lights.Add(SceneLight.Presets["Torch"]());
            s.Lights.Add(SceneLight.Presets["Spotlight"]());
            s.Occluders.Add(SceneOccluder.Rect(10, 10, 5, 5));
            s.Emitters.Add(SceneEmitter.FromPreset(EmitterPreset.Fire));
            Scene.Save(s, Path.Combine(dir, "cave.pxscene"));
            p.Add(Path.Combine(dir, "cave.pxscene"));

            var r = Exporter.Plan(p, new ExportProfile());
            Assert.True(r.Ok, string.Join("\n", r.Errors));
            string level = r.TextEnding("cave.tscn");
            foreach (var part in new[] { "type=\"CanvasModulate\"", "type=\"PointLight2D\"", "shadow_enabled = true", "type=\"GradientTexture2D\"", "_spot1.png",
                         "polygon = PackedVector2Array(10.0, 10.0, 15.0, 10.0, 15.0, 15.0, 10.0, 15.0)", "type=\"CPUParticles2D\"", "blend_mode = 1", "metadata/flicker" })
                Assert.Contains(part, level);
            Assert.Contains(r.Files.Keys, k => k.EndsWith("cave_spot1.png"));

            var g = Exporter.Plan(p, new ExportProfile { Target = ExportTarget.Generic });
            var json = System.Text.Json.JsonDocument.Parse(g.Files.Single(f => f.Key.EndsWith("cave_scene.json")).Value).RootElement;
            Assert.Equal("#FF101020", json.GetProperty("ambient").GetString());
            Assert.Equal(2, json.GetProperty("lights").GetArrayLength());
            Assert.Equal("Fire", json.GetProperty("emitters")[0].GetProperty("preset").GetString());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Lit_scene_with_2000_particles_renders_fast_enough()
    {
        var s = new Scene { Width = 640, Height = 360, Ambient = 0xFF303040 };
        for (int i = 0; i < 4; i++) s.Lights.Add(new SceneLight { X = 40 + i * 80, Y = 90, Radius = 100, Glow = 0.3 });
        s.Occluders.Add(SceneOccluder.Rect(100, 60, 20, 60));
        s.Emitters.Add(new SceneEmitter { X = 0, Y = 0, W = 320, Rate = 2000, LifeMs = 1000, Speed = 50, Direction = 90 });
        var dir = Path.Combine(Path.GetTempPath(), "pf-light-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var a = new SceneAssets(Project.Create(dir, "L"));
            SceneRenderer.Render(s, a, 0, 0, 320, 180, 0);   // warm-up
            var sw = Stopwatch.StartNew();
            for (int f = 0; f < 10; f++) SceneRenderer.Render(s, a, 0, 0, 320, 180, f * 16);
            Assert.True(sw.ElapsedMilliseconds / 10 < 100, $"{sw.ElapsedMilliseconds / 10} ms per frame");
        }
        finally { Directory.Delete(dir, true); }
    }
}
