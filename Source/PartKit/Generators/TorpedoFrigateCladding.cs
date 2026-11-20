using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Zero Day's interior wall cladding (Karl, 2026-11-20: "low-poly indoor cladding ... more of an interior military aesthetic").
/// Each room face of every wall run gets a working warship's lining, low-poly and drawn only (no collision, no mass): a dark kick plate,
/// bays of chamfered panels between vertical frame ribs (dark gunmetal below a bump rail with the faction's blue stripe, battleship
/// grey above, an ID plate on every other bay, a louvred vent on every third), and a service run under the ceiling: a duct channel
/// with a conduit and a colour-coded pipe on brackets at each rib, capped where they stop.
///
/// Only the room side is clad: a wall face whose far side is the hull's void (the gap between the inner wall and the skin) is left as
/// it was, so in-wall storage can use that gap. Cladding stops round anything already on a face (door frames, consoles, screens,
/// the walls that meet it) and round the room's sockets, so a fitting hung on a wall or stood against it never has a panel through it.
/// Each clad box goes with its wall's piece (SplitDeck), so a wall shot off takes its cladding.
/// </summary>
public sealed partial class TorpedoFrigateParts
{
    private const string CladUp = "#7d8589", CladLow = "#4b5359", CladRib = "#2a2e33", CladKick = "#30353b", CladDuct = "#3a4046",
        CladConduit = "#646c72", CladPlate = "#d6d9d2", CladVent = "#1c1f23";

    /// <summary>A clad box: the wall run (its index in Walls) whose piece takes it.</summary>
    private static readonly Dictionary<PyDict, int> CladWall = new(ReferenceEqualityComparer.Instance);

    private const double WallHalf = 0.125;

    /// <summary>A rectangle on a wall face: along the wall (u) and up (y).</summary>
    private readonly record struct FaceRect(double U0, double U1, double Y0, double Y1, bool Soft = false, double Out = 1, bool Hung = false)
    {
        public bool Tall => Y1 - Y0 > 1.8;
    }

    /// <summary>Clads every room face of part <paramref name="p"/>'s wall runs (drawn whole, before SplitDeck deals it out).</summary>
    private static Part Clad(Part p)
    {
        if (!Walls.TryGetValue(p, out var walls))
            return p;
        var drawn = p.Boxes.ToList(); // (what is on the faces before any cladding)
        for (var i = 0; i < walls.Count; i++)
            foreach (var side in new[] { -1, 1 })
                CladFace(p, walls, i, side, drawn);
        return p;
    }

