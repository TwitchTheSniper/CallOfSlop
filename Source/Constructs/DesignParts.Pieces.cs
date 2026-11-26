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
            var one = DesignScene.PieceMesh(d, p, copies: false);
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
                    e.Mesh.AddTri(m, tr, Vector3.One, new Vector3(float.NaN));
                e.Area = e.Mesh.Area;
                e.Kg = e.Area * PieceKgPerM2;
                HullOf(e, m.P);
                var at = els.Count;
                els.Add(e);
                if (st.BandAt(p.Hull, sets[k].Copy, (e.BoxMin.Z + e.BoxMax.Z) * 0.5f) is var b and >= 0)
                    Weld(els, b, at);
                Works(new PieceAt(p, e, at, sets[k].W, els));
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
