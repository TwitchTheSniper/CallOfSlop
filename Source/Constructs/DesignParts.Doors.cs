using System.Numerics;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>
/// Design parts, the doors (DesignParts.cs has the map of the files): the sockets' doors, airlocks and hatches, and the rooms' doors (walkable
/// designs, step 4, the plan's package E). Every door is the designer's own leaf, nothing drawn a second time: the triangles a piece's drawing
/// tags as a leaf (<see cref="DesignMesh.Leaf"/>: the airlock's outer hatch and inner door, a docking hatch's lid, a docking collar's mating hatch,
/// a cargo hatch's two leaves, a boarding hatch's lid), a placed door's leaf between two rooms (<see cref="DesignScene.RoomDoorLeaf"/>) and the
/// cover of a docking collar's hole through a room's floor (<see cref="DesignScene.HoleCover"/>), each taken out of its piece's look and collider.
/// <list type="bullet">
/// <item>Each is an element of its own (<c>Fn "door"</c>, its def an event door, ConstructDoors): box-only (one solid box round its leaf, in a
/// frame turned as its piece faces) on segment 0, so a settle moves only its own collider leaf (ConstructCollider.MovePart moves nothing with hulls).
/// F opens and shuts it (ConstructEdits.Use, past the helm redirect: KeepsUse); the server sends its moves (CBodyKind.Door) as for any door.</item>
/// <item>An airlock is its two doors and its chamber (the piece itself): each door welded to the piece alone, so each opens on its own.
/// A cargo hatch's leaves are welded to each other too, so F on either opens both (ConstructDoors.Use works a linked door with it).</item>
/// <item>A docking collar's mating hatch and a docking hatch's lid are docking hatches (<see cref="CDoorDef.Dock"/>): the server opens them as
/// the body docks (ConstructDoors.StepDocks), F doesn't.</item>
/// <item>Sliding and iris doors slide (an iris has no game form yet: Karl's call, 2026-11-26) along their opening by the leaf's own width;
/// hinged ones swing a quarter turn on their hinge, into the ship (a cargo hatch's leaves out, as bomb-bay doors).</item>
/// <item>A boarding hatch's lid is a thin door (half its height 10 cm or less) so a ladder's climb finds it (ConstructRideLadder.NextLid):
/// <see cref="LidOf"/> gives it to the risers' stage.</item>
/// </list>
/// </summary>
public static partial class DesignParts
{
    public const float DoorSeconds = 1.2f, HatchSeconds = 2f; // a door between rooms; a socket's door or hatch
    public const float DoorKgPerM2 = 40f; // a leaf's drawn surface (both its faces: about 1 cm of steel)
    private const float LeafGap = 0.04f; // a slide's travel past its leaf's own width
    private const float LeafSample = 0.1f, LeafSlant = 0.04f; // a leaf's surface sampled this often; a slanting leaf's cells each this much of its slant deep
    private const int LeafCells = 16; // a slanting leaf's cells each way at most

