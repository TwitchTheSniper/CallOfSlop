using System.Diagnostics;
using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Map shapes": map boundaries that aren't rectangles (MapBoundary.cs, MapEdges.cs). Point-in-shape for
/// every shape (concave beans and noisy outlines included), outline sampling, wall pieces following the outline, the
/// barrier against rays, both movers, grenades and debris all round the edge, everything generated staying inside, the
/// nav graph, fingerprints of maps without a boundary, a bot match on "Shape Test" and costs. The circle, polygon,
/// noise, hedge and props cases are maps built here from JSON text (not files).
/// </summary>
public static partial class SelfTest
{
    private const string ShapeTestMap = "Shape Test";

    /// <summary>
    /// Fingerprints of the shipped maps (by file name) as origin/main computed them before boundaries existed (taken with
    /// --check-map on 02717b3). A map whose file is later edited on purpose can be dropped from this list.
    /// </summary>
    private static readonly (string File, uint Hash)[] ShapeBaseline =
    [
        ("01-ProtoBox", 0xa83dd7a5u), ("02-Courtyard", 0x9b62bfadu), ("03-Crossroads", 0x04fab23au), ("04-Warehouse9", 0x433369d1u),
        ("05-LongShot", 0x9a88c69cu), ("06-Ziggurat", 0xd8b982f8u), ("07-Scrapyard", 0xdece740cu), ("08-Rooftops", 0x3601e902u),
        ("09-Harbor", 0x56f96d12u), ("10-MarketStreet", 0xb8c3562au), ("11-Quarry", 0xc0f1d401u), ("12-Trenches", 0xb1bd94b3u),
        ("13-TrainYard", 0xea4ba752u), ("14-OfficePark", 0xbf692394u), ("15-Ruins", 0xe93ec317u), ("16-Overpass", 0x5b03f45fu),
        ("17-Farmland", 0xfcc5a090u), ("18-Downtown", 0x2a9960f5u), ("19-Canyon", 0x03353c42u), ("20-Airfield", 0xf47d65a6u),
        ("21-Village", 0x8004499bu), ("22-Industrial", 0x87c760edu), ("23-Highlands", 0xe932e450u), ("24-MilitaryBase", 0xe6122c44u),
        ("01-Blueprint", 0xde9e3279u), ("02-Galleria", 0x429afd78u), ("03-Floodgate", 0xcb35e9deu), ("04-Trefoil", 0x39b08239u),
        ("05-Citadel", 0x340e65a1u), ("06-Confluence", 0x3aad3033u), ("FoliageTest", 0x8a733893u), ("MeshTest", 0xb7a01a63u),
        ("PhysicsTest", 0x476984bcu), ("PropGallery", 0x3e664c24u), ("PropYard", 0x59b1babcu), ("TerrainTest", 0x6ad80326u),
        ("01-GravelPit", 0x09d3bf6bu), ("02-Hillfort", 0x29d6cdf4u), ("03-Riverbend", 0x033958c0u), ("04-AdobeMesa", 0xe4bb26c5u),
        ("05-Ridgeline", 0x35d68126u), ("06-AlpinePass", 0x99dc0b75u),
    ];

    private static readonly List<string> ShapeNotes = new();

    /// <summary>A result line, also collected for the section's GitHub notice.</summary>
    private static void ShapeLine(string text)
    {
        Line(text);
        ShapeNotes.Add(text);
    }

    private static readonly string[] ShapeMapJson =
    [
        """
        { "name": "Shape Circle", "maxPlayers": 12,
          "boundary": { "shape": "circle", "center": [4, -2], "radius": 30, "edge": { "style": "fence" } },
          "ops": [
            { "op": "Ground", "color": [0.4, 0.42, 0.36] },
            { "op": "Prop", "x": 4, "z": -2, "w": 3, "h": 1.2, "d": 3, "color": [0.9, 0.5, 0.2] },
            { "op": "Prop", "x": 16, "z": 8, "w": 1, "h": 1.2, "d": 6, "color": [0.6, 0.6, 0.6] },
            { "op": "Prop", "x": -10, "z": -12, "w": 6, "h": 1.2, "d": 1, "color": [0.6, 0.6, 0.6] },
            { "op": "RingSpawns", "count": 6, "frac": 0.7 },
            { "op": "AutoWaypoints", "spacing": 6 },
            { "op": "Clutter", "props": ["Debris"], "x0": -40, "z0": -40, "x1": 40, "z1": 40, "count": 40, "seed": 2 }
          ],
          "foliage": { "regions": [ { "type": "grass", "density": 1.5, "rect": [-40, -40, 40, 40] }, { "type": "tree", "density": 0.02, "rect": [-40, -40, 40, 40] } ] } }
        """,
        """
        { "name": "Shape Polygon",
          "boundary": { "shape": "polygon", "points": [[-30, -25], [30, -25], [30, 25], [10, 25], [10, -5], [-10, -5], [-10, 25], [-30, 25]], "edge": { "style": "rocks", "seed": 3 } },
          "ops": [
            { "op": "Ground", "color": [0.45, 0.42, 0.38] },
            { "op": "Prop", "x": 0, "z": -15, "w": 4, "h": 1.2, "d": 2, "color": [0.9, 0.5, 0.2] },
            { "op": "Prop", "x": -20, "z": 10, "w": 2, "h": 1.2, "d": 4, "color": [0.9, 0.5, 0.2] },
            { "op": "RingSpawns", "count": 8, "frac": 0.75 },
            { "op": "AutoWaypoints", "spacing": 5 },
            { "op": "Clutter", "props": ["Debris"], "x0": -30, "z0": -25, "x1": 30, "z1": 25, "count": 30, "seed": 5 }
          ],
          "foliage": { "regions": [ { "type": "bush", "density": 0.3, "rect": [-35, -30, 35, 30] } ] } }
        """,
        """
        { "name": "Shape Noise",
          "boundary": { "shape": "ellipse", "radiusX": 45, "radiusZ": 30, "rotation": 15,
                        "noise": { "amplitude": 3, "scale": 14, "seed": 5 }, "edge": { "style": "cliff", "height": 8, "width": 5 } },
          "terrain": { "cell": 1, "margin": 16, "features": [ { "type": "noise", "amplitude": 0.6, "scale": 20, "seed": 2 } ] },
          "ops": [
            { "op": "Prop", "x": 0, "z": 0, "w": 3, "h": 1.2, "d": 3, "color": [0.9, 0.5, 0.2] },
            { "op": "RingSpawns", "count": 8, "frac": 0.75 },
            { "op": "AutoWaypoints", "spacing": 7 },
            { "op": "Clutter", "props": ["Debris"], "x0": -50, "z0": -40, "x1": 50, "z1": 40, "count": 30, "seed": 6 }
          ],
          "foliage": { "regions": [ { "type": "grass", "density": 1, "rect": [-60, -45, 60, 45] } ] } }
        """,
        """
        { "name": "Shape Hedge", "halfX": 28, "halfZ": 20,
          "boundary": { "shape": "rect", "round": 8, "edge": { "style": "hedge" } },
          "ops": [
            { "op": "Ground", "color": [0.4, 0.45, 0.35] },
            { "op": "RingSpawns", "count": 8, "frac": 0.75 },
            { "op": "AutoWaypoints", "spacing": 6 }
          ] }
        """,
        """
        { "name": "Shape Props",
          "boundary": { "shape": "smooth", "points": [[-26, -4], [-14, -20], [10, -18], [26, -2], [16, 16], [-12, 18]],
                        "noise": { "amplitude": 1.5, "scale": 9, "seed": 8, "octaves": 3 }, "edge": { "style": "props", "props": ["Road Barrier", "Sandbag Wall"], "spacing": 3.5 } },
          "ops": [
            { "op": "Ground", "color": [0.4, 0.4, 0.42] },
            { "op": "RingSpawns", "count": 8, "frac": 0.7 },
            { "op": "AutoWaypoints", "spacing": 6 }
          ] }
        """,
    ];

