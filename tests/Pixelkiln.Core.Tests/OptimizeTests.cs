using System.Diagnostics;
using Pixelkiln.Core;

namespace Pixelkiln.Core.Tests;

public sealed class OptimizeTests() : TempDirTest("opt")
{
    const uint R = 0xFFFF0000, B = 0xFF0000FF, K = 0xFF000000;

    static void NoOverlap(IReadOnlyList<(int X, int Y, int W, int H)> r, int binW, int binH)
    {
        for (int i = 0; i < r.Count; i++)
        {
            Assert.True(r[i].X >= 0 && r[i].Y >= 0 && r[i].X + r[i].W <= binW && r[i].Y + r[i].H <= binH, $"rect {i} outside");
            for (int j = i + 1; j < r.Count; j++)
                Assert.False(r[i].X < r[j].X + r[j].W && r[j].X < r[i].X + r[i].W && r[i].Y < r[j].Y + r[j].H && r[j].Y < r[i].Y + r[i].H, $"rects {i} and {j} overlap");
        }
    }

    [Fact]
    public void MaxRects_places_without_overlap_and_reports_what_does_not_fit()
    {
        var rng = new Random(4);
        var sizes = Enumerable.Range(0, 300).Select(_ => (rng.Next(4, 40), rng.Next(4, 40))).ToList();
        var pos = Atlas.MaxRects(sizes, 512, 512);
        Assert.All(pos, p => Assert.NotNull(p));                                   // ~150k px in 262k: all fit
        NoOverlap([.. sizes.Select((s, i) => (pos[i]!.Value.X, pos[i]!.Value.Y, s.Item1, s.Item2))], 512, 512);
        var big = Atlas.MaxRects([(300, 300), (300, 300)], 512, 512);
        Assert.Equal(1, big.Count(p => p is not null));
    }

    [Fact]
    public void Pack_trims_stores_repeats_once_and_splits_into_pages()
    {
        var a = new uint[8 * 8]; a[2 * 8 + 3] = R; a[4 * 8 + 5] = B;               // content in x 3–5, y 2–4
        var b = new uint[8 * 8]; b[0] = R; b[2 * 8 + 2] = B;                        // the same 3×3 picture at another offset
        var r = Atlas.Pack([new("a", a, 8, 8), new("empty", new uint[16], 4, 4), new("b", b, 8, 8)], maxSize: 64);
        Assert.Single(r.Pages);
        var fa = r.Frames[0];
        Assert.Equal((3, 3, 3, 2, 8, 8), (fa.W, fa.H, fa.TrimX, fa.TrimY, fa.SourceW, fa.SourceH));
        Assert.Equal(R, r.Pages[0].Pixels[fa.Y * r.Pages[0].W + fa.X]);
        Assert.Equal(B, r.Pages[0].Pixels[(fa.Y + 2) * r.Pages[0].W + fa.X + 2]);
        Assert.Equal(-1, r.Frames[1].Page);                                          // empty frame: not stored
        Assert.Equal(("a", fa.X, fa.Y, 0, 0), (r.Frames[2].SameAs, r.Frames[2].X, r.Frames[2].Y, r.Frames[2].TrimX, r.Frames[2].TrimY));
        Assert.True(System.Numerics.BitOperations.IsPow2(r.Pages[0].W));

        var many = Enumerable.Range(0, 20).Select(i => new Atlas.Item($"t{i}", Enumerable.Repeat(0xFF000000u | (uint)i, 30 * 30).ToArray(), 30, 30));
        var paged = Atlas.Pack(many, maxSize: 64);
        Assert.True(paged.Pages.Count > 1);
        Assert.All(paged.Pages, pg => Assert.True(pg.W <= 64 && pg.H <= 64));
        Assert.Throws<InvalidDataException>(() => Atlas.Pack([new("huge", Enumerable.Repeat(R, 100 * 100).ToArray(), 100, 100)], maxSize: 64));
        Assert.Contains("\"sameAs\": \"a\"", Atlas.Json(r, i => $"p{i}.png"));
    }

    [Fact]
    public void Packing_1000_sprites_is_fast_and_overlap_free()
    {
        var rng = new Random(7);
        var items = Enumerable.Range(0, 1000).Select(i =>
        {
            int w = rng.Next(8, 33), h = rng.Next(8, 33);
            var px = new uint[w * h];
            for (int k = 0; k < px.Length; k++) px[k] = 0xFF000000u | (uint)rng.Next(0x1000000);
            return new Atlas.Item($"s{i}", px, w, h);
        }).ToList();
        var sw = Stopwatch.StartNew();
        var r = Atlas.Pack(items, maxSize: 1024);
        Assert.True(sw.ElapsedMilliseconds < 10000, $"{sw.ElapsedMilliseconds} ms");
        foreach (var page in Enumerable.Range(0, r.Pages.Count))
            NoOverlap([.. r.Frames.Where(f => f.Page == page).Select(f => (f.X, f.Y, f.W, f.H))], r.Pages[page].W, r.Pages[page].H);
        var f9 = r.Frames[9];
        Assert.Equal(items[9].Pixels[0], r.Pages[f9.Page].Pixels[f9.Y * r.Pages[f9.Page].W + f9.X]);
    }

