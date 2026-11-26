using System.Numerics;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>The design check's hollow hull (DesignCheck.cs, <c>--hollow</c>): what its colliders came to, and a person standing in each room.</summary>
public static partial class DesignCheck
{
    private const float PersonRadius = 0.35f, PersonHeight = 1.8f;

    /// <summary>
    /// The hull's colliders (DesignParts.LastHollow), then a capsule a person's size stood on each room's floor at its middle: every collider
    /// it touches is named (none should: the hull is plates round the rooms, and pieces are kept out of them).
    /// </summary>
    private static void Hollow(Run r)
    {
        Console.WriteLine($"[design] hollow hull: {DesignParts.LastHollow}");
        var d = ShipDesign.FromJson(r.Text);
        if (d.Stacked)
        {
            Console.WriteLine("[design] hollow: a stacked design's rooms aren't stood in (its decks are along z)");
            return;
        }
        var clear = 0;
        for (var ri = 0; ri < d.Rooms.Count; ri++)
        {
            var room = d.Rooms[ri];
            var b = d.Box(room);
            var (x, z, floor, ceiling) = ((float)(b[0] + b[1]) / 2, (float)(b[4] + b[5]) / 2, (float)b[2], (float)b[3]);
            var top = MathF.Min(floor + PersonHeight, ceiling - 0.05f);
            var foot = new Vector3(x, floor + PersonRadius + 0.05f, z);
            var head = new Vector3(x, MathF.Max(foot.Y, top - PersonRadius), z);
            var hits = new List<string>();
            foreach (var e in r.Els)
            {
                var back = Quaternion.Conjugate(Rot24.Quat(e.Rot));
                for (var hi = 0; hi < e.Hulls.Length; hi++)
                    for (var s = 0; s <= 8; s++)
                    {
                        var q = Vector3.Transform(Vector3.Lerp(foot, head, s / 8f) - e.Origin, back);
                        if (Depth(e.Hulls[hi], q) < PersonRadius)
                        {
                            hits.Add($"{e.Id} ({e.Name}) hull {hi}");
                            break;
                        }
                    }
            }
            if (hits.Count == 0)
                clear++;
            else
                Console.WriteLine($"[design] hollow: a person in room {ri} ({DesignScene.Name(room)}) at {x:0.0}, {floor:0.0}, {z:0.0} touches {string.Join(", ", hits.Take(6))}{(hits.Count > 6 ? $" and {hits.Count - 6} more" : "")}");
        }
        Console.WriteLine($"[design] hollow: {clear} of {d.Rooms.Count} rooms hold a standing person touching no collider");
        if (clear < d.Rooms.Count)
            r.Fail($"{d.Rooms.Count - clear} rooms where a standing person touches a collider");
        // Each socket's opening: how far the middle of its hole is from the skin's plates (open: past a person's radius), and from any piece's collider.
        foreach (var p in d.Pieces)
        {
            if (PieceKinds.Find(p.Kind)?.Opening is not { } op || d.Hull(p.Hull) is not { } h)
                continue;
            var w = DesignScene.Where(h, p);
            var o = new Vector3((float)w.O[0], (float)w.O[1], (float)w.O[2]);
            float Clear(bool bands)
            {
                var best = float.MaxValue;
                foreach (var e in r.Els)
                    if ((e.Kind == "band") == bands)
                    {
                        var q = Vector3.Transform(o - e.Origin, Quaternion.Conjugate(Rot24.Quat(e.Rot)));
                        foreach (var hl in e.Hulls)
                            best = MathF.Min(best, Depth(hl, q));
                    }
                return best;
            }
            var (skin, pieces) = (Clear(true), Clear(false));
            Console.WriteLine($"[design] hollow: {p.Kind}'s opening ({2 * op.Hx * p.Size * p.Hole:0.00} x {2 * op.Hz * p.Size * p.Hole:0.00} m) at {o.X:0.0}, {o.Y:0.0}, {o.Z:0.0}: its middle {(skin >= PersonRadius ? "is" : "is NOT")} clear of the skin's plates ({skin:0.00} m), {pieces:0.00} m from a piece's collider{(pieces < 0 ? " (inside one: a door's work, package E)" : "")}");
            if (skin < PersonRadius)
                r.Fail($"{p.Kind}'s opening is plated over");
        }
    }

    /// <summary>How far outside a hull a point is along its faces' planes (a lower bound of its distance; at most 0 inside).</summary>
    private static float Depth(ConvexHull h, Vector3 p)
    {
        var worst = float.MinValue;
        for (var f = 0; f + 1 < h.FaceStart.Length; f++)
            worst = MathF.Max(worst, Vector3.Dot(h.N[f], p - h.V[h.FaceIdx[h.FaceStart[f]]]));
        return worst;
    }
}
