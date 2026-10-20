using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.App;

/// <summary>
/// Scene › Motion: animation libraries (.pxanim). A timeline per clip with one row per animated property; keys are
/// dragged in time, double-clicking a row adds a key, and the selected key's value, curve and Bezier handles are
/// edited on the right. The preview box plays the clip on the selected track's target.
/// </summary>
sealed class MotionEditor : UserControl, IEditor
{
    const double PxPerMs = 0.25, RowH = 26;
    const string Filter = "PixelForge animations (*.pxanim)|*.pxanim";

    AnimLibrary _doc = NewLibrary();
    readonly History<AnimLibrary> _history = new(l => l.Clone(), App.Settings.UndoLimit);
    string? _path;
    bool _dirty, _refreshing;
    int _clip;
    AnimTrack? _track;
    AnimKey? _key;
    double _time;
    DateTime _last;

    readonly ComboBox _clips = new() { Width = 160 }, _playMode = new() { Width = 100 }, _ease = new();
    readonly CheckBox _reverse = new() { Content = "Reverse", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
    readonly ToggleButton _play = new() { Content = "▶ Play", Padding = new Thickness(10, 2, 10, 2) };
    readonly Slider _scrub = new() { Width = 260, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    readonly TextBlock _timeText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Width = 70 };
    readonly StackPanel _rows = new();
    readonly TextBox _kTime = new(), _kValue = new(), _x1 = new(), _y1 = new(), _x2 = new(), _y2 = new();
    readonly Canvas _curve = new() { Height = 120, Background = Brushes.Transparent, ClipToBounds = true };
    readonly Canvas _stage = new() { Width = 200, Height = 160, ClipToBounds = true };
    readonly Rectangle _box = new() { Width = 32, Height = 32, Fill = Brushes.Orange, RenderTransformOrigin = new Point(0.5, 0.5) };
    readonly TextBlock _status = new() { Margin = new Thickness(10, 4, 10, 4), Opacity = 0.8 };
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    static AnimLibrary NewLibrary() => new()
    {
        Clips = [new AnimClip { Name = "move", Tracks = [new AnimTrack { Property = AnimProperty.X, Keys = [new AnimKey(0, 0, AnimEase.EaseInOut), new AnimKey(1000, 48)] }] }],
    };

    public MotionEditor()
    {
        Focusable = false;
        _playMode.ItemsSource = Enum.GetNames<AnimPlay>();
        _ease.ItemsSource = Enum.GetNames<AnimEase>();
        foreach (var (c, n) in new (FrameworkElement, string)[] { (_clips, "Clip"), (_playMode, "Play mode"), (_scrub, "Time"), (_ease, "Curve"), (_kTime, "Key time (ms)"), (_kValue, "Key value"), (_x1, "Bezier X1"), (_y1, "Bezier Y1"), (_x2, "Bezier X2"), (_y2, "Bezier Y2") })
            AutomationProperties.SetName(c, n);

        Button Btn(string text, Action a, string? tip = null) { var b = new Button { Content = text, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2), ToolTip = tip }; b.Click += (_, _) => a(); return b; }
        var bar = new WrapPanel { Margin = new Thickness(10, 6, 10, 6) };
        foreach (var e in new UIElement[]
        {
            new TextBlock { Text = "Clip", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }, _clips,
            Btn("New", NewClip), Btn("Rename…", RenameClip), Btn("Duplicate", DuplicateClip), Btn("Delete", DeleteClip),
            new TextBlock { Text = "Play", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 6, 0) }, _playMode, _reverse, _play, _scrub, _timeText,
        }) bar.Children.Add(e);
        DockPanel.SetDock(bar, Dock.Top);

