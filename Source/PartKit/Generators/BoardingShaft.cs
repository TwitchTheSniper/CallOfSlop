using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// A boarding shaft's pieces, shared by the ships with one (Karl, 2026-11-22: "ladders need to be recessed ... removing the outer
/// panelling and an optional hatch because it keeps impacting rooms"; "the hatch should be able to slide into the hull, or as a
/// hinge ... it acts as a smooth part of the hull when closed"). Before, the boarding ladder stood on a belly plate that slid down to
/// the ground, so while it was shut the ladder stood stowed up through the airlock's floor inside a rail. Now nothing stands over the
/// deck: a flush Deck Hatch (DoorParts.DeckHatch) shuts the shaft's top, and in the shaft
///
/// - the <b>boarding ladder</b>, a fixed flight from the belly up to just under the deck hatch, whose foot ("ladderFoot") is the
///   ground under the shaft: it is climbed from there once its sections are down;
/// - its <b>sections</b>, event doors on its front that slide down (each further than the one before, telescoping) from nested over
///   the flight to a ladder from the belly to the ground; their rungs land on the flight's pitch;
/// - the <b>belly hatch</b>, a plate flush with the keel's flat, hung on one long edge (the side away from the way out from under the hull, so open it
///   doesn't stand in it), that swings a quarter turn down outside.
///
/// F on the ladder opens the way (the airlock's switch works both hatches): ConstructRide.OpenWay works every
/// shut lid across the ladder's line and every stowed section linked to it. Numbers are the ship's metres; the parts' own frames are
/// set from the sockets the ship's generator puts on its floor (the shaft's aft face) and its keel (the void's face beside the shaft).
/// </summary>
public static class BoardingShaft
{
    /// <summary>
    /// A ship's shaft: its name word, section and group, the hole (x0..x1 across, z0..z1 along; the ladder on its aft edge z1, its
    /// sections climbed from forward of it), the deck's top, the keel flat's underside at the shaft, the ground under a craft on its
    /// legs, which edge the belly hatch hangs on (true: x1) and the paint. Fore: the ladder is on the hole's fore edge z0, climbed
    /// from aft of it. Lip: how far the keel's cut stands out from the hole (the belly hatch's plate overlaps the hole by Lip - 3 cm).
    /// </summary>
    public sealed record Spec(string Ship, string Section, string Group, double X0, double Z0, double X1, double Z1, double Deck, double Keel, double Ground,
        bool HingeX1, string Plate, string Frame, string Hazard, string Rail = "#9aa0a4", bool Fore = false, double Lip = 0.05)
    {
        public double Mid => (X0 + X1) / 2;
        /// <summary>The ladder's edge of the hole (z).</summary>
        public double LadderZ => Fore ? Z0 : Z1;
        /// <summary>The floor's socket the ladder hangs by (its back on the hole's face), in ship metres; it faces into the hole.</summary>
        public double[] LadderAt => [Mid, Deck - 0.125, LadderZ];
        public string LadderDir => Fore ? "+z" : "-z";
        /// <summary>
        /// The keel's socket the belly hatch hangs by, on the keel's cut face (Lip out from the hole), HangUp over the flat (the keel's void boxes start a little over it, higher where it slopes): beside the
        /// shaft on its hinge side, or in a shallow shaft (Shallow) on the ladder's face, the hatch carrying the rungs.
        /// </summary>
        public double[] HatchAt => Shallow(this) ? [Mid, Keel + HangUp, Fore ? Z0 - Lip : Z1 + Lip] : [HingeX1 ? X1 + Lip : X0 - Lip, Keel + HangUp, (Z0 + Z1) / 2];
        public string HatchDir => Shallow(this) ? (Fore ? "+z" : "-z") : HingeX1 ? "-x" : "+x";
        public string LadderName => $"{Ship} Boarding Ladder";
        public string HatchName => $"{Ship} Belly Hatch";
        public string SectionName(int k) => $"{Ship} Ladder Section {k}";
    }

    /// <summary>How far over the keel's flat the belly hatch's socket stands.</summary>
    public const double HangUp = 0.1;

    private const double Pitch = 0.3; // rung to rung
    private const double Front = -0.19; // the flight's front face (its back is on the shaft's aft face, at 0)
    private const double Nest = 0.035; // each section stands this much further forward than the one behind it

