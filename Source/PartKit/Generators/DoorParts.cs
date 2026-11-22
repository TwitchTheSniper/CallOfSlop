using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The flush doors, the door hinges, the event doors and the Door Handle: Constructs/Parts/Doors (docs/sandbox/CONSTRUCTS.md, "Doors").
/// Was tools/constructs/door_parts.py.
///
/// A door here hangs inside its opening, in the plane of the wall, with the same small gap all round. A Door Hinge does that: it fits in the gap
/// itself (Gap wide), its base socket on one jamb and its leaf socket facing the door, and its pivot sits on the door's outer face, so the door swings
/// out past the jamb. Its min is 0: it opens one way, towards its own -z, and the hinge's knuckles are all that shows outside. Turn it end over to swing
/// the other way.
///
/// A door is named for its opening, not its slab: a Flush Door 1.5x2.25 m is Gap smaller at each edge, with the hinge in the gap on one side. Sizes are
/// the openings the game has: 1.5x2.25 m a Door Wall, Door Corner or bunker; 0.7x1.8 m a Door Frame 1x2 m (its jamb is off the socket grid: weld the
/// hinge on); 1.25x2.35 m a vehicle kit room; 1x2 m and 1.25x2 m for what players cut themselves. Its hang socket is at the middle of its edge, and a
/// door's height is measured from the same middle, so a hinge at the opening's mid-height centres it. Recipes come from the part kit's assembling (--gen-parts assembling).
/// </summary>
public sealed class DoorParts : IPartGenerator
{
    public string Name => "door_parts";

    private const string Out = "Constructs/Parts/Doors";
    private const string Section = "Doors and gates";
    private const string Group = "Building";
    private const double Gap = 0.02; // the gap round a door, and a Door Hinge's width
    private const double Thick = 0.04; // a flush door
    private const double Pivot = -Thick / 2; // a Door Hinge's axis: on the door's outer face, so its inner corner swings away from the jamb
    private const double Knuckle = 0.01; // its barrel's radius: all that stands out of the wall
    private const string Steel = "#3b4046", Dark = "#2a2d31", Barrel = "#55595e", Pin = "#8d939a", Grip = "#c8c8c8";
    private static readonly (double W, double H)[] Openings = [(0.7, 1.8), (1.0, 2.0), (1.25, 2.0), (1.25, 2.35), (1.5, 2.25)];

    /// <summary>name, colour, trim colour, material, kg/m2 (int or float, as written), health/m2, armour.</summary>
    private sealed record Kind(string Word, string Colour, string Trim, string Material, double Kg, int Hp, int Armour);

    private static readonly Kind[] Kinds = [new("Steel", "#5a6168", "#4c5359", "steel", 43, 215, 30), new("Wood", "#6b5a3e", "#5c4d35", "wood", 12.5, 57, 2)];

    private static PyDict Use => Kw(("use", true), ("paintable", false));

    private static Part Hinge(string name, double length, int knuckles, double mass, double health)
    {
        var p = new Part(name, Small, "Hinges", "Fits in a door's gap, on the jamb: what hangs on it swings in the wall's plane and out past it. Any panel can hang on it.",
            role: "hinge", tool: "wrench", color: Steel, group: "Joints and Motion");
        var h = length / 2;
        p.Add(Box([-Gap / 2, -h, -Thick / 2], [Gap / 2, h, Thick / 2], Dark, shape: Kw(("side", "base"))));
        var pitch = length / knuckles;
        for (var k = 0; k < knuckles; k++)
        {
            var y = -h + (k + 0.5) * pitch;
            var b = pitch * 0.4;
            p.Add(Box([-Knuckle, y - b, Pivot - Knuckle], [Knuckle, y + b, Pivot + Knuckle], k % 2 != 0 ? Pin : Barrel, detail: true,
                shape: Kw(("shape", "cylinder"), ("segments", 8), ("front", "+y"), ("side", k % 2 != 0 ? "leaf" : "base"), ("paintable", false))));
        }
        p.Sock("base", [-Gap / 2, 0, 0], "-x");
        p.Sock("leaf", [Gap / 2, 0, 0], "+x", kind: "leaf");
        p.Set(("collider", "boxes")); // (the knuckles are looks, and half of them swing with the leaf: a hull here would stand still)
        p.Set(("hinge", Kw(("axis", "+y"), ("pivot", Py.L(0, 0, Pivot)), ("min", 0), ("max", 100), ("leaf", "leaf"))), ("config", Py.L(
            Kw(("key", "max"), ("label", "Swings open to"), ("group", "Limits"), ("unit", "deg"), ("min", 0), ("max", 100), ("step", 1), ("tooltip", "How far it swings open.")),
            Kw(("key", "pushable"), ("label", "Opens when pushed"), ("group", "Door"), ("default", true), ("tooltip", "On: a player walking into it swings it. Off: only F opens it. A wire into its Push port overrides this while connected: high on, low off.")))));
        p.Stats(mass, health, 5, 1.5);
        return p;
    }