    /// <summary>A project with a used sprite, an unused image, two identical images and a too large picture.</summary>
    Project Game()
    {
        var p = Project.Create(_dir, "O");
        var s = new Sprite(8, 8);
        s.AddFrame(1, copyFrom: 0);                                                // a repeated frame
        s.Cel(0, 0)[9] = R;
        s.Cel(0, 1)[9] = R;
        ProjectFile.Save(s, P("hero.pxf"));
        var hero = p.Add(P("hero.pxf"));
        Character.Save(new Character { SpriteAssetId = hero.Id }, P("hero.pxchar"));
        var ch = p.Add(P("hero.pxchar"));
        var scene = new Scene();
        scene.Instances.Add(new SceneInstance { AssetId = ch.Id, LayerId = scene.Layers[0].Id });
        Scene.Save(scene, P("level.pxscene"));
        p.Add(P("level.pxscene"));
        ImageIO.SavePng(P("a.png"), [R, B], 2, 1);
        ImageIO.SavePng(P("b.png"), [R, B], 2, 1);
        ImageIO.SavePng(P("huge.png"), new uint[3000 * 2], 3000, 2);
        foreach (var f in new[] { "a.png", "b.png", "huge.png" }) p.Add(P(f));
        return p;
    }

    [Fact]
    public void Report_finds_unused_identical_and_oversized_assets_and_atlas_savings()
    {
        var p = Game();
        var r = Optimizer.Run(p);
        Assert.Equal(["a.png", "b.png", "huge.png"], r.Unused.Select(a => a.Path));   // the sprite is used through the character
        Assert.Equal(["a.png", "b.png"], r.Duplicates.Single().Select(a => a.Path));
        Assert.Contains(r.SizeProblems, x => x.Asset.Path == "huge.png");
        Assert.Equal(2, r.DuplicateFrames);                                            // hero frame 1 and b.png
        Assert.True(r.AtlasPages >= 1);
        Assert.Contains("Identical files: 1", r.Text());

        var e = Exporter.Plan(p, new ExportProfile { Target = ExportTarget.Generic, Atlas = true });
        Assert.True(e.Ok, string.Join("\n", e.Errors));
        Assert.Contains("atlas/atlas0.png", e.Files.Keys);
        Assert.Contains("\"key\": \"hero#1\"", e.Text("atlas/atlas.json"));
    }

    [Fact]
    public void Style_guide_checks_grid_colours_palette_outline_and_strokes()
    {
        var p = Project.Create(_dir, "S");
        var s = new Sprite(10, 8);
        var cel = s.Cel(0, 0);
        for (int y = 2; y < 5; y++) for (int x = 2; x < 5; x++) cel[y * 10 + x] = K;     // 3×3 black square …
        cel[3 * 10 + 3] = R;                                                           // … with a red centre
        cel[0] = B;                                                                    // a lone blue pixel: an edge that is not black
        ProjectFile.Save(s, P("s.pxf"));
        p.Add(P("s.pxf"));
        PaletteIO.Save(P("pal.hex"), [K, R]);
        var pal = p.Add(P("pal.hex"));
        var v = new VectorDoc { Width = 10, Height = 10 };
        v.Shapes.Add(new VShape { Kind = VShapeKind.Rect, W = 4, H = 4, Stroke = VPaint.Solid(K), StrokeWidth = 3 });
        VectorDoc.Save(v, P("v.pxvec"));
        p.Add(P("v.pxvec"));

        Assert.Empty(StyleCheck.Run(p, new StyleGuide()));
        var issues = StyleCheck.Run(p, new StyleGuide { Grid = 8, MaxColors = 2, Palettes = [pal.Id], Outline = K, VectorStroke = 1, MaxSize = 8 }).Select(i => i.Message).ToList();
        Assert.Contains(issues, m => m.Contains("multiple of the 8 px grid"));
        Assert.Contains(issues, m => m.Contains("uses 3 colours"));
        Assert.Contains(issues, m => m.Contains("#0000FF"));
        Assert.Contains(issues, m => m.StartsWith("1 edge pixel"));
        Assert.Contains(issues, m => m.Contains("stroke other than 1"));
        Assert.Contains(issues, m => m.Contains("larger than 8 px"));

        p.Settings.Style = new StyleGuide { Grid = 16, Outline = K, Palettes = [pal.Id] };
        p.Save();
        var back = Project.Load(p.FilePath);
        Assert.Equal((16, (uint?)K, pal.Id), (back.Settings.Style.Grid, back.Settings.Style.Outline, back.Settings.Style.Palettes[0]));
        File.WriteAllText(p.FilePath, File.ReadAllText(p.FilePath).Replace("\"Style\"", "\"Unused\""));   // a project from before the guide
        Assert.True(Project.Load(p.FilePath).Settings.Style.IsEmpty);
    }
}
