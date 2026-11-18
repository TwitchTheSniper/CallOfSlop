using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.Generators.StationLook;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The berth clamp arm, a design draft for the Station gallery (Karl, 2026-11-18): a station's berths no longer have to match each ship's
/// shape. A ship flies its berth's approach lane (Game.StationLanes, GameNavDock.NavDockInSpace) to the stage point, 30 m out from the
/// berth's box on the lane axis; there a long telescoping arm on the docking arm's top reaches out, its magnetic clamp head touches the
/// hull, the ship's drives and flight stop, and the arm draws it in to its fixed berth pose. Once moored the ship is not simulated for
/// movement at all; a craft it releases (a Shuttle) flies as normal.
///
/// The blueprint "Clamp Arm Demo" is a storyboard: one length of docking arm with four Zero Day berths, each showing a step: 1 on the
/// lane, the arm stowed; 2 at the stage point, the arm reaching (its pad amber, seeking); 3 clamped and drawing in; 4 moored (pad green).
/// The ships are glass outlines of the Zero Day's size, the lane its marker lamps. Looks only: the arm doesn't move in the game yet.
/// </summary>
public sealed partial class SpaceStationParts
{
    public const string ClampDemoName = "Clamp Arm Demo";

    /// <summary>The clamp arm's numbers: five nested boom sections 13 m each (so about 12 m folded, 60 m out), the shoulder's height over
    /// the arm's top, the head from wrist ball to pad.</summary>
    private const double BoomSect = 13, BoomTail = 1.5, ShoulderUp = 4.0, HeadLen = 4.2, MinSlide = 0.15;
    private static readonly double[] BoomT = [3.2, 2.7, 2.25, 1.85, 1.5];

    private const string Seek = "#ffb02e", Locked = "#3bff74", Off = "#3a4046", Accent = "#3fd0ff";

    private enum ArmPose { Stowed, Reach, Draw, Moored }

    private static Kit ClampArmDemo()
    {
        var k = new Kit("Clamp Demo", "Clamp arm demo");
        var fr = new ArmFrame(1, 0); // (along x; the berths on +z)
        const double yb = 0, pitch = 65;
        var ship = ZeroDay;
        var mids = new[] { 35.0, 100.0, 165.0, 230.0 };
        Arm(k, "Arm", fr, 0, 266, yb, [.. mids.Select(m => (m - ship.L / 2, m + ship.L / 2))], false, 50);

        // where a berth's ship sits (its middle across the arm), the berth box's half extent on the lane axis, and the lane's stage point
        // (GameNavDock: the stage is the box's half extent + 30 m out from its centre; moored within half extent + 15 m)
        var berthC = ArmHalf + 3.5 + ship.W / 2;
        var half = (ship.W + 4) / 2;
        var stage = berthC + half + 30;
        var bottom = yb - 3.6;
        var shipTop = bottom + ship.H;

        var steps = new (ArmPose Pose, double ShipC)[] { (ArmPose.Stowed, stage + 40), (ArmPose.Reach, stage), (ArmPose.Draw, berthC + 20), (ArmPose.Moored, berthC) };
        for (var i = 0; i < mids.Length; i++)
        {
            var mid = mids[i];
            var n = i + 1;
            Cradle(k, $"Cradle {n} 1", fr, 1, mid - 18, yb, bottom, ship.W, ship.L, ship.H, false);
            Cradle(k, $"Cradle {n} 2", fr, 1, mid, yb, bottom - 4, ship.W, ship.L, ship.H, true);
            Cradle(k, $"Cradle {n} 3", fr, 1, mid + 18, yb, bottom, ship.W, ship.L, ship.H, false);
            var (pose, shipC) = steps[i];
            ClampArm(k, $"Clamp Arm {n}", mid, yb + ArmH, pose, shipC - 3, shipTop);
            Approach(k, $"Approach {n}", mid, yb, shipC, bottom, ship, pose, stage, mid + pitch / 2 - 8);
        }
        return k;
    }

