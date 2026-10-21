using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The size of a push cart in the world, read off its built model (gun space: -Z forward, +Y up): its footprint, where
/// the push bar is, and how far in front of the player's feet the cart's centre rolls.
/// </summary>
public readonly record struct CartShape(Vector3 Half, Vector3 ModelGround, float PushDist, float BarHeight)
{
    /// <summary>Gap between the player's feet and the push bar (where you stand to push).</summary>
    public const float PlayerGap = 0.4f;

    /// <summary>The shape of a cart build's model (its flattened body: the boxes' corners and the trigger hand on the push bar).</summary>
    public static CartShape From(GunMesh m)
    {
        var mn = new Vector3(float.MaxValue);
        var mx = new Vector3(float.MinValue);
        foreach (var w in m.Matrices)
            for (var i = 0; i < 8; i++)
            {
                var p = Vector3.Transform(new Vector3((i & 1) - 0.5f, ((i >> 1) & 1) - 0.5f, ((i >> 2) & 1) - 0.5f), w);
                mn = Vector3.Min(mn, p);
                mx = Vector3.Max(mx, p);
            }
        if (mn.X > mx.X)
            (mn, mx) = (new Vector3(-0.3f, -0.95f, -1f), new Vector3(0.3f, 0.05f, 0.1f));
        var half = Vector3.Max((mx - mn) * 0.5f, new Vector3(0.15f, 0.2f, 0.2f));
        var ground = new Vector3((mn.X + mx.X) * 0.5f, mn.Y, (mn.Z + mx.Z) * 0.5f);
        // The bar sits behind the centre (+Z is back in gun space): the feet stand a step behind the bar.
        var push = MathF.Max(0.3f, m.Grip.Z - ground.Z) + PlayerGap;
        return new CartShape(half, ground, push, m.Grip.Y - mn.Y);
    }

    /// <summary>The world matrix that puts the cart's model on the ground at <paramref name="pos"/>, facing <paramref name="yaw"/>.</summary>
    public Matrix4x4 World(Vector3 pos, float yaw) =>
        Matrix4x4.CreateTranslation(-ModelGround) * Matrix4x4.CreateRotationY(-yaw) * Matrix4x4.CreateTranslation(pos);
}

/// <summary>A push cart's physical state (server-owned; replicated in <see cref="Msg.CartState"/>).</summary>
public struct CartBody
{
    /// <summary>Centre of its footprint, on the ground.</summary>
    public Vector3 Position;

    public float Yaw;

    /// <summary>Metres rolled so far (spins the wheels).</summary>
    public float Rolled;

    /// <summary>The last step couldn't go all the way (a wall, a step too high, a slope too steep).</summary>
    public bool Blocked;
}

/// <summary>
/// Push cart physics (docs/zombies/WEAPON-BUILDER.md, "The push cart"). Deterministic and server-authoritative: the cart
/// rolls toward a point in front of the player, turns slower than the player can, collides with the world as a box,
/// can't climb steps above <see cref="StepLimit"/> or slopes steeper than <see cref="MaxSlopeDeg"/>, and when it's
/// stuck the player is held at the push bar (you can't walk through your own cart). Carts are slow to push because a
/// cart build's mobility is low (the frame's 0.65 times the weight penalty).
/// </summary>
public static class CartPhysics
{
    /// <summary>Highest step a cart's wheels roll up (players step much higher).</summary>
    public const float StepLimit = 0.15f;

    /// <summary>Steepest slope a cart rolls up (front wheels against back wheels).</summary>
    public const float MaxSlopeDeg = 22f;

    /// <summary>How fast a cart turns to follow you (rad/s): it's a cart.</summary>
    public const float TurnRate = 2.2f;

    /// <summary>Fastest a cart rolls (m/s).</summary>
    public const float MaxSpeed = 7f;

    /// <summary>How far the player may stray from the push bar before the cart holds them.</summary>
    public const float Slack = 0.2f;

    /// <summary>The box clears the ground by this much (curbs under the step limit pass under it).</summary>
    public const float Clearance = StepLimit + 0.02f;

