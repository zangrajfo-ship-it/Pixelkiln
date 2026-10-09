using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.App;

public partial class SpriteEditor : UserControl
{
    static readonly int[] ZoomLevels = [1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64];

    Sprite _sprite = new(App.Settings.DefaultSpriteWidth, App.Settings.DefaultSpriteHeight);
    readonly History<Sprite> _history = new(s => s.Clone(), App.Settings.UndoLimit);
    int _layer, _frame, _zoom = 16;
    uint _primary = 0xFF000000, _secondary = 0;
    bool _editSecondary;   // which color the picker edits
    string? _path;
    bool _dirty;
    WriteableBitmap? _bmp;

    public SpriteEditor()
    {
        InitializeComponent();
        // TabControl re-fires Loaded on every tab switch; init once
        Loaded += Init;
        IsVisibleChanged += (_, _) => UpdateTitle();

        void Init(object s, RoutedEventArgs e) { Loaded -= Init; LoadSprite(_sprite, null); UpdateToolOptions(); }
        Checker.Fill = Ui.CheckerBrush();
        App.SettingsChanged += () => { Checker.Fill = Ui.CheckerBrush(); ApplyZoom(); };
        Picker.ColorChanged += c =>
        {
            if (_editSecondary) _secondary = c; else _primary = c;
            RefreshSwatches();
        };
        // visible focus on the canvas (WCAG 2.4.7): shortcuts go to the editor while it has focus
        IsKeyboardFocusedChanged += (_, _) => Scroller.BorderBrush = IsKeyboardFocused ? (Brush)FindResource("FocusRing") : Brushes.Transparent;
    }

    /// <summary>Current frame, flattened (used as a tileset source).</summary>
    public (uint[] Pixels, int Width, int Height) CurrentImage => (Compositor.Flatten(_sprite, _frame), _sprite.Width, _sprite.Height);

    // ---------- document ----------

    void LoadSprite(Sprite s, string? path)
    {
        _sprite = s;
        _path = path;
        _dirty = false;
        _history.Clear();
        _layer = Math.Min(_layer, s.Layers.Count - 1);
        _frame = 0;
        (_float, _sel, _selMask) = (null, null, null);
        RefreshAll();
    }

    /// <summary>Call before every edit.</summary>
    void Snapshot()
    {
        CommitFloat();
        _history.Push(_sprite);
        _dirty = true;
        UpdateTitle();
    }

    void RefreshAll()
    {
        _layer = Math.Clamp(_layer, 0, _sprite.Layers.Count - 1);
        _frame = Math.Clamp(_frame, 0, _sprite.Frames.Count - 1);
        ApplyZoom();
        RefreshPalette();
        RefreshLayers();
        RefreshOpacity();
        RefreshSwatches();
        RefreshFrames();
        RefreshTags();
        Render();
        UpdateTitle();
    }

    void Render()
    {
        int w = _sprite.Width, h = _sprite.Height;
        if (_bmp is null || _bmp.PixelWidth != w || _bmp.PixelHeight != h)
        {
            CanvasImage.Source = _bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            UpdateTiledBg();
        }

        var current = Compositor.Flatten(_sprite, _frame);
        var shown = current;
        if (OnionToggle.IsChecked == true && !_playTimer.IsEnabled && _sprite.Frames.Count > 1)
        {
            shown = new uint[w * h];
            if (_frame > 0) Compositor.Blend(shown, Compositor.Flatten(_sprite, _frame - 1), 0.3);
            if (_frame < _sprite.Frames.Count - 1) Compositor.Blend(shown, Compositor.Flatten(_sprite, _frame + 1), 0.15);
            Compositor.Blend(shown, current);
        }
        if (_float is not null) PixelOps.Stamp(shown, w, h, _float, _fw, _fh, _fx, _fy);
        _bmp.WritePixels(new Int32Rect(0, 0, w, h), shown, w * 4, 0);
        _preview?.ShowFrame(current, w, h);
        UpdateOverlay();

        if (FramesList.Items.Count > _frame && FramesList.Items[_frame] is FrameworkElement { Tag: Image thumb })
            thumb.Source = ImageIO.ToBitmap(current, w, h);
    }

    void UpdateTitle()
    {
        if (IsVisible && Window.GetWindow(this) is { } win)
            win.Title = $"PixelForge — {(_path is null ? "untitled" : System.IO.Path.GetFileName(_path))}{(_dirty ? " *" : "")}";
    }

