using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    private const string SwBeam = "Square Beam 0.5 m";
    private const string SwFrame = "Frame Block";

    /// <summary>
    /// "Socket weld 3-step" (board item dev-sandbox-socket-weld-3step-sw01, decisions 26 and 27; docs/sandbox/CONSTRUCTS.md,
    /// "Socket weld"): an aimed loose beam lists its socket markers (no allocation); its end socket picked, a frame block's
    /// side socket picked, the ghost has the socket on the socket facing out; spun 30 degrees and confirmed it is welded at
    /// exactly that pose as a fixed-pose segment, while 90 degrees stays on the grid (merged, linked, smart rotation learns
    /// the correction); a flat face point takes a piece; RMB backs out each step; a red target (an overlap) can't be
    /// confirmed; survival's weld takes the fit time; the construct message (what a save reads) and a blueprint round trip
    /// keep the pose; a real host with a separate client welds both ways and both agree. Decision 27: physics-only parts take
    /// the welder, items the wrench, whatever the file's old "tool" says. Short scripted scenarios, flat ground and the yard.
    /// </summary>
    private static void CheckSocketWeld()
    {
        SocketWeldTools();
        SocketWeldScene();
        SocketWeldLoopback();
    }

    private static void SocketWeldTools()
    {
        var engine = ConstructParts.Find("Engine");
        var lamp = ConstructParts.Find("Lamp");
        string? why = null;
        ConstructPartDef? oldHint = null, noHint = null;
        try
        {
            oldHint = ConstructParts.Parse($$"""{"format": "{{ConstructParts.Format}}", "name": "Old Heavy", "role": "frame", "tool": "wrench", "mass": 120, "health": 50, "boxes": [{"size": [0.4, 0.4, 0.4]}], "sockets": [{"name": "-y", "at": [0, -0.2, 0], "dir": "-y"}]}""", "old.json");
            noHint = ConstructParts.Parse($$"""{"format": "{{ConstructParts.Format}}", "name": "No Hint", "role": "frame", "mass": 2, "health": 10, "boxes": [{"size": [0.2, 0.2, 0.2]}], "sockets": [{"name": "-y", "at": [0, -0.1, 0], "dir": "-y"}]}""", "nohint.json");
        }
        catch (ConstructPartException e)
        {
            why = e.Message;
        }
        var ok = engine != null && lamp != null && oldHint != null && noHint != null
                 && ConstructBuild.ToolFor(engine) == ConstructTool.Welder && ConstructBuild.ToolFits(ConstructTool.Welder, engine) && !ConstructBuild.ToolFits(ConstructTool.Wrench, engine)
                 && ConstructBuild.ToolFor(lamp) == ConstructTool.Wrench && oldHint.Tool == ConstructTool.Welder && oldHint.PhysicsOnly && noHint.Tool == ConstructTool.Wrench && !noHint.PhysicsOnly;
        var mismatch = 0;
        foreach (var d in ConstructParts.All)
            if (d.Tool != (d.PhysicsOnly ? ConstructTool.Welder : ConstructTool.Wrench))
                mismatch++;
        Line($"socket weld tools: the Engine takes the {engine?.Tool}, the Lamp the {lamp?.Tool}; a 120 kg part whose file says \"wrench\" takes the {oldHint?.Tool}, a file with no \"tool\" the {noHint?.Tool}; {mismatch} of {ConstructParts.All.Count} parts off the rule {why}");
        if (!ok || mismatch > 0)
            Fail("socket weld: decision 27's tools by function don't hold (physics-only: the welder; items: the wrench)");
    }

    private static Construct? SwBody(ConstructWorld w, string part, Vector3 pos, Quaternion rot, bool pinned)
    {
        if (ConstructParts.Find(part) is not { } def || w.Create(pos, rot) is not { } c)
        {
            Fail($"socket weld: no {part} (or the world is full)");
            return null;
        }
        w.AddPart(c, def, Vector3.Zero, 0);
        c.Pinned = pinned;
        c.Frozen = !pinned;
        c.Rebuild();
        return c;
    }

    private static int SwSocket(ConstructPartDef d, string name) => Array.FindIndex(d.Sockets, s => s.Name == name);

    /// <summary>Part <paramref name="part"/>'s world pose if the piece's body were at <paramref name="pos"/> / <paramref name="rot"/>.</summary>
    private static (Vector3 P, Quaternion Q) SwPartAt(Construct c, int part, Vector3 pos, Quaternion rot) =>
        (pos + Vector3.Transform(c.Parts[part].Pos, rot), Quaternion.Normalize(rot * Rot24.Quat(c.Parts[part].Rot)));

    private static void SocketWeldScene()
    {
        var w = new ConstructWorld();
        if (SwBody(w, SwFrame, new Vector3(0, 0.5f, 0), Quaternion.Identity, pinned: true) is not { } frame
            || SwBody(w, SwBeam, new Vector3(2.2f, 0.5f, 0.3f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f), pinned: false) is not { } beam)
            return;
        var fd = frame.DefOf(0);
        var bd = beam.DefOf(0);
        var eye = new Vector3(1.2f, 1.6f, 1.6f);
        var hands = FwHands();
        var memory = new SmartRotationMemory();

        // Markers for the aimed beam (and nothing allocated listing them again).
        var dir = Vector3.Normalize(beam.WorldCom - eye);
        var aimed = w.Raycast(eye, dir, 6, out var hit) && hit.Body == beam;
        var marks = new WeldMarker[SocketWeld.MaxMarkers];
        var n = SocketWeld.Markers(w, eye, dir, beam, marks);
        var onBeam = 0;
        var free = true;
        for (var k = 0; k < n; k++)
            if (marks[k].Body == beam.Id)
            {
                onBeam++;
                free &= marks[k].State == WeldMarkerState.Free && marks[k].Fade > 0;
            }
        SocketWeld.Markers(w, eye, dir, beam, marks);
        var a0 = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 20; k++)
        {
            n = SocketWeld.Markers(w, eye, dir, beam, marks);
            SocketWeld.PickMarker(marks.AsSpan(0, n), onlyBody: beam.Id);
        }
        var alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        Line($"socket weld: aimed at the loose {bd.Name} ({aimed}): {n} markers, {onBeam} on the beam (of {bd.Sockets.Length} sockets), all free and shown {free}; {alloc} bytes over 20 listings");
        if (!aimed || onBeam != bd.Sockets.Length || !free)
            Fail("socket weld: the aimed beam's socket markers weren't listed");
        if (alloc > 0)
            Fail($"socket weld: listing the markers allocated {alloc} bytes");

        // Step 1: the beam's end socket; step 2: the frame's +x side socket. The ghost: socket on socket, facing out.
        var flow = new SocketWeldFlow();
        var end = SwSocket(bd, "-z");
        var side = SwSocket(fd, "+x");
        flow.PickSource(beam.Id, 0, end);
        var step2 = flow.Step == SocketWeldFlow.Stage.Target;
        var target = new WeldTarget { Body = frame.Id, Part = 0, Socket = (byte)side };
        flow.PickTarget(frame, target, beam, memory);
        var step3 = flow.Step == SocketWeldFlow.Stage.Rotate;
        SocketWeld.Pose(beam, 0, end, frame, target, flow.Angle, out _, out _, out var gp, out var gq);
        beam.SocketAt(0, end, out var sAt, out var sDir);
        frame.SocketAt(0, side, out var tAt, out var tDir);
        var sw = gp + Vector3.Transform(sAt, gq);
        var tw = frame.ToWorld(tAt);
        var facing = Vector3.Dot(Vector3.Transform(ConstructParts.DirVector(sDir), gq), Vector3.Transform(ConstructParts.DirVector(tDir), frame.Rot));
        var outward = gp + Vector3.Transform((bd.BoundsMin + bd.BoundsMax) * 0.5f, gq);
        Line($"socket weld: step 1 picked the beam's {bd.Sockets[end].Name} ({step2}), step 2 the frame's {fd.Sockets[side].Name} ({step3}), start angle {flow.StartAngle:0}; the ghost's socket {Vector3.Distance(sw, tw) * 1000:0.000} mm off the target, facing {facing:0.000}, its middle at x {outward.X:0.00}");
        if (!step2 || !step3 || Vector3.Distance(sw, tw) > 0.0051f || facing > -0.999f || outward.X < 0.3f) // (on the grid: within the centimetre grid's half)
            Fail("socket weld: the ghost isn't aligned socket on socket, facing out");

        // A spin of 30 degrees (15 degree snaps: 20 is 15, 10 more is 30) welded: a fixed-pose segment exactly there.
        flow.Spin(20, false, false);
        var snapped = flow.Angle;
        flow.Spin(10, false, false);
        var angle = flow.Angle;
        var free7 = SocketWeld.Snap(7.3f, free: true, fine: false);
        var fine7 = SocketWeld.Snap(7.3f, free: false, fine: true);
        SocketWeld.Pose(beam, 0, end, frame, target, angle, out _, out _, out gp, out gq);
        var ghost = SwPartAt(beam, 0, gp, gq);
        var bodies = w.Bodies.Count;
        var why = hands.BeginSocketWeld(w, 0, eye, beam.Id, 0, end, target, angle, 0);
        var welded = why == null && w.Get(beam.Id) == null && w.Bodies.Count == bodies - 1 && frame.FreeformCount == 1;
        var pi = frame.PartCount - 1;
        var pose = welded ? FwDiff(ghost, FwWorld(frame, pi)) : (99f, 99f);
        var spun = welded ? FwDiff((Vector3.Zero, Quaternion.Identity), (Vector3.Zero, Quaternion.Conjugate(frame.Rot) * FwWorld(frame, pi).Q)).Item2 : -1;
        Line($"socket weld: spun to {snapped:0} then {angle:0} deg (Shift: {free7:0.0}, Ctrl: {fine7:0}); welded {welded} ({why ?? "ok"}) as a freeform segment, {pose.Item1 * 1000:0.000} mm / {pose.Item2:0.0000} deg from the ghost (turned {spun:0.0} deg from the frame)");
        if (snapped != 15 || angle != 30 || MathF.Abs(free7 - 7.3f) > 0.01f || fine7 != 5 || !welded || pose.Item1 > 0.001f || pose.Item2 > 0.05f)
            Fail("socket weld: the 30 degree weld didn't land as a fixed-pose segment at the ghost's pose");

        // 90 degrees onto the -x socket stays on the grid: merged and linked, no new freeform weld; smart rotation learns.
        if (SwBody(w, SwBeam, new Vector3(-2.2f, 0.5f, -0.4f), Quaternion.Identity, pinned: false) is not { } beam2)
            return;
        var flow2 = new SocketWeldFlow();
        var minus = SwSocket(fd, "-x");
        var t2 = new WeldTarget { Body = frame.Id, Part = 0, Socket = (byte)minus };
        flow2.PickSource(beam2.Id, 0, end);
        flow2.PickTarget(frame, t2, beam2, memory);
        var start = flow2.StartAngle;
        flow2.Spin(90, false, false);
        var a2 = flow2.Angle;
        SocketWeld.Pose(beam2, 0, end, frame, t2, a2, out _, out var rel2, out gp, out gq);
        ghost = SwPartAt(beam2, 0, gp, gq);
        var learned = flow2.Learn(frame, beam2, memory, rel2);
        var parts = frame.PartCount;
        why = hands.BeginSocketWeld(w, 0, eye, beam2.Id, 0, end, t2, a2, 0);
        if (frame.Dirty)
            frame.Rebuild();
        var grid = why == null && w.Get(beam2.Id) == null && frame.PartCount == parts + 1 && frame.FreeformCount == 1 && frame.Parts[parts].Seg == 0 && frame.SocketUsed(0, minus);
        var gpose = grid ? FwDiff(ghost, FwWorld(frame, parts)) : (99f, 99f);
        Line($"socket weld: 90 deg from the guess {start:0} (to {a2:0}): on the grid {grid} ({why ?? "ok"}), {gpose.Item1 * 1000:0.000} mm / {gpose.Item2:0.0000} deg from the ghost; smart rotation ranked {flow2.Ranked} turns and learned the correction {learned} ({memory.LearnedCount})");
        if (!grid || gpose.Item1 > 0.002f || gpose.Item2 > 0.05f)
            Fail("socket weld: a 90 degree weld onto a socket didn't stay on the grid");
        if (flow2.Ranked > 0 && (!learned || memory.LearnedCount != 1))
            Fail("socket weld: smart rotation didn't learn from the step-3 correction");

        // RMB backs out each step; R turns to the next facing.
        var flow3 = new SocketWeldFlow();
        if (SwBody(w, SwBeam, new Vector3(0.3f, 0.5f, 2.4f), Quaternion.Identity, pinned: false) is not { } beam3)
            return;
        flow3.PickSource(beam3.Id, 0, end);
        flow3.PickTarget(frame, target, beam3, null);
        var r0 = flow3.Angle;
        flow3.NextVariant(beam3, frame);
        var r1 = flow3.Angle;
        var b1 = flow3.Back() && flow3.Step == SocketWeldFlow.Stage.Target && flow3.Source == beam3.Id;
        var b2 = flow3.Back() && flow3.Step == SocketWeldFlow.Stage.Source && flow3.Source == 0;
        var b3 = !flow3.Back();
        Line($"socket weld: R turned {r0:0} to {r1:0} deg; RMB: 3 to 2 {b1}, 2 to 1 {b2}, then nothing to back out of {b3}");
        if (r0 == r1 || !b1 || !b2 || !b3)
            Fail("socket weld: RMB didn't step back 3 -> 2 -> 1 -> cancel (or R didn't turn it)");

        // A red target: the frame's +x again overlaps the welded beam; it can't be confirmed and nothing changes.
        var red = SocketWeld.Check(w, null, beam3, 0, end, frame, target, 0, ReadOnlySpan<Vector3>.Empty);
        parts = frame.PartCount;
        var refused = hands.BeginSocketWeld(w, 0, eye, beam3.Id, 0, end, target, 0, 0);
        Line($"socket weld: a red target: \"{red}\"; confirmed anyway: \"{refused}\", the beam still loose {w.Get(beam3.Id) != null}");
        if (red == null || refused == null || w.Get(beam3.Id) == null || frame.PartCount != parts)
            Fail("socket weld: a red target (an overlap) could be confirmed");

        // A flat face point (no socket there), in survival: the weld takes the beam's fit time.
        if (!SocketWeld.FaceAt(frame, 0, new Vector3(0.06f, 0.75f, -0.047f), Vector3.UnitY, out var face))
        {
            Fail("socket weld: no face point on the frame's top");
            return;
        }
        var shands = FwHands(survival: true);
        SocketWeld.Pose(beam3, 0, end, frame, face, 45, out _, out _, out gp, out gq);
        ghost = SwPartAt(beam3, 0, gp, gq);
        why = shands.BeginSocketWeld(w, 0, eye, beam3.Id, 0, end, face, 45, 10.0);
        var pending = why == null && shands.Hand(0).Edit == EditKind.SocketWeld && w.Get(beam3.Id) != null;
        shands.Tick(w, 10.0 + bd.AttachSeconds * 0.5, _ => eye);
        var notYet = w.Get(beam3.Id) != null;
        shands.Tick(w, 10.0 + bd.AttachSeconds + 0.01, _ => eye);
        var faced = w.Get(beam3.Id) == null && frame.FreeformCount == 2;
        var fpose = faced ? FwDiff(ghost, FwWorld(frame, frame.PartCount - 1)) : (99f, 99f);
        Line($"socket weld: a face point at {face.At.X:0.00} {face.At.Y:0.00} {face.At.Z:0.00} facing {ConstructParts.DirName(face.Dir)}, survival at 45 deg: welding {pending}, not done at half time {notYet}, done at {bd.AttachSeconds} s {faced} ({why ?? "ok"}), {fpose.Item1 * 1000:0.000} mm / {fpose.Item2:0.0000} deg from the ghost");
        if (face.Dir != 2 || MathF.Abs(face.At.Y - 0.25f) > 1e-4f || !pending || !notYet || !faced || fpose.Item1 > 0.001f || fpose.Item2 > 0.05f)
            Fail("socket weld: the face point weld (or survival's weld time) didn't work");

        // The construct message (what a client and a save read) and a blueprint keep the 30 degree beam's pose.
        var copyPose = (99f, 99f);
        {
            var copy = new ConstructWorld();
            var pw = new PacketWriter(65536);
            ConstructNet.WriteFull(pw, frame);
            var pr = new PacketReader(pw.Span[1..]);
            ConstructNet.Read(ref pr, copy, 0);
            if (copy.Get(frame.Id) is { FreeformCount: 2 } cc && cc.PartCount == frame.PartCount)
                copyPose = FwDiff(FwRel(frame, 0, frame, pi), FwRel(cc, 0, cc, pi));
        }
        var bpPose = (99f, 99f);
        var bpWhy = "";
        {
            var weld = -1;
            for (var j = 0; j < frame.JointCount && weld < 0; j++)
                if (frame.Joints[j].Freeform && frame.Joints[j].Child == pi)
                    weld = j;
            if (weld >= 0)
            {
                Freeform.StepPose(frame, weld, out var sp, out var sr);
                var json = $$"""{ "format": "call-of-slop-construct-blueprint/1", "name": "Socket Weld", "steps": [ { "part": "{{SwFrame}}" }, { "part": "{{SwBeam}}", "weld": {{ConstructBuild.WeldJson(0, sp, sr)}} } ] }""";
                try
                {
                    var bp = ConstructBuild.ParseBlueprint(json, "socketweld.json");
                    var wb = new ConstructWorld();
                    if (ConstructBuild.Build(wb, bp, Vector3.Zero, Quaternion.Identity, out bpWhy) is { FreeformCount: 1 } built)
                        bpPose = FwDiff(FwRel(frame, 0, frame, pi), FwRel(built, 0, built, built.PartCount - 1));
                }
                catch (ConstructPartException e)
                {
                    bpWhy = e.Message;
                }
            }
        }
        Line($"socket weld: a fresh copy from the construct message puts the 30 degree beam {copyPose.Item1 * 1000:0.000} mm / {copyPose.Item2:0.0000} deg off; a blueprint with its weld step {bpPose.Item1 * 1000:0.000} mm / {bpPose.Item2:0.0000} deg {bpWhy}");
        if (copyPose.Item1 > 0.001f || copyPose.Item2 > 0.05f || bpPose.Item1 > 0.001f || bpPose.Item2 > 0.05f)
            Fail($"socket weld: the message or blueprint round trip didn't keep the pose ({bpWhy})");
    }

    private static void SocketWeldLoopback()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"socket weld loopback: the {ConstructYardMap} map is missing");
            return;
        }
        const ushort port = 26387;
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
            Fail("socket weld loopback: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        Pump(server, [a], 0.3, ref now, clock);
        var w = server.Constructs;
        var g = ConstructWorld.Ground(World.Query, sp.Position.X + 2f, sp.Position.Z);
        if (SwBody(w, SwFrame, g + new Vector3(0, 0.9f, 0), Quaternion.Identity, pinned: true) is not { } frame
            || SwBody(w, SwBeam, g + new Vector3(0, 1.8f, 1.2f), Quaternion.Identity, pinned: false) is not { } beam
            || SwBody(w, SwBeam, g + new Vector3(0, 1.8f, -1.2f), Quaternion.Identity, pinned: false) is not { } beam2)
            return;
        frame.Owner = sp.Id;
        var fd = frame.DefOf(0);
        var end = SwSocket(beam.DefOf(0), "-z");
        var tx = new WeldTarget { Body = frame.Id, Part = 0, Socket = (byte)SwSocket(fd, "+x") };
        var tm = new WeldTarget { Body = frame.Id, Part = 0, Socket = (byte)SwSocket(fd, "-x") };
        SocketWeld.Pose(beam, 0, end, frame, tx, 30, out _, out _, out var gp, out var gq);
        var ghost = SwPartAt(beam, 0, gp, gq);
        Pump(server, [a], 0.6, ref now, clock);
        a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Welder);
        Pump(server, [a], 0.3, ref now, clock);
        a.SandboxSendSocketWeld(beam.Id, 0, end, tx, 30);
        Pump(server, [a], 0.8, ref now, clock);
        var welded = w.Get(beam.Id) == null && frame.FreeformCount == 1 && frame.PartCount == 2;
        var serverPose = welded ? FwDiff(ghost, FwWorld(frame, 1)) : (99f, 99f);
        a.SandboxSendSocketWeld(beam2.Id, 0, end, tm, 90);
        Pump(server, [a], 0.8, ref now, clock);
        var grid = w.Get(beam2.Id) == null && frame.FreeformCount == 1 && frame.PartCount == 3 && frame.Parts[2].Seg == 0;
        var copy = a.Constructs.Get(frame.Id);
        var agree = copy is { FreeformCount: 1, PartCount: 3 } && welded && grid;
        var p1 = agree ? FwDiff(FwWorld(frame, 1), FwWorld(copy!, 1)) : (99f, 99f);
        var p2 = agree ? FwDiff(FwWorld(frame, 2), FwWorld(copy!, 2)) : (99f, 99f);
        var gone = a.Constructs.Get(beam.Id) == null && a.Constructs.Get(beam2.Id) == null;
        Line($"socket weld loopback: the client's 30 degree weld on the server {welded} ({serverPose.Item1 * 1000:0.000} mm / {serverPose.Item2:0.0000} deg from the ghost), its 90 degree weld on the grid {grid}; "
             + $"the client's copy agrees to {p1.Item1 * 1000:0.000} mm / {p1.Item2:0.0000} deg and {p2.Item1 * 1000:0.000} mm / {p2.Item2:0.0000} deg, the loose beams gone {gone}");
        if (!welded || !grid || serverPose.Item1 > 0.001f || serverPose.Item2 > 0.05f || p1.Item1 > 0.001f || p1.Item2 > 0.05f || p2.Item1 > 0.001f || p2.Item2 > 0.05f || !gone)
            Fail("socket weld loopback: the client's socket welds didn't reach the server, or the two disagree on the pose");
    }
}
