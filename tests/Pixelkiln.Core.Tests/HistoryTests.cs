using System.Text;
using Pixelkiln.Core;

namespace Pixelkiln.Core.Tests;

public sealed class HistoryTests() : TempDirTest("history")
{
    [Fact]
    public void Records_dedupes_restores_and_survives_reload()
    {
        var p = Project.Create(_dir, "G");
        var s = new Sprite(4, 4);
        ProjectFile.Save(s, P("a.pxf"));
        var a = p.Add(P("a.pxf"));
        var store = new VersionStore(p);
        var v1 = store.Record(a.Id, "first")!;
        Assert.Null(store.Record(a.Id));                     // same content: no new version

        s.Layers[0].Cels[0][0] = 0xFFFF0000;
        ProjectFile.Save(s, P("a.pxf"));
        var v2 = store.Record(a.Id)!;
        Assert.Equal([v2.Hash, v1.Hash], store.Versions(a.Id).Select(v => v.Hash));

        store.Restore(a.Id, v1);
        Assert.Equal(store.Read(v1), File.ReadAllBytes(P("a.pxf")));
        var reloaded = new VersionStore(Project.Load(p.FilePath));
        var hashes = reloaded.Versions(a.Id).Select(v => v.Hash).ToList();
        Assert.Equal([v1.Hash, v2.Hash, v1.Hash], hashes.Take(3));   // restore is itself a version; "before restore" equals v2
        Assert.Equal(2, Directory.GetFiles(P(".history", "objects")).Length);   // stored once per content
    }

    [Fact]
    public void Limits_drop_the_oldest_versions_and_their_objects()
    {
        var p = Project.Create(_dir, "G");
        p.Settings.HistoryMaxVersions = 3;
        File.WriteAllText(P("pal.hex"), "000000");
        var a = p.Add(P("pal.hex"));
        var store = new VersionStore(p);
        for (int i = 0; i < 6; i++)
        {
            File.WriteAllText(P("pal.hex"), $"{i:X6}");
            store.Record(a.Id);
        }
        Assert.Equal(3, store.Versions(a.Id).Count);
        Assert.Equal(3, Directory.GetFiles(P(".history", "objects")).Length);
        Assert.Equal("000005", Encoding.UTF8.GetString(store.Read(store.Versions(a.Id)[0])));

        File.WriteAllText(P(".history", "objects", store.Versions(a.Id)[1].Hash), "broken");
        Assert.Throws<InvalidDataException>(() => store.Read(store.Versions(a.Id)[1]));
    }

    [Fact]
    public void Diff_shows_changed_pixels_and_fields()
    {
        var a = new VersionDiff.Picture([0xFF000000, 0xFF000000], 2, 1);
        var b = new VersionDiff.Picture([0xFF000000, 0xFFFFFFFF, 0xFFFFFFFF], 3, 1);
        var (img, changed) = VersionDiff.Pixels(a, b);
        Assert.Equal((3, 2), (img.Width, changed));
        Assert.Equal(0xFFFF00FF, img.Pixels[1]);

        var s = new Sprite(2, 2);
        var before = Encoding.UTF8.GetBytes(ProjectFile.Serialize(s));
        s.Tags.Add(new FrameTag { Name = "run" });
        s.Layers[0].Name = "Ink";
        var after = Encoding.UTF8.GetBytes(ProjectFile.Serialize(s));
        var lines = VersionDiff.Fields(before, after);
        Assert.Contains(lines, l => l.Contains("Name") && l.Contains("Ink"));
        Assert.Contains(lines, l => l.StartsWith('+') && l.Contains("Tags"));
        Assert.Equal(2, VersionDiff.Render(AssetType.Sprite, after)!.Width);
        Assert.Equal(["identical"], VersionDiff.Fields(after, after));
    }
}
