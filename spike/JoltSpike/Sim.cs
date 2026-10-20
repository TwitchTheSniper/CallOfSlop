// The game project (CallOfSlop.csproj) compiles every .cs under the repo, so the spike's code only exists
// when JOLT_SPIKE is defined (JoltSpike.csproj defines it).
#if JOLT_SPIKE
using Brutal.JoltApi;
using Brutal.Numerics;

namespace JoltSpike;

[Flags]
public enum Btn : byte
{
    None = 0,
    Forward = 1,
    Back = 2,
    Left = 4,
    Right = 8,
    Jump = 16,
    Sprint = 32,
    Crouch = 64,
    Prone = 128,
}

public enum Stance : byte
{
    Stand,
    Crouch,
    Prone,
}

/// <summary>One tick of input, like the game's InputCmd.</summary>
public struct Input
{
    public Btn Buttons;
    public float Yaw; // radians; forward = (sin yaw, 0, -cos yaw) as in the game
}

/// <summary>The minimal state a snapshot carries (what the game's MoveState has).</summary>
public struct NetState
{
    public float3 Position;
    public float3 Velocity;
    public Stance Stance;
}

/// <summary>The game's movement constants (Source/Game/World.cs).</summary>
public static class Sim
{
    public const int TickRate = 60;
    public const float Dt = 1f / TickRate;
    public const float WalkSpeed = 6f, SprintSpeed = 9f, JumpSpeed = 6f, Gravity = 18f;
    public const float StepHeight = 0.55f, MantleReach = 1.7f, PlayerRadius = 0.4f;
    public const float MaxSlopeDeg = 45f;

    public static float HeightOf(Stance s) => s switch { Stance.Crouch => 1.2f, Stance.Prone => 0.6f, _ => 1.8f };
    public static float SpeedOf(Stance s) => s switch { Stance.Crouch => 0.5f, Stance.Prone => 0.22f, _ => 1f };

    public static float3 FlatForward(float yaw) => new(DMath.Sin(yaw), 0, -DMath.Cos(yaw));
    public static float3 FlatRight(float yaw) => new(DMath.Cos(yaw), 0, DMath.Sin(yaw));
}

/// <summary>
/// Shared shapes for the three stances. Feet are at the character's origin, so each shape is offset up.
/// Stand/crouch are capsules (radius 0.4); prone is a 0.6 m cylinder of radius 0.4 (a lying capsule would
/// have to rotate with the view and snag on walls when turning; the long prone body stays a hitbox only).
/// </summary>
public static class StanceShapes
{
    static Shape[]? _shapes;

    public static Shape Get(Stance s)
    {
        if (_shapes == null)
        {
            Phys.InitJolt();
            _shapes = new Shape[3];
            for (var i = 0; i < 3; i++)
            {
                var st = (Stance)i;
                var h = Sim.HeightOf(st);
                Shape inner = st == Stance.Prone
                    ? Phys.Cylinder(h / 2, Sim.PlayerRadius, 0.05f)
                    : Phys.Capsule(h / 2 - Sim.PlayerRadius, Sim.PlayerRadius);
                _shapes[i] = Phys.Keep(Phys.Offset(new float3(0, h / 2, 0), floatQuat.Identity, Phys.Keep(inner)));
            }
        }
        return _shapes[(int)s];
    }
}

/// <summary>
/// A player body driven by Jolt's CharacterVirtual, with the game's movement rules on top
/// (acceleration blend, jump, stance changes with headroom checks).
/// </summary>
public sealed class Mover
{
    public readonly CharacterVirtual Ch;
    public Stance Stance;
    readonly Phys _p;

    public static CharacterVirtual.ExtendedUpdateSettings Ext = new()
    {
        StickToFloorStepDown = new float3(0, -0.5f, 0),
        // Measured (step sweep): with a 0.3 m minimum forward step, step-up 0.5 climbs 0.55 and 0.60 steps and
        // blocks 0.70 (the capsule's round bottom rolls over the last few cm). Jolt's default 0.02 m forward step
        // lands the round bottom on the step's edge, which counts as too steep, so WalkStairs fails above 0.4 m.
        WalkStairsStepUp = new float3(0, 0.5f, 0),
        WalkStairsMinStepForward = 0.3f,
        WalkStairsStepForwardTest = 0.15f,
        WalkStairsCosAngleForwardContact = 0.258819f, // cos(75 deg)
        WalkStairsStepDownExtra = float3.Zero,
    };

