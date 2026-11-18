using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.Generators.StationLook;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Space stations, built procedurally from the station kit (SpaceStationKit.cs, the look in SpaceStationLook.cs; notes in
/// docs/sandbox/constructs/station.md). A station is a plan: a list of module calls up a vertical spine, each with a few numbers (a
/// block's half-size and heights, an arm's direction and reach, the ship each berth takes). The kit turns each call into welded pieces in
/// the station's frame; then every touching pair of pieces gets a socket pair (<see cref="Connect"/>), the pieces are placed out from
/// the first, and the docking clamps and hatches are hung on their sockets.
///
/// The plan is Caledon Highport, third pass (Karl, 2026-11-18: a ring of habitat segments, one hexagonal hub, a docking spindle; his
/// reference picture is the project's station/reference-ring-station.png): <see cref="RingStation"/> in SpaceStationRing.cs. The first
/// two passes (a 310 m tower, then a hex commons of seven towers) are on PR 301's history.
/// </summary>
public sealed partial class SpaceStationParts : IPartGenerator
{
    public string Name => "space_station";

    private const string Out = "Constructs/Parts/SpaceStations";
    private const string BpOut = "Constructs/Blueprints";
    public const string Group = "Station";

    /// <summary>The ships the berths are sized for (metres across, long, high: measured from their blueprints, 2026-11-18).</summary>
    private static readonly (double W, double L, double H) ZeroDay = (17.1, 53.3, 14.7), Magpie = (18.3, 34.9, 11.1), Dray = (14.7, 61.3, 11.7);

    // ---- Caledon Highport

    public const string HighportName = "Caledon Highport";

    // ---- the base's pieces

    private static void Thrusters(Kit k, string name, double h, double y0, double y1)
    {
        var p = k.Piece(name, "The thrust block under the reactor: four station-keeping drives that hold the station in its orbit.", Frame);
        Solid(p, -h, y0, -h, h, y1, h, Plate2, Plated);
        foreach (var (sx, sz) in Corners)
        {
            double x = sx * 6, z = sz * 6;
            Cyl(p, 'y', x, 0, z, 1.7, y0 - 2.5, y0, Frame, Soot);
            Shaped(p, x - 3, y0 - 10, z - 3, x + 3, y0 - 2.5, z + 3, Truss, Soot, "tube", "+y", ("inner", 0.86), ("ends", "open"));
            Lamp(p, x - 1.2, y0 - 9.2, z - 1.2, x + 1.2, y0 - 8.9, z + 1.2, Glow);
            Detail(p, x - 3.1, y0 - 3.2, z - 3.1, x + 3.1, y0 - 2.6, z + 3.1, Hazard, HazardFin);
        }
        foreach (var (sx, sz) in Corners)
            Lamp(p, sx * (h - 0.4) - 0.2, y1 - 0.4, sz * (h + 0.01) - (sz < 0 ? 0.1 : 0), sx * (h - 0.4) + 0.2, y1 - 0.1, sz * (h + 0.01) + (sz < 0 ? 0 : 0.1), sx < 0 ? NavRed : NavGreen);
        Finish(p);
    }

    private static void Reactor(Kit k, string name, double h, double y0, double y1, double plugH, double plugTop)
    {
        var p = k.Piece(name, "The reactor, sealed under a shield plug below engineering: coolant risers up its faces, hazard bands, its vents glowing.", Frame);
        Solid(p, -h, y0, -h, h, y1, h, "#5a4a44", Painted);
        Solid(p, -plugH, y1, -plugH, plugH, plugTop, plugH, Plate2, Plated);
        foreach (var s in new[] { -1, 1 })
        {
            for (var u = -h + 2; u < h - 1; u += 4)
            {
                Cyl(p, 'y', u, 0, s * (h + 0.5), 0.45, y0 + 0.5, y1 - 0.2, Tank, Rusty);
                Cyl(p, 'y', s * (h + 0.5), 0, u, 0.45, y0 + 0.5, y1 - 0.2, Tank, Rusty);
            }
            Detail(p, -h, y0 + 7, s < 0 ? -h - 0.03 : h, h, y0 + 8.2, s < 0 ? -h : h + 0.03, Hazard, HazardFin);
            Detail(p, s < 0 ? -h - 0.03 : h, y0 + 7, -h, s < 0 ? -h : h + 0.03, y0 + 8.2, h, Hazard, HazardFin);
            Detail(p, -h + 1.5, y0 + 2, s < 0 ? -h - 0.025 : h, h - 1.5, y0 + 2.6, s < 0 ? -h : h + 0.025, Glow, glow: true);
            Detail(p, s < 0 ? -h - 0.025 : h, y0 + 2, -h + 1.5, s < 0 ? -h : h + 0.025, y0 + 2.6, h - 1.5, Glow, glow: true);
        }
        Detail(p, -plugH, plugTop - 0.5, -plugH - 0.03, plugH, plugTop - 0.1, -plugH, Hazard, HazardFin);
        Detail(p, -plugH, plugTop - 0.5, plugH, plugH, plugTop - 0.1, plugH + 0.03, Hazard, HazardFin);
        Finish(p, 800);
    }

    // ---- welding the pieces together

    private static double[] Vd(PyDict b, string key) => [.. ((List<object?>)b[key]!).Select(v => Convert.ToDouble(v) + H)];

    private static bool Drawn(PyDict b) => b.Get("detail") is true;

    /// <summary>A facing socket pair wherever two pieces' solid boxes touch face to face (one pair a pair of pieces, on their biggest
    /// contact); throws where two pieces' solids overlap, which the game would refuse.</summary>
    private static void Connect(List<Part> parts)
    {
        var solid = parts.Select(q => q.Boxes.Where(b => !Drawn(b)).Select(b => (Mn: Vd(b, "min"), Mx: Vd(b, "max"))).ToList()).ToList();
        var bounds = solid.Select(l => (Mn: Enumerable.Range(0, 3).Select(a => l.Min(b => b.Mn[a])).ToArray(), Mx: Enumerable.Range(0, 3).Select(a => l.Max(b => b.Mx[a])).ToArray())).ToList();
        var count = new int[parts.Count];
        for (var i = 0; i < parts.Count; i++)
            for (var j = i + 1; j < parts.Count; j++)
            {
                var (bi, bj) = (bounds[i], bounds[j]);
                if (Enumerable.Range(0, 3).Any(a => bi.Mn[a] > bj.Mx[a] + 0.002 || bj.Mn[a] > bi.Mx[a] + 0.002))
                    continue;
                (double Area, double[] At, int Axis, bool IPlus) best = (0, [], 0, false);
                foreach (var A in solid[i])
                    foreach (var B in solid[j])
                    {
                        if (Enumerable.Range(0, 3).All(a => Math.Min(A.Mx[a], B.Mx[a]) - Math.Max(A.Mn[a], B.Mn[a]) > 0.01))
                            throw new InvalidOperationException($"space_station: {parts[i].D["name"]} and {parts[j].D["name"]} overlap at [{string.Join(", ", A.Mn.Select(v => v.ToString("0.##")))}]");
                        for (var ax = 0; ax < 3; ax++)
                        {
                            bool iPlus = Math.Abs(A.Mx[ax] - B.Mn[ax]) < 0.001, iMinus = Math.Abs(A.Mn[ax] - B.Mx[ax]) < 0.001;
                            if (!iPlus && !iMinus)
                                continue;
                            var at = new double[3];
                            var area = 1.0;
                            var ok = true;
                            for (var a = 0; a < 3 && ok; a++)
                            {
                                if (a == ax)
                                {
                                    at[a] = iPlus ? A.Mx[a] : A.Mn[a];
                                    continue;
                                }
                                double lo = Math.Max(A.Mn[a], B.Mn[a]), hi = Math.Min(A.Mx[a], B.Mx[a]);
                                if (hi - lo < 0.03)
                                    ok = false;
                                at[a] = Math.Round((lo + hi) / 2, 2);
                                area *= hi - lo;
                            }
                            if (ok && area > best.Area)
                                best = (area, at, ax, iPlus);
                        }
                    }
                if (best.Area <= 0)
                    continue;
                var axis = "xyz"[best.Axis];
                Sock(parts[i], $"j{count[i]++}", best.At[0], best.At[1], best.At[2], (best.IPlus ? "+" : "-") + axis);
                Sock(parts[j], $"j{count[j]++}", best.At[0], best.At[1], best.At[2], (best.IPlus ? "-" : "+") + axis);
            }
    }

    private static bool Meet(Part a, Part b)
    {
        foreach (var s in a.Sockets)
            foreach (var t in b.Sockets)
            {
                var (pa, pb) = ((List<object?>)s["at"]!, (List<object?>)t["at"]!);
                if (Enumerable.Range(0, 3).All(i => Math.Abs(Convert.ToDouble(pa[i]) - Convert.ToDouble(pb[i])) < 0.004))
                {
                    var (da, db) = ((string)s["dir"]!, (string)t["dir"]!);
                    if (da[1] == db[1] && da[0] != db[0])
                        return true;
                }
            }
        return false;
    }

    /// <summary>The order to place the pieces in: out from the first over the socket pairs they meet at.</summary>
    private static List<string> PlaceOrder(List<Part> parts, string first)
    {
        var names = parts.Select(q => (string)q.D["name"]!).ToList();
        var seen = new HashSet<int> { names.IndexOf(first) };
        var order = new List<string> { first };
        var queue = new Queue<int>(seen);
        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            for (var j = 0; j < parts.Count; j++)
                if (!seen.Contains(j) && Meet(parts[i], parts[j]))
                {
                    seen.Add(j);
                    order.Add(names[j]);
                    queue.Enqueue(j);
                }
        }
        if (order.Count != parts.Count)
            throw new InvalidOperationException("space_station: pieces joined to nothing: " + string.Join(", ", names.Where((n, i) => !seen.Contains(i))));
        return order;
    }

    /// <summary>
    /// Moves a piece's origin from the station's to the middle of its own boxes (whole metres, so cells stay aligned): the game refuses
    /// a box more than 200 m from its part's origin, and a station is taller than that. Returns where the origin went (station frame),
    /// which is where the blueprint puts the piece.
    /// </summary>
    private static double[] Recentre(Part p)
    {
        var all = p.Boxes.Select(b => (Mn: Vd(b, "min"), Mx: Vd(b, "max"))).ToList();
        double[] o = [.. Enumerable.Range(0, 3).Select(a => Math.Round((all.Min(b => b.Mn[a]) + all.Max(b => b.Mx[a])) / 2))];
        List<object?> Shift(List<object?> v) => [.. v.Select((x, a) => (object?)R(Convert.ToDouble(x) - o[a]))];
        foreach (var b in p.Boxes)
        {
            b["min"] = Shift((List<object?>)b["min"]!);
            b["max"] = Shift((List<object?>)b["max"]!);
            if (b.Get("points") is List<object?> pts)
                b["points"] = pts.Select(q => (object?)Shift((List<object?>)q!)).ToList();
        }
        foreach (var s in p.Sockets)
            s["at"] = Shift((List<object?>)s["at"]!);
        if (p.D.Get("clampArm") is PyDict arm)
            foreach (var key in new[] { "shoulder", "stow" })
                arm[key] = Shift((List<object?>)arm[key]!);
        return o;
    }

    private static PyDict Blueprint(string name, string desc, Kit kit, List<string> order, Dictionary<string, double[]> origins, Dictionary<string, PyDict> defs)
    {
        var bp = new BuildingParts.Blueprint(name, desc, defs, maxSteps: 800);
        var placed = new Dictionary<string, int>();
        foreach (var piece in order)
            placed[piece] = placed.Count == 0 ? bp.First(piece) : bp.Put(piece, Sub3(origins[piece], origins[order[0]]), 0);
        foreach (var h in kit.Hangs)
        {
            var step = bp.HangTurned(h.Part, placed[h.Piece], h.Sock, h.By, BuildingParts.R0);
            if (h.Config != null)
                bp.Steps[step]["config"] = h.Config;
            if (h.Paint != null)
                bp.Steps[step]["paint"] = h.Paint;
            if (h.Finish != null)
                bp.Steps[step]["finish"] = h.Finish;
        }
        return bp.Json();
    }

    private static double[] Sub3(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];

    public void Make(PartOutput o)
    {
        var kit = RingStation();
        Connect(kit.Pieces);
        var order = PlaceOrder(kit.Pieces, "Highport Hub");
        var origins = kit.Pieces.ToDictionary(p => (string)p.D["name"]!, Recentre);
        var defs = new Dictionary<string, PyDict>();
        o.Own(Out);
        foreach (var p in kit.Pieces)
        {
            var d = p.Json();
            var name = (string)d["name"]!;
            defs.Add(name, d);
            o.File($"{Out}/Highport/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
        // the clamp arms' moving pieces, drawn by the game, never placed (SpaceStationClampArm.cs)
        foreach (var p in ClampArmPieces(new Kit("Station", "Clamp arm")))
        {
            var d = p.Json();
            o.File($"{Out}/ClampArm/{FileName((string)d["name"]!)}", PyJson.Dumps(d, 2) + "\n");
        }
        BuildingParts.AddPartDefs(o, defs, Out);
        o.File($"{BpOut}/CaledonHighport.json", PyJson.Dumps(Blueprint(HighportName,
            "Caledon Highport, the first space station: a ring of sixteen habitat segments 264 m across round one hexagonal hub under a command dome, joined by three spokes. Landing pads for Shuttles and Limpets on the ring's roof, hangars in its walls; under the hub a docking spindle with the customs drum, a dock collar with Magpie berths east and west, a freight collar with Dray berths, a frigate collar with Zero Day berths north and south (one line of berths to a level), and engineering with its radiators, the reactor and the station-keeping drives at its foot.",
            kit, order, origins, defs), 2) + "\n");

        // the clamp arm demo (a design draft: SpaceStationClampArm.cs)
        var demo = ClampArmDemo();
        Connect(demo.Pieces);
        var dOrder = PlaceOrder(demo.Pieces, "Clamp Demo Arm 1");
        var dOrigins = demo.Pieces.ToDictionary(p => (string)p.D["name"]!, Recentre);
        foreach (var p in demo.Pieces)
        {
            var d = p.Json();
            var name = (string)d["name"]!;
            defs.Add(name, d);
            o.File($"{Out}/ClampArm/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
        o.File($"{BpOut}/ClampArmDemo.json", PyJson.Dumps(Blueprint(ClampDemoName,
            "Design draft: the berth clamp arm, as a storyboard along one docking arm with four Zero Day berths. 1: a ship on the berth's approach lane, the arm stowed. 2: the ship at the lane's stage point, the arm reaching out to it, its clamp seeking. 3: clamped, the ship's drives off, drawn in. 4: moored, no longer flown. The ships are glass outlines of the Zero Day's size.",
            demo, dOrder, dOrigins, defs), 2) + "\n");
    }
}
