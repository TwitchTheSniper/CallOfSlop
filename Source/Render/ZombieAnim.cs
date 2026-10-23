using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>What a zombie's body is doing this frame (picked from its <see cref="ZState"/> by the drawing code).</summary>
public enum ZAnimMode : byte
{
    /// <summary>Walking, jogging or sprinting (or standing), by <see cref="ZombieAnimInput.Speed"/> and how fast it moves.</summary>
    Locomotion,

    /// <summary>At a window, tearing the top remaining board off (<see cref="ZombieAnimInput.Phase"/>: one board's cycle, 0-1).</summary>
    Tearing,

    /// <summary>At a window with no boards left (waiting for its turn to climb, or swiping through it): clawing at the opening.</summary>
    WindowIdle,

    /// <summary>Climbing in through the window (<see cref="ZombieAnimInput.Phase"/>: the climb's progress, 0-1).</summary>
    Vaulting,

    /// <summary>Climbing out of the ground at a ground spawner (<see cref="ZombieAnimInput.Phase"/>: 0-1).</summary>
    Rising,

    /// <summary>Dead without a ragdoll: tips over backwards (<see cref="ZombieAnimInput.Phase"/>: seconds since it died).</summary>
    Dead,
}

/// <summary>
/// A zombie's own look, seeded by its id (every PC picks the same): how it holds its arms, which leg limps and how badly,
/// how its head hangs, how far it leans, and which arm swipes.
/// </summary>
public readonly struct ZombieLook
{
    /// <summary>0: both arms reaching forward (the classic), 1: one forward and one hanging, 2: both hanging and swaying.</summary>
    public readonly byte ArmStyle;

    /// <summary>The arm that reaches when <see cref="ArmStyle"/> is 1, and the arm that swipes (+1 right, -1 left).</summary>
    public readonly sbyte ReachSide, SwipeSide;

    /// <summary>The limping leg (+1 right, -1 left) and how badly (0 none, up to 0.75).</summary>
    public readonly sbyte LimpSide;

    public readonly float Limp;

    /// <summary>Head hanging to one side (radians, +- 0.35) and forward (0 - 0.3).</summary>
    public readonly float HeadRoll, HeadNod;

    /// <summary>Extra forward lean (radians, 0 - 0.18) and arm height (metres, +- 0.08).</summary>
    public readonly float Lean, ArmHeight;

    public ZombieLook(ushort id)
    {
        var h = (uint)id * 2654435761u;
        h ^= h >> 15;
        h *= 0x2c1b3c6du;
        h ^= h >> 12;
        h *= 0x297a2d39u;
        h ^= h >> 15;
        ArmStyle = (byte)(h % 3);
        ReachSide = (sbyte)((h & 8) != 0 ? 1 : -1);
        SwipeSide = (sbyte)((h & 16) != 0 ? 1 : -1);
        LimpSide = (sbyte)((h & 32) != 0 ? 1 : -1);
        var limpRoll = (h >> 6) & 255;
        Limp = limpRoll < 110 ? 0f : 0.2f + 0.55f * ((limpRoll - 110) / 145f); // a bit over half of them limp
        HeadRoll = (((h >> 14) & 255) / 127.5f - 1f) * 0.35f;
        HeadNod = ((h >> 22) & 63) / 63f * 0.3f;
        Lean = ((h >> 28) & 15) / 15f * 0.18f;
        ArmHeight = (((h >> 3) & 31) / 15.5f - 1f) * 0.08f;
    }
}

/// <summary>Everything a zombie's pose is worked out from (filled by the drawing code from the zombie, the map and the rules).</summary>
public struct ZombieAnimInput
{
    /// <summary>Feet position (world) and facing (radians; at a window: the window's yaw, facing in).</summary>
    public Vector3 Pos;

    public float Yaw;
    public ZAnimMode Mode;
    public ZSpeed Speed;
    public ushort Id;

    /// <summary>Walk cycle phase (radians) and how much it is moving (0 standing .. 1 at its speed).</summary>
    public float Gait, Move;

    /// <summary>Tearing: the board cycle 0-1 (the board comes off at 1); vaulting / rising: progress 0-1; dead: seconds.</summary>
    public float Phase;

    /// <summary>Seconds in the current state, and a free-running clock (idle sway, clawing).</summary>
    public float SinceState, Time;

    /// <summary>Seconds since its swipe started (negative: none), and the windup before the swipe lands.</summary>
    public float Attack, Windup;

    /// <summary>Flinch strength now (0-1, from being shot), and seconds since it landed from a vault (negative: none).</summary>
    public float Flinch, Stagger;

    /// <summary>Seconds per board (for the yank that matches the flying board).</summary>
    public float TearSeconds;

    // ---- The window (tearing, clawing, vaulting) ----

    /// <summary>The opening's centre (world), the horizontal unit vector pointing in, the one along the opening, its half width, the sill height and the opening's height.</summary>
    public Vector3 WinCenter, WinIn, WinSide;

    public float WinHalfWidth, SillY, WinHeight;

    /// <summary>Boards still on the window, and how many it has when full.</summary>
    public int Boards, MaxBoards;

    /// <summary>The window fields from <paramref name="w"/> (and the boards on it).</summary>
    public void SetWindow(ZWindow w, int boards)
    {
        var o = w.Opening;
        var inward = w.Inside - w.Outside;
        inward.Y = 0;
        WinIn = inward.LengthSquared() > 1e-6f ? Vector3.Normalize(inward) : new Vector3(0, 0, -1);
        WinSide = new Vector3(-WinIn.Z, 0, WinIn.X);
        WinCenter = o.Center;
        WinHalfWidth = MathF.Abs(WinSide.X) > MathF.Abs(WinSide.Z) ? o.Half.X : o.Half.Z;
        SillY = o.Min.Y;
        WinHeight = o.Half.Y * 2;
        Boards = boards;
        MaxBoards = Math.Max(1, w.Boards);
    }
}

/// <summary>
/// Where a zombie's limbs want to be (its own frame: feet at the origin, -Z facing, +X right), before the two-bone IK.
/// The drawing code smooths these from frame to frame (<see cref="ZombieAnim.Smooth"/>), so a change of state blends.
/// </summary>
public struct ZombieTargets
{
    public Vector3 Pelvis;

    /// <summary>Torso lean forward, roll to the right, twist (radians), and the head's roll and nod.</summary>
    public float Pitch, Roll, Twist, HeadRoll, HeadNod;

    public Vector3 HandR, HandL, FootR, FootL;
}

