using System.Numerics;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>
/// Design parts, the risers (DesignParts.cs has the map of the files): ladders and lifts (the plan's package F), laid out once by the designer
/// (<see cref="DesignRisers"/>: the same boxes its page draws), never a second time here.
/// <list type="bullet">
/// <item>A <b>ladder</b> (a riser, a Shaft room's well, a boarding hatch's) is an element with <see cref="Element.LadderTop"/> and its foot: its
/// rails are its solid boxes (its bounds are the ladder's, which the climb's line is read from: ConstructRideLadder), turned so its -z faces the
/// climber in its well and its +z the landing it is stepped off onto. A boarding hatch's ladder is the hatch kit's own rails (the piece draws
/// them); over a Shaft room the shaft's ladder carries it on up as one climb. Its lid is the doors' (package E): ConstructRideLadder.NextLid
/// finds a shut hatch door across its line.</item>
/// <item>A <b>lift</b> is its shaft (posts, fixed), a chain of pistons (one a span between stops, each riding the one under it: element
/// "lift{id}.j{i}") and its car on the last (<see cref="Element.Seg"/>): what stands on the car rides it (MoveState.RideOn). A stop is
/// every deck from its From to its To; F on the car sends it to the next stop up (from the top, back to the bottom), F on the shaft calls it
/// to the deck the player is on (<see cref="LiftUse"/>). Each piston is moved by its latch alone (CJoint.Open: out to its span, or in), so the
/// physics, the wire and the save carry a lift as any piston.</item>
/// </list>
/// A stacked design's risers aren't built yet (logged).
/// </summary>
public static partial class DesignParts
{
    public const float LadderKgPerM = 12f, PostKgPerM = 9f, CarKg = 180f; // a ladder's two rails and rungs; a lift's shaft post; a car
    public const float LiftSpeed = 0.8f; // m/s each span's piston runs at
    public const float LiftStrength = 2.0e5f; // N each piston pushes with (a car with riders and the pistons over it, many times over)

    /// <summary>The risers' elements: after the rooms (each welds to the rooms at its ends) and the doors.</summary>
    private static IEnumerable<int> AddRisers(Steps st)
    {
        var (d, els) = (st.D, st.Els);
        var lays = DesignRisers.Lays(d);
        var hatches = BoardingHatches(d);
        if (lays.Count == 0 && hatches.Count == 0)
            yield break;
        if (d.Stacked)
        {
            Console.WriteLine($"[design] \"{d.Name}\": a stacked design's {lays.Count + hatches.Count} ladder(s) and lift(s) aren't built yet (its rooms aren't walked yet either)");
            yield break;
        }
        var roomEl = new Dictionary<int, int>(); // a room's id: its shell's first element
        for (var ri = 0; ri < d.Rooms.Count; ri++)
            if (els.FindIndex(e => e.Id == $"room{ri}") is var at and >= 0)
                roomEl[d.Rooms[ri].Id] = at;
        int RoomAt(Room? r) => r != null && roomEl.TryGetValue(r.Id, out var k) ? k : -1;
        var carried = new HashSet<int>(); // (boarding hatches a shaft ladder carries on up)
        foreach (var l in lays)
        {
            if (l.Lift)
                AddLift(st, l, RoomAt);
            else
            {
                AddLadder(st, l, RoomAt);
                if (l.HatchIndex >= 0)
                    carried.Add(l.HatchIndex);
            }
            yield return els.Count;
        }
        foreach (var (pi, w) in hatches)
            if (!carried.Contains(pi))
            {
                HatchLadder(st, pi, w, RoomAt);
                yield return els.Count;
            }
    }

    /// <summary>The boarding hatches on a belly (each the piece's index and place; a mirrored or paired copy isn't given a ladder yet).</summary>
    private static List<(int Piece, DesignScene.Place W)> BoardingHatches(ShipDesign d)
    {
        var o = new List<(int, DesignScene.Place)>();
        for (var i = 0; i < d.Pieces.Count; i++)
        {
            var p = d.Pieces[i];
            if (p.Kind != "Boarding hatch" || d.Hull(p.Hull) is not { } h)
                continue;
            var w = DesignScene.Where(h, p);
            if (w.Y[1] > -0.9)
            {
                Console.WriteLine($"[design] \"{d.Name}\": the boarding hatch {p.Id} isn't on a belly facing down: it has no ladder");
                continue;
            }
            if (p.Mirror || p.Pair && h.Copies == 2)
                Console.WriteLine($"[design] \"{d.Name}\": the boarding hatch {p.Id}'s mirrored copy has no ladder yet (only its first)");
            o.Add((i, w));
        }
        return o;
    }

