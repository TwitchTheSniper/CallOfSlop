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
        var names = Arg("--ride-check", "Rust Bucket,Zero Day Frigate,Magpie Mining Ship");
        if (names.StartsWith("--"))
            names = "Rust Bucket,Zero Day Frigate,Magpie Mining Ship";
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
        HeldOnly = args.Any(a => a.Equals("--held", StringComparison.OrdinalIgnoreCase));
        LooseOnly = args.Any(a => a.Equals("--loose", StringComparison.OrdinalIgnoreCase));
        LooseMany = int.Parse(Arg("--loose-many", "0"));
        if (LooseOnly)
        {
            LooseSpeeds = Arg("--speeds", args.Any(a => a.Equals("--speed", StringComparison.OrdinalIgnoreCase)) ? Arg("--speed", "10") : "0,60,120,200")
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            if (names == "Rust Bucket,Zero Day Frigate,Magpie Mining Ship")
                names += ",IT Box Truck 1";
        }
        var bad = HeldOnly || LooseOnly ? 0 : Lift(speed);
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
        if (LooseOnly)
        {
            var looseBad = 0;
            if (found.Count < 2)
            {
                Console.WriteLine("  loose: fewer than two standing spots");
                return 0;
            }
            foreach (var v in LooseSpeeds)
            {
                looseBad += Loose(w, c, found[0].Item2, found[found.Count / 2].Item2, v, turn, posed: false);
                looseBad += Loose(w, c, found[0].Item2, found[found.Count / 2].Item2, v, turn, posed: true);
            }
            return looseBad;
        }
        if (HeldOnly)
        {
            var heldBad = 0;
            for (var k = 0; k < Math.Min(found.Count, 3); k++)
            {
                heldBad += Held(w, c, found[k].Item2, speed, turn, posed: false);
                heldBad += Held(w, c, found[k].Item2, speed, turn, posed: true);
            }
            return heldBad;
        }
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
                    if (ConstructRide.InFrame(c, c.ToLocal(s.Position), Vector3.Transform(Vector3.UnitY, iq), 0f, 0f)) // (left past the padded frame, a player is caught again inside its bounds proper)
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
            // ...and one dropped 1.5 m out past its side, in the air: still in its frame.
            var side = c.ToWorld(new Vector3(c.LocalMax.X + 1.5f, (c.LocalMin.Y + c.LocalMax.Y) * 0.5f, (c.LocalMin.Z + c.LocalMax.Z) * 0.5f));
            if (ConstructBuild.PlaceNew(w, ConstructParts.Need("Deck Plate"), side, 0) is { } beside)
            {
                beside.Vel = Vector3.Zero;
                ConstructRide.SpawnIntoMotion(w);
                beside.Rebuild();
                var want = c.PointVel(beside.WorldCom);
                Console.WriteLine($"  plate beside its hull: {beside.Vel.Length():0.00} m/s, the ship there {want.Length():0.00} m/s");
                if ((beside.Vel - want).Length() > 0.05f)
                    lapses++;
                w.Remove(beside);
            }
            // ...and one cut free there that the physics then steps: it keeps the ship's speed past the physics' limit (a part
            // detached from a fast ship flew off behind it: Construct.SpeedKept). Made well over the ship, so the step meets
            // nothing, and the ship is held where it is for that step.
            if (ConstructBuild.PlaceNew(w, ConstructParts.Need("Deck Plate"), at + GravityFrames.UpAt(at) * (c.Radius * 2 + 20f), 0) is { } cut)
            {
                cut.Rebuild();
                cut.Vel = c.PointVel(cut.WorldCom);
                cut.AngVel = c.AngVel;
                var want = cut.Vel;
                ConstructRide.SpawnIntoMotion(w);
                c.Anchored = true;
                ConstructPhysics.Step(w, World.Query, World.TickDt);
                c.Anchored = false;
                var off = Vector3.Dot(want - cut.Vel, want.LengthSquared() > 1e-4f ? Vector3.Normalize(want) : Vector3.Zero);
                Console.WriteLine($"  plate cut free, a physics step on: {cut.Vel.Length():0.00} m/s, the ship there {want.Length():0.00} m/s (fell behind by {off:0.00} m/s)");
                if (off > 0.5f)
                    lapses++;
                w.Remove(cut);
            }
        }
        c.Pos = c.PrevPos = home - Vector3.Transform(-Vector3.UnitZ, c.Rot) * MathF.Min(speed * 2.5f, 120f); // (back over the yard, and short of it by half the flight: the map's edge clamps a flyer too; past 50 m/s the yard is too small, try it in space)
        lapses += NoclipBoard(w, c, speed);
        lapses += NoclipBoardKey(w, c, speed);
        return lapses;
    }

    /// <summary>
    /// The Board inside key (ConstructRideInterior.cs): a noclip flyer 40 m off the moving ship's side aims at its middle and
    /// presses it; they must be set down inside, under its roof, and stay on that floor while it flies on.
    /// </summary>
    /// <summary>
    /// A flyer held in <paramref name="c"/>'s frame for a set-down inside: the server's search stepped a slice a tick as
    /// Game.NoclipHoldTick steps it, the ship flying on and the flyer held in it meanwhile, then set down.
    /// </summary>
    private static string Settle(ConstructWorld w, Construct c, ref MoveState s, float speed)
    {
        if (!s.NoclipHold)
            return "NOT held";
        var search = new ConstructRide.InteriorSearch();
        search.Begin(w, c, s.Position, World.HeightOf(Stance.Stand));
        var moved = 0f;
        var start = c.ToLocal(s.Position);
        while (!search.Step(ConstructRide.InteriorBudgetMs) && search.Ticks < 600)
        {
            var vel = Vector3.Transform(-Vector3.UnitZ, c.Rot) * speed;
            Pose(w, c, c.Pos + vel * World.TickDt, c.Rot, vel, Vector3.Zero);
            World.Simulate(ref s, new InputCmd { SpeedPct = 100, RideBody = s.RideBody });
            moved = MathF.Max(moved, (c.ToLocal(s.Position) - start).Length());
        }
        if (search.Found)
            ConstructRide.NoclipSetDown(ref s, c, search.Spot);
        else
        {
            s.NoclipHold = s.Noclip = false;
            NoclipMove.Leave(ref s, 0, w);
        }
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"held {search.Ticks} ticks (drifted {moved:0.00} m in the frame), {search.Rays} rays down {search.Columns} columns, {search.WorstMs:0.00} ms at worst a tick, {search.Ms:0.0} ms in all{(search.Found ? "" : ", NO floor inside")}");
    }

    private static int NoclipBoardKey(ConstructWorld w, Construct c, float speed)
    {
        var was = ConstructRide.Bodies;
        ConstructRide.Bodies = w;
        try
        {
            var mid = (c.LocalMin + c.LocalMax) * 0.5f;
            var s = new MoveState { Position = c.ToWorld(mid with { X = c.LocalMax.X + 40f }), Noclip = true };
            World.Simulate(ref s, new InputCmd { SpeedPct = 100, NoclipBoard = c.Id, NoclipBoardAt = mid });
            var held = Settle(w, c, ref s, speed);
            var at = c.ToLocal(s.Position);
            var roofed = s.RideBody == c.Id && w.Raycast(s.Position + s.RideUpW * 0.5f, s.RideUpW, ConstructRide.InteriorRoof, out _, only: c);
            int grounded = 0, off = 0;
            for (var k = 0; k < 60 * 2; k++)
            {
                var vel = Vector3.Transform(-Vector3.UnitZ, c.Rot) * speed;
                Pose(w, c, c.Pos + vel * World.TickDt, c.Rot, vel, Vector3.Zero);
                World.Simulate(ref s, new InputCmd { SpeedPct = 100, RideBody = s.RideBody });
                w.BlockPlayer(ref s, World.HeightOf(s.Stance), server: true, who: 1);
                grounded += s.Grounded && s.RideBody == c.Id ? 1 : 0;
                off += s.RideBody != c.Id ? 1 : 0;
            }
            var drift = (c.ToLocal(s.Position) - at).Length();
            Console.WriteLine($"  board inside key: set down at {at}, {(roofed ? "under its roof" : "NOT under a roof")}, grounded {grounded} of 120 ticks, out of its frame {off}, moved {drift:0.00} m in it; {held}");
            return s.RideBody != c.Id || off > 0 || grounded < 100 ? 1 : 0;
        }
        finally
        {
            ConstructRide.Bodies = was;
        }
    }

    /// <summary>
    /// Creative noclip into the moving ship (NoclipMove.cs, ConstructRide.NoclipFrame): a flyer hovers still in its path, the
    /// ship overtakes them and they are caught into its frame; they hover on while it flies on and turns (they must stay in);
    /// then they leave noclip and must stand in the ship, on a deck.
    /// </summary>
    private static int NoclipBoard(ConstructWorld w, Construct c, float speed)
    {
        var was = ConstructRide.Bodies;
        ConstructRide.Bodies = w;
        try
        {
            var mid = (c.LocalMin + c.LocalMax) * 0.5f;
            var start = c.ToWorld(new Vector3(mid.X, mid.Y, c.LocalMin.Z - 6f)); // (still, in its path ahead of the bow: it overtakes them)
            var s = new MoveState { Position = start };
            var yaw = 0f;
            int caught = -1, lost = 0, t = 0;
            var vel = Vector3.Transform(-Vector3.UnitZ, c.Rot) * speed;
            for (; t < 60 * 2; t++)
            {
                var fwd = Vector3.Transform(-Vector3.UnitZ, c.Rot);
                vel = fwd * speed;
                var q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.05f * World.TickDt) * c.Rot;
                Pose(w, c, c.Pos + vel * World.TickDt, q, vel, new Vector3(0, 0.05f, 0));
                var b = Buttons.None;
                var cmd = new InputCmd { Buttons = b, Yaw = yaw, SpeedPct = 100, Noclip = true, RideBody = s.RideBody, RideYaw = 0 };
                World.Simulate(ref s, cmd);
                if (s.RideBody == c.Id && caught < 0)
                    caught = t;
                else if (caught >= 0 && s.RideBody != c.Id)
                    lost++;
            }
            var inside = c.ToLocal(s.Position);
            Console.WriteLine($"  noclip in: {(caught < 0 ? "NEVER caught" : $"caught at t{caught}")}, out of its frame {lost} ticks after, at {inside} (bounds {c.LocalMin} .. {c.LocalMax})");
            // Leaving noclip there: the game finds room (Game.NoclipLeft), then they ride its frame.
            World.Simulate(ref s, new InputCmd { Yaw = yaw, SpeedPct = 100, RideBody = s.RideBody });
            var held = s.NoclipHold ? Settle(w, c, ref s, speed) : "not held (not inside its hull)";
            if (s.NoclipExit)
                NoclipMove.Leave(ref s, 0, w); // (as Game.NoclipLeft: out of what they are in, onto its deck)
            var roofed = s.RideBody == c.Id && w.Raycast(s.Position + s.RideUpW * 0.5f, s.RideUpW, ConstructRide.InteriorRoof, out _, only: c);
            Console.WriteLine($"  noclip set down at {c.ToLocal(s.Position)}, {(roofed ? "under its roof (inside)" : "with no roof over it")}; {held}");
            var grounded = 0;
            var off = 0;
            for (var k = 0; k < 60 * 3; k++, t++)
            {
                var fwd = Vector3.Transform(-Vector3.UnitZ, c.Rot);
                vel = fwd * speed;
                Pose(w, c, c.Pos + vel * World.TickDt, c.Rot, vel, Vector3.Zero);
                World.Simulate(ref s, new InputCmd { Yaw = yaw, SpeedPct = 100, RideBody = s.RideBody });
                w.BlockPlayer(ref s, World.HeightOf(s.Stance), server: true, who: 1);
                grounded += s.Grounded && s.RideBody == c.Id ? 1 : 0;
                off += s.RideBody != c.Id ? 1 : 0;
            }
            var end = c.ToLocal(s.Position);
            var on = s.RideBody == c.Id && s.Grounded && ConstructRide.InFrame(c, end, Vector3.UnitY, 0f, 0f);
            Console.WriteLine($"  noclip off: {(on ? "on a deck" : "NOT on a deck")} at {end}, grounded in its frame {grounded} of 180 ticks, out of it {off}, speed off the ship's {(s.Velocity).Length():0.00} m/s (relative)");
            return caught < 0 || lost > 0 || !on ? 1 : 0;
        }
        finally
        {
            ConstructRide.Bodies = was;
        }
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

    private static bool HeldOnly, LooseOnly;
    private static float[] LooseSpeeds = [0, 60, 120, 200];
    private static int LooseMany;

    /// <summary>What carries the loose bodies of a posed ship (one out of the bubble) once it is posed: the game's own pass for them (GameSpace.cs).</summary>
    private static void CarryPosed(ConstructWorld w) => ConstructFrames.After(w, posed: true);

    /// <summary>
    /// "--loose" (local-frame-physics.md phase 0): loose bodies aboard a moving ship, as the server steps them. A Frame Block is
    /// set down resting on a deck spot and another is thrown across the ship from a second spot (3 m/s in the ship's frame);
    /// the ship cruises straight (3 s), turns at <paramref name="turn"/> (4 s), brakes at 8 m/s2 (2 s) and runs on (2 s),
    /// stepped (a driven ship in the bubble) or posed (out of the bubble, posed after the step). Printed per phase: the
    /// resting block's drift in the ship's frame, its largest drawn jump in the ship's frame (the host's blend of the two
    /// poses at a quarter, half and three quarters of a tick), its ticks asleep, the thrown block's state, and the step's CPU.
    /// </summary>
    private static int Loose(ConstructWorld w, Construct c, Vector3 spotA, Vector3 spotB, float speed, float turn, bool posed)
    {
        var home = c.Pos;
        var homeRot = c.Rot;
        var block = ConstructParts.Need("Frame Block");
        var fwd0 = Vector3.Transform(-Vector3.UnitZ, c.Rot);
        Pose(w, c, c.Pos, c.Rot, fwd0 * speed, Vector3.Zero);
        if (posed)
            c.SpaceShip = 60000;
        var rest = ConstructBuild.PlaceNew(w, block, c.ToWorld(spotA + new Vector3(0, 0.3f, 0)), 0);
        var thrown = ConstructBuild.PlaceNew(w, block, c.ToWorld(spotB + new Vector3(0, 1.0f, 0)), 0);
        if (rest == null || thrown == null)
        {
            Console.WriteLine("  loose: blocks not placed");
            return 0;
        }
        foreach (var b in new[] { rest, thrown })
        {
            b.Rebuild();
            b.Vel = c.PointVel(b.WorldCom);
            b.AngVel = c.AngVel;
            b.KeepSpeed();
            b.Fresh = false;
            if (ConstructFrames.Find(w, b, posed) is { } f)
                ConstructFrames.Join(b, f); // (as the game joins one made aboard: ConstructRide.IntoMotion, Game.SpaceLaunchAfter)
            else
                Console.WriteLine("  loose: a block found no frame");
        }
        thrown.Vel += Vector3.Transform(new Vector3(2f, 1f, -2f), c.Rot);
        // "--loose-many N": N more blocks set down in a heap round the two spots (the step's cost with many bodies aboard).
        var extra = new List<Construct>();
        for (var k = 0; k < LooseMany; k++)
        {
            var spot = k % 2 == 0 ? spotA : spotB;
            var at = spot + new Vector3((k / 2 % 3 - 1) * 0.6f, 0.5f + k / 6 * 0.45f, (k / 6 % 3 - 1) * 0.6f);
            if (ConstructBuild.PlaceNew(w, block, c.ToWorld(at), 0) is not { } b)
                continue;
            b.Rebuild();
            b.Vel = c.PointVel(b.WorldCom);
            b.AngVel = c.AngVel;
            b.Fresh = false;
            if (ConstructFrames.Find(w, b, posed) is { } f)
                ConstructFrames.Join(b, f);
            extra.Add(b);
        }
        var loose = new[] { rest, thrown };
        var lastL = new Vector3[3];
        Vector3 L(Construct b) => c.ToLocal(b.WorldCom);
        Vector3 Drawn(Construct b, float a)
        {
            var sp = Vector3.Lerp(c.PrevPos, c.Pos, a);
            var sq = Quaternion.Slerp(c.PrevRot, c.Rot, a);
            var bp = Vector3.Lerp(b.PrevPos, b.Pos, a);
            var bq = Quaternion.Slerp(b.PrevRot, b.Rot, a);
            return Vector3.Transform(bp + Vector3.Transform(b.Com, bq) - sp, Quaternion.Conjugate(sq));
        }
        var phases = new (string Name, int From, int To)[] { ("cruise", 0, 180), ("turn", 180, 420), ("brake", 420, 540), ("after", 540, 660) };
        var start = new Vector3[phases.Length];
        var startT = new Vector3[phases.Length];
        var jump = new float[phases.Length];
        var asleep = new int[phases.Length];
        double msSum = 0, msMax = 0;
        var v = speed;
        var prevDrawn = Vector3.Zero;
        var lost = "";
        for (var t = 0; t < 660; t++)
        {
            var ph = t < 180 ? 0 : t < 420 ? 1 : t < 540 ? 2 : 3;
            if (t == (ph == 0 ? 60 : phases[ph].From))
            {
                start[ph] = L(rest); // (the cruise from a second in: the block is set down 0.3 m over the deck)
                startT[ph] = L(thrown);
            }
            if (ph == 2)
                v = MathF.Max(0, v - 8f * World.TickDt);
            var ang = ph == 1 ? new Vector3(0, turn, 0) : Vector3.Zero;
            var vel = Vector3.Transform(-Vector3.UnitZ, c.Rot) * v;
            if (!posed)
                Drive(w, c, vel, ang);
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            w.Step(World.Query, World.TickDt);
            if (posed)
            {
                c.SpaceAccel = (vel - c.Vel) * World.TickRate; // (as its drive would: Game.SpaceLaunchBefore)
                Pose(w, c, c.Pos + vel * World.TickDt, Quaternion.CreateFromAxisAngle(Vector3.UnitY, ang.Y * World.TickDt) * c.Rot, vel, ang);
                c.SpaceShip = 60000;
                CarryPosed(w);
            }
            var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (t > 5)
            {
                msSum += ms;
                msMax = Math.Max(msMax, ms);
            }
            asleep[ph] += rest.Asleep ? 1 : 0;
            if (w.Get(rest.Id) != rest || w.Get(thrown.Id) != thrown)
            {
                lost = " (a block went)";
                break;
            }
            // The drawn jumps: three blends a tick, each against the last, for the resting block only, after it settled.
            for (var k = 1; k <= 4; k++)
            {
                var d = Drawn(rest, k / 4f);
                if (t > phases[ph].From + (ph == 2 ? 10 : 60))
                    jump[ph] = MathF.Max(jump[ph], Vector3.Distance(d, prevDrawn));
                prevDrawn = d;
            }
        }
        var iq = Quaternion.Conjugate(c.Rot);
        var upL = Vector3.Transform(GravityFrames.UpAt(rest.WorldCom), iq);
        var inA = ConstructRide.InFrame(c, L(rest), upL);
        var inB = ConstructRide.InFrame(c, L(thrown), upL);
        var line = new System.Text.StringBuilder();
        line.Append(FormattableString.Invariant($"  loose ({(posed ? "posed" : "stepped")}) at {speed:0} m/s:"));
        for (var k = 0; k < phases.Length; k++)
        {
            var end = k + 1 < phases.Length ? start[k + 1] : L(rest);
            line.Append(FormattableString.Invariant($" {phases[k].Name} drift {Vector3.Distance(end, start[k]):0.000} m, jump {jump[k] * 1000:0} mm, asleep {asleep[k]}/{phases[k].To - phases[k].From};"));
        }
        var relT = (thrown.Vel - c.PointVel(thrown.WorldCom)).Length();
        line.Append(FormattableString.Invariant($" thrown: in frame {inB}, {relT:0.00} m/s against the deck at the end, moved {Vector3.Distance(L(thrown), startT[0]):0.0} m from a second in;"));
        if (extra.Count > 0)
            line.Append(FormattableString.Invariant($" {extra.Count} more: {extra.Count(b => b.Asleep)} asleep at the end, {extra.Count(b => b.Frame == c.Id)} in its frame;"));
        foreach (var b in extra)
            w.Remove(b);
        line.Append(FormattableString.Invariant($" rest block in frame {inA} (in the ship's {(rest.Frame == c.Id ? "frame" : "WORLD")}); step {msSum / 654:0.00} ms mean, {msMax:0.00} most{lost}"));
        Console.WriteLine(line.ToString());
        w.Remove(rest);
        w.Remove(thrown);
        c.SpaceShip = 0;
        c.Pos = c.PrevPos = home;
        c.Rot = c.PrevRot = homeRot;
        c.Vel = c.AngVel = Vector3.Zero;
        var cruiseDrift = Vector3.Distance(start[1], start[0]);
        return !inA || cruiseDrift > 0.05f || jump[0] > 0.05f ? 1 : 0;
    }

    /// <summary>
    /// "--held": a player standing at <paramref name="local"/> (the ship's frame) holds a Frame Block in the physics gun 2 m
    /// out along a look fixed in the ship's frame while the ship moves and turns, as the server runs it (the beam's aim and
    /// the frame's motion, then the physics step; posed: a ship out of the bubble, not stepped, posed after the step). The
    /// held point should stay on the beam's point in the ship's frame, and its turn on the ship's: printed are how far it
    /// sits off, how far it jumps tick to tick in the ship's frame, and how far it turns against the ship.
    /// </summary>
    private static int Held(ConstructWorld w, Construct c, Vector3 local, float speed, float turn, bool posed)
    {
        var home = c.Pos;
        var homeRot = c.Rot;
        var eyeL = local + new Vector3(0, 1.6f, 0);
        var lookL = Vector3.Normalize(new Vector3(0, -0.25f, -1f));
        const float dist = 2f;
        var block = ConstructParts.Need("Frame Block");
        if (ConstructBuild.PlaceNew(w, block, c.ToWorld(eyeL + lookL * dist) - new Vector3(0, 0.25f, 0), 0) is not { } held)
            return 0;
        held.Rebuild();
        held.Vel = c.PointVel(held.WorldCom);
        held.AngVel = c.AngVel;
        var hands = new ConstructEdits();
        ref var h = ref hands.Hand(0);
        h.Tool = ConstructTool.PhysicsGun;
        h.RideBody = c.Id;
        h.RideSeg = 0;
        h.RideTurn = c.Rot;
        var grab = hands.Grab(w, 0, c.ToWorld(eyeL), 0, held.Id, held.Com);
        if (grab != null)
        {
            Console.WriteLine($"  held ({(posed ? "posed" : "stepped")}): not grabbed: {grab}");
            w.Remove(held);
            return 0;
        }
        hands.Hand(0).GrabDist = dist;
        if (posed)
            c.SpaceShip = 60000; // (as a ship out of the bubble: not stepped, its velocity given to the solver)
        float offMax = 0, offSum = 0, jumpMax = 0, turnMax = 0;
        var n = 0;
        var lastRel = Vector3.Zero;
        var rot0 = Quaternion.Identity;
        for (var t = 0; t < 60 * 8; t++)
        {
            var k = MathF.Min(1f, t / 120f);
            var vel = Vector3.Transform(-Vector3.UnitZ, c.Rot) * speed * k;
            var ang = new Vector3(0, turn * k, 0);
            if (!posed)
                Drive(w, c, vel, ang); // (its speed and turn set; the step moves it, as a driven ship)
            ref var hh = ref hands.Hand(0);
            hh.RideTurn = c.Rot;
            var eye = c.ToWorld(eyeL);
            var look = Vector3.Transform(lookL, c.Rot);
            hands.GrabTick(w, 0, eye, look, 0, ConstructRide.SurfaceVel(c, 0, eye + look * dist), ConstructRide.SurfaceSpin(c, 0));
            w.Step(World.Query, World.TickDt);
            if (posed)
            {
                c.SpaceAccel = (vel - c.Vel) * World.TickRate; // (as its drive would: Game.SpaceLaunchBefore)
                Pose(w, c, c.Pos + vel * World.TickDt, Quaternion.CreateFromAxisAngle(Vector3.UnitY, ang.Y * World.TickDt) * c.Rot, vel, ang);
                c.Anchored = false;
                CarryPosed(w);
            }
            if (held.GrabbedBy != 0)
            {
                Console.WriteLine($"  held ({(posed ? "posed" : "stepped")}): let go at t{t}");
                break;
            }
            var rel = c.ToLocal(held.ToWorld(held.GrabLocal)); // (where the held point is in the ship's frame now; the eye is posed with the ship next tick)
            var relRot = Quaternion.Normalize(Quaternion.Conjugate(c.Rot) * held.Rot);
            if (t == 150)
                rot0 = relRot;
            if (t > 150)
            {
                var off = Vector3.Distance(rel, eyeL + lookL * dist);
                offMax = MathF.Max(offMax, off);
                offSum += off;
                n++;
                jumpMax = MathF.Max(jumpMax, Vector3.Distance(rel, lastRel));
                turnMax = MathF.Max(turnMax, 2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(relRot, rot0)), 0f, 1f)) * 180f / MathF.PI);
            }
            lastRel = rel;
            if (t == 60 * 8 - 1)
                Console.WriteLine(FormattableString.Invariant($"    last: off {rel - (eyeL + lookL * dist):0.000} in the ship's frame, held speed {held.Vel.Length():0.00} m/s against the ship's there {c.PointVel(held.WorldCom).Length():0.00}"));
        }
        Console.WriteLine(FormattableString.Invariant($"  held ({(posed ? "posed" : "stepped")}) from {local:0.0} at {speed:0} m/s, turning {turn * 180f / MathF.PI:0} deg/s: off the beam's point {(n > 0 ? offSum / n : 0):0.000} m mean, {offMax:0.000} m most; jumps {jumpMax * 1000:0.0} mm a tick most in the ship's frame; turned {turnMax:0.00} deg against the ship"));
        hands.Release(w, 0);
        w.Remove(held);
        c.SpaceShip = 0;
        c.Pos = c.PrevPos = home;
        c.Rot = c.PrevRot = homeRot;
        return offMax > 0.25f || jumpMax > 0.02f ? 1 : 0;
    }

    /// <summary>
    /// A stepped ship driven as its engines would: its velocity and turn set before the step so that after it (the pull, the air's
    /// damping) they are <paramref name="vel"/> and <paramref name="ang"/>, and its speed past the free body's limit, as a flying
    /// ship's is. (Set flat, the step's damping and limit made the ship itself lurch every tick and stop at 60 m/s.)
    /// </summary>
    private static void Drive(ConstructWorld w, Construct c, Vector3 vel, Vector3 ang)
    {
        Pose(w, c, c.Pos, c.Rot, vel / 0.999f - ConstructGravity.GravityFor(c, c.WorldCom) * World.TickDt, ang / 0.985f);
        c.SpeedKept = c.Vel.Length() + 1f;
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
