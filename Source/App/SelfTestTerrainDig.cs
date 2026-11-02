using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The "Terrain digging" self-test section: dug ground (TerrainEdits.cs, decision 16). A crater and a tunnel on a small
/// terrain map: heights, ground and ceiling queries, rays and their normals; walking along the tunnel (under its roof)
/// and into its end wall; every drawn face agrees with the collision (solid behind it, air in front); bedrock; the
/// renderer's surface indices keep their length; the same ops on another copy give the same ground and survive the
/// wire; a restored map is whole again; queries don't allocate and craters are cheap. Then a real host and client on the
/// Construct Yard: a construct gun's explosive round digs, the client and a late joiner get the ops, a buggy over a fresh
/// crater falls in, and nothing digs with Destruction off or outside Sandbox.
/// </summary>
public static partial class SelfTest
{
    private const string DigLabJson = """
        {
          "format": "call-of-slop-map/1",
          "name": "Dig Lab",
          "halfX": 30,
          "halfZ": 30,
          "terrain": {
            "cell": 1,
            "margin": 8,
            "features": [ { "type": "hill", "x": 18, "z": -18, "radius": 8, "height": 3 } ]
          },
          "ops": [
            { "op": "Ground", "color": [0.36, 0.5, 0.25] },
            { "op": "Perimeter", "height": 3, "color": "#8f8a80" },
            { "op": "RingSpawns", "count": 8, "frac": 0.75 }
          ]
        }
        """;

    private static void DigLine(string s) => Line("  " + s);

    private static void CheckTerrainDig()
    {
        var previous = World.Map;
        var previousMover = World.Mover;
        try
        {
            World.Mover = ClassicMover.Instance;
            DigQueries();
            DigCopiesAndWire();
            DigSpeed();
            DigCraterEscape();
            World.Mover = SweptMover.Instance;
            DigCraterEscape(); // (the swept capsule controller too: hosts can pick it)
            World.Mover = ClassicMover.Instance;
            DigLoopback();
        }
        finally
        {
            World.Mover = previousMover;
            World.SetMap(previous);
        }
    }

    private static Map DigLab()
    {
        var m = MapFile.Load(DigLabJson, "dig lab");
        m.Validate();
        return m;
    }

    /// <summary>A crater at the middle, a tunnel along +Z at x = -10 three metres down, and a scoop into the hill.</summary>
    private static void DigLabOps(TerrainEdits e, float h0)
    {
        e.Apply(new DigOp(DigKind.Crater, new Vector3(0, h0, 0), 3f).Quantised);
        for (var z = -8f; z <= 8f; z += 0.6f)
            e.Apply(new DigOp(DigKind.Scoop, new Vector3(-10, h0 - 3f, z), 1.2f).Quantised);
        e.Apply(new DigOp(DigKind.Scoop, new Vector3(14, h0 + 1f, -18), 1.5f).Quantised);
    }

