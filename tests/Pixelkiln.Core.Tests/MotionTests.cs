using Pixelkiln.Core;

namespace Pixelkiln.Core.Tests;

public sealed class MotionTests() : TempDirTest("motion")
{
    const uint R = 0xFFFF0000, B = 0xFF0000FF, G = 0xFF00FF00;

    static AnimClip Slide(AnimPlay play, bool reverse = false) => new()
    {
        Name = "slide", Play = play, Reverse = reverse,
        Tracks = [new AnimTrack { Property = AnimProperty.X, Keys = [new AnimKey(0, 0), new AnimKey(1000, 100)] }],
    };

    [Fact]
    public void Play_modes_reverse_and_curves()
    {
        Assert.Equal(25, Slide(AnimPlay.Loop).Value("", AnimProperty.X, 1250), 6);
        Assert.Equal(100, Slide(AnimPlay.Once).Value("", AnimProperty.X, 5000), 6);
        Assert.Equal(75, Slide(AnimPlay.PingPong).Value("", AnimProperty.X, 1250), 6);
        Assert.Equal(75, Slide(AnimPlay.Loop, reverse: true).Value("", AnimProperty.X, 250), 6);
        Assert.Equal(1, Slide(AnimPlay.Loop).Value("", AnimProperty.Opacity, 300));          // no track: neutral value
        Assert.Equal(0, Slide(AnimPlay.Loop).Value("arm", AnimProperty.X, 300));             // other target

        var linear = new AnimKey(0, 0, AnimEase.Bezier, 0, 0, 1, 1);
        foreach (var u in new[] { 0.1, 0.5, 0.9 }) Assert.Equal(u, AnimClip.Ease(linear, u), 4);
        var ease = new AnimKey(0, 0, AnimEase.Bezier, 0.42, 0, 0.58, 1);                  // CSS ease-in-out
        Assert.Equal(0.5, AnimClip.Ease(ease, 0.5), 4);
        Assert.True(AnimClip.Ease(ease, 0.2) < 0.2);
        Assert.Equal(0, AnimClip.Ease(new AnimKey(0, 0, AnimEase.Step), 0.99));
    }

    [Fact]
    public void Library_round_trips_and_rejects_bad_clips()
    {
        var lib = new AnimLibrary { Clips = [Slide(AnimPlay.PingPong), new AnimClip { Name = "fade", Tracks = [new AnimTrack { Target = "arm", Property = AnimProperty.Opacity, Keys = [new AnimKey(0, 1, AnimEase.Bezier, 0.1, 0.2, 0.3, 0.4), new AnimKey(500, 0)] }] }] };
        AnimLibrary.Save(lib, P("a.pxanim"));
        var back = AnimLibrary.Load(P("a.pxanim"));
        Assert.Equal(["slide", "fade"], back.Clips.Select(c => c.Name));
        Assert.Equal(AnimPlay.PingPong, back.Clips[0].Play);
        Assert.Equal(lib.Clips[1].Tracks[0].Keys, back.Clips[1].Tracks[0].Keys);

        string json = AnimLibrary.Serialize(lib);
        Assert.Throws<InvalidDataException>(() => AnimLibrary.Deserialize(json.Replace("\"fade\"", "\"slide\"")));
        Assert.Throws<InvalidDataException>(() => AnimLibrary.Deserialize(json.Replace("\"TimeMs\": 500", "\"TimeMs\": -5")));
        lib.Clips[0].Tracks.Add(lib.Clips[0].Tracks[0].Clone());
        Assert.Throws<InvalidDataException>(() => AnimLibrary.Deserialize(AnimLibrary.Serialize(lib)));
    }

    [Fact]
    public void Clip_from_an_old_track_moves_the_same()
    {
        var track = new KeyframeTrack { Keys = [new Keyframe(0), new Keyframe(600, X: 30, Y: -12, Rotation: 90, Opacity: 0.5, Easing: Easing.EaseIn), new Keyframe(1200, X: 10)], Loop = true };
        var clip = AnimClip.FromTrack(track, "t");
        foreach (var t in new[] { 0.0, 150, 600, 900, 1500 })
        {
            var a = track.Sample(t);
            var b = clip.Pose("", t);
            Assert.Equal((a.X, a.Y, a.Rotation, a.Opacity), (b.X, b.Y, b.Rotation, b.Opacity));
        }
    }

    static List<RigPart> Arm() =>
    [
        new() { Name = "shoulder", X = 0, Y = 0, W = 2, H = 2, PivotX = 0, PivotY = 0 },
        new() { Name = "hand", X = 2, Y = 0, W = 2, H = 2, Parent = "shoulder", OffsetX = 10, Z = 1 },
    ];

