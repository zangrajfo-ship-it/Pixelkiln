using PixelForge.Core;

namespace PixelForge.Core.Tests;

/// <summary>
/// Golden files in tests/PixelForge.Core.Tests/Fixtures: every file version PixelForge ever wrote must
/// keep loading with the same content. v1 files are hand-written in the old shape; v2 files were
/// written by the code at commit 9144477 (regenerate only on purpose, see <see cref="WriteV2Fixtures"/>).
/// </summary>
public class FormatRegressionTests
{
    const uint R = 0xFFFF0000, B = 0xFF0000FF;

    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void Sprite_v1()
    {
        var s = ProjectFile.Load(Fixture("sprite.v1.pxf"));
        Assert.Equal((2, 2, 2, 1), (s.Width, s.Height, s.Frames.Count, s.Layers.Count));
        Assert.Equal([100, 250], s.Frames.Select(f => f.DurationMs));
        Assert.Equal([R, 0u, 0u, B], s.Cel(0, 0));
        Assert.Equal(1.0, s.Layers[0].Opacity);   // added later, defaults to opaque
        Assert.Empty(s.Tags);                     // added later
    }

    [Fact]
    public void Sprite_v2()
    {
        var s = ProjectFile.Load(Fixture("sprite.v2.pxf"));
        Assert.Equal((3, 2, 3, 2), (s.Width, s.Height, s.Frames.Count, s.Layers.Count));
        Assert.Equal("walk", s.Tags.Single().Name);
        Assert.Equal((1, 2), (s.Tags[0].From, s.Tags[0].To));
        Assert.Equal(0.5, s.Layers[1].Opacity);
        Assert.False(s.Layers[1].Visible);
        Assert.Equal(B, s.Cel(1, 2)[5]);
    }

    [Fact]
    public void Map_v1()
    {
        var m = Tilemap.Load(Fixture("map.v1.pxm"));
        Assert.Equal((3, 2, 2), (m.Width, m.Height, m.TileSize));
        Assert.Equal(2, m.Tileset!.Count);
        Assert.Equal([0, 1, -1, -1, 1, 0], m.Layers[0].Tiles);
        Assert.Empty(m.Objects);
        Assert.Empty(m.Terrains);
    }

    [Fact]
    public void Map_v2()
    {
        var m = Tilemap.Load(Fixture("map.v2.pxm"));
        Assert.Equal(2, m.Layers.Count);
        Assert.Equal(["Player", "Wall"], m.Objects.Select(o => o.Name));
        Assert.True(m.Objects[0].IsPoint);
        Assert.Equal([0], m.Terrains.Select(t => t.Base));
        Assert.Equal(TerrainMode.Blob16, m.Terrains[0].Mode);
        Assert.Equal(1, m.Layers[1].Tiles[0]);
    }

    [Fact]
    public void Song_v1()
    {
        var s = Song.Load(Fixture("song.v1.pxs"));
        Assert.Equal((140, 16), (s.Bpm, s.Steps));
        Assert.Equal(60, s.Patterns[0].Notes[0][0]);
        Assert.Equal(69, s.Patterns[0].Notes[2][4]);
        Assert.Equal(Pattern.DefaultVolume, s.Patterns[0].Volumes[0][0]);
        Assert.Equal(0.25, s.Instruments[0].Duty);   // channel default
    }

    [Fact]
    public void Song_v2()
    {
        var s = Song.Load(Fixture("song.v2.pxs"));
        Assert.Equal([0, 1, 0], s.Order);
        Assert.Equal(Pattern.Hold, s.Patterns[1].Notes[1][3]);
        Assert.Equal(5, s.Patterns[1].Volumes[1][2]);
        Assert.Equal(0.125, s.Instruments[0].Duty);
        Assert.Equal(300, s.Instruments[3].DecayMs);
    }

    [Fact]
    public void Sound_v1_without_version_field()
    {
        var s = Sfx.Load(Fixture("sound.v1.sfx"));
        Assert.Equal(SfxWave.Saw, s.Wave);
        Assert.Equal(523, s.Freq);
        Assert.Equal(-2, s.Slide);
        Assert.Equal((int)Math.Round(0.25 * Synth.SampleRate), s.Render().Length);
    }

    [Fact]
    public void All_fixtures_still_render_or_export()
    {
        var sprite = ProjectFile.Load(Fixture("sprite.v2.pxf"));
        Assert.Contains("frameTags", SpriteSheet.Json(sprite, SpriteSheet.Build(sprite), "a.png", "a"));
        Assert.Contains("\"objectgroup\"", TiledExport.Json(Tilemap.Load(Fixture("map.v2.pxm")), "t.png"));
        Assert.NotEmpty(Synth.Render(Song.Load(Fixture("song.v2.pxs"))));
    }

    /// <summary>Writes the v2 fixtures with the current code. Runs only with PF_WRITE_FIXTURES=1 and the fixtures folder as PF_FIXTURES_DIR.</summary>
    [Fact]
    public void WriteV2Fixtures()
    {
        if (Environment.GetEnvironmentVariable("PF_WRITE_FIXTURES") != "1") return;
        string dir = Environment.GetEnvironmentVariable("PF_FIXTURES_DIR") ?? throw new InvalidOperationException("Set PF_FIXTURES_DIR.");

        var s = new Sprite(3, 2);
        s.AddFrame(1);
        s.AddFrame(2, copyFrom: 1);
        s.AddLayer(1, "fx");
        s.Layers[1].Visible = false;
        s.Layers[1].Opacity = 0.5;
        s.Cel(0, 0)[0] = R;
        s.Cel(1, 2)[5] = B;
        s.Tags.Add(new FrameTag { Name = "walk", From = 1, To = 2 });
        ProjectFile.Save(s, Path.Combine(dir, "sprite.v2.pxf"));

        var m = new Tilemap(3, 2, 2) { Tileset = new Tileset([R, R, B, B, R, R, B, B], 4, 2, 2) };
        m.Layers[0].Tiles[4] = 0;
        m.AddLayer(1, "Deco").Tiles[0] = 1;
        m.Objects.Add(new MapObject { Id = 1, Name = "Player", Type = "spawn", X = 1, Y = 1 });
        m.Objects.Add(new MapObject { Id = 2, Name = "Wall", Type = "solid", X = 0, Y = 0, W = 6, H = 2 });
        m.Terrains.Add(new TerrainRule { Base = 0 });
        Tilemap.Save(m, Path.Combine(dir, "map.v2.pxm"));

        var song = new Song { Bpm = 150 };
        song.Patterns.Add(Pattern.Empty(16));
        song.Patterns[1].SetNote(1, 2, 64, 3);
        song.Patterns[1].Volumes[1][2] = 5;
        song.Instruments[0].Duty = 0.125;
        song.Instruments[3].DecayMs = 300;
        song.Order.AddRange([1, 0]);
        Song.Save(song, Path.Combine(dir, "song.v2.pxs"));
    }
}
