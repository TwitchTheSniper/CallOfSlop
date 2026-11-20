using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Stair round (Karl, 2026-11-20): five open steel stairs to replace the solid Steel Wide Stair 3 m, each under 1,000 triangles,
/// from his reference of a steel stair with side stringers, rails and separate treads with yellow and black hazard nosing, open
/// underneath (room to shove junk under it). Each keeps the Wide Stair's footprint, rise, steps and sockets (1.75 m wide, 3.5 m
/// deep, 3 m up in 12 steps of 0.25 m), so the picked one can take its place in every blueprint.
///
/// What a person walks on is a hidden 5 cm slab at each tread ("collider": "boxes"): nothing under the treads collides, so the
/// space below is free; the looks (stringers, rails, stripes) don't collide.
/// </summary>
public sealed partial class StyleSheetParts
{
    private const int StairSteps = 12;
    private const double StairW = 1.75, StairDepth = 3.5, StairRise = 3.0, StairRun = StairDepth / StairSteps, StairStep = StairRise / StairSteps;
    private const double O = -0.125; // (a building part's origin is its first cell's centre: the frame's corner is at -0.125)

    private const string Steel = "#6f767f", SteelDark = "#4f565e", SteelLight = "#8c939b", Yellow = "#e7b416", Black = "#1c1d1f",
        Rust = "#7a5a40", Worn = "#9a8f7c";

    /// <summary>A stair's part: name, sockets, the hidden tread slabs a person stands on, stats. Looks are the caller's (in frame metres).</summary>
    private sealed class Stair
    {
        public readonly FittingLookParts.Kit K;
        public Stair(string name, string desc)
        {
            var p = new Part(name, Small, "Stairs and access", desc, role: "frame", tool: "welder", material: "steel", color: Steel, group: Group);
            p.Set(("finish", "painted steel"), ("collider", "boxes"), ("walk", "stairs"));
            for (var k = 0; k < StairSteps; k++)
                p.Add(PartKitMath.Box([O, O + Top(k) - 0.05, O + k * StairRun], [O + StairW, O + Top(k), O + (k + 1) * StairRun], Steel, hidden: true));
            // The Wide Stair's sockets, cell for cell (BuildingParts.StraightStair, width 7, run 14, rise 12).
            void S(string n, int x, int y, int z, string dir)
            {
                var at = new[] { x * 0.25, y * 0.25, z * 0.25 };
                var a = "xyz".IndexOf(dir[1]);
                var sg = dir[0] == '+' ? 1 : -1;
                at[a] += sg * 0.125;
                p.Sock(n, at, dir);
                // A hidden 2 cm pad under each socket, so the socket sits on a solid face (the open stair has none there),
                // small enough to leave the floor under the flight free.
                if (n[0] != 't')
                {
                    double[] mn = [at[0] - 0.1, at[1] - 0.1, at[2] - 0.1], mx = [at[0] + 0.1, at[1] + 0.1, at[2] + 0.1];
                    mn[a] = sg > 0 ? at[a] - 0.02 : at[a];
                    mx[a] = sg > 0 ? at[a] : at[a] + 0.02;
                    p.Add(PartKitMath.Box(mn, mx, Steel, hidden: true));
                }
            }
            foreach (var x in new[] { 0, 6 })
            {
                foreach (var z in new[] { 0, 13 })
                    S($"b{x}_{z}", x, 0, z, "-y");
                S($"f{x}", x, 0, 0, "-z");
                foreach (var y in new[] { 0, 6, 11 })
                    S($"k{x}_{y}", x, y, 13, "+z");
                S($"t{x}", x, 11, 13, "+y");
            }
            foreach (var z in new[] { 0, 6, 7, 13 })
            {
                S($"l{z}", 0, 0, z, "-x");
                S($"r{z}", 6, 0, z, "+x");
            }
            p.Stats(620, 520, 20, 3.0);
            K = new FittingLookParts.Kit(p);
        }