    [Fact]
    public void Turning_a_parent_carries_its_children()
    {
        var clip = new AnimClip { Tracks = [new AnimTrack { Target = "shoulder", Property = AnimProperty.Rotation, Keys = [new AnimKey(0, 90)] }] };
        var pose = Rig.Pose(Arm(), clip, 0);
        var hand = pose.Single(p => p.Part.Name == "hand");
        Assert.Equal((0, 10, 90), (Math.Round(hand.X, 6), Math.Round(hand.Y, 6), hand.Rotation));
        Assert.Equal(["shoulder", "hand"], pose.Select(p => p.Part.Name));

        // the hand is cut from x 2–3 of the sprite and drawn at its posed place
        var frame = new uint[] { R, R, B, B, R, R, B, B };
        var dst = new uint[32 * 32];
        Rig.Draw(dst, 32, 32, frame, 4, 2, Arm(), null, 0, 5, 5);
        Assert.Equal(R, dst[5 * 32 + 5]);
        Assert.Equal(B, dst[5 * 32 + 15]);

        var bad = Arm();
        bad[0].Parent = "hand";                       // loop
        bad.Add(new RigPart { Name = "x", Parent = "ghost", W = 9 });
        var problems = Rig.Check(bad, 4, 2);
        Assert.Contains(problems, p => p.Contains("loop"));
        Assert.Contains(problems, p => p.Contains("ghost"));
        Assert.Contains(problems, p => p.Contains("outside"));
        Assert.Throws<InvalidDataException>(() => Rig.Pose(bad, null, 0));
    }

    [Fact]
    public void Character_v2_keeps_its_rig_and_rejects_duplicate_parts()
    {
        var anim = Guid.NewGuid();
        var c = new Character { Parts = Arm(), RigAnimAssetId = anim, RigClips = { ["idle"] = "breathe" } };
        var back = Character.Deserialize(Character.Serialize(c));
        Assert.Equal(["shoulder", "hand"], back.Parts.Select(p => p.Name));
        Assert.Equal((anim, "breathe", 10.0), (back.RigAnimAssetId!.Value, back.RigClips["idle"], back.Parts[1].OffsetX));
        c.Parts[1].Name = "shoulder";
        Assert.Throws<InvalidDataException>(() => Character.Deserialize(Character.Serialize(c)));
    }

    /// <summary>A project with a 2×2 red sprite and an animation library; scene objects and UI elements play its clips.</summary>
    (Project P, Guid Sprite, Guid Anim) Setup()
    {
        var p = Project.Create(_dir, "M");
        var s = new Sprite(2, 2);
        Array.Fill(s.Cel(0, 0), R);
        ProjectFile.Save(s, P("dot.pxf"));
        var lib = new AnimLibrary { Clips = [Slide(AnimPlay.Once), new AnimClip { Name = "shake", Tracks = [new AnimTrack { Property = AnimProperty.Y, Keys = [new AnimKey(0, 3)] }] },
            new AnimClip { Name = "bob", Tracks = [new AnimTrack { Target = "shoulder", Property = AnimProperty.Y, Keys = [new AnimKey(0, 3)] }] }] };
        AnimLibrary.Save(lib, P("moves.pxanim"));
        return (p, p.Add(P("dot.pxf")).Id, p.Add(P("moves.pxanim")).Id);
    }

    [Fact]
    public void Scene_objects_play_library_clips_and_rigs_draw_from_parts()
    {
        var (p, dot, anim) = Setup();
        Assert.Equal(AssetType.Anim, p.Find(anim)!.Type);
        var s = new Scene { Width = 200, Height = 20, Background = 0xFF000000 };
        s.Instances.Add(new SceneInstance { AssetId = dot, LayerId = s.Layers[0].Id, X = 0, Y = 0, ClipAssetId = anim, Clip = "slide" });
        Assert.Contains(anim, s.ReferencedAssets());
        var o = SceneRenderer.Render(s, new SceneAssets(p), 0, 0, 200, 20, 500);
        Assert.Equal(R, o[50]);                       // slid half way
        Assert.Equal(0xFF000000, o[0]);

        // a rigged character: two parts of its sprite, the second hung 6 px to the right
        var wide = new Sprite(4, 2);
        for (int i = 0; i < 8; i++) wide.Cel(0, 0)[i] = i % 4 < 2 ? G : B;
        ProjectFile.Save(wide, P("wide.pxf"));
        var ch = new Character { SpriteAssetId = p.Add(P("wide.pxf")).Id, Parts = Arm(), RigAnimAssetId = anim, RigClips = { ["idle"] = "bob" } };
        ch.Parts[1].OffsetX = 6;
        Character.Save(ch, P("rig.pxchar"));
        var rigId = p.Add(P("rig.pxchar")).Id;
        s.Instances.Clear();
        s.Instances.Add(new SceneInstance { AssetId = rigId, LayerId = s.Layers[0].Id, X = 10, Y = 5 });
        o = SceneRenderer.Render(s, new SceneAssets(p), 0, 0, 200, 20, 0);
        Assert.Equal(G, o[8 * 200 + 10]);             // shoulder at the origin, moved down 3 by the clip
        Assert.Equal(B, o[8 * 200 + 16]);             // hand 6 px further
        Assert.Contains(p.Find(anim)!.Id, AssetKinds.Of(AssetType.Character).References!(P("rig.pxchar")));
    }

