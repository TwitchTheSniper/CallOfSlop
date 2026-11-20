using static CallOfSlop.PartKit.PartKitMath;
using K = CallOfSlop.PartKit.Generators.FittingLookParts;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The commoners' style, round 3 (Karl, 2026-11-20: "Another 25, but use feedback, polish kept items and go again. Think more
/// usable gameplay items"). He kept the water tote, oxygen generator, radio set, jump seat, jacked strut, old fridge, bucket head
/// and parts bins; rated the bulkhead door "top tier"; called the floor hatch, fold table, shower and outer airlock door "too
/// clean", asked for the airlock door to take after the bulkhead door with a window, the telescope to become a periscope and the
/// intercom's bars to cover its grille properly. So these 25 are things a crew uses, and most work as parts do: lockers and
/// crates hold items, seats and the bunk seat a rider, lamps light, switches and the intercom's talk bar drive a wire, the fuel
/// tanks hold fuel and the battery bank stores power. The doors and the periscope are looks only for now.
/// </summary>
public sealed partial class CommonerStyleParts
{
    // ---- Round 3's kit

    /// <summary>A free-standing piece for round 3 (the look's hidden solid fills its bounds).</summary>
    private static Part F3(string name, string desc, double[] mn, double[] mx, Action<K.Kit> draw, bool wall = true, string role = "frame")
    {
        var k = Free("Commoner " + name, $"Commoner {name.ToLowerInvariant()} (draft, set 3): {desc}", mn, mx, role: role, wall: wall);
        draw(k);
        return k.P;
    }

    /// <summary>A storage piece: a hollow hidden shell t thick round the bounds (what a hand or a round meets) with one slot inside.</summary>
    private static Part Store(string name, string desc, double[] mn, double[] mx, Action<K.Kit> draw, bool wall = true, double t = 0.03, string set = "set 3")
    {
        var p = new Part("Commoner " + name, Small, Section, $"Commoner {name.ToLowerInvariant()} (draft, {set}): {desc}", role: "storage", tool: "wrench", material: "steel", color: Cream, group: Group);
        p.Set(("finish", "worn paint"), ("collider", "boxes"));
        double x0 = mn[0], y0 = mn[1], z0 = mn[2], x1 = mx[0], y1 = mx[1], z1 = mx[2];
        foreach (var (a, b) in new (double[], double[])[]
        {
            ([x0, y0, z0], [x1, y0 + t, z1]), ([x0, y1 - t, z0], [x1, y1, z1]),
            ([x0, y0 + t, z0], [x1, y1 - t, z0 + t]), ([x0, y0 + t, z1 - t], [x1, y1 - t, z1]),
            ([x0, y0 + t, z0 + t], [x0 + t, y1 - t, z1 - t]), ([x1 - t, y0 + t, z0 + t], [x1, y1 - t, z1 - t]),
        })
            p.Add(Box([.. a.Select(R)], [.. b.Select(R)], Cream, hidden: true));
        p.Sock("bottom", [R(0.5 * (x0 + x1)), y0, R(0.5 * (z0 + z1))], "-y");
        if (wall)
            p.Sock("wall", [R(0.5 * (x0 + x1)), R(y0 + Math.Min(y1 - y0, 0.25) / 2), z0], "-z");
        var g = t + 0.01;
        p.Set(("storage", new PyDict { ["slots"] = Py.L(K.Slot([R(x0 + g), R(y0 + g), R(z0 + g)], [R(x1 - g), R(y1 - g), R(z1 - g)])) }));
        p.Stats(40, 220, 2, 2.0);
        var k = new K.Kit(p);
        draw(k);
        return p;
    }

    /// <summary>Makes a seat of a piece whose cushion top is at y (a rider sits 0.55 m up by default).</summary>
    private static Part Seat(Part p, double cushion, bool bunk = false)
    {
        p.Set(("seat", bunk ? Kw(("at", Py.L(0, R(cushion - 0.55), 0)), ("bunk", true)) : Kw(("at", Py.L(0, R(cushion - 0.55), 0)))));
        return p;
    }

    /// <summary>Makes a lamp of a piece: lit off the ship's mains, warm by default.</summary>
    private static Part Lamp(Part p, double watts = 60)
    {
        p.Set(("role", "logic"), ("logic", Kw(("kind", "lamp"), ("mains", true))), ("power", watts), ("config", Py.L(
            Kw(("key", "colour"), ("type", "colour"), ("label", "Colour"), ("group", "Light"), ("default", Bulb), ("tooltip", "The colour it glows when lit.")),
            Kw(("key", "brightness"), ("label", "Brightness"), ("group", "Light"), ("unit", "%"), ("min", 10), ("max", 200), ("step", 1), ("default", 100), ("tooltip", "How bright it glows.")),
            Kw(("key", "blink"), ("label", "Blink"), ("group", "Light"), ("unit", "Hz"), ("min", 0), ("max", 10), ("step", 0.1), ("default", 0), ("tooltip", "Flashes this many times a second while lit (0: steady).")))));
        return p;
    }

    /// <summary>Makes a switch of a piece (F flips it; its output drives a wire).</summary>
    private static Part Switch(Part p)
    {
        p.Set(("role", "logic"), ("logic", Kw(("kind", "switch"))), ("config", Py.L(
            Kw(("key", "startOn"), ("label", "Starts on"), ("group", "General"), ("default", false), ("tooltip", "A newly built switch starts in the on position.")))));
        return p;
    }

    /// <summary>Makes a push button of a piece (on while held, or latched).</summary>
    private static Part Button(Part p)
    {
        p.Set(("role", "logic"), ("logic", Kw(("kind", "button"))), ("config", Py.L(
            Kw(("key", "latched"), ("label", "Latched"), ("group", "General"), ("default", false), ("tooltip", "On: each press turns it on or off. Off: it springs back after its hold time.")),
            Kw(("key", "hold"), ("label", "Stays on for"), ("group", "General"), ("unit", "s"), ("min", 0.05), ("max", 30), ("step", 0.05), ("default", 0.5), ("tooltip", "How long a press keeps it on (when not latched).")))));
        return p;
    }

    /// <summary>Makes a fuel tank of a piece, its outlet on a face.</summary>
    private static Part Fuel(Part p, double litres, double[] at, string face)
    {
        p.Set(("role", "fueltank"), ("fuelTank", Kw(("capacity", litres))), ("ports", Py.L(
            Kw(("name", "Fuel out"), ("type", "pipe"), ("dir", "out"), ("fluid", "fuel"), ("at", at.Select(v => (object?)R(v)).ToList()), ("face", face)))));
        return p;
    }

    /// <summary>A ribbed metal door slab face on +z at z, from (x0, y0) to (x1, y1), with a pressed rib down each side.</summary>
    private static void RibsZ(K.Kit k, double x0, double y0, double x1, double y1, double z, int n, string col)
    {
        for (var i = 0; i < n; i++)
        {
            var y = y0 + (y1 - y0) * (i + 0.5) / n;
            k.Box(x0, y - 0.012, z, x1, y + 0.012, z + 0.012, col, "worn paint");
        }
    }

    // ---- The 25

