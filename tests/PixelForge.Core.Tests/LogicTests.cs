using PixelForge.Core;

namespace PixelForge.Core.Tests;

public sealed class LogicTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "pf-logic-" + Guid.NewGuid().ToString("N"));

    public LogicTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);
    string P(string name) => Path.Combine(_dir, name);

    /// <summary>Builds a graph from (id, kind, params) nodes and "from.port>to" links.</summary>
    static LogicGraph G(IEnumerable<(string Id, LogicKind Kind, string Params)> nodes, params string[] links)
    {
        var g = new LogicGraph();
        foreach (var (id, kind, ps) in nodes)
        {
            var n = LogicGraph.NewNode(kind, id);
            foreach (var kv in ps.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('=', 2))) n.Params[kv[0]] = kv[1];
            g.Nodes.Add(n);
        }
        foreach (var l in links)
        {
            var (from, rest) = (l.Split('.')[0], l.Split('.')[1]);
            g.Links.Add(new LogicLink(from, rest.Split('>')[0], rest.Split('>')[1]));
        }
        return g;
    }

    static readonly LogicEvents None = new([], []);

    static LogicRuntime Run(LogicGraph g, double until, double step = 16, Func<double, LogicEvents>? events = null)
    {
        var r = new LogicRuntime(g);
        for (double t = 0; t <= until; t += step) r.Step(t, events?.Invoke(t) ?? None);
        return r;
    }

    [Fact]
    public void Check_finds_bad_ports_links_to_events_loops_without_wait_and_missing_params()
    {
        var ok = G([("s", LogicKind.Start, ""), ("a", LogicKind.SetVariable, ""), ("w", LogicKind.Wait, "")], "s.out>a", "a.out>w", "w.out>a");
        Assert.DoesNotContain(ok.Check(), m => !m.StartsWith("Warning:"));                       // the loop passes a Wait

        var loop = G([("s", LogicKind.Start, ""), ("a", LogicKind.SetVariable, ""), ("b", LogicKind.GiveItem, "")], "s.out>a", "a.out>b", "b.out>a");
        Assert.Contains(loop.Check(), m => m.Contains("loop without a Wait"));
        var bad = G([("s", LogicKind.Start, ""), ("c", LogicKind.Compare, "value=x"), ("t", LogicKind.Timer, "ms=0"), ("e", LogicKind.EnterArea, "")], "s.out>c", "c.out>e", "s.yes>c");
        var m = bad.Check();
        Assert.Contains(m, x => x.Contains("op must be one of"));
        Assert.Contains(m, x => x.Contains("ms must be a number of at least 1"));
        Assert.Contains(m, x => x.Contains("area is empty"));
        Assert.Contains(m, x => x.Contains("has no port “yes”"));
        Assert.Contains(m, x => x.Contains("is an event"));
        Assert.Throws<InvalidDataException>(() => new LogicRuntime(bad));

        var lonely = G([("s", LogicKind.Start, ""), ("x", LogicKind.GiveItem, "")]);
        Assert.Equal(["Warning: GiveItem “x” is not reached from any event."], lonely.Check());

        var back = LogicGraph.Deserialize(LogicGraph.Serialize(ok));
        Assert.Equal(ok.Links, back.Links);
        Assert.Equal(LogicKind.Wait, back.Node("w")!.Kind);
        Assert.Throws<InvalidDataException>(() => LogicGraph.Deserialize(LogicGraph.Serialize(ok).Replace("\"a\"", "\"s\"")));
    }

    [Fact]
    public void Conditions_choose_a_branch_and_actions_change_variables_and_items()
    {
        var g = G([("s", LogicKind.Start, ""), ("add", LogicKind.SetVariable, "variable=score;op=+=;value=5"), ("cmp", LogicKind.Compare, "variable=score;op=>=;value=5"),
                ("key", LogicKind.GiveItem, "item=key;count=2"), ("no", LogicKind.SetVariable, "variable=fail;op==;value=1"), ("has", LogicKind.HasItem, "item=key;count=2"),
                ("ok", LogicKind.SetVariable, "variable=open;op==;value=1"), ("drop", LogicKind.GiveItem, "item=key;count=-9")],
            "s.out>add", "add.out>cmp", "cmp.true>key", "cmp.false>no", "key.out>has", "has.true>ok", "ok.out>drop");
        var r = Run(g, 0);
        Assert.Equal((5.0, 0.0, 1.0), (r.Var("score"), r.Var("fail"), r.Var("open")));
        Assert.Equal(0, r.Items["key"]);                                                           // never below zero
    }

    [Fact]
    public void Timers_waits_and_once_per_frame_events_follow_the_spec()
    {
        var timers = G([("t", LogicKind.Timer, "ms=1000;repeat=yes"), ("inc", LogicKind.SetVariable, "variable=n;op=+=;value=1"),
                ("once", LogicKind.Timer, "ms=500;repeat=no"), ("one", LogicKind.SetVariable, "variable=m;op=+=;value=1")], "t.out>inc", "once.out>one");
        var r = Run(timers, 3100);
        Assert.Equal((3.0, 1.0), (r.Var("n"), r.Var("m")));

        var wait = G([("s", LogicKind.Start, ""), ("w", LogicKind.Wait, "ms=500"), ("set", LogicKind.SetVariable, "variable=late;op==;value=1"),
                ("z", LogicKind.Wait, "ms=0"), ("next", LogicKind.SetVariable, "variable=next;op==;value=1")], "s.out>w", "w.out>set", "s.out>z", "z.out>next");
        var rw = new LogicRuntime(wait);
        rw.Step(0, None);
        Assert.Equal((0.0, 0.0), (rw.Var("late"), rw.Var("next")));                              // Wait 0 = next frame
        rw.Step(16, None);
        Assert.Equal((0.0, 1.0), (rw.Var("late"), rw.Var("next")));
        rw.Step(496, None);
        Assert.Equal(0, rw.Var("late"));
        rw.Step(512, None);
        Assert.Equal(1, rw.Var("late"));

        var areas = G([("e", LogicKind.EnterArea, "area=door"), ("inc", LogicKind.SetVariable, "variable=n;op=+=;value=1"),
                ("set", LogicKind.SetState, "object=gate;state=open"), ("sc", LogicKind.StateChanged, "object=gate;state=open"), ("seen", LogicKind.SetVariable, "variable=seen;op=+=;value=1")],
            "e.out>inc", "e.out>set", "sc.out>seen");
        var ra = new LogicRuntime(areas);
        ra.Step(0, new LogicEvents(["door", "door"], []));                                         // two areas with one name: one run
        Assert.Equal((1.0, 1.0), (ra.Var("n"), ra.Var("seen")));                                   // state change handled in the same frame
        Assert.Contains(ra.Effects, e => e is SetStateEffect { Object: "gate", State: "open" });
    }

    [Fact]
    public void Load_scene_stops_and_the_step_limit_holds()
    {
        var target = Guid.NewGuid();
        var g = G([("s", LogicKind.Start, ""), ("go", LogicKind.LoadScene, $"scene={target}"), ("after", LogicKind.SetVariable, "variable=x;op==;value=1")], "s.out>go", "s.out>after");
        var r = new LogicRuntime(g);
        r.Step(0, None);
        Assert.Equal(new LoadSceneEffect(target), r.Effects.Single());
        Assert.Equal(0, r.Var("x"));

        var chain = new LogicGraph { Nodes = [LogicGraph.NewNode(LogicKind.Start, "s")] };
        for (int i = 0; i < 1100; i++)
        {
            chain.Nodes.Add(LogicGraph.NewNode(LogicKind.SetVariable, $"v{i}"));
            chain.Links.Add(new LogicLink(i == 0 ? "s" : $"v{i - 1}", "out", $"v{i}"));
        }
        var rc = new LogicRuntime(chain);
        rc.Step(0, None);
        Assert.Equal(LogicRuntime.MaxStepsPerFrame, rc.Var("score"));
        Assert.Single(rc.Log);
    }

    /// <summary>A scene with a player on a floor, a coin and a door area, and a logic graph using them.</summary>
    (Project P, Scene S, Guid Sound, Guid Next) Game(LogicGraph g)
    {
        var p = Project.Create(_dir, "L");
        var map = new Tilemap(8, 8, 8) { Tileset = new Tileset(Enumerable.Repeat(0xFF00FF00u, 16 * 8).ToArray(), 16, 8, 8) };
        Array.Fill(map.Layers[0].Tiles, -1);
        for (int x = 0; x < 8; x++) map.Layers[0].Tiles[7 * 8 + x] = 1;
        map.TileCollisions[1] = new TileCollision(0, 0, 8, 8);
        Tilemap.Save(map, P("ground.pxm"));
        var sprite = new Sprite(8, 8);
        sprite.Tags.Add(new FrameTag { Name = "idle", From = 0, To = 0 });
        ProjectFile.Save(sprite, P("hero.pxf"));
        var spriteId = p.Add(P("hero.pxf")).Id;
        var hero = new Character { Kind = CharacterKind.Player, SpriteAssetId = spriteId, OriginX = 4, OriginY = 8 };
        hero.Animations["idle"] = "idle";
        hero.Boxes.Add(new CharacterBox { Kind = BoxKind.Solid, X = 2, Y = 0, W = 4, H = 8 });
        Character.Save(hero, P("hero.pxchar"));
        var heroId = p.Add(P("hero.pxchar")).Id;
        Sfx.Save(new Sfx(), P("ding.sfx"));
        var ding = p.Add(P("ding.sfx")).Id;
        Scene.Save(new Scene(), P("next.pxscene"));
        var next = p.Add(P("next.pxscene")).Id;

        foreach (var n in g.Nodes)
            foreach (var k in n.Params.Keys.ToList()) n.Params[k] = n.Params[k].Replace("SOUND", ding.ToString()).Replace("NEXT", next.ToString());
        LogicGraph.Save(g, P("rules.pxlogic"));
        var logic = p.Add(P("rules.pxlogic")).Id;

        var s = new Scene { Width = 64, Height = 64, LogicAssetId = logic };
        s.Layers.Insert(0, new SceneLayer { Name = "Ground", Kind = SceneLayerKind.Tilemap, AssetId = p.Add(P("ground.pxm")).Id });
        s.Instances.Add(new SceneInstance { Name = "Hero", AssetId = heroId, LayerId = s.Layers[1].Id, X = 16, Y = 56 });
        s.Instances.Add(new SceneInstance { Name = "Coin", AssetId = spriteId, LayerId = s.Layers[1].Id, X = 30, Y = 48 });
        s.Areas.Add(new SceneArea { Name = "exit", X = 50, Y = 40, W = 8, H = 16 });
        Scene.Save(s, P("level.pxscene"));
        p.Add(P("level.pxscene"));
        return (p, s, ding, next);
    }

    [Fact]
    public void Game_preview_runs_the_graph_touches_hides_sounds_and_changes_scene()
    {
        var g = G([("s", LogicKind.Start, ""), ("hide", LogicKind.Show, "object=Coin;visible=no"), ("show", LogicKind.Show, "object=Coin;visible=yes"),
                ("touch", LogicKind.Touch, "object=Coin"), ("got", LogicKind.SetVariable, "variable=coins;op=+=;value=1"), ("ding", LogicKind.PlaySound, "sound=SOUND"),
                ("exit", LogicKind.EnterArea, "area=exit"), ("go", LogicKind.LoadScene, "scene=NEXT"), ("t", LogicKind.Timer, "ms=200")],
            "s.out>hide", "t.out>show", "touch.out>got", "got.out>ding", "exit.out>go");
        var (p, s, ding, next) = Game(g);
        var sb = new Sandbox(s, new SceneAssets(p), 64, 64);
        Assert.Null(sb.LogicError);
        sb.Step(16, new SandboxInput(false, false, false));
        Assert.DoesNotContain(sb.Scene.Instances, i => i.Name == "Coin");                           // hidden at the start
        for (int i = 0; i < 15; i++) sb.Step(16, new SandboxInput(false, false, false));
        Assert.Contains(sb.Scene.Instances, i => i.Name == "Coin");                                // back after 200 ms
        bool dinged = false;
        for (int i = 0; i < 120 && sb.RequestedScene is null; i++)
        {
            sb.Step(16, new SandboxInput(false, true, false));
            dinged |= sb.SoundsToPlay.Contains(ding);
        }
        Assert.True(dinged);
        Assert.Equal(1, sb.Logic!.Var("coins"));
        Assert.Equal(next, sb.RequestedScene);
        sb.Reset();
        Assert.Equal(0, sb.Logic!.Var("coins"));

        s.LogicAssetId = Guid.NewGuid();
        Assert.NotNull(new Sandbox(s, new SceneAssets(p), 64, 64).LogicError);
    }

    [Fact]
    public void Godot_export_writes_a_script_and_generic_export_the_graph()
    {
        var g = G([("s", LogicKind.Start, ""), ("w", LogicKind.Wait, "ms=250"), ("ding", LogicKind.PlaySound, "sound=SOUND"), ("t", LogicKind.Timer, "ms=1500;repeat=yes"),
                ("cmp", LogicKind.Compare, "variable=coins;op=>=;value=3"), ("go", LogicKind.LoadScene, "scene=NEXT"), ("touch", LogicKind.Touch, "object=Coin"),
                ("exit", LogicKind.EnterArea, "area=exit"), ("st", LogicKind.SetState, "object=Hero;state=idle"), ("sc", LogicKind.StateChanged, "object=Hero;state=idle")],
            "s.out>w", "w.out>ding", "t.out>cmp", "cmp.true>go", "touch.out>st", "exit.out>ding");
        var (p, _, _, _) = Game(g);
        var r = Exporter.Plan(p, new ExportProfile());
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        string gd = System.Text.Encoding.UTF8.GetString(r.Files["level_logic.gd"]);
        foreach (var part in new[] { "extends Node", "func _n_s() -> void:", "await get_tree().create_timer(0.25).timeout", "res://assets/ding.wav", "wait_time = 1.5",
                     "one_shot = false", "if float(vars.get(\"coins\", 0.0)) >= 3.0:", "change_scene_to_file(\"res://assets/next.tscn\")", "body_entered.connect(_entered_n_exit)",
                     "distance_to(", "return _obj(\"Hero\")", "if n == \"Hero\" and s == \"idle\":" })
            Assert.Contains(part, gd);
        Assert.DoesNotContain("\r", gd);
        Assert.Contains("path=\"res://assets/level_logic.gd\"", System.Text.Encoding.UTF8.GetString(r.Files["level.tscn"]));

        var gen = Exporter.Plan(p, new ExportProfile { Target = ExportTarget.Generic });
        Assert.Contains("rules_logic.json", gen.Files.Keys);
        Assert.Contains("\"logic\": \"rules_logic.json\"", System.Text.Encoding.UTF8.GetString(gen.Files["level_scene.json"]));
        Assert.Contains(p.Assets.First(a => a.Path == "ding.sfx").Id, AssetKinds.Of(AssetType.Logic).References!(P("rules.pxlogic")));
    }
}

