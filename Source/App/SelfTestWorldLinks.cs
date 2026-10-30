using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Nodes;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "World links" (board item wl01; docs/sandbox/CONSTRUCTS.md, "World links", "Hoses"): a fuel pipe routed across the
    /// ground between two anchored constructs carries fuel (and the routing rules refuse a sunk node, a node in the air, a
    /// wrong bend and a shaft); a data cable across the ground lights a lamp on another construct; a power cable from a
    /// parked buggy to a base carries charge while suspension settling leaves it alone, and snaps within a tick of the
    /// buggy driving off (with the hint); a world node whose ground goes breaks its link; a fuel hose taken off a station's
    /// reel and plugged into the buggy's inlet refuels it, pops out when the buggy drives past its length, and unplugs and
    /// hangs back by hand; the wire round trip; no allocation per tick; and a real host with a separate client (and a late
    /// joiner): the client routes a world link and plugs a hose over the network, and both mirror the server's.
    /// Short scripted scenarios on flat ground and the Construct Yard.
    /// </summary>
    private static void CheckWorldLinks()
    {
        WorldLinksPipe();
        WorldLinksData();
        WorldLinksVehicle();
        WorldLinksHose();
        WorldLinksLoopback();
    }

    private static string OnePart(string part, params string[] more)
    {
        var steps = new List<string> { $$"""{ "part": "{{part}}" }""" };
        steps.AddRange(more);
        return $$"""{ "format": "call-of-slop-construct-blueprint/1", "name": "{{part}} test", "steps": [ {{string.Join(", ", steps)}} ] }""";
    }

    /// <summary>A one-part (or a few parts) construct pinned to the ground at (x, z): it counts as the world.</summary>
    private static Construct? Pinned(ConstructWorld w, string json, float x, float z, string what)
    {
        if (RoutesBuild(w, json, x, z, what) is not { } c)
            return null;
        c.Pinned = true;
        c.Rebuild();
        return c;
    }

    private static WNode Ground(Vector3 p) => new() { Pos = p };

    private static WNode OnPart(Construct c, int part, Vector3 world)
    {
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        return new WNode { Body = c.Id, Part = (byte)part, Pos = Vector3.Transform(world - t, Quaternion.Conjugate(q)) };
    }

    /// <summary>A world that loses its ground (craters, decision 16, aren't built: this stands in for terrain blown away).</summary>
    private sealed class HoleWorld(IWorldQuery inner) : IWorldQuery
    {
        public bool Gone;
        public ITerrainCollision? Terrain => inner.Terrain;
        public float Raycast(Vector3 origin, Vector3 dir, float maxDist, in QueryFilter filter) => Gone ? maxDist : inner.Raycast(origin, dir, maxDist, filter);

        public bool Raycast(Vector3 origin, Vector3 dir, float maxDist, in QueryFilter filter, out RayHit hit)
        {
            hit = default;
            return !Gone && inner.Raycast(origin, dir, maxDist, filter, out hit);
        }

        public bool SphereCast(Vector3 origin, float radius, Vector3 dir, float maxDist, in QueryFilter filter, out RayHit hit) => inner.SphereCast(origin, radius, dir, maxDist, filter, out hit);
        public bool CapsuleCast(in Capsule capsule, Vector3 dir, float maxDist, in QueryFilter filter, out RayHit hit) => inner.CapsuleCast(capsule, dir, maxDist, filter, out hit);
        public bool OverlapBox(in Obb box, in QueryFilter filter) => inner.OverlapBox(box, filter);
        public bool OverlapCapsule(in Capsule capsule, in QueryFilter filter) => inner.OverlapCapsule(capsule, filter);
        public bool OverlapBody(Vector3 feet, float height, in QueryFilter filter) => inner.OverlapBody(feet, height, filter);
        public bool PointInside(Vector3 point, float radius, in QueryFilter filter) => inner.PointInside(point, radius, filter);
    }

    private static void WorldLinksPipe()
    {
        WorldLinksPipeOf(Fluid.Fuel);
        // One pipe for every fluid (decision 28): the same routing, the same RouteType.Pipe, with the tanks holding water.
        var tank = ConstructParts.Find("Fuel Tank")!;
        tank.TankFluid = Fluid.Water;
        try
        {
            WorldLinksPipeOf(Fluid.Water);
        }
        finally
        {
            tank.TankFluid = Fluid.Fuel;
        }
    }

    private static void WorldLinksPipeOf(Fluid fluid)
    {
        var w = new ConstructWorld();
        var q = new HoleWorld(RoutesGround);
        if (Pinned(w, OnePart("Fuel Tank"), 0, 0, "tank A") is not { } a || Pinned(w, OnePart("Fuel Tank"), 9, 0, "tank B") is not { } b)
            return;
        b.Fuel = 0;
        var r = ConstructRoutes.Radius(RouteType.Pipe);
        var ta = ConstructWorld.PortWorld(a, 0, 255);
        var tb = ConstructWorld.PortWorld(b, 0, 255);
        // Down at 15 degrees to the ground, along it, and up at 15 degrees into the other tank.
        var run = (ta.Y - r) / MathF.Tan(15 * MathF.PI / 180);
        Span<WNode> nodes = [Ground(new Vector3(ta.X + run, r, 0)), Ground(new Vector3(4.5f, r, 0)), Ground(new Vector3(tb.X - run, r, 0))];
        for (var i = 0; i < nodes.Length; i++)
            nodes[i].Pos = ConstructWorld.Quantise(nodes[i].Pos);
        var ok = w.CheckWorldLink(q, RouteType.Pipe, a, 0, 255, nodes, b, 0, 255);
        // The refusals: a node sunk into the ground, a node in the air, a bend off the 15 degree steps, a shaft.
        Span<WNode> bad = [nodes[0], nodes[1], nodes[2]];
        bad[1].Pos.Y = -0.02f;
        var sunk = w.CheckWorldLink(q, RouteType.Pipe, a, 0, 255, bad, b, 0, 255) ?? "";
        bad[1].Pos = new Vector3(4.5f, r, 0.9f);
        var bend = w.CheckWorldLink(q, RouteType.Pipe, a, 0, 255, bad, b, 0, 255) ?? "";
        var shaft = w.CheckWorldLink(q, RouteType.Shaft, a, 0, 255, nodes, b, 0, 255) ?? "";
        var added = ok == null && w.AddWorldLink(q, RouteType.Pipe, a, 0, 255, nodes, b, 0, 255) >= 0;
        var before = a.Fuel;
        for (var t = 0; t < World.TickRate; t++)
            w.Step(q, World.TickDt);
        var moved = b.Fuel;
        // The ground under a node goes: the link breaks within a sweep of the nodes.
        q.Gone = true;
        for (var t = 0; t < w.WorldNodeCount + 2 && w.WorldLinkCount > 0; t++)
            w.Step(q, World.TickDt);
        var groundGone = w.WorldLinkCount == 0 && w.Events.Any(e => e.Contains("ground under it is gone"));
        Line($"world links, {fluid} pipe: across the ground between two pinned tanks {(ok ?? "accepted")}; in a second {moved:0.00} l went over (A {before:0.0} -> {a.Fuel:0.0} l); refusals: \"{sunk}\", \"{bend}\", \"{shaft}\"; the ground gone broke it {groundGone}");
        if (!added)
            Fail($"world links: a {fluid} pipe across the ground between two anchored constructs was refused: {ok}");
        if (MathF.Abs(moved - ConstructWorld.PipeFlow) > 0.1f)
            Fail($"world links: the {fluid} pipe carried {moved:0.00} l in a second (expected {ConstructWorld.PipeFlow:0.0})");
        if (!sunk.Contains("sink") || !bend.Contains("15 degree") || !shaft.Contains("drive shaft"))
            Fail("world links: a sunk node, a wrong bend or a shaft wasn't refused for the right reason");
        if (!groundGone)
            Fail("world links: a world node whose ground went didn't break its link");
    }

    private static void WorldLinksData()
    {
        var w = new ConstructWorld();
        if (Pinned(w, OnePart("Switch"), 0, 3, "switch") is not { } sw || Pinned(w, OnePart("Lamp"), 4, 3, "lamp") is not { } lamp)
            return;
        var r = ConstructRoutes.Radius(RouteType.Data);
        Span<WNode> nodes = [Ground(ConstructWorld.Quantise(new Vector3(2, r, 3)))];
        Span<WNode> high = [Ground(ConstructWorld.Quantise(new Vector3(2, 1.5f, 3)))];
        var air = w.CheckWorldLink(RoutesGround, RouteType.Data, sw, 0, 255, high, lamp, 0, 255) ?? "";
        var ok = w.CheckWorldLink(RoutesGround, RouteType.Data, sw, 0, 255, nodes, lamp, 0, 255);
        if (ok == null)
            w.AddWorldLink(RoutesGround, RouteType.Data, sw, 0, 255, nodes, lamp, 0, 255);
        sw.Parts[0].State = 1; // (switched on)
        sw.Wake();
        for (var t = 0; t < 10; t++)
            w.Step(RoutesGround, World.TickDt);
        var lit = lamp.Parts[0].In >= 0.5f;
        sw.Parts[0].State = 0;
        sw.Wake();
        for (var t = 0; t < 10; t++)
            w.Step(RoutesGround, World.TickDt);
        var off = lamp.Parts[0].In < 0.5f;
        Line($"world links, data: a cable across the ground from a switch to a lamp on another construct {(ok ?? "accepted")}; switched on it lights {lit}, off it goes out {off}; a node in the air refused: \"{air}\"");
        if (!air.Contains("surface"))
            Fail("world links: a world node in the air wasn't refused");
        if (ok != null || !lit || !off)
            Fail("world links: a data cable between constructs didn't carry the switch's signal");
    }

    /// <summary>The buggy with a battery and a fuel hose inlet on its tank.</summary>
    private static Construct? HoseBuggy(ConstructWorld w, float x, float z)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Constructs", "Blueprints", "Buggy.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var steps = root["steps"]!.AsArray();
        var tank = -1;
        for (var i = 0; i < steps.Count; i++)
            if ((string?)steps[i]!["part"] == "Fuel Tank")
                tank = i;
        steps.Add(JsonNode.Parse($$"""{ "part": "Fluid Hose Inlet", "on": {{tank}}, "socket": "+x", "by": "-z" }"""));
        steps.Add(JsonNode.Parse($$"""{ "part": "Battery", "on": {{tank}}, "socket": "+y", "by": "-y" }"""));
        root["name"] = "Hose Buggy";
        return RoutesBuild(w, root.ToJsonString(), x, z, "the hose buggy");
    }

    private static void WorldLinksVehicle()
    {
        var w = new ConstructWorld();
        if (HoseBuggy(w, 0, 0) is not { } car || Pinned(w, OnePart("Battery"), 6, 0, "base battery") is not { } bat)
            return;
        bat.Charge = 0;
        // Settle a little first (dropped onto its wheels), then link while it is still settling.
        for (var t = 0; t < 40; t++)
            w.Step(RoutesGround, World.TickDt);
        var cb = PartNamed(car, "Battery");
        var top = ConstructWorld.PortWorld(car, cb, 255) + new Vector3(0, 0.125f + ConstructRoutes.Radius(RouteType.Power), 0);
        var r = ConstructRoutes.Radius(RouteType.Power);
        Span<WNode> nodes = [OnPart(car, cb, ConstructWorld.Quantise(top)), Ground(ConstructWorld.Quantise(new Vector3(3.2f, r, top.Z)))];
        var ok = w.CheckWorldLink(RoutesGround, RouteType.Power, car, cb, 255, nodes, bat, 0, 255);
        if (ok == null)
            w.AddWorldLink(RoutesGround, RouteType.Power, car, cb, 255, nodes, bat, 0, 255);
        // Linked while it is still settling on its suspension: the link stays while it comes to rest.
        var p0 = car.Pos;
        var settle = 0f;
        for (var t = 0; t < 3 * World.TickRate; t++)
        {
            w.Step(RoutesGround, World.TickDt);
            settle = MathF.Max(settle, Vector3.Distance(p0, car.Pos));
        }
        var kept = w.WorldLinkCount == 1;
        var charged = bat.Charge;
        // It drives off: the link snaps within a tick of the buggy moving past the tolerance.
        var made = w.WorldLinkCount > 0 ? w.WorldLinks[0].MadeFrom : default;
        car.Wake();
        int movedAt = -1, brokeAt = -1;
        w.Hints.Clear();
        for (var t = 0; t < World.TickRate && brokeAt < 0; t++)
        {
            car.Vel = new Vector3(0, 0, -4f);
            w.Step(RoutesGround, World.TickDt);
            if (movedAt < 0 && Vector3.Distance(ConstructWorld.PortWorld(car, cb, 255), made) > ConstructWorld.SnapMetres)
                movedAt = t;
            if (w.WorldLinkCount == 0)
                brokeAt = t;
        }
        var hint = w.Hints.Count > 0 ? w.Hints[0].Text : "";
        var spark = w.Fires.AsSpan(0, w.FireCount).ToArray().Any(f => f.Kind == CFireKind.LinkSnap);
        Line($"world links, vehicle: a power cable from a parked buggy to a pinned battery {(ok ?? "accepted")}; settling moved it {settle * 100:0.0} cm and the link stayed {kept}; {charged:0.0} kJ charged in 3 s; driving off, it moved past {ConstructWorld.SnapMetres * 100:0} cm at tick {movedAt} and snapped at tick {brokeAt} (sparks {spark}): \"{hint}\"");
        if (ok != null || !kept)
            Fail($"world links: the link to a parked vehicle was refused or settling broke it ({ok})");
        if (charged < ConstructWorld.CableFlow * 2.5f)
            Fail($"world links: the power cable carried {charged:0.0} kJ in 3 s");
        if (brokeAt < 0 || movedAt < 0 || brokeAt > movedAt + 1 || !hint.Contains("tore off") || !hint.Contains("vehicle") || !spark)
            Fail("world links: the link didn't snap within a tick of the vehicle driving off (or no hint)");
    }

    private static void WorldLinksHose()
    {
        var w = new ConstructWorld();
        if (Pinned(w, OnePart("Fuel Tank", RouteStep("Fluid Hose Reel", 0, "+y", "-y")), 0, 6, "station") is not { } st || HoseBuggy(w, 3, 6) is not { } car)
            return;
        for (var t = 0; t < 60; t++)
            w.Step(RoutesGround, World.TickDt);
        car.Fuel = 5;
        var reel = PartNamed(st, "Fluid Hose Reel");
        var inlet = PartNamed(car, "Fluid Hose Inlet");
        var hand = ConstructWorld.HosePoint(st, reel);
        w.Hands = id => id == 1 ? hand : null;
        var full = w.HoseUse(1, st, reel, handsFree: false) ?? "";
        var took = w.HoseUse(1, st, reel, handsFree: true);
        var held = w.HoseHeldBy(1) >= 0;
        hand = ConstructWorld.HosePoint(car, inlet);
        w.Step(RoutesGround, World.TickDt);
        var wrong = w.HoseUse(1, st, reel, handsFree: true); // (hangs it back: allowed; take it again)
        w.HoseUse(1, st, reel, handsFree: true);
        var plugged = w.HoseUse(1, car, inlet, handsFree: true);
        var fuel0 = car.Fuel;
        for (var t = 0; t < World.TickRate; t++)
            w.Step(RoutesGround, World.TickDt);
        var refuelled = car.Fuel - fuel0;
        // Unplug by hand, and plug it back in.
        var unplug = w.HoseUse(1, car, inlet, handsFree: true);
        var inHand = w.HoseHeldBy(1) >= 0 && !w.Hoses[w.HoseHeldBy(1)].Plugged;
        w.HoseUse(1, car, inlet, handsFree: true);
        w.Hands = _ => null;
        // Drive off past the hose's length: it pops out (no damage) and reels back.
        var length = st.DefOf(reel).HoseLength;
        int popAt = -1;
        float popDist = 0;
        w.Hints.Clear();
        var hp = car.Parts[inlet].Health;
        car.Wake();
        for (var t = 0; t < 8 * World.TickRate && popAt < 0; t++)
        {
            car.Vel = new Vector3(4f, 0, 0);
            w.Step(RoutesGround, World.TickDt);
            if (w.HoseCount == 0)
            {
                popAt = t;
                popDist = Vector3.Distance(ConstructWorld.HosePoint(st, reel), ConstructWorld.HosePoint(car, inlet));
            }
        }
        var popHint = w.Hints.Count > 0 ? w.Hints[0].Text : "";
        var unhurt = car.Parts[inlet].Health >= hp;
        Line($"world links, hose: full hands refused (\"{full}\"); took the nozzle {took ?? "ok"} (held {held}); hung back and taken again {wrong ?? "ok"}; plugged into the buggy {plugged ?? "ok"}, {refuelled:0.00} l in a second; unplugged by hand {unplug ?? "ok"} (in hand {inHand}); driving off it popped out at {popDist:0.00} m (length {length:0}) \"{popHint}\", the inlet unhurt {unhurt}");
        if (!full.Contains("put down") || took != null || !held || plugged != null || unplug != null || !inHand)
            Fail("world links: taking, plugging or unplugging the hose didn't go as it should");
        if (MathF.Abs(refuelled - st.DefOf(reel).HoseFlow) > 0.1f)
            Fail($"world links: the fuel hose moved {refuelled:0.00} l in a second (rated {st.DefOf(reel).HoseFlow:0.0})");
        if (popAt < 0 || popDist > length + 0.2f || popDist < length || !popHint.Contains("popped out") || !unhurt)
            Fail("world links: the hose didn't pop out at its length when the vehicle drove off (or it was damaged)");
        // The wire: a fresh copy reads back the same links and hoses.
        w.HoseUse(1, st, reel, handsFree: true);
        var bw = new PacketWriter();
        ConstructNet.WriteWorldLinks(bw, w);
        var copy = new ConstructWorld();
        var rd = new PacketReader(bw.Span[1..]);
        try
        {
            ConstructNet.Read(ref rd, copy, 0);
        }
        catch (Exception e)
        {
            Fail($"world links: the wire didn't read back: {e.Message}");
        }
        if (copy.HoseCount != w.HoseCount || copy.WorldLinkCount != w.WorldLinkCount)
            Fail("world links: the wire round trip lost links or hoses");
        // No allocation per tick with links and a plugged hose.
        w.Hands = id => id == 1 ? ConstructWorld.HosePoint(st, reel) : null;
        for (var t = 0; t < 30; t++)
            w.Step(RoutesGround, World.TickDt);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var t = 0; t < 60; t++)
            w.Step(RoutesGround, World.TickDt);
        var alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        Line($"world links: {alloc} bytes allocated over 60 ticks with a hose out");
        if (alloc > 0)
            Fail($"world links: the tick allocates ({alloc} B over 60 ticks)");
    }

    /// <summary>A real host and a separate client: the client routes a world link and plugs a hose over the network; it and a late joiner mirror the server's.</summary>
    private static void WorldLinksLoopback()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"world links: the {ConstructYardMap} map is missing");
            return;
        }
        const ushort port = 26297;
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
            Fail("world links loopback: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        // A switch and a lamp pinned on open ground, beside each other; a fuel station and a tank with an inlet.
        var spot = OpenSpot(server.Constructs, sp.Position);
        var w = server.Constructs;
        Construct? Place(string json, Vector3 at)
        {
            var bp = ConstructBuild.ParseBlueprint(json, "loopback");
            var c = ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, at.X, at.Z), 0, out var why);
            if (c == null)
            {
                Fail($"world links loopback: a test construct didn't build: {why}");
                return null;
            }
            c.Pinned = true;
            c.Rebuild();
            return c;
        }
        if (Place(OnePart("Switch"), spot) is not { } sw || Place(OnePart("Lamp"), spot + new Vector3(3, 0, 0)) is not { } lamp
            || Place(OnePart("Fuel Tank", RouteStep("Fluid Hose Reel", 0, "+y", "-y")), spot + new Vector3(0, 0, 3)) is not { } st
            || Place(OnePart("Fuel Tank", RouteStep("Fluid Hose Inlet", 0, "+x", "-z")), spot + new Vector3(3, 0, 3)) is not { } tank)
            return;
        Pump(server, [a], 0.4, ref now, clock);
        // The client routes a data cable with a node on the ground (the linking tool out, standing by the lamp).
        server.SandboxHands.Hand(a.LocalId).Tool = ConstructTool.Linker;
        a.SandboxHands.Hand(a.LocalId).Tool = ConstructTool.Linker;
        sp.Teleport(ConstructWorld.Ground(World.Query, spot.X + 1.5f, spot.Z - 1.5f));
        Pump(server, [a], 0.3, ref now, clock);
        var mid = ConstructWorld.Ground(World.Query, spot.X + 1.5f, spot.Z) + new Vector3(0, ConstructRoutes.Radius(RouteType.Data), 0);
        Span<WNode> nodes = [new WNode { Pos = ConstructWorld.Quantise(mid) }];
        var why = server.Constructs.CheckWorldLink(World.Query, RouteType.Data, sw, 0, 255, nodes, lamp, 0, 255);
        a.SandboxSendWorldRoute(RouteType.Data, sw.Id, 0, 255, nodes, lamp.Id, 0, 255);
        Pump(server, [a], 0.5, ref now, clock);
        var routed = w.WorldLinkCount == 1;
        // The client takes the nozzle (empty hands), walks it over and plugs it in.
        server.SandboxHands.Hand(a.LocalId).Held = ConstructParts.None;
        sp.Teleport(ConstructWorld.Ground(World.Query, spot.X + 1, spot.Z + 2));
        Pump(server, [a], 0.3, ref now, clock);
        var reel = PartNamed(st, "Fluid Hose Reel");
        var inlet = PartNamed(tank, "Fluid Hose Inlet");
        a.SandboxSend(CActKind.Use, body: st.Id, part: (byte)reel);
        Pump(server, [a], 0.4, ref now, clock);
        var heldOnClient = a.Constructs.HoseHeldBy(a.LocalId) >= 0;
        sp.Teleport(ConstructWorld.Ground(World.Query, spot.X + 2.2f, spot.Z + 2));
        Pump(server, [a], 0.3, ref now, clock);
        a.SandboxSend(CActKind.Use, body: tank.Id, part: (byte)inlet);
        Pump(server, [a], 0.5, ref now, clock);
        var plugged = w.HoseCount == 1 && w.Hoses[0].Plugged;
        string Mirror(Game client)
        {
            var cw = client.Constructs;
            if (cw.WorldLinkCount != w.WorldLinkCount || cw.WorldNodeCount != w.WorldNodeCount || cw.HoseCount != w.HoseCount)
                return $"{cw.WorldLinkCount} links, {cw.WorldNodeCount} nodes, {cw.HoseCount} hoses (the server: {w.WorldLinkCount}, {w.WorldNodeCount}, {w.HoseCount})";
            for (var k = 0; k < w.WorldLinkCount; k++)
            {
                for (var i = -1; i <= w.WorldLinks[k].Count; i++)
                    if (!w.WorldPoint(k, i, out var ps) || !cw.WorldPoint(k, i, out var pc) || Vector3.Distance(ps, pc) > 0.02f)
                        return $"world link {k}'s point {i} differs";
            }
            for (var k = 0; k < w.HoseCount; k++)
                if (cw.Hoses[k].BodyOut != w.Hoses[k].BodyOut || cw.Hoses[k].BodyIn != w.Hoses[k].BodyIn || cw.Hoses[k].Holder != w.Hoses[k].Holder)
                    return $"hose {k} differs";
            return "";
        }
        var mirrorA = Mirror(a);
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a, b], 1.5, ref now, clock);
        var mirrorB = Mirror(b);
        Line($"world links loopback: the client routed a data cable over the ground ({why ?? "checks"}): made {routed}; took the nozzle (its copy shows it held {heldOnClient}) and plugged it into the tank {plugged}; "
             + $"the client mirrors them: {(mirrorA.Length == 0 ? "yes" : mirrorA)}; the late joiner: {(mirrorB.Length == 0 ? "yes" : mirrorB)}; notice \"{a.SandboxNotice}\"");
        if (!routed || !plugged || !heldOnClient)
            Fail("world links loopback: the client's world link or hose didn't go through the server");
        if (mirrorA.Length > 0 || mirrorB.Length > 0)
            Fail("world links loopback: a client (or a late joiner) doesn't mirror the world links and hoses");
    }

    /// <summary>A spot on open ground near <paramref name="near"/>, 6 m clear of every construct.</summary>
    private static Vector3 OpenSpot(ConstructWorld w, Vector3 near)
    {
        for (var ring = 4; ring < 60; ring += 3)
            for (var k = 0; k < 12; k++)
            {
                var a = k * MathF.Tau / 12;
                var p = near + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * ring;
                var clear = true;
                foreach (var c in w.Bodies)
                    if (Vector3.Distance(c.WorldCom with { Y = 0 }, p with { Y = 0 }) < c.Radius + 6f)
                        clear = false;
                var g = ConstructWorld.Ground(World.Query, p.X, p.Z);
                var g2 = ConstructWorld.Ground(World.Query, p.X + 3, p.Z + 3);
                if (clear && MathF.Abs(g.Y - g2.Y) < 0.05f && MathF.Abs(g.Y - ConstructWorld.Ground(World.Query, p.X + 3, p.Z).Y) < 0.05f)
                    return g;
            }
        return near;
    }
}
