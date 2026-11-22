using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Compact landing gear sets (Karl, 2026-11-21: "Ship doesn't need landing gear so big. Try and design a new few sets to use").
/// The leg pods (LegPodParts) are a canopy, a gantry, a brace and a carriage round one leg, three or four metres of pod under the
/// keel. These are the same working leg without the pod: a shallow mount welded under a keel, a leg on a hinge in it and a sprung
/// foot that rides the leg's ankle level. One set kept (Karl, 2026-11-22, the Tuck and Skid Legs taken out with the other ships):
/// the <b>Ram Leg</b>, a hydraulic ram (a barrel and a chrome rod) in an open box cradle with hazard bars, folding forward; it swings
/// a quarter turn down onto a broad square pad. Industrial, for haulers and yards; the Knacker stands on six.
///
/// The machinery is the pods' (ConstructDoors): the leg is a "held" swing door ("gear": "leg"; its collider stays at the hinge), the foot
/// a held door whose link rides the ankle round the hinge and keeps it level, on a "spring". B in a seat lowers and raises them as it
/// does the pods (no canopy: the legs go at once). Frame: the mount's top at y 0 ("top", +y, under a keel), x across, z along (bow -z).
/// Writes Constructs/Parts/LandingGear; a ship hangs a set with HangGear.
/// </summary>
public sealed class LandingGearParts : IPartGenerator
{
    public string Name => "landing_gear";

    private const string Out = "Constructs/Parts/LandingGear";
    private const string Section = "Landing gear", Group = "Ship";

    private const string Skin = "#b4b2aa", Skin2 = "#9e9c94", Armour = "#3b4148", Armour2 = "#30353b", Frame = "#1f2226",
        Hazard = "#e0b020", Glow = "#ffb040", Chrome = "#9aa0a4", Rubber = "#1a1b1d", Lining = "#2a2e33";

    /// <summary>
    /// A set: its name; the hinge (y under the mount's top, z); the leg's length and which way it lies stowed from the hinge (+1 aft,
    /// -1 forward); how far it swings down (degrees); the leg's half width and half height; the pad's half width and half length, its
    /// top under the ankle and its thickness; the spring's travel and its sag at 1 g; masses.
    /// </summary>
    private sealed record Set(string Name, double PivotY, double PivotZ, double Len, int Dir, double Swing, double LegW, double LegH,
        double PadW, double PadL, double PadTop, double PadT, double Travel, double Sag, double MountMass, double LegMass, double FootMass)
    {
        public double Angle => Dir * Swing; // (about +x: a leg lying aft turns down by a positive turn, one lying forward by a negative)
        public double AnkleZ0 => PivotZ + Dir * Len;
        public (double Y, double Z) Ankle1 => (PivotY - Len * Math.Sin(Swing * Math.PI / 180), PivotZ + Dir * Len * Math.Cos(Swing * Math.PI / 180));
        public double Reach => Py.Round(-Ankle1.Y + PadTop + PadT, 2);
        public double Standing => Py.Round(Reach - Sag, 2);
        public double Hz => Py.Round(Math.Sqrt(18.0 / Sag) / (2 * Math.PI), 3); // (the same 1 g as the pods: x = g / w^2)
        /// <summary>The face of the mount's end wall the stowed pad rests against.</summary>
        public double WallZ => AnkleZ0 + Dir * (PadL + 0.004);
    }

    private static readonly Set Ram = new("Ram Leg", -0.2, 0.7, 1.35, -1, 90, 0.1, 0.09, 0.32, 0.32, 0.1, 0.14, 0.07, 0.04, 220, 130, 90);
    private static readonly Set[] Sets = [Ram];

    /// <summary>Where a set's pad stands under its mount's top, its leg down and its spring in under the ship's weight.</summary>
    public static double StandingOf(string set) => Sets.First(s => s.Name == set).Standing;

    /// <summary>A whole set hung by its mount's top on socket <paramref name="sock"/> of step <paramref name="on"/> (nose forward). Returns the mount's step.</summary>
    public static int HangGear(BuildingParts.Blueprint bp, string set, int on, string sock)
    {
        var m = bp.Hang($"{set} Mount", on, sock, "top");
        GearOn(bp, set, m);
        return m;
    }

    private static void GearOn(BuildingParts.Blueprint bp, string set, int m)
    {
        bp.Hang($"{set} Strut", m, "leg", "hang");
        bp.Hang($"{set} Foot", m, "foot", "hang");
    }

