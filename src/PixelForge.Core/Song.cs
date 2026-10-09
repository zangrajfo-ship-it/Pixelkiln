using System.Text.Json;

namespace PixelForge.Core;

/// <summary>Fixed chiptune channels, NES-style.</summary>
public enum Channel { Pulse1, Pulse2, Triangle, Noise }

public sealed class Pattern
{
    public const int Off = -1, Hold = -2, MaxVolume = 15, DefaultVolume = 12;

    /// <summary>[channel][step] = MIDI note (60 = C4), Off, or Hold (keep the previous note sounding).</summary>
    public int[][] Notes { get; init; } = [];
    /// <summary>[channel][step] = 0..15, used where a note starts.</summary>
    public int[][] Volumes { get; init; } = [];

    public static Pattern Empty(int steps) => new()
    {
        Notes = [.. Enumerable.Range(0, Song.Channels).Select(_ => Enumerable.Repeat(Off, steps).ToArray())],
        Volumes = [.. Enumerable.Range(0, Song.Channels).Select(_ => Enumerable.Repeat(DefaultVolume, steps).ToArray())],
    };

    public Pattern Clone() => new() { Notes = [.. Notes.Select(c => (int[])c.Clone())], Volumes = [.. Volumes.Select(c => (int[])c.Clone())] };

    /// <summary>Step where the note covering <paramref name="step"/> starts, or -1 if the step is silent.</summary>
    public int NoteStart(int ch, int step)
    {
        var n = Notes[ch];
        while (step >= 0 && n[step] == Hold) step--;
        return step >= 0 && n[step] >= 0 ? step : -1;
    }

    /// <summary>Steps a note lasts, counting its Holds.</summary>
    public int Length(int ch, int start)
    {
        int end = start + 1;
        while (end < Notes[ch].Length && Notes[ch][end] == Hold) end++;
        return end - start;
    }

    public void RemoveNote(int ch, int start)
    {
        int len = Length(ch, start);
        for (int i = start; i < start + len; i++) Notes[ch][i] = Off;
    }

    /// <summary>Writes a note of the given length at start, replacing what was there (and orphaned Holds after it).</summary>
    public void SetNote(int ch, int start, int note, int length)
    {
        var n = Notes[ch];
        length = Math.Clamp(length, 1, n.Length - start);
        n[start] = note;
        for (int i = start + 1; i < start + length; i++) n[i] = Hold;
        for (int i = start + length; i < n.Length && n[i] == Hold; i++) n[i] = Off;
    }
}

/// <summary>Sound settings of one channel. Times in ms, levels 0..1.</summary>
public sealed class Instrument
{
    /// <summary>Pulse width for the pulse channels: 0.125, 0.25, 0.5 or 0.75.</summary>
    public double Duty { get; set; } = 0.5;
    public double AttackMs { get; set; } = 2;
    public double DecayMs { get; set; } = 60;
    public double Sustain { get; set; } = 0.7;
    public double ReleaseMs { get; set; } = 40;
    public double Volume { get; set; } = 1;

    public Instrument Clone() => (Instrument)MemberwiseClone();

    public static Instrument Default(Channel ch) => ch switch
    {
        Channel.Pulse1 => new() { Duty = 0.25 },
        Channel.Pulse2 => new() { Duty = 0.5 },
        Channel.Triangle => new() { Sustain = 1, DecayMs = 0 },
        _ => new() { DecayMs = 120, Sustain = 0.1, ReleaseMs = 30, Volume = 0.8 },   // drum-like noise
    };
}

public sealed class Song
{
    public const int Channels = 4, MinNote = 36, MaxNote = 84;   // C2..C6

    public int Bpm { get; set; } = 120;
    /// <summary>Steps per pattern; one step = a 16th note.</summary>
    public int Steps { get; }
    public List<Pattern> Patterns { get; } = [];
    /// <summary>Play order as pattern indices.</summary>
    public List<int> Order { get; } = [];
    public Instrument[] Instruments { get; } = [.. Enumerable.Range(0, Channels).Select(c => Instrument.Default((Channel)c))];

    public Song(int steps = 16, bool empty = false)
    {
        if (steps < 1 || steps > 256) throw new ArgumentOutOfRangeException(nameof(steps), "Steps must be 1..256.");
        Steps = steps;
        if (empty) return;
        Patterns.Add(Pattern.Empty(steps));
        Order.Add(0);
    }

