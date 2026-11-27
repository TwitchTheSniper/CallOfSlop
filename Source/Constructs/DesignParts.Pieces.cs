using System.Numerics;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>
/// Design parts, the pieces (DesignParts.cs has the map of the files): one element a piece's copy (<see cref="AddPieces"/>), and what the
/// working ones do (step 3, Karl 2026-11-26): the drives and RCS nozzles push, a seat is sat in, and the flight computer flies them. Their
/// kinds go in the tables here (<see cref="RegisterPieces"/>).
/// </summary>
public static partial class DesignParts
{
    /// <summary>Pieces: one element a copy (its mirrored twin, its pair on the other body), welded to the band it stands on; then the flight computer.</summary>
    private static IEnumerable<int> AddPieces(Steps st)
    {
        var (d, els) = (st.D, st.Els);
        for (var pi = 0; pi < d.Pieces.Count; pi++)
        {
            var p = d.Pieces[pi];
            if (d.Hull(p.Hull) is not { } h || PieceKinds.Find(p.Kind) is null)
                continue;
            var one = DesignScene.PieceMesh(d, p, copies: false, shut: true); // (its doors shut, each leaf its own element: DesignParts.Doors.cs)
            if (one.Tris == 0)
                continue;
            var w0 = DesignSeats.PlaceOf(d, p) ?? DesignHangar.PlaceOf(d, p) ?? DesignScene.Where(h, p);
            var sets = new List<(DesignMesh M, int Copy, DesignScene.Place W)> { (one, p.Copy, w0) };
            if (p.Mirror)
            {
                var cx = h.CopyX(Math.Min(p.Copy, h.Copies - 1));
                sets.Add((Mirror(one, cx), p.Copy, Mirror(w0, cx))); // (its twin across its own body's centreline)
            }
            if (p.Pair && h.Copies == 2)
                for (int k = 0, n = sets.Count; k < n; k++)
                    sets.Add((Mirror(sets[k].M, 0), 1 - sets[k].Copy, Mirror(sets[k].W, 0))); // (the same on the other body of the pair, across the ship)
            for (var k = 0; k < sets.Count; k++)
            {
                var m = sets[k].M;
                var e = new Element { Id = sets.Count > 1 ? $"p{pi}.{k}" : $"p{pi}", Kind = "piece", Name = p.Kind, Mesh = new LookBuilder() };
                for (var tr = 0; tr < m.Tris; tr++)
                    if (!InLeaf(m, tr))
                        e.Mesh.AddTri(m, tr, Vector3.One, new Vector3(float.NaN));
                e.Area = e.Mesh.Area;
                e.Kg = e.Area * PieceKgPerM2;
                HullOf(e, Unleaved(m));
                var at = els.Count;
                els.Add(e);
                if (st.BandAt(p.Hull, sets[k].Copy, (e.BoxMin.Z + e.BoxMax.Z) * 0.5f) is var b and >= 0)
                    Weld(els, b, at);
                Works(new PieceAt(p, e, at, sets[k].W, els));
                if (p.Kind == "Landing leg")
                    Leg(new PieceAt(p, e, at, sets[k].W, els), m); // (its leg is landing gear, not a door F works)
                else
                    PieceDoors(new PieceAt(p, e, at, sets[k].W, els), m); // (its door leaves, each a door of its own)
                if (p.Kind == "Boarding hatch" && k == 0)
                    HatchDrop(new PieceAt(p, e, at, sets[k].W, els), d); // (its ladder's foot, let down to the ground: the risers' stage links it to the ladder)
            }
            yield return els.Count;
        }
        Flight(els);
    }

    /// <summary>This file's kinds: the drives, the RCS quads and the seat (by piece kind), and the thrust, seat and flight functions.</summary>
    private static void RegisterPieces(KindTable k)
    {
        k.Piece("Main drive", Drive);
        k.Piece("Small drive", Drive);
        k.Piece("RCS quad", Rcs);
        k.Piece("Heavy RCS", Rcs);
        k.Piece(DesignSeats.Kind, Seat);
        k.Fn("thrust", ThrustStart, ThrustFinish);
        k.Fn("seat", _ => SeatTemplate.ShallowCopy(), SeatFinish);
        k.Fn("flight", FlightStart);
        k.Fn("gear", GearStart);
    }

