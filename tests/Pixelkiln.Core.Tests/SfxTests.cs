using Pixelkiln.Core;

namespace Pixelkiln.Core.Tests;

public class SfxTests() : TempDirTest("sfx")
{
    [Fact]
    public void Presets_are_deterministic_bounded_and_right_length()
    {
        foreach (var name in Sfx.PresetNames)
            for (int seed = 0; seed < 20; seed++)
            {
                var a = Sfx.Preset(name, new Random(seed));
                var b = Sfx.Preset(name, new Random(seed));
                var x = a.Render();
                Assert.Equal(x, b.Render());
                Assert.Equal((int)Math.Round(a.Duration * Synth.SampleRate), x.Length);
                Assert.All(x, v => Assert.True(float.IsFinite(v) && Math.Abs(v) <= 1));
                Assert.Contains(x, v => v != 0);

                var m = a.Mutate(new Random(seed));
                Assert.All(m.Render(), v => Assert.True(float.IsFinite(v) && Math.Abs(v) <= 1));
            }
    }

    [Fact]
    public void Save_load_round_trip_clamps()
    {
        var path = P("a.sfx");
        Sfx.Save(new Sfx { Wave = SfxWave.Saw, Freq = 99999, Decay = 0.3 }, path);
        var s = Sfx.Load(path);
        Assert.Equal(SfxWave.Saw, s.Wave);
        Assert.Equal(4000, s.Freq);
        Assert.Equal(0.3, s.Decay);
    }

    // ---------- drum kit (song v3) ----------

    static float[] Hit(Drum d)
    {
        var s = new Song(16) { Bpm = 60 };   // one step = 0.25 s
        s.Patterns[0].SetNote((int)Channel.Noise, 0, (int)d, 1);
        return Synth.Render(s);
    }

    static int Crossings(float[] x, int n) => Enumerable.Range(1, n - 1).Count(i => x[i - 1] < 0 != x[i] < 0);

    [Fact]
    public void Every_drum_is_audible_bounded_and_kick_is_lower_than_hat()
    {
        Assert.True(new Song().Instruments[(int)Channel.Noise].Kit);                     // new songs have the kit
        foreach (var d in Drums.Kit)
        {
            var x = Hit(d);
            Assert.All(x, v => Assert.True(float.IsFinite(v) && Math.Abs(v) <= 1));
            Assert.True(x.Take(4000).Max(Math.Abs) > 0.05, $"{d} is too quiet");
            Assert.True(x.Skip(Synth.SampleRate * 2).All(v => v == 0), $"{d} does not stop");  // one-shot, well before 2 s
        }
        int n = 2000;
        Assert.True(Crossings(Hit(Drum.Kick), n) * 10 < Crossings(Hit(Drum.ClosedHat), n));
        Assert.Equal(Drum.ClosedHat, Drums.Of(43));
        Assert.Equal(Drum.Kick, Drums.Of(20));
    }

    [Fact]
    public void Beats_fill_sixteen_steps_and_the_generated_song_has_drums()
    {
        foreach (var name in DrumBeats.Names)
        {
            var p = Pattern.Empty(32);
            DrumBeats.Apply(p, name);
            var drums = p.Notes[(int)Channel.Noise];
            Assert.Contains(drums, n => n >= 0);
            Assert.All(drums.Where(n => n >= 0), n => Assert.Contains((Drum)n, Drums.Kit));
            Assert.Equal(drums[..16], drums[16..]);                                      // repeats on longer patterns
        }
        Assert.Throws<ArgumentException>(() => DrumBeats.Apply(Pattern.Empty(16), "nope"));

        var song = Procedural.Song(7, 120, "minor", 4);
        Assert.True(song.Instruments[(int)Channel.Noise].Kit);
        Assert.Contains(song.Patterns[0].Notes[(int)Channel.Noise], n => n == (int)Drum.ClosedHat);
        var audio = Synth.Render(song);
        Assert.True(Math.Sqrt(audio.Average(v => (double)v * v)) > 0.02, "generated song is (nearly) silent");
    }

    [Fact]
    public void Older_songs_keep_pitched_noise_and_v3_keeps_the_kit()
    {
        Assert.False(Song.Load(TestFiles.Fixture("song.v2.pxs")).Instruments[(int)Channel.Noise].Kit);
        var s = new Song();
        s.Instruments[(int)Channel.Noise].Kit = false;
        Assert.False(Song.Deserialize(Song.Serialize(s)).Instruments[(int)Channel.Noise].Kit);
        Assert.True(Song.Deserialize(Song.Serialize(new Song())).Instruments[(int)Channel.Noise].Kit);
    }
}
