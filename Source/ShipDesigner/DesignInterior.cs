namespace CallOfSlop.ShipDesigner;

public static partial class DesignScene
{
    /// <summary>The most triangles one room's interior cladding may add (solid and glow together); past it the rest is left bare.</summary>
    private const double HullSideOff = 0.012; // how much further off its skin's line a side out to the hull is clad (Side)

    public const int MaxInteriorTris = 20000; // (9000 ran out on a long room out to the hull, its later posts and trim left bare: Karl, 2026-11-27)

    /// <summary>
    /// A room's interior cladding (the user's corridor references, 2026-11-24): framed wall panels with seams, ribs ringing the room every
    /// <see cref="Room.RibSpacing"/> metres chamfered at the corners, a kick plate, ceiling light strips and floor plates (laid by
    /// <see cref="LayPlates"/>) with lit floor strips. Industrial adds deeper ribs, vent boxes and hazard-striped trim. Everything sits 3 mm
    /// and more proud of the inside faces; nothing is drawn inside a door's clearance, outside every hull or on a wall open to a room of
    /// its group. Returns the solid mesh and the glow mesh (the page draws that unlit, as the mesh carries no emissive).
    /// A room out to the hull follows its shell (Karl, 2026-11-27: its sloped and tapered walls were left bare): its sides are clad on the
    /// skin's own surface (the shell's slices and rows), its floor and ceiling reach out to them, its ends close where it runs on to; and a
    /// room's extensions are clad too (their walls, floor and ceiling, an up one's vault), the room's wall opened to them.
    /// </summary>
    public static (DesignMesh Solid, DesignMesh Glow) RoomInterior(ShipDesign d, Room r, List<double[]> doors)
    {
        var m = new DesignMesh();
        var g = new DesignMesh();
        if (r.Interior == "Off")
            return (m, g);
        doors = [.. doors, .. RoomWindows(d, r)]; // (its panels open behind its windows: DesignWindows.cs)
        m.World();
        g.World();
        var ind = r.Interior == "Industrial";
        var b = d.Box(r);
        int ax = 0, al = d.Stacked ? 1 : 2, up = d.Stacked ? 2 : 1;
        var lo = new[] { b[0], b[2], b[4] };
        var hi = new[] { b[1], b[3], b[5] };
        // Local frame: a across, l along, u up from the floor (stacked plans have up as -Z, as the shell's door test does).
        var sg = d.Stacked ? -1.0 : 1.0;
        var floor = d.Stacked ? hi[up] : lo[up];
        // Inside its own shell (Karl, 2026-11-25: the walls sit in the room's box): the sides by the shell's depth, the ceiling too.
        var sd = ShellDepth(r);
        var sdl = sd > 0 ? sd + 0.003 : 0;
        var hull = r.ToHull;
        var hgt = hi[up] - lo[up];
        // A room out to the hull runs as far as its shell does (on into the nose or the stern: Span).
        var (sp0, sp1) = hull ? Span(d, r) : (lo[al], hi[al]);
        double A0 = lo[ax] + (hull ? 0 : sd), A1 = hi[ax] - (hull ? 0 : sd), L0 = sp0 + sdl, L1 = sp1 - sdl, H = hgt - sd;
        var panel = DesignMesh.Hex(r.PanelColour);
        var trim = DesignMesh.Hex(r.TrimColour);
        string Hx((byte R, byte G, byte B) c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";
        string pHex = Hx(panel), pDark = Hx(DesignMesh.Tone(panel, 0.78)), pFrame = Hx(DesignMesh.Tone(panel, ind ? 0.62 : 0.9));
        var groove = ind ? "#202326" : "#3a3f45";
        var tHex = Hx(trim);
        var lit = r.Lights;
        var glowCol = ind ? "#ffd890" : "#f4fbff";
        var offCol = "#6a7078";
        var mates = d.Rooms.Where(o => o != r && r.Group != 0 && o.Group == r.Group).Select(d.Box).ToList();
        var budget = MaxInteriorTris;
        // Its extensions, where its shell walls them (RoomShell: a room with a shell, not out to the hull).
        var exts = sd > 0 && !hull ? r.Exts : [];
        var vaulted = exts.Any(e => e.Side == "up");

        // ---- the skin's edges a room out to the hull follows: its shell's own samples (HullShell: 50 cm slices along, rows at quarters
        // up), between them as the shell's faces run. Lft and Rgt are the inside faces of its side shells; a box room's are its box's.
        var gv = new List<double>();
        var gu = new List<double>();
        double[,] eL = new double[1, 1], eR = new double[1, 1];
        if (hull)
        {
            var n0 = Math.Max(1, (int)Math.Ceiling((sp1 - sp0) / 0.5));
            for (var q = 0; q <= n0; q++)
                gv.Add(sp0 + (sp1 - sp0) * q / n0);
            gu.AddRange([0, hgt / 4, hgt / 2, hgt * 3 / 4, hgt]);
            eL = new double[gv.Count, gu.Count];
            eR = new double[gv.Count, gu.Count];
            for (var i = 0; i < gv.Count; i++)
                for (var k = 0; k < gu.Count; k++)
                    (eL[i, k], eR[i, k]) = Edges(d, r, gv[i], gu[k]);
        }
        double Ed(double[,] e, double l, double u)
        {
            var fi = Math.Clamp((l - sp0) / Math.Max(1e-6, sp1 - sp0) * (gv.Count - 1), 0, gv.Count - 1 - 1e-9);
            var fk = Math.Clamp(u / Math.Max(1e-6, hgt) * (gu.Count - 1), 0, gu.Count - 1 - 1e-9);
            int i = (int)fi, k = (int)fk;
            double ti = fi - i, tk = fk - k;
            return (e[i, k] * (1 - ti) + e[i + 1, k] * ti) * (1 - tk) + (e[i, k + 1] * (1 - ti) + e[i + 1, k + 1] * ti) * tk;
        }
        double Lft(double l, double u) => hull ? Ed(eL, l, u) + sd : A0;
        double Rgt(double l, double u) => hull ? Ed(eR, l, u) - sd : A1;

        double[] W(double a, double l, double u) { var v = new double[3]; v[ax] = a; v[al] = l; v[up] = floor + sg * u; return v; }
        (double, double, double) T(double[] v) => (v[0], v[1], v[2]);
        bool InBox(double[] p, double[] o) => p[0] > o[0] && p[0] < o[1] && p[1] > o[2] && p[1] < o[3] && p[2] > o[4] && p[2] < o[5];
        bool InHull(double[] p) => d.Hulls.Any(h => h.Depth(p[0], p[1], p[2]) > 0.01);
        // Its own openings (Karl, 2026-11-27: cladding ran across holes in the wall), each as the room-local box (a across, l along, u up) it
        // keeps clear, at its own size: a door in a wall from over the floor's dressing to its head, its half along the wall, reaching in from
        // the wall (out to the hull, on in to the skin's line); a hole through the floor (a well, a hatch, a collar) only what lies on the
        // floor over it; one through the ceiling (a well from the deck over) only what hangs under the ceiling.
        var own = DoorsOf(d, r, doors);
        var open = new List<(double A0, double A1, double L0, double L1, double U0, double U1, bool Hole)>();
        foreach (var q in own)
        {
            var half = DoorHalf(q);
            var uq = sg * (q[up] - floor);
            if (IsHole(q))
            {
                var atFloor = Math.Abs(uq) < DoorLevel;
                open.Add((q[ax] - half, q[ax] + half, q[al] - half, q[al] + half, atFloor ? -0.3 : uq - 0.9, atFloor ? FloorTop(r) + 0.15 : uq + 0.3, true));
                continue;
            }
            var reach = hull ? 1.5 : 0.6;
            double u0 = FloorTop(r) + 0.04, u1 = uq + DoorTop(q);
            double dA0 = Math.Abs(q[ax] - lo[ax]), dA1 = Math.Abs(q[ax] - hi[ax]), dL0 = Math.Abs(q[al] - lo[al]), dL1 = Math.Abs(q[al] - hi[al]);
            if (Math.Min(dA0, dA1) <= Math.Min(dL0, dL1))
                open.Add((dA0 < dA1 ? q[ax] - 0.6 : q[ax] - reach, dA0 < dA1 ? q[ax] + reach : q[ax] + 0.6, q[al] - half, q[al] + half, u0, u1, false));
            else
                open.Add((q[ax] - half, q[ax] + half, dL0 < dL1 ? q[al] - 0.6 : q[al] - reach, dL0 < dL1 ? q[al] + reach : q[al] + 0.6, u0, u1, false));
        }
        bool Clear(double[] c0, double[] c1)
        {
            double a0 = Math.Min(c0[ax], c1[ax]), a1 = Math.Max(c0[ax], c1[ax]), l0 = Math.Min(c0[al], c1[al]), l1 = Math.Max(c0[al], c1[al]);
            double u0 = sg * (c0[up] - floor), u1 = sg * (c1[up] - floor);
            if (u1 < u0) (u0, u1) = (u1, u0);
            foreach (var o in open)
                if (a0 < o.A1 && a1 > o.A0 && l0 < o.L1 && l1 > o.L0 && u0 < o.U1 && u1 > o.U0)
                    return false;
            return true;
        }
        // The holes through the floor or the ceiling that reach heights u0..u1, as openings in (l along, a across) for AroundHoles.
        List<(double S0, double S1, double H0, double H1)> FlatHoles(double u0, double u1)
        {
            var o = new List<(double, double, double, double)>();
            foreach (var h in open)
                if (h.Hole && u0 < h.U1 && u1 > h.U0)
                    o.Add((h.L0, h.L1, h.A0, h.A1));
            return o;
        }
        // A box in the local frame; probe is a point just beyond the face it is on (open to a group-mate, or past the skin: left out).
        bool Box(DesignMesh into, double a0, double a1, double l0, double l1, double u0, double u1, string col, double[]? probe = null, bool cut = false)
        {
            if (budget < 12 || a1 - a0 < 1e-4 || l1 - l0 < 1e-4 || u1 - u0 < 1e-4)
                return false;
            var c0 = W(a0, l0, u0);
            var c1 = W(a1, l1, u1);
            if (!cut && !Clear(c0, c1))
                return false;
            var mid = W((a0 + a1) / 2, (l0 + l1) / 2, (u0 + u1) / 2);
            if (!InHull(mid))
                return false;
            if (probe is not null && mates.Any(o => InBox(probe, o)))
                return false;
            into.Box(Math.Min(c0[0], c1[0]), Math.Min(c0[1], c1[1]), Math.Min(c0[2], c1[2]), Math.Max(c0[0], c1[0]), Math.Max(c0[1], c1[1]), Math.Max(c0[2], c1[2]), col);
            budget -= 12;
            return true;
        }
        // Eight corners (a ring of four, then the same ring higher) as a solid: its faces shaded as a box's.
        void Hexa(DesignMesh into, double[] b0, double[] b1, double[] b2, double[] b3, double[] t0, double[] t1, double[] t2, double[] t3, string col)
        {
            var c = DesignMesh.Hex(col);
            into.Quad(T(t0), T(t1), T(t2), T(t3), c, false);
            into.Quad(T(b0), T(b1), T(b2), T(b3), DesignMesh.Tone(c, 0.7), false);
            into.Quad(T(b0), T(b1), T(t1), T(t0), DesignMesh.Tone(c, 0.8), false);
            into.Quad(T(b1), T(b2), T(t2), T(t1), DesignMesh.Tone(c, 0.9), false);
            into.Quad(T(b2), T(b3), T(t3), T(t2), DesignMesh.Tone(c, 0.85), false);
            into.Quad(T(b3), T(b0), T(t0), T(t3), DesignMesh.Tone(c, 0.95), false);
            budget -= 12;
        }
        // Where to split a run a..c: at those of the breaks br inside it where the line f(x) (one or more points) bends more than 4 mm off the straight line
        // between the splits either side (the skin is often nearly flat along a room: a split a slice there only costs triangles).
        List<double> Cuts(double a, double c, List<double>? br, Func<double, double[]>? f = null)
        {
            var o = new List<double> { a };
            if (br is not null)
            {
                var inner = br.Where(x => x > a + 0.01 && x < c - 0.01).ToList();
                if (f is null)
                    o.AddRange(inner);
                else
                {
                    void Split(double x0, double x1, double[] p0, double[] p1)
                    {
                        double best = 0.004, at = double.NaN;
                        double[]? pb = null;
                        foreach (var x in inner)
                        {
                            if (x <= x0 || x >= x1)
                                continue;
                            var p = f(x);
                            var t = (x - x0) / (x1 - x0);
                            var dev = 0.0;
                            for (var k = 0; k + 2 < p.Length; k += 3)
                            {
                                double dx = p[k] - (p0[k] + (p1[k] - p0[k]) * t), dy = p[k + 1] - (p0[k + 1] + (p1[k + 1] - p0[k + 1]) * t), dz = p[k + 2] - (p0[k + 2] + (p1[k + 2] - p0[k + 2]) * t);
                                dev = Math.Max(dev, Math.Sqrt(dx * dx + dy * dy + dz * dz));
                            }
                            if (dev > best)
                                (best, at, pb) = (dev, x, p);
                        }
                        if (pb is null)
                            return;
                        Split(x0, at, p0, pb);
                        o.Add(at);
                        Split(at, x1, pb, p1);
                    }
                    Split(a, c, f(a), f(c));
                }
            }
            o.Add(c);
            return o;
        }
        // A flat band from a0(l) to a1(l) across, l0..l1 along, u0..u1 up: a box in a box room; out to the hull a solid a slice at a time
        // (split at the shell's slices so its sides keep to the skin's line, or one piece where it stands well clear of it).
        bool Band(DesignMesh into, Func<double, double> a0, Func<double, double> a1, double l0, double l1, double u0, double u1, string col, bool split = true, bool cut = false)
        {
            if (!hull)
                return Box(into, a0(l0), a1(l0), l0, l1, u0, u1, col, null, cut);
            var any = false;
            void Run(List<double> ls, bool whole)
            {
                for (var i = 0; i + 1 < ls.Count; i++)
                {
                    double la = ls[i], lb = ls[i + 1];
                    double xa0 = a0(la), xa1 = a1(la), xb0 = a0(lb), xb1 = a1(lb);
                    if (budget < 12 || xa1 - xa0 < 1e-3 || xb1 - xb0 < 1e-3 || lb - la < 1e-4)
                        continue;
                    if (!cut && !Clear(W(Math.Min(xa0, xb0), la, u0), W(Math.Max(xa1, xb1), lb, u1)) || !InHull(W((xa0 + xa1 + xb0 + xb1) / 4, (la + lb) / 2, (u0 + u1) / 2)))
                    {
                        // A long piece a door's clearance reaches: its slices one by one, so only those by the door are left out.
                        if (!whole && split)
                            Run(Cuts(la, lb, gv), true);
                        continue;
                    }
                    Hexa(into, W(xa0, la, u0), W(xa1, la, u0), W(xb1, lb, u0), W(xb0, lb, u0), W(xa0, la, u1), W(xa1, la, u1), W(xb1, lb, u1), W(xb0, lb, u1), col);
                    any = true;
                }
            }
            // (its two sides' lines side by side, so a bend in either splits it)
            Run(split ? Cuts(l0, l1, gv, l => [.. W(a0(l), l, u0), .. W(a1(l), l, u0)]) : [l0, l1], false);
            return any;
        }
        // A flat band on the floor or under the ceiling cut round the holes through it (as a wall's panels are round its doors), not dropped.
        bool Flat(DesignMesh into, Func<double, double> a0, Func<double, double> a1, double l0, double l1, double u0, double u1, string col, bool split = true)
        {
            var any = false;
            AroundHoles(l0, l1, -1e4, 1e4, FlatHoles(u0, u1), 0, (la, lb, c0, c1) =>
                any |= Band(into, l => Math.Max(a0(l), c0), l => Math.Min(a1(l), c1), la, lb, u0, u1, col, split, true));
            return any;
        }
        // A plate on a surface F(s, u, off) (world; off along its normal into the room): s0..s1 by u0..u1, from o0 to o1 off it, its face
        // following the surface (split at sBr and uBr, the shell's own slices and rows), its rims round it; its back is on the wall.
        bool SBox(DesignMesh into, Func<double, double, double, double[]> F, double s0, double s1, double u0, double u1, double o0, double o1, string col,
            double[]? probe, List<double>? sBr = null, List<double>? uBr = null)
        {
            if (s1 - s0 < 1e-4 || u1 - u0 < 1e-4 || o1 - o0 < 1e-5)
                return false;
            var ss = Cuts(s0, s1, sBr, s => [.. F(s, u0, o1), .. F(s, u1, o1)]);
            var us = Cuts(u0, u1, uBr, u => [.. F(s0, u, o1), .. F(s1, u, o1)]);
            int ns = ss.Count - 1, nu = us.Count - 1, cost = 2 * ns * nu + 4 * (ns + nu);
            if (budget < cost)
                return false;
            if (!InHull(F((s0 + s1) / 2, (u0 + u1) / 2, (o0 + o1) / 2)))
                return false;
            if (probe is not null && mates.Any(o => InBox(probe, o)))
                return false;
            var c = DesignMesh.Hex(col);
            var rim = DesignMesh.Tone(c, 0.8);
            (double, double, double) Q(double s, double u, double o) => T(F(s, u, o));
            for (var i = 0; i < ns; i++)
                for (var k = 0; k < nu; k++)
                    into.Quad(Q(ss[i], us[k], o1), Q(ss[i + 1], us[k], o1), Q(ss[i + 1], us[k + 1], o1), Q(ss[i], us[k + 1], o1), c, false);
            for (var k = 0; k < nu; k++)
            {
                into.Quad(Q(s0, us[k], o0), Q(s0, us[k + 1], o0), Q(s0, us[k + 1], o1), Q(s0, us[k], o1), rim, false);
                into.Quad(Q(s1, us[k], o0), Q(s1, us[k + 1], o0), Q(s1, us[k + 1], o1), Q(s1, us[k], o1), rim, false);
            }
            for (var i = 0; i < ns; i++)
            {
                into.Quad(Q(ss[i], u0, o0), Q(ss[i + 1], u0, o0), Q(ss[i + 1], u0, o1), Q(ss[i], u0, o1), DesignMesh.Tone(c, 0.7), false);
                into.Quad(Q(ss[i], u1, o0), Q(ss[i + 1], u1, o0), Q(ss[i + 1], u1, o1), Q(ss[i], u1, o1), DesignMesh.Tone(c, 0.9), false);
            }
            budget -= cost;
            return true;
        }
        // A side wall of a room out to the hull (k 0: low a, 1: high a): s along the room, u up, off along its normal into the room.
        double[] Side(int k, double s, double u, double off)
        {
            double E(double l, double uu) => k == 0 ? Lft(l, uu) : Rgt(l, uu);
            const double e = 0.02;
            double dl = (E(s + e, u) - E(s - e, u)) / (2 * e), du = (E(s, u + e) - E(s, u - e)) / (2 * e);
            var sk = k == 0 ? 1.0 : -1.0;
            double na = sk, nl = -sk * dl, nu = -sk * du;
            var len = Math.Sqrt(na * na + nl * nl + nu * nu);
            // (a centimetre more off the skin's line than the shell's face, as what lies against a side does: the shell's flat quads cut
            // the curved line between its samples, and showed through the panels in jagged patches; Karl, 2026-11-27)
            off += HullSideOff;
            return W(E(s, u) + na / len * off, s + nl / len * off, u + nu / len * off);
        }
        void Strip(Func<double, double> a0, Func<double, double> a1, double l0, double l1, double u0, double u1)
        {
            if (lit) Flat(g, a0, a1, l0, l1, u0, u1, glowCol);
            else Flat(m, a0, a1, l0, l1, u0, u1, offCol);
        }

        const double P = 0.003; // proud of the inside faces
        var FL = FloorTop(r) + 0.009; // the floor layer sits on the page's 8 cm floor marker (or the shell's floor), 9 mm proud of its top
        var spacing = Math.Clamp(r.RibSpacing, Room.RibSpacingMin, Room.RibSpacingMax);
        var ribs = new List<double>();
        {
            var n = Math.Clamp((int)Math.Round((L1 - L0) / spacing), 1, 40);
            var s = (L1 - L0) / n;
            for (var k = 1; k < n; k++)
                ribs.Add(L0 + s * k);
        }
        double ribW = ind ? 0.16 : 0.1, ribD = ind ? 0.14 : 0.06, ch = ind ? 0.3 : 0.22;
        double skirt = ind ? 0.18 : 0.12;
        // Out to the hull, what lies against a side keeps a centimetre more off it (the skin curves between the shell's samples).
        var hk = hull ? 0.01 : 0;

        // ---- floor: plates (the hull's plate layer, on a flat grid) over a dark groove base, lit strips along the walls.
        {
            double FA0(double l) => Lft(l, FL) + P + hk;
            double FA1(double l) => Rgt(l, FL) - P - hk;
            double fl0 = L0 + P, fl1 = L1 - P;
            var arc = FA1(fl0) - FA0(fl0);
            if (hull)
                foreach (var l in gv)
                    arc = Math.Max(arc, FA1(Math.Clamp(l, fl0, fl1)) - FA0(Math.Clamp(l, fl0, fl1)));
            var len = fl1 - fl0;
            if (r.Floor is "Plates" or "Hex") // the other floors are the shell's dressing
                Flat(m, FA0, FA1, fl0, fl1, FL, FL + 0.004, groove);
            int nu = Math.Clamp((int)Math.Round(arc / 0.6), 1, 40), nv = Math.Clamp((int)Math.Round(len / 0.6), 1, 60);
            // A point of the floor: u across (0..1 of its width there), v along.
            double FA(double u, double v)
            {
                var l = fl0 + len * v;
                var a0 = FA0(l);
                return a0 + (FA1(l) - a0) * u;
            }
            var taken = new bool[nu, nv];
            for (var x = 0; x < nu; x++)
                for (var y = 0; y < nv; y++)
                {
                    double cu = (x + 0.5) / nu, cv = (y + 0.5) / nv;
                    var c = W(FA(cu, cv), fl0 + len * cv, 0.01);
                    taken[x, y] = !Clear(W(FA((double)x / nu, (double)y / nv), fl0 + len * y / nv, 0), W(FA((double)(x + 1) / nu, (double)(y + 1) / nv), fl0 + len * (y + 1) / nv, 0.02))
                        || !InHull(c) || hull && FA1(fl0 + len * cv) - FA0(fl0 + len * cv) < 0.3;
                }
            if (nu * nv * 30 < budget && r.Floor is ("Plates" or "Hex") && arc > 0.1)
            {
                var fm = new DesignMesh();
                var n1 = W(0, 0, 1);
                var n0 = W(0, 0, 0);
                (double, double, double) nrm = (n1[0] - n0[0], n1[1] - n0[1], n1[2] - n0[2]);
                LayPlates(fm, nu, nv, arc, len, new Random(unchecked(r.Id * 7919 + 17)), ind, 0.12, DesignMesh.Tone(panel, ind ? 0.55 : 0.82),
                    (u, v) => T(W(FA(u, v), fl0 + len * v, FL + 0.004)), (u, v) => nrm,
                    // Its skirts reach down to the groove base (the plate layer's sink is 3 cm; offset so they start in the base, not the floor).
                    (u, v, h) => T(W(FA(u, v), fl0 + len * v, FL + 0.004 + Math.Max(h, -0.003))), taken, null, 0, r.Floor == "Hex");
                m.P.AddRange(fm.P);
                m.C.AddRange(fm.C);
                budget -= fm.Tris;
            }
            // Lit strips along the floor's long edges (Industrial: hazard edges instead, with one strip down the middle).
            if (!ind)
            {
                Strip(l => Lft(l, FL) + hk + 0.2, l => Lft(l, FL) + hk + 0.26, L0 + 0.1, L1 - 0.1, FL + 0.006, FL + 0.026);
                Strip(l => Rgt(l, FL) - hk - 0.26, l => Rgt(l, FL) - hk - 0.2, L0 + 0.1, L1 - 0.1, FL + 0.006, FL + 0.026);
            }
            else
                Strip(l => (Lft(l, FL) + Rgt(l, FL)) / 2 - 0.04, l => (Lft(l, FL) + Rgt(l, FL)) / 2 + 0.04, L0 + 0.3, L1 - 0.3, FL + 0.006, FL + 0.024);
        }

        // ---- ceiling: panels with seams and light strips down its length (none under a vault: that is clad as its own).
        double CL(double l) => Lft(l, H) + hk;
        double CR(double l) => Rgt(l, H) - hk;
        if (!vaulted)
        {
            int nl = Math.Clamp((int)Math.Round((L1 - L0) / 1.0), 1, 60);
            for (var y = 0; y < nl; y++)
            {
                double l0 = L0 + (L1 - L0) * y / nl + 0.015, l1 = L0 + (L1 - L0) * (y + 1) / nl - 0.015;
                var lm = (l0 + l1) / 2;
                var na = Math.Clamp((int)Math.Round((CR(lm) - CL(lm)) / 1.2), 1, 12);
                for (var x = 0; x < na; x++)
                {
                    var (fx0, fx1) = ((double)x / na, (double)(x + 1) / na);
                    Flat(m, l => CL(l) + (CR(l) - CL(l)) * fx0 + 0.015, l => CL(l) + (CR(l) - CL(l)) * fx1 - 0.015, l0, l1, H - P - 0.012, H - P, (x + y) % 2 == 0 ? pHex : pFrame, false);
                }
            }
            var wMid = CR((L0 + L1) / 2) - CL((L0 + L1) / 2);
            var strips = ind || wMid < 2.4 ? new[] { 0.5 } : new[] { 0.3, 0.7 };
            foreach (var f in strips)
            {
                double C(double l) => CL(l) + (CR(l) - CL(l)) * f;
                Flat(m, l => C(l) - 0.1, l => C(l) + 0.1, L0 + 0.15, L1 - 0.15, H - P - 0.03, H - P - 0.008, pDark);
                Strip(l => C(l) - 0.06, l => C(l) + 0.06, L0 + 0.2, L1 - 0.2, H - P - 0.04, H - P - 0.026);
            }
        }

        // The openings its extensions make in a wall (side x0, x1, z0, z1), floor to ceiling.
        List<(double S0, double S1, double H0, double H1)> ExtHoles(string side)
        {
            var o = new List<(double, double, double, double)>();
            foreach (var e in exts)
                if (e.Side == side)
                {
                    var (s0, s1, _, _) = ExtSpan(r, e);
                    o.Add((s0, s1, -1, H + 1));
                }
            return o;
        }

        // ---- walls: framed panels between the ribs, a kick plate, Industrial vents. surf: the wall's face (s, u, off) where it is not a
        // plane of the box (a side out to the hull); clip: how far across it reaches between two heights (an end out to the hull).
        void Wall(bool across, double at, double inward, double s0, double s1, List<(double S0, double S1, double H0, double H1)> extra,
            Func<double, double, double, double[]>? surf = null, Func<double, double, (double, double)>? clip = null, double? plane = null)
        {
            // across: the wall's plane is at a = at (a side wall, running along l); else at l = at (an end wall, running across a).
            double[] Pt(double s, double off, double u) => surf is not null ? surf(s, u, off) : across ? W(at + inward * off, s, u) : W(s, at + inward * off, u);
            // The doors through this wall, as openings (Karl, 2026-11-25): the panels are cut round them, not dropped.
            var wt = sd > 0 ? sd - ShellGap : 0;
            // (a side out to the hull is clad on the skin's line, but its doors are cut where its box's wall is: plane)
            var holes = DoorHoles(d, own, across ? ax : al, plane ?? at - inward * wt / 2, across ? al : ax, floor, sg);
            holes.AddRange(extra);
            bool WBox1(DesignMesh into, double sa, double sb, double o0, double o1, double u0, double u1, string col)
            {
                var probe = Pt((sa + sb) / 2, -0.3, (u0 + u1) / 2);
                if (surf is not null)
                    return SBox(into, surf, sa, sb, u0, u1, o0, o1, col, probe, gv, gu);
                double x0 = at + inward * o0, x1 = at + inward * o1;
                if (x1 < x0) (x0, x1) = (x1, x0);
                return across ? Box(into, x0, x1, sa, sb, u0, u1, col, probe, true) : Box(into, sa, sb, x0, x1, u0, u1, col, probe, true);
            }
            bool WBox(DesignMesh into, double sa, double sb, double o0, double o1, double u0, double u1, string col)
            {
                var any = false;
                AroundHoles(sa, sb, u0, u1, holes, 0, (a, b2, c, e) => any |= WBox1(into, a, b2, o0, o1, c, e, col));
                return any;
            }
            (double, double) Clip(double sa, double sb, double u0, double u1)
            {
                if (clip is null)
                    return (sa, sb);
                var (c0, c1) = clip(u0, u1);
                return (Math.Max(sa, c0), Math.Min(sb, c1));
            }
            // Its doors' frames: a lining through the shell and an architrave over the panels' cut ends.
            DoorFrames(holes, s0, s1, FL - 0.009, H, -wt + 0.004, P + 0.04, (a, b2, c, e, o0, o1, col) => WBox1(m, a, b2, o0, o1, c, e, col), pFrame, tHex);
            // Columns: between the ribs on a side wall, about a metre on an end wall.
            var cuts = new List<double> { s0 };
            if (across)
                cuts.AddRange(ribs);
            cuts.Add(s1);
            var cols = new List<(double, double)>();
            for (var k = 0; k + 1 < cuts.Count; k++)
            {
                var n = Math.Clamp((int)Math.Round((cuts[k + 1] - cuts[k]) / 1.1), 1, 8);
                for (var j = 0; j < n; j++)
                    cols.Add((cuts[k] + (cuts[k + 1] - cuts[k]) * j / n, cuts[k] + (cuts[k + 1] - cuts[k]) * (j + 1) / n));
            }
            var gapR = across ? ribW / 2 + 0.01 : 0.0;
            var rows = new[] { (skirt + 0.02, Math.Min(1.25, H * 0.5)), (Math.Min(1.25, H * 0.5) + 0.03, H - 0.06) };
            var vent = 0;
            foreach (var (c0, c1) in cols)
            {
                double sa0 = c0 + 0.02 + (ribs.Any(q => Math.Abs(q - c0) < 1e-6) ? gapR : 0), sb0 = c1 - 0.02 - (ribs.Any(q => Math.Abs(q - c1) < 1e-6) ? gapR : 0);
                if (sb0 - sa0 < 0.15)
                    continue;
                foreach (var (u0, u1) in rows)
                {
                    if (u1 - u0 < 0.15)
                        continue;
                    var (sa, sb) = Clip(sa0, sb0, u0, u1);
                    if (sb - sa < 0.15)
                        continue;
                    // A frame round a recessed face: Clean's frame is light and rounded off by its chamfered corners, Industrial's dark.
                    const double f = 0.045;
                    WBox(m, sa, sb, P, P + 0.008, u0, u1, pHex);
                    WBox(m, sa, sa + f, P + 0.002, P + 0.02, u0 + 0.03, u1 - 0.03, pFrame);
                    WBox(m, sb - f, sb, P + 0.002, P + 0.02, u0 + 0.03, u1 - 0.03, pFrame);
                    WBox(m, sa + 0.03, sb - 0.03, P + 0.002, P + 0.02, u0, u0 + f, pFrame);
                    WBox(m, sa + 0.03, sb - 0.03, P + 0.002, P + 0.02, u1 - f, u1, pFrame);
                }
                if (ind && (vent++ % 2 == 0) && H > 2.2 && sb0 - sa0 > 0.7)
                {
                    // A vent box: a housing high on the wall with grille slats across it.
                    double vm = (sa0 + sb0) / 2, vu = Math.Min(H - 0.45, 1.85);
                    var (va, vb) = Clip(vm - 0.28, vm + 0.28, vu, vu + 0.3);
                    if (vb - va > 0.5 && !holes.Any(o => vm + 0.28 > o.S0 - 0.1 && vm - 0.28 < o.S1 + 0.1 && vu + 0.3 > o.H0 - 0.1 && vu < o.H1 + 0.1) && WBox(m, vm - 0.28, vm + 0.28, P + 0.008, P + 0.08, vu, vu + 0.3, pDark))
                        for (var k = 0; k < 4; k++)
                            WBox(m, vm - 0.24, vm + 0.24, P + 0.08, P + 0.095, vu + 0.04 + k * 0.065, vu + 0.075 + k * 0.065, groove);
                }
            }
            // The kick plate along the wall's foot; Industrial's is hazard-striped (accent and black, 20 cm each).
            var (k0, k1) = Clip(s0 + 0.01, s1 - 0.01, P, skirt);
            if (!ind)
                WBox(m, k0, k1, P, P + 0.03, P, skirt, tHex);
            else
            {
                WBox(m, k0, k1, P, P + 0.025, P, skirt, "#1b1c1e");
                var n = Math.Clamp((int)((k1 - k0 + 0.02) / 0.4), 0, 80);
                for (var k = 0; k < n; k++)
                    WBox(m, k0 + 0.04 + k * 0.4, k0 + 0.24 + k * 0.4, P + 0.025, P + 0.032, P + 0.02, skirt - 0.02, tHex);
            }
        }
        var lm0 = (L0 + L1) / 2;
        if (!hull)
        {
            Wall(true, A0, 1, L0, L1, ExtHoles("x0"));
            Wall(true, A1, -1, L0, L1, ExtHoles("x1"));
            Wall(false, L0, 1, A0 + 0.04, A1 - 0.04, ExtHoles("z0"));
            Wall(false, L1, -1, A0 + 0.04, A1 - 0.04, ExtHoles("z1"));
        }
        else
        {
            // Its sides on the skin's surface, and its ends (a run-on end too: the shell closes it where it stops) as wide as the
            // skin there, each row of panels as wide as the skin is at that row's heights.
            Wall(true, Lft(lm0, 1), 1, L0, L1, [], (s, u, o) => Side(0, s, u, o), null, lo[ax]);
            Wall(true, Rgt(lm0, 1), -1, L0, L1, [], (s, u, o) => Side(1, s, u, o), null, hi[ax]);
            foreach (var (l, inw) in new[] { (L0, 1.0), (L1, -1.0) })
            {
                (double, double) Reach(double u0, double u1)
                {
                    double c0 = double.MinValue, c1 = double.MaxValue;
                    foreach (var u in new[] { u0, (u0 + u1) / 2, u1 })
                        (c0, c1) = (Math.Max(c0, Lft(l, u)), Math.Min(c1, Rgt(l, u)));
                    return (c0 + hk + 0.03, c1 - hk - 0.03);
                }
                double w0 = double.MaxValue, w1 = double.MinValue;
                foreach (var u in gu)
                    (w0, w1) = (Math.Min(w0, Lft(l, Math.Min(u, H))), Math.Max(w1, Rgt(l, Math.Min(u, H))));
                if (w1 - w0 > 0.3)
                    Wall(false, l, inw, w0 + hk + 0.03, w1 - hk - 0.03, [], null, Reach);
            }
        }

        // ---- ribs: a frame ringing the room at each spacing, chamfered at the four corners (out to the hull: posts on its curved sides,
        // the beam and the floor bar reaching across to them).
        bool InExtSpan(string side, double s, double pad)
        {
            foreach (var e in exts)
                if (e.Side == side)
                {
                    var (s0, s1, _, _) = ExtSpan(r, e);
                    if (s > s0 - pad && s < s1 + pad)
                        return true;
                }
            return false;
        }
        foreach (var l in ribs)
        {
            double l0 = l - ribW / 2, l1 = l + ribW / 2;
            var ribCol = ind ? pDark : pFrame;
            // Ceiling beam and a low floor bar (walkable), short of the chamfers.
            var bi = hull ? ribD + 0.006 : ch;
            if (!vaulted)
                Band(m, q => Lft(q, H) + hk + bi, q => Rgt(q, H) - hk - bi, l0, l1, H - P - ribD, H - P - 0.001, ribCol, false);
            Band(m, q => Lft(q, FL) + hk + bi, q => Rgt(q, FL) - hk - bi, l0, l1, FL + 0.004, FL + 0.024, ind ? tHex : ribCol, false);
            for (var s = 0; s < 2; s++)
            {
                if (InExtSpan(s == 0 ? "x0" : "x1", l, ribW))
                    continue; // (the wall is open there to an extension)
                if (hull)
                {
                    // (no post across a door in that side, nor over its frame's architrave)
                    if (DoorHoles(d, own, ax, s == 0 ? lo[ax] : hi[ax], al, floor, sg).Any(o => l1 > o.S0 - 0.09 && l0 < o.S1 + 0.09))
                        continue;
                    var k = s;
                    double[] F(double sv, double u, double o) => Side(k, sv, u, o);
                    var probe = Side(k, l, H / 2, -0.3);
                    if (SBox(m, F, l0, l1, FL, H - P - 0.004, P, P + ribD, ribCol, probe, null, gu) && ind)
                        for (var q = 0; q < 4; q++) // hazard stripes up the post's foot
                            SBox(m, F, l0 - 0.004, l1 + 0.004, ch + 0.05 + q * 0.16, ch + 0.13 + q * 0.16, P + ribD, P + ribD + 0.006, q % 2 == 0 ? tHex : "#1b1c1e", probe, null, gu);
                    continue;
                }
                double wa = s == 0 ? A0 : A1, inw = s == 0 ? 1 : -1;
                double pa0 = wa + inw * P, pa1 = wa + inw * (P + ribD);
                var probe2 = W(wa - inw * 0.3, l, H / 2);
                Box(m, Math.Min(pa0, pa1), Math.Max(pa0, pa1), l0, l1, ch, H - ch, ribCol, probe2);
                if (ind)
                    for (var k = 0; k < 4; k++) // hazard stripes up the post's foot
                        Box(m, Math.Min(pa0, pa1) - (s == 0 ? 0 : 0.006), Math.Max(pa0, pa1) + (s == 0 ? 0.006 : 0), l0 - 0.004, l1 + 0.004, ch + 0.05 + k * 0.16, ch + 0.13 + k * 0.16, k % 2 == 0 ? tHex : "#1b1c1e", probe2);
                // Chamfers: a block at 45 degrees across each corner (ceiling and floor), its own frame.
                foreach (var top in new[] { true, false })
                {
                    if (top && vaulted)
                        continue; // (no ceiling corner under a vault)
                    // The corner itself; the chamfer runs from the wall at ch off the corner to the ceiling (or floor) at ch off it.
                    var o = W(wa + inw * P, l, top ? H - P : P);
                    if (!Clear(W(wa, l0, top ? H - ch : 0), W(wa + inw * ch, l1, top ? H : ch)) || budget < 24 || mates.Any(mm => InBox(probe2, mm)))
                        continue;
                    var ua = W(wa + inw * P + inw, l, top ? H - P : P);
                    var uu = W(wa + inw * P, l, (top ? H - P : P) + 1);
                    double[] ea = [ua[0] - o[0], ua[1] - o[1], ua[2] - o[2]], eu = [uu[0] - o[0], uu[1] - o[1], uu[2] - o[2]];
                    var su = top ? 1.0 : -1.0;
                    var r2 = Math.Sqrt(2);
                    // x along the slope, y its normal into the room, z along the ship.
                    double[] xa = [(ea[0] + su * eu[0]) / r2, (ea[1] + su * eu[1]) / r2, (ea[2] + su * eu[2]) / r2];
                    double[] ya = [(ea[0] - su * eu[0]) / r2, (ea[1] - su * eu[1]) / r2, (ea[2] - su * eu[2]) / r2];
                    double[] za = [0, 0, 0];
                    za[al] = 1;
                    var half = ch / r2;
                    double ox = o[0] + ya[0] * half, oy = o[1] + ya[1] * half, oz = o[2] + ya[2] * half;
                    m.Frame(ox, oy, oz, xa, ya, za);
                    m.Box(-half, -0.012, -ribW / 2, half, ribD, ribW / 2, ribCol);
                    m.World();
                    budget -= 12;
                    // Its lamp: a lit band on the upper chamfer.
                    if (top && lit && budget >= 12)
                    {
                        g.Frame(ox, oy, oz, xa, ya, za);
                        g.Box(-half * 0.6, ribD, -ribW * 0.3, half * 0.6, ribD + 0.012, ribW * 0.3, glowCol);
                        g.World();
                        budget -= 12;
                    }
                }
            }
        }

        // ---- its extensions: framed panels on each wall of the outline (a vault's along the room), plates on a wall one's floor and
        // panels on its ceiling, a vault's ends closed by a panel each.
        foreach (var e in exts)
        {
            if (ExtAt(d, r, e) is not { } x)
                continue;
            var t = sd - ShellGap;
            var ol = x.Outline;
            // The outline's walls' inside faces: each segment pulled in by the wall's thickness (towards the middle), the ends run on to
            // the room's own inside face (o = -sd) so they meet its wall's panels.
            var lines = new List<(double PA, double PO, double DA, double DO, double NA, double NO, double Len)>();
            for (var i = 0; i + 1 < ol.Count; i++)
            {
                var (q0, q1) = (ol[i], ol[i + 1]);
                double da = q1.A - q0.A, dO = q1.B - q0.B, len = Math.Sqrt(da * da + dO * dO);
                if (len < 1e-4)
                    continue;
                (da, dO) = (da / len, dO / len);
                double na = -dO, no = da;
                if (na * (x.CA - (q0.A + q1.A) / 2) + no * (x.CO - (q0.B + q1.B) / 2) < 0)
                    (na, no) = (-na, -no);
                lines.Add((q0.A + na * t, q0.B + no * t, da, dO, na, no, len));
            }
            if (lines.Count == 0)
                continue;
            // Where line i meets line j (or, j < 0, the room's inside face).
            (double A, double O) Meet(int i, int j)
            {
                var a = lines[i];
                if (j < 0)
                    return Math.Abs(a.DO) < 1e-6 ? (a.PA, a.PO) : (a.PA + a.DA * (-sd - a.PO) / a.DO, -sd);
                var c = lines[j];
                var den = a.DA * c.DO - a.DO * c.DA;
                if (Math.Abs(den) < 1e-9)
                    return (c.PA, c.PO);
                var k = ((c.PA - a.PA) * c.DO - (c.PO - a.PO) * c.DA) / den;
                return (a.PA + a.DA * k, a.PO + a.DO * k);
            }
            var vs = new List<(double A, double O)> { Meet(0, -1) };
            for (var i = 1; i < lines.Count; i++)
                vs.Add(Meet(i - 1, i));
            vs.Add(Meet(lines.Count - 1, -1));
            double cA = vs.Average(v => v.A), cO = vs.Average(v => v.O);
            // A polygon of the inside (shrunk towards its middle by in) as a thin solid from h0 to h1 (heights, or an up one's places along).
            void Poly(DesignMesh into, double inset, double h0, double h1, string col)
            {
                var n = vs.Count;
                if (budget < 4 * n || n < 3)
                    return;
                var pts = vs.Select<(double A, double O), (double A, double O)>(v =>
                {
                    double da = cA - v.A, dO = cO - v.O, l = Math.Sqrt(da * da + dO * dO);
                    return l < 1e-6 ? v : (v.A + da / l * Math.Min(inset, l * 0.9), v.O + dO / l * Math.Min(inset, l * 0.9));
                }).ToList();
                var c = DesignMesh.Hex(col);
                var top = pts.Select(v => T(x.P(v.A, v.O, h1))).ToList();
                var bot = pts.Select(v => T(x.P(v.A, v.O, h0))).ToList();
                for (var i = 1; i + 1 < n; i++)
                {
                    into.Tri(top[0], top[i], top[i + 1], c, false);
                    into.Tri(bot[0], bot[i + 1], bot[i], DesignMesh.Tone(c, 0.9), false);
                }
                for (var i = 0; i < n; i++)
                    into.Quad(bot[i], bot[(i + 1) % n], top[(i + 1) % n], top[i], DesignMesh.Tone(c, 0.8), false);
                budget -= 4 * n;
            }
            // A framed panel on a face F, as a wall's (a face alone where it is too small to frame, or on a rounded outline: its many
            // narrow faces framed one by one were busy and cost a vault several thousand triangles).
            var framed = lines.Count <= 5;
            void Panel(Func<double, double, double, double[]> F, double sa, double sb, double u0, double u1)
            {
                if (sb - sa < 0.1 || u1 - u0 < 0.1)
                    return;
                const double f = 0.045;
                SBox(m, F, sa, sb, u0, u1, P, P + 0.008, pHex, null);
                if (!framed || sb - sa < 0.5 || u1 - u0 < 0.5)
                    return;
                SBox(m, F, sa, sa + f, u0 + 0.03, u1 - 0.03, P + 0.002, P + 0.02, pFrame, null);
                SBox(m, F, sb - f, sb, u0 + 0.03, u1 - 0.03, P + 0.002, P + 0.02, pFrame, null);
                SBox(m, F, sa + 0.03, sb - 0.03, u0, u0 + f, P + 0.002, P + 0.02, pFrame, null);
                SBox(m, F, sa + 0.03, sb - 0.03, u1 - f, u1, P + 0.002, P + 0.02, pFrame, null);
            }
            if (!x.Up)
            {
                for (var i = 0; i + 1 < vs.Count; i++)
                {
                    var (v0, v1) = (vs[i], vs[i + 1]);
                    double da = v1.A - v0.A, dO = v1.O - v0.O, len = Math.Sqrt(da * da + dO * dO);
                    if (len < 0.05)
                        continue;
                    var ln = lines[Math.Min(i, lines.Count - 1)];
                    double[] F(double s, double u, double o) => x.P(v0.A + da / len * s + ln.NA * o, v0.O + dO / len * s + ln.NO * o, u);
                    var n = Math.Clamp((int)Math.Round(len / 1.1), 1, 8);
                    for (var j = 0; j < n; j++)
                    {
                        double sa = len * j / n + 0.02, sb = len * (j + 1) / n - 0.02;
                        Panel(F, sa, sb, skirt + 0.02, Math.Min(1.25, H * 0.5));
                        Panel(F, sa, sb, Math.Min(1.25, H * 0.5) + 0.03, H - 0.06);
                    }
                    SBox(m, F, 0.01, len - 0.01, P, skirt, P, P + 0.03, ind ? "#1b1c1e" : tHex, null);
                }
                if (r.Floor is "Plates" or "Hex")
                {
                    Poly(m, 0.003, FL, FL + 0.004, groove);
                    Poly(m, 0.03, FL + 0.004, FL + 0.016, Hx(DesignMesh.Tone(panel, ind ? 0.55 : 0.82)));
                }
                Poly(m, 0.003, H - P - 0.012, H - P, pFrame);
                Poly(lit ? g : m, 0.35, H - P - 0.022, H - P - 0.013, lit ? glowCol : offCol);
                continue;
            }
            // A vault: its outline's faces along the room's run (an up extension runs the room's length along its longer side).
            double r0 = x.L0 + sdl, r1 = x.L1 - sdl;
            var top0 = -1;
            var topO = double.MinValue;
            for (var i = 0; i + 1 < vs.Count; i++)
            {
                var (v0, v1) = (vs[i], vs[i + 1]);
                double da = v1.A - v0.A, dO = v1.O - v0.O, len = Math.Sqrt(da * da + dO * dO);
                if (len < 0.05)
                    continue;
                if ((v0.O + v1.O) / 2 > topO)
                    (top0, topO) = (i, (v0.O + v1.O) / 2);
                var ln = lines[Math.Min(i, lines.Count - 1)];
                double[] F(double s, double u, double o) => x.P(v0.A + da / len * u + ln.NA * o, v0.O + dO / len * u + ln.NO * o, s);
                int ns = Math.Clamp((int)Math.Round((r1 - r0) / 1.1), 1, 40), nu = Math.Clamp((int)Math.Round(len / 1.25), 1, 4);
                for (var j = 0; j < ns; j++)
                    for (var k = 0; k < nu; k++)
                        Panel(F, r0 + (r1 - r0) * j / ns + 0.02, r0 + (r1 - r0) * (j + 1) / ns - 0.02, len * k / nu + 0.02, len * (k + 1) / nu - 0.02);
            }
            if (top0 >= 0 && r1 - r0 > 0.6)
            {
                var (v0, v1) = (vs[top0], vs[top0 + 1]);
                double da = v1.A - v0.A, dO = v1.O - v0.O, len = Math.Sqrt(da * da + dO * dO);
                var ln = lines[Math.Min(top0, lines.Count - 1)];
                double[] F(double s, double u, double o) => x.P(v0.A + da / len * u + ln.NA * o, v0.O + dO / len * u + ln.NO * o, s);
                SBox(lit ? g : m, F, r0 + 0.2, r1 - 0.2, len / 2 - 0.06, len / 2 + 0.06, P + 0.012, P + 0.024, lit ? glowCol : offCol, null);
            }
            Poly(m, 0.02, r0 + 0.003, r0 + 0.011, pHex);
            Poly(m, 0.02, r1 - 0.011, r1 - 0.003, pHex);
        }
        return (m, g);
    }
}
