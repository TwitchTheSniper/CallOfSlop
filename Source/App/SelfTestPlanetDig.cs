using System.Diagnostics;
using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The "Planetoid digging" self-test section (Source/Planets/PlanetEdits.cs, docs/planets/PLANETS.md "Digging"): craters
/// at the pole, on a face seam and on a cube corner are one round crater across the faces; rays land on what collision
/// has, and no ray finds a gap into the ground; a player stands in a crater and walks round in it without sinking; fills,
/// measures and burying work along "up"; a second copy given the log over the wire ends with the same columns; the
/// headless drawing masks the dug cells and builds their blocks; and a real host, a client and a late joiner end with the
/// same ground. Short scenarios only.
/// </summary>
public static partial class SelfTest
{
    private static void CheckPlanetDig()
    {
        var previous = World.Map;
        try
        {
            var map = Maps.Find("Planetoid") is { Planetoid: not null } dev ? dev : Maps.All.FirstOrDefault(m => m.Planetoid != null); // (the Dev map these checks were made on, not the first world of the Planetoids pack)
            if (map?.Planetoid is not { } pl)
            {
                Fail("planetoid digging: no map with a planetoid (Maps/Dev/Planetoid.json)");
                return;
            }
            map.RestoreProps();
            World.SetMap(map);
            var e = pl.Edits;
            Line($"planetoid digging: {e.Sub} x {e.Sub} columns a cell ({e.Size:0.000} m), bedrock {e.Bedrock:0} m, {e.ChunksPerSide} chunks a face side");
            var spots = PdSpots(pl);
            foreach (var (name, g, dir) in spots)
                PdCrater(pl, name, g, dir);
            PdRays(pl, spots);
            PdWalk(pl, spots);
            PdFill(pl, spots[0].Ground, spots[0].Up);
            PdCopy(map, pl);
            PdDraw(pl, spots);
            map.RestoreProps();
            if (e.Any || e.Log.Count > 0 || MathF.Abs(pl.Altitude(spots[0].Ground)) > 0.01f)
                Fail("planetoid digging: restoring the map didn't fill the ground in again");
            PdLoopback(map);
            PdDrill(map);
        }
        finally
        {
            World.Map.RestoreProps();
            World.SetMap(previous);
        }
    }

    /// <summary>Where the craters go: near the pole (off its pillar), on the +X/+Z seam and on the (1, 1, -1) cube corner.</summary>
    private static (string Name, Vector3 Ground, Vector3 Up)[] PdSpots(Planetoid pl)
    {
        var dirs = new (string, Vector3)[]
        {
            ("the pole", PlanetoidDir(89.8, 30)),
            ("a face seam", Vector3.Normalize(new Vector3(1, 0.3f, 1))),
            ("a cube corner", Vector3.Normalize(new Vector3(1, 1, -1))),
        };
        return dirs.Select(d => (d.Item1, pl.Center + d.Item2 * pl.SurfaceRadius(d.Item2, out _), d.Item2)).ToArray();
    }

