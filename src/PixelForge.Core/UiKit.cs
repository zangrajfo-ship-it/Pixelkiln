using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelForge.Core;

public enum UiElementKind { Button, Panel, Dialog, Slider, HealthBar, InventorySlot, Icon, Label }
public enum UiState { Normal, Hover, Pressed, Disabled }
public enum UiAnchor { TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight, Stretch }

/// <summary>Look shared by every element of a kit: five colours, corner radius, border, and crisp pixel edges or smooth ones.</summary>
public sealed record UiStyle(uint Base = 0xFF3A5A8C, uint Accent = 0xFFF0B030, uint Dark = 0xFF141A26, uint Light = 0xFFD8E4F4, uint Text = 0xFFFFFFFF,
    int Radius = 3, int Border = 1, bool Pixel = true, int FontSize = 8);

/// <summary>
/// One reusable piece of interface. Its art per state is either a project picture or, when none is set, drawn from
/// the kit style. Margin is the nine-slice border that keeps corners sharp when the element is stretched.
/// </summary>
public sealed class UiElement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Button";
    public UiElementKind Kind { get; set; }
    public int W { get; set; } = 48;
    public int H { get; set; } = 16;
    public int Margin { get; set; } = 4;
    public string Label { get; set; } = "";
    public Dictionary<UiState, Guid> Art { get; set; } = [];
    /// <summary>Icon picture for Icon and InventorySlot (drawn in the middle).</summary>
    public Guid? IconAssetId { get; set; }
    public UiElement Clone() { var c = (UiElement)MemberwiseClone(); c.Art = new(Art); return c; }
}

/// <summary>An element placed on a screen: rectangle in design pixels, anchor for other resolutions, its own text and value.</summary>
public sealed class UiPlacement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ElementId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public UiAnchor Anchor { get; set; }
    public string? Text { get; set; }
    /// <summary>Fill of health bars and position of sliders (0–1).</summary>
    public double Value { get; set; } = 0.7;
    public UiState State { get; set; }
    /// <summary>Motion from an animation library (offset, scale, rotation, opacity over time).</summary>
    public Guid? ClipAssetId { get; set; }
    public string? Clip { get; set; }
    public UiPlacement Clone() => (UiPlacement)MemberwiseClone();
}

public sealed class UiScreen
{
    public string Name { get; set; } = "Screen";
    public int Width { get; set; } = 320;
    public int Height { get; set; } = 180;
    /// <summary>0 = transparent (an overlay on top of the game).</summary>
    public uint Background { get; set; }
    public List<UiPlacement> Items { get; set; } = [];
    public UiScreen Clone() => new() { Name = Name, Width = Width, Height = Height, Background = Background, Items = [.. Items.Select(i => i.Clone())] };
}

/// <summary>UI kit (.pxui): a style, the elements built with it, and screens laid out from them.</summary>
public sealed class UiKit
{
    // v2: UiPlacement.ClipAssetId/Clip
    public const int Version = 2;
    public UiStyle Style { get; set; } = new();
    public List<UiElement> Elements { get; set; } = [];
    public List<UiScreen> Screens { get; set; } = [];

    public UiElement? Element(Guid id) => Elements.FirstOrDefault(e => e.Id == id);

    public UiKit Clone() => new() { Style = Style, Elements = [.. Elements.Select(e => e.Clone())], Screens = [.. Screens.Select(s => s.Clone())] };

    public IEnumerable<Guid> ReferencedAssets() => Elements.SelectMany(e => e.Art.Values.Concat(e.IconAssetId is Guid g ? [g] : []))
        .Concat(Screens.SelectMany(s => s.Items).Select(i => i.ClipAssetId).OfType<Guid>()).Distinct();

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    sealed record Dto(int Version, UiStyle Style, List<UiElement> Elements, List<UiScreen> Screens);

    public static string Serialize(UiKit k) => JsonSerializer.Serialize(new Dto(Version, k.Style, k.Elements, k.Screens), Json);

