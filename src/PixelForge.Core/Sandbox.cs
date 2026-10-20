namespace PixelForge.Core;

/// <summary>Software renderer for a scene seen through a camera at the game's resolution (parallax, tracks, flips, hitboxes).</summary>
public static class SceneRenderer
{
    public static uint[] Render(Scene s, SceneAssets a, int camX, int camY, int w, int h, double timeMs, bool boxes = false, IEnumerable<Collider>? colliders = null)
    {
        var o = new uint[w * h];
        Array.Fill(o, s.Background | 0xFF000000);
        // per-frame memo: many objects share an asset; a file-time check and a flatten per object would cost a frame
        var looks = new Dictionary<(Guid, string?), InstanceLook?>();
        var flat = new Dictionary<(Sprite, int), uint[]>();
        InstanceLook? Look(SceneInstance i) => looks.TryGetValue((i.AssetId, i.Animation), out var l) ? l : looks[(i.AssetId, i.Animation)] = a.Look(i);
        foreach (var l in s.Layers.Where(l => l.Visible))
        {
            int lx = l.OffsetX - (int)Math.Round(camX * l.ParallaxX), ly = l.OffsetY - (int)Math.Round(camY * l.ParallaxY);
            switch (l.Kind)
            {
                case SceneLayerKind.Tilemap when l.AssetId is Guid id && a.TilemapImage(id) is var (px, pw, ph):
                    Blit(o, w, h, px, pw, ph, lx, ly, false, 1);
                    break;
                case SceneLayerKind.Image when l.AssetId is Guid id && a.Image(id) is var (px, pw, ph):
                    if (l.RepeatX)
                        for (int x = ((lx % pw) + pw) % pw - pw; x < w; x += pw) Blit(o, w, h, px, pw, ph, x, ly, false, 1);
                    else Blit(o, w, h, px, pw, ph, lx, ly, false, 1);
                    break;
                case SceneLayerKind.Instances:
                    foreach (var i in s.InstancesOf(l.Id).OrderBy(i => i.Z))
                    {
                        if (Look(i) is not { } look) continue;
                        var pose = i.Track?.Sample(timeMs) ?? new Keyframe(0);
                        int ox = i.FlipX ? look.Sprite.Width - look.OriginX : look.OriginX;
                        int dx = (int)Math.Round(i.X + pose.X) - ox + lx - l.OffsetX, dy = (int)Math.Round(i.Y + pose.Y) - look.OriginY + ly - l.OffsetY;
                        bool transformed = pose.ScaleX != 1 || pose.ScaleY != 1 || pose.Rotation != 0;
                        // off screen: a transformed sprite fits in a circle of its scaled diagonal around the pivot
                        int reach = transformed ? (int)Math.Ceiling(Math.Sqrt(look.Sprite.Width * look.Sprite.Width + look.Sprite.Height * look.Sprite.Height) * Math.Max(Math.Abs(pose.ScaleX), Math.Abs(pose.ScaleY))) : 0;
                        if (dx - reach >= w || dy - reach >= h || dx + look.Sprite.Width + reach <= 0 || dy + look.Sprite.Height + reach <= 0) continue;
                        int f = SceneAssets.FrameAt(look.Sprite, look.From, look.To, timeMs);
                        if (!flat.TryGetValue((look.Sprite, f), out var px)) flat[(look.Sprite, f)] = px = Compositor.Flatten(look.Sprite, f);
                        if (transformed)
                            BlitTransformed(o, w, h, px, look.Sprite.Width, look.Sprite.Height, look.OriginX, look.OriginY,
                                i.X + pose.X + lx - l.OffsetX, i.Y + pose.Y + ly - l.OffsetY, (i.FlipX ? -1 : 1) * pose.ScaleX, pose.ScaleY, pose.Rotation, pose.Opacity);
                        else Blit(o, w, h, px, look.Sprite.Width, look.Sprite.Height, dx, dy, i.FlipX, pose.Opacity);
                    }
                    break;
            }
        }
        if (!boxes) return o;

        foreach (var c in colliders ?? []) Outline(o, w, h, c.X - camX, c.Y - camY, c.W, c.H, c.OneWay ? 0xFF33AAFF : 0xFFFF3333);
        foreach (var ar in s.Areas) Outline(o, w, h, ar.X - camX, ar.Y - camY, ar.W, ar.H, AreaColor(ar.Kind));
        foreach (var i in s.Instances)
            if (Look(i) is { Character: { } ch } look)
                foreach (var b in ch.BoxesAt(i.Animation ?? ch.Animations.Keys.FirstOrDefault(), 0))
                {
                    int bx = i.FlipX ? look.OriginX - b.X - b.W : b.X - look.OriginX;
                    Outline(o, w, h, i.X + bx - camX, i.Y + b.Y - look.OriginY - camY, b.W, b.H, b.Kind switch
                    {
                        BoxKind.Hurt => 0xFF33DD33, BoxKind.Hit => 0xFFFF4040, BoxKind.Solid => 0xFFFFDD33, _ => 0xFFCC66FF,
                    });
                }
        return o;
    }

