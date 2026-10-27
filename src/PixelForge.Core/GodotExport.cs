using System.Globalization;
using System.Text;

namespace PixelForge.Core;

/// <summary>
/// Godot 4 text resources (.tres) and scenes (.tscn), format=3. Targets Godot 4.3+ (Parallax2D); the TileMap node
/// it writes still loads there and Godot offers to convert it to TileMapLayer nodes.
/// </summary>
public static class GodotExport
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Floats always with a decimal point, as Godot writes them.</summary>
    public static string F(double v)
    {
        string s = Math.Round(v, 4).ToString("0.####", Inv);
        return s.Contains('.') ? s : s + ".0";
    }

    public static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    /// <summary>Collects external and sub resources with stable ids, then writes the file header first.</summary>
    sealed class Doc(string header)
    {
        readonly List<(string Type, string Path, string Id)> _ext = [];
        readonly StringBuilder _subs = new(), _body = new();
        int _sub;

        public string Ext(string type, string path)
        {
            var hit = _ext.FirstOrDefault(e => e.Path == path && e.Type == type);
            if (hit.Id is not null) return $"ExtResource(\"{hit.Id}\")";
            string id = (_ext.Count + 1).ToString(Inv);
            _ext.Add((type, path, id));
            return $"ExtResource(\"{id}\")";
        }

        public string Sub(string type, params string[] props)
        {
            string id = $"{type}_{++_sub}";
            _subs.Append($"[sub_resource type=\"{type}\" id=\"{id}\"]\n");
            foreach (var p in props) _subs.Append(p).Append('\n');
            _subs.Append('\n');
            return $"SubResource(\"{id}\")";
        }

        public StringBuilder Body => _body;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(header.Replace("{steps}", (_ext.Count + _sub + 1).ToString(Inv))).Append("\n\n");
            foreach (var (type, path, id) in _ext) sb.Append($"[ext_resource type=\"{type}\" path={Q(path)} id=\"{id}\"]\n");
            if (_ext.Count > 0) sb.Append('\n');
            sb.Append(_subs).Append(_body);
            return sb.ToString().TrimEnd('\n') + "\n";
        }
    }

    // ---------- sprites ----------

    /// <summary>SpriteFrames: one animation per tag (or "default" with every frame); per-frame durations as multiples of the shortest.</summary>
    public static string SpriteFrames(Sprite s, int columns, string texturePath)
    {
        var d = new Doc("[gd_resource type=\"SpriteFrames\" load_steps={steps} format=3]");
        string tex = d.Ext("Texture2D", texturePath);
        var atlas = new string[s.Frames.Count];
        for (int f = 0; f < s.Frames.Count; f++)
            atlas[f] = d.Sub("AtlasTexture", $"atlas = {tex}", $"region = Rect2({f % columns * s.Width}, {f / columns * s.Height}, {s.Width}, {s.Height})");
        var tags = s.Tags.Count > 0 ? s.Tags.Select(t => (t.Name, t.From, t.To)) : [("default", 0, s.Frames.Count - 1)];
        var anims = tags.Select(t =>
        {
            int unit = Enumerable.Range(t.From, t.To - t.From + 1).Min(f => Math.Max(1, s.Frames[f].DurationMs));
            var frames = Enumerable.Range(t.From, t.To - t.From + 1)
                .Select(f => $"{{\n\"duration\": {F(Math.Max(1, s.Frames[f].DurationMs) / (double)unit)},\n\"texture\": {atlas[f]}\n}}");
            return $"{{\n\"frames\": [{string.Join(", ", frames)}],\n\"loop\": true,\n\"name\": &{Q(t.Item1)},\n\"speed\": {F(1000.0 / unit)}\n}}";
        });
        d.Body.Append("[resource]\n").Append($"animations = [{string.Join(", ", anims)}]\n");
        return d.ToString();
    }

    // ---------- tiles ----------

    /// <summary>
    /// TileSet with one atlas source holding every tile; tile collisions become polygons on one physics layer per
    /// collision category (layer bit and mask from the project settings); one-way tiles keep their flag.
    /// </summary>
    public static string TileSet(Tilemap m, string texturePath, ProjectSettings settings)
    {
        var ts = m.Tileset!;
        var d = new Doc("[gd_resource type=\"TileSet\" load_steps={steps} format=3]");
        var cats = m.TileCollisions.Values.Select(c => c.Category).Distinct().Order().ToList();
        var lines = new List<string> { $"texture = {d.Ext("Texture2D", texturePath)}", $"texture_region_size = Vector2i({ts.TileSize}, {ts.TileSize})" };
        double half = ts.TileSize / 2.0;
        for (int i = 0; i < ts.Count; i++)
        {
            string cell = $"{i % ts.Columns}:{i / ts.Columns}/0";
            lines.Add($"{cell} = 0");
            if (!m.TileCollisions.TryGetValue(i, out var c)) continue;
            string layer = $"{cell}/physics_layer_{cats.IndexOf(c.Category)}/polygon_0";
            double x0 = c.X - half, y0 = c.Y - half, x1 = x0 + c.W, y1 = y0 + c.H;   // polygon points are relative to the tile centre
            lines.Add($"{layer}/points = PackedVector2Array({F(x0)}, {F(y0)}, {F(x1)}, {F(y0)}, {F(x1)}, {F(y1)}, {F(x0)}, {F(y1)})");
            if (c.OneWay) lines.Add($"{layer}/one_way = true");
        }
        string source = d.Sub("TileSetAtlasSource", [.. lines]);
        d.Body.Append("[resource]\n").Append($"tile_size = Vector2i({ts.TileSize}, {ts.TileSize})\n");
        for (int k = 0; k < cats.Count; k++)
        {
            d.Body.Append($"physics_layer_{k}/collision_layer = {1u << cats[k]}\n");
            d.Body.Append($"physics_layer_{k}/collision_mask = {settings.CollisionMasks.GetValueOrDefault(cats[k], uint.MaxValue)}\n");
        }
        d.Body.Append($"sources/0 = {source}\n");
        return d.ToString();
    }

    /// <summary>Godot's TileMap cell encoding: (y &lt;&lt; 16 | x), (source | atlasX &lt;&lt; 16), (atlasY | alternative &lt;&lt; 16).</summary>
    public static int[] CellData(MapLayer l, int width, int columns)
    {
        var o = new List<int>();
        for (int i = 0; i < l.Tiles.Length; i++)
        {
            int t = l.Tiles[i];
            if (t < 0) continue;
            o.Add(i / width << 16 | i % width & 0xFFFF);
            o.Add(0 | t % columns << 16);
            o.Add(t / columns);
        }
        return [.. o];
    }

    public static string TileMapScene(Tilemap m, string name, string tileSetPath)
    {
        var d = new Doc("[gd_scene load_steps={steps} format=3]");
        string tileset = d.Ext("TileSet", tileSetPath);
        var b = d.Body;
        b.Append($"[node name={Q(NodeName(name))} type=\"TileMap\"]\n");
        b.Append($"tile_set = {tileset}\nformat = 2\n");
        for (int i = 0; i < m.Layers.Count; i++)
        {
            var l = m.Layers[i];
            b.Append($"layer_{i}/name = {Q(l.Name)}\n");
            if (!l.Visible) b.Append($"layer_{i}/enabled = false\n");
            b.Append($"layer_{i}/z_index = {i}\n");
            b.Append($"layer_{i}/tile_data = PackedInt32Array({string.Join(", ", CellData(l, m.Width, m.Tileset!.Columns))})\n");
        }
        b.Append('\n');
        // map objects: points become markers, rectangles become areas
        var names = new HashSet<string>();
        foreach (var o in m.Objects)
        {
            string n = Unique(names, o.Name.Length > 0 ? o.Name : $"object_{o.Id}");
            if (o.IsPoint)
            {
                b.Append($"[node name={Q(n)} type=\"Marker2D\" parent=\".\"]\nposition = Vector2({F(o.X)}, {F(o.Y)})\n");
                if (o.Type.Length > 0) b.Append($"metadata/type = {Q(o.Type)}\n");
                b.Append('\n');
            }
            else
            {
                string shape = d.Sub("RectangleShape2D", $"size = Vector2({F(o.W)}, {F(o.H)})");
                b.Append($"[node name={Q(n)} type=\"Area2D\" parent=\".\"]\nposition = Vector2({F(o.X + o.W / 2.0)}, {F(o.Y + o.H / 2.0)})\n");
                if (o.Type.Length > 0) b.Append($"metadata/type = {Q(o.Type)}\n");
                b.Append($"\n[node name=\"CollisionShape2D\" type=\"CollisionShape2D\" parent={Q(n)}]\nshape = {shape}\n\n");
            }
        }
        return d.ToString();
    }

    // ---------- characters ----------

    /// <summary>
    /// Character scene: CharacterBody2D (player, enemy) or Area2D (npc, item) with an AnimatedSprite2D whose origin is
    /// the node position, the solid box as collision shape and the other boxes as metadata; properties as metadata.
    /// </summary>
    public static string CharacterScene(Character c, Sprite s, string name, string framesPath, ProjectSettings settings, string? sheetPath = null, AnimLibrary? rigLib = null)
    {
        var d = new Doc("[gd_scene load_steps={steps} format=3]");
        string frames = d.Ext("SpriteFrames", framesPath);
        var solid = c.Boxes.FirstOrDefault(b => b.Kind == BoxKind.Solid && b.State is null) ?? c.Boxes.FirstOrDefault(b => b.Kind == BoxKind.Solid);
        string? shape = solid is null ? null : d.Sub("RectangleShape2D", $"size = Vector2({F(solid.W)}, {F(solid.H)})");
        string type = c.Kind is CharacterKind.Player or CharacterKind.Enemy ? "CharacterBody2D" : "Area2D";
        string defaultState = c.Animations.Keys.FirstOrDefault(k => k == "idle") ?? c.Animations.Keys.FirstOrDefault() ?? "";
        string anim = c.Animations.GetValueOrDefault(defaultState) ?? s.Tags.FirstOrDefault()?.Name ?? "default";
        var b = d.Body;
        b.Append($"[node name={Q(NodeName(name))} type=\"{type}\"]\n");
        int cat = solid?.Category ?? 0;
        b.Append($"collision_layer = {1u << cat}\ncollision_mask = {settings.CollisionMasks.GetValueOrDefault(cat, uint.MaxValue)}\n");
        b.Append($"metadata/kind = {Q(c.Kind.ToString())}\nmetadata/facing = {Q(c.Facing.ToString())}\n");
        b.Append($"metadata/states = {{{string.Join(", ", c.Animations.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{Q(kv.Key)}: {Q(kv.Value)}"))}}}\n");
        foreach (var (k, v) in c.Properties.OrderBy(kv => kv.Key, StringComparer.Ordinal)) b.Append($"metadata/{NodeName(k)} = {Q(v)}\n");
        var others = c.Boxes.Where(x => x != solid).Select(x => $"{{\"kind\": {Q(x.Kind.ToString())}, \"state\": {(x.State is null ? "null" : Q(x.State))}, \"frame\": {(x.Frame is int f ? f.ToString(Inv) : "null")}, \"rect\": Rect2({x.X - c.OriginX}, {x.Y - c.OriginY}, {x.W}, {x.H}), \"category\": {x.Category}}}");
        b.Append($"metadata/boxes = [{string.Join(", ", others)}]\n");
        if (c.Parts.Count > 0 && sheetPath is not null)
        {
            b.Append($"metadata/rig_states = {{{string.Join(", ", c.RigClips.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{Q(kv.Key)}: {Q(kv.Value)}"))}}}\n\n");
            Rig(d, c, sheetPath, rigLib, defaultState);
        }
        else
        {
            b.Append($"\n[node name=\"AnimatedSprite2D\" type=\"AnimatedSprite2D\" parent=\".\"]\n");
            b.Append($"sprite_frames = {frames}\nanimation = &{Q(anim)}\nautoplay = {Q(anim)}\ncentered = false\noffset = Vector2({F(-c.OriginX)}, {F(-c.OriginY)})\n\n");
        }
        if (solid is not null)
            b.Append($"[node name=\"CollisionShape2D\" type=\"CollisionShape2D\" parent=\".\"]\nposition = Vector2({F(solid.X - c.OriginX + solid.W / 2.0)}, {F(solid.Y - c.OriginY + solid.H / 2.0)})\nshape = {shape}\n\n");
        return d.ToString();
    }

    /// <summary>
    /// Cut-out rig: Sprite2D per part (a region of the sheet's first frame) nested under its parent with the pivot as
    /// node origin, absolute z_index; the state clips baked into an AnimationPlayer (no skinning).
    /// </summary>
    static void Rig(Doc d, Character c, string sheetPath, AnimLibrary? lib, string defaultState)
    {
        var b = d.Body;
        string tex = d.Ext("Texture2D", sheetPath);
        b.Append("[node name=\"Rig\" type=\"Node2D\" parent=\".\"]\n\n");
        var paths = new Dictionary<string, string>();
        string PathOf(RigPart p, int depth) => paths.TryGetValue(p.Name, out var hit) ? hit
            : paths[p.Name] = (depth <= c.Parts.Count && c.Parts.FirstOrDefault(x => x.Name == p.Parent && x != p) is { } up ? PathOf(up, depth + 1) : "Rig") + "/" + NodeName(p.Name);
        // parents first: Godot needs a node's parent declared above it
        foreach (var p in c.Parts.OrderBy(p => PathOf(p, 0).Count(ch => ch == '/')))
        {
            string path = PathOf(p, 0), parent = path[..path.LastIndexOf('/')];
            string region = d.Sub("AtlasTexture", $"atlas = {tex}", $"region = Rect2({p.X}, {p.Y}, {p.W}, {p.H})");
            b.Append($"[node name={Q(NodeName(p.Name))} type=\"Sprite2D\" parent={Q(parent)}]\n");
            b.Append($"position = Vector2({F(p.OffsetX)}, {F(p.OffsetY)})\nz_index = {p.Z}\nz_as_relative = false\ntexture = {region}\ncentered = false\noffset = Vector2({F(-p.PivotX)}, {F(-p.PivotY)})\n\n");
        }
        if (lib is null) return;
        var clips = c.RigClips.Values.Distinct().Select(lib.Clip).OfType<AnimClip>().ToList();
        if (clips.Count == 0) return;
        var anims = clips.ToDictionary(clip => clip.Name, clip => Baked(d, clip, c.Parts.SelectMany(p => Tracks(clip, p.Name, PathOf(p, 0), p.OffsetX, p.OffsetY, 1, "position"))));
        string auto = c.RigClips.GetValueOrDefault(defaultState) ?? clips[0].Name;
        Player(d, ".", anims, anims.ContainsKey(auto) ? auto : clips[0].Name);
    }

    const double BakeMs = 1000 / 30.0;

    /// <summary>
    /// A clip as a Godot Animation, sampled 30 times a second so every curve (Bezier, ping-pong, reverse) plays as in
    /// PixelForge. ponytail: baked keys, not Godot's own curves; editing in Godot means re-exporting.
    /// </summary>
    static string Baked(Doc d, AnimClip clip, IEnumerable<(string Path, Func<double, string> At)> tracks)
    {
        double period = Math.Max(1, clip.Play == AnimPlay.PingPong ? 2 * clip.DurationMs : clip.DurationMs);
        int n = Math.Max(1, (int)Math.Ceiling(period / BakeMs));
        var times = Enumerable.Range(0, n + 1).Select(k => Math.Min(period, k * BakeMs)).ToList();
        var props = new List<string> { $"resource_name = {Q(clip.Name)}", $"length = {F(period / 1000)}", $"loop_mode = {(clip.Play == AnimPlay.Once ? 0 : 1)}" };
        int i = 0;
        foreach (var (path, at) in tracks)
        {
            ValueTrack(props, i++, path, string.Join(", ", times.Select(t => F(t / 1000))), string.Join(", ", times.Select(_ => "1.0")), string.Join(", ", times.Select(at)));
        }
        return d.Sub("Animation", [.. props]);
    }

    /// <summary>The properties a clip animates on one target, as Godot node properties around a base position.</summary>
    static IEnumerable<(string Path, Func<double, string> At)> Tracks(AnimClip c, string target, string node, double baseX, double baseY, double flip, string positionProp)
    {
        bool Has(AnimProperty p) => c.Tracks.Any(t => t.Target == target && t.Property == p && t.Keys.Count > 0);
        if (Has(AnimProperty.X) || Has(AnimProperty.Y))
            yield return ($"{node}:{positionProp}", t => $"Vector2({F(baseX + c.Value(target, AnimProperty.X, t))}, {F(baseY + c.Value(target, AnimProperty.Y, t))})");
        if (Has(AnimProperty.ScaleX) || Has(AnimProperty.ScaleY))
            yield return ($"{node}:scale", t => $"Vector2({F(flip * c.Value(target, AnimProperty.ScaleX, t))}, {F(c.Value(target, AnimProperty.ScaleY, t))})");
        if (Has(AnimProperty.Rotation)) yield return ($"{node}:rotation", t => F(c.Value(target, AnimProperty.Rotation, t) * Math.PI / 180));
        if (Has(AnimProperty.Opacity)) yield return ($"{node}:modulate", t => $"Color(1, 1, 1, {F(c.Value(target, AnimProperty.Opacity, t))})");
    }

    /// <summary>AnimationPlayer under a node (its root is that node) with the given animations, one autoplaying.</summary>
    static void Player(Doc d, string parent, Dictionary<string, string> anims, string autoplay)
    {
        string lib = d.Sub("AnimationLibrary", $"_data = {{\n{string.Join(",\n", anims.Select(kv => $"&{Q(kv.Key)}: {kv.Value}"))}\n}}");
        d.Body.Append($"[node name=\"AnimationPlayer\" type=\"AnimationPlayer\" parent={Q(parent)}]\nroot_node = NodePath(\"..\")\nlibraries = {{\n&\"\": {lib}\n}}\nautoplay = {Q(autoplay)}\n\n");
    }

    // ---------- levels ----------

    /// <summary>
    /// Level scene: layers in order (tilemaps and characters instanced from their own scenes, images as Sprite2D,
    /// parallax layers inside Parallax2D), movement tracks as AnimationPlayers, areas as Area2D in groups named
    /// after their kind, the start camera and the music.
    /// </summary>
    public static string LevelScene(Scene s, string name, Project p, SceneAssets assets, Func<Guid, string> res, List<string> warnings,
        Func<string, uint[], int, int, string>? picture = null, string? logicScript = null)
    {
        var d = new Doc("[gd_scene load_steps={steps} format=3]");
        var b = d.Body;
        var rootNames = new HashSet<string>();
        b.Append($"[node name={Q(NodeName(name))} type=\"Node2D\"]\n");
        if (s.Level.Length > 0) b.Append($"metadata/level = {Q(s.Level)}\nmetadata/order = {s.Order}\n");
        b.Append('\n');

        foreach (var l in s.Layers)
        {
            string layerName = Unique(rootNames, l.Name);
            bool parallax = l.ParallaxX != 1 || l.ParallaxY != 1 || l.RepeatX;
            string parent = ".";
            if (parallax)
            {
                b.Append($"[node name={Q(layerName)} type=\"Parallax2D\" parent=\".\"]\nscroll_scale = Vector2({F(l.ParallaxX)}, {F(l.ParallaxY)})\n");
                if (l.RepeatX && l.AssetId is Guid rid && assets.Image(rid) is var (_, rw, _)) b.Append($"repeat_size = Vector2({F(rw)}, 0.0)\nrepeat_times = 3\n");
                if (!l.Visible) b.Append("visible = false\n");
                b.Append('\n');
                parent = layerName;
            }
            string Pos() => $"position = Vector2({F(l.OffsetX)}, {F(l.OffsetY)})\n";
            string Hidden() => !parallax && !l.Visible ? "visible = false\n" : "";
            string own = parallax ? "Content" : layerName;
            switch (l.Kind)
            {
                case SceneLayerKind.Tilemap when l.AssetId is Guid id:
                    b.Append($"[node name={Q(own)} parent={Q(parent)} instance={d.Ext("PackedScene", res(id) + ".tscn")}]\n{Pos()}{Hidden()}\n");
                    break;
                case SceneLayerKind.Image when l.AssetId is Guid id:
                    b.Append($"[node name={Q(own)} type=\"Sprite2D\" parent={Q(parent)}]\n{Pos()}{Hidden()}texture = {d.Ext("Texture2D", res(id) + ".png")}\ncentered = false\n\n");
                    break;
                case SceneLayerKind.Instances:
                {
                    b.Append($"[node name={Q(own)} type=\"Node2D\" parent={Q(parent)}]\n{Pos()}{Hidden()}\n");
                    string layerPath = parent == "." ? own : $"{parent}/{own}";
                    var names = new HashSet<string>();
                    foreach (var i in s.InstancesOf(l.Id))
                        Instance(d, i, Unique(names, i.Name.Length > 0 ? i.Name : "object"), layerPath, p, assets, res);
                    break;
                }
            }
        }

        var areaNames = new HashSet<string>(rootNames);
        foreach (var a in s.Areas)
        {
            string n = Unique(areaNames, a.Name.Length > 0 ? a.Name : a.Kind.ToString());
            string shape = d.Sub("RectangleShape2D", $"size = Vector2({F(a.W)}, {F(a.H)})");
            b.Append($"[node name={Q(n)} type=\"Area2D\" parent=\".\" groups=[{Q(a.Kind.ToString().ToLowerInvariant())}]]\n");
            b.Append($"position = Vector2({F(a.X + a.W / 2.0)}, {F(a.Y + a.H / 2.0)})\nmetadata/kind = {Q(a.Kind.ToString())}\n");
            foreach (var (k, v) in a.Properties.OrderBy(kv => kv.Key, StringComparer.Ordinal)) b.Append($"metadata/{NodeName(k)} = {Q(v)}\n");
            b.Append($"\n[node name=\"CollisionShape2D\" type=\"CollisionShape2D\" parent={Q(n)}]\nshape = {shape}\n\n");
        }

        if (s.UiAssetId is Guid ui && p.Resolve(ui) is string uiPath)
        {
            var kit = UiKit.Load(uiPath);
            var screen = kit.Screens.FirstOrDefault(x => x.Name == s.UiScreen) ?? kit.Screens.FirstOrDefault();
            if (screen is not null)
            {
                string layer = Unique(areaNames, "UI");
                b.Append($"[node name={Q(layer)} type=\"CanvasLayer\" parent=\".\"]\n\n");
                b.Append($"[node name={Q(NodeName(screen.Name))} parent={Q(layer)} instance={d.Ext("PackedScene", UiExport.ScreenFile(res(ui), screen.Name))}]\n\n");
            }
        }
        Effects(d, s, assets, areaNames, picture, warnings);
        if (logicScript is not null)
            b.Append($"[node name={Q(Unique(areaNames, "Logic"))} type=\"Node\" parent=\".\"]\nscript = {d.Ext("Script", logicScript)}\n\n");
        string cam = Unique(areaNames, "Camera2D");
        b.Append($"[node name={Q(cam)} type=\"Camera2D\" parent=\".\"]\n");
        b.Append($"position = Vector2({F(s.CameraX + p.Settings.TargetWidth / 2.0)}, {F(s.CameraY + p.Settings.TargetHeight / 2.0)})\n");
        b.Append($"limit_left = 0\nlimit_top = 0\nlimit_right = {s.Width}\nlimit_bottom = {s.Height}\n\n");
        if (s.CameraClipAssetId is Guid camClip && assets.Anim(camClip)?.Clip(s.CameraClip) is { } cc)
            Player(d, cam, new() { [cc.Name] = Baked(d, cc, Tracks(cc, "", ".", 0, 0, 1, "offset")) }, cc.Name);
        if (s.MusicAssetId is Guid music)
            b.Append($"[node name={Q(Unique(areaNames, "Music"))} type=\"AudioStreamPlayer\" parent=\".\"]\nstream = {d.Ext("AudioStream", res(music) + ".wav")}\nautoplay = true\n\n");
        return d.ToString();
    }

    static string ColArgs(uint c) => $"{F((c >> 16 & 0xFF) / 255.0)}, {F((c >> 8 & 0xFF) / 255.0)}, {F((c & 0xFF) / 255.0)}, {F((c >> 24) / 255.0)}";
    static string Col(uint c) => $"Color({ColArgs(c)})";

    /// <summary>One "value" track of a Godot Animation: keys at the given times, with transitions and values (comma lists).</summary>
    static void ValueTrack(List<string> props, int i, string path, string times, string transitions, string values)
    {
        props.Add($"tracks/{i}/type = \"value\"");
        props.Add($"tracks/{i}/path = NodePath({Q(path)})");
        props.Add($"tracks/{i}/interp = 1");
        props.Add($"tracks/{i}/keys = {{\"times\": PackedFloat32Array({times}), \"transitions\": PackedFloat32Array({transitions}), \"update\": 0, \"values\": [{values}]}}");
    }

    /// <summary>
    /// Lighting and particles: CanvasModulate for the ambient light, PointLight2D (radial gradient texture; spots get a
    /// cone texture written next to the level), LightOccluder2D for polygons and merged rows of solid tiles, and
    /// CPUParticles2D. ponytail: glow and flicker are metadata; Godot needs a WorldEnvironment glow or a script for them.
    /// </summary>
    static void Effects(Doc d, Scene s, SceneAssets assets, HashSet<string> names, Func<string, uint[], int, int, string>? picture, List<string> warnings)
    {
        var b = d.Body;
        if (Lighting.IsOn(s)) b.Append($"[node name={Q(Unique(names, "Ambient"))} type=\"CanvasModulate\" parent=\".\"]\ncolor = {Col(s.Ambient | 0xFF000000)}\n\n");
        string? radial = null;
        int spot = 0;
        foreach (var l in s.Lights)
        {
            string tex;
            if (l.Kind == LightKind.Spot && picture is not null)
            {
                const int size = 256;
                var px = new uint[size * size];
                var cone = new Scene { Ambient = 0xFF000000, Lights = [new SceneLight { X = size / 2, Y = size / 2, Radius = size / 2, Kind = LightKind.Spot, Direction = 0, Cone = l.Cone, Color = 0xFFFFFFFF, Shadows = false }] };
                Array.Fill(px, 0xFFFFFFFF);
                Lighting.Apply(px, size, size, 0, 0, cone, [], 0);
                tex = d.Ext("Texture2D", picture($"_spot{++spot}.png", px, size, size));
            }
            else
            {
                if (l.Kind == LightKind.Spot) warnings.Add($"Spot light “{l.Name}” exported as a point light.");
                radial ??= d.Sub("GradientTexture2D", $"gradient = {d.Sub("Gradient", "offsets = PackedFloat32Array(0, 0.5, 1)", "colors = PackedColorArray(1, 1, 1, 1, 0.25, 0.25, 0.25, 1, 0, 0, 0, 1)")}",
                    "width = 256", "height = 256", "fill = 1", "fill_from = Vector2(0.5, 0.5)", "fill_to = Vector2(1, 0.5)");
                tex = radial;
            }
            b.Append($"[node name={Q(Unique(names, l.Name))} type=\"PointLight2D\" parent=\".\"]\nposition = Vector2({F(l.X)}, {F(l.Y)})\n");
            if (l.Kind == LightKind.Spot) b.Append($"rotation = {F(l.Direction * Math.PI / 180)}\n");
            b.Append($"color = {Col(l.Color | 0xFF000000)}\nenergy = {F(l.Intensity)}\ntexture = {tex}\ntexture_scale = {F(l.Radius / 128.0)}\n");
            if (l.Shadows) b.Append("shadow_enabled = true\n");
            if (l.Glow > 0) b.Append($"metadata/glow = {F(l.Glow)}\n");
            if (l.Flicker > 0) b.Append($"metadata/flicker = {F(l.Flicker)}\n");
            b.Append('\n');
        }
        void Occluder(string name, IEnumerable<(int X, int Y)> pts)
        {
            string poly = d.Sub("OccluderPolygon2D", $"polygon = PackedVector2Array({string.Join(", ", pts.Select(q => $"{F(q.X)}, {F(q.Y)}"))})");
            b.Append($"[node name={Q(Unique(names, name))} type=\"LightOccluder2D\" parent=\".\"]\noccluder = {poly}\n\n");
        }
        foreach (var o in s.Occluders) Occluder(o.Name, o.Points.Chunk(2).Select(c => (c[0], c[1])));
        if (s.TileShadows && s.Lights.Any(l => l.Shadows))
            // neighbouring solid tiles of a row become one occluder
            foreach (var row in assets.Solids(s).Where(c => !c.OneWay).GroupBy(c => (c.Y, c.H)).OrderBy(g => g.Key.Y))
            {
                var xs = row.OrderBy(c => c.X).ToList();
                for (int i = 0; i < xs.Count;)
                {
                    int j = i, end = xs[i].X + xs[i].W;
                    while (j + 1 < xs.Count && xs[j + 1].X <= end) end = Math.Max(end, xs[++j].X + xs[j].W);
                    int y = row.Key.Y, h = row.Key.H;
                    Occluder("TileShadow", [(xs[i].X, y), (end, y), (end, y + h), (xs[i].X, y + h)]);
                    i = j + 1;
                }
            }
        foreach (var e in s.Emitters)
        {
            string ramp = d.Sub("Gradient", "offsets = PackedFloat32Array(0, 1)", $"colors = PackedColorArray({ColArgs(e.Color)}, {ColArgs(e.EndColor)})");
            double endScale = e.Size > 0 ? e.EndSize / e.Size : 1;
            string curve = d.Sub("Curve", $"_data = [Vector2(0, 1), 0.0, 0.0, 0, 0, Vector2(1, {F(endScale)}), 0.0, 0.0, 0, 0]", "point_count = 2");
            b.Append($"[node name={Q(Unique(names, e.Name))} type=\"CPUParticles2D\" parent=\".\"]\nposition = Vector2({F(e.X + e.W / 2.0)}, {F(e.Y + e.H / 2.0)})\n");
            if (e.Additive) b.Append($"material = {d.Sub("CanvasItemMaterial", "blend_mode = 1")}\n");
            double rad = e.Direction * Math.PI / 180;
            b.Append($"amount = {Math.Max(1, (int)Math.Round(e.Rate * e.LifeMs / 1000))}\nlifetime = {F(e.LifeMs / 1000.0)}\npreprocess = {F(e.LifeMs / 1000.0)}\n");
            if (e.W > 0 || e.H > 0) b.Append($"emission_shape = 3\nemission_rect_extents = Vector2({F(e.W / 2.0)}, {F(e.H / 2.0)})\n");
            b.Append($"direction = Vector2({F(Math.Cos(rad))}, {F(Math.Sin(rad))})\nspread = {F(e.Spread / 2)}\ngravity = Vector2(0, {F(e.Gravity)})\n");
            b.Append($"initial_velocity_min = {F(e.Speed * 0.6)}\ninitial_velocity_max = {F(e.Speed)}\nscale_amount_min = {F(e.Size)}\nscale_amount_max = {F(e.Size)}\n");
            b.Append($"scale_amount_curve = {curve}\ncolor_ramp = {ramp}\nseed = {(uint)e.Seed}\nuse_fixed_seed = true\nmetadata/preset = {Q(e.Preset.ToString())}\n\n");
        }
    }

    static void Instance(Doc d, SceneInstance i, string n, string parent, Project p, SceneAssets assets, Func<Guid, string> res)
    {
        var b = d.Body;
        var type = p.Find(i.AssetId)?.Type;
        if (type == AssetType.Character)
        {
            b.Append($"[node name={Q(n)} parent={Q(parent)} instance={d.Ext("PackedScene", res(i.AssetId) + "_character.tscn")}]\n");
            b.Append($"position = Vector2({F(i.X)}, {F(i.Y)})\n");
            if (i.FlipX) b.Append("scale = Vector2(-1.0, 1.0)\n");
            if (i.Z != 0) b.Append($"z_index = {i.Z}\n");
            if (i.Animation is not null && assets.Character(i.AssetId) is { } c && c.Animations.TryGetValue(i.Animation, out var tag))
                b.Append($"metadata/start_state = {Q(i.Animation)}\nmetadata/start_animation = {Q(tag)}\n");
        }
        else
        {
            b.Append($"[node name={Q(n)} type=\"AnimatedSprite2D\" parent={Q(parent)}]\n");
            b.Append($"position = Vector2({F(i.X)}, {F(i.Y)})\n");
            if (i.Z != 0) b.Append($"z_index = {i.Z}\n");
            b.Append($"sprite_frames = {d.Ext("SpriteFrames", res(i.AssetId) + ".tres")}\n");
            string anim = i.Animation ?? (assets.Sprite(i.AssetId)?.Tags.FirstOrDefault()?.Name ?? "default");
            b.Append($"animation = &{Q(anim)}\nautoplay = {Q(anim)}\ncentered = false\n");
            if (i.FlipX) b.Append("flip_h = true\n");
        }
        foreach (var (k, v) in i.Properties.OrderBy(kv => kv.Key, StringComparer.Ordinal)) b.Append($"metadata/{NodeName(k)} = {Q(v)}\n");
        b.Append('\n');
        if (assets.ClipOf(i) is { } clip)
            Player(d, $"{parent}/{n}", new() { [clip.Name] = Baked(d, clip, Tracks(clip, "", ".", i.X, i.Y, type == AssetType.Character && i.FlipX ? -1 : 1, "position")) }, clip.Name);
        else if (i.Track is { Keys.Count: > 0 } t) Track(d, t, i, $"{parent}/{n}", type == AssetType.Character && i.FlipX);
    }

    /// <summary>Movement track → AnimationPlayer with position, scale, rotation and opacity tracks (autoplay).</summary>
    static void Track(Doc d, KeyframeTrack t, SceneInstance i, string nodePath, bool flipped)
    {
        var keys = t.Keys.OrderBy(k => k.TimeMs).ToList();
        string times = string.Join(", ", keys.Select(k => F(k.TimeMs / 1000.0)));
        string trans = string.Join(", ", keys.Select(k => F(k.Easing switch { Easing.EaseIn => 2, Easing.EaseOut => 0.5, Easing.EaseInOut => -2, Easing.Step => 0, _ => 1 })));
        var props = new List<string> { $"resource_name = \"track\"", $"length = {F(Math.Max(0.001, keys[^1].TimeMs / 1000.0))}", $"loop_mode = {(t.Loop ? 1 : 0)}" };
        void Add(int n, string prop, IEnumerable<string> values) => ValueTrack(props, n, ".:" + prop, times, trans, string.Join(", ", values));
        Add(0, "position", keys.Select(k => $"Vector2({F(i.X + k.X)}, {F(i.Y + k.Y)})"));
        int next = 1;
        if (keys.Any(k => k.ScaleX != 1 || k.ScaleY != 1)) Add(next++, "scale", keys.Select(k => $"Vector2({F((flipped ? -1 : 1) * k.ScaleX)}, {F(k.ScaleY)})"));
        if (keys.Any(k => k.Rotation != 0)) Add(next++, "rotation", keys.Select(k => F(k.Rotation * Math.PI / 180)));
        if (keys.Any(k => k.Opacity != 1)) Add(next++, "modulate", keys.Select(k => $"Color(1, 1, 1, {F(k.Opacity)})"));
        Player(d, nodePath, new() { ["track"] = d.Sub("Animation", [.. props]) }, "track");
    }

    /// <summary>Node names may not contain . : @ / " %.</summary>
    public static string NodeName(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s.Trim()) sb.Append(c is '.' or ':' or '@' or '/' or '"' or '%' or '\\' || char.IsControl(c) ? '_' : c);
        return sb.Length == 0 ? "Node" : sb.ToString();
    }

    /// <summary>A node name not yet in <paramref name="used"/> (Name, Name2, Name3 …); adds it.</summary>
    internal static string Unique(HashSet<string> used, string name)
    {
        string n = NodeName(name), u = n;
        for (int k = 2; !used.Add(u); k++) u = $"{n}{k}";
        return u;
    }
}
