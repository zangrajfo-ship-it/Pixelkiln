namespace PixelForge.Core;

/// <summary>Renders a song to mono float samples (-1..1).</summary>
public static class Synth
{
    public const int SampleRate = 44100;

    /// <param name="order">Pattern indices to play; defaults to the song's order.</param>
    public static float[] Render(Song song, IReadOnlyList<int>? order = null)
    {
        order ??= song.Order;
        int stepLen = (int)Math.Round(SampleRate * song.StepSeconds);
        var output = new float[order.Count * song.Steps * stepLen];
        var phase = new double[Song.Channels];
        var rnd = new Random(1);
        float noise = 0;
        int attack = SampleRate / 500;                       // 2 ms, avoids clicks
        int releaseStart = (int)(stepLen * 0.7);

        int pos = 0;
        foreach (int pi in order)
        {
            var pat = song.Patterns[pi];
            for (int step = 0; step < song.Steps; step++, pos += stepLen)
            {
                for (int ch = 0; ch < Song.Channels; ch++)
                {
                    int note = pat.Notes[ch][step];
                    if (note == Pattern.Off) continue;
                    double inc = Song.Frequency(note) / SampleRate;
                    int noiseHold = Math.Max(1, (int)(1 / (inc * 8)));
                    for (int i = 0; i < stepLen; i++)
                    {
                        double env = i < attack ? (double)i / attack
                            : i > releaseStart ? (double)(stepLen - i) / (stepLen - releaseStart) : 1;
                        double p = phase[ch];
                        double v = (Channel)ch switch
                        {
                            Channel.Pulse1 => p < 0.25 ? 0.5 : -0.5,       // 25% duty
                            Channel.Pulse2 => p < 0.5 ? 0.5 : -0.5,        // 50% duty
                            Channel.Triangle => 4 * Math.Abs(p - 0.5) - 1,
                            _ => (i % noiseHold == 0 ? noise = (float)(rnd.NextDouble() * 2 - 1) : noise) * 0.4,
                        };
                        phase[ch] = (p + inc) % 1;
                        output[pos + i] += (float)(v * env * 0.25);
                    }
                }
            }
        }
        return output;
    }
}

public static class WavWriter
{
    /// <summary>16-bit PCM mono WAV.</summary>
    public static void Write(Stream s, float[] samples, int sampleRate = Synth.SampleRate)
    {
        var w = new BinaryWriter(s);
        int dataBytes = samples.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + dataBytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(sampleRate); w.Write(sampleRate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(dataBytes);
        foreach (var x in samples) w.Write((short)Math.Round(Math.Clamp(x, -1f, 1f) * short.MaxValue));
        w.Flush();
    }

    public static byte[] ToBytes(float[] samples)
    {
        var ms = new MemoryStream();
        Write(ms, samples);
        return ms.ToArray();
    }
}
