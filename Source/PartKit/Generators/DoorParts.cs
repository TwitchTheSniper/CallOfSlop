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
/// door's height is measured from the same middle, so a hinge at the opening's mid-height centres it. Recipes come from tools/constructs/assembling.py.
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
        WriteParts(o, Out, parts);
    }
}
