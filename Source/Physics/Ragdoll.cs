using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The ragdoll skeleton every body shares (players, bots and zombies): 15 particles, the distance links between them and
/// the rest poses they start from (body space: feet at the origin, -Z = facing, +X = right). The torso is a rigid cluster
/// of six points (hips, shoulders, a belly point in front and a back point behind, all linked to each other, so it can't
/// fold), the head swings on the shoulder line, and arms and legs are two links each with their bend limited by
/// minimum-distance links (a shoulder can't come closer to its hand than a folded elbow allows, and so on). Knees also get
/// a one-sided "bend forward only" rule in <see cref="Ragdoll"/>.
/// </summary>
public static class RagdollRig
{
    public const int HipR = 0, HipL = 1, ShoulderR = 2, ShoulderL = 3, Belly = 4, Back = 5, Head = 6,
        ElbowR = 7, HandR = 8, ElbowL = 9, HandL = 10, KneeR = 11, FootR = 12, KneeL = 13, FootL = 14;

    /// <summary>Particles per body.</summary>
    public const int Count = 15;

    /// <summary>Collision radius of each particle (m): about the half-thickness of the boxes drawn there.</summary>
    public static readonly float[] Radius = [0.14f, 0.14f, 0.14f, 0.14f, 0.16f, 0.16f, 0.2f, 0.07f, 0.07f, 0.07f, 0.07f, 0.1f, 0.09f, 0.1f, 0.09f];

    /// <summary>Inverse mass of each particle: the torso is heavy, hands and feet light.</summary>
    public static readonly float[] InvMass = [0.7f, 0.7f, 0.7f, 0.7f, 0.6f, 0.6f, 1f, 1.3f, 1.6f, 1.3f, 1.6f, 1.1f, 1.3f, 1.1f, 1.3f];

    /// <summary>Hip joints (the legs' roots) and shoulders in the standing pose.</summary>
    public const float HipY = LegAnimator.StandPelvis - 0.04f, HipX = 0.19f, ShoulderY = 1.3f, ShoulderX = 0.28f;

    /// <summary>The standing rest pose (body space). Link lengths are measured on it.</summary>
    public static readonly Vector3[] Stand = MakeStand();

    /// <summary>Crouching: the upper body lowered and the knees bent forward.</summary>
    public static readonly Vector3[] Crouch = MakeCrouch();

    /// <summary>Prone: the standing pose tipped face down along the facing, as the prone hit box lies.</summary>
    public static readonly Vector3[] Prone = MakeProne();

    /// <summary>A link between two particles: rigid when <see cref="Min"/> equals <see cref="Max"/>, a range otherwise.</summary>
    public readonly record struct Link(byte A, byte B, float Min, float Max);

    /// <summary>Every link, rigid ones first (solved in this order each iteration).</summary>
    public static readonly Link[] Links = MakeLinks();

    /// <summary>Upper arm and forearm length, thigh and shin length (from the rest pose).</summary>
    public static readonly float UpperArm = Vector3.Distance(Stand[ShoulderR], Stand[ElbowR]), Forearm = Vector3.Distance(Stand[ElbowR], Stand[HandR]);

    /// <summary>Where the torso box centre sits between the hips' midpoint (0) and the shoulders' midpoint (1).</summary>
    public static readonly float TorsoAlong = (BodyModel.Stances[0].TorsoAt.Y - HipY) / (ShoulderY - HipY);

    private static Vector3[] MakeStand()
    {
        var head = BodyModel.Stances[0].HeadAt;
        var p = new Vector3[Count];
        for (var s = 0; s < 2; s++)
        {
            var x = s == 0 ? 1f : -1f;
            p[HipR + s] = new Vector3(x * HipX, HipY, 0);
            p[ShoulderR + s] = new Vector3(x * ShoulderX, ShoulderY, 0);
            p[ElbowR + s * 2] = new Vector3(x * 0.34f, 0.945f, 0.02f);
            p[HandR + s * 2] = new Vector3(x * 0.36f, 0.615f, -0.06f);
            p[KneeR + s * 2] = new Vector3(x * HipX, HipY - 0.42f, -0.13f);
            p[FootR + s * 2] = new Vector3(x * HipX, LegAnimator.AnkleHeight, 0.1f);
        }
        p[Belly] = new Vector3(0, 1.08f, -0.11f);
        p[Back] = new Vector3(0, 1.1f, 0.11f);
        p[Head] = head;
        return p;
    }

