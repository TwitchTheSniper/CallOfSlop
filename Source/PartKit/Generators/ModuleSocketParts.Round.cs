using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Round module sockets (module-sockets.md 2.7, Sean 2026-11-21): a Round Module Socket, Plug and Stand of 1, 2 and 3 m across, for
/// round hulls (engine nacelles, pods, drums). The same standard as the square ones (W = L = the diameter, the "module" socket in the
/// footprint's middle, the plug 15 mm over the plate's drawn face, the same pass-through ports pulled in towards the middle), with
/// <c>"round": true</c>: a round plate takes only a round plug of its diameter, at any turn when it is not keyed. Sockets on the back and
/// the top stand only where a part's cell is inside the disc.
/// </summary>
public sealed partial class ModuleSocketParts
{
    private static readonly int[] RoundSizes = [1, 2, 3];

    private const string GasketC = "#24282d";

    private static string RoundSize(int d) => $"{d} m";

    private static int Segs(int d) => d switch { 1 => 24, 2 => 32, _ => 40 };

    private static PyDict Cyl(int segs) => Kw(("shape", "cylinder"), ("segments", segs), ("front", "+y"));

    private static PyDict Tube(int segs, double inner) => Kw(("shape", "tube"), ("segments", segs), ("inner", Py.Round(inner, 4)), ("front", "+y"));

    /// <summary>A disc round (<paramref name="cx"/>, <paramref name="cz"/>), radius <paramref name="r"/>, from y0 up to y1.</summary>
    private static PyDict? Disc(double cx, double cz, double r, double y0, double y1, string colour, int segs, bool detail = false) =>
        Box([cx - r, y0, cz - r], [cx + r, y1, cz + r], colour, detail: detail, shape: Cyl(segs));

    /// <summary>
    /// A disc's solid for collision (a shaped box is drawn only, ConstructParts): three hidden boxes, a square and a cross, whose corners
    /// stay inside the circle.
    /// </summary>
    private static void SolidDisc(Part p, double cx, double cz, double r, double y0, double y1)
    {
        p.Add(Box([cx - 0.7 * r, y0, cz - 0.7 * r], [cx + 0.7 * r, y1, cz + 0.7 * r], hidden: true));
        p.Add(Box([cx - 0.92 * r, y0, cz - 0.38 * r], [cx + 0.92 * r, y1, cz + 0.38 * r], hidden: true));
        p.Add(Box([cx - 0.38 * r, y0, cz - 0.92 * r], [cx + 0.38 * r, y1, cz + 0.92 * r], hidden: true));
    }

    /// <summary>A ring round (<paramref name="cx"/>, <paramref name="cz"/>) from radius <paramref name="r0"/> out to <paramref name="r1"/>.</summary>
    private static PyDict? Ring(double cx, double cz, double r0, double r1, double y0, double y1, string colour, int segs, bool detail = false) =>
        Box([cx - r1, y0, cz - r1], [cx + r1, y1, cz + r1], colour, detail: detail, shape: Tube(segs, r0 / r1));

    /// <summary>Is a point <paramref name="reach"/> m round (x, z) inside a disc of diameter <paramref name="d"/> (the footprint's middle its centre)?</summary>
    private static bool Inside(int d, double x, double z, double reach)
    {
        var c = (d - 1) * 0.5;
        return Math.Sqrt((x - c) * (x - c) + (z - c) * (z - c)) + reach <= d * 0.5 + 1e-9;
    }

    /// <summary>The back's sockets of a round plate: a Medium one at each metre cell whose middle is a quarter metre inside the rim, Small ones on the 0.5 m grid 12 cm inside it (the square plate's names).</summary>
    private static void RoundBackSockets(Part p, int d, double y, string dir)
    {
        for (var i = 0; i < d; i++)
            for (var j = 0; j < d; j++)
            {
                if (Inside(d, i, j, 0.25))
                    p.Sock($"back.{i}.{j}", [i, y, j], dir);
                for (var a = 0; a < 2; a++)
                    for (var b = 0; b < 2; b++)
                        if (Inside(d, i - 0.25 + a * 0.5, j - 0.25 + b * 0.5, 0.12))
                            p.Sock($"b{i}{a}.{j}{b}", [i - 0.25 + a * 0.5, y, j - 0.25 + b * 0.5], dir, Small);
            }
    }

