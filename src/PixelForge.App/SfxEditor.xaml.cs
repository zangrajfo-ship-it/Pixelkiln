using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.App;

/// <summary>sfxr-style sound effect generator: presets, sliders, play, save, export WAV.</summary>
public partial class SfxEditor : UserControl
{
    Sfx _sfx = Sfx.Preset("Coin", new Random(1));
    readonly Random _rnd = new();
    string? _path;
    bool _dirty, _refreshing;

    readonly List<(Slider Slider, TextBlock Value, Func<Sfx, double> Get, string Format)> _sliders = [];
    readonly Dictionary<SfxWave, RadioButton> _waves = [];
    UIElement _dutyRow = null!;

    public SfxEditor()
    {
        InitializeComponent();
        foreach (var name in Sfx.PresetNames)
        {
            var b = new Button { Content = name, Height = 30, Margin = new Thickness(2) };
            b.Click += (_, _) => SetSfx(Sfx.Preset(name, _rnd));
            PresetPanel.Children.Add(b);
        }
        var mutate = new Button { Content = "Mutate", Height = 30, Margin = new Thickness(2, 12, 2, 2), ToolTip = "Small random changes" };
        mutate.Click += (_, _) => SetSfx(_sfx.Mutate(_rnd));
        PresetPanel.Children.Add(mutate);
        BuildParams();

        Loaded += Init;
        IsVisibleChanged += (_, _) => UpdateTitle();
        void Init(object s, RoutedEventArgs e) { Loaded -= Init; Refresh(); }
    }

    // ---------- parameters ----------

    void BuildParams()
    {
        var waveRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        waveRow.Children.Add(new TextBlock { Text = "Wave", Width = 110, VerticalAlignment = VerticalAlignment.Center });
        foreach (var w in Enum.GetValues<SfxWave>())
        {
            var r = new RadioButton { Content = w.ToString(), GroupName = "wave", Margin = new Thickness(0, 0, 12, 0) };
            r.Click += (_, _) => { _sfx.Wave = w; Changed(); Play(); };
            _waves[w] = r;
            waveRow.Children.Add(r);
        }
        ParamsPanel.Children.Add(waveRow);

        AddSlider("Frequency", 20, 4000, s => s.Freq, (s, v) => s.Freq = v, "0 Hz");
        AddSlider("Slide", -10, 10, s => s.Slide, (s, v) => s.Slide = v, "0.00 oct/s");
        AddSlider("Slide change", -10, 10, s => s.DeltaSlide, (s, v) => s.DeltaSlide = v, "0.00");
        AddSlider("Vibrato depth", 0, 0.5, s => s.VibratoDepth, (s, v) => s.VibratoDepth = v, "0.00");
        AddSlider("Vibrato speed", 0, 40, s => s.VibratoSpeed, (s, v) => s.VibratoSpeed = v, "0.0 Hz");
        _dutyRow = AddSlider("Duty", 0.05, 0.95, s => s.Duty, (s, v) => s.Duty = v, "0.00");
        AddSlider("Pitch jump", -24, 24, s => s.PitchJump, (s, v) => s.PitchJump = v, "0.0 semitones");
        AddSlider("Jump time", 0, 1, s => s.JumpTime, (s, v) => s.JumpTime = v, "0.00 s");
        AddSlider("Attack", 0, 1, s => s.Attack, (s, v) => s.Attack = v, "0.00 s");
        AddSlider("Sustain", 0, 2, s => s.Sustain, (s, v) => s.Sustain = v, "0.00 s");
        AddSlider("Decay", 0, 2, s => s.Decay, (s, v) => s.Decay = v, "0.00 s");
        AddSlider("Low-pass", 0.01, 1, s => s.LowPass, (s, v) => s.LowPass = v, "0.00 (1 = off)");
        AddSlider("Volume", 0, 1, s => s.Volume, (s, v) => s.Volume = v, "0.00");
    }

