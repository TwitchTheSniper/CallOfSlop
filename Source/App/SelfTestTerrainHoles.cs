using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The "Terrain holes" self-test section: holes and caves in the terrain (TerrainVoids.cs). The hole mask from the "hole"
/// features, ground and ray queries through holes, falling and grenades, no plants or clutter over holes, the renderer's
/// index list, the Cave Test map (walking through its tunnel, a bot route out of the sinkhole, pits bots can't leave),
/// fingerprints of every other map unchanged and no slowdown for terrain without holes.
/// </summary>
public static partial class SelfTest
{
    private const string HoleLabJson = """
        {
          "format": "call-of-slop-map/1",
          "name": "Hole Lab",
          "halfX": 30,
          "halfZ": 30,
          "terrain": {
            "cell": 1,
            "margin": 8,
            "features": [
              { "type": "hole", "shape": "circle", "x": 0, "z": 0, "radius": 4, "depth": 3 },
              { "type": "hole", "shape": "ellipse", "x": 15, "z": 15, "radius": 4, "radiusZ": 2, "angle": 45, "depth": 2 },
              { "type": "hole", "shape": "polygon", "points": [[-20, -20], [-12, -20], [-12, -13]], "depth": 1 }
            ]
          },
          "ops": [
            { "op": "Ground", "color": [0.36, 0.5, 0.25] },
            { "op": "Perimeter", "height": 3, "color": "#8f8a80" },
            { "op": "RingSpawns", "count": 8, "frac": 0.75 },
            { "op": "AutoWaypoints", "spacing": 12 },
            { "op": "Clutter", "props": ["Wooden Crate", "Oil Drum", "Tyre"], "x0": -25, "z0": -25, "x1": 25, "z1": 25, "count": 60, "seed": 3 }
          ],
          "foliage": { "seed": 1, "regions": [ { "type": "grass", "density": 6, "rect": [-29, -29, 29, 29], "seed": 1 }, { "type": "tree", "density": 0.05, "rect": [-29, -29, 29, 29], "seed": 2 } ] }
        }
        """;

    private static void HolesLine(string s) => Line("  " + s);

    private static void CheckTerrainHoles()
    {
        var previous = World.Map;
        var previousMover = World.Mover;
        try
        {
            World.Mover = ClassicMover.Instance;
            HoleMask();
            HoleQueries();
            HoleFoliageAndProps();
            CaveTestMap();
            HoleTraps();
            HoleFingerprints();
            HolePerformance();
        }
        finally
        {
            World.Mover = previousMover;
            World.SetMap(previous);
        }
    }

    private static Map HoleLab(string json = HoleLabJson, string source = "hole lab")
    {
        var m = MapFile.Load(json, source);
        m.Validate();
        return m;
    }

    // ---- The mask -------------------------------------------------------------------------

