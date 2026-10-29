using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pixelkiln.Core;

/// <summary>Reads WAV files (PCM 8/16/24/32-bit, float 32-bit, any channel count) as mono floats at <see cref="Synth.SampleRate"/>.</summary>
public static class WavReader
{
    public const int MaxSeconds = 600;

    public static float[] Load(string path)
    {
        using var s = File.OpenRead(path);
        return Read(s);
    }

    public static float[] Read(Stream stream)
    {
        var r = new BinaryReader(stream);
        try
        {
            if (!r.ReadBytes(4).AsSpan().SequenceEqual("RIFF"u8)) throw new InvalidDataException("Not a WAV file (no RIFF header).");
            r.ReadInt32();
            if (!r.ReadBytes(4).AsSpan().SequenceEqual("WAVE"u8)) throw new InvalidDataException("Not a WAV file (no WAVE tag).");
            int format = 0, channels = 0, rate = 0, bits = 0;
            while (true)
            {
                string id = System.Text.Encoding.ASCII.GetString(r.ReadBytes(4));
                int size = r.ReadInt32();
                if (size < 0) throw new InvalidDataException("Damaged WAV chunk.");
                if (id == "fmt ")
                {
                    var fmt = r.ReadBytes(size);
                    format = BitConverter.ToUInt16(fmt, 0);
                    channels = BitConverter.ToUInt16(fmt, 2);
                    rate = BitConverter.ToInt32(fmt, 4);
                    bits = BitConverter.ToUInt16(fmt, 14);
                    if (format == 0xFFFE && size >= 26) format = BitConverter.ToUInt16(fmt, 24);   // WAVE_FORMAT_EXTENSIBLE: real format in the sub-format GUID
                }
                else if (id == "data")
                {
                    if (channels is < 1 or > 16 || rate is < 1000 or > 384000) throw new InvalidDataException("Unsupported WAV: bad channel count or sample rate.");
                    if (!(format == 1 && bits is 8 or 16 or 24 or 32) && !(format == 3 && bits == 32))
                        throw new InvalidDataException($"Unsupported WAV encoding (format {format}, {bits} bits). Use PCM or 32-bit float.");
                    int frameBytes = channels * bits / 8;
                    long frames = Math.Min(size, stream.Length - stream.Position) / frameBytes;
                    if (frames > (long)rate * MaxSeconds) throw new InvalidDataException($"WAV is longer than {MaxSeconds / 60} minutes.");
                    var data = r.ReadBytes((int)(frames * frameBytes));
                    var mono = new float[frames];
                    for (long f = 0; f < frames; f++)
                    {
                        float sum = 0;
                        for (int c = 0; c < channels; c++) sum += Sample(data, (int)(f * frameBytes + c * bits / 8), format, bits);
                        mono[f] = sum / channels;
                    }
                    return Resample(mono, rate, Synth.SampleRate);
                }
                else stream.Seek(size + (size & 1), SeekOrigin.Current);   // chunks are word-aligned
            }
        }
        catch (EndOfStreamException) { throw new InvalidDataException("WAV file ends early (no data chunk)."); }
    }

    static float Sample(byte[] d, int i, int format, int bits) => (format, bits) switch
    {
        (3, 32) => BitConverter.ToSingle(d, i),
        (_, 8) => (d[i] - 128) / 128f,
        (_, 16) => BitConverter.ToInt16(d, i) / 32768f,
        (_, 24) => (d[i] | d[i + 1] << 8 | (sbyte)d[i + 2] << 16) / 8388608f,
        _ => BitConverter.ToInt32(d, i) / 2147483648f,
    };

    /// <summary>Linear resampling. ponytail: no anti-alias filter; fine for game sounds, add a windowed-sinc if downsampled music sounds harsh.</summary>
    static float[] Resample(float[] src, int from, int to)
    {
        if (from == to || src.Length == 0) return src;
        var dst = new float[(long)src.Length * to / from];
        double step = (double)from / to;
        for (int i = 0; i < dst.Length; i++)
        {
            double p = i * step;
            int a = (int)p;
            float t = (float)(p - a);
            dst[i] = a + 1 < src.Length ? src[a] * (1 - t) + src[a + 1] * t : src[^1];
        }
        return dst;
    }
}

/// <summary>Sample-buffer edits shared by the mixer and playlists: slice, fade, join, gain.</summary>
public static class AudioEdit
{
    public static int Samples(int ms) => (int)((long)ms * Synth.SampleRate / 1000);
    public static int Ms(int samples) => (int)((long)samples * 1000 / Synth.SampleRate);

    /// <summary>Copy of [fromMs, fromMs + lengthMs); length null = to the end. Out-of-range parts are dropped.</summary>
    public static float[] Slice(float[] s, int fromMs, int? lengthMs)
    {
        int a = Math.Clamp(Samples(fromMs), 0, s.Length);
        int b = lengthMs is int l ? Math.Clamp(a + Samples(l), a, s.Length) : s.Length;
        return s[a..b];
    }

