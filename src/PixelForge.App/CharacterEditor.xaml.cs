using System.Windows;
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
/// Character Studio: a character on top of a sprite asset. States map to the sprite's tags (timing stays in the
/// sprite), plus origin, facing, hit/hurt/solid boxes, game properties, preview with speed and transitions.
/// </summary>
public partial class CharacterEditor : UserControl, IEditor, IStyleSource
{
    Character _char = NewCharacter();
    readonly History<Character> _history = new(c => c.Clone(), App.Settings.UndoLimit);
    string? _path;
    bool _dirty, _refreshing;

    Sprite? _sprite;
    string? _spritePath;
    int _zoom = 6, _frameInState;
    CharacterBox? _selBox;
    Point? _dragFrom;
    Rectangle? _dragRect;
    readonly DispatcherTimer _play = new();

    /// <summary>Set by MainWindow: true if that sprite file is open with unsaved changes in the Sprite tab.</summary>
    public Func<string, bool>? IsSpriteOpenDirty { get; set; }
    /// <summary>Set by MainWindow: the sprite file was changed here (template), so an open Sprite tab can reload it.</summary>
    public Action<string>? SpriteFileChanged { get; set; }

    static readonly double[] Speeds = [0.25, 0.5, 1, 2];

    public CharacterEditor()
    {
        InitializeComponent();
        KindBox.ItemsSource = Enum.GetNames<CharacterKind>();
        FacingBox.ItemsSource = Enum.GetNames<Facing>();
        BoxKindBox.ItemsSource = Enum.GetNames<BoxKind>();
        BoxKindBox.SelectedIndex = 0;
        SpeedBox.ItemsSource = Speeds.Select(s => $"{s}×").ToArray();
        SpeedBox.SelectedIndex = 2;
        PreviewChecker.Fill = Ui.CheckerBrush();
        _play.Tick += (_, _) => Step();
        App.ProjectChanged += () => { if (IsLoaded) RefreshAll(); };
        App.SettingsChanged += () => PreviewChecker.Fill = Ui.CheckerBrush();
        Loaded += Init;
        IsVisibleChanged += (_, _) => UpdateTitle();

        void Init(object s, RoutedEventArgs e) { Loaded -= Init; LoadCharacter(_char, null); }
    }

    // ---------- document ----------

    void LoadCharacter(Character c, string? path)
    {
        StopPlay();
        _char = c;
        _path = path;
        _dirty = false;
        _history.Clear();
        _selBox = null;
        RefreshAll();
    }

    void Snapshot()
    {
        _history.Push(_char);
        _dirty = true;
        UpdateTitle();
    }

    // ---------- shared with the Rig mode ----------

    internal Character Doc => _char;
    internal Sprite? SpriteDoc => _sprite;
    /// <summary>Raised after the document was replaced or changed (open, undo, rig edits).</summary>
    internal event Action? DocumentChanged;

    /// <summary>An undoable edit made by another mode.</summary>
    internal void Change(Action edit)
    {
        Snapshot();
        edit();
        RefreshAll();
    }

    sealed record CharacterStyle(CharacterKind Kind, Facing Facing, Dictionary<string, string> Properties);

    public object? CopyStyle() => new CharacterStyle(_char.Kind, _char.Facing, new(_char.Properties));

    public string? PasteStyle(object style)
    {
        if (style is not CharacterStyle s) return null;
        Snapshot();
        (_char.Kind, _char.Facing, _char.Properties) = (s.Kind, s.Facing, new(s.Properties));
        RefreshAll();
        return "Kind, facing and properties pasted.";
    }

