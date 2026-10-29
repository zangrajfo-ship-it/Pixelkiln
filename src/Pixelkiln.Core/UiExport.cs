using System.Globalization;
using System.Text;
using System.Text.Json;
using static Pixelkiln.Core.GodotExport;

namespace Pixelkiln.Core;

/// <summary>
/// UI kit export: every element state as a PNG, an atlas with a JSON description (sizes, nine-slice margins, screens
/// with anchors), and for Godot a Theme (StyleBoxTexture per state) plus one Control scene per screen.
/// </summary>
public static class UiExport
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static string Safe(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s.Trim()) sb.Append(char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
        return sb.Length == 0 ? "_" : sb.ToString();
    }

    public static string ScreenFile(string b, string screen) => $"{b}_{Safe(screen)}.tscn";

    /// <summary>Exported picture of one element state.</summary>
    static string StateFile(string b, string element, UiState s) => $"{b}_ui/{element}_{s.ToString().ToLowerInvariant()}.png";

    static IEnumerable<UiState> StatesOf(UiElement e) => e.Kind is UiElementKind.Button or UiElementKind.InventorySlot or UiElementKind.Slider ? Enum.GetValues<UiState>() : [UiState.Normal];

    /// <summary>All files for one kit; keys are export-relative paths. res maps a relative path to res://… (Godot) or null (generic).</summary>
    public static SortedDictionary<string, byte[]> Files(UiKit kit, string b, Project? p, Func<string, string>? res)
    {
        var o = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var names = new Dictionary<Guid, string>();
        var used = new HashSet<string>();
        foreach (var e in kit.Elements)
        {
            string n = Safe(e.Name), u = n;
            for (int k = 2; !used.Add(u); k++) u = $"{n}_{k}";
            names[e.Id] = u;
        }

        // states as pictures, and packed into one atlas row per element
        var parts = new List<(UiElement E, UiState S, uint[] Px, string File)>();
        foreach (var e in kit.Elements)
            foreach (var s in StatesOf(e))
            {
                var px = UiRender.Native(kit, e, s, p);
                string file = StateFile(b, names[e.Id], s);
                o[file] = ImageIO.EncodePng(px, e.W, e.H);
                parts.Add((e, s, px, file));
            }
        int aw = Math.Max(1, kit.Elements.Count == 0 ? 1 : kit.Elements.Max(e => e.W * StatesOf(e).Count())), ah = Math.Max(1, kit.Elements.Sum(e => e.H));
        var atlas = new uint[aw * ah];
        var rects = new Dictionary<(Guid, UiState), (int X, int Y)>();
        int y = 0;
        foreach (var e in kit.Elements)
        {
            int x = 0;
            foreach (var part in parts.Where(q => q.E == e))
            {
                PixelOps.Stamp(atlas, aw, ah, part.Px, e.W, e.H, x, y);
                rects[(e.Id, part.S)] = (x, y);
                x += e.W;
            }
            y += e.H;
        }
        o[$"{b}_atlas.png"] = ImageIO.EncodePng(atlas, aw, ah);
        o[$"{b}_ui.json"] = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            format = "pixelkiln-ui", version = 1, atlas = $"{b[(b.LastIndexOf('/') + 1)..]}_atlas.png",
            style = new { kit.Style.Radius, kit.Style.Border, kit.Style.Pixel, colors = new { @base = Hex(kit.Style.Base), accent = Hex(kit.Style.Accent), dark = Hex(kit.Style.Dark), light = Hex(kit.Style.Light), text = Hex(kit.Style.Text) } },
            elements = kit.Elements.Select(e => new
            {
                name = names[e.Id], kind = e.Kind.ToString(), width = e.W, height = e.H, margin = e.Margin, label = e.Label,
                states = StatesOf(e).ToDictionary(s => s.ToString().ToLowerInvariant(), s => new { x = rects[(e.Id, s)].X, y = rects[(e.Id, s)].Y, w = e.W, h = e.H }),
            }),
            screens = kit.Screens.Select(s => new
            {
                name = s.Name, width = s.Width, height = s.Height, background = s.Background >> 24 == 0 ? null : Hex(s.Background),
                items = s.Items.Where(i => names.ContainsKey(i.ElementId)).Select(i => new { element = names[i.ElementId], i.X, i.Y, w = i.W, h = i.H, anchor = i.Anchor.ToString(), text = i.Text, value = i.Value, state = i.State.ToString() }),
            }),
        }, DocumentFile.Indented).Replace("\r\n", "\n"));

        if (res is null) return o;

        // ---- Godot: Theme + one Control scene per screen ----
        string themeFile = $"{b}_theme.tres";
        o[themeFile] = Encoding.UTF8.GetBytes(Theme(kit, (e, st) => res(StateFile(b, names[e.Id], st))));
        foreach (var s in kit.Screens)
            o[ScreenFile(b, s.Name)] = Encoding.UTF8.GetBytes(ScreenScene(kit, s, b, names, res, res(themeFile)));
        return o;
    }

    static string Hex(uint c) => $"#{c & 0xFFFFFF:x6}";

    /// <summary>Godot Theme: StyleBoxTexture per button state, panel, dialog and progress bar, using the exported state PNGs.</summary>
    static string Theme(UiKit kit, Func<UiElement, UiState, string> fileOf)
    {
        var ext = new List<string>();
        var subs = new StringBuilder();
        var body = new StringBuilder();
        int sub = 0;
        void Style(string target, UiElement e, UiState s)
        {
            string file = fileOf(e, s);
            int i = ext.IndexOf(file);
            if (i < 0) { ext.Add(file); i = ext.Count - 1; }
            string id = $"StyleBoxTexture_{++sub}";
            subs.Append($"[sub_resource type=\"StyleBoxTexture\" id=\"{id}\"]\ntexture = ExtResource(\"{i + 1}\")\n");
            foreach (var side in new[] { "left", "top", "right", "bottom" }) subs.Append($"texture_margin_{side} = {F(e.Margin)}\n");
            subs.Append('\n');
            body.Append($"{target} = SubResource(\"{id}\")\n");
        }
        if (kit.Elements.FirstOrDefault(e => e.Kind == UiElementKind.Button) is { } button)
            foreach (var s in Enum.GetValues<UiState>()) Style($"Button/styles/{s.ToString().ToLowerInvariant()}", button, s);
        if (kit.Elements.FirstOrDefault(e => e.Kind == UiElementKind.Panel) is { } panel) Style("Panel/styles/panel", panel, UiState.Normal);
        if (kit.Elements.FirstOrDefault(e => e.Kind == UiElementKind.Dialog) is { } dialog) Style("PanelContainer/styles/panel", dialog, UiState.Normal);
        if (kit.Elements.FirstOrDefault(e => e.Kind == UiElementKind.HealthBar) is { } bar)
        {
            Style("ProgressBar/styles/background", bar, UiState.Normal);
            Style("ProgressBar/styles/fill", bar, UiState.Normal);
        }
        body.Append($"default_font_size = {kit.Style.FontSize * 2}\n");
        body.Append($"Label/colors/font_color = Color({C(kit.Style.Text)})\nButton/colors/font_color = Color({C(kit.Style.Text)})\n");
        var sb = new StringBuilder($"[gd_resource type=\"Theme\" load_steps={ext.Count + sub + 1} format=3]\n\n");
        for (int i = 0; i < ext.Count; i++) sb.Append($"[ext_resource type=\"Texture2D\" path=\"{ext[i]}\" id=\"{i + 1}\"]\n");
        if (ext.Count > 0) sb.Append('\n');
        sb.Append(subs).Append("[resource]\n").Append(body);
        return sb.ToString();
    }

    static string C(uint c) => string.Join(", ", new[] { c >> 16 & 0xFF, c >> 8 & 0xFF, c & 0xFF, c >> 24 }.Select(v => (v / 255.0).ToString("0.###", Inv)));

    /// <summary>Godot anchors for our anchor names (fractions of the parent) and the matching offsets.</summary>
    static (double L, double T, double R, double B) Anchors(UiAnchor a) => a switch
    {
        UiAnchor.TopLeft => (0, 0, 0, 0), UiAnchor.Top => (0.5, 0, 0.5, 0), UiAnchor.TopRight => (1, 0, 1, 0),
        UiAnchor.Left => (0, 0.5, 0, 0.5), UiAnchor.Center => (0.5, 0.5, 0.5, 0.5), UiAnchor.Right => (1, 0.5, 1, 0.5),
        UiAnchor.BottomLeft => (0, 1, 0, 1), UiAnchor.Bottom => (0.5, 1, 0.5, 1), UiAnchor.BottomRight => (1, 1, 1, 1),
        _ => (0, 0, 1, 1),
    };

    static string ScreenScene(UiKit kit, UiScreen s, string b, Dictionary<Guid, string> names, Func<string, string> res, string themePath)
    {
        var ext = new List<(string Type, string Path)> { ("Theme", themePath) };
        string Ext(string type, string path)
        {
            int i = ext.FindIndex(x => x.Path == path);
            if (i < 0) { ext.Add((type, path)); i = ext.Count - 1; }
            return $"ExtResource(\"{i + 1}\")";
        }
        var body = new StringBuilder();
        body.Append($"[node name={Q(NodeName(s.Name))} type=\"Control\"]\nlayout_mode = 3\nanchors_preset = 15\nanchor_right = 1.0\nanchor_bottom = 1.0\ngrow_horizontal = 2\ngrow_vertical = 2\ntheme = ExtResource(\"1\")\n");
        body.Append($"metadata/design_size = Vector2({s.Width}, {s.Height})\n\n");
        if (s.Background >> 24 != 0)
            body.Append($"[node name=\"Background\" type=\"ColorRect\" parent=\".\"]\nlayout_mode = 1\nanchors_preset = 15\nanchor_right = 1.0\nanchor_bottom = 1.0\ncolor = Color({C(s.Background)})\n\n");
        var used = new HashSet<string>();
        foreach (var i in s.Items)
        {
            if (kit.Element(i.ElementId) is not { } e) continue;
            string u = Unique(used, i.Text is { Length: > 0 } t && t.Length <= 24 ? t : e.Name);
            var (al, at, ar, ab) = Anchors(i.Anchor);
            // offsets are distances from the anchor points in design pixels
            double ol = i.X - al * s.Width, ot = i.Y - at * s.Height, or = i.X + i.W - ar * s.Width, ob = i.Y + i.H - ab * s.Height;
            string type = e.Kind switch
            {
                UiElementKind.Button => "Button", UiElementKind.Panel => "Panel", UiElementKind.Dialog => "PanelContainer", UiElementKind.Slider => "HSlider",
                UiElementKind.HealthBar => "ProgressBar", UiElementKind.Label => "Label", _ => "TextureRect",
            };
            body.Append($"[node name={Q(u)} type=\"{type}\" parent=\".\"]\nlayout_mode = 1\n");
            body.Append($"anchor_left = {F(al)}\nanchor_top = {F(at)}\nanchor_right = {F(ar)}\nanchor_bottom = {F(ab)}\n");
            body.Append($"offset_left = {F(ol)}\noffset_top = {F(ot)}\noffset_right = {F(or)}\noffset_bottom = {F(ob)}\n");
            switch (type)
            {
                case "Button": body.Append($"text = {Q(i.Text ?? e.Label)}\n"); if (i.State == UiState.Disabled) body.Append("disabled = true\n"); break;
                case "Label": body.Append($"text = {Q(i.Text ?? e.Label)}\nhorizontal_alignment = 1\nvertical_alignment = 1\n"); break;
                case "HSlider": body.Append($"value = {F(i.Value * 100)}\n"); break;
                case "ProgressBar": body.Append($"value = {F(i.Value * 100)}\nshow_percentage = false\n"); break;
                case "TextureRect":
                    body.Append($"texture = {Ext("Texture2D", res(StateFile(b, names[e.Id], i.State)))}\nexpand_mode = 1\nstretch_mode = 5\n");
                    break;
            }
            body.Append('\n');
            if (type == "PanelContainer" && (i.Text ?? e.Label) is { Length: > 0 } dt)
                body.Append($"[node name=\"Text\" type=\"Label\" parent={Q(u)}]\nlayout_mode = 2\ntext = {Q(dt.Replace('|', '\n'))}\nautowrap_mode = 3\n\n");
        }
        var sb = new StringBuilder($"[gd_scene load_steps={ext.Count + 1} format=3]\n\n");
        for (int k = 0; k < ext.Count; k++) sb.Append($"[ext_resource type=\"{ext[k].Type}\" path=\"{ext[k].Path}\" id=\"{k + 1}\"]\n");
        sb.Append('\n').Append(body);
        return sb.ToString().TrimEnd('\n') + "\n";
    }
}