    static uint AreaColor(AreaKind k) => k switch
    {
        AreaKind.Spawn => 0xFF33FF66, AreaKind.Checkpoint => 0xFF33CCFF, AreaKind.Camera => 0xFFEE88EE, AreaKind.Kill => 0xFFFF2020, _ => 0xFFFFCC00,
    };

    /// <summary>Alpha-over copy, clipped to the target, optionally mirrored and faded.</summary>
    public static void Blit(uint[] dst, int dw, int dh, uint[] src, int sw, int sh, int x, int y, bool flipX, double opacity)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y), x1 = Math.Min(dw, x + sw), y1 = Math.Min(dh, y + sh);
        for (int ty = y0; ty < y1; ty++)
            for (int tx = x0; tx < x1; tx++)
            {
                int sx = flipX ? sw - 1 - (tx - x) : tx - x;
                uint p = src[(ty - y) * sw + sx];
                uint alpha = (uint)((p >> 24) * Math.Clamp(opacity, 0, 1));
                if (alpha == 0) continue;
                int di = ty * dw + tx;
                if (alpha == 255) { dst[di] = p; continue; }
                uint d = dst[di];
                uint Mix(int shift) => ((p >> shift & 0xFF) * alpha + (d >> shift & 0xFF) * (255 - alpha)) / 255 << shift;
                dst[di] = 0xFF000000 | Mix(16) | Mix(8) | Mix(0);
            }
    }

    /// <summary>
    /// Scaled and rotated copy (nearest pixel, alpha-over), like the scene editor shows it: the sprite's origin sits on
    /// the pivot; scale first, then rotation (degrees, clockwise on screen); a negative scaleX mirrors.
    /// </summary>
    public static void BlitTransformed(uint[] dst, int dw, int dh, uint[] src, int sw, int sh, int originX, int originY,
        double pivotX, double pivotY, double scaleX, double scaleY, double rotation, double opacity)
    {
        if (Math.Abs(scaleX) < 1e-6 || Math.Abs(scaleY) < 1e-6) return;
        double rad = rotation * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
        // destination bounds from the four transformed corners
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var (cx, cy) in new[] { (0.0, 0.0), (sw, 0.0), (0.0, sh), ((double)sw, (double)sh) })
        {
            double vx = (cx - originX) * scaleX, vy = (cy - originY) * scaleY;
            double x = pivotX + vx * cos - vy * sin, y = pivotY + vx * sin + vy * cos;
            (minX, minY, maxX, maxY) = (Math.Min(minX, x), Math.Min(minY, y), Math.Max(maxX, x), Math.Max(maxY, y));
        }
        int x0 = Math.Max(0, (int)Math.Floor(minX)), y0 = Math.Max(0, (int)Math.Floor(minY));
        int x1 = Math.Min(dw, (int)Math.Ceiling(maxX)), y1 = Math.Min(dh, (int)Math.Ceiling(maxY));
        double a = Math.Clamp(opacity, 0, 1);
        for (int ty = y0; ty < y1; ty++)
            for (int tx = x0; tx < x1; tx++)
            {
                // back from the pixel centre to the source: undo rotation, then scale
                double px = tx + 0.5 - pivotX, py = ty + 0.5 - pivotY;
                double rx = px * cos + py * sin, ry = -px * sin + py * cos;
                int sx = (int)Math.Floor(rx / scaleX + originX), sy = (int)Math.Floor(ry / scaleY + originY);
                if (sx < 0 || sy < 0 || sx >= sw || sy >= sh) continue;
                uint p = src[sy * sw + sx];
                uint alpha = (uint)((p >> 24) * a);
                if (alpha == 0) continue;
                int di = ty * dw + tx;
                if (alpha == 255) { dst[di] = p; continue; }
                uint d = dst[di];
                uint Mix(int shift) => ((p >> shift & 0xFF) * alpha + (d >> shift & 0xFF) * (255 - alpha)) / 255 << shift;
                dst[di] = 0xFF000000 | Mix(16) | Mix(8) | Mix(0);
            }
    }

    static void Outline(uint[] o, int w, int h, int x, int y, int bw, int bh, uint c)
    {
        void P(int px, int py) { if (px >= 0 && py >= 0 && px < w && py < h) o[py * w + px] = c; }
        for (int i = 0; i < bw; i++) { P(x + i, y); P(x + i, y + bh - 1); }
        for (int j = 0; j < bh; j++) { P(x, y + j); P(x + bw - 1, y + j); }
    }
}

