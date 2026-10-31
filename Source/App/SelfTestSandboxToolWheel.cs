using System.Numerics;
using Brutal.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Sandbox tool wheel": the radial widget (RadialMenu.cs) and the Sandbox tool wheel on it (SandboxToolWheel.cs;
    /// docs/sandbox/CONSTRUCTS.md, "The tool wheel"). The direction-to-segment maths (every segment, both edges, the
    /// wrap-around at the top, the dead zone, other counts), the pointer held within reach; the key (the middle mouse
    /// button, unshared, added by binds version 10); in an offline Sandbox game a scripted hold-flick-release takes out
    /// each tool through the number keys' request, the current tool is marked, letting go in the middle cancels, a tool
    /// still to come says so, and the wheel doesn't open in a seat and closes without choosing on death or a menu; drawn
    /// headless with no ImGui error and nothing allocated per frame while open; and a real client's pick reaches the server.
    /// </summary>
    private static void CheckSandboxToolWheel()
    {
        // ---- The maths --------------------------------------------------------------------------------------------
        var bad = new List<string>();
        static Vector2 Dir(float degrees, float r) => new(MathF.Sin(degrees * MathF.PI / 180) * r, -MathF.Cos(degrees * MathF.PI / 180) * r);
        foreach (var n in new[] { 8, 4, 3, 6, 1 })
        {
            var step = 360f / n;
            for (var i = 0; i < n; i++)
            {
                var mid = i * step;
                foreach (var (angle, what) in new[] { (mid, "middle"), (mid - step / 2 + 0.5f, "left edge"), (mid + step / 2 - 0.5f, "right edge") })
                    if (RadialMenu.SegmentAt(Dir(angle, 50), n, 20) != i)
                        bad.Add($"{n} segments: {angle:0.#} deg ({what} of {i}) gave {RadialMenu.SegmentAt(Dir(angle, 50), n, 20)}");
            }
        }
        // The wrap-around at the top (8 segments: segment 0 spans 337.5..22.5 degrees) and the dead zone.
        var wrap = RadialMenu.SegmentAt(Dir(359, 40), 8, 20) == 0 && RadialMenu.SegmentAt(Dir(338, 40), 8, 20) == 0 && RadialMenu.SegmentAt(Dir(337, 40), 8, 20) == 7
                   && RadialMenu.SegmentAt(Dir(-1, 40), 8, 20) == 0 && RadialMenu.SegmentAt(Dir(22, 40), 8, 20) == 0 && RadialMenu.SegmentAt(Dir(23, 40), 8, 20) == 1;
        var dead = RadialMenu.SegmentAt(Vector2.Zero, 8, 20) == -1 && RadialMenu.SegmentAt(new Vector2(12, 12), 8, 20) == -1 && RadialMenu.SegmentAt(new Vector2(19.9f, 0), 8, 20) == -1
                   && RadialMenu.SegmentAt(new Vector2(20.1f, 0), 8, 20) == 2 && RadialMenu.SegmentAt(new Vector2(float.NaN, 30), 8, 20) == -1 && RadialMenu.SegmentAt(new Vector2(0, -30), 0, 20) == -1;
        var m = new RadialMenu(8) { DeadZone = 20, Reach = 60 };
        m.Open();
        for (var i = 0; i < 10; i++)
            m.Move(new Vector2(50, 0)); // (a long drag east: held at reach)
        var held = m.Pointer.Length();
        m.Move(new Vector2(-50, -50)); // (one small flick then turns it north-west-ish)
        var turned = m.Hovered;
        m.Move(new Vector2(float.PositiveInfinity, 0));
        var finite = float.IsFinite(m.Pointer.X);
        var released = m.Release();
        var after = m.IsOpen || m.Hovered != -1 || m.Release() != -1;
        Line($"radial maths: {bad.Count} wrong segments over 8/4/3/6/1 segments (middles and edges); wrap-around at the top {wrap}; dead zone {dead}; "
             + $"a long drag held at {held:0.#} of 60; one flick back turned it to {turned}; not-a-number moves ignored {finite}; release gave {released}, closed {!after}");
        if (bad.Count > 0)
            Fail($"tool wheel: wrong segments: {string.Join("; ", bad.Take(4))}");
        if (!wrap || !dead || MathF.Abs(held - 60) > 0.01f || turned != 0 || !finite || released != 0 || after)
            Fail("tool wheel: the wrap-around, the dead zone or the pointer's reach is wrong");

        // ---- The key ----------------------------------------------------------------------------------------------
        var kd = Keybinds.Defaults;
        var middle = Keybinds.Mouse(Brutal.GlfwApi.GlfwMouseButton.Middle);
        if (kd[(int)GameAction.ToolWheel] != middle || Duplicates(kd).Length > 0)
            Fail($"tool wheel: the default key isn't an unshared middle mouse button ({Duplicates(kd)})");
        var old = (int[])kd.Clone();
        old[(int)GameAction.ToolWheel] = Keybinds.Unbound;
        var moved = Keybinds.Migrate(old, 9);
        var taken = (int[])kd.Clone();
        taken[(int)GameAction.ToolWheel] = Keybinds.Unbound;
        taken[(int)GameAction.Melee] = middle;
        Keybinds.Migrate(taken, 9);
        Line($"tool wheel key: {Keybinds.Name(kd[(int)GameAction.ToolWheel])}; binds version 9 migrated to it {old[(int)GameAction.ToolWheel] == middle} ({moved.Count} moved); "
             + $"a file with the middle button on melee keeps it {taken[(int)GameAction.Melee] == middle} (the wheel unbound {taken[(int)GameAction.ToolWheel] == Keybinds.Unbound})");
        if (old[(int)GameAction.ToolWheel] != middle || moved.Count(a => a == GameAction.ToolWheel) != 1 || taken[(int)GameAction.Melee] != middle || taken[(int)GameAction.ToolWheel] != Keybinds.Unbound)
            Fail("tool wheel: the binds version 10 migration is wrong");

        // ---- In a Sandbox game ------------------------------------------------------------------------------------
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"tool wheel: the {ConstructYardMap} map is missing");
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
            Fail("tool wheel: no local player in the Sandbox game");
            return;
        }
        var wheel = new SandboxToolWheel();
        var w = wheel.Menu;
        g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.None);
        // Hold, flick (three small frames of mouse movement), let go.
        ConstructTool Flick(Vector2 to, out int hovered, out int marked)
        {
            wheel.Step(g, true, Vector2.Zero, false);
            marked = w.Current;
            for (var f = 0; f < 3; f++)
                wheel.Step(g, true, to / 3, false);
            hovered = w.Hovered;
            wheel.Step(g, false, Vector2.Zero, false);
            Run(g, ref now, 0.05);
            return g.SandboxHands.Hand(me.Id).Tool;
        }
        var east = Flick(new Vector2(36, 2), out var hE, out var mStart);
        var north = Flick(new Vector2(-3, -30), out var hN, out var mWelder);
        var west = Flick(new Vector2(-45, 0), out var hW, out var mWrench);
        var south = Flick(new Vector2(4, 40), out var hS, out var mLinker);
        var picks = east == ConstructTool.Welder && north == ConstructTool.Wrench && west == ConstructTool.Linker && south == ConstructTool.None
                    && hE == (int)SandboxToolWheel.Slot.Welder && hN == (int)SandboxToolWheel.Slot.Wrench && hW == (int)SandboxToolWheel.Slot.Linker && hS == (int)SandboxToolWheel.Slot.Hands;
        var marks = mStart == (int)SandboxToolWheel.Slot.Hands && mWelder == (int)SandboxToolWheel.Slot.Welder && mWrench == (int)SandboxToolWheel.Slot.Wrench && mLinker == (int)SandboxToolWheel.Slot.Linker;
        Line($"tool wheel picks: east {east}, north {north}, west {west}, south {south} (hands); marked on opening: {mStart}, {mWelder}, {mWrench}, {mLinker}");
        if (!picks)
            Fail("tool wheel: a hold-flick-release didn't take out the tool in that direction");
        if (!marks)
            Fail("tool wheel: the current tool isn't marked when the wheel opens");

        // A cancel in the dead zone, a tool still to come, and the number keys' favourites unchanged.
        g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Welder);
        var cancel = Flick(new Vector2(8, -6), out var hC, out _);
        var later = Flick(new Vector2(-30, 30), out var hL, out _);
        var laterSaid = g.SandboxNotice;
        Line($"tool wheel: let go in the middle kept the {cancel} (hovered {hC}); the south-west ({(hL >= 0 ? w.Items[hL].Label : "?")}) kept the {later} and said \"{laterSaid}\"");
        // The physics gun on the north-east (where the hoist was).
        var phys = Flick(new Vector2(30, -30), out var hP, out _);
        Line($"tool wheel: the north-east ({(hP >= 0 ? w.Items[hP].Label : "?")}) took out the {phys}");
        if (phys != ConstructTool.PhysicsGun || hP != (int)SandboxToolWheel.Slot.PhysicsGun)
            Fail("tool wheel: the north-east flick didn't take out the physics gun");
        g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Welder);
        Run(g, ref now, 0.05);
        if (cancel != ConstructTool.Welder || hC != -1)
            Fail("tool wheel: letting go in the dead zone didn't cancel");
        if (later != ConstructTool.Welder || hL != (int)SandboxToolWheel.Slot.Scanner || !laterSaid.Contains("coming later") || w.Items[hL].Enabled)
            Fail("tool wheel: a tool still to come was taken out, or nothing said why not");

        // Closing without choosing: a menu (or the parts menu) over it, death; not opening in a seat or while the key stays held.
        wheel.Step(g, true, Vector2.Zero, false);
        wheel.Step(g, true, new Vector2(0, 40), false);
        var openBefore = w.IsOpen;
        wheel.Step(g, true, Vector2.Zero, true); // (a menu opened)
        var menuClosed = !w.IsOpen;
        wheel.Step(g, true, Vector2.Zero, false); // (the key still held after the menu: it doesn't reopen)
        var noReopen = !w.IsOpen;
        wheel.Step(g, false, Vector2.Zero, false);
        var keptMenu = g.SandboxHands.Hand(me.Id).Tool == ConstructTool.Welder;
        wheel.Step(g, true, Vector2.Zero, false);
        wheel.Step(g, true, new Vector2(0, 40), false);
        me.Alive = false;
        wheel.Step(g, true, Vector2.Zero, false);
        var deathClosed = !w.IsOpen;
        me.Alive = true;
        wheel.Step(g, false, Vector2.Zero, false);
        var keptDeath = g.SandboxHands.Hand(me.Id).Tool == ConstructTool.Welder;
        ref var hand = ref g.SandboxHands.Hand(me.Id);
        hand.SeatBody = 999; // (sitting: as far as the wheel can tell)
        wheel.Step(g, true, Vector2.Zero, false);
        var seatOpen = w.IsOpen;
        var seatSaid = g.SandboxNotice;
        wheel.Step(g, false, Vector2.Zero, false);
        hand.SeatBody = 0;
        Line($"tool wheel closing: open {openBefore}, a menu closed it {menuClosed} and the held key didn't reopen it {noReopen}, tool kept {keptMenu}; death closed it {deathClosed}, kept {keptDeath}; "
             + $"in a seat it opened {seatOpen} (\"{seatSaid}\")");
        if (!openBefore || !menuClosed || !noReopen || !keptMenu || !deathClosed || !keptDeath)
            Fail("tool wheel: a menu or death didn't close the wheel without choosing");
        if (seatOpen || !seatSaid.Contains("seat"))
            Fail("tool wheel: the wheel opened in a seat");

        wheel.SetOptions(SandboxToolWheel.Slot.Linker, new SandboxLinkWheel()); // (the outer ring drawn too, with no allocation)
        SandboxToolWheelDrawn(g, wheel);
        SandboxToolWheelOverWire(yard);
        SandboxWheelCursorContain();
    }

    /// <summary>
    /// The wheel's virtual cursor and its containment per tier (board item dev-wheel-cursor-contain-wc01): the paint tool's 15
    /// options form arcs centred on its wedge, well under 360 degrees a row; out through the wedge and sweeping sideways stays
    /// clamped to that arc (no other tool's options, the wedge never changes); back inward releases to tier 1 (with a small
    /// hysteresis) and the centre cancels; the third tier (Recent colours) is entered through its option and clamps likewise;
    /// the cursor dot position is reported and grows outward; moving it allocates nothing.
    /// </summary>
    private static void SandboxWheelCursorContain()
    {
        const int Paint = (int)SandboxToolWheel.Slot.Paint;
        const int N = (int)SandboxToolWheel.Slot.COUNT;
        const int Recent = (int)SandboxPaintWheel.Option.Recent;
        var wheel = new SandboxToolWheel();
        wheel.SetOptions(SandboxToolWheel.Slot.Paint, new SandboxPaintWheel());
        var m = wheel.Menu;
        var opts = m.Items[Paint].Options!;
        var bad = new List<string>();
        static Vector2 Along(float radians, float r) => new(MathF.Sin(radians) * r, -MathF.Cos(radians) * r);
        static float Deg(float r) => r * 180f / MathF.PI;
        static float Turn(Vector2 p, float centre)
        {
            var d = MathF.Atan2(p.X, -p.Y) - centre;
            d %= MathF.Tau;
            return d > MathF.PI ? d - MathF.Tau : d < -MathF.PI ? d + MathF.Tau : d;
        }
        void To(float angle, float r) => m.Move(Along(angle, r) - m.Pointer);
        var centre = RadialMenu.SegmentAngle(Paint, N);
        var rows = RadialMenu.OptionRows(opts.Length);
        var per = RadialMenu.RowSize(opts.Length);

        // The options are arcs centred on the wedge, each well under a full circle.
        var widest = 0f;
        for (var row = 0; row < rows; row++)
        {
            var c = RadialMenu.RowCount(row, opts.Length);
            var span = Deg(c * RadialMenu.OptionStepMax);
            widest = MathF.Max(widest, span);
            var mid = (RadialMenu.OptionAngle(row * per, Paint, N, opts.Length) + RadialMenu.OptionAngle(row * per + c - 1, Paint, N, opts.Length)) * 0.5f;
            if (MathF.Abs(mid - centre) > 1e-4f)
                bad.Add($"paint row {row} isn't centred on the paint wedge");
        }
        if (opts.Length != (int)SandboxPaintWheel.Option.COUNT || rows < 2 || widest > 220f)
            bad.Add($"paint options: {opts.Length} in {rows} rows, widest arc {widest:0} degrees");

        // Out through the wedge: tier 2, the middle option of the first row; the tool stays highlighted.
        m.Open();
        To(centre, 50f);
        var tier1 = m.Ring == Paint && m.Hovered == Paint && !m.InOptions && m.HoveredOption == -1;
        To(centre, m.RowDistance(0));
        var mid0 = m.InOptions && m.Hovered == Paint && m.HoveredOption == per / 2;
        // Sweeping sideways, far past either end of the arc: held at the arc's end, still the paint tool's options.
        var held = new List<string>();
        foreach (var sweep in new[] { 2.9f, -2.9f, 1.4f, -1.4f })
        {
            To(centre + sweep, m.RowDistance(0));
            var last = RadialMenu.RowCount(0, opts.Length) - 1;
            var expect = sweep > 0 ? last : 0;
            var halfArc = RadialMenu.RowCount(0, opts.Length) * RadialMenu.OptionStepMax * 0.5f;
            if (m.Hovered != Paint || m.HoveredOption != expect || MathF.Abs(Turn(m.Pointer, centre)) > halfArc + 1e-3f)
                held.Add($"{Deg(sweep):0}: hovered {m.Hovered}/{m.HoveredOption} (wanted {Paint}/{expect}), {Deg(Turn(m.Pointer, centre)):0.#} deg off");
        }
        // Rows further out: the second and third row's options, in the same arc.
        var rowsOk = true;
        for (var row = 1; row < rows; row++)
        {
            To(centre, m.RowDistance(row));
            rowsOk &= m.HoveredOption >= row * per && m.HoveredOption < (row + 1) * per && m.Hovered == Paint;
        }
        // The cursor dot: reported, along the pointer, and further out for each tier.
        var dot0 = m.CursorOffset(1f);
        var dotOk = dot0.Length() > RadialMenu.OuterEdge + 2 * (RadialMenu.RingDepth + RadialMenu.RingGap) - 1f && Vector2.Dot(Vector2.Normalize(dot0), Vector2.Normalize(m.Pointer)) > 0.999f
                    && MathF.Abs(m.CursorOffset(2f).Length() - 2f * dot0.Length()) < 0.01f;
        var prevDot = -1f;
        var growing = true;
        for (var l = 0f; l <= 190f; l += 5f)
        {
            var d = m.CursorHud(l);
            growing &= d >= prevDot;
            prevDot = d;
        }
        // Back inward: the hysteresis keeps the clamp a little, then the tier is released to the tool's wedge; the centre cancels.
        To(centre + 1.0f, m.Reach + m.OuterGap + 4f);
        To(centre + 1.0f, m.Reach + m.OuterGap - 3f);
        var keptHyst = m.InOptions && m.Hovered == Paint && m.HoveredOption >= 0;
        To(centre, m.Reach + m.OuterGap - RadialMenu.TierHyst - 2f);
        var released = !m.InOptions && m.Hovered == Paint && m.HoveredOption == -1 && m.Ring == Paint;
        To(centre, 8f);
        var cancels = m.Hovered == -1;
        if (!tier1 || !mid0 || held.Count > 0 || !rowsOk || !dotOk || !growing || !keptHyst || !released || !cancels)
            bad.Add($"tiers: wedge {tier1}, middle option {mid0}, sweeps [{string.Join("; ", held)}], rows {rowsOk}, dot {dotOk}, dot grows {growing}, hysteresis {keptHyst}, released {released}, centre cancels {cancels}");

        // Into tier 2 only through the tool's own wedge: pointing elsewhere is held at the ring's inner edge.
        m.Close();
        m.Open();
        To(centre + 0.3f, 80f);
        To(0f, m.Reach + m.OuterGap + 30f); // (straight up: the wrench's wedge)
        var heldOut = !m.InOptions && m.Hovered == (int)SandboxToolWheel.Slot.Wrench && m.Pointer.Length() < m.Reach + m.OuterGap;
        m.Close();
        m.Open();
        To(centre + 0.3f, 80f); // (near the wedge's edge)
        To(centre + 0.3f, m.RowDistance(0));
        var throughWedge = m.InOptions && m.Hovered == Paint;
        if (!heldOut || !throughWedge)
            bad.Add($"entering tier 2: held outside the wedge {heldOut}, through it near its edge {throughWedge}");

        // The third tier: Recent colours is on the last row; the arc is entered through its option, then clamped.
        var lastRow = RadialMenu.RowOf(Recent, opts.Length);
        var optA = RadialMenu.OptionAngle(Recent, Paint, N, opts.Length);
        var edge = m.RingEdge(rows);
        m.Close();
        m.Open();
        To(centre, 50f);
        To(centre, m.RowDistance(0)); // (out through the wedge)
        To(optA, m.RowDistance(lastRow));
        var onRecent = m.HoveredOption == Recent && m.SubRing == Recent;
        To(optA + 0.9f, edge + 4f); // (beside the option: not through it)
        var notThrough = !m.InSubOptions && m.Pointer.Length() < edge;
        To(optA, edge + RadialMenu.SubGap + 12f);
        var inTier3 = m.InSubOptions && m.HoveredOption == Recent;
        var subs = opts[Recent].Options!;
        var subHalf = RadialMenu.OptionStep(subs.Length, RadialMenu.SubStepMax) * subs.Length * 0.5f;
        var subHeld = true;
        foreach (var sweep in new[] { 3.0f, -3.0f })
        {
            To(optA + sweep, edge + RadialMenu.SubGap + 12f);
            subHeld &= m.InSubOptions && m.HoveredOption == Recent && m.Hovered == Paint && m.HoveredSub == (sweep > 0 ? subs.Length - 1 : 0)
                       && MathF.Abs(Turn(m.Pointer, optA)) <= subHalf + 1e-3f;
        }
        To(optA, m.RowDistance(lastRow));
        var tier3Released = !m.InSubOptions && m.HoveredSub == -1 && m.HoveredOption == Recent;
        if (!onRecent || !notThrough || !inTier3 || !subHeld || !tier3Released)
            bad.Add($"third tier: on Recent {onRecent}, not entered beside it {notThrough}, entered {inTier3}, sweeps held {subHeld}, released inward {tier3Released}");
        m.Close();

        // Moving the cursor allocates nothing.
        m.Open();
        for (var i = 0; i < 400; i++)
        {
            To(centre + (i % 40) * 0.05f - 1f, 40f + i % 150);
            _ = m.CursorOffset(1f);
        }
        var b0 = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2000; i++)
        {
            To(centre + (i % 40) * 0.05f - 1f, 40f + i % 150);
            _ = m.CursorOffset(1.5f);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - b0;
        m.Close();
        if (allocated > 0)
            bad.Add($"moving the cursor allocates {allocated} B per 2000 moves");
        Line($"tool wheel cursor: paint options {opts.Length} in {rows} rows (widest arc {widest:0} degrees), contained per tier: {bad.Count} problems; cursor dot at {dot0.X:0},{dot0.Y:0}; {allocated} B per 2000 moves");
        if (bad.Count > 0)
            Fail($"tool wheel cursor: {string.Join("; ", bad.Take(3))}");
    }

    /// <summary>The wheel headless: no ImGui error, and nothing allocated per frame while open and moving (the step included).</summary>
    private static void SandboxToolWheelDrawn(Game g, SandboxToolWheel wheel)
    {
        var ui = new Ui();
        ui.InitHeadless(new float2(1600, 900));
        string? error = null;
        var frames = 0;
        var angle = 0f;
        void Frames(int n)
        {
            for (var i = 0; i < n && error == null; i++)
            {
                try
                {
                    angle += 0.21f; // (the pointer goes round: every segment highlighted in turn, the dead zone too)
                    var d = new Vector2(MathF.Sin(angle), -MathF.Cos(angle)) * (i % 17 == 0 ? -60f : 12f);
                    wheel.Step(g, true, d, false);
                    ui.BeginFrame();
                    wheel.Draw(ui.Scale);
                    ui.EndFrame();
                    frames++;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }
        }
        try
        {
            wheel.Step(g, false, Vector2.Zero, false);
            wheel.Step(g, true, Vector2.Zero, false);
            Frames(40);
            Thread.Sleep(200); // (the tiered JIT)
            Frames(20);
            var least = long.MaxValue;
            for (var round = 0; round < 3; round++)
            {
                var b0 = GC.GetAllocatedBytesForCurrentThread();
                Frames(60);
                least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - b0);
            }
            // The third tier showing with a sub-option under the pointer: drawn and held still, it allocates nothing either.
            var m = wheel.Menu;
            wheel.Step(g, false, Vector2.Zero, false);
            wheel.Step(g, true, Vector2.Zero, false);
            // (the linking tool's pipe has no third tier since there is one pipe: a test set on the welder slot has one)
            var linkI = (int)SandboxToolWheel.Slot.Welder;
            var pipeI = 1;
            wheel.SetOptions(SandboxToolWheel.Slot.Welder, new TestTieredOptions());
            var optN = m.Items[linkI].Options?.Length ?? 0;
            var centreA = RadialMenu.OptionAngle(pipeI, linkI, (int)SandboxToolWheel.Slot.COUNT, optN);
            var subA = RadialMenu.ArcAngle(1, centreA, 5, RadialMenu.SubStepMax);
            var toward = RadialMenu.SegmentAngle(linkI, (int)SandboxToolWheel.Slot.COUNT);
            wheel.Step(g, true, new Vector2(MathF.Sin(toward), -MathF.Cos(toward)) * 45f, false);
            wheel.Step(g, true, new Vector2(MathF.Sin(centreA), -MathF.Cos(centreA)) * 120f - m.Pointer, false);
            wheel.Step(g, true, new Vector2(MathF.Sin(centreA), -MathF.Cos(centreA)) * (m.Reach + m.OuterDepth) - m.Pointer, false); // (the third tier is entered through its option)
            wheel.Step(g, true, new Vector2(MathF.Sin(subA), -MathF.Cos(subA)) * (m.Reach + m.OuterDepth + RadialMenu.SubGap + 14f) - m.Pointer, false);
            var tierShown = m.SubRing == pipeI && m.HoveredSub == 1;
            var tierLeast = long.MaxValue;
            for (var round = 0; round < 3; round++)
            {
                var b0 = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 60 && error == null; i++)
                {
                    try
                    {
                        wheel.Step(g, true, Vector2.Zero, false);
                        ui.BeginFrame();
                        wheel.Draw(ui.Scale);
                        ui.EndFrame();
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                    }
                }
                tierLeast = Math.Min(tierLeast, GC.GetAllocatedBytesForCurrentThread() - b0);
            }
            Line($"tool wheel third tier drawn: showing with a sub-option hovered {tierShown}, ImGui error: {error ?? "none"}; allocates {tierLeast} B per 60 frames");
            if (!tierShown)
                Fail("tool wheel: the third tier didn't show with a sub-option hovered");
            if (tierLeast > 0)
                Fail($"tool wheel: the third tier allocates {tierLeast} B per 60 frames (should be nothing)");
            var stillOpen = wheel.Menu.IsOpen;
            wheel.Menu.Close();
            ui.BeginFrame();
            wheel.Draw(ui.Scale); // (closed: nothing drawn)
            ui.EndFrame();
            Line($"tool wheel drawn: {frames} headless frames, ImGui error: {error ?? "none"}; open frames allocate {least} B per 60 (least of three); stayed open {stillOpen}");
            if (error != null)
                Fail($"tool wheel: drawing threw in ImGui: {error}");
            if (least > 0)
                Fail($"tool wheel: the open wheel allocates {least} B per 60 frames (should be nothing)");
            if (!stillOpen)
                Fail("tool wheel: the wheel closed by itself while held");
        }
        finally
        {
            ui.DisposeHeadless();
        }
    }

    /// <summary>A real client: a wheel pick goes through the number keys' request and reaches the server.</summary>
    private static void SandboxToolWheelOverWire(Map yard)
    {
        const ushort port = 26347;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now);
        Pump(server, [a], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Fail("tool wheel over the wire: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        Pump(server, [a], 0.4, ref now, clock);
        var wheel = new SandboxToolWheel();
        wheel.Step(a, true, Vector2.Zero, false);
        wheel.Step(a, true, new Vector2(-40, 0), false); // (west: the linking tool)
        wheel.Step(a, false, Vector2.Zero, false);
        Pump(server, [a], 0.4, ref now, clock);
        var onServer = server.SandboxHands.Hand(sp.Id).Tool;
        var onClient = a.SandboxHands.Hand(a.LocalId).Tool;
        Line($"tool wheel over the wire: the client alive {a.Local is { Alive: true }}; its pick: the server has the {onServer}, the client the {onClient}");
        if (onServer != ConstructTool.Linker || onClient != ConstructTool.Linker)
            Fail("tool wheel: a client's wheel pick didn't reach the server");
    }
}
