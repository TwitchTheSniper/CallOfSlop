using TB = CallOfSlop.PartKit.Generators.TierStyle.TB;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The machine look on the tiered buildings (docs/sandbox/constructs/look.md, "Machine look"): what <see cref="MachineLook"/> gives the
/// Extruders and the Press, put over each tier's own body from <see cref="TierStyle"/>. A line keeps one set of cues at every tier (a
/// Smelter's charge door, tap hole and smoking stack; a Sawmill's carriage on rails through a turning blade; a Foundry's crucible over
/// moulds; an Assembler's stroking tool head over its trays), and a higher tier is built up further (tier by build: guard rails, pipework,
/// cladding, a taller stack cap), its silhouette its own.
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
            if (Name is "Smelter" or "Foundry") // (the aisle across the machine stays walkable)
            {
                double r = 0.41, feet = 0.03 * Size;
                if (Over(mn, mx, [-0.3 * Size - r, feet, 0.1 * Size - r], [0.3 * Size + r, feet + 1.86, 0.1 * Size + r]))
                    return false;
            }
            else if (Name is not "Sawmill" && Over(mn, mx, U(-0.44, 0.05, 0.47), U(0.44, 0.55, 0.6))) // (an Assembler's open front)
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
            case "Smelter":
                Furnace(s, false);
                break;
            case "Foundry":
                Furnace(s, true);
                break;
            case "Sawmill":
                Sawmill(s);
                break;
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

    /// <summary>A dark kick band round a footprint, a little proud.</summary>
    private static void Kick(Site s, double x0, double x1, double z0, double z1, double y0, double y1)
    {
        s.Add([x0 - Thin, y0, z0 - Thin], [x1 + Thin, y1, z0 + 0.002], MachineLook.Base);
        s.Add([x0 - Thin, y0, z1 - 0.002], [x1 + Thin, y1, z1 + Thin], MachineLook.Base);
        s.Add([x0 - Thin, y0, z0 + 0.002], [x0 + 0.002, y1, z1 - 0.002], MachineLook.Base);
        s.Add([x1 - 0.002, y0, z0 + 0.002], [x1 + Thin, y1, z1 - 0.002], MachineLook.Base);
    }

    /// <summary>A band round the outside of a walled room, at its top (a hopper's white rim).</summary>
    private static void Rim(Site s, double x0, double x1, double z0, double z1, double y0, double y1, string color, double proud)
    {
        s.Add([x0 - proud, y0, z0 - proud], [x1 + proud, y1, z0 + 0.003], color);
        s.Add([x0 - proud, y0, z1 - 0.003], [x1 + proud, y1, z1 + proud], color);
        s.Add([x0 - proud, y0, z0 + 0.003], [x0 + 0.003, y1, z1 - 0.003], color);
        s.Add([x1 - 0.003, y0, z0 + 0.003], [x1 + proud, y1, z1 - 0.003], color);
    }

    /// <summary>The solid stacks of a furnace: slim, tall, standing above its hopper's walls.</summary>
    private static List<TB> Stacks(Site s, double wallTop) =>
        [.. s.Shape.Solid.Where(b => !b.Hidden && b.Max[0] - b.Min[0] < 0.24 && b.Max[2] - b.Min[2] < 0.24 && b.Max[1] - b.Min[1] > 0.1 && b.Max[1] > wallTop + 0.01)];

    // ---- The Smelter and the Foundry: a vessel that is charged at the top, burns, and is tapped at the front across the aisle

    private static void Furnace(Site s, bool foundry)
    {
        var t = s.Tier;
        var (inMin, inMax) = s.Input!.Value;
        // The hopper: a white rim (the charge goes in here), and from tier 2 a guard rail round it.
        double hx0 = inMin[0] - s.U(0.045), hx1 = inMax[0] + s.U(0.045), hz0 = inMin[2] - s.U(0.045), hz1 = inMax[2] + s.U(0.045), hTop = inMax[1] + s.U(0.02);
        var proud = Math.Max(Thin, s.U(0.004));
        Rim(s, hx0, hx1, hz0, hz1, hTop - s.U(0.035), hTop + 0.004, MachineLook.In, proud);
        if (t >= 2)
        {
            double post = Math.Max(0.03, s.U(0.008)), railY = hTop + s.U(0.07);
            foreach (var x in new[] { hx0 - proud, hx1 + proud - post })
                foreach (var z in new[] { hz0 - proud, hz1 + proud - post })
                    s.Add([x, hTop + 0.006, z], [x + post, railY, z + post], MachineLook.Base, clear: true);
            s.Add([hx0 - proud + post, railY - post, hz0 - proud + 0.004], [hx1 + proud - post, railY, hz0 - proud + post - 0.004], MachineLook.Accent, clear: true);
            s.Add([hx0 - proud + post, railY - post, hz1 + proud - post + 0.004], [hx1 + proud - post, railY, hz1 + proud - 0.004], MachineLook.Accent, clear: true);
        }
        if (t >= 3) // pipework: a main along the hopper's back with a valve wheel
        {
            double r = s.U(0.016), py = hTop - s.U(0.11);
            s.Add([hx0, py - r, hz0 - proud - 2 * r], [hx1, py + r, hz0 - proud], MachineLook.Steel, "steel", shape: Cylinder("+x"), clear: true);
            var mx = (hx0 + hx1) / 2;
            s.Add([mx - 2.2 * r, py - 2.2 * r, hz0 - proud - 2 * r - 0.03], [mx + 2.2 * r, py + 2.2 * r, hz0 - proud - 2 * r + 0.004], MachineLook.Accent, shape: Cylinder("-z"), clear: true);
        }

        // The stacks: steel collars, a cap and a flue that smokes while the furnace works. A higher tier's cap stands taller.
        foreach (var k in Stacks(s, hTop / s.Size))
        {
            double x0 = s.U(k.Min[0]), x1 = s.U(k.Max[0]), z0 = s.U(k.Min[2]), z1 = s.U(k.Max[2]), y0 = Math.Max(s.U(k.Min[1]), hTop), y1 = s.U(k.Max[1]);
            var c = Math.Max(Thin, s.U(0.005));
            var collars = t >= 5 ? 3 : t >= 2 ? 2 : 1;
            for (var i = 0; i < collars; i++)
            {
                var y = y0 + (y1 - y0) * (0.82 - 0.27 * i);
                s.Add([x0 - c, y, z0 - c], [x1 + c, y + s.U(0.022), z1 + c], MachineLook.Steel, "steel");
            }
            var top = s.TopOver(x0, x1, z0, z1);
            if (top < y1 + 0.02) // (nothing sits on it yet: a cap, taller by the tier)
            {
                var capH = s.U(0.012 + 0.006 * t);
                s.Add([x0 - c * 1.6, y1 + 0.003, z0 - c * 1.6], [x1 + c * 1.6, y1 + 0.003 + capH, z1 + c * 1.6], MachineLook.Base);
                top = y1 + 0.003 + capH;
            }
            double fx = (x1 - x0) * 0.06, fz = (z1 - z0) * 0.06; // (the puffs are as wide as their box)
            var flue = s.Add([x0 + fx, top - 0.004, z0 + fz], [x1 - fx, top + Math.Max(0.02, s.U(0.008)), z1 - fz], MachineLook.Ink);
            MachineLook.Work(flue, "smoke");
        }

        // The mouth across the aisle.
        var fl0 = s.U(0.02);
        var mouth = s.Shape.Detail.FirstOrDefault(b => b.Role == "heat");
        if (mouth is not null && s.Built(mouth, true) is { } m)
        {
            double mx0 = C(m, "min", 0), mx1 = C(m, "max", 0), my0 = C(m, "min", 1), my1 = C(m, "max", 1), mz = C(m, "max", 2), mw = mx1 - mx0, mh = my1 - my0, cx = (mx0 + mx1) / 2;
            if (!foundry)
            {
                // A Smelter: the mouth is a dark charge door with two glowing slots and a handle; under it the tap hole glows over a steel spout.
                var door = new PyDict();
                foreach (var (key, v) in m)
                    if (key != "glow")
                        door[key] = v;
                door["color"] = MachineLook.Base;
                door["paintable"] = false;
                s.Boxes[s.Boxes.IndexOf(m)] = door;
                foreach (var f in new[] { 0.3, 0.7 })
                    MachineLook.Work(s.Add([mx0 + mw * (f - 0.14), my0 + mh * 0.62, mz - 0.004], [mx0 + mw * (f + 0.14), my0 + mh * 0.8, mz + Thin], MachineLook.Hot), "glow");
                s.Add([mx1 - mw * 0.09, my0 + mh * 0.3, mz - 0.004], [mx1 - mw * 0.04, my0 + mh * 0.7, mz + 0.03], MachineLook.Accent);
                MachineLook.Work(s.Add([cx - mw * 0.16, my0 + mh * 0.1, mz - 0.004], [cx + mw * 0.16, my0 + mh * 0.46, mz + Thin + 0.004], MachineLook.Hot), "glow");
                s.Add([cx - mw * 0.2, my0 + mh * 0.02, mz - 0.004], [cx + mw * 0.2, my0 + mh * 0.1 - 0.004, mz + 0.06], MachineLook.Steel, "steel");
                if (t >= 2) // a steel frame round the door
                {
                    var f = Math.Max(0.03, mw * 0.03);
                    s.Add([mx0 - f, my0, mz - 0.006], [mx0 + 0.004, my1 + f, mz + 0.02], MachineLook.Steel, "steel", clear: true);
                    s.Add([mx1 - 0.004, my0, mz - 0.006], [mx1 + f, my1 + f, mz + 0.02], MachineLook.Steel, "steel", clear: true);
                }
            }
            else
            {
                // A Foundry: the mouth glows; a steel crucible hangs in front of it with its melt showing, and pours into a row of moulds.
                Hot(s, m);
                var room = (0.1 * s.Size - 0.41 - 0.03 - (mz + 0.01)) / 2; // (it stands out of the recess, short of the aisle)
                double r = Math.Min(Math.Min(mw * 0.3, s.U(0.042)), room), cy = my0 + mh * 0.5, cz = mz + 0.01 + r;
                s.Add([cx - r, cy, cz - r], [cx + r, cy + 2 * r, cz + r], MachineLook.Steel, "steel", shape: Cylinder("+y"));
                MachineLook.Work(s.Add([cx - r * 0.7, cy + 2 * r - 0.006, cz - r * 0.7], [cx + r * 0.7, cy + 2 * r + 0.01, cz + r * 0.7], MachineLook.Hot, shape: Cylinder("+y")), "glow");
                s.Add([cx - r * 1.5, cy + r * 0.8, cz - 0.02], [cx + r * 1.5, cy + r * 1.2, cz + 0.02], MachineLook.Accent); // (its trunnion bar)
                MachineLook.Work(s.Add([cx - 0.02, fl0 + s.U(0.043), cz + r * 0.5], [cx + 0.02, cy + 0.004, cz + r * 0.5 + 0.03], MachineLook.Hot), "glow"); // (the pour)
                var mould = mw * 0.3;
                for (var i = -1; i <= 1; i++)
                {
                    var x = cx + i * mw * 0.34;
                    s.Add([x - mould / 2, fl0 - 0.004, mz + 0.006], [x + mould / 2, fl0 + s.U(0.045), cz + r * 0.9], MachineLook.Steel, "steel");
                    MachineLook.Work(s.Add([x - mould * 0.32, fl0 + s.U(0.045) - 0.008, mz + 0.006 + mould * 0.15], [x + mould * 0.32, fl0 + s.U(0.045) + 0.006, cz + r * 0.9 - mould * 0.15], MachineLook.Hot), "glow");
                }
            }
        }

        // The aisle: tread plate to walk on, hazard paint where a player steps in; the tray's rim says out. Cladding on the tray from tier 4.
        double fl = s.U(0.02), tx = s.U(foundry ? 0.38 : 0.36);
        s.Add([s.U(-0.44), fl - 0.004, s.U(-0.03)], [s.U(0.44), fl + 0.01, s.U(0.25)], MachineLook.Steel, "tread plate");
        s.Add([s.U(-0.497), fl - 0.004, s.U(-0.03)], [s.U(-0.44) - 0.003, fl + 0.012, s.U(0.25)], MachineLook.Accent, "hazard paint");
        s.Add([s.U(0.44) + 0.003, fl - 0.004, s.U(-0.03)], [s.U(0.497), fl + 0.012, s.U(0.25)], MachineLook.Accent, "hazard paint");
        s.Add([-tx, s.U(0.2), s.U(0.5) - 0.004], [tx, s.U(0.232), s.U(0.5) + Thin], MachineLook.Out, clear: true);
        if (t >= 4)
            s.Add([-tx, s.U(0.03), s.U(0.5) - 0.004], [tx, s.U(0.1), s.U(0.5) + Thin], MachineLook.Base, "tread plate", clear: true);
    }

    // ---- The Sawmill: a log carriage on rails over the in bed, through a blade that turns, planks over the out bed

    private static void Sawmill(Site s)
    {
        var t = s.Tier;
        // The blade turns (the plates TierStyle draws across the frame).
        foreach (var b in s.Shape.Detail.Where(b => b.Role is "edge" or "stripe" && b.Max[0] - b.Min[0] < 0.05 && b.Max[1] - b.Min[1] > 0.3 && Math.Abs(b.Min[0] + b.Max[0]) < 0.01))
            if (s.Built(b, true) is { } blade)
            {
                var o = new PyDict(blade);
                if (o.Has("color") && !o.Has("glow"))
                {
                    o["color"] = MachineLook.Steel;
                    o["paintable"] = false;
                    o["finish"] = "steel";
                }
                MachineLook.Work(o, "spin", "+x");
                s.Boxes[s.Boxes.IndexOf(blade)] = o;
            }
        var bedTop = s.Shape.Solid.Where(b => b.Min[0] <= -0.49 && b.Max[0] >= 0.49 && b.Min[2] <= -0.49).Max(b => b.Max[1]);
        // Rails from the in bed to the out bed, either side of the blade, on sleepers where the beds are low.
        double ry0 = s.U(Math.Max(bedTop, 0.44) + 0.004), ry1 = ry0 + s.U(0.026);
        foreach (var x in new[] { -0.105, 0.075 })
            s.Add([s.U(x), ry0, s.U(-0.492)], [s.U(x + 0.03), ry1, s.U(0.492)], MachineLook.Steel, "steel");
        if (ry0 - s.U(bedTop) > 0.014)
            foreach (var (z0, z1) in new[] { (-0.497, -0.443), (-0.157, -0.103), (0.103, 0.157), (0.443, 0.497) })
                s.Add([s.U(-0.13), s.U(bedTop) - 0.004, s.U(z0)], [s.U(0.13), ry0 - 0.003, s.U(z1)], MachineLook.Base);
        // The carriage and its log: they feed towards the blade while the mill works.
        double cy = ry1 + 0.004, feed = s.U(0.1);
        MachineLook.Work(s.Add([s.U(-0.13), cy, s.U(-0.43)], [s.U(0.13), cy + s.U(0.02), s.U(-0.19)], MachineLook.Accent), "stroke", "+z", feed);
        MachineLook.Work(s.Add([s.U(-0.05), cy + s.U(0.02) + 0.003, s.U(-0.46)], [s.U(0.05), cy + s.U(0.12) + 0.003, s.U(-0.16)], "#8a6a40", "wood", shape: Cylinder("+z", 10)), "stroke", "+z", feed);
        // Planks coming off, over the out bed.
        var planks = t >= 3 ? 3 : 2;
        for (var i = 0; i < planks; i++)
            s.Add([s.U(-0.12 + 0.012 * i), cy + s.U(0.022 * i) + 0.002 * i, s.U(0.3 + 0.008 * i)], [s.U(0.12 - 0.012 * i), cy + s.U(0.022 * i + 0.018) + 0.002 * i, s.U(0.47 - 0.008 * i)], "#b89460", "wood");
        // A dark kick band round the beds; hazard paint on the bed edges by the blade from tier 2; steel cladding on the long sides from tier 4.
        Kick(s, s.U(-0.5), s.U(0.5), s.U(-0.5), s.U(0.5), s.U(0.02) + 0.004, s.U(0.065));
        if (t >= 2)
            foreach (var (z0, z1) in new[] { (-0.157, -0.103), (0.103, 0.157) })
                foreach (var (x0, x1) in new[] { (-0.44, -0.2), (0.2, 0.44) })
                    s.Add([s.U(x0), s.U(bedTop) - 0.004, s.U(z0)], [s.U(x1), s.U(bedTop) + 0.012, s.U(z1)], MachineLook.Accent, "hazard paint", clear: true);
        if (t >= 4)
            foreach (var z in new[] { -0.5, 0.5 })
                s.Add([s.U(-0.4), s.U(0.12), s.U(z) - (z < 0 ? Thin : 0.004)], [s.U(0.4), s.U(0.2), s.U(z) + (z < 0 ? 0.004 : Thin)], MachineLook.Steel, "steel", clear: true);
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
