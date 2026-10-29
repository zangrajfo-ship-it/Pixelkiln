using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Pixelkiln.Core;

namespace Pixelkiln.App;

/// <summary>
/// Texture mode (Sprite tab): seamless materials from a seeded recipe, a painting or a picture, with height, normal
/// and occlusion maps, a repeat preview and a lit preview. Maps are rebuilt in the background after each change.
/// </summary>
public partial class TextureEditor : UserControl, IEditor, IPictureSource
{
    TextureDoc _doc = new();
    readonly History<TextureDoc> _history = new(t => t.Clone(), App.Settings.UndoLimit);
    string? _path;
    bool _dirty, _refreshing = true, _sliderSnapshot;
    TextureMaps? _maps;
    int _buildVersion;
    readonly DispatcherTimer _rebuild = new() { Interval = TimeSpan.FromMilliseconds(120) };

    public TextureEditor()
    {
        InitializeComponent();
        SourceBox.ItemsSource = Enum.GetNames<TextureSource>();
        MaterialBox.ItemsSource = Enum.GetNames<Material>();
        SizeBox.ItemsSource = new[] { "64", "128", "256", "512", "1024", "2048" };
        Picker.ColorChanged += OnPickerColor;
        foreach (var s in new[] { ScaleSlider, DetailSlider, SeamSlider, NormalSlider, AoSlider })
        {
            s.ValueChanged += OnSlider;
            s.PreviewMouseUp += (_, _) => _sliderSnapshot = false;
            s.KeyUp += (_, _) => _sliderSnapshot = false;
        }
        LightSlider.ValueChanged += (_, _) => { if (ViewLit.IsChecked == true) ShowMap(); };
        _rebuild.Tick += (_, _) => { _rebuild.Stop(); Build(); };
        IsVisibleChanged += (_, _) =>
        {
            UpdateTitle();
            if (IsVisible && _doc.Source != TextureSource.Procedural) Build();   // the painting may have changed in Paint mode
        };
        App.ProjectChanged += () => { if (IsLoaded) RefreshAssets(); };
        Loaded += Init;
        void Init(object s, RoutedEventArgs e) { Loaded -= Init; LoadDoc(_doc, null); }
    }

    // ---------- document ----------

    void LoadDoc(TextureDoc t, string? path)
    {
        (_doc, _path, _dirty) = (t, path, false);
        _history.Clear();
        RefreshFields();
        Build();
        UpdateTitle();
    }

    void Snapshot()
    {
        _history.Push(_doc);
        _dirty = true;
        UpdateTitle();
    }

    void UpdateTitle() => Ui.ShowTitle(this, _path, _dirty, "untitled texture");

    void RefreshAssets()
    {
        _refreshing = true;
        var items = App.Project?.Assets.Where(a => a.Type is AssetType.Paint or AssetType.Image or AssetType.Sprite or AssetType.Vector)
            .OrderBy(a => a.Type != AssetType.Paint).ThenBy(a => a.Path).Select(a => new ComboBoxItem { Content = $"{a.Type} · {a.Path}", Tag = a.Id }).ToList();
        AssetBox.ItemsSource = items;
        AssetBox.SelectedItem = items?.FirstOrDefault(i => (Guid)i.Tag == _doc.AssetId);
        _refreshing = false;
    }

    void RefreshFields()
    {
        _refreshing = true;
        var t = _doc;
        SourceBox.SelectedItem = t.Source.ToString();
        MaterialBox.SelectedItem = t.Material.ToString();
        SeedBox.Text = t.Seed;
        SizeBox.SelectedItem = t.Size.ToString();
        (ScaleSlider.Value, DetailSlider.Value, SeamSlider.Value, NormalSlider.Value, AoSlider.Value) = (t.Scale, t.Detail, t.SeamBlend, t.NormalStrength, t.OcclusionStrength);
        FlipGreenBox.IsChecked = t.FlipGreen;
        Picker.Color = EditLight.IsChecked == true ? t.Light : t.Dark;
        bool procedural = t.Source == TextureSource.Procedural;
        ProceduralPanel.Visibility = procedural ? Visibility.Visible : Visibility.Collapsed;
        AssetPanel.Visibility = procedural ? Visibility.Collapsed : Visibility.Visible;
        PaintButton.IsEnabled = !procedural || App.Project is not null;
        Labels();
        _refreshing = false;
        RefreshAssets();
    }

