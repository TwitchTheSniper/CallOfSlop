using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// The parts beyond construct joints collide (board item dev-sandbox-joint-collision-jc01; docs/sandbox/CONSTRUCTS.md
    /// "Joint collision"): a hinged flap swung into the yard's ground stops on it and never goes below it, then sleeps; a
    /// motor hinge driving an arm into the ground lifts its (free) construct; a motorised hinge pushing into a post stalls
    /// at its strength and goes on once the post is gone; a turntable's arm stops at another construct; a fast swing doesn't
    /// pass through a 5 cm wall; the yard's jointed constructs sleep at rest; no allocation, and a tick inside its budget
    /// with a dozen jointed constructs moving. Short seeded scenarios, mostly on a flat test floor with walls.
    /// </summary>
    private static void CheckConstructJoints()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"construct joints: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        JointFlapOnGround();
        JointArmLifts();
        JointMotorStalls();
        JointTurntableStops();
        JointFastSwing();
        JointsSleep(yard);
        JointsBudget();
    }

    /// <summary>A flat floor at y 0 with box walls: the joint tests' world (walls can be taken away mid-test).</summary>
    internal sealed class JointTestWorld : IWorldQuery
    {
        public readonly List<(Vector3 Min, Vector3 Max)> Walls = new();
        public ITerrainCollision? Terrain => null;

        public float Raycast(Vector3 origin, Vector3 dir, float maxDist, in QueryFilter filter) =>
            Raycast(origin, dir, maxDist, filter, out var hit) ? hit.Distance : maxDist;

        public bool Raycast(Vector3 origin, Vector3 dir, float maxDist, in QueryFilter filter, out RayHit hit)
        {
            hit = default;
            var best = maxDist;
            var found = false;
            if (dir.Y < -1e-6f && origin.Y >= 0)
            {
                var t = origin.Y / -dir.Y;
                if (t <= best)
                {
                    best = t;
                    found = true;
                    hit.Normal = Vector3.UnitY;
                }
            }
            foreach (var (mn, mx) in Walls)
                if (Geometry.RayAabb(mn, mx, origin, dir, best) is { } d && d < best)
                {
                    best = d;
                    found = true;
                    hit.Normal = Geometry.BoxEntryNormal(mn, mx, origin, dir, d);
                }
            hit.Distance = best;
            hit.Point = origin + dir * best;
            return found;
        }

        public bool SphereCast(Vector3 origin, float radius, Vector3 dir, float maxDist, in QueryFilter filter, out RayHit hit)
        {
            hit = default;
            return false;
        }

        public bool CapsuleCast(in Capsule capsule, Vector3 dir, float maxDist, in QueryFilter filter, out RayHit hit)
        {
            hit = default;
            return false;
        }

        public bool OverlapBox(in Obb box, in QueryFilter filter) => false;
        public bool OverlapCapsule(in Capsule capsule, in QueryFilter filter) => false;
        public bool OverlapBody(Vector3 feet, float height, in QueryFilter filter) => false;
        public bool PointInside(Vector3 point, float radius, in QueryFilter filter) => false;
    }

    private static void StepJoints(ConstructWorld w, IWorldQuery q, int ticks, Action<int>? each = null)
    {
        for (var t = 0; t < ticks; t++)
        {
            each?.Invoke(t);
            w.Step(q, World.TickDt);
        }
    }

    /// <summary>Part <paramref name="part"/> onto socket <paramref name="socket"/> of part <paramref name="on"/>, turned the first way <paramref name="ok"/> accepts (by its rest turn). -1 if none fits.</summary>
    internal static int FitTurned(ConstructWorld w, Construct c, int on, string socket, string part, string by, Func<byte, bool> ok)
    {
        var def = ConstructParts.Need(part);
        var ts = Array.FindIndex(c.DefOf(on).Sockets, s => s.Name == socket);
        var ds = Array.FindIndex(def.Sockets, s => s.Name == by);
        for (var spin = 0; spin < 4; spin++)
            if (ConstructBuild.Place(c, on, ts, def, ds, spin, out var g) == null && ok(g.Rot))
            {
                var i = ConstructBuild.Attach(w, c, def, g);
                c.Rebuild();
                return i;
            }
        return -1;
    }

    /// <summary>A joint part turned so its axis lies flat and a positive angle swings what goes out along +x (body frame) downwards.</summary>
    private static Func<byte, bool> FlatAxisSwingingDown(string part, Vector3 outward)
    {
        var d = ConstructParts.Need(part);
        return rot =>
        {
            var axis = ConstructParts.DirVector(Rot24.ApplyDir(rot, d.JointAxis));
            return MathF.Abs(axis.Y) < 0.5f && Vector3.Cross(axis, outward).Y < -0.5f;
        };
    }

    /// <summary>
    /// An anchored flap: a foundation, a frame block at its edge and a passive hinge on the block's side with its axis
    /// flat, the door hanging on it out past the foundation (its long side along the axis): it swings down to the ground.
    /// </summary>
    internal static Construct? BuildFlap(ConstructWorld w, IWorldQuery q, float x, float z)
    {
        if (ConstructBuild.PlaceNew(w, ConstructParts.Need("Foundation"), ConstructWorld.Ground(q, x, z), 0) is not { } c)
            return null;
        var block = FitTurned(w, c, 0, "top31", "Frame Block", "-y", _ => true);
        var hinge = block < 0 ? -1 : FitTurned(w, c, block, "+x", "Hinge", "base", FlatAxisSwingingDown("Hinge", Vector3.UnitX));
        var axis = hinge < 0 ? Vector3.Zero : ConstructParts.DirVector(Rot24.ApplyDir(c.Parts[hinge].Rot, c.DefOf(hinge).JointAxis));
        var door = hinge < 0 ? -1 : FitTurned(w, c, hinge, "leaf", "Door", "hang", rot => MathF.Abs(Vector3.Dot(Rot24.Apply(rot, Vector3.UnitY), axis)) > 0.9f);
        if (door < 0 || c.JointCount != 1)
        {
            w.Remove(c);
            return null;
        }
        return c;
    }

    /// <summary>The world corners of part <paramref name="i"/>'s solid boxes, into <paramref name="into"/> (returns how many).</summary>
    private static int PartCorners(Construct c, int i, Span<Vector3> into)
    {
        c.WorldFrame(c.Parts[i].Seg, out var q, out var t);
        var n = 0;
        for (var b = 0; b < c.DefOf(i).SolidBoxes && n + 8 <= into.Length; b++)
        {
            c.BoxOf(i, b, out var centre, out var half);
            for (var k = 0; k < 8; k++)
                into[n++] = t + Vector3.Transform(centre + new Vector3((k & 1) != 0 ? half.X : -half.X, (k & 2) != 0 ? half.Y : -half.Y, (k & 4) != 0 ? half.Z : -half.Z), q);
        }
        return n;
    }

    /// <summary>How far part <paramref name="i"/>'s lowest corner is below the ground under it (0 or less: not in it).</summary>
    private static float GroundPen(Construct c, int i, IWorldQuery q)
    {
        Span<Vector3> corners = stackalloc Vector3[32];
        var n = PartCorners(c, i, corners);
        var worst = float.MinValue;
        for (var k = 0; k < n; k++)
            worst = MathF.Max(worst, ConstructWorld.Ground(q, corners[k].X, corners[k].Z).Y - corners[k].Y);
        return worst;
    }

    /// <summary>How deep part <paramref name="i"/>'s deepest corner is inside a box (0: none inside).</summary>
    private static float BoxPen(Construct c, int i, Vector3 min, Vector3 max)
    {
        Span<Vector3> corners = stackalloc Vector3[32];
        var n = PartCorners(c, i, corners);
        var worst = 0f;
        for (var k = 0; k < n; k++)
        {
            var p = corners[k];
            var o = Vector3.Min(p - min, max - p);
            worst = MathF.Max(worst, MathF.Min(o.X, MathF.Min(o.Y, o.Z)));
        }
        return worst;
    }

    private static float Deg(float rad) => rad * 180 / MathF.PI;

    /// <summary>The developer's case: a hinged piece swung down into the yard's terrain stops on it, never below, and sleeps.</summary>
    private static void JointFlapOnGround()
    {
        var w = new ConstructWorld();
        var q = World.Query;
        if (BuildFlap(w, q, 6, 14) is not { } flap)
        {
            Fail("construct joints: the test flap (foundation, block, flat hinge, door) doesn't build");
            return;
        }
        var door = PartNamed(flap, "Door");
        StepJoints(w, q, 5);
        // Latched open (towards its far limit, through the ground) and flung down.
        flap.Joints[0].Open = true;
        flap.Joints[0].Vel = 6f;
        flap.Wake();
        var worst = float.MinValue;
        var sleptAt = -1;
        StepJoints(w, q, 240, t =>
        {
            worst = MathF.Max(worst, GroundPen(flap, door, q));
            if (sleptAt < 0 && flap.Asleep)
                sleptAt = t;
        });
        var angle = flap.Joints[0].Angle;
        var a0 = angle;
        StepJoints(w, q, 30);
        var drift = MathF.Abs(flap.Joints[0].Angle - a0);
        Line($"flap on the ground (yard terrain): flung down at 6 rad/s towards {Deg(flap.Joints[0].Max):0} deg, it stopped at {Deg(angle):0.0} deg, at worst {worst * 100:0.0} cm into the ground "
             + $"({flap.SegPointCount} points beyond the joint), asleep after {sleptAt / 60f:0.00} s, drift {Deg(drift):0.00} deg");
        if (worst > 0.03f || angle > 1.2f || angle < 0.2f)
            Fail("construct joints: a hinged flap swung into the ground went into it (or didn't come down to it)");
        if (sleptAt < 0 || drift > 0.002f)
            Fail("construct joints: a flap resting on the ground didn't sleep (or crept)");
    }

    /// <summary>A free construct: a beam with a motor hinge and a second beam on it; driven down into the floor, the arm lifts the beam's end.</summary>
    private static void JointArmLifts()
    {
        var w = new ConstructWorld();
        var q = new JointTestWorld();
        if (ConstructBuild.PlaceNew(w, ConstructParts.Need("Frame Beam"), Vector3.Zero, MathF.PI / 2) is not { } c)
            return;
        // The beam lies along the body's z: the motor hinge goes on its end, the arm out along +z (the body frame).
        var hinge = FitTurned(w, c, 0, "end+", "Motor Hinge", "base", FlatAxisSwingingDown("Motor Hinge", Vector3.UnitZ));
        var arm = hinge < 0 ? -1 : FitTurned(w, c, hinge, "leaf", "Frame Beam", "end-", _ => true);
        if (arm < 0)
        {
            Fail("construct joints: the free arm (beam, flat motor hinge, beam) doesn't build");
            return;
        }
        StepJoints(w, q, 30);
        var hingeY = ConstructRender.PartCentre(c, hinge).Y;
        c.Joints[0].Open = true; // (Interact: to its far limit, down through the floor)
        c.Wake();
        var worst = float.MinValue;
        var finite = true;
        StepJoints(w, q, 240, _ =>
        {
            worst = MathF.Max(worst, GroundPen(c, arm, q));
            finite &= float.IsFinite(c.Pos.X + c.Pos.Y + c.Pos.Z + c.Joints[0].Angle);
        });
        var rose = ConstructRender.PartCentre(c, hinge).Y - hingeY;
        var rootLow = GroundPen(c, 0, q);
        Line($"free arm: the motor hinge ({c.Joints[0].Strength:0} N m) drove its arm into the floor to {Deg(c.Joints[0].Angle):0} deg and lifted the hinge {rose:0.00} m; "
             + $"the arm at worst {worst * 100:0.0} cm into the floor, the beam {rootLow * 100:0.0} cm; {c.Mass:0} kg, asleep {c.Asleep}");
        if (!finite || worst > 0.04f || rootLow > 0.04f)
            Fail("construct joints: a motor arm driven into the floor went through it (or blew up)");
        if (rose < 0.3f)
            Fail("construct joints: a strong motor arm driven into the floor didn't lift its construct");
    }

    /// <summary>The Motor Gate's door opening into a post stalls there at its motor's strength; with the post gone it goes on.</summary>
    private static void JointMotorStalls()
    {
        var w = new ConstructWorld();
        var q = new JointTestWorld();
        var bp = ConstructBuild.FindBlueprint("Motor Gate");
        if (bp == null || ConstructBuild.BuildOnGround(w, bp, Vector3.Zero, 0, out _) is not { } gate)
        {
            Fail("construct joints: the Motor Gate doesn't build");
            return;
        }
        var door = PartNamed(gate, "Door");
        StepJoints(w, q, 5);
        // A post where the door would be at 50 degrees either way, 0.7 m out from the hinge.
        ref var jt = ref gate.Joints[0];
        gate.WorldFrame(jt.ParentSeg, out var pq, out var pt);
        var axis = Vector3.Transform(jt.Axis, pq);
        var pivot = pt + Vector3.Transform(jt.Pivot, pq);
        var out0 = ConstructRender.PartCentre(gate, door) - pivot;
        out0 = Vector3.Normalize(out0 - axis * Vector3.Dot(out0, axis));
        foreach (var side in new[] { 1f, -1f })
        {
            var at = pivot + Vector3.Transform(out0, Quaternion.CreateFromAxisAngle(axis, side * 50 * MathF.PI / 180)) * 0.7f;
            q.Walls.Add((new Vector3(at.X - 0.15f, 0, at.Z - 0.15f), new Vector3(at.X + 0.15f, 3, at.Z + 0.15f)));
        }
        var postA = q.Walls[0];
        var postB = q.Walls[1];
        var e = new ConstructEdits();
        var button = PartNamed(gate, "Button");
        e.Use(w, 1, ConstructRender.PartCentre(gate, button) + new Vector3(0, 0.3f, -1.4f), gate.Id, button); // (its toggle: open)
        var worst = 0f;
        Action<int> each = _ => worst = MathF.Max(worst, MathF.Max(BoxPen(gate, door, postA.Min, postA.Max), BoxPen(gate, door, postB.Min, postB.Max)));
        StepJoints(w, q, 150, each);
        var stalled = gate.Joints[0].Angle;
        var before = GC.GetAllocatedBytesForCurrentThread();
        StepJoints(w, q, 60, each);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var held = gate.Joints[0].Angle;
        var awake = !gate.Asleep;
        q.Walls.Clear();
        StepJoints(w, q, 120);
        var freed = gate.Joints[0].Angle;
        Line($"motor hinge into a post: stalled at {Deg(stalled):0.0} deg (posts at 50 either way), held {Deg(held):0.0} deg a second later, at worst {worst * 100:0.0} cm into the post, "
             + $"awake {awake} (still pushing); the post gone, it went on to {Deg(freed):0} deg (max {Deg(gate.Joints[0].Max):0}); {bytes} bytes allocated in 60 stalled ticks");
        if (MathF.Abs(stalled) > 50 * MathF.PI / 180 || MathF.Abs(stalled) < 10 * MathF.PI / 180 || worst > 0.03f || MathF.Abs(held - stalled) > 0.01f)
            Fail("construct joints: a motorised hinge pushing into a post didn't stall at it");
        if (!awake || MathF.Abs(freed - gate.Joints[0].Max) > 0.05f)
            Fail("construct joints: a stalled motor hinge didn't go on once its way was clear");
        if (bytes > 0)
            Fail($"construct joints: {bytes} bytes allocated in 60 ticks of a stalled joint (should be none)");
    }

    /// <summary>
    /// An anchored wall: a foundation, two frame blocks and a frame beam out sideways from the top block at
    /// (<paramref name="x"/>, <paramref name="z"/>) along -x (or +x when <paramref name="mirror"/>).
    /// </summary>
    private static Construct? BuildWallArm(ConstructWorld w, IWorldQuery q, float x, float z, bool mirror)
    {
        // The foundation's top00 block sits at (-0.75, -0.75) of its centre; turned half round for the mirror.
        var yaw = mirror ? MathF.PI : 0;
        var off = mirror ? new Vector3(-0.75f, 0, -0.75f) : new Vector3(0.75f, 0, 0.75f);
        if (ConstructBuild.PlaceNew(w, ConstructParts.Need("Foundation"), ConstructWorld.Ground(q, x + off.X, z + off.Z), yaw) is not { } c)
            return null;
        var b1 = FitTurned(w, c, 0, "top00", "Frame Block", "-y", _ => true);
        var b2 = b1 < 0 ? -1 : FitTurned(w, c, b1, "+y", "Frame Block", "-y", _ => true);
        var beam = b2 < 0 ? -1 : FitTurned(w, c, b2, "-x", "Frame Beam", "end+", _ => true);
        return beam < 0 ? null : c;
    }

    /// <summary>The Turret's arm turned by its lever into another construct stops there (whichever way it turns: a wall each side).</summary>
    private static void JointTurntableStops()
    {
        var w = new ConstructWorld();
        var q = new JointTestWorld();
        var bp = ConstructBuild.FindBlueprint("Turret");
        if (bp == null || ConstructBuild.BuildOnGround(w, bp, Vector3.Zero, 0, out _) is not { } turret)
        {
            Fail("construct joints: the Turret doesn't build");
            return;
        }
        // Walls at the arm's height a little beside each end of its sweep, beams running in towards it.
        // (the turntable stands on the foundation's top11 block, 0.25 m off its centre in x and z)
        var wa = BuildWallArm(w, q, 2.7f, 0.75f, false);
        var wb = BuildWallArm(w, q, -3.2f, 0.75f, true);
        if (wa == null || wb == null)
        {
            Fail("construct joints: the test walls for the turret don't build");
            return;
        }
        StepJoints(w, q, 10);
        var e = new ConstructEdits();
        var lever = PartNamed(turret, "Lever");
        var eye = ConstructRender.PartCentre(turret, lever) + new Vector3(1.4f, 1f, 0);
        e.Use(w, 1, eye, turret.Id, lever); // forward
        var worst = 0f;
        var corners = new Vector3[32];
        StepJoints(w, q, 240, _ =>
        {
            for (var i = 0; i < turret.PartCount; i++)
                if (turret.Parts[i].Seg > 0)
                    foreach (var wall in new[] { wa, wb })
                    {
                        var n = PartCorners(turret, i, corners);
                        for (var k = 0; k < n; k++)
                            worst = MathF.Max(worst, DepthInside(wall, corners[k]));
                    }
        });
        var stopped = turret.Joints[0].Angle;
        StepJoints(w, q, 30);
        var held = turret.Joints[0].Angle;
        Line($"turntable into a wall: the arm stopped at {Deg(stopped):0.0} deg (free it would turn {Deg(turret.Joints[0].Speed * 4):0} deg in 4 s), held {Deg(held):0.0} deg, "
             + $"at worst {worst * 100:0.0} cm into the other construct; the walls moved {Vector3.Distance(wa.Pos, wa.PrevPos) * 100:0.0} cm (anchored)");
        if (MathF.Abs(stopped) > 1.2f || MathF.Abs(stopped) < 0.02f || MathF.Abs(held - stopped) > 0.01f || worst > 0.04f)
            Fail("construct joints: a turntable's arm turned into another construct didn't stop at it");
    }

    /// <summary>How deep a world point is inside any of <paramref name="c"/>'s part boxes (0: outside).</summary>
    private static float DepthInside(Construct c, Vector3 p)
    {
        var worst = 0f;
        for (var i = 0; i < c.PartCount; i++)
        {
            c.WorldFrame(c.Parts[i].Seg, out var q, out var t);
            var local = Vector3.Transform(p - t, Quaternion.Conjugate(q));
            for (var b = 0; b < c.DefOf(i).SolidBoxes; b++)
            {
                c.BoxOf(i, b, out var centre, out var half);
                var o = half - Vector3.Abs(local - centre);
                worst = MathF.Max(worst, MathF.Min(o.X, MathF.Min(o.Y, o.Z)));
            }
        }
        return worst;
    }

    /// <summary>The Gate's door flung open at 10 and 25 rad/s at a 5 cm wall (one each side of it): it never gets through.</summary>
    private static void JointFastSwing()
    {
        var parts = new List<string>();
        var bad = false;
        foreach (var speed in new[] { 10f, 25f })
        {
            var w = new ConstructWorld();
            var q = new JointTestWorld();
            var bp = ConstructBuild.FindBlueprint("Gate");
            if (bp == null || ConstructBuild.BuildOnGround(w, bp, Vector3.Zero, 0, out _) is not { } gate)
            {
                Fail("construct joints: the Gate doesn't build");
                return;
            }
            StepJoints(w, q, 5);
            ref var jt = ref gate.Joints[0];
            var door = PartNamed(gate, "Door");
            gate.WorldFrame(jt.ParentSeg, out var pq, out var pt);
            var pivot = pt + Vector3.Transform(jt.Pivot, pq);
            // The door goes out along +x from its hinge and swings round towards +z or -z: a slab 5 cm thick 0.6 m off each side.
            var walls = new (Vector3 Min, Vector3 Max)[2];
            for (var side = 0; side < 2; side++)
            {
                var zc = pivot.Z + (side == 0 ? 0.6f : -0.6f);
                walls[side] = (new Vector3(pivot.X + 0.2f, 0, zc - 0.025f), new Vector3(pivot.X + 1.6f, 3, zc + 0.025f));
                q.Walls.Add(walls[side]);
            }
            jt.Open = true;
            jt.Vel = speed;
            gate.Wake();
            var through = false;
            var depth = 0f;
            var furthest = 0f;
            var corners = new Vector3[32];
            StepJoints(w, q, 90, _ =>
            {
                furthest = MathF.Max(furthest, MathF.Abs(gate.Joints[0].Angle));
                var n = PartCorners(gate, door, corners);
                for (var k = 0; k < n; k++)
                {
                    var c = corners[k];
                    // (past a wall's far face, within its length: through it)
                    through |= c.X > pivot.X + 0.3f && MathF.Abs(c.Z - pivot.Z) > 0.6f + 0.025f;
                }
                foreach (var wall in walls)
                    depth = MathF.Max(depth, BoxPen(gate, door, wall.Min, wall.Max));
            });
            parts.Add($"{speed:0} rad/s ({speed:0.0} m/s at the door's edge): it got to {Deg(furthest):0.0} deg (the wall meets it at about {Deg(MathF.Asin(0.55f)):0}), at worst {depth * 100:0.0} cm into the wall, through it {through}, rests at {Deg(MathF.Abs(gate.Joints[0].Angle)):0.0}");
            bad |= through || depth > 0.035f;
        }
        Line($"fast swing at a 5 cm wall: {string.Join("; ", parts)}");
        if (bad)
            Fail("construct joints: a fast swing went through a thin wall");
    }

    /// <summary>The yard's constructs left alone: every jointed one (the gates, the lift, the turret, the buggies' steering) falls asleep.</summary>
    private static void JointsSleep(Map yard)
    {
        var w = new ConstructWorld();
        w.Populate(yard.Sandbox!, World.Query);
        StepConstructs(w, 180);
        var jointed = w.Bodies.Where(b => b.JointCount > 0).ToList();
        var awake = jointed.Where(b => !b.Asleep).Select(b => $"{b.Id} ({b.PartCount} parts)").ToList();
        StepConstructs(w, 1);
        Line($"at rest: {jointed.Count} jointed constructs in the yard, {jointed.Count - awake.Count} asleep after 3 s{(awake.Count > 0 ? $" (awake: {string.Join(", ", awake)})" : "")}; {ConstructPhysics.LastRays} rays a tick");
        if (awake.Count > 0)
            Fail("construct joints: jointed constructs at rest didn't sleep");
    }

    /// <summary>A dozen jointed constructs moving at once (turrets turning, flaps falling): the tick inside its budget, no allocation.</summary>
    private static void JointsBudget()
    {
        var w = new ConstructWorld();
        var q = new JointTestWorld();
        var bp = ConstructBuild.FindBlueprint("Turret");
        for (var k = 0; k < 6; k++)
        {
            if (bp != null && ConstructBuild.BuildOnGround(w, bp, new Vector3(k * 5, 0, 0), 0, out _) is { } t)
                t.Parts[PartNamed(t, "Lever")].State = 1; // (forward: turning)
            BuildFlap(w, q, k * 5, 8);
        }
        foreach (var c in w.Bodies)
            if (c.JointCount > 0 && c.Joints[0].Type == JointType.Passive)
                c.Joints[0].Open = true;
        StepJoints(w, q, 30);
        var contacts = 0;
        Action<int> each = _ => contacts = Math.Max(contacts, ConstructPhysics.LastJointContacts);
        each(0);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var before = GC.GetAllocatedBytesForCurrentThread();
        StepJoints(w, q, 60, each);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var ms = sw.Elapsed.TotalMilliseconds / 60;
        var moving = w.Bodies.Count(b => b.JointCount > 0 && MathF.Abs(b.Joints[0].Vel) > 0.05f);
        Line($"budget: {w.Bodies.Count} jointed constructs ({moving} joints moving), up to {contacts} contacts beyond joints, {ConstructPhysics.LastRays} rays a tick; {ms:0.000} ms a tick, {bytes} bytes allocated in 60 ticks");
        if (bytes > 0)
            Fail($"construct joints: {bytes} bytes allocated in 60 ticks of jointed constructs (should be none)");
        if (ms > 2.0)
            Fail($"construct joints: {ms:0.00} ms a tick for a dozen jointed constructs (budget 2 ms of the 8 ms tick)");
    }
}
