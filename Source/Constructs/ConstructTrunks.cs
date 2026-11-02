using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A construct against the generated trees (ResourceTrunks.cs). A hull's points probe the world with rays, and a trunk is
/// far thinner than the gap between a hull's corners, so a tree could stand in the middle of a bumper unseen: here it is
/// the other way round, each trunk near a moving body meets the body's part boxes (wheels included). The lattice cells
/// under the body's sphere are a handful, an empty one costs a lookup, and only boxes beside a trunk are looked at.
/// </summary>
public static partial class ConstructPhysics
{
    private const float TrunkFriction = 0.6f, TrunkSkin = 0.02f;

    private static void TrunkContacts(Construct c, short index, ResourceField field, float dt)
    {
        var com = c.WorldCom;
        var reach = c.Radius + c.Vel.Length() * dt + ResourceField.MaxTrunkRadius;
        for (var iz = ResourceField.Lattice(com.Z - reach); iz <= ResourceField.Lattice(com.Z + reach); iz++)
            for (var ix = ResourceField.Lattice(com.X - reach); ix <= ResourceField.Lattice(com.X + reach); ix++)
            {
                if (!field.TrunkAt(ix, iz, out var t))
                    continue;
                float dx = t.X - com.X, dz = t.Z - com.Z;
                if (dx * dx + dz * dz > reach * reach)
                    continue;
                field.Span(t, out var bottom, out var top);
                if (com.Y - reach > top || com.Y + reach < bottom)
                    continue;
                if (!TrunkAgainst(c, index, t, bottom, top, dt))
                    return;
            }
    }

    private static bool TrunkAgainst(Construct c, short index, in Trunk t, float bottom, float top, float dt)
    {
        var com = c.WorldCom;
        var dyn = _dyn[index];
        for (var pi = 0; pi < c.PartCount; pi++)
        {
            var seg = c.Parts[pi].Seg;
            var moves = c.MoveSeg(seg) != 0;
            if (!dyn && !moves)
                continue; // (an anchored base: only what its joints swing)
            c.WorldFrame(seg, out var q, out var tr);
            var boxes = c.SolidCount(pi);
            for (var bi = 0; bi < boxes; bi++)
            {
                if (_contactCount >= MaxContacts - 1)
                    return false;
                c.SolidBox(pi, bi, out var centre, out var half);
                var mid = tr + Vector3.Transform(centre, q);
                var near = half.Length() + t.Radius + 1f;
                float fx = mid.X - t.X, fz = mid.Z - t.Z;
                if (fx * fx + fz * fz > near * near)
                    continue;
                // The trunk's axis beside this box, in the box's frame: the box grown by the trunk's radius.
                var s = new Vector3(t.X, Math.Clamp(mid.Y, bottom, top), t.Z);
                var invQ = Quaternion.Conjugate(q);
                var local = Vector3.Transform(s - tr, invQ) - centre;
                var grown = half + new Vector3(t.Radius);
                var un = 0;
                var v = Vector3.Zero;
                if (moves)
                {
                    un = Chain(c, index, seg, s, out v);
                    if (un < 0)
                        return false;
                }
                if (dyn)
                    v += c.Vel + Vector3.Cross(c.AngVel, s - com);
                if (!TrunkTouch(grown, local, Vector3.Transform(-v, invQ), q, dt, out var outward, out var gap))
                    continue;
                var n = -outward; // (the box's face looks at the trunk: the body goes the other way)
                if (Add(index, -1, s + n * t.Radius, n, gap, TrunkFriction, moves ? _chainCount : 0, un) && moves)
                    _chainCount += un;
            }
        }
        return true;
    }

    /// <summary>
    /// A point (the trunk's axis) against a grown box it moves through at <paramref name="relative"/> in the box's frame: the
    /// box's upright face it is in or will reach this tick, as a world direction, and the gap to it (below 0: inside).
    /// </summary>
    private static bool TrunkTouch(Vector3 grown, Vector3 local, Vector3 relative, Quaternion q, float dt, out Vector3 outward, out float gap)
    {
        outward = default;
        gap = 0;
        var depth = grown - Vector3.Abs(local);
        if (depth.X > 0 && depth.Y > 0 && depth.Z > 0)
        {
            // Inside: the shallowest way out that isn't up or down (a trunk stands, it doesn't lift a body).
            var best = float.MaxValue;
            for (var axis = 0; axis < 3; axis++)
            {
                var along = axis == 0 ? new Vector3(MathF.Sign(local.X) is var sx && sx != 0 ? sx : 1, 0, 0)
                    : axis == 1 ? new Vector3(0, MathF.Sign(local.Y) is var sy && sy != 0 ? sy : 1, 0)
                    : new Vector3(0, 0, MathF.Sign(local.Z) is var sz && sz != 0 ? sz : 1);
                var world = Vector3.Transform(along, q);
                var d = axis == 0 ? depth.X : axis == 1 ? depth.Y : depth.Z;
                if (MathF.Abs(world.Y) < 0.7f && d < best)
                    (best, outward) = (d, world);
            }
            gap = -best;
            return best < float.MaxValue;
        }
        var speed = relative.Length();
        if (speed * dt < 1e-4f)
            return false;
        var dir = relative / speed;
        var travel = speed * dt + TrunkSkin;
        if (Geometry.RayAabb(-grown, grown, local, dir, travel) is not { } at)
            return false;
        var normal = Geometry.BoxEntryNormal(-grown, grown, local, dir, at);
        outward = Vector3.Transform(normal, q);
        if (MathF.Abs(outward.Y) >= 0.7f)
            return false;
        gap = at * MathF.Max(0.1f, -Vector3.Dot(normal, dir));
        return true;
    }

    /// <summary>Would a trunk here go through <paramref name="c"/>? (A tree growing back waits for what stands on its stump.)</summary>
    internal static bool TouchesTrunk(Construct c, in Trunk t, float ground)
    {
        var top = ground + t.Height * Trunk.HeightShare;
        for (var pi = 0; pi < c.PartCount; pi++)
        {
            c.WorldFrame(c.Parts[pi].Seg, out var q, out var tr);
            var boxes = c.SolidCount(pi);
            for (var bi = 0; bi < boxes; bi++)
            {
                c.SolidBox(pi, bi, out var centre, out var half);
                var s = new Vector3(t.X, Math.Clamp((tr + Vector3.Transform(centre, q)).Y, ground, top), t.Z);
                var d = Vector3.Abs(Vector3.Transform(s - tr, Quaternion.Conjugate(q)) - centre) - half - new Vector3(t.Radius);
                if (d.X < 0 && d.Y < 0 && d.Z < 0)
                    return true;
            }
        }
        return false;
    }
}