    /// <summary>
    /// One clamp arm on the docking arm's top at x = a (deck: the arm's top): a turntable and hex housing, a yoke and shoulder pivot,
    /// a five-section telescoping boom with collars and a lifting ram, a wrist ball, and the clamp head hanging under it: neck, hex
    /// housing, a magnetic pad ringed by a status lamp, and four talons. gripZ: where across the arm the pad meets the hull's top (y top).
    /// </summary>
    private static void ClampArm(Kit k, string name, double a, double deck, ArmPose pose, double gripZ, double top)
    {
        var p = k.Piece(name, "A berth clamp arm: a turntable on the docking arm, a five-section telescoping boom and a magnetic clamp head that reaches out to a ship at the end of its approach lane and draws it in to the berth.", Hazard);
        var S = new[] { a, deck + ShoulderUp, 0 };
        // where the pad meets the hull, and the wrist over it
        double[] G = pose == ArmPose.Stowed ? [a - 13.2, deck + 1.1, 0] : [a, top, gripZ];
        double[] W = [G[0], G[1] + HeadLen, G[2]];
        var d = new[] { W[0] - S[0], W[1] - S[1], W[2] - S[2] };
        var len = Math.Sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
        var u = new[] { d[0] / len, d[1] / len, d[2] / len };
        double[] At(double s) => [S[0] + u[0] * s, S[1] + u[1] * s, S[2] + u[2] * s];
        var slide = Math.Max(MinSlide, (len + BoomTail - BoomSect) / (BoomT.Length - 1));
        if (slide > BoomSect - 1.5)
            throw new InvalidOperationException($"space_station: {name} can't reach {len:0.0} m (the boom's sections would overlap under 1.5 m)");
        var pad = pose switch { ArmPose.Stowed => Off, ArmPose.Reach => Seek, _ => Locked };

        // the turntable: a hidden weld block on the arm's top chord, a drum and a hex housing
        Hidden(p, a - 2, deck, -2, a + 2, deck + 0.8, 2);
        Cyl(p, 'y', a, 0, 0, 2.6, deck + 0.003, deck + 0.8, Frame, Dirty);
        Detail(p, a - 2.62, deck + 0.35, -2.62, a + 2.62, deck + 0.5, 2.62, Hazard, HazardFin);
        Hull(p, HexRing(a, 0, 2.4, deck + 0.805).Concat(HexRing(a, 0, 2.0, deck + 2.6)).ToList(), Plate2, Plated);
        Lamp(p, a - 0.3, deck + 2.0, -2.3, a + 0.3, deck + 2.3, -2.2, pad);
        // the yoke: two cheeks either side of the boom's swing plane, the pivot through them
        var alongX = Math.Abs(u[0]) > Math.Abs(u[2]);
        foreach (var s in new[] { -1, 1 })
        {
            if (alongX)
                Detail(p, a - 1.6, deck + 2.605, s * 1.75, a + 1.6, S[1] + 1.4, s * 2.3, Hazard, HazardFin);
            else
                Detail(p, a + s * 1.75, deck + 2.605, -1.6, a + s * 2.3, S[1] + 1.4, 1.6, Hazard, HazardFin);
        }
        if (alongX)
            Cyl(p, 'z', a, S[1], 0, 0.95, -2.5, 2.5, Frame, Steel);
        else
            Cyl(p, 'x', 0, S[1], 0, 0.95, a - 2.5, a + 2.5, Frame, Steel);

        // the boom: nested square sections, each slid out of the last, a collar at each mouth
        for (var i = 0; i < BoomT.Length; i++)
        {
            var s0 = -BoomTail + i * slide;
            var s1 = i == BoomT.Length - 1 ? len : s0 + BoomSect;
            var col = i % 2 == 0 ? Plate : Plate3;
            Bar(p, At(s0), At(s1), BoomT[i], col, Plated);
            if (i < BoomT.Length - 1)
            {
                Bar(p, At(s0 + BoomSect - 0.5), At(s0 + BoomSect + 0.04), BoomT[i] + 0.25, Hazard, HazardFin);
                Bar(p, At(s0 + BoomSect - 0.32), At(s0 + BoomSect - 0.18), BoomT[i] + 0.29, Accent, Steel, glow: true);
            }
        }
        // the lifting ram from the housing's front to the first section, and a counterweight on the tail
        var ramFoot = new[] { S[0] + u[0] * 2.2, deck + 1.6, S[2] + u[2] * 2.2 };
        var ramTop = At(5.5);
        Bar(p, ramFoot, [ramTop[0], ramTop[1] - 1.65, ramTop[2]], 0.8, Frame, Steel);
        Bar(p, At(-BoomTail - 1.6), At(-BoomTail + 0.002), 3.7, Frame, Dirty);
        // a feed hose along the boom's top
        Bar(p, [S[0], S[1] + 1.75, S[2]], [W[0], W[1] + 1.2, W[2]], 0.3, Tank, Rusty);

        // the wrist and the clamp head
        Sphere(p, W[0], W[1], W[2], 1.25, Frame, Steel);
        GlowCyl(p, W[0], W[2], 1.3, W[1] - 0.12, W[1] + 0.12, pad);
        Cyl(p, 'y', W[0], 0, W[2], 0.75, G[1] + 2.0, W[1] - 0.6, Plate2, Plated);
        foreach (var s in new[] { -1, 1 })
            Bar(p, [W[0] + s * 1.15, W[1] - 0.2, W[2]], [G[0] + s * 1.6, G[1] + 2.02, G[2]], 0.34, Frame, Steel);
        Hull(p, HexRing(G[0], G[2], 2.3, G[1] + 0.385).Concat(HexRing(G[0], G[2], 1.85, G[1] + 2.0)).ToList(), Hazard, HazardFin);
        Cyl(p, 'y', G[0], 0, G[2], 2.4, G[1] + 0.02, G[1] + 0.38, HazardDark, "rubber");
        GlowCyl(p, G[0], G[2], 2.46, G[1] + 0.13, G[1] + 0.27, pad);
        Lamp(p, G[0] - 0.3, G[1] + 1.0, G[2] - 2.07, G[0] + 0.3, G[1] + 1.4, G[2] - 1.95, LampCol);
        // talons: splayed while it seeks, closed on the hull once it holds
        var (reach, foot) = pose switch { ArmPose.Reach => (3.9, 0.7), ArmPose.Stowed => (3.2, 0.5), _ => (2.85, 0.06) };
        for (var t = 0; t < 4; t++)
        {
            var ang = Math.PI / 4 + t * Math.PI / 2;
            double cx = Math.Cos(ang), cz = Math.Sin(ang);
            double[] root = [G[0] + cx * 1.9, G[1] + 1.6, G[2] + cz * 1.9];
            double[] knee = [G[0] + cx * (reach + 0.4), G[1] + 1.15, G[2] + cz * (reach + 0.3)];
            double[] tip = [G[0] + cx * reach, G[1] + foot, G[2] + cz * reach];
            Bar(p, root, knee, 0.4, Frame, Steel);
            Bar(p, knee, tip, 0.32, Hazard, HazardFin);
        }
        if (pose == ArmPose.Stowed) // the rest saddle on the arm's top under the parked head
            Detail(p, G[0] - 2.0, deck + 0.003, -2.0, G[0] + 2.0, G[1] - 0.03, 2.0, Frame, Dirty);
        Finish(p);
    }

