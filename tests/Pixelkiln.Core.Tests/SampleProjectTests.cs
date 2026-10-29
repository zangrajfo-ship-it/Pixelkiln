using Pixelkiln.Core;

namespace Pixelkiln.Core.Tests;

public sealed class SampleProjectTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "pf-sample-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    static readonly SandboxInput Idle = new(false, false, false), Right = new(false, true, false);

    [Fact]
    public void Sample_project_is_complete_and_plays()
    {
        var created = SampleProject.Create(_dir);
        var p = Project.Load(created.FilePath);
        Assert.Empty(p.MissingAssets());
        Assert.Empty(p.DamagedAssets());
        Assert.Empty(p.BrokenReferences());
        Assert.True(File.Exists(Path.Combine(_dir, "README.txt")));
        Assert.Throws<IOException>(() => SampleProject.Create(_dir));                       // never into a folder with files

        var scenes = p.Assets.Where(a => a.Type == AssetType.Scene).ToDictionary(a => a.Name, a => Scene.Load(p.FullPath(a)));
        Assert.Equal(["cave", "meadow"], scenes.Keys.Order());
        foreach (var (name, s) in scenes)
        {
            Assert.DoesNotContain(SceneCheck.Run(s, p), i => i.Severity == "error");
            Assert.DoesNotContain(LogicGraph.Load(p.Resolve(s.LogicAssetId!.Value)!).Check(), m => !m.StartsWith("Warning:"));
        }
        Assert.True(Lighting.IsOn(scenes["cave"]));
        Assert.All(p.Assets.Where(a => a.Type == AssetType.Song), a => Assert.True(Song.Load(p.FullPath(a)).Instruments[(int)Channel.Noise].Kit));

        // walking right from the start picks up the first coins; with all five the exit leads to the cave
        var assets = new SceneAssets(p);
        var sb = new Sandbox(scenes["meadow"], assets, 320, 180);
        Assert.Null(sb.LogicError);
        for (int i = 0; i < 20; i++) sb.Step(16, Idle);                                     // land
        Assert.True(sb.OnGround);
        for (int i = 0; i < 60; i++) sb.Step(16, Right);
        Assert.True(sb.Logic!.Var("coins") >= 2, $"coins {sb.Logic.Var("coins")}");
        Assert.DoesNotContain(sb.Scene.Instances, i => i.Name == "Coin1");
        var exit = sb.Scene.Areas.First(a => a.Name == "exit");
        sb.Logic.Variables["coins"] = 5;
        (sb.X, sb.Y) = (exit.X + 8, exit.Y + exit.H);
        for (int i = 0; i < 60 && sb.RequestedScene is null; i++) sb.Step(16, Idle);
        Assert.Equal(p.Assets.First(a => a.Name == "cave" && a.Type == AssetType.Scene).Id, sb.RequestedScene);

        foreach (var target in new[] { ExportTarget.Godot4, ExportTarget.Generic })
        {
            var r = Exporter.Plan(p, new ExportProfile { Target = target });
            Assert.True(r.Ok, string.Join("\n", r.Errors));
        }

        if (Environment.GetEnvironmentVariable("PF_ART_PREVIEW") is string dir)
            foreach (var (name, s) in scenes)
                ImageIO.SavePng(Path.Combine(dir, $"sample-{name}.png"), SceneRenderer.Render(s, assets, 0, 0, s.Width, s.Height, 500), s.Width, s.Height);
    }
}
