using System.Numerics;

namespace CallOfSlop;

/// <summary>Where a hand should be: position, rotation (hand space -> parent space) and the shape it makes.</summary>
public struct HandTarget
{
    public Vector3 Pos;
    public Quaternion Rot;
    public HandShape Shape;

    public HandTarget(Vector3 pos, Quaternion rot, HandShape shape)
    {
        Pos = pos;
        Rot = rot;
        Shape = shape;
    }

    /// <summary>Blends two targets: position lerped, rotation slerped, the shape switches half-way.</summary>
    public static HandTarget Blend(in HandTarget a, in HandTarget b, float t)
    {
        if (t <= 0)
            return a;
        if (t >= 1)
            return b;
        return new HandTarget(Vector3.Lerp(a.Pos, b.Pos, t), Quaternion.Slerp(a.Rot, b.Rot, t), t >= 0.5f ? b.Shape : a.Shape);
    }
}

/// <summary>Colours of one pair of arms.</summary>
public struct ArmColors
{
    public Vector3 Sleeve, Cuff, Glove, Fingers;

    /// <summary>The shared scheme: sleeves in the given colour, dark gauntlet cuffs, tan gloves.</summary>
    public static ArmColors For(Vector3 sleeve) =>
        new() { Sleeve = sleeve, Cuff = new Vector3(0.24f, 0.24f, 0.26f), Glove = new Vector3(0.34f, 0.28f, 0.2f), Fingers = new Vector3(0.3f, 0.245f, 0.175f) };
}

/// <summary>
/// One arm as a small transform hierarchy: root (fixed to the camera or body) -> shoulder (slides a little
/// towards far targets, like a clavicle / torso twist) -> upper arm -> forearm -> hand. The elbow comes from
/// analytic two-bone IK with a pole vector. Each bone's frame has local -Z along the bone, so the matrices
/// port directly to a scene graph (child world = child local * parent world).
/// </summary>
public sealed class ArmRig
{
    /// <summary>+1 for the right arm, -1 for the left (hand shapes mirror in X).</summary>
    public readonly float Side;

    public Vector3 Root;
    public float Upper, Fore;
    public Vector3 Pole;
    public float MaxShift;
    public float Thick;
    public float HandScale;

    // ---- Results of the last Solve (world space of whatever space the targets were in) ----
    public Vector3 Shoulder, Elbow, Wrist;
    public bool Reached;
    public Matrix4x4 UpperWorld, ForeWorld, HandWorld;
    public HandShape Shape;

    public ArmRig(float side, Vector3 root, float upper, float fore, Vector3 pole, float maxShift, float thick, float handScale = 1f)
    {
        Side = side;
        Root = root;
        Upper = upper;
        Fore = fore;
        Pole = Vector3.Normalize(pole);
        MaxShift = maxShift;
        Thick = thick;
        HandScale = handScale;
    }

    /// <summary>Wrist point in hand space for a shape (mirrored for the left hand).</summary>
    public Vector3 WristLocal(HandShape s)
    {
        var w = Arms.Wrists[(int)s];
        return new Vector3(w.X * Side, w.Y, w.Z) * HandScale;
    }

    /// <summary>
    /// Poses the arm so the hand sits on <paramref name="hand"/>. <paramref name="space"/> places the root and pole
    /// (identity in first person; the body transform in third person). Returns false if the hand was out of reach
    /// (the arm is then straight and the hand follows the clamped wrist).
    /// </summary>
    public bool Solve(in HandTarget hand, in Matrix4x4 space, Vector3 rootOffset)
    {
        var root = Vector3.Transform(Root + rootOffset, space);
        var pole = Vector3.Normalize(Vector3.TransformNormal(Pole, space));
        var wristOffset = Vector3.Transform(WristLocal(hand.Shape), hand.Rot);
        var target = hand.Pos + wristOffset;

        // Clavicle: a far target pulls the shoulder towards it a little.
        var to = target - root;
        var dist = to.Length();
        var shift = Math.Clamp(dist - (Upper + Fore) * 0.9f, 0f, MaxShift);
        Shoulder = dist > 1e-6f ? root + to * (shift / dist) : root;

        Reached = Arms.SolveTwoBone(Shoulder, target, Upper, Fore, pole, out Elbow, out Wrist);
        UpperWorld = Arms.BoneFrame(Shoulder, Elbow, pole);
        ForeWorld = Arms.BoneFrame(Elbow, Wrist, pole);
        HandWorld = Matrix4x4.CreateFromQuaternion(hand.Rot);
        HandWorld.Translation = Wrist - wristOffset; // the hand follows the (possibly clamped) wrist
        Shape = hand.Shape;
        return Reached;
    }

