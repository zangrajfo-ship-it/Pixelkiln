using System.Diagnostics;
using PixelForge.Core;

namespace PixelForge.Core.Tests;

/// <summary>Big documents and projects. Limits are generous (slow CI machines); they catch order-of-magnitude regressions.</summary>
[Trait("Category", "Load")]
public sealed class LoadTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "pf-load-" + Guid.NewGuid().ToString("N"));

    public LoadTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

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
        var h = new History<Sprite>(x => x.Clone(), 100, x => 4L * x.Width * x.Height * x.Layers.Count * x.Frames.Count, maxBytes: 256L * 1024 * 1024);
        for (int i = 0; i < 20; i++) h.Push(s);
        Assert.InRange(h.Bytes, size, 256L * 1024 * 1024);
        Assert.Equal(8, h.UndoCount);
        Assert.NotNull(h.Undo(s));

        var huge = new History<Sprite>(x => x.Clone(), 100, x => 4L * x.Width * x.Height * x.Layers.Count * x.Frames.Count, maxBytes: 1);
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
}