    private static void HoleMask()
    {
        var m = HoleLab();
        var t = m.Terrain!;
        if (t.Voids is not { } v)
        {
            Fail("hole lab: no hole data");
            return;
        }
        // Cells whose middle is inside each shape.
        int circle = 0, ellipse = 0, poly = 0;
        for (var j = 0; j < t.CountZ - 1; j++)
        for (var i = 0; i < t.CountX - 1; i++)
        {
            if (t.CellKind(i, j) != TerrainVoids.Open)
                continue;
            float x = t.OriginX + (i + 0.5f) * t.Cell, z = t.OriginZ + (j + 0.5f) * t.Cell;
            if (x * x + z * z < 30)
                circle++;
            else if (MathF.Abs(x - 15) < 5 && MathF.Abs(z - 15) < 5)
                ellipse++;
            else if (x < -10 && z < -10)
                poly++;
        }
        HolesLine($"hole lab: {v.OpenCells} hole cells (circle {circle}, ellipse {ellipse}, triangle {poly}), {v.CaveCells} cave cells, "
                  + $"floors {v.MinFloor:0.00} m and up, fingerprint {t.Hash:x8}");
        if (circle is < 42 or > 58 || ellipse is < 18 or > 34 || poly is < 18 or > 32 || circle + ellipse + poly != v.OpenCells)
            Fail($"hole lab: unexpected hole cells (circle {circle} ~50, ellipse {ellipse} ~25, triangle {poly} ~25, total {v.OpenCells})");
        if (!m.IsTerrainHole(0.5f, 0.5f) || !m.IsTerrainHole(14.5f, 15.5f) || !m.IsTerrainHole(-14.5f, -18.5f) || m.IsTerrainHole(6.5f, 0.5f) || m.IsTerrainHole(15.5f, 19.5f))
            Fail("hole lab: IsTerrainHole is wrong at a test point");

        // Renderer: hole cells get no triangles; terrain without holes gets every cell.
        var (indices, _) = TerrainRenderer.SurfaceIndices(t);
        var cells = (t.CountX - 1) * (t.CountZ - 1);
        if (indices.Length != (cells - v.OpenCells) * 6)
            Fail($"surface index count {indices.Length}, expected {(cells - v.OpenCells) * 6} ({cells} cells minus {v.OpenCells} holes, 6 each)");
        t.Locate(0.5f, 0.5f, out var hi, out var hj, out _, out _);
        var corners = new HashSet<uint> { (uint)(hj * t.CountX + hi), (uint)(hj * t.CountX + hi + 1), (uint)((hj + 1) * t.CountX + hi), (uint)((hj + 1) * t.CountX + hi + 1) };
        for (var k = 0; k < indices.Length; k += 3)
            if (corners.Contains(indices[k]) && corners.Contains(indices[k + 1]) && corners.Contains(indices[k + 2]))
            {
                Fail("a hole cell still has a surface triangle");
                break;
            }
        var (caveVerts, caveIdx) = TerrainRenderer.CaveMesh(t);
        HolesLine($"surface {indices.Length / 3} triangles ({cells * 2} without holes), hole floors and rims {caveIdx.Length / 3} triangles, {caveVerts.Length} vertices");
        if (caveIdx.Length < v.OpenCells * 6)
            Fail("the hole mesh is missing floors");
        if (Maps.Find("Terrain Test") is { Terrain: { } plain })
        {
            var (pi, _) = TerrainRenderer.SurfaceIndices(plain);
            if (pi.Length != (plain.CountX - 1) * (plain.CountZ - 1) * 6 || TerrainRenderer.CaveMesh(plain).Indices.Length != 0)
                Fail("Terrain Test (no holes) lost surface triangles or got a hole mesh");
        }

        // Bad features are refused with a readable message.
        foreach (var (bad, why) in new[]
                 {
                     ("{ \"type\": \"hole\", \"shape\": \"circle\", \"x\": 0, \"z\": 0 }", "radius"),
                     ("{ \"type\": \"hole\", \"shape\": \"polygon\", \"points\": [[0, 0], [1, 1]] }", "3 points"),
                     ("{ \"type\": \"hole\", \"shape\": \"square\", \"x\": 0, \"z\": 0, \"radius\": 1 }", "circle"),
                     ("{ \"type\": \"cave\", \"points\": [[0, 0], [5, 0]], \"width\": -1 }", "width"),
                     ("{ \"type\": \"cave\", \"points\": [[0, 0], [5, 0]], \"open\": \"middle\" }", "both"),
                 })
        {
            try
            {
                HoleLab(HoleLabJson.Replace("{ \"type\": \"hole\", \"shape\": \"circle\", \"x\": 0, \"z\": 0, \"radius\": 4, \"depth\": 3 },", bad + ","), "bad hole");
                Fail($"a broken hole/cave feature loaded: {bad}");
            }
            catch (MapFileException ex) when (ex.Message.Contains(why, StringComparison.Ordinal))
            {
            }
            catch (MapFileException ex)
            {
                Fail($"broken feature {bad}: message doesn't mention \"{why}\": {ex.Message}");
            }
        }

        // Round trip through the map file writer.
        var json = MapFile.ToJson(m);
        var copy = MapFile.Load(json, "hole lab round trip");
        copy.Validate();
        if (copy.ContentHash != m.ContentHash || copy.Terrain!.Hash != t.Hash || MapFile.ToJson(copy) != json)
            Fail("a map with holes changed in a file round trip");
    }

