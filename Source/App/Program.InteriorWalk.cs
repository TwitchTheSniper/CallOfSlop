using System.Numerics;
using CallOfSlop.Interiors;
using CallOfSlop.VehicleKit;

namespace CallOfSlop;

public static unsafe partial class Program
{
    /// <summary>
    /// <c>--interior-walk [fitted blueprint file] [--of base blueprint] [--seed S] [--damage] [--walk S]</c>: a scripted player walk-through of a fitted
    /// construct (docs/sandbox/INTERIORS.md "Phase 6"). The construct is built frozen in the air over the Proving Ground; a player
    /// (the server's own movement, World.Simulate and ConstructWorld.BlockPlayer, with the ride frame) walks every corridor end to
    /// end, through every doorway both ways (event doors opened with the Use path first; one shut door, and a bulkhead line sealed from the bridge, must stop them), up and down every
    /// stair flight, and rides every lift car on its button and walks off at the landing. Every place they stick, fall, don't fit or are
    /// pushed aside is printed, and furniture standing in front of a doorway. --damage shoots long panels and a slab until they break and
    /// sets off blasts on panels, walls and a slab, printing what came off and in which bulkhead sections. With --walk S the construct is then set on the ground, its bank charged, and driven forward S seconds
    /// with one player in a room and one on a lift car: both must stay with the frame.
    /// </summary>
    private static int InteriorWalk(string[] args)
    {
        string? Flag(string f) => args.SkipWhile(a => !a.Equals(f, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        var file = args.SkipWhile(a => !a.Equals("--interior-walk", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() is { } fa && !fa.StartsWith("--", StringComparison.Ordinal) ? fa : Path.Combine(VehicleRegistry.RepoBlueprints(), "TitanWalkerFitted.json");
        var ofName = Flag("--of") ?? Path.Combine(VehicleRegistry.RepoBlueprints(), "TitanWalker.json");
        var seed = int.TryParse(Flag("--seed"), out var sd) ? sd : 1;
        var walkSeconds = float.TryParse(Flag("--walk"), System.Globalization.CultureInfo.InvariantCulture, out var ws) ? ws : 0f;
        var data = InteriorData.Load(InteriorData.Folder);
        var baseBp = ConstructBuild.ParseBlueprint(File.ReadAllText(ofName), Path.GetFileName(ofName));
        var fitted = ConstructBuild.ParseBlueprint(File.ReadAllText(file), Path.GetFileName(file));
        // the plans, as --gen-interior makes them (cheap: the base's few parts and the planner)
        var b = InteriorBuilder.From(data, baseBp);
        var top = b.Class!;
        InteriorPlan? vplan = null;
        if (top.Chassis.Count > 0 && b.VoidHull(out _) is { } hollow)
        {
            var decks = (int)MathF.Floor(hollow.Size.Y / top.Deck.Storey + 1e-4f);
            var vcls = top with { Deck = top.Deck with { Decks = decks, ReserveRoute = false }, Programme = top.Chassis, Fill = top.Fill.Where(f => top.Chassis.Contains(f)).ToList() };
            vplan = InteriorPlanner.Plan(data, vcls, hollow, seed, @sealed: true);
            var rest = top.Programme.ToList();
            foreach (var room in top.Chassis)
                rest.Remove(room);
            top = top with { Programme = rest };
        }
        var plan = InteriorPlanner.Plan(data, top, b.Hull!, seed);
        var map = Maps.Find("Proving Ground") ?? Maps.Find("Construct Yard")!;
        World.SetMap(map);
        World.OpenSky = true;
        var q = World.Query;
        var w = new ConstructWorld();
        var g0 = ConstructWorld.Ground(q, 0, 0);
        var c = ConstructBuild.BuildOnGround(w, fitted, g0 + new Vector3(0, 60, 0), 0, out var why);
        if (c == null)
        {
            Console.WriteLine($"walk: the fitted construct doesn't build: {why}");
            return 1;
        }
        c.Rebuild();
        c.Anchored = c.Frozen = true;
        var shift = c.Parts[0].Pos - b.RootPos; // the builder's rest frame to this body's
        Vector3 Floor(int level, int x, int z) => b.FloorAt(level, x, z) + shift;
        Console.WriteLine($"walk: {fitted.Name}: {c.PartCount} parts, {c.JointCount} joints on {map.Name}; charge {c.Charge:0} of {c.ChargeCapacity:0} kJ");
        var problems = new List<string>();
        var counts = new SortedDictionary<string, (int Ok, int Bad)>();
        void Tally(string what, bool ok, string? problem)
        {
            var v = counts.GetValueOrDefault(what);
            counts[what] = ok ? (v.Ok + 1, v.Bad) : (v.Ok, v.Bad + 1);
            if (!ok && problem != null)
                problems.Add($"{what}: {problem}");
        }
        void Tick(ref MoveState s, Buttons bt, float yaw, bool world = false)
        {
            World.Simulate(ref s, new InputCmd { Buttons = bt, Yaw = yaw, SpeedPct = 100 });
            w.BlockPlayer(ref s, World.HeightOf(s.Stance), server: true, who: 1);
            if (!world)
                return;
            w.Step(q, World.TickDt);
            if (s.RideUp != Vector3.Zero && w.Get(s.RideBody) is { } rc && s.RideSeg <= rc.JointCount)
            {
                var d = s.Position - s.RideWorld;
                ConstructRide.Place(ref s, rc);
                s.Position += d;
            }
        }
        MoveState Drop(Vector3 local)
        {
            var s = new MoveState { Position = c.ToWorld(local) + new Vector3(0, 0.05f, 0) };
            for (var t = 0; t < 15; t++)
                Tick(ref s, Buttons.None, 0);
            return s;
        }
        // Walks towards a local point (on its own level), steering every tick; (reached, why not, how far pushed off the line, the height change)
        (bool Ok, string Why, float Off, float Dy) WalkTo(ref MoveState s, Vector3 local, float seconds, bool climb = false)
        {
            var from = c.ToLocal(s.Position);
            var line = new Vector2(local.X - from.X, local.Z - from.Z);
            var len = line.Length();
            var dir = len > 0 ? line / len : Vector2.UnitX;
            float off = 0, best = len;
            var lastBest = 0;
            for (var t = 0; t < seconds * World.TickRate; t++)
            {
                var at = c.ToLocal(s.Position);
                var to = new Vector2(local.X - at.X, local.Z - at.Z);
                var dist = to.Length();
                if (dist < 0.25f)
                {
                    for (var k = 0; k < 20; k++) // (settled where it ended: off a stair's last step)
                        Tick(ref s, Buttons.None, 0);
                    return (true, "", off, c.ToLocal(s.Position).Y - from.Y);
                }
                var rel = new Vector2(at.X - from.X, at.Z - from.Z);
                off = MathF.Max(off, MathF.Abs(rel.X * dir.Y - rel.Y * dir.X));
                if (at.Y < from.Y - (climb ? 4f : 1.0f))
                    return (false, $"fell {from.Y - at.Y:0.0} m at {Fmt(at)}", off, at.Y - from.Y);
                if (dist < best - 0.05f)
                {
                    best = dist;
                    lastBest = t;
                }
                else if (t - lastBest > World.TickRate)
                    return (false, $"stuck {dist:0.0} m short at {Fmt(at)}", off, at.Y - from.Y);
                var wd = c.ToWorld(local) - s.Position;
                Tick(ref s, Buttons.Forward, MathF.Atan2(wd.X, -wd.Z));
            }
            var end = c.ToLocal(s.Position);
            return (false, $"out of time {new Vector2(local.X - end.X, local.Z - end.Z).Length():0.0} m short at {Fmt(end)}", off, end.Y - from.Y);
        }
        var f0 = Floor(0, 0, 0);
        string Near(Vector3 l)
        {
            var hits = new List<string>();
            for (var i = 0; i < c.PartCount && hits.Count < 6; i++)
            {
                c.RestBounds(i, out var lo, out var hi);
                if (c.Parts[i].Seg != 0)
                {
                    var (a0, b0) = (lo, hi);
                    lo = new Vector3(float.MaxValue);
                    hi = new Vector3(float.MinValue);
                    for (var k = 0; k < 8; k++)
                    {
                        var p = c.ToLocal(c.SegToWorld(c.Parts[i].Seg, new Vector3((k & 1) != 0 ? b0.X : a0.X, (k & 2) != 0 ? b0.Y : a0.Y, (k & 4) != 0 ? b0.Z : a0.Z)));
                        lo = Vector3.Min(lo, p);
                        hi = Vector3.Max(hi, p);
                    }
                }
                if (l.X > lo.X - 0.45f && l.X < hi.X + 0.45f && l.Z > lo.Z - 0.45f && l.Z < hi.Z + 0.45f && l.Y + 1.8f > lo.Y && l.Y - 0.5f < hi.Y && i != 0)
                    hits.Add($"{c.DefOf(i).Name} y {lo.Y:0.00}..{hi.Y:0.00}");
            }
            return string.Join("; ", hits);
        }
        string Fmt(Vector3 l) => $"({l.X:0.0}, {l.Y:0.0}, {l.Z:0.0}; L{Level(l.Y)} block {MathF.Floor((l.X - f0.X + 2) / 4):0},{MathF.Floor((l.Z - f0.Z + 2) / 4):0} +{((l.X - f0.X + 2) % 4 + 4) % 4:0.0},{((l.Z - f0.Z + 2) % 4 + 4) % 4:0.0})";
        int Level(float localY) => (int)MathF.Round((localY - Floor(0, 0, 0).Y) / 3f);

        // ---- doors: every event door opened through the Use path (eye in reach) and settled (phase 7: no joints); a double door's
        // second leaf opens with the first, so only doors not already heading open are used
        var leaves = Enumerable.Range(0, c.PartCount).Where(i => c.DefOf(i).Door != null).ToList();
        var e = new ConstructEdits { Query = q, InfiniteParts = true };
        string? UseFrom(int part, Vector3 eye) => e.Use(w, 0, eye, c.Id, part);
        Vector3 MidOf(int part)
        {
            c.RestBounds(part, out var lo, out var hi);
            return (lo + hi) * 0.5f;
        }
        var refusedUse = 0;
        c.EnsureDoors();
        foreach (var i in leaves)
        {
            if (ConstructDoors.TargetOpen(c, i))
                continue;
            var mid = MidOf(i);
            if (UseFrom(i, c.ToWorld(mid + new Vector3(0, 0.3f, 0))) is { } r && refusedUse++ < 3)
                problems.Add($"door: Use refused on the door at {Fmt(mid)}: {r}");
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var worstAll = 0.0;
        for (var t = 0; t < 120; t++)
        {
            var t0 = clock.Elapsed.TotalMilliseconds;
            w.Step(q, World.TickDt);
            worstAll = Math.Max(worstAll, clock.Elapsed.TotalMilliseconds - t0);
        }
        c.EnsureDoors();
        var openNow = leaves.Count(i => ConstructDoors.SettledOpen(c, i));
        Console.WriteLine($"walk: {leaves.Count} door parts ({c.DoorCount} listed: {leaves.Count(i => c.DefOf(i).Door!.Slide)} sliding), {openNow} open after Use; 120 ticks frozen took {clock.ElapsedMilliseconds} ms (worst {worstAll:0} ms, every door settling at once)");
        Tally("doors open on Use", openNow == leaves.Count, $"{leaves.Count - openNow} of {leaves.Count} not open");

        var doorways = new List<(Vector3 Mid, Vector3 N)>();
        {
            var flat = 0;
            foreach (var i in leaves)
            {
                c.RestBounds(i, out var lo, out var hi);
                if (hi.Y - lo.Y < 1f && flat++ < 6)
                    problems.Add($"door: the door at {Fmt(lo)}..{Fmt(hi)} lies flat open");
            }
            Tally("door leaves upright", flat == 0, null);
        }
        // ---- doorways: every Door Wall, Door Corner and Garage Wall, both ways, 1.6 m out each side
        foreach (var i in Enumerable.Range(0, c.PartCount))
        {
            var name = c.DefOf(i).Name;
            if (name is not ("Steel Door Wall 2 m" or "Steel Door Corner" or "Steel Garage Wall 4 m") || c.Parts[i].Seg != 0)
                continue;
            var rot = c.Parts[i].Rot;
            var mid = c.Parts[i].Pos + Rot24.Apply(rot, new Vector3(name.StartsWith("Steel Garage", StringComparison.Ordinal) ? 1.875f : 0.875f, -0.1f, 0));
            var n = Rot24.Apply(rot, Vector3.UnitZ);
            var kind = $"doorway L{Level(mid.Y)} {name}";
            doorways.Add((mid, n));
            foreach (var side in new[] { 1f, -1f })
            {
                var start = mid + n * 1.6f * side;
                var s = Drop(start);
                var at = c.ToLocal(s.Position);
                if (Vector3.Distance(at, start) > 0.4f)
                {
                    Tally(kind + " (start spot)", false, $"the spot 1.6 m out at {Fmt(start)} won't hold a player (ended at {Fmt(at)}) near {Near(start)}");
                    continue;
                }
                var r = WalkTo(ref s, mid - n * 1.6f * side, 5f);
                var ok = r.Ok && MathF.Abs(r.Dy) < 0.3f;
                Tally(kind, ok, ok ? null : $"from {Fmt(start)}: {(r.Ok ? $"came out {r.Dy:0.00} m off the floor" : r.Why)}; near {Near(c.ToLocal(s.Position))}");
                if (r.Ok && r.Off > 0.35f)
                    Tally(kind + " (pushed)", false, $"from {Fmt(start)}: pushed {r.Off:0.00} m off the line");
            }
        }

        // ---- furniture in front of a doorway: anything standing on a floor within 1 m of an opening's middle, either side
        {
            var shelfy = new HashSet<string> { "Steel Floor 4x4 m", "Steel Stair Floor 4x4 m" };
            var near = 0;
            foreach (var (m, n) in doorways)
                for (var i = 0; i < c.PartCount; i++)
                {
                    var d = c.DefOf(i);
                    if (c.Parts[i].Seg != 0 || d.Role is PartRole.Door or PartRole.Frame || shelfy.Contains(d.Name) || d.Name.Contains("Wall", StringComparison.Ordinal))
                        continue;
                    c.RestBounds(i, out var lo, out var hi);
                    if (hi.Y < m.Y + 0.2f || lo.Y > m.Y + 0.4f || d.Role is PartRole.Hinge or PartRole.Joint)
                        continue;
                    var p = Vector3.Clamp(m, lo, hi);
                    if (new Vector2(p.X - m.X, p.Z - m.Z).Length() < 1.0f && near++ < 8)
                        problems.Add($"furniture: a {d.Name} ({Fmt(lo)}..{Fmt(hi)}) stands within 1 m of the doorway at {Fmt(m)}");
                }
            Tally("furniture clear of doorways", near == 0, null);
        }
        // ---- corridors: every deck's spine, end to end along its middle, through the bulkheads
        foreach (var (p, lv) in vplan != null ? new[] { (vplan, b.VoidLevel), (plan, 0) } : new[] { (plan, 0) })
            for (var k = 0; k < p.Decks; k++)
            {
                var rows = Enumerable.Range(0, p.D).Where(z => Enumerable.Range(0, p.W).Count(x => p.Cells[k][x, z] == InteriorPlan.Corridor) > p.W / 2).ToList();
                if (rows.Count == 0)
                    continue;
                var xs = Enumerable.Range(0, p.W).Where(x => rows.All(z => p.Cells[k][x, z] == InteriorPlan.Corridor)).ToList();
                if (xs.Count < 2)
                    continue;
                var zc = rows[rows.Count / 2];
                var zOff = rows.Count % 2 == 0 ? -2f : 0f; // (an even spine: walk its middle line, between two rows)
                var a = Floor(k + lv, xs[0], zc) + new Vector3(0, 0, zOff);
                var s = Drop(a);
                var segs = 0;
                string? bad = null;
                // block by block, so a break in the spine (a keep-out) is walked round no further than the next block
                for (var j = 1; j < xs.Count && bad == null; j++)
                {
                    if (xs[j] != xs[j - 1] + 1)
                    {
                        s = Drop(Floor(k + lv, xs[j], zc) + new Vector3(0, 0, zOff));
                        continue;
                    }
                    // (a bulkhead on the block's west edge: through its doorway)
                    var edge = Floor(k + lv, xs[j], zc).X - 2;
                    var door = doorways.FirstOrDefault(d => MathF.Abs(d.Mid.X - edge) < 0.6f && MathF.Abs(d.N.X) > 0.9f && Level(d.Mid.Y) == k + lv
                        && rows.Any(z => MathF.Abs(d.Mid.Z - Floor(0, 0, z).Z) < 2.1f));
                    if (door.N != Vector3.Zero)
                    {
                        var w1 = WalkTo(ref s, door.Mid - Vector3.UnitX * 1.2f, 4f);
                        var w2 = w1.Ok ? WalkTo(ref s, door.Mid + Vector3.UnitX * 1.2f, 4f) : w1;
                        if (!w2.Ok)
                        {
                            bad = $"level {k + lv} the bulkhead door at block {xs[j]}: {w2.Why}";
                            break;
                        }
                    }
                    var r = WalkTo(ref s, Floor(k + lv, xs[j], zc) + new Vector3(0, 0, zOff), 4f);
                    if (!r.Ok)
                        bad = $"level {k + lv} block {xs[j]}: {r.Why}";
                    else if (r.Off > 0.35f)
                        bad = $"level {k + lv} block {xs[j]}: pushed {r.Off:0.00} m off the line";
                    segs++;
                }
                Tally($"corridor L{k + lv}", bad == null, bad);
            }

        // ---- stairs: every flight up from its foot and down from its top
        foreach (var i in Enumerable.Range(0, c.PartCount).Where(i => c.DefOf(i).Name == "Steel Straight Stair 3 m"))
        {
            var rot = c.Parts[i].Rot;
            var o = c.Parts[i].Pos;
            var foot = o + Rot24.Apply(rot, new Vector3(0.375f, -0.1f, -0.55f));
            var head = o + Rot24.Apply(rot, new Vector3(0.375f, 2.9f, 3.5f));
            var kind = $"stairs L{Level(foot.Y)} up";
            var s = Drop(foot);
            if (Vector3.Distance(c.ToLocal(s.Position), foot) > 0.4f)
            {
                Tally(kind, false, $"the foot at {Fmt(foot)} won't hold a player (at {Fmt(c.ToLocal(s.Position))}) near {Near(foot)}");
                continue;
            }
            var r = WalkTo(ref s, head, 6f, climb: true);
            Tally(kind, r.Ok && MathF.Abs(r.Dy - 3f) < 0.45f, $"from {Fmt(foot)}: {(r.Ok ? $"rose {r.Dy:0.00} m" : r.Why)}");
            kind = $"stairs L{Level(foot.Y) + 1} down";
            s = Drop(head);
            if (Vector3.Distance(c.ToLocal(s.Position), head) > 0.4f)
            {
                Tally(kind, false, $"the top at {Fmt(head)} won't hold a player (at {Fmt(c.ToLocal(s.Position))})");
                continue;
            }
            r = WalkTo(ref s, foot, 6f, climb: true);
            Tally(kind, r.Ok && MathF.Abs(r.Dy + 3f) < 0.45f, $"from {Fmt(head)}: {(r.Ok ? $"fell {-r.Dy:0.00} m, at {Fmt(c.ToLocal(s.Position))} near {Near(c.ToLocal(s.Position))}" : r.Why)}");
        }

        // ---- one shut door stops a player: the first unwired swing door shut again and walked into; then a bulkhead line sealed from
        // the bridge (its Switch on: the doors shut and lock), walked into, F refused, and unsealed again
        bool Blocks(int i, string kind)
        {
            c.RestBounds(i, out var lo, out var hi);
            var mid = (lo + hi) * 0.5f;
            var thin = (hi - lo).X < (hi - lo).Z ? Vector3.UnitX : Vector3.UnitZ;
            var s = Drop(new Vector3(mid.X, lo.Y + 0.05f, mid.Z) + thin * 1.5f);
            var r = WalkTo(ref s, new Vector3(mid.X, lo.Y + 0.05f, mid.Z) - thin * 1.5f, 3f);
            return !r.Ok;
        }
        double worstDoorTick = 0;
        void Settle(int ticks)
        {
            var sw = new System.Diagnostics.Stopwatch();
            for (var t = 0; t < ticks; t++)
            {
                sw.Restart();
                w.Step(q, World.TickDt);
                worstDoorTick = Math.Max(worstDoorTick, sw.Elapsed.TotalMilliseconds);
            }
            c.EnsureDoors();
        }
        bool WiredInto(int door) => Enumerable.Range(0, c.WireCount).Any(k => c.Wires[k].To == door);
        if (leaves.FirstOrDefault(i => !c.DefOf(i).Door!.Slide && !WiredInto(i) && c.DefOf(i).Name.Contains("Swing", StringComparison.Ordinal)) is var sw0 and > 0)
        {
            UseFrom(sw0, c.ToWorld(MidOf(sw0) + new Vector3(0, 0.3f, 0)));
            Settle(90);
            Console.WriteLine($"walk: one door shut: worst tick {worstDoorTick:0.0} ms");
            worstDoorTick = 0;
            var shut = !ConstructDoors.SettledOpen(c, sw0);
            var stops = Blocks(sw0, "swing");
            Tally("door shut stops", shut && stops, $"shut {shut}, walked through {!stops}");
            UseFrom(sw0, c.ToWorld(MidOf(sw0) + new Vector3(0, 0.3f, 0)));
            Settle(90);
        }
        {
            var lineDoors = leaves.Where(WiredInto).ToList();
            var seal = lineDoors.Count == 0 ? -1 : Enumerable.Range(0, c.WireCount).Where(k => c.Wires[k].To == lineDoors[0] && c.DefOf(c.Wires[k].From).Name == "Switch").Select(k => (int)c.Wires[k].From).DefaultIfEmpty(-1).First();
            if (seal < 0)
                Tally("bulkhead sealed stops", false, "no bulkhead door wired from a Switch");
            else
            {
                var line = Enumerable.Range(0, c.WireCount).Where(k => c.Wires[k].From == seal && c.DefOf(c.Wires[k].To).Door != null).Select(k => (int)c.Wires[k].To).ToList();
                worstDoorTick = 0;
                var flip = UseFrom(seal, c.ToWorld(MidOf(seal) + new Vector3(0, 0.3f, 0)));
                Settle(120);
                var shut = line.Count(i => !ConstructDoors.SettledOpen(c, i));
                var locked = line.Count(i => ConstructDoors.Locked(c, i));
                var refused = UseFrom(line[0], c.ToWorld(MidOf(line[0]) + new Vector3(0, 0.3f, 0)));
                Settle(90);
                var stops = Blocks(line[0], "bulkhead");
                Console.WriteLine($"walk: seal switch at {Fmt(MidOf(seal))} ({(flip == null ? "worked" : flip)}): {shut} of {line.Count} bulkhead doors shut, {locked} locked; F on one: {(refused ?? "worked")}; walked through {!stops}; worst tick while doors moved {worstDoorTick:0.0} ms");
                Tally("bulkhead sealed stops", shut == line.Count && locked == line.Count && refused is { Length: > 0 } && stops && !ConstructDoors.SettledOpen(c, line[0]),
                    $"{shut} of {line.Count} shut, {locked} locked, F: {refused ?? "worked"}, walked through {!stops}");
                UseFrom(seal, c.ToWorld(MidOf(seal) + new Vector3(0, 0.3f, 0)));
                Settle(120);
                var reopened = line.Count(i => ConstructDoors.SettledOpen(c, i) && !ConstructDoors.Locked(c, i));
                Tally("bulkhead unsealed opens", reopened == line.Count, $"{reopened} of {line.Count} open and unlocked again");
            }
        }

        // ---- lifts: each car ridden up on its own button, then walked off at the landing; then called down from the lower landing
        var cars = Enumerable.Range(0, c.PartCount).Where(i => c.DefOf(i).Name == "Freight Lift Car").ToList();
        clock.Restart();
        foreach (var car in cars)
        {
            var seg = c.Parts[car].Seg;
            var button = Enumerable.Range(0, c.PartCount).FirstOrDefault(i => c.DefOf(i).Logic == CLogic.Button && c.Parts[i].Seg == seg, -1);
            c.PartBounds(car, out var lo, out var hi);
            var floorLocal = new Vector3((lo.X + hi.X) * 0.5f, lo.Y + 0.15f, (lo.Z + hi.Z) * 0.5f);
            var lvl = Level(floorLocal.Y);
            // where the car stands now (its segment's pose), the rider dropped on its middle
            var onCar = c.ToLocal(c.SegToWorld(seg, floorLocal)) + new Vector3(0, 0.2f, 0);
            var s = Drop(onCar);
            var y0 = s.Position.Y;
            var riding = s.RideBody == c.Id && s.RideSeg == seg;
            if (button < 0)
            {
                Tally($"lift L{lvl}", false, "the car has no button");
                continue;
            }
            var eye = s.Position + new Vector3(0, 1.6f, 0);
            var use = UseFrom(button, eye);
            for (var t = 0; t < 6 * World.TickRate; t++)
                Tick(ref s, Buttons.None, 0, world: true);
            var rose = s.Position.Y - y0;
            var stillOn = s.RideBody == c.Id && s.RideSeg == seg;
            Tally($"lift L{lvl} ride up", riding && use == null && rose is > 2.7f and < 3.3f && stillOn, $"car {car}: boarded {riding}, button {use ?? "pressed"}, rider rose {rose:0.00} m, still on the car {stillOn}");
            // walk off: the first of the four ways that gets 3 m off the car on the landing's level
            var at = c.ToLocal(s.Position);
            var off = false;
            string lastWhy = "";
            foreach (var d in new[] { Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitX, -Vector3.UnitX })
            {
                var s2 = s;
                var r = WalkTo(ref s2, at + d * 3.2f, 4f);
                if (r.Ok && MathF.Abs(r.Dy) < 0.3f && s2.RideSeg == 0)
                {
                    off = true;
                    break;
                }
                lastWhy = r.Why;
            }
            Tally($"lift L{lvl} walk off at the top", off, $"car {car}: no way off 3 m on the landing ({lastWhy})");
        }
        Console.WriteLine($"walk: {cars.Count} lift cars ridden in {clock.ElapsedMilliseconds} ms");

        foreach (var (k, v) in counts)
            Console.WriteLine($"  {k}: {v.Ok} ok, {v.Bad} not");
        foreach (var p in problems.Take(80))
            Console.WriteLine($"  problem: {p}");
        if (problems.Count > 80)
            Console.WriteLine($"  ... and {problems.Count - 80} more");

        if (walkSeconds > 0)
            WalkWithRiders(w, c, q, walkSeconds, Floor, plan, cars);
        if (args.Any(a => a.Equals("--damage", StringComparison.OrdinalIgnoreCase)))
            Damage(w, c, Floor, b, plan);
        World.OpenSky = false;
        return problems.Count;
    }

    /// <summary>
    /// Damage: a long panel and a slab shot until they break (DamagePart, as a round does), and a blast against a panel (Blast): what came off,
    /// how far from the hit, whether any of it lay outside the hit's bulkhead section.
    /// </summary>
    private static void Damage(ConstructWorld w, Construct c, Func<int, int, int, Vector3> floor, InteriorBuilder b, InteriorPlan plan)
    {
        var x0 = floor(0, 0, 0).X - 2f; // (block 0's west edge)
        int Section(Vector3 l) => plan.SectionOf((int)MathF.Floor((l.X - x0) / 4f));
        HashSet<(int, int, int, ushort)> Snapshot()
        {
            var set = new HashSet<(int, int, int, ushort)>();
            for (var i = 0; i < c.PartCount; i++)
                set.Add(((int)MathF.Round(c.Parts[i].Pos.X * 100), (int)MathF.Round(c.Parts[i].Pos.Y * 100), (int)MathF.Round(c.Parts[i].Pos.Z * 100), c.Parts[i].Def));
            return set;
        }
        void Report(string what, Vector3 at, HashSet<(int, int, int, ushort)> before, int bodies)
        {
            var after = Snapshot();
            var gone = before.Where(k => !after.Contains(k)).ToList();
            var far = gone.Count == 0 ? 0 : gone.Max(k => Vector3.Distance(new Vector3(k.Item1, k.Item2, k.Item3) * 0.01f, at));
            var sections = gone.Select(k => Section(new Vector3(k.Item1, k.Item2, k.Item3) * 0.01f)).Distinct().OrderBy(x => x).ToList();
            var kinds = gone.GroupBy(k => ConstructParts.Def(k.Item4)?.Name ?? "?").OrderByDescending(g => g.Count()).Take(6).Select(g => $"{g.Count()} {g.Key}");
            Console.WriteLine($"damage: {what} at section {Section(at)}: {gone.Count} parts off ({string.Join(", ", kinds)}), farthest {far:0.0} m, sections {string.Join(",", sections)}, {w.Bodies.Count - bodies} new bodies");
        }
        foreach (var name in new[] { "Steel Panel Wall 16 m", "Steel Panel Wall 12 m", "Steel Floor 4x4 m" })
        {
            var i = Enumerable.Range(0, c.PartCount).Where(i => c.DefOf(i).Name == name && c.Parts[i].Seg == 0 && c.Parts[i].Pos.Y > floor(0, 0, 0).Y - 0.5f).Skip(5).FirstOrDefault(-1);
            if (i < 0)
                continue;
            c.RestBounds(i, out var lo, out var hi);
            var at = (lo + hi) * 0.5f;
            var before = Snapshot();
            var bodies = w.Bodies.Count;
            var (armour, health) = (c.DefOf(i).Armour, c.DefOf(i).Health);
            var shots = 0;
            while (shots < 2000 && i < c.PartCount && c.DefOf(i).Name == name && !w.DamagePart(c, i, 60, out _))
                shots++;
            Report($"a {name} shot {shots + 1} times (60 a round; health {health}, armour {armour})", at, before, bodies);
        }
        var pick = 0;
        foreach (var (name, dmg) in new[] { ("Steel Panel Wall 16 m", 3000f), ("Steel Panel Wall 16 m", 8000f), ("Steel Panel Wall 16 m", 16000f), ("Steel Wall 4 m", 3000f), ("Steel Wall 4 m", 8000f), ("Steel Floor 4x4 m", 8000f) })
        {
            var i = Enumerable.Range(0, c.PartCount).Where(i => c.DefOf(i).Name == name && c.Parts[i].Seg == 0).Skip(3 + 2 * pick++).FirstOrDefault(-1);
            if (i < 0)
                continue;
            c.RestBounds(i, out var lo, out var hi);
            var at = (lo + hi) * 0.5f;
            var before = Snapshot();
            var bodies = w.Bodies.Count;
            w.Blast(c.ToWorld(at), 4f, dmg);
            Report($"a blast (4 m, {dmg:0}) on a {name}", at, before, bodies);
        }
    }

    /// <summary>The walk: set down on the ground, the bank charged, a player in the Bridge and one on a lift car, driven forward from the helm.</summary>
    private static void WalkWithRiders(ConstructWorld w, Construct c, IWorldQuery q, float seconds, Func<int, int, int, Vector3> floor, InteriorPlan plan, List<int> cars)
    {
        c.Anchored = c.Frozen = false;
        var g = ConstructWorld.Ground(q, c.Pos.X, c.Pos.Z);
        c.Pos = c.PrevPos = c.NetPos = c.Pos - new Vector3(0, 60 - 0.5f, 0);
        c.Charge = c.ChargeCapacity;
        var helm = Enumerable.Range(0, c.PartCount).First(i => c.DefOf(i).Name == "Bridge Helm");
        c.Wake();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double worst = 0;
        void Step()
        {
            var t0 = clock.Elapsed.TotalMilliseconds;
            w.Step(q, World.TickDt);
            worst = Math.Max(worst, clock.Elapsed.TotalMilliseconds - t0);
        }
        for (var t = 0; t < 5 * World.TickRate; t++)
            Step();
        Console.WriteLine($"walk: the helm (part {helm}) is the drive seat: {c.DriveSeat == helm}; legs {c.LegCount}");
        Console.WriteLine($"walk: settled 5 s: deck {c.Pos.Y - g.Y:0.0} m over the ground, tilt {MathF.Acos(Math.Clamp(Vector3.Transform(Vector3.UnitY, c.Rot).Y, -1, 1)) * 180 / MathF.PI:0.0} deg, charge {c.Charge:0} kJ, worst tick {worst:0} ms");
        var bridge = plan.Rooms.First(r => r.Type == "bridge");
        var riders = new List<(string What, MoveState S)>();
        var spot = floor(bridge.Deck, bridge.Rect.X0, bridge.Rect.Z1);
        riders.Add(("Bridge", new MoveState { Position = c.ToWorld(spot) + new Vector3(0, 0.05f, 0) }));
        if (cars.Count > 0)
        {
            var car = cars[^1];
            c.PartBounds(car, out var lo, out var hi);
            var f = c.ToLocal(c.SegToWorld(c.Parts[car].Seg, new Vector3((lo.X + hi.X) * 0.5f, lo.Y + 0.35f, (lo.Z + hi.Z) * 0.5f)));
            riders.Add(("lift car", new MoveState { Position = c.ToWorld(f) }));
        }
        var states = riders.Select(r => r.S).ToArray();
        void TickAll()
        {
            for (var k = 0; k < states.Length; k++)
            {
                World.Simulate(ref states[k], new InputCmd { SpeedPct = 100 });
                w.BlockPlayer(ref states[k], World.HeightOf(states[k].Stance), server: true, who: (byte)(k + 1));
            }
            Step();
            for (var k = 0; k < states.Length; k++)
                if (states[k].RideUp != Vector3.Zero && w.Get(states[k].RideBody) is { } rc && states[k].RideSeg <= rc.JointCount)
                {
                    var d = states[k].Position - states[k].RideWorld;
                    ConstructRide.Place(ref states[k], rc);
                    states[k].Position += d;
                }
        }
        for (var t = 0; t < 30; t++)
            TickAll();
        var local0 = states.Select(s => s.RideLocal).ToArray();
        var seg0 = states.Select(s => (s.RideBody, s.RideSeg)).ToArray();
        var p0 = c.Pos;
        worst = 0;
        for (var t = 0; t < seconds * World.TickRate; t++)
        {
            c.Parts[helm].Keys = (ushort)((ushort)Buttons.Forward | 0x8000);
            c.Driver = 0;
            c.DriverSeat = helm;
            c.Throttle = 1;
            c.Wake();
            TickAll();
        }
        var moved = c.Pos - p0;
        Console.WriteLine($"walk: driven {seconds:0} s: moved {new Vector2(moved.X, moved.Z).Length():0.00} m ({new Vector2(moved.X, moved.Z).Length() / seconds:0.00} m/s), deck {c.Pos.Y - g.Y:0.0} m over the ground, charge {c.Charge:0} kJ, worst tick {worst:0} ms");
        for (var k = 0; k < states.Length; k++)
            Console.WriteLine($"  rider in the {riders[k].What}: on body {seg0[k].RideBody} seg {seg0[k].RideSeg} -> body {states[k].RideBody} seg {states[k].RideSeg}, drift {Vector3.Distance(states[k].RideLocal, local0[k]):0.00} m in its frame");
    }
}
