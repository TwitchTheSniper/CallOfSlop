namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The machine look on the gathering modules (the Drill Module and the Saw Module of machine_tiers; look.md "Machine look"). A tier keeps
/// its own body (TierStyle), so the language goes on as look boxes over the settled part: a dark skirt, an orange band at the way out, a
/// fill band up the saw's bin, an accent collar on the bit and hub caps on the blades that turn, hazard paint at what bites, and a strip
/// of buttons on the rim where a hand finds it (a module has no screen). Tier by build adds, never reshapes: hazard strips at tier 2,
/// a conduit with clamps at tier 3, steel corner plates at tier 4, a second collar or a rim strip at tier 5. A box gives way to a
/// detail the tier already has there, and nothing here is solid.
/// </summary>
public static class ModuleLook
{
    private static double[] V3(object? v) => [.. ((List<object?>)v!).Select(Py.D)];

    public static void Dress(List<PyDict> boxes, bool drill, int tier, double s)
    {
        var solids = boxes.Where(b => !Py.Truthy(b.Get("detail"))).Select(b => (Min: V3(b["min"]), Max: V3(b["max"]))).ToList();
        var given = boxes.Where(b => Py.Truthy(b.Get("detail"))).Select(b => (Min: V3(b["min"]), Max: V3(b["max"]))).ToList(); // (the tier's own dressing: it wins)
        var p = Math.Max(0.013, 0.011 * s); // how far a look box stands proud
        var thin = Math.Max(0.013, 0.012 * s);

        // The top of the solid body over a point of its plan (unit metres), up to a height.
        double TopAt(double x, double z, double below = 99) =>
            solids.Where(b => b.Min[0] <= x * s && x * s <= b.Max[0] && b.Min[2] <= z * s && z * s <= b.Max[2] && b.Max[1] <= below * s + 0.001).Select(b => b.Max[1]).DefaultIfEmpty(0).Max();

        // A look box from its corners in metres; it gives way to any detail already there.
        PyDict? Put(double[] mn, double[] mx, string color, string? finish = null)
        {
            if (given.Any(g => Enumerable.Range(0, 3).All(a => mn[a] < g.Max[a] + 0.003 && g.Min[a] - 0.003 < mx[a])))
                return null;
            if (solids.Any(b => Enumerable.Range(0, 3).All(a => b.Min[a] <= mn[a] && mx[a] <= b.Max[a])))
                return null; // (buried in the body)
            var o = MachineLook.D(mn, mx, color, finish);
            if (o is not null)
                boxes.Add(o);
            return o;
        }
        void Spin(PyDict? b) => MachineLook.Work(b, "spin", "+y");

        // A strip on a wall's outer face: side 0 = -x, 1 = +x, 2 = -z, 3 = +z; (a0, a1) along the wall, (y0, y1) up it, in unit metres; half is the body's half width.
        PyDict? Strip(int side, double half, double a0, double a1, double y0, double y1, string color, string? finish = null, double extra = 0, double grow = 0)
        {
            var sg = side % 2 == 0 ? -1 : 1;
            double f0 = half * s - 0.004 - extra * 0.5, f1 = half * s + p + extra;
            double[] mn = new double[3], mx = new double[3];
            var ax = side < 2 ? 0 : 2;
            (mn[ax], mx[ax]) = sg > 0 ? (f0, f1) : (-f1, -f0);
            (mn[2 - ax], mx[2 - ax]) = (a0 * s - grow, a1 * s + grow);
            (mn[1], mx[1]) = (y0 == 0 ? 0.004 : y0 * s, y1 * s); // (a skirt stops short of the underside: no face on the body's own planes)
            return Put(mn, mx, color, finish);
        }

        // The buttons: a dark strip on the -x rim's top with three accent buttons and a red stop.
        void Buttons(double x0, double x1, double z0, double z1)
        {
            var top = TopAt((x0 + x1) / 2, (z0 + z1) / 2);
            if (top <= 0 || Put([x0 * s, top + 0.001, z0 * s], [x1 * s, top + 0.001 + thin, z1 * s], MachineLook.Base) is null)
                return;
            double w = (x1 - x0) * s, len = (z1 - z0) * s, y = top + 0.001 + thin;
            for (var k = 0; k < 4; k++)
            {
                var c = z0 * s + len * (0.14 + 0.24 * k);
                var stop = k == 3;
                Put([x0 * s + w * 0.2, y, c - len * 0.07], [x1 * s - w * 0.2, y + thin * (stop ? 1.5 : 1), c + len * 0.07], stop ? MachineLook.Stop : MachineLook.Accent);
            }
        }

        if (drill)
        {
            const double h = 0.25; // the block's half width; the hopper ring stands on it, the mast and bit in front
            for (var side = 0; side < 4; side++)
                Strip(side, h, -h, h, 0.0, 0.07, MachineLook.Base, grow: side < 2 ? 0.006 : -0.006);
            Strip(1, h, -0.2, 0.2, 0.37, 0.44, MachineLook.Out); // the way out: the chute leaves the hopper on this side
            Put([0.19 * s - 0.004, 0.462 * s, -0.15 * s], [0.19 * s + p, 0.5 * s, 0.15 * s], MachineLook.Accent, "hazard paint");
            Buttons(-0.245, -0.195, -0.19, 0.19);
            // The bit: an accent collar under the mast that turns with it.
            Spin(Put([-0.088 * s, -0.352 * s, 0.592 * s], [0.088 * s, -0.303 * s, 0.768 * s], MachineLook.Accent));
            if (tier >= 2)
            {
                Strip(3, h, -0.24, -0.135, 0.39, 0.44, MachineLook.Accent, "hazard paint");
                Strip(3, h, 0.135, 0.24, 0.39, 0.44, MachineLook.Accent, "hazard paint");
            }
            if (tier >= 3)
            {
                Strip(0, h, -0.2, 0.2, 0.1, 0.13, MachineLook.Base, extra: 0.004);
                Strip(0, h, -0.16, -0.13, 0.085, 0.145, MachineLook.Steel, "steel", 0.007);
                Strip(0, h, 0.13, 0.16, 0.085, 0.145, MachineLook.Steel, "steel", 0.007);
            }
            if (tier >= 4)
                foreach (var side in new[] { 2, 3 })
                {
                    Strip(side, h, -0.244, -0.2, 0.09, 0.36, MachineLook.Steel, "steel", 0.002);
                    Strip(side, h, 0.2, 0.244, 0.09, 0.36, MachineLook.Steel, "steel", 0.002);
                }
            if (tier >= 5)
                Spin(Put([-0.082 * s, -1.12 * s, 0.598 * s], [0.082 * s, -1.08 * s, 0.762 * s], MachineLook.Steel, "steel"));
            return;
        }

        const double b0 = 0.5; // the bin's half width
        var wall = TopAt(-0.47, 0, 0.72) / s; // the walls' top (a slatted or a low bin is lower)
        for (var side = 0; side < 4; side++)
            Strip(side, b0, -b0, b0, 0.0, 0.09, MachineLook.Base, grow: side < 2 ? 0.006 : -0.006);
        // The fill band: a steel strip up the bin's side with a mark at each quarter.
        if (Strip(1, b0, -0.36, -0.24, 0.1, wall - 0.04, MachineLook.Steel, "steel") is not null)
            for (var k = 1; k < 4; k++)
            {
                var y = 0.1 + (wall - 0.14) * k / 4;
                Strip(1, b0, -0.36, -0.24, y - 0.008, y + 0.008, MachineLook.Ink, extra: 0.003, grow: 0.004);
            }
        Strip(3, b0, 0.14, 0.45, wall - 0.13, wall - 0.05, MachineLook.In); // the wood comes in over the front wall, each side of the arm
        Strip(3, b0, -0.45, -0.14, wall - 0.13, wall - 0.05, MachineLook.In);
        Strip(0, b0, -0.4, 0.4, wall - 0.13, wall - 0.05, MachineLook.Out); // and is taken out over the side
        Buttons(-0.495, -0.445, -0.3, 0.08);
        // The blade bar: hazard paint along the edge that cuts.
        Put([-0.48 * s, 0.248 * s, 1.36 * s - 0.004], [0.48 * s, 0.312 * s, 1.36 * s + p], MachineLook.Accent, "hazard paint");
        if (tier >= 2)
        {
            Put([-0.5 * s - p, 0.248 * s, 1.02 * s], [-0.5 * s + 0.004, 0.312 * s, 1.34 * s], MachineLook.Accent, "hazard paint");
            Put([0.5 * s - 0.004, 0.248 * s, 1.02 * s], [0.5 * s + p, 0.312 * s, 1.34 * s], MachineLook.Accent, "hazard paint");
        }
        if (tier >= 3)
        {
            Strip(0, b0, -0.42, 0.42, 0.14, 0.17, MachineLook.Base, extra: 0.004);
            Strip(0, b0, -0.36, -0.33, 0.125, 0.185, MachineLook.Steel, "steel", 0.007);
            Strip(0, b0, 0.33, 0.36, 0.125, 0.185, MachineLook.Steel, "steel", 0.007);
        }
        if (tier >= 4)
            foreach (var side in new[] { 2, 3 })
            {
                Strip(side, b0, -0.494, -0.43, 0.11, wall - 0.16, MachineLook.Steel, "steel", 0.002);
                Strip(side, b0, 0.43, 0.494, 0.11, wall - 0.16, MachineLook.Steel, "steel", 0.002);
            }
        if (tier >= 5)
            Strip(2, b0, -0.4, 0.4, 0.2, 0.23, MachineLook.Steel, "steel", 0.002);
    }
}
