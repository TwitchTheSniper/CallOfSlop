using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Wheel orders": every order of building a wheel on a suspension gives a working sprung wheel, and reach is measured where a part
    /// really is. The held part through the player's own path (the menu's pick, the ghost, the click): a suspension on each face of a
    /// frame, then a wheel on it: the guess keeps the axle level, so the wheel stands (it used to lie flat on most faces and never
    /// rolled); the welder's orders on a buggy with a corner off, driven against the wrench's buggy; a part beyond a
    /// freeform weld, whose numbers are its old piece's, attached to from beside it, by a client over the real transport, on a frozen
    /// construct and riding a moving one. Seeded, scripted; settling waits for the state with a deadline.
    /// </summary>
    private static void CheckWheelOrders()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail("wheel orders: the Construct Yard map is missing");
            return;
        }
        var q = new DriveWorld();
        var bp = ConstructBuild.FindBlueprint("Buggy")!;
        var refWorld = new ConstructWorld();
        var refCar = DriveSpawn(refWorld, bp, 0, 0, MathF.PI / 2, "the buggy")!;
        if (WwMeasure(refWorld, q, refCar, "wrench (blueprint)") is not { } reference)
            return;
        WoHeldFaces(yard, sb);
        WoWelds(q, reference);
        WoWire(yard, sb, bp);
    }

    /// <summary>Aims at a socket on the view's own copy, through the player's frame. False, with why: no ghost.</summary>
    private static string WoAim(Game view, Vector3 eye, Vector3 target, bool click, double now)
    {
        var dir = Vector3.Normalize(target - eye);
        var vp = Matrix4x4.CreateLookAt(eye, eye + dir, Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(1.2f, 16f / 9f, 0.1f, 100f);
        var look = Program.SandboxHeldFrame(view, eye, dir, new Vector2(1920, 1080), vp, false, now, out var sel, out var valid);
        if (sel < 0 || !valid)
            return $"no ghost (selected {sel}, valid {valid}, HUD \"{look}\")";
        if (click)
            Program.SandboxHeldFrame(view, eye, dir, new Vector2(1920, 1080), vp, true, now + 0.02, out _, out _);
        return "";
    }

    private static string WoClick(Game g, ref double now, Player me, Construct c, int part, int socket, ConstructPartDef held)
    {
        PlaceHold(g, me.Id, held);
        g.SandboxSend(CActKind.Tool, part: (byte)held.Tool);
        Run(g, ref now, 0.2);
        c.SocketAt(part, socket, out var at, out var dir);
        c.WorldFrame(c.Parts[part].Seg, out var q, out _);
        var outward = Vector3.Transform(ConstructParts.DirVector(dir), q);
        var target = c.SegToWorld(c.Parts[part].Seg, at) - outward * 0.03f; // (just inside the part, from the side it faces)
        StandBy(me, target, 2.5f, target - outward);
        Run(g, ref now, 0.2);
        var before = c.PartCount;
        if (!g.Constructs.Raycast(me.RenderEye, Vector3.Normalize(target - me.RenderEye), 6f, out var seen) || seen.Body != c)
        {
            c.SolidBox(part, 0, out var middle, out _);
            target = c.SegToWorld(c.Parts[part].Seg, middle); // (the part's middle: nothing of it at the socket from here)
        }
        var why = WoAim(g, me.RenderEye, target, true, now);
        if (why.Length > 0)
            return why;
        Run(g, ref now, held.AttachSeconds + 0.4);
        return c.PartCount == before + 1 ? "" : $"the click attached nothing (\"{g.SandboxNotice}\")";
    }

    private static int WoAxle(ConstructPartDef d) => Array.FindIndex(d.Sockets, s => s.Kind == SocketKind.Axle);

    /// <summary>A suspension on a frame held up in the air, by the ghost's own guess, then a wheel on its axle: it is a raycast wheel, and springs when let go.</summary>
    private static void WoHeldFaces(Map yard, SandboxMap sb)
    {
        yard.RestoreProps();
        World.SetMap(yard);
        sb.DebrisSeconds = 0;
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        Run(g, ref now, Game.CountdownSeconds + 0.3);
        g.RequestRespawn();
        Run(g, ref now, 1.0);
        if (g.Local is not { Alive: true } me || OpenSpot(g.Constructs) is not { } spot)
        {
            Fail("wheel orders: no live player or open ground on the yard");
            return;
        }
        var w = g.Constructs;
        var block = ConstructParts.Need("Frame Block");
        var wheel = ConstructParts.Need("Wheel");
        var all = ConstructParts.All.Where(d => d.Role == PartRole.Suspension && d.Class == ConstructParts.Small).ToList();
        (ConstructPartDef Def, string[] Faces)[] cases = all.Select(d => (d, d.Name is "Suspension" or "Double Wishbone" or "Pogo Strut" or "Swing Arm" or "Trailing Arm" or "Leaf Spring"
            ? new[] { "+x", "-x", "+z", "-z", "+y", "-y" } : new[] { "-x" })).ToArray();
        int tried = 0, good = 0, upright = 0;
        var problems = new List<string>();
        foreach (var (s, faces) in cases)
        foreach (var face in faces)
        {
            tried++;
            var what = $"{s.Name} on the frame's {face} face";
            me.Teleport(spot + new Vector3(0, 0.05f, 2.5f));
            Run(g, ref now, 0.1);
            if (w.Create(spot + new Vector3(0, 2.6f, 0), Quaternion.Identity) is not { } c)
            {
                problems.Add($"{what}: no room in the world");
                break;
            }
            w.AddPart(c, block, Vector3.Zero, 0);
            c.Frozen = true;
            c.Owner = me.Id;
            c.Rebuild();
            var why = WoClick(g, ref now, me, c, 0, Array.FindIndex(block.Sockets, x => x.Name == face), s);
            if (why.Length == 0)
            {
                var axle = WoAxle(s);
                if (SmartRotation.AxleUp(s, c.Parts[1].Rot))
                {
                    // (a bracket's mount is opposite its axle: on a top or bottom face it can only be a spring foot. Any turn that keeps the axle level must have been the guess)
                    var level = false;
                    for (var v = 0; v < s.Sockets.Length * 4 && !level; v++)
                        level = SnapBuild.OwnOrder(s).IndexOf((byte)(v / 4)) >= 0 && ConstructBuild.Place(c, 0, Array.FindIndex(block.Sockets, x => x.Name == face), s, v / 4, v % 4, out var gv) is null or ConstructBuild.InTheWay && !SmartRotation.AxleUp(s, gv.Rot);
                    if (level)
                        why = $"its axle stands upright (turn {c.Parts[1].Rot}), though it could lie level";
                    else
                        upright++;
                    w.Remove(c);
                    if (why.Length > 0)
                        problems.Add($"{what}: {why}");
                    else
                        good++;
                    continue;
                }
                why = WoClick(g, ref now, me, c, 1, axle, wheel);
                if (why.Contains("Doesn't fit"))
                    why = WoClick(g, ref now, me, c, 1, axle, ConstructParts.Need("Small Wheel")); // (a short arm on one block: the big wheel meets the block)
            }
            if (why.Length == 0)
            {
                c.Frozen = false;
                c.Rebuild();
                c.Wake();
                Run(g, ref now, 1.2);
                if (c.WheelCount != 1)
                    why = $"{c.WheelCount} raycast wheels";
                else if (!c.Wheels[0].Touching || c.Wheels[0].Compression <= 0.001f)
                {
                    c.SocketAt(1, WoAxle(s), out _, out var axd);
                    // (the frame's corner or the strut may reach the ground first on this face: a wheel is all this checks then, and that it isn't flat)
                    if (MathF.Abs(ConstructParts.DirVector(axd).Y) > 0.5f)
                        why = "the wheel lies flat";
                }
            }
            if (why.Length == 0)
                good++;
            else
                problems.Add($"{what}: {why}");
            w.Remove(c);
        }
        Line($"wheel orders: held path: suspension then wheel on {good} of {tried} faces and kinds ({upright} can only stand as a spring foot there)");
        foreach (var p in problems.Take(8))
            Fail($"wheel orders: {p}");
    }

    private sealed class WoCorner
    {
        public ConstructWorld W = new();
        public Construct Car = null!;
        public ConstructPartDef Susp = null!;
        public int Beam, BeamSocket, MountSocket;
        public ConstructEdits Hands = null!;
        public Vector3 Eye;

        public int FreeSuspension()
        {
            var axle = WoAxle(Susp);
            for (var i = 0; i < Car.PartCount; i++)
                if (Car.DefOf(i) == Susp && !Car.SocketUsed(i, axle))
                    return i;
            return -1;
        }
    }

    /// <summary>The wrench's buggy with one driven wheel off, and its suspension too when <paramref name="noSuspension"/>.</summary>
    private static WoCorner? WoCar(IWorldQuery q, ConstructBuild.Blueprint bp, bool noSuspension)
    {
        var o = new WoCorner();
        if (DriveSpawn(o.W, bp, 0, 0, MathF.PI / 2, "the buggy") is not { } car)
            return null;
        o.Car = car;
        WwSettle(o.W, q, car);
        var k = 0;
        while (k < car.WheelCount && !car.Wheels[k].Driven)
            k++;
        int wheel = car.Wheels[k].Part, susp = car.Wheels[k].Suspension;
        o.Susp = car.DefOf(susp);
        o.W.RemovePart(car, wheel);
        if (susp > wheel)
            susp--;
        for (var l = 0; l < car.LinkCount; l++)
        {
            ref var li = ref car.Links[l];
            if (li.A == susp)
                (o.Beam, o.BeamSocket, o.MountSocket) = (li.B, li.SB, li.SA);
            else if (li.B == susp)
                (o.Beam, o.BeamSocket, o.MountSocket) = (li.A, li.SA, li.SB);
        }
        car.Rebuild();
        o.Hands = FwHands();
        car.SocketAt(o.Beam, o.BeamSocket, out var at, out _);
        o.Eye = car.ToWorld(at) + new Vector3(0, 1.4f, 0);
        if (noSuspension)
        {
            o.W.RemovePart(car, susp);
            if (o.Beam > susp)
                o.Beam--;
            car.Rebuild();
        }
        return o;
    }

    /// <summary>The corner is back on: four wheels, the car springs on them from a drop and drives.</summary>
    private static void WoJudge(string what, WoCorner o, string? why, IWorldQuery q, in WwResult reference, float distShare = 0.25f)
    {
        var car = o.Car;
        if (why != null)
        {
            Fail($"wheel orders ({what}): refused: {why}");
            return;
        }
        car.Rebuild();
        if (car.WheelCount != reference.Wheels)
        {
            Fail($"wheel orders ({what}): {car.WheelCount} raycast wheels, not {reference.Wheels}");
            return;
        }
        if (WwMeasure(o.W, q, car, what) is not { } r)
            return;
        Line($"wheel orders ({what}): {car.WheelCount} wheels, rests {r.Rest * 100:0.0} cm in (wrench {reference.Rest * 100:0.0}), a drop {r.Peak * 100:0.0} cm ({reference.Peak * 100:0.0}), 1 s of throttle {r.Dist:0.0} m ({reference.Dist:0.0}), driven wheels {r.Spin:0.0} rad/s");
        if (r.Rest < 0.005f || r.Peak < r.Rest + 0.01f || r.Spin < 3f || MathF.Abs(r.Dist - reference.Dist) > distShare * reference.Dist + 0.1f || MathF.Sign(r.Yaw) != MathF.Sign(reference.Yaw))
            Fail($"wheel orders ({what}): doesn't spring, roll and steer like the wrench's (rest {r.Rest}, peak {r.Peak}, {r.Dist} m, spin {r.Spin}, yaw {r.Yaw}; wrench {reference.Rest}, {reference.Peak}, {reference.Dist} m, yaw {reference.Yaw})");
    }

    private static void WoWelds(IWorldQuery q, in WwResult reference)
    {
        var bp = ConstructBuild.FindBlueprint("Buggy")!;
        var wheelDef = ConstructParts.Need("Wheel");
        var wax = WoAxle(wheelDef);
        // The wheel by the welder onto the suspension on the car, on and off the grid, frozen or not.
        foreach (var frozen in new[] { false, true })
        foreach (var spin in new[] { 0f, 37f })
        {
            if (WoCar(q, bp, false) is not { } o)
                return;
            var susp = o.FreeSuspension();
            if (FwLoose(o.W, "Wheel", o.Eye + new Vector3(1, -0.3f, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f), frozen: true) is not { } loose)
                return;
            o.Car.Frozen = frozen;
            o.Car.Rebuild();
            var target = new WeldTarget { Body = o.Car.Id, Part = (ushort)susp, Socket = (byte)WoAxle(o.Susp) };
            var why = o.Hands.BeginSocketWeld(o.W, 0, o.Eye, loose.Id, 0, wax, target, spin, 0);
            o.Car.Frozen = false;
            o.Car.Rebuild();
            o.Car.Wake();
            WoJudge($"wheel socket-attached at {spin:0} degrees{(frozen ? ", frozen" : "")}", o, why, q, reference);
        }
        // Freeform: let go near the axle.
        foreach (var (off, tilt) in new[] { (0.04f, 4f), (0.2f, 15f) })
        {
            if (WoCar(q, bp, false) is not { } o)
                return;
            var susp = o.FreeSuspension();
            if (FwLoose(o.W, "Wheel", Vector3.Zero, Quaternion.Identity, frozen: true) is not { } loose)
                return;
            var axle = WoAxle(o.Susp);
            var target = new WeldTarget { Body = o.Car.Id, Part = (ushort)susp, Socket = (byte)axle };
            SocketWeld.Pose(loose, 0, wax, o.Car, target, 37, out _, out _, out var gp, out var gq);
            o.Car.SocketAt(susp, axle, out _, out var dir);
            var outward = Vector3.Transform(ConstructParts.DirVector(dir), o.Car.Rot);
            loose.Pos = loose.PrevPos = gp + outward * off;
            loose.Rot = loose.PrevRot = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(0.3f, 1, 0.2f)), tilt * MathF.PI / 180) * gq);
            WoJudge($"wheel freeform, {off * 100:0} cm off", o, o.Hands.BeginFreeform(o.W, 0, o.Eye, loose.Id, 0, o.Car.Id, susp, 0), q, reference);
        }
        // Point attach: the wheel's inner face onto the suspension's outer face at the axle (they line up: it is that socket weld).
        if (WoCar(q, bp, false) is { } po)
        {
            var susp = po.FreeSuspension();
            var axle = WoAxle(po.Susp);
            if (FwLoose(po.W, "Wheel", po.Eye + new Vector3(1, -0.3f, 0), Quaternion.Identity, frozen: true) is { } loose)
            {
                loose.SocketAt(0, wax, out var wAt, out var wDir);
                po.Car.SocketAt(susp, axle, out var sAt, out var sDir);
                po.Car.WorldFrame(po.Car.Parts[susp].Seg, out var sq, out _);
                var from = PointWeld.OriginAt(loose, 0, loose.ToWorld(wAt), Vector3.Transform(ConstructParts.DirVector(wDir), loose.Rot), out var src);
                var onto = SocketWeld.FaceAt(po.Car, susp, po.Car.SegToWorld(po.Car.Parts[susp].Seg, sAt), Vector3.Transform(ConstructParts.DirVector(sDir), sq), out var target);
                WoJudge("wheel point-attached", po, !from || !onto ? "no flat face at the axle" : po.Hands.BeginPointWeld(po.W, 0, po.Eye, loose.Id, src, target, 0, 0), q, reference);
            }
        }
        // Wheel first: the suspension and wheel as a piece, onto the car.
        foreach (var (mode, spin) in new[] { ("socket", 0f), ("socket", 90f), ("socket", 15f), ("freeform", 0f) })
        {
            if (WoCar(q, bp, true) is not { } o)
                return;
            if (FwLoose(o.W, "Wheel", o.Eye + new Vector3(1.5f, -0.3f, 0), Quaternion.Identity, frozen: true) is not { } piece)
                return;
            var why = ConstructBuild.Place(piece, 0, wax, o.Susp, WoAxle(o.Susp), 0, out var gh);
            if (why == null)
                ConstructBuild.Attach(o.W, piece, o.Susp, gh);
            piece.Rebuild();
            var target = new WeldTarget { Body = o.Car.Id, Part = (ushort)o.Beam, Socket = (byte)o.BeamSocket };
            if (mode == "socket")
                why ??= o.Hands.BeginSocketWeld(o.W, 0, o.Eye, piece.Id, 1, o.MountSocket, target, spin, 0);
            else
            {
                SocketWeld.Pose(piece, 1, o.MountSocket, o.Car, target, spin, out _, out _, out var gp, out var gq);
                piece.Pos = piece.PrevPos = gp + new Vector3(0, 0.01f, 0);
                piece.Rot = piece.PrevRot = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.2f) * gq);
                why ??= o.Hands.BeginFreeform(o.W, 0, o.Eye, piece.Id, 1, o.Car.Id, o.Beam, 0);
            }
            WoJudge($"wheel, then suspension, then {mode} onto the car at {spin:0} degrees", o, why, q, reference, 0.7f); // (an off-grid corner may lose the drive line: it must still spring and roll)
        }
        // The suspension welded on at an angle, then the wheel by the wrench: its travel is still straight up and down.
        foreach (var tilt in new[] { 15f, 90f })
        {
            if (WoCar(q, bp, true) is not { } o)
                return;
            if (FwLoose(o.W, o.Susp.Name, o.Eye + new Vector3(1.5f, -0.3f, 0), Quaternion.Identity, frozen: true) is not { } loose)
                return;
            var target = new WeldTarget { Body = o.Car.Id, Part = (ushort)o.Beam, Socket = (byte)o.BeamSocket };
            SocketWeld.Pose(loose, 0, o.MountSocket, o.Car, target, 0, out _, out _, out var gp, out var gq);
            loose.Pos = loose.PrevPos = gp + new Vector3(0, 0.01f, 0);
            loose.SocketAt(0, WoAxle(o.Susp), out _, out var axd); // (about its own axle: the axle stays level, the strut leans)
            loose.Rot = loose.PrevRot = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.Transform(ConstructParts.DirVector(axd), gq), tilt * MathF.PI / 180) * gq);
            var why = o.Hands.BeginFreeform(o.W, 0, o.Eye, loose.Id, 0, o.Car.Id, o.Beam, 0);
            if (why == null)
            {
                o.Car.Rebuild();
                why = ConstructBuild.Place(o.Car, o.FreeSuspension(), WoAxle(o.Susp), wheelDef, wax, 0, out var gh);
                if (why == null)
                    ConstructBuild.Attach(o.W, o.Car, wheelDef, gh);
            }
            WoJudge($"suspension welded on at {tilt:0} degrees about its axle, then the wheel", o, why, q, reference, 0.7f);
        }
        // Swapping a wheel later.
        {
            if (WoCar(q, bp, false) is { } o)
            {
                var other = ConstructParts.Need("Off-Road Wheel");
                var why = ConstructBuild.Place(o.Car, o.FreeSuspension(), WoAxle(o.Susp), wheelDef, wax, 0, out var gh);
                if (why == null)
                {
                    var k = ConstructBuild.Attach(o.W, o.Car, wheelDef, gh);
                    o.Car.Rebuild();
                    o.W.RemovePart(o.Car, k);
                    o.Car.Rebuild();
                    why = ConstructBuild.Place(o.Car, o.FreeSuspension(), WoAxle(o.Susp), other, WoAxle(other), 0, out gh);
                    if (why == null)
                        ConstructBuild.Attach(o.W, o.Car, other, gh);
                }
                WoJudge("a wheel swapped", o, why, q, reference);
            }
        }
    }

    /// <summary>A client over the real transport: a wheel on the buggy's bare suspension, and reach beyond a freeform weld: frozen, and on a moving construct.</summary>
    private static void WoWire(Map yard, SandboxMap sb, ConstructBuild.Blueprint bp)
    {
        const ushort port = 26357;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        sb.DebrisSeconds = 0;
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now);
        Pump(server, [a], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Fail("wheel orders: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        Pump(server, [a], 0.3, ref now, clock);
        var w = server.Constructs;
        if (OpenSpot(w) is not { } spot)
        {
            Fail("wheel orders: no open level ground on the yard");
            return;
        }
        var wheel = ConstructParts.Need("Wheel");

        // A wheel on the buggy's bare suspension, by a client.
        if (ConstructBuild.BuildOnGround(w, bp, spot, 0, out var refused) is not { } car)
        {
            Fail($"wheel orders: the buggy wasn't built ({refused})");
            return;
        }
        Pump(server, [a], 1.0, ref now, clock);
        var kw = 0;
        var part = car.Wheels[kw].Part;
        w.RemovePart(car, part);
        car.Rebuild();
        car.Owner = a.LocalId;
        var susp = -1;
        for (var i = 0; i < car.PartCount; i++)
            if (car.DefOf(i).Role == PartRole.Suspension && !car.SocketUsed(i, WoAxle(car.DefOf(i))))
                susp = i;
        w.StructureDirty.Add(car.Id);
        Pump(server, [a], 0.5, ref now, clock);
        var why = WoClientAttach(server, a, sp, car, susp, WoAxle(car.DefOf(susp)), wheel, ref now, clock);
        var copy = a.Constructs.Get(car.Id);
        Line($"wheel orders: over the wire, a wheel on the buggy's suspension: {(why.Length == 0 ? "attached" : why)}; {car.WheelCount} raycast wheels, the client's copy {copy?.WheelCount}");
        if (why.Length > 0 || car.WheelCount != 4 || copy == null || copy.WheelCount != 4)
            Fail($"wheel orders: a client's wheel on a suspension: {why}, {car.WheelCount} wheels, the client's copy {copy?.WheelCount}");
        for (var t = 0; t < 40; t++)
            Pump(server, [a], 0.03, ref now, clock);
        var compressed = true;
        for (var k = 0; k < car.WheelCount; k++)
            compressed &= car.Wheels[k].Touching && car.Wheels[k].Compression > 0.005f;
        if (!compressed)
            Fail("wheel orders: the buggy with the client's wheel doesn't stand on its springs");
        w.Remove(car);
        Pump(server, [a], 0.3, ref now, clock);

        // A frame with a beam two freeform welds along: its numbers are its old piece's, near this construct's origin. From beside it.
        var susDef = ConstructParts.Need("Suspension");
        foreach (var how in new[] { "frozen", "free", "riding it" })
        {
            var moving = how != "frozen";
            var riding = how == "riding it";
            var at = spot + new Vector3(3, moving ? 0.6f : 1.8f, -3);
            if (FwLoose(w, "Frame Block", at, Quaternion.Identity, frozen: !moving) is not { } first)
                return;
            var body = first;
            var hands = FwHands();
            string? fw = null;
            for (var n = 1; n <= 3 && fw == null; n++)
            {
                if (FwLoose(w, "Square Beam 2 m", at + new Vector3(3f * n, 0, 0), Quaternion.Identity, frozen: true) is not { } next)
                    return;
                body.Rebuild();
                var end = 0; // (the chain's last part so far, whichever piece kept its body)
                for (var i = 1; i < body.PartCount; i++)
                    if (body.SegToWorld(body.Parts[i].Seg, body.Parts[i].Pos).X > body.SegToWorld(body.Parts[end].Seg, body.Parts[end].Pos).X)
                        end = i;
                fw = hands.BeginFreeform(w, 0, at + new Vector3(3f * n - 1.5f, 1, 0), body.Id, end, next.Id, 0, 0);
                body = w.Get(body.Id) ?? next;
            }
            body.Rebuild();
            if (fw != null || body.PartCount != 4)
            {
                Fail($"wheel orders: the freeform chain wasn't welded: {fw}, {body.PartCount} parts");
                return;
            }
            // (the part furthest from the construct's origin: its own numbers are small, so they alone say it is near the origin)
            var farPart = 0;
            for (var i = 1; i < body.PartCount; i++)
                if (Vector3.Distance(body.SegToWorld(body.Parts[i].Seg, body.Parts[i].Pos), body.Pos) > Vector3.Distance(body.SegToWorld(body.Parts[farPart].Seg, body.Parts[farPart].Pos), body.Pos))
                    farPart = i;
            body.Owner = a.LocalId;
            w.StructureDirty.Add(body.Id);
            Pump(server, [a], 0.5, ref now, clock);
            var socket = Array.FindIndex(body.DefOf(farPart).Sockets, s => s.Name.StartsWith("+z") || s.Name.StartsWith("-z"));
            body.SocketAt(farPart, socket, out var sAt, out _);
            var world = body.SegToWorld(body.Parts[farPart].Seg, sAt);
            if (riding)
            {
                // Standing on the part next to it, carried along.
                var near = -1;
                for (var i = 0; i < body.PartCount; i++)
                    if (i != farPart && (near < 0 || Vector3.Distance(body.SegToWorld(body.Parts[i].Seg, body.Parts[i].Pos), world) < Vector3.Distance(body.SegToWorld(body.Parts[near].Seg, body.Parts[near].Pos), world)))
                        near = i;
                sp.Teleport(body.SegToWorld(body.Parts[near].Seg, body.Parts[near].Pos + new Vector3(0, 0.2f, 1)));
                for (var t = 0; t < 30; t++)
                {
                    body.Vel = new Vector3(1.5f, 0, 0);
                    body.Wake();
                    Pump(server, [a], 0.03, ref now, clock);
                }
            }
            else if (moving)
            {
                body.Vel = new Vector3(1.5f, 0, 0);
                body.Wake();
            }
            Pump(server, [a], 0.3, ref now, clock);
            var rode = sp.Sim.RideBody == body.Id;
            var res = WoClientAttach(server, a, sp, body, farPart, socket, susDef, ref now, clock, stand: !riding);
            var cc = a.Local;
            var eyes = cc == null ? 99f : Vector3.Distance(cc.RenderEye, sp.Eye);
            Line($"wheel orders: over the wire, on a part beyond two freeform welds ({how}{(riding ? rode ? ", in its frame" : ", NOT in its frame" : "")}): {(res.Length == 0 ? "attached" : res)}; the client's eye {eyes:0.00} m off the server's; the server's numbers alone would be {Vector3.Distance(sp.Eye, body.ToWorld(sAt)):0.0} m away");
            if (res.Length > 0)
                Fail($"wheel orders: a part beyond a freeform weld, from beside it ({how}): {res}");
            if (eyes > 0.5f)
                Fail($"wheel orders: the client's eye and the server's differ by {eyes:0.00} m");
            w.Remove(body);
            Pump(server, [a], 0.3, ref now, clock);
        }
    }

    /// <summary>The client's own hands: the pick, the tool, the ghost on its copy, the click. The result is the server's.</summary>
    private static string WoClientAttach(Game server, Game client, Player sp, Construct c, int part, int socket, ConstructPartDef held, ref double now, Stopwatch clock, bool stand = true)
    {
        PlaceHold(server, client.LocalId, held, client);
        client.SandboxSend(CActKind.Tool, part: (byte)held.Tool);
        Pump(server, [client], 0.3, ref now, clock);
        if (client.Constructs.Get(c.Id) is not { } copy || client.Local is not { } me)
            return "the client has no copy";
        copy.SocketAt(part, socket, out var at, out var dir);
        copy.WorldFrame(copy.Parts[part].Seg, out var fq, out _);
        var outward = Vector3.Transform(ConstructParts.DirVector(dir), fq);
        var target = copy.SegToWorld(copy.Parts[part].Seg, at) - outward * 0.03f; // (just inside the part, from the side it faces)
        if (stand)
            StandBy(sp, target, 2f, target - outward);
        Pump(server, [client], 0.4, ref now, clock);
        if (client.Constructs.Get(c.Id) is { } moved && !stand)
        {
            moved.SocketAt(part, socket, out at, out _);
            target = moved.SegToWorld(moved.Parts[part].Seg, at) - outward * 0.03f; // (it has moved on)
        }
        var before = c.PartCount;
        var why = WoAim(client, me.RenderEye, target, true, now);
        if (why.Length > 0)
            return why;
        Pump(server, [client], held.AttachSeconds + 0.6, ref now, clock);
        return c.PartCount == before + 1 ? "" : $"the click attached nothing (\"{client.SandboxNotice}\", server \"{server.SandboxNotice}\")";
    }
}
