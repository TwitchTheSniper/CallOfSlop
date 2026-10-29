using System.Numerics;
using System.Text.Json;
using Brutal.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Sandbox parts menu": the debug parts menu (UiSandboxParts.cs; docs/sandbox/CONSTRUCTS.md). The map flag reads (and
    /// a bad one is refused); in an offline Sandbox game on the yard the menu's request hands the chosen part (by id) to
    /// the player, an unknown id is refused, the rack still works, a blueprint spawns in front of the player, the map flag
    /// turns both off; the window draws headless (the full list, a search, Up/Down/Enter state, a pick that takes out the
    /// part's tool and closes it) without ImGui errors and with no allocation per frame while open; a real client's
    /// requests go over the wire; and outside Sandbox a blueprint spawn is refused.
    /// </summary>
    private static void CheckSandboxPartsMenu()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"parts menu: the {ConstructYardMap} map is missing");
            return;
        }
        // The map flag.
        bool Flag(string json) => SandboxMap.Read(JsonDocument.Parse(json).RootElement, "test").PartsMenu;
        var byDefault = Flag("{\"rack\": [0, 0]}");
        var off = !Flag("{\"rack\": [0, 0], \"partsMenu\": false}");
        string bad;
        try
        {
            Flag("{\"partsMenu\": 1}");
            bad = "accepted";
        }
        catch (MapFileException ex)
        {
            bad = ex.Message;
        }
        Line($"parts menu flag: on by default {byDefault}, \"partsMenu\": false turns it off {off}, a number refused: \"{bad}\"");
        if (!byDefault || !off || !bad.Contains("partsMenu"))
            Fail("parts menu: the map's \"partsMenu\" flag doesn't read right");

        yard.RestoreProps();
        World.SetMap(yard);
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        Run(g, ref now, Game.CountdownSeconds + 0.3);
        g.RequestRespawn();
        Run(g, ref now, 1.0);
        if (g.Local is not { Alive: true } me || World.Map.Sandbox is not { } sb)
        {
            Fail("parts menu: no local player in the Sandbox game");
            return;
        }
        ref var hand = ref g.SandboxHands.Hand(me.Id);

        // The request: any part by id, anywhere; an unknown id refused; the rack unchanged.
        var piston = ConstructParts.Need("Piston");
        me.Teleport(ConstructWorld.Ground(World.Query, sb.RackX + 12, sb.RackZ - 12));
        Run(g, ref now, 0.2);
        g.SandboxTakePart(piston.Id);
        var gotPiston = hand.Held == piston.Id;
        g.SandboxTakePart(240);
        var unknown = g.SandboxNotice;
        var keptPiston = hand.Held == piston.Id;
        g.SandboxSend(CActKind.Rack); // (far from the rack: refused as before)
        var farRack = g.SandboxNotice;
        me.Teleport(ConstructWorld.Ground(World.Query, sb.RackX + 1, sb.RackZ));
        Run(g, ref now, 0.2);
        g.SandboxSend(CActKind.Rack);
        var fromRack = ConstructParts.Get(hand.Held)?.Name ?? "nothing";
        var rackOk = hand.Held != piston.Id && hand.Held != ConstructParts.None;
        Line($"parts menu request: a Piston by id in hand {gotPiston}; id 240 refused (\"{unknown}\"), hands kept {keptPiston}; the rack away from it: \"{farRack}\"; at it: {fromRack}");
        if (!gotPiston || !unknown.Contains("no such part") || !keptPiston)
            Fail("parts menu: the request didn't hand over the part by id, or an unknown id wasn't refused");
        if (!farRack.Contains("rack") || !rackOk)
            Fail("parts menu: the parts rack stopped working as before");

        // A blueprint in front of the player.
        me.Teleport(ConstructWorld.Ground(World.Query, sb.RackX + 12, sb.RackZ - 12));
        Run(g, ref now, 0.2);
        var turret = IndexOfBlueprint("Turret");
        var before = g.Constructs.Bodies.Count;
        g.SandboxSend(CActKind.Blueprint, part: (byte)turret);
        var spawnWhy = g.SandboxNotice;
        var spawned = g.Constructs.Bodies.Count == before + 1 ? g.Constructs.Bodies[^1] : null;
        Run(g, ref now, 1.0);
        var ahead = spawned == null ? 0 : Vector3.Dot(spawned.WorldCom - me.Position, World.FlatForward(me.Yaw));
        var apart = spawned == null ? 0 : Vector2.Distance(new Vector2(spawned.WorldCom.X, spawned.WorldCom.Z), new Vector2(me.Position.X, me.Position.Z));
        var parts = spawned?.PartCount ?? 0;
        g.SandboxSend(CActKind.Blueprint, part: 200);
        var badBp = g.SandboxNotice;
        Line($"parts menu blueprint: the Turret spawned {(spawned != null ? $"as construct {spawned.Id} with {parts} parts, {ahead:0.0} m ahead and {apart:0.0} m away" : $"no (\"{spawnWhy}\")")}; index 200: \"{badBp}\"");
        if (spawned == null || parts < 4 || ahead < 2 || apart > 12 || !badBp.Contains("no such blueprint"))
            Fail("parts menu: a blueprint didn't spawn in front of the player (or a bad index wasn't refused)");

        // The map flag turns both off.
        sb.PartsMenu = false;
        g.SandboxTakePart(piston.Id);
        var offPart = g.SandboxNotice;
        var offMenu = g.SandboxPartsMenu;
        sb.PartsMenu = true;
        Line($"parts menu off on the map: the menu offered {offMenu}, a part asked for: \"{offPart}\"");
        if (offMenu || !offPart.Contains("off"))
            Fail("parts menu: the map flag doesn't turn the menu off");

        SandboxPartsWindow(g, ref now);

        SandboxPartsOverWire(yard);

        // Outside Sandbox: refused.
        g.Mode = GameMode.FreeForAll;
        Run(g, ref now, 0.2);
        var outside = g.Local is { } p2 ? g.SandboxSpawnBlueprint(p2, turret) : "no player";
        Line($"parts menu outside Sandbox: offered {g.SandboxPartsMenu}, a blueprint spawn: \"{outside}\"");
        if (g.SandboxPartsMenu || outside == null || !outside.Contains("only in Sandbox"))
            Fail("parts menu: a blueprint spawn wasn't refused outside Sandbox");
    }

    private static int IndexOfBlueprint(string name)
    {
        var all = ConstructBuild.Blueprints;
        for (var i = 0; i < all.Count; i++)
            if (all[i].Name == name)
                return i;
        return -1;
    }

    /// <summary>The window headless: lists every part, a search narrows it, a pick takes out the tool and closes it; no ImGui error and no allocation per frame while open.</summary>
    private static void SandboxPartsWindow(Game g, ref double now)
    {
        var me = g.Local!;
        var ui = new Ui();
        ui.InitHeadless(new float2(1600, 900));
        var settings = new Settings();
        string? error = null;
        var frames = 0;
        void Frames(int n)
        {
            for (var i = 0; i < n && error == null; i++)
            {
                try
                {
                    ui.BeginFrame();
                    ui.DrawSandboxParts(g, settings);
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
            g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.None);
            ui.SandboxPartsKeepOpen = false;
            ui.OpenSandboxParts(focusSearch: false); // (an active text box keeps its own copy of the text: the test types through SetSandboxPartsSearch)
            Frames(3);
            var all = ui.SandboxPartsListed;
            ui.SetSandboxPartsSearch("wheel");
            Frames(2);
            var wheels = ui.SandboxPartsListed;
            var first = ui.SandboxPartsHighlighted;
            ui.SetSandboxPartsSearch("zzzz nothing");
            Frames(2);
            var none = ui.SandboxPartsListed;
            ui.SetSandboxPartsSearch("transmission");
            Frames(2);
            var pick = ui.SandboxPartsHighlighted;
            if (pick != null)
                ui.SandboxPartsPick(g, pick);
            ref var hand = ref g.SandboxHands.Hand(me.Id);
            var picked = pick != null && hand.Held == pick.Id && hand.Tool == pick.Tool && !ui.SandboxPartsOpen && ui.SandboxPartsRecent[0] == pick.Id;
            Line($"parts menu window: {all} of {ConstructParts.All.Count} parts listed; \"wheel\" {wheels} (highlight {first?.Name ?? "none"}), nonsense {none}; "
                 + $"picking the {pick?.Name ?? "?"} put it in hand with the {hand.Tool.ToString().ToLowerInvariant()} out and closed the window: {picked}");
            if (all != ConstructParts.All.Count || wheels < 1 || wheels >= all || none != 0 || !picked)
                Fail("parts menu: the window doesn't list, search or pick parts right");

            // Open, steady frames: nothing allocated (least of three 60-frame rounds after a warm-up).
            ui.SetSandboxPartsSearch("");
            ui.SandboxPartsKeepOpen = true;
            ui.OpenSandboxParts(focusSearch: false);
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
            var stillOpen = ui.SandboxPartsOpen;
            ui.CloseSandboxParts();
            Frames(2); // (closed: nothing drawn, ImGui still balanced)
            Line($"parts menu window: {frames} headless frames, ImGui error: {error ?? "none"}; open steady frames allocate {least} B per 60 (least of three); stayed open {stillOpen}");
            if (error != null)
                Fail($"parts menu: the window threw in ImGui: {error}");
            if (least > 0)
                Fail($"parts menu: the open window allocates {least} B per 60 frames (should be nothing)");
            if (!stillOpen)
                Fail("parts menu: the window closed by itself while kept open");
        }
        finally
        {
            ui.DisposeHeadless();
        }
    }

    /// <summary>A real client: the part by id and a blueprint go over the wire, an unknown id comes back refused.</summary>
    private static void SandboxPartsOverWire(Map yard)
    {
        const ushort port = 26343;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now); // (the countdown on the game clock, not the wall clock)
        Pump(server, [a], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Fail("parts menu over the wire: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        Pump(server, [a], 0.3, ref now, clock);
        var lamp = ConstructParts.Need("Lamp");
        a.SandboxTakePart(lamp.Id);
        Pump(server, [a], 0.4, ref now, clock);
        var got = server.SandboxHands.Hand(sp.Id).Held == lamp.Id;
        var mirrored = a.SandboxHands.Hand(a.LocalId).Held == lamp.Id;
        a.SandboxTakePart(249);
        Pump(server, [a], 0.4, ref now, clock);
        var refused = a.SandboxNotice;
        var before = server.Constructs.Bodies.Count;
        a.SandboxSend(CActKind.Blueprint, part: (byte)IndexOfBlueprint("Gate"));
        Pump(server, [a], 0.6, ref now, clock);
        var spawned = server.Constructs.Bodies.Count == before + 1;
        var copies = a.Constructs.Bodies.Count == server.Constructs.Bodies.Count;
        Line($"parts menu over the wire: a client's Lamp by id reached the server {got} (and its hands {mirrored}); id 249 refused \"{refused}\"; a Gate blueprint spawned {spawned}, mirrored {copies}");
        if (!got || !mirrored || !refused.Contains("no such part") || !spawned || !copies)
            Fail("parts menu: a client's parts menu requests didn't go over the wire right");
    }
}
