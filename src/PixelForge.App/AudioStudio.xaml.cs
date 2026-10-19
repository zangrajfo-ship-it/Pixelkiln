using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.App;

/// <summary>
/// Audio studio: arranges project songs, sounds, WAVs and other mixes on tracks (cut, fade, repeat, join),
/// with a loop region for game music, playback with a following playhead, WAV export, and the project's playlists.
/// </summary>
public partial class AudioStudio : UserControl, IEditor
{
    const int TrackHeight = 56, RulerHeight = 22;
    static readonly double[] Zooms = [10, 20, 40, 80, 160, 320];   // pixels per second

    Mix _mix = new();
    readonly History<Mix> _history = new(m => m.Clone(), App.Settings.UndoLimit);
    string? _path;
    bool _dirty, _refreshing;
    double _pxPerSec = 80;
    int _playheadMs;
    AudioLibrary? _lib;
    MixTrack? _track;
    MixClip? _clip;
    Line? _playhead;

    // dragging a clip
    Point? _dragFrom;
    int _dragStartMs;
    bool _dragSnapshotDone;

    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    int _playOffset;   // the rendered mix starts at this sample

    public AudioStudio()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => FollowPlayback();
        App.ProjectChanged += () => { if (IsLoaded) { _lib = null; RefreshAll(); } };
        Loaded += Init;
        IsVisibleChanged += (_, _) => { UpdateTitle(); if (!IsVisible) StopPlay(); else if (IsLoaded) { RefreshLibrary(); Rebuild(); } };
        IsKeyboardFocusedChanged += (_, _) => Scroller.BorderBrush = IsKeyboardFocused || Timeline.IsKeyboardFocused ? (Brush)FindResource("FocusRing") : Brushes.Transparent;