    public static UiKit Deserialize(string json)
    {
        var d = JsonSerializer.Deserialize<Dto>(json, Json) ?? throw new InvalidDataException("Empty UI kit.");
        var k = new UiKit { Style = d.Style ?? new(), Elements = d.Elements ?? [], Screens = d.Screens ?? [] };
        var ids = k.Elements.Select(e => e.Id).ToHashSet();
        if (ids.Count != k.Elements.Count) throw new InvalidDataException("Two elements have the same id.");
        foreach (var e in k.Elements)
        {
            e.Art ??= [];
            if (e.W is < 1 or > 4096 || e.H is < 1 or > 4096 || e.Margin < 0 || e.Margin * 2 > Math.Min(e.W, e.H))
                throw new InvalidDataException($"Element “{e.Name}” has a size outside 1–4096 or a margin wider than half of it.");
        }
        foreach (var s in k.Screens)
        {
            s.Items ??= [];
            if (s.Width is < 16 or > 8192 || s.Height is < 16 or > 8192) throw new InvalidDataException($"Screen “{s.Name}” must be 16–8192 px.");
            foreach (var i in s.Items) if (!ids.Contains(i.ElementId)) throw new InvalidDataException($"Screen “{s.Name}” places an element that is not in the kit.");
        }
        if (k.Screens.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != k.Screens.Count) throw new InvalidDataException("Two screens have the same name.");
        return k;
    }

    public static void Save(UiKit k, string path) => DocumentFile.Save(path, Serialize(k), Version);
    public static UiKit Load(string path) => DocumentFile.Load(path, "UI kit", Version, Deserialize);

    // ---------- starter kits (templates) ----------

    public static readonly IReadOnlyDictionary<string, UiStyle> Styles = new Dictionary<string, UiStyle>(StringComparer.OrdinalIgnoreCase)
    {
        ["Fantasy"] = new(0xFF6B4A2A, 0xFFE8B84A, 0xFF24160C, 0xFFF0DCA8, 0xFFFFF4D8, 2, 1, true),
        ["Sci-fi"] = new(0xFF1E3A5A, 0xFF3CE0F0, 0xFF0A1220, 0xFF9CD8FF, 0xFFE8FAFF, 0, 1, true),
        ["Minimal"] = new(0xFF4A4A52, 0xFF4A90E2, 0xFF1C1C20, 0xFFE8E8EC, 0xFFFFFFFF, 4, 0, false),
        ["Candy"] = new(0xFFE85A8A, 0xFFFFD84A, 0xFF5A1A3A, 0xFFFFE0EC, 0xFFFFFFFF, 6, 1, true),
    };

    public static readonly string[] ScreenNames = ["Title", "Main menu", "Settings", "Level select", "Victory", "Game over", "Dialog", "Shop", "Inventory", "HUD"];