    // ---- Landing legs and the boarding ladder's drop ---------------------------------------------------------------------------

    // The Landing leg's drawing (PieceKinds "Landing leg", its frame's +Y out of the skin): the pod 0 to 0.9, the strut to 3.0, the pad to 3.25, 0.85 across.
    private const float PodDepth = 0.9f, PadTop = 3.0f, PadSole = 3.25f, PadRadius = 0.85f;
    public const float LegSeconds = 3.5f; // a leg's way up or down (a leg pod's: LegPodParts)
    // Its foot's spring (the XL leg pod's, whose keel stands about as high: LegPodParts), the travel by the piece's size.
    private const float FootTravel = 0.1375f, FootHz = 2.283f, FootDamping = 0.6f;
    // The boarding hatch's ladder as the piece draws it: 2.2 m of well (PieceKinds "Boarding hatch"); its drop's rails just outside the well's
    // ladder's (x 0.25-0.3) and its rungs a half step off theirs, so nothing lies on a face of the fixed ladder; its foot this high over the ground.
    private const float HatchWell = 2.2f, DropFootClear = 0.35f, DropSeconds = 1.5f;

    /// <summary>
    /// A Landing leg's leg (its leaf 0: strut and pad) as landing gear (ConstructDoors "Landing gear"): a held door that slides along the
    /// piece's +Y, worked by the helm's gear key (<see cref="CDoorDef.Gear"/> 2) with every other leg. It is made as drawn, down, and starts
    /// settled open (its DoorOpen setting's default, <see cref="GearStart"/>), so a design stands on its legs as the designer shows it; up, it
    /// draws in until its pad meets the pod. Its one solid box is its pad (its foot box: what stands on the ground, sprung as a leg pod's foot,
    /// ConstructPhysics.GearFeet, and what GearLift lifts the craft by as the legs come down on the ground); the strut is drawn only. Welded to its pod.
    /// </summary>
    private static void Leg(in PieceAt a, DesignMesh m)
    {
        if (m.Leaves?.Find(l => l.Leaf == 0) is not { } l || l.To <= l.From)
            return;
        var (s, o, x, y, z) = (a.S, a.O, a.X, a.Y, a.Z);
        var e = new Element { Id = $"{a.E.Id}.leg", Kind = "gear", Name = "Landing leg", Fn = "gear", Mesh = new LookBuilder() };
        for (var t = l.From; t < l.To; t++)
            e.Mesh.AddTri(m, t, Vector3.One, new Vector3(float.NaN));
        // The pad's box, in the ship's frame (its part stands square to the ship: a keel's slant is a few degrees).
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (var i = 0; i < 8; i++)
        {
            var q = o + s * (x * ((i & 1) != 0 ? PadRadius : -PadRadius) + y * ((i & 2) != 0 ? PadSole : PadTop) + z * ((i & 4) != 0 ? PadRadius : -PadRadius));
            (lo, hi) = (Vector3.Min(lo, q), Vector3.Max(hi, q));
        }
        e.Boxes = [new ElementBox(lo, hi)];
        (e.BoxMin, e.BoxMax) = (lo, hi);
        e.Area = e.Mesh.Area;
        e.Kg = MathF.Max(20f, e.Area * PieceKgPerM2);
        a.E.Kg = MathF.Max(1f, a.E.Kg - e.Kg);
        var dir = NearestDir(y);
        var travel = Math.Clamp((PadSole - PodDepth) * s, 0.05f, 20f); // (up, its sole at the pod's underside)
        e.Door = new CDoorDef
        {
            Slide = true, Held = true, Axis = dir, Travel = travel, Shift = ConstructParts.DirVector(dir) * travel, Seconds = LegSeconds, Hang = 0, Gear = 2,
            SpringTravel = Math.Clamp(FootTravel * s, 0.02f, 0.9f), SpringHz = Math.Clamp(FootHz / MathF.Sqrt(MathF.Max(0.1f, s)), 0.3f, 10f), SpringDamp = FootDamping,
        };
        a.Els.Add(e);
        Weld(a.Els, a.At, a.Els.Count - 1);
    }

    /// <summary>A landing leg's part: the door template's, settled open from the start (its legs down, as drawn).</summary>
    private static ConstructPartDef GearStart(Element e)
    {
        var d = DoorTemplate.ShallowCopy();
        d.Config = [.. d.Config.Select(c => c.Id == CfgKey.DoorOpen ? c.Copy(dflt: 1f) : c)];
        return d;
    }