/// <summary>A solved pose in world space: frames for the torso and head, a frame per bone (Y along the bone, origin at its middle), and the joints.</summary>
public struct ZombiePose
{
    public Matrix4x4 Root, Upper, Head;
    public Matrix4x4 ArmUpR, ArmUpL, ArmLoR, ArmLoL, LegUpR, LegUpL, LegLoR, LegLoL;
    public Vector3 ShoulderR, ShoulderL, ElbowR, ElbowL, HandR, HandL, HipR, HipL, KneeR, KneeL, FootR, FootL;
}

/// <summary>
/// Procedural zombie animation (docs/zombies/ZOMBIES-MODE.md, "Animation"): no skeletal assets, just the blocky body
/// driven by a handful of targets and an analytic two-bone IK per limb. Tearing boards (hands on the top remaining board,
/// a tug, then a jerk back with the board as it comes off), vaulting through the window (hands on the sill, tilt, legs
/// tucked, a landing and a stagger), rising out of the ground (arms first), a shamble / jog / sprint keyed to the speed,
/// per-zombie variation by id (<see cref="ZombieLook"/>), the swipe timed to the damage and a flinch when shot.
/// Pure functions of <see cref="ZombieAnimInput"/>: no allocations, deterministic, the same on every PC.
/// </summary>
public static class ZombieAnim
{
    // ---- Body dimensions (the frame the old zombie was drawn in) ----

    /// <summary>Upper arm and forearm (to the middle of the hand) lengths; thigh and shin (to the ankle).</summary>
    public const float ArmUp = 0.38f, ArmLo = 0.42f, LegUp = 0.45f, LegLo = 0.43f;

    /// <summary>Standing pelvis height, hip and shoulder offsets.</summary>
    public const float PelvisY = 0.86f, HipX = 0.15f, ShoulderX = 0.37f, ShoulderY = 0.55f;

    /// <summary>How far the hands reach, with a little slack so a reached target is reached exactly.</summary>
    public const float Reach = (ArmUp + ArmLo) * 0.97f;

    /// <summary>The share of a board's cycle the yank takes (the board leaves with the hands), then the throw.</summary>
    public const float YankShare = 0.12f, ThrowShare = 0.28f;

    /// <summary>How far the body may step towards a board it can't reach from where it stands (metres; presentation only).</summary>
    public const float MaxStep = 1.05f;

    /// <summary>Half a step's length by speed class (the walk cycle covers four of these, so <see cref="Game"/> keys the cycle to the speed).</summary>
    public static float StrideHalf(ZSpeed s) => s switch { ZSpeed.Sprint => 0.46f, ZSpeed.Run => 0.33f, _ => 0.24f };

    /// <summary>Beyond this distance limbs are drawn as one box each (today's count of boxes).</summary>
    public static float LodRange = 28f;

    private static readonly Vector3 ArmPoleR = Vector3.Normalize(new Vector3(0.55f, -0.65f, 0.45f));
    private static readonly Vector3 ArmPoleL = Vector3.Normalize(new Vector3(-0.55f, -0.65f, 0.45f));
    private static readonly Vector3 KneePole = Vector3.Normalize(new Vector3(0, 0.15f, -1f));

    // ---- The window: boards and sill -------------------------------------------------------------------

    /// <summary>The centre of board <paramref name="k"/> (0 = bottom) on the window (world): the same place the window draws it.</summary>
    public static Vector3 BoardCenter(in ZombieAnimInput a, int k) =>
        new(a.WinCenter.X, a.SillY + (k + 0.5f) * a.WinHeight / Math.Max(1, a.MaxBoards), a.WinCenter.Z);

    /// <summary>Where a hand grabs board <paramref name="k"/> (world): its outer face, a little in from each end.</summary>
    public static Vector3 BoardGrip(in ZombieAnimInput a, int k, int side) =>
        BoardCenter(a, k) - a.WinIn * 0.04f + a.WinSide * (side * MathF.Min(0.28f, a.WinHalfWidth * 0.6f));

    /// <summary>Where a hand goes on the sill when climbing (world): on top, near the outer edge.</summary>
    public static Vector3 SillGrip(in ZombieAnimInput a, int side) =>
        new Vector3(a.WinCenter.X, a.SillY + 0.03f, a.WinCenter.Z) - a.WinIn * 0.06f + a.WinSide * (side * MathF.Min(0.26f, a.WinHalfWidth * 0.6f));

    /// <summary>
    /// The yank: where a torn board (and the hands holding it) is, relative to its place on the window, <paramref name="k"/>
    /// of the way through the yank (0-1): pulled out towards the zombie and up a little (world, outward = -WinIn).
    /// </summary>
    public static Vector3 Yank(in ZombieAnimInput a, float k)
    {
        var e = 1 - (1 - Math.Clamp(k, 0, 1)) * (1 - Math.Clamp(k, 0, 1)); // ease out: the jerk
        return -a.WinIn * (0.42f * e) + new Vector3(0, 0.08f * e, 0);
    }

    // ---- Small helpers -----------------------------------------------------------------------------------

