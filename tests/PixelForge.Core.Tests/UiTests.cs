using PixelForge.Core;

namespace PixelForge.Core.Tests;

public sealed class UiTests() : TempDirTest("ui")
{
    static UiKit Kit() => UiKit.Starter(UiKit.Styles["Fantasy"], UiKit.ScreenNames, 320, 180);

    [Fact]
    public void Starter_kit_has_every_screen_and_round_trips()
    {
        var k = Kit();
        Assert.Equal(UiKit.ScreenNames, k.Screens.Select(s => s.Name));
        Assert.All(k.Screens, s => Assert.NotEmpty(s.Items));
        Assert.Equal(Enum.GetValues<UiElementKind>().Length, k.Elements.Count);
        var back = UiKit.Deserialize(UiKit.Serialize(k));
        Assert.Equal(k.Screens.Sum(s => s.Items.Count), back.Screens.Sum(s => s.Items.Count));
        var keep = k.Screens[0].Items[0].ElementId;
        k.Screens[0].Items[0].ElementId = Guid.NewGuid();                    // places an element the kit does not have
        Assert.Throws<InvalidDataException>(() => UiKit.Deserialize(UiKit.Serialize(k)));
        k.Screens[0].Items[0].ElementId = keep;
        k.Elements[0].Margin = 40;
        Assert.Throws<InvalidDataException>(() => UiKit.Deserialize(UiKit.Serialize(k)));
    }

    [Fact]
    public void Button_states_look_different_and_pixel_style_has_no_soft_edges()
    {
        var k = Kit();
        var button = k.Elements.First(e => e.Kind == UiElementKind.Button);
        var states = Enum.GetValues<UiState>().Select(s => UiRender.Native(k, button, s)).ToList();
        for (int a = 0; a < states.Count; a++)
            for (int b = a + 1; b < states.Count; b++) Assert.NotEqual(states[a], states[b]);
        Assert.DoesNotContain(states[0], p => p >> 24 is > 0 and < 255);    // pixel style: crisp
        var smooth = UiKit.Starter(UiKit.Styles["Minimal"], ["HUD"], 320, 180);
        Assert.Contains(UiRender.Native(smooth, smooth.Elements[0], UiState.Normal), p => p >> 24 is > 0 and < 255);
    }

    [Fact]
    public void Nine_slice_keeps_corners_and_anchors_follow_the_screen()
    {
        const uint C = 0xFFFF0000, E = 0xFF00FF00, M = 0xFF0000FF;
        // 6×6 source: 2px corners red, edges green, middle blue
        var src = new uint[36];
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 6; x++)
            {
                bool ex = x < 2 || x > 3, ey = y < 2 || y > 3;
                src[y * 6 + x] = ex && ey ? C : ex || ey ? E : M;
            }
        var big = UiRender.NineSlice(src, 6, 6, 2, 20, 10);
        Assert.Equal(C, big[0]); Assert.Equal(C, big[1 * 20 + 1]); Assert.Equal(C, big[9 * 20 + 19]);
        Assert.Equal(E, big[0 * 20 + 10]);
        Assert.Equal(M, big[5 * 20 + 10]);

        var i = new UiPlacement { X = 300, Y = 160, W = 16, H = 16, Anchor = UiAnchor.BottomRight };
        Assert.Equal((620, 340, 16, 16), UiRender.Resolve(i, 320, 180, 640, 360));
        i.Anchor = UiAnchor.Center;
        Assert.Equal((460, 250, 16, 16), UiRender.Resolve(i, 320, 180, 640, 360));
        i.Anchor = UiAnchor.Stretch;
        Assert.Equal((300, 160, 336, 196), UiRender.Resolve(i, 320, 180, 640, 360));
    }

    [Fact]
    public void Screens_render_export_for_godot_and_show_over_the_game()
    {
        var p = Project.Create(_dir, "g");
        var r = TemplateEngine.Create(TemplateLibrary.BuiltIn().Single(t => t.Manifest.Id == "ui-scifi"), new Dictionary<string, string> { ["screens"] = "Main menu, HUD" }, p, "ui", false);
        var kit = UiKit.Load(p.FullPath(r.Primary));
        var menu = UiRender.Screen(kit, kit.Screens[0], 320, 180, p);
        Assert.Equal(kit.Style.Dark, menu[0]);                                 // menu background
        var hud = UiRender.Screen(kit, kit.Screens[1], 320, 180, p);
        Assert.Equal(0u, hud[180 / 2 * 320 + 160]);                            // HUD is see-through in the middle

        var scene = new Scene { Width = 320, Height = 180, UiAssetId = r.Primary.Id, UiScreen = "HUD" };
        Scene.Save(scene, P("level.pxscene"));
        p.Add(P("level.pxscene"));
        Assert.DoesNotContain(SceneCheck.Run(scene, p), x => x.Severity == "error");
        var sb = new Sandbox(scene, new SceneAssets(p), 320, 180);
        var view = sb.Render(false);
        int barPixel = 10 * 320 + 20;
        Assert.NotEqual(scene.Background | 0xFF000000, view[barPixel]);        // the health bar is drawn over the game
        Assert.Contains(SceneCheck.Run(new Scene { UiAssetId = r.Primary.Id, UiScreen = "Nope" }, p), x => x.Message.Contains("no screen"));

        var godot = Exporter.Plan(p, new ExportProfile { Scales = [2] });
        Assert.True(godot.Ok, string.Join("; ", godot.Errors));
        string theme = godot.TextEnding("_theme.tres");
        foreach (var s in new[] { "normal", "hover", "pressed", "disabled" }) Assert.Contains($"Button/styles/{s} = SubResource", theme);
        Assert.Contains("texture_margin_left = 4.0", theme);
        string screen = godot.TextEnding("_hud.tscn");
        Assert.Contains("type=\"ProgressBar\"", screen);
        Assert.Contains("anchor_left = 1.0", screen);                          // top-right icon
        string level = godot.Text("level.tscn");
        Assert.Contains("type=\"CanvasLayer\"", level);
        Assert.Contains("_hud.tscn", level);
        Assert.Contains(godot.Files.Keys, k => k.EndsWith("_ui/button_hover.png"));

        var generic = Exporter.Plan(p, new ExportProfile { Target = ExportTarget.Generic, OutputFolder = "export/g" });
        Assert.Contains(generic.Files.Keys, k => k.EndsWith("_ui.json"));
        Assert.Contains("\"anchor\": \"TopRight\"", generic.TextEnding("_ui.json"));
    }

    [Fact]
    public void Extra_scales_write_larger_copies_of_pictures()
    {
        var p = Project.Create(_dir, "g");
        ImageIO.SavePng(P("dot.png"), [0xFFFF0000, 0xFF00FF00], 2, 1);
        p.Add(P("dot.png"));
        var v = new VectorDoc { Width = 4, Height = 4 };
        v.Shapes.Add(new VShape { Kind = VShapeKind.Ellipse, W = 4, H = 4, Fill = VPaint.Solid(0xFF0000FF) });
        VectorDoc.Save(v, P("ball.pxvec"));
        p.Add(P("ball.pxvec"));
        var r = Exporter.Plan(p, new ExportProfile { Scales = [2, 4] });
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Contains("dot@2x.png", r.Files.Keys);
        Assert.Contains("dot@4x.png", r.Files.Keys);
        Assert.Contains("ball@4x.png", r.Files.Keys);
        Assert.DoesNotContain(r.Files.Keys, k => k.Contains("normal@"));
    }
}
