using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Pixelkiln.Core;

public enum SfxWave { Square, Saw, Sine, Noise }

/// <summary>
/// sfxr-style sound effect: one oscillator with pitch slide, vibrato, a pitch jump,
/// attack/sustain/decay envelope and a low-pass filter. Times in seconds.
/// </summary>
public sealed class Sfx
{
    public SfxWave Wave { get; set; } = SfxWave.Square;
    /// <summary>Start pitch in Hz.</summary>
    public double Freq { get; set; } = 440;
    /// <summary>Pitch change in octaves per second (negative = falling).</summary>
    public double Slide { get; set; }
    /// <summary>Change of Slide per second.</summary>
    public double DeltaSlide { get; set; }
    public double VibratoDepth { get; set; }
    public double VibratoSpeed { get; set; } = 10;
    public double Duty { get; set; } = 0.5;
    /// <summary>Pitch jump in semitones at JumpTime (0 = none).</summary>
    public double PitchJump { get; set; }
    public double JumpTime { get; set; } = 0.1;
    public double Attack { get; set; }
    public double Sustain { get; set; } = 0.1;
    public double Decay { get; set; } = 0.2;
    /// <summary>Low-pass amount 0..1, 1 = off.</summary>
    public double LowPass { get; set; } = 1;
    public double Volume { get; set; } = 0.5;

    [JsonIgnore]
    public double Duration => Attack + Sustain + Decay;

    public Sfx Clone() => (Sfx)MemberwiseClone();

    public float[] Render(int seed = 1)
    {
        const int sr = Synth.SampleRate;
        var rnd = new Random(seed);
        var output = new float[Math.Max(1, (int)Math.Round(Duration * sr))];
        double freq = Math.Clamp(Freq, 20, 8000), slide = Slide, phase = 0, filtered = 0, noise = rnd.NextDouble() * 2 - 1;
        bool jumped = PitchJump == 0;
        double cutoff = Math.Clamp(LowPass, 0.01, 1);

        for (int i = 0; i < output.Length; i++)
        {
            double t = (double)i / sr;
            if (!jumped && t >= JumpTime) { freq *= Math.Pow(2, PitchJump / 12); jumped = true; }
            slide += DeltaSlide / sr;
            freq = Math.Clamp(freq * Math.Pow(2, slide / sr), 20, 8000);
            double f = freq * (1 + VibratoDepth * Math.Sin(2 * Math.PI * VibratoSpeed * t));

            double x = Wave switch
            {
                SfxWave.Square => phase < Math.Clamp(Duty, 0.05, 0.95) ? 1 : -1,
                SfxWave.Saw => 2 * phase - 1,
                SfxWave.Sine => Math.Sin(2 * Math.PI * phase),
                _ => noise,
            };
            phase += f / sr;
            if (phase >= 1)
            {
                phase %= 1;
                noise = rnd.NextDouble() * 2 - 1;
            }

            double env = t < Attack ? t / Attack
                : t < Attack + Sustain ? 1
                : Decay > 0 ? Math.Max(0, 1 - (t - Attack - Sustain) / Decay) : 0;
            filtered += cutoff * (x - filtered);
            output[i] = (float)Math.Clamp(filtered * env * Volume, -1, 1);
        }
        return output;
    }

    // ---------- presets ----------

    public static readonly string[] PresetNames = ["Coin", "Laser", "Explosion", "Power-up", "Hit", "Jump", "Blip", "Random"];