    void Labels()
    {
        ScaleLabel.Text = $"Features per tile: {ScaleSlider.Value:0}";
        DetailLabel.Text = $"Detail: {DetailSlider.Value:0} octave{(DetailSlider.Value == 1 ? "" : "s")}";
        SeamLabel.Text = SeamSlider.Value == 0 ? "Seam blend: off (the picture already tiles)" : $"Seam blend: {SeamSlider.Value:0} px";
        NormalLabel.Text = $"Normal strength: {NormalSlider.Value:0.##}";
        AoLabel.Text = $"Occlusion strength: {AoSlider.Value:0.##}";
    }

    // ---------- edits ----------

    void OnFieldChanged(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        var source = Enum.Parse<TextureSource>((string)SourceBox.SelectedItem);
        var material = Enum.Parse<Material>((string)MaterialBox.SelectedItem);
        int size = int.Parse((string)SizeBox.SelectedItem);
        Guid? asset = (AssetBox.SelectedItem as ComboBoxItem)?.Tag as Guid?;
        string seed = SeedBox.Text.Trim();
        bool flip = FlipGreenBox.IsChecked == true;
        if ((source, material, size, asset, seed, flip) == (_doc.Source, _doc.Material, _doc.Size, _doc.AssetId, _doc.Seed, _doc.FlipGreen)) return;
        Snapshot();
        (_doc.Source, _doc.Material, _doc.Size, _doc.AssetId, _doc.Seed, _doc.FlipGreen) = (source, material, size, source == TextureSource.Procedural ? _doc.AssetId : asset, seed, flip);
        RefreshFields();
        Schedule();
    }

