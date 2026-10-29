using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Pixelkiln.Core;

namespace Pixelkiln.App;

/// <summary>
/// Start screen (.ai/context/design.md, Home): a new sprite from a size preset on the left, recent files as
/// cards on the right. It only collects the choice; MainWindow does the work through the actions below.
/// </summary>
public partial class HomeView : UserControl
{
    static readonly (int W, int H, string Sub)[] Presets =
        [(16, 16, "Icon"), (32, 32, "Character"), (64, 64, "Large sprite"), (128, 128, "Tileset"), (320, 180, "Scene 16:9"), (256, 256, "Canvas")];

    public Action<int, int>? CreateSprite;
    public Action<string>? OpenFile;
    public Action? OpenFileDialog, OpenProject, NewProject, OpenSample, Learn, Closed;

    bool _syncing;

    public HomeView()
    {
        InitializeComponent();
        VersionText.Text = "Version " + (typeof(HomeView).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
        foreach (var (w, h, sub) in Presets) PresetGrid.Children.Add(PresetButton(w, h, sub));
        SetSize(App.Settings.DefaultSpriteWidth, App.Settings.DefaultSpriteHeight);
        IsVisibleChanged += (_, _) => { if (IsVisible) { RefreshRecent(); Dispatcher.BeginInvoke(() => WidthBox.Focus()); } };
    }

    // ---------- new sprite ----------

    RadioButton PresetButton(int w, int h, string sub)
    {
        // a small box with the preset's proportions
        double r = (double)w / h, bw = r >= 1 ? 22 : 22 * r, bh = r >= 1 ? 22 / r : 22;
        var box = new Border { Width = bw, Height = bh, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
        box.SetResourceReference(Border.BorderBrushProperty, "Text3Brush");
        var label = new TextBlock { Text = $"{w}×{h}", FontWeight = FontWeights.Medium, HorizontalAlignment = HorizontalAlignment.Center };
        label.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        var subText = new TextBlock { Text = sub, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
        subText.SetResourceReference(TextBlock.ForegroundProperty, "Text3Brush");
        var b = new RadioButton
        {
            GroupName = "preset", Tag = (w, h), Margin = new Thickness(4), Height = 84,
            Content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new Grid { Height = 22, Margin = new Thickness(0, 0, 0, 8), Children = { box } }, label, subText } },
            Template = (ControlTemplate)FindResource("PresetTemplate"),
        };
        System.Windows.Automation.AutomationProperties.SetName(b, $"{w} by {h} pixels, {sub}");
        b.Checked += (_, _) => { if (!_syncing) SetSize(w, h); };
        b.MouseDoubleClick += (_, _) => OnCreate(b, new RoutedEventArgs());
        return b;
    }

    void SetSize(int w, int h)
    {
        _syncing = true;
        WidthBox.Text = w.ToString();
        HeightBox.Text = h.ToString();
        _syncing = false;
        SyncPresets();
    }

    (int W, int H)? TypedSize() =>
        int.TryParse(WidthBox.Text, out int w) && int.TryParse(HeightBox.Text, out int h) && w is >= 1 and <= 4096 && h is >= 1 and <= 4096 ? (w, h) : null;

    void OnSizeTyped(object sender, TextChangedEventArgs e)
    {
        if (!_syncing && CreateText is not null) SyncPresets();   // TextChanged already fires inside InitializeComponent, before CreateText exists
    }

    /// <summary>The preset matching the typed size is shown as chosen; the button says what will be made.</summary>
    void SyncPresets()
    {
        var size = TypedSize();
        _syncing = true;
        foreach (RadioButton b in PresetGrid.Children) b.IsChecked = size is { } s && ((int, int))b.Tag == (s.W, s.H);
        _syncing = false;
        CreateBtn.IsEnabled = size is not null;
        CreateText.Text = size is { } t ? $"Create {t.W} × {t.H} sprite" : "Width and height: 1 to 4096";
    }

    void OnSwap(object sender, RoutedEventArgs e) => (WidthBox.Text, HeightBox.Text) = (HeightBox.Text, WidthBox.Text);

    void OnCreate(object sender, RoutedEventArgs e)
    {
        if (TypedSize() is not { } s) return;
        Hide();
        CreateSprite?.Invoke(s.W, s.H);
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
        else if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control) { OnOpenFile(this, e); e.Handled = true; }
        else if (e.Key == Key.Enter && e.OriginalSource is TextBox t && t != SearchBox) { OnCreate(this, e); e.Handled = true; }
    }

