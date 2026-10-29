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
        if (old[(int)GameAction.ToolWheel] != middle || moved.Count != 1 || taken[(int)GameAction.Melee] != middle || taken[(int)GameAction.ToolWheel] != Keybinds.Unbound)
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
        var later = Flick(new Vector2(30, -30), out var hL, out _);
        var laterSaid = g.SandboxNotice;
        Line($"tool wheel: let go in the middle kept the {cancel} (hovered {hC}); the north-east ({(hL >= 0 ? w.Items[hL].Label : "?")}) kept the {later} and said \"{laterSaid}\"");
        if (cancel != ConstructTool.Welder || hC != -1)
            Fail("tool wheel: letting go in the dead zone didn't cancel");
        if (later != ConstructTool.Welder || hL != (int)SandboxToolWheel.Slot.Hoist || !laterSaid.Contains("coming later") || w.Items[hL].Enabled)
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

        SandboxToolWheelDrawn(g, wheel);
        SandboxToolWheelOverWire(yard);
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
