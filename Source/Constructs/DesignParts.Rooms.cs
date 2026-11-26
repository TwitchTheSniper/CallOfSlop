using System.Numerics;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>
/// Design parts, the rooms (DesignParts.cs has the map of the files): walkable rooms (step 4, Karl 2026-11-26). Each room is the designer's own
/// shell (<see cref="DesignScene.RoomShell"/>): its floor, ceiling and walls, the doorways the designer cut in them (a door between rooms, a
/// shaft's or a hatch's hole, a socket's way in: <see cref="DesignScene.Check.Doors"/>), drawn as the designer draws them and colliding as the
/// very boxes it draws (<see cref="Element.Boxes"/>, merged where two meet whole), nothing laid out a second time.
/// <list type="bullet">
/// <item>A box the clip to the skin (<see cref="DesignScene.ClipToHulls"/>) bends is a thin convex hull of its bent corners instead, in an element
/// of its own (a part's hulls stand for all its boxes). A room out to the hull (<see cref="Room.ToHull"/>) is its hull shell's strips, each such
/// a hull; the strips along its floor are what is stood on.</item>
/// <item>A room with no shell (WallThick 0) keeps a floor slab under its box, as before.</item>
/// <item>Its mass is its floor's area at <see cref="DeckKgPerM2"/>, shared among its pieces.</item>
/// <item>Extensions (<see cref="Room.Exts"/>) have no wall yet: each is logged.</item>
/// </list>
/// </summary>
public static partial class DesignParts
{
    public const int RoomBoxesPerPart = 240; // a room element's boxes (ConstructParts.MaxBoxes is 256): past it the rest go in another element
    public const int RoomHullsPerPart = 96; // a room element's thin hulls (bent boxes, hull-shell strips)
    public const float BentBy = 0.001f; // a box's corner moved by more than this by the clip to the skin: it is made a hull
    private const int ClipSlice = 3000; // a room's mesh corners clipped to the skin a step (a few ms), and its boxes and strips BentSlice a step
    private const int BentSlice = 64;

