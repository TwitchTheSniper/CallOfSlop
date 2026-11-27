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
/// <item>Its extensions (<see cref="Room.Exts"/>) are in its shell (<see cref="DesignScene.RoomShell"/> walls each one and opens the room's wall or
/// ceiling across it), each wall a thin convex piece; a room out to the hull (or with no shell) has no extension walls yet (logged).</item>
/// </list>
/// Then the ways between: each passage through an arm between bodies (<see cref="DesignScene.PassageShell"/>: its floor, walls and roof, the arm's
/// own solid collider left out where one runs through it), a lift passage's car on a piston along a straight level arm, and each socket's
/// connector shaft to the room it is linked to (<see cref="DesignScene.ConnectorOf"/>: its walls, and an upright one's ladder).
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
        var check = st.Check ??= DesignScene.Run(d); // (the doorways, as the designer has them)
        var roomEl = new Dictionary<int, int>(); // a room's id: its shell's first element
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
            if (r.Exts.Count > 0 && (r.ToHull || r.WallThick < 0.005))
                Console.WriteLine($"[design] {name} on deck {r.Deck + 1}: its {r.Exts.Count} extension(s) have no wall to walk on yet ({(r.ToHull ? "a room out to the hull" : "a room with no shell")}: drawn only in the designer)");
            var boxes = new List<(double[] Lo, double[] Hi)>();
            var strips = new List<double[]>();
            var shell = DesignScene.RoomShell(d, r, check.Doors, boxes, strips, neutral: true); // (steel, not the designer's room tint)
            var over = DesignScene.ShellOver(r); // (an up extension reaches over its ceiling)
            yield return els.Count; // (the shell, then its clip a slice of corners a step, its boxes and strips a slice a step, the merge: each a step)
            // (a corner shared by several triangles, boxes or strips is pulled in once: one memo for the shell's clip, one for the interior's)
            var memo = new Dictionary<(double, double, double), (double, double, double)>();
            for (var v = 0; v < shell.Tris * 3; v += ClipSlice)
            {
                DesignScene.ClipToHulls(d, shell, DesignScene.ShellInset, r, over, v, v + ClipSlice, memo);
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
                var (bent, corners) = Bent(d, r, lo, hi, over, memo);
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
                DesignScene.ClipToHulls(d, tmp, DesignScene.ShellInset, r, over, 0, int.MaxValue, memo);
                var c = new Vector3[s.Length / 3]; // (a hull-shell strip's 8 corners, an extension's piece's 8 or more)
                for (var k = 0; k < c.Length; k++)
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
            roomEl[r.Id] = first;
            yield return els.Count;
        }
        foreach (var n in AddWays(st, roomEl))
            yield return n;
    }

    /// <summary>A shell box through the clip to the skin the designer gives its mesh: whether it bent, and its 8 corners after.</summary>
    private static (bool Bent, Vector3[] Corners) Bent(ShipDesign d, Room r, double[] lo, double[] hi, double over, Dictionary<(double, double, double), (double, double, double)> memo)
    {
        var tmp = new DesignMesh();
        for (var i = 0; i < 8; i++)
        {
            tmp.P.Add((i & 1) == 0 ? lo[0] : hi[0]);
            tmp.P.Add((i & 2) == 0 ? lo[1] : hi[1]);
            tmp.P.Add((i & 4) == 0 ? lo[2] : hi[2]);
        }
        var before = tmp.P.ToArray();
        DesignScene.ClipToHulls(d, tmp, DesignScene.ShellInset, r, over, 0, int.MaxValue, memo);
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

    // ---- The ways between rooms (package J): passages through the arms, a lift passage's car, the sockets' connector shafts ----------

    public const float PassageCarLen = 1.0f, PassageCarClear = 0.015f, PassageCarT = 0.035f; // its car's length, its gap over the floor, its deck (its top 5 cm over the floor: walked onto)

    /// <summary>The passages and the connectors, after the rooms (each welds to the rooms it joins).</summary>
    private static IEnumerable<int> AddWays(Steps st, Dictionary<int, int> roomEl)
    {
        var (d, els, check) = (st.D, st.Els, st.Check!);
        if (d.Stacked && (check.Passages.Count > 0 || check.Links.Exists(l => l.Shaft > 0)))
        {
            Console.WriteLine($"[design] \"{d.Name}\": a stacked design's passages and connector shafts aren't built yet");
            yield break;
        }
        int RoomAt(int id) => roomEl.TryGetValue(id, out var k) ? k : -1;
        foreach (var (k, l) in check.Passages)
        {
            if (l is null)
                continue;
            var pieces = new List<double[]>();
            var mesh = DesignScene.PassageShell(d, k, l, pieces);
            var fr = DesignScene.PassageFrames(d, k, l);
            var len = 0.0;
            for (var i = 0; i + 1 < fr.Count; i++)
                len += Math.Sqrt(Sq(fr[i + 1].Floor[0] - fr[i].Floor[0]) + Sq(fr[i + 1].Floor[1] - fr[i].Floor[1]) + Sq(fr[i + 1].Floor[2] - fr[i].Floor[2]));
            var hw = (float)DesignScene.PassageHalf(l);
            var first = PieceElements(els, $"pass{k.Id}", k.Kind == "Lift" ? "Lift passage" : "Walkway", pieces, LookOf(mesh), (float)len * 2 * hw * DeckKgPerM2 * 2);
            if (first < 0)
                continue;
            foreach (var room in new[] { k.A, k.B })
                if (RoomAt(room) is var re and >= 0)
                    Weld(els, re, first);
            // Its arm's solid collider (one convex hull round the arm, which fills it) is kept out of the passage's tube (ClipHulls, by PassKeeps):
            // the passage's walls collide inside, the arm's skin round them still does.
            var ji = d.Joins.IndexOf(l.Join);
            var midX = l.Pts[l.Pts.Count / 2][0];
            var je = els.FindIndex(e => e.Id == $"j{ji}") is var j0 and >= 0 ? j0 : els.FindIndex(e => e.Id == $"j{ji}.{(midX < 0 ? 1 : 0)}");
            if (je >= 0)
            {
                (els[je].PassKeeps ??= []).AddRange(PassageKeeps(fr, hw));
                Weld(els, je, first);
            }
            if (k.Kind == "Lift")
                PassageLift(st, k, l, fr, hw, first);
            yield return els.Count;
        }
        for (var i = 0; i < check.Links.Count; i++)
        {
            if (DesignScene.ConnectorOf(d, check, i) is not { } cn)
                continue;
            var len = (float)DesignScene.Dist3(check.Links[i].A, check.Links[i].B);
            var first = PieceElements(els, $"conn{i}", "Connector shaft", cn.Pieces, LookOf(cn.Mesh), len * 4 * (float)(check.Links[i].Shaft + 0.2) * SkinKgPerM2);
            if (first < 0)
                continue;
            if (cn.Room is { } room && RoomAt(room.Id) is var re and >= 0)
                Weld(els, re, first);
            if (cn.Why != "")
                Console.WriteLine($"[design] \"{d.Name}\": the connector shaft from {check.Links[i].Sock} socket {i}: {cn.Why}");
            if (!double.IsNaN(cn.Foot) && cn.Rails.Count > 0)
            {
                var e = new Element
                {
                    Id = $"conn{i}.ladder", Kind = "ladder", Name = "Connector ladder", Boxes = [.. cn.Rails.Select(b => ShipBox(b))],
                    Rot = Facing(Dir(cn.Toward), Vector3.UnitY), LadderFoot = (float)cn.Foot, LadderTop = (float)cn.Top,
                    Kg = MathF.Max(5f, (float)(cn.Top - cn.Foot) * LadderKgPerM),
                };
                (e.BoxMin, e.BoxMax) = (e.Boxes[0].Min, e.Boxes[0].Max);
                els.Add(e);
                Weld(els, first, els.Count - 1);
            }
            yield return els.Count;
        }
    }

    private static double Sq(double v) => v * v;

    /// <summary>
    /// Convex pieces (corners, 3 numbers each) as elements of <paramref name="name"/>: <see cref="RoomHullsPerPart"/> a part, the first drawing
    /// <paramref name="look"/>, the rest welded to it. The first's index, or -1 with none.
    /// </summary>
    private static int PieceElements(List<Element> els, string id, string name, List<double[]> pieces, LookBuilder look, float kg)
    {
        if (pieces.Count == 0)
            return -1;
        var first = els.Count;
        var kgEach = MathF.Max(1f, kg / pieces.Count);
        for (var k = 0; k < pieces.Count; k += RoomHullsPerPart)
        {
            var n = Math.Min(RoomHullsPerPart, pieces.Count - k);
            var e = new Element { Id = k == 0 ? id : $"{id}.{k / RoomHullsPerPart}", Kind = "floor", Name = name, Kg = kgEach * n, Points = [], Runs = [] };
            (e.BoxMin, e.BoxMax) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
            for (var h = k; h < k + n; h++)
            {
                var s = pieces[h];
                for (var c = 0; c + 2 < s.Length; c += 3)
                {
                    var v = new Vector3((float)s[c], (float)s[c + 1], (float)s[c + 2]);
                    e.Points.Add(v);
                    (e.BoxMin, e.BoxMax) = (Vector3.Min(e.BoxMin, v), Vector3.Max(e.BoxMax, v));
                }
                e.Runs.Add(e.Points.Count);
            }
            if (k == 0)
                e.Mesh = look;
            els.Add(e);
            if (k > 0)
                Weld(els, first, els.Count - 1);
        }
        return first;
    }

    /// <summary>
    /// A lift passage's car (F's lifts' way: a piston and a car on it, package F): along a straight, level arm on one of the ship's level axes,
    /// a platform from its first door's end to its last's on one piston (<c>liftp{id}.j0</c>, its car <c>liftp{id}.car</c>), F on the car sending
    /// it to the other end (<see cref="LiftUse"/>). One that bends, climbs or runs across the ship's axes is a walkway (logged).
    /// </summary>
    private static void PassageLift(Steps st, Passage k, DesignScene.PassageLay l, List<DesignScene.PassageFrame> fr, float hw, int floorEl)
    {
        var (d, els) = (st.D, st.Els);
        Vector3 F(int i) => new((float)fr[i].Floor[0], (float)fr[i].Floor[1], (float)fr[i].Floor[2]);
        var (f0, f1) = (F(0), F(fr.Count - 1));
        var run = f1 - f0;
        var flat = new Vector3(run.X, 0, run.Z);
        var axisX = MathF.Abs(flat.X) >= MathF.Abs(flat.Z);
        var along = axisX ? MathF.Abs(flat.X) : MathF.Abs(flat.Z);
        var dir = axisX ? new Vector3(MathF.Sign(flat.X), 0, 0) : new Vector3(0, 0, MathF.Sign(flat.Z));
        var why = DesignScene.LiftLine(DesignScene.PassageFrames(d, k, l, true)) is { Length: > 0 } w ? w : null;
        if (why == null && els.Count(x => x.Joint != null) + 1 > Construct.MaxJoints)
            why = $"the body's {Construct.MaxJoints} joints are all taken";
        if (why != null)
        {
            Console.WriteLine($"[design] \"{d.Name}\": the lift passage {k.Id} is a walkway for now: {why}");
            return;
        }
        var across = axisX ? Vector3.UnitZ : Vector3.UnitX;
        var bottom = MathF.Max(f0.Y, f1.Y) + PassageCarClear; // (level over its floor, laid straight: DesignScene.PassageFrames; its deck's top under the 6 cm a person walks over)
        var start = f0 + dir * (0.05f + PassageCarLen / 2);
        var travel = along - 0.1f - PassageCarLen;
        var ramAt = start - dir * (PassageCarLen / 2 - 0.05f) + across * (hw - 0.08f);
        var ramBox = new ElementBox(new Vector3(ramAt.X - 0.03f, bottom, ramAt.Z - 0.03f), new Vector3(ramAt.X + 0.03f, bottom + 0.06f, ramAt.Z + 0.03f));
        var joints = els.Count(x => x.Joint != null);
        var ram = new Element
        {
            Id = $"liftp{k.Id}.j0", Kind = "lift", Name = "Passage lift ram", Kg = 20f, Boxes = [ramBox],
            Joint = new JointSpec
            {
                Type = JointType.Piston, Axis = (byte)(axisX ? (dir.X > 0 ? 0 : 1) : (dir.Z > 0 ? 4 : 5)), Min = 0, Max = travel, Speed = LiftSpeed, Strength = LiftStrength,
                Pivot = (ramBox.Min + ramBox.Max) * 0.5f,
            },
        };
        (ram.BoxMin, ram.BoxMax) = (ramBox.Min, ramBox.Max);
        els.Add(ram);
        Weld(els, floorEl, els.Count - 1);
        var half = dir * (PassageCarLen / 2) + across * (hw - 0.1f);
        var (cmin, cmax) = (Vector3.Min(start - half, start + half), Vector3.Max(start - half, start + half));
        var deck = new ElementBox(new Vector3(cmin.X, bottom, cmin.Z), new Vector3(cmax.X, bottom + PassageCarT, cmax.Z));
        var look = new DesignMesh();
        look.World();
        look.Box(deck.Min.X, deck.Min.Y, deck.Min.Z, deck.Max.X, deck.Max.Y, deck.Max.Z, "#9aa3ad");
        var car = new Element
        {
            Id = $"liftp{k.Id}.car", Kind = "lift", Name = "Passage lift car", Fn = "walkdeck", Seg = (byte)(joints + 1), Kg = CarKg + PassageCarLen * 2 * hw * DeckKgPerM2,
            Boxes = [deck], Mesh = LookOf(look),
        };
        (car.BoxMin, car.BoxMax) = (deck.Min, deck.Max);
        els.Add(car);
        Weld(els, els.Count - 2, els.Count - 1);
    }

    /// <summary>This file's kinds: a lift passage's car is a deck a person steps up onto from the floor round it (ConstructWorld.BlockPlayer's walkable parts); the rest is structure.</summary>
    private static void RegisterRooms(KindTable k)
    {
        k.Fn("walkdeck", _ => Template.ShallowCopy(), (_, def) => def.Walk = WalkKind.Floor);
    }
}
