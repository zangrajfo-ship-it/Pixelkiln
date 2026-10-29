using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Pixelkiln.App;

/// <summary>
/// Colours and fonts. Every theme publishes the design tokens (Bg0Brush … DangerBrush, UiFont, MonoFont) that the
/// editors use; "Modern" (.ai/context/design.md) is Fluent dark with every Fluent colour mapped onto those tokens,
/// Inter as the UI font and JetBrains Mono for numbers.
/// </summary>
static class Theme
{
    public static readonly string[] Names = ["Modern", "Dark", "Light", "System"];

    public static readonly FontFamily Inter = new(new Uri("pack://application:,,,/"), "./Fonts/#Inter");
    public static readonly FontFamily Mono = new(new Uri("pack://application:,,,/"), "./Fonts/#JetBrains Mono");
    static readonly FontFamily Segoe = new("Segoe UI");
    static readonly FontFamily Consolas = new("Cascadia Mono, Consolas");

    static Color C(uint argb) => Ui.ColorOf(argb);

    // token → (Modern, Dark, Light)
    static readonly (string Key, uint Modern, uint Dark, uint Light)[] Tokens =
    [
        ("Bg0", 0xFF141518, 0xFF1E1E1E, 0xFFE9E9EC),
        ("Bg1", 0xFF1C1D21, 0xFF202020, 0xFFF7F7F8),
        ("Bg2", 0xFF24262B, 0xFF2B2B2B, 0xFFFFFFFF),
        ("Hover", 0xFF2A2C32, 0xFF323232, 0xFFE4E4E8),
        ("Selected", 0xFF2E3036, 0xFF383838, 0xFFDADADF),
        ("Line", 0xFF2B2D33, 0xFF3A3A3A, 0xFFD0D0D6),
        ("Text", 0xFFE6E7EA, 0xFFFFFFFF, 0xFF1A1A1E),
        ("Text2", 0xFFA3A6AD, 0xFFB8B8B8, 0xFF55575E),
        ("Text3", 0xFF8A8D94, 0xFF9A9A9A, 0xFF6B6E75),
        ("Disabled", 0xFF6B6E75, 0xFF6E6E6E, 0xFF9A9CA2),
        ("Accent", 0xFF6AA1FF, 0xFF3B82F6, 0xFF2563EB),
        ("AccentTint", 0xFF2B3A55, 0xFF1E3A66, 0xFFD6E4FF),
        ("OnAccent", 0xFF0F1115, 0xFFFFFFFF, 0xFFFFFFFF),
        ("Success", 0xFF5AD18A, 0xFF5AD18A, 0xFF1F9D55),
        ("Danger", 0xFFE8505B, 0xFFE8505B, 0xFFC92A37),
        ("Link", 0xFF8FB8FF, 0xFF8FB8FF, 0xFF1D4ED8),
        ("Menu", 0xFFC9CBD1, 0xFFE0E0E0, 0xFF2A2C32),
        ("Outline", 0xFF33353B, 0xFF4A4A4A, 0xFFC4C6CC),
        ("OutlineHover", 0xFF4A4D55, 0xFF5A5A5A, 0xFFA8AAB0),
    ];

    /// <summary>Applies a theme to the application's resources (call again after a change in Preferences).</summary>
    public static void Apply(Application app, string name, bool systemLight)
    {
        bool modern = name == "Modern", light = name == "Light" || name == "System" && systemLight;
        foreach (var (key, m, d, l) in Tokens)
        {
            var brush = new SolidColorBrush(C(modern ? m : light ? l : d));
            brush.Freeze();
            app.Resources[key + "Brush"] = brush;
            app.Resources[key + "Color"] = brush.Color;
        }
        var accent = (Color)app.Resources["AccentColor"];
        foreach (var (key, alpha) in new[] { ("AccentRow", 0x1F), ("AccentFaint", 0x24), ("AccentSoft", 0x2E), ("AccentRing", 0x66) })
        {
            var b = new SolidColorBrush(Color.FromArgb((byte)alpha, accent.R, accent.G, accent.B));
            b.Freeze();
            app.Resources[key + "Brush"] = b;
        }
        app.Resources["UiFont"] = modern ? Inter : Segoe;
        app.Resources["MonoFont"] = modern ? Mono : Consolas;
        app.Resources["MutedText"] = app.Resources["Text2Brush"];   // older name used across the editors
        app.Resources["FocusRing"] = app.Resources["AccentBrush"];

        foreach (var key in Remapped) app.Resources.Remove(key);
        Remapped.Clear();
        if (modern) RemapFluent(app);
        _font = modern ? Inter : null;
        foreach (Window w in app.Windows) ApplyFont(w);
    }