    private static Part Door(Kind kind, double w, double h)
    {
        var name = $"{kind.Word} Flush Door {Py.G(w)}x{Py.G(h)} m";
        double dw = w - 2 * Gap, dh = h - 2 * Gap;
        var p = new Part(name, Small, Section, $"Fills a {Py.G(w)} by {Py.G(h)} m opening, on a Door Hinge in its gap.", role: "door", tool: "wrench", material: kind.Material, color: kind.Colour, group: Group);
        var t = Thick / 2;
        p.Add(Box([0, -dh / 2, -t], [dw, dh / 2, t]));
        // Two raised panels, on both faces (one box through the door: a box under 1 cm is skipped), and the handle through it.
        var m = 0.1;
        var mid = dh * 0.1;
        p.Add(Box([m, mid + 0.05, -t - 0.003], [dw - m, dh / 2 - m, t + 0.003], kind.Trim, detail: true));
        p.Add(Box([m, -dh / 2 + m, -t - 0.003], [dw - m, mid - 0.05, t + 0.003], kind.Trim, detail: true));
        p.Add(Box([dw - 0.15, -0.05, -t - 0.04], [dw - 0.1, 0.05, t + 0.04], Grip, detail: true, shape: Use));
        p.Sock("hang", [0, 0, 0], "-x", kind: "leaf");
        p.Sock("front", [dw / 2, 0, t], "+z");
        p.Sock("back", [dw / 2, 0, -t], "-z");
        var area = dw * dh;
        p.Stats(kind.Kg * area, kind.Hp * area, kind.Armour, 2.0);
        if (kind.Kg * area <= 40)
            p.Set(("physicsOnly", true)); // (light but bulky: never an item, as the Wood Door 1.25 m)
        return p;
    }

    // Event doors (docs/sandbox/CONSTRUCTS.md, "Event doors"): no hinge part and no joint. The door part goes straight on the
    // opening's jamb (its "hang" socket is a plain one, Gap off its edge, so the gap is kept), and its "door" block says how it
    // moves: a swing door turns a quarter turn about its edge on its outer face (towards its own -z, out past the jamb), a
    // bulkhead slides into the wall beside the opening. Its state is the game's (shut, open, moving), its F line is a door's,
    // and it has Open, Toggle and Lock ports. A Left and a Right leaf hung on either jamb of one opening meet in the middle
    // ("meet" sockets): linked there, F on either works both.
    private const double EventSeconds = 0.8;
    private const double BulkheadThick = 0.06;
    private static readonly (double W, double H)[] EventOpenings = [(1.0, 2.0), (1.25, 2.0), (1.25, 2.35), (1.5, 2.25)];

    private static void Slab(Part p, Kind kind, double x0, double x1, double dh, double t)
    {
        p.Add(Box([x0, -dh / 2, -t], [x1, dh / 2, t]));
        var m = 0.1;
        var mid = dh * 0.1;
        p.Add(Box([x0 + m, mid + 0.05, -t - 0.003], [x1 - m, dh / 2 - m, t + 0.003], kind.Trim, detail: true));
        p.Add(Box([x0 + m, -dh / 2 + m, -t - 0.003], [x1 - m, mid - 0.05, t + 0.003], kind.Trim, detail: true));
    }

    /// <summary>A hidden solid strip in the gap on the hang side: what the plain "hang" socket sits on (a socket is on a face).</summary>
    private static void HangStrip(Part p, int s = 1)
    {
        var (x0, x1) = s > 0 ? (-Gap, 0.0) : (0.0, Gap);
        p.Add(Box([x0, -0.2, -0.01], [x1, 0.2, 0.01], hidden: true));
    }