    /// <summary>Draws sleeve, elbow, forearm, gauntlet cuff and the hand's boxes.</summary>
    public void Draw(Renderer g, in ArmColors c)
    {
        var t = Thick;
        Arms.Box(g, new Vector3(t, t, Upper + t * 0.3f), new Vector3(0, 0, -Upper * 0.5f), UpperWorld, c.Sleeve);
        Arms.Box(g, new Vector3(t * 0.95f), Vector3.Zero, ForeWorld, c.Sleeve * 0.92f); // elbow
        Arms.Box(g, new Vector3(t * 0.84f, t * 0.8f, Fore * 0.8f), new Vector3(0, 0, -Fore * 0.4f), ForeWorld, c.Sleeve);
        Arms.Box(g, new Vector3(t * 0.9f, t * 0.86f, Fore * 0.26f), new Vector3(0, 0, -Fore * 0.87f), ForeWorld, c.Cuff);
        var parts = Arms.Shapes[(int)Shape];
        var k = HandScale;
        foreach (var p in parts)
            Arms.Box(g, p.Size * k, new Vector3(p.Center.X * Side, p.Center.Y, p.Center.Z) * k, HandWorld, p.Finger ? c.Fingers : c.Glove);
    }
}

/// <summary>Shared arm maths and the hand shapes.</summary>
public static class Arms
{
    /// <summary>One box of a hand shape, in right-hand space (metres).</summary>
    public readonly record struct Part(Vector3 Size, Vector3 Center, bool Finger);

    /// <summary>Hand shapes for the RIGHT hand, indexed by <see cref="HandShape"/>. The left hand mirrors X.</summary>
    public static readonly Part[][] Shapes =
    [
        [ // Wrap: palm right of the bar, fingers round the front, index along the trigger guard, thumb over the top
            new(new(0.026f, 0.085f, 0.08f), new(0.038f, -0.005f, 0.012f), false),
            new(new(0.07f, 0.075f, 0.024f), new(0.004f, -0.012f, -0.038f), true),
            new(new(0.022f, 0.07f, 0.03f), new(-0.034f, -0.012f, -0.02f), true),
            new(new(0.018f, 0.018f, 0.055f), new(0.03f, 0.05f, -0.03f), true),
            new(new(0.018f, 0.02f, 0.05f), new(-0.03f, 0.045f, -0.005f), true),
        ],
        [ // Under: palm up, fingers up the far side, thumb up the near side
            new(new(0.07f, 0.024f, 0.09f), new(-0.008f, -0.013f, 0.005f), false),
            new(new(0.02f, 0.05f, 0.08f), new(0.04f, 0.012f, -0.005f), true),
            new(new(0.018f, 0.04f, 0.055f), new(-0.043f, 0.008f, 0f), true),
        ],
        [ // Cup: palm left of the grip, fingers over the strong hand's, thumb forward along the frame
            new(new(0.026f, 0.08f, 0.08f), new(-0.052f, -0.015f, 0.012f), false),
            new(new(0.08f, 0.07f, 0.022f), new(-0.01f, -0.02f, -0.06f), true),
            new(new(0.018f, 0.018f, 0.055f), new(-0.035f, 0.035f, -0.02f), true),
        ],
        [ // Flat: palm down, fingers forward
            new(new(0.08f, 0.024f, 0.085f), new(0f, 0.012f, 0.005f), false),
            new(new(0.075f, 0.018f, 0.05f), new(0f, 0.01f, -0.06f), true),
            new(new(0.018f, 0.018f, 0.05f), new(-0.045f, 0.01f, -0.005f), true),
        ],
    ];

