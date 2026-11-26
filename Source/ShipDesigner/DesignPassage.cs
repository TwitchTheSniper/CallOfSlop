namespace CallOfSlop.ShipDesigner;

/// <summary>
/// Passages between bodies (Karl, 2026-11-26): a lift or a walkway from a room in one body to a room in another, through the arm that
/// joins them. Its line runs from a door in the first room's wall, along the arm's spine (its Bézier middle line, inside the strut), to
/// a door in the second's; the clear room inside the arm is its narrowest section less the walls.
/// </summary>
public static partial class DesignScene
{
    /// <summary>The walls' allowance inside an arm (m each side), and the door's middle over the floor.</summary>
    public const double ArmWall = 0.08, PassageDoorMid = 1.05;

    public sealed record PassageLay(HullJoin Join, List<double[]> Pts, List<double[]> Ups, double Clear, double[] DoorA, double[] DoorB);

    /// <summary>The arm between two rooms' bodies (the passage's own when it names one): null with none.</summary>
    public static HullJoin? PassageArm(ShipDesign d, Room a, Room b, int join = 0)
    {
        var (ha, hb) = (HomeBody(d, a), HomeBody(d, b));
        if (ha is null || hb is null || ha == hb)
            return null;
        bool Between(HullJoin j) => (j.A.Hull == ha.Id && j.B.Hull == hb.Id) || (j.A.Hull == hb.Id && j.B.Hull == ha.Id);
        if (join != 0 && d.JoinById(join) is { } named && Between(named))
            return named;
        // The nearest of the arms between them (a pod can hang off two).
        var mid = Mid(d, a);
        return d.Joins.Where(Between).MinBy(j => j.Spine(d) is { Count: > 0 } s ? s.Min(f => Dist(f.C, mid)) : double.MaxValue);
    }

    /// <summary>Lays a passage: why it can't be laid ("" when it can), and its line.</summary>
    public static (string Why, PassageLay? Lay) LayPassage(ShipDesign d, Passage k)
    {
        var (ra, rb) = (d.RoomById(k.A), d.RoomById(k.B));
        if (ra is null || rb is null)
            return ("its room is gone", null);
        var (ha, hb) = (HomeBody(d, ra), HomeBody(d, rb));
        if (ha == hb)
            return ($"{Name(ra)} and {Name(rb)} are in the same body: a door, a ladder or a lift joins them", null);
        var j = PassageArm(d, ra, rb, k.Join);
        if (j is null)
            return ($"no arm joins {ha?.Name ?? "?"} and {hb?.Name ?? "?"}: connect the bodies first (Hull tab, Connect)", null);
        var spine = j.Spine(d);
        if (spine.Count < 2)
            return ("the arm has no spine", null);
        // A mirrored arm's spine holds both copies, one after the other: the one nearer the rooms.
        var chains = new List<List<(double[] C, double[] S, double[] U, double W, double H)>>();
        var half = spine.Count / 2;
        if (j.Mirror && spine.Count % 2 == 0 && half >= 2 && Math.Abs(spine[0].C[0] + spine[half].C[0]) < 1e-6 && Math.Abs(spine[0].C[0]) > 1e-3)
            chains.AddRange([spine.GetRange(0, half), spine.GetRange(half, half)]);
        else
            chains.Add(spine);
        var (ma, mb) = (Mid(d, ra), Mid(d, rb));
        double Cost(List<(double[] C, double[] S, double[] U, double W, double H)> c) => Math.Min(Dist(c[0].C, ma) + Dist(c[^1].C, mb), Dist(c[^1].C, ma) + Dist(c[0].C, mb));
        var ch = new List<(double[] C, double[] S, double[] U, double W, double H)>(chains.MinBy(Cost)!);
        if (Dist(ch[^1].C, ma) + Dist(ch[0].C, mb) < Dist(ch[0].C, ma) + Dist(ch[^1].C, mb))
            ch.Reverse();
        var clear = ch.Min(f => Math.Min(f.W, f.H * (1 - Math.Abs(j.Bias)))) - 2 * ArmWall;
        var (da, db) = (DoorOnWall(d, ra, ch[0].C), DoorOnWall(d, rb, ch[^1].C));
        double[] up = d.Stacked ? [0, 0, -1] : [0, 1, 0];
        double[] Lift(double[] p) => [p[0] + up[0] * PassageDoorMid, p[1] + up[1] * PassageDoorMid, p[2] + up[2] * PassageDoorMid];
        var pts = new List<double[]> { Lift(da) };
        var ups = new List<double[]> { up };
        // Its line in the arm: the spine's middle, the ends sunk under the skins left out where they are behind the doors.
        foreach (var f in ch)
        {
            pts.Add(f.C);
            ups.Add(f.U);
        }
        pts.Add(Lift(db));
        ups.Add(up);
        return ("", new PassageLay(j, pts, ups, clear, da, db));
    }

    /// <summary>A room's door towards a point: on the wall of its plan nearest it, kept 55 cm off the corners, at its floor.</summary>
    private static double[] DoorOnWall(ShipDesign d, Room r, double[] toward)
    {
        var dk = d.DeckOf(r);
        double pu = toward[0], pv = d.Stacked ? toward[1] : toward[2];
        double u = Math.Clamp(pu, r.X0, r.X1), v = Math.Clamp(pv, r.Z0, r.Z1);
        // Inside its plan (an arm leaving through the ceiling or the floor): the nearest wall.
        if (u > r.X0 && u < r.X1 && v > r.Z0 && v < r.Z1)
        {
            var e = new[] { (u - r.X0, 0), (r.X1 - u, 1), (v - r.Z0, 2), (r.Z1 - v, 3) }.MinBy(q => q.Item1).Item2;
            if (e == 0) u = r.X0; else if (e == 1) u = r.X1; else if (e == 2) v = r.Z0; else v = r.Z1;
        }
        const double c = 0.55;
        if (u <= r.X0 || u >= r.X1)
            v = r.Z1 - r.Z0 > 2 * c ? Math.Clamp(v, r.Z0 + c, r.Z1 - c) : (r.Z0 + r.Z1) / 2;
        else
            u = r.X1 - r.X0 > 2 * c ? Math.Clamp(u, r.X0 + c, r.X1 - c) : (r.X0 + r.X1) / 2;
        return d.World(dk, u, v, 0);
    }

    private static double[] Mid(ShipDesign d, Room r)
    {
        var b = d.Box(r);
        return [(b[0] + b[1]) / 2, (b[2] + b[3]) / 2, (b[4] + b[5]) / 2];
    }

    private static double Dist(double[] a, double[] b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));
}
