using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The "Terrain" self-test section: heightfield generation (deterministic, identical to the Python map tools),
/// bilinear sampling, ray casts, walking on slopes and into cliffs, mantling onto banks, grenades resting on
/// slopes, placing boxes and spawns on the ground, fingerprints and round trips, and a bot match on the demo map.
/// </summary>
public static partial class SelfTest
{
    /// <summary>
    /// A test terrain using every feature type. Its fingerprint was computed by the Python port
    /// (mapviz/terrain.py); if the two ever disagree, one of them changed a formula.
    /// </summary>
    private const string TerrainLabJson = """
        { "name": "Terrain Lab", "halfX": 40, "halfZ": 40,
          "terrain": { "cell": 1, "margin": 8, "min": -3.5, "max": 6.5,
            "surface": { "sandBelow": -3 },
            "features": [
              { "type": "noise", "amplitude": 1, "scale": 6, "octaves": 3, "seed": 5, "x": 33, "z": -10, "radius": 5 },
              { "type": "hill", "x": -20, "z": -20, "radius": 14, "height": 6 },
              { "type": "basin", "x": 25, "z": 5, "radius": 6, "radiusZ": 4, "depth": 2, "falloff": "dome" },
              { "type": "ridge", "points": [[-35, 32, 1], [-15, 32, 3]], "width": 6, "falloff": "linear", "material": "rock" },
              { "type": "valley", "points": [[20, -30], [38, -30]], "width": 10, "depth": 4, "falloff": "gauss" },
              { "type": "plateau", "x": 25, "z": 25, "w": 10, "d": 8, "height": 1.4, "edge": 0.6 },
              { "type": "flatten", "x": -20, "z": -20, "radius": 3, "edge": 2 },
              { "type": "cliff", "points": [[-40, 10], [0, 10]], "height": 6, "face": 1.5, "back": 12, "side": "left", "taper": 0 },
              { "type": "ramp", "points": [[10, 30, 0], [10, 0, 6]], "width": 6, "edge": 2 },
              { "type": "path", "points": [[30, -15], [36, -20]], "width": 2, "edge": 1, "mode": "cut", "material": "dirt" },
              { "type": "smooth", "passes": 2, "x": 25, "z": 5, "radius": 6 },
              { "type": "grid", "x0": -6, "z0": -36, "cell": 2, "cols": 3, "rows": 3, "heights": [0, 1, 0, 1, 2, 1, 0, 1, 0] },
              { "type": "grid", "x0": 30, "z0": 34, "cell": 1, "cols": 2, "rows": 2, "heights": [0.5, 0.5, 0.5, 0.5], "mode": "set" },
              { "type": "plateau", "x": -32, "z": -36, "radius": 2, "height": 2, "edge": 3, "mode": "set" },
              { "type": "noise", "amplitude": 0.05, "scale": 30, "seed": -3 }
            ] },
          "ops": [
            { "op": "Ground", "color": [0.4, 0.4, 0.4] },
            { "op": "Perimeter", "height": 4, "color": "#808080" },
            { "op": "Prop", "x": 28, "z": 12, "w": 2, "h": 1, "d": 2, "color": "#ff0000" },
            { "op": "Prop", "x": 8, "z": 20, "w": 2, "h": 1, "d": 2, "color": "#00ff00" },
            { "op": "Prop", "x": -30, "z": 25, "w": 2, "h": 1, "d": 2, "color": "#0000ff", "y": 10, "anchor": "world" },
            { "op": "CoverLine", "x0": 12, "z0": 26, "x1": 12, "z1": 4, "height": 1, "thick": 0.6, "color": "#ffff00", "segment": 5, "gap": 2 },
            { "op": "RingSpawns", "count": 8, "frac": 0.7 },
            { "op": "AutoWaypoints", "spacing": 6 }
          ] }
        """;

    /// <summary>Fingerprint of the lab terrain from mapviz/terrain.py.</summary>
    private const uint TerrainLabPythonHash = 0xea5ad12f;

    private static void CheckTerrain()
    {
        var previous = World.Map;
        try
        {
            TerrainGeneration();
            TerrainRays();
            TerrainMovement();
            TerrainThrowables();
            TerrainPlacement();
            TerrainFiles();
            TerrainDemoMap();
        }
        finally
        {
            World.SetMap(previous);
        }
    }

