using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Nodes;

namespace CallOfSlop;

// THROWAWAY: the agent's hand check of control modules. Removed before the final push.
public static partial class SelfTest
{
    private static (int Part, int Socket) SmFree(Construct c, ConstructPartDef def, int ds, Func<Vector3, bool>? where = null)
    {
        var best = (-1, -1);
        var bestY = float.MinValue;
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            if (d.Role != PartRole.Frame || !c.Rigid(c.Parts[i].Seg))
                continue;
            for (var s = 0; s < d.Sockets.Length; s++)
            {
                if (c.SocketUsed(i, s))
                    continue;
                c.SocketAt(i, s, out var at, out var dir);
                if (ConstructParts.DirVector(dir).Y < 0.7f || where != null && !where(at))
                    continue;
                if (ConstructBuild.Place(c, i, s, def, ds, 0, out _) == null && at.Y > bestY)
                {
                    bestY = at.Y;
                    best = (i, s);
                }
            }
        }
        return best;
    }

    private static int SmFit(ConstructWorld w, Construct c, string part, string by, Func<Vector3, bool>? where = null)
    {
        var def = ConstructParts.Need(part);
        var ds = Array.FindIndex(def.Sockets, s => s.Name == by);
        var (on, sock) = SmFree(c, def, ds, where);
        if (on < 0)
        {
            Fail($"seatmods: no free socket for {part}");
            return -1;
        }
        ConstructBuild.Place(c, on, sock, def, ds, 0, out var g);
        var i = ConstructBuild.Attach(w, c, def, g);
        c.Rebuild();
        return i;
    }

    private static void CheckSeatMods()
    {
        var yard = Maps.Find(ConstructYardMap)!;
        const ushort port = 26451;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 6), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        using var e = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Echo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Game[] all = [a, b, e];
        Pump(server, all, 1.5, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            Pump(server, all, 0.25, ref now, clock);
        Pump(server, all, 0.5, ref now, clock);
        foreach (var g in all)
            if (!server.Players.TryGetValue(g.LocalId, out var pl))
                Fail("seatmods: no join");
            else if (!pl.Alive)
                server.SelfTestRespawn(pl, now);
        Pump(server, all, 0.3, ref now, clock);
        var simHost = server.Net.SimulateBadNetwork(50, 10, 2);
        var w = server.Constructs;
        var yardCar = w.Bodies.First(x => x.CanDrive);
        var carAt = yardCar.Pos;
        var carFwd = Vector3.Transform(yardCar.DriveFwd, yardCar.Rot);
        var carYaw = MathF.Atan2(-carFwd.X, -carFwd.Z);
        Line($"seatmods: yard car at {carAt} fwd {carFwd}");
        foreach (var body in w.Bodies.ToArray())
            if (!body.Anchored)
                w.Remove(body); // (room to drive)
        Pump(server, all, 0.3, ref now, clock);
        var spot = ConstructWorld.Ground(World.Query, 0, 0);
        void Sit(Game g, Construct c, int seat)
        {
            var at = ConstructEdits.SeatFeet(c, seat);
            server.Players[g.LocalId].Teleport(at + new Vector3(0, 0.2f, 0));
            Pump(server, all, 0.3, ref now, clock);
            g.SandboxSend(CActKind.Use, body: c.Id, part: (byte)seat);
            Pump(server, all, 0.6, ref now, clock);
        }
        void Stand(Game g)
        {
            g.HeldButtons = Buttons.None;
            g.SandboxSend(CActKind.Stand);
            Pump(server, all, 0.5, ref now, clock);
        }
        Construct? Build(ConstructBuild.Blueprint bp, Vector3 at)
        {
            var c = ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, carAt.X + at.X, carAt.Z + at.Z), carYaw, out var why);
            if (c == null)
                Fail($"seatmods: {bp.Name} didn't build: {why}");
            return c;
        }
        // 1. A buggy through its module, client driving.
        var buggy = Build(ConstructBuild.FindBlueprint("Buggy")!, Vector3.Zero);
        Pump(server, all, 1.0, ref now, clock);
        var copy = a.Constructs.Get(buggy!.Id);
        var seat0 = PartNamed(buggy, "Seat");
        Sit(a, buggy, seat0);
        var p0 = buggy.WorldCom;
        a.HeldButtons = Buttons.Forward;
        Pump(server, all, 2.0, ref now, clock);
        var moved = Vector3.Distance(p0, buggy.WorldCom);
        Line($"seatmods 1 dbg: throttle {buggy.Throttle} running {buggy.EngineRunning} rpm {buggy.Rpm} asleep {buggy.Asleep} candrive {buggy.CanDrive} problem [{buggy.DriveProblem}] note {buggy.Note} wired {buggy.EngineWired} fuel {buggy.Fuel} input {server.Players[a.LocalId].LastInput.Buttons} keys {buggy.Parts[seat0].Keys}");
        Line($"seatmods 1 buggy: drive seat host {buggy.DriveSeat} client {copy?.DriveSeat}, driver {buggy.Driver} (client {a.LocalId}), moved {moved:0.0} m, hud client {copy?.Driver}");
        if (buggy.DriveSeat != seat0 || copy?.DriveSeat != seat0 || moved < 1 || buggy.Driver != a.LocalId)
            Fail("seatmods: buggy didn't drive through its module");
        Stand(a);
        w.Remove(buggy);
        // 2. No module: nothing, with a reason; then rewired by hand.
        var path = Path.Combine(AppContext.BaseDirectory, "Constructs", "Blueprints", "Buggy.json");
        var tree = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        tree["steps"]!.AsArray().RemoveAt(tree["steps"]!.AsArray().Count - 1);
        tree.Remove("wires");
        var bare = ConstructBuild.ParseBlueprint(tree.ToJsonString(), "bare.json");
        var noMod = Build(bare, Vector3.Zero);
        Pump(server, all, 1.0, ref now, clock);
        var seat1 = PartNamed(noMod!, "Seat");
        var nmCopy = a.Constructs.Get(noMod!.Id);
        Line($"seatmods 2 no module: host: {ConstructModules.Problem(noMod, seat1)} / client: {(nmCopy == null ? "?" : ConstructModules.Problem(nmCopy, seat1))} / describe: {ConstructDrive.Describe(bare)}");
        Sit(a, noMod, seat1);
        p0 = noMod.WorldCom;
        a.HeldButtons = Buttons.Forward;
        Pump(server, all, 1.5, ref now, clock);
        moved = Vector3.Distance(p0, noMod.WorldCom);
        var engineOn = noMod.EngineRunning;
        a.HeldButtons = Buttons.None;
        Line($"seatmods 2: seated {a.SandboxHands.Hand(a.LocalId).Seated} driver {noMod.Driver}, moved {moved:0.00} m, engine {engineOn}");
        if (moved > 0.3f || noMod.Driver != 255 || ConstructModules.Problem(noMod, seat1) == null || nmCopy != null && ConstructModules.Problem(nmCopy, seat1) == null)
            Fail("seatmods: a seat with no module drove, or gave no reason");
        Stand(a);
        var mod = SmFitOn(w, noMod, seat1, "+x", "Driving Module", "-x");
        var engine = noMod.Engine;
        var why = ConstructBuild.CanWire(w, noMod, seat1, mod);
        w.AddWire(noMod, seat1, mod);
        var why2 = ConstructBuild.CanWire(w, noMod, mod, engine);
        w.AddWire(noMod, mod, engine);
        Line($"seatmods 2 rewired: wire refusals [{why}] [{why2}] drive seat {noMod.DriveSeat}, seat problem [{ConstructModules.Problem(noMod, seat1)}], routes {noMod.RouteCount} into port {noMod.Routes[noMod.RouteCount - 1].ToPort}");
        // A second wire refused at the drive port from something else.
        var refuse = ConstructRoutes.Check(w, noMod, RouteType.Data, seat1, 255, 255, 0, default, default, engine, ConstructRoutes.DataPortByte(ConstructModules.BusPort(noMod.DefOf(engine))));
        var refuse2 = ConstructRoutes.Check(w, noMod, RouteType.Data, mod, 255, 255, 0, default, default, seat1, 255);
        var refuse3 = ConstructRoutes.Check(w, noMod, RouteType.Data, engine, 255, 255, 0, default, default, mod, 255);
        Line($"seatmods 2 refusals: [{refuse}] [{refuse2}] [{refuse3}]");
        if (refuse == null || refuse3 == null)
            Fail("seatmods: refusals missing");
        Sit(a, noMod, seat1);
        p0 = noMod.WorldCom;
        a.HeldButtons = Buttons.Forward;
        Pump(server, all, 1.5, ref now, clock);
        moved = Vector3.Distance(p0, noMod.WorldCom);
        Line($"seatmods 2 rewired drove {moved:0.0} m");
        if (moved < 1)
            Fail("seatmods: the rewired buggy didn't drive");
        // The link cut under the driver: they stop driving.
        w.RemoveWires(noMod, mod);
        Pump(server, all, 0.3, ref now, clock);
        Line($"seatmods 2 link cut: driver {noMod.Driver}, throttle {noMod.Throttle}");
        if (noMod.Driver != 255)
            Fail("seatmods: still the driver with the link cut");
        Stand(a);
        // 2b. Wired by hand: throttle, steer, run.
        w.Remove(noMod);
        noMod = Build(bare, Vector3.Zero)!;
        Pump(server, all, 1.0, ref now, clock);
        mod = SmFitOn(w, noMod, seat1, "+x", "Driving Module", "-x");
        engine = noMod.Engine;
        var pivot = -1;
        for (var i = 0; i < noMod.PartCount; i++)
            if (noMod.DefOf(i).JointType == JointType.Steering && pivot < 0)
                pivot = i;
        w.AddWire(noMod, seat1, mod);
        var pivots = 0;
        ConstructRoutes.Add(w, noMod, RouteType.Data, mod, ConstructRoutes.DataPortByte(6), 255, 0, default, default, engine, 255);
        ConstructRoutes.Add(w, noMod, RouteType.Data, mod, ConstructRoutes.DataPortByte(2), 255, 0, default, default, engine, ConstructRoutes.DataPortByte(1));
        for (var i = 0; i < noMod.PartCount; i++)
            if (noMod.DefOf(i).JointType == JointType.Steering)
            {
                ConstructRoutes.Add(w, noMod, RouteType.Data, mod, ConstructRoutes.DataPortByte(1), 255, 0, default, default, i, 255);
                pivots++;
            }
        noMod.Rebuild();
        Pump(server, all, 0.3, ref now, clock);
        Sit(a, noMod, seat1);
        p0 = noMod.WorldCom;
        a.HeldButtons = Buttons.Forward | Buttons.Right;
        var yaw0 = World.Forward(0, 0);
        var f0 = Vector3.Transform(noMod.DriveFwd, noMod.Rot);
        Pump(server, all, 2.0, ref now, clock);
        var f1 = Vector3.Transform(noMod.DriveFwd, noMod.Rot);
        moved = Vector3.Distance(p0, noMod.WorldCom);
        var turn = MathF.Atan2(Vector3.Cross(f0, f1).Y, Vector3.Dot(f0, f1)) * 180 / MathF.PI;
        Line($"seatmods 2b by hand ({pivots} pivots): drive seat {noMod.DriveSeat}, control seat {noMod.ControlSeat}, wired {noMod.EngineWired} signal {noMod.EngineSignal}, moved {moved:0.0} m, turned {turn:0.0} deg");
        if (moved < 1)
            Fail("seatmods: hand wired buggy didn't drive");
        Stand(a);
        a.HeldButtons = Buttons.None;
        w.Remove(noMod);
        // 3. A tank on a steering differential.
        var tank = Swap(ConstructBuild.FindBlueprint("Tracked Crawler")!, ("Wide Differential", "Wide Steering Differential"));
        var crawl = Build(tank, Vector3.Zero);
        Pump(server, all, 1.0, ref now, clock);
        var seat2 = PartNamed(crawl!, "Seat");
        Sit(a, crawl!, seat2);
        p0 = crawl!.WorldCom;
        a.HeldButtons = Buttons.Forward;
        Pump(server, all, 2.5, ref now, clock);
        moved = Vector3.Distance(p0, crawl.WorldCom);
        a.HeldButtons = Buttons.Right;
        Pump(server, all, 0.5, ref now, clock);
        f0 = Vector3.Transform(crawl.DriveFwd, crawl.Rot);
        Pump(server, all, 2.0, ref now, clock);
        f1 = Vector3.Transform(crawl.DriveFwd, crawl.Rot);
        turn = MathF.Atan2(Vector3.Cross(f0, f1).Y, Vector3.Dot(f0, f1)) * 180 / MathF.PI;
        Line($"seatmods 3 tank: steer diff {crawl.SteerDiff}, moved {moved:0.0} m, D turned {turn:0.0} deg (negative: clockwise, right)");
        if (!crawl.SteerDiff || moved < 1 || turn > -3)
            Fail("seatmods: the tank didn't drive or steer the right way");
        Stand(a);
        w.Remove(crawl);
        // 4. A truck, a driver and two gunners on two turrets.
        var truck = Build(ConstructBuild.FindBlueprint("Tracked Crawler")!, Vector3.Zero);
        c4(truck!);
        void c4(Construct t)
        {
            var driver = t.DriveSeat;
            bool Clear(Vector3 at)
            {
                for (var i = 0; i < t.PartCount; i++)
                {
                    t.PartBounds(i, out var mn, out var mx);
                    if (mx.Y > at.Y + 0.05f && mn.X < at.X + 0.5f && mx.X > at.X - 0.5f && mn.Z < at.Z + 0.8f && mx.Z > at.Z - 0.8f)
                        return false;
                }
                return true;
            }
            var post = SmFit(w, t, "Frame Beam", "end-");
            var ts0 = Array.FindIndex(t.DefOf(post).Sockets, s => s.Name == "end+");
            Line($"seatmods 4 post at {t.Parts[post].Pos} sockets {string.Join(" ", t.DefOf(post).Sockets.Select(s => s.Name))}");
            var cross = SmFitOn(w, t, post, "end+", "Frame Beam", "y+1");
            Line($"seatmods 4 cross at {t.Parts[cross].Pos} rot {t.Parts[cross].Rot} free top: {string.Join(" ", Enumerable.Range(0, t.DefOf(cross).Sockets.Length).Where(s => !t.SocketUsed(cross, s)).Select(s => { t.SocketAt(cross, s, out var at, out var dir); return $"{t.DefOf(cross).Sockets[s].Name}:{at.X:0.0},{at.Y:0.0},{at.Z:0.0}/{dir}"; }))}");
            var ta = SmFit(w, t, "Turntable", "base", Clear);
            var ka = SmFitOn(w, t, ta, "top", "Frame Block", "-y");
            var gun1 = SmFitOn(w, t, ka, "+y", "Mounted MG", "bottom");
            var box1 = SmFitOn(w, t, gun1, "back", "Ammo Box", "front");
            var tb = SmFit(w, t, "Turntable", "base", at => Clear(at) && Vector3.Distance(at, t.Parts[ta].Pos) > 1.2f);
            var kb = SmFitOn(w, t, tb, "top", "Frame Block", "-y");
            var gun2 = SmFitOn(w, t, kb, "+y", "Mounted MG", "bottom");
            var box2 = SmFitOn(w, t, gun2, "back", "Ammo Box", "front");
            var s1 = SmFit(w, t, "Seat", "bottom", at => Vector3.Distance(at, t.Parts[ta].Pos) > 0.7f && Vector3.Distance(at, t.Parts[tb].Pos) > 0.7f);
            var s2 = SmFit(w, t, "Seat", "bottom", at => Vector3.Distance(at, t.Parts[ta].Pos) > 0.7f && Vector3.Distance(at, t.Parts[tb].Pos) > 0.7f && Vector3.Distance(at, t.Parts[s1].Pos) > 0.6f);
            var m1 = SmFitOn(w, t, s1, "+x", "Turret Module", "-x");
            var m2 = SmFitOn(w, t, s2, "-x", "Turret Module", "+x");
            w.AddWire(t, s1, m1);
            w.AddWire(t, m1, gun1);
            w.AddWire(t, s2, m2);
            w.AddWire(t, m2, gun2);
            t.Rebuild();
            t.Parts[gun1].Rounds = 0;
            Pump(server, all, 1.5, ref now, clock);
            var cc = b.Constructs.Get(t.Id);
            Line($"seatmods 4 built: guns {t.GunCount} (seats {string.Join(",", Enumerable.Range(0, t.GunCount).Select(k => t.Guns[k].Seat))}, aim joints {string.Join(",", Enumerable.Range(0, t.GunCount).Select(k => t.Guns[k].AimJoints))}, problems [{t.Guns[0].Problem}]), turret mods host {t.TurretModCount} client {cc?.TurretModCount}, kinds {t.SeatKindOf(driver)}/{t.SeatKindOf(s1)}/{t.SeatKindOf(s2)}, ok seats {s1},{s2} mods {m1},{m2} guns {gun1},{gun2}");
            Sit(a, t, driver);
            Sit(b, t, s1);
            Sit(e, t, s2);
            Line($"seatmods 4 seated: driver {t.Driver}, a {a.LocalId}, Parts sitters {t.Parts[s1].SitterPlus1 - 1},{t.Parts[s2].SitterPlus1 - 1} b {b.LocalId} e {e.LocalId}");
            int JointOf(int part) => t.Parts[part].Seg - 1;
            var ja = JointOf(gun1);
            var jb = JointOf(gun2);
            var a0 = t.Joints[ja].Angle;
            var b0 = t.Joints[jb].Angle;
            var r0 = (t.Parts[gun1].Rounds, t.Parts[gun2].Rounds, t.Parts[box1].Rounds, t.Parts[box2].Rounds);
            var seatYaw = t.Parts[s1].AimYaw;
            b.LookYaw = World.WrapAngle(b.LookYaw + 1.4f);
            b.SandboxSend(CActKind.SeatKeys, part: 1);
            p0 = t.WorldCom;
            a.HeldButtons = Buttons.Forward;
            Pump(server, all, 2.0, ref now, clock);
            b.SandboxSend(CActKind.SeatKeys, part: 0);
            a.HeldButtons = Buttons.Jump;
            Pump(server, all, 0.6, ref now, clock);
            a.HeldButtons = Buttons.None;
            var movedT = Vector3.Distance(p0, t.WorldCom);
            var turnA = t.Joints[ja].Angle - a0;
            var turnB = t.Joints[jb].Angle - b0;
            Line($"seatmods 4 gunner 1 fires and looks 80 deg off: truck moved {movedT:0.0} m, turret A {turnA * 180 / MathF.PI:0.0} deg, turret B {turnB * 180 / MathF.PI:0.0} deg, rounds gun1 {r0.Item1}->{t.Parts[gun1].Rounds} box1 {r0.Item3}->{t.Parts[box1].Rounds}, gun2 {r0.Item2}->{t.Parts[gun2].Rounds} box2 {r0.Item4}->{t.Parts[box2].Rounds}, shots {w.ShotsFired}");
            if (movedT < 1 || MathF.Abs(turnA) < 0.2f)
                Fail("seatmods: two turrets: the wrong joints turned, or the driver didn't drive");
            var fired1 = t.Parts[gun1].Rounds + t.Parts[box1].Rounds;
            var fired2 = t.Parts[gun2].Rounds + t.Parts[box2].Rounds;
            if (fired1 >= r0.Item1 + r0.Item3 || fired2 != r0.Item2 + r0.Item4)
                Fail("seatmods: the wrong gun fired");
            // Gunner 2 now.
            var e0 = t.Joints[ja].Angle;
            var f20 = fired2;
            e.LookYaw = World.WrapAngle(e.LookYaw - 1.4f);
            e.SandboxSend(CActKind.SeatKeys, part: 1);
            Pump(server, all, 2.0, ref now, clock);
            e.SandboxSend(CActKind.SeatKeys, part: 0);
            Pump(server, all, 0.4, ref now, clock);
            Line($"seatmods 4 gunner 2: turret B {(t.Joints[jb].Angle - b0) * 180 / MathF.PI:0.0} deg, gun2+box {f20}->{t.Parts[gun2].Rounds + t.Parts[box2].Rounds}");
            if (t.Parts[gun2].Rounds + t.Parts[box2].Rounds >= f20)
                Fail("seatmods: gunner 2 didn't fire");
            // 5. Seat 1 also gets turret B's module: Aim steps to it. (a second module on seat 1 wired to gun 2)
            Stand(e);
            var m3 = m2;
            w.RemoveWires(t, m2);
            w.AddWire(t, s1, m3);
            w.AddWire(t, m3, gun2);
            if (m3 >= 0)
            {
                t.Rebuild();
                Pump(server, all, 0.4, ref now, clock);
                var fg1 = t.Parts[gun1].Rounds + t.Parts[box1].Rounds;
                var fg2 = t.Parts[gun2].Rounds + t.Parts[box2].Rounds;
                b.SandboxSend(CActKind.SeatKeys, part: 2);
                Pump(server, all, 0.25, ref now, clock);
                b.SandboxSend(CActKind.SeatKeys, part: 0);
                Pump(server, all, 0.25, ref now, clock);
                b.SandboxSend(CActKind.SeatKeys, part: 1);
                Pump(server, all, 1.2, ref now, clock);
                b.SandboxSend(CActKind.SeatKeys, part: 0);
                Pump(server, all, 0.3, ref now, clock);
                var gb = b.Constructs.Get(t.Id);
                Line($"seatmods 5 switch: of {ConstructModules.TurretsOf(t, s1)}, gun1 {fg1}->{t.Parts[gun1].Rounds + t.Parts[box1].Rounds}, gun2 {fg2}->{t.Parts[gun2].Rounds + t.Parts[box2].Rounds}, client's active gun part {(gb == null ? -1 : gb.Guns[Math.Max(0, ConstructModules.ActiveGun(gb, s1))].Part)} (gun2 {gun2})");
                if (t.Parts[gun2].Rounds + t.Parts[box2].Rounds >= fg2 || t.Parts[gun1].Rounds + t.Parts[box1].Rounds < fg1)
                    Fail("seatmods: Aim didn't step to the next turret");
            }
            Stand(b);
            Stand(a);
            // 6. Save as blueprint, whole and a joiner.
            var items = new ushort[t.PartCount];
            for (var i = 0; i < items.Length; i++)
                items[i] = (ushort)i;
            var clip = ConstructClip.Capture(t, items, out var cwhy, w);
            var text = clip?.ToBlueprint("Round trip", "", out cwhy);
            if (text == null)
            {
                Fail($"seatmods: no blueprint: {cwhy}");
                return;
            }
            var rt = ConstructBuild.ParseBlueprint(text, "rt.json");
            var back = Build(rt, new Vector3(0, 0, 12));
            Line($"seatmods 6 blueprint round trip: drive seat {back?.DriveSeat} (seat kind {back?.SeatKindOf(back.DriveSeat)}), turret mods {back?.TurretModCount}, gun seats {(back == null ? "" : string.Join(",", Enumerable.Range(0, back.GunCount).Select(k => back.Guns[k].Seat)))}, routes {t.RouteCount}/{back?.RouteCount}");
            if (back == null || back.DriveSeat < 0 || back.TurretModCount != t.TurretModCount)
                Fail("seatmods: a saved blueprint lost its modules");
            // Selection: only the front half of the seats and modules, ToBlueprint from a partial set.
            var sel = new List<ushort>();
            for (var i = 0; i < t.PartCount; i++)
                if (i == driver || t.DefOf(i).IsSeatModule || t.DefOf(i).Role is PartRole.Seat)
                    sel.Add((ushort)i);
            Pump(server, all, 0.5, ref now, clock);
            using var d = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Delta", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            Game[] more = [a, b, e, d];
            Pump(server, more, 2.5, ref now, clock);
            var jc = d.Constructs.Get(t.Id);
            Line($"seatmods 6 joiner: drive seat {jc?.DriveSeat} (host {t.DriveSeat}), turret mods {jc?.TurretModCount}, gun seats {(jc == null ? "" : string.Join(",", Enumerable.Range(0, jc.GunCount).Select(k => jc.Guns[k].Seat)))}");
            if (jc == null || jc.DriveSeat != t.DriveSeat || jc.TurretModCount != t.TurretModCount)
                Fail("seatmods: a joiner lost the modules");
            // 7. World save and back.
            var session = Game.WorldNew("SeatMods", yard, false);
            server.WorldAttach(session);
            var saved = server.WorldSaveNow(wait: true);
            var entry = WorldStore.List().Count;
            Line($"seatmods 7 world save: {saved} entries {entry}");
            var opened = Game.WorldOpen(WorldStore.List()[0], out var owhy);
            Line($"seatmods 7 open: {opened != null} [{owhy}]");
            using var host2 = new Game(NetPeer.Host(26452, 4), new Audio(), "Host2", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
            host2.Mode = GameMode.Sandbox;
            host2.WorldAttach(opened);
            var tn = now;
            Pump(host2, [], 2.0, ref tn, clock);
            for (var wait = 0; wait < 40 && host2.Phase != MatchPhase.Playing; wait++)
                Pump(host2, [], 0.25, ref tn, clock);
            Pump(host2, [], 1.0, ref tn, clock);
            var driven = host2.Constructs.Bodies.Where(x => x.DriveSeat >= 0).ToList();
            var turreted = host2.Constructs.Bodies.Where(x => x.TurretModCount > 0).ToList();
            Line($"seatmods 7 loaded: phase {host2.Phase}, bodies {host2.Constructs.Bodies.Count}, with a drive seat {driven.Count}, with turrets {turreted.Count} (mods {string.Join(",", turreted.Select(x => x.TurretModCount))}, gun seats {string.Join(",", turreted.SelectMany(x => Enumerable.Range(0, x.GunCount).Select(k => x.Guns[k].Seat)))})");
            if (driven.Count == 0 || turreted.Count == 0)
                Fail("seatmods: a saved world lost its modules");
        }
    }

    private static int SmFitOn(ConstructWorld w, Construct c, int on, string socket, string part, string by)
    {
        var def = ConstructParts.Need(part);
        var ts = Array.FindIndex(c.DefOf(on).Sockets, s => s.Name == socket);
        var ds = Array.FindIndex(def.Sockets, s => s.Name == by);
        if (ts < 0 || ds < 0 || ConstructBuild.Place(c, on, ts, def, ds, 0, out var g) is { } why)
        {
            Fail($"seatmods: {part} on {c.DefOf(on).Name} {socket}: socket {ts}/{ds} {(ts >= 0 && ds >= 0 ? ConstructBuild.Place(c, on, ts, def, ds, 0, out _) : "")}");
            return -1;
        }
        var i = ConstructBuild.Attach(w, c, def, g);
        c.Rebuild();
        return i;
    }
}