    private static void EventStats(Part p, Kind kind, double area)
    {
        p.Stats(kind.Kg * area, kind.Hp * area, kind.Armour, 2.0);
        if (kind.Kg * area <= 40)
            p.Set(("physicsOnly", true));
    }

    private static Part SwingDoor(Kind kind, double w, double h)
    {
        var name = $"{kind.Word} Swing Door {Py.G(w)}x{Py.G(h)} m";
        double dw = w - 2 * Gap, dh = h - 2 * Gap;
        var p = new Part(name, Small, Section, $"Fills a {Py.G(w)} by {Py.G(h)} m opening: goes straight on its jamb, no hinge. F opens it; it swings out past the jamb.",
            role: "door", tool: "wrench", material: kind.Material, color: kind.Colour, group: Group);
        var t = Thick / 2;
        Slab(p, kind, 0, dw, dh, t);
        p.Add(Box([dw - 0.15, -0.05, -t - 0.04], [dw - 0.1, 0.05, t + 0.04], Grip, detail: true, shape: Use));
        HangStrip(p);
        p.Sock("hang", [-Gap, 0, 0], "-x");
        p.Sock("front", [dw / 2, 0, t], "+z");
        p.Sock("back", [dw / 2, 0, -t], "-z");
        p.Set(("door", Kw(("kind", "swing"), ("axis", "+y"), ("pivot", Py.L(0, 0, -t)), ("angle", 90), ("seconds", EventSeconds), ("hang", "hang"))));
        EventStats(p, kind, dw * dh);
        return p;
    }

    /// <summary>Half of a double door for a w-wide opening: Left hangs on the left jamb, Right on the right; they meet in the middle.</summary>
    private static Part DoubleLeaf(Kind kind, double w, double h, string side)
    {
        var name = $"{kind.Word} Double Door {side[0]} {Py.G(w)}x{Py.G(h)} m"; // (L or R: a name is at most 32 characters)
        double dw = w / 2 - 1.5 * Gap, dh = h - 2 * Gap;
        var p = new Part(name, Small, Section, $"One leaf of a double door for a {Py.G(w)} by {Py.G(h)} m opening, on the {side.ToLowerInvariant()} jamb; the other leaf meets it. F on either opens both.",
            role: "door", tool: "wrench", material: kind.Material, color: kind.Colour, group: Group);
        var t = Thick / 2;
        var s = side == "Left" ? 1 : -1; // (the Right leaf is the Left one mirrored: it hangs on its +x edge)
        var (x0, x1) = s > 0 ? (0.0, dw) : (-dw, 0.0);
        Slab(p, kind, x0, x1, dh, t);
        var gx = s > 0 ? dw - 0.12 : -dw + 0.07;
        p.Add(Box([gx, -0.05, -t - 0.04], [gx + 0.05, 0.05, t + 0.04], Grip, detail: true, shape: Use));
        HangStrip(p, s);
        p.Sock("hang", [-s * Gap, 0, 0], s > 0 ? "-x" : "+x");
        double a = s * (dw - Gap), b = s * (dw + Gap / 2);
        double m0 = Math.Min(a, b), m1 = Math.Max(a, b);
        p.Add(Box([m0, -0.2, -0.01], [m1, 0.2, 0.01], hidden: true)); // (the meeting edge's socket sits on it, half the gap out)
        p.Sock("meet", [s * (dw + Gap / 2), 0, 0], s > 0 ? "+x" : "-x");
        p.Sock("front", [s * dw / 2, 0, t], "+z");
        p.Sock("back", [s * dw / 2, 0, -t], "-z");
        p.Set(("door", Kw(("kind", "swing"), ("axis", "+y"), ("pivot", Py.L(0, 0, -t)), ("angle", 90 * s), ("seconds", EventSeconds), ("hang", "hang"))));
        EventStats(p, kind, dw * dh);
        return p;
    }

