using System.IO.Compression;
using PixelForge.Core;

namespace PixelForge.Core.Tests;

public sealed class TemplateTests() : TempDirTest("tpl")
{
    Project NewProject(string name = "game")
    {
        string root = P(name);
        Directory.CreateDirectory(root);
        return Project.Create(root, name);
    }

    static TemplateInfo Builtin(string id) => TemplateLibrary.BuiltIn().Single(t => t.Manifest.Id == id);

    [Fact]
    public void Every_built_in_template_creates_valid_assets_as_kit_and_as_starting_point()
    {
        var all = TemplateLibrary.BuiltIn().ToList();
        Assert.True(all.Count >= 20);
        Assert.Contains(all, t => t.Manifest.Kind == TemplateKind.Character);
        Assert.Contains(all, t => t.Manifest.Kind == TemplateKind.Level);
        Assert.Contains(all, t => t.Manifest.Kind == TemplateKind.Music);
        foreach (var t in all)
            foreach (bool kit in new[] { true, false })
            {
                var p = NewProject($"{t.Manifest.Id}-{kit}");
                var r = TemplateEngine.Create(t, null, p, "made", kit);
                Assert.NotEmpty(r.Created);
                Assert.Empty(p.DamagedAssets());
                Assert.Empty(p.BrokenReferences());
                foreach (var a in r.Created) AssetLoader.Probe(a.Type, p.FullPath(a));
                if (r.Primary.Type == AssetType.Scene)
                    Assert.DoesNotContain(SceneCheck.Run(Scene.Load(p.FullPath(r.Primary)), p), i => i.Severity == "error");
            }
    }

    [Fact]
    public void Same_parameters_give_the_same_files_and_the_kit_is_bigger_than_the_start()
    {
        // asset and object ids are fresh GUIDs by design; everything else must match byte for byte
        var guid = new System.Text.RegularExpressions.Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}");
        string[] Run(string project, string seed)
        {
            var p = NewProject(project);
            var r = TemplateEngine.Create(Builtin("level-forest"), new Dictionary<string, string> { ["seed"] = seed, ["name"] = "w1" }, p, "", fullKit: true);
            return [.. r.Created.OrderBy(a => a.Path).Select(a => guid.Replace(Convert.ToBase64String(File.ReadAllBytes(p.FullPath(a))) is var b64 && a.Type is AssetType.Image ? b64
                : File.ReadAllText(p.FullPath(a)), "ID"))];
        }
        var a = Run("a", "moss");
        Assert.Equal(a, Run("b", "moss"));
        Assert.NotEqual(a, Run("c", "other"));

        var p = NewProject("start");
        var start = TemplateEngine.Create(Builtin("level-forest"), null, p, "", fullKit: false);
        Assert.Equal(3, start.Created.Count);                              // tileset, map, level
        var kit = TemplateEngine.Create(Builtin("level-forest"), null, p, "kit", fullKit: true);
        Assert.True(kit.Created.Count > start.Created.Count);
        var level = Scene.Load(p.FullPath(kit.Primary));
        Assert.Contains(level.Areas, x => x.Kind == AreaKind.Spawn);
        Assert.Contains(level.Instances, i => i.Name == "Player");
        Assert.NotNull(level.MusicAssetId);
        Assert.False(Lighting.IsOn(level));                                // daylight forest

