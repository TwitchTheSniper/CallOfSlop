using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Ministry of Slop's parts: Constructs/Parts/British (docs/vehiclekit/VEHICLEKIT.md, "The British style"). Was tools/constructs/british_parts.py.
///
/// What a very British faction bolts on where bandits hang skulls, mostly understated: a Union Flag on a pole for the roofs, a Belisha
/// beacon and a rain gauge beside it, a teapot in its cosy, a traffic cone nobody will move and a garden gnome as trophies, furled
/// brollies down the flanks as spikes (it's that sort of day), queue posts on the beds (a queue forms behind them), and in the rooms a
/// pillar box and a red telephone box (loot in each) round a tea urn. Dressing stands on one "base" socket and points up its own +y, so
/// the kit turns it outward, as the bandit parts do (BanditParts.cs); furniture stands on feet on the floor's quarter grid, as
/// OfficeParts.cs's Server Rack does. Faces that meet are kept a few millimetres apart (CLAUDE.md, "No coplanar faces").
/// Recipes come from tools/constructs/assembling.py; run it after this one.
/// </summary>
public sealed class BritishParts : IPartGenerator
{
    public string Name => "british_parts";

    private const string Out = "Constructs/Parts/British";
    private const string Section = "Body panels";
    private const string Group = "Lights and Details";
    private const string Red = "#c8102e", RedDark = "#9a0c22", Blue = "#012169", White = "#f2f0e8";
    private const string Black = "#17181a", Iron = "#3a3532", Chrome = "#c9cbc8", Gold = "#c8a24a";
    private const string Amber = "#f39c12", Cone = "#f26a1b", Cream = "#e8dfc8", Knit = "#3c6e47";
    private const string GnomeBlue = "#2f5aa8", Skin = "#e2b48c", Beard = "#eeeae0", GlassBlue = "#7fb2d9";
    private const string Wood = "#6b4527";

    private static PyDict NoPaint => Kw(("paintable", false));

    private static void BaseSocket(Part p) => p.Sock("base", [0, 0, 0], "-y");

    /// <summary>A square post from the base up to top, under whatever it holds.</summary>
    private static void Post(Part p, double top, string colour = Iron) => p.Add(Box([-0.02, 0, -0.02], [0.02, top, 0.02], colour));

    /// <summary>A lamp: unlit, bright, not paint.</summary>
    private static void Lit(Part p, double[] mn, double[] mx, string colour, params (string, object?)[] shape)
    {
        var s = Kw(("paintable", false), ("glow", true));
        s.Update(shape);
        p.Add(Box(mn, mx, colour, detail: true, shape: s));
    }

    private static void Feet(Part p, double[] xs, double[] zs)
    {
        for (var i = 0; i < xs.Length; i++)
            for (var k = 0; k < zs.Length; k++)
                p.Sock($"foot{i}{k}", [xs[i], 0, zs[k]], "-y");
    }

    /// <summary>shape=..., segments=..., [taper=[t, t]], [front=...], [paintable=False], in that order.</summary>
    private static PyDict Rnd(string shape, int segments, string? front = null, double? taper = null, bool paint = true)
    {
        var d = Kw(("shape", shape), ("segments", segments));
        if (taper is { } t)
            d["taper"] = Py.L(t, t);
        if (front is not null)
            d["front"] = front;
        if (!paint)
            d["paintable"] = false;
        return d;
    }

