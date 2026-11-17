using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--skin-check [BLUEPRINT,...] [--step M]</c>: does a ship's drawn skin collide where it is drawn? Each ship is built on the
/// Proving Ground and held still 30 m up. Rays come down on it from above and in from both sides on a grid; each is met against
/// what is drawn (every solid box and every "hull" shape, drawn only or not) and against what collides
/// (<see cref="ConstructWorld.Raycast"/>), and the gap between the two is counted. Then a player is set down over some of the
/// downward hits and stood there two seconds (<see cref="World.Simulate"/>, <see cref="ConstructWorld.BlockPlayer"/>), and a
/// Deck Plate is dropped on them through the physics, and how far each sinks below the drawn skin is reported. A dev tool:
/// it allocates freely.
/// </summary>
public static class SkinCheck
{
    public static int Cli(string[] args)
    {
        string Arg(string name, string def) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() ?? def;
        const string All = "Rust Bucket,Zero Day Frigate,Magpie Mining Ship,Dray Container Hauler,Shuttle";
        var names = Arg("--skin-check", All);
        if (names.StartsWith("--"))
            names = All;
        var step = float.Parse(Arg("--step", "0.7"), System.Globalization.CultureInfo.InvariantCulture);
        Speed = float.Parse(Arg("--speed", "0"), System.Globalization.CultureInfo.InvariantCulture);
        if (Maps.Find("Proving Ground") is not { } yard)
        {
            Console.WriteLine("no Proving Ground map");
            return 1;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        var bad = 0;
        foreach (var name in names.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            bad += One(name, step);
        Console.WriteLine($"skin check: {bad} ship(s) with gaps");
        return bad;
    }

    private static readonly Dictionary<ConstructPartDef, ConvexHull[]> Drawn = new();
    private static float Speed;
    private static readonly bool Trace = Environment.GetEnvironmentVariable("SKIN_TRACE") == "1"; // --speed: the ship flies forward at this while players stand on it (contraption space: ConstructRideMove.cs)

    /// <summary>Every "hull" shape of a part (drawn only or not) as a convex piece in the part's frame.</summary>
    private static ConvexHull[] DrawnHulls(ConstructPartDef d)
    {
        if (Drawn.TryGetValue(d, out var have))
            return have;
        var list = new List<ConvexHull>();
        foreach (var box in d.Boxes)
        {
            if (box.Shape == 0 || PartShapes.KindOf(box.Shape) != "hull")
                continue;
            var mesh = PartShapes.Mesh(box.Shape);
            var pts = new Vector3[mesh.Length / 6];
            for (var k = 0; k < pts.Length; k++)
                pts[k] = box.ShapePoint(new Vector3(mesh[k * 6], mesh[k * 6 + 1], mesh[k * 6 + 2]));
            if (ConvexHull.Of(pts) is { } h)
                list.Add(h);
        }
        return Drawn[d] = list.ToArray();
    }

    /// <summary>The first drawn surface along a world ray: solid boxes and every hull shape.</summary>
    private static int _drawnPart;

    private static float DrawnHit(Construct c, Vector3 origin, Vector3 dir, float max)
    {
        var best = max;
        _drawnPart = -1;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            if (p.Broken)
                continue;
            c.WorldFrame(p.Seg, out var q, out var t);
            var iq = Quaternion.Conjugate(q);
            var lo = Vector3.Transform(origin - t, iq);
            var ld = Vector3.Transform(dir, iq);
            for (var b = 0; b < c.SolidCount(i); b++)
            {
                if (p.D.Boxes[b].Hidden)
                    continue; // (what is drawn: a hidden box stands for something)
                c.SolidBox(i, b, out var centre, out var half);
                if (Geometry.RayAabb(centre - half, centre + half, lo, ld, best) is { } d && d < best)
                {
                    best = d;
                    _drawnPart = i;
                }
            }
            var back = Quaternion.Conjugate(Rot24.Quat(p.Rot));
            var pl = Vector3.Transform(lo - p.Pos, back);
            var pd = Vector3.Transform(ld, back);
            foreach (var h in DrawnHulls(p.D))
                if (h.Ray(pl, pd, best, out var d, out _) && d < best)
                {
                    best = d;
                    _drawnPart = i;
                }
        }
        return best;
    }

    private static int One(string name, float step)
    {
        var bp = ConstructBuild.FindBlueprint(name);
        var w = new ConstructWorld();
        var c = bp == null ? null : ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, 0, 0), 0, out _);
        if (c == null)
        {
            Console.WriteLine($"{name}: doesn't build");
            return 1;
        }
        c.Rebuild();
        var ship = w.Bodies.ToArray(); // (a docked craft is a body of its own: the Zero Day's Shuttle)
        foreach (var o in ship)
            o.Rebuild();
        Hold(w, ship, new Vector3(0, 30f, 0));
        c.ColliderChanged();
        _ = c.Collider; // (the first build makes each part's hulls)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        c.ColliderChanged();
        var leaves = c.Collider.LeafCount;
        var hulls = c.Collider.Hulls.Count(h => h != null);
        sw.Stop();
        Console.WriteLine($"{name}: {c.PartCount} parts, {w.Bodies.Count} bodies, collider {leaves} leaves ({hulls} convex) built in {sw.Elapsed.TotalMilliseconds:0.0} ms");