    private static float Smoothstep(float e0, float e1, float x)
    {
        var t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>World to the zombie's own frame (feet at the origin, -Z facing).</summary>
    public static Vector3 ToLocal(in ZombieAnimInput a, Vector3 world)
    {
        var d = world - a.Pos;
        float c = MathF.Cos(a.Yaw), s = MathF.Sin(a.Yaw);
        // The inverse of CreateRotationY(-yaw): rotate by +yaw.
        return new Vector3(d.X * c - d.Z * s, d.Y, d.X * s + d.Z * c);
    }

    /// <summary>The upper body's frame (hips at the origin) in the zombie's own frame.</summary>
    public static Matrix4x4 UpperLocal(in ZombieTargets t) =>
        Matrix4x4.CreateRotationZ(t.Roll) * Matrix4x4.CreateRotationX(-t.Pitch) * Matrix4x4.CreateRotationY(t.Twist) * Matrix4x4.CreateTranslation(t.Pelvis);

    /// <summary>A shoulder (+1 right, -1 left) in the zombie's own frame.</summary>
    public static Vector3 Shoulder(in ZombieTargets t, int side) => Vector3.Transform(new Vector3(side * ShoulderX, ShoulderY, 0), UpperLocal(t));

    /// <summary>
    /// How far a shoulder at <paramref name="s"/> must move along the unit vector <paramref name="dir"/> to bring
    /// <paramref name="target"/> into reach (0 if it already is).
    /// </summary>
    public static float ShiftToReach(Vector3 s, Vector3 target, Vector3 dir)
    {
        var d = target - s;
        var len2 = d.LengthSquared();
        if (len2 <= Reach * Reach)
            return 0;
        var dh = Vector3.Dot(d, dir);
        var disc = dh * dh - len2 + Reach * Reach;
        return disc >= 0 ? MathF.Max(0, dh - MathF.Sqrt(disc)) : MathF.Max(0, dh);
    }

    /// <summary>
    /// Two-bone IK: the middle joint and the end for a chain from <paramref name="root"/> towards <paramref name="target"/>
    /// (bones <paramref name="l1"/>, <paramref name="l2"/>), bending towards <paramref name="pole"/>. Out of reach: the
    /// chain points at the target, nearly straight.
    /// </summary>
    public static void SolveTwoBone(Vector3 root, Vector3 target, float l1, float l2, Vector3 pole, out Vector3 joint, out Vector3 end)
    {
        var d = target - root;
        var dist = d.Length();
        var dir = dist > 1e-5f ? d / dist : new Vector3(0, -1, 0);
        dist = Math.Clamp(dist, MathF.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-4f);
        end = root + dir * dist;
        var a = (l1 * l1 + dist * dist - l2 * l2) / (2 * dist);
        var h = MathF.Sqrt(MathF.Max(0, l1 * l1 - a * a));
        var bend = pole - dir * Vector3.Dot(pole, dir);
        if (bend.LengthSquared() < 1e-6f)
        {
            bend = new Vector3(0, 0, 1) - dir * dir.Z;
            if (bend.LengthSquared() < 1e-6f)
                bend = new Vector3(1, 0, 0) - dir * dir.X;
        }
        joint = root + dir * a + Vector3.Normalize(bend) * h;
    }

    /// <summary>A frame for a bone from <paramref name="a"/> to <paramref name="b"/>: Y along it, Z towards <paramref name="hint"/>, origin at its middle.</summary>
    public static Matrix4x4 Bone(Vector3 a, Vector3 b, Vector3 hint)
    {
        var y = b - a;
        var len = y.Length();
        y = len > 1e-6f ? y / len : Vector3.UnitY;
        var z = hint - y * Vector3.Dot(hint, y);
        if (z.LengthSquared() < 1e-6f)
        {
            z = Vector3.UnitZ - y * y.Z;
            if (z.LengthSquared() < 1e-6f)
                z = Vector3.UnitX - y * y.X;
        }
        z = Vector3.Normalize(z);
        var x = Vector3.Cross(y, z);
        var m = (a + b) * 0.5f;
        return new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, m.X, m.Y, m.Z, 1);
    }

    // ---- Targets ------------------------------------------------------------------------------------------

    /// <summary>The targets for one frame (pure; no allocations).</summary>
    public static void Targets(in ZombieAnimInput a, out ZombieTargets t)
    {
        var look = new ZombieLook(a.Id);
        switch (a.Mode)
        {
            case ZAnimMode.Tearing:
            case ZAnimMode.WindowIdle:
                WindowTargets(a, look, out t);
                break;
            case ZAnimMode.Vaulting:
                VaultTargets(a, look, out t);
                break;
            case ZAnimMode.Rising:
                RiseTargets(a, look, out t);
                break;
            default:
                Locomotion(a, look, out t);
                break;
        }
        if (a.Mode == ZAnimMode.Dead)
            return; // the arms go up as it tips over (Solve), no overlays
        if (a.Attack >= 0 && a.Mode != ZAnimMode.Vaulting && a.Mode != ZAnimMode.Rising)
            SwipeOverlay(a, look, ref t);
        if (a.Stagger >= 0 && a.Stagger < 0.7f && a.Mode == ZAnimMode.Locomotion)
        {
            // Just landed from the window: knees give, a wobble, arms out for balance.
            var w = 1 - a.Stagger / 0.7f;
            w *= w;
            t.Pelvis.Y -= 0.2f * w;
            t.Pitch += 0.3f * w;
            t.Roll += 0.22f * w * MathF.Sin(a.Stagger * 15f);
            t.HandR += new Vector3(0.25f * w, 0.12f * w, 0.1f * w);
            t.HandL += new Vector3(-0.25f * w, 0.12f * w, 0.1f * w);
            t.FootR.X += 0.06f * w;
            t.FootL.X -= 0.06f * w;
        }
        if (a.Flinch > 0.01f && a.Mode != ZAnimMode.Vaulting && a.Mode != ZAnimMode.Tearing)
        {
            // Shot: the torso snaps back, the head jerks, the arms fly up a little.
            var f = Math.Clamp(a.Flinch, 0, 1);
            t.Pitch -= 0.35f * f;
            t.HeadNod -= 0.45f * f;
            t.Pelvis.Z += 0.06f * f;
            t.HandR += new Vector3(0, 0.1f * f, 0.16f * f);
            t.HandL += new Vector3(0, 0.1f * f, 0.16f * f);
        }
        else if (a.Flinch > 0.01f)
        {
            // At the window the hands stay on the board: only the head and torso flinch.
            var f = Math.Clamp(a.Flinch, 0, 1);
            t.HeadNod -= 0.45f * f;
            t.Roll += 0.12f * f;
        }
    }

    /// <summary>Hands in front of a shoulder, for the arm styles (its own frame).</summary>
    private static Vector3 ArmRest(in ZombieAnimInput a, in ZombieLook look, Vector3 s, int side, float gaitSide, float amp)
    {
        var reach = look.ArmStyle == 0 || (look.ArmStyle == 1 && side == look.ReachSide);
        var sway = MathF.Sin(a.Time * 1.3f + side * 1.7f + a.Id) * 0.03f;
        switch (a.Speed)
        {
            case ZSpeed.Sprint when !reach || look.ArmStyle != 0:
                // Pumping hard.
                return s + new Vector3(side * 0.08f, -0.42f, -0.12f - 0.42f * amp * MathF.Sin(gaitSide + MathF.PI));
            case ZSpeed.Run:
                return s + new Vector3(side * 0.05f, -0.36f + look.ArmHeight, -0.4f - 0.24f * amp * MathF.Sin(gaitSide + MathF.PI));
        }
        if (reach)
            return s + new Vector3(side * 0.03f, -0.1f + look.ArmHeight + sway + 0.04f * amp * MathF.Sin(gaitSide * 0.5f + side), -0.66f);
        return s + new Vector3(side * 0.07f, -0.66f, -0.1f + sway + 0.12f * amp * MathF.Sin(gaitSide + MathF.PI));
    }