    private static void CladFace(Part p, List<WallLine> walls, int wi, int side, List<PyDict> drawn)
    {
        var w = walls[wi];
        double a0 = Math.Min(w.A0, w.A1), a1 = Math.Max(w.A0, w.A1), y0 = w.Y0, y1 = w.Y1;
        var face = w.C + side * WallHalf;
        // The room side only: a point 0.4 m off the face must stand over this deck's floor plan, and another wall of the deck must
        // run beyond it that way (an outer wall's far side is the hull's void, the gap in-wall storage uses: never clad).
        bool InRoom(double u)
        {
            double x = w.AlongX ? u : face + side * 0.4, z = w.AlongX ? face + side * 0.4 : u;
            if (!Floors.Values.SelectMany(q => q).Any(f => Math.Abs(f.Y1 - y0) < 0.06 && f.Plan.Any(r => x > r.X0 && x < r.X1 && z > r.Z0 && z < r.Z1)))
                return false;
            return Walls.Values.SelectMany(q => q).Any(o => o.AlongX == w.AlongX && Math.Abs(o.Y0 - y0) < 0.06 && side * (o.C - w.C) > 0.3
                && u > Math.Min(o.A0, o.A1) && u < Math.Max(o.A0, o.A1));
        }

        // What is on (or against) the face already: boxes reaching out of it, the doorways, the sockets of fittings hung on it or
        // stood against it.
        var obstacles = new List<FaceRect>();
        foreach (var b in drawn)
        {
            double[] mn = Vd(b, "min"), mx = Vd(b, "max");
            double n0 = w.AlongX ? mn[2] : mn[0], n1 = w.AlongX ? mx[2] : mx[0], u0 = w.AlongX ? mn[0] : mn[2], u1 = w.AlongX ? mx[0] : mx[2];
            var outOf = side > 0 ? n1 - face : face - n0; // how far it reaches out of the face into the room
            var gapTo = side > 0 ? n0 - face : face - n1; // how far off the face it starts
            if (outOf > 0.004 && gapTo < 0.07 && u1 > a0 && u0 < a1 && mx[1] > y0 && mn[1] < y1)
                obstacles.Add(new FaceRect(u0 - 0.05, u1 + 0.05, mn[1], mx[1], Out: outOf));
        }
        foreach (var g in w.Gaps)
            obstacles.Add(new FaceRect(g.A - 0.12, g.A + g.W + 0.12, y0 + g.Sill - 0.1, y0 + g.Head + 0.12));
        var hung = new List<(double U, double Y)>(); // fittings hung on the face: panels part round them
        foreach (var s in p.Sockets)
        {
            var at = ((List<object?>)s["at"]!).Select(v => Convert.ToDouble(v) + H).ToArray();
            double n = w.AlongX ? at[2] : at[0], u = w.AlongX ? at[0] : at[2];
            if (u < a0 - 0.6 || u > a1 + 0.6 || at[1] < y0 - 0.05 || at[1] > y1 + 0.05)
                continue;
            var off = side * (n - face);
            var dir = s["dir"] as string ?? "";
            var axis = w.AlongX ? "z" : "x";
            if (Math.Abs(off) < 0.03 && dir == (side > 0 ? "+" : "-") + axis)
            {
                obstacles.Add(new FaceRect(u - 0.45, u + 0.45, at[1] - 0.5, at[1] + 0.8, Soft: true, Hung: true)); // a fitting hung on the face
                hung.Add((u, at[1]));
            }
            else if (off > -0.01 && off < 0.9 && at[1] < y0 + 0.2)
                obstacles.Add(new FaceRect(u - 0.5, u + 0.5, y0, y0 + 2.2, Soft: true)); // one stood on the floor against it
        }

        // Fitting sockets (soft) keep what stands out (ribs, rail, pipes, vents, plates) away. A row of panels parts round a fitting hung
        // on the face (a switch is barely proud of the wall), and goes thin behind one stood against it, so a fitting that is never
        // placed leaves no hole. Small things drawn on the face that stand further out than a panel let it pass behind them.
        bool Hits(FaceRect o, FaceRect r) => o.U1 > r.U0 && o.U0 < r.U1 && o.Y1 > r.Y0 && o.Y0 < r.Y1;
        bool Free(FaceRect r) => !obstacles.Any(o => Hits(o, r));
        bool PanelFree(FaceRect r) => !obstacles.Any(o => !o.Soft && !(o.Out > 0.04 && o.Y1 - o.Y0 < 1.2) && Hits(o, r));
        bool UnderFitting(FaceRect r) => obstacles.Any(o => (o.Soft && !o.Hung || !o.Soft && o.Out > 0.04) && Hits(o, r)); // (behind something stood against it)
        // The pieces of a panel row b0..b1 left between the fittings hung over it.
        List<(double, double)> Parted(double b0, double b1, double ya, double yb)
        {
            var o2 = new List<(double, double)>();
            var at = b0;
            foreach (var (hu, _) in hung.Where(h => h.Y > ya - 0.3 && h.Y < yb + 0.3 && h.U + 0.14 > b0 && h.U - 0.14 < b1).OrderBy(h => h.U))
            {
                if (hu - 0.14 - at >= 0.15)
                    o2.Add((at, hu - 0.14));
                at = Math.Max(at, hu + 0.14);
            }
            if (b1 - at >= 0.15)
                o2.Add((at, b1));
            return o2;
        }
        List<(double, double)> Spans(double u0, double u1, double ya, double yb, double least, bool tallOnly = false)
        {
            var cuts = obstacles.Where(o => (!tallOnly || o.Tall && !o.Soft) && o.Y1 > ya && o.Y0 < yb && o.U1 > u0 && o.U0 < u1).OrderBy(o => o.U0).ToList();
            var o2 = new List<(double, double)>();
            var at = u0;
            foreach (var c in cuts)
            {
                if (c.U0 - at >= least)
                    o2.Add((at, c.U0));
                at = Math.Max(at, c.U1);
            }
            if (u1 - at >= least)
                o2.Add((at, u1));
            return o2;
        }
        // A box on the face: u along the wall, y up, d out of the face (negative: sunk into the wall, so no face sits on its plane).
        void Put(double u0, double u1, double ya, double yb, double d0, double d1, string color, string finish, PyDict? shape = null)
        {
            double na = face + side * d0, nb = face + side * d1;
            var sh = shape ?? new PyDict();
            sh["finish"] = finish;
            var mn = w.AlongX ? P(u0, ya, Math.Min(na, nb)) : P(Math.Min(na, nb), ya, u0);
            var mx = w.AlongX ? P(u1, yb, Math.Max(na, nb)) : P(Math.Max(na, nb), yb, u1);
            var count = p.Boxes.Count;
            p.Add(Box(mn, mx, color, detail: true, shape: sh));
            if (p.Boxes.Count > count)
                CladWall[p.Boxes[^1]] = wi;
        }
        string along = w.AlongX ? "+x" : "+z";
        PyDict Bevel(double radius, string? front = null, bool longOnly = false)
        {
            var d = new PyDict { ["shape"] = "bevel", ["radius"] = radius };
            if (longOnly)
                d["edges"] = "long";
            if (front != null)
                d["front"] = front;
            return d;
        }
        PyDict Pipe() => new() { ["shape"] = "cylinder", ["segments"] = 6, ["front"] = along };

        double kick = y0 + 0.297, lowA = y0 + 0.337, lowB = y0 + 1.033, railA = y0 + 1.077, railB = y0 + 1.167, upA = y0 + 1.227;
        double duct = y1 - 0.42, upB = duct - 0.06, ribTop = duct + 0.005;
        var pipeColor = (wi % 3) switch { 0 => PipeBlue, 1 => PipeYellow, _ => PipeRed };

        // The service run under the ceiling: wherever the face is free at that height, over doorways too.
        foreach (var (u0, u1) in Spans(a0, a1, duct, y1, 0.3))
        {
            if (!InRoom((u0 + u1) / 2))
                continue;
            Put(u0 + 0.004, u1 - 0.004, duct, y1 - 0.006, -0.01, 0.014, CladDuct, "painted steel");
            Put(u0 + 0.05, u1 - 0.05, y1 - 0.32, y1 - 0.22, 0.022, 0.122, CladConduit, "painted steel", Pipe());
            Put(u0 + 0.05, u1 - 0.05, y1 - 0.158, y1 - 0.102, 0.036, 0.092, pipeColor, "painted steel", Pipe());
            foreach (var e in new[] { u0 + 0.05, u1 - 0.09 }) // end caps
            {
                Put(e, e + 0.04, y1 - 0.332, y1 - 0.208, 0.016, 0.128, CladRib, "painted steel", Pipe());
                Put(e, e + 0.04, y1 - 0.166, y1 - 0.094, 0.03, 0.098, CladRib, "painted steel", Pipe());
            }
        }

        // The bays: between the things on the face, at the panels' height.
        foreach (var (s0, s1) in Spans(a0, a1, y0 + 0.003, upB, 0.5, tallOnly: true))
        {
            if (!InRoom((s0 + s1) / 2))
                continue;
            var len = s1 - s0;
            var n = Math.Max(1, (int)Math.Round(len / 1.3));
            var bay = len / n;
            foreach (var (k0, k1) in Spans(s0, s1, y0 + 0.003, kick, 0.2))
                Put(k0 + 0.004, k1 - 0.004, y0 + 0.003, kick, -0.01, 0.018, CladKick, "painted steel");
            foreach (var (r0, r1) in Spans(s0, s1, railA, railB + 0.03, 0.2))
            {
                Put(r0 + 0.004, r1 - 0.004, railA, railB, -0.01, 0.04, CladRib, "painted steel", Bevel(0.35, along, true));
                Put(r0 + 0.004, r1 - 0.004, railB + 0.004, railB + 0.026, -0.01, 0.022, Blue, "painted plating");
            }
            for (var k = 0; k <= n; k++)
            {
                var c = s0 + k * bay;
                double r0 = k == 0 ? s0 : k == n ? s1 - 0.09 : c - 0.045, r1 = r0 + 0.09;
                if (Free(new FaceRect(r0, r1, y0, ribTop)))
                {
                    Put(r0, r1, y0 + 0.003, ribTop, -0.01, 0.05, CladRib, "painted steel", Bevel(0.3, "+y", true));
                    // A bracket under the service run, behind its pipes.
                    if (Free(new FaceRect(r0, r1, duct, y1)) && k > 0 && k < n)
                        Put(c - 0.025, c + 0.025, y1 - 0.36, y1 - 0.07, -0.01, 0.03, CladRib, "painted steel");
                }
            }
            for (var k = 0; k < n; k++)
            {
                double b0 = s0 + k * bay + (k == 0 ? 0.09 : 0.045) + 0.04, b1 = s0 + (k + 1) * bay - (k == n - 1 ? 0.09 : 0.045) - 0.04;
                if (b1 - b0 < 0.25)
                    continue;
                var lower = new FaceRect(b0, b1, lowA, lowB);
                if (PanelFree(lower))
                {
                    var parts = Parted(b0, b1, lowA, lowB);
                    var whole = parts.Count == 1 && parts[0] == (b0, b1);
                    if (whole && k % 3 == 1 && Free(lower))
                    {
                        Put(b0, b1, lowA, lowB, -0.01, 0.016, CladLow, "painted steel");
                        double v0 = b0 + 0.12, v1 = b1 - 0.12;
                        Put(v0, v1, lowA + 0.14, lowB - 0.14, -0.01, 0.02, CladVent, "painted steel");
                        for (var l = 0; l < 4; l++)
                        {
                            var ly = lowA + 0.19 + l * 0.095;
                            Put(v0 + 0.02, v1 - 0.02, ly, ly + 0.035, -0.01, 0.03, CladLow, "painted steel");
                        }
                    }
                    else
                        foreach (var (q0, q1) in parts)
                        {
                            var thin = UnderFitting(new FaceRect(q0, q1, lowA, lowB));
                            Put(q0, q1, lowA, lowB, -0.01, thin ? 0.007 : 0.025, CladLow, "painted plating", thin ? null : Bevel(0.4));
                        }
                }
                var upperRect = new FaceRect(b0, b1, upA, upB);
                if (PanelFree(upperRect))
                {
                    var parts = Parted(b0, b1, upA, upB);
                    foreach (var (q0, q1) in parts)
                    {
                        var thin = UnderFitting(new FaceRect(q0, q1, upA, upB));
                        Put(q0, q1, upA, upB, -0.01, thin ? 0.007 : 0.025, CladUp, "painted plating", thin ? null : Bevel(0.4));
                        if (q0 == b0 && k % 2 == 0 && q1 - q0 > 0.5 && Free(new FaceRect(q0, q0 + 0.3, upB - 0.2, upB)))
                            Put(q0 + 0.08, q0 + 0.26, upB - 0.16, upB - 0.08, 0.019, 0.031, CladPlate, "painted plating");
                    }
                }
            }
        }
    }
}