        public static double Top(int k) => (k + 1) * StairStep;
        public static double Front(int k) => k * StairRun;

        // Looks in frame metres (0..1.75, 0..3, 0..3.5).
        public void Box(double x0, double y0, double z0, double x1, double y1, double z1, string col, string finish) => K.Box(x0 + O, y0 + O, z0 + O, x1 + O, y1 + O, z1 + O, col, finish);
        public void Paint(double x0, double y0, double z0, double x1, double y1, double z1, string col, string finish = "painted steel") => K.Paint(x0 + O, y0 + O, z0 + O, x1 + O, y1 + O, z1 + O, col, finish);
        /// <summary>A side profile (y, z pairs in frame metres) across x.</summary>
        public void Profile(double x0, double x1, (double Y, double Z)[] yz, string col, string finish) =>
            K.Profile(x0 + O, x1 + O, [.. yz.Select(q => (q.Y + O, q.Z + O))], col, finish);
        public void Hull(IEnumerable<double[]> pts, string col, string finish) => K.Hull(pts.Select(q => new[] { q[0] + O, q[1] + O, q[2] + O }), col, finish);

        /// <summary>A stringer along the flight: its top edge <paramref name="above"/> over the nosing line (negative: under the
        /// treads), <paramref name="depth"/> deep, cut level with the floor below and the floor above and at the flight's ends.</summary>
        public void Stringer(double x0, double x1, double depth, double above, string col, string finish)
        {
            var s = StairStep / StairRun;
            double U(double z) => s * z + StairStep + above;
            var poly = new List<(double Y, double Z)> { (U(-1), -1), (U(StairDepth + 1), StairDepth + 1), (U(StairDepth + 1) - depth, StairDepth + 1), (U(-1) - depth, -1) };
            poly = Clip(poly, q => q.Y, 0, true);
            poly = Clip(poly, q => q.Y, StairRise, false);
            poly = Clip(poly, q => q.Z, 0, true);
            poly = Clip(poly, q => q.Z, StairDepth, false);
            Profile(x0, x1, [.. poly], col, finish);
        }

        /// <summary>One side of a convex polygon kept: v(q) at or over (keepOver) or under the cut.</summary>
        private static List<(double Y, double Z)> Clip(List<(double Y, double Z)> pts, Func<(double Y, double Z), double> v, double cut, bool keepOver)
        {
            bool In((double Y, double Z) q) => keepOver ? v(q) >= cut - 1e-9 : v(q) <= cut + 1e-9;
            var o = new List<(double Y, double Z)>();
            for (var i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                if (In(a))
                    o.Add(a);
                if (In(a) != In(b))
                {
                    var t = (cut - v(a)) / (v(b) - v(a));
                    o.Add((Math.Round(a.Y + (b.Y - a.Y) * t, 4), Math.Round(a.Z + (b.Z - a.Z) * t, 4)));
                }
            }
            return o;
        }

        /// <summary>The top edge's height at z of a stringer <paramref name="above"/> the nosing line.</summary>
        public static double Nose(double z, double above = 0) => z * StairStep / StairRun + StairStep + above;

        /// <summary>A slanted bar (a rail) from (z0, y0) to (z1, y1), <paramref name="w"/> wide across x and <paramref name="h"/> deep.</summary>
        public void Bar(double xc, double w, double z0, double y0, double z1, double y1, double h, string col, string finish) =>
            Profile(xc - w / 2, xc + w / 2, [(y0, z0), (y0 + h, z0), (y1 + h, z1), (y1, z1)], col, finish);

        /// <summary>A black diagonal stripe lying on a nosing's top (x along the tread, z0..z1 the nosing's depth, y its top).</summary>
        public void Stripe(double x, double w, double z0, double z1, double y, string col = Black)
        {
            var lean = z1 - z0;
            Hull([[x, y, z0], [x + w, y, z0], [x + w + lean, y, z1], [x + lean, y, z1],
                  [x, y + 0.004, z0], [x + w, y + 0.004, z0], [x + w + lean, y + 0.004, z1], [x + lean, y + 0.004, z1]], col, "hazard paint");
        }