    /// <summary>A ship-frame box from the designer's (two corners).</summary>
    private static ElementBox ShipBox((double[] Lo, double[] Hi) b, float kg = 0) =>
        new(new Vector3((float)b.Lo[0], (float)b.Lo[1], (float)b.Lo[2]), new Vector3((float)b.Hi[0], (float)b.Hi[1], (float)b.Hi[2]), kg);

    private static Vector3 Dir(double[] v) => Vector3.Normalize(new Vector3((float)v[0], (float)v[1], (float)v[2]));

    /// <summary>The look of some of a riser's boxes (the ship's frame), into a look builder.</summary>
    private static LookBuilder LookOf(DesignMesh m)
    {
        var look = new LookBuilder();
        for (var t = 0; t < m.Tris; t++)
            look.AddTri(m, t, Vector3.One, new Vector3(float.NaN));
        return look;
    }

    /// <summary>A ladder: its rails solid, its rungs and strips drawn, climbed from its well to its landing.</summary>
    private static void AddLadder(Steps st, DesignRisers.Lay l, Func<Room?, int> roomAt)
    {
        var (d, els) = (st.D, st.Els);
        var all = DesignRisers.Fixed(d, l);
        var e = new Element
        {
            Id = l.Riser != null ? $"riser{l.Id}" : $"shaft{l.Id}", Kind = "ladder", Name = l.Riser != null ? "Ladder" : "Shaft ladder",
            Boxes = [.. all.Where(b => b.Solid).Select(b => ShipBox(DesignRisers.InShip(l, b)))], Mesh = LookOf(DesignRisers.Mesh(l, all)),
            Rot = Facing(Dir(l.SideU != 0 ? l.U : l.V) * (l.SideU != 0 ? l.SideU : l.SideV), Dir(l.Up)),
            LadderFoot = (float)l.World(0, l.Stops[0], 0)[1], LadderTop = (float)l.World(0, l.Top, 0)[1],
        };
        e.Kg = MathF.Max(5f, (float)(l.Top - l.Stops[0]) * LadderKgPerM);
        (e.BoxMin, e.BoxMax) = (e.Boxes[0].Min, e.Boxes[0].Max);
        if (l.Hatch is { } hp && HatchPlace(d, l.HatchIndex) is { } w)
        {
            // On up from a boarding hatch: one climb from the belly (the hatch kit's rails, drawn by the piece) to this one's top.
            var hatch = HatchRails(d, hp, w);
            e.Boxes.AddRange(hatch.Boxes);
            e.LadderFoot = hatch.Foot;
            e.Id = $"hatch{l.HatchIndex}.ladder";
            e.Name = "Boarding ladder";
            e.Kg += (e.LadderTop - e.LadderFoot) * LadderKgPerM * 0.5f;
        }
        // A Shaft room joins a room over it and one under it (or two of the deck above): one ladder each, all with the shaft's id. The first
        // keeps "shaft{id}" (a saved body's name), the others are named by the other room, since MakeSteps' ById takes the last of a repeat.
        if (els.Exists(x => x.Id == e.Id))
        {
            var other = l.Rooms.FirstOrDefault(r => r.Id != l.Id) ?? l.Rooms[^1];
            e.Id = $"{e.Id}.r{other.Id}";
            for (var n = 2; els.Exists(x => x.Id == e.Id); n++)
                e.Id = $"shaft{l.Id}.r{other.Id}.{n}";
        }
        var at = els.Count;
        els.Add(e);
        if (l.Hatch != null && els.FindIndex(x => x.Id == $"p{l.HatchIndex}") is var pe and >= 0)
            Weld(els, pe, at);
        if (l.Hatch != null)
            DropOnto(els, at, l.HatchIndex); // (its foot let down to the ground: DesignParts.Pieces.cs)
        foreach (var r in l.Rooms)
            if (roomAt(r) is var k and >= 0)
                Weld(els, k, at);
        if (!l.Ok)
            Console.WriteLine($"[design] \"{d.Name}\": the {e.Name.ToLowerInvariant()} {l.Id} has no room round it at every stop: built all the same");
    }

