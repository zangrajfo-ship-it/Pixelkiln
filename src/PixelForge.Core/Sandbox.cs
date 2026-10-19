namespace PixelForge.Core;

/// <summary>Software renderer for a scene seen through a camera at the game's resolution (parallax, tracks, flips, hitboxes).</summary>
public static class SceneRenderer
{
    public static uint[] Render(Scene s, SceneAssets a, int camX, int camY, int w, int h, double timeMs, bool boxes = false, IEnumerable<Collider>? colliders = null)
    {
        var o = new uint[w * h];
        Array.Fill(o, s.Background | 0xFF000000);
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
                        if (a.Look(i) is not { } look) continue;
                        // ponytail: tracks move and fade objects here; scale and rotation are shown only in the editor
                        var pose = i.Track?.Sample(timeMs) ?? new Keyframe(0);
                        int f = SceneAssets.FrameAt(look.Sprite, look.From, look.To, timeMs);
                        int ox = i.FlipX ? look.Sprite.Width - look.OriginX : look.OriginX;
                        Blit(o, w, h, Compositor.Flatten(look.Sprite, f), look.Sprite.Width, look.Sprite.Height,
                            (int)Math.Round(i.X + pose.X) - ox + lx - l.OffsetX, (int)Math.Round(i.Y + pose.Y) - look.OriginY + ly - l.OffsetY, i.FlipX, pose.Opacity);
                    }
                    break;
            }
        }
        if (!boxes) return o;

        foreach (var c in colliders ?? []) Outline(o, w, h, c.X - camX, c.Y - camY, c.W, c.H, c.OneWay ? 0xFF33AAFF : 0xFFFF3333);
        foreach (var ar in s.Areas) Outline(o, w, h, ar.X - camX, ar.Y - camY, ar.W, ar.H, AreaColor(ar.Kind));
        foreach (var i in s.Instances)
            if (a.Look(i) is { Character: { } ch } look)
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

    public uint[] Render(bool boxes) => SceneRenderer.Render(Scene, _assets, CamX, CamY, _viewW, _viewH, TimeMs, boxes, Colliders);
}
