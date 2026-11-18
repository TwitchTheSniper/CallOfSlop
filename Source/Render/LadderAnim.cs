using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A player on a vertical ladder (ConstructRideLadder.cs), drawn facing it with hands and feet on its rungs: hand over hand
/// and foot over foot, each limb gripping a rung while the body rises past it, then reaching for the one two rungs up (or
/// down). The pose comes from where the feet are on the ladder alone (<see cref="Player.RenderLadderFeet"/>), so every
/// machine draws the same climb for the same position, a remote climber as smoothly as their snapshots, and nothing is
/// kept per player. Stepping on and off, the limbs ease between the rungs and a standing pose (<see cref="Weight"/>).
/// </summary>
public static class LadderAnim
{
    /// <summary>The body's frame (feet at the origin, -Z facing) while on a ladder: facing it, upright in its frame.</summary>
    public static Matrix4x4 BodyFrame(Player p)
    {
        var r = p.RenderLadderFrame;
        r.Translation = Vector3.Zero;
        return Matrix4x4.CreateRotationY(MathF.PI) * r * Matrix4x4.CreateTranslation(p.RenderPosition);
    }

    /// <summary>The body's rotation only (<see cref="BodyFrame"/> without its position).</summary>
    public static Matrix4x4 BodyRot(Player p)
    {
        var r = p.RenderLadderFrame;
        r.Translation = Vector3.Zero;
        return Matrix4x4.CreateRotationY(MathF.PI) * r;
    }

    /// <summary>How much the limbs are on the rungs: 1 on the ladder, easing to 0 a step away from it (stepping on or off).</summary>
    public static float Weight(Player p)
    {
        if (!p.RenderLadder)
            return 0;
        var g = p.RenderLadderGeom;
        var f = p.RenderLadderFeet;
        var off = MathF.Max(MathF.Abs(f.Z - (g.FrontZ - ConstructRide.LadderBack)), MathF.Abs(f.X - g.Cx) * 0.7f);
        var w = 1f - Math.Clamp((off - 0.05f) / 0.4f, 0f, 1f);
        if (f.Y < g.Base - 0.3f || f.Y > g.Top + 0.5f)
            w = 0;
        return w * w * (3 - 2 * w);
    }

    /// <summary>A ladder point (its own frame) in the climber's body space (feet at the origin, -Z facing the ladder).</summary>
    private static Vector3 ToBody(Player p, Vector3 l)
    {
        var d = l - p.RenderLadderFeet;
        return new Vector3(-d.X, d.Y, -d.Z);
    }

    private static float Smooth(float x) => x * x * (3 - 2 * x);

    /// <summary>
    /// The rung a limb grips, or between two as it moves up to the next (ladder frame height above the foot), and how far it
    /// is through that move (0: gripping). <paramref name="phase"/> staggers the four limbs; <paramref name="offset"/> picks
    /// which rungs (every other one) the limb takes.
    /// </summary>
    private static float Hold(float h, float phase, float offset, out float lift)
    {
        const float cycle = LadderGeom.Rung * 2; // (each limb takes every other rung)
        var u = h / cycle + phase;
        var k = MathF.Floor(u);
        var fr = u - k;
        var y = cycle * k + offset; // (a rung: a whole number of rungs above the foot)
        lift = 0;
        if (fr < 0.7f)
            return y;
        var s = (fr - 0.7f) / 0.3f;
        lift = MathF.Sin(MathF.PI * s);
        return y + cycle * Smooth(s);
    }

    /// <summary>Both feet's targets on the rungs (body space, ankles), blended to a standing pose by <see cref="Weight"/>; index 0 the right.</summary>
    public static void Feet(Player p, float w, Span<Vector3> feet)
    {
        var g = p.RenderLadderGeom;
        var h = p.RenderLadderFeet.Y - g.Base;
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? 1f : -1f;
            // Foot i grips rung 2k - i: from 0.42 m above the feet to level with them, then steps two rungs on.
            var y = Hold(h, 0.7f + 0.5f * i, -LadderGeom.Rung * i, out var lift);
            y = Math.Clamp(y, 0f, g.Top - g.Base); // (the floor at the foot, the landing at the top)
            var rung = new Vector3(g.Cx - side * 0.15f, g.Base + y + LegAnimator.AnkleHeight, g.RungZ - 0.07f - 0.16f * lift);
            var stand = new Vector3(side * 0.15f, LegAnimator.AnkleHeight, 0.02f);
            feet[i] = Vector3.Lerp(stand, ToBody(p, rung), w);
        }
    }

    /// <summary>Both hands on the rungs (world, as <see cref="ThirdPersonArms.PoseHands"/> takes them), blended to the chest by <see cref="Weight"/>.</summary>
    public static void Hands(Player p, float w, in Matrix4x4 body, out HandTarget right, out HandTarget left)
    {
        var g = p.RenderLadderGeom;
        var h = p.RenderLadderFeet.Y - g.Base;
        var rot = ThirdPersonArms.RotationOf(body);
        right = left = default;
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? 1f : -1f;
            // Hand i grips rung 2k + 6 - i: from 1.8 m above the feet to shoulder height, then reaches two rungs up.
            var y = Hold(h, 0.5f * i, LadderGeom.Rung * (6 - i), out var lift);
            y = Math.Clamp(y, LadderGeom.Rung, g.RailTop - g.Base - 0.08f);
            var rung = ToBody(p, new Vector3(g.Cx - side * 0.2f, g.Base + y, g.RungZ + 0.01f));
            rung.Z += 0.12f * lift; // (reaching: the hand comes off the rung towards the body)
            var rest = new Vector3(side * 0.22f, 1.05f, -0.28f);
            var at = Vector3.Transform(Vector3.Lerp(rest, rung, w), body);
            var grip = Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -side * MathF.PI / 2), rot); // (the grip along the rung)
            var t = new HandTarget(at, grip, HandShape.Wrap);
            if (i == 0)
                right = t;
            else
                left = t;
        }
    }
}
