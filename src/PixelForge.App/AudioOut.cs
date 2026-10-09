using NAudio.Wave;
using PixelForge.Core;

namespace PixelForge.App;

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

    readonly WaveOutEvent _out = new() { DesiredLatency = 120 };
    readonly object _lock = new();
    readonly List<(float[] Data, int Pos)> _oneShots = [];
    SongStream? _song;

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

    public int Read(float[] buffer, int offset, int count)
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