    // ---- helpers (metres; the part kit's boxes)

    private static double[] V(double x, double y, double z) => [Py.Round(x, 4), Py.Round(y, 4), Py.Round(z, 4)];
    private static List<object?> L3(double x, double y, double z) => [Py.Round(x, 4), Py.Round(y, 4), Py.Round(z, 4)];

    private static void D(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string col, string finish, bool glow = false, string? rig = null)
    {
        var shape = glow ? Kw(("glow", true), ("paintable", false)) : Kw(("finish", finish));
        if (rig != null)
            shape["rig"] = rig;
        p.Add(Box(V(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), V(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), col, detail: true, shape: shape));
    }

    private static void S(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string col, string finish) =>
        p.Add(Box(V(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), V(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), col, shape: Kw(("finish", finish))));

    private static void Hidden(Part p, double x0, double y0, double z0, double x1, double y1, double z1) =>
        p.Add(Box(V(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), V(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), hidden: true));

    private static void Hull(Part p, IEnumerable<double[]> pts, string col, string finish, string? rig = null)
    {
        var q = pts.Select(a => V(a[0], a[1], a[2])).ToList();
        double[] mn = [.. Enumerable.Range(0, 3).Select(i => q.Min(a => a[i]))];
        double[] mx = [.. Enumerable.Range(0, 3).Select(i => q.Max(a => a[i]))];
        var shape = Kw(("shape", "hull"), ("points", q.Select(a => (object?)a.Select(x => (object?)x).ToList()).ToList()), ("finish", finish));
        if (rig != null)
            shape["rig"] = rig;
        p.Add(Box(mn, mx, col, detail: true, shape: shape));
    }

    /// <summary>A cylinder along x (a pin), along y (a rod, a round pad) or along z.</summary>
    private static void Cyl(Part p, char axis, double cx, double cy, double cz, double r, double a0, double a1, string col, string finish, int seg = 12, string? rig = null)
    {
        double[] mn, mx;
        string front;
        switch (axis)
        {
            case 'x': mn = V(Math.Min(a0, a1), cy - r, cz - r); mx = V(Math.Max(a0, a1), cy + r, cz + r); front = "+x"; break;
            case 'y': mn = V(cx - r, Math.Min(a0, a1), cz - r); mx = V(cx + r, Math.Max(a0, a1), cz + r); front = "+y"; break;
            default: mn = V(cx - r, cy - r, Math.Min(a0, a1)); mx = V(cx + r, cy + r, Math.Max(a0, a1)); front = "+z"; break;
        }
        var shape = Kw(("shape", "cylinder"), ("segments", seg), ("front", front), ("finish", finish));
        if (rig != null)
            shape["rig"] = rig;
        p.Add(Box(mn, mx, col, detail: true, shape: shape));
    }

    // ---- the mount: a top plate, two side walls, the hinge bracket at one end and the end wall the pad stows against at the other