    void RefreshAll()
    {
        _refreshing = true;
        var p = App.Project;
        var sprites = p?.Assets.Where(a => a.Type == AssetType.Sprite).OrderBy(a => a.Path).ToList() ?? [];
        SpriteBox.ItemsSource = sprites.Select(a => new ComboBoxItem { Content = a.Path, Tag = a.Id }).ToList();
        SpriteBox.SelectedItem = SpriteBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (Guid)i.Tag == _char.SpriteAssetId);
        SpriteBox.IsEnabled = p is not null;
        KindBox.SelectedItem = _char.Kind.ToString();
        FacingBox.SelectedItem = _char.Facing.ToString();
        OriginXBox.Text = _char.OriginX.ToString();
        OriginYBox.Text = _char.OriginY.ToString();
        CategoryBox.ItemsSource = (p?.Settings.CollisionCategories ?? new ProjectSettings().CollisionCategories)
            .OrderBy(kv => kv.Key).Select(kv => new ComboBoxItem { Content = $"{kv.Key}: {kv.Value}", Tag = kv.Key }).ToList();
        if (CategoryBox.SelectedIndex < 0) CategoryBox.SelectedIndex = 0;
        PropsBox.Text = string.Join("\n", _char.Properties.Select(kv => $"{kv.Key} = {kv.Value}"));
        LoadSprite();
        RefreshStates();
        _refreshing = false;
        RenderPreview();
        RefreshBoxList();
        UpdateTitle();
        DocumentChanged?.Invoke();
    }

    void LoadSprite()
    {
        _sprite = null;
        _spritePath = null;
        if (App.Project is null) { Status.Text = "Open a project (Project menu) to link the character to a sprite asset."; return; }
        if (_char.SpriteAssetId is not Guid id) { Status.Text = "Choose the character's sprite."; return; }
        if (App.Project.Resolve(id) is not string path) { Status.Text = "The character's sprite asset is missing from the project."; return; }
        try
        {
            _sprite = ProjectFile.Load(path);
            _spritePath = path;
            _zoom = Math.Clamp(320 / Math.Max(_sprite.Width, _sprite.Height), 1, 12);
            Status.Text = $"{System.IO.Path.GetFileName(path)}: {_sprite.Width}×{_sprite.Height}, {_sprite.Frames.Count} frames, {_sprite.Tags.Count} tags";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException) { Status.Text = ex.Message; }
    }

    void RefreshStates()
    {
        string? keep = StateBox.SelectedItem as string;
        StatesPanel.Children.Clear();
        var tags = _sprite?.Tags.Select(t => t.Name).ToList() ?? [];
        foreach (var (state, tag) in _char.Animations.OrderBy(kv => Array.IndexOf(Character.StandardStates, kv.Key) is int i && i >= 0 ? i : 99).ThenBy(kv => kv.Key))
        {
            var box = new ComboBox { ItemsSource = tags, SelectedItem = tags.Contains(tag) ? tag : null, MinWidth = 110 };
            System.Windows.Automation.AutomationProperties.SetName(box, $"Sprite tag for state {state}");
            string s = state;
            box.SelectionChanged += (_, _) =>
            {
                if (_refreshing || box.SelectedItem is not string t || _char.Animations[s] == t) return;
                Snapshot();
                _char.Animations[s] = t;
                RenderPreview();
            };
            var label = new TextBlock { Text = state, Width = 70, VerticalAlignment = VerticalAlignment.Center, Foreground = tags.Contains(tag) ? null : Brushes.OrangeRed, ToolTip = tags.Contains(tag) ? null : "No sprite tag with this name yet" };
            DockPanel.SetDock(label, Dock.Left);
            StatesPanel.Children.Add(new DockPanel { Margin = new Thickness(0, 1, 0, 1), Children = { label, box } });
        }
        var names = _char.Animations.Keys.ToList();
        StateBox.ItemsSource = names;
        StateBox.SelectedItem = keep is not null && names.Contains(keep) ? keep : names.FirstOrDefault();
        ThenBox.ItemsSource = new[] { "(loop)" }.Concat(names).ToList();
        ThenBox.SelectedIndex = 0;
    }

    void UpdateTitle()
    {
        if (IsVisible && Window.GetWindow(this) is { } win)
            win.Title = $"PixelForge — {(_path is null ? "untitled character" : System.IO.Path.GetFileName(_path))}{(_dirty ? " *" : "")}";
    }

    // ---------- fields ----------

    void OnSpriteChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || SpriteBox.SelectedItem is not ComboBoxItem { Tag: Guid id } || id == _char.SpriteAssetId) return;
        Snapshot();
        _char.SpriteAssetId = id;
        RefreshAll();
    }

    void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || KindBox.SelectedItem is not string k || k == _char.Kind.ToString()) return;
        Snapshot();
        _char.Kind = Enum.Parse<CharacterKind>(k);
    }

    void OnFacingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || FacingBox.SelectedItem is not string f || f == _char.Facing.ToString()) return;
        Snapshot();
        _char.Facing = Enum.Parse<Facing>(f);
        RenderPreview();
    }

    void OnFieldKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnOriginCommit(sender, e);
    }

    void OnOriginCommit(object sender, RoutedEventArgs e)
    {
        bool okX = int.TryParse(OriginXBox.Text, out int x), okY = int.TryParse(OriginYBox.Text, out int y);
        Ui.MarkInvalid(OriginXBox, okX ? null : "Whole number of sprite pixels.");
        Ui.MarkInvalid(OriginYBox, okY ? null : "Whole number of sprite pixels.");
        if (!okX || !okY || (x, y) == (_char.OriginX, _char.OriginY)) return;
        Snapshot();
        (_char.OriginX, _char.OriginY) = (x, y);
        RenderPreview();
    }

    void OnPropsCommit(object sender, RoutedEventArgs e)
    {
        var props = new Dictionary<string, string>();
        foreach (var line in PropsBox.Text.Split('\n'))
        {
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            props[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        if (props.Count == _char.Properties.Count && props.All(kv => _char.Properties.GetValueOrDefault(kv.Key) == kv.Value)) return;
        Snapshot();
        _char.Properties = props;
    }

    void OnAddState(object sender, RoutedEventArgs e)
    {
        if (Ui.Ask(this, "Add state", "State name (e.g. crouch, climb):", "") is not string name || name.Trim().Length == 0) return;
        name = name.Trim().ToLowerInvariant();
        if (_char.Animations.ContainsKey(name)) return;
        Snapshot();
        _char.Animations[name] = _sprite?.Tags.Any(t => t.Name == name) == true ? name : _sprite?.Tags.FirstOrDefault()?.Name ?? name;
        RefreshStates();
        StateBox.SelectedItem = name;
    }

    void OnRemoveState(object sender, RoutedEventArgs e)
    {
        if (StateBox.SelectedItem is not string s) return;
        Snapshot();
        _char.Animations.Remove(s);
        _char.Boxes.RemoveAll(b => b.State == s);
        RefreshStates();
        RefreshBoxList();
        RenderPreview();
    }

    // ---------- preview ----------

    string? State => StateBox.SelectedItem as string;

    (int From, int To) Range => _sprite is null ? (0, 0)
        : State is string s && _char.FramesOf(s, _sprite) is { } r ? r : (0, _sprite.Frames.Count - 1);

    void OnStateChanged(object sender, SelectionChangedEventArgs e)
    {
        _frameInState = 0;
        _selBox = null;
        RenderPreview();
        RefreshBoxList();
    }

    void OnFrameSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_refreshing) return;
        _frameInState = (int)FrameSlider.Value;
        RenderPreview();
    }

    void RenderPreview()
    {
        BoxLayer.Children.Clear();
        if (_sprite is not { } s) { PreviewImage.Source = null; PreviewHost.Width = PreviewHost.Height = 0; return; }
        var (from, to) = Range;
        _frameInState = Math.Clamp(_frameInState, 0, to - from);
        _refreshing = true;
        FrameSlider.Maximum = to - from;
        FrameSlider.Value = _frameInState;
        _refreshing = false;
        int frame = from + _frameInState;
        FrameText.Text = $"frame {_frameInState + 1}/{to - from + 1}";
        PreviewImage.Source = ImageIO.ToBitmap(Compositor.Flatten(s, frame), s.Width, s.Height);
        PreviewHost.Width = s.Width * _zoom;
        PreviewHost.Height = s.Height * _zoom;

        foreach (var b in _char.BoxesAt(State, _frameInState))
        {
            var color = b.Kind switch { BoxKind.Hit => Colors.Red, BoxKind.Solid => Colors.DeepSkyBlue, BoxKind.Trigger => Colors.Gold, _ => Colors.LimeGreen };
            var r = new Rectangle
            {
                Width = b.W * _zoom, Height = b.H * _zoom, Stroke = new SolidColorBrush(b == _selBox ? Colors.White : color),
                StrokeThickness = b == _selBox ? 3 : 2, Fill = new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B)),
            };
            Canvas.SetLeft(r, b.X * _zoom);
            Canvas.SetTop(r, b.Y * _zoom);
            BoxLayer.Children.Add(r);
        }
        // origin cross
        double ox = (_char.OriginX + 0.5) * _zoom, oy = (_char.OriginY + 0.5) * _zoom;
        BoxLayer.Children.Add(new Line { X1 = ox - 8, X2 = ox + 8, Y1 = oy, Y2 = oy, Stroke = Brushes.Magenta, StrokeThickness = 2 });
        BoxLayer.Children.Add(new Line { X1 = ox, X2 = ox, Y1 = oy - 8, Y2 = oy + 8, Stroke = Brushes.Magenta, StrokeThickness = 2 });
        System.Windows.Automation.AutomationProperties.SetItemStatus(PreviewHost,
            $"{State ?? "all frames"}, frame {_frameInState + 1}, {_char.BoxesAt(State, _frameInState).Count()} boxes, origin {_char.OriginX},{_char.OriginY}");
    }

    void OnSpeedChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_play.IsEnabled) Step(advance: false);
    }

    void OnPlay(object? sender, RoutedEventArgs? e)
    {
        if (_play.IsEnabled) { StopPlay(); return; }
        if (_sprite is null) { PlayBtn.IsChecked = false; return; }
        PlayBtn.IsChecked = true;
        PlayBtn.Content = "■ Stop";
        Step(advance: false);
        _play.Start();
    }

    void StopPlay()
    {
        _play.Stop();
        PlayBtn.IsChecked = false;
        PlayBtn.Content = "▶ Play";
    }

    /// <summary>Next frame; at the end of the state either loop or move on to the "Then" state (transition preview).</summary>
    void Step(bool advance = true)
    {
        if (_sprite is not { } s) return;
        var (from, to) = Range;
        if (advance)
        {
            if (_frameInState >= to - from)
            {
                if (ThenBox.SelectedIndex > 0 && ThenBox.SelectedItem is string next && next != State)
                {
                    ThenBox.SelectedIndex = 0;   // one transition, then loop the new state
                    StateBox.SelectedItem = next;
                    (from, to) = Range;
                }
                _frameInState = 0;
            }
            else _frameInState++;
        }
        RenderPreview();
        double speed = Speeds[Math.Max(0, SpeedBox.SelectedIndex)];
        _play.Interval = TimeSpan.FromMilliseconds(s.Frames[from + _frameInState].DurationMs / speed);
    }

    // ---------- boxes ----------

    (int X, int Y) PixelAt(MouseEventArgs e)
    {
        var p = e.GetPosition(PreviewHost);
        return ((int)Math.Floor(p.X / _zoom), (int)Math.Floor(p.Y / _zoom));
    }

    void OnPreviewDown(object sender, MouseButtonEventArgs e)
    {
        PreviewHost.Focus();
        if (_sprite is null) return;
        StopPlay();
        var (x, y) = PixelAt(e);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            Snapshot();
            (_char.OriginX, _char.OriginY) = (x, y);
            OriginXBox.Text = x.ToString();
            OriginYBox.Text = y.ToString();
            RenderPreview();
            return;
        }
        var hit = _char.BoxesAt(State, _frameInState).LastOrDefault(b => x >= b.X && y >= b.Y && x < b.X + b.W && y < b.Y + b.H);
        if (hit is not null) { SelectBox(hit); return; }
        _dragFrom = new Point(x, y);
        _dragRect = new Rectangle { Stroke = Brushes.White, StrokeDashArray = [3, 2], StrokeThickness = 2 };
        BoxLayer.Children.Add(_dragRect);
        PreviewHost.CaptureMouse();
        OnPreviewMove(sender, e);
    }

    (int X, int Y, int W, int H) DragBox(MouseEventArgs e)
    {
        var (x, y) = PixelAt(e);
        int x0 = (int)_dragFrom!.Value.X, y0 = (int)_dragFrom.Value.Y;
        int ax = Math.Clamp(Math.Min(x, x0), 0, _sprite!.Width - 1), ay = Math.Clamp(Math.Min(y, y0), 0, _sprite.Height - 1);
        int bx = Math.Clamp(Math.Max(x, x0), 0, _sprite.Width - 1), by = Math.Clamp(Math.Max(y, y0), 0, _sprite.Height - 1);
        return (ax, ay, bx - ax + 1, by - ay + 1);
    }

    void OnPreviewMove(object sender, MouseEventArgs e)
    {
        if (_dragRect is null || _dragFrom is null) return;
        var (x, y, w, h) = DragBox(e);
        Canvas.SetLeft(_dragRect, x * _zoom);
        Canvas.SetTop(_dragRect, y * _zoom);
        (_dragRect.Width, _dragRect.Height) = (w * _zoom, h * _zoom);
    }

    void OnPreviewUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragFrom is null) return;
        var (x, y, w, h) = DragBox(e);
        _dragFrom = null;
        _dragRect = null;
        PreviewHost.ReleaseMouseCapture();
        Snapshot();
        var box = new CharacterBox
        {
            Kind = Enum.Parse<BoxKind>((string)BoxKindBox.SelectedItem), X = x, Y = y, W = w, H = h,
            Category = CategoryBox.SelectedItem is ComboBoxItem { Tag: int c } ? c : 0,
            State = ThisStateOnly.IsChecked == true ? State : null,
            Frame = ThisFrameOnly.IsChecked == true ? _frameInState : null,
        };
        _char.Boxes.Add(box);
        SelectBox(box);
    }

    void SelectBox(CharacterBox? b)
    {
        _selBox = b;
        RefreshBoxList();
        RenderPreview();
    }

    void RefreshBoxList()
    {
        _refreshing = true;
        BoxList.Items.Clear();
        foreach (var b in _char.Boxes)
        {
            string where = (b.State ?? "all states") + (b.Frame is int f ? $", frame {f + 1}" : "");
            BoxList.Items.Add(new ListBoxItem { Content = $"{b.Kind}  {b.X},{b.Y} {b.W}×{b.H}  ({where})", Tag = b });
        }
        BoxList.SelectedItem = BoxList.Items.Cast<ListBoxItem>().FirstOrDefault(i => i.Tag == _selBox);
        _refreshing = false;
    }

    void OnBoxSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing) return;
        _selBox = (BoxList.SelectedItem as ListBoxItem)?.Tag as CharacterBox;
        RenderPreview();
    }

    void OnPreviewKey(object sender, KeyEventArgs e)
    {
        if (_selBox is not { } b) return;
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        int dx = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0, dy = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
        if (e.Key == Key.Delete)
        {
            Snapshot();
            _char.Boxes.Remove(b);
            SelectBox(null);
        }
        else if (dx != 0 || dy != 0)
        {
            Snapshot();
            if (shift) (b.W, b.H) = (Math.Max(1, b.W + dx), Math.Max(1, b.H + dy));
            else (b.X, b.Y) = (b.X + dx, b.Y + dy);
            SelectBox(b);
        }
        else return;
        e.Handled = true;
    }

    // ---------- template + export ----------

    void OnTemplate(object sender, RoutedEventArgs e)
    {
        if (_sprite is not { } s || _spritePath is not string path) { Ui.Error(new InvalidOperationException("Choose the character's sprite first.")); return; }
        if (IsSpriteOpenDirty?.Invoke(path) == true) { Ui.Error(new InvalidOperationException("The sprite has unsaved changes in the Sprite tab. Save it first.")); return; }
        var missing = Character.StandardStates.Where(st => s.Tags.All(t => t.Name != st)).ToList();
        if (missing.Count > 0 && MessageBox.Show(Window.GetWindow(this),
                $"Add {missing.Count} tag(s) ({string.Join(", ", missing)}) with one new frame each to “{System.IO.Path.GetFileName(path)}” and save it?",
                "Standard states", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        Snapshot();
        _char.ApplyTemplate(s);
        if (missing.Count > 0)
        {
            try { ProjectFile.Save(s, path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); return; }
            SpriteFileChanged?.Invoke(path);
            App.NotifyProjectChanged();
        }
        RefreshAll();
    }

    void OnExport(object sender, RoutedEventArgs e)
    {
        if (_sprite is not { } s) { Ui.Error(new InvalidOperationException("Choose the character's sprite first.")); return; }
        string name = System.IO.Path.GetFileNameWithoutExtension(_path) ?? "character";
        var dlg = new SaveFileDialog { Filter = "Character JSON (*.json)|*.json", FileName = name + ".json" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            string sheetName = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName) + "-sheet.png";
            var sheet = SpriteSheet.Build(s);
            ImageIO.SavePng(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dlg.FileName)!, sheetName), sheet.Pixels, sheet.Width, sheet.Height);
            DocumentFile.WriteAtomic(dlg.FileName, CharacterExport.Json(_char, s, name, sheetName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); }
    }

    // ---------- undo ----------

    void OnUndo(object? sender, RoutedEventArgs? e) => Restore(_history.Undo(_char));
    void OnRedo(object? sender, RoutedEventArgs? e) => Restore(_history.Redo(_char));

    void Restore(Character? c)
    {
        if (c is null) return;
        _char = c;
        _selBox = null;
        _dirty = true;
        RefreshAll();
    }

    // ---------- files ----------

    const string Filter = "PixelForge character (*.pxchar)|*.pxchar";

    void OnNew(object? sender, RoutedEventArgs? e)
    {
        if (!ConfirmDiscard()) return;
        LoadCharacter(NewCharacter(), null);
    }

    static Character NewCharacter()
    {
        var c = new Character();
        foreach (var st in Character.StandardStates.Take(2)) c.Animations[st] = st;   // idle, walk to start with
        return c;
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
            var dlg = new SaveFileDialog { Filter = Filter, FileName = "character.pxchar", InitialDirectory = App.Project?.Root ?? "" };
            if (dlg.ShowDialog() != true) return false;
            path = dlg.FileName;
        }
        try
        {
            Character.Save(_char, path);
            (_path, _dirty) = (path, false);
            UpdateTitle();
            App.AddRecent(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); return false; }
    }

    // ---------- IEditor ----------

    public string[] FileExtensions => [".pxchar"];
    public bool IsDirty => _dirty;
    public string? CurrentPath => _path;

    public bool ConfirmDiscard()
    {
        if (!_dirty) return true;
        var r = MessageBox.Show("Save changes to the current character?", "PixelForge", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return r == MessageBoxResult.No || (r == MessageBoxResult.Yes && Save(_path));
    }

    public void OpenPath(string path, bool confirmed = false)
    {
        if (!confirmed && !ConfirmDiscard()) return;
        try { LoadCharacter(Character.Load(path), path); App.AddRecent(path); }
        catch (Exception ex) when (ex is InvalidDataException or IOException) { Ui.Error(ex); }
    }

    public void OnFileMoved(string oldPath, string newPath)
    {
        if (string.Equals(_path, oldPath, StringComparison.OrdinalIgnoreCase)) { _path = newPath; UpdateTitle(); }
    }

    public void WriteAutosave(string file) => Character.Save(_char, file);

    public void RestoreAutosave(string file, string? originalPath)
    {
        LoadCharacter(Character.Load(file), originalPath);
        _dirty = true;
        UpdateTitle();
    }

    IReadOnlyList<MenuItem>? _menus;

    Action Do(RoutedEventHandler h) => () => h(this, new RoutedEventArgs());

    public IReadOnlyList<MenuItem> Menus => _menus ??=
    [
        Ui.Menu("_File",
            Ui.Item("_New character", "Ctrl+N", Do(OnNew)),
            Ui.Item("_Open…", "Ctrl+O", Do(OnOpen)),
            Ui.RecentMenu(this),
            Ui.Item("_Save", "Ctrl+S", Do(OnSave)),
            Ui.Item("Save _as…", "Ctrl+Shift+S", () => Save(null)),
            new Separator(),
            Ui.Item("_Export character JSON…", null, Do(OnExport)),
            new Separator(),
            Ui.Item("E_xit", "Alt+F4", () => Window.GetWindow(this)?.Close())),
        Ui.Menu("_Edit",
            Ui.Item("_Undo", "Ctrl+Z", Do(OnUndo)),
            Ui.Item("_Redo", "Ctrl+Y", Do(OnRedo)),
            new Separator(),
            Ui.Item("Add _standard states to sprite", null, Do(OnTemplate)),
            Ui.Item("Add s_tate…", null, Do(OnAddState)),
            Ui.Item("_Delete selected box", "Del", () => { if (_selBox is { } b) { Snapshot(); _char.Boxes.Remove(b); SelectBox(null); } })),
        Ui.Menu("_Play",
            Ui.Item("_Play / stop", "Space", () => OnPlay(null, null))),
    ];

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
            case Key.Space: OnPlay(null, null); break;
            default: e.Handled = false; break;
        }
    }
}