    private static Vector3[] MakeCrouch()
    {
        var p = (Vector3[])MakeStand().Clone();
        var down = new Vector3(0, -0.34f, 0.08f);
        for (var i = 0; i <= HandL; i++)
            p[i] += down;
        for (var s = 0; s < 2; s++)
        {
            var hip = p[HipR + s];
            var knee = hip + new Vector3(0, -0.06f, -0.436f);
            p[KneeR + s * 2] = knee;
            p[FootR + s * 2] = new Vector3(hip.X, LegAnimator.AnkleHeight, knee.Z + 0.2f);
        }
        return p;
    }

    private static Vector3[] MakeProne()
    {
        var p = MakeStand();
        var tip = Matrix4x4.CreateRotationX(-MathF.PI / 2) * Matrix4x4.CreateTranslation(0, 0.25f, 0.8f); // up -> forward, front -> down
        for (var i = 0; i < Count; i++)
            p[i] = Vector3.Transform(p[i], tip);
        return p;
    }

    private static Link[] MakeLinks()
    {
        var s = MakeStand();
        var list = new List<Link>();
        void Rigid(int a, int b)
        {
            var d = Vector3.Distance(s[a], s[b]);
            list.Add(new Link((byte)a, (byte)b, d, d));
        }
        void AtLeast(int a, int b, float min) => list.Add(new Link((byte)a, (byte)b, min, float.MaxValue));

        // Torso: all 15 pairs of the six points (a complete graph is rigid in 3D).
        for (var a = HipR; a <= Back; a++)
            for (var b = a + 1; b <= Back; b++)
                Rigid(a, b);
        // Head: on the shoulder line, nodding at most ~55 degrees either way (it can't get closer to the hips than that).
        Rigid(Head, ShoulderR);
        Rigid(Head, ShoulderL);
        AtLeast(Head, HipR, Vector3.Distance(s[Head], s[HipR]) * 0.9f);
        AtLeast(Head, HipL, Vector3.Distance(s[Head], s[HipL]) * 0.9f);
        for (var side = 0; side < 2; side++)
        {
            int sh = ShoulderR + side, el = ElbowR + side * 2, ha = HandR + side * 2;
            int hip = HipR + side, kn = KneeR + side * 2, ft = FootR + side * 2;
            Rigid(sh, el);
            Rigid(el, ha);
            AtLeast(sh, ha, 0.25f); // the elbow folds to about 140 degrees
            AtLeast(el, Belly, 0.24f); // arms stay outside the chest
            AtLeast(el, Back, 0.24f);
            AtLeast(ha, Belly, 0.24f);
            AtLeast(ha, Back, 0.24f);
            Rigid(hip, kn);
            Rigid(kn, ft);
            AtLeast(hip, ft, 0.3f); // the knee folds to about 140 degrees
            AtLeast(kn, Back, 0.58f); // the thigh swings ~100 degrees forward, ~45 back
            AtLeast(kn, sh, 0.55f);
            AtLeast(kn, HipL - side, 0.45f); // legs don't cross much
        }
        // Rigid links first: the range limits then correct what they bend.
        list.Sort((x, y) => (x.Min != x.Max).CompareTo(y.Min != y.Max));
        return list.ToArray();
    }
}