    private static Part UnionFlag()
    {
        var p = new Part("Union Flag", Small, Section, "The Union Flag on a pole. It's been up there so long that nobody remembers which way up it goes.", color: Blue, group: Group);
        double r = 0.016, tall = 1.0;
        p.Add(Box([-0.05, 0, -0.04], [0.05, 0.04, 0.04], Iron)); // (its foot)
        p.Add(Box([-r, 0.04, -r], [r, tall, r], hidden: true));
        p.Add(Box([-r, 0.04, -r], [r, tall, r], Wood, detail: true, shape: Rnd("cylinder", 8, "+y")));
        double x0 = r + 0.004, x1 = 0.58, y0 = tall - 0.30, y1 = tall - 0.02;
        p.Add(Box([x0, y0, -0.006], [x1, y1, 0.006], Blue, detail: true)); // (the field)
        var steps = 6;
        double sw = (x1 - x0) / steps, sh = (y1 - y0) / steps;
        for (var k = 0; k < steps; k++) // (the diagonals, in steps: white 3 mm proud of the field, a thinner red 3 mm proud of the white)
        {
            var x = x0 + k * sw;
            foreach (var y in new[] { y0 + k * sh, y1 - (k + 1) * sh })
            {
                p.Add(Box([x, y, -0.009], [Math.Min(x1, x + sw * 1.1), Math.Min(y1, y + sh * 1.1), 0.009], White, detail: true, shape: NoPaint));
                p.Add(Box([x + sw * 0.3, y + sh * 0.3, -0.012], [x + sw * 0.8, y + sh * 0.8, 0.012], Red, detail: true, shape: NoPaint));
            }
        }
        double xm = (x0 + x1) / 2, ym = (y0 + y1) / 2;
        p.Add(Box([x0, ym - 0.047, -0.015], [x1, ym + 0.047, 0.015], White, detail: true, shape: NoPaint)); // (the white cross)
        p.Add(Box([xm - 0.047, y0, -0.015], [xm + 0.047, y1, 0.015], White, detail: true, shape: NoPaint));
        p.Add(Box([x0, ym - 0.028, -0.018], [x1, ym + 0.028, 0.018], Red, detail: true, shape: NoPaint)); // (the red cross on it)
        p.Add(Box([xm - 0.028, y0, -0.018], [xm + 0.028, y1, 0.018], Red, detail: true, shape: NoPaint));
        BaseSocket(p);
        p.Stats(2, 25, 0, 0.5);
        return p;
    }

    private static Part Teapot()
    {
        var p = new Part("Teapot", Small, Section, "A teapot in a knitted cosy, on a post. Still warm. Somehow it is always still warm.", color: Knit, group: Group);
        var top = 0.18;
        Post(p, top);
        p.Add(Box([-0.1, top, -0.1], [0.1, top + 0.012, 0.1], White, detail: true, shape: Rnd("cylinder", 12, "+y", paint: false))); // (its saucer)
        p.Add(Box([-0.09, top + 0.014, -0.09], [0.09, top + 0.17, 0.09], hidden: true));
        p.Add(Box([-0.1, top + 0.014, -0.1], [0.1, top + 0.18, 0.1], Knit, detail: true, shape: Rnd("sphere", 12))); // (the cosy)
        p.Add(Box([-0.05, top + 0.165, -0.05], [0.05, top + 0.2, 0.05], Cream, detail: true, shape: Rnd("cylinder", 10, "+y", paint: false))); // (the lid)
        p.Add(Box([-0.018, top + 0.2, -0.018], [0.018, top + 0.228, 0.018], Red, detail: true, shape: Rnd("sphere", 8, paint: false))); // (the pompom)
        p.Add(Box([-0.2, top + 0.06, -0.016], [-0.09, top + 0.09, 0.016], Cream, detail: true, shape: NoPaint)); // (the spout, then its upturn)
        p.Add(Box([-0.215, top + 0.07, -0.016], [-0.18, top + 0.15, 0.016], Cream, detail: true, shape: NoPaint));
        p.Add(Box([0.09, top + 0.05, -0.012], [0.17, top + 0.075, 0.012], Cream, detail: true, shape: NoPaint)); // (the handle: a loop of three)
        p.Add(Box([0.145, top + 0.077, -0.012], [0.17, top + 0.13, 0.012], Cream, detail: true, shape: NoPaint));
        p.Add(Box([0.09, top + 0.132, -0.012], [0.17, top + 0.155, 0.012], Cream, detail: true, shape: NoPaint));
        BaseSocket(p);
        p.Stats(2, 15, 0, 0.5);
        return p;
    }