    UIElement AddSlider(string name, double min, double max, Func<Sfx, double> get, Action<Sfx, double> set, string format)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Width = 300, VerticalAlignment = VerticalAlignment.Center };
        var value = new TextBlock { Width = 120, Margin = new Thickness(8, 0, 0, 0), Foreground = (Brush)FindResource("MutedText"), VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, _) =>
        {
            value.Text = slider.Value.ToString(format);
            if (_refreshing) return;
            set(_sfx, slider.Value);
            Changed();
        };
        slider.PreviewMouseLeftButtonUp += (_, _) => Play();   // hear it when you let go
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2),
            Children = { new TextBlock { Text = name, Width = 110, VerticalAlignment = VerticalAlignment.Center }, slider, value },
        };
        ParamsPanel.Children.Add(row);
        _sliders.Add((slider, value, get, format));
        return row;
    }

    void SetSfx(Sfx s)
    {
        _sfx = s;
        _dirty = true;
        Refresh();
        Play();
    }

    void Changed()
    {
        _dirty = true;
        _dutyRow.Visibility = _sfx.Wave == SfxWave.Square ? Visibility.Visible : Visibility.Collapsed;
        DrawWave();
        UpdateTitle();
    }

    void Refresh()
    {
        _refreshing = true;
        foreach (var (slider, value, get, format) in _sliders)
        {
            slider.Value = get(_sfx);
            value.Text = slider.Value.ToString(format);
        }
        _waves[_sfx.Wave].IsChecked = true;
        _refreshing = false;
        _dutyRow.Visibility = _sfx.Wave == SfxWave.Square ? Visibility.Visible : Visibility.Collapsed;
        DrawWave();
        UpdateTitle();
    }

    // ---------- waveform + sound ----------

    void OnWaveSize(object sender, SizeChangedEventArgs e) => DrawWave();

    /// <summary>Min/max per pixel column of the rendered sound.</summary>
    void DrawWave()
    {
        double w = WaveCanvas.ActualWidth, h = WaveCanvas.ActualHeight;
        if (w < 2 || h < 2) return;
        var x = _sfx.Render();
        var pts = new PointCollection();
        int cols = (int)w;
        for (int c = 0; c < cols; c++)
        {
            int a = (int)((long)c * x.Length / cols), b = Math.Max(a + 1, (int)((long)(c + 1) * x.Length / cols));
            float lo = 0, hi = 0;
            for (int i = a; i < b && i < x.Length; i++) { lo = Math.Min(lo, x[i]); hi = Math.Max(hi, x[i]); }
            pts.Add(new Point(c, h / 2 - hi * h / 2));
            pts.Add(new Point(c, h / 2 - lo * h / 2));
        }
        WaveLine.Points = pts;
        Status.Text = $"{_sfx.Duration:0.00} s";
    }

    void Play() => AudioOut.Shared.PlayOnce(_sfx.Render());

    void OnPlay(object? sender, RoutedEventArgs? e) => Play();

    // ---------- files ----------

    const string SfxFilter = "PixelForge sound (*.sfx)|*.sfx";

    void UpdateTitle() => Ui.ShowTitle(this, _path, _dirty, "untitled sound");

    public bool ConfirmDiscard() => Ui.ConfirmDiscard(_dirty, "sound", () => Save(_path));

    void OnOpen(object? sender, RoutedEventArgs? e)
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog { Filter = SfxFilter };
        if (dlg.ShowDialog() == true) OpenPath(dlg.FileName, confirmed: true);
    }

    public string[] FileExtensions => [".sfx"];
    public bool IsDirty => _dirty;
    public string? CurrentPath => _path;

    /// <summary>The asset browser renamed or moved the open file: keep saving to the new place.</summary>
    public void OnFileMoved(string oldPath, string newPath)
    {
        if (!string.Equals(_path, oldPath, StringComparison.OrdinalIgnoreCase)) return;
        _path = newPath;
        UpdateTitle();
    }

    public void OpenPath(string path, bool confirmed = false)
    {
        if (!confirmed && !ConfirmDiscard()) return;
        try
        {
            _sfx = Sfx.Load(path);
            (_path, _dirty) = (path, false);
            Refresh();
            App.AddRecent(path);
        }
        catch (Exception ex) { Ui.Error(ex); }
    }

    public void WriteAutosave(string file) => Sfx.Save(_sfx, file);

    public void RestoreAutosave(string file, string? originalPath)
    {
        _sfx = Sfx.Load(file);
        (_path, _dirty) = (originalPath, true);
        Refresh();
    }

    void OnSave(object? sender, RoutedEventArgs? e) => Save(_path);

    bool Save(string? path)
    {
        if (path is null)
        {
            var dlg = new SaveFileDialog { Filter = SfxFilter, FileName = "sound.sfx" };
            if (dlg.ShowDialog() != true) return false;
            path = dlg.FileName;
        }
        try
        {
            Sfx.Save(_sfx, path);
            App.AddRecent(path);
            (_path, _dirty) = (path, false);
            UpdateTitle();
            return true;
        }
        catch (Exception ex) { Ui.Error(ex); return false; }
    }

    void OnExportWav(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "WAV audio (*.wav)|*.wav", FileName = (System.IO.Path.GetFileNameWithoutExtension(_path) ?? "sound") + ".wav" };
        if (dlg.ShowDialog() != true) return;
        try { File.WriteAllBytes(dlg.FileName, WavWriter.ToBytes(_sfx.Render())); }
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
            case Key.Space: Play(); break;
            case Key.S when ctrl && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift): Save(null); break;
            case Key.S when ctrl: OnSave(null, null); break;
            case Key.O when ctrl: OnOpen(null, null); break;
            default: e.Handled = false; break;
        }
    }
}
