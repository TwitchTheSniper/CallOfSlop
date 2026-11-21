using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// Body styles phase 8a (docs/vehiclekit/BODY-STYLES.md, section 16): the archetype library. An archetype is a body language with its proportions, taken from a
/// class of real vehicles as inspiration only (no maker's design, name or badge): its section profile, roofline, scale class, cab kind and height, belt, windscreen
/// rake, nose and hood length, the fastback of its cab's roof, its brows and tails and the ride it leans to. It is laid over the faction's own rule
/// (<see cref="BodyStyleRule"/>) for one vehicle, so the faction keeps its paint, trim, fittings, lining and props and the archetype gives the silhouette.
/// Which factions build an archetype, and how often, is data here (<see cref="Factions"/>); which shapes it may take is <see cref="Shapes"/>.
/// </summary>
public sealed record BodyArchetype(string Name, string Family, string Words, string[] Shapes, (string Style, double W)[] Factions, Func<BodyStyleRule, BodyStyleRule> Over)
{
    /// <summary>
    /// Phase 9a (docs/vehiclekit/BODY-STYLES.md, section 19): the archetype's own body plans for the shapes it takes (null: the recipe's), so each faction's car has its
    /// own outline (IT's long tail deck, say).
    /// </summary>
    public BodyPattern[]? Bodies { get; init; }

    private static readonly BodySegment Row = ShapeGrammar.Row, GunBay = ShapeGrammar.GunBay;
    private static BodySegment Boot(float a, float b) => ShapeGrammar.Boot(a, b);
    private static BodySegment Bed(float a, float b) => ShapeGrammar.Bed(a, b);

    private static HullLoft.Section S(params float[] xy) => new(Enumerable.Range(0, xy.Length / 2).Select(i => new Vector2(xy[2 * i], xy[2 * i + 1])).ToArray());

    // ---- The languages: each keeps a walk-in room's limits (x 1.0 m or more below 1.6 m, over 2.1 m at its top), so any of them can loft a room as well as a cab.

    /// <summary>An early long-hood fastback: a soft hip, a long tumblehome, the roof running down into the tail.</summary>
    public static readonly BodyLanguage Fastback = new("fastback", "an early fastback: a soft hip, a long tumblehome, the roof running down into the tail",
        S(1.02f, 0, 1.1f, 0.55f, 1.1f, 1.0f, 1.0f, 1.6f, 0.7f, 2.05f, 0, 2.16f), 2.1f, 2.24f, 0.62f, 0.75f, 0.55f, 0.7f, 0.92f, 0.86f)
        { Lines = [(Roofline.Fastback, 2), (Roofline.Sloped, 1)], FlareBlend = 0.45f };

    /// <summary>A late-sixties coke-bottle: a hip over the rear wheel, pinched at the belt, a pillared roof set in.</summary>
    public static readonly BodyLanguage Coke = new("coke", "a coke-bottle: a hip flare low, pinched at the belt, a narrow roof set in",
        S(1.0f, 0, 1.14f, 0.45f, 1.04f, 0.95f, 1.1f, 1.3f, 1.0f, 1.6f, 0.66f, 2.06f, 0, 2.14f), 2.1f, 2.22f, 0.62f, 0.75f, 0.6f, 0.75f, 0.92f, 0.88f)
        { Lines = [(Roofline.Fastback, 1), (Roofline.Crowned, 1)], FlareBlend = 0.4f };

    /// <summary>A seventies wedge: slab sides, a hard crease at the belt, a flat formal roof.</summary>
    public static readonly BodyLanguage Wedge = new("wedge", "a seventies wedge: slab sides, a crease at the belt, a flat formal roof",
        S(1.05f, 0, 1.08f, 0.9f, 1.06f, 1.65f, 0.86f, 2.12f, 0, 2.16f), 2.12f, 2.24f, 0.7f, 0.85f, 0.75f, 0.9f, 0.96f, 0.94f)
        { Lines = [(Roofline.Wedged, 2), (Roofline.Plain, 1)], FlareBlend = 0.05f };

