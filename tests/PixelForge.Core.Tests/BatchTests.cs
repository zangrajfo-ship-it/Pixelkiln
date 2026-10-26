using PixelForge.Core;

namespace PixelForge.Core.Tests;

public sealed class BatchTests() : TempDirTest("batch")
{
    (Project P, List<AssetEntry> Sprites) Setup(int n)
    {
        var p = Project.Create(_dir, "G");
        var list = new List<AssetEntry>();
        for (int i = 0; i < n; i++)
        {
            string f = Path.Combine(_dir, $"s{i}.pxf");
            ProjectFile.Save(new Sprite(4, 2), f);
            list.Add(p.Add(f));
        }
        return (p, list);
    }

    [Fact]
    public void Rename_with_a_pattern_keeps_ids_and_refuses_collisions()
    {
        var (p, s) = Setup(12);
        var moved = Batch.Rename(p, [.. s.Select(a => a.Id)], "enemy_{n}");
        Assert.Equal("enemy_01", s[0].Name);
        Assert.Equal("enemy_12", s[11].Name);
        Assert.Equal(12, moved.Count);
        Assert.True(File.Exists(p.FullPath(s[11])));

        Batch.Rename(p, [s[0].Id, s[1].Id], "{name}_x");
        Assert.Equal("enemy_02_x", s[1].Name);
        // swap two names: works because of the temporary names
        Batch.Rename(p, [s[2].Id, s[3].Id], "{name}");
        Assert.Throws<ArgumentException>(() => Batch.Rename(p, [s[2].Id, s[3].Id], "same"));
        Assert.Throws<IOException>(() => Batch.Rename(p, [s[4].Id], "enemy_06"));
        Assert.Equal("enemy_05", s[4].Name);                               // nothing changed after a refused batch
    }

    [Fact]
    public void Tags_scale_and_convert()
    {
        var (p, s) = Setup(2);
        s[0].Tags = ["old", "keep"];
        Batch.Tag(p, s.Select(a => a.Id), ["enemy", "Enemy"], ["old"]);
        Assert.Equal(["keep", "enemy"], s[0].Tags);
        Assert.Equal(["enemy"], s[1].Tags);

        Batch.ScaleSprites(p, [s[0].Id], 300);
        Assert.Equal((12, 6), (ProjectFile.Load(p.FullPath(s[0])).Width, ProjectFile.Load(p.FullPath(s[0])).Height));
        Assert.Throws<ArgumentOutOfRangeException>(() => Batch.ScaleSprites(p, [s[0].Id], 100000));

        var png = Batch.Convert(p, [s[1].Id]).Single();
        Assert.Equal(AssetType.Image, png.Type);
        var back = Batch.Convert(p, [png.Id]).Single();
        Assert.Equal("s1 2.pxf", Path.GetFileName(back.Path));             // never overwrites the original sprite
        Assert.Equal(4, ProjectFile.Load(p.FullPath(back)).Width);
    }
}