    private static Part Mount(Set s)
    {
        var p = new Part($"{s.Name} Mount", Small, Section,
            $"A {s.Name.ToLowerInvariant()}'s mount: a shallow bay welded by its top under a keel, the leg's hinge at one end and the wall its foot stows against at the other. B in a seat lowers and raises the leg in it. A generic ship fitting: it fits any ship.",
            role: "frame", tool: "welder", material: "steel", color: Armour, group: Group);
        var e = s.Dir;
        double hingeEnd = s.PivotZ - e * (s.LegH + 0.12), footEnd = s.WallZ; // (the bay's inner ends)
        double z0 = Math.Min(hingeEnd, footEnd), z1 = Math.Max(hingeEnd, footEnd);
        double inW = Math.Max(s.LegW, s.PadW) + 0.03, wallT = 0.05, outW = inW + wallT, depth = s == Ram ? -0.34 : -0.28;
        double padMid = s.PivotY - s.PadTop - s.PadT / 2, lip = Math.Min(depth, s.PivotY - s.PadTop - s.PadT - 0.02);
        // The top plate, the side walls, the end walls (the foot end one deeper: the stowed pad sits against it).
        S(p, -outW, -0.06, z0 - 0.08, outW, 0, z1 + 0.08, s == Ram ? Armour : Skin, "plated steel");
        foreach (var sx in new[] { -1.0, 1.0 })
            S(p, sx * inW, depth, z0 - 0.08, sx * outW, -0.06, z1 + 0.08, s == Ram ? Armour2 : Skin2, "plated steel");
        S(p, -inW, depth, hingeEnd - e * 0.08, inW, -0.06, hingeEnd, Armour, "plated steel");
        S(p, -inW, lip, footEnd, inW, -0.06, footEnd + e * 0.08, Armour, "plated steel");
        // The hinge bracket down from the top to the leg's root eye, its pin's caps.
        S(p, -s.LegW - 0.02, s.PivotY + s.LegH, s.PivotZ - s.LegH, s.LegW + 0.02, -0.06, s.PivotZ + s.LegH, Frame, "dirty steel");
        foreach (var sx in new[] { -1.0, 1.0 })
            Cyl(p, 'x', 0, s.PivotY, s.PivotZ, s.LegH * 0.7, sx * (inW - 0.002), sx * (inW + 0.025 + wallT), Chrome, "chrome");
        // The looks of each set.
        // An open box cradle: the side walls cut to a lattice of bars, hazard bars along the bottom edges, a reservoir on the top.
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            D(p, sx * (outW + 0.004), depth - 0.004, z0 - 0.08, sx * (outW + 0.02), depth + 0.07, z1 + 0.08, Hazard, "hazard paint");
            for (var i = 0; i < 4; i++)
            {
                var z = z0 + 0.15 + i * (z1 - z0 - 0.3) / 3;
                D(p, sx * (outW + 0.004), depth + 0.07, z - 0.04, sx * (outW + 0.03), -0.06, z + 0.04, Frame, "dirty steel");
            }
        }
        Cyl(p, 'z', 0.18, -0.06 - 0.075, 0, 0.07, z0 + 0.1, z0 + 0.5, Hazard, "hazard paint");
        D(p, -0.12, depth - 0.016, footEnd - e * 0.04 - 0.03, 0.12, depth - 0.002, footEnd - e * 0.04 + 0.03, Glow, "", glow: true);
        
