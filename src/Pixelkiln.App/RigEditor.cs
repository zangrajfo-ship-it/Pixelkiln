using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Pixelkiln.Core;

namespace Pixelkiln.App;

/// <summary>
/// Character › Rig: cut the sprite's first frame into body parts (drag a rectangle; right-click sets the selected
/// part's pivot), hang parts from each other, pick the animation library and the clip each state plays, and watch it.
/// Edits the States mode's document, so both modes share one file, undo history and save.
/// </summary>
sealed class RigEditor : UserControl, IEditor
{
    readonly CharacterEditor _ch;
    readonly Image _sheet = new(), _preview = new();
    readonly Canvas _overlay = new();
    readonly ListBox _list = new() { Height = 160 };
    readonly ComboBox _lib = new(), _previewState = new();
    readonly StackPanel _states = new();
    readonly TextBlock _problems = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed, Margin = new Thickness(0, 6, 0, 0) };
    readonly CheckBox _play = new() { Content = "Play", Margin = new Thickness(0, 6, 0, 0) };
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    readonly DateTime _start = DateTime.Now;
    int _zoom = 8;
    Point? _dragFrom;
    Rectangle? _dragRect;
    RigPart? _sel;
    AnimLibrary? _anims;
    bool _refreshing;

    public RigEditor(CharacterEditor ch)
    {
        _ch = ch;
        RenderOptions.SetBitmapScalingMode(_sheet, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(_preview, BitmapScalingMode.NearestNeighbor);
        AutomationProperties.SetName(_list, "Body parts");
        AutomationProperties.SetName(_lib, "Animation library");
        AutomationProperties.SetName(_previewState, "Preview state");
        var sheetHost = new Grid { Background = Ui.CheckerBrush(), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Cursor = Cursors.Cross };
        sheetHost.Children.Add(_sheet);
        sheetHost.Children.Add(_overlay);
        sheetHost.MouseLeftButtonDown += OnDown;
        sheetHost.MouseMove += OnMove;
        sheetHost.MouseLeftButtonUp += OnUp;
        sheetHost.MouseRightButtonDown += OnPivot;
        var previewHost = new Border { Background = Ui.CheckerBrush(), Child = _preview, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };

        Button Btn(string text, Action a) { var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 2, 8, 2) }; b.Click += (_, _) => a(); return b; }
        TextBlock Head(string t) => new() { Text = t, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) };
        var side = new StackPanel { Margin = new Thickness(10) };
        side.Children.Add(Head("Body parts"));
        side.Children.Add(new TextBlock { Text = "Drag on the sprite to cut a part. Right-click sets the selected part's pivot.", TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
        side.Children.Add(_list);
        side.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0), Children = { Btn("Edit…", EditPart), Btn("Delete", DeletePart) } });
        side.Children.Add(Head("Animation library"));
        side.Children.Add(_lib);
        side.Children.Add(Head("Clip per state"));
        side.Children.Add(_states);
        side.Children.Add(Head("Preview"));
        side.Children.Add(_previewState);
        side.Children.Add(_play);
        side.Children.Add(_problems);
        var right = new ScrollViewer { Width = 300, Content = side, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DockPanel.SetDock(right, Dock.Right);
        var center = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10), Children = { sheetHost, previewHost } },
        };
        Content = new DockPanel { Children = { right, center } };

        _list.SelectionChanged += (_, _) => { if (!_refreshing) { _sel = (_list.SelectedItem as ListBoxItem)?.Tag as RigPart; DrawOverlay(); } };
        _list.MouseDoubleClick += (_, _) => EditPart();
        _lib.SelectionChanged += (_, _) =>
        {
            if (_refreshing) return;
            var id = (_lib.SelectedItem as ComboBoxItem)?.Tag as Guid?;
            if (id == Doc.RigAnimAssetId) return;
            _ch.Change(() => Doc.RigAnimAssetId = id);
        };
        _previewState.SelectionChanged += (_, _) => RenderPreview();
        _play.Checked += (_, _) => _timer.Start();
        _play.Unchecked += (_, _) => { _timer.Stop(); RenderPreview(); };
        _timer.Tick += (_, _) => RenderPreview();
        _ch.DocumentChanged += () => { if (IsVisible) Refresh(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) Refresh(); else _play.IsChecked = false; };
    }

    Character Doc => _ch.Doc;
    Sprite? Sprite => _ch.SpriteDoc;

    void Refresh()
    {
        _refreshing = true;
        _anims = null;
        if (Doc.RigAnimAssetId is Guid aid && App.Project?.Resolve(aid) is string path)
            try { _anims = AnimLibrary.Load(path); }
            catch (Exception ex) when (ex is InvalidDataException or IOException) { _problems.Text = ex.Message; }
        var libs = App.Project?.Assets.Where(a => a.Type == AssetType.Anim).OrderBy(a => a.Path).Select(a => new ComboBoxItem { Content = a.Path, Tag = a.Id }).ToList() ?? [];
        libs.Insert(0, new ComboBoxItem { Content = "(none)", Tag = null });
        _lib.ItemsSource = libs;
        _lib.SelectedItem = libs.FirstOrDefault(i => (Guid?)i.Tag == Doc.RigAnimAssetId) ?? libs[0];

        _list.ItemsSource = Doc.Parts.Select(p => new ListBoxItem
        {
            Content = $"{p.Name}  {p.W}×{p.H}{(p.Parent.Length > 0 ? $"  ← {p.Parent}" : "")}  z {p.Z}", Tag = p,
        }).ToList();
        _sel = Doc.Parts.FirstOrDefault(p => p.Name == _sel?.Name);
        _list.SelectedItem = _list.Items.Cast<ListBoxItem>().FirstOrDefault(i => i.Tag == _sel);

        var clipNames = new[] { "(rest)" }.Concat(_anims?.Clips.Select(c => c.Name) ?? []).ToArray();
        _states.Children.Clear();
        foreach (var state in Doc.Animations.Keys)
        {
            var box = new ComboBox { ItemsSource = clipNames, SelectedItem = Doc.RigClips.GetValueOrDefault(state) ?? "(rest)", Width = 150 };
            AutomationProperties.SetName(box, $"Clip for {state}");
            box.SelectionChanged += (_, _) =>
            {
                string? clip = box.SelectedItem as string is "(rest)" or null ? null : (string)box.SelectedItem;
                if (clip == Doc.RigClips.GetValueOrDefault(state)) return;
                _ch.Change(() => { if (clip is null) Doc.RigClips.Remove(state); else Doc.RigClips[state] = clip; });
            };
            _states.Children.Add(new DockPanel { Margin = new Thickness(0, 2, 0, 2), Children = { new TextBlock { Text = state, Width = 110, VerticalAlignment = VerticalAlignment.Center }, box } });
        }
        if (Doc.Animations.Count == 0) _states.Children.Add(new TextBlock { Text = "Add states in States mode first.", Opacity = 0.75 });
        string? keep = _previewState.SelectedItem as string;
        _previewState.ItemsSource = Doc.Animations.Keys.ToList();
        _previewState.SelectedItem = keep is not null && Doc.Animations.ContainsKey(keep) ? keep : Doc.Animations.Keys.FirstOrDefault();
        _refreshing = false;

        if (Sprite is { } s)
        {
            _zoom = Math.Clamp(320 / Math.Max(s.Width, s.Height), 1, 16);
            _sheet.Source = ImageIO.ToBitmap(Compositor.Flatten(s, 0), s.Width, s.Height);
            _sheet.Width = _overlay.Width = s.Width * _zoom;
            _sheet.Height = _overlay.Height = s.Height * _zoom;
        }
        else _sheet.Source = null;
        var problems = Sprite is { } sp ? Rig.Check(Doc.Parts, sp.Width, sp.Height) : ["Choose the character's sprite in States mode."];
        _problems.Text = string.Join("\n", problems);
        DrawOverlay();
        RenderPreview();
    }

    void DrawOverlay()
    {
        _overlay.Children.Clear();
        foreach (var p in Doc.Parts)
        {
            bool sel = p == _sel;
            var r = new Rectangle { Width = p.W * _zoom, Height = p.H * _zoom, Stroke = sel ? Brushes.Orange : Brushes.DeepSkyBlue, StrokeThickness = sel ? 2 : 1, IsHitTestVisible = false };
            Canvas.SetLeft(r, p.X * _zoom);
            Canvas.SetTop(r, p.Y * _zoom);
            _overlay.Children.Add(r);
            var pivot = new Ellipse { Width = 7, Height = 7, Fill = sel ? Brushes.Orange : Brushes.DeepSkyBlue, IsHitTestVisible = false };
            Canvas.SetLeft(pivot, (p.X + p.PivotX) * _zoom - 3.5);
            Canvas.SetTop(pivot, (p.Y + p.PivotY) * _zoom - 3.5);
            _overlay.Children.Add(pivot);
        }
    }

    void RenderPreview()
    {
        if (Sprite is not { } s || Doc.Parts.Count == 0) { _preview.Source = null; return; }
        int w = s.Width * 3, h = s.Height * 3;
        var o = new uint[w * h];
        var clip = _anims?.Clip(_previewState.SelectedItem is string st && Doc.RigClips.TryGetValue(st, out var n) ? n : "\0");
        double t = _play.IsChecked == true ? (DateTime.Now - _start).TotalMilliseconds : 0;
        try { Rig.Draw(o, w, h, Compositor.Flatten(s, 0), s.Width, s.Height, Doc.Parts, clip, t, s.Width + Doc.OriginX, s.Height + Doc.OriginY); }
        catch (InvalidDataException) { _preview.Source = null; return; }   // parent loop: listed under problems
        _preview.Source = ImageIO.ToBitmap(o, w, h);
        int z = Math.Max(1, _zoom / 2);
        (_preview.Width, _preview.Height) = (w * z, h * z);
    }

    (int X, int Y) Cell(MouseEventArgs e)
    {
        var p = e.GetPosition(_overlay);
        return ((int)Math.Floor(p.X / _zoom), (int)Math.Floor(p.Y / _zoom));
    }

    void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (Sprite is null) return;
        var (x, y) = Cell(e);
        // a click inside a part selects it; a drag cuts a new one
        _dragFrom = new Point(x, y);
        ((UIElement)sender).CaptureMouse();
        var hit = Doc.Parts.LastOrDefault(p => x >= p.X && y >= p.Y && x < p.X + p.W && y < p.Y + p.H);
        if (hit is not null) { _sel = hit; _list.SelectedItem = _list.Items.Cast<ListBoxItem>().FirstOrDefault(i => i.Tag == hit); DrawOverlay(); }
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        if (_dragFrom is not { } a || e.LeftButton != MouseButtonState.Pressed) return;
        var (x, y) = Cell(e);
        _dragRect ??= new Rectangle { Stroke = Brushes.Orange, StrokeDashArray = [3, 2], IsHitTestVisible = false };
        if (!_overlay.Children.Contains(_dragRect)) _overlay.Children.Add(_dragRect);
        Canvas.SetLeft(_dragRect, Math.Min(a.X, x) * _zoom);
        Canvas.SetTop(_dragRect, Math.Min(a.Y, y) * _zoom);
        _dragRect.Width = (Math.Abs(x - a.X) + 1) * _zoom;
        _dragRect.Height = (Math.Abs(y - a.Y) + 1) * _zoom;
    }

    void OnUp(object sender, MouseButtonEventArgs e)
    {
        ((UIElement)sender).ReleaseMouseCapture();
        if (_dragFrom is not { } a || Sprite is not { } s) return;
        _dragFrom = null;
        if (_dragRect is not null) _overlay.Children.Remove(_dragRect);
        var (x, y) = Cell(e);
        if (x == (int)a.X && y == (int)a.Y) return;   // a click, not a drag
        int x0 = Math.Clamp((int)Math.Min(a.X, x), 0, s.Width - 1), y0 = Math.Clamp((int)Math.Min(a.Y, y), 0, s.Height - 1);
        int x1 = Math.Clamp((int)Math.Max(a.X, x), 0, s.Width - 1), y1 = Math.Clamp((int)Math.Max(a.Y, y), 0, s.Height - 1);
        string name = Enumerable.Range(1, 999).Select(i => $"part{i}").First(n => Doc.Parts.All(p => p.Name != n));
        var part = new RigPart
        {
            Name = name, X = x0, Y = y0, W = x1 - x0 + 1, H = y1 - y0 + 1, PivotX = (x1 - x0 + 1) / 2, PivotY = (y1 - y0 + 1) / 2, Z = Doc.Parts.Count,
        };
        // placed where it was cut: its pivot relative to the character origin
        (part.OffsetX, part.OffsetY) = (x0 + part.PivotX - Doc.OriginX, y0 + part.PivotY - Doc.OriginY);
        _sel = part;
        _ch.Change(() => Doc.Parts.Add(part));
    }

    void OnPivot(object sender, MouseButtonEventArgs e)
    {
        if (_sel is not { } p) return;
        var (x, y) = Cell(e);
        int px = Math.Clamp(x - p.X, 0, p.W), py = Math.Clamp(y - p.Y, 0, p.H);
        _ch.Change(() =>
        {
            var part = Doc.Parts.First(q => q.Name == p.Name);
            // keep the part where it is: moving the pivot moves the offset by the same amount
            (part.OffsetX, part.OffsetY) = (part.OffsetX + px - part.PivotX, part.OffsetY + py - part.PivotY);
            (part.PivotX, part.PivotY) = (px, py);
        });
    }

    void EditPart()
    {
        if (_sel is not { } p) return;
        string[] parents = ["(origin)", .. Doc.Parts.Where(q => q != p).Select(q => q.Name)];
        var v = Ui.AskFields(this, $"Body part “{p.Name}”",
            ("Name", p.Name, null), ("X", $"{p.X}", null), ("Y", $"{p.Y}", null), ("Width", $"{p.W}", null), ("Height", $"{p.H}", null),
            ("Pivot X", $"{p.PivotX}", null), ("Pivot Y", $"{p.PivotY}", null), ("Hangs from", p.Parent.Length > 0 ? p.Parent : "(origin)", parents),
            ("Offset X", $"{p.OffsetX}", null), ("Offset Y", $"{p.OffsetY}", null), ("Z order", $"{p.Z}", null));
        if (v is null) return;
        string name = v[0].Trim();
        if (name.Length == 0 || Doc.Parts.Any(q => q != p && q.Name == name)) { Ui.Error(new InvalidDataException($"Body part names must be unique and not empty (“{name}”).")); return; }
        if (!v[1..7].All(s => int.TryParse(s, out _)) || !double.TryParse(v[8], out double ox) || !double.TryParse(v[9], out double oy) || !int.TryParse(v[10], out int z))
        { Ui.Error(new FormatException("Positions, sizes and Z must be numbers.")); return; }
        int[] n = [.. v[1..7].Select(int.Parse)];
        if (n[2] < 1 || n[3] < 1) { Ui.Error(new InvalidDataException("A body part needs a width and height of at least 1.")); return; }
        string old = p.Name;
        _ch.Change(() =>
        {
            var part = Doc.Parts.First(q => q.Name == old);
            (part.Name, part.X, part.Y, part.W, part.H, part.PivotX, part.PivotY) = (name, n[0], n[1], n[2], n[3], n[4], n[5]);
            (part.Parent, part.OffsetX, part.OffsetY, part.Z) = (v[7] == "(origin)" ? "" : v[7], ox, oy, z);
            foreach (var child in Doc.Parts.Where(q => q.Parent == old)) child.Parent = name;   // children follow a rename
            _sel = part;
        });
    }

    void DeletePart()
    {
        if (_sel is not { } p) return;
        _ch.Change(() =>
        {
            Doc.Parts.RemoveAll(q => q.Name == p.Name);
            foreach (var child in Doc.Parts.Where(q => q.Parent == p.Name)) child.Parent = p.Parent;   // children move up
        });
        _sel = null;
    }

    // ---------- IEditor: the States mode's document ----------

    IReadOnlyList<MenuItem>? _menus;
    public IReadOnlyList<MenuItem> Menus => _menus ??=
    [
        .. _ch.Menus.Take(2),   // File and Edit of the shared document (only one mode's menus are shown at a time)
        Ui.Menu("_Rig",
            Ui.Item("_Edit selected part…", "Enter", EditPart),
            Ui.Item("_Delete selected part", "Del", DeletePart),
            Ui.Item("_Play / stop", "Space", () => _play.IsChecked = _play.IsChecked != true)),
    ];
    public string[] FileExtensions => [];
    public bool IsDirty => _ch.IsDirty;
    public string? CurrentPath => _ch.CurrentPath;
    public bool ConfirmDiscard() => _ch.ConfirmDiscard();
    public void OpenPath(string path, bool confirmed = false) => _ch.OpenPath(path, confirmed);
    public void OnFileMoved(string oldPath, string newPath) { }   // the States mode keeps the path
    public void WriteAutosave(string file) => _ch.WriteAutosave(file);
    public void RestoreAutosave(string file, string? originalPath) => _ch.RestoreAutosave(file, originalPath);

    public void HandleKey(KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        switch (e.Key)
        {
            case Key.Delete: DeletePart(); e.Handled = true; break;
            case Key.Enter: EditPart(); e.Handled = true; break;
            case Key.Space: _play.IsChecked = _play.IsChecked != true; e.Handled = true; break;
            default: _ch.HandleKey(e); break;   // save, undo, redo …
        }
    }
}
