using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Pixelkiln.Core;

namespace Pixelkiln.App;

/// <summary>Lights, occluders and particle emitters: a list with dialogs, draggable markers and a lit preview.</summary>
public partial class SceneEditor
{
    Image? _litImage;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    void RefreshEffects()
    {
        AmbientBox.Text = ColorUtil.ToHex(_scene.Ambient);
        TileShadowsBox.IsChecked = _scene.TileShadows;
        object? keep = (EffectsList.SelectedItem as ListBoxItem)?.Tag;
        var items = _scene.Lights.Select(l => new ListBoxItem { Content = $"☀ {l.Name} ({l.Kind}, r {l.Radius})", Tag = l })
            .Concat(_scene.Emitters.Select(e => new ListBoxItem { Content = $"✦ {e.Name} ({e.Preset}, {e.Rate:0}/s)", Tag = e }))
            .Concat(_scene.Occluders.Select(o => new ListBoxItem { Content = $"▰ {o.Name} ({o.Points.Count / 2} points)", Tag = o })).ToList();
        EffectsList.ItemsSource = items;
        EffectsList.SelectedItem = items.FirstOrDefault(i => Id(i.Tag) == Id(keep));
    }

    static Guid? Id(object? fx) => fx switch { SceneLight l => l.Id, SceneEmitter e => e.Id, SceneOccluder o => o.Id, _ => null };

    void OnAmbientCommit(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        bool ok = ColorUtil.TryParseHex(AmbientBox.Text, out uint c);
        Ui.MarkInvalid(AmbientBox, ok ? null : "RRGGBB (FFFFFF = no lighting).");
        if (!ok || (c | 0xFF000000) == _scene.Ambient) return;
        Snapshot();
        _scene.Ambient = c | 0xFF000000;
        Rebuild();
    }

    void OnTileShadows(object sender, RoutedEventArgs e)
    {
        Snapshot();
        _scene.TileShadows = TileShadowsBox.IsChecked == true;
        Rebuild();
    }

    /// <summary>The middle of the camera's view: where new effects appear.</summary>
    (int X, int Y) ViewCenter() => (_scene.CameraX + Target.W / 2, _scene.CameraY + Target.H / 2);

    void OnAddLight(object sender, RoutedEventArgs e)
    {
        var v = Ui.AskFields(this, "Add light", ("Preset", "Torch", [.. SceneLight.Presets.Keys]));
        if (v is null) return;
        var light = SceneLight.Presets[v[0]]();
        (light.X, light.Y) = ViewCenter();
        Snapshot();
        if (_scene.Ambient == 0xFFFFFFFF) _scene.Ambient = 0xFF404050;   // a light in full daylight shows nothing: dim the scene
        _scene.Lights.Add(light);
        AfterEffectsChange(light);
    }

    void OnAddEmitter(object sender, RoutedEventArgs e)
    {
        var v = Ui.AskFields(this, "Add particles", ("Preset", nameof(EmitterPreset.Fire), Enum.GetNames<EmitterPreset>()));
        if (v is null) return;
        var em = SceneEmitter.FromPreset(Enum.Parse<EmitterPreset>(v[0]));
        var (cx, cy) = ViewCenter();
        // rain and snow cover the view from above it; the others start in the middle
        (em.X, em.Y, em.W) = em.Preset is EmitterPreset.Rain or EmitterPreset.Snow ? (_scene.CameraX, _scene.CameraY - 8, Target.W) : (cx, cy, em.W);
        Snapshot();
        _scene.Emitters.Add(em);
        AfterEffectsChange(em);
    }

    void OnAddOccluder(object sender, RoutedEventArgs e)
    {
        var (cx, cy) = ViewCenter();
        var o = SceneOccluder.Rect(cx - 8, cy - 8, 16, 16);
        Snapshot();
        _scene.Occluders.Add(o);
        AfterEffectsChange(o);
    }