    private static void CheckMapShapes()
    {
        ShapeNotes.Clear();
        try
        {
            CheckMapShapesBody();
        }
        finally
        {
            // One GitHub notice with the section's results (the workflow prints selftest.log; see SelfTestSlopes.cs).
            Line("::notice title=Map shapes::" + string.Join("%0A", ShapeNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
        }
    }

    private static void CheckMapShapesBody()
    {
        ShapeContains();
        ShapeOutlines();
        var file = Maps.Find(ShapeTestMap);
        if (file == null || file.SourceFile == null || file.Boundary == null)
        {
            Fail($"Maps/Dev/ShapeTest.json ('{ShapeTestMap}') didn't load with a boundary");
            return;
        }
        var maps = new List<Map> { file };
        foreach (var json in ShapeMapJson)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var m = MapFile.Load(json, "shape test map");
                m.Validate();
                maps.Add(m);
                ShapeLine($"{m.Name}: loaded in {sw.Elapsed.TotalMilliseconds:0} ms (outline {m.Boundary!.BuildMs:0.0} ms)");
                if (sw.Elapsed.TotalMilliseconds > 3000)
                    Fail($"{m.Name}: took {sw.Elapsed.TotalMilliseconds:0} ms to load");
            }
            catch (Exception ex)
            {
                Fail($"a code shape map failed to load: {ex.Message}");
            }
        }
        var previousMap = World.Map;
        var previousMode = World.PhysicsMode;
        try
        {
            foreach (var m in maps)
            {
                ShapeEdgePieces(m);
                ShapeContents(m);
                World.SetMap(m);
                ShapeBarrier(m);
                ShapeNav(m);
            }
        }
        finally
        {
            World.PhysicsMode = previousMode;
            World.SetMap(previousMap);
        }
        ShapeFingerprints(file);
        ShapeErrors();
        ShapeCosts(file);
        ShapeBotMatch(file);
    }

    // ---- Point in shape ---------------------------------------------------------------------------------------

    /// <summary>An independent winding-number test on a polygon (for checking the fast path).</summary>
    private static bool WindingInside(IReadOnlyList<Vector2> pts, Vector2 p)
    {
        var wn = 0;
        for (var i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            var cross = (b.X - a.X) * (p.Y - a.Y) - (p.X - a.X) * (b.Y - a.Y);
            if (a.Y <= p.Y)
            {
                if (b.Y > p.Y && cross > 0)
                    wn++;
            }
            else if (b.Y <= p.Y && cross < 0)
                wn--;
        }
        return wn != 0;
    }

    private static float DistToPolyline(IReadOnlyList<Vector2> pts, Vector2 p)
    {
        var best = float.MaxValue;
        for (var i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var e = pts[(i + 1) % pts.Count] - a;
            var t = Math.Clamp(Vector2.Dot(p - a, e) / e.LengthSquared(), 0f, 1f);
            best = MathF.Min(best, Vector2.Distance(p, a + e * t));
        }
        return best;
    }

