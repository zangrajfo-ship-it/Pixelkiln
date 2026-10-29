using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using Pixelkiln.Core;

namespace Pixelkiln.App;

/// <summary>
/// UI mode (Sprite tab): a kit of buttons, panels, dialogs, sliders, bars, slots, icons and labels in four states,
/// drawn from one style or from project pictures, and screens laid out with anchors and previewed at other resolutions.
/// </summary>
public partial class UiEditor : UserControl, IEditor, IPictureSource
{
    enum ViewKind { Style, Element, Screen }
    static readonly (string Name, int W, int H)[] Devices =
        [("Design size", 0, 0), ("320 × 180", 320, 180), ("480 × 270", 480, 270), ("640 × 360", 640, 360), ("1280 × 720", 1280, 720),
         ("1920 × 1080", 1920, 1080), ("1080 × 1920 portrait", 1080, 1920), ("2400 × 1080 wide phone", 2400, 1080)];

    UiKit _kit = UiKit.Starter(UiKit.Styles["Fantasy"], ["Main menu", "HUD"], 320, 180);
    readonly History<UiKit> _history = new(k => k.Clone(), App.Settings.UndoLimit);
    string? _path;
    bool _dirty, _refreshing = true, _dragSnapshot;
    int _zoom = 3;
    ViewKind _view = ViewKind.Style;
    Guid? _element, _placement;
    string? _screen;
    Point? _dragFrom;
    (int X, int Y) _dragOrig;

    public UiEditor()
    {
        InitializeComponent();
        NewKindBox.ItemsSource = Enum.GetNames<UiElementKind>();
        NewKindBox.SelectedIndex = 0;
        ElKindBox.ItemsSource = Enum.GetNames<UiElementKind>();
        PlAnchorBox.ItemsSource = Enum.GetNames<UiAnchor>();
        PlStateBox.ItemsSource = Enum.GetNames<UiState>();
        DeviceBox.ItemsSource = Devices.Select(d => d.Name).ToList();
        DeviceBox.SelectedIndex = 0;
        Picker.ColorChanged += OnStyleColor;
        foreach (var s in Enum.GetValues<UiState>())
        {
            ArtGrid.RowDefinitions.Add(new RowDefinition());
            var label = new TextBlock { Text = s.ToString(), VerticalAlignment = VerticalAlignment.Center };
            var box = new ComboBox { Tag = s, Margin = new Thickness(0, 2, 0, 2) };
            System.Windows.Automation.AutomationProperties.SetName(box, $"{s} art");
            box.SelectionChanged += OnElementCommit;
            Grid.SetRow(label, (int)s);
            Grid.SetRow(box, (int)s);
            Grid.SetColumn(box, 1);
            ArtGrid.Children.Add(label);
            ArtGrid.Children.Add(box);
        }
        IsVisibleChanged += (_, _) => { UpdateTitle(); if (IsVisible && IsLoaded) Redraw(); };
        App.ProjectChanged += () => { if (IsLoaded) { RefreshProps(); Redraw(); } };
        Loaded += Init;
        void Init(object s, RoutedEventArgs e) { Loaded -= Init; LoadKit(_kit, null); }
    }

    UiElement? Element => _element is Guid id ? _kit.Element(id) : null;
    UiScreen? Screen => _kit.Screens.FirstOrDefault(s => s.Name == _screen);
    UiPlacement? Placement => Screen?.Items.FirstOrDefault(i => i.Id == _placement);

    // ---------- document ----------

    void RefreshAll()
    {
        RefreshLists();
        RefreshProps();
        Redraw();
    }

    void LoadKit(UiKit k, string? path)
    {
        (_kit, _path, _dirty) = (k, path, false);
        _history.Clear();
        (_element, _placement) = (null, null);
        _screen = k.Screens.FirstOrDefault()?.Name;
        _view = _screen is null ? ViewKind.Style : ViewKind.Screen;
        RefreshAll();
        UpdateTitle();
    }

    void Snapshot()
    {
        _history.Push(_kit);
        _dirty = true;
        UpdateTitle();
    }

