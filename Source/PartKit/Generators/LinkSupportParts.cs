using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Supports that carry links up and over an aisle (docs/sandbox/design/conveyor-inclines.md section 6, part kit generator <c>link_supports</c>,
/// in Constructs/Parts/LinkSupports): Trestles (two legs and a cross-beam), Trestle Posts (one post and a short beam, for a link along a wall)
/// and Link Bridges (a deck truss from one trestle or wall to the next). Each top is a saddle: a <c>tray</c> block whose lanes a node snaps to
/// (ConstructTrays), so a chute or a conveyor laid over one sits on its centreline. They are structure only: what a link carries and how
/// fast is the same on a support as anywhere (the user, 2026-11-17). A lane's line is 0.12 m over the top, where a conveyor's under-plate
/// clears the beam by 3 cm and a chute's duct rests on it.
/// </summary>
public sealed class LinkSupportParts : IPartGenerator
{
    public string Name => "link_supports";

    private const string Out = "Constructs/Parts/LinkSupports";
    private const string Frame = "#3e5a70", Beam = "#2f3a44", Saddle = "#8d969e", Hazard = "#f2b01e", Plate = "#24282c";
    private const double LaneUp = 0.12, BeamH = 0.2, BeamD = 0.3, Leg = 0.12, Pitch = 0.8;

    /// <summary>The heights a trestle comes in (its beam's top over its feet): every half metre to 5 m, then every metre to 8 m.</summary>
    public static readonly double[] Heights = [1.0, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0, 4.5, 5.0, 6.0, 7.0, 8.0];

    /// <summary>The spans a Link Bridge comes in.</summary>
    public static readonly double[] Spans = [4.0, 6.0, 8.0];

    private static string Metres(double h) => h == Math.Floor(h) ? $"{h:0}" : $"{h:0.0}";

    /// <summary>A saddle's lip each side of every lane on a beam's top (look boxes), a few mm proud of it.</summary>
    private static void Lips(Part p, double top, int lanes, double depth)
    {
        for (var k = 0; k < lanes; k++)
        {
            var mid = (k - (lanes - 1) * 0.5) * Pitch;
            foreach (var e in new[] { -1, 1 })
            {
                var x = mid + e * (Pitch * 0.5 - 0.04);
                p.Add(Box([x - 0.015, top - 0.002, -depth / 2 + 0.006], [x + 0.015, top + 0.035, depth / 2 - 0.006], Saddle, detail: true));
            }
        }
    }

    /// <summary>The saddle's lanes: across the beam (x), along the link (z), at the lane line over the top.</summary>
    private static void Lanes(Part p, double top, int lanes, double length)
    {
        p.Set(("collider", "boxes"), ("tray", Kw(("at", Py.L(0, top + LaneUp, 0)), ("axis", Py.L(0, 0, 1)), ("across", Py.L(1, 0, 0)), ("lanes", lanes),
            ("pitch", Pitch), ("length", length), ("guard", length))));
    }

    /// <summary>A Trestle <paramref name="h"/> m: two square steel legs, a tie between them past 2 m, a cross-beam two lanes wide on top.</summary>
    private static Part Trestle(double h)
    {
        const int lanes = 2;
        var w = lanes * Pitch + 0.2;
        var x = w / 2 - Leg / 2 - 0.02;
        var p = new Part($"Trestle {Metres(h)} m", Small, "Link supports",
            $"Two legs and a beam {Metres(h)} m up: lay a chute or conveyor over its saddle (two lanes) to carry it over an aisle.", color: Frame, group: "Storage and Logistics");
        var legTop = h - BeamH - 0.004;
        foreach (var e in new[] { -1, 1 })
        {
            p.Add(Box([e * x - Leg / 2, 0, -Leg / 2], [e * x + Leg / 2, legTop, Leg / 2])); // a leg
            p.Add(Box([e * x - 0.14, 0.003, -0.14], [e * x + 0.14, 0.025, 0.14], Plate, detail: true)); // its foot plate
            if (h >= 2.0)
                p.Add(Box([e * x - Leg / 2 - 0.004, 1.7, -Leg / 2 - 0.004], [e * x + Leg / 2 + 0.004, 1.9, Leg / 2 + 0.004], Hazard, detail: true)); // a band where a head meets it
        }
        if (h >= 2.0)
            p.Add(Box([-x + Leg / 2, h * 0.45 - 0.04, -0.04], [x - Leg / 2, h * 0.45 + 0.04, 0.04], Frame)); // the tie
        p.Add(Box([-w / 2, h - BeamH, -BeamD / 2], [w / 2, h, BeamD / 2], Beam)); // the beam
        Lips(p, h, lanes, BeamD);
        p.Sock("foot", [0, 0, 0], "-y");
        p.Sock("top", [0, h, 0], "+y");
        p.Stats(h * 2 * 0.0045 * 7850 + w * 0.006 * 7850, 1200 + (int)(h * 100), 10);
        Lanes(p, h, lanes, BeamD);
        return p;
    }

    /// <summary>A Trestle Post <paramref name="h"/> m: one post and a beam one lane wide, for a link along a wall or a single run.</summary>
    private static Part Post(double h)
    {
        var w = Pitch + 0.1;
        var p = new Part($"Trestle Post {Metres(h)} m", Small, "Link supports",
            $"One post and a short beam {Metres(h)} m up: lay a chute or conveyor over its saddle (one lane).", color: Frame, group: "Storage and Logistics");
        const double post = 0.14;
        p.Add(Box([-post / 2, 0, -post / 2], [post / 2, h - BeamH - 0.004, post / 2])); // the post
        p.Add(Box([-0.16, 0.003, -0.16], [0.16, 0.025, 0.16], Plate, detail: true)); // the foot plate
        if (h >= 2.0)
            p.Add(Box([-post / 2 - 0.004, 1.7, -post / 2 - 0.004], [post / 2 + 0.004, 1.9, post / 2 + 0.004], Hazard, detail: true));
        p.Add(Box([-w / 2, h - BeamH, -BeamD / 2], [w / 2, h, BeamD / 2], Beam)); // the beam
        Lips(p, h, 1, BeamD);
        p.Sock("foot", [0, 0, 0], "-y");
        p.Sock("top", [0, h, 0], "+y");
        p.Stats(h * 0.0055 * 7850 + w * 0.006 * 7850, 900 + (int)(h * 80), 8);
        Lanes(p, h, 1, BeamD);
        return p;
    }

    /// <summary>
    /// A Link Bridge <paramref name="span"/> m: a deck between two bottom chords, a side truss each side (top chord, posts every metre) and a
    /// hazard band under each end's chord, four lanes on the deck. A seat under each end stands it on a trestle's top or a wall.
    /// </summary>
    private static Part Bridge(double span)
    {
        const int lanes = 4;
        double w = lanes * Pitch + 0.2, chord = 0.12, deckLo = 0.14, deckHi = 0.18, rail = 1.0;
        var p = new Part($"Link Bridge {span:0} m", Small, "Link supports",
            $"A {span:0} m truss with a deck for four links: stand it on two trestles or two walls to carry links over a lane.", color: Frame, group: "Storage and Logistics");
        var x = w / 2;
        foreach (var e in new[] { -1, 1 })
        {
            var (lo, hi) = e < 0 ? (-x, -x + chord) : (x - chord, x);
            p.Add(Box([lo, 0, 0], [hi, chord + 0.04, span])); // the bottom chord
            p.Add(Box([lo, rail - chord, 0.004], [hi, rail, span - 0.004])); // the top chord
            for (var z = 0.0; z <= span + 1e-6; z += 1.0)
            {
                var z0 = Math.Clamp(z - 0.04, 0.008, span - 0.088);
                p.Add(Box([lo + 0.01, chord + 0.044, z0], [hi - 0.01, rail - chord - 0.004, z0 + 0.08], Frame)); // a post
            }
            foreach (var z in new[] { 0.3, span - 0.6 })
                p.Add(Box([lo - 0.004, 0.02, z], [hi + 0.004, chord + 0.02, z + 0.3], Hazard, detail: true)); // a band at the end, where a vehicle's roof passes under
        }
        p.Add(Box([-x + chord + 0.004, deckLo, 0.006], [x - chord - 0.004, deckHi, span - 0.006], Beam)); // the deck
        p.Sock("seatA", [0, 0, 0.15], "-y");
        p.Sock("seatB", [0, 0, span - 0.15], "-y");
        // (the lanes run the bridge's length: its own z)
        foreach (var k in Enumerable.Range(0, lanes))
        {
            var mid = (k - (lanes - 1) * 0.5) * Pitch;
            foreach (var e in new[] { -1, 1 })
            {
                var lx = mid + e * (Pitch * 0.5 - 0.04);
                if (Math.Abs(lx) < x - chord - 0.02)
                    p.Add(Box([lx - 0.012, deckHi - 0.002, 0.03], [lx + 0.012, deckHi + 0.025, span - 0.03], Saddle, detail: true)); // a lane's guide strip
            }
        }
        p.Stats(span * (2 * 0.004 + 0.012) * 7850, 2500 + (int)(span * 150), 20);
        p.Set(("collider", "boxes"), ("tray", Kw(("at", Py.L(0, deckHi + LaneUp, span / 2)), ("axis", Py.L(0, 0, 1)), ("across", Py.L(1, 0, 0)), ("lanes", lanes),
            ("pitch", Pitch), ("length", span), ("guard", span))));
        return p;
    }

    public void Make(PartOutput o)
    {
        var parts = new List<(string, Part)>();
        foreach (var h in Heights)
        {
            parts.Add(("", Trestle(h)));
            parts.Add(("", Post(h)));
        }
        foreach (var s in Spans)
            parts.Add(("", Bridge(s)));
        WriteParts(o, Out, [.. parts]);
    }
}
