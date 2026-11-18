using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The shuttle, the Zero Day's boarding pod (Karl, 2026-11-17: "Add a boarding shuttle to the Day Zero", his pick of four: a
/// reusable hull-cutting pod on the spine, first called the Limpet and renamed the Shuttle when he saw it; docs/sandbox/constructs/shuttle.md). About 10 m long and 4 m across: a faceted wedge in
/// the IT style's paint, a cockpit for its pilot and a cabin for four boarders that runs to the stern, where its two drives'
/// engines (a Drive Core XS each) stand on the floor. It rides a Docking Clamp in a cradle on the Zero Day's roof, its docking
/// hatch in the cabin floor over the Zero Day's own on the cradle: both open while the clamp holds it (ConstructDoors.StepDocks).
/// It can also set its four hull grapples down on a ship's hull and grip it (ConstructBreach.cs: it rides on that body from then
/// on); the boarders open the hatch by hand. (Karl, 2026-11-17: the breaching cutter came out for the docking hatch.)
///
/// Built like the Zero Day, of lofted, faceted pieces welded together, rooms inside a separate hull: the belly (the floor, the keel,
/// the skids), a hull side each (the beam facets, the rooms' side walls, the lift sponsons and the tanks the jets draw on), the roof
/// (the ceiling), the nose (its bulkhead, armour and the sensor blister), the stern (the aft wall and the drive's mount) and the
/// cockpit's bulkhead. The pod's own frame: x across from its centreline, y up from the grapples' feet, z from its middle back
/// (forward is -z). It reuses the Zero Day's drawing helpers (TorpedoFrigateParts.cs) and its paint.
/// </summary>
public sealed partial class TorpedoFrigateParts
{
    public const string ShuttleName = "Shuttle";
    private const string LSection = "Shuttle";

    // The plan (pod frame).
    private const double LFt = 0.65, LFloor0 = 0.4, LCeil = 2.7, LCeilTop = 2.8; // the floor's top and underside, the ceiling and its top
    private const double LNoseZ = -2.6, LBulkZ = -1.0, LAftZ = 4.7; // the nose bulkhead (the cockpit's front), the cockpit's back, the cabin's aft wall (the stern's mount plate, 0.3 m to the stern: Karl, 2026-11-17, the room touching the back so the engines can be maintained)
    private const double LEngX = 0.7; // the two drives' (and their engines') axes either side of the centreline
    private static double LEngY => LFt + ShipWorksParts.AxisOver(ShipWorksParts.XS) + 0.01; // the axes' height: a Drive Core XS's saddles 1 cm over the cabin floor (a place rounds to the centimetre)
    private const double LCockW = 0.95, LCabW = 1.25, LWallT = 0.1; // the cockpit's and the cabin's inner half widths, a wall's thickness
    private static readonly Rect LHatch = new(-0.65, -0.65, 0.65, 0.65); // the belly hatch the cutter rings (the hole in the floor)
    private const double LHatchIn = 0.5; // the hatch's clear half width inside the cutter's ring
    private static readonly Rect LHole = new(-LHatchIn, -LHatchIn, LHatchIn, LHatchIn); // the hole in the floor: the ring's inside (the ring stands under the floor round it)

    // The hull: stations of a half profile (P0 the keel's edge, P1 the lower chine, P2 the beam, P3 the upper chine, P4 the roof's edge).
    private static readonly (double, double)[] LMid = [(1.2, 0.3), (1.95, 0.6), (2.1, 1.45), (1.8, 2.6), (1.0, 3.05)];

    private static readonly Sec[] LHull =
    [
        new(-5.0, [(0.3, 0.75), (0.45, 0.95), (0.55, 1.35), (0.45, 1.7), (0.25, 1.85)]),
        new(-3.4, [(0.95, 0.4), (1.55, 0.65), (1.8, 1.4), (1.6, 2.45), (0.9, 2.85)]),
        new(-1.8, LMid), new(3.2, LMid),
        new(5.0, [(1.1, 0.4), (1.85, 0.65), (2.0, 1.45), (1.8, 2.7), (1.05, 3.1)]),
    ];

    private const double LTip = -5.0, LStern = 5.0;

    private static (double H, double Y)[] LAt(double z)
    {
        if (z <= LHull[0].Z)
            return LHull[0].Pts;
        for (var i = 0; i < LHull.Length - 1; i++)
            if (z <= LHull[i + 1].Z)
            {
                var t = (z - LHull[i].Z) / (LHull[i + 1].Z - LHull[i].Z);
                return [.. Enumerable.Range(0, 5).Select(k => (LHull[i].Pts[k].H + (LHull[i + 1].Pts[k].H - LHull[i].Pts[k].H) * t, LHull[i].Pts[k].Y + (LHull[i + 1].Pts[k].Y - LHull[i].Pts[k].Y) * t))];
            }
        return LHull[^1].Pts;
    }

    private static double LSkinHalf(double z, double y)
    {
        var q = LAt(z);
        if (y < q[0].Y || y > q[4].Y)
            return 0;
        for (var i = 0; i < 4; i++)
            if (y <= q[i + 1].Y)
                return q[i].H + (q[i + 1].H - q[i].H) * (y - q[i].Y) / (q[i + 1].Y - q[i].Y);
        return q[4].H;
    }

    private static void LMustFit(string what, double z, double hx, double y)
    {
        var s = LSkinHalf(z, y);
        if (s < hx + 0.06)
            throw new InvalidOperationException($"Shuttle: {what} at z {z}, y {y} is {hx:0.00} from the centreline and the skin {s:0.00}");
    }

    private static (double[] A, double[] B) LEdge(int k, int s, double z)
    {
        var q = LAt(z);
        double[] V((double H, double Y) p, int side) => [side * p.H, p.Y, z];
        return k switch
        {
            0 => (V(q[0], -1), V(q[0], 1)),
            5 => (V(q[4], 1), V(q[4], -1)),
            _ => (V(q[k - 1], s), V(q[k], s)),
        };
    }

    private static double[] LOn(int k, int s, double za, double zb, double u, double v)
    {
        var (a0, b0) = LEdge(k, s, za);
        var (a1, b1) = LEdge(k, s, zb);
        return Lerp(Lerp(a0, b0, v), Lerp(a1, b1, v), u);
    }

    private static double[] LNormal(int k, int s, double za, double zb)
    {
        var c = LOn(k, s, za, zb, 0.5, 0.5);
        var n = Unit(Cross(Sub(LOn(k, s, za, zb, 0.5, 1), LOn(k, s, za, zb, 0.5, 0)), Sub(LOn(k, s, za, zb, 1, 0.5), LOn(k, s, za, zb, 0, 0.5))));
        double[] axis = [0, 1.5, c[2]];
        return Dot(n, Sub(c, axis)) < 0 ? [-n[0], -n[1], -n[2]] : n;
    }

    /// <summary>The skin of facet k on side s from za to zb (the Zero Day's staggered ends: z-fighting.md).</summary>
    private static void LFacet(Part p, int k, int s, double za, double zb, string color, string finish = "painted plating")
    {
        var dz = k % 2 == 1 ? FacetStagger : 0;
        double[] At2(double u, double v) { var q = LOn(k, s, za, zb, u, v); return Sh([q[0], q[1], q[2] + dz + (u < 0.5 ? FacetTrim : -FacetTrim)]); }
        Quad(p, At2(0, 0), At2(0, 1), At2(1, 1), At2(1, 0), color, finish);
    }

