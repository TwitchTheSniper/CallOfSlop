using CallOfSlop.PartKit.ShipKit;
using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Folio, the Traders' shuttle (Karl, 2026-11-21: "it's the traders' faction, a shuttle", the first ship built on the ship kit,
/// "not generic, cookie-cutter replacements and snap-ons"). A small craft, about 15 m long: it carries a firm's people and a client or
/// two between a station and a ship, or down to a planet's landing field. A glass cockpit in the Traders' blunt nose, a cabin of six
/// couches in three rows under a band of windows (the client cabin), and a utility room aft: the belly hatch with its ladder, the
/// suit, the ship's air, the plant and the Engineer's Panel. Two drives in square office-case housings on the transom, four lift jets
/// in louvred shrouds under the keel, small leg pods. Every jet is under ShipWorks' jetMost, so nothing aft needs works.
///
/// Drawn on the kit (LoftedShip, docs/sandbox/constructs/ship-kit.md): its hull, rooms and dressing are its own; the kit gives the
/// loft, the walls, the splitting into welded pieces and the way each fitting is drawn from the specs below. Ship metres: x across,
/// y up from the deck's underside, z along (forward is -z; 0 near the cabin's middle). Notes: docs/sandbox/constructs/folio.md.
/// </summary>
public sealed class FolioParts : LoftedShip, IPartGenerator
{
    public string Name => "folio_parts";

    private const string Out = "Constructs/Parts/Folio";
    private const string BpOut = "Constructs/Blueprints";
    public const string ShipName = "Folio Shuttle";

    public FolioParts() : base("Folio", "Folio shuttle", ShipStyle.Traders)
    {
        Hull = HullSecs;
        Bands = [Zt, ZS1, ZCk, ZCab, ZMid, ZCabEnd, ZEngEnd, Stern];
        RibZ = [ZCk, ZCabEnd, ZEngEnd];
    }

    // ---- the plan

    private const double Ft = 0.25; // the deck's top
    private const double CCeil = 2.65, CRoof = 2.9, Lw = 1.75; // the ceiling, the roof slab's top, the rooms' inner half width
    private const double Zt = -7.0, ZS1 = -5.6, ZCk = -4.0, ZCab = -2.0, ZMid = -0.35, ZCabEnd = 2.5, ZEngEnd = 6.25, Stern = 8.25;
    private const double ZCkFloor = -6.1; // the cockpit's floor, forward
    private const double ZCkW = -3.5, ZCabW = 2.5, ZEngAW = 6.0; // the cross walls' middles: the cockpit's back, the cabin's aft, the utility's aft
    /// <summary>The cabin's three rows of couches (their middles), a window by each row.</summary>
    private static readonly double[] Rows = [-2.6, -1.1, 0.4];
    private const double SeatX = 0.95, WinSill = 1.05, WinHead = 1.85, WinW = 0.9;

    // The boarding shaft in the utility room, the belly hatch shutting it and the ladder on the hatch.
    private static readonly Rect Shaft = new(Cx0 + 0.1, 3.2, Cx0 + 1.25, 4.45);
    private const double Cx0 = 0.0;
    private const string HatchName = "Folio Belly Hatch";
    private static double HatchX => Shaft.X0 + 0.575;

    // ---- flying it

    private const double LiftForce = 220000, DriveForce = 300000, Burn = 2.5; // (each under jetMost: no works)
    private const int KeelFuel = 3200, SternFuel = 1600;
    private static readonly (double X, double Z)[] LiftAt = [(-0.55, -2.45), (0.55, -2.45), (-0.75, 5.4), (0.75, 5.4)];
    private static readonly (double X, double Z)[] LegAt = [(-1.35, -3.6), (1.35, -3.6), (-1.5, 5.6), (1.5, 5.6)]; // (each pad inside one station band: a pad across a cut is in the next piece's way)
    private const string LegPod = "S";
    private const double LegProud = 0.16;
    private static double Ground => -0.6 - LegProud - LegPodParts.ReachOf(LegPod);
    private const string Rcs = "RCS Thruster";
    private const double BowRcsZ = -3.3, SternRcsZ = 7.0;
    private const double DriveX = 0.85, DriveY = 1.25;

    // ---- the hull: stations of a half profile (keel edge, lower chine, beam, upper chine, roof edge). A blunt glass nose sealed by a
    // nose cap, widening to the cabin's full section, which runs straight to the utility room and tucks in to a square transom.

    private static readonly (double, double)[] CabSec = [(1.9, -0.6), (2.55, 0.0), (2.7, 1.3), (2.5, 2.8), (1.5, 3.3)];

    private static readonly Sec[] HullSecs =
    [
        new(Zt, [(0.7, 0.1), (1.0, 0.4), (1.1, 1.4), (0.95, 2.1), (0.5, 2.4)]),
        new(ZS1, [(1.1, -0.3), (1.55, 0.15), (1.7, 1.35), (1.55, 2.5), (0.85, 2.9)]),
        new(ZCk, [(1.75, -0.6), (2.45, 0.0), (2.6, 1.3), (2.4, 2.75), (1.4, 3.2)]),
        new(ZCab, CabSec), new(ZCabEnd, CabSec),
        new(ZEngEnd, [(1.85, -0.55), (2.5, 0.05), (2.65, 1.3), (2.45, 2.75), (1.45, 3.2)]),
        new(Stern, [(0.95, 0.0), (1.55, 0.4), (1.75, 1.3), (1.55, 2.3), (0.85, 2.6)]),
    ];

    /// <summary>The armour panels on a facet band: the Folio's are long and few (a shuttle is smooth), every other one a cream slab
    /// with a corporate-blue inset, the rest bare skin with a seam and a yellow tab.</summary>
    private void Panels(Part p, int k, int s, double za, double zb, int salt, double v0 = 0.08, double v1 = 0.92)
    {
        var n = Math.Max(1, (int)Math.Round((zb - za) / 2.4));
        for (var i = 0; i < n; i++)
        {
            double u0 = (double)i / n + 0.12 / (zb - za), u1 = (double)(i + 1) / n - 0.12 / (zb - za);
            switch ((k * 5 + i * 3 + salt) % 3)
            {
                case 0:
                    Slab(p, k, s, za, zb, u0, u1, v0, v1, 0.02, 0.08, 0.05, Skin);
                    Slab(p, k, s, za, zb, u0 + (u1 - u0) * 0.3, u1 - (u1 - u0) * 0.3, v0 + 0.2, v1 - 0.25, 0.06, 0.11, 0.02, Armour2);
                    break;
                case 1:
                    Slab(p, k, s, za, zb, u0, u1, v0, v1, 0.02, 0.07, 0.04, Skin2);
                    break;
                default:
                    FStrip(p, k, s, za, zb, (u0 + u1) / 2 - 0.004, (u0 + u1) / 2 + 0.004, v0, v1, 0.03, Frame, "dirty steel");
                    FStrip(p, k, s, za, zb, u0, u0 + Math.Min(0.2, (u1 - u0) * 0.2), v0 + 0.08, v0 + 0.18, 0.031, Hazard, "hazard paint");
                    break;
            }
        }
    }