/// <summary>
/// One ragdoll: a Verlet particle body (<see cref="RagdollRig"/>) with gravity, distance links, a knee rule, and collision
/// of each particle (a sphere) against the world: the terrain by its height field, and the map's boxes, rotated boxes,
/// building mesh triangles and boundary walls gathered once for the region around the body from the physics world's BVH
/// (<see cref="PhysicsWorld.Gather"/>) and gathered again only when the body leaves that region. Fast bodies take
/// sub-steps so no particle moves more than <see cref="MaxStepMove"/> per sub-step (no tunnelling through walls or
/// floors). A body that has stopped moving goes to sleep and costs nothing until a blast wakes it.
///
/// Purely cosmetic and client-side: nothing here touches gameplay (no hit boxes, nothing blocks bullets or movement).
/// Deterministic: the same start and the same world give the same fall, step for step. No allocations once created.
/// </summary>
public sealed class Ragdoll
{
    /// <summary>Simulation step (s): 60 Hz like the game.</summary>
    public const float Step = 1f / 60f;

    /// <summary>Constraint iterations per (sub-)step.</summary>
    public const int Iterations = 6;

    /// <summary>Most a particle may move in one sub-step (m); faster bodies take more sub-steps (up to <see cref="MaxSubSteps"/>).</summary>
    public const float MaxStepMove = 0.07f;

    public const int MaxSubSteps = 6;

    /// <summary>Fastest a particle may move (m/s): even a point-blank blast can't throw a body faster.</summary>
    public const float MaxSpeed = 24f;

    /// <summary>Asleep after this many steps in a row with every particle slower than <see cref="SleepSpeed"/>.</summary>
    public const int SleepSteps = 30;

    public const float SleepSpeed = 0.25f;

    /// <summary>Forced to sleep after this many steps awake (s * 60), so a body jammed somewhere jittery stops costing.</summary>
    public const int MaxAwakeSteps = 60 * 9;

    /// <summary>Velocity kept per step in the air, and the share of sliding speed friction takes on contact per step.</summary>
    public const float AirDamping = 0.995f, Friction = 0.3f;

    public readonly Vector3[] Pos = new Vector3[RagdollRig.Count];
    public readonly Vector3[] Prev = new Vector3[RagdollRig.Count];

    /// <summary>Positions before the last full step (for drawing between steps).</summary>
    public readonly Vector3[] Last = new Vector3[RagdollRig.Count];

    public bool Asleep;
    public int QuietSteps, AwakeSteps;

    /// <summary>Something went wrong (a non-finite position, fell out of the world): the pool drops it.</summary>
    public bool Broken;

    /// <summary>Particles touching something this step (bit per particle).</summary>
    public int Touching;

    private readonly List<ContactShape> _shapes = new(64);
    private Vector3 _gatherMin, _gatherMax;
    private bool _gathered;
    private PhysicsWorld? _gatheredFrom;
    private int _gatheredGeneration;

    /// <summary>How many times any ragdoll gathered shapes, and how many steps ran (stats and the self-test).</summary>
    public static long Gathers, Steps;

    /// <summary>Bumped when the map's colliders change (a prop broke): every body gathers its shapes again.</summary>
    public static int WorldGeneration;

    /// <summary>Gather margin around the body (m).</summary>
    private const float GatherPad = 1.2f;

    private static readonly QueryFilter Filter = new(QueryLayers.StaticShapes | QueryLayers.Boundary);

    /// <summary>
    /// Places the body: <paramref name="pose"/> (a rest pose, body space) through <paramref name="toWorld"/>, every particle
    /// moving at <paramref name="velocity"/>. Clears sleep, contacts and the shape cache.
    /// </summary>
    public void Init(ReadOnlySpan<Vector3> pose, in Matrix4x4 toWorld, Vector3 velocity)
    {
        var dv = Clamp(velocity) * Step;
        for (var i = 0; i < RagdollRig.Count; i++)
        {
            var p = Vector3.Transform(pose[i], toWorld);
            Pos[i] = Last[i] = p;
            Prev[i] = p - dv;
        }
        Asleep = Broken = false;
        QuietSteps = AwakeSteps = 0;
        Touching = 0;
        _gathered = false;
    }

    /// <summary>Velocity of particle <paramref name="i"/> (m/s).</summary>
    public Vector3 Velocity(int i) => (Pos[i] - Prev[i]) / Step;

