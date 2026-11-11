using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Walker legs (docs/sandbox/GIANTS.md): one all-in-one part a leg, for every kind and size tier, in Constructs/Parts/Walkers, and the
/// test walker, Constructs/Blueprints/TitanWalker.json. Was tools/constructs/leg_parts.py.
///
/// A leg part is its hip housing (one cell of its size class, the "mount" socket on its -x face, the hip's pivot at the cell's centre) and
/// a "leg" block: the kind, the segments (hip first), their width, the foot, what it carries and how fast it steps. The segments, knees,
/// ankle and foot are posed by the gait brain (Source/Constructs/ConstructWalk.cs) and drawn by Source/Render/ConstructRenderLegs.cs:
/// they are not boxes of the file.
/// </summary>
public sealed class LegParts : IPartGenerator
{
    public string Name => "leg_parts";

    private const string Out = "Constructs/Parts/Walkers";
    private const string Blueprint = "Constructs/Blueprints/TitanWalker.json";
    private const string Section = "Legs"; // (Constructs/Categories.json: Wheels and Suspension, Legs)

    // Size tiers: class, reach (m, hip to foot), the thickest segment (m), kg a leg carries, and the top speed (m/s) a six-legged walker on
    // electric legs walks at on the default setting (docs/sandbox/GIANTS.md, "Leg speeds"): bigger legs are faster, their strides longer.
    private static readonly (string Name, Cls C, double Reach, double Width, int Load, double Speed)[] Tiers =
    [
        ("Small", Small, 1.6, 0.12, 400, 10.0),
        ("Medium", Medium, 6.0, 0.5, 8_000, 12.0),
        ("Large", Large, 20.0, 2.0, 600_000, 14.0),
        ("Titan", Capital, 48.0, 4.5, 12_000_000, 16.0),
    ];

    // How far the body goes while a foot is planted, a share of the reach (ConstructWalk.cs, StretchSlow).
    private const double Stride = 0.95;

    // Kinds: segment shares of the reach (hip first), the hip's standing height and the foot's spread (shares of the reach), colours.
    private static readonly (string Name, string Key, double[] Shares, double Stand, double Spread, string Colour, string Joint)[] Kinds =
    [
        ("Mech", "mech", [0.42, 0.42, 0.16], 0.62, 0.3, "#5d6670", "#2a2d31"),
        ("Digitigrade", "digitigrade", [0.36, 0.38, 0.26], 0.6, 0.3, "#6b5f4f", "#2e2a25"),
        ("Insect", "insect", [0.14, 0.34, 0.38, 0.14], 0.45, 0.6, "#4f5d48", "#252a22"),
    ];

    // Strength on each source (phase 2 gates the legs on them).
    private static PyDict Power() => Kw(("hydraulic", 1.0), ("electric", 0.8), ("shaft", 1.2));

    /// <summary>Seconds a step takes in the air for a tripod on electric legs to walk at speed (ConstructWalk.cs: top = stride x reach / swing).</summary>
    private static double StepTime(double reach, double speed) => Py.Round(Stride * reach * 0.8 / speed, 3);

    // The gait profiles on the configure tool (docs/sandbox/GIANTS.md, phase 3).
    private const string Whole = "The walker follows its leg with the highest profile.";