    // The flight's span in its own frame (0: the socket, 12.5 cm under the deck's top): from just over the shut belly hatch to just
    // under the shut deck hatch; the ground and the landing.
    private static double FootY(Spec s) => s.Ground - (s.Deck - 0.125);
    private static double LowY(Spec s) => s.Keel + HangUp + 0.02 - (s.Deck - 0.125); // (over the belly hatch and its hanger)
    private static double HighY => 0.125 - DoorParts.HatchThick - 0.012;
    private static double LandY => 0.125;

    /// <summary>
    /// A shaft too shallow for a flight under its deck hatch (the Dibber's 0.3 m belly): the belly hatch hangs on the ladder's edge
    /// and carries the rungs, hanging down open where the flight would stand, and the ladder is only its line (a hidden part).
    /// </summary>
    public static bool Shallow(Spec s) => HighY - LowY(s) < 0.1 + Pitch;

    /// <summary>How far each section slides down, and how many it takes to reach the ground: a whole number of rungs a step, overlapping 10 cm or more.</summary>
    public static (double Step, int Count) Sections(Spec s)
    {
        var len = HighY - LowY(s);
        var step = Math.Floor((len - 0.1) / Pitch) * Pitch;
        if (step < Pitch)
            throw new InvalidOperationException($"boarding shaft: the {s.Ship}'s shaft is too shallow for a telescoping ladder ({len:0.00} m)");
        var count = (int)Math.Ceiling((LowY(s) - FootY(s) - 0.02) / step);
        return (step, Math.Max(1, count));
    }

    /// <summary>The section parts' names (none in a shallow shaft).</summary>
    public static IEnumerable<string> SectionNames(Spec s) => Shallow(s) ? [] : Enumerable.Range(1, Sections(s).Count).Select(s.SectionName);

    /// <summary>The ladder, its sections and the belly hatch.</summary>
    public static IEnumerable<Part> Parts(Spec s)
    {
        yield return Ladder(s);
        if (Shallow(s))
        {
            yield return Hatch(s);
            yield break;
        }
        var (step, n) = Sections(s);
        for (var k = 1; k <= n; k++)
            yield return Section(s, k, n, step);
        yield return Hatch(s);
    }

    /// <summary>The deck hatch that shuts the shaft's top: the hole across (x) and along (z), hung by an x edge.</summary>
    public static string DeckHatchName(Spec s) => DoorParts.HatchName(Math.Round(s.X1 - s.X0, 2), Math.Round(s.Z1 - s.Z0, 2));

    /// <summary>
    /// Fits a shaft to a ship's blueprint: the ladder on the floor's socket, its sections on it, the deck hatch in the floor's hole
    /// and the belly hatch on the keel, all shut. Returns the two hatches, for the airlock's switch (the sections come down on F at
    /// the ladder, linked to it: a switch's cable to them would cross the deck hatch).
    /// </summary>
    public static int[] Fit(BuildingParts.Blueprint bp, Spec s, int floor, string ladderSock, int lidOn, string lidSock, int keel, string hatchSock)
    {
        var ladder = bp.Hang(s.LadderName, floor, ladderSock, "back");
        for (var k = 1; !Shallow(s) && k <= Sections(s).Count; k++)
            bp.Hang(s.SectionName(k), ladder, $"section{k}", "hang");
        return [bp.Hang(s.HatchName, keel, hatchSock, "hang"), bp.Hang(DeckHatchName(s), lidOn, lidSock, "hang")];
    }

    private static PyDict Fin(string f) => Kw(("finish", f));