    /// <summary>
    /// Where a design's lowest landing pad's sole is (the ship's Y, its legs down: what it stands on), or null with no legs; from the pieces as
    /// drawn (a mirrored leg's is as high).
    /// </summary>
    private static float? StandY(ShipDesign d)
    {
        float? low = null;
        foreach (var p in d.Pieces)
            if (p.Kind == "Landing leg" && d.Hull(p.Hull) is { } h)
            {
                var w = DesignScene.Where(h, p);
                var y = (float)(w.O[1] - w.Y[1] * p.Recess + w.Y[1] * PadSole * p.Size);
                low = low is { } v ? MathF.Min(v, y) : y;
            }
        return low;
    }

    /// <summary>
    /// A boarding hatch's drop (step 4: from the ground nobody could reach the belly ladder's foot, 3.2 m up on the Zero Day's legs): a short
    /// ladder, two rails and rungs stowed up the hatch's well beside the drawn ladder's rails, a slide door that lets it down out of the hatch
    /// until its foot is <see cref="DropFootClear"/> over the ground the design stands on with its legs down (at most the well's depth). The
    /// risers' stage welds it to the hatch's ladder and lowers that ladder's foot by its travel (<see cref="DropOnto"/>): F on the ladder lets
    /// it down with the lid (ConstructRide.OpenWay), a climb waits for it (StowedFoot), and the climb starts from the ground. None when the
    /// design has no legs or its belly is a step up.
    /// </summary>
    private static void HatchDrop(in PieceAt a, ShipDesign d)
    {
        if (StandY(d) is not { } ground || a.Y.Y > -0.9f)
            return; // (no legs; or the hatch isn't on a belly: it has no ladder, DesignParts.Risers.cs)
        var p = a.P;
        var s = a.S;
        var drop = MathF.Min(a.O.Y - ground - DropFootClear, HatchWell * s); // (from its foot on the skin, where the drawn ladder's rails end)
        if (drop < 0.3f)
            return;
        var across = PieceKinds.Find(p.Kind)?.Footprint is not null ? (float)p.Hole : 1f;
        var w = a.W;
        var m = new DesignMesh();
        m.Frame(w.O[0] - w.Y[0] * p.Recess, w.O[1] - w.Y[1] * p.Recess, w.O[2] - w.Y[2] * p.Recess, w.X, w.Y, w.Z, p.Size, across);
        var len = drop / s; // (the hatch's frame: stowed, from len into the ship to the skin)
        m.Box(-0.35, -len, 0.385, -0.305, 0, 0.425, PieceKinds.Amber);
        m.Box(0.305, -len, 0.385, 0.35, 0, 0.425, PieceKinds.Amber);
        for (var r = -len + 0.15; r < -0.05; r += 0.3)
            m.Box(-0.305, r, 0.395, 0.305, r + 0.04, 0.415, PieceKinds.Gun);
        var e = new Element { Id = $"{a.E.Id}.drop", Kind = "ladder", Name = "Boarding ladder drop", Fn = "door", Mesh = new LookBuilder() };
        for (var t = 0; t < m.Tris; t++)
            e.Mesh.AddTri(m, t, Vector3.One, new Vector3(float.NaN));
        var (o, ax, ay, az) = (a.O, a.X, a.Y, a.Z);
        Vector3 P(float x, float y, float z) => o + s * (ax * (x * across) + ay * y + az * (z * across));
        e.Boxes = [];
        foreach (var (x0, x1) in new[] { (-0.35f, -0.305f), (0.305f, 0.35f) })
        {
            var (b0, b1) = (P(x0, (float)-len, 0.385f), P(x1, 0, 0.425f));
            e.Boxes.Add(new ElementBox(Vector3.Min(b0, b1), Vector3.Max(b0, b1)));
        }
        (e.BoxMin, e.BoxMax) = (Vector3.Min(e.Boxes[0].Min, e.Boxes[1].Min), Vector3.Max(e.Boxes[0].Max, e.Boxes[1].Max));
        e.Area = e.Mesh.Area;
        e.Kg = MathF.Max(5f, drop * DropKgPerM);
        e.Door = new CDoorDef { Slide = true, Axis = NearestDir(a.Y), Travel = Math.Clamp(drop, 0.05f, 20f), Seconds = DropSeconds, Hang = 0 };
        a.Els.Add(e);
        Weld(a.Els, a.At, a.Els.Count - 1);
    }

