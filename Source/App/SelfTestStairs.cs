using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The "Stairs" self-test section: every staircase is walkable standing up. It finds every flight of steps on every map
/// of every pack, and in every building of the Buildings folder (each placed alone on a test map, in each floor count
/// that changes its geometry), from the box geometry itself: chains of tread-shaped boxes, each a step up from the last
/// (so map ops, building stairs, escalators, props used as steps and hand-placed blocks are all found). For each flight:
/// <list type="bullet">
/// <item>headroom: a standing body (0.8 m footprint) on any tread, on the landings just off both ends included, keeps
/// <see cref="BuildingGen.StairHeadroom"/> (2.1 m) under the lowest thing over it (slab, ceiling, beam, lintel);</item>
/// <item>risers: no step is taller than the step-up height (<see cref="World.StepHeight"/>);</item>
/// <item>width: at least <see cref="StairWidthMin"/> clear between railings and side walls;</item>
/// <item>landings: <see cref="StairLandingMin"/> clear beyond the top and the foot, or room to turn off sideways;</item>
/// <item>walks: the real movers (classic and swept), standing with plain walk input, go from the foot to the top and back
/// down without being stopped (sampled: once per distinct flight and its surroundings, both side lanes and the middle).</item>
/// </list>
/// Tunnel paths through terrain caves (metro stairs) keep the same headroom. Each failure names the map or building, the
/// flight's place and the cause in cm. Also checks that every building staircase was found (the detector's coverage).
/// </summary>
public static partial class SelfTest
{
    /// <summary>The least clear width of a flight, railing to wall.</summary>
    private const float StairWidthMin = 1.0f;

    /// <summary>The least clear depth beyond a flight's top and foot when there's no room to turn off sideways.</summary>
    private const float StairLandingMin = 1.2f;

    private const float TreadMinRun = 0.15f, TreadMaxRun = 1.6f, TreadMinWidth = 0.6f, StepMinRise = 0.05f, StepMaxRise = 0.62f;

    /// <summary>One flight of steps found in a map's boxes.</summary>
    private sealed class StairFlight
    {
        public int Axis; // 0: climbs along X, 2: along Z
        public int Dir; // +1 or -1 along that axis
        public readonly List<int> Treads = new(); // Solid indices, foot to top (a destructible building's tread cut into chunks: one of its pieces)
        public readonly List<Box> Boxes = new(); // the treads' boxes (chunks joined back into the whole tread)
        public float U0, U1; // along the climb (u = coordinate * Dir): the first tread's near face, the last tread's far face
        public float C0, C1; // the across span every tread shares
        public float Y0, Y1; // the floor at the foot, the last tread's top
        public float MaxRise;
        public string Where = "";
    }

    // TEMP (dev runs): geometry dumps of chosen flights, written as annotations.
    private static readonly List<string> StairDevDump = new();
    private static readonly (string Map, float X, float Z)[] StairDevTargets =
    [
        ("Bocage", 6.6f, -34.3f), ("London", 41.9f, 7.9f), ("Slopview Mall", -59.0f, -7.0f),
    ];

    private static void StairDump(Map m, PhysicsWorld phys, StairFlight f)
    {
        var c = StairPoint(f, (f.U0 + f.U1) / 2, (f.C0 + f.C1) / 2, 0);
        if (!StairDevTargets.Any(t => t.Map == m.Name && MathF.Abs(t.X - c.X) < 2.5f && MathF.Abs(t.Z - c.Z) < 2.5f))
            return;
        var p0 = StairPoint(f, f.U0 - 1.5f, f.C0 - 1.2f, 0);
        var p1 = StairPoint(f, f.U1 + 1.5f, f.C1 + 1.2f, 0);
        var sb = new System.Text.StringBuilder($"DUMP {m.Name} {f.Where} U {f.U0:0.00}..{f.U1:0.00} C {f.C0:0.00}..{f.C1:0.00} Y {f.Y0:0.00}..{f.Y1:0.00} treads {f.Treads.Count}:");
        foreach (var j in phys.CollectBoxes(MathF.Min(p0.X, p1.X), MathF.Min(p0.Z, p1.Z), MathF.Max(p0.X, p1.X), MathF.Max(p0.Z, p1.Z)))
        {
            if (j >= m.Solid.Count)
                continue;
            var o = m.Solid[j];
            if (o.Max.Y < f.Y0 - 0.3f || o.Min.Y > f.Y1 + 2.3f || o.Max.Y < -100)
                continue;
            var k = f.Treads.Contains(j) ? "T" : m.IsPropCollider(j) ? "P" : m.IsBuildingBox(j) ? "B" : "M";
            sb.Append($" {k}[{o.Min.X:0.00},{o.Min.Y:0.00},{o.Min.Z:0.00}/{o.Max.X:0.00},{o.Max.Y:0.00},{o.Max.Z:0.00}]");
        }
        StairDevDump.Add(sb.ToString());
    }