        /// <summary>A sawtooth tooth lying on a nosing's top: a black triangle, its point to the back.</summary>
        public void Tooth(double x, double w, double z0, double z1, double y, string col = Black) =>
            Hull([[x, y, z0], [x + w, y, z0], [x + w / 2, y, z1], [x, y + 0.004, z0], [x + w, y + 0.004, z0], [x + w / 2, y + 0.004, z1]], col, "hazard paint");

        public Part Done() => K.P;
    }

    /// <summary>A: the reference, near as it is: two channel stringers, plain grey plate treads, a yellow nosing with black
    /// diagonal stripes, a tube rail on each side.</summary>
    private static Part StairA()
    {
        var s = new Stair("Style Stair A Reference", "Draft (stair round): open steel stair as in the reference: channel stringers, plate treads, striped hazard nosing, a rail each side.");
        const double sw = 0.06, nose = 0.07;
        foreach (var x0 in new[] { 0.0, StairW - sw })
            s.Stringer(x0, x0 + sw, 0.32, 0.05, Steel, "painted steel");
        for (var k = 0; k < StairSteps; k++)
        {
            double top = Stair.Top(k), z0 = Stair.Front(k), z1 = z0 + StairRun;
            s.Box(sw, top - 0.035, z0 + nose, StairW - sw, top, z1 + 0.01, SteelLight, "painted steel");
            s.Box(sw, top - 0.04, z0, StairW - sw, top, z0 + nose, Yellow, "hazard paint");
            for (var i = 0; i < 4; i++)
                s.Stripe(sw + 0.08 + i * 0.4, 0.12, z0 + 0.004, z0 + nose - 0.004, top);
        }
        // Rails: a square tube on two posts each side, 0.9 m over the nosings.
        foreach (var xc in new[] { sw / 2, StairW - sw / 2 })
        {
            s.Bar(xc, 0.045, 0.15, 0.15 * StairStep / StairRun + 0.95, StairDepth - 0.1, StairRise + 0.95 - 0.1 * StairStep / StairRun, 0.045, SteelLight, "painted steel");
            foreach (var z in new[] { 0.15, StairDepth - 0.1 })
            {
                var y = z * StairStep / StairRun + 0.25;
                s.Box(xc - 0.02, y, z - 0.02, xc + 0.02, y + 0.95 - 0.25 + 0.0, z + 0.02, Steel, "painted steel");
            }
        }
        return s.Done();
    }

    /// <summary>B: one spine. A deep box beam up the middle carries cantilevered tread-plate treads; plain yellow nosings with a
    /// black rubber lip; one rail. Both sides wide open underneath.</summary>
    private static Part StairB()
    {
        var s = new Stair("Style Stair B Spine", "Draft (stair round): a single box spine up the middle, cantilevered tread-plate steps, solid yellow nosings, one rail.");
        s.Stringer(StairW / 2 - 0.12, StairW / 2 + 0.12, 0.3, -0.3, SteelDark, "painted steel");
        for (var k = 0; k < StairSteps; k++)
        {
            double top = Stair.Top(k), z0 = Stair.Front(k), z1 = z0 + StairRun;
            s.Box(0.02, top - 0.03, z0 + 0.06, StairW - 0.02, top, z1 + 0.012, Steel, "tread plate");
            s.Box(0.02, top - 0.04, z0, StairW - 0.02, top, z0 + 0.06, Yellow, "hazard paint");
            s.Box(0.03, top - 0.05, z0 - 0.012, StairW - 0.03, top - 0.012, z0, Black, "rubber");
            s.Profile(StairW / 2 - 0.3, StairW / 2 + 0.3, [(top - 0.031, z0 + 0.03), (top - 0.031, z1 - 0.01), (Stair.Nose(z0 + 0.03, -0.3) - 0.01, z0 + 0.03)], SteelDark, "painted steel"); // (the bracket on the spine)
        }
        s.Bar(0.05, 0.05, 0.12, 0.12 * StairStep / StairRun + 1.0, StairDepth - 0.1, StairRise + 1.0 - 0.1 * StairStep / StairRun, 0.05, Yellow, "hazard paint");
        foreach (var z in new[] { 0.12, StairDepth / 2, StairDepth - 0.1 })
        {
            var k = Math.Min(StairSteps - 1, (int)(z / StairRun));
            s.Box(0.03, Stair.Top(k), z - 0.022, 0.075, z * StairStep / StairRun + 1.0, z + 0.022, SteelDark, "painted steel");
        }
        return s.Done();
    }

