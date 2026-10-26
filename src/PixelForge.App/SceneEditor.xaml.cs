using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.App;

/// <summary>
/// Scene &amp; level studio: tilemap/image/object layers with parallax, placed sprites and characters (with
/// movement tracks), areas (trigger, checkpoint, spawn, camera, kill), camera start, validation.
/// Drawn with WPF elements (one per object), so moving things around stays fast in big scenes.
/// </summary>
public partial class SceneEditor : UserControl, IEditor, IStyleSource
{
    static readonly double[] Zooms = [0.25, 0.5, 1, 2, 3, 4, 6, 8];

    Scene _scene = new();
    readonly ViewAids _aids;
    readonly History<Scene> _history = new(s => s.Clone(), App.Settings.UndoLimit);
    string? _path;
    bool _dirty, _refreshing;
    double _zoom = 2;
    SceneAssets? _assets;
    Guid? _layerId;
    object? _sel;   // SceneInstance or SceneArea

    // dragging
    Point? _dragFrom;
    (int X, int Y) _dragOrig;
    bool _dragSnapshotDone;
    Rectangle? _areaPreview;

    // playback
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    readonly Stopwatch _clock = new();
    readonly List<(SceneInstance Inst, Image Img, InstanceLook Look)> _live = [];
    readonly Dictionary<(Sprite, int), BitmapSource> _frames = [];

    public SceneEditor()
    {
        InitializeComponent();
        _aids = new ViewAids(this, SceneCanvas, Scroller, SceneCanvas, () => _zoom);
        AreaKindBox.ItemsSource = Enum.GetNames<AreaKind>();
        AreaKindBox.SelectedIndex = 0;
        SelAreaKindBox.ItemsSource = Enum.GetNames<AreaKind>();
        RenderOptions.SetBitmapScalingMode(SceneCanvas, BitmapScalingMode.NearestNeighbor);
        _timer.Tick += (_, _) => Animate(_clock.Elapsed.TotalMilliseconds);
        App.ProjectChanged += () => { if (IsLoaded) { _assets = null; RefreshAll(); } };
        Loaded += Init;
        IsVisibleChanged += (_, _) => { UpdateTitle(); if (IsVisible && IsLoaded) Rebuild(); };   // other tabs may have changed the assets
        IsKeyboardFocusedChanged += (_, _) => Scroller.BorderBrush = IsKeyboardFocused ? (Brush)FindResource("FocusRing") : Brushes.Transparent;

        void Init(object s, RoutedEventArgs e) { Loaded -= Init; LoadScene(_scene, null); }
    }

    SceneAssets? Assets => App.Project is { } p ? _assets ??= new SceneAssets(p) : null;
    int Tile => App.Project?.Settings.TileSize ?? 16;

    // ---------- document ----------

    void LoadScene(Scene s, string? path)
    {
        StopPlay();
        _scene = s;
        _path = path;
        _dirty = false;
        _aids.Bind(path);
        _history.Clear();
        _sel = null;
        _layerId = s.Layers.LastOrDefault(l => l.Kind == SceneLayerKind.Instances)?.Id ?? s.Layers.Last().Id;
        RefreshAll();
    }

    void Snapshot()
    {
        _history.Push(_scene);
        _dirty = true;
        UpdateTitle();
    }