        // Rays: down from above, and in from port and starboard.
        var mn = c.LocalMin;
        var mx = c.LocalMax;
        var size = mx - mn;
        var rays = 0;
        var gaps = 0;
        var missed = 0;
        var worst = 0f;
        var worstAt = Vector3.Zero;
        var downHits = new List<Vector3>();
        void Ray(Vector3 localFrom, Vector3 localDir, float len, bool down)
        {
            var from = c.ToWorld(localFrom);
            var dir = Vector3.Transform(localDir, c.Rot);
            var drawn = DrawnHit(c, from, dir, len);
            if (drawn >= len)
                return;
            rays++;
            var hit = w.Raycast(from, dir, len, out var h, only: c) ? h.Distance : len;
            var gap = hit - drawn;
            if (hit >= len)
                missed++;
            if (gap > 0.05f)
            {
                gaps++;
                if (gap > worst)
                {
                    worst = gap;
                    worstAt = c.ToLocal(from + dir * drawn);
                }
            }
            var spot = c.ToLocal(from + dir * drawn);
            if (down && DrawnHit(c, c.ToWorld(spot + new Vector3(0, 0.05f, 0)), Vector3.Transform(Vector3.UnitY, c.Rot), World.PlayerHeight + 0.3f) >= World.PlayerHeight + 0.3f
                && !w.Raycast(c.ToWorld(spot + new Vector3(0, 0.05f, 0)), Vector3.Transform(Vector3.UnitY, c.Rot), World.PlayerHeight + 0.3f, out _, only: c))
                downHits.Add(spot); // (room to stand: not under a crane or a mast's yard)
        }
        for (var x = mn.X + step / 2; x < mx.X; x += step)
        for (var z = mn.Z + step / 2; z < mx.Z; z += step)
            Ray(new Vector3(x, mx.Y + 2f, z), -Vector3.UnitY, size.Y + 4f, true);
        for (var y = mn.Y + step / 2; y < mx.Y; y += step)
        for (var z = mn.Z + step / 2; z < mx.Z; z += step)
        {
            Ray(new Vector3(mx.X + 2f, y, z), -Vector3.UnitX, size.X + 4f, false);
            Ray(new Vector3(mn.X - 2f, y, z), Vector3.UnitX, size.X + 4f, false);
        }
        Console.WriteLine($"  rays: {rays} meet the drawn skin, {gaps} reach a collider more than 5 cm past it ({missed} none at all), worst {worst:0.00} m at {worstAt}");

