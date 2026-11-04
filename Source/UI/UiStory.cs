using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The Story window (F4 in Sandbox by default; docs/story/STORY.md, "The window"): the story layer's data in 2D, on the
/// host.
/// <list type="bullet">
/// <item>World: the story world as a graph: players, people, places and constructs, their relationships as arrows. Click
/// one for what it holds, who holds what towards it and the stories it is in; drag to move it; a query box.</item>
/// <item>Stories: where each story is at: every pattern's matches in progress as a row of its stages (done, waited for
/// with the time left, still to come), and the stories told.</item>
/// <item>Chronicle: every event, newest first, and what the sifter did with each.</item>
/// <item>Data: the relations, qualities, verbs and patterns loaded and their counts, and the scenarios to play.</item>
/// </list>
/// Closed, it costs one bool check. Open, it lays the graph out a step a frame (the entities shown, at most
/// <see cref="StoryGraphMax"/>).
/// </summary>
public sealed partial class Ui
{
    public bool StoryOpen;

    public const int StoryGraphMax = 400;

    private StoryEngine? _stEngine; // the engine the layout belongs to (another one: the layout starts again)
    private Vector2[] _stPos = [], _stVel = [];
    private bool[] _stIsShown = [];
    private int _stPlaced;
    private readonly List<int> _stShown = new(256);
    private readonly List<(int T, int Rel, int Value)> _stEdges = new(32); // (one entity's relations drawn, by target)
    private int _stShownWas = -1;
    private float _stTemp = 10f;
    private Vector2 _stPan;
    private float _stZoom = 1f;
    private int _stSel = -1, _stDrag = -1;
    private bool _stShowGone, _stShowScripted = true, _stLabels = true, _stAutoFit = true;
    private bool[] _stRelOn = [];
    private ImInputString? _stQuery;
    private List<int[]>? _stQueryRows;
    private string[] _stQueryVars = [];
    private string _stQueryError = "";
    private int _stVerbFilter = -1; // -1 all, -2 stories only, else a verb
    private List<StoryScenario>? _stScenarios;
    private readonly List<string> _stScenarioErrors = new();

    private static readonly (byte R, byte G, byte B)[] StoryRelColours =
    [
        (235, 90, 80), (90, 170, 255), (120, 210, 120), (240, 200, 80), (200, 120, 240),
        (80, 210, 210), (255, 150, 60), (180, 180, 190), (255, 120, 190), (160, 200, 80),
    ];

    /// <summary>A relation's colour in the graph and the lists.</summary>
    private static ImColor8 StoryRelColour(int rel, byte alpha = 255)
    {
        var (r, g, b) = StoryRelColours[rel % StoryRelColours.Length];
        return new ImColor8(r, g, b, alpha);
    }

    private static ImColor8 StoryKindColour(StoryKind k, bool gone) => gone ? new ImColor8(110, 110, 115, 255) : k switch
    {
        StoryKind.Player => new ImColor8(255, 170, 60, 255),
        StoryKind.Person => new ImColor8(235, 225, 195, 255),
        StoryKind.Faction => new ImColor8(190, 120, 255, 255),
        StoryKind.Place => new ImColor8(110, 200, 120, 255),
        _ => new ImColor8(100, 170, 255, 255),
    };

    private static readonly float4 StoryDone = new(0.25f, 0.55f, 0.3f, 1f), StoryWaiting = new(0.75f, 0.45f, 0.12f, 1f), StoryLater = new(0.22f, 0.22f, 0.26f, 1f);

    /// <summary>The window. <paramref name="game"/>: the session (its story is the host's).</summary>
    public void DrawStory(Game? game, int bind)
    {
        if (!StoryOpen)
            return;
        ImGui.SetNextWindowSize(new float2(1020 * _scale, 780 * _scale), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.96f);
        if (ImGui.Begin("Story###story"u8, ref StoryOpen))
        {
            if (game?.Story is not { } s)
            {
                ImGuiSafe.TextWrapped(game == null ? "Start or join a game: the story runs in Sandbox, on the host."
                    : !game.SandboxOn ? "The story runs in Sandbox: this mode has none."
                    : !game.IsServer ? "The story lives on the host: in this first version only the host sees it (docs/story/STORY.md)."
                    : "No story yet.");
                ImGuiSafe.TextColored(Dim, $"{Keybinds.Name(bind)} or Esc closes this window.");
            }
            else
                DrawStoryBody(game, s);
        }
        ImGui.End();
    }

