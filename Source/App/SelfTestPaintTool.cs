using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Paint tool" (board item dev-sandbox-paint-tool-pp01, decision 29; docs/sandbox/CONSTRUCTS.md, "The paint tool"):
    /// the tool wheel's segment and ring (modes, ten swatches, Custom..., the recent colours as a third tier), the model;
    /// painting a part changes only its paintable boxes (a pane's glass, a wheel's tyre and a lamp's lens keep theirs);
    /// the eyedropper takes a painted part's colour (an unpainted one's own main colour); a whole-construct fill paints
    /// every paintable part and every link; Reset restores the defaults; painting a pipe recolours its segments (the
    /// link, not its neighbours); a held stroke acts on each swept part once; the rules (tool out, reach, seat, a
    /// contested construct in survival); the construct message and the change message round-trip the paint; a blueprint
    /// with "paint" builds painted (and a bad colour is refused); a part removed keeps the others' paint; no allocation
    /// while painting; and a real host with a separate client paint over the transport and both agree.
    /// </summary>
    private static void CheckPaintTool()
    {
        PaintWheelCases();
        var w = new ConstructWorld();
        if (ConstructBuild.FindBlueprint("Buggy") is not { } bp || DriveSpawn(w, bp, 0, 0, 0, "the buggy") is not { } car)
        {
            Fail("paint tool: no Buggy");
            return;
        }
        PaintBoxCases();
        PaintEditCases(w, car);
        PaintStrokeCases();
        PaintWireCases(w, car);
        PaintBlueprintCases();
        PaintLoopback();
    }

    private static ConstructEdits PtHands(bool survival = false)
    {
        var hands = new ConstructEdits { InfiniteParts = !survival, Survival = survival };
        hands.Hand(0).Tool = ConstructTool.Paint;
        return hands;
    }

    private static readonly uint PtRed = ConstructPaint.Rgb(200, 30, 20);
    private static readonly uint PtBlue = ConstructPaint.Rgb(30, 60, 200);

    private static void PaintWheelCases()
    {
        var wheel = new SandboxToolWheel();
        var paint = new SandboxPaintWheel();
        wheel.SetOptions(SandboxToolWheel.Slot.Paint, paint);
        var item = wheel.Menu.Items[(int)SandboxToolWheel.Slot.Paint];
        var slotOk = SandboxToolWheel.ToolOf((int)SandboxToolWheel.Slot.Paint) == ConstructTool.Paint && SandboxToolWheel.SlotOf(ConstructTool.Paint) == (int)SandboxToolWheel.Slot.Paint && item.Enabled;
        var ring = item.Options;
        var swatches = ring?.Count(o => o.Icon is >= 0 and < 10) ?? 0;
        var recent = ring is { Length: > 0 } ? ring[(int)SandboxPaintWheel.Option.Recent].Options : null;
        var modes = ring is { Length: > 0 } && ring[0].Label == "Part" && ring[1].Label == "Whole construct" && ring[2].Label == "Reset colour";
        var custom = ring is { Length: > 0 } && ring[(int)SandboxPaintWheel.Option.Custom].Label == "Custom...";
        var defaults = paint.Mode == PaintMode.Part && paint.CurrentOption == (int)SandboxPaintWheel.Option.Swatch0;
        // Picks: a swatch sets the colour, a mode the mode, Custom asks for the picker, a recent colour comes back.
        paint.Choose((int)SandboxPaintWheel.Option.Swatch0 + 7);
        var red = paint.Colour == ConstructPaint.Swatches[7];
        paint.Choose((int)SandboxPaintWheel.Option.Whole);
        var whole = paint.Mode == PaintMode.Whole && paint.Colour == ConstructPaint.Swatches[7];
        paint.Choose((int)SandboxPaintWheel.Option.Custom);
        var asked = paint.WantCustom;
        paint.WantCustom = false;
        for (var k = 0; k < 10; k++)
            paint.SetColour(ConstructPaint.Rgb((byte)(10 + k), 20, 30));
        var kept = paint.Recent.Count(c => c != 0) == SandboxPaintWheel.RecentMax && paint.Recent[0] == ConstructPaint.Rgb(19, 20, 30);
        paint.SetColour(ConstructPaint.Rgb(15, 20, 30)); // (used again: moves to the front, no duplicate)
        var front = paint.Recent[0] == ConstructPaint.Rgb(15, 20, 30) && paint.Recent.Distinct().Count() == SandboxPaintWheel.RecentMax;
        paint.ChooseSub((int)SandboxPaintWheel.Option.Recent, 3);
        var sub = paint.Colour == paint.Recent[3];
        var model = ConstructTools.For(ConstructTool.Paint) is { Name: "Paint Tool" };
        var wire = ConstructToolWire.From((byte)ConstructTool.Paint) == ConstructTool.Paint;
        Line($"paint tool wheel: segment {slotOk}, {ring?.Length} options ({swatches} swatches, modes {modes}, Custom {custom}), {recent?.Length} recent colours; defaults {defaults}; picks: swatch {red}, mode {whole}, Custom asks {asked}; "
             + $"recent keeps 8 newest {kept}, reuse moves to front {front}, third-tier pick {sub}; model {model}, tool byte {wire}");
        if (!slotOk || ring is not { Length: (int)SandboxPaintWheel.Option.COUNT } || swatches != 10 || recent is not { Length: SandboxPaintWheel.RecentMax } || !modes || !custom || !defaults)
            Fail("paint tool: the wheel's segment or ring is wrong");
        if (!red || !whole || !asked || !kept || !front || !sub)
            Fail("paint tool: a pick on the ring or the recent colours is wrong");
        if (!model || !wire)
            Fail("paint tool: the held model or the tool byte is missing");
    }

    private static void PaintBoxCases()
    {
        var bad = new List<string>();
        foreach (var (name, expect) in new[] { ("Glass Pane 1 m", "frame yes, glass no"), ("Wheel", "rim yes, tyre no"), ("Lamp", "body yes, lens no"), ("Panel", "all") })
        {
            if (ConstructParts.Find(name) is not { } d)
            {
                bad.Add($"no {name}");
                continue;
            }
            var any = false;
            foreach (var b in d.Boxes)
            {
                var drawn = ConstructPaint.BoxColour(PtRed, in b);
                var changed = drawn != b.Color;
                any |= changed;
                var wantPaintable = name switch
                {
                    "Glass Pane 1 m" => !b.IsGlass,
                    "Wheel" => b.Detail,
                    "Lamp" => !b.Detail,
                    _ => true,
                };
                if (b.Paintable != wantPaintable)
                    bad.Add($"{name}: a box paintable {b.Paintable}, expected {wantPaintable}");
                if (b.Paintable && b.Tone == 1f && drawn != ConstructPaint.ToVector(PtRed))
                    bad.Add($"{name}: a paintable box didn't take the colour");
                if (!b.Paintable && changed)
                    bad.Add($"{name}: a box that keeps its colour was painted");
                if (ConstructPaint.BoxColour(0, in b) != b.Color)
                    bad.Add($"{name}: an unpainted part's box changed");
            }
            if (!any && name == "Panel")
                bad.Add("the panel didn't change");
            if (name == "Glass Pane 1 m" && !ConstructPaint.Paintable(d) || name != "Glass Pane 1 m" && !ConstructPaint.Paintable(d))
                bad.Add($"{name}: reported as nothing to paint");
            _ = expect;
        }
        Line($"paint tool boxes: pane, wheel, lamp and panel painted red change only their paintable boxes (glass, tyre, lens keep theirs): {bad.Count} wrong");
        if (bad.Count > 0)
            Fail($"paint tool: boxes: {string.Join("; ", bad.Take(4))}");
    }

    private static void PaintEditCases(ConstructWorld w, Construct car)
    {
        var hands = PtHands();
        var panel = PartNamed(car, "Plate Steel 1 m");
        var eye = ConstructRender.PartCentre(car, panel) + new Vector3(0, 2f, 0);
        if (panel < 0 || car.RouteCount == 0)
        {
            Fail($"paint tool: the buggy has no plate ({panel}) or no links ({car.RouteCount})");
            return;
        }
        // A part: only it changes.
        var why = hands.Paint(w, 0, eye, car.Id, PaintMode.Part, false, panel, PtRed);
        var only = car.Parts[panel].Paint == PtRed && ConstructPaint.Painted(car) == 1;
        var dirty = w.PaintDirty.Count == 1;
        // The eyedropper: a painted part's colour, an unpainted one's own main colour.
        var other = (panel + 1) % car.PartCount;
        var pickPainted = ConstructPaint.Sample(car.Parts[panel]) == PtRed;
        var pickOwn = ConstructPaint.Sample(car.Parts[other]) == ConstructPaint.MainColour(car.DefOf(other)) && ConstructPaint.IsSet(ConstructPaint.Sample(car.Parts[other]));
        var paint = new SandboxPaintWheel();
        paint.SetColour(ConstructPaint.Sample(car.Parts[panel]));
        var dropper = paint.Colour == PtRed && paint.Recent[0] == PtRed;
        // A link: its segments take the colour (the link's, not its neighbours').
        var route = car.RouteCount - 1;
        var whyRoute = hands.Paint(w, 0, eye, car.Id, PaintMode.Part, true, route, PtBlue);
        var routeOk = whyRoute == null && car.Routes[route].Paint == PtBlue && Enumerable.Range(0, car.RouteCount).Count(k => car.Routes[k].Paint != 0) == 1;
        var pickRoute = ConstructPaint.IsSet(RoutePick(car, route)) && RoutePick(car, route) == PtBlue;
        // Whole construct: every part that takes paint and every link; then Reset restores the defaults.
        why ??= hands.Paint(w, 0, eye, car.Id, PaintMode.Whole, false, 0, PtBlue);
        var paintable = 0;
        var wholeOk = true;
        for (var i = 0; i < car.PartCount; i++)
            if (ConstructPaint.Paintable(car.DefOf(i)))
            {
                paintable++;
                wholeOk &= car.Parts[i].Paint == PtBlue;
            }
            else
                wholeOk &= car.Parts[i].Paint == 0;
        for (var k = 0; k < car.RouteCount; k++)
            wholeOk &= car.Routes[k].Paint == PtBlue;
        var resetPart = hands.Paint(w, 0, eye, car.Id, PaintMode.Reset, false, panel, 0) == null && car.Parts[panel].Paint == 0 && ConstructPaint.Painted(car) == paintable + car.RouteCount - 1;
        var resetRoute = hands.Paint(w, 0, eye, car.Id, PaintMode.Reset, true, route, 0) == null && car.Routes[route].Paint == 0;
        var resetAll = hands.Paint(w, 0, eye, car.Id, PaintMode.Whole, false, 0, PtRed) == null;
        for (var i = 0; i < car.PartCount; i++)
            resetAll &= hands.Paint(w, 0, eye, car.Id, PaintMode.Reset, false, i, 0) == null;
        for (var k = 0; k < car.RouteCount; k++)
            resetAll &= hands.Paint(w, 0, eye, car.Id, PaintMode.Reset, true, k, 0) == null;
        resetAll &= ConstructPaint.Painted(car) == 0;
        // The rules.
        var noTool = PtHands();
        noTool.Hand(0).Tool = ConstructTool.Welder;
        var tool = noTool.Paint(w, 0, eye, car.Id, PaintMode.Part, false, panel, PtRed) is { } t1 && t1.Contains("paint tool");
        var far = hands.Paint(w, 0, eye + new Vector3(0, 30f, 0), car.Id, PaintMode.Part, false, panel, PtRed) is { } t2 && t2.Contains("far");
        var seated = PtHands();
        seated.Hand(0).SeatBody = car.Id;
        var seat = seated.Paint(w, 0, eye, car.Id, PaintMode.Part, false, panel, PtRed) is { } t3 && t3.Contains("seat");
        var badColour = hands.Paint(w, 0, eye, car.Id, PaintMode.Part, false, panel, 0x7F000000u | 5) is { } t4 && t4.Contains("colour");
        var gone = hands.Paint(w, 0, eye, 9999, PaintMode.Part, false, 0, PtRed) is { } t5 && t5.Contains("gone");
        // Survival: someone else's construct with its owner alive beside it is defended; yours and abandoned ones are free.
        var surv = PtHands(survival: true);
        var wasOwner = car.Owner;
        car.Owner = 3;
        surv.AliveAt = id => id == 3 ? car.WorldCom : null;
        surv.NameOf = id => "Rival";
        var defended = surv.Paint(w, 0, eye, car.Id, PaintMode.Part, false, panel, PtRed) is { } t6 && t6.Contains("defended");
        surv.AliveAt = _ => null;
        var abandoned = surv.Paint(w, 0, eye, car.Id, PaintMode.Part, false, panel, PtRed) == null;
        car.Owner = wasOwner;
        for (var i = 0; i < car.PartCount; i++)
            car.Parts[i].Paint = 0;
        for (var k = 0; k < car.RouteCount; k++)
            car.Routes[k].Paint = 0;
        w.PaintDirty.Clear();
        Line($"paint tool edits: a part painted only itself {only} (queued {dirty}); eyedropper: painted {pickPainted}, own colour {pickOwn}, into the can {dropper}; a link {routeOk} (picked {pickRoute}); "
             + $"whole construct ({paintable} parts + {car.RouteCount} links) {wholeOk}; reset a part {resetPart}, a link {resetRoute}, all {resetAll}; refused: wrong tool {tool}, far {far}, seated {seat}, bad colour {badColour}, gone {gone}; survival: defended {defended}, abandoned free {abandoned}");
        if (why != null || !only || !dirty || !pickPainted || !pickOwn || !dropper)
            Fail($"paint tool: painting a part or the eyedropper is wrong ({why})");
        if (!routeOk || !pickRoute)
            Fail($"paint tool: painting a link is wrong ({whyRoute})");
        if (!wholeOk || !resetPart || !resetRoute || !resetAll)
            Fail("paint tool: a whole-construct fill or a reset is wrong");
        if (!tool || !far || !seat || !badColour || !gone || !defended || !abandoned)
            Fail("paint tool: a refusal is missing or wrong");

        // No allocation while painting (a part, a link, the whole construct, a reset).
        for (var round = 0; round < 3; round++)
        {
            hands.Paint(w, 0, eye, car.Id, PaintMode.Part, false, panel, PtRed);
            hands.Paint(w, 0, eye, car.Id, PaintMode.Part, true, route, PtBlue);
            hands.Paint(w, 0, eye, car.Id, PaintMode.Whole, false, 0, PtBlue);
            hands.Paint(w, 0, eye, car.Id, PaintMode.Reset, false, panel, 0);
            w.PaintDirty.Clear();
        }
        var a0 = GC.GetAllocatedBytesForCurrentThread();
        for (var round = 0; round < 20; round++)
        {
            hands.Paint(w, 0, eye, car.Id, PaintMode.Part, false, panel, round % 2 == 0 ? PtRed : PtBlue);
            hands.Paint(w, 0, eye, car.Id, PaintMode.Part, true, route, PtBlue);
            hands.Paint(w, 0, eye, car.Id, PaintMode.Whole, false, 0, round % 2 == 0 ? PtBlue : PtRed);
            hands.Paint(w, 0, eye, car.Id, PaintMode.Reset, false, panel, 0);
            w.PaintDirty.Clear();
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - a0;
        Line($"paint tool allocation: 80 paint requests allocated {bytes} bytes");
        if (bytes > 0)
            Fail($"paint tool: painting allocated {bytes} bytes");
        w.PaintDirty.Clear();
    }

    private static uint RoutePick(Construct c, int k) =>
        ConstructPaint.IsSet(c.Routes[k].Paint) ? c.Routes[k].Paint : ConstructPaint.FromVector(RouteRender.Colour(c.Routes[k].Type));

    private static void PaintStrokeCases()
    {
        // A sweep over A, A, B, A, C, B, then a click on D: each swept part is acted on once; a new stroke starts afresh.
        var s = new PaintStroke();
        var sweep = new[] { (1, false), (1, false), (2, false), (1, false), (3, false), (2, false), (2, true), (2, true) };
        var acted = new List<string>();
        foreach (var (i, r) in sweep)
            if (s.Try(7, r, i))
                acted.Add($"{(r ? "L" : "P")}{i}");
        var once = string.Join(",", acted) == "P1,P2,P3,L2";
        var otherBody = s.Try(8, false, 1);
        s.Clear();
        var again = s.Try(7, false, 1);
        var click = new PaintStroke().Try(7, false, 4);
        // Whole mode: one target for the construct however many parts the crosshair crosses.
        var whole = new PaintStroke();
        var wholeOnce = whole.Try(7, false, 5, whole: true) && !whole.Try(7, false, 9, whole: true) && !whole.Try(7, true, 3, whole: true) && whole.Try(8, false, 1, whole: true);
        for (var i = 0; i < 200; i++)
            s.Try(1, i % 2 == 0, i % 90);
        s.Clear();
        var a0 = GC.GetAllocatedBytesForCurrentThread();
        for (var round = 0; round < 50; round++)
        {
            for (var i = 0; i < 200; i++)
                s.Try(1, i % 2 == 0, i % 90);
            s.Clear();
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - a0;
        Line($"paint tool stroke: a sweep acted on {string.Join(",", acted)} (once each {once}), another construct's part {otherBody}, a new stroke starts afresh {again}, a click paints one {click}, whole mode is one target {wholeOnce}; 50 long strokes allocated {bytes} bytes");
        if (!once || !otherBody || !again || !click || !wholeOnce || bytes > 0)
            Fail("paint tool: a held stroke doesn't act on each swept part once, or allocates");
    }

    private static void PaintWireCases(ConstructWorld w, Construct car)
    {
        var hands = PtHands();
        var eye = car.WorldCom + new Vector3(0, 2f, 0);
        var panel = PartNamed(car, "Plate Steel 1 m");
        var route = car.RouteCount - 1;
        hands.Paint(w, 0, eye, car.Id, PaintMode.Whole, false, 0, PtRed);
        hands.Paint(w, 0, eye, car.Id, PaintMode.Part, false, panel, PtBlue);
        hands.Paint(w, 0, eye, car.Id, PaintMode.Part, true, route, PtBlue);
        w.PaintDirty.Clear();
        // The construct message (what a save would read): a fresh copy has the same paint.
        var writer = new PacketWriter(16384);
        ConstructNet.WriteFull(writer, car);
        var copyWorld = new ConstructWorld();
        var r = new PacketReader(writer.Span[1..]);
        ConstructNet.Read(ref r, copyWorld, 0);
        var copy = copyWorld.Get(car.Id);
        var same = copy != null && copy.PartCount == car.PartCount && copy.RouteCount == car.RouteCount;
        for (var i = 0; same && i < car.PartCount; i++)
            same &= copy!.Parts[i].Paint == car.Parts[i].Paint;
        for (var k = 0; same && k < car.RouteCount; k++)
            same &= copy!.Routes[k].Paint == car.Routes[k].Paint;
        // The change message: paint changes sent alone reach a copy made before them.
        for (var i = 0; i < car.PartCount; i++)
            car.Parts[i].Paint = 0;
        for (var k = 0; k < car.RouteCount; k++)
            car.Routes[k].Paint = 0;
        w.PaintDirty.Clear();
        ConstructNet.WriteFull(writer, car);
        r = new PacketReader(writer.Span[1..]);
        ConstructNet.Read(ref r, copyWorld, 0); // (the copy is unpainted again)
        hands.Paint(w, 0, eye, car.Id, PaintMode.Whole, false, 0, PtBlue);
        hands.Paint(w, 0, eye, car.Id, PaintMode.Part, false, panel, PtRed);
        hands.Paint(w, 0, eye, car.Id, PaintMode.Reset, false, 1, 0);
        var changes = w.PaintDirty.Count;
        var messages = 0;
        for (var from = 0; from < w.PaintDirty.Count; messages++)
        {
            from = ConstructPaint.WriteChanges(writer, w, w.PaintDirty, from);
            var cr = new PacketReader(writer.Span[1..]);
            ConstructNet.Read(ref cr, copyWorld, 0);
        }
        var copy2 = copyWorld.Get(car.Id)!;
        var delta = true;
        for (var i = 0; i < car.PartCount; i++)
            delta &= copy2.Parts[i].Paint == car.Parts[i].Paint;
        for (var k = 0; k < car.RouteCount; k++)
            delta &= copy2.Routes[k].Paint == car.Routes[k].Paint;
        // A part taken off keeps the others' paint (the colour moves with its part when the arrays close up).
        hands.Paint(w, 0, eye, car.Id, PaintMode.Whole, false, 0, PtRed);
        var tagged = new Dictionary<ushort, uint>();
        for (var i = 0; i < car.PartCount; i++)
            tagged[car.Parts[i].Tag] = car.Parts[i].Paint;
        var victim = PartNamed(car, "Plate Steel 1 m");
        var removed = victim >= 0;
        if (removed)
        {
            w.RemovePart(car, victim);
            if (car.Dirty)
                car.Rebuild();
        }
        var carried = removed && car.PartCount > 0;
        for (var i = 0; i < car.PartCount; i++)
            carried &= tagged.TryGetValue(car.Parts[i].Tag, out var was) && car.Parts[i].Paint == was;
        // Bad bytes are refused.
        var refused = false;
        try
        {
            writer.Begin(Msg.CBody).U8((byte)CBodyKind.Paint).U8(1).U16(car.Id).U8(0).U8(250).U8(1).U8(2).U8(3);
            var br = new PacketReader(writer.Span[1..]);
            ConstructNet.Read(ref br, copyWorld, 0);
            refused = copy2.Parts[0].Paint == copy2.Parts[0].Paint; // (a part index past the end is ignored, not trusted)
        }
        catch (MalformedPacketException)
        {
            refused = true;
        }
        Line($"paint tool wire: the construct message puts the same paint on a fresh copy {same}; {changes} changes in {messages} message(s) catch an unpainted copy up {delta}; a part taken off leaves the rest painted {carried}; a bad index ignored {refused}");
        if (!same)
            Fail("paint tool: the construct message doesn't carry the paint");
        if (!delta || messages < 1)
            Fail("paint tool: the paint change messages don't catch a copy up");
        if (!carried)
            Fail("paint tool: a part taken off lost or moved the others' paint");
    }

    private static void PaintBlueprintCases()
    {
        var bad = new List<string>();
        const string json = """
            { "format": "call-of-slop-construct-blueprint/1", "name": "Painted Pipe", "steps": [
              { "part": "Chest", "paint": "#c04020" },
              { "part": "Lid Hinge", "on": 0, "socket": "lid", "by": "base" }
            ] }
            """;
        var parsed = ConstructBuild.ParseBlueprint(json, "painted.json");
        var parseOk = parsed.StepPaint.Count == 2 && parsed.StepPaint[0] == ConstructPaint.Rgb(0xc0, 0x40, 0x20) && parsed.StepPaint[1] == 0;
        var world = new ConstructWorld();
        var built = ConstructBuild.Build(world, parsed, new Vector3(30, 0, 30), Quaternion.Identity, out var why);
        var buildOk = built != null && built.PartCount == 2 && built.Parts[0].Paint == ConstructPaint.Rgb(0xc0, 0x40, 0x20) && built.Parts[1].Paint == 0;
        if (!buildOk)
            bad.Add($"the blueprint didn't build painted ({why})");
        // The text form round-trips, and a bad colour is refused.
        var hex = ConstructPaint.ToHex(ConstructPaint.Rgb(0xc0, 0x40, 0x20));
        var text = hex == "#c04020" && ConstructPaint.TryParse(hex, out var back) && back == ConstructPaint.Rgb(0xc0, 0x40, 0x20) && !ConstructPaint.TryParse("c04020", out _) && !ConstructPaint.TryParse("#c0402", out _) && !ConstructPaint.TryParse("#zz0000", out _);
        var refused = 0;
        foreach (var badPaint in new[] { "\"paint\": \"red\"", "\"paint\": 5", "\"paint\": \"#12345\"" })
            try
            {
                ConstructBuild.ParseBlueprint(json.Replace("\"paint\": \"#c04020\"", badPaint), "bad.json");
            }
            catch (ConstructPartException)
            {
                refused++;
            }
        var linkRefused = false;
        try
        {
            ConstructRoutes.ParseLink(System.Text.Json.JsonDocument.Parse("{\"type\":\"data\",\"from\":0,\"to\":1,\"paint\":\"blue\"}").RootElement, "x.json", 0);
        }
        catch (ConstructPartException)
        {
            linkRefused = true;
        }
        var linkOk = ConstructRoutes.ParseLink(System.Text.Json.JsonDocument.Parse("{\"type\":\"pipe\",\"style\":\"water\",\"from\":0,\"to\":1,\"paint\":\"#2040c0\"}").RootElement, "x.json", 0).Paint == ConstructPaint.Rgb(0x20, 0x40, 0xc0);
        if (built != null)
            world.Remove(built);
        // The shipped Buggy still builds (no paint: nothing changes).
        var plain = ConstructBuild.FindBlueprint("Buggy")!.StepPaint.All(p => p == 0);
        Line($"paint tool blueprint: \"paint\" on a step builds painted {buildOk} (parsed {parseOk}); the colour text round-trips {text}; {refused}/3 bad step colours refused, a bad link colour refused {linkRefused}, a link's colour parsed {linkOk}; shipped blueprints unpainted {plain}");
        if (!parseOk || !text || refused != 3 || !linkRefused || !linkOk || !plain || bad.Count > 0)
            Fail($"paint tool: blueprint paint is wrong ({string.Join("; ", bad)})");
    }

    private static void PaintLoopback()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"paint tool: the {ConstructYardMap} map is missing");
            return;
        }
        const ushort port = 26391;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.5, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            Pump(server, [a], 0.25, ref now, clock);
        Pump(server, [a], 0.5, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Fail("paint tool loopback: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        Pump(server, [a], 0.3, ref now, clock);
        var w = server.Constructs;
        var g = ConstructWorld.Ground(World.Query, sp.Position.X + 2f, sp.Position.Z);
        var bp = ConstructBuild.FindBlueprint("Buggy");
        var car = bp != null ? ConstructBuild.BuildOnGround(w, bp, g, 0, out _) : null;
        if (car == null)
        {
            Fail("paint tool loopback: the Buggy didn't build");
            return;
        }
        car.Owner = sp.Id;
        car.Rebuild();
        Pump(server, [a], 0.8, ref now, clock);
        // The client puts the paint tool out and paints; both ends agree. (Its eye must be within reach: the server checks.)
        var panel = PartNamed(car, "Plate Steel 1 m");
        a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Paint);
        Pump(server, [a], 0.3, ref now, clock);
        var toolOut = server.SandboxHands.Hand(sp.Id).Tool == ConstructTool.Paint;
        a.SandboxPaint(car.Id, PaintMode.Part, false, panel, PtRed);
        a.SandboxPaint(car.Id, PaintMode.Part, true, car.RouteCount - 1, PtBlue);
        Pump(server, [a], 0.8, ref now, clock);
        var copy = a.Constructs.Get(car.Id);
        var part = car.Parts[panel].Paint == PtRed && ConstructPaint.Painted(car) == 2;
        var partClient = copy != null && copy.Parts[panel].Paint == PtRed && copy.Routes[car.RouteCount - 1].Paint == PtBlue && ConstructPaint.Painted(copy) == 2;
        a.SandboxPaint(car.Id, PaintMode.Whole, false, 0, PtBlue);
        Pump(server, [a], 0.8, ref now, clock);
        var whole = ConstructPaint.Painted(car) > 10 && car.Parts[panel].Paint == PtBlue;
        copy = a.Constructs.Get(car.Id);
        var wholeClient = copy != null && Enumerable.Range(0, car.PartCount).All(i => copy.Parts[i].Paint == car.Parts[i].Paint) && Enumerable.Range(0, car.RouteCount).All(k => copy.Routes[k].Paint == car.Routes[k].Paint);
        a.SandboxPaint(car.Id, PaintMode.Reset, false, 0, 0);
        a.SandboxPaint(car.Id, PaintMode.Whole, false, 0, PtRed);
        a.SandboxPaint(car.Id, PaintMode.Reset, false, panel, 0);
        Pump(server, [a], 0.8, ref now, clock);
        var reset = car.Parts[panel].Paint == 0 && car.Parts[(panel + 1) % car.PartCount].Paint is var p2 && (p2 == PtRed || p2 == 0);
        copy = a.Constructs.Get(car.Id);
        var resetClient = copy != null && Enumerable.Range(0, car.PartCount).All(i => copy.Parts[i].Paint == car.Parts[i].Paint);
        // A client that joins later gets the paint in the construct message.
        using var late = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Late", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a, late], 2.0, ref now, clock);
        var lateCopy = late.Constructs.Get(car.Id);
        var joined = lateCopy != null && Enumerable.Range(0, car.PartCount).All(i => lateCopy.Parts[i].Paint == car.Parts[i].Paint);
        Line($"paint tool loopback: tool out {toolOut}; the client's part + link paint on the server {part}, its copy {partClient}; whole construct {whole}, copy {wholeClient}; reset {reset}, copy {resetClient}; a client joining later sees the paint {joined}");
        if (!toolOut || !part || !partClient || !whole || !wholeClient || !reset || !resetClient || !joined)
            Fail("paint tool loopback: a client's paint didn't reach the server, or a copy disagrees");
    }
}
