using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Street pieces: Constructs/Parts/Street (docs/vehiclekit/VEHICLEKIT.md, "The Mercenary style").
///
/// A traffic cone nobody will move and a garden gnome, as trophies. Dressing stands on one "base" socket and points up its own +y, so
/// the kit turns it outward, as the raider parts do (ScrapParts.cs). Faces that meet are kept a few millimetres apart (CLAUDE.md, "No coplanar faces").
/// Recipes come from the part kit's assembling (--gen-parts assembling); run it after this one.
/// </summary>
public sealed class StreetParts : IPartGenerator
{
    public string Name => "street_parts";

    private const string Out = "Constructs/Parts/Street";
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

    public void Make(PartOutput o) => WriteParts(o, Out,
    [
        ("", TrafficCone()), ("", Gnome()),
    ]);
}