    private void DrawStoryBody(Game game, StoryEngine s)
    {
        if (!ReferenceEquals(s, _stEngine))
        {
            _stEngine = s;
            _stPlaced = 0;
            _stSel = _stDrag = -1;
            _stPan = Vector2.Zero;
            _stZoom = 1f;
            _stAutoFit = true;
            _stQueryRows = null;
            _stRelOn = Enumerable.Repeat(true, s.Rules.Relations.Length).ToArray();
        }
        var told = 0;
        foreach (var c in s.Sifter.Completed)
            told += c;
        ImGuiSafe.TextColored(Orange, $"Story clock {StoryRules.Clock(s.Time)}    {s.World.Count} entities, {s.World.PairTotal} relationships, {s.Chronicle.Count} events{(s.Chronicle.FoldedTotal > 0 ? $" ({s.Chronicle.FoldedTotal} folded)" : "")}, {s.Sifter.LiveCount} stories in progress, {told} told");
        ImGuiSafe.TextColored(Dim, $"Sifting: last event {s.LastSiftMs:0.000} ms, worst {s.WorstSiftMs:0.000} ms (with the stories it completed). {s.Recorded} events this session.");
        if (game.StoryPlaying is { } run)
        {
            ImGui.SameLine();
            ImGuiSafe.TextColored(Orange, $"  Playing {run.Scenario.Name}: {run.Next}/{run.Scenario.Steps.Length}");
        }
        if (!ImGui.BeginTabBar("##storytabs"u8))
            return;
        if (ImGui.BeginTabItem("World"u8))
        {
            DrawStoryWorld(s);
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Stories"u8))
        {
            DrawStoryStories(s);
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Chronicle"u8))
        {
            DrawStoryChronicle(s);
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Data"u8))
        {
            DrawStoryData(game, s);
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }

    // ---- World: the graph -------------------------------------------------------------------------------------------

    private void DrawStoryWorld(StoryEngine s)
    {
        var w = s.World;
        var rules = s.Rules;
        ImGui.Checkbox("Gone"u8, ref _stShowGone);
        if (ImGui.IsItemHovered())
            ImGui.SetItemTooltip("Entities whose game object went (a construct taken apart, a scenario's people): their history stays."u8);
        ImGui.SameLine();
        ImGui.Checkbox("Scripted"u8, ref _stShowScripted);
        if (ImGui.IsItemHovered())
            ImGui.SetItemTooltip("A scenario's own entities (made fresh each time one plays)."u8);
        ImGui.SameLine();
        ImGui.Checkbox("Labels"u8, ref _stLabels);
        ImGui.SameLine();
        if (ImGui.Button("Fit"u8))
            _stAutoFit = true;
        if (ImGui.IsItemHovered())
            ImGui.SetItemTooltip("Everything shown in view (it stays so until you move or zoom the view)."u8);
        for (var r = 0; r < rules.Relations.Length && r < _stRelOn.Length; r++)
        {
            ImGui.SameLine();
            var col = StoryRelColour(r);
            ImGui.PushStyleColor(ImGuiCol.Text, col);
            ImGui.Checkbox($"{rules.Relations[r].Name}##strel{r}", ref _stRelOn[r]);
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
                ImGuiSafe.SetItemTooltip(rules.Relations[r].About);
        }

        // Which entities are shown, and their places (a new one starts on a spiral).
        if (_stPos.Length < w.Count)
        {
            var n = Math.Max(w.Count, _stPos.Length * 2);
            Array.Resize(ref _stPos, n);
            Array.Resize(ref _stVel, n);
            Array.Resize(ref _stIsShown, n);
        }
        for (; _stPlaced < w.Count; _stPlaced++)
        {
            var a = _stPlaced * 2.39996f;
            var rr = 40f * MathF.Sqrt(_stPlaced + 1);
            _stPos[_stPlaced] = new Vector2(MathF.Cos(a), MathF.Sin(a)) * rr;
            _stVel[_stPlaced] = Vector2.Zero;
        }
        _stShown.Clear();
        Array.Clear(_stIsShown);
        foreach (var e in w.All)
        {
            if (e.Gone && !_stShowGone || e.Scripted && !_stShowScripted)
                continue;
            if (_stShown.Count >= StoryGraphMax)
                break;
            _stShown.Add(e.Id);
            _stIsShown[e.Id] = true;
        }
        if (_stShown.Count != _stShownWas)
        {
            _stShownWas = _stShown.Count;
            _stTemp = 10f;
        }
        StoryLayout(w);

        var avail = ImGui.GetContentRegionAvail();
        var side = 320 * _scale;
        var size = new float2(MathF.Max(100, avail.X - side - 8 * _scale), MathF.Max(100, avail.Y));
        var p0 = ImGui.GetCursorScreenPos();
        var p1 = p0 + size;
        ImGui.InvisibleButton("##storygraph"u8, size);
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p0, p1, new ImColor8(16, 18, 24, 255));
        dl.PushClipRect(p0, p1, true);
        var centre = new Vector2(p0.X + size.X * 0.5f, p0.Y + size.Y * 0.5f);
        if (_stAutoFit && _stShown.Count > 0)
        {
            var mn = new Vector2(float.MaxValue);
            var mx = new Vector2(float.MinValue);
            foreach (var id in _stShown)
            {
                mn = Vector2.Min(mn, _stPos[id]);
                mx = Vector2.Max(mx, _stPos[id]);
            }
            var margin = 70 * _scale;
            var fit = MathF.Min((size.X - 2 * margin) / MathF.Max(mx.X - mn.X, 1), (size.Y - 2 * margin) / MathF.Max(mx.Y - mn.Y, 1)) / _scale;
            _stZoom = Math.Clamp(fit, 0.15f, 2.5f);
            _stPan = -(mn + mx) * 0.5f;
        }
        var zoom = _stZoom * _scale;
        Vector2 Screen(Vector2 g) => centre + (g + _stPan) * zoom;
        Vector2 Graph(Vector2 sc) => (sc - centre) / zoom - _stPan;
        var mouse = new Vector2(ImGui.GetMousePos().X, ImGui.GetMousePos().Y);

        // The node under the mouse.
        var hover = -1;
        var best = 12f * _scale * 12f * _scale;
        foreach (var id in _stShown)
        {
            var d = Vector2.DistanceSquared(Screen(_stPos[id]), mouse);
            if (d < best)
            {
                best = d;
                hover = id;
            }
        }
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left, false))
        {
            _stSel = hover;
            _stDrag = hover;
        }
        if (_stDrag >= 0 && active && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _stPos[_stDrag] = Graph(mouse);
            _stVel[_stDrag] = Vector2.Zero;
        }
        else if (active && _stDrag < 0 && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0))
        {
            var md = ImGui.GetIO().MouseDelta;
            _stPan += new Vector2(md.X, md.Y) / zoom;
            _stAutoFit = false;
        }
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            _stDrag = -1;
        if (hovered && ImGui.GetIO().MouseWheel != 0)
        {
            var before = Graph(mouse);
            _stZoom = Math.Clamp(_stZoom * MathF.Pow(1.15f, ImGui.GetIO().MouseWheel), 0.15f, 5f);
            _stAutoFit = false;
            zoom = _stZoom * _scale;
            _stPan += Graph(mouse) - before; // (zoom about the mouse)
        }

        // Relationships: an arrow from the one who holds it (two-way ones a plain line). Every relation one entity holds
        // towards another is a line of its own, side by side, with one label for them all set off the line on its side, so
        // Owns and Rode, or two grudges held both ways, never draw over each other.
        var focus = _stSel >= 0 ? _stSel : hover;
        foreach (var sid in _stShown)
        {
            var e = w[sid];
            _stEdges.Clear();
            for (var k = 0; k < e.PairCount; k++)
            {
                var pr = e.Pairs[k];
                var t = pr.Target;
                var rel = pr.Rel;
                if ((uint)t >= (uint)w.Count || !_stIsShown[t] || t == sid || rel >= _stRelOn.Length || !_stRelOn[rel])
                    continue;
                if (rules.Relations[rel].Symmetric && t < sid)
                    continue;
                _stEdges.Add((t, rel, pr.Value));
            }
            _stEdges.Sort();
            for (var g = 0; g < _stEdges.Count;)
            {
                var t = _stEdges[g].T;
                var end = g;
                while (end < _stEdges.Count && _stEdges[end].T == t)
                    end++;
                var a = Screen(_stPos[sid]);
                var b = Screen(_stPos[t]);
                var dir = b - a;
                var len = dir.Length();
                if (len < 1)
                {
                    g = end;
                    continue;
                }
                dir /= len;
                var normal = new Vector2(-dir.Y, dir.X);
                var lit = focus < 0 || focus == sid || focus == t;
                var outer = 0f;
                for (var k = g; k < end; k++)
                {
                    var (_, rel, value) = _stEdges[k];
                    var def = rules.Relations[rel];
                    var off = def.Symmetric && end - g == 1 ? 0 : (4f + 4f * (k - g)) * _scale;
                    outer = off;
                    var perp = normal * off;
                    var col = StoryRelColour(rel, lit ? (byte)255 : (byte)50);
                    var from = a + dir * 9 * _scale + perp;
                    var to = b - dir * 10 * _scale + perp;
                    var thick = (1.2f + MathF.Min(3f, MathF.Log2(1 + MathF.Abs(value)) * 0.4f)) * _scale;
                    dl.AddLine(F2(from), F2(to), col, thick);
                    if (!def.Symmetric)
                    {
                        var head = 7 * _scale;
                        var side2 = normal * head * 0.45f;
                        dl.AddTriangleFilled(F2(to), F2(to - dir * head + side2), F2(to - dir * head - side2), col);
                    }
                }
                if (_stLabels && lit && (focus >= 0 || _stZoom > 0.7f))
                {
                    // One label: "Owns, Rode 2", each name in its relation's colour, its centre pushed out along the normal
                    // by half its own extent that way, so labels of the two directions sit either side of the lines.
                    var width = 0f;
                    for (var k = g; k < end; k++)
                        width += ImGui.CalcTextSize(EdgeText(rules, k, end)).X;
                    var height = ImGui.GetTextLineHeight();
                    var mid = (a + b) * 0.5f + normal * (outer + 5 * _scale + MathF.Abs(normal.X) * width * 0.5f + MathF.Abs(normal.Y) * height * 0.5f);
                    var at = mid - new Vector2(width * 0.5f, height * 0.5f);
                    for (var k = g; k < end; k++)
                    {
                        var text = EdgeText(rules, k, end);
                        dl.AddText(F2(at + new Vector2(1, 1)), new ImColor8(0, 0, 0, 200), text);
                        dl.AddText(F2(at), StoryRelColour(_stEdges[k].Rel, 255), text);
                        at.X += ImGui.CalcTextSize(text).X;
                    }
                }
                g = end;
            }
        }

        // The entities.
        foreach (var id in _stShown)
        {
            var e = w[id];
            var c = Screen(_stPos[id]);
            var r = (e.Kind is StoryKind.Place ? 9 : e.Kind is StoryKind.Construct ? 6 : 8) * _scale;
            var col = StoryKindColour(e.Kind, e.Gone);
            var cf = F2(c);
            ImDrawListExtensions.AddCircleFilled(dl, in cf, r, col, 20);
            if (id == _stSel)
                ImDrawListExtensions.AddCircle(dl, in cf, r + 3 * _scale, new ImColor8(255, 255, 255, 255), 24, 2f * _scale);
            else if (id == hover)
                ImDrawListExtensions.AddCircle(dl, in cf, r + 2 * _scale, new ImColor8(255, 255, 255, 140), 24, 1.5f * _scale);
            if (_stLabels || id == hover || id == _stSel)
            {
                var at = F2(c + new Vector2(r + 3 * _scale, -7 * _scale));
                dl.AddText(at + new float2(1, 1), new ImColor8(0, 0, 0, 200), e.Name);
                dl.AddText(at, focus < 0 || focus == id ? new ImColor8(230, 230, 235, 255) : new ImColor8(160, 160, 170, 160), e.Name);
            }
        }
        dl.AddText(new float2(p0.X + 8 * _scale, p1.Y - 22 * _scale), new ImColor8(150, 150, 160, 255),
            _stShown.Count >= StoryGraphMax ? $"the first {StoryGraphMax} entities shown. Drag: move one, or the view; wheel: zoom" : "Drag: move one, or the view; wheel: zoom; click: details");
        dl.PopClipRect();
        if (hovered && hover >= 0)
            ImGuiSafe.SetTooltip($"{w[hover].Name} ({w[hover].Kind}{(w[hover].Gone ? ", gone" : "")}): {StoryCli.Describe(s, w[hover])}");

        ImGui.SameLine();
        if (ImGui.BeginChild("##storyside"u8, new float2(side, size.Y), ImGuiChildFlags.Borders))
        {
            if (_stSel >= 0 && w.Valid(_stSel))
                DrawStoryEntity(s, w[_stSel]);
            else
                ImGuiSafe.TextWrapped("Click an entity for its relationships, qualities and stories.");
            ImGui.Separator();
            DrawStoryQuery(s);
        }
        ImGui.EndChild();
    }