    private static void Locomotion(in ZombieAnimInput a, in ZombieLook look, out ZombieTargets t)
    {
        var amp = Math.Clamp(a.Move, 0, 1);
        var stride = StrideHalf(a.Speed);
        var (lift, pitch, bob) = a.Speed switch
        {
            ZSpeed.Sprint => (0.24f, 0.55f, 0.06f),
            ZSpeed.Run => (0.15f, 0.3f, 0.045f),
            _ => (0.07f, 0.16f, 0.025f),
        };
        var limp = a.Speed == ZSpeed.Sprint ? look.Limp * 0.4f : look.Limp;
        t = default;
        t.Pitch = pitch + look.Lean;
        t.Pelvis = new Vector3(0, PelvisY - 0.03f - bob * amp * (0.5f + 0.5f * MathF.Cos(2 * a.Gait)), 0.03f + 0.06f * t.Pitch);
        for (var side = -1; side <= 1; side += 2)
        {
            var g = a.Gait + (side > 0 ? 0 : MathF.PI);
            var lame = side == look.LimpSide ? limp : 0f;
            var sh = stride * amp * (1 - 0.5f * lame);
            var up = MathF.Max(0, MathF.Cos(g)) * lift * amp * (1 - 0.75f * lame);
            var foot = new Vector3(side * (0.17f - 0.03f * lame), up, -MathF.Sin(g) * sh + (a.Speed == ZSpeed.Walk ? 0.04f * lame : 0));
            if (side > 0)
                t.FootR = foot;
            else
                t.FootL = foot;
            // The lame leg gives: the hips drop while it carries the weight.
            if (lame > 0)
                t.Pelvis.Y -= 0.06f * lame * MathF.Max(0, -MathF.Cos(g)) * (0.4f + 0.6f * amp);
        }
        t.Roll = look.LimpSide * look.Limp * 0.1f * (0.3f + MathF.Sin(a.Gait) * amp) + 0.04f * amp * MathF.Sin(a.Gait);
        t.Twist = 0.12f * amp * MathF.Sin(a.Gait) * (a.Speed == ZSpeed.Walk ? 0.6f : 1f);
        t.HeadRoll = look.HeadRoll + 0.05f * MathF.Sin(a.Gait + a.Time * 0.3f);
        t.HeadNod = look.HeadNod + 0.06f * amp * MathF.Sin(2 * a.Gait) - (a.Speed == ZSpeed.Sprint ? 0.25f : 0);
        var sr = Shoulder(t, 1);
        var sl = Shoulder(t, -1);
        t.HandR = ArmRest(a, look, sr, 1, a.Gait, amp);
        t.HandL = ArmRest(a, look, sl, -1, a.Gait + MathF.PI, amp);
    }

    /// <summary>At a window: tearing the top remaining board (or clawing at an empty opening).</summary>
    private static void WindowTargets(in ZombieAnimInput a, in ZombieLook look, out ZombieTargets t)
    {
        // The cycle: yank (the board comes off with the hands), throw it aside, reach for the next, grip and tug, brace.
        var u = a.Mode == ZAnimMode.Tearing ? a.Phase - MathF.Floor(a.Phase) : 0.6f;
        var fresh = a.SinceState < u * MathF.Max(0.1f, a.TearSeconds) + 0.05f; // no board torn yet this visit: start at the reach
        var yanking = a.Mode == ZAnimMode.Tearing && !fresh && u < ThrowShare && a.Boards > 0 && a.Boards < a.MaxBoards; // the board in slot Boards just came off (none left: it climbs in now)
        var tearing = a.Mode == ZAnimMode.Tearing && (a.Boards > 0 || yanking);
        var top = Math.Clamp(a.Boards - 1, 0, a.MaxBoards - 1);
        var prev = Math.Clamp(a.Boards, 0, a.MaxBoards - 1);
        var onBoard = a.Mode == ZAnimMode.Tearing && a.Boards > 0;
        var gR = ToLocal(a, onBoard ? BoardGrip(a, top, 1) : SillGrip(a, 1) + new Vector3(0, 0.45f, 0));
        var gL = ToLocal(a, onBoard ? BoardGrip(a, top, -1) : SillGrip(a, -1) + new Vector3(0, 0.45f, 0));
        var pR = ToLocal(a, BoardGrip(a, prev, 1));
        var pL = ToLocal(a, BoardGrip(a, prev, -1));
        var height = yanking ? MathF.Max(gR.Y + gL.Y, pR.Y + pL.Y) * 0.5f : (gR.Y + gL.Y) * 0.5f;
        t = default;
        t.Pelvis = new Vector3(0, PelvisY - 0.04f + 0.07f * Math.Clamp((height - 1.5f) / 0.5f, 0, 1), 0.05f); // up on its toes for the top boards
        t.Pitch = Lerp(0.5f, 0.05f, Math.Clamp((height - 0.95f) / 1.0f, 0, 1)) + look.Lean * 0.5f; // stands up for the high boards
        t.HeadRoll = look.HeadRoll * 0.6f;
        t.HeadNod = look.HeadNod * 0.5f - 0.1f;
        t.FootR = new Vector3(0.18f, 0, -0.12f);
        t.FootL = new Vector3(-0.18f, 0, 0.1f);
        if (yanking && u < YankShare)
            t.Pitch -= 0.22f * (1 - u / YankShare) * Smoothstep(0, 0.3f, u / YankShare); // jerked back
        else if (onBoard && u > 0.88f)
            t.Pitch -= 0.1f * Smoothstep(0.88f, 1f, u); // bracing to pull
        if (onBoard && u >= 0.5f)
            t.Roll = 0.06f * MathF.Sin(u * 19f); // tugging side to side

        // Step in until both hands reach the board (blended in as it arrives; presentation only). While yanking, the
        // board that came off (one slot up) must be in reach too.
        var forward = new Vector3(0, 0, -1);
        var sR0 = Shoulder(t, 1);
        var sL0 = Shoulder(t, -1);
        var step = MathF.Max(ShiftToReach(sR0, gR, forward), ShiftToReach(sL0, gL, forward));
        if (yanking)
            step = MathF.Max(step, MathF.Max(ShiftToReach(sR0, pR, forward), ShiftToReach(sL0, pL, forward)));
        step = MathF.Min(step, MaxStep) * Smoothstep(0, 0.35f, a.SinceState);
        t.Pelvis += new Vector3(0, 0, -step + (yanking && u < YankShare ? 0.08f * (1 - u / YankShare) : 0));
        t.FootR += new Vector3(0, 0, -step);
        t.FootL += new Vector3(0, 0, -step);
        var sR = Shoulder(t, 1);
        var sL = Shoulder(t, -1);
        var restR = sR + new Vector3(0.18f, -0.5f, -0.25f);
        var restL = sL + new Vector3(-0.18f, -0.5f, -0.25f);
        if (!tearing)
        {
            // Clawing at the opening.
            var claw = MathF.Sin(a.Time * 6f + a.Id);
            t.HandR = gR + new Vector3(0, 0.12f * claw, -0.15f * MathF.Max(0, claw));
            t.HandL = gL + new Vector3(0, -0.12f * claw, -0.15f * MathF.Max(0, -claw));
            t.Pitch += 0.05f * claw;
            return;
        }
        if (yanking && u < YankShare)
        {
            // The yank: the board that just came off leaves with the hands (the same Yank the flying board follows).
            var off = ToLocalDir(a, Yank(a, u / YankShare));
            t.HandR = pR + off;
            t.HandL = pL + off;
        }
        else if (yanking)
        {
            // Throwing it aside and down.
            var k = Smoothstep(YankShare, ThrowShare, u);
            var off = ToLocalDir(a, Yank(a, 1));
            var throwTo = new Vector3(look.SwipeSide * 0.45f, -0.35f, 0.1f);
            t.HandR = Vector3.Lerp(pR + off, restR + throwTo, k);
            t.HandL = Vector3.Lerp(pL + off, restL + throwTo, k);
        }
        else if (u < 0.5f)
        {
            // Reaching for the next board.
            var k = Smoothstep(fresh ? 0f : ThrowShare, 0.5f, u);
            t.HandR = Vector3.Lerp(restR, gR, k);
            t.HandL = Vector3.Lerp(restL, gL, k);
        }
        else
        {
            // Hands on the board, tugging at it (the board wobbles with them), then a brace for the jerk.
            var tug = MathF.Sin(u * 38f + a.Id) * 0.012f * (1 - Smoothstep(0.85f, 0.95f, u));
            t.HandR = gR + new Vector3(0, 0, tug);
            t.HandL = gL + new Vector3(0, 0, -tug);
        }
    }

