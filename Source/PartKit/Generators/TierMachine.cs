using TB = CallOfSlop.PartKit.Generators.TierStyle.TB;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The machine look on the tiered buildings (docs/sandbox/constructs/look.md, "Machine look"): what <see cref="MachineLook"/> gives the
/// Extruders and the Press, put over each tier's own body from <see cref="TierStyle"/>. A line keeps one set of cues at every tier (an
/// Assembler's stroking tool head over its trays), and a higher tier is built up further (tier by build: guard rails, pipework,
/// cladding, a taller stack cap), its silhouette its own. (The Smelter, the Foundry and the Sawmill went to the block system: SmelterParts, FoundryParts, SawmillParts.)
///
/// Everything here is a look box in metres, added after the screen is mounted and the body's faces are settled, so no solid box, socket,
/// port or screen moves. A box that would cover a port, a socket, a bin, a tray, the screen, the aisle or an Assembler's open front is
/// left out (<see cref="Site.Add"/>): the "Machine tier looks" check's rules, kept by construction.
/// </summary>
public static class TierMachine
{
    private const double Thin = 0.014; // a plate on a face (the game refuses under 1 cm; MachineLook.D wants 12 mm)

    public sealed class Site
    {
        public required string Name;
        public required int Tier;
        public required double Size; // the machine's width in metres: a TierStyle unit
        public required TierStyle.Shape Shape;
        public required List<PyDict> Boxes;
        public required PyDict Plate;
        public PyDict? Lamp;
        public required string Face;
        public required List<(double[] At, string Face, bool In)> Ports;
        public required List<(double[] At, string Dir)> Sockets; // (not the bottom one)
        public required List<(double[] Min, double[] Max)> Rooms; // bins, the input, trays
        public (double[] Min, double[] Max)? Input; // the input's room
        public int Left; // boxes left out

        private double[] _lo = [], _hi = [];
        private List<(double[] Min, double[] Max)> _old = [];

        public double U(double v) => v * Size;

        public double[] U(double x, double y, double z) => [x * Size, y * Size, z * Size];

        public void Start()
        {
            var solid = Boxes.Where(b => !Py.Truthy(b.Get("detail"))).ToList();
            _lo = [.. Enumerable.Range(0, 3).Select(a => solid.Min(b => C(b, "min", a)))];
            _hi = [.. Enumerable.Range(0, 3).Select(a => solid.Max(b => C(b, "max", a)))];
            _old = [.. Boxes.Where(b => Py.Truthy(b.Get("detail")) && !Py.Truthy(b.Get("hidden"))).Select(b => (Min(b), Max(b)))];
        }

        private static bool Over(double[] aMin, double[] aMax, double[] bMin, double[] bMax, double grow = 0) =>
            aMin[0] < bMax[0] + grow && aMax[0] > bMin[0] - grow && aMin[1] < bMax[1] + grow && aMax[1] > bMin[1] - grow && aMin[2] < bMax[2] + grow && aMax[2] > bMin[2] - grow;

        /// <summary>Whether a look box may stand here.</summary>
        public bool Free(double[] mn, double[] mx, bool clearOfDetails = false)
        {
            foreach (var p in Ports)
                if (Over(mn, mx, [p.At[0] - 0.03, p.At[1] - 0.03, p.At[2] - 0.03], [p.At[0] + 0.03, p.At[1] + 0.03, p.At[2] + 0.03]))
                    return false;
            foreach (var (at, dir) in Sockets)
            {
                var a = "xyz".IndexOf(dir[1]);
                var spot = (double[])at.Clone();
                spot[a] += dir[0] == '+' ? 0.03 : -0.03;
                if (Over(mn, mx, [spot[0] - 0.035, spot[1] - 0.035, spot[2] - 0.035], [spot[0] + 0.035, spot[1] + 0.035, spot[2] + 0.035]))
                    return false;
            }
            foreach (var (rMin, rMax) in Rooms)
                if (Over(mn, mx, rMin, rMax, -0.004))
                    return false;
            if (Over(mn, mx, Min(Plate), Max(Plate), 0.01) || Lamp is not null && Over(mn, mx, Min(Lamp), Max(Lamp), 0.01))
                return false;
            var margin = 0.09 * Size;
            for (var a = 0; a < 3; a++)
                if (mn[a] < _lo[a] - margin || mx[a] > _hi[a] + margin)
                    return false;
            if (Over(mn, mx, U(-0.44, 0.05, 0.47), U(0.44, 0.55, 0.6))) // (an Assembler's open front)
                return false;
            if (clearOfDetails && _old.Any(o => Over(mn, mx, o.Min, o.Max, 0.004)))
                return false;
            return true;
        }