    public static Sfx Preset(string name, Random r)
    {
        double R(double a, double b) => a + r.NextDouble() * (b - a);
        return name switch
        {
            "Coin" => new() { Wave = SfxWave.Square, Freq = R(800, 1400), Sustain = R(0.04, 0.1), Decay = R(0.15, 0.3), PitchJump = R(5, 12), JumpTime = R(0.03, 0.08) },
            "Laser" => new() { Wave = r.Next(2) == 0 ? SfxWave.Saw : SfxWave.Square, Freq = R(500, 1200), Slide = R(-6, -2), Duty = R(0.2, 0.5), Sustain = R(0.05, 0.15), Decay = R(0.1, 0.25) },
            "Explosion" => new() { Wave = SfxWave.Noise, Freq = R(60, 200), Slide = R(-1.5, -0.3), Sustain = R(0.1, 0.3), Decay = R(0.3, 0.6), LowPass = R(0.15, 0.5), Volume = 0.7 },
            "Power-up" => new() { Wave = SfxWave.Square, Freq = R(300, 500), Slide = R(1, 3), VibratoDepth = R(0.05, 0.2), VibratoSpeed = R(8, 20), Sustain = R(0.2, 0.3), Decay = R(0.2, 0.4) },
            "Hit" => new() { Wave = r.Next(2) == 0 ? SfxWave.Noise : SfxWave.Saw, Freq = R(200, 500), Slide = R(-6, -3), Sustain = R(0.02, 0.05), Decay = R(0.1, 0.2), LowPass = R(0.5, 1) },
            "Jump" => new() { Wave = SfxWave.Square, Freq = R(250, 450), Slide = R(2, 4), Duty = R(0.3, 0.5), Sustain = R(0.1, 0.2), Decay = R(0.1, 0.2) },
            "Blip" => new() { Wave = r.Next(2) == 0 ? SfxWave.Square : SfxWave.Sine, Freq = R(600, 1200), Sustain = R(0.03, 0.08), Decay = R(0.03, 0.08) },
            _ => new()
            {
                Wave = (SfxWave)r.Next(4), Freq = R(80, 1500), Slide = R(-4, 4), DeltaSlide = R(-2, 2), VibratoDepth = r.Next(3) == 0 ? R(0, 0.3) : 0,
                VibratoSpeed = R(2, 25), Duty = R(0.1, 0.9), PitchJump = r.Next(3) == 0 ? R(-12, 12) : 0, JumpTime = R(0.02, 0.2),
                Attack = R(0, 0.1), Sustain = R(0.02, 0.3), Decay = R(0.05, 0.5), LowPass = R(0.2, 1),
            },
        };
    }

    /// <summary>Small random changes to every parameter.</summary>
    public Sfx Mutate(Random r)
    {
        double J(double v, double amount) => v + (r.NextDouble() * 2 - 1) * amount;
        return Sanitized(new Sfx
        {
            Wave = Wave, Freq = Freq * Math.Pow(2, J(0, 0.15)), Slide = J(Slide, 0.3), DeltaSlide = J(DeltaSlide, 0.2),
            VibratoDepth = J(VibratoDepth, 0.03), VibratoSpeed = J(VibratoSpeed, 2), Duty = J(Duty, 0.05),
            PitchJump = PitchJump == 0 ? 0 : J(PitchJump, 1), JumpTime = J(JumpTime, 0.02),
            Attack = J(Attack, 0.01), Sustain = J(Sustain, 0.02), Decay = J(Decay, 0.03), LowPass = J(LowPass, 0.05), Volume = Volume,
        });
    }

    /// <summary>Clamps everything into the ranges the editor allows.</summary>
    public static Sfx Sanitized(Sfx s) => new()
    {
        Wave = Enum.IsDefined(s.Wave) ? s.Wave : SfxWave.Square,
        Freq = Math.Clamp(s.Freq, 20, 4000), Slide = Math.Clamp(s.Slide, -10, 10), DeltaSlide = Math.Clamp(s.DeltaSlide, -10, 10),
        VibratoDepth = Math.Clamp(s.VibratoDepth, 0, 0.5), VibratoSpeed = Math.Clamp(s.VibratoSpeed, 0, 40), Duty = Math.Clamp(s.Duty, 0.05, 0.95),
        PitchJump = Math.Clamp(s.PitchJump, -24, 24), JumpTime = Math.Clamp(s.JumpTime, 0, 1),
        Attack = Math.Clamp(s.Attack, 0, 1), Sustain = Math.Clamp(s.Sustain, 0, 2), Decay = Math.Clamp(s.Decay, 0, 2),
        LowPass = Math.Clamp(s.LowPass, 0.01, 1), Volume = Math.Clamp(s.Volume, 0, 1),
    };

    /// <summary>v1 had no Version field; v2 adds it (DocumentFile treats a missing field as 1).</summary>
    public const int Version = 2;

    public static string Serialize(Sfx s)
    {
        var node = JsonSerializer.SerializeToNode(s)!.AsObject();
        node.Insert(0, "Version", Version);
        return node.ToJsonString(DocumentFile.Indented);
    }

    public static Sfx Deserialize(string json) =>
        Sanitized(JsonSerializer.Deserialize<Sfx>(json) ?? throw new InvalidDataException("Empty sound file."));

    public static void Save(Sfx s, string path) => DocumentFile.Save(path, Serialize(s), Version);

    public static Sfx Load(string path) => DocumentFile.Load(path, "sound", Version, Deserialize);
}
