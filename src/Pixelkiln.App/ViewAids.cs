using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Pixelkiln.Core;

namespace Pixelkiln.App;

/// <summary>
/// Editing aids shared by the canvas editors: rulers (click one to add a guide, right-click near a guide to remove it),
/// guides with snapping, and a reference picture over the canvas. Stored per asset in the project (AssetView), so
/// asset files never change; without a project they last for the session.
/// </summary>
sealed class ViewAids
{
    static readonly Dictionary<string, AssetView> Loose = new(StringComparer.OrdinalIgnoreCase);

    readonly Canvas _overlay = new() { IsHitTestVisible = false };
    readonly Image _reference = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    readonly Ruler _top, _left;
    readonly FrameworkElement _owner;
    AssetView _view = new();
    string? _path;
    double _docW, _docH, _scale = 1, _stroke = 1;

    public CheckBox SnapToggle { get; } = new() { IsChecked = true };
    public CheckBox ShowToggle { get; } = new() { IsChecked = true };

    /// <param name="owner">The editor (dialogs are owned by its window).</param>
    /// <param name="canvasHost">Panel inside the zoomed canvas; the overlay is added on top of its children.</param>
    /// <param name="scroller">The canvas scroll viewer; rulers are placed along its top and left edge.</param>
    /// <param name="target">Element whose top-left corner is document pixel (0, 0) on screen.</param>
    /// <param name="screenPerDoc">Screen pixels per document pixel (zoom).</param>
    public ViewAids(FrameworkElement owner, Panel canvasHost, ScrollViewer scroller, FrameworkElement target, Func<double> screenPerDoc)
    {
        _owner = owner;
        RenderOptions.SetBitmapScalingMode(_reference, BitmapScalingMode.HighQuality);
        _overlay.Children.Add(_reference);
        Panel.SetZIndex(_overlay, 1000);
        canvasHost.Children.Add(_overlay);
        _top = new Ruler(Orientation.Horizontal, target, screenPerDoc, pos => AddGuide(true, pos), pos => RemoveGuide(true, pos), () => _view.GuidesX);
        _left = new Ruler(Orientation.Vertical, target, screenPerDoc, pos => AddGuide(false, pos), pos => RemoveGuide(false, pos), () => _view.GuidesY);
        WrapWithRulers(scroller);
        scroller.ScrollChanged += (_, _) => InvalidateRulers();
        target.SizeChanged += (_, _) => InvalidateRulers();   // zoom moves the canvas after layout
        ShowToggle.Checked += (_, _) => Redraw();
        ShowToggle.Unchecked += (_, _) => Redraw();
    }