    /// <summary>The pass-through ports of a round size: the square table's places pulled in towards the middle (three quarters), so every one is on the disc.</summary>
    private static List<object?> RoundPorts(int d, double y, string dir, string face)
    {
        var list = PipePorts(d, d, y, dir, face);
        var c = (d - 1) * 0.5;
        foreach (var o in list)
        {
            var port = (PyDict)o!;
            var at = (List<object?>)port["at"]!;
            at[0] = Py.Round(c + (Convert.ToDouble(at[0]) - c) * 0.75, 3);
            at[2] = Py.Round(c + (Convert.ToDouble(at[2]) - c) * 0.75, 3);
        }
        return list;
    }

    /// <summary>Points round a circle: <paramref name="n"/> of them, the first at <paramref name="start"/> degrees from +x towards +z.</summary>
    private static IEnumerable<(double X, double Z)> Circle(double cx, double cz, double r, int n, double start = 0)
    {
        for (var k = 0; k < n; k++)
        {
            var a = (start + 360.0 * k / n) * Math.PI / 180;
            yield return (cx + r * Math.Cos(a), cz + r * Math.Sin(a));
        }
    }

    /// <summary>
    /// A round plate from <paramref name="y0"/> up to its socket at <paramref name="top"/>: a dark base flange, the plate's body, its face (a
    /// hazard ring with four index ticks round a plain middle), the bolt circle, the seal gasket under the plug's rim, the keying notch
    /// (at the -x -z side, as the square plate's corner) and the hidden socket face.
    /// </summary>
    private static void RoundPlate(Part p, int d, double y0, double top)
    {
        var segs = Segs(d);
        double c = (d - 1) * 0.5, r = d * 0.5, face = top - Drawn;
        SolidDisc(p, c, c, r, y0, face);
        p.Add(Disc(c, c, r, y0, y0 + 0.035, LugC, segs, detail: true)); // the base flange: full width, dark
        p.Add(Disc(c, c, r - 0.02, y0 + 0.035, face - 0.016, PlateC, segs, detail: true)); // the plate's body
        // The face: the hazard ring round a plain middle, one slab high (the same segments, so their shared edge meets).
        p.Add(Ring(c, c, r - 0.13, r - 0.024, face - 0.016, face, RimC, segs, detail: true));
        p.Add(Disc(c, c, r - 0.13, face - 0.016, face, PlateC, segs, detail: true));
        // The index ticks across the ring at its four quarters, standing on its face (detail).
        foreach (var (x, z) in Circle(c, c, r - 0.077, 4))
        {
            var alongX = Math.Abs(x - c) > 0.01;
            p.Add(alongX
                ? Box([x - 0.05, face - 0.01, z - 0.018], [x + 0.05, face + 0.004, z + 0.018], LugC, detail: true)
                : Box([x - 0.018, face - 0.01, z - 0.05], [x + 0.018, face + 0.004, z + 0.05], LugC, detail: true));
        }
        // The bolt circle on the ring, between the ticks (6 mm over the face: under the plug, 15 mm over it).
        var bolts = Math.Max(8, (int)Math.Round(2 * Math.PI * (r - 0.077) / 0.35 / 4) * 4);
        foreach (var (x, z) in Circle(c, c, r - 0.077, bolts, 180.0 / bolts))
            p.Add(Disc(x, z, 0.02, face - 0.01, face + 0.006, BoltC, 8, detail: true));
        // The seal gasket the plug's rim lands on, and the centre's locating boss (both under the plug).
        p.Add(Ring(c, c, r - 0.2, r - 0.165, face - 0.01, face + 0.004, GasketC, segs, detail: true));
        p.Add(Ring(c, c, 0.06, 0.1, face - 0.01, face + 0.004, BoltC, 16, detail: true));
        // The keying notch: a dark block on the -x -z side (a keyed socket takes its module with the notches together).
        var k = c - (r - 0.27) * Math.Sqrt(0.5);
        p.Add(Box([k - 0.05, face - 0.01, k - 0.05], [k + 0.05, face + 0.006, k + 0.05], LugC, detail: true));
        p.Add(Box([c - 0.1, top - 0.012, c - 0.1], [c + 0.1, top, c + 0.1], hidden: true)); // (the socket's solid face; never drawn)
        p.Sock("module", [c, top, c], "+y", kind: "module");
    }