    private static Part TrafficCone()
    {
        var p = new Part("Traffic Cone", Small, Section, "A traffic cone. Nobody remembers putting it there, and nobody will ever move it.", color: Cone, group: Group);
        p.Add(Box([-0.13, 0, -0.13], [0.13, 0.037, 0.13], Black)); // (its square foot)
        p.Add(Box([-0.1, 0.039, -0.1], [0.1, 0.5, 0.1], Cone, detail: true, shape: Rnd("cylinder", 12, "-y", 0.15)));
        p.Add(Box([-0.072, 0.2, -0.072], [0.072, 0.29, 0.072], White, detail: true, shape: Rnd("cylinder", 12, "-y", 0.78, false))); // (the reflective band, a hair proud)
        p.Add(Box([-0.05, 0.039, -0.05], [0.05, 0.45, 0.05], hidden: true));
        BaseSocket(p);
        p.Stats(1, 15, 0, 0.5);
        return p;
    }

    private static Part Gnome()
    {
        var p = new Part("Garden Gnome", Small, Section, "A garden gnome with a fishing rod. It has seen things. It will not be drawn on them.", color: GnomeBlue, group: Group);
        p.Add(Box([-0.08, 0, -0.08], [0.08, 0.037, 0.08], "#6f6a5e")); // (its plinth)
        p.Add(Box([-0.07, 0.039, -0.06], [0.07, 0.17, 0.06], GnomeBlue, detail: true, shape: Rnd("cylinder", 10, "+y", 0.8))); // (coat)
        p.Add(Box([-0.072, 0.13, -0.062], [0.072, 0.15, 0.062], Black, detail: true, shape: Rnd("cylinder", 10, "+y", paint: false))); // (belt)
        p.Add(Box([-0.05, 0.17, -0.05], [0.05, 0.25, 0.05], Skin, detail: true, shape: Rnd("sphere", 10, paint: false))); // (head)
        p.Add(Box([-0.045, 0.15, -0.068], [0.045, 0.215, -0.03], Beard, detail: true, shape: NoPaint)); // (beard)
        p.Add(Box([-0.054, 0.232, -0.054], [0.054, 0.38, 0.054], Red, detail: true, shape: Rnd("cylinder", 10, "-y", 0.05, false))); // (hat)
        p.Add(Box([0.07, 0.09, -0.012], [0.09, 0.11, 0.012], Skin, detail: true, shape: NoPaint)); // (hand, rod up out of it)
        p.Add(Box([0.074, 0.112, -0.006], [0.086, 0.42, 0.006], Wood, detail: true, shape: NoPaint));
        p.Add(Box([-0.05, 0.037, -0.05], [0.05, 0.36, 0.05], hidden: true));
        BaseSocket(p);
        p.Stats(2, 15, 0, 0.5);
        return p;
    }

    private static Part RainGauge()
    {
        var p = new Part("Rain Gauge", Small, Section, "A rain gauge on a post. Never empty. Worth mentioning to everyone you meet.", color: Iron, group: Group);
        var top = 0.5;
        Post(p, top);
        p.Add(Box([-0.06, top, -0.06], [0.06, top + 0.02, 0.06], Iron)); // (its bracket)
        p.Add(Box([-0.045, top + 0.022, -0.045], [0.045, top + 0.15, 0.045], GlassBlue, detail: true, shape: Rnd("cylinder", 10, "+y", paint: false))); // (the rain: always some)
        p.Add(Box([-0.05, top + 0.022, -0.05], [0.05, top + 0.26, 0.05], Chrome, detail: true, glass: true, shape: Rnd("cylinder", 10, "+y", paint: false))); // (the tube)
        p.Add(Box([-0.08, top + 0.262, -0.08], [0.08, top + 0.3, 0.08], Chrome, detail: true, shape: Rnd("cylinder", 10, "+y", 0.6, false))); // (the funnel)
        BaseSocket(p);
        p.Stats(1, 15, 0, 0.5);
        return p;
    }