    private static void DigQueries()
    {
        var m = DigLab();
        World.SetMap(m);
        var t = m.Terrain!;
        var tc = m.TerrainCollision!;
        var h0 = t.Sample(0, 0);
        var before = (Sample: t.Sample(5, 0), Hash: t.Hash);
        DigLabOps(t.Edits, h0);
        var e = t.Edits;
        DigLine($"dig lab: {e.Log.Count} ops, {e.TouchedCells} cells dug ({e.Sub} x {e.Sub} columns of {e.Size:0.00} m), bedrock {e.Bedrock:0.0} m");
        if (t.Hash != before.Hash)
            Fail("dig lab: digging changed the terrain's fingerprint (dug ground is runtime state)");

        // The crater.
        var floor = t.Sample(0.1f, 0.1f);
        var rim = t.Sample(5, 0);
        if (floor > h0 - 1.4f || floor < h0 - 1.8f || MathF.Abs(rim - before.Sample) > 1e-4f)
            Fail($"dig lab: the crater's floor is at {floor - h0:0.00} m (want about -1.6) and 5 m out the ground moved {rim - before.Sample:0.000} m");
        var down = tc.Raycast(new Vector3(0.1f, h0 + 10, 0.1f), -Vector3.UnitY, 30);
        if (MathF.Abs(h0 + 10 - down - floor) > 0.06f)
            Fail($"dig lab: a ray down into the crater stopped at {h0 + 10 - down:0.00} m, the floor is at {floor:0.00} m");
        var across = tc.Raycast(new Vector3(0.1f, h0 - 1f, 0.1f), Vector3.UnitX, 10);
        var wallN = tc.HitNormal(new Vector3(0.1f + across, h0 - 1f, 0.1f), Vector3.UnitX);
        if (across is < 1.7f or > 3.0f || wallN.X > -0.9f)
            Fail($"dig lab: a ray across the crater met its wall {across:0.00} m out (want about 2.4) with normal {wallN}");

        // The tunnel: roofed, its floor and ceiling where they should be, the surface over it untouched.
        float tx = -10.1f, tz = 0.3f;
        var tFloor = t.GroundBelow(tx, tz, h0 - 3f);
        var tCeil = t.CeilingAbove(tx, tz, h0 - 3f);
        var tTop = t.Sample(tx, tz);
        var inside = !t.SolidAt(tx, tz, h0 - 3f) && t.SolidAt(tx, tz, h0 - 1.2f) && t.SolidAt(tx, tz, h0 - 4.5f);
        DigLine($"tunnel: floor {tFloor - h0:0.00} m, ceiling {tCeil - h0:0.00} m, surface {tTop - h0:0.00} m");
        if (!inside || MathF.Abs(tFloor - (h0 - 4.2f)) > 0.15f || MathF.Abs(tCeil - (h0 - 1.8f)) > 0.15f || MathF.Abs(tTop - h0) > 1e-3f)
            Fail("dig lab: the tunnel's floor, ceiling or roof is wrong");
        var up = tc.Raycast(new Vector3(tx, h0 - 3f, tz), Vector3.UnitY, 10);
        if (MathF.Abs(h0 - 3f + up - tCeil) > 0.06f || tc.HitNormal(new Vector3(tx, h0 - 3f + up, tz), Vector3.UnitY).Y > -0.9f)
            Fail($"dig lab: a ray up in the tunnel met the roof at {h0 - 3f + up - h0:0.00} m (ceiling {tCeil - h0:0.00})");

        // Walking: along the tunnel, under its roof, to its end wall; jumping bumps the head.
        var walk = new MoveState { Position = new Vector3(tx, tFloor, -6f), Grounded = true };
        var highest = float.MinValue;
        for (var k = 0; k < 4 * World.TickRate; k++)
        {
            World.Simulate(ref walk, new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint, Yaw = MathF.PI, SpeedPct = 100 }); // +Z
            highest = MathF.Max(highest, walk.Position.Y);
        }
        DigLine($"walk along the tunnel: ended at z {walk.Position.Z:0.00}, highest feet {highest - h0:0.00} m");
        if (walk.Position.Z < 7.5f || walk.Position.Z > 9.5f || highest > h0 - 2.5f)
            Fail($"dig lab: walking along the tunnel ended at z {walk.Position.Z:0.00} (the end wall is near 9) with feet up to {highest - h0:0.00} m (popped onto the roof?)");
        var jump = new MoveState { Position = new Vector3(tx, tFloor, 0), Grounded = true };
        var peak = jump.Position.Y;
        for (var k = 0; k < World.TickRate; k++)
        {
            World.Simulate(ref jump, new InputCmd { Buttons = Buttons.Jump, SpeedPct = 100 });
            peak = MathF.Max(peak, jump.Position.Y);
        }
        if (peak + World.PlayerHeight > tCeil + 0.02f)
            Fail($"dig lab: a jump in the tunnel put the head at {peak + World.PlayerHeight - h0:0.00} m, through the roof at {tCeil - h0:0.00} m");

