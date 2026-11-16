using NAudio.Wave;
using Pixelkiln.Core;

namespace Pixelkiln.App;

/// <summary>
/// One shared audio output: an optional looping song stream plus short one-shot sounds
/// (note previews, sound effects), mixed on NAudio's playback thread.
/// </summary>
sealed class AudioOut : ISampleProvider, IDisposable
{
    static AudioOut? _shared;
    /// <summary>Opened on first use, so the app starts without touching the audio device.</summary>
    public static AudioOut Shared => _shared ??= new AudioOut();
    public static void DisposeShared() => _shared?.Dispose();

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Synth.SampleRate, 1);

    // two buffers of 60 ms: about 120 ms from a key press to its sound
    readonly WaveOut _out = new() { BufferMilliseconds = 60, NumberOfBuffers = 2 };
    float[] _mix = [];       // the song stream and the mixing work on an array; NAudio asks for a span
    readonly object _lock = new();
    readonly List<(float[] Data, int Pos)> _oneShots = [];
    SongStream? _song;
    float[]? _clip;          // audio studio playback: one clip with a position the UI can follow
    int _clipPos, _loopFrom = -1, _loopTo = -1;

    AudioOut()
    {
        _out.Init(this);
        _out.Play();   // runs all the time and outputs silence when idle
    }

    public SongStream? Song
    {
        get { lock (_lock) return _song; }
        set { lock (_lock) _song = value; }
    }

    public void PlayOnce(float[] samples)
    {
        lock (_lock)
        {
            _oneShots.Clear();   // a new preview replaces the previous one
            _oneShots.Add((samples, 0));
        }
    }

    /// <summary>Plays samples from a position; with a loop region it jumps back from loopTo to loopFrom until stopped.</summary>
    public void PlayClip(float[] samples, int from = 0, int loopFrom = -1, int loopTo = -1)
    {
        lock (_lock)
        {
            _clip = samples;
            _clipPos = Math.Clamp(from, 0, samples.Length);
            bool loop = loopFrom >= 0 && loopTo > loopFrom && loopTo <= samples.Length;
            (_loopFrom, _loopTo) = loop ? (loopFrom, loopTo) : (-1, -1);
        }
    }

    public void StopClip() { lock (_lock) _clip = null; }

    /// <summary>Sample position of the playing clip; null when nothing plays (finished or stopped).</summary>
    public int? ClipPosition { get { lock (_lock) return _clip is null ? null : _clipPos; } }

    public int Read(Span<float> output)
    {
        int count = output.Length;
        if (_mix.Length < count) _mix = new float[count];
        Read(_mix, 0, count);
        _mix.AsSpan(0, count).CopyTo(output);
        return count;
    }

    int Read(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);
        lock (_lock)
        {
            try
            {
                if (_song is { Finished: false } s) s.Read(buffer, offset, count);
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException)
            {
                // the song was edited mid-read (pattern removed etc.); this block stays silent
            }
            for (int i = 0; _clip is { } c && i < count; i++)
            {
                if (_clipPos == _loopTo) _clipPos = _loopFrom;
                if (_clipPos >= c.Length) { _clip = null; break; }
                buffer[offset + i] += c[_clipPos++];
            }
            for (int k = _oneShots.Count - 1; k >= 0; k--)
            {
                var (data, pos) = _oneShots[k];
                int n = Math.Min(count, data.Length - pos);
                for (int i = 0; i < n; i++) buffer[offset + i] += data[pos + i];
                if (pos + n >= data.Length) _oneShots.RemoveAt(k);
                else _oneShots[k] = (data, pos + n);
            }
        }
        for (int i = offset; i < offset + count; i++) buffer[i] = Math.Clamp(buffer[i], -1f, 1f);
        return count;
    }

    public void Dispose()
    {
        _out.Stop();
        _out.Dispose();
    }
}