    private static void ShapeContains()
    {
        var rng = new Random(41);
        var bean = new BoundarySpec { Shape = BoundaryShape.Smooth, Points = [new(-38, 0), new(-32, -15), new(-14, -22), new(8, -22), new(28, -16), new(40, -2), new(34, 13), new(18, 18), new(4, 11), new(-10, 18), new(-28, 16)] };
        var cases = new (string Name, BoundarySpec Spec, Func<Vector2, float>? Exact)[]
        {
            ("circle", new BoundarySpec { Shape = BoundaryShape.Circle, Center = new(5, -3), Radius = 30 }, p => Vector2.Distance(p, new(5, -3)) - 30),
            ("ellipse", new BoundarySpec { Shape = BoundaryShape.Ellipse, Center = new(-2, 4), RadiusX = 40, RadiusZ = 22, Rotation = 30 }, p =>
            {
                var d = p - new Vector2(-2, 4);
                var a = 30 * MathF.PI / 180;
                // Inverse of the spec's turn (local x -> (cos, -sin)).
                float lx = d.X * MathF.Cos(a) - d.Y * MathF.Sin(a), lz = d.X * MathF.Sin(a) + d.Y * MathF.Cos(a);
                var k = MathF.Sqrt(lx * lx / (40f * 40f) + lz * lz / (22f * 22f));
                return (k - 1) * 22f; // sign only (scaled so the "near the edge" band is generous)
            }),
            ("rounded rect", new BoundarySpec { Shape = BoundaryShape.Rect, HalfX = 30, HalfZ = 18, Round = 6 }, p =>
            {
                var q = new Vector2(MathF.Abs(p.X) - 24, MathF.Abs(p.Y) - 12);
                return Vector2.Max(q, Vector2.Zero).Length() + MathF.Min(MathF.Max(q.X, q.Y), 0) - 6;
            }),
            ("concave polygon", new BoundarySpec { Shape = BoundaryShape.Polygon, Points = [new(-30, -25), new(30, -25), new(30, 25), new(10, 25), new(10, -5), new(-10, -5), new(-10, 25), new(-30, 25)] }, null),
            ("bean (smooth, concave)", bean, null),
            ("noisy circle", new BoundarySpec { Shape = BoundaryShape.Circle, Radius = 35, Noise = new BoundaryNoise { Amplitude = 4, Scale = 10, Seed = 9, Octaves = 3 } }, null),
            ("noisy bean", new BoundarySpec { Shape = BoundaryShape.Smooth, Points = bean.Points, Noise = new BoundaryNoise { Amplitude = 2, Scale = 8, Seed = 4 } }, null),
        };
        foreach (var (name, spec, exact) in cases)
        {
            MapBoundary b;
            try
            {
                b = new MapBoundary(spec, 40, 40);
            }
            catch (Exception ex)
            {
                Fail($"shape {name}: {ex.Message}");
                continue;
            }
            int wrong = 0, wrongExact = 0, wrongInside = 0, tested = 0;
            for (var i = 0; i < 20000; i++)
            {
                var p = new Vector2(Rand(b.Min.X - 8, b.Max.X + 8), Rand(b.Min.Y - 8, b.Max.Y + 8));
                var near = DistToPolyline(b.Outline, p);
                if (near < 1e-3f)
                    continue;
                tested++;
                var inside = b.Contains(p.X, p.Y);
                if (inside != WindingInside(b.Outline, p))
                    wrong++;
                if (exact != null && MathF.Abs(exact(p)) > spec.Tolerance * 2 + 0.02f && inside != exact(p) < 0)
                    wrongExact++;
                var margin = Rand(0, 3);
                if (b.Inside(p.X, p.Y, margin) != (inside && near >= margin) && MathF.Abs(near - margin) > 1e-3f)
                    wrongInside++;
                if (MathF.Abs(MathF.Abs(b.SignedDistance(p.X, p.Y)) - near) > 1e-3f || (b.SignedDistance(p.X, p.Y) < 0) != inside)
                    wrongInside++;
            }
            // The polygon and smooth cases: the defining points must be on the right side.
            var extra = "";
            if (spec.Shape == BoundaryShape.Polygon)
            {
                if (b.Contains(0, 10) || !b.Contains(-20, 10) || !b.Contains(20, 10) || !b.Contains(0, -15))
                    Fail($"shape {name}: the notch of the U should be outside and its arms inside");
                extra = ", notch outside";
            }
            if (spec.Shape == BoundaryShape.Smooth && spec.Noise == null)
            {
                if (b.Contains(4, 16) || !b.Contains(4, 4) || !b.Contains(-20, 6) || !b.Contains(20, 6))
                    Fail($"shape {name}: the bean's dent at (4, 16) should be outside and its lobes inside");
                extra = ", dent outside";
            }
            if (spec.Noise is { } nz && spec.Shape == BoundaryShape.Circle)
            {
                var r = b.Outline.Select(p => Vector2.Distance(p, spec.Center)).ToArray();
                if (r.Max() > spec.Radius + nz.Amplitude + 0.1f || r.Min() < spec.Radius - nz.Amplitude - 0.1f || r.Max() - r.Min() < nz.Amplitude * 0.8f)
                    Fail($"shape {name}: radius {r.Min():0.0}-{r.Max():0.0} should wobble within {spec.Radius} +- {nz.Amplitude}");
                extra = $", radius {r.Min():0.0}-{r.Max():0.0}";
            }
            ShapeLine($"shape {name}: {b.SegmentCount} segments, {b.Area:0} m2, {tested} points: {wrong} differ from winding, {wrongExact} from the exact shape, "
                + $"{wrongInside} margin/distance mismatches{extra}");
            if (wrong > 0 || wrongExact > 0 || wrongInside > 0)
                Fail($"shape {name}: point-in-shape wrong ({wrong} vs winding, {wrongExact} vs exact, {wrongInside} margin/distance)");
        }

        float Rand(float a, float c) => a + (float)rng.NextDouble() * (c - a);
    }

    // ---- Outline sampling -----------------------------------------------------------------------------------------