    /// <summary>Linear fade-in and fade-out in place (each at most the whole buffer).</summary>
    public static float[] Fade(float[] s, int inMs, int outMs)
    {
        int fi = Math.Min(Samples(inMs), s.Length), fo = Math.Min(Samples(outMs), s.Length);
        for (int i = 0; i < fi; i++) s[i] *= (float)i / fi;
        for (int i = 0; i < fo; i++) s[s.Length - 1 - i] *= (float)i / fo;
        return s;
    }

    /// <summary>b after a; the last crossfadeMs of a overlap the start of b with a linear crossfade.</summary>
    public static float[] Join(float[] a, float[] b, int crossfadeMs = 0)
    {
        int x = Math.Min(Samples(crossfadeMs), Math.Min(a.Length, b.Length));
        var o = new float[a.Length + b.Length - x];
        a.CopyTo(o, 0);
        for (int i = 0; i < x; i++)
        {
            float t = (float)(i + 1) / (x + 1);
            o[a.Length - x + i] = a[a.Length - x + i] * (1 - t) + b[i] * t;
        }
        Array.Copy(b, x, o, a.Length, b.Length - x);
        return o;
    }

    public static float[] Repeat(float[] s, int times)
    {
        var o = new float[(long)s.Length * Math.Max(1, times)];
        for (int k = 0; k < Math.Max(1, times); k++) s.CopyTo(o, (long)k * s.Length);
        return o;
    }

    public static float Peak(float[] s) => s.Length == 0 ? 0 : s.Max(Math.Abs);
}

public sealed class MixClip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Song, sound, WAV or another mix.</summary>
    public Guid AssetId { get; set; }
    public int StartMs { get; set; }
    /// <summary>Cut: skip this much of the source …</summary>
    public int OffsetMs { get; set; }
    /// <summary>… and keep this much (null = to the end).</summary>
    public int? LengthMs { get; set; }
    public double Gain { get; set; } = 1;
    public int FadeInMs { get; set; }
    public int FadeOutMs { get; set; }
    /// <summary>The cut part played this many times back to back (loops).</summary>
    public int Repeat { get; set; } = 1;
    public MixClip Clone() => (MixClip)MemberwiseClone();
}

public sealed class MixTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Track";
    public double Volume { get; set; } = 1;
    public bool Mute { get; set; }
    public bool Solo { get; set; }
    public List<MixClip> Clips { get; set; } = [];
    public MixTrack Clone() => new() { Id = Id, Name = Name, Volume = Volume, Mute = Mute, Solo = Solo, Clips = [.. Clips.Select(c => c.Clone())] };
}

/// <summary>Audio arrangement (.pxmix): tracks of clips with cuts, fades and repeats, plus the loop region a game uses for music.</summary>
public sealed class Mix
{
    public const int Version = 1;
    public double MasterVolume { get; set; } = 1;
    public List<MixTrack> Tracks { get; set; } = [new() { Name = "Music" }, new() { Name = "Effects" }];
    /// <summary>Game music loop: play to LoopEndMs, jump back to LoopStartMs. Both null = no loop.</summary>
    public int? LoopStartMs { get; set; }
    public int? LoopEndMs { get; set; }

    public Mix Clone() => new() { MasterVolume = MasterVolume, LoopStartMs = LoopStartMs, LoopEndMs = LoopEndMs, Tracks = [.. Tracks.Select(t => t.Clone())] };

    public IEnumerable<Guid> ReferencedAssets() => Tracks.SelectMany(t => t.Clips).Select(c => c.AssetId).Distinct();

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    record Dto(int Version, double MasterVolume, int? LoopStartMs, int? LoopEndMs, List<MixTrack> Tracks);

    public static string Serialize(Mix m) => JsonSerializer.Serialize(new Dto(Version, m.MasterVolume, m.LoopStartMs, m.LoopEndMs, m.Tracks), Json);

    public static Mix Deserialize(string json)
    {
        var d = JsonSerializer.Deserialize<Dto>(json, Json) ?? throw new InvalidDataException("Empty mix file.");
        var m = new Mix { MasterVolume = Math.Clamp(d.MasterVolume, 0, 4), LoopStartMs = d.LoopStartMs, LoopEndMs = d.LoopEndMs, Tracks = d.Tracks ?? [] };
        if (m.LoopStartMs is int ls && m.LoopEndMs is int le && (ls < 0 || le <= ls)) throw new InvalidDataException("The loop must end after it starts.");
        if ((m.LoopStartMs is null) != (m.LoopEndMs is null)) throw new InvalidDataException("The loop needs both a start and an end.");
        foreach (var t in m.Tracks)
        {
            t.Clips ??= [];
            t.Volume = Math.Clamp(t.Volume, 0, 4);
            foreach (var c in t.Clips)
                if (c.StartMs < 0 || c.OffsetMs < 0 || c.LengthMs is <= 0 || c.FadeInMs < 0 || c.FadeOutMs < 0 || c.Repeat is < 1 or > 1000 || c.Gain is < 0 or > 8)
                    throw new InvalidDataException($"Clip on track “{t.Name}” has a negative time, a repeat outside 1–1000 or a gain outside 0–8.");
        }
        var ids = m.Tracks.Select(t => t.Id).Concat(m.Tracks.SelectMany(t => t.Clips).Select(c => c.Id)).ToList();
        if (ids.Count != ids.Distinct().Count()) throw new InvalidDataException("The mix contains duplicate ids.");
        return m;
    }