    /// <summary>The rooms: each its shell's elements, the first welded to the band of its deck's body there, the rest to it.</summary>
    private static IEnumerable<int> AddRooms(Steps st)
    {
        var (d, els) = (st.D, st.Els);
        if (d.Rooms.Count == 0)
            yield break;
        var check = DesignScene.Run(d); // (the doorways, as the designer has them)
        yield return els.Count;
        for (var ri = 0; ri < d.Rooms.Count; ri++)
        {
            var r = d.Rooms[ri];
            var bx = d.Box(r);
            var (x0, x1, y0, y1, z0, z1) = ((float)bx[0], (float)bx[1], (float)bx[2], (float)bx[3], (float)bx[4], (float)bx[5]);
            var area = d.Stacked ? (x1 - x0) * (y1 - y0) : (x1 - x0) * (z1 - z0);
            if (area <= 0.01f)
                continue;
            var name = DesignScene.Name(r);
            if (r.Exts.Count > 0)
                Console.WriteLine($"[design] {name} on deck {r.Deck + 1}: its {r.Exts.Count} extension(s) have no wall to walk on yet (drawn only in the designer)");
            var boxes = new List<(double[] Lo, double[] Hi)>();
            var strips = new List<double[]>();
            var shell = DesignScene.RoomShell(d, r, check.Doors, boxes, strips);
            yield return els.Count; // (the shell, then its clip a slice of corners a step, its boxes and strips a slice a step, the merge: each a step)
            // (a corner shared by several triangles, boxes or strips is pulled in once: one memo for the shell's clip, one for the interior's)
            var memo = new Dictionary<(double, double, double), (double, double, double)>();
            for (var v = 0; v < shell.Tris * 3; v += ClipSlice)
            {
                DesignScene.ClipToHulls(d, shell, DesignScene.ShellInset, r, r.WallThick, v, v + ClipSlice, memo);
                yield return els.Count;
            }
            var look = new LookBuilder();
            for (var tr = 0; tr < shell.Tris; tr++)
                look.AddTri(shell, tr, Vector3.One, new Vector3(float.NaN));
            if (r.Interior != "Off")
            {
                var (solid, glow) = DesignScene.RoomInterior(d, r, check.Doors);
                var inMemo = new Dictionary<(double, double, double), (double, double, double)>();
                foreach (var m in new[] { solid, glow })
                    for (var v = 0; v < m.Tris * 3; v += ClipSlice)
                    {
                        DesignScene.ClipToHulls(d, m, DesignScene.InteriorInset, r, 0, v, v + ClipSlice, inMemo);
                        yield return els.Count;
                    }
                for (var tr = 0; tr < solid.Tris; tr++)
                    look.AddTri(solid, tr, Vector3.One, new Vector3(float.NaN));
                for (var tr = 0; tr < glow.Tris; tr++)
                    look.AddTri(glow, tr, Vector3.One, new Vector3(float.NaN));
            }
            // Its colliders: the shell's boxes (merged), those the clip bent as hulls, and the hull shell's strips.
            var solids = new List<ElementBox>();
            var hulls = new List<Vector3[]>();
            for (var bi = 0; bi < boxes.Count; bi++)
            {
                if (bi > 0 && bi % BentSlice == 0)
                    yield return els.Count;
                var (lo, hi) = boxes[bi];
                var (bent, corners) = Bent(d, r, lo, hi, memo);
                if (bent)
                    hulls.Add(corners);
                else
                    solids.Add(new ElementBox(new Vector3((float)lo[0], (float)lo[1], (float)lo[2]), new Vector3((float)hi[0], (float)hi[1], (float)hi[2])));
            }
            for (var si = 0; si < strips.Count; si++)
            {
                if (si % BentSlice == 0)
                    yield return els.Count;
                var s = strips[si];
                var tmp = new DesignMesh();
                tmp.P.AddRange(s);
                DesignScene.ClipToHulls(d, tmp, DesignScene.ShellInset, r, r.WallThick, 0, int.MaxValue, memo);
                var c = new Vector3[8];
                for (var k = 0; k < 8; k++)
                    c[k] = new Vector3((float)tmp.P[k * 3], (float)tmp.P[k * 3 + 1], (float)tmp.P[k * 3 + 2]);
                hulls.Add(c);
            }
            yield return els.Count;
            MergeBoxes(solids); // (a step of its own)
            if (solids.Count == 0 && hulls.Count == 0)
                // No shell: a slab under its box's floor, as before (stood on, its mass).
                solids.Add(d.Stacked ? new ElementBox(new Vector3(x0, y0, z1 - 0.1f), new Vector3(x1, y1, z1)) : new ElementBox(new Vector3(x0, y0 - 0.1f, z0), new Vector3(x1, y0, z1)));
            var pieces = (solids.Count + RoomBoxesPerPart - 1) / RoomBoxesPerPart + (hulls.Count + RoomHullsPerPart - 1) / RoomHullsPerPart;
            var kgEach = MathF.Max(1f, area * DeckKgPerM2 / Math.Max(1, solids.Count + hulls.Count));
            var first = els.Count;
            for (var k = 0; k < solids.Count; k += RoomBoxesPerPart)
            {
                var part = solids.GetRange(k, Math.Min(RoomBoxesPerPart, solids.Count - k));
                var e = new Element { Id = k == 0 ? $"room{ri}" : $"room{ri}.{k / RoomBoxesPerPart}", Kind = "floor", Name = $"{name} shell", Area = k == 0 ? area : 0, Kg = kgEach * part.Count, Boxes = part };
                (e.BoxMin, e.BoxMax) = (part[0].Min, part[0].Max);
                if (k == 0)
                    e.Mesh = look;
                els.Add(e);
            }
            for (var k = 0; k < hulls.Count; k += RoomHullsPerPart)
            {
                var n = Math.Min(RoomHullsPerPart, hulls.Count - k);
                var e = new Element { Id = $"room{ri}.h{k / RoomHullsPerPart}", Kind = "floor", Name = $"{name} shell (to the skin)", Area = els.Count == first ? area : 0, Kg = kgEach * n, Points = [], Runs = [] };
                (e.BoxMin, e.BoxMax) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
                for (var h = k; h < k + n; h++)
                {
                    foreach (var v in hulls[h])
                    {
                        e.Points.Add(v);
                        (e.BoxMin, e.BoxMax) = (Vector3.Min(e.BoxMin, v), Vector3.Max(e.BoxMax, v));
                    }
                    e.Runs.Add(e.Points.Count);
                }
                if (els.Count == first)
                    e.Mesh = look;
                els.Add(e);
            }
            for (var k = first + 1; k < els.Count; k++)
                Weld(els, first, k);
            var hull = d.Decks[r.Deck].Hull != 0 ? d.Decks[r.Deck].Hull : d.Hulls.Count > 0 ? d.Hulls[0].Id : 0;
            var floorZ = d.Stacked ? z1 : (z0 + z1) * 0.5f;
            if (st.BandAt(hull, (x0 + x1) < 0 ? 1 : 0, floorZ) is var b and >= 0)
                Weld(els, b, first);
            if (pieces > 1 && ri == 0)
                Console.WriteLine($"[design] {name}: its shell is {pieces} parts ({solids.Count} boxes, {hulls.Count} hulls)");
            yield return els.Count;
        }
    }

