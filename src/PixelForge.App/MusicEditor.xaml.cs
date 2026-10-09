using System.Diagnostics;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.App;

// ponytail: SoundPlayer plays a pre-rendered WAV; edits apply on next Play and the playhead is
// timer-estimated. Switch to a streaming audio lib (NAudio) for live edits / exact sync.
public partial class MusicEditor : UserControl
{
    Song _song = new();
    readonly History<Song> _history = new(s => s.Clone());
    int _pattern, _channel;
    string? _path;
    bool _dirty;

    // drag painting: note to set (or Off when erasing)
    bool _painting, _erasing;

    SoundPlayer? _player, _preview;
    IReadOnlyList<int> _playOrder = [];
    readonly Stopwatch _clock = new();
    readonly DispatcherTimer _playheadTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };

    public MusicEditor()
    {
        InitializeComponent();
        _playheadTimer.Tick += (_, _) => UpdatePlayhead();
        for (int i = 0; i < Song.Channels; i++) ((RadioButton)FindName($"Ch{i}")).BorderBrush = StepGrid.ChannelBrushes[i];
        Loaded += Init;
        IsVisibleChanged += (_, _) => UpdateTitle();

        void Init(object s, RoutedEventArgs e)
        {
            Loaded -= Init;
            LoadSong(_song, null);
            Scroller.ScrollToVerticalOffset(StepsGrid.YOf(72) - 20);   // start around C5..C3
        }
    }

    // ---------- document ----------

    void LoadSong(Song s, string? path)
    {
        Stop();
        _song = s;
        _path = path;
        _dirty = false;
        _history.Clear();
        _pattern = 0;
        RefreshAll();
    }

    void Snapshot()
    {
        _history.Push(_song);
        _dirty = true;
        UpdateTitle();
    }

    void RefreshAll()
    {
        _pattern = Math.Clamp(_pattern, 0, _song.Patterns.Count - 1);
        BpmBox.Text = _song.Bpm.ToString();
        RefreshPatterns();
        RedrawGrid();
        UpdateTitle();
    }

    void RedrawGrid()
    {
        StepsGrid.Song = _song;
        StepsGrid.Pattern = _pattern;
        StepsGrid.Channel = _channel;
        StepsGrid.InvalidateMeasure();
        StepsGrid.InvalidateVisual();
        Status.Text = $"Pattern {_pattern + 1}/{_song.Patterns.Count}   {_song.Steps} steps";
    }

    void UpdateTitle()
    {
        if (IsVisible && Window.GetWindow(this) is { } win)
            win.Title = $"PixelForge — {(_path is null ? "untitled song" : System.IO.Path.GetFileName(_path))}{(_dirty ? " *" : "")}";
    }

    public bool ConfirmDiscard()
    {
        if (!_dirty) return true;
        var r = MessageBox.Show("Save changes to the current song?", "PixelForge", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return r == MessageBoxResult.No || (r == MessageBoxResult.Yes && Save(_path));
    }

    // ---------- note editing ----------

    void OnGridDown(object sender, MouseButtonEventArgs e)
    {
        Keyboard.Focus(this);
        if (StepsGrid.Hit(e.GetPosition(StepsGrid)) is not var (step, note)) return;
        var notes = _song.Patterns[_pattern].Notes[_channel];
        _erasing = e.ChangedButton == MouseButton.Right || notes[step] == note;
        Snapshot();
        notes[step] = _erasing ? Pattern.Off : note;
        _painting = true;
        StepsGrid.CaptureMouse();
        StepsGrid.InvalidateVisual();
        if (!_erasing && _player is null) Preview(note);
    }

    void OnGridMove(object sender, MouseEventArgs e)
    {
        if (!_painting || StepsGrid.Hit(e.GetPosition(StepsGrid)) is not var (step, note)) return;
        var notes = _song.Patterns[_pattern].Notes[_channel];
        int value = _erasing ? Pattern.Off : note;
        if (notes[step] == value) return;
        notes[step] = value;
        StepsGrid.InvalidateVisual();
    }

    void OnGridUp(object sender, MouseButtonEventArgs e)
    {
        _painting = false;
        StepsGrid.ReleaseMouseCapture();
    }

    void SelectChannel(int ch)
    {
        _channel = ch;
        ((RadioButton)FindName($"Ch{ch}")).IsChecked = true;
        RedrawGrid();
    }

    void OnChannel(object sender, RoutedEventArgs e) => SelectChannel(((FrameworkElement)sender).Name[2] - '0');

    // ---------- patterns / order ----------

    bool _refreshingPatterns;

    void RefreshPatterns()
    {
        _refreshingPatterns = true;
        PatternsList.Items.Clear();
        for (int i = 0; i < _song.Patterns.Count; i++)
            PatternsList.Items.Add(new TextBlock { Text = $" {i + 1} ", FontWeight = FontWeights.SemiBold });
        PatternsList.SelectedIndex = _pattern;
        OrderBox.Text = string.Join(" ", _song.Order.Select(i => i + 1));
        _refreshingPatterns = false;
    }

    void OnPatternSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingPatterns || PatternsList.SelectedIndex < 0) return;
        _pattern = PatternsList.SelectedIndex;
        RedrawGrid();
    }

    void EditPatterns(Action change)
    {
        Snapshot();
        change();
        _pattern = Math.Clamp(_pattern, 0, _song.Patterns.Count - 1);
        RefreshPatterns();
        RedrawGrid();
    }

    void OnAddPattern(object sender, RoutedEventArgs e) =>
        EditPatterns(() => { _song.Patterns.Add(Pattern.Empty(_song.Steps)); _pattern = _song.Patterns.Count - 1; });

    void OnDuplicatePattern(object sender, RoutedEventArgs e) =>
        EditPatterns(() => { _song.Patterns.Add(_song.Patterns[_pattern].Clone()); _pattern = _song.Patterns.Count - 1; });

    void OnDeletePattern(object sender, RoutedEventArgs e)
    {
        if (_song.Patterns.Count > 1) EditPatterns(() => _song.RemovePattern(_pattern));
    }

    void OnOrderKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnOrderCommit(sender, e);
    }

    void OnOrderCommit(object sender, RoutedEventArgs e)
    {
        var parts = OrderBox.Text.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries);
        var order = parts.Select(p => int.TryParse(p, out int n) ? n - 1 : -1).ToList();
        if (order.Count == 0 || order.Any(i => i < 0 || i >= _song.Patterns.Count))
        {
            OrderBox.Text = string.Join(" ", _song.Order.Select(i => i + 1));   // invalid: revert
            return;
        }
        if (order.SequenceEqual(_song.Order)) return;
        Snapshot();
        _song.Order.Clear();
        _song.Order.AddRange(order);
    }

    void OnBpmKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnBpmCommit(sender, e);
    }

    void OnBpmCommit(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(BpmBox.Text, out int bpm) || bpm < 20 || bpm > 400) { BpmBox.Text = _song.Bpm.ToString(); return; }
        if (bpm == _song.Bpm) return;
        Snapshot();
        _song.Bpm = bpm;
    }

    // ---------- playback ----------

    void OnPlaySong(object? sender, RoutedEventArgs? e) => Play(_song.Order);
    void OnPlayPattern(object sender, RoutedEventArgs e) => Play([_pattern]);
    void OnStop(object? sender, RoutedEventArgs? e) => Stop();

    void Play(IReadOnlyList<int> order)
    {
        Stop();
        _playOrder = [.. order];
        _player = new SoundPlayer(new MemoryStream(WavWriter.ToBytes(Synth.Render(_song, _playOrder))));
        _player.Load();
        _player.PlayLooping();
        _clock.Restart();
        _playheadTimer.Start();
    }

    void Stop()
    {
        _playheadTimer.Stop();
        _player?.Stop();
        _player?.Dispose();
        _player = null;
        StepsGrid.Playhead = -1;
        StepsGrid.InvalidateVisual();
    }

    void UpdatePlayhead()
    {
        long stepIndex = (long)(_clock.Elapsed.TotalSeconds / _song.StepSeconds) % (_playOrder.Count * _song.Steps);
        int playing = _playOrder[(int)(stepIndex / _song.Steps)];
        int head = playing == _pattern ? (int)(stepIndex % _song.Steps) : -1;
        if (head == StepsGrid.Playhead) return;
        StepsGrid.Playhead = head;
        StepsGrid.InvalidateVisual();
    }

    void Preview(int note)
    {
        var s = new Song(steps: 2) { Bpm = _song.Bpm };
        s.Patterns[0].Notes[_channel][0] = note;
        _preview?.Dispose();
        _preview = new SoundPlayer(new MemoryStream(WavWriter.ToBytes(Synth.Render(s))));
        _preview.Play();
    }

    // ---------- undo ----------

    void OnUndo(object? sender, RoutedEventArgs? e) => Restore(_history.Undo(_song));
    void OnRedo(object? sender, RoutedEventArgs? e) => Restore(_history.Redo(_song));

    void Restore(Song? s)
    {
        if (s is null || _painting) return;
        _song = s;
        _dirty = true;
        RefreshAll();
    }

    // ---------- files ----------

    const string SongFilter = "PixelForge song (*.pxs)|*.pxs";

    void OnNew(object? sender, RoutedEventArgs? e)
    {
        if (!ConfirmDiscard()) return;
        if (Ui.Ask(this, "New song", "Steps per pattern (16 = one bar of 16th notes):", _song.Steps.ToString()) is not string text) return;
        try { LoadSong(new Song(int.Parse(text)) { Bpm = _song.Bpm }, null); }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentOutOfRangeException) { Ui.Error(ex); }
    }

    void OnOpen(object? sender, RoutedEventArgs? e)
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog { Filter = SongFilter };
        if (dlg.ShowDialog() != true) return;
        try { LoadSong(Song.Load(dlg.FileName), dlg.FileName); }
        catch (Exception ex) { Ui.Error(ex); }
    }

    void OnSave(object? sender, RoutedEventArgs? e) => Save(_path);
    void OnSaveAs(object sender, RoutedEventArgs e) => Save(null);

    bool Save(string? path)
    {
        if (path is null)
        {
            var dlg = new SaveFileDialog { Filter = SongFilter, FileName = "song.pxs" };
            if (dlg.ShowDialog() != true) return false;
            path = dlg.FileName;
        }
        try
        {
            Song.Save(_song, path);
            (_path, _dirty) = (path, false);
            UpdateTitle();
            return true;
        }
        catch (Exception ex) { Ui.Error(ex); return false; }
    }

    void OnExportWav(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "WAV audio (*.wav)|*.wav", FileName = (System.IO.Path.GetFileNameWithoutExtension(_path) ?? "song") + ".wav" };
        if (dlg.ShowDialog() != true) return;
        try { File.WriteAllBytes(dlg.FileName, WavWriter.ToBytes(Synth.Render(_song))); }
        catch (Exception ex) { Ui.Error(ex); }
    }

    // ---------- keyboard ----------

    public void HandleKey(KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Z when ctrl: OnUndo(null, null); break;
            case Key.Y when ctrl: OnRedo(null, null); break;
            case Key.S when ctrl: OnSave(null, null); break;
            case Key.N when ctrl: OnNew(null, null); break;
            case Key.O when ctrl: OnOpen(null, null); break;
            case Key.Space: if (_player is null) OnPlaySong(null, null); else Stop(); break;
            case >= Key.D1 and <= Key.D4 when !ctrl: SelectChannel(e.Key - Key.D1); break;
            default: e.Handled = false; break;
        }
    }

}