    /// <summary>Adds <paramref name="dv"/> (m/s) to particle <paramref name="i"/> (capped at <see cref="MaxSpeed"/>) and wakes the body.</summary>
    public void Push(int i, Vector3 dv)
    {
        if (!float.IsFinite(dv.X + dv.Y + dv.Z))
            return;
        var v = Clamp(Velocity(i) + dv);
        Prev[i] = Pos[i] - v * Step;
        Wake();
    }

    public void Wake()
    {
        Asleep = false;
        QuietSteps = 0;
        AwakeSteps = Math.Min(AwakeSteps, MaxAwakeSteps - 120); // a blast on a sleeping body gets at least 2 s to settle again
    }

    private static Vector3 Clamp(Vector3 v)
    {
        var l2 = v.LengthSquared();
        return l2 > MaxSpeed * MaxSpeed ? v * (MaxSpeed / MathF.Sqrt(l2)) : v;
    }

    /// <summary>The middle of the hips (world).</summary>
    public Vector3 Pelvis => (Pos[RagdollRig.HipR] + Pos[RagdollRig.HipL]) * 0.5f;

    /// <summary>One 60 Hz step against <paramref name="world"/> (no-op while asleep).</summary>
    public void Advance(PhysicsWorld world)
    {
        if (Asleep || Broken)
            return;
        Steps++;
        Array.Copy(Pos, Last, RagdollRig.Count);

        // Sub-steps: no particle moves more than MaxStepMove per sub-step.
        var maxMove2 = 0f;
        for (var i = 0; i < RagdollRig.Count; i++)
            maxMove2 = MathF.Max(maxMove2, (Pos[i] - Prev[i]).LengthSquared());
        var maxMove = MathF.Sqrt(maxMove2) + World.Gravity * Step * Step;
        var sub = Math.Clamp((int)MathF.Ceiling(maxMove / MaxStepMove), 1, MaxSubSteps);
        if (sub > 1)
            for (var i = 0; i < RagdollRig.Count; i++)
                Prev[i] = Pos[i] - (Pos[i] - Prev[i]) / sub;
        var h = Step / sub;
        var terrain = world.Terrain;
        Touching = 0;
        for (var s = 0; s < sub; s++)
        {
            var g = new Vector3(0, -World.Gravity * h * h, 0);
            for (var i = 0; i < RagdollRig.Count; i++)
            {
                var p = Pos[i];
                Pos[i] = p + (p - Prev[i]) * AirDamping + g;
                Prev[i] = p;
            }
            EnsureShapes(world);
            for (var it = 0; it < Iterations; it++)
            {
                SolveLinks();
                if ((it & 1) == 1)
                    Collide(terrain, it == Iterations - 1);
            }
        }
        if (sub > 1)
            for (var i = 0; i < RagdollRig.Count; i++)
                Prev[i] = Pos[i] - (Pos[i] - Prev[i]) * sub;

        // Sanity, sleep.
        var fastest2 = 0f;
        for (var i = 0; i < RagdollRig.Count; i++)
        {
            var p = Pos[i];
            if (!float.IsFinite(p.X + p.Y + p.Z) || p.Y < -500f)
            {
                Broken = true;
                return;
            }
            var v = Pos[i] - Prev[i];
            var l2 = v.LengthSquared();
            if (l2 > MaxSpeed * MaxSpeed * Step * Step)
                Prev[i] = Pos[i] - v * (MaxSpeed * Step / MathF.Sqrt(l2));
            fastest2 = MathF.Max(fastest2, l2);
        }
        AwakeSteps++;
        QuietSteps = fastest2 < SleepSpeed * SleepSpeed * Step * Step && Touching != 0 ? QuietSteps + 1 : 0;
        if (QuietSteps >= SleepSteps || AwakeSteps >= MaxAwakeSteps)
            Sleep();
    }

    /// <summary>Stops simulating: the body stays where it is (no velocity) until woken.</summary>
    public void Sleep()
    {
        Asleep = true;
        Array.Copy(Pos, Prev, RagdollRig.Count);
        Array.Copy(Pos, Last, RagdollRig.Count);
    }

