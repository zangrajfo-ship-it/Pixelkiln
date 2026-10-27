using PixelForge.Core;

namespace PixelForge.Core.Tests;

public sealed class TextureTests() : TempDirTest("tex")
{
    [Fact]
    public void Every_material_tiles_without_a_seam_and_repeats_for_a_seed()
    {
        foreach (var m in Enum.GetValues<Material>())
        {
            var t = new TextureDoc { Material = m, Size = 64, Seed = "s", Scale = 5 };
            var (a, h) = Textures.Generate(t);
            // the jump across the wrap-around edge is no bigger than the jumps next to it
            int seam = Textures.SeamError(a, 64, 64);
            Assert.True(seam <= 24, $"{m}: seam {seam}");
            Assert.Equal(a, Textures.Generate(t).Albedo);
            Assert.NotEqual(a, Textures.Generate(new TextureDoc { Material = m, Size = 64, Seed = "other", Scale = 5 }).Albedo);
            Assert.All(h, v => Assert.InRange(v, 0f, 1f));
        }
    }

    [Fact]
    public void Normal_map_points_away_from_slopes_and_occlusion_darkens_hollows()
    {
        int w = 8;
        var flat = new float[w * w];
        Assert.All(Textures.NormalMap(flat, w, w, 2), n => Assert.Equal(0xFF8080FFu, n));   // straight up
        var ramp = new float[w * w];
        for (int y = 0; y < w; y++) for (int x = 0; x < w; x++) ramp[y * w + x] = x / (float)w;   // rises to the right
        uint mid = Textures.NormalMap(ramp, w, w, 2)[3 * w + 3];
        Assert.True((mid >> 16 & 0xFF) < 128);                                  // normal leans left (x negative)
        Assert.Equal(128u, mid >> 8 & 0xFF, 1u);
        uint flipped = Textures.NormalMap(ramp, w, w, 2, flipGreen: true)[3 * w + 3];
        Assert.Equal(mid >> 16 & 0xFF, flipped >> 16 & 0xFF);

        var pit = Enumerable.Repeat(1f, 64 * 64).ToArray();
        pit[32 * 64 + 32] = 0;
        var ao = Textures.OcclusionMap(pit, 64, 64, 1);
        Assert.True((ao[32 * 64 + 32] & 0xFF) < (ao[0] & 0xFF));
    }

    [Fact]
    public void Make_seamless_removes_the_seam_of_a_picture()
    {
        int w = 32;
        var px = new uint[w * w];
        for (int y = 0; y < w; y++) for (int x = 0; x < w; x++) px[y * w + x] = 0xFF000000 | (uint)(x * 8) << 16;   // left dark, right red: a hard seam
        Assert.True(Textures.SeamError(px, w, w) > 200);
        var fixedPx = Textures.MakeSeamless(px, w, w, 8);
        Assert.True(Textures.SeamError(fixedPx, w, w) < 40);
        Assert.Equal(px[16 * w + 16], fixedPx[16 * w + 16]);                    // the middle is untouched
    }

    [Fact]
    public void Painted_textures_round_trip_export_and_wrap_painting_crosses_edges()
    {
        var p = Project.Create(_dir, "g");
        var paint = new PaintDoc(32, 32);
        var s = new PaintStroke(paint, paint.Layers[0], new BrushSettings(PaintTool.Pencil, Size: 6, Smoothing: 0, PressureSize: false, Wrap: true), 0xFFFF0000);
        s.To(1, 16);
        Assert.Equal(0xFFFF0000u, paint.Layers[0].Get(31, 16));                 // the dab continues on the other side
        PaintDoc.Save(paint, P("rock.pxpaint"));
        var rock = p.Add(P("rock.pxpaint"));

        var t = new TextureDoc { Source = TextureSource.Painting, AssetId = rock.Id, SeamBlend = 4 };
        TextureDoc.Save(t, P("rockface.pxtex"));
        var back = TextureDoc.Load(P("rockface.pxtex"));
        Assert.Equal((TextureSource.Painting, rock.Id, 4), (back.Source, back.AssetId!.Value, back.SeamBlend));
        var texAsset = p.Add(P("rockface.pxtex"));
        Assert.Contains(rock.Id, p.ReferencesOf(texAsset));
        var maps = Textures.Build(back, p);
        Assert.Equal((32, 32), (maps.Width, maps.Height));
        Assert.Throws<InvalidDataException>(() => Textures.Build(back, null));   // a painted texture needs its project
        Assert.Throws<InvalidDataException>(() => TextureDoc.Deserialize(TextureDoc.Serialize(new TextureDoc { Source = TextureSource.Picture })));

        TextureDoc.Save(new TextureDoc { Material = Material.Brick, Size = 32 }, P("wall.pxtex"));
        p.Add(P("wall.pxtex"));
        var godot = Exporter.Plan(p, new ExportProfile());
        Assert.True(godot.Ok, string.Join("; ", godot.Errors));
        Assert.Contains("wall_normal.png", godot.Files.Keys);
        Assert.Contains("normal_texture = ExtResource(\"2\")", godot.Text("wall_material.tres"));
        var generic = Exporter.Plan(p, new ExportProfile { Target = ExportTarget.Generic, OutputFolder = "export/g" });
        Assert.Contains("rockface_ao.png", generic.Files.Keys);
        Assert.Contains("rockface_height.png", generic.Files.Keys);
    }

    [Fact]
    public void Material_templates_make_textures()
    {
        var p = Project.Create(_dir, "g");
        var t = TemplateLibrary.BuiltIn().Single(x => x.Manifest.Id == "material-wood");
        var r = TemplateEngine.Create(t, new Dictionary<string, string> { ["size"] = "64" }, p, "materials", false);
        var tex = TextureDoc.Load(p.FullPath(r.Primary));
        Assert.Equal((Material.Wood, 64), (tex.Material, tex.Size));
        Assert.NotNull(AssetKinds.Of(AssetType.Texture).Picture!(p.FullPath(r.Primary), p));
    }
}