    private static void ShapeOutlines()
    {
        var rng = new Random(5);
        // Circle: every vertex on the circle, every chord within tolerance, segment count near the minimum for the tolerance.
        foreach (var (r, tol) in new[] { (12f, 0.05f), (40f, 0.05f), (40f, 0.2f), (150f, 0.05f) })
        {
            var b = new MapBoundary(new BoundarySpec { Shape = BoundaryShape.Circle, Radius = r, Tolerance = tol }, 50, 50);
            var worstVertex = b.Outline.Max(p => MathF.Abs(p.Length() - r));
            var worstChord = 0f;
            for (var i = 0; i < b.SegmentCount; i++)
                worstChord = MathF.Max(worstChord, r - ((b.Outline[i] + b.Outline[(i + 1) % b.SegmentCount]) * 0.5f).Length());
            var minimum = MathF.PI / MathF.Acos(1 - tol / r);
            var needed = MathF.Max(minimum, 2 * MathF.PI * r / MapBoundary.MaxSegment);
            ShapeLine($"circle r {r} tolerance {tol}: {b.SegmentCount} segments (minimum about {minimum:0}), worst vertex {worstVertex:0.0000} m, worst chord {worstChord:0.000} m, built in {b.BuildMs:0.0} ms");
            if (worstVertex > 0.01f || worstChord > tol * 1.05f + 0.002f)
                Fail($"circle r {r}: outline strays from the circle (vertex {worstVertex:0.000}, chord {worstChord:0.000}, tolerance {tol})");
            if (b.SegmentCount > needed * 1.6f + 8)
                Fail($"circle r {r}: {b.SegmentCount} segments is far more than the ~{needed:0} needed (not adapting to the tolerance)");
        }
        // Straight edges stay few: a square only gets cut every MaxSegment metres.
        var sq = new MapBoundary(new BoundarySpec { Shape = BoundaryShape.Rect, HalfX = 40, HalfZ = 40 }, 40, 40);
        if (sq.SegmentCount != 4 * (int)MathF.Ceiling(80 / MapBoundary.MaxSegment))
            Fail($"an 80 m square has {sq.SegmentCount} segments (expected {4 * (int)MathF.Ceiling(80 / MapBoundary.MaxSegment)})");

        // Closed, counter-clockwise, not self-intersecting, normals outward; the smooth curve stays within tolerance of the dense curve.
        var specs = new[]
        {
            new BoundarySpec { Shape = BoundaryShape.Smooth, Points = [new(-38, 0), new(-32, -15), new(-14, -22), new(8, -22), new(28, -16), new(40, -2), new(34, 13), new(18, 18), new(4, 11), new(-10, 18), new(-28, 16)] },
            new BoundarySpec { Shape = BoundaryShape.Smooth, Points = [new(30, 0), new(0, 20), new(-30, 0), new(0, -20)] },
            new BoundarySpec { Shape = BoundaryShape.Polygon, Points = [new(-20, -20), new(-20, 20), new(20, 20), new(20, -20)] }, // clockwise on purpose
            new BoundarySpec { Shape = BoundaryShape.Ellipse, RadiusX = 60, RadiusZ = 25, Rotation = -40, Noise = new BoundaryNoise { Amplitude = 3, Scale = 12, Seed = 2, Octaves = 4 } },
        };
        var k = 0;
        foreach (var spec in specs)
        {
            k++;
            var b = new MapBoundary(spec, 60, 60);
            var pts = b.Outline;
            var dupes = Enumerable.Range(0, pts.Length).Count(i => Vector2.Distance(pts[i], pts[(i + 1) % pts.Length]) < 1e-3f);
            var crosses = MapBoundary.SelfIntersects(pts, out _, out _);
            var badNormals = 0;
            for (var i = 0; i < pts.Length; i++)
            {
                var mid = (pts[i] + pts[(i + 1) % pts.Length]) * 0.5f;
                if (b.Contains(mid.X + b.Normals[i].X * 0.05f, mid.Y + b.Normals[i].Y * 0.05f) || !b.Contains(mid.X - b.Normals[i].X * 0.05f, mid.Y - b.Normals[i].Y * 0.05f))
                    badNormals++;
            }
            var longest = Enumerable.Range(0, pts.Length).Max(i => Vector2.Distance(pts[i], pts[(i + 1) % pts.Length]));
            var stray = 0f;
            if (spec.Shape == BoundaryShape.Smooth)
                foreach (var p in MapBoundary.CatmullRom(spec.Points, 0.05))
                    stray = MathF.Max(stray, DistToPolyline(pts, p));
            ShapeLine($"outline {k} ({spec.Shape.ToString().ToLowerInvariant()}{(spec.Noise != null ? " + noise" : "")}): {pts.Length} segments, area {b.Area:0} m2, longest {longest:0.0} m, "
                + $"duplicates {dupes}, crosses itself {crosses}, wrong normals {badNormals}{(spec.Shape == BoundaryShape.Smooth ? $", strays {stray:0.000} m from the curve" : "")}");
            if (dupes > 0 || crosses || badNormals > 0 || b.Area <= 0 || longest > MapBoundary.MaxSegment + 1e-3f || stray > spec.Tolerance + 0.01f)
                Fail($"outline {k}: not a clean closed counter-clockwise outline within tolerance");
        }
        _ = rng;
    }

    // ---- Wall and edge pieces -------------------------------------------------------------------------------------