    private void SolveLinks()
    {
        var links = RagdollRig.Links;
        var w = RagdollRig.InvMass;
        for (var k = 0; k < links.Length; k++)
        {
            ref readonly var l = ref links[k];
            var a = Pos[l.A];
            var b = Pos[l.B];
            var d = b - a;
            var len2 = d.LengthSquared();
            if (len2 < 1e-12f)
                continue;
            if (len2 >= l.Min * l.Min && len2 <= l.Max * l.Max && l.Min != l.Max)
                continue; // a range link inside its range
            var len = MathF.Sqrt(len2);
            var target = len < l.Min ? l.Min : len > l.Max ? l.Max : len;
            if (target == len)
                continue;
            float wa = w[l.A], wb = w[l.B];
            var k2 = (len - target) / (len * (wa + wb));
            Pos[l.A] = a + d * (wa * k2);
            Pos[l.B] = b - d * (wb * k2);
        }
        // Knees bend forward only: each knee stays in front of the line from its hip to its foot.
        var fwd = Forward();
        for (var side = 0; side < 2; side++)
        {
            int hip = RagdollRig.HipR + side, knee = RagdollRig.KneeR + side * 2, foot = RagdollRig.FootR + side * 2;
            var mid = (Pos[hip] + Pos[foot]) * 0.5f;
            var ahead = Vector3.Dot(Pos[knee] - mid, fwd);
            if (ahead < 0.02f)
                Pos[knee] += fwd * (0.02f - ahead);
        }
    }

    /// <summary>The torso's facing (world, unit): the way the chest points.</summary>
    public Vector3 Forward()
    {
        TorsoAxes(Pos, out _, out _, out var back);
        return -back;
    }

    /// <summary>
    /// The torso's axes from particle positions <paramref name="p"/>: right (+X), up (+Y) and back (+Z), orthonormal and
    /// right-handed like the body frame. Safe for degenerate input (falls back to the world axes).
    /// </summary>
    public static void TorsoAxes(ReadOnlySpan<Vector3> p, out Vector3 right, out Vector3 up, out Vector3 back)
    {
        var r = p[RagdollRig.ShoulderR] - p[RagdollRig.ShoulderL] + p[RagdollRig.HipR] - p[RagdollRig.HipL];
        var u = p[RagdollRig.ShoulderR] + p[RagdollRig.ShoulderL] - p[RagdollRig.HipR] - p[RagdollRig.HipL];
        up = u.LengthSquared() > 1e-10f ? Vector3.Normalize(u) : Vector3.UnitY;
        r -= up * Vector3.Dot(r, up);
        right = r.LengthSquared() > 1e-10f ? Vector3.Normalize(r) : MathF.Abs(up.X) < 0.9f ? Vector3.Normalize(Vector3.Cross(up, Vector3.UnitZ)) : Vector3.UnitZ;
        back = Vector3.Cross(right, up);
    }

    /// <summary>Gathers the shapes around the body when it has left the region gathered last time (or the world changed).</summary>
    private void EnsureShapes(PhysicsWorld world)
    {
        var lo = Pos[0];
        var hi = Pos[0];
        for (var i = 1; i < RagdollRig.Count; i++)
        {
            lo = Vector3.Min(lo, Pos[i]);
            hi = Vector3.Max(hi, Pos[i]);
        }
        lo -= new Vector3(0.3f);
        hi += new Vector3(0.3f);
        if (_gathered && ReferenceEquals(world, _gatheredFrom) && _gatheredGeneration == WorldGeneration
            && lo.X >= _gatherMin.X && lo.Y >= _gatherMin.Y && lo.Z >= _gatherMin.Z && hi.X <= _gatherMax.X && hi.Y <= _gatherMax.Y && hi.Z <= _gatherMax.Z)
            return;
        _gatherMin = lo - new Vector3(GatherPad);
        _gatherMax = hi + new Vector3(GatherPad);
        world.Gather(_gatherMin, _gatherMax, Filter, _shapes);
        _gathered = true;
        _gatheredFrom = world;
        _gatheredGeneration = WorldGeneration;
        Gathers++;
    }