    void OnSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_refreshing) return;
        if (!_sliderSnapshot) { Snapshot(); _sliderSnapshot = true; }   // one undo step per drag or key run
        (_doc.Scale, _doc.Detail, _doc.SeamBlend, _doc.NormalStrength, _doc.OcclusionStrength) =
            (ScaleSlider.Value, (int)DetailSlider.Value, (int)SeamSlider.Value, Math.Round(NormalSlider.Value, 2), Math.Round(AoSlider.Value, 2));
        Labels();
        Schedule();
    }

    void OnColorTarget(object sender, RoutedEventArgs e)
    {
        _refreshing = true;
        Picker.Color = EditLight.IsChecked == true ? _doc.Light : _doc.Dark;
        _refreshing = false;
    }

    void OnPickerColor(uint c)
    {
        if (_refreshing) return;
        if (!_sliderSnapshot) { Snapshot(); _sliderSnapshot = true; }
        if (EditLight.IsChecked == true) _doc.Light = c | 0xFF000000; else _doc.Dark = c | 0xFF000000;
        Schedule();
    }

    void OnShuffle(object sender, RoutedEventArgs e)
    {
        SeedBox.Text = Random.Shared.Next(100000).ToString();
        OnFieldChanged(sender, e);
    }

    // ---------- building and showing maps ----------

    void Schedule()
    {
        _rebuild.Stop();
        _rebuild.Start();
    }

    async void Build()
    {
        int version = ++_buildVersion;
        var doc = _doc.Clone();
        var project = App.Project;
        Status.Text = "Building…";
        try
        {
            var maps = await Task.Run(() => Textures.Build(doc, project));
            if (version != _buildVersion) return;   // a newer change is already on its way
            _maps = maps;
            int seam = Textures.SeamError(maps.Albedo, maps.Width, maps.Height);
            Status.Text = $"{maps.Width}×{maps.Height} px · " + (seam <= 24 ? "tiles without a visible seam" : $"edges differ by up to {seam} (use Seam blend)");
            ShowMap();
        }
        catch (InvalidDataException ex)
        {
            if (version != _buildVersion) return;
            _maps = null;
            PreviewRect.Fill = null;
            Status.Text = ex.Message;
        }
    }

    void OnViewChanged(object sender, RoutedEventArgs e) => ShowMap();

    /// <summary>Chooses a view from the menu or a key (the radio buttons only report clicks, so the map is redrawn here).</summary>
    void ShowView(RadioButton view)
    {
        view.IsChecked = true;
        ShowMap();
    }
    void OnPreviewSize(object sender, SizeChangedEventArgs e) => ShowMap();

    void ShowMap()
    {
        if (_maps is not { } m) return;
        uint[] px = ViewHeight.IsChecked == true ? Textures.Gray(m.HeightMap)
            : ViewNormal.IsChecked == true ? m.Normal
            : ViewAo.IsChecked == true ? m.Occlusion
            : ViewLit.IsChecked == true ? Textures.Lit(m, LightSlider.Value)
            : m.Albedo;
        var bmp = ImageIO.ToBitmap(px, m.Width, m.Height);
        bmp.Freeze();
        int times = RepeatBox.IsChecked == true ? 4 : 1;
        var brush = new ImageBrush(bmp) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 1.0 / times, 1.0 / times), ViewportUnits = BrushMappingMode.RelativeToBoundingBox, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(brush, m.Width * times < 512 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        PreviewRect.Fill = brush;
        double side = Math.Min(((FrameworkElement)PreviewRect.Parent).ActualWidth, ((FrameworkElement)PreviewRect.Parent).ActualHeight) - 16;
        PreviewRect.Width = PreviewRect.Height = Math.Max(64, side);
    }

    public (uint[] Pixels, int Width, int Height)? CurrentPicture => _maps is { } m ? (m.Albedo, m.Width, m.Height) : null;

    // ---------- painting it ----------

    /// <summary>Set by MainWindow: opens a painting in Paint mode with wrap-around strokes.</summary>
    public Action<AssetEntry>? OpenPainting { get; set; }

    void OnPaint(object sender, RoutedEventArgs e)
    {
        if (App.Project is not { } p) { Ui.Error(new InvalidOperationException("Open a project first: the texture links to a painting in it.")); return; }
        if (_doc.Source != TextureSource.Procedural && _doc.AssetId is Guid id && p.Find(id) is { Type: AssetType.Paint } existing)
        {
            OpenPainting?.Invoke(existing);
            return;
        }
        // a new painting the size of the texture, starting from the current colours, linked to this texture
        string dir = System.IO.Path.Combine(p.Root, "textures");
        Directory.CreateDirectory(dir);
        string name = System.IO.Path.GetFileNameWithoutExtension(_path ?? "texture");
        string file = System.IO.Path.Combine(dir, name + ".pxpaint");
        for (int n = 2; File.Exists(file); n++) file = System.IO.Path.Combine(dir, $"{name} {n}.pxpaint");
        var paint = new PaintDoc(_maps?.Width ?? _doc.Size, _maps?.Height ?? _doc.Size);
        if (_maps is { } m) paint.Layers.Insert(0, PaintOps.LayerFrom(paint, m.Albedo, m.Width, m.Height, "Start"));
        PaintDoc.Save(paint, file);
        var asset = p.Add(file);
        App.NotifyProjectChanged();
        Snapshot();
        (_doc.Source, _doc.AssetId) = (TextureSource.Painting, asset.Id);
        RefreshFields();
        Build();
        OpenPainting?.Invoke(asset);
    }

    // ---------- undo ----------

    void OnUndo(object? sender, RoutedEventArgs? e) => Restore(_history.Undo(_doc));
    void OnRedo(object? sender, RoutedEventArgs? e) => Restore(_history.Redo(_doc));

    void Restore(TextureDoc? t)
    {
        if (t is null) return;
        _doc = t;
        _dirty = true;
        RefreshFields();
        Build();
        UpdateTitle();
    }

    // ---------- files ----------

    const string Filter = "Pixelkiln texture (*.pxtex)|*.pxtex";

    void OnNew(object? sender, RoutedEventArgs? e)
    {
        if (ConfirmDiscard()) LoadDoc(new TextureDoc(), null);
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
            var dlg = new SaveFileDialog { Filter = Filter, FileName = $"{_doc.Material.ToString().ToLowerInvariant()}.pxtex", InitialDirectory = App.Project?.Root ?? "" };
            if (dlg.ShowDialog() != true) return false;
            path = dlg.FileName;
        }
        try
        {
            TextureDoc.Save(_doc, path);
            (_path, _dirty) = (path, false);
            App.AddRecent(path);
            UpdateTitle();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); return false; }
    }

    void OnExport(object? sender, RoutedEventArgs? e)
    {
        if (_maps is not { } m) { Ui.Error(new InvalidOperationException("The texture has no maps yet: " + Status.Text)); return; }
        var dlg = new SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = System.IO.Path.ChangeExtension(System.IO.Path.GetFileName(_path) ?? "texture", ".png") };
        if (dlg.ShowDialog() != true) return;
        string Beside(string suffix) => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dlg.FileName)!, System.IO.Path.GetFileNameWithoutExtension(dlg.FileName) + suffix + ".png");
        try
        {
            ImageIO.SavePng(dlg.FileName, m.Albedo, m.Width, m.Height);
            ImageIO.SavePng(Beside("_normal"), m.Normal, m.Width, m.Height);
            ImageIO.SavePng(Beside("_height"), Textures.Gray(m.HeightMap), m.Width, m.Height);
            ImageIO.SavePng(Beside("_ao"), m.Occlusion, m.Width, m.Height);
            Status.Text = $"Exported 4 maps next to {System.IO.Path.GetFileName(dlg.FileName)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); }
    }

    // ---------- IEditor ----------

    public string[] FileExtensions => [".pxtex"];
    public bool IsDirty => _dirty;
    public string? CurrentPath => _path;

    public bool ConfirmDiscard() => Ui.ConfirmDiscard(_dirty, "texture", () => Save(_path));

    public void OpenPath(string path, bool confirmed = false)
    {
        if (!confirmed && !ConfirmDiscard()) return;
        try { LoadDoc(TextureDoc.Load(path), path); App.AddRecent(path); }
        catch (Exception ex) when (ex is InvalidDataException or IOException) { Ui.Error(ex); }
    }

    public void OnFileMoved(string oldPath, string newPath)
    {
        if (string.Equals(_path, oldPath, StringComparison.OrdinalIgnoreCase)) { _path = newPath; UpdateTitle(); }
    }

    public void WriteAutosave(string file) => TextureDoc.Save(_doc, file);

    public void RestoreAutosave(string file, string? originalPath)
    {
        LoadDoc(TextureDoc.Load(file), originalPath);
        _dirty = true;
        UpdateTitle();
    }

    IReadOnlyList<MenuItem>? _menus;
    Action Do(RoutedEventHandler h) => () => h(this, new RoutedEventArgs());

    public IReadOnlyList<MenuItem> Menus => _menus ??=
    [
        Ui.Menu("_File",
            Ui.Item("_New texture", "Ctrl+N", Do(OnNew)),
            Ui.Item("_Open…", "Ctrl+O", Do(OnOpen)),
            Ui.RecentMenu(this),
            Ui.Item("_Save", "Ctrl+S", Do(OnSave)),
            Ui.Item("Save _as…", "Ctrl+Shift+S", () => Save(null)),
            new Separator(),
            Ui.Item("_Export maps…", "Ctrl+E", Do(OnExport)),
            Ui.Item("_Paint it…", null, Do(OnPaint)),
            new Separator(),
            Ui.Item("E_xit", "Alt+F4", () => Window.GetWindow(this)?.Close())),
        Ui.Menu("_Edit",
            Ui.Item("_Undo", "Ctrl+Z", Do(OnUndo)),
            Ui.Item("_Redo", "Ctrl+Y", Do(OnRedo)),
            Ui.Item("_Shuffle seed", "R", Do(OnShuffle))),
        Ui.Menu("_View",
            Ui.Item("_Colour", "1", () => ShowView(ViewAlbedo)),
            Ui.Item("_Height", "2", () => ShowView(ViewHeight)),
            Ui.Item("_Normal", "3", () => ShowView(ViewNormal)),
            Ui.Item("_Occlusion", "4", () => ShowView(ViewAo)),
            Ui.Item("_Lit", "5", () => ShowView(ViewLit)),
            Ui.Toggle("_Repeat 4×4", "T", RepeatBox)),
    ];

    public void HandleKey(KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Z when ctrl: OnUndo(null, null); break;
            case Key.Y when ctrl: OnRedo(null, null); break;
            case Key.S when ctrl && shift: Save(null); break;
            case Key.S when ctrl: OnSave(null, null); break;
            case Key.N when ctrl: OnNew(null, null); break;
            case Key.O when ctrl: OnOpen(null, null); break;
            case Key.E when ctrl: OnExport(null, null); break;
            case Key.R when !ctrl: OnShuffle(this, new RoutedEventArgs()); break;
            case Key.D1 when !ctrl: ShowView(ViewAlbedo); break;
            case Key.D2 when !ctrl: ShowView(ViewHeight); break;
            case Key.D3 when !ctrl: ShowView(ViewNormal); break;
            case Key.D4 when !ctrl: ShowView(ViewAo); break;
            case Key.D5 when !ctrl: ShowView(ViewLit); break;
            case Key.T when !ctrl: RepeatBox.IsChecked = RepeatBox.IsChecked != true; ShowMap(); break;
            default: e.Handled = false; break;
        }
    }
}