    /// <summary>A shell box through the clip to the skin the designer gives its mesh: whether it bent, and its 8 corners after.</summary>
    private static (bool Bent, Vector3[] Corners) Bent(ShipDesign d, Room r, double[] lo, double[] hi, Dictionary<(double, double, double), (double, double, double)> memo)
    {
        var tmp = new DesignMesh();
        for (var i = 0; i < 8; i++)
        {
            tmp.P.Add((i & 1) == 0 ? lo[0] : hi[0]);
            tmp.P.Add((i & 2) == 0 ? lo[1] : hi[1]);
            tmp.P.Add((i & 4) == 0 ? lo[2] : hi[2]);
        }
        var before = tmp.P.ToArray();
        DesignScene.ClipToHulls(d, tmp, DesignScene.ShellInset, r, r.WallThick, 0, int.MaxValue, memo);
        var bent = false;
        var c = new Vector3[8];
        for (var i = 0; i < 8; i++)
        {
            c[i] = new Vector3((float)tmp.P[i * 3], (float)tmp.P[i * 3 + 1], (float)tmp.P[i * 3 + 2]);
            bent |= Vector3.DistanceSquared(c[i], new Vector3((float)before[i * 3], (float)before[i * 3 + 1], (float)before[i * 3 + 2])) > BentBy * BentBy;
        }
        return (bent, c);
    }

    /// <summary>Boxes that meet face to face over the whole face merged into one, until none do (the shell's 30 cm runs into its walls' panels).</summary>
    private static void MergeBoxes(List<ElementBox> boxes)
    {
        const float eps = 1e-4f;
        static bool Same(float a, float b) => MathF.Abs(a - b) < eps;
        var merged = true;
        while (merged)
        {
            merged = false;
            for (var i = 0; i < boxes.Count; i++)
                for (var j = i + 1; j < boxes.Count; j++)
                {
                    var (a, b) = (boxes[i], boxes[j]);
                    for (var k = 0; k < 3; k++)
                    {
                        int u = (k + 1) % 3, v = (k + 2) % 3;
                        if (!Same(a.Min[u], b.Min[u]) || !Same(a.Max[u], b.Max[u]) || !Same(a.Min[v], b.Min[v]) || !Same(a.Max[v], b.Max[v]))
                            continue;
                        if (!Same(a.Max[k], b.Min[k]) && !Same(b.Max[k], a.Min[k]))
                            continue;
                        boxes[i] = new ElementBox(Vector3.Min(a.Min, b.Min), Vector3.Max(a.Max, b.Max));
                        boxes.RemoveAt(j);
                        j = i; // (the grown box against the rest again)
                        merged = true;
                        break;
                    }
                }
        }
    }

    /// <summary>This file's kinds (none: a room's elements are structure).</summary>
    private static void RegisterRooms(KindTable k)
    {
    }
}