    private static void LSlab(Part p, int k, int s, double za, double zb, double u0, double u1, double v0, double v1, double d0, double d1, double bev, string color, string finish = "plated steel")
    {
        var n = LNormal(k, s, za, zb);
        var lu = zb - za;
        var lv = Math.Sqrt(Dot(Sub(LOn(k, s, za, zb, 0.5, 1), LOn(k, s, za, zb, 0.5, 0)), Sub(LOn(k, s, za, zb, 0.5, 1), LOn(k, s, za, zb, 0.5, 0))));
        double bu = bev / lu, bv = bev / Math.Max(lv, 0.1);
        double[] Off(double[] q, double d) => [q[0] + n[0] * d, q[1] + n[1] * d, q[2] + n[2] * d];
        double[][] inner = [.. new[] { (u0, v0), (u1, v0), (u1, v1), (u0, v1) }.Select(t => Sh(Off(LOn(k, s, za, zb, t.Item1, t.Item2), d0)))];
        double[][] outer = [.. new[] { (u0 + bu, v0 + bv), (u1 - bu, v0 + bv), (u1 - bu, v1 - bv), (u0 + bu, v1 - bv) }.Select(t => Sh(Off(LOn(k, s, za, zb, t.Item1, t.Item2), d1)))];
        Prism(p, inner, outer, color, finish);
    }

    private static void LStrip(Part p, int k, int s, double za, double zb, double u0, double u1, double v0, double v1, double d, string color, string finish = "painted plating", bool glow = false)
    {
        var n = LNormal(k, s, za, zb);
        double[] Q(double u, double v)
        {
            var q = LOn(k, s, za, zb, u, v);
            return Sh([q[0] + n[0] * d, q[1] + n[1] * d, q[2] + n[2] * d]);
        }
        Plate(p, [Q(u0, v0), Q(u1, v0), Q(u1, v1), Q(u0, v1)], color, finish, glow: glow);
    }

    /// <summary>A chine rail along profile point i (1..3) on side s from za to zb.</summary>
    private static void LChine(Part p, int i, int s, double za, double zb, double w, double h, string color)
    {
        double[][] Ring(double z)
        {
            var q = LAt(z);
            double[] c = [s * q[i].H, q[i].Y, z], a = [s * q[i - 1].H, q[i - 1].Y, z], b = [s * q[i + 1].H, q[i + 1].Y, z];
            var da = Unit(Sub(a, c));
            var db = Unit(Sub(b, c));
            var n = Unit([-(da[0] + db[0]), -(da[1] + db[1]), 0]);
            double[] Pt(double[] d1, double t1, double t2) => Sh([c[0] + d1[0] * t1 + n[0] * t2, c[1] + d1[1] * t1 + n[1] * t2, z]);
            return [Pt(da, w, 0.01), Pt(da, w * 0.45, h), Pt(db, w * 0.45, h), Pt(db, w, 0.01)];
        }
        var ra = Ring(za);
        var rb = Ring(zb);
        foreach (var q in ra)
            q[2] += FacetStagger / 2 + FacetTrim;
        foreach (var q in rb)
            q[2] += FacetStagger / 2 - FacetTrim;
        Prism(p, ra, rb, color, "plated steel");
    }

    /// <summary>Armour panels on a facet band, about 1.6 m long, the pattern picked by facet and panel (both sides match).</summary>
    private static void LPlates(Part p, int k, int s, double za, double zb, int salt, double v0 = 0.08, double v1 = 0.92)
    {
        var n = Math.Max(1, (int)Math.Round((zb - za) / 1.6));
        for (var i = 0; i < n; i++)
        {
            double u0 = (double)i / n + 0.025 / (zb - za) * 4, u1 = (double)(i + 1) / n - 0.025 / (zb - za) * 4;
            switch ((k * 7 + i * 3 + salt * 5) % 5)
            {
                case 0:
                case 3:
                    LSlab(p, k, s, za, zb, u0, u1, v0, v1, 0.015, 0.09, 0.05, Armour);
                    break;
                case 1:
                    LSlab(p, k, s, za, zb, u0, u1, v0, v1, 0.015, 0.08, 0.05, Armour2);
                    LSlab(p, k, s, za, zb, u0 + (u1 - u0) * 0.2, u1 - (u1 - u0) * 0.15, v0 + 0.15, v1 - 0.2, 0.08, 0.13, 0.03, Armour);
                    break;
                case 2:
                    LSlab(p, k, s, za, zb, u0, u1, v0, v1, 0.015, 0.06, 0.04, Blue, "painted plating");
                    break;
                default:
                    LStrip(p, k, s, za, zb, (u0 + u1) / 2 - 0.004, (u0 + u1) / 2 + 0.004, v0, v1, 0.02, Frame, "dirty steel");
                    break;
            }
        }
    }

    /// <summary>Hidden boxes for the void from xin (0: across the middle) out to the skin less a margin, y and z bands.</summary>
    private static void LVoid(Part p, double za, double zb, double ya, double yb, double xin)
    {
        var xo = Math.Floor((new[] { LSkinHalf(za, ya), LSkinHalf(za, yb), LSkinHalf(zb, ya), LSkinHalf(zb, yb) }.Min() - 0.06) * 100) / 100;
        if (xo < xin + 0.08)
            return;
        za += 0.004;
        zb -= 0.004;
        if (xin <= 0)
            Hidden(p, -xo, ya, za, xo, yb, zb);
        else
            foreach (var s in new[] { -1, 1 })
                Hidden(p, Math.Min(s * xin, s * xo), ya, za, Math.Max(s * xin, s * xo), yb, zb);
    }

    private static Part LBig(string name, string desc, string role = "frame", string color = Skin) =>
        new(name, Small, LSection, desc, role: role, tool: "welder", material: "steel", color: color, group: Group);

    private static readonly (double Za, double Zb)[] LBody = [(LNoseZ, -1.8), (-1.8, 3.2), (3.2, LAftZ)]; // (the bands along the rooms, inside stations)

    // ---- the pieces