    /// <summary>A mid-engined supercar: very wide and low, a strong tumblehome to a small canopy.</summary>
    public static readonly BodyLanguage SuperWedge = new("superwedge", "a supercar: wide at the sills, a strong tumblehome to a small canopy",
        S(1.0f, 0, 1.18f, 0.35f, 1.14f, 0.8f, 1.02f, 1.4f, 1.0f, 1.6f, 0.62f, 1.98f, 0, 2.12f), 2.1f, 2.2f, 0.55f, 0.66f, 0.6f, 0.72f, 0.88f, 0.86f)
        { Lines = [(Roofline.Fastback, 1.5), (Roofline.Tapered, 1)], FlareBlend = 0.6f };

    /// <summary>A European compact hatch: near-upright sides, a tall glasshouse, a short upright tail.</summary>
    public static readonly BodyLanguage Hatch = new("hatch", "a compact hatch: near-upright sides, a tall glasshouse, a short upright tail",
        S(1.02f, 0, 1.08f, 0.7f, 1.05f, 1.55f, 0.9f, 2.0f, 0.5f, 2.14f, 0, 2.17f), 2.14f, 2.26f, 0.62f, 0.78f, 0.82f, 0.94f, 0.94f, 0.95f)
        { Lines = [(Roofline.Plain, 1.5), (Roofline.Crowned, 1)], FlareBlend = 0.3f };

    /// <summary>A European estate: a long level roof, square shoulders, upright at the back.</summary>
    public static readonly BodyLanguage Estate = new("estate", "an estate: square shoulders under a long level roof, upright at the back",
        S(1.03f, 0, 1.07f, 0.8f, 1.05f, 1.62f, 0.94f, 2.08f, 0.55f, 2.18f, 0, 2.2f), 2.16f, 2.28f, 0.65f, 0.8f, 0.85f, 0.95f, 0.95f, 0.96f)
        { Lines = [(Roofline.Level, 2), (Roofline.Plain, 1)], FlareBlend = 0.2f };

    /// <summary>A European minibus: upright sides, a tall glazed band, a gently domed roof.</summary>
    public static readonly BodyLanguage Minibus = new("minibus", "a minibus: upright sides, a tall glazed band, a gently domed roof",
        S(1.02f, 0, 1.06f, 0.8f, 1.04f, 1.9f, 0.92f, 2.22f, 0.5f, 2.32f, 0, 2.34f), 2.28f, 2.4f, 0.62f, 0.78f, 0.8f, 0.92f, 0.95f, 0.95f)
        { Lines = [(Roofline.Level, 1.5), (Roofline.Crowned, 1)], FlareBlend = 0.3f };

    /// <summary>A European rigid truck's box body: flat panels, crisp corners, one height end to end.</summary>
    public static readonly BodyLanguage BoxBody = new("box body", "a rigid truck's box body: flat panels, crisp corners, one height end to end",
        S(1.07f, 0, 1.08f, 2.3f, 1.02f, 2.38f, 0, 2.4f), 2.34f, 2.5f, 0.85f, 0.95f, 0.88f, 0.97f, 0.99f, 0.99f)
        { Lines = [(Roofline.Level, 3), (Roofline.Plain, 0.5)], FlareBlend = 0.05f };

    /// <summary>A curtain-sider: the curtain bowed a little between a sole rail and a cant rail, a flat roof.</summary>
    public static readonly BodyLanguage Curtain = new("curtain", "a curtain-sider: the curtain bowed between a sole rail and a cant rail, a flat roof",
        S(1.06f, 0, 1.1f, 0.4f, 1.1f, 1.9f, 1.07f, 2.26f, 1.0f, 2.34f, 0, 2.36f), 2.3f, 2.46f, 0.85f, 0.95f, 0.88f, 0.97f, 0.99f, 0.99f)
        { Lines = [(Roofline.Level, 3), (Roofline.Plain, 0.5)], FlareBlend = 0.2f };

    /// <summary>A European cab-over: a flat upright face, square shoulders rounding into the roof (drawn on the cab; its rooms are the box's).</summary>
    public static readonly BodyLanguage CabOver = new("cab-over", "a European cab-over: a flat upright face, square shoulders rounding into the roof",
        S(1.04f, 0, 1.06f, 1.5f, 1.0f, 2.0f, 0.84f, 2.26f, 0, 2.3f), 2.24f, 2.36f, 0.8f, 0.92f, 0.8f, 0.92f, 0.97f, 0.97f)
        { Lines = [(Roofline.Level, 1)], FlareBlend = 0.1f };