    /// <summary>Wrist point of each shape (right hand), where the forearm ends.</summary>
    public static readonly Vector3[] Wrists = [new(0.03f, -0.03f, 0.065f), new(-0.02f, -0.03f, 0.07f), new(-0.05f, -0.04f, 0.065f), new(0f, 0.018f, 0.07f)];

    /// <summary>
    /// Analytic two-bone IK: shoulder <paramref name="s"/>, target <paramref name="t"/>, bone lengths and a pole
    /// direction the elbow bends towards. The wrist lands exactly on the target when it's reachable; otherwise it is
    /// clamped onto the line towards it (fully stretched, or folded for targets too close). Returns whether it reached.
    /// </summary>
    public static bool SolveTwoBone(Vector3 s, Vector3 t, float l1, float l2, Vector3 pole, out Vector3 elbow, out Vector3 wrist)
    {
        var d = t - s;
        var dist = d.Length();
        var dir = dist > 1e-6f ? d / dist : SafeNormalize(pole, Vector3.UnitY);
        var minD = MathF.Abs(l1 - l2) + 1e-4f;
        var maxD = l1 + l2 - 1e-4f;
        var reach = Math.Clamp(dist, minD, maxD);
        var cosA = Math.Clamp((l1 * l1 + reach * reach - l2 * l2) / (2 * l1 * reach), -1f, 1f);
        var perp = pole - dir * Vector3.Dot(pole, dir);
        if (perp.LengthSquared() < 1e-12f)
            perp = Vector3.Cross(dir, MathF.Abs(dir.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY);
        perp = Vector3.Normalize(perp);
        elbow = s + dir * (l1 * cosA) + perp * (l1 * MathF.Sqrt(MathF.Max(0f, 1 - cosA * cosA)));
        wrist = s + dir * reach;
        return dist <= maxD && dist >= minD;
    }

    /// <summary>A rigid frame at <paramref name="a"/> whose local -Z points at <paramref name="b"/> and +Y leans towards <paramref name="up"/>.</summary>
    public static Matrix4x4 BoneFrame(Vector3 a, Vector3 b, Vector3 up)
    {
        var z = SafeNormalize(a - b, Vector3.UnitZ);
        var x = Vector3.Cross(up, z);
        if (x.LengthSquared() < 1e-10f)
            x = Vector3.Cross(MathF.Abs(z.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX, z);
        x = Vector3.Normalize(x);
        var y = Vector3.Cross(z, x);
        return new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, a.X, a.Y, a.Z, 1);
    }

    private static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
    {
        var l = v.Length();
        return l > 1e-6f ? v / l : fallback;
    }

    /// <summary>Draws a box of <paramref name="size"/> centred at <paramref name="center"/> in the frame <paramref name="frame"/>.</summary>
    public static void Box(Renderer g, Vector3 size, Vector3 center, in Matrix4x4 frame, Vector3 color) =>
        g.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(center) * frame, color);

    /// <summary>
    /// Draws what a hand carries during a reload (new magazine, shells, speedloader, ramrod) on the left hand.
    /// </summary>
    public static void DrawCarry(Renderer g, ArmRig hand, in ArmAnchorSet a)
    {
        var c = a.Carry;
        var off = hand.Shape switch
        {
            HandShape.Under => new Vector3(0, c.Y / 2, 0),
            HandShape.Flat => new Vector3(0, -c.Y / 2, -0.01f),
            _ => a.Style == ReloadStyle.LmgBox ? new Vector3(c.X * 0.5f - 0.02f, 0, 0) : a.Style == ReloadStyle.MuzzleLoad ? new Vector3(0, 0.12f, 0) : Vector3.Zero,
        };
        if (a.Style == ReloadStyle.BreakOpen)
        {
            Box(g, c, off + new Vector3(-0.012f, 0, 0), hand.HandWorld, a.CarryColor);
            Box(g, c, off + new Vector3(0.012f, 0, 0), hand.HandWorld, a.CarryColor);
        }
        else
            Box(g, c, off, hand.HandWorld, a.CarryColor);
    }
}
