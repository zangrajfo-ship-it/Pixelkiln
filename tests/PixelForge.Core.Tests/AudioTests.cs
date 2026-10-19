using PixelForge.Core;

namespace PixelForge.Core.Tests;

public sealed class AudioTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "pf-audio-" + Guid.NewGuid().ToString("N"));

    public AudioTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    string P(string name) => Path.Combine(_dir, name);

    static float[] Ramp(int n) => Enumerable.Range(0, n).Select(i => (float)i / n).ToArray();

    [Fact]
    public void Wav_round_trip_has_exact_length_and_stereo_or_other_rates_are_converted()
    {
        var s = Ramp(4410);
        WavWriter.Save(P("a.wav"), s);
        Assert.Equal(44 + s.Length * 2, new FileInfo(P("a.wav")).Length);
        Assert.False(File.Exists(P("a.wav.tmp")));
        var back = WavReader.Load(P("a.wav"));
        Assert.Equal(s.Length, back.Length);
        Assert.InRange(back[2000] - s[2000], -0.001f, 0.001f);

        // 22050 Hz stereo 16-bit: half the frames, channels averaged
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        int frames = 100;
        w.Write("RIFF"u8); w.Write(36 + frames * 4); w.Write("WAVE"u8);
        w.Write("LIST"u8); w.Write(3); w.Write(new byte[] { 1, 2, 3, 0 });   // odd chunk with pad byte
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(22050); w.Write(22050 * 4); w.Write((short)4); w.Write((short)16);
        w.Write("data"u8); w.Write(frames * 4);
        for (int i = 0; i < frames; i++) { w.Write((short)16384); w.Write((short)0); }
        ms.Position = 0;
        var st = WavReader.Read(ms);
        Assert.Equal(200, st.Length);
        Assert.InRange(st[50], 0.24f, 0.26f);

        Assert.Throws<InvalidDataException>(() => WavReader.Read(new MemoryStream("RIFF\0\0\0\0WAVE"u8.ToArray())));
        Assert.Throws<InvalidDataException>(() => WavReader.Read(new MemoryStream(new byte[12])));
    }

    [Fact]
    public void Edits_slice_fade_join_and_repeat()
    {
        var s = Enumerable.Repeat(1f, AudioEdit.Samples(1000)).ToArray();
        Assert.Equal(AudioEdit.Samples(250), AudioEdit.Slice(s, 500, 250).Length);
        Assert.Equal(AudioEdit.Samples(500), AudioEdit.Slice(s, 500, null).Length);
        Assert.Empty(AudioEdit.Slice(s, 5000, 100));

        var f = AudioEdit.Fade((float[])s.Clone(), 100, 100);
        Assert.Equal(0, f[0]);
        Assert.Equal(1, f[s.Length / 2]);
        Assert.InRange(f[^1], 0, 0.01f);

        var j = AudioEdit.Join(s, s, 100);
        Assert.Equal(2 * s.Length - AudioEdit.Samples(100), j.Length);
        Assert.All(j, x => Assert.InRange(x, 0.99f, 1.01f));   // equal-level crossfade keeps the level
        Assert.Equal(3 * s.Length, AudioEdit.Repeat(s, 3).Length);
    }

    [Fact]
    public void Mix_renders_tracks_with_cuts_repeats_mute_solo_and_round_trips()
    {
        var p = Project.Create(_dir, "G");
        WavWriter.Save(P("tone.wav"), Enumerable.Repeat(0.5f, AudioEdit.Samples(1000)).ToArray());
        var tone = p.Add(P("tone.wav"));
        var m = new Mix();
        m.Tracks[0].Clips.Add(new MixClip { AssetId = tone.Id, StartMs = 0, OffsetMs = 200, LengthMs = 300, Repeat = 2 });
        m.Tracks[1].Clips.Add(new MixClip { AssetId = tone.Id, StartMs = 500, Gain = 0.5 });
        var lib = new AudioLibrary(p);

        var o = lib.Render(m);
        Assert.Equal(AudioEdit.Samples(1500), o.Length);
        Assert.InRange(o[AudioEdit.Samples(100)], 0.49f, 0.51f);
        Assert.InRange(o[AudioEdit.Samples(550)], 0.74f, 0.76f);   // overlap: 0.5 + 0.25
        Assert.Equal(600, lib.ClipMs(m.Tracks[0].Clips[0]));

        m.Tracks[1].Solo = true;
        Assert.Equal(0, lib.Render(m)[AudioEdit.Samples(100)]);
        m.Tracks[1].Mute = true;
        Assert.Empty(lib.Render(m));
        m.LoopStartMs = 0; m.LoopEndMs = 2000;
        Assert.Equal(AudioEdit.Samples(2000), lib.Render(m).Length);   // loop end pads with silence

        var t = Mix.Deserialize(Mix.Serialize(m));
        Assert.Equal((0, 2000, 2), (t.LoopStartMs, t.LoopEndMs, t.Tracks[0].Clips[0].Repeat));
        Assert.Throws<InvalidDataException>(() => Mix.Deserialize(Mix.Serialize(m).Replace("\"LoopEndMs\": 2000", "\"LoopEndMs\": 0")));
        Assert.Throws<InvalidDataException>(() => Mix.Deserialize(Mix.Serialize(m).Replace("\"Repeat\": 2", "\"Repeat\": 0")));
    }

    [Fact]
    public void Mix_inside_itself_is_refused_and_references_are_tracked()
    {
        var p = Project.Create(_dir, "G");
        Mix.Save(new Mix(), P("a.pxmix"));
        var a = p.Add(P("a.pxmix"));
        var m = new Mix();
        m.Tracks[0].Clips.Add(new MixClip { AssetId = a.Id });
        Mix.Save(m, P("a.pxmix"));
        var ex = Assert.Throws<InvalidDataException>(() => new AudioLibrary(p).Samples(a.Id));
        Assert.Contains("contains itself", ex.Message);
        Assert.Contains(a.Id, p.UsersOf(a.Id).Select(x => x.Id));
    }

    [Fact]
    public void Playlist_joins_items_and_is_saved_in_the_project()
    {
        var p = Project.Create(_dir, "G");
        WavWriter.Save(P("x.wav"), new float[441]);
        var x = p.Add(P("x.wav"));
        p.Settings.Playlists.Add(new Playlist { Name = "World 1", Items = [x.Id, x.Id] });
        p.Save();
        var q = Project.Load(p.FilePath);
        Assert.Equal("World 1", q.Settings.Playlists[0].Name);
        Assert.Equal(882, new AudioLibrary(q).Render(q.Settings.Playlists[0]).Length);
    }
}