    private static void ShapeEdgePieces(Map m)
    {
        var b = m.Boundary!;
        var style = b.Spec.Edge.Style;
        var pieces = m.MeshPlacements.Take(m.BoundaryMeshCount).ToList();
        var line = $"{m.Name}: edge {style.ToString().ToLowerInvariant()}, {b.SegmentCount} segments, {m.BoundaryBarriers.Count} barrier pieces, {pieces.Count} edge meshes";
        if (m.BoundaryBarriers.Count != b.SegmentCount)
            Fail($"{m.Name}: {m.BoundaryBarriers.Count} barrier pieces for {b.SegmentCount} segments");
        // Every barrier piece: inner face on its segment, outside the outline.
        var worstBarrier = 0f;
        for (var i = 0; i < b.SegmentCount; i++)
        {
            var o = m.BoundaryBarriers[i];
            var inner = o.ToWorld(new Vector3(0, 0, -o.Half.Z));
            var mid = (b.Outline[i] + b.Outline[(i + 1) % b.SegmentCount]) * 0.5f;
            worstBarrier = MathF.Max(worstBarrier, Vector2.Distance(new Vector2(inner.X, inner.Z), mid));
            if (b.Contains(o.Center.X, o.Center.Z))
                Fail($"{m.Name}: barrier piece {i} is inside the playable area");
        }
        if (worstBarrier > 0.01f)
            Fail($"{m.Name}: a barrier piece's inner face is {worstBarrier:0.000} m off the outline");
        if (style is BoundaryEdgeStyle.Wall or BoundaryEdgeStyle.Hedge)
        {
            var walls = pieces.Where(p => p.Source.StartsWith("boundary wall") || p.Source.StartsWith("boundary hedge")).ToList();
            var worst = 0f;
            var worstEnd = 0f;
            foreach (var w in walls)
            {
                // The unit box's inner face (local z = +0.5 is inwards) at mid-length and at both ends, at ground level.
                var c = Vector3.Transform(new Vector3(0, 0, 0.5f), w.Transform);
                worst = MathF.Max(worst, MathF.Abs(b.SignedDistance(c.X, c.Z)));
                foreach (var end in new[] { -0.5f, 0.5f })
                {
                    var e = Vector3.Transform(new Vector3(end, 0, 0.5f), w.Transform);
                    worstEnd = MathF.Max(worstEnd, MathF.Abs(b.SignedDistance(e.X, e.Z)));
                }
                // The outer face is outside.
                var outer = Vector3.Transform(new Vector3(0, 0, -0.5f), w.Transform);
                if (b.Contains(outer.X, outer.Z))
                    Fail($"{m.Name}: a wall piece's outer face is inside the playable area");
            }
            line += $", {walls.Count} wall pieces (inner faces within {worst:0.000} m of the outline, ends within {worstEnd:0.00} m)";
            if (walls.Count != b.SegmentCount)
                Fail($"{m.Name}: {walls.Count} wall pieces for {b.SegmentCount} outline segments");
            if (worst > 0.02f || worstEnd > 0.6f)
                Fail($"{m.Name}: wall pieces don't follow the outline (mid {worst:0.000} m, ends {worstEnd:0.00} m)");
        }
        if (style is BoundaryEdgeStyle.Fence or BoundaryEdgeStyle.Rocks && pieces.Count < b.Perimeter / (b.EdgeSpacing * 1.5f))
            Fail($"{m.Name}: only {pieces.Count} {style} pieces round a {b.Perimeter:0} m outline");
        if (style == BoundaryEdgeStyle.Props)
        {
            var props = m.PropInstances.Take(m.ClutterStart).Count(p => p.Def.Name is "Road Barrier" or "Sandbag Wall");
            line += $", {props} edge props";
            if (props < b.Perimeter / (b.EdgeSpacing * 1.5f))
                Fail($"{m.Name}: only {props} props along a {b.Perimeter:0} m outline");
        }
        if (style == BoundaryEdgeStyle.Cliff && m.Terrain != null)
        {
            // Outside the outline the ground rises by the cliff height; inside it doesn't.
            var rises = 0;
            var checks = 0;
            for (var i = 0; i < b.SegmentCount; i += Math.Max(1, b.SegmentCount / 40))
            {
                var mid = (b.Outline[i] + b.Outline[(i + 1) % b.SegmentCount]) * 0.5f;
                var outside = mid + b.Normals[i] * (b.CliffWidth + 1.5f);
                var inside = mid - b.Normals[i] * 1.5f;
                checks++;
                if (m.GroundHeight(outside.X, outside.Y) - m.GroundHeight(inside.X, inside.Y) > b.EdgeHeight - 2f)
                    rises++;
            }
            line += $", cliff rises at {rises}/{checks} checks";
            if (rises < checks * 0.9f)
                Fail($"{m.Name}: the cliff edge only rose at {rises} of {checks} points round the outline");
        }
        ShapeLine(line);
    }

    // ---- Everything inside ----------------------------------------------------------------------------------------

    private static void ShapeContents(Map m)
    {
        var b = m.Boundary!;
        var r = World.PlayerRadius;
        var spawnsOut = m.Spawns.Count(s => !b.Inside(s.X, s.Z, r));
        var waypointsOut = m.Waypoints.Count(w => !b.Inside(w.X, w.Z, r));
        var generatedOut = m.Spawns.Skip(Math.Max(0, m.AuthoredSpawns)).Count(s => !b.Inside(s.X, s.Z, r));
        var clutter = m.PropInstances.Skip(m.ClutterStart).ToList();
        var clutterOut = clutter.Count(p => !(b.Inside(p.Bounds.Min.X, p.Bounds.Min.Z) && b.Inside(p.Bounds.Max.X, p.Bounds.Max.Z)
                                               && b.Inside(p.Bounds.Min.X, p.Bounds.Max.Z) && b.Inside(p.Bounds.Max.X, p.Bounds.Min.Z)));
        var plantsOut = m.FoliageInstances.Count(f => !b.Inside(f.Position.X, f.Position.Z, 0.05f));
        var objectives = m.Objectives;
        var hillsOut = objectives.Hills.Count(h => !b.Inside(h.Center.X, h.Center.Z));
        var flagsOut = objectives.Flags.Count(f => !b.Inside(f.X, f.Z));
        ShapeLine($"{m.Name}: {m.Spawns.Count} spawns ({m.GeneratedSpawns} generated), {m.Waypoints.Count} waypoints, {clutter.Count} clutter props, "
            + $"{m.FoliageInstances.Length} plants, {objectives.Hills.Count} hills; outside: {spawnsOut} spawns, {waypointsOut} waypoints, {clutterOut} clutter, {plantsOut} plants, {hillsOut} hills, {flagsOut} flags");
        if (spawnsOut + waypointsOut + generatedOut + clutterOut + plantsOut + hillsOut + flagsOut > 0)
            Fail($"{m.Name}: something generated or placed is outside the boundary");
        if (m.Spawns.Count < 2 || m.Waypoints.Count < 6)
            Fail($"{m.Name}: only {m.Spawns.Count} spawns and {m.Waypoints.Count} waypoints");
        if (m.Name == "Shape Circle" && m.GeneratedSpawns == 0)
            Fail($"{m.Name}: expected generated spawns (6 placed for 12 players)");
        if (m.Spawns.Any(s => m.IsBlocked(s)))
            Fail($"{m.Name}: a spawn is still blocked");
        // The map's size class and player limit come from the area inside the outline.
        if (MathF.Abs(m.PlayArea - b.Area) > 0.01f)
            Fail($"{m.Name}: PlayArea {m.PlayArea:0} isn't the outline's area {b.Area:0}");
        var hx = MathF.Max(MathF.Abs(b.Min.X), MathF.Abs(b.Max.X));
        var hz = MathF.Max(MathF.Abs(b.Min.Y), MathF.Abs(b.Max.Y));
        if (MathF.Abs(m.HalfX - Math.Clamp(hx, 5, 500)) > 1e-4f || MathF.Abs(m.HalfZ - Math.Clamp(hz, 5, 500)) > 1e-4f)
            Fail($"{m.Name}: HalfX/HalfZ {m.HalfX}/{m.HalfZ} aren't the outline's bounding box {hx}/{hz}");
    }

    // ---- The barrier --------------------------------------------------------------------------------------------