    private static Part Brolly()
    {
        var p = new Part("Furled Brolly", Small, Section, "A furled umbrella, held out like a lance. You'll want it later: it's that sort of day.", color: Black, group: Group);
        p.Add(Box([-0.04, 0, -0.04], [0.04, 0.037, 0.04], Iron)); // (its clamp)
        p.Add(Box([-0.03, 0.039, -0.03], [0.03, 0.12, 0.03], Wood, detail: true, shape: NoPaint)); // (the crook's shank, then the hook)
        p.Add(Box([-0.03, 0.0, 0.03], [0.03, 0.03, 0.11], Wood, detail: true, shape: NoPaint));
        p.Add(Box([-0.045, 0.12, -0.045], [0.045, 0.62, 0.045], Black, detail: true, shape: Rnd("cylinder", 8, "-y", 0.3))); // (the furled canopy)
        p.Add(Box([-0.047, 0.4, -0.047], [0.047, 0.43, 0.047], Chrome, detail: true, shape: Rnd("cylinder", 8, "+y", paint: false))); // (its strap)
        p.Add(Box([-0.006, 0.62, -0.006], [0.006, 0.72, 0.006], Chrome, detail: true, shape: NoPaint)); // (the ferrule)
        p.Add(Box([-0.03, 0.12, -0.03], [0.03, 0.6, 0.03], hidden: true));
        BaseSocket(p);
        p.Stats(1, 20, 0, 0.5);
        return p;
    }

    private static Part Belisha()
    {
        var p = new Part("Belisha Beacon", Small, Section, "A Belisha beacon, blinking amber. Everybody stops for it. Everybody.", color: Amber, group: Group);
        var tall = 0.8;
        p.Add(Box([-0.05, 0, -0.05], [0.05, 0.037, 0.05], Iron));
        for (var k = 0; k < 6; k++) // (the pole, in black and white bands)
        {
            var y0 = 0.037 + k * (tall - 0.037) / 6;
            p.Add(Box([-0.022, y0, -0.022], [0.022, y0 + (tall - 0.037) / 6, 0.022], k % 2 == 0 ? Black : White, detail: k > 0, shape: NoPaint));
        }
        Lit(p, [-0.08, tall, -0.08], [0.08, tall + 0.16, 0.08], Amber, ("shape", "sphere"), ("segments", 10));
        BaseSocket(p);
        p.Stats(2, 20, 0, 0.5);
        return p;
    }

    private static Part QueuePost()
    {
        var p = new Part("Queue Post", Small, Section, "A queue post and a length of its belt. Wherever it stands, a queue forms behind it, politely.", color: Chrome, group: Group);
        p.Add(Box([-0.12, 0, -0.12], [0.12, 0.037, 0.12], Black, detail: true, shape: Rnd("cylinder", 12, "+y"))); // (its weighted base)
        p.Add(Box([-0.09, 0, -0.09], [0.09, 0.037, 0.09], hidden: true));
        p.Add(Box([-0.025, 0.037, -0.025], [0.025, 0.9, 0.025], Chrome));
        p.Add(Box([-0.04, 0.9, -0.04], [0.04, 0.95, 0.04], Chrome, detail: true)); // (the head the belt reels from)
        p.Add(Box([0.042, 0.86, -0.004], [0.36, 0.92, 0.004], "#2a3f8f", detail: true, shape: NoPaint)); // (the belt, pulled out)
        p.Add(Box([0.36, 0.84, -0.012], [0.39, 0.94, 0.012], Chrome, detail: true, shape: NoPaint)); // (its clip)
        BaseSocket(p);
        p.Stats(3, 20, 0, 0.5);
        return p;
    }

