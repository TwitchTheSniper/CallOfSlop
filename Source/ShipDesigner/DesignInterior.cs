namespace CallOfSlop.ShipDesigner;

public static partial class DesignScene
{
    /// <summary>The most triangles one room's interior cladding may add (solid and glow together); past it the rest is left bare.</summary>
    public const int MaxInteriorTris = 9000;

    /// <summary>
    /// A room's interior cladding (the user's corridor references, 2026-11-24): framed wall panels with seams, ribs ringing the room every
    /// <see cref="Room.RibSpacing"/> metres chamfered at the corners, a kick plate, ceiling light strips and floor plates (laid by
    /// <see cref="LayPlates"/>) with lit floor strips. Industrial adds deeper ribs, vent boxes and hazard-striped trim. Everything sits 3 mm
    /// and more proud of the inside faces; nothing is drawn inside a door's clearance, outside every hull or on a wall open to a room of
    /// its group. Returns the solid mesh and the glow mesh (the page draws that unlit, as the mesh carries no emissive).
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
        double A0 = lo[ax] + (r.ToHull ? 0 : sd), A1 = hi[ax] - (r.ToHull ? 0 : sd), L0 = lo[al] + sdl, L1 = hi[al] - sdl, H = hi[up] - lo[up] - sd;
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

        double[] W(double a, double l, double u) { var v = new double[3]; v[ax] = a; v[al] = l; v[up] = floor + sg * u; return v; }
        bool InBox(double[] p, double[] o) => p[0] > o[0] && p[0] < o[1] && p[1] > o[2] && p[1] < o[3] && p[2] > o[4] && p[2] < o[5];
        bool Clear(double[] c0, double[] c1)
        {
            foreach (var q in doors)
            {
                double ul = d.Stacked ? q[up] - 2.2 : q[up] - 0.3, uh = d.Stacked ? q[up] + 0.3 : q[up] + 2.2;
                if (Math.Min(c0[ax], c1[ax]) < q[ax] + 0.6 && Math.Max(c0[ax], c1[ax]) > q[ax] - 0.6 &&
                    Math.Min(c0[al], c1[al]) < q[al] + 0.6 && Math.Max(c0[al], c1[al]) > q[al] - 0.6 &&
                    Math.Min(c0[up], c1[up]) < uh && Math.Max(c0[up], c1[up]) > ul)
                    return false;
            }
            return true;
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
            if (!d.Hulls.Any(h => h.Depth(mid[0], mid[1], mid[2]) > 0.01))
                return false;
            if (probe is not null && mates.Any(o => InBox(probe, o)))
                return false;
            into.Box(Math.Min(c0[0], c1[0]), Math.Min(c0[1], c1[1]), Math.Min(c0[2], c1[2]), Math.Max(c0[0], c1[0]), Math.Max(c0[1], c1[1]), Math.Max(c0[2], c1[2]), col);
            budget -= 12;
            return true;
        }
        void Strip(double a0, double a1, double l0, double l1, double u0, double u1, double[]? probe = null)
        {
            if (lit) Box(g, a0, a1, l0, l1, u0, u1, glowCol, probe);
            else Box(m, a0, a1, l0, l1, u0, u1, offCol, probe);
        }

        const double P = 0.003; // proud of the inside faces
        var FL = FloorTop(r) + 0.009; // the floor layer sits on the page's 8 cm floor marker (or the shell's floor), 9 mm proud of its top
        var sides = !r.ToHull;
        var foreEnd = !(r.ToHull && r.RunOn && !d.Stacked);
        var aftEnd = !(r.ToHull && r.RunAft && !d.Stacked);
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