    private static List<object?> Config() => Py.L(
        Kw(("key", "walkProfile"), ("label", "Gait profile"), ("group", "Gait"), ("type", "choice"), ("options", Py.L("Simple", "Basic", "Advanced")), ("default", 0),
            ("tooltip", "Simple: it just walks. Basic: the gait settings. Advanced: each leg's own tuning too. " + Whole)),
        Kw(("key", "walkSpeed"), ("label", "Walk speed"), ("group", "Gait"), ("unit", "%"), ("min", 0), ("max", 200), ("step", 5), ("default", 100),
            ("tooltip", "Top speed at full throttle, a share of the legs' own: 200 is twice as fast, with quicker steps, more power and a rougher ride. Every profile, Simple too.")),
        Kw(("key", "stride"), ("label", "Stride"), ("group", "Gait"), ("unit", "%"), ("min", 50), ("max", 150), ("step", 5), ("default", 100),
            ("tooltip", "How far each step reaches: longer strides walk faster. Basic and Advanced. " + Whole)),
        Kw(("key", "stepLift"), ("label", "Step height"), ("group", "Gait"), ("unit", "%"), ("min", 50), ("max", 250), ("step", 5), ("default", 100),
            ("tooltip", "How high the feet lift: higher clears bigger obstacles. Basic and Advanced. " + Whole)),
        Kw(("key", "bodyHeight"), ("label", "Body height"), ("group", "Gait"), ("unit", "%"), ("min", 60), ("max", 130), ("step", 5), ("default", 100),
            ("tooltip", "How high it stands (still no higher than the legs reach). Basic and Advanced. " + Whole)),
        Kw(("key", "gait"), ("label", "Gait"), ("group", "Gait"), ("type", "choice"), ("options", Py.L("Auto", "Tripod", "Wave")), ("default", 0),
            ("tooltip", "Auto: a tripod with six or more legs in an even count, a trot with four, else a wave. Tripod: half the legs at a time. Wave: one at a time. " + Whole)),
        Kw(("key", "legPace"), ("label", "Leg pace"), ("group", "This leg"), ("unit", "%"), ("min", 50), ("max", 150), ("step", 5), ("default", 100), ("advanced", true),
            ("tooltip", "This leg's step speed. Advanced profile only.")),
        Kw(("key", "legLift"), ("label", "Leg lift"), ("group", "This leg"), ("unit", "%"), ("min", 50), ("max", 200), ("step", 5), ("default", 100), ("advanced", true),
            ("tooltip", "How high this leg lifts its foot, on top of the step height. Advanced profile only.")),
        Kw(("key", "legSpread"), ("label", "Leg spread"), ("group", "This leg"), ("unit", "%"), ("min", 70), ("max", 130), ("step", 5), ("default", 100), ("advanced", true),
            ("tooltip", "How far out this leg puts its foot. Advanced profile only.")),
        Kw(("key", "legGroup"), ("label", "Gait group"), ("group", "This leg"), ("type", "choice"), ("options", Py.L("Auto", "First", "Second")), ("default", 0), ("advanced", true),
            ("tooltip", "In a tripod, which half this leg steps with. Advanced profile only.")));

    private static Part Leg((string Name, Cls C, double Reach, double Width, int Load, double Speed) t, (string Name, string Key, double[] Shares, double Stand, double Spread, string Colour, string Joint) k)
    {
        var c = t.C;
        var h = c.Half;
        var p = new Part($"{t.Name} {k.Name} Leg", c, Section, $"A {k.Name.ToLowerInvariant()} walker leg: hip, segments, knees, foot and actuators in one part. The gait brain walks it.",
            role: "leg", color: k.Colour, group: c == Small ? "Walkers" : null);
        p.Add(Box([-h, -h, -h], [h, h, h])); // the hip housing: one cell, solid
        var a = h * 0.55;
        p.Add(Box([h + 0.004 * c.X, -a, -a], [h + h * 0.3, a, a], k.Joint, detail: true)); // the hip actuator's cap, proud of the outer face
        p.Sock("mount", [-h, 0, 0], "-x");
        p.Sock("top", [0, h, 0], "+y");
        p.Sock("drive", [0, 0, -h], "-z"); // (a drive shaft's port: the leg walks on the engine)
        p.Set(("leg", Kw(
            ("type", k.Key), ("tier", t.Name.ToLowerInvariant()), ("segments", k.Shares.Select(s => (object?)R(t.Reach * s)).ToList()), ("width", R(t.Width)), ("foot", R(t.Width * 0.9)),
            ("hip", Py.L(0, 0, 0)), ("load", t.Load), ("stepTime", StepTime(t.Reach, t.Speed)), ("stride", Stride), ("stepHeight", 0.15), ("stand", k.Stand), ("spread", k.Spread),
            ("power", Power()), ("jointColor", k.Joint))));
        // Power (docs/sandbox/GIANTS.md, phase 2): a hose from a valve or a pump, a drive shaft, or the electric circuit through the leg's mount.
        p.Set(("ports", Py.L(
            Kw(("type", "pipe"), ("fluid", "oil"), ("name", "Oil in"), ("dir", "in"), ("at", Py.L(0, R(-h * 0.5), R(h))), ("face", "+z")),
            Kw(("name", "Drive in"), ("type", "shaft"), ("dir", "in"), ("socket", "drive")))));
        p.Set(("config", Config()));
        return p.Stats(8 * Math.Pow(t.Reach, 2.6), 300 * Math.Pow(t.Reach, 1.5), c.Armour);
    }