    /// <summary>A new cart in front of <paramref name="feet"/> (on the ground there, or at the feet if that spot is blocked).</summary>
    public static CartBody Place(in CartShape s, Vector3 feet, float yaw, IWorldQuery q)
    {
        var c = new CartBody { Position = feet, Yaw = yaw };
        var at = feet + global::CallOfSlop.World.FlatForward(yaw) * s.PushDist;
        if (Ground(q, at, feet.Y) is { } g && Math.Abs(g - feet.Y) <= StepLimit + 0.05f && !Overlaps(s, at with { Y = g }, yaw, q))
            c.Position = at with { Y = g };
        return c;
    }

    /// <summary>
    /// One tick of pushing: the cart turns toward <paramref name="ownerYaw"/>, rolls toward the spot in front of the
    /// owner's feet (sliding along walls), and returns where the owner's feet may be (held at the bar when it's stuck).
    /// </summary>
    public static Vector3 Step(ref CartBody c, in CartShape s, Vector3 ownerFeet, float ownerYaw, float dt, IWorldQuery q)
    {
        var turn = global::CallOfSlop.World.WrapAngle(ownerYaw - c.Yaw);
        c.Yaw = global::CallOfSlop.World.WrapAngle(c.Yaw + Math.Clamp(turn, -TurnRate * dt, TurnRate * dt));
        var fwd = global::CallOfSlop.World.FlatForward(c.Yaw);
        var want = ownerFeet + fwd * s.PushDist;
        var delta = new Vector3(want.X - c.Position.X, 0, want.Z - c.Position.Z);
        var len = delta.Length();
        var max = MaxSpeed * dt;
        if (len > max)
            delta *= max / len;
        c.Blocked = false;
        if (len > 1e-4f && !TryMove(ref c, s, delta, q))
        {
            c.Blocked = true;
            // Slide along whatever stopped it, one axis at a time.
            if (!TryMove(ref c, s, new Vector3(delta.X, 0, 0), q))
                TryMove(ref c, s, new Vector3(0, 0, delta.Z), q);
        }
        // The owner is held at the bar: no further than the slack from where they'd stand to push.
        var bar = c.Position - fwd * s.PushDist;
        var off = new Vector2(ownerFeet.X - bar.X, ownerFeet.Z - bar.Z);
        if (off.Length() > Slack)
        {
            var o = Vector2.Normalize(off) * Slack;
            ownerFeet = new Vector3(bar.X + o.X, ownerFeet.Y, bar.Z + o.Y);
        }
        return ownerFeet;
    }

    /// <summary>Ground height under <paramref name="p"/>, searching down from a bit above <paramref name="fromY"/>, or null (a drop).</summary>
    public static float? Ground(IWorldQuery q, Vector3 p, float fromY)
    {
        var origin = new Vector3(p.X, fromY + StepLimit + 0.35f, p.Z);
        var d = q.Raycast(origin, -Vector3.UnitY, 4f, QueryFilter.All);
        return d >= 4f ? null : origin.Y - d;
    }

    /// <summary>Would the cart's box at <paramref name="pos"/> (on the ground) overlap the world?</summary>
    public static bool Overlaps(in CartShape s, Vector3 pos, float yaw, IWorldQuery q)
    {
        var halfH = MathF.Max(0.05f, s.Half.Y - Clearance * 0.5f);
        var centre = new Vector3(pos.X, pos.Y + Clearance + halfH, pos.Z);
        return q.OverlapBox(Obb.FromYaw(centre, new Vector3(s.Half.X, halfH, s.Half.Z), yaw), QueryFilter.All);
    }

    private static bool TryMove(ref CartBody c, in CartShape s, Vector3 delta, IWorldQuery q)
    {
        if (delta.LengthSquared() < 1e-10f)
            return false;
        var np = c.Position + delta;
        var fwd = global::CallOfSlop.World.FlatForward(c.Yaw);
        var front = Ground(q, np + fwd * (s.Half.Z * 0.85f), c.Position.Y);
        var back = Ground(q, np - fwd * (s.Half.Z * 0.85f), c.Position.Y);
        if (front is not { } gf || back is not { } gb)
            return false; // a drop off an edge: carts stay on the ground
        var top = MathF.Max(gf, gb);
        if (top - c.Position.Y > StepLimit)
            return false; // a step too high for its wheels
        if (MathF.Atan2(MathF.Abs(gf - gb), s.Half.Z * 1.7f) > MaxSlopeDeg * MathF.PI / 180f)
            return false; // too steep
        np.Y = top;
        if (Overlaps(s, np, c.Yaw, q))
            return false;
        c.Position = np;
        c.Rolled += delta.Length();
        return true;
    }
}