    /// <summary>A world direction in the zombie's own frame.</summary>
    private static Vector3 ToLocalDir(in ZombieAnimInput a, Vector3 d)
    {
        float c = MathF.Cos(a.Yaw), s = MathF.Sin(a.Yaw);
        return new Vector3(d.X * c - d.Z * s, d.Y, d.X * s + d.Z * c);
    }

    /// <summary>Climbing in: hands on the sill, the body tips forward over it with the legs tucked, lands inside.</summary>
    private static void VaultTargets(in ZombieAnimInput a, in ZombieLook look, out ZombieTargets t)
    {
        var p = Math.Clamp(a.Phase, 0, 1);
        var gR = ToLocal(a, SillGrip(a, 1));
        var gL = ToLocal(a, SillGrip(a, -1));
        var ground = ToLocal(a, new Vector3(a.Pos.X, a.Pos.Y - MathF.Sin(p * MathF.PI) * 0.9f, a.Pos.Z)).Y; // the floor under the arc (the server's lift)
        t = default;
        var tuck = Smoothstep(0.08f, 0.32f, p) * (1 - Smoothstep(0.58f, 0.82f, p));
        var land = Smoothstep(0.78f, 1f, p);
        t.Pitch = Lerp(0.35f, 1.15f, Smoothstep(0f, 0.42f, p)) * (1 - Smoothstep(0.55f, 0.95f, p)) + 0.35f * Smoothstep(0.55f, 0.95f, p) * (1 - land * 0.4f);
        t.Pelvis = new Vector3(0, Lerp(PelvisY - 0.08f, 0.48f, tuck), 0.04f);
        t.Pelvis.Y = Lerp(t.Pelvis.Y, ground + 0.6f, land * (1 - Smoothstep(0.95f, 1f, p) * 0.5f)); // knees give on landing
        t.HeadNod = -0.25f * tuck + look.HeadNod * 0.5f;
        t.HeadRoll = look.HeadRoll * 0.5f;
        // Legs: tucked up under the hips, then reaching down for the floor inside.
        var tuckR = new Vector3(0.16f, t.Pelvis.Y - 0.36f, 0.12f);
        var tuckL = new Vector3(-0.14f, t.Pelvis.Y - 0.3f, 0.2f);
        var downR = new Vector3(0.18f, ground, -0.1f);
        var downL = new Vector3(-0.18f, ground, 0.12f);
        var legsDown = 1 - tuck;
        t.FootR = Vector3.Lerp(tuckR, downR, legsDown);
        t.FootL = Vector3.Lerp(tuckL, downL, legsDown);
        // Hands: reach for the sill, keep them there while the body goes over, then let go and reach for the floor.
        var plant = Smoothstep(0f, 0.16f, p) * (1 - Smoothstep(0.55f, 0.7f, p));
        // Lean the body towards the sill until both hands reach it (only while planted).
        var mid = (Shoulder(t, 1) + Shoulder(t, -1)) * 0.5f;
        var toSill = (gR + gL) * 0.5f - mid;
        if (toSill.LengthSquared() > 1e-6f && plant > 0)
        {
            var dir = Vector3.Normalize(toSill);
            var x = MathF.Max(ShiftToReach(Shoulder(t, 1), gR, dir), ShiftToReach(Shoulder(t, -1), gL, dir));
            t.Pelvis += dir * (MathF.Min(x, 0.9f) * Smoothstep(0f, 0.16f, p) * (1 - Smoothstep(0.55f, 0.7f, p)));
        }
        var sR = Shoulder(t, 1);
        var sL = Shoulder(t, -1);
        var freeR = sR + new Vector3(0.12f, -0.35f - 0.2f * land, -0.45f);
        var freeL = sL + new Vector3(-0.12f, -0.35f - 0.2f * land, -0.45f);
        t.HandR = Vector3.Lerp(freeR, gR, plant);
        t.HandL = Vector3.Lerp(freeL, gL, plant);
    }

