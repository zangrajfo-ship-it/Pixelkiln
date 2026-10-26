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
        Assert.Equal("C4", Song.NoteName(60));
        Assert.Equal("A#3", Song.NoteName(58));
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

        Assert.Throws<InvalidDataException>(() => Song.Deserialize("""{"Version":2,"Bpm":120,"Steps":16,"PatternData":[],"Order":[0]}"""));
    }
}