        /// <summary>Adds a look box where it may stand; null where it may not.</summary>
        public PyDict? Add(double[] mn, double[] mx, string color, string? finish = null, bool paintable = false, PyDict? shape = null, bool clear = false)
        {
            if (!Free(mn, mx, clear) || MachineLook.D(mn, mx, color, finish, paintable, shape) is not { } b)
            {
                Left++;
                return null;
            }
            Boxes.Add(b);
            return b;
        }

        /// <summary>The built box of a TierStyle box (it may have moved a few millimetres when the faces were settled).</summary>
        public PyDict? Built(TB t, bool detail)
        {
            double[] mn = U(t.Min[0], t.Min[1], t.Min[2]), mx = U(t.Max[0], t.Max[1], t.Max[2]);
            return Boxes.FirstOrDefault(b => Py.Truthy(b.Get("detail")) == detail && Enumerable.Range(0, 3).All(a => Math.Abs(C(b, "min", a) - mn[a]) < 0.03 && Math.Abs(C(b, "max", a) - mx[a]) < 0.03));
        }

        /// <summary>The top of what is drawn over a footprint.</summary>
        public double TopOver(double x0, double x1, double z0, double z1) =>
            Boxes.Where(b => !Py.Truthy(b.Get("hidden")) && C(b, "min", 0) < x1 && C(b, "max", 0) > x0 && C(b, "min", 2) < z1 && C(b, "max", 2) > z0).Max(b => C(b, "max", 1));
    }

    private static double C(PyDict b, string k, int a) => Py.D(((List<object?>)b[k]!)[a]);

    private static double[] Min(PyDict b) => [C(b, "min", 0), C(b, "min", 1), C(b, "min", 2)];

    private static double[] Max(PyDict b) => [C(b, "max", 0), C(b, "max", 1), C(b, "max", 2)];

    private static PyDict Cylinder(string front, int segments = 12) => new PyDict().Update(("shape", "cylinder"), ("front", front), ("segments", segments));

    /// <summary>A built box again, in the hot tone that glows while the machine works (and no longer always lit).</summary>
    private static void Hot(Site s, PyDict b, string color = MachineLook.Hot)
    {
        var o = new PyDict();
        foreach (var (k, v) in b)
            if (k != "glow")
                o[k] = v;
        o["color"] = color;
        o["paintable"] = false;
        o["work"] = "glow";
        s.Boxes[s.Boxes.IndexOf(b)] = o;
    }

    public static void Dress(Site s)
    {
        s.Start();
        // Three tones under any paint: what TierStyle draws dark (stacks, housings) or as bare metal (blades, bits, pipes) keeps its tone.
        foreach (var tb in s.Shape.Solid.Concat(s.Shape.Detail).Where(b => b.Role is "dark" or "edge" or "pipe"))
            foreach (var detail in new[] { false, true })
                if (s.Built(tb, detail) is { } own && !own.Has("paintable"))
                    own["paintable"] = false;
        var before = s.Boxes.Count;
        MachineLook.Buttons(s.Boxes, s.Plate, s.Face);
        // (a button that would cover a socket or a port goes)
        for (var i = s.Boxes.Count - 1; i >= before; i--)
            if (!s.Free(Min(s.Boxes[i]), Max(s.Boxes[i])))
                s.Boxes.RemoveAt(i);
        foreach (var p in s.Ports)
            Port(s, p.At, p.Face, p.In);
        switch (s.Name)
        {
            default:
                Assembler(s);
                break;
        }
    }