    /// <summary>True if it is OK to drop the current sprite.</summary>
    public bool ConfirmDiscard()
    {
        if (!_dirty) return true;
        var r = MessageBox.Show("Save changes to the current sprite?", "PixelForge", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return r == MessageBoxResult.No || (r == MessageBoxResult.Yes && Save(_path));
    }

    // ---------- zoom / grid ----------

    void ApplyZoom()
    {
        LayoutCanvas();
        GridOverlay.Fill = GridToggle.IsChecked == true && _zoom >= 4 ? Ui.GridBrush(_zoom) : null;
        Status.Text = $"{_sprite.Width}×{_sprite.Height} px";
        ZoomText.Text = $"{_zoom * 100} %";
        UpdateOverlay();
    }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        int i = Array.IndexOf(ZoomLevels, _zoom) + Math.Sign(e.Delta);
        if (i >= 0 && i < ZoomLevels.Length) ZoomTo(ZoomLevels[i], e.GetPosition(CanvasHost));
    }

    /// <summary>Changes zoom and keeps the canvas point <paramref name="anchor"/> in place on screen.</summary>
    void ZoomTo(int zoom, Point anchor)
    {
        int old = _zoom;
        _zoom = zoom;
        ApplyZoom();
        Scroller.UpdateLayout();
        Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset + anchor.X / old * (_zoom - old));
        Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + anchor.Y / old * (_zoom - old));
    }

    void OnGridToggle(object sender, RoutedEventArgs e) => ApplyZoom();

    // ---------- palette / colors ----------

    void RefreshPalette()
    {
        PaletteWrap.Children.Clear();
        int count = _sprite.Palette.Count;
        for (int i = 0; i < count; i++)
        {
            uint c = _sprite.Palette[i];
            int index = i;
            // a button, so the palette works with the keyboard: Enter = left color, Shift+Enter = right color, Del = remove
            var sw = Ui.Swatch(c, 24, $"Color {Ui.ColorName(c)}, {i + 1} of {count}");
            sw.Click += (_, _) =>
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) RemoveColor(index);
                else { (_primary, _editSecondary) = (c, false); RefreshSwatches(); }
            };
            sw.MouseRightButtonUp += (_, e) => { (_secondary, _editSecondary) = (c, true); RefreshSwatches(); e.Handled = true; };
            sw.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Delete) { RemoveColor(index); e.Handled = true; }
                else if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { (_secondary, _editSecondary) = (c, true); RefreshSwatches(); e.Handled = true; }
            };
            PaletteWrap.Children.Add(sw);
        }
        var add = new Button { Content = "+", Width = 24, Height = 24, Margin = new Thickness(1), ToolTip = "Add the picker's color to the palette", Padding = new Thickness(0) };
        System.Windows.Automation.AutomationProperties.SetName(add, "Add the picker's color to the palette");
        add.Click += (_, _) =>
        {
            Snapshot();
            _sprite.Palette.Add(_editSecondary ? _secondary : _primary);   // the picker's color
            RefreshPalette();
        };
        PaletteWrap.Children.Add(add);
    }

    void OnPaletteMenu(object sender, RoutedEventArgs e)
    {
        PaletteMenuBtn.ContextMenu.PlacementTarget = PaletteMenuBtn;
        PaletteMenuBtn.ContextMenu.IsOpen = true;
    }

    const string PaletteFilter = "Palette (*.hex;*.gpl)|*.hex;*.gpl|Lospec hex (*.hex)|*.hex|GIMP palette (*.gpl)|*.gpl";

    void OnLoadPalette(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = PaletteFilter };
        if (dlg.ShowDialog() != true) return;
        try { SetPalette(PaletteIO.Load(dlg.FileName)); }
        catch (Exception ex) { Ui.Error(ex); }
    }

    void OnSavePalette(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = PaletteFilter, FileName = BaseName + ".hex" };
        if (dlg.ShowDialog() != true) return;
        try { PaletteIO.Save(dlg.FileName, _sprite.Palette); }
        catch (Exception ex) { Ui.Error(ex); }
    }

    void OnResetPalette(object sender, RoutedEventArgs e) => SetPalette(Sprite.DefaultPalette);

    void SetPalette(IEnumerable<uint> colors)
    {
        Snapshot();
        _sprite.Palette.Clear();
        _sprite.Palette.AddRange(colors);
        RefreshPalette();
    }

    void RemoveColor(int index)
    {
        Snapshot();
        _sprite.Palette.RemoveAt(index);
        RefreshPalette();
        if (PaletteWrap.Children.Count > 0)   // keep keyboard focus in the palette
            ((UIElement)PaletteWrap.Children[Math.Min(index, PaletteWrap.Children.Count - 1)]).Focus();
    }

    void RefreshSwatches()
    {
        PrimarySwatch.Background = Ui.BrushOf(_primary);
        SecondarySwatch.Background = Ui.BrushOf(_secondary);
        // the swatch the picker edits gets the accent ring
        PrimarySwatch.BorderBrush = _editSecondary ? Brushes.Gray : (Brush)FindResource("FocusRing");
        SecondarySwatch.BorderBrush = _editSecondary ? (Brush)FindResource("FocusRing") : Brushes.Gray;
        PrimarySwatch.BorderThickness = new Thickness(_editSecondary ? 1 : 3);
        SecondarySwatch.BorderThickness = new Thickness(_editSecondary ? 3 : 1);
        Picker.Color = _editSecondary ? _secondary : _primary;
        System.Windows.Automation.AutomationProperties.SetName(PrimarySwatch, $"Left mouse color {Ui.ColorName(_primary)}{(_editSecondary ? "" : ", edited by the color picker")}");
        System.Windows.Automation.AutomationProperties.SetName(SecondarySwatch, $"Right mouse color {Ui.ColorName(_secondary)}{(_editSecondary ? ", edited by the color picker" : "")}");
    }

    void OnPrimarySwatch(object sender, RoutedEventArgs e)
    {
        _editSecondary = false;
        RefreshSwatches();
        Picker.Focus();
    }

    void OnSecondarySwatch(object sender, RoutedEventArgs e)
    {
        _editSecondary = true;
        RefreshSwatches();
        Picker.Focus();
    }

    // ---------- layers (list shows top layer first) ----------

    bool _refreshingLayers;

    void RefreshLayers()
    {
        _refreshingLayers = true;
        Ui.FillLayers(LayersList, _sprite.Layers, _layer,
            visibilityChanged: () => { _dirty = true; UpdateTitle(); Render(); },
            rename: (layer, name) => { Snapshot(); layer.Name = name; });
        _refreshingLayers = false;
        RefreshOpacity();
    }

    void OnLayerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingLayers || LayersList.SelectedIndex < 0) return;
        CommitFloat();
        _layer = _sprite.Layers.Count - 1 - LayersList.SelectedIndex;
        RefreshOpacity();
    }

    void OnAddLayer(object sender, RoutedEventArgs e)
    {
        Snapshot();
        _sprite.AddLayer(++_layer, $"Layer {_sprite.Layers.Count + 1}");
        RefreshLayers();
    }

    void OnDeleteLayer(object sender, RoutedEventArgs e)
    {
        if (_sprite.Layers.Count == 1) return;
        Snapshot();
        _sprite.RemoveLayer(_layer);
        _layer = Math.Max(0, _layer - 1);
        RefreshLayers();
        Render();
    }

    void OnLayerUp(object sender, RoutedEventArgs e) => MoveLayer(+1);
    void OnLayerDown(object sender, RoutedEventArgs e) => MoveLayer(-1);

    void MoveLayer(int delta)
    {
        int to = _layer + delta;
        if (to < 0 || to >= _sprite.Layers.Count) return;
        Snapshot();
        _sprite.MoveLayer(_layer, to);
        _layer = to;
        RefreshLayers();
        Render();
    }

    // ---------- frames / animation ----------

    bool _refreshingFrames;
    readonly System.Windows.Threading.DispatcherTimer _playTimer = new();

    void RefreshFrames()
    {
        _refreshingFrames = true;
        FramesList.Items.Clear();
        for (int f = 0; f < _sprite.Frames.Count; f++)
        {
            var thumb = new Image { Width = 48, Height = 48, Stretch = Stretch.Uniform, Source = ImageIO.ToBitmap(Compositor.Flatten(_sprite, f), _sprite.Width, _sprite.Height) };
            RenderOptions.SetBitmapScalingMode(thumb, BitmapScalingMode.NearestNeighbor);
            System.Windows.Automation.AutomationProperties.SetName(thumb, $"Frame {f + 1}, {_sprite.Frames[f].DurationMs} ms");
            FramesList.Items.Add(new StackPanel
            {
                Tag = thumb,
                Children = { new Border { Background = Brushes.LightGray, Child = thumb }, new TextBlock { Text = $"{f + 1}", HorizontalAlignment = HorizontalAlignment.Center } },
            });
        }
        FramesList.SelectedIndex = _frame;
        DurationBox.Text = _sprite.Frames[_frame].DurationMs.ToString();
        Ui.MarkInvalid(DurationBox, null);
        _refreshingFrames = false;
    }

    void SelectFrame(int f)
    {
        CommitFloat();
        _frame = f;
        _refreshingFrames = true;
        FramesList.SelectedIndex = f;
        _refreshingFrames = false;
        DurationBox.Text = _sprite.Frames[f].DurationMs.ToString();
        Ui.MarkInvalid(DurationBox, null);
        Render();
    }

    void OnFrameSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshingFrames && FramesList.SelectedIndex >= 0) SelectFrame(FramesList.SelectedIndex);
    }

    void EditFrames(Action change)
    {
        StopPlaying();
        Snapshot();
        change();
        _frame = Math.Clamp(_frame, 0, _sprite.Frames.Count - 1);
        RefreshFrames();
        RefreshTags();   // frame edits move tag ranges
        Render();
    }

    void OnAddFrame(object sender, RoutedEventArgs e) => EditFrames(() => _sprite.AddFrame(++_frame));
    void OnDuplicateFrame(object sender, RoutedEventArgs e) => EditFrames(() => _sprite.AddFrame(_frame + 1, copyFrom: _frame++));
    void OnDeleteFrame(object sender, RoutedEventArgs e) { if (_sprite.Frames.Count > 1) EditFrames(() => _sprite.RemoveFrame(_frame)); }
    void OnFrameLeft(object sender, RoutedEventArgs e) { if (_frame > 0) EditFrames(() => _sprite.MoveFrame(_frame, --_frame)); }
    void OnFrameRight(object sender, RoutedEventArgs e) { if (_frame < _sprite.Frames.Count - 1) EditFrames(() => _sprite.MoveFrame(_frame, ++_frame)); }

    void OnDurationKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnDurationCommit(sender, e);
    }

    void OnDurationCommit(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(DurationBox.Text, out int ms) || ms < 10 || ms > 60000)
        {
            Ui.MarkInvalid(DurationBox, "Frame duration must be a whole number of milliseconds from 10 to 60000. Not applied.");
            return;
        }
        Ui.MarkInvalid(DurationBox, null);
        if (ms == _sprite.Frames[_frame].DurationMs) return;
        Snapshot();
        _sprite.Frames[_frame].DurationMs = ms;
    }

    void OnOnionToggle(object sender, RoutedEventArgs e) => Render();

    void OnPlay(object? sender, RoutedEventArgs? e)
    {
        if (_playTimer.IsEnabled) { StopPlaying(); return; }
        if (_sprite.Frames.Count < 2) { PlayBtn.IsChecked = false; return; }
        var (from, to) = PlayRange;
        if (_frame < from || _frame > to) SelectFrame(from);
        _playTimer.Tick -= OnPlayTick;
        _playTimer.Tick += OnPlayTick;
        _playTimer.Interval = TimeSpan.FromMilliseconds(_sprite.Frames[_frame].DurationMs);
        _playTimer.Start();
        PlayBtn.IsChecked = true;
        PlayBtn.Content = "■ Stop";
        Render();
    }

    void OnPlayTick(object? sender, EventArgs e)
    {
        var (from, to) = PlayRange;
        SelectFrame(_frame + 1 > to || _frame < from ? from : _frame + 1);
        _playTimer.Interval = TimeSpan.FromMilliseconds(_sprite.Frames[_frame].DurationMs);
    }

    void StopPlaying()
    {
        if (!_playTimer.IsEnabled) return;
        _playTimer.Stop();
        PlayBtn.IsChecked = false;
        PlayBtn.Content = "▶ Play";
        Render();
    }

    string BaseName => System.IO.Path.GetFileNameWithoutExtension(_path) ?? "sprite";

    void OnExportGif(object sender, RoutedEventArgs e)
    {
        CommitFloat();
        var dlg = new SaveFileDialog { Filter = "Animated GIF (*.gif)|*.gif", FileName = BaseName + (ActiveTag is { } tag ? "-" + tag.Name : "") + ".gif" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var (from, to) = PlayRange;   // the chosen tag only, or all frames
            var range = Enumerable.Range(from, to - from + 1).ToList();
            GifWriter.Save(dlg.FileName, [.. range.Select(f => Compositor.Flatten(_sprite, f))], [.. range.Select(f => _sprite.Frames[f].DurationMs)], _sprite.Width, _sprite.Height);
        }
        catch (Exception ex) { Ui.Error(ex); }
    }

    void OnExportSheet(object sender, RoutedEventArgs e)
    {
        CommitFloat();
        if (Ui.Ask(this, "Export sprite sheet", "Columns (0 = all frames in one row):", "0") is not string text) return;
        if (!int.TryParse(text, out int cols) || cols < 0) { Ui.Error(new FormatException("Columns must be a whole number ≥ 0.")); return; }
        var dlg = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = BaseName + "-sheet.png" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var sheet = SpriteSheet.Build(_sprite, cols);
            ImageIO.SavePng(dlg.FileName, sheet.Pixels, sheet.Width, sheet.Height);
            File.WriteAllText(System.IO.Path.ChangeExtension(dlg.FileName, ".json"),
                SpriteSheet.Json(_sprite, sheet, System.IO.Path.GetFileName(dlg.FileName), BaseName));
        }
        catch (Exception ex) { Ui.Error(ex); }
    }

    // ---------- undo ----------

    void OnUndo(object? sender, RoutedEventArgs? e) => Restore(_history.Undo(_sprite));
    void OnRedo(object? sender, RoutedEventArgs? e) => Restore(_history.Redo(_sprite));

    void Restore(Sprite? s)
    {
        if (s is null || _stroking) return;
        _sprite = s;
        (_float, _sel, _selMask) = (null, null, null);
        _dirty = true;
        RefreshAll();
    }

    // ---------- files ----------

    const string ProjectFilter = "PixelForge sprite (*.pxf)|*.pxf";

    void OnNew(object? sender, RoutedEventArgs? e)
    {
        if (!ConfirmDiscard()) return;
        if (Ui.AskSize(this, "New sprite", _sprite.Width, _sprite.Height, anchor: false) is not (int w, int h, _, _)) return;
        LoadSprite(new Sprite(w, h), null);
    }

    void OnCanvasSize(object sender, RoutedEventArgs e)
    {
        CommitFloat();
        if (Ui.AskSize(this, "Canvas size", _sprite.Width, _sprite.Height, anchor: true) is not (int w, int h, int ax, int ay)) return;
        Resize(w, h, PixelOps.AnchorOffset(_sprite.Width, w, ax), PixelOps.AnchorOffset(_sprite.Height, h, ay));
    }

    void OnCrop(object sender, RoutedEventArgs e)
    {
        CommitFloat();
        if (_sel is not { } s) { Ui.Error(new InvalidOperationException("Select an area first (Select tool, M).")); return; }
        Resize(s.Width, s.Height, -s.X, -s.Y);
    }

    void Resize(int w, int h, int ox, int oy)
    {
        Snapshot();
        _sprite = PixelOps.Resized(_sprite, w, h, ox, oy);
        (_sel, _selMask) = (null, null);
        RefreshAll();
    }

    void OnOpen(object? sender, RoutedEventArgs? e)
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog { Filter = ProjectFilter + "|Images (*.png;*.gif;*.bmp;*.jpg)|*.png;*.gif;*.bmp;*.jpg|All files|*.*" };
        if (dlg.ShowDialog() == true) OpenPath(dlg.FileName, confirmed: true);
    }

    public string[] FileExtensions => [".pxf", ".png", ".gif", ".bmp", ".jpg"];
    public bool IsDirty => _dirty;
    public string? CurrentPath => _path;

    public void OpenPath(string path, bool confirmed = false)
    {
        if (!confirmed && !ConfirmDiscard()) return;
        try
        {
            if (path.EndsWith(".pxf", StringComparison.OrdinalIgnoreCase))
            {
                LoadSprite(ProjectFile.Load(path), path);
            }
            else
            {
                var (px, w, h) = ImageIO.LoadImage(path);
                var s = new Sprite(w, h);
                px.CopyTo(s.Cel(0, 0), 0);
                LoadSprite(s, null);   // imported image: Save asks for a .pxf name
            }
            App.AddRecent(path);
        }
        catch (Exception ex) { Ui.Error(ex); }
    }

    public void WriteAutosave(string file)
    {
        var s = _sprite.Clone();
        if (_float is not null && _floatLayer < s.Layers.Count && _floatFrame < s.Frames.Count)   // include pixels being moved
            PixelOps.Stamp(s.Cel(_floatLayer, _floatFrame), s.Width, s.Height, _float, _fw, _fh, _fx, _fy);
        ProjectFile.Save(s, file);
    }

    public void RestoreAutosave(string file, string? originalPath)
    {
        LoadSprite(ProjectFile.Load(file), originalPath);
        _dirty = true;
        UpdateTitle();
    }

    void OnSave(object? sender, RoutedEventArgs? e) => Save(_path);
    void OnSaveAs(object sender, RoutedEventArgs e) => Save(null);

    bool Save(string? path)
    {
        CommitFloat();
        if (path is null)
        {
            var dlg = new SaveFileDialog { Filter = ProjectFilter, FileName = "sprite.pxf" };
            if (dlg.ShowDialog() != true) return false;
            path = dlg.FileName;
        }
        try
        {
            ProjectFile.Save(_sprite, path);
            App.AddRecent(path);
            _path = path;
            _dirty = false;
            UpdateTitle();
            return true;
        }
        catch (Exception ex) { Ui.Error(ex); return false; }
    }

    void OnExportPng(object sender, RoutedEventArgs e)
    {
        CommitFloat();
        var dlg = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = System.IO.Path.ChangeExtension(System.IO.Path.GetFileName(_path) ?? "sprite", ".png") };
        if (dlg.ShowDialog() != true) return;
        try { ImageIO.SavePng(dlg.FileName, Compositor.Flatten(_sprite, _frame), _sprite.Width, _sprite.Height); }
        catch (Exception ex) { Ui.Error(ex); }
    }

    // ---------- keyboard (routed from MainWindow) ----------

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
            case Key.OemPlus or Key.Add when ctrl: ZoomStep(+1); break;
            case Key.OemMinus or Key.Subtract when ctrl: ZoomStep(-1); break;
            case Key.D0 or Key.NumPad0 when ctrl: ZoomFit(); break;
            case Key.N when ctrl: OnNew(null, null); break;
            case Key.O when ctrl: OnOpen(null, null); break;
            case Key.G when ctrl && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift): TileGridToggle.IsChecked = TileGridToggle.IsChecked != true; ApplyZoom(); break;
            case Key.G when ctrl: GridToggle.IsChecked = GridToggle.IsChecked != true; ApplyZoom(); break;
            case Key.T when !ctrl: TiledToggle.IsChecked = !Tiled; OnTiledToggle(this, new RoutedEventArgs()); break;
            case Key.P when !ctrl: TogglePreview(); break;
            case Key.B when !ctrl: SetTool(Tool.Pencil); break;
            case Key.E when !ctrl: SetTool(Tool.Eraser); break;
            case Key.G when !ctrl: SetTool(Tool.Fill); break;
            case Key.I when !ctrl: SetTool(Tool.Picker); break;
            case Key.L when !ctrl: SetTool(Tool.Line); break;
            case Key.U when !ctrl: SetTool(Tool.Rect); break;
            case Key.O when !ctrl: SetTool(Tool.Ellipse); break;
            case Key.M when !ctrl: SetTool(Tool.Select); break;
            case Key.W when !ctrl: SetTool(Tool.Wand); break;
            case Key.OemOpenBrackets: BrushSize--; break;
            case Key.OemCloseBrackets: BrushSize++; break;
            case Key.A when ctrl: SelectAll(); break;
            case Key.C when ctrl: Copy(); break;
            case Key.X when ctrl: Cut(); break;
            case Key.V when ctrl: Paste(); break;
            case Key.Delete: DeleteSelection(); break;
            case Key.H when !ctrl: TransformFloat(PixelOps.FlipH); break;
            case Key.V when !ctrl: TransformFloat(PixelOps.FlipV); break;
            case Key.R when !ctrl: TransformFloat(PixelOps.Rotate90, swapsSize: true); break;
            case Key.Enter: CommitFloat(); break;
            case Key.Escape: CancelFloat(); break;
            case Key.Space: OnPlay(null, null); break;
            case Key.Left: StopPlaying(); SelectFrame((_frame + _sprite.Frames.Count - 1) % _sprite.Frames.Count); break;
            case Key.Right: StopPlaying(); SelectFrame((_frame + 1) % _sprite.Frames.Count); break;
            default: e.Handled = false; break;
        }
    }
}