    public Mover(Phys p, float3 feet)
    {
        _p = p;
        var s = CharacterVirtualSettings.Create();
        s.SetShape(StanceShapes.Get(Stance.Stand));
        Phys.Try(() => s.SetUp(new float3(0, 1, 0)), "CharacterVirtualSettings.SetUp");
        Phys.Try(() => s.SetMaxSlopeAngle(Sim.MaxSlopeDeg * MathF.PI / 180f), "CharacterVirtualSettings.SetMaxSlopeAngle");
        // Only the bottom hemisphere supports the player (so a wall touching the capsule's side isn't "ground").
        Phys.Try(() => s.SetSupportingVolume(Plane.Create(new float3(0, 1, 0), -Sim.PlayerRadius)), "CharacterVirtualSettings.SetSupportingVolume");
        Phys.Try(() => s.SetMass(80), "CharacterVirtualSettings.SetMass");
        Phys.Try(() => s.SetMaxStrength(100), "CharacterVirtualSettings.SetMaxStrength");
        Phys.Try(() => s.SetCharacterPadding(0.02f), "CharacterVirtualSettings.SetCharacterPadding");
        Phys.Try(() => s.SetPenetrationRecoverySpeed(1f), "CharacterVirtualSettings.SetPenetrationRecoverySpeed");
        Phys.Try(() => s.SetPredictiveContactDistance(0.1f), "CharacterVirtualSettings.SetPredictiveContactDistance");
        Phys.Try(() => s.SetBackFaceMode(BackFaceMode.CollideWithBackFaces), "CharacterVirtualSettings.SetBackFaceMode");
        Phys.Try(() => s.SetEnhancedInternalEdgeRemoval(true), "CharacterVirtualSettings.SetEnhancedInternalEdgeRemoval");
        Ch = CharacterVirtual.Create(s, feet, floatQuat.Identity, 0, p.System);
    }

    public bool Grounded => Ch.GetGroundState() == CharacterBase.GroundState.OnGround;

    /// <summary>Tries to change stance. Going up needs room: Jolt refuses the shape swap if it would overlap.</summary>
    public bool TrySetStance(Stance want)
    {
        if (want == Stance)
            return true;
        var lower = Sim.HeightOf(want) <= Sim.HeightOf(Stance);
        // maxPenetrationDepth 0 = refuse any overlap; when crouching down anything goes.
        var ok = Ch.SetShape(StanceShapes.Get(want), lower ? float.MaxValue : 0f, _p.WorldBp, _p.WorldObj, _p.AnyBody, _p.AnyShape, _p.Temp);
        if (ok)
            Stance = want;
        return ok;
    }