    private static Part PillarBox()
    {
        var p = new Part("Pillar Box", Small, "Furniture", "A pillar box. Last collection was some time ago. What's inside is loot, and the odd postcard.",
            role: "storage", tool: "wrench", color: Red, group: "Vehicle Body");
        double r = 0.26, tall = 1.1, wall = 0.04;
        p.Add(Box([-r - 0.01, 0, -r - 0.01], [r + 0.01, 0.08, r + 0.01], Black, detail: true, shape: Rnd("cylinder", 12, "+y"))); // (the black foot)
        p.Add(Box([-r, 0.082, -r], [r, tall - 0.12, r], Red, detail: true, shape: Rnd("cylinder", 12, "+y"))); // (the pillar)
        p.Add(Box([-r - 0.012, tall - 0.12, -r - 0.012], [r + 0.012, tall - 0.08, r + 0.012], RedDark, detail: true, shape: Rnd("cylinder", 12, "+y", paint: false))); // (the cap's rim)
        p.Add(Box([-r + 0.01, tall - 0.08, -r + 0.01], [r - 0.01, tall, r - 0.01], Red, detail: true, shape: Rnd("sphere", 12))); // (the dome)
        p.Add(Box([-0.11, tall - 0.27, -r - 0.012], [0.11, tall - 0.25, -r + 0.03], Black, detail: true, shape: NoPaint)); // (the slot)
        p.Add(Box([-0.07, tall - 0.42, -r - 0.008], [0.07, tall - 0.33, -r + 0.03], White, detail: true, shape: NoPaint)); // (the collection plate)
        p.Add(Box([-0.03, 0.55, -r - 0.008], [0.03, 0.62, -r + 0.03], Gold, detail: true, shape: NoPaint)); // (the door's lock)
        // Solid: its foot, its lid and four walls round the loot (a slot can't run into a solid box).
        var i = 0.9 * r;
        p.Add(Box([-i, 0, -i], [i, 0.08, i], hidden: true));
        p.Add(Box([-i, tall - 0.12, -i], [i, tall - 0.08, i], hidden: true));
        foreach (var s in new[] { -1, 1 })
        {
            double a = s * (i - wall), b = s * i;
            double lo = Math.Min(a, b), hi = Math.Max(a, b);
            p.Add(Box([lo, 0.082, -i], [hi, tall - 0.122, i], hidden: true));
            p.Add(Box([-i + wall + 0.002, 0.082, lo], [i - wall - 0.002, tall - 0.122, hi], hidden: true));
        }
        Feet(p, [-0.125, 0.125], [0.0]);
        p.Stats(60, 220, 3, 2.0);
        var k = i - wall - 0.006;
        p.Set(("storage", Kw(("slots", Py.L(
            Kw(("min", Py.L(-k, 0.088, -k)), ("max", Py.L(k, 0.5, k)), ("label", "post")),
            Kw(("min", Py.L(-k, 0.51, -k)), ("max", Py.L(k, tall - 0.128, k)), ("label", "post")))))));
        return p;
    }

    private static Part PhoneBox()
    {
        var p = new Part("Phone Box", Small, "Furniture", "A red telephone box, cut down to fit. The phone's been out of order for years; the shelf under it hasn't.",
            role: "storage", tool: "wrench", color: Red, group: "Vehicle Body");
        double w = 0.3, tall = 1.6, postW = 0.05;
        p.Add(Box([-w - 0.01, 0, -w - 0.01], [w + 0.01, 0.06, w + 0.01], RedDark)); // (the plinth)
        foreach (var x in new[] { -1, 1 }) // (the four corner posts)
            foreach (var z in new[] { -1, 1 })
                p.Add(Box([x * w - postW, 0.062, z * w - postW], [x * w + postW, tall - 0.16, z * w + postW], Red));
        // The glazing, each face a pane between the posts with its glazing bars; the back face is solid (it stands against a wall).
        (int, int)[] faces = [(0, -1), (1, 0), (-1, 0)];
        foreach (var (fx, fz) in faces)
        {
            if (fx == 0)
            {
                p.Add(Box([-w + postW + 0.002, 0.3, fz * w - 0.006], [w - postW - 0.002, tall - 0.2, fz * w + 0.006], GlassBlue, detail: true, glass: true, shape: NoPaint));
                p.Add(Box([-0.012, 0.3, fz * w - 0.01], [0.012, tall - 0.2, fz * w + 0.01], Red, detail: true, shape: NoPaint));
            }
            else
            {
                p.Add(Box([fx * w - 0.006, 0.3, -w + postW + 0.002], [fx * w + 0.006, tall - 0.2, w - postW - 0.002], GlassBlue, detail: true, glass: true, shape: NoPaint));
                p.Add(Box([fx * w - 0.01, 0.3, -0.012], [fx * w + 0.01, tall - 0.2, 0.012], Red, detail: true, shape: NoPaint));
            }
        }
        p.Add(Box([-w + postW + 0.002, 0.062, w - 0.03], [w - postW - 0.002, tall - 0.162, w + 0.002], Red)); // (the back)
        foreach (var (fx, fz) in faces) // (the kick panels under the glass)
        {
            if (fx == 0)
                p.Add(Box([-w + postW + 0.002, 0.062, fz * w - 0.01], [w - postW - 0.002, 0.298, fz * w + 0.01], Red));
            else
                p.Add(Box([fx * w - 0.01, 0.062, -w + postW + 0.002], [fx * w + 0.01, 0.298, w - postW - 0.002], Red));
        }
        p.Add(Box([-w - 0.02, tall - 0.16, -w - 0.02], [w + 0.02, tall - 0.06, w + 0.02], Red)); // (the roof's band, where the sign goes)
        p.Add(Box([-0.14, tall - 0.145, -w - 0.024], [0.14, tall - 0.075, -w - 0.018], White, detail: true, shape: NoPaint)); // (the sign, lit)
        p.Add(Box([-w + 0.02, tall - 0.058, -w + 0.02], [w - 0.02, tall, w - 0.02], Red, detail: true, shape: Rnd("sphere", 12))); // (the domed roof)
        p.Add(Box([-0.08, 0.95, w - 0.1], [0.08, 1.15, w - 0.032], Black, detail: true, shape: NoPaint)); // (the phone, on the back wall)
        p.Add(Box([-0.2, 0.62, w - 0.2], [0.2, 0.65, w - 0.032], Wood, detail: true, shape: NoPaint)); // (the shelf)
        p.Add(Box([-w, 0.0, -w], [w, 0.06, w], hidden: true));
        Feet(p, [-0.125, 0.125], [0.0]);
        p.Stats(80, 260, 3, 2.0);
        var k = w - postW - 0.01;
        p.Set(("storage", Kw(("slots", Py.L(Kw(("min", Py.L(-k, 0.07, -k)), ("max", Py.L(k, 0.6, Py.Round(w - 0.04, 3))), ("label", "under the shelf")))))));
        return p;
    }