    /// <summary>A step of a force layout (Fruchterman and Reingold): entities push apart, relationships pull together, all drift to the middle.</summary>
    private void StoryLayout(StoryWorld w)
    {
        var n = _stShown.Count;
        if (n == 0 || _stTemp < 0.05f)
            return;
        const float k = 95f, k2 = k * k;
        foreach (var i in _stShown)
        {
            var f = -_stPos[i] * 0.02f;
            foreach (var j in _stShown)
            {
                if (j == i)
                    continue;
                var d = _stPos[i] - _stPos[j];
                var d2 = MathF.Max(d.LengthSquared(), 1f);
                f += d * (k2 / d2);
            }
            var e = w[i];
            for (var p = 0; p < e.PairCount; p++)
                f += Pull(i, e.Pairs[p].Target);
            for (var p = 0; p < e.HeldCount; p++)
                f += Pull(i, (int)(uint)e.Held[p]);
            _stVel[i] = f;
        }
        foreach (var i in _stShown)
        {
            if (i == _stDrag)
                continue;
            var v = _stVel[i];
            var len = v.Length();
            if (len > 1e-3f)
                _stPos[i] += v / len * MathF.Min(len, _stTemp);
        }
        _stTemp = MathF.Max(0.3f, _stTemp * 0.993f);

        Vector2 Pull(int i, int t)
        {
            if ((uint)t >= (uint)_stIsShown.Length || !_stIsShown[t] || t == i)
                return Vector2.Zero;
            var d = _stPos[t] - _stPos[i];
            return d * (d.Length() / k);
        }
    }