    /// <summary>A kit with the usual elements and the chosen screens laid out at the design resolution.</summary>
    public static UiKit Starter(UiStyle style, IEnumerable<string> screens, int width, int height)
    {
        var k = new UiKit { Style = style };
        UiElement E(string name, UiElementKind kind, int w, int h, int margin, string label = "")
        {
            var e = new UiElement { Name = name, Kind = kind, W = w, H = h, Margin = margin, Label = label };
            k.Elements.Add(e);
            return e;
        }
        var button = E("Button", UiElementKind.Button, 64, 16, 4, "Button");
        var panel = E("Panel", UiElementKind.Panel, 32, 32, 6);
        var dialog = E("Dialog", UiElementKind.Dialog, 48, 40, 10);
        var slider = E("Slider", UiElementKind.Slider, 64, 10, 4);
        var bar = E("Health bar", UiElementKind.HealthBar, 64, 8, 2);
        var slot = E("Slot", UiElementKind.InventorySlot, 20, 20, 4);
        var icon = E("Icon", UiElementKind.Icon, 16, 16, 0);
        var label = E("Label", UiElementKind.Label, 64, 12, 0, "Text");
        int cx = width / 2, bw = Math.Min(120, width / 3), bh = Math.Max(12, height / 12), gap = bh + bh / 3;
        UiPlacement P(UiElement e, int x, int y, int w, int h, UiAnchor a, string? text = null, double value = 0.7) =>
            new() { ElementId = e.Id, X = x, Y = y, W = w, H = h, Anchor = a, Text = text, Value = value };
        List<UiPlacement> Menu(int top, params string[] items) => [.. items.Select((t, i) => P(button, cx - bw / 2, top + i * gap, bw, bh, UiAnchor.Center, t))];
        foreach (var name in screens)
        {
            var s = new UiScreen { Name = name, Width = width, Height = height, Background = name is "HUD" or "Dialog" ? 0 : style.Dark };
            switch (name)
            {
                case "Title":
                    s.Items.Add(P(label, cx - bw, height / 5, bw * 2, bh * 2, UiAnchor.Top, "GAME TITLE"));
                    s.Items.AddRange(Menu(height / 2, "Start", "Continue", "Quit"));
                    break;
                case "Main menu":
                    s.Items.Add(P(panel, cx - bw / 2 - 8, height / 4 - 8, bw + 16, gap * 4 + 8, UiAnchor.Center));
                    s.Items.AddRange(Menu(height / 4, "Play", "Options", "Credits", "Quit"));
                    break;
                case "Settings":
                    s.Items.Add(P(dialog, width / 8, height / 8, width * 3 / 4, height * 3 / 4, UiAnchor.Stretch, "Settings"));
                    foreach (var (t, i) in new[] { "Music", "Sound", "Brightness" }.Select((t, i) => (t, i)))
                    {
                        s.Items.Add(P(label, width / 8 + 12, height / 8 + 24 + i * gap, bw / 2, bh, UiAnchor.Center, t));
                        s.Items.Add(P(slider, width / 8 + 16 + bw / 2, height / 8 + 24 + i * gap + bh / 4, bw, bh / 2 + 2, UiAnchor.Center, null, 0.3 + 0.2 * i));
                    }
                    s.Items.Add(P(button, cx - bw / 4, height * 7 / 8 - bh - 8, bw / 2, bh, UiAnchor.Bottom, "Back"));
                    break;
                case "Level select":
                    s.Items.Add(P(label, cx - bw, 8, bw * 2, bh, UiAnchor.Top, "Choose a level"));
                    for (int i = 0; i < 8; i++)
                        s.Items.Add(P(slot, width / 2 - 2 * (bh * 2 + 6) + i % 4 * (bh * 2 + 6), height / 3 + i / 4 * (bh * 2 + 6), bh * 2, bh * 2, UiAnchor.Center, (i + 1).ToString()));
                    break;
                case "Victory":
                case "Game over":
                    s.Items.Add(P(label, cx - bw, height / 4, bw * 2, bh * 2, UiAnchor.Center, name == "Victory" ? "YOU WIN!" : "GAME OVER"));
                    s.Items.AddRange(Menu(height / 2, name == "Victory" ? "Next level" : "Try again", "Menu"));
                    break;
                case "Dialog":
                    s.Items.Add(P(dialog, 8, height - height / 3 - 8, width - 16, height / 3, UiAnchor.Bottom, "Elder|Hello, traveller! Will you help us?"));
                    s.Items.Add(P(icon, 16, height - height / 3, 24, 24, UiAnchor.BottomLeft));
                    break;
                case "Shop":
                    s.Items.Add(P(dialog, width / 10, height / 10, width * 4 / 5, height * 4 / 5, UiAnchor.Stretch, "Shop"));
                    for (int i = 0; i < 6; i++)
                    {
                        s.Items.Add(P(slot, width / 10 + 12, height / 10 + 22 + i % 3 * (bh + 8), bh + 4, bh + 4, UiAnchor.Center));
                        s.Items.Add(P(button, width / 10 + 20 + bh + (i / 3) * (width * 2 / 5), height / 10 + 22 + i % 3 * (bh + 8), bw / 2, bh, UiAnchor.Center, $"Buy {10 * (i + 1)}"));
                    }
                    break;
                case "Inventory":
                    s.Items.Add(P(panel, width / 6, height / 6, width * 2 / 3, height * 2 / 3, UiAnchor.Center));
                    int sz = Math.Max(12, Math.Min(width, height) / 9);
                    for (int i = 0; i < 12; i++)
                        s.Items.Add(P(slot, width / 6 + 8 + i % 6 * (sz + 2), height / 6 + 8 + i / 6 * (sz + 2), sz, sz, UiAnchor.Center));
                    break;
                case "HUD":
                    s.Items.Add(P(bar, 6, 6, Math.Min(80, width / 4), 8, UiAnchor.TopLeft, null, 0.8));
                    s.Items.Add(P(icon, width - 22, 4, 16, 16, UiAnchor.TopRight));
                    s.Items.Add(P(label, width - 70, 6, 46, 12, UiAnchor.TopRight, "x 12"));
                    break;
            }
            k.Screens.Add(s);
        }
        return k;
    }
}