    private static void ShapeBarrier(Map m)
    {
        var b = m.Boundary!;
        var rng = new Random(m.Name.Length * 31 + 7);
        int rayMiss = 0, navMiss = 0, outClassic = 0, outSwept = 0, outGrenade = 0, outDebris = 0, kindWrong = 0;
        float worstClassic = -9, worstSwept = -9;
        const int points = 40;
        var phys = World.Physics;
        if (phys.BarrierCount != m.BoundaryBarriers.Count)
            Fail($"{m.Name}: the physics world has {phys.BarrierCount} barrier pieces, the map {m.BoundaryBarriers.Count}");
        for (var k = 0; k < points; k++)
        {
            var seg = rng.Next(b.SegmentCount);
            var t = 0.1f + (float)rng.NextDouble() * 0.8f;
            var q = Vector2.Lerp(b.Outline[seg], b.Outline[(seg + 1) % b.SegmentCount], t);
            var n = b.Normals[seg];
            var inPt = q - n * 2f;
            if (!b.Inside(inPt.X, inPt.Y, 1f))
                continue; // a narrow spot: the start isn't clearly inside
            var g = m.GroundHeight(inPt.X, inPt.Y);
            // Rays (bullets, sight) and nav rays stop at the outline.
            var o = new Vector3(inPt.X, g + 1.0f, inPt.Y);
            var dir = new Vector3(n.X, 0, n.Y);
            var hitDist = World.RaycastWorld(o, dir, 8f);
            if (hitDist > 2.05f)
                rayMiss++;
            if (World.RaycastNav(o, dir, 8f) > 2.05f)
                navMiss++;
            if (World.Query.Raycast(o, dir, 8f, QueryFilter.All, out var hit) && hit.Kind != ShapeKind.Boundary && hit.Distance > 1.9f)
                kindWrong++;

            // Both movers: run (and jump) straight at the edge, then along it.
            foreach (var mode in new[] { PhysicsMode.Classic, PhysicsMode.Swept })
            {
                World.PhysicsMode = mode;
                var s = new MoveState { Position = new Vector3(inPt.X, g, inPt.Y), Grounded = true };
                var yaw = MathF.Atan2(n.X, -n.Y);
                var worst = -9f;
                for (var tick = 0; tick < 150; tick++)
                {
                    var buttons = Buttons.Forward | Buttons.Sprint | (tick % 25 == 3 ? Buttons.Jump : Buttons.None) | (tick > 90 ? Buttons.Right : Buttons.None);
                    var cmd = new InputCmd { Buttons = buttons, Yaw = yaw, SpeedPct = 100 };
                    World.Simulate(ref s, cmd);
                    worst = MathF.Max(worst, b.SignedDistance(s.Position.X, s.Position.Z));
                }
                if (mode == PhysicsMode.Classic)
                {
                    worstClassic = MathF.Max(worstClassic, worst);
                    if (worst > -World.PlayerRadius + 0.01f)
                        outClassic++;
                }
                else
                {
                    worstSwept = MathF.Max(worstSwept, worst);
                    if (worst > -World.PlayerRadius + 0.01f)
                        outSwept++;
                }
            }
            World.PhysicsMode = PhysicsMode.Classic;

            // A grenade thrown hard at the edge (low, and lobbed high over the barrier).
            foreach (var lob in new[] { 2f, 14f })
            {
                var p = new Projectile { Kind = ThrowableKind.Frag, Pos = new Vector3(inPt.X, g + 1.4f, inPt.Y), Vel = new Vector3(n.X * 18, lob, n.Y * 18) };
                var worst = -9f;
                for (var tick = 0; tick < 180; tick++)
                {
                    Throwables.Step(p, World.TickDt);
                    worst = MathF.Max(worst, b.SignedDistance(p.Pos.X, p.Pos.Z));
                }
                if (worst > 0.001f)
                    outGrenade++;
            }
            // Debris: the same bounce rule (MapBoundary.KeepInside) every frame.
            var dp = new Vector3(inPt.X, g + 1, inPt.Y);
            var dv = new Vector3(n.X * 25, 4, n.Y * 25);
            for (var f = 0; f < 60; f++)
            {
                dp += dv * (1 / 60f);
                b.KeepInside(ref dp, ref dv, 0.05f, 0.3f);
                if (b.SignedDistance(dp.X, dp.Z) > 0.001f)
                {
                    outDebris++;
                    break;
                }
            }
        }
        ShapeLine($"{m.Name}: barrier at {points} points round the edge: rays through {rayMiss}, nav rays through {navMiss}, hit something else {kindWrong}; "
            + $"classic mover out {outClassic} (closest {-worstClassic:0.000} m from the edge), swept out {outSwept} (closest {-worstSwept:0.000} m), grenades out {outGrenade}, debris out {outDebris}");
        if (rayMiss + navMiss + kindWrong + outClassic + outSwept + outGrenade + outDebris > 0)
            Fail($"{m.Name}: the boundary let something through (rays {rayMiss}, nav {navMiss}, other hits {kindWrong}, classic {outClassic}, swept {outSwept}, grenades {outGrenade}, debris {outDebris})");

        // Random spots well outside, and points just inside the edge: the clamp puts players back exactly at the inset.
        var clampBad = 0;
        for (var k = 0; k < 2000; k++)
        {
            var p = new Vector2((float)(rng.NextDouble() * 2 - 1) * (m.HalfX + 10), (float)(rng.NextDouble() * 2 - 1) * (m.HalfZ + 10));
            var c = b.Clamp(p, World.PlayerRadius);
            if (!b.Inside(c.X, c.Y, World.PlayerRadius - 0.001f) || (b.Inside(p.X, p.Y, World.PlayerRadius) && c != p))
                clampBad++;
        }
        if (clampBad > 0)
            Fail($"{m.Name}: the boundary clamp left {clampBad} of 2000 points outside (or moved points that were fine)");
    }

