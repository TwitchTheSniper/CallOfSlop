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

    /// <summary>
    /// The relaxed pose a still body's limbs drift towards (<see cref="Ragdoll"/>'s relax pull, relative to the torso):
    /// arms hanging by the body with the elbows a little bent and the hands a little forward, knees a little bent, feet
    /// under the hips. Not the standing pose: what a body lying limp looks like. Link lengths are the standing pose's.
    /// </summary>
    public static readonly Vector3[] Relaxed = MakeRelaxed();

    /// <summary>The middle of the hips in <see cref="Relaxed"/> (the relax pull's origin).</summary>
    public static readonly Vector3 RelaxedMidHip = (Relaxed[HipR] + Relaxed[HipL]) * 0.5f;

    /// <summary>
    /// Each particle's neighbours (a shot's push spreads to them): <see cref="NeighbourStart"/>[i] to
    /// <see cref="NeighbourStart"/>[i + 1] in <see cref="NeighbourList"/>.
    /// </summary>
    public static readonly byte[] NeighbourList, NeighbourStart;

    /// <summary>A link between two particles: rigid when <see cref="Min"/> equals <see cref="Max"/>, a range otherwise.</summary>
    public readonly record struct Link(byte A, byte B, float Min, float Max);

    /// <summary>Every link, rigid ones first (solved in this order each iteration).</summary>
    public static readonly Link[] Links = MakeLinks();

    /// <summary>Upper arm and forearm length, thigh and shin length (from the rest pose).</summary>
    public static readonly float UpperArm = Vector3.Distance(Stand[ShoulderR], Stand[ElbowR]), Forearm = Vector3.Distance(Stand[ElbowR], Stand[HandR]);

    /// <summary>Where the torso box centre sits between the hips' midpoint (0) and the shoulders' midpoint (1).</summary>
    public static readonly float TorsoAlong = (BodyModel.Stances[0].TorsoAt.Y - HipY) / (ShoulderY - HipY);

    static RagdollRig()
    {
        // (A static constructor runs after every field initialiser above, so Stand and the link lengths are ready.)
        byte[][] n =
        [
            [KneeR, Belly, Back], [KneeL, Belly, Back], // hips
            [Head, ElbowR, Belly, Back], [Head, ElbowL, Belly, Back], // shoulders
            [ShoulderR, ShoulderL, HipR, HipL], [ShoulderR, ShoulderL, HipR, HipL], // belly, back
            [ShoulderR, ShoulderL], // head
            [ShoulderR, HandR], [ElbowR], [ShoulderL, HandL], [ElbowL], // arms
            [HipR, FootR], [KneeR], [HipL, FootL], [KneeL], // legs
        ];
        NeighbourStart = new byte[Count + 1];
        var list = new List<byte>();
        for (var i = 0; i < Count; i++)
        {
            NeighbourStart[i] = (byte)list.Count;
            list.AddRange(n[i]);
        }
        NeighbourStart[Count] = (byte)list.Count;
        NeighbourList = list.ToArray();
    }

    private static Vector3[] MakeRelaxed()
    {
        var p = MakeStand();
        var upper = Vector3.Distance(p[ShoulderR], p[ElbowR]);
        var fore = Vector3.Distance(p[ElbowR], p[HandR]);
        var thigh = Vector3.Distance(p[HipR], p[KneeR]);
        var shin = Vector3.Distance(p[KneeR], p[FootR]);
        for (var s = 0; s < 2; s++)
        {
            var x = s == 0 ? 1f : -1f;
            var el = p[ShoulderR + s] + Vector3.Normalize(new Vector3(x * 0.12f, -1f, -0.12f)) * upper;
            p[ElbowR + s * 2] = el;
            p[HandR + s * 2] = el + Vector3.Normalize(new Vector3(x * 0.05f, -0.8f, -0.55f)) * fore;
            var kn = p[HipR + s] + Vector3.Normalize(new Vector3(x * 0.03f, -1f, -0.15f)) * thigh;
            p[KneeR + s * 2] = kn;
            p[FootR + s * 2] = kn + Vector3.Normalize(new Vector3(0, -1f, 0.2f)) * shin;
        }
        return p;
    }

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

    /// <summary>Constraint iterations per (sub-)step (collisions after the 2nd and the 4th).</summary>
    public const int Iterations = 4;

    /// <summary>Most a particle may move in one sub-step (m); faster bodies take more sub-steps (up to <see cref="MaxSubSteps"/>).</summary>
    public const float MaxStepMove = 0.15f;

    public const int MaxSubSteps = 4;

    /// <summary>Fastest a particle may move (m/s): even a point-blank blast can't throw a body faster.</summary>
    public const float MaxSpeed = 24f;

    /// <summary>Asleep after this many steps in a row with every particle slower than <see cref="SleepSpeed"/>.</summary>
    public const int SleepSteps = 20;

    public const float SleepSpeed = 0.35f;

    /// <summary>How far (m) a knee may bend backwards past straight before the knee rule pushes it back.</summary>
    public const float KneeSlack = 0.01f;

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

    /// <summary>Powered by a death reaction's muscles (<see cref="RagdollActive"/>): it doesn't relax or fall asleep meanwhile.</summary>
    public bool Driven;

    /// <summary>Something went wrong (a non-finite position, fell out of the world): the pool drops it.</summary>
    public bool Broken;

    /// <summary>Particles touching something this step (bit per particle).</summary>
    public int Touching;

    private readonly List<ContactShape> _shapes = new(64);

    // The gathered shapes that reach the body this sub-step (indices into _shapes); -1 = use them all.
    private readonly int[] _near = new int[48];
    private int _nearCount;
    private float _terrainLow = float.NegativeInfinity;

    /// <summary>
    /// A link ready to solve: the particles, its limits (and squares), its length in the standing pose (where a range
    /// link's soft zone is measured from) and each end's share of the correction.
    /// </summary>
    private readonly struct Solver(RagdollRig.Link l)
    {
        public readonly int A = l.A, B = l.B;
        public readonly float Min = l.Min, Max = l.Max, Min2 = l.Min * l.Min, Max2 = l.Max == float.MaxValue ? float.MaxValue : l.Max * l.Max;
        public readonly float Rest = Vector3.Distance(RagdollRig.Stand[l.A], RagdollRig.Stand[l.B]);
        public readonly float Ka = RagdollRig.InvMass[l.A] / (RagdollRig.InvMass[l.A] + RagdollRig.InvMass[l.B]);
        public readonly float Kb = RagdollRig.InvMass[l.B] / (RagdollRig.InvMass[l.A] + RagdollRig.InvMass[l.B]);
    }

    private static readonly Solver[] Rigid = RagdollRig.Links.Where(l => l.Min == l.Max).Select(l => new Solver(l)).ToArray();
    private static readonly Solver[] Ranges = RagdollRig.Links.Where(l => l.Min != l.Max).Select(l => new Solver(l)).ToArray();
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
        Asleep = Broken = Driven = false;
        QuietSteps = AwakeSteps = RelaxSteps = 0;
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
        QuietSteps = RelaxSteps = 0;
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
        PrepareSoft();

        // Sub-steps: no particle moves more than MaxStepMove per sub-step.
        var maxMove2 = 0f;
        for (var i = 0; i < RagdollRig.Count; i++)
            maxMove2 = MathF.Max(maxMove2, (Pos[i] - Prev[i]).LengthSquared());
        var maxMove = MathF.Sqrt(maxMove2) + World.Gravity * Step * Step * MathF.Max(1f, World.GravityScale);
        var sub = Math.Clamp((int)MathF.Ceiling(maxMove / MaxStepMove), 1, MaxSubSteps);
        if (sub > 1)
            for (var i = 0; i < RagdollRig.Count; i++)
                Prev[i] = Pos[i] - (Pos[i] - Prev[i]) / sub;
        var h = Step / sub;
        var terrain = world.Terrain;
        Touching = 0;
        var g = new Vector3(0, -World.Gravity * h * h * World.GravityScale, 0);
        for (var s = 0; s < sub; s++)
        {
            for (var i = 0; i < RagdollRig.Count; i++)
            {
                var p = Pos[i];
                Pos[i] = p + (p - Prev[i]) * AirDamping + (GravityFrames.Curved ? GravityFrames.GravityAt(p) * (h * h) : g); // (a planet: towards its centre)
                Prev[i] = p;
            }
            EnsureShapes(world, terrain);
            var fwd = Forward(); // the knee rule's "forward", once per sub-step (the torso is rigid, it barely turns within one)
            for (var it = 0; it < Iterations; it++)
            {
                SolveLinks(fwd, it == Iterations - 1);
                if ((it & 1) == 1)
                    Collide(terrain, it == Iterations - 1);
            }
        }
        if (sub > 1)
            for (var i = 0; i < RagdollRig.Count; i++)
                Prev[i] = Pos[i] - (Pos[i] - Prev[i]) * sub;

        var relaxing = !Driven && RelaxPull();

        // Sanity, sleep.
        var fastest2 = 0f;
        var moved2 = 0f;
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
            moved2 = MathF.Max(moved2, (p - Last[i]).LengthSquared());
        }
        AwakeSteps++;
        // Still: every particle slow and something touching; while the relax pull works, also nothing still drifting
        // towards the relaxed pose (a limb the floor holds back doesn't drift: it can still fall asleep).
        var quietMove = RagdollTuning.Values[(int)RT.NeutralQuiet];
        var still = fastest2 < SleepSpeed * SleepSpeed * Step * Step && Touching != 0 && (!relaxing || moved2 < quietMove * quietMove);
        QuietSteps = still && !Driven ? QuietSteps + 1 : 0;
        if (QuietSteps >= SleepSteps || AwakeSteps >= MaxAwakeSteps)
            Sleep();
    }

    // The relax pull's particles and their weights' tuning ids (arms, legs, head).
    private static readonly byte[] RelaxParticles =
        [RagdollRig.ElbowR, RagdollRig.HandR, RagdollRig.ElbowL, RagdollRig.HandL, RagdollRig.KneeR, RagdollRig.FootR, RagdollRig.KneeL, RagdollRig.FootL, RagdollRig.Head];
    private static readonly RT[] RelaxWeights = [RT.NeutralArms, RT.NeutralArms, RT.NeutralArms, RT.NeutralArms, RT.NeutralLegs, RT.NeutralLegs, RT.NeutralLegs, RT.NeutralLegs, RT.NeutralHead];

    /// <summary>The relax pull's strength last step (0-1; self-test), and the steps it has worked since the body was placed or woken.</summary>
    public float Relax;
    public int RelaxSteps;

    /// <summary>The share of the way a relaxing limb's speed (across gravity) moves to the drive's per step.</summary>
    private const float RelaxBlend = 0.5f;

    // The torso particles' total mass (the relax pull's reaction is spread over them by mass).
    private static readonly float TorsoMass = 1f / RagdollRig.InvMass[0] + 1f / RagdollRig.InvMass[1] + 1f / RagdollRig.InvMass[2] + 1f / RagdollRig.InvMass[3] + 1f / RagdollRig.InvMass[4] + 1f / RagdollRig.InvMass[5];

    /// <summary>
    /// Once a body slows down, its limbs drift towards the relaxed pose (<see cref="RagdollRig.Relaxed"/>, placed on the
    /// torso as it lies): a weak, capped drive speed (NeutralPull of the way per step, at most NeutralMaxStep) that the
    /// limb's own speed across gravity blends towards, so it can't pump energy in or fight a limb held back, fading in as the fastest particle drops from NeutralSpeedNone to NeutralSpeedFull, for at most
    /// NeutralTime seconds (then it fades out, so nothing can keep the body awake). The torso takes the opposite change of
    /// momentum, so the pull is internal: it can't slide the body along the floor. Returns true when it drove something.
    /// </summary>
    private bool RelaxPull()
    {
        var tune = RagdollTuning.Values;
        var pull = tune[(int)RT.NeutralPull];
        var maxStep = tune[(int)RT.NeutralMaxStep];
        Relax = 0;
        if (pull <= 0 || maxStep <= 0)
            return false;
        var fastest2 = 0f;
        for (var i = 0; i < RagdollRig.Count; i++)
            fastest2 = MathF.Max(fastest2, (Pos[i] - Prev[i]).LengthSquared());
        var speed = MathF.Sqrt(fastest2) / Step;
        float full = tune[(int)RT.NeutralSpeedFull], none = MathF.Max(tune[(int)RT.NeutralSpeedNone], full + 0.01f);
        var w = Math.Clamp((none - speed) / (none - full), 0f, 1f);
        w *= Math.Clamp((tune[(int)RT.NeutralTime] * 60f - RelaxSteps) / 30f, 0f, 1f);
        if (w <= 0)
            return false;
        w = w * w * (3f - 2f * w);
        Relax = w;
        RelaxSteps++;
        var reaction = Vector3.Zero;
        TorsoAxes(Pos, out var right, out var up, out var back);
        var mid = Pelvis;
        var moved = false;
        for (var k = 0; k < RelaxParticles.Length; k++)
        {
            int i = RelaxParticles[k];
            var r = RagdollRig.Relaxed[i] - RagdollRig.RelaxedMidHip;
            var target = mid + right * r.X + up * r.Y + back * r.Z;
            // The drive: a velocity towards the target (capped), blended into the particle's own, across gravity only
            // (a limp limb is never lifted, and gravity keeps the say on falling). A limb something holds back just
            // stops: its velocity is what the solver left it, so nothing pumps.
            var gUp = GravityFrames.Curved ? GravityFrames.UpAt(Pos[i]) : Vector3.UnitY;
            var want = (target - Pos[i]) * (pull * tune[(int)RelaxWeights[k]]);
            want -= gUp * Vector3.Dot(want, gUp);
            var l2 = want.LengthSquared();
            if (l2 > maxStep * maxStep)
                want *= maxStep / MathF.Sqrt(l2);
            var v = Pos[i] - Prev[i];
            v -= gUp * Vector3.Dot(v, gUp);
            var dv = (want - v) * (RelaxBlend * w);
            if (dv.LengthSquared() < 1e-14f)
                continue;
            Prev[i] -= dv;
            reaction += dv / RagdollRig.InvMass[i];
            moved = true;
        }
        if (moved)
        {
            reaction /= -TorsoMass; // the torso takes the opposite change of momentum
            for (var i = RagdollRig.HipR; i <= RagdollRig.Back; i++)
                Prev[i] -= reaction;
        }
        return moved;
    }

    /// <summary>How far the relax pull's particles are from the relaxed pose on the torso as it lies (mean, m; self-test, smoke log).</summary>
    public float RelaxError()
    {
        TorsoAxes(Pos, out var right, out var up, out var back);
        var mid = Pelvis;
        var sum = 0f;
        foreach (int i in RelaxParticles)
        {
            var r = RagdollRig.Relaxed[i] - RagdollRig.RelaxedMidHip;
            sum += Vector3.Distance(mid + right * r.X + up * r.Y + back * r.Z, Pos[i]);
        }
        return sum / RelaxParticles.Length;
    }

    /// <summary>Stops simulating: the body stays where it is (no velocity) until woken.</summary>
    public void Sleep()
    {
        Asleep = true;
        Array.Copy(Pos, Prev, RagdollRig.Count);
        Array.Copy(Pos, Last, RagdollRig.Count);
    }

    /// <summary>One solver pass: rigid links, the soft joint limits and the knees; <paramref name="damp"/> (the last pass of a sub-step): also the soft limits' damping.</summary>
    private void SolveLinks(Vector3 fwd, bool damp)
    {
        var pos = Pos.AsSpan();
        foreach (ref readonly var l in Rigid.AsSpan())
        {
            var d = pos[l.B] - pos[l.A];
            var len2 = d.LengthSquared();
            if (len2 < 1e-12f)
                continue;
            var len = MathF.Sqrt(len2);
            var k = (len - l.Min) / len;
            pos[l.A] += d * (k * l.Ka);
            pos[l.B] -= d * (k * l.Kb);
        }
        // Range links (the joint limits) are soft: free inside the comfortable range, then a spring that stiffens as
        // k * t^exponent (t: 0 where the soft zone starts, 1 at the limit) pushes the joint back, full strength past the
        // limit, and a hard backstop just past it that nothing bends beyond (RagdollTuning; damping in DampSoftLimits).
        var tune = RagdollTuning.Values;
        var stiff = tune[(int)RT.SoftStiffness];
        var damping = damp ? tune[(int)RT.SoftDamping] : 0f;
        var prev = Prev.AsSpan();
        var ranges = Ranges.AsSpan();
        for (var r = 0; r < ranges.Length; r++)
        {
            ref readonly var l = ref ranges[r];
            var d = pos[l.B] - pos[l.A];
            var len2 = d.LengthSquared();
            if (len2 >= SoftLen2[r] && len2 <= l.Max2 || len2 < 1e-12f)
                continue; // inside its comfortable range (the usual case)
            var len = MathF.Sqrt(len2);
            float want, t = 1f;
            if (len2 > l.Max2)
                want = l.Max; // (a maximum stays hard: no link has one today)
            else
            {
                var soft = SoftLen[r];
                if (len > l.Min)
                    t = (soft - len) * SoftInvSpan[r];
                want = MathF.Max(len + (soft - len) * (t >= 1f ? stiff : stiff * Curve(t)), BackLen[r]);
            }
            var c = (len - want) / len;
            pos[l.A] += d * (c * l.Ka);
            pos[l.B] -= d * (c * l.Kb);
            if (damping > 0)
            {
                // The damping: part of the joint's bending speed along the link goes (both ends share it: momentum kept).
                var n = d / len;
                var dv = Vector3.Dot(pos[l.B] - prev[l.B] - (pos[l.A] - prev[l.A]), n) * damping * MathF.Min(t, 1f);
                prev[l.A] -= n * (dv * l.Ka);
                prev[l.B] += n * (dv * l.Kb);
            }
        }
        // Knees bend forward only: a knee in front of the line from its hip to its foot by less than KneeSoft gets the same
        // kind of spring (full strength once it's KneeSlack behind the line), and a hard stop KneeHard further back. The fix
        // moves the knee forward and the hip and foot back (momentum kept), so a straight leg lying on the floor isn't
        // pushed into it every step (that fight kept bodies creeping and never asleep).
        EnsureCurve(tune[(int)RT.SoftExponent]);
        float kneeSoft = tune[(int)RT.KneeSoft], kneeHard = -KneeSlack - tune[(int)RT.KneeHard];
        for (var side = 0; side < 2; side++)
        {
            int hip = RagdollRig.HipR + side, knee = RagdollRig.KneeR + side * 2, foot = RagdollRig.FootR + side * 2;
            var mid = (pos[hip] + pos[foot]) * 0.5f;
            var ahead = Vector3.Dot(pos[knee] - mid, fwd);
            if (ahead >= kneeSoft)
                continue;
            var t = (kneeSoft - ahead) / (kneeSoft + KneeSlack);
            var push = (kneeSoft - ahead) * (t >= 1f ? stiff : stiff * Curve(t));
            push = MathF.Max(push, (kneeHard - ahead) * 1.2f); // the backstop (the knee moves 5/6 of the push relative to the line)
            pos[knee] += fwd * (push * (2f / 3f));
            pos[hip] -= fwd * (push / 6f);
            pos[foot] -= fwd * (push / 6f);
        }
    }

    // t^SoftExponent for t in 0..1, as a table (MathF.Pow per link per pass cost a fifth of the step): rebuilt when the exponent changes.
    private const int CurveSteps = 64;
    private static readonly float[] CurveTable = new float[CurveSteps + 2];
    private static float _curveExp = float.NaN;

    private static void EnsureCurve(float exp)
    {
        if (exp == _curveExp)
            return;
        _curveExp = exp;
        for (var i = 0; i <= CurveSteps; i++)
            CurveTable[i] = MathF.Pow(i / (float)CurveSteps, exp);
        CurveTable[CurveSteps + 1] = 1f;
    }

    /// <summary>t^SoftExponent (t clamped to 0..1), from the table.</summary>
    private static float Curve(float t)
    {
        var x = Math.Clamp(t, 0f, 1f) * CurveSteps;
        var i = (int)x;
        return CurveTable[i] + (CurveTable[i + 1] - CurveTable[i]) * (x - i);
    }

    // Each range link's soft zone (its start, squared, 1 / its width) and backstop, from the tuning (rebuilt when it changes).
    private static readonly float[] SoftLen = new float[Ranges.Length], SoftLen2 = new float[Ranges.Length], SoftInvSpan = new float[Ranges.Length], BackLen = new float[Ranges.Length];
    private static float _softRange = float.NaN, _hardMargin = float.NaN;

    /// <summary>
    /// Where each range link's soft zone starts (SoftRange of the way from its limit to its standing length; a link whose
    /// standing length is at its limit has none) and where its backstop is (HardMargin past the limit).
    /// </summary>
    private static void PrepareSoft()
    {
        var tune = RagdollTuning.Values;
        float softRange = tune[(int)RT.SoftRange], hard = tune[(int)RT.HardMargin];
        if (softRange == _softRange && hard == _hardMargin)
            return;
        (_softRange, _hardMargin) = (softRange, hard);
        for (var r = 0; r < Ranges.Length; r++)
        {
            ref readonly var l = ref Ranges[r];
            var soft = l.Rest > l.Min ? l.Min + (l.Rest - l.Min) * softRange : l.Min;
            SoftLen[r] = soft;
            SoftLen2[r] = soft * soft;
            SoftInvSpan[r] = soft - l.Min > 1e-5f ? 1f / (soft - l.Min) : 0f;
            BackLen[r] = l.Min * (1f - hard);
        }
    }

    /// <summary>Self-test: range link <paramref name="r"/>'s soft start and backstop (m).</summary>
    public static (float Soft, float Back) SoftZone(int r)
    {
        PrepareSoft();
        return (SoftLen[r], BackLen[r]);
    }

    /// <summary>Self-test: the range links' particles and limits, in solve order.</summary>
    public static (int A, int B, float Min) RangeLink(int r) => (Ranges[r].A, Ranges[r].B, Ranges[r].Min);

    public static int RangeCount => Ranges.Length;

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
    private void EnsureShapes(PhysicsWorld world, ITerrainCollision? terrain)
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
        if (!(_gathered && ReferenceEquals(world, _gatheredFrom) && _gatheredGeneration == WorldGeneration
              && lo.X >= _gatherMin.X && lo.Y >= _gatherMin.Y && lo.Z >= _gatherMin.Z && hi.X <= _gatherMax.X && hi.Y <= _gatherMax.Y && hi.Z <= _gatherMax.Z))
        {
            _gatherMin = lo - new Vector3(GatherPad);
            _gatherMax = hi + new Vector3(GatherPad);
            world.Gather(_gatherMin, _gatherMax, Filter, _shapes);
            _gathered = true;
            _gatheredFrom = world;
            _gatheredGeneration = WorldGeneration;
            Gathers++;
        }
        // Broadphase for this sub-step: only the shapes whose bounds reach the body (usually the floor and maybe a wall).
        _nearCount = 0;
        var all = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_shapes);
        for (var k = 0; k < all.Length; k++)
        {
            ref readonly var sh = ref all[k];
            if (sh.Min.X > hi.X || sh.Max.X < lo.X || sh.Min.Y > hi.Y || sh.Max.Y < lo.Y || sh.Min.Z > hi.Z || sh.Max.Z < lo.Z)
                continue;
            if (_nearCount == _near.Length)
            {
                _nearCount = -1; // a crowded spot: test them all
                break;
            }
            _near[_nearCount++] = k;
        }
        // The terrain only if the ground under the body's middle comes within reach (a body on a roof skips it).
        _terrainLow = float.NegativeInfinity;
        if (terrain != null && !terrain.HasVoids)
        {
            var mid = (lo + hi) * 0.5f;
            var reach = MathF.Max(hi.X - lo.X, hi.Z - lo.Z);
            if (lo.Y > terrain.HeightAt(mid.X, mid.Z) + reach + 1f)
                _terrainLow = float.PositiveInfinity; // well above the ground: no terrain this sub-step
        }
    }

    /// <summary>Pushes every particle out of the terrain and the gathered shapes; on the last pass, friction on what touched.</summary>
    private void Collide(ITerrainCollision? terrain, bool friction)
    {
        var radius = RagdollRig.Radius;
        var shapes = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_shapes);
        var near = _nearCount < 0 ? null : _near;
        var count = _nearCount < 0 ? shapes.Length : _nearCount;
        if (float.IsPositiveInfinity(_terrainLow))
            terrain = null;
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
            for (var j = 0; j < count; j++)
            {
                ref readonly var s = ref shapes[near == null ? j : near[j]];
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
