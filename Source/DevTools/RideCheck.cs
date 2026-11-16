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
        var bad = Lift(speed);
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
            int frameless = 0, segSwaps = 0, bodySwaps = 0, airTicks = 0, maxAir = 0, inFrameLapses = 0, leftAt = -1;
            var leaveRel = 0f;
            var after = float.NaN;
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
                    var iq = Quaternion.Conjugate(c.Rot);
                    if (ConstructRide.InFrame(c, c.ToLocal(s.Position), Vector3.Transform(Vector3.UnitY, iq), ConstructRide.SpawnPad, ConstructRide.SpawnAbove)) // (left past the padded frame, a player is caught again inside its bounds proper)
                        inFrameLapses++;
                    if (leftAt < 0)
                    {
                        leftAt = t;
                        leaveRel = (s.Velocity - c.PointVel(s.Position)).Length();
                    }
                    if (t == leftAt + 60)
                        after = new Vector2(s.Velocity.X, s.Velocity.Z).Length();
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
            var lost = inFrameLapses > 0;
            lapses += lost ? 1 : 0;
            Console.WriteLine($"  from {c.DefOf(part).Name}#{part} at {local}: frameless {frameless} ticks ({inFrameLapses} inside its bounds), segment swaps {segSwaps}, body swaps {bodySwaps}, air ticks {airTicks} (longest {maxAir}), ends {(s.RideBody == 0 ? "OFF" : "on")} at {c.ToLocal(s.Position)}"
                + (leftAt >= 0 ? $"; left at t{leftAt} {leaveRel:0.0} m/s off the ship's point speed ({speed:0} m/s), across {after:0.0} m/s a second later" : ""));
            foreach (var l in log)
                Console.WriteLine("     " + l);
        }
        // Spawn into motion: a loose plate made a metre over the first spot, as any path makes a body.
        if (found.Count > 0)
        {
            var at = c.ToWorld(found[0].Item2) + Vector3.Transform(new Vector3(0, 1f, 0), c.Rot);
            if (ConstructBuild.PlaceNew(w, ConstructParts.Need("Deck Plate"), at, 0) is { } loose)
            {
                loose.Vel = Vector3.Zero;
                ConstructRide.SpawnIntoMotion(w);
                loose.Rebuild();
                var want = c.PointVel(loose.WorldCom);
                Console.WriteLine($"  spawned plate: {loose.Vel.Length():0.00} m/s, the ship there {want.Length():0.00} m/s (off by {(loose.Vel - want).Length():0.000}), spin {loose.AngVel.Y:0.000} (ship {c.AngVel.Y:0.000})");
                if ((loose.Vel - want).Length() > 0.05f)
                    lapses++;
                w.Remove(loose);
            }
        }
        return lapses;
    }

    /// <summary>
    /// A lift on a moving deck: a row of deck plates with a piston and a plate on it, the body driven along and the piston up
    /// and down (its pose and speed set each tick); a player standing on the lift's plate rides it in the body's frame.
    /// </summary>
    public static int Lift(float speed)
    {
        var w = new ConstructWorld();
        var plate = ConstructParts.Need("Deck Plate");
        if (ConstructBuild.PlaceNew(w, plate, ConstructWorld.Ground(World.Query, 0, 0), 0) is not { } c)
            return 1;
        var at = 0;
        for (var k = 1; k < 4; k++)
            at = Fit(w, c, at, "+z", plate, "-z");
        var piston = Fit(w, c, 1, "top11", ConstructParts.Need("Piston"), "base");
        var deck = piston < 0 ? -1 : Fit(w, c, piston, "top", plate, "bottom");
        c.Rebuild();
        if (deck < 0 || c.JointCount != 1)
        {
            Console.WriteLine($"lift: doesn't build (piston {piston}, deck {deck}, joints {c.JointCount})");
            return 1;
        }
        ref var j = ref c.Joints[0];
        Pose(w, c, c.Pos + new Vector3(0, 30f, 0), c.Rot, Vector3.Zero, Vector3.Zero);
        c.SolidBox(deck, 0, out var centre, out var half);
        var top = centre + new Vector3(0, half.Y + 0.03f, 0);
        c.SegPose(c.Parts[deck].Seg, out var sq, out var st);
        var s = new MoveState { Position = c.ToWorld(st + Vector3.Transform(top, sq)) };
        float worst = 0, angle = 0;
        int frameless = 0, onLift = 0;
        var fwd = Vector3.Transform(-Vector3.UnitZ, c.Rot);
        for (var t = 0; t < 60 * 8; t++)
        {
            var vel = fwd * speed * MathF.Min(1f, t / 60f);
            var jv = t < 60 ? 0f : (t / 120) % 2 == 0 ? 0.6f : -0.6f;
            angle = Math.Clamp(angle + jv * World.TickDt, j.Min, j.Max);
            j.Angle = angle;
            j.Vel = angle <= j.Min || angle >= j.Max ? 0 : jv;
            Pose(w, c, c.Pos + vel * World.TickDt, c.Rot, vel, Vector3.Zero);
            var walk = t > 60 * 7 ? Buttons.Forward : Buttons.None;
            World.Simulate(ref s, new InputCmd { Buttons = walk, SpeedPct = 100, RideBody = s.RideBody });
            w.BlockPlayer(ref s, World.HeightOf(s.Stance), server: true, who: 1);
            if (s.RideBody == 0 && t < 60 * 7)
                frameless++;
            if (t > 30 && t < 60 * 7 && s.RideBody != 0)
            {
                onLift += s.RideOn == c.Parts[deck].Seg ? 1 : 0;
                c.SegPose(c.Parts[deck].Seg, out sq, out st);
                var deckTop = (st + Vector3.Transform(top, sq)).Y - 0.03f;
                worst = MathF.Max(worst, MathF.Abs(c.ToLocal(s.Position).Y - deckTop));
            }
        }
        Console.WriteLine($"lift at {speed:0} m/s: on the lift {onLift} ticks (of {60 * 7 - 31}), feet off its top by {worst * 100:0.0} cm at worst, frameless {frameless} ticks, ends {(s.RideBody == 0 ? "OFF" : $"on (carried by segment {s.RideOn})")} at {c.ToLocal(s.Position)}");
        return worst > 0.05f || frameless > 0 ? 1 : 0;
    }

    private static int Fit(ConstructWorld w, Construct c, int on, string socket, ConstructPartDef def, string by)
    {
        if (on < 0)
            return -1;
        var ts = Array.FindIndex(c.DefOf(on).Sockets, x => x.Name == socket);
        var ds = Array.FindIndex(def.Sockets, x => x.Name == by);
        for (var rot = 0; rot < 24; rot++)
            if (ConstructBuild.Place(c, on, ts, def, ds, rot, out var g) is null)
            {
                var i = ConstructBuild.Attach(w, c, def, g);
                c.Rebuild();
                return i;
            }
        return -1;
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