    private void Band(Part p, int k, int s, double za, double zb, string color, int salt, bool panels = true)
    {
        Facet(p, k, s, za, zb, color, "painted plating");
        if (panels && zb - za > 1.4)
            Panels(p, k, s, za + (RibAt(za) ? 0.3 : 0.05), zb - (RibAt(zb) ? 0.3 : 0.05), salt);
    }

    /// <summary>The Folio's livery on the upper side (facet 3): the client cabin's glasshouse, a broad corporate-blue band behind the
    /// windows from the beam up, a yellow pinstripe over it.</summary>
    private void Glasshouse(Part p, int s, double za, double zb)
    {
        double a = za + (RibAt(za) ? 0.26 : 0.02), b = zb - (RibAt(zb) ? 0.26 : 0.02), len = zb - za;
        FStrip(p, 3, s, za, zb, (a - za) / len, (b - za) / len, 0.0, 0.6, 0.024, Armour, "painted plating");
        FStrip(p, 3, s, za, zb, (a - za) / len, (b - za) / len, 0.63, 0.67, 0.024, Style.Hazard, "painted plating");
    }

    /// <summary>The v (0 at the beam) of a height on facet 3 at z.</summary>
    private double V3(double z, double y)
    {
        var q = At(z);
        return (y - q[2].Y) / (q[3].Y - q[2].Y);
    }

    // ---- the keel: the flat underside, the tankage under the decks, the shaft, the lift jets' and legs' mounts

    private Part Keel()
    {
        var p = Big("Folio Keel", "The Folio's keel: the flat underside under the cockpit, the cabin and the utility room, with the fuel tankage under the decks, the boarding shaft, and the lift jets' and legs' mounts.", "fueltank", Armour);
        var shaft = new Rect(Shaft.X0 - 0.05, Shaft.Z0 - 0.05, Shaft.X1 + 0.05, Shaft.Z1 + 0.05);
        foreach (var (i, za, zb) in BandsIn(Zt, ZEngEnd))
        {
            var y = At(za)[0].Y;
            var hb = At(za)[0].H;
            if (za <= Shaft.Z0 && zb >= Shaft.Z1)
            {
                var a = Shaft;
                Plate(p, [P(Cx - hb, y, za), P(Cx + hb, y, za), P(Cx + hb, y, a.Z0), P(Cx - hb, y, a.Z0)], Armour, "plated steel");
                Plate(p, [P(Cx - hb, y, a.Z1), P(Cx + hb, y, a.Z1), P(Cx + hb, y, zb), P(Cx - hb, y, zb)], Armour, "plated steel");
                Plate(p, [P(Cx - hb, y, a.Z0), P(a.X0, y, a.Z0), P(a.X0, y, a.Z1), P(Cx - hb, y, a.Z1)], Armour, "plated steel");
                Plate(p, [P(a.X1, y, a.Z0), P(Cx + hb, y, a.Z0), P(Cx + hb, y, a.Z1), P(a.X1, y, a.Z1)], Armour, "plated steel");
            }
            else
                Facet(p, 0, 1, za, zb, Armour, "plated steel");
            foreach (var s in new[] { -1, 1 })
            {
                Band(p, 1, s, za, zb, Skin2, 1 + i, panels: za >= ZCk);
                Chine(p, 1, s, za, zb, 0.16, 0.07, Frame);
            }
            if (zb - za > 2 && za >= ZCk) // (skids along the flat)
                foreach (var s in new[] { -1, 1 })
                    Detail(p, Cx + s * 1.55 - 0.07, y - 0.1, za + 0.3, Cx + s * 1.55 + 0.07, y - 0.023, zb - 0.3, Dark, "dirty steel");
        }
        Ribs(p, ZCk, ZEngEnd, 0, 1);
        foreach (var (_, za, zb) in BandsIn(Zt, ZEngEnd))
        {
            var yb = Math.Max(At(za)[0].Y, At(zb)[0].Y) + 0.02;
            if (yb >= -0.05)
                continue;
            var xo = Math.Floor((new[] { SkinHalf(za, yb), SkinHalf(za, 0), SkinHalf(zb, yb), SkinHalf(zb, 0) }.Min() - 0.06) * 100) / 100;
            if (xo < 0.3)
                continue;
            foreach (var r in Minus([new Rect(Cx - xo, za + 0.012, Cx + xo, zb - 0.012)], shaft))
                Hidden(p, r.X0, yb, r.Z0, r.X1, 0.0, r.Z1);
        }
        // The shaft's lining down through the keel, hazard round its mouth.
        var hy = At((Shaft.Z0 + Shaft.Z1) / 2)[0].Y;
        foreach (var (x0, z0, x1, z1, e) in new[] { (shaft.X0, shaft.Z0, shaft.X1, shaft.Z0, 0.0), (shaft.X0, shaft.Z1, shaft.X1, shaft.Z1, 0.0),
            (shaft.X0, shaft.Z0 + 0.025, shaft.X0, shaft.Z1 - 0.025, 0.005), (shaft.X1, shaft.Z0 + 0.025, shaft.X1, shaft.Z1 - 0.025, 0.005) })
            Plate(p, [P(x0, hy + 0.01 + e, z0), P(x1, hy + 0.01 + e, z1), P(x1, -0.01 - e, z1), P(x0, -0.01 - e, z0)], Grate, "dirty steel");
        Detail(p, shaft.X0 - 0.12, hy - 0.03, shaft.Z0 - 0.12, shaft.X1 + 0.12, hy - 0.004, shaft.Z0, Hazard, "hazard paint");
        Detail(p, shaft.X0 - 0.12, hy - 0.03, shaft.Z1, shaft.X1 + 0.12, hy - 0.004, shaft.Z1 + 0.12, Hazard, "hazard paint");
        for (var i = 0; i < LiftAt.Length; i++)
            MountPad(p, $"lift{i}", Cx + LiftAt[i].X, At(LiftAt[i].Z)[0].Y, LiftAt[i].Z, 0.4);
        for (var i = 0; i < LegAt.Length; i++)
            MountPad(p, $"leg{i}", Cx + LegAt[i].X, At(LegAt[i].Z)[0].Y, LegAt[i].Z, 0.38, LegProud);
        foreach (var s in new[] { -1, 1 })
            RcsPad(p, "rcsDown" + (s < 0 ? "L" : "R"), Cx + s * 0.4, At(BowRcsZ)[0].Y, BowRcsZ, "-y", 0.05, 0.4);
        // Landing lamps under the chin.
        foreach (var s in new[] { -1, 1 })
            Detail(p, Cx + s * 0.8 - 0.14, At(ZCk)[0].Y - 0.05, ZCk + 0.25, Cx + s * 0.8 + 0.14, At(ZCk)[0].Y - 0.012, ZCk + 0.5, Glow, glow: true);
        p.Set(("fuelTank", Kw(("capacity", KeelFuel))));
        return p.Stats(6000, 20000, 35, 15.0);
    }