    /// <summary>The belly: the floor (with the hatch the cutter rings), the keel and the lower chines, the skids and the void under the floor.</summary>
    private static Part LBelly()
    {
        var p = LBig("Shuttle Belly", "The shuttle's belly: the cabin's and the cockpit's floor, the keel, the skids between its hull grapples and the opening its docking hatch closes.", color: Armour);
        // The floor, round the hatch: the cockpit's narrower than the cabin's (the hull narrows forward).
        var floorW = Math.Floor((Math.Min(LSkinHalf(LNoseZ, LFloor0), LSkinHalf(LNoseZ, LFt)) - 0.07) * 100) / 100;
        var cabW = Math.Floor((Math.Min(LSkinHalf(LAftZ, LFloor0), LSkinHalf(LAftZ, LFt)) - 0.07) * 100) / 100;
        // (the docking hatch's pocket: the floor there is a plate under it and a skin over it, the lid sliding aft between them)
        var pocket = new Rect(-DockHatchHalf - 0.01, -DockHatchHalf - 0.01, DockHatchHalf + 0.01, DockHatchHalf + DockHatchTravel + 0.01);
        foreach (var r in Minus(Minus([new Rect(-floorW, LNoseZ, floorW, LBulkZ), new Rect(-cabW, LBulkZ, cabW, LAftZ)], LHole), pocket))
            Solid(p, r.X0, LFloor0, r.Z0, r.X1, LFt, r.Z1, Deck, "painted deck plate");
        foreach (var r in Minus([pocket], LHole))
        {
            Solid(p, r.X0, LFloor0, r.Z0, r.X1, LFt - DockHatchTop, r.Z1, Deck, "painted deck plate");
            Solid(p, r.X0, LFt - DockSkin, r.Z0, r.X1, LFt, r.Z1, Deck, "painted deck plate");
        }
        // The keel's flat and the lower chines (facets 0 and 1), with the hatch's mouth cut through the flat.
        foreach (var (za, zb) in LBody)
        {
            if (za <= LHatch.Z0 && zb >= LHatch.Z1)
            {
                var q = LAt(za);
                double y = q[0].Y, hb = q[0].H;
                var a = LHatch;
                Plate(p, [P(-hb, y, za), P(hb, y, za), P(hb, y, a.Z0), P(-hb, y, a.Z0)], Armour, "plated steel");
                Plate(p, [P(-hb, y, a.Z1), P(hb, y, a.Z1), P(hb, y, zb), P(-hb, y, zb)], Armour, "plated steel");
                Plate(p, [P(-hb, y, a.Z0), P(a.X0, y, a.Z0), P(a.X0, y, a.Z1), P(-hb, y, a.Z1)], Armour, "plated steel");
                Plate(p, [P(a.X1, y, a.Z0), P(hb, y, a.Z0), P(hb, y, a.Z1), P(a.X1, y, a.Z1)], Armour, "plated steel");
            }
            else
                LFacet(p, 0, 1, za, zb, Armour, "plated steel");
            foreach (var s in new[] { -1, 1 })
            {
                LFacet(p, 1, s, za, zb, Skin2, "painted plating");
                LPlates(p, 1, s, za, zb, 1);
            }
        }
        // The void under the floor, round the hatch's shaft.
        var shaft = new Rect(LHatch.X0 - 0.015, LHatch.Z0 - 0.015, LHatch.X1 + 0.015, LHatch.Z1 + 0.015); // (the lining plates 4 cm thick about it: their faces either side of the keel plate's edge)
        foreach (var (za, zb) in LBody)
        {
            var y0 = Math.Max(LAt(za)[0].Y, LAt(zb)[0].Y) + 0.02;
            var xo = new[] { LSkinHalf(za, y0), LSkinHalf(zb, y0), LSkinHalf(za, LFloor0), LSkinHalf(zb, LFloor0) }.Min() - 0.06;
            foreach (var r in Minus([new Rect(-xo, za + 0.004, xo, zb - 0.004)], shaft))
                Hidden(p, r.X0, y0, r.Z0, r.X1, LFloor0, r.Z1);
        }
        // The hatch's lining down to the keel and its hazard lip on the floor (open at its back edge, where the ladder lands people).
        var hy = LMid[0].Item2;
        foreach (var (x0, z0, x1, z1) in new[] { (shaft.X0, shaft.Z0, shaft.X1, shaft.Z0), (shaft.X0, shaft.Z1, shaft.X1, shaft.Z1), (shaft.X0, shaft.Z0 + 0.02, shaft.X0, shaft.Z1 - 0.02), (shaft.X1, shaft.Z0 + 0.02, shaft.X1, shaft.Z1 - 0.02) })
            Plate(p, [P(x0, hy + 0.01, z0), P(x1, hy + 0.01, z1), P(x1, LFloor0 - 0.01, z1), P(x0, LFloor0 - 0.01, z0)], Grate, "dirty steel");
        Detail(p, LHole.X0 - 0.1, LFt, LHole.Z0 - 0.1, LHole.X1 + 0.1, LFt + 0.012, LHole.Z0, Hazard, "hazard paint");
        Detail(p, LHole.X0 - 0.1, LFt, LHole.Z0, LHole.X0, LFt + 0.012, LHole.Z1, Hazard, "hazard paint");
        Detail(p, LHole.X1, LFt, LHole.Z0, LHole.X1 + 0.1, LFt + 0.012, LHole.Z1, Hazard, "hazard paint");
        // (no rail round it: Karl, 2026-11-17, the docking hatch slides shut in the floor, flush, and is walked over)
        // The runner down the cabin and the cockpit.
        Detail(p, -0.4, LFt, LHole.Z1 + 0.05, 0.4, LFt + 0.01, LAftZ - 0.05, "#30353b", "rubber matting");
        Detail(p, -0.4, LFt, LNoseZ + 0.05, 0.4, LFt + 0.01, LHole.Z0 - 0.12, "#30353b", "rubber matting");
        // The skids: a dark rail each side under the keel between the grapples' pads, their feet 2 cm off the pads' soles.
        foreach (var s in new[] { -1, 1 })
        {
            Solid(p, s * 0.95, 0.02, -1.9, s * 0.75, hy, 3.3, Frame, "dirty steel");
            Detail(p, s * 0.96, 0.12, -1.85, s * 0.74, 0.16, 3.25, Hazard, "hazard paint");
        }
        // Sockets: the docking hatch in the floor, the grapples' pads at the skids' ends, the pilot and the flight computer, the
        // boarders' seats, the lockers and the batteries.
        // The docking hatch lies in the floor (Karl, 2026-11-17: "a sliding hatch underneath so that you can walk over it"): its lid
        // 4 mm under the floor's top over the opening, sliding aft inside the floor plate. It hangs by its shoe on a block in the
        // floor ahead of the opening.
        Sock(p, "hatch", 0, LFt - DockHatchTop, -DockHatchHang, "+y"); // (on the pocket's floor plate ahead of the opening)
        foreach (var (s, z, n) in new[] { (-1, -2.2, "gripFL"), (1, -2.2, "gripFR"), (-1, 3.6, "gripBL"), (1, 3.6, "gripBR") })
        {
            // Each grapple's mount: a block under the keel between the void and the pad (the keel rises toward the nose).
            var (za, zb) = LBody.First(b => z >= b.Za && z < b.Zb);
            var top = Math.Max(LAt(za)[0].Y, LAt(zb)[0].Y) + 0.02;
            Solid(p, s * 0.6, hy, z - 0.25, s * 1.1, top, z + 0.25, Frame, "dirty steel");
            Sock(p, n, s * 0.85, hy, z, "-y");
        }
        Sock(p, "pilot", 0, LFt, -1.5, "+y");
        Sock(p, "computer", 0, LFt, -2.3, "+y");
        // (facing in, a couch's back stands 0.4 m out from its middle: 0.74 keeps it off the cabin's side wall and leaves a 0.8 m aisle)
        foreach (var (s, z, n) in new[] { (-1, 1.5, "seatL0"), (-1, 2.5, "seatL1"), (1, 1.5, "seatR0"), (1, 2.5, "seatR1") })
            Sock(p, n, s * 0.74, LFt, z, "+y");
        // (the lockers stand against the side walls ahead of the seats, facing in, Karl's ask; the batteries between the seats and the engines)
        Sock(p, "lockerL", -(LCabW - 0.3), LFt, 0.45, "+y"); // (Space Suit Lockers, 0.6 m deep: backs to the walls)
        Sock(p, "lockerR", LCabW - 0.3, LFt, 0.45, "+y");
        Sock(p, "battery", -(LCabW - 0.17), LFt, 3.3, "+y"); // (its Ship Battery XS, back to the port wall: Karl, 2026-11-18)
        foreach (var z in new[] { LNoseZ, LBulkZ })
            LMustFit("cockpit floor", z, floorW, LFt);
        foreach (var z in new[] { LBulkZ, LAftZ })
            LMustFit("cabin floor", z, cabW, LFt);
        return p.Stats(1600, 7000, 30, 12.0);
    }