    private static Part TeaUrn()
    {
        var p = new Part("Tea Urn", Small, "Furniture", "A tea urn on a trolley. When it runs dry, everything stops.", tool: "wrench", color: Chrome, group: "Vehicle Body");
        p.Add(Box([-0.25, 0, -0.2], [0.25, 0.04, 0.2], Iron)); // (the trolley's bottom shelf)
        foreach (var x in new[] { -0.23, 0.23 })
        {
            p.Add(Box([x - 0.015, 0.042, -0.18], [x + 0.015, 0.7, -0.15], Iron));
            p.Add(Box([x - 0.015, 0.042, 0.15], [x + 0.015, 0.7, 0.18], Iron));
        }
        p.Add(Box([-0.25, 0.702, -0.2], [0.25, 0.74, 0.2], Wood)); // (its top)
        p.Add(Box([-0.13, 0.742, -0.12], [0.13, 1.18, 0.12], Chrome, detail: true, shape: Rnd("cylinder", 12, "+y"))); // (the urn)
        p.Add(Box([-0.1, 1.182, -0.09], [0.1, 1.22, 0.09], Black, detail: true, shape: Rnd("cylinder", 12, "+y", paint: false))); // (its lid)
        p.Add(Box([-0.02, 0.84, -0.15], [0.02, 0.88, -0.11], Black, detail: true, shape: NoPaint)); // (the tap)
        Lit(p, [-0.01, 1.0, -0.126], [0.01, 1.02, -0.118], Red); // (the "on" light: always on)
        double[] cups = [-0.2, -0.14, 0.15];
        for (var k = 0; k < cups.Length; k++) // (cups, waiting)
        {
            var x = cups[k];
            p.Add(Box([x - 0.025, 0.742, 0.1 - k * 0.04], [x + 0.025, 0.8, 0.15 - k * 0.04], White, detail: true, shape: Rnd("cylinder", 8, "+y", paint: false)));
        }
        p.Add(Box([-0.25, 0.04, -0.2], [0.25, 0.74, 0.2], hidden: true));
        Feet(p, [-0.125, 0.125], [0.0]);
        p.Stats(25, 50, 0, 1.0);
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out,
    [
        ("", UnionFlag()), ("", Teapot()), ("", TrafficCone()), ("", Gnome()), ("", RainGauge()), ("", Brolly()), ("", Belisha()),
        ("", QueuePost()), ("", PillarBox()), ("", PhoneBox()), ("", TeaUrn()),
    ]);
}