    private static void ShapeNav(Map m)
    {
        for (var wait = 0; wait < 2000 && NavGraph.Current == NavGraph.Empty; wait++)
            NavGraph.Pump(50);
        var g = NavGraph.Current;
        var b = m.Boundary!;
        var nodesOut = g.Nodes.Count(p => !b.Inside(p.X, p.Z, 0.3f));
        var edgesOut = 0;
        for (var i = 0; i < g.Nodes.Length; i++)
            for (var e = g.EdgeStart[i]; e < g.EdgeStart[i + 1]; e++)
            {
                var a = g.Nodes[i];
                var c = g.Nodes[g.EdgeTo[e]];
                var steps = Math.Max(1, (int)(Vector3.Distance(a, c) / 0.5f));
                for (var s = 1; s < steps; s++)
                {
                    var p = Vector3.Lerp(a, c, s / (float)steps);
                    if (!b.Inside(p.X, p.Z, 0.1f))
                    {
                        edgesOut++;
                        break;
                    }
                }
            }
        ShapeLine($"{m.Name}: nav graph {g.Nodes.Length} nodes, {g.EdgeCount} edges, built in {g.BuildMs:0} ms; {nodesOut} nodes and {edgesOut} edges leave the outline");
        if (g.Nodes.Length < 20)
            Fail($"{m.Name}: nav graph has only {g.Nodes.Length} nodes");
        if (nodesOut > 0 || edgesOut > 0)
            Fail($"{m.Name}: the nav graph has {nodesOut} nodes and {edgesOut} edges outside the boundary");
    }

    // ---- Fingerprints, errors, costs ------------------------------------------------------------------------------

    private static void ShapeFingerprints(Map file)
    {
        var plain = Maps.All.Where(m => m.Boundary == null).ToList();
        if (plain.Any(m => m.BoundaryHash != 0 || m.BoundaryBarriers.Count > 0 || m.BoundaryMeshCount > 0))
            Fail("a map without a boundary has boundary data (its fingerprint or looks would change)");
        var checkedHashes = 0;
        foreach (var (name, hash) in ShapeBaseline)
        {
            if (Maps.All.FirstOrDefault(x => x.SourceFile != null && Path.GetFileNameWithoutExtension(x.SourceFile).Equals(name, StringComparison.OrdinalIgnoreCase)) is not { } m
                || m.Boundary != null)
                continue;
            checkedHashes++;
            if (m.ContentHash != hash)
                Fail($"{m.Name} ({name}): fingerprint {m.ContentHash:x8} changed (was {hash:x8} before boundaries)");
        }
        // The same file with "boundary": null, and a box map with a rect boundary (a new version of the map online).
        var proto = Maps.Find("Proto Box") ?? Maps.All[0];
        var json = MapFile.ToJson(proto);
        var withNull = MapFile.Load(json.Replace("\"ops\": [", "\"boundary\": null,\n  \"ops\": ["), "null boundary");
        withNull.Validate();
        var copy = MapFile.Load(json, "copy");
        copy.Validate();
        if (withNull.ContentHash != copy.ContentHash || copy.ContentHash != proto.ContentHash)
            Fail($"\"boundary\": null changed {proto.Name}'s fingerprint");
        var rect = MapFile.Load(json.Replace("\"ops\": [", "\"boundary\": { \"shape\": \"rect\", \"edge\": \"none\" },\n  \"ops\": [").Replace("{ \"op\": \"Perimeter\"", "{ \"op\": \"Block\", \"x0\": 0, \"y0\": 0, \"z0\": 0, \"x1\": 0.1, \"y1\": 0.1, \"z1\": 0.1, \"color\": [1, 1, 1] }, { \"op\": \"Ground\""), "rect boundary");
        rect.Validate();
        if (rect.Boundary == null || rect.ContentHash == copy.ContentHash || rect.HalfX != proto.HalfX)
            Fail("a rect boundary on Proto Box should keep its size and change the fingerprint");
        // Round trip of a shaped map: identical outline, boxes and fingerprint.
        var again = MapFile.Load(MapFile.ToJson(file), "Shape Test (round trip)");
        again.Validate();
        if (again.ContentHash != file.ContentHash || !again.Boundary!.Outline.SequenceEqual(file.Boundary!.Outline) || again.HalfX != file.HalfX)
            Fail("Shape Test changed when written out and read back");
        ShapeLine($"fingerprints: {plain.Count} maps without a boundary have no boundary data; {checkedHashes} match their pre-boundary fingerprints; "
            + $"Shape Test {file.ContentHash:x8} (boundary {file.BoundaryHash:x8}) survives a round trip");
        if (ShapeBaseline.Length > 0 && checkedHashes < 20)
            Fail($"only {checkedHashes} baseline fingerprints were checked");
    }

    private static void ShapeErrors()
    {
        foreach (var (bad, expect) in new[]
                 {
                     ("\"boundary\": { \"shape\": \"blob\" }", "unknown shape"),
                     ("\"boundary\": { \"shape\": \"circle\" }", "radius"),
                     ("\"boundary\": { \"shape\": \"circle\", \"radius\": 20, \"radiusX\": 3 }", "has no"),
                     ("\"boundary\": { \"shape\": \"polygon\", \"points\": [[0, 0], [10, 0]] }", "points"),
                     ("\"boundary\": { \"shape\": \"polygon\", \"points\": [[-20, -20], [20, 20], [20, -20], [-20, 20]] }", "crosses itself"),
                     ("\"boundary\": { \"shape\": \"circle\", \"radius\": 20, \"edge\": \"cliff\" }", "terrain"),
                     ("\"boundary\": { \"shape\": \"circle\", \"radius\": 20, \"edge\": { \"style\": \"moat\" } }", "unknown style"),
                     ("\"boundary\": { \"shape\": \"circle\", \"radius\": 20, \"edge\": { \"style\": \"props\" } }", "props"),
                     ("\"boundary\": { \"shape\": \"circle\", \"radius\": 20, \"noise\": { \"amplitude\": 2, \"wobble\": 1 } }", "unknown field"),
                     ("\"boundary\": { \"shape\": \"circle\", \"radius\": 20, \"noise\": { \"amplitude\": 25, \"scale\": 3 } }", "crosses itself"),
                 })
        {
            var json = "{ \"name\": \"X\", " + bad + ", \"ops\": [ { \"op\": \"Ground\", \"color\": [0.5, 0.5, 0.5] }, { \"op\": \"Spawn\", \"x\": 0, \"z\": 0 } ] }";
            try
            {
                MapFile.Load(json, "bad.json");
                Fail($"a bad boundary loaded without complaint (expected '{expect}'): {bad}");
            }
            catch (MapFileException ex) when (ex.Message.Contains(expect, StringComparison.OrdinalIgnoreCase))
            {
            }
            catch (Exception ex)
            {
                Fail($"bad boundary {bad} gave the wrong error: {ex.Message}");
            }
        }
        // Perimeter can't be combined with a boundary (its edge replaces it).
        try
        {
            MapFile.Load("{ \"name\": \"X\", \"boundary\": { \"shape\": \"circle\", \"radius\": 20 }, \"ops\": [ { \"op\": \"Perimeter\", \"height\": 4, \"color\": [1, 1, 1] }, { \"op\": \"Spawn\", \"x\": 0, \"z\": 0 } ] }", "bad.json");
            Fail("Perimeter on a map with a boundary should be an error");
        }
        catch (MapFileException ex) when (ex.Message.Contains("boundary"))
        {
        }
        ShapeLine("bad boundary sections give readable errors");
    }

