using PixelForge.Core;

namespace PixelForge.Core.Tests;

public class MusicTests
{
    [Fact]
    public void Empty_song_is_silent_and_has_right_length()
    {
        var s = new Song { Bpm = 120 };                       // step = 0.125 s
        var samples = Synth.Render(s);
        Assert.Equal(16 * (int)Math.Round(Synth.SampleRate * 0.125), samples.Length);
        Assert.All(samples, x => Assert.Equal(0f, x));
    }

    [Fact]
    public void A4_on_pulse_has_440_hz()
    {
        var s = new Song();
        s.Patterns[0].Notes[(int)Channel.Pulse2][0] = 69;
        var x = Synth.Render(s);
        int stepLen = (int)Math.Round(Synth.SampleRate * s.StepSeconds);
        // count rising zero crossings over the steady part of the first step
        int from = stepLen / 10, to = stepLen * 6 / 10, rises = 0;
        for (int i = from + 1; i < to; i++) if (x[i - 1] <= 0 && x[i] > 0) rises++;
        double hz = rises / ((to - from) / (double)Synth.SampleRate);
        Assert.InRange(hz, 430, 450);
        Assert.All(x[stepLen..], v => Assert.Equal(0f, v));    // nothing after step 0
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
    public void Song_round_trip_and_note_names()
    {
        var s = new Song { Bpm = 150 };
        s.Patterns.Add(Pattern.Empty(16));
        s.Patterns[1].Notes[3][7] = 40;
        s.Order.AddRange([1, 0]);
        var t = Song.Deserialize(Song.Serialize(s));
        Assert.Equal(150, t.Bpm);
        Assert.Equal([0, 1, 0], t.Order);
        Assert.Equal(40, t.Patterns[1].Notes[3][7]);
        Assert.Equal("C4", Song.NoteName(60));
        Assert.Equal("A#3", Song.NoteName(58));
        Assert.Throws<InvalidDataException>(() => Song.Deserialize("""{"Version":1,"Bpm":120,"Steps":16,"Patterns":[],"Order":[0]}"""));
    }
}