        Label L(string t, Control c) => new() { Content = t, Target = c, Padding = new Thickness(0, 6, 0, 2) };
        var side = new StackPanel { Margin = new Thickness(10) };
        side.Children.Add(new TextBlock { Text = "Tracks", FontWeight = FontWeights.SemiBold });
        side.Children.Add(new WrapPanel { Margin = new Thickness(0, 4, 0, 8), Children = { Btn("Add track…", AddTrack), Btn("Remove track", RemoveTrack) } });
        side.Children.Add(new TextBlock { Text = "Selected key", FontWeight = FontWeights.SemiBold });
        side.Children.Add(L("Time (ms)", _kTime)); side.Children.Add(_kTime);
        side.Children.Add(L("Value", _kValue)); side.Children.Add(_kValue);
        side.Children.Add(L("Curve to the next key", _ease)); side.Children.Add(_ease);
        var bez = new UniformGrid { Columns = 4, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var t in new[] { _x1, _y1, _x2, _y2 }) { t.Margin = new Thickness(0, 0, 4, 0); bez.Children.Add(t); }
        side.Children.Add(L("Bezier x1 y1 x2 y2", _x1)); side.Children.Add(bez);
        side.Children.Add(new WrapPanel { Margin = new Thickness(0, 8, 0, 0), Children = { Btn("Apply", ApplyKey), Btn("Delete key", DeleteKey) } });
        side.Children.Add(new TextBlock { Text = "Preview (selected track's target)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4) });
        _stage.Children.Add(_box);
        side.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = _stage, HorizontalAlignment = HorizontalAlignment.Left });
        var right = new ScrollViewer { Width = 260, Content = side, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DockPanel.SetDock(right, Dock.Right);

        var curveHost = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 1, 0, 0), Child = _curve, Margin = new Thickness(10, 0, 10, 0) };
        AutomationProperties.SetName(_curve, "Curve of the selected track");
        DockPanel.SetDock(curveHost, Dock.Bottom);
        DockPanel.SetDock(_status, Dock.Bottom);
        var timeline = new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(10, 0, 10, 6) };
        Content = new DockPanel { Children = { bar, right, _status, curveHost, timeline } };

        _clips.SelectionChanged += (_, _) => { if (!_refreshing && _clips.SelectedIndex >= 0) { _clip = _clips.SelectedIndex; (_track, _key) = (null, null); Refresh(); } };
        _playMode.SelectionChanged += (_, _) => EditClip(c => c.Play = Enum.Parse<AnimPlay>((string)_playMode.SelectedItem));
        _ease.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _key is not null) foreach (var b in new[] { _x1, _y1, _x2, _y2 }) b.IsEnabled = (string)_ease.SelectedItem == nameof(AnimEase.Bezier);
        };
        _reverse.Click += (_, _) => EditClip(c => c.Reverse = _reverse.IsChecked == true);
        _play.Checked += (_, _) => { _last = DateTime.Now; _timer.Start(); };
        _play.Unchecked += (_, _) => _timer.Stop();
        _scrub.ValueChanged += (_, _) => { if (!_refreshing && _play.IsChecked != true) { _time = _scrub.Value; ShowTime(); } };
        _timer.Tick += (_, _) =>
        {
            var now = DateTime.Now;
            _time += (now - _last).TotalMilliseconds;
            _last = now;
            ShowTime();
        };
        IsVisibleChanged += (_, _) => { UpdateTitle(); if (!IsVisible) _play.IsChecked = false; };
        Loaded += Init;
        void Init(object s, RoutedEventArgs e) { Loaded -= Init; LoadDoc(_doc, null); }
    }

    AnimClip Cur => _doc.Clips[Math.Clamp(_clip, 0, _doc.Clips.Count - 1)];

    // ---------- document ----------

    void LoadDoc(AnimLibrary l, string? path)
    {
        if (l.Clips.Count == 0) l.Clips.Add(new AnimClip { Name = "clip" });
        (_doc, _path, _dirty, _clip, _track, _key, _time) = (l, path, false, 0, null, null, 0);
        _history.Clear();
        Refresh();
        UpdateTitle();
    }

    void Snapshot()
    {
        _history.Push(_doc);
        _dirty = true;
        UpdateTitle();
    }

    void UpdateTitle()
    {
        if (IsVisible && Window.GetWindow(this) is { } win)
            win.Title = $"PixelForge — {(_path is null ? "untitled animations" : System.IO.Path.GetFileName(_path))}{(_dirty ? " *" : "")}";
    }

    void EditClip(Action<AnimClip> edit)
    {
        if (_refreshing) return;
        Snapshot();
        edit(Cur);
        Refresh();
    }

    void Refresh()
    {
        _refreshing = true;
        _clip = Math.Clamp(_clip, 0, _doc.Clips.Count - 1);
        _clips.ItemsSource = _doc.Clips.Select(c => c.Name).ToList();
        _clips.SelectedIndex = _clip;
        _playMode.SelectedItem = Cur.Play.ToString();
        _reverse.IsChecked = Cur.Reverse;
        _scrub.Maximum = Math.Max(1, Cur.Play == AnimPlay.PingPong ? 2 * Cur.DurationMs : Cur.DurationMs);
        if (_track is not null && !Cur.Tracks.Contains(_track)) _track = null;
        if (_key is not null && _track?.Keys.Contains(_key) != true) _key = null;
        _refreshing = false;
        BuildRows();
        ShowKey();
        ShowTime();
        _status.Text = $"{_doc.Clips.Count} clips · “{Cur.Name}” lasts {Cur.DurationMs / 1000.0:0.##} s · double-click a row to add a key, drag keys to move them, Del removes the selected key";
    }

    static string Label(AnimTrack t) => (t.Target.Length > 0 ? t.Target + " · " : "") + t.Property;

    void BuildRows()
    {
        _rows.Children.Clear();
        double width = Math.Max(600, (Cur.DurationMs + 1000) * PxPerMs);
        // time ruler
        var ruler = new Canvas { Width = width, Height = 18 };
        for (int ms = 0; ms * PxPerMs <= width; ms += 250)
        {
            var tick = new Line { X1 = ms * PxPerMs, X2 = ms * PxPerMs, Y1 = ms % 1000 == 0 ? 6 : 12, Y2 = 18, Stroke = Brushes.Gray };
            ruler.Children.Add(tick);
            if (ms % 1000 == 0) { var t = new TextBlock { Text = $"{ms / 1000} s", FontSize = 10, Opacity = 0.7 }; Canvas.SetLeft(t, ms * PxPerMs + 2); ruler.Children.Add(t); }
        }
        _rows.Children.Add(new DockPanel { Children = { new TextBlock { Width = 150 }, ruler } });
        foreach (var track in Cur.Tracks)
        {
            var label = new TextBlock { Text = Label(track), Width = 150, VerticalAlignment = VerticalAlignment.Center, FontWeight = track == _track ? FontWeights.SemiBold : FontWeights.Normal };
            var row = new Canvas { Width = width, Height = RowH, Background = track == _track ? new SolidColorBrush(Color.FromArgb(40, 255, 165, 0)) : new SolidColorBrush(Color.FromArgb(18, 128, 128, 128)) };
            AutomationProperties.SetName(row, $"Track {Label(track)}");
            var t = track;
            row.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2) { AddKey(t, e.GetPosition(row).X / PxPerMs); e.Handled = true; return; }
                (_track, _key) = (t, null);
                Refresh();
            };
            foreach (var key in track.Keys)
            {
                var k = key;
                var mark = new Rectangle
                {
                    Width = 10, Height = 10, Fill = k == _key ? Brushes.Orange : Brushes.DeepSkyBlue, Stroke = Brushes.Black, StrokeThickness = 0.5,
                    RenderTransform = new RotateTransform(45, 5, 5), Cursor = Cursors.SizeWE, ToolTip = $"{k.TimeMs} ms = {k.Value:0.##} ({k.Ease})",
                };
                Canvas.SetLeft(mark, k.TimeMs * PxPerMs - 5);
                Canvas.SetTop(mark, RowH / 2 - 5);
                mark.MouseLeftButtonDown += (_, e) => { e.Handled = true; StartDrag(t, k, mark, row); };
                row.Children.Add(mark);
            }
            row.Children.Add(new Line { X1 = 0, X2 = 0, Y1 = 0, Y2 = RowH, Stroke = Brushes.IndianRed, StrokeThickness = 1.5, IsHitTestVisible = false, Tag = "head" });
            _rows.Children.Add(new DockPanel { Margin = new Thickness(0, 1, 0, 1), Children = { label, row } });
        }
        if (Cur.Tracks.Count == 0) _rows.Children.Add(new TextBlock { Text = "No tracks yet: use Add track… on the right.", Margin = new Thickness(0, 10, 0, 0), Opacity = 0.75 });
    }

    void StartDrag(AnimTrack track, AnimKey key, Rectangle mark, Canvas row)
    {
        (_track, _key) = (track, key);
        mark.Fill = Brushes.Orange;
        ShowKey();
        mark.CaptureMouse();
        bool moved = false;
        int time = key.TimeMs;
        void Move(object? s, MouseEventArgs e)
        {
            time = Math.Max(0, (int)Math.Round(e.GetPosition(row).X / PxPerMs / 10) * 10);   // 10 ms steps
            moved |= time != key.TimeMs;
            Canvas.SetLeft(mark, time * PxPerMs - 5);
        }
        void Up(object? s, MouseButtonEventArgs e)
        {
            mark.MouseMove -= Move;
            mark.MouseLeftButtonUp -= Up;
            mark.ReleaseMouseCapture();
            if (moved) MoveKey(track, key, time); else Refresh();
        }
        mark.MouseMove += Move;
        mark.MouseLeftButtonUp += Up;
    }

    void MoveKey(AnimTrack track, AnimKey key, int time)
    {
        if (track.Keys.Any(k => k != key && k.TimeMs == time)) { _status.Text = $"There is already a key at {time} ms."; Refresh(); return; }
        Snapshot();
        var (t, k) = Same(track, key);
        int i = t.Keys.IndexOf(k);
        t.Keys[i] = k with { TimeMs = time };
        t.Keys.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
        (_track, _key) = (t, t.Keys.First(x => x.TimeMs == time));
        Refresh();
    }

    /// <summary>The track and key in the current document (records are compared by value, tracks by position).</summary>
    (AnimTrack, AnimKey) Same(AnimTrack track, AnimKey key)
    {
        var t = Cur.Tracks.First(x => x.Target == track.Target && x.Property == track.Property);
        return (t, t.Keys.First(x => x == key));
    }

    void AddKey(AnimTrack track, double ms)
    {
        int time = Math.Max(0, (int)Math.Round(ms / 10) * 10);
        if (track.Keys.Any(k => k.TimeMs == time)) return;
        double value = track.Keys.Count > 0 ? AnimClip.Sample(track, time) : AnimClip.Neutral(track.Property);
        Snapshot();
        var t = Cur.Tracks.First(x => x.Target == track.Target && x.Property == track.Property);
        var key = new AnimKey(time, value, t.Keys.LastOrDefault(k => k.TimeMs < time)?.Ease ?? AnimEase.Linear);
        t.Keys.Add(key);
        t.Keys.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
        (_track, _key) = (t, key);
        Refresh();
    }

    void ShowKey()
    {
        _refreshing = true;
        bool on = _key is not null;
        foreach (var c in new Control[] { _kTime, _kValue, _ease, _x1, _y1, _x2, _y2 }) c.IsEnabled = on;
        var k = _key ?? new AnimKey(0, 0);
        (_kTime.Text, _kValue.Text, _ease.SelectedItem) = (on ? $"{k.TimeMs}" : "", on ? $"{k.Value:0.###}" : "", k.Ease.ToString());
        (_x1.Text, _y1.Text, _x2.Text, _y2.Text) = ($"{k.X1:0.##}", $"{k.Y1:0.##}", $"{k.X2:0.##}", $"{k.Y2:0.##}");
        foreach (var b in new[] { _x1, _y1, _x2, _y2 }) b.IsEnabled = on && k.Ease == AnimEase.Bezier;
        _refreshing = false;
        DrawCurve();
    }

    void ApplyKey()
    {
        if (_track is null || _key is null) return;
        bool ok = int.TryParse(_kTime.Text, out int time) & double.TryParse(_kValue.Text, out double value);
        double[] b = new double[4];
        var boxes = new[] { _x1, _y1, _x2, _y2 };
        for (int i = 0; i < 4; i++) ok &= double.TryParse(boxes[i].Text, out b[i]);
        Ui.MarkInvalid(_kTime, time < 0 || !int.TryParse(_kTime.Text, out _) ? "Time is a whole number of milliseconds, 0 or more." : null);
        if (!ok || time < 0) return;
        if (_track.Keys.Any(k => k != _key && k.TimeMs == time)) { Ui.MarkInvalid(_kTime, $"There is already a key at {time} ms."); return; }
        Snapshot();
        var (t, k) = Same(_track, _key);
        var ease = Enum.Parse<AnimEase>((string)_ease.SelectedItem);
        var nk = new AnimKey(time, value, ease, Math.Clamp(b[0], 0, 1), b[1], Math.Clamp(b[2], 0, 1), b[3]);
        t.Keys[t.Keys.IndexOf(k)] = nk;
        t.Keys.Sort((x, y) => x.TimeMs.CompareTo(y.TimeMs));
        (_track, _key) = (t, nk);
        Refresh();
    }

    void DeleteKey()
    {
        if (_track is null || _key is null) return;
        Snapshot();
        var (t, k) = Same(_track, _key);
        t.Keys.Remove(k);
        (_track, _key) = (t, null);
        Refresh();
    }

    void AddTrack()
    {
        var v = Ui.AskFields(this, "Add track", ("Target (empty = the whole object, camera or UI element; a body part's name for rigs)", _track?.Target ?? "", null),
            ("Property", nameof(AnimProperty.Y), Enum.GetNames<AnimProperty>()));
        if (v is null) return;
        string target = v[0].Trim();
        var prop = Enum.Parse<AnimProperty>(v[1]);
        if (Cur.Tracks.Any(t => t.Target == target && t.Property == prop)) { _status.Text = "The clip already animates that property."; return; }
        Snapshot();
        double n = AnimClip.Neutral(prop);
        var track = new AnimTrack { Target = target, Property = prop, Keys = [new AnimKey(0, n), new AnimKey(Math.Max(500, Cur.DurationMs), n)] };
        Cur.Tracks.Add(track);
        (_track, _key) = (track, null);
        Refresh();
    }

    void RemoveTrack()
    {
        if (_track is null) return;
        Snapshot();
        Cur.Tracks.RemoveAll(t => t.Target == _track.Target && t.Property == _track.Property);
        (_track, _key) = (null, null);
        Refresh();
    }

    string? AskClipName(string title, string value)
    {
        var name = Ui.Ask(this, title, "Clip name", value)?.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        if (_doc.Clips.Any(c => c.Name == name)) { Ui.Error(new InvalidDataException($"There is already a clip named “{name}”.")); return null; }
        return name;
    }

    void NewClip()
    {
        if (AskClipName("New clip", $"clip{_doc.Clips.Count + 1}") is not { } name) return;
        Snapshot();
        _doc.Clips.Add(new AnimClip { Name = name });
        (_clip, _track, _key) = (_doc.Clips.Count - 1, null, null);
        Refresh();
    }

    void RenameClip()
    {
        if (AskClipName("Rename clip", Cur.Name) is not { } name) return;
        Snapshot();
        Cur.Name = name;
        Refresh();
    }

    void DuplicateClip()
    {
        string name = Enumerable.Range(2, 999).Select(i => $"{Cur.Name} {i}").First(n => _doc.Clips.All(c => c.Name != n));
        Snapshot();
        var copy = Cur.Clone();
        copy.Name = name;
        _doc.Clips.Add(copy);
        (_clip, _track, _key) = (_doc.Clips.Count - 1, null, null);
        Refresh();
    }

    void DeleteClip()
    {
        if (_doc.Clips.Count == 1) { _status.Text = "A library keeps at least one clip."; return; }
        Snapshot();
        _doc.Clips.RemoveAt(_clip);
        (_clip, _track, _key) = (Math.Max(0, _clip - 1), null, null);
        Refresh();
    }

    /// <summary>Playhead, scrubber and the preview box at the current time.</summary>
    void ShowTime()
    {
        double local = Cur.LocalTime(_time);
        _refreshing = true;
        _scrub.Value = Math.Clamp(Cur.Play == AnimPlay.Once ? local : _time % Math.Max(1, _scrub.Maximum), 0, _scrub.Maximum);
        _refreshing = false;
        _timeText.Text = $"{local / 1000:0.00} s";
        foreach (var row in _rows.Children.OfType<DockPanel>().Select(d => d.Children.OfType<Canvas>().FirstOrDefault()).OfType<Canvas>())
            foreach (var head in row.Children.OfType<Line>().Where(l => "head".Equals(l.Tag))) head.X1 = head.X2 = local * PxPerMs;
        var pose = Cur.Pose(_track?.Target ?? "", _time);
        _box.RenderTransform = new TransformGroup
        {
            Children = { new ScaleTransform(pose.ScaleX, pose.ScaleY), new RotateTransform(pose.Rotation), new TranslateTransform(pose.X, pose.Y) },
        };
        _box.Opacity = Math.Clamp(pose.Opacity, 0, 1);
        Canvas.SetLeft(_box, _stage.Width / 2 - 16);
        Canvas.SetTop(_box, _stage.Height / 2 - 16);
    }

    /// <summary>Value of the selected track over the clip, with its keys.</summary>
    void DrawCurve()
    {
        _curve.Children.Clear();
        if (_track is not { Keys.Count: > 0 } t) return;
        double w = Math.Max(1, _curve.ActualWidth > 0 ? _curve.ActualWidth : 600), h = _curve.Height - 10;
        double end = Math.Max(1, t.Keys[^1].TimeMs);
        var values = Enumerable.Range(0, 201).Select(i => AnimClip.Sample(t, end * i / 200)).ToList();
        double lo = values.Min(), hi = values.Max(), span = Math.Max(1e-6, hi - lo);
        var line = new Polyline { Stroke = Brushes.DeepSkyBlue, StrokeThickness = 1.5 };
        for (int i = 0; i <= 200; i++) line.Points.Add(new Point(w * i / 200, 5 + h - (values[i] - lo) / span * h));
        _curve.Children.Add(line);
        foreach (var k in t.Keys)
        {
            var dot = new Ellipse { Width = 6, Height = 6, Fill = k == _key ? Brushes.Orange : Brushes.DeepSkyBlue };
            Canvas.SetLeft(dot, w * k.TimeMs / end - 3);
            Canvas.SetTop(dot, 5 + h - (k.Value - lo) / span * h - 3);
            _curve.Children.Add(dot);
        }
        _curve.Children.Add(new TextBlock { Text = $"{Label(t)}: {lo:0.##} … {hi:0.##}", FontSize = 10, Opacity = 0.7 });
    }

    // ---------- file ----------

    void OnNew() { if (ConfirmDiscard()) LoadDoc(NewLibrary(), null); }

    void OnOpen()
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog { Filter = Filter, InitialDirectory = App.Project?.Root ?? "" };
        if (dlg.ShowDialog() == true) OpenPath(dlg.FileName, confirmed: true);
    }

    bool Save(string? path)
    {
        if (path is null)
        {
            var dlg = new SaveFileDialog { Filter = Filter, FileName = "animations.pxanim", InitialDirectory = App.Project?.Root ?? "" };
            if (dlg.ShowDialog() != true) return false;
            path = dlg.FileName;
        }
        try
        {
            AnimLibrary.Save(_doc, path);
            (_path, _dirty) = (path, false);
            App.AddRecent(path);
            UpdateTitle();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { Ui.Error(ex); return false; }
    }

    void Undo() { if (_history.Undo(_doc) is { } l) { _doc = l; _dirty = true; Refresh(); UpdateTitle(); } }
    void Redo() { if (_history.Redo(_doc) is { } l) { _doc = l; _dirty = true; Refresh(); UpdateTitle(); } }

    // ---------- IEditor ----------

    public string[] FileExtensions => [".pxanim"];
    public bool IsDirty => _dirty;
    public string? CurrentPath => _path;

    public bool ConfirmDiscard()
    {
        if (!_dirty) return true;
        var r = MessageBox.Show("Save changes to the current animations?", "PixelForge", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return r == MessageBoxResult.No || (r == MessageBoxResult.Yes && Save(_path));
    }

    public void OpenPath(string path, bool confirmed = false)
    {
        if (!confirmed && !ConfirmDiscard()) return;
        try { LoadDoc(AnimLibrary.Load(path), path); App.AddRecent(path); }
        catch (Exception ex) when (ex is InvalidDataException or IOException) { Ui.Error(ex); }
    }

    public void OnFileMoved(string oldPath, string newPath)
    {
        if (string.Equals(_path, oldPath, StringComparison.OrdinalIgnoreCase)) { _path = newPath; UpdateTitle(); }
    }

    public void WriteAutosave(string file) => AnimLibrary.Save(_doc, file);

    public void RestoreAutosave(string file, string? originalPath)
    {
        LoadDoc(AnimLibrary.Load(file), originalPath);
        _dirty = true;
        UpdateTitle();
    }

    IReadOnlyList<MenuItem>? _menus;

    public IReadOnlyList<MenuItem> Menus => _menus ??=
    [
        Ui.Menu("_File",
            Ui.Item("_New animations", "Ctrl+N", OnNew),
            Ui.Item("_Open…", "Ctrl+O", OnOpen),
            Ui.RecentMenu(this),
            Ui.Item("_Save", "Ctrl+S", () => Save(_path)),
            Ui.Item("Save _as…", "Ctrl+Shift+S", () => Save(null)),
            new Separator(),
            Ui.Item("E_xit", "Alt+F4", () => Window.GetWindow(this)?.Close())),
        Ui.Menu("_Edit",
            Ui.Item("_Undo", "Ctrl+Z", Undo),
            Ui.Item("_Redo", "Ctrl+Y", Redo),
            new Separator(),
            Ui.Item("_New clip…", null, NewClip),
            Ui.Item("D_uplicate clip", null, DuplicateClip),
            Ui.Item("_Add track…", null, AddTrack),
            Ui.Item("_Delete key", "Del", DeleteKey)),
        Ui.Menu("_Play",
            Ui.Item("_Play / stop", "Space", () => _play.IsChecked = _play.IsChecked != true),
            Ui.Item("_Rewind", "Home", () => { _time = 0; ShowTime(); })),
    ];

    public void HandleKey(KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Z when ctrl: Undo(); break;
            case Key.Y when ctrl: Redo(); break;
            case Key.S when ctrl && shift: Save(null); break;
            case Key.S when ctrl: Save(_path); break;
            case Key.N when ctrl: OnNew(); break;
            case Key.O when ctrl: OnOpen(); break;
            case Key.Delete: DeleteKey(); break;
            case Key.Space: _play.IsChecked = _play.IsChecked != true; break;
            case Key.Home: _time = 0; ShowTime(); break;
            default: e.Handled = false; break;
        }
    }
}