    private static void CheckStairs()
    {
        var sw = Stopwatch.StartNew();
        var previousMover = World.Mover;
        var problems = new List<string>();
        int flights = 0, walked = 0, maps = 0;
        var seen = new HashSet<ulong>(); // flight signatures already walked (identical placements walk once)
        var worst = float.MaxValue;
        var perPack = new Dictionary<string, (int Flights, int Bad)>();
        try
        {
            // 1. Every building in the Buildings folder on its own, in every floor count that changes its geometry.
            foreach (var d in Buildings.All)
            {
                var counts = new SortedSet<int> { d.DefaultFloors > 0 ? d.DefaultFloors : d.MinFloors };
                if (d.MaxFloors > d.MinFloors)
                {
                    counts.Add(d.MinFloors);
                    counts.Add(Math.Min(d.MaxFloors, d.MinFloors + 3));
                }
                foreach (var floors in counts)
                {
                    var m = new Map { Name = $"Stairs: {d.Name} ({floors} floors)", Description = "self-test scene", HalfX = 150, HalfZ = 150 };
                    m.Ground(new Vector3(0.4f));
                    try
                    {
                        m.PlaceBuilding(d.Name, [0f, 0f], 0, 0, new MapJson($"{{\"floors\": {floors}}}"));
                    }
                    catch (InvalidOperationException ex)
                    {
                        problems.Add($"building {d.Name} ({floors} floors): doesn't build: {ex.Message}");
                        continue;
                    }
                    AuditMapStairs(m, $"building {d.Name} ({floors} floors)", problems, seen, ref flights, ref walked, ref worst, perPack, "Buildings");
                    maps++;
                }
            }
            // 2. Every map of every pack.
            foreach (var m in Maps.All)
            {
                AuditMapStairs(m, $"{m.Pack?.Name ?? "?"} / {m.Name}", problems, seen, ref flights, ref walked, ref worst, perPack, m.Pack?.Name ?? "?");
                maps++;
            }
        }
        finally
        {
            World.Mover = previousMover;
            World.SetMap(Maps.All[0]);
        }
        var summary = $"stairs: {flights} flights on {maps} maps and buildings ({walked} walked standing, classic and swept, up and down), "
                      + $"{problems.Count} problem(s); least headroom {worst:0.00} m (rule {BuildingGen.StairHeadroom} m); "
                      + string.Join(", ", perPack.Select(kv => $"{kv.Key} {kv.Value.Flights}" + (kv.Value.Bad > 0 ? $" ({kv.Value.Bad} bad)" : "")))
                      + $"; {sw.Elapsed.TotalSeconds:0.0} s";
        Line(summary);
        foreach (var p in problems)
            Line("  " + p);
        foreach (var p in problems.Take(DevStairsOnly ? 0 : 30)) // TEMP: dev runs report through annotations only
            Fail(p);
        if (problems.Count > 30 && !DevStairsOnly)
            Fail($"stairs: {problems.Count - 30} more problem(s) (see the log)");
        Line("::notice title=Stairs (results)::" + summary.Replace("%", "%25"));
        { // TEMP: dumps, up to 4 annotations
            var chunk = "";
            var chunks = 0;
            foreach (var d in StairDevDump.OrderBy(d => d.StartsWith("CAVE") ? 0 : 1))
            {
                Line("  " + d);
                if (chunk.Length + d.Length > 3900 && chunk.Length > 0)
                {
                    if (chunks++ < 3)
                        Line("::error title=Stairs dump::" + chunk);
                    chunk = "";
                }
                chunk += (chunk.Length > 0 ? "%0A" : "") + (d.Length > 3900 ? d[..3900] : d);
            }
            if (chunk.Length > 0 && chunks < 3)
                Line("::error title=Stairs dump::" + chunk);
        }
        foreach (var g in problems.GroupBy(p => p.Split(':')[0].Split(" / ")[0].Split(' ')[0]).OrderBy(g => Array.IndexOf(["Classic", "High", "Cluttered", "World", "Collision", "Dev", "building"], g.Key)).Take(4)) // TEMP: dev report, one annotation per pack
        {
            var text = "";
            var shapes = new HashSet<string>();
            foreach (var p in g)
            {
                if (!shapes.Add(System.Text.RegularExpressions.Regex.Replace(p, "[-0-9.]+", "#")))
                    continue;
                if (text.Length + p.Length > 3900)
                    break;
                text += (text.Length > 0 ? "%0A" : "") + p.Replace("%", "%25");
            }
            Line($"::error title=Stairs {g.Key} ({g.Count()})::" + text);
        }
    }