    /// <summary>C: grating. Flat-bar stringers, each tread an open frame of bars you can see through; only the first and last steps
    /// carry hazard stripes (as on a works floor); a top rail and a knee rail each side.</summary>
    private static Part StairC()
    {
        var s = new Stair("Style Stair C Grating", "Draft (stair round): open grating steps between flat-bar stringers, striped first and last steps, double rails.");
        const double sw = 0.05;
        foreach (var x0 in new[] { 0.0, StairW - sw })
            s.Stringer(x0, x0 + sw, 0.26, 0.04, SteelDark, "dirty steel");
        for (var k = 0; k < StairSteps; k++)
        {
            double top = Stair.Top(k), z0 = Stair.Front(k), z1 = z0 + StairRun;
            var edge = k == 0 || k == StairSteps - 1;
            s.Box(sw, top - 0.045, z0, StairW - sw, top, z0 + 0.06, Yellow, "hazard paint");
            s.Box(sw, top - 0.045, z1 - 0.04, StairW - sw, top, z1, Steel, "grating");
            foreach (var zc in new[] { 0.11, 0.18 })
                s.Box(sw, top - 0.035, z0 + zc - 0.012, StairW - sw, top - 0.006, z0 + zc + 0.012, Steel, "grating");
            if (edge)
                for (var i = 0; i < 6; i++)
                    s.Stripe(sw + 0.06 + i * 0.27, 0.1, z0 + 0.004, z0 + 0.056, top);
        }
        foreach (var xc in new[] { sw / 2, StairW - sw / 2 })
            foreach (var h in new[] { 0.5, 0.95 })
                s.Bar(xc, 0.04, 0.1, 0.1 * StairStep / StairRun + h, StairDepth - 0.08, StairRise + h - 0.08 * StairStep / StairRun, 0.04, h > 0.6 ? Yellow : Steel, h > 0.6 ? "hazard paint" : "painted steel");
        foreach (var xc in new[] { sw / 2, StairW - sw / 2 })
            foreach (var z in new[] { 0.1, StairDepth / 2, StairDepth - 0.08 })
            {
                var y = z * StairStep / StairRun + 0.2;
                s.Box(xc - 0.02, y, z - 0.02, xc + 0.02, z * StairStep / StairRun + 0.95, z + 0.02, Steel, "painted steel");
            }
        return s.Done();
    }

