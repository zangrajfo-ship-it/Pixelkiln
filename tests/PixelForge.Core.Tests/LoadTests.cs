using System.Diagnostics;
using PixelForge.Core;

namespace PixelForge.Core.Tests;

/// <summary>Big documents and projects. Limits are generous (slow CI machines); they catch order-of-magnitude regressions.</summary>
[Trait("Category", "Load")]
[Collection("Load")]
public sealed class LoadTests() : TempDirTest("load")
{
    /// <summary>Undo cost of a sprite snapshot: 4 bytes per pixel of every cel.</summary>
    static long Bytes(Sprite x) => 4L * x.Width * x.Height * x.Layers.Count * x.Frames.Count;

    static long Ms(Action a)
    {
        a();   // warm-up (JIT)
        var sw = Stopwatch.StartNew();
        a();
        return sw.ElapsedMilliseconds;
    }

    [Fact]
    public void Flattening_a_1024_sprite_with_four_layers_is_fast()
    {
        var s = new Sprite(1024, 1024);
        for (int i = 1; i < 4; i++) s.AddLayer(i, $"L{i}");
        foreach (var l in s.Layers) Array.Fill(l.Cels[0], 0x80FF0000u);
        Assert.InRange(Ms(() => Compositor.Flatten(s, 0)), 0, 500);
    }

    [Fact]
    public void Undo_of_a_big_sprite_stays_inside_the_memory_budget()
    {
        var s = new Sprite(512, 512);
        for (int i = 1; i < 32; i++) s.AddFrame(i);   // 32 MB per snapshot
        long size = 4L * 512 * 512 * 32;
        var h = new History<Sprite>(x => x.Clone(), 100, Bytes, maxBytes: 256L * 1024 * 1024);
        for (int i = 0; i < 20; i++) h.Push(s);
        Assert.InRange(h.Bytes, size, 256L * 1024 * 1024);
        Assert.Equal(8, h.UndoCount);
        Assert.NotNull(h.Undo(s));

        var huge = new History<Sprite>(x => x.Clone(), 100, Bytes, maxBytes: 1);
        huge.Push(s);
        huge.Push(s);
        Assert.Equal(1, huge.UndoCount);   // over budget, but the newest step is kept so Ctrl+Z still works
    }

    [Fact]
    public void Project_with_5000_assets_loads_and_searches_quickly()
    {
        var p = Project.Create(_dir, "Big");
        for (int i = 0; i < 5000; i++)
            p.Assets.Add(new AssetEntry { Id = Guid.NewGuid(), Type = i % 2 == 0 ? AssetType.Sprite : AssetType.Sound, Path = $"folder{i % 50}/asset{i}.{(i % 2 == 0 ? "pxf" : "sfx")}", Tags = i % 7 == 0 ? ["tree"] : [] });
        p.Save();
        Project? q = null;
        Assert.InRange(Ms(() => q = Project.Load(p.FilePath)), 0, 1000);
        Assert.InRange(Ms(() => q!.Search("tree").ToList()), 0, 100);
        Assert.Equal(715, q!.Search("tree").Count());
        Assert.InRange(Ms(() => q.Search("asset4999", AssetType.Sound).ToList()), 0, 100);
    }

    [Fact]
    public void Game_view_with_500_objects_renders_within_a_frame_budget()
    {
        var p = Project.Create(_dir, "G");
        var sp = new Sprite(16, 16);
        Array.Fill(sp.Layers[0].Cels[0], 0xFF00FF00u);
        ProjectFile.Save(sp, Path.Combine(_dir, "o.pxf"));
        var asset = p.Add(Path.Combine(_dir, "o.pxf"));
        var s = new Scene { Width = 4096, Height = 512 };
        for (int i = 0; i < 500; i++) s.Instances.Add(new SceneInstance { AssetId = asset.Id, LayerId = s.Layers[0].Id, X = i * 8 % 4096, Y = i % 300 });
        var sb = new Sandbox(s, new SceneAssets(p), 320, 180);
        Assert.InRange(Ms(() => sb.Render(boxes: true)), 0, 100);
    }

    // ---------- formats of phases 17–20 ----------

    [Fact]
    public void Big_animation_library_and_rig_stay_fast()
    {
        var lib = new AnimLibrary();
        for (int c = 0; c < 200; c++)
            lib.Clips.Add(new AnimClip
            {
                Name = $"c{c}",
                Tracks = [.. Enumerable.Range(0, 30).Select(t => new AnimTrack { Target = $"p{t}", Property = AnimProperty.Rotation,
                    Keys = [.. Enumerable.Range(0, 20).Select(k => new AnimKey(k * 50, k, AnimEase.Bezier, 0.42, 0, 0.58, 1))] })],
            });
        Assert.InRange(Ms(() => AnimLibrary.Deserialize(AnimLibrary.Serialize(lib))), 0, 3000);
        var parts = Enumerable.Range(0, 30).Select(i => new RigPart { Name = $"p{i}", W = 4, H = 4, Parent = i == 0 ? "" : $"p{i - 1}", OffsetX = 2 }).ToList();
        var frame = Enumerable.Repeat(0xFFFF0000u, 64 * 64).ToArray();
        var dst = new uint[320 * 180];
        Assert.InRange(Ms(() => { for (int t = 0; t < 300; t++) Rig.Draw(dst, 320, 180, frame, 64, 64, parts, lib.Clips[7], t * 16, 100, 90); }), 0, 3000);
    }

    [Fact]
    public void Big_logic_graph_checks_and_runs_fast()
    {
        var g = new LogicGraph();
        for (int e = 0; e < 200; e++)
        {
            var ev = LogicGraph.NewNode(LogicKind.Timer, $"t{e}");
            ev.Params["repeat"] = "yes";
            ev.Params["ms"] = $"{100 + e}";
            g.Nodes.Add(ev);
            string prev = ev.Id;
            for (int k = 0; k < 9; k++)
            {
                var n = LogicGraph.NewNode(k % 3 == 1 ? LogicKind.Compare : LogicKind.SetVariable, $"n{e}_{k}");
                g.Nodes.Add(n);
                g.Links.Add(new LogicLink(prev, "out", n.Id));
                prev = n.Id;
            }
        }
        // compare nodes continue on their true port
        g.Links = [.. g.Links.Select(l => g.Node(l.From)!.Kind == LogicKind.Compare ? l with { Port = "true" } : l)];
        Assert.InRange(Ms(() => g.Check()), 0, 2000);
        Assert.InRange(Ms(() =>
        {
            var r = new LogicRuntime(g);
            for (int f = 0; f < 600; f++) r.Step(f * 16, new LogicEvents([], []));
        }), 0, 3000);
    }
}

/// <summary>Timings are only meaningful when nothing else runs at the same time.</summary>
[CollectionDefinition("Load", DisableParallelization = true)]
public sealed class LoadCollection;