    private const float DropKgPerM = 12f; // (two light rails and their rungs)

    /// <summary>
    /// The risers' stage, a boarding hatch's ladder just made (element <paramref name="ladder"/>, hatch piece <paramref name="piece"/>): welded to
    /// the hatch's drop if it has one, and its foot let down by the drop's travel, so the climb runs from the drop's foot to the ladder's top.
    /// </summary>
    private static void DropOnto(List<Element> els, int ladder, int piece)
    {
        if (els.FindIndex(x => x.Id == $"p{piece}.drop" || x.Id == $"p{piece}.0.drop") is not (var k and >= 0) || els[k].Door is not { } dd)
            return;
        els[ladder].LadderFoot -= dd.Travel;
        Weld(els, k, ladder);
    }

    /// <summary>A drive pushes along its bell's axis, away from its exhaust.</summary>
    private static void Drive(in PieceAt a)
    {
        var (p, e, s) = (a.P, a.E, a.S);
        var main = p.Kind == "Main drive";
        e.Fn = "thrust";
        e.Exhaust = Vector3.Normalize(a.Y);
        e.ThrustAt = a.O + a.Y * ((main ? MainDriveBell : SmallDriveBell) * s);
        e.Force = (main ? MainDriveN : SmallDriveN) * s * s;
        e.Mouth = (main ? 2.0f : 1.0f) * s; // (the bell's mouth in PieceKinds' drawing)
    }

    /// <summary>An RCS quad gets an element for each of its four nozzles, welded to it.</summary>
    private static void Rcs(in PieceAt a)
    {
        var (p, e, s, els, o, x, y, z) = (a.P, a.E, a.S, a.Els, a.O, a.X, a.Y, a.Z);
        var k = (float)(p.Kind == "Heavy RCS" ? DesignFittings.HeavyRcs : 1) * s;
        // The four nozzles as the drawing has them (DesignFittings.Rcs): along its X at 0.55, along its Z at 0.6, 0.3 out.
        Span<(Vector3 Dir, float Out)> nozzles = [(x, 0.55f), (-x, 0.55f), (z, 0.6f), (-z, 0.6f)];
        for (var n = 0; n < nozzles.Length; n++)
        {
            var dir = Vector3.Normalize(nozzles[n].Dir);
            var mid = o + y * (0.3f * k) + dir * (0.2f * k); // (its box inside the quad's block, under its hull)
            var ne = new Element
            {
                Id = $"{e.Id}.n{n}", Kind = "nozzle", Name = $"{p.Kind} nozzle", Fn = "thrust", Exhaust = dir, ThrustAt = o + y * (0.3f * k) + dir * (nozzles[n].Out * k),
                Force = RcsNozzleN * k * k, Mouth = (n < 2 ? 0.08f : 0.1f) * k, Kg = e.Kg * NozzleShare, BoxMin = mid - new Vector3(0.06f * k), BoxMax = mid + new Vector3(0.06f * k),
            };
            e.Kg -= ne.Kg;
            els.Add(ne);
            Weld(els, a.At, els.Count - 1);
        }
    }

    /// <summary>A seat is sat in, facing the way its place faces.</summary>
    private static void Seat(in PieceAt a)
    {
        var (p, e, s) = (a.P, a.E, a.S);
        e.Fn = "seat";
        e.Role = p.Role;
        e.Kg = SeatKg * s;
        e.SeatAt = a.O + a.Z * (0.3f * s) + a.Y * 0.004f; // (the sitter's feet, at the seat pan's front edge)
        e.Rot = Facing(-a.Z, a.Y); // (a seat's sitter faces its part's -z: Construct.SeatForward)
        e.SeatTurn = TurnPast(e.Rot, a.Z); // (and the rest of the designer's free turn, about its up: ConstructPartDef.SeatTurn)
    }