    void RefreshAll()
    {
        _refreshing = true;
        WidthBox.Text = _scene.Width.ToString();
        HeightBox.Text = _scene.Height.ToString();
        LevelBox.Text = _scene.Level;
        OrderBox.Text = _scene.Order.ToString();
        BackgroundBox.Text = ColorUtil.ToHex(_scene.Background);
        var p = App.Project;
        var songs = p?.Assets.Where(a => a.Type == AssetType.Song).ToList() ?? [];
        MusicBox.ItemsSource = new[] { new ComboBoxItem { Content = "(none)", Tag = null } }.Concat(songs.Select(a => new ComboBoxItem { Content = a.Path, Tag = a.Id })).ToList();
        MusicBox.SelectedItem = MusicBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag is Guid g && g == _scene.MusicAssetId) ?? MusicBox.Items[0];
        var kits = p?.Assets.Where(a => a.Type == AssetType.Ui).ToList() ?? [];
        UiBox.ItemsSource = new[] { new ComboBoxItem { Content = "(none)", Tag = null } }.Concat(kits.Select(a => new ComboBoxItem { Content = a.Path, Tag = a.Id })).ToList();
        UiBox.SelectedItem = UiBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag is Guid g && g == _scene.UiAssetId) ?? UiBox.Items[0];
        string[] screens = [];
        if (_scene.UiAssetId is Guid uid && p?.Resolve(uid) is string uiPath)
            try { screens = [.. UiKit.Load(uiPath).Screens.Select(s => s.Name)]; } catch (InvalidDataException) { }
        UiScreenBox.ItemsSource = screens;
        UiScreenBox.SelectedItem = screens.FirstOrDefault(s => s == _scene.UiScreen);
        UiScreenBox.IsEnabled = screens.Length > 0;
        RefreshEffects();
        var graphs = p?.Assets.Where(a => a.Type == AssetType.Logic).OrderBy(a => a.Path).ToList() ?? [];
        LogicBox.ItemsSource = new[] { new ComboBoxItem { Content = "(none)", Tag = null } }.Concat(graphs.Select(a => new ComboBoxItem { Content = a.Path, Tag = a.Id })).ToList();
        LogicBox.SelectedItem = LogicBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag is Guid g && g == _scene.LogicAssetId) ?? LogicBox.Items[0];
        CameraClipBox.ItemsSource = ClipItems("(none)");
        SelectClip(CameraClipBox, _scene.CameraClipAssetId, _scene.CameraClip);
        PlaceList.ItemsSource = p?.Assets.Where(a => a.Type is AssetType.Sprite or AssetType.Character).OrderBy(a => a.Type).ThenBy(a => a.Path)
            .Select(a => new ListBoxItem { Content = $"{(a.Type == AssetType.Character ? "☺ " : "")}{a.Path}", Tag = a.Id }).ToList();
        _refreshing = false;
        RefreshLayers();
        RefreshSelection();
        Rebuild();
        UpdateTitle();
        Status.Text = p is null ? "Open a project (Project menu): scenes are built from project assets." : $"{_scene.Width}×{_scene.Height} px, {_scene.Instances.Count} objects, {_scene.Areas.Count} areas";
    }

    void UpdateTitle() => Ui.ShowTitle(this, _path, _dirty, "untitled scene");

    // ---------- drawing ----------

    BitmapSource FrameBitmap(Sprite s, int frame)
    {
        if (!_frames.TryGetValue((s, frame), out var b))
        {
            b = ImageIO.ToBitmap(Compositor.Flatten(s, frame), s.Width, s.Height);
            b.Freeze();
            _frames[(s, frame)] = b;
        }
        return b;
    }

    /// <summary>Rebuilds every element of the canvas from the scene (after edits, undo, load).</summary>
    void Rebuild()
    {
        _litImage = null;
        SceneCanvas.Children.Clear();
        _live.Clear();
        _frames.Clear();
        SceneCanvas.Width = _scene.Width;
        SceneCanvas.Height = _scene.Height;
        SceneCanvas.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        ZoomText.Text = $"{_zoom * 100:0} %";
        SceneCanvas.Children.Add(new Rectangle { Width = _scene.Width, Height = _scene.Height, Fill = Ui.BrushOf(_scene.Background) });
        var a = Assets;
        foreach (var l in _scene.Layers.Where(l => l.Visible))
        {
            switch (l.Kind)
            {
                case SceneLayerKind.Tilemap when l.AssetId is Guid id && a?.TilemapImage(id) is var (px, w, h):
                    Place(new Image { Source = ImageIO.ToBitmap(px, w, h), Width = w, Height = h }, l.OffsetX, l.OffsetY);
                    break;
                case SceneLayerKind.Image when l.AssetId is Guid id && a?.Image(id) is var (px, w, h):
                    var bmp = ImageIO.ToBitmap(px, w, h);
                    if (l.RepeatX)
                        Place(new Rectangle
                        {
                            Width = _scene.Width, Height = h,
                            Fill = new ImageBrush(bmp) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, w, h), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill },
                        }, l.OffsetX % w - w, l.OffsetY);
                    else Place(new Image { Source = bmp, Width = w, Height = h }, l.OffsetX, l.OffsetY);
                    break;
                case SceneLayerKind.Instances:
                    foreach (var i in _scene.InstancesOf(l.Id)) AddInstance(i);
                    break;
            }
        }
        foreach (var ar in _scene.Areas) AddArea(ar);
        _aids.Attach(SceneCanvas);
        _aids.Layout(_scene.Width, _scene.Height, 1, 1 / _zoom);
        // camera start: the game's view at the target resolution
        var (tw, th) = Target;
        Place(new Rectangle { Width = tw, Height = th, Stroke = Brushes.White, StrokeThickness = 1 / _zoom, StrokeDashArray = [6, 4], IsHitTestVisible = false }, _scene.CameraX, _scene.CameraY);
        Animate(0);
        DrawEffects();
        DrawSelection();
    }

    (int W, int H) Target => App.Project is { } p ? (p.Settings.TargetWidth, p.Settings.TargetHeight) : (320, 180);

    void Place(UIElement e, double x, double y)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        SceneCanvas.Children.Add(e);
    }

    void AddInstance(SceneInstance i)
    {
        if (Assets?.Look(i) is not { } look)
        {
            var missing = new Border { Width = 16, Height = 16, Background = Brushes.Magenta, Tag = i, ToolTip = "Missing asset", Child = new TextBlock { Text = "?", HorizontalAlignment = HorizontalAlignment.Center } };
            Place(missing, i.X, i.Y);
            return;
        }
        var img = new Image { Width = look.Sprite.Width, Height = look.Sprite.Height, Tag = i, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        SceneCanvas.Children.Add(img);
        _live.Add((i, img, look));
    }

    /// <summary>Positions every object for a moment in time: sprite frame, flip, and movement track (scale, rotation, opacity).</summary>
    void Animate(double t)
    {
        foreach (var (i, img, look) in _live)
        {
            img.Source = FrameBitmap(look.Sprite, SceneAssets.FrameAt(look.Sprite, look.From, look.To, t));
            var pose = Assets?.ClipOf(i)?.Pose("", t) ?? i.Track?.Sample(t) ?? new Keyframe(0);
            Canvas.SetLeft(img, i.X + pose.X - look.OriginX);
            Canvas.SetTop(img, i.Y + pose.Y - look.OriginY);
            img.Opacity = pose.Opacity;
            var tg = new TransformGroup();
            tg.Children.Add(new ScaleTransform((i.FlipX ? -1 : 1) * pose.ScaleX, pose.ScaleY, look.OriginX, look.OriginY));
            if (pose.Rotation != 0) tg.Children.Add(new RotateTransform(pose.Rotation, look.OriginX, look.OriginY));
            img.RenderTransform = tg;
        }
        if (t > 0) UpdateLit(t);   // playing: particles and flicker move
    }

    static Color AreaColor(AreaKind k) => k switch
    {
        AreaKind.Spawn => Colors.LimeGreen, AreaKind.Checkpoint => Colors.DeepSkyBlue, AreaKind.Camera => Colors.Violet, AreaKind.Kill => Colors.Red, _ => Colors.Gold,
    };

    void AddArea(SceneArea ar)
    {
        var c = AreaColor(ar.Kind);
        var r = new Rectangle { Width = ar.W, Height = ar.H, Stroke = new SolidColorBrush(c), StrokeThickness = 1.5 / _zoom, Fill = new SolidColorBrush(Color.FromArgb(0x28, c.R, c.G, c.B)), Tag = ar };
        Place(r, ar.X, ar.Y);
        var label = new TextBlock { Text = ar.Name.Length > 0 ? $"{ar.Kind}: {ar.Name}" : ar.Kind.ToString(), Foreground = Brushes.White, FontSize = 10 / _zoom, IsHitTestVisible = false };
        Place(label, ar.X + 1, ar.Y + 1);
    }

    Rectangle? _selRect;

    void DrawSelection()
    {
        if (_selRect is not null) SceneCanvas.Children.Remove(_selRect);
        _selRect = null;
        if (Bounds(_sel) is not var (x, y, w, h)) return;
        _selRect = new Rectangle { Width = w + 2, Height = h + 2, Stroke = Brushes.White, StrokeThickness = 2 / _zoom, StrokeDashArray = [3, 2], IsHitTestVisible = false };
        Place(_selRect, x - 1, y - 1);
    }

    /// <summary>Scene-space box of an instance (at time 0) or area.</summary>
    (double X, double Y, double W, double H)? Bounds(object? o) => o switch
    {
        SceneArea a => (a.X, a.Y, a.W, a.H),
        SceneInstance i when Assets?.Look(i) is { } l => (i.X - l.OriginX, i.Y - l.OriginY, l.Sprite.Width, l.Sprite.Height),
        SceneInstance i => (i.X, i.Y, 16, 16),
        _ => null,
    };

    // ---------- playback ----------

    void OnPlay(object? sender, RoutedEventArgs? e)
    {
        if (_timer.IsEnabled) { StopPlay(); return; }
        PlayBtn.IsChecked = true;
        PlayBtn.Content = "■ Stop";
        _clock.Restart();
        _timer.Start();
    }

    void StopPlay()
    {
        _timer.Stop();
        PlayBtn.IsChecked = false;
        PlayBtn.Content = "▶ Play";
        Animate(0);
    }

    // ---------- zoom ----------

    void SetZoom(double z, Point? anchor = null)
    {
        double old = _zoom;
        _zoom = z;
        Rebuild();
        if (anchor is Point p)
        {
            Scroller.UpdateLayout();
            Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset + p.X * (_zoom - old));
            Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + p.Y * (_zoom - old));
        }
    }

    void ZoomStep(int dir, Point? anchor = null)
    {
        int i = Array.IndexOf(Zooms, _zoom) + dir;
        if (i >= 0 && i < Zooms.Length) SetZoom(Zooms[i], anchor);
    }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        ZoomStep(Math.Sign(e.Delta), e.GetPosition(SceneCanvas));
    }

    void OnZoomIn(object sender, RoutedEventArgs e) => ZoomStep(+1);
    void OnZoomOut(object sender, RoutedEventArgs e) => ZoomStep(-1);
    void OnZoomFit(object sender, RoutedEventArgs e) => ZoomFit();

    void ZoomFit() => SetZoom(Zooms.LastOrDefault(z => _scene.Width * z <= Scroller.ViewportWidth - 80 && _scene.Height * z <= Scroller.ViewportHeight - 80, Zooms[0]));

    // ---------- mouse ----------

    /// <summary>Guides win over the tile grid; Shift turns both off.</summary>
    int Snap(double v, bool vertical)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return (int)Math.Floor(v);
        double g = _aids.Snap(v, vertical, _zoom);
        if (g != v) return (int)Math.Round(g);
        return SnapToggle.IsChecked == true ? (int)Math.Round(v / Tile) * Tile : (int)Math.Floor(v);
    }

    object? HitTest(Point p)
    {
        foreach (var ar in Enumerable.Reverse(_scene.Areas))
            if (p.X >= ar.X && p.Y >= ar.Y && p.X < ar.X + ar.W && p.Y < ar.Y + ar.H) return ar;
        foreach (var l in Enumerable.Reverse(_scene.Layers).Where(l => l.Kind == SceneLayerKind.Instances && l.Visible))
            foreach (var i in _scene.InstancesOf(l.Id).Reverse())
                if (Bounds(i) is var (x, y, w, h) && p.X >= x && p.Y >= y && p.X < x + w && p.Y < y + h) return i;
        return null;
    }

    void OnCanvasDown(object sender, MouseButtonEventArgs e)
    {
        Keyboard.Focus(this);
        if (e.ChangedButton != MouseButton.Left || App.Project is null) return;
        var p = e.GetPosition(SceneCanvas);
        if (SelectTool.IsChecked == true)
        {
            _sel = HitTest(p);
            RefreshSelection();
            DrawSelection();
            if (_sel is null) return;
            _dragFrom = p;
            _dragOrig = _sel is SceneInstance i ? (i.X, i.Y) : _sel is SceneArea a ? (a.X, a.Y) : default;
            _dragSnapshotDone = false;
            SceneCanvas.CaptureMouse();
        }
        else if (PlaceTool.IsChecked == true) PlaceAt(p);
        else if (AreaTool.IsChecked == true)
        {
            _dragFrom = new Point(Snap(p.X, true), Snap(p.Y, false));
            _areaPreview = new Rectangle { Stroke = Brushes.White, StrokeThickness = 1 / _zoom, StrokeDashArray = [4, 3] };
            Place(_areaPreview, _dragFrom.Value.X, _dragFrom.Value.Y);
            SceneCanvas.CaptureMouse();
        }
        else if (CameraTool.IsChecked == true)
        {
            Snapshot();
            var (tw, th) = Target;
            _scene.CameraX = Math.Clamp((int)p.X - tw / 2, 0, Math.Max(0, _scene.Width - tw));
            _scene.CameraY = Math.Clamp((int)p.Y - th / 2, 0, Math.Max(0, _scene.Height - th));
            Rebuild();
        }
    }

    void OnCanvasMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(SceneCanvas);
        Status.Text = $"{(int)p.X}, {(int)p.Y}   {_scene.Width}×{_scene.Height} px";
        if (_dragFrom is not Point from) return;
        if (_areaPreview is not null)
        {
            var (x, y, w, h) = AreaRect(from, p);
            Canvas.SetLeft(_areaPreview, x);
            Canvas.SetTop(_areaPreview, y);
            (_areaPreview.Width, _areaPreview.Height) = (w, h);
            return;
        }
        if (_sel is null) return;
        if (!_dragSnapshotDone) { Snapshot(); _dragSnapshotDone = true; }   // a plain click is not an edit
        int nx = Snap(_dragOrig.X + p.X - from.X, true), ny = Snap(_dragOrig.Y + p.Y - from.Y, false);
        if (_sel is SceneInstance i) (i.X, i.Y) = (nx, ny);
        else if (_sel is SceneArea a) (a.X, a.Y) = (nx, ny);
        Rebuild();
    }

    (int X, int Y, int W, int H) AreaRect(Point from, Point p)
    {
        int x2 = Snap(p.X, true), y2 = Snap(p.Y, false);
        int x = (int)Math.Min(from.X, x2), y = (int)Math.Min(from.Y, y2);
        return (x, y, Math.Max(1, (int)Math.Abs(x2 - from.X)), Math.Max(1, (int)Math.Abs(y2 - from.Y)));
    }

    void OnCanvasUp(object sender, MouseButtonEventArgs e)
    {
        if (_areaPreview is not null && _dragFrom is Point from)
        {
            var (x, y, w, h) = AreaRect(from, e.GetPosition(SceneCanvas));
            if (w < 2 && h < 2) (w, h) = (Tile, Tile);   // a click makes a one-tile area
            Snapshot();
            var area = new SceneArea { Kind = Enum.Parse<AreaKind>((string)AreaKindBox.SelectedItem), X = x, Y = y, W = w, H = h };
            _scene.Areas.Add(area);
            _sel = area;
            _areaPreview = null;
            RefreshSelection();
            Rebuild();
        }
        _dragFrom = null;
        SceneCanvas.ReleaseMouseCapture();
        if (_dragSnapshotDone) RefreshSelection();
    }

    void PlaceAt(Point p)
    {
        if (PlaceList.SelectedItem is not ListBoxItem { Tag: Guid assetId }) { Status.Text = "Choose a sprite or character on the right first."; return; }
        Snapshot();
        var layer = _scene.Layers.FirstOrDefault(l => l.Id == _layerId && l.Kind == SceneLayerKind.Instances)
                    ?? _scene.Layers.LastOrDefault(l => l.Kind == SceneLayerKind.Instances);
        if (layer is null)
        {
            layer = new SceneLayer { Name = "Objects" };
            _scene.Layers.Add(layer);
        }
        var inst = new SceneInstance { AssetId = assetId, LayerId = layer.Id, X = Snap(p.X, true), Y = Snap(p.Y, false), Name = App.Project?.Find(assetId)?.Name ?? "" };
        _scene.Instances.Add(inst);
        _sel = inst;
        RefreshLayers();
        RefreshSelection();
        Rebuild();
    }

    void OnPlaceChosen(object sender, SelectionChangedEventArgs e) => PlaceTool.IsChecked = PlaceList.SelectedItem is not null;

    // ---------- scene fields ----------

    void OnApplySize(object sender, RoutedEventArgs e)
    {
        bool ok = int.TryParse(WidthBox.Text, out int w) & int.TryParse(HeightBox.Text, out int h);
        try
        {
            if (!ok) throw new FormatException("Width and height must be whole numbers.");
            var copy = _scene.Clone();
            copy.Resize(w, h);   // validate before taking the undo step
            Snapshot();
            _scene.Resize(w, h);
            Rebuild();
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException) { Ui.Error(ex); }
    }

    void OnSceneFieldCommit(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        bool orderOk = int.TryParse(OrderBox.Text, out int order), bgOk = ColorUtil.TryParseHex(BackgroundBox.Text, out uint bg);
        Ui.MarkInvalid(OrderBox, orderOk ? null : "Whole number.");
        Ui.MarkInvalid(BackgroundBox, bgOk ? null : "RRGGBB or AARRGGBB.");
        if (!orderOk || !bgOk || (LevelBox.Text.Trim() == _scene.Level && order == _scene.Order && bg == _scene.Background)) return;
        Snapshot();
        (_scene.Level, _scene.Order, _scene.Background) = (LevelBox.Text.Trim(), order, bg);
        Rebuild();
    }

    void OnMusicChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || MusicBox.SelectedItem is not ComboBoxItem item) return;
        Guid? id = item.Tag as Guid?;
        if (id == _scene.MusicAssetId) return;
        Snapshot();
        _scene.MusicAssetId = id;
    }

    void OnUiChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing) return;
        Guid? kit = (UiBox.SelectedItem as ComboBoxItem)?.Tag as Guid?;
        string? screen = kit is null ? null : UiScreenBox.SelectedItem as string;
        if (kit != _scene.UiAssetId) screen = null;   // another kit: choose its screen again
        if ((kit, screen) == (_scene.UiAssetId, _scene.UiScreen)) return;
        Snapshot();
        (_scene.UiAssetId, _scene.UiScreen) = (kit, screen);
        RefreshAll();
    }

    // ---------- motion clips (animation libraries made in the Motion mode) ----------

    /// <summary>Every clip of every animation library in the project, tagged with (library, clip name).</summary>
    static List<ComboBoxItem> ClipItems(string none)
    {
        var items = new List<ComboBoxItem> { new() { Content = none, Tag = null } };
        if (App.Project is not { } p) return items;
        foreach (var a in p.Assets.Where(a => a.Type == AssetType.Anim).OrderBy(a => a.Path))
            try { items.AddRange(AnimLibrary.Load(p.FullPath(a)).Clips.Select(c => new ComboBoxItem { Content = $"{a.Path} › {c.Name}", Tag = (a.Id, c.Name) })); }
            catch (Exception ex) when (ex is InvalidDataException or IOException) { }   // Check project reports broken files
        return items;
    }

    static void SelectClip(ComboBox box, Guid? lib, string? clip) =>
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag is (Guid g, string n) && g == lib && n == clip) ?? box.Items[0];

    static (Guid? Lib, string? Clip) ClipOf(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag is (Guid g, string n) ? (g, n) : (null, null);

    void OnLogicChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing) return;
        var id = (LogicBox.SelectedItem as ComboBoxItem)?.Tag as Guid?;
        if (id == _scene.LogicAssetId) return;
        Snapshot();
        _scene.LogicAssetId = id;
    }

    void OnCameraClipChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing) return;
        var (lib, clip) = ClipOf(CameraClipBox);
        if ((lib, clip) == (_scene.CameraClipAssetId, _scene.CameraClip)) return;
        Snapshot();
        (_scene.CameraClipAssetId, _scene.CameraClip) = (lib, clip);
    }

    void OnClipChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || _sel is not SceneInstance i) return;
        var (lib, clip) = ClipOf(ClipBox);
        if ((lib, clip) == (i.ClipAssetId, i.Clip)) return;
        Snapshot();
        (i.ClipAssetId, i.Clip) = (lib, clip);
        Rebuild();
    }

    void OnSaveTrackAsClip(object sender, RoutedEventArgs e)
    {
        if (_sel is not SceneInstance { Track: { Keys.Count: > 0 } track } i) { Ui.Error(new InvalidOperationException("The selected object has no movement track.")); return; }
        if (App.Project is not { } p) { Ui.Error(new InvalidOperationException("Open a project first.")); return; }
        const string newFile = "(new library)";
        var libs = p.Assets.Where(a => a.Type == AssetType.Anim).OrderBy(a => a.Path).ToList();
        var v = Ui.AskFields(this, "Save track as clip", ("Animation library", libs.FirstOrDefault()?.Path ?? newFile, [newFile, .. libs.Select(a => a.Path)]),
            ("Clip name", i.Name.Length > 0 ? i.Name : "move", null));
        if (v is null || v[1].Trim() is not { Length: > 0 } name) return;
        try
        {
            string path;
            AnimLibrary lib;
            if (v[0] == newFile)
            {
                path = Enumerable.Range(1, 999).Select(n => System.IO.Path.Combine(p.Root, n == 1 ? "animations.pxanim" : $"animations{n}.pxanim")).First(f => !File.Exists(f));
                lib = new AnimLibrary();
            }
            else
            {
                path = p.FullPath(libs.First(a => a.Path == v[0]));
                lib = AnimLibrary.Load(path);
            }
            if (lib.Clips.Any(c => c.Name == name)) { Ui.Error(new InvalidDataException($"The library already has a clip named “{name}”.")); return; }
            lib.Clips.Add(AnimClip.FromTrack(track, name));
            AnimLibrary.Save(lib, path);
            App.AddRecent(path);   // registers a new library in the project
            var id = p.FindByPath(path)!.Id;
            Snapshot();
            (i.ClipAssetId, i.Clip, i.Track) = (id, name, null);
            _assets = null;   // the library changed on disk
            RefreshAll();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { Ui.Error(ex); }
    }

    // ---------- layers (list shows the front layer first) ----------

    SceneLayer? Layer => _scene.Layers.FirstOrDefault(l => l.Id == _layerId);

    void RefreshLayers()
    {
        _refreshing = true;
        LayersList.Items.Clear();
        foreach (var l in Enumerable.Reverse(_scene.Layers))
        {
            string par = l.ParallaxX == 1 && l.ParallaxY == 1 ? "" : $" · parallax {l.ParallaxX:0.##}";
            LayersList.Items.Add(new ListBoxItem { Content = $"{(l.Visible ? "" : "(hidden) ")}{l.Name} · {l.Kind}{par}", Tag = l.Id });
        }
        LayersList.SelectedItem = LayersList.Items.Cast<ListBoxItem>().FirstOrDefault(i => (Guid)i.Tag == _layerId);
        var layer = Layer;
        LayerProps.Visibility = layer is null ? Visibility.Collapsed : Visibility.Visible;
        if (layer is not null)
        {
            LayerNameBox.Text = layer.Name;
            ParallaxXBox.Text = layer.ParallaxX.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            ParallaxYBox.Text = layer.ParallaxY.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            OffsetXBox.Text = layer.OffsetX.ToString();
            OffsetYBox.Text = layer.OffsetY.ToString();
            RepeatXBox.IsChecked = layer.RepeatX;
            RepeatXBox.IsEnabled = layer.Kind == SceneLayerKind.Image;
            LayerVisibleBox.IsChecked = layer.Visible;
            var type = layer.Kind == SceneLayerKind.Tilemap ? new[] { AssetType.Tilemap } : [AssetType.Image, AssetType.Sprite, AssetType.Paint, AssetType.Vector];
            LayerAssetBox.IsEnabled = layer.Kind != SceneLayerKind.Instances;
            LayerAssetBox.ItemsSource = layer.Kind == SceneLayerKind.Instances ? null
                : App.Project?.Assets.Where(a => type.Contains(a.Type)).OrderBy(a => a.Path).Select(a => new ComboBoxItem { Content = a.Path, Tag = a.Id }).ToList();
            LayerAssetBox.SelectedItem = LayerAssetBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag is Guid g && g == layer.AssetId);
        }
        _refreshing = false;
    }

    void OnLayerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || LayersList.SelectedItem is not ListBoxItem { Tag: Guid id }) return;
        _layerId = id;
        RefreshLayers();
    }

    void AddLayer(SceneLayer l)
    {
        Snapshot();
        int at = Layer is { } cur ? _scene.Layers.IndexOf(cur) + 1 : _scene.Layers.Count;
        _scene.Layers.Insert(at, l);
        _layerId = l.Id;
        RefreshLayers();
        Rebuild();
    }

    void OnAddMapLayer(object sender, RoutedEventArgs e) => AddLayer(new SceneLayer { Name = "Map", Kind = SceneLayerKind.Tilemap });
    void OnAddImageLayer(object sender, RoutedEventArgs e) => AddLayer(new SceneLayer { Name = "Background", Kind = SceneLayerKind.Image, ParallaxX = 0.5, ParallaxY = 0.5 });
    void OnAddObjectLayer(object sender, RoutedEventArgs e) => AddLayer(new SceneLayer { Name = "Objects" });

    void OnDeleteLayer(object sender, RoutedEventArgs e)
    {
        if (Layer is not { } l || _scene.Layers.Count == 1) return;
        int count = _scene.Instances.Count(i => i.LayerId == l.Id);
        if (count > 0 && MessageBox.Show(Window.GetWindow(this), $"Delete layer “{l.Name}” and its {count} object(s)?", "Delete layer", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        Snapshot();
        _scene.Layers.Remove(l);
        _scene.Instances.RemoveAll(i => i.LayerId == l.Id);
        _layerId = _scene.Layers.Last().Id;
        _sel = null;
        RefreshLayers();
        RefreshSelection();
        Rebuild();
    }

    void MoveLayer(int dir)
    {
        if (Layer is not { } l) return;
        int i = _scene.Layers.IndexOf(l), to = i + dir;
        if (to < 0 || to >= _scene.Layers.Count) return;
        Snapshot();
        _scene.Layers.RemoveAt(i);
        _scene.Layers.Insert(to, l);
        RefreshLayers();
        Rebuild();
    }

    void OnLayerUp(object sender, RoutedEventArgs e) => MoveLayer(+1);
    void OnLayerDown(object sender, RoutedEventArgs e) => MoveLayer(-1);

    void OnLayerAssetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || Layer is not { } l || LayerAssetBox.SelectedItem is not ComboBoxItem { Tag: Guid id } || id == l.AssetId) return;
        Snapshot();
        l.AssetId = id;
        Rebuild();
    }

    void OnLayerFieldCommit(object sender, RoutedEventArgs e)
    {
        if (_refreshing || Layer is not { } l) return;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        bool ok = double.TryParse(ParallaxXBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, inv, out double px)
                & double.TryParse(ParallaxYBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, inv, out double py)
                & int.TryParse(OffsetXBox.Text, out int ox) & int.TryParse(OffsetYBox.Text, out int oy);
        Ui.MarkInvalid(ParallaxXBox, ok ? null : "Numbers, e.g. 0.5 for half speed; offsets are whole pixels.");
        if (!ok) return;
        string name = LayerNameBox.Text.Trim().Length > 0 ? LayerNameBox.Text.Trim() : l.Name;
        if ((name, px, py, ox, oy, RepeatXBox.IsChecked == true, LayerVisibleBox.IsChecked == true) == (l.Name, l.ParallaxX, l.ParallaxY, l.OffsetX, l.OffsetY, l.RepeatX, l.Visible)) return;
        Snapshot();
        (l.Name, l.ParallaxX, l.ParallaxY, l.OffsetX, l.OffsetY, l.RepeatX, l.Visible) = (name, px, py, ox, oy, RepeatXBox.IsChecked == true, LayerVisibleBox.IsChecked == true);
        RefreshLayers();
        Rebuild();
    }

    // ---------- selection properties ----------

    void RefreshSelection()
    {
        _refreshing = true;
        SelectionProps.Visibility = _sel is null ? Visibility.Collapsed : Visibility.Visible;
        InstanceProps.Visibility = _sel is SceneInstance ? Visibility.Visible : Visibility.Collapsed;
        AreaProps.Visibility = _sel is SceneArea ? Visibility.Visible : Visibility.Collapsed;
        SelWBox.Visibility = SelHBox.Visibility = _sel is SceneArea ? Visibility.Visible : Visibility.Collapsed;
        switch (_sel)
        {
            case SceneInstance i:
                SelectionTitle.Text = $"Object: {App.Project?.Find(i.AssetId)?.Path ?? "missing asset"}";
                SelNameBox.Text = i.Name;
                (SelXBox.Text, SelYBox.Text) = (i.X.ToString(), i.Y.ToString());
                var anims = Assets?.Look(i) is { } look
                    ? look.Character is { } c ? c.Animations.Keys.ToList() : look.Sprite.Tags.Select(t => t.Name).ToList()
                    : [];
                AnimBox.ItemsSource = new[] { "(default)" }.Concat(anims).ToList();
                AnimBox.SelectedItem = i.Animation is not null && anims.Contains(i.Animation) ? i.Animation : "(default)";
                FlipBox.IsChecked = i.FlipX;
                ZBox.Text = i.Z.ToString();
                TrackBox.Text = i.Track is null ? "" : string.Join("\n", i.Track.Keys.Select(k => FormatKey(k)));
                LoopBox.IsChecked = i.Track?.Loop ?? true;
                ClipBox.ItemsSource = ClipItems("(movement track)");
                SelectClip(ClipBox, i.ClipAssetId, i.Clip);
                SelPropsBox.Text = string.Join("\n", i.Properties.Select(kv => $"{kv.Key} = {kv.Value}"));
                break;
            case SceneArea a:
                SelectionTitle.Text = $"{a.Kind} area";
                SelNameBox.Text = a.Name;
                (SelXBox.Text, SelYBox.Text, SelWBox.Text, SelHBox.Text) = (a.X.ToString(), a.Y.ToString(), a.W.ToString(), a.H.ToString());
                SelAreaKindBox.SelectedItem = a.Kind.ToString();
                SelPropsBox.Text = string.Join("\n", a.Properties.Select(kv => $"{kv.Key} = {kv.Value}"));
                break;
            default:
                SelectionTitle.Text = "Nothing selected";
                break;
        }
        _refreshing = false;
    }

    static string FormatKey(Keyframe k)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string s = string.Format(inv, "{0} {1:0.##} {2:0.##}", k.TimeMs, k.X, k.Y);
        if (k.ScaleX != 1 || k.ScaleY != 1 || k.Rotation != 0 || k.Opacity != 1 || k.Easing != Easing.Linear)
            s += string.Format(inv, " {0:0.##} {1:0.##} {2:0.##} {3:0.##} {4}", k.ScaleX, k.ScaleY, k.Rotation, k.Opacity, k.Easing);
        return s;
    }

    /// <summary>"time x y [scaleX scaleY rotation opacity easing]" per line; null with a message on bad input.</summary>
    static KeyframeTrack? ParseTrack(string text, bool loop, out string? error)
    {
        error = null;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var keys = new List<Keyframe>();
        foreach (var raw in text.Split('\n'))
        {
            var p = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) continue;
            double D(int i, double fallback) => i < p.Length && i < 7 ? double.Parse(p[i].Replace(',', '.'), inv) : fallback;
            try
            {
                if (p.Length < 3) throw new FormatException();
                var easing = p.Length >= 8 ? Enum.Parse<Easing>(p[7], ignoreCase: true) : p.Length == 4 && Enum.TryParse<Easing>(p[3], true, out var e4) ? e4 : Easing.Linear;
                keys.Add(new Keyframe(int.Parse(p[0]), D(1, 0), D(2, 0), p.Length >= 7 ? D(3, 1) : 1, p.Length >= 7 ? D(4, 1) : 1, p.Length >= 7 ? D(5, 0) : 0, p.Length >= 7 ? D(6, 1) : 1, easing));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
            {
                error = $"Line “{raw.Trim()}”: use time x y, optionally followed by an easing or by scaleX scaleY rotation opacity easing.";
                return null;
            }
        }
        return keys.Count == 0 ? null : new KeyframeTrack { Keys = keys, Loop = loop };
    }

    static Dictionary<string, string> ParseProps(string text) =>
        text.Split('\n').Select(l => l.Split('=', 2)).Where(p => p.Length == 2 && p[0].Trim().Length > 0)
            .GroupBy(p => p[0].Trim()).ToDictionary(g => g.Key, g => g.Last()[1].Trim());

    void OnSelCommit(object sender, RoutedEventArgs e)
    {
        if (_refreshing || _sel is null) return;
        bool ok = int.TryParse(SelXBox.Text, out int x) & int.TryParse(SelYBox.Text, out int y);
        Ui.MarkInvalid(SelXBox, ok ? null : "Whole pixels.");
        if (!ok) return;
        var props = ParseProps(SelPropsBox.Text);
        switch (_sel)
        {
            case SceneInstance i:
                bool zOk = int.TryParse(ZBox.Text, out int z);
                var track = ParseTrack(TrackBox.Text, LoopBox.IsChecked == true, out string? err);
                Ui.MarkInvalid(TrackBox, err);
                Ui.MarkInvalid(ZBox, zOk ? null : "Whole number.");
                if (err is not null || !zOk) return;
                Snapshot();
                (i.Name, i.X, i.Y, i.FlipX, i.Z, i.Track, i.Properties) = (SelNameBox.Text.Trim(), x, y, FlipBox.IsChecked == true, z, track, props);
                break;
            case SceneArea a:
                bool sizeOk = int.TryParse(SelWBox.Text, out int w) & int.TryParse(SelHBox.Text, out int h) && w > 0 && h > 0;
                Ui.MarkInvalid(SelWBox, sizeOk ? null : "Width and height must be at least 1.");
                if (!sizeOk) return;
                Snapshot();
                (a.Name, a.X, a.Y, a.W, a.H, a.Properties) = (SelNameBox.Text.Trim(), x, y, w, h, props);
                break;
        }
        Rebuild();
    }

    void OnAnimChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || _sel is not SceneInstance i || AnimBox.SelectedItem is not string a) return;
        string? anim = a == "(default)" ? null : a;
        if (anim == i.Animation) return;
        Snapshot();
        i.Animation = anim;
        Rebuild();
    }

    void OnSelAreaKind(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || _sel is not SceneArea a || SelAreaKindBox.SelectedItem is not string k || k == a.Kind.ToString()) return;
        Snapshot();
        a.Kind = Enum.Parse<AreaKind>(k);
        RefreshSelection();
        Rebuild();
    }

    void OnDeleteSelected(object? sender, RoutedEventArgs? e)
    {
        if (_sel is null) return;
        Snapshot();
        if (_sel is SceneInstance i) _scene.Instances.Remove(i);
        if (_sel is SceneArea a) _scene.Areas.Remove(a);
        _sel = null;
        RefreshSelection();
        Rebuild();
    }

    void Nudge(int dx, int dy)
    {
        if (_sel is null) return;
        Snapshot();
        if (_sel is SceneInstance i) (i.X, i.Y) = (i.X + dx, i.Y + dy);
        if (_sel is SceneArea a) (a.X, a.Y) = (a.X + dx, a.Y + dy);
        RefreshSelection();
        Rebuild();
    }

    void OnGamePreview(object sender, RoutedEventArgs e) => GamePreviewNow();

    /// <summary>Plays the scene as it is now (unsaved edits included) in its own window.</summary>
    void GamePreviewNow()
    {
        if (App.Project is not { } p) { Ui.Error(new InvalidOperationException("Open a project first.")); return; }
        StopPlay();
        GamePreview.Show(Window.GetWindow(this), _scene, p);
    }

    void OnCheck(object? sender, RoutedEventArgs? e)
    {
        if (App.Project is not { } p) { Ui.Error(new InvalidOperationException("Open a project first.")); return; }
        var issues = SceneCheck.Run(_scene, p);
        MessageBox.Show(Window.GetWindow(this), issues.Count == 0 ? "No problems found."
            : string.Join("\n", issues.Select(i => $"{(i.Severity == "error" ? "✖" : "⚠")} {i.Message}")), "Check scene",
            MessageBoxButton.OK, issues.Any(i => i.Severity == "error") ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    // ---------- undo ----------

    void OnUndo(object? sender, RoutedEventArgs? e) => Restore(_history.Undo(_scene));
    void OnRedo(object? sender, RoutedEventArgs? e) => Restore(_history.Redo(_scene));

    void Restore(Scene? s)
    {
        if (s is null || _dragFrom is not null) return;
        _scene = s;
        _sel = null;
        _dirty = true;
        if (_scene.Layers.All(l => l.Id != _layerId)) _layerId = _scene.Layers.Last().Id;
        RefreshAll();
    }

    // ---------- files ----------

    const string Filter = "PixelForge scene (*.pxscene)|*.pxscene";

    void OnNew(object? sender, RoutedEventArgs? e)
    {
        if (!ConfirmDiscard()) return;
        var (tw, th) = Target;
        LoadScene(new Scene { Width = tw * 2, Height = th }, null);
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
            var dlg = new SaveFileDialog { Filter = Filter, FileName = "level.pxscene", InitialDirectory = App.Project?.Root ?? "" };
            if (dlg.ShowDialog() != true) return false;
            path = dlg.FileName;
        }
        try
        {
            Scene.Save(_scene, path);
            (_path, _dirty) = (path, false);
            _aids.Rebind(path);
            UpdateTitle();
            App.AddRecent(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); return false; }
    }

    // ---------- style (Ctrl+Alt+C / V): how an object or area behaves, not where it is ----------

    sealed record ObjectStyle(string? Animation, bool FlipX, int Z, Dictionary<string, string> Properties, KeyframeTrack? Track);
    sealed record AreaStyle(AreaKind Kind, Dictionary<string, string> Properties);

    public object? CopyStyle() => _sel switch
    {
        SceneInstance i => new ObjectStyle(i.Animation, i.FlipX, i.Z, new(i.Properties), i.Track?.Clone()),
        SceneArea a => new AreaStyle(a.Kind, new(a.Properties)),
        _ => null,
    };

    public string? PasteStyle(object style)
    {
        switch (style, _sel)
        {
            case (ObjectStyle s, SceneInstance i):
                Snapshot();
                (i.Animation, i.FlipX, i.Z, i.Properties, i.Track) = (s.Animation, s.FlipX, s.Z, new(s.Properties), s.Track?.Clone());
                break;
            case (AreaStyle s, SceneArea a):
                Snapshot();
                (a.Kind, a.Properties) = (s.Kind, new(s.Properties));
                break;
            default: return null;
        }
        RefreshSelection();
        Rebuild();
        return "Style pasted to the selection.";
    }

    // ---------- IEditor ----------


    public string[] FileExtensions => [".pxscene"];
    public bool IsDirty => _dirty;
    public string? CurrentPath => _path;

    public bool ConfirmDiscard() => Ui.ConfirmDiscard(_dirty, "scene", () => Save(_path));

    public void OpenPath(string path, bool confirmed = false)
    {
        if (!confirmed && !ConfirmDiscard()) return;
        try { LoadScene(Scene.Load(path), path); App.AddRecent(path); }
        catch (Exception ex) when (ex is InvalidDataException or IOException) { Ui.Error(ex); }
    }

    public void OnFileMoved(string oldPath, string newPath)
    {
        if (string.Equals(_path, oldPath, StringComparison.OrdinalIgnoreCase)) { _path = newPath; _aids.Rebind(newPath); UpdateTitle(); }
    }

    public void WriteAutosave(string file) => Scene.Save(_scene, file);

    public void RestoreAutosave(string file, string? originalPath)
    {
        LoadScene(Scene.Load(file), originalPath);
        _dirty = true;
        UpdateTitle();
    }

    IReadOnlyList<MenuItem>? _menus;
    Action Do(RoutedEventHandler h) => () => h(this, new RoutedEventArgs());

    public IReadOnlyList<MenuItem> Menus => _menus ??=
    [
        Ui.Menu("_File",
            Ui.Item("_New scene", "Ctrl+N", Do(OnNew)),
            Ui.Item("_Open…", "Ctrl+O", Do(OnOpen)),
            Ui.RecentMenu(this),
            Ui.Item("_Save", "Ctrl+S", Do(OnSave)),
            Ui.Item("Save _as…", "Ctrl+Shift+S", () => Save(null)),
            new Separator(),
            Ui.Item("E_xit", "Alt+F4", () => Window.GetWindow(this)?.Close())),
        Ui.Menu("_Edit",
            Ui.Item("_Undo", "Ctrl+Z", Do(OnUndo)),
            Ui.Item("_Redo", "Ctrl+Y", Do(OnRedo)),
            new Separator(),
            Ui.Item("_Delete selected", "Del", () => OnDeleteSelected(null, null)),
            Ui.Item("_Check scene…", null, () => OnCheck(null, null))),
        Ui.Menu("_View",
            _aids.Menu(),
            new Separator(),
            Ui.Item("Zoom _in", "Ctrl+=", () => ZoomStep(+1)),
            Ui.Item("Zoom _out", "Ctrl+-", () => ZoomStep(-1)),
            Ui.Item("_Fit to window", "Ctrl+0", ZoomFit),
            Ui.Toggle("_Snap to tiles", null, SnapToggle),
            new Separator(),
            Ui.Item("_Play animations here", "Space", () => OnPlay(null, null)),
            Ui.Item("_Game preview…", "F5", GamePreviewNow)),
    ];

    public void HandleKey(KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        int step = shift ? Tile : 1;
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Z when ctrl: OnUndo(null, null); break;
            case Key.Y when ctrl: OnRedo(null, null); break;
            case Key.S when ctrl && shift: Save(null); break;
            case Key.S when ctrl: OnSave(null, null); break;
            case Key.N when ctrl: OnNew(null, null); break;
            case Key.O when ctrl: OnOpen(null, null); break;
            case Key.OemPlus or Key.Add when ctrl: ZoomStep(+1); break;
            case Key.OemMinus or Key.Subtract when ctrl: ZoomStep(-1); break;
            case Key.D0 or Key.NumPad0 when ctrl: ZoomFit(); break;
            case Key.V when !ctrl: SelectTool.IsChecked = true; break;
            case Key.I when !ctrl: PlaceTool.IsChecked = true; break;
            case Key.A when !ctrl: AreaTool.IsChecked = true; break;
            case Key.C when !ctrl: CameraTool.IsChecked = true; break;
            case Key.Delete: OnDeleteSelected(null, null); break;
            case Key.Left: Nudge(-step, 0); break;
            case Key.Right: Nudge(step, 0); break;
            case Key.Up: Nudge(0, -step); break;
            case Key.Down: Nudge(0, step); break;
            case Key.Space: OnPlay(null, null); break;
            case Key.F5: GamePreviewNow(); break;
            default: e.Handled = false; break;
        }
    }
}