    // ---- Flow: the in side white, the out side orange, an arrow the way the work goes (back to front), hazard paint at the port

    private static void Port(Site s, double[] at, string face, bool input)
    {
        var n = "xyz".IndexOf(face[1]);
        var sg = face[0] == '+' ? 1 : -1;
        var along = 2 - n; // (the other level axis: x for a z face)
        // The solid face the port is on.
        var host = s.Boxes.Where(b => !Py.Truthy(b.Get("detail")) && Math.Abs((sg > 0 ? C(b, "max", n) : C(b, "min", n)) - at[n]) < 0.035
            && C(b, "min", 1) <= at[1] + 0.01 && C(b, "max", 1) >= at[1] - 0.01 && C(b, "min", along) <= at[along] + 0.01 && C(b, "max", along) >= at[along] - 0.01)
            .OrderByDescending(b => (C(b, "max", 1) - C(b, "min", 1)) * (C(b, "max", along) - C(b, "min", along))).FirstOrDefault();
        if (host is null || Py.Truthy(host.Get("hidden")))
            Fitting(s, at, n, sg, along, input); // (nothing drawn at the port: a hidden wall under a tier 5's shapes counts as nothing)
        if (host is null)
            return;
        var wall = sg > 0 ? C(host, "max", n) : C(host, "min", n);
        double a0 = C(host, "min", along), a1 = C(host, "max", along), y0 = C(host, "min", 1), y1 = C(host, "max", 1);
        var high = Math.Clamp(s.Size * 0.03, 0.09, 0.2); // the band's height
        var half = Math.Min(Math.Clamp(s.Size * 0.09, 0.22, 0.6), (a1 - a0) / 2 - 0.01);
        var mid = Math.Clamp(at[along], a0 + half + 0.005, a1 - half - 0.005);
        var gap = 0.07;
        var above = y1 - at[1] >= gap + high + 0.01;
        if (!above && at[1] - y0 < gap + high + 0.01)
            return;
        var (b0, b1) = above ? (at[1] + gap, at[1] + gap + high) : (at[1] - gap - high, at[1] - gap);
        double[] Box3(double u0, double u1, double v0, double v1, double d0, double d1)
        {
            var mn = new double[3];
            (mn[along], mn[1], mn[n]) = (u0, v0, sg > 0 ? wall + d0 : wall - d1);
            return mn;
        }
        double[] Box3x(double u0, double u1, double v0, double v1, double d0, double d1)
        {
            var mx = new double[3];
            (mx[along], mx[1], mx[n]) = (u1, v1, sg > 0 ? wall + d1 : wall - d0);
            return mx;
        }
        void Flat(double u0, double u1, double v0, double v1, double proud, string color, string? finish = null, double from = -0.004) =>
            s.Add(Box3(u0, u1, v0, v1, from, proud), Box3x(u0, u1, v0, v1, from, proud), color, finish);
        Flat(mid - half, mid + half, b0, b1, Thin, input ? MachineLook.In : MachineLook.Out);
        // The arrow: along the machine, back to front (+z on an x face).
        var dirn = along == 2 ? 1 : -1;
        double my = (b0 + b1) / 2, len = half * 1.2, w = high * 0.7;
        void Bar(double f0, double f1, double wide)
        {
            var (u0, u1) = dirn > 0 ? (mid + f0 * len, mid + f1 * len) : (mid - f1 * len, mid - f0 * len);
            Flat(u0, u1, my - w * wide / 2, my + w * wide / 2, Thin + 0.008, MachineLook.Ink, from: 0.004); // (it stands in the band, its back inside it)
        }
        Bar(-0.5, 0.1, 0.34);
        Bar(0.1, 0.3, 1);
        Bar(0.3, 0.5, 0.5);
        // Hazard paint on the port's other side.
        var (h0, h1) = above ? (at[1] - gap - high * 0.6, at[1] - gap) : (at[1] + gap, at[1] + gap + high * 0.6);
        if (h0 >= y0 + 0.005 && h1 <= y1 - 0.005 && h1 - h0 >= 0.03)
            Flat(mid - half, mid + half, h0, h1, Thin, MachineLook.Accent, "hazard paint");
    }