    private static Part RoundSocket(int d)
    {
        var p = new Part($"Round Module Socket {RoundSize(d)}", Medium, "Module sockets",
            $"A standard round socket {RoundSize(d)} across, for round hulls (nacelles, pods): a Round Module Plug of its size fits it. F with a module near fits it; F again takes it off.",
            role: "frame", color: PlateC, group: Group);
        RoundPlate(p, d, Back, PlateTop);
        double c = (d - 1) * 0.5, r = d * 0.5;
        var l = c + (r - 0.27) * Math.Sqrt(0.5);
        p.Add(Box([l - 0.03, PlateTop - Drawn - 0.01, c - (l - c) - 0.03], [l + 0.03, PlateTop + 0.003, c - (l - c) + 0.03], LampC, detail: true)); // the lamp (+x -z): the last box
        RoundBackSockets(p, d, Back, "-y");
        p.Sock("bc", [c, Back, c], "-y", Small); // (the back's middle, so a plate goes centred on a Small mount)
        var area = Math.PI / 4 * d * d;
        p.Stats(Math.Round(area * 85), Math.Round(120 + area * Medium.HealthM2), Medium.Armour, seconds: Math.Round(3.0 + area, 1));
        p.Set(("onBreak", "detach"), ("moduleSocket", Kw(("w", d), ("l", d), ("round", true))), ("ports", RoundPorts(d, Back, "in", "-y")));
        return p;
    }

    private static Part RoundPlug(int d)
    {
        var p = new Part($"Round Module Plug {RoundSize(d)}", Medium, "Module sockets",
            $"The base of a round module {RoundSize(d)} across: weld anything onto it (an engine unit, a pod), then fit it to a Round Module Socket of its size. Its lugs are where arms grip it.",
            role: "frame", color: PlugC, group: Group);
        var segs = Segs(d);
        double c = (d - 1) * 0.5, r = d * 0.5 - Inset;
        double y0 = Back + 0.012, y1 = Back + PlugT; // (its underside 12 mm over its socket: 15 mm over the plate's drawn face, clear of its bolts)
        SolidDisc(p, c, c, r, y0, y1);
        p.Add(Disc(c, c, r, y0, y1 - 0.02, PlugC, segs, detail: true)); // the body, its rim a ledge round the top
        p.Add(Disc(c, c, r - 0.06, y1 - 0.02, y1, PlugTopC, segs, detail: true)); // the top, where parts weld
        // The ledge's bolt circle.
        var bolts = Math.Max(8, (int)Math.Round(2 * Math.PI * (r - 0.03) / 0.3 / 4) * 4);
        foreach (var (x, z) in Circle(c, c, r - 0.03, bolts, 180.0 / bolts))
            p.Add(Disc(x, z, 0.013, y1 - 0.02, y1 - 0.008, BoltC, 8, detail: true));
        // The lifting lugs on the ledge at the four quarters (outside the top, so a part as wide as the top stands between them), each with its eye.
        foreach (var (x, z) in Circle(c, c, r - 0.03, 4))
        {
            var alongX = Math.Abs(x - c) > 0.01;
            double hx = alongX ? 0.022 : 0.04, hz = alongX ? 0.04 : 0.022;
            p.Add(Box([x - hx, y1 - 0.02, z - hz], [x + hx, y1 + 0.05, z + hz], LugC));
            p.Add(Box([x - (alongX ? 0.01 : 0.025), y1 + 0.05, z - (alongX ? 0.025 : 0.01)], [x + (alongX ? 0.01 : 0.025), y1 + 0.09, z + (alongX ? 0.025 : 0.01)], BoltC, detail: true));
        }
        // The keying notch on the ledge, matching the plate's (-x -z).
        var k = c - (r - 0.03) * Math.Sqrt(0.5);
        p.Add(Box([k - 0.018, y1 - 0.02, k - 0.018], [k + 0.018, y1 + 0.004, k + 0.018], LugC, detail: true));
        p.Add(Box([c - 0.1, Back, c - 0.1], [c + 0.1, Back + 0.012, c + 0.1], hidden: true)); // (the socket's solid face)
        p.Sock("module", [c, Back, c], "-y", kind: "module");
        for (var i = 0; i < d; i++)
            for (var j = 0; j < d; j++)
            {
                if (Inside(d, i, j, 0.25))
                    p.Sock($"top.{i}.{j}", [i, y1, j], "+y");
                for (var a = 0; a < 2; a++)
                    for (var b = 0; b < 2; b++)
                        if (Inside(d, i - 0.25 + a * 0.5, j - 0.25 + b * 0.5, 0.12))
                            p.Sock($"t{i}{a}.{j}{b}", [i - 0.25 + a * 0.5, y1, j - 0.25 + b * 0.5], "+y", Small);
            }
        for (var i = 0; i < d; i++)
            for (var j = 0; j < d; j++)
                if (Inside(d, i, j, 0.25))
                    p.Sock($"s{i}.{j}", [i, y1, j], "+y", Small);
        if (d % 2 == 0)
            p.Sock("tc", [c, y1, c], "+y", Small); // (the top's middle where no cell's is, so one part stands centred)
        var area = Math.PI / 4 * d * d;
        p.Stats(Math.Round(area * 60), Math.Round(100 + area * Medium.HealthM2), Medium.Armour, seconds: Math.Round(3.0 + area, 1));
        p.Set(("onBreak", "detach"), ("modulePlug", Kw(("w", d), ("l", d), ("round", true))), ("ports", RoundPorts(d, y1, "out", "+y")));
        return p;
    }