    /// <summary>Out of the ground: arms burst up first, hands plant on the ground and push, the body and then the legs come out.</summary>
    private static void RiseTargets(in ZombieAnimInput a, in ZombieLook look, out ZombieTargets t)
    {
        var r = Math.Clamp(a.Phase, 0, 1);
        var depth = r < 0.35f ? Lerp(-2.1f, -1.25f, Smoothstep(0, 0.35f, r))
            : r < 0.65f ? Lerp(-1.25f, -0.55f, Smoothstep(0.35f, 0.65f, r))
            : Lerp(-0.55f, 0f, Smoothstep(0.65f, 1f, r));
        t = default;
        t.Pelvis = new Vector3(0, depth + PelvisY - 0.04f, 0.03f);
        t.Pitch = r < 0.35f ? 0.1f : Lerp(0.1f, 0.75f, Smoothstep(0.35f, 0.6f, r)) * (1 - Smoothstep(0.75f, 1f, r)) + 0.2f * Smoothstep(0.75f, 1f, r);
        t.HeadNod = -0.35f * (1 - Smoothstep(0.2f, 0.5f, r)) + look.HeadNod * Smoothstep(0.6f, 1f, r);
        t.HeadRoll = look.HeadRoll * Smoothstep(0.6f, 1f, r);
        t.Roll = 0.12f * MathF.Sin(a.Time * 5f) * (1 - r);
        // Legs: still in the hole; the right foot steps up onto the ground, then the left.
        var stepR = Smoothstep(0.65f, 0.85f, r);
        var stepL = Smoothstep(0.82f, 1f, r);
        t.FootR = new Vector3(0.17f, Lerp(depth, 0, stepR), Lerp(0, -0.25f, stepR) * (1 - stepL * 0.6f));
        t.FootL = new Vector3(-0.17f, Lerp(depth, 0, stepL), 0.05f);
        var sR = Shoulder(t, 1);
        var sL = Shoulder(t, -1);
        // Arms: up and clawing, then planted on the ground in front, then down to the shamble.
        var claw = MathF.Sin(a.Time * 14f + a.Id) * 0.06f;
        var upR = sR + new Vector3(0.12f, 0.66f + claw, -0.15f);
        var upL = sL + new Vector3(-0.12f, 0.66f - claw, -0.15f);
        var plantR = new Vector3(0.32f, 0.02f, -0.45f);
        var plantL = new Vector3(-0.32f, 0.02f, -0.45f);
        var restR = sR + new Vector3(0.05f, -0.15f, -0.6f);
        var restL = sL + new Vector3(-0.05f, -0.15f, -0.6f);
        var toPlant = Smoothstep(0.28f, 0.4f, r);
        var toRest = Smoothstep(0.66f, 0.9f, r);
        t.HandR = Vector3.Lerp(Vector3.Lerp(upR, plantR, toPlant), restR, toRest);
        t.HandL = Vector3.Lerp(Vector3.Lerp(upL, plantL, toPlant), restL, toRest);
    }

    /// <summary>The swipe: the arm winds up behind, strikes across right as the damage lands (at the windup), then recovers.</summary>
    private static void SwipeOverlay(in ZombieAnimInput a, in ZombieLook look, ref ZombieTargets t)
    {
        var s = a.Attack;
        var w = MathF.Max(0.05f, a.Windup);
        const float strike = 0.14f, recover = 0.32f;
        if (s > w + strike + recover)
            return;
        var side = look.SwipeSide;
        var weight = s < w + strike ? 1f : 1 - Smoothstep(w + strike, w + strike + recover, s);
        var sh = Shoulder(t, side);
        var other = Shoulder(t, -side);
        var back = sh + new Vector3(side * 0.32f, 0.3f, 0.24f);
        var across = sh + new Vector3(-side * 0.3f, -0.28f, -0.62f);
        Vector3 hand;
        float twist, pitch;
        if (s < w)
        {
            var k = Smoothstep(0, w, s);
            var from = side > 0 ? t.HandR : t.HandL;
            hand = Vector3.Lerp(from, back, k);
            twist = side * 0.35f * k;
            pitch = -0.1f * k;
        }
        else
        {
            var k = Smoothstep(w, w + strike, s);
            hand = Vector3.Lerp(back, across, k);
            twist = Lerp(side * 0.35f, -side * 0.3f, k);
            pitch = 0.25f * k;
        }
        var reach = other + new Vector3(-side * 0.02f, -0.12f, -0.56f);
        if (side > 0)
        {
            t.HandR = Vector3.Lerp(t.HandR, hand, weight);
            t.HandL = Vector3.Lerp(t.HandL, reach, weight * 0.6f);
        }
        else
        {
            t.HandL = Vector3.Lerp(t.HandL, hand, weight);
            t.HandR = Vector3.Lerp(t.HandR, reach, weight * 0.6f);
        }
        t.Twist += twist * weight;
        t.Pitch += pitch * weight;
    }

    /// <summary>
    /// Frame-to-frame smoothing of the targets (so a change of state blends instead of popping): moves
    /// <paramref name="state"/> towards <paramref name="target"/> by <paramref name="k"/> (0-1).
    /// </summary>
    public static void Smooth(ref ZombieTargets state, in ZombieTargets target, float k)
    {
        state.Pelvis = Vector3.Lerp(state.Pelvis, target.Pelvis, k);
        state.Pitch = Lerp(state.Pitch, target.Pitch, k);
        state.Roll = Lerp(state.Roll, target.Roll, k);
        state.Twist = Lerp(state.Twist, target.Twist, k);
        state.HeadRoll = Lerp(state.HeadRoll, target.HeadRoll, k);
        state.HeadNod = Lerp(state.HeadNod, target.HeadNod, k);
        state.HandR = Vector3.Lerp(state.HandR, target.HandR, k);
        state.HandL = Vector3.Lerp(state.HandL, target.HandL, k);
        state.FootR = Vector3.Lerp(state.FootR, target.FootR, k);
        state.FootL = Vector3.Lerp(state.FootL, target.FootL, k);
    }

    // ---- Solve --------------------------------------------------------------------------------------------

    /// <summary>The root (feet) frame in the world: facing, and for a dead zombie the fall backwards and the sink.</summary>
    public static Matrix4x4 RootFrame(in ZombieAnimInput a)
    {
        var body = Matrix4x4.CreateRotationY(-a.Yaw);
        var pos = a.Pos;
        if (a.Mode == ZAnimMode.Dead)
        {
            var fall = Math.Clamp(a.Phase / 0.45f, 0, 1);
            body = Matrix4x4.CreateRotationX(fall * 1.45f) * body;
            pos.Y -= Math.Clamp(a.Phase - 0.9f, 0, 1) * 0.6f;
        }
        return body * Matrix4x4.CreateTranslation(pos);
    }