    private static Part Ladder(Spec s)
    {
        if (Shallow(s))
        {
            var q = new Part(s.LadderName, Small, s.Section, $"The {s.Ship}'s boarding ladder: its rungs are on the belly hatch, which hangs down open from the shaft's edge under the deck hatch. F opens the way (both hatches), then F climbs it.",
                role: "frame", tool: "welder", material: "steel", color: s.Frame, group: s.Group);
            q.Add(Box([-0.3, LowY(s), Front], [0.3, HighY - 0.04, 0], hidden: true)); // (the flight's room in the shaft: the open hatch hangs under it)
            q.Add(Box([-0.25, HighY - 0.04, -0.06], [0.25, HighY - 0.005, -0.004], s.Rail, detail: true, shape: Fin("painted steel"))); // (a grab rail across the shaft's face under the deck hatch)
            q.Sock("back", [0, 0, 0], "+z");
            q.Set(("ladder", LandY), ("ladderFoot", Py.Round(FootY(s), 3)));
            return q.Stats(10, 200, 6, 1.0);
        }
        var (step, n) = Sections(s);
        var p = new Part(s.LadderName, Small, s.Section, $"The {s.Ship}'s boarding ladder: a fixed flight up the boarding shaft under the airlock's deck hatch, with {n} section{(n == 1 ? "" : "s")} that slide down from it to the ground. F opens the way (the hatches and the sections), then F climbs it.",
            role: "frame", tool: "welder", material: "steel", color: s.Frame, group: s.Group);
        double lo = LowY(s), hi = HighY, foot = FootY(s);
        p.Add(Box([-0.3, lo, Front], [0.3, hi, 0], hidden: true));
        foreach (var x in new[] { -0.25, 0.25 })
        {
            p.Add(Box([x - 0.03, lo, -0.18], [x + 0.03, hi, -0.12], s.Rail, detail: true, shape: Fin("painted steel")));
            for (var y = lo + 0.15; y < hi - 0.1; y += 0.45)
                p.Add(Box([x - 0.02, y, -0.12], [x + 0.02, y + 0.06, -0.006], s.Frame, detail: true, shape: Fin("dirty steel"))); // (stand-offs to the shaft's wall)
        }
        for (var y = foot + Pitch; y < hi - 0.03; y += Pitch)
            if (y > lo + 0.03)
                p.Add(Box([-0.22, y - 0.02, -0.165], [0.22, y + 0.02, -0.135], s.Hazard, detail: true, shape: Fin("hazard paint")));
        p.Sock("back", [0, 0, 0], "+z");
        for (var k = 1; k <= n; k++)
            p.Sock($"section{k}", [0, Py.Round(SectionSockY(s, k), 3), Front], "-z");
        p.Set(("ladder", LandY), ("ladderFoot", Py.Round(foot, 3)));
        return p.Stats(40 + (hi - lo) * 12, 400, 6, 3.0);
    }

    /// <summary>Where section k hangs on the flight's front (each at its own height, so their hangers don't meet).</summary>
    private static double SectionSockY(Spec s, int k) => HighY - 0.08 - 0.06 * k;

    private static Part Section(Spec s, int k, int n, double step)
    {
        var travel = Math.Min(k * step, LowY(s) - FootY(s) - 0.01);
        var p = new Part(s.SectionName(k), Small, s.Section, $"Section {k} of the {s.Ship}'s boarding ladder: it slides down {travel:0.##} m from nested on the fixed flight, the ladder reaching the ground in {n} section{(n == 1 ? "" : "s")}. F on the ladder lowers them all.",
            role: "door", tool: "welder", material: "steel", color: s.Rail, group: s.Group);
        var sy = SectionSockY(s, k);
        double lo = LowY(s) - sy, hi = HighY - sy, z1 = -Nest * k + 0.03, z0 = z1 - 0.03, w = 0.25 - 0.03 * k; // (its own frame: 0 at its socket on the flight's front)
        foreach (var x in new[] { -w, w })
            p.Add(Box([x - 0.025, lo, z0], [x + 0.025, hi, z1], s.Rail, detail: true, shape: Fin("painted steel")));
        // Rungs on the flight's pitch once it is down (it moves a whole number of rungs).
        var foot = FootY(s) - sy;
        for (var y = foot + Pitch; y < hi - 0.03; y += Pitch)
        {
            var at = y - (travel - Math.Floor(travel / Pitch + 1e-6) * Pitch); // (offset so its rungs meet the pitch at the end of its travel)
            if (at > lo + 0.03 && at < hi - 0.03)
                p.Add(Box([-w + 0.02, at - 0.02, z0 + 0.005], [w - 0.02, at + 0.02, z1 - 0.005], s.Hazard, detail: true, shape: Fin("hazard paint")));
        }
        p.Add(Box([-w - 0.03, lo, z0 - 0.004], [w + 0.03, lo + 0.06, z1 + 0.004], s.Frame, detail: true, shape: Fin("dirty steel"))); // (its foot: a crossbar, rubber shod)
        p.Add(Box([-0.02, -0.01, z0], [0.02, 0.01, 0], hidden: true)); // (the hanger back to the flight's face: a socket is on a face)
        p.Sock("hang", [0, 0, 0], "+z");
        p.Set(("door", Kw(("kind", "slide"), ("axis", "-y"), ("travel", Py.Round(travel, 3)), ("seconds", 2.0 + 0.5 * k), ("hang", "hang"))));
        p.Set(("physicsOnly", true));
        return p.Stats(18 + (hi - lo) * 8, 300, 6, 2.0);
    }