    /// <summary>
    /// The yaw (radians, about the part's +y) that turns a part already turned by <paramref name="rot"/> so its -z points along
    /// <paramref name="ahead"/> (the ship's frame), as near as a turn about its up can: within 45 degrees, since Rot24 is the nearest.
    /// </summary>
    private static float TurnPast(byte rot, Vector3 ahead)
    {
        var v = Vector3.Transform(Vector3.Normalize(ahead), Quaternion.Conjugate(Rot24.Quat(rot))); // (where its sitter should look, in the part's frame)
        return v.X * v.X + v.Z * v.Z < 1e-8f ? 0 : MathF.Atan2(-v.X, -v.Z); // (a turn of t about +y takes -z to (-sin t, 0, -cos t))
    }

    /// <summary>
    /// The flight computer: a design with thrust and a seat gets one Flight Stabilizer of its own (a hidden element inside its helm, else its
    /// first seat), so the helm flies it with nothing wired: it finds the drives and the nozzles as it finds any thrusters.
    /// </summary>
    private static void Flight(List<Element> els)
    {
        if (!els.Any(x => x.Fn == "thrust"))
            return;
        var seat = els.FindIndex(x => x.Fn == "seat" && x.Role == "Helm");
        if (seat < 0)
            seat = els.FindIndex(x => x.Fn == "seat");
        if (seat < 0)
            return;
        var at = ((els[seat].BoxMin + els[seat].BoxMax) * 0.5f) with { Y = els[seat].BoxMin.Y + 0.2f };
        els.Add(new Element { Id = "flight", Kind = "flight", Name = "flight computer", Fn = "flight", Kg = FlightKg, BoxMin = at - new Vector3(0.08f), BoxMax = at + new Vector3(0.08f) });
        Weld(els, seat, els.Count - 1);
    }

    private static ConstructPartDef ThrustStart(Element e)
    {
        var d = ThrustTemplate.ShallowCopy();
        d.ThrustFuel = EngineFuel.None; // (unfed: a design carries no tanks yet; ConstructPhysics.ThrusterLive)
        d.ThrustPower = 0;
        return d;
    }

    private static void ThrustFinish(Element e, ConstructPartDef def)
    {
        // Pushes away from its exhaust, exactly (Construct.BuildThrusters reads ThrustExhaust); its flame along the nearest axis.
        def.ThrustExhaust = Vector3.Normalize(e.Exhaust);
        def.ThrustDir = NearestDir(def.ThrustExhaust);
        def.ThrustAt = e.ThrustAt;
        def.ThrustForce = e.Force;
        def.ThrustSpool = e.Kind == "nozzle" ? 0.15f : 0.5f;
        // Its flame from its mouth, not its force (a part file's sqrt(F) rule made a 5 MN nozzle's jet 4.5 m wide and 29 m long
        // from a 12 cm nozzle): a drive's a long plume, a nozzle's a short puff.
        var r = 0.9f * e.Mouth;
        def.Flames = [new FlameDef { At = e.ThrustAt, Dir = def.ThrustDir, Radius = r, Length = r * (e.Kind == "nozzle" ? 5f : 4f), Kind = FxKind.Rocket, Source = FlameSource.Thrust }];
    }

    private static void SeatFinish(Element e, ConstructPartDef def)
    {
        def.SeatAt = e.SeatAt;
        def.SeatTurn = e.SeatTurn == 0 ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(Vector3.UnitY, e.SeatTurn);
        var (station, helm) = DesignSeats.StationOf(e.Role);
        def.Helm = helm;
        def.DefaultSetting = (byte)station; // (its Station setting: CockpitStations)
    }

    private static ConstructPartDef FlightStart(Element e)
    {
        // The Flight Stabilizer itself, on Ship keys (its keys setting's default; the rest of its settings as the part's).
        var d = ConstructParts.Need("Flight Stabilizer").ShallowCopy();
        d.Config = [.. d.Config.Select(c => c.Id == CfgKey.Keys ? c.Copy(dflt: (float)FlightPattern.Ship) : c)];
        d.Gyro = 0; // (it turns the ship by its RCS)
        d.Panels = [];
        return d;
    }

    /// <summary>The nearest of the six directions to <paramref name="v"/> (ConstructParts.DirVector's index).</summary>
    private static byte NearestDir(Vector3 v)
    {
        byte best = 0;
        var most = float.MinValue;
        for (byte k = 0; k < 6; k++)
            if (Vector3.Dot(ConstructParts.DirVector(k), v) is var dot && dot > most)
                (most, best) = (dot, k);
        return best;
    }
}