        // Faces against the collision: solid just behind each face, air just in front.
        var check = new DigFaceCheck { T = t };
        for (var j = 0; j < t.CountZ - 1; j++)
        for (var i = 0; i < t.CountX - 1; i++)
            e.CellFaces(i, j, ref check);
        DigLine($"{check.Faces} faces drawn for the dug cells; {check.Wrong} disagree with the collision");
        if (check.Faces < 500 || check.Wrong > 0)
            Fail($"dig lab: {check.Wrong} of {check.Faces} dug faces don't match the collision (first: {check.First})");

        // Bedrock: digging far down stops there.
        for (var y = h0; y > e.Bedrock - 10; y -= 2)
            e.Apply(new DigOp(DigKind.Scoop, new Vector3(20, y, 20), 1.1f).Quantised);
        var deepest = t.Sample(20.1f, 20.1f);
        if (MathF.Abs(deepest - e.Bedrock) > 0.01f || !t.SolidAt(20.1f, 20.1f, e.Bedrock - 1))
            Fail($"dig lab: dug down to {deepest:0.00} m, bedrock is {e.Bedrock:0.00} m");

        // The renderer's surface: each chunk keeps its index count, dug cells degenerate.
        var (indices, chunks) = TerrainRenderer.SurfaceIndices(t);
        var scratch = new uint[TerrainEdits.ChunkCells * TerrainEdits.ChunkCells * 6];
        var sameLength = true;
        var degenerate = 0;
        for (var c = 0; c < chunks.Length; c++)
        {
            var n = TerrainRenderer.ChunkIndices(t, c % e.ChunksX, c / e.ChunksX, scratch, out _, out _);
            sameLength &= n == chunks[c].IndexCount;
            for (var k = 0; k < n; k += 3)
                if (scratch[k] == scratch[k + 1] && scratch[k + 1] == scratch[k + 2])
                    degenerate++;
        }
        if (!sameLength || degenerate != e.TouchedCells * 2 || indices.Length != (t.CountX - 1) * (t.CountZ - 1) * 6)
            Fail($"dig lab: the surface indices changed size or don't hide the dug cells ({degenerate} degenerate triangles, {e.TouchedCells * 2} expected)");