    // ---- the roof: the cockpit's glass top, the cabin's roof with a dorsal fairing (antennas and the beacon), the void over the ceilings

    private Part Roof()
    {
        var p = Big("Folio Roof", "The Folio's roof: the cockpit's glass top, the cabin's roof with its dorsal fairing, the antennas and the beacon, and the utility room's roof.", color: Skin);
        foreach (var (i, za, zb) in BandsIn(Zt, ZEngEnd))
        {
            var glass = zb <= ZCk + 1e-6;
            if (glass)
                Facet(p, 5, 1, za, zb, Glass, glass: true);
            else
            {
                Facet(p, 5, 1, za, zb, Skin2, "painted plating");
                if (zb - za > 1.4)
                    Panels(p, 5, 1, za + (RibAt(za) ? 0.3 : 0.05), zb - (RibAt(zb) ? 0.3 : 0.05), 7 + i, 0.14, 0.86);
            }
            foreach (var s in new[] { -1, 1 })
            {
                if (glass)
                    Facet(p, 4, s, za, zb, Glass, glass: true);
                else
                    Band(p, 4, s, za, zb, Skin, 4 + i);
                Chine(p, 4, s, za, zb, glass ? 0.06 : 0.14, glass ? 0.045 : 0.07, glass ? Dark : Armour);
            }
        }
        Ribs(p, ZCk, ZEngEnd, 4, 5);
        // The canopy's frame: hoops at the stations and a spine bar down the middle.
        foreach (var z in new[] { ZS1, ZCk })
        {
            Slab(p, 5, 1, z - 0.06, z + 0.06, 0, 1, 0, 1, 0.01, 0.07, 0.015, Dark, "painted steel");
            foreach (var s in new[] { -1, 1 })
                Slab(p, 4, s, z - 0.06 - FacetStagger, z + 0.06 + FacetStagger, 0, 1, 0, 1, 0.01, 0.07, 0.015, Dark, "painted steel");
        }
        foreach (var (za, zb) in new[] { (Zt + 0.1, ZS1 - 0.07), (ZS1 + 0.07, ZCk - 0.07) })
            FStrip(p, 5, 1, za, zb, 0, 1, 0.47, 0.53, 0.03, Dark, "painted steel");
        double Top(double z) => At(z)[4].Y;
        // The dorsal fairing over the cabin: a low cream hump raked at both ends, a blue cap on it, the antennas along it (the Traders'
        // whips and a small dish: a shuttle talks to its station all the way in), the red beacon at its aft end.
        {
            double z0 = -2.9, z1 = 1.8, y = Top(0) - 0.01, w = 0.42, h = 0.32;
            Prism(p, [P(Cx - w, y, z0), P(Cx + w, y, z0), P(Cx + w, y, z1), P(Cx - w, y, z1)],
                [P(Cx - w * 0.7, y + h, z0 + 0.7), P(Cx + w * 0.7, y + h, z0 + 0.7), P(Cx + w * 0.7, y + h, z1 - 0.5), P(Cx - w * 0.7, y + h, z1 - 0.5)], Skin, "painted plating");
            Detail(p, Cx - w * 0.5, y + h, z0 + 0.9, Cx + w * 0.5, y + h + 0.03, z1 - 0.7, Armour, "painted plating");
            foreach (var (dz, hh) in new[] { (-1.6, 1.0), (-0.9, 0.7) })
            {
                Detail(p, Cx - 0.02, y + h + 0.03, dz - 0.02, Cx + 0.02, y + h + 0.03 + hh, dz + 0.02, Dark, "painted steel");
                Detail(p, Cx - 0.04, y + h + 0.03 + hh, dz - 0.04, Cx + 0.04, y + h + 0.11 + hh, dz + 0.04, Link, glow: true);
            }
            Detail(p, Cx - 0.05, y + h + 0.03, 0.15, Cx + 0.05, y + h + 0.3, 0.25, Frame, "painted steel");
            Prism(p, Ngon(Cx, y + h + 0.3, 0.2, 0.06, 10), Ngon(Cx, y + h + 0.45, 0.2, 0.32, 10), Paper, "painted steel");
            Detail(p, Cx - 0.07, y + h + 0.03, 1.0, Cx + 0.07, y + h + 0.17, 1.14, Red, glow: true);
        }
        // Navigation lights on the cabin's shoulders: red to port, green to starboard; a docking floodlight over the utility room.
        foreach (var s in new[] { -1, 1 })
            Detail(p, Cx + s * 1.25 - 0.09, Top(-3.6) - 0.01, -3.7, Cx + s * 1.25 + 0.09, Top(-3.6) + 0.08, -3.5, s < 0 ? Red : Green, glow: true);
        Detail(p, Cx - 0.25, Top(4.6) - 0.01, 4.5, Cx + 0.25, Top(4.6) + 0.1, 4.7, Glow, glow: true);
        // The bow's and the stern's up jets.
        foreach (var s in new[] { -1, 1 })
            RcsPad(p, "rcsUp" + (s < 0 ? "L" : "R"), Cx + s * 0.95, Top(BowRcsZ), BowRcsZ, "+y", 0.05, 0.4);
        // The void over the ceilings.
        foreach (var (_, za, zb) in BandsIn(ZCk, ZEngEnd))
        {
            var z0 = Math.Max(za, ZCkW + 0.125);
            VoidT(p, z0, zb, CRoof, Math.Min(Top(z0), Top(zb)) - 0.03, 0, z0 > za ? 0.0 : 0.004, 0.004);
        }
        return p.Stats(3500, 12000, 30, 12.0);
    }

    // ---- the cabin: the glass cockpit, then the client cabin (six couches under the windows), its hull bands

