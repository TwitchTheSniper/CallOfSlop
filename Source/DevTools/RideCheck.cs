using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--ride-check [BLUEPRINT,...] [--speed V] [--turn DEG/S] [--space]</c>: walks a player round a moving ship headless and
/// reports every tick their frame changed or lapsed (docs/sandbox/design/construct-frame-notes.md). Each blueprint is built on
/// the Construct Yard, lifted clear of the ground and moved as the physics would (its pose, velocity and turn set each tick);
/// a player is set down on standing spots found on its parts, walks four ways and jumps, through the real movement step
/// (<see cref="World.Simulate"/> and <see cref="ConstructWorld.BlockPlayer"/>). A dev tool: it allocates freely.
/// </summary>
public static class RideCheck
{
    public static int Cli(string[] args)
    {
        string Arg(string name, string def) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() ?? def;
        var names = Arg("--ride-check", "Rust Bucket,Zero Day Frigate,Halberd Frigate,Magpie Mining Ship");
        if (names.StartsWith("--"))
            names = "Rust Bucket,Zero Day Frigate,Halberd Frigate,Magpie Mining Ship";
        var speed = float.Parse(Arg("--speed", "10"), System.Globalization.CultureInfo.InvariantCulture);
        var turn = float.Parse(Arg("--turn", "10"), System.Globalization.CultureInfo.InvariantCulture) * MathF.PI / 180f;
        var spots = int.Parse(Arg("--spots", "8"));
        var mapName = Arg("--map", "Proving Ground");
        if (Maps.Find(mapName) is not { } yard)
        {
            Console.WriteLine($"no {mapName} map");
            return 1;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        var bad = 0;
        foreach (var name in names.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            bad += One(name, speed, turn, spots);
        Console.WriteLine($"ride check: {bad} lapse(s)");
        return bad > 0 ? 1 : 0;
    }

    private static int One(string name, float speed, float turn, int maxSpots)
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
        var frees = 0;
        for (var j = 0; j < c.JointCount; j++)
            frees += c.Joints[j].Freeform ? 1 : 0;
        Console.WriteLine($"{name}: {c.PartCount} parts, {c.JointCount} joints ({frees} freeform), bounds {c.LocalMin} .. {c.LocalMax}");
        Pose(w, c, c.Pos + new Vector3(0, 30f, 0), c.Rot, Vector3.Zero, Vector3.Zero);
        var found = Spots(c, maxSpots);
        found.AddRange(Spots(c, 4, joints: true));
        Console.WriteLine($"  {found.Count} standing spots");
        var lapses = 0;
        var clock = 0;
        var home = c.Pos;
        foreach (var (part, local) in found)
        {
            c.Pos = c.PrevPos = home; // (back over the yard: the map's edge clamps a player)
            var s = new MoveState { Position = c.ToWorld(local) + new Vector3(0, 0.03f, 0) };
            s.Velocity = c.PointVel(s.Position); // (set down moving with it)
            var vel = Vector3.Zero;
            var ang = Vector3.Zero;
            var log = new List<string>();
            int frameless = 0, segSwaps = 0, bodySwaps = 0, airTicks = 0, maxAir = 0;
            var lastBody = (ushort)0;
            var lastSeg = (byte)0;
            for (var t = 0; t < 60 * 15; t++)
            {
                // Speed up over two seconds, then on, turning.
                var k = MathF.Min(1f, clock++ / 120f);
                var fwd = Vector3.Transform(-Vector3.UnitZ, c.Rot);
                vel = fwd * speed * k;
                ang = new Vector3(0, turn * k, 0);
                var q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, ang.Y * World.TickDt) * c.Rot;
                Pose(w, c, c.Pos + vel * World.TickDt, q, vel, ang);
                var phase = t / 120; // (2 s each: settle, then four walks, then jumps)
                var b = Buttons.None;
                var yaw = 0f;
                if (phase >= 1 && phase <= 4)
                {
                    b = Buttons.Forward;
                    yaw = (phase - 1) * MathF.PI * 0.5f;
                }
                else if (phase >= 5 && t % 50 == 0)
                    b = Buttons.Jump | Buttons.Forward;
                else if (phase >= 5)
                    b = Buttons.Forward;
                // The look in the ship's frame (as a client sends it).
                var cmd = new InputCmd { Buttons = b, Yaw = yaw, SpeedPct = 100, RideBody = s.RideBody, RideYaw = yaw };
                World.Simulate(ref s, cmd);
                w.BlockPlayer(ref s, World.HeightOf(s.Stance), server: true, who: 1);
                if (t < 30)
                {
                    lastBody = s.RideBody;
                    lastSeg = s.RideSeg;
                    continue;
                }
                if (s.RideBody == 0)
                {
                    frameless++;
                    if (frameless < 6)
                        log.Add($"t{t}: no frame (grounded {s.Grounded}, local {c.ToLocal(s.Position)}, rel speed {(s.Velocity - c.PointVel(s.Position)).Length():0.0})");
                }
                if (s.RideBody != lastBody && s.RideBody != 0 && lastBody != 0)
                    bodySwaps++;
                if (s.RideBody == lastBody && s.RideSeg != lastSeg)
                {
                    segSwaps++;
                    if (segSwaps < 6)
                        log.Add($"t{t}: segment {lastSeg} -> {s.RideSeg}");
                }
                if (s.RideBody != 0 && !s.Grounded)
                {
                    airTicks++;
                    maxAir = Math.Max(maxAir, s.RideAir);
                }
                lastBody = s.RideBody;
                lastSeg = s.RideSeg;
            }
            var lost = frameless > 0;
            lapses += lost ? 1 : 0;
            Console.WriteLine($"  from {c.DefOf(part).Name}#{part} at {local}: frameless {frameless} ticks, segment swaps {segSwaps}, body swaps {bodySwaps}, air ticks {airTicks} (longest {maxAir}), ends {(s.RideBody == 0 ? "OFF" : "on")} at {c.ToLocal(s.Position)}");
            foreach (var l in log)
                Console.WriteLine("     " + l);
        }
        return lapses;
    }