    private static Part Hatch(Spec s)
    {
        var shallow = Shallow(s);
        var p = new Part(s.HatchName, Small, s.Section, shallow
                ? $"The {s.Ship}'s belly hatch: a plate flush with the belly under the boarding shaft, the ladder's rungs on its inside, hung on the ladder's edge. F (or the switch, or F on the ladder) swings it down to hang as the ladder."
                : $"The {s.Ship}'s belly hatch: a plate flush with the keel under the boarding shaft, hung on one edge. F (or the airlock's switch, or F on the ladder) swings it down outside.",
            role: "door", tool: "welder", material: "steel", color: s.Plate, group: s.Group);
        // Its own frame: x out from the shaft towards the hinge (the socket at 0, Lip out from the hole's edge), y up from the socket
        // (HangUp over the keel's flat), z along the hinge from the shaft's middle. Built for a hinge on +x, turned by the socket for the
        // others (a shallow shaft's hinge is on the ladder's edge, along x).
        double len = shallow ? s.X1 - s.X0 : s.Z1 - s.Z0, wide = shallow ? s.Z1 - s.Z0 : s.X1 - s.X0, lip = s.Lip;
        double x0 = -wide - 2 * lip + 0.03, x1 = -0.03, bot = -HangUp - 0.012, top = bot + 0.08, h = len / 2 + lip - 0.038;
        p.Add(Box([x0, bot, -h], [x1, top, h], s.Plate, shape: Fin("plated steel")));
        for (var z = -h + 0.15; z < h - 0.2; z += 0.3)
            p.Add(Box([x0 + 0.1, bot - 0.006, z], [x1 - 0.1, bot, z + 0.12], s.Hazard, detail: true, shape: Kw(("finish", "hazard paint"), ("paintable", false)))); // (hazard bars on its outside)
        if (shallow)
        {
            // The ladder on its inside: two rails out from the hinge and rungs between them, a rung's pitch apart (open, it hangs as the ladder).
            foreach (var z in new[] { -0.25, 0.25 })
                p.Add(Box([x0 + 0.04, top + 0.002, z - 0.03], [x1 - 0.04, top + 0.052, z + 0.03], s.Rail, detail: true, shape: Fin("painted steel")));
            for (var x = x1 - 0.15; x > x0 + 0.08; x -= Pitch)
                p.Add(Box([x - 0.02, top + 0.003, -0.217], [x + 0.02, top + 0.033, 0.217], s.Hazard, detail: true, shape: Kw(("finish", "hazard paint"), ("paintable", false))));
        }
        else
        {
            p.Add(Box([x0 + 0.04, top + 0.002, -h + 0.04], [x0 + 0.12, top + 0.052, h - 0.04], s.Frame, detail: true, shape: Fin("dirty steel"))); // (stiffeners inside)
            p.Add(Box([x1 - 0.12, top + 0.002, -h + 0.04], [x1 - 0.04, top + 0.052, h - 0.04], s.Frame, detail: true, shape: Fin("dirty steel")));
        }
        foreach (var z in new[] { -h + 0.15, h - 0.3 })
            p.Add(Box([x1 - 0.015, bot - 0.004, z], [x1 + 0.012, bot + 0.03, z + 0.15], "#55595e", detail: true, shape: Fin("steel"))); // (the hinge's knuckles, at its outboard foot)
        p.Add(Box([x1, -0.01, -0.2], [0, 0.01, 0.2], hidden: true)); // (the hanger to the keel's face: a socket is on a face)
        p.Sock("hang", [0, 0, 0], "+x");
        p.Set(("door", Kw(("kind", "swing"), ("axis", "+z"), ("pivot", Py.L(Py.Round(x1, 3), Py.Round(bot, 3), 0.0)), ("angle", 90), ("seconds", 2.5), ("hang", "hang"))));
        return p.Stats(260 * wide * len, 3000, 30, 5.0);
    }
}