    /// <summary>Finds and checks every flight on one map (and, on maps with caves, the tunnel paths' headroom).</summary>
    private static void AuditMapStairs(Map m, string label, List<string> problems, HashSet<ulong> seen, ref int flights, ref int walked,
        ref float worst, Dictionary<string, (int Flights, int Bad)> perPack, string pack)
    {
        World.SetMap(m);
        var phys = World.Physics;
        var found = FindStairFlights(m, phys);
        var bad = 0;
        foreach (var f in found)
        {
            flights++;
            var before = problems.Count;
            CheckStairFlight(m, phys, f, label, problems, seen, ref walked, ref worst);
            if (problems.Count > before)
                bad++;
        }
        // Coverage: every staircase a building planned was found.
        foreach (var b in m.PlacedBuildings)
            foreach (var (bottom, top) in b.Plan.Stairs)
                if (!found.Any(f => MathF.Abs(f.Y1 - top.Y) < 0.06f && Vector2.Distance(new Vector2(bottom.X, bottom.Z), Flat(StairPoint(f, f.U0, (f.C0 + f.C1) / 2, 0))) < 1.3f))
                {
                    problems.Add($"{label}: {b.Def.Name}'s stairs from ({bottom.X:0.0}, {bottom.Y:0.0}, {bottom.Z:0.0}) up to {top.Y:0.0} m weren't found as a flight (stair detector)");
                    bad++;
                }
        // Tunnels through terrain caves (metro stairs): the same headroom along their paths.
        if (m.Terrain?.Voids is { } voids && m.TerrainCollision is { } tc)
        {
            var low = float.MaxValue;
            var at = Vector3.Zero;
            foreach (var p in voids.NavPoints)
            {
                var g = tc.GroundBelow(p.X, p.Z, p.Y + 0.5f);
                var c = MathF.Min(tc.CeilingAbove(p.X, p.Z, g + 0.1f), CeilingOver(m, phys, p.X, p.Z, g, out _));
                if (c - g < low)
                {
                    low = c - g;
                    at = p with { Y = g };
                }
            }
            Line($"  {label}: cave tunnels' least headroom {low * 100:0} cm at ({at.X:0.0}, {at.Y:0.0}, {at.Z:0.0})");
            StairDevDump.Add($"CAVE {label} least {low:0.00} at ({at.X:0.0},{at.Y:0.0},{at.Z:0.0})"); // TEMP
        }
        var t = perPack.GetValueOrDefault(pack);
        perPack[pack] = (t.Flights + found.Count, t.Bad + bad);
    }

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);

    /// <summary>The world point at (along, across, height) in a flight's frame.</summary>
    private static Vector3 StairPoint(StairFlight f, float u, float c, float y) => f.Axis == 0 ? new Vector3(u * f.Dir, y, c) : new Vector3(c, y, u * f.Dir);

    private static float AlongLo(in Box b, int axis, int dir) => dir > 0 ? (axis == 0 ? b.Min.X : b.Min.Z) : -(axis == 0 ? b.Max.X : b.Max.Z);

    private static float AlongHi(in Box b, int axis, int dir) => dir > 0 ? (axis == 0 ? b.Max.X : b.Max.Z) : -(axis == 0 ? b.Min.X : b.Min.Z);

    private static float AcrossLo(in Box b, int axis) => axis == 0 ? b.Min.Z : b.Min.X;

    private static float AcrossHi(in Box b, int axis) => axis == 0 ? b.Max.Z : b.Max.X;

    /// <summary>Boxes the stair checks ignore: parked (broken or open) ones and building doors (they open).</summary>
    private static bool StairIgnored(Map m, int i, in Box b) => b.Max.Y < -100f || m.IsBuildingDoorBox(i);

    /// <summary>
    /// Flights in a map's boxes: tread-shaped boxes (0.15-1.6 m deep along the climb, at least 0.6 m wide, room above
    /// them), each followed by the next one a step higher (5-62 cm) that starts where it ends and shares 0.6 m of its
    /// width; chains of three treads or more climbing at least 0.6 m.
    /// </summary>
    private static List<StairFlight> FindStairFlights(Map m, PhysicsWorld phys)
    {
        var raw = m.Solid;
        var n = raw.Count;
        var group = StairChunkGroups(m, phys, out var solid); // solid[i]: box i, or the whole tread its chunk was cut from
        var tread = new bool[n];
        for (var i = 0; i < n; i++)
        {
            var b = solid[i];
            if (group[i] != i || StairIgnored(m, i, raw[i]) || m.IsBuildingGlass(i) || i == m.GroundIndex || b.Max.Y - b.Min.Y < 0.03f)
                continue;
            float sx = b.Max.X - b.Min.X, sz = b.Max.Z - b.Min.Z;
            if (!(sx is >= TreadMinRun and <= TreadMaxRun && sz >= TreadMinWidth || sz is >= TreadMinRun and <= TreadMaxRun && sx >= TreadMinWidth))
                continue;
            tread[i] = FreeAbove(m, phys, i, b, group);
        }
        // next[i * 4 + slot]: the tread a step up from i along slot (0 +X, 1 -X, 2 +Z, 3 -Z); -1 none.
        var next = new int[n * 4];
        var hasPrev = new bool[n * 4];
        Array.Fill(next, -1);
        for (var i = 0; i < n; i++)
        {
            if (!tread[i])
                continue;
            var a = solid[i];
            for (var slot = 0; slot < 4; slot++)
            {
                int axis = slot < 2 ? 0 : 2, dir = slot % 2 == 0 ? 1 : -1;
                var run = AlongHi(a, axis, dir) - AlongLo(a, axis, dir);
                float a0 = AcrossLo(a, axis), a1 = AcrossHi(a, axis);
                if (run > TreadMaxRun || a1 - a0 < TreadMinWidth)
                    continue;
                var far = AlongHi(a, axis, dir);
                var p0 = StairPoint(new StairFlight { Axis = axis, Dir = dir }, far - 0.07f, a0, 0);
                var p1 = StairPoint(new StairFlight { Axis = axis, Dir = dir }, far + 0.07f, a1, 0);
                var best = -1;
                var bestOverlap = 0f;
                foreach (var jr in phys.CollectBoxes(MathF.Min(p0.X, p1.X), MathF.Min(p0.Z, p1.Z), MathF.Max(p0.X, p1.X), MathF.Max(p0.Z, p1.Z)))
                {
                    var j = jr < n ? group[jr] : -1;
                    if (j < 0 || j == i || !tread[j])
                        continue;
                    var b = solid[j];
                    var rise = b.Max.Y - a.Max.Y;
                    if (rise < StepMinRise || rise > StepMaxRise || MathF.Abs(AlongLo(b, axis, dir) - far) > 0.06f)
                        continue;
                    var brun = AlongHi(b, axis, dir) - AlongLo(b, axis, dir);
                    var overlap = MathF.Min(a1, AcrossHi(b, axis)) - MathF.Max(a0, AcrossLo(b, axis));
                    if (brun > TreadMaxRun || overlap < TreadMinWidth || overlap <= bestOverlap)
                        continue;
                    best = j;
                    bestOverlap = overlap;
                }
                if (best < 0)
                    continue;
                next[i * 4 + slot] = best;
                hasPrev[best * 4 + slot] = true;
            }
        }
        var list = new List<StairFlight>();
        for (var i = 0; i < n; i++)
        {
            if (!tread[i])
                continue;
            for (var slot = 0; slot < 4; slot++)
            {
                if (hasPrev[i * 4 + slot] || next[i * 4 + slot] < 0)
                    continue;
                var f = new StairFlight { Axis = slot < 2 ? 0 : 2, Dir = slot % 2 == 0 ? 1 : -1 };
                f.C0 = AcrossLo(solid[i], f.Axis);
                f.C1 = AcrossHi(solid[i], f.Axis);
                for (var k = i; k >= 0 && f.Treads.Count < 200; k = next[k * 4 + slot])
                {
                    var b = solid[k];
                    float c0 = MathF.Max(f.C0, AcrossLo(b, f.Axis)), c1 = MathF.Min(f.C1, AcrossHi(b, f.Axis));
                    if (c1 - c0 < TreadMinWidth)
                        break;
                    if (f.Treads.Count > 0)
                        f.MaxRise = MathF.Max(f.MaxRise, b.Max.Y - f.Boxes[^1].Max.Y);
                    (f.C0, f.C1) = (c0, c1);
                    f.Treads.Add(k);
                    f.Boxes.Add(b);
                }
                // The foot: leading treads a body can stand on whole (0.8 m deep or more) are the landing in front of the
                // flight, and one flush with the floor in front of it isn't a step.
                while (f.Treads.Count >= 3)
                {
                    var b = f.Boxes[0];
                    var front = StairPoint(f, AlongLo(b, f.Axis, f.Dir) - 0.45f, (f.C0 + f.C1) / 2, 0);
                    if (AlongHi(b, f.Axis, f.Dir) - AlongLo(b, f.Axis, f.Dir) < 2 * World.PlayerRadius
                        && SupportAt(m, phys, front.X, front.Z, b.Max.Y + World.StepHeight, b.Max.Y - 3f, 0.1f) < b.Max.Y - 0.04f
                        && !(m.TerrainCollision is { } tg && tg.GroundBelow(front.X, front.Z, b.Max.Y + 3f) >= b.Max.Y - 0.04f))
                        break; // (a tread level with the ground in front of it, or under it, isn't a step)
                    f.Treads.RemoveAt(0);
                    f.Boxes.RemoveAt(0);
                }
                if (f.Treads.Count < 3)
                    continue;
                (f.C0, f.C1) = (float.MinValue, float.MaxValue);
                f.MaxRise = 0;
                for (var k = 0; k < f.Treads.Count; k++)
                {
                    var b = f.Boxes[k];
                    f.C0 = MathF.Max(f.C0, AcrossLo(b, f.Axis));
                    f.C1 = MathF.Min(f.C1, AcrossHi(b, f.Axis));
                    if (k > 0)
                        f.MaxRise = MathF.Max(f.MaxRise, b.Max.Y - f.Boxes[k - 1].Max.Y);
                }
                var first = f.Boxes[0];
                var last = f.Boxes[^1];
                if (last.Max.Y - first.Max.Y < 0.6f)
                    continue;
                f.U0 = AlongLo(first, f.Axis, f.Dir);
                f.U1 = AlongHi(last, f.Axis, f.Dir);
                f.Y1 = last.Max.Y;
                var mid = (f.C0 + f.C1) / 2;
                var foot = StairPoint(f, f.U0 - 0.45f, mid, 0);
                var ground = SupportAt(m, phys, foot.X, foot.Z, first.Max.Y - StepMinRise, first.Max.Y - 3f);
                f.Y0 = float.IsNegativeInfinity(ground) ? first.Min.Y : ground;
                // Where: a building (by its shell range) or the map.
                var place = $"({(first.Center.X + last.Center.X) / 2:0.0}, {(first.Center.Z + last.Center.Z) / 2:0.0})";
                foreach (var pb in m.PlacedBuildings)
                    if (f.Treads[0] >= pb.ShellStart && f.Treads[0] < pb.ShellStart + pb.ShellCount)
                        place = $"{pb.Def.Name} #{pb.Index} " + place;
                f.Where = $"flight {f.Y0:0.0}->{f.Y1:0.0} m at {place} climbing {(f.Dir > 0 ? "+" : "-")}{(f.Axis == 0 ? "x" : "z")}";
                list.Add(f);
            }
        }
        return list;
    }

    /// <summary>
    /// Destructible buildings' shells are cut into chunks (MapDestruction.cs): a tread can be several boxes side by side.
    /// Chunks with the same height range that share a whole face are joined back: group[i] is the lowest Solid index of
    /// box i's piece group (i itself for an ordinary box), boxes[group] the whole tread's box.
    /// </summary>
    private static int[] StairChunkGroups(Map m, PhysicsWorld phys, out Box[] boxes)
    {
        var solid = m.Solid;
        var n = solid.Count;
        var parent = new int[n];
        for (var i = 0; i < n; i++)
            parent[i] = i;
        int Find(int i)
        {
            while (parent[i] != i)
                i = parent[i] = parent[parent[i]];
            return i;
        }
        static bool Same(float a, float b) => MathF.Abs(a - b) < 0.003f;
        if (m.Destruction is { } d)
        {
            var near = new List<int>();
            for (var i = 0; i < n; i++)
            {
                if (!d.IsChunk(i, out _))
                    continue;
                var a = solid[i];
                near.Clear();
                foreach (var j in phys.CollectBoxes(a.Min - new Vector3(0.01f), a.Max + new Vector3(0.01f)))
                    if (j > i && j < n)
                        near.Add(j);
                foreach (var j in near)
                {
                    var b = solid[j];
                    if (!d.IsChunk(j, out _) || !Same(a.Min.Y, b.Min.Y) || !Same(a.Max.Y, b.Max.Y))
                        continue;
                    var alongZ = Same(a.Min.X, b.Min.X) && Same(a.Max.X, b.Max.X) && (Same(a.Max.Z, b.Min.Z) || Same(b.Max.Z, a.Min.Z));
                    var alongX = Same(a.Min.Z, b.Min.Z) && Same(a.Max.Z, b.Max.Z) && (Same(a.Max.X, b.Min.X) || Same(b.Max.X, a.Min.X));
                    if (alongX || alongZ)
                    {
                        int ra = Find(i), rb = Find(j);
                        if (ra != rb)
                            parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
                    }
                }
            }
        }
        var group = new int[n];
        boxes = new Box[n];
        var lo = new Vector3[n];
        var hi = new Vector3[n];
        for (var i = 0; i < n; i++)
        {
            group[i] = Find(i);
            lo[i] = solid[i].Min;
            hi[i] = solid[i].Max;
        }
        for (var i = 0; i < n; i++)
        {
            var g = group[i];
            if (g == i)
                continue;
            lo[g] = Vector3.Min(lo[g], solid[i].Min);
            hi[g] = Vector3.Max(hi[g], solid[i].Max);
        }
        for (var i = 0; i < n; i++)
        {
            var g = group[i];
            boxes[i] = new Box((lo[g] + hi[g]) / 2, (hi[g] - lo[g]) / 2);
        }
        return group;
    }

    /// <summary>Is there room to stand on a tread (nothing within 1 m over its top at its middle, or at a third along its length either side)?</summary>
    private static bool FreeAbove(Map m, PhysicsWorld phys, int i, in Box b, int[] group)
    {
        var alongX = b.Max.X - b.Min.X >= b.Max.Z - b.Min.Z;
        for (var s = -1; s <= 1; s++)
        {
            var x = alongX ? b.Center.X + s * b.Half.X * 0.66f : b.Center.X;
            var z = alongX ? b.Center.Z : b.Center.Z + s * b.Half.Z * 0.66f;
            var free = true;
            foreach (var j in phys.CollectBoxes(x - 0.01f, z - 0.01f, x + 0.01f, z + 0.01f))
            {
                if (j >= m.Solid.Count || group[j] == i)
                    continue;
                var o = m.Solid[j];
                if (StairIgnored(m, j, o))
                    continue;
                if (x > o.Min.X && x < o.Max.X && z > o.Min.Z && z < o.Max.Z && o.Min.Y < b.Max.Y + 1.0f && o.Max.Y > b.Max.Y + 0.02f)
                {
                    free = false;
                    break;
                }
            }
            if (free)
                return true;
        }
        return false;
    }

    /// <summary>The highest floor under a standing body's footprint at (x, z) between <paramref name="yMin"/> and <paramref name="yMax"/> (boxes and terrain); -infinity if none.</summary>
    private static float SupportAt(Map m, PhysicsWorld phys, float x, float z, float yMax, float yMin, float r = World.PlayerRadius)
    {
        var best = float.NegativeInfinity;
        foreach (var j in phys.CollectBoxes(x - r, z - r, x + r, z + r))
        {
            if (j >= m.Solid.Count)
                continue;
            var o = m.Solid[j];
            if (StairIgnored(m, j, o) || !(x + r > o.Min.X && x - r < o.Max.X && z + r > o.Min.Z && z - r < o.Max.Z))
                continue;
            if (o.Max.Y <= yMax + 0.001f && o.Max.Y >= yMin && o.Max.Y > best)
                best = o.Max.Y;
        }
        if (m.TerrainCollision is { } t)
        {
            var g = t.GroundBelow(x, z, yMax + 0.01f);
            if (g <= yMax + 0.001f && g >= yMin && g > best)
                best = g;
        }
        return best;
    }

    /// <summary>The lowest underside over a standing body's footprint at (x, z) at least 0.3 m above its feet (boxes, a cave's ceiling); +infinity if open.</summary>
    private static float CeilingOver(Map m, PhysicsWorld phys, float x, float z, float feet, out int box)
    {
        const float r = World.PlayerRadius;
        var low = float.PositiveInfinity;
        box = -1;
        foreach (var j in phys.CollectBoxes(x - r, z - r, x + r, z + r))
        {
            if (j >= m.Solid.Count)
                continue;
            var o = m.Solid[j];
            if (StairIgnored(m, j, o) || !(x + r > o.Min.X && x - r < o.Max.X && z + r > o.Min.Z && z - r < o.Max.Z))
                continue;
            if (o.Min.Y >= feet + 0.3f && o.Min.Y < low)
            {
                low = o.Min.Y;
                box = j;
            }
        }
        if (m.TerrainCollision is { HasVoids: true } t)
        {
            var c = t.CeilingAbove(x, z, feet + 0.1f);
            if (c < low)
            {
                low = c;
                box = -2;
            }
        }
        return low;
    }

    /// <summary>
    /// Adds the across spans of the side obstacles at along-position <paramref name="u"/> for a body standing at
    /// <paramref name="feet"/> (walls, railings, posts, props, terrain banks between step height and head height) that
    /// reach into the flight's treads.
    /// </summary>
    private static void StairObstacles(Map m, PhysicsWorld phys, StairFlight f, float u, float feet, List<(float, float)> spans)
    {
        var p0 = StairPoint(f, u, f.C0, 0);
        var p1 = StairPoint(f, u, f.C1, 0);
        foreach (var j in phys.CollectBoxes(MathF.Min(p0.X, p1.X) - 0.01f, MathF.Min(p0.Z, p1.Z) - 0.01f, MathF.Max(p0.X, p1.X) + 0.01f, MathF.Max(p0.Z, p1.Z) + 0.01f))
        {
            if (j >= m.Solid.Count)
                continue;
            var o = m.Solid[j];
            if (StairIgnored(m, j, o) || !(AlongLo(o, f.Axis, f.Dir) < u && AlongHi(o, f.Axis, f.Dir) > u) || o.Min.Y >= feet + World.PlayerHeight || o.Max.Y <= feet + World.StepHeight + 0.01f)
                continue;
            float c0 = MathF.Max(AcrossLo(o, f.Axis), f.C0), c1 = MathF.Min(AcrossHi(o, f.Axis), f.C1);
            if (c1 > c0)
                spans.Add((c0, c1));
        }
        if (m.TerrainCollision is { } t)
            for (var c = f.C0 + 0.025f; c < f.C1; c += 0.05f)
            {
                var p = StairPoint(f, u, c, 0);
                if (t.GroundBelow(p.X, p.Z, feet + World.PlayerHeight) > feet + World.StepHeight + 0.01f)
                    spans.Add((c - 0.025f, c + 0.025f));
            }
    }

    /// <summary>The widest clear span across the treads, between the obstacles found.</summary>
    private static (float Lo, float Hi) StairWidestGap(StairFlight f, List<(float, float)> spans)
    {
        spans.Sort();
        float bestLo = f.C0, bestHi = f.C0, cursor = f.C0;
        foreach (var (a, b) in spans)
        {
            if (a > cursor && a - cursor > bestHi - bestLo)
                (bestLo, bestHi) = (cursor, a);
            cursor = MathF.Max(cursor, b);
        }
        if (f.C1 - cursor > bestHi - bestLo)
            (bestLo, bestHi) = (cursor, f.C1);
        return (bestLo, bestHi);
    }

    /// <summary>
    /// Where a body gets onto a flight at its foot: straight on (standing in front of the first tread, on a floor a step
    /// below it) or from the side (beside the first tread, coming along a wall the flight starts close to, with room
    /// to come from). Null if neither.
    /// </summary>
    private static (Vector3 At, bool Sideways)? StairFootEntry(Map m, PhysicsWorld phys, StairFlight f, float c)
    {
        var first = f.Boxes[0];
        float top = first.Max.Y, lowest = top - World.StepHeight - 0.005f;
        foreach (var u in (ReadOnlySpan<float>)[f.U0 - 0.5f, f.U0 - World.PlayerRadius - 0.03f])
        {
            var p = StairPoint(f, u, c, 0);
            var floor = SupportAt(m, phys, p.X, p.Z, top - StepMinRise + 0.01f, lowest);
            if (float.IsFinite(floor) && !phys.Blocked(p with { Y = floor + 0.002f }, World.PlayerHeight))
                return (p with { Y = floor + 0.002f }, false);
        }
        foreach (var side in (ReadOnlySpan<int>)[-1, 1])
        {
            var edge = side < 0 ? f.C0 : f.C1;
            // Beside the first treads, on a floor a step below them or level with the first (a plinth at its side), where
            // stepping on sideways climbs one tread at most (a body that would reach two higher ones at once can't), with a
            // clear way across the treads to the lane.
            for (var u = f.U0 - World.PlayerRadius + 0.02f; u <= f.U0 + 1.0f; u += 0.02f)
            {
                var p = StairPoint(f, u, edge + side * (World.PlayerRadius + 0.02f), 0);
                var floor = SupportAt(m, phys, p.X, p.Z, top + 0.005f, lowest, 0.15f); // firm footing under the middle
                if (!float.IsFinite(floor))
                    continue;
                int higher = 0;
                var reach = 0f;
                foreach (var b in f.Boxes)
                    if (AlongLo(b, f.Axis, f.Dir) < u + World.PlayerRadius - 0.005f && AlongHi(b, f.Axis, f.Dir) > u - World.PlayerRadius + 0.005f && b.Max.Y > floor + 0.003f)
                    {
                        higher++;
                        reach = MathF.Max(reach, b.Max.Y - floor);
                    }
                if (higher is < 1 or > 1 || reach > World.StepHeight)
                    continue;
                if (StairRoomAlong(m, phys, f, u, edge, side, floor, World.PlayerRadius + 0.02f) && StairAcrossClear(m, phys, f, u, edge, c))
                    return (p with { Y = floor + 0.002f }, true);
            }
        }
        return null;
    }

    private static void CheckStairFlight(Map m, PhysicsWorld phys, StairFlight f, string label, List<string> problems, HashSet<ulong> seen, ref int walked, ref float worst)
    {
        var name = $"{label}: {f.Where}";
        StairDump(m, phys, f); // TEMP
        var issues = new List<string>();
        // Width: the widest clear span over the treads (every tread's side obstacles at its middle, standing on it)...
        var spans = new List<(float, float)>();
        foreach (var b in f.Boxes)
        {
            StairObstacles(m, phys, f, (AlongLo(b, f.Axis, f.Dir) + AlongHi(b, f.Axis, f.Dir)) / 2, b.Max.Y, spans);
        }
        // ... and just off both ends, where a body steps off the flight (a catwalk's railings, a narrower doorway).
        var topFree = !phys.Blocked(StairPoint(f, f.U1 + World.PlayerRadius + 0.05f, (f.C0 + f.C1) / 2, f.Y1 + 0.002f), World.PlayerHeight);
        var footFree = !phys.Blocked(StairPoint(f, f.U0 - World.PlayerRadius - 0.05f, (f.C0 + f.C1) / 2, f.Y0 + 0.002f), World.PlayerHeight);
        if (topFree)
            StairObstacles(m, phys, f, f.U1 + World.PlayerRadius + 0.05f, f.Y1, spans);
        if (footFree)
            StairObstacles(m, phys, f, f.U0 - World.PlayerRadius - 0.05f, f.Y0, spans);
        var (lo, hi) = StairWidestGap(f, spans);
        var width = MathF.Max(0, hi - lo);
        if (width < StairWidthMin - 0.005f)
            issues.Add($"only {width * 100:0} cm wide between its sides (need {StairWidthMin * 100:0})");
        var centre = width > 0 ? (lo + hi) / 2 : (f.C0 + f.C1) / 2;

        // The foot: a floor a step below the first tread, straight in front of it or beside it.
        var firstTop = f.Boxes[0].Max.Y;
        var entry = StairFootEntry(m, phys, f, centre);
        if (entry is { } e)
            f.Y0 = e.At.Y - 0.002f;
        else
        {
            var below = StairPoint(f, f.U0 - 0.5f, centre, 0);
            var floor = SupportAt(m, phys, below.X, below.Z, firstTop - StepMinRise, firstTop - 30f);
            issues.Add(float.IsFinite(floor) && firstTop - floor > World.StepHeight + 0.005f
                ? $"the first step is {(firstTop - floor) * 100:0} cm above the floor in front of it (the step-up is {World.StepHeight * 100:0} cm)"
                : "nowhere to stand at the foot (in front of the first step or beside it)");
        }
        // Risers, step to step.
        if (f.MaxRise > World.StepHeight + 0.005f)
            issues.Add($"a riser is {f.MaxRise * 100:0} cm (the step-up is {World.StepHeight * 100:0} cm)");
        Span<float> lanes = stackalloc float[3];
        var laneCount = 0;
        lanes[laneCount++] = centre;
        if (width >= 2 * World.PlayerRadius + 0.2f)
        {
            // Near each side (5 cm off it: walking by a wall or a railing, not grinding along it).
            lanes[laneCount++] = lo + World.PlayerRadius + 0.05f;
            lanes[laneCount++] = hi - World.PlayerRadius - 0.05f;
        }

        // Headroom: a standing body along each lane, from just before the foot to just past the top, feet on the highest
        // tread under it (stepping up at most a step at a time).
        var least = float.PositiveInfinity;
        var leastAt = "";
        for (var li = 0; li < laneCount; li++)
        {
            var c = lanes[li];
            var y = f.Y0;
            for (var u = f.U0 - 0.45f; u <= f.U1 + 0.45f; u += 0.05f)
            {
                var p = StairPoint(f, u, c, 0);
                var s = SupportAt(m, phys, p.X, p.Z, y + World.StepHeight, y - 0.6f);
                if (!float.IsNegativeInfinity(s))
                    y = MathF.Max(y, s);
                var ceiling = CeilingOver(m, phys, p.X, p.Z, y, out var box);
                if (ceiling - y < least)
                {
                    least = ceiling - y;
                    var what = box == -2 ? "the cave's ceiling" : box >= 0 ? StairBoxName(m, box) : "nothing";
                    leastAt = $"{what} {ceiling - y:0.00} m over the feet at ({p.X:0.0}, {y:0.0}, {p.Z:0.0})";
                }
            }
        }
        worst = MathF.Min(worst, least);
        if (least < BuildingGen.StairHeadroom - 0.005f)
            issues.Add($"headroom {least * 100:0} cm (need {BuildingGen.StairHeadroom * 100:0}): {leastAt}");

        // Landings: clear depth beyond the top and the foot, or room to turn off sideways.
        CheckStairLanding(m, phys, f, centre, true, issues);
        if (entry is { Sideways: false })
            CheckStairLanding(m, phys, f, centre, false, issues);

        // Walks with the real movers, once per distinct flight (same steps, same surroundings).
        if (seen.Add(StairSignature(m, phys, f)))
        {
            walked++;
            foreach (var mover in (ReadOnlySpan<IMover>)[ClassicMover.Instance, SweptMover.Instance])
            {
                World.Mover = mover;
                for (var li = 0; li < laneCount; li++)
                {
                    var why = StairWalk(m, phys, f, lanes[li]);
                    if (why != null)
                    {
                        issues.Add($"{(mover is SweptMover ? "swept" : "classic")} mover, {(li == 0 ? "middle" : "side")} lane: {why}");
                        break;
                    }
                }
            }
        }
        foreach (var i in issues.Distinct())
            problems.Add($"{name}: {i}");
    }

    /// <summary>What a box is, for a problem report: "a building's shell", "a prop", "a map box", with its extent.</summary>
    private static string StairBoxName(Map m, int i)
    {
        var b = m.Solid[i];
        var kind = "a map box";
        foreach (var pb in m.PlacedBuildings)
            if (i >= pb.ShellStart && i < pb.ShellStart + pb.ShellCount)
                kind = $"{pb.Def.Name}'s shell box";
        return $"{kind} ({b.Min.X:0.00}..{b.Max.X:0.00}, {b.Min.Y:0.00}..{b.Max.Y:0.00}, {b.Min.Z:0.00}..{b.Max.Z:0.00})";
    }

    private static void CheckStairLanding(Map m, PhysicsWorld phys, StairFlight f, float c, bool top, List<string> issues)
    {
        var y = top ? f.Y1 : f.Y0;
        var s = top ? 1 : -1;
        var edge = top ? f.U1 : f.U0;
        // Depth: the nearest obstacle in the body's band straight on.
        var depth = 3f;
        var p0 = StairPoint(f, edge, c - World.PlayerRadius, 0);
        var p1 = StairPoint(f, edge + s * 3f, c + World.PlayerRadius, 0);
        foreach (var j in phys.CollectBoxes(MathF.Min(p0.X, p1.X), MathF.Min(p0.Z, p1.Z), MathF.Max(p0.X, p1.X), MathF.Max(p0.Z, p1.Z)))
        {
            if (j >= m.Solid.Count)
                continue;
            var o = m.Solid[j];
            if (StairIgnored(m, j, o) || o.Min.Y >= y + World.PlayerHeight || o.Max.Y <= y + World.StepHeight + 0.01f
                || AcrossHi(o, f.Axis) <= c - World.PlayerRadius || AcrossLo(o, f.Axis) >= c + World.PlayerRadius)
                continue;
            var d = top ? AlongLo(o, f.Axis, f.Dir) - edge : edge - AlongHi(o, f.Axis, f.Dir);
            if (d >= -0.01f)
                depth = MathF.Min(depth, MathF.Max(0, d));
        }
        // No floor ahead (a drop): the landing ends there too.
        for (var k = 0.45f; k < depth; k += 0.15f)
        {
            var p = StairPoint(f, edge + s * k, c, 0);
            if (!float.IsFinite(SupportAt(m, phys, p.X, p.Z, y + World.StepHeight + k * 0.9f, y - 0.3f))) // (ground rising at a walkable slope is fine)
            {
                depth = k - 0.45f + World.PlayerRadius;
                break;
            }
        }
        if (depth >= StairLandingMin - 0.005f)
            return;
        // Room to turn: a standing body just off the flight (at the top it may still overlap the last tread, the same
        // height; at the foot it must stand clear of the first) fits up to 1 m to one side, on the floor.
        var stand = top ? Math.Clamp(depth - World.PlayerRadius - 0.01f, -0.3f, 0.45f) : World.PlayerRadius + 0.01f;
        if (top || depth >= 2 * World.PlayerRadius + 0.02f)
            foreach (var side in (ReadOnlySpan<int>)[-1, 1])
                if (StairRoomAlong(m, phys, f, edge + s * stand, c, side, y, 0f))
                    return;
        issues.Add($"the {(top ? "top" : "foot")} landing is {depth * 100:0} cm deep (need {StairLandingMin * 100:0}) with no room to turn off it");
    }

    /// <summary>Does a standing body fit (on the floor at <paramref name="y"/>, or ground rising at a walkable slope) all the way from across <paramref name="c0"/> + <paramref name="from"/> to a body width further out to <paramref name="side"/>, at along <paramref name="u"/>?</summary>
    private static bool StairRoomAlong(Map m, PhysicsWorld phys, StairFlight f, float u, float c0, int side, float y, float from)
    {
        for (var k = from; k <= from + 2 * World.PlayerRadius + 0.001f; k += 0.2f)
        {
            var p = StairPoint(f, u, c0 + side * k, 0);
            var floor = SupportAt(m, phys, p.X, p.Z, y + World.StepHeight + k * 0.9f, y - 0.3f);
            if (!float.IsFinite(floor) || phys.Blocked(p with { Y = floor + 0.002f }, World.PlayerHeight))
                return false;
        }
        return true;
    }

    /// <summary>Can a body standing on the treads under it at along <paramref name="u"/> move across from the flight's edge to the lane?</summary>
    private static bool StairAcrossClear(Map m, PhysicsWorld phys, StairFlight f, float u, float edge, float c)
    {
        var y = float.NegativeInfinity;
        foreach (var b in f.Boxes)
        {
            if (AlongLo(b, f.Axis, f.Dir) < u + World.PlayerRadius && AlongHi(b, f.Axis, f.Dir) > u - World.PlayerRadius)
                y = MathF.Max(y, b.Max.Y);
        }
        if (!float.IsFinite(y))
            return false;
        var n = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(c - edge) / 0.2f));
        for (var i = 0; i <= n; i++)
            if (phys.Blocked(StairPoint(f, u, edge + (c - edge) * i / n, y + 0.002f), World.PlayerHeight))
                return false;
        return true;
    }

    /// <summary>A hash of a flight's boxes and its surroundings in its own frame (a building placed twice, or turned, walks once).</summary>
    private static ulong StairSignature(Map m, PhysicsWorld phys, StairFlight f)
    {
        var p0 = StairPoint(f, f.U0 - 1.5f, f.C0 - 1.5f, 0);
        var p1 = StairPoint(f, f.U1 + 1.5f, f.C1 + 1.5f, 0);
        var items = new List<(int, int, int, int, int, int)>();
        var mirrored = new List<(int, int, int, int, int, int)>();
        static int R(float v) => (int)MathF.Round(v * 50f);
        foreach (var j in phys.CollectBoxes(MathF.Min(p0.X, p1.X), MathF.Min(p0.Z, p1.Z), MathF.Max(p0.X, p1.X), MathF.Max(p0.Z, p1.Z)))
        {
            if (j >= m.Solid.Count)
                continue;
            var o = m.Solid[j];
            if (StairIgnored(m, j, o) || o.Max.Y < f.Y0 - 0.5f || o.Min.Y > f.Y1 + 3f)
                continue;
            float u0 = MathF.Max(AlongLo(o, f.Axis, f.Dir), f.U0 - 1.5f) - f.U0, u1 = MathF.Min(AlongHi(o, f.Axis, f.Dir), f.U1 + 1.5f) - f.U0;
            float c0 = MathF.Max(AcrossLo(o, f.Axis), f.C0 - 1.5f), c1 = MathF.Min(AcrossHi(o, f.Axis), f.C1 + 1.5f);
            float y0 = MathF.Max(o.Min.Y, f.Y0 - 0.5f) - f.Y0, y1 = MathF.Min(o.Max.Y, f.Y1 + 3f) - f.Y0;
            items.Add((R(u0), R(u1), R(c0 - f.C0), R(c1 - f.C0), R(y0), R(y1)));
            mirrored.Add((R(u0), R(u1), R(f.C1 - c1), R(f.C1 - c0), R(y0), R(y1)));
        }
        if (m.TerrainCollision != null)
            items.Add((R(f.U0 + f.Dir * 0.001f), f.Axis, f.Dir, R(f.C0), R(f.Y0), 1)); // terrain maps: placement matters
        return Math.Min(StairHash(items), m.TerrainCollision != null ? ulong.MaxValue : StairHash(mirrored)) ^ (ulong)f.Treads.Count;
    }

    private static ulong StairHash(List<(int, int, int, int, int, int)> items)
    {
        items.Sort();
        var h = 14695981039346656037ul;
        foreach (var (a, b, c, d, e, g) in items)
            foreach (var v in (ReadOnlySpan<int>)[a, b, c, d, e, g])
            {
                h ^= (uint)v;
                h *= 1099511628211ul;
            }
        return h;
    }

    /// <summary>
    /// Walks a flight with the current mover, standing, plain walk input: from just before the foot up onto the last tread,
    /// then back down past the foot. Null if it got there both ways without being stopped; otherwise why not.
    /// </summary>
    private static string? StairWalk(Map m, PhysicsWorld phys, StairFlight f, float c)
    {
        if (StairFootEntry(m, phys, f, c) is not { } entry)
            return "nowhere to stand at the foot";
        var dir = f.Axis == 0 ? new Vector3(f.Dir, 0, 0) : new Vector3(0, 0, f.Dir);
        var upYaw = MathF.Atan2(dir.X, -dir.Z);
        var (start, sideways) = entry;
        var s = new MoveState { Position = start, Grounded = true, Stance = Stance.Stand };
        var length = f.U1 - f.U0;
        var ticks = (int)((length + 1.5f) / World.WalkSpeed * World.TickRate * 2) + 90;
        if (sideways)
        {
            // Onto the first tread from the side, then up the lane.
            var across = f.Axis == 0 ? start.Z : start.X;
            var sgn = MathF.Sign(c - across);
            var side = f.Axis == 0 ? new Vector3(0, 0, sgn) : new Vector3(sgn, 0, 0);
            var sideYaw = MathF.Atan2(side.X, -side.Z);
            var reached = false;
            for (var t = 0; t < 120 && !reached; t++)
            {
                World.Simulate(ref s, new InputCmd { Buttons = Buttons.Forward, Yaw = sideYaw, SpeedPct = 100 });
                var a = f.Axis == 0 ? s.Position.Z : s.Position.X;
                reached = (c - a) * sgn <= 0.35f; // (it carries on a bit: the lane steering takes over)
            }
            if (!reached)
                return "coming onto the first step from the side: " + StairStuckWhy(m, phys, f, s, true, c);
        }
        // Up: until the body stands on the last tread; then back down past the foot.
        var why = StairLeg(m, phys, f, ref s, upYaw, ticks, true, c);
        if (why != null)
            return "going up: " + why;
        why = StairLeg(m, phys, f, ref s, upYaw + MathF.PI, ticks, false, c);
        return why == null ? null : "going down: " + why;
    }

    private static string? StairLeg(Map m, PhysicsWorld phys, StairFlight f, ref MoveState s, float yaw, int ticks, bool up, float c)
    {
        var stall = 0;
        var lastU = Along(f, s.Position);
        var lastLo = AlongLo(f.Boxes[^1], f.Axis, f.Dir); // up: done once standing on the last tread
        var firstTop = f.Boxes[0].Max.Y; // down: done once on the first tread or below it, at the foot
        var right = World.FlatRight(yaw);
        var rightAcross = f.Axis == 0 ? right.Z : right.X; // + if "right" points towards +across
        for (var t = 0; t < ticks; t++)
        {
            // Walk the lane like a player does: forward, strafing back onto it when the body drifts off it.
            var off = c - (f.Axis == 0 ? s.Position.Z : s.Position.X);
            var steer = MathF.Abs(off) < 0.04f ? Buttons.None : off * rightAcross > 0 ? Buttons.Right : Buttons.Left;
            World.Simulate(ref s, new InputCmd { Buttons = Buttons.Forward | steer, Yaw = yaw, SpeedPct = 100 });
            var u = Along(f, s.Position);
            if (s.Stance != Stance.Stand)
                return $"had to leave the standing stance at ({s.Position.X:0.0}, {s.Position.Y:0.0}, {s.Position.Z:0.0})";
            if (up ? u >= lastLo - 0.3f && s.Position.Y >= f.Y1 - 0.05f : u <= f.U0 + 0.1f && s.Position.Y <= firstTop + 0.05f)
                return null;
            stall = MathF.Abs(u - lastU) < 0.004f ? stall + 1 : 0; // under 0.25 m/s
            lastU = u;
            if (stall > 20)
                break;
        }
        return StairStuckWhy(m, phys, f, s, up, c);
    }

    private static float Along(StairFlight f, Vector3 p) => (f.Axis == 0 ? p.X : p.Z) * f.Dir;

    /// <summary>Why a walk stopped: a riser too tall, the head against something (with the clear height), or a side snag.</summary>
    private static string StairStuckWhy(Map m, PhysicsWorld phys, StairFlight f, in MoveState s, bool up, float c)
    {
        var p = s.Position;
        var u = Along(f, p);
        var at = $"stopped at ({p.X:0.00}, {p.Y:0.00}, {p.Z:0.00})";
        if (up)
        {
            // The next tread the body hasn't climbed yet.
            foreach (var b in f.Boxes)
            {
                if (b.Max.Y <= p.Y + 0.02f || AlongLo(b, f.Axis, f.Dir) < u - World.PlayerRadius - 0.05f)
                    continue;
                var rise = b.Max.Y - p.Y;
                if (rise > World.StepHeight + 0.001f)
                    return $"{at}: the next step is {rise * 100:0} cm up (the step-up is {World.StepHeight * 100:0} cm)";
                var probe = StairPoint(f, u + 0.1f, c, b.Max.Y);
                if (phys.Blocked(probe, World.PlayerHeight) && !phys.Blocked(probe, World.HeightOf(Stance.Crouch)))
                {
                    var ceiling = CeilingOver(m, phys, probe.X, probe.Z, b.Max.Y, out var box);
                    return $"{at}: the head hits {(box >= 0 ? StairBoxName(m, box) : "the ceiling")} ({(ceiling - b.Max.Y) * 100:0} cm clear over the next step; crouching would pass)";
                }
                break;
            }
        }
        else
        {
            var ceiling = CeilingOver(m, phys, p.X, p.Z, p.Y, out var box);
            if (ceiling - p.Y < World.PlayerHeight + 0.05f)
                return $"{at}: the head is against {(box >= 0 ? StairBoxName(m, box) : "the ceiling")} ({(ceiling - p.Y) * 100:0} cm clear)";
        }
        // Something in the way: the first box the body would overlap a little further on (or the next step's top).
        var ahead = StairPoint(f, u + (up ? 0.1f : -0.1f), c, 0);
        foreach (var y in (ReadOnlySpan<float>)[p.Y + 0.02f, p.Y + World.StepHeight])
            foreach (var j in phys.CollectBoxes(ahead.X - World.PlayerRadius, ahead.Z - World.PlayerRadius, ahead.X + World.PlayerRadius, ahead.Z + World.PlayerRadius))
            {
                if (j >= m.Solid.Count)
                    continue;
                var o = m.Solid[j];
                if (StairIgnored(m, j, o) || !(ahead.X + World.PlayerRadius > o.Min.X && ahead.X - World.PlayerRadius < o.Max.X && ahead.Z + World.PlayerRadius > o.Min.Z && ahead.Z - World.PlayerRadius < o.Max.Z)
                    || y >= o.Max.Y - 0.001f || y + World.PlayerHeight <= o.Min.Y + 0.001f || o.Max.Y - p.Y <= World.StepHeight && y == p.Y + 0.02f)
                    continue;
                return $"{at}: snagged on {StairBoxName(m, j)}";
            }
        return $"{at}: snagged (on a side, a railing or something on the steps)";
    }
}