/// <summary>Solid or one-way rectangle in scene pixels.</summary>
public sealed record Collider(int X, int Y, int W, int H, bool OneWay);

public sealed record SandboxInput(bool Left, bool Right, bool Jump, bool Up = false, bool Down = false);

/// <summary>
/// Game preview runtime: a platformer player (the first Player character, starting at a spawn area if there is one),
/// tile collisions, checkpoints, kill areas, triggers and a following camera. Works on a copy: the scene is never changed.
/// </summary>
public sealed class Sandbox
{
    readonly Scene _source;
    readonly SceneAssets _assets;
    readonly int _viewW, _viewH;

    public Scene Scene { get; private set; } = null!;
    public List<Collider> Colliders { get; } = [];
    public SceneInstance? Player { get; private set; }
    public double X, Y, VX, VY;
    public bool OnGround { get; private set; }
    public int CamX { get; private set; }
    public int CamY { get; private set; }
    public double TimeMs { get; private set; }
    public (double X, double Y) Respawn { get; private set; }
    public int Deaths { get; private set; }
    /// <summary>Areas entered since the last step (triggers fire once per entry).</summary>
    public List<SceneArea> Entered { get; } = [];
    readonly HashSet<Guid> _inside = [];

    // tuning: character properties speed, jump, gravity (pixels per second)
    double _speed = 90, _jump = 230, _gravity = 640;
    (int X, int Y, int W, int H) _box;   // player body relative to the origin

    public Sandbox(Scene scene, SceneAssets assets, int viewW, int viewH)
    {
        (_source, _assets, _viewW, _viewH) = (scene.Clone(), assets, viewW, viewH);   // reset returns to the scene as it was when the preview started
        foreach (var l in scene.Layers.Where(l => l.Kind == SceneLayerKind.Tilemap && l.ParallaxX == 1 && l.ParallaxY == 1 && l.AssetId is not null))
            if (assets.Tilemap(l.AssetId!.Value) is { } m)
                foreach (var layer in m.Layers.Where(x => x.Visible))
                    for (int i = 0; i < layer.Tiles.Length; i++)
                        if (layer.Tiles[i] >= 0 && m.TileCollisions.TryGetValue(layer.Tiles[i], out var c))
                            Colliders.Add(new(l.OffsetX + i % m.Width * m.TileSize + c.X, l.OffsetY + i / m.Width * m.TileSize + c.Y, c.W, c.H, c.OneWay));
        Reset();
    }

