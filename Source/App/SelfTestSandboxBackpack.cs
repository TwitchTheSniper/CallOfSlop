using System.Diagnostics;
using System.Numerics;
using Brutal.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>A box sink that counts boxes and remembers their centres (the backpack's drawing, headless).</summary>
    private struct PackBoxCount : IToolBoxOut
    {
        public int Boxes;
        public Vector3 Sum;

        public void Box(in Matrix4x4 model, Vector3 color, int mode, ushort shape = 0)
        {
            Boxes++;
            Sum += model.Translation;
        }
    }

    /// <summary>
    /// "Sandbox backpack" (the developer's answer on carrying, 2026-10-30; docs/sandbox/CONSTRUCTS.md "The backpack"): the
    /// Backpack layout (eight slots: small pouches, a big bag, side pouches, a hip pouch; 40 kg at most) and its keys (H
    /// and J, unshared; a binds version 10 file gets them); in an offline Sandbox game stowing a held lamp puts it in the
    /// smallest slot it fits, a small fuel tank in the big bag, an engine is refused as too heavy and a wheel as too big (both
    /// stay in the hands), drawing takes the next part round the slots into empty hands (refused with full hands, or an
    /// empty pack), the pack is drawn on the player's back (its boxes behind them) with the parts in it, the HUD line and
    /// the strip of slot outlines show it (headless, no ImGui error), a death drops its contents as loose parts and
    /// empties it; nothing allocates per frame (the message, the drawing, the strip); and over a loopback a client's
    /// stow reaches the server and the other client sees the pack, and a late joiner sees it too.
    /// </summary>
    private static void CheckSandboxBackpack()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox == null)
        {
            Fail($"sandbox backpack: the {ConstructYardMap} map (with its sandbox section) is missing");
            return;
        }
        if (ConstructEdits.Backpack is not { } bp)
        {
            Fail("sandbox backpack: the Backpack part didn't load (Constructs/Parts/Storage/Backpack.json)");
            return;
        }
        var classes = bp.Slots.Select(s => ConstructStorage.SizeClass(s)).ToList();
        var kd = Keybinds.Defaults;
        var old = (int[])kd.Clone();
        old[(int)GameAction.Stow] = old[(int)GameAction.DrawPack] = Keybinds.Unbound;
        Keybinds.Migrate(old, 10);
        Line($"backpack: {bp.Slots.Length} slots ({string.Join(", ", classes)}), at most {bp.StoreMaxMass:0} kg; keys {Keybinds.Name(kd[(int)GameAction.Stow])} / {Keybinds.Name(kd[(int)GameAction.DrawPack])}, "
             + $"a binds version 10 file gets them {old[(int)GameAction.Stow] == kd[(int)GameAction.Stow] && old[(int)GameAction.DrawPack] == kd[(int)GameAction.DrawPack]}");
        if (bp.Slots.Length != 8 || bp.StoreMaxMass <= 0 || classes.Distinct().Count() < 3)
            Fail("sandbox backpack: the Backpack isn't eight slots of different sizes with a weight limit");
        if (kd[(int)GameAction.Stow] == Keybinds.Unbound || kd.Count(k => k == kd[(int)GameAction.Stow]) != 1 || kd.Count(k => k == kd[(int)GameAction.DrawPack]) != 1
            || old[(int)GameAction.Stow] != kd[(int)GameAction.Stow])
            Fail("sandbox backpack: the stow and draw keys are unbound, shared, or not given to an older binds file");
        BackpackOffline(yard);
        BackpackOverWire(yard);
    }

    private static void BackpackOffline(Map yard)
    {
        yard.RestoreProps();
        World.SetMap(yard);
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        Run(g, ref now, Game.CountdownSeconds + 0.3);
        g.RequestRespawn();
        Run(g, ref now, 1.0);
        if (g.Local is not { Alive: true } me)
        {
            Fail("sandbox backpack: no local player in the Sandbox game");
            return;
        }
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        var bp = ConstructEdits.Backpack!;
        ref var hand = ref g.SandboxHands.Hand(me.Id);
        var pack = g.SandboxHands.Pack(me.Id);
        var lamp = ConstructParts.Need("Lamp");
        var susp = ConstructParts.Need("Small Fuel Tank"); // (0.4 by 0.3 by 0.4 m: only the big bag takes it; the rescaled suspension is a thin plate now)
        var engine = ConstructParts.Need("Engine");
        var wheel = ConstructParts.Need("Wheel");
        int SlotOf(ConstructPartDef d)
        {
            for (var k = 0; k < pack.SlotCount; k++)
                if (pack.Def[k] == d.Id)
                    return k;
            return -1;
        }

        hand.Held = lamp.Id;
        g.SandboxSend(CActKind.Stow);
        var lampSlot = SlotOf(lamp);
        var lampRoom = lampSlot >= 0 ? bp.Slots[lampSlot].Size : Vector3.Zero;
        var smallest = MathF.Max(lampRoom.X, MathF.Max(lampRoom.Y, lampRoom.Z)) <= ConstructStorage.SmallSize + 0.01f;
        Want(lampSlot >= 0 && hand.Held == ConstructParts.None && smallest, $"a stowed lamp goes into a small slot (slot {lampSlot}) and leaves the hands");
        hand.Held = susp.Id;
        g.SandboxSend(CActKind.Stow);
        var suspSlot = SlotOf(susp);
        Want(suspSlot >= 0 && bp.Slots[suspSlot].Label == "big bag", $"a small fuel tank goes into the big bag (slot {suspSlot})");
        hand.Held = engine.Id;
        g.SandboxSend(CActKind.Stow);
        var heavy = g.SandboxNotice;
        Want(hand.Held == engine.Id && heavy.Contains("too heavy"), $"an engine is refused as too heavy (\"{heavy}\")");
        hand.Held = wheel.Id;
        g.SandboxSend(CActKind.Stow);
        var big = g.SandboxNotice;
        Want(hand.Held == wheel.Id && big.Contains("too big"), $"a wheel is refused as too big (\"{big}\")");
        g.SandboxSend(CActKind.Draw);
        var fullHands = g.SandboxNotice;
        Want(hand.Held == wheel.Id && fullHands.Contains("full"), $"drawing with full hands is refused (\"{fullHands}\")");
        hand.Held = ConstructParts.None;
        g.SandboxSend(CActKind.Draw);
        var first = hand.Held;
        hand.Held = ConstructParts.None;
        g.SandboxSend(CActKind.Draw);
        var second = hand.Held;
        hand.Held = ConstructParts.None;
        g.SandboxSend(CActKind.Draw);
        var none = g.SandboxNotice;
        Want(new[] { first, second }.Order().SequenceEqual(new[] { lamp.Id, susp.Id }.Order()) && pack.Items == 0 && none.Contains("empty"),
            $"drawing takes both parts out in turn ({ConstructParts.Get(first)?.Name}, {ConstructParts.Get(second)?.Name}), then \"{none}\"");

        // Drawn on the back with what it holds; the HUD line and the strip.
        hand.Held = lamp.Id;
        g.SandboxSend(CActKind.Stow);
        hand.Held = susp.Id;
        g.SandboxSend(CActKind.Stow);
        ToolRender.Hands = g.SandboxHands;
        var drawn = new PackBoxCount();
        var worn = BackpackRender.Wears(me, out var worn1);
        var frame = BackpackRender.OnBack(me) * LegAnimator.Animate(me, now).Upper * Matrix4x4.CreateRotationY(-me.RenderYaw) * Matrix4x4.CreateTranslation(me.RenderPosition);
        if (worn)
            BackpackRender.Emit(ref drawn, worn1!, frame);
        var expect = bp.Boxes.Length + lamp.Boxes.Length + susp.Boxes.Length;
        var centre = drawn.Boxes > 0 ? drawn.Sum / drawn.Boxes : Vector3.Zero;
        var behind = Vector3.Dot(centre - me.RenderPosition, World.FlatForward(me.RenderYaw)) < -0.1f;
        Want(worn && drawn.Boxes == expect && behind, $"the pack is drawn on the back with its parts ({drawn.Boxes} boxes of {expect}; behind the player {behind})");
        var lines = new List<HudLine>();
        var white = new Vector4(1);
        g.SandboxHud(me, lines, white, white, white, white, white);
        var hudLine = lines.FirstOrDefault(l => l.Text.StartsWith("Backpack")).Text ?? "";
        Want(hudLine.Contains("2/8") && hudLine.Contains(lamp.Name), $"the HUD line shows the pack (\"{hudLine}\")");
        var strip = BackpackStrip(g, out var stripError, out var stripAlloc);
        Want(strip == 8 && stripError == null && stripAlloc == 0, $"the strip draws its 8 slots headless ({strip}, error {stripError ?? "none"}, {stripAlloc} B per 60 frames)");

        // No allocation a frame or tick: the pack's message and its drawing (a stow or a draw logs one line, like every edit).
        var pw = new PacketWriter(512);
        var allocated = long.MaxValue;
        for (var round = 0; round < 4; round++)
        {
            var b0 = GC.GetAllocatedBytesForCurrentThread();
            for (var k = 0; k < 120; k++)
            {
                ConstructNet.WritePack(pw, me.Id, pack);
                var o = new PackBoxCount();
                BackpackRender.Emit(ref o, pack, frame);
            }
            if (round > 0)
                allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - b0);
        }
        Want(allocated == 0, $"the pack message and its drawing allocate {allocated} B per 120 (should be none)");

        // A death drops the contents as loose parts.
        var before = g.Constructs.Bodies.Select(b => b.Id).ToHashSet();
        var at = me.Position;
        g.SelfTestKill(me, me, Game.MeleeKillId, now);
        Run(g, ref now, 1.0);
        var dropped = g.Constructs.Bodies.Where(b => !before.Contains(b.Id) && b.Loose && Vector3.Distance(b.WorldCom, at) < 3f).Select(b => b.DefOf(0).Name).OrderBy(n => n).ToList();
        Want(dropped.Count == 2 && dropped.Contains(lamp.Name) && dropped.Contains(susp.Name) && pack.Items == 0, $"a death drops the lamp and small fuel tank near the body ({string.Join(", ", dropped)}) and empties the pack");
        ToolRender.Hands = null;
        Line($"backpack offline: lamp in slot {lampSlot}, small fuel tank in slot {suspSlot}; engine \"{heavy}\"; wheel \"{big}\"; drew {ConstructParts.Get(first)?.Name}, {ConstructParts.Get(second)?.Name}; "
             + $"drawn {drawn.Boxes} boxes behind {behind}; HUD \"{hudLine}\"; strip {strip} slots; {allocated} B allocated; dropped on death [{string.Join(", ", dropped)}]");
        if (problems.Count > 0)
            Fail($"sandbox backpack: offline: {string.Join("; ", problems)}");
    }

    /// <summary>The backpack strip headless: slots drawn, any ImGui error, and what 60 frames allocate (least of three).</summary>
    private static int BackpackStrip(Game g, out string? error, out long allocated)
    {
        var ui = new Ui();
        ui.InitHeadless(new float2(1600, 900));
        error = null;
        allocated = long.MaxValue;
        var drawn = 0;
        try
        {
            void Frames(int n)
            {
                for (var i = 0; i < n; i++)
                {
                    ui.BeginFrame();
                    ui.DrawSandboxPack(g, new float2(0, 0), new float2(1600, 900));
                    ui.EndFrame();
                    drawn = ui.PackSlotsDrawn;
                }
            }
            Frames(30);
            for (var round = 0; round < 3; round++)
            {
                var b0 = GC.GetAllocatedBytesForCurrentThread();
                Frames(60);
                allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - b0);
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            ui.DisposeHeadless();
        }
        return drawn;
    }

    private static void BackpackOverWire(Map yard)
    {
        const ushort port = 26351;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a, b], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a, b], 0.25, ref now);
        Pump(server, [a, b], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue || b.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var pa))
        {
            Fail("sandbox backpack over the wire: the clients didn't join");
            return;
        }
        if (!pa.Alive)
            server.SelfTestRespawn(pa, now);
        Pump(server, [a, b], 0.3, ref now, clock);
        var battery = ConstructParts.Need("Battery");
        server.SandboxHands.Hand(pa.Id).Held = battery.Id;
        Pump(server, [a, b], 0.3, ref now, clock);
        a.SandboxSend(CActKind.Stow);
        Pump(server, [a, b], 0.4, ref now, clock);
        bool Has(Game g) => g.SandboxHands.Pack(pa.Id).Def.Take(g.SandboxHands.Pack(pa.Id).SlotCount).Contains(battery.Id);
        var onServer = Has(server);
        var onOther = Has(b);
        var onOwn = Has(a);
        using var c = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Charlie", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a, b, c], 1.0, ref now, clock);
        var late = Has(c);
        Line($"backpack over the wire: a client's stowed battery on the server {onServer}, in its own copy {onOwn}, on the other client {onOther}, on a late joiner {late}");
        if (!onServer || !onOwn || !onOther || !late)
            Fail("sandbox backpack over the wire: a client's backpack doesn't reach the server and every client");
    }
}
