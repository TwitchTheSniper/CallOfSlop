using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Lifters' drawing (docs/sandbox/design/arms-and-cranes.md, section 6): an arm's turning half, posed each frame from its hand by
/// <see cref="ArmIk.Solve"/> (a turret on the part's slewing ring, a shoulder, the upper arm, an elbow, the forearm, a wrist and a
/// gripper), and the piece at true size in its paint on the move's path (ConstructLifters.Pose), run on from the last word the
/// peer heard. The pose comes from the hand alone, so host and client draw the same arm. Nothing is drawn for a lifter more than
/// <see cref="DrawNear"/> away. Allocates nothing.
/// </summary>
public static class LifterRender
{
    public const float DrawNear = 150f;
    private static readonly Vector3 Out = new(0.94f, 0.47f, 0.09f), Accent = new(0.95f, 0.69f, 0.12f);
    private static double _scanAt, _drawnAt;
    private static float _dt;

    public static void Draw(Renderer gfx, ConstructWorld world)
    {
        var now = ConstructConveyor.Clock;
        _dt = (float)Math.Clamp(now - _drawnAt, 0, 0.1);
        _drawnAt = now;
        if (world.LiftBusy == 0 && now - _scanAt > 1.0)
        {
            _scanAt = now; // (a client finds its lifters itself; a host's own step looks while a hand is busy)
            ConstructLifters.Scan(world, server: false);
        }
        var eye = gfx.CameraPosition;
        for (var bi = 0; bi < world.LiftBodies.Count; bi++)
        {
            var lb = world.LiftBodies[bi];
            if (world.Get(lb.Body) is not { } c)
                continue;
            for (var i = 0; i < lb.N; i++)
            {
                var l = lb.Lifts[i];
                if (l.Part >= c.PartCount || ConstructLifters.DefOf(c, l.Part) is not { } ld || c.Parts[l.Part].Broken)
                    continue;
                ConstructRender.PartDrawn(c, l.Part, out var lp, out var lr); // (as its body is drawn this frame, not the tick's pose: no jitter on a moving body)
                var shoulder = lp + Vector3.Transform(ld.Base, lr);
                // The hand, in the part's frame: the piece's origin and turn on the path, the gripper bar on its top.
                var turn = Quaternion.Identity;
                var age = ConstructLifters.AgeNow(l);
                var origin = l.Moved ? ConstructLifters.Pose(ld, l, age, out turn) : ConstructLifters.RestOf(ld); // (at rest before its first move)
                // Culled by the nearer of its base and its hand: a crane's bridge and piece may be 300 m along its rails from its cabinet.
                var handAt = lp + Vector3.Transform(origin, lr);
                if (Vector3.DistanceSquared(eye, shoulder) > DrawNear * DrawNear && Vector3.DistanceSquared(eye, handAt) > DrawNear * DrawNear)
                    continue;
                var seen = _lookBody == c.Id && _lookPart == l.Part && now - _lookAt < 0.3 || CabBody == c.Id && CabPart == l.CabPart && l.CabPart >= 0; // (phase 8: and while its cab is sat in here)
                if (seen && ld.Bridge)
                    DrawCraneReach(gfx, ld, Matrix4x4.CreateFromQuaternion(lr) * Matrix4x4.CreateTranslation(lp));
                else if (seen)
                    DrawReach(gfx, world, c, l.Part, ld, Matrix4x4.CreateFromQuaternion(lr) * Matrix4x4.CreateTranslation(lp), shoulder);
                if (CabBody == c.Id && CabPart == l.CabPart && l.CabPart >= 0)
                {
                    // (phase 8: the hook's shadow on what is under it, for the cab: a dark square 2 cm over the first thing below)
                    var below = World.Map.GroundHeight(handAt.X, handAt.Z);
                    if (world.Raycast(handAt + Vector3.UnitY * 0.05f, -Vector3.UnitY, 80f, out var under, noTracks: true))
                        below = MathF.Max(below, handAt.Y + 0.05f - under.Distance);
                    gfx.Draw(Matrix4x4.CreateScale(0.7f, 0.01f, 0.7f) * Matrix4x4.CreateTranslation(handAt.X, below + 0.02f, handAt.Z), new Vector3(0.05f, 0.05f, 0.06f));
                }
                var grip = origin + Vector3.UnitY * ConstructLifters.GripLift;
                var span = 0.35f;
                var part = Matrix4x4.CreateFromQuaternion(lr) * Matrix4x4.CreateTranslation(lp);
                if (l.Def != 0 && ConstructParts.Get(l.Def) is { } d)
                {
                    var m = Matrix4x4.CreateFromQuaternion(turn) * Matrix4x4.CreateTranslation(origin) * part;
                    var paint = l.Paint != 0 ? ConstructPaint.ToVector(l.Paint) : Vector3.Zero;
                    for (var bx = 0; bx < d.Boxes.Length; bx++)
                    {
                        ref readonly var box = ref d.Boxes[bx];
                        if (!box.Hidden)
                            gfx.Draw(box.Model(0, box.Center) * m, l.Paint != 0 && box.Paintable ? paint : box.Color, shape: box.Shape);
                    }
                    var top = new Vector3((d.BoundsMin.X + d.BoundsMax.X) * 0.5f, d.BoundsMax.Y, (d.BoundsMin.Z + d.BoundsMax.Z) * 0.5f);
                    grip = origin + Vector3.Transform(top, turn) + Vector3.UnitY * ConstructLifters.GripLift;
                    span = Math.Clamp(MathF.Min(d.BoundsMax.X - d.BoundsMin.X, d.BoundsMax.Z - d.BoundsMin.Z) * 0.8f, 0.25f, 1.2f);
                }
                if (l.Moved && l.Fetch > 0 && age < l.Fetch)
                    grip = ConstructLifters.HandPose(ld, l, age) + Vector3.UnitY * ConstructLifters.GripLift; // (the empty hand on its way to the piece, which still lies where it was)
                if (ld.Bridge)
                {
                    DrawCrane(gfx, ld, part, grip);
                    continue;
                }
                ArmIk.Solve(ld, grip, out var yaw, out var elbow, out var wrist);
                if (ld.Kit)
                {
                    if (ld.Spec is { Valid: true } spec)
                    {
                        if (!l.FoldServer) // (a client steps the fold by the server's rule: out while it may work, in when idle without power)
                            l.Fold = l.Why == LiftWhy.NoPower && l.State == LiftState.Idle ? MathF.Max(0, l.Fold - _dt / ArmKit.FoldSeconds) : MathF.Min(1, l.Fold + _dt / ArmKit.FoldSeconds);
                        ArmKitRender.Draw(gfx, c, ld, spec, part, yaw, elbow, wrist, l.Fold);
                    }
                    continue;
                }
                DrawArm(gfx, ld, part, yaw, elbow, wrist, span);
                if (ld.Fitter)
                    DrawToolHead(gfx, part, yaw, wrist, l.CarryBody != 0 && world.Get(l.CarryBody) is { } held ? MathF.Min(held.LocalMax.X - held.LocalMin.X, held.LocalMax.Z - held.LocalMin.Z) : 1.2f);
            }
        }
    }