    /// <summary>One tick: the game's Simulate() rules, with Jolt doing the collision.</summary>
    public void Step(in Input cmd)
    {
        var b = cmd.Buttons;
        var want = (b & Btn.Prone) != 0 ? Stance.Prone : (b & Btn.Crouch) != 0 ? Stance.Crouch : Stance.Stand;
        if (want == Stance.Prone && !Grounded)
            want = Stance.Crouch;
        if (want != Stance && !TrySetStance(want) && want == Stance.Stand && Stance == Stance.Prone)
            TrySetStance(Stance.Crouch);

        var wish = float3.Zero;
        if ((b & Btn.Forward) != 0) wish += Sim.FlatForward(cmd.Yaw);
        if ((b & Btn.Back) != 0) wish -= Sim.FlatForward(cmd.Yaw);
        if ((b & Btn.Right) != 0) wish += Sim.FlatRight(cmd.Yaw);
        if ((b & Btn.Left) != 0) wish -= Sim.FlatRight(cmd.Yaw);
        var len2 = float3.Dot(wish, wish);
        if (len2 > 0)
            wish /= MathF.Sqrt(len2);

        var sprint = (b & Btn.Sprint) != 0 && Stance == Stance.Stand;
        var speed = (sprint ? Sim.SprintSpeed : Sim.WalkSpeed) * Sim.SpeedOf(Stance);

        Ch.UpdateGroundVelocity();
        var grounded = Grounded;
        var ground = grounded ? Ch.GetGroundVelocity() : float3.Zero;
        var v = Ch.GetLinearVelocity();
        // Horizontal velocity relative to whatever we stand on, blended toward the wish (same constants as the game).
        var blend = grounded ? 0.20839f : 0.04877f; // 1 - exp(-14/60), 1 - exp(-3/60), precomputed (no CRT exp)
        var rx = v.X - ground.X;
        var rz = v.Z - ground.Z;
        // Note: no CancelVelocityTowardsSteepSlopes here. ExtendedUpdate does it itself, and cancelling first
        // removes the push into a step face that WalkStairs needs (steps above the capsule radius stop working).
        var desired = wish * speed;
        rx += (desired.X - rx) * blend;
        rz += (desired.Z - rz) * blend;

        // On walkable ground: ride the ground (no gravity, so we don't creep down slopes; ExtendedUpdate's
        // stick-to-floor keeps us glued going downhill). Jumping and falling integrate gravity like the game does.
        float vy;
        if (grounded)
            vy = ground.Y + ((b & Btn.Jump) != 0 && Stance == Stance.Stand ? Sim.JumpSpeed - Sim.Gravity * Sim.Dt : 0f);
        else
            vy = v.Y - Sim.Gravity * Sim.Dt;

        var vel = new float3(ground.X + rx, vy, ground.Z + rz);
        Ch.SetLinearVelocity(vel);
        if (ManualExtended)
            ManualExtendedUpdate(vel);
        else
            Ch.ExtendedUpdate(Sim.Dt, new float3(0, -Sim.Gravity, 0), in Ext, _p.WorldBp, _p.WorldObj, _p.AnyBody, _p.AnyShape, _p.Temp);
    }

    /// <summary>
    /// true: our C# copy of Jolt's ExtendedUpdate (Update + StickToFloor + WalkStairs, all exported natively).
    /// The native ExtendedUpdate in 2026.10.2 seems to ignore the settings struct (it climbs exactly Jolt's
    /// default 0.4 m whatever WalkStairsStepUp says), so the spike measures both.
    /// </summary>
    public static bool ManualExtended = true;

    void ManualExtendedUpdate(float3 desired)
    {
        var up = new float3(0, 1, 0);
        var oldPos = Ch.GetPosition();
        var wasSupported = Ch.IsSupported();
        Ch.Update(Sim.Dt, new float3(0, -Sim.Gravity, 0), _p.WorldBp, _p.WorldObj, _p.AnyBody, _p.AnyShape, _p.Temp);

        // Walking off a ledge or down a slope: snap down instead of flying off (only if we weren't moving up).
        if (wasSupported && !Ch.IsSupported())
        {
            var vUp = (Ch.GetPosition().Y - oldPos.Y) / Sim.Dt;
            if (vUp <= 1e-6f)
                Ch.StickToFloor(Ext.StickToFloorStepDown, _p.WorldBp, _p.WorldObj, _p.AnyBody, _p.AnyShape, _p.Temp);
        }

        // Blocked by something steep while trying to move: try stepping up onto it.
        if (Ch.CanWalkStairs(desired))
        {
            var step = new float3(desired.X, 0, desired.Z) * Sim.Dt;
            var stepLen = step.Length();
            if (stepLen > 0)
            {
                var dir = step / stepLen;
                var moved = Ch.GetPosition() - oldPos;
                var achieved = MathF.Max(0, float3.Dot(new float3(moved.X, 0, moved.Z), dir));
                if (achieved + 1e-4f < stepLen)
                {
                    StairCalls++;
                    bool ok;
                    if (OwnStep)
                        ok = OwnStepUp(dir, MathF.Max(OwnStepForward, stepLen - achieved));
                    else
                    {
                        var forward = dir * MathF.Max(Ext.WalkStairsMinStepForward, stepLen - achieved);
                        var forwardTest = dir * Ext.WalkStairsStepForwardTest;
                        ok = Ch.WalkStairs(Sim.Dt, Ext.WalkStairsStepUp, forward, forwardTest, Ext.WalkStairsStepDownExtra,
                            _p.WorldBp, _p.WorldObj, _p.AnyBody, _p.AnyShape, _p.Temp);
                    }
                    if (ok)
                        StairOk++;
                }
            }
        }
        _ = up;
    }