    /// <summary>
    /// A port with no solid face behind it (an Assembler's Chute out beside its open trays, a tier 5 wall drawn by shapes) gets
    /// the shared spout (<see cref="MachineLook.Mouth"/>): back to what is drawn behind it when that is near, else on a
    /// stand from the floor. A port already on something drawn gets nothing.
    /// </summary>
    private static void Fitting(Site s, double[] at, int n, int sg, int along, bool input)
    {
        var back = double.PositiveInfinity;
        foreach (var b in s.Boxes)
        {
            if (Py.Truthy(b.Get("hidden")) || C(b, "min", 1) > at[1] || C(b, "max", 1) < at[1] || C(b, "min", along) > at[along] || C(b, "max", along) < at[along])
                continue;
            var gap = (at[n] - (sg > 0 ? C(b, "max", n) : C(b, "min", n))) * sg;
            if (gap > -0.001)
                back = Math.Min(back, Math.Max(gap, 0));
        }
        if (back <= 0.03)
            return;
        var half = Math.Clamp(s.Size * 0.035, 0.07, 0.2);
        var stood = back > 0.3;
        var depth = stood ? Math.Clamp(s.Size * 0.035, 0.07, 0.2) : back + 0.004;
        s.Boxes.AddRange(MachineLook.Mouth(at, n, sg, half, depth, input));
        if (!stood)
            return;
        // The stand: from the floor under the spout up into it.
        double[] mn = new double[3], mx = new double[3];
        (mn[n], mx[n]) = sg > 0 ? (at[n] - depth + 0.004, at[n] - 0.03) : (at[n] + 0.03, at[n] + depth - 0.004);
        (mn[along], mx[along]) = (at[along] - half * 0.5, at[along] + half * 0.5);
        var under = at[1] - half;
        var floor = double.NegativeInfinity;
        foreach (var b in s.Boxes)
            if (!Py.Truthy(b.Get("hidden")) && C(b, "max", 1) < under - 0.02 && C(b, "min", 0) <= mn[0] && C(b, "max", 0) >= mx[0] && C(b, "min", 2) <= mn[2] && C(b, "max", 2) >= mx[2])
                floor = Math.Max(floor, C(b, "max", 1));
        if (double.IsNegativeInfinity(floor))
            return;
        (mn[1], mx[1]) = (floor - 0.004, under + 0.003);
        if (MachineLook.D(mn, mx, MachineLook.Base) is { } stand)
            s.Boxes.Add(stand);
    }

    // ---- The Assembler family: a gantry over a bed, a tool head that strokes, trays with an orange lip; the hopper's rim white