    private void DrawStoryEntity(StoryEngine s, StoryEntity e)
    {
        var w = s.World;
        var rules = s.Rules;
        ImGui.PushStyleColor(ImGuiCol.Text, StoryKindColour(e.Kind, e.Gone));
        ImGuiSafe.TextWrapped($"{e.Name}");
        ImGui.PopStyleColor();
        ImGuiSafe.TextColored(Dim, $"{e.Kind}, since {StoryRules.Clock(e.Born)}{(e.Gone ? ", gone" : "")}{(e.Scripted ? ", scripted" : "")}");
        if (e.QualityCount > 0)
        {
            var q = new List<string>(e.QualityCount);
            for (var i = 0; i < e.QualityCount; i++)
                q.Add($"{rules.Qualities[e.Qualities[i].Quality].Name} {e.Qualities[i].Value}");
            ImGuiSafe.TextWrapped(string.Join(", ", q));
        }
        if (e.PairCount > 0)
        {
            ImGui.TextColored(Orange, "Holds"u8);
            for (var i = 0; i < e.PairCount; i++)
            {
                var p = e.Pairs[i];
                ImGui.PushStyleColor(ImGuiCol.Text, StoryRelColour(p.Rel));
                if (ImGui.Selectable($"{rules.Relations[p.Rel].Name} {w.NameOf(p.Target)}{(p.Value != 1 ? $" {p.Value}" : "")}##sto{i}", false))
                    _stSel = p.Target;
                ImGui.PopStyleColor();
            }
        }
        if (e.HeldCount > 0)
        {
            ImGui.TextColored(Orange, "Held by"u8);
            for (var i = 0; i < e.HeldCount; i++)
            {
                var rel = (ushort)(e.Held[i] >> 32);
                var who = (int)(uint)e.Held[i];
                var v = w.Get(who, rel, e.Id);
                ImGui.PushStyleColor(ImGuiCol.Text, StoryRelColour(rel));
                if (ImGui.Selectable($"{w.NameOf(who)} {rules.Relations[rel].Name}{(v != 1 ? $" {v}" : "")}##sti{i}", false))
                    _stSel = who;
                ImGui.PopStyleColor();
            }
        }
        // Its stories: in progress, and told.
        var any = false;
        foreach (ref readonly var m in s.Sifter.Pool.AsSpan())
        {
            if (!m.Live || !Binds(m.Binds, e.Id))
                continue;
            if (!any)
                ImGui.TextColored(Orange, "Stories in progress"u8);
            any = true;
            var p = rules.Patterns[m.Pattern];
            ImGuiSafe.TextWrapped($"{p.Name} {m.Stage}/{p.Stages.Length}: {s.Sifter.BindText(m.Pattern, m.Binds)}");
        }
        any = false;
        for (var i = s.Sifter.Done.Count - 1; i >= 0; i--)
        {
            var d = s.Sifter.Done[i];
            if (!Binds(d.Binds, e.Id))
                continue;
            if (!any)
                ImGui.TextColored(Orange, "Stories told"u8);
            any = true;
            ImGuiSafe.TextWrapped($"{StoryRules.Clock(d.At)} {s.Sifter.Tell(d.Pattern, d.Binds)}");
        }
        // Its last events.
        ImGui.TextColored(Orange, "Last events"u8);
        var shown = 0;
        var all = s.Chronicle.All;
        for (var i = all.Length - 1; i >= 0 && shown < 12; i--)
        {
            ref readonly var ev = ref all[i];
            if (ev.Actor != e.Id && ev.Target != e.Id && ev.Owner != e.Id && ev.Place != e.Id && ev.Item != e.Id)
                continue;
            shown++;
            ImGuiSafe.TextWrapped($"{StoryRules.Clock(ev.At)} {s.Sentence(ev)}");
        }
        if (shown == 0)
            ImGui.TextColored(Dim, "none kept"u8);

        static bool Binds(in Ints8 b, int id)
        {
            for (var v = 0; v < StoryRules.MaxVars; v++)
                if (b[v] == id)
                    return true;
            return false;
        }
    }