    private static Map LabMap()
    {
        var m = MapFile.Load(TerrainLabJson, "terrain lab");
        m.Validate();
        return m;
    }

    // ---- Heights --------------------------------------------------------------------------

    private static void TerrainGeneration()
    {
        var sw = Stopwatch.StartNew();
        var a = LabMap();
        var ms = sw.Elapsed.TotalMilliseconds;
        var b = LabMap();
        var t = a.Terrain!;
        Line($"lab terrain: {t.CountX} x {t.CountZ} samples, heights {t.MinHeight:0.000} to {t.MaxHeight:0.000}, fingerprint {t.Hash:x8} (Python {TerrainLabPythonHash:x8}), built in {ms:0.0} ms");
        if (!t.Heights.SequenceEqual(b.Terrain!.Heights) || !t.MaterialIds.SequenceEqual(b.Terrain.MaterialIds) || t.Hash != b.Terrain.Hash)
            Fail("terrain generation isn't deterministic (two builds differ)");
        if (t.Hash != TerrainLabPythonHash)
            Fail($"lab terrain fingerprint {t.Hash:x8} differs from the Python map tools' {TerrainLabPythonHash:x8}: the generators no longer match");
        if (t.Heights.Any(h => h * 1024 != MathF.Round(h * 1024)))
            Fail("terrain heights aren't multiples of 1/1024 m");
        if (t.MinHeight < -3.5f || t.MaxHeight > 6.5f)
            Fail($"terrain min/max clamps not applied ({t.MinHeight}..{t.MaxHeight})");

        // Spot heights (Python values, rounded to 1e-3).
        foreach (var (x, z, want) in new[] { (-20f, -20f, 5.999f), (-27f, -20f, 3.013f), (25f, 25f, 1.404f), (10f, 15f, 2.985f), (29f, -30f, -3.5f), (31f, 35f, 0.502f), (-32f, -36f, 2.028f) })
        {
            var h = a.GroundHeight(x, z);
            if (MathF.Abs(h - want) > 0.0015f)
                Fail($"ground at ({x}, {z}) is {h:0.0000}, expected {want:0.000}");
        }

        // Sampling: exact at the samples, bilinear in between, clamped outside the grid.
        var rng = new Random(11);
        var worst = 0f;
        for (var n = 0; n < 2000; n++)
        {
            var i = rng.Next(t.CountX - 1);
            var j = rng.Next(t.CountZ - 1);
            var tx = (float)rng.NextDouble();
            var tz = (float)rng.NextDouble();
            var x = t.OriginX + (i + tx) * t.Cell;
            var z = t.OriginZ + (j + tz) * t.Cell;
            float h00 = t.Height(i, j), h10 = t.Height(i + 1, j), h01 = t.Height(i, j + 1), h11 = t.Height(i + 1, j + 1);
            var want = (h00 * (1 - tx) + h10 * tx) * (1 - tz) + (h01 * (1 - tx) + h11 * tx) * tz;
            worst = MathF.Max(worst, MathF.Abs(a.GroundHeight(x, z) - want));
            if (n < 200 && a.GroundHeight(t.OriginX + i * t.Cell, t.OriginZ + j * t.Cell) != h00)
            {
                Fail($"ground at sample ({i}, {j}) isn't the sample's height");
                break;
            }
        }
        if (worst > 1e-4f)
            Fail($"bilinear sampling is off by up to {worst} m");
        if (a.GroundHeight(-1000, -1000) != t.Height(0, 0) || a.GroundHeight(1000, 1000) != t.Height(t.CountX - 1, t.CountZ - 1))
            Fail("sampling outside the grid isn't clamped to the edge");

        // Normals: a slope's normal matches its gradient; flat ground points straight up.
        var n0 = a.GroundNormal(10, 15);
        var expectSlope = MathF.Atan(6f / 30f) * 180f / MathF.PI;
        if (MathF.Abs(a.GroundSlope(10, 15) - expectSlope) > 1.5f || n0.Z <= 0)
            Fail($"ramp slope {a.GroundSlope(10, 15):0.0} deg (expected about {expectSlope:0.0}) or its normal {n0} points the wrong way");
        if (a.GroundSlope(30, 25) > 2f)
            Fail($"plateau top isn't flat ({a.GroundSlope(30, 25):0.0} deg)");

        // Materials: the painted ridge is rock, the painted path is dirt, the valley floor is sand, flat grass is grass.
        TerrainMaterial Mat(float x, float z)
        {
            t.Locate(x, z, out var i, out var j, out var fx, out var fz);
            return (TerrainMaterial)t.MaterialIds[(j + (fz > 0.5f ? 1 : 0)) * t.CountX + i + (fx > 0.5f ? 1 : 0)];
        }
        if (Mat(-25, 32) != TerrainMaterial.Rock || Mat(33, -17.5f) != TerrainMaterial.Dirt || Mat(29, -30) != TerrainMaterial.Sand || Mat(-30, 0) != TerrainMaterial.Grass)
            Fail($"terrain materials wrong: ridge {Mat(-25, 32)}, path {Mat(33, -17.5f)}, valley {Mat(29, -30)}, field {Mat(-30, 0)}");
        Line($"sampling exact to {worst:0e0} m; ramp slope {a.GroundSlope(10, 15):0.0} deg; materials grass/dirt/rock/sand: "
             + string.Join("/", Enumerable.Range(0, 4).Select(k => t.MaterialIds.Count(m => m == k))));
    }