    private static Part Bulkhead(double w, double h)
    {
        var kind = Kinds[0];
        var name = $"Bulkhead Door {Py.G(w)}x{Py.G(h)} m";
        double dw = w - 2 * Gap, dh = h - 2 * Gap;
        var p = new Part(name, Small, Section, $"A sliding steel door for a {Py.G(w)} by {Py.G(h)} m opening: goes on its jamb and slides into the wall on that side (keep {Py.G(dw)} m of wall there).",
            role: "door", tool: "wrench", material: "steel", color: "#6a6f58", group: Group);
        var t = BulkheadThick / 2;
        p.Add(Box([0, -dh / 2, -t], [dw, dh / 2, t]));
        foreach (var y in new[] { -dh / 2 + 0.25, dh / 2 - 0.25 }) // (two ribs across, both faces)
            p.Add(Box([0.06, y - 0.04, -t - 0.012], [dw - 0.06, y + 0.04, t + 0.012], "#4f5442", detail: true));
        p.Add(Box([dw - 0.12, -0.15, -t - 0.03], [dw - 0.06, 0.15, t + 0.03], Grip, detail: true, shape: Use));
        HangStrip(p);
        p.Sock("hang", [-Gap, 0, 0], "-x");
        p.Sock("front", [dw / 2, 0, t], "+z");
        p.Sock("back", [dw / 2, 0, -t], "-z");
        p.Set(("door", Kw(("kind", "slide"), ("axis", "-x"), ("travel", Py.Round(dw + Gap, 2)), ("seconds", 1.2), ("hang", "hang"))));
        p.Stats(kind.Kg * 1.5 * dw * dh, kind.Hp * 1.5 * dw * dh, kind.Armour + 10, 2.5);
        return p;
    }