/// <summary>Draws kit elements and whole screens to pixels (straight ARGB), so the editor, the exports and the game preview agree.</summary>
public static class UiRender
{
    static Color C(uint c) => Color.FromArgb((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c);

    static uint Mix(uint a, uint b, double t)
    {
        uint Ch(int s) => (uint)Math.Round((a >> s & 0xFF) * (1 - t) + (b >> s & 0xFF) * t) << s;
        return Ch(24) | Ch(16) | Ch(8) | Ch(0);
    }

    /// <summary>The element's own art for a state at its native size: the project picture if one is set, otherwise drawn from the style.</summary>
    public static uint[] Native(UiKit kit, UiElement e, UiState state, Project? p = null)
    {
        Guid? art = e.Art.TryGetValue(state, out var a) ? a : e.Art.TryGetValue(UiState.Normal, out var n) ? n : null;
        if (art is Guid id && p?.Resolve(id) is string path)
        {
            var (px, w, h) = AssetLoader.LoadImage(path);
            return w == e.W && h == e.H ? px : Scale(px, w, h, e.W, e.H);
        }
        return Draw(kit.Style, e, state, e.W, e.H, null, 0.7, p);
    }

    /// <summary>Element at any size: nine-slice of its native art (corners kept, edges and middle stretched).</summary>
    public static uint[] Sized(UiKit kit, UiElement e, UiState state, int w, int h, string? text, double value, Project? p = null)
    {
        if (e.Art.Count == 0) return Draw(kit.Style, e, state, w, h, text, value, p);   // generated art is simply drawn at the size
        var nine = NineSlice(Native(kit, e, state, p), e.W, e.H, e.Margin, w, h);
        return text is { Length: > 0 } ? Overlay(nine, w, h, dc => DrawText(dc, kit.Style, text, new Rect(0, 0, w, h), state)) : nine;
    }

    public static uint[] NineSlice(uint[] src, int sw, int sh, int m, int tw, int th)
    {
        var o = new uint[tw * th];
        m = Math.Min(m, Math.Min(Math.Min(sw, sh) / 2, Math.Min(tw, th) / 2));
        int Map(int t, int tl, int sl) => t < m ? t : t >= tl - m ? sl - (tl - t) : sl - 2 * m <= 0 ? m : m + (t - m) * (sl - 2 * m) / Math.Max(1, tl - 2 * m);
        for (int y = 0; y < th; y++)
        {
            int sy = Math.Clamp(Map(y, th, sh), 0, sh - 1);
            for (int x = 0; x < tw; x++) o[y * tw + x] = src[sy * sw + Math.Clamp(Map(x, tw, sw), 0, sw - 1)];
        }
        return o;
    }

    static uint[] Scale(uint[] src, int sw, int sh, int tw, int th)
    {
        var o = new uint[tw * th];
        for (int y = 0; y < th; y++) for (int x = 0; x < tw; x++) o[y * tw + x] = src[y * sh / th * sw + x * sw / tw];
        return o;
    }

    /// <summary>Draws an element from the style: shape per kind, state changes (hover lighter, pressed darker and lower, disabled faded).</summary>
    public static uint[] Draw(UiStyle st, UiElement e, UiState state, int w, int h, string? text, double value, Project? p = null) => Sta.Run(() =>
    {
        var icon = e.IconAssetId is Guid ic && p?.Resolve(ic) is string ip ? AssetLoader.LoadImage(ip) : ((uint[], int, int)?)null;
        return Render(w, h, st.Pixel, dc =>
        {
            uint baseC = state switch { UiState.Hover => Mix(st.Base, st.Light, 0.25), UiState.Pressed => Mix(st.Base, st.Dark, 0.3), _ => st.Base };
            double r = st.Radius, bw = st.Border, dy = state == UiState.Pressed ? 1 : 0;
            var full = new Rect(bw / 2, bw / 2 + dy, Math.Max(0, w - bw), Math.Max(0, h - bw - dy));
            Pen? border = bw > 0 ? new Pen(new SolidColorBrush(C(st.Dark)), bw) : null;
            switch (e.Kind)
            {
                case UiElementKind.Button:
                    dc.DrawRoundedRectangle(new SolidColorBrush(C(baseC)), border, full, r, r);
                    if (state != UiState.Pressed) dc.DrawLine(new Pen(new SolidColorBrush(C(Mix(baseC, st.Light, 0.5))), 1), new Point(r + bw, bw + 0.5), new Point(w - r - bw, bw + 0.5));
                    DrawText(dc, st, text ?? e.Label, full, state);
                    break;
                case UiElementKind.Panel:
                case UiElementKind.Dialog:
                    dc.DrawRoundedRectangle(new SolidColorBrush(C(Mix(st.Dark, st.Base, 0.35))), bw > 0 ? new Pen(new SolidColorBrush(C(st.Accent)), bw) : null, full, r, r);
                    if (e.Kind == UiElementKind.Dialog)
                    {
                        double bar = Math.Min(h / 3.0, st.FontSize + 4);
                        dc.DrawRectangle(new SolidColorBrush(C(st.Accent)), null, new Rect(bw, bw, w - 2 * bw, bar));
                        if ((text ?? e.Label) is { Length: > 0 } t)
                        {
                            // first line in the title bar, the rest as body text
                            var parts = t.Split('|', 2);
                            DrawText(dc, st with { Text = st.Dark }, parts[0], new Rect(0, 0, w, bar + bw), state);
                            if (parts.Length > 1) DrawText(dc, st, parts[1], new Rect(4, bar + 2, w - 8, h - bar - 4), state, left: true);
                        }
                    }
                    else DrawText(dc, st, text ?? e.Label, full, state);
                    break;
                case UiElementKind.Slider:
                    double track = Math.Max(2, h / 3.0);
                    dc.DrawRoundedRectangle(new SolidColorBrush(C(st.Dark)), null, new Rect(0, (h - track) / 2, w, track), track / 2, track / 2);
                    dc.DrawRoundedRectangle(new SolidColorBrush(C(st.Accent)), null, new Rect(0, (h - track) / 2, w * Math.Clamp(value, 0, 1), track), track / 2, track / 2);
                    double knob = h - 1;
                    dc.DrawEllipse(new SolidColorBrush(C(baseC == st.Base ? st.Light : baseC)), border, new Point(Math.Clamp(w * value, knob / 2, w - knob / 2), h / 2.0), knob / 2, knob / 2);
                    break;
                case UiElementKind.HealthBar:
                    dc.DrawRoundedRectangle(new SolidColorBrush(C(st.Dark)), border, full, r, r);
                    var inner = new Rect(full.X + bw + 1, full.Y + bw + 1, Math.Max(0, (full.Width - 2 * bw - 2) * Math.Clamp(value, 0, 1)), Math.Max(0, full.Height - 2 * bw - 2));
                    dc.DrawRoundedRectangle(new SolidColorBrush(C(st.Accent)), null, inner, Math.Max(0, r - 1), Math.Max(0, r - 1));
                    break;
                case UiElementKind.InventorySlot:
                    dc.DrawRoundedRectangle(new SolidColorBrush(C(Mix(st.Dark, st.Base, state == UiState.Hover ? 0.4 : 0.2))), new Pen(new SolidColorBrush(C(st.Base)), Math.Max(1, bw)), full, r, r);
                    if (icon is var (ipx, iw, ih)) DrawPicture(dc, ipx, iw, ih, new Rect(w * 0.15, h * 0.15, w * 0.7, h * 0.7));
                    DrawText(dc, st, text ?? e.Label, full, state);
                    break;
                case UiElementKind.Icon:
                    if (icon is var (px, pw, ph)) DrawPicture(dc, px, pw, ph, new Rect(0, 0, w, h));
                    else dc.DrawEllipse(new SolidColorBrush(C(st.Accent)), border, new Point(w / 2.0, h / 2.0), w / 2.0 - bw, h / 2.0 - bw);
                    break;
                case UiElementKind.Label:
                    DrawText(dc, st, text ?? e.Label, new Rect(0, 0, w, h), state);
                    break;
            }
        }, state == UiState.Disabled);
    });

    static void DrawPicture(DrawingContext dc, uint[] px, int w, int h, Rect into)
    {
        var bmp = ImageIO.ToBitmap(px, w, h);
        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.NearestNeighbor);
        group.Children.Add(new ImageDrawing(bmp, into));
        dc.DrawDrawing(group);
    }