    // ---- Phase 8b (section 18): buggies, monster trucks, fantasy wagons, concept cars.

    /// <summary>A sci-fi buggy's tub: a wide flat sill, the sides tucking hard in to a low canopy line (drawn on a caged cab).</summary>
    public static readonly BodyLanguage Tub = new("tub", "a buggy's tub: a wide flat sill, the sides tucked hard in to a low canopy line",
        S(1.12f, 0, 1.14f, 0.5f, 1.02f, 1.2f, 0.84f, 1.8f, 0.5f, 2.08f, 0, 2.12f), 2.1f, 2.2f, 0.55f, 0.7f, 0.6f, 0.75f, 0.9f, 0.88f)
        { Lines = [(Roofline.Wedged, 1.5), (Roofline.Tapered, 1)], FlareBlend = 0.1f };

    /// <summary>A pickup's cab, tall and upright: square shoulders, a flat glasshouse (the monster truck's cab, high over its wheels).</summary>
    public static readonly BodyLanguage Pickup = new("pickup", "a pickup's cab: square shoulders, a tall upright glasshouse, a flat roof",
        S(1.04f, 0, 1.07f, 1.0f, 1.03f, 1.55f, 0.84f, 2.06f, 0, 2.14f), 2.12f, 2.24f, 0.68f, 0.82f, 0.75f, 0.9f, 0.95f, 0.95f)
        { Lines = [(Roofline.Plain, 1.5), (Roofline.Level, 1)], FlareBlend = 0.15f };

    /// <summary>A war wagon's hooped top: low upright sides, a tall round canvas arch over them.</summary>
    public static readonly BodyLanguage Hoop = new("hoop", "a war wagon: low upright sides under a tall round arch of hoops",
        S(1.04f, 0, 1.06f, 1.15f, 1.02f, 1.6f, 0.84f, 2.0f, 0.48f, 2.3f, 0, 2.36f), 2.3f, 2.44f, 0.55f, 0.7f, 0.55f, 0.7f, 0.86f, 0.86f)
        { Lines = [(Roofline.Crowned, 2), (Roofline.Sagged, 1)], FlareBlend = 0.4f };

    /// <summary>An ornate carriage: a body bowed out at the waist, tucked in at the sill and the roof, a crowned top.</summary>
    public static readonly BodyLanguage Carriage = new("carriage", "a carriage: a body bowed out at the waist, tucked under and in, a crowned top",
        S(1.0f, 0, 1.12f, 0.55f, 1.16f, 1.1f, 1.08f, 1.7f, 0.92f, 2.1f, 0.6f, 2.28f, 0, 2.32f), 2.26f, 2.38f, 0.65f, 0.8f, 0.65f, 0.8f, 0.9f, 0.9f)
        { Lines = [(Roofline.Crowned, 2), (Roofline.Sheer, 1)], FlareBlend = 0.6f };

    /// <summary>A concept car's bubble: round from the sill to a dome canopy.</summary>
    public static readonly BodyLanguage Bubble = new("bubble", "a bubble car: round from the sill to a dome canopy",
        S(1.0f, 0, 1.12f, 0.4f, 1.14f, 0.9f, 1.04f, 1.4f, 0.8f, 1.85f, 0.42f, 2.1f, 0, 2.16f), 2.12f, 2.22f, 0.55f, 0.66f, 0.55f, 0.66f, 0.86f, 0.84f)
        { Lines = [(Roofline.Crowned, 1.5), (Roofline.Fastback, 1)], FlareBlend = 0.6f };