    /// <summary>
    /// The test walker: a bare 128 x 64 m hollow capital chassis on six titan mech legs, a helm on the deck at the bow, a Driving Module wired
    /// to the first leg, a battery bank that powers the legs and a 40 MW genset plant that charges it (docs/sandbox/GIANTS.md, INTERIORS.md).
    /// </summary>
    private static PyDict TitanWalker()
    {
        var steps = Py.L(Kw(("part", "Capital Hollow Chassis 128x64 m")));
        foreach (var side in new[] { "-z", "+z" })
            foreach (var i in new[] { 0, 4, 7 }) // (the deck's side sockets: cells 0, 4 and 7 along it)
                steps.Add(Kw(("part", "Titan Mech Leg"), ("on", 0), ("socket", $"{side}{i}"), ("by", "mount")));
        steps.AddRange([
            Kw(("part", "Adapter Capital to Small"), ("on", 0), ("socket", "top0.0"), ("by", "back")), // (the helm on the deck at the bow)
            Kw(("part", "Bridge Helm"), ("on", 7), ("socket", "front2.2"), ("by", "-y"), ("spin", 1)),
            Kw(("part", "Driving Module"), ("on", 8), ("socket", "+x"), ("by", "-x")),
            Kw(("part", "Battery"), ("on", 9), ("socket", "+x"), ("by", "-x")), // (the helm's own)
            Kw(("part", "Capital Battery Bank"), ("on", 0), ("socket", "top7.0"), ("by", "-y")), // (the legs draw their work from it)
            // The plant (step 12 on): a Large Fuel Bunker on an adapter at the stern's other corner, a Large Genset on either end of it.
            Kw(("part", "Adapter Capital to Large"), ("on", 0), ("socket", "top7.3"), ("by", "back")),
            Kw(("part", "Large Fuel Bunker"), ("on", 12), ("socket", "front2.2"), ("by", "-y")),
            Kw(("part", "Large Genset"), ("on", 13), ("socket", "+x"), ("by", "-x")),
            Kw(("part", "Large Genset"), ("on", 13), ("socket", "-x"), ("by", "+x")),
        ]);
        return Kw(("format", "call-of-slop-construct-blueprint/1"), ("name", "Titan Walker"), ("group", "Walkers"),
            ("description", "A bare 128 m hollow walking chassis on six titan legs (docs/sandbox/GIANTS.md). The helm walks it: W/S, A/D turn, Q/E strafe, Sprint/Crouch height."),
            ("steps", steps), ("wires", Py.L(Py.L(8, 9), Py.L(9, 1))));
    }

    public void Make(PartOutput o)
    {
        WriteParts(o, Out, Tiers.SelectMany(t => Kinds.Select(k => ("", Leg(t, k)))));
        o.File(Blueprint, PyJson.Dumps(TitanWalker(), 2) + "\n");
    }
}