    private static DesignScene.Place? HatchPlace(ShipDesign d, int pi) =>
        pi >= 0 && pi < d.Pieces.Count && d.Hull(d.Pieces[pi].Hull) is { } h ? DesignScene.Where(h, d.Pieces[pi]) : null;

    /// <summary>
    /// A boarding hatch's ladder as its piece draws it (PieceKinds "Boarding hatch": rails x 0.25-0.3, z 0.38-0.43, from its foot on the skin
    /// 2.2 m into the ship, in its frame scaled by its size and across by its hole): the rails' boxes in the ship, its foot's and its top's height.
    /// </summary>
    private static (List<ElementBox> Boxes, float Foot, float Top) HatchRails(ShipDesign d, Piece p, DesignScene.Place w)
    {
        var across = PieceKinds.Find(p.Kind)?.Footprint is not null ? p.Hole : 1;
        var s = p.Size;
        double[] P(double x, double y, double z) =>
        [
            w.O[0] - w.Y[0] * p.Recess + s * (across * x * w.X[0] + y * w.Y[0] + across * z * w.Z[0]),
            w.O[1] - w.Y[1] * p.Recess + s * (across * x * w.X[1] + y * w.Y[1] + across * z * w.Z[1]),
            w.O[2] - w.Y[2] * p.Recess + s * (across * x * w.X[2] + y * w.Y[2] + across * z * w.Z[2]),
        ];
        var boxes = new List<ElementBox>();
        foreach (var (x0, x1) in new[] { (-0.3, -0.25), (0.25, 0.3) })
        {
            var (a, b) = (P(x0, -2.2, 0.38), P(x1, 0, 0.43));
            boxes.Add(ShipBox(([Math.Min(a[0], b[0]), Math.Min(a[1], b[1]), Math.Min(a[2], b[2])], [Math.Max(a[0], b[0]), Math.Max(a[1], b[1]), Math.Max(a[2], b[2])])));
        }
        var foot = (float)P(0, 0, 0)[1];
        var top = (float)P(0, -2.2, 0)[1];
        return (boxes, MathF.Min(foot, top), MathF.Max(foot, top));
    }

    /// <summary>A boarding hatch's ladder on its own (no shaft over it): from the belly to the floor of the room over it.</summary>
    private static void HatchLadder(Steps st, int pi, DesignScene.Place w, Func<Room?, int> roomAt)
    {
        var (d, els) = (st.D, st.Els);
        var p = d.Pieces[pi];
        var (boxes, foot, top) = HatchRails(d, p, w);
        var room = d.Rooms.Where(r => w.O[0] >= r.X0 - 0.3 && w.O[0] <= r.X1 + 0.3 && w.O[2] >= r.Z0 - 0.3 && w.O[2] <= r.Z1 + 0.3 && d.DeckOf(r).Floor >= w.O[1] - 0.5)
            .OrderBy(r => d.DeckOf(r).Floor).FirstOrDefault();
        if (room != null)
            top = (float)(d.DeckOf(room).Floor + DesignScene.ShellDepth(room)); // (stepped off onto its room's floor)
        var z = Vector3.Normalize(new Vector3((float)w.Z[0], 0, (float)w.Z[2]));
        var e = new Element
        {
            Id = $"hatch{pi}.ladder", Kind = "ladder", Name = "Boarding ladder", Boxes = boxes, Rot = Facing(z, Vector3.UnitY),
            LadderFoot = foot, LadderTop = top, Kg = MathF.Max(5f, (top - foot) * LadderKgPerM),
        };
        (e.BoxMin, e.BoxMax) = (boxes[0].Min, boxes[0].Max);
        var at = els.Count;
        els.Add(e);
        if (els.FindIndex(x => x.Id == $"p{pi}") is var pe and >= 0)
            Weld(els, pe, at);
        DropOnto(els, at, pi); // (its foot let down to the ground: DesignParts.Pieces.cs)
        if (roomAt(room) is var k and >= 0)
            Weld(els, k, at);
        // TODO(package E): the hatch's lid is a door element across this ladder's line (NextLid then opens it on F); none yet.
    }