    public double StepSeconds => 60.0 / Bpm / 4;

    public static double Frequency(int note) => 440 * Math.Pow(2, (note - 69) / 12.0);

    public static string NoteName(int note) =>
        $"{"C C#D D#E F F#G G#A A#B "[(note % 12 * 2)..(note % 12 * 2 + 2)].Trim()}{note / 12 - 1}";

    /// <summary>Removes a pattern and fixes the order (drops its uses, shifts higher indices).</summary>
    public void RemovePattern(int index)
    {
        if (Patterns.Count <= 1) return;
        Patterns.RemoveAt(index);
        var order = Order.Where(i => i != index).Select(i => i > index ? i - 1 : i).ToList();
        Order.Clear();
        Order.AddRange(order.Count > 0 ? order : [0]);
    }

    public Song Clone()
    {
        var s = new Song(Steps, empty: true) { Bpm = Bpm };
        s.Patterns.AddRange(Patterns.Select(p => p.Clone()));
        s.Order.AddRange(Order);
        for (int i = 0; i < Channels; i++) s.Instruments[i] = Instruments[i].Clone();
        return s;
    }

    // ---------- .pxs file ----------

    record PatternDto(int[][] Notes, int[][] Volumes);
    // v1 stored notes only, under "Patterns"; v2 uses "PatternData" + "Instruments"
    record SongDto(int Version, int Bpm, int Steps, int[] Order, int[][][]? Patterns = null, PatternDto[]? PatternData = null, Instrument[]? Instruments = null);

    public static string Serialize(Song s) => JsonSerializer.Serialize(new SongDto(
        2, s.Bpm, s.Steps, [.. s.Order],
        PatternData: [.. s.Patterns.Select(p => new PatternDto(p.Notes, p.Volumes))],
        Instruments: s.Instruments));

    public static Song Deserialize(string json)
    {
        var d = JsonSerializer.Deserialize<SongDto>(json) ?? throw new InvalidDataException("Empty song file.");
        if (d.Bpm < 20 || d.Bpm > 400) throw new InvalidDataException("BPM must be 20..400.");
        var s = new Song(d.Steps, empty: true) { Bpm = d.Bpm };
        var patterns = d.PatternData ?? d.Patterns?.Select(n => new PatternDto(n, null!)).ToArray() ?? [];
        foreach (var p in patterns)
        {
            if (p.Notes.Length != Channels || p.Notes.Any(c => c.Length != d.Steps)) throw new InvalidDataException("Pattern has wrong size.");
            if (p.Notes.Any(c => c.Any(n => n < Pattern.Hold || n > 127))) throw new InvalidDataException("Pattern has invalid notes.");
            var vols = p.Volumes ?? Pattern.Empty(d.Steps).Volumes;
            if (vols.Length != Channels || vols.Any(c => c.Length != d.Steps)) throw new InvalidDataException("Pattern volumes have wrong size.");
            s.Patterns.Add(new Pattern { Notes = p.Notes, Volumes = [.. vols.Select(c => c.Select(v => Math.Clamp(v, 0, Pattern.MaxVolume)).ToArray())] });
        }
        if (s.Patterns.Count == 0 || d.Order.Length == 0 || d.Order.Any(i => i < 0 || i >= s.Patterns.Count))
            throw new InvalidDataException("Song order refers to missing patterns.");
        s.Order.AddRange(d.Order);
        if (d.Instruments is { Length: Channels } inst)
            for (int i = 0; i < Channels; i++) s.Instruments[i] = Sanitize(inst[i]);
        return s;
    }

    static Instrument Sanitize(Instrument i) => new()
    {
        Duty = Math.Clamp(i.Duty, 0.05, 0.95),
        AttackMs = Math.Clamp(i.AttackMs, 0, 5000),
        DecayMs = Math.Clamp(i.DecayMs, 0, 5000),
        Sustain = Math.Clamp(i.Sustain, 0, 1),
        ReleaseMs = Math.Clamp(i.ReleaseMs, 0, 5000),
        Volume = Math.Clamp(i.Volume, 0, 1),
    };

    public static void Save(Song s, string path) => File.WriteAllText(path, Serialize(s));
    public static Song Load(string path) => Deserialize(File.ReadAllText(path));
}
