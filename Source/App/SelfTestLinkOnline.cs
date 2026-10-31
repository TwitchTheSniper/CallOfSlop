using System.Diagnostics;
using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Link tool online" (board item dev-link-tool-mp-crash-lc01): the linking tool's real per-frame input
    /// (<see cref="Program.LinkTestFrame"/>) on a real host and a separate client over the transport (simulated lag and
    /// loss), the host's and the client's tool in turn: aimed at every part of three constructs (engine, transmission,
    /// differential, tank, battery, lamp, switch, proximity sensor, frame block, seat: ports and none), at a part that does not
    /// exist (gone, or not received yet), picking every option the picker lists and starting a link from it, with the
    /// highlight on and off. Then a link in the making while the other peer takes parts off the construct (every index
    /// shifts) and deletes the link it branches from, links made and deleted with the edit modes on both sides, and the
    /// tool drawn again after Delete mode starts in route mode. The guard must never need to catch anything: any exception
    /// reaches this test (with its stack), and so does every frame that would be recovered.
    /// Reopened (lc01: the host crashed natively, no [crash] block, looking at a seat): every tool frame runs like the game's,
    /// the input outside an ImGui frame and then the tool's ImGui pass (port markers, picker) in a real headless ImGui frame
    /// with a camera on the aimed part and ImGui's error recovery on. Text asked for outside a frame (which dereferenced
    /// ImGui's null font: the crash) and any ImGui error fail the test. The seats: the wired buggy's driver and passenger
    /// seats while it rolls, a driver and a rider in them, the Gunner Turret's seat (on its turntable's segment, turning)
    /// with a gunner in it, while the host freezes, moves and lets go of a loose piece nearby.
    /// </summary>
    private static void CheckLinkOnline()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"link online: the {ConstructYardMap} map is missing");
            return;
        }
        const ushort port = 26437;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        bool WaitFor(Func<bool> ready, double seconds = 8.0)
        {
            var until = clock.Elapsed.TotalSeconds + seconds;
            while (clock.Elapsed.TotalSeconds < until)
            {
                if (ready())
                    return true;
                Pump(server, [a], 0.05, ref now, clock);
            }
            return ready();
        }
        Pump(server, [a], 1.5, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            Pump(server, [a], 0.25, ref now, clock);
        Pump(server, [a], 0.5, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Fail("link online: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        if (server.Local is { Alive: false } hostPlayer)
            server.SelfTestRespawn(hostPlayer, now);
        var simHost = server.Net.SimulateBadNetwork(50, 10, 3);
        var simClient = a.Net.SimulateBadNetwork(50, 10, 3);
        var spot = OpenSpot(server.Constructs, sp.Position);
        var w = server.Constructs;
        Construct? Place(string steps, Vector3 at)
        {
            var bp = ConstructBuild.ParseBlueprint($$"""{ "format": "call-of-slop-construct-blueprint/1", "name": "LO", "steps": [ { "part": "Foundation" }, {{steps}} ] }""", "lc01");
            var c = ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, at.X, at.Z), 0, out var why);
            if (c == null)
                Fail($"link online: a test construct didn't build: {why}");
            else
            {
                c.Pinned = true;
                c.Rebuild();
            }
            return c;
        }
        if (Place($"{RouteStep("Transmission", 0, "top00", "-y")}, {RouteStep("Engine", 0, "top30", "-y")}, {RouteStep("Differential", 0, "top02", "bottom0")}, {RouteStep("Fuel Tank", 0, "top33", "-y")}, {RouteStep("Engine", 0, "top20", "-y")}", spot) is not { } mech
            || Place($"{RouteStep("Switch", 0, "top00", "-y")}, {RouteStep("Lamp", 0, "top03", "-y")}, {RouteStep("Proximity Sensor", 0, "top33", "-y")}, {RouteStep("Battery", 0, "top30", "-y")}, {RouteStep("Frame Block", 0, "top22", "-y")}, {RouteStep("Lamp", 0, "top11", "-y")}", spot + new Vector3(6, 0, 0)) is not { } elec
            || Place($"{RouteStep("Seat", 0, "top33", "bottom")}, {RouteStep("Lamp", 0, "top30", "-y")}, {RouteStep("Frame Block", 0, "top03", "-y")}", spot + new Vector3(12, 0, 0)) is not { } seatc)
            return;
        sp.Teleport(ConstructWorld.Ground(World.Query, spot.X + 3f, spot.Z - 2f));
        server.Local?.Teleport(ConstructWorld.Ground(World.Query, spot.X + 3f, spot.Z - 3f));
        server.SandboxHands.Hand(a.LocalId).Tool = ConstructTool.Linker;
        a.SandboxHands.Hand(a.LocalId).Tool = ConstructTool.Linker;
        if (server.Local != null)
        {
            server.SandboxHands.Hand(server.LocalId).Tool = ConstructTool.Linker;
            server.SandboxHands.Hand(server.LocalId).Tool = ConstructTool.Linker;
        }
        Pump(server, [a], 0.8, ref now, clock);
        var ids = new[] { mech.Id, elec.Id, seatc.Id };
        if (ids.Any(id => a.Constructs.Get(id) == null))
        {
            Fail("link online: the client doesn't have the test constructs");
            return;
        }
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        var frames = 0;
        var picks = 0;
        var parts = 0;
        var started = 0;
        Program.LinkGuardRethrow = true;
        var trips0 = Program.LinkGuardTrips;
        // A headless ImGui context, as the game's: error recovery on (Ui.SafeErrors), a camera on what the tool aims at.
        var ctx = ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.IniFilename = default;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset | ImGuiBackendFlags.RendererHasTextures;
        io.DisplaySize = new float2(1280, 720);
        io.DisplayFramebufferScale = new float2(1, 1);
        io.DeltaTime = 1f / 60;
        Ui.SafeErrors(io);
        var outOfFrame0 = Hud.OutOfFrame;
        int imguiErrors = 0, markerFrames = 0, seatMarkerFrames = 0, drawnMarkerFrames = 0;
        string firstImguiError = "";
        // The tool's ImGui pass, after its input (which ran outside any frame, as in the game): markers and the picker.
        void DrawPass(Vector3 target, bool seat, string what)
        {
            var eye = target + new Vector3(0.3f, 0.9f, 1.8f);
            var view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(1.2f, 1280f / 720f, 0.05f, 500f);
            Hud.Camera = new HudCamera(view * proj, eye, Vector3.Normalize(target - eye), true);
            var markers = Program.LinkTestMarkers;
            ImGui.NewFrame();
            Program.LinkTestDraw();
            ImGui.Render();
            var dd = ImGui.GetDrawData();
            FakeTextureUploads(dd);
            var e0 = Ui.ImGuiErrors;
            Ui.CountImGuiErrors();
            var errors = Ui.ImGuiErrors - e0;
            if (errors > 0 && imguiErrors == 0)
                firstImguiError = what;
            imguiErrors += errors;
            if (markers > 0)
            {
                markerFrames++;
                if (seat)
                    seatMarkerFrames++;
                if (dd.TotalVtxCount > 0)
                    drawnMarkerFrames++;
            }
            ImString.SharedStorage.Reset();
        }
        try
        {
            // One frame of the tool of game g aimed at part `part` of its copy of construct `id` (a part index that doesn't exist is aimed at too).
            bool Frame(Game g, ushort id, int part, bool fire = false, bool aim = false, string what = "")
            {
                if (g.Constructs.Get(id) is not { } c || g.Local == null)
                    return false;
                var hit = new ConstructHit { Body = c, Part = part, Distance = 2f, Normal = Vector3.UnitY, Point = c.Pos + new Vector3(0, 1f, 0) };
                if (part >= 0 && part < c.PartCount)
                {
                    c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
                    hit.Point = t + Vector3.Transform(c.Parts[part].Pos, q);
                }
                ref var h = ref g.SandboxHands.Hand(g.LocalId);
                frames++;
                var seat = part >= 0 && part < c.PartCount && c.DefOf(part).Role == PartRole.Seat;
                var oof = Hud.OutOfFrame;
                try
                {
                    Program.LinkTestFrame(g, ref h, hit, fire, aim);
                    if (Hud.OutOfFrame != oof)
                        problems.Add($"{(g == server ? "host" : "client")} tool {what} at part {part} of construct {id}: HUD text asked for outside an ImGui frame (a native crash in the game)");
                    DrawPass(hit.Point, seat, $"{what} at part {part} of construct {id}");
                }
                catch (Exception ex)
                {
                    var side = g == server ? "host" : "client";
                    problems.Add($"{side} tool {what} at part {part} of construct {id} ({c.PartCount} parts): {ex.GetType().Name} {ex.Message} at {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
                    Program.LinkTestReset();
                }
                return true;
            }
            bool FrameRoute(Game g, ushort id, int route, string what)
            {
                if (g.Constructs.Get(id) is not { } c || route < 0 || route >= c.RouteCount || g.Local == null)
                    return false;
                var p0 = ConstructRoutes.Point(c, route, -1, out var s0);
                var p1 = ConstructRoutes.Point(c, route, 0, out _);
                c.WorldFrame(s0, out var q, out var t);
                var hit = new ConstructHit { Body = c, Part = c.Routes[route].Source, Distance = 2f, Normal = Vector3.UnitY, Point = t + Vector3.Transform((p0 + p1) * 0.5f, q), RoutePlus1 = (byte)(route + 1) };
                ref var h = ref g.SandboxHands.Hand(g.LocalId);
                frames++;
                try
                {
                    Program.LinkTestFrame(g, ref h, hit, true);
                    DrawPass(hit.Point, false, $"{what} on link {route}");
                }
                catch (Exception ex)
                {
                    problems.Add($"{(g == server ? "host" : "client")} tool {what} on link {route} of construct {id}: {ex.GetType().Name} {ex.Message} at {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
                    Program.LinkTestReset();
                }
                return true;
            }
            var sides = server.Local != null ? new[] { a, server } : new[] { a };
            Want(server.Local != null, "the host has no local player to draw the tool with");

            // 1) Every part of every construct, aimed at and clicked, on each side, highlight off and on.
            foreach (var g in sides)
                foreach (var highlight in new[] { false, true })
                {
                    Program.LinkTestReset();
                    if (Program.LinkTestWheel.Highlight != highlight)
                        Program.LinkTestWheel.Choose((int)SandboxLinkWheel.Option.Highlight);
                    foreach (var id in ids)
                    {
                        var c = g.Constructs.Get(id)!;
                        for (var part = -1; part < c.PartCount + 3; part++)
                        {
                            parts++;
                            Frame(g, id, part, what: "aiming");
                            Frame(g, id, part, fire: true, what: "clicking");
                            if (Program.LinkTestPicker.IsOpen)
                            {
                                var count = Program.LinkTestPickCount;
                                for (var i = 0; i < count; i++)
                                {
                                    if (!Program.LinkTestPicker.IsOpen)
                                        Frame(g, id, part, fire: true, what: "clicking again");
                                    if (!Program.LinkTestPicker.IsOpen)
                                        break;
                                    Program.LinkTestHover(i);
                                    picks++;
                                    Frame(g, id, part, fire: true, what: $"picking option {i}");
                                    if (Program.LinkTestRouting)
                                    {
                                        started++;
                                        // (the link is on: aim at every part of the construct as its end, open the end picker, cancel)
                                        for (var to = 0; to < c.PartCount; to += 2)
                                        {
                                            Frame(g, id, to, what: "aiming at an end");
                                            Frame(g, id, to, fire: true, what: "clicking an end");
                                            if (Program.LinkTestPicker.IsOpen)
                                                Frame(g, id, to, aim: true, what: "cancelling the end picker");
                                        }
                                        Program.LinkTestReset();
                                    }
                                }
                                Program.LinkTestReset();
                            }
                            Program.LinkTestReset();
                        }
                    }
                }
            if (Program.LinkTestWheel.Highlight)
                Program.LinkTestWheel.Choose((int)SandboxLinkWheel.Option.Highlight);

            // 1b) Seats (lc01 reopened): the wired buggy's driver and passenger seats, rolling, with a driver and a rider; the Gunner
            //     Turret's seat on its turning turntable, with a gunner; meanwhile the host freezes, moves and lets go of a loose
            //     piece nearby (the developer's crash: a client grabbing and freezing an I-Beam). Each peer: hover (markers),
            //     open the picker, start a data link from the seat, end links at the other seat, and links from a lamp to it.
            var car = w.Bodies.FirstOrDefault(b => b.CountRole(PartRole.Seat) == 2 && b.CanDrive);
            var turret = BuildBlueprint(w, "Gunner Turret", spot.X + 4f, spot.Z + 9f);
            var loose = Place($"{RouteStep("Frame Block", 0, "top11", "-y")}", spot + new Vector3(3, 0, 6));
            if (car == null || turret == null || loose == null)
                problems.Add($"no wired buggy on the yard or the Gunner Turret / loose piece didn't build (buggy {car != null}, turret {turret != null})");
            else
            {
                loose.Pinned = false;
                var driver = server.SelfTestAddHuman();
                var rider = server.SelfTestAddHuman();
                var gunner = server.SelfTestAddHuman();
                foreach (var p in new[] { driver, rider, gunner })
                {
                    server.SelfTestRespawn(p, now);
                    p.ImmuneUntil = 1e9;
                }
                Pump(server, [a], 0.3, ref now, clock);
                var other = PartNamed(car, "Seat", 1);
                var gunSeat = PartNamed(turret, "Seat");
                driver.Teleport(ConstructEdits.SeatFeet(car, car.DriveSeat) + new Vector3(0, 0.1f, 0));
                rider.Teleport(ConstructEdits.SeatFeet(car, other) + new Vector3(0, 0.1f, 0));
                gunner.Teleport(ConstructEdits.SeatFeet(turret, gunSeat) + new Vector3(0, 0.1f, 0));
                var why1 = server.SandboxHands.Use(w, driver.Id, driver.Eye, car.Id, car.DriveSeat);
                var why2 = server.SandboxHands.Use(w, rider.Id, rider.Eye, car.Id, other);
                var why3 = server.SandboxHands.Use(w, gunner.Id, gunner.Eye, turret.Id, gunSeat);
                Want(why1 == null && why2 == null && why3 == null && car.Driver == driver.Id, $"the test riders didn't sit (driver \"{why1}\", rider \"{why2}\", gunner \"{why3}\", buggy driver {car.Driver})");
                if (PartNamed(turret, "Lever") is var lever and >= 0)
                    turret.Parts[lever].State = 1; // (the turntable turns: the seat's segment moves)
                turret.Wake();
                Pump(server, [a], 0.6, ref now, clock);
                var seatBodies = new[] { car.Id, turret.Id };
                var looseSteps = 0;
                void Shake()
                {
                    // The host's moving world while the tool draws: the buggy rolls, the loose piece is frozen, moved and let go.
                    car.Vel = new Vector3(2.5f, 0, 0);
                    car.Wake();
                    turret.Wake();
                    looseSteps++;
                    loose.Frozen = loose.Anchored = looseSteps % 3 == 1;
                    if (loose.Frozen)
                        loose.Pos += new Vector3(0, 0.05f, 0);
                    else
                        loose.Wake();
                    w.StructureDirty.Add(loose.Id);
                    Pump(server, [a], 0.05, ref now, clock);
                }
                foreach (var g in sides)
                {
                    Program.LinkTestReset();
                    foreach (var id in seatBodies)
                    {
                        if (g.Constructs.Get(id) is not { } c)
                        {
                            problems.Add($"the {(g == server ? "host" : "client")} doesn't have seat construct {id}");
                            continue;
                        }
                        for (var part = 0; part < c.PartCount; part++)
                        {
                            if (c.DefOf(part).Role != PartRole.Seat)
                                continue;
                            for (var hover = 0; hover < 4; hover++)
                            {
                                Frame(g, id, part, what: "hovering a seat");
                                if (hover % 2 == 0)
                                    Shake();
                            }
                            Frame(g, id, part, fire: true, what: "clicking a seat");
                            for (var i = 0; i < Program.LinkTestPickCount && Program.LinkTestPicker.IsOpen; i++)
                            {
                                Program.LinkTestHover(i);
                                Frame(g, id, part, what: $"pointing at the seat's option {i}");
                            }
                            if (Program.LinkTestPicker.IsOpen)
                            {
                                Frame(g, id, part, fire: true, what: "picking the seat's port");
                                if (Program.LinkTestRouting)
                                {
                                    started++;
                                    // (a link from the seat: aimed along the construct, at the other seat as its end, then cancelled)
                                    for (var to = 0; to < c.PartCount; to++)
                                    {
                                        Frame(g, id, to, what: "routing from a seat");
                                        if (c.DefOf(to).Role == PartRole.Seat || to % 3 == 0)
                                        {
                                            Frame(g, id, to, fire: true, what: "clicking an end from a seat");
                                            if (Program.LinkTestPicker.IsOpen)
                                                Frame(g, id, to, aim: true, what: "cancelling the end picker from a seat");
                                        }
                                        if (to % 4 == 0)
                                            Shake();
                                    }
                                }
                            }
                            Program.LinkTestReset();
                            // (a link from another part of the construct ending at the seat)
                            for (var from = 0; from < c.PartCount; from++)
                            {
                                if (from == part || !c.DefOf(from).HasOutput)
                                    continue;
                                Frame(g, id, from, fire: true, what: "clicking a source for the seat");
                                if (Program.LinkTestPicker.IsOpen)
                                    Frame(g, id, from, fire: true, what: "picking a source for the seat");
                                if (Program.LinkTestRouting)
                                {
                                    Frame(g, id, part, what: "aiming at a seat as an end");
                                    Frame(g, id, part, fire: true, what: "ending at a seat");
                                    if (Program.LinkTestPicker.IsOpen)
                                        Frame(g, id, part, aim: true, what: "cancelling a seat's end picker");
                                }
                                Program.LinkTestReset();
                                break;
                            }
                            Shake();
                        }
                    }
                }
                Program.LinkTestReset();
                Want(seatMarkerFrames > 0, "the seats never showed a port marker");
                foreach (var p in new[] { driver, rider, gunner })
                    server.SandboxHands.Stand(w, p.Id);
                car.Vel = Vector3.Zero;
                Pump(server, [a], 0.3, ref now, clock);
            }

            // 2) Make a shaft, a pipe and a data cable through the picker on the client while the host adds and takes parts off the
            //    same construct; the links appear on both.
            Program.LinkTestReset();
            var cm = a.Constructs.Get(mech.Id)!;
            var cl = a.Constructs.Get(elec.Id)!;
            void Make(Construct c, string from, string option, string to, string toOption, RouteType type)
            {
                var f = PartNamed(c, from);
                var t = PartNamed(c, to);
                if (f < 0 || t < 0)
                {
                    problems.Add($"no {from} or {to} on construct {c.Id}");
                    return;
                }
                Frame(a, c.Id, f, fire: true, what: $"clicking {from}");
                var idx = -1;
                for (var i = 0; i < Program.LinkTestPickCount && Program.LinkTestPicker.IsOpen; i++)
                    if (Program.LinkTestPicker.Items[i].Label == option)
                        idx = i;
                if (idx < 0)
                {
                    problems.Add($"{from}'s picker has no \"{option}\"");
                    Program.LinkTestReset();
                    return;
                }
                Program.LinkTestHover(idx);
                Frame(a, c.Id, f, fire: true, what: $"picking {option}");
                Frame(a, c.Id, t, what: "aiming at the end");
                Frame(a, c.Id, t, fire: true, what: "clicking the end");
                if (Program.LinkTestPicker.IsOpen)
                {
                    for (var i = 0; i < Program.LinkTestPickCount; i++)
                        if (Program.LinkTestPicker.Items[i].Label == toOption)
                            Program.LinkTestHover(i);
                    Frame(a, c.Id, t, fire: true, what: $"picking {toOption}");
                }
                var before = c.RouteCount;
                if (!WaitFor(() => c.RouteCount > before && w.Get(c.Id)!.RouteCount > before))
                    problems.Add($"the {type} {from} -> {to} wasn't made ({a.SandboxNotice})");
                else
                    Want(c.Routes[c.RouteCount - 1].Type == type, $"the link made from {from} is a {c.Routes[c.RouteCount - 1].Type}, not a {type}");
                Program.LinkTestReset();
            }
            Make(cm, "Transmission", "Output shaft", "Differential", "Input shaft", RouteType.Shaft);
            Make(cm, "Fuel Tank", "Fuel out", "Engine", "Fuel in", RouteType.Pipe);
            Make(cl, "Switch", "Data out", "Lamp", "Data in", RouteType.Data);
            Make(cl, "Proximity Sensor", "Data out", "Lamp", "Data in", RouteType.Data);
            var sensorRoutes = cl.RouteCount;

            // 3) A link in the making while the other peer takes parts off (every later index shifts) or deletes the link it
            //    branches from: the next frames, aimed anywhere, must not throw.
            foreach (var removeWhat in new[] { "an earlier part", "the last part", "the parent link", "the start part" })
            {
                Program.LinkTestReset();
                var cc = a.Constructs.Get(elec.Id);
                var sc = w.Get(elec.Id);
                if (cc == null || sc == null || sc.PartCount < 4)
                    break;
                var sensor = PartNamed(cc, "Proximity Sensor");
                if (sensor < 0)
                    break;
                var parent = -1;
                if (removeWhat == "the parent link")
                {
                    for (var k = 0; k < cc.RouteCount; k++)
                        if (cc.Routes[k].Type == RouteType.Data)
                            parent = k;
                    if (parent < 0)
                        break;
                    FrameRoute(a, elec.Id, parent, "branching"); // (not in a routing state yet: the edit mode isn't on)
                }
                Frame(a, elec.Id, sensor, fire: true, what: "clicking the sensor");
                if (Program.LinkTestPicker.IsOpen)
                {
                    for (var i = 0; i < Program.LinkTestPickCount; i++)
                        if (Program.LinkTestPicker.Items[i].Enabled)
                        {
                            Program.LinkTestHover(i);
                            break;
                        }
                    Frame(a, elec.Id, sensor, fire: true, what: "picking the sensor's port");
                }
                Want(Program.LinkTestRouting, $"a link wasn't started at the sensor ({removeWhat}; sensor {sensor} of {cc.PartCount}, picker {(Program.LinkTestPicker.IsOpen ? "open" : "closed")} with {Program.LinkTestPickCount}, mode {Program.LinkTestWheel.Mode}, notice '{a.SandboxNotice}')");
                // The host takes a part off (or the link goes).
                var lampOnHost = PartNamed(sc, "Lamp");
                var partsBefore = sc.PartCount;
                var removeAt = removeWhat switch
                {
                    "the start part" => PartNamed(sc, "Proximity Sensor"),
                    "the last part" => sc.PartCount - 1,
                    _ => PartNamed(sc, "Switch"),
                };
                if (removeWhat == "the parent link")
                {
                    if (sc.RouteCount > 0)
                    {
                        ConstructRoutes.Remove(w, sc, 0);
                        sc.Rebuild();
                    }
                }
                else if (removeAt >= 0 && removeAt != sc.Root)
                {
                    w.RemovePart(sc, removeAt);
                    sc.Rebuild();
                }
                for (var settle = 0; settle < 40 && a.Constructs.Get(elec.Id) is { } seen && seen.PartCount == cc.PartCount && removeWhat != "the parent link"; settle++)
                    Pump(server, [a], 0.05, ref now, clock); // (the client's copy gets the host's change)
                Frame(a, elec.Id, 0, what: $"the first frame after {removeWhat} went");
                var stillRouting = Program.LinkTestRouting;
                for (var look = 0; look < 40; look++)
                {
                    foreach (var part in new[] { 0, 1, 2, 3, cc.PartCount - 1, cc.PartCount + 2 })
                    {
                        Frame(a, elec.Id, part, what: $"aiming after {removeWhat} went");
                        if (look % 8 == 0)
                            Frame(a, elec.Id, part, fire: true, what: $"clicking after {removeWhat} went");
                    }
                    Pump(server, [a], 0.03, ref now, clock);
                    cc = a.Constructs.Get(elec.Id);
                    if (cc == null)
                        break;
                }
                if (removeWhat is "an earlier part" or "the start part")
                    Want(!stillRouting, $"the link was still being made after {removeWhat} was taken off (host {sc.PartCount} parts, client {a.Constructs.Get(elec.Id)?.PartCount}, was {partsBefore}; removed index {removeAt}, root {sc.Root}, notice '{a.SandboxNotice}')");
                Program.LinkTestReset();
                _ = lampOnHost;
            }

            // 4) The edit modes on both sides: delete the links just made, re-route one, move nothing.
            foreach (var g in sides)
            {
                foreach (var mode in new[] { SandboxLinkWheel.Option.Delete, SandboxLinkWheel.Option.Reroute })
                {
                    Program.LinkTestReset();
                    Program.LinkTestWheel.Choose((int)mode);
                    foreach (var id in ids)
                        for (var k = (g.Constructs.Get(id)?.RouteCount ?? 0) - 1; k >= 0; k--)
                        {
                            FrameRoute(g, id, k, mode.ToString());
                            Pump(server, [a], 0.15, ref now, clock);
                            Program.LinkTestReset();
                            Program.LinkTestWheel.Choose((int)mode);
                        }
                }
                Program.LinkTestReset();
            }
            Pump(server, [a], 0.6, ref now, clock);

            // 5) A tool drawn again after Delete mode starts in route mode.
            Program.LinkTestWheel.Choose((int)SandboxLinkWheel.Option.Delete);
            Want(Program.LinkTestWheel.Mode == LinkMode.Delete, "Delete mode wasn't chosen");
            Program.LinkTestPutAway(a);
            Program.LinkTestTakeOut(a);
            Want(Program.LinkTestWheel.Mode == LinkMode.Link, $"the linking tool taken out again is in {Program.LinkTestWheel.Mode} mode, not route mode");
            Program.LinkTestWheel.Choose((int)SandboxLinkWheel.Option.Reroute);
            Program.LinkTestPutAway(a);
            Program.LinkTestTakeOut(a);
            Want(Program.LinkTestWheel.Mode == LinkMode.Link, $"the linking tool taken out after Re-route is in {Program.LinkTestWheel.Mode} mode, not route mode");
        }
        finally
        {
            Program.LinkGuardRethrow = false;
            Program.LinkTestReset();
            Hud.Camera = default;
            ImString.SharedStorage.Reset();
            ImGui.DestroyContext(ctx);
        }
        Want(Hud.OutOfFrame == outOfFrame0, $"HUD text was asked for outside an ImGui frame {Hud.OutOfFrame - outOfFrame0} time(s)");
        Want(imguiErrors == 0, $"ImGui reported {imguiErrors} error(s) (unbalanced Begin/End, Push/Pop or IDs), first at {firstImguiError}");
        Want(drawnMarkerFrames == markerFrames && markerFrames > 0, $"port markers were drawn on {drawnMarkerFrames} of {markerFrames} frames that had them");
        Want(Program.LinkGuardTrips == trips0, $"the guard had to recover {Program.LinkGuardTrips - trips0} frame(s)");
        Want(simHost && simClient, "simulated lag and loss weren't on");
        Pump(server, [a], 0.5, ref now, clock);
        Line($"link online: {frames} tool frames over {parts} aimed parts, {picks} picker picks, {started} links started; {markerFrames} frames with port markers ({seatMarkerFrames} on seats), ImGui errors {imguiErrors}; sim lag {simHost && simClient}; {problems.Count} problems");
        if (problems.Count > 0)
            Fail($"link online: {string.Join("; ", problems.Distinct().Take(10))}");
    }
}