    /// <summary>
    /// One step's ship and lane: the ship as a glass outline of its size (bow at -x), the lane's marker lamps on its axis out to 150 m,
    /// corner brackets at the stage point, a dashed mooring line. A hidden rod under the arm's bottom chord welds it to the arm at x =
    /// weldA (the game wants every piece joined; the rod is never drawn).
    /// </summary>
    private static void Approach(Kit k, string name, double a, double yb, double shipC, double bottom, (double W, double L, double H) ship, ArmPose pose, double stage, double weldA)
    {
        var p = k.Piece(name, "Design aid for the clamp arm demo: the ship as a glass outline and the berth's approach lane as marker lamps.", Window);
        var cy = bottom + ship.H / 2;
        Hidden(p, weldA, yb + 0.2, ArmHalf, weldA + 0.4, yb + 0.8, shipC);
        // the ship: an octagon of its width and height, chamfered 3 m, the bow narrowed
        double hw = ship.W / 2, hh = ship.H / 2, ch = 3;
        List<double[]> Oct(double x, double sc, double dy) =>
        [
            [x, cy + dy - hh * sc, shipC - (hw - ch) * sc], [x, cy + dy - hh * sc, shipC + (hw - ch) * sc],
            [x, cy + dy - (hh - ch) * sc, shipC + hw * sc], [x, cy + dy + (hh - ch) * sc, shipC + hw * sc],
            [x, cy + dy + hh * sc, shipC + (hw - ch) * sc], [x, cy + dy + hh * sc, shipC - (hw - ch) * sc],
            [x, cy + dy + (hh - ch) * sc, shipC - hw * sc], [x, cy + dy - (hh - ch) * sc, shipC - hw * sc],
        ];
        var pts = Oct(a - ship.L / 2, 0.55, 1.5).Concat(Oct(a - ship.L / 2 + 14, 1, 0)).Concat(Oct(a + ship.L / 2, 1, 0)).Select(q => StationLook.P(q[0], q[1], q[2])).ToList();
        double[] mn = [.. Enumerable.Range(0, 3).Select(i => pts.Min(q => q[i]))], mx = [.. Enumerable.Range(0, 3).Select(i => pts.Max(q => q[i]))];
        p.Add(Box(mn, mx, Window, detail: true, glass: true, shape: Kw(("shape", "hull"), ("points", pts.Select(q => (object?)q.Select(v => (object?)R(v)).ToList()).ToList()))));
        // drive glow at the stern while it flies; dark once the clamp holds
        if (pose is ArmPose.Stowed or ArmPose.Reach)
            foreach (var s in new[] { -1, 1 })
                Cyl(p, 'x', 0, cy - 1, shipC + s * 4, 1.4, a + ship.L / 2 + 0.05, a + ship.L / 2 + 0.35, Glow, Steel);
        // the lane: marker lamps on its axis every 6 m, from past the ship out to 150 m
        for (var z = Math.Max(stage, shipC) + hw + 4; z < 150; z += 6)
            Lamp(p, a - 0.35, cy - 0.35, z, a + 0.35, cy + 0.35, z + 0.7, z % 12 < 6 ? Seek : LampCol);
        // the stage point: corner brackets round where the ship stops, at its middle
        foreach (var sx in new[] { -1, 1 })
            foreach (var sy in new[] { -1, 1 })
            {
                double x = a + sx * (ship.L / 2 + 3), y = cy + sy * (hh + 3);
                Lamp(p, x - (sx < 0 ? 0 : 4), y - 0.15, stage - 0.15, x + (sx < 0 ? 4 : 0), y + 0.15, stage + 0.15, Accent);
                Lamp(p, x - 0.15, y - (sy < 0 ? 0 : 4), stage - 0.15, x + 0.15, y + (sy < 0 ? 4 : 0), stage + 0.15, Accent);
            }
        Finish(p, 1);
    }