    private static IEnumerable<Part> Set3()
    {
        // ===== Storage =====
        yield return Store("Crew Locker", "a tall dented steel locker in faded avocado, its door sprung at the top corner and held with a bungee, a name strip on tape, louvres choked with fluff, a padlock hasp with no padlock and a pin-up curling on the inside of the door.", [-0.3, 0, -0.25], [0.3, 1.9, 0.25], k =>
        {
            k.Box(-0.29, 0.06, 0.25, 0.27, 1.84, 0.262, Avocado, "rusty paint");
            k.Box(-0.29, 0.0, -0.24, 0.29, 0.06, 0.255, Grime, "dirty steel");
            LouvreZ(k, -0.2, 0.15, 1.55, 1.75, 0.262, 5, Avocado);
            LouvreZ(k, -0.2, 0.15, 0.12, 0.3, 0.262, 4, Avocado);
            k.Box(-0.14, 1.4, 0.262, 0.14, 1.45, 0.275, Tape, "fabric");
            k.Box(0.2, 0.85, 0.262, 0.24, 1.05, 0.29, Chrome, "steel");
            k.Box(0.2, 1.1, 0.262, 0.25, 1.16, 0.282, Grime, "steel"); // (the empty hasp)
            k.Hull([[0.27, 1.84, 0.262], [0.27, 1.6, 0.262], [0.29, 1.84, 0.3], [0.27, 1.84, 0.275], [0.27, 1.6, 0.272]], Avocado, "rusty paint"); // (the sprung corner)
            Tube(k, 1, 0.31, 0.2, 1.3, 1.8, 0.008, Signal, "rubber", 6); // (the bungee)
            k.Hull([[-0.25, 0.4, 0.262], [-0.1, 0.38, 0.262], [-0.12, 0.6, 0.262], [-0.24, 0.58, 0.262], [-0.18, 0.5, 0.274]], "#6e6a5e", "rusty paint");
            StreakZ(k, -0.25, 1.2, 1.8, 0.262, 0.03);
            StripesX(k, 0.3, -0.2, 0.2, 1.2, 1);
        });
        yield return Store("Footlocker", "a squat footlocker at the end of a bunk, olive canvas over plywood with leather corners worn through, a stencilled number half scrubbed off, two buckled straps and a tin mug hooked on the handle.", [-0.4, 0, -0.25], [0.4, 0.45, 0.25], k =>
        {
            k.Box(-0.39, 0.02, -0.24, 0.39, 0.4, 0.24, Olive, "fabric");
            k.Box(-0.4, 0.4, -0.25, 0.4, 0.45, 0.25, "#4f5434", "fabric");
            foreach (var sx in new[] { -1.0, 1.0 })
                foreach (var sz in new[] { -1.0, 1.0 })
                    k.Box(sx * 0.4 - (sx > 0 ? 0.06 : -0.06) - 0.003 * sx, 0.0, sz * 0.25 - (sz > 0 ? 0.06 : -0.06), sx * 0.403, 0.453, sz * 0.253, Vinyl, "fabric");
            foreach (var x in new[] { -0.2, 0.2 })
            {
                k.Box(x - 0.025, 0.05, 0.242, x + 0.025, 0.455, 0.252, Brown, "fabric");
                k.Box(x - 0.025, 0.453, -0.2, x + 0.025, 0.462, 0.252, Brown, "fabric");
                k.Box(x - 0.03, 0.3, 0.252, x + 0.03, 0.34, 0.262, Brass, "brass");
            }
            Stencil(k, -0.1, 0.15, 0.1, 0.25, 0.242, Ceramic);
            Tube(k, 0, 0.3, 0.27, -0.06, 0.06, 0.008, Grime, "steel", 6);
            k.Hull(Frustum(1, 0.0, 0.3, 0.22, 0.04, 0.3, 0.04, 8), Chrome, "steel");
        });
        yield return Store("Scrap Crate", "a cargo crate knocked together from three other crates: a rusty steel frame, a wood side, a sheet of a sign for a lid with half a word on it, a ratchet strap round it and a chalked tally of what's inside.", [-0.5, 0, -0.5], [0.5, 0.8, 0.5], k =>
        {
            Cage(k, -0.5, 0.0, -0.5, 0.5, 0.8, 0.5, Steel, 0.04);
            k.Box(-0.47, 0.04, -0.474, 0.47, 0.76, -0.462, "#7a7d72", "rusty paint");
            k.Box(-0.47, 0.04, 0.462, 0.47, 0.76, 0.474, Wood, "wood");
            for (var i = 0; i < 3; i++)
                k.Box(-0.47, 0.1 + i * 0.25, 0.474, 0.47, 0.2 + i * 0.25, 0.486, WoodDark, "wood");
            k.Box(-0.474, 0.04, -0.46, -0.462, 0.76, 0.46, Orange, "worn paint");
            k.Box(0.462, 0.04, -0.46, 0.474, 0.76, 0.46, "#5d6b74", "rusty paint");
            k.Box(-0.47, 0.762, -0.47, 0.47, 0.776, 0.47, Ceramic, "worn paint");
            k.Box(-0.4, 0.776, -0.1, 0.1, 0.787, 0.12, Signal, "worn paint"); // (half a word on the old sign)
            k.Box(-0.506, 0.38, -0.506, 0.506, 0.43, 0.506, Mustard, "fabric"); // (the ratchet strap)
            k.Box(0.2, 0.36, 0.506, 0.32, 0.45, 0.53, Grime, "steel");
            for (var i = 0; i < 4; i++)
                k.Box(-0.38 + i * 0.05, 0.5, -0.49, -0.366 + i * 0.05, 0.62, -0.474, Ceramic, "plastic");
            k.Box(-0.4, 0.55, -0.494, -0.18, 0.562, -0.478, Ceramic, "plastic");
        });
        yield return Store("Tool Cabinet", "a rolling tool cabinet, red paint gone pink and scraped back to steel at the corners, drawers that don't shut flush, one handle replaced with a bent bolt, a vice clamped on its top and a rag hanging from the side.", [-0.35, 0, -0.25], [0.35, 1.0, 0.25], k =>
        {
            foreach (var (x, z) in new[] { (-0.28, -0.18), (0.28, -0.18), (-0.28, 0.18), (0.28, 0.18) })
                k.Hull(Frustum(0, 0.04, z, x - 0.02, 0.04, x + 0.02, 0.04, 8), Rubber, "rubber");
            k.Box(-0.34, 0.08, -0.24, 0.34, 0.92, 0.24, "#a8574c", "worn paint");
            double[] gaps = [0.0, 0.006, 0.0, 0.012, 0.003];
            for (var i = 0; i < 5; i++)
            {
                var y = 0.12 + i * 0.155;
                k.Box(-0.31, y, 0.24 + gaps[i], 0.31, y + 0.13, 0.252 + gaps[i], i == 3 ? "#b86a5c" : "#9e4e44", "worn paint");
                if (i == 1)
                    Tube(k, 0, y + 0.08, 0.27 + gaps[i], -0.1, 0.1, 0.008, Grime, "rusty paint", 6); // (the bent bolt)
                else
                    k.Box(-0.12, y + 0.07, 0.252 + gaps[i], 0.12, y + 0.09, 0.27 + gaps[i], Chrome, "steel");
            }
            foreach (var sx in new[] { -1.0, 1.0 })
                k.Box(sx * 0.34 - (sx > 0 ? 0.04 : 0), 0.85, 0.24, sx * 0.34 + (sx < 0 ? 0.04 : 0) + 0.003 * sx, 0.92, 0.244, Chrome, "steel"); // (scraped corners)
            k.Box(-0.345, 0.92, -0.245, 0.345, 0.95, 0.245, Grime, "rubber matting");
            k.Box(-0.25, 0.95, -0.05, -0.05, 1.0, 0.1, "#4f6a7a", "worn paint"); // (the vice)
            Tube(k, 2, -0.15, 0.98, 0.1, 0.2, 0.008, Chrome, "steel", 6);
            k.Box(0.342, 0.4, -0.1, 0.354, 0.75, 0.05, Ceramic, "fabric");
        });
        yield return Store("Medkit Box", "a wall first-aid box in chipped cream enamel with a red cross painted over twice, a cracked clear window on the front, a tally of plasters on the side in marker and a seal of tape that's been cut and re-taped.", [-0.25, 0.9, -0.25], [0.25, 1.3, -0.08], k =>
        {
            k.Box(-0.245, 0.905, -0.08, 0.245, 1.295, -0.07, Cream, "worn paint");
            k.Box(-0.05, 1.12, -0.07, 0.05, 1.26, -0.056, Signal, "worn paint");
            k.Box(-0.12, 1.16, -0.07, 0.12, 1.22, -0.062, Signal, "worn paint");
            k.Box(0.08, 0.95, -0.07, 0.2, 1.08, -0.064, "#b8c8c2", "plastic");
            k.Box(-0.24, 1.0, -0.066, -0.17, 1.2, -0.058, Tape, "fabric");
            k.Box(0.245, 0.95, -0.22, 0.258, 1.25, -0.1, Ceramic, "plastic");
            k.Box(-0.05, 0.86, -0.18, 0.05, 0.905, -0.12, Chrome, "steel");
            k.Hull([[-0.24, 0.93, -0.07], [-0.15, 0.91, -0.07], [-0.16, 1.0, -0.07], [-0.2, 0.96, -0.058]], "#6e6a5e", "rusty paint");
        });
        yield return Store("Galley Cupboard", "a wall cupboard over the galley, wood-grain laminate peeling at the edges, a sliding perspex front scratched cloudy with tins and a jar of something behind it, a rail along the front to stop things flying and a calendar three years out of date.", [-0.5, 1.3, -0.25], [0.5, 1.8, 0.05], k =>
        {
            k.Box(-0.5, 1.3, 0.05, 0.5, 1.33, 0.062, WoodDark, "wood");
            k.Box(-0.5, 1.77, 0.05, 0.5, 1.8, 0.062, WoodDark, "wood");
            k.Box(-0.505, 1.3, -0.25, -0.49, 1.8, 0.06, Wood, "wood");
            k.Box(0.49, 1.3, -0.25, 0.505, 1.8, 0.06, Wood, "wood");
            k.Box(-0.48, 1.34, 0.062, 0.02, 1.76, 0.072, "#c8d0c8", "plastic");
            k.Box(-0.02, 1.34, 0.074, 0.48, 1.76, 0.084, "#bcc4bc", "plastic");
            foreach (var (x, c) in new[] { (-0.38, Signal), (-0.28, Mustard), (-0.16, BottleGreen) })
                k.Hull(Frustum(1, x, -0.05, 1.333, 0.04, 1.45, 0.04, 8), c, "worn paint");
            k.Hull(Frustum(1, 0.3, -0.05, 1.333, 0.05, 1.5, 0.05, 8), "#b8a060", "plastic");
            Tube(k, 0, 1.4, 0.11, -0.48, 0.48, 0.008, Chrome, "steel", 6);
            foreach (var x in new[] { -0.44, 0.44 })
                k.Box(x - 0.01, 1.36, 0.084, x + 0.01, 1.41, 0.11, Chrome, "steel");
            k.Box(0.505, 1.4, -0.2, 0.517, 1.72, -0.02, Ceramic, "plastic");
            k.Box(0.505, 1.66, -0.2, 0.52, 1.72, -0.02, Signal, "plastic");
            k.Hull([[-0.5, 1.33, 0.062], [-0.44, 1.33, 0.062], [-0.5, 1.4, 0.062], [-0.5, 1.33, 0.07]], "#c9b88a", "plastic"); // (peeling laminate)
        });
        yield return Store("Scrap Drum", "a steel drum for scrap and rags with its top cut off and a dented lid on a chain, rust rings where it's stood in water, a hand-painted SCRAP and bits of cable and pipe sticking up.", [-0.3, 0, -0.3], [0.3, 0.9, 0.3], k =>
        {
            k.Hull(Frustum(1, 0, 0, 0.004, 0.29, 0.86, 0.29, 12), "#5d6b74", "rusty paint");
            foreach (var y in new[] { 0.05, 0.3, 0.6 })
                k.Hull(Frustum(1, 0, 0, y, 0.298, y + 0.03, 0.298, 12), Grime, "rusty paint");
            k.Hull(Frustum(1, 0, 0, 0.0, 0.3, 0.04, 0.3, 12), Rust, "rust");
            Stencil(k, -0.12, 0.38, 0.12, 0.5, 0.29, Ceramic);
            k.Hull([[-0.05, 0.86, 0.05], [0.0, 0.86, 0.1], [0.08, 1.05, 0.02], [0.05, 1.08, -0.02], [0.02, 0.86, 0.0]], Copper, "copper");
            Tube(k, 1, -0.1, -0.08, 0.7, 1.0, 0.018, Chrome, "steel", 6);
            k.Hull([[0.31, 0.2, -0.25], [0.33, 0.2, -0.25], [0.31, 0.75, -0.1], [0.33, 0.75, -0.1], [0.31, 0.2, 0.25], [0.33, 0.2, 0.25], [0.31, 0.75, 0.1], [0.33, 0.75, 0.1]], "#6e7880", "rusty paint"); // (the lid leaning on its side)
            Tube(k, 1, 0.31, 0.0, 0.75, 0.86, 0.006, Chrome, "steel", 4);
        });

        // ===== Fuel and power =====
        yield return Fuel(F3("Jerrycan Rack", "four jerrycans in a welded rack on the wall, each a different colour and age, one strapped with a bungee because its clip broke, a hand pump hose coiled on a hook and a funnel cut from a bottle.", [-0.5, 0, -0.25], [0.5, 0.55, 0.0], k =>
        {
            k.Box(-0.48, 0.0, -0.25, 0.48, 0.03, -0.0, Steel, "rusty paint");
            k.Box(-0.48, 0.03, -0.25, 0.48, 0.5, -0.22, Steel, "rusty paint");
            string[] cols = [Signal, Olive, Mustard, "#5d6b74"];
            for (var i = 0; i < 4; i++)
            {
                var x = -0.4 + i * 0.22;
                k.Box(x - 0.08, 0.03, -0.215, x + 0.08, 0.43 - (i % 2) * 0.02, -0.04, cols[i], i == 2 ? "rusty paint" : "worn paint");
                k.Box(x - 0.08, 0.2, -0.04, x + 0.08, 0.26, -0.033, Grime, "steel");
                k.Box(x - 0.05, 0.43 - (i % 2) * 0.02, -0.17, x + 0.03, 0.47 - (i % 2) * 0.02, -0.1, cols[i], "worn paint");
                k.Hull(Frustum(1, x + 0.05, -0.08, 0.43 - (i % 2) * 0.02, 0.02, 0.47 - (i % 2) * 0.02, 0.018, 6), Grime, "plastic");
            }
            Tube(k, 0, 0.3, -0.025, 0.1, 0.32, 0.007, Signal, "rubber", 6);
            Pipe(k, Rubber, 0.012, "rubber", (0.46, 0.45, -0.18), (0.46, 0.1, -0.18));
            k.Hull(Frustum(1, 0.46, -0.1, 0.45, 0.03, 0.53, 0.05, 8), "#b8c8c2", "plastic");
        }), 80, [0.5, 0.2, -0.12], "+x");
        yield return Fuel(F3("Patched Fuel Tank", "a fat upright fuel tank that's been holed and mended: welded patches in bare steel and primer, a sight tube gone yellow, a valve wheel painted red, drips down from a weep at a seam and a tin tray under it to catch them.", [-0.45, 0, -0.45], [0.45, 1.6, 0.45], k =>
        {
            k.Box(-0.44, 0.0, -0.44, 0.44, 0.04, 0.44, Grime, "dirty steel");
            TankUp(k, 0.0, 0.0, 0.08, 1.55, 0.38, "#8a7a52", "rusty paint", 12);
            foreach (var (x, z) in new[] { (-0.3, -0.3), (0.3, -0.3), (-0.3, 0.3), (0.3, 0.3) })
                k.Box(x - 0.03, 0.04, z - 0.03, x + 0.03, 0.3, z + 0.03, Steel, "rusty paint");
            k.Box(-0.18, 0.6, 0.37, 0.02, 0.85, 0.392, "#9a9c98", "steel"); // (a bare patch)
            k.Box(0.08, 1.05, 0.36, 0.26, 1.25, 0.384, "#8a3f2e", "worn paint"); // (a primer patch)
            Tube(k, 1, 0.3, 0.26, 0.3, 1.3, 0.018, "#c8b878", "plastic", 6);
            foreach (var y in new[] { 0.3, 1.3 })
                k.Box(0.28, y - 0.02, 0.24, 0.32, y + 0.02, 0.29, Chrome, "steel");
            Pipe(k, Steel, 0.03, "steel", (0.0, 0.25, 0.37), (0.0, 0.25, 0.42), (0.44, 0.25, 0.42));
            Wheel(k, 2, 0.0, 0.42, 0.42, 0.06, Signal);
            Tube(k, 2, 0.0, 0.42, 0.37, 0.41, 0.01, Chrome, "steel", 6);
            StreakZ(k, -0.15, 0.3, 0.9, 0.384, 0.04);
            k.Box(-0.3, 0.04, 0.2, 0.0, 0.07, 0.44, Chrome, "steel"); // (the drip tray)
        }, wall: false), 600, [0.45, 0.25, 0.42], "+x");
        yield return M3Battery();

        // ===== Seats and beds =====
        yield return Seat(F3("Captain's Chair", "a high-backed captain's chair from an old coach, plaid fabric worn to the weave, a sheepskin thrown over the seat, duct tape on both arms, a swivel that squeaks and a cup holder screwed on the right.", [-0.35, 0, -0.35], [0.35, 1.3, 0.35], k =>
        {
            k.Box(-0.26, 0.0, -0.26, 0.26, 0.04, 0.26, Grime, "dirty steel");
            Tube(k, 1, 0.0, 0.0, 0.04, 0.32, 0.05, Chrome, "steel", 8);
            k.Box(-0.22, 0.32, -0.22, 0.22, 0.36, 0.22, Grime, "dirty steel");
            k.Profile(-0.28, 0.28, [(0.36, -0.28), (0.36, 0.3), (0.42, 0.33), (0.5, 0.3), (0.5, -0.26)], "#7a5a3a", "fabric");
            k.Hull([[-0.22, 0.5, -0.2], [0.22, 0.5, -0.2], [-0.22, 0.5, 0.26], [0.22, 0.5, 0.26], [-0.2, 0.53, -0.18], [0.2, 0.53, -0.18], [-0.2, 0.53, 0.24], [0.2, 0.53, 0.24]], "#d8ccb0", "fabric"); // (the sheepskin)
            k.Profile(-0.28, 0.28, [(0.5, -0.34), (0.5, -0.22), (1.25, -0.28), (1.3, -0.33), (1.25, -0.35)], "#7a5a3a", "fabric");
            foreach (var (y0, y1, c) in new[] { (0.75, 0.8, Brown), (0.8, 0.84, Orange), (0.84, 0.88, Mustard) })
                k.Hull([[-0.27, y0, -0.2 - (y0 - 0.5) * 0.08], [0.27, y0, -0.2 - (y0 - 0.5) * 0.08], [-0.27, y1, -0.2 - (y1 - 0.5) * 0.08], [0.27, y1, -0.2 - (y1 - 0.5) * 0.08],
                        [-0.27, y0, -0.214 - (y0 - 0.5) * 0.08], [0.27, y0, -0.214 - (y0 - 0.5) * 0.08], [-0.27, y1, -0.214 - (y1 - 0.5) * 0.08], [0.27, y1, -0.214 - (y1 - 0.5) * 0.08]], c, "fabric");
            foreach (var sx in new[] { -1.0, 1.0 })
            {
                k.Box(sx * 0.28 - (sx > 0 ? 0 : 0.06), 0.65, -0.24, sx * 0.28 + (sx > 0 ? 0.06 : 0), 0.7, 0.24, Vinyl, "rubber");
                k.Box(sx * 0.31 - 0.032, 0.5, -0.2, sx * 0.31 + 0.032, 0.65, -0.16, Grime, "steel");
                k.Box(sx * 0.31 - 0.035, 0.702, -0.05, sx * 0.31 + 0.035, 0.714, 0.1, Tape, "fabric");
            }
            k.Hull(Frustum(1, 0.38, 0.2, 0.6, 0.04, 0.706, 0.045, 8), Grime, "plastic");
        }, wall: false, role: "seat"), 0.53);
        yield return Seat(F3("Crew Seat", "a bus seat bolted to a steel frame on the deck, brown vinyl split and taped, a lap belt with a bent buckle, a seat-back pocket with a manual stuffed in it and someone's initials scratched in the grab bar.", [-0.3, 0, -0.35], [0.3, 1.1, 0.3], k =>
        {
            foreach (var x in new[] { -0.24, 0.24 })
            {
                k.Box(x - 0.02, 0.0, -0.3, x + 0.02, 0.4, -0.26, Steel, "rusty paint");
                k.Box(x - 0.02, 0.0, 0.2, x + 0.02, 0.4, 0.24, Steel, "rusty paint");
                k.Box(x - 0.022, 0.0, -0.3, x + 0.022, 0.03, 0.24, Grime, "steel");
            }
            k.Box(-0.27, 0.4, -0.28, 0.27, 0.43, 0.26, Steel, "rusty paint");
            k.Profile(-0.27, 0.27, [(0.43, -0.28), (0.43, 0.26), (0.48, 0.28), (0.52, 0.25), (0.52, -0.25)], Vinyl, "rubber");
            k.Profile(-0.265, 0.265, [(0.48, -0.34), (0.48, -0.24), (1.02, -0.29), (1.05, -0.34)], Vinyl, "rubber");
            k.Box(-0.1, 0.52, -0.05, 0.08, 0.531, 0.15, Tape, "fabric");
            k.Box(-0.26, 0.7, -0.35, 0.26, 0.9, -0.336, "#5a3f2c", "rubber"); // (the pocket)
            k.Box(-0.15, 0.85, -0.352, 0.05, 0.95, -0.345, Ceramic, "plastic");
            Tube(k, 0, 1.06, -0.32, -0.25, 0.25, 0.014, Chrome, "steel", 6);
            k.Box(-0.271, 0.52, -0.05, 0.2, 0.535, 0.01, "#4a5a6a", "fabric");
            k.Box(0.2, 0.522, -0.06, 0.25, 0.54, 0.02, Chrome, "steel");
        }, wall: false, role: "seat"), 0.52);
        yield return Seat(F3("Bench Seat", "a long padded bench along the wall, three cushions that don't match, the middle one sagging, a blanket folded at one end and a lifting lid underneath (the hinges show) with a spare boot sticking out.", [-0.7, 0, -0.25], [0.7, 0.95, 0.25], k =>
        {
            k.Box(-0.7, 0.0, -0.25, 0.7, 0.38, 0.22, Wood, "wood");
            k.Box(-0.69, 0.02, 0.22, 0.69, 0.36, 0.232, WoodDark, "wood");
            foreach (var x in new[] { -0.4, 0.0, 0.4 })
                k.Box(x - 0.03, 0.38, 0.215, x + 0.03, 0.4, 0.235, Chrome, "steel");
            string[] cols = [Vinyl, Teal, Orange];
            for (var i = 0; i < 3; i++)
            {
                double x0 = -0.69 + i * 0.465, sag = i == 1 ? 0.025 : 0.0;
                k.Hull([[x0 + 0.005, 0.38, -0.24], [x0 + 0.455, 0.38, -0.24], [x0 + 0.005, 0.38, 0.22], [x0 + 0.455, 0.38, 0.22],
                        [x0 + 0.015, 0.48, -0.23], [x0 + 0.445, 0.48, -0.23], [x0 + 0.015, 0.47, 0.21], [x0 + 0.445, 0.47, 0.21], [x0 + 0.23, 0.48 - sag, 0.0]], cols[i], i == 1 ? "fabric" : "rubber");
                k.Profile(x0 + 0.01, x0 + 0.45, [(0.48, -0.25), (0.48, -0.17), (0.92, -0.2), (0.95, -0.25)], cols[2 - i], i == 0 ? "fabric" : "rubber");
            }
            k.Box(0.45, 0.48, -0.15, 0.68, 0.56, 0.15, "#7a3a3a", "fabric");
            k.Hull([[0.1, 0.04, 0.232], [0.22, 0.04, 0.232], [0.1, 0.2, 0.232], [0.22, 0.04, 0.33], [0.12, 0.12, 0.3]], Rubber, "rubber"); // (the boot)
        }, role: "seat"), 0.47);
        yield return Seat(F3("Pipe Bunk", "a bunk made of scaffold pipe and clamps with a mattress that's seen better decades, a sleeping bag half unzipped, a reading light clipped to the frame, a shelf from a plank with a paperback face down and socks drying on the rail.", [-1.0, 0, -0.45], [1.0, 1.3, 0.45], k =>
        {
            foreach (var x in new[] { -0.97, 0.97 })
                foreach (var z in new[] { -0.42, 0.42 })
                    Tube(k, 1, x, z, 0.0, 1.25, 0.024, Steel, "rusty paint", 6);
            foreach (var z in new[] { -0.42, 0.42 })
                Tube(k, 0, 0.36, z, -0.97, 0.97, 0.02, Steel, "rusty paint", 6);
            foreach (var x in new[] { -0.97, 0.97 })
            {
                Tube(k, 2, x, 0.36, -0.42, 0.42, 0.02, Steel, "rusty paint", 6);
                Tube(k, 2, x, 1.0, -0.42, 0.42, 0.02, Steel, "rusty paint", 6);
                foreach (var z in new[] { -0.42, 0.42 })
                    k.Box(x - 0.04, 0.33, z - 0.04, x + 0.04, 0.39, z + 0.04, Grime, "steel");
            }
            k.Box(-0.93, 0.38, -0.39, 0.93, 0.4, 0.39, Wood, "wood");
            k.Box(-0.92, 0.4, -0.38, 0.92, 0.52, 0.38, "#b4a882", "fabric");
            k.Hull([[-0.4, 0.52, -0.37], [0.9, 0.52, -0.37], [-0.4, 0.52, 0.37], [0.9, 0.52, 0.37], [-0.3, 0.58, -0.3], [0.85, 0.58, -0.3], [-0.3, 0.57, 0.3], [0.85, 0.57, 0.3]], "#3f5a72", "fabric");
            k.Hull([[-0.42, 0.52, -0.1], [-0.2, 0.52, 0.37], [-0.6, 0.52, 0.3], [-0.45, 0.6, 0.2], [-0.32, 0.55, 0.32]], "#c94a2a", "fabric"); // (the bag's lining folded back)
            k.Box(-0.9, 0.52, -0.3, -0.6, 0.62, 0.3, Ceramic, "fabric");
            k.Box(-0.95, 0.9, -0.45, -0.3, 0.93, -0.3, WoodDark, "wood");
            k.Box(-0.7, 0.93, -0.42, -0.55, 0.96, -0.34, Signal, "plastic");
            CagedBulbZ(k, 0.85, 0.95, 0.44);
            k.Box(0.2, 0.9, 0.436, 0.32, 1.0, 0.452, Mustard, "fabric");
            k.Box(0.4, 0.9, 0.436, 0.5, 0.98, 0.452, "#7a8a5a", "fabric");
        }, wall: false, role: "seat"), 0.58, bunk: true);

        // ===== Lamps =====
        yield return Lamp(F3("Cage Lamp", "a ceiling cage lamp off an old ship: a cast body with flaking cream paint, a ribbed glass dome yellowed, a steel cage dented on one side, a warm bulb and a bit of wire wrapped round where the screw went missing.", [-0.15, 1.95, -0.15], [0.15, 2.4, 0.15], k =>
        {
            k.Hull(Frustum(1, 0, 0, 2.3, 0.14, 2.4, 0.13, 10), Cream, "worn paint");
            k.Hull(Frustum(1, 0, 0, 2.27, 0.11, 2.3, 0.12, 10), Grime, "steel");
            k.Hull(Frustum(1, 0, 0, 2.07, 0.07, 2.27, 0.1, 10), Bulb, "", glow: true);
            for (var i = 0; i < 4; i++)
            {
                double a = i * Math.PI / 2 + 0.4, dx = Math.Cos(a) * (i == 1 ? 0.09 : 0.115), dz = Math.Sin(a) * (i == 1 ? 0.09 : 0.115);
                k.Hull([[dx - 0.006, 2.27, dz - 0.006], [dx + 0.006, 2.27, dz + 0.006], [dx * 0.6 - 0.006, 2.02, dz * 0.6 - 0.006], [dx * 0.6 + 0.006, 2.02, dz * 0.6 + 0.006], [dx, 2.27, dz + 0.006], [dx * 0.6, 2.02, dz * 0.6 + 0.006]], Chrome, "steel");
            }
            Ring(k, 1, 0, 0, 2.15, 0.105, 0.012, Chrome, "steel", 8);
            k.Hull(Frustum(1, 0, 0, 1.99, 0.03, 2.03, 0.04, 8), Chrome, "steel");
            Tube(k, 0, 2.36, 0.141, 0.05, 0.12, 0.006, Copper, "copper", 4);
        }, wall: false));
        yield return Lamp(F3("Wall Sconce", "a wall sconce made from a car headlamp bezel on a bent bracket, a frosted shade cracked across and glued, the warm bulb behind, a pull-chain with a bead, and its flex stapled up the wall.", [-0.12, 1.4, -0.25], [0.12, 1.75, -0.05], k =>
        {
            k.Box(-0.06, 1.45, -0.25, 0.06, 1.6, -0.235, Chrome, "steel");
            k.Hull([[-0.015, 1.5, -0.235], [0.015, 1.5, -0.235], [-0.015, 1.55, -0.235], [0.015, 1.55, -0.235], [-0.015, 1.62, -0.13], [0.015, 1.62, -0.13], [-0.015, 1.66, -0.13], [0.015, 1.66, -0.13]], Grime, "steel");
            k.Hull(Frustum(2, 0.0, 1.63, -0.15, 0.08, -0.12, 0.1, 10), Chrome, "chrome");
            k.Hull(Frustum(2, 0.0, 1.63, -0.12, 0.085, -0.07, 0.07, 10), Bulb, "", glow: true);
            k.Hull([[-0.08, 1.6, -0.066], [0.07, 1.67, -0.066], [0.07, 1.655, -0.066], [-0.08, 1.585, -0.066], [-0.08, 1.6, -0.078], [0.07, 1.67, -0.078], [0.07, 1.655, -0.078], [-0.08, 1.585, -0.078]], Grime, "plastic"); // (the glued crack)
            Tube(k, 1, 0.07, -0.13, 1.45, 1.6, 0.003, Chrome, "steel", 4);
            k.Hull(Frustum(1, 0.07, -0.13, 1.42, 0.012, 1.45, 0.012, 6), Ceramic, "plastic");
            Tube(k, 1, -0.04, -0.235, 1.6, 1.75, 0.006, Ceramic, "plastic", 4);
            foreach (var y in new[] { 1.66, 1.72 })
                k.Box(-0.055, y, -0.25, -0.025, y + 0.012, -0.226, Chrome, "steel");
        }));
        yield return Lamp(F3("Work Light", "a halogen work light on a folding tripod, its yellow paint chipped, the guard bent so it doesn't sit straight, a lead with insulation tape at three places and a gaffer-taped switch.", [-0.35, 0, -0.35], [0.35, 1.6, 0.35], k =>
        {
            foreach (var (a, top) in new[] { (0.3, 0.97), (2.4, 0.974), (4.5, 0.978) })
                k.Hull([[-0.012, top, -0.012], [0.012, top, 0.012], [Math.Cos(a) * 0.33 - 0.012, 0.0, Math.Sin(a) * 0.33 - 0.012], [Math.Cos(a) * 0.33 + 0.012, 0.0, Math.Sin(a) * 0.33 + 0.012], [0.012, top, -0.012], [Math.Cos(a) * 0.33 + 0.012, 0.0, Math.Sin(a) * 0.33 - 0.012]], Mustard, "worn paint");
            Tube(k, 1, 0.0, 0.0, 0.9, 1.4, 0.016, Chrome, "steel", 6);
            k.Box(-0.04, 0.95, -0.04, 0.04, 1.0, 0.04, Grime, "steel");
            k.Box(-0.16, 1.38, -0.04, 0.16, 1.4, 0.04, Grime, "steel");
            k.Box(-0.15, 1.4, -0.08, 0.15, 1.58, 0.04, Mustard, "worn paint");
            k.Glow(-0.13, 1.42, 0.04, 0.13, 1.56, 0.05, Bulb);
            for (var i = 0; i < 4; i++)
                k.Hull([[-0.14 + i * 0.09, 1.405, 0.06], [-0.13 + i * 0.09, 1.405, 0.06], [-0.14 + i * 0.09 + 0.02 * (i == 2 ? 1 : 0), 1.575, 0.072], [-0.13 + i * 0.09 + 0.02 * (i == 2 ? 1 : 0), 1.575, 0.072], [-0.135 + i * 0.09, 1.49, 0.07]], Chrome, "steel");
            CableZ(k, 0.02, 0.15, 1.45, 0.3, 0.02, 0.0, Mustard);
            k.Box(0.05, 1.2, -0.03, 0.11, 1.26, 0.03, Tape, "fabric");
        }, wall: false), 150);
        yield return Lamp(F3("Bulkhead Lamp", "an oval bulkhead lamp in a cast frame bolted to the wall, three bars across a lens gone amber with age, a cracked corner with a dab of sealant, paint over the bolts from the last time someone painted the wall round it.", [-0.16, 1.5, -0.25], [0.16, 1.75, -0.15], k =>
        {
            k.Hull(Frustum(2, 0.0, 1.625, -0.25, 0.14, -0.21, 0.13, 12), Cream, "worn paint");
            k.Hull(Frustum(2, 0.0, 1.625, -0.21, 0.1, -0.18, 0.1, 12), Bulb, "", glow: true);
            for (var i = 0; i < 3; i++)
                k.Box(-0.12, 1.57 + i * 0.05, -0.18, 0.12, 1.585 + i * 0.05, -0.166, Cream, "worn paint");
            foreach (var x in new[] { -0.12, 0.12 })
                k.Box(x - 0.012, 1.615, -0.21, x + 0.012, 1.635, -0.198, Cream, "worn paint"); // (painted-over bolts)
            k.Box(0.06, 1.54, -0.18, 0.1, 1.57, -0.174, "#9a9c98", "rubber");
        }), 40);

        // ===== Switches and controls =====
        yield return Switch(F3("Toggle Plate", "a big ex-military toggle on a cream steel plate, a flip-up red guard with its spring gone so it's held up with a cable tie, a lamp beside it and a label punched on embossing tape.", [-0.1, 1.2, -0.25], [0.1, 1.45, -0.18], k =>
        {
            k.Box(-0.08, 1.22, -0.25, 0.08, 1.43, -0.235, Cream, "worn paint");
            foreach (var y in new[] { 1.24, 1.41 })
                foreach (var x in new[] { -0.06, 0.06 })
                    k.Box(x - 0.008, y - 0.008, -0.235, x + 0.008, y + 0.008, -0.226, Chrome, "steel");
            ToggleZ(k, 0.0, 1.31, -0.235, true);
            k.Hull([[-0.03, 1.36, -0.235], [0.03, 1.36, -0.235], [-0.03, 1.36, -0.22], [0.03, 1.36, -0.22], [-0.03, 1.42, -0.19], [0.03, 1.42, -0.19], [-0.03, 1.43, -0.2], [0.03, 1.43, -0.2]], Signal, "plastic");
            k.Box(0.032, 1.38, -0.215, 0.044, 1.41, -0.2, Ceramic, "plastic");
            PilotZ(k, 0.05, 1.28, -0.235, PilotGreen, 0.012);
            k.Box(-0.07, 1.235, -0.235, 0.0, 1.252, -0.229, Rubber, "plastic");
        }));
        yield return Switch(F3("Knife Switch", "a huge old knife switch on a slate board for the main circuit: copper blades gone green, a wooden handle polished by hands, a cardboard sign that says don't, and scorch marks where someone did.", [-0.2, 1.0, -0.25], [0.2, 1.6, -0.08], k =>
        {
            k.Box(-0.18, 1.02, -0.25, 0.18, 1.58, -0.22, "#3e4448", "plastic");
            foreach (var x in new[] { -0.08, 0.08 })
            {
                k.Box(x - 0.03, 1.12, -0.22, x + 0.03, 1.18, -0.18, Copper, "copper");
                k.Box(x - 0.03, 1.42, -0.22, x + 0.03, 1.47, -0.19, Copper, "copper");
                k.Hull([[x - 0.008, 1.15, -0.2], [x + 0.008, 1.15, -0.2], [x - 0.008, 1.16, -0.19], [x + 0.008, 1.16, -0.19], [x - 0.008, 1.43, -0.17], [x + 0.008, 1.43, -0.17], [x - 0.008, 1.44, -0.16], [x + 0.008, 1.44, -0.16]], "#5a8a6a", "copper");
            }
            Tube(k, 0, 1.44, -0.165, -0.1, 0.1, 0.01, Grime, "steel", 6);
            Tube(k, 2, 0.0, 1.44, -0.165, -0.09, 0.02, Wood, "wood", 8);
            k.Hull([[-0.15, 1.27, -0.22], [-0.02, 1.27, -0.22], [-0.15, 1.37, -0.22], [-0.02, 1.37, -0.22], [-0.15, 1.27, -0.21], [-0.02, 1.27, -0.21], [-0.15, 1.37, -0.21], [-0.02, 1.37, -0.21]], "#c9b88a", "fabric");
            k.Box(-0.12, 1.31, -0.209, -0.05, 1.33, -0.204, Signal, "worn paint");
            k.Hull([[0.05, 1.48, -0.22], [0.16, 1.5, -0.22], [0.12, 1.57, -0.22], [0.1, 1.53, -0.214]], Grime, "soot");
        }));
        yield return Switch(F3("Breaker Box", "a grey breaker box with its door off its top hinge, a row of breakers with one taped down, a handwritten list of what each one does (two crossed out), a big rotary isolator in red and yellow, and a cable gland stuffed with rag.", [-0.25, 1.0, -0.25], [0.25, 1.6, -0.1], k =>
        {
            k.Box(-0.22, 1.05, -0.25, 0.22, 1.55, -0.15, "#8a8d88", "worn paint");
            k.Box(-0.2, 1.08, -0.15, 0.2, 1.52, -0.145, Grime, "dirty steel");
            for (var i = 0; i < 6; i++)
            {
                var x = -0.17 + i * 0.05;
                k.Box(x, 1.35, -0.145, x + 0.04, 1.45, -0.13, Ceramic, "plastic");
                k.Box(x + 0.012, 1.4 - (i == 4 ? 0.03 : 0.0), -0.13, x + 0.028, 1.43 - (i == 4 ? 0.03 : 0.0), -0.115, Rubber, "plastic");
            }
            k.Box(0.02, 1.39, -0.118, 0.07, 1.42, -0.11, Tape, "fabric");
            k.Box(-0.17, 1.12, -0.145, 0.0, 1.3, -0.14, "#e8e0c8", "plastic");
            k.Box(0.05, 1.12, -0.145, 0.19, 1.28, -0.14, Mustard, "worn paint");
            k.Hull(Frustum(2, 0.12, 1.2, -0.14, 0.05, -0.12, 0.045, 10), Signal, "plastic");
            k.Box(0.1, 1.17, -0.12, 0.14, 1.23, -0.1, Signal, "plastic");
            k.Hull([[0.22, 1.05, -0.15], [0.24, 1.05, -0.12], [0.24, 1.55, -0.12], [0.22, 1.55, -0.15], [0.26, 1.3, -0.1]], "#8a8d88", "worn paint"); // (the hanging door)
            k.Hull(Frustum(1, -0.1, -0.2, 1.0, 0.03, 1.05, 0.03, 8), Grime, "plastic");
            k.Hull(Frustum(1, -0.1, -0.2, 0.96, 0.025, 1.0, 0.035, 6), Ceramic, "fabric");
        }));
        yield return Button(F3("Intercom", "a cream intercom box with a round speaker behind a grille whose bars run edge to edge across the whole circle, like the old sets had, a talk bar worn shiny, a little channel dial and someone's name scratched into the casing.", [-0.12, 1.2, -0.25], [0.12, 1.55, -0.12], k =>
        {
            k.Box(-0.1, 1.23, -0.25, 0.1, 1.53, -0.18, Cream, "worn paint");
            k.Hull(Frustum(2, 0.0, 1.43, -0.18, 0.075, -0.172, 0.075, 16), Grime, "dirty steel");
            k.Hull(Frustum(2, 0.0, 1.43, -0.18, 0.084, -0.168, 0.084, 16), Cream, "worn paint"); // (the rim the bars sit in)
            for (var i = 0; i < 7; i++)
            {
                var y = -0.063 + i * 0.021;
                var half = Math.Sqrt(0.075 * 0.075 - y * y);
                k.Box(-half, 1.43 + y - 0.0055, -0.172, half, 1.43 + y + 0.0055, -0.162, Chrome, "steel");
            }
            k.Box(-0.07, 1.26, -0.18, 0.07, 1.31, -0.158, "#d8d2c0", "chrome");
            k.Box(-0.05, 1.27, -0.158, 0.05, 1.3, -0.152, "#e8e4d8", "chrome"); // (worn shiny)
            k.Hull(Frustum(2, 0.075, 1.505, -0.18, 0.012, -0.165, 0.012, 6), Rubber, "plastic");
            k.Box(-0.08, 1.33, -0.18, -0.02, 1.338, -0.175, "#8a8070", "worn paint");
        }));

        // ===== Doors and lookouts (looks only for now) =====
        yield return F3("Periscope", "a periscope down through the ceiling: a thick tube in chipped grey with a band of rust where it meets the deck head, two fold-down handles wrapped in tape, a rubber eyecup split down one side, a training knob and a bearing ring with the numbers worn off.", [-0.25, 1.2, -0.25], [0.25, 2.4, 0.25], k =>
        {
            k.Box(-0.25, 2.36, -0.25, 0.25, 2.4, 0.25, Grime, "dirty steel");
            k.Hull(Frustum(1, 0, 0, 2.3, 0.14, 2.36, 0.14, 12), "#7a7d72", "rusty paint");
            Ring(k, 1, 0, 0, 2.27, 0.13, 0.03, Rust, "rust", 12);
            k.Hull(Frustum(1, 0, 0, 1.45, 0.1, 2.3, 0.1, 12), "#8a8d82", "worn paint");
            Ring(k, 1, 0, 0, 1.9, 0.112, 0.025, Brass, "brass", 12);
            k.Box(-0.13, 1.25, -0.11, 0.13, 1.5, 0.11, "#8a8d82", "worn paint");
            k.Hull(Frustum(2, 0.0, 1.38, 0.11, 0.05, 0.16, 0.045, 10), Grime, "steel");
            k.Hull(Frustum(2, 0.0, 1.38, 0.16, 0.05, 0.2, 0.06, 10), Rubber, "rubber");
            k.Box(-0.01, 1.34, 0.16, 0.012, 1.42, 0.204, Tape, "fabric"); // (the split)
            foreach (var sx in new[] { -1.0, 1.0 })
            {
                Tube(k, 0, 1.36, 0.0, sx > 0 ? 0.13 : -0.3, sx > 0 ? 0.3 : -0.13, 0.018, Grime, "steel", 6);
                Tube(k, 0, 1.36, 0.0, sx > 0 ? 0.2 : -0.29, sx > 0 ? 0.29 : -0.2, 0.024, Tape, "fabric", 6);
            }
            k.Hull(Frustum(0, 1.46, -0.05, 0.13, 0.03, 0.16, 0.03, 8), Signal, "plastic");
            k.Box(-0.13, 1.2, -0.11, 0.13, 1.25, 0.11, Grime, "steel");
        }, wall: false);
        yield return F3("Outer Airlock Door", "the bulkhead door's heavy cousin for the outer lock: a thick rusty frame, a slab dented at boot height with a patch welded over the worst of it, a real window with a wire-mesh glass pane and its frame bolted on, a hand wheel to dog it, rust running from the top hinge and hazard tape half peeled off.", [-0.6, 0, -0.15], [0.6, 2.2, 0.15], k =>
        {
            k.Box(-0.6, 0.0, -0.12, -0.48, 2.2, 0.12, Steel, "rusty paint");
            k.Box(0.48, 0.0, -0.12, 0.6, 2.2, 0.12, Steel, "rusty paint");
            k.Box(-0.48, 2.06, -0.12, 0.48, 2.2, 0.12, Steel, "rusty paint");
            k.Box(-0.48, 0.0, -0.12, 0.48, 0.1, 0.12, Grime, "dirty steel");
            k.Box(-0.47, 0.11, -0.06, 0.47, 2.05, 0.06, "#7a7d72", "worn paint");
            RibsZ(k, -0.4, 0.15, 0.4, 0.8, 0.06, 4, "#6e7166");
            PanelZ(k, -0.32, 0.92, 0.32, 1.28, 0.06, "#9a8a6a", "rusty paint");
            k.Hull([[-0.3, 0.2, 0.074], [0.0, 0.17, 0.074], [-0.04, 0.42, 0.074], [-0.27, 0.45, 0.074], [-0.14, 0.32, 0.088]], "#6e6a5e", "rusty paint"); // (the boot dent)
            k.Box(-0.2, 1.4, 0.06, 0.2, 1.88, 0.08, Grime, "steel"); // (the window's frame)
            k.Glass(-0.16, 1.44, 0.07, 0.16, 1.84, 0.075);
            for (var i = 0; i < 3; i++)
                k.Box(-0.16, 1.5 + i * 0.12, 0.08, 0.16, 1.508 + i * 0.12, 0.086, Grime, "steel");
            foreach (var x in new[] { -0.18, 0.18 })
                foreach (var y in new[] { 1.42, 1.86 })
                    k.Box(x - 0.01, y - 0.01, 0.08, x + 0.01, y + 0.01, 0.092, Chrome, "steel");
            foreach (var y in new[] { 0.35, 1.85 })
                k.Box(-0.5, y, 0.06, -0.42, y + 0.18, 0.09, Grime, "steel");
            StreakZ(k, -0.44, 1.2, 1.85, 0.09, 0.05);
            Tube(k, 2, 0.32, 1.1, 0.06, 0.12, 0.014, Chrome, "steel");
            Wheel(k, 2, 0.32, 1.1, 0.125, 0.09);
            foreach (var (x, y) in new[] { (-0.54, 0.6), (-0.54, 1.6), (0.54, 0.6), (0.54, 1.6) })
                k.Box(x - 0.035, y - 0.05, 0.12, x + 0.035, y + 0.05, 0.16, Chrome, "steel");
            k.Box(-0.47, 1.32, 0.061, -0.02, 1.37, 0.07, HazardYStr, "hazard paint");
            k.Hull([[-0.02, 1.32, 0.061], [0.08, 1.33, 0.061], [0.05, 1.37, 0.061], [-0.02, 1.37, 0.074]], HazardYStr, "hazard paint");
        }, wall: false);
        yield return F3("Floor Hatch", "a square floor hatch that's been walked on for decades: tread plate worn to a shine down the middle and rusty at the edges, a lifting ring folded into a pocket full of grit, a perished seal squashed out at one corner, a patch welded over a crack and oily boot prints.", [-0.45, 0, -0.45], [0.45, 0.08, 0.45], k =>
        {
            k.Box(-0.45, 0.0, -0.45, 0.45, 0.04, 0.45, Steel, "rusty paint");
            k.Box(-0.39, 0.04, -0.39, 0.39, 0.055, 0.39, "#6f6a60", "tread plate");
            k.Box(-0.18, 0.055, -0.39, 0.12, 0.059, 0.39, "#9a9a92", "tread plate"); // (worn shiny down the middle)
            foreach (var sx in new[] { -1.0, 1.0 })
                k.Box(sx * 0.39 - (sx > 0 ? 0.06 : 0), 0.055, -0.39, sx * 0.39 + (sx < 0 ? 0.06 : 0), 0.058, 0.39, Rust, "rust");
            k.Hull([[-0.45, 0.04, 0.38], [-0.3, 0.04, 0.45], [-0.45, 0.04, 0.45], [-0.45, 0.055, 0.42], [-0.38, 0.05, 0.45]], Rubber, "rubber"); // (the squashed seal)
            k.Box(0.25, 0.055, -0.1, 0.36, 0.066, 0.1, Grime, "dirt");
            Ring(k, 1, 0.305, 0.0, 0.07, 0.04, 0.012, Chrome, "steel", 6);
            k.Box(-0.3, 0.059, 0.1, -0.05, 0.07, 0.22, "#8a8d88", "steel"); // (the welded patch)
            foreach (var (x, z) in new[] { (0.0, -0.25), (-0.1, -0.05), (0.03, 0.15) })
                k.Box(x - 0.04, 0.059, z - 0.06, x + 0.04, 0.063, z + 0.06, Grime, "soot");
        }, wall: false);
    }