    /// <summary>
    /// A Module Fitter's tool head (module-sockets.md 7.1): under the wrist, a turning carrier with the gripper's spreader (two bars
    /// sized to what it holds), a welding torch and a cutting head on its sides. Boxes only, in the part's frame.
    /// </summary>
    private static void DrawToolHead(Renderer gfx, Matrix4x4 part, float yaw, Vector3 wrist, float across)
    {
        var turn = Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitY, -yaw));
        var spread = Math.Clamp(across, 0.6f, 3.2f);
        var carrier = new Vector3(0.82f, 0.64f, 0.12f);
        var dark = new Vector3(0.16f, 0.17f, 0.18f);
        Box(gfx, part, turn, wrist + new Vector3(0, -0.18f, 0), new Vector3(0.42f, 0.22f, 0.42f), carrier); // (the carrier)
        Box(gfx, part, turn, wrist + new Vector3(0, -0.33f, 0), new Vector3(spread, 0.08f, 0.12f), dark); // (the spreader's beam)
        for (var k = -1; k <= 1; k += 2)
        {
            var end = Vector3.Transform(new Vector3(k * spread * 0.5f, 0, 0), turn);
            Box(gfx, part, turn, wrist + end + new Vector3(0, -0.45f, 0), new Vector3(0.08f, 0.2f, 0.16f), dark); // (a grip at each end, on the lugs)
        }
        var side = Vector3.Transform(new Vector3(0, 0, 0.26f), turn);
        Box(gfx, part, turn, wrist + side + new Vector3(0, -0.24f, 0), new Vector3(0.07f, 0.3f, 0.07f), new Vector3(0.55f, 0.58f, 0.62f)); // (the welding torch)
        Box(gfx, part, turn, wrist - side + new Vector3(0, -0.24f, 0), new Vector3(0.12f, 0.24f, 0.12f), new Vector3(0.9f, 0.32f, 0.1f)); // (the cutting head)
    }

    public static ushort CabBody; // (phase 8) the Lifter Cab this peer's player sits in (Program.SandboxLifter.cs), else 0
    public static int CabPart = -1;
    private static ushort _lookBody;
    private static int _lookPart = -1;
    private static double _lookAt = -10;
    private const int RingSegments = 40;

    /// <summary>A lifter looked at this frame: its reach is drawn round it (Program.Sandbox.cs calls this each frame it is aimed at).</summary>
    public static void Look(ushort body, int part) => (_lookBody, _lookPart, _lookAt) = (body, part, ConstructConveyor.Clock);

    /// <summary>
    /// The reach view (arms-and-cranes.md section 6): an arm's ring in amber at its plinth's floor and at the top of its reach, its
    /// two radii, and a post at each end of the ring's quarters; a Building Station building in reach shows its hold point green.
    /// </summary>
    private static void DrawReach(Renderer gfx, ConstructWorld world, Construct c, int part, LifterDef ld, Matrix4x4 frame, Vector3 shoulder)
    {
        var amber = new Vector3(1f, 0.72f, 0.15f);
        var floorY = ld.Base.Y - ld.Down;
        const float t = 0.04f;
        for (var ring = 0; ring < 2; ring++)
        {
            var r = ring == 0 ? ld.ReachMin : ld.ReachMax;
            for (var level = 0; level < 2; level++)
            {
                var y = level == 0 ? floorY : ld.Base.Y + ld.Up;
                var chord = 2 * r * MathF.Sin(MathF.PI / RingSegments) + t;
                for (var k = 0; k < RingSegments; k++)
                {
                    var a = (k + 0.5f) * MathF.Tau / RingSegments;
                    var at = new Vector3(ld.Base.X + r * MathF.Cos(a), y, ld.Base.Z + r * MathF.Sin(a));
                    var m = Matrix4x4.CreateScale(t, t, chord) * Matrix4x4.CreateRotationY(-a) * Matrix4x4.CreateTranslation(at) * frame;
                    gfx.Draw(m, amber, 0.8f);
                }
            }
            for (var q = 0; q < 4; q++)
            {
                var a = q * MathF.PI * 0.5f;
                var h = ld.Up + ld.Down;
                var at = new Vector3(ld.Base.X + r * MathF.Cos(a), floorY + h * 0.5f, ld.Base.Z + r * MathF.Sin(a));
                gfx.Draw(Matrix4x4.CreateScale(t, h, t) * Matrix4x4.CreateTranslation(at) * frame, amber, 0.8f);
            }
        }
        // The stations it serves: a green post over each building station's hold point it reaches, red when the hold is out of reach.
        for (var si = 0; si < world.StationOpen.Count; si++)
        {
            if (world.Get(world.StationOpen[si]) is not { } sc)
                continue;
            for (var st = 0; st < sc.PartCount; st++)
            {
                if (!sc.DefOf(st).IsBuildStation || sc.Parts[st].Station is not { State: StationState.Building })
                    continue;
                if (Vector3.Distance(ConstructLifters.HoldPoint(sc, st, shoulder), shoulder) > ld.ReachMax * 2 + 4)
                    continue;
                Freeform.PartWorld(c, part, out _, out var lr);
                var ok = ConstructLifters.HoldPick(sc, st, ld, shoulder, lr, ConstructLifters.SafeLift, ConstructLifters.SafeLift, out var hold); // (the nearest point along the hold's side it reaches)
                gfx.Draw(Matrix4x4.CreateScale(0.08f, 1.2f, 0.08f) * Matrix4x4.CreateTranslation(hold + Vector3.UnitY * 0.6f), ok ? new Vector3(0.3f, 1f, 0.45f) : new Vector3(1f, 0.25f, 0.2f), 0.8f);
            }
        }
    }

    /// <summary>The turning half of an arm, from the part's frame matrix and its pose (all in the part's frame), in its tier's look and size.</summary>
    private static void DrawArm(Renderer gfx, LifterDef ld, Matrix4x4 part, float yaw, Vector3 elbow, Vector3 wrist, float span)
    {
        var s = ld.Base;
        var k = ld.Upper / 2.3f; // (the tier's size: the drawn half grows with its links)
        var heading = new Vector3(MathF.Cos(yaw), 0, MathF.Sin(yaw));
        var side = new Vector3(-heading.Z, 0, heading.X);
        var turret = Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitY, -yaw)); // (+x towards the hand)
        // The turret: a squat housing on the slewing ring (sunk 4 mm into it), a counterweight behind, the shoulder's yoke on top.
        // (a big arm's housing and weight keep to its pedestal, "housing": its links outgrow it, LifterParts.BigArms)
        var floor = ld.Ring - 0.004f;
        var h = s.Y - 0.12f * k - floor;
        var kh = ld.Housing > 0 ? MathF.Min(k, ld.Housing) : k;
        Box(gfx, part, turret, new Vector3(s.X, floor + h * 0.5f, s.Z), new Vector3(0.72f * kh, h, MathF.Max(0.66f * kh, 0.54f * k)), ld.Turret); // (never narrower than the shoulder's yoke cheeks on it)
        var weight = s - heading * 0.5f * kh + new Vector3(0, floor + 0.3f * kh - s.Y, 0);
        Box(gfx, part, turret, weight, new Vector3(0.36f, 0.42f, 0.58f) * kh, ld.Weight);
        Box(gfx, part, turret, weight + side * 0.292f * kh, new Vector3(0.3f, 0.08f, 0.012f) * kh, Accent); // (hazard on the weight's flank)
        Box(gfx, part, turret, s + side * 0.22f * k, new Vector3(0.3f, 0.34f, 0.1f) * k, ld.Joint);
        Box(gfx, part, turret, s - side * 0.22f * k, new Vector3(0.3f, 0.34f, 0.1f) * k, ld.Joint);
        Box(gfx, part, turret, s, new Vector3(0.24f, 0.24f, 0.56f) * k, ld.Pin); // (the shoulder's pin: what moves)
        // The upper arm, the elbow, the forearm.
        Beam(gfx, part, s, elbow, 0.2f * k, ld.UpperCol);
        if (ld.Tier >= 3)
            Beam(gfx, part, s + (elbow - s) * 0.15f + Vector3.UnitY * 0.16f * k, s + (elbow - s) * 0.7f + Vector3.UnitY * 0.16f * k, 0.07f * k, ld.Joint); // (a hydraulic ram along the upper arm)
        Box(gfx, part, turret, elbow, new Vector3(0.28f, 0.28f, 0.34f) * k, ld.Joint);
        Box(gfx, part, turret, elbow, new Vector3(0.14f, 0.14f, 0.42f) * k, ld.Pin); // (the elbow's pin)
        Beam(gfx, part, elbow, wrist, 0.15f * k, ld.ForeCol);
        // The wrist: a knuckle, a rod straight down to the gripper bar, the pads either side of the piece.
        Box(gfx, part, turret, wrist, new Vector3(0.18f, 0.18f, 0.22f) * k, ld.Joint);
        var bar = wrist - Vector3.UnitY * ConstructLifters.Wrist;
        Beam(gfx, part, wrist, bar + Vector3.UnitY * 0.04f, 0.07f * MathF.Min(k, 1.5f), ld.Joint);
        Box(gfx, part, turret, bar + Vector3.UnitY * 0.04f, new Vector3(0.14f, 0.08f, span + 0.12f), Out);
        for (var e = -1; e <= 1; e += 2)
            Box(gfx, part, turret, bar + side * (e * (span * 0.5f + 0.03f)) - Vector3.UnitY * 0.1f, new Vector3(0.1f, 0.24f, 0.04f), Out);
    }

    private static readonly Vector3 CraneYellow = new(0.94f, 0.76f, 0.1f), CraneDark = new(0.133f, 0.149f, 0.169f), CraneSteel = new(0.6f, 0.63f, 0.66f);

    /// <summary>
    /// A Bridge Crane's moving half (arms-and-cranes.md 3.4, section 6), in the part's frame from the hook's place: an end truck on each
    /// rail, a double girder across the span over them, the trolley on the girders over the hook, the cable down between the girders
    /// and the hook block with its orange hook on the piece's top. The rails are the parts. Its size follows the hook's depth (the class).
    /// </summary>
    private static void DrawCrane(Renderer gfx, LifterDef ld, Matrix4x4 part, Vector3 grip)
    {
        var k = ld.Hall ? 2f : MathF.Max(0.25f, ld.Depth / 0.6f); // (Small 0.25, Medium 1, Large 4, Capital 16; a Hall Crane 2: its 2 m rail blocks)
        var b = ld.Base;
        var hz = grip.Z;
        var id = Matrix4x4.Identity;
        // The end trucks, sat 5 mm over the rails' top; the girders on them; the trolley on the girders.
        for (var e = 0; e < 2; e++)
        {
            var x = b.X + e * ld.Span;
            Box(gfx, part, id, new Vector3(x, b.Y + 0.155f * k, hz), new Vector3(0.6f, 0.3f, 1.8f) * k, CraneDark);
            Box(gfx, part, id, new Vector3(x + (e == 0 ? -0.305f : 0.305f) * k, b.Y + 0.155f * k, hz), new Vector3(0.012f, 0.1f, 1.2f) * k, MachineAccent); // (hazard on its outer face)
        }
        var gl = ld.Span + 0.6f * k;
        for (var s = -1; s <= 1; s += 2)
            Box(gfx, part, id, new Vector3(b.X + ld.Span * 0.5f, b.Y + 0.545f * k, hz + s * 0.4f * k), new Vector3(gl, 0.5f * k, 0.26f * k), CraneYellow);
        if (ld.Hall)
            DrawHallCab(gfx, ld, part, k, hz);
        var hx = grip.X;
        Box(gfx, part, id, new Vector3(hx, b.Y + 0.999f * k, hz), new Vector3(1.0f, 0.4f, 1.24f) * k, CraneDark);
        Box(gfx, part, id, new Vector3(hx + 0.3f * k, b.Y + 1.25f * k, hz), new Vector3(0.36f, 0.12f, 0.5f) * k, CraneSteel); // (its hoist drum's housing)
        // The cable: two falls between the girders down to the hook block.
        var block = grip + Vector3.UnitY * (0.22f * k);
        for (var s = -1; s <= 1; s += 2)
            Beam(gfx, part, new Vector3(hx, b.Y + 0.8f * k, hz + s * 0.08f * k), block + new Vector3(0, 0.1f * k, s * 0.08f * k), 0.035f * k, CraneDark);
        Box(gfx, part, id, block, new Vector3(0.3f, 0.36f, 0.34f) * k, Out);
        Box(gfx, part, id, block + new Vector3(0.152f * k, 0, 0), new Vector3(0.004f, 0.3f, 0.26f) * k, CraneDark); // (a dark band on the block)
        if (ld.Hall)
            for (var st = -1; st <= 1; st++) // (a Hall Crane's hook block: hazard stripes on both faces, drawing 24's warning)
                for (var f = -1; f <= 1; f += 2)
                    Box(gfx, part, id, block + new Vector3(st * 0.09f * k, 0, f * 0.171f * k), new Vector3(0.04f * k, 0.36f * k, 0.004f * k), CraneDark);
        Beam(gfx, part, block - Vector3.UnitY * 0.18f * k, grip + Vector3.UnitY * 0.01f, 0.06f * k, CraneSteel); // (the hook's shank to the piece)
    }

    private static readonly Vector3 MachineAccent = new(0.95f, 0.69f, 0.12f);

    private static readonly Vector3 CabGlass = new(0.12f, 0.2f, 0.26f), SignPale = new(0.93f, 0.92f, 0.89f);

    /// <summary>
    /// A Hall Crane's cab (drawing 24), hung under the bridge's girders 2 m in from the near rail, travelling with the bridge: a yellow
    /// box on two hangers, its windows dark glass all round, a KEEP CLEAR plate (pale, dark lettering bars) on each long side, and the
    /// festoon cable's loops along the near girder.
    /// </summary>
    private static void DrawHallCab(Renderer gfx, LifterDef ld, Matrix4x4 part, float k, float hz)
    {
        var id = Matrix4x4.Identity;
        var b = ld.Base;
        var at = new Vector3(b.X + 1.1f * k, b.Y - 0.42f * k, hz + 0.45f * k);
        for (var s = -1; s <= 1; s += 2)
            Box(gfx, part, id, at + new Vector3(s * 0.25f * k, 0.5f * k, 0), new Vector3(0.06f, 0.4f, 0.06f) * k, CraneDark); // (its hangers)
        Box(gfx, part, id, at, new Vector3(0.9f, 0.62f, 0.7f) * k, CraneYellow);
        Box(gfx, part, id, at + new Vector3(0, 0.08f * k, 0), new Vector3(0.905f, 0.22f, 0.705f) * k, CabGlass); // (its window band)
        Box(gfx, part, id, at - new Vector3(0, 0.34f * k, 0), new Vector3(0.94f, 0.06f, 0.74f) * k, CraneDark); // (its floor)
        for (var s = -1; s <= 1; s += 2)
        {
            var side = at + new Vector3(0, -0.2f * k, s * 0.353f * k);
            Box(gfx, part, id, side, new Vector3(0.6f, 0.12f, 0.004f) * k, SignPale); // (KEEP CLEAR)
            Box(gfx, part, id, side + new Vector3(0, 0.025f * k, s * 0.002f * k), new Vector3(0.46f, 0.025f, 0.004f) * k, CraneDark);
            Box(gfx, part, id, side + new Vector3(0, -0.025f * k, s * 0.002f * k), new Vector3(0.36f, 0.025f, 0.004f) * k, CraneDark);
        }
        // The festoon: cable loops hanging between trolleys along the near girder's outer side.
        var n = Math.Clamp((int)(ld.Span / (1.5f * k)), 2, 20);
        for (var i = 0; i < n; i++)
        {
            var x = b.X + (0.6f + i * 0.9f) * k;
            if (x > b.X + ld.Span - 0.6f * k)
                break;
            Box(gfx, part, id, new Vector3(x, b.Y + 0.28f * k, hz - 0.58f * k), new Vector3(0.08f, 0.08f, 0.08f) * k, CraneSteel);
            Box(gfx, part, id, new Vector3(x + 0.45f * k, b.Y + 0.14f * k, hz - 0.58f * k), new Vector3(0.75f, 0.03f, 0.03f) * k, CraneDark);
        }
    }

    /// <summary>A crane's reach view: its box's edges in amber, under the rails from the hook's highest point to its drop.</summary>
    private static void DrawCraneReach(Renderer gfx, LifterDef ld, Matrix4x4 frame)
    {
        if (ld.RailWhy != 0)
            return;
        var amber = new Vector3(1f, 0.72f, 0.15f);
        const float t = 0.05f;
        var b = ld.Base;
        float x0 = b.X + ld.AcrossMin, x1 = b.X + ld.AcrossMax, y0 = b.Y - ld.Down, y1 = b.Y - ld.Depth, z0 = b.Z + ld.RunLo, z1 = b.Z + ld.RunHi;
        for (var i = 0; i < 4; i++)
        {
            float y = (i & 1) == 0 ? y0 : y1, z = (i & 2) == 0 ? z0 : z1, x = (i & 2) == 0 ? x0 : x1;
            gfx.Draw(Matrix4x4.CreateScale(x1 - x0, t, t) * Matrix4x4.CreateTranslation((x0 + x1) * 0.5f, y, z) * frame, amber, 0.8f);
            gfx.Draw(Matrix4x4.CreateScale(t, t, z1 - z0) * Matrix4x4.CreateTranslation(x, y, (z0 + z1) * 0.5f) * frame, amber, 0.8f);
            gfx.Draw(Matrix4x4.CreateScale(t, y1 - y0, t) * Matrix4x4.CreateTranslation((i & 1) == 0 ? x0 : x1, (y0 + y1) * 0.5f, z) * frame, amber, 0.8f);
        }
    }

    private static void Box(Renderer gfx, Matrix4x4 part, Matrix4x4 turn, Vector3 at, Vector3 size, Vector3 colour) =>
        gfx.Draw(Matrix4x4.CreateScale(size) * turn * Matrix4x4.CreateTranslation(at) * part, colour);

    /// <summary>A square beam from <paramref name="a"/> to <paramref name="b"/>, in the part's frame.</summary>
    private static void Beam(Renderer gfx, Matrix4x4 part, Vector3 a, Vector3 b, float thick, Vector3 colour)
    {
        var d = b - a;
        var len = d.Length();
        if (len < 1e-3f)
            return;
        var z = d / len;
        var x = Vector3.Normalize(Vector3.Cross(MathF.Abs(z.Y) > 0.98f ? Vector3.UnitX : Vector3.UnitY, z));
        var y = Vector3.Cross(z, x);
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        gfx.Draw(Matrix4x4.CreateScale(thick, thick, len) * basis * Matrix4x4.CreateTranslation((a + b) * 0.5f) * part, colour);
    }
}