    /// <summary>WalkStairs attempts and successes (for the step sweep's diagnostics).</summary>
    public static int StairCalls, StairOk;

    /// <summary>The last successful own step's numbers (diagnostics).</summary>
    public static string LastStep = "";

    /// <summary>
    /// false: Jolt's CharacterVirtual.WalkStairs. true: our own step-up built from Update + StickToFloor
    /// (sweep up, sweep forward, snap down; undo if nothing was gained), like a classic FPS step.
    /// </summary>
    public static bool OwnStep;

    /// <summary>Minimum forward move of the own step, so the capsule's round bottom lands on top of the step, not on its edge.</summary>
    public static float OwnStepForward = 0.25f;

    bool OwnStepUp(float3 dir, float forward)
    {
        var basePos = Ch.GetPosition();
        var baseVel = Ch.GetLinearVelocity();
        var noGravity = float3.Zero;
        // 1. Sweep up by the step height (a low ceiling stops it).
        Ch.SetLinearVelocity(new float3(0, Ext.WalkStairsStepUp.Y / Sim.Dt, 0));
        Ch.Update(Sim.Dt, noGravity, _p.WorldBp, _p.WorldObj, _p.AnyBody, _p.AnyShape, _p.Temp);
        var rise = Ch.GetPosition().Y - basePos.Y;
        if (rise > 0.05f)
        {
            // 2. Sweep forward at the raised height.
            Ch.SetLinearVelocity(dir * (forward / Sim.Dt));
            Ch.Update(Sim.Dt, noGravity, _p.WorldBp, _p.WorldObj, _p.AnyBody, _p.AnyShape, _p.Temp);
            var moved = Ch.GetPosition() - basePos;
            // Sliding up over an edge during the forward sweep means the obstacle is taller than the step.
            if (float3.Dot(new float3(moved.X, 0, moved.Z), dir) > 0.05f && moved.Y <= rise + 0.01f)
            {
                // 3. Snap down onto whatever is below; keep the result only if we really ended up higher, on something.
                var landed = Ch.StickToFloor(new float3(0, -(rise + 0.05f), 0), _p.WorldBp, _p.WorldObj, _p.AnyBody, _p.AnyShape, _p.Temp);
                var gain = Ch.GetPosition().Y - basePos.Y;
                // Landing on steep ground (a 50 deg ramp) doesn't count, or players could "step" up any slope.
                if (landed && gain > 0.02f && gain <= Ext.WalkStairsStepUp.Y && Ch.GetGroundState() == CharacterBase.GroundState.OnGround)
                {
                    LastStep = $"rise {Report.F(rise, 2)} fwdY {Report.F(moved.Y, 2)} gain {Report.F(gain, 2)}";
                    Ch.SetLinearVelocity(new float3(baseVel.X, 0, baseVel.Z));
                    return true;
                }
            }
        }
        Ch.SetPosition(basePos);
        Ch.SetLinearVelocity(baseVel);
        Ch.RefreshContacts(_p.WorldBp, _p.WorldObj, _p.AnyBody, _p.AnyShape, _p.Temp);
        return false;
    }

    public NetState Net => new() { Position = Ch.GetPosition(), Velocity = Ch.GetLinearVelocity(), Stance = Stance };

    public void HashInto(ref Hasher h)
    {
        h.Add(Ch.GetPosition());
        h.Add(Ch.GetLinearVelocity());
        h.Add((uint)Ch.GetGroundState() | ((uint)Stance << 8));
    }

    /// <summary>Reconciliation from a snapshot: position, velocity and stance only, then let Jolt re-find contacts.</summary>
    public void RestoreNet(in NetState s)
    {
        if (s.Stance != Stance)
        {
            Ch.SetShape(StanceShapes.Get(s.Stance), float.MaxValue, _p.WorldBp, _p.WorldObj, _p.AnyBody, _p.AnyShape, _p.Temp);
            Stance = s.Stance;
        }
        Ch.SetPosition(s.Position);
        Ch.SetLinearVelocity(s.Velocity);
        Ch.RefreshContacts(_p.WorldBp, _p.WorldObj, _p.AnyBody, _p.AnyShape, _p.Temp);
    }

    /// <summary>Full Jolt character state (contacts, ground info) as bytes.</summary>
    public static bool FullStateAvailable = true;

