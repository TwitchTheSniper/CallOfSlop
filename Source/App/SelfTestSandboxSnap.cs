using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Sandbox snap build" (board item dev-sandbox-snappy-build-sb01, decision 24; docs/sandbox/CONSTRUCTS.md, "Snappy
    /// building"): a held barrel sees only a receiver's front and snaps to it from an off aim on screen (and not from far
    /// off, and never to a frame); a gearbox mounts by a structural face and its input lines up with an engine's output;
    /// every part with sockets reports its own connection points (single-purpose parts only their functional ones); the
    /// switch key moves to another own point; quick building in creative fits at once and keeps the part, so six lamps
    /// are placed along a beam with one held part; picking takes the aimed part's kind; undo removes the last placements
    /// and is refused for someone else's construct; the cache is refreshed only when the construct changes and allocates
    /// nothing in a steady frame.
    /// </summary>
    private static void CheckSandboxSnapBuild()
    {
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        var cache = new SnapCache();
        var w = new ConstructWorld();
        var hands = new ConstructEdits();
        const byte me = 0, other = 1;
        ref var h = ref hands.Hand(me);

        // 1. A barrel: only the receiver's front; the magnet.
        var receiver = ConstructParts.Need("Rifle Receiver");
        var barrel = ConstructParts.Need("Gun Barrel");
        var block = ConstructParts.Need("Frame Block");
        var rc = ConstructBuild.PlaceNew(w, receiver, new Vector3(20, 0, 20), 0f);
        if (rc == null)
        {
            Fail("sandbox snap build: the receiver didn't place");
            return;
        }
        rc.Owner = me;
        int Sock(Construct c, int part, string name) => Array.FindIndex(c.DefOf(part).Sockets, s => s.Name == name);
        int AttachBy(Construct c, ConstructPartDef def, int part, string socket, string own)
        {
            var os = Array.FindIndex(def.Sockets, s => s.Name == own);
            for (var spin = 0; spin < 4; spin++)
                if (ConstructBuild.Place(c, part, Sock(c, part, socket), def, os, spin, out var g) == null)
                    return ConstructBuild.Attach(w, c, def, g);
            return -1;
        }
        var top = AttachBy(rc, block, 0, "bottom", "+y"); // a block under the receiver
        cache.Refresh(rc, barrel);
        var onlyFront = cache.Count > 0;
        for (var k = 0; k < cache.Count; k++)
            onlyFront &= rc.DefOf(cache.Part[k]) == receiver && receiver.Sockets[cache.Socket[k]].Name == "front" && cache.Type[k] == ConnType.Barrel;
        Want(top >= 0 && onlyFront && cache.Count == 1, $"a held barrel should see exactly the receiver's front ({cache.Count} cached, only front {onlyFront})");
        var blockCache = new SnapCache();
        blockCache.Refresh(rc, block);
        Want(blockCache.Count > 3, "a held block should still see the free faces of every part");
        var muzzleCache = new SnapCache();
        muzzleCache.Refresh(rc, ConstructParts.Need("Gun Muzzle Brake"));
        Want(muzzleCache.Count == 0, "a muzzle device needs a barrel's front: none on a bare receiver");

        var frontIdx = Sock(rc, 0, "front");
        rc.SocketAt(0, frontIdx, out var fat, out _);
        rc.SegPose(0, out var fq, out var ft);
        var fworld = rc.ToWorld(ft + Vector3.Transform(fat, fq));
        var size = new Vector2(1920, 1080);
        Matrix4x4 View(Vector3 look)
        {
            var eyeAt = fworld + new Vector3(0.6f, 0.3f, -4f);
            return Matrix4x4.CreateLookAt(eyeAt, look, Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(1.2f, size.X / size.Y, 0.1f, 100f);
        }
        var near = SnapBuild.Magnet(rc, cache, View(fworld + new Vector3(0.12f, 0.08f, 0)), size, SnapBuild.SnapPixels);
        var far = SnapBuild.Magnet(rc, cache, View(fworld + new Vector3(2.5f, 1.5f, 0)), size, SnapBuild.SnapPixels);
        var exact = SnapBuild.Magnet(rc, cache, View(fworld), size, SnapBuild.SnapPixels);
        Want(near == 0 && exact == 0 && far < 0, $"the magnet should take the front from a near aim (got {near}), exactly on it ({exact}) and not from far off ({far})");
        Want(SnapBuild.Fit(rc, barrel, cache.Part[0], cache.Socket[0], -1, out var bg) && bg.Part == 0 && bg.Socket == frontIdx, "the barrel should fit the front");

        // 2. A gearbox: mounted by a structural face, its input against an engine's output.
        var engine = ConstructParts.Need("Engine");
        var gear = ConstructParts.Need("5-Speed Gearbox");
        var beamDef = ConstructParts.Need("Frame Beam");
        var order = SnapBuild.OwnOrder(gear);
        Want(order.Length == gear.Sockets.Length && SnapBuild.OwnType(gear, order[0]) == ConnType.Mount, "a gearbox should try a structural face first");
        var shafts = 0;
        foreach (var o in order)
            shafts += SnapBuild.OwnType(gear, o) is ConnType.ShaftIn or ConnType.ShaftOut ? 1 : 0;
        Want(shafts == 2, "a gearbox has a shaft in and a shaft out");
        var gw = new ConstructWorld();
        var ew = ConstructBuild.PlaceNew(gw, engine, new Vector3(40, 0, 40), 0f)!;
        var outPort = Sock(ew, 0, "-z");
        var mounted = SnapBuild.Fit(ew, gear, 0, outPort, -1, out var mg);
        var mountFirst = mounted && SnapBuild.OwnType(gear, mg.Variant / 4) == ConnType.Mount;
        var lined = 0;
        var cur = mg.Variant;
        Span<Lineup> lines = stackalloc Lineup[8];
        for (var k = 0; k < order.Length && mounted; k++)
        {
            var nv = SnapBuild.NextOwn(ew, gear, 0, outPort, cur);
            if (nv < 0)
                break;
            cur = nv;
            if (SnapBuild.OwnType(gear, nv / 4) == ConnType.ShaftIn && ConstructBuild.Place(ew, 0, outPort, gear, nv / 4, nv % 4, out var sg) == null)
            {
                lined = SnapBuild.Lineups(ew, gear, sg, lines);
                break;
            }
        }
        Want(mountFirst, "a gearbox against an engine's output should mount by a structural face first");
        Want(lined > 0 && lines[0].Type == ConnType.ShaftIn && ew.DefOf(lines[0].Part) == engine, "switching the gearbox to its shaft in should mark the connection to the engine's output");
        Span<ConstructGhost> gh = stackalloc ConstructGhost[ConstructBuild.MaxGhosts];

        // 3. Own connection points of every part with sockets.
        var bad = 0;
        var singles = 0;
        Span<OwnPoint> pts = stackalloc OwnPoint[32];
        foreach (var d in ConstructParts.All)
        {
            if (d.Sockets.Length == 0 || d.Sockets.Length > 32)
                continue;
            var n = SnapBuild.OwnPoints(d, pts);
            var usable = 0;
            for (var k = 0; k < n; k++)
            {
                if (!pts[k].Usable)
                    continue;
                usable++;
                if (SnapBuild.SinglePurpose(d) && pts[k].Type == ConnType.Mount)
                    bad++;
            }
            if (n != d.Sockets.Length || usable == 0)
                bad++;
            singles += SnapBuild.SinglePurpose(d) ? 1 : 0;
        }
        Want(bad == 0 && singles > 0, $"every part with sockets should report its points, a single-purpose one only functional ({bad} bad)");
        Want(SnapBuild.SinglePurpose(ConstructParts.Need("Wheel")) && SnapBuild.OwnType(ConstructParts.Need("Wheel"), 0) == ConnType.Hub, "a wheel is single-purpose by its hub");

        // 4. The switch key: another own point mates.
        var ec = new ConstructWorld();
        var plat = ConstructBuild.PlaceNew(ec, beamDef, new Vector3(60, 0, 60), 0f)!;
        var sc = new SnapCache();
        sc.Refresh(plat, engine);
        var switched = false;
        if (sc.Count > 0 && SnapBuild.Fit(plat, engine, sc.Part[0], sc.Socket[0], -1, out var first))
        {
            var nv = SnapBuild.NextOwn(plat, engine, first.Part, first.Socket, first.Variant);
            switched = nv >= 0 && nv / 4 != first.Variant / 4;
        }
        Want(switched, "the switch key should move an engine to another of its own points");

        // 5. Quick building: six lamps along a beam with one held part; the part stays in the hands.
        var panel = ConstructParts.Need("Lamp");
        var pw = new ConstructWorld();
        var qh = new ConstructEdits { InfiniteParts = true };
        var beam = ConstructBuild.PlaceNew(pw, beamDef, new Vector3(80, 0, 80), 0f)!;
        beam.Owner = me;
        qh.SetQuick(me, true);
        ref var qhand = ref qh.Hand(me);
        qhand.Held = panel.Id;
        qhand.Tool = panel.Tool;
        var eye = beam.WorldCom + new Vector3(0, 1.2f, 0);
        var qcache = new SnapCache();
        var placed = 0;
        var refused = "";
        var now = 0.0;
        for (var round = 0; round < 6; round++)
        {
            var n = SnapBuild.Ghosts(beam, panel, qcache, gh, beam.Com, 6f);
            var done = false;
            for (var k = 0; k < n && !done; k++)
            {
                var before = beam.PartCount;
                var why = qh.BeginAttach(pw, me, eye, beam.Id, gh[k].Part, gh[k].Socket, gh[k].Variant, now);
                if (why != null)
                    refused = why;
                else if (beam.PartCount == before + 1)
                {
                    placed++;
                    done = true;
                }
            }
        }
        Want(placed == 6 && qhand.Held == panel.Id && qhand.Edit == EditKind.None, $"quick building should place 6 lamps with one held part ({placed} placed, still held {qhand.Held == panel.Id}, last refusal \"{refused}\")");
        var plain = new ConstructEdits { InfiniteParts = true };
        ref var ph = ref plain.Hand(me);
        ph.Held = panel.Id;
        ph.Tool = panel.Tool;
        var before2 = beam.PartCount;
        var sc2 = new SnapCache();
        var n2 = SnapBuild.Ghosts(beam, panel, sc2, gh, beam.Com, 6f);
        if (n2 > 0)
            plain.BeginAttach(pw, me, eye, beam.Id, gh[0].Part, gh[0].Socket, gh[0].Variant, now);
        Want(n2 > 0 && beam.PartCount == before2 + 1 && ph.Held == ConstructParts.None, "without quick building the part leaves the hands when it is fitted");

        SnapStrokeCases(problems, beamDef, panel);

        // 6. Pick, undo.
        var count = beam.PartCount;
        Want(qh.UndoCount(me) == 6, $"six placements should be remembered ({qh.UndoCount(me)})");
        var u1 = qh.UndoPlaced(pw, me, eye);
        Want(u1 == null && beam.PartCount == count - 1, $"undo should remove the last placement (\"{u1}\", {beam.PartCount} of {count})");
        var theirs = qh.UndoPlaced(pw, other, eye);
        Want(theirs != null, "undo for a player who placed nothing is refused");
        beam.Owner = 7;
        var u2 = qh.UndoPlaced(pw, me, eye);
        Want(u2 != null && beam.PartCount == count - 1, $"undo on someone else's construct is refused (\"{u2}\")");
        beam.Owner = me;
        qhand.Held = ConstructParts.None;
        var pick = qh.PickPart(pw, me, eye, beam.Id, 0);
        Want(pick == null && qhand.Held == beamDef.Id, $"picking the beam should put a Frame Beam in the hands (\"{pick}\")");
        var far2 = qh.PickPart(pw, me, eye + new Vector3(0, 30, 0), beam.Id, 0);
        Want(far2 != null, "picking out of reach is refused");
        var surv = new ConstructEdits { InfiniteParts = false };
        Want(surv.QuickBuild(me) == false && surv.UndoPlaced(pw, me, eye) != null, "no quick building or undo outside creative");

        // 7. The cache: refreshed only on change, nothing allocated in a steady frame.
        var cc = new SnapCache();
        cc.Refresh(beam, panel);
        var rebuilds = cc.Rebuilds;
        GC.Collect();
        var a0 = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 2000; k++)
        {
            cc.Refresh(beam, panel);
            SnapBuild.Magnet(beam, cc, View(fworld), size, 80f);
        }
        var alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        Want(cc.Rebuilds == rebuilds && alloc < 4096, $"a steady frame should not rebuild or allocate ({cc.Rebuilds - rebuilds} rebuilds, {alloc} B over 2000 frames)");
        qhand.Held = panel.Id;
        qhand.Tool = panel.Tool;
        var ng = SnapBuild.Ghosts(beam, panel, cc, gh, beam.Com, 6f);
        if (ng > 0)
            qh.BeginAttach(pw, me, eye, beam.Id, gh[0].Part, gh[0].Socket, gh[0].Variant, now);
        cc.Refresh(beam, panel);
        Want(cc.Rebuilds == rebuilds + 1, "adding a part should rebuild the cache once");

        Line($"snap build: barrel front cache {cache.Count}, magnet near {near} far {far}; gearbox lineups {lined}; {placed} lamps with one held part; undo {u1 ?? "ok"}; cache {cc.Rebuilds - rebuilds} rebuild, {alloc} B");
        if (problems.Count > 0)
            Fail($"sandbox snap build: {string.Join("; ", problems)}");
    }

    /// <summary>
    /// Hold to paint (board item dev-sandbox-paint-too-strong-pt01): a press places exactly one part even when the new
    /// part's own socket ends up under the crosshair; holding still places no more; sweeping across free beam sockets
    /// places one per socket and never on a part the stroke placed; the repeat rate is limited; the setting turns it off.
    /// A stationary press is the case the first version never tested (a chain grew toward the player at frame rate).
    /// </summary>
    private static void SnapStrokeCases(List<string> problems, ConstructPartDef beamDef, ConstructPartDef lamp)
    {
        // Runs one stroke: hover(t) names the socket index under the crosshair at time t (-1: the newest part's socket).
        // Returns the times of the placements and whether any landed on a part the stroke placed.
        static (List<double> times, bool onOwn, int start, int end) Stroke(ConstructPartDef beamDef, ConstructPartDef lamp, double release, Func<double, int> hover, double step, bool paintOn = true)
        {
            var wd = new ConstructWorld();
            var ed = new ConstructEdits { InfiniteParts = true };
            var b = ConstructBuild.PlaceNew(wd, beamDef, new Vector3(120, 0, 120), 0f)!;
            b.Owner = 0;
            ed.SetQuick(0, true);
            ref var hd = ref ed.Hand(0);
            hd.Held = lamp.Id;
            hd.Tool = lamp.Tool;
            var eyeAt = b.WorldCom + new Vector3(0, 1.2f, 0);
            var cache = new SnapCache();
            var gs = new ConstructGhost[128];
            // a second beam joined end to end: a straight side of eight sockets to sweep along
            var nb = SnapBuild.Ghosts(b, beamDef, new SnapCache(), gs, b.Com, 6f);
            for (var k = 0; k < nb; k++)
                if (gs[k].Part == 0 && gs[k].Socket == 0)
                {
                    ConstructBuild.Attach(wd, b, beamDef, in gs[k]);
                    break;
                }
            var n0 = SnapBuild.Ghosts(b, lamp, cache, gs, b.Com, 10f);
            // the free sockets as first seen, in order along the beam (end-, x+0 .. x+3, end+ line up on the beam's axis)
            var cand = new List<(int part, int socket, float z)>();
            for (var k = 0; k < n0; k++)
                if (!cand.Exists(x => x.part == gs[k].Part && x.socket == gs[k].Socket) && Math.Abs(gs[k].Pos.Y) < 0.01f && gs[k].Pos.X > 0.3f)
                    cand.Add((gs[k].Part, gs[k].Socket, gs[k].Pos.Z));
            cand.Sort((a, c2) => a.z.CompareTo(c2.z));
            if (cand.Count > 6)
                cand.RemoveRange(6, cand.Count - 6);
            var stroke = new SnapStroke();
            var times = new List<double>();
            var onOwn = false;
            var start = b.PartCount;
            var t = 0.0;
            for (var f = 0; t <= release + 1e-9; f++, t = f * step)
            {
                if (f == 0)
                    stroke.Press(b.Id, b.PartCount, t);
                var idx = hover(t);
                var n = SnapBuild.Ghosts(b, lamp, cache, gs, b.Com, 10f);
                var pick = -1;
                for (var k = 0; k < n && pick < 0; k++)
                    if (idx < 0 ? gs[k].Part >= start : idx < cand.Count && gs[k].Part == cand[idx].part && gs[k].Socket == cand[idx].socket)
                        pick = k;
                if (pick < 0)
                    continue;
                ref var g0 = ref gs[pick];
                b.SegPose(b.Parts[g0.Part].Seg, out var wq, out var wt);
                var at = b.ToWorld(wt + Vector3.Transform(g0.Pos, wq));
                var key = ((long)b.Id << 24) | ((long)g0.Part << 8) | (uint)g0.Socket;
                var go = f == 0 || (paintOn && stroke.Want(t, b.Id, b.PartCount, g0.Part, key, at));
                if (!go)
                    continue;
                var before = b.PartCount;
                if (ed.BeginAttach(wd, 0, eyeAt, b.Id, g0.Part, g0.Socket, g0.Variant, t) == null && b.PartCount == before + 1)
                {
                    onOwn |= g0.Part >= start;
                    stroke.Sent(b.Id, b.PartCount, t, key, at);
                    times.Add(t);
                }
            }
            return (times, onOwn, start, b.PartCount);
        }

        // A click with the new part's socket under the crosshair at once: exactly one.
        var click = Stroke(beamDef, lamp, 0.05, t => t < 0.001 ? 0 : -1, 1.0 / 60);
        Want1(click.times.Count == 1, $"a click should place exactly 1 part ({click.times.Count})");
        // Holding still for 2 s on the new part's socket: still one (and none on the stroke's own parts).
        var still = Stroke(beamDef, lamp, 2.0, t => t < 0.001 ? 0 : -1, 1.0 / 60);
        Want1(still.times.Count == 1 && !still.onOwn, $"holding still 2 s should place 1 ({still.times.Count}, onOwn {still.onOwn})");
        // Holding still on the same pre-existing socket: one.
        var same = Stroke(beamDef, lamp, 2.0, _ => 0, 1.0 / 60);
        Want1(same.times.Count == 1, $"holding still on one socket should place 1 ({same.times.Count})");
        // Sweeping across six free sockets, 0.4 s each, the first at the press: six, none on the stroke's own parts.
        var sweep = Stroke(beamDef, lamp, 6 * 0.4, t => (int)(t / 0.4), 1.0 / 60);
        Want1(sweep.times.Count == 6 && !sweep.onOwn && sweep.end == sweep.start + 6, $"sweeping 6 sockets should place 6, none on stroke parts ({sweep.times.Count}, onOwn {sweep.onOwn})");
        // A fast sweep (30 ms per socket) cannot out-run the limit; a sweep that ends before the hold time paints nothing.
        var fast = Stroke(beamDef, lamp, 1.2, t => (int)(t / 0.03) % 6, 1.0 / 240);
        var gapOk = true;
        for (var k = 1; k < fast.times.Count; k++)
            gapOk &= fast.times[k] - fast.times[k - 1] >= SnapStroke.MinGap - 1e-6;
        Want1(gapOk && (fast.times.Count < 2 || fast.times[1] >= SnapStroke.HoldSecs - 1e-6), $"paints should keep {SnapStroke.MinGap} s apart and wait {SnapStroke.HoldSecs} s ({string.Join(",", fast.times.Select(x => x.ToString("0.00")))})");
        var quickSweep = Stroke(beamDef, lamp, 0.25, t => (int)(t / 0.04), 1.0 / 60);
        Want1(quickSweep.times.Count == 1, $"a stroke released before the hold time should place 1 ({quickSweep.times.Count})");

        void Want1(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
    }
}