    /// <summary>A streamliner: a low teardrop, wide at the sills, a narrow single-seat canopy.</summary>
    public static readonly BodyLanguage Streamliner = new("streamliner", "a streamliner: a low teardrop, wide at the sills, a narrow canopy",
        S(1.0f, 0, 1.16f, 0.35f, 1.12f, 0.8f, 0.94f, 1.3f, 0.62f, 1.8f, 0.3f, 2.08f, 0, 2.12f), 2.1f, 2.18f, 0.5f, 0.6f, 0.5f, 0.6f, 0.86f, 0.84f)
        { Lines = [(Roofline.Fastback, 2), (Roofline.Tapered, 1)], FlareBlend = 0.6f };

    public static readonly BodyLanguage[] Languages = [Fastback, Coke, Wedge, SuperWedge, Hatch, Estate, Minibus, BoxBody, Curtain, CabOver, Tub, Pickup, Hoop, Carriage, Bubble, Streamliner];

    // ---- The archetypes (the user, 2026-11-07: muscle cars by era, supercars, European car lines, European trucks; real vehicles as inspiration only).

    private static readonly string[] Car = ["Car"], Rigid = ["Box Truck"], Bus = ["War Bus"];
    private static readonly string[] Buggy = ["Buggy"], Monster = ["Monster Truck"], Wagon = ["Wagon"], GiantTruck = ["Giant Truck"], Concept = ["Concept"];

    // (Every archetype was the IT style's; since its vehicles went (Karl, 2026-11-21) no style takes one: a new style names them in its weights.)
    public static readonly BodyArchetype[] All =
    [
        // Supercars: low, wide, cab-forward wedges.
        new("supercar", "supercar", "a mid-engined supercar: a cab-forward wedge, very low and wide, a raked screen", Car, [("IT", 2)],
            r => r with
            {
                Languages = [(SuperWedge, 1)], Mix = 0, Cabs = [("cab-over", 2), ("enclosed", 1)], Noses = [('w', 3)], Ride = (4, 1, 0),
                CabTallMin = 1.55f, CabTallMax = 1.6f, BeltMin = 0.9f, BeltMax = 0.94f, RakeMin = 0.24f, RakeMax = 0.24f, CapsuleRakeMin = 0.75f, CapsuleRakeMax = 0.8f,
                CabHalfMin = 0.94f, CabHalfMax = 0.95f, HoodHalfMin = 0.62f, HoodHalfMax = 0.64f, HoodCrownMin = 0.16f, HoodCrownMax = 0.17f, CabBackMin = 0.7f, CabBackMax = 0.9f,
            }) { Bodies = [new(1, Boot(2.5f, 3f))] }, // (phase 9a: a long tail deck behind two seats)
        // European trucks: a flat-fronted cab-over ahead of a box body, one height end to end.
        new("rigid box", "european truck", "a European rigid truck: a flat-fronted cab-over ahead of a crisp box body", Rigid, [("IT", 1.5)],
            r => r with
            {
                Languages = [(BoxBody, 1)], Mix = 0, FacetsMin = 0, FacetsMax = 0, Scales = [(BodyScale.Heavy, 1.5), (BodyScale.Standard, 1)], TallMin = 1.0f, TallMax = 1.1f, FlareShare = 0.15f,
                Brows = [(1f, 1)], Tails = [(0.75f, 1), (1f, 1)], Cabs = [("cab-over", 1)], CapsuleRakeMin = 0.12f, CapsuleRakeMax = 0.22f, Uppers = [("none", 1)], Cage = 0, Ride = (0.5, 2, 0.2),
                CabTallMin = 1.8f, CabTallMax = 1.9f, DoorRear = 0.6, CabLanguage = CabOver,
            }),
        // Phase 8b (section 18). Sci-fi open-top 4x4 gun buggies: a caged tub low between wheels on arms, the gun on a ring behind the cage.
        new("gun buggy", "buggy", "a sci-fi open-top 4x4 gun buggy: a caged tub, chunky wheels thrown out on arms, a gun on a ring behind the cage", Buggy, [("IT", 2)],
            r => r with
            {
                Languages = [(Tub, 1)], Mix = 0, Cabs = [("caged", 1)], Noses = [('w', 2), ('r', 1)], Ride = (3, 1, 0),
                CabTallMin = 1.58f, CabTallMax = 1.64f, BeltMin = 0.86f, BeltMax = 0.92f, RakeMin = 0.22f, RakeMax = 0.24f, CabHalfMin = 0.92f, CabHalfMax = 0.95f,
                HoodReach = -0.05f, HoodHalfMin = 0.56f, HoodHalfMax = 0.6f, HoodCrownMin = 0.14f, HoodCrownMax = 0.16f,
            }),
        // A one-off concept vehicle: a bubble-canopy car.
        new("bubble car", "concept", "a bubble-canopy concept car: round from sill to dome, a cab-forward capsule, a short tail", Concept, [("IT", 2)],
            r => r with
            {
                Languages = [(Bubble, 1)], Mix = 0, Cabs = [("cab-over", 2), ("enclosed", 1)], Noses = [('r', 2), ('w', 1)], Ride = (2, 1, 0),
                CabTallMin = 1.58f, CabTallMax = 1.66f, BeltMin = 0.84f, BeltMax = 0.9f, RakeMin = 0.24f, RakeMax = 0.24f, CapsuleRakeMin = 0.6f, CapsuleRakeMax = 0.7f,
                CabHalfMin = 0.9f, CabHalfMax = 0.94f, HoodHalfMin = 0.56f, HoodHalfMax = 0.6f, HoodCrownMin = 0.2f, HoodCrownMax = 0.24f, CabBackMin = 0.5f, CabBackMax = 0.7f,
            }),
        // A giant truck: the giant scale class over the faction's own languages (the class does the work: wider, taller, longer).
        new("giant hauler", "giant truck", "a giant truck: a land train's room at the giant scale class, a tall cab ahead", GiantTruck, [("IT", 1)],
            r => r with { Scales = [(BodyScale.Giant, 1)], Uppers = [("none", 1)], Balcony = 0, Ride = (0.2, 2, 0) }),
    ];