    void UpdateTitle() => Ui.ShowTitle(this, _path, _dirty, "untitled UI kit");

    void RefreshLists()
    {
        _refreshing = true;
        ElementsList.ItemsSource = _kit.Elements.Select(e => new ListBoxItem { Content = $"{e.Name} · {e.Kind} {e.W}×{e.H}", Tag = e.Id }).ToList();
        ElementsList.SelectedItem = ElementsList.Items.Cast<ListBoxItem>().FirstOrDefault(i => (Guid)i.Tag == _element && _view == ViewKind.Element);
        ScreensList.ItemsSource = _kit.Screens.Select(s => new ListBoxItem { Content = $"{s.Name} · {s.Width}×{s.Height}{(s.Background >> 24 == 0 ? " · overlay" : "")}", Tag = s.Name }).ToList();
        ScreensList.SelectedItem = ScreensList.Items.Cast<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == _screen && _view == ViewKind.Screen);
        _refreshing = false;
    }

    List<ComboBoxItem> Pictures(string none) =>
        [new ComboBoxItem { Content = none, Tag = null }, .. (App.Project?.Assets ?? []).Where(a => AssetLoader.IsPicture(a.Type)).OrderBy(a => a.Path).Select(a => new ComboBoxItem { Content = a.Path, Tag = a.Id })];

    void RefreshProps()
    {
        _refreshing = true;
        StyleProps.Visibility = _view == ViewKind.Style ? Visibility.Visible : Visibility.Collapsed;
        ElementProps.Visibility = _view == ViewKind.Element && Element is not null ? Visibility.Visible : Visibility.Collapsed;
        PlacementProps.Visibility = _view == ViewKind.Screen && Placement is not null ? Visibility.Visible : Visibility.Collapsed;
        ScreenProps.Visibility = _view == ViewKind.Screen && Placement is null && Screen is not null ? Visibility.Visible : Visibility.Collapsed;
        var st = _kit.Style;
        switch (_view)
        {
            case ViewKind.Style:
                PropsTitle.Text = "Style (every element drawn from it changes)";
                Picker.Color = StyleColor();
                (RadiusBox.Text, BorderBox.Text, FontBox.Text, PixelBox.IsChecked) = (st.Radius.ToString(), st.Border.ToString(), st.FontSize.ToString(), st.Pixel);
                break;
            case ViewKind.Element when Element is { } e:
                PropsTitle.Text = $"Element: {e.Name}";
                (ElNameBox.Text, ElKindBox.SelectedItem, ElWBox.Text, ElHBox.Text, ElMarginBox.Text, ElLabelBox.Text) = (e.Name, e.Kind.ToString(), e.W.ToString(), e.H.ToString(), e.Margin.ToString(), e.Label);
                ElIconBox.ItemsSource = Pictures("(none)");
                ElIconBox.SelectedItem = ElIconBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (i.Tag as Guid?) == e.IconAssetId) ?? ElIconBox.Items[0];
                foreach (var box in ArtGrid.Children.OfType<ComboBox>())
                {
                    box.ItemsSource = Pictures("(drawn from style)");
                    Guid? art = e.Art.TryGetValue((UiState)box.Tag, out var a) ? a : null;
                    box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (i.Tag as Guid?) == art) ?? box.Items[0];
                }
                break;
            case ViewKind.Screen when Placement is { } p:
                PropsTitle.Text = $"On “{_screen}”: {_kit.Element(p.ElementId)?.Name}";
                (PlXBox.Text, PlYBox.Text, PlWBox.Text, PlHBox.Text, PlAnchorBox.SelectedItem, PlTextBox.Text, PlStateBox.SelectedItem, PlValueBox.Text) =
                    (p.X.ToString(), p.Y.ToString(), p.W.ToString(), p.H.ToString(), p.Anchor.ToString(), p.Text ?? "", p.State.ToString(), p.Value.ToString("0.##", CultureInfo.InvariantCulture));
                break;
            case ViewKind.Screen when Screen is { } s:
                PropsTitle.Text = $"Screen: {s.Name}";
                (ScNameBox.Text, ScWBox.Text, ScHBox.Text, ScOverlayBox.IsChecked) = (s.Name, s.Width.ToString(), s.Height.ToString(), s.Background >> 24 == 0);
                break;
            default:
                PropsTitle.Text = "";
                break;
        }
        _refreshing = false;
    }

    // ---------- view ----------

    (int W, int H) PreviewSize(UiScreen s) => Devices[Math.Max(0, DeviceBox.SelectedIndex)] is var (_, w, h) && w > 0 ? (w, h) : (s.Width, s.Height);

    void Redraw()
    {
        uint[] px;
        int w, h;
        var p = App.Project;
        try
        {
            switch (_view)
            {
                case ViewKind.Screen when Screen is { } s:
                    (w, h) = PreviewSize(s);
                    px = UiRender.Screen(_kit, s, w, h, p);
                    break;
                case ViewKind.Element when Element is { } e:
                    // the four states side by side, then the normal state stretched to show the nine-slice
                    var states = Enum.GetValues<UiState>().Select(st => UiRender.Native(_kit, e, st, p)).ToList();
                    int sw = e.W * 2 + 8, sh = e.H * 2;
                    (w, h) = (e.W * 4 + 4 * 6 + sw, Math.Max(e.H, sh));
                    px = new uint[w * h];
                    for (int i = 0; i < 4; i++) SceneRenderer.Blit(px, w, h, states[i], e.W, e.H, i * (e.W + 6), 0, false, 1);
                    SceneRenderer.Blit(px, w, h, UiRender.Sized(_kit, e, UiState.Normal, sw, sh, null, 0.6, p), sw, sh, 4 * (e.W + 6), 0, false, 1);
                    break;
                default:
                    // gallery of every element in its normal state
                    w = Math.Max(16, _kit.Elements.Sum(e => e.W + 6));
                    h = Math.Max(16, _kit.Elements.Count == 0 ? 16 : _kit.Elements.Max(e => e.H));
                    px = new uint[w * h];
                    int x = 0;
                    foreach (var e in _kit.Elements)
                    {
                        SceneRenderer.Blit(px, w, h, UiRender.Native(_kit, e, UiState.Normal, p), e.W, e.H, x, 0, false, 1);
                        x += e.W + 6;
                    }
                    break;
            }
        }
        catch (InvalidDataException ex) { Status.Text = ex.Message; return; }
        View.Source = ImageIO.ToBitmap(px, w, h);
        View.Width = Checker.Width = w;
        View.Height = Checker.Height = h;
        CanvasHost.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        Checker.Fill = Ui.CheckerBrush(_zoom);
        ZoomText.Text = $"{_zoom * 100} %";
        DrawSelection();
        Status.Text = _view switch
        {
            ViewKind.Screen when Screen is { } s => $"{s.Name}: design {s.Width}×{s.Height}, shown at {w}×{h}" + (PreviewSize(s) != (s.Width, s.Height) ? " (preview: drag only at design size)" : ""),
            ViewKind.Element => "Normal · Hover · Pressed · Disabled · stretched (nine-slice)",
            _ => $"{_kit.Elements.Count} elements, {_kit.Screens.Count} screens",
        };
    }

    void DrawSelection()
    {
        Overlay.Children.Clear();
        if (Placement is not { } pl || Screen is not { } s) return;
        var (pw, ph) = PreviewSize(s);
        var (x, y, w, h) = UiRender.Resolve(pl, s.Width, s.Height, pw, ph);
        var r = new Rectangle { Width = w + 2, Height = h + 2, Stroke = Brushes.DeepSkyBlue, StrokeThickness = 1.0 / _zoom * 2, StrokeDashArray = [3, 2] };
        Canvas.SetLeft(r, x - 1);
        Canvas.SetTop(r, y - 1);
        Overlay.Children.Add(r);
    }

    void OnDeviceChanged(object sender, SelectionChangedEventArgs e) { if (!_refreshing && IsLoaded) Redraw(); }
    void OnZoomIn(object sender, RoutedEventArgs e) { _zoom = Math.Min(12, _zoom + 1); Redraw(); }
    void OnZoomOut(object sender, RoutedEventArgs e) { _zoom = Math.Max(1, _zoom - 1); Redraw(); }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        e.Handled = true;
        _zoom = Math.Clamp(_zoom + Math.Sign(e.Delta), 1, 12);
        Redraw();
    }

    // ---------- selection ----------

    void OnElementSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || ElementsList.SelectedItem is not ListBoxItem { Tag: Guid id }) return;
        (_view, _element, _placement) = (ViewKind.Element, id, null);
        _refreshing = true;
        ScreensList.SelectedItem = null;
        _refreshing = false;
        RefreshProps();
        Redraw();
    }

    void OnScreenSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || ScreensList.SelectedItem is not ListBoxItem { Tag: string name }) return;
        (_view, _screen, _placement) = (ViewKind.Screen, name, null);
        _refreshing = true;
        ElementsList.SelectedItem = null;
        _refreshing = false;
        RefreshProps();
        Redraw();
    }

    void OnShowStyle(object sender, RoutedEventArgs e)
    {
        (_view, _placement) = (ViewKind.Style, null);
        RefreshAll();
    }

    // ---------- mouse on a screen ----------

    void OnCanvasDown(object sender, MouseButtonEventArgs e)
    {
        Keyboard.Focus(this);
        if (_view != ViewKind.Screen || Screen is not { } s) return;
        var p = e.GetPosition(View);
        var (pw, ph) = PreviewSize(s);
        var hit = Enumerable.Reverse(s.Items).FirstOrDefault(i => UiRender.Resolve(i, s.Width, s.Height, pw, ph) is var (x, y, w, h) && p.X >= x && p.Y >= y && p.X < x + w && p.Y < y + h);
        _placement = hit?.Id;
        if (hit is not null && (pw, ph) == (s.Width, s.Height))
        {
            _dragFrom = p;
            _dragOrig = (hit.X, hit.Y);
            _dragSnapshot = false;
            CanvasHost.CaptureMouse();
        }
        RefreshProps();
        DrawSelection();
    }

    void OnCanvasMove(object sender, MouseEventArgs e)
    {
        if (_dragFrom is not Point from || Placement is not { } pl) return;
        var p = e.GetPosition(View);
        int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 8 : 1;
        int nx = _dragOrig.X + (int)Math.Round((p.X - from.X) / step) * step, ny = _dragOrig.Y + (int)Math.Round((p.Y - from.Y) / step) * step;
        if ((nx, ny) == (pl.X, pl.Y)) return;
        if (!_dragSnapshot) { Snapshot(); _dragSnapshot = true; }
        (pl.X, pl.Y) = (nx, ny);
        Redraw();
    }

    void OnCanvasUp(object sender, MouseButtonEventArgs e)
    {
        _dragFrom = null;
        CanvasHost.ReleaseMouseCapture();
        if (_dragSnapshot) RefreshProps();
    }

    // ---------- edits ----------

    uint StyleColor()
    {
        var st = _kit.Style;
        return ColAccent.IsChecked == true ? st.Accent : ColDark.IsChecked == true ? st.Dark : ColLight.IsChecked == true ? st.Light : ColText.IsChecked == true ? st.Text : st.Base;
    }

    void OnStyleColorTarget(object sender, RoutedEventArgs e)
    {
        _refreshing = true;
        Picker.Color = StyleColor();
        _refreshing = false;
    }

    bool _colorSnapshot;

    void OnStyleColor(uint c)
    {
        if (_refreshing || _view != ViewKind.Style) return;
        if (!_colorSnapshot) { Snapshot(); _colorSnapshot = true; }   // one undo step per colour session
        var st = _kit.Style;
        c |= 0xFF000000;
        _kit.Style = ColAccent.IsChecked == true ? st with { Accent = c } : ColDark.IsChecked == true ? st with { Dark = c }
            : ColLight.IsChecked == true ? st with { Light = c } : ColText.IsChecked == true ? st with { Text = c } : st with { Base = c };
        Redraw();
    }

    static bool Int(TextBox b, int min, int max, out int v) => int.TryParse(b.Text.Trim(), out v) && v >= min && v <= max;

    void OnStyleCommit(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        _colorSnapshot = false;
        bool ok = Int(RadiusBox, 0, 64, out int r) & Int(BorderBox, 0, 16, out int b) & Int(FontBox, 4, 64, out int f);
        Ui.MarkInvalid(RadiusBox, ok ? null : "Radius 0–64, border 0–16, font 4–64.");
        if (!ok) return;
        var next = _kit.Style with { Radius = r, Border = b, FontSize = f, Pixel = PixelBox.IsChecked == true };
        if (next == _kit.Style) return;
        Snapshot();
        _kit.Style = next;
        Redraw();
    }

    void OnElementCommit(object sender, RoutedEventArgs e)
    {
        if (_refreshing || Element is not { } el) return;
        bool ok = Int(ElWBox, 1, 4096, out int w) & Int(ElHBox, 1, 4096, out int h) & Int(ElMarginBox, 0, 2048, out int m) && m * 2 <= Math.Min(w, h);
        Ui.MarkInvalid(ElWBox, ok ? null : "Sizes 1–4096; the margin at most half the smaller side.");
        if (!ok) return;
        var art = ArtGrid.Children.OfType<ComboBox>().Where(b => b.SelectedItem is ComboBoxItem { Tag: Guid }).ToDictionary(b => (UiState)b.Tag, b => (Guid)((ComboBoxItem)b.SelectedItem).Tag);
        Guid? icon = (ElIconBox.SelectedItem as ComboBoxItem)?.Tag as Guid?;
        var kind = Enum.Parse<UiElementKind>((string)ElKindBox.SelectedItem);
        string name = ElNameBox.Text.Trim().Length > 0 ? ElNameBox.Text.Trim() : el.Name;
        bool same = (name, kind, w, h, m, ElLabelBox.Text, icon) == (el.Name, el.Kind, el.W, el.H, el.Margin, el.Label, el.IconAssetId) && art.Count == el.Art.Count && art.All(kv => el.Art.GetValueOrDefault(kv.Key) == kv.Value);
        if (same) return;
        Snapshot();
        (el.Name, el.Kind, el.W, el.H, el.Margin, el.Label, el.IconAssetId, el.Art) = (name, kind, w, h, m, ElLabelBox.Text, icon, art);
        RefreshLists();
        Redraw();
    }

    void OnPlacementCommit(object sender, RoutedEventArgs e)
    {
        if (_refreshing || Placement is not { } pl) return;
        bool ok = Int(PlXBox, -8192, 8192, out int x) & Int(PlYBox, -8192, 8192, out int y) & Int(PlWBox, 1, 8192, out int w) & Int(PlHBox, 1, 8192, out int h)
                  & double.TryParse(PlValueBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v);
        Ui.MarkInvalid(PlXBox, ok ? null : "Whole pixels; value 0–1.");
        if (!ok) return;
        var anchor = Enum.Parse<UiAnchor>((string)PlAnchorBox.SelectedItem);
        var state = Enum.Parse<UiState>((string)PlStateBox.SelectedItem);
        string? text = PlTextBox.Text.Length == 0 ? null : PlTextBox.Text;
        v = Math.Clamp(v, 0, 1);
        if ((x, y, w, h, anchor, state, text, v) == (pl.X, pl.Y, pl.W, pl.H, pl.Anchor, pl.State, pl.Text, pl.Value)) return;
        Snapshot();
        (pl.X, pl.Y, pl.W, pl.H, pl.Anchor, pl.State, pl.Text, pl.Value) = (x, y, w, h, anchor, state, text, v);
        Redraw();
    }

    void OnScreenCommit(object sender, RoutedEventArgs e)
    {
        if (_refreshing || Screen is not { } s) return;
        bool ok = Int(ScWBox, 16, 8192, out int w) & Int(ScHBox, 16, 8192, out int h);
        string name = ScNameBox.Text.Trim();
        ok &= name.Length > 0 && (name == s.Name || _kit.Screens.All(x => !string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)));
        Ui.MarkInvalid(ScNameBox, ok ? null : "A unique name and a size of 16–8192.");
        if (!ok) return;
        uint bg = ScOverlayBox.IsChecked == true ? 0 : s.Background >> 24 == 0 ? _kit.Style.Dark : s.Background;
        if ((name, w, h, bg) == (s.Name, s.Width, s.Height, s.Background)) return;
        Snapshot();
        (s.Name, s.Width, s.Height, s.Background) = (name, w, h, bg);
        _screen = name;
        RefreshLists();
        Redraw();
    }

    void OnAddElement(object sender, RoutedEventArgs e)
    {
        var kind = Enum.Parse<UiElementKind>((string)NewKindBox.SelectedItem);
        Snapshot();
        var (w, h, m) = kind switch
        {
            UiElementKind.Button => (64, 16, 4), UiElementKind.Panel or UiElementKind.Dialog => (48, 40, 8), UiElementKind.Slider => (64, 10, 4),
            UiElementKind.HealthBar => (64, 8, 2), UiElementKind.InventorySlot => (20, 20, 4), UiElementKind.Icon => (16, 16, 0), _ => (64, 12, 0),
        };
        var el = new UiElement { Name = $"{kind} {_kit.Elements.Count(x => x.Kind == kind) + 1}", Kind = kind, W = w, H = h, Margin = m, Label = kind is UiElementKind.Button or UiElementKind.Label ? kind.ToString() : "" };
        _kit.Elements.Add(el);
        (_view, _element) = (ViewKind.Element, el.Id);
        RefreshAll();
    }

    void OnDuplicateElement(object sender, RoutedEventArgs e)
    {
        if (Element is not { } el) return;
        Snapshot();
        var copy = el.Clone();
        copy.Id = Guid.NewGuid();
        copy.Name = el.Name + " copy";
        _kit.Elements.Add(copy);
        _element = copy.Id;
        RefreshAll();
    }

    void OnDeleteElement(object sender, RoutedEventArgs e)
    {
        if (Element is not { } el) return;
        int uses = _kit.Screens.Sum(s => s.Items.Count(i => i.ElementId == el.Id));
        if (uses > 0 && MessageBox.Show(Window.GetWindow(this), $"“{el.Name}” is placed {uses} time(s) on screens. Delete it and those placements?", "Delete element",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        Snapshot();
        _kit.Elements.Remove(el);
        foreach (var s in _kit.Screens) s.Items.RemoveAll(i => i.ElementId == el.Id);
        (_element, _view) = (null, ViewKind.Style);
        RefreshAll();
    }

    void OnAddScreen(object sender, RoutedEventArgs e)
    {
        var free = UiKit.ScreenNames.Where(n => _kit.Screens.All(s => s.Name != n)).ToList();
        if (Ui.AskFields(this, "Add screen", ("_Start from", free.FirstOrDefault() ?? "Empty", [.. free, "Empty"]), ("_Name", free.FirstOrDefault() ?? "Screen", null)) is not { } f) return;
        string name = f[1].Trim();
        if (name.Length == 0 || _kit.Screens.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))) { Ui.Error(new InvalidOperationException("Use a new, non-empty screen name.")); return; }
        var (w, h) = App.Project is { } p ? (p.Settings.TargetWidth, p.Settings.TargetHeight) : (320, 180);
        Snapshot();
        UiScreen screen;
        if (f[0] == "Empty") screen = new UiScreen { Name = name, Width = w, Height = h, Background = _kit.Style.Dark };
        else
        {
            // lay the screen out with a starter kit of the same style, then map its elements onto this kit's (same kinds)
            var starter = UiKit.Starter(_kit.Style, [f[0]], w, h);
            screen = starter.Screens[0];
            screen.Name = name;
            foreach (var i in screen.Items)
            {
                var kind = starter.Element(i.ElementId)!.Kind;
                var mine = _kit.Elements.FirstOrDefault(x => x.Kind == kind);
                if (mine is null) { mine = starter.Element(i.ElementId)!.Clone(); _kit.Elements.Add(mine); }
                i.ElementId = mine.Id;
            }
        }
        _kit.Screens.Add(screen);
        (_view, _screen, _placement) = (ViewKind.Screen, name, null);
        RefreshAll();
    }

    void OnDeleteScreen(object sender, RoutedEventArgs e)
    {
        if (Screen is not { } s) return;
        Snapshot();
        _kit.Screens.Remove(s);
        _screen = _kit.Screens.FirstOrDefault()?.Name;
        _placement = null;
        _view = _screen is null ? ViewKind.Style : ViewKind.Screen;
        RefreshAll();
    }

    void OnPlace(object? sender, RoutedEventArgs? e)
    {
        var target = Screen ?? _kit.Screens.FirstOrDefault();
        var el = Element ?? _kit.Elements.FirstOrDefault();
        if (target is null || el is null) { Status.Text = "Add a screen and an element first."; return; }
        Snapshot();
        var pl = new UiPlacement { ElementId = el.Id, X = (target.Width - el.W) / 2, Y = (target.Height - el.H) / 2, W = el.W, H = el.H, Anchor = UiAnchor.Center };
        target.Items.Add(pl);
        (_view, _screen, _placement) = (ViewKind.Screen, target.Name, pl.Id);
        RefreshAll();
    }

    void OnRemovePlacement(object? sender, RoutedEventArgs? e)
    {
        if (Screen is not { } s || Placement is not { } pl) return;
        Snapshot();
        s.Items.Remove(pl);
        _placement = null;
        RefreshProps();
        Redraw();
    }

    void Nudge(int dx, int dy)
    {
        if (Placement is not { } pl) return;
        Snapshot();
        (pl.X, pl.Y) = (pl.X + dx, pl.Y + dy);
        RefreshProps();
        Redraw();
    }

    // ---------- undo ----------

    void OnUndo(object? sender, RoutedEventArgs? e) => Restore(_history.Undo(_kit));
    void OnRedo(object? sender, RoutedEventArgs? e) => Restore(_history.Redo(_kit));

    void Restore(UiKit? k)
    {
        if (k is null || _dragFrom is not null) return;
        _kit = k;
        _dirty = true;
        if (Element is null) _element = null;
        if (Screen is null) _screen = _kit.Screens.FirstOrDefault()?.Name;
        if (Placement is null) _placement = null;
        RefreshAll();
        UpdateTitle();
    }

    // ---------- files ----------

    const string Filter = "Pixelkiln UI kit (*.pxui)|*.pxui";

    void OnNew(object? sender, RoutedEventArgs? e)
    {
        if (!ConfirmDiscard()) return;
        if (Ui.AskFields(this, "New UI kit", ("_Style", "Fantasy", [.. UiKit.Styles.Keys]), ("_Screens", "Main menu, HUD", null)) is not { } f) return;
        var names = f[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Where(UiKit.ScreenNames.Contains).ToList();
        var (w, h) = App.Project is { } p ? (p.Settings.TargetWidth, p.Settings.TargetHeight) : (320, 180);
        LoadKit(UiKit.Starter(UiKit.Styles[f[0]], names, w, h), null);
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
            var dlg = new SaveFileDialog { Filter = Filter, FileName = "ui.pxui", InitialDirectory = App.Project?.Root ?? "" };
            if (dlg.ShowDialog() != true) return false;
            path = dlg.FileName;
        }
        try
        {
            UiKit.Save(_kit, path);
            (_path, _dirty) = (path, false);
            App.AddRecent(path);
            UpdateTitle();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); return false; }
    }

    void OnExportPngs(object? sender, RoutedEventArgs? e)
    {
        var dlg = new OpenFolderDialog { Title = "Folder for the element PNGs" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            int n = 0;
            foreach (var (rel, data) in UiExport.Files(_kit, "ui", App.Project, null).Where(kv => kv.Key.EndsWith(".png")))
            {
                string f = System.IO.Path.Combine(dlg.FolderName, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f)!);
                File.WriteAllBytes(f, data);
                n++;
            }
            Status.Text = $"Wrote {n} PNG(s), including the atlas.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); }
    }

    // ---------- IEditor ----------

    public string[] FileExtensions => [".pxui"];
    public bool IsDirty => _dirty;
    public string? CurrentPath => _path;

    public bool ConfirmDiscard() => Ui.ConfirmDiscard(_dirty, "UI kit", () => Save(_path));

    public void OpenPath(string path, bool confirmed = false)
    {
        if (!confirmed && !ConfirmDiscard()) return;
        try { LoadKit(UiKit.Load(path), path); App.AddRecent(path); }
        catch (Exception ex) when (ex is InvalidDataException or IOException) { Ui.Error(ex); }
    }

    public void OnFileMoved(string oldPath, string newPath)
    {
        if (string.Equals(_path, oldPath, StringComparison.OrdinalIgnoreCase)) { _path = newPath; UpdateTitle(); }
    }

    public void WriteAutosave(string file) => UiKit.Save(_kit, file);

    public void RestoreAutosave(string file, string? originalPath)
    {
        LoadKit(UiKit.Load(file), originalPath);
        _dirty = true;
        UpdateTitle();
    }

    /// <summary>The picture currently shown (for the device preview).</summary>
    public (uint[] Pixels, int Width, int Height)? CurrentPicture =>
        Screen is { } s && _view == ViewKind.Screen ? (UiRender.Screen(_kit, s, s.Width, s.Height, App.Project), s.Width, s.Height) : null;

    IReadOnlyList<MenuItem>? _menus;
    Action Do(RoutedEventHandler h) => () => h(this, new RoutedEventArgs());

    public IReadOnlyList<MenuItem> Menus => _menus ??=
    [
        Ui.Menu("_File",
            Ui.Item("_New UI kit…", "Ctrl+N", Do(OnNew)),
            Ui.Item("_Open…", "Ctrl+O", Do(OnOpen)),
            Ui.RecentMenu(this),
            Ui.Item("_Save", "Ctrl+S", Do(OnSave)),
            Ui.Item("Save _as…", "Ctrl+Shift+S", () => Save(null)),
            new Separator(),
            Ui.Item("_Export element PNGs…", null, Do(OnExportPngs)),
            new Separator(),
            Ui.Item("E_xit", "Alt+F4", () => Window.GetWindow(this)?.Close())),
        Ui.Menu("_Edit",
            Ui.Item("_Undo", "Ctrl+Z", Do(OnUndo)),
            Ui.Item("_Redo", "Ctrl+Y", Do(OnRedo)),
            new Separator(),
            Ui.Item("_Place element on screen", "P", () => OnPlace(null, null)),
            Ui.Item("_Remove from screen", "Del", () => OnRemovePlacement(null, null)),
            Ui.Item("_Style…", null, Do(OnShowStyle))),
        Ui.Menu("_View",
            Ui.Item("Zoom _in", "Ctrl+=", Do(OnZoomIn)),
            Ui.Item("Zoom _out", "Ctrl+-", Do(OnZoomOut))),
    ];

    public void HandleKey(KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        int step = shift ? 8 : 1;
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Z when ctrl: OnUndo(null, null); break;
            case Key.Y when ctrl: OnRedo(null, null); break;
            case Key.S when ctrl && shift: Save(null); break;
            case Key.S when ctrl: OnSave(null, null); break;
            case Key.N when ctrl: OnNew(null, null); break;
            case Key.O when ctrl: OnOpen(null, null); break;
            case Key.OemPlus or Key.Add when ctrl: OnZoomIn(this, new RoutedEventArgs()); break;
            case Key.OemMinus or Key.Subtract when ctrl: OnZoomOut(this, new RoutedEventArgs()); break;
            case Key.P when !ctrl: OnPlace(null, null); break;
            case Key.Delete: OnRemovePlacement(null, null); break;
            case Key.Left: Nudge(-step, 0); break;
            case Key.Right: Nudge(step, 0); break;
            case Key.Up: Nudge(0, -step); break;
            case Key.Down: Nudge(0, step); break;
            default: e.Handled = false; break;
        }
    }
}