    /// <summary>Two tangent axes at "up".</summary>
    private static void PdAxes(Vector3 up, out Vector3 a, out Vector3 b)
    {
        a = Vector3.Normalize(Vector3.Cross(up, MathF.Abs(up.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
        b = Vector3.Cross(up, a);
    }

    /// <summary>A crater of radius 3 at a spot: as deep as it should be in the middle, round (the same depth all round, across faces).</summary>
    private static void PdCrater(Planetoid pl, string name, Vector3 g, Vector3 up)
    {
        var e = pl.Edits;
        const float r = 3f;
        var before = new float[16];
        PdAxes(up, out var ta, out var tb);
        for (var k = 0; k < 16; k++)
        {
            var p = g + (ta * MathF.Cos(k * MathF.Tau / 16) + tb * MathF.Sin(k * MathF.Tau / 16)) * 2f;
            before[k] = (pl.GroundUnder(p) - pl.Center).Length();
        }
        var sw = Stopwatch.StartNew();
        var changed = e.Apply(new DigOp(DigKind.Crater, g, r).Quantised);
        var applyMs = sw.Elapsed.TotalMilliseconds;
        var phys = World.Physics;
        var ground = new QueryFilter(QueryLayers.Terrain);
        var from = g + up * 20f;
        var hit = phys.Raycast(from, -up, 60, ground, out var h);
        var depth = hit ? h.Distance - 20f : float.NaN;
        float lo = float.MaxValue, hi = float.MinValue;
        var faces = new HashSet<int>();
        for (var k = 0; k < 16; k++)
        {
            var p = g + (ta * MathF.Cos(k * MathF.Tau / 16) + tb * MathF.Sin(k * MathF.Tau / 16)) * 2f;
            var d = before[k] - (pl.GroundUnder(p) - pl.Center).Length();
            lo = MathF.Min(lo, d);
            hi = MathF.Max(hi, d);
            var rel = p - pl.Center;
            pl.Locate(rel.X, rel.Y, rel.Z, out var f, out _, out _);
            faces.Add(f);
        }
        var want = r * TerrainEdits.CraterDepth;
        var ring = TerrainEdits.CraterDepth * MathF.Sqrt(r * r - 4);
        Line($"planetoid crater at {name}: {changed} columns in {applyMs:0.00} ms on {faces.Count} face(s); middle {depth:0.00} m deep (want {want:0.00}), normal up {(hit ? Vector3.Dot(h.Normal, up) : 0):0.00}; 2 m out {lo:0.00} to {hi:0.00} m deep (want about {ring:0.00})");
        if (!hit || MathF.Abs(depth - want) > 0.1f || Vector3.Dot(h.Normal, up) < 0.95f)
            Fail($"planetoid digging: the crater at {name} isn't as deep as it should be in the middle ({depth:0.00} m, want {want:0.00}; hit {hit})");
        if (lo < ring - 0.45f || hi > ring + 0.45f || hi - lo > 0.6f)
            Fail($"planetoid digging: the crater at {name} isn't round ({lo:0.00} to {hi:0.00} m deep 2 m out, want about {ring:0.00})");
        if (name != "the pole" && faces.Count < 2)
            Fail($"planetoid digging: the crater at {name} was meant to cross a seam but its ring is on one face");
    }

    /// <summary>Rays land on what collision has (the gathered triangles), and no ray finds a gap into the ground.</summary>
    private static void PdRays(Planetoid pl, (string Name, Vector3 Ground, Vector3 Up)[] spots)
    {
        var phys = World.Physics;
        var ground = new QueryFilter(QueryLayers.Terrain);
        var rng = new Random(17);
        var tris = new List<ContactShape>();
        int rays = 0, missed = 0, off = 0, grazes = 0, leaks = 0, probes = 0;
        var worst = 0f;
        var sw = Stopwatch.StartNew();
        foreach (var (name, g, up) in spots)
        {
            tris.Clear();
            pl.Gather(g - new Vector3(12), g + new Vector3(12), tris);
            PdAxes(up, out var ta, out var tb);
            for (var k = 0; k < 400; k++)
            {
                // From the air round the crater at a random slant, at a random point in or near it.
                var target = g + ta * (float)(rng.NextDouble() * 8 - 4) + tb * (float)(rng.NextDouble() * 8 - 4) - up * (float)(rng.NextDouble() * 1.5);
                var side = Vector3.Normalize(ta * (float)(rng.NextDouble() * 2 - 1) + tb * (float)(rng.NextDouble() * 2 - 1));
                var o = target + up * (float)(2 + rng.NextDouble() * 4) + side * (float)(rng.NextDouble() * 5);
                if (pl.Altitude(o) < 0.2f)
                    continue;
                var dir = Vector3.Normalize(target - o);
                var t2 = Brute(o, dir, 20f);
                rays++;
                var got = phys.Raycast(o, dir, 20f, ground, out var h);
                if (t2 < 20f && !got)
                {
                    missed++;
                    continue;
                }
                if (got && t2 < 20f)
                {
                    var err = MathF.Abs(h.Distance - t2);
                    if (err <= 0.03f)
                        worst = MathF.Max(worst, err);
                    if (err > 0.03f)
                    {
                        // A graze: the ray passes within a few millimetres of a rim's edge or a column's corner, where either
                        // answer is right. Moved 3 mm up, down or sideways it agrees, or it's off.
                        var graze = false;
                        foreach (var shift in (ReadOnlySpan<Vector3>)[up, -up, ta, -ta, tb, -tb])
                            graze |= MathF.Abs(h.Distance - Brute(o + shift * 0.003f, dir, 20f)) <= 0.03f;
                        if (graze)
                            grazes++;
                        else
                            off++;
                    }
                }
            }
            // Gaps: a ray from the air to a point well inside the ground always crosses a face first.
            for (var k = 0; k < 300; k++)
            {
                var inside = g + ta * (float)(rng.NextDouble() * 10 - 5) + tb * (float)(rng.NextDouble() * 10 - 5);
                inside = pl.GroundUnder(inside) - up * (float)(0.3 + rng.NextDouble() * 1.5);
                if (pl.Altitude(inside) > -0.25f)
                    continue;
                var side = Vector3.Normalize(ta * (float)(rng.NextDouble() * 2 - 1) + tb * (float)(rng.NextDouble() * 2 - 1));
                var o = inside + up * (float)(3 + rng.NextDouble() * 3) + side * (float)(rng.NextDouble() * 6);
                if (pl.Altitude(o) < 0.2f)
                    continue;
                probes++;
                var len = Vector3.Distance(o, inside);
                if (Brute(o, (inside - o) / len, len) >= len)
                    leaks++;
            }
        }
        var ms = sw.Elapsed.TotalMilliseconds;
        Line($"planetoid dug rays: {rays} slanted rays, {missed} missed what collision has, {off} more than 3 cm off it, {grazes} grazing a rim's edge within 3 mm (else within {worst * 1000:0.0} mm); {probes} rays into the ground, {leaks} found a gap; {ms:0} ms");
        if (missed > 0 || off > 0 || leaks > 0 || rays < 500)
            Fail($"planetoid digging: rays and collision disagree on dug ground ({missed} missed, {off} off, {leaks} gaps into the ground of {probes})");

        float Brute(Vector3 o, Vector3 d, float max)
        {
            var best = max;
            foreach (var t in tris)
                if (Geometry.RayTriangle(o, d, t.A, t.B, t.C, best) is { } hit && hit < best)
                    best = hit;
            return best;
        }
    }

    /// <summary>A player dropped in each crater stands on its floor, then walks and sprints round in it without going into the ground.</summary>
    private static void PdWalk(Planetoid pl, (string Name, Vector3 Ground, Vector3 Up)[] spots)
    {
        foreach (var (name, g, up) in spots)
        {
            var floor = pl.GroundUnder(g);
            var s = new MoveState { Position = floor + up * 0.5f, Grounded = false };
            for (var i = 0; i < 60; i++)
                World.Simulate(ref s, new InputCmd { SpeedPct = 100 });
            var stand = pl.Altitude(s.Position);
            if (!s.Grounded || MathF.Abs(stand) > 0.1f)
            {
                Fail($"planetoid digging: a player doesn't stand in the crater at {name} (grounded {s.Grounded}, {stand:0.000} m off its floor)");
                continue;
            }
            var lowest = float.MaxValue;
            var far = 0f;
            var start = s.Position;
            for (var w = 0; w < 4; w++)
            {
                var yaw = w * MathF.PI / 2 + 0.3f;
                var pitch = 0f;
                var prev = s.Position;
                for (var t = 0; t < 150; t++)
                {
                    GravityFrames.Transport(ref yaw, ref pitch, prev, s.Position);
                    prev = s.Position;
                    var b = Buttons.Forward | Buttons.Sprint | (t % 50 == 25 ? Buttons.Jump : 0);
                    World.Simulate(ref s, new InputCmd { Buttons = b, Yaw = yaw, Pitch = pitch, SpeedPct = 100 });
                    lowest = MathF.Min(lowest, pl.Altitude(s.Position));
                    far = MathF.Max(far, Vector3.Distance(start, s.Position));
                }
            }
            Line($"planetoid crater walk at {name}: stood {stand:+0.000;-0.000} m off the floor, walked as far as {far:0.0} m out, feet never lower than {lowest:+0.000;-0.000} m");
            if (lowest < -0.1f || far < 1.5f)
                Fail($"planetoid digging: walking in the crater at {name} went wrong (lowest {lowest:0.000} m, {far:0.0} m out)");
        }
    }

    /// <summary>A fill puts ground back along "up": measured, buries a player standing there, raises the ground, and digs back out as what it was.</summary>
    private static void PdFill(Planetoid pl, Vector3 g, Vector3 up)
    {
        var e = pl.Edits;
        var floor = pl.GroundUnder(g);
        var gives = DiggingTable.Current.Ground;
        Span<float> v = stackalloc float[(int)GroundKind.COUNT];
        var fill = new DigOp(DigKind.FillStone, floor + up * 0.4f, 0.8f).Quantised;
        var vol = e.Measure(in fill, gives, 3f, v);
        var buries = e.FillTouches(in fill, floor - new Vector3(0.4f), floor + up * 1.8f + new Vector3(0.4f));
        var aside = floor + Vector3.Normalize(Vector3.Cross(up, Vector3.UnitX)) * 2.5f;
        var clear = !e.FillTouches(in fill, aside - new Vector3(0.4f), aside + new Vector3(0.4f) + up * 1.8f);
        var a0 = (pl.GroundUnder(floor + up * 3f) - pl.Center).Length();
        var changed = e.Apply(fill);
        var a1 = (pl.GroundUnder(floor + up * 3f) - pl.Center).Length();
        var scoop = new DigOp(DigKind.Scoop, floor + up * 0.4f, 0.5f).Quantised;
        var back = e.Measure(in scoop, gives, 3f, v);
        var stone = v[(int)GroundKind.Stone];
        var ball = 4f / 3f * MathF.PI * 0.8f * 0.8f * 0.8f;
        Line($"planetoid fill: {vol:0.00} m3 measured (a ball is {ball:0.00}), {changed} columns, the floor rose {a1 - a0:0.00} m; buries a player on it {buries}, one 2.5 m away {!clear}; a scoop of it gives {back:0.00} m3, {stone:0.00} of it stone");
        if (vol < ball * 0.5f || vol > ball * 1.6f || a1 - a0 < 0.6f || !buries || !clear || back <= 0 || stone < back * 0.9f)
            Fail($"planetoid digging: a fill on the planetoid went wrong ({vol:0.00} m3, rose {a1 - a0:0.00} m, buries {buries}, clear {clear}, stone {stone:0.00} of {back:0.00})");
        if (e.AboveBuilt(floor + up * 5f) is var above && (above < 2f || above > 5f))
            Fail($"planetoid digging: AboveBuilt reads {above:0.00} m 5 m over a crater's floor (the surface as built is about 1.65 m above that floor)");
    }

    /// <summary>A fresh copy of the map's planetoid, loaded from its file (null, with a failure, when it can't be found).</summary>
    private static Planetoid? PdCopyOf(Map map)
    {
        var rel = map.SourceFile ?? "";
        foreach (var file in new[] { rel, Path.Combine(AppContext.BaseDirectory, "Maps", rel), Path.Combine(AppContext.BaseDirectory, rel) })
            if (rel.Length > 0 && File.Exists(file))
                return MapFile.Load(File.ReadAllText(file), rel).Planetoid;
        Fail($"planetoid digging: can't find the map's file ({rel}) to load a copy of the planetoid");
        return null;
    }

    /// <summary>A second copy of the planetoid given the log over the wire, as a joiner gets it, has the same columns bit for bit.</summary>
    private static void PdCopy(Map map, Planetoid pl)
    {
        if (PdCopyOf(map) is not { } copy)
            return;
        var w = new PacketWriter();
        var bytes = 0;
        var packets = Game.WriteTerrainDigState(w, pl.Edits, pw =>
        {
            bytes += pw.Span.Length;
            var r = new PacketReader(pw.Span[1..]);
            Game.ApplyTerrainDig(copy.Edits, ref r);
        });
        var a = pl.Edits.Digest();
        var b = copy.Edits.Digest();
        Line($"planetoid dug copy: {pl.Edits.Log.Count} ops in {packets} packets ({bytes} bytes), {pl.Edits.TouchedCells} cells; digests {a:x16} and {b:x16}");
        if (a != b || copy.Edits.Log.Count != pl.Edits.Log.Count)
            Fail("planetoid digging: a copy given the same ops over the wire has different ground");
    }

    /// <summary>The headless drawing: level-0 pages over the craters get a mask, and the dug chunks get blocks.</summary>
    private static void PdDraw(Planetoid pl, (string Name, Vector3 Ground, Vector3 Up)[] spots)
    {
        var r = new PlanetRenderer(headless: true);
        r.Reset(pl);
        foreach (var (name, g, up) in spots)
        {
            var eye = g + up * 4f;
            PdAxes(up, out var ta, out _);
            var view = Matrix4x4.CreateLookAt(eye, g + ta * 3f, up);
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(1.1f, 16f / 9f, 0.05f, 30000f);
            Span<Vector4> planes = stackalloc Vector4[6];
            TerrainRenderer.FrustumPlanes(view * proj, planes);
            for (var k = 0; k < 400; k++)
            {
                r.Update(eye, planes);
                if (r.Settled && r.DugSettled)
                    break;
                Thread.Sleep(5);
            }
            r.Update(eye, planes);
            var (masked, chunks, triangles) = r.DugStats;
            Line($"planetoid dug drawing at {name}: {masked} level-0 pages masked, {chunks} chunks of blocks, {triangles} triangles");
            if (masked == 0 || chunks == 0 || triangles == 0)
                Fail($"planetoid digging: the drawing at {name} has no dug mask or no blocks ({masked} pages masked, {chunks} chunks)");
        }
        r.Reset(null);
    }

    /// <summary>A real host and a client in Sandbox on the planetoid: a blast digs, the client and a late joiner end with the same ground; off with Destruction off.</summary>
    private static void PdLoopback(Map map)
    {
        if (PdCopyOf(map) is not { } mirrorA || PdCopyOf(map) is not { } mirrorB)
            return;
        var pl = map.Planetoid!;
        const ushort port = 26311;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        map.RestoreProps();
        World.SetMap(map);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        a.PlanetDigMirror = mirrorA; // (peers in one process share the map: the client digs a copy of its own)
        Pump(server, [a], 1.5, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            Pump(server, [a], 0.25, ref now, clock);
        Pump(server, [a], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.TerrainDigLive)
        {
            Fail($"planetoid digging loopback: the client didn't join or digging is off in Sandbox on the planetoid (live {server.TerrainDigLive})");
            return;
        }
        var spots = PdSpots(pl);
        foreach (var (_, g, _) in spots)
            server.BlastCrater(g, 6f);
        server.Dig(new DigOp(DigKind.Scoop, pl.GroundUnder(spots[0].Ground), 0.8f)); // (on the crater's floor)
        Pump(server, [a], 1.0, ref now, clock);
        var ops = server.DigOps;
        var host = pl.Edits.Digest(); // (now: peers in one process share the map, and a joiner's map load restores it)
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        b.PlanetDigMirror = mirrorB;
        Pump(server, [a, b], 2.0, ref now, clock);
        var da = a.PlanetDigMirror!.Edits.Digest();
        var db = b.PlanetDigMirror!.Edits.Digest();
        var before = pl.Edits.Log.Count;
        server.DestructionEnabled = false;
        server.BlastCrater(spots[1].Ground, 6f);
        server.DestructionEnabled = true;
        var offOps = pl.Edits.Log.Count - before;
        Line($"planetoid digging loopback ({clock.Elapsed.TotalSeconds:0.0} s): {ops} ops on the host, client read {a.DigOpsRead}, late joiner {b.DigOpsRead}; ground the same: client {da == host}, joiner {db == host}; {offOps} dug with Destruction off");
        if (ops != 4 || a.DigOpsRead != ops || b.DigOpsRead != ops || da != host || db != host)
            Fail($"planetoid digging loopback: the peers' ground differs ({ops} ops, client read {a.DigOpsRead}, joiner {b.DigOpsRead}, digests equal {da == host}/{db == host})");
        if (offOps != 0)
            Fail("planetoid digging loopback: the ground was dug with Destruction off");
        map.RestoreProps();
    }

    /// <summary>A look at a world point in the gravity frame at the eye (yaw and pitch are the local ones on a planetoid).</summary>
    private static void PdLook(Game g, Player me, Vector3 at)
    {
        var local = GravityFrames.ToLocal(me.RenderEye, Vector3.Normalize(at - me.RenderEye));
        g.LookPitch = MathF.Asin(Math.Clamp(local.Y, -1f, 1f));
        g.LookYaw = MathF.Atan2(local.X, -local.Z);
    }

    /// <summary>
    /// The ground drill's real input on the equator, where "up" is +X: a click digs one scoop, a held RMB fills once, and a
    /// sweep along the ground (north, so a flat x-z spacing would see no movement) lays a line.
    /// </summary>
    private static void PdDrill(Map map)
    {
        var pl = map.Planetoid!;
        map.RestoreProps();
        World.SetMap(map);
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, map.Id);
        if (SpawnLocalSafely(g, ref now) is { } notReady)
        {
            Fail($"planetoid digging: {notReady}");
            return;
        }
        var me = g.Local!;
        Program.SweepRelease(g);
        void Frame(ReadOnlySpan<GameAction> keys)
        {
            Program.SweepFrame(g, now, keys);
            now += 1.0 / 60;
            g.Update(now, 1.0 / 60);
        }
        g.SandboxHands.Hand(me.Id).Tool = ConstructTool.GroundDrill;
        var up = PlanetoidDir(0, 90);
        GravityFrames.Frame(pl.Center + up * (float)pl.Radius, out _, out _, out var north);
        var stand = pl.GroundUnder(pl.Center + up * (float)(pl.Radius + 50));
        me.Teleport(stand + up * 0.05f);
        Run(g, ref now, 0.5);
        var inv = g.SandboxInventory(me.Id);
        var dirtId = Game.MaterialOf(GroundKind.Dirt);
        inv.Materials[dirtId] = 0;
        var east = Vector3.Normalize(Vector3.Cross(north, up));
        var target = pl.GroundUnder(me.Position + east * 2.2f);
        PdLook(g, me, target);
        Frame([]);
        var ops0 = g.DigOps;
        Frame([GameAction.Fire]);
        Frame([]);
        var click = g.DigOps - ops0;
        var dug = inv.Materials[dirtId];
        Run(g, ref now, 0.4);
        inv.Materials[dirtId] = 500;
        var fillAt = pl.GroundUnder(me.Position - east * 2.2f);
        PdLook(g, me, fillAt);
        Frame([]);
        var before = g.DigOps;
        for (var k = 0; k < 120; k++)
            Frame([GameAction.Aim]);
        Frame([]);
        var held = g.DigOps - before;
        Run(g, ref now, 0.4);
        before = g.DigOps;
        for (var k = 0; k < 7; k++)
        {
            PdLook(g, me, pl.GroundUnder(me.Position - east * 2.5f + north * (k * 1.2f - 3.6f)));
            for (var f = 0; f < 25; f++)
                Frame([GameAction.Aim]);
        }
        Frame([]);
        var swept = g.DigOps - before;
        Line($"planetoid ground drill on the equator: a click made {click} op and gave {dug} dirt; RMB held still {held} fill; a sweep north over 7 spots {swept} fills; HUD \"{g.SandboxLookText}\"");
        if (click != 1 || dug <= 0 || held != 1 || swept < 3 || swept > 7)
            Fail($"planetoid digging: the ground drill on the planetoid went wrong (click {click} op, {dug} dirt, held {held} fills, sweep {swept})");
        map.RestoreProps();
    }
}