    private Part Cabin()
    {
        var p = Big("Folio Cabin", "The Folio's cabin: the glass cockpit forward (two couches, the flight computer), then the client cabin, six couches in three rows under a band of windows, a coffee station and a locker by the aft door.");
        Rect ckA = new(Cx - 0.8, ZCkFloor, Cx + 0.8, ZCkFloor + 0.8), ckB = new(Cx - 1.3, ZCkFloor + 0.8, Cx + 1.3, ZCkW - 0.125), cab = new(Cx - Lw - 0.25, ZCkW - 0.125, Cx + Lw + 0.25, ZCabW + 0.125);
        Floor(p, [ckA, ckB, cab], 0, Ft, Deck, "painted deck plate");
        Floor(p, [new Rect(cab.X0, ZCkW + 0.125, cab.X1, ZCabW - 0.125)], CCeil, CRoof, Ceil, "painted plating");
        Room(p, new Rect(Cx - 1.3, ZCkFloor - 0.4, Cx + 1.3, ZCkW), 0, 3.1);
        Room(p, cab, 0, CRoof);
        CeilingAt(p, CCeil);
        // The cockpit: a mat, the dash under the windscreen (its screens and keys), the console between the couches.
        Detail(p, Cx - 1.2, Ft, ZCkFloor + 0.55, Cx + 1.2, Ft + 0.012, ZCkW - 0.25, Style.Mat, "rubber matting");
        Solid(p, Cx - 0.75, Ft, ZCkFloor, Cx + 0.75, 0.9, ZCkFloor + 0.45, Dark, "plastic");
        foreach (var (x0, x1, col) in new[] { (-0.68, -0.26, Style.Screen), (-0.2, 0.2, Style.Screen2), (0.26, 0.68, Style.Screen) })
            Detail(p, Cx + x0, 0.9, ZCkFloor + 0.05, Cx + x1, 0.912, ZCkFloor + 0.4, col, glow: true);
        for (var i = 0; i < 6; i++)
            Detail(p, Cx - 0.5 + i * 0.2, 0.55, ZCkFloor + 0.45, Cx - 0.42 + i * 0.2, 0.63, ZCkFloor + 0.463, i % 3 == 0 ? Hazard : Paper, "plastic");
        // The canopy's backing (what stops a body at the glass), the void under it, the nose's void.
        foreach (var (za, zb) in new[] { (ZCkFloor, ZS1), (ZS1, ZCkW - 0.125) })
        {
            var hx = new[] { SkinHalf(za, 0.9), SkinHalf(za, 2.2), SkinHalf(zb, 0.9), SkinHalf(zb, 2.2) }.Min() - 0.1;
            foreach (var s in new[] { -1, 1 })
                Hidden(p, Cx + s * (hx - 0.08), 0.9, za + 0.004, Cx + s * hx, 2.2, zb - 0.004);
            var ty = Math.Min(At(za)[4].Y, At(zb)[4].Y) - 0.12;
            var tw = Math.Min(At(za)[4].H, At(zb)[4].H);
            Hidden(p, Cx - tw, ty - 0.08, za + 0.004, Cx + tw, ty, zb - 0.004);
            VoidT(p, za, zb, za == ZCkFloor ? 0 : 0.1, 0.9, za == ZCkFloor ? 0.8 : 1.3);
        }
        Hidden(p, Cx - 0.7, Ft, ZCkFloor - 0.4, Cx + 0.7, 1.7, ZCkFloor - 0.004);
        // The cockpit's back wall (a door to the cabin), the cabin's sides with a window by each row, its aft wall (a door aft).
        WallRun(p, true, Cx - Lw - 0.25, Cx + Lw + 0.25, ZCkW, Ft, CCeil, Door(Cx - 0.75));
        foreach (var s in new[] { -1, 1 })
            WallRun(p, false, ZCkW + 0.125, ZCabW - 0.125, Cx + s * (Lw + 0.125), Ft, CCeil, [.. Rows.Select(z => new Gap(z - WinW / 2, WinW, WinSill, WinHead, true))]);
        WallRun(p, true, Cx - Lw - 0.25, Cx + Lw + 0.25, ZCabW, Ft, CCeil, Door(Cx - 1.2));
        // The aisle: a blue runner down the middle, a light strip either side over the couches, the reading lamps' strip over the aisle.
        Detail(p, Cx - 0.45, Ft, ZCkW + 0.3, Cx + 0.45, Ft + 0.012, ZCabW - 0.3, Armour, "fabric");
        foreach (var s in new[] { -1, 1 })
            CeilStrip(p, Cx + s * 0.95 - 0.08, Cx + s * 0.95 + 0.08, ZCkW + 0.5, ZCabW - 0.6, CCeil, "#fff0d8");
        CeilStrip(p, Cx - 0.06, Cx + 0.06, ZCkW + 0.4, ZCabW - 0.4, CCeil);
        // Grab rails along the cabin's walls over the windows.
        foreach (var s in new[] { -1, 1 })
            Detail(p, Cx + s * (Lw - 0.05), Ft + WinHead + 0.15, ZCkW + 0.4, Cx + s * (Lw - 0.01), Ft + WinHead + 0.2, ZCabW - 0.4, Frame, "painted steel");
        // The hull: the cockpit's glass sides and windscreen, then the cabin's bands with the glasshouse and its windows.
        foreach (var s in new[] { -1, 1 })
        {
            Facet(p, 2, s, Zt, ZS1, Glass, glass: true);
            Facet(p, 3, s, Zt, ZS1, Glass, glass: true);
            Band(p, 2, s, ZS1, ZCk, Skin, 2, panels: false);
            Facet(p, 3, s, ZS1, ZCk, Glass, glass: true);
            Chine(p, 2, s, Zt, ZS1, 0.06, 0.045, Dark);
            Chine(p, 3, s, Zt, ZS1, 0.06, 0.045, Dark);
            Chine(p, 2, s, ZS1, ZCk, 0.2, 0.09, Armour);
            Chine(p, 3, s, ZS1, ZCk, 0.06, 0.045, Dark);
            Slab(p, 2, s, ZS1, ZCk, 0.12, 0.88, 0.14, 0.86, 0.02, 0.09, 0.05, Armour);
            foreach (var (_, za, zb) in BandsIn(ZCk, ZCabEnd))
            {
                Band(p, 2, s, za, zb, Skin, 3);
                Facet(p, 3, s, za, zb, Skin, "painted plating");
                Glasshouse(p, s, za, zb);
                Chine(p, 2, s, za, zb, 0.2, 0.09, Armour);
                Chine(p, 3, s, za, zb, 0.16, 0.07, Frame);
            }
            // A window by each row: a dark frame on the band, the glass in it.
            foreach (var z in Rows)
            {
                var (za, zb) = BandsIn(ZCk, ZCabEnd).Select(b => (b.A, b.B)).First(b => z > b.A && z < b.B);
                double len = zb - za, u0 = (z - WinW / 2 - za) / len, u1 = (z + WinW / 2 - za) / len, v0 = V3(z, Ft + WinSill), v1 = V3(z, Ft + WinHead);
                Slab(p, 3, s, za, zb, u0 - 0.05 / len, u1 + 0.05 / len, v0 - 0.04, v1 + 0.04, 0.03, 0.07, 0.02, Dark, "painted steel");
                FStrip(p, 3, s, za, zb, u0, u1, v0, v1, 0.072, "#24384a", "glass");
            }
            // The registry by the door aft: a white plate with the Traders' blue square.
            Slab(p, 2, s, ZMid, ZCabEnd, 0.62, 0.9, 0.35, 0.8, 0.065, 0.09, 0.02, Paper, "painted plating"); // (on the panel there)
            Slab(p, 2, s, ZMid, ZCabEnd, 0.64, 0.69, 0.42, 0.73, 0.085, 0.105, 0.01, Armour, "painted plating");
        }
        Ribs(p, ZCk, ZCabEnd, 2, 3);
        foreach (var (_, za, zb) in BandsIn(ZCk, ZCabEnd))
            foreach (var (ya, yb) in new[] { (0.0, 1.3), (1.3, CRoof) })
                VoidT(p, za, zb, ya, yb, Lw + 0.25, za == ZCk ? 0.25 : 0.004, zb == ZCabEnd ? 0.125 : 0.004);
        // The bow's out jets and its retro pads (forward-facing jets on the cabin's flanks: the brakes).
        foreach (var s in new[] { -1, 1 })
        {
            RcsPad(p, "rcsOut" + (s < 0 ? "L" : "R"), FacetX(2, s, BowRcsZ, 0.7), 0.7, BowRcsZ, s < 0 ? "-x" : "+x", 0.2, 0.4);
            RcsPad(p, "rcsFwd" + (s < 0 ? "L" : "R"), FacetX(2, s, -2.6, 0.45) - s * 0.12, 0.45, -3.75, "-z", 0.05, 0.4);
        }
        DoorSock(p, "doorCk", true, Cx - 0.75, ZCkW, Ft);
        DoorSock(p, "doorAft", true, Cx - 1.2, ZCabW, Ft); // (to port of the aisle: its landing aft clears the hatch rail)
        // The cockpit's couches and computer, its lamps and switch on the back wall.
        Sock(p, "pilot", Cx - 0.75, Ft, -4.6, "+y");
        Sock(p, "copilot", Cx + 0.75, Ft, -4.6, "+y");
        Sock(p, "computer", Cx, Ft, -5.25, "+y");
        Sock(p, "lampCkL", Cx - 1.05, Ft + 2.0, ZCkW - 0.125, "-z");
        Sock(p, "lampCkR", Cx + 1.05, Ft + 2.0, ZCkW - 0.125, "-z");
        Sock(p, "switchCk", Cx + 1.05, Ft + 1.3, ZCkW - 0.125, "-z");
        // The cabin: six couches, the coffee station and a locker on the aft wall, lamps between the windows, the switch by the door.
        for (var r = 0; r < Rows.Length; r++)
            foreach (var s in new[] { -1, 1 })
                Sock(p, $"seat{r}{(s < 0 ? "L" : "R")}", Cx + s * SeatX, Ft, Rows[r], "+y");
        Sock(p, "coffee", Cx + 1.2, Ft + 0.125, ZCabW - 0.125, "-z");
        Sock(p, "locker", Cx - 1.3, Ft, ZCabW - 0.125 - 0.28, "+y");
        foreach (var s in new[] { -1, 1 })
            foreach (var (z, k) in new[] { (-1.85, 0), (-0.35, 1) })
                Sock(p, $"lamp{(s < 0 ? "L" : "R")}{k}", Cx + s * Lw, Ft + 2.05, z, s < 0 ? "+x" : "-x");
        Sock(p, "switchCab", Cx + 1.1, Ft + 1.3, ZCkW + 0.125, "+z");
        // The aft wall's utility face: the hatch's switch and the room's, the cycle lights over the door.
        Sock(p, "switchHatch", Cx + 1.05, Ft + 1.45, ZCabW + 0.125, "+z");
        Sock(p, "switchUtil", Cx - 1.5, Ft + 1.3, ZCabW + 0.125, "+z");
        OnWall(p, () =>
        {
            Detail(p, Cx + 0.9, Ft + 1.95, ZCabW + 0.125, Cx + 1.04, Ft + 2.09, ZCabW + 0.14, Red, glow: true);
            Detail(p, Cx + 1.08, Ft + 1.95, ZCabW + 0.125, Cx + 1.22, Ft + 2.09, ZCabW + 0.14, "#40e070", glow: true);
        });
        foreach (var (what, z, hx) in new[] { ("cockpit front", ZCkFloor, 0.8), ("cockpit", ZCkFloor + 0.8, 1.3), ("cabin front", ZCkW - 0.125, Lw + 0.25), ("cabin aft", ZCabW + 0.125, Lw + 0.25) })
            foreach (var y in new[] { 0.0, z < ZCkW - 0.2 ? 0.9 : CRoof })
                MustFit(what, z, hx, y);
        return p.Stats(6000, 16000, 30, 15.0);
    }