    /// <summary>The pose for the targets: the IK for each limb, and every frame in the world (pure; no allocations).</summary>
    public static void Solve(in ZombieAnimInput a, in ZombieTargets t, out ZombiePose p)
    {
        p = default;
        var root = RootFrame(a);
        p.Root = root;
        var upperL = UpperLocal(t);
        p.Upper = upperL * root;
        var neck = new Vector3(0, 0.6f, -0.04f);
        p.Head = Matrix4x4.CreateTranslation(-neck) * Matrix4x4.CreateRotationZ(t.HeadRoll) * Matrix4x4.CreateRotationX(-t.HeadNod) * Matrix4x4.CreateTranslation(neck) * p.Upper;

        var handR = t.HandR;
        var handL = t.HandL;
        if (a.Mode == ZAnimMode.Dead)
        {
            // Arms fly up as it goes over.
            var up = Math.Clamp(a.Phase / 0.45f, 0, 1);
            handR = Vector3.Lerp(handR, Vector3.Transform(new Vector3(ShoulderX + 0.15f, ShoulderY + 0.6f, -0.3f), upperL), up);
            handL = Vector3.Lerp(handL, Vector3.Transform(new Vector3(-ShoulderX - 0.15f, ShoulderY + 0.6f, -0.3f), upperL), up);
        }
        var sR = Vector3.Transform(new Vector3(ShoulderX, ShoulderY, 0), upperL);
        var sL = Vector3.Transform(new Vector3(-ShoulderX, ShoulderY, 0), upperL);
        SolveTwoBone(sR, handR, ArmUp, ArmLo, ArmPoleR, out var eR, out var hR);
        SolveTwoBone(sL, handL, ArmUp, ArmLo, ArmPoleL, out var eL, out var hL);
        var twistY = Matrix4x4.CreateRotationY(t.Twist * 0.3f);
        var hipR = t.Pelvis + Vector3.Transform(new Vector3(HipX, -0.02f, 0), twistY);
        var hipL = t.Pelvis + Vector3.Transform(new Vector3(-HipX, -0.02f, 0), twistY);
        SolveTwoBone(hipR, t.FootR, LegUp, LegLo, KneePole, out var kR, out var fR);
        SolveTwoBone(hipL, t.FootL, LegUp, LegLo, KneePole, out var kL, out var fL);

        var fwd = new Vector3(0, 0, -1);
        p.ArmUpR = Bone(sR, eR, ArmPoleR) * root;
        p.ArmLoR = Bone(eR, hR, ArmPoleR) * root;
        p.ArmUpL = Bone(sL, eL, ArmPoleL) * root;
        p.ArmLoL = Bone(eL, hL, ArmPoleL) * root;
        p.LegUpR = Bone(hipR, kR, fwd) * root;
        p.LegLoR = Bone(kR, fR, fwd) * root;
        p.LegUpL = Bone(hipL, kL, fwd) * root;
        p.LegLoL = Bone(kL, fL, fwd) * root;
        p.ShoulderR = Vector3.Transform(sR, root);
        p.ShoulderL = Vector3.Transform(sL, root);
        p.ElbowR = Vector3.Transform(eR, root);
        p.ElbowL = Vector3.Transform(eL, root);
        p.HandR = Vector3.Transform(hR, root);
        p.HandL = Vector3.Transform(hL, root);
        p.HipR = Vector3.Transform(hipR, root);
        p.HipL = Vector3.Transform(hipL, root);
        p.KneeR = Vector3.Transform(kR, root);
        p.KneeL = Vector3.Transform(kL, root);
        p.FootR = Vector3.Transform(fR, root);
        p.FootL = Vector3.Transform(fL, root);
    }

    /// <summary>Targets then solve, no smoothing (the self-test and anything that wants the exact pose).</summary>
    public static void Evaluate(in ZombieAnimInput a, out ZombiePose p)
    {
        Targets(a, out var t);
        Solve(a, t, out p);
    }

    // ---- Drawing ------------------------------------------------------------------------------------------

    /// <summary>
    /// Draws the posed body (immediate mode, the zombie palette): thighs, shins and feet; the torso and head
    /// (BodyModel.DrawZombieUpper); glowing eyes;
    /// upper arms, forearms and hands. Far away (<paramref name="lod"/>) each limb is one box. Returns the boxes drawn.
    /// </summary>
    public static int Draw(Renderer g, in ZombiePose p, Vector3 shirt, Vector3 skin, Vector3 pants, bool detail, bool lod, bool eyes)
    {
        var n = 0;
        var arm = skin * 0.92f;
        if (lod)
        {
            n += Limb(g, p.HipR, p.FootR, 0.21f, pants, p.Root);
            n += Limb(g, p.HipL, p.FootL, 0.21f, pants, p.Root);
        }
        else
        {
            g.Draw(Matrix4x4.CreateScale(0.21f, LegUp + 0.04f, 0.23f) * p.LegUpR, pants);
            g.Draw(Matrix4x4.CreateScale(0.21f, LegUp + 0.04f, 0.23f) * p.LegUpL, pants);
            g.Draw(Matrix4x4.CreateScale(0.17f, LegLo, 0.18f) * p.LegLoR, pants * 0.9f);
            g.Draw(Matrix4x4.CreateScale(0.17f, LegLo, 0.18f) * p.LegLoL, pants * 0.9f);
            n += 4;
            if (detail)
            {
                // Bare feet, pointing the way it faces.
                var fr = Feet(p.FootR, p.Root);
                var fl = Feet(p.FootL, p.Root);
                g.Draw(fr, skin * 0.7f);
                g.Draw(fl, skin * 0.7f);
                n += 2;
            }
        }
        n += BodyModel.DrawZombieUpper(g, p.Upper, p.Head, shirt, skin, detail);
        if (eyes)
            for (var side = -1; side <= 1; side += 2)
            {
                g.Draw(Matrix4x4.CreateScale(0.07f, 0.05f, 0.02f) * Matrix4x4.CreateTranslation(side * 0.09f, 0.84f, -0.23f) * p.Head, new Vector3(1f, 0.55f, 0.1f), 1f, mode: 2);
                n++;
            }
        if (lod)
        {
            n += Limb(g, p.ShoulderR, p.HandR, 0.13f, arm, p.Root);
            n += Limb(g, p.ShoulderL, p.HandL, 0.13f, arm, p.Root);
            return n;
        }
        g.Draw(Matrix4x4.CreateScale(0.14f, ArmUp + 0.04f, 0.14f) * p.ArmUpR, arm);
        g.Draw(Matrix4x4.CreateScale(0.14f, ArmUp + 0.04f, 0.14f) * p.ArmUpL, arm);
        g.Draw(Matrix4x4.CreateScale(0.12f, ArmLo, 0.12f) * p.ArmLoR, arm * 0.95f);
        g.Draw(Matrix4x4.CreateScale(0.12f, ArmLo, 0.12f) * p.ArmLoL, arm * 0.95f);
        n += 4;
        if (detail)
        {
            // Hands: a flat box at the end of each forearm, along it.
            g.Draw(Matrix4x4.CreateScale(0.13f, 0.12f, 0.06f) * Matrix4x4.CreateTranslation(0, ArmLo * 0.5f, 0) * p.ArmLoR, skin * 0.8f);
            g.Draw(Matrix4x4.CreateScale(0.13f, 0.12f, 0.06f) * Matrix4x4.CreateTranslation(0, ArmLo * 0.5f, 0) * p.ArmLoL, skin * 0.8f);
            n += 2;
        }
        return n;
    }