    /// <summary>A lift: its shaft, a piston a span between stops (each on the one under it) and its car on the last.</summary>
    private static void AddLift(Steps st, DesignRisers.Lay l, Func<Room?, int> roomAt)
    {
        var (d, els) = (st.D, st.Els);
        var spans = l.Stops.Length - 1;
        var joints = els.Count(x => x.Joint != null);
        if (spans < 1 || joints + spans > Construct.MaxJoints)
        {
            Console.WriteLine($"[design] \"{d.Name}\": the lift {l.Id} isn't built: {(spans < 1 ? "it has one stop" : $"its {spans} spans pass the body's {Construct.MaxJoints} joints")}");
            return;
        }
        var fixedBoxes = DesignRisers.Fixed(d, l);
        var shaft = new Element
        {
            Id = $"lift{l.Id}.shaft", Kind = "lift", Name = "Lift shaft", Boxes = [.. fixedBoxes.Where(b => b.Solid).Select(b => ShipBox(DesignRisers.InShip(l, b)))],
            Mesh = LookOf(DesignRisers.Mesh(l, fixedBoxes)),
        };
        shaft.Kg = MathF.Max(5f, 4 * (float)(l.Top + DesignRisers.CarHeight) * PostKgPerM);
        (shaft.BoxMin, shaft.BoxMax) = (shaft.Boxes[0].Min, shaft.Boxes[0].Max);
        var at = els.Count;
        els.Add(shaft);
        foreach (var r in l.Rooms)
            if (roomAt(r) is var k and >= 0)
                Weld(els, k, at);
        // The pistons: span i's ram is a small box inside the -u -v corner post at its stop, riding span i - 1.
        var up = Dir(l.Up);
        var axis = (byte)(up.Y > 0.5f ? 2 : up.Y < -0.5f ? 3 : up.Z > 0.5f ? 4 : up.Z < -0.5f ? 5 : up.X > 0 ? 0 : 1);
        var prev = at;
        byte seg = 0;
        for (var i = 0; i < spans; i++)
        {
            var ram = DesignRisers.InShip(l, new DesignRisers.RBox(-l.Hx - 0.02, l.Stops[i] + 0.1, -l.Hz - 0.02, -l.Hx + 0.04, l.Stops[i] + 0.16, -l.Hz + 0.04, "", true));
            var j = new Element
            {
                Id = $"lift{l.Id}.j{i}", Kind = "lift", Name = "Lift ram", Seg = seg, Kg = 20f, Boxes = [ShipBox(ram)],
                Joint = new JointSpec
                {
                    Type = JointType.Piston, Axis = axis, Min = 0, Max = (float)(l.Stops[i + 1] - l.Stops[i]), Speed = LiftSpeed, Strength = LiftStrength,
                    Pivot = (ShipBox(ram).Min + ShipBox(ram).Max) * 0.5f,
                },
            };
            (j.BoxMin, j.BoxMax) = (j.Boxes[0].Min, j.Boxes[0].Max);
            els.Add(j);
            Weld(els, prev, els.Count - 1);
            prev = els.Count - 1;
            seg = (byte)(joints + i + 1);
        }
        var carBoxes = DesignRisers.Car(l);
        var car = new Element
        {
            Id = $"lift{l.Id}.car", Kind = "lift", Name = "Lift car", Seg = seg, Kg = CarKg + (float)(4 * l.Hx * l.Hz) * DeckKgPerM2,
            Boxes = [.. carBoxes.Where(b => b.Solid).Select(b => ShipBox(DesignRisers.InShip(l, b)))], Mesh = LookOf(DesignRisers.Mesh(l, carBoxes)),
        };
        (car.BoxMin, car.BoxMax) = (car.Boxes[0].Min, car.Boxes[0].Max);
        els.Add(car);
        Weld(els, prev, els.Count - 1);
        if (!l.Ok)
            Console.WriteLine($"[design] \"{d.Name}\": the lift {l.Id} has no room round it at every stop: built all the same");
    }

    /// <summary>This file's kinds (none: a riser's elements are made by its stage; a ladder's and a lift's parts are plain structure with a ladder or a joint).</summary>
    private static void RegisterRisers(KindTable k)
    {
    }

    /// <summary>The element a design part is (null: not a design part).</summary>
    private static Element? ElementOf(ConstructPartDef d)
    {
        if (!IsDesign(d))
            return null;
        lock (Interned)
            return Info.TryGetValue(d, out var i) ? i.El : null;
    }

    /// <summary>Whether a design part is a lift's (its shaft, a ram or its car): F works it where it is (<see cref="KeepsUse"/>).</summary>
    public static bool IsLift(ConstructPartDef d) => ElementOf(d) is { Kind: "lift" };

    /// <summary>The lift a design part is of ("lift{id}"), and what of it ("shaft", "car", "j{i}"); false: not a lift's.</summary>
    private static bool LiftName(string id, out ReadOnlySpan<char> lift, out ReadOnlySpan<char> what)
    {
        lift = what = default;
        var dot = id.IndexOf('.');
        if (!id.StartsWith("lift", StringComparison.Ordinal) || dot < 0)
            return false;
        lift = id.AsSpan(0, dot);
        what = id.AsSpan(dot + 1);
        return true;
    }

    /// <summary>
    /// A lift's pistons on body <paramref name="c"/>, lowest first: the joint slot of each span (<paramref name="slots"/>, filled up to the
    /// count returned). A span's piston missing (shot off) ends the chain there.
    /// </summary>
    public static int LiftJoints(Construct c, int part, Span<int> slots)
    {
        if (part < 0 || part >= c.PartCount || ElementOf(c.DefOf(part)) is not { } me || !LiftName(me.Id, out var lift, out _))
            return 0;
        var n = 0;
        for (var i = 0; i < slots.Length; i++)
        {
            var found = -1;
            for (var j = 0; j < c.JointCount && found < 0; j++)
                if (c.Joints[j].Hinge < c.PartCount && ElementOf(c.DefOf(c.Joints[j].Hinge)) is { } e && LiftName(e.Id, out var l2, out var w2) &&
                    l2.SequenceEqual(lift) && w2.Length > 1 && w2[0] == 'j' && int.TryParse(w2[1..], out var k) && k == i)
                    found = j;
            if (found < 0)
                break;
            slots[n++] = found;
        }
        return n;
    }

    /// <summary>The stop a lift's car is at or bound for (how many of its pistons are latched out from the bottom).</summary>
    public static int LiftStop(Construct c, ReadOnlySpan<int> slots)
    {
        var s = 0;
        while (s < slots.Length && c.Joints[slots[s]].Open)
            s++;
        return s;
    }

    /// <summary>Sends a lift to stop <paramref name="stop"/>: every span under it latched out, every one over it in.</summary>
    public static void LiftTo(Construct c, ReadOnlySpan<int> slots, int stop)
    {
        for (var i = 0; i < slots.Length; i++)
            c.Joints[slots[i]].Open = i < stop;
        c.Wake();
    }

    /// <summary>
    /// F on a design lift's part (ConstructEdits.Use): on its car, to its next stop up (from the top, back down to the bottom); on its shaft or
    /// a ram, the car called to the stop nearest the player's eye (<paramref name="eye"/>, the world). "" when it moved, a reason when not,
    /// null when <paramref name="part"/> isn't a lift's. Not per tick.
    /// </summary>
    public static string? LiftUse(Construct c, int part, Vector3 eye)
    {
        if (part < 0 || part >= c.PartCount || ElementOf(c.DefOf(part)) is not { Kind: "lift" } me || !LiftName(me.Id, out _, out var what))
            return null;
        Span<int> slots = stackalloc int[Construct.MaxJoints];
        var n = LiftJoints(c, part, slots);
        if (n == 0)
            return "the lift's rams are gone";
        slots = slots[..n];
        foreach (var s in slots)
            if (c.Joints[s].Locked || c.Joints[s].Wired)
                return "its wires run it";
        var at = LiftStop(c, slots);
        int to;
        if (what.SequenceEqual("car"))
            to = at >= n ? 0 : at + 1;
        else
        {
            // The stop nearest the eye: each span's ram stands at its stop (the shaft's frame), the top stop past the last ram by its span.
            var local = c.ToLocal(eye);
            var best = float.MaxValue;
            to = 0;
            for (var i = 0; i <= n; i++)
            {
                // (a ram's Pos is the body's rest frame as built, a box 0.13 m over its stop's floor; the top stop is the last ram's span over it)
                ref var jt = ref c.Joints[slots[Math.Min(i, n - 1)]];
                var y = c.Parts[jt.Hinge].Pos.Y - 0.13f + (i == n ? jt.Max : 0) + 1.6f; // (an eye over that stop's floor)
                if (MathF.Abs(local.Y - y) < best)
                    (best, to) = (MathF.Abs(local.Y - y), i);
            }
            if (to == at)
                return "the lift is here";
        }
        LiftTo(c, slots, to);
        return "";
    }
}