    /// <summary>A Slide Door: the miner's pocket door (Karl, 2026-11-15: "more like what you'd expect on a miner ship"). It goes on a doorway's
    /// jamb like a Bulkhead Door and slides sideways into the wall on that side. A framed, ribbed steel leaf with a kick plate, a small viewport,
    /// a hazard-striped leading stile with a rubber seal, a pull bar each side and a status lamp. Everything stays within 9 cm of its middle, so
    /// it hides inside any wall 20 cm thick or more once open.</summary>
    private static Part SlideDoor(double w, double h)
    {
        var kind = Kinds[0];
        var name = $"Slide Door {Py.G(w)}x{Py.G(h)} m";
        double dw = w - 2 * Gap, dh = h - 2 * Gap, hy = dh / 2;
        var p = new Part(name, Small, Section, $"A rugged sliding pocket door for a {Py.G(w)} by {Py.G(h)} m opening, in place of a hinge and a door: goes on its jamb and slides into the wall on that side (keep {Py.G(dw)} m of wall there, 20 cm thick or more).",
            role: "door", tool: "wrench", material: "steel", color: "#8a8f86", group: Group);
        const string Trim = "#5e635c", Kick = "#3f433f", Ring = "#2a2d2b", Yellow = "#e2b21c", Black = "#1d1e1d", Rubber = "#161716", Lamp = "#5cff7a";
        var fixedPaint = Kw(("paintable", false));
        // The leaf's core, a little inside its frame on every edge (so no face of it lies on the frame's), with the viewport cut through it.
        double wx0 = dw * 0.5 - 0.17, wx1 = dw * 0.5 + 0.17, wy1 = hy - 0.17, wy0 = hy - 0.6; // (the viewport's opening)
        double c = 0.035, e = 0.005;
        p.Add(Box([e, -hy + e, -c], [dw - e, wy0 - 0.004, c]));
        p.Add(Box([e, wy1 + 0.004, -c], [dw - e, hy - e, c]));
        p.Add(Box([e, wy0 - 0.004, -c], [wx0 - 0.004, wy1 + 0.004, c]));
        p.Add(Box([wx1 + 0.004, wy0 - 0.004, -c], [dw - e, wy1 + 0.004, c]));
        p.Add(Box([wx0 - 0.004, wy0 - 0.004, -0.008], [wx1 + 0.004, wy1 + 0.004, 0.008], glass: true, detail: true));
        // The frame: two stiles (the leading one wider) and two rails, proud of the core on both faces.
        p.Add(Box([0, -hy, -0.05], [0.09, hy, 0.05], Trim, detail: true));
        p.Add(Box([dw - 0.11, -hy, -0.05], [dw, hy, 0.05], Trim, detail: true));
        p.Add(Box([0.07, hy - 0.1, -0.045], [dw - 0.09, hy, 0.045], Trim, detail: true));
        p.Add(Box([0.07, -hy, -0.045], [dw - 0.09, -hy + 0.08, 0.045], Trim, detail: true));
        p.Add(Box([0.07, -hy + 0.06, -0.047], [dw - 0.09, -hy + 0.34, 0.047], Kick, detail: true)); // (the kick plate)
        // The viewport's dark ring, its opening 4 mm inside the hole in the core.
        p.Add(Box([wx0 - 0.04, wy1, -0.05], [wx1 + 0.04, wy1 + 0.04, 0.05], Ring, detail: true));
        p.Add(Box([wx0 - 0.04, wy0 - 0.04, -0.05], [wx1 + 0.04, wy0, 0.05], Ring, detail: true));
        p.Add(Box([wx0 - 0.04, wy0 + 0.002, -0.049], [wx0, wy1 - 0.002, 0.049], Ring, detail: true));
        p.Add(Box([wx1, wy0 + 0.002, -0.049], [wx1 + 0.04, wy1 - 0.002, 0.049], Ring, detail: true));
        // Corrugations across the leaf between the kick plate and the viewport.
        for (var y = -hy + 0.46; y + 0.06 < wy0 - 0.1; y += 0.22)
            p.Add(Box([0.07, y, -0.043], [dw - 0.09, y + 0.06, 0.043], Trim, detail: true));
        // The leading stile: a black strip with yellow bands, and the rubber seal standing just past the edge.
        p.Add(Box([dw - 0.1, -hy + 0.03, -0.052], [dw - 0.01, hy - 0.03, 0.052], Black, detail: true, shape: fixedPaint));
        for (var y = -hy + 0.06; y + 0.14 < hy - 0.05; y += 0.28)
            p.Add(Box([dw - 0.095, y, -0.055], [dw - 0.015, y + 0.14, 0.055], Yellow, detail: true, shape: fixedPaint));
        p.Add(Box([dw - 0.004, -hy + 0.03, -0.03], [dw + 0.012, hy - 0.03, 0.03], Rubber, detail: true, shape: fixedPaint));
        // Bolts down the hang stile.
        for (var y = -hy + 0.2; y < hy - 0.15; y += 0.4)
            p.Add(Box([0.03, y, -0.056], [0.06, y + 0.03, 0.056], Ring, detail: true));
        // A pull bar on standoffs on each face, and a status lamp on the top rail.
        double bx0 = dw - 0.24, bx1 = dw - 0.2;
        foreach (var y in new[] { -0.2, 0.16 })
            p.Add(Box([bx0 + 0.005, y, -0.07], [bx1 - 0.005, y + 0.04, 0.07], Ring, detail: true));
        p.Add(Box([bx0, -0.22, 0.07], [bx1, 0.22, 0.09], Grip, detail: true, shape: Use));
        p.Add(Box([bx0, -0.22, -0.09], [bx1, 0.22, -0.07], Grip, detail: true, shape: Use));
        p.Add(Box([dw - 0.2, hy - 0.075, -0.051], [dw - 0.14, hy - 0.035, 0.051], Lamp, detail: true, shape: Kw(("glow", true), ("paintable", false))));
        HangStrip(p);
        p.Sock("hang", [-Gap, 0, 0], "-x");
        p.Sock("front", [dw / 2, 0, c], "+z");
        p.Sock("back", [dw / 2, 0, -c], "-z");
        p.Set(("door", Kw(("kind", "slide"), ("axis", "-x"), ("travel", Py.Round(dw + Gap, 2)), ("seconds", 1.0), ("hang", "hang"))));
        p.Stats(kind.Kg * 1.6 * dw * dh, kind.Hp * 1.6 * dw * dh, kind.Armour + 10, 2.5);
        return p;
    }

    // ---- Deck hatches (Karl, 2026-11-22: "ladders need to be recessed and meet the edge of the hull ... I should be able to walk over
    // the hatch"; "it acts as a smooth part of the hull when closed": the flush shell pods' look). A ladder between decks stands in a
    // hole against the outboard wall, its top under the floor, and the hole takes one of these: a lid level with the deck, so a shut
    // hatch is floor to walk over. It hangs by its hull-side edge and swings down a quarter turn into the shaft, standing against
    // the hull side of it, clear of the ladder on the hole's fore or aft edge. F (from above or below) works it, and F on the ladder
    // under a shut one opens it first (ConstructRide.NextLid, OpenWay). The floor round the hole carries the coaming (DeckHatchRim).