        // Restored: whole again.
        m.RestoreProps();
        if (t.IsDug || e.Log.Count != 0 || MathF.Abs(t.Sample(0.1f, 0.1f) - h0) > 1e-4f || tc.HasVoids)
            Fail("dig lab: restoring the map didn't fill the ground in");
    }

    private struct DigFaceCheck : IDigFaceSink
    {
        public TerrainData T;
        public int Faces, Wrong;
        public string First;

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector3 color, float rock)
        {
            Faces++;
            if ((Vector3.Cross(b - a, c - a) + Vector3.Cross(c - a, d - a)).LengthSquared() < 1e-8f)
                return; // (a sliver: nothing to stand behind)
            var mid = (a + b + c + d) * 0.25f;
            if (n.Y == 0 && MathF.Max(MathF.Max(a.Y, b.Y), MathF.Max(c.Y, d.Y)) - MathF.Min(MathF.Min(a.Y, b.Y), MathF.Min(c.Y, d.Y)) < 0.1f)
                return; // (a seam sliver against sloping ground: thinner than the probe's step across a slope)
            if (Vector3.Dot(Vector3.Cross(c - a, d - b), n) < 0)
            {
                Wrong++;
                First ??= $"face at {mid} wound away from its normal {n}";
                return;
            }
            const float eps = 0.02f;
            var front = mid + n * eps;
            var back = mid - n * eps;
            if (T.SolidAt(front.X, front.Z, front.Y) || !T.SolidAt(back.X, back.Z, back.Y))
            {
                Wrong++;
                First ??= $"face at {mid} normal {n}: front solid {T.SolidAt(front.X, front.Z, front.Y)}, back solid {T.SolidAt(back.X, back.Z, back.Y)} [{a} {b} {c} {d}] front {Spans(T, front)} back {Spans(T, back)}";
            }
        }
    }

    private static void DigCopiesAndWire()
    {
        var a = DigLab();
        var b = DigLab();
        var h0 = a.Terrain!.Sample(0, 0);
        DigLabOps(a.Terrain.Edits, h0);
        // Over the wire, as a joiner gets it.
        var w = new PacketWriter();
        var bytes = 0;
        var packets = Game.WriteTerrainDigState(w, a.Terrain.Edits, pw =>
        {
            bytes += pw.Span.Length;
            var r = new PacketReader(pw.Span[1..]);
            Game.ApplyTerrainDig(b.Terrain!, ref r);
        });
        var differ = 0;
        for (var z = -25f; z <= 25f; z += 0.37f)
        for (var x = -25f; x <= 25f; x += 0.37f)
        for (var y = h0 - 6; y <= h0 + 4; y += 0.5f)
            if (a.Terrain.SolidAt(x, z, y) != b.Terrain!.SolidAt(x, z, y))
                differ++;
        DigLine($"{a.Terrain.Edits.Log.Count} ops in {packets} packets ({bytes} bytes) rebuilt another copy: {differ} points differ");
        if (differ > 0 || b.Terrain!.Edits.Log.Count != a.Terrain.Edits.Log.Count)
            Fail("dig lab: a copy given the same ops over the wire has different ground");
    }

    private static void DigSpeed()
    {
        var m = DigLab();
        World.SetMap(m);
        var t = m.Terrain!;
        var h0 = t.Sample(0, 0);
        // Queries in and out of dug ground allocate nothing.
        DigLabOps(t.Edits, h0);
        var tc = m.TerrainCollision!;
        var sum = 0f;
        var a0 = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 2000; k++)
        {
            var x = -12 + k % 25;
            sum += t.Sample(x, 0.3f) + t.GroundBelow(x, 0.3f, h0 - 3) + tc.Raycast(new Vector3(x, h0 + 2, -1), Vector3.Normalize(new Vector3(0.2f, -1, 0.3f)), 20)
                   + tc.Layer(h0 - 3).HeightAt(x, 0.3f) + (t.SolidAt(x, 0.3f, h0 - 2) ? 1 : 0);
        }
        var alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        // An artillery barrage: 300 craters.
        var sw = Stopwatch.StartNew();
        var rng = new Random(7);
        for (var k = 0; k < 300; k++)
            t.Edits.Apply(new DigOp(DigKind.Crater, new Vector3(rng.NextSingle() * 50 - 25, h0, rng.NextSingle() * 50 - 25), 1 + rng.NextSingle() * 3).Quantised);
        var craterMs = sw.Elapsed.TotalMilliseconds / 300;
        // Faces for the whole dug map (what the renderer builds, a few chunks a frame).
        sw.Restart();
        var faces = new DigFaceCount();
        for (var j = 0; j < t.CountZ - 1; j++)
        for (var i = 0; i < t.CountX - 1; i++)
            t.Edits.CellFaces(i, j, ref faces);
        var faceMs = sw.Elapsed.TotalMilliseconds;
        // A ray over a well-dug map.
        sw.Restart();
        for (var k = 0; k < 1000; k++)
            sum += tc.Raycast(new Vector3(-25 + k % 50, h0 + 3, -25), Vector3.Normalize(new Vector3(0.1f, -0.15f, 1)), 60);
        var rayUs = sw.Elapsed.TotalMilliseconds;
        DigLine($"queries allocate {alloc} B; a crater takes {craterMs:0.000} ms; {t.Edits.TouchedCells} dug cells give {faces.N} faces in {faceMs:0.0} ms; a ray across them {rayUs:0.00} us ({sum:0})");
        if (alloc > 0)
            Fail($"terrain queries on dug ground allocated {alloc} B");
        if (craterMs > 0.5)
            Fail($"a crater took {craterMs:0.000} ms (budget 0.5 ms)");
        if (rayUs > 60)
            Fail($"a ray across dug ground took {rayUs:0.00} us (budget 60)");
        m.RestoreProps();
    }

    private static string Spans(TerrainData t, Vector3 p) => t.Edits.TryColumn(p.X, p.Z, out var s) ? string.Join(",", s.ToArray().Select(v => v.ToString("0.000"))) : $"base {t.BaseGroundTop(p.X, p.Z):0.000}";

    private struct DigFaceCount : IDigFaceSink
    {
        public int N;

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector3 color, float rock) => N++;
    }

    /// <summary>
    /// Frag-grenade craters on the Construct Yard (sloping ground), one grenade and then a second in the hole. Karl got stuck
    /// standing at the edge of two (2026-11-02): the second, lower blast cut under the first one's rim and left a low
    /// overhang, and walking (or jumping, or climbing) up the ground under it wedged a standing player's head in it
    /// (crouching got out). Overhangs stay (digging through is the point), so: a standing player's head never goes into the
    /// ground (a gap too low to stand in is a wall: crouch to go in), nobody sticks walking back in, and a player gets out
    /// of one crater every way. Two in one spot can make a pit with walls too tall to climb (counted, not failed).
    /// </summary>
    private static void DigCraterEscape()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Terrain is not { } t)
            return;
        yard.RestoreProps();
        World.SetMap(yard);
        var boxes = yard.AsBuiltSolid();
        var spots = new List<Vector3> { new(8, 0, -6), new(14, 0, 10), new(-20, 0, 0), new(20, 0, -20), new(2, 0, 18) };
        var frag = Throwables.Get(ThrowableKind.Frag).Radius;
        var r = Math.Clamp(frag * Game.CraterPerBlast, Game.CraterMin, Game.CraterMax);
        int roofs = 0, wedged = 0, crouched = 0, trapped = 0, pits = 0, stuck = 0, runs = 0, runs2 = 0;
        string first = "", firstWedge = "";
        for (var si = 0; si < spots.Count; si++)
        {
            var s0 = spots[si];
            var c = s0 with { Y = t.Sample(s0.X, s0.Z) + 0.25f }; // (a frag goes off 0.15 m over a 0.1 m grenade on the ground)
            var floor0 = t.Sample(c.X + 0.1f, c.Z + 0.1f);
            t.Edits.Apply(new DigOp(DigKind.Crater, c, r).Quantised);
            if (floor0 - t.Sample(c.X + 0.1f, c.Z + 0.1f) < 1f)
                Fail($"a frag crater at ({c.X:0}, {c.Z:0}) on open ground dug only {floor0 - t.Sample(c.X + 0.1f, c.Z + 0.1f):0.00} m");
            Escapes(c, c, r, true, ref runs, ref trapped);
            // The second grenade: in the hole, off the middle (a different way at each spot).
            var off = World.FlatForward(si * 1.3f) * (r * (0.3f + 0.1f * si));
            var c2 = new Vector3(c.X + off.X, 0, c.Z + off.Z);
            c2.Y = t.Sample(c2.X, c2.Z) + 0.25f;
            t.Edits.Apply(new DigOp(DigKind.Crater, c2, r).Quantised);
            // Overhangs: a dug column with air under ground that is still there (counted only: they're allowed).
            var reach = r + off.Length();
            for (var z = c.Z - reach; z <= c.Z + reach; z += 0.25f)
            for (var x = c.X - reach; x <= c.X + reach; x += 0.25f)
                if (t.Edits.TryColumn(x, z, out var sp) && sp.Length > 2)
                    roofs++;
            Escapes(c, c2, reach, false, ref runs2, ref pits);
        }
        DigLine($"{World.Mover.GetType().Name}: frag craters (radius {r:0.0} m) at {spots.Count} spots on {ConstructYardMap}: one: {runs} ways out, {trapped} trapped; a second in each: {roofs} overhang points, {runs2} ways out, {pits} walled in (a pit); {crouched} crouched under a lip, {stuck} stuck walking back in, {wedged} ticks with a head in the ground");
        if (wedged > 0 || trapped > 0 || stuck > 0)
            Fail($"a player was wedged under an overhang ({wedged} ticks), couldn't get out of a crater ({trapped} of {runs}) or stuck walking back in ({stuck}); first: {firstWedge} {first}");
        yard.RestoreProps();

        // Out every way from the bottom: sprint forward, jumping now and then (a mantle), 3 s; when a lip stops a standing
        // player, the same crouched. Then back in towards the middle (standing up again): never stuck in place on the way.
        void Escapes(Vector3 c, Vector3 from, float reach, bool mustOut, ref int count, ref int notOut)
        {
            for (var a = 0; a < 8; a++)
            {
                var yaw = a * MathF.PI / 4;
                if (WalledOff(boxes, from, yaw, reach + 0.6f))
                    continue; // (a map structure stands that way: a wall, not a trap)
                count++;
                var start = new MoveState { Position = new Vector3(from.X + 0.1f, t.Sample(from.X + 0.1f, from.Z + 0.1f), from.Z + 0.1f), Grounded = true };
                var st = start;
                var outside = Escape(ref st, Buttons.Sprint, yaw, from, reach);
                if (!outside)
                {
                    st = start;
                    outside = Escape(ref st, Buttons.Crouch, yaw, from, reach);
                    if (outside)
                        crouched++;
                }
                if (!outside)
                {
                    notOut++;
                    if (mustOut)
                        Note($"crater(s) at ({c.X:0.0}, {c.Z:0.0}) / ({from.X:0.0}, {from.Z:0.0}) heading {a * 45} deg: ended crouched at ({st.Position.X:0.00}, {st.Position.Y:0.00}, {st.Position.Z:0.00}), ground outside {t.Sample(from.X + World.FlatForward(yaw).X * (reach + 1), from.Z + World.FlatForward(yaw).Z * (reach + 1)):0.00}");
                }
                var moved = 0f;
                var last = st.Position;
                for (var k = 0; k < World.TickRate; k++)
                {
                    World.Simulate(ref st, new InputCmd { Buttons = Buttons.Forward, Yaw = yaw + MathF.PI, SpeedPct = 100 });
                    Wedge(st, "walking back in");
                    moved += Vector3.Distance(last, st.Position);
                    last = st.Position;
                }
                if (moved < 0.5f)
                {
                    stuck++;
                    Note($"crater(s) at ({c.X:0.0}, {c.Z:0.0}) / ({from.X:0.0}, {from.Z:0.0}) heading {a * 45} deg: stuck walking back in at ({st.Position.X:0.00}, {st.Position.Y:0.00}, {st.Position.Z:0.00})");
                }
            }
        }

        bool Escape(ref MoveState m, Buttons how, float yaw, Vector3 from, float reach)
        {
            for (var k = 0; k < 3 * World.TickRate; k++)
            {
                World.Simulate(ref m, new InputCmd { Buttons = Buttons.Forward | how | (k % 20 < 2 ? Buttons.Jump : Buttons.None), Yaw = yaw, SpeedPct = 100 });
                Wedge(m, how == Buttons.Crouch ? "crouched" : "standing");
                var dx = m.Position.X - from.X;
                var dz = m.Position.Z - from.Z;
                if (dx * dx + dz * dz > (reach + 0.6f) * (reach + 0.6f))
                    return true;
            }
            return false;
        }

        void Note(string what)
        {
            if (first.Length == 0)
                first = what;
        }

        void Wedge(in MoveState m, string doing)
        {
            var ceiling = t.CeilingAbove(m.Position.X, m.Position.Z, m.Position.Y + 0.05f);
            if (m.Position.Y + World.HeightOf(m.Stance) <= ceiling + 0.05f)
                return;
            wedged++;
            if (firstWedge.Length == 0)
                firstWedge = $"{doing}: head in the ground at ({m.Position.X:0.00}, {m.Position.Y:0.00}, {m.Position.Z:0.00}), {m.Stance}, ceiling {ceiling:0.00}";
        }
    }

    /// <summary>Does a map box stand in the way of walking straight out from <paramref name="c"/> along <paramref name="yaw"/>?</summary>
    private static bool WalledOff(Box[] boxes, Vector3 c, float yaw, float reach)
    {
        var dir = World.FlatForward(yaw);
        for (var d = 0f; d <= reach + 0.5f; d += 0.25f)
        {
            var p = c + dir * d;
            foreach (var b in boxes)
                if (p.X > b.Min.X - 0.6f && p.X < b.Max.X + 0.6f && p.Z > b.Min.Z - 0.6f && p.Z < b.Max.Z + 0.6f && b.Max.Y > c.Y - 1f && b.Min.Y < c.Y + 2f)
                    return true;
        }
        return false;
    }

    /// <summary>A real host and client on the Construct Yard: the ops travel, a late joiner gets them, a buggy falls into a crater.</summary>
    private static void DigLoopback()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Terrain is not { } t)
        {
            Fail($"terrain digging: the {ConstructYardMap} map is missing or has no terrain");
            return;
        }
        const ushort port = 26297;
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
        Pump(server, [a], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.TerrainDigLive)
        {
            Fail($"terrain digging loopback: the client didn't join or digging is off in Sandbox (live {server.TerrainDigLive})");
            return;
        }
        // A construct gun's explosive round on open ground (the hook construct guns use).
        var car = server.Constructs.Bodies.FirstOrDefault(b => b.CanDrive);
        var open = new Vector3(car != null ? car.WorldCom.X + 12 : 0, 0, car != null ? car.WorldCom.Z : 0);
        open.Y = t.Sample(open.X, open.Z);
        var g0 = t.Sample(open.X + 0.1f, open.Z + 0.1f);
        ConstructWeapons.Explode(server.Constructs, open, 5f, 10f, null, CFireKind.Blast);
        var g1 = t.Sample(open.X + 0.1f, open.Z + 0.1f);
        // Under the buggy: it falls in.
        var carY0 = car?.WorldCom.Y ?? 0;
        if (car != null)
            server.Dig(new DigOp(DigKind.Crater, new Vector3(car.WorldCom.X, t.Sample(car.WorldCom.X, car.WorldCom.Z), car.WorldCom.Z), 4f));
        Pump(server, [a], 1.5, ref now, clock);
        var sank = car == null ? 0 : carY0 - car.WorldCom.Y;
        var ops = server.DigOps;
        var gotA = a.DigOpsRead;
        // A late joiner gets the whole log.
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a, b], 2.0, ref now, clock);
        var gotB = b.DigOpsRead;
        // Off: Destruction off, then another mode.
        server.DestructionEnabled = false;
        server.BlastCrater(open + new Vector3(0, 0, 8), 5f);
        server.DestructionEnabled = true;
        server.Mode = GameMode.TeamDeathmatch;
        server.BlastCrater(open + new Vector3(0, 0, -8), 5f);
        var offOps = server.DigOps - ops;
        DigLine($"loopback ({clock.Elapsed.TotalSeconds:0.0} s): crater {g0 - g1:0.00} m deep, buggy sank {sank:0.00} m, {ops} ops on the server, client read {gotA}, late joiner {gotB}, {offOps} dug with digging off");
        if (g0 - g1 < 0.8f)
            Fail("terrain digging loopback: a construct gun's explosive round left no crater");
        if (car != null && sank < 0.5f)
            Fail($"terrain digging loopback: the buggy over a fresh crater only sank {sank:0.00} m");
        if (gotA != ops || gotB != ops || ops < 2)
            Fail($"terrain digging loopback: the client read {gotA} and the late joiner {gotB} of the server's {ops} ops");
        if (offOps != 0)
            Fail("terrain digging loopback: the ground was dug with Destruction off or outside Sandbox");
        yard.RestoreProps();
    }
}
