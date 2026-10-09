using System.Text.Json;

namespace PixelForge.Core;

/// <summary>Fixed chiptune channels, NES-style.</summary>
public enum Channel { Pulse1, Pulse2, Triangle, Noise }

public sealed class Pattern
{
    public const int Off = -1;
    /// <summary>[channel][step] = MIDI note (60 = C4) or Off. One note per channel per step.</summary>
    public int[][] Notes { get; init; } = [];

    public static Pattern Empty(int steps) =>
        new() { Notes = [.. Enumerable.Range(0, Song.Channels).Select(_ => Enumerable.Repeat(Off, steps).ToArray())] };

    public Pattern Clone() => new() { Notes = [.. Notes.Select(c => (int[])c.Clone())] };
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
        return s;
    }

    // ---------- .pxs file ----------

    record SongDto(int Version, int Bpm, int Steps, int[][][] Patterns, int[] Order);

    public static string Serialize(Song s) =>
        JsonSerializer.Serialize(new SongDto(1, s.Bpm, s.Steps, [.. s.Patterns.Select(p => p.Notes)], [.. s.Order]));

    public static Song Deserialize(string json)
    {
        var d = JsonSerializer.Deserialize<SongDto>(json) ?? throw new InvalidDataException("Empty song file.");
        if (d.Bpm < 20 || d.Bpm > 400) throw new InvalidDataException("BPM must be 20..400.");
        var s = new Song(d.Steps, empty: true) { Bpm = d.Bpm };
        foreach (var notes in d.Patterns)
        {
            if (notes.Length != Channels || notes.Any(c => c.Length != d.Steps)) throw new InvalidDataException("Pattern has wrong size.");
            s.Patterns.Add(new Pattern { Notes = notes });
        }
        if (s.Patterns.Count == 0 || d.Order.Length == 0 || d.Order.Any(i => i < 0 || i >= s.Patterns.Count))
            throw new InvalidDataException("Song order refers to missing patterns.");
        s.Order.AddRange(d.Order);
        return s;
    }

    public static void Save(Song s, string path) => File.WriteAllText(path, Serialize(s));
    public static Song Load(string path) => Deserialize(File.ReadAllText(path));
}