        // ---- floor: plates (the hull's plate layer, on a flat grid) over a dark groove base, lit strips along the walls.
        {
            double fa0 = A0 + P, fa1 = A1 - P, fl0 = L0 + P, fl1 = L1 - P;
            if (r.Floor is "Plates" or "Hex") // the other floors are the shell's dressing
                Box(m, fa0, fa1, fl0, fl1, FL, FL + 0.004, groove, null);
            int nu = Math.Clamp((int)Math.Round((fa1 - fa0) / 0.6), 1, 40), nv = Math.Clamp((int)Math.Round((fl1 - fl0) / 0.6), 1, 60);
            var taken = new bool[nu, nv];
            for (var x = 0; x < nu; x++)
                for (var y = 0; y < nv; y++)
                {
                    double ca = fa0 + (fa1 - fa0) * (x + 0.5) / nu, cl = fl0 + (fl1 - fl0) * (y + 0.5) / nv;
                    var c = W(ca, cl, 0.01);
                    taken[x, y] = !Clear(W(fa0 + (fa1 - fa0) * x / nu, fl0 + (fl1 - fl0) * y / nv, 0), W(fa0 + (fa1 - fa0) * (x + 1) / nu, fl0 + (fl1 - fl0) * (y + 1) / nv, 0.02))
                        || !d.Hulls.Any(h => h.Depth(c[0], c[1], c[2]) > 0.01);
                }
            if (nu * nv * 30 < budget && r.Floor is ("Plates" or "Hex"))
            {
                var fm = new DesignMesh();
                var n1 = W(0, 0, 1);
                var n0 = W(0, 0, 0);
                (double, double, double) nrm = (n1[0] - n0[0], n1[1] - n0[1], n1[2] - n0[2]);
                var arc = fa1 - fa0;
                var len = fl1 - fl0;
                (double, double, double) T(double[] v) => (v[0], v[1], v[2]);
                LayPlates(fm, nu, nv, arc, len, new Random(unchecked(r.Id * 7919 + 17)), ind, 0.12, DesignMesh.Tone(panel, ind ? 0.55 : 0.82),
                    (u, v) => T(W(fa0 + arc * u, fl0 + len * v, FL + 0.004)), (u, v) => nrm,
                    // Its skirts reach down to the groove base (the plate layer's sink is 3 cm; offset so they start in the base, not the floor).
                    (u, v, h) => T(W(fa0 + arc * u, fl0 + len * v, FL + 0.004 + Math.Max(h, -0.003))), taken, null, 0, r.Floor == "Hex");
                m.P.AddRange(fm.P);
                m.C.AddRange(fm.C);
                budget -= fm.Tris;
            }
            // Lit strips along the floor's long edges (Industrial: hazard edges instead, with one strip down the middle).
            if (!ind)
            {
                Strip(A0 + 0.2, A0 + 0.26, L0 + 0.1, L1 - 0.1, FL + 0.006, FL + 0.026);
                Strip(A1 - 0.26, A1 - 0.2, L0 + 0.1, L1 - 0.1, FL + 0.006, FL + 0.026);
            }
            else
                Strip((A0 + A1) / 2 - 0.04, (A0 + A1) / 2 + 0.04, L0 + 0.3, L1 - 0.3, FL + 0.006, FL + 0.024);
        }

        // ---- ceiling: panels with seams and light strips down its length.
        {
            int na = Math.Clamp((int)Math.Round((A1 - A0) / 1.2), 1, 12), nl = Math.Clamp((int)Math.Round((L1 - L0) / 1.0), 1, 60);
            for (var x = 0; x < na; x++)
                for (var y = 0; y < nl; y++)
                {
                    double a0 = A0 + (A1 - A0) * x / na + 0.015, a1 = A0 + (A1 - A0) * (x + 1) / na - 0.015;
                    double l0 = L0 + (L1 - L0) * y / nl + 0.015, l1 = L0 + (L1 - L0) * (y + 1) / nl - 0.015;
                    Box(m, a0, a1, l0, l1, H - P - 0.012, H - P, (x + y) % 2 == 0 ? pHex : pFrame);
                }
            var strips = ind || A1 - A0 < 2.4 ? new[] { (A0 + A1) / 2 } : new[] { A0 + (A1 - A0) * 0.3, A0 + (A1 - A0) * 0.7 };
            foreach (var c in strips)
            {
                Box(m, c - 0.1, c + 0.1, L0 + 0.15, L1 - 0.15, H - P - 0.03, H - P - 0.008, pDark);
                Strip(c - 0.06, c + 0.06, L0 + 0.2, L1 - 0.2, H - P - 0.04, H - P - 0.026);
            }
        }