        var cave = Scene.Load(p.FullPath(TemplateEngine.Create(Builtin("level-cave"), null, p, "cave", fullKit: false).Primary));
        Assert.Equal(0xFF302838u, cave.Ambient);
        Assert.Equal(cave.Areas.Count(x => x.Kind is AreaKind.Spawn or AreaKind.Checkpoint), cave.Lights.Count);   // torches
        var ice = Scene.Load(p.FullPath(TemplateEngine.Create(Builtin("level-ice"), null, p, "ice", fullKit: false).Primary));
        Assert.Equal((EmitterPreset.Snow, ice.Width), (ice.Emitters.Single().Preset, ice.Emitters[0].W));
    }

    [Fact]
    public void Character_template_has_states_boxes_and_properties_and_uses_a_project_palette()
    {
        var p = NewProject();
        File.WriteAllText(Path.Combine(p.Root, "pal.hex"), "000000\nffffff\nff0000\n00ff00\n");
        p.Add(Path.Combine(p.Root, "pal.hex"));
        var r = TemplateEngine.Create(Builtin("platformer-hero"), new Dictionary<string, string> { ["size"] = "16", ["palette"] = "pal" }, p, "chars", false);
        var c = Character.Load(p.FullPath(r.Primary));
        var s = ProjectFile.Load(p.Resolve(c.SpriteAssetId!.Value)!);
        Assert.Equal((16, 16, 4), (s.Width, s.Height, s.Palette.Count));
        Assert.Equal(["idle", "run", "jump", "fall", "attack", "hurt", "death"], s.Tags.Select(t => t.Name));
        Assert.Equal(16, s.Frames.Count);                                  // 2 + 4 + 1 + 1 + 3 + 1 + 4
        Assert.Equal(("run", 2, 5), (s.Tags[1].Name, s.Tags[1].From, s.Tags[1].To));
        Assert.Contains(c.Boxes, b => b.Kind == BoxKind.Hit && b.State == "attack");
        Assert.Equal((8, 16), (c.OriginX, c.OriginY));
        Assert.Equal("230", c.Properties["jump"]);
    }

    [Fact]
    public void Saved_template_copies_assets_and_repoints_their_references()
    {
        var p = NewProject("src");
        var made = TemplateEngine.Create(Builtin("platformer-hero"), null, p, "", false);
        string scope = P("user-templates");
        var t = TemplateLibrary.SaveFromAssets(p, [made.Primary.Id], scope, TemplateSource.User, "my-hero", "My hero", "1.0", TemplateKind.Character, "mine", ["hero"]);
        Assert.Equal(2, t.Manifest.Steps.Count);                          // the sprite came along as a dependency
        Assert.Throws<IOException>(() => TemplateLibrary.SaveFromAssets(p, [made.Primary.Id], scope, TemplateSource.User, "my-hero", "My hero", "1.0", TemplateKind.Character, null, []));

        var q = NewProject("dst");
        var r = TemplateEngine.Create(t, null, q, "copied", false);
        var c = Character.Load(q.FullPath(r.Primary));
        Assert.NotEqual(made.Created.First(a => a.Type == AssetType.Sprite).Id, c.SpriteAssetId);
        Assert.Equal(AssetType.Sprite, q.Find(c.SpriteAssetId!.Value)!.Type);     // points at the copy in the new project
        Assert.Empty(q.BrokenReferences());
    }

    [Fact]
    public void Packs_round_trip_and_bad_templates_or_paths_are_refused()
    {
        string pack = P("a.pxtplpack");
        TemplateLibrary.ExportPack(pack, [Builtin("asset-door"), Builtin("music-chiptune-loop")]);
        string scope = P("scope");
        var installed = TemplateLibrary.ImportPack(pack, scope, TemplateSource.User);
        Assert.Equal(["asset-door", "music-chiptune-loop"], installed.Select(t => t.Manifest.Id).Order());
        byte[] first = File.ReadAllBytes(pack);
        TemplateLibrary.ExportPack(pack, [Builtin("asset-door"), Builtin("music-chiptune-loop")]);
        Assert.Equal(first, File.ReadAllBytes(pack));                      // reproducible

        string evil = P("evil.pxtplpack");
        using (var z = ZipFile.Open(evil, ZipArchiveMode.Create)) z.CreateEntry("../outside.txt");
        Assert.Throws<InvalidDataException>(() => TemplateLibrary.ImportPack(evil, scope, TemplateSource.User));

        string bad = P("bad.pxtplpack");
        using (var z = ZipFile.Open(bad, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(z.CreateEntry("ok@1.0/template.json").Open())) w.Write(TemplateLoader.Serialize(Builtin("asset-ladder").Manifest));
            using (var w = new StreamWriter(z.CreateEntry("xx@1.0/template.json").Open()))
                w.Write("""{ "apiVersion": 1, "id": "xx", "name": "X", "version": "1.0", "kind": "Asset", "primary": "a", "steps": [ { "id": "a", "type": "sprite", "settings": { "colour": "1" } } ] }""");
        }
        var ex = Assert.Throws<InvalidDataException>(() => TemplateLibrary.ImportPack(bad, scope, TemplateSource.User));
        Assert.Contains("unknown setting “colour”", ex.Message);
        Assert.False(Directory.Exists(Path.Combine(scope, "asset-ladder@1.0")));   // nothing from a bad pack is installed
    }

    [Theory]
    [InlineData("""{ "apiVersion": 2, "id": "t", "name": "T", "version": "1.0", "kind": "Asset", "primary": "a", "steps": [ { "id": "a", "type": "song" } ] }""", "API 2")]
    [InlineData("""{ "apiVersion": 1, "id": "t1", "name": "T", "version": "1.0", "kind": "Asset", "primary": "b", "steps": [ { "id": "a", "type": "song" } ] }""", "primary")]
    [InlineData("""{ "apiVersion": 1, "id": "t2", "name": "T", "version": "1.0", "kind": "Asset", "primary": "a", "steps": [ { "id": "a", "type": "python" } ] }""", "unknown type")]
    [InlineData("""{ "apiVersion": 1, "id": "t3", "name": "{x}", "version": "1.0", "kind": "Asset", "primary": "a", "steps": [ { "id": "a", "type": "song", "name": "{x}" } ] }""", "not a parameter")]
    [InlineData("""{ "apiVersion": 1, "id": "t4", "name": "T", "version": "1.0", "kind": "Asset", "primary": "b", "steps": [ { "id": "b", "type": "character", "settings": { "spriteStep": "later" } } ] }""", "earlier step")]
    [InlineData("""{ "apiVersion": 1, "id": "t5", "name": "T", "version": "1.0", "kind": "Asset", "primary": "a", "steps": [ { "id": "a", "type": "file", "file": "x.pxf" } ] }""", "Built-in templates cannot copy")]
    public void Invalid_manifests_are_refused_with_a_reason(string json, string reason)
    {
        var ex = Assert.Throws<InvalidDataException>(() => TemplateLoader.Validate(TemplateLoader.Parse(json), null));
        Assert.Contains(reason, ex.Message);
    }

    /// <summary>Pictures of what the built-in templates make (only with PF_PREVIEW_DIR set; for looking, not asserting).</summary>
    [Trait("Category", "Tool")]   // a helper for people, not a check: without its variable it passes doing nothing
    [Fact]
    public void WriteTemplatePreviews()
    {
        if (Environment.GetEnvironmentVariable("PF_PREVIEW_DIR") is not { Length: > 0 } outDir) return;
        Directory.CreateDirectory(outDir);
        foreach (var t in TemplateLibrary.BuiltIn())
        {
            var p = NewProject("prev-" + t.Manifest.Id);
            var r = TemplateEngine.Create(t, null, p, "", true);
            if (AssetKinds.Of(r.Primary.Type).Picture?.Invoke(p.FullPath(r.Primary), p) is var (px, w, h))
                ImageIO.SavePng(Path.Combine(outDir, t.Manifest.Id + ".png"), px, w, h);
        }
    }
}
