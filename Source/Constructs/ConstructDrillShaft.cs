using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The hole a working Drill Module digs under its bit (docs/sandbox/CONSTRUCTS.md "Gathering"). The hole itself is the only
/// state, so a drill moved away stops, one brought back resumes, and a second drill can take over a hole. Each scoop is a
/// <see cref="DigKind.Scoop"/> through <see cref="Game.Dig"/>, one for every <see cref="UnitsPerScoop"/> units tapped. The
/// scoops of a hole are a fixed list, worked out from the ground as built and how deep the floor is now, so a hole costs
/// at most <see cref="Scoops"/> ops however long its drill runs, and the hole ends <see cref="Depth"/> under the surface.
/// <para>Each scoop is a little lower and a little narrower than the one before, with R(k)^2 = R(k-1)^2 - Step^2: at the rim
/// of a scoop the one before it is still at least Step tall, so the two always meet and no shelf of ground stays over the
/// hole. The result is a funnel with steps a player can climb (at most <see cref="World.MantleReach"/>), not a trap.</para>
/// </summary>
public static class DrillShaft
{
    public const float Depth = 3f; // how far under the surface as built the hole ends
    public const float Radius = 1.4f; // the first scoop's radius
    public const float Lift = 0.9f; // the first scoop's centre above the surface, so it starts shallow
    public const float Step = 0.3f; // how much lower each scoop is
    public const float Reach = Depth + 0.75f; // how far over a hole's floor a bit still works (the last scoop goes a little past Depth, and a bit may sit a little over the rim)
    public const int Scoops = 12; // the most scoops a hole can take
    public const int UnitsPerScoop = 60; // units tapped for each scoop (6 s at a drill's 10 units a second)
    public const int MaxScoopsPerStep = 1; // across the world, at 10 Hz: however many drills run, the ops stay slow
    public const int MaxLog = TerrainEdits.MaxOps / 2; // drills stop digging here, so blasts always have room in the log

    /// <summary>The surface at a point as the map built it: what a hole's depth is measured from.</summary>
    public static float Surface(TerrainData t, float x, float z) =>
        t.EditsIfMade is { } e && e.TryBuiltTop(x, z, out var top) ? top : t.BaseGroundTop(x, z);

    /// <summary>
    /// Does a bit (its work box's lowest point <paramref name="bottom"/>, over <paramref name="at"/>) reach the ground? It does
    /// within <see cref="ConstructGatherer.GroundSlack"/> of the ground, and also over a hole: not above the surface as built,
    /// with the floor no more than <see cref="Reach"/> below it, so digging never moves the ground out of reach.
    /// </summary>
    public static bool Touches(ResourceField field, Vector3 at, float bottom)
    {
        const float slack = ConstructGatherer.GroundSlack;
        if (field.Terrain is not { } t)
            return bottom <= field.Ground(at.X, at.Z) + slack;
        var floor = t.GroundBelow(at.X, at.Z, bottom + slack);
        return bottom <= floor + slack || bottom <= Surface(t, at.X, at.Z) + slack && bottom - floor <= Reach;
    }

    /// <summary>Scoop k's centre height above the surface and its radius.</summary>
    private static void Scoop(int k, out float lift, out float radius)
    {
        lift = Lift - k * Step;
        radius = MathF.Sqrt(Radius * Radius - k * Step * Step);
    }

    /// <summary>The next scoop of the hole at (x, z), on the column grid (a body's small moves ask for the same scoop); false when it's deep enough.</summary>
    public static bool Next(TerrainData t, float x, float z, out DigOp op)
    {
        op = default;
        var e = t.Edits;
        e.ColumnOf(x, z, out var ci, out var cj);
        var cx = t.OriginX + (ci + 0.5f) * e.Size;
        var cz = t.OriginZ + (cj + 0.5f) * e.Size;
        var surface = Surface(t, cx, cz);
        var floor = t.GroundBelow(cx, cz, surface + 0.01f);
        if (floor <= surface - Depth + 0.01f)
            return false;
        for (var k = 0; k < Scoops; k++)
        {
            Scoop(k, out var lift, out var r);
            if (surface + lift - r < floor - 0.05f)
            {
                op = new DigOp(DigKind.Scoop, new Vector3(cx, surface + lift, cz), r).Quantised;
                return true;
            }
        }
        return false;
    }

    /// <summary>Server: a drill tapped <paramref name="units"/>: every <see cref="UnitsPerScoop"/> of them dig the next scoop.</summary>
    public static void Dig(ConstructWorld world, ResourceField field, CStore st, Vector3 at, int units)
    {
        if (world.DrillDig is not { } dig || field.Terrain is not { } t)
            return;
        st.JobAmount = Math.Min(st.JobAmount + units, UnitsPerScoop);
        if (st.JobAmount < UnitsPerScoop || world.ScoopsThisStep >= MaxScoopsPerStep || (t.EditsIfMade?.Log.Count ?? 0) >= MaxLog)
            return;
        st.JobAmount = 0;
        if (!Next(t, at.X, at.Z, out var op))
            return;
        world.ScoopsThisStep++;
        dig(op);
    }
}