    /// <summary>A deck hatch's lid: its thickness (its top is the deck's top) and the gap round it in its hole.</summary>
    public const double HatchThick = 0.05, HatchGap = 0.01;

    /// <summary>The deck hatch sizes: the hole each fills (m), across (out from its hinge) and along its hinge.</summary>
    public static readonly (double Across, double Along)[] HatchSizes = [(1.0, 1.0), (1.25, 1.25), (0.9, 0.75), (1.2, 1.1)];

    /// <summary>A deck hatch's name for a hole <paramref name="across"/> out from its hinge and <paramref name="along"/> it (a square one by its side).</summary>
    public static string HatchName(double across, double? along = null) =>
        along is { } l && l != across ? $"Deck Hatch {Py.G(l)}x{Py.G(across)} m" : $"Deck Hatch {Py.G(across)} m";

    private const string HatchLid = "#3b4148", HatchUnder = "#2c3036", HatchTrim = "#155a64", HatchHazard = "#d8a428";

    private static Part DeckHatch(double across, double along)
    {
        var hole = across == along ? $"{Py.G(across)} m square" : $"{Py.G(along)} by {Py.G(across)} m";
        var p = new Part(HatchName(across, along), Small, Section, $"A flush lid for a {hole} hole in a deck, level with the floor: walk over it shut. It hangs by one edge (the hull side of a ladder shaft) and F swings it down into the shaft; F on a ladder under a shut one opens it.",
            role: "door", tool: "wrench", material: "steel", color: HatchLid, group: Group);
        double t = HatchThick, a = across - 2 * HatchGap, h = (along - 2 * HatchGap) / 2; // (x: 0 at the hinge out to a; z: -h..h along the hinge; y: the top at 0)
        p.Add(Box([0, -t, -h], [a, 0, h], HatchLid, shape: Kw(("finish", "painted steel"))));
        // On top, all 3 mm proud: a deep teal trim round the edge (the shell pods' rim), a recessed pull and a hinge strip.
        foreach (var s in new[] { -1.0, 1.0 })
            p.Add(Box([0.04, 0, Math.Min(s * (h - 0.04), s * (h - 0.058))], [a - 0.04, 0.003, Math.Max(s * (h - 0.04), s * (h - 0.058))], HatchTrim, detail: true, shape: Kw(("paintable", false))));
        p.Add(Box([0.04, 0, -h + 0.061], [0.058, 0.003, h - 0.061], HatchTrim, detail: true, shape: Kw(("paintable", false))));
        p.Add(Box([a - 0.058, 0, -h + 0.061], [a - 0.04, 0.003, h - 0.061], HatchTrim, detail: true, shape: Kw(("paintable", false))));
        p.Add(Box([a - 0.2, 0, -0.14], [a - 0.08, 0.003, 0.14], "#1c1f23", detail: true, shape: Kw(("finish", "rubber"), ("paintable", false)))); // (the pull's recess)
        p.Add(Box([a - 0.17, 0.003, -0.1], [a - 0.11, 0.006, 0.1], Grip, detail: true, shape: Kw(("use", true), ("paintable", false)))); // (its bar)
        p.Add(Box([0.006, 0, -h + 0.08], [0.03, 0.003, h - 0.08], Barrel, detail: true)); // (the hinge, along the hull-side edge)
        // Underneath: darker plating with two stiffeners and hazard chevrons, what shows from the deck below and when it hangs open.
        p.Add(Box([0.08, -t - 0.035, -h + 0.06], [0.14, -t, h - 0.06], HatchUnder, detail: true));
        p.Add(Box([a - 0.14, -t - 0.035, -h + 0.06], [a - 0.08, -t, h - 0.06], HatchUnder, detail: true));
        for (var z = -h + 0.12; z < h - 0.2; z += 0.3)
            p.Add(Box([0.2, -t - 0.003, z], [a - 0.2, -t, z + 0.12], HatchHazard, detail: true, shape: Kw(("finish", "hazard paint"), ("paintable", false))));
        p.Add(Box([-HatchGap, -t, -0.2], [0, 0, 0.2], hidden: true)); // (the hang socket's strip, in the gap: a socket is on a face)
        p.Sock("hang", [-HatchGap, -t / 2, 0], "-x");
        p.Set(("door", Kw(("kind", "swing"), ("axis", "+z"), ("pivot", Py.L(0, -t, 0)), ("angle", -90), ("seconds", 1.2), ("hang", "hang"))));
        p.Stats(43 * a * 2 * h, 215 * a * 2 * h, 30, 2.0);
        return p;
    }