    private static void Assembler(Site s)
    {
        var t = s.Tier;
        // The tool head: TierStyle's tip, a steel bit under an accent collar, stroking down to the tray while it builds.
        var tip = s.Shape.Detail.FirstOrDefault(b => b.Role == "edge" && Math.Abs(b.Min[0] + b.Max[0]) < 0.01 && Math.Abs(b.Min[2] + b.Max[2]) < 0.01 && b.Max[0] - b.Min[0] < 0.06);
        var travel = s.U(0.07);
        if (tip is not null && s.Built(tip, true) is { } bit)
        {
            var o = new PyDict(bit);
            o["color"] = MachineLook.Steel;
            o["paintable"] = false;
            o["finish"] = "steel";
            MachineLook.Work(o, "stroke", "-y", travel);
            s.Boxes[s.Boxes.IndexOf(bit)] = o;
            var y = C(bit, "max", 1);
            MachineLook.Work(s.Add([s.U(-0.045), y - s.U(0.014), s.U(-0.045)], [s.U(0.045), y + s.U(0.006), s.U(0.045)], MachineLook.Accent), "stroke", "-y", travel);
            var low = C(bit, "min", 1); // (a steel platen on the bit, over the trays)
            MachineLook.Work(s.Add([s.U(-0.1), low + s.U(0.006), s.U(-0.035)], [s.U(0.1), low + s.U(0.022), s.U(0.1)], MachineLook.Steel, "steel"), "stroke", "-y", travel);
        }
        // Three tones under paint: dark shoes on the posts, an accent band round the head's motor.
        foreach (var post in s.Shape.Solid.Where(b => b.Min[1] <= 0.031 && b.Max[1] - b.Min[1] > 0.5 && b.Max[0] - b.Min[0] < 0.1 && b.Max[2] - b.Min[2] < 0.1))
            s.Add([s.U(post.Min[0]) - Thin, s.U(0.03) - 0.004, s.U(post.Min[2]) - Thin], [s.U(post.Max[0]) + Thin, s.U(0.1), s.U(post.Max[2]) + Thin], MachineLook.Base);
        if (s.Shape.Solid.FirstOrDefault(b => b.Role == "motor") is { } motor)
        {
            var my = s.U(motor.Min[1] + (motor.Max[1] - motor.Min[1]) * 0.2);
            s.Add([s.U(motor.Min[0]) - Thin, my, s.U(motor.Min[2]) - Thin], [s.U(motor.Max[0]) + Thin, my + s.U(0.03), s.U(motor.Max[2]) + Thin], MachineLook.Accent);
        }
        // The family's own cue: a Thruster Assembler's nozzle glows on its stand, a Tailor's bolt of cloth turns.
        if (t < 5 && s.Name == "Thruster Assembler" && s.Shape.Detail.LastOrDefault(b => b.Role == "coil" && Py.Truthy(b.Keys.Get("shape"))) is { } nozzle && s.Built(nozzle, true) is { } nz)
            Hot(s, nz);
        if (s.Name == "Tailor" && s.Shape.Detail.LastOrDefault(b => b.Role == "stripe" && (string?)b.Keys.Get("shape") == "cylinder") is { } bolt && s.Built(bolt, true) is { } bl)
        {
            var o = new PyDict(bl);
            MachineLook.Work(o, "spin", "+x");
            s.Boxes[s.Boxes.IndexOf(bl)] = o;
        }
        // The hopper at the back: a white rim along its back and front walls.
        var proud = Math.Max(Thin, s.U(0.006));
        s.Add([s.U(-0.455), s.U(0.3), s.U(-0.46) - proud], [s.U(0.455), s.U(0.343), s.U(-0.46) + 0.003], MachineLook.In, clear: true);
        s.Add([s.U(-0.455), s.U(0.3), s.U(-0.04) - 0.003], [s.U(0.455), s.U(0.343), s.U(-0.04) + proud], MachineLook.In, clear: true);
        // The trays: a dark divider between them, an orange lip at the open front, hazard paint on the floor's front edge.
        double fl = s.U(0.03);
        s.Add([s.U(-0.008), fl - 0.004, s.U(0.02)], [s.U(0.008), s.U(0.12), s.U(0.465)], MachineLook.Base);
        s.Add([s.U(-0.455), fl - 0.004, s.U(0.483)], [s.U(0.455), s.U(0.047), s.U(0.497)], MachineLook.Out);
        s.Add([s.U(-0.5) + 0.004, fl - 0.004, s.U(-0.035)], [s.U(0.5) - 0.004, fl + 0.01, s.U(0.012)], MachineLook.Accent, "hazard paint");
        // Tier by build: guard strips on the front posts from tier 2, a cable run along a side beam from tier 3, kick cladding at the back from tier 4.
        if (t >= 2)
            foreach (var x in new[] { -0.5, 0.46 })
                s.Add([s.U(x) - 0.004, s.U(0.36), s.U(0.5) - 0.004], [s.U(x + 0.04) + 0.004, s.U(0.6), s.U(0.5) + Thin], MachineLook.Accent, "hazard paint", clear: true);
        if (t >= 3)
            s.Add([s.U(-0.5) - Thin, s.U(0.19), s.U(-0.4)], [s.U(-0.5) + 0.004, s.U(0.23), s.U(-0.1)], MachineLook.Steel, "steel", clear: true);
        if (t >= 4)
            s.Add([s.U(-0.4), s.U(0.04), s.U(-0.46) - Thin], [s.U(0.4), s.U(0.14), s.U(-0.46) + 0.004], MachineLook.Base, "tread plate", clear: true);
    }
}
