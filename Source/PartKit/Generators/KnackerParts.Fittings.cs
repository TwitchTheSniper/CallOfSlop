using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>The Knacker's fittings, drawn from the specs it writes (jets, the power plant, the flight computer, the ladder, the hatch).</summary>
public sealed partial class KnackerParts
{
    /// <summary>Which way a jet pushes: Lift (exhaust down, its "top" socket up on a mount under the keel), Aft (exhaust +z, its
    /// "mount" socket -z on a transom) or Fore (exhaust -z, a retro, "mount" +z).</summary>
    public enum JetAxis { Lift, Aft, Fore }

    /// <summary>A jet: its name and what it says, its force (N; over ShipWorks.json's jetMost a jet needs works the kit doesn't fit), its
    /// fuel (litres a second a MN), its spool; the nozzle as stations (along from the mount, radius) drawn as an n-sided bell; the
    /// mount either a round collar (radius at the mount, radius at its end, its length) or a square louvred shroud (half width, length,
    /// slats a side); where the hazard band goes round it; the glow in its throat.</summary>
    private sealed record JetSpec(string Name, string Desc, JetAxis Axis, double Force, double Burn, double Spool,
        (double Along, double R)[] Nozzle, int Sides = 8,
        double CollarR = 0, double CollarR2 = 0, double CollarL = 0,
        double ShroudW = 0, double ShroudL = 0, int Louvres = 0,
        double BandAt = -1, string? Body = null, string? Throat = "#ffb060", double Mass = 1000, double Health = 4000);