    // ---- the utility room: the belly hatch and its ladder, the suit, the ship's air, the plant, the batteries and the panel

    private Part Utility()
    {
        var p = Big("Folio Utility", "The Folio's utility room aft of the cabin: the belly hatch over the boarding shaft with its ladder, a suit locker, the ship's air, the power plant, the batteries and the Engineer's Panel.");
        var plan = new Rect(Cx - Lw - 0.25, ZCabW + 0.125, Cx + Lw + 0.25, ZEngAW + 0.125);
        Floor(p, [plan], 0, Ft, Deck, "painted deck plate", Shaft);
        Floor(p, [new Rect(plan.X0, ZCabW + 0.125, plan.X1, ZEngAW - 0.125)], CCeil, CRoof, Ceil, "painted plating");
        Room(p, plan, 0, CRoof);
        CeilingAt(p, CCeil);
        foreach (var s in new[] { -1, 1 })
            WallRun(p, false, ZCabW + 0.125, ZEngAW - 0.125, Cx + s * (Lw + 0.125), Ft, CCeil);
        WallRun(p, true, Cx - Lw - 0.25, Cx + Lw + 0.25, ZEngAW, Ft, CCeil);
        HatchRail(p, Shaft, Ft);
        // A grating walk from the door to the hatch's landing, a cable tray and a fuel line along the port wall, a light strip.
        Detail(p, Cx - 0.75, Ft, ZCabW + 0.3, Cx - 0.15, Ft + 0.012, ZEngAW - 0.4, Grate, "painted grating");
        Detail(p, Cx + 0.05, Ft, Shaft.Z1 + 0.15, Cx + 1.0, Ft + 0.012, ZEngAW - 0.7, Grate, "painted grating");
        Detail(p, Cx - Lw + 0.004, 2.15, ZCabW + 0.3, Cx - Lw + 0.09, 2.21, ZEngAW - 0.3, Red, "painted steel");
        Detail(p, Cx - Lw + 0.004, 2.27, ZCabW + 0.3, Cx - Lw + 0.09, 2.33, ZEngAW - 0.3, Link, "painted steel");
        CeilStrip(p, Cx - 0.1, Cx + 0.1, ZCabW + 0.5, ZEngAW - 0.5, CCeil);
        foreach (var s in new[] { -1, 1 })
            foreach (var (i, za, zb) in BandsIn(ZCabEnd, ZEngEnd))
            {
                Band(p, 2, s, za, zb, Skin, 21 + i);
                Band(p, 3, s, za, zb, Skin2, 22 + i, panels: false);
                Chine(p, 2, s, za, zb, 0.2, 0.09, Armour);
                Chine(p, 3, s, za, zb, 0.16, 0.07, Frame);
                // A service panel on the flank by the plant (to port) and by the hatch (to starboard), a vent grille over it.
                Slab(p, 3, s, za, zb, 0.2, 0.55, 0.15, 0.7, 0.02, 0.06, 0.02, Frame, "painted steel");
                for (var k = 0; k < 4; k++)
                    FStrip(p, 3, s, za, zb, 0.24, 0.51, 0.22 + k * 0.12, 0.27 + k * 0.12, 0.062, Dark, "painted steel");
            }
        foreach (var (_, za, zb) in BandsIn(ZCabEnd, ZEngEnd))
            foreach (var (ya, yb) in new[] { (0.0, 1.3), (1.3, CRoof) })
                VoidT(p, za, zb, ya, yb, Lw + 0.25, za == ZCabEnd ? 0.125 : 0.004, zb == ZEngEnd ? 0.0 : 0.004); // (flush aft: it welds to the stern)
        // The fittings' sockets.
        Sock(p, "panel", Cx - Lw, Ft + 0.6, 3.15, "+x");
        Sock(p, "plant", Cx - 1.45, Ft, 4.75, "+y");
        Sock(p, "battery0", Cx - 1.5, Ft, 3.95, "+y");
        Sock(p, "battery1", Cx - 1.15, Ft, 3.95, "+y");
        Sock(p, "board", Cx + Lw, Ft + 0.9, 3.4, "-x");
        Sock(p, "suit", Cx + Lw, Ft + 0.125, 5.2, "-x");
        Sock(p, "lifeSupport", Cx - 0.3, Ft + 0.125, ZEngAW - 0.125, "-z");
        Sock(p, "o2Tank", Cx + 0.55, Ft + 0.125, ZEngAW - 0.125, "-z");
        Sock(p, "lampUtilL", Cx - Lw, Ft + 2.05, 4.3, "+x");
        Sock(p, "lampUtilR", Cx + Lw, Ft + 2.05, 4.3, "-x");
        Sock(p, "boarding", HatchX, Ft - 0.125, Shaft.Z1, "-z");
        foreach (var z in new[] { plan.Z0, plan.Z1 })
            foreach (var y in new[] { 0.0, CRoof })
                MustFit("utility", z, Lw + 0.25, y);
        return p.Stats(4000, 12000, 30, 15.0);
    }