    public byte[]? SaveFull(StateRecorderImpl rec)
    {
        if (!FullStateAvailable)
            return null;
        rec.Clear();
        try { Ch.SaveState(rec); }
        catch (EntryPointNotFoundException)
        {
            FullStateAvailable = false;
            Phys.Try(() => throw new EntryPointNotFoundException(), "CharacterVirtual.SaveState/RestoreState");
            return null;
        }
        var bytes = new byte[(int)rec.GetDataSize() + 1];
        bytes[0] = (byte)Stance;
        rec.GetData(bytes.AsSpan(1));
        return bytes;
    }

    public void RestoreFull(StateRecorderImpl rec, byte[] bytes)
    {
        var st = (Stance)bytes[0];
        if (st != Stance)
        {
            Ch.SetShape(StanceShapes.Get(st), float.MaxValue, _p.WorldBp, _p.WorldObj, _p.AnyBody, _p.AnyShape, _p.Temp);
            Stance = st;
        }
        rec.Clear();
        rec.WriteBytes(bytes.AsSpan(1));
        rec.Rewind();
        Ch.RestoreState(rec);
    }

    /// <summary>
    /// Mantle probe: a sphere cast straight down in front of the player finds the ledge top, then a shape
    /// overlap test checks the player fits on it. Returns the ledge top height above the feet, or NaN.
    /// </summary>
    public float ProbeLedge(float yaw, out bool fits)
    {
        fits = false;
        if (!LedgeProbeAvailable)
            return float.NaN;
        try { return ProbeLedgeCore(yaw, out fits); }
        catch (EntryPointNotFoundException e)
        {
            LedgeProbeAvailable = false;
            Phys.Try(() => throw e, "NarrowPhaseQuery.CastShapeClosest / CharacterVirtual.CheckCollisionClosest (mantle probe)");
            return float.NaN;
        }
    }

    public static bool LedgeProbeAvailable = true;

    float ProbeLedgeCore(float yaw, out bool fits)
    {
        fits = false;
        var feet = Ch.GetPosition();
        var f = Sim.FlatForward(yaw);
        Shape probe = Probe;
        ReadOnlySpan<float> reaches = [0.55f, 0.8f, 1.05f];
        ReadOnlySpan<Stance> fitStances = [Stance.Stand, Stance.Crouch];
        foreach (var reach in reaches)
        {
            var start = feet + f * (Sim.PlayerRadius + reach) + new float3(0, Sim.MantleReach + 0.3f, 0);
            var down = Sim.MantleReach + 0.3f - Sim.StepHeight;
            var com = float4x4.CreateTranslation(start);
            var cast = new RShapeCast
            {
                Shape = probe,
                Scale = new float3(1, 1, 1),
                CenterOfMassStart = com,
                Direction = new float3(0, -down, 0),
                ShapeWorldBounds = probe.GetWorldSpaceBounds(in com, new float3(1, 1, 1)),
            };
            if (!_p.Query.CastShapeClosest(in cast, CastSettings, float3.Zero, out var hit, _p.WorldBp, _p.WorldObj, _p.AnyBody, _p.AnyShape))
                continue;
            if (hit.Fraction <= 0)
                continue; // started inside something: a wall taller than our reach
            var top = hit.ContactPointOn2.Y;
            var rel = top - feet.Y;
            if (rel <= Sim.StepHeight || rel > Sim.MantleReach)
                continue;
            var target = new float3(start.X, top + 0.02f, start.Z);
            // Fits standing (or crouched) at the target?
            foreach (var st in fitStances)
            {
                var blocked = Ch.CheckCollisionClosest(target, floatQuat.Identity, float3.Zero, 0f, StanceShapes.Get(st), float3.Zero, out _, _p.WorldBp, _p.WorldObj, _p.AnyBody, _p.AnyShape);
                if (!blocked)
                {
                    fits = true;
                    return rel;
                }
            }
            return rel;
        }
        return float.NaN;
    }

    static Shape? _probe;
    static Shape Probe => _probe ??= Phys.Keep(Phys.Sphere(0.15f));
    static ShapeCastSettings? _castSettings;
    static ShapeCastSettings CastSettings => _castSettings ??= ShapeCastSettings.Create();
}
#endif