    // ---------- more ways to start ----------

    void Run(Action? a) { Hide(); a?.Invoke(); }
    void OnOpenFile(object sender, RoutedEventArgs e) => Run(OpenFileDialog);
    void OnOpenProject(object sender, RoutedEventArgs e) => Run(OpenProject);
    void OnNewProject(object sender, RoutedEventArgs e) => Run(NewProject);
    void OnSample(object sender, RoutedEventArgs e) => Run(OpenSample);
    void OnLearn(object sender, RoutedEventArgs e) => Run(Learn);
    void OnClose(object sender, RoutedEventArgs e) => Hide();

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        Closed?.Invoke();
    }

    // ---------- recent files ----------

    /// <summary>File types this version edits (sprites and images, characters, tilemaps, projects) and their names on the cards.</summary>
    static readonly Dictionary<string, string> Kinds = new()
    {
        [".pxf"] = "Sprite", [".png"] = "Image", [".pxchar"] = "Character", [".pxm"] = "Tilemap", [Project.Extension] = "Project",
    };

    void OnSearch(object sender, TextChangedEventArgs e) => RefreshRecent();

    void RefreshRecent()
    {
        RecentPanel.Children.Clear();
        string q = SearchBox.Text.Trim();
        var files = App.Settings.RecentFiles
            .Where(f => Kinds.ContainsKey(System.IO.Path.GetExtension(f).ToLowerInvariant()) && File.Exists(f))
            .Where(f => q.Length == 0 || System.IO.Path.GetFileName(f).Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var f in files) RecentPanel.Children.Add(Card(f));
        EmptyText.Visibility = files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = q.Length > 0 ? $"No recent files match “{q}”."
            : "Files you open or save appear here. Start with a new sprite on the left, or open the sample game.";
    }

    Button Card(string path)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        bool isProject = ext == Project.Extension;
        var type = isProject ? (AssetType?)null : AssetKinds.ByExtension.TryGetValue(ext, out var t) ? t : null;
        var img = new Image { Stretch = Stretch.Uniform, MaxWidth = 168, MaxHeight = 118 };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        var thumbHost = new Border { Height = 150, BorderThickness = new Thickness(0, 0, 0, 1), Child = img };
        thumbHost.SetResourceReference(Border.BackgroundProperty, "Bg0Brush");
        thumbHost.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        if (type is { } at)
        {
            var project = App.Project;
            Task.Run(() => AssetItem.Make(at, path, project, 128)).ContinueWith(r =>
            {
                if (r.Result is { } pic) img.Source = pic;
                else thumbHost.Child = new PathIcon { Kind = "sheet", Width = 40, Height = 40, StrokeWidth = 1.4 };   // e.g. a character outside the open project
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        else thumbHost.Child = new PathIcon { Kind = "folder", Width = 40, Height = 40, StrokeWidth = 1.4 };

        var name = new TextBlock { Text = System.IO.Path.GetFileNameWithoutExtension(path), FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis };
        var meta = new TextBlock { Text = $"{Kinds[ext]} · {File.GetLastWriteTime(path):d. M. yyyy}", FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
        meta.SetResourceReference(TextBlock.ForegroundProperty, "Text3Brush");
        var card = new Button
        {
            Width = 200, Margin = new Thickness(0, 0, 16, 16), ToolTip = path,
            Template = (ControlTemplate)FindResource("CardTemplate"),
            Content = new DockPanel { Children = { Top(thumbHost), new StackPanel { Margin = new Thickness(14, 12, 14, 12), Children = { name, meta } } } },
        };
        System.Windows.Automation.AutomationProperties.SetName(card, $"{System.IO.Path.GetFileName(path)}, {Kinds[ext]}");
        card.Click += (_, _) => { Hide(); OpenFile?.Invoke(path); };
        return card;

        static UIElement Top(UIElement e) { DockPanel.SetDock(e, Dock.Top); return e; }
    }
}
