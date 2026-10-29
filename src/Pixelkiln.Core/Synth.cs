namespace Pixelkiln.Core;

/// <summary>
/// Streams a song as mono float samples. Reads the song at every step, so edits made while
/// playing are heard right away. Offline rendering uses the same stream, so both always match.
/// </summary>
public sealed class SongStream(Func<Song> song, IReadOnlyList<int>? order = null, bool loop = false)
{
    enum Stage { Idle, Attack, Decay, Sustain, Release }

    sealed class Voice
    {
        public Stage Stage;
        public double Level, Phase, ReleaseFrom;
        public int Note;
        public double Velocity;
        public float Noise;
        // drum kit: which drum, seconds since the hit, its oscillator and the previous noise value
        public Drum Hit;
        public double Age, DrumPhase, PrevNoise;
    }

    readonly Voice[] _voices = [.. Enumerable.Range(0, Song.Channels).Select(_ => new Voice())];
    readonly Random _rnd = new(1);
    long _step = -1;        // global step index; -1 = not started
    int _inStep, _stepLen;  // samples into the current step / its length

    /// <summary>Pattern indices to play; null = the song's order.</summary>
    public IReadOnlyList<int>? Order { get; } = order;
    public bool Loop { get; } = loop;
    public bool Finished { get; private set; }

    /// <summary>Position for the playhead (written by the audio thread).</summary>
    public volatile int CurrentOrderIndex, CurrentStep;

    /// <summary>Adds samples into buffer[offset..offset+count]. Returns count (silence after the end).</summary>
    public int Read(float[] buffer, int offset, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (Finished) break;
            var s = song();
            if (_step < 0 || _inStep >= _stepLen) NextStep(s);
            if (Finished) break;
            buffer[offset + i] += Sample(s);
            _inStep++;
        }
        return count;
    }

    void NextStep(Song s)
    {
        var ord = Order ?? s.Order;
        long total = (long)ord.Count * s.Steps;
        _step++;
        if (_step >= total)
        {
            if (!Loop || total == 0) { Finished = true; return; }
            _step = 0;
        }
        int oi = (int)(_step / s.Steps), st = (int)(_step % s.Steps);
        (CurrentOrderIndex, CurrentStep) = (oi, st);
        _inStep = 0;
        _stepLen = Math.Max(1, (int)Math.Round(Synth.SampleRate * s.StepSeconds));

        int pi = ord[oi];
        if (pi < 0 || pi >= s.Patterns.Count) { ReleaseAll(); return; }   // pattern deleted while playing
        var pat = s.Patterns[pi];
        for (int ch = 0; ch < Song.Channels; ch++)
        {
            var v = _voices[ch];
            int n = st < pat.Notes[ch].Length ? pat.Notes[ch][st] : Pattern.Off;
            if (ch == (int)Channel.Noise && s.Instruments[ch].Kit)
            {
                // drums are one-shots: a note starts a hit, Off and Hold change nothing
                if (n >= 0) (v.Hit, v.Age, v.DrumPhase, v.Stage, v.Velocity) = (Drums.Of(n), 0, 0, Stage.Sustain, pat.Volumes[ch][st] / (double)Pattern.MaxVolume);
                continue;
            }
            if (n >= 0)
            {
                (v.Note, v.Stage, v.Velocity) = (n, Stage.Attack, pat.Volumes[ch][st] / (double)Pattern.MaxVolume);   // level continues: no click
            }
            else if (n == Pattern.Off && v.Stage is Stage.Attack or Stage.Decay or Stage.Sustain)
            {
                (v.Stage, v.ReleaseFrom) = (Stage.Release, v.Level);
            }
        }
    }

    void ReleaseAll()
    {
        foreach (var v in _voices)
            if (v.Stage is not (Stage.Idle or Stage.Release)) (v.Stage, v.ReleaseFrom) = (Stage.Release, v.Level);
    }

    float Sample(Song s)
    {
        double sum = 0;
        for (int ch = 0; ch < Song.Channels; ch++)
        {
            var v = _voices[ch];
            if (v.Stage == Stage.Idle) continue;
            var ins = s.Instruments[ch];
            if (ch == (int)Channel.Noise && ins.Kit)
            {
                double noise = _rnd.NextDouble() * 2 - 1, bright = (noise - v.PrevNoise) * 0.5;
                v.PrevNoise = noise;
                sum += Drums.Sample(v.Hit, v.Age, ref v.DrumPhase, noise, bright) * v.Velocity * ins.Volume * 0.35;
                v.Age += 1.0 / Synth.SampleRate;
                if (v.Age >= Drums.Length(v.Hit)) v.Stage = Stage.Idle;
                continue;
            }
            Envelope(v, ins);
            double inc = Song.Frequency(v.Note) / Synth.SampleRate;
            double p = v.Phase;
            double wave = (Channel)ch switch
            {
                Channel.Pulse1 or Channel.Pulse2 => p < ins.Duty ? 0.5 : -0.5,
                Channel.Triangle => 4 * Math.Abs(p - 0.5) - 1,
                _ => v.Noise * 0.5,
            };
            v.Phase = p + inc * (ch == (int)Channel.Noise ? 8 : 1);
            if (v.Phase >= 1)
            {
                v.Phase %= 1;
                if (ch == (int)Channel.Noise) v.Noise = (float)(_rnd.NextDouble() * 2 - 1);   // pitched noise: new value each period
            }
            sum += wave * v.Level * v.Velocity * ins.Volume * 0.25;
        }
        return (float)sum;
    }

    static void Envelope(Voice v, Instrument ins)
    {
        static double Per(double ms) => ms <= 0 ? 1 : 1000.0 / (ms * Synth.SampleRate);
        switch (v.Stage)
        {
            case Stage.Attack:
                v.Level += Per(ins.AttackMs);
                if (v.Level >= 1) (v.Level, v.Stage) = (1, Stage.Decay);
                break;
            case Stage.Decay:
                v.Level -= (1 - ins.Sustain) * Per(ins.DecayMs);
                if (v.Level <= ins.Sustain) (v.Level, v.Stage) = (ins.Sustain, Stage.Sustain);
                break;
            case Stage.Sustain:
                v.Level = ins.Sustain;
                break;
            case Stage.Release:
                v.Level -= v.ReleaseFrom * Per(ins.ReleaseMs);
                if (v.Level <= 0) (v.Level, v.Stage) = (0, Stage.Idle);
                break;
        }
    }
}

public static class Synth
{
    public const int SampleRate = 44100;

    /// <summary>Whole song (or the given pattern order) once, cut at the last step.</summary>
    public static float[] Render(Song song, IReadOnlyList<int>? order = null)
    {
        order ??= song.Order;
        int stepLen = Math.Max(1, (int)Math.Round(SampleRate * song.StepSeconds));
        var output = new float[order.Count * song.Steps * stepLen];
        new SongStream(() => song, order).Read(output, 0, output.Length);
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

    /// <summary>Writes through a temporary file, so a failed export never leaves half a WAV.</summary>
    public static void Save(string path, float[] samples) => DocumentFile.WriteViaTemp(path, f => Write(f, samples));

    public static byte[] ToBytes(float[] samples)
    {
        var ms = new MemoryStream();
        Write(ms, samples);
        return ms.ToArray();
    }
}