    /// <summary>The battery bank: a 1x1 module that stores power, a rack of mismatched car batteries.</summary>
    private static Part M3Battery()
    {
        const double H = 1.0;
        var k = K.Reactor("", 1, "Commoner battery bank (draft, set 3): a rack of six mismatched car batteries strapped into an angle-iron frame on the module plate, linked with jump leads and copper strap, a charge meter salvaged from a dashboard and a fan cable-tied on to cool them. It stores 4 MJ.", H, "Commoner Battery Bank", Section, ["Data 1"], role: "battery", group: Group);
        const double h = 0.5 - 0.124;
        Cage(k, -h, 0.08, -h, h, 0.92, h, Steel, 0.025);
        k.Box(-h + 0.02, 0.48, -h + 0.02, h - 0.02, 0.5, h - 0.02, Steel, "rusty paint");
        string[] cols = [Rubber, "#2f4a6a", Rubber, "#5a2a2a", "#3a3a3a", Rubber];
        for (var i = 0; i < 6; i++)
        {
            double x = -0.22 + (i % 3) * 0.22, y = i < 3 ? 0.11 : 0.5, hgt = 0.26 + (i % 2) * 0.04;
            k.Box(x - 0.09, y, -0.18, x + 0.09, y + hgt, 0.18, cols[i], "plastic");
            k.Hull(Frustum(1, x - 0.05, 0.1, y + hgt, 0.015, y + hgt + 0.03, 0.013, 6), Signal, "plastic");
            k.Hull(Frustum(1, x + 0.05, 0.1, y + hgt, 0.015, y + hgt + 0.03, 0.013, 6), Grime, "plastic");
        }
        Pipe(k, Signal, 0.008, "rubber", (-0.17, 0.43, 0.1), (-0.17, 0.46, 0.1), (0.05, 0.46, 0.1));
        k.Box(-0.2, 0.78, h + 0.004, 0.2, 0.9, h + 0.03, Grime, "plastic");
        DialZ(k, -0.1, 0.84, h + 0.03, 0.045, 0.3);
        k.Hull(Frustum(2, 0.12, 0.25, h + 0.004, 0.08, h + 0.04, 0.08, 10), "#4a4a4a", "plastic");
        k.P.Set(("battery", Kw(("capacity", 4000.0))),
            ("ports", Py.L(
                Kw(("name", "Power out"), ("type", "power"), ("dir", "out"), ("at", new object[] { 0.5, 0.5, 0 }), ("face", "+x")),
                Kw(("name", "Power in"), ("type", "power"), ("dir", "in"), ("noEffect", true), ("at", new object[] { -0.5, 0.5, 0 }), ("face", "-x")))));
        return k.P.Stats(260, 700, 3, 6.0);
    }
}