    /// <summary>Pushes every particle out of the terrain and the gathered shapes; on the last pass, friction on what touched.</summary>
    private void Collide(ITerrainCollision? terrain, bool friction)
    {
        var radius = RagdollRig.Radius;
        var shapes = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_shapes);
        for (var i = 0; i < RagdollRig.Count; i++)
        {
            var p = Pos[i];
            var r = radius[i];
            var normal = Vector3.Zero;
            var hit = false;
            if (terrain != null)
            {
                var h = terrain.HasVoids ? terrain.GroundBelow(p.X, p.Z, p.Y + r) : terrain.HeightAt(p.X, p.Z);
                if (float.IsFinite(h) && p.Y - r < h && p.Y - r > h - 3f)
                {
                    p.Y = h + r;
                    normal = terrain.NormalAt(p.X, p.Z);
                    hit = true;
                }
            }
            for (var k = 0; k < shapes.Length; k++)
            {
                ref readonly var s = ref shapes[k];
                if (p.X + r < s.Min.X || p.X - r > s.Max.X || p.Y + r < s.Min.Y || p.Y - r > s.Max.Y || p.Z + r < s.Min.Z || p.Z - r > s.Max.Z)
                    continue;
                if (SpherePush(s, ref p, r, out var n))
                {
                    normal += n;
                    hit = true;
                }
            }
            if (!hit)
                continue;
            Pos[i] = p;
            Touching |= 1 << i;
            if (!friction)
                continue;
            var nl = normal.LengthSquared();
            if (nl < 1e-12f)
                continue;
            var nn = normal / MathF.Sqrt(nl);
            var v = p - Prev[i];
            var vn = Vector3.Dot(v, nn);
            var vt = v - nn * vn;
            var keep = vn < 0 ? vt * (1f - Friction) : vt * (1f - Friction) + nn * vn; // no bounce into the surface
            Prev[i] = p - keep;
        }
    }

    /// <summary>Moves the sphere (centre <paramref name="p"/>, radius <paramref name="r"/>) out of one shape. True if it touched; <paramref name="n"/> is the push direction.</summary>
    public static bool SpherePush(in ContactShape s, ref Vector3 p, float r, out Vector3 n)
    {
        if (s.Type == ContactShape.TypeBox)
        {
            ref readonly var b = ref s.Box;
            var local = b.ToLocal(p);
            var c = Vector3.Clamp(local, -b.Half, b.Half);
            var d = local - c;
            var d2 = d.LengthSquared();
            if (d2 >= r * r)
            {
                n = default;
                return false;
            }
            Vector3 ln;
            float depth;
            if (d2 > 1e-12f)
            {
                var dist = MathF.Sqrt(d2);
                ln = d / dist;
                depth = r - dist;
            }
            else
            {
                // The centre is inside: out through the nearest face.
                var gap = b.Half - Vector3.Abs(local);
                var axis = gap.X <= gap.Y && gap.X <= gap.Z ? 0 : gap.Y <= gap.Z ? 1 : 2;
                var sign = local[axis] >= 0 ? 1f : -1f;
                ln = Geometry.Axis(axis, sign);
                depth = gap[axis] + r;
            }
            n = b.DirToWorld(ln);
            p += n * depth;
            return true;
        }
        var q = Geometry.ClosestPointTriangle(p, s.A, s.B, s.C);
        var e = p - q;
        var e2 = e.LengthSquared();
        if (e2 >= r * r)
        {
            n = default;
            return false;
        }
        if (e2 > 1e-12f)
        {
            var dist = MathF.Sqrt(e2);
            n = e / dist;
            if (s.OneSided && Vector3.Dot(n, Geometry.TriangleNormal(s.A, s.B, s.C)) < 0)
                n = Geometry.TriangleNormal(s.A, s.B, s.C); // behind a one-sided face: out the front
            p += n * (r - dist);
        }
        else
        {
            n = Geometry.TriangleNormal(s.A, s.B, s.C);
            p += n * r;
        }
        return true;
    }

    /// <summary>The shapes gathered last (self-test).</summary>
    public int ShapeCount => _shapes.Count;
}
