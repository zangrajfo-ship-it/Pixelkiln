using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pixelkiln.Core;

namespace Pixelkiln.App;

/// <summary>
/// Game preview: plays a scene at the project's resolution with a simple platformer player, music and trigger sounds.
/// Runs on a copy (Sandbox), so nothing it does reaches the scene or its assets.
/// </summary>
sealed class GamePreview : Window
{
    Sandbox _sb;
    readonly Project _project;
    readonly AudioLibrary _audio;   // one library, so each sound is rendered once and replayed from its cache
    Scene _scene;
    readonly WriteableBitmap _bmp;
    readonly TextBlock _status = new() { Margin = new Thickness(8, 4, 8, 4), TextWrapping = TextWrapping.Wrap };
    readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    readonly Stopwatch _clock = new();
    readonly HashSet<Key> _down = [];
    readonly int _w, _h;
    bool _boxes, _muted, _paused;
    string _lastEvent = "";
    double _last;

    public static void Show(Window owner, Scene scene, Project project)
    {
        var assets = new SceneAssets(project);
        new GamePreview(scene, project, new Sandbox(scene, assets, project.Settings.TargetWidth, project.Settings.TargetHeight)) { Owner = owner }.Show();
    }

    GamePreview(Scene scene, Project project, Sandbox sb)
    {
        (_scene, _project, _sb) = (scene, project, sb);
        _audio = new AudioLibrary(project);
        (_w, _h) = (project.Settings.TargetWidth, project.Settings.TargetHeight);
        _bmp = new WriteableBitmap(_w, _h, 96, 96, PixelFormats.Bgra32, null);
        var img = new Image { Source = _bmp, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        AutomationProperties.SetName(img, "Game view");
        DockPanel.SetDock(_status, Dock.Bottom);
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        int scale = Math.Max(1, Math.Min(4, 960 / _w));
        Title = $"Game preview — {(scene.Level.Length > 0 ? scene.Level : "scene")}";
        Width = _w * scale + 40;
        Height = _h * scale + 120;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Black;
        Content = new DockPanel { Children = { _status, img } };
        Focusable = true;

        KeyDown += OnKey;
        KeyUp += (_, e) => _down.Remove(e.Key);
        Deactivated += (_, _) => _down.Clear();   // keys released in another window must not stick
        _timer.Tick += (_, _) => Frame();
        Loaded += (_, _) => { StartMusic(); _clock.Start(); _timer.Start(); Draw(); };
        Closed += (_, _) => { _timer.Stop(); AudioOut.Shared.StopClip(); };
    }

    void StartMusic()
    {
        AudioOut.Shared.StopClip();
        if (_muted || _scene.MusicAssetId is not Guid id) return;
        try
        {
            var s = _audio.Samples(id);
            if (s.Length == 0) return;
            var loop = _project.Find(id)?.Type == AssetType.Mix && Mix.Load(_project.Resolve(id)!) is { LoopStartMs: int a, LoopEndMs: int b }
                ? (AudioEdit.Samples(a), Math.Min(AudioEdit.Samples(b), s.Length)) : (0, s.Length);
            AudioOut.Shared.PlayClip(s, 0, loop.Item1, loop.Item2);
        }
        catch (InvalidDataException ex) { _lastEvent = $"Music not played: {ex.Message}"; }
    }

    bool Held(params Key[] keys) => keys.Any(_down.Contains);

    void Frame()
    {
        double now = _clock.Elapsed.TotalMilliseconds, dt = now - _last;
        _last = now;
        if (_paused) return;
        _sb.Step(dt, new SandboxInput(Held(Key.Left, Key.A), Held(Key.Right, Key.D), Held(Key.Space, Key.Up, Key.W, Key.Z), Held(Key.Up, Key.W), Held(Key.Down, Key.S)));
        foreach (var a in _sb.Entered)
        {
            _lastEvent = $"{a.Kind} “{a.Name}”" + (a.Properties.Count > 0 ? " · " + string.Join(", ", a.Properties.Select(kv => $"{kv.Key}={kv.Value}")) : "");
            if (!_muted && a.Properties.TryGetValue("sound", out var sound) && _project.Assets.FirstOrDefault(x => string.Equals(x.Path, sound, StringComparison.OrdinalIgnoreCase) || x.Name == sound) is { } asset)
                try { AudioOut.Shared.PlayOnce(_audio.Samples(asset.Id)); }
                catch (InvalidDataException ex) { _lastEvent += $" (sound not played: {ex.Message})"; }
        }
        // what the scene's logic asked for
        if (!_muted)
            foreach (var id in _sb.SoundsToPlay)
                try { AudioOut.Shared.PlayOnce(_audio.Samples(id)); }
                catch (InvalidDataException ex) { _lastEvent = $"sound not played: {ex.Message}"; }
        if (_sb.RequestedScene is Guid next && _project.Resolve(next) is string path)
            try
            {
                _scene = Scene.Load(path);
                _sb = new Sandbox(_scene, new SceneAssets(_project), _w, _h);
                _lastEvent = $"scene “{_project.Find(next)?.Name}”";
                Title = $"Game preview — {(_scene.Level.Length > 0 ? _scene.Level : _project.Find(next)?.Name)}";
                StartMusic();
            }
            catch (InvalidDataException ex) { _lastEvent = $"scene not loaded: {ex.Message}"; }
        Draw();
    }

    void Draw()
    {
        var px = _sb.Render(_boxes);
        _bmp.WritePixels(new Int32Rect(0, 0, _w, _h), px, _w * 4, 0);
        string who = _sb.Player is null ? "No player character: arrows move the camera." : $"Player {_sb.X:0},{_sb.Y:0} · {(_sb.OnGround ? "on ground" : "in air")} · {_sb.Player.Animation ?? "-"} · deaths {_sb.Deaths}";
        string logic = _sb.LogicError is { } err ? $"\nLogic not running: {err}"
            : _sb.Logic is { } l && (l.Variables.Count > 0 || l.Items.Count > 0)
                ? "\n" + string.Join(" · ", l.Variables.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value:0.##}").Concat(l.Items.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} ×{kv.Value}")))
                : "";
        _status.Text = $"{_sb.TimeMs / 1000:0.0} s{(_paused ? " (paused)" : "")} · {who}{(_lastEvent.Length > 0 ? " · last: " + _lastEvent : "")}{logic}\n"
                       + "←→/A D move · Space/↑ jump · R reset · H hitboxes · M mute · P pause · Esc close";
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        _down.Add(e.Key);
        switch (e.Key)
        {
            case Key.R: _sb.Reset(); _lastEvent = "reset"; StartMusic(); Draw(); break;
            case Key.H: _boxes = !_boxes; Draw(); break;
            case Key.M: _muted = !_muted; StartMusic(); break;
            case Key.P: _paused = !_paused; Draw(); break;
            case Key.Escape: Close(); break;
            default: return;
        }
        e.Handled = true;
    }
}
