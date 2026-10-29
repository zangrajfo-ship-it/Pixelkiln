using Pixelkiln.Core;
using static Pixelkiln.Core.Tests.TestFiles;

namespace Pixelkiln.Core.Tests;

public sealed class DocumentFileTests() : TempDirTest("doc")
{
    [Fact]
    public void Failed_write_leaves_the_original_untouched()
    {
        string file = P("a.pxs");
        Song.Save(new Song { Bpm = 99 }, file);
        Directory.CreateDirectory(file + ".tmp");                   // the temp file cannot be created
        Assert.ThrowsAny<Exception>(() => Song.Save(new Song { Bpm = 150 }, file));   // IOException or UnauthorizedAccessException
        Assert.Equal(99, Song.Load(file).Bpm);
    }

    [Fact]
    public void Migrated_file_gets_one_backup_of_the_old_version()
    {
        string file = P("song.pxs");
        File.Copy(Fixture("song.v1.pxs"), file);
        var song = Song.Load(file);
        Song.Save(song, file);
        Song.Save(song, file);                                       // second save: no new backup
        Assert.True(File.Exists(file + ".v1.bak"));
        Assert.Equal(File.ReadAllText(Fixture("song.v1.pxs")), File.ReadAllText(file + ".v1.bak"));
        Assert.Equal(Song.Version, DocumentFile.PeekVersion(File.ReadAllText(file)));
        Assert.False(Song.Load(file).Instruments[(int)Channel.Noise].Kit);   // migrated: keeps its pitched noise
        Assert.Single(Directory.GetFiles(_dir, "*.bak"));
        Assert.Equal(140, Song.Load(file).Bpm);                      // data survived the migration
    }

    [Fact]
    public void Sound_v1_is_saved_as_v2_with_backup_and_reloads()
    {
        string file = P("s.sfx");
        File.Copy(Fixture("sound.v1.sfx"), file);
        var s = Sfx.Load(file);
        Sfx.Save(s, file);
        string json = File.ReadAllText(file);
        Assert.Equal(2, DocumentFile.PeekVersion(json));
        Assert.DoesNotContain("Duration", json);
        Assert.True(File.Exists(file + ".v1.bak"));
        Assert.Equal(523, Sfx.Load(file).Freq);
    }

    [Theory]
    [InlineData("x.pxf")]
    [InlineData("x.pxm")]
    [InlineData("x.pxs")]
    [InlineData("x.sfx")]
    public void Damaged_and_newer_files_give_readable_errors(string name)
    {
        string file = P(name);
        Action load = Path.GetExtension(name) switch
        {
            ".pxf" => () => ProjectFile.Load(file),
            ".pxm" => () => Tilemap.Load(file),
            ".pxs" => () => Song.Load(file),
            _ => () => Sfx.Load(file),
        };
        File.WriteAllText(file, "{ \"Version\": 2, \"Width\": ");
        Assert.Contains("damaged", Assert.Throws<InvalidDataException>(load).Message);
        if (!name.EndsWith(".sfx"))   // a sound ignores unknown fields; the others have required structure
        {
            File.WriteAllText(file, "{ \"Version\": 2, \"Width\": -5, \"Height\": 1, \"Layers\": null }");
            Assert.Throws<InvalidDataException>(load);
        }
        File.WriteAllText(file, "{ \"Version\": 42 }");
        Assert.Contains("newer Pixelkiln", Assert.Throws<InvalidDataException>(load).Message);
        Assert.Throws<FileNotFoundException>(() => ProjectFile.Load(P("nope.pxf")));
    }

    [Fact]
    public void Map_v2_upgrades_to_current_keeping_everything()
    {
        string file = P("m.pxm");
        File.Copy(Fixture("map.v2.pxm"), file);
        Tilemap.Save(Tilemap.Load(file), file);
        var m = Tilemap.Load(file);
        Assert.Equal(Tilemap.Version, DocumentFile.PeekVersion(File.ReadAllText(file)));
        Assert.Equal(["Player", "Wall"], m.Objects.Select(o => o.Name));
        Assert.Null(m.TilesetAssetId);
        Assert.True(File.Exists(file + ".v2.bak"));
    }
}