    /// <summary>A construct moved as the physics would: its pose, velocity and turn, its acceleration noted.</summary>
    private static void Pose(ConstructWorld w, Construct c, Vector3 pos, Quaternion rot, Vector3 vel, Vector3 ang)
    {
        c.PrevPos = c.Pos;
        c.PrevRot = c.Rot;
        c.Pos = pos;
        c.Rot = Quaternion.Normalize(rot);
        c.Vel = vel;
        c.AngVel = ang;
        c.Asleep = false;
        c.Anchored = false;
        ConstructRide.BeforePhysics(w);
    }

    /// <summary>Tops of the body's own wide parts with room above them for a standing player, spread over the ship.</summary>
    private static List<(int, Vector3)> Spots(Construct c, int max, bool joints = false)
    {
        var all = new List<(int, Vector3)>();
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            if (p.Broken || joints != (c.MoveSeg(p.Seg) != 0))
                continue;
            for (var b = 0; b < c.SolidCount(i); b++)
            {
                c.SolidBox(i, b, out var centre, out var half);
                c.SegPose(p.Seg, out var q, out var tr);
                var upL = Vector3.Transform(Vector3.UnitY, Quaternion.Conjugate(q));
                if (upL.Y < 0.99f || half.X < 0.35f || half.Z < 0.35f)
                    continue;
                var top = tr + Vector3.Transform(centre + new Vector3(0, half.Y + 0.02f, 0), q);
                var world = c.ToWorld(top);
                if (ConstructRide.BlockedOn(c, i, world, World.PlayerHeight))
                    continue;
                // (under a roof: somewhere inside the ship)
                if (top.Y > c.LocalMax.Y - 2.2f)
                    continue;
                all.Add((i, top));
            }
        }
        var picked = new List<(int, Vector3)>();
        for (var k = 0; k < max && all.Count > 0; k++)
            picked.Add(all[(int)((long)k * all.Count / max)]);
        return picked;
    }
}
