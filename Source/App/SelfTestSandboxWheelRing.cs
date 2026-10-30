using System.Numerics;
using Brutal.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Sandbox tool wheel outer ring" (board item dev-wheel-outer-ring-wo01; RadialMenu.cs, SandboxToolWheel.cs,
    /// SandboxLinkWheel.cs): the outer ring's arcs (a few options in an arc centred on the tool, many round the full
    /// circle, each picked by direction); in an offline Sandbox game pointing at the linking tool shows the ring with every
    /// link type, pipe style and edit mode; flicking out onto each option and letting go takes the linking tool out with
    /// it; letting go on the linking tool itself keeps the last option; a tool with two options (the welder, given a test
    /// set) gets a partial arc, and outside it the pointer turns the inner ring as before; and the centre text (each tool's
    /// and option's name, the title, a name far too long) fits the centre circle at UI scales 0.75 to 2.
    /// </summary>
    private static void CheckSandboxToolWheelRing()
    {
        // ---- The arcs ---------------------------------------------------------------------------------------------
        const int N = (int)SandboxToolWheel.Slot.COUNT;
        static Vector2 At(float radians, float r) => new(MathF.Sin(radians) * r, -MathF.Cos(radians) * r);
        var bad = new List<string>();
        foreach (var (owner, options) in new[] { (6, 13), (2, 2), (0, 3), (6, 9), (4, 1), (5, 10) })
        {
            var step = RadialMenu.OptionStep(options);
            for (var i = 0; i < options; i++)
            {
                var mid = RadialMenu.OptionAngle(i, owner, N, options);
                foreach (var (a, what) in new[] { (mid, "middle"), (mid - step * 0.45f, "left edge"), (mid + step * 0.45f, "right edge") })
                    if (RadialMenu.OptionAt(At(a, 120), owner, N, options) != i)
                        bad.Add($"{options} options round item {owner}: option {i}'s {what} gave {RadialMenu.OptionAt(At(a, 120), owner, N, options)}");
            }
            // The arc's centre is the item's direction.
            var centre = (RadialMenu.OptionAngle(0, owner, N, options) + RadialMenu.OptionAngle(options - 1, owner, N, options)) * 0.5f;
            if (MathF.Abs(centre - RadialMenu.SegmentAngle(owner, N)) > 1e-4f)
                bad.Add($"{options} options round item {owner}: centred on {centre * 180 / MathF.PI:0.#} deg, not the item's {RadialMenu.SegmentAngle(owner, N) * 180 / MathF.PI:0.#}");
            // A partial arc: the far side picks nothing.
            if (!RadialMenu.FullCircle(options) && RadialMenu.OptionAt(At(RadialMenu.SegmentAngle(owner, N) + MathF.PI, 120), owner, N, options) != -1)
                bad.Add($"{options} options round item {owner}: the far side picked an option");
        }
        var full13 = RadialMenu.FullCircle(13);
        var part3 = !RadialMenu.FullCircle(3) && MathF.Abs(RadialMenu.OptionStep(3) - RadialMenu.OptionStepMax) < 1e-6f;
        // The third tier's arcs (25 degrees an item): 5 items span 125 degrees round the owner's direction, picked by direction too.
        foreach (var items in new[] { 5, 2, 3, 1 })
        {
            var centre = 1.3f;
            var step = RadialMenu.OptionStep(items, RadialMenu.SubStepMax);
            for (var i = 0; i < items; i++)
                foreach (var d in new[] { 0f, -0.45f, 0.45f })
                    if (RadialMenu.ArcAt(At(RadialMenu.ArcAngle(i, centre, items, RadialMenu.SubStepMax) + d * step, 150), centre, items, RadialMenu.SubStepMax) != i)
                        bad.Add($"sub arc of {items}: item {i} at {d} gave something else");
            if (RadialMenu.FullCircle(items, RadialMenu.SubStepMax) || RadialMenu.ArcAt(At(centre + MathF.PI, 150), centre, items, RadialMenu.SubStepMax) != -1)
                bad.Add($"sub arc of {items}: the far side picked");
        }
        var span5 = RadialMenu.OptionStep(5, RadialMenu.SubStepMax) * 5 * 180f / MathF.PI;
        Line($"third tier arc: 5 items span {span5:0.#} degrees");
        if (MathF.Abs(span5 - 125f) > 0.01f)
            bad.Add($"the 5 item third tier spans {span5} degrees, not 125");
        Line($"outer ring arcs: {bad.Count} wrong picks over 13/2/3/9/1/10 options (middles and edges, centred on the item); 13 options round the full circle {full13}, 3 in an arc {part3}");
        if (bad.Count > 0)
            Fail($"outer ring: {string.Join("; ", bad.Take(4))}");
        if (!full13 || !part3)
            Fail("outer ring: the choice between an arc and the full circle is wrong");

        // ---- The linking tool's options cover every type, style and mode ------------------------------------------
        var probe = new SandboxLinkWheel();
        var types = new HashSet<RouteType>();
        var styles = new HashSet<byte>();
        var modes = new HashSet<LinkMode>();
        var highlightToggles = false;
        for (var i = 0; i < probe.Options.Length; i++)
        {
            var h = probe.Highlight;
            probe.Mode = LinkMode.Link;
            probe.Choose(i);
            if (probe.Highlight != h)
                highlightToggles = true;
            else if (probe.Mode == LinkMode.Link)
            {
                types.Add(probe.Type);
                if (probe.Type == RouteType.Pipe)
                    styles.Add(probe.Style);
            }
            else
                modes.Add(probe.Mode);
            if (probe.Highlight == h && probe.CurrentOption != i)
                bad.Add($"option {i} ({probe.Options[i].Label}) chosen shows {probe.CurrentOption} as current");
        }
        var arcN = probe.Options[(int)SandboxLinkWheel.Option.Pipe].Options?.Length ?? 0;
        for (var k = 0; k < arcN; k++)
        {
            probe.Mode = LinkMode.Link;
            probe.ChooseSub((int)SandboxLinkWheel.Option.Pipe, k);
            types.Add(probe.Type);
            styles.Add(probe.Style);
            if (probe.Type != RouteType.Pipe || probe.Style != SandboxLinkWheel.ArcStyles[k] || SandboxLinkWheel.ArcIndexOf(probe.Style) != k)
                bad.Add($"pipe arc item {k} gave {probe.Type}/{probe.Style}");
            if (probe.CurrentOption != (int)SandboxLinkWheel.Option.Pipe)
                bad.Add($"pipe arc item {k} not current");
        }
        if (arcN != 5 || probe.Options.Length != 9 || SandboxLinkWheel.ArcStyles[2] != 255)
            bad.Add($"the linking tool has {probe.Options.Length} options and a {arcN} item pipe arc (9 and 5, Auto in the middle expected)");
        var everyType = Enumerable.Range(0, (int)RouteType.COUNT).All(t => types.Contains((RouteType)t));
        var everyStyle = styles.Contains(255) && Enumerable.Range(0, (int)PipeStyle.COUNT).All(s => styles.Contains((byte)s));
        var everyMode = Enum.GetValues<LinkMode>().All(m => m == LinkMode.Link || modes.Contains(m));
        Line($"linking tool options: {probe.Options.Length} ({string.Join(", ", probe.Options.Select(o => o.Label))}); every link type {everyType}, every pipe style and auto {everyStyle}, every edit mode {everyMode}, highlight toggles {highlightToggles}");
        if (!everyType || !everyStyle || !everyMode || !highlightToggles)
            Fail("outer ring: the linking tool's options miss a link type, a pipe style or an edit mode");
        if (bad.Count > 0)
            Fail($"outer ring: {string.Join("; ", bad.Take(4))}");

        // ---- In a Sandbox game ------------------------------------------------------------------------------------
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"outer ring: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        Run(g, ref now, Game.CountdownSeconds + 0.3);
        g.RequestRespawn();
        Run(g, ref now, 1.0);
        if (g.Local is not { Alive: true } me)
        {
            Fail("outer ring: no local player in the Sandbox game");
            return;
        }
        var wheel = new SandboxToolWheel();
        var links = new SandboxLinkWheel();
        var welderModes = new TestToolOptions("Weld", "Cut");
        wheel.SetOptions(SandboxToolWheel.Slot.Linker, links);
        wheel.SetOptions(SandboxToolWheel.Slot.Welder, welderModes);
        var w = wheel.Menu;
        const int Linker = (int)SandboxToolWheel.Slot.Linker;
        ConstructTool Tool() => g.SandboxHands.Hand(me.Id).Tool;
        void Hold() => wheel.Step(g, true, Vector2.Zero, false);
        void Flick(Vector2 to)
        {
            for (var f = 0; f < 3; f++)
                wheel.Step(g, true, to / 3, false);
        }
        ConstructTool LetGo()
        {
            wheel.Step(g, false, Vector2.Zero, false);
            Run(g, ref now, 0.05);
            return Tool();
        }

        // Pointing at the linking tool shows its ring with every option, animating in.
        g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Welder);
        Run(g, ref now, 0.05);
        Hold();
        var before = w.Ring;
        Flick(new Vector2(-45, 0));
        var ring = w.Ring;
        var shown = w.Items[Linker].Options?.Length ?? 0;
        var ringIn0 = w.RingIn;
        for (var f = 0; f < 8; f++)
            wheel.Step(g, true, Vector2.Zero, false);
        var ringIn1 = w.RingIn;
        var innerHovered = w.Hovered;
        var noOption = w.HoveredOption;
        var labels = w.Items[Linker].Options is { } lo ? string.Join(", ", lo.Select(o => o.Label)) : "none";
        wheel.Menu.Close();
        wheel.Step(g, false, Vector2.Zero, false);
        Line($"outer ring shown: before pointing {before}, on the linking tool {ring} with {shown} options ({labels}); animating in {ringIn0:0.00} then {ringIn1:0.00}; inner {innerHovered}, option {noOption}");
        if (before != -1 || ring != Linker || shown != (int)SandboxLinkWheel.Option.COUNT || innerHovered != Linker || noOption != -1)
            Fail("outer ring: pointing at the linking tool didn't show its ring with every link type");
        if (ringIn1 < 0.999f || ringIn0 >= ringIn1)
            Fail("outer ring: the ring didn't animate in");

        // Flicking out onto each option and letting go takes the linking tool out with it.
        var wrong = new List<string>();
        var expect = new SandboxLinkWheel();
        for (var i = 0; i < links.Options.Length; i++)
        {
            g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Welder);
            Run(g, ref now, 0.05);
            Hold();
            Flick(new Vector2(-45, 0));
            var target = At(RadialMenu.OptionAngle(i, Linker, N, links.Options.Length), w.Reach + w.OuterGap + 25);
            wheel.Step(g, true, target - w.Pointer, false);
            var hov = w.Hovered;
            var opt = w.HoveredOption;
            var tool = LetGo();
            expect.Choose(i);
            if (hov != Linker || opt != i || w.HoveredSub != -1 || tool != ConstructTool.Linker || links.Type != expect.Type || links.Style != expect.Style || links.Mode != expect.Mode || links.Highlight != expect.Highlight)
                wrong.Add($"option {i} ({links.Options[i].Label}): hovered {hov}/{opt}, tool {tool}, got {links.Type}/{links.Style}/{links.Mode}/{links.Highlight}, expected {expect.Type}/{expect.Style}/{expect.Mode}/{expect.Highlight}");
        }
        Line($"outer ring picks: {links.Options.Length - wrong.Count} of {links.Options.Length} options took the linking tool out with that option{(wrong.Count > 0 ? ": " + string.Join("; ", wrong.Take(3)) : "")}");
        if (wrong.Count > 0)
            Fail($"outer ring: choosing an outer segment didn't equip it: {wrong[0]}");

        // Letting go on the linking tool itself keeps its last option (and it is marked when the ring shows).
        links.ChooseSub((int)SandboxLinkWheel.Option.Pipe, SandboxLinkWheel.ArcIndexOf((byte)PipeStyle.Oil));
        g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Welder);
        Run(g, ref now, 0.05);
        Hold();
        Flick(new Vector2(-45, 0));
        var marked = w.Items[Linker].OptionCurrent;
        var innerTool = LetGo();
        var kept = links.Type == RouteType.Pipe && links.Style == (byte)PipeStyle.Oil && links.Mode == LinkMode.Link;
        links.Choose((int)SandboxLinkWheel.Option.Reroute);
        Hold();
        Flick(new Vector2(-45, 0));
        LetGo(); // (already out: still the linking tool, still re-routing)
        var keptMode = links.Mode == LinkMode.Reroute && Tool() == ConstructTool.Linker;
        // Letting go in the dead zone after the ring showed: a cancel, nothing changed.
        g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Wrench);
        Run(g, ref now, 0.05);
        Hold();
        Flick(new Vector2(-45, 0));
        wheel.Step(g, true, -w.Pointer, false);
        var cancelTool = LetGo();
        Line($"outer ring, let go on the tool: the linking tool out {innerTool}, the oil pipe kept {kept} (marked {marked}); a re-route kept {keptMode}; let go in the middle kept the {cancelTool} (mode {links.Mode})");
        if (innerTool != ConstructTool.Linker || !kept || marked != (int)SandboxLinkWheel.Option.Pipe || links.Options[(int)SandboxLinkWheel.Option.Pipe].OptionCurrent != SandboxLinkWheel.ArcIndexOf((byte)PipeStyle.Oil) || !keptMode)
            Fail("outer ring: letting go on the inner segment didn't take the tool out with its last option");
        if (cancelTool != ConstructTool.Wrench || links.Mode != LinkMode.Reroute)
            Fail("outer ring: letting go in the middle after the ring showed changed something");

        ThirdTier(g, ref now, me, wheel, links);

        // A partial arc (the welder with two test modes, east): an option picked; outside the arc the inner ring turns.
        Hold();
        Flick(new Vector2(45, 0));
        var welderRing = w.Ring;
        var cutAt = At(RadialMenu.OptionAngle(1, (int)SandboxToolWheel.Slot.Welder, N, 2), w.Reach + w.OuterGap + 25);
        wheel.Step(g, true, cutAt - w.Pointer, false);
        var cutHovered = w.HoveredOption;
        var welder = LetGo();
        Hold();
        Flick(new Vector2(45, 0));
        wheel.Step(g, true, At(0, w.Reach + w.OuterGap + 25) - w.Pointer, false); // (straight up, far out: outside the arc, over the wrench)
        var outsideInner = w.Hovered;
        var outsideRing = w.Ring;
        var pulledIn = w.Pointer.Length() <= w.Reach + 0.01f;
        var outsideTool = LetGo();
        Line($"outer ring, two welder modes: ring {welderRing}, Cut hovered {cutHovered}, took out the {welder} with {welderModes.Chosen}; outside the arc: inner {outsideInner}, ring {outsideRing}, pointer back in reach {pulledIn}, took out the {outsideTool}");
        if (welderRing != (int)SandboxToolWheel.Slot.Welder || cutHovered != 1 || welder != ConstructTool.Welder || welderModes.Chosen != 1)
            Fail("outer ring: a tool with two options didn't pick from its arc");
        if (outsideInner != (int)SandboxToolWheel.Slot.Wrench || outsideRing != -1 || !pulledIn || outsideTool != ConstructTool.Wrench)
            Fail("outer ring: outside a partial arc the pointer didn't turn the inner ring");

        SandboxWheelCentreText(wheel, links);
    }

    /// <summary>
    /// The third tier (board item dev-wheel-third-tier-pipes-wt01): resting on the Pipe option opens a 5 item arc round its
    /// direction (well under 360 degrees), picking Water equips Pipe with the water style, letting go on Pipe itself keeps
    /// the last style (its label says which), moving to Shaft or inward closes the arc, and the pick doesn't flicker at the
    /// edge.
    /// </summary>
    private static void ThirdTier(Game g, ref double clock, Player me, SandboxToolWheel wheel, SandboxLinkWheel links)
    {
        const int N = (int)SandboxToolWheel.Slot.COUNT;
        const int Linker = (int)SandboxToolWheel.Slot.Linker;
        const int Pipe = (int)SandboxLinkWheel.Option.Pipe;
        var now = clock;
        var w = wheel.Menu;
        var optN = links.Options.Length;
        var arcN = links.Options[Pipe].Options!.Length;
        var centre = RadialMenu.OptionAngle(Pipe, Linker, N, optN);
        var edge = w.Reach + w.OuterDepth;
        Vector2 At(float radians, float r) => new(MathF.Sin(radians) * r, -MathF.Cos(radians) * r);
        void Out(float angle, float r) => wheel.Step(g, true, At(angle, r) - w.Pointer, false);
        void Open()
        {
            g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Welder);
            Run(g, ref now, 0.05);
            wheel.Step(g, true, Vector2.Zero, false);
            wheel.Step(g, true, new Vector2(-45, 0), false);
        }
        ConstructTool LetGo()
        {
            wheel.Step(g, false, Vector2.Zero, false);
            Run(g, ref now, 0.05);
            return g.SandboxHands.Hand(me.Id).Tool;
        }
        var bad = new List<string>();
        var fresh = new SandboxLinkWheel();
        var firstLabel = fresh.Options[Pipe].Label;

        // Resting on Pipe: the arc shows, spanning well under a full circle, centred on Pipe's direction; nothing picked yet.
        links.Style = 255;
        _ = links.CurrentOption;
        Open();
        Out(centre, w.Reach + w.OuterGap + 25);
        var shown = w.SubRing == Pipe && w.Hovered == Linker && w.HoveredOption == Pipe && w.HoveredSub == -1;
        var spanDeg = RadialMenu.OptionStep(arcN, RadialMenu.SubStepMax) * arcN * 180f / MathF.PI;
        var mid = (RadialMenu.ArcAngle(0, centre, arcN, RadialMenu.SubStepMax) + RadialMenu.ArcAngle(arcN - 1, centre, arcN, RadialMenu.SubStepMax)) * 0.5f;
        if (!shown || arcN != 5 || spanDeg is < 100f or > 180f || MathF.Abs(mid - centre) > 1e-4f)
            bad.Add($"resting on Pipe: arc {w.SubRing}, hovered {w.Hovered}/{w.HoveredOption}/{w.HoveredSub}, {arcN} items span {spanDeg:0.#} deg");
        var labels = string.Join(", ", links.Options[Pipe].Options!.Select(o => o.Label));
        var autoMid = links.Options[Pipe].Options![2].Label.StartsWith("Auto");

        // Hysteresis: out past the edge picks (at the entry distance), stays down to the keep distance, and closes inward.
        var water = 1;
        var angleWater = RadialMenu.ArcAngle(water, centre, arcN, RadialMenu.SubStepMax);
        Out(angleWater, edge + RadialMenu.SubGap - 2f);
        var notYet = w.HoveredSub;
        Out(angleWater, edge + RadialMenu.SubGap + 12f);
        var picked = w.HoveredSub;
        var pointerOut = w.Pointer.Length() > edge + 1f;
        Out(angleWater, edge + RadialMenu.SubKeep + 3f);
        var kept = w.HoveredSub;
        Out(angleWater, edge + RadialMenu.SubKeep - 1f);
        var released = w.HoveredSub;
        var arcStays = w.SubRing == Pipe && w.Hovered == Linker;
        Out(angleWater, edge + RadialMenu.SubGap - 2f);
        var noBounce = w.HoveredSub; // (not back on until the entry distance)
        if (notYet != -1 || picked != water || !pointerOut || kept != water || released != -1 || !arcStays || noBounce != -1)
            bad.Add($"hysteresis: before the entry {notYet}, on Water {picked} (pointer out {pointerOut}), kept {kept}, let go of {released} (arc stays {arcStays}), not bouncing {noBounce}");

        // Picking Water equips Pipe with the water style (and Water is marked on the arc next time).
        Out(angleWater, edge + RadialMenu.SubGap + 12f);
        var toolWater = LetGo();
        var gotWater = links.Type == RouteType.Pipe && links.Style == (byte)PipeStyle.Water && links.Mode == LinkMode.Link && toolWater == ConstructTool.Linker;
        var labelWater = links.Options[Pipe].Label;
        var markWater = links.Options[Pipe].OptionCurrent;
        if (!gotWater || labelWater != "Pipe (water)" || markWater != 1 || w.IsOpen)
            bad.Add($"picking Water: type {links.Type}, style {links.Style}, tool {toolWater}, label '{labelWater}', marked {markWater}");

        // Letting go on Pipe itself uses the last style (and switches back to routing).
        links.Mode = LinkMode.Delete;
        Open();
        Out(centre, w.Reach + w.OuterGap + 25);
        var onPipe = w.HoveredOption == Pipe && w.HoveredSub == -1;
        var toolPipe = LetGo();
        var lastKept = links.Type == RouteType.Pipe && links.Style == (byte)PipeStyle.Water && links.Mode == LinkMode.Link && toolPipe == ConstructTool.Linker;
        if (!onPipe || !lastKept)
            bad.Add($"letting go on Pipe: on it {onPipe}, type {links.Type}, style {links.Style}, mode {links.Mode}");

        // Auto from the arc's middle, then Steam on the far end.
        Open();
        Out(centre, w.Reach + w.OuterGap + 25);
        Out(RadialMenu.ArcAngle(2, centre, arcN, RadialMenu.SubStepMax), edge + RadialMenu.SubGap + 12f);
        LetGo();
        var autoOk = links.Style == 255 && links.Options[Pipe].Label == "Pipe (auto)" && links.Options[Pipe].OptionCurrent == 2;
        Open();
        Out(centre, w.Reach + w.OuterGap + 25);
        Out(RadialMenu.ArcAngle(4, centre, arcN, RadialMenu.SubStepMax), edge + RadialMenu.SubGap + 12f);
        LetGo();
        var steamOk = links.Style == (byte)PipeStyle.Steam && links.Options[Pipe].Label == "Pipe (steam)";
        if (!autoOk || !steamOk)
            bad.Add($"Auto from the middle {autoOk}, Steam from the end {steamOk}");

        // Moving round to Shaft (or inward) closes the arc and picks the shaft.
        Open();
        Out(centre, w.Reach + w.OuterGap + 25);
        Out(angleWater, edge + RadialMenu.SubGap + 12f);
        var shaftAngle = RadialMenu.OptionAngle((int)SandboxLinkWheel.Option.Shaft, Linker, N, optN);
        Out(shaftAngle, w.Reach + w.OuterGap + 25);
        var closed = w.SubRing == -1 && w.HoveredSub == -1 && w.HoveredOption == (int)SandboxLinkWheel.Option.Shaft && w.Pointer.Length() <= edge + 0.01f;
        Out(centre, w.Reach + w.OuterGap + 25);
        var reopened = w.SubRing == Pipe;
        Out(centre, 40f);
        var closedIn = w.SubRing == -1 && w.HoveredOption == -1;
        Out(RadialMenu.SegmentAngle(Linker, N), 45f); // (back on the linking tool, then out onto Shaft)
        Out(shaftAngle, w.Reach + w.OuterGap + 25);
        LetGo();
        var shaftOk = links.Type == RouteType.Shaft;
        if (!closed || !reopened || !closedIn || !shaftOk)
            bad.Add($"closing: moving to Shaft closed it {closed}, back on Pipe reopened it {reopened}, inward closed it {closedIn}, Shaft equipped {shaftOk}");

        Line($"third tier: Pipe's arc ({labels}) spans {spanDeg:0.#} degrees, Auto in the middle {autoMid}, fresh label '{firstLabel}'; shown {shown}; hysteresis, Water, Auto, Steam, last style, closing: {bad.Count} problems");
        if (firstLabel != "Pipe (auto)" || !autoMid)
            bad.Add($"the fresh Pipe label is '{firstLabel}' and the arc's middle is not Auto");
        clock = now;
        if (bad.Count > 0)
            Fail($"third tier: {string.Join("; ", bad.Take(3))}");
    }

    /// <summary>The centre text: every tool's and option's name, the title and a name far too long fit the centre circle at UI scales 0.75 to 2.</summary>
    private static void SandboxWheelCentreText(SandboxToolWheel wheel, SandboxLinkWheel links)
    {
        var ui = new Ui();
        ui.InitHeadless(new float2(1600, 900));
        try
        {
            ui.BeginFrame();
            const string TooLong = "An extremely long tool name that could never fit";
            var names = wheel.Menu.Items.Select(i => i.Label).Concat(links.Options.Select(o => o.Label)).Append(wheel.Menu.Title).Append(TooLong).ToArray();
            var over = new List<string>();
            var shrunk = new HashSet<string>();
            var cut = new HashSet<string>();
            var smallest = float.MaxValue;
            foreach (var scale in new[] { 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f })
                foreach (var name in names)
                {
                    var shown = RadialMenu.FitCentre(name, scale, out var size);
                    var width = Hud.TextWidth(shown, size);
                    var room = RadialMenu.CentreWidth(scale, size);
                    smallest = MathF.Min(smallest, size / scale);
                    if (width > room + 0.01f || size > RadialMenu.CentreSize * scale + 0.01f || size < RadialMenu.CentreMinSize * scale - 0.01f || size * 0.5f > RadialMenu.CentreRadius * scale)
                        over.Add($"'{name}' at {scale}: '{shown}' {width:0.#} px wide at {size:0.#} px in {room:0.#} px");
                    if (shown != name)
                        cut.Add(name);
                    else if (size < RadialMenu.CentreSize * scale - 0.01f)
                        shrunk.Add(name);
                }
            ui.EndFrame();
            Line($"wheel centre text: {names.Length} names at scales 0.75 to 2: {over.Count} overflow; shrunk to fit: {(shrunk.Count > 0 ? string.Join(", ", shrunk) : "none")}; cut short: {(cut.Count > 0 ? string.Join(", ", cut) : "none")}; smallest {smallest:0.#} px a scale");
            if (over.Count > 0)
                Fail($"outer ring: centre text doesn't fit the centre circle: {string.Join("; ", over.Take(3))}");
            if (!cut.Contains(TooLong) || cut.Count != 1)
                Fail($"outer ring: the centre text was cut short wrongly ({string.Join(", ", cut)}; only the over-long test name should be)");
        }
        finally
        {
            ui.DisposeHeadless();
        }
    }

    /// <summary>A tool's options for the test (the welder's modes, later).</summary>
    private sealed class TestToolOptions : IToolOptions
    {
        public RadialMenu.Item[] Options { get; }
        public RadialIcon? Icons => null;
        public int CurrentOption => Chosen;
        public int Chosen = 0;

        public TestToolOptions(params string[] names) => Options = names.Select(n => new RadialMenu.Item { Label = n, Hint = "a test mode" }).ToArray();

        public void Choose(int option) => Chosen = option;
    }
}
