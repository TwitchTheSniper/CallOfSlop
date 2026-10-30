using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Freeform weld" (board item dev-sandbox-freeform-weld-fw01, decision 25; docs/sandbox/CONSTRUCTS.md, "Freeform
    /// welds"): a panel held at 30 degrees against the buggy is welded by one ray-through click and stays at exactly that
    /// pose while the buggy drives and turns (no allocation per tick); the two-click fallback welds parts side by side; a
    /// gap over the limit is refused with the reason; socket parts attach to the freeform panel on its own grid; the welder
    /// cuts it free where it is (the part on it comes along); destroying the anchor detaches it; survival's weld takes its
    /// time; the construct message (a fresh copy, what a save would read) and a blueprint round trip put the panel at the
    /// same pose; a real host with a separate client welds and cuts over the transport and both agree on the pose; the
    /// welder's default mode is still socket weld. Short scripted scenarios on flat ground and the Construct Yard.
    /// </summary>
    private static void CheckFreeformWeld()
    {
        var modes = new SandboxWeldModes();
        var socketDefault = !modes.Freeform && modes.CurrentOption == (int)SandboxWeldModes.Option.Socket && modes.Options.Length == 2;
        modes.Choose((int)SandboxWeldModes.Option.Freeform);
        var picked = modes.Freeform && modes.CurrentOption == (int)SandboxWeldModes.Option.Freeform;
        Line($"freeform weld: the welder's outer ring has \"{modes.Options[0].Label}\" (the default {socketDefault}) and \"{modes.Options[1].Label}\" (picked {picked})");
        if (!socketDefault || !picked)
            Fail("freeform weld: the welder's default mode isn't socket weld, or freeform can't be picked");
        FreeformBuggy();
        FreeformAnchor();
        FreeformLoopback();
    }

    private static Construct? FwLoose(ConstructWorld w, string part, Vector3 pos, Quaternion rot, bool frozen = false)
    {
        if (ConstructParts.Find(part) is not { } def || w.Create(pos, rot) is not { } c)
        {
            Fail($"freeform weld: no {part} (or the world is full)");
            return null;
        }
        w.AddPart(c, def, Vector3.Zero, 0);
        c.Frozen = frozen; // (held where it is, like the physics gun's freeze)
        c.Rebuild();
        return c;
    }

    /// <summary>Pose of <paramref name="b"/> in <paramref name="a"/>'s frame: how far it moved (m) and turned (degrees) between two of them.</summary>
    private static (float Move, float Turn) FwDiff((Vector3 P, Quaternion Q) a, (Vector3 P, Quaternion Q) b) =>
        (Vector3.Distance(a.P, b.P), 2 * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a.Q), Quaternion.Normalize(b.Q))), 0, 1)) * 180 / MathF.PI);

    private static (Vector3 P, Quaternion Q) FwWorld(Construct c, int part)
    {
        Freeform.PartWorld(c, part, out var p, out var q);
        return (p, q);
    }

    /// <summary><paramref name="part"/>'s pose relative to <paramref name="frame"/> (both parts of one construct, or not).</summary>
    private static (Vector3 P, Quaternion Q) FwRel(Construct cf, int frame, Construct cp, int part)
    {
        var (fp, fq) = FwWorld(cf, frame);
        var (pp, pq) = FwWorld(cp, part);
        var iq = Quaternion.Conjugate(fq);
        return (Vector3.Transform(pp - fp, iq), iq * pq);
    }

    private static ConstructEdits FwHands(bool survival = false)
    {
        var hands = new ConstructEdits { InfiniteParts = !survival, Survival = survival };
        hands.Hand(0).Tool = ConstructTool.Welder;
        return hands;
    }

    private static void FreeformBuggy()
    {
        if (ConstructBuild.FindBlueprint("Buggy") is not { } bp)
        {
            Fail("freeform weld: no Buggy blueprint");
            return;
        }
        var w = new ConstructWorld();
        if (DriveSpawn(w, bp, 0, 0, 0, "the buggy") is not { } car)
            return;
        DriveSteps(w, RoutesGround, 60);
        var plate = PartNamed(car, "Plate Steel 1 m");
        if (plate < 0)
        {
            Fail("freeform weld: the buggy has no plate");
            return;
        }
        // A panel held at 30 degrees over the plate, its low edge 4 cm above it (a gap: the bead bridges it).
        Freeform.PartBox(car, plate, out var bq, out var bc, out var bh);
        var up = Vector3.Transform(Vector3.UnitY, bq);
        var tilt = car.Rot * Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 6);
        var panelAt = bc + up * (bh.Y + 0.05f * MathF.Cos(MathF.PI / 6) + 0.5f * MathF.Sin(MathF.PI / 6) + 0.04f) + Vector3.Transform(new Vector3(0, 0, 0.2f), car.Rot);
        if (FwLoose(w, "Panel", panelAt, tilt, frozen: true) is not { } panel)
            return;
        var bodies = w.Bodies.Count;
        // One click: the ray from above goes through the panel to the buggy behind it.
        var eye = panelAt + up * 1.6f;
        var dir = -up;
        var hands = FwHands();
        string? why = "nothing hit";
        var onPanel = w.Raycast(eye, dir, ConstructBuild.Reach + 1, out var hit) && hit.Body == panel;
        ConstructHit behind = default;
        var through = onPanel && Freeform.Behind(w, hit, eye, dir, ConstructBuild.Reach + 1.5f, out behind) && behind.Body == car;
        var gap = through ? Freeform.Gap(panel, hit.Part, car, behind.Part, out _, out _) : -1;
        var before = FwWorld(panel, 0);
        if (through)
            why = hands.BeginFreeform(w, 0, eye, panel.Id, hit.Part, car.Id, behind.Part, 0);
        var welded = why == null && w.Bodies.Count == bodies - 1 && w.Get(panel.Id) == null && car.FreeformCount == 1;
        var pi = car.PartCount - 1;
        var weldPose = welded ? FwDiff(before, FwWorld(car, pi)) : (99f, 99f);
        Line($"freeform weld: the ray through the panel finds the buggy's {(through ? car.DefOf(behind.Part).Name : "-")} behind it (gap {gap:0.000} m); welded {welded} ({why ?? "ok"}), the panel moved {weldPose.Item1 * 1000:0.000} mm and turned {weldPose.Item2:0.0000} deg");
        if (!onPanel || !through || !welded || weldPose.Item1 > 0.001f || weldPose.Item2 > 0.05f)
        {
            Fail($"freeform weld: the ray-through weld failed or moved the panel ({why})");
            return;
        }
        // Socket parts attach to the freeform panel (on its own Rot24 grid, its segment).
        var beam = ConstructParts.Find("Square Beam 0.5 m");
        var attached = -1;
        for (var s = 0; beam != null && s < car.SocketCount(pi) && attached < 0; s++)
            for (var ds = 0; ds < beam.Sockets.Length && attached < 0; ds++)
                if (ConstructBuild.Place(car, pi, s, beam, ds, 0, out var g) == null)
                    attached = ConstructBuild.Attach(w, car, beam, g);
        if (car.Dirty)
            car.Rebuild();
        var onSeg = attached >= 0 && car.Parts[attached].Seg == car.Parts[pi].Seg;
        var beamRel = attached >= 0 ? FwRel(car, pi, car, attached) : default;
        Line($"freeform weld: a Square Beam 0.5 m socket-attached to the freeform panel as part {attached} on its segment {onSeg}");
        if (!onSeg)
            Fail("freeform weld: a socket part didn't attach to the freeform panel's segment");
        // It drives and turns: the panel stays exactly where it was welded on the plate.
        var rel0 = FwRel(car, plate, car, pi);
        Seat(car);
        var p0 = car.WorldCom;
        var spin = car.Rot;
        var alloc = -1L;
        DriveSteps(w, RoutesGround, 90, _ => DriveKeys(car, Buttons.Forward));
        Action<int> turnLeft = _ => DriveKeys(car, Buttons.Forward | Buttons.Left);
        var a0 = GC.GetAllocatedBytesForCurrentThread();
        DriveSteps(w, RoutesGround, 90, turnLeft);
        alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        var travelled = Vector3.Distance(p0, car.WorldCom);
        var turned = FwDiff((Vector3.Zero, spin), (Vector3.Zero, car.Rot)).Item2;
        var held = FwDiff(rel0, FwRel(car, plate, car, pi));
        Line($"freeform weld: driving {travelled:0.0} m and turning {turned:0} deg, the panel moved {held.Item1 * 1000:0.000} mm and turned {held.Item2:0.0000} deg on the plate; {alloc} bytes allocated over 90 ticks");
        if (travelled < 2 || turned < 10 || held.Item1 > 0.001f || held.Item2 > 0.05f)
            Fail("freeform weld: the welded panel didn't stay put while the buggy drove and turned (or it didn't drive)");
        if (alloc > 0)
            Fail($"freeform weld: {alloc} bytes allocated per 90 ticks of a buggy with a freeform panel");
        car.Driver = 255;
        car.DriverSeat = -1;
        // The construct message: a fresh copy reads the same pose (what a client, and a save, would see).
        var copyPose = (99f, 99f);
        {
            var copy = new ConstructWorld();
            var pw = new PacketWriter(65536);
            ConstructNet.WriteFull(pw, car);
            var pr = new PacketReader(pw.Span[1..]);
            ConstructNet.Read(ref pr, copy, 0);
            if (copy.Get(car.Id) is { FreeformCount: 1 } cc)
                copyPose = FwDiff(FwRel(car, plate, car, pi), FwRel(cc, plate, cc, pi));
        }
        // A blueprint with the weld as a step builds the panel at the same place on the plate.
        var bpPose = (99f, 99f);
        string bpWhy = "";
        {
            var weld = -1;
            for (var j = 0; j < car.JointCount; j++)
                if (car.Joints[j].Freeform)
                    weld = j;
            Freeform.StepPose(car, weld, out var sp, out var sr);
            var anchor = car.Joints[weld].Hinge;
            var steps = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Constructs", "Blueprints", "Buggy.json"));
            var root = System.Text.Json.Nodes.JsonNode.Parse(steps)!.AsObject();
            root["name"] = "Freeform Buggy";
            root["steps"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse($$"""{ "part": "Panel", "weld": {{ConstructBuild.WeldJson(anchor, sp, sr)}} }"""));
            try
            {
                var fbp = ConstructBuild.ParseBlueprint(root.ToJsonString(), "freeform.json");
                var wb = new ConstructWorld();
                if (ConstructBuild.Build(wb, fbp, Vector3.Zero, Quaternion.Identity, out bpWhy) is { } built && built.FreeformCount == 1 && built.DefOf(anchor) == car.DefOf(anchor))
                    bpPose = FwDiff(FwRel(car, anchor, car, pi), FwRel(built, anchor, built, built.Joints[built.JointCount - 1].Child));
            }
            catch (ConstructPartException e)
            {
                bpWhy = e.Message;
            }
        }
        Line($"freeform weld: a fresh copy from the construct message puts the panel {copyPose.Item1 * 1000:0.000} mm / {copyPose.Item2:0.0000} deg off; a blueprint with its weld step {bpPose.Item1 * 1000:0.000} mm / {bpPose.Item2:0.0000} deg {bpWhy}");
        if (copyPose.Item1 > 0.001f || copyPose.Item2 > 0.05f)
            Fail("freeform weld: the construct message didn't carry the freeform pose");
        if (bpPose.Item1 > 0.001f || bpPose.Item2 > 0.05f)
            Fail($"freeform weld: the blueprint round trip didn't keep the freeform pose ({bpWhy})");
        // A gap over the limit is refused, with the reason.
        Freeform.PartBox(car, plate, out bq, out bc, out bh);
        up = Vector3.Transform(Vector3.UnitY, bq);
        var far = FwLoose(w, "Panel", bc + up * (bh.Y + 0.05f + 0.6f), car.Rot, frozen: true);
        var farWhy = far == null ? "none" : hands.BeginFreeform(w, 0, bc + up * 2, far.Id, 0, car.Id, plate, 0) ?? "accepted";
        Line($"freeform weld: a panel 0.6 m off the plate: \"{farWhy}\"");
        if (!farWhy.Contains("too far apart"))
            Fail("freeform weld: a gap over the limit wasn't refused with the reason");
        if (far != null)
            w.Remove(far);
        // The welder cuts it free where it is; the beam on it comes along.
        var cutAt = FwWorld(car, pi);
        bodies = w.Bodies.Count;
        var weldJ = Freeform.SeamAt(car, pi, Vector3.Lerp(cutAt.P, bc, 0.5f));
        for (var j = 0; j < car.JointCount && weldJ < 0; j++)
            if (car.Joints[j].Freeform)
                weldJ = j;
        var cutWhy = hands.FreeformCut(w, 0, cutAt.P + up * 1.5f, car.Id, pi, weldJ) ?? "ok";
        var freed = w.Bodies.Count == bodies + 1 ? w.Bodies[^1] : null;
        if (car.Dirty)
            car.Rebuild(); // (the physics does it next tick)
        var freedPose = freed != null && freed.PartCount > 0 ? FwDiff(cutAt, FwWorld(freed, 0)) : (99f, 99f);
        Line($"freeform weld: cut ({cutWhy}): the buggy has {car.FreeformCount} freeform welds; the piece is construct {freed?.Id} of {freed?.PartCount} parts, {freedPose.Item1 * 1000:0.000} mm / {freedPose.Item2:0.0000} deg from where it was");
        if (cutWhy != "ok" || car.FreeformCount != 0 || freed == null || freed.PartCount != 2 || freedPose.Item1 > 0.001f || freedPose.Item2 > 0.05f)
            Fail("freeform weld: the welder didn't cut the panel free where it was (with the beam on it)");
        _ = beamRel;
    }

    private static void FreeformAnchor()
    {
        // Two panels side by side: the ray through one finds nothing behind it, so two clicks weld them.
        var w = new ConstructWorld();
        if (FwLoose(w, "Panel", new Vector3(0, 0.3f, 0), Quaternion.Identity) is not { } frame)
            return;
        frame.Pinned = true;
        frame.Rebuild();
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.15f);
        if (FwLoose(w, "Panel", new Vector3(1.12f, 0.3f, 0.05f), turn, frozen: true) is not { } side)
            return;
        var eye = new Vector3(1.2f, 1.9f, 0);
        var aimed = w.Raycast(eye, -Vector3.UnitY, 3, out var hit) && hit.Body == side;
        var nothingBehind = aimed && !Freeform.Behind(w, hit, eye, -Vector3.UnitY, 3, out _);
        var hands = FwHands(survival: true);
        var before = FwWorld(side, 0);
        // Survival: the weld takes its time (done by the tick once it is up).
        var why = hands.BeginFreeform(w, 0, eye, side.Id, 0, frame.Id, 0, 10.0);
        var pending = why == null && hands.Hand(0).Edit == EditKind.Weld && w.Get(side.Id) != null;
        hands.Tick(w, 10.0 + Freeform.WeldSeconds * 0.5, _ => eye);
        var notYet = w.Get(side.Id) != null;
        hands.Tick(w, 10.0 + Freeform.WeldSeconds + 0.01, _ => eye);
        var done = w.Get(side.Id) == null && frame.FreeformCount == 1;
        var pose = done ? FwDiff(before, FwWorld(frame, frame.PartCount - 1)) : (99f, 99f);
        Line($"freeform weld: two panels side by side: nothing behind the first {nothingBehind}; two clicks, survival: welding {pending}, not done at half time {notYet}, done at {Freeform.WeldSeconds} s {done} ({why ?? "ok"}); the panel moved {pose.Item1 * 1000:0.000} mm / {pose.Item2:0.0000} deg");
        if (!aimed || !nothingBehind || !pending || !notYet || !done || pose.Item1 > 0.001f || pose.Item2 > 0.05f)
            Fail("freeform weld: the two-click fallback (or survival's weld time) didn't work");
        if (!done)
            return;
        // Destroying the anchor part detaches the welded piece where it was.
        var held = FwWorld(frame, frame.PartCount - 1);
        var anchor = frame.Joints[0].Hinge;
        var bodies = w.Bodies.Count;
        w.DamagePart(frame, anchor, 1e6f, out _, raw: true);
        Construct? loose = null;
        foreach (var c in w.Bodies)
            if (c.PartCount == 1 && c.DefOf(0).Name == "Panel" && FwDiff(held, FwWorld(c, 0)).Item1 < 0.01f)
                loose = c;
        var lp = loose != null ? FwDiff(held, FwWorld(loose, 0)) : (99f, 99f);
        Line($"freeform weld: the anchor destroyed: {bodies} bodies became {w.Bodies.Count}; the welded panel came off where it was {loose != null} ({lp.Item1 * 1000:0.000} mm / {lp.Item2:0.0000} deg)");
        if (loose == null || lp.Item1 > 0.001f || lp.Item2 > 0.05f)
            Fail("freeform weld: destroying the anchor didn't detach the welded panel where it was");
    }

    private static void FreeformLoopback()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"freeform weld: the {ConstructYardMap} map is missing");
            return;
        }
        const ushort port = 26383;
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
            Fail("freeform weld loopback: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        Pump(server, [a], 0.3, ref now, clock);
        // A pinned panel beside the client's player and a panel frozen at 30 degrees just over it.
        var w = server.Constructs;
        var g = ConstructWorld.Ground(World.Query, sp.Position.X + 2f, sp.Position.Z);
        if (FwLoose(w, "Panel", g + new Vector3(0, 0.5f, 0), Quaternion.Identity) is not { } frame)
            return;
        frame.Pinned = true;
        frame.Owner = sp.Id;
        frame.Rebuild();
        var tilt = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 6);
        if (FwLoose(w, "Panel", g + new Vector3(0, 0.5f + 0.05f + 0.0433f + 0.25f + 0.03f, 0), tilt, frozen: true) is not { } panel)
            return;
        var before = FwWorld(panel, 0);
        Pump(server, [a], 0.6, ref now, clock);
        a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Welder);
        Pump(server, [a], 0.3, ref now, clock);
        a.SandboxSend(CActKind.FreeformWeld, body: panel.Id, part: 0, body2: frame.Id, socket: 0);
        Pump(server, [a], 0.8, ref now, clock);
        var welded = w.Get(panel.Id) == null && frame.FreeformCount == 1;
        var serverPose = welded ? FwDiff(before, FwWorld(frame, 1)) : (99f, 99f);
        var copy = a.Constructs.Get(frame.Id);
        var clientPose = welded && copy is { FreeformCount: 1, PartCount: 2 } ? FwDiff(FwWorld(frame, 1), FwWorld(copy, 1)) : (99f, 99f);
        var clientGone = a.Constructs.Get(panel.Id) == null;
        // The client cuts it free again.
        var weld = -1;
        for (var j = 0; j < frame.JointCount; j++)
            if (frame.Joints[j].Freeform)
                weld = j;
        var had = new HashSet<ushort>();
        foreach (var c in w.Bodies)
            had.Add(c.Id);
        if (weld >= 0)
            a.SandboxSend(CActKind.FreeformCut, body: frame.Id, part: 1, socket: (byte)weld);
        Pump(server, [a], 0.8, ref now, clock);
        var cut = frame.FreeformCount == 0 && frame.PartCount == 1;
        Construct? freed = null;
        foreach (var c in w.Bodies)
            if (!had.Contains(c.Id) && c.PartCount == 1 && c.DefOf(0).Name == "Panel")
                freed = c; // (it falls once cut free: found by being new)
        var clientCut = a.Constructs.Get(frame.Id) is { FreeformCount: 0, PartCount: 1 } && freed != null && a.Constructs.Get(freed.Id) != null;
        Line($"freeform weld loopback: the client's weld on the server {welded} (the panel moved {serverPose.Item1 * 1000:0.000} mm / {serverPose.Item2:0.0000} deg), its copy agrees to {clientPose.Item1 * 1000:0.000} mm / {clientPose.Item2:0.0000} deg and dropped the loose panel {clientGone}; "
             + $"the client's cut on the server {cut}, freed as construct {freed?.Id}, mirrored {clientCut}");
        if (!welded || serverPose.Item1 > 0.001f || serverPose.Item2 > 0.05f || clientPose.Item1 > 0.001f || clientPose.Item2 > 0.05f || !clientGone)
            Fail("freeform weld loopback: the client's freeform weld didn't reach the server, or the two disagree on the pose");
        if (!cut || freed == null || !clientCut)
            Fail("freeform weld loopback: the client's cut didn't free the panel on both");
    }
}
