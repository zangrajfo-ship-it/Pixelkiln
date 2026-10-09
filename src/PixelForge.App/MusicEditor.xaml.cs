using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.App;

public partial class MusicEditor : UserControl
{
    Song _song = new();
    readonly History<Song> _history = new(s => s.Clone(), App.Settings.UndoLimit);
    int _pattern, _channel;
    string? _path;
    bool _dirty;

    // mouse drag in the piano-roll
    enum Drag { None, Extend, Volume }
    Drag _drag;
    int _dragStart, _dragNote;

    SongStream? _stream;
    readonly DispatcherTimer _playheadTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };

    public MusicEditor()
    {
        InitializeComponent();
        _playheadTimer.Tick += (_, _) => UpdatePlayhead();
        for (int i = 0; i < Song.Channels; i++) ((RadioButton)FindName($"Ch{i}")).BorderBrush = StepGrid.ChannelBrushes[i];
        BuildInstrumentPanel();
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
        Ui.MarkInvalid(BpmBox, null);
        Ui.MarkInvalid(OrderBox, null);
        RefreshPatterns();
        RefreshInstrument();
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
        AnnounceCursor();
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

    Pattern Pat => _song.Patterns[_pattern];

    void OnGridDown(object sender, MouseButtonEventArgs e)
    {
        StepsGrid.Focus();
        var p = e.GetPosition(StepsGrid);
        if (StepsGrid.InVolumeLane(p))
        {
            Snapshot();
            _drag = Drag.Volume;
            SetVolume(p);
            StepsGrid.CaptureMouse();
            return;
        }
        if (StepsGrid.Hit(p) is not var (step, note)) return;
        StepsGrid.KeyCursor = (step, note);
        int start = Pat.NoteStart(_channel, step);

        // right-click, or click on the note itself: remove it (with its length)
        if (e.ChangedButton == MouseButton.Right || (start >= 0 && Pat.Notes[_channel][start] == note))
        {
            if (start < 0) return;
            Snapshot();
            Pat.RemoveNote(_channel, start);
            StepsGrid.InvalidateVisual();
            return;
        }
        if (e.ChangedButton != MouseButton.Left) return;
        Snapshot();
        Pat.SetNote(_channel, step, note, 1);
        (_drag, _dragStart, _dragNote) = (Drag.Extend, step, note);
        StepsGrid.CaptureMouse();
        StepsGrid.InvalidateVisual();
        Preview(note);
    }

    void OnGridMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(StepsGrid);
        switch (_drag)
        {
            case Drag.Volume:
                SetVolume(p);
                break;
            case Drag.Extend:
                int len = Math.Max(1, StepsGrid.StepAt(p.X) - _dragStart + 1);
                if (len == Pat.Length(_channel, _dragStart)) return;
                Pat.SetNote(_channel, _dragStart, _dragNote, len);
                StepsGrid.InvalidateVisual();
                break;
        }
    }

    void OnGridUp(object sender, MouseButtonEventArgs e)
    {
        _drag = Drag.None;
        StepsGrid.ReleaseMouseCapture();
    }

    /// <summary>
    /// Piano-roll keyboard (WCAG 2.1.1): arrows move the cursor, Enter adds/removes a note,
    /// Shift+←/→ shortens/lengthens it, Del removes it, +/− change its volume. Space still plays.
    /// </summary>
    void OnGridKey(object sender, KeyEventArgs e)
    {
        var (st, n) = StepsGrid.KeyCursor;
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        int start = Pat.NoteStart(_channel, st);
        switch (e.Key)
        {
            case Key.Left or Key.Right when shift:
                if (start < 0) return;
                Snapshot();
                int len = Pat.Length(_channel, start) + (e.Key == Key.Right ? 1 : -1);
                Pat.SetNote(_channel, start, Pat.Notes[_channel][start], len);
                st = start + Pat.Length(_channel, start) - 1;
                break;
            case Key.Left: st = Math.Max(0, st - 1); break;
            case Key.Right: st = Math.Min(_song.Steps - 1, st + 1); break;
            case Key.Up: n = Math.Min(Song.MaxNote, n + 1); break;
            case Key.Down: n = Math.Max(Song.MinNote, n - 1); break;
            case Key.Enter:
                Snapshot();
                if (start >= 0 && Pat.Notes[_channel][start] == n) Pat.RemoveNote(_channel, start);
                else { Pat.SetNote(_channel, st, n, 1); Preview(n); }
                break;
            case Key.Delete:
                if (start < 0) return;
                Snapshot();
                Pat.RemoveNote(_channel, start);
                break;
            case Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract:
                if (start < 0) return;
                Snapshot();
                int d = e.Key is Key.OemPlus or Key.Add ? 1 : -1;
                Pat.Volumes[_channel][start] = Math.Clamp(Pat.Volumes[_channel][start] + d, 0, Pattern.MaxVolume);
                break;
            default: return;
        }
        StepsGrid.KeyCursor = (st, n);
        StepsGrid.BringIntoView(StepsGrid.CellRect(st, n));
        StepsGrid.InvalidateVisual();
        AnnounceCursor();
        e.Handled = true;
    }

    /// <summary>Tells screen readers what is under the piano-roll cursor.</summary>
    void AnnounceCursor()
    {
        var (st, n) = StepsGrid.KeyCursor;
        int start = Pat.NoteStart(_channel, st);
        string state = start < 0 ? "empty"
            : $"note {Song.NoteName(Pat.Notes[_channel][start])}, {Pat.Length(_channel, start)} steps, volume {Pat.Volumes[_channel][start]}";
        System.Windows.Automation.AutomationProperties.SetName(StepsGrid,
            $"Piano roll, pattern {_pattern + 1}, {(Channel)_channel}. Cursor {Song.NoteName(n)}, step {st + 1}: {state}");
    }

    void SetVolume(Point p)
    {
        int step = StepsGrid.StepAt(p.X), vol = StepsGrid.VolumeAt(p.Y);
        if (Pat.Volumes[_channel][step] == vol) return;
        Pat.Volumes[_channel][step] = vol;
        StepsGrid.InvalidateVisual();
    }

    void SelectChannel(int ch)
    {
        _channel = ch;
        ((RadioButton)FindName($"Ch{ch}")).IsChecked = true;
        RefreshInstrument();
        RedrawGrid();
    }

    void OnChannel(object sender, RoutedEventArgs e) => SelectChannel(((FrameworkElement)sender).Name[2] - '0');

    // ---------- instrument (per channel) ----------

    readonly List<(Slider Slider, TextBlock Value, Func<Instrument, double> Get, string Unit)> _instSliders = [];
    ComboBox _dutyBox = null!;
    StackPanel _dutyRow = null!;
    bool _refreshingInstrument;
    static readonly double[] Duties = [0.125, 0.25, 0.5, 0.75];

    void BuildInstrumentPanel()
    {
        _dutyBox = new ComboBox { ItemsSource = new[] { "12.5 %", "25 %", "50 %", "75 %" }, Width = 96 };
        System.Windows.Automation.AutomationProperties.SetName(_dutyBox, "Pulse duty cycle");
        _dutyBox.SelectionChanged += (_, _) =>
        {
            if (_refreshingInstrument || _dutyBox.SelectedIndex < 0) return;
            Snapshot();
            _song.Instruments[_channel].Duty = Duties[_dutyBox.SelectedIndex];
        };
        _dutyRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 4),
            Children = { new TextBlock { Text = "Duty ", Width = 54, VerticalAlignment = VerticalAlignment.Center }, _dutyBox },
        };
        InstrumentPanel.Children.Add(_dutyRow);

        AddSlider("Attack", 0, 500, i => i.AttackMs, (i, v) => i.AttackMs = v, "ms");
        AddSlider("Decay", 0, 1000, i => i.DecayMs, (i, v) => i.DecayMs = v, "ms");
        AddSlider("Sustain", 0, 100, i => i.Sustain * 100, (i, v) => i.Sustain = v / 100, "%");
        AddSlider("Release", 0, 1000, i => i.ReleaseMs, (i, v) => i.ReleaseMs = v, "ms");
        AddSlider("Volume", 0, 100, i => i.Volume * 100, (i, v) => i.Volume = v / 100, "%");
    }

    void AddSlider(string name, double min, double max, Func<Instrument, double> get, Action<Instrument, double> set, string unit)
    {
        var slider = new Slider { Minimum = min, Maximum = max, IsSnapToTickEnabled = true, TickFrequency = 1 };
        var value = new TextBlock { FontSize = 11, Foreground = (System.Windows.Media.Brush)FindResource("MutedText"), HorizontalAlignment = HorizontalAlignment.Right };
        slider.PreviewMouseLeftButtonDown += (_, _) => Snapshot();   // one undo step per drag
        slider.ValueChanged += (_, _) =>
        {
            value.Text = $"{slider.Value:0} {unit}";
            if (!_refreshingInstrument) set(_song.Instruments[_channel], slider.Value);
        };
        var label = new TextBlock { Text = name, Width = 54 };
        DockPanel.SetDock(label, Dock.Left);
        InstrumentPanel.Children.Add(new DockPanel { Margin = new Thickness(2, 2, 0, 0), Children = { label, value } });
        InstrumentPanel.Children.Add(slider);
        _instSliders.Add((slider, value, get, unit));
    }

    void RefreshInstrument()
    {
        _refreshingInstrument = true;
        var ins = _song.Instruments[_channel];
        foreach (var (slider, value, get, unit) in _instSliders)
        {
            slider.Value = get(ins);
            value.Text = $"{slider.Value:0} {unit}";
        }
        _dutyRow.Visibility = _channel is (int)Channel.Pulse1 or (int)Channel.Pulse2 ? Visibility.Visible : Visibility.Collapsed;
        _dutyBox.SelectedIndex = Array.IndexOf(Duties, Duties.MinBy(d => Math.Abs(d - ins.Duty)));
        _refreshingInstrument = false;
    }

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
            Ui.MarkInvalid(OrderBox, $"Use pattern numbers 1 to {_song.Patterns.Count}, separated by spaces (e.g. 1 1 2 1). Not applied.");
            return;
        }
        Ui.MarkInvalid(OrderBox, null);
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
        if (!int.TryParse(BpmBox.Text, out int bpm) || bpm < 20 || bpm > 400)
        {
            Ui.MarkInvalid(BpmBox, "Tempo must be a whole number from 20 to 400. Not applied.");
            return;
        }
        Ui.MarkInvalid(BpmBox, null);
        if (bpm == _song.Bpm) return;
        Snapshot();
        _song.Bpm = bpm;
    }

    // ---------- playback (live: the stream reads the current song every step) ----------

    void OnPlaySong(object? sender, RoutedEventArgs? e) => Play(null);
    void OnPlayPattern(object sender, RoutedEventArgs e) => Play([_pattern]);
    void OnStop(object? sender, RoutedEventArgs? e) => Stop();

    /// <param name="order">Fixed pattern order, or null to follow the song's (editable) order.</param>
    void Play(IReadOnlyList<int>? order)
    {
        _stream = new SongStream(() => _song, order, loop: true);
        AudioOut.Shared.Song = _stream;
        _playheadTimer.Start();
    }

    void Stop()
    {
        _playheadTimer.Stop();
        if (_stream is not null && AudioOut.Shared.Song == _stream) AudioOut.Shared.Song = null;
        _stream = null;
        StepsGrid.Playhead = -1;
        StepsGrid.InvalidateVisual();
    }

    void UpdatePlayhead()
    {
        if (_stream is null) return;
        var order = _stream.Order ?? _song.Order;
        int oi = _stream.CurrentOrderIndex;
        int head = oi < order.Count && order[oi] == _pattern ? _stream.CurrentStep : -1;
        if (head == StepsGrid.Playhead) return;
        StepsGrid.Playhead = head;
        StepsGrid.InvalidateVisual();
    }

    void Preview(int note)
    {
        if (_stream is not null) return;   // the song is playing; don't talk over it
        var s = new Song(steps: 2) { Bpm = _song.Bpm };
        s.Instruments[_channel] = _song.Instruments[_channel].Clone();
        s.Patterns[0].Notes[_channel][0] = note;
        AudioOut.Shared.PlayOnce(Synth.Render(s));
    }

    // ---------- undo ----------

    void OnUndo(object? sender, RoutedEventArgs? e) => Restore(_history.Undo(_song));
    void OnRedo(object? sender, RoutedEventArgs? e) => Restore(_history.Redo(_song));

    void Restore(Song? s)
    {
        if (s is null || _drag != Drag.None) return;
        _song = s;   // a playing stream follows automatically (it reads _song)
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
        if (dlg.ShowDialog() == true) OpenPath(dlg.FileName, confirmed: true);
    }

    public string[] FileExtensions => [".pxs"];
    public bool IsDirty => _dirty;
    public string? CurrentPath => _path;

    public void OpenPath(string path, bool confirmed = false)
    {
        if (!confirmed && !ConfirmDiscard()) return;
        try { LoadSong(Song.Load(path), path); App.AddRecent(path); }
        catch (Exception ex) { Ui.Error(ex); }
    }

    public void WriteAutosave(string file) => Song.Save(_song, file);

    public void RestoreAutosave(string file, string? originalPath)
    {
        LoadSong(Song.Load(file), originalPath);
        _dirty = true;
        UpdateTitle();
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
            App.AddRecent(path);
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
            case Key.S when ctrl && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift): Save(null); break;
            case Key.S when ctrl: OnSave(null, null); break;
            case Key.N when ctrl: OnNew(null, null); break;
            case Key.O when ctrl: OnOpen(null, null); break;
            case Key.Space: if (_stream is null) OnPlaySong(null, null); else Stop(); break;
            case >= Key.D1 and <= Key.D4 when !ctrl: SelectChannel(e.Key - Key.D1); break;
            default: e.Handled = false; break;
        }
    }
}