    static void DrawText(DrawingContext dc, UiStyle st, string? text, Rect box, UiState state, bool left = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        double size = Math.Max(4, Math.Min(st.FontSize * (box.Height >= st.FontSize * 2.4 ? 1.5 : 1), box.Height * 0.8));
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            size, new SolidColorBrush(C(state == UiState.Disabled ? Mix(st.Text, st.Dark, 0.5) : st.Text)), 1)
        { MaxTextWidth = Math.Max(1, box.Width - 2), TextAlignment = left ? TextAlignment.Left : TextAlignment.Center, Trimming = TextTrimming.CharacterEllipsis };
        dc.DrawText(ft, new Point(box.X + 1, box.Y + Math.Max(0, (box.Height - ft.Height) / 2)));
    }

    /// <summary>Renders a drawing callback into straight-alpha pixels; pixel style = no half-transparent edges.</summary>
    static uint[] Render(int w, int h, bool pixel, Action<DrawingContext> draw, bool faded = false)
    {
        var visual = new DrawingVisual();
        if (pixel) TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Aliased);
        using (var dc = visual.RenderOpen())
        {
            if (faded) dc.PushOpacity(0.5);
            draw(dc);
            if (faded) dc.Pop();
        }
        var rtb = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var px = new uint[Math.Max(1, w) * Math.Max(1, h)];
        rtb.CopyPixels(px, Math.Max(1, w) * 4, 0);
        for (int i = 0; i < px.Length; i++)
        {
            uint p = px[i], a = p >> 24;
            if (a is 0 or 255) continue;
            if (pixel && !faded && a < 128) { px[i] = 0; continue; }
            uint U(int s) => Math.Min(255u, ((p >> s & 0xFF) * 255 + a / 2) / a) << s;
            px[i] = (pixel && !faded ? 255u : a) << 24 | U(16) | U(8) | U(0);
        }
        return px;
    }

    static uint[] Overlay(uint[] under, int w, int h, Action<DrawingContext> draw) => Sta.Run(() =>
    {
        var top = Render(w, h, false, draw);
        var o = (uint[])under.Clone();
        for (int i = 0; i < o.Length; i++) if (top[i] >> 24 > 0) o[i] = Blending.Over(o[i], top[i] | 0xFF000000, (top[i] >> 24) / 255.0);
        return o;
    });

    /// <summary>Where a placement lands when its screen is shown at another size (design size = the screen's own).</summary>
    public static (int X, int Y, int W, int H) Resolve(UiPlacement i, int designW, int designH, int w, int h)
    {
        int dx = w - designW, dy = h - designH;
        return i.Anchor switch
        {
            UiAnchor.TopLeft => (i.X, i.Y, i.W, i.H),
            UiAnchor.Top => (i.X + dx / 2, i.Y, i.W, i.H),
            UiAnchor.TopRight => (i.X + dx, i.Y, i.W, i.H),
            UiAnchor.Left => (i.X, i.Y + dy / 2, i.W, i.H),
            UiAnchor.Center => (i.X + dx / 2, i.Y + dy / 2, i.W, i.H),
            UiAnchor.Right => (i.X + dx, i.Y + dy / 2, i.W, i.H),
            UiAnchor.BottomLeft => (i.X, i.Y + dy, i.W, i.H),
            UiAnchor.Bottom => (i.X + dx / 2, i.Y + dy, i.W, i.H),
            UiAnchor.BottomRight => (i.X + dx, i.Y + dy, i.W, i.H),
            _ => (i.X, i.Y, Math.Max(1, i.W + dx), Math.Max(1, i.H + dy)),   // stretch: keeps its distance to every edge
        };
    }

    /// <summary>A whole screen at a size (anchors applied); transparent background = an overlay.</summary>
    public static uint[] Screen(UiKit kit, UiScreen s, int w, int h, Project? p = null, double timeMs = 0) => Sta.Run(() =>   // one STA thread for all elements
    {
        var o = new uint[w * h];
        if (s.Background >> 24 != 0) Array.Fill(o, s.Background);
        var libs = new Dictionary<Guid, AnimLibrary?>();
        foreach (var i in s.Items)
        {
            if (kit.Element(i.ElementId) is not { } e) continue;
            var (x, y, iw, ih) = Resolve(i, s.Width, s.Height, w, h);
            if (iw <= 0 || ih <= 0) continue;
            var px = Sized(kit, e, i.State, iw, ih, i.Text, i.Value, p);
            // an element with a clip moves, scales, turns and fades around its centre
            if (i.ClipAssetId is Guid cid && p?.Resolve(cid) is string lp
                && (libs.TryGetValue(cid, out var lib) ? lib : libs[cid] = TryLoad(lp))?.Clip(i.Clip) is { } clip)
            {
                var pose = clip.Pose("", timeMs);
                SceneRenderer.BlitTransformed(o, w, h, px, iw, ih, iw / 2, ih / 2, x + iw / 2.0 + pose.X, y + ih / 2.0 + pose.Y, pose.ScaleX, pose.ScaleY, pose.Rotation, pose.Opacity);
            }
            else SceneRenderer.Blit(o, w, h, px, iw, ih, x, y, false, 1);
        }
        return o;
    });

    static AnimLibrary? TryLoad(string path)
    {
        try { return AnimLibrary.Load(path); }
        catch (InvalidDataException) { return null; }
    }
}