    private static IEnumerable<double[]> HexRing(double x, double z, double r, double y) =>
        Enumerable.Range(0, 6).Select(i => new[] { x + r * Math.Cos(i * Math.PI / 3 + Math.PI / 6), y, z + r * Math.Sin(i * Math.PI / 3 + Math.PI / 6) });
}

// ---- the clamp arms in the game (Karl, 2026-11-18: "It looks so good!")

public sealed partial class SpaceStationParts
{
    /// <summary>How far the arm reaches from its shoulder to a hull (m): past the lane's stage point on every Highport berth.</summary>
    public const double ClampReach = 60;

    /// <summary>
    /// A berth's clamp arm base on a docking arm's top at along a, a little to the berth's side (side +1 or -1, so each berth's arm is
    /// nearest its own berth): the drum, a hidden weld block on the top chord, and the saddle its head rests on when stowed, 13 m
    /// along the arm away from the berth's middle (stowDir). The moving arm (yoke, boom, head) is drawn by the game from the
    /// "clampArm" block: its shoulder, where its wrist rests stowed, its reach (ClampArmRender.cs, GameStationArms.cs).
    /// </summary>
    private static Part ClampArmBase(Kit k, string name, ArmFrame fr, double a, int side, double yb, int stowDir, double stowAlong = ClampStowAlong)
    {
        var p = k.Piece(name, "A berth's clamp arm: a turntable on the docking arm's top. Its telescoping boom reaches out to a ship at the end of the berth's approach lane, its magnetic clamp takes the hull and it draws the ship in to the berth.", Frame);
        var deck = yb + ArmH;
        var c = side * 0.6;
        var (x, z) = fr.At(a, c);
        fr.Hidden(p, a - 1.4, a + 1.4, c - 1.4, c + 1.4, deck, deck + 0.8);
        Cyl(p, 'y', x, 0, z, 2.15, deck + 0.003, deck + 0.8, Frame, Dirty);
        Shaped(p, x - 2.17, deck + 0.35, z - 2.17, x + 2.17, deck + 0.5, z + 2.17, Hazard, HazardFin, "cylinder", "+y");
        // the saddle the parked head rests on
        var (sx, sz) = fr.At(a + stowDir * stowAlong, c);
        Detail(p, sx - 2.0, deck + 0.003, sz - 2.0, sx + 2.0, deck + ClampStowPad - 0.03, sz + 2.0, Frame, Dirty);
        Lamp(p, sx - 2.02, deck + 0.3, sz - 0.3, sx + 2.02, deck + 0.5, sz + 0.3, LampCol);
        var shoulder = P(x, deck + ShoulderUp, z);
        var (wx, wz) = fr.At(a + stowDir * stowAlong, c);
        var stow = P(wx, deck + ClampStowPad + HeadLen, wz);
        p.Set(("clampArm", Kw(("shoulder", Pt(shoulder)), ("stow", Pt(stow)), ("reach", ClampReach))));
        return Finish(p);
    }