    /// <summary>Back to the start: a fresh copy of the scene, player at the spawn, time zero.</summary>
    public void Reset()
    {
        Scene = _source.Clone();
        TimeMs = 0;
        Deaths = 0;
        Entered.Clear();
        _inside.Clear();
        Player = Scene.Instances.FirstOrDefault(i => _assets.Look(i)?.Character?.Kind == CharacterKind.Player);
        var spawn = Scene.Areas.FirstOrDefault(a => a.Kind == AreaKind.Spawn);
        if (Player is not null && _assets.Look(Player)?.Character is { } c)
        {
            double P(string key, double fallback) => c.Properties.TryGetValue(key, out var v) && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : fallback;
            (_speed, _jump, _gravity) = (P("speed", 90), P("jump", 230), P("gravity", 640));
            var solid = c.Boxes.FirstOrDefault(b => b.Kind == BoxKind.Solid);
            var sprite = _assets.Look(Player)!.Sprite;
            _box = solid is null ? (-c.OriginX, -c.OriginY, sprite.Width, sprite.Height) : (solid.X - c.OriginX, solid.Y - c.OriginY, solid.W, solid.H);
        }
        (X, Y) = spawn is not null ? (spawn.X + spawn.W / 2.0, spawn.Y + spawn.H) : Player is not null ? (Player.X, Player.Y) : (Scene.CameraX + _viewW / 2.0, Scene.CameraY + _viewH / 2.0);
        Respawn = (X, Y);
        (VX, VY, OnGround) = (0, 0, false);
        Sync();
        (CamX, CamY) = Player is null ? (Scene.CameraX, Scene.CameraY) : CameraFor(X, Y);
        ClampCamera();
    }

    // the body stays in fractional pixels: rounding would hide the small sink that keeps the player grounded
    (double X, double Y, double W, double H) Body(double x, double y) => (x + (Player?.FlipX == true ? -_box.X - _box.W : _box.X), y + _box.Y, _box.W, _box.H);

    static bool Hit((double X, double Y, double W, double H) a, int x, int y, int w, int h) => a.X < x + w && x < a.X + a.W && a.Y < y + h && y < a.Y + a.H;
    static bool Hit((double X, double Y, double W, double H) a, Collider b) => Hit(a, b.X, b.Y, b.W, b.H);
    static bool Hit((double X, double Y, double W, double H) a, SceneArea b) => Hit(a, b.X, b.Y, b.W, b.H);

    public void Step(double dtMs, SandboxInput input)
    {
        dtMs = Math.Clamp(dtMs, 0, 50);   // a stalled frame must not tunnel through floors
        double dt = dtMs / 1000;
        TimeMs += dtMs;
        Entered.Clear();
        if (Player is null)
        {
            // no player: arrows move the camera
            X += ((input.Right ? 1 : 0) - (input.Left ? 1 : 0)) * 200 * dt;
            Y += ((input.Down ? 1 : 0) - (input.Up ? 1 : 0)) * 200 * dt;
            (CamX, CamY) = ((int)Math.Round(X - _viewW / 2.0), (int)Math.Round(Y - _viewH / 2.0));
            ClampCamera();
            (X, Y) = (CamX + _viewW / 2.0, CamY + _viewH / 2.0);
            return;
        }

        VX = ((input.Right ? 1 : 0) - (input.Left ? 1 : 0)) * _speed;
        if (VX != 0) Player.FlipX = VX < 0;
        if (input.Jump && OnGround) VY = -_jump;
        VY = Math.Min(VY + _gravity * dt, 600);

        // move one axis at a time, stop at the first solid
        double nx = X + VX * dt;
        if (!Colliders.Any(c => !c.OneWay && Hit(Body(nx, Y), c))) X = nx;
        else VX = 0;
        double ny = Y + VY * dt;
        double oldBottom = Y + _box.Y + _box.H;
        var landing = Colliders.Where(c => Hit(Body(X, ny), c) && (!c.OneWay || (VY > 0 && oldBottom <= c.Y))).ToList();
        OnGround = false;
        if (landing.Count == 0) Y = ny;
        else
        {
            if (VY > 0) { Y = landing.Min(c => c.Y) - _box.Y - _box.H; OnGround = true; }
            else Y = landing.Max(c => c.Y + c.H) - _box.Y;
            VY = 0;
        }

        // areas
        var body = Body(X, Y);
        foreach (var a in Scene.Areas)
        {
            bool inside = Hit(body, a);
            if (inside && _inside.Add(a.Id)) Entered.Add(a);
            else if (!inside) _inside.Remove(a.Id);
        }
        if (Entered.FirstOrDefault(a => a.Kind == AreaKind.Checkpoint) is { } cp) Respawn = (cp.X + cp.W / 2.0, cp.Y + cp.H);
        if (Scene.Areas.Any(a => a.Kind == AreaKind.Kill && Hit(body, a)) || Y + _box.Y > Scene.Height + 64) Die();

        Sync();
        (CamX, CamY) = CameraFor(X, Y);
        ClampCamera();
    }