    void OnDeleteEffect(object sender, RoutedEventArgs e)
    {
        var fx = (EffectsList.SelectedItem as ListBoxItem)?.Tag;
        if (fx is null) return;
        Snapshot();
        _scene.Lights.RemoveAll(l => l.Id == Id(fx));
        _scene.Emitters.RemoveAll(x => x.Id == Id(fx));
        _scene.Occluders.RemoveAll(o => o.Id == Id(fx));
        AfterEffectsChange(null);
    }

    void AfterEffectsChange(object? select)
    {
        RefreshEffects();
        if (select is not null) EffectsList.SelectedItem = EffectsList.Items.Cast<ListBoxItem>().FirstOrDefault(i => Id(i.Tag) == Id(select));
        Rebuild();
    }

    void OnEditEffect(object sender, RoutedEventArgs e)
    {
        switch ((EffectsList.SelectedItem as ListBoxItem)?.Tag)
        {
            case SceneLight l: EditLight(l); break;
            case SceneEmitter em: EditEmitter(em); break;
            case SceneOccluder o: EditOccluder(o); break;
        }
    }

    static string N(double v) => v.ToString("0.###", Inv);
    static string[] YesNo => ["Yes", "No"];

    /// <summary>Parses dialog numbers; reports the first bad field and returns null.</summary>
    double[]? Numbers(string[] labels, IEnumerable<string> values)
    {
        var o = new List<double>();
        foreach (var (label, text) in labels.Zip(values))
        {
            if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, Inv, out double d)) { Ui.Error(new FormatException($"{label} must be a number (“{text}”).")); return null; }
            o.Add(d);
        }
        return [.. o];
    }

    bool ParseColor(string label, string text, out uint c)
    {
        if (ColorUtil.TryParseHex(text, out c)) return true;
        Ui.Error(new FormatException($"{label}: use RRGGBB or AARRGGBB (“{text}”)."));
        return false;
    }

    void EditLight(SceneLight l)
    {
        var v = Ui.AskFields(this, $"Light “{l.Name}”", ("Name", l.Name, null), ("Kind", l.Kind.ToString(), Enum.GetNames<LightKind>()),
            ("X", $"{l.X}", null), ("Y", $"{l.Y}", null), ("Radius (1–4096)", $"{l.Radius}", null), ("Intensity (0–16)", N(l.Intensity), null),
            ("Spot direction (°, 0 = right, 90 = down)", N(l.Direction), null), ("Spot cone (°)", N(l.Cone), null),
            ("Glow (0–1)", N(l.Glow), null), ("Flicker (0–1)", N(l.Flicker), null), ("Colour", ColorUtil.ToHex(l.Color), null), ("Casts shadows", l.Shadows ? "Yes" : "No", YesNo));
        if (v is null || Numbers(["X", "Y", "Radius", "Intensity", "Direction", "Cone", "Glow", "Flicker"], v[2..10]) is not { } n || !ParseColor("Colour", v[10], out uint c)) return;
        if (n[2] is < 1 or > 4096 || n[3] is < 0 or > 16 || n[5] is < 1 or > 360) { Ui.Error(new InvalidDataException("Radius 1–4096, intensity 0–16, cone 1–360°.")); return; }
        Snapshot();
        var t = _scene.Lights.First(x => x.Id == l.Id);
        (t.Name, t.Kind, t.X, t.Y, t.Radius, t.Intensity) = (v[0].Trim(), Enum.Parse<LightKind>(v[1]), (int)n[0], (int)n[1], (int)n[2], n[3]);
        (t.Direction, t.Cone, t.Glow, t.Flicker, t.Color, t.Shadows) = (n[4], n[5], Math.Clamp(n[6], 0, 1), Math.Clamp(n[7], 0, 1), c | 0xFF000000, v[11] == "Yes");
        AfterEffectsChange(t);
    }

    void EditEmitter(SceneEmitter em)
    {
        var v = Ui.AskFields(this, $"Particles “{em.Name}”", ("Name", em.Name, null),
            ("X", $"{em.X}", null), ("Y", $"{em.Y}", null), ("Area width (0 = point)", $"{em.W}", null), ("Area height", $"{em.H}", null),
            ("Particles per second (0–2000)", N(em.Rate), null), ("Life (ms)", $"{em.LifeMs}", null), ("Speed (px/s)", N(em.Speed), null),
            ("Direction (°, -90 = up)", N(em.Direction), null), ("Spread (°)", N(em.Spread), null), ("Gravity (px/s², negative rises)", N(em.Gravity), null),
            ("Start size", N(em.Size), null), ("End size", N(em.EndSize), null), ("Seed", $"{em.Seed}", null),
            ("Start colour", ColorUtil.ToHex(em.Color), null), ("End colour (alpha fades)", ColorUtil.ToHex(em.EndColor), null), ("Glows (additive)", em.Additive ? "Yes" : "No", YesNo));
        if (v is null || Numbers(["X", "Y", "Width", "Height", "Rate", "Life", "Speed", "Direction", "Spread", "Gravity", "Start size", "End size", "Seed"], v[1..14]) is not { } n
            || !ParseColor("Start colour", v[14], out uint c0) || !ParseColor("End colour", v[15], out uint c1)) return;
        if (n[4] is < 0 or > 2000 || n[5] is < 1 or > 60000 || n[2] < 0 || n[3] < 0) { Ui.Error(new InvalidDataException("Rate 0–2000 per second, life 1–60000 ms, area not negative.")); return; }
        Snapshot();
        var t = _scene.Emitters.First(x => x.Id == em.Id);
        (t.Name, t.X, t.Y, t.W, t.H, t.Rate, t.LifeMs) = (v[0].Trim(), (int)n[0], (int)n[1], (int)n[2], (int)n[3], n[4], (int)n[5]);
        (t.Speed, t.Direction, t.Spread, t.Gravity, t.Size, t.EndSize, t.Seed) = (n[6], n[7], n[8], n[9], n[10], n[11], (int)n[12]);
        (t.Color, t.EndColor, t.Additive) = (c0, c1, v[16] == "Yes");
        AfterEffectsChange(t);
    }

    void EditOccluder(SceneOccluder o)
    {
        string pts = string.Join(", ", o.Points.Chunk(2).Select(p => $"{p[0]} {p[1]}"));
        var v = Ui.AskFields(this, $"Occluder “{o.Name}”", ("Name", o.Name, null), ("Points (x y, x y, …)", pts, null));
        if (v is null) return;
        var nums = v[1].Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries).Select(s => int.TryParse(s, out int k) ? k : (int?)null).ToList();
        if (nums.Any(k => k is null) || nums.Count < 6 || nums.Count % 2 != 0) { Ui.Error(new FormatException("Give at least three points as whole numbers: x y, x y, x y.")); return; }
        Snapshot();
        var t = _scene.Occluders.First(x => x.Id == o.Id);
        (t.Name, t.Points) = (v[0].Trim(), [.. nums.Select(k => k!.Value)]);
        AfterEffectsChange(t);
    }

    /// <summary>Markers on the canvas: a light's reach, an emitter's area, an occluder's outline. Dragging moves them.</summary>
    void DrawEffects()
    {
        _litImage = null;
        if (LitPreviewBox.IsChecked == true && Assets is { } a && (long)_scene.Width * _scene.Height <= 4096L * 4096)
        {
            _litImage = new Image { Width = _scene.Width, Height = _scene.Height, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(_litImage, BitmapScalingMode.NearestNeighbor);
            Place(_litImage, 0, 0);
            UpdateLit(0);
        }
        double t = 1 / _zoom;
        var selected = Id((EffectsList.SelectedItem as ListBoxItem)?.Tag);
        Brush Stroke(Guid id, Brush b) => id == selected ? Brushes.Orange : b;
        foreach (var l in _scene.Lights)
        {
            var reach = new Ellipse { Width = 2 * l.Radius, Height = 2 * l.Radius, Stroke = Stroke(l.Id, Brushes.Gold), StrokeThickness = t, StrokeDashArray = [4, 4], IsHitTestVisible = false };
            Place(reach, l.X - l.Radius, l.Y - l.Radius);
            Marker(l, new Ellipse { Width = 10 * t, Height = 10 * t, Fill = Ui.BrushOf(l.Color | 0xFF000000), Stroke = Stroke(l.Id, Brushes.Black), StrokeThickness = t }, l.X, l.Y);
        }
        foreach (var em in _scene.Emitters)
        {
            if (em.W > 0 || em.H > 0)
                Place(new Rectangle { Width = Math.Max(1, em.W), Height = Math.Max(1, em.H), Stroke = Stroke(em.Id, Brushes.Plum), StrokeThickness = t, StrokeDashArray = [2, 3], IsHitTestVisible = false }, em.X, em.Y);
            Marker(em, new Rectangle { Width = 9 * t, Height = 9 * t, Fill = Brushes.Plum, Stroke = Stroke(em.Id, Brushes.Black), StrokeThickness = t, RenderTransform = new RotateTransform(45, 4.5 * t, 4.5 * t) }, em.X, em.Y);
        }
        foreach (var o in _scene.Occluders)
        {
            var poly = new Polygon { Points = [.. o.Points.Chunk(2).Select(p => new Point(p[0], p[1]))], Fill = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), Stroke = Stroke(o.Id, Brushes.SlateGray), StrokeThickness = t };
            Marker(o, poly, 0, 0, center: false);
        }
    }

    void UpdateLit(double timeMs)
    {
        if (_litImage is null || Assets is not { } a) return;
        _litImage.Source = ImageIO.ToBitmap(SceneRenderer.Render(_scene, a, 0, 0, _scene.Width, _scene.Height, timeMs), _scene.Width, _scene.Height);
    }

    void OnLitPreview(object sender, RoutedEventArgs e) => Rebuild();

    void OnEffectSelected(object sender, SelectionChangedEventArgs e) { if (!_refreshing) Rebuild(); }

    void Marker(object fx, Shape shape, int x, int y, bool center = true)
    {
        shape.Cursor = Cursors.SizeAll;
        shape.ToolTip = fx switch { SceneLight l => $"Light “{l.Name}”", SceneEmitter em => $"Particles “{em.Name}”", SceneOccluder o => $"Occluder “{o.Name}”", _ => null };
        Place(shape, center ? x - shape.Width / 2 : x, center ? y - shape.Height / 2 : y);
        Point? from = null;
        bool moved = false;
        shape.MouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left) return;
            e.Handled = true;
            EffectsList.SelectedItem = EffectsList.Items.Cast<ListBoxItem>().FirstOrDefault(i => Id(i.Tag) == Id(fx));
            if (e.ClickCount == 2) { OnEditEffect(this, e); return; }
            (from, moved) = (e.GetPosition(SceneCanvas), false);
            shape.CaptureMouse();
        };
        shape.MouseMove += (_, e) =>
        {
            if (from is not Point f || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(SceneCanvas);
            if (!moved) { Snapshot(); moved = true; }
            int dx = (int)Math.Round(p.X - f.X), dy = (int)Math.Round(p.Y - f.Y);
            if (dx == 0 && dy == 0) return;
            from = new Point(f.X + dx, f.Y + dy);
            switch (fx)
            {
                case SceneLight l: (l.X, l.Y) = (l.X + dx, l.Y + dy); break;
                case SceneEmitter em: (em.X, em.Y) = (em.X + dx, em.Y + dy); break;
                case SceneOccluder o: for (int i = 0; i < o.Points.Count; i++) o.Points[i] += i % 2 == 0 ? dx : dy; break;
            }
            Canvas.SetLeft(shape, Canvas.GetLeft(shape) + dx);
            Canvas.SetTop(shape, Canvas.GetTop(shape) + dy);
        };
        shape.MouseUp += (_, e) =>
        {
            if (from is null) return;
            from = null;
            shape.ReleaseMouseCapture();
            if (moved) Rebuild();
        };
    }
}
