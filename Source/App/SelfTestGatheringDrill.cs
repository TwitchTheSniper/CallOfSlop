using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Outcrops: two builds of a field draw the same rocks, within the budget; they stand over deposits of their own colour,
    /// not under solid boxes, and a vein reads as one long streak. A map file can't place them (the "Foliage" section), rocks
    /// don't sway, and a table can't give a surface kind an outcrop.
    /// </summary>
    private static void GatherOutcrops(Map map, ResourceField f, ResourceField f2, Action<bool, string> want)
    {
        f2.DrawAround(Vector3.Zero);
        var rocks = f.Drawn.Where(d => d.Type == FoliageType.Outcrop).ToArray();
        want(rocks.Length > 0 && rocks.SequenceEqual(f2.Drawn.Where(d => d.Type == FoliageType.Outcrop)), $"two fields drew different outcrops ({rocks.Length} rocks)");
        want(rocks.Length <= ResourceField.MaxDrawnOutcrops, $"{rocks.Length} rocks drawn");
        int over = 0, matched = 0, solid = 0;
        var iron = new List<Vector2>();
        var kinds = new HashSet<string>();
        foreach (var r in rocks)
        {
            var p = new Vector2(r.Position.X, r.Position.Z);
            if (f.KindAt(p.X, p.Y, false) is not { } k)
                continue;
            over++;
            kinds.Add(k.Name);
            if (MathF.Abs(r.Color.X / r.Color.Z - k.OutcropTint.X / k.OutcropTint.Z) < 0.01f)
            {
                matched++;
                if (k.Name == "iron vein")
                    iron.Add(p);
            }
        }
        foreach (var b in map.Solid)
            if (b.Max.Y - b.Min.Y >= 0.4f && b.Max.X - b.Min.X <= 80 && b.Max.Z - b.Min.Z <= 80)
                solid += rocks.Count(r => r.Position.X > b.Min.X && r.Position.X < b.Max.X && r.Position.Z > b.Min.Z && r.Position.Z < b.Max.Z);
        // The biggest group of iron rocks, each within 14 m of the next: long and thin?
        var group = new int[iron.Count];
        for (var i = 0; i < group.Length; i++)
            group[i] = i;
        int Find(int i) => group[i] == i ? i : group[i] = Find(group[i]);
        for (var i = 0; i < iron.Count; i++)
            for (var j = 0; j < i; j++)
                if (Vector2.DistanceSquared(iron[i], iron[j]) < 14 * 14)
                    group[Find(i)] = Find(j);
        var largest = iron.Select((_, i) => i).GroupBy(Find).OrderByDescending(g => g.Count()).FirstOrDefault()?.Select(i => iron[i]).ToArray() ?? [];
        var (length, across) = StreakShape(largest);
        Line($"gathering: {rocks.Length} outcrop rocks of {kinds.Count} kinds, {over} over a deposit, {matched} of its colour; biggest iron streak {largest.Length} rocks, {length:0} by {across:0} m");
        want(kinds.Count >= 2, $"outcrops show only {kinds.Count} kinds on {map.Name}");
        want(over >= rocks.Length * 0.9f, $"only {over} of {rocks.Length} rocks stand over a deposit");
        want(matched >= over * 0.9f, $"only {matched} of {over} rocks have their deposit's colour");
        want(solid == 0, $"{solid} rocks stand in solid boxes");
        want(largest.Length >= 40 && length >= 60 && length >= across * 2.5f, $"a vein isn't one long streak: {largest.Length} rocks, {length:0} by {across:0} m");
        var mesh = FoliageMeshes.Mesh(FoliageType.Outcrop, 0);
        var sways = false;
        for (var i = mesh.FirstIndex; i < mesh.FirstIndex + mesh.IndexCount; i++)
            sways |= FoliageMeshes.Vertices[FoliageMeshes.Indices[i] * FoliageMeshes.FloatsPerVertex + 6] != 0;
        want(mesh.IndexCount > 0 && FoliageMeshes.Triangles(FoliageType.Outcrop, 1) > 0 && !sways, "the outcrop mesh is empty or sways");
        want(ResourceTable.Parse("{\"format\":\"" + ResourceTable.Format + "\",\"kinds\":[{\"name\":\"x\",\"material\":\"wood\",\"layer\":\"surface\",\"cell\":99,\"chance\":1,\"radius\":[9,9],\"aspect\":[1,1],\"units\":1,\"outcrop\":{\"tint\":[0,0,0],\"spacing\":5,\"size\":[1,1]}}]}").Problem.Length > 0, "a surface kind's outcrop was accepted");
    }

    /// <summary>The length and width of a set of points along and across its main direction.</summary>
    private static (float Length, float Across) StreakShape(Vector2[] points)
    {
        if (points.Length == 0)
            return (0, 0);
        var mid = points.Aggregate(Vector2.Zero, (a, b) => a + b) / points.Length;
        float xx = 0, xz = 0, zz = 0;
        foreach (var p in points)
        {
            var d = p - mid;
            (xx, xz, zz) = (xx + d.X * d.X, xz + d.X * d.Y, zz + d.Y * d.Y);
        }
        var angle = 0.5f * MathF.Atan2(2 * xz, xx - zz);
        var axis = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var side = new Vector2(-axis.Y, axis.X);
        return (points.Max(p => Vector2.Dot(p, axis)) - points.Min(p => Vector2.Dot(p, axis)), points.Max(p => Vector2.Dot(p, side)) - points.Min(p => Vector2.Dot(p, side)));
    }

    /// <summary>A drill's bit a little over the ground (or over the surface as built).</summary>
    private static Construct DrillAt(ConstructWorld w, ResourceField f, float x, float z) =>
        GatherBody(w, "Drill Module", new Vector3(x, f.Ground(x, z) + 0.4f, z));

    /// <summary>
    /// A drill's hole (ConstructDrillShaft.cs), through the real gather step: slow scoops; half an hour of drilling digs a
    /// bounded hole and the drill keeps working over it, anchored in place; moved up it stops, brought back it resumes
    /// without another op; another drill takes the hole over; no hook, no hole; a full bin digs nothing; many drills stay at
    /// one op a step; a free drill settles into its hole; drills leave blasts their half of the log. A scoop is cheap.
    /// </summary>
    private static void GatherShaft(Map map, ResourceField f, Vector2 vein, Action<bool, string> want)
    {
        if (map.Terrain is not { } t)
        {
            want(false, $"{map.Name} has no terrain to drill");
            return;
        }
        map.RestoreProps();
        var w = new ConstructWorld { Resources = f, CreativeRecycling = true };
        var ops = 0;
        var costs = new List<double>();
        var hook = (DigOp op) =>
        {
            var t0 = Stopwatch.GetTimestamp();
            var n = t.Edits.Apply(op);
            costs.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
            ops += n > 0 ? 1 : 0;
            return n;
        };
        w.DrillDig = hook;
        var taken = 0;
        void Gather(float seconds, CStore? empty = null)
        {
            for (var k = 0; k < seconds * World.TickRate; k++)
            {
                ConstructGatherer.Step(w);
                if (empty != null)
                    (taken, empty.Amount) = (taken + empty.Amount, 0);
            }
        }
        var c = DrillAt(w, f, vein.X, vein.Y);
        var st = w.StoreOf(c, 0)!;
        var pos = c.Pos;
        ConstructGatherer.WorkBox(c, 0, out var bit, out var bottom, out _);
        var surface = t.Sample(bit.X, bit.Z);
        var scoop = DrillShaft.UnitsPerScoop / ConstructParts.Need("Drill Module").GatherRate;
        Gather(scoop - 1, st);
        want(ops == 0, $"a drill dug {ops} ops before its first scoop");
        Gather(2, st);
        want(ops == 1 && st.Busy, $"after one scoop's time: {ops} ops, busy {st.Busy}");
        Gather(scoop, st);
        want(ops == 2, $"after two scoops' time: {ops} ops");
        const float long_ = 30 * 60;
        Gather(long_, st);
        var floor = t.Sample(bit.X, bit.Z);
        Line($"gathering: drilled {long_ / 60:0} min: {ops} ops, floor {surface - floor:0.00} m down, {taken} units, bit {bottom - floor:0.00} m over the floor");
        want(ops >= 4 && ops <= DrillShaft.Scoops, $"a hole took {ops} ops");
        want(surface - floor >= DrillShaft.Depth && surface - floor < DrillShaft.Depth + 0.5f, $"the hole is {surface - floor:0.00} m deep");
        want(st.Busy, "the drill stopped once the hole was deeper than its bit");
        want(taken >= ConstructParts.Need("Drill Module").GatherRate * long_ * 0.95f, $"only {taken} units in {long_} s");
        want(c.Pos == pos, "the anchored drill moved");
        // Moved away, it stops; brought back, it resumes, and a hole already dug takes no more ops.
        var done = ops;
        c.Pos = pos + new Vector3(0, 3.5f, 0);
        Gather(1);
        var kept = st.Amount;
        Gather(2);
        want(!st.Busy && st.Amount == kept, "a drill lifted out of its hole kept working");
        c.Pos = pos;
        Gather(1, st);
        want(st.Busy, "a drill brought back to its hole didn't resume");
        w.Remove(c);
        c = DrillAt(w, f, vein.X, vein.Y);
        c.Pos = pos;
        st = w.StoreOf(c, 0)!;
        Gather(scoop + 1);
        want(st.Busy && st.Amount > 0 && ops == done, $"a second drill at the hole: busy {st.Busy}, {st.Amount} units, {ops - done} more ops");
        w.Remove(c);
        // Fresh spots along the deposit.
        var spots = new List<Vector2>();
        for (var dz = -60; dz <= 60; dz += 6)
            for (var dx = -60; dx <= 60; dx += 6)
                if (Vector2.Distance(new Vector2(dx, dz), Vector2.Zero) > 8 && f.KindAt(vein.X + dx, vein.Y + dz, false)?.Name == "iron vein")
                    spots.Add(new Vector2(vein.X + dx, vein.Y + dz));
        if (spots.Count < 8)
        {
            want(false, $"only {spots.Count} spots on the vein");
            return;
        }
        // No hook: it taps, the ground stays.
        w.DrillDig = null;
        var g0 = f.Ground(spots[0].X, spots[0].Y);
        c = DrillAt(w, f, spots[0].X, spots[0].Y);
        st = w.StoreOf(c, 0)!;
        Gather(scoop + 1);
        want(st.Amount > 0 && f.Ground(spots[0].X, spots[0].Y) == g0, $"with digging off: {st.Amount} units, ground moved {g0 - f.Ground(spots[0].X, spots[0].Y):0.00} m");
        w.Remove(c);
        // A full bin digs nothing.
        w.DrillDig = hook;
        c = DrillAt(w, f, spots[1].X, spots[1].Y);
        st = w.StoreOf(c, 0)!;
        st.Amount = ConstructParts.Need("Drill Module").BulkCapacity;
        st.JobAmount = DrillShaft.UnitsPerScoop;
        done = ops;
        Gather(scoop + 1);
        want(ops == done, "a full drill dug");
        w.Remove(c);
        // Many drills: one op a gather step.
        var many = spots.GetRange(2, 5).Select(s => DrillAt(w, f, s.X, s.Y)).ToArray();
        var most = 0;
        for (var round = 0; round < 8; round++)
        {
            foreach (var d in many)
            {
                var s = w.StoreOf(d, 0)!;
                s.Amount = 0;
                s.JobAmount = DrillShaft.UnitsPerScoop;
            }
            done = ops;
            Gather((float)ConstructGatherer.Every / World.TickRate);
            most = Math.Max(most, ops - done);
        }
        want(most == DrillShaft.MaxScoopsPerStep, $"many drills took {most} ops in a gather step");
        foreach (var d in many)
            w.Remove(d);
        // A free drill settles into its own hole and stays in the world.
        done = ops;
        var at = spots[^1];
        c = DrillAt(w, f, at.X, at.Y);
        c.Anchored = false;
        c.Dirty = true;
        st = w.StoreOf(c, 0)!;
        for (var k = 0; k < 40 * World.TickRate; k++)
        {
            st.Amount = 0;
            st.JobAmount = DrillShaft.UnitsPerScoop;
            c.Wake();
            w.Step(World.Query, World.TickDt);
        }
        want(w.Bodies.Contains(c) && ops - done <= DrillShaft.Scoops * 3 && c.WorldCom.Y > t.BaseGroundTop(at.X, at.Y) - DrillShaft.Depth - 3, $"a free drill: {ops - done} ops, at {c.WorldCom.Y:0.00} over ground {t.BaseGroundTop(at.X, at.Y):0.00}");
        w.Remove(c);
        // Drills leave the log's other half to blasts.
        c = DrillAt(w, f, spots[^2].X, spots[^2].Y);
        st = w.StoreOf(c, 0)!;
        t.Edits.Log.AddRange(Enumerable.Repeat(default(DigOp), DrillShaft.MaxLog - t.Edits.Log.Count));
        done = ops;
        st.JobAmount = DrillShaft.UnitsPerScoop;
        Gather(1);
        want(ops == done && st.Busy, $"a drill dug {ops - done} ops with the log at its limit (busy {st.Busy})");
        w.Remove(c);
        costs.Sort();
        var mid = costs.Count > 0 ? costs[costs.Count / 2] : 0;
        Line($"gathering: a scoop takes {mid:0.000} ms (worst {costs.LastOrDefault():0.000} ms of {costs.Count})");
        want(mid < 1, $"a scoop takes {mid:0.000} ms");
    }

    /// <summary>A real host, client and late joiner: a drill's hole reaches both, a body beside it falls in, and with Destruction off the drill taps and digs nothing.</summary>
    private static void GatherShaftNet(Game server, Game a, Game late, ResourceField f, ushort port, ref double now, Stopwatch clock)
    {
        if (FirstUnderground(f, 40, true, "iron vein") is not { } vein)
        {
            Fail("gathering network: no vein in the yard");
            return;
        }
        var w = server.Constructs;
        var drill = DrillAt(w, f, vein.X, vein.Y);
        var st = w.StoreOf(drill, 0)!;
        ConstructGatherer.WorkBox(drill, 0, out var bit, out _, out _);
        var block = ConstructBuild.PlaceNew(w, ConstructParts.Need("Frame Block"), new Vector3(bit.X, f.Ground(bit.X, bit.Z + 1f) + 0.6f, bit.Z + 1f), 0)!;
        Pump(server, [a, late], 1.0, ref now, clock);
        var y0 = block.WorldCom.Y;
        var start = server.DigOps;
        for (var t = 0f; t < 8 && server.DigOps - start < 4; t += 0.1f)
        {
            st.Amount = 0;
            st.JobAmount = DrillShaft.UnitsPerScoop;
            Pump(server, [a, late], 0.1, ref now, clock);
        }
        Pump(server, [a, late], 1.0, ref now, clock);
        var ops = server.DigOps;
        var sank = y0 - block.WorldCom.Y;
        Line($"gathering network: {ops - start} scoops, client read {a.DigOpsRead}, joiner {late.DigOpsRead}, a block beside the hole sank {sank:0.00} m, floor {f.Ground(bit.X, bit.Z):0.00}");
        if (ops - start < 4)
            Fail($"gathering network: the drill took only {ops - start} scoops");
        if (a.DigOpsRead != ops || late.DigOpsRead != ops)
            Fail($"gathering network: the client read {a.DigOpsRead} and the joiner {late.DigOpsRead} of {ops} ops");
        if (sank < 0.3f)
            Fail($"gathering network: a block beside the hole only sank {sank:0.00} m");
        using var later = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Later", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a, late, later], 1.5, ref now, clock);
        if (later.DigOpsRead != ops)
            Fail($"gathering network: a later joiner read {later.DigOpsRead} of {ops} ops");
        w.Remove(drill);
        // Destruction off: a fresh spot taps, and nothing is dug.
        server.DestructionEnabled = false;
        var fresh = DrillAt(w, f, vein.X + 7, vein.Y);
        var fs = w.StoreOf(fresh, 0)!;
        for (var t = 0f; t < 1; t += 0.1f)
        {
            fs.JobAmount = DrillShaft.UnitsPerScoop;
            Pump(server, [a, late, later], 0.1, ref now, clock);
        }
        if (fs.Amount == 0 || !server.TerrainDigLive && server.DigOps != ops)
            Fail($"gathering network: Destruction off: {fs.Amount} units, {server.DigOps - ops} ops");
        server.DestructionEnabled = true;
        w.Remove(fresh);
    }

    /// <summary>
    /// A drill's whole hole on open ground: no overhang, a player falls in and climbs out every way with no head in the
    /// ground; stacked on a crater, and a crater in the hole, no head goes in the ground either (a pit there may be too deep
    /// to climb: counted, as with stacked craters).
    /// </summary>
    private static void DigShaftEscape()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Terrain is not { } t)
            return;
        yard.RestoreProps();
        World.SetMap(yard);
        var boxes = yard.AsBuiltSolid();
        var spots = new[] { new Vector2(8, -6), new Vector2(14, 10), new Vector2(-20, 0), new Vector2(2, 18) };
        var frag = Throwables.Get(ThrowableKind.Frag).Radius;
        var r = Math.Clamp(frag * Game.CraterPerBlast, Game.CraterMin, Game.CraterMax);
        int runs = 0, trapped = 0, wedged = 0, stackRuns = 0, pits = 0, overhangs = 0, scoopsMost = 0;
        var first = "";
        foreach (var s in spots)
        {
            var scoops = Hole(s.X, s.Y);
            scoopsMost = Math.Max(scoopsMost, scoops);
            var deep = t.BaseGroundTop(s.X, s.Y) - t.Sample(s.X, s.Y);
            if (deep < DrillShaft.Depth || deep > DrillShaft.Depth + 0.5f || scoops > DrillShaft.Scoops)
                Fail($"a drill's hole at ({s.X}, {s.Y}): {scoops} scoops, {deep:0.00} m deep");
            for (var z = s.Y - 2.5f; z <= s.Y + 2.5f; z += 0.25f)
            for (var x = s.X - 2.5f; x <= s.X + 2.5f; x += 0.25f)
                if (t.Edits.TryColumn(x, z, out var sp) && sp.Length > 2)
                    overhangs++;
            Climb(s, true, ref runs, ref trapped);
            // A frag in the hole, off the middle, and the drill goes on.
            var c = new Vector3(s.X + 0.8f, 0, s.Y);
            c.Y = t.Sample(c.X, c.Z) + 0.25f;
            t.Edits.Apply(new DigOp(DigKind.Crater, c, r).Quantised);
            Hole(s.X, s.Y);
            Climb(s, false, ref stackRuns, ref pits);
            // A hole in a crater, off the middle.
            var o = new Vector3(s.X + 8, 0, s.Y + 8);
            o.Y = t.Sample(o.X, o.Z) + 0.25f;
            t.Edits.Apply(new DigOp(DigKind.Crater, o, r).Quantised);
            Hole(o.X + 0.6f, o.Z);
            Climb(new Vector2(o.X + 0.6f, o.Z), false, ref stackRuns, ref pits);
        }
        DigLine($"{World.Mover.GetType().Name}: drill holes at {spots.Length} spots: at most {scoopsMost} scoops, {overhangs} overhang points, {runs} ways out, {trapped} trapped; stacked: {stackRuns} ways out, {pits} pits, {wedged} ticks with a head in the ground");
        if (overhangs > 0 || trapped > 0 || wedged > 0)
            Fail($"a drill's hole left {overhangs} overhang points, trapped a player {trapped} of {runs} ways or wedged a head ({wedged} ticks); first: {first}");
        yard.RestoreProps();

        int Hole(float x, float z)
        {
            var n = 0;
            while (n < DrillShaft.Scoops + 2 && DrillShaft.Next(t, x, z, out var op) && t.Edits.Apply(op) > 0)
                n++;
            return n;
        }

        void Climb(Vector2 from, bool note, ref int count, ref int notOut)
        {
            for (var a = 0; a < 8; a++)
            {
                var yaw = a * MathF.PI / 4;
                var centre = new Vector3(from.X, t.Sample(from.X, from.Y), from.Y);
                if (WalledOff(boxes, centre, yaw, DrillShaft.Radius + 1))
                    continue;
                count++;
                var m = new MoveState { Position = new Vector3(from.X + 0.1f, t.Sample(from.X + 0.1f, from.Y + 0.1f), from.Y + 0.1f), Grounded = true };
                var outside = false;
                for (var k = 0; k < 4 * World.TickRate && !outside; k++)
                {
                    World.Simulate(ref m, new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint | (k % 20 < 2 ? Buttons.Jump : Buttons.None), Yaw = yaw, SpeedPct = 100 });
                    var ceiling = t.CeilingAbove(m.Position.X, m.Position.Z, m.Position.Y + 0.05f);
                    if (m.Position.Y + World.HeightOf(m.Stance) > ceiling + 0.05f)
                    {
                        wedged++;
                        if (first.Length == 0)
                            first = $"head in the ground at ({m.Position.X:0.00}, {m.Position.Y:0.00}, {m.Position.Z:0.00})";
                    }
                    outside = Vector2.Distance(new Vector2(m.Position.X, m.Position.Z), from) > DrillShaft.Radius + 0.8f;
                }
                if (outside)
                    continue;
                notOut++;
                if (note && first.Length == 0)
                    first = $"not out of the hole at ({from.X}, {from.Y}) heading {a * 45} deg: ended at ({m.Position.X:0.00}, {m.Position.Y:0.00}, {m.Position.Z:0.00})";
            }
        }
    }
}
