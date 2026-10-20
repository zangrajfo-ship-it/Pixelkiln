using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.App;

/// <summary>
/// Scene › Logic: a node graph (.pxlogic). Add nodes from the palette, drag them by the title, drag from an output port
/// onto another node to link, click a link and press Del to remove it. Problems are listed below the graph; the game
/// preview runs the graph of the scene that uses it (spec: .ai/context/logic.md).
/// </summary>
sealed class LogicEditor : UserControl, IEditor
{
    const string Filter = "PixelForge logic (*.pxlogic)|*.pxlogic";
    const double NodeW = 170;

    LogicGraph _doc = new();
    readonly History<LogicGraph> _history = new(g => g.Clone(), App.Settings.UndoLimit);
    string? _path;
    bool _dirty, _refreshing;
    string? _sel;
    LogicLink? _selLink;

    readonly Canvas _canvas = new() { Width = 2400, Height = 1600, Background = Brushes.Transparent, ClipToBounds = true };
    readonly ScrollViewer _scroll;
    readonly StackPanel _props = new() { Margin = new Thickness(10) };
    readonly ListBox _problems = new() { Height = 90, Margin = new Thickness(10, 0, 10, 6) };

    static Brush GroupBrush(LogicKind k) => LogicGraph.IsEvent(k) ? new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5))
        : LogicGraph.IsCondition(k) ? new SolidColorBrush(Color.FromRgb(0xC9, 0x8A, 0x1E)) : new SolidColorBrush(Color.FromRgb(0x3C, 0x9A, 0x5F));

    public LogicEditor()
    {
        Focusable = false;
        AutomationProperties.SetName(_canvas, "Logic graph");
        AutomationProperties.SetName(_problems, "Logic problems");
        var palette = new StackPanel { Margin = new Thickness(10) };
        void Group(string title, IEnumerable<LogicKind> kinds)
        {
            palette.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
            foreach (var k in kinds)
            {
                var b = new Button { Content = k.ToString(), Margin = new Thickness(0, 0, 0, 3), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 2, 8, 2), Background = GroupBrush(k), Foreground = Brushes.White };
                AutomationProperties.SetName(b, $"Add {k} node");
                b.Click += (_, _) => AddNode(k);
                palette.Children.Add(b);
            }
        }
        var all = Enum.GetValues<LogicKind>();
        Group("Events", all.Where(LogicGraph.IsEvent));
        Group("Conditions", all.Where(LogicGraph.IsCondition));
        Group("Actions", all.Where(k => !LogicGraph.IsEvent(k) && !LogicGraph.IsCondition(k)));
        var left = new ScrollViewer { Width = 150, Content = palette, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DockPanel.SetDock(left, Dock.Left);
        var right = new ScrollViewer { Width = 260, Content = _props, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DockPanel.SetDock(right, Dock.Right);
        DockPanel.SetDock(_problems, Dock.Bottom);
        _scroll = new ScrollViewer { Content = _canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Content = new DockPanel { Children = { left, right, _problems, _scroll } };
        _canvas.MouseLeftButtonDown += (_, _) => { (_sel, _selLink) = (null, null); Redraw(); };
        _problems.MouseDoubleClick += (_, _) =>
        {
            if (_problems.SelectedItem is string m && _doc.Nodes.FirstOrDefault(n => m.Contains($"“{n.Id}”")) is { } n) { _sel = n.Id; Redraw(); }
        };
        IsVisibleChanged += (_, _) => UpdateTitle();
        Loaded += Init;
        void Init(object s, RoutedEventArgs e) { Loaded -= Init; LoadDoc(Starter(), null); }
    }

    /// <summary>New graphs start with a small example: entering the "exit" area adds to the score.</summary>
    static LogicGraph Starter()
    {
        var g = new LogicGraph();
        var e = LogicGraph.NewNode(LogicKind.EnterArea, "enter", 40, 60);
        e.Params["area"] = "exit";
        g.Nodes.Add(e);
        g.Nodes.Add(LogicGraph.NewNode(LogicKind.SetVariable, "score", 280, 60));
        g.Links.Add(new LogicLink("enter", "out", "score"));
        return g;
    }

    // ---------- document ----------

    void LoadDoc(LogicGraph g, string? path)
    {
        (_doc, _path, _dirty, _sel, _selLink) = (g, path, false, null, null);
        _history.Clear();
        Redraw();
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
            win.Title = $"PixelForge — {(_path is null ? "untitled logic" : System.IO.Path.GetFileName(_path))}{(_dirty ? " *" : "")}";
    }

    string NewId(LogicKind k)
    {
        string stem = char.ToLowerInvariant(k.ToString()[0]) + k.ToString()[1..];
        return Enumerable.Range(1, 9999).Select(i => $"{stem}{i}").First(id => _doc.Node(id) is null);
    }

    void AddNode(LogicKind k)
    {
        Snapshot();
        double x = _scroll.HorizontalOffset + 60 + _doc.Nodes.Count % 5 * 24, y = _scroll.VerticalOffset + 60 + _doc.Nodes.Count % 5 * 24;
        var n = LogicGraph.NewNode(k, NewId(k), x, y);
        _doc.Nodes.Add(n);
        (_sel, _selLink) = (n.Id, null);
        Redraw();
    }

    void DeleteSelected()
    {
        if (_selLink is { } l) { Snapshot(); _doc.Links.Remove(l); _selLink = null; Redraw(); return; }
        if (_sel is not string id) return;
        Snapshot();
        _doc.Nodes.RemoveAll(n => n.Id == id);
        _doc.Links.RemoveAll(x => x.From == id || x.To == id);
        _sel = null;
        Redraw();
    }

    // ---------- drawing ----------

    static Point PortOut(LogicNode n, string port) =>
        new(n.X + NodeW, n.Y + (LogicGraph.IsCondition(n.Kind) ? (port == "true" ? 34 : 56) : 34));
    static Point PortIn(LogicNode n) => new(n.X, n.Y + 34);

    void Redraw()
    {
        _canvas.Children.Clear();
        foreach (var l in _doc.Links)
        {
            if (_doc.Node(l.From) is not { } a || _doc.Node(l.To) is not { } b) continue;
            var (p0, p1) = (PortOut(a, l.Port), PortIn(b));
            double dx = Math.Max(40, Math.Abs(p1.X - p0.X) / 2);
            var fig = new PathFigure { StartPoint = p0, Segments = { new BezierSegment(new Point(p0.X + dx, p0.Y), new Point(p1.X - dx, p1.Y), p1, true) } };
            bool sel = l == _selLink;
            var path = new System.Windows.Shapes.Path
            {
                Data = new PathGeometry([fig]), StrokeThickness = sel ? 4 : 2.5, Cursor = Cursors.Hand,
                Stroke = sel ? Brushes.Orange : l.Port == "false" ? Brushes.IndianRed : l.Port == "true" ? Brushes.SeaGreen : Brushes.Gray,
                ToolTip = $"{l.From}.{l.Port} → {l.To} (click, then Del to remove)",
            };
            var link = l;
            path.MouseLeftButtonDown += (_, e) => { (_selLink, _sel) = (link, null); e.Handled = true; Redraw(); };
            _canvas.Children.Add(path);
        }
        foreach (var n in _doc.Nodes) DrawNode(n);
        ShowProps();
        _problems.ItemsSource = _doc.Check() is { Count: > 0 } m ? m : ["No problems."];
    }

    void DrawNode(LogicNode n)
    {
        bool sel = n.Id == _sel;
        var title = new Border
        {
            Background = GroupBrush(n.Kind), Padding = new Thickness(6, 3, 6, 3), Cursor = Cursors.SizeAll,
            Child = new TextBlock { Text = $"{n.Kind}  ·  {n.Id}", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
        };
        string summary = string.Join("\n", n.Params.Where(kv => kv.Value.Length > 0).Select(kv => $"{kv.Key}: {Short(kv.Key, kv.Value)}"));
        var body = new StackPanel { Children = { title, new TextBlock { Text = summary.Length > 0 ? summary : " ", Margin = new Thickness(6, 4, 6, 6), TextWrapping = TextWrapping.Wrap, FontSize = 11 } } };
        var box = new Border
        {
            Width = NodeW, MinHeight = 66, Child = body, BorderThickness = new Thickness(sel ? 2.5 : 1), CornerRadius = new CornerRadius(4),
            BorderBrush = sel ? Brushes.Orange : Brushes.Gray, Background = (Brush)(TryFindResource(SystemColors.WindowBrushKey) ?? Brushes.White),
        };
        AutomationProperties.SetName(box, $"{n.Kind} node {n.Id}");
        Canvas.SetLeft(box, n.X);
        Canvas.SetTop(box, n.Y);
        _canvas.Children.Add(box);
        box.MouseLeftButtonDown += (_, e) => { e.Handled = true; if (_sel != n.Id || _selLink is not null) { (_sel, _selLink) = (n.Id, null); Redraw(); } };

        // move by the title
        Point? from = null;
        bool moved = false;
        title.MouseLeftButtonDown += (_, e) => { (_sel, _selLink, from, moved) = (n.Id, null, e.GetPosition(_canvas), false); title.CaptureMouse(); e.Handled = true; };
        title.MouseMove += (_, e) =>
        {
            if (from is not Point f || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(_canvas);
            if (!moved) { Snapshot(); moved = true; }
            var node = _doc.Node(n.Id)!;
            (node.X, node.Y) = (Math.Clamp(node.X + p.X - f.X, 0, _canvas.Width - NodeW), Math.Clamp(node.Y + p.Y - f.Y, 0, _canvas.Height - 60));
            from = p;
            Canvas.SetLeft(box, node.X);
            Canvas.SetTop(box, node.Y);
        };
        title.MouseLeftButtonUp += (_, _) => { title.ReleaseMouseCapture(); from = null; Redraw(); };

        if (!LogicGraph.IsEvent(n.Kind)) Port(PortIn(n), Brushes.Gray, "in", null);
        foreach (var port in LogicGraph.Ports(n.Kind))
            Port(PortOut(n, port), port == "true" ? Brushes.SeaGreen : port == "false" ? Brushes.IndianRed : Brushes.DimGray, port, n);
    }

    string Short(string key, string value) =>
        key is "sound" or "scene" or "library" && Guid.TryParse(value, out var id) ? App.Project?.Find(id)?.Name ?? "missing asset" : value;

    /// <summary>A port dot; dragging from an output port onto a node links them.</summary>
    void Port(Point at, Brush fill, string port, LogicNode? owner)
    {
        var dot = new Ellipse { Width = 12, Height = 12, Fill = fill, Stroke = Brushes.White, StrokeThickness = 1.5, ToolTip = port };
        Canvas.SetLeft(dot, at.X - 6);
        Canvas.SetTop(dot, at.Y - 6);
        _canvas.Children.Add(dot);
        if (owner is null) return;
        dot.Cursor = Cursors.Cross;
        Line? rubber = null;
        dot.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            rubber = new Line { X1 = at.X, Y1 = at.Y, X2 = at.X, Y2 = at.Y, Stroke = Brushes.Orange, StrokeThickness = 2, StrokeDashArray = [3, 2], IsHitTestVisible = false };
            _canvas.Children.Add(rubber);
            dot.CaptureMouse();
        };
        dot.MouseMove += (_, e) =>
        {
            if (rubber is null) return;
            var p = e.GetPosition(_canvas);
            (rubber.X2, rubber.Y2) = (p.X, p.Y);
        };
        dot.MouseLeftButtonUp += (_, e) =>
        {
            if (rubber is null) return;
            dot.ReleaseMouseCapture();
            _canvas.Children.Remove(rubber);
            rubber = null;
            var p = e.GetPosition(_canvas);
            var target = _doc.Nodes.LastOrDefault(t => p.X >= t.X && p.X <= t.X + NodeW && p.Y >= t.Y && p.Y <= t.Y + 80);
            if (target is null || target.Id == owner.Id) return;
            if (LogicGraph.IsEvent(target.Kind)) { _problems.ItemsSource = new[] { $"“{target.Id}” is an event: events start chains and cannot be linked to." }; return; }
            var link = new LogicLink(owner.Id, port, target.Id);
            if (_doc.Links.Contains(link)) return;
            Snapshot();
            _doc.Links.Add(link);
            Redraw();
        };
    }

    // ---------- properties of the selected node ----------

    void ShowProps()
    {
        _refreshing = true;
        _props.Children.Clear();
        if (_sel is null || _doc.Node(_sel) is not { } n)
        {
            _props.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, Opacity = 0.8,
                Text = "Add nodes from the left. Drag a node by its title, drag from a coloured dot on its right edge onto another node to link them. " +
                       "Events (blue) start chains, conditions (orange) choose the green or red way, actions (green) do things. " +
                       "Choose this file under Logic in the scene's Layout mode; F5 there runs it.",
            });
            _refreshing = false;
            return;
        }
        _props.Children.Add(new TextBlock { Text = $"{n.Kind} “{n.Id}”", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        foreach (var (key, _) in LogicGraph.ParamsOf(n.Kind))
        {
            Control input = key switch
            {
                "op" => Combo(n.Kind == LogicKind.Compare ? LogicGraph.CompareOps : LogicGraph.SetOps, n.P(key)),
                "visible" or "repeat" => Combo(["yes", "no"], n.P(key)),
                "sound" => Assets(n.P(key), t => AudioLibrary.IsAudio(t)),
                "scene" => Assets(n.P(key), t => t == AssetType.Scene),
                "library" => Assets(n.P(key), t => t == AssetType.Anim),
                _ => new TextBox { Text = n.P(key) },
            };
            AutomationProperties.SetName(input, key);
            _props.Children.Add(new Label { Content = key, Target = input, Padding = new Thickness(0, 6, 0, 2) });
            _props.Children.Add(input);
            string k = key;
            void Commit()
            {
                if (_refreshing) return;
                string v = input switch
                {
                    TextBox t => t.Text.Trim(),
                    ComboBox { SelectedItem: ComboBoxItem { Tag: Guid g } } => g.ToString(),
                    ComboBox c => c.SelectedItem as string ?? "",
                    _ => "",
                };
                if (v == n.P(k)) return;
                Snapshot();
                _doc.Node(n.Id)!.Params[k] = v;
                Redraw();
            }
            if (input is TextBox tb) tb.LostKeyboardFocus += (_, _) => Commit();
            else ((ComboBox)input).SelectionChanged += (_, _) => Commit();
        }
        var del = new Button { Content = "Delete node", Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 2, 8, 2) };
        del.Click += (_, _) => DeleteSelected();
        _props.Children.Add(del);
        _refreshing = false;
    }

    static ComboBox Combo(string[] options, string value) => new() { ItemsSource = options, SelectedItem = options.Contains(value) ? value : options[0] };

    static ComboBox Assets(string value, Func<AssetType, bool> fits)
    {
        var items = App.Project?.Assets.Where(a => fits(a.Type)).OrderBy(a => a.Path).Select(a => new ComboBoxItem { Content = a.Path, Tag = a.Id }).ToList() ?? [];
        return new ComboBox { ItemsSource = items, SelectedItem = items.FirstOrDefault(i => i.Tag is Guid g && g.ToString() == value) };
    }

    // ---------- file ----------

    void OnNew() { if (ConfirmDiscard()) LoadDoc(Starter(), null); }

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
            var dlg = new SaveFileDialog { Filter = Filter, FileName = "logic.pxlogic", InitialDirectory = App.Project?.Root ?? "" };
            if (dlg.ShowDialog() != true) return false;
            path = dlg.FileName;
        }
        try
        {
            LogicGraph.Save(_doc, path);
            (_path, _dirty) = (path, false);
            App.AddRecent(path);
            UpdateTitle();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { Ui.Error(ex); return false; }
    }

    void Undo() { if (_history.Undo(_doc) is { } g) { _doc = g; _dirty = true; Redraw(); UpdateTitle(); } }
    void Redo() { if (_history.Redo(_doc) is { } g) { _doc = g; _dirty = true; Redraw(); UpdateTitle(); } }

    // ---------- IEditor ----------

    public string[] FileExtensions => [".pxlogic"];
    public bool IsDirty => _dirty;
    public string? CurrentPath => _path;

    public bool ConfirmDiscard()
    {
        if (!_dirty) return true;
        var r = MessageBox.Show("Save changes to the current logic?", "PixelForge", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return r == MessageBoxResult.No || (r == MessageBoxResult.Yes && Save(_path));
    }

    public void OpenPath(string path, bool confirmed = false)
    {
        if (!confirmed && !ConfirmDiscard()) return;
        try { LoadDoc(LogicGraph.Load(path), path); App.AddRecent(path); }
        catch (Exception ex) when (ex is InvalidDataException or IOException) { Ui.Error(ex); }
    }

    public void OnFileMoved(string oldPath, string newPath)
    {
        if (string.Equals(_path, oldPath, StringComparison.OrdinalIgnoreCase)) { _path = newPath; UpdateTitle(); }
    }

    public void WriteAutosave(string file) => LogicGraph.Save(_doc, file);

    public void RestoreAutosave(string file, string? originalPath)
    {
        LoadDoc(LogicGraph.Load(file), originalPath);
        _dirty = true;
        UpdateTitle();
    }

    IReadOnlyList<MenuItem>? _menus;

    public IReadOnlyList<MenuItem> Menus => _menus ??=
    [
        Ui.Menu("_File",
            Ui.Item("_New logic", "Ctrl+N", OnNew),
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
            Ui.Item("_Delete selected node or link", "Del", DeleteSelected)),
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
            case Key.Delete: DeleteSelected(); break;
            default: e.Handled = false; break;
        }
    }
}