    /// <summary>A hull side: the beam facets and their armour, the rooms' side walls, the void between, a lift sponson fore and aft
    /// (the jets' and the attitude jets' mounts) and the fuel they draw (each side a tank).</summary>
    private static Part LSide(int s)
    {
        var n = s < 0 ? "Port" : "Starboard";
        var p = LBig($"Shuttle Hull {n}", $"The shuttle's {n.ToLowerInvariant()} side: the beam's armour, the side wall of the cockpit and the cabin, a lift sponson fore and aft and the fuel tank in the void between.", "fueltank");
        foreach (var (za, zb) in LBody)
        {
            LFacet(p, 2, s, za, zb, Skin, "painted plating");
            LFacet(p, 3, s, za, zb, Skin2, "painted plating");
            LPlates(p, 2, s, za, zb, 2, 0.1, 0.9);
            LPlates(p, 3, s, za, zb, 3);
            LChine(p, 2, s, za, zb, 0.18, 0.08, Armour);
            LStrip(p, 3, s, za, zb, 0.03, 0.97, 0.05, 0.075, 0.02, Glow, glow: true);
        }
        // The side walls: the cockpit's in from the cabin's, a step between them at the bulkhead.
        double xc = s * LCockW, xb = s * LCabW;
        Solid(p, Math.Min(xc, xc + s * LWallT), LFt, LNoseZ + 0.004, Math.Max(xc, xc + s * LWallT), LCeil, LBulkZ, Wall, "painted plating");
        Solid(p, Math.Min(xc + s * LWallT, xb + s * LWallT), LFt, LBulkZ - LWallT, Math.Max(xc + s * LWallT, xb + s * LWallT), LCeil, LBulkZ, Wall, "painted plating"); // (the step out to the cabin's wall, the bulkhead's back face)
        Solid(p, Math.Min(xb, xb + s * LWallT), LFt, LBulkZ, Math.Max(xb, xb + s * LWallT), LCeil, LAftZ - 0.004, Wall, "painted plating");
        Detail(p, Math.Min(xb, xb - s * 0.012), LFt + 0.003, LBulkZ + 0.05, Math.Max(xb, xb - s * 0.012), LFt + 0.7, LAftZ - 0.05, WallLow, "painted plating");
        Detail(p, Math.Min(xc, xc - s * 0.012), LFt + 0.003, LNoseZ + 0.05, Math.Max(xc, xc - s * 0.012), LFt + 0.7, LBulkZ - 0.05, WallLow, "painted plating");
        // The camera screens in the cockpit, a link-blue light along the cabin's wall under the ceiling.
        Detail(p, Math.Min(xc, xc - s * 0.016), LFt + 1.1, -2.3, Math.Max(xc, xc - s * 0.016), LFt + 1.55, -1.5, "#101830", glow: true);
        Detail(p, Math.Min(xc, xc - s * 0.02), LFt + 1.15, -2.25, Math.Max(xc, xc - s * 0.02), LFt + 1.5, -1.55, Screen, glow: true);
        Detail(p, Math.Min(xb, xb - s * 0.02), LCeil - 0.12, LBulkZ + 0.2, Math.Max(xb, xb - s * 0.02), LCeil - 0.08, LAftZ - 0.2, "#d8f0ff", glow: true);
        // The void between the walls and the skin (the tank).
        foreach (var (za, zb) in LBody)
        {
            var xin = za < LBulkZ ? LCockW + LWallT : LCabW + LWallT;
            if (za < LBulkZ && zb > LBulkZ)
            {
                LVoidSide(p, s, za, LBulkZ - LWallT, LFt, LCeil, LCockW + LWallT);
                LVoidSide(p, s, LBulkZ, zb, LFt, LCeil, LCabW + LWallT);
            }
            else
                LVoidSide(p, s, za, zb, LFt, LCeil, xin);
        }
        // The lift sponsons on the lower chine: a faceted block standing out of the beam, a lift jet under it, attitude jets round it.
        foreach (var (z, tag) in new[] { (-2.0, "F"), (3.4, "B") })
        {
            var q = LAt(z);
            double x0 = s * (q[2].H - 0.25), x1 = s * (q[2].H + 0.42), y0 = 0.72, y1 = 1.32, h = 0.42;
            double[][] Sp(double zz, double inset) => [P(x0, y0 + inset, zz), P(x1 - s * inset, y0 + inset, zz), P(x1 - s * inset, y1 - inset - 0.1, zz), P(x1 - s * 0.2, y1 - inset, zz), P(x0, y1 - inset, zz)];
            Prism(p, Sp(z - h - 0.2, 0.12), Sp(z - h, 0), Armour2, "plated steel");
            Prism(p, Sp(z - h + 0.005, 0), Sp(z + h - 0.005, 0), Armour, "plated steel");
            Prism(p, Sp(z + h, 0), Sp(z + h + 0.25, 0.14), Armour2, "plated steel");
            Detail(p, Math.Min(x1, x1 + s * 0.004), y0 + 0.2, z - 0.3, Math.Max(x1, x1 + s * 0.004), y0 + 0.26, z + 0.3, Hazard, "hazard paint");
            Hidden(p, Math.Min(s * (q[2].H - 0.2), x1), y0, z - h - 0.2, Math.Max(s * (q[2].H - 0.2), x1), y1, z + h + 0.25);
            Sock(p, $"lift{tag}", s * (q[2].H + 0.1), y0, z, "-y");
            Sock(p, $"rcsUp{tag}", s * (q[2].H + 0.1), y1, z, "+y");
            Sock(p, $"rcsOut{tag}", x1, (y0 + y1) / 2, z, s < 0 ? "-x" : "+x");
            Sock(p, $"rcsEnd{tag}", s * (q[2].H + 0.1), (y0 + y1) / 2, tag == "F" ? z - h - 0.2 : z + h + 0.25, tag == "F" ? "-z" : "+z");
        }
        // The cockpit's and the cabin's lamps and switches on this wall.
        Sock(p, "lampCockpit", xc, LCeil - 0.4, -1.9, s < 0 ? "+x" : "-x");
        Sock(p, "lampCabinF", xb, LCeil - 0.4, -0.35, s < 0 ? "+x" : "-x"); // (ahead of the locker on this wall)
        Sock(p, "lampCabinB", xb, LCeil - 0.4, 3.4, s < 0 ? "+x" : "-x");
        Sock(p, "switchA", xc, LFt + 1.25, -1.25, s < 0 ? "+x" : "-x");
        Sock(p, "switchB", xc, LFt + 1.0, -1.25, s < 0 ? "+x" : "-x");
        Sock(p, "switchCabin", xb, LFt + 1.3, 0.95, s < 0 ? "+x" : "-x");
        foreach (var (z, xh) in new[] { (LNoseZ, LCockW + LWallT), (LBulkZ, LCockW + LWallT), (LBulkZ, LCabW + LWallT), (LAftZ, LCabW + LWallT) })
        {
            LMustFit("side wall", z, xh, LFt);
            LMustFit("side wall", z, xh, LCeil);
        }
        p.Set(("fuelTank", Kw(("capacity", 600))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(s * 1.5, 1.0, LAftZ - 0.004).Cast<object>()], "+z", "fuel"))));
        return p.Stats(1150, 6500, 30, 12.0);
    }

    private static void LVoidSide(Part p, int s, double za, double zb, double ya, double yb, double xin)
    {
        var xo = Math.Floor((new[] { LSkinHalf(za, ya), LSkinHalf(za, yb), LSkinHalf(zb, ya), LSkinHalf(zb, yb) }.Min() - 0.06) * 100) / 100;
        if (xo < xin + 0.08)
            return;
        Hidden(p, Math.Min(s * xin, s * xo), ya, za + 0.004, Math.Max(s * xin, s * xo), yb, zb - 0.004);
    }

    /// <summary>The roof: the upper facets and the top, the ceiling over both rooms with its light strip, the void over it, the dorsal
    /// armour and the lifting eyes.</summary>
    private static Part LRoof()
    {
        var p = LBig("Shuttle Roof", "The shuttle's roof: the upper armour and the top, the ceiling over the cockpit and the cabin and its light strip.", color: Armour);
        foreach (var (za, zb) in LBody)
        {
            LFacet(p, 5, 1, za, zb, Armour, "plated steel");
            LSlab(p, 5, 1, za, zb, 0.05, 0.95, 0.06, 0.32, 0.015, 0.08, 0.04, Armour2);
            LSlab(p, 5, 1, za, zb, 0.05, 0.95, 0.68, 0.94, 0.015, 0.08, 0.04, Armour2);
            foreach (var s in new[] { -1, 1 })
            {
                LFacet(p, 4, s, za, zb, Skin, "painted plating");
                LPlates(p, 4, s, za, zb, 4);
                LChine(p, 3, s, za, zb, 0.16, 0.07, Armour2);
            }
        }
        // The ceiling: the cockpit's narrower, then the cabin's.
        Solid(p, -(LCockW + LWallT), LCeil, LNoseZ + 0.004, LCockW + LWallT, LCeilTop, LBulkZ, Ceil, "painted plating");
        Solid(p, -(LCabW + LWallT), LCeil, LBulkZ, LCabW + LWallT, LCeilTop, LAftZ - 0.004, Ceil, "painted plating");
        CeilStrip(p, -0.2, 0.2, LBulkZ + 0.2, LAftZ - 0.3, LCeil);
        CeilStrip(p, -0.15, 0.15, LNoseZ + 0.2, LBulkZ - 0.2, LCeil);
        // The void over the ceiling.
        foreach (var (za, zb) in LBody)
            LVoid(p, za, zb, LCeilTop, Math.Min(LAt(za)[4].Y, LAt(zb)[4].Y) - 0.03, 0);
        // A raised dorsal spine down the middle (the antenna run), two lifting eyes, a red beacon aft.
        double Top(double z) => LAt(z)[4].Y;
        double[][] Sect(double z, double w, double h) => [P(-w, Top(z) - 0.01, z), P(-w * 0.7, Top(z) + h, z), P(w * 0.7, Top(z) + h, z), P(w, Top(z) - 0.01, z)];
        Prism(p, Sect(-2.4, 0.15, 0.05), Sect(-1.6, 0.32, 0.22), Armour2, "plated steel");
        Prism(p, Sect(-1.6 + 0.01, 0.32, 0.22), Sect(3.0, 0.32, 0.22), Armour2, "plated steel");
        Prism(p, Sect(3.0 + 0.01, 0.32, 0.22), Sect(4.2, 0.2, 0.06), Armour2, "plated steel");
        Detail(p, -0.33, Top(1) + 0.1, -1.4, -0.325, Top(1) + 0.14, 2.8, Glow, glow: true);
        Detail(p, 0.325, Top(1) + 0.1, -1.4, 0.33, Top(1) + 0.14, 2.8, Glow, glow: true);
        foreach (var z in new[] { -0.8, 2.4 })
            foreach (var s in new[] { -1, 1 })
            {
                Detail(p, s * 0.62 - 0.05, Top(z) + 0.004, z - 0.12, s * 0.62 + 0.05, Top(z) + 0.18, z - 0.08, Hazard, "hazard paint");
                Detail(p, s * 0.62 - 0.05, Top(z) + 0.004, z + 0.08, s * 0.62 + 0.05, Top(z) + 0.18, z + 0.12, Hazard, "hazard paint");
                Detail(p, s * 0.62 - 0.05, Top(z) + 0.18, z - 0.12, s * 0.62 + 0.05, Top(z) + 0.22, z + 0.12, Hazard, "hazard paint");
            }
        Detail(p, -0.08, Top(4.0) + 0.06, 3.92, 0.08, Top(4.0) + 0.16, 4.08, Red, glow: true);
        Detail(p, -0.015, Top(-0.2) + 0.22, -0.2, 0.015, Top(-0.2) + 1.1, -0.17, Frame, "steel");
        Detail(p, -0.025, Top(-0.2) + 1.1, -0.205, 0.025, Top(-0.2) + 1.15, -0.165, Red, glow: true);
        LMustFit("ceiling", LNoseZ, LCockW + LWallT, LCeilTop);
        LMustFit("ceiling", LAftZ, LCabW + LWallT, LCeilTop);
        return p.Stats(1000, 6000, 30, 12.0);
    }

    /// <summary>A cap across the hull at z: the profile's ten points as one plate, drawn at one end of the hull.</summary>
    private static void LCap(Part p, double z, string color, string finish)
    {
        var q = LAt(z);
        var pts = new List<double[]>();
        for (var i = 0; i < 5; i++)
            pts.Add(P(-q[i].H, q[i].Y, z));
        for (var i = 4; i >= 0; i--)
            pts.Add(P(q[i].H, q[i].Y, z));
        Plate(p, [.. pts], color, finish);
    }

    /// <summary>The nose: the chisel bow from its tip to the cockpit's front bulkhead, armoured, with the sensor blister under its
    /// chin and the cockpit's view screen on the bulkhead's inner face.</summary>
    private static Part LNose()
    {
        var p = LBig("Shuttle Nose", "The shuttle's nose: the armoured chisel bow, the sensor blister under its chin and the cockpit's front bulkhead with its view screen.", color: Armour);
        foreach (var (za, zb) in new[] { (LTip, -3.4), (-3.4, LNoseZ) })
        {
            LFacet(p, 0, 1, za, zb, Armour, "plated steel");
            LFacet(p, 5, 1, za, zb, Armour, "plated steel");
            foreach (var s in new[] { -1, 1 })
                for (var k = 1; k <= 4; k++)
                    LFacet(p, k, s, za, zb, k is 2 or 3 ? Skin : Armour, k is 2 or 3 ? "painted plating" : "plated steel");
            LSlab(p, 5, 1, za, zb, 0.08, 0.92, 0.1, 0.9, 0.015, 0.1, 0.06, Armour2);
            foreach (var s in new[] { -1, 1 })
            {
                LSlab(p, 3, s, za, zb, 0.06, 0.94, 0.1, 0.9, 0.015, 0.09, 0.05, za < -4 ? Armour2 : Armour);
                LSlab(p, 2, s, za, zb, 0.06, 0.94, 0.12, 0.88, 0.015, 0.06, 0.04, za < -4 ? Armour : Blue, za < -4 ? "plated steel" : "painted plating");
                LChine(p, 2, s, za, zb, 0.15, 0.07, Armour2);
            }
        }
        LCap(p, LTip, Armour2, "plated steel");
        Detail(p, -0.2, 1.2, LTip - 0.04, 0.2, 1.28, LTip - 0.012, Red, glow: true);
        // The sensor blister under the chin: a faceted pod, its lens.
        Prism(p, Ngon(0, LAt(-3.9)[0].Y + 0.004, -3.9, 0.28, 8), Ngon(0, LAt(-3.9)[0].Y - 0.16, -3.9, 0.18, 8), Armour2, "plated steel");
        Detail(p, -0.07, LAt(-3.9)[0].Y - 0.175, -4.0, 0.07, LAt(-3.9)[0].Y - 0.16, -3.86, Screen, glow: true);
        // The front bulkhead: the cockpit's front wall, and the void ahead of it out to the skin.
        Solid(p, -(LCockW + LWallT), LFloor0, LNoseZ - LWallT, LCockW + LWallT, LCeilTop, LNoseZ, Wall, "painted plating");
        Detail(p, -0.6, LFt + 1.0, LNoseZ - 0.016 + 0.0, 0.6, LFt + 1.75, LNoseZ + 0.004, "#101830", glow: true); // (the view screen: no glass, Karl)
        foreach (var (x, y) in new[] { (-0.45, 1.3), (-0.2, 1.6), (0.1, 1.2), (0.3, 1.55), (0.5, 1.15) })
            Detail(p, x, LFt + y, LNoseZ + 0.004, x + 0.025, LFt + y + 0.025, LNoseZ + 0.008, "#ffffff", glow: true);
        Detail(p, -0.62, LFt + 0.98, LNoseZ, 0.62, LFt + 1.0, LNoseZ + 0.02, WallTrim, "painted steel");
        foreach (var (za, zb) in new[] { (-4.6, -3.4), (-3.4, LNoseZ - LWallT) })
        {
            var ya = Math.Max(LAt(za)[0].Y, LAt(zb)[0].Y) + 0.03;
            var yb = Math.Min(LAt(za)[4].Y, LAt(zb)[4].Y) - 0.03;
            LVoid(p, za, zb, ya, yb, 0);
        }
        return p.Stats(900, 8000, 45, 12.0);
    }

    /// <summary>The stern: the last band of the hull and the cabin's aft wall, a 0.3 m mount plate the two drives bolt to outside
    /// and their engines (a Drive Core XS each, on the drive's axis) inside, standing on the cabin floor where a person reaches them.</summary>
    private static Part LSternPart()
    {
        var p = LBig("Shuttle Stern", "The shuttle's stern: the cabin's aft wall, an armoured plate its two drives bolt to outside and their engines inside, in the cabin.", "frame", Armour);
        var (za, zb) = (LAftZ, LStern);
        LFacet(p, 0, 1, za, zb, Armour, "plated steel");
        LFacet(p, 5, 1, za, zb, Armour, "plated steel");
        foreach (var s in new[] { -1, 1 })
            for (var k = 1; k <= 4; k++)
                LFacet(p, k, s, za, zb, k is 2 or 3 ? Skin : Armour2, k is 2 or 3 ? "painted plating" : "plated steel");
        LCap(p, LStern, Armour2, "plated steel");
        // The plate: the cabin's end wall, as thick as the band (it stops 4 mm short of the cap, whose plate closes it).
        Solid(p, -(LCabW + LWallT), LFloor0, LAftZ, LCabW + LWallT, LCeilTop, LStern - 0.004, Wall, "painted plating");
        Detail(p, -LCabW + 0.05, LFt + 0.003, LAftZ - 0.012, -LEngX - 0.42, LFt + 0.7, LAftZ, WallLow, "painted plating");
        Detail(p, LEngX + 0.42, LFt + 0.003, LAftZ - 0.012, LCabW - 0.05, LFt + 0.7, LAftZ, WallLow, "painted plating");
        Detail(p, -LEngX + 0.42, LFt + 0.003, LAftZ - 0.012, LEngX - 0.42, LFt + 0.7, LAftZ, WallLow, "painted plating");
        // The void about the plate out to the skin: under the floor, over the ceiling and either side.
        var ya = Math.Max(LAt(za)[0].Y, LAt(zb)[0].Y) + 0.03;
        var yb = Math.Min(LAt(za)[4].Y, LAt(zb)[4].Y) - 0.03;
        LVoid(p, za, zb - 0.05, ya, LFloor0, 0);
        LVoid(p, za, zb - 0.05, LCeilTop, yb, 0);
        LVoid(p, za, zb - 0.05, LFloor0, LCeilTop, LCabW + LWallT);
        // Each drive's ring plate on the stern, hazard round it; the tail lights over them.
        foreach (var s in new[] { -1, 1 })
        {
            Solid(p, s * LEngX - 0.36, LEngY - 0.36, LStern - 0.04, s * LEngX + 0.36, LEngY + 0.36, LStern + 0.1, Frame, "dirty steel");
            Detail(p, s * LEngX - 0.4, LEngY - 0.41, LStern - 0.035, s * LEngX + 0.4, LEngY - 0.36, LStern + 0.08, Hazard, "hazard paint");
            Detail(p, s * 1.2 - 0.1, 2.2, LStern - 0.01, s * 1.2 + 0.1, 2.3, LStern + 0.006, Red, glow: true);
            var n = s < 0 ? "L" : "R";
            Sock(p, $"drive{n}", s * LEngX, LEngY, LStern + 0.1, "+z");
            Sock(p, $"engine{n}", s * LEngX, LEngY, LAftZ, "-z");
        }
        Works.Mount(p);
        return p.Stats(900, 7000, 40, 10.0);
    }

    /// <summary>One of the shuttle's two drives: a small torch bell on the stern, its engine (a Drive Core XS) right behind it
    /// through the plate, inside the cabin (Karl, 2026-11-17: two thrusters on the back, each with a small engine attached). Under
    /// the table's 500 kN a drive needs no works; these ask for a core all the same, so the engine is there to mend and to shoot out.</summary>
    private static Part LDrive()
    {
        var p = new Part("Shuttle Drive", Small, LSection, $"One of the shuttle's two drives, {LDriveForce / 1e3:0} kN, on its stern. Its engine, a Drive Core XS, is right behind it in the cabin, on its axis.", role: "thruster", tool: "wrench", material: "steel", color: Frame, group: Group);
        (double Z, double R)[] st = [(0.0, 0.3), (0.12, 0.24), (0.35, 0.3), (0.6, 0.36)];
        for (var s = 0; s < st.Length - 1; s++)
        {
            var r0 = RingZ(0, 0, st[s].R, st[s].R, st[s].Z + (s == 0 ? 0.004 : 0), 10);
            var r1 = RingZ(0, 0, st[s + 1].R, st[s + 1].R, st[s + 1].Z, 10);
            for (var i = 0; i < 10; i++)
                Quad(p, r0[i], r0[(i + 1) % 10], r1[(i + 1) % 10], r1[i], s == 0 ? "#3a3d40" : "#2a2c2e", s == 0 ? "dirty steel" : "soot");
        }
        Prism(p, RingZ(0, 0, 0.31, 0.31, 0.42, 10), RingZ(0, 0, 0.33, 0.33, 0.47, 10), "#5a4a3a", "copper");
        Plate(p, [.. RingZ(0, 0, 0.2, 0.2, 0.14, 10)], Glow, glow: true);
        p.Add(Box([-0.25, -0.25, 0.0], [0.25, 0.25, 0.6], hidden: true));
        p.Sock("mount", [0, 0, 0], "-z");
        p.Set(("thruster", Kw(("force", LDriveForce), ("spool", 0.8), ("exhaust", "+z"), ("at", Py.L(0, 0, 0.6)), ("burnRate", Py.Round(LDriveForce / 1e6 * Burn, 4)))), ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [0, 0.25, 0.3], "+y", "fuel"))));
        p.Set(("flame", Py.L(Kw(("at", Py.L(0, 0, 0.6)), ("dir", "+z"), ("radius", 0.32), ("length", 2.4), ("kind", "Rocket")))));
        p.Set(("works", Kw(("needs", Py.L("core")))));
        return p.Stats(250, 2500, 25, 4.0);
    }

    private const double LDriveForce = 60000; // N a drive: the two push what the one 120 kN drive did

    /// <summary>The cockpit's bulkhead: the frame between the cockpit and the cabin, open in the middle (no door: a pod this small seals as one).</summary>
    private static Part LBulkhead()
    {
        var p = LBig("Shuttle Bulkhead", "The frame between the shuttle's cockpit and its cabin, open in the middle.", color: Wall);
        foreach (var s in new[] { -1, 1 })
            Solid(p, Math.Min(s * 0.5, s * LCockW), LFt, LBulkZ - LWallT, Math.Max(s * 0.5, s * LCockW), LCeil, LBulkZ, Wall, "painted plating");
        Solid(p, -0.5, LFt + 1.95, LBulkZ - LWallT, 0.5, LCeil, LBulkZ, Wall, "painted plating");
        foreach (var s in new[] { -1, 1 })
            Detail(p, Math.Min(s * 0.5, s * 0.57), LFt, LBulkZ - LWallT - 0.02, Math.Max(s * 0.5, s * 0.57), LFt + 2.02, LBulkZ + 0.02, WallTrim, "painted steel");
        Detail(p, -0.5, LFt + 1.95, LBulkZ - LWallT - 0.02, 0.5, LFt + 2.02, LBulkZ + 0.02, Hazard, "hazard paint");
        return p.Stats(150, 2500, 15, 6.0);
    }

    // ---- fittings of its own

    /// <summary>A hull grapple: a magnetic pad on a short ram, its sole the shuttle's footing. On (its input), it grips the hull under it.</summary>
    private static Part LGrapple()
    {
        var p = new Part("Hull Grapple", Small, "Ship fittings", "A magnetic hull grapple for a boarding craft (any ship can carry them): wire a switch to it; while it is on and a hull is within half a metre under its pad, the craft grips that hull and rides on it. Turning every grapple off lets go. Fit it under the craft, pad down, as its footing.", role: "logic", tool: "wrench", material: "steel", color: Frame, group: Group);
        p.Add(Box([-0.22, -0.3, -0.22], [0.22, -0.06, 0.22], Frame, shape: Kw(("finish", "dirty steel"))));
        p.Add(Box([-0.12, -0.06, -0.12], [0.12, 0, 0.12], "#9aa0a4", shape: Kw(("finish", "chrome"))));
        p.Add(Box([-0.24, -0.3, -0.24], [0.24, -0.27, 0.24], "#202326", detail: true, shape: Kw(("finish", "rubber"))));
        p.Add(Box([-0.225, -0.2, -0.225], [0.225, -0.16, 0.225], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Add(Box([-0.06, -0.12, 0.222], [0.06, -0.08, 0.236], "#ff8030", detail: true, shape: Kw(("glow", true)))); // (its lamp: lit while it is on)
        p.Sock("top", [0, 0, 0], "+y");
        p.Set(("logic", Kw(("kind", "lamp"))), ("breach", Kw(("kind", "grapple"), ("reach", 0.6))), ("power", 300),
            ("config", Py.L(Kw(("key", "colour"), ("type", "colour"), ("label", "Colour"), ("group", "Light"), ("default", "#ff9040"), ("tooltip", "The colour its lamp glows while it grips.")))));
        return p.Stats(120, 1500, 25, 4.0);
    }

    /// <summary>The breaching cutter: a ring of thermal lances round a boarding craft's belly hatch. On, while the craft grips a hull, it burns through what is under the hatch.</summary>
    private static Part LCutter()
    {
        var p = new Part("Breaching Cutter", Small, "Ship fittings", "A boarding craft's breaching cutter (any ship can carry one): a ring of thermal lances round its belly hatch. Wire a switch to it; while the craft's hull grapples grip a hull and it is on, it burns through what is under the hatch, part after part, until the way is open. Fit it in the hatch, its ring down.", role: "logic", tool: "wrench", material: "steel", color: Frame, group: Group);
        double o = LHatch.X1 - 0.03, i = LHatchIn, top = LFloor0 - LMid[0].Item2; // (from under the keel's face up to the floor's underside, round the hole)
        foreach (var (x0, z0, x1, z1) in new[] { (-o, -o, o, -i), (-o, i, o, o), (-o, -i, -i, i), (i, -i, o, i) })
            p.Add(Box([x0, -0.12, z0], [x1, top, z1], Armour2, shape: Kw(("finish", "plated steel"))));
        // The lances round the inside of the ring and their glow (lit while it is on), and hazard on its face.
        foreach (var (x, z) in new[] { (-0.42, -0.42), (0, -0.44), (0.42, -0.42), (0.44, 0), (0.42, 0.42), (0, 0.44), (-0.42, 0.42), (-0.44, 0) })
        {
            p.Add(Box([x - 0.03, -0.16, z - 0.03], [x + 0.03, -0.1, z + 0.03], "#3a3d40", detail: true, shape: Kw(("finish", "soot"))));
            p.Add(Box([x - 0.02, -0.175, z - 0.02], [x + 0.02, -0.16, z + 0.02], "#ff7020", detail: true, shape: Kw(("glow", true))));
        }
        foreach (var (x0, z0, x1, z1) in new[] { (-o + 0.02, -o + 0.02, o - 0.02, -o + 0.08), (-o + 0.02, o - 0.08, o - 0.02, o - 0.02) })
            p.Add(Box([x0, -0.126, z0], [x1, -0.12, z1], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("top", [0, top, (LHatchIn + LHatch.Z1 - 0.03) / 2], "+y");
        p.Set(("logic", Kw(("kind", "lamp"))), ("breach", Kw(("kind", "cutter"), ("reach", 2.0), ("rate", 900))), ("power", 2500),
            ("config", Py.L(Kw(("key", "colour"), ("type", "colour"), ("label", "Colour"), ("group", "Light"), ("default", "#ff6a20"), ("tooltip", "The colour its lances glow while it burns.")))));
        return p.Stats(260, 3000, 30, 6.0);
    }

    /// <summary>
    /// The docking hatch (Karl, 2026-11-17: "a docking hatch which opens when docked", the Shuttle's and the Zero Day's): an
    /// armoured lid 1.44 m square that slides 1.5 m along its +z off the opening it closes. It is an event door with
    /// <c>"dock": true</c>: while a dock holds the craft and its hatch lies over another docking hatch on the body holding it, both
    /// slide open; when the dock lets go both shut (ConstructDoors.StepDocks). F opens and shuts it by hand as well. It hangs by its
    /// base (its underside) on the deck round the opening, or in a pocket in a floor (the Shuttle's), its top just under the floor's.
    /// </summary>
    private static Part LDockHatch()
    {
        var p = new Part("Docking Hatch", Small, "Ship fittings", "A docking hatch (any ship can carry one): an armoured lid that slides 1.5 m off an opening up to 1.3 m across. While a dock holds the craft over another docking hatch, both slide open by themselves, and shut when the dock lets go. F opens or shuts it by hand. Fit it flat over the opening, with 1.5 m clear on its sliding side.", role: "door", tool: "wrench", material: "steel", color: Armour2, group: Group);
        const double h = DockHatchHalf, t = 0.06;
        p.Add(Box([-h, 0.014, -h], [h, t, h], Armour2, shape: Kw(("finish", "plated steel")))); // (1.4 cm off what it hangs on: clear of a deck's hazard edging)
        p.Add(Box([-0.3, 0, -DockHatchHang - 0.03], [0.3, 0.014, -DockHatchHang + 0.03], hidden: true)); // (the shoe it hangs by, on its fore edge)
        // Hazard chevrons along its two ends, ribs across it, a handle at its leading end and a lamp.
        foreach (var z in new[] { -h + 0.04, h - 0.16 })
            p.Add(Box([-h + 0.04, t - 0.006, z], [h - 0.04, t + 0.008, z + 0.12], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        foreach (var x in new[] { -0.4, 0.0, 0.4 })
            p.Add(Box([x - 0.04, t - 0.006, -h + 0.2], [x + 0.04, t + 0.03, h - 0.2], Armour, detail: true, shape: Kw(("finish", "plated steel"))));
        p.Add(Box([-0.2, t - 0.006, -h + 0.18], [0.2, t + 0.06, -h + 0.22], "#9aa0a4", detail: true, shape: Kw(("finish", "steel"))));
        p.Add(Box([h - 0.14, t - 0.006, -0.05], [h - 0.06, t + 0.025, 0.05], "#40e070", detail: true, shape: Kw(("glow", true))));
        p.Sock("base", [0, 0, -DockHatchHang], "-y");
        p.Set(("door", Kw(("kind", "slide"), ("axis", "+z"), ("travel", DockHatchTravel), ("seconds", 2.0), ("hang", "base"), ("dock", true))));
        return p.Stats(250, 3000, 30, 4.0);
    }

    public const double DockHatchHalf = 0.72; // the docking hatch's half width (it covers an opening up to 1.3 m across)
    public const double DockHatchHang = 0.69; // its hanging shoe (its "base" socket) this far ahead of its middle, on its fore edge
    private const double DockHatchTop = 0.066, DockSkin = 0.004; // the docking hatch's top over its shoe plus 6 mm (its lid's top 6 mm under the floor it lies flush with), and the floor's skin over its pocket
    public const double DockHatchTravel = 1.5; // how far it slides open

    /// <summary>The shuttle's flight computer: a low console ahead of the pilot, a flight stabilizer with a small craft's gyro.</summary>
    private static Part LFlightComputer()
    {
        var p = new Part("Shuttle Flight Computer", Small, LSection, "The shuttle's flight computer: a flight stabilizer in a console ahead of the pilot. Fit it level, its arrow forward.", role: "logic", tool: "wrench", material: "steel", color: Console, group: Group);
        p.Add(Box([-0.4, 0, -0.25], [0.4, 0.8, 0.2], Console, shape: Kw(("finish", "plastic"))));
        p.Add(Box([-0.4, 0.8, -0.25], [0.4, 0.95, -0.05], Console, shape: Kw(("finish", "plastic"))));
        p.Add(Box([-0.32, 0.6, 0.2], [0.32, 0.78, 0.212], Screen, detail: true, shape: Kw(("glow", true))));
        for (var k = 0; k < 4; k++)
            p.Add(Box([-0.25 + k * 0.14, 0.45, 0.2], [-0.2 + k * 0.14, 0.5, 0.215], k % 2 == 0 ? "#40e070" : Amber, detail: true, shape: Kw(("glow", true))));
        p.Add(Box([-0.08, 0.951, -0.2], [0.08, 0.96, -0.1], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("logic", Kw(("kind", "flightstabilizer"), ("gyro", 600000))), ("config", FlightParts.Config), ("faces", "forward"), ("faceAxis", "-z"));
        return p.Stats(120, 600, 6, 3.0);
    }

    /// <summary>The shuttle's jets, from the Zero Day's: a lift jet under each sponson and the drive on the stern.</summary>
    private static Part LJet(string name, string desc, double force, double scale)
    {
        var p = Jet(name, desc, force, scale);
        p.D["section"] = LSection;
        return p;
    }

    /// <summary>The ladder from the Zero Day's mess up into the docked Shuttle: its rails stop at the roof (so it never stands in the
    /// pod's way), its landing is the shuttle's cabin floor over the hatch (behind its top).</summary>
    private static Part ShuttleLadder()
    {
        var rise = LadderRise;
        var p = new Part("Zero Day Shuttle Ladder", Small, Section, "The ladder from the Zero Day's mess up through the roof into the shuttle's belly hatch: F climbs it.", role: "frame", tool: "welder", material: "steel", color: Frame, group: Group);
        var top = LadderRails;
        p.Add(Box([-0.3, 0, -0.19], [0.3, top, -0.005], hidden: true));
        foreach (var x in new[] { -0.25, 0.25 })
        {
            p.Add(Box([x - 0.03, 0.0, -0.18], [x + 0.03, top, -0.12], "#9aa0a4", detail: true, shape: Kw(("finish", "painted steel"))));
            for (var y = 0.6; y < top - 0.2; y += 1.2)
                p.Add(Box([x - 0.02, y, -0.12], [x + 0.02, y + 0.06, -0.01], Frame, detail: true, shape: Kw(("finish", "dirty steel"))));
        }
        for (var y = 0.3; y < top - 0.05; y += 0.3)
            p.Add(Box([-0.22, y - 0.02, -0.165], [0.22, y + 0.02, -0.135], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("bottom", [0, 0, -0.095], "-y");
        p.Set(("ladder", Py.Round(rise, 3)));
        return p.Stats(60 + rise * 12, 400, 6, 3.0);
    }
}

public sealed partial class TorpedoFrigateParts
{
    private const string LOut = "Constructs/Parts/Shuttle", BoardingOut = "Constructs/Parts/Boarding";

    /// <summary>The shuttle's pieces, welded (one pair a touching pair), in the order they are placed out from the belly.</summary>
    private static (List<Part> Pieces, List<string> Order) ShuttleStructure()
    {
        List<Part> pieces = [LBelly(), LSide(-1), LSide(1), LRoof(), LNose(), LSternPart(), LBulkhead()];
        Connect(pieces);
        return (pieces, PlaceOrder(pieces, "Shuttle Belly"));
    }

    /// <summary>The shuttle's own parts (its pieces, jets, flight computer) and the boarding fittings any ship can carry.</summary>
    private static List<(Part Part, string Folder)> ShuttleParts(List<Part> pieces) =>
    [
        .. pieces.Select(x => (x, LOut)),
        (LJet("Shuttle Lift Jet", "A lift jet under one of the shuttle's sponsons: 110 kN straight down.", 110000, 0.5), LOut),
        (LDrive(), LOut),
        (LFlightComputer(), LOut),
        (LGrapple(), BoardingOut),
        (LCutter(), BoardingOut),
        (LDockHatch(), BoardingOut),
    ];

    private static PyDict ShuttleBlueprint(Dictionary<string, PyDict> defs, List<string> order)
    {
        var bp = new BuildingParts.Blueprint(ShuttleName, "The Zero Day's shuttle: a pilot and four boarders, two drives on its stern with their engines in the cabin, and a docking hatch under its belly that opens by itself while it is docked over another. It can grip a ship's hull with four magnetic grapples (the port cockpit switch).", defs, maxSteps: 120);
        bp.Conduit = true;
        double[] zero = [0, 0, 0];
        var st = new Dictionary<string, int>();
        foreach (var name in order)
            st[name] = st.Count == 0 ? bp.First(name) : bp.Put(name, zero, 0);
        int belly = st["Shuttle Belly"], port = st["Shuttle Hull Port"], stbd = st["Shuttle Hull Starboard"], stern = st["Shuttle Stern"];
        // Flight: a lift jet under each sponson, attitude jets round them, the drive on the stern.
        foreach (var side in new[] { port, stbd })
            foreach (var tag in new[] { "F", "B" })
            {
                bp.Hang("Shuttle Lift Jet", side, $"lift{tag}", "top");
                foreach (var r in new[] { "rcsUp", "rcsOut", "rcsEnd" })
                    bp.Hang("RCS Thruster", side, r + tag, "base");
            }
        // The two drives on the stern's plate, each with its engine (a Drive Core XS) bolted to the plate's inner face on its axis.
        foreach (var n in new[] { "L", "R" })
        {
            bp.HangTurned("Shuttle Drive", stern, $"drive{n}", "mount", 0);
            bp.HangTurned("Drive Core XS", stern, $"engine{n}", "front", 0);
        }
        // The docking hatch in the floor, sliding aft; the grapples on a cockpit switch to port.
        bp.HangTurned("Docking Hatch", belly, "hatch", "base", BuildingParts.R0);
        var grips = new[] { "gripFL", "gripFR", "gripBL", "gripBR" }.Select(n => bp.Hang("Hull Grapple", belly, n, "top")).ToArray();
        var gripSwitch = bp.Hang("Bakelite Switch", port, "switchB", "back");
        foreach (var g in grips)
            bp.Wires.Add((gripSwitch, g));
        // The pilot and the flight computer; the boarders' four seats facing in along the cabin's walls; lockers and batteries aft.
        var pilot = bp.Hang("Flight Couch", belly, "pilot", "bottom");
        Paint(bp, pilot, "#2c3e58", "fabric");
        var fc = bp.Hang("Shuttle Flight Computer", belly, "computer", "bottom");
        bp.Steps[fc]["config"] = ShipParts.ShipFlight();
        bp.Wires.Add((pilot, fc));
        // (a couch faces its -z: RY270 turns that to +x, so the port pair faces in to starboard, and RY90 the starboard pair to port)
        foreach (var n in new[] { "seatL0", "seatL1" })
            Paint(bp, bp.HangTurned("Flight Couch", belly, n, "bottom", BuildingParts.RY270), "#2c3e58", "fabric");
        foreach (var n in new[] { "seatR0", "seatR1" })
            Paint(bp, bp.HangTurned("Flight Couch", belly, n, "bottom", BuildingParts.RY90), "#2c3e58", "fabric");
        // (a Space Suit Locker's back is its +z: RY270 puts it to -x, against the port wall, and RY90 to +x)
        bp.HangTurned(EvaParts.LockerName, belly, "lockerL", "bottom", BuildingParts.RY270);
        bp.HangTurned(EvaParts.LockerName, belly, "lockerR", "bottom", BuildingParts.RY90);
        bp.HangTurned("Ship Battery XS", belly, "battery", "bottom", BuildingParts.RY270);
        // Lights: the cockpit's and the cabin's, on the starboard cockpit switch; the cabin's switch by the hatch.
        int Lamp(int on, string sock, string colour)
        {
            var s = bp.Hang("Bulkhead Light", on, sock, "back");
            bp.Steps[s]["config"] = new PyDict { ["colour"] = colour };
            return s;
        }
        var lightSwitch = bp.Hang("Bakelite Switch", stbd, "switchA", "back");
        bp.Steps[lightSwitch]["config"] = new PyDict { ["startOn"] = true };
        foreach (var side in new[] { port, stbd })
            bp.Wires.Add((lightSwitch, Lamp(side, "lampCockpit", "#ff6050")));
        var cabinSwitch = bp.Hang("Bakelite Switch", stbd, "switchCabin", "back");
        bp.Steps[cabinSwitch]["config"] = new PyDict { ["startOn"] = true };
        foreach (var side in new[] { port, stbd })
            foreach (var l in new[] { "lampCabinF", "lampCabinB" })
                bp.Wires.Add((cabinSwitch, Lamp(side, l, l == "lampCabinF" ? "#ffb070" : "#e8f4ff")));
        return bp.Json();
    }
}