    private void DrawStoryQuery(StoryEngine s)
    {
        _stQuery ??= new ImInputString(256);
        ImGui.TextColored(Orange, "Query"u8);
        ImGui.SetNextItemWidth(-1);
        var run = false;
        try
        {
            run = ImGui.InputTextWithHint("##storyq"u8, "?a Grudge ?b >= 20, ?b Kills"u8, _stQuery, ImGuiInputTextFlags.EnterReturnsTrue);
        }
        catch (Exception ex)
        {
            ImGuiSafe.FieldFailed("story query", ex);
        }
        run |= ImGui.Button("Run##storyq"u8);
        if (ImGui.IsItemHovered())
            ImGui.SetItemTooltip("Clauses joined by commas: \"?a Grudge ?b\" (or \">= 20\"), \"?a Kills > 2\", \"?a is Player\", \"?a != ?b\", \"not\" before one. Names stand for entities: Karl, 'Glacier Station'."u8);
        if (run)
        {
            var vars = new List<string>();
            try
            {
                var q = StoryQuery.Compile(ImGuiSafe.Text(_stQuery), s.Rules, vars);
                _stQueryRows = q.All(s.World, 200);
                _stQueryVars = vars.ToArray();
                _stQueryError = "";
            }
            catch (StoryException ex)
            {
                _stQueryRows = null;
                _stQueryError = ex.Message;
            }
        }
        if (_stQueryError.Length > 0)
            ImGuiSafe.TextColored(Red, _stQueryError);
        if (_stQueryRows is not { } rows)
            return;
        ImGuiSafe.TextColored(Dim, rows.Count == 0 ? "no answers" : rows.Count >= 200 ? "the first 200 answers" : $"{rows.Count} answer(s)");
        if (_stQueryVars.Length == 0 || !ImGui.BeginTable("##storyqrows"u8, _stQueryVars.Length, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
            return;
        foreach (var v in _stQueryVars)
            ImGui.TableSetupColumn($"?{v}", ImGuiTableColumnFlags.WidthStretch, 0, default);
        ImGui.TableHeadersRow();
        for (var r = 0; r < rows.Count; r++)
        {
            ImGui.TableNextRow(ImGuiTableRowFlags.None, 0);
            for (var c = 0; c < _stQueryVars.Length; c++)
            {
                ImGui.TableNextColumn();
                var id = c < rows[r].Length ? rows[r][c] : -1;
                if (ImGui.Selectable($"{s.World.NameOf(id)}##stq{r}_{c}", id == _stSel) && id >= 0)
                    _stSel = id;
            }
        }
        ImGui.EndTable();
    }

    // ---- Stories: where each one is at -------------------------------------------------------------------------------

    private void DrawStoryStories(StoryEngine s)
    {
        var rules = s.Rules;
        var sifter = s.Sifter;
        if (!ImGui.BeginChild("##storypatterns"u8, new float2(0, ImGui.GetContentRegionAvail().Y * 0.62f), ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar))
        {
            ImGui.EndChild();
            return;
        }
        foreach (var p in rules.Patterns)
        {
            var live = 0;
            foreach (ref readonly var m in sifter.Pool.AsSpan())
                if (m.Live && m.Pattern == p.Id)
                    live++;
            ImGuiSafe.TextColored(Orange, p.Name);
            ImGui.SameLine();
            ImGuiSafe.TextColored(Dim, $"  {live} in progress, {sifter.Completed[p.Id]} told, {sifter.Started[p.Id]} begun, {sifter.RanOut[p.Id]} ran out, {sifter.Ended[p.Id]} ended{(sifter.Dropped[p.Id] > 0 ? $", {sifter.Dropped[p.Id]} dropped" : "")}");
            if (p.About.Length > 0 && ImGui.IsItemHovered())
                ImGuiSafe.SetItemTooltip(p.About);
            // The pattern itself: its stages as a row, then each match in progress under it.
            ImGui.Indent();
            for (var st = 0; st < p.Stages.Length; st++)
            {
                if (st > 0)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(Dim, "->"u8);
                    ImGui.SameLine();
                }
                StoryStage($"{p.Stages[st].Text}##stp{p.Id}_{st}", StoryLater, null); // (its text says "within")
            }
            if (p.Unless.Length > 0)
            {
                ImGui.SameLine();
                ImGuiSafe.TextColored(Dim, $"  unless {string.Join(" or ", p.Unless.Select(u => u.Text))}");
            }
            for (var i = 0; i < sifter.Pool.Length; i++)
            {
                ref readonly var m = ref sifter.Pool[i];
                if (!m.Live || m.Pattern != p.Id)
                    continue;
                for (var st = 0; st < p.Stages.Length; st++)
                {
                    if (st > 0)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(Dim, "->"u8);
                        ImGui.SameLine();
                    }
                    if (st < m.Stage)
                    {
                        var at = s.Chronicle.IndexOf(m.Events[st]);
                        var text = at >= 0 ? s.Sentence(s.Chronicle[at]) : $"event {m.Events[st]}";
                        StoryStage($"{text}##stm{i}_{st}", StoryDone, at >= 0 ? $"{StoryRules.Clock(s.Chronicle[at].At)}: {text}" : null);
                    }
                    else if (st == m.Stage)
                    {
                        var within = p.Stages[st].Within;
                        var left = within > 0 ? $", {StoryRules.Span(Math.Max(0, within - (s.Time - m.Last)))} left" : "";
                        StoryStage($"waiting: {StageText(s, p, p.Stages[st], m.Binds)}{left}##stm{i}_{st}", StoryWaiting, "The next event that fits moves it on.");
                    }
                    else
                        StoryStage($"{StageText(s, p, p.Stages[st], m.Binds)}##stm{i}_{st}", StoryLater, null);
                }
                ImGui.SameLine();
                ImGuiSafe.TextColored(Dim, $"  {sifter.BindText(m.Pattern, m.Binds)}; begun {StoryRules.Clock(m.Started)}");
            }
            ImGui.Unindent();
            ImGui.Spacing();
        }
        ImGui.EndChild();

        ImGui.TextColored(Orange, "Stories told, newest first"u8);
        if (ImGui.BeginChild("##storytold"u8, new float2(0, 0), ImGuiChildFlags.Borders))
        {
            var shown = 0;
            for (var i = sifter.Done.Count - 1; i >= 0 && shown < 300; i--, shown++)
            {
                var d = sifter.Done[i];
                ImGuiSafe.TextColored(Dim, $"{StoryRules.Clock(d.At)}");
                ImGui.SameLine();
                ImGuiSafe.TextWrapped($"{rules.Patterns[d.Pattern].Name}: {sifter.Tell(d.Pattern, d.Binds)}");
                if (ImGui.IsItemHovered())
                {
                    var lines = new List<string>();
                    for (var st = 0; st < rules.Patterns[d.Pattern].Stages.Length; st++)
                        if (s.Chronicle.IndexOf(d.Events[st]) is var at and >= 0)
                            lines.Add($"{StoryRules.Clock(s.Chronicle[at].At)}  {s.Sentence(s.Chronicle[at])}");
                    ImGuiSafe.SetTooltip(string.Join("\n", lines));
                }
            }
            if (shown == 0)
                ImGuiSafe.TextWrapped("None yet. A story is a pattern of events (Story/Patterns): a kill paid back, a wreck picked over, a favour returned.");
        }
        ImGui.EndChild();
    }