    [Fact]
    public void Godot_export_bakes_clips_and_builds_the_cutout_rig()
    {
        var (p, dot, anim) = Setup();
        var wide = new Sprite(4, 2);
        ProjectFile.Save(wide, P("wide.pxf"));
        var ch = new Character { SpriteAssetId = p.Add(P("wide.pxf")).Id, Parts = Arm(), RigAnimAssetId = anim, RigClips = { ["idle"] = "bob" } };
        ch.Animations["idle"] = "idle";
        Character.Save(ch, P("rig.pxchar"));
        var rig = p.Add(P("rig.pxchar"));
        var s = new Scene { Width = 64, Height = 64, CameraClipAssetId = anim, CameraClip = "shake" };
        s.Instances.Add(new SceneInstance { Name = "Dot", AssetId = dot, LayerId = s.Layers[0].Id, X = 4, ClipAssetId = anim, Clip = "slide" });
        s.Instances.Add(new SceneInstance { Name = "Guy", AssetId = rig.Id, LayerId = s.Layers[0].Id });
        Scene.Save(s, P("level.pxscene"));
        p.Add(P("level.pxscene"));

        var r = Exporter.Plan(p, new ExportProfile());
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        string level = r.TextEnding("level.tscn"), guy = r.TextEnding("rig_character.tscn");
        Assert.Contains("NodePath(\".:position\")", level);
        Assert.Contains("Vector2(54.0, 0.0)", level);                  // slide at 0.5 s: 4 + 50
        Assert.Contains("NodePath(\".:offset\")", level);              // camera shake
        Assert.Contains("[node name=\"hand\" type=\"Sprite2D\" parent=\"Rig/shoulder\"]", guy);
        Assert.Contains("NodePath(\"Rig/shoulder:position\")", guy);
        Assert.Contains("autoplay = \"bob\"", guy);
        Assert.DoesNotContain("AnimatedSprite2D", guy);

        var g = Exporter.Plan(p, new ExportProfile { Target = ExportTarget.Generic });
        string gs = g.TextEnding("level_scene.json");
        Assert.Contains("\"clip\": \"slide\"", gs);
        Assert.Contains("moves.json", gs);
        Assert.Contains("\"pivot\"", g.TextEnding("rig_character.json"));
    }

    [Fact]
    public void Ui_elements_play_clips_and_the_camera_clip_round_trips()
    {
        var (p, _, anim) = Setup();
        var kit = UiKit.Starter(UiKit.Styles["Minimal"], ["HUD"], 200, 40);
        var item = kit.Screens[0].Items[0];
        var still = UiRender.Screen(kit, kit.Screens[0], 200, 40, p);
        item.ClipAssetId = anim;
        item.Clip = "slide";
        Assert.Contains(anim, kit.ReferencedAssets());
        var moved = UiRender.Screen(kit, kit.Screens[0], 200, 40, p, 1000);
        Assert.NotEqual(still, moved);
        Assert.Equal(still, UiRender.Screen(kit, kit.Screens[0], 200, 40, p, 0));      // slide starts at 0
        var back = UiKit.Deserialize(UiKit.Serialize(kit));
        Assert.Equal((anim, "slide"), (back.Screens[0].Items[0].ClipAssetId!.Value, back.Screens[0].Items[0].Clip));

        var s = new Scene { Width = 64, Height = 64, CameraClipAssetId = anim, CameraClip = "shake" };
        var sv = Scene.Deserialize(Scene.Serialize(s));
        Assert.Equal((anim, "shake"), (sv.CameraClipAssetId!.Value, sv.CameraClip));
        Assert.Contains(anim, s.ReferencedAssets());
    }
}
