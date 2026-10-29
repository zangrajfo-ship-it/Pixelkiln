using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Path = System.Windows.Shapes.Path;
using Shape = System.Windows.Shapes.Shape;

namespace Pixelkiln.App;

/// <summary>
/// A line icon drawn from a path in a 24×24 box (the redesign's icon set), stroked with the inherited foreground
/// so it follows hover, active and disabled colours. Use Kind (a name from <see cref="Icons"/>).
/// </summary>
sealed class PathIcon : Viewbox
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(PathIcon),
        new PropertyMetadata(null, (d, e) => ((PathIcon)d).Build()));
    public static readonly DependencyProperty FilledProperty = DependencyProperty.Register(nameof(Filled), typeof(bool), typeof(PathIcon),
        new PropertyMetadata(false, (d, e) => ((PathIcon)d).Build()));

    public string? Kind { get => (string?)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    /// <summary>Filled shapes (play triangle, squares) instead of strokes.</summary>
    public bool Filled { get => (bool)GetValue(FilledProperty); set => SetValue(FilledProperty, value); }
    public double StrokeWidth { get; set; } = 1.7;

    public PathIcon()
    {
        Width = Height = 20;
        Stretch = Stretch.Uniform;
        IsHitTestVisible = false;
        Focusable = false;
    }

    void Build()
    {
        if (Kind is null || !Icons.Paths.TryGetValue(Kind, out var data)) { Child = null; return; }
        var path = new Path
        {
            Data = Geometry.Parse(data), StrokeThickness = StrokeWidth, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        var fg = new Binding { Path = new PropertyPath(TextElement.ForegroundProperty), RelativeSource = RelativeSource.Self };
        path.SetBinding(Filled ? Shape.FillProperty : Shape.StrokeProperty, fg);
        Child = new Canvas { Width = 24, Height = 24, Children = { path } };
    }
}

/// <summary>Icon paths from the redesign mockups (24-unit box). The whole set is kept, so some names are not used yet.</summary>
static class Icons
{
    public static readonly Dictionary<string, string> Paths = new()
    {
        ["pencil"] = "M4 20l4-1 11-11-3-3L5 16l-1 4z M14 6l3 3",
        ["eraser"] = "M8 20h12 M4 15l9-9 6 6-7 7H8l-4-4z M9 10l6 6",
        ["bucket"] = "M5 11l7-7 7 7-7 7-7-7z M5 11h14 M20 15c0 1.5 1 2.5 1 3.5a1 1 0 0 1-2 0c0-1 1-2 1-3.5z",
        ["picker"] = "M15 4l5 5-2 2-5-5 2-2z M13 6l-8 8v5h5l8-8",
        ["line"] = "M5 19L19 5",
        ["rect"] = "M5 5h14v14H5z",
        ["ellipse"] = "M5 12a7 7 0 1 0 14 0a7 7 0 1 0-14 0",
        ["marquee"] = "M4 4h3 M10 4h4 M17 4h3v3 M20 10v4 M20 17v3h-3 M14 20h-4 M7 20H4v-3 M4 14v-4 M4 7V4",
        ["lasso"] = "M12 4c5 0 8 2.5 8 5.5S16 15 12 15s-8-2.5-8-5.5S7 4 12 4z M7 14c-1 2 0 4 2 4s2 2 1 3",
        ["wand"] = "M4 20L14 10 M16 3v4 M14 5h4 M20 9v3 M18.5 10.5h3",
        ["move"] = "M12 3v18 M3 12h18 M9 6l3-3 3 3 M9 18l3 3 3-3 M6 9l-3 3 3 3 M18 9l3 3-3 3",
        ["swap"] = "M7 4L4 7l3 3 M4 7h11a5 5 0 0 1 5 5 M17 20l3-3-3-3 M20 17H9a5 5 0 0 1-5-5",
        ["undo"] = "M9 14L4 9l5-5 M4 9h10.5a5.5 5.5 0 0 1 0 11H11",
        ["redo"] = "M15 14l5-5-5-5 M20 9H9.5a5.5 5.5 0 0 0 0 11H13",
        ["download"] = "M12 4v11 M7 10l5 5 5-5 M5 20h14",
        ["plus"] = "M12 5v14 M5 12h14",
        ["minus"] = "M5 12h14",
        ["close"] = "M6 6l12 12 M18 6L6 18",
        ["grid"] = "M4 4h16v16H4z M4 9.33h16 M4 14.67h16 M9.33 4v16 M14.67 4v16",
        ["onion"] = "M9 6a6 6 0 1 0 0 12a6 6 0 1 0 0-12z M15 6a6 6 0 1 0 0 12a6 6 0 1 0 0-12z",
        ["tiles"] = "M4 4h7v7H4z M13 4h7v7h-7z M4 13h7v7H4z M13 13h7v7h-7z",
        ["tiled"] = "M3 3h18v18H3z M3 12h18 M12 3v18",
        ["eye"] = "M2 12s3.5-6 10-6 10 6 10 6-3.5 6-10 6S2 12 2 12z M12 9.5a2.5 2.5 0 1 0 0 5 2.5 2.5 0 1 0 0-5z",
        ["eyeoff"] = "M3 3l18 18 M10.6 6.1A10 10 0 0 1 12 6c6.5 0 10 6 10 6a17 17 0 0 1-3 3.6 M6.6 6.6C3.9 8.3 2 12 2 12s3.5 6 10 6c1.7 0 3.2-.4 4.5-1",
        ["first"] = "M6 5v14 M18 6l-8 6 8 6z",
        ["last"] = "M18 5v14 M6 6l8 6-8 6z",
        ["prev"] = "M15 6l-6 6 6 6",
        ["next"] = "M9 6l6 6-6 6",
        ["play"] = "M7 5l12 7-12 7z",
        ["stop"] = "M6 6h12v12H6z",
        ["duplicate"] = "M8 8h12v12H8z M4 16V4h12",
        ["trash"] = "M4 7h16 M9 7V4h6v3 M6 7l1 13h10l1-13",
        ["left"] = "M15 6l-6 6 6 6",
        ["right"] = "M9 6l6 6-6 6",
        ["up"] = "M6 15l6-6 6 6",
        ["down"] = "M6 9l6 6 6-6",
        ["more"] = "M5 12h.01 M12 12h.01 M19 12h.01",
        ["chevron"] = "M6 9l6 6 6-6",
        ["preview"] = "M3 5h18v12H3z M8 21h8 M12 17v4",
        ["folder"] = "M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z",
        ["history"] = "M4 4v6h6 M20 12a8 8 0 1 1-2.3-5.7L20 8.5 M4 10l2.3-2.3",
        ["sheet"] = "M4 6h16v12H4z M4 10h16 M10 10v8",
        ["search"] = "M11 4a7 7 0 1 0 0 14a7 7 0 1 0 0-14z M20 20l-4-4",
        ["tag"] = "M4 4h8l8 8-8 8-8-8z M8 8h.01",
        ["hand"] = "M8 13V5.5a1.5 1.5 0 0 1 3 0V12 M11 11.5V4a1.5 1.5 0 0 1 3 0v8 M14 11V6a1.5 1.5 0 0 1 3 0v7 M17 10a1.5 1.5 0 0 1 3 0v4a7 7 0 0 1-7 7h-1a7 7 0 0 1-5-2.1L4 15.5a1.6 1.6 0 0 1 2.3-2.2L8 15",
        ["camera"] = "M3 8h13v10H3z M16 11l5-3v10l-5-3",
        ["pointer"] = "M5 3l14 8-6 2-2 6z",
        ["place"] = "M12 5v14M5 12h14 M4 4h4 M16 4h4 M4 20h4 M16 20h4",
        ["collision"] = "M4 4h16v16H4z M4 12l8-8 M4 20L20 4 M12 20l8-8",
        ["flipH"] = "M12 3v18 M8 7L3 12l5 5z M16 7l5 5-5 5z",
        ["flipV"] = "M3 12h18 M7 8l5-5 5 5z M7 16l5 5 5-5z",
        ["rotate"] = "M20 12a8 8 0 1 1-2.3-5.7 M20 4v4.5h-4.5",
        ["loop"] = "M17 2l3 3-3 3 M4 11V9a4 4 0 0 1 4-4h12 M7 22l-3-3 3-3 M20 13v2a4 4 0 0 1-4 4H4",
        ["note"] = "M9 18V5l11-2v13 M9 18a3 3 0 1 1-6 0 3 3 0 0 1 6 0z M20 16a3 3 0 1 1-6 0 3 3 0 0 1 6 0z",
        ["wave"] = "M4 12h2 M8 8v8 M12 5v14 M16 8v8 M20 11v2",
        ["dice"] = "M5 5h14v14H5z M9 9h.01 M15 15h.01 M15 9h.01 M9 15h.01",
        ["mutate"] = "M4 12a8 8 0 0 1 14-5 M20 12a8 8 0 0 1-14 5 M18 3v4h-4 M6 21v-4h4",
        ["coin"] = "M12 4a8 8 0 1 0 0 16a8 8 0 1 0 0-16z M12 8v8",
        ["jump"] = "M6 18c2-8 10-8 12 0 M12 4v6 M9 7l3-3 3 3",
        ["laser"] = "M4 18L20 6 M15 6h5v5",
        ["hit"] = "M13 3L5 14h6l-1 7 8-11h-6z",
        ["explosion"] = "M12 2l2 6 6-2-4 5 6 3-7 1 1 7-4-5-4 5 1-7-7-1 6-3-4-5 6 2z",
        ["powerup"] = "M12 20V6 M6 12l6-6 6 6 M5 20h14",
        ["blip"] = "M5 12h3l2-5 4 10 2-5h3",
        ["layers"] = "M12 4l8 4-8 4-8-4z M4 12l8 4 8-4 M4 16l8 4 8-4",
        ["help"] = "M12 4a8 8 0 1 0 0 16a8 8 0 1 0 0-16z M9.5 9.5a2.5 2.5 0 1 1 3.5 2.3c-.6.3-1 .8-1 1.5v.7 M12 17h.01",
    };
}