    // ---------- Modern: Fluent's dark colours mapped onto the tokens ----------

    static readonly List<object> Remapped = [];

    static void RemapFluent(Application app)
    {
        var found = new Dictionary<object, object>();
        void Walk(ResourceDictionary d)
        {
            foreach (var m in d.MergedDictionaries) Walk(m);
            foreach (var key in d.Keys) found[key] = d[key];
        }
        foreach (var m in app.Resources.MergedDictionaries) Walk(m);
        foreach (var (key, value) in found)
        {
            string k = key.ToString() ?? "";
            object? mapped = value switch
            {
                SolidColorBrush b when Map(k, b.Color) is Color c && c != b.Color => Frozen(c),
                Color c0 when Map(k, c0) is Color c && c != c0 => c,
                double size when k is "ControlContentThemeFontSize" or "ContentControlFontSize" or "BodyTextBlockFontSize" or "TreeViewItemFontSize" => 13.0,
                _ => null,
            };
            if (mapped is null) continue;
            app.Resources[key] = mapped;
            Remapped.Add(key);
        }
    }

    static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>One Fluent colour in the Modern palette (alpha kept for translucent overlays).</summary>
    static Color? Map(string key, Color c)
    {
        uint rgb = (uint)(c.R << 16 | c.G << 8 | c.B);
        Color Opaque(uint v) => C(0xFF000000 | v);
        Color Keep(uint v) => C((uint)c.A << 24 | v);
        bool selectedRow = key.Contains("Selected") && (key.Contains("ListBox") || key.Contains("ListView") || key.Contains("TreeView") || key.Contains("DataGrid") || key.Contains("ComboBoxItem"));
        return rgb switch
        {
            // Windows accent (green here, whatever the user picked elsewhere) → the design accent
            0x61BA61 or 0x107C10 or 0x6CCB5F when key.Contains("Success") => null,
            0x61BA61 or 0x107C10 => selectedRow ? Opaque(0x2B3A55) : Keep(0x6AA1FF),
            0x90D690 => Keep(0x8FB8FF),
            // solid surfaces
            0x202020 or 0x2C2C2C or 0x1C1C1C when key.Contains("Stroke") => Keep(0x2B2D33),
            0x202020 or 0x2C2C2C => Keep(0x1C1D21),
            0x1C1C1C or 0x1E1E1E or 0x1A1A1A => Keep(0x141518),
            0x0A0A0A or 0x131313 => Keep(0x101114),
            0x282828 => Keep(0x24262B),
            0x2E2E2E => Keep(0x2A2C32),
            0x454545 => Keep(0x3A3D44),
            0x3A3A3A => Keep(0x2B2D33),
            // text on dark: white steps → the text tokens
            0xFFFFFF when c.A == 0xFF && key.Contains("FocusStrokeColorOuter") => Opaque(0x6AA1FF),
            0xFFFFFF when c.A == 0xFF => Opaque(0xE6E7EA),
            0xFFFFFF when c.A == 0xC5 => Opaque(0xA3A6AD),
            0xFFFFFF when c.A == 0x87 => Opaque(0x8A8D94),
            0xFFFFFF when c.A == 0x5D => Opaque(0x6B6E75),
            // black text on accent surfaces
            0x000000 when c.A == 0xFF && (key.Contains("Accent") || key.Contains("Glyph") || key.Contains("Today")) => Opaque(0x0F1115),
            _ => null,
        };
    }

    // ---------- fonts ----------

    static FontFamily? _font;
    static bool _hooked;

    /// <summary>Every window gets the theme's UI font as it loads (WPF has no application-wide font setting).</summary>
    public static void HookWindows()
    {
        if (_hooked) return;
        _hooked = true;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => ApplyFont((Window)s)));
    }

    static void ApplyFont(Window w)
    {
        w.SetResourceReference(Control.ForegroundProperty, "TextBrush");   // labels and plain text inherit the text token
        if (_font is null) w.ClearValue(TextElement.FontFamilyProperty);
        else w.FontFamily = _font;
    }
}