    private static void ShapeCosts(Map file)
    {
        var b = file.Boundary!;
        var rng = new Random(3);
        // Clamp cost near the edge (the worst case: every call walks the segments) and in the open (one grid lookup).
        var near = new Vector3[4096];
        for (var i = 0; i < near.Length; i++)
        {
            var seg = rng.Next(b.SegmentCount);
            var q = Vector2.Lerp(b.Outline[seg], b.Outline[(seg + 1) % b.SegmentCount], (float)rng.NextDouble());
            var p = q + b.Normals[seg] * ((float)rng.NextDouble() * 2 - 1.5f);
            near[i] = new Vector3(p.X, 0, p.Y);
        }
        var sw = Stopwatch.StartNew();
        var sink = 0f;
        for (var rep = 0; rep < 10; rep++)
            foreach (var p in near)
                sink += b.ClampFeet(p, World.PlayerRadius).X;
        var nearUs = sw.Elapsed.TotalMilliseconds * 1000 / (near.Length * 10);
        sw.Restart();
        var pole = new Vector3(b.Pole.X, 0, b.Pole.Y);
        for (var i = 0; i < 100000; i++)
            sink += b.ClampFeet(pole, World.PlayerRadius).X;
        var openUs = sw.Elapsed.TotalMilliseconds * 1000 / 100000;
        var build = Stopwatch.StartNew();
        var rebuilt = new MapBoundary(b.Spec, file.HalfX, file.HalfZ);
        var buildMs = build.Elapsed.TotalMilliseconds;
        World.SetMap(file);
        var phys = World.Physics;
        ShapeLine($"costs on {file.Name}: {b.SegmentCount} outline segments, {file.BoundaryMeshCount} boundary meshes ({file.BoundaryMeshCount - 2} wall pieces + floor + far ground), "
            + $"outline built in {buildMs:0.0} ms; clamp {nearUs:0.00} us near the edge, {openUs:0.000} us in the open; physics: {phys.Describe()}");
        if (rebuilt.SegmentCount != b.SegmentCount)
            Fail("rebuilding the Shape Test outline gave a different outline");
        if (b.SegmentCount > 600 || file.BoundaryMeshCount > 650)
            Fail($"Shape Test has {b.SegmentCount} outline segments and {file.BoundaryMeshCount} boundary meshes (too many for a small bean)");
        if (buildMs > 250)
            Fail($"building the Shape Test outline took {buildMs:0} ms");
        if (nearUs > 40 || openUs > 2)
            Fail($"the boundary clamp is too slow ({nearUs:0.0} us near the edge, {openUs:0.00} us in the open)");
        _ = sink;
    }

    private static void ShapeBotMatch(Map map)
    {
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
            bots: Math.Min(map.MaxPlayers, Game.ClassicPlayers) - 1, botSkill: BotDifficulty.Normal, mapId: map.Id);
        game.MapVoteEnabled = false;
        const double dt = 1.0 / World.TickRate;
        var now = 0.0;
        var moved = new Dictionary<byte, float>();
        var last = new Dictionary<byte, Vector3>();
        var outside = 0;
        var fallen = 0;
        var cpu = new TickCpu();
        var b = map.Boundary!;
        for (var i = 0; i < 40 * World.TickRate; i++)
        {
            now += dt;
            cpu.Start();
            var t0 = Stopwatch.GetTimestamp();
            game.Update(now, dt);
            cpu.Stop(Stopwatch.GetElapsedTime(t0).TotalMilliseconds, count: i > 60);
            foreach (var p in game.Players.Values)
            {
                if (!p.Alive)
                    continue;
                if (!float.IsFinite(p.Position.X + p.Position.Y + p.Position.Z))
                {
                    Fail($"player {p.Id} position is NaN on {map.Name}");
                    return;
                }
                if (b.SignedDistance(p.Position.X, p.Position.Z) > -World.PlayerRadius + 0.01f)
                    outside++;
                if (p.Position.Y < -0.5f)
                    fallen++;
            }
            if (i % 60 == 0)
                foreach (var p in game.Players.Values.Where(p => p.Bot != null && p.Alive))
                {
                    if (last.TryGetValue(p.Id, out var lp))
                        moved[p.Id] = moved.GetValueOrDefault(p.Id) + Vector3.Distance(lp, p.Position);
                    last[p.Id] = p.Position;
                }
        }
        var stuck = moved.Count(kv => kv.Value < 10f);
        var kills = game.Players.Values.Sum(p => (int)p.Kills);
        ShapeLine($"bot match on {map.Name} (40 s, {game.Players.Count} players): kills {kills}, avg bot travel {(moved.Count == 0 ? 0 : moved.Values.Average()):0} m, stuck {stuck}, "
            + $"player-ticks outside the outline {outside}, below the floor {fallen}, worst tick {cpu.WorstText}");
        if (stuck > 1)
            Fail($"{map.Name}: {stuck} bots barely moved");
        if (fallen > 0 || outside > 0)
            Fail($"{map.Name}: players left the map ({outside} player-ticks outside the outline, {fallen} below the floor)");
        if (cpu.WorstMs > MaxTickWorkMs)
            Fail($"{map.Name}: a tick did {cpu.WorstMs:0.0} ms of work (over {MaxTickWorkMs} ms; {cpu.WorstDetail})");
    }
}