    private const double ClampStowAlong = 13.2, ClampStowPad = 1.1;

    private static List<object?> Pt(double[] v) => [.. v.Select(q => (object?)R(q))];

    /// <summary>
    /// The arm's moving pieces, drawn by the game where the arm is (ClampArmRender.cs), never placed: each in its own frame, the
    /// shoulder pivot or the wrist at the origin (P adds the half cell; the game takes it back off). The yoke turns about +y, its
    /// boom swinging in its y-z plane; boom section k runs along +z from 0 to its length; the head hangs from the wrist down -y to its
    /// pad. Three heads: parked (dark), seeking (talons open, amber) and holding (talons shut, green).
    /// </summary>
    private static List<Part> ClampArmPieces(Kit k)
    {
        var all = new List<Part>();
        var y = k.Piece("Clamp Arm Yoke", "A clamp arm's turning housing and shoulder yoke (drawn where the arm points).", Hazard);
        var dk = -ShoulderUp; // (the drum's top, under the pivot)
        Hidden(y, -0.2, -0.2, -0.2, 0.2, 0.2, 0.2);
        Sock(y, "pivot", 0, 0.2, 0, "+y");
        Hull(y, HexRing(0, 0, 2.0, dk + 0.805).Concat(HexRing(0, 0, 1.7, dk + 2.6)).ToList(), Plate2, Plated);
        foreach (var s in new[] { -1, 1 })
            Detail(y, s * 1.75, dk + 2.605, -1.6, s * 2.3, 1.4, 1.6, Hazard, HazardFin);
        Cyl(y, 'x', 0, 0, 0, 0.95, -2.5, 2.5, Frame, Steel);
        Lamp(y, -0.3, dk + 2.0, -1.95, 0.3, dk + 2.3, -1.85, LampCol);
        all.Add(Finish(y, 1));
        for (var i = 0; i < BoomT.Length; i++)
        {
            var b = k.Piece($"Clamp Arm Boom {i + 1}", "A clamp arm's telescoping boom section (drawn where the arm reaches).", Plate);
            var t = BoomT[i] / 2;
            Hidden(b, -0.2, -0.2, 1, 0.2, 0.2, 1.4);
            Sock(b, "root", 0, 0, 0, "-z");
            Solid(b, -t, -t, 0, t, t, BoomSect, i % 2 == 0 ? Plate : Plate3, Plated);
            if (i == 0)
            {
                Detail(b, -t - 0.35, -t - 0.35, -1.6, t + 0.35, t + 0.35, 0.002, Frame, Dirty); // (the counterweight behind the pivot)
                Detail(b, -0.15, t + 0.05, 0.5, 0.15, t + 0.35, BoomSect - 0.6, Tank, Rusty); // (the feed hose along its top)
            }
            if (i < BoomT.Length - 1)
            {
                var c = t + 0.125;
                Detail(b, -c, -c, BoomSect - 0.5, c, c, BoomSect + 0.04, Hazard, HazardFin);
                Lamp(b, -c - 0.02, -c - 0.02, BoomSect - 0.32, c + 0.02, c + 0.02, BoomSect - 0.18, "#3fd0ff");
            }
            all.Add(Finish(b, 1));
        }
        foreach (var (nm, pad, open) in new[] { ("Clamp Arm Head", Off, false), ("Clamp Arm Head Seek", Seek, true), ("Clamp Arm Head Lock", Locked, false) })
        {
            var h = k.Piece(nm, "A clamp arm's magnetic clamp head: wrist, neck, housing, pad and talons (drawn where the arm holds).", Hazard);
            double g = -HeadLen; // (the pad's face)
            Hidden(h, -0.2, -0.2, -0.2, 0.2, 0.2, 0.2);
            Sock(h, "wrist", 0, 0.2, 0, "+y");
            Sphere(h, 0, 0, 0, 1.25, Frame, Steel);
            Shaped(h, -1.3, -0.12, -1.3, 1.3, 0.12, 1.3, pad, Steel, "cylinder", "+y", ("glow", true), ("paintable", false));
            Cyl(h, 'y', 0, 0, 0, 0.75, g + 2.0, -0.6, Plate2, Plated);
            foreach (var s in new[] { -1, 1 })
                Bar(h, [s * 1.15, -0.2, 0], [s * 1.6, g + 2.02, 0], 0.34, Frame, Steel);
            Hull(h, HexRing(0, 0, 2.3, g + 0.385).Concat(HexRing(0, 0, 1.85, g + 2.0)).ToList(), Hazard, HazardFin);
            Cyl(h, 'y', 0, 0, 0, 2.4, g + 0.02, g + 0.38, HazardDark, "rubber");
            Shaped(h, -2.46, g + 0.13, -2.46, 2.46, g + 0.27, 2.46, pad, Steel, "cylinder", "+y", ("glow", true), ("paintable", false));
            Lamp(h, -0.3, g + 1.0, -2.07, 0.3, g + 1.4, -1.95, LampCol);
            var (reach, foot) = open ? (3.9, 0.7) : (2.85, 0.06);
            for (var t = 0; t < 4; t++)
            {
                var ang = Math.PI / 4 + t * Math.PI / 2;
                double cx = Math.Cos(ang), cz = Math.Sin(ang);
                Bar(h, [cx * 1.9, g + 1.6, cz * 1.9], [cx * (reach + 0.4), g + 1.15, cz * (reach + 0.4)], 0.4, Frame, Steel);
                Bar(h, [cx * (reach + 0.4), g + 1.15, cz * (reach + 0.4)], [cx * reach, g + foot, cz * reach], 0.32, Hazard, HazardFin);
            }
            all.Add(Finish(h, 1));
        }
        return all;
    }
}