    /// <summary>One box from <paramref name="a"/> to <paramref name="b"/> (world), facing like the root.</summary>
    private static int Limb(Renderer g, Vector3 a, Vector3 b, float w, Vector3 color, in Matrix4x4 root)
    {
        var len = Vector3.Distance(a, b);
        var fwd = Vector3.TransformNormal(new Vector3(0, 0, -1), root);
        g.Draw(Matrix4x4.CreateScale(w, len + 0.05f, w) * Bone(a, b, fwd), color);
        return 1;
    }

    private static Matrix4x4 Feet(Vector3 ankle, in Matrix4x4 root)
    {
        var fwd = Vector3.TransformNormal(new Vector3(0, 0, -1), root);
        var up = Vector3.TransformNormal(Vector3.UnitY, root);
        var right = Vector3.Cross(fwd, up);
        var c = ankle + fwd * 0.07f - up * 0.02f;
        return Matrix4x4.CreateScale(0.13f, 0.07f, 0.26f) * new Matrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, -fwd.X, -fwd.Y, -fwd.Z, 0, c.X, c.Y, c.Z, 1);
    }

    // ---- Torn boards in flight -----------------------------------------------------------------------------

    /// <summary>How long a torn board lasts: the yank and throw, lying there, then it shrinks away.</summary>
    public const float BoardFlySeconds = 1.5f;

    /// <summary>
    /// A torn board's frame <paramref name="age"/> seconds after it came off (world; scale it by the board's size): first
    /// it follows the yank (<see cref="Yank"/>, with the hands), then it's flung aside, tumbles to the ground outside,
    /// lies there and shrinks away. <paramref name="seed"/> picks the side and the spin. <paramref name="scale"/> is 0 once it's gone.
    /// </summary>
    public static Matrix4x4 BoardFlight(in ZombieAnimInput a, int k, float age, int seed, float groundY, out float scale)
    {
        var yankTime = YankShare * MathF.Max(0.1f, a.TearSeconds);
        var start = BoardCenter(a, k);
        var side = (seed & 1) == 0 ? 1f : -1f;
        var spin = 3f + (seed >> 1 & 3);
        scale = 1f;
        Vector3 at;
        float tumble = 0, roll = 0;
        if (age < yankTime)
            at = start + Yank(a, age / yankTime);
        else
        {
            var f = age - yankTime;
            var from = start + Yank(a, 1);
            var vel = -a.WinIn * 1.3f + a.WinSide * (side * 1.4f) + new Vector3(0, 1.4f, 0);
            at = from + vel * f + new Vector3(0, -4.9f * f * f, 0);
            var floor = groundY + 0.03f;
            if (at.Y < floor)
            {
                // On the ground: it stops where it landed (solve for the landing time).
                var tLand = (1.4f + MathF.Sqrt(1.96f + 19.6f * MathF.Max(0, from.Y - floor))) / 9.8f;
                at = from + vel * tLand + new Vector3(0, -4.9f * tLand * tLand, 0);
                at.Y = floor;
                f = tLand;
            }
            tumble = f * spin;
            roll = f * spin * 0.6f * side;
            scale = 1 - Smoothstep(BoardFlySeconds - 0.35f, BoardFlySeconds, age);
        }
        // Long axis along the window (as it was on it), then tumbling.
        var yaw = MathF.Atan2(a.WinSide.X, -a.WinSide.Z);
        return Matrix4x4.CreateRotationZ(roll) * Matrix4x4.CreateRotationX(tumble) * Matrix4x4.CreateRotationY(-yaw + MathF.PI / 2) * Matrix4x4.CreateTranslation(at);
    }

    // ---- Culling and stats -------------------------------------------------------------------------------

    private static readonly Vector4[] Planes = new Vector4[6];

    /// <summary>The view frustum for <see cref="Visible"/> (row-vector view * projection, as the renderer keeps it).</summary>
    public static void SetFrustum(in Matrix4x4 m)
    {
        var c1 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        var c2 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        var c3 = new Vector4(m.M13, m.M23, m.M33, m.M43);
        var c4 = new Vector4(m.M14, m.M24, m.M34, m.M44);
        Planes[0] = c4 + c1;
        Planes[1] = c4 - c1;
        Planes[2] = c4 + c2;
        Planes[3] = c4 - c2;
        Planes[4] = c4 + c3;
        Planes[5] = c4 - c3;
        for (var i = 0; i < 6; i++)
        {
            var l = new Vector3(Planes[i].X, Planes[i].Y, Planes[i].Z).Length();
            if (l > 1e-6f)
                Planes[i] /= l;
        }
        _frustum = true;
    }

    private static bool _frustum;

    /// <summary>No frustum: everything is visible (headless, before the first camera).</summary>
    public static void ClearFrustum() => _frustum = false;

    /// <summary>Is a sphere in the view frustum?</summary>
    public static bool Visible(Vector3 c, float r)
    {
        if (!_frustum)
            return true;
        foreach (var p in Planes)
            if (p.X * c.X + p.Y * c.Y + p.Z * c.Z + p.W < -r)
                return false;
        return true;
    }

    private static long _ticks;
    private static int _posed, _culled, _boxes, _frames;

    /// <summary>Counts one posed and drawn zombie (with the time it took) or one culled.</summary>
    public static void Note(long t0, int boxes)
    {
        _ticks += Stopwatch.GetTimestamp() - t0;
        _posed++;
        _boxes += boxes;
    }

    public static void NoteCulled() => _culled++;

    public static void NoteFrame() => _frames++;

    /// <summary>One line for the log: zombies posed and culled per frame, boxes, CPU (since the last read).</summary>
    public static string Stats
    {
        get
        {
            var f = Math.Max(1, _frames);
            var s = $"zombie anim: {_posed / (double)f:0.0} posed, {_culled / (double)f:0.0} culled, {_boxes / (double)f:0} boxes per frame, CPU {_ticks * 1e6 / Stopwatch.Frequency / f:0.0} us/frame";
            _ticks = 0;
            _posed = _culled = _boxes = _frames = 0;
            return s;
        }
    }
}