        p.Sock("top", [0, 0, 0], "+y");
        p.Sock("leg", V(0, s.PivotY + s.LegH, s.PivotZ), "-y");
        p.Sock("foot", V(0, padMid, footEnd), e > 0 ? "-z" : "+z");
        return p.Stats(s.MountMass, 2500, 30, 4.0);
    }

    // ---- the strut: a held swing door; only its root eye collides

    private static Part Strut(Set s)
    {
        var p = new Part($"{s.Name} Strut", Small, Section,
            $"A {s.Name.ToLowerInvariant()}'s strut: it lies folded in its mount and swings {s.Swing:0} degrees down on its hinge, the foot at its ankle kept level. A generic ship fitting: it fits any ship.",
            role: "door", tool: "welder", material: "steel", color: Armour, group: Group);
        var e = s.Dir;
        double y = s.PivotY, zr = s.PivotZ, za = s.AnkleZ0, w = s.LegW, h = s.LegH;
        double zIn(double t) => zr + e * s.Len * t;
        Cyl(p, 'x', 0, y, zr, h + 0.015, -w + 0.006, w - 0.006, Frame, "dirty steel");
        Cyl(p, 'x', 0, y, za, h, -w + 0.006, w - 0.006, Frame, "dirty steel");
        // A ram: the barrel from the root, the chrome rod out of it to the ankle, a gland ring, a hose along the barrel.
        Cyl(p, 'z', 0, y, 0, h, zIn(0.12), zIn(0.62), Armour, "painted steel", 16);
        Cyl(p, 'z', 0, y, 0, h + 0.012, zIn(0.58), zIn(0.64), Hazard, "hazard paint", 16);
        Cyl(p, 'z', 0, y, 0, h * 0.5, zIn(0.6), zIn(0.9), Chrome, "chrome", 16);
        Cyl(p, 'z', 0.0, y + h + 0.025, 0, 0.018, zIn(0.15), zIn(0.55), Rubber, "rubber", 8);
        D(p, -0.03, y + h - 0.004, zIn(0.13) - 0.03, 0.03, y + h + 0.045, zIn(0.13) + 0.03, Frame, "dirty steel");
        Hidden(p, -w, y - h, zr - h, w, y + h, zr + h);
        p.Sock("hang", V(0, y + h, zr), "+y");
        p.Set(("door", Kw(("kind", "swing"), ("axis", "+x"), ("pivot", L3(0, y, zr)), ("angle", s.Angle), ("held", true), ("seconds", 3.0), ("hang", "hang"), ("gear", "leg"))));
        return p.Stats(s.LegMass, 1800, 30, 4.0);
    }

    // ---- the foot: rides the ankle level, on a sprung rod

    private static Part Foot(Set s)
    {
        var p = new Part($"{s.Name} Foot", Small, Section,
            $"A {s.Name.ToLowerInvariant()}'s foot: it rides the strut's ankle down and back without turning, so it stays level; on the ground its spring gives up to {Py.G(Py.Round(s.Travel * 100, 0))} cm and it stands {Py.G(s.Standing)} m under the mount's top. A generic ship fitting: it fits any ship.",
            role: "door", tool: "welder", material: "steel", color: Armour, group: Group);
        double ay = s.PivotY, az = s.AnkleZ0, top = ay - s.PadTop, bot = top - s.PadT, w = s.LegW;
        double plate = ay - s.LegH - 0.006;
        // The clevis round the ankle: two cheeks, the pin, a plate under them.
        foreach (var sx in new[] { -1.0, 1.0 })
            D(p, sx * (w + 0.006), plate + 0.004, az - 0.1, sx * (w + 0.05), ay + 0.05, az + 0.1, Frame, "dirty steel");
        Cyl(p, 'x', 0, ay, az, 0.035, -w - 0.06, w + 0.06, Chrome, "chrome");
        D(p, -w - 0.05, plate - 0.025, az - 0.11, w + 0.05, plate, az + 0.11, Armour2, "plated steel");
        // What the spring moves: the rod and the pad.
        Cyl(p, 'y', 0, 0, az, 0.03, top + 0.02, plate - 0.008, Chrome, "chrome", 12, "oleo");
        // A broad square pad: a ribbed top, a hazard rim, a thick rubber sole.
        double W = s.PadW;
        D(p, -W, bot + 0.04, az - W, W, top, az + W, Armour, "plated steel", rig: "oleo");
        for (var i = -1; i <= 1; i++)
        {
            D(p, i * 0.18 - 0.02, top, az - W + 0.05, i * 0.18 + 0.02, top + 0.04, az + W - 0.05, Armour2, "plated steel", rig: "oleo");
            D(p, -W + 0.05, top, az + i * 0.18 - 0.02, W - 0.05, top + 0.036, az + i * 0.18 + 0.02, Armour2, "plated steel", rig: "oleo");
        }
        D(p, -W - 0.006, bot + 0.04, az - W - 0.006, W + 0.006, bot + 0.07, az + W + 0.006, Hazard, "hazard paint", rig: "oleo");
        D(p, -W + 0.02, bot, az - W + 0.02, W - 0.02, bot + 0.04, az + W - 0.02, Rubber, "rubber", rig: "oleo");
        
        // Collision: the pad (it meets the ground past the spring's travel) and the clevis's plate.
        double pw = s.PadW, pl = s.PadW;
        Hidden(p, -pw, bot, az - pl, pw, top, az + pl);
        Hidden(p, -w - 0.05, plate - 0.025, az - 0.11, w + 0.05, plate, az + 0.11);
        p.Sock("hang", V(0, (top + bot) / 2, az + s.Dir * pl), s.Dir > 0 ? "+z" : "-z");
        p.Set(("door", Kw(("kind", "swing"), ("axis", "+x"), ("held", true), ("seconds", 3.0), ("hang", "hang"), ("gear", "leg"),
            ("link", Kw(("crank", L3(0, s.PivotY, s.PivotZ)), ("crankAxis", "+x"), ("crankAngle", s.Angle), ("knee", L3(0, ay, az)), ("ride", true))),
            ("spring", Kw(("travel", s.Travel), ("hz", s.Hz), ("damping", 0.6))))));
        return p.Stats(s.FootMass, 2000, 30, 4.0);
    }

    public void Make(PartOutput o)
    {
        o.Own(Out);
        foreach (var s in Sets)
        {
            foreach (var part in new[] { Mount(s), Strut(s), Foot(s) })
            {
                var d = part.Json();
                o.File($"{Out}/{((string)d["name"]!).Replace(" ", "")}.json", PyJson.Dumps(d, 2) + "\n");
            }
        }
    }
}