    /// <summary>
    /// The coaming round a deck hatch's hole (x0, z0 to x1, z1) on a floor whose top is y: a hazard band 3 mm
    /// proud with a teal trim along its inner edge, the flush shell pods' rim. <paramref name="skip"/> leaves one side bare (a
    /// wall stands there): "-x", "+x", "-z" or "+z".
    /// </summary>
    public static void DeckHatchRim(Part p, double x0, double z0, double x1, double z1, double y, string? skip = null)
    {
        const double band = 0.07;
        void Band(double ax, double az, double bx, double bz) =>
            p.Add(Box([ax, y, az], [bx, y + 0.003, bz], HatchHazard, detail: true, shape: Kw(("finish", "hazard paint"), ("paintable", false))));
        void Trim(double ax, double az, double bx, double bz) =>
            p.Add(Box([ax, y + 0.003, az], [bx, y + 0.005, bz], HatchTrim, detail: true, shape: Kw(("paintable", false))));
        if (skip != "-z") { Band(x0 - band, z0 - band, x1 + band, z0); Trim(x0, z0 - 0.025, x1, z0 - 0.008); }
        if (skip != "+z") { Band(x0 - band, z1, x1 + band, z1 + band); Trim(x0, z1 + 0.008, x1, z1 + 0.025); }
        if (skip != "-x") { Band(x0 - band, z0 + 0.001, x0, z1 - 0.001); Trim(x0 - 0.025, z0, x0 - 0.008, z1); }
        if (skip != "+x") { Band(x1, z0 + 0.001, x1 + band, z1 - 0.001); Trim(x1 + 0.008, z0, x1 + 0.025, z1); }
    }

    private static Part Handle()
    {
        var p = new Part("Door Handle", Small, Section, "On anything hung on a hinge: what F takes hold of. A panel works without one; this shows where.", tool: "wrench", color: Grip, group: Group);
        p.Add(Box([-0.03, 0, -0.08], [0.03, 0.012, 0.08], Steel));
        p.Add(Box([-0.015, 0.012, -0.07], [0.015, 0.04, -0.04], Grip, detail: true));
        p.Add(Box([-0.015, 0.012, 0.04], [0.015, 0.04, 0.07], Grip, detail: true));
        p.Add(Box([-0.018, 0.04, -0.07], [0.018, 0.065, 0.07], Grip, detail: true, shape: Use));
        p.Sock("base", [0, 0, 0], "-y");
        p.Stats(1.5, 40, 3, 0.5);
        return p;
    }

    public void Make(PartOutput o)
    {
        var parts = new List<(string, Part)> { ("", Hinge("Door Hinge", 0.5, 3, 3, 120)), ("", Hinge("Long Door Hinge", 2.0, 7, 9, 240)), ("", Handle()) };
        foreach (var kind in Kinds)
        {
            foreach (var (w, h) in Openings)
                parts.Add((kind.Word, Door(kind, w, h)));
            foreach (var (w, h) in EventOpenings)
                parts.Add(("Event" + kind.Word, SwingDoor(kind, w, h)));
            foreach (var side in new[] { "Left", "Right" })
                parts.Add(("Event" + kind.Word, DoubleLeaf(kind, 1.5, 2.25, side)));
        }
        foreach (var (w, h) in new[] { (1.25, 2.0), (1.5, 2.25) })
            parts.Add(("EventSteel", Bulkhead(w, h)));
        foreach (var (w, h) in EventOpenings)
            parts.Add(("EventSteel", SlideDoor(w, h)));
        foreach (var (across, along) in HatchSizes)
            parts.Add(("EventSteel", DeckHatch(across, along)));
        WriteParts(o, Out, parts);
    }
}