    public static void Save(Mix m, string path) => DocumentFile.Save(path, Serialize(m), Version);
    public static Mix Load(string path) => DocumentFile.Load(path, "mix", Version, Deserialize);
}

/// <summary>Named list of songs/mixes a game plays in order (project setting).</summary>
public sealed class Playlist
{
    public string Name { get; set; } = "Playlist";
    public List<Guid> Items { get; set; } = [];
    /// <summary>Start over after the last item.</summary>
    public bool Loop { get; set; } = true;
    public bool Shuffle { get; set; }
}

/// <summary>Turns project audio assets into samples (cached by path and write time) and renders mixes and playlists.</summary>
public sealed class AudioLibrary(Project project)
{
    readonly Dictionary<string, float[]> _cache = new(StringComparer.OrdinalIgnoreCase);
    public Project Project { get; } = project;

    public static bool IsAudio(AssetType t) => t is AssetType.Song or AssetType.Sound or AssetType.Audio or AssetType.Mix;

    /// <summary>Samples of an asset; throws InvalidDataException when it is missing, not audio, unreadable or a mix that contains itself.</summary>
    public float[] Samples(Guid id) => Samples(id, []);

    float[] Samples(Guid id, HashSet<Guid> inProgress)
    {
        var entry = Project.Find(id) ?? throw new InvalidDataException($"Asset {id} is not in the project.");
        if (!IsAudio(entry.Type)) throw new InvalidDataException($"“{entry.Path}” is not audio.");
        string path = Project.FullPath(entry);
        if (!File.Exists(path)) throw new InvalidDataException($"“{entry.Path}” is missing.");
        string key = $"{path}|{File.GetLastWriteTimeUtc(path).Ticks}";
        if (_cache.TryGetValue(key, out var hit)) return hit;
        if (!inProgress.Add(id)) throw new InvalidDataException($"The mix “{entry.Path}” contains itself.");
        try
        {
            var s = entry.Type switch
            {
                AssetType.Song => Synth.Render(Song.Load(path)),
                AssetType.Sound => Sfx.Load(path).Render(),
                AssetType.Audio => WavReader.Load(path),
                _ => Render(Mix.Load(path), inProgress),
            };
            return _cache[key] = s;
        }
        catch (IOException ex) { throw new InvalidDataException($"“{entry.Path}” could not be read: {ex.Message}"); }
        finally { inProgress.Remove(id); }
    }

    public float[] Render(Mix m) => Render(m, []);

    float[] Render(Mix m, HashSet<Guid> inProgress)
    {
        bool solo = m.Tracks.Any(t => t.Solo);
        var parts = new List<(int At, float[] Data, float Gain)>();
        foreach (var t in m.Tracks.Where(t => !t.Mute && (!solo || t.Solo)))
            foreach (var c in t.Clips)
            {
                var piece = AudioEdit.Fade(AudioEdit.Repeat(AudioEdit.Slice(Samples(c.AssetId, inProgress), c.OffsetMs, c.LengthMs), c.Repeat), c.FadeInMs, c.FadeOutMs);
                parts.Add((AudioEdit.Samples(c.StartMs), piece, (float)(c.Gain * t.Volume * m.MasterVolume)));
            }
        int length = Math.Max(parts.Count == 0 ? 0 : parts.Max(p => p.At + p.Data.Length), m.LoopEndMs is int le ? AudioEdit.Samples(le) : 0);
        var o = new float[length];
        foreach (var (at, data, gain) in parts)
            for (int i = 0; i < data.Length; i++) o[at + i] += data[i] * gain;
        return o;
    }

    /// <summary>Clip length in ms as placed (after cut and repeats); null when the source cannot be read.</summary>
    public int? ClipMs(MixClip c)
    {
        try
        {
            int total = AudioEdit.Ms(Samples(c.AssetId).Length), from = Math.Min(c.OffsetMs, total);
            return Math.Min(total - from, c.LengthMs ?? int.MaxValue) * c.Repeat;
        }
        catch (InvalidDataException) { return null; }
    }

    /// <summary>Every item once, in order, with an optional crossfade (preview and export of a playlist).</summary>
    public float[] Render(Playlist p, int crossfadeMs = 0) =>
        p.Items.Select(Samples).Aggregate(Array.Empty<float>(), (acc, s) => AudioEdit.Join(acc, s, crossfadeMs));
}