    // ---- the stern: the hull tucking in to a square transom, the two drives' office-case housings on it, the stern's attitude jets

    private Part SternPart()
    {
        var p = Big("Folio Stern", "The Folio's stern: the hull tucking in behind the utility room to a square transom, the two drives' housings on it, the tail lights and the stern's attitude jets. The drives' own tank is in it.", "fueltank", Skin);
        Facet(p, 0, 1, ZEngEnd, Stern, Armour, "plated steel");
        Facet(p, 5, 1, ZEngEnd, Stern, Skin2, "painted plating");
        Slab(p, 5, 1, ZEngEnd, Stern, 0.14, 0.9, 0.14, 0.86, 0.02, 0.08, 0.05, Armour);
        foreach (var s in new[] { -1, 1 })
        {
            for (var k = 1; k <= 4; k++)
            {
                Facet(p, k, s, ZEngEnd, Stern, k % 2 == 0 ? Skin : Skin2, "painted plating");
                Slab(p, k, s, ZEngEnd, Stern, 0.14, 0.92, 0.12, 0.88, 0.02, k == 2 ? 0.11 : 0.08, 0.05, k == 3 ? Armour : Skin, "painted plating");
            }
            Chine(p, 1, s, ZEngEnd, Stern, 0.16, 0.07, Frame);
            Chine(p, 2, s, ZEngEnd, Stern, 0.2, 0.09, Armour);
            Chine(p, 3, s, ZEngEnd, Stern, 0.16, 0.07, Frame);
            Chine(p, 4, s, ZEngEnd, Stern, 0.14, 0.07, Armour);
        }
        Ribs(p, ZEngEnd, Stern, 0, 1, 2, 3, 4, 5);
        // The transom: a dark plate; on it each drive's housing, an office case on its side: a cream box with a vent grille, a blue
        // bezel round the mount, yellow corner tabs.
        var q = At(Stern);
        Plate(p, [.. Enumerable.Range(0, 5).Select(k => P(Cx + q[k].H, q[k].Y, Stern)).Concat(Enumerable.Range(0, 5).Reverse().Select(k => P(Cx - q[k].H, q[k].Y, Stern)))], Frame, "soot");
        foreach (var s in new[] { -1, 1 })
        {
            double x = Cx + s * DriveX, y = DriveY, z0 = Stern - 0.4, z1 = Stern + 0.25;
            Solid(p, x - 0.5, y - 0.5, z0, x + 0.5, y + 0.5, z1, Skin, "painted plating");
            // (the bezel a ring round the mount, clear of the drive's shroud)
            Detail(p, x - 0.53, y - 0.53, z1 - 0.06, x + 0.53, y - 0.42, z1 + 0.03, Armour, "painted plating");
            Detail(p, x - 0.53, y + 0.42, z1 - 0.06, x + 0.53, y + 0.53, z1 + 0.03, Armour, "painted plating");
            Detail(p, x - 0.53, y - 0.416, z1 - 0.064, x - 0.42, y + 0.416, z1 + 0.026, Armour, "painted plating");
            Detail(p, x + 0.42, y - 0.416, z1 - 0.064, x + 0.53, y + 0.416, z1 + 0.026, Armour, "painted plating");
            foreach (var (cx, cy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                Detail(p, x + cx * 0.475 - 0.04, y + cy * 0.475 - 0.04, z1 + 0.03, x + cx * 0.475 + 0.04, y + cy * 0.475 + 0.04, z1 + 0.042, Hazard, "hazard paint");
            for (var k = 0; k < 5; k++)
                Detail(p, x + s * 0.5, y - 0.36 + k * 0.16, z0 + 0.06, x + s * 0.512, y - 0.3 + k * 0.16, z1 - 0.1, Dark, "painted steel");
            Sock(p, s < 0 ? "driveL" : "driveR", x, y, z1, "+z");
            // Tail lights and a hazard band over the drives.
            Detail(p, Cx + s * 1.35 - 0.1, 0.6, Stern + 0.02, Cx + s * 1.35 + 0.1, 0.9, Stern + 0.06, Red, glow: true);
            Detail(p, Cx + s * 0.85 - 0.4, 2.0, Stern + 0.02, Cx + s * 0.85 + 0.4, 2.05, Stern + 0.05, Hazard, "hazard paint");
        }
        foreach (var (ya, yb) in new[] { (-0.4, 0.0), (0.0, 1.3), (1.3, 2.6), (2.6, 3.0) })
        {
            VoidT(p, ZEngEnd, Stern, ya, yb, 0, 0.0, 0.03);
            VoidT(p, ZEngEnd, ZEngEnd + 0.4, ya, yb, 0, 0.0, 0.0); // (the bulkhead's void, full width: it welds to the utility room's)
        }
        var sq = At(SternRcsZ);
        foreach (var s in new[] { -1, 1 })
        {
            var n = s < 0 ? "L" : "R";
            RcsPad(p, "rcsUp" + n, Cx + s * 0.6, sq[4].Y, SternRcsZ, "+y", 0.05, 0.4);
            RcsPad(p, "rcsDown" + n, Cx + s * 0.6, sq[0].Y, SternRcsZ, "-y", 0.05, 0.4);
            RcsPad(p, "rcsOut" + n, FacetX(2, s, SternRcsZ, 0.8), 0.8, SternRcsZ, s < 0 ? "-x" : "+x", 0.2, 0.4);
        }
        p.Set(("fuelTank", Kw(("capacity", SternFuel)))); // (the drives draw from the tank they stand on)
        FuelPort(p, 0.6);
        return p.Stats(2500, 9000, 35, 10.0);
    }

    /// <summary>The nose cap: a rounded dome sealing the blunt glass nose, lofted from the first station's section in shrinking rings,
    /// a dark rim where it meets the glass, a blue collar, the beacon on its face.</summary>
    private Part NoseCap()
    {
        var p = Big("Folio Nose Cap", "The Folio's nose cap: a rounded plate dome sealing the front of the glass cockpit, with the nav beacon on its face.", color: Skin);
        const double cy = 1.25;
        var sec = At(Zt);
        double[][] Ring(double dz, double k)
        {
            var pts = new List<double[]>();
            foreach (var sd in new[] { -1, 1 })
                foreach (var q in sec)
                    pts.Add(Sh([Cx + sd * q.H * k, cy + (q.Y - cy) * k, Zt - dz]));
            return [.. pts];
        }
        (double Dz, double K)[] rings = [(0.004, 1.012), (0.12, 0.985), (0.25, 0.88), (0.36, 0.7), (0.43, 0.45), (0.46, 0.2)];
        for (var i = 0; i + 1 < rings.Length; i++)
            Prism(p, Ring(rings[i].Dz, rings[i].K), Ring(rings[i + 1].Dz, rings[i + 1].K), i == 0 ? Frame : i == 1 ? Armour : Skin, "painted plating");
        Detail(p, Cx - 0.07, cy + 0.25, Zt - 0.48, Cx + 0.07, cy + 0.37, Zt - 0.42, Red, glow: true);
        Hidden(p, Cx - 0.5, Ft, Zt - 0.25, Cx + 0.5, 1.7, ZCkFloor - 0.4);
        return p.Stats(400, 3000, 30, 8.0);
    }

    // ---- the Folio's own fittings, drawn by the kit from its specs

    /// <summary>The lift jets: short bells in square louvred shrouds (the Traders' office-case vents), ten-sided, a hazard band.</summary>
    private Part LiftJet() => Jet(new JetSpec("Folio Lift Jet",
        $"A lift jet: {LiftForce / 1e3:0} kN straight down from a short bell in a louvred shroud. Four under the Folio's keel hold it up, from the keel's tanks.",
        JetAxis.Lift, LiftForce, Burn, 0.5, [(0.33, 0.2), (0.42, 0.17), (0.64, 0.25), (0.76, 0.28)], Sides: 10,
        ShroudW: 0.34, ShroudL: 0.33, Louvres: 2, BandAt: 0.2, Mass: 450, Health: 3000));

    /// <summary>The drives: a twelve-sided bell out of a square bezel, long and flared, on the transom's housings.</summary>
    private Part Drive() => Jet(new JetSpec("Folio Drive",
        $"One of the Folio's two drives, {DriveForce / 1e3:0} kN, out of an office-case housing on its transom. Small enough to need no works behind it.",
        JetAxis.Aft, DriveForce, Burn, 0.7, [(0.3, 0.26), (0.4, 0.22), (0.75, 0.33), (1.0, 0.38)], Sides: 12,
        ShroudW: 0.36, ShroudL: 0.3, Louvres: 0, BandAt: 0.15, Body: Frame, Mass: 600, Health: 3500));

    /// <summary>The power plant: two tall cells one behind the other in a narrow cage against the wall, like a pair of tower cases.</summary>
    private Part Plant() => PowerPlant(new PlantSpec("Folio Power Plant",
        "The Folio's power plant: two tall cells one behind the other in a narrow cage, 160 kW for the lights, the air and the flight computer. No fuel.",
        160, 0.24, 0.42, 0.15, [(0.0, -0.19), (0.0, 0.19)], 0.16, 1.35, 12, 1.05, 0.03, 1.45, [(-0.12, Red), (0.12, Link)], Mass: 1400, Health: 2500));

    /// <summary>The flight computer: a narrow console with a raked top between the couches, one wide screen and a row of keys.</summary>
    private Part Computer() => FlightComputer(new ConsoleSpec("Folio Flight Computer",
        "The Folio's flight computer: a flight stabilizer with a light craft's gyro in a narrow raked console between the couches. Fit it level, its arrow forward.",
        5000000, 0.26, 0.5, 0.78, 0.55, [(-0.2, 0.2, 0.28, 0.52, Style.Screen)], 4, 0.16));

    private Part BoardingLadder(double rise) => Ladder(new LadderSpec("Folio Boarding Ladder",
        "The Folio's boarding ladder: it stands on the belly hatch, stowed up through the utility room's floor while the hatch is shut, and runs from the ground up the shaft when it is open. F climbs it.",
        Py.Round(rise, 3), -0.03, "foot", [0, 0, -0.1], "-y", Rung: 0.28, Standoff: 1.0, HalfW: 0.22));

    private (Part Hatch, double Rise) Hatch()
    {
        var bottom = At(Shaft.Z1)[0].Y - 0.012;
        var top = bottom + 0.08;
        var travel = Math.Min(bottom - Ground - 0.005, CCeil - 0.03 - Ft);
        var hatch = BellyHatch(new HatchSpec(HatchName,
            "The Folio's belly hatch: a plate that shuts the boarding shaft under the keel. F (or the utility room's switch) slides it down to the ground, and the boarding ladder standing on it comes down with it.",
            Shaft, bottom, top, Ft, HatchX, Shaft.Z1 - 0.03, travel, Stripes: 3));
        return (hatch, Ft - (top - travel));
    }

    // ---- the blueprint

    private List<string> order = [];

    private PyDict Blueprint(Dictionary<string, PyDict> defs)
    {
        var bp = new BuildingParts.Blueprint(ShipName, "The Traders' shuttle: a firm's people and its clients between a station and a ship, or down to a landing field. A glass cockpit in the blunt nose, a client cabin of six couches under a band of windows, a utility room aft with the belly hatch and its ladder, the ship's air and the plant; two drives on the transom and four lift jets under the keel.", defs, maxSteps: 250);
        bp.Conduit = true;
        var placed = PlaceStructure(bp, order);
        int keel = placed["Folio Keel"], roof = placed["Folio Roof"], cab = placed["Folio Cabin"], util = placed["Folio Utility"], stern = placed["Folio Stern"];
        HangDoor(bp, cab, "doorCk");
        HangDoor(bp, cab, "doorAft");
        // The cockpit.
        var pilot = Hang(bp, "Flight Couch", cab, "pilot", "bottom");
        Hang(bp, "Flight Couch", cab, "copilot", "bottom");
        var fc = Hang(bp, "Folio Flight Computer", cab, "computer", "bottom");
        bp.Steps[fc]["config"] = ShipParts.ShipFlight(pitchDamping: 200);
        bp.Wires.Add((pilot, fc));
        // The client cabin: six couches in the Traders' blue, the coffee and a locker by the aft door.
        for (var r = 0; r < Rows.Length; r++)
            foreach (var n in new[] { "L", "R" })
                Paint(bp, Hang(bp, "Flight Couch", cab, $"seat{r}{n}", "bottom"), Armour);
        Hang(bp, "Galley Coffee Station", cab, "coffee", "wall");
        Paint(bp, Hang(bp, "Service Locker", cab, "locker", "bottom"), Armour);
        // The utility room.
        var hatch = Hang(bp, HatchName, util, "boarding", "hang");
        Paint(bp, bp.Hang("Folio Boarding Ladder", hatch, "ladder", "foot"), Frame, "painted steel");
        Switch(bp, cab, "switchHatch", false, hatch);
        Hang(bp, "Engineer's Panel", util, "panel", "back");
        Hang(bp, "Power Board", util, "board", "back");
        Hang(bp, "Folio Power Plant", util, "plant", "bottom");
        Hang(bp, "Battery", util, "battery0", "-y");
        Hang(bp, "Battery", util, "battery1", "-y");
        Hang(bp, EvaParts.LockerName, util, "suit", "wall");
        Hang(bp, EvaParts.LifeSupportName, util, "lifeSupport", "wall");
        Hang(bp, EvaParts.OxygenTankName, util, "o2Tank", "wall");
        // The drives, the jets, the legs.
        foreach (var n in new[] { "L", "R" })
        {
            Hang(bp, "Folio Drive", stern, $"drive{n}", "mount");
            foreach (var d in new[] { "Up", "Down", "Out" })
                Paint(bp, Hang(bp, Rcs, stern, $"rcs{d}{n}", "base"), Frame, "dirty steel");
            Paint(bp, Hang(bp, Rcs, roof, $"rcsUp{n}", "base"), Frame, "dirty steel");
            Paint(bp, Hang(bp, Rcs, keel, $"rcsDown{n}", "base"), Frame, "dirty steel");
            Paint(bp, Hang(bp, Rcs, cab, $"rcsOut{n}", "base"), Frame, "dirty steel");
            Paint(bp, Hang(bp, Rcs, cab, $"rcsFwd{n}", "base"), Frame, "dirty steel");
        }
        for (var i = 0; i < LiftAt.Length; i++)
            Hang(bp, "Folio Lift Jet", keel, $"lift{i}", "top");
        for (var i = 0; i < LegAt.Length; i++)
            LegPodParts.HangPod(bp, LegPod, Holder(bp, keel, $"leg{i}"), $"leg{i}");
        // Lamps and their switches.
        Switch(bp, cab, "switchCk", Lamp(bp, cab, "lampCkL", "#ffd080"), Lamp(bp, cab, "lampCkR", "#ffd080"));
        Switch(bp, cab, "switchCab", Lamp(bp, cab, "lampL0", "#fff0d8"), Lamp(bp, cab, "lampL1", "#fff0d8"), Lamp(bp, cab, "lampR0", "#fff0d8"), Lamp(bp, cab, "lampR1", "#fff0d8"));
        Switch(bp, cab, "switchUtil", Lamp(bp, util, "lampUtilL", "#a0d0ff"), Lamp(bp, util, "lampUtilR", "#ffd040"));
        return bp.Json();
    }

    public void Make(PartOutput o)
    {
        Reset();
        var structure = new List<Part>();
        structure.AddRange(SplitBands(Keel(), "Keel", 0.0));
        structure.AddRange(SplitDeck(Cabin(), "Cabin"));
        structure.AddRange(SplitDeck(Utility(), "Utility"));
        structure.AddRange(SplitBands(Roof(), "Roof", 0.0));
        structure.Add(SternPart());
        structure.Add(NoseCap());
        Connect(structure);
        order = PlaceOrder(structure, "Folio Keel");
        var (hatch, rise) = Hatch();
        Emit(o, Out, [.. structure, hatch, BoardingLadder(rise), LiftJet(), Drive(), Plant(), Computer()], $"{BpOut}/FolioShuttle.json", Blueprint);
    }
}