        // ---- walls: framed panels between the ribs, a kick plate, Industrial vents.
        void Wall(bool across, double at, double inward, double s0, double s1)
        {
            // across: the wall's plane is at a = at (a side wall, running along l); else at l = at (an end wall, running across a).
            double[] Pt(double s, double off, double u) => across ? W(at + inward * off, s, u) : W(s, at + inward * off, u);
            // The doors through this wall, as openings (Karl, 2026-11-25): the panels are cut round them, not dropped.
            var wt = sd > 0 ? sd - ShellGap : 0;
            var holes = DoorHoles(d, doors, across ? ax : al, at - inward * wt / 2, across ? al : ax, floor, sg);
            bool WBox1(DesignMesh into, double sa, double sb, double o0, double o1, double u0, double u1, string col)
            {
                var probe = Pt((sa + sb) / 2, -0.3, (u0 + u1) / 2);
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
                double sa = c0 + 0.02 + (ribs.Any(q => Math.Abs(q - c0) < 1e-6) ? gapR : 0), sb = c1 - 0.02 - (ribs.Any(q => Math.Abs(q - c1) < 1e-6) ? gapR : 0);
                if (sb - sa < 0.15)
                    continue;
                foreach (var (u0, u1) in rows)
                {
                    if (u1 - u0 < 0.15)
                        continue;
                    // A frame round a recessed face: Clean's frame is light and rounded off by its chamfered corners, Industrial's dark.
                    const double f = 0.045;
                    WBox(m, sa, sb, P, P + 0.008, u0, u1, pHex);
                    WBox(m, sa, sa + f, P + 0.002, P + 0.02, u0 + 0.03, u1 - 0.03, pFrame);
                    WBox(m, sb - f, sb, P + 0.002, P + 0.02, u0 + 0.03, u1 - 0.03, pFrame);
                    WBox(m, sa + 0.03, sb - 0.03, P + 0.002, P + 0.02, u0, u0 + f, pFrame);
                    WBox(m, sa + 0.03, sb - 0.03, P + 0.002, P + 0.02, u1 - f, u1, pFrame);
                }
                if (ind && (vent++ % 2 == 0) && H > 2.2 && sb - sa > 0.7)
                {
                    // A vent box: a housing high on the wall with grille slats across it.
                    double vm = (sa + sb) / 2, vu = Math.Min(H - 0.45, 1.85);
                    if (!holes.Any(o => vm + 0.28 > o.S0 - 0.1 && vm - 0.28 < o.S1 + 0.1 && vu + 0.3 > o.H0 - 0.1 && vu < o.H1 + 0.1) && WBox(m, vm - 0.28, vm + 0.28, P + 0.008, P + 0.08, vu, vu + 0.3, pDark))
                        for (var k = 0; k < 4; k++)
                            WBox(m, vm - 0.24, vm + 0.24, P + 0.08, P + 0.095, vu + 0.04 + k * 0.065, vu + 0.075 + k * 0.065, groove);
                }
            }
            // The kick plate along the wall's foot; Industrial's is hazard-striped (accent and black, 20 cm each).
            if (!ind)
                WBox(m, s0 + 0.01, s1 - 0.01, P, P + 0.03, P, skirt, tHex);
            else
            {
                WBox(m, s0 + 0.01, s1 - 0.01, P, P + 0.025, P, skirt, "#1b1c1e");
                var n = Math.Clamp((int)((s1 - s0) / 0.4), 0, 80);
                for (var k = 0; k < n; k++)
                    WBox(m, s0 + 0.05 + k * 0.4, s0 + 0.25 + k * 0.4, P + 0.025, P + 0.032, P + 0.02, skirt - 0.02, tHex);
            }
        }
        if (sides)
        {
            Wall(true, A0, 1, L0, L1);
            Wall(true, A1, -1, L0, L1);
        }
        double ea0 = sides ? A0 + 0.04 : A0 + 0.3, ea1 = sides ? A1 - 0.04 : A1 - 0.3;
        if (foreEnd)
            Wall(false, L0, 1, ea0, ea1);
        if (aftEnd)
            Wall(false, L1, -1, ea0, ea1);

        // ---- ribs: a frame ringing the room at each spacing, chamfered at the four corners.
        foreach (var l in ribs)
        {
            double l0 = l - ribW / 2, l1 = l + ribW / 2;
            var ribCol = ind ? pDark : pFrame;
            // Ceiling beam and a low floor bar (walkable), short of the chamfers.
            Box(m, A0 + (sides ? ch : 0.1), A1 - (sides ? ch : 0.1), l0, l1, H - P - ribD, H - P - 0.001, ribCol);
            Box(m, A0 + (sides ? ch : 0.3), A1 - (sides ? ch : 0.3), l0, l1, FL + 0.004, FL + 0.024, ind ? tHex : ribCol);
            if (!sides)
                continue;
            for (var s = 0; s < 2; s++)
            {
                double wa = s == 0 ? A0 : A1, inw = s == 0 ? 1 : -1;
                double pa0 = wa + inw * P, pa1 = wa + inw * (P + ribD);
                var probe = W(wa - inw * 0.3, l, H / 2);
                Box(m, Math.Min(pa0, pa1), Math.Max(pa0, pa1), l0, l1, ch, H - ch, ribCol, probe);
                if (ind)
                    for (var k = 0; k < 4; k++) // hazard stripes up the post's foot
                        Box(m, Math.Min(pa0, pa1) - (s == 0 ? 0 : 0.006), Math.Max(pa0, pa1) + (s == 0 ? 0.006 : 0), l0 - 0.004, l1 + 0.004, ch + 0.05 + k * 0.16, ch + 0.13 + k * 0.16, k % 2 == 0 ? tHex : "#1b1c1e", probe);
                // Chamfers: a block at 45 degrees across each corner (ceiling and floor), its own frame.
                foreach (var top in new[] { true, false })
                {
                    // The corner itself; the chamfer runs from the wall at ch off the corner to the ceiling (or floor) at ch off it.
                    var o = W(wa + inw * P, l, top ? H - P : P);
                    if (!Clear(W(wa, l0, top ? H - ch : 0), W(wa + inw * ch, l1, top ? H : ch)) || budget < 24 || mates.Any(mm => InBox(probe, mm)))
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
        return (m, g);
    }
}