    // ---- Ray casts ------------------------------------------------------------------------

    private static void TerrainRays()
    {
        var m = LabMap();
        World.SetMap(m);
        var t = m.TerrainCollision!;

        // Straight down: hits exactly the ground.
        var down = World.RaycastWorld(new Vector3(5.3f, 20, -7.7f), -Vector3.UnitY, 100);
        if (MathF.Abs(down - (20 - m.GroundHeight(5.3f, -7.7f))) > 1e-3f)
            Fail($"downward ray hit at {down:0.000}, ground is {20 - m.GroundHeight(5.3f, -7.7f):0.000} below");
        // Into the hill's west flank at 3 m: the flank is 3 m high about 7 m from the centre.
        var hill = World.RaycastWorld(new Vector3(-40, 3, -20), Vector3.UnitX, 100);
        if (MathF.Abs(hill - 13f) > 0.4f)
            Fail($"ray into the hill hit at {hill:0.00} m (expected about 13)");
        // Along the valley floor, below the rim but above the floor: no hit. Across the valley: hits its side.
        var along = World.RaycastWorld(new Vector3(24, -2.0f, -30), Vector3.UnitX, 10);
        var across = World.RaycastWorld(new Vector3(29, -2.0f, -30), Vector3.UnitZ, 10);
        if (along < 10)
            Fail($"ray along the valley floor hit the ground at {along:0.00} m");
        if (across >= 10 || across < 1)
            Fail($"ray across the valley didn't hit its side ({across:0.00} m)");
        // Shooting from above the highest point upwards or level: never hits.
        if (World.RaycastWorld(new Vector3(0, 30, 0), Vector3.Normalize(new Vector3(1, 0.1f, 0.3f)), 200) < 200)
            Fail("a ray above all the terrain hit it");

        // Random rays against a brute-force march (2 cm steps).
        var rng = new Random(5);
        var worst = 0f;
        var mismatches = 0;
        for (var n = 0; n < 400; n++)
        {
            var o = new Vector3((float)rng.NextDouble() * 70 - 35, (float)rng.NextDouble() * 9 - 1, (float)rng.NextDouble() * 70 - 35);
            if (o.Y < m.GroundHeight(o.X, o.Z) + 0.05f)
                o.Y = m.GroundHeight(o.X, o.Z) + 0.05f + (float)rng.NextDouble() * 3;
            var d = Vector3.Normalize(new Vector3((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 1.2f - 0.9f, (float)rng.NextDouble() * 2 - 1));
            const float max = 40;
            var got = t.Raycast(o, d, max);
            var want = max;
            for (var s = 0.02f; s <= max; s += 0.02f)
            {
                var p = o + d * s;
                if (p.Y <= t.HeightAt(p.X, p.Z))
                {
                    want = s;
                    break;
                }
            }
            var err = MathF.Abs(got - want);
            if (err > 0.05f)
                mismatches++;
            worst = MathF.Max(worst, err);
        }
        if (mismatches > 0)
            Fail($"{mismatches} of 400 terrain ray casts disagree with a brute-force march (worst {worst:0.000} m)");

        // Cost: bullets, bot sight lines and explosions all use this.
        var sw = Stopwatch.StartNew();
        var hits = 0;
        for (var n = 0; n < 20000; n++)
        {
            var o = new Vector3((float)rng.NextDouble() * 70 - 35, 1.6f + (float)rng.NextDouble() * 4, (float)rng.NextDouble() * 70 - 35);
            var a = (float)rng.NextDouble() * MathF.Tau;
            if (t.Raycast(o, Vector3.Normalize(new Vector3(MathF.Cos(a), (float)rng.NextDouble() * 0.2f - 0.12f, MathF.Sin(a))), 200) < 200)
                hits++;
        }
        var us = sw.Elapsed.TotalMilliseconds * 1000 / 20000;
        Line($"rays: down {down:0.000} m, hill {hill:0.00} m, valley along {along:0.0} / across {across:0.00} m; 400 random rays within {worst:0.000} m of a brute-force march; "
             + $"{us:0.00} us per 200 m ray ({hits} of 20000 hit)");
        if (us > 50)
            Fail($"terrain ray casts are slow ({us:0.0} us each)");
    }

    // ---- Movement -------------------------------------------------------------------------

    private static MoveState Walk(Vector3 start, float yaw, int ticks, Buttons buttons, Func<int, Buttons>? extra = null)
    {
        var s = new MoveState { Position = start, Grounded = true };
        for (var i = 0; i < ticks; i++)
            World.Simulate(ref s, new InputCmd { Buttons = buttons | (extra?.Invoke(i) ?? Buttons.None), Yaw = yaw, SpeedPct = 100 });
        return s;
    }

    private static void TerrainMovement()
    {
        var m = LabMap();
        World.SetMap(m);
        const float north = 0; // yaw 0 faces -Z

        // Up the 11 degree ramp: from its foot, 3 s of walking north.
        var start = new Vector3(10, m.GroundHeight(10, 29), 29);
        var up = Walk(start, north, 180, Buttons.Forward);
        Line($"ramp: walked from z 29 (y {start.Y:0.00}) to z {up.Position.Z:0.0}, y {up.Position.Y:0.00} (ground {m.GroundHeight(up.Position.X, up.Position.Z):0.00}), grounded {up.Grounded}");
        if (up.Position.Z > 14 || up.Position.Y < 3 || MathF.Abs(up.Position.Y - m.GroundHeight(up.Position.X, up.Position.Z)) > 0.05f || !up.Grounded)
            Fail("couldn't walk up a gentle slope");

        // And back down it: stays on the ground (no hopping) the whole way.
        var s = up;
        var airborne = 0;
        for (var i = 0; i < 150; i++)
        {
            World.Simulate(ref s, new InputCmd { Buttons = Buttons.Back | Buttons.Sprint, Yaw = north, SpeedPct = 100 });
            if (!s.Grounded)
                airborne++;
        }
        if (airborne > 3)
            Fail($"walking down a slope left the ground on {airborne} of 150 ticks");

        // Into the cliff face (6 m, about 76 degrees), walking and jumping: never gets up it.
        var foot = new Vector3(-20, m.GroundHeight(-20, 16), 16);
        var walk = Walk(foot, north, 240, Buttons.Forward | Buttons.Sprint);
        var hop = Walk(foot, north, 300, Buttons.Forward, i => i % 40 == 0 ? Buttons.Jump : Buttons.None);
        Line($"cliff: walking stopped at z {walk.Position.Z:0.00} y {walk.Position.Y:0.00}; jumping at z {hop.Position.Z:0.00} y {hop.Position.Y:0.00}");
        if (walk.Position.Z < 10.6f || walk.Position.Y > 1.5f || hop.Position.Z < 10.3f || hop.Position.Y > 3f)
            Fail("a cliff didn't block the player");

        // Standing on the cliff face: slides down it.
        var onFace = new Vector3(-20, m.GroundHeight(-20, 10.6f), 10.6f);
        var slid = Walk(onFace, north, 120, Buttons.None);
        if (slid.Position.Y > onFace.Y - 1 || slid.Position.Z < 11.0f)
            Fail($"didn't slide down a cliff face (from y {onFace.Y:0.00} to {slid.Position.Y:0.00}, z {slid.Position.Z:0.00})");

        // Diagonally into the causeway's steep side (cliff test for the axis-by-axis mover).
        var side = Walk(new Vector3(3, m.GroundHeight(3, 23), 23), MathF.PI * 0.25f, 200, Buttons.Forward);
        if (side.Position.Y > 1.5f)
            Fail($"walked diagonally up a 56 degree bank (to y {side.Position.Y:0.00})");

        // Mantle onto the 1.4 m plateau bank (too steep to walk up, low enough to climb).
        var bank = new Vector3(25, m.GroundHeight(25, 31), 31);
        var mantled = false;
        var ms = new MoveState { Position = bank, Grounded = true };
        for (var i = 0; i < 120; i++)
        {
            World.Simulate(ref ms, new InputCmd { Buttons = Buttons.Forward | (i is > 30 and < 60 ? Buttons.Jump : Buttons.None), Yaw = north, SpeedPct = 100 });
            mantled |= ms.MantleTicks > 0;
        }
        var walkOnly = Walk(bank, north, 120, Buttons.Forward);
        Line($"bank: walking stops at y {walkOnly.Position.Y:0.00}; mantle {(mantled ? "yes" : "no")}, ended at y {ms.Position.Y:0.00} z {ms.Position.Z:0.0}");
        if (walkOnly.Position.Y > 0.6f)
            Fail("walked straight up a 1.4 m bank");
        if (!mantled || ms.Position.Y < 1.3f)
            Fail("couldn't mantle onto a 1.4 m terrain bank");

        // Determinism (client prediction replays these ticks): the same inputs give the same result.
        var again = Walk(foot, north, 300, Buttons.Forward, i => i % 40 == 0 ? Buttons.Jump : Buttons.None);
        if (again.Position != hop.Position || again.Velocity != hop.Velocity)
            Fail("terrain movement isn't deterministic");

        // Never below the ground, wherever you're dropped.
        var rng = new Random(3);
        var under = 0;
        for (var n = 0; n < 60; n++)
        {
            var p = new Vector3((float)rng.NextDouble() * 76 - 38, 9, (float)rng.NextDouble() * 76 - 38);
            var yaw = (float)rng.NextDouble() * MathF.Tau;
            var r = Walk(p, yaw, 240, Buttons.Forward | Buttons.Sprint, i => i % 50 == 0 ? Buttons.Jump : Buttons.None);
            if (r.Position.Y < m.GroundHeight(r.Position.X, r.Position.Z) - 0.01f)
                under++;
        }
        if (under > 0)
            Fail($"{under} of 60 random walks ended below the ground");

        // Walkable straight lines (for bot navigation).
        bool Can(float x0, float z0, float x1, float z1) =>
            World.CanWalkStraight(new Vector3(x0, m.GroundHeight(x0, z0), z0), new Vector3(x1, m.GroundHeight(x1, z1), z1));
        var acrossCliff = Can(-20, 18, -20, 2);
        var flat = Can(20, 32, 32, 36);
        var upRamp = Can(10, 28, 10, 4);
        var overBank = Can(2, 15, 18, 15);
        var overHill = Can(-36, -20, -4, -20);
        Line($"walkable: across the cliff {acrossCliff}, flat {flat}, up the ramp {upRamp}, over the causeway side {overBank}, over the hill {overHill}");
        if (acrossCliff || !flat || !upRamp || overBank || !overHill)
            Fail("CanWalkStraight got a terrain case wrong");
    }

    // ---- Throwables -----------------------------------------------------------------------

    private static void TerrainThrowables()
    {
        var m = LabMap();
        World.SetMap(m);
        // Dropped onto the hill's 30-degree flank with a little sideways speed: bounces, rolls, stops.
        var x = -27f;
        var z = -20f;
        var p = new Projectile { Pos = new Vector3(x, m.GroundHeight(x, z) + 0.6f, z), Vel = new Vector3(0.5f, 0, 0.3f) };
        var lastSecond = Vector3.Zero;
        for (var i = 0; i < 6 * World.TickRate; i++)
        {
            Throwables.Step(p, World.TickDt);
            if (i == 5 * World.TickRate)
                lastSecond = p.Pos;
        }
        var ground = m.GroundHeight(p.Pos.X, p.Pos.Z) + Throwables.ProjectileRadius;
        var slope = m.GroundSlope(p.Pos.X, p.Pos.Z);
        Line($"grenade on a slope: came to rest at ({p.Pos.X:0.00}, {p.Pos.Y:0.00}, {p.Pos.Z:0.00}) on {slope:0} deg ground, speed {p.Vel.Length():0.000}, moved {Vector3.Distance(lastSecond, p.Pos):0.000} m in the last second");
        if (p.Vel.Length() > 0.05f || Vector3.Distance(lastSecond, p.Pos) > 0.05f)
            Fail("a grenade didn't come to rest on a slope");
        if (MathF.Abs(p.Pos.Y - ground) > 0.05f)
            Fail($"the grenade isn't lying on the ground ({p.Pos.Y:0.00} vs {ground:0.00})");
        if (slope < 15)
            Fail("the grenade rolled all the way off the slope");

        // Thrown hard into a slope: bounces off it (now moving away from the surface, and slower).
        var q = new Projectile { Pos = new Vector3(-40, 4, -20), Vel = new Vector3(15, -3, 0) };
        var bounced = false;
        for (var i = 0; i < 120 && !bounced; i++)
        {
            var before = q.Vel.Length();
            if (Throwables.Step(q, World.TickDt))
                bounced = Vector3.Dot(q.Vel, m.GroundNormal(q.Pos.X, q.Pos.Z)) > 0.5f && q.Vel.Length() < before;
        }
        if (!bounced)
            Fail("a grenade thrown at a hillside didn't bounce off it");
    }

    // ---- Boxes and spawns on terrain ------------------------------------------------------

    private static void TerrainPlacement()
    {
        var m = LabMap();
        World.SetMap(m);
        IEnumerable<Box> ByColor(Vector3 c) => m.Solid.Where((_, i) => m.Colors[i] == c);
        var red = ByColor(new Vector3(1, 0, 0)).Single();
        var green = ByColor(new Vector3(0, 1, 0)).Single();
        var blue = ByColor(new Vector3(0, 0, 1)).Single();
        var cover = ByColor(new Vector3(1, 1, 0)).ToList();
        var greenWant = m.LowestGroundUnder(green.Min.X, green.Min.Z, green.Max.X, green.Max.Z);
        Line($"placement: flat prop bottom {red.Min.Y:0.000}, prop on the ramp {green.Min.Y:0.000} (lowest ground under it {greenWant:0.000}), "
             + $"world-anchored prop {blue.Min.Y:0.000}, {cover.Count} cover pieces draped");
        if (MathF.Abs(red.Min.Y - m.LowestGroundUnder(red.Min.X, red.Min.Z, red.Max.X, red.Max.Z)) > 1e-4f || MathF.Abs(red.Max.Y - red.Min.Y - 1) > 1e-4f)
            Fail("a prop on flat terrain isn't sitting on the ground");
        if (MathF.Abs(green.Min.Y - greenWant) > 1e-4f || greenWant < 1)
            Fail("a prop on a slope isn't on the lowest ground under it");
        if (MathF.Abs(blue.Min.Y - 10) > 1e-4f)
            Fail("\"anchor\": \"world\" didn't keep the absolute height");
        if (cover.Count < 3 || cover.Any(b => MathF.Abs(b.Min.Y - m.LowestGroundUnder(b.Min.X, b.Min.Z, b.Max.X, b.Max.Z)) > 1e-4f)
            || cover.Select(b => b.Min.Y).Distinct().Count() < 3)
            Fail("CoverLine pieces weren't draped over the ramp one by one");
        if (m.GroundIndex != -1 || m.Solid.Any(b => b.Min.Y <= -1.001f && b.Max.Y <= 0.001f && b.Half.X > 40))
            Fail("the Ground op still made a floor slab on a terrain map");
        var walls = m.Solid.Where((_, i) => m.Colors[i] == new Vector3(128 / 255f, 128 / 255f, 128 / 255f)).ToList();
        if (walls.Count != 4 || walls.Any(w => w.Min.Y > m.GroundHeight(w.Center.X, w.Center.Z) - 0.99f || w.Max.Y < m.GroundHeight(w.Center.X, w.Center.Z) + 3.99f))
            Fail("Perimeter walls don't reach from under the terrain to their height above it");

        // Spawns and waypoints: on the ground, never under it, never on ground too steep to stand on.
        var bad = m.Spawns.Concat(m.Waypoints).Count(p => MathF.Abs(p.Y - m.GroundHeight(p.X, p.Z)) > 0.01f || m.IsSteep(p.X, p.Z) || m.IsBlocked(p));
        Line($"{m.Spawns.Count} spawns and {m.Waypoints.Count} waypoints, {bad} off the ground or on steep ground");
        if (bad > 0)
            Fail($"{bad} spawn(s)/waypoint(s) aren't on walkable ground");
        // A spawn asked for on the cliff face gets moved off it.
        var steep = MapFile.Load(TerrainLabJson.Replace("{ \"op\": \"RingSpawns\", \"count\": 8, \"frac\": 0.7 },",
            "{ \"op\": \"RingSpawns\", \"count\": 8, \"frac\": 0.7 }, { \"op\": \"Spawn\", \"x\": -20, \"z\": 10.7 },"), "steep spawn");
        steep.Validate();
        var moved = steep.Spawns[^1];
        if (steep.IsSteep(moved.X, moved.Z) || MathF.Abs(moved.Y - steep.GroundHeight(moved.X, moved.Z)) > 0.01f)
            Fail($"a spawn on a cliff face wasn't moved to walkable ground ({moved})");
    }

    // ---- Files and fingerprints -----------------------------------------------------------

    private static void TerrainFiles()
    {
        var m = LabMap();
        var json = MapFile.ToJson(m);
        var copy = MapFile.Load(json, "lab round trip");
        copy.Validate();
        if (MapFile.ToJson(copy) != json || copy.Terrain!.Hash != m.Terrain!.Hash || copy.ContentHash != m.ContentHash
            || !copy.Solid.SequenceEqual(m.Solid) || !copy.Spawns.SequenceEqual(m.Spawns) || !copy.Waypoints.SequenceEqual(m.Waypoints))
            Fail("a terrain map changed in a file round trip");
        if (!json.Contains("\"anchor\": \"world\"", StringComparison.Ordinal))
            Fail("an op's anchor wasn't written back to the file");

        var changed = MapFile.Load(TerrainLabJson.Replace("\"height\": 1.4", "\"height\": 1.5"), "lab changed");
        Line($"fingerprints: lab {m.ContentHash:x8}, round trip {copy.ContentHash:x8}, one plateau 10 cm higher {changed.ContentHash:x8}");
        if (changed.ContentHash == m.ContentHash || changed.Terrain!.Hash == m.Terrain.Hash)
            Fail("changing the terrain didn't change the map's fingerprint");

        // Flat maps: identical with "terrain": null, so old maps keep their fingerprints.
        var proto = Maps.Find("Proto Box");
        if (proto?.SourceFile != null)
        {
            var text = File.ReadAllText(Path.Combine(Maps.Folder, proto.SourceFile));
            var withNull = MapFile.Load(text.Replace("\"ops\":", "\"terrain\": null, \"ops\":"), "proto null terrain");
            if (withNull.ContentHash != MapFile.Load(text, "proto").ContentHash || withNull.HasTerrain || proto.HasTerrain)
                Fail("\"terrain\": null changed a flat map");
        }

        // Bad terrain gives a readable error, not a crash.
        foreach (var (bad, expect) in new[]
                 {
                     ("{ \"type\": \"hil\", \"x\": 0 }", "unknown"),
                     ("{ \"type\": \"hill\", \"x\": 0, \"z\": 0, \"radius\": 5 }", "needs \"height\""),
                     ("{ \"type\": \"hill\", \"x\": 0, \"z\": 0, \"radius\": -5, \"height\": 2 }", "above 0"),
                     ("{ \"type\": \"plateau\", \"x\": 0, \"z\": 0, \"radius\": 5, \"w\": 4, \"d\": 4, \"height\": 2 }", "either"),
                     ("{ \"type\": \"ramp\", \"points\": [[0, 0]] }", "at least 2"),
                     ("{ \"type\": \"hill\", \"x\": 0, \"z\": 0, \"radius\": 5, \"height\": 2, \"falloff\": \"spiky\" }", "smooth"),
                     ("{ \"type\": \"hill\", \"x\": 0, \"z\": 0, \"radius\": 5, \"height\": 2, \"colour\": 1 }", "no parameter"),
                 })
        {
            var file = $$"""{ "name": "Bad", "halfX": 20, "halfZ": 20, "terrain": { "features": [ {{bad}} ] }, "ops": [ { "op": "RingSpawns", "count": 8, "frac": 0.5 } ] }""";
            try
            {
                MapFile.Load(file, "bad.json");
                Fail($"a broken terrain loaded without complaint (expected '{expect}')");
            }
            catch (MapFileException ex) when (ex.Message.Contains(expect, StringComparison.OrdinalIgnoreCase))
            {
            }
            catch (Exception ex)
            {
                Fail($"broken terrain gave the wrong error: {ex.Message}");
            }
        }
        try
        {
            MapFile.Load("""{ "name": "Bad", "halfX": 20, "halfZ": 20, "ops": [ { "op": "Prop", "x": 0, "z": 0, "w": 1, "h": 1, "d": 1, "color": "#ffffff", "anchor": "sky" }, { "op": "Spawn", "x": 1, "z": 1 } ] }""", "bad anchor");
            Fail("a bad \"anchor\" loaded without complaint");
        }
        catch (MapFileException ex) when (ex.Message.Contains("anchor", StringComparison.Ordinal))
        {
        }
    }

    // ---- The demo map ---------------------------------------------------------------------

    private static void TerrainDemoMap()
    {
        var demo = Maps.Find("Terrain Test");
        if (demo == null)
        {
            if (Directory.Exists(Maps.Folder))
                Fail("Maps/Dev/TerrainTest.json (the terrain demo) didn't load");
            return;
        }
        var t = demo.Terrain!;
        Line($"demo '{demo.Name}': {t.CountX} x {t.CountZ} samples, heights {t.MinHeight:0.00} to {t.MaxHeight:0.00} m, fingerprint {t.Hash:x8}, "
             + $"{demo.Solid.Count} boxes, {demo.Spawns.Count} spawns, {demo.Waypoints.Count} waypoints");
        var badSpawns = demo.Spawns.Count(p => p.Y < demo.GroundHeight(p.X, p.Z) - 0.01f || demo.IsBlocked(p) || (MathF.Abs(p.Y - demo.GroundHeight(p.X, p.Z)) < 0.05f && demo.IsSteep(p.X, p.Z)));
        if (badSpawns > 0)
            Fail($"{badSpawns} demo spawn(s) are under the ground, inside geometry or on steep ground");
        if (demo.Spawns.Count < 8)
            Fail("the demo map has fewer than 8 spawns");

        // A full bot match: nobody stuck, nobody below the ground.
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
            bots: Game.MaxPlayers - 1, botSkill: BotDifficulty.Normal, mapId: demo.Id);
        game.MapVoteEnabled = false;
        const double dt = 1.0 / World.TickRate;
        var now = 0.0;
        var travel = new Dictionary<byte, float>();
        var last = new Dictionary<byte, Vector3>();
        var below = 0;
        var worstTick = 0.0;
        for (var i = 0; i < 90 * World.TickRate; i++)
        {
            now += dt;
            var t0 = Stopwatch.GetTimestamp();
            game.Update(now, dt);
            if (i > 60)
                worstTick = Math.Max(worstTick, Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
            foreach (var p in game.Players.Values)
                if (p.Alive && p.Position.Y < demo.GroundHeight(p.Position.X, p.Position.Z) - 0.05f)
                    below++;
            if (i % 60 != 0)
                continue;
            foreach (var p in game.Players.Values.Where(p => p.Bot != null && p.Alive))
            {
                if (last.TryGetValue(p.Id, out var lp))
                    travel[p.Id] = travel.GetValueOrDefault(p.Id) + Vector3.Distance(lp, p.Position);
                last[p.Id] = p.Position;
            }
        }
        var stuck = travel.Count(kv => kv.Value < 15f);
        var kills = game.Players.Values.Sum(p => (int)p.Kills);
        Line($"demo bot match: kills {kills}, avg bot travel {(travel.Count > 0 ? travel.Values.Average() : 0):0} m, stuck bots {stuck}, below ground {below} player-ticks, worst tick {worstTick:0.00} ms");
        if (stuck > 0)
            Fail($"{stuck} bot(s) got stuck on the terrain demo");
        if (below > 0)
            Fail("players ended up below the terrain");
    }
}
