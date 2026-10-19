using System.Text;
using PixelForge.Core;

namespace PixelForge.Core.Tests;

public sealed class HistoryTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "pf-history-" + Guid.NewGuid().ToString("N"));

    public HistoryTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    string P(string name) => Path.Combine(_dir, name);

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
        Assert.Equal(2, Directory.GetFiles(Path.Combine(_dir, ".history", "objects")).Length);   // stored once per content
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
        Assert.Equal(3, Directory.GetFiles(Path.Combine(_dir, ".history", "objects")).Length);
        Assert.Equal("000005", Encoding.UTF8.GetString(store.Read(store.Versions(a.Id)[0])));

        File.WriteAllText(Path.Combine(_dir, ".history", "objects", store.Versions(a.Id)[1].Hash), "broken");
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

public class ProceduralTests
{
    static readonly uint[] Pal = [0xFF101010, 0xFF406020, 0xFF60A030, 0xFFA0E060];

    [Fact]
    public void Same_seed_same_result_different_seed_different_result()
    {
        Assert.Equal(Procedural.Sprite(7, 16, 16, Pal), Procedural.Sprite(7, 16, 16, Pal));
        Assert.NotEqual(Procedural.Sprite(7, 16, 16, Pal), Procedural.Sprite(8, 16, 16, Pal));
        Assert.Equal(Procedural.Tiles(1, 8, Pal, TilePattern.Bricks).Pixels, Procedural.Tiles(1, 8, Pal, TilePattern.Bricks).Pixels);
        Assert.Equal(Procedural.Cave(3, 20, 10), Procedural.Cave(3, 20, 10));
        Assert.Equal(Procedural.Platforms(3, 30, 10), Procedural.Platforms(3, 30, 10));
        Assert.Equal(Rng.Seed("forest"), Rng.Seed("forest"));
        Assert.Equal(42UL, Rng.Seed("42"));
        Assert.Equal(new Rng(1).NextULong(), new Rng(1).NextULong());
    }

    [Fact]
    public void Generated_content_has_the_promised_shape()
    {
        var sp = Procedural.Sprite(11, 12, 12, Pal);
        for (int y = 0; y < 12; y++)
            for (int x = 0; x < 6; x++) Assert.Equal(sp[y * 12 + x], sp[y * 12 + 11 - x]);   // mirrored
        Assert.All(sp, c => Assert.True(c == 0 || Pal.Contains(c)));
        Assert.Contains(sp, c => c != 0);

        var (tiles, w, h) = Procedural.Tiles(5, 8, Pal, TilePattern.Noise, 3);
        Assert.Equal((24, 8), (w, h));
        Assert.All(tiles, c => Assert.Contains(c, Pal));

        var cave = Procedural.Cave(9, 16, 12);
        Assert.True(cave[0] && cave[15] && cave[16 * 11] && cave[^1]);                  // solid border
        Assert.Contains(false, cave);

        var map = new Tilemap(16, 12, 8) { Tileset = new Tileset(new uint[8 * 8 * 16], 32, 32, 8) };
        Procedural.Fill(map, map.Layers[0], cave, null, 5);
        Assert.Equal(cave.Count(c => c), map.Layers[0].Tiles.Count(t => t == 5));
        Assert.Throws<ArgumentException>(() => Procedural.Sprite(1, 8, 8, [0xFF000000]));
    }
}
