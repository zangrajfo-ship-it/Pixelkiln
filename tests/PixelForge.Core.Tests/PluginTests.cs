using PixelForge.Core;

namespace PixelForge.Core.Tests;

public sealed class PluginTests() : TempDirTest("plugins")
{
    static string Sample()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "samples", "plugins", "retro-pack"))) return Path.Combine(d.FullName, "samples", "plugins", "retro-pack");
        throw new DirectoryNotFoundException("samples/plugins/retro-pack");
    }

    void Write(string id, string json)
    {
        Directory.CreateDirectory(Path.Combine(_dir, id));
        File.WriteAllText(Path.Combine(_dir, id, "plugin.json"), json);
    }

    static string Manifest(string id = "demo", int api = 1, string extra = "", string contributions = "{}") =>
        $$"""{ "apiVersion": {{api}}, "id": "{{id}}", "name": "Demo", "version": "1.0" {{extra}}, "contributions": {{contributions}} }""";

    [Fact]
    public void Sample_plugin_loads_with_every_kind_of_contribution()
    {
        var p = PluginLoader.Load(Sample());
        var c = p.Manifest.Contributions;
        Assert.Equal(16, c.Palettes![0].Argb().Count);
        Assert.Equal(0xFF1D2B53, c.Palettes[0].Argb()[1]);
        Assert.Equal(["sprite", "tiles", "layout"], c.GeneratorPresets!.Select(g => g.Generator));
        Assert.Equal(ExportTarget.Godot4, c.ExportProfiles![0].Target);
        Assert.Equal(2, c.Macros![0].Commands.Count);

        var installed = PluginLoader.Install(Sample(), _dir);
        Assert.True(File.Exists(Path.Combine(_dir, "retro-pack", "plugin.json")));
        Assert.Equal("retro-pack", installed.Manifest.Id);
    }

    [Fact]
    public void Invalid_plugins_are_rejected_with_a_reason_and_the_rest_load()
    {
        Write("good", Manifest("good"));
        Write("future", Manifest("future", api: 2));
        Write("typo", Manifest("typo", extra: ", \"colour\": 1"));
        Write("wrong-folder", Manifest("other"));
        Write("bad-color", Manifest("bad-color", contributions: """{ "palettes": [{ "name": "x", "colors": ["#12"] }] }"""));
        Write("bad-preset", Manifest("bad-preset", contributions: """{ "generatorPresets": [{ "name": "x", "generator": "sprite", "settings": { "speed": "1" } }] }"""));
        Write("escape", Manifest("escape", contributions: """{ "exportProfiles": [{ "name": "x", "target": "Godot4", "outputFolder": "../../out" }] }"""));
        Write("dll", Manifest("dll", contributions: """{ "assembly": "evil.dll" }"""));
        Directory.CreateDirectory(Path.Combine(_dir, "empty"));

        var (ok, bad) = PluginLoader.LoadAll(_dir);
        Assert.Equal(["good"], ok.Select(p => p.Manifest.Id));
        string Why(string folder) => bad.Single(b => Path.GetFileName(b.Folder) == folder).Reason;
        Assert.Contains("API 2 is not supported", Why("future"));
        Assert.Contains("colour", Why("typo"));
        Assert.Contains("named after the id", Why("wrong-folder"));
        Assert.Contains("not a colour", Why("bad-color"));
        Assert.Contains("unknown setting “speed”", Why("bad-preset"));
        Assert.Contains("inside the project", Why("escape"));
        Assert.Contains("assembly", Why("dll"));
        Assert.Contains("No plugin.json", Why("empty"));
    }
}
