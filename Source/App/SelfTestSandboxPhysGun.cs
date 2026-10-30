using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Sandbox physics gun" (board item dev-sandbox-physics-gun-pg01; docs/sandbox/CONSTRUCTS.md "The physics gun"), on
    /// the Construct Yard in a bare construct world: the tool's model loads; a grabbed part is pulled to a point and held
    /// there in the air; the wheel's distance moves it out; a turn (snapped to 45 degree steps) is reached; the snap maths;
    /// a frozen part stays put against gravity; unfreeze-all frees only the player's own; two players can't hold one thing;
    /// players, anchored and too heavy things are refused; the frozen state round-trips through the construct's structure;
    /// survival's contested rule refuses the physics gun, the welder and the linking tool on a defended construct and lets
    /// them once the owner is away; nothing allocates (unused or holding); and over a loopback a client grabs, steers and
    /// freezes on the server while the host's beam shows on the client.
    /// </summary>
    private static void CheckSandboxPhysGun()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"physics gun: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        var keepRadius = ConstructGrab.ContestRadius;
        try
        {
            PhysGunOffline();
            PhysGunContested();
            PhysGunLoopback(yard);
        }
        finally
        {
            ConstructGrab.ContestRadius = keepRadius;
        }
    }

    private static (Vector3 Dir, float Yaw) Aim(Vector3 eye, Vector3 at)
    {
        var d = Vector3.Normalize(at - eye);
        return (d, MathF.Atan2(d.X, -d.Z));
    }

    private static void PhysGunOffline()
    {
        var model = ConstructTools.For(ConstructTool.PhysicsGun);
        var wheel = SandboxToolWheel.ToolOf((int)SandboxToolWheel.Slot.PhysicsGun);
        Line($"physics gun: model {(model == null ? "missing" : $"{model.Name}, {model.Boxes.Length} boxes")}, on the tool wheel's {SandboxToolWheel.Slot.PhysicsGun} slot: {wheel}; wire byte {(byte)ConstructTool.PhysicsGun} reads back {ConstructToolWire.From((byte)ConstructTool.PhysicsGun)}");
        if (model == null || wheel != ConstructTool.PhysicsGun || ConstructToolWire.From((byte)ConstructTool.PhysicsGun) != ConstructTool.PhysicsGun || ConstructToolWire.From(77) != ConstructTool.None)
            Fail("physics gun: its model, its tool wheel slot or its tool byte is wrong");

        var w = new ConstructWorld();
        var hands = new ConstructEdits();
        var block = ConstructParts.Need("Frame Block");
        if (OpenSpot(w) is not { } spot)
        {
            Fail("physics gun: no open spot on the yard");
            return;
        }
        var dt = World.TickDt;
        var a = ConstructBuild.PlaceNew(w, block, spot, 0)!;
        var b = ConstructBuild.PlaceNew(w, block, spot + new Vector3(3f, 0, 0), 0)!;
        for (var t = 0; t < 30; t++)
            w.Step(World.Query, dt);
        hands.Hand(0).Tool = ConstructTool.PhysicsGun;
        hands.Hand(1).Tool = ConstructTool.PhysicsGun;
        var eye = a.WorldCom + new Vector3(-4f, 1.2f, 0);
        var (dir, yaw) = Aim(eye, a.WorldCom);
        var bare = new ConstructEdits();
        var noTool = bare.Grab(w, 0, eye, yaw, a.Id, a.Com);
        var grab = hands.Grab(w, 0, eye, yaw, a.Id, a.Com);
        // Pull it up and to the side: the beam's point 2 m higher and 1 m across, as far out as it was.
        var dist = hands.Hand(0).GrabDist;
        var goal = eye + Vector3.Normalize(a.WorldCom + new Vector3(0, 2f, 1f) - eye) * dist;
        (dir, yaw) = Aim(eye, goal);
        void Ticks(int n)
        {
            for (var t = 0; t < n; t++)
            {
                hands.GrabTick(w, 0, eye, dir, yaw);
                hands.GrabTick(w, 1, eye + new Vector3(0, 0, 2), dir, yaw);
                w.Step(World.Query, dt);
            }
        }
        Ticks(90);
        var off = Vector3.Distance(a.ToWorld(a.Com), goal);
        var up = a.WorldCom.Y - spot.Y;
        Line($"physics gun: bare-handed grab refused (\"{noTool}\"), grabbed {grab ?? "ok"}; pulled to the point within {off:0.000} m, {up:0.00} m up in the air");
        if (noTool == null || grab != null || off > 0.12f || up < 1.5f || a.GrabbedBy != 0)
            Fail("physics gun: a grabbed part wasn't pulled onto the beam's point and held there");

        // The wheel: 3 m further out.
        hands.SteerGrab(0, dist + 3f, hands.Hand(0).GrabRel);
        Ticks(60);
        var outAt = Vector3.Distance(eye, a.ToWorld(a.Com));
        // A turn: a little over 45 degrees round and tipped forward, snapped to whole 45 degree steps.
        var rel0 = hands.Hand(0).GrabRel;
        var turned = ConstructGrab.Snap(ConstructGrab.Turn(rel0, 0.9f, 0.4f));
        hands.SteerGrab(0, dist + 3f, turned);
        Ticks(90);
        var wantRot = ConstructGrab.YawQ(yaw) * turned;
        var rotDot = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a.Rot), Quaternion.Normalize(wantRot)));
        // The snap maths: steps of 45 degrees, a snapped turn stays put, three 30 degree nudges with the step make one step.
        var snapped = ConstructGrab.IsSnapped(turned) && !ConstructGrab.IsSnapped(ConstructGrab.Turn(Quaternion.Identity, 0.3f, 0));
        var stays = MathF.Abs(Quaternion.Dot(ConstructGrab.Snap(turned), turned)) > 0.9999f;
        var step = ConstructGrab.Snap(ConstructGrab.Turn(Quaternion.Identity, ConstructGrab.SnapStep, 0));
        var stepOk = MathF.Abs(Quaternion.Dot(step, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4))) > 0.9999f;
        Line($"physics gun: the wheel moved it out to {outAt:0.00} m (want {dist + 3f:0.00}); turned and snapped, it reached the turn (dot {rotDot:0.0000}); snapped turns {snapped}, a snapped one stays {stays}, one step is 45 degrees {stepOk}");
        if (MathF.Abs(outAt - (dist + 3f)) > 0.15f || rotDot < 0.999f || !snapped || !stays || !stepOk)
            Fail("physics gun: the wheel's distance or the snapped turn wasn't reached");

        // Freeze: it stays where it is against gravity, tinted (frozen flag), anchored.
        var held = a.WorldCom;
        var froze = hands.Freeze(w, 0);
        Ticks(120);
        var drift = Vector3.Distance(a.WorldCom, held);
        Line($"physics gun: froze it {froze ?? "ok"} ({held.Y - spot.Y:0.00} m up): after 2 s it moved {drift * 1000:0.0} mm, frozen {a.Frozen} by {a.FrozenBy}, anchored {a.Anchored}, let go {hands.Hand(0).GrabBody == 0 && a.GrabbedBy == 255}");
        if (froze != null || drift > 0.001f || !a.Frozen || a.FrozenBy != 0 || !a.Anchored || hands.Hand(0).GrabBody != 0 || held.Y - spot.Y < 1.5f)
            Fail("physics gun: a frozen part didn't stay in the air where it was frozen");

        // Two players can't hold one thing; the second player's freeze is theirs.
        var e1 = b.WorldCom + new Vector3(-4f, 1.2f, 0);
        var (d1, y1) = Aim(e1, b.WorldCom);
        var g1 = hands.Grab(w, 1, e1, y1, b.Id, b.Com);
        var g0 = hands.Grab(w, 0, e1, y1, b.Id, b.Com);
        _ = d1;
        hands.SteerGrab(1, hands.Hand(1).GrabDist, hands.Hand(1).GrabRel);
        for (var t = 0; t < 20; t++)
        {
            hands.GrabTick(w, 1, e1, d1, y1);
            w.Step(World.Query, dt);
        }
        var f1 = hands.Freeze(w, 1);
        // Unfreeze-all: player 0's only.
        var freed = hands.UnfreezeAll(w, 0, everyone: false);
        var aFree = !a.Frozen && !a.Anchored;
        var bStill = b.Frozen && b.FrozenBy == 1;
        var y0 = a.WorldCom.Y;
        for (var t = 0; t < 40; t++)
            w.Step(World.Query, dt);
        var fell = y0 - a.WorldCom.Y;
        // Grabbing a frozen one unfreezes it.
        var regrab = hands.Grab(w, 1, e1, y1, b.Id, b.Com);
        var thawed = !b.Frozen && b.GrabbedBy == 1;
        hands.Release(w, 1);
        Line($"physics gun: player 1 holds the other block ({g1 ?? "ok"}), player 0 refused (\"{g0}\"); player 0's unfreeze-all freed {freed} (theirs free {aFree}, player 1's still frozen {bStill}); theirs then fell {fell:0.00} m; grabbing a frozen one {regrab ?? "ok"} unfroze it {thawed}");
        if (g1 != null || g0 == null || !g0.Contains("someone else") || f1 != null || freed != 1 || !aFree || !bStill || fell < 0.2f || regrab != null || !thawed)
            Fail("physics gun: two players held one thing, or unfreeze-all reached someone else's, or a frozen one didn't unfreeze when grabbed");

        // Refusals: a seat in use (players), anchored, too heavy (survival's cap).
        b.Driver = 3;
        var inIt = hands.GrabRefusal(b, 0);
        b.Driver = 255;
        b.Pinned = true;
        b.Rebuild();
        var pinned = hands.GrabRefusal(b, 0);
        b.Pinned = false;
        b.Rebuild();
        hands.InfiniteParts = false;
        var mass = b.Mass;
        b.Mass = 900;
        var heavy = hands.GrabRefusal(b, 0);
        b.Mass = mass;
        var light = hands.GrabRefusal(b, 0);
        hands.InfiniteParts = true;
        Line($"physics gun: refused \"{inIt}\", \"{pinned}\", \"{heavy}\"; a 30 kg part in survival {light ?? "ok"}");
        if (inIt == null || pinned == null || heavy == null || !heavy.Contains("heavy") || light != null)
            Fail("physics gun: a construct with someone in it, an anchored one or one too heavy wasn't refused");

        // The frozen state round-trips through the structure (world saving isn't built: this is what a save would hold).
        hands.Grab(w, 1, e1, y1, b.Id, b.Com);
        hands.Freeze(w, 1);
        var pw = new PacketWriter(16384);
        ConstructNet.WriteFull(pw, b);
        var copy = new ConstructWorld();
        var r = new PacketReader(pw.Span[1..]);
        ConstructNet.Read(ref r, copy, 0);
        var cb = copy.Get(b.Id);
        ConstructNet.WriteFull(pw, a);
        r = new PacketReader(pw.Span[1..]);
        ConstructNet.Read(ref r, copy, 0);
        var ca = copy.Get(a.Id);
        var trip = cb is { Frozen: true, FrozenBy: 1, Anchored: true } && ca is { Frozen: false, FrozenBy: 255 } && Vector3.Distance(cb.Pos, b.Pos) < 1e-4f;
        Line($"physics gun: frozen state through the structure: {(cb == null ? "missing" : $"frozen {cb.Frozen} by {cb.FrozenBy}, anchored {cb.Anchored}")}; the unfrozen one {(ca == null ? "missing" : $"frozen {ca.Frozen}")}: {trip}");
        if (!trip)
            Fail("physics gun: the frozen state didn't round-trip");

        // No allocation: unused (nobody holds anything), and while holding.
        hands.UnfreezeAll(w, 1, everyone: true);
        for (var t = 0; t < 30; t++)
            Ticks(1);
        long Alloc()
        {
            var least = long.MaxValue;
            for (var rep = 0; rep < 3; rep++)
            {
                var b0 = GC.GetAllocatedBytesForCurrentThread();
                Ticks(30);
                least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - b0);
            }
            return least;
        }
        var unused = Alloc();
        hands.Grab(w, 0, eye, yaw, a.Id, a.ToLocal(a.WorldCom));
        var holding = Alloc();
        Line($"physics gun: allocation over 30 ticks: unused {unused} bytes, holding {holding} bytes");
        if (unused > 0 || holding > 0)
            Fail("physics gun: the construct step allocates with the physics gun (unused or holding)");
    }

    private static void PhysGunContested()
    {
        var w = new ConstructWorld();
        var hands = new ConstructEdits { InfiniteParts = false };
        var block = ConstructParts.Need("Frame Block");
        if (OpenSpot(w) is not { } spot)
            return;
        var c = ConstructBuild.PlaceNew(w, block, spot, 0)!;
        c.Rebuild();
        c.Owner = 5;
        c.Damaged = true; // (disabled: salvage would otherwise be refused first)
        var ownerAt = spot + new Vector3(10f, 0, 0);
        hands.AliveAt = id => id == 5 ? ownerAt : id == 0 ? spot : null;
        hands.NameOf = id => id == 5 ? "Rival" : "Me";
        ref var h = ref hands.Hand(0);
        var eye = spot + new Vector3(-2f, 1.5f, 0);
        var (_, yaw) = Aim(eye, c.WorldCom);
        h.Tool = ConstructTool.PhysicsGun;
        var gun = hands.Grab(w, 0, eye, yaw, c.Id, c.Com);
        h.Tool = ConstructTool.Welder; // (a frame block comes off with the welder)
        var wrench = hands.BeginRemove(w, 0, eye, c.Id, 0, 0);
        hands.Cancel(0);
        h.Tool = ConstructTool.Linker;
        var linker = hands.Unwire(w, 0, eye, c.Id, 0);
        // Creative: unrestricted.
        hands.InfiniteParts = true;
        h.Tool = ConstructTool.PhysicsGun;
        var creative = hands.Grab(w, 0, eye, yaw, c.Id, c.Com);
        hands.Release(w, 0);
        hands.InfiniteParts = false;
        // The owner far off (or dead): it can be taken.
        ownerAt = spot + new Vector3(ConstructGrab.ContestRadius + c.Radius + 5f, 0, 0);
        var away = hands.Grab(w, 0, eye, yaw, c.Id, c.Com);
        hands.Release(w, 0);
        // The map's constructs and your own are never contested.
        c.Owner = 255;
        var nobodys = hands.Contested(c, 0);
        c.Owner = 0;
        var mine = hands.Contested(c, 0);
        Line($"physics gun, contested (survival): physics gun \"{gun}\", welder \"{wrench}\", linking tool \"{linker}\"; creative {creative ?? "ok"}; the owner away {away ?? "ok"}; nobody's {nobodys ?? "ok"}, your own {mine ?? "ok"}");
        if (gun != "Owned by Rival - defended" || wrench != gun || linker != gun || creative != null || away != null || nobodys != null || mine != null)
            Fail("physics gun: the contested rule didn't refuse the tools on a defended enemy construct (or refused an undefended one)");
    }

    /// <summary>A real host and client: the client grabs a loose part, steers it, freezes it; the host's beam reaches the client.</summary>
    private static void PhysGunLoopback(Map yard)
    {
        const ushort port = 26377;
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
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp) || server.Local is not { } host)
        {
            Fail("physics gun loopback: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        if (!host.Alive)
            server.SelfTestRespawn(host, now);
        var loose = server.Constructs.Bodies.Where(b => b.Loose).ToList();
        if (loose.Count < 1)
        {
            Fail("physics gun loopback: the yard has no loose part");
            return;
        }
        var part = loose[0];
        sp.Teleport(ConstructWorld.Ground(World.Query, part.WorldCom.X - 3f, part.WorldCom.Z));
        a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.PhysicsGun);
        Pump(server, [a], 0.4, ref now, clock);
        a.SandboxGrab(part.Id, part.Com);
        Pump(server, [a], 0.4, ref now, clock);
        var held = part.GrabbedBy == a.LocalId && server.SandboxHands.Hand(a.LocalId).GrabBody == part.Id;
        // The host can't take it from them.
        server.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.PhysicsGun);
        var hostTry = server.SandboxHands.Grab(server.Constructs, host.Id, part.WorldCom + new Vector3(0, 0, 2), 0, part.Id, part.Com);
        // Steer it further out and turned; then freeze.
        var d0 = server.SandboxHands.Hand(a.LocalId).GrabDist;
        var rel = ConstructGrab.Snap(ConstructGrab.Turn(server.SandboxHands.Hand(a.LocalId).GrabRel, MathF.PI / 4, 0));
        for (var k = 0; k < 10; k++)
        {
            a.SandboxSteer(d0 + 1.5f, rel, now);
            Pump(server, [a], 0.05, ref now, clock);
        }
        var steered = MathF.Abs(server.SandboxHands.Hand(a.LocalId).GrabDist - (d0 + 1.5f)) < 0.02f && MathF.Abs(Quaternion.Dot(server.SandboxHands.Hand(a.LocalId).GrabRel, rel)) > 0.999f;
        Pump(server, [a], 0.5, ref now, clock);
        a.SandboxLetGo(freeze: true);
        Pump(server, [a], 0.5, ref now, clock);
        var copy = a.Constructs.Get(part.Id);
        var frozen = part.Frozen && part.FrozenBy == a.LocalId && copy is { Frozen: true } && Vector3.Distance(copy.Pos, part.Pos) < 0.02f;
        // The host grabs another thing: the client sees the host's beam (its hand in the tail).
        var other = server.Constructs.Bodies.FirstOrDefault(b => b != part && b.Loose && !b.Anchored);
        var hostBeam = false;
        if (other != null)
        {
            host.Teleport(ConstructWorld.Ground(World.Query, other.WorldCom.X - 3f, other.WorldCom.Z));
            Pump(server, [a], 0.3, ref now, clock);
            server.SandboxGrab(other.Id, other.Com); // (the host's own request: straight into the server's hands)
            Pump(server, [a], 0.5, ref now, clock);
            hostBeam = other.GrabbedBy == host.Id && a.SandboxHands.Hand(host.Id).GrabBody == other.Id && Vector3.Distance(a.SandboxHands.Hand(host.Id).GrabLocal, other.Com) < 0.011f;
            server.SandboxLetGo(freeze: true); // (and the host's freeze)
            Pump(server, [a], 0.3, ref now, clock);
            hostBeam &= other.Frozen && other.FrozenBy == host.Id && other.GrabbedBy == 255 && server.SandboxHands.Hand(host.Id).GrabBody == 0;
        }
        // The client's double-tap: unfreeze-all frees it.
        a.SandboxUnfreezeAll(everyone: false);
        Pump(server, [a], 0.5, ref now, clock);
        var thawed = !part.Frozen && a.Constructs.Get(part.Id) is { Frozen: false };
        Line($"physics gun loopback: the client holds the part on the server {held}; the host refused (\"{hostTry}\"); steered {steered}; frozen on both {frozen}; the host's beam on the client and its own freeze {hostBeam}; unfreeze-all {thawed} (\"{a.SandboxNotice}\")");
        if (!held || hostTry == null || !steered || !frozen || !hostBeam || !thawed)
            Fail("physics gun loopback: grab, steer, freeze or unfreeze didn't go through the server, or the beam didn't reach the other machine");
    }
}