    void WrapWithRulers(ScrollViewer scroller)
    {
        var parent = (Panel)scroller.Parent;
        int index = parent.Children.IndexOf(scroller);
        parent.Children.Remove(scroller);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Ruler.Size) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Ruler.Size) });
        grid.RowDefinitions.Add(new RowDefinition());
        Grid.SetColumn(_top, 1);
        Grid.SetRow(_left, 1);
        Grid.SetRow(scroller, 1);
        Grid.SetColumn(scroller, 1);
        grid.Children.Add(_top);
        grid.Children.Add(_left);
        grid.Children.Add(scroller);
        parent.Children.Insert(index, grid);
    }

    void InvalidateRulers()
    {
        _top.InvalidateVisual();
        _left.InvalidateVisual();
    }

    // ---------- binding to an asset ----------

    /// <summary>Puts the overlay back after the editor cleared its canvas.</summary>
    public void Attach(Panel host)
    {
        if (_overlay.Parent is null) host.Children.Add(_overlay);
    }

    /// <summary>The same document got a (new) path: saved for the first time, renamed or moved. Keeps the guides.</summary>
    public void Rebind(string? path)
    {
        _path = path;
        if (!_view.IsEmpty) Store();
    }

    /// <summary>Call when the editor shows another file (or a new unsaved one: null).</summary>
    public void Bind(string? path)
    {
        _path = path;
        _view = path is null ? new AssetView()
            : App.Project?.FindByPath(path) is { } a ? App.Project.Settings.Views.GetValueOrDefault(a.Id) ?? new AssetView()
            : Loose.GetValueOrDefault(path) ?? new AssetView();
        LoadReference();
        Redraw();
    }

    void Store()
    {
        if (_path is null) return;
        if (App.Project is { } p && p.FindByPath(_path) is { } a)
        {
            if (_view.IsEmpty) p.Settings.Views.Remove(a.Id);
            else p.Settings.Views[a.Id] = _view;
            try { p.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); }
        }
        else Loose[_path] = _view;
    }

    // ---------- drawing ----------

    /// <summary>Document size, document-to-canvas scale (1 when the canvas is scaled by a transform) and line width in canvas units.</summary>
    public void Layout(double docW, double docH, double scale, double stroke)
    {
        (_docW, _docH, _scale, _stroke) = (docW, docH, scale, stroke);
        Redraw();
    }

    void Redraw()
    {
        _overlay.Children.Clear();
        _overlay.Children.Add(_reference);
        bool show = ShowToggle.IsChecked == true;
        _reference.Visibility = show && _view.ReferenceVisible && _reference.Source is not null ? Visibility.Visible : Visibility.Collapsed;
        _reference.Opacity = _view.ReferenceOpacity;
        _reference.Width = _docW * _scale;
        _reference.Height = _docH * _scale;
        if (show)
        {
            var brush = new SolidColorBrush(Color.FromArgb(0xC0, 0x00, 0xC8, 0xFF));
            foreach (var x in _view.GuidesX) _overlay.Children.Add(new Line { X1 = x * _scale, X2 = x * _scale, Y1 = 0, Y2 = _docH * _scale, Stroke = brush, StrokeThickness = _stroke });
            foreach (var y in _view.GuidesY) _overlay.Children.Add(new Line { Y1 = y * _scale, Y2 = y * _scale, X1 = 0, X2 = _docW * _scale, Stroke = brush, StrokeThickness = _stroke });
        }
        InvalidateRulers();
    }

    void LoadReference()
    {
        _reference.Source = null;
        if (_view.Reference is not string r) return;
        string full = App.Project is { } p && !System.IO.Path.IsPathRooted(r) ? System.IO.Path.Combine(p.Root, r) : r;
        if (!File.Exists(full)) return;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // do not keep the file locked
            bmp.UriSource = new Uri(full);
            bmp.EndInit();
            bmp.Freeze();
            _reference.Source = bmp;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException) { }   // a broken reference is just not shown
    }

    // ---------- guides ----------


    /// <summary>Snaps a document coordinate to a guide within 6 screen pixels (when snapping and guides are on).</summary>
    public double Snap(double v, bool vertical, double screenPerDoc) =>
        SnapToggle.IsChecked == true && ShowToggle.IsChecked == true ? AssetView.Snap(v, vertical ? _view.GuidesX : _view.GuidesY, 6 / screenPerDoc) : v;

    void AddGuide(bool vertical, double pos)
    {
        pos = Math.Round(pos);
        var list = vertical ? _view.GuidesX : _view.GuidesY;
        if (list.Contains(pos)) return;
        list.Add(pos);
        list.Sort();
        Store();
        Redraw();
    }

    void RemoveGuide(bool vertical, double pos)
    {
        var list = vertical ? _view.GuidesX : _view.GuidesY;
        if (list.Count == 0) return;
        double nearest = list.MinBy(g => Math.Abs(g - pos));
        list.Remove(nearest);
        Store();
        Redraw();
    }

    // ---------- menu ----------

    public MenuItem Menu() => Ui.Menu("G_uides and reference",
        Ui.Item("_Add guide…", null, AskGuide),
        Ui.Item("_Clear guides", null, () => { _view.GuidesX.Clear(); _view.GuidesY.Clear(); Store(); Redraw(); }),
        Ui.Toggle("_Snap to guides", null, SnapToggle),
        Ui.Toggle("Show guides and _reference", null, ShowToggle),
        new Separator(),
        Ui.Item("Reference _picture…", null, ChooseReference),
        Ui.Item("Reference _opacity…", null, AskOpacity),
        Ui.Item("Re_move reference picture", null, () => { _view.Reference = null; Store(); LoadReference(); Redraw(); }));

    void AskGuide()
    {
        if (Ui.AskFields(_owner, "Add guide", ("_Direction", "Vertical (x)", ["Vertical (x)", "Horizontal (y)"]), ("_Position (px)", "0", null)) is not { } f) return;
        if (!double.TryParse(f[1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double pos)) { Ui.Error(new FormatException("Position: a number of pixels.")); return; }
        AddGuide(f[0].StartsWith("Vertical"), pos);
    }

    void ChooseReference()
    {
        var dlg = new OpenFileDialog { Filter = "Pictures (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
        if (dlg.ShowDialog() != true) return;
        string path = dlg.FileName;
        if (App.Project is { } p && path.StartsWith(p.Root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            path = System.IO.Path.GetRelativePath(p.Root, path);   // moves with the project
        _view.Reference = path;
        _view.ReferenceVisible = true;
        Store();
        LoadReference();
        Redraw();
    }

    void AskOpacity()
    {
        if (Ui.AskFields(_owner, "Reference opacity", ("_Opacity (%)", ((int)(_view.ReferenceOpacity * 100)).ToString(), ["10", "25", "40", "60", "80", "100"])) is not { } f) return;
        if (!int.TryParse(f[0], out int pct) || pct is < 0 or > 100) { Ui.Error(new FormatException("Opacity: 0–100.")); return; }
        _view.ReferenceOpacity = pct / 100.0;
        Store();
        Redraw();
    }
}

/// <summary>Pixel ruler along one edge of a canvas; ticks follow zoom and scrolling. Click adds a guide, right-click removes the nearest.</summary>
sealed class Ruler : FrameworkElement
{
    public const double Size = 18;
    readonly Orientation _o;
    readonly FrameworkElement _target;
    readonly Func<double> _scale;
    readonly Func<IEnumerable<double>> _guides;

    public Ruler(Orientation o, FrameworkElement target, Func<double> scale, Action<double> add, Action<double> remove, Func<IEnumerable<double>> guides)
    {
        (_o, _target, _scale, _guides) = (o, target, scale, guides);
        Cursor = o == Orientation.Horizontal ? Cursors.SizeWE : Cursors.SizeNS;
        ToolTip = "Click: add a guide here · right-click: remove the nearest guide";
        AutomationProperties.SetName(this, o == Orientation.Horizontal ? "Horizontal ruler" : "Vertical ruler");
        MouseLeftButtonUp += (_, e) => add(DocAt(e.GetPosition(this)));
        MouseRightButtonUp += (_, e) => { remove(DocAt(e.GetPosition(this))); e.Handled = true; };
    }

    double Origin()
    {
        if (!_target.IsVisible || PresentationSource.FromVisual(this) is null) return 0;
        var p = _target.TranslatePoint(new Point(0, 0), this);
        return _o == Orientation.Horizontal ? p.X : p.Y;
    }

    /// <summary>1, 2, 5, 10, 20, 50 … document pixels: the first that puts ticks at least 6 screen pixels apart.</summary>
    static double TickStep(double screenPerDoc)
    {
        for (double decade = 1; ; decade *= 10)
            foreach (double m in new[] { 1.0, 2, 5 })
                if (m * decade * screenPerDoc >= 6) return m * decade;
    }

    double DocAt(Point p) => ((_o == Orientation.Horizontal ? p.X : p.Y) - Origin()) / _scale();

    protected override void OnRender(DrawingContext dc)
    {
        double len = _o == Orientation.Horizontal ? ActualWidth : ActualHeight, origin = Origin(), s = _scale();
        dc.DrawRectangle((Brush?)TryFindResource("ControlFillColorDefaultBrush") ?? Brushes.DimGray, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var pen = new Pen((Brush?)TryFindResource("MutedText") ?? Brushes.Gray, 1);
        double step = TickStep(s);
        int first = (int)Math.Floor(-origin / s / step), last = (int)Math.Ceiling((len - origin) / s / step);
        var typeface = new Typeface("Segoe UI");
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (int i = first; i <= last; i++)
        {
            double doc = i * step, at = origin + doc * s;
            bool major = i % 10 == 0;
            double tick = major ? Size : i % 5 == 0 ? Size * 0.5 : Size * 0.3;
            if (_o == Orientation.Horizontal) dc.DrawLine(pen, new Point(at, Size - tick), new Point(at, Size));
            else dc.DrawLine(pen, new Point(Size - tick, at), new Point(Size, at));
            if (!major) continue;
            var text = new FormattedText(doc.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 9, pen.Brush, dpi);
            if (_o == Orientation.Horizontal) dc.DrawText(text, new Point(at + 2, 0));
            else
            {
                dc.PushTransform(new RotateTransform(-90, 0, at));
                dc.DrawText(text, new Point(-text.Width - 2, at));
                dc.Pop();
            }
        }
        var guidePen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0xFF)), 2);
        foreach (var g in _guides())
        {
            double at = origin + g * s;
            if (_o == Orientation.Horizontal) dc.DrawLine(guidePen, new Point(at, 0), new Point(at, Size));
            else dc.DrawLine(guidePen, new Point(0, at), new Point(Size, at));
        }
    }
}
