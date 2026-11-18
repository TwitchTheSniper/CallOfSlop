using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// Ships out of the bubble meet (Karl 2026-11-18: the Zero Day's Shuttle flew through other ships). A ship past the sky's edge
/// is posed by its flight, not stepped, so the physics' step never pairs two of them and they passed through each other.
/// After every ship is posed (<see cref="SpaceLaunchAfter"/>) each pair whose bounds meet is asked for its pieces' contacts
/// (<see cref="ConstructPhysics.ShipContact"/>), and the two flights' states are changed (<see cref="SpaceShips.SetState"/>),
/// never the bodies' velocities alone (the next tick's pose would undo them): the closing speed along the contact goes,
/// shared by mass with a small bounce and some friction, and an overlap is pushed apart a little each tick. Their spin is
/// left as it is. Host only; nothing new goes over the wire.
/// </summary>
public sealed partial class Game
{
    /// <summary>What two ships meeting in space keep of their closing speed, their friction, the overlap left alone (m), and the most an overlap is pushed apart in a tick (m).</summary>
    public const float SpaceBumpBounce = 0.1f, SpaceBumpFriction = 0.3f, SpaceBumpSlop = 0.01f, SpaceBumpPushMost = 0.25f;

    /// <summary>Host: the ships posed this tick (SpaceLaunchAfter), paired once they all are.</summary>
    private readonly List<Construct> _spacePosed = new(SpaceShipCapacity);

    /// <summary>Host, once every ship is posed this tick: each pair of ships that touch is kept apart.</summary>
    private void SpaceCollide(SpaceFlight f, in FramePose pose, float dt)
    {
        var list = _spacePosed;
        for (var x = 0; x < list.Count; x++)
        {
            var a = list[x];
            for (var y = x + 1; y < list.Count; y++)
            {
                var b = list[y];
                var reach = a.Radius + b.Radius + (a.Vel - b.Vel).Length() * dt + 1f;
                if (Vector3.DistanceSquared(a.WorldCom, b.WorldCom) > reach * reach)
                    continue;
                if (Constructs.CouplingCount > 0 && Constructs.Coupled(a.Id, b.Id))
                    continue;
                if (a.Dirty)
                    a.Rebuild();
                if (b.Dirty)
                    b.Rebuild();
                if (ConstructPhysics.ShipContact(Constructs, a, b, dt, out var n, out var gap, out _) > 0)
                    SpaceBump(f, pose, a, b, n, gap, dt);
            }
        }
        list.Clear();
    }

    /// <summary>Host: ships <paramref name="a"/> and <paramref name="b"/> meet along <paramref name="n"/> (from b into a), <paramref name="gap"/> apart (below 0: overlapping).</summary>
    private static void SpaceBump(SpaceFlight f, in FramePose pose, Construct a, Construct b, Vector3 n, float gap, float dt)
    {
        var ia = a.Mass > 0 ? 1f / a.Mass : 0f;
        var ib = b.Mass > 0 ? 1f / b.Mass : 0f;
        var sum = ia + ib;
        if (sum <= 0)
            return;
        var rel = a.Vel - b.Vel;
        var vn = Vector3.Dot(rel, n);
        // Not yet touching: it may close the gap this tick and no more. Touching: the closing speed goes, bar a little bounce.
        var target = gap > 0 ? -gap / dt : vn < 0 ? -vn * SpaceBumpBounce : vn;
        var dvn = MathF.Max(0, target - vn);
        var push = gap < -SpaceBumpSlop ? MathF.Min(-gap - SpaceBumpSlop, SpaceBumpPushMost) : 0f;
        if (dvn <= 0 && push <= 0)
            return;
        var j = dvn / sum;
        var dv = n * j;
        var vt = rel - n * vn;
        var vtl = vt.Length();
        if (j > 0 && vtl > 1e-4f)
            dv -= vt / vtl * MathF.Min(vtl / sum, SpaceBumpFriction * j);
        SpaceBumpMove(f, pose, a, dv * ia, n * (push * ia / sum));
        SpaceBumpMove(f, pose, b, -dv * ib, -n * (push * ib / sum));
    }

    /// <summary>Host, a window tool's verb ("ram"): ship <paramref name="c"/> in space moved and sped as <see cref="SpaceBumpMove"/> does.</summary>
    internal string SpaceShove(Construct c, Vector3 dp, Vector3 dv)
    {
        if (Space?.Flight is not { } f || f.Ships.IndexOfId(c.SpaceShip) < 0)
            return "is not a ship in space";
        SpaceBumpMove(f, Space.Bubble.PoseAt(f.Ships.Time), c, dv, dp);
        return "shoved";
    }

    /// <summary>Host: a ship's flight and its posed body moved by <paramref name="dp"/> and sped by <paramref name="dv"/> (game axes).</summary>
    private static void SpaceBumpMove(SpaceFlight f, in FramePose pose, Construct c, Vector3 dv, Vector3 dp)
    {
        if (dv == Vector3.Zero && dp == Vector3.Zero)
            return;
        var i = f.Ships.IndexOfId(c.SpaceShip);
        if (i < 0)
            return;
        f.Ships.RelativeOf(i, out var primary, out var r, out var v);
        f.Ships.SetState(i, primary, r + pose.Rot.Rotate(Vec3d.From(dp)), v + pose.Rot.Rotate(Vec3d.From(dv)));
        c.Pos += dp;
        c.Vel += dv;
    }
}
