namespace PixelForge.Core;

/// <summary>Drum kit sounds of the noise channel; the value is the note (row) that plays it (General MIDI numbers).</summary>
public enum Drum { Kick = 36, Snare = 38, Clap = 39, ClosedHat = 42, LowTom = 45, OpenHat = 46, HighTom = 48, Crash = 49 }

/// <summary>
/// Synthesised drums (no samples): a falling sine for kick and toms, noise bursts for snare and clap, bright
/// (differentiated) noise for hats and crash. Each hit is a fixed-length one-shot, started by a note on channel 4.
/// </summary>
public static class Drums
{
    public static readonly Drum[] Kit = Enum.GetValues<Drum>();

    public static string Name(Drum d) => d switch
    {
        Drum.ClosedHat => "Hi-hat", Drum.OpenHat => "Open hat", Drum.LowTom => "Low tom", Drum.HighTom => "High tom", _ => d.ToString(),
    };

    /// <summary>The drum a note plays (other notes play the nearest drum).</summary>
    public static Drum Of(int note) => Kit.MinBy(d => Math.Abs((int)d - note));

    /// <summary>Seconds a hit lasts; after that the voice is silent.</summary>
    public static double Length(Drum d) => d switch
    {
        Drum.ClosedHat => 0.08, Drum.Snare => 0.3, Drum.Clap => 0.35, Drum.Crash => 1.5, _ => 0.5,
    };

    /// <summary>One sample of a hit, t seconds after it started. Phase carries the oscillator; noise is white noise (−1..1), bright the differentiated noise.</summary>
    public static double Sample(Drum d, double t, ref double phase, double noise, double bright)
    {
        // pitched drums: the tone falls from its start to its end frequency
        var (from, to, fall) = d switch
        {
            Drum.Kick => (150.0, 45.0, 30.0), Drum.LowTom => (110.0, 70.0, 12.0), Drum.HighTom => (170.0, 115.0, 12.0), _ => (190.0, 190.0, 1.0),
        };
        phase += (to + (from - to) * Math.Exp(-t * fall)) / Synth.SampleRate;
        double tone = Math.Sin(2 * Math.PI * phase);
        return d switch
        {
            Drum.Kick => tone * Math.Exp(-t * 9),
            Drum.Snare => noise * Math.Exp(-t * 18) * 0.7 + tone * Math.Exp(-t * 25) * 0.5,
            Drum.Clap => noise * (Burst(t, 0) + Burst(t, 0.012) + Burst(t, 0.024) + Math.Exp(-t * 12) * 0.35) * 0.7,
            Drum.ClosedHat => bright * Math.Exp(-t * 90) * 0.5,
            Drum.OpenHat => bright * Math.Exp(-t * 9) * 0.4,
            Drum.LowTom or Drum.HighTom => tone * Math.Exp(-t * 10),
            _ => bright * Math.Exp(-t * 3) * 0.35,
        };
        static double Burst(double t, double at) => t < at ? 0 : Math.Exp(-(t - at) * 120);
    }
}

/// <summary>Ready-made 16-step beats for the drum kit (one drum per step: the noise channel plays one sound at a time).</summary>
public static class DrumBeats
{
    // K kick, S snare (s soft), C clap, h hi-hat, o open hat, t low tom, T high tom, X crash, . nothing
    static readonly Dictionary<string, string> Beats = new()
    {
        ["Rock"] = "K.h.S.h.K.K.S.h.",
        ["Four on the floor"] = "K.o.K.o.K.o.K.o.",
        ["Hip-hop"] = "K.h.S.hK.hKS.h.h",
        ["Breakbeat"] = "K.h.S.hK.hKh.S.h",
        ["Half-time"] = "K.h.h.h.S.hKh.h.",
        ["Disco"] = "KhohChohKhohChoh",
        ["March"] = "XsSsSsSsKsSsSsSs",
        ["Tom fill"] = "S.S.T.T.TTt.t.tX",
    };

    public static IReadOnlyCollection<string> Names => Beats.Keys;

    /// <summary>Replaces the drum channel of a pattern with a beat (repeated if the pattern is longer than 16 steps).</summary>
    public static void Apply(Pattern p, string name)
    {
        string beat = Beats.TryGetValue(name, out var b) ? b : throw new ArgumentException($"Unknown beat “{name}”.", nameof(name));
        int ch = (int)Channel.Noise, steps = p.Notes[ch].Length;
        for (int st = 0; st < steps; st++)
        {
            char c = beat[st % beat.Length];
            Drum? d = c switch
            {
                'K' => Drum.Kick, 'S' or 's' => Drum.Snare, 'C' => Drum.Clap, 'h' => Drum.ClosedHat, 'o' => Drum.OpenHat,
                't' => Drum.LowTom, 'T' => Drum.HighTom, 'X' => Drum.Crash, _ => null,
            };
            p.Notes[ch][st] = d is { } drum ? (int)drum : Pattern.Off;
            p.Volumes[ch][st] = c switch { 's' => 6, 'h' or 'o' => 8, _ => 13 };
        }
    }
}
