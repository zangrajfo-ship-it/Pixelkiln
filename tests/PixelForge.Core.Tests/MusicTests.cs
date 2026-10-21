using PixelForge.Core;

namespace PixelForge.Core.Tests;

public class MusicTests
{
    static int StepLen(Song s) => (int)Math.Round(Synth.SampleRate * s.StepSeconds);

    [Fact]
    public void Empty_song_is_silent_and_has_right_length()
    {
        var s = new Song { Bpm = 120 };                       // step = 0.125 s
        var samples = Synth.Render(s);
        Assert.Equal(16 * (int)Math.Round(Synth.SampleRate * 0.125), samples.Length);
        Assert.All(samples, x => Assert.Equal(0f, x));
    }

    [Fact]
    public void A4_on_pulse_has_440_hz_and_stops_after_release()
    {
        var s = new Song();
        s.Patterns[0].Notes[(int)Channel.Pulse2][0] = 69;
        var x = Synth.Render(s);
        int stepLen = StepLen(s);
        int from = stepLen / 10, to = stepLen * 6 / 10, rises = 0;
        for (int i = from + 1; i < to; i++) if (x[i - 1] <= 0 && x[i] > 0) rises++;
        double hz = rises / ((to - from) / (double)Synth.SampleRate);
        Assert.InRange(hz, 430, 450);
        int releaseEnd = stepLen + (int)(s.Instruments[1].ReleaseMs / 1000 * Synth.SampleRate) + 2;
        Assert.All(x[releaseEnd..], v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Streaming_in_odd_chunks_matches_render()
    {
        var s = new Song { Bpm = 180 };
        s.Patterns[0].SetNote(0, 0, 60, 3);
        s.Patterns[0].SetNote(2, 4, 40, 8);
        s.Patterns[0].Notes[3][2] = 70;
        var whole = Synth.Render(s);
        var chunked = new float[whole.Length];
        var stream = new SongStream(() => s);
        for (int pos = 0; pos < chunked.Length; pos += 777)
            stream.Read(chunked, pos, Math.Min(777, chunked.Length - pos));
        Assert.Equal(whole, chunked);
    }

    [Fact]
    public void Hold_does_not_retrigger_attack()
    {
        double PeakAtStep1(bool hold)
        {
            var s = new Song();
            s.Instruments[1] = new Instrument { AttackMs = 1, DecayMs = 5, Sustain = 0.3 };
            s.Patterns[0].Notes[1][0] = 60;
            s.Patterns[0].Notes[1][1] = hold ? Pattern.Hold : 60;
            var x = Synth.Render(s);
            int n = StepLen(s);
            return x[n..(n + 400)].Max(Math.Abs);
        }
        Assert.True(PeakAtStep1(hold: false) > PeakAtStep1(hold: true) * 2);
    }

    [Fact]
    public void Volume_zero_is_silent_and_loop_wraps()
    {
        var s = new Song();
        s.Patterns[0].Notes[0][0] = 60;
        s.Patterns[0].Volumes[0][0] = 0;
        Assert.All(Synth.Render(s), v => Assert.Equal(0f, v));

        var loop = new SongStream(() => s, loop: true);
        var buf = new float[StepLen(s) * 20];
        loop.Read(buf, 0, buf.Length);
        Assert.False(loop.Finished);
        Assert.Equal(3, loop.CurrentStep);   // 20 steps into a 16-step loop
    }

    [Fact]
    public void Note_helpers()
    {
        var p = Pattern.Empty(8);
        p.SetNote(0, 2, 60, 3);
        Assert.Equal([-1, -1, 60, -2, -2, -1, -1, -1], p.Notes[0]);
        Assert.Equal(2, p.NoteStart(0, 4));
        Assert.Equal(-1, p.NoteStart(0, 5));
        Assert.Equal(3, p.Length(0, 2));
        p.SetNote(0, 2, 62, 1);                  // shorten: orphan holds removed
        Assert.Equal([-1, -1, 62, -1, -1, -1, -1, -1], p.Notes[0]);
        p.RemoveNote(0, 2);
        Assert.All(p.Notes[0], n => Assert.Equal(Pattern.Off, n));
    }

    [Fact]
    public void Remove_pattern_fixes_order()
    {
        var s = new Song();
        s.Patterns.Add(Pattern.Empty(16));
        s.Patterns.Add(Pattern.Empty(16));
        s.Order.AddRange([1, 2, 1]);           // 0 1 2 1
        s.RemovePattern(1);
        Assert.Equal([0, 1], s.Order);
        s.RemovePattern(0);
        Assert.Equal([0], s.Order);
        s.RemovePattern(0);                    // last pattern stays
        Assert.Single(s.Patterns);
    }

    [Fact]
    public void Wav_header_and_size()
    {
        var bytes = WavWriter.ToBytes([0f, 1f, -1f]);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
        Assert.Equal(44 + 6, bytes.Length);
        Assert.Equal(Synth.SampleRate, BitConverter.ToInt32(bytes, 24));
        Assert.Equal(short.MaxValue, BitConverter.ToInt16(bytes, 46));
        Assert.Equal(-short.MaxValue, BitConverter.ToInt16(bytes, 48));
    }

    [Fact]
    public void Song_v2_round_trip_and_v1_still_loads()
    {
        var s = new Song { Bpm = 150 };
        s.Patterns.Add(Pattern.Empty(16));
        s.Patterns[1].SetNote(3, 7, 40, 2);
        s.Patterns[1].Volumes[3][7] = 5;
        s.Instruments[0].Duty = 0.125;
        s.Order.AddRange([1, 0]);
        var t = Song.Deserialize(Song.Serialize(s));
        Assert.Equal(150, t.Bpm);
        Assert.Equal([0, 1, 0], t.Order);
        Assert.Equal(Pattern.Hold, t.Patterns[1].Notes[3][8]);
        Assert.Equal(5, t.Patterns[1].Volumes[3][7]);
        Assert.Equal(0.125, t.Instruments[0].Duty);

        var notes = string.Join(",", Enumerable.Range(0, 4).Select(c => "[" + string.Join(",", Enumerable.Repeat(c == 0 ? "60" : "-1", 16)) + "]"));
        var v1 = Song.Deserialize($$"""{"Version":1,"Bpm":100,"Steps":16,"Patterns":[[{{notes}}]],"Order":[0]}""");
        Assert.Equal(60, v1.Patterns[0].Notes[0][5]);
        Assert.Equal(Pattern.DefaultVolume, v1.Patterns[0].Volumes[0][5]);
        Assert.Equal(0.25, v1.Instruments[0].Duty);

        Assert.Equal("C4", Song.NoteName(60));
        Assert.Equal("A#3", Song.NoteName(58));
        Assert.Throws<InvalidDataException>(() => Song.Deserialize("""{"Version":2,"Bpm":120,"Steps":16,"PatternData":[],"Order":[0]}"""));
    }
}

public class SfxTests
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
        var path = Path.Combine(Path.GetTempPath(), $"pf-{Guid.NewGuid()}.sfx");
        try
        {
            Sfx.Save(new Sfx { Wave = SfxWave.Saw, Freq = 99999, Decay = 0.3 }, path);
            var s = Sfx.Load(path);
            Assert.Equal(SfxWave.Saw, s.Wave);
            Assert.Equal(4000, s.Freq);
            Assert.Equal(0.3, s.Decay);
        }
        finally { File.Delete(path); }
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
        Assert.False(Song.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "song.v2.pxs")).Instruments[(int)Channel.Noise].Kit);
        var s = new Song();
        s.Instruments[(int)Channel.Noise].Kit = false;
        Assert.False(Song.Deserialize(Song.Serialize(s)).Instruments[(int)Channel.Noise].Kit);
        Assert.True(Song.Deserialize(Song.Serialize(new Song())).Instruments[(int)Channel.Noise].Kit);
    }
}