    /// <summary>Draws a jet from its spec.</summary>
    private Part Jet(JetSpec j)
    {
        var p = new Part(j.Name, Small, Section, j.Desc, role: "thruster", tool: "wrench", material: "steel", color: Frame, group: Group);
        var body = j.Body ?? Armour;
        // Local frame: (u, v) across the axis, a along it from the mount (a > 0 toward the exhaust).
        double[] M(double u, double v, double a) => j.Axis switch
        {
            JetAxis.Lift => [Py.Round(u, 4), Py.Round(-a, 4), Py.Round(v, 4)],
            JetAxis.Aft => [Py.Round(u, 4), Py.Round(v, 4), Py.Round(a, 4)],
            _ => [Py.Round(u, 4), Py.Round(v, 4), Py.Round(-a, 4)],
        };
        double[] R(double r, double a, int i, int n)
        {
            var an = Math.PI / n + i * 2 * Math.PI / n;
            return M(r * Math.Cos(an), r * Math.Sin(an), a);
        }
        double[][] Ring(double r, double a, int n) => [.. Enumerable.Range(0, n).Select(i => R(r, a, i, n))];
        double[][] Square(double w, double a) => [M(-w, -w, a), M(w, -w, a), M(w, w, a), M(-w, w, a)];
        double mountEnd;
        if (j.ShroudW > 0)
        {
            // A square shroud like an office case's, louvre slats across its sides, a dark lip where the bell leaves it.
            mountEnd = j.ShroudL;
            Prism(p, Square(j.ShroudW, 0.003), Square(j.ShroudW, j.ShroudL), body, "painted plating");
            Prism(p, Square(j.ShroudW + 0.015, j.ShroudL - 0.05), Square(j.ShroudW + 0.015, j.ShroudL + 0.004), Frame, "dirty steel"); // (its end proud of the shroud's)
            for (var k = 0; k < j.Louvres; k++)
            {
                var a0 = 0.08 + k * (j.ShroudL - 0.2) / Math.Max(1, j.Louvres);
                var a1 = a0 + (j.ShroudL - 0.2) / Math.Max(1, j.Louvres) * 0.45;
                foreach (var (su, sv) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    double w = j.ShroudW, d = 0.012, e = w * 0.8;
                    double[][] slat = su != 0
                        ? [M(su * w, -e, a0), M(su * (w + d), -e, a0), M(su * (w + d), e, a0), M(su * w, e, a0)]
                        : [M(-e, sv * w, a0), M(-e, sv * (w + d), a0), M(e, sv * (w + d), a0), M(e, sv * w, a0)];
                    double[][] slat2 = [.. slat.Select(q => q.ToArray())];
                    for (var i = 0; i < 4; i++)
                    {
                        var m = M(0, 0, a1 - a0);
                        for (var c = 0; c < 3; c++)
                            slat2[i][c] = Py.Round(slat2[i][c] + m[c], 4);
                    }
                    Prism(p, slat, slat2, Dark, "painted steel");
                }
            }
            p.Add(Box(Mn(M(-j.ShroudW, -j.ShroudW, 0), M(j.ShroudW, j.ShroudW, j.ShroudL)), Mx(M(-j.ShroudW, -j.ShroudW, 0), M(j.ShroudW, j.ShroudW, j.ShroudL)), hidden: true));
        }
        else
        {
            mountEnd = j.CollarL;
            Prism(p, Ring(j.CollarR, 0.003, j.Sides), Ring(j.CollarR2, j.CollarL, j.Sides), body, "worn paint");
            var c = j.CollarR2 * 0.82;
            p.Add(Box(Mn(M(-c, -c, 0), M(c, c, j.CollarL)), Mx(M(-c, -c, 0), M(c, c, j.CollarL)), hidden: true));
        }
        if (j.BandAt >= 0)
        {
            var br = j.ShroudW > 0 ? j.ShroudW * 1.02 : Math.Max(j.CollarR, j.CollarR2) * 1.02;
            if (j.ShroudW > 0)
                Prism(p, Square(br, j.BandAt - 0.04), Square(br, j.BandAt + 0.04), Hazard, "hazard paint");
            else
                Prism(p, Ring(br, j.BandAt - 0.04, j.Sides), Ring(br, j.BandAt + 0.04, j.Sides), Hazard, "hazard paint");
        }
        // The bell: quads between its stations, the first in bare steel, the rest sooted.
        var st = j.Nozzle;
        for (var s = 0; s < st.Length - 1; s++)
            for (var i = 0; i < j.Sides; i++)
                Quad(p, R(st[s].R, st[s].Along, i, j.Sides), R(st[s].R, st[s].Along, i + 1, j.Sides), R(st[s + 1].R, st[s + 1].Along, i + 1, j.Sides), R(st[s + 1].R, st[s + 1].Along, i, j.Sides), s == 0 ? "#3a3d40" : "#2a2c2e", s == 0 ? "dirty steel" : "soot");
        if (j.Throat != null)
            Plate(p, Ring(st[0].R * 0.8, st[0].Along + 0.01, j.Sides), j.Throat, glow: true);
        var end = st[^1];
        var nr = st.Max(q => q.R) * 0.88;
        p.Add(Box(Mn(M(-nr, -nr, mountEnd), M(nr, nr, end.Along)), Mx(M(-nr, -nr, mountEnd), M(nr, nr, end.Along)), hidden: true));
        var exhaust = j.Axis switch { JetAxis.Lift => "-y", JetAxis.Aft => "+z", _ => "-z" };
        var at = M(0, 0, end.Along);
        if (j.Axis == JetAxis.Lift)
            p.Sock("top", [0, 0, 0], "+y");
        else
            p.Sock("mount", [0, 0, 0], j.Axis == JetAxis.Aft ? "-z" : "+z");
        var portR = j.ShroudW > 0 ? j.ShroudW : j.CollarR2 * 0.82;
        p.Set(("thruster", Kw(("force", j.Force), ("spool", j.Spool), ("exhaust", exhaust), ("at", Py.L(at[0], at[1], at[2])), ("burnRate", Py.Round(j.Force / 1e6 * j.Burn, 4)))),
            ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [.. M(portR, 0, mountEnd * 0.45).Cast<object>()], "+x", "fuel"))));
        p.Set(("flame", Py.L(Kw(("at", Py.L(at[0], at[1], at[2])), ("dir", exhaust), ("radius", Py.Round(end.R * 0.9, 4)), ("length", Py.Round(end.R * 7, 4)), ("kind", "Rocket")))));
        return p.Stats(j.Mass, j.Health, 25, 6.0);
    }

    private static double[] Mn(double[] a, double[] b) => [Math.Min(a[0], b[0]), Math.Min(a[1], b[1]), Math.Min(a[2], b[2])];

    private static double[] Mx(double[] a, double[] b) => [Math.Max(a[0], b[0]), Math.Max(a[1], b[1]), Math.Max(a[2], b[2])];

    /// <summary>A power plant: its rate (kW), its plinth (half sizes across and along, height), its cells (middles on the plinth,
    /// radius, height, sides, the height of each's glowing band), the cage's posts (inset from the plinth's corners) and lid, the
    /// risers on the lid. No fuel. Stands on its "bottom" socket.</summary>
    private sealed record PlantSpec(string Name, string Desc, double Rate, double HalfX, double HalfZ, double PlinthH,
        (double X, double Z)[] Cells, double CellR, double CellH, int CellSides, double BandY,
        double CageInset, double CageH, (double X, string Colour)[] Risers, string? CellCol = null, string BandCol = "#80d0ff",
        double Mass = 2400, double Health = 3500);

    private Part PowerPlant(PlantSpec s)
    {
        var p = new Part(s.Name, Small, Section, s.Desc, role: "generator", tool: "welder", material: "steel", color: Armour, group: Group);
        void B(double x0, double y0, double z0, double x1, double y1, double z1, string col, string fin, bool detail = false) =>
            p.Add(Box([x0, y0, z0], [x1, y1, z1], col, detail: detail, shape: Kw(("finish", fin))));
        double[][] O(double x, double y, double z, double r) => [.. Enumerable.Range(0, s.CellSides).Select(i =>
        {
            var a = Math.PI / s.CellSides + i * 2 * Math.PI / s.CellSides;
            return new[] { Py.Round(x + r * Math.Cos(a), 4), y, Py.Round(z + r * Math.Sin(a), 4) };
        })];
        B(-s.HalfX, 0, -s.HalfZ, s.HalfX, s.PlinthH, s.HalfZ, Frame, "dirty steel");
        foreach (var (x, z) in s.Cells)
        {
            Prism(p, O(x, s.PlinthH, z, s.CellR), O(x, s.PlinthH + s.CellH, z, s.CellR), s.CellCol ?? Skin, "worn paint");
            Prism(p, O(x, s.BandY, z, s.CellR + 0.02), O(x, s.BandY + 0.08, z, s.CellR + 0.02), s.BandCol, "glass", glow: true);
        }
        double cx = s.HalfX - s.CageInset, cz = s.HalfZ - s.CageInset, top = s.PlinthH + s.CageH;
        foreach (var (x, z) in new[] { (-cx, -cz), (cx, -cz), (-cx, cz), (cx, cz) })
            B(x - 0.04, s.PlinthH, z - 0.04, x + 0.04, top, z + 0.04, Armour, "painted steel", true);
        B(-cx - 0.045, top - 0.04, -cz - 0.045, cx + 0.045, top + 0.04, cz + 0.045, Armour, "painted steel", true);
        foreach (var (x, col) in s.Risers)
            B(x - 0.05, top + 0.04, -0.05, x + 0.05, top + 0.45, 0.05, col, "painted steel", true);
        p.Add(Box([-cx + 0.05, s.PlinthH, -cz + 0.05], [cx - 0.05, s.PlinthH + s.CellH, cz - 0.05], hidden: true));
        B(-s.HalfX + 0.01, s.PlinthH * 0.2, -s.HalfZ - 0.012, -s.HalfX + 0.35, s.PlinthH * 0.9, -s.HalfZ + 0.01, Hazard, "hazard paint", true);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("generator", Kw(("rate", s.Rate), ("spool", 6), ("fuel", "none"))), ("ports", Py.L(Kw(("name", "Power out"), ("type", "power"), ("dir", "out"), ("at", Py.L(0, Py.Round(s.PlinthH / 2, 4), s.HalfZ)), ("face", "+z")))));
        return p.Stats(s.Mass, s.Health, 25, 10.0);
    }

    /// <summary>A flight computer's console: its gyro (N m: small craft overshoot on a big one), its body (half width, depth, the
    /// height of its back and of its front, so its top rakes toward the couches), the screens on the face toward the couches (x0, x1,
    /// y0, y1, glow colour), a row of keys under them, the arrow on its top (forward, -z). Fit it level, its arrow forward.</summary>
    private sealed record ConsoleSpec(string Name, string Desc, double Gyro, double HalfW, double Depth, double BackH, double FrontH,
        (double X0, double X1, double Y0, double Y1, string Colour)[] Screens, int Keys, double KeysY);

    private Part FlightComputer(ConsoleSpec c)
    {
        var console = Style.Console;
        var p = new Part(c.Name, Small, Section, c.Desc, role: "logic", tool: "wrench", material: "steel", color: console, group: Group);
        double z0 = -c.Depth / 2, z1 = c.Depth / 2; // (z1 faces the couches, aft)
        // The body: a solid block to the front's height, a raked cap from there up to the back's.
        p.Add(Box([-c.HalfW, 0, z0], [c.HalfW, c.FrontH, z1], console, shape: Kw(("finish", "plastic"))));
        if (c.BackH > c.FrontH + 0.01)
            Prism(p, [[-c.HalfW, c.FrontH, z0], [c.HalfW, c.FrontH, z0], [c.HalfW, c.FrontH, z1], [-c.HalfW, c.FrontH, z1]],
                [[-c.HalfW, c.BackH, z0], [c.HalfW, c.BackH, z0], [c.HalfW, c.FrontH + 0.004, z1 - 0.004], [-c.HalfW, c.FrontH + 0.004, z1 - 0.004]], console, "plastic");
        foreach (var (x0, x1, y0, y1, col) in c.Screens)
            p.Add(Box([x0, y0, z1], [x1, y1, z1 + 0.018], col, detail: true, shape: Kw(("glow", true))));
        for (var i = 0; i < c.Keys; i++)
        {
            var w = (2 * c.HalfW - 0.12) / Math.Max(1, c.Keys);
            p.Add(Box([-c.HalfW + 0.06 + i * w + 0.01, c.KeysY, z1], [-c.HalfW + 0.06 + (i + 1) * w - 0.01, c.KeysY + 0.06, z1 + 0.018], i % 2 == 0 ? Style.Screen2 : Hazard, detail: true, shape: Kw(("glow", true))));
        }
        var ty = Math.Max(c.BackH, c.FrontH);
        var az = c.BackH > c.FrontH + 0.01 ? z0 + 0.04 : z0 + 0.08; // (on the flat at the back of a raked top)
        p.Add(Box([-0.05, ty + 0.001, az], [0.05, ty + 0.017, az + 0.24], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Add(Box([-0.12, ty + 0.001, az], [0.12, ty + 0.017, az + 0.07], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("logic", Kw(("kind", "flightstabilizer"), ("gyro", c.Gyro))), ("config", FlightParts.Config), ("faces", "forward"), ("faceAxis", "-z"));
        return p.Stats(300, 600, 8, 3.0);
    }

    /// <summary>A ladder: rails up from its foot (its rise, and over metres past the landing), rungs every rung metres, stand-offs every
    /// standoff metres, a hazard cap; its back (-z) to the wall it stands on. Its socket by the caller's name.</summary>
    private sealed record LadderSpec(string Name, string Desc, double Rise, double Over, string SockName, double[] SockAt, string SockDir,
        double Rung = 0.3, double Standoff = 1.2, double HalfW = 0.25, string? RailCol = null, bool Hoop = false);

    private Part Ladder(LadderSpec l)
    {
        var p = new Part(l.Name, Small, Section, l.Desc, role: "frame", tool: "welder", material: "steel", color: Frame, group: Group);
        var top = l.Rise + l.Over;
        var w = l.HalfW;
        p.Add(Box([-w - 0.05, 0, -0.19], [w + 0.05, top, -0.005], hidden: true));
        foreach (var x in new[] { -w, w })
        {
            p.Add(Box([x - 0.03, 0.0, -0.18], [x + 0.03, top, -0.12], l.RailCol ?? "#9aa0a4", detail: true, shape: Kw(("finish", "painted steel"))));
            for (var y = l.Standoff / 2; y < top - 0.2; y += l.Standoff)
                p.Add(Box([x - 0.02, y, -0.12], [x + 0.02, y + 0.06, -0.01], Frame, detail: true, shape: Kw(("finish", "dirty steel"))));
        }
        for (var y = l.Rung; y < l.Rise + 0.1; y += l.Rung)
            p.Add(Box([-w + 0.03, y - 0.02, -0.165], [w - 0.03, y + 0.02, -0.135], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Add(Box([-w - 0.04, top - 0.06, -0.19], [w + 0.04, top, -0.11], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock(l.SockName, l.SockAt, l.SockDir);
        p.Set(("ladder", l.Rise));
        return p.Stats(60 + l.Rise * 12, 400, 6, 3.0);
    }

    /// <summary>A belly hatch (the Dray's kind): a plate that shuts a boarding shaft flush under the keel, hung on a strut up the shaft's
    /// aft wall to the deck's edge. It slides down travel metres to the ground, and the boarding ladder standing on it rides it.
    /// Ship metres: the shaft (its rect), the plate's bottom and top, the deck's top, the hang point's x, the ladder's foot z; stripes
    /// under it; the paint of its plate.</summary>
    private sealed record HatchSpec(string Name, string Desc, Rect Shaft, double Bottom, double Top, double DeckTop, double HangX, double LadderZ,
        double Travel, int Stripes = 4, string? Plate = null, double Seconds = 4.0);

    private Part BellyHatch(HatchSpec h)
    {
        var plate = h.Plate ?? Armour;
        var p = new Part(h.Name, Small, Section, h.Desc, role: "door", tool: "welder", material: "steel", color: plate, group: Group);
        double[] S = [h.HangX, h.DeckTop - 0.125, h.Shaft.Z1];
        double[] L(double x, double y, double z) => [Py.Round(x - S[0], 4), Py.Round(y - S[1], 4), Py.Round(z - S[2], 4)];
        void B(double x0, double y0, double z0, double x1, double y1, double z1, string? col, string? fin, bool detail = false, bool hidden = false) =>
            p.Add(hidden ? Box(L(x0, y0, z0), L(x1, y1, z1), hidden: true) : Box(L(x0, y0, z0), L(x1, y1, z1), col!, detail: detail, shape: Kw(("finish", fin!))));
        double x0 = h.Shaft.X0 - 0.02, x1 = h.Shaft.X1 + 0.02, z0 = h.Shaft.Z0 - 0.02, z1 = h.Shaft.Z1 - 0.035;
        B(x0, h.Bottom, z0, x1, h.Top, z1, plate, "plated steel");
        var pitch = (z1 - z0 - 0.3) / Math.Max(1, h.Stripes);
        for (var i = 0; i < h.Stripes; i++)
        {
            var z = z0 + 0.15 + i * pitch;
            B(x0 + 0.1, h.Bottom - 0.008, z, x1 - 0.1, h.Bottom - 0.002, z + pitch * 0.45, Hazard, "hazard paint", detail: true);
        }
        B(x0 + 0.03, h.Top + 0.002, z0 + 0.03, x0 + 0.13, h.Top + 0.06, z1 - 0.03, Frame, "dirty steel", detail: true);
        B(x1 - 0.13, h.Top + 0.002, z0 + 0.03, x1 - 0.03, h.Top + 0.06, z1 - 0.03, Frame, "dirty steel", detail: true);
        B(h.HangX - 0.3, h.Top, h.Shaft.Z1 - 0.03, h.HangX + 0.3, h.DeckTop - 0.01, h.Shaft.Z1, null, null, hidden: true);
        B(h.HangX - 0.28, h.Top + 0.004, h.Shaft.Z1 - 0.028, h.HangX + 0.28, h.DeckTop - 0.02, h.Shaft.Z1 - 0.004, Frame, "painted steel");
        p.Sock("hang", [0, 0, 0], "+z");
        p.Sock("ladder", L(h.HangX, h.Top, h.LadderZ - 0.1), "+y");
        p.Set(("door", Kw(("kind", "slide"), ("axis", "-y"), ("travel", Py.Round(h.Travel, 3)), ("seconds", h.Seconds), ("hang", "hang"))));
        return p.Stats(500, 4000, 30, 6.0);
    }
}