    // ---- Queries, falling, grenades -------------------------------------------------------

    private static void HoleQueries()
    {
        var m = HoleLab();
        World.SetMap(m);
        var tc = m.TerrainCollision!;
        void Near(string what, float got, float want, float tol)
        {
            if (!(MathF.Abs(got - want) <= tol))
                Fail($"hole lab: {what} = {got:0.000}, expected {want:0.000}");
        }
        Near("ground in the circle hole", m.GroundHeight(0.5f, 0.5f), -3f, 0.01f);
        Near("ground beside it", m.GroundHeight(8.5f, 0.5f), 0f, 0.01f);
        Near("ground in the triangle hole", m.GroundHeight(-14.5f, -18.5f), -1f, 0.01f);
        Near("ray down into the hole", tc.Raycast(new Vector3(0.5f, 5, 0.5f), -Vector3.UnitY, 20), 8f, 0.01f);
        Near("ray down beside it", tc.Raycast(new Vector3(8.5f, 5, 0.5f), -Vector3.UnitY, 20), 5f, 0.01f);
        // Sideways inside the pit: the pit's wall (the next solid cell) stops the ray; the hit normal faces back.
        var side = tc.Raycast(new Vector3(0.5f, -1.5f, 0.5f), Vector3.UnitX, 10);
        if (side is < 2.4f or > 4.6f)
            Fail($"hole lab: sideways ray in the pit went {side:0.00} m (wall expected 2.5-4.5 m away)");
        if (World.Query.Raycast(new Vector3(0.5f, -1.5f, 0.5f), Vector3.UnitX, 10, QueryFilter.All, out var hit) && hit.Normal.X > -0.9f)
            Fail($"hole lab: the pit wall's hit normal is {hit.Normal}, expected -X");
        if (World.Blocked(new Vector3(0.5f, -3f, 0.5f)) || !World.Blocked(new Vector3(8.5f, -1f, 0.5f)))
            Fail("hole lab: body checks wrong in or beside the hole");

        // A player over the hole falls in, then can't walk out of a 3 m pit (only the wall stops them).
        var s = new MoveState { Position = new Vector3(0.5f, 0f, 0.5f) };
        for (var k = 0; k < 2 * World.TickRate; k++)
            World.Simulate(ref s, new InputCmd { Yaw = 0, SpeedPct = 100 });
        Near("player fallen into the hole", s.Position.Y, -3f, 0.05f);
        var yaw = MathF.Atan2(1, 0); // +X
        for (var k = 0; k < 3 * World.TickRate; k++)
            World.Simulate(ref s, new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint, Yaw = yaw, SpeedPct = 100 });
        HolesLine($"player in the pit after walking at its wall: ({s.Position.X:0.00}, {s.Position.Y:0.00}, {s.Position.Z:0.00})");
        if (s.Position.X > 4.6f || s.Position.Y > -2.9f)
            Fail("hole lab: walked out through a pit wall");
        // Walking off the edge from outside drops in.
        s = new MoveState { Position = new Vector3(8.5f, 0f, 0.5f), Grounded = true };
        yaw = MathF.Atan2(-1, 0); // -X
        for (var k = 0; k < 2 * World.TickRate; k++)
            World.Simulate(ref s, new InputCmd { Buttons = Buttons.Forward, Yaw = yaw, SpeedPct = 100 });
        if (s.Position.Y > -2.9f)
            Fail($"hole lab: walking into the hole didn't drop in (y {s.Position.Y:0.00})");