        // What rides in it as a body of its own (the Zero Day's Shuttle in its cradle): the ship held, the rest left to the
        // physics for three seconds. How far each moves, and what a physics step costs with them touching.
        if (ship.Length > 1)
        {
            var starts = ship.Select(b => (b.Pos, b.Rot)).ToArray();
            foreach (var b in ship)
            {
                b.Anchored = b == c;
                if (b != c)
                    b.Wake();
            }
            var steps = new List<double>();
            for (var t = 0; t < 180; t++)
            {
                c.Vel = c.AngVel = Vector3.Zero;
                var st = System.Diagnostics.Stopwatch.GetTimestamp();
                ConstructPhysics.Step(w, World.Query, World.TickDt);
                if (t >= 10)
                    steps.Add(System.Diagnostics.Stopwatch.GetElapsedTime(st).TotalMilliseconds); // (after the first steps' JIT)
            }
            for (var k = 1; k < ship.Length; k++)
            {
                var b = ship[k];
                var turned = 2 * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(b.Rot, starts[k].Rot)), 0f, 1f)) * 180 / MathF.PI;
                Console.WriteLine($"  {b.DefOf(0).Name} (body {b.Id}, {b.PartCount} parts): moved {Vector3.Distance(b.Pos, starts[k].Pos) * 100:0.0} cm, turned {turned:0.00} deg, {b.Vel.Length():0.00} m/s after 3 s{(b.Asleep ? ", asleep" : "")}{(b.Anchored ? ", anchored" : "")}");
            }
            steps.Sort();
            Console.WriteLine($"  physics step with them: median {steps[steps.Count / 2]:0.00} ms, worst {steps[^1]:0.00} ms");
            // ...and let go of (out of its cradle's hold), pushed down and sideways into the ship at 2 m/s for three seconds:
            // a craft drifting into the hull stops on its skin.
            for (var k = 1; k < ship.Length; k++)
            {
                var b = ship[k];
                b.Pos = b.PrevPos = starts[k].Pos;
                b.Rot = b.PrevRot = starts[k].Rot;
                b.CarriedBy = 0;
                b.Anchored = false;
                b.Wake();
                foreach (var push in new[] { -Vector3.UnitY, Vector3.UnitX, -Vector3.UnitX })
                {
                    b.Pos = b.PrevPos = starts[k].Pos;
                    b.Rot = b.PrevRot = starts[k].Rot;
                    b.Vel = Vector3.Transform(push, c.Rot) * 2f;
                    b.AngVel = Vector3.Zero;
                    b.Wake();
                    var deepest = 0f;
                    for (var t = 0; t < 180; t++)
                    {
                        c.Vel = c.AngVel = Vector3.Zero;
                        c.Anchored = true;
                        ConstructPhysics.Step(w, World.Query, World.TickDt);
                        deepest = MathF.Max(deepest, Vector3.Dot(b.Pos - starts[k].Pos, Vector3.Transform(push, c.Rot)));
                    }
                    Console.WriteLine($"  {b.DefOf(0).Name} let go, pushed {push} at 2 m/s: went {deepest:0.00} m that way at most, {Vector3.Distance(b.Pos, starts[k].Pos):0.00} m from its cradle after 3 s, {b.Vel.Length():0.00} m/s");
                }
            }
            for (var k = 0; k < ship.Length; k++)
            {
                ship[k].Pos = ship[k].PrevPos = starts[k].Pos;
                ship[k].Rot = ship[k].PrevRot = starts[k].Rot;
            }
            Hold(w, ship, Vector3.Zero);
        }

        // Players stood on the top, and plates dropped on it.
        var picks = new List<Vector3>();
        for (var k = 0; k < 24 && downHits.Count > 0; k++)
            picks.Add(downHits[(int)((long)k * downHits.Count / 24 + downHits.Count / 48) % downHits.Count]);
        int sunk = 0, fell = 0, slid = 0, plateSunk = 0, plateFell = 0, plateSlid = 0;
        var worstSink = 0f;
        var worstPlate = 0f;
        var plate = ConstructParts.Need("Deck Plate");
        var plateHalf = (plate.BoundsMax.Y - plate.BoundsMin.Y) * 0.5f;
        foreach (var top in picks)
        {
            var s = new MoveState { Position = c.ToWorld(top + new Vector3(0, 0.3f, 0)) };
            var home = c.Pos;
            var fly = Vector3.Transform(-Vector3.UnitZ, c.Rot) * Speed;
            s.Velocity = fly;
            for (var t = 0; t < 120; t++)
            {
                Hold(w, ship, fly * World.TickDt);
                if (Speed > 0)
                    foreach (var b in ship)
                    {
                        b.Vel = fly;
                        b.Anchored = false;
                    }
                World.Simulate(ref s, new InputCmd { SpeedPct = 100, RideBody = s.RideBody });
                if (Trace && t < 3)
                    Console.WriteLine($"      t{t} moved: at {c.ToLocal(s.Position)}");
                w.BlockPlayer(ref s, World.HeightOf(s.Stance), server: true, who: 1);
                if (Trace)
                    Console.WriteLine($"      t{t}: at {c.ToLocal(s.Position)} vel {s.Velocity} grounded {s.Grounded} ride {s.RideBody}/{s.RideUp}");
            }
            var endAt = c.ToLocal(s.Position);
            if (top.Y - endAt.Y > 0.3f && Vector2.Distance(new Vector2(top.X, top.Z), new Vector2(endAt.X, endAt.Z)) < 0.3f)
            {
                DrawnHit(c, c.ToWorld(top + new Vector3(0, 0.05f, 0)), Vector3.Transform(-Vector3.UnitY, c.Rot), 1f);
                Console.WriteLine($"    player went down through {(_drawnPart >= 0 ? c.DefOf(_drawnPart).Name : "?")} at {top}: ends {top.Y - endAt.Y:0.00} m lower");
                fell++;
            }
            var sink = SinkAt(c, c.ToLocal(s.Position), 0f, size.Y);
            Hold(w, ship, home - c.Pos);
            if (sink == null)
                slid++;
            else if (sink > 1f)
                fell++;
            else if (sink > 0.05f)
                sunk++;
            if (sink is > 0.05f)
                Console.WriteLine($"    player from {top} ends at {c.ToLocal(s.Position)}, {sink * 100:0} cm under the skin of {c.DefOf(_drawnPart).Name} (riding {s.RideBody}, grounded {s.Grounded})");
            if (sink is { } sk && sk <= 1f)
                worstSink = MathF.Max(worstSink, sk);

            if (ConstructBuild.PlaceNew(w, plate, c.ToWorld(top + new Vector3(0, 0.4f, 0)), 0) is { } loose)
            {
                loose.Rebuild();
                for (var t = 0; t < 120; t++)
                {
                    Hold(w, ship, Vector3.Zero);
                    ConstructPhysics.Step(w, World.Query, World.TickDt);
                }
                var under = SinkAt(c, c.ToLocal(loose.WorldCom), plateHalf, size.Y);
                if (under == null)
                    plateSlid++;
                else if (under > 1f)
                    plateFell++;
                else if (under > 0.05f)
                    plateSunk++;
                if (under is > 0.05f)
                    Console.WriteLine($"    plate from {top} ends at {c.ToLocal(loose.WorldCom)}, {under * 100:0} cm under the skin of {c.DefOf(_drawnPart).Name}");
                if (under is { } u && u <= 1f)
                    worstPlate = MathF.Max(worstPlate, u);
                w.Remove(loose);
            }
        }
        Console.WriteLine($"  players on {picks.Count} spots: {sunk} sank 5 cm to 1 m into the skin, {fell} fell through, {slid} slid off (worst sink {worstSink * 100:0} cm)");
        Console.WriteLine($"  plates dropped on them: {plateSunk} sank 5 cm to 1 m, {plateFell} fell through, {plateSlid} slid off (worst sink {worstPlate * 100:0} cm)");
        return gaps > rays / 20 || fell + plateFell > 0 ? 1 : 0;
    }

    /// <summary>How far below the drawn skin under it a thing ends (its centre at <paramref name="local"/>, its bottom <paramref name="below"/>
    /// under that, in the ship's frame): the first drawn surface straight down from 3 m over it. Null: no skin under it (slid off).</summary>
    private static float? SinkAt(Construct c, Vector3 local, float below, float height)
    {
        var from = local + new Vector3(0, below + 0.5f, 0); // (from a little over it: an overhang higher up is not what it sank into)
        var d = DrawnHit(c, c.ToWorld(from), Vector3.Transform(-Vector3.UnitY, c.Rot), height + 6f);
        if (d >= height + 6f)
            return null;
        // (the first surface over its middle: if it is above the thing by more than its own height, it went in under it)
        return from.Y - d - (local.Y - below);
    }

    /// <summary>The ship's bodies held still, moved by <paramref name="off"/>.</summary>
    private static void Hold(ConstructWorld w, Construct[] ship, Vector3 off)
    {
        foreach (var b in ship)
        {
            b.PrevPos = b.Pos;
            b.PrevRot = b.Rot;
            b.Pos += off;
            b.Vel = Vector3.Zero;
            b.AngVel = Vector3.Zero;
            b.Anchored = true;
        }
        ConstructRide.BeforePhysics(w);
    }
}