        void Init(object s, RoutedEventArgs e) { Loaded -= Init; LoadMix(_mix, null); }
    }

    AudioLibrary? Lib => App.Project is { } p ? _lib ??= new AudioLibrary(p) : null;

    // ---------- document ----------

    void LoadMix(Mix m, string? path)
    {
        StopPlay();
        (_mix, _path, _dirty) = (m, path, false);
        _history.Clear();
        _track = _mix.Tracks.FirstOrDefault();
        _clip = null;
        _playheadMs = 0;
        RefreshAll();
    }

    void Snapshot()
    {
        _history.Push(_mix);
        _dirty = true;
        UpdateTitle();
    }

    void RefreshAll()
    {
        RefreshLibrary();
        RefreshPlaylists();
        RefreshProps();
        Rebuild();
        UpdateTitle();
        Status.Text = App.Project is null ? "Open a project (Project menu): mixes are built from project audio." : StatusLine();
    }

    string StatusLine() => $"Playhead {_playheadMs / 1000.0:0.00} s · {_mix.Tracks.Count} tracks · {_mix.Tracks.Sum(t => t.Clips.Count)} clips"
                           + (_clip is not null ? $" · selected: {AssetName(_clip.AssetId)} at {_clip.StartMs / 1000.0:0.00} s" : "");

    void UpdateTitle()
    {
        if (IsVisible && Window.GetWindow(this) is { } win)
            win.Title = $"PixelForge — {(_path is null ? "untitled mix" : System.IO.Path.GetFileName(_path))}{(_dirty ? " *" : "")}";
    }

    string AssetName(Guid id) => App.Project?.Find(id)?.Path ?? "missing asset";

    // ---------- library ----------

    void RefreshLibrary()
    {
        var keep = (LibraryList.SelectedItem as ListBoxItem)?.Tag;
        LibraryList.ItemsSource = App.Project?.Assets.Where(a => AudioLibrary.IsAudio(a.Type)).OrderBy(a => a.Type).ThenBy(a => a.Path)
            .Select(a => new ListBoxItem { Content = $"{a.Type} · {a.Path}", Tag = a.Id }).ToList();
        LibraryList.SelectedItem = LibraryList.Items.Cast<ListBoxItem>().FirstOrDefault(i => Equals(i.Tag, keep));
    }

    Guid? LibrarySelection => (LibraryList.SelectedItem as ListBoxItem)?.Tag as Guid?;

    bool TryRender(Func<float[]> render, out float[] samples)
    {
        try { samples = render(); return true; }
        catch (InvalidDataException ex) { Ui.Error(ex); samples = []; return false; }
    }

    void OnLibraryPreview(object sender, RoutedEventArgs e)
    {
        if (LibrarySelection is Guid id && Lib is { } lib && TryRender(() => lib.Samples(id), out var s)) AudioOut.Shared.PlayOnce(s);
    }

    void OnLibraryAdd(object? sender, RoutedEventArgs? e)
    {
        if (LibrarySelection is Guid id) AddClip(id);
    }

    void OnLibraryDouble(object sender, MouseButtonEventArgs e) => OnLibraryAdd(null, null);

    void OnLibraryKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        OnLibraryAdd(null, null);
        e.Handled = true;
    }

    /// <summary>Adds an asset as a clip on the selected track at the playhead (also used when a WAV is opened from the asset browser).</summary>
    public void AddClip(Guid assetId)
    {
        if (App.Project?.Find(assetId) is not { } a || !AudioLibrary.IsAudio(a.Type)) return;
        Snapshot();
        _track ??= _mix.Tracks.FirstOrDefault() ?? AddTrackNoSnapshot();
        _clip = new MixClip { AssetId = assetId, StartMs = _playheadMs };
        _track.Clips.Add(_clip);
        RefreshProps();
        Rebuild();
    }

    // ---------- timeline drawing ----------

    double X(int ms) => ms * _pxPerSec / 1000;
    int Ms(double x) => Math.Max(0, (int)Math.Round(x * 1000 / _pxPerSec));

    int EndMs => _mix.Tracks.SelectMany(t => t.Clips).Select(c => c.StartMs + (Lib?.ClipMs(c) ?? 1000)).DefaultIfEmpty(0).Max();

    void Rebuild()
    {
        Timeline.Children.Clear();
        int lengthMs = Math.Max(Math.Max(EndMs, _mix.LoopEndMs ?? 0) + 4000, 20000);
        Timeline.Width = X(lengthMs);
        Timeline.Height = RulerHeight + Math.Max(1, _mix.Tracks.Count) * TrackHeight;
        var muted = (Brush)FindResource("MutedText");

        // ruler: a mark every second, labels where there is room
        int labelEvery = _pxPerSec >= 40 ? 1 : _pxPerSec >= 20 ? 2 : 5;
        for (int s = 0; s * 1000 <= lengthMs; s++)
        {
            Timeline.Children.Add(new Line { X1 = X(s * 1000), X2 = X(s * 1000), Y1 = RulerHeight - 6, Y2 = Timeline.Height, Stroke = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)) });
            if (s % labelEvery == 0) Put(new TextBlock { Text = $"{s}s", FontSize = 10, Foreground = muted }, X(s * 1000) + 2, 2);
        }
        if (_mix.LoopStartMs is int ls && _mix.LoopEndMs is int le)
            Put(new Rectangle { Width = X(le) - X(ls), Height = 6, Fill = Brushes.Orange, ToolTip = "Loop region", IsHitTestVisible = false }, X(ls), RulerHeight - 7);

        for (int ti = 0; ti < _mix.Tracks.Count; ti++)
        {
            var t = _mix.Tracks[ti];
            double y = RulerHeight + ti * TrackHeight;
            Put(new Rectangle { Width = Timeline.Width, Height = TrackHeight - 1, Fill = new SolidColorBrush(t == _track ? Color.FromRgb(0x2E, 0x2E, 0x38) : Color.FromRgb(0x26, 0x26, 0x26)), IsHitTestVisible = false }, 0, y);
            Put(new TextBlock { Text = $"{t.Name}{(t.Mute ? " (muted)" : "")}{(t.Solo ? " (solo)" : "")}", FontSize = 10, Foreground = muted, IsHitTestVisible = false }, 4, y + 1);
            foreach (var c in t.Clips) DrawClip(c, y);
        }
        _playhead = new Line { Y1 = 0, Y2 = Timeline.Height, Stroke = Brushes.White, StrokeThickness = 1.5, IsHitTestVisible = false };
        Timeline.Children.Add(_playhead);
        MovePlayhead(_playheadMs);
    }

    void Put(UIElement e, double x, double y)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        Timeline.Children.Add(e);
    }

    void DrawClip(MixClip c, double trackY)
    {
        float[]? src = null;
        try { src = Lib?.Samples(c.AssetId); } catch (InvalidDataException) { }
        int ms = Lib?.ClipMs(c) ?? 1000;
        double w = Math.Max(4, X(ms)), h = TrackHeight - 16;
        bool sel = c == _clip;
        var g = new Grid { Width = w, Height = h, Tag = c, ClipToBounds = true };
        g.Children.Add(new Rectangle
        {
            Fill = new SolidColorBrush(src is null ? Color.FromRgb(0x80, 0x20, 0x20) : Color.FromRgb(0x2F, 0x5D, 0x8A)),
            Stroke = sel ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x5B, 0x8F, 0xC4)), StrokeThickness = sel ? 2 : 1, RadiusX = 3, RadiusY = 3,
        });
        if (src is not null && ms > 0) g.Children.Add(Waveform(AudioEdit.Slice(src, c.OffsetMs, c.LengthMs), c, w, h));
        // fades as slopes
        if (c.FadeInMs > 0) g.Children.Add(new Line { X1 = 0, Y1 = h, X2 = X(c.FadeInMs), Y2 = 0, Stroke = Brushes.Gold, IsHitTestVisible = false });
        if (c.FadeOutMs > 0) g.Children.Add(new Line { X1 = w - X(c.FadeOutMs), Y1 = 0, X2 = w, Y2 = h, Stroke = Brushes.Gold, IsHitTestVisible = false });
        g.Children.Add(new TextBlock { Text = System.IO.Path.GetFileName(AssetName(c.AssetId)) + (c.Repeat > 1 ? $" ×{c.Repeat}" : ""), FontSize = 10, Foreground = Brushes.White, Margin = new Thickness(4, 1, 0, 0), IsHitTestVisible = false });
        Put(g, X(c.StartMs), trackY + 12);
    }

    /// <summary>Peak outline, one point per two pixels, repeated for loops.</summary>
    Polyline Waveform(float[] s, MixClip c, double w, double h)
    {
        var line = new Polyline { Stroke = new SolidColorBrush(Color.FromArgb(0xC0, 0xD0, 0xE8, 0xFF)), StrokeThickness = 1, IsHitTestVisible = false };
        if (s.Length == 0) return line;
        double pieceW = w / Math.Max(1, c.Repeat);
        for (double x = 0; x < w; x += 2)
        {
            double inPiece = x % pieceW / pieceW;
            int a = (int)(inPiece * s.Length), b = Math.Min(s.Length, a + Math.Max(1, (int)(s.Length * 2 / pieceW)));
            float peak = 0;
            for (int i = a; i < b; i++) peak = Math.Max(peak, Math.Abs(s[i]));
            line.Points.Add(new Point(x, h / 2 - peak * c.Gain * h / 2));
        }
        return line;
    }

    void MovePlayhead(int ms)
    {
        if (_playhead is null) return;
        _playhead.X1 = _playhead.X2 = X(ms);
    }

    // ---------- mouse ----------

    (MixTrack? Track, MixClip? Clip) HitTest(Point p)
    {
        int ti = (int)Math.Floor((p.Y - RulerHeight) / TrackHeight);
        if (p.Y < RulerHeight || ti < 0 || ti >= _mix.Tracks.Count) return (null, null);
        var t = _mix.Tracks[ti];
        var c = t.Clips.LastOrDefault(c => p.X >= X(c.StartMs) && p.X < X(c.StartMs) + Math.Max(4, X(Lib?.ClipMs(c) ?? 1000)));
        return (t, c);
    }

    void OnTimelineDown(object sender, MouseButtonEventArgs e)
    {
        Timeline.Focus();
        var p = e.GetPosition(Timeline);
        var (t, c) = HitTest(p);
        if (t is not null) _track = t;
        _clip = c;
        if (c is null)
        {
            _playheadMs = Ms(p.X);
            if (_timer.IsEnabled) StartPlay();   // jump while playing
        }
        else
        {
            _dragFrom = p;
            _dragStartMs = c.StartMs;
            _dragSnapshotDone = false;
            Timeline.CaptureMouse();
        }
        RefreshProps();
        Rebuild();
        Status.Text = StatusLine();
    }

    void OnTimelineMove(object sender, MouseEventArgs e)
    {
        if (_dragFrom is not Point from || _clip is null) return;
        var p = e.GetPosition(Timeline);
        if (!_dragSnapshotDone)
        {
            if (Math.Abs(p.X - from.X) < 3) return;   // a click is not a move
            Snapshot();
            _dragSnapshotDone = true;
        }
        int ms = Math.Max(0, _dragStartMs + Ms(p.X) - Ms(from.X));
        _clip.StartMs = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? ms : SnapToEdges(ms);
        Rebuild();
    }

    /// <summary>Snaps a start time to other clip ends and starts on the same track within 6 px, else to 10 ms.</summary>
    int SnapToEdges(int ms)
    {
        if (_track is null) return ms;
        int range = Ms(6);
        foreach (var o in _track.Clips.Where(o => o != _clip))
        {
            int end = o.StartMs + (Lib?.ClipMs(o) ?? 0);
            if (Math.Abs(ms - end) <= range) return end;
            if (Math.Abs(ms - o.StartMs) <= range) return o.StartMs;
        }
        return ms / 10 * 10;
    }

    void OnTimelineUp(object sender, MouseButtonEventArgs e)
    {
        _dragFrom = null;
        Timeline.ReleaseMouseCapture();
        if (_dragSnapshotDone) RefreshProps();
    }

    // ---------- playback ----------

    void OnPlay(object? sender, RoutedEventArgs? e)
    {
        if (_timer.IsEnabled) StopPlay();
        else StartPlay();
    }

    void StartPlay()
    {
        if (Lib is not { } lib || !TryRender(() => lib.Render(_mix), out var s)) { StopPlay(); return; }
        int from = AudioEdit.Samples(_playheadMs);
        bool loop = LoopToggle.IsChecked == true && _mix.LoopStartMs is int ls && _mix.LoopEndMs is int le && _playheadMs < le;
        if (from >= s.Length && !loop) { Status.Text = "Nothing to play after the playhead."; StopPlay(); return; }
        _playOffset = 0;
        AudioOut.Shared.PlayClip(s, from, loop ? AudioEdit.Samples(_mix.LoopStartMs!.Value) : -1, loop ? AudioEdit.Samples(_mix.LoopEndMs!.Value) : -1);
        PlayBtn.IsChecked = true;
        PlayBtn.Content = "■ Stop";
        _timer.Start();
    }

    void StopPlay()
    {
        if (_timer.IsEnabled) AudioOut.Shared.StopClip();
        _timer.Stop();
        PlayBtn.IsChecked = false;
        PlayBtn.Content = "▶ Play";
    }

    /// <summary>The playhead follows what the sound card is actually playing.</summary>
    void FollowPlayback()
    {
        if (AudioOut.Shared.ClipPosition is not int pos) { StopPlay(); return; }
        int ms = AudioEdit.Ms(pos + _playOffset);
        MovePlayhead(ms);
        double x = X(ms);
        if (x > Scroller.HorizontalOffset + Scroller.ViewportWidth - 40 || x < Scroller.HorizontalOffset) Scroller.ScrollToHorizontalOffset(Math.Max(0, x - 40));
    }

    // ---------- export ----------

    void OnExportWav(object? sender, RoutedEventArgs? e)
    {
        if (Lib is not { } lib) { Ui.Error(new InvalidOperationException("Open a project first.")); return; }
        if (!TryRender(() => lib.Render(_mix), out var s)) return;
        if (s.Length == 0) { Ui.Error(new InvalidOperationException("The mix is empty.")); return; }
        var dlg = new SaveFileDialog { Filter = "WAV (*.wav)|*.wav", FileName = System.IO.Path.ChangeExtension(System.IO.Path.GetFileName(_path) ?? "mix", ".wav"), InitialDirectory = App.Project?.Root ?? "" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            WavWriter.Save(dlg.FileName, s);
            float peak = AudioEdit.Peak(s);
            Status.Text = $"Exported {s.Length / (double)Synth.SampleRate:0.00} s to {System.IO.Path.GetFileName(dlg.FileName)}"
                          + (peak > 1 ? $". Warning: peak {peak:0.00} was clipped; lower the master volume." : ".");
            App.AddRecent(dlg.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); }
    }

    // ---------- zoom ----------

    void ZoomStep(int dir)
    {
        int i = Array.IndexOf(Zooms, _pxPerSec) + dir;
        if (i < 0 || i >= Zooms.Length) return;
        _pxPerSec = Zooms[i];
        Rebuild();
    }

    void OnZoomIn(object sender, RoutedEventArgs e) => ZoomStep(+1);
    void OnZoomOut(object sender, RoutedEventArgs e) => ZoomStep(-1);

    // ---------- properties ----------

    static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    void RefreshProps()
    {
        _refreshing = true;
        MasterBox.Value = _mix.MasterVolume;
        LoopStartBox.Text = _mix.LoopStartMs?.ToString() ?? "";
        LoopEndBox.Text = _mix.LoopEndMs?.ToString() ?? "";
        TrackNameBox.Text = _track?.Name ?? "";
        TrackVolumeBox.Value = _track?.Volume ?? 1;
        MuteBox.IsChecked = _track?.Mute;
        SoloBox.IsChecked = _track?.Solo;
        ClipProps.Visibility = _clip is null ? Visibility.Collapsed : Visibility.Visible;
        if (_clip is { } c)
        {
            ClipTitle.Text = $"Clip: {AssetName(c.AssetId)}";
            ClipStartBox.Text = c.StartMs.ToString();
            ClipOffsetBox.Text = c.OffsetMs.ToString();
            ClipLengthBox.Text = c.LengthMs?.ToString() ?? "";
            ClipGainBox.Text = F(c.Gain);
            ClipFadeInBox.Text = c.FadeInMs.ToString();
            ClipFadeOutBox.Text = c.FadeOutMs.ToString();
            ClipRepeatBox.Text = c.Repeat.ToString();
        }
        _refreshing = false;
    }

    void OnMasterChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_refreshing || Math.Abs(e.NewValue - _mix.MasterVolume) < 1e-9) return;
        Snapshot();   // ponytail: every slider tick is an undo step; merge steps if that proves annoying
        _mix.MasterVolume = Math.Round(e.NewValue, 2);
    }

    static bool TryMs(string text, out int? ms)
    {
        ms = null;
        if (text.Trim().Length == 0) return true;
        if (!int.TryParse(text.Trim(), out int v) || v < 0) return false;
        ms = v;
        return true;
    }

    void OnLoopCommit(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        bool ok = TryMs(LoopStartBox.Text, out var a) & TryMs(LoopEndBox.Text, out var b);
        ok = ok && (a is null) == (b is null) && (a is null || b > a);
        Ui.MarkInvalid(LoopStartBox, ok ? null : "Both empty, or a start in ms and a later end.");
        if (!ok || (a, b) == (_mix.LoopStartMs, _mix.LoopEndMs)) return;
        Snapshot();
        (_mix.LoopStartMs, _mix.LoopEndMs) = (a, b);
        Rebuild();
    }

    void OnTrackCommit(object sender, RoutedEventArgs e)
    {
        if (_refreshing || _track is not { } t) return;
        string name = TrackNameBox.Text.Trim().Length > 0 ? TrackNameBox.Text.Trim() : t.Name;
        if ((name, MuteBox.IsChecked == true, SoloBox.IsChecked == true) == (t.Name, t.Mute, t.Solo)) return;
        Snapshot();
        (t.Name, t.Mute, t.Solo) = (name, MuteBox.IsChecked == true, SoloBox.IsChecked == true);
        Rebuild();
    }

    void OnTrackVolume(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_refreshing || _track is not { } t || Math.Abs(e.NewValue - t.Volume) < 1e-9) return;
        Snapshot();
        t.Volume = Math.Round(e.NewValue, 2);
    }

    MixTrack AddTrackNoSnapshot()
    {
        var t = new MixTrack { Name = $"Track {_mix.Tracks.Count + 1}" };
        _mix.Tracks.Add(t);
        return t;
    }

    void OnAddTrack(object sender, RoutedEventArgs e)
    {
        Snapshot();
        _track = AddTrackNoSnapshot();
        _clip = null;
        RefreshProps();
        Rebuild();
    }

    void OnDeleteTrack(object sender, RoutedEventArgs e)
    {
        if (_track is not { } t) return;
        if (t.Clips.Count > 0 && MessageBox.Show(Window.GetWindow(this), $"Delete track “{t.Name}” and its {t.Clips.Count} clip(s)?", "Delete track",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        Snapshot();
        _mix.Tracks.Remove(t);
        _track = _mix.Tracks.FirstOrDefault();
        _clip = null;
        RefreshProps();
        Rebuild();
    }

    void OnClipCommit(object sender, RoutedEventArgs e)
    {
        if (_refreshing || _clip is not { } c) return;
        bool ok = int.TryParse(ClipStartBox.Text, out int start) & int.TryParse(ClipOffsetBox.Text, out int offset)
                  & TryMs(ClipLengthBox.Text, out var length)
                  & double.TryParse(ClipGainBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double gain)
                  & int.TryParse(ClipFadeInBox.Text, out int fin) & int.TryParse(ClipFadeOutBox.Text, out int fout) & int.TryParse(ClipRepeatBox.Text, out int rep);
        ok = ok && start >= 0 && offset >= 0 && length is null or > 0 && gain is >= 0 and <= 8 && fin >= 0 && fout >= 0 && rep is >= 1 and <= 1000;
        Ui.MarkInvalid(ClipStartBox, ok ? null : "Times are whole ms ≥ 0 (keep > 0 or empty), gain 0–8, repeat 1–1000.");
        if (!ok) return;
        if ((start, offset, length, gain, fin, fout, rep) == (c.StartMs, c.OffsetMs, c.LengthMs, c.Gain, c.FadeInMs, c.FadeOutMs, c.Repeat)) return;
        Snapshot();
        (c.StartMs, c.OffsetMs, c.LengthMs, c.Gain, c.FadeInMs, c.FadeOutMs, c.Repeat) = (start, offset, length, gain, fin, fout, rep);
        Rebuild();
    }

    /// <summary>Join: the clip starts where the previous clip on its track ends, minus the crossfade (with matching fades).</summary>
    void OnJoinPrevious(object sender, RoutedEventArgs e)
    {
        if (_clip is not { } c || _track is not { } t) return;
        if (!int.TryParse(CrossfadeBox.Text, out int x) || x < 0) { Ui.MarkInvalid(CrossfadeBox, "Whole ms ≥ 0."); return; }
        Ui.MarkInvalid(CrossfadeBox, null);
        var prev = t.Clips.Where(o => o != c && o.StartMs <= c.StartMs).OrderBy(o => o.StartMs).LastOrDefault();
        if (prev is null || Lib?.ClipMs(prev) is not int prevMs) { Status.Text = "No earlier clip on this track to join to."; return; }
        Snapshot();
        c.StartMs = Math.Max(0, prev.StartMs + prevMs - x);
        if (x > 0) (prev.FadeOutMs, c.FadeInMs) = (x, x);
        RefreshProps();
        Rebuild();
    }

    /// <summary>Cut in two at the playhead: the second half becomes its own clip (non-destructive, the source is untouched).</summary>
    void OnSplit(object? sender, RoutedEventArgs? e)
    {
        if (_clip is not { } c || _track is not { } t || Lib?.ClipMs(c) is not int len) return;
        int at = _playheadMs - c.StartMs;
        if (c.Repeat != 1 || at <= 0 || at >= len) { Status.Text = "Put the playhead inside a clip that is not repeated, then split."; return; }
        Snapshot();
        var second = c.Clone();
        second.Id = Guid.NewGuid();
        (second.StartMs, second.OffsetMs, second.LengthMs, second.FadeInMs) = (_playheadMs, c.OffsetMs + at, len - at, 0);
        (c.LengthMs, c.FadeOutMs) = (at, 0);
        t.Clips.Add(second);
        _clip = second;
        RefreshProps();
        Rebuild();
    }

    void OnDeleteClip(object? sender, RoutedEventArgs? e)
    {
        if (_clip is null) return;
        Snapshot();
        foreach (var t in _mix.Tracks) t.Clips.Remove(_clip);
        _clip = null;
        RefreshProps();
        Rebuild();
    }

    void NudgeClip(int ms)
    {
        if (_clip is null) return;
        Snapshot();
        _clip.StartMs = Math.Max(0, _clip.StartMs + ms);
        RefreshProps();
        Rebuild();
        Status.Text = StatusLine();
    }

    void MoveClipToTrack(int dir)
    {
        if (_clip is not { } c || _track is not { } t) return;
        int i = _mix.Tracks.IndexOf(t) + dir;
        if (i < 0 || i >= _mix.Tracks.Count) return;
        Snapshot();
        t.Clips.Remove(c);
        _track = _mix.Tracks[i];
        _track.Clips.Add(c);
        RefreshProps();
        Rebuild();
    }

    void SelectNeighbour(int dir)
    {
        var all = _mix.Tracks.SelectMany(t => t.Clips.Select(c => (t, c))).OrderBy(x => x.c.StartMs).ToList();
        if (all.Count == 0) return;
        int i = _clip is null ? (dir > 0 ? 0 : all.Count - 1) : Math.Clamp(all.FindIndex(x => x.c == _clip) + dir, 0, all.Count - 1);
        (_track, _clip) = all[i];
        RefreshProps();
        Rebuild();
        Status.Text = StatusLine();
        Scroller.ScrollToHorizontalOffset(Math.Max(0, X(_clip.StartMs) - 40));
    }

    // ---------- playlists (stored in the project) ----------

    Playlist? CurrentPlaylist => App.Project?.Settings.Playlists.ElementAtOrDefault(PlaylistBox.SelectedIndex);

    void RefreshPlaylists(int? select = null)
    {
        _refreshing = true;
        var lists = App.Project?.Settings.Playlists ?? [];
        int keep = select ?? PlaylistBox.SelectedIndex;
        PlaylistBox.ItemsSource = lists.Select(l => l.Name).ToList();
        PlaylistBox.SelectedIndex = lists.Count == 0 ? -1 : Math.Clamp(keep, 0, lists.Count - 1);
        _refreshing = false;
        RefreshPlaylistItems();
    }

    void RefreshPlaylistItems(int select = -1)
    {
        var pl = CurrentPlaylist;
        PlaylistItems.ItemsSource = pl?.Items.Select((id, i) => $"{i + 1}. {AssetName(id)}").ToList();
        PlaylistItems.SelectedIndex = select;
        _refreshing = true;
        PlaylistLoop.IsChecked = pl?.Loop;
        PlaylistShuffle.IsChecked = pl?.Shuffle;
        PlaylistLoop.IsEnabled = PlaylistShuffle.IsEnabled = pl is not null;
        _refreshing = false;
    }

    /// <summary>Playlists live in the project file: every change is saved at once (no undo, like asset renames).</summary>
    void SaveProject(int? select = null, int item = -1)
    {
        try { App.Project?.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); }
        RefreshPlaylists(select);
        RefreshPlaylistItems(item);
    }

    void OnPlaylistChosen(object sender, SelectionChangedEventArgs e) { if (!_refreshing) RefreshPlaylistItems(); }

    void OnPlaylistNew(object sender, RoutedEventArgs e)
    {
        if (App.Project is not { } p) { Ui.Error(new InvalidOperationException("Open a project first.")); return; }
        if (Ui.Ask(this, "New playlist", "Name", $"Playlist {p.Settings.Playlists.Count + 1}") is not { Length: > 0 } name) return;
        p.Settings.Playlists.Add(new Playlist { Name = name.Trim() });
        SaveProject(p.Settings.Playlists.Count - 1);
    }

    void OnPlaylistRename(object sender, RoutedEventArgs e)
    {
        if (CurrentPlaylist is not { } pl || Ui.Ask(this, "Rename playlist", "Name", pl.Name) is not { Length: > 0 } name) return;
        pl.Name = name.Trim();
        SaveProject();
    }

    void OnPlaylistDelete(object sender, RoutedEventArgs e)
    {
        if (CurrentPlaylist is not { } pl || App.Project is not { } p) return;
        if (MessageBox.Show(Window.GetWindow(this), $"Delete playlist “{pl.Name}”? The songs stay in the project.", "Delete playlist", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        p.Settings.Playlists.Remove(pl);
        SaveProject(0);
    }

    void OnPlaylistAdd(object sender, RoutedEventArgs e)
    {
        if (CurrentPlaylist is not { } pl) { Status.Text = "Create a playlist first."; return; }
        if (LibrarySelection is not Guid id) { Status.Text = "Choose a library item first."; return; }
        pl.Items.Add(id);
        SaveProject(item: pl.Items.Count - 1);
    }

    void OnPlaylistRemove(object sender, RoutedEventArgs e)
    {
        if (CurrentPlaylist is not { } pl || PlaylistItems.SelectedIndex < 0) return;
        int i = PlaylistItems.SelectedIndex;
        pl.Items.RemoveAt(i);
        SaveProject(item: Math.Min(i, pl.Items.Count - 1));
    }

    void MovePlaylistItem(int dir)
    {
        if (CurrentPlaylist is not { } pl || PlaylistItems.SelectedIndex is var i && (i < 0 || i + dir < 0 || i + dir >= pl.Items.Count)) return;
        (pl.Items[i], pl.Items[i + dir]) = (pl.Items[i + dir], pl.Items[i]);
        SaveProject(item: i + dir);
    }

    void OnPlaylistUp(object sender, RoutedEventArgs e) => MovePlaylistItem(-1);
    void OnPlaylistDown(object sender, RoutedEventArgs e) => MovePlaylistItem(+1);

    void OnPlaylistFlags(object sender, RoutedEventArgs e)
    {
        if (_refreshing || CurrentPlaylist is not { } pl) return;
        (pl.Loop, pl.Shuffle) = (PlaylistLoop.IsChecked == true, PlaylistShuffle.IsChecked == true);
        SaveProject(item: PlaylistItems.SelectedIndex);
    }

    void OnPlaylistPlay(object sender, RoutedEventArgs e)
    {
        if (CurrentPlaylist is not { Items.Count: > 0 } pl || Lib is not { } lib) { Status.Text = "The playlist is empty."; return; }
        StopPlay();
        if (TryRender(() => lib.Render(pl), out var s)) AudioOut.Shared.PlayOnce(s);
    }

    // ---------- undo ----------

    void OnUndo(object? sender, RoutedEventArgs? e) => Restore(_history.Undo(_mix));
    void OnRedo(object? sender, RoutedEventArgs? e) => Restore(_history.Redo(_mix));

    void Restore(Mix? m)
    {
        if (m is null || _dragFrom is not null) return;
        int ti = _track is null ? 0 : _mix.Tracks.IndexOf(_track);
        _mix = m;
        _track = _mix.Tracks.ElementAtOrDefault(ti) ?? _mix.Tracks.FirstOrDefault();
        _clip = null;
        _dirty = true;
        RefreshProps();
        Rebuild();
        UpdateTitle();
    }

    // ---------- files ----------

    const string Filter = "PixelForge mix (*.pxmix)|*.pxmix";

    void OnNew(object? sender, RoutedEventArgs? e)
    {
        if (ConfirmDiscard()) LoadMix(new Mix(), null);
    }

    void OnOpen(object? sender, RoutedEventArgs? e)
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog { Filter = Filter };
        if (dlg.ShowDialog() == true) OpenPath(dlg.FileName, confirmed: true);
    }

    void OnSave(object? sender, RoutedEventArgs? e) => Save(_path);

    bool Save(string? path)
    {
        if (path is null)
        {
            var dlg = new SaveFileDialog { Filter = Filter, FileName = "music.pxmix", InitialDirectory = App.Project?.Root ?? "" };
            if (dlg.ShowDialog() != true) return false;
            path = dlg.FileName;
        }
        try
        {
            Mix.Save(_mix, path);
            (_path, _dirty) = (path, false);
            UpdateTitle();
            App.AddRecent(path);
            _lib = null;   // other mixes may contain this one
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); return false; }
    }

    // ---------- IEditor ----------

    public string[] FileExtensions => [".pxmix"];
    public bool IsDirty => _dirty;
    public string? CurrentPath => _path;

    public bool ConfirmDiscard()
    {
        if (!_dirty) return true;
        var r = MessageBox.Show("Save changes to the current mix?", "PixelForge", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return r == MessageBoxResult.No || (r == MessageBoxResult.Yes && Save(_path));
    }

    public void OpenPath(string path, bool confirmed = false)
    {
        if (!confirmed && !ConfirmDiscard()) return;
        try { LoadMix(Mix.Load(path), path); App.AddRecent(path); }
        catch (Exception ex) when (ex is InvalidDataException or IOException) { Ui.Error(ex); }
    }

    public void OnFileMoved(string oldPath, string newPath)
    {
        if (string.Equals(_path, oldPath, StringComparison.OrdinalIgnoreCase)) { _path = newPath; UpdateTitle(); }
        _lib = null;
    }

    public void WriteAutosave(string file) => Mix.Save(_mix, file);

    public void RestoreAutosave(string file, string? originalPath)
    {
        LoadMix(Mix.Load(file), originalPath);
        _dirty = true;
        UpdateTitle();
    }

    IReadOnlyList<MenuItem>? _menus;
    Action Do(RoutedEventHandler h) => () => h(this, new RoutedEventArgs());

    public IReadOnlyList<MenuItem> Menus => _menus ??=
    [
        Ui.Menu("_File",
            Ui.Item("_New mix", "Ctrl+N", Do(OnNew)),
            Ui.Item("_Open…", "Ctrl+O", Do(OnOpen)),
            Ui.RecentMenu(this),
            Ui.Item("_Save", "Ctrl+S", Do(OnSave)),
            Ui.Item("Save _as…", "Ctrl+Shift+S", () => Save(null)),
            Ui.Item("_Export WAV…", "Ctrl+E", () => OnExportWav(null, null)),
            new Separator(),
            Ui.Item("E_xit", "Alt+F4", () => Window.GetWindow(this)?.Close())),
        Ui.Menu("_Edit",
            Ui.Item("_Undo", "Ctrl+Z", Do(OnUndo)),
            Ui.Item("_Redo", "Ctrl+Y", Do(OnRedo)),
            new Separator(),
            Ui.Item("_Split clip at playhead", "S", () => OnSplit(null, null)),
            Ui.Item("_Delete clip", "Del", () => OnDeleteClip(null, null))),
        Ui.Menu("_Play",
            Ui.Item("_Play / stop", "Space", () => OnPlay(null, null)),
            Ui.Toggle("_Loop region", "L", LoopToggle),
            Ui.Item("Playhead to _start", "Home", () => { _playheadMs = 0; MovePlayhead(0); Status.Text = StatusLine(); })),
    ];

    public void HandleKey(KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox or ListBoxItem or ListBox or ComboBox or Slider) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Z when ctrl: OnUndo(null, null); break;
            case Key.Y when ctrl: OnRedo(null, null); break;
            case Key.S when ctrl && shift: Save(null); break;
            case Key.S when ctrl: OnSave(null, null); break;
            case Key.N when ctrl: OnNew(null, null); break;
            case Key.O when ctrl: OnOpen(null, null); break;
            case Key.E when ctrl: OnExportWav(null, null); break;
            case Key.OemPlus or Key.Add when ctrl: ZoomStep(+1); break;
            case Key.OemMinus or Key.Subtract when ctrl: ZoomStep(-1); break;
            case Key.Space: OnPlay(null, null); break;
            case Key.L: LoopToggle.IsChecked = LoopToggle.IsChecked != true; break;
            case Key.S: OnSplit(null, null); break;
            case Key.Home: _playheadMs = 0; MovePlayhead(0); Status.Text = StatusLine(); break;
            case Key.Delete: OnDeleteClip(null, null); break;
            case Key.Left: NudgeClip(shift ? -100 : -10); break;
            case Key.Right: NudgeClip(shift ? 100 : 10); break;
            case Key.Up: MoveClipToTrack(-1); break;
            case Key.Down: MoveClipToTrack(+1); break;
            case Key.OemOpenBrackets: SelectNeighbour(-1); break;
            case Key.OemCloseBrackets: SelectNeighbour(+1); break;
            default: e.Handled = false; break;
        }
    }
}