    /// <summary>The chance a vehicle of a shape takes one of its style's archetypes instead of the style's own body (a Car always does when its style has one).</summary>
    public static readonly Dictionary<string, double> Share = new(StringComparer.Ordinal) { ["Car"] = 1, ["Box Truck"] = 0.4, ["War Bus"] = 0.3,
        ["Buggy"] = 1, ["Monster Truck"] = 1, ["Wagon"] = 1, ["Giant Truck"] = 1, ["Concept"] = 1 }; // (phase 8b: its new shapes always take one)

    /// <summary>The archetypes a style builds for a shape, with their weights.</summary>
    public static (BodyArchetype A, double W)[] For(FactionStyle st, string shape) =>
        All.Where(a => a.Shapes.Contains(shape)).Select(a => (A: a, W: a.Factions.FirstOrDefault(f => f.Style == st.Name).W)).Where(x => x.W > 0).ToArray();

    /// <summary><c>--archetype NAME</c>: every vehicle of a shape it takes in that archetype, whatever its style (to look at one).</summary>
    public static BodyArchetype? Force;

    /// <summary><c>--no-archetypes</c>: none (the styles' own bodies, as before phase 8a), to measure against.</summary>
    public static bool None;

    /// <summary>The archetype laid over a style's rule, made once (the identity's and the cab's reference caches are keyed by the rule).</summary>
    public static BodyStyleRule RuleOf(BodyArchetype a, FactionStyle st)
    {
        lock (Rules)
        {
            if (!Rules.TryGetValue((a.Name, st.Name), out var r))
                Rules[(a.Name, st.Name)] = r = a.Over(BodyStyleRule.Of(st));
            return r;
        }
    }

    private static readonly Dictionary<(string, string), BodyStyleRule> Rules = new();

    public static BodyArchetype? Find(string name) => All.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A vehicle's archetype, on a fork of its own ("archetype"), so nothing else of a seed moves; null: the style's own body.</summary>
    public static BodyArchetype? Roll(Vehicle v, string shape)
    {
        if (None)
            return null;
        if (Force is { } f)
            return f.Shapes.Contains(shape) ? f : null;
        var list = For(v.Style, shape);
        if (list.Length == 0 || !Share.TryGetValue(shape, out var share))
            return null;
        var d = v.Dice.Fork("archetype");
        return d.Fork("take").Chance(share) ? BodyStyleRule.Weighted(d.Fork("which"), list) : null;
    }
}

