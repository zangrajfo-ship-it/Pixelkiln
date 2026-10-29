using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pixelkiln.Core;

public enum LogicKind
{
    // events
    Start, EnterArea, Touch, Timer, StateChanged,
    // conditions
    Compare, HasItem,
    // actions
    SetVariable, GiveItem, PlaySound, Show, SetState, PlayClip, Wait, LoadScene,
}

public sealed class LogicNode
{
    public string Id { get; set; } = "n1";
    public LogicKind Kind { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public Dictionary<string, string> Params { get; set; } = [];
    public LogicNode Clone() => new() { Id = Id, Kind = Kind, X = X, Y = Y, Params = new(Params) };
    public string P(string key, string fallback = "") => Params.TryGetValue(key, out var v) ? v : fallback;

    /// <summary>A parameter as a number (invariant culture), or the fallback when it is missing or not a number.</summary>
    public double Number(string key, double fallback = 0) =>
        double.TryParse(P(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
}

/// <summary>Flow from a node's port (out, true, false) to another node.</summary>
public sealed record LogicLink(string From, string Port, string To);

/// <summary>Visual game logic (.pxlogic): event, condition and action nodes joined by flow links. Semantics: .ai/context/logic.md.</summary>
public sealed class LogicGraph
{
    public const int Version = 1;
    public List<LogicNode> Nodes { get; set; } = [];
    public List<LogicLink> Links { get; set; } = [];

    public LogicGraph Clone() => new() { Nodes = [.. Nodes.Select(n => n.Clone())], Links = [.. Links] };
    public LogicNode? Node(string id) => Nodes.FirstOrDefault(n => n.Id == id);

    public static bool IsEvent(LogicKind k) => k <= LogicKind.StateChanged;
    public static bool IsCondition(LogicKind k) => k is LogicKind.Compare or LogicKind.HasItem;
    public static string[] Ports(LogicKind k) => IsCondition(k) ? ["true", "false"] : ["out"];

    /// <summary>Parameters each kind needs, with a default for new nodes.</summary>
    public static IReadOnlyList<(string Key, string Default)> ParamsOf(LogicKind k) => k switch
    {
        LogicKind.EnterArea => [("area", "")],
        LogicKind.Touch => [("object", "")],
        LogicKind.Timer => [("ms", "1000"), ("repeat", "no")],
        LogicKind.StateChanged => [("object", ""), ("state", "")],
        LogicKind.Compare => [("variable", "score"), ("op", ">="), ("value", "10")],
        LogicKind.HasItem => [("item", "key"), ("count", "1")],
        LogicKind.SetVariable => [("variable", "score"), ("op", "+="), ("value", "1")],
        LogicKind.GiveItem => [("item", "key"), ("count", "1")],
        LogicKind.PlaySound => [("sound", "")],
        LogicKind.Show => [("object", ""), ("visible", "no")],
        LogicKind.SetState => [("object", ""), ("state", "")],
        LogicKind.PlayClip => [("object", ""), ("library", ""), ("clip", "")],
        LogicKind.Wait => [("ms", "500")],
        LogicKind.LoadScene => [("scene", "")],
        _ => [],
    };

    public static readonly string[] CompareOps = ["==", "!=", "<", "<=", ">", ">="];
    public static readonly string[] SetOps = ["=", "+=", "-="];

    public static LogicNode NewNode(LogicKind k, string id, double x = 0, double y = 0) =>
        new() { Id = id, Kind = k, X = x, Y = y, Params = ParamsOf(k).ToDictionary(p => p.Key, p => p.Default) };

    static bool Num(string s, out double v) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    /// <summary>Errors that make the graph unusable, then warnings (prefixed "Warning:").</summary>
    public List<string> Check()
    {
        var o = new List<string>();
        foreach (var g in Nodes.GroupBy(n => n.Id).Where(g => g.Count() > 1)) o.Add($"Two nodes have the id “{g.Key}”.");
        foreach (var n in Nodes)
        {
            string Need(string key) => n.P(key).Trim();
            void Bad(string what) => o.Add($"{n.Kind} “{n.Id}”: {what}");
            foreach (var (key, _) in ParamsOf(n.Kind))
                if (key is not ("visible" or "repeat" or "count") && Need(key).Length == 0) Bad($"{key} is empty.");
            switch (n.Kind)
            {
                case LogicKind.Timer when !Num(Need("ms"), out double ms) || ms < 1: Bad("ms must be a number of at least 1."); break;
                case LogicKind.Wait when !Num(Need("ms"), out double ms) || ms < 0: Bad("ms must be a number of at least 0."); break;
                case LogicKind.Compare when !CompareOps.Contains(Need("op")) || !Num(Need("value"), out _): Bad("op must be one of == != < <= > >= and value a number."); break;
                case LogicKind.SetVariable when !SetOps.Contains(Need("op")) || !Num(Need("value"), out _): Bad("op must be = += or -= and value a number."); break;
                case LogicKind.HasItem or LogicKind.GiveItem when Need("count").Length > 0 && !int.TryParse(Need("count"), out _): Bad("count must be a whole number."); break;
                case LogicKind.PlaySound or LogicKind.LoadScene when !Guid.TryParse(Need(n.Kind == LogicKind.PlaySound ? "sound" : "scene"), out _): Bad("choose an asset."); break;
                case LogicKind.PlayClip when !Guid.TryParse(Need("library"), out _): Bad("choose an animation library."); break;
            }
        }
        var ids = Nodes.Select(n => n.Id).ToHashSet();
        foreach (var l in Links)
        {
            if (Node(l.From) is not { } from) { o.Add($"A link starts at the unknown node “{l.From}”."); continue; }
            if (!Ports(from.Kind).Contains(l.Port)) o.Add($"{from.Kind} “{from.Id}” has no port “{l.Port}”.");
            if (!ids.Contains(l.To)) o.Add($"A link from “{l.From}” goes to the unknown node “{l.To}”.");
            else if (IsEvent(Node(l.To)!.Kind)) o.Add($"“{l.To}” is an event; events start chains and cannot be linked to.");
        }
        // a loop must pass a Wait, or it would run forever within one frame
        var next = Links.GroupBy(l => l.From).ToDictionary(g => g.Key, g => g.Select(l => l.To).ToList());
        var state = new Dictionary<string, int>();   // 1 = on the path, 2 = done
        bool Loop(string id)
        {
            if (Node(id) is not { } n || n.Kind == LogicKind.Wait) return false;
            if (state.GetValueOrDefault(id) == 1) return true;
            if (state.GetValueOrDefault(id) == 2) return false;
            state[id] = 1;
            bool found = next.GetValueOrDefault(id)?.Any(Loop) == true;
            state[id] = 2;
            return found;
        }
        foreach (var n in Nodes)
            if (state.GetValueOrDefault(n.Id) == 0 && Loop(n.Id)) { o.Add($"The nodes around “{n.Id}” form a loop without a Wait."); break; }
        if (o.Count > 0) return o;

        var reached = new HashSet<string>();
        void Reach(string id) { if (reached.Add(id)) foreach (var t in next.GetValueOrDefault(id) ?? []) Reach(t); }
        foreach (var e in Nodes.Where(n => IsEvent(n.Kind))) Reach(e.Id);
        foreach (var n in Nodes.Where(n => !reached.Contains(n.Id))) o.Add($"Warning: {n.Kind} “{n.Id}” is not reached from any event.");
        return o;
    }

    // ---------- .pxlogic ----------

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    sealed record Dto(int Version, List<LogicNode>? Nodes, List<LogicLink>? Links);

    public static string Serialize(LogicGraph g) => JsonSerializer.Serialize(new Dto(Version, g.Nodes, g.Links), Json);

    public static LogicGraph Deserialize(string json)
    {
        var d = JsonSerializer.Deserialize<Dto>(json, Json) ?? throw new InvalidDataException("Empty logic file.");
        var g = new LogicGraph { Nodes = d.Nodes ?? [], Links = d.Links ?? [] };
        foreach (var n in g.Nodes) n.Params ??= [];
        if (g.Nodes.Any(n => string.IsNullOrWhiteSpace(n.Id)) || g.Nodes.Select(n => n.Id).Distinct().Count() != g.Nodes.Count)
            throw new InvalidDataException("Logic node ids must be unique and not empty.");
        if (g.Links.Any(l => l.From is null || l.To is null || l.Port is null)) throw new InvalidDataException("A logic link is incomplete.");
        return g;
    }

    public static void Save(LogicGraph g, string path) => DocumentFile.Save(path, Serialize(g), Version);
    public static LogicGraph Load(string path) => DocumentFile.Load(path, "logic", Version, Deserialize);
}

/// <summary>What happened in the game this frame, as the logic sees it.</summary>
public sealed record LogicEvents(IReadOnlyList<string> EnteredAreas, IReadOnlyList<string> Touched);

/// <summary>Effects the game must carry out (the runtime itself only keeps variables and the inventory).</summary>
public abstract record LogicEffect;
public sealed record PlaySoundEffect(Guid Sound) : LogicEffect;
public sealed record ShowEffect(string Object, bool Visible) : LogicEffect;
public sealed record SetStateEffect(string Object, string State) : LogicEffect;
public sealed record PlayClipEffect(string Object, Guid Library, string Clip) : LogicEffect;
public sealed record LoadSceneEffect(Guid Scene) : LogicEffect;

/// <summary>
/// Deterministic interpreter for a logic graph (spec: .ai/context/logic.md). Call <see cref="Step"/> once per frame with
/// the time since the scene started; read <see cref="Effects"/> after each step.
/// </summary>
public sealed class LogicRuntime
{
    public const int MaxStepsPerFrame = 1000;
    readonly LogicGraph _g;
    readonly Dictionary<string, List<LogicLink>> _out;
    readonly List<(double Due, long Order, LogicNode Wait)> _waits = [];
    readonly Dictionary<string, double> _nextTimer = [];
    long _order;
    bool _started, _stopped;
    int _steps;
    HashSet<string> _firedThisFrame = [];
    readonly Queue<(string Object, string State)> _stateChanges = new();

    public Dictionary<string, double> Variables { get; } = [];
    public Dictionary<string, int> Items { get; } = [];
    public List<LogicEffect> Effects { get; } = [];
    public List<string> Log { get; } = [];

    public LogicRuntime(LogicGraph g)
    {
        if (g.Check().FirstOrDefault(m => !m.StartsWith("Warning:")) is { } error) throw new InvalidDataException(error);
        _g = g.Clone();
        _out = _g.Links.GroupBy(l => l.From).ToDictionary(x => x.Key, x => x.ToList());
        foreach (var t in _g.Nodes.Where(n => n.Kind == LogicKind.Timer)) _nextTimer[t.Id] = t.Number("ms");
    }

    public double Var(string name) => Variables.GetValueOrDefault(name.Trim());

    public void Step(double timeMs, LogicEvents ev)
    {
        Effects.Clear();
        if (_stopped) return;
        _steps = 0;
        _firedThisFrame = [];
        // 1. waits that are due, in the order they were asked for
        foreach (var w in _waits.Where(w => w.Due <= timeMs).OrderBy(w => w.Due).ThenBy(w => w.Order).ToList())
        {
            _waits.Remove(w);
            Continue(w.Wait, "out", timeMs);
            if (_stopped) return;
        }
        // 2. events
        if (!_started) { _started = true; Fire(LogicKind.Start, _ => true, timeMs); }
        foreach (var t in _g.Nodes.Where(n => n.Kind == LogicKind.Timer))
            if (_nextTimer.TryGetValue(t.Id, out double due) && timeMs >= due)
            {
                if (t.P("repeat") == "yes")
                {
                    double every = Math.Max(1, t.Number("ms")), next = due + every;
                    while (next <= timeMs) next += every;   // a slow frame fires once, not once per missed period
                    _nextTimer[t.Id] = next;
                }
                else _nextTimer.Remove(t.Id);
                Run(t, timeMs);
            }
        Fire(LogicKind.EnterArea, n => ev.EnteredAreas.Contains(n.P("area").Trim()), timeMs);
        Fire(LogicKind.Touch, n => ev.Touched.Contains(n.P("object").Trim()), timeMs);
        while (!_stopped && _stateChanges.TryDequeue(out var sc))
            Fire(LogicKind.StateChanged, n => n.P("object").Trim() == sc.Object && n.P("state").Trim() == sc.State, timeMs);
    }

    void Fire(LogicKind kind, Func<LogicNode, bool> match, double t)
    {
        foreach (var n in _g.Nodes.Where(n => n.Kind == kind && match(n)))
        {
            if (_stopped) return;
            Run(n, t);
        }
    }

    /// <summary>Runs an event node (at most once a frame) and its chain.</summary>
    void Run(LogicNode e, double t)
    {
        if (!_firedThisFrame.Add(e.Id)) return;
        Continue(e, "out", t);
    }

    void Continue(LogicNode from, string port, double t)
    {
        foreach (var l in _out.GetValueOrDefault(from.Id) ?? [])
        {
            if (_stopped) return;
            if (l.Port == port && _g.Node(l.To) is { } n) Exec(n, t);
        }
    }

    void Exec(LogicNode n, double t)
    {
        if (++_steps > MaxStepsPerFrame)
        {
            if (_steps == MaxStepsPerFrame + 1) Log.Add($"{t:0} ms: more than {MaxStepsPerFrame} nodes in one frame; the rest waits for the next frame.");
            return;
        }
        string Name(string key) => n.P(key).Trim();
        switch (n.Kind)
        {
            case LogicKind.Compare:
            {
                double a = Var(Name("variable")), b = n.Number("value");
                bool yes = Name("op") switch { "==" => a == b, "!=" => a != b, "<" => a < b, "<=" => a <= b, ">" => a > b, _ => a >= b };
                Continue(n, yes ? "true" : "false", t);
                return;
            }
            case LogicKind.HasItem:
                Continue(n, Items.GetValueOrDefault(Name("item")) >= (int)n.Number("count", 1) ? "true" : "false", t);
                return;
            case LogicKind.SetVariable:
            {
                string v = Name("variable");
                double x = n.Number("value");
                Variables[v] = Name("op") switch { "+=" => Var(v) + x, "-=" => Var(v) - x, _ => x };
                break;
            }
            case LogicKind.GiveItem:
                Items[Name("item")] = Math.Max(0, Items.GetValueOrDefault(Name("item")) + (int)n.Number("count", 1));
                break;
            case LogicKind.PlaySound when Guid.TryParse(Name("sound"), out var s): Effects.Add(new PlaySoundEffect(s)); break;
            case LogicKind.Show: Effects.Add(new ShowEffect(Name("object"), n.P("visible") == "yes")); break;
            case LogicKind.SetState:
                Effects.Add(new SetStateEffect(Name("object"), Name("state")));
                _stateChanges.Enqueue((Name("object"), Name("state")));
                break;
            case LogicKind.PlayClip when Guid.TryParse(Name("library"), out var lib): Effects.Add(new PlayClipEffect(Name("object"), lib, Name("clip"))); break;
            case LogicKind.Wait:
                _waits.Add((t + Math.Max(0, n.Number("ms")) + (n.Number("ms") <= 0 ? 0.001 : 0), _order++, n));   // Wait 0 = next frame
                return;
            case LogicKind.LoadScene when Guid.TryParse(Name("scene"), out var sc):
                Effects.Add(new LoadSceneEffect(sc));
                _stopped = true;
                return;
        }
        Continue(n, "out", t);
    }
}