    /// <summary>D: heavy deck stair. Deep box stringers with a capping flange, solid plate treads, a sawtooth hazard band (black
    /// teeth on yellow), kick plates along the stringers and a thick handrail each side.</summary>
    private static Part StairHeavy()
    {
        var s = new Stair("Style Stair D Heavy", "Draft (stair round): heavy box stringers with flanges, plate steps with a sawtooth hazard band, thick rails.");
        const double sw = 0.09;
        foreach (var x0 in new[] { 0.0, StairW - sw })
        {
            s.Stringer(x0, x0 + sw, 0.36, 0.1, SteelDark, "painted steel");
            s.Stringer(x0 - 0.008, x0 + sw + 0.008, 0.035, 0.13, Yellow, "hazard paint"); // (a yellow cap along its top)
        }
        for (var k = 0; k < StairSteps; k++)
        {
            double top = Stair.Top(k), z0 = Stair.Front(k), z1 = z0 + StairRun;
            s.Box(sw, top - 0.04, z0 + 0.08, StairW - sw, top, z1 + 0.012, Steel, "deck plate");
            s.Box(sw, top - 0.05, z0, StairW - sw, top, z0 + 0.08, Yellow, "hazard paint");
            for (var i = 0; i < 5; i++)
                s.Tooth(sw + 0.04 + i * 0.3, 0.2, z0 + 0.004, z0 + 0.076, top);
        }
        foreach (var xc in new[] { sw / 2, StairW - sw / 2 })
        {
            s.Bar(xc, 0.06, 0.15, 0.15 * StairStep / StairRun + 1.0, StairDepth - 0.12, StairRise + 1.0 - 0.12 * StairStep / StairRun, 0.06, Black, "rubber");
            foreach (var z in new[] { 0.15, StairDepth / 2, StairDepth - 0.12 })
            {
                var y = z * StairStep / StairRun + 0.3;
                s.Box(xc - 0.025, y, z - 0.025, xc + 0.025, z * StairStep / StairRun + 1.0, z + 0.025, SteelDark, "painted steel");
            }
        }
        return s.Done();
    }

    /// <summary>E: worn. Angle-iron stringers, rusty tread-plate steps whose yellow nosings have chipped away in places (a worn
    /// band, a bare gap, a short new strip), one pipe rail on brackets off the stringer.</summary>
    private static Part StairE()
    {
        var s = new Stair("Style Stair E Worn", "Draft (stair round): angle-iron stringers, worn tread-plate steps with chipped yellow nosings, one pipe rail.");
        const double sw = 0.06;
        foreach (var x0 in new[] { 0.0, StairW - sw })
        {
            s.Stringer(x0, x0 + sw, 0.34, 0.03, Rust, "rusty paint");
        }
        var rnd = new Random(7);
        for (var k = 0; k < StairSteps; k++)
        {
            double top = Stair.Top(k), z0 = Stair.Front(k), z1 = z0 + StairRun;
            s.Box(sw, top - 0.03, z0, StairW - sw, top, z1 + 0.012, Worn, "tread plate");
            // The nosing: two or three yellow patches with bare plate between, the paint worn through where boots land.
            var x = sw + 0.02 + rnd.NextDouble() * 0.1;
            var n = 0;
            while (x < StairW - sw - 0.2 && n < 3)
            {
                var len = 0.25 + rnd.NextDouble() * 0.35;
                var x1 = Math.Min(StairW - sw - 0.02, x + len);
                s.Box(x, top - 0.031, z0 - 0.002, x1, top + 0.004, z0 + 0.055, Yellow, "worn paint");
                x = x1 + 0.08 + rnd.NextDouble() * 0.2;
                n++;
            }
        }
        // A pipe rail on the left, on three posts off the stringer.
        s.Bar(sw / 2, 0.045, 0.1, 0.1 * StairStep / StairRun + 0.95, StairDepth - 0.1, StairRise + 0.95 - 0.1 * StairStep / StairRun, 0.045, Yellow, "worn paint");
        foreach (var z in new[] { 0.1, StairDepth / 2, StairDepth - 0.1 })
        {
            var y = z * StairStep / StairRun;
            s.Box(sw / 2 - 0.02, y + 0.2, z - 0.02, sw / 2 + 0.02, y + 0.95, z + 0.02, Rust, "rusty paint");
        }
        return s.Done();
    }

    private static IEnumerable<Part> Stairs() => [StairA(), StairB(), StairC(), StairHeavy(), StairE()];
}