public sealed partial class Vehicle
{
    /// <summary>Phase 8a: this vehicle's archetype (null: its style's own body).</summary>
    public BodyArchetype? Archetype { get; private set; }

    private BodyStyleRule? _rule;

    /// <summary>The body rule this vehicle is made by: its style's (<see cref="BodyStyleRule.Of"/>), with its archetype laid over it.</summary>
    public BodyStyleRule Rule => _rule ??= BodyStyleRule.Of(Style);

    /// <summary>Phase 9a: this vehicle's cab sub-style, its faction's cab family (null: none rolled).</summary>
    public CabStyle? CabStyle { get; private set; }

    /// <summary>Lays an archetype over the style's rule (before the look is rolled), then the faction's cab sub-style over both (phase 9a, its own fork "cab style").</summary>
    public void UseArchetype(BodyArchetype? a, string? shape = null)
    {
        Archetype = a;
        _rule = a == null ? BodyStyleRule.Of(Style) : BodyArchetype.RuleOf(a, Style);
        if (a != null)
            Modules.Add("archetype " + a.Name);
        var own = BodyStyleRule.Of(Style).CabStyles;
        if (own.Length == 0 || CabStyle.None || shape == null || CabStyle.Skip.Contains(shape))
            return;
        CabStyle = BodyStyleRule.Weighted(Dice.Fork("cab style"), own.Select(x => (x, x.W)).ToArray());
        _rule = CabStyle.RuleOf(_rule, a == null ? CabStyle.Over.Own : CabStyle.CarLike.Contains(shape) ? CabStyle.Over.Car : CabStyle.Over.Archetype);
        Modules.Add("cab style " + CabStyle.Name);
    }
}

/// <summary>
/// Phase 9a (docs/vehiclekit/BODY-STYLES.md, section 19): one of a faction's two or three cab sub-styles, its cab family. Laid over the vehicle's rule after its
/// archetype, so the faction's cab reads the same across its archetypes: on its own bodies a sub-style sets the cab's kinds, language and proportions; over a truck,
/// bus or wagon archetype the proportions and noses (the archetype keeps its kinds and language); over a car-like one (a car, a concept: its cab is its
/// silhouette) only the windscreen's rake and the noses. Data: <see cref="BodyStyleRule.CabStyles"/>.
/// </summary>
public sealed record CabStyle(string Name, double W, Func<BodyStyleRule, CabStyle.Over, BodyStyleRule> Apply)
{
    public enum Over { Own, Archetype, Car }

    /// <summary>The shapes whose cab is their body's silhouette.</summary>
    public static readonly string[] CarLike = ["Car", "Concept"];

    /// <summary>Shapes that take no sub-style: a buggy's roll cage is its archetype (an upright screen on its tub put two cage bars on one face plane).</summary>
    public static readonly string[] Skip = ["Buggy"];

    /// <summary><c>--no-cab-styles</c>: the rules as before phase 9a, to measure against.</summary>
    public static bool None;

    private static readonly Dictionary<(BodyStyleRule, string, Over), BodyStyleRule> Cache = new();

    /// <summary>The sub-style over a rule, made once (the identity's and the cab's reference caches are keyed by the rule).</summary>
    public BodyStyleRule RuleOf(BodyStyleRule r, Over how)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue((r, Name, how), out var made))
                Cache[(r, Name, how)] = made = Apply(r, how);
            return made;
        }
    }

    /// <summary>
    /// A sub-style from its parts: <paramref name="own"/> what it sets on the faction's own bodies only (kinds, language), <paramref name="face"/> the proportions any
    /// non-car body takes (belt, height, width), <paramref name="screen"/> the windscreen and noses every body takes.
    /// </summary>
    public static CabStyle Of(string name, double w, Func<BodyStyleRule, BodyStyleRule> screen, Func<BodyStyleRule, BodyStyleRule>? face = null, Func<BodyStyleRule, BodyStyleRule>? own = null) =>
        new(name, w, (r, how) =>
        {
            if (how == Over.Own && own != null)
                r = own(r);
            if (how != Over.Car && face != null)
                r = face(r);
            return screen(r);
        });
}