    /// <summary>One relation's part of an edge label: its name, its strength unless 1, a comma unless it is the last.</summary>
    private string EdgeText(StoryRules rules, int k, int end)
    {
        var (_, rel, value) = _stEdges[k];
        var name = rules.Relations[rel].Name;
        return (value != 1 ? $"{name} {value}" : name) + (k < end - 1 ? ", " : "");
    }

    /// <summary>A stage as a coloured button (it does nothing: its tooltip says more).</summary>
    private static void StoryStage(string label, in float4 colour, string? tip)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, colour);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, colour);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, colour);
        ImGui.SmallButton(label);
        ImGui.PopStyleColor(3);
        if (tip != null && ImGui.IsItemHovered())
            ImGuiSafe.SetItemTooltip(tip);
    }

    /// <summary>A stage's text with the variables bound so far named ("Kill actor Rook target ?a").</summary>
    private static string StageText(StoryEngine s, PatternDef p, PatternStep st, in Ints8 binds)
    {
        var text = st.Text;
        var order = Enumerable.Range(0, p.Vars.Length).OrderByDescending(v => p.Vars[v].Length);
        foreach (var v in order)
            if (binds[v] >= 0)
                text = text.Replace("?" + p.Vars[v], s.World.NameOf(binds[v]));
        return text;
    }

    // ---- Chronicle ----------------------------------------------------------------------------------------------------

    private void DrawStoryChronicle(StoryEngine s)
    {
        var rules = s.Rules;
        ImGui.SetNextItemWidth(200 * _scale);
        if (ImGui.BeginCombo("##storyverb"u8, _stVerbFilter == -1 ? "Every event" : _stVerbFilter == -2 ? "Stories only" : s.VerbName(_stVerbFilter)))
        {
            if (ImGui.Selectable("Every event"u8, _stVerbFilter == -1))
                _stVerbFilter = -1;
            if (ImGui.Selectable("Stories only"u8, _stVerbFilter == -2))
                _stVerbFilter = -2;
            for (var v = 0; v < rules.Verbs.Length; v++)
                if (v != rules.StoryVerb && ImGui.Selectable($"{rules.Verbs[v].Name}##stv{v}", _stVerbFilter == v))
                    _stVerbFilter = v;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGuiSafe.TextColored(Dim, $"{s.Chronicle.Count} events kept{(s.Chronicle.FoldedTotal > 0 ? $", {s.Chronicle.FoldedTotal} older plain ones folded into counts" : "")}; the newest 500 shown");
        var avail = ImGui.GetContentRegionAvail();
        var left = avail.X * 0.58f;
        if (ImGui.BeginChild("##storyevents"u8, new float2(left, avail.Y), ImGuiChildFlags.Borders))
        {
            var all = s.Chronicle.All;
            var shown = 0;
            for (var i = all.Length - 1; i >= 0 && shown < 500; i--)
            {
                ref readonly var ev = ref all[i];
                var story = ev.Verb == rules.StoryVerb;
                if (_stVerbFilter == -2 && !story || _stVerbFilter >= 0 && ev.Verb != _stVerbFilter)
                    continue;
                shown++;
                ImGuiSafe.TextColored(Dim, $"{StoryRules.Clock(ev.At)}");
                ImGui.SameLine();
                var scripted = (ev.Flags & StoryEventFlags.Scripted) != 0;
                ImGuiSafe.TextColored(story ? Orange : scripted ? new float4(0.6f, 0.75f, 0.9f, 1f) : new float4(0.9f, 0.9f, 0.92f, 1f), s.Sentence(ev));
                if (ImGui.IsItemHovered())
                    ImGuiSafe.SetTooltip($"event {ev.Id}: {s.VerbName(ev.Verb)}; actor {s.World.NameOf(ev.Actor)}, target {s.World.NameOf(ev.Target)}, owner {s.World.NameOf(ev.Owner)}, place {s.World.NameOf(ev.Place)}{(ev.Item >= 0 ? $", item {s.World.NameOf(ev.Item)}" : "")}{(scripted ? "; from a scenario" : "")}{((ev.Flags & StoryEventFlags.Kept) != 0 ? "; part of a story (never folded)" : "")}");
            }
            if (shown == 0)
                ImGui.TextColored(Dim, "Nothing yet."u8);
        }
        ImGui.EndChild();
        ImGui.SameLine();
        if (ImGui.BeginChild("##storynotes"u8, new float2(0, avail.Y), ImGuiChildFlags.Borders))
        {
            ImGui.TextColored(Orange, "What the sifter did, newest first"u8);
            var sifter = s.Sifter;
            var first = Math.Max(0, sifter.NoteTotal - StorySifter.NoteRing);
            for (var n = sifter.NoteTotal - 1; n >= first; n--)
            {
                ref readonly var note = ref sifter.Notes[n % StorySifter.NoteRing];
                ImGuiSafe.TextColored(Dim, $"{StoryRules.Clock(note.At)}");
                ImGui.SameLine();
                var col = note.Kind switch
                {
                    SiftKind.Completed => Orange,
                    SiftKind.Started or SiftKind.Advanced => new float4(0.6f, 0.85f, 0.6f, 1f),
                    _ => new float4(0.7f, 0.6f, 0.6f, 1f),
                };
                ImGuiSafe.TextColored(col, s.NoteLine(note));
            }
            if (sifter.NoteTotal == 0)
                ImGui.TextColored(Dim, "Nothing this session (the sifter's notes aren't saved with the world)."u8);
        }
        ImGui.EndChild();
    }

    // ---- Data ---------------------------------------------------------------------------------------------------------

    private void DrawStoryData(Game game, StoryEngine s)
    {
        var rules = s.Rules;
        if (rules.Errors.Count > 0)
            foreach (var e in rules.Errors)
                ImGuiSafe.TextColored(Red, $"refused: {e}");
        if (ImGui.CollapsingHeader("Scenarios"u8, ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (_stScenarios == null)
            {
                _stScenarioErrors.Clear();
                _stScenarios = StoryScenario.LoadFolder(rules, null, _stScenarioErrors);
            }
            ImGuiSafe.TextWrapped("A scenario plays scripted events into this world's story, one every " + $"{StoryScenario.LiveGap:0.##} s. Its entities are its own (made fresh, marked scripted), so it never touches the real ones. Story/Scenarios/*.json; --story-sim plays one headless.");
            if (ImGui.Button("Reload"u8))
                _stScenarios = null;
            foreach (var sc in _stScenarios ?? [])
            {
                ImGui.BeginDisabled(game.StoryPlaying != null);
                if (ImGui.SmallButton($"Play##stsc{sc.File}"))
                    game.StoryPlay(sc);
                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGuiSafe.TextWrapped($"{sc.Name} ({sc.File}, {sc.Steps.Length} events): {sc.About}");
            }
            foreach (var e in _stScenarioErrors)
                ImGuiSafe.TextColored(Red, $"refused: {e}");
        }
        if (ImGui.CollapsingHeader("Patterns (Story/Patterns)"u8, ImGuiTreeNodeFlags.DefaultOpen))
            foreach (var p in rules.Patterns)
            {
                ImGuiSafe.TextColored(Orange, $"{p.Name} ({p.File})");
                ImGui.Indent();
                if (p.About.Length > 0)
                    ImGuiSafe.TextWrapped(p.About);
                ImGuiSafe.TextWrapped(string.Join("  ->  ", p.Stages.Select(st => st.Text)));
                if (p.Unless.Length > 0)
                    ImGuiSafe.TextColored(Dim, $"unless {string.Join(" or ", p.Unless.Select(u => u.Text))}");
                if (p.Tells.Length > 0)
                    ImGuiSafe.TextColored(Dim, $"tells: \"{p.Tells}\"");
                ImGui.Unindent();
            }
        if (ImGui.CollapsingHeader("Verbs (Story/Verbs.json): the actions"u8, ImGuiTreeNodeFlags.DefaultOpen))
            foreach (var v in rules.Verbs)
            {
                ImGuiSafe.TextColored(Orange, $"{v.Name}{(v.Players ? "  [players]" : "")}{(v.Npcs ? "  [npcs]" : "")}");
                ImGui.SameLine();
                ImGuiSafe.TextWrapped($"\"{v.Sentence}\": {v.About}");
            }
        if (ImGui.CollapsingHeader("Relations and qualities (Story/Relations.json)"u8, ImGuiTreeNodeFlags.None))
        {
            foreach (var r in rules.Relations)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, StoryRelColour(r.Id));
                ImGuiSafe.TextWrapped($"{r.Name}{(r.Exclusive ? " (exclusive)" : "")}{(r.Symmetric ? " (two-way)" : "")}: {r.About}");
                ImGui.PopStyleColor();
            }
            foreach (var q in rules.Qualities)
                ImGuiSafe.TextWrapped($"{q.Name}: {q.About}");
        }
    }
}