    void Die()
    {
        Deaths++;
        (X, Y) = Respawn;
        (VX, VY) = (0, 0);
        _inside.Clear();
    }

    /// <summary>Writes the simulated position and state onto the player instance of the copy (that is what gets drawn).</summary>
    void Sync()
    {
        if (Player is null) return;
        (Player.X, Player.Y) = ((int)Math.Round(X), (int)Math.Round(Y));
        if (_assets.Look(Player)?.Character is not { } c) return;
        string[] wanted = !OnGround && VY < 0 ? ["jump"] : !OnGround ? ["fall", "jump"] : VX != 0 ? ["run", "walk"] : ["idle"];
        Player.Animation = wanted.FirstOrDefault(c.Animations.ContainsKey) ?? Player.Animation;
    }

    (int, int) CameraFor(double x, double y) => ((int)Math.Round(x - _viewW / 2.0), (int)Math.Round(y - _viewH * 0.6));

    /// <summary>Keeps the view inside the scene, or inside the camera area the player stands in.</summary>
    void ClampCamera()
    {
        var bounds = (X: 0, Y: 0, W: Scene.Width, H: Scene.Height);
        if (Player is not null && Scene.Areas.FirstOrDefault(a => a.Kind == AreaKind.Camera && Hit(Body(X, Y), a)) is { } area) bounds = (area.X, area.Y, area.W, area.H);
        CamX = bounds.W <= _viewW ? bounds.X + (bounds.W - _viewW) / 2 : Math.Clamp(CamX, bounds.X, bounds.X + bounds.W - _viewW);
        CamY = bounds.H <= _viewH ? bounds.Y + (bounds.H - _viewH) / 2 : Math.Clamp(CamY, bounds.Y, bounds.Y + bounds.H - _viewH);
    }

    public uint[] Render(bool boxes)
    {
        var o = SceneRenderer.Render(Scene, _assets, CamX, CamY, _viewW, _viewH, TimeMs, boxes, Colliders);
        if (Overlay() is { } ui)
            for (int i = 0; i < o.Length; i++) if (ui[i] >> 24 != 0) o[i] = Blending.Over(o[i], ui[i] | 0xFF000000, (ui[i] >> 24) / 255.0);
        return o;
    }

    uint[]? _overlay;
    bool _overlayTried;

    /// <summary>The scene's UI screen at the view size (drawn once; it does not move with the camera).</summary>
    uint[]? Overlay()
    {
        if (_overlayTried) return _overlay;
        _overlayTried = true;
        if (Scene.UiAssetId is not Guid id || _assets.Project.Resolve(id) is not string path) return null;
        try
        {
            var kit = UiKit.Load(path);
            var screen = kit.Screens.FirstOrDefault(s => s.Name == Scene.UiScreen) ?? kit.Screens.FirstOrDefault();
            return _overlay = screen is null ? null : UiRender.Screen(kit, screen, _viewW, _viewH, _assets.Project);
        }
        catch (InvalidDataException) { return null; }   // Check scene reports it
    }
}