    /// <summary>The doors of the rooms: each placed door's leaf, each docking collar's hole cover; welded to their rooms' shells.</summary>
    private static IEnumerable<int> AddDoors(Steps st)
    {
        var (d, els) = (st.D, st.Els);
        if (d.Rooms.Count == 0 || d.Stacked)
            yield break;
        var check = st.Check ??= DesignScene.Run(d);
        yield return els.Count;
        int Shell(Room r) => els.FindIndex(e => e.Id == $"room{d.Rooms.IndexOf(r)}" || e.Id == $"room{d.Rooms.IndexOf(r)}.h0");
        // Placed doors between rooms (the designer's RoomDoor of kind Door), each in the doorway the two shells cut for it.
        foreach (var k in d.Doors)
        {
            if (k.Kind != "Door" || d.RoomById(k.A) is not { } a || d.RoomById(k.B) is not { } b)
                continue;
            var placed = check.Placed.FindIndex(q => q.Id == k.Id);
            if (placed < 0)
                continue; // (the designer's checks say it has no wall to sit in)
            var at = check.Placed[placed].At;
            var q = check.Doors.Find(o => !DesignScene.IsHole(o) && Math.Abs(o[0] - at[0]) < 0.05 && Math.Abs(o[1] - at[1]) < 0.05 && Math.Abs(o[2] - at[2]) < 0.05);
            if (q == null || DesignScene.RoomDoorLeaf(d, q, a, b, out var across) is not { } leaf)
            {
                Console.WriteLine($"[design] \"{d.Name}\": the door between {DesignScene.Name(a)} and {DesignScene.Name(b)} has no doorway of its own cut for it (a second door on one wall): it gets no leaf");
                continue;
            }
            var along = across == 0 ? Vector3.UnitZ : Vector3.UnitX;
            var e = Leaf($"door{k.Id}", $"Door ({DesignScene.Name(a)} to {DesignScene.Name(b)})", leaf, 0, leaf.Tris, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
            // Into the wall on the side with more of it (the shared wall's run past the leaf's edge: Placed's S0 and S1 are where the door's
            // middle may go, half its width in from the wall's ends); a wall too short either side for the leaf: it swings into the bigger room.
            var ax = across == 0 ? 2 : 0;
            double room0 = Math.Abs(at[ax] - check.Placed[placed].S0[ax]), room1 = Math.Abs(check.Placed[placed].S1[ax] - at[ax]);
            var width = MathF.Abs(Vector3.Dot(e.BoxMax - e.BoxMin, along));
            var way = room1 >= room0 ? along : -along;
            if (Math.Max(room0, room1) + 0.05 >= width)
                Slide(e, way, DoorSeconds);
            else
            {
                var big = (a.X1 - a.X0) * (a.Z1 - a.Z0) >= (b.X1 - b.X0) * (b.Z1 - b.Z0) ? a : b;
                var middle = across == 0 ? (big.X0 + big.X1) / 2 : (big.Z0 + big.Z1) / 2;
                var into = (middle < at[across] ? -1 : 1) * (across == 0 ? Vector3.UnitX : Vector3.UnitZ);
                Swing(e, leaf, 0, leaf.Tris, way, Vector3.UnitY, into, DoorSeconds); // (on its jamb on the longer side, into that room)
            }
            els.Add(e);
            foreach (var r in new[] { a, b })
                if (Shell(r) is var s and >= 0)
                    Weld(els, s, els.Count - 1);
        }
        // A docking collar's way in through a room's floor: its own hole (not a riser's well or another socket's beside it), which the walk would fall down, gets a cover.
        var n = 0;
        foreach (var p in d.Pieces)
        {
            if (PieceKinds.Find(p.Kind)?.Socket != "dock" || d.Hull(p.Hull) is not { } h)
                continue;
            var o = DesignScene.Where(h, p).O;
            foreach (var q in check.Doors)
            {
                if (!DesignScene.IsHole(q) || !check.SocketWays.TryGetValue(q, out var cut) || cut != p || Math.Abs(q[0] - o[0]) > 1.5 || Math.Abs(q[2] - o[2]) > 1.5)
                    continue;
                foreach (var r in d.Rooms)
                    if (DesignScene.HoleCover(d, r, q) is { } cover)
                    {
                        var e = Leaf($"cover{n++}", $"Hatch cover ({p.Kind}, {DesignScene.Name(r)})", cover, 0, cover.Tris, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
                        var bx = d.Box(r);
                        var x = bx[1] - bx[0] >= bx[5] - bx[4];
                        var mid = x ? (bx[0] + bx[1]) / 2 - q[0] : (bx[4] + bx[5]) / 2 - q[2];
                        Slide(e, (x ? Vector3.UnitX : Vector3.UnitZ) * (mid >= 0 ? 1 : -1), HatchSeconds); // (aside under the floor, towards the room's middle)
                        els.Add(e);
                        if (Shell(r) is var s and >= 0)
                            Weld(els, s, els.Count - 1);
                    }
            }
        }
        yield return els.Count;
    }

    /// <summary>This file's kinds: the door function (an event door's part from the door template; PartOf copies the element's leaf onto it).</summary>
    private static void RegisterDoors(KindTable k)
    {
        k.Fn("door", _ => DoorTemplate.ShallowCopy());
    }

    // ---- A piece's leaves (AddPieces hands each copy's mesh here) ---------------------------------------------------------------

    /// <summary>Whether triangle <paramref name="t"/> of <paramref name="m"/> is a door leaf's (left out of its piece's look and collider).</summary>
    private static bool InLeaf(DesignMesh m, int t)
    {
        if (m.Leaves is not { } ls)
            return false;
        foreach (var l in ls)
            if (t >= l.From && t < l.To)
                return true;
        return false;
    }

    /// <summary>A piece's mesh's corners without its leaves' (what its collider is taken from).</summary>
    private static IReadOnlyList<double> Unleaved(DesignMesh m)
    {
        if (m.Leaves is not { Count: > 0 })
            return m.P;
        var o = new List<double>(m.P.Count);
        for (var t = 0; t < m.Tris; t++)
            if (!InLeaf(m, t))
                o.AddRange(m.P.GetRange(t * 9, 9));
        return o;
    }

    /// <summary>The leaves a piece's copy draws (<paramref name="m"/>, its mesh in the ship's frame), each made a door element welded to it.</summary>
    private static void PieceDoors(in PieceAt a, DesignMesh m)
    {
        if (m.Leaves is not { Count: > 0 } leaves || PieceKinds.Find(a.P.Kind) is not { } kind)
            return;
        var (p, els) = (a.P, a.Els);
        (int From, int To) Range(int leaf)
        {
            foreach (var l in leaves)
                if (l.Leaf == leaf)
                    return (l.From, l.To);
            return (0, 0);
        }
        switch (kind.Socket)
        {
            case "airlock":
                foreach (var (leaf, type, _, _, _) in PieceDocking.AirlockDoors(p))
                {
                    var (f, t) = Range(leaf);
                    if (t <= f)
                        continue;
                    var e = Leaf($"{a.E.Id}.d{leaf}", leaf == 0 ? "Airlock outer hatch" : "Airlock inner door", m, f, t, a.X, a.Y, a.Z);
                    if (type == "hinged")
                        Swing(e, m, f, t, a.Z, a.X, -a.Y, HatchSeconds); // (on its hinge along its +Z edge, into the ship)
                    else
                        Slide(e, a.Z, HatchSeconds); // (sliding, and an iris as a slide for now)
                    els.Add(e);
                    Weld(els, a.At, els.Count - 1);
                }
                break;
            case "cargo":
            {
                var first = els.Count;
                for (var leaf = 0; leaf < 2; leaf++)
                {
                    var (f, t) = Range(leaf);
                    if (t <= f)
                        continue;
                    var e = Leaf($"{a.E.Id}.d{leaf}", "Cargo hatch leaf", m, f, t, a.X, a.Y, a.Z);
                    Swing(e, m, f, t, leaf == 0 ? -a.X : a.X, a.Z, a.Y, HatchSeconds); // (each on its outer edge, out of the skin)
                    els.Add(e);
                    Weld(els, a.At, els.Count - 1);
                }
                if (els.Count == first + 2)
                    Weld(els, first, first + 1); // (F on either opens both)
                break;
            }
            case "dock" or "hatch":
            {
                var (f, t) = Range(0);
                if (t <= f)
                    break;
                var e = Leaf($"{a.E.Id}.d0", kind.Socket == "dock" ? "Docking collar hatch" : "Docking hatch lid", m, f, t, a.X, a.Y, a.Z);
                Slide(e, a.Z, HatchSeconds);
                e.Door!.Dock = true; // (opened and shut by the server as its body docks: ConstructDoors.StepDocks)
                els.Add(e);
                Weld(els, a.At, els.Count - 1);
                break;
            }
            case "shaft":
                MakeLid(a, m);
                break;
            default:
                Console.WriteLine($"[design] {p.Kind} draws {leaves.Count} door leaves but makes no doors of them yet (drawn as part of it)");
                break;
        }
    }

    /// <summary>
    /// A boarding hatch's lid (its leaf 0), a thin door that slides aside along the hatch's X: so a ladder up its well finds it shut across its
    /// line and opens it (ConstructRideLadder.NextLid, OpenWay). Made once a copy, by <see cref="PieceDoors"/>; its element index, or -1.
    /// </summary>
    private static int MakeLid(in PieceAt a, DesignMesh m)
    {
        var id = $"{a.E.Id}.lid";
        if (LidOf(a.Els, a.E.Id) is var have and >= 0)
            return have;
        if (m.Leaves?.Find(l => l.Leaf == 0) is not { } l || l.To <= l.From)
            return -1;
        var e = Leaf(id, "Boarding hatch lid", m, l.From, l.To, a.X, a.Y, a.Z);
        Slide(e, a.X, HatchSeconds);
        a.Els.Add(e);
        Weld(a.Els, a.At, a.Els.Count - 1);
        return a.Els.Count - 1;
    }

    /// <summary>The lid of the boarding hatch whose element is <paramref name="pieceId"/> (the risers' stage: its ladder's lid), or -1.</summary>
    private static int LidOf(List<Element> els, string pieceId) => els.FindIndex(e => e.Id == pieceId + ".lid");

    // ---- A leaf as an element ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// A door element of triangles <paramref name="from"/> to <paramref name="to"/> of <paramref name="m"/> (the ship's frame): its look those
    /// triangles, its part turned as the frame x, y, z faces (the nearest Rot24), one solid box round them in that turn; its door set by
    /// <see cref="Slide"/> or <see cref="Swing"/>.
    /// </summary>
    private static Element Leaf(string id, string name, DesignMesh m, int from, int to, Vector3 x, Vector3 y, Vector3 z)
    {
        var rot = Facing(z, y);
        var turn = Rot24.Quat(rot);
        var back = Quaternion.Conjugate(turn);
        var e = new Element { Id = id, Kind = "door", Name = name, Fn = "door", Rot = rot, Mesh = new LookBuilder() };
        // Its surface sampled about every 10 cm, in the part's turn (a leaf on a sloping skin lies at a slant to it).
        var pts = new List<Vector3>();
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (var t = from; t < to; t++)
        {
            e.Mesh.AddTri(m, t, Vector3.One, new Vector3(float.NaN));
            var c = new Vector3[3];
            for (var v = 0; v < 3; v++)
            {
                var i = (t * 3 + v) * 3;
                c[v] = Vector3.Transform(new Vector3((float)m.P[i], (float)m.P[i + 1], (float)m.P[i + 2]), back);
            }
            var n = Math.Clamp((int)MathF.Ceiling(MathF.Max(Vector3.Distance(c[0], c[1]), MathF.Max(Vector3.Distance(c[1], c[2]), Vector3.Distance(c[2], c[0]))) / LeafSample), 1, 64);
            for (var a = 0; a <= n; a++)
                for (var b = 0; a + b <= n; b++)
                {
                    var q = c[0] + (c[1] - c[0]) * ((float)a / n) + (c[2] - c[0]) * ((float)b / n);
                    pts.Add(q);
                    (lo, hi) = (Vector3.Min(lo, q), Vector3.Max(hi, q));
                }
        }
        // Its solid boxes: one round it when it lies square to its turn; on a slant, cut across the slant into cells (each the bounds of the
        // samples in it), so the leaf collides near its face, not as a box as deep as its slant.
        var outward = Vector3.Transform(Vector3.Normalize(y), back);
        var size = hi - lo;
        int thin = MathF.Abs(outward.X) >= MathF.Abs(outward.Y) && MathF.Abs(outward.X) >= MathF.Abs(outward.Z) ? 0 : MathF.Abs(outward.Y) >= MathF.Abs(outward.Z) ? 1 : 2;
        int ai = (thin + 1) % 3, aj = (thin + 2) % 3;
        int Cells(int k) => Math.Clamp((int)MathF.Ceiling(size[k] * MathF.Abs(outward[k]) / LeafSlant), 1, LeafCells);
        int ni = Cells(ai), nj = Cells(aj);
        var cells = new (Vector3 Lo, Vector3 Hi)[ni * nj];
        Array.Fill(cells, (new Vector3(float.MaxValue), new Vector3(float.MinValue)));
        foreach (var q in pts)
        {
            var ci = Math.Clamp((int)((q[ai] - lo[ai]) / MathF.Max(size[ai], 1e-4f) * ni), 0, ni - 1);
            var cj = Math.Clamp((int)((q[aj] - lo[aj]) / MathF.Max(size[aj], 1e-4f) * nj), 0, nj - 1);
            ref var cell = ref cells[ci * nj + cj];
            (cell.Lo, cell.Hi) = (Vector3.Min(cell.Lo, q), Vector3.Max(cell.Hi, q));
        }
        e.Boxes = [];
        foreach (var (cl, ch) in cells)
        {
            if (cl.X > ch.X)
                continue;
            var half = Vector3.Max((ch - cl) * 0.5f, new Vector3(0.01f));
            // (each in the ship's frame as IntoFrames reads one: the middle turned back, the half by the part's turn, so it comes out exact)
            var mid = Vector3.Transform((cl + ch) * 0.5f, turn);
            var h = Rot24.ApplyHalf(rot, half);
            e.Boxes.Add(new ElementBox(mid - h, mid + h));
        }
        (e.BoxMin, e.BoxMax) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        foreach (var b in e.Boxes)
            (e.BoxMin, e.BoxMax) = (Vector3.Min(e.BoxMin, b.Min), Vector3.Max(e.BoxMax, b.Max));
        e.Area = e.Mesh.Area;
        e.Kg = MathF.Max(5f, e.Area * DoorKgPerM2);
        return e;
    }

    /// <summary>Its door slides along <paramref name="way"/> (the ship's frame, to the nearest of the six) by its leaf's own width that way and a little.</summary>
    private static void Slide(Element e, Vector3 way, float seconds)
    {
        var dir = NearestDir(way);
        var ax = ConstructParts.DirVector(dir);
        var width = MathF.Abs(Vector3.Dot(e.BoxMax - e.BoxMin, ax));
        e.Door = new CDoorDef { Slide = true, Axis = dir, Travel = Math.Clamp(width + LeafGap, 0.05f, 20f), Seconds = seconds, Hang = 0 };
    }

    /// <summary>
    /// Its door swings a quarter turn about a hinge along <paramref name="hingeAlong"/> (to the nearest of the six), on the leaf's edge farthest
    /// along <paramref name="edge"/>, the way that takes the leaf towards <paramref name="towards"/> (the ship's frame).
    /// </summary>
    private static void Swing(Element e, DesignMesh m, int from, int to, Vector3 edge, Vector3 hingeAlong, Vector3 towards, float seconds)
    {
        var dir = NearestDir(hingeAlong);
        var axis = ConstructParts.DirVector(dir);
        // The hinge: the leaf's corners farthest along the edge, their middle (on its face's middle plane).
        edge = Vector3.Normalize(edge);
        var most = float.MinValue;
        for (var i = from * 9; i < to * 9; i += 3)
            most = MathF.Max(most, Vector3.Dot(new Vector3((float)m.P[i], (float)m.P[i + 1], (float)m.P[i + 2]), edge));
        var mid = (e.BoxMin + e.BoxMax) * 0.5f;
        var pivot = mid + edge * (most - Vector3.Dot(mid, edge));
        var turned = Vector3.Transform(mid - pivot, Matrix4x4.CreateFromAxisAngle(axis, MathF.PI / 2)) + pivot;
        var angle = Vector3.Dot(turned - mid, towards) >= 0 ? MathF.PI / 2 : -MathF.PI / 2;
        e.Door = new CDoorDef { Slide = false, Axis = dir, Pivot = pivot, Angle = angle, Seconds = seconds, Hang = 0 };
    }
}