    private static Part RoundStand(int d)
    {
        var p = new Part($"Round Module Stand {RoundSize(d)}", Medium, "Module stands",
            $"A round plate {RoundSize(d)} across on a pedestal, 0.9 m up: a round module stands on it, stored (nothing couples), or is built on it as on a bench.",
            role: "frame", color: FrameC, group: Group);
        const double top = 0.42, slab = 0.3;
        var segs = Segs(d);
        double c = (d - 1) * 0.5, r = d * 0.5;
        RoundPlate(p, d, slab, top);
        // The pedestal: a foot disc on the floor, a column and four ribs under the plate.
        var col = 0.1 + 0.06 * d;
        SolidDisc(p, c, c, r - 0.08, Back, Back + 0.05);
        SolidDisc(p, c, c, col, Back + 0.05, slab);
        p.Add(Disc(c, c, r - 0.08, Back, Back + 0.05, FrameC, segs, detail: true));
        p.Add(Disc(c, c, col, Back + 0.05, slab, FrameC, 20, detail: true));
        foreach (var (x, z) in Circle(c, c, (col + r - 0.14) * 0.5, 4))
        {
            var alongX = Math.Abs(x - c) > 0.01;
            double half = (r - 0.14 - col) * 0.5 + 0.02;
            p.Add(alongX
                ? Box([x - half, Back + 0.05, z - 0.02], [x + half, slab - 0.003, z + 0.02], FrameC)
                : Box([x - 0.02, Back + 0.05, z - half], [x + 0.02, slab - 0.003, z + half], FrameC));
        }
        // The anchor bolts round the foot.
        foreach (var (x, z) in Circle(c, c, r - 0.12, 8, 22.5))
            p.Add(Disc(x, z, 0.018, Back + 0.04, Back + 0.065, BoltC, 8, detail: true));
        RoundBackSockets(p, d, Back, "-y"); // (its feet: on a Medium or a Small floor)
        var area = Math.PI / 4 * d * d;
        p.Stats(Math.Round(area * 110), Math.Round(120 + area * Medium.HealthM2), Medium.Armour, seconds: Math.Round(3.0 + area, 1));
        p.Set(("moduleSocket", Kw(("w", d), ("l", d), ("stand", true), ("round", true))));
        return p;
    }

    private static List<(string, Part)> RoundParts()
    {
        var parts = new List<(string, Part)>();
        foreach (var d in RoundSizes)
        {
            parts.Add(("", RoundSocket(d)));
            parts.Add(("", RoundPlug(d)));
            parts.Add(("", RoundStand(d)));
        }
        return parts;
    }
}
