using System.Diagnostics;
using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Construct salvage" (milestone 3, part 2; docs/sandbox/CONSTRUCTS.md "Salvage and the inventory"): the scrap
    /// values and the map's flags read; in an offline Sandbox game on the yard a wheel that fell off the buggy as a wreck
    /// is salvaged into the inventory (timed, the hand left empty), the broken suspension gives scrap, someone else's
    /// working gate can't be taken apart until a part of it is shot off; with infinite parts off the parts menu hands out
    /// only what the inventory holds, a fit uses one (the next comes into the hand), your own gate's door goes back into
    /// the inventory, and a counted part in hand survives a death; the HUD shows the counts; a wreck nobody is near goes
    /// after the map's time but not while it is being salvaged or someone stands by; the inventory, the rules and the
    /// debris check allocate nothing a tick; and over a loopback the owner's client sees its counts (another client
    /// doesn't), the owner travels with a construct, and an expired wreck goes on the clients too.
    /// </summary>
    private static void CheckConstructSalvage()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail($"construct salvage: the {ConstructYardMap} map (with its sandbox section) is missing");
            return;
        }
        SalvageData();
        var keepInfinite = sb.InfiniteParts;
        var keepSeconds = sb.DebrisSeconds;
        var keepNear = sb.DebrisNear;
        try
        {
            SalvageOffline(yard, sb);
            sb.InfiniteParts = keepInfinite;
            sb.DebrisSeconds = keepSeconds;
            sb.DebrisNear = keepNear;
            SalvageOverWire(yard, sb);
        }
        finally
        {
            sb.InfiniteParts = keepInfinite;
            sb.DebrisSeconds = keepSeconds;
            sb.DebrisNear = keepNear;
        }
    }

    private static void SalvageData()
    {
        var engine = ConstructParts.Need("Engine");
        var wheel = ConstructParts.Need("Wheel");
        var lamp = ConstructParts.Need("Lamp");
        var formula = SandboxInventory.ScrapFrom(30, "steel") == 6 && SandboxInventory.ScrapFrom(3, "steel") == 1 && SandboxInventory.ScrapFrom(40, "rubber") == 4
                      && SandboxInventory.ScrapFrom(600, "concrete") == 30;
        Line($"salvage scrap: Engine {engine.Mass:0} kg {engine.Material}: {engine.Scrap}; Wheel {wheel.Mass:0} kg {wheel.Material}: {wheel.Scrap}; Lamp {lamp.Mass:0} kg: {lamp.Scrap}; formula right {formula}");
        if (!formula || engine.Scrap != SandboxInventory.ScrapFrom(engine.Mass, engine.Material) || lamp.Scrap < 1)
            Fail("construct salvage: the scrap a wrecked part gives isn't from its mass and material");
        SandboxMap Read(string json) => SandboxMap.Read(JsonDocument.Parse(json).RootElement, "test");
        var def = Read("{\"rack\": [0, 0]}");
        var set = Read("{\"rack\": [0, 0], \"infiniteParts\": false, \"debrisSeconds\": 30, \"debrisNear\": 5}");
        string Bad(string json)
        {
            try
            {
                Read(json);
                return "accepted";
            }
            catch (MapFileException ex)
            {
                return ex.Message;
            }
        }
        var badFlag = Bad("{\"infiniteParts\": 1}");
        var badTime = Bad("{\"debrisSeconds\": -2}");
        Line($"salvage map flags: by default infinite {def.InfiniteParts}, debris {def.DebrisSeconds:0} s / {def.DebrisNear:0} m; set: infinite {set.InfiniteParts}, {set.DebrisSeconds:0} s / {set.DebrisNear:0} m; "
             + $"a number for the flag: \"{badFlag}\"; a negative time: \"{badTime}\"");
        if (!def.InfiniteParts || def.DebrisSeconds != SandboxMap.DefaultDebrisSeconds || set.InfiniteParts || set.DebrisSeconds != 30 || set.DebrisNear != 5
            || !badFlag.Contains("infiniteParts") || !badTime.Contains("debrisSeconds"))
            Fail("construct salvage: the map's infiniteParts / debrisSeconds / debrisNear don't read right");
    }

    private static Vector3 PartCentre(Construct c, int part)
    {
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        c.PartBounds(part, out var mn, out var mx);
        return t + Vector3.Transform((mn + mx) * 0.5f, q);
    }

    /// <summary>Stands the player <paramref name="dist"/> m from a point, on the side away from <paramref name="awayFrom"/> (on the ground).</summary>
    private static void StandBy(Player me, Vector3 at, float dist = 2.2f, Vector3? awayFrom = null)
    {
        var dir = at - (awayFrom ?? at - Vector3.UnitX);
        dir.Y = 0;
        dir = dir.LengthSquared() > 1e-4f ? Vector3.Normalize(dir) : Vector3.UnitX;
        var from = at + dir * dist;
        me.Teleport(ConstructWorld.Ground(World.Query, from.X, from.Z) + new Vector3(0, 0.05f, 0));
    }

    private static void StandAt(Player me, float x, float z) => me.Teleport(ConstructWorld.Ground(World.Query, x, z) + new Vector3(0, 0.05f, 0));

    private static void SalvageOffline(Map yard, SandboxMap sb)
    {
        yard.RestoreProps();
        World.SetMap(yard);
        sb.DebrisSeconds = 0; // (no expiry while the scene is set up; tested on its own below)
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        Run(g, ref now, Game.CountdownSeconds + 0.3);
        g.RequestRespawn();
        Run(g, ref now, 1.0);
        if (g.Local is not { Alive: true } me)
        {
            Fail("construct salvage: no local player in the Sandbox game");
            return;
        }
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        ref var hand = ref g.SandboxHands.Hand(me.Id);
        var inv = g.SandboxInventory(me.Id);
        var w = g.Constructs;
        var wheelDef = ConstructParts.Need("Wheel");
        var suspDef = ConstructParts.Need("Suspension");

        // A wreck: the yard buggy's suspension shot through drops its wheel.
        var car = w.Bodies.First(b => b.CanDrive && b.Owner == 255);
        var susp = PartOf(car, PartRole.Suspension);
        w.DamagePart(car, susp, 100000, out _);
        Run(g, ref now, 2.0);
        var wheelWreck = w.Bodies.FirstOrDefault(b => b.Wreck && PartOf(b, PartRole.Wheel) is var k && k >= 0 && !b.Parts[k].Broken);
        var suspDebris = w.Bodies.FirstOrDefault(b => b.Wreck && b.PartCount == 1 && b.DefOf(0) == suspDef && b.Parts[0].Broken);
        Want(car.Damaged && wheelWreck != null && suspDebris != null, "the shot suspension leaves a wheel wreck and a broken suspension, and marks the buggy damaged");
        if (wheelWreck == null || suspDebris == null)
        {
            Fail($"construct salvage: offline: {string.Join("; ", problems)}");
            return;
        }

        // Salvage the wheel: the wrench, timed, into the inventory (the hand stays empty).
        var wi = PartOf(wheelWreck, PartRole.Wheel);
        StandBy(me, PartCentre(wheelWreck, wi), 2.2f, car.WorldCom);
        Run(g, ref now, 0.2);
        g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Welder);
        g.SandboxSend(CActKind.Remove, body: wheelWreck.Id, part: (byte)wi);
        var wrongTool = g.SandboxNotice;
        g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Wrench);
        g.SandboxSay("");
        g.SandboxSend(CActKind.Remove, body: wheelWreck.Id, part: (byte)wi);
        var began = hand.Edit == EditKind.Remove;
        Run(g, ref now, ConstructBuild.RemoveSeconds(wheelDef) * 0.5);
        var early = inv.Count(wheelDef.Id);
        Run(g, ref now, ConstructBuild.RemoveSeconds(wheelDef) * 0.5 + 0.15);
        var wheels = inv.Count(wheelDef.Id);
        Line($"salvage a wreck's wheel: the welder refused (\"{wrongTool}\"); the wrench began {began}, half-way {early}, done: {wheels} Wheel in the inventory, hand {(hand.Held == ConstructParts.None ? "empty" : "full")}");
        // (decision 21: with infinite parts on, creative, a wrench removal is instant; counted, it is timed)
        var instant = g.SandboxHands.Creative;
        Want(wrongTool.Contains("wrench") && (instant ? !began && early == 1 : began && early == 0) && wheels == 1 && hand.Held == ConstructParts.None,
            $"a wreck's wheel is salvaged ({(instant ? "at once in creative" : "timed")}, with the wrench) into the inventory");

        // Scrap from the wrecked suspension.
        StandBy(me, suspDebris.WorldCom, 2.2f, car.WorldCom);
        Run(g, ref now, 0.2);
        var scrap0 = inv.Scrap;
        g.SandboxSend(CActKind.Remove, body: suspDebris.Id, part: 0);
        Run(g, ref now, ConstructBuild.RemoveSeconds(suspDef) + 0.15);
        var gotScrap = inv.Scrap - scrap0;
        Line($"salvage scrap: the broken Suspension gave {gotScrap} scrap (its value {suspDef.Scrap}), no Suspension part {inv.Count(suspDef.Id) == 0}, the debris gone {w.Get(suspDebris.Id) == null}");
        Want(gotScrap == suspDef.Scrap && inv.Count(suspDef.Id) == 0 && w.Get(suspDebris.Id) == null, "a destroyed part gives its scrap, not the part");

        // Someone else's gate: refused while it works, salvaged once disabled.
        var gateBp = IndexOfBlueprint("Gate");
        StandAt(me, 10, 0);
        Run(g, ref now, 0.3);
        g.SandboxSend(CActKind.Blueprint, part: (byte)gateBp);
        var gate = w.Bodies[^1];
        gate.Owner = 42; // (as if another player built it)
        Run(g, ref now, 1.0);
        var door = PartOf(gate, PartRole.Door);
        var doorDef = gate.DefOf(door);
        StandBy(me, PartCentre(gate, door), 1.8f, gate.WorldCom);
        Run(g, ref now, 0.2);
        g.SandboxSay("");
        g.SandboxSend(CActKind.Remove, body: gate.Id, part: (byte)door);
        var theirs = g.SandboxNotice;
        var block = PartOf(gate, PartRole.Frame);
        w.DamagePart(gate, PartOf(gate, PartRole.Foundation), 100000, out _); // (disabled: its foundation wrecked)
        Run(g, ref now, 1.0);
        door = PartOf(gate, PartRole.Door);
        if (door >= 0)
            StandBy(me, PartCentre(gate, door), 1.8f, gate.WorldCom);
        Run(g, ref now, 0.2);
        g.SandboxSay("");
        var doors0 = inv.Count(doorDef.Id);
        if (door >= 0)
            g.SandboxSend(CActKind.Remove, body: gate.Id, part: (byte)door);
        var disabledWhy = g.SandboxNotice;
        Run(g, ref now, ConstructBuild.RemoveSeconds(doorDef) + 0.15);
        Line($"salvage someone else's gate: working: \"{theirs}\"; disabled (damaged {gate.Damaged}): \"{disabledWhy}\", {inv.Count(doorDef.Id) - doors0} Door salvaged");
        Want(theirs.Contains("someone else's") && gate.Damaged && disabledWhy.Length == 0 && inv.Count(doorDef.Id) == doors0 + 1, "someone else's construct can be salvaged only once disabled");
        _ = block;

        // Infinite parts off: the menu hands out only what the inventory holds; a fit uses one; your own comes back.
        sb.InfiniteParts = false;
        StandAt(me, 18, 0);
        Run(g, ref now, 0.3);
        g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Wrench);
        hand.Held = ConstructParts.None;
        var pistonDef = ConstructParts.Need("Piston");
        g.SandboxSay("");
        g.SandboxTakePart(pistonDef.Id);
        var noPiston = g.SandboxNotice;
        inv.Add(wheelDef.Id); // (two wheels now)
        g.SandboxTakePart(wheelDef.Id);
        var tookWheel = hand.Held == wheelDef.Id && inv.Count(wheelDef.Id) == 1;
        // Fit it on the ground as a new construct: it uses that one, and the next comes out into the hand.
        var spot = me.Position + World.FlatForward(me.Yaw) * 2f;
        var ground = ConstructWorld.Ground(World.Query, spot.X, spot.Z);
        var bodies0 = w.Bodies.Count;
        g.SandboxSend(CActKind.PlaceNew, at: ground, yaw: 0);
        var placeWhy = g.SandboxNotice;
        Run(g, ref now, wheelDef.AttachSeconds + 0.15);
        var placed = w.Bodies.Count == bodies0 + 1 ? w.Bodies[^1] : null;
        var refilled = hand.Held == wheelDef.Id && inv.Count(wheelDef.Id) == 0;
        var owned = placed?.Owner == me.Id;
        g.SandboxSay(""); // (a notice takes the hint's place)
        var lines = new List<HudLine>();
        var white = Vector4.One;
        g.SandboxHud(me, lines, white, white, white, white, white);
        var hud = string.Join(" | ", lines.Select(l => l.Text));
        Line($"infinite parts off: a Piston refused (\"{noPiston}\"); a Wheel taken {tookWheel}; set down {(placed != null ? $"as construct {placed.Id}, owned by the player {owned}" : $"no (\"{placeWhy}\")")}, the next in hand {refilled}; HUD: {hud}");
        Want(noPiston.Contains("no Piston") && tookWheel && placed != null && owned && refilled, "with infinite parts off the menu hands out the inventory's parts and a fit uses one");
        Want(hud.Contains("last one in your inventory") && hud.Contains("Inventory:") && hud.Contains("scrap"), "the HUD shows the counts while holding a part");

        // Your own gate: its door comes back into the inventory; the hand's wheel survives a death.
        hand.Held = ConstructParts.None;
        inv.Add(wheelDef.Id);
        StandAt(me, 2, -6);
        Run(g, ref now, 0.3);
        sb.InfiniteParts = true; // (the menu's blueprints aren't counted)
        g.SandboxSend(CActKind.Blueprint, part: (byte)gateBp);
        sb.InfiniteParts = false;
        var mine = w.Bodies[^1];
        Run(g, ref now, 1.0);
        var myDoor = PartOf(mine, PartRole.Door);
        StandBy(me, PartCentre(mine, myDoor), 1.8f, mine.WorldCom);
        Run(g, ref now, 0.2);
        var d0 = inv.Count(doorDef.Id);
        g.SandboxSay("");
        g.SandboxSend(CActKind.Remove, body: mine.Id, part: (byte)myDoor);
        var ownWhy = g.SandboxNotice;
        Run(g, ref now, ConstructBuild.RemoveSeconds(doorDef) + 0.15);
        var back = inv.Count(doorDef.Id) - d0;
        g.SandboxTakePart(wheelDef.Id);
        var w0 = inv.Count(wheelDef.Id);
        g.SandboxOnSpawn(me); // (what a respawn does to the hands)
        var survived = inv.Count(wheelDef.Id) == w0 + 1 && hand.Held == ConstructParts.None;
        Line($"infinite parts off: own gate (owner {mine.Owner}) \"{ownWhy}\", its Door back into the inventory: {back}; a counted Wheel in hand goes back on respawn {survived}");
        Want(mine.Owner == me.Id && ownWhy.Length == 0 && back == 1 && survived, "with infinite parts off your own construct's parts go back into the inventory");

        // Infinite parts on: your own parts come off into the hand, as before.
        sb.InfiniteParts = true;
        Run(g, ref now, 0.1);
        var hinge = PartOf(mine, PartRole.Hinge);
        hand.Held = ConstructParts.None;
        var hingeDef = mine.DefOf(hinge);
        var h0 = inv.Count(hingeDef.Id);
        g.SandboxSend(CActKind.Remove, body: mine.Id, part: (byte)hinge);
        Run(g, ref now, ConstructBuild.RemoveSeconds(hingeDef) + 0.15);
        var intoHand = hand.Held == hingeDef.Id && inv.Count(hingeDef.Id) == h0;
        Line($"infinite parts on: your own gate's Hinge came off into the hand {intoHand}");
        Want(intoHand, "with infinite parts on your own parts come off into the hand");

        SalvageDebris(g, ref now, sb, problems);
        SalvageNoAlloc(g, ref now, problems);
        foreach (var p in problems)
            Fail($"construct salvage: {p}");
    }

    /// <summary>Debris expiry: a wreck alone goes after the time; not while being salvaged, not with a player near.</summary>
    private static void SalvageDebris(Game g, ref double now, SandboxMap sb, List<string> problems)
    {
        var me = g.Local!;
        var w = g.Constructs;
        var car = w.Bodies.FirstOrDefault(b => b.CanDrive && b.Owner == 255);
        if (car == null)
        {
            problems.Add("debris: no second buggy to break");
            return;
        }
        // Two wrecks: the second buggy's two suspensions shot through (each drops its wheel).
        w.DamagePart(car, PartOf(car, PartRole.Suspension), 100000, out _);
        w.DamagePart(car, PartOf(car, PartRole.Suspension), 100000, out _);
        Run(g, ref now, 1.5);
        var wrecks = w.Bodies.Where(b => b.Wreck && PartOf(b, PartRole.Wheel) is var k && k >= 0 && !b.Parts[k].Broken).ToList();
        if (wrecks.Count < 2)
        {
            problems.Add($"debris: {wrecks.Count} wheel wrecks from two shot suspensions");
            return;
        }
        var kept = wrecks[0];
        var alone = wrecks[1];
        // Salvaging the first (slowly: a long removal) while the time runs out; nobody near the second. (Counted parts for
        // this: a creative wrench removal is instant, decision 21.)
        var infinite = sb.InfiniteParts;
        sb.InfiniteParts = false;
        sb.DebrisSeconds = 1.0f;
        sb.DebrisNear = 0.5f;
        var wi = PartOf(kept, PartRole.Wheel);
        StandBy(me, PartCentre(kept, wi), 2.6f, alone.WorldCom);
        Run(g, ref now, 0.1);
        g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Wrench);
        g.SandboxSay("");
        g.SandboxSend(CActKind.Remove, body: kept.Id, part: (byte)wi);
        ref var hand = ref g.SandboxHands.Hand(me.Id);
        var began = g.SandboxNotice;
        hand.Duration = 4.0; // (a slow salvage)
        Run(g, ref now, 2.5);
        var keptWhile = w.Get(kept.Id) != null && hand.Edit == EditKind.Remove;
        var aloneGone = w.Get(alone.Id) == null;
        g.SandboxSend(CActKind.Cancel);
        sb.InfiniteParts = infinite;
        StandAt(me, 20, 24);
        Run(g, ref now, 2.0);
        var keptGoneAfter = w.Get(kept.Id) == null;
        // A player near keeps one.
        w.DamagePart(car, PartOf(car, PartRole.Suspension) is var wh and >= 0 ? wh : 0, 100000, out _); // (a wrecked tyre goes flat and stays on: shoot a suspension to drop a wheel)
        Run(g, ref now, 0.5);
        var near = w.Bodies.LastOrDefault(b => b.Wreck);
        var nearKept = false;
        if (near != null)
        {
            sb.DebrisNear = 6f;
            StandBy(me, near.WorldCom, 3f, car.WorldCom);
            Run(g, ref now, 2.5);
            nearKept = w.Get(near.Id) != null;
        }
        sb.DebrisSeconds = 0;
        Line($"debris expiry (1 s): the wreck alone gone {aloneGone}; the one being salvaged kept {keptWhile} (\"{began}\"), gone once left {keptGoneAfter}; a wreck with a player 3 m off kept {nearKept}");
        if (!aloneGone || !keptWhile || !keptGoneAfter || !nearKept)
            problems.Add("debris expiry: a wreck alone wasn't cleared, or one being salvaged or with someone near was");
    }

    /// <summary>No allocation a tick: the Sandbox tick with a salvage going on, the debris check, the inventory and its message.</summary>
    private static void SalvageNoAlloc(Game g, ref double now, List<string> problems)
    {
        Run(g, ref now, 0.5);
        var me = g.Local!;
        var inv = g.SandboxInventory(me.Id);
        var wheel = ConstructParts.Need("Wheel").Id;
        var pw = new PacketWriter(4096);
        var sb = World.Map.Sandbox!;
        sb.DebrisSeconds = 1000;
        var t = now;
        void Work(int n)
        {
            for (var k = 0; k < n; k++)
            {
                t += World.TickDt;
                g.ServerTickSandbox(t);
                g.Constructs.ExpireDebris(t, sb.DebrisSeconds, sb.DebrisNear, g.SandboxHands);
                inv.Add(wheel);
                inv.Take(wheel);
                inv.AddScrap(1);
                ConstructNet.WriteInventory(pw, inv);
                _ = inv.TotalParts + inv.Kinds;
            }
        }
        Work(60);
        var least = long.MaxValue;
        for (var round = 0; round < 3; round++)
        {
            var b0 = GC.GetAllocatedBytesForCurrentThread();
            Work(120);
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - b0);
        }
        sb.DebrisSeconds = 0;
        now = t;
        Line($"salvage: the Sandbox tick, the debris check and the inventory allocate {least} bytes per 120 ticks (least of three)");
        if (least > 0)
            problems.Add($"{least} bytes allocated per 120 ticks (should be none)");
    }

    /// <summary>A real host and two clients: the owner sees their counts (the other doesn't), owners travel, an expired wreck goes everywhere.</summary>
    private static void SalvageOverWire(Map yard, SandboxMap sb)
    {
        const ushort port = 26347;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        sb.DebrisSeconds = 0;
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a, b], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a, b], 0.25, ref now);
        Pump(server, [a, b], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue || b.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Fail("construct salvage over the wire: the clients didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        Pump(server, [a, b], 0.3, ref now, clock);
        var car = server.Constructs.Bodies.First(c => c.CanDrive && c.Owner == 255);
        var wi = PartOf(car, PartRole.Wheel);
        var wheel = ConstructParts.Need("Wheel");
        StandBy(sp, PartCentre(car, wi), 2.2f, car.WorldCom);
        Pump(server, [a, b], 0.3, ref now, clock);
        a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Wrench);
        a.SandboxSend(CActKind.Remove, body: car.Id, part: (byte)wi);
        Pump(server, [a, b], ConstructBuild.RemoveSeconds(wheel) + 0.6, ref now, clock);
        var onServer = server.SandboxInventory(a.LocalId).Count(wheel.Id);
        var onOwner = a.SandboxInventory(a.LocalId).Count(wheel.Id);
        var onOther = b.SandboxInventory(b.LocalId).Count(wheel.Id) + b.SandboxInventory(a.LocalId).Count(wheel.Id);
        var flag = a.SandboxInfiniteParts;
        // The owner travels with a construct: a gate a client spawns is theirs on every copy.
        a.SandboxSend(CActKind.Blueprint, part: (byte)IndexOfBlueprint("Gate"));
        Pump(server, [a, b], 0.6, ref now, clock);
        var gate = server.Constructs.Bodies[^1];
        var ownerA = a.Constructs.Get(gate.Id)?.Owner;
        var ownerB = b.Constructs.Get(gate.Id)?.Owner;
        // A wreck cleared away on the server goes on the clients too.
        server.Constructs.DamagePart(car, PartOf(car, PartRole.Suspension), 100000, out _);
        Pump(server, [a, b], 0.4, ref now, clock);
        var wrecks = server.Constructs.Bodies.Where(c => c.Wreck).Select(c => c.Id).ToList();
        var mirrored = wrecks.Count > 0 && wrecks.All(id => a.Constructs.Get(id)?.Wreck == true);
        sp.Teleport(ConstructWorld.Ground(World.Query, 25, 25) + new Vector3(0, 0.05f, 0));
        if (server.Players.TryGetValue(b.LocalId, out var bp))
            bp.Teleport(ConstructWorld.Ground(World.Query, 26, 25) + new Vector3(0, 0.05f, 0));
        sb.DebrisSeconds = 0.5f;
        sb.DebrisNear = 2f;
        PumpFast(server, [a, b], 1.5, ref now);
        Pump(server, [a, b], 0.4, ref now, clock);
        sb.DebrisSeconds = 0;
        var goneServer = wrecks.Count > 0 && wrecks.All(id => server.Constructs.Get(id) == null);
        var goneClient = wrecks.All(id => a.Constructs.Get(id) == null && b.Constructs.Get(id) == null);
        Line($"salvage over the wire: a client's salvaged Wheel: server {onServer}, the owner's client {onOwner}, the other client {onOther}; infinite on the client {flag}; "
             + $"a client's gate owned by {gate.Owner} (client {a.LocalId}): copies say {ownerA} and {ownerB}; {wrecks.Count} wreck(s) mirrored {mirrored}, expired on the server {goneServer} and the clients {goneClient}");
        if (onServer != 1 || onOwner != 1 || onOther != 0 || !flag)
            Fail("construct salvage over the wire: the owner's client doesn't see its counts (or another client does)");
        if (gate.Owner != a.LocalId || ownerA != a.LocalId || ownerB != a.LocalId)
            Fail("construct salvage over the wire: a construct's owner doesn't travel");
        if (!mirrored || !goneServer || !goneClient)
            Fail("construct salvage over the wire: an expired wreck didn't go on every copy");
    }
}