        // Grenades: fall through the hole to its floor, land on the ground beside it, bounce off the pit's wall.
        var g = new Projectile { Kind = ThrowableKind.Frag, Pos = new Vector3(0.5f, 3f, 0.5f) };
        for (var k = 0; k < 3 * World.TickRate; k++)
            Throwables.Step(g, World.TickDt);
        Near("grenade dropped into the hole", g.Pos.Y, -3f + Throwables.ProjectileRadius, 0.06f);
        var g2 = new Projectile { Kind = ThrowableKind.Frag, Pos = new Vector3(8.5f, 3f, 0.5f) };
        for (var k = 0; k < 3 * World.TickRate; k++)
            Throwables.Step(g2, World.TickDt);
        Near("grenade dropped beside the hole", g2.Pos.Y, Throwables.ProjectileRadius, 0.06f);
        var g3 = new Projectile { Kind = ThrowableKind.Frag, Pos = new Vector3(0.5f, -2f, 0.5f), Vel = new Vector3(9f, 0, 0) };
        var maxX = 0f;
        for (var k = 0; k < 2 * World.TickRate; k++)
        {
            Throwables.Step(g3, World.TickDt);
            maxX = MathF.Max(maxX, g3.Pos.X);
        }
        HolesLine($"grenades: into the hole y {g.Pos.Y:0.00}, beside it y {g2.Pos.Y:0.00}, thrown at the pit wall: furthest x {maxX:0.00}, ended ({g3.Pos.X:0.00}, {g3.Pos.Y:0.00})");
        if (maxX > 4.6f || g3.Pos.Y > -1.5f || g3.Pos.Y < -3.1f)
            Fail("hole lab: a grenade went through the pit's wall");
    }

    private static void HoleFoliageAndProps()
    {
        var m = HoleLab();
        var plants = m.FoliageInstances;
        var overHoles = plants.Count(p => m.TerrainHoleNear(p.Position.X, p.Position.Z, 0.3f));
        var props = m.PropInstances;
        var propsOver = props.Count(p => m.IsTerrainHole(p.Position.X, p.Position.Z) || m.IsTerrainHole(p.Bounds.Min.X, p.Bounds.Min.Z)
                                         || m.IsTerrainHole(p.Bounds.Max.X, p.Bounds.Max.Z) || m.IsTerrainHole(p.Bounds.Min.X, p.Bounds.Max.Z)
                                         || m.IsTerrainHole(p.Bounds.Max.X, p.Bounds.Min.Z));
        HolesLine($"hole lab: {plants.Length} plants ({overHoles} over holes), {props.Count} props ({propsOver} over holes)");
        if (plants.Length < 500 || props.Count < 10)
            Fail("hole lab: too few plants or props to test");
        if (overHoles > 0 || propsOver > 0)
            Fail("hole lab: plants or props over a hole");
        if (m.Waypoints.Any(w => m.IsTerrainHole(w.X, w.Z) && MathF.Abs(w.Y - m.GroundHeight(w.X, w.Z)) > 0.05f))
            Fail("hole lab: a waypoint in a hole isn't on the hole's floor");
    }

    // ---- The Cave Test map --------------------------------------------------------------

    /// <summary>Walks a player along <paramref name="route"/> (x, z matter), sprinting, jumping when stalled. True if it got to the end.</summary>
    private static bool HoleWalk(Vector3 start, IReadOnlyList<Vector3> route, float seconds, Action<MoveState>? each, out MoveState s)
    {
        s = new MoveState { Position = start, Grounded = true };
        var idx = 0;
        var check = s.Position;
        var jump = false;
        for (var tick = 0; tick < seconds * World.TickRate; tick++)
        {
            var pos = s.Position;
            while (idx < route.Count && new Vector2(route[idx].X - pos.X, route[idx].Z - pos.Z).Length() < 1.0f)
                idx++;
            if (idx >= route.Count)
                return true;
            var aim = route[idx];
            var yaw = MathF.Atan2(aim.X - pos.X, -(aim.Z - pos.Z));
            var keys = Buttons.Forward | Buttons.Sprint | (jump ? Buttons.Jump : Buttons.None);
            jump = false;
            World.Simulate(ref s, new InputCmd { Buttons = keys, Yaw = yaw, SpeedPct = 100 });
            each?.Invoke(s);
            if (tick % World.TickRate == World.TickRate - 1)
            {
                if (new Vector2(s.Position.X - check.X, s.Position.Z - check.Z).Length() < 1f)
                    jump = true;
                check = s.Position;
            }
        }
        return false;
    }

    private static void CaveTestMap()
    {
        if (Maps.Find("Cave Test") is not { } m)
        {
            Fail("the Cave Test map didn't load");
            return;
        }
        var t = m.Terrain!;
        if (t.Voids is not { } v || v.OpenCells == 0 || v.CaveCells == 0)
        {
            Fail("Cave Test has no holes or no roofed cave");
            return;
        }
        World.SetMap(m);
        var tc = m.TerrainCollision!;
        HolesLine($"Cave Test: {v.OpenCells} hole cells, {v.CaveCells} cave cells, {v.NavPoints.Length} cave nav points, floors down to {v.MinFloor:0.0} m, fingerprint {m.ContentHash:x8}");

        // The chamber: floor at -7, a roof over it (apart from the skylight), darker inside than at the entrances.
        var kind = t.Column(-3f, 1.5f, out var floor, out var ceiling, out var top, out var hollow);
        if (kind != TerrainVoids.Cave || !hollow || MathF.Abs(floor + 7f) > 0.3f || ceiling - floor < 3.5f || top - ceiling < TerrainVoids.RoofMin - 0.01f)
            Fail($"Cave Test chamber: kind {kind}, floor {floor:0.00}, ceiling {ceiling:0.00}, surface {top:0.00}");
        if (MathF.Abs(m.GroundHeight(-3f, 1.5f) - top) > 0.01f || MathF.Abs(m.GroundHeight(-3f, 1.5f, floor + 0.5f) - floor) > 0.01f)
            Fail("Cave Test: ground over the chamber should be the surface on top and the floor inside");
        // A ray down from the sky stops on the roof; from inside, on the floor; up from inside, at the ceiling.
        var down = tc.Raycast(new Vector3(-3f, top + 5f, 1.5f), -Vector3.UnitY, 30f);
        var downIn = tc.Raycast(new Vector3(-3f, floor + 1.5f, 1.5f), -Vector3.UnitY, 30f);
        var upIn = tc.Raycast(new Vector3(-3f, floor + 1.5f, 1.5f), Vector3.UnitY, 30f);
        if (MathF.Abs(down - 5f) > 0.05f || MathF.Abs(downIn - 1.5f) > 0.1f || MathF.Abs(upIn - (ceiling - floor - 1.5f)) > 0.1f)
            Fail($"Cave Test: rays in the chamber: down from the sky {down:0.00} (5), down inside {downIn:0.00} (1.5), up inside {upIn:0.00} ({ceiling - floor - 1.5f:0.00})");
        var deep = v.Light[(int)MathF.Round((0f - t.OriginZ) / t.Cell) * t.CountX + (int)MathF.Round((-12f - t.OriginX) / t.Cell)];
        var mouth = v.Light[(int)MathF.Round((0f - t.OriginZ) / t.Cell) * t.CountX + (int)MathF.Round((-34f - t.OriginX) / t.Cell)];
        HolesLine($"daylight on the floor: tunnel mouth {mouth:0.00}, deep in the tunnel {deep:0.00}");
        if (!(deep < 0.6f && mouth > 0.8f))
            Fail("Cave Test: the cave isn't darker inside than at its mouth");
        if (m.FoliageInstances.Any(p => m.TerrainHoleNear(p.Position.X, p.Position.Z, 0.3f)) || m.FoliageInstances.Any(p => p.Position.Y < t.GroundBelow(p.Position.X, p.Position.Z, 100f) - 0.5f))
            Fail("Cave Test: plants over a hole or inside a cave");

        // Walk in at the west entrance, through the tunnel and the chamber, out of the east entrance.
        Vector3 At(float x, float z) => new(x, m.GroundHeight(x, z), z);
        var route = new[] { At(-38, 0), At(-30, 0), At(-20, 0), At(-10, 0), At(-2, 1.5f), At(8, 0), At(20, 0), At(30, 0), At(38, 0), At(46, 0) };
        var lowest = float.MaxValue;
        var underRoof = false;
        var popped = false;
        var ok = HoleWalk(At(-46, 0), route, 40f, st =>
        {
            lowest = MathF.Min(lowest, st.Position.Y);
            var k = t.Column(st.Position.X, st.Position.Z, out _, out var c, out var h, out var hol);
            if (k == TerrainVoids.Cave && hol && st.Position.Y < c)
                underRoof = true;
            if (k == TerrainVoids.Cave && st.Position.Y > h - 0.5f && MathF.Abs(st.Position.X) < 20)
                popped = true; // on top of the roof while walking the tunnel: pushed through it
        }, out var end);
        HolesLine($"walk through the cave: {(ok ? "made it" : "stuck")} at ({end.Position.X:0.0}, {end.Position.Y:0.00}, {end.Position.Z:0.0}), lowest {lowest:0.00} m, under the roof {underRoof}");
        if (!ok || lowest > -6f || !underRoof || popped || MathF.Abs(end.Position.Y - m.GroundHeight(end.Position.X, end.Position.Z)) > 0.3f)
            Fail("Cave Test: couldn't walk in at one entrance, through the tunnel and out of the other");

        // Into the tunnel's side wall: stays in the tunnel, under the roof.
        var sideways = new MoveState { Position = At(-14, 0) with { Y = t.GroundBelow(-14, 0, -5f) }, Grounded = true };
        for (var k = 0; k < 2 * World.TickRate; k++)
            World.Simulate(ref sideways, new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint, Yaw = MathF.PI, SpeedPct = 100 }); // +Z
        if (sideways.Position.Z > 3.2f || sideways.Position.Y > -3f)
            Fail($"Cave Test: walked into the tunnel wall and ended at ({sideways.Position.X:0.0}, {sideways.Position.Y:0.00}, {sideways.Position.Z:0.0})");
        // Jumping in the tunnel: the head stops at the ceiling.
        var jump = new MoveState { Position = new Vector3(-14, t.GroundBelow(-14, 0, -5f), 0), Grounded = true };
        var peak = jump.Position.Y;
        for (var k = 0; k < World.TickRate; k++)
        {
            World.Simulate(ref jump, new InputCmd { Buttons = Buttons.Jump, SpeedPct = 100 });
            peak = MathF.Max(peak, jump.Position.Y);
        }
        var tunnelCeiling = t.CeilingAbove(-14, 0, peak);
        if (peak + World.PlayerHeight > tunnelCeiling + 0.01f || peak > -3f)
            Fail($"Cave Test: a jump in the tunnel reached feet {peak:0.00} (ceiling {tunnelCeiling:0.00})");
        // A grenade lobbed up in the tunnel comes back down off the ceiling.
        var gren = new Projectile { Kind = ThrowableKind.Frag, Pos = new Vector3(-14, t.GroundBelow(-14, 0, -5f) + 1f, 0), Vel = new Vector3(0, 12f, 0) };
        var gPeak = gren.Pos.Y;
        for (var k = 0; k < 2 * World.TickRate; k++)
        {
            Throwables.Step(gren, World.TickDt);
            gPeak = MathF.Max(gPeak, gren.Pos.Y);
        }
        if (gPeak > tunnelCeiling || gren.Pos.Y > -4f)
            Fail($"Cave Test: a grenade thrown up in the tunnel peaked at {gPeak:0.00} (ceiling {tunnelCeiling:0.00}) and ended at {gren.Pos.Y:0.00}");
        // A grenade dropped into the sinkhole lands on the side passage's floor.
        var sink = new Projectile { Kind = ThrowableKind.Frag, Pos = new Vector3(0.5f, 6f, 27.5f) };
        for (var k = 0; k < 3 * World.TickRate; k++)
            Throwables.Step(sink, World.TickDt);
        if (MathF.Abs(sink.Pos.Y - (-7f + Throwables.ProjectileRadius)) > 0.1f)
            Fail($"Cave Test: a grenade dropped into the sinkhole ended at y {sink.Pos.Y:0.00}");

        // Bots: a route out of the sinkhole, along the side passage, the chamber and the west tunnel, and a bot can walk it.
        for (var wait = 0; wait < 2000 && NavGraph.Current == NavGraph.Empty; wait++)
            NavGraph.Pump(50);
        var g = NavGraph.Current;
        var caveNodes = g.Nodes.Count(n => t.Column(n.X, n.Z, out _, out var c, out _, out var hol) == TerrainVoids.Cave && hol && n.Y < c);
        int a = g.Nearest(new Vector3(0.5f, -7f, 27.5f), needSight: false), b = g.Nearest(At(-46, 0), needSight: false);
        var path = new int[256];
        var len = a < 0 || b < 0 ? 0 : new NavSearch().Find(g, a, b, path, null, 100000);
        var throughTunnel = false;
        for (var i = 0; i < len; i++)
        {
            var n = g.Nodes[path[i]];
            if (n.X is > -22 and < -9 && n.Y < -3.5f)
                throughTunnel = true;
        }
        HolesLine($"Cave Test nav: {g.Nodes.Length} nodes ({caveNodes} under a roof), {g.MainNodes.Length} in the main area, {g.TrapNodes} trap nodes; "
                  + $"sinkhole -> west field: {len} nodes, through the tunnel {throughTunnel}");
        if (caveNodes < 15)
            Fail("Cave Test: hardly any nav nodes in the caves");
        if (len == 0 || !throughTunnel)
            Fail("Cave Test: no bot route out of the sinkhole through the tunnel");
        else
        {
            var nodes = Enumerable.Range(0, len).Select(i => g.Nodes[path[i]]).ToList();
            var walked = HoleWalk(new Vector3(0.5f, -7f, 27.5f), nodes, 45f, null, out var botEnd);
            HolesLine($"walking that route: {(walked ? "made it" : "stuck")} at ({botEnd.Position.X:0.0}, {botEnd.Position.Y:0.00}, {botEnd.Position.Z:0.0})");
            if (!walked)
                Fail("Cave Test: a player couldn't follow the bot route out of the sinkhole");
        }
    }

    /// <summary>A pit too deep to climb out of: its nodes stay in the graph (bots who fall in can still plan), but never as goals.</summary>
    private static void HoleTraps()
    {
        var m = HoleLab();
        World.SetMap(m);
        var g = NavGraph.Build(m);
        var inPit = g.Nodes.Count(n => n.X * n.X + n.Z * n.Z < 9 && n.Y < -2.5f);
        var goalsInPit = g.MainNodes.Count(i => g.Nodes[i].X * g.Nodes[i].X + g.Nodes[i].Z * g.Nodes[i].Z < 16 && g.Nodes[i].Y < -2.5f);
        var shallow = g.MainNodes.Count(i => m.IsTerrainHole(g.Nodes[i].X, g.Nodes[i].Z) && g.Nodes[i].Y < -0.5f && g.Nodes[i].Y > -1.5f);
        HolesLine($"hole lab nav: {g.Nodes.Length} nodes, {inPit} in the 3 m pit, {goalsInPit} of those bot goals, {g.TrapNodes} trap nodes, {shallow} goals in the 1 m pit (climbable)");
        if (goalsInPit > 0)
            Fail("hole lab: bots may pick a goal in a pit they can't climb out of");
        if (shallow == 0)
            Fail("hole lab: the shallow pits (climbable) dropped out of the bots' area");
    }

    // ---- Fingerprints and speed ---------------------------------------------------------

    private static void HoleFingerprints()
    {
        var bySource = Maps.All.Where(m => m.SourceFile != null).ToDictionary(m => m.SourceFile!.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);
        var same = 0;
        var off = 0;
        foreach (var (file, hash) in PrePackAssetFingerprints)
        {
            if (!bySource.TryGetValue(file, out var m))
                continue;
            if (m.Terrain?.Voids != null)
                Fail($"{file}: has hole data but no hole or cave features");
            if (m.ContentHash == hash)
                same++;
            else
                off++;
        }
        HolesLine($"{same} of {PrePackAssetFingerprints.Length} pinned maps keep their fingerprint{(off > 0 ? $" ({off} differ: {(OperatingSystem.IsWindows() ? "FAIL" : "Linux maths, not a failure here")})" : "")}");
        if (off > 0 && OperatingSystem.IsWindows())
            Fail($"{off} pinned map fingerprints changed");
        var withHoles = HoleLab();
        var without = HoleLab(HoleLabJson.Replace("{ \"type\": \"hole\", \"shape\": \"circle\", \"x\": 0, \"z\": 0, \"radius\": 4, \"depth\": 3 },", "")
                .Replace("{ \"type\": \"hole\", \"shape\": \"ellipse\", \"x\": 15, \"z\": 15, \"radius\": 4, \"radiusZ\": 2, \"angle\": 45, \"depth\": 2 },", "")
                .Replace("{ \"type\": \"hole\", \"shape\": \"polygon\", \"points\": [[-20, -20], [-12, -20], [-12, -13]], \"depth\": 1 }", ""), "hole lab without holes");
        var deeper = HoleLab(HoleLabJson.Replace("\"radius\": 4, \"depth\": 3", "\"radius\": 4, \"depth\": 3.5"), "hole lab deeper");
        if (without.Terrain!.Voids != null || without.Terrain.Hash == withHoles.Terrain!.Hash || deeper.Terrain!.Hash == withHoles.Terrain.Hash)
            Fail("holes aren't part of the terrain fingerprint (or a map without holes got hole data)");
        if (HoleLab().ContentHash != withHoles.ContentHash)
            Fail("hole lab fingerprint isn't deterministic");
    }

    private static void HolePerformance()
    {
        if (Maps.Find("Terrain Test") is not { TerrainCollision: { } tc, Terrain: { } data })
            return;
        // The old ground query, for comparison: the bilinear patch with no hole check.
        float OldSample(float x, float z)
        {
            data.Locate(x, z, out var i, out var j, out var tx, out var tz);
            var k = j * data.CountX + i;
            var h = data.Heights;
            return (h[k] * (1 - tx) + h[k + 1] * tx) * (1 - tz) + (h[k + data.CountX] * (1 - tx) + h[k + data.CountX + 1] * tx) * tz;
        }
        const int n = 200_000;
        double Time(Func<float, float, float> f)
        {
            var best = double.MaxValue;
            var sink = 0f;
            for (var run = 0; run < 5; run++)
            {
                var sw = Stopwatch.StartNew();
                for (var k = 0; k < n; k++)
                    sink += f(-55f + (k % 997) * 0.11f, -55f + (k % 991) * 0.111f);
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
            }
            return sink == float.MinValue ? -1 : best * 1e6 / n; // ns per call (the sink keeps the loop alive)
        }
        Time(OldSample);
        Time(tc.HeightAt);
        var old = Time(OldSample);
        var now = Time(tc.HeightAt);
        var layered = Time((x, z) => tc.Layer(2f).HeightAt(x, z));
        var rays = Stopwatch.StartNew();
        var hits = 0;
        for (var k = 0; k < 20_000; k++)
        {
            var o = new Vector3(-50f + k % 100, 12f, -50f + k / 200 % 100);
            var d = Vector3.Normalize(new Vector3(MathF.Sin(k), -0.4f, MathF.Cos(k)));
            if (tc.Raycast(o, d, 80f) < 80f)
                hits++;
        }
        var rayUs = rays.Elapsed.TotalMilliseconds * 1000 / 20_000;
        HolesLine($"Terrain Test (no holes): ground query {now:0.0} ns vs {old:0.0} ns for the plain patch, layered view {layered:0.0} ns, ray {rayUs:0.00} us ({hits} hits)");
        if (now > old * 2.5 + 5)
            Fail($"ground queries on terrain without holes got slower: {now:0.0} ns vs {old:0.0} ns");
        if (!ReferenceEquals(tc.Layer(1f), tc))
            Fail("terrain without holes allocates a layered view");
    }
}
